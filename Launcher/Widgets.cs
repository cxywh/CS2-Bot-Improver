using System.Drawing.Drawing2D;

namespace BotLauncher;

// A weight row rendered as one custom-painted strip: name, drag track, numeric value, share.
// Dragging, arrow keys and double click edit the value; the mouse wheel scrolls the list.
internal sealed class SliderRow : Control
{
    private const int ValueWidth = 52;
    private const int PercentWidth = 64;

    private readonly int _min;
    private readonly int _max;
    private int _value;
    private int _total;
    private bool _hot;
    private bool _dragging;

    internal SliderRow(string key, string label, int value, int min = 1, int max = 100)
    {
        Key = key;
        Label = label;
        _min = min;
        _max = max;
        _value = Math.Clamp(value, min, max);
        Height = 36;
        Width = 720;
        BackColor = Theme.Card;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    internal string Key { get; }

    internal string Label { get; }

    // Width of the name column; the page sizes it to the longest label in the group.
    internal int NameWidth { get; set; } = 268;

    internal int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _min, _max);
            if (clamped == _value)
                return;
            _value = clamped;
            Invalidate();
            Changed?.Invoke(this);
        }
    }

    internal int Min => _min;

    internal int Max => _max;

    internal event Action<SliderRow>? Changed;

    private Rectangle TrackArea => new(NameWidth, 0, Math.Max(120, Width - NameWidth - ValueWidth - PercentWidth - 28), Height);

    internal void SetTotal(int total)
    {
        _total = total;
        Invalidate();
    }

    private void SetFromMouse(int x)
    {
        var area = TrackArea;
        var pad = 8;
        var usable = Math.Max(1, area.Width - pad * 2);
        var fraction = Math.Clamp((x - area.X - pad) / (float)usable, 0F, 1F);
        Value = (int)Math.Round(_min + fraction * (_max - _min));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var back = new SolidBrush(_hot ? Theme.CardHi : Theme.Card))
            g.FillRectangle(back, ClientRectangle);

        var nameRect = new Rectangle(4, 0, NameWidth - 12, Height);
        TextRenderer.DrawText(g, Label, Theme.Body, nameRect, _hot ? Theme.Text : Color.FromArgb(210, 216, 228),
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.Left);

        var area = TrackArea;
        var pad = 8;
        var trackRect = new Rectangle(area.X + pad, Height / 2 - 3, Math.Max(10, area.Width - pad * 2), 6);
        Theme.FillRound(g, trackRect, 3, Color.FromArgb(44, 50, 62));

        var fraction = (float)(_value - _min) / Math.Max(1, _max - _min);
        var fillWidth = (int)Math.Round(trackRect.Width * fraction);
        if (fillWidth > 2)
        {
            using var brush = Theme.AccentBrush(trackRect);
            var fillRect = new Rectangle(trackRect.X, trackRect.Y, fillWidth, trackRect.Height);
            Theme.FillRound(g, fillRect, 3, Color.Transparent);
            using var path = Theme.RoundRect(fillRect, 3);
            g.FillPath(brush, path);
        }

        var thumbX = trackRect.X + fillWidth;
        var thumb = new Rectangle(thumbX - 7, Height / 2 - 7, 14, 14);
        if (_hot || _dragging)
        {
            using var glow = new SolidBrush(Color.FromArgb(46, Theme.Accent));
            g.FillEllipse(glow, thumb.X - 4, thumb.Y - 4, thumb.Width + 8, thumb.Height + 8);
        }
        using (var thumbBrush = new SolidBrush(_dragging ? Color.White : Theme.Accent))
            g.FillEllipse(thumbBrush, thumb);
        using (var ring = new Pen(Color.FromArgb(24, 28, 36), 2F))
            g.DrawEllipse(ring, thumb);

        var valueRect = new Rectangle(Width - ValueWidth - PercentWidth - 8, 0, ValueWidth, Height);
        using (var valueBrush = new SolidBrush(Theme.Text))
        using (var valueFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            g.DrawString(_value.ToString(), Theme.Mono, valueBrush, valueRect, valueFormat);

        var percent = _total > 0 ? $"{_value * 100.0 / _total:0.0}%" : "—";
        var percentRect = new Rectangle(Width - PercentWidth, 0, PercentWidth - 2, Height);
        using (var percentBrush = new SolidBrush(Theme.Muted))
        using (var percentFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            g.DrawString(percent, Theme.Small, percentBrush, percentRect, percentFormat);

        using var line = new Pen(Color.FromArgb(26, 31, 40));
        g.DrawLine(line, 4, Height - 1, Width - 8, Height - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            SetFromMouse(e.X);
            return;
        }

        var hot = e.X >= NameWidth - 10;
        if (hot != _hot)
        {
            _hot = hot;
            Invalidate();
        }

        Cursor = e.X >= NameWidth - 10 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragging)
            return;
        _hot = false;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;

        Focus();
        if (e.Clicks == 2)
        {
            PromptValue();
            return;
        }

        if (e.X < NameWidth - 10)
            return;

        _dragging = true;
        Capture = true;
        SetFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down:
                Value = _value - 1;
                e.Handled = true;
                break;
            case Keys.Right or Keys.Up:
                Value = _value + 1;
                e.Handled = true;
                break;
            case Keys.Enter:
                PromptValue();
                e.Handled = true;
                break;
        }
    }

    internal void PromptValue()
    {
        using var dialog = new PromptForm($"设置「{Label}」权重", $"范围 {_min} – {_max}", _value, _min, _max);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            Value = dialog.Result;
    }
}

