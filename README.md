# FanWatch

See what is making your laptop's fan loud, and stop it.

FanWatch is a small Windows desktop app. Open it while the fan is roaring and it tells you which program is to blame, why, and lets you end it with one click.

## Features

- **"Why your fan is loud" card**: names the most likely culprit and explains why in plain words (for example "Busy since it started 11 h ago, using about one full core"), with a chart of its last minute. When nothing is busy it says so.
- **Process table**: CPU now, 1-minute average, CPU since start and memory for every program, with app icons and bars. Processes are grouped by program, and each Claude Code session is grouped with everything it started.
- **Safe End button**: always asks first and says exactly what will close. Windows' own processes and elevated processes are never ended. A browser's main process has no End button, because ending it closes every window.
- **Browser tabs**: with Brave's or Chrome's Task Manager open (Shift+Esc), FanWatch shows which tab each browser process belongs to.
- **Ports tab**: lists programs listening on network ports and flags dev servers that look left running (for example started by a Claude session that has finished), with a Stop button.
- **Light on your laptop**: about 0.5% CPU and 35 MB of memory, and it only works while its window is open.

## Install

Download or build `FanWatch-Setup-<version>.exe` and run it. It installs for the current user (no admin rights needed) and adds FanWatch to the Start menu, with an optional desktop shortcut. Uninstall from Windows Settings > Apps.

The installer is not code-signed, so Windows SmartScreen may warn the first time: choose **More info > Run anyway**.

Requires Windows 10 (1809) or later, 64-bit. The installer includes .NET, so nothing else is needed.

## Build from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download). For the installer, also [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

```powershell
dotnet build FanWatch.slnx               # build
dotnet test FanWatch.slnx                # run the tests
.\build-installer.ps1                    # tests + self-contained build + dist\FanWatch-Setup-<version>.exe
.\install.ps1                            # or: quick install to %LOCALAPPDATA%\FanWatch for development
```

The version comes from `<Version>` in `src/FanWatch/FanWatch.csproj`.

Command-line options: `--ports` opens on the Ports tab; `--expand=<name>` expands a program group (for example `--expand=brave`).

## How it works

- Process CPU and memory come from `NtQuerySystemInformation`, sampled every 2 seconds. CPU figures are a share of the whole machine.
- The culprit is scored on both long-term use (CPU since start, weighted by age) and recent use (the last minute), so a program that has quietly used a core for hours is caught as well as a sudden spike.
- Claude Code sessions are recognised from `%USERPROFILE%\.claude\sessions`.
- Listening ports come from `GetExtendedTcpTable`; a dev server counts as left running when whatever started it has exited.
- Browser tab names are read from the browser's own Task Manager window through UI Automation (Windows offers no other way to match a browser process to its tab).

## Project layout

```
src/FanWatch/Core    data, scoring and process handling (no UI)
src/FanWatch/UI      the window and its owner-drawn controls (WinForms)
tests/FanWatch.Tests xUnit tests
installer/           Inno Setup script
docs/                original design and plan
```
