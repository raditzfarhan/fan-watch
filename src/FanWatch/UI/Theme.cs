using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace FanWatch.UI;

internal static class Theme
{
    private static Color C(string hex) => ColorTranslator.FromHtml(hex);

    // Surfaces
    public static readonly Color Background = C("#1A1B1E");
    public static readonly Color Panel = C("#26282C");
    public static readonly Color Table = C("#222327");
    public static readonly Color TableLine = C("#2C2E33");
    public static readonly Color Hover = C("#2A2C31");
    public static readonly Color Selection = C("#31343A");
    public static readonly Color Track = C("#34373D");
    public static readonly Color Culprit = C("#2E2123");
    public static readonly Color CulpritHover = C("#36262A");
    public static readonly Color Secondary = C("#2E2820");
    public static readonly Color SecondaryHover = C("#362F25");
    public static readonly Color VerdictTop = C("#3A1F1F");
    public static readonly Color VerdictBorder = C("#4A2A2A");

    // Text
    public static readonly Color Text = C("#E6E6E6");
    public static readonly Color Bright = C("#FFFFFF");
    public static readonly Color Dim = C("#B9BBC2");
    public static readonly Color Muted = C("#9A9CA3");
    public static readonly Color CulpritText = C("#FFB4AB");
    public static readonly Color VerdictMuted = C("#D7C3C1");

    // Accents
    public static readonly Color Coral = C("#FF8A80");
    public static readonly Color OnCoral = C("#2A1212");
    public static readonly Color Peach = C("#F5A97F");
    public static readonly Color Slate = C("#7F8FA6");
    public static readonly Color Blue = C("#8AB4FF");
    public static readonly Color Green = C("#9FD8A6");
    public static readonly Color Amber = C("#FFD38A");
    public static readonly Color AmberBack = C("#5A4320");
    public static readonly Color EndBack = C("#3A2222");
    public static readonly Color EndHover = C("#4A2A2A");
    public static readonly Color EndBorder = C("#6B3A38");

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Bold = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.25f);
    public static readonly Font Caption = new("Segoe UI Semibold", 7.75f);
    public static readonly Font Title = new("Segoe UI Semibold", 15f);
    public static readonly Font Big = new("Segoe UI", 26f, FontStyle.Bold);
    public static readonly Font Letter = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font BigLetter = new("Segoe UI", 16f, FontStyle.Bold);

    /// <summary>Windows' icon font (Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10).</summary>
    public static readonly Font Icons = IconFont(9f);
    public static readonly Font SmallIcons = IconFont(7.5f);

    public const string GlyphChevronRight = "", GlyphChevronDown = "", GlyphChevronUp = "",
        GlyphLock = "", GlyphSearch = "", GlyphInfo = "", GlyphWarning = "", GlyphPower = "";

    private static Font IconFont(float size)
    {
        var f = new Font("Segoe Fluent Icons", size);
        if (f.Name == "Segoe Fluent Icons") return f;
        f.Dispose();
        return new Font("Segoe MDL2 Assets", size);
    }

    /// <summary>The screen's scale (FanWatch is system-DPI aware): pixel sizes go through <see cref="S"/>.</summary>
    public static float Scale { get; set; } = 1f;
    public static int S(int px) => (int)Math.Round(px * Scale);
    public static float S(float px) => px * Scale;

    private static readonly Color[] TileColors =
        [C("#D97757"), C("#FB8C5A"), C("#6FA8DC"), C("#8CC84B"), C("#4FA3E8"), C("#E8C468"), C("#B48EDB"), C("#5CC8B5")];

    /// <summary>A stable colour per program name, for the letter tile shown when its icon can't be read.</summary>
    public static Color TileColor(string name)
    {
        int h = 0;
        foreach (char ch in name.ToLowerInvariant()) h = unchecked(h * 31 + ch);
        return TileColors[(h & 0x7fffffff) % TileColors.Length];
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRounded(Graphics g, Brush b, RectangleF r, float radius)
    {
        using var p = Rounded(r, radius);
        g.FillPath(b, p);
    }

    public static void FillRounded(Graphics g, Color c, RectangleF r, float radius)
    {
        using var b = new SolidBrush(c);
        FillRounded(g, b, r, radius);
    }

    public static void DrawRounded(Graphics g, Color c, RectangleF r, float radius, float width = 1)
    {
        using var p = Rounded(r, radius);
        using var pen = new Pen(c, width);
        g.DrawPath(pen, p);
    }

    /// <summary>Crisp single-line text; ellipsis when it doesn't fit.</summary>
    public static void Text_(Graphics g, string text, Font font, Color color, Rectangle r,
        TextFormatFlags align = TextFormatFlags.Left | TextFormatFlags.VerticalCenter) =>
        TextRenderer.DrawText(g, text, font, r, color,
            align | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

    /// <summary>A program's icon, or a coloured tile with its first letter.</summary>
    public static void DrawAppIcon(Graphics g, Bitmap? icon, string name, Rectangle r)
    {
        if (icon != null)
        {
            var old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(icon, r);
            g.InterpolationMode = old;
            return;
        }
        var smooth = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        FillRounded(g, TileColor(name), r, r.Width * 0.27f);
        g.SmoothingMode = smooth;
        string letter = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?";
        TextRenderer.DrawText(g, letter, r.Width >= S(36) ? BigLetter : Letter, r, Background,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>Dark title bar on Windows 10 20H1+ / 11.</summary>
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        _ = DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
