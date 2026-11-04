using FanWatch.Core;

namespace FanWatch.UI;

/// <summary>
/// The single FanWatch window: a "why your fan is loud" card, CPU/memory/power, and a sortable, expandable process table
/// (or the Ports table).
/// </summary>
internal sealed class MainForm : Form
{
    private const int ColProcess = 0, ColNow = 1, ColAvg = 2, ColSince = 3, ColMem = 4, ColEnd = 5;
    private const string OverallKey = "*overall*";

    private readonly int _cores = Environment.ProcessorCount;
    private readonly NtProcessSource _source = new();
    private readonly CpuHistory _history;
    private readonly ClaudeSessions _claude = new(ClaudeSessions.DefaultDir);
    private readonly ProcessKiller _killer = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly TcpPortSource _ports = new();
    private readonly CommandLines _commands = new();
    private readonly CpuTrend _trend = new();
    private readonly PortsView _portsView;
    private bool _showPorts;
    private Row? _lastCulprit;
    private SystemSnapshot? _lastSys;
    private bool _lastFirst = true;

    // Which tab each browser process is, read from the browser's own Task Manager (Shift+Esc) while it is open.
    private const string OpenTaskManagerAction = "open-task-manager";
    private const string RestoreTaskManagerAction = "restore-task-manager";
    private readonly Dictionary<string, IntPtr> _taskManagerMinimized = new();
    private static readonly TimeSpan TasksEvery = TimeSpan.FromSeconds(3);
    private Dictionary<int, List<BrowserTask>> _browserTasks = new();
    private readonly Dictionary<int, string> _windowTitles = new();
    private readonly Dictionary<string, IntPtr> _browserMainWindow = new();
    private readonly HashSet<string> _taskManagerOpen = new();
    private DateTime _tasksReadAt = DateTime.MinValue;
    private bool _tasksReading;
    private IReadOnlyList<PortRow> _portRows = [];

