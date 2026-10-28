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
/// whitespace, digits and punctuation commit; other letters (e.g. Latin) and Esc reset.
/// Events that may move the caret anywhere — Ctrl+Backspace, navigation, shortcuts, paste, focus,
/// foreground, mouse and layout changes — discard the word without committing it and leave the
/// buffer <see cref="IsDetached">detached</see>: the text before the caret is unknown until it is
/// read (<see cref="ResumeFromContext"/>) or assumed empty (<see cref="ResumeEmpty"/>).
/// Auto-repeated keys simply arrive as repeated events.
/// </remarks>
public sealed class WordBuffer
{
    /// <summary>Longer runs are not words; tracking stops until the next boundary.</summary>
    public const int MaxLength = 48;

    private readonly StringBuilder _text = new(MaxLength);

    // True when the text before the caret is unknown (caret moved, or Backspace into a word we did
    // not see typed, or a run longer than MaxLength). Letters are ignored until resumed or a boundary.
    private bool _detached;

    // Letters typed while detached: they belong to a word whose start we did not see.
    private int _ignoredLetters;

    /// <summary>The word as typed, including tashkeel and tatweel.</summary>
    public string Text => _text.ToString();

    public int Length => _text.Length;

    /// <summary>Number of letters (marks excluded).</summary>
    public int LetterCount { get; private set; }

    public bool IsEmpty => _text.Length == 0;

    /// <summary>The text before the caret is unknown; nothing is tracked until resumed or a boundary.</summary>
    public bool IsDetached => _detached;

    public BufferResult Apply(InputEvent input) => input.Kind switch
    {
        InputEventKind.Character => ApplyCharacter(input.Character),
        InputEventKind.Backspace => ApplyBackspace(),
        InputEventKind.Escape => Reset(),

        // Handled by InputPipeline; the word itself is unchanged.
        InputEventKind.AcceptSuggestion or InputEventKind.DismissSuggestions or InputEventKind.NavigationSelect or InputEventKind.ForgetSuggestion => BufferResult.None,
        _ => Detach(), // the caret may have moved anywhere (toggling off/on included)
    };

    /// <summary>
    /// The accepted suggestion was typed into the app: the word is committed (counts as used for
    /// learning). With a trailing space the next word starts empty; without one the caret is right
    /// after the accepted word, which becomes the current word.
    /// </summary>
    public BufferResult AcceptWord(string word, bool appendedSpace)
    {
        var committed = ArabicText.StripIgnorable(word);
        Reset();
        if (!appendedSpace)
        {
            _text.Append(committed);
            LetterCount = committed.Count(ArabicText.IsArabicLetter);
        }

        return new BufferResult(BufferOutcome.Committed, committed);
    }

    /// <summary>
    /// Resumes tracking with an empty word when the text before the caret cannot be read, unless
    /// letters were typed while detached (then the current word started before we could see it and
    /// tracking waits for the next boundary).
    /// </summary>
    /// <returns><see cref="BufferOutcome.Reset"/> when tracking resumed, otherwise <see cref="BufferResult.None"/>.</returns>
    public BufferResult ResumeEmpty()
    {
        if (!_detached || _ignoredLetters > 0)
        {
            return BufferResult.None;
        }

        _detached = false;
        return new BufferResult(BufferOutcome.Reset);
    }

    /// <summary>
    /// Resumes tracking after <see cref="IsDetached"/>, using the text immediately before the caret
    /// read from the app (only a few characters, never stored beyond the current word).
    /// </summary>
    /// <param name="textBeforeCaret">Characters before the caret, at most <paramref name="maxRead"/>.</param>
    /// <param name="maxRead">How many characters were requested; a full read with no boundary means the word may be cut.</param>
    /// <returns>
    /// <see cref="BufferOutcome.Updated"/> when the word before the caret became the current word,
    /// <see cref="BufferOutcome.Reset"/> when tracking resumed with an empty word (start of text or a
    /// boundary before the caret), <see cref="BufferOutcome.None"/> when the context is unusable and
    /// the buffer stays detached.
    /// </returns>
    public BufferResult ResumeFromContext(ReadOnlySpan<char> textBeforeCaret, int maxRead)
    {
        if (!_detached)
        {
            return BufferResult.None;
        }

        // Trailing run of Arabic letters and marks = the word the caret is in.
        var start = textBeforeCaret.Length;
        while (start > 0 && (ArabicText.IsArabicLetter(textBeforeCaret[start - 1]) || ArabicText.IsIgnorable(textBeforeCaret[start - 1])))
        {
            start--;
        }

        var word = textBeforeCaret[start..];
        var before = start > 0 ? textBeforeCaret[start - 1] : '\0';

        // Unusable: the word may continue beyond what was read, it is glued to a non-Arabic letter,
        // or it is longer than a word can be.
        if ((start == 0 && textBeforeCaret.Length >= maxRead)
            || (start > 0 && !ArabicText.IsWordBoundary(before))
            || word.Length > MaxLength)
        {
            return BufferResult.None;
        }

        // Leading marks without a letter are not part of a word.
        while (!word.IsEmpty && !ArabicText.IsArabicLetter(word[0]))
        {
            word = word[1..];
        }

        _detached = false;
        _ignoredLetters = 0;
        _text.Clear();
        _text.Append(word);
        LetterCount = 0;
        foreach (var c in word)
        {
            if (ArabicText.IsArabicLetter(c))
            {
                LetterCount++;
            }
        }

        return new BufferResult(word.IsEmpty ? BufferOutcome.Reset : BufferOutcome.Updated);
    }

    /// <summary>Discards the current word without committing it.</summary>
    public BufferResult Reset()
    {
        var hadContent = _text.Length > 0;
        _text.Clear();
        LetterCount = 0;
        _detached = false;
        _ignoredLetters = 0;
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
            if (ArabicText.IsArabicLetter(c))
            {
                _ignoredLetters++;
            }

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
