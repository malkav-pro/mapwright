---
last_mapped_commit: ac4a7877683666650894b025737047a903c69740
last_mapped_at: 2026-09-22
---
# External Integrations

**Analysis Date:** 2026-09-22

## APIs & External Services

**Application services:**

- No network service SDK, HTTP client, cloud API or remote asset lookup detected in `Scripts/`, `src/`, or their project manifests. The native shell uses local imports (`Scripts/App/Main.cs`).

**File interoperability:**

- Inkarnate `.ink` v3: local gzip/JSON import through BCL streams and an incremental tokenizer (`Scripts/Core/InkImportService.cs`). No account authentication or Inkarnate API connection.
- Import recovers embedded preview/raster data and records unresolved asset IDs; command payloads are not converted into native history (`Scripts/Core/InkImportService.cs`). Do not treat asset references as downloaded art.
- PNG: custom streaming encoder and validation in `Scripts/Export/StreamingPngWriter.cs` and `Scripts/Export/PngValidator.cs`; GPU-backed export probes use these local file boundaries (`Scripts/Export/TerrainPipelineExportProbe.cs`).

**Build-time services:**

- NuGet package restore uses nuget.org plus the portable Godot SDK package directory (`NuGet.Config`). This is a tooling dependency, not an application backend.

## Data Storage

**Databases:**

- Embedded SQLite via Microsoft.Data.Sqlite 8.0.31 (`src/Mapwright.Infrastructure/Mapwright.Infrastructure.csproj`).
  - Connection: project-directory `scene.sqlite`, constructed in code; no connection-string environment variable (`src/Mapwright.Infrastructure/SqliteProjectRepository.cs`).
  - Stores revision snapshots, commands, schema version and current project revision; enables foreign keys, WAL and `synchronous=FULL` (`src/Mapwright.Infrastructure/SqliteProjectRepository.cs`).
  - Implemented and exercised by `tests/Mapwright.ContractTests/Program.cs`; the Godot shell in `Scripts/App/Main.cs` still uses `ProjectStore` for imported-project persistence.

**File Storage:**

- Local filesystem only: import publication writes `manifest.json`, `import-report.txt` and SHA-256-addressed `blobs/` through a staging directory (`Scripts/Core/ProjectStore.cs`).
- The shell saves under Godot's globalized `user://projects` directory (`Scripts/App/Main.cs`). Original import bytes and recovered rasters are retained and verified on reopen (`Scripts/Core/ProjectStore.cs`).
- Probe results and image output go under `artifacts/spike/` (`Scripts/App/Main.cs`); generated artifacts are ignored by `.gitignore`.

**Caching:**

- Local disposable GPU resources in `Scripts/Rendering/GlobalGpuBrushSurface.cs`; SQLite project creation also creates `cache/tiles/` and `cache/history/` directories (`src/Mapwright.Infrastructure/SqliteProjectRepository.cs`). Directory creation alone is not a complete cache implementation.
- No remote cache integration detected in `Mapwright.csproj` or `src/Mapwright.Infrastructure/Mapwright.Infrastructure.csproj`.

## Authentication & Identity

**Auth Provider:**

- Not detected. `Scripts/App/Main.cs` provides a local desktop workflow without login or remote identity; project identifiers in `src/Mapwright.Domain/MapModel.cs` represent documents rather than user accounts.

## Monitoring & Observability

**Error Tracking:**

- No hosted error-tracking dependency in `Mapwright.csproj`; shell failures use `GD.PrintErr` in `Scripts/App/Main.cs`.

**Logs:**

- Godot console output and JSON probe reports (`Scripts/App/Main.cs`); local process/memory measurements use `System.Diagnostics` there.

## CI/CD & Deployment

**Hosting:**

- Native desktop target, no service hosting configuration detected; Windows and Linux are named in `README.md`, with Linux validation still outstanding in `docs/engine-decision.md`.

**CI Pipeline:**

- No application CI workflow detected. Local validation entry points are `Scripts/run-contract-tests.ps1`, `Scripts/run-spike.ps1`, and the specialized probe scripts under `Scripts/`.

## Environment Configuration

**Required env vars:**

- No application service credentials required by inspected code. `MAPWRIGHT_INK_FIXTURE` selects an external local sample file and otherwise falls back to a developer-specific path (`Scripts/App/Main.cs`).
- Harness-managed variables include `DOTNET_ROOT`, `DOTNET_HOST_PATH`, `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `APPDATA`, `LOCALAPPDATA`, `TEMP`, `TMP`, and `PATH` (`Scripts/run-spike.ps1`); these are tool/runtime paths.

**Secrets location:**

- No application secret store or `.env` configuration detected in the inspected application scope; configuration is in `project.godot`, `NuGet.Config`, and the local harnesses. No secret files were read.

## Webhooks & Callbacks

**Incoming:**

- No webhooks. Entry points are the Godot scene and local user arguments, including probe switches (`project.godot`, `Scripts/App/Main.cs`).

**Outgoing:**

- No network callbacks detected. Rendering callbacks stay inside the process (`Scripts/Rendering/GlobalGpuBrushSurface.cs`); file export and local crash-worker processes form the external boundaries (`Scripts/App/Main.cs`, `Tools/StorageCrashWorker/Program.cs`).

---

*Integration audit: 2026-09-22*
