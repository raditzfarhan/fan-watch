using System.Runtime.InteropServices;

namespace FanWatch.Core;

public interface IProcessSource
{
    /// <summary>Every process except Idle (PID 0).</summary>
    IReadOnlyList<ProcSample> Read();

    /// <summary>Idle (PID 0) CPU time from the last <see cref="Read"/>, summed over all cores.</summary>
    TimeSpan IdleCpuTime { get; }
}

/// <summary>
/// Reads all processes with one NtQuerySystemInformation(SystemProcessInformation) call.
/// No process handles are opened, so protected processes never cause access-denied errors.
/// </summary>
public sealed unsafe class NtProcessSource : IProcessSource
{
    private const int SystemProcessInformation = 5;
    private const uint StatusInfoLengthMismatch = 0xC0000004;

    // SYSTEM_PROCESS_INFORMATION field offsets on 64-bit Windows.
    private const int OffPrivateWorkingSet = 8, OffCreateTime = 32, OffUserTime = 40, OffKernelTime = 48,
        OffNameLength = 56, OffNameBuffer = 64, OffPid = 80, OffParentPid = 88, OffSessionId = 100;
    private const int MinEntrySize = 152; // through WorkingSetSize; every field we read lies inside this

    private byte[] _buffer = new byte[512 * 1024];
    private int _lastLength;

    public TimeSpan IdleCpuTime { get; private set; }

    public NtProcessSource()
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("FanWatch needs 64-bit Windows.");
    }

    public IReadOnlyList<ProcSample> Read()
    {
        while (true)
        {
            fixed (byte* buf = _buffer)
            {
                uint status = NtQuerySystemInformation(SystemProcessInformation, buf, _buffer.Length, out int returned);
                if (status == StatusInfoLengthMismatch)
                {
                    _buffer = new byte[Math.Max(returned, _buffer.Length) + 64 * 1024];
                    continue;
                }
                if (status != 0) throw new InvalidOperationException($"NtQuerySystemInformation failed: 0x{status:X8}");
                _lastLength = returned > 0 ? Math.Min(returned, _buffer.Length) : _buffer.Length;
                return Parse(buf, _lastLength);
            }
        }
    }

    /// <summary>Test hook: re-parses the last buffer as if only <paramref name="length"/> bytes had been returned.</summary>
    internal IReadOnlyList<ProcSample> ParseLastBuffer(int length)
    {
        fixed (byte* buf = _buffer) return Parse(buf, Math.Clamp(length, 0, _lastLength));
    }

    private List<ProcSample> Parse(byte* buf, int length)
    {
        var list = new List<ProcSample>(400);
        long offset = 0;
        while (offset >= 0 && offset + MinEntrySize <= length)
        {
            byte* e = buf + offset;
            int pid = (int)*(long*)(e + OffPid);
            var cpu = TimeSpan.FromTicks(*(long*)(e + OffUserTime) + *(long*)(e + OffKernelTime));

            if (pid == 0)
            {
                IdleCpuTime = cpu;
            }
            else
            {
                int nameLen = *(ushort*)(e + OffNameLength) / 2;
                char* namePtr = *(char**)(e + OffNameBuffer);
                // The name buffer lives inside our buffer; ignore it if it points anywhere else.
                bool nameInside = namePtr != null && (byte*)namePtr >= buf && (byte*)(namePtr + nameLen) <= buf + length;
                string name = nameLen > 0 && nameInside ? new string(namePtr, 0, nameLen) : (pid == 4 ? "System" : "Unknown");
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

                long create = *(long*)(e + OffCreateTime);
                var start = create > 0 ? DateTime.FromFileTimeUtc(create) : DateTime.MinValue.ToUniversalTime();

                list.Add(new ProcSample(pid, (int)*(long*)(e + OffParentPid), name, start, cpu, *(long*)(e + OffPrivateWorkingSet))
                {
                    SessionId = (int)*(uint*)(e + OffSessionId),
                });
            }

            uint next = *(uint*)e;
            if (next == 0) break;
            offset += next;
        }
        return list;
    }

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(int infoClass, byte* info, int length, out int returnLength);
}
