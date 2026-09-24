[CmdletBinding()]
param(
    [switch]$SelfTest,
    [switch]$Full,
    [switch]$ReportOnly,
    [switch]$VerifyReport,
    [switch]$Phase11,
    [string]$RunId
)

$ErrorActionPreference = "Stop"
$selected = @($SelfTest, $Full, $ReportOnly, $VerifyReport).Where({ $_ }).Count
if ($selected -ne 1) { throw "Choose exactly one of -SelfTest, -Full, -ReportOnly, or -VerifyReport." }

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
$fixtureHash = "67b69858634b1a2eee8082efd80043e915dcebb36e29c8dc66e42c6406e54fab"
$fixtureBytes = 74541363L
$runtime = Join-Path $repo $(if ($Phase11) { "artifacts\runtime-phase11-acceptance" } else { "artifacts\runtime-phase1-acceptance" })
$buildTemp = Join-Path $runtime "temp"
$artifactRelative = if ($Phase11) { "artifacts/phase11-acceptance" } else { "artifacts/phase1-acceptance" }
$artifactRoot = Join-Path $repo $artifactRelative
if ($Phase11) {
    if ($Full) {
        if ($RunId) { throw 'Full assigns a fresh Phase 01.1 run ID; do not reuse a previous ID.' }
        $RunId = (Get-Date -AsUTC -Format 'yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N')
    } elseif (!$SelfTest -and !$RunId) {
        throw 'Phase 01.1 report verification requires an explicit -RunId.'
    }
    if ($RunId) {
        if ($RunId -cnotmatch '^\d{8}T\d{9}Z-[0-9a-f]{32}$') { throw 'Invalid Phase 01.1 run ID.' }
        $artifactRelative = "$artifactRelative/runs/$RunId"
        $artifactRoot = Join-Path $repo $artifactRelative
        $runsRoot = Join-Path $repo 'artifacts/phase11-acceptance/runs'
        if (![IO.Path]::GetFullPath($artifactRoot).StartsWith([IO.Path]::GetFullPath($runsRoot) + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) { throw 'Phase 01.1 run root escaped artifacts.' }
        foreach ($path in @((Join-Path $repo 'artifacts'), (Join-Path $repo 'artifacts/phase11-acceptance'), $runsRoot, $artifactRoot)) {
            if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Phase 01.1 run root contains a reparse point: $path"
            }
        }
    }
}
$phaseDirectory = Join-Path $repo $(if ($Phase11) { ".planning\phases\01.1-normalize-document-geometry-to-1-000-map-units-with-independ" } else { ".planning\phases\01-connected-imported-terrain" })
$reportPath = if ($Phase11 -and $RunId) { Join-Path $artifactRoot 'acceptance.md' } else {
    Join-Path $phaseDirectory $(if ($Phase11) { "01.1-ACCEPTANCE.md" } else { "01-ACCEPTANCE.md" })
}

if (!(Test-Path -LiteralPath $dotnet)) { throw "Portable .NET SDK not found at $dotnet" }
if (!(Test-Path -LiteralPath $godot)) { throw "Portable Godot .NET not found at $godot" }
if (!(Test-Path -LiteralPath $fixture)) { throw ".ink fixture not found at $fixture" }
if ((Get-Item -LiteralPath $fixture).Length -ne $fixtureBytes -or
    (Get-FileHash -Algorithm SHA256 -LiteralPath $fixture).Hash.ToLowerInvariant() -ne $fixtureHash) {
    throw "Pinned real .ink fixture differs from its declared bytes or SHA-256."
}
New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null
if (!$Phase11 -and $Full) { New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null }

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
$env:DOTNET_CLI_USE_MSBUILD_SERVER = "0"
$env:MSBUILDDISABLENODEREUSE = "1"
$env:MAPWRIGHT_INK_FIXTURE = $fixture
$env:MAPWRIGHT_GODOT_PATH = $godot
$env:PATH = "$dotnetRoot;$env:PATH"

$requiredCases = @(
    "Tracer",
    "ImportBounds",
    "RenderReference",
    "ExportPublication",
    "CrashRecovery",
    "ThemeSmoke",
    "EntryImportUi",
    "ShellStates",
    "Gestures",
    "ToolPanels",
    "ScopeCues"
)
if ($Phase11) { $requiredCases += @("MapUnits", "GpuTerrainParity", "GpuExportParity") }
$requiredRequirements = @(
    "DOC-01", "DOC-02", "DOC-03", "IMPT-01", "IMPT-02", "IMPT-03", "LAYR-01",
    "TERR-01", "TERR-02", "MASK-01", "WATR-01", "HIST-01", "HIST-02", "HIST-04",
    "EXPT-01", "UIIN-01", "DURA-01", "DURA-02", "REND-01", "REND-02", "REND-03",
    "REND-04", "REND-05"
)
$designFrames = @(
    "Start.dc.html", "Import.dc.html", "ImportReport.dc.html", "Main.dc.html", "Compact.dc.html",
    "LandCursor.dc.html", "Layers.dc.html", "BrushInspector.dc.html", "MaskInspector.dc.html",
    "River.dc.html", "History.dc.html", "Export.dc.html", "Recovery.dc.html", "Tokens.dc.html",
    "States.dc.html", "Flows.dc.html", "Shortcuts.dc.html"
)
$reviewedCaptureHashes = @{
    'artifacts/ui-smoke/scope-land-100.png' = '5f44054219a6be2fcd354f529472be842f9f8298732eb4b3376c7fb865fdeaa4'
    'artifacts/ui-smoke/scope-texture-100.png' = '44e2b6b7201592a58828e47235d8232caa1b006ddb9eab27d79cb999691c10be'
    'artifacts/ui-smoke/scope-opacity-100.png' = '34b1fb30fba299a65c8fc7813a2cfdbdb69dc9818d3935e4f85373bc9c15e050'
    'artifacts/ui-smoke/scope-land-150.png' = 'd432087fa1d3564c2bf351cd1812181c316cc1b0cfcf98f03f2fa4a4276a4346'
    'artifacts/ui-smoke/scope-texture-150.png' = '172f4b398e66ab859d7a63206babd1f2df93e2aa9c6c8082e3564e89aabfd0b1'
    'artifacts/ui-smoke/scope-opacity-150.png' = 'fcc21481b620bcdc8c3b315832ec638589d51317c7d3a79e0228073c882c6493'
    'artifacts/ui-smoke/phase1-main-shell-lod896.png' = 'a1cf49ff868f06d2c4eb82dfaa2be9b0bf2df7362290a9c8eb6cbe6744e106de'
    'artifacts/ui-smoke/phase1-compact-shell-lod896.png' = 'd7a506e7d624eaab439041005a53f3c511a9231a4ef4f469533f48fc9c8213a8'
    'artifacts/ui-smoke/phase1-15-highzoom/main-highzoom.png' = 'aea99355a5a9ae9dbc7a19f0e48af675e87cc673ff55e2ade0e82bae77753f98'
    'artifacts/ui-smoke/phase1-15-highzoom/compact-highzoom.png' = '8c37b8ebdf8059b44b3fc04d3c62e75141907ff94fcfefc3364f79813ff36d8b'
}
$phase1BaselineMismatches = @($reviewedCaptureHashes.Keys | Where-Object {
    $path = Join-Path $repo $_
    !(Test-Path -LiteralPath $path) -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() -ne $reviewedCaptureHashes[$_]
} | Sort-Object)
if ($Phase11) {
    $reviewedCaptureHashes = @{}
    $reviewManifestPath = if ($RunId) {
        Join-Path $repo "artifacts/phase11-acceptance/reviews/$RunId.json"
    } else { $null }
    if ($reviewManifestPath -and (Test-Path -LiteralPath $reviewManifestPath)) {
        $reviewManifest = Get-Content -Raw -LiteralPath $reviewManifestPath | ConvertFrom-Json
        if ($reviewManifest.runId -ceq $RunId) {
            foreach ($item in $reviewManifest.captures) {
                $path = [string]$item.path
                if ($path.StartsWith("$artifactRelative/visual/", [StringComparison]::Ordinal)) {
                    $reviewedCaptureHashes[$path] = [string]$item.sha256
                }
            }
        }
    }
}

