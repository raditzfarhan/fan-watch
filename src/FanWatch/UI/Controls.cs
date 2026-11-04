using System.Drawing.Drawing2D;
using FanWatch.Core;

namespace FanWatch.UI;

/// <summary>Program icons, read once per program name from its exe. Null when the exe can't be read (system processes).</summary>
internal static class AppIcons
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? For(ProcSample p)
    {
        if (Cache.TryGetValue(p.Name, out var bmp)) return bmp;
        try
        {
            var path = ProcessImage.PathOf(p.Pid);
            using var icon = path != null ? Icon.ExtractAssociatedIcon(path) : null;
            bmp = icon?.ToBitmap();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { bmp = null; }
        // Only cache misses for processes we can never open; a transient failure would hide the icon forever otherwise.
        if (bmp != null || p.SessionId == 0) Cache[p.Name] = bmp;
        return bmp;
    }
}

/// <summary>Base for FanWatch's owner-drawn controls.</summary>
internal abstract class PaintedControl : Control
{
    protected PaintedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }
}

/// <summary>A rounded "pill" button: filled (primary) or outlined (secondary).</summary>
internal sealed class PillButton : Button
{
    private bool _hover;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Theme.Coral;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color HoverFill { get; set; } = ControlPaint.Light(Theme.Coral, 0.2f);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Border { get; set; } = Color.Empty;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color TextColor { get; set; } = Theme.OnCoral;

    public PillButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = Theme.Bold;
        Height = Theme.S(34);
    }

    public void FitText() => Width = TextRenderer.MeasureText(Text, Font).Width + Theme.S(36);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        OnPaintBackground(e);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var fill = _hover ? HoverFill : Fill;
        if (fill.A > 0) Theme.FillRounded(g, fill, r, r.Height / 2);
        if (!Border.IsEmpty) Theme.DrawRounded(g, Border, r, r.Height / 2);
        if (Focused && ShowFocusCues)
            Theme.DrawRounded(g, Theme.Blue, RectangleF.Inflate(r, -2, -2), (r.Height - 4) / 2, 1.5f);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, TextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>What the big card at the top says.</summary>
internal sealed record Verdict(
    bool Loud, string Heading, string Title, string Detail, string Percent, string PercentCaption,
    IReadOnlyList<double> Trend, Bitmap? Icon, string? IconName, string? Primary, string? Secondary);

/// <summary>"Why your fan is loud": the culprit's name, why, its CPU over the last minute, and what you can do.</summary>
internal sealed class VerdictCard : PaintedControl
{
    private Verdict? _v;
    public PillButton PrimaryButton { get; } = new();
    public PillButton SecondaryButton { get; } = new()
    {
        Fill = Color.Transparent, HoverFill = Color.FromArgb(40, 255, 255, 255), Border = Color.FromArgb(0x5A, 0x3B, 0x3B),
        TextColor = Color.FromArgb(0xF2, 0xDC, 0xDA), Font = Theme.Body,
    };

    public VerdictCard()
    {
        Controls.Add(PrimaryButton);
        Controls.Add(SecondaryButton);
    }

    private bool Wide => Width >= Theme.S(640);
    private int RightWidth => Wide ? Theme.S(300) : 0;