    private readonly VerdictCard _verdict = new() { Dock = DockStyle.Fill };
    private readonly StatsCard _stats = new() { Dock = DockStyle.Right };
    private readonly Panel _statsGap = new() { Dock = DockStyle.Right };
    private readonly SegmentedTabs _tabs = new() { Dock = DockStyle.Left };
    private readonly SearchBox _search = new() { Dock = DockStyle.Right };
    private readonly Label _note = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
        Font = Theme.Body, ForeColor = Theme.Muted, BackColor = Theme.Background,
    };
    private readonly BufferedGrid _grid = new();
    private Row? _verdictTarget;   // what the card's End button ends
    private Row? _verdictShow;     // what "Show its processes" reveals

    private readonly HashSet<string> _expanded = new();
    private readonly HashSet<string> _autoExpanded = new();
    private List<ViewRow> _view = [];
    private IReadOnlyList<Row> _rows = [];
    private int _sortCol = -1;          // -1 = culprit score
    private bool _sortDesc = true;
    private double _barScale = 10;
    private int _hoverRow = -1, _hoverCol = -1;
    // Intervals use a monotonic clock so clock changes can't distort CPU %; a long gap (sleep, minimized) restarts sampling.
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(6);
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly DateTime _clockEpochUtc = DateTime.UtcNow;
    private TimeSpan? _lastSampleAt;
    private TimeSpan _lastIdle;
    private readonly System.Windows.Forms.Timer _settle = new() { Interval = 400 };
    private string? _flash;
    private DateTime _flashUntil;
    private string? _error;
    private bool _busyEnding;

    private sealed record ViewRow(Row Row, int Depth);

    public MainForm(bool startOnPorts = false, IEnumerable<string>? expandGroups = null)
    {
        foreach (var g in expandGroups ?? []) _expanded.Add("g:" + g.ToLowerInvariant());
        _history = new CpuHistory(_cores);
        Text = "FanWatch";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(Theme.S(1180), Theme.S(720));
        MinimumSize = new Size(Theme.S(760), Theme.S(560));
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        // Top: the verdict card and, on wide windows, the CPU / memory / power card.
        var top = new Panel { Dock = DockStyle.Top, Height = Theme.S(186), Padding = new Padding(Theme.S(24), Theme.S(20), Theme.S(24), Theme.S(14)), BackColor = Theme.Background };
        _stats.Width = Theme.S(250);
        _statsGap.Width = Theme.S(16);
        top.Controls.Add(_verdict);
        top.Controls.Add(_statsGap);
        top.Controls.Add(_stats);

        // Toolbar: tabs, a note line, search.
        var bar = new Panel { Dock = DockStyle.Top, Height = Theme.S(50), Padding = new Padding(Theme.S(24), 0, Theme.S(24), Theme.S(14)), BackColor = Theme.Background };
        _search.Width = Theme.S(230);
        _note.Padding = new Padding(Theme.S(14), 0, Theme.S(14), 0);
        bar.Controls.Add(_note);
        bar.Controls.Add(_search);
        bar.Controls.Add(_tabs);

        _portsView = new PortsView(_killer, Flash, RefreshAfterAction) { Visible = false };
        SetUpGrid();
        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(4), Theme.S(4), Theme.S(4), Theme.S(10)) };
        card.Controls.Add(_portsView);
        card.Controls.Add(_grid);
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(24), 0, Theme.S(24), Theme.S(20)), BackColor = Theme.Background };
        content.Controls.Add(card);

        Controls.Add(content);
        Controls.Add(bar);
        Controls.Add(top);

        _tabs.SelectedChanged += i => SwitchTab(ports: i == 1);
        _search.Box.TextChanged += (_, _) => Rebind();
        _verdict.PrimaryButton.Click += async (_, _) => { if (_verdictTarget != null) await EndRowAsync(_verdictTarget); };
        _verdict.SecondaryButton.Click += (_, _) => { if (_verdictShow != null) ShowInList(_verdictShow); };
        _tabs.Select(startOnPorts ? 1 : 0);
        SwitchTab(ports: startOnPorts);

        _timer.Tick += (_, _) => Refresh_();
        // One extra early refresh so the overall CPU figure is real within half a second of opening.
        _settle.Tick += (_, _) => { _settle.Stop(); Refresh_(); };
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        bool wide = ClientSize.Width >= Theme.S(1000);
        _stats.Visible = _statsGap.Visible = wide;
        if (!IsHandleCreated || !Visible) return;
        if (WindowState == FormWindowState.Minimized)
        {
            _timer.Stop();
        }
        else if (!_timer.Enabled)
        {
            Refresh_();  // the gap check treats this as a fresh start
            _timer.Start();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkTitleBar(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Refresh_();
        _timer.Start();
        _settle.Start();
        _grid.Focus();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _settle.Stop();
        base.OnFormClosed(e);
    }

    private void SwitchTab(bool ports)
    {
        _showPorts = ports;
        _portsView.Visible = ports;
        _grid.Visible = !ports;
        _search.Visible = !ports;
        if (_tabs.Selected != (ports ? 1 : 0)) _tabs.Select(ports ? 1 : 0);
        UpdateNote();
    }

    private void Flash(string message)
    {
        _flash = message;
        _flashUntil = DateTime.UtcNow.AddSeconds(5);
        UpdateNote();
    }

    /// <summary>Refresh straight after an End/Stop, restarting the timer so the next interval is a full 2 s.</summary>
    private void RefreshAfterAction()
    {
        _timer.Stop();
        Refresh_();
        _timer.Start();
    }

    private void SetUpGrid()
    {
        var g = _grid;
        GridStyle.Apply(g);
        GridStyle.AddColumn(g, "Process", 40, DataGridViewContentAlignment.MiddleLeft);
        GridStyle.AddColumn(g, "CPU now", 17, DataGridViewContentAlignment.MiddleLeft);
        GridStyle.AddColumn(g, "Avg 1 min", 17, DataGridViewContentAlignment.MiddleLeft);
        GridStyle.AddColumn(g, "Since start", 10, DataGridViewContentAlignment.MiddleRight);
        GridStyle.AddColumn(g, "Memory", 9, DataGridViewContentAlignment.MiddleRight);
        GridStyle.AddColumn(g, "", 10, DataGridViewContentAlignment.MiddleRight, sortable: false);

        g.CellValueNeeded += OnCellValueNeeded;
        g.CellPainting += OnCellPainting;
        g.RowPostPaint += OnRowPostPaint;
        g.CellToolTipTextNeeded += OnToolTip;
        g.CellClick += OnCellClick;
        g.CellMouseEnter += (_, e) => SetHover(e.RowIndex, e.ColumnIndex);
        g.MouseLeave += (_, _) => SetHover(-1, -1);
        g.ColumnHeaderMouseClick += OnHeaderClick;
        g.KeyDown += OnGridKey;
    }

    private void SetHover(int row, int col)
    {
        if (row == _hoverRow && col == _hoverCol) return;
        int old = _hoverRow;
        (_hoverRow, _hoverCol) = (row, col);
        if (old >= 0 && old < _grid.RowCount) _grid.InvalidateRow(old);
        if (row >= 0 && row < _grid.RowCount) _grid.InvalidateRow(row);
        _grid.Cursor = IsClickable(row, col) ? Cursors.Hand : Cursors.Default;
    }

    // ---------- refresh ----------

    private void Refresh_()
    {
        try { RefreshCore(); _error = null; }
        catch (Exception ex) { ShowError(ex); }
    }

    internal void ShowError(Exception ex)
    {
        _error = $"Couldn't refresh: {ex.Message}";
        UpdateNote();
    }

    private void RefreshCore()
    {
        var nowUtc = DateTime.UtcNow;              // real time: process ages
        var at = _clock.Elapsed;                     // monotonic: sampling intervals
        bool firstSample = _lastSampleAt is not TimeSpan last || at - last > MaxGap;
        if (firstSample) { _history.Clear(); _trend.Clear(); }

        var samples = _source.Read();
        _history.Update(samples, _clockEpochUtc + at);

        var sys = SystemStats.Take(_source.IdleCpuTime - _lastIdle, firstSample ? TimeSpan.Zero : at - _lastSampleAt!.Value, _cores);
        _lastIdle = _source.IdleCpuTime;
        _lastSampleAt = at;

        _rows = Grouper.Build(samples, _history, _claude, nowUtc, _cores);
        // No overall CPU yet on a first sample: only a process that is clearly busy on its own (score >= 3) gets flagged.
        var culprit = CulpritScorer.Apply(_rows, firstSample ? 0 : sys.CpuPercent, nowUtc);

        var groupKeys = new HashSet<string>();
        foreach (var r in _rows)
        {
            if (r.Kind != RowKind.Group) continue;
            groupKeys.Add(r.Key);
            if (r.Mark == CulpritMark.Primary && _autoExpanded.Add(r.Key)) _expanded.Add(r.Key);
        }
        _autoExpanded.IntersectWith(groupKeys);

        // One-minute trends for the verdict card's chart.
        if (!firstSample) _trend.Push(OverallKey, sys.CpuPercent);
        foreach (var r in AllRows()) if (r.CpuNow is double now) _trend.Push(r.Key, now);
        _trend.Retain(AllRows().Select(r => r.Key).Append(OverallKey));
        _barScale = Math.Max(10, _rows.Select(r => Math.Max(r.CpuNow ?? 0, r.Avg60 ?? 0)).DefaultIfEmpty(0).Max() * 1.15);

        // Ports are cheap to read (one table call) and command lines are cached, so they refresh even when the tab is hidden:
        // that keeps the Ports badge honest.
        _portRows = PortGrouper.Build(_ports.Read(), samples, _claude, _commands.For, nowUtc);
        _commands.Prune(samples.Select(s => s.Key).ToHashSet());
        RefreshBrowserTabs();
        _claude.Prune(_rows.Where(r => r.Kind == RowKind.ClaudeSession).Select(r => r.Root.Key)
            .Concat(samples.Where(s => s.Name.Equals("claude", StringComparison.OrdinalIgnoreCase)).Select(s => s.Key)).ToHashSet());
        int leftovers = _portRows.Count(p => p.LikelyLeftover);
        _tabs.SetBadge(leftovers > 0 ? $"{leftovers} left on" : null);

        _lastCulprit = culprit;
        _lastSys = sys;
        _lastFirst = firstSample;
        UpdateStats(sys, firstSample);
        UpdateVerdict();
        UpdateNote();
        Rebind(resort: false);
        _portsView.Show(_portRows);
    }

    private void UpdateStats(SystemSnapshot sys, bool first)
    {
        const double gb = 1024.0 * 1024 * 1024;
        _stats.Show(first ? 0 : sys.CpuPercent / 100, first ? "…" : $"{sys.CpuPercent:0}%",
            sys.RamTotalBytes > 0 ? (double)sys.RamUsedBytes / sys.RamTotalBytes : 0,
            $"{sys.RamUsedBytes / gb:0.0} / {sys.RamTotalBytes / gb:0} GB", SystemStats.PowerText(sys.Battery));
    }

    private void UpdateNote()
    {
        if (_error != null) { _note.ForeColor = Theme.Coral; _note.Text = _error; return; }
        if (_flash != null && DateTime.UtcNow < _flashUntil) { _note.ForeColor = Theme.Text; _note.Text = _flash; return; }
        _flash = null;
        if (_showPorts)
        {
            _note.ForeColor = _portRows.Any(p => p.LikelyLeftover) ? Theme.Amber : Theme.Muted;
            _note.Text = PortsView.Summary(_portRows);
            return;
        }
        _note.ForeColor = Theme.Muted;
        _note.Text = _rows.Count == 0 ? "" : $"{_rows.Count} programs · sorted by what's most likely making noise · updates every 2 s";
    }

    private int DepthOf(Row r) => _rows.Contains(r) ? 0 : 1;
    private Row? ParentOf(Row r) => _rows.FirstOrDefault(g => g.Children.Contains(r));

    /// <summary>The big card: the culprit, why, its last minute, and End; or "all quiet" with the whole machine's CPU.</summary>
    private void UpdateVerdict()
    {
        var culprit = _lastCulprit;
        _verdictTarget = _verdictShow = null;
        if (culprit == null)
        {
            string cpu = _lastSys is SystemSnapshot s && !_lastFirst ? $"{s.CpuPercent:0}%" : "…";
            _verdict.Show(new Verdict(false, "All quiet", "Nothing is working hard right now",
                "Fan noise may be from charging or the laptop cooling down.", cpu, "CPU, whole machine",
                _trend.Of(OverallKey), null, null, null, null));
            return;
        }

        int depth = DepthOf(culprit);
        var tab = TabDescription(culprit);
        bool browserProcess = culprit.Kind == RowKind.Single && Chromium.Browsers.Contains(culprit.Root.Name);
        string title = browserProcess
            ? Chromium.DisplayName(culprit.Root.Name) + " · " + (tab ?? Capitalize(RoleOf(culprit) ?? "process"))
            : culprit.Label;
        string detail = culprit.Hint ?? Presentation.VerdictDetail(culprit, _cores, DateTime.UtcNow);

        string? primary = null;
        if (CanEnd(culprit, depth))
        {
            _verdictTarget = culprit;
            primary = browserProcess ? "End this process" : Presentation.EndButton(culprit);
        }
        else
        {
            var secondary = AllRows().FirstOrDefault(r => r.Mark == CulpritMark.Secondary && r.Kind != RowKind.Group);
            if (secondary != null && CanEnd(secondary, DepthOf(secondary)))
            {
                _verdictTarget = secondary;
                primary = $"End {secondary.Label} instead";
                detail += culprit.Protected ? $". Windows needs it, so FanWatch won't end it; the busiest you can end is {secondary.Label}" : "";
            }
            else if (culprit.Protected) detail += ". Windows needs it, so FanWatch won't end it";
        }

        string? show = null;
        if (culprit.Children.Count > 0) { show = $"Show its {culprit.Children.Count} processes"; _verdictShow = culprit; }
        else if (depth > 0) { show = "Show in list"; _verdictShow = culprit; }

        var (pct, caption) = Presentation.VerdictFigure(culprit, DateTime.UtcNow);
        _verdict.Show(new Verdict(true, "Why your fan is loud", title, detail, Pct(pct), caption,
            _trend.Of(culprit.Key), AppIcons.For(culprit.Root), culprit.Root.Name, primary, show));
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Expand to and select a row in the table.</summary>
    private void ShowInList(Row r)
    {
        if (_showPorts) SwitchTab(ports: false);
        _search.Box.Clear();
        if (r.Children.Count > 0) _expanded.Add(r.Key);
        else if (ParentOf(r) is Row parent) _expanded.Add(parent.Key);
        Rebind();
        int i = _view.FindIndex(v => v.Row.Key == r.Key);
        if (i < 0) return;
        _grid.CurrentCell = _grid[ColProcess, i];
        _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, i - 1);
        _grid.Focus();
    }

    /// <summary>
    /// For every browser group: remember its window titles, and if its Task Manager (Shift+Esc) is open, read which
    /// tab each process is (off the UI thread, at most every 3 s).
    /// </summary>
    private void RefreshBrowserTabs()
    {
        _windowTitles.Clear();
        _browserMainWindow.Clear();
        _taskManagerOpen.Clear();
        _taskManagerMinimized.Clear();
        var windows = new List<IntPtr>();
        foreach (var b in _rows.Where(r => r.Kind == RowKind.Group && Chromium.Browsers.Contains(r.Root.Name)))
        {
            var pids = b.Members.Select(m => m.Pid).ToHashSet();
            foreach (var w in BrowserTabs.WindowsOf(pids))
            {
                bool isTaskManager = w.Title.StartsWith("Task Manager - ", StringComparison.Ordinal);
                if (isTaskManager)
                {
                    _taskManagerOpen.Add(b.Key);
                    if (BrowserTaskManager.IsMinimized(w.Handle)) _taskManagerMinimized[b.Key] = w.Handle;
                    else windows.Add(w.Handle);
                    continue;
                }
                _windowTitles.TryAdd(w.Pid, w.Title);
                _browserMainWindow.TryAdd(b.Key, w.Handle);
            }
        }

        if (windows.Count == 0) { _browserTasks = new(); return; }
        if (_tasksReading || DateTime.UtcNow - _tasksReadAt < TasksEvery) return;
        _tasksReading = true;
        Task.Run(() => windows.SelectMany(BrowserTaskManager.Read).GroupBy(t => t.Pid).ToDictionary(g => g.Key, g => g.ToList()))
            .ContinueWith(t =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke(() =>
                    {
                        _tasksReading = false;
                        _tasksReadAt = DateTime.UtcNow;
                        if (t.IsCompletedSuccessfully) { _browserTasks = t.Result; UpdateVerdict(); Rebind(resort: false); }
                    });
                }
                catch (InvalidOperationException) { /* window closing */ }
            }, TaskScheduler.Default);
    }

    /// <summary>When the browser's Task Manager is closed or minimized, one clickable row about it.</summary>
    private IEnumerable<Row> TabRows(Row group)
    {
        if (!Chromium.Browsers.Contains(group.Root.Name)) yield break;
        if (_taskManagerMinimized.ContainsKey(group.Key))
            yield return new Row { Label = "", Kind = RowKind.Info, Root = group.Root, Members = [], Protected = true, Hint = RestoreTaskManagerAction };
        else if (!_taskManagerOpen.Contains(group.Key))
            yield return new Row { Label = "", Kind = RowKind.Info, Root = group.Root, Members = [], Protected = true, Hint = OpenTaskManagerAction };
    }

    private static (string Title, string Sub) InfoText(Row r)
    {
        string b = Chromium.DisplayName(r.Root.Name);
        return r.Hint == RestoreTaskManagerAction
            ? ($"{b}'s Task Manager is minimized, so FanWatch can't read which tab is which",
               "Click here to put it behind your other windows instead (it stays out of your way there).")
            : ($"Which tab is which? Click here to open {b}'s Task Manager",
               $"Or press Shift+Esc in {b}. Keep it open; behind other windows is fine.");
    }

    private string? RoleOf(Row r) => r.Kind == RowKind.Single ? Chromium.Role(r.Root.Name, _commands.For(r.Root)) : null;

    /// <summary>
    /// End is offered unless the row is protected, or it is a browser's main process listed inside its group: ending that
    /// closes every window, which looked like "End this tab" (regression: it closed all of Brave).
    /// </summary>
    private bool CanEnd(Row r, int depth) =>
        !r.Protected && r.Kind != RowKind.Info && !(depth > 0 && Chromium.IsWholeBrowser(r.Root.Name, RoleOf(r)));

    /// <summary>"tab: My Office" etc. for a browser process, when its Task Manager is open.</summary>
    private string? TabDescription(Row r) =>
        r.Kind == RowKind.Single && _browserTasks.TryGetValue(r.Root.Pid, out var tasks) && tasks.Count > 0
            ? BrowserTaskManager.Describe(tasks) : null;
    private IEnumerable<Row> AllRows() => _rows.SelectMany(r => r.Kind == RowKind.Group ? [r, .. r.Children] : new[] { r });

    /// <param name="resort">False on timer refreshes: while the mouse is over the table, rows keep their positions so a
    /// click can't land on a row that just moved under the cursor. User actions (sort, expand, search) always resort.</param>
    private void Rebind(bool resort = true)
    {
        string? selected = _grid.CurrentCell != null && _grid.CurrentCell.RowIndex < _view.Count ? Key(_view[_grid.CurrentCell.RowIndex]) : null;
        int firstVisible = _grid.RowCount > 0 ? _grid.FirstDisplayedScrollingRowIndex : 0;

        bool freeze = !resort && _view.Count > 0 && _grid.ClientRectangle.Contains(_grid.PointToClient(Cursor.Position));
        Dictionary<string, int>? previousOrder = null;
        if (freeze)
        {
            previousOrder = new Dictionary<string, int>(_view.Count);
            for (int i = 0; i < _view.Count; i++) previousOrder.TryAdd(Key(_view[i]), i);
        }
        IEnumerable<Row> Order(IEnumerable<Row> rows, int depth) => previousOrder == null
            ? Sorted(rows)
            : Sorted(rows).OrderBy(r => previousOrder.GetValueOrDefault(Key(new ViewRow(r, depth)), int.MaxValue));

        string q = _search.Box.Text.Trim();
        var view = new List<ViewRow>(_rows.Count + 32);
        foreach (var r in Order(_rows.Where(r => Presentation.Matches(r, q)), 0))
        {
            view.Add(new ViewRow(r, 0));
            if (r.Children.Count == 0) continue;
            bool selfMatch = q.Length == 0 || r.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                             || r.Root.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
            if (!selfMatch)
                view.AddRange(Order(r.Children.Where(c => Presentation.Matches(c, q)), 1).Select(c => new ViewRow(c, 1)));
            else if (_expanded.Contains(r.Key))
            {
                view.AddRange(TabRows(r).Select(t => new ViewRow(t, 1)));
                view.AddRange(Order(r.Children, 1).Select(c => new ViewRow(c, 1)));
            }
        }
        _view = view;

        if (_grid.RowCount != view.Count) _grid.RowCount = view.Count;
        _grid.Invalidate();

        if (view.Count == 0) return;
        int sel = selected == null ? -1 : view.FindIndex(v => Key(v) == selected);
        if (sel >= 0) _grid.CurrentCell = _grid[ColProcess, sel];
        else _grid.ClearSelection();
        // Restore scroll after selection, because assigning CurrentCell scrolls it into view.
        if (firstVisible >= 0 && firstVisible < view.Count) _grid.FirstDisplayedScrollingRowIndex = firstVisible;
    }

    private static string Key(ViewRow v) => v.Depth == 0 ? v.Row.Key : "c" + v.Row.Key;

    private IEnumerable<Row> Sorted(IEnumerable<Row> rows)
    {
        Func<Row, double> key = _sortCol switch
        {
            ColNow => r => r.CpuNow ?? -1,
            ColAvg => r => r.Avg60 ?? -1,
            ColSince => r => r.SinceStart,
            ColMem => r => r.Memory,
            _ => r => r.Score,
        };
        if (_sortCol == ColProcess)
            return _sortDesc ? rows.OrderByDescending(r => r.Label, StringComparer.OrdinalIgnoreCase)
                             : rows.OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase);
        return _sortDesc ? rows.OrderByDescending(key).ThenBy(r => r.Label) : rows.OrderBy(key).ThenBy(r => r.Label);
    }

    // ---------- grid callbacks ----------

    /// <summary>Plain-text values: used for accessibility and copy; what you see is painted in <see cref="OnCellPainting"/>.</summary>
    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex >= _view.Count) return;
        var (r, depth) = _view[e.RowIndex];
        e.Value = e.ColumnIndex switch
        {
            ColProcess => r.Kind == RowKind.Info ? InfoText(r).Title : ChildTitle(r, depth),
            _ when r.Kind == RowKind.Info => "",
            ColNow => Pct(r.CpuNow),
            ColAvg => Pct(r.Avg60),
            ColSince => Pct(r.SinceStart),
            ColMem => Mem(r.Memory),
            ColEnd => CanEnd(r, depth) ? "End" : "",
            _ => "",
        };
    }

    private string ChildTitle(Row r, int depth)
    {
        if (depth == 0) return r.Label;
        var role = TabDescription(r) ?? RoleOf(r);
        if (role == "browser") return "Main browser process (all windows)";
        return role != null ? Capitalize(role) : r.Label;
    }

    private string ChildSubtitle(Row r, int depth)
    {
        if (depth == 0) return Presentation.Subtitle(r, DateTime.UtcNow);
        var role = TabDescription(r) ?? RoleOf(r);
        return role != null ? $"{r.Label} · PID {r.Root.Pid}" : Presentation.Subtitle(r, DateTime.UtcNow);
    }

    private static string Pct(double? v) => v is double d ? (d < 0.05 ? "0%" : $"{d:0.0}%") : "…";

    private static string Mem(long bytes) => bytes < 1L << 30 ? $"{bytes / (1024.0 * 1024):N0} MB" : $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";

    private void OnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        var g = e.Graphics!;
        if (e.RowIndex < 0) { if (e.ColumnIndex >= 0) GridStyle.PaintHeader(e, _grid); return; }
        if (e.RowIndex >= _view.Count || e.ColumnIndex < 0) return;
        e.Handled = true;
        var (r, depth) = _view[e.RowIndex];
        var b = e.CellBounds;
        bool hover = e.RowIndex == _hoverRow;
        bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var back = r.Mark switch
        {
            CulpritMark.Primary => hover || selected ? Theme.CulpritHover : Theme.Culprit,
            CulpritMark.Secondary => hover || selected ? Theme.SecondaryHover : Theme.Secondary,
            _ => selected ? Theme.Selection : hover ? Theme.Hover : Theme.Table,
        };
        GridStyle.PaintRow(g, b, back);
        if (r.Kind == RowKind.Info) return;   // drawn across the whole row in OnRowPostPaint

        var text = depth > 0 ? Theme.Dim : Theme.Text;
        switch (e.ColumnIndex)
        {
            case ColProcess: PaintProcess(g, b, r, depth); break;
            case ColNow: GridStyle.PaintBar(g, b, r.CpuNow, _barScale, Pct(r.CpuNow), GridStyle.Heat(r.CpuNow), text); break;
            case ColAvg: GridStyle.PaintBar(g, b, r.Avg60, _barScale, Pct(r.Avg60), GridStyle.Heat(r.Avg60), Theme.Dim, alpha: 170); break;
            case ColSince:
            case ColMem:
                Theme.Text_(g, e.ColumnIndex == ColSince ? Pct(r.SinceStart) : Mem(r.Memory), Theme.Body, Theme.Dim,
                    Rectangle.FromLTRB(b.Left, b.Top, b.Right - Theme.S(14), b.Bottom), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                break;
            case ColEnd:
                if (CanEnd(r, depth)) GridStyle.PaintPill(g, b, "End", hover && _hoverCol == ColEnd);
                else if (r.Protected || Chromium.IsWholeBrowser(r.Root.Name, RoleOf(r)))
                    Theme.Text_(g, Theme.GlyphLock, Theme.SmallIcons, Theme.Muted,
                        Rectangle.FromLTRB(b.Left, b.Top, b.Right - Theme.S(18), b.Bottom), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                break;
        }
    }

    private void PaintProcess(Graphics g, Rectangle b, Row r, int depth)
    {
        int x = b.Left + Theme.S(14) + depth * Theme.S(36);
        if (depth == 0)
        {
            if (r.Children.Count > 0)
                Theme.Text_(g, _expanded.Contains(r.Key) ? Theme.GlyphChevronDown : Theme.GlyphChevronRight, Theme.SmallIcons, Theme.Muted,
                    new Rectangle(x, b.Top, Theme.S(14), b.Height));
            x += Theme.S(22);
        }
        int size = depth == 0 ? Theme.S(24) : Theme.S(18);
        Theme.DrawAppIcon(g, AppIcons.For(r.Root), r.Root.Name, new Rectangle(x, b.Top + (b.Height - size) / 2, size, size));
        x += size + Theme.S(12);
        var title = ChildTitle(r, depth);
        var color = r.Mark == CulpritMark.Primary ? Theme.CulpritText : depth > 0 ? Theme.Text : Theme.Text;
        GridStyle.PaintTwoLine(g, Rectangle.FromLTRB(x, b.Top, b.Right - Theme.S(8), b.Bottom), title,
            depth == 0 ? Theme.Bold : Theme.Body, color, ChildSubtitle(r, depth), Theme.Muted);
    }

    /// <summary>Info rows ("open the browser's Task Manager") read across the whole row.</summary>
    private void OnRowPostPaint(object? sender, DataGridViewRowPostPaintEventArgs e)
    {
        if (e.RowIndex >= _view.Count || _view[e.RowIndex].Row is not { Kind: RowKind.Info } r) return;
        var b = e.RowBounds;
        int x = b.Left + Theme.S(14) + Theme.S(36) - _grid.HorizontalScrollingOffset;
        Theme.Text_(e.Graphics, Theme.GlyphInfo, Theme.Icons, Theme.Blue, new Rectangle(x, b.Top, Theme.S(18), b.Height));
        var (title, sub) = InfoText(r);
        GridStyle.PaintTwoLine(e.Graphics, Rectangle.FromLTRB(x + Theme.S(30), b.Top, b.Right - Theme.S(14), b.Bottom - 1),
            title, Theme.Body, Theme.Blue, sub, Theme.Muted);
    }

    private void OnToolTip(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
        var (r, depth) = _view[e.RowIndex];
        if (e.ColumnIndex == ColSince)
            e.ToolTipText = $"{r.CpuTotal.TotalMinutes:N0} CPU-min over {Presentation.Age(DateTime.UtcNow - r.Root.StartUtc)}";
        else if (e.ColumnIndex == ColProcess && r.Kind == RowKind.Single && _browserTasks.TryGetValue(r.Root.Pid, out var tasks))
            e.ToolTipText = BrowserTaskManager.DescribeAll(tasks);
        else if (e.ColumnIndex == ColProcess && r.Hint != null && r.Kind != RowKind.Info)
            e.ToolTipText = r.Hint;
        else if (e.ColumnIndex == ColEnd && depth > 0 && Chromium.IsWholeBrowser(r.Root.Name, RoleOf(r)))
            e.ToolTipText = $"Ending this closes ALL {Chromium.DisplayName(r.Root.Name)} windows and tabs, so it has no End button. Use End on the {r.Root.Name} group if you mean to close {Chromium.DisplayName(r.Root.Name)}.";
        else if (e.ColumnIndex == ColProcess && depth > 0 && RoleOf(r) == "browser" && _windowTitles.TryGetValue(r.Root.Pid, out var winTitle))
            e.ToolTipText = $"{Chromium.DisplayName(r.Root.Name)}'s main process. Current window: {winTitle}";
        else if (e.ColumnIndex == ColEnd && r.Protected && r.Kind != RowKind.Info)
            e.ToolTipText = "Windows needs this process, so FanWatch won't end it.";
    }

    private bool IsClickable(int row, int col) =>
        row >= 0 && row < _view.Count &&
        ((col == ColEnd && CanEnd(_view[row].Row, _view[row].Depth))
         || (_view[row].Depth == 0 && _view[row].Row.Children.Count > 0 && col != ColEnd)
         || _view[row].Row.Hint is OpenTaskManagerAction or RestoreTaskManagerAction && _view[row].Row.Kind == RowKind.Info);

    private void OnGridKey(object? sender, KeyEventArgs e)
    {
        if (_grid.CurrentCell is not { RowIndex: var i } || i >= _view.Count) return;
        var r = _view[i].Row;
        if (r.Children.Count == 0 || _view[i].Depth != 0) return;
        if ((e.KeyCode == Keys.Right && !_expanded.Contains(r.Key)) || (e.KeyCode == Keys.Left && _expanded.Contains(r.Key)))
        {
            if (!_expanded.Remove(r.Key)) _expanded.Add(r.Key);
            Rebind();
            e.Handled = true;
        }
    }

    private async void OnCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (!IsClickable(e.RowIndex, e.ColumnIndex)) return;
        var r = _view[e.RowIndex].Row;

        if (r.Kind == RowKind.Info && r.Hint == RestoreTaskManagerAction)
        {
            var g = _rows.FirstOrDefault(x => x.Kind == RowKind.Group && x.Root.Name.Equals(r.Root.Name, StringComparison.OrdinalIgnoreCase));
            if (g != null && _taskManagerMinimized.TryGetValue(g.Key, out var tm))
            {
                BrowserTaskManager.RestoreBehind(tm);
                _tasksReadAt = DateTime.MinValue;
                RefreshAfterAction();
            }
            return;
        }

        if (r.Kind == RowKind.Info && r.Hint == OpenTaskManagerAction)
        {
            var group = _rows.FirstOrDefault(g => g.Kind == RowKind.Group && g.Root.Name.Equals(r.Root.Name, StringComparison.OrdinalIgnoreCase));
            if (group != null && _browserMainWindow.TryGetValue(group.Key, out var hwnd))
            {
                BrowserTaskManager.Open(hwnd);
                _tasksReadAt = DateTime.MinValue;   // read it as soon as it appears
                Flash("Opened the browser's Task Manager. Tab names appear here in a few seconds; you can minimize it.");
            }
            return;
        }

        if (e.ColumnIndex != ColEnd)
        {
            if (!_expanded.Remove(r.Key)) _expanded.Add(r.Key);
            Rebind();
            return;
        }

        await EndRowAsync(r);
    }

    /// <summary>Confirm, then end a row's processes (from the table or the verdict card).</summary>
    private async Task EndRowAsync(Row r)
    {
        if (_busyEnding || !CanEnd(r, DepthOf(r))) return;
        var confirm = (r.Kind == RowKind.Single ? Chromium.EndConfirmation(r.Root.Name, r.Root.Pid, RoleOf(r), TabDescription(r)) : null)
                      ?? _killer.ConfirmText(r);
        var answer = MessageBox.Show(this, confirm, "End process", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        _busyEnding = true;
        try { Flash((await _killer.EndAsync(r, TimeSpan.FromSeconds(3))).Message); }
        finally { _busyEnding = false; }
        // Restart the timer so the next "CPU now" interval is a full 2 s, not a few noisy milliseconds.
        RefreshAfterAction();
    }

    private void OnHeaderClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex == ColEnd) return;
        if (_sortCol == e.ColumnIndex) _sortDesc = !_sortDesc;
        else { _sortCol = e.ColumnIndex; _sortDesc = e.ColumnIndex != ColProcess; }

        foreach (DataGridViewColumn c in _grid.Columns) c.HeaderCell.SortGlyphDirection = SortOrder.None;
        _grid.Columns[_sortCol].HeaderCell.SortGlyphDirection = _sortDesc ? SortOrder.Descending : SortOrder.Ascending;
        Rebind();
    }
}
