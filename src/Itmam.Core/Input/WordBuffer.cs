using System.Text;
using Itmam.Core.Text;

namespace Itmam.Core.Input;

public enum BufferOutcome
{
    /// <summary>Nothing changed.</summary>
    None,

    /// <summary>The current word changed (letter added or removed).</summary>
    Updated,

    /// <summary>A word boundary ended the word; <see cref="BufferResult.CommittedWord"/> is learnable.</summary>
    Committed,

    /// <summary>The word was discarded without committing it.</summary>
    Reset,
}

public readonly record struct BufferResult(BufferOutcome Outcome, string? CommittedWord = null)
{
    public static BufferResult None => new(BufferOutcome.None);
}

/// <summary>
/// Tracks the word being typed (characters since the last boundary) from abstract input events.
/// </summary>
/// <remarks>
/// Rules: Arabic letters append; tashkeel/tatweel append (inside a word) but are ignored for matching;
/// whitespace, digits and punctuation commit; Ctrl+Backspace, navigation, shortcuts, paste, focus,
/// foreground, mouse and layout changes reset without committing; other letters (e.g. Latin) reset.
/// Auto-repeated keys simply arrive as repeated events.
/// </remarks>
public sealed class WordBuffer
{
    /// <summary>Longer runs are not words; tracking stops until the next boundary.</summary>
    public const int MaxLength = 48;

    private readonly StringBuilder _text = new(MaxLength);

    // True when the caret is inside a word whose start we did not see (Backspace into the previous
    // word, or a run longer than MaxLength). Letters are ignored until the next boundary or reset.
    private bool _detached;

    /// <summary>The word as typed, including tashkeel and tatweel.</summary>
    public string Text => _text.ToString();

    public int Length => _text.Length;

    /// <summary>Number of letters (marks excluded).</summary>
    public int LetterCount { get; private set; }

    public bool IsEmpty => _text.Length == 0;

    /// <summary>The caret is inside a word whose start was not seen; nothing is tracked until a boundary.</summary>
    public bool IsDetached => _detached;

    public BufferResult Apply(InputEvent input) => input.Kind switch
    {
        InputEventKind.Character => ApplyCharacter(input.Character),
        InputEventKind.Backspace => ApplyBackspace(),
        _ => Reset(),
    };

    /// <summary>Discards the current word without committing it.</summary>
    public BufferResult Reset()
    {
        var hadContent = _text.Length > 0;
        _text.Clear();
        LetterCount = 0;
        _detached = false;
        return hadContent ? new BufferResult(BufferOutcome.Reset) : BufferResult.None;
    }

    private BufferResult ApplyCharacter(char c)
    {
        if (ArabicText.IsWordBoundary(c))
        {
            return Commit();
        }

        if (_detached)
        {
            return BufferResult.None;
        }

        if (ArabicText.IsArabicLetter(c))
        {
            if (_text.Length >= MaxLength)
            {
                return Detach();
            }

            _text.Append(c);
            LetterCount++;
            return new BufferResult(BufferOutcome.Updated);
        }

        if (ArabicText.IsIgnorable(c))
        {
            if (LetterCount == 0 || _text.Length >= MaxLength)
            {
                return BufferResult.None;
            }

            _text.Append(c);
            return new BufferResult(BufferOutcome.Updated);
        }

        // Any other character (Latin letter, unknown symbol) means this is not an Arabic word.
        return Reset();
    }

    /// <summary>
    /// Backspace removes exactly one character; the word is reset only when no letter is left.
    /// Backspace on an empty buffer moves the caret into the previous word, whose start we do not
    /// know, so tracking stops until the next boundary.
    /// </summary>
    private BufferResult ApplyBackspace()
    {
        if (_detached)
        {
            return BufferResult.None;
        }

        if (_text.Length == 0)
        {
            return Detach();
        }

        var removed = _text[^1];
        _text.Length--;
        if (ArabicText.IsArabicLetter(removed))
        {
            LetterCount--;
        }

        if (LetterCount == 0)
        {
            Reset();
            return new BufferResult(BufferOutcome.Reset);
        }

        return new BufferResult(BufferOutcome.Updated);
    }

    private BufferResult Detach()
    {
        var result = Reset();
        _detached = true;
        return result;
    }

    private BufferResult Commit()
    {
        if (LetterCount == 0 || _detached)
        {
            return Reset();
        }

        var word = ArabicText.StripIgnorable(_text.ToString());
        _text.Clear();
        LetterCount = 0;
        return new BufferResult(BufferOutcome.Committed, word);
    }
}
