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

    /// <summary>
    /// After the context was read from the app (focus change, click, Backspace…): the words before the
    /// caret's word that are separated by single spaces, so a phrase typed before the read still counts.
    /// </summary>
    /// <param name="textBeforeCaret">The text read before the caret.</param>
    /// <param name="maxRead">Characters asked for: a word touching the start of a full read may be cut.</param>
    public void ResumeFrom(ReadOnlySpan<char> textBeforeCaret, int maxRead)
    {
        Clear();
        var words = new List<string>(Max);
        var end = WordStart(textBeforeCaret, textBeforeCaret.Length); // skip the word being typed
        while (words.Count < Max && end >= 2 && textBeforeCaret[end - 1] == ' ' && IsWordChar(textBeforeCaret[end - 2]))
        {
            var start = WordStart(textBeforeCaret, end - 1);
            if (start == 0 && textBeforeCaret.Length >= maxRead)
            {
                break; // may be the end of a longer word
            }

            words.Add(textBeforeCaret[start..(end - 1)].ToString());
            if (start > 0 && textBeforeCaret[start - 1] != ' ')
            {
                break; // glued to something else: the chain starts here
            }

            end = start;
        }

        words.Reverse();
        Push(string.Join(' ', words));
    }

    private static bool IsWordChar(char c) => ArabicText.IsArabicLetter(c) || ArabicText.IsIgnorable(c);

    // Start of the run of Arabic letters and marks that ends at <paramref name="end"/>.
    private static int WordStart(ReadOnlySpan<char> text, int end)
    {
        while (end > 0 && IsWordChar(text[end - 1]))
        {
            end--;
        }

        return end;
    }

    /// <summary>A copy of the words in a row, to put back with <see cref="Restore"/> (an accept undone).</summary>
    public (string[] Keys, string[] Words) Save() => ([.. _keys], [.. _words]);

    public void Restore((string[] Keys, string[] Words) saved)
    {
        _keys.Clear();
        _keys.AddRange(saved.Keys);
        _words.Clear();
        _words.AddRange(saved.Words);
        Snapshot = [.. _keys];
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
