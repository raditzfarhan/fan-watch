# Builds FanWatch and installs it to %LOCALAPPDATA%\FanWatch with Desktop and Start menu shortcuts.
# Re-running updates the install and overwrites both shortcuts.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'FanWatch'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }

# Close a running FanWatch so its exe can be replaced (force it if it hasn't closed after 3 s).
Get-Process FanWatch -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    if (-not $_.WaitForExit(3000)) { $_.Kill(); $_.WaitForExit(3000) | Out-Null }
}

& $dotnet publish (Join-Path $root 'src\FanWatch') -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -o $dest --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$exe = Join-Path $dest 'FanWatch.exe'
$shell = New-Object -ComObject WScript.Shell
$links = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'FanWatch.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'FanWatch.lnk')
)
foreach ($path in $links) {
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $dest
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = 'See what is making your fan loud'
    $lnk.Save()
    "shortcut: $path"
}
"installed: $exe"
