# FanWatch: design

Date: 2026-11-04
Status: reviewed, awaiting owner approval

## Why this exists

The owner's Windows 11 laptop (ASUS Zenbook, 20 logical cores, 31.6 GB RAM) sometimes runs its fan loud, and they want to find out the cause immediately. Task Manager doesn't answer this well. The real cause on 2026-11-03 was a Claude Code session (`my-office` project) that kept about 1 core busy for 11.5 hours. It never topped Task Manager's instant CPU list (about 5% of the whole machine), but it had used 485 CPU-minutes since it started. The fan follows *sustained* load, not momentary spikes.

FanWatch is a small desktop app. You open it when the fan is loud. It shows which apps are using CPU and memory, flags the one most likely heating the laptop, and lets you end it.

## Goals

1. When opened, show at a glance which apps are using the most CPU and memory.
2. Flag the single most likely fan culprit, based on sustained CPU use, the moment the window opens. It must not need to have been running beforehand.
3. Let the owner end an app or process with a button.
4. Use very little CPU and memory itself.

## Non-goals

- No background monitoring, tray-resident mode or notifications. The owner chose "show when open only".
- No fan RPM or temperature readings. Windows doesn't expose these to normal apps without vendor drivers or admin-level sensor libraries.
- No history or logging across runs.
- No GPU breakdown in v1. The GPU was idle in the investigation. A builder may add a GPU column only if it's cheap; it's not required.
- No managing startup apps, services or power plans.

## Anti-goals (failure even if it works)

- FanWatch itself shows up as a noticeable CPU or RAM user. Target: under 1% of total CPU while the window is open, about 60 MB RAM or less, nothing running when closed.
- An accidental click crashes Windows or loses work without warning.
- Opening it shows a blank or useless culprit for the first minute.

## The screen (owner chose layout B, a sortable table)

A single window, roughly like a cut-down Task Manager.

**Top strip:** overall CPU %, RAM used / total, and battery / charging state (for example "Charging 79%"). Charging heat is a common reason for a loud fan even when the CPU is quiet.

**Table:** one row per app, with related processes grouped under their process name (for example "Brave (42)", "VS Code (45)"). Expanding a group shows its individual processes.

Columns:

| Column | Meaning |
|---|---|
| Process | Name, plus count for groups. Claude Code rows show their project (see below). |
| CPU now | % of the whole machine (all cores), from the last refresh interval. |
| CPU avg 60 s | Rolling average while the window is open. Shows "…" until it has data. |
| CPU since start | Total CPU time ÷ how long the process has been running, as % of the whole machine. Available instantly on open. Also show the raw total (for example "485 min") in a tooltip or a secondary column. |
| Memory | Working set (for groups, the sum). |
| End | Button (see "Ending processes"). |

- Clicking a column header sorts by it. Default sort: the culprit score, highest first.
- Refresh every 2 seconds while the window is open.

**Fan culprit:** exactly one row is highlighted red with a 🔥.
- Scoring is per individual process, never summed across a name group, so "Brave (42)" or "svchost (95)" can't beat a single claude.exe keeping one core busy just by having many members. *(My call.)*
- If the winner sits inside a collapsed group, the group row gets the 🔥 and auto-expands to show the winning process. *(My call.)*
- The winner is the process with the highest sustained load, combining "CPU since start" (weighted by how long it has run) and "CPU avg 60 s". The exact scoring is left to the builder, with these constraints: a process running 1 core for hours must beat a 2-second spike, and a short-lived process at 100% for the last minute must also be able to win. If nothing is using meaningful CPU (overall under ~15% and no process sustaining over ~3% of the machine), show "Nothing is working hard right now. Fan noise may be from charging or the laptop cooling down." and highlight no row.

**Claude Code sessions:** each `claude.exe` row is labelled with its project. Claude Code writes `%USERPROFILE%\.claude\sessions\<pid>.json`, which includes `cwd` and `name`. Show `claude (<last folder of cwd>)`. This file format is undocumented, so if it's missing or unreadable, fall back to plain `claude` with no error. Claude processes are shown individually, not merged into one group, because each one is a separate terminal session.

**Claude session child processes** *(my call)*: a Claude session's descendants (node MCP servers, shells, dev servers) count toward that session's row, so CPU used by its child processes still points at "claude (my-office)". Find them by walking the parent-PID tree, and check each child's start time is after its parent's to guard against Windows reusing PIDs. Expanding a claude row lists its children. Descendants of other apps don't need this; they keep normal name grouping.

**Culprit that can't be ended** *(my call)*: if the top process is protected or refuses to end (MsMpEng, svchost, System, Memory Compression, elevated processes), still highlight it honestly with no End button and a short hint where known (for example "Windows Defender scan: usually finishes on its own"). If the top culprit can't be ended, the next process that *can* be ended is marked with a smaller secondary marker.

## Ending processes

- Every row, and every group row, has an End button.
- Clicking it asks for confirmation: "End claude (my-office)? Anything unsaved in it will be lost." On a group: "End all 42 Brave processes?"
- Protected processes have no End button: System, Idle, Registry, smss, csrss, wininit, winlogon, services, lsass, svchost, dwm, fontdrvhost, Memory Compression, MsMpEng, and FanWatch itself.
- How it ends *(my call)*: for an app with a window, first ask it to close (like clicking its X), then force-stop it if it's still running after 3 seconds. Console and background processes are force-stopped straight away. The confirmation wording stays the same either way.
- What gets ended *(my call)*: a claude row ends the session plus all its child processes (dev servers included), and the confirmation says so: "End claude (my-office) and its 3 child processes, including its dev server?" Other rows and groups end only the processes listed.
- If ending fails (access denied, already gone), show a short message in the window and don't crash. The app runs as a normal user, not elevated.

## How it's built (owner-approved and my calls)

- **Platform:** native Windows desktop app in C# on .NET. The owner approved installing the .NET SDK (not currently installed; Node 22 and Python 3.10 are present). Electron and other browser-based shells are ruled out because they'd break the "stays light" anti-goal. *(Platform: owner-approved.)*
- **.NET version:** current LTS (.NET 10). *(My call.)*
- **UI toolkit:** WinForms, with a dark theme matching the mockup. It's the lightest option and fine for a single table window. *(My call. Builder may use WPF if they have a strong reason.)*
- **Launching:** a desktop shortcut and a Start menu entry. No tray icon. Closing the window exits the app. *(My call, following "show when open only".)*
- **Location:** source in `C:\laragon\www\claude\fanwatch\`. This is not a git repo.

## Left to the builder

- Exact culprit scoring formula, within the constraints above.
- How CPU and memory are read (for example `Process.TotalProcessorTime` deltas vs performance counters). It must respect the "stays light" target; `Get-Counter`-style wildcard counters were slow and error-prone in testing.
- Whether the build is framework-dependent or self-contained, and how the shortcuts are created (a small install script is fine).
- Visual details beyond the mockup: dark theme, compact rows, red culprit row.

Reference mockup: `fanwatch\.superpowers\brainstorm\25196-1793760874\content\layout.html`, option B.
