namespace Itmam.App.SettingsUi;

/// <summary>
/// Puts a run of settings cards in order by dragging them with the mouse (user decision after 2.0: the
/// long texts). A line in the accent color shows where the dragged card will go; dragging near the top
/// or bottom of the page scrolls it. Buttons inside a card keep working: a drag starts only from the
/// card itself or its text.
/// </summary>
internal sealed class CardReorder
{
    private readonly PageScroller _content;
    private readonly IReadOnlyList<SettingCard> _cards;
    private readonly Action<int, int> _move;
    private readonly Panel _marker = new() { Visible = false };
    private Point? _pressedAt;
    private int _pressedIndex = -1;

    // The card being dragged (-1 when none): kept here, the drag data is only a plain string.
    private int _dragging = -1;

    /// <param name="content">The page the cards are on.</param>
    /// <param name="cards">The cards, in their current order.</param>
    /// <param name="accent">Color of the insertion line.</param>
    /// <param name="move">Moves the item at the first index to the second (both in the list without it moved).</param>
    private CardReorder(PageScroller content, IReadOnlyList<SettingCard> cards, Color accent, Action<int, int> move)
    {
        _content = content;
        _cards = cards;
        _move = move;
        _marker.BackColor = accent;
        content.Controls.Add(_marker);
        content.AllowDrop = true;
        content.DragOver += OnDragOver;
        content.DragDrop += OnDragDrop;
        content.DragLeave += OnDragLeave;
        for (var i = 0; i < cards.Count; i++)
        {
            cards[i].ShowGrip = true;
            Hook(cards[i], i);
        }

        // The page is rebuilt after every change: the line and the page's handlers go with the cards.
        if (cards.Count > 0)
        {
            cards[0].Disposed += (_, _) =>
            {
                content.DragOver -= OnDragOver;
                content.DragDrop -= OnDragDrop;
                content.DragLeave -= OnDragLeave;
                content.AllowDrop = false;
                content.Controls.Remove(_marker);
                _marker.Dispose();
            };
        }
    }

    public static void Attach(PageScroller content, IReadOnlyList<SettingCard> cards, Color accent, Action<int, int> move)
    {
        if (cards.Count > 1)
        {
            _ = new CardReorder(content, cards, accent, move);
        }
    }

    private void Hook(SettingCard card, int index)
    {
        foreach (var control in WithChildren(card))
        {
            control.AllowDrop = true;
            control.DragOver += OnDragOver;
            control.DragDrop += OnDragDrop;
            control.DragLeave += OnDragLeave;

            // Not from the buttons: a click there must stay a click.
            if (card.Control is { } buttons && (control == buttons || buttons.Contains(control)))
            {
                continue;
            }

            control.Cursor = Cursors.SizeAll;
            control.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    _pressedAt = Cursor.Position;
                    _pressedIndex = index;
                }
            };
            control.MouseUp += (_, _) => _pressedAt = null;
            control.MouseMove += (_, e) =>
            {
                if (_pressedAt is not { } start || e.Button != MouseButtons.Left)
                {
                    return;
                }

                var drag = SystemInformation.DragSize;
                var now = Cursor.Position;
                if (Math.Abs(now.X - start.X) < drag.Width && Math.Abs(now.Y - start.Y) < drag.Height)
                {
                    return;
                }

                _pressedAt = null;
                _dragging = _pressedIndex;
                try
                {
                    control.DoDragDrop(nameof(CardReorder), DragDropEffects.Move);
                }
                finally
                {
                    _dragging = -1;
                    HideMarker();
                }
            };
        }
    }

    private static IEnumerable<Control> WithChildren(Control control)
    {
        yield return control;
        foreach (Control child in control.Controls)
        {
            foreach (var descendant in WithChildren(child))
            {
                yield return descendant;
            }
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (_dragging < 0)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        e.Effect = DragDropEffects.Move;
        var point = _content.PointToClient(new Point(e.X, e.Y));
        AutoScroll(point.Y);
        ShowMarker(InsertIndex(point.Y));
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        HideMarker();
        var from = _dragging;
        if (from < 0)
        {
            return;
        }

        var to = InsertIndex(_content.PointToClient(new Point(e.X, e.Y)).Y);
        if (to > from)
        {
            to--; // the list without the moved item
        }

        if (to != from)
        {
            _move(from, to);
        }
    }

    private void OnDragLeave(object? sender, EventArgs e)
    {
        // Leaving a card for the next one also raises this: hide only when the mouse left the page.
        if (!_content.ClientRectangle.Contains(_content.PointToClient(Cursor.Position)))
        {
            HideMarker();
        }
    }

    // Before the first card whose middle is below the mouse, or after the last one.
    private int InsertIndex(int y)
    {
        for (var i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            if (y < card.Top + (card.Height / 2))
            {
                return i;
            }
        }

        return _cards.Count;
    }

    private void ShowMarker(int index)
    {
        var scale = _content.DeviceDpi / 96f;
        var thickness = Math.Max(2, (int)(3 * scale));
        var reference = _cards[Math.Min(index, _cards.Count - 1)];
        var gap = index < _cards.Count && index > 0 ? reference.Top - _cards[index - 1].Bottom : (int)(4 * scale);
        var y = index < _cards.Count
            ? reference.Top - (gap / 2) - (thickness / 2)
            : reference.Bottom + (gap / 2) - (thickness / 2);
        _marker.SetBounds(reference.Left, y, reference.Width, thickness);
        _marker.Visible = true;
        _marker.BringToFront();
    }

    private void HideMarker()
    {
        // The drop may rebuild the page (and dispose the line) before the drag loop returns.
        if (!_marker.IsDisposed)
        {
            _marker.Visible = false;
        }
    }

    // Near the top or bottom edge the page scrolls, so a card can be dragged past the visible ones.
    private void AutoScroll(int y)
    {
        var edge = (int)(40 * _content.DeviceDpi / 96f);
        var step = (int)(16 * _content.DeviceDpi / 96f);
        if (y < edge)
        {
            _content.Offset -= step;
        }
        else if (y > _content.ClientSize.Height - edge)
        {
            _content.Offset += step;
        }
    }
}