// Small rounded push button. Accent variant is used for the primary action on each page.
internal sealed class PillButton : Control
{
    private bool _hot;
    private bool _down;

    internal PillButton(string text, bool accent = false, int width = 0)
    {
        Text = text;
        Accent = accent;
        Height = 34;
        Font = Theme.Body;
        // Width is derived from the measured text; the caller value only sets a floor.
        var needed = Theme.TextWidth(Font, text) + 44;
        Width = Math.Max(width > 0 ? width : 0, needed);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    internal bool Accent { get; set; }

    internal DialogResult Result { get; set; } = DialogResult.None;

    internal Color TextColor { get; set; } = Color.Empty;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        if (Accent)
        {
            var fill = _down ? Color.FromArgb(70, 108, 210) : _hot ? Color.FromArgb(104, 150, 255) : Theme.Accent;
            using var brush = new LinearGradientBrush(rect, fill, _hot ? Theme.Accent2 : Color.FromArgb(120, 82, 226), LinearGradientMode.Horizontal);
            using var path = Theme.RoundRect(rect, 8);
            g.FillPath(brush, path);
        }
        else
        {
            Theme.FillRound(g, rect, 8, _down ? Color.FromArgb(40, 46, 58) : _hot ? Theme.CardHi : Color.FromArgb(28, 33, 42));
            Theme.StrokeRound(g, rect, 8, _hot ? Theme.BorderHi : Theme.Border);
        }

        var color = TextColor.IsEmpty ? (_hot || Accent ? Color.White : Theme.Text) : TextColor;
        TextRenderer.DrawText(g, Text, Font, rect, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = false;
        _down = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _down = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _down = false;
        Invalidate();
        base.OnMouseUp(e);
    }
}

// Selectable square chip used by the knife picker and the weights group switcher.
internal sealed class Chip : Control
{
    private bool _hot;

