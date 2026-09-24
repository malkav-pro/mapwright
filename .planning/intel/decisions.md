# Decisions

> Coastline follow-up (2026-09-22): mandatory P0 coast styling controls and decorative fade/ring/isoline effects below are superseded. Prefer a fixed outer-coast style with unstyled rivers/lakes, or use the authorized unstyled fallback if separation is too involved. Current requirements govern colour/seam checks and conditional distance-field acceptance.


> Later user amendment (2026-09-22): the connected product workload now uses exactly Background and Foreground, one Foreground land/coastline mask and object/path/text layers above both. Four-terrain-layer references below describe earlier targets or recorded probes. Follow [the working specification](../../docs/spec.md) and [current requirements](../REQUIREMENTS.md); the Godot and durability decisions remain settled.

## Engine decision: Godot 4 .NET
- source: docs/engine-decision.md
- status: proposed
- decision:
  DATA_61pkde6t_START
  Continue with Godot 4 .NET. Keep the document, history, storage and import models independent of Godot scene types, but do not maintain a parallel Rust + wgpu implementation.

  This decision selects the implementation stack; it does not declare Phase 0 complete. The connected four-layer editing workflow, ordering, tablet input and Linux validation remain acceptance work. Durable command replay has been demonstrated in isolation; it still must be exercised through the connected editor.
  DATA_61pkde6t_END
- scope: Godot 4 .NET, Rust + wgpu, device-loss recovery, command journal, GPU caches, connected editor, export

## Phase 0 spike report
- source: docs/spike-report.md
- status: proposed
- decision:
  DATA_e6be2ije_START
  Godot 4 .NET is final for the first implementation. The measured work does not justify the cost of moving to Rust + wgpu. This is an engine decision, not a declaration that all Phase 0 acceptance work is complete.

  Device loss is a save-and-restart condition on the tested Windows/AMD configuration. A Windows TDR left Godot unable to recreate a usable Vulkan device in-process and cleanup eventually terminated the child natively. Recovery therefore cannot depend on post-loss managed code or an orderly shutdown. Commands must reach durable storage before the UI acknowledges them; a fresh process replays those commands and rebuilds disposable GPU state.

  The decisive interaction milestone is still open. Existing interaction evidence covers one persistent 1,024 px coverage texture with a simple colour pass for 30 seconds. It does not establish responsiveness for the real imported map, four visible terrain layers, live coastline updates, river editing, visible undo or cold/evicted caches.
  DATA_e6be2ije_END
- scope: Godot 4 .NET, Rust + wgpu, terrain renderer, Vulkan device loss, command journal, Inkarnate import, terrain export, river editing, undo, viewport responsiveness
