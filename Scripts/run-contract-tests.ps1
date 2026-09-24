$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$commonGit = (& git -C $repo rev-parse --path-format=absolute --git-common-dir).Trim()
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $commonGit)) {
    throw "Could not resolve the source checkout from this worktree."
}
$sourceCheckout = Split-Path -Parent $commonGit
$toolsRoot = Join-Path $sourceCheckout ".tools"
$dotnetRoot = Join-Path $toolsRoot "dotnet-8.0.425"
$dotnet = Join-Path $dotnetRoot "dotnet.exe"
$runtime = Join-Path $repo "artifacts\runtime-temp"
$buildTemp = Join-Path $runtime "temp"
New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_CLI_HOME = Join-Path $runtime "dotnet-home"
$env:NUGET_PACKAGES = Join-Path $toolsRoot "nuget-packages"
$env:NUGET_HTTP_CACHE_PATH = Join-Path $runtime "nuget-http"
$env:APPDATA = Join-Path $runtime "appdata"
$env:LOCALAPPDATA = Join-Path $runtime "localappdata"
$env:TEMP = $buildTemp
$env:TMP = $buildTemp
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:PATH = "$dotnetRoot;$env:PATH"

Push-Location $repo
try {
    & $dotnet restore "tests\Mapwright.ContractTests\Mapwright.ContractTests.csproj" --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet run --project "tests\Mapwright.ContractTests\Mapwright.ContractTests.csproj" --no-restore
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
