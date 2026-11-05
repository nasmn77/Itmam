namespace Itmam.App.SettingsUi;

/// <summary>
/// Puts a run of settings cards in order by dragging them with the mouse, like the language list in Windows
/// Settings (user decision after 2.1): a translucent copy of the card follows the mouse, the other cards
/// slide out of the way while dragging, and dropping saves the order without rebuilding the page. Esc
/// cancels; near the top or bottom of the page it scrolls. Buttons inside a card keep working: a drag
/// starts only from the card itself or its text.
/// </summary>
internal sealed class CardReorder : IMessageFilter
{
    private const int WmKeyDown = 0x0100;
    private const int VkEscape = 0x1B;
    private const double SlideMilliseconds = 120;

    private readonly PageScroller _content;
    private readonly IReadOnlyList<SettingCard> _cards;
    private readonly List<Control> _pageControls;
    private readonly Action _layoutPage;
    private readonly Action<IReadOnlyList<int>> _reordered;
    private readonly List<SettingCard> _order;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Dictionary<SettingCard, int> _targets = [];
    private readonly Dictionary<SettingCard, int> _slideFrom = [];

    private SettingCard? _pressed;
    private Point _pressedAt;
    private SettingCard? _dragged;
    private DragGhost? _ghost;
    private Point _grab;
    private int _gap;
    private List<SettingCard> _orderAtStart = [];
    private long _slideStartedAt;

    private CardReorder(PageScroller content, IReadOnlyList<SettingCard> cards, List<Control> pageControls, Action layoutPage, Action<IReadOnlyList<int>> reordered)
    {
        _content = content;
        _cards = cards;
        _pageControls = pageControls;
        _layoutPage = layoutPage;
        _reordered = reordered;
        _order = [.. cards];
        _timer.Tick += (_, _) => Tick();
        foreach (var card in cards)
        {
            card.ShowGrip = true;
            Hook(card);
        }

        // The cards go with the page: nothing may keep running after them.
        cards[0].Disposed += (_, _) =>
        {
            StopDragging();
            _timer.Dispose();
        };
    }

    /// <param name="content">The page the cards are on.</param>
    /// <param name="cards">The cards in their order; they must follow each other in <paramref name="pageControls"/>.</param>
    /// <param name="pageControls">The page's controls in layout order: the cards are moved inside it.</param>
    /// <param name="layoutPage">Stacks the page's controls again.</param>
    /// <param name="reordered">Called after a drop with the new order, as indexes into <paramref name="cards"/>.</param>
    public static void Attach(PageScroller content, IReadOnlyList<SettingCard> cards, List<Control> pageControls, Action layoutPage, Action<IReadOnlyList<int>> reordered)
    {
        if (cards.Count > 1)
        {
            _ = new CardReorder(content, cards, pageControls, layoutPage, reordered);
        }
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg == WmKeyDown && (int)m.WParam == VkEscape && _dragged is not null)
        {
            EndDrag(commit: false);
            return true;
        }

