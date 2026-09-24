# Phase 0 spike report

Date: 2026-09-22 (Asia/Jerusalem)

Platform: Windows 11 build 26200, 12 logical processors

GPU: AMD Radeon RX 7800 XT, Vulkan 1.4.349

Runtime: Godot 4.7.2 .NET, .NET SDK 8.0.425

## Decision

Godot 4 .NET is final for the first implementation. The measured work does not justify the cost of moving to Rust + wgpu. This is an engine decision, not a declaration that all Phase 0 acceptance work is complete.

Device loss is a save-and-restart condition on the tested Windows/AMD configuration. A Windows TDR left Godot unable to recreate a usable Vulkan device in-process and cleanup eventually terminated the child natively. Recovery therefore cannot depend on post-loss managed code or an orderly shutdown. Commands must reach durable storage before the UI acknowledges them; a fresh process replays those commands and rebuilds disposable GPU state.

The decisive interaction milestone is still open. Existing interaction evidence covers one persistent 1,024 px coverage texture with a simple colour pass for 30 seconds. It does not establish responsiveness for the real imported map, four visible terrain layers, live coastline updates, river editing, visible undo or cold/evicted caches.

## Next acceptance milestone

Build one connected workflow over the real imported map with four visible terrain layers, actual texture painting, coastline effects and an editable river. Painting, river point/width edits, undo/redo, viewport updates, save/reopen and export must consume the same authoritative document state.

At a 1,920 x 1,080 viewport on this development machine, the explicit targets are input-to-visible p95 <= 50 ms and p99 <= 100 ms; frame-interval p95 <= 20 ms and p99 <= 33.3 ms while painting and navigating; and visible recent undo affecting up to 16 resident tiles within 100 ms at p95. These are acceptance targets, not demonstrated results.

Run each of painting across tile boundaries, pan/zoom during painting, river point/width editing with coastline updates, and undo/redo for at least 60 seconds under warm-cache, cold-cache and cache-eviction conditions. Record p50/p95/p99, every stall over 100 ms and its cause, longest stall, queue/backlog behaviour, RAM, GPU allocations where available, visible correctness, and generated/processed/coalesced/dropped sample counts. Sequence ids must correlate each edit to the rendered update containing it; a generic `FramePostDraw` event is not sufficient. Correctness readbacks remain outside the normal interactive path.

## Measured results

| Probe | Result | Measurement |
| --- | --- | --- |
| Native compute | Pass | AMD RX 7800 XT; deterministic 4,096-value compute/readback; about 67 ms in the latest full suite, including device and resource creation |
| Terrain kernel | Pass | 512 px soft coverage plus river fixture and bounded coastline/wave pass; about 0.7 ms per pass in the latest suite |
| Real terrain seams | Pass for implemented pipeline | Persistent coverage from accumulated soft strokes and a cross-tile river; 512 px whole JFA render versus sixteen 128 px tiles with 22 px halos; zero differing channels and zero boundary differences |
| Agreement with discrete distance oracle | Pass | JFA plus refinement differed by at most 0.124 px from the 45 x 45 opposite-class pixel-centre oracle; this does not establish accuracy to the interpolated 0.5 coverage contour |
| Earlier brush/viewport throughput | Measured; latency gate not established | 30 seconds at 120 synthetic samples/s on one texture. Monotonic frame interval was 17.23 ms p95, but submission-to-`FramePostDraw` was not correlated to presentation of a particular dab. `LatencyGateEstablished` remains false. |
| Same-pipeline terrain export | Correctness pass at 2K | 2,048 x 2,048, sixteen 512 px JFA tiles with 22 px halos; validated PNG in 1.84 s in the latest suite; refreshed process peak 603,594,752 bytes and explicit GPU buffers 13,226,048 bytes |
| Same-pipeline 16K terrain export | Correctness/bounded-memory pass for current fixture | Separate measured run: sixty-four 2,048 px JFA tiles with 22 px halos; validated PNG in 96.92 s; refreshed process peak 1,861,361,664 bytes and explicit renderer buffers 191,107,136 bytes. Duration is observational. |
| Analytic 16K export baseline | Pass | 64 GPU tiles; validated 50,385,558-byte PNG in 5.45 s in the latest suite. This is plumbing evidence, not terrain performance evidence. |
| Export replacement | Pass | Injected mid-render failure preserved the previous destination and removed the temporary sibling |
| Initial-publication crash recovery | Pass | Separate worker processes were killed after blob write, manifest write and publication; pre-publication projects stayed invisible and the published project reopened with valid hashes |
| Acknowledged-edit crash recovery | Pass for isolated document core | Commands were appended and flushed before acknowledgement. Workers were killed during editing and after export temporary-file creation; restart replay recovered four layer strokes plus river point/width edits, preserved the previous export and removed the partial file. |
| Vulkan device-loss characterization | Pass; restart required | A non-terminating shader triggered Windows TDR. In-process device recreation failed and cleanup ended in a native exception; a fresh Godot process immediately passed the complete JFA seam probe. |
| Streaming `.ink` import | Pass for current fixture | Incremental parsing recovered 3 rasters, 3,596 commands and 375 entities with a 64 MiB maximum token buffer and 73,947,547 retained raster bytes. The isolated run peaked at 854,876,160 bytes including a 464,609,280-byte Godot/Vulkan baseline. |
| Source preservation | Pass for prototype store | The real 74,541,363-byte `.ink` and four recovered raster layers were stored as immutable content-addressed blobs; cache deletion, reopen, hash verification, preview decode and re-encode passed. Layer recomposition remains untested. |
| River/history core | Pass for resident commands | 2,000 document-owned point/width edits; deterministic undo/redo and redo-branch invalidation passed. Point moves include old/new adjacent segments and width changes include downstream effect reach. Visible tile restoration remains untested. |

