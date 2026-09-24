---
last_mapped_commit: ac4a7877683666650894b025737047a903c69740
last_mapped_at: 2026-09-22
---
# Codebase Concerns

**Analysis Date:** 2026-09-22

Scope: focused source inspection; tests and performance/destructive probes were not rerun. Measurements refer to recorded evidence in `docs/spike-report.md`. Use `docs/spec.md` for current requirements and `docs/engine-decision.md` for the accepted Godot stack; `docs/reference/map-editor-spec-final.md` is a provenance snapshot.

## Tech Debt

**Disconnected persistence and editing paths:**

- Issue: the Godot shell imports previews and saves through the prototype JSON/blob store; viewport brush actions go directly to a GPU surface. The separated application/SQLite projects are referenced but these UI actions do not use their commit path.
- Files: `Scripts/App/Main.cs`, `Scripts/App/MapCanvas.cs`, `Scripts/Core/ProjectStore.cs`, `src/Mapwright.Application/EditSession.cs`, `src/Mapwright.Infrastructure/SqliteProjectRepository.cs`, `Mapwright.csproj`.
- Impact: visible GPU edits do not establish durable committed edits. Prototype import storage, isolated journal replay and SQLite contracts do not form one authoritative workflow.
- Fix approach: connect import conversion, commands, render invalidation, reopen and export through the same session; preserve persist-before-acknowledgement ordering in `src/Mapwright.Application/EditSession.cs`.

**Initial publication is not ongoing project saving:**

- Issue: `Scripts/Core/ProjectStore.cs` stages an import and rejects an existing destination. It has no revision-update path, and its ordinary blob/manifest writes do not explicitly flush to disk.
- Impact: process-kill publication evidence does not prove power-loss durability or incremental saves.
- Fix approach: use transactional revisions for ongoing edits and define durable blob publication/recovery while retaining hash verification.

## Known Bugs

**Device recreation fails after recorded Windows TDR:**

- Symptoms: local Vulkan device recreation fails and native cleanup terminates the child on the documented Windows/AMD configuration (`docs/spike-report.md`).
- Files: `Scripts/Rendering/DeviceLossProbe.cs`, `Scripts/run-tdr-probe.ps1`, `Shaders/tdr_hang.glsl`.
- Trigger: deliberate non-terminating compute shader; not reproduced during mapping.
- Workaround: fresh-process restart and replay of already durable commands, as required by `docs/engine-decision.md`. Reliable post-loss cleanup is not established.

## Security Considerations

**Importer allocations lack explicit size budgets:**

- Risk: token buffers double until a full JSON token fits; decoded raster arrays remain resident. Large inputs can exhaust memory despite incremental parsing.
- Files: `Scripts/Core/InkImportService.cs`.
- Current mitigation: JSON depth limit of 256, base64 validation and PNG signature checks. Explicit token-byte and aggregate raster limits are not present in these paths.
- Recommendations: enforce budgets before allocation/decode and add oversized-input fixtures with readable errors.

## Performance Bottlenecks

**Synchronous save callback:**

- Problem: source hashing/copying and raster writes execute directly in `SaveRecoveredProject` on the UI path.
- Files: `Scripts/App/Main.cs`, `Scripts/Core/ProjectStore.cs`.
- Cause: unlike import, save is not dispatched as background work.
- Improvement path: application jobs with progress/cancellation and atomic publication. Actual stall duration is unmeasured.

**Full snapshots per revision:**

- Problem: each SQLite commit stores the complete serialized project alongside its command; history retention/compaction is not implemented here.
- Files: `src/Mapwright.Infrastructure/SqliteProjectRepository.cs`, `src/Mapwright.Domain/MapModel.cs`.
- Improvement path: measure long-session growth before selecting checkpoint/replay policies; no growth benchmark was rerun.

## Fragile Areas

**Latency instrumentation cannot identify presented edits:**

- Files: `Scripts/App/MapCanvas.cs`, `Scripts/Rendering/GlobalGpuBrushSurface.cs`.
- Why fragile: `FramePostDraw` completes timestamps without correlating the displayed dab. The probe explicitly leaves its latency gate unestablished.
- Safe modification: propagate command/revision or sequence IDs through rendered updates before asserting input-to-visible latency.
- Test coverage: the 30-second, single 1,024 px surface benchmark does not cover the 60-second four-layer warm/cold/evicted-cache workflow required in `docs/spike-report.md`.

## Scaling Limits

**Memory evidence is fixture-specific:**

- Current capacity: recorded import peak is 854,876,160 bytes and 16K terrain export peak is 1,861,361,664 bytes in `docs/spike-report.md`; these are observations, not capacity guarantees.
- Files: `Scripts/Core/InkImportService.cs`, `Scripts/Export/TerrainPipelineExportProbe.cs`, `Scripts/Rendering/GpuJfaTerrainRenderer.cs`.
- Limit: internal/driver GPU overhead, installed-memory percentage gates and connected multi-layer cache pressure remain unmeasured.
- Scaling path: preserve tile-local buffers and measure combined document/cache/render allocations in the connected editor.

## Dependencies at Risk

- Dependency vulnerabilities or abandonment were not assessed. `Mapwright.csproj` relies on Godot .NET/Vulkan; `docs/engine-decision.md` selects this stack. Linux and physical tablet behaviour remain unvalidated in `docs/spike-report.md`.

## Missing Critical Features

**Connected editing and export controls:**

- Problem: four-layer painting, river handles, visible undo, coastline updates and save/reopen/export do not consume one shared document through the UI. Terrain export progress and responsive cancellation remain absent.
- Files: `Scripts/App/Main.cs`, `Scripts/App/MapCanvas.cs`, `Scripts/Export/TerrainPipelineExportProbe.cs`, `docs/spike-report.md`.
- Blocks: product acceptance; isolated domain, durability and renderer probes cannot close this integration gap.

**Semantic import recovery:**

- Problem: importer preserves rasters, source and provenance/counts rather than fully replaying history into editable semantics; missing assets are reported.
- Files: `Scripts/Core/InkImportService.cs`, `Scripts/Core/MapDocument.cs`, `Scripts/App/Main.cs`.
- Blocks: faithful editing of all recovered entities/effects. The source-preservation probe re-encodes a preview; it does not recompose recovered layers (`docs/spike-report.md`).

## Test Coverage Gaps

**Connected durability and visible restoration — High:**

- What's not tested: acknowledged UI edits surviving device-loss restart, visible undo over resident/evicted tiles, layer recomposition and multi-revision replacement through the editor.
- Files: `tests/Mapwright.ContractTests/Program.cs`, `Scripts/Core/DurableEditRecoveryProbe.cs`, `Scripts/App/Main.cs`, `docs/spike-report.md`.
- Risk: isolated commit-ordering and SQLite reopen tests can pass while UI integration bypasses those guarantees.

**Ordering, effects and platform fixtures — Medium:**

- What's not tested: alternating transparent atlas-page ordering, blur/shadow/corner stamps, dedicated fonts, unknown state-changing import commands, physical tablets and Linux.
- Files: `Scripts/Rendering/GpuSeamProbe.cs`, `Scripts/Core/InkImportService.cs`, `Scripts/App/MapCanvas.cs`, `docs/spike-report.md`.
- Risk: terrain seam success does not cover these cases. These are open acceptance requirements, not newly reproduced bugs.

---

*Concerns audit: 2026-09-22*
