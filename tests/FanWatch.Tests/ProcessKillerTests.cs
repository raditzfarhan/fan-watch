using System.Diagnostics;
using static FanWatch.Tests.Fakes;

namespace FanWatch.Tests;

public class ProcessKillerTests
{
    private static Process StartPing() => Process.Start(new ProcessStartInfo("ping", "-n 30 127.0.0.1")
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
    })!;

    private static Row RowFor(ProcSample s) => new()
    {
        Label = s.Name, Kind = RowKind.Single, Root = s, Members = [s],
    };

    private static ProcSample RealSample(Process p) =>
        new NtProcessSource().Read().Single(s => s.Pid == p.Id);

    [Fact]
    public async Task EndsSpawnedProcess()
    {
        using var ping = StartPing();
        var result = await new ProcessKiller().EndAsync(RowFor(RealSample(ping)), TimeSpan.FromSeconds(3));
        Assert.Equal(1, result.Ended);
        Assert.True(ping.WaitForExit(5000));
        Assert.Equal("Ended 1 process.", result.Message);
    }

    [Fact]
    public async Task AlreadyGoneIsReportedNotThrown()
    {
        var result = await new ProcessKiller().EndAsync(RowFor(P(999_999, "ghost")), TimeSpan.FromSeconds(1));
        Assert.Equal(1, result.AlreadyGone);
        Assert.Equal(0, result.Failed);
        Assert.Equal("Already closed.", result.Message);
    }

    [Fact]
    public async Task PidReuseNotKilled()
    {
        using var ping = StartPing();
        try
        {
            var stale = RealSample(ping) with { StartUtc = DateTime.UtcNow.AddHours(-1) };
            var result = await new ProcessKiller().EndAsync(RowFor(stale), TimeSpan.FromSeconds(1));
            Assert.Equal(1, result.AlreadyGone);
            Assert.False(ping.HasExited);
        }
        finally { ping.Kill(); }
    }

    [Fact]
    public async Task NeverAttemptsSystemSessionProcesses()
    {
        // Safe even from an admin shell: session-0 processes are skipped before any Kill is attempted.
        var target = new NtProcessSource().Read().First(s => s.SessionId == 0 && !Protection.IsProtected(s.Name, s.Pid));
        var result = await new ProcessKiller().EndAsync(RowFor(target), TimeSpan.FromSeconds(1));
        Assert.Equal(0, result.Ended);
        Assert.Equal(0, result.Failed);
        Assert.Contains(new NtProcessSource().Read(), s => s.Key == target.Key);
    }

    [Fact]
    public void DescribesFailures() =>
        Assert.Equal("Ended 2 processes. Could not end 1 process (access denied).", ProcessKiller.Describe(2, 0, 1));

    [Fact]
    public void ReadsRealSessionIds()
    {
        var all = new NtProcessSource().Read();
        Assert.Contains(all, s => s.SessionId == 0);
        Assert.NotEqual(0, all.Single(s => s.Pid == Environment.ProcessId).SessionId);
    }

    [Fact]
    public async Task SkipsProtectedMembers()
    {
        using var ping = StartPing();
        var self = new NtProcessSource().Read().Single(s => s.Pid == Environment.ProcessId);
        var target = RealSample(ping);
        var row = new Row { Label = "claude", Kind = RowKind.ClaudeSession, Root = target, Members = [target, self] };
        var result = await new ProcessKiller().EndAsync(row, TimeSpan.FromSeconds(1));
        Assert.Equal(1, result.Ended);
        Assert.Equal(0, result.Failed);
        Assert.True(ping.WaitForExit(5000));
    }

    private static readonly Func<int, bool> NoWindows = _ => false;

    [Fact]
    public void ConfirmTextClaude()
    {
        var c = P(10, "claude");
        var kids = new[] { P(11, "node", 10), P(12, "node", 11), P(13, "bash", 10) };
        var row = new Row { Label = "claude (my-office)", Kind = RowKind.ClaudeSession, Root = c, Members = [c, .. kids] };
        Assert.Equal("End claude (my-office) and its 3 child processes? Anything unsaved in it will be lost.",
            new ProcessKiller().ConfirmText(row, NoWindows));
    }

    [Fact]
    public void ConfirmTextClaudeCountsOnlyEndableChildren()
    {
        var c = P(10, "claude");
        var kids = new[] { P(11, "node", 10), P(12, "FanWatch", 10), P(13, "svc", 10) with { SessionId = 0 } };
        var row = new Row { Label = "claude (api)", Kind = RowKind.ClaudeSession, Root = c, Members = [c, .. kids] };
        Assert.Equal("End claude (api) and its 1 child process? Anything unsaved in it will be lost.",
            new ProcessKiller().ConfirmText(row, NoWindows));
    }

    [Fact]
    public void ConfirmTextClaudeNamesWindowedApps()
    {
        var c = P(10, "claude");
        var kids = new[] { P(11, "Code", 10), P(12, "node", 10) };
        var row = new Row { Label = "claude (api)", Kind = RowKind.ClaudeSession, Root = c, Members = [c, .. kids] };
        Assert.Equal("End claude (api) and its 2 child processes, including Code? Anything unsaved in it will be lost.",
            new ProcessKiller().ConfirmText(row, pid => pid == 11));
    }

    [Fact]
    public async Task EndDoesNotBlockTheCaller()
    {
        using var ping = StartPing();
        var task = new ProcessKiller().EndAsync(RowFor(RealSample(ping)), TimeSpan.FromSeconds(3));
        Assert.False(task.IsCompleted);
        Assert.Equal(1, (await task).Ended);
    }

    [Fact]
    public async Task GroupClosesWindowedMemberBeforeKillingHelpers()
    {
        // A tiny minimized WinForms window (it must stay in the taskbar: hiding it gives the form a hidden owner,
        // so Windows no longer treats it as a main window). Closing it gracefully exits with 0; being killed gives -1.
        var windowed = Process.Start(new ProcessStartInfo("powershell",
            "-NoProfile -Command \"Add-Type -A System.Windows.Forms; $f = New-Object Windows.Forms.Form; $f.Text = 'fanwatch-test'; " +
            "$f.WindowState = 'Minimized'; [Windows.Forms.Application]::Run($f); exit 0\"")
        { UseShellExecute = false, CreateNoWindow = true })!;
        using var helper = StartPing();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline) { windowed.Refresh(); if (windowed.MainWindowHandle != IntPtr.Zero) break; Thread.Sleep(100); }
            Assert.NotEqual(IntPtr.Zero, windowed.MainWindowHandle);

            var w = RealSample(windowed);
            var h = RealSample(helper);
            var row = new Row { Label = "test (2)", Kind = RowKind.Group, Root = w, Members = [w, h] };
            var result = await new ProcessKiller().EndAsync(row, TimeSpan.FromSeconds(5));

            Assert.Equal(2, result.Ended);
            Assert.True(windowed.WaitForExit(5000));
            Assert.Equal(0, windowed.ExitCode);
            Assert.True(helper.HasExited);
        }
        finally
        {
            if (!windowed.HasExited) windowed.Kill();
            windowed.Dispose();
        }
    }

    [Fact]
    public void ConfirmTextGroup()
    {
        var members = Enumerable.Range(1, 42).Select(i => P(i, "node")).ToList();
        var row = new Row { Label = "node (42)", Kind = RowKind.Group, Root = members[0], Members = members };
        Assert.Equal("End all 42 node processes?", new ProcessKiller().ConfirmText(row));
    }

    [Fact]
    public void ConfirmTextSingle()
    {
        var s = P(1, "explorer");
        Assert.Equal("End explorer? Anything unsaved in it will be lost.", new ProcessKiller().ConfirmText(RowFor(s)));
    }
}
