$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$dotnetRoot = Join-Path $repo ".tools\dotnet-8.0.425"
$dotnet = Join-Path $dotnetRoot "dotnet.exe"
$godot = Join-Path $repo ".tools\godot-4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
$artifactDirectory = Join-Path $repo "artifacts\spike"
$resultPath = Join-Path $artifactDirectory "tdr-device-loss.json"
$launcherPath = Join-Path $artifactDirectory "tdr-launcher.json"
$stdoutPath = Join-Path $artifactDirectory "tdr-stdout.txt"
$stderrPath = Join-Path $artifactDirectory "tdr-stderr.txt"

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_HOST_PATH = $dotnet
$env:DOTNET_CLI_HOME = Join-Path $repo ".tools\dotnet-home"
$env:NUGET_PACKAGES = Join-Path $repo ".tools\nuget-packages"
$env:APPDATA = Join-Path $repo ".tools\appdata"
$env:LOCALAPPDATA = Join-Path $repo ".tools\localappdata"
$env:PATH = "$dotnetRoot;$env:PATH"

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
Remove-Item -LiteralPath $resultPath,$launcherPath,$stdoutPath,$stderrPath -Force -ErrorAction SilentlyContinue

Push-Location $repo
try {
    & $dotnet build --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $process = Start-Process -FilePath $godot `
        -ArgumentList @("--path", $repo, "--", "--tdr-device-loss") `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $finished = $process.WaitForExit(30000)
    if (!$finished) {
        $process.Kill($true)
        $process.WaitForExit()
    }

    $stage = if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw } else { $null }
    $launcher = [ordered]@{
        childExited = $finished
        childExitCode = if ($finished) { $process.ExitCode } else { $null }
        killedAfterTimeout = !$finished
        durableStage = if ($stage) { $stage | ConvertFrom-Json } else { $null }
    }
    $launcher | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $launcherPath -Encoding utf8
    $launcher | ConvertTo-Json -Depth 8
    if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath }
    if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath }
    exit 0
}
finally {
    Pop-Location
}
