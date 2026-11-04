using System.Text.Json;

namespace FanWatch.Core;

/// <summary>
/// Maps a running claude.exe to its project folder using Claude Code's (undocumented)
/// %USERPROFILE%\.claude\sessions\&lt;pid&gt;.json. Any problem yields null, never an exception.
/// </summary>
public sealed class ClaudeSessions(string sessionsDir)
{
    private static readonly TimeSpan StartTolerance = TimeSpan.FromSeconds(2);
    private readonly Dictionary<ProcKey, string?> _cache = new();

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sessions");

    internal int CachedCount => _cache.Count;

    /// <summary>Drops cached lookups for sessions that are no longer running.</summary>
    public void Prune(IReadOnlySet<ProcKey> alive)
    {
        foreach (var k in _cache.Keys.Where(k => !alive.Contains(k)).ToList()) _cache.Remove(k);
    }

    public string? ProjectFor(int pid, DateTime startUtc)
    {
        var key = new ProcKey(pid, startUtc);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var result = Load(pid, startUtc);
        // Cache hits only; a miss may be a session file not written yet.
        if (result != null) _cache[key] = result;
        return result;
    }

    private string? Load(int pid, DateTime startUtc)
    {
        try
        {
            var path = Path.Combine(sessionsDir, $"{pid}.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (root.TryGetProperty("procStart", out var ps) && ps.ValueKind == JsonValueKind.String
                && long.TryParse(ps.GetString(), out long ticks))
            {
                var fileStart = DateTime.FromFileTimeUtc(ticks);
                if ((fileStart - startUtc).Duration() > StartTolerance) return null;
            }

            if (!root.TryGetProperty("cwd", out var cwd) || cwd.ValueKind != JsonValueKind.String) return null;
            var folder = Path.GetFileName(cwd.GetString()!.TrimEnd('\\', '/'));
            return string.IsNullOrWhiteSpace(folder) ? null : folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