    internal Chip(string text, bool selected, int width = 0, int height = 36)
    {
        Text = text;
        Selected = selected;
        Height = height;
        // Width is derived from the measured text so long labels are not clipped.
        Width = Math.Max(width, Theme.TextWidth(Theme.Body, text) + 46);
        Font = Theme.Body;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    internal bool Selected { get; set; }

    internal event Action<Chip>? Toggled;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        if (Selected)
        {
            using var brush = new SolidBrush(Color.FromArgb(34, 48, 82));
            using var path = Theme.RoundRect(rect, 8);
            g.FillPath(brush, path);
            Theme.StrokeRound(g, rect, 8, Color.FromArgb(78, 116, 200));
            var dot = new Rectangle(10, Height / 2 - 3, 6, 6);
            Theme.DrawDot(g, dot, Theme.Accent);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(24, 0, Width - 28, Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }
        else
        {
            Theme.FillRound(g, rect, 8, _hot ? Theme.CardHi : Color.FromArgb(25, 30, 38));
            Theme.StrokeRound(g, rect, 8, _hot ? Theme.BorderHi : Theme.Border);
            TextRenderer.DrawText(g, Text, Font, rect, _hot ? Theme.Text : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Selected = !Selected;
        Invalidate();
        Toggled?.Invoke(this);
    }
}

// Left navigation entry with an accent bar when active.
internal sealed class NavItem : Control
{
    private bool _hot;

    internal NavItem(string text, string glyph)
    {
        Text = text;
        Glyph = glyph;
        Height = 42;
        Font = Theme.Body;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    internal string Glyph { get; }

    internal bool Active { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        if (Active)
        {
            Theme.FillRound(g, rect, 8, Color.FromArgb(30, 38, 54));
            Theme.FillRound(g, new Rectangle(0, 10, 3, Height - 20), 2, Theme.Accent);
        }
        else if (_hot)
        {
            Theme.FillRound(g, rect, 8, Color.FromArgb(24, 29, 37));
        }

        TextRenderer.DrawText(g, Glyph, Theme.Body, new Rectangle(16, 0, 20, Height),
            Active ? Theme.Accent : Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(42, 0, Width - 50, Height),
            Active ? Theme.Text : _hot ? Color.FromArgb(206, 214, 228) : Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = false;
        Invalidate();
        base.OnMouseLeave(e);
    }
}

// Large selectable card for mode / difficulty choices.
internal sealed class ChoiceCard : Control
{
    private bool _hot;

    internal ChoiceCard(string title, string subtitle, string badge)
    {
        Title = title;
        Subtitle = subtitle;
        Badge = badge;
        Height = 96;
        Measure();
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    internal string Title { get; }

    internal string Subtitle { get; set; }

    internal string Badge { get; set; }

    internal bool Selected { get; set; }

    private int _titleHeight = 24;
    private int _subtitleTop = 42;
    private int _subtitleHeight = 40;

    // Card height is derived from the measured title and wrapped subtitle.
    internal void Fit(int width)
    {
        Width = width;
        Measure();
        Height = _subtitleTop + _subtitleHeight + 14;
    }

    private void Measure()
    {
        var innerWidth = Math.Max(120, Width - 40);
        _titleHeight = Theme.SingleLine(Theme.H2, Title);
        _subtitleTop = 14 + _titleHeight + 4;
        _subtitleHeight = Theme.Wrapped(Theme.Small, Subtitle, innerWidth);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        Theme.FillRound(g, rect, 12, Selected ? Color.FromArgb(30, 40, 62) : _hot ? Theme.CardHi : Theme.Card);
        Theme.StrokeRound(g, rect, 12, Selected ? Color.FromArgb(84, 124, 214) : _hot ? Theme.BorderHi : Theme.Border);

        if (Selected)
            Theme.DrawDot(g, new Rectangle(18, 22, 8, 8), Theme.Accent);

        var left = Selected ? 34 : 18;
        var titleWidth = string.IsNullOrEmpty(Badge) ? Width - left - 22 : Width - left - 138;
        TextRenderer.DrawText(g, Title, Theme.H2, new Rectangle(left, 14, Math.Max(80, titleWidth), _titleHeight),
            Theme.Text, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Subtitle, Theme.Small,
            new Rectangle(left, _subtitleTop, Math.Max(120, Width - left - 22), _subtitleHeight),
            Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

        if (!string.IsNullOrEmpty(Badge))
        {
            var size = TextRenderer.MeasureText(Badge, Theme.Small);
            var badgeRect = new Rectangle(Width - size.Width - 34, 15, size.Width + 18, Math.Max(22, size.Height + 6));
            Theme.FillRound(g, badgeRect, 6, Selected ? Color.FromArgb(48, 74, 128) : Color.FromArgb(34, 40, 50));
            TextRenderer.DrawText(g, Badge, Theme.Small, badgeRect,
                Selected ? Color.FromArgb(190, 214, 255) : Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = false;
        Invalidate();
        base.OnMouseLeave(e);
    }
}

// Minimal modal used for numeric entry, so a weight can be typed instead of dragged.
internal sealed class PromptForm : Form
{
    private readonly NumericUpDown? _numeric;
    private readonly TextBox? _text;
    private readonly int _min;
    private readonly int _max;
    private readonly bool _textMode;

    internal PromptForm(string title, string hint, object value, int min = 0, int max = int.MaxValue, bool textMode = false)
    {
        _min = min;
        _max = max;
        _textMode = textMode;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Theme.Card;
        ForeColor = Theme.Text;
        ClientSize = new Size(360, textMode ? 150 : 168);
        Font = Theme.Body;

        var label = new Label { Text = hint, ForeColor = Theme.Muted, Location = new Point(20, 18), AutoSize = true };
        Controls.Add(label);

        if (textMode)
        {
            _text = new TextBox
            {
                Text = Convert.ToString(value) ?? string.Empty,
                Location = new Point(20, 46),
                Width = 316,
                BackColor = Color.FromArgb(18, 22, 28),
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.Mono
            };
            Controls.Add(_text);
        }
        else
        {
            _numeric = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Clamp(Convert.ToInt32(value), min, max),
                Location = new Point(20, 46),
                Width = 316,
                BackColor = Color.FromArgb(18, 22, 28),
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.Mono
            };
            Controls.Add(_numeric);
        }

        var ok = new PillButton("确定", accent: true, width: 96) { Location = new Point(240, ClientSize.Height - 52), Result = DialogResult.OK };
        var cancel = new PillButton("取消", width: 96) { Location = new Point(134, ClientSize.Height - 52), Result = DialogResult.Cancel };
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = null;
        CancelButton = null;
    }

    internal int Result => _numeric != null ? (int)_numeric.Value : int.TryParse(_TextResult, out var v) ? v : 0;

    internal string _TextResult => _text?.Text.Trim() ?? string.Empty;

    internal string TextResult => _TextResult;
}
