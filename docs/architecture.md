# Mapwright architecture

Status: accepted for the first implementation  
Date: 2026-09-22

## 1. Purpose

The Phase 0 code is an evidence harness. It established GPU, import, export, seam, memory and failure behaviour, but its types are not the product foundation. Production work starts from the boundaries in this document. Spike code remains reproducible until equivalent acceptance tests exist, then moves out of the shipping build.

The first vertical slice is deliberately narrow: open the recovered map, display Background below Foreground, texture-paint both, edit Foreground coverage and one river, undo/redo, persist every acknowledged edit, rebuild dirty coastline tiles, and export the same revision. The 2026-09-22 user amendment replaces the former four-terrain-layer workload; other acceptance limits remain unchanged.

## 2. Dependency rule

Dependencies point inward:

```text
Mapwright.Godot  --->  Mapwright.Application  --->  Mapwright.Domain
        |                       ^
        +--> Mapwright.Infrastructure --------+
        +--> Mapwright.Rendering.Godot -------+
```

- `Mapwright.Domain`: deterministic map state, value objects, commands and invalidation. No Godot, filesystem, JSON, SQL, threading or GPU types.
- `Mapwright.Application`: use cases and orchestration. Defines ports for transactions, blobs, clocks, rendering, export and telemetry. It decides when an edit is acknowledged.
- `Mapwright.Infrastructure`: SQLite project store, content-addressed blobs, `.ink` import, recovery and PNG publication. Implements application ports; it does not reference Godot.
- `Mapwright.Rendering.Godot`: tile scheduler, GPU resources, shaders and viewport/export rendering. GPU state is always reconstructible from a frozen domain revision.
- `Mapwright.Godot`: composition root and UI. Converts Godot input/events to application requests and renders view models. It contains no document rules or persistence protocol.
- `Mapwright.Spike`: destructive hardware probes and historical acceptance fixtures. It is not referenced by the shipping application.

No layer may pass `Godot.Vector2`, `Rid`, scene nodes, raw SQL rows or storage DTOs into the domain.

## 3. Authoritative state

`MapProject` is the aggregate root. A committed `ProjectRevision` identifies one immutable logical state. Stable ids identify maps, layers, strokes, rivers and assets.

The first slice uses these domain concepts:

- `MapProject`: project/map ids, title, document extent, ordered layers, active revision and history cursor.
- `TerrainLayer`: exactly one Background and one Foreground, in fixed order, with visibility, lock state, opacity, persistent colour bases and ordered texture strokes. Only Foreground owns land coverage, mask strokes, water modifiers and coastline style. Additional mixed object layers (stamps, paths and text) remain above the pair. Domain commands validate fixed terrain roles/order; imported source data does not create extra editable terrain layers.
- `PaintStroke`: document-space sampled path, resolved brush parameters, immutable texture reference or persisted solid-colour source, seed and algorithm version.
- `River`: centreline points, width and Foreground target; its coverage subtracts from the Foreground mask before coastline distance is built, revealing Background. Lakes instead use ordinary Land-mask subtraction or Foreground water texture/solid-colour paint commands (Phase 3 amendment); there is no required P0 spline-lake domain entity.
- `ObjectLayer` (Phase 3): mixed ordered stamp/path/text entities above terrain; initialize one for every map and prevent deletion of the last. Per-tool UI settings are independent; stored entity styles/geometry remain document authority.
- Label geometry (Phase 3): stored Straight/Curve/S-shape mode and signed deflection parameters; shared shaping/placement across viewport and export. Freeform label handles are not required.
- `EditCommand`: immutable payload with command id, base revision and affected document bounds.
- `DocumentChange`: result of applying a command, including the next state and conservative invalidation.

Commands are the semantic history. Rendered tiles, distance fields, mipmaps and compressed undo deltas are acceleration data only.

The Phase 4 discussion fixes the longest map edge at 1,000 map units, independent of initial grid counts and editing/export resolution. Phase 1.1 owns normalization and legacy compatibility. Source-to-map and map-to-raster transforms must remain explicit; map-relative widths/effects use map units and screen-space UI uses pixels.

## 4. Edit and acknowledgement protocol

The UI never mutates the document directly.

```text
input sample -> gesture builder -> immutable command
             -> validate against revision
             -> commit transaction durably
             -> publish new in-memory snapshot
             -> acknowledge command id/revision to UI
             -> enqueue invalidated tiles
```

The repository transaction appends the command, writes reversible data, updates materialised state/history cursor and advances the revision in one SQLite transaction. P0 uses WAL and `synchronous=FULL`. Acknowledgement occurs only after commit succeeds. GPU submission happens after acknowledgement and can be repeated.

An active gesture is a preview until committed. Preview rendering may use transient GPU data but cannot become authoritative. A native renderer crash can lose the preview, never an acknowledged command.

Undo and redo are application use cases that commit a new cursor/revision transition before visible tile restoration. New edits after undo invalidate the redo branch transactionally.

## 5. Snapshots and concurrency

Application services expose immutable `DocumentSnapshot` instances. A render job captures exactly one revision. Jobs never read a mutating object graph.

- The UI thread creates requests and consumes results.
- A single ordered commit queue serialises document revisions.
- The tile scheduler coalesces invalidation by revision and tile key.
- Worker queues are bounded. Interactive work has priority over export and cache generation.
- Export freezes a revision and asset set; edits may continue against later revisions.
- Cancellation is cooperative outside GPU dispatches and checked between bounded jobs.

## 6. Tile rendering

Residency tiles start at 512 x 512 raster pixels at the selected sampling level, mapped explicitly into document coordinates; 512 pixels is not 512 map units. A tile key contains map, revision, layer/composite target, scale level, coordinates and renderer compatibility version.

