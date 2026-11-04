using System.Runtime.InteropServices;
using System.Text;

namespace FanWatch.Core;

/// <summary>Finds a browser's top-level windows (main windows and its Task Manager).</summary>
public static class BrowserTabs
{
    /// <summary>Visible, titled top-level windows owned by any of these PIDs (a browser can have several windows).</summary>
    public static IReadOnlyList<(IntPtr Handle, int Pid, string Title)> WindowsOf(IReadOnlySet<int> pids)
    {
        var found = new List<(IntPtr, int, string)>();
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out int pid);
            if (!pids.Contains(pid) || !IsWindowVisible(h) || GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
            var sb = new StringBuilder(512);
            if (GetWindowText(h, sb, sb.Capacity) > 0) found.Add((h, pid, sb.ToString()));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
}
