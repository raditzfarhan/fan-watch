using FanWatch.Core;

namespace FanWatch.UI;

/// <summary>The "Ports" tab: every program of yours listening on a port, with likely-leftover dev servers first.</summary>
internal sealed class PortsView : UserControl
{
    private const int ColPort = 0, ColProcess = 1, ColProject = 2, ColStartedBy = 3, ColAge = 4, ColCommand = 5, ColStop = 6;

    private readonly BufferedGrid _grid = new();
    private readonly ProcessKiller _killer;
    private readonly Action<string> _flash;
    private readonly Action _refreshNow;
    private IReadOnlyList<PortRow> _rows = [];
    private bool _busy;
    private int _hoverRow = -1, _hoverCol = -1;

    public PortsView(ProcessKiller killer, Action<string> flash, Action refreshNow)
    {
        _killer = killer;
        _flash = flash;
        _refreshNow = refreshNow;
        Dock = DockStyle.Fill;
        BackColor = Theme.Table;

        GridStyle.Apply(_grid);
        GridStyle.AddColumn(_grid, "Port", 11, DataGridViewContentAlignment.MiddleLeft, sortable: false);
        GridStyle.AddColumn(_grid, "Process", 11, DataGridViewContentAlignment.MiddleLeft, sortable: false);
        GridStyle.AddColumn(_grid, "Project", 13, DataGridViewContentAlignment.MiddleLeft, sortable: false);
        GridStyle.AddColumn(_grid, "Started by", 15, DataGridViewContentAlignment.MiddleLeft, sortable: false);
        GridStyle.AddColumn(_grid, "Running for", 10, DataGridViewContentAlignment.MiddleRight, sortable: false);
        GridStyle.AddColumn(_grid, "Command", 33, DataGridViewContentAlignment.MiddleLeft, sortable: false);
        GridStyle.AddColumn(_grid, "", 8, DataGridViewContentAlignment.MiddleRight, sortable: false);

        _grid.CellValueNeeded += OnValue;
        _grid.CellPainting += OnPaintCell;
        _grid.CellToolTipTextNeeded += OnToolTip;
        _grid.CellClick += OnClick;
        _grid.CellMouseEnter += (_, e) => SetHover(e.RowIndex, e.ColumnIndex);
        _grid.MouseLeave += (_, _) => SetHover(-1, -1);
        Controls.Add(_grid);
    }

    public void Show(IReadOnlyList<PortRow> rows)
    {
        // Keep the selection on the same program across refreshes, without scrolling.
        ProcKey? selected = _grid.CurrentCell != null && _grid.CurrentCell.RowIndex < _rows.Count ? _rows[_grid.CurrentCell.RowIndex].Proc.Key : null;
        int firstVisible = _grid.RowCount > 0 ? _grid.FirstDisplayedScrollingRowIndex : 0;

        _rows = rows;
        if (_grid.RowCount != rows.Count) _grid.RowCount = rows.Count;
        _grid.Invalidate();
        if (rows.Count == 0) return;

        int sel = selected is ProcKey k ? rows.ToList().FindIndex(r => r.Proc.Key == k) : -1;
        if (sel >= 0) _grid.CurrentCell = _grid[ColPort, sel];
        else _grid.ClearSelection();
        if (firstVisible >= 0 && firstVisible < rows.Count) _grid.FirstDisplayedScrollingRowIndex = firstVisible;
    }

    public static string Summary(IReadOnlyList<PortRow> rows)
    {
        var left = rows.Where(r => r.LikelyLeftover).ToList();
        int ports = rows.Sum(r => r.Ports.Count);
        if (left.Count == 0) return $"No leftover dev servers. {ports} port{(ports == 1 ? "" : "s")} in use by your programs.";
        var list = string.Join(", ", left.Take(4).Select(r => $"{PortText(r)} ({r.Project ?? r.Proc.Name})"));
        return $"{left.Count} dev server{(left.Count == 1 ? " looks" : "s look")} left over: {list}{(left.Count > 4 ? ", …" : "")}";
    }

    private static string PortText(PortRow r) => r.Ports.Count <= 2 ? string.Join(", ", r.Ports) : $"{r.Ports[0]} +{r.Ports.Count - 1}";

    private static string StartedByText(PortRow r) => r.StartedBy ?? (r.Orphaned ? "nothing (launcher closed)" : "—");

    private static string Age(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h" : t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} m" : $"{Math.Max(0, (int)t.TotalMinutes)} m";

    private void OnValue(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex >= _rows.Count) return;
        var r = _rows[e.RowIndex];
        e.Value = e.ColumnIndex switch
        {
            ColPort => PortText(r),
            ColProcess => r.Proc.Name,
            ColProject => r.Project ?? "",
            ColStartedBy => r.IsDevServer ? StartedByText(r) : "",
            ColAge => Age(DateTime.UtcNow - r.Proc.StartUtc),
            ColCommand => r.Command ?? "",
            ColStop => r.CanStop ? "Stop" : "",
            _ => "",
        };
    }

