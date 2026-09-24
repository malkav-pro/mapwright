---
last_mapped_commit: 2aa524f6cc18dcb5d6b04f5332902248f6013693
last_mapped_at: 2026-09-23
---
# Codebase Structure

**Analysis Date:** 2026-09-22

## Directory Layout

```text
mapwright/
├── project.godot                 # Godot startup/display configuration
├── Mapwright.csproj              # Godot C# host and project references
├── Scenes/Main.tscn              # Root Control scene
├── Scripts/
│   ├── App/                     # Actual UI and probe dispatcher
│   ├── Core/                    # Spike import, manifest storage and history
│   ├── Rendering/               # Godot GPU renderers and hardware probes
│   ├── Export/                  # Streaming PNG and export fixtures
│   └── run-*.ps1                # Development/probe launchers
├── Shaders/                     # GLSL compute resources
├── src/
│   ├── Mapwright.Domain/        # Engine-independent immutable model/commands
│   ├── Mapwright.Application/   # Edit orchestration and ports
│   └── Mapwright.Infrastructure/# SQLite repository and clock adapter
├── tests/Mapwright.ContractTests/ # Standalone contract-test executable
├── Tools/StorageCrashWorker/     # Separate forced-termination worker
├── Assets/
│   └── UI/                     # Offline fonts, OFL provenance and accessible SVG icon atlas
├── docs/                        # Requirements, decisions, architecture, evidence
└── .planning/codebase/          # Generated codebase maps
```

## Directory Purposes

**`Scripts/`:**

- The Godot host compiles this source tree through `Mapwright.csproj`; `Scripts/App/Main.cs` dispatches the connected project-entry/editor shell and retains historical probe routes. `Scripts/App/EditorTheme.cs` owns the offline visual system, while `Scripts/App/ConnectedUiSmoke.cs` owns entry/import/shell composition plus registered UI smoke cases.
- `Scripts/Core/MapDocument.cs` and `Scripts/Core/EditingDocument.cs` are spike types. Do not confuse these with the similarly named concepts in `src/Mapwright.Domain/`.
- `Scripts/Rendering/ConnectedTerrainGraph.cs` is the connected frozen-revision evaluator shared by the canvas and PNG export. `Scripts/Rendering/RenderResourceLedger.cs` owns checked CPU/GPU/export/process/history admission and the explicit inspection-only hardware outcome. GPU fixtures in `GpuSeamProbe.cs` and `TerrainPipelineExportProbe.cs` remain separate evidence paths.

**`src/`:**

- Three separate assemblies with inward dependencies, declared by `src/Mapwright.Domain/Mapwright.Domain.csproj`, `src/Mapwright.Application/Mapwright.Application.csproj` and `src/Mapwright.Infrastructure/Mapwright.Infrastructure.csproj`.
- `Mapwright.csproj` excludes `src/**/*.cs` from direct compilation and references the projects instead; apply the same pattern when introducing another independently compiled project.

**`tests/` and `Tools/`:**

- `tests/Mapwright.ContractTests/Program.cs` tests the engine-independent application path.
- `Tools/StorageCrashWorker/Program.cs` dispatches spike crash-child modes. These trees are excluded from host source compilation in `Mapwright.csproj`.

**`docs/`:**

- `docs/README.md` indexes document precedence and provenance; `docs/reference/map-editor-spec-final.md` preserves the historical source specification.
- `docs/spec.md` is the product specification, not an inventory of implemented features.
- `docs/engine-decision.md` records the selected stack and device-loss contract.
- `docs/architecture.md` records accepted boundaries and remaining integration work; `docs/spike-report.md` records probe evidence.

## Key File Locations

**Entry Points:**

- `project.godot`, `Scenes/Main.tscn`, `Scripts/App/Main.cs`: runtime startup chain.
- `tests/Mapwright.ContractTests/Program.cs`: contract test runner.
- `Tools/StorageCrashWorker/Program.cs`: process-isolated storage probe worker.

**Configuration:**

- `Mapwright.csproj`: host compilation and references.
- `global.json`: .NET SDK selection; `NuGet.Config`: package source configuration.
- `project.godot`: scene, window and rendering configuration.
- `.gitignore`: generated engine/build/probe/project-data exclusions.

**Core Logic:**

