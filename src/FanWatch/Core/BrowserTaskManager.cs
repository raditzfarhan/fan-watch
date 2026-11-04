using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace FanWatch.Core;

public enum TaskKind { Tab, Subframe, Extension, Worker, Other }

/// <summary>One row of a Chromium browser's own Task Manager (Shift+Esc): what a renderer process is doing.</summary>
public sealed record BrowserTask(TaskKind Kind, string Title, int Pid, double? CpuPercentOfCore, long? MemoryBytes);

/// <summary>
/// Reads Brave/Chrome/Edge's built-in Task Manager window through Windows UI Automation (read-only). It is the only place
/// the browser reveals which process ID belongs to which tab, so FanWatch uses it when it is open.
/// </summary>
public static class BrowserTaskManager
{
    private const int MaxTitle = 70;

    /// <summary>Parses grid text: row 0 is the header ("Column Task unsorted", ...), the rest are cells.</summary>
    public static IReadOnlyList<BrowserTask> Parse(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0) return [];
        var header = rows[0].Select(ColumnName).ToList();
        int iTask = header.IndexOf("Task"), iPid = header.IndexOf("Process ID"), iCpu = header.IndexOf("CPU"), iMem = header.IndexOf("Memory footprint");
        if (iTask < 0 || iPid < 0) return [];

        var tasks = new List<BrowserTask>();
        int groupPid = 0;
        foreach (var cells in rows.Skip(1))
        {
            string Cell(int i) => i >= 0 && i < cells.Length ? cells[i].Trim() : "";
            var task = Cell(iTask);
            int pid;
            if (int.TryParse(Cell(iPid), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid) && pid > 0)
                groupPid = pid;                         // first task of a process
            else if (Cell(iPid).Length == 0 && groupPid > 0 && task.Length > 0)
                pid = groupPid;                         // later task in the same process: Brave leaves the cells blank
            else
                continue;
            var (_, titleOnly) = Classify(task);
            if (task.Length == 0 || titleOnly.Length == 0) continue;

            double? cpu = double.TryParse(Cell(iCpu), NumberStyles.Float, CultureInfo.InvariantCulture, out var c) ? c : null;
            long? mem = null;
            var memText = Cell(iMem).Replace(",", "");
            if (memText.EndsWith('K') && long.TryParse(memText[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb)) mem = kb * 1024;

            var (kind, title) = Classify(task);
            tasks.Add(new BrowserTask(kind, title, pid, cpu, mem));
        }
        return tasks;
    }

    /// <summary>Short label for a process from its tasks, busiest first, e.g. "tab: Docs + 2 more".</summary>
    public static string Describe(IReadOnlyList<BrowserTask> tasks)
    {
        var ordered = tasks.OrderBy(t => t.Kind).ThenByDescending(t => t.CpuPercentOfCore ?? 0).ToList();
        var first = ordered[0];
        string prefix = first.Kind switch
        {
            TaskKind.Tab => "tab: ",
            TaskKind.Subframe => "embedded in a page: ",
            TaskKind.Extension => "extension: ",
            TaskKind.Worker => "worker: ",
            _ => "",
        };
        var title = first.Title.Length > MaxTitle ? first.Title[..(MaxTitle - 1)] + "…" : first.Title;
        return prefix + title + (ordered.Count > 1 ? $" + {ordered.Count - 1} more" : "");
    }

    /// <summary>Full list for tooltips.</summary>
    public static string DescribeAll(IReadOnlyList<BrowserTask> tasks) =>
        string.Join("\n", tasks.OrderBy(t => t.Kind).ThenByDescending(t => t.CpuPercentOfCore ?? 0)
            .Select(t => $"{t.Kind}: {t.Title}" + (t.CpuPercentOfCore is double c ? $"  ({c:0.0}% of a core)" : "")));

