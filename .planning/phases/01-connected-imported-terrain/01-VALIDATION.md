---
phase: "1"
slug: "connected-imported-terrain"
status: complete
nyquist_compliant: true
wave_0_complete: true
created: "2026-09-22"
---

# Phase 1 — Validation Strategy

> Per-phase validation contract. The existing harness proves isolated components; Phase 1 must add connected, restart, export and hardware evidence.

## Test Infrastructure

| Property | Value |
|---|---|
| Framework | Existing .NET 8 console contract runner plus Godot runtime probes |
| Config file | `tests/Mapwright.ContractTests/Mapwright.ContractTests.csproj`, `Mapwright.csproj` |
| Quick run command | `./Scripts/run-contract-tests.ps1` |
| Current full probe | `./Scripts/run-spike.ps1 -InkFixture '<actual .ink path>'` — historical spike only; never claim Phase 1 acceptance from this alone |
| Phase 1 full gate | `./Scripts/run-phase1-acceptance.ps1 -Full` and `-VerifyReport`; 11 independent connected cases, validated 16K export, and twelve ≥60-second real-Vulkan scenario/cache rows |

## Sampling Rate

- After each domain, application or storage task: run `./Scripts/run-contract-tests.ps1` and require exit 0 with nonzero tests reported.
- After each renderer or UI wave: run the connected fixture for that wave plus the contract runner.
- Before phase verification: run the full connected suite, real `.ink` fixture, fresh-process recovery, validated 16K export and four warm/cold/evicted hardware scenarios.
- Do not substitute synthetic spike output, screenshots or inferred performance for connected evidence.

## Per-Requirement Verification Map

| Requirements | Test type and assertion | Existing evidence | Wave 0 need |
|---|---|---|---|
| DOC-01–03, IMPT-01–03 | Integration: both import modes, two-role mapping, source bytes/hash/transforms, bounded rejection, cache deletion/reopen | Import and blob prototypes only | Real and hostile `.ink` fixtures, connected reopen |
| LAYR-01, TERR-01–02, MASK-01, WATR-01, HIST-04 | Domain/property plus renderer: fixed roles, refused targets, deterministic resolved brush replay, mask/river ordering, D-28 scope cues | Partial four-layer contract fixture | Two-role fixture, deterministic brush oracle, cursor/state tests |
| HIST-01–02, DURA-01–02 | SQLite and child-process: commit-before-ack, cursor/restart/redo, older reconstruction cancel, failure injection | Basic commit/reopen only | History/blob interruption worker and crash fixture |
| EXPT-01, REND-01–03 | Connected GPU/reference: shared revision, linear-premultiplied colour/alpha, seamless tiled/reference ≤1 channel, frozen PNG and atomic publication | Synthetic seam/export probes | Shared-graph colour/seam oracle and real export fixture |
| UIIN-01, REND-04–05 | Connected UI and hardware: 100%/150% D-28 comprehension; 1920×1080, ≥60 seconds per scenario, sequence-correlated latency, resource ledger | No connected acceptance | Input-to-visible instrumentation and four-scenario runner |

## Wave 0 Requirements

- [x] Replace four-layer test fixture with exactly Background and Foreground, including Foreground-owned mask.
- [x] Add bounded import fixtures and source hash/provenance assertions.
- [x] Add deterministic texture/coverage/river reference cases.
- [x] Add fresh-process SQLite interruption and history-cursor test worker.
- [x] Add whole-versus-tiled colour/alpha oracle and sequence-to-visible instrumentation.

## Manual and Hardware Verification

| Behavior | Requirement | Why manual/hardware | Evidence |
|---|---|---|---|
| D-28 scope comprehension at 100% and 150% | MASK-01, UIIN-01 | Visual meaning across similar low-strength effects | Captured cursor/row states and reviewer answers for active tool, target and affected property |
| Four ≥60-second interaction scenarios at 1920×1080 | REND-04, REND-05 | Real device, scheduler and cache interactions | Hardware/driver record, sequence-correlated p50/p95/p99, sample counts, warm/cold/evicted runs and attributed stalls |
| Conditional fixed outer-coast style | MASK-01, REND-02–03 | Coast/river identity may be infeasible | Fixture and bounded resource evidence for styled branch, or recorded D-20 unstyled fallback and inapplicable distance checks |

## Validation Sign-Off

- [x] Every executable plan task has an automated check or explicit Wave 0 dependency.
- [x] No three consecutive implementation tasks lack automated feedback.
- [x] Full connected acceptance commands are written after their harnesses exist.
- [x] Full suite and manual/hardware gates are complete.
- [x] Set `nyquist_compliant: true` only after evidence is present.

**Approval:** passing `01-ACCEPTANCE.md` and `01-VERIFICATION.md`, 2026-09-23.
