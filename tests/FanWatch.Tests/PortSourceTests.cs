using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace FanWatch.Tests;

public class PortSourceTests
{
    [Fact]
    public void SeesOwnLoopbackListener()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try
        {
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            var mine = new TcpPortSource().Read().Single(x => x.Port == port);
            Assert.Equal(Environment.ProcessId, mine.Pid);
            Assert.True(mine.LoopbackOnly);
        }
        finally { l.Stop(); }
    }

    [Fact]
    public void SeesIPv6AnyListener()
    {
        var l = new TcpListener(IPAddress.IPv6Any, 0);
        l.Start();
        try
        {
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            var mine = new TcpPortSource().Read().Single(x => x.Port == port);
            Assert.Equal(Environment.ProcessId, mine.Pid);
            Assert.False(mine.LoopbackOnly);
        }
        finally { l.Stop(); }
    }

    [Fact]
    public void StoppedListenerIsGone()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        Assert.DoesNotContain(new TcpPortSource().Read(), x => x.Port == port && x.Pid == Environment.ProcessId);
    }
}

public class CommandLinesTests
{
    [Fact]
    public void ReadsAnotherProcessCommandLine()
    {
        using var ping = Process.Start(new ProcessStartInfo("ping", "-n 20 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        try
        {
            var s = new NtProcessSource().Read().Single(p => p.Pid == ping.Id);
            Assert.Contains("-n 20 127.0.0.1", new CommandLines().For(s));
        }
        finally { ping.Kill(); }
    }

    [Fact]
    public void ProtectedProcessGivesNullNotException()
    {
        var csrss = new NtProcessSource().Read().First(p => p.Name.Equals("csrss", StringComparison.OrdinalIgnoreCase));
        Assert.Null(new CommandLines().For(csrss));
    }

    [Fact]
    public void GoneProcessGivesNull() =>
        Assert.Null(new CommandLines().For(Fakes.P(999_999, "ghost")));
}