    public void Show(Verdict v)
    {
        _v = v;
        PrimaryButton.Visible = v.Primary != null;
        if (v.Primary != null) { PrimaryButton.Text = v.Primary; PrimaryButton.FitText(); }
        SecondaryButton.Visible = v.Secondary != null;
        if (v.Secondary != null) { SecondaryButton.Text = v.Secondary; SecondaryButton.FitText(); }
        SecondaryButton.Border = v.Loud ? Color.FromArgb(0x5A, 0x3B, 0x3B) : Theme.Track;
        SecondaryButton.TextColor = v.Loud ? Color.FromArgb(0xF2, 0xDC, 0xDA) : Theme.Text;
        PerformLayout();
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int x = Theme.S(22), y = Height - Theme.S(20) - PrimaryButton.Height;
        if (PrimaryButton.Visible) { PrimaryButton.Location = new Point(x, y); x += PrimaryButton.Width + Theme.S(10); }
        SecondaryButton.Location = new Point(x, y);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Smooth(g);
        g.Clear(Parent?.BackColor ?? Theme.Background);
        if (_v is not Verdict v) return;
        var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = Theme.S(14f);
        if (v.Loud)
        {
            using var grad = new LinearGradientBrush(new PointF(0, 0), new PointF(Width, Height), Theme.VerdictTop, Theme.Panel);
            Theme.FillRounded(g, grad, card, radius);
            Theme.DrawRounded(g, Theme.VerdictBorder, card, radius);
        }
        else Theme.FillRounded(g, Theme.Panel, card, radius);

        int pad = Theme.S(22);
        int left = pad, right = Width - pad - RightWidth - (Wide ? Theme.S(20) : 0);

        // Heading: flame (loud) or a green dot (quiet), in small capitals.
        var accent = v.Loud ? Theme.Coral : Theme.Green;
        int hy = Theme.S(18);
        if (v.Loud) Flame(g, new RectangleF(left, hy, Theme.S(13f), Theme.S(15f)), accent);
        else using (var b = new SolidBrush(accent)) g.FillEllipse(b, left + Theme.S(2), hy + Theme.S(4), Theme.S(8), Theme.S(8));
        Theme.Text_(g, v.Heading.ToUpperInvariant(), Theme.Caption, v.Loud ? Theme.CulpritText : Theme.Green,
            new Rectangle(left + Theme.S(20), hy, right - left, Theme.S(16)));

        // Icon, name and why.
        int iy = Theme.S(46), icon = Theme.S(44), tx = left;
        if (v.IconName != null)
        {
            Theme.DrawAppIcon(g, v.Icon, v.IconName, new Rectangle(left, iy, icon, icon));
            tx = left + icon + Theme.S(14);
        }
        Theme.Text_(g, v.Title, Theme.Title, Theme.Bright, new Rectangle(tx, iy - Theme.S(2), right - tx, Theme.S(28)));
        // Up to two lines: a hint plus "Windows needs it…" doesn't fit on one.
        TextRenderer.DrawText(g, v.Detail, Theme.Body, new Rectangle(tx, iy + Theme.S(27), right - tx, Theme.S(40)),
            v.Loud ? Theme.VerdictMuted : Theme.Dim, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        if (!Wide) return;
        // Right: the big number and a one-minute line chart.
        int rx = Width - pad - RightWidth;
        var size = TextRenderer.MeasureText(g, v.Percent, Theme.Big, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, v.Percent, Theme.Big, new Point(rx, Theme.S(14)), v.Loud ? Theme.Coral : Theme.Green, TextFormatFlags.NoPadding);
        Theme.Text_(g, v.PercentCaption, Theme.Small, v.Loud ? Theme.VerdictMuted : Theme.Dim,
            new Rectangle(rx + size.Width + Theme.S(8), Theme.S(14) + size.Height - Theme.S(22), RightWidth - size.Width - Theme.S(8), Theme.S(18)));
        var chart = new RectangleF(rx, Theme.S(68), RightWidth, Height - Theme.S(68) - Theme.S(40));
        Sparkline(g, v.Trend, chart, accent, slots: 30);
        var axis = v.Loud ? Color.FromArgb(0xB8, 0xA3, 0xA1) : Theme.Muted;
        Theme.Text_(g, "1 min ago", Theme.Small, axis, new Rectangle(rx, (int)chart.Bottom + Theme.S(6), RightWidth / 2, Theme.S(16)));
        Theme.Text_(g, "now", Theme.Small, axis, new Rectangle(rx + RightWidth / 2, (int)chart.Bottom + Theme.S(6), RightWidth / 2, Theme.S(16)),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    /// <summary>A filled line chart; values are right-aligned so a fresh start grows in from the right.</summary>
    public static void Sparkline(Graphics g, IReadOnlyList<double> values, RectangleF r, Color color, int slots)
    {
        using (var basePen = new Pen(Color.FromArgb(60, color), 1)) g.DrawLine(basePen, r.Left, r.Bottom, r.Right, r.Bottom);
        if (values.Count < 2) return;
        double max = Math.Max(values.Max() * 1.25, 2);
        float step = r.Width / (slots - 1);
        int n = Math.Min(values.Count, slots);
        var pts = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            double v = values[values.Count - n + i];
            pts[i] = new PointF(r.Right - (n - 1 - i) * step, r.Bottom - (float)(v / max) * r.Height);
        }
        using (var area = new GraphicsPath())
        {
            area.AddLines(pts);
            area.AddLine(pts[^1], new PointF(pts[^1].X, r.Bottom));
            area.AddLine(new PointF(pts[^1].X, r.Bottom), new PointF(pts[0].X, r.Bottom));
            area.CloseFigure();
            using var fill = new LinearGradientBrush(new PointF(0, r.Top - 1), new PointF(0, r.Bottom + 1),
                Color.FromArgb(110, color), Color.FromArgb(0, color));
            g.FillPath(fill, area);
        }
        using var pen = new Pen(color, Theme.S(2f)) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, pts);
    }

    private static void Flame(Graphics g, RectangleF r, Color c)
    {
        using var p = new GraphicsPath();
        float X(float v) => r.X + v / 16 * r.Width;
        float Y(float v) => r.Y + v / 16 * r.Height;
        p.AddBezier(X(8), Y(0), X(9), Y(4), X(14), Y(6), X(14), Y(10.5f));
        p.AddBezier(X(14), Y(10.5f), X(14), Y(14), X(11), Y(16), X(8), Y(16));
        p.AddBezier(X(8), Y(16), X(5), Y(16), X(2), Y(14), X(2), Y(10.5f));
        p.AddBezier(X(2), Y(10.5f), X(2), Y(8), X(3.5f), Y(6.5f), X(5), Y(5.5f));
        p.AddBezier(X(5), Y(5.5f), X(5), Y(8), X(6.5f), Y(9), X(7.5f), Y(9));
        p.AddBezier(X(7.5f), Y(9), X(7.5f), Y(6), X(6), Y(3.5f), X(8), Y(0));
        p.CloseFigure();
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }
}

/// <summary>CPU and memory bars plus power state.</summary>
internal sealed class StatsCard : PaintedControl
{
    private double _cpu, _mem;
    private string _cpuText = "…", _memText = "…";
    private string? _power;

