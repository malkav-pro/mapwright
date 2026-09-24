param(
    [string]$InkFixture = "C:\Users\almar\Downloads\Main Continent-backup-2026-09-21T18_46_35.150Z.ink"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$dotnetRoot = Join-Path $repo ".tools\dotnet-8.0.425"
$dotnet = Join-Path $dotnetRoot "dotnet.exe"
$godot = Join-Path $repo ".tools\godot-4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"

if (!(Test-Path -LiteralPath $dotnet)) { throw "Portable .NET SDK not found at $dotnet" }
if (!(Test-Path -LiteralPath $godot)) { throw "Portable Godot .NET not found at $godot" }
if (!(Test-Path -LiteralPath $InkFixture)) { throw ".ink fixture not found at $InkFixture" }

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_HOST_PATH = $dotnet
$env:DOTNET_CLI_HOME = Join-Path $repo ".tools\dotnet-home"
$env:NUGET_PACKAGES = Join-Path $repo ".tools\nuget-packages"
$env:APPDATA = Join-Path $repo ".tools\appdata"
$env:LOCALAPPDATA = Join-Path $repo ".tools\localappdata"
$buildTemp = Join-Path $repo ".tools\temp"
New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null
$env:TEMP = $buildTemp
$env:TMP = $buildTemp
$env:MAPWRIGHT_INK_FIXTURE = $InkFixture
$env:PATH = "$dotnetRoot;$env:PATH"

Push-Location $repo
try {
    & $dotnet build --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet build "Tools\StorageCrashWorker\StorageCrashWorker.csproj" --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $godot --path $repo -- --spike-self-test
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
