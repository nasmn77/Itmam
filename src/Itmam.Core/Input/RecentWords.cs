using Itmam.Core.Text;

namespace Itmam.Core.Input;

/// <summary>
/// The last few words typed in a row (normalized), so the bar can continue a known phrase
/// (<see cref="Suggestions.PhraseIndex"/>). Memory only: never stored, logged or learned; forgotten
/// when anything other than a space ends a word, and on every caret move, Backspace or focus change.
/// </summary>
public sealed class RecentWords
{
    /// <summary>Longest phrase prefix that can matter.</summary>
    public const int Max = 8;

    private readonly List<string> _keys = new(Max);

    /// <summary>A copy, oldest first (safe to hand to other threads).</summary>
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
            }

            _keys.Add(ArabicNormalizer.Normalize(literal));
        }

        Snapshot = [.. _keys];
    }

    public void Clear()
    {
        if (_keys.Count > 0)
        {
            _keys.Clear();
            Snapshot = [];
        }
    }
}
