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
/// whitespace, punctuation and digits at the start of a word commit; digits after a letter append, so a
/// text shortcut such as «ع1» is seen whole (such a word is never learned); other letters (e.g. Latin)
/// and Esc reset.
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

    // What was typed while detached and waiting for the context (memory only, at most MaxLength
    // characters): words finished before the context could be read are recovered from it.
    private readonly StringBuilder _blind = new(MaxLength);
    private bool _blindOverflow;

    /// <summary>The word as typed, including tashkeel and tatweel.</summary>
    public string Text => _text.ToString();

    public int Length => _text.Length;

    /// <summary>Number of letters (marks excluded).</summary>
    public int LetterCount { get; private set; }

    public bool IsEmpty => _text.Length == 0;

    /// <summary>The text before the caret is unknown; nothing is tracked until resumed or a boundary.</summary>
    public bool IsDetached => _detached;

    /// <summary>
    /// Set by the pipeline while a context read is scheduled. A word boundary typed meanwhile then
    /// keeps the buffer detached and is recorded with the rest of the typing, instead of starting a
    /// new word blind (which lost the word typed before it).
    /// </summary>
    public bool AwaitingContext { get; set; }

    /// <summary>Something was typed while waiting for the context.</summary>
    public bool HasBlindTyping => _detached && _blind.Length > 0;

    /// <summary>
    /// Words typed and finished while the context was unknown, found by the last resume (oldest first;
    /// <c>EndedBySpace</c> false when another boundary ended it). They were typed by the user, so they
    /// are learned like any finished word.
    /// </summary>
    public IReadOnlyList<(string Word, bool EndedBySpace)> RecoveredWords { get; private set; } = [];

    public BufferResult Apply(InputEvent input) => input.Kind switch
    {
        InputEventKind.Character => ApplyCharacter(input.Character),
        InputEventKind.Backspace => ApplyBackspace(),
        InputEventKind.Escape => Reset(),

        // Handled by InputPipeline; the word itself is unchanged.
        InputEventKind.AcceptSuggestion or InputEventKind.DismissSuggestions or InputEventKind.NavigationSelect or InputEventKind.ForgetSuggestion
            or InputEventKind.UndoAccept => BufferResult.None,
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

    /// <summary>An accept was undone: the word is the text typed before it again.</summary>
    public BufferResult Restore(string typed)
    {
        Reset();
        _detached = false;
        _ignoredLetters = 0;
        _text.Append(typed);
        LetterCount = typed.Count(ArabicText.IsArabicLetter);
        return new BufferResult(typed.Length == 0 ? BufferOutcome.Reset : BufferOutcome.Updated);
    }

    /// <summary>
    /// Resumes tracking with an empty word when the text before the caret cannot be read, unless
    /// letters were typed while detached (then the current word started before we could see it and
    /// tracking waits for the next boundary).
    /// </summary>
    /// <returns><see cref="BufferOutcome.Reset"/> when tracking resumed, otherwise <see cref="BufferResult.None"/>.</returns>
    public BufferResult ResumeEmpty()
    {
        RecoveredWords = [];
        if (!_detached || _ignoredLetters > 0 || _blind.Length > 0)
        {
            return BufferResult.None;
        }

        _detached = false;
        ClearBlind();
        return new BufferResult(BufferOutcome.Reset);
    }

    /// <summary>
    /// The context could not be read, but a word boundary was typed meanwhile: the word after the last
    /// boundary is known exactly (typed after it), and so are the words between boundaries. The text
    /// before the first boundary may continue an existing word, so it is not used.
    /// </summary>
    /// <returns><see cref="BufferOutcome.Updated"/> or <see cref="BufferOutcome.Reset"/> when tracking resumed, otherwise None.</returns>
    public BufferResult ResumeFromBlind()
    {
        RecoveredWords = [];
        var blind = _blind.ToString();
        var first = blind.AsSpan().IndexOfAnyExcept(WordChars());
        if (!_detached || _blindOverflow || first < 0 || !ArabicText.IsWordBoundary(blind[first]))
        {
            return BufferResult.None;
        }

        var (words, current) = Split(blind[first..]);
        RecoveredWords = words;
        return Attach(current);
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
        RecoveredWords = [];
        if (!_detached)
        {
            return BufferResult.None;
        }

        var recovered = Recover(textBeforeCaret, maxRead);

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

        RecoveredWords = recovered;
        return Attach(word.ToString());
    }

    /// <summary>Discards the current word without committing it.</summary>
    public BufferResult Reset()
    {
        var hadContent = _text.Length > 0;
        _text.Clear();
        LetterCount = 0;
        _detached = false;
        _ignoredLetters = 0;
        ClearBlind();
        return hadContent ? new BufferResult(BufferOutcome.Reset) : BufferResult.None;
    }

    private BufferResult ApplyCharacter(char c)
    {
        // A digit after a letter belongs to a text shortcut («ع1»).
        if (char.IsDigit(c) && LetterCount > 0 && !_detached)
        {
            if (_text.Length >= MaxLength)
            {
                return Detach();
            }

            _text.Append(c);
            return new BufferResult(BufferOutcome.Updated);
        }

        if (_detached && AwaitingContext)
        {
            // Recorded until the context is read (see ResumeFromContext / ResumeFromBlind).
            if (_blind.Length < MaxLength)
            {
                _blind.Append(c);
            }
            else
            {
                _blindOverflow = true;
            }

            if (ArabicText.IsArabicLetter(c))
            {
                _ignoredLetters++;
            }

            return BufferResult.None;
        }

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
            if (_blind.Length > 0)
            {
                if (ArabicText.IsArabicLetter(_blind[^1]))
                {
                    _ignoredLetters--;
                }

                _blind.Length--;
            }

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

    // Tracking resumes with this word (letters and marks only) as the current word.
    private BufferResult Attach(string word)
    {
        _detached = false;
        _ignoredLetters = 0;
        ClearBlind();
        _text.Clear();
        _text.Append(word);
        LetterCount = word.Count(ArabicText.IsArabicLetter);
        return new BufferResult(word.Length == 0 ? BufferOutcome.Reset : BufferOutcome.Updated);
    }

    private void ClearBlind()
    {
        _blind.Clear();
        _blindOverflow = false;
    }

    // The words typed blind and finished, when the text read from the app ends with everything typed
    // blind (the app has applied it). The first part counts only if a boundary or the start of the
    // text is right before it, so a word the caret was in the middle of is never learned cut.
    private IReadOnlyList<(string Word, bool EndedBySpace)> Recover(ReadOnlySpan<char> textBeforeCaret, int maxRead)
    {
        if (_blind.Length == 0 || _blindOverflow)
        {
            return [];
        }

        var blind = Lines(_blind.ToString());
        var context = Lines(textBeforeCaret.ToString());
        if (!context.EndsWith(blind, StringComparison.Ordinal))
        {
            return [];
        }

        var start = context.Length - blind.Length;
        var startsWord = start > 0 ? ArabicText.IsWordBoundary(context[start - 1]) : context.Length < maxRead;
        if (!startsWord)
        {
            var first = blind.AsSpan().IndexOfAnyExcept(WordChars());
            if (first < 0)
            {
                return [];
            }

            blind = blind[first..];
        }

        return Split(blind).Words;
    }

    // Words ended by a boundary, and the word after the last boundary (the current word).
    private static (IReadOnlyList<(string Word, bool EndedBySpace)> Words, string Current) Split(string typed)
    {
        var words = new List<(string, bool)>();
        var start = 0;
        for (var i = 0; i < typed.Length; i++)
        {
            if (ArabicText.IsWordBoundary(typed[i]))
            {
                var word = ArabicText.StripIgnorable(typed.AsSpan(start, i - start));
                if (ArabicText.IsArabicWord(word))
                {
                    words.Add((word, typed[i] == ' '));
                }

                start = i + 1;
            }
        }

        var current = typed[start..];
        return (words, current.All(c => ArabicText.IsArabicLetter(c) || ArabicText.IsIgnorable(c)) ? current : string.Empty);
    }

    // Line breaks as one character, whatever the app reports.
    private static string Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace('\u00A0', ' ');

    private static System.Buffers.SearchValues<char> WordChars() => WordCharSet;

    private static readonly System.Buffers.SearchValues<char> WordCharSet = System.Buffers.SearchValues.Create(
        [.. Enumerable.Range(0x0600, 0x0100).Select(c => (char)c).Where(c => ArabicText.IsArabicLetter(c) || ArabicText.IsIgnorable(c))]);

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