        return false;
    }

    private void Hook(SettingCard card)
    {
        foreach (var control in WithChildren(card))
        {
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
                    _pressed = card;
                    _pressedAt = Cursor.Position;
                }
            };
            control.MouseUp += (_, _) => _pressed = null;
            control.MouseMove += (_, e) =>
            {
                if (_pressed is not null && e.Button == MouseButtons.Left)
                {
                    var drag = SystemInformation.DragSize;
                    var now = Cursor.Position;
                    if (Math.Abs(now.X - _pressedAt.X) >= drag.Width || Math.Abs(now.Y - _pressedAt.Y) >= drag.Height)
                    {
                        BeginDrag(_pressed);
                    }
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

    private void BeginDrag(SettingCard card)
    {
        _pressed = null;
        _dragged = card;
        _orderAtStart = [.. _order];
        _gap = _order[1].Top - _order[0].Bottom;

        // What the card looks like now, from the screen: only the part inside the page, which is under the
        // mouse in the active window. (Drawing an Arabic window to a bitmap comes out mirrored.)
        var shown = Rectangle.Intersect(ScreenBounds(card), ScreenBounds(_content));
        var image = new Bitmap(shown.Width, shown.Height);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.CopyFromScreen(shown.Location, Point.Empty, shown.Size);
        }

        _grab = new Point(_pressedAt.X - shown.X, _pressedAt.Y - shown.Y);
        _ghost = new DragGhost(image) { Location = shown.Location };
        _ghost.Show(_content.FindForm());

        // The page holds the mouse from here on: the card's own text is hidden, and that would end the capture.
        _content.Capture = true;
        _content.Cursor = Cursors.SizeAll;
        card.SetDragHole(true);
        ReadTargets();
        Application.AddMessageFilter(this);
        _timer.Start();
    }

    // Screen rectangle of a control; a mirrored (Arabic) window maps its corners the other way round.
    private static Rectangle ScreenBounds(Control control)
    {
        var a = control.PointToScreen(Point.Empty);
        var b = control.PointToScreen(new Point(control.Width, control.Height));
        return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    // The whole drag runs here: the mouse is read, not waited for, so it also scrolls with the mouse still.
    private void Tick()
    {
        if (_dragged is null || _ghost is null)
        {
            return;
        }

        if ((Control.MouseButtons & MouseButtons.Left) == 0)
        {
            EndDrag(commit: true);
            return;
        }

        var cursor = Cursor.Position;
        _ghost.Location = new Point(cursor.X - _grab.X, cursor.Y - _grab.Y);
        var ghostTop = _content.PointToClient(_ghost.Location).Y;
        AutoScroll(_content.PointToClient(cursor).Y);
        MoveHole(InsertIndex(ghostTop));
        Slide();
    }

    // Where the card goes: measured on the other cards stacked without its gap, so the answer depends only
    // on the mouse and cannot flip back and forth as the cards move.
    private int InsertIndex(int ghostTop)
    {
        var top = _targets[_order[0]];
        var index = 0;
        foreach (var card in _order)
        {
            if (card == _dragged)
            {
                continue;
            }

            if (ghostTop < top + (card.Height / 2))
            {
                return index;
            }

            top += card.Height + _gap;
            index++;
        }

        return index;
    }

    private void MoveHole(int index)
    {
        var current = _order.IndexOf(_dragged!);
        if (index == current)
        {
            return;
        }

        var tops = _order.ToDictionary(card => card, card => card.Top);
        _order.RemoveAt(current);
        _order.Insert(index, _dragged!);
        PutInPage(_order);
        _layoutPage();
        ReadTargets();

        // The layout put the cards in their new places: they slide there from where they were.
        _slideFrom.Clear();
        foreach (var card in _order.Where(card => card != _dragged))
        {
            _slideFrom[card] = tops[card];
            card.Top = tops[card];
        }

        _slideStartedAt = Environment.TickCount64;
    }

    private void PutInPage(List<SettingCard> order)
    {
        var start = order.Min(card => _pageControls.IndexOf(card));
        for (var i = 0; i < order.Count; i++)
        {
            _pageControls[start + i] = order[i];
        }
    }

    private void ReadTargets()
    {
        _targets.Clear();
        foreach (var card in _order)
        {
            _targets[card] = card.Top;
        }
    }

    private void Slide()
    {
        if (_slideFrom.Count == 0)
        {
            return;
        }

        var t = Math.Min(1, (Environment.TickCount64 - _slideStartedAt) / SlideMilliseconds);
        var eased = 1 - ((1 - t) * (1 - t));
        foreach (var (card, from) in _slideFrom)
        {
            card.Top = from + (int)Math.Round((_targets[card] - from) * eased);
        }

        if (t >= 1)
        {
            _slideFrom.Clear();
        }
    }

    // Near the top or bottom edge the page scrolls, so a card can be dragged past the visible ones.
    private void AutoScroll(int y)
    {
        var edge = (int)(40 * _content.DeviceDpi / 96f);
        var step = (int)(8 * _content.DeviceDpi / 96f);
        var offset = _content.Offset;
        if (y < edge)
        {
            _content.Offset = offset - step;
        }
        else if (y > _content.ClientSize.Height - edge)
        {
            _content.Offset = offset + step;
        }

        if (_content.Offset != offset)
        {
            // The page laid itself out at the new offset: any slide ends there.
            _slideFrom.Clear();
            ReadTargets();
        }
    }

    private void EndDrag(bool commit)
    {
        if (_dragged is not { } card)
        {
            return;
        }

        StopDragging();
        card.SetDragHole(false);
        if (!commit)
        {
            _order.Clear();
            _order.AddRange(_orderAtStart);
            PutInPage(_order);
        }

        _layoutPage();
        if (commit && !_order.SequenceEqual(_orderAtStart))
        {
            _reordered([.. _order.Select(c => _cards.ToList().IndexOf(c))]);
        }
    }

    private void StopDragging()
    {
        _timer.Stop();
        Application.RemoveMessageFilter(this);
        _dragged = null;
        _slideFrom.Clear();
        _ghost?.Close();
        _ghost?.Dispose();
        _ghost = null;
        if (!_content.IsDisposed)
        {
            _content.Capture = false;
            _content.Cursor = Cursors.Default;
        }
    }

    // The translucent copy under the mouse: it takes neither the focus nor the mouse.
    private sealed class DragGhost : Form
    {
        private const int WsExNoActivate = 0x08000000;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private readonly Bitmap _image;

        public DragGhost(Bitmap image)
        {
            _image = image;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Opacity = 0.75;
            BackgroundImage = image;
            BackgroundImageLayout = ImageLayout.None;
            ClientSize = image.Size;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WsExNoActivate | WsExTransparent | WsExToolWindow;
                return parameters;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _image.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
