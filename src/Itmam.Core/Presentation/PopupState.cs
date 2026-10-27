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

    public PopupSnapshot Current => Volatile.Read(ref _current);

    public void Show(IReadOnlyList<string> words, Rectangle bounds) => Volatile.Write(ref _current, new PopupSnapshot(words, bounds));

    public void Hide() => Volatile.Write(ref _current, PopupSnapshot.Hidden);
}