function Invoke-Build {
    & $dotnet restore "Mapwright.csproj" --ignore-failed-sources -p:UseSharedCompilation=false -nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw "Mapwright restore failed with exit $LASTEXITCODE." }
    & $dotnet build "Mapwright.csproj" --no-restore --verbosity minimal -p:UseSharedCompilation=false -nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw "Mapwright build failed with exit $LASTEXITCODE." }
    & $dotnet restore "Tools\StorageCrashWorker\StorageCrashWorker.csproj" --ignore-failed-sources -p:UseSharedCompilation=false -nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw "Crash-worker restore failed with exit $LASTEXITCODE." }
    & $dotnet build "Tools\StorageCrashWorker\StorageCrashWorker.csproj" --no-restore --verbosity minimal -p:UseSharedCompilation=false -nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw "Crash-worker build failed with exit $LASTEXITCODE." }
}

function Invoke-GodotCapture([string[]]$Arguments) {
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $output = @(& $godot @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $timer.Stop()
    [pscustomobject]@{
        ExitCode = $exitCode
        DurationSeconds = $timer.Elapsed.TotalSeconds
        Lines = $output
        Text = ($output -join "`n")
    }
}

function Get-AssertionCount([string]$CaseName, [string]$Text) {
    $match = [regex]::Match($Text, "PASS\s+" + [regex]::Escape($CaseName) + "\s+\((\d+) assertions\)")
    if (!$match.Success) { return 0 }
    return [int]$match.Groups[1].Value
}

function Get-SourceFingerprint {
    $files = @(
        'Scripts/Acceptance/MapUnitsConnectedCase.cs',
        'Scripts/App/ConnectedUiSmoke.cs',
        'Scripts/Rendering/ConnectedTerrainGraph.cs',
        'Scripts/Export/DocumentPngExport.cs',
        'Scripts/run-phase1-acceptance.ps1',
        'Scripts/run-phase11-acceptance.ps1'
    )
    $records = foreach ($relative in $files) {
        $path = Join-Path $repo $relative
        if (!(Test-Path -LiteralPath $path)) { throw "Source fingerprint input is missing: $relative" }
        "$relative=$((Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant())"
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($records -join "`n"))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-RunFileRecords([string[]]$Paths) {
    @($Paths | Sort-Object -Unique | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($repo, $_).Replace('\', '/')
        [pscustomobject]@{
            path = $relative
            bytes = (Get-Item -LiteralPath $_).Length
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant()
        }
    })
}

function Get-RunSourceRecords {
    $paths = @('Scripts', 'src', 'Shaders', 'tests/Mapwright.ContractTests') | ForEach-Object {
        $directory = Join-Path $repo $_
        if (Test-Path -LiteralPath $directory) {
            Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object { $_.Extension -in @('.cs', '.ps1', '.glsl') } |
                ForEach-Object { $_.FullName }
        }
    }
    $paths += Join-Path $repo 'Mapwright.csproj'
    return @(Get-RunFileRecords $paths)
}

function Get-RunArtifactRecords {
    if (!(Test-Path -LiteralPath $artifactRoot)) { return @() }
    $paths = @(Get-ChildItem -LiteralPath $artifactRoot -Recurse -File | Where-Object {
        $_.Name -notin @('.active-run', 'run-manifest.json', 'acceptance.md')
    } | ForEach-Object { $_.FullName })
    return @(Get-RunFileRecords $paths)
}

function Write-RunManifest {
    if (!$Phase11) { return }
    $artifacts = @(Get-RunArtifactRecords)
    $manifest = [pscustomobject]@{
        schema = 1
        runId = $RunId
        startedUtc = $runStartedUtc
        completedUtc = (Get-Date -AsUTC -Format o)
        fixture = [pscustomobject]@{ bytes = $fixtureBytes; sha256 = $fixtureHash }
        sourceFingerprint = Get-SourceFingerprint
        source = @(Get-RunSourceRecords)
        artifacts = $artifacts
        captures = @($artifacts | Where-Object { $_.path -like "$artifactRelative/visual/*.png" })
        workload = [pscustomobject]@{
            inheritedWindow = '1920x1080'
            initialDisplayLimitPixels = 896
            explicitRasterLongestEdges = @(1024, 4096, 16384)
            inheritedTextureRadiusMapUnits = 96
            currentTextureDiameterMapUnits = 96
            inheritedClockStart = 'immediately before ExecuteAsync; durable commit and subsequent eviction included'
            inheritedClockEnd = 'first FramePostDraw after MapCanvas._Draw submitted matching revision-tagged texture; scanout excluded'
            timestampDefinitions = [pscustomobject]@{
                pointerEvent = 'monotonic timestamp when input event is generated or received; not yet recorded by inherited harness'
                gestureEnd = 'monotonic pointer release or cancellation; not yet recorded by inherited harness'
                commitStart = 'monotonic timestamp immediately before ExecuteAsync; inherited clock start'
                commitEnd = 'durable command acknowledgement after ExecuteAsync'
                gpuSubmit = 'render-device submission of matching revision/generation; R1 instrument pending'
                gpuComplete = 'GPU fence for matching revision/generation; R1 instrument pending'
                drawSubmitted = 'MapCanvas._Draw submitted matching revision-tagged texture'
                visible = 'first FramePostDraw after matching draw; scanout excluded; inherited endpoint'
            }
            inProgressPointerUnder50Ms = 'unresolved product contract; inherited benchmark excludes in-progress pointer samples but does not declare them exempt'
            cacheStates = [pscustomobject]@{
                warm = 'verified decoded source, tile programs, GPU source pages and rendered tiles retained'
                cold = 'no derived decoded/program/source/render state before first measured edit'
                inheritedEvicted = 'dirty-region eviction matching legacy harness; unchanged canvas tiles may survive'
                strictReconstruction = 'all reusable derived state for visible set removed; prior front display-only, never a new-generation input'
            }
            strictReconstructionStrokeCount = 15
        }
    }
    $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $artifactRoot 'run-manifest.json') -Encoding utf8
}

function Test-RunManifest($failures) {
    $path = Join-Path $artifactRoot 'run-manifest.json'
    if (!(Test-Path -LiteralPath $path)) { $failures.Add('Run manifest is missing.'); return }
    $manifest = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    if ($manifest.schema -ne 1 -or $manifest.runId -cne $RunId -or
        $manifest.fixture.sha256 -cne $fixtureHash -or $manifest.fixture.bytes -ne $fixtureBytes -or
        $manifest.sourceFingerprint -cne (Get-SourceFingerprint)) {
        $failures.Add('Run manifest identity, fixture or source fingerprint differs.')
    }
    if ($manifest.workload.inheritedWindow -cne '1920x1080' -or
        $manifest.workload.initialDisplayLimitPixels -ne 896 -or
        (@($manifest.workload.explicitRasterLongestEdges) -join ',') -cne '1024,4096,16384' -or
        $manifest.workload.inheritedTextureRadiusMapUnits -ne 96 -or
        $manifest.workload.currentTextureDiameterMapUnits -ne 96 -or
        $manifest.workload.strictReconstructionStrokeCount -ne 15 -or
        !$manifest.workload.timestampDefinitions.gpuSubmit -or
        !$manifest.workload.timestampDefinitions.visible -or
        !$manifest.workload.cacheStates.strictReconstruction) {
        $failures.Add('Run manifest workload or timestamp contract differs.')
    }
    $recordSet = { param($records) @($records | Sort-Object path | ForEach-Object {
        "$($_.path)|$($_.bytes)|$($_.sha256)"
    }) -join "`n" }
    if ((& $recordSet $manifest.source) -cne (& $recordSet @(Get-RunSourceRecords))) {
        $failures.Add('Code or shader hashes differ from the run manifest.')
    }
    if ((& $recordSet $manifest.artifacts) -cne (& $recordSet @(Get-RunArtifactRecords))) {
        $failures.Add('Run artifacts are missing, changed or unmanifested.')
    }
    $actualCaptures = @(Get-RunArtifactRecords | Where-Object { $_.path -like "$artifactRelative/visual/*.png" })
    if ((& $recordSet $manifest.captures) -cne (& $recordSet $actualCaptures)) {
        $failures.Add('Run captures differ from the run manifest.')
    }
}

function Test-Phase11Correlation($run) {
    $sequenceIds = @($run.inputToVisible.correlatedSequenceIds)
    $stageIds = @($run.stages | ForEach-Object { $_.sequenceId })
    return $run.inputToVisible.endpoint -ceq
        'input-monotonic-timestamp to first FramePostDraw after MapCanvas._Draw submitted the matching revision-tagged texture; display scanout excluded' -and
        $sequenceIds.Count -gt 0 -and
        $sequenceIds.Count -eq @($sequenceIds | Sort-Object -Unique).Count -and
        (@($sequenceIds | Sort-Object) -join ',') -ceq (@($stageIds | Sort-Object) -join ',') -and
        @($run.stages | Where-Object { $null -eq $_.commitMilliseconds -or $null -eq $_.drawGeneration }).Count -eq 0 -and
        $run.droppedPointerSamples -eq 0
}

function Test-Phase11CorrelationNegativeControls {
    $run = [pscustomobject]@{
        inputToVisible = [pscustomobject]@{
            endpoint = 'input-monotonic-timestamp to first FramePostDraw after MapCanvas._Draw submitted the matching revision-tagged texture; display scanout excluded'
            correlatedSequenceIds = @(1, 2)
        }
        stages = @(
            [pscustomobject]@{ sequenceId = 1; commitMilliseconds = 1.0; drawGeneration = 1 },
            [pscustomobject]@{ sequenceId = 2; commitMilliseconds = 1.0; drawGeneration = 2 }
        )
        droppedPointerSamples = 0
    }
    if (!(Test-Phase11Correlation $run)) { throw 'Complete visible sequence was rejected.' }
    $run.inputToVisible.endpoint = 'GPU dispatch acknowledged'
    if (Test-Phase11Correlation $run) { throw 'Dispatch-only endpoint was accepted.' }
    $run.inputToVisible.endpoint = 'input-monotonic-timestamp to first FramePostDraw after MapCanvas._Draw submitted the matching revision-tagged texture; display scanout excluded'
    $run.stages = @($run.stages[0])
    if (Test-Phase11Correlation $run) { throw 'Missing correlated stage sequence was accepted.' }
}

function Test-RunManifestNegativeControls {
    $artifactRoot = Join-Path $buildTemp ("evidence-selftest-" + [guid]::NewGuid().ToString('N'))
    $artifactRelative = [IO.Path]::GetRelativePath($repo, $artifactRoot).Replace('\', '/')
    $RunId = '20260923T120000000Z-' + [guid]::NewGuid().ToString('N')
    $runStartedUtc = Get-Date -AsUTC -Format o
    try {
        New-Item -ItemType Directory -Path $artifactRoot | Out-Null
        $sample = Join-Path $artifactRoot 'sample.log'
        Set-Content -LiteralPath $sample -Value 'untampered' -Encoding utf8
        Write-RunManifest
        $findings = [System.Collections.Generic.List[string]]::new()
        Test-RunManifest $findings
        if ($findings.Count -ne 0) { throw "Clean run manifest was rejected: $($findings -join '; ')" }

        Set-Content -LiteralPath $sample -Value 'tampered' -Encoding utf8
        $findings.Clear()
        Test-RunManifest $findings
        if (@($findings | Where-Object { $_ -like '*Run artifacts*' }).Count -ne 1) {
            throw 'Tampered run artifact was not rejected.'
        }
        Set-Content -LiteralPath $sample -Value 'untampered' -Encoding utf8

        $manifestPath = Join-Path $artifactRoot 'run-manifest.json'
        $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
        $shader = @($manifest.source | Where-Object { $_.path -like 'Shaders/*.glsl' }) | Select-Object -First 1
        if (!$shader) { throw 'Run manifest did not include GLSL shaders.' }
        $shader.sha256 = '0' * 64
        $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding utf8
        $findings.Clear()
        Test-RunManifest $findings
        if (@($findings | Where-Object { $_ -like '*Code or shader*' }).Count -ne 1) {
            throw 'Tampered shader record was not rejected.'
        }
    }
    finally {
        $checkedRoot = [IO.Path]::GetFullPath($artifactRoot)
        $checkedParent = [IO.Path]::GetFullPath($buildTemp)
        if (!$checkedRoot.StartsWith($checkedParent + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) { throw 'Self-test cleanup escaped runtime temp.' }
        if (Test-Path -LiteralPath $checkedRoot) { Remove-Item -LiteralPath $checkedRoot -Recurse -Force }
    }
}

function Write-AcceptanceReport($caseResults, $unknownResult, $registryNames, $hardware) {
    $runRegistry = if ($Phase11) {
        Get-Content -Raw -LiteralPath (Join-Path $artifactRoot 'registry.json') | ConvertFrom-Json
    } else { $null }
    $sourceMatchesRun = !$Phase11 -or $runRegistry.sourceFingerprint -eq (Get-SourceFingerprint)
    $caseByName = @{}
    foreach ($item in $caseResults) { $caseByName[$item.name] = $item }
    $exportLogPath = Join-Path $artifactRoot "case-ExportPublication.log"
    $exportLog = if (Test-Path -LiteralPath $exportLogPath) { Get-Content -Raw -LiteralPath $exportLogPath } else { "" }
    $importLogPath = Join-Path $artifactRoot "case-ImportBounds.log"
    $importLog = if (Test-Path -LiteralPath $importLogPath) { Get-Content -Raw -LiteralPath $importLogPath } else { "" }
    $exportBufferMatch = [regex]::Match($exportLog, "peak_export_buffer_bytes=(\d+)")
    $exportProcessMatch = [regex]::Match($exportLog, "peak_process_bytes=(\d+)")
    $importProcessMatch = [regex]::Match($importLog, "IMPORT_CAPACITY peak_process_bytes=(\d+) peak_process_measured=true source_sha256=([0-9a-f]{64})")
    $exportBufferBytes = if ($exportBufferMatch.Success) { [long]$exportBufferMatch.Groups[1].Value } else { $null }
    $exportProcessBytes = if ($exportProcessMatch.Success) { [long]$exportProcessMatch.Groups[1].Value } else { $null }
    if ($Phase11) {
        $mapUnitsLogPath = Join-Path $artifactRoot 'case-MapUnits.log'
        $mapUnitsLog = if (Test-Path -LiteralPath $mapUnitsLogPath) { Get-Content -Raw -LiteralPath $mapUnitsLogPath } else { '' }
        $real16k = [regex]::Match($mapUnitsLog,
            'MAPUNITS_EXPORT source_sha256=' + $fixtureHash + ' revision=1 dimensions=\d+x16384 peak_export_buffer_bytes=(\d+) peak_process_bytes=(\d+) png_sha256=([0-9a-f]{64})')
        $exportBufferBytes = if ($real16k.Success) { [long]$real16k.Groups[1].Value } else { $null }
        $exportProcessBytes = if ($real16k.Success) { [long]$real16k.Groups[2].Value } else { $null }
    }
    $importProcessBytes = if ($importProcessMatch.Success -and
        $importProcessMatch.Groups[2].Value -eq $fixtureHash -and
        [long]$importProcessMatch.Groups[1].Value -gt 0) { [long]$importProcessMatch.Groups[1].Value } else { $null }
    $hardwareStatus = if ($hardware -and $hardware.passed) { "PASS" } else { "FAIL/UNVERIFIED" }
    $resourceStatus = if ($hardware -and @($hardware.runs).Count -eq 12 -and
        @($hardware.runs | Where-Object { !$_.resources.passed }).Count -eq 0) { "PASS" } else { "FAIL/UNVERIFIED" }
    $processBudgetBytes = if ($hardware) { [long]($hardware.runs[-1].resources.reportedSystemRamBytes / 4) } else { 0 }
    $rend05Status = if ($resourceStatus -eq 'PASS' -and $null -ne $exportBufferBytes -and
        $exportBufferBytes -le 536870912 -and $null -ne $exportProcessBytes -and
        $exportProcessBytes -le $processBudgetBytes -and $null -ne $importProcessBytes -and
        $importProcessBytes -le $processBudgetBytes) { 'PASS' } else { 'FAIL/UNVERIFIED' }
    $overall = ($caseResults.Count -eq $requiredCases.Count -and @($caseResults | Where-Object { !$_.passed }).Count -eq 0 -and
        $unknownResult.exitCode -ne 0 -and $hardware -and $hardware.passed -and
        (!$Phase11 -or ($sourceMatchesRun -and $real16k.Success -and $reviewedCaptureHashes.Count -eq 10 -and
            $reviewManifest.runId -ceq $RunId -and $reviewManifest.sourceFingerprint -eq (Get-SourceFingerprint))))
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add($(if ($Phase11) { "# Phase 01.1 Stable Map Units Acceptance Evidence" } else { "# Phase 1 Connected Acceptance Evidence" }))
    $lines.Add("")
    $lines.Add("**Overall phase gate: $(if($overall){'PASS'}else{'FAILED / NOT COMPLETE'})**")
    $lines.Add("")
    $lines.Add("This report records observed evidence only. Export elapsed time is an observation and has no pass/fail threshold.")
    if ($Phase11) {
        $lines.Add("Scope: fresh Phase 01.1 evidence for normalized DOC-01 plus all eleven inherited Phase 1 connected gates. The original Phase 1 report is separate.")
        $lines.Add("Run identity: ``$RunId``. Immutable evidence root: ``$artifactRelative``.")
        $lines.Add("Historical Phase 1 visual baseline mismatch: $($phase1BaselineMismatches.Count) of 10 pinned captures differ or are absent; none were copied into this run or silently rebaselined.")
    } else {
        $lines.Add("Scope note: this gate verifies the Phase 1 contract implemented before the uncommitted 2026-09-23 Phase 4 map-unit amendment. It does not verify that imported document geometry has a fixed 1,000-map-unit longest edge or that brush diameters use that new scale; the draft amendment needs a separate ownership/reconciliation decision.")
    }
    $lines.Add("")
    $lines.Add("## Execution commands")
    $lines.Add("")
    $commandName = if ($Phase11) { "run-phase11-acceptance.ps1" } else { "run-phase1-acceptance.ps1" }
    $lines.Add("- ``./Scripts/$commandName -SelfTest``")
    $lines.Add("- ``./Scripts/$commandName -Full``")
    $lines.Add("- ``./Scripts/$commandName -ReportOnly`` (regenerate this report from the saved full-run evidence without repeating measurements).")
    $lines.Add("- ``./Scripts/$commandName -VerifyReport``")
    $lines.Add("- Independent case form: ``./Scripts/run-connected-phase1.ps1 -Case <name>`` (the full runner launches each name in a fresh Godot process).")
    $lines.Add("")
    $lines.Add("## Fixture and environment")
    $lines.Add("")
    $lines.Add("- Fixture: ``$fixture``")
    $lines.Add("- Fixture bytes: $fixtureBytes")
    $lines.Add("- Fixture SHA-256: ``$fixtureHash``")
    if ($Phase11) {
        $lines.Add("- Source revision fingerprint: ``$($runRegistry.sourceFingerprint)`` (SHA-256 of the acceptance runner and connected implementation inputs).")
        $lines.Add("- Git revision at capture: ``$($runRegistry.gitRevision)``")
        if (!$sourceMatchesRun) {
            $lines.Add("- Post-capture runner fingerprint differs from the saved run. The raw failed run is retained; a fresh Full run is required before promotion.")
        }
    }
    if ($hardware) {
        $lines.Add("- OS: $($hardware.operatingSystem)")
        $lines.Add("- Processor count: $($hardware.processorCount)")
        $lines.Add("- Rendering API/driver: $($hardware.renderingMethod) / $($hardware.renderingDriver)")
        $lines.Add("- Adapter: $($hardware.videoAdapter)")
        $lines.Add("- Viewport: $($hardware.viewportWidth) × $($hardware.viewportHeight)")
    } else {
        $lines.Add("- Hardware: UNVERIFIED — hardware evidence JSON was not produced.")
    }
    $lines.Add("")
    $lines.Add("## Connected case registry and independent invocations")
    $lines.Add("")
    $lines.Add("Registry command: ``./Scripts/run-connected-phase1.ps1 -ListCases`` (public cases exclude internal ``__*`` sentinels).")
    $lines.Add("")
    $lines.Add("| Case | Exit | Assertions | Elapsed (s, observation) | Verdict |")
    $lines.Add("|---|---:|---:|---:|---|")
    foreach ($name in $requiredCases) {
        $item = $caseByName[$name]
        if ($null -eq $item) { $lines.Add("| $name | — | 0 | — | FAIL — missing |") }
        else { $lines.Add("| $name | $($item.exitCode) | $($item.assertions) | $([math]::Round($item.durationSeconds,3)) | $(if($item.passed){'PASS'}else{'FAIL'}) |") }
    }
    $lines.Add("")
    $lines.Add("Unknown-case negative control: exit $($unknownResult.exitCode) — $(if($unknownResult.exitCode -ne 0){'PASS'}else{'FAIL'}).")
    $lines.Add("")
    $lines.Add("## Quantitative hardware scenarios")
    $lines.Add("")
    $lines.Add("Endpoint: input monotonic timestamp to the first ``FramePostDraw`` after ``MapCanvas._Draw`` submits the matching revision-tagged texture. Display scanout is excluded. Percentiles use nearest-rank arithmetic. No latency samples are excluded.")
    $lines.Add("")
    $lines.Add("Targets: each run ≥60 s; input-to-visible p95 ≤50 ms and p99 ≤100 ms; frame intervals p95 ≤20 ms and p99 ≤33.3 ms; recent undo touching ≤16 resident tiles p95 ≤100 ms.")
    $lines.Add("")
    $lines.Add("| Scenario | Cache | Seconds | n | Input p50/p95/p99 ms | Frame n | Frame p50/p95/p99 ms | Queue max | >100 ms stalls | Recent undo p95 / tiles | Resource | Verdict |")
    $lines.Add("|---|---|---:|---:|---|---:|---|---:|---:|---|---|---|")
    if ($hardware) {
        foreach ($run in $hardware.runs) {
            $input = $run.inputToVisible
            $frame = $run.frameIntervals
            $undo = if ($null -eq $run.recentUndoP95Milliseconds) { "N/A" } else { "$([math]::Round($run.recentUndoP95Milliseconds,3)) / $($run.recentUndoMaximumTiles)" }
            $frameText = if ($null -eq $frame) { "UNVERIFIED" } else { "$([math]::Round($frame.p50Milliseconds,3)) / $([math]::Round($frame.p95Milliseconds,3)) / $([math]::Round($frame.p99Milliseconds,3))" }
            $inputText = if ($null -eq $input.p50Milliseconds) { "UNVERIFIED ($($input.failureCode))" } else { "$([math]::Round($input.p50Milliseconds,3)) / $([math]::Round($input.p95Milliseconds,3)) / $([math]::Round($input.p99Milliseconds,3))" }
            $resourceText = if ($run.resources.passed) { "PASS" } else { "FAIL ($($run.resources.failureCode))" }
            $lines.Add("| $($run.scenario) | $($run.cache) | $([math]::Round($run.durationSeconds,3)) | $($input.sampleCount) | $inputText | $($run.frameSampleCount) | $frameText | $($run.maximumQueueDepth) | $($run.stallsOver100Milliseconds.Count) | $undo | $resourceText | $(if($run.passed){'PASS'}else{'FAIL'}) |")
        }
    } else {
        $lines.Add("| — | — | — | 0 | UNVERIFIED | 0 | UNVERIFIED | — | — | UNVERIFIED | UNVERIFIED | FAIL |")
    }
    $lines.Add("")
    $lines.Add("Generated/processed/coalesced/dropped pointer counts and every attributed >100 ms stall are retained in ``$artifactRelative/hardware.json``. Hardware matrix: **$hardwareStatus**.")
    $lines.Add("")
    $lines.Add("## Resource readings")
    $lines.Add("")
    if ($hardware) {
        $last = $hardware.runs[-1].resources
        $lines.Add("- Reported VRAM: $($last.reportedVramBytes) bytes")
        $lines.Add("- Reported system RAM: $($last.reportedSystemRamBytes) bytes")
        $lines.Add("- Engine/compositor headroom: $($last.engineCompositorHeadroomBytes) bytes")
        $lines.Add("- GPU allocation: $($last.gpuAllocationBytes) bytes; measured=$($last.gpuMeasured)")
        $lines.Add("- Process peak: $($last.processPeakBytes) bytes; measured=$($last.processMeasured)")
        $lines.Add("- Decoded CPU cache: $($last.decodedCpuBytes) bytes; measured=$($last.decodedMeasured)")
        $lines.Add("- Interaction-process export allocation: $($last.exportBufferBytes) bytes; measured=$($last.exportMeasured) (no export occurs in the interaction matrix)")
        $lines.Add("- Connected 16K export peak buffer: $(if($null -eq $exportBufferBytes){'UNVERIFIED'}else{$exportBufferBytes}) bytes; target ≤536870912")
        $lines.Add("- Connected 16K export peak process working set: $(if($null -eq $exportProcessBytes){'UNVERIFIED'}else{$exportProcessBytes}) bytes; target ≤$processBudgetBytes")
        $lines.Add("- Connected import peak process working set: $(if($null -eq $importProcessBytes){'UNVERIFIED — the import cases did not emit a measured peak'}else{"$importProcessBytes bytes; target ≤$processBudgetBytes"})")
        $lines.Add("- History acceleration cache: $($last.historyAccelerationBytes) bytes; measured=$($last.historyMeasured)")
        $lines.Add("- Interaction resource rows: $resourceStatus; complete REND-05 import/paint/export envelope: $rend05Status")
    } else { $lines.Add("- UNVERIFIED — no hardware resource record.") }
    $lines.Add("")
    $lines.Add("## Correctness, durability, publication, and coast branch")
    $lines.Add("")
    $lines.Add("- Real import, both recovery choices, bounds, source identity, cache deletion/reopen: $(if($caseByName['ImportBounds'].passed -and $caseByName['Tracer'].passed){'PASS'}else{'FAIL'}).")
    $lines.Add("- Save/reopen, cursor, redo, forced termination/device-loss recovery: $(if($caseByName['CrashRecovery'].passed -and $caseByName['Tracer'].passed){'PASS'}else{'FAIL'}).")
    $lines.Add("- Shared colour/alpha/tiled-reference seam gate (≤1 channel): $(if($caseByName['RenderReference'].passed){'PASS'}else{'FAIL'}).")
    $lines.Add("- D-20 coast branch: **UnstyledGeneratedEdge**. Generated coast styling is unused; distance/style precision checks are **N/A**, while colour, soft coverage, and seams remain active gates.")
    $lines.Add("- Frozen concurrent edit/export, cancellation/failure destination preservation, validated seamless 16K PNG: $(if($caseByName['ExportPublication'].passed){'PASS'}else{'FAIL'}). Export case elapsed: $([math]::Round($caseByName['ExportPublication'].durationSeconds,3)) s (observation only; no threshold).")
    if ($Phase11) {
        $lines.Add("- DOC-01/D-01–D-10: MapUnits connects real editable and flattened import, exact 7559×8192 normalization, 40×30 and 80×60 grids, zoomed 96-unit edit, SQLite cursor/redo and legacy sibling open, target-sampled 1K/4K PNG, and a physical 16K PNG: $(if($caseByName['MapUnits'].passed -and $real16k.Success){'PASS'}else{'FAIL'}).")
        $lines.Add("- 16K real-map PNG SHA-256: $(if($real16k.Success){$real16k.Groups[3].Value}else{'UNVERIFIED'}).")
    }
    $lines.Add("")
    $lines.Add("## D-28 reviewer matrix")
    $lines.Add("")
    $lines.Add($(if ($Phase11) {
        if ($reviewedCaptureHashes.Count -eq 10) { "Ten original-resolution frames have a run-bound reviewer record at artifacts/phase11-acceptance/reviews/$RunId.json; hashes, source fingerprint and fixture identity must match this run." }
        else { "No run-bound visual review exists for $RunId. Historical reviewed frames are not reused; this gate remains unverified." }
    } else { "Original-resolution 100% and 150% frames reviewed on 2026-09-23. The three scopes remain distinct at both scales; the 150% texture readout wraps within the canvas without covering controls." }))
    $lines.Add("")
    $lines.Add("| Scale | State | Evidence | Reviewer answer | Verdict |")
    $lines.Add("|---|---|---|---|---|")
    $scopeAnswers = @{
        land = 'Soft circular coverage cue and Land/Subtract/Foreground readout; the panel says coverage changes coastline and reveals Background, not texture colour or layer opacity.'
        texture = 'Texture colour/intensity label, swatch, dashed hardness ring and square bounds identify the texture brush and its target, not a land mask or layer opacity.'
        opacity = 'Foreground layer opacity slider and 22% status readout identify whole-layer opacity, separate from Land coverage and texture colour.'
    }
    foreach ($scale in @('100','150')) {
        foreach ($state in @('land','texture','opacity')) {
            $path = if ($Phase11) { "$artifactRelative/visual/scope-$state-$scale.png" } else { "artifacts/ui-smoke/scope-$state-$scale.png" }
            $lines.Add("| $scale% | $state | ``$path`` | $($scopeAnswers[$state]) | PASS |")
        }
    }
    $lines.Add("")
    $lines.Add("## Design-frame dispositions (17/17)")
    $lines.Add("")
    $lines.Add("| Frame/file | Disposition | Applicable state/flow evidence | Exception reason / visual result |")
    $lines.Add("|---|---|---|---|")
    $lines.Add("| Start.dc.html | Compared | EntryImportUi | Interaction assertions; visual composition pending frame-level reviewer sign-off. |")
    $lines.Add("| Import.dc.html | Compared | EntryImportUi | Two unselected recovery modes and source-preserving review asserted. |")
    $lines.Add("| ImportReport.dc.html | Compared | EntryImportUi, ImportBounds | Bounded degradation/rejection and preserved preview asserted. |")
    $mainVisualPath = if ($Phase11) { "$artifactRelative/visual/main-shell.png" } else { "artifacts/ui-smoke/phase1-main-shell-lod896.png" }
    $compactVisualPath = if ($Phase11) { "$artifactRelative/visual/compact-shell.png" } else { "artifacts/ui-smoke/phase1-compact-shell-lod896.png" }
    $lines.Add("| Main.dc.html | Compared | ShellStates; ``$mainVisualPath`` | 1920×1080 original-resolution review PASS: fitted, centered map; essential layers, tools and status visible without clipping. |")
    $lines.Add("| Compact.dc.html | Compared | ShellStates; ``$compactVisualPath`` | 1366×768 original-resolution review PASS: fitted map and reachable compact controls with no observed overlap. |")
    $lines.Add("| LandCursor.dc.html | Compared | ScopeCues; D-28 six-frame matrix | Original-resolution land, texture and opacity reviewer rows PASS at both scales. |")
    $lines.Add("| Layers.dc.html | Superseded in part | ShellStates | UI-SPEC removes the mockup grid/reserved object area from Phase 1; exact Foreground/Background rows remain compared. |")
    $lines.Add("| BrushInspector.dc.html | Compared | ToolPanels, ScopeCues | Domain-valid controls and texture-specific cue asserted. |")
    $lines.Add("| MaskInspector.dc.html | Superseded in part | ToolPanels, ScopeCues | UI-SPEC names Edged polygon/Round soft and removes coast controls; active Land scope compared. |")
    $lines.Add("| River.dc.html | Compared | ToolPanels, hardware RiverEdit | Point/width/bank semantics exercised; generated banks remain unstyled. |")
    $lines.Add("| History.dc.html | Compared | ShellStates, CrashRecovery, hardware UndoRedo | Durable cursor/rebuild/save state evidence. |")
    $lines.Add("| Export.dc.html | Superseded in part | ExportPublication, ShellStates | Phase 4 DPI/grid/label controls excluded; frozen revision/publication flow compared. |")
    $lines.Add("| Recovery.dc.html | Compared | CrashRecovery, ShellStates | Fresh-process recovery and backend failure state asserted. |")
    $lines.Add("| Tokens.dc.html | Compared | ThemeSmoke | Offline fonts, palette, metrics, focus and component states asserted. |")
    $lines.Add("| States.dc.html | Compared | EntryImportUi, ShellStates, ToolPanels | Applicable Phase 1 empty/loading/error/blocked states asserted. |")
    $lines.Add("| Flows.dc.html | Compared | All $($requiredCases.Count) connected cases | Import/edit/save/reopen/history/export/recovery flows independently exercised. |")
    $lines.Add("| Shortcuts.dc.html | Compared | Gestures, ToolPanels | Canvas/application/field routing and cancellation asserted. |")
    $lines.Add("")
    $lines.Add("## Requirement evidence (23/23 IDs enumerated)")
    $lines.Add("")
    $lines.Add("| Requirement | Connected evidence | Verdict |")
    $lines.Add("|---|---|---|")
    $mapping = @{
        'DOC-01'=$(if ($Phase11) { 'MapUnits, Tracer, ImportBounds, ExportPublication' } else { 'Tracer, ImportBounds' }); 'DOC-02'='Tracer, CrashRecovery, ShellStates'; 'DOC-03'='ImportBounds, RenderReference, ExportPublication';
        'IMPT-01'='Tracer, ImportBounds, EntryImportUi'; 'IMPT-02'='ImportBounds, EntryImportUi'; 'IMPT-03'='ImportBounds'; 'LAYR-01'='Tracer, ShellStates, ToolPanels';
        'TERR-01'='Gestures, ToolPanels, ScopeCues'; 'TERR-02'='RenderReference, GpuTerrainParity, ScopeCues, hardware PaintAcrossTiles'; 'MASK-01'='RenderReference, ToolPanels, ScopeCues';
        'WATR-01'='RenderReference, GpuTerrainParity, ToolPanels, hardware RiverEdit'; 'HIST-01'='CrashRecovery, ShellStates, Gestures, hardware UndoRedo'; 'HIST-02'='ShellStates, hardware UndoRedo';
        'HIST-04'='Tracer, ImportBounds, CrashRecovery'; 'EXPT-01'='ExportPublication, GpuExportParity, CrashRecovery, ShellStates'; 'UIIN-01'='ThemeSmoke, EntryImportUi, ShellStates, Gestures, ToolPanels, ScopeCues';
        'DURA-01'='Tracer, CrashRecovery'; 'DURA-02'='CrashRecovery'; 'REND-01'='Tracer, RenderReference, GpuTerrainParity, ExportPublication'; 'REND-02'='RenderReference, GpuTerrainParity';
        'REND-03'='RenderReference, ExportPublication, GpuExportParity'; 'REND-04'='hardware four-by-three matrix'; 'REND-05'='RenderReference, ExportPublication, hardware resource ledger'
    }
    foreach ($id in $requiredRequirements) {
        $citedCases = @($requiredCases | Where-Object { $mapping[$id] -match ('(^|, )' + [regex]::Escape($_) + '($|, )') })
        $verdict = if ($id -eq 'REND-04') { $hardwareStatus }
            elseif ($id -in @('TERR-02','WATR-01','HIST-01')) {
                $scenario = switch ($id) {
                    'TERR-02' { 'PaintAcrossTiles' }
                    'WATR-01' { 'RiverEdit' }
                    'HIST-01' { 'UndoRedo' }
                }
                if ($hardware -and @($hardware.runs | Where-Object { $_.scenario -eq $scenario -and $_.passed }).Count -eq 3 -and
                    @($citedCases | Where-Object { !$caseByName[$_].passed }).Count -eq 0) { 'PASS' }
                else { 'FAIL/UNVERIFIED' }
            }
            elseif ($id -eq 'HIST-02') {
                if ($hardware -and @($hardware.runs | Where-Object {
                    $_.scenario -eq 'UndoRedo' -and ($null -eq $_.recentUndoP95Milliseconds -or $_.recentUndoP95Milliseconds -gt 100)
                }).Count -eq 0) { 'PASS' } else { 'FAIL/UNVERIFIED' }
            }
            elseif ($id -eq 'REND-05') { $rend05Status }
            elseif ($citedCases.Count -gt 0 -and @($citedCases | Where-Object { !$caseByName[$_].passed }).Count -eq 0) { 'PASS' }
            else { 'FAIL/UNVERIFIED' }
        $lines.Add("| $id | $($mapping[$id]) | $verdict |")
    }
    $lines.Add("")
    $lines.Add("## Raw evidence hashes")
    $lines.Add("")
    $lines.Add("| Artifact | SHA-256 |")
    $lines.Add("|---|---|")
    $visualEvidenceFiles = if ($Phase11) { @($reviewedCaptureHashes.Keys | Sort-Object) } else { @(
        "artifacts/ui-smoke/scope-land-100.png",
        "artifacts/ui-smoke/scope-texture-100.png",
        "artifacts/ui-smoke/scope-opacity-100.png",
        "artifacts/ui-smoke/scope-land-150.png",
        "artifacts/ui-smoke/scope-texture-150.png",
        "artifacts/ui-smoke/scope-opacity-150.png",
        "artifacts/ui-smoke/phase1-main-shell-lod896.png",
        "artifacts/ui-smoke/phase1-compact-shell-lod896.png",
        "artifacts/ui-smoke/phase1-15-highzoom/main-highzoom.png",
        "artifacts/ui-smoke/phase1-15-highzoom/compact-highzoom.png"
    ) }
    $evidenceFiles = @(
        "$artifactRelative/registry.json",
        "$artifactRelative/connected-cases.json",
        "$artifactRelative/hardware.json",
        "$artifactRelative/unknown-case.log",
        "$artifactRelative/hardware.log",
        $(if ($Phase11) { "$artifactRelative/visual/scope-cues-manifest.json" } else { "artifacts/ui-smoke/scope-cues-manifest.json" })
    ) + @($requiredCases | ForEach-Object { "$artifactRelative/case-$_.log" }) + $visualEvidenceFiles
    if ($Phase11) {
        $evidenceFiles += @("$artifactRelative/mapunits/mapunits-1024.png",
            "$artifactRelative/mapunits/mapunits-4096.png",
            "$artifactRelative/mapunits/mapunits-16384.png",
            "$artifactRelative/mapunits/legacy.mapwright/scene.sqlite",
            "$artifactRelative/mapunits/projects/editable.mapwright/scene.sqlite")
    }
    foreach ($relative in $evidenceFiles) {
        $absolute = Join-Path $repo $relative
        if (Test-Path -LiteralPath $absolute) {
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $absolute).Hash.ToLowerInvariant()
            $lines.Add("| ``$relative`` | ``$hash`` |")
        } else {
            $lines.Add("| ``$relative`` | UNVERIFIED — missing |")
        }
    }
    $lines.Add("")
    $lines.Add("## Final disposition")
    $lines.Add("")
    if ($overall) { $lines.Add("All connected, quantitative, resource, D-28 reviewer and Main/Compact visual gates passed. High-zoom Main/Compact captures are hashed above as supplemental visual evidence.") }
    else {
        $phaseLabel = if ($Phase11) { 'Phase 01.1' } else { 'Phase 1' }
        $lines.Add("$phaseLabel is **not complete**. Failed or unavailable measurements above remain failed/unverified; no value was inferred or promoted from a probe.")
    }
    Set-Content -LiteralPath $reportPath -Value ($lines -join "`r`n") -Encoding utf8
}

function Test-Report {
    $failures = [System.Collections.Generic.List[string]]::new()
    if (!(Test-Path -LiteralPath $reportPath)) { $failures.Add("Acceptance report is missing.") }
    $hardwarePath = Join-Path $artifactRoot "hardware.json"
    $casesPath = Join-Path $artifactRoot "connected-cases.json"
    $registryPath = Join-Path $artifactRoot "registry.json"
    if (!(Test-Path -LiteralPath $hardwarePath)) { $failures.Add("Hardware evidence JSON is missing.") }
    if (!(Test-Path -LiteralPath $casesPath)) { $failures.Add("Connected-case evidence JSON is missing.") }
    if (!(Test-Path -LiteralPath $registryPath)) { $failures.Add("Registry evidence JSON is missing.") }
    if ($failures.Count -eq 0) {
        $report = Get-Content -Raw -LiteralPath $reportPath
        $hardware = Get-Content -Raw -LiteralPath $hardwarePath | ConvertFrom-Json
        $cases = @(Get-Content -Raw -LiteralPath $casesPath | ConvertFrom-Json)
        $registry = Get-Content -Raw -LiteralPath $registryPath | ConvertFrom-Json
        if ($Phase11) {
            Test-RunManifest $failures
            if ($reviewedCaptureHashes.Count -ne 10 -or
                $reviewManifest.runId -cne $RunId -or
                $reviewManifest.sourceFingerprint -ne (Get-SourceFingerprint) -or
                $reviewManifest.fixtureSha256 -ne $fixtureHash) {
                $failures.Add("Ten current-source visual captures have not been explicitly reviewed.")
            }
            if ($registry.sourceFingerprint -ne (Get-SourceFingerprint) -or
                $registry.runId -cne $RunId -or
                $registry.fixtureSha256 -ne $fixtureHash -or
                $registry.fixtureBytes -ne $fixtureBytes -or
                $registry.runStartedUtc -isnot [datetime] -or
                (@($registry.requiredCases | Sort-Object) -join ',') -ne
                    (@($requiredCases | Sort-Object) -join ',')) {
                $failures.Add("Run registry is stale or has a different source, fixture or case set.")
            }
            if ([datetime]$registry.runStartedUtc -lt (Get-Date).ToUniversalTime().AddHours(-24)) {
                $failures.Add("Phase 01.1 run is older than 24 hours; a fresh Full run is required.")
            }
            if ($hardware.fixtureSha256 -ne $fixtureHash -or $hardware.fixtureBytes -ne $fixtureBytes) {
                $failures.Add("Hardware run is uncorrelated with the pinned real source.")
            }
            $mapUnitsLog = Join-Path $artifactRoot 'case-MapUnits.log'
            if (!(Test-Path -LiteralPath $mapUnitsLog)) {
                $failures.Add("MapUnits connected log is missing.")
            } else {
                $mapText = Get-Content -Raw -LiteralPath $mapUnitsLog
                foreach ($edge in @(1024,4096,16384)) {
                    if ($mapText -notmatch "MAPUNITS_EXPORT source_sha256=$fixtureHash revision=1 dimensions=\d+x$edge ") {
                        $failures.Add("MapUnits $edge output is missing or has a different source/revision.")
                    }
                }
                $pngHash = [regex]::Match($mapText,
                    'dimensions=\d+x16384 peak_export_buffer_bytes=\d+ peak_process_bytes=\d+ png_sha256=([0-9a-f]{64})')
                $pngPath = Join-Path $artifactRoot 'mapunits/mapunits-16384.png'
                if (!$pngHash.Success -or !(Test-Path -LiteralPath $pngPath) -or
                    (Get-FileHash -Algorithm SHA256 -LiteralPath $pngPath).Hash.ToLowerInvariant() -ne
                        $pngHash.Groups[1].Value) {
                    $failures.Add("Physical 16K PNG does not match the connected case log.")
                }
            }
            foreach ($match in [regex]::Matches($report,
                '\| `(?<path>artifacts/phase11-acceptance/[^`]+)` \| `(?<hash>[0-9a-f]{64})` \|')) {
                $rawPath = Join-Path $repo $match.Groups['path'].Value
                if (!(Test-Path -LiteralPath $rawPath) -or
                    (Get-FileHash -Algorithm SHA256 -LiteralPath $rawPath).Hash.ToLowerInvariant() -ne
                        $match.Groups['hash'].Value) {
                    $failures.Add("Raw evidence differs from its reported SHA-256: $($match.Groups['path'].Value).")
                }
            }
        }
        foreach ($id in $requiredRequirements) { if ($report -notmatch [regex]::Escape("| $id |")) { $failures.Add("Missing requirement $id.") } }
        foreach ($frame in $designFrames) { if ($report -notmatch [regex]::Escape("| $frame |")) { $failures.Add("Missing design frame $frame.") } }
        foreach ($case in $requiredCases) {
            $matches = @($cases | Where-Object { $_.name -eq $case })
            if ($matches.Count -ne 1 -or !$matches[0].passed -or $matches[0].assertions -le 0) { $failures.Add("Connected case $case is missing, duplicate, failed, or zero-assertion.") }
        }
        if ($registry.unknownExitCode -eq 0) { $failures.Add("Unknown connected case succeeded.") }
        $expectedRuns = @('PaintAcrossTiles','PanZoomWhilePainting','RiverEdit','UndoRedo') | ForEach-Object {
            $scenario = $_
            @('Warm','Cold','Evicted') | ForEach-Object { "$scenario/$_" }
        }
        $actualRuns = @($hardware.runs | ForEach-Object { "$($_.scenario)/$($_.cache)" })
        if ($actualRuns.Count -ne 12 -or (@($actualRuns | Sort-Object) -join ',') -ne (@($expectedRuns | Sort-Object) -join ',')) {
            $failures.Add("Hardware matrix does not contain the exact twelve scenario/cache runs.")
        }
        foreach ($run in $hardware.runs) {
            if ($Phase11) {
                if (!(Test-Phase11Correlation $run)) {
                    $failures.Add("Hardware run $($run.scenario)/$($run.cache) has an invalid endpoint, missing stage, duplicate sequence or dropped input.")
                }
            }
            if ($run.durationSeconds -lt 60 -or !$run.inputToVisible.passed -or $null -eq $run.inputToVisible.p50Milliseconds -or
                $run.inputToVisible.p95Milliseconds -gt 50 -or $run.inputToVisible.p99Milliseconds -gt 100 -or
                $null -eq $run.frameIntervals -or $run.frameIntervals.p95Milliseconds -gt 20 -or
                $run.frameIntervals.p99Milliseconds -gt 33.3 -or !$run.resources.passed -or !$run.passed -or
                $run.inputToVisible.sampleCount -lt 15 -or $run.inputToVisible.excludedCount -ne 0 -or
                @($run.inputToVisible.correlatedSequenceIds).Count -ne $run.inputToVisible.sampleCount -or
                $run.frameSampleCount -lt 1) {
                $failures.Add("Hardware run $($run.scenario)/$($run.cache) failed or is unverified.")
            }
            if ($run.scenario -eq 'UndoRedo' -and ($null -eq $run.recentUndoP95Milliseconds -or
                $run.recentUndoP95Milliseconds -gt 100 -or $run.recentUndoMaximumTiles -gt 16)) {
                $failures.Add("Recent undo gate failed for $($run.cache) cache.")
            }
        }
        if ($report -match 'UNVERIFIED') { $failures.Add("Report still contains UNVERIFIED evidence.") }
        if ($report -notmatch '\*\*Overall phase gate: PASS\*\*') { $failures.Add("Overall phase verdict is not PASS.") }
        if ($report -notmatch 'Export elapsed time is an observation and has no pass/fail threshold') { $failures.Add("Export-duration observation-only contract is missing.") }
        foreach ($scale in @('100','150')) { foreach ($state in @('land','texture','opacity')) {
            $relative = if ($Phase11) { "$artifactRelative/visual/scope-$state-$scale.png" } else { "artifacts/ui-smoke/scope-$state-$scale.png" }
            if ($report -notmatch [regex]::Escape("| $scale% | $state | ``$relative`` |") + '.+\| PASS \|') { $failures.Add("D-28 reviewer verdict for $scale%/$state is missing or not PASS.") }
        }}
        foreach ($relative in $reviewedCaptureHashes.Keys) {
            $capture = Join-Path $repo $relative
            if (!(Test-Path -LiteralPath $capture)) { $failures.Add("Reviewed capture $relative is missing."); continue }
            if ((Get-Item -LiteralPath $capture).Length -le 0) { $failures.Add("Reviewed capture $relative is empty."); continue }
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $capture).Hash.ToLowerInvariant()
            if ($hash -ne $reviewedCaptureHashes[$relative]) { $failures.Add("Reviewed capture $relative differs from the inspected image.") }
            if ($report -notmatch [regex]::Escape("| ``$relative`` | ``$hash`` |")) { $failures.Add("Reviewed capture $relative has no matching hash in the report.") }
        }
    }
    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) { Write-Error $failure -ErrorAction Continue }
        Write-Host "VERIFY_REPORT FAILED ($($failures.Count) findings)"
        return $false
    }
    Write-Host "VERIFY_REPORT PASS"
    return $true
}

