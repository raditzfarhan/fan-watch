using System.ComponentModel;
using System.Diagnostics;

namespace FanWatch.Core;

public sealed record KillResult(int Ended, int AlreadyGone, int Failed, string Message);

/// <summary>
/// Ends the processes behind a row. Windowed members are asked to close first (like clicking X) so apps such as
/// browsers shut down cleanly; whatever is still running after the grace period is force-stopped, deepest child first.
/// Runs off the UI thread.
/// </summary>
public sealed class ProcessKiller
{
    private static readonly TimeSpan StartTolerance = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(3);

    /// <summary>Members FanWatch will actually try to end: never protected names or session-0 services.</summary>
    public static bool WillAttempt(ProcSample m) => !Protection.IsProtected(m.Name, m.Pid) && m.SessionId != 0;

    public string ConfirmText(Row row, Func<int, bool>? hasWindow = null)
    {
        hasWindow ??= HasMainWindow;
        switch (row.Kind)
        {
            case RowKind.Group when Chromium.Browsers.Contains(row.Root.Name):
                var browser = Chromium.DisplayName(row.Root.Name);
                return $"Close {browser} completely? This ends all {row.Members.Count(WillAttempt)} {browser} processes: every window and every open tab.";
            case RowKind.Group:
                return $"End all {row.Members.Count(WillAttempt)} {row.Root.Name} processes?";
            case RowKind.Server:
                var listener = row.Listener ?? row.Root;
                int related = row.Members.Count(m => m.Pid != listener.Pid && WillAttempt(m));
                var also = related == 0 ? "" : $" and {related} related process{(related == 1 ? "" : "es")}";
                return $"Stop the server on {row.Label}? This ends {listener.Name}{also}.";
            case RowKind.ClaudeSession:
                var kids = row.Members.Where(m => m.Pid != row.Root.Pid && WillAttempt(m)).ToList();
                if (kids.Count == 0) break;
                var apps = kids.Where(m => hasWindow(m.Pid)).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var including = apps.Count > 0 ? $", including {string.Join(", ", apps)}" : "";
                return $"End {row.Label} and its {kids.Count} child process{(kids.Count == 1 ? "" : "es")}{including}? Anything unsaved in it will be lost.";
        }
        return $"End {row.Label}? Anything unsaved in it will be lost.";
    }

    public Task<KillResult> EndAsync(Row row, TimeSpan closeGrace) => Task.Run(() => End(row, closeGrace));

    private enum Outcome { Ended, Gone, Failed }

    private static async Task<KillResult> End(Row row, TimeSpan closeGrace)
    {
        var outcomes = new Dictionary<ProcKey, Outcome>();
        var live = new List<(ProcSample Sample, Process Proc)>();

        // Open every target first, verifying it is still the same process (PID reuse).
        foreach (var m in row.Members.Where(WillAttempt))
        {
            var p = Open(m, out var early);
            if (p == null) outcomes[m.Key] = early;
            else live.Add((m, p));
        }

        try
        {
            // 1. Ask windowed members to close, then give them the grace period together.
            var closing = live.Where(x => SafeHasWindow(x.Proc) && SafeCloseWindow(x.Proc)).ToList();
            if (closing.Count > 0)
            {
                using var cts = new CancellationTokenSource(closeGrace);
                try { await Task.WhenAll(closing.Select(x => x.Proc.WaitForExitAsync(cts.Token))); }
                catch (OperationCanceledException) { /* some are still open: they get force-stopped below */ }
            }

            // 2. Force-stop whatever is left, deepest child first (members are parent-before-child).
            foreach (var (s, p) in Enumerable.Reverse(live))
            {
                if (SafeExited(p)) { outcomes[s.Key] = Outcome.Ended; continue; }
                outcomes[s.Key] = await Kill(p);
            }
        }
        finally
        {
            foreach (var (_, p) in live) p.Dispose();
        }

        int ended = outcomes.Values.Count(o => o == Outcome.Ended);
        int gone = outcomes.Values.Count(o => o == Outcome.Gone);
        int failed = outcomes.Values.Count(o => o == Outcome.Failed);
        return new KillResult(ended, gone, failed, Describe(ended, gone, failed));
    }

    private static Process? Open(ProcSample s, out Outcome outcome)
    {
        outcome = Outcome.Gone;
        Process p;
        try { p = Process.GetProcessById(s.Pid); }
        catch (ArgumentException) { return null; }
        try
        {
            if (p.HasExited || (p.StartTime.ToUniversalTime() - s.StartUtc).Duration() > StartTolerance)
            {
                p.Dispose();
                return null;
            }
            return p;
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            outcome = e is Win32Exception ? Outcome.Failed : Outcome.Gone;
            p.Dispose();
            return null;
        }
    }

    private static async Task<Outcome> Kill(Process p)
    {
        try
        {
            p.Kill();
            using var cts = new CancellationTokenSource(KillWait);
            await p.WaitForExitAsync(cts.Token);
            return Outcome.Ended;
        }
        catch (OperationCanceledException) { return Outcome.Failed; } // refused to die
        catch (InvalidOperationException) { return Outcome.Ended; }   // exited on its own just now
        catch (Win32Exception) { return Outcome.Failed; }
    }

    private static bool HasMainWindow(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.MainWindowHandle != IntPtr.Zero; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return false; }
    }

    private static bool SafeHasWindow(Process p) { try { return p.MainWindowHandle != IntPtr.Zero; } catch (InvalidOperationException) { return false; } }
    private static bool SafeCloseWindow(Process p) { try { return p.CloseMainWindow(); } catch (InvalidOperationException) { return false; } }
    private static bool SafeExited(Process p) { try { return p.HasExited; } catch (Exception e) when (e is InvalidOperationException or Win32Exception) { return false; } }

    internal static string Describe(int ended, int gone, int failed)
    {
        var parts = new List<string>();
        if (ended > 0) parts.Add($"Ended {ended} process{(ended == 1 ? "" : "es")}.");
        if (failed > 0) parts.Add($"Could not end {failed} process{(failed == 1 ? "" : "es")} (access denied).");
        if (parts.Count == 0) parts.Add("Already closed.");
        return string.Join(" ", parts);
    }
}
