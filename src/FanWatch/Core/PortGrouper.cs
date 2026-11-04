using System.Text.RegularExpressions;

namespace FanWatch.Core;

/// <summary>One program that is listening on one or more ports, who started it, and whether it looks forgotten.</summary>
public sealed class PortRow
{
    public required IReadOnlyList<int> Ports { get; init; }
    public required bool LoopbackOnly { get; init; }
    public required ProcSample Proc { get; init; }
    public string? Command { get; init; }
    /// <summary>Project folder guessed from the command line, e.g. "shipitlah\app".</summary>
    public string? Project { get; init; }
    /// <summary>"claude (project)" when a running Claude session started it.</summary>
    public string? StartedBy { get; init; }
    /// <summary>Nothing that owns it (terminal, IDE, Claude, Explorer) is still running: whoever started it has exited.</summary>
    public bool Orphaned { get; init; }
    public bool IsDevServer { get; init; }
    public bool Protected { get; init; }
    public bool LikelyLeftover => IsDevServer && (StartedBy != null || Orphaned);
    public bool CanStop => IsDevServer && !Protected;
    /// <summary>What "Stop" ends: the listener plus the launcher chain above it and their children (the whole stack, if orphaned).</summary>
    public required Row StopTarget { get; init; }
}

public static class PortGrouper
{
    /// <summary>Runtimes that dev servers run on.</summary>
    private static readonly HashSet<string> DevRuntimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "node", "php", "python", "python3", "pythonw", "py", "ruby", "deno", "bun", "java", "dotnet", "go",
        "uvicorn", "gunicorn", "hypercorn", "flask", "rails", "puma", "air", "hugo",
    };

    /// <summary>Processes between a shell and a dev server (package runners, wrappers); stopping a server ends these too.</summary>
    private static readonly HashSet<string> Launchers = new(DevRuntimes, StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "npm", "npx", "pnpm", "yarn", "uv", "concurrently", "nodemon",
    };

    /// <summary>Long-lived programs that own the things they start. Reaching one means the server still has an owner.</summary>
    private static readonly HashSet<string> Owners = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude", "explorer", "WindowsTerminal", "Code", "Cursor", "Windsurf", "devenv", "idea64", "phpstorm64", "webstorm64",
        "pycharm64", "rider64", "goland64", "wezterm-gui", "alacritty", "mintty", "Hyper", "Tabby", "ConEmu64", "laragon",
    };

    private static readonly HashSet<string> GenericFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "src", "web", "www", "frontend", "backend", "client", "server", "api", "site",
    };

    private static readonly Regex PathRx = new(@"[A-Za-z]:[\\/][^""\s]*", RegexOptions.Compiled);

    public static IReadOnlyList<PortRow> Build(IReadOnlyList<Listener> listeners, IReadOnlyList<ProcSample> samples,
        ClaudeSessions claude, Func<ProcSample, string?> commandFor, DateTime nowUtc)
    {
        var byPid = new Dictionary<int, ProcSample>(samples.Count);
        foreach (var s in samples) byPid[s.Pid] = s;
        var children = samples.Where(s => s.ParentPid != s.Pid).ToLookup(s => s.ParentPid);

        var rows = new List<PortRow>();
        foreach (var g in listeners.GroupBy(l => l.Pid))
        {
            if (!byPid.TryGetValue(g.Key, out var proc) || proc.SessionId == 0) continue;
            var ports = g.Select(l => l.Port).Distinct().Order().ToList();

            var chain = Ancestors(proc, byPid);
            var owner = chain.FirstOrDefault(a => Owners.Contains(a.Name));
            bool orphaned = owner == null;
            string? startedBy = owner != null && owner.Name.Equals("claude", StringComparison.OrdinalIgnoreCase)
                ? (claude.ProjectFor(owner.Pid, owner.StartUtc) is string project ? $"claude ({project})" : "claude")
                : null;

            // Owned: climb only through launchers (never a shell, terminal or Claude).
            // Orphaned: everything left above it is the abandoned stack, so take the top-most surviving ancestor.
            var top = proc;
            if (orphaned)
            {
                if (chain.Count > 0) top = chain[^1];
            }
            else
            {
                foreach (var a in chain)
                {
                    if (!Launchers.Contains(a.Name)) break;
                    top = a;
                }
            }
            var members = new List<ProcSample> { top };
            CollectDescendants(top, children, members);

            var command = commandFor(proc);
            rows.Add(new PortRow
            {
                Ports = ports,
                LoopbackOnly = g.All(l => l.LoopbackOnly),
                Proc = proc,
                Command = command,
                Project = command == null ? null : ProjectFromCommand(command),
                StartedBy = startedBy,
                Orphaned = orphaned,
                IsDevServer = DevRuntimes.Contains(proc.Name),
                Protected = !Protection.IsEndable(proc),
                StopTarget = new Row
                {
                    Label = ports.Count == 1 ? $"port {ports[0]}" : $"ports {string.Join(", ", ports)}",
                    Kind = RowKind.Server,
                    Root = top,
                    Listener = proc,
                    Members = members,
                },
            });
        }

        return rows.OrderByDescending(r => r.LikelyLeftover).ThenByDescending(r => r.IsDevServer).ThenBy(r => r.Ports[0]).ToList();
    }

    /// <summary>The project folder a dev server belongs to: the folder holding node_modules or vendor in its command line.</summary>
    public static string? ProjectFromCommand(string command)
    {
        foreach (Match m in PathRx.Matches(command))
        {
            var segments = m.Value.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            int i = Array.FindIndex(segments, s => s.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                                                || s.Equals("vendor", StringComparison.OrdinalIgnoreCase));
            if (i < 2) continue; // need at least drive + project folder
            var folder = segments[i - 1];
            return GenericFolders.Contains(folder) && i >= 3 ? $@"{segments[i - 2]}\{folder}" : folder;
        }
        return null;
    }

    /// <summary>Parents, nearest first. Stops when a parent is gone or is a newer process reusing the PID.</summary>
    private static List<ProcSample> Ancestors(ProcSample p, Dictionary<int, ProcSample> byPid)
    {
        var chain = new List<ProcSample>();
        var cur = p;
        for (int depth = 0; depth < 64; depth++)
        {
            if (!byPid.TryGetValue(cur.ParentPid, out var parent) || parent.StartUtc > cur.StartUtc || parent.Pid == cur.Pid) break;
            chain.Add(parent);
            cur = parent;
        }
        return chain;
    }

    private static void CollectDescendants(ProcSample parent, ILookup<int, ProcSample> children, List<ProcSample> into)
    {
        foreach (var child in children[parent.Pid])
        {
            if (child.StartUtc < parent.StartUtc || into.Contains(child)) continue;
            into.Add(child);
            CollectDescendants(child, children, into);
        }
    }
}
