using System.IO.Compression;
using System.Text;

namespace Itmam.Core.Dictionary;

/// <summary>
/// Reads and writes the shipped dictionary file.
/// </summary>
/// <remarks>
/// Layout: magic "ITMD", format version (uint16), then a Brotli stream containing
/// <list type="bullet">
/// <item>source count, then per source: name, license, url (length-prefixed UTF-8)</item>
/// <item>entry count, then per entry in key order:
///   shared-prefix length with the previous key, suffix length, suffix chars;
///   display length + 1 followed by its chars, or 0 when the display equals the key;
///   frequency in <see cref="FrequencyScale"/> units.</item>
/// </list>
/// All integers are 7-bit varints. Characters are stored as <c>varint(c ^ 0x0600)</c> so Arabic
/// letters (U+0600–U+067F) take one byte each before compression.
/// </remarks>
public static class DictionaryFile
{
    private static ReadOnlySpan<byte> Magic => "ITMD"u8;
    private const ushort FormatVersion = 1;
    private const int ArabicBlock = 0x0600;

    public static void Write(Stream output, WordDictionary dictionary)
    {
        output.Write(Magic);
        Span<byte> version = [(byte)FormatVersion, (byte)(FormatVersion >> 8)];
        output.Write(version);

        using var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
        using var writer = new BinaryWriter(brotli, Encoding.UTF8, leaveOpen: true);

        writer.Write7BitEncodedInt(dictionary.Sources.Count);
        foreach (var source in dictionary.Sources)
        {
            writer.Write(source.Name);
            writer.Write(source.License);
            writer.Write(source.Url);
        }

        writer.Write7BitEncodedInt(dictionary.Count);
        var previous = ReadOnlySpan<char>.Empty;
        for (var i = 0; i < dictionary.Count; i++)
        {
            var key = dictionary.GetKey(i);
            var shared = key.CommonPrefixLength(previous);
            writer.Write7BitEncodedInt(shared);
            WriteChars(writer, key[shared..]);

            var display = dictionary.GetDisplay(i);
            if (display.SequenceEqual(key))
            {
                writer.Write7BitEncodedInt(0);
            }
            else
            {
                writer.Write7BitEncodedInt(display.Length + 1);
                foreach (var c in display)
                {
                    writer.Write7BitEncodedInt(c ^ ArabicBlock);
                }
            }

            writer.Write7BitEncodedInt64(dictionary.GetFrequency(i));
            previous = key;
        }
    }

    public static WordDictionary Read(Stream input)
    {
        Span<byte> header = stackalloc byte[6];
        input.ReadExactly(header);
        if (!header[..4].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not an Itmam dictionary file.");
        }

        var version = header[4] | (header[5] << 8);
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported dictionary format version {version}.");
        }

        // Decompress fully, then parse from memory: much faster than many small stream reads.
        using var decompressed = new MemoryStream();
        using (var brotli = new BrotliStream(input, CompressionMode.Decompress, leaveOpen: true))
        {
            brotli.CopyTo(decompressed);
        }

        var reader = new SpanReader(decompressed.GetBuffer().AsSpan(0, (int)decompressed.Length));

        var sourceCount = reader.ReadCount();
        var sources = new DictionarySource[sourceCount];
        for (var i = 0; i < sourceCount; i++)
        {
            sources[i] = new DictionarySource(reader.ReadString(), reader.ReadString(), reader.ReadString());
        }

        var count = reader.ReadCount();
        var builder = new WordDictionary.Builder(count);
        Span<char> key = stackalloc char[256];
        Span<char> display = stackalloc char[256];
        var keyLength = 0;
        for (var i = 0; i < count; i++)
        {
            var shared = reader.ReadCount();
            var suffixLength = reader.ReadCount();
            if (shared > keyLength || shared + suffixLength > key.Length)
            {
                throw new InvalidDataException("Corrupt dictionary entry.");
            }

            for (var j = 0; j < suffixLength; j++)
            {
                key[shared + j] = reader.ReadChar();
            }

            keyLength = shared + suffixLength;

            var displayMarker = reader.ReadCount();
            ReadOnlySpan<char> displaySpan = key[..keyLength];
            if (displayMarker > 0)
            {
                var displayLength = displayMarker - 1;
                if (displayLength > display.Length)
                {
                    throw new InvalidDataException("Corrupt dictionary entry.");
                }

                for (var j = 0; j < displayLength; j++)
                {
                    display[j] = reader.ReadChar();
                }

                displaySpan = display[..displayLength];
            }

            builder.Add(key[..keyLength], displaySpan, (uint)reader.ReadVarint());
        }

        return builder.ToDictionary(sources);
    }

    public static WordDictionary Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    private static void WriteChars(BinaryWriter writer, ReadOnlySpan<char> chars)
    {
        writer.Write7BitEncodedInt(chars.Length);
        foreach (var c in chars)
        {
            writer.Write7BitEncodedInt(c ^ ArabicBlock);
        }
    }

    private ref struct SpanReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public ulong ReadVarint()
        {
            ulong result = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                if (_position >= _data.Length)
                {
                    throw new InvalidDataException("Unexpected end of dictionary data.");
                }

                var b = _data[_position++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }
            }

            throw new InvalidDataException("Invalid varint in dictionary data.");
        }

        public int ReadCount()
        {
            var value = ReadVarint();
            return value <= int.MaxValue ? (int)value : throw new InvalidDataException("Count out of range.");
        }

        public char ReadChar() => (char)((int)ReadVarint() ^ ArabicBlock);

        public string ReadString()
        {
            var length = ReadCount();
            if (length > _data.Length - _position)
            {
                throw new InvalidDataException("Unexpected end of dictionary data.");
            }

            var value = Encoding.UTF8.GetString(_data.Slice(_position, length));
            _position += length;
            return value;
        }
    }
}
