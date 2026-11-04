namespace FanWatch.Core;

/// <summary>Identifies one process instance; Windows reuses PIDs, so the start time is part of the identity.</summary>
public readonly record struct ProcKey(int Pid, DateTime StartUtc);

/// <summary>One process as seen at one refresh. <see cref="Name"/> has no ".exe".</summary>
public sealed record ProcSample(int Pid, int ParentPid, string Name, DateTime StartUtc, TimeSpan CpuTime, long PrivateMemory)
{
    public ProcKey Key => new(Pid, StartUtc);

    /// <summary>Windows session: 0 = services/SYSTEM (never endable by a normal user), 1+ = interactive logons.</summary>
    public int SessionId { get; init; } = 1;
}
