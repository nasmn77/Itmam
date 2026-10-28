using System.Drawing.Imaging;
using Itmam.App.Tray;

// Writes a multi-size .ico (PNG-compressed entries, supported since Windows Vista).
//   dotnet run --project tools/IconGenerator -- src/Itmam.App/Assets/Itmam.ico
if (args is not [var output])
{
    Console.Error.WriteLine("Usage: IconGenerator <output.ico>");
    return 2;
}

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var images = sizes.Select(size =>
{
    using var bitmap = AppIconArt.Render(size);
    using var png = new MemoryStream();
    bitmap.Save(png, ImageFormat.Png);
    return (Size: size, Data: png.ToArray());
}).ToArray();

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
using var file = File.Create(output);
using var writer = new BinaryWriter(file);

// ICONDIR
writer.Write((ushort)0);            // reserved
writer.Write((ushort)1);            // type: icon
writer.Write((ushort)images.Length);

// ICONDIRENTRY per image, then the PNG data
var offset = 6 + (16 * images.Length);
foreach (var (size, data) in images)
{
    writer.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
    writer.Write((byte)(size >= 256 ? 0 : size));
    writer.Write((byte)0);           // palette colors
    writer.Write((byte)0);           // reserved
    writer.Write((ushort)1);         // color planes
    writer.Write((ushort)32);        // bits per pixel
    writer.Write(data.Length);
    writer.Write(offset);
    offset += data.Length;
}

foreach (var (_, data) in images)
{
    writer.Write(data);
}

Console.WriteLine($"Wrote {output} ({string.Join(", ", sizes)} px)");
return 0;
