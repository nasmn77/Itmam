namespace Itmam.Core.Input;

/// <summary>Keystrokes needed to turn the typed prefix into the accepted word.</summary>
/// <param name="Backspaces">Characters to delete before the caret.</param>
/// <param name="Text">Text to type after deleting.</param>
/// <param name="DeleteForward">
/// Characters to delete after the caret (Delete key) before typing — used to replace an existing
/// space after the caret by the typed one, so accepting mid-line never leaves two spaces.
/// </param>
public readonly record struct TextEdit(int Backspaces, string Text, int DeleteForward = 0)
{
    /// <summary>
    /// Computes the minimal edit: keep the longest common prefix of what was typed and the
    /// suggestion, delete the rest of the typed text and type the rest of the suggestion.
    /// When the suggestion extends the typed text this is just the remaining suffix; when
    /// normalization made them differ from the first letter (ا vs أحمد) it deletes everything
    /// and types the full word.
    /// </summary>
    /// <param name="typed">The buffer exactly as typed (one backspace per UTF-16 unit).</param>
    /// <param name="suggestion">The accepted word in its original spelling.</param>
    /// <param name="appendSpace">Add a trailing space after the word.</param>
    public static TextEdit Compute(string typed, string suggestion, bool appendSpace = false)
    {
        var common = typed.AsSpan().CommonPrefixLength(suggestion);
        var text = suggestion[common..];
        return new TextEdit(typed.Length - common, appendSpace ? text + " " : text);
    }

    /// <summary>
    /// Like <see cref="Compute(string, string, bool)"/> but aware of the character after the caret:
    /// a following space is replaced (Delete, then the word and a space), following punctuation gets
    /// no space before it, and otherwise (end of line, other text, or unknown) a space is appended.
    /// </summary>
    /// <param name="typed">The buffer exactly as typed.</param>
    /// <param name="suggestion">The accepted word.</param>
    /// <param name="appendSpace">The "add a space after accepting" setting.</param>
    /// <param name="charAfterCaret">The next character in the app, '\0' at the end of the text, null if unknown.</param>
    public static TextEdit Compute(string typed, string suggestion, bool appendSpace, char? charAfterCaret)
    {
        if (!appendSpace)
        {
            return Compute(typed, suggestion);
        }

        return charAfterCaret switch
        {
            ' ' or ' ' => Compute(typed, suggestion, appendSpace: true) with { DeleteForward = 1 },
            { } next when char.IsPunctuation(next) => Compute(typed, suggestion),
            _ => Compute(typed, suggestion, appendSpace: true),
        };
    }

    /// <summary>The edit ends with a typed space (the caret is after a space).</summary>
    public bool EndsWithSpace => Text.EndsWith(' ');
}
