using static FanWatch.Tests.Fakes;

namespace FanWatch.Tests;

public sealed class PortGrouperTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fanwatch-ports-" + Guid.NewGuid().ToString("N"));
    public PortGrouperTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static readonly Func<ProcSample, string?> NoCmd = _ => null;

    private IReadOnlyList<PortRow> Build(Listener[] ls, ProcSample[] ps, Func<ProcSample, string?>? cmd = null) =>
        PortGrouper.Build(ls, ps, new ClaudeSessions(_dir), cmd ?? NoCmd, Now);

    [Fact]
    public void ServerStartedByClaudeIsLeftoverAndNamesSession()
    {
        var p = P(10, "claude");
        File.WriteAllText(Path.Combine(_dir, "10.json"), $$"""{"cwd":"C:\\www\\my-office","procStart":"{{p.StartUtc.ToFileTimeUtc()}}"}""");
        var rows = Build([new Listener(4777, 50, true)], [p, P(20, "bash", 10, 50), P(50, "node", 20, 40)]);
        var r = Assert.Single(rows);
        Assert.Equal("claude (my-office)", r.StartedBy);
        Assert.True(r.IsDevServer);
        Assert.True(r.LikelyLeftover);
    }

    [Fact]
    public void OrphanedDevServerIsLeftover()
    {
        // Its launcher (PID 30) no longer exists.
        var r = Assert.Single(Build([new Listener(5173, 50, false)], [P(50, "node", 30, 40)]));
        Assert.True(r.Orphaned);
        Assert.True(r.LikelyLeftover);
    }

    [Fact]
    public void ParentStartedAfterChildCountsAsOrphaned()
    {
        // PID 30 was reused by a newer process: the real launcher is gone.
        var r = Assert.Single(Build([new Listener(8000, 50, true)], [P(30, "cmd", ageMin: 5), P(50, "php", 30, 60)]));
        Assert.True(r.Orphaned);
    }

    [Fact]
    public void AppListenerIsNotDevServer()
    {
        var r = Assert.Single(Build([new Listener(80, 60, false)], [P(1, "explorer"), P(60, "httpd", 1)]));
        Assert.False(r.IsDevServer);
        Assert.False(r.LikelyLeftover);
    }

    [Fact]
    public void DevServerStartedFromTerminalIsNotLeftover()
    {
        var r = Assert.Single(Build([new Listener(3000, 50, true)], [P(5, "WindowsTerminal"), P(6, "pwsh", 5), P(50, "node", 6, 30)]));
        Assert.True(r.IsDevServer);
        Assert.False(r.LikelyLeftover);
        Assert.Null(r.StartedBy);
    }

    [Fact]
    public void Ipv4AndIpv6OnSamePortAreOneRow()
    {
        var rows = Build([new Listener(5173, 50, true), new Listener(5173, 50, false)], [P(50, "node", 30)]);
        var r = Assert.Single(rows);
        Assert.False(r.LoopbackOnly);
    }

    [Fact]
    public void SystemSessionListenersAreHidden() =>
        Assert.Empty(Build([new Listener(135, 70, false)], [P(70, "svchost") with { SessionId = 0 }]));

    [Fact]
    public void StopTargetIncludesLauncherChainAndChildren()
    {
        // claude → bash → pnpm (node) → cmd → node vite (listener) → esbuild child
        var ps = new[]
        {
            P(10, "claude", 0, 60), P(20, "bash", 10, 55), P(30, "node", 20, 50), P(40, "cmd", 30, 49),
            P(50, "node", 40, 48), P(60, "esbuild", 50, 47), P(70, "node", 20, 45),
        };
        var r = Assert.Single(Build([new Listener(5173, 50, true)], ps));
        var pids = r.StopTarget.Members.Select(m => m.Pid).ToHashSet();
        Assert.Equal(new HashSet<int> { 30, 40, 50, 60 }, pids);   // not claude, not bash, not the unrelated node 70
        Assert.Equal(30, r.StopTarget.Root.Pid);
    }

    [Fact]
    public void CommandAndProjectComeFromCommandLine()
    {
        var r = Assert.Single(Build([new Listener(8000, 50, true)], [P(50, "php", 30)],
            _ => @"php.exe -S 127.0.0.1:8000 C:\laragon\www\shipitlah\app\vendor\laravel\framework\src\server.php"));
        Assert.Contains("-S 127.0.0.1:8000", r.Command);
    }

    [Fact]
    public void OrphanedChainThroughShellsIsLeftoverAndStopsWholeStack()
    {
        // Real shipitlah case: `composer dev` started from a shell that has since exited.
        // vite ← cmd ← concurrently(node) ← cmd ← composer(php) ← sh ← sh ← [gone]; artisan serve is a sibling branch.
        var ps = new[]
        {
            P(21, "sh", 999, 60), P(22, "sh", 21, 59), P(23, "php", 22, 58), P(24, "cmd", 23, 57), P(25, "node", 24, 56),
            P(26, "cmd", 25, 55), P(27, "node", 26, 54),                     // vite, listening on 5173
            P(28, "cmd", 25, 55), P(29, "php", 28, 54), P(30, "cmd", 29, 53), P(31, "php", 30, 52),  // artisan serve → php -S on 8000
        };
        var rows = Build([new Listener(5173, 27, false), new Listener(8000, 31, true)], ps);
        Assert.All(rows, r => Assert.True(r.LikelyLeftover));
        Assert.All(rows, r => Assert.True(r.Orphaned));
        var vite = rows.Single(r => r.Ports.Contains(5173));
        Assert.Equal(21, vite.StopTarget.Root.Pid);
        Assert.Equal(11, vite.StopTarget.Members.Count);
    }

    [Fact]
    public void OneRowPerProcessWithAllItsPorts()
    {
        var r = Assert.Single(Build([new Listener(51837, 60, true), new Listener(15013, 60, true)], [P(1, "explorer"), P(60, "Code", 1)]));
        Assert.Equal([15013, 51837], r.Ports);
    }

    [Fact]
    public void OnlyDevServersCanBeStopped()
    {
        var rows = Build([new Listener(80, 60, false), new Listener(5173, 50, true)], [P(60, "httpd", 99), P(50, "node", 98)]);
        Assert.False(rows.Single(r => r.Proc.Name == "httpd").CanStop);
        Assert.True(rows.Single(r => r.Proc.Name == "node").CanStop);
    }

    [Theory]
    [InlineData(@"node ""C:\laragon\www\shipitlah\app\node_modules\.bin\..\.pnpm\vite@8.1.0\node_modules\vite\bin\vite.js""", @"shipitlah\app")]
    [InlineData(@"php.exe -S 127.0.0.1:8000 C:\laragon\www\shipitlah\app\vendor\laravel\framework\src\server.php", @"shipitlah\app")]
    [InlineData(@"""C:\Program Files\nodejs\node.exe"" --import file:///C:/laragon/www/my-office/node_modules/tsx/dist/loader.mjs server/index.ts", "my-office")]
    [InlineData(@"C:\laragon\bin\apache\httpd.exe", null)]
    public void ProjectFromCommandLine(string cmd, string? expected) =>
        Assert.Equal(expected, PortGrouper.ProjectFromCommand(cmd));

    [Fact]
    public void ConfirmTextForServer()
    {
        var ps = new[] { P(30, "node", 20, 50), P(40, "cmd", 30, 49), P(50, "node", 40, 48) };
        var r = Assert.Single(Build([new Listener(5173, 50, true)], ps));
        Assert.Equal("Stop the server on port 5173? This ends node and 2 related processes.",
            new ProcessKiller().ConfirmText(r.StopTarget, _ => false));
    }
}