    private static (TaskKind, string) Classify(string task)
    {
        (string Prefix, TaskKind Kind)[] map =
        [
            ("Tab: ", TaskKind.Tab), ("Incognito Tab: ", TaskKind.Tab), ("App: ", TaskKind.Tab), ("Prerender: ", TaskKind.Tab),
            ("Subframe: ", TaskKind.Subframe),
            ("Extension: ", TaskKind.Extension), ("Background Page: ", TaskKind.Extension),
            ("Service Worker: ", TaskKind.Worker), ("Dedicated Worker: ", TaskKind.Worker), ("Shared Worker: ", TaskKind.Worker),
        ];
        foreach (var (prefix, kind) in map)
        {
            if (task.StartsWith(prefix, StringComparison.Ordinal)) return (kind, task[prefix.Length..].Trim());
            if (task == prefix.TrimEnd()) return (kind, "");   // e.g. "Tab:" with no title (trimmed)
        }
        return (TaskKind.Other, task);
    }

    private static string ColumnName(string header)
    {
        var h = header.Trim();
        if (h.StartsWith("Column ", StringComparison.Ordinal)) h = h["Column ".Length..];
        foreach (var suffix in new[] { " unsorted", " sorted in descending order", " sorted in ascending order" })
            if (h.EndsWith(suffix, StringComparison.Ordinal)) h = h[..^suffix.Length];
        return h;
    }

    // ---------- live window ----------

    /// <summary>The browser's open Task Manager window, if any.</summary>
    public static IntPtr FindWindow(IReadOnlySet<int> browserPids) =>
        BrowserTabs.WindowsOf(browserPids).FirstOrDefault(w => w.Title.StartsWith("Task Manager - ", StringComparison.Ordinal)).Handle;

    /// <summary>Reads every row of the Task Manager grid. Empty if the window closed or isn't readable.</summary>
    public static IReadOnlyList<BrowserTask> Read(IntPtr window)
    {
        try
        {
            var root = AutomationElement.FromHandle(window);
            var grid = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid));
            if (grid == null) return [];
            var walker = TreeWalker.ControlViewWalker;
            var rows = new List<string[]>();

            // The header row is always the grid's first child.
            var header = walker.GetFirstChild(grid);
            if (header == null) return [];
            var headerCells = new List<string>();
            for (var cell = walker.GetFirstChild(header); cell != null; cell = walker.GetNextSibling(cell)) headerCells.Add(cell.Current.Name);
            rows.Add([.. headerCells]);

            // Walking the tree only reaches rows that are scrolled into view; the grid pattern hands out every row.
            if (grid.TryGetCurrentPattern(GridPattern.Pattern, out var p) && p is GridPattern gp)
            {
                int rowCount = gp.Current.RowCount, colCount = gp.Current.ColumnCount;
                for (int r = 0; r < rowCount; r++)
                {
                    var cells = new string[colCount];
                    for (int c = 0; c < colCount; c++) cells[c] = gp.GetItem(r, c)?.Current.Name ?? "";
                    rows.Add(cells);
                }
            }
            else
            {
                for (var row = walker.GetNextSibling(header); row != null; row = walker.GetNextSibling(row))
                {
                    var cells = new List<string>();
                    for (var cell = walker.GetFirstChild(row); cell != null; cell = walker.GetNextSibling(cell)) cells.Add(cell.Current.Name);
                    if (cells.Count > 0) rows.Add([.. cells]);
                }
            }
            return Parse(rows);
        }
        catch (Exception e) when (e is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>Opens the browser's Task Manager by pressing Shift+Esc in its main window (only on the user's click).</summary>
    public static void Open(IntPtr browserWindow)
    {
        ShowWindow(browserWindow, 9 /* SW_RESTORE */);
        SetForegroundWindow(browserWindow);
        Thread.Sleep(150);
        var inputs = new[]
        {
            Key(0x10, false), Key(0x1B, false), Key(0x1B, true), Key(0x10, true), // Shift down, Esc down, Esc up, Shift up
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    /// <summary>A minimized Task Manager exposes no rows, so FanWatch has to ask for it to be restored.</summary>
    public static bool IsMinimized(IntPtr window) => IsIconic(window);

    /// <summary>Restores the window without focusing it and puts it behind all other windows (only on the user's click).</summary>
    public static void RestoreBehind(IntPtr window)
    {
        ShowWindow(window, 4 /* SW_SHOWNOACTIVATE */);
        SetWindowPos(window, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE|NOMOVE|NOACTIVATE */);
    }

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private static Input Key(ushort vk, bool up) => new() { Type = 1, Ki = new KeybdInput { Vk = vk, Flags = up ? 2u : 0u } };

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeybdInput Ki; }

    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
}
