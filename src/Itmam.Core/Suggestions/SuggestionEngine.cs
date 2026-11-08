using Itmam.Core.Dictionary;
using Itmam.Core.Languages;
using Itmam.Core.Learning;

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

    /// <summary>
    /// Nothing else matched: one typing mistake undone (a key next to the right one, a sound-alike letter, two
    /// letters swapped, a letter too many or missing, a missing space).
    /// </summary>
    KeyTypo = 4,
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
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public SuggestionOptions Options { get; set; } = options ?? new SuggestionOptions();

    public WordDictionary Dictionary => dictionary;

    public UserWordIndex UserWords => userWords;

    /// <summary>The dictionary's language: its letters, matching keys and clitics.</summary>
    public LanguageProfile Language => dictionary.Language;

    public IReadOnlyList<Suggestion> Suggest(string typed)
    {
        var literal = Language.StripIgnorable(typed);
        if (literal.Length < Options.MinChars || !Language.IsWordPrefix(literal))
        {
            return [];
        }

        var key = Language.Normalize(literal);
        var context = new Context(literal, _time.GetUtcNow());

        Collect(context, literal, key, MatchKind.Literal, MatchKind.Normalized, attachedPrefix: "", excludedStemStart: null);

        if (context.Results.Count < Options.MaxSuggestions)
        {
            foreach (var variant in Language.FallbackVariants(key))
            {
                Collect(context, literal, variant, MatchKind.HamzaFallback, MatchKind.HamzaFallback, "", excludedStemStart: null);
            }
        }

        if (Options.CliticAware && context.Results.Count < Options.MaxSuggestions)
        {
            foreach (var clitic in Language.Clitics)
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
                    excludedStemStart: Language.ExcludedStemStart(clitic));
            }
        }

        var typos = KeyTypoNeed(context, literal, key);
        if (typos != TypoNeed.None)
        {
            CollectKeyTypos(context, key, typos == TypoNeed.RareWord);
        }

        var minTypoScore = typos == TypoNeed.RareWord ? Options.KeyTypoStrongScore : Options.KeyTypoMinScore;

        // In the case being typed; the same word in two cases (learned «Hello», dictionary «hello») once.
        return context.Results.Values
            .OrderBy(s => Tier(s.Kind))
            .ThenByDescending(s => s.Score)
            .ThenBy(s => s.Word, StringComparer.Ordinal)
            .Select(s => s with { Word = Language.MatchCase(s.Word, literal) })
            .Where(s => s.Word != literal && (s.Kind != MatchKind.KeyTypo || s.Score >= minTypoScore))
            .DistinctBy(s => Language.DisplayKey(s.Word), StringComparer.Ordinal)
            .Take(Options.MaxSuggestions)
            .ToArray();
    }

    private enum TypoNeed
    {
        None,

        /// <summary>The letters typed match nothing at all: any reasonably common correction is shown.</summary>
        NoMatch,

        /// <summary>
        /// They are only a rare dictionary entry with no longer forms (the large dictionaries hold misspellings:
        /// «مدرية»): only a clearly common correction is shown. Not with other matches: the letters may be the
        /// start of a correct word («الرجف» on the way to «الرجفان»).
        /// </summary>
        RareWord,
    }

    private TypoNeed KeyTypoNeed(Context context, string literal, string key)
    {
        if (context.Results.Count > 0 || userWords.Get(literal) is not null)
        {
            return TypoNeed.None;
        }

        var (start, end) = dictionary.FindPrefixRange(key);
        if (start >= end)
        {
            return TypoNeed.NoMatch;
        }

        // The spellings of the typed word itself sort first in the range; a correct word is left alone.
        for (var i = start; i < end && dictionary.GetKey(i).Length == key.Length; i++)
        {
            if (dictionary.GetBaseScore(i) >= Options.KeyTypoRareWordScore)
            {
                return TypoNeed.None;
            }
        }

        return TypoNeed.RareWord;
    }

    // One mistake undone at a time, each result looked up as a prefix (the word may still be incomplete).
    // A rare word in the dictionary may be correct: removing a letter or adding a space would break real words
    // with an attached pronoun («مطاولة» → «طاولة», «شارعها» → «شارع ها»).
    private void CollectKeyTypos(Context context, string key, bool rareWord)
    {
        if (!Options.FixKeyTypos || key.Length < Options.KeyTypoMinChars || key.Length > Options.KeyTypoMaxChars)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal) { key };
        void Try(string variant)
        {
            // Most variants exist nowhere: no need to allocate a candidate pool for them.
            var (start, end) = dictionary.FindPrefixRange(variant);
            if (seen.Add(variant) && (start < end || userWords.FindByPrefix(variant).Any()))
            {
                Collect(context, variant, variant, MatchKind.KeyTypo, MatchKind.KeyTypo, "", excludedStemStart: null, skipExactPrefix: false, Options.KeyTypoMinBaseScore);
            }
        }

        var letters = key.ToCharArray();
        for (var i = 0; i < letters.Length; i++)
        {
            var original = letters[i];

            // The wrong key, or a letter written as another that sounds like it.
            foreach (var other in Language.KeyNeighbors(original).Concat(Language.SoundAlikes(original)))
            {
                letters[i] = other;
                Try(new string(letters));
            }

            letters[i] = original;

            // Two letters swapped.
            if (i + 1 < letters.Length && letters[i + 1] != original)
            {
                (letters[i], letters[i + 1]) = (letters[i + 1], letters[i]);
                Try(new string(letters));
                (letters[i], letters[i + 1]) = (letters[i + 1], letters[i]);
            }
        }

        if (key.Length < Options.KeyTypoEditMinChars)
        {
            return;
        }

        for (var i = 0; i < key.Length; i++)
        {
            if (!rareWord)
            {
                Try(key.Remove(i, 1)); // a letter too many
            }

            // A letter missing; never before the first one, which is rarely forgotten and matches too much.
            for (var a = 0; i > 0 && a < Language.KeyAlphabet.Length; a++)
            {
                Try(key.Insert(i, Language.KeyAlphabet[a].ToString()));
            }
        }

        if (!rareWord)
        {
            CollectMissingSpace(context, key);
        }
    }

    // «فيالبيت» → «في البيت»: two common words, both complete. The second has 3 letters or more: shorter ones are
    // mostly attached pronouns («حملتها» is not «حملت ها»).
    private void CollectMissingSpace(Context context, string key)
    {
        for (var i = 2; i <= key.Length - 3; i++)
        {
            var (left, leftScore) = BestSpelling(key[..i]);
            var (right, rightScore) = BestSpelling(key[i..]);
            var score = Math.Min(leftScore, rightScore);
            if (left is not null && right is not null && score >= Options.KeyTypoSplitMinScore)
            {
                context.Add($"{left} {right}", MatchKind.KeyTypo, score + Options.CompleteWordBonus);
            }
        }
    }

    private (string? Word, double Score) BestSpelling(string key)
    {
        string? best = null;
        var bestScore = double.NegativeInfinity;
        var (start, end) = dictionary.FindPrefixRange(key);
        for (var i = start; i < end && dictionary.GetKey(i).Length == key.Length; i++)
        {
            var score = dictionary.GetBaseScore(i);
            if (score > bestScore && !userWords.IsBlocked(dictionary.GetDisplay(i)))
            {
                best = dictionary.GetDisplay(i).ToString();
                bestScore = score;
            }
        }

        return (best, bestScore);
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
        string? excludedStemStart,
        bool skipExactPrefix = true,
        double minBaseScore = double.NegativeInfinity)
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
            if (dictionary.GetBaseScore(i) < minBaseScore)
            {
                return;
            }

            var score = dictionary.GetBaseScore(i) + (isLiteral ? literalBonus : otherBonus);
            if (dictionary.GetKey(i).Length == keyPrefix.Length)
            {
                score += completeWordBonus;
            }

            if (!pool.WouldAccept(Tier(kind), score)
                || pool.Contains(i)
                || (skipExactPrefix && display.SequenceEqual(literalPrefix))
                || (excludedStemStart is not null && display.StartsWith(excludedStemStart))
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
                || (skipExactPrefix && user.Word == literalPrefix)
                || (excludedStemStart is not null && user.Word.StartsWith(excludedStemStart, StringComparison.Ordinal)))
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
