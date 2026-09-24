## Conflict Detection Report

### BLOCKERS (0)

None.

### WARNINGS (0)

None.

### INFO (5)

[INFO] Auto-resolved: Engine selection replaces prototype-default status
  Found: The archived specification treats Godot as a prototype default to confirm/reject after Phase 0.
  Note: ADR precedence selects Godot 4 .NET for the first implementation without a parallel Rust + wgpu stack. The engine ADR and spike decision explicitly keep remaining acceptance work open; the working specification incorporates this decision. Final does not satisfy the classifier's literal Accepted lock test, so locked=false does not reopen the choice.
  source: docs/reference/map-editor-spec-final.md §§1,13; docs/engine-decision.md, Decision; docs/spike-report.md, Decision; docs/spec.md §1; docs/README.md, Later decisions retained

[INFO] Auto-resolved: Export duration target explicitly superseded
  Found: The archived specification provisionally gates the 16K fixture at 120 seconds.
  Note: The working specification requires correct validated output, bounded memory, progress, cancellation and destination preservation; duration is observational and roughly five minutes is acceptable. The README explicitly records supersession and the working specification identifies itself as incorporating later decisions. This is an explicit source relationship, not selection by date or filename.
  source: docs/reference/map-editor-spec-final.md §13; docs/spec.md §§1,7,13; docs/README.md, Specification provenance and Later decisions retained

[INFO] Auto-resolved: Fresh-process recovery replaces orderly post-loss recovery
  Found: The archived device-loss text permits in-process recovery or saving pending commands before orderly restart.
  Note: The higher-precedence engine ADR requires persist-before-acknowledgement and fresh-process recovery because native termination is possible. Working spec and architecture agree; no pending-command save or orderly cleanup after device loss is required for correctness. The active unacknowledged gesture may be lost.
  source: docs/reference/map-editor-spec-final.md §8; docs/engine-decision.md, Device-loss contract; docs/spike-report.md, Required versus implemented recovery; docs/spec.md §8; docs/architecture.md §§4,8; docs/README.md, Later decisions retained

[INFO] Auto-resolved: Interaction scenarios and thresholds explicitly superseded
  Found: The archive requires a 30-second brush scenario and viewport p95 frame time at most 33.3 ms.
  Note: The current working spec and spike's acceptance milestone require at least 60-second warm/cold/evicted scenarios, sequence-correlated input-to-visible p95/p99 at most 50/100 ms and frame intervals at most 20/33.3 ms. README explicitly identifies the replacement. Earlier one-texture throughput is evidence only and cannot satisfy the current gate.
  source: docs/reference/map-editor-spec-final.md §13; docs/spec.md §13; docs/spike-report.md, Next acceptance milestone and Measured results; docs/README.md, Later decisions retained

[INFO] Auto-resolved: Connected document workflow replaces isolated-prototype milestone
  Found: The archived prototype description covers individual terrain/import/persistence capabilities and one texture brush.
  Note: The engine ADR, architecture and working spec require the real map with four layers, painting, river edits, undo/redo, viewport, save/reopen and export over one authoritative state. README records this supersession. Spike results remain historical evidence; its JSONL/manifest store is not the production SQLite design and isolated replay is not connected-editor completion.
  source: docs/reference/map-editor-spec-final.md §13; docs/engine-decision.md, Interaction milestone; docs/spec.md §13; docs/architecture.md §§1,4,7,12; docs/spike-report.md, Known limitations; docs/README.md, Later decisions retained
