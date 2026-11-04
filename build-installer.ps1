# Builds dist\FanWatch-Setup-<version>.exe: runs the tests, publishes a self-contained FanWatch (no .NET needed on the
# target laptop), then packages it with Inno Setup. Usage: powershell -ExecutionPolicy Bypass -File build-installer.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }

[xml]$proj = Get-Content (Join-Path $root 'src\FanWatch\FanWatch.csproj')
$version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw "No <Version> in FanWatch.csproj" }
"FanWatch $version"

"Running tests..."
& $dotnet test (Join-Path $root 'FanWatch.slnx') --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Tests failed; not building an installer." }

$app = Join-Path $root 'dist\app'
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
"Publishing self-contained build..."
& $dotnet publish (Join-Path $root 'src\FanWatch') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $app --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

"Packaging installer..."
& $iscc /Q "/DAppVersion=$version" "/DSourceDir=$app" (Join-Path $root 'installer\FanWatch.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)" }

$setup = Join-Path $root "dist\FanWatch-Setup-$version.exe"
"Built: $setup ({0:N1} MB)" -f ((Get-Item $setup).Length / 1MB)
