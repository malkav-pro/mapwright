$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$dotnetRoot = Join-Path $repo ".tools\dotnet-8.0.425"
$dotnet = Join-Path $dotnetRoot "dotnet.exe"
$godot = Join-Path $repo ".tools\godot-4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_HOST_PATH = $dotnet
$env:DOTNET_CLI_HOME = Join-Path $repo ".tools\dotnet-home"
$env:NUGET_PACKAGES = Join-Path $repo ".tools\nuget-packages"
$env:APPDATA = Join-Path $repo ".tools\appdata"
$env:LOCALAPPDATA = Join-Path $repo ".tools\localappdata"
$env:PATH = "$dotnetRoot;$env:PATH"

Push-Location $repo
try {
    & $dotnet build --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $godot --path $repo -- --import-memory-probe
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
