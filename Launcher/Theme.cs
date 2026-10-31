using System.Drawing.Drawing2D;

namespace BotLauncher;

// Dark palette + shared drawing helpers. Every control paints through these so the whole app
// stays on one set of colours and radii.
internal static class Theme
{
    internal static readonly Color Bg = Color.FromArgb(13, 16, 21);
    internal static readonly Color Card = Color.FromArgb(22, 26, 33);
    internal static readonly Color CardHi = Color.FromArgb(29, 34, 43);
    internal static readonly Color Border = Color.FromArgb(38, 44, 56);
    internal static readonly Color BorderHi = Color.FromArgb(58, 68, 86);
    internal static readonly Color Text = Color.FromArgb(232, 236, 244);
    internal static readonly Color Muted = Color.FromArgb(138, 148, 166);
    internal static readonly Color Dim = Color.FromArgb(96, 106, 124);
    internal static readonly Color Accent = Color.FromArgb(91, 140, 255);
    internal static readonly Color Accent2 = Color.FromArgb(139, 92, 246);
    internal static readonly Color Ok = Color.FromArgb(74, 222, 128);
    internal static readonly Color Warn = Color.FromArgb(251, 191, 36);
    internal static readonly Color Bad = Color.FromArgb(248, 113, 113);

    internal static readonly Font H1 = new("Microsoft YaHei UI", 15F, FontStyle.Bold);
    internal static readonly Font H2 = new("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
    internal static readonly Font Body = new("Microsoft YaHei UI", 9.5F);
    internal static readonly Font Small = new("Microsoft YaHei UI", 8.5F);
    internal static readonly Font Mono = new("Consolas", 10.5F, FontStyle.Bold);
    internal static readonly Font Num = new("Consolas", 12F, FontStyle.Bold);

    // Layout is derived from measured text. Fixed pixel boxes do not survive DPI scaling.
    internal static int SingleLine(Font font, string text) =>
        TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Height;

    internal static int Wrapped(Font font, string text, int width) =>
        TextRenderer.MeasureText(text, font, new Size(Math.Max(40, width), int.MaxValue),
            TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak).Height;

    internal static int TextWidth(Font font, string text) =>
        TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;

    internal static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0)
            return path;

        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void FillRound(Graphics g, Rectangle r, int radius, Color color)
    {
        using var path = RoundRect(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    internal static void StrokeRound(Graphics g, Rectangle r, int radius, Color color, float width = 1F)
    {
        using var path = RoundRect(r, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    internal static LinearGradientBrush AccentBrush(Rectangle r)
    {
        var brush = new LinearGradientBrush(
            new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)),
            Accent,
            Accent2,
            LinearGradientMode.Horizontal);
        return brush;
    }

    // Renders a "card" surface: soft shadow, rounded body, hairline border.
    internal static void DrawCard(Graphics g, Rectangle r, bool hot = false)
    {
        FillRound(g, r, 12, hot ? CardHi : Card);
        StrokeRound(g, r, 12, hot ? BorderHi : Border);
    }

    internal static void DrawText(Graphics g, string text, Font font, Color color, Point at, StringFormat? format = null)
    {
        TextRenderer.DrawText(g, text, font, at, color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    internal static void DrawDot(Graphics g, Rectangle cell, Color color)
    {
        using var brush = new SolidBrush(color);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillEllipse(brush, cell);
    }
}
