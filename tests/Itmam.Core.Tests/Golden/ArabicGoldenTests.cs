using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Itmam.Core.Dictionary;
using Itmam.Core.Dictionary.Building;
using Itmam.Core.Input;
using Itmam.Core.Learning;
using Itmam.Core.Suggestions;
using Itmam.Core.Text;

namespace Itmam.Core.Tests.Golden;

/// <summary>
/// Snapshots of every Arabic text path, taken before the language layer of 2.0. Refactoring for other
/// languages must leave all of them unchanged.
/// </summary>
public class ArabicGoldenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] HandPicked =
    [
        "ان", "انا", "الى", "اذا", "مدرسه", "مكتبه", "اعتق", "مرح", "شكر", "سءال", "مسؤ", "مسئ", "قراء",
        "وال", "والمد", "بالت", "فالك", "كالم", "للم", "سيك", "وسي", "بمد", "لمد", "فمد", "كمد",
        "كَت", "كتـا", "آل", "أن", "إن", "ٱل", "على", "علي", "الي", "مستشفى", "مستشفي", "جميله",
    ];

    [Theory]
    [InlineData("suggestions-basic.txt", false)]
    [InlineData("suggestions-wikipedia.txt", true)]
    public void Dictionary_suggestions_are_unchanged(string file, bool wikipedia)
    {
        var dictionary = DictionaryFile.Load(wikipedia ? TestDictionaries.WikipediaDictionaryPath : TestDictionaries.ShippedDictionaryPath);
        var user = LearnedWords(dictionary);
        var engine = new SuggestionEngine(
            dictionary,
            user,
            new SuggestionOptions { MinChars = 1, MaxSuggestions = 6 },
            new FixedTimeProvider(Now));

        GoldenFile.Verify(file, Prefixes(dictionary).Select(prefix =>
            prefix + "\t" + string.Join(" | ", engine.Suggest(prefix).Select(s =>
                $"{s.Word} {s.Kind} {s.Score.ToString("F4", CultureInfo.InvariantCulture)}"))));
    }

    [Fact]
    public void Normalization_and_cleaning_are_unchanged()
    {
        var dictionary = DictionaryFile.Load(TestDictionaries.WikipediaDictionaryPath);
        var words = Enumerable.Range(0, dictionary.Count)
            .Where(i => i % 211 == 0)
            .Select(i => dictionary.GetDisplay(i).ToString())
            .Concat(HandPicked)
            .Concat(["كَتَبَ", "مـــدرسة", "abc", "كتاب1", "ك", "", "  ", "كتاب جميل", "سؤال", "شئون", "ﻻ"]);

        GoldenFile.Verify("normalization.txt", words.Select(w => string.Join('\t',
            w,
            ArabicNormalizer.Normalize(w),
            string.Join(',', ArabicNormalizer.HamzaFallbackVariants(ArabicNormalizer.Normalize(w))),
            ArabicText.StripIgnorable(w),
            ArabicText.IsArabicWord(w),
            UserWordIndex.Clean(w) ?? "<null>",
            Blocklist.LooseKey(w),
            TextShortcuts.CleanKey(w) ?? "<null>",
            PhraseIndex.Clean(w) ?? "<null>")));
    }

    [Fact]
    public void Blocklist_matching_is_unchanged()
    {
        var blocklist = Blocklist.Load(TestDictionaries.ShippedBlocklistPath);
        var entries = File.ReadAllLines(TestDictionaries.ShippedBlocklistPath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToArray();
        string[] clitics = ["", "و", "ف", "ب", "ك", "ل", "ال", "وال", "بال", "لل"];
        string[] suffixes = ["", "ه", "ها", "هم", "ك", "ات"];

        var probes = entries
            .SelectMany(e => clitics.SelectMany(c => suffixes.Select(s => c + e + s)))
            .Concat(HandPicked)
            .ToArray();
        var bits = string.Concat(probes.Select(p => blocklist.IsBlocked(p) ? '1' : '0'));

        // Hashed so that the blocked words themselves are not written out again.
        GoldenFile.Verify("blocklist.txt",
        [
            $"probes {probes.Length}",
            $"blocked {bits.Count(b => b == '1')}",
            $"sha256 {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bits)))}",
        ]);
    }

    [Fact]
    public void Phrase_suggestions_are_unchanged()
    {
        PhraseIndex shipped;
        using (var reader = File.OpenText(TestDictionaries.ShippedPhrasesPath))
        {
            shipped = PhraseIndex.Read(reader);
        }

        var index = shipped.With(["نفيدكم علما", "نفيدكم علما بأن الموضوع", "مع خالص التحية والتقدير"]);
        var lines = new List<string>();
        foreach (var phrase in index.Phrases)
        {
            for (var k = 0; k < phrase.Words.Count; k++)
            {
                var context = phrase.Keys.Take(k).ToArray();
                var word = phrase.Words[k];
                IEnumerable<string> typed = k == 0 ? [] : [""];
                foreach (var t in typed.Concat(Enumerable.Range(1, Math.Min(3, word.Length)).Select(n => word[..n])))
                {
                    var sources = new Dictionary<string, string>();
                    var result = index.Suggest(context, t, 4, sources);
                    lines.Add($"{string.Join(' ', context)}\t{t}\t{string.Join(" | ", result.Select(r => $"{r} <{sources.GetValueOrDefault(r)}>"))}");
                }
            }
        }

        GoldenFile.Verify("phrases.txt", lines.Distinct());
    }

    [Fact]
    public void Corrections_are_unchanged()
    {
        Corrections corrections;
        using (var reader = File.OpenText(TestDictionaries.ShippedCorrectionsPath))
        {
            corrections = Corrections.Read(reader);
        }

        var keys = File.ReadAllLines(TestDictionaries.ShippedCorrectionsPath)
            .Where(l => l.Contains('=') && !l.TrimStart().StartsWith('#'))
            .Select(l => l[..l.IndexOf('=')].Trim())
            .SelectMany(k => new[] { k, k + "َ", "و" + k, k.Length > 1 ? k[..^1] : k })
            .Concat(HandPicked);

        GoldenFile.Verify("corrections.txt", keys.Select(k => $"{k}\t{string.Join(" | ", corrections.Find(k))}"));
    }

    [Fact]
    public void Word_buffer_behaviour_is_unchanged()
    {
        string[][] scenarios =
        [
            ["t:كتاب جميل", "t:."],
            ["t:ع1 ", "t:1", "t:كتاب1 "],
            ["t:كَتـب", "bs", "bs", "t:ب،"],
            ["t:ذهب الط", "cbs", "t:مدر", "nav", "t:سة"],
            ["t:abc", "t:كتب", "t:x", "t:يب"],
            ["t:مد", "click", "t:ر", "ctx:ذهب الطالب إلى المدر", "t:سة "],
            ["bs", "t:رسة جميلة", "resumeBlind"],
            ["bs", "resumeEmpty", "t:قلم"],
            ["t:قل", "accept:قلم+", "bs", "restore:قل", "t:م؟"],
            ["t:قل", "esc", "t:م", "dismiss", "t:ي"],
            ["t:سلام", "paste", "t:عليكم", "focus", "t:و", "layout", "t:رحمة", "shortcut", "t:الله"],
            ["t:كتاب", "sel", "t:ب", "reset", "t:ﻻ", "t:\u200Fكتب\r"],
        ];

        var lines = new List<string>();
        foreach (var scenario in scenarios)
        {
            var buffer = new WordBuffer();
            lines.Add("---");
            foreach (var step in scenario)
            {
                var result = Run(buffer, step);
                var recovered = string.Join(',', buffer.RecoveredWords.Select(w => w.Word + (w.EndedBySpace ? "_" : "")));
                lines.Add($"{step.Replace("\r", "\\r")}\t{result.Outcome} [{result.CommittedWord}] text={buffer.Text} letters={buffer.LetterCount} " +
                          $"detached={buffer.IsDetached} blind={buffer.HasBlindTyping} recovered={recovered}");
            }
        }

        GoldenFile.Verify("word-buffer.txt", lines);
    }

    private static BufferResult Run(WordBuffer buffer, string step)
    {
        var (name, arg) = step.IndexOf(':') is var i and >= 0 ? (step[..i], step[(i + 1)..]) : (step, "");
        switch (name)
        {
            case "t":
                var last = BufferResult.None;
                foreach (var c in arg)
                {
                    last = buffer.Apply(InputEvent.Char(c));
                }

                return last;
            case "accept":
                return buffer.AcceptWord(arg.TrimEnd('+'), appendedSpace: arg.EndsWith('+'));
            case "restore":
                return buffer.Restore(arg);
            case "ctx":
                return buffer.ResumeFromContext(arg, 49);
            case "resumeBlind":
                return buffer.ResumeFromBlind();
            case "resumeEmpty":
                return buffer.ResumeEmpty();
            case "reset":
                return buffer.Reset();
            default:
                var kind = name switch
                {
                    "bs" => InputEventKind.Backspace,
                    "cbs" => InputEventKind.CtrlBackspace,
                    "nav" => InputEventKind.Navigation,
                    "sel" => InputEventKind.Selection,
                    "esc" => InputEventKind.Escape,
                    "dismiss" => InputEventKind.DismissSuggestions,
                    "paste" => InputEventKind.Paste,
                    "focus" => InputEventKind.FocusChanged,
                    "click" => InputEventKind.MouseClick,
                    "layout" => InputEventKind.LayoutChanged,
                    "shortcut" => InputEventKind.Shortcut,
                    _ => throw new ArgumentException(step),
                };
                return buffer.Apply(InputEvent.Of(kind));
        }
    }

    /// <summary>A few learned words (some sharing a prefix with dictionary words) so user ranking is pinned too.</summary>
    private static UserWordIndex LearnedWords(WordDictionary dictionary)
    {
        var user = new UserWordIndex();
        for (var i = 0; i < 40; i++)
        {
            var word = dictionary.GetDisplay(i * 9973 % dictionary.Count).ToString();
            user.Set(word, 1 + i % 7, Now.AddDays(-i * 3));
        }

        user.Set("مدرستنا", 9, Now.AddDays(-1));
        user.Set("إتمام", 5, Now);
        user.Set("مستخدمي", 3, Now.AddDays(-40));
        user.SetBlocked("مدرسة", blocked: true, Now);
        return user;
    }

    /// <summary>Every 1–2 letter literal prefix in the dictionary, longer prefixes of a sample, and hand-picked ones.</summary>
    private static IEnumerable<string> Prefixes(WordDictionary dictionary)
    {
        var shortOnes = Enumerable.Range(0, dictionary.Count)
            .SelectMany(i => Enumerable.Range(1, Math.Min(2, dictionary.GetDisplay(i).Length)).Select(n => dictionary.GetDisplay(i)[..n].ToString()));
        var sampled = Enumerable.Range(0, dictionary.Count)
            .Where(i => i % 1999 == 0)
            .SelectMany(i => Enumerable.Range(3, Math.Max(0, Math.Min(6, dictionary.GetDisplay(i).Length) - 2)).Select(n => dictionary.GetDisplay(i)[..n].ToString()));

        return shortOnes.Concat(sampled).Concat(HandPicked).Distinct().Order(StringComparer.Ordinal);
    }
}