The dependency graph for the first slice is:

```text
Foreground coverage base + mask strokes + river geometry
    -> Foreground coverage tile with halo
    -> final soft coverage
    -> optional fixed outer-coast treatment, excluding river/lake banks
       (distance/provenance passes only if this path is used)
    -> Background colour + masked Foreground colour + fixed coast treatment if used
    -> ordered mixed object layers (stamps, paths and text) above terrain (as introduced)
    -> viewport tile or export band
```

Invalidation is expressed in document bounds plus downstream effect reach, then converted to tile keys by the scheduler. The renderer never invents invalidation rules.

Interactive rendering uses the global Godot `RenderingDevice` and directly displayable textures. Normal interaction performs no correctness readback. Export may use an isolated local device and staged asynchronous readback where available.

First-implementation styling amendment (2026-09-22): Prefer one fixed outer-coast style with unstyled river/lake banks. If reliable separation is too involved, ship without generated edge styling; this fallback is authorized. Coastline style controls, decorative fades, wave rings and isolines are deferred beyond P0. Soft coverage, editable bank softness, colour/alpha and seam correctness remain required. Distance-field checks apply only where that path is used. The current probe combines island and river coverage before applying styling, so it is not evidence of coast-only decoration. Keeping native water geometry separate offers an implementation route, but mouths, imported ambiguity and tile crossings need verification. If separation is too involved, omit generated styling rather than add classification controls or extra layers.

## 7. Storage layout

```text
project.mapwright/
  scene.sqlite
  blobs/sha256
  cache/tiles/
  cache/history/
  imports/
  manifest.json
```

SQLite is authoritative for revisions, commands, history cursor, materialised entities and blob references. `blobs/` contains immutable source/import/asset bytes addressed by SHA-256. Cache deletion must not affect reopen or export correctness.

The JSONL journal used by the spike is not the product store. It remains only as evidence for the persist-before-acknowledgement contract until the SQLite adapter and equivalent kill tests replace it.

## 8. Failure model

- Validation or disk failure: do not advance or acknowledge the revision.
- Renderer/device loss: stop scheduling; assume native process death is possible; recover in a fresh process from the last committed revision.
- Export failure/cancellation: delete the temporary sibling and preserve the previous destination.
- Cache corruption: discard and rebuild.
- Missing/corrupt authoritative blob: fail open with a precise recovery report; never silently substitute a cache.
- Unknown imported state-changing command: stop trusted replay for affected content and keep the baked fallback.

## 9. Observability

Every input gesture, command, revision, tile job and presented update carries correlation ids. Production telemetry is local and opt-in to files under the project diagnostics directory.

Interaction measurements record generated, processed, coalesced and dropped samples; commit latency; queue depth; input-to-rendered-update latency; monotonic frame intervals; stalls over 100 ms with attributed phase; RAM; and explicit GPU allocations. A frame callback alone is not presentation proof and is labelled accordingly.

## 10. Testing strategy

- Domain tests: command determinism, invalidation, undo/redo branching and serialization-independent equality.
- Application tests: persist-before-acknowledgement ordering, render scheduling after commit, revision conflicts, export snapshot isolation and cancellation.
- Infrastructure tests: SQLite crash boundaries, schema migration, blob integrity, disk-full simulation, import bounds and atomic export publication.
- Renderer fixtures: tiled-versus-whole output, distance tolerance, effect dependencies, transparency ordering and resource budgets.
- Hardware acceptance: 60-second warm/cold/eviction scenarios on the connected Background/Foreground workflow, TDR characterization, physical tablet input, Windows and Linux. Preserve the specified latency, memory and correctness limits.

Tests use fakes only at declared ports. They do not reach into Godot nodes to verify domain behaviour.

## 11. Migration plan

1. Create the domain and application projects with dependency tests and the edit/revision contract.
2. Implement the SQLite/blob repository behind application ports and repeat forced-process recovery tests.
3. Build the bounded tile scheduler and Godot renderer adapter around immutable snapshots.
4. Replace `MapCanvas` with a controller/view split using the connected document.
5. Connect fixed Background/Foreground terrain, texture painting, Foreground mask/river editing, undo/redo and save/reopen.
6. Route export through the same snapshot and render graph.
7. Run interaction acceptance scenarios and fix measured causes.
8. Move remaining probes into `Mapwright.Spike`; delete superseded runtime types only after their replacement tests pass.

At no point will spike types be wrapped and renamed as production abstractions. Behaviour is reimplemented from the specification and retained evidence.

## 12. First vertical-slice completion criteria

- The real imported project opens through the application service.
- Background and Foreground, Foreground's single coastline mask and one river live in one authoritative snapshot. Objects added by later slices composite above the terrain pair.
- Paint, river edit, undo and redo each commit durably before acknowledgement.
- Each command produces correct affected bounds and schedules only intersecting dependency tiles.
- Viewport and export consume the same frozen revision and renderer graph.
- Restart after forced termination recovers every acknowledged command.
- The specified interaction scenarios produce p50/p95/p99, queue, stall and memory evidence.
- The shipping project has no dependency on `Mapwright.Spike`.

## Phase 4 publication refinements

Grid is a default, initially hidden row fixed above terrain and all objects. Export uses its inclusion flag separately from editor visibility. Appearance controls are a dedicated toolbar panel.

Portable ZIP export contains current editable state and its required references without inherited undo/redo. Construct and validate a current-state baseline in an exported copy; never prune the working project as a packaging side effect. Native strokes/commands required for current content remain authoritative even when historical undo navigation is omitted. Manual JSON export includes all entities/notes/markers and visibility, with a source revision; normal save does not refresh a companion JSON. All output default filenames include map name and date/time.
