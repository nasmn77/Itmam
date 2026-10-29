using Itmam.Core.Text;

namespace Itmam.Core.Input;

/// <summary>
/// The last few words typed in a row, so the bar can continue a known phrase
/// (<see cref="Suggestions.PhraseIndex"/>) and repeated sequences can be learned. Memory only: never
/// logged; forgotten when anything other than a space ends a word, and on every caret move, Backspace
/// or focus change. Only the sequences handed to phrase learning are stored (by the learning service).
/// </summary>
public sealed class RecentWords
{
    /// <summary>Longest phrase prefix that can matter.</summary>
    public const int Max = 8;

    /// <summary>Longest sequence learned as a phrase (user decision: 2–5 words).</summary>
    public const int MaxSequenceWords = 5;

    private readonly List<string> _keys = new(Max);
    private readonly List<string> _words = new(Max);

    /// <summary>Normalized keys, oldest first: a copy, safe to hand to other threads.</summary>
    public IReadOnlyList<string> Snapshot { get; private set; } = [];

    /// <summary>Adds a word, or every word of an accepted phrase. Anything not Arabic breaks the chain.</summary>
    public void Push(string text)
    {
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var literal = ArabicText.StripIgnorable(word);
            if (!ArabicText.IsArabicWord(literal))
            {
                Clear();
                return;
            }

            if (_keys.Count == Max)
            {
                _keys.RemoveAt(0);
                _words.RemoveAt(0);
            }

            _keys.Add(ArabicNormalizer.Normalize(literal));
            _words.Add(literal);
        }

        Snapshot = [.. _keys];
    }

    /// <summary>
    /// The sequences of 2 to <see cref="MaxSequenceWords"/> words (as typed, shortest first) that the
    /// words in a row so far form with <paramref name="word"/>, the word just finished.
    /// </summary>
    public IEnumerable<string> SequencesEndingWith(string word)
    {
        var literal = ArabicText.StripIgnorable(word);
        if (!ArabicText.IsArabicWord(literal))
        {
            yield break;
        }

        for (var before = 1; before < MaxSequenceWords && before <= _words.Count; before++)
        {
            yield return string.Join(' ', _words.Skip(_words.Count - before).Append(literal));
        }
    }

    public void Clear()
    {
        if (_keys.Count > 0)
        {
            _keys.Clear();
            _words.Clear();
            Snapshot = [];
        }
    }
}
