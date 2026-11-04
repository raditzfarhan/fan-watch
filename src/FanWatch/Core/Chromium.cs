using System.Text.RegularExpressions;

namespace FanWatch.Core;

/// <summary>Knows how Chromium-based apps (Brave, Chrome, Edge, VS Code, Electron apps) label their processes and tabs.</summary>
public static class Chromium
{
    public static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "brave", "chrome", "msedge", "vivaldi", "opera", "arc", "chromium", "thorium",
    };

    private static readonly Regex TypeRx = new(@"--type=([\w-]+)", RegexOptions.Compiled);
    private static readonly Regex SubTypeRx = new(@"--utility-sub-type=([\w.]+)", RegexOptions.Compiled);

    private static readonly Regex DescriptionRx = new(@"^(?<kind>tab|embedded in a page|extension|worker): (?<title>.*?)(?: \+ (?<more>\d+) more)?$", RegexOptions.Compiled);

    public static string DisplayName(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    /// <summary>The browser's main process: ending it closes every window and tab, so its row gets no End button.</summary>
    public static bool IsWholeBrowser(string name, string? role) => Browsers.Contains(name) && role == "browser";

    /// <summary>A confirmation that says exactly what ending this browser process closes; null for non-browser processes.</summary>
    public static string? EndConfirmation(string name, int pid, string? role, string? tabDescription)
    {
        if (!Browsers.Contains(name)) return null;
        var b = DisplayName(name);
        switch (role)
        {
            case "browser":
                return $"End {b}'s MAIN process (PID {pid})? This closes ALL {b} windows and every open tab.";
            case "tab renderer" or "window renderer" or "extension":
                var m = tabDescription == null ? null : DescriptionRx.Match(tabDescription);
                if (m is not { Success: true })
                    return $"End a {b} tab process (PID {pid})? Whichever tab uses it stops and shows \"Aw, Snap!\"; the rest of {b} keeps running.";
                var title = m.Groups["title"].Value;
                if (m.Groups["more"].Success)
                    return $"End the {b} process for \"{title}\" and {m.Groups["more"].Value} other tabs/pages (PID {pid})? Those tabs stop: {b} shows \"Aw, Snap!\" in them and everything else keeps running.";
                return m.Groups["kind"].Value == "extension"
                    ? $"End the {b} extension \"{title}\" (PID {pid})? The extension stops until {b} restarts it; your tabs keep running."
                    : $"End the {b} tab \"{title}\" (PID {pid})? Only that tab stops: {b} shows \"Aw, Snap!\" in it and everything else keeps running.";
            default:
                return $"End {b}'s {role ?? "helper"} process (PID {pid})? {b} restarts it automatically; pages may flicker or reload.";
        }
    }

    /// <summary>What a Chromium process does, from its command line; null if it isn't a Chromium process.</summary>
    public static string? Role(string name, string? commandLine)
    {
        if (commandLine == null) return null;
        var type = TypeRx.Match(commandLine);
        if (!type.Success) return Browsers.Contains(name) ? "browser" : null;

        switch (type.Groups[1].Value)
        {
            case "renderer":
                if (commandLine.Contains("--extension-process", StringComparison.Ordinal)) return "extension";
                return Browsers.Contains(name) ? "tab renderer" : "window renderer";
            case "gpu-process": return "GPU";
            case "crashpad-handler": return "crash reporter";
            case "utility":
                var sub = SubTypeRx.Match(commandLine);
                var service = sub.Success ? sub.Groups[1].Value : "";
                if (service.StartsWith("network.", StringComparison.Ordinal)) return "network";
                if (service.StartsWith("audio.", StringComparison.Ordinal)) return "audio";
                if (service.StartsWith("storage.", StringComparison.Ordinal)) return "storage";
                if (service.StartsWith("video_capture.", StringComparison.Ordinal)) return "video capture";
                return "utility";
            default: return type.Groups[1].Value;
        }
    }
}
