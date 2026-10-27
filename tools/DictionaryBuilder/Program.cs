using System.Diagnostics;
using System.Globalization;
using System.Text;
using Itmam.Core.Dictionary;
using Itmam.Core.Dictionary.Building;

// Builds the shipped dictionary from local frequency lists. Never touches the network:
// download the sources first with download-sources.ps1 (see data/README.md).
//
//   DictionaryBuilder --source frequencywords=data/raw/frequencywords-ar_full.txt
//                     [--weight frequencywords=1.0] [--min-count frequencywords=5]
//                     [--blocklist data/blocklist.txt] [--max-words 600000]
//                     --output data/itmam-ar.dict

// Attribution for every source the builder accepts; unknown source names are rejected so the
// shipped file can never contain unattributed data.
var knownSources = new Dictionary<string, DictionarySource>(StringComparer.OrdinalIgnoreCase)
{
    ["frequencywords"] = new(
        "FrequencyWords (OpenSubtitles 2018) by Hermit Dave",
        "CC-BY-SA-4.0",
        "https://github.com/hermitdave/FrequencyWords"),
};

try
{
    var options = Options.Parse(args);
    if (options.Sources.Count == 0 || options.Output is null)
    {
        Console.Error.WriteLine("Usage: DictionaryBuilder --source <name>=<path> [--weight <name>=<w>] [--min-count <name>=<n>] [--blocklist <path>] [--max-words <n>] --output <path>");
        Console.Error.WriteLine($"Known sources: {string.Join(", ", knownSources.Keys)}");
        return 2;
    }

    var stopwatch = Stopwatch.StartNew();
    var readers = new List<StreamReader>();
    var inputs = new List<SourceInput>();
    foreach (var (name, path) in options.Sources)
    {
        if (!knownSources.TryGetValue(name, out var source))
        {
            throw new ArgumentException($"Unknown source '{name}'. Add its attribution to knownSources first.");
        }

        var reader = new StreamReader(path, Encoding.UTF8);
        readers.Add(reader);
        inputs.Add(new SourceInput(
            source,
            FrequencyList.Read(reader),
            options.Weights.GetValueOrDefault(name, 1.0),
            options.MinCounts.GetValueOrDefault(name, 1)));
    }

    var blocklist = Blocklist.Empty;
    if (options.Blocklist is { } blocklistPath)
    {
        using var reader = new StreamReader(blocklistPath, Encoding.UTF8);
        blocklist = Blocklist.Read(reader);
    }

    var (dictionary, stats) = DictionaryBuildPipeline.Build(inputs, blocklist, options.MaxWords);
    readers.ForEach(r => r.Dispose());

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.Output))!);
    using (var output = File.Create(options.Output))
    {
        DictionaryFile.Write(output, dictionary);
    }

    Console.WriteLine($"Source tokens:   {stats.SourceTokens:N0}");
    Console.WriteLine($"Below min count: {stats.BelowMinCount:N0}");
    Console.WriteLine($"Rejected (junk): {stats.Rejected:N0}");
    Console.WriteLine($"Blocked:         {stats.Blocked:N0} (blocklist has {blocklist.Count:N0} keys)");
    Console.WriteLine($"Accepted:        {stats.Accepted:N0}");
    Console.WriteLine($"Entries:         {stats.Entries:N0}");
    Console.WriteLine($"Output:          {options.Output} ({new FileInfo(options.Output).Length:N0} bytes) in {stopwatch.Elapsed.TotalSeconds:F1}s");
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or FormatException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

internal sealed class Options
{
    public List<(string Name, string Path)> Sources { get; } = [];

    public Dictionary<string, double> Weights { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, long> MinCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Blocklist { get; private set; }

    public string? Output { get; private set; }

    public int? MaxWords { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"Missing value for {args[i]}.");
            switch (args[i])
            {
                case "--source":
                    var (name, path) = SplitPair(value);
                    options.Sources.Add((name, path));
                    break;
                case "--weight":
                    var (weightName, weight) = SplitPair(value);
                    options.Weights[weightName] = double.Parse(weight, CultureInfo.InvariantCulture);
                    break;
                case "--min-count":
                    var (countName, count) = SplitPair(value);
                    options.MinCounts[countName] = long.Parse(count, CultureInfo.InvariantCulture);
                    break;
                case "--blocklist":
                    options.Blocklist = value;
                    break;
                case "--max-words":
                    options.MaxWords = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--output":
                    options.Output = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown option {args[i]}.");
            }

            i++;
        }

        return options;
    }

    private static (string Name, string Value) SplitPair(string pair)
    {
        var separator = pair.IndexOf('=');
        return separator > 0
            ? (pair[..separator], pair[(separator + 1)..])
            : throw new ArgumentException($"Expected <name>=<value>, got '{pair}'.");
    }
}
