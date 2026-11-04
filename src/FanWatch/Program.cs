using System.Diagnostics;
using System.Runtime.InteropServices;
using FanWatch.UI;

namespace FanWatch;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\FanWatch.SingleInstance", out bool first);
        if (!first)
        {
            BringExistingToFront();
            return;
        }

        // Must come before any window is created. Any unexpected UI-thread error goes to the status line,
        // never a stack of exception dialogs.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        ApplicationConfiguration.Initialize();
        using (var screen = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = screen.DpiX / 96f;
        var expand = args.Where(a => a.StartsWith("--expand=", StringComparison.OrdinalIgnoreCase)).Select(a => a["--expand=".Length..]);
        var form = new MainForm(startOnPorts: args.Contains("--ports", StringComparer.OrdinalIgnoreCase), expandGroups: expand);
        Application.ThreadException += (_, e) => form.ShowError(e.Exception);
        Application.Run(form);
    }

    private static void BringExistingToFront()
    {
        var me = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(me.ProcessName))
        {
            using (p)
            {
                if (p.Id == me.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                ShowWindow(p.MainWindowHandle, 9 /* SW_RESTORE */);
                SetForegroundWindow(p.MainWindowHandle);
                return;
            }
        }
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