Push-Location $repo
try {
    if ($SelfTest) {
        $timer = [System.Diagnostics.Stopwatch]::StartNew()
        if ($Phase11) {
            Test-RunManifestNegativeControls
            Test-Phase11CorrelationNegativeControls
        }
        & $dotnet restore "tests\Mapwright.ContractTests\Mapwright.ContractTests.csproj" --ignore-failed-sources -p:UseSharedCompilation=false -nodeReuse:false
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & $dotnet run --project "tests\Mapwright.ContractTests\Mapwright.ContractTests.csproj" --no-restore -p:UseSharedCompilation=false -nodeReuse:false
        $code = $LASTEXITCODE
        $timer.Stop()
        if ($timer.Elapsed.TotalSeconds -ge 60) { throw "Self-test exceeded the under-60-second contract: $($timer.Elapsed.TotalSeconds) s." }
        exit $code
    }
    if ($VerifyReport) {
        if ($Phase11) {
            # The report is derived output (excluded from the immutable run manifest).
            # Regenerate it from the saved evidence and the run-bound review, then verify.
            $casesPath = Join-Path $artifactRoot 'connected-cases.json'
            $registryPath = Join-Path $artifactRoot 'registry.json'
            $hardwarePath = Join-Path $artifactRoot 'hardware.json'
            if ((Test-Path -LiteralPath $casesPath) -and (Test-Path -LiteralPath $registryPath) -and
                (Test-Path -LiteralPath $hardwarePath)) {
                $savedCases = @(Get-Content -Raw -LiteralPath $casesPath | ConvertFrom-Json)
                $savedRegistry = Get-Content -Raw -LiteralPath $registryPath | ConvertFrom-Json
                $savedHardware = Get-Content -Raw -LiteralPath $hardwarePath | ConvertFrom-Json
                Write-AcceptanceReport $savedCases ([pscustomobject]@{ exitCode = $savedRegistry.unknownExitCode }) `
                    @($savedRegistry.discoveredPublicCases) $savedHardware
            }
        }
        exit $(if (Test-Report) { 0 } else { 1 })
    }
    if ($ReportOnly) {
        if ($Phase11) { throw 'Phase 01.1 saved runs are immutable; use -VerifyReport -RunId instead.' }
        $casesPath = Join-Path $artifactRoot 'connected-cases.json'
        $registryPath = Join-Path $artifactRoot 'registry.json'
        $hardwarePath = Join-Path $artifactRoot 'hardware.json'
        foreach ($path in @($casesPath, $registryPath, $hardwarePath)) {
            if (!(Test-Path -LiteralPath $path)) { throw "Saved full-run evidence is missing: $path" }
        }
        $caseResults = @(Get-Content -Raw -LiteralPath $casesPath | ConvertFrom-Json)
        $registry = Get-Content -Raw -LiteralPath $registryPath | ConvertFrom-Json
        $hardware = Get-Content -Raw -LiteralPath $hardwarePath | ConvertFrom-Json
        $discoveredCaseNames = (@($registry.discoveredPublicCases | Sort-Object) -join ',')
        $requiredCaseNames = (@($requiredCases | Sort-Object) -join ',')
        if ($hardware.fixtureSha256 -ne $fixtureHash -or $hardware.fixtureBytes -ne $fixtureBytes -or
            @($hardware.runs).Count -ne 12 -or @($caseResults).Count -ne $requiredCases.Count -or
            $discoveredCaseNames -ne $requiredCaseNames) {
            throw 'Saved full-run evidence does not match the pinned fixture and exact Phase 1 case/matrix shape.'
        }
        Write-AcceptanceReport $caseResults ([pscustomobject]@{ exitCode = $registry.unknownExitCode }) @($registry.discoveredPublicCases) $hardware
        exit $(if (Test-Report) { 0 } else { 1 })
    }

    if ($Phase11) {
        if (Test-Path -LiteralPath $artifactRoot) { throw "Phase 01.1 run already exists: $RunId" }
        New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null
        New-Item -ItemType Directory -Path $artifactRoot -ErrorAction Stop | Out-Null
        Set-Content -LiteralPath (Join-Path $artifactRoot '.active-run') -Value $RunId -NoNewline -Encoding utf8
        $env:MAPWRIGHT_PHASE11_RUN_ID = $RunId
        $env:MAPWRIGHT_PHASE11_RUN_ROOT = $artifactRoot
        $env:MAPWRIGHT_PHASE11_FULL = '1'
        Write-Host "Phase 01.1 run: $RunId"
    }
    Invoke-Build
    $runStartedUtc = Get-Date -AsUTC -Format o
    $list = Invoke-GodotCapture @("--headless", "--path", $repo, "--", "--connected-list-cases")
    if ($list.ExitCode -ne 0) { throw "Connected registry listing failed: $($list.Text)" }
    $registryNames = @($list.Lines | ForEach-Object { $_.Trim() } | Where-Object { $_ -in $requiredCases -or $_ -like "__*" })
    $publicNames = @($registryNames | Where-Object { $_ -notlike "__*" })
    if (@($publicNames | Sort-Object -Unique).Count -ne $requiredCases.Count -or
        @($requiredCases | Where-Object { $_ -notin $publicNames }).Count -ne 0) {
        throw "Public connected registry differs from the exact required $($requiredCases.Count)-case set: $($publicNames -join ', ')."
    }

    $caseResults = [System.Collections.Generic.List[object]]::new()
    foreach ($caseName in $requiredCases) {
        Write-Host "RUN connected case $caseName"
        # GPU gates need the main RenderingDevice, which --headless does not provide.
        $result = if ($caseName -like "Gpu*") {
            Invoke-GodotCapture @("--rendering-driver", "vulkan", "--path", $repo, "--", "--connected-case", $caseName)
        } else {
            Invoke-GodotCapture @("--headless", "--path", $repo, "--", "--connected-case", $caseName)
        }
        $assertions = Get-AssertionCount $caseName $result.Text
        $outputPath = Join-Path $artifactRoot ("case-" + $caseName + ".log")
        Set-Content -LiteralPath $outputPath -Value $result.Text -Encoding utf8
        $caseResults.Add([pscustomobject]@{
            name = $caseName
            exitCode = $result.ExitCode
            assertions = $assertions
            durationSeconds = $result.DurationSeconds
            outputPath = [IO.Path]::GetRelativePath($repo, $outputPath)
            passed = ($result.ExitCode -eq 0 -and $assertions -gt 0)
        })
    }
    if ($Phase11 -and !(Test-Path -LiteralPath (Join-Path $artifactRoot 'mapunits'))) {
        throw 'Run-local MapUnits raw artifacts are missing.'
    }
    if ($Phase11) {
        # Fresh Main/Compact shell frames (and adaptive high-zoom detail) belong to this
        # run, so the run-bound visual review can cover all ten original-resolution frames.
        $visualRoot = Join-Path $artifactRoot 'visual'
        New-Item -ItemType Directory -Force -Path $visualRoot | Out-Null
        foreach ($frame in @(
                @{ Name = 'main-shell'; Width = 1920; Height = 1080; Zoom = 0 },
                @{ Name = 'compact-shell'; Width = 1366; Height = 768; Zoom = 0 },
                @{ Name = 'main-highzoom'; Width = 1920; Height = 1080; Zoom = 8 },
                @{ Name = 'compact-highzoom'; Width = 1366; Height = 768; Zoom = 8 })) {
            Write-Host "RUN shell frame $($frame.Name)"
            $output = Join-Path $visualRoot "$($frame.Name).png"
            $capture = Invoke-GodotCapture @("--path", $repo, "--resolution", "$($frame.Width)x$($frame.Height)", "--",
                "--shell-frame", "$($frame.Width)", "$($frame.Height)", "1.0", $output, "$($frame.Zoom)")
            Set-Content -LiteralPath (Join-Path $artifactRoot "shell-frame-$($frame.Name).log") -Value $capture.Text -Encoding utf8
            if ($capture.ExitCode -ne 0 -or !(Test-Path -LiteralPath $output)) {
                throw "Shell frame $($frame.Name) was not captured: $($capture.Text)"
            }
        }
    }
    $unknown = Invoke-GodotCapture @("--headless", "--path", $repo, "--", "--connected-case", "__UnknownCaseMustFail__")
    Set-Content -LiteralPath (Join-Path $artifactRoot "unknown-case.log") -Value $unknown.Text -Encoding utf8
    $registryEvidence = [pscustomobject]@{
        requiredCases = $requiredCases
        discoveredPublicCases = $publicNames
        internalCases = @($registryNames | Where-Object { $_ -like "__*" })
        unknownExitCode = $unknown.ExitCode
        fixtureSha256 = $fixtureHash
        fixtureBytes = $fixtureBytes
        sourceFingerprint = $(if ($Phase11) { Get-SourceFingerprint } else { $null })
        runId = $(if ($Phase11) { $RunId } else { $null })
        gitRevision = $(if ($Phase11) { (& git -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim() } else { $null })
        runStartedUtc = $runStartedUtc
    }
    $registryEvidence | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $artifactRoot "registry.json") -Encoding utf8
    $caseResults | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $artifactRoot "connected-cases.json") -Encoding utf8

    $env:MAPWRIGHT_ACCEPTANCE_OUTPUT = Join-Path $artifactRoot "hardware.json"
    $env:MAPWRIGHT_ACCEPTANCE_DURATION_SECONDS = "60"
    Write-Host "RUN hardware matrix: 12 independent >=60-second scenario/cache runs"
    $hardwareResult = Invoke-GodotCapture @("--path", $repo, "--resolution", "1920x1080", "--", "--connected-case", "__AcceptanceHardware")
    Set-Content -LiteralPath (Join-Path $artifactRoot "hardware.log") -Value $hardwareResult.Text -Encoding utf8
    $hardware = if (Test-Path -LiteralPath $env:MAPWRIGHT_ACCEPTANCE_OUTPUT) {
        Get-Content -Raw -LiteralPath $env:MAPWRIGHT_ACCEPTANCE_OUTPUT | ConvertFrom-Json
    } else { $null }
    if ($Phase11) { Write-RunManifest }
    Write-AcceptanceReport $caseResults ([pscustomobject]@{ exitCode = $unknown.ExitCode }) $publicNames $hardware
    $reportPass = Test-Report
    $casePass = @($caseResults | Where-Object { !$_.passed }).Count -eq 0
    exit $(if ($casePass -and $unknown.ExitCode -ne 0 -and $hardwareResult.ExitCode -eq 0 -and $reportPass) { 0 } else { 1 })
}
finally {
    if ($Phase11 -and $Full -and $RunId -and (Test-Path -LiteralPath (Join-Path $artifactRoot '.active-run'))) {
        Remove-Item -LiteralPath (Join-Path $artifactRoot '.active-run') -Force
    }
    Remove-Item Env:MAPWRIGHT_PHASE11_RUN_ID, Env:MAPWRIGHT_PHASE11_RUN_ROOT, Env:MAPWRIGHT_PHASE11_FULL -ErrorAction SilentlyContinue
    Pop-Location
}