The importer no longer retains a decompressed JSON DOM. Its current peak includes the Godot/Vulkan baseline, a growable buffer large enough for the largest base64 token, and retained decoded raster source data. Full-suite peaks also include prior render/export allocations and are not attributed to import alone.

## Gate status

| Spec gate | Status | Evidence or next test |
| --- | --- | --- |
| Source preservation | Provisional pass | Real source and raster blobs survive cache deletion, reopen and hash verification. Layer recomposition and multi-revision storage remain. |
| Input-to-visible update | Not established | Add sequence-correlated rendered-update measurement over the shared four-layer document. |
| Viewport responsiveness | Not established for acceptance workflow | Run the required 60-second warm/cold/eviction scenarios with four layers, live coastline work, river edits and visible undo. |
| Memory | Partial | Tile-local terrain and streaming PNG/import paths are bounded for tested fixtures. Driver/internal GPU overhead and percentage-of-installed-memory gates remain unmeasured. |
| Export reliability | Partial pass | Correct output, bounded buffers and previous-destination preservation pass. Progress reporting and responsive cancellation are not implemented/tested. Duration has no threshold. |
| Seams | Pass for implemented terrain pipeline | Coverage, halos, JFA, coast rings, global coordinates and a cross-tile river are bit-exact. Blur/shadow/stamp fixtures remain. |
| Dependencies | Partial | Halo coastline rings, global sampling and a cross-tile river run. Sequential blurs, shadows and corner stamps remain. |
| Ordering | Not run | Alternating transparent atlas-page stamp fixture remains. |
| Save and recovery | Partial pass | Forced termination proves initial publication and isolated persist-before-acknowledgement replay during editing/export. The connected editor and multi-revision replacement remain. |
| Device loss | Characterized; restart contract passes in isolation | Windows TDR demonstrated that orderly in-process recovery is unreliable. Fresh-process rendering and isolated journal replay pass; the connected editor must exercise the complete path. |
| Import degradation | Partial | Missing assets are reported. Dedicated fonts, unknown state-changing commands, cache resolutions and baked-effect fixtures remain. |
| Undo | Partial pass | Resident command restoration and conservative river invalidation pass. Visible restoration for up to 16 tiles, brush commands and evicted replay remain untested. |
| Engine decision | Final for first implementation | Continue with Godot. A stack change now requires a demonstrated engine constraint that cannot be contained behind the renderer boundary. |

## Required versus implemented recovery

Required behaviour: every acknowledged command is already durable; GPU resources are caches; a device-loss crash may discard only the active unacknowledged gesture; restart replays the journal; a partial export never replaces the previous destination.

Implemented evidence: the document core journals paint and river commands with write-through plus an explicit disk flush before applying/acknowledging them. A process-kill harness recovers those commands after termination during editing and export and cleans the partial export. The TDR harness proves that a fresh process can render after native device loss.

Not implemented through the product workflow: the viewport, four-layer renderer, live coastlines, undo UI and exporter do not yet all consume this same journal-backed document. The isolated proof must be carried into that connected workflow.

## Godot versus Rust + wgpu

| Concern | Godot 4 .NET evidence | Rust + wgpu implication |
| --- | --- | --- |
| Export and tile control | Halo-tiled JFA terrain produced validated 16K output with tile-local coverage; duration is not a gate | More explicit queues and staging, but no demonstrated user-visible need |
| Device loss | TDR made in-process recovery fail and eventually terminated Godot; fresh-process rendering and isolated acknowledged-command replay pass | Explicit lifecycle would not remove Windows reset behaviour or the need for durable CPU-authoritative state |
| UI, text and packaging | Godot supplies the editor UI, shaping, input and Windows packaging; Linux remains unvalidated | These integrations would need separate selection and maintenance |
| Tablet input | Not measured; remains a gate | Potentially stronger low-level access with platform-specific cost |
| Developer velocity | Working GPU/import/export and durability probes exist in C# | A rewrite currently buys control without evidence that it solves the open interaction question |

## Known limitations

- The connected four-layer editing workflow is not implemented or benchmarked. The previous responsiveness verdict is unchanged.
- The real 16K terrain fixture exports correctly with bounded working buffers, but progress and cancellation remain missing.
- River edits are document-owned and conservatively invalidated, but pointer handles and GPU tile rebuilding are not connected.
- The prototype store is a content-addressed manifest directory rather than the specified final SQLite transaction/history design.
- The original `.ink` is preserved, but recovery is visual plus provenance extraction rather than full semantic replay.
- No Inkarnate-hosted assets are included.
- Transparency ordering, physical tablet input and Linux validation remain open.

## Reproduction

Run `Scripts/run-spike.ps1` for streaming import, storage interruption, acknowledged-edit crash recovery, seams and baseline export probes. Run `Scripts/run-tdr-probe.ps1` only in a disposable session to reproduce the destructive Windows TDR characterization. Run `Scripts/run-interactive-probe.ps1` for the earlier 30-second global-GPU benchmark, and `Scripts/run-terrain-export-16k.ps1` for the full terrain/JFA 16K measurement. Evidence is written under `artifacts/spike/`.
