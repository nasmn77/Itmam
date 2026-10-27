namespace Itmam.Core.Input;

/// <summary>Keystrokes needed to turn the typed prefix into the accepted word.</summary>
/// <param name="Backspaces">Characters to delete before the caret.</param>
/// <param name="Text">Text to type after deleting.</param>
public readonly record struct TextEdit(int Backspaces, string Text)
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
}