- `src/Mapwright.Domain/Commands.cs`: paint/river commands and invalidation.
- `src/Mapwright.Application/EditSession.cs`: durable commit, publication and render-enqueue ordering.
- `src/Mapwright.Infrastructure/SqliteProjectRepository.cs`: transaction-backed revision snapshots.
- `Scripts/Core/InkImportService.cs`, `Scripts/Core/ProjectStore.cs`: actual UI import/save implementation.
- `Scripts/Rendering/GlobalGpuBrushSurface.cs`: interactive GPU brush; `Shaders/interactive_brush.glsl`: compute kernel.
- `Scripts/Rendering/GpuJfaTerrainRenderer.cs` and `Shaders/terrain_sdf_*.glsl`: terrain distance pipeline.
- `Scripts/Rendering/ConnectedTerrainGraph.cs`: fixed-order Background/Foreground texture/coverage/Land/river evaluation for viewport and PNG rows.
- `Scripts/Rendering/RenderResourceLedger.cs`: startup hardware observations, checked allocation caps, and export band/halo fitting.

**Testing:**

- `tests/Mapwright.ContractTests/Program.cs`, `Scripts/run-contract-tests.ps1`: domain/application/SQLite contracts.
- `Scripts/run-connected-phase1.ps1`: registered real-fixture connected cases, including theme/entry/shell UI state, and fresh-process reopen/export checks. The shell frame route writes ignored visual-smoke PNGs under `artifacts/ui-smoke/`.
- `Scripts/run-spike.ps1`, `Scripts/run-interactive-probe.ps1`, `Scripts/run-import-memory-probe.ps1`, `Scripts/run-terrain-export-16k.ps1`, `Scripts/run-tdr-probe.ps1`: explicit probe launchers; inspect their behavior before execution.

## Naming Conventions

**Files:**

- Use PascalCase C# filenames matching primary types, as in `src/Mapwright.Application/EditSession.cs`.
- Use snake_case GLSL names such as `Shaders/terrain_sdf_seed.glsl` and hyphenated PowerShell launchers such as `Scripts/run-contract-tests.ps1`.

**Directories:**

- Use `Mapwright.<Layer>` assembly directories under `src/`; the corresponding namespaces follow those names.
- Preserve case in Godot resource paths, for example `res://Shaders/interactive_brush.glsl` in `Scripts/Rendering/GlobalGpuBrushSurface.cs`.

## Where to Add New Code

**New Feature:**

- Put deterministic document rules in `src/Mapwright.Domain/`, orchestration/ports in `src/Mapwright.Application/`, and persistence adapters in `src/Mapwright.Infrastructure/`.
- Add focused contract providers implementing `IContractCaseProvider`; reflection discovers them without editing the runner loop. UI integration belongs at `Scripts/App/Main.cs`, `Scripts/App/ConnectedUiSmoke.cs`, `Scripts/App/EditorTheme.cs`, and `Scripts/App/MapCanvas.cs`, with the boundary constraints in `docs/architecture.md`.

**New Component/Module:**

- GPU implementation currently lives in `Scripts/Rendering/` and `Shaders/`; `docs/architecture.md` specifies a dedicated rendering adapter assembly, but no such `src/` project exists.
- Retain probe-specific fixtures in `Scripts/Rendering/`, `Scripts/Export/` or `Tools/StorageCrashWorker/` as appropriate. The separate `Mapwright.Spike` assembly named in `docs/architecture.md` is not present.

**Utilities:**

- Keep geometry in `src/Mapwright.Domain/Geometry.cs` and system adapters near `src/Mapwright.Infrastructure/SystemClock.cs`; no shared utility directory is detected.

## Special Directories

- `.godot/`, `.mono/`, `bin/`, `obj/`: generated engine/build state, excluded by `.gitignore`; do not treat their copies as source.
- `artifacts/`: generated probe reports/images/project fixtures, excluded by `.gitignore`; producers include `Scripts/App/Main.cs`.
- `*.mapwright/`: runtime project directories excluded by `.gitignore`; connected projects use SQLite plus verified blobs, while `Scripts/Core/ProjectStore.cs` remains the legacy manifest/blob probe path.
- `.codex/`: bundled workflow tooling, outside the product source mapping scope.
- `.planning/codebase/`: generated planning references, including this file; not part of the Godot runtime.

---

*Structure analysis: 2026-09-22*
