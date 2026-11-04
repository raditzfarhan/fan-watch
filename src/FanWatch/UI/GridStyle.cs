using System.Drawing.Drawing2D;

namespace FanWatch.UI;

/// <summary>Shared look for FanWatch's tables: everything is painted here, the grid only supplies rows and columns.</summary>
internal static class GridStyle
{
    public static void Apply(DataGridView g)
    {
        g.Dock = DockStyle.Fill;
        g.VirtualMode = true;
        g.ReadOnly = true;
        g.AllowUserToAddRows = g.AllowUserToDeleteRows = g.AllowUserToResizeRows = false;
        g.AllowUserToResizeColumns = false;
        g.RowHeadersVisible = false;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = false;
        g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.None;
        g.GridColor = Theme.TableLine;
        g.BackgroundColor = Theme.Table;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = Theme.S(36);
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Table, ForeColor = Theme.Muted, Font = Theme.Caption,
            SelectionBackColor = Theme.Table, SelectionForeColor = Theme.Muted,
        };
        g.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Table, ForeColor = Theme.Text, Font = Theme.Body,
            SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Text,
        };
        g.RowTemplate.Height = Theme.S(46);
        g.ShowCellToolTips = true;
    }

    public static void AddColumn(DataGridView g, string header, float weight, DataGridViewContentAlignment align, bool sortable = true)
    {
        var col = new DataGridViewTextBoxColumn
        {
            HeaderText = header, FillWeight = weight, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = sortable ? DataGridViewColumnSortMode.Programmatic : DataGridViewColumnSortMode.NotSortable,
        };
        col.DefaultCellStyle.Alignment = align;
        col.HeaderCell.Style.Alignment = align;
        g.Columns.Add(col);
    }

    /// <summary>Header cell: small capitals, muted, with a sort arrow on the sorted column.</summary>
    public static void PaintHeader(DataGridViewCellPaintingEventArgs e, DataGridView grid)
    {
        var g = e.Graphics!;
        var b = e.CellBounds;
        using (var bg = new SolidBrush(Theme.Table)) g.FillRectangle(bg, b);
        using (var line = new Pen(Theme.TableLine)) g.DrawLine(line, b.Left, b.Bottom - 1, b.Right, b.Bottom - 1);
        var col = grid.Columns[e.ColumnIndex];
        string text = col.HeaderText.ToUpperInvariant();
        bool right = col.HeaderCell.Style.Alignment == DataGridViewContentAlignment.MiddleRight;
        var r = Rectangle.FromLTRB(b.Left + Theme.S(14), b.Top, b.Right - Theme.S(14), b.Bottom);
        var sort = col.HeaderCell.SortGlyphDirection;
        if (sort != SortOrder.None)
        {
            string glyph = sort == SortOrder.Descending ? Theme.GlyphChevronDown : Theme.GlyphChevronUp;
            int tw = TextRenderer.MeasureText(g, text, Theme.Caption, Size.Empty, TextFormatFlags.NoPadding).Width;
            int gx = right ? r.Right - tw - Theme.S(14) : r.Left + tw + Theme.S(4);
            Theme.Text_(g, glyph, Theme.SmallIcons, Theme.Text, new Rectangle(gx, r.Top, Theme.S(12), r.Height));
        }
        Theme.Text_(g, text, Theme.Caption, sort != SortOrder.None ? Theme.Text : Theme.Muted, r,
            (right ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter);
        e.Handled = true;
    }

    /// <summary>Row background and its separator line.</summary>
    public static void PaintRow(Graphics g, Rectangle b, Color back)
    {
        using (var bg = new SolidBrush(back)) g.FillRectangle(bg, b);
        using var line = new Pen(Theme.TableLine);
        g.DrawLine(line, b.Left, b.Bottom - 1, b.Right, b.Bottom - 1);
    }

    /// <summary>A rounded bar for a percentage, with its value to the right.</summary>
    public static void PaintBar(Graphics g, Rectangle b, double? value, double scale, string text, Color color, Color textColor, int alpha = 255)
    {
        int textW = Theme.S(50);
        var tr = new Rectangle(b.Right - textW - Theme.S(14), b.Top, textW, b.Height);
        Theme.Text_(g, text, Theme.Body, textColor, tr, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        int left = b.Left + Theme.S(14), right = tr.Left - Theme.S(10);
        if (right - left < Theme.S(20)) return;
        float h = Theme.S(6f);
        var track = new RectangleF(left, b.Top + (b.Height - h) / 2, right - left, h);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillRounded(g, Theme.Track, track, h / 2);
        if (value is double v && v > 0)
        {
            float w = Math.Max(h, (float)Math.Min(1, v / scale) * track.Width);
            Theme.FillRounded(g, Color.FromArgb(alpha, color), new RectangleF(track.X, track.Y, w, h), h / 2);
        }
        g.SmoothingMode = old;
    }

    /// <summary>Bar colour by how much of the machine a process uses.</summary>
    public static Color Heat(double? v) => v switch { >= 8 => Theme.Coral, >= 2.5 => Theme.Peach, _ => Theme.Slate };

    /// <summary>The End / Stop pill in a cell; returns its bounds for hit testing.</summary>
    public static Rectangle PaintPill(Graphics g, Rectangle b, string text, bool hover)
    {
        int w = TextRenderer.MeasureText(text, Theme.Small).Width + Theme.S(28), h = Theme.S(26);
        var r = new Rectangle(b.Right - w - Theme.S(14), b.Top + (b.Height - h) / 2, w, h);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
        Theme.FillRounded(g, hover ? Theme.EndHover : Theme.EndBack, rf, rf.Height / 2);
        Theme.DrawRounded(g, Theme.EndBorder, rf, rf.Height / 2);
        g.SmoothingMode = old;
        Theme.Text_(g, text, Theme.Small, Theme.CulpritText, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return r;
    }

    /// <summary>A title over a smaller muted subtitle.</summary>
    public static void PaintTwoLine(Graphics g, Rectangle r, string title, Font titleFont, Color titleColor, string? subtitle, Color subColor)
    {
        if (string.IsNullOrEmpty(subtitle))
        {
            Theme.Text_(g, title, titleFont, titleColor, r);
            return;
        }
        int th = Theme.S(18), sh = Theme.S(16), top = r.Top + (r.Height - th - sh) / 2;
        Theme.Text_(g, title, titleFont, titleColor, new Rectangle(r.Left, top, r.Width, th));
        Theme.Text_(g, subtitle, Theme.Small, subColor, new Rectangle(r.Left, top + th, r.Width, sh));
    }
}

/// <summary>DataGridView with double buffering (no flicker on 2 s refreshes) and dark scroll bars.</summary>
internal sealed class BufferedGrid : DataGridView
{
    public BufferedGrid() => DoubleBuffered = true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        foreach (ScrollBar bar in new ScrollBar[] { VerticalScrollBar, HorizontalScrollBar })
            _ = SetWindowTheme(bar.Handle, "DarkMode_Explorer", null);
    }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);
}
