namespace FanWatch.Tests;

public class TaskManagerTests
{
    private static readonly string[] Header =
        ["Column Task unsorted", "Column Memory footprint unsorted", "Column CPU sorted in descending order", "Column Network unsorted", "Column Process ID unsorted"];

    private static IReadOnlyList<BrowserTask> Parse(params string[][] rows) => BrowserTaskManager.Parse([Header, .. rows]);

    [Fact]
    public void ParsesTabRow()
    {
        var t = Assert.Single(Parse(["Tab: My Office", "128,520K", "79.0", "0", "52812"]));
        Assert.Equal(TaskKind.Tab, t.Kind);
        Assert.Equal("My Office", t.Title);
        Assert.Equal(52812, t.Pid);
        Assert.Equal(79.0, t.CpuPercentOfCore);
        Assert.Equal(128_520L * 1024, t.MemoryBytes);
    }

    [Fact]
    public void TitleMayContainSeparators()
    {
        var t = Assert.Single(Parse(["Tab: Facebook Live | Facebook", "238,392K", "0.4", "0", "38364"]));
        Assert.Equal("Facebook Live | Facebook", t.Title);
    }

    [Theory]
    [InlineData("Subframe: https://youtube.com/", TaskKind.Subframe, "https://youtube.com/")]
    [InlineData("Extension: Claude", TaskKind.Extension, "Claude")]
    [InlineData("Incognito Tab: Secret", TaskKind.Tab, "Secret")]
    [InlineData("Service Worker: https://web.whatsapp.com/", TaskKind.Worker, "https://web.whatsapp.com/")]
    [InlineData("GPU Process", TaskKind.Other, "GPU Process")]
    public void TaskKinds(string task, TaskKind kind, string title)
    {
        var t = Assert.Single(Parse([task, "1K", "0", "0", "1"]));
        Assert.Equal(kind, t.Kind);
        Assert.Equal(title, t.Title);
    }

    [Fact]
    public void UsesHeaderToFindColumns()
    {
        // Columns reordered / extra column added by the user.
        string[] header = ["Column Process ID unsorted", "Column Task unsorted", "Column Profile unsorted", "Column CPU unsorted"];
        var t = Assert.Single(BrowserTaskManager.Parse([header, ["4242", "Tab: Mail", "Person 1", "3.5"]]));
        Assert.Equal(4242, t.Pid);
        Assert.Equal("Mail", t.Title);
        Assert.Equal(3.5, t.CpuPercentOfCore);
        Assert.Null(t.MemoryBytes);
    }

    [Fact]
    public void WithoutPidColumnNothingCanBeMatched() =>
        Assert.Empty(BrowserTaskManager.Parse([["Column Task unsorted", "Column CPU unsorted"], ["Tab: Mail", "3.5"]]));

    [Fact]
    public void SkipsMalformedRows() =>
        Assert.Empty(Parse(["Tab: Broken", "x", "y", "z", "not-a-pid"], ["", "", "", "", ""]));

    [Fact]
    public void GroupedRowsBelongToTheProcessAbove()
    {
        // Brave shows PID/memory/CPU only on the first task of a process; the rest of the group has blank cells.
        var tasks = Parse(
            ["Subframe: https://accounts.google.com/", "47,460K", "0.0", "0", "22124"],
            ["Subframe: https://accounts.google.com/", "", "", "", ""],
            ["Tab: Gmail", "", "", "", ""]);
        Assert.Equal(3, tasks.Count);
        Assert.All(tasks, t => Assert.Equal(22124, t.Pid));
        Assert.Equal("tab: Gmail + 2 more", BrowserTaskManager.Describe(tasks));
    }

    [Fact]
    public void BlankRowBeforeAnyPidIsSkipped() =>
        Assert.Empty(Parse(["Tab: Orphan", "", "", "", ""]));

    [Fact]
    public void EmptyTitleIsSkipped() =>
        Assert.Empty(Parse(["Tab: ", "–", "–", "–", "51056"]));

    [Fact]
    public void DescribesOneTab()
    {
        var tasks = Parse(["Tab: My Office", "1K", "79.0", "0", "52812"]);
        Assert.Equal("tab: My Office", BrowserTaskManager.Describe(tasks));
    }

    [Fact]
    public void DescribesSeveralTasksBusiestFirst()
    {
        var tasks = Parse(
            ["Subframe: https://youtube.com/", "1K", "0.5", "0", "7"],
            ["Tab: Mail", "1K", "0.1", "0", "7"],
            ["Tab: Docs", "1K", "4.0", "0", "7"]);
        Assert.Equal("tab: Docs + 2 more", BrowserTaskManager.Describe(tasks));
    }

    [Fact]
    public void DescribesSubframeOnlyProcess()
    {
        var tasks = Parse(["Subframe: https://youtube.com/", "1K", "2.0", "0", "49868"]);
        Assert.Equal("embedded in a page: https://youtube.com/", BrowserTaskManager.Describe(tasks));
    }
}
