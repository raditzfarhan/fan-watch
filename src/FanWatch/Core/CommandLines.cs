using System.Runtime.InteropServices;

namespace FanWatch.Core;

/// <summary>Reads another process's command line (NtQueryInformationProcess class 60). Cached per process instance; null when not allowed.</summary>
public sealed unsafe class CommandLines
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessCommandLineInformation = 60;
    private readonly Dictionary<ProcKey, string?> _cache = new();

    public string? For(ProcSample s)
    {
        if (_cache.TryGetValue(s.Key, out var cached)) return cached;
        var value = Query(s.Pid);
        if (_cache.Count > 2048) _cache.Clear();
        _cache[s.Key] = value;
        return value;
    }

    public void Prune(IReadOnlySet<ProcKey> alive)
    {
        foreach (var k in _cache.Keys.Where(k => !alive.Contains(k)).ToList()) _cache.Remove(k);
    }

    private static string? Query(int pid)
    {
        IntPtr h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            int size = 4096;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var buffer = new byte[size];
                fixed (byte* buf = buffer)
                {
                    int status = NtQueryInformationProcess(h, ProcessCommandLineInformation, buf, size, out int needed);
                    if (status == unchecked((int)0xC0000004) /* STATUS_INFO_LENGTH_MISMATCH */) { size = Math.Max(needed, size * 2); continue; }
                    if (status != 0) return null;
                    // UNICODE_STRING { ushort Length; ushort MaxLength; (pad); char* Buffer } pointing into our buffer.
                    int len = *(ushort*)buf / 2;
                    char* text = *(char**)(buf + 8);
                    if (len == 0 || text == null || (byte*)text < buf || (byte*)(text + len) > buf + size) return null;
                    return new string(text, 0, len).Trim();
                }
            }
            return null;
        }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr h, int infoClass, byte* info, int length, out int returned);
}
