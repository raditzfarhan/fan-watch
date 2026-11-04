using Xunit.Abstractions;

namespace FanWatch.Tests;

/// <summary>Runs against a real open (not minimized) Brave/Chrome Task Manager when there is one; passes trivially otherwise.</summary>
public class LiveBrowserTaskManagerTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ReadsOpenTaskManagerFromBackgroundThread()
    {
        var browserPids = new NtProcessSource().Read().Where(p => Chromium.Browsers.Contains(p.Name)).Select(p => p.Pid).ToHashSet();
        var window = BrowserTaskManager.FindWindow(browserPids);
        output.WriteLine($"task manager window: {window}");
        if (window == IntPtr.Zero || BrowserTaskManager.IsMinimized(window)) return;

        var tasks = await Task.Run(() => BrowserTaskManager.Read(window));
        output.WriteLine($"tasks read: {tasks.Count}");
        Assert.NotEmpty(tasks);
        Assert.All(tasks, t => Assert.Contains(t.Pid, browserPids));

        // Every row the Task Manager lists, including ones scrolled out of view.
        var root = System.Windows.Automation.AutomationElement.FromHandle(window);
        var grid = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.DataGrid));
        var gp = (System.Windows.Automation.GridPattern)grid.GetCurrentPattern(System.Windows.Automation.GridPattern.Pattern);
        output.WriteLine($"grid rows: {gp.Current.RowCount}");
        Assert.True(tasks.Count >= gp.Current.RowCount - 2, $"read {tasks.Count} of {gp.Current.RowCount} rows");
    }
}