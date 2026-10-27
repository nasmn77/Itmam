using Itmam.Core.Dictionary;
using Itmam.Core.Learning;
using Itmam.Core.Text;

namespace Itmam.Core.Suggestions;

/// <summary>
/// How a suggestion matched the typed prefix. Literal and normalized matches compete on score
/// (literal ones get <see cref="SuggestionOptions.LiteralMatchBonus"/>); hamza-fallback and
/// clitic matches always rank below them.
/// </summary>
public enum MatchKind
{
    /// <summary>The word starts with exactly the typed letters.</summary>
    Literal = 0,

    /// <summary>Matches only after normalization (e.g. typed ا, word starts with أ).</summary>
    Normalized = 1,

    /// <summary>Matches after swapping a hamza seat (ء ↔ ؤ ↔ ئ).</summary>
    HamzaFallback = 2,

    /// <summary>An attached prefix (و ف ب ك ل ال …) was stripped, the stem completed and the prefix re-attached.</summary>
    Clitic = 3,
}

public readonly record struct Suggestion(string Word, MatchKind Kind, double Score);

/// <summary>
/// Produces ranked completions for the word being typed from the shipped dictionary and the user's
/// learned words. Not thread-safe (shares <see cref="UserWordIndex"/> with the learning code).
/// </summary>
public sealed class SuggestionEngine(
    WordDictionary dictionary,
    UserWordIndex userWords,
    SuggestionOptions? options = null,
    TimeProvider? timeProvider = null)
{
    // Already normalized. Longest first; every matching clitic is tried and results are merged.
    private static readonly string[] Clitics = ["وال", "بال", "فال", "كال", "لل", "ال", "و", "ف", "ب", "ك", "ل", "س"];

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public SuggestionOptions Options { get; set; } = options ?? new SuggestionOptions();

    public IReadOnlyList<Suggestion> Suggest(string typed)
    {
        var literal = ArabicText.StripIgnorable(typed);
        if (literal.Length < Options.MinChars || !ArabicText.IsArabicWord(literal))
        {
            return [];
        }

        var key = ArabicNormalizer.Normalize(literal);
        var context = new Context(literal, _time.GetUtcNow());

        Collect(context, literal, key, MatchKind.Literal, MatchKind.Normalized, attachedPrefix: "", skipStemsWithAl: false);

        if (context.Results.Count < Options.MaxSuggestions)
        {
            foreach (var variant in ArabicNormalizer.HamzaFallbackVariants(key))
            {
                Collect(context, literal, variant, MatchKind.HamzaFallback, MatchKind.HamzaFallback, "", skipStemsWithAl: false);
            }
        }

        if (Options.CliticAware && context.Results.Count < Options.MaxSuggestions)
        {
            foreach (var clitic in Clitics)
            {
                if (!key.StartsWith(clitic, StringComparison.Ordinal) || key.Length - clitic.Length < Options.MinStemChars)
                {
                    continue;
                }

                // ل + ال is written لل, so stems starting with ال never follow a clitic ending in ل.
                Collect(
                    context,
                    literal[clitic.Length..],
                    key[clitic.Length..],
                    MatchKind.Clitic,
                    MatchKind.Clitic,
                    attachedPrefix: literal[..clitic.Length],
                    skipStemsWithAl: clitic[^1] == 'ل');
            }
        }

        return context.Results.Values
            .OrderBy(s => Tier(s.Kind))
            .ThenByDescending(s => s.Score)
            .ThenBy(s => s.Word, StringComparer.Ordinal)
            .Take(Options.MaxSuggestions)
            .ToArray();
    }

    private static int Tier(MatchKind kind) => kind <= MatchKind.Normalized ? 0 : (int)kind;

    private double KindBonus(MatchKind kind) => kind == MatchKind.Literal ? Options.LiteralMatchBonus : 0;

    private void Collect(
        Context context,
        string literalPrefix,
        string keyPrefix,
        MatchKind literalKind,
        MatchKind otherKind,
        string attachedPrefix,
        bool skipStemsWithAl)
    {
        // 1. Best dictionary entries in the prefix range by (tier, base score + bonuses).
        var pool = new CandidatePool(Options.CandidatePoolSize);
        var literalBonus = (float)KindBonus(literalKind);
        var otherBonus = (float)KindBonus(otherKind);
        var completeWordBonus = (float)Options.CompleteWordBonus;

        void Consider(int i)
        {
            var display = dictionary.GetDisplay(i);
            var isLiteral = display.StartsWith(literalPrefix);
            var kind = isLiteral ? literalKind : otherKind;
            var score = dictionary.GetBaseScore(i) + (isLiteral ? literalBonus : otherBonus);
            if (dictionary.GetKey(i).Length == keyPrefix.Length)
            {
                score += completeWordBonus;
            }

            if (!pool.WouldAccept(Tier(kind), score)
                || pool.Contains(i)
                || display.SequenceEqual(literalPrefix)
                || (skipStemsWithAl && display.StartsWith("ال"))
                || userWords.IsBlocked(display))
            {
                return;
            }

            pool.Add(i, kind, Tier(kind), score);
        }

        var (start, end) = dictionary.FindPrefixRange(keyPrefix);
        if (Options.CandidatePoolSize <= PrefixCandidateCache.CandidatesPerPrefix
            && dictionary.Cache.TryGet(keyPrefix, literalPrefix, out var byKey, out var byLiteral))
        {
            // Entries whose key equals the typed key (complete-word bonus) sort first in the range.
            for (var i = start; i < end && dictionary.GetKey(i).Length == keyPrefix.Length; i++)
            {
                Consider(i);
            }

            foreach (var i in byLiteral)
            {
                Consider(i);
            }

            foreach (var i in byKey)
            {
                Consider(i);
            }
        }
        else
        {
            for (var i = start; i < end; i++)
            {
                Consider(i);
            }
        }

        foreach (var (index, kind, _, score) in pool.Items)
        {
            var word = dictionary.GetDisplay(index).ToString();
            var user = userWords.Get(word);
            var userScore = user is null ? 0 : Ranking.UserScore(user.Count, user.LastUsed, context.Now, Options);
            context.Add(attachedPrefix + word, kind, score + userScore);
        }

        // 2. Learned words with the same prefix (they may be outside the pool or not in the dictionary).
        foreach (var user in userWords.FindByPrefix(keyPrefix))
        {
            if (user.IsBlocked
                || user.Word == literalPrefix
                || (skipStemsWithAl && user.Word.StartsWith("ال", StringComparison.Ordinal)))
            {
                continue;
            }

            var index = dictionary.IndexOfWord(user.Word);
            if (index < 0 && user.Count < Options.MinUsesToSuggest)
            {
                continue;
            }

            var baseScore = index >= 0 ? dictionary.GetBaseScore(index) : 0;
            var kind = user.Word.StartsWith(literalPrefix, StringComparison.Ordinal) ? literalKind : otherKind;
            context.Add(
                attachedPrefix + user.Word,
                kind,
                Ranking.Score(baseScore, user.Count, user.LastUsed, context.Now, Options)
                    + KindBonus(kind)
                    + (user.Key.Length == keyPrefix.Length ? Options.CompleteWordBonus : 0));
        }
    }

    private sealed class Context(string typedLiteral, DateTimeOffset now)
    {
        public DateTimeOffset Now { get; } = now;

        public Dictionary<string, Suggestion> Results { get; } = new(StringComparer.Ordinal);

        /// <summary>Keeps the best match per word; never suggests the word exactly as typed.</summary>
        public void Add(string word, MatchKind kind, double score)
        {
            if (word == typedLiteral)
            {
                return;
            }

            if (!Results.TryGetValue(word, out var existing)
                || Tier(kind) < Tier(existing.Kind)
                || (Tier(kind) == Tier(existing.Kind) && score > existing.Score))
            {
                Results[word] = new Suggestion(word, kind, score);
            }
        }
    }

    /// <summary>Fixed-size set of the best (tier, score) entries seen so far.</summary>
    private sealed class CandidatePool(int capacity)
    {
        private readonly List<(int Index, MatchKind Kind, int Tier, float Score)> _items = new(capacity);
        private int _worst = -1;

        public IReadOnlyList<(int Index, MatchKind Kind, int Tier, float Score)> Items => _items;

        public bool Contains(int index)
        {
            foreach (var item in _items)
            {
                if (item.Index == index)
                {
                    return true;
                }
            }

            return false;
        }

        public bool WouldAccept(int tier, float score) =>
            _items.Count < capacity || IsBetter(tier, score, _items[_worst].Tier, _items[_worst].Score);

        public void Add(int index, MatchKind kind, int tier, float score)
        {
            if (_items.Count < capacity)
            {
                _items.Add((index, kind, tier, score));
            }
            else
            {
                _items[_worst] = (index, kind, tier, score);
            }

            _worst = 0;
            for (var i = 1; i < _items.Count; i++)
            {
                if (IsBetter(_items[_worst].Tier, _items[_worst].Score, _items[i].Tier, _items[i].Score))
                {
                    _worst = i;
                }
            }
        }

        private static bool IsBetter(int tier, float score, int otherTier, float otherScore) =>
            tier < otherTier || (tier == otherTier && score > otherScore);
    }
}
