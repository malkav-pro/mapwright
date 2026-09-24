# Engine decision: Godot 4 .NET

Date: 2026-09-22
Status: Final for the first implementation

## Decision

Continue with Godot 4 .NET. Keep the document, history, storage and import models independent of Godot scene types, but do not maintain a parallel Rust + wgpu implementation.

This decision selects the implementation stack; it does not declare Phase 0 complete. The connected Background/Foreground editing workflow, ordering, tablet input and Linux validation remain acceptance work. Durable command replay has been demonstrated in isolation; it still must be exercised through the connected editor. The 2026-09-22 user terrain-model amendment supersedes the four-layer product workload; recorded four-layer probe results below remain historical evidence.

## Evidence

- The global `RenderingDevice` can update a persistent GPU texture that Godot displays directly without routine CPU readback.
- Halo-tiled coverage, JFA distance, coastline effects and a cross-tile river matched the whole-image fixture exactly for the implemented pipeline.
- The real terrain/JFA pipeline generated and fully validated a 16K PNG with tile-local coverage and bounded working buffers. Duration is observational, not a gate.
- The `.ink` importer now streams JSON incrementally. On the real backup it retained 74 MB of decoded imagery, used a 64 MB maximum token buffer, reproduced all prior counts, and peaked at 855 MB including a 465 MB Godot/Vulkan baseline.
- On the AMD Radeon RX 7800 XT, an intentionally non-terminating compute shader triggered Windows TDR. `RenderingDevice.Sync()` returned, but Godot could not create a fresh local Vulkan device in-process. Cleanup subsequently terminated the child with a native exception. A new Godot process immediately created a device and passed the complete JFA seam probe.
- A process-kill recovery probe persisted each edit before acknowledgement, then forcibly terminated separate workers during editing and after export temporary-file creation. Restart replay recovered four layer strokes plus river point/width edits, preserved the previous destination and removed the orphan partial output.

## Device-loss contract

Godot device loss is a **save-and-restart** condition on the tested Windows/AMD configuration. In-process recovery is not supported by this application.

- CPU document state and the on-disk command journal are authoritative.
- A completed command is persisted before the UI acknowledges it as committed.
- GPU tiles, textures, distance fields and compositor resources are disposable caches.
- On detected submission/device failure, stop accepting GPU jobs and avoid cleanup paths that assume valid Vulkan handles.
- The in-flight gesture may be discarded; acknowledged commands and saves must survive.
- Relaunch the application, reopen the last acknowledged revision and rebuild GPU caches.
- Export writes to a temporary sibling, so device loss cannot replace the previous destination with a partial file.

The observed process could execute managed code after `Sync()` returned, but it could not restore rendering and later died during native device cleanup. That is not in-process recoverability. Recovery comes from work made durable before acknowledgement, not from code that might run after device loss.

## Interaction milestone

The next engineering milestone is the connected workflow defined in the specification: the real imported map, Background and Foreground, texture painting on both, Foreground coastline mask/effects and an editable river, with painting, river edits, undo/redo, viewport updates and export consuming the same document state.

If an interaction target fails, classify the cause as algorithm, scheduling, allocation, synchronization or an engine constraint before reconsidering the stack. A stack change is justified only by a demonstrated engine constraint that cannot be contained behind the renderer boundary.

## Why not Rust + wgpu now

Rust + wgpu would provide more explicit device and queue control, but would not remove Windows device reset behaviour or the need for CPU-authoritative recovery. It would also require replacing Godot's UI, input, text, windowing and packaging integration. Current evidence does not justify that cost.
