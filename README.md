# Malkav's Mapwright

Mapwright is a local-first native fantasy-map editor prototype for Windows and Linux. The Phase 0 spike validates `.ink` recovery, bounded GPU rendering, tiled terrain and mask processing, editable water modifiers, durable storage, and seamless 16K export.

## Pinned development toolchain

- Godot 4.7.2 .NET
- .NET SDK 8.0.425
- C# 12 / .NET 8

Portable tool binaries live in `.tools/` and are ignored by Git.

## Project documentation

- [Documentation index](docs/README.md) — reading order, specification provenance and later decisions.
- [Working specification](docs/spec.md) — product scope, P0/P1/P2 priorities and acceptance targets.
- [Architecture](docs/architecture.md) — accepted implementation boundaries and the first connected editing slice.
- [Engine decision](docs/engine-decision.md) — Godot selected for the first implementation and the restart recovery contract.
- [Original final specification](docs/reference/map-editor-spec-final.md) — preserved 2026-09-21 source supplied for onboarding.
- [Codebase map](.planning/codebase/ARCHITECTURE.md) — observed implementation, with stack, structure, conventions, testing, integrations and concerns alongside it.

## Current spike milestone

- Native Godot project and C# shell
- GPU compute availability probe through `RenderingDevice`
- Persistent soft-brush coverage and a cross-tile river fixture
- Halo-tiled JFA coastline/wave rendering with 0.124 px maximum error against the discrete pixel-centre oracle
- Bit-exact tiled-versus-whole terrain output for the implemented pipeline
- Global-rendering-device brush surface displayed directly in the viewport without CPU readback
- 30-second 120 Hz brush-submission and monotonic frame-interval benchmark (presentation latency remains unproven)
- Streaming, tiled 16K PNG export with full CRC and decompression validation
- `.ink` v3 gzip/JSON inspection
- Embedded preview and raster checkpoint recovery
- Explicit unresolved-asset report
- Immutable storage of the original `.ink` and recovered rasters
- Atomic directory publication, hash-verified reopen, cache deletion, exception injection, and forced-process-termination tests
- Validate-before-replace export publication that preserves an existing destination on failure

See the [spike report](docs/spike-report.md) for measurements, gate status and limitations, and the [engine decision](docs/engine-decision.md) for the selected stack. Run the Windows spike harnesses with:

```powershell
.\Scripts\run-spike.ps1
.\Scripts\run-interactive-probe.ps1
.\Scripts\run-terrain-export-16k.ps1
```

Godot is selected for the first implementation. Phase 0 acceptance remains incomplete, including the connected editing workflow and Linux validation.
