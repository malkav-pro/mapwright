[CmdletBinding(DefaultParameterSetName = "Run")]
param(
    [Parameter(Mandatory = $true, ParameterSetName = "Run")]
    [ValidateNotNullOrEmpty()]
    [string]$Case,

    [Parameter(Mandatory = $true, ParameterSetName = "List")]
    [switch]$ListCases,

    [Parameter(ParameterSetName = "Run")]
    [switch]$WindowedGpu
)

$ErrorActionPreference = "Stop"
if ($WindowedGpu -and $Case -notlike "*Gpu*" -and $Case -ne "__AcceptanceHardware") {
    throw "WindowedGpu is reserved for the GPU terrain and hardware acceptance cases."
}
$repo = Split-Path -Parent $PSScriptRoot
$commonGit = (& git -C $repo rev-parse --path-format=absolute --git-common-dir).Trim()
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $commonGit)) {
    throw "Could not resolve the source checkout from this worktree."
}
$sourceCheckout = Split-Path -Parent $commonGit
$toolsRoot = Join-Path $sourceCheckout ".tools"
$dotnetRoot = Join-Path $toolsRoot "dotnet-8.0.425"
$dotnet = Join-Path $dotnetRoot "dotnet.exe"
$godot = Join-Path $toolsRoot "godot-4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
$fixture = "C:\Users\almar\Downloads\Main Continent-backup-2026-09-21T18_46_35.150Z.ink"
$runtime = Join-Path $repo "artifacts\runtime-temp"
$buildTemp = Join-Path $runtime "temp"

if (!(Test-Path -LiteralPath $dotnet)) { throw "Portable .NET SDK not found at $dotnet" }
if (!(Test-Path -LiteralPath $godot)) { throw "Portable Godot .NET not found at $godot" }
if (!(Test-Path -LiteralPath $fixture)) { throw ".ink fixture not found at $fixture" }
New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_HOST_PATH = $dotnet
$env:DOTNET_CLI_HOME = Join-Path $runtime "dotnet-home"
$env:NUGET_PACKAGES = Join-Path $toolsRoot "nuget-packages"
$env:NUGET_HTTP_CACHE_PATH = Join-Path $runtime "nuget-http"
$env:APPDATA = Join-Path $runtime "appdata"
$env:LOCALAPPDATA = Join-Path $runtime "localappdata"
$env:TEMP = $buildTemp
$env:TMP = $buildTemp
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:MAPWRIGHT_INK_FIXTURE = $fixture
$env:MAPWRIGHT_GODOT_PATH = $godot
$env:PATH = "$dotnetRoot;$env:PATH"

Push-Location $repo
try {
    & $dotnet restore "Mapwright.csproj" --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet build "Mapwright.csproj" --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet restore "Tools\StorageCrashWorker\StorageCrashWorker.csproj" --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet build "Tools\StorageCrashWorker\StorageCrashWorker.csproj" --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if ($ListCases) {
        & $godot --headless --path $repo -- --connected-list-cases
    }
    elseif ($WindowedGpu -or $Case -like "Gpu*" -or $Case -like "__Gpu*") {
        # Godot's global RenderingDevice/display bridge is absent under --headless.
        # This case must draw a real CanvasItem on the pinned Vulkan renderer.
        & $godot --rendering-driver vulkan --path $repo -- --connected-case $Case
    }
    else {
        & $godot --headless --path $repo -- --connected-case $Case
    }
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
