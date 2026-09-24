---
last_mapped_commit: ac4a7877683666650894b025737047a903c69740
last_mapped_at: 2026-09-22
---
# Technology Stack

**Analysis Date:** 2026-09-22

## Languages

**Primary:**

- C# 12 targeting .NET 8: Godot shell and probes in `Scripts/`, engine-independent libraries in `src/`; versions are explicit in `Mapwright.csproj` and `src/Mapwright.Domain/Mapwright.Domain.csproj`.

**Secondary:**

- GLSL 450 compute shaders: terrain coverage, jump-flood distance fields, coastline and export kernels in `Shaders/terrain_coverage.glsl`, `Shaders/terrain_sdf_jump.glsl`, and `Shaders/export_tile.glsl`.
- PowerShell: Windows development and validation harnesses in `Scripts/run-spike.ps1` and `Scripts/run-contract-tests.ps1`.

## Runtime

**Environment:**

- Godot 4.7.2 .NET, pinned by `Mapwright.csproj`; `project.godot` selects C# and Forward Plus features and launches `Scenes/Main.tscn`.
- .NET SDK 8.0.425 with `latestPatch` roll-forward in `global.json`.
- `Mapwright.csproj` contains an Android-specific `net9.0` conditional; this is configuration, not evidence of supported Android deployment.

**Package Manager:**

- NuGet through the .NET SDK; `NuGet.Config` enables the portable Godot package directory and nuget.org.
- No checked-in NuGet lockfile detected; direct package versions are pinned in `Mapwright.csproj` and `src/Mapwright.Infrastructure/Mapwright.Infrastructure.csproj`.

## Frameworks

**Core:**

- Godot.NET.Sdk 4.7.2: native Control-based UI, rendering device integration and scene lifecycle in `Mapwright.csproj`, `Scripts/App/Main.cs`, and `Scripts/Rendering/GlobalGpuBrushSurface.cs`.
- Plain .NET class libraries: domain, application ports and SQLite infrastructure in `src/Mapwright.Domain/`, `src/Mapwright.Application/`, and `src/Mapwright.Infrastructure/`.

**Testing:**

- Custom executable contract runner; no xUnit/NUnit/MSTest package in `tests/Mapwright.ContractTests/Mapwright.ContractTests.csproj`; assertions and test dispatch live in `tests/Mapwright.ContractTests/Program.cs`.
- Godot-hosted probes and forced-termination worker in `Scripts/App/Main.cs`, `Scripts/Core/DurableEditRecoveryProbe.cs`, and `Tools/StorageCrashWorker/Program.cs`.

**Build/Dev:**

- MSBuild / `dotnet build` for the Godot project and worker; portable Windows executables are selected by `Scripts/run-spike.ps1`.
- `Scripts/run-contract-tests.ps1` restores and runs the engine-independent test executable.

## Key Dependencies

**Critical:**

- Godot.NET.Sdk 4.7.2 supplies engine bindings and build integration (`Mapwright.csproj`).
- Microsoft.Data.Sqlite 8.0.31 supplies embedded transactional storage (`src/Mapwright.Infrastructure/Mapwright.Infrastructure.csproj`).
- .NET BCL `System.Text.Json`, `System.IO.Compression`, and `System.Security.Cryptography` handle streaming import, gzip/PNG processing and content hashes (`Scripts/Core/InkImportService.cs`, `Scripts/Core/ProjectStore.cs`, `Scripts/Export/StreamingPngWriter.cs`).

**Infrastructure:**

- Local SQLite with WAL and `synchronous=FULL` in `src/Mapwright.Infrastructure/SqliteProjectRepository.cs`; no external database server.
- Godot `RenderingDevice` GPU compute in `Scripts/Rendering/GlobalGpuBrushSurface.cs`; no parallel Rust/wgpu implementation is selected by `docs/engine-decision.md`.

## Configuration

**Environment:**

- `MAPWRIGHT_INK_FIXTURE` overrides the machine-specific sample path in `Scripts/App/Main.cs`; `Scripts/run-spike.ps1` sets it from its fixture argument.
- Harnesses configure portable SDK, NuGet cache, temporary and application-data paths through process environment variables (`Scripts/run-spike.ps1`, `Scripts/run-contract-tests.ps1`). No service keys are required by inspected application code.

**Build:**

- `global.json`: SDK selection; `NuGet.Config`: package sources; `Mapwright.csproj`: language/runtime, Godot SDK and project references; `project.godot`: engine entry scene and window settings.
- `Mapwright.csproj` excludes `src/`, `tests/`, and `Tools/` source globs and references the three `src/` projects explicitly.

## Platform Requirements

**Development:**

- Current supplied harnesses require Windows PowerShell and portable Windows binaries under `.tools/` (`Scripts/run-spike.ps1`). Those binaries are ignored in `.gitignore`.
- GPU probes require a Godot RenderingDevice-capable backend; the compatibility renderer is not the specified compute fallback (`docs/spec.md`, `Scripts/Rendering/GlobalGpuBrushSurface.cs`).

**Production:**

- Native local-first Windows/Linux desktop editor is the target (`README.md`); Linux validation and the connected editor workflow remain acceptance work (`docs/engine-decision.md`).
- No production packaging/export preset or hosting pipeline detected alongside `project.godot`. Use `docs/engine-decision.md` for the accepted stack; roadmap features in `docs/spec.md` are not proof of implementation.

---

*Stack analysis: 2026-09-22*