    private void SetHover(int row, int col)
    {
        if (row == _hoverRow && col == _hoverCol) return;
        int old = _hoverRow;
        (_hoverRow, _hoverCol) = (row, col);
        if (old >= 0 && old < _grid.RowCount) _grid.InvalidateRow(old);
        if (row >= 0 && row < _grid.RowCount) _grid.InvalidateRow(row);
        _grid.Cursor = IsStop(row, col) ? Cursors.Hand : Cursors.Default;
    }

    private void OnPaintCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        var g = e.Graphics!;
        if (e.RowIndex < 0) { if (e.ColumnIndex >= 0) GridStyle.PaintHeader(e, _grid); return; }
        if (e.RowIndex >= _rows.Count || e.ColumnIndex < 0) return;
        e.Handled = true;
        var r = _rows[e.RowIndex];
        var b = e.CellBounds;
        bool hover = e.RowIndex == _hoverRow, selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var back = r.LikelyLeftover ? (hover || selected ? Theme.SecondaryHover : Theme.Secondary)
                 : selected ? Theme.Selection : hover ? Theme.Hover : Theme.Table;
        GridStyle.PaintRow(g, b, back);
        var inner = Rectangle.FromLTRB(b.Left + Theme.S(14), b.Top, b.Right - Theme.S(10), b.Bottom);
        var color = r.IsDevServer ? Theme.Text : Theme.Muted;
        switch (e.ColumnIndex)
        {
            case ColPort:
                if (r.LikelyLeftover)
                {
                    Theme.Text_(g, Theme.GlyphWarning, Theme.SmallIcons, Theme.Amber, new Rectangle(inner.Left, b.Top, Theme.S(14), b.Height));
                    inner = Rectangle.FromLTRB(inner.Left + Theme.S(22), inner.Top, inner.Right, inner.Bottom);
                }
                Theme.Text_(g, PortText(r), Theme.Bold, r.LikelyLeftover ? Theme.Amber : color, inner);
                break;
            case ColProcess:
            {
                int size = Theme.S(20);
                Theme.DrawAppIcon(g, AppIcons.For(r.Proc), r.Proc.Name, new Rectangle(inner.Left, b.Top + (b.Height - size) / 2, size, size));
                Theme.Text_(g, r.Proc.Name, Theme.Body, color, Rectangle.FromLTRB(inner.Left + size + Theme.S(10), b.Top, inner.Right, b.Bottom));
                break;
            }
            case ColStop:
                if (r.CanStop) GridStyle.PaintPill(g, b, "Stop", hover && _hoverCol == ColStop);
                break;
            default:
                var align = _grid.Columns[e.ColumnIndex].DefaultCellStyle.Alignment == DataGridViewContentAlignment.MiddleRight
                    ? TextFormatFlags.Right : TextFormatFlags.Left;
                Theme.Text_(g, e.FormattedValue?.ToString() ?? "", Theme.Body, e.ColumnIndex == ColCommand ? Theme.Muted : color, inner,
                    align | TextFormatFlags.VerticalCenter);
                break;
        }
    }

    private void OnToolTip(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        var r = _rows[e.RowIndex];
        e.ToolTipText = e.ColumnIndex switch
        {
            ColPort => string.Join(", ", r.Ports) + (r.LoopbackOnly ? "  (this PC only)" : "  (reachable from your network)"),
            ColProcess => $"PID {r.Proc.Pid}",
            ColStartedBy when r.LikelyLeftover && r.StartedBy != null => "Started by this Claude session. If its job is done, the server can be stopped.",
            ColStartedBy when r.LikelyLeftover => "Whatever started this server has already exited, so nothing will ever stop it.",
            ColCommand => r.Command ?? "",
            ColStop when r.CanStop => $"Ends {r.StopTarget.Members.Count} process{(r.StopTarget.Members.Count == 1 ? "" : "es")}",
            _ => "",
        };
    }

    private bool IsStop(int row, int col) => col == ColStop && row >= 0 && row < _rows.Count && _rows[row].CanStop;

    private async void OnClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (!IsStop(e.RowIndex, e.ColumnIndex) || _busy) return;
        var target = _rows[e.RowIndex].StopTarget;
        var answer = MessageBox.Show(FindForm(), _killer.ConfirmText(target), "Stop server",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        _busy = true;
        try { _flash((await _killer.EndAsync(target, TimeSpan.FromSeconds(3))).Message); }
        finally { _busy = false; }
        _refreshNow();
    }
}