    public void Show(double cpuFraction, string cpuText, double memFraction, string memText, string? power)
    {
        (_cpu, _cpuText, _mem, _memText, _power) = (cpuFraction, cpuText, memFraction, memText, power);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Smooth(g);
        g.Clear(Parent?.BackColor ?? Theme.Background);
        Theme.FillRounded(g, Theme.Panel, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(14f));
        int pad = Theme.S(20), w = Width - 2 * pad, y = Theme.S(20);
        Bar(g, "CPU", _cpuText, _cpu, Theme.Peach, pad, y, w);
        Bar(g, "Memory", _memText, _mem, Theme.Blue, pad, y + Theme.S(52), w);
        if (_power != null)
        {
            int py = y + Theme.S(108);
            bool charging = _power.StartsWith("Charging", StringComparison.Ordinal) || _power.StartsWith("Plugged", StringComparison.Ordinal);
            Theme.Text_(g, Theme.GlyphPower, Theme.Icons, charging ? Theme.Green : Theme.Peach, new Rectangle(pad, py, Theme.S(18), Theme.S(18)));
            int split = _power.LastIndexOf(' ');
            Theme.Text_(g, split > 0 ? _power[..split] : _power, Theme.Body, Theme.Dim, new Rectangle(pad + Theme.S(24), py, w, Theme.S(18)));
            if (split > 0)
                Theme.Text_(g, _power[(split + 1)..], Theme.Bold, Theme.Text, new Rectangle(pad, py, w, Theme.S(18)),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    private static void Bar(Graphics g, string label, string value, double fraction, Color color, int x, int y, int w)
    {
        Theme.Text_(g, label, Theme.Body, Theme.Dim, new Rectangle(x, y, w, Theme.S(18)));
        Theme.Text_(g, value, Theme.Bold, Theme.Text, new Rectangle(x, y, w, Theme.S(18)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        var track = new RectangleF(x, y + Theme.S(26), w, Theme.S(8));
        Theme.FillRounded(g, Theme.Track, track, track.Height / 2);
        float fw = (float)Math.Clamp(fraction, 0, 1) * w;
        if (fw >= track.Height) Theme.FillRounded(g, color, new RectangleF(x, track.Y, fw, track.Height), track.Height / 2);
    }
}

/// <summary>The Processes / Ports switch, with an amber badge when servers look left on.</summary>
internal sealed class SegmentedTabs : PaintedControl
{
    private readonly string[] _labels = ["Processes", "Ports"];
    private Rectangle[] _segments = [];
    private int _hover = -1;
    public int Selected { get; private set; }
    public string? Badge { get; private set; }
    public event Action<int>? SelectedChanged;

    public SegmentedTabs()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        Height = Theme.S(36);
        AccessibleRole = AccessibleRole.PageTabList;
        Measure();
    }

    public void Select(int index) { Selected = index; Invalidate(); }

    public void SetBadge(string? badge)
    {
        if (badge == Badge) return;
        Badge = badge;
        Measure();
        Invalidate();
    }

    private void Measure()
    {
        int x = Theme.S(3);
        var list = new List<Rectangle>();
        for (int i = 0; i < _labels.Length; i++)
        {
            int w = TextRenderer.MeasureText(_labels[i], Theme.Bold).Width + Theme.S(32);
            if (i == 1 && Badge != null) w += TextRenderer.MeasureText(Badge, Theme.Caption).Width + Theme.S(22);
            list.Add(new Rectangle(x, Theme.S(3), w, Height - Theme.S(6)));
            x += w;
        }
        _segments = [.. list];
        Width = x + Theme.S(3);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = Array.FindIndex(_segments, s => s.Contains(e.Location));
        if (h != _hover) { _hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int i = Array.FindIndex(_segments, s => s.Contains(e.Location));
        if (i >= 0) Choose(i);
    }

    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) Choose(0);
        else if (e.KeyCode == Keys.Right) Choose(1);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    private void Choose(int i)
    {
        if (i == Selected) return;
        Selected = i;
        Invalidate();
        SelectedChanged?.Invoke(i);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Smooth(g);
        g.Clear(Parent?.BackColor ?? Theme.Background);
        Theme.FillRounded(g, Theme.Panel, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(10f));
        for (int i = 0; i < _segments.Length; i++)
        {
            var s = _segments[i];
            bool on = i == Selected;
            if (on || i == _hover) Theme.FillRounded(g, on ? Color.FromArgb(0x3A, 0x3D, 0x44) : Theme.Hover, s, Theme.S(8f));
            if (on && Focused && ShowFocusCues) Theme.DrawRounded(g, Theme.Blue, RectangleF.Inflate(s, -1, -1), Theme.S(7f), 1.5f);
            var text = new Rectangle(s.X + Theme.S(16), s.Y, s.Width, s.Height);
            Theme.Text_(g, _labels[i], on ? Theme.Bold : Theme.Body, on ? Theme.Bright : Theme.Dim, text);
            if (i == 1 && Badge != null)
            {
                int tw = TextRenderer.MeasureText(_labels[i], on ? Theme.Bold : Theme.Body).Width;
                int bw = TextRenderer.MeasureText(Badge, Theme.Caption).Width + Theme.S(14);
                var badge = new Rectangle(text.X + tw + Theme.S(8), s.Y + (s.Height - Theme.S(18)) / 2, bw, Theme.S(18));
                Theme.FillRounded(g, Theme.AmberBack, badge, badge.Height / 2f);
                Theme.Text_(g, Badge, Theme.Caption, Theme.Amber, badge, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}

/// <summary>A rounded search field.</summary>
internal sealed class SearchBox : PaintedControl
{
    public TextBox Box { get; } = new()
    {
        BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.Body,
        PlaceholderText = "Find a process",
    };

    public SearchBox()
    {
        Height = Theme.S(36);
        Controls.Add(Box);
        Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { Box.Clear(); e.SuppressKeyPress = true; } };
        Cursor = Cursors.IBeam;
        Click += (_, _) => Box.Focus();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int x = Theme.S(34);
        Box.SetBounds(x, (Height - Box.PreferredHeight) / 2, Width - x - Theme.S(12), Box.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Smooth(g);
        g.Clear(Parent?.BackColor ?? Theme.Background);
        Theme.FillRounded(g, Theme.Panel, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(10f));
        Theme.Text_(g, Theme.GlyphSearch, Theme.Icons, Theme.Muted, new Rectangle(Theme.S(12), 0, Theme.S(16), Height));
    }
}

/// <summary>A rounded dark surface that holds a table.</summary>
internal sealed class CardPanel : Panel
{
    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Table;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillRounded(g, Theme.Table, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(12f));
    }
}
