using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace FanWatch.Core;

/// <summary>Processes FanWatch never offers to end, plus friendly hints for common busy Windows processes.</summary>
public static class Protection
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "svchost", "dwm", "fontdrvhost", "Memory Compression", "MsMpEng", "FanWatch",
    };

    private static readonly Dictionary<string, string> Hints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MsMpEng"] = "Defender is scanning files: often caused by builds or installs",
        ["svchost"] = "Windows service (often Windows Update)",
        ["System"] = "Windows kernel and drivers",
        ["Memory Compression"] = "Windows is compressing memory: RAM is nearly full",
    };

    // One OpenProcess probe per process instance; elevated processes in the user's session refuse PROCESS_TERMINATE.
    private static readonly ConcurrentDictionary<ProcKey, bool> Probed = new();
    private const int MaxProbeCache = 4096;

    public static bool IsProtected(string name, int pid) => pid == Environment.ProcessId || Names.Contains(name);

    /// <summary>True if a normal user could end this process: not on the protected list, not a session-0 service, not elevated.</summary>
    public static bool IsEndable(ProcSample s)
    {
        if (IsProtected(s.Name, s.Pid) || s.SessionId == 0) return false;
        if (Probed.Count > MaxProbeCache) Probed.Clear();
        return Probed.GetOrAdd(s.Key, _ => CanTerminate(s.Pid));
    }

    public static string? HintFor(string name) => Hints.GetValueOrDefault(name);

    private static bool CanTerminate(int pid)
    {
        IntPtr h = OpenProcess(0x0001 /* PROCESS_TERMINATE */, false, pid);
        if (h != IntPtr.Zero) { CloseHandle(h); return true; }
        // Only "access denied" means unendable; a process that's already gone (or a fake test PID) isn't held against it.
        return Marshal.GetLastPInvokeError() != 5;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
}
