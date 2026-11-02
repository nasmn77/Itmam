using Itmam.Core.Languages;

namespace Itmam.Core.Suggestions;

/// <summary>A fixed expression ("بسم الله الرحمن الرحيم"): its words as written and their normalized keys.</summary>
public sealed class Phrase
{
    internal Phrase(string[] words, string[] keys)
    {
        Words = words;
        Keys = keys;
        Text = string.Join(' ', words);
    }

    public string Text { get; }

    public IReadOnlyList<string> Words { get; }

    public IReadOnlyList<string> Keys { get; }
}

/// <summary>
/// Arabic expressions the bar can complete: the whole rest of the phrase as one suggestion, or its
/// next word. Built from the user's own ("My phrases"), the learned ones (LearningService) and
/// data/phrases.txt, in that order.
/// </summary>
/// <remarks>
/// The words typed just before (<c>context</c>) live in memory only (see <c>RecentWords</c>).
/// To keep Tab predictable in everyday text, a phrase is not offered after a short function word
/// alone (في، على، من…): "على" + Tab must still insert "على", not "على سبيل المثال".
/// </remarks>
public sealed class PhraseIndex
{
    private const int MinStartLetters = 3;

    private readonly Phrase[] _phrases;

    private PhraseIndex(Phrase[] phrases, LanguageProfile language) => (_phrases, Language) = (phrases, language);

    public static PhraseIndex Empty { get; } = new([], LanguageProfile.Arabic);

    /// <summary>The language of the phrases: what a word is, how keys are built, which words are too common to start one.</summary>
    public LanguageProfile Language { get; }

    public int Count => _phrases.Length;

    public IReadOnlyList<Phrase> Phrases => _phrases;

    /// <summary>One phrase per line; <c>#</c> starts a comment. Invalid lines are skipped.</summary>
    public static PhraseIndex Read(TextReader reader, LanguageProfile? language = null)
    {
        language ??= LanguageProfile.Arabic;
        var phrases = new List<Phrase>();
        while (reader.ReadLine() is { } line)
        {
            var text = line.Trim();
            if (text.Length > 0 && text[0] != '#' && Parse(text, language) is { } phrase)
            {
                phrases.Add(phrase);
            }
        }

        return new PhraseIndex(Distinct(phrases), language);
    }

    /// <summary>These phrases without the given ones (built-in phrases the user deleted from the bar).</summary>
    public PhraseIndex Without(IEnumerable<string> phrases)
    {
        var hidden = phrases.Select(p => Parse(p, Language)).OfType<Phrase>().Select(p => string.Join(' ', p.Keys)).ToHashSet(StringComparer.Ordinal);
        return hidden.Count == 0 ? this : new([.. _phrases.Where(p => !hidden.Contains(string.Join(' ', p.Keys)))], Language);
    }

    /// <summary>Whether this list holds the phrase (tashkeel and hamza seats ignored).</summary>
    public bool Contains(string phrase) =>
        Parse(phrase, Language) is { } parsed && _phrases.Any(p => p.Keys.SequenceEqual(parsed.Keys, StringComparer.Ordinal));

    /// <summary>The user's phrases first (they win ties), then these.</summary>
    public PhraseIndex With(IEnumerable<string> userPhrases) =>
        new(Distinct([.. userPhrases.Select(p => Parse(p, Language)).OfType<Phrase>(), .. _phrases]), Language);

    /// <summary>
    /// A typed phrase cleaned for storage ("My phrases"): single spaces, tashkeel removed; null unless it
    /// has at least two words of the language (default Arabic).
    /// </summary>
    public static string? Clean(string text, LanguageProfile? language = null) => Parse(text, language ?? LanguageProfile.Arabic)?.Text;

    /// <summary>
    /// <see cref="Clean"/> for a phrase of any language ("My phrases" hold phrases of every language):
    /// Arabic rules for an Arabic phrase, otherwise words of letters of any script.
    /// </summary>
    public static string? CleanAny(string text) => Clean(text, LanguageProfile.Arabic) ?? Clean(text, LanguageRegistry.AnyScript);

