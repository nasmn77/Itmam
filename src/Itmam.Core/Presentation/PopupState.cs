using System.Drawing;

namespace Itmam.Core.Presentation;

/// <summary>What the suggestion bar shows right now: the words (best first) and its screen bounds.</summary>
public sealed record PopupSnapshot(IReadOnlyList<string> Words, Rectangle Bounds)
{
    public static readonly PopupSnapshot Hidden = new([], Rectangle.Empty);

    public bool IsVisible => Words.Count > 0;
}

/// <summary>
/// The bar's current snapshot, published by the UI thread when it shows or hides the bar and read
/// lock-free by the keyboard hook to decide, within its time budget, whether to intercept a key.
/// </summary>
public sealed class PopupState
{
    private PopupSnapshot _current = PopupSnapshot.Hidden;
    private int _canUndo;

    public PopupSnapshot Current => Volatile.Read(ref _current);

    public void Show(IReadOnlyList<string> words, Rectangle bounds) => Volatile.Write(ref _current, new PopupSnapshot(words, bounds));

    public void Hide() => Volatile.Write(ref _current, PopupSnapshot.Hidden);

    /// <summary>
    /// The bar shows a suggestion's actions (right-click): no word for the keys to accept, but clicks
    /// inside <paramref name="bounds"/> still belong to the bar.
    /// </summary>
    public void ShowActions(Rectangle bounds) => Volatile.Write(ref _current, new PopupSnapshot([], bounds));

    /// <summary>
    /// A suggestion was just accepted and Backspace may undo it. Set by the pipeline; cleared by the
    /// hook at the next key or click, and by the pipeline at its next event.
    /// </summary>
    public bool CanUndoAccept => Volatile.Read(ref _canUndo) != 0;

    public void SetUndoAccept(bool available) => Volatile.Write(ref _canUndo, available ? 1 : 0);
}