    /// <summary>
    /// Suggestions that continue a phrase: first the rest of the best phrases (inserted whole), then their
    /// next words. <paramref name="context"/> holds the normalized words typed just before, oldest first.
    /// </summary>
    /// <param name="context">Normalized keys of the words typed right before the current one.</param>
    /// <param name="typed">The word being typed (may be empty right after a space).</param>
    /// <param name="max">Most suggestions to return.</param>
    /// <param name="sources">
    /// Filled with the whole phrase each suggestion comes from (the bar shows only the rest of it), so the
    /// right-click actions can delete or keep the whole phrase.
    /// </param>
    public IReadOnlyList<string> Suggest(IReadOnlyList<string> context, string typed, int max, IDictionary<string, string>? sources = null)
    {
        var literal = Language.StripIgnorable(typed);
        if (max <= 0 || (literal.Length > 0 && !Language.IsWordPrefix(literal)))
        {
            return [];
        }

        var typedKey = literal.Length == 0 ? "" : Language.Normalize(literal);
        var matches = new List<(Phrase Phrase, int Position)>();
        foreach (var phrase in _phrases)
        {
            if (BestPosition(phrase, context, typedKey) is var position and >= 0)
            {
                matches.Add((phrase, position));
            }
        }

        if (matches.Count == 0)
        {
            return [];
        }

        // Only the phrases matching the most words typed so far: after «بسم الله» the user is in the
        // basmala, not in «الله أكبر» (which «الله» alone would also match). Stable: list order is kept.
        var best = matches.Max(m => m.Position);
        matches.RemoveAll(m => m.Position != best);

        // A longer phrase that continues an earlier one comes right before it: «نفيدكم علما بأن الموضوع»
        // first, «نفيدكم علما» second. Otherwise the shorter one, accepted (and so counted) more often,
        // would always win and the whole phrase could never be inserted at once.
        var family = matches.Select(m => matches.FindIndex(p => StartsWith(m.Phrase.Keys, p.Phrase.Keys))).ToArray();
        matches = [.. matches.Select((m, i) => (Match: m, Family: family[i]))
            .OrderBy(m => m.Family)
            .ThenByDescending(m => m.Match.Phrase.Keys.Count)
            .Select(m => m.Match)];

        // Starting a phrase, one whole phrase is enough (two when one continues the other): the
        // dictionary's words for the same letters (الله، اللغة… for «الل») must keep their place.
        // Inside a phrase, offer up to two endings.
        var sameFamily = family.Count(f => f == family.Min()) > 1;
        var phraseLimit = Math.Min(max, matches[0].Position == 0 && !sameFamily ? 1 : 2);
        var result = new List<string>(max);
        foreach (var (phrase, position) in matches)
        {
            if (position < phrase.Words.Count - 1)
            {
                Add(result, string.Join(' ', phrase.Words.Skip(position)), phraseLimit, phrase, sources);
            }
        }

        foreach (var (phrase, position) in matches)
        {
            if (position > 0)
            {
                Add(result, phrase.Words[position], max, phrase, sources);
            }
        }

        return result;
    }

    // The word of the phrase the user is typing now, or -1: the words before it must be the last words
    // typed, and the typed letters must start it.
    private int BestPosition(Phrase phrase, IReadOnlyList<string> context, string typedKey)
    {
        for (var position = Math.Min(context.Count, phrase.Keys.Count - 1); position >= 0; position--)
        {
            if (!EndsWith(context, phrase.Keys, position) || !phrase.Keys[position].StartsWith(typedKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (position == 0)
            {
                // Starting a phrase: enough of a distinctive first word.
                var first = phrase.Keys[0];
                var enough = typedKey.Length >= MinStartLetters && typedKey.Length * 10 >= first.Length * 6;
                return enough && !Language.CommonPhraseStarts.Contains(first) ? 0 : -1;
            }

            // Right after a space with nothing typed yet: not after a common word alone.
            if (typedKey.Length == 0 && position == 1 && Language.CommonPhraseStarts.Contains(phrase.Keys[0]))
            {
                continue;
            }

            return position;
        }

        return -1;
    }

    private static bool EndsWith(IReadOnlyList<string> context, IReadOnlyList<string> keys, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (context[context.Count - count + i] != keys[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWith(IReadOnlyList<string> keys, IReadOnlyList<string> prefix) =>
        prefix.Count <= keys.Count && prefix.Select((key, i) => keys[i] == key).All(same => same);

    private static void Add(List<string> result, string suggestion, int max, Phrase phrase, IDictionary<string, string>? sources)
    {
        if (result.Count < max && !result.Contains(suggestion))
        {
            result.Add(suggestion);
            sources?.TryAdd(suggestion, phrase.Text);
        }
    }

    private static Phrase? Parse(string text, LanguageProfile language)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => language.StripIgnorable(w))
            .ToArray();
        if (words.Length < 2 || !words.All(w => language.IsWord(w)))
        {
            return null;
        }

        return new Phrase(words, [.. words.Select(w => language.Normalize(w))]);
    }

    private static Phrase[] Distinct(IEnumerable<Phrase> phrases) =>
        [.. phrases.DistinctBy(p => string.Join(' ', p.Keys), StringComparer.Ordinal)];
}
