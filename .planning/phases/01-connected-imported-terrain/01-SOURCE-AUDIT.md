# Phase 1 multi-source coverage audit

Status: all in-scope source items are assigned to executable plans. This audit records planned coverage, not implementation or measured acceptance.

## Goal and requirement coverage

| Source | Item | Plan(s) | Status |
|---|---|---|---|
| GOAL | Recover real map; edit two terrain textures, Foreground mask and river; undo/redo; durable reopen; same-revision viewport/16K PNG within gates | 01–10 | COVERED |
| REQ | DOC-01 | 01, 02, 03, 08, 10 | COVERED |
| REQ | DOC-02 | 01, 05, 07, 08, 10 | COVERED |
| REQ | DOC-03 | 02, 06, 07, 10 | COVERED |
| REQ | IMPT-01 | 01, 02, 08, 10 | COVERED |
| REQ | IMPT-02 | 02, 08, 10 | COVERED |
| REQ | IMPT-03 | 02, 08, 10 | COVERED |
| REQ | LAYR-01 | 01, 03, 08, 09, 10 | COVERED |
| REQ | TERR-01 | 01, 03, 09, 10 | COVERED |
| REQ | TERR-02 | 03, 06, 09, 10 | COVERED |
| REQ | MASK-01 | 04, 06, 09, 10 | COVERED |
| REQ | WATR-01 | 04, 06, 09, 10 | COVERED |
| REQ | HIST-01 | 05, 08, 09, 10 | COVERED |
| REQ | HIST-02 | 05, 08, 09, 10 | COVERED |
| REQ | HIST-04 | 03, 04, 05, 07, 10 | COVERED |
| REQ | EXPT-01 | 01, 07, 08, 10 | COVERED |
| REQ | UIIN-01 | 08, 09, 10 | COVERED |
| REQ | DURA-01 | 01, 05, 07, 10 | COVERED |
| REQ | DURA-02 | 07, 08, 10 | COVERED |
| REQ | REND-01 | 01, 04, 06, 07, 10 | COVERED |
| REQ | REND-02 | 06, 10 | COVERED |
| REQ | REND-03 | 06, 07, 10 | COVERED |
| REQ | REND-04 | 10 | COVERED |
| REQ | REND-05 | 06, 07, 08, 10 | COVERED |

## Locked decision coverage

Every ID below occurs in a task `<action>`, with implementation and verification details in the named plan.

| Source | Item | Plan(s) | Status |
|---|---|---|---|
| CONTEXT | D-01 recovery preview/review | 01, 02, 08 | COVERED |
| CONTEXT | D-02 unselected two-mode choice | 01, 02, 08 | COVERED |
| CONTEXT | D-03 configured projects folder/override | 02, 08 | COVERED |
| CONTEXT | D-04 recent-project entry | 08 | COVERED |
| CONTEXT | D-05 source/extras/flattened mapping | 01, 02, 08 | COVERED |
| CONTEXT | D-06 fixed two terrain roles | 01, 03, 08 | COVERED |
| CONTEXT | D-07 Foreground-only mask | 01, 03, 04, 06 | COVERED |
| CONTEXT | D-08 separate texture/coverage/water | 03, 04, 06 | COVERED |
| CONTEXT | D-09 auto mask targeting and restore | 09 | COVERED |
| CONTEXT | D-10 blocked-target actions | 03, 09 | COVERED |
| CONTEXT | D-11 shared Texture Brush settings | 03, 09 | COVERED |
| CONTEXT | D-12 diameter in document pixels | 03, 09 | COVERED |
| CONTEXT | D-13 pan/zoom and focus | 09 | COVERED |
| CONTEXT | D-14 two Land modes | 04, 09 | COVERED |
| CONTEXT | D-15 geometric corner Smooth | 04, 09 | COVERED |
| CONTEXT | D-16 texture preset vocabulary | 03, 09 | COVERED |
| CONTEXT | D-17 mouse taper | 03, 09 | COVERED |
| CONTEXT | D-18 resolved recipe/controls | 03, 09 | COVERED |
| CONTEXT | D-19 fixed outer coast only if proven | 06 | COVERED |
| CONTEXT | D-20 authorized unstyled branch | 06, 10 | COVERED |
| CONTEXT | D-21 bank softness/mouth/tile cases | 04, 06 | COVERED |
| CONTEXT | D-22 colour/seam and conditional distance | 06, 10 | COVERED |
| CONTEXT | D-23 visible undo/redo and History panel | 05, 08 | COVERED |
| CONTEXT | D-24 whole-gesture cancellation | 09 | COVERED |
| CONTEXT | D-25 older replay and pan/zoom | 05, 09 | COVERED |
| CONTEXT | D-26 non-blocking recovery banner | 07, 08 | COVERED |
| CONTEXT | D-27 durability/frozen export | 01, 05, 07, 08 | COVERED |
| CONTEXT | D-28 three-way immediate scope cues at 100%/150% | 08, 09, 10 | COVERED |

## Research and approved UI contract coverage

| Source | Item | Plan(s) | Status |
|---|---|---|---|
| RESEARCH | Real .ink source-to-role/version/transform inspection and replay cutoff | 02 | COVERED |
| RESEARCH | One immutable aggregate, command validation and inward tier boundaries | 01, 03, 04 | COVERED |
| RESEARCH | SHA-256 staged source/blob publication before SQLite WAL FULL ack | 01, 02, 05, 07 | COVERED |
| RESEARCH | Versioned schema, cursor/redo/checkpoint/reconstruction and cache independence | 01, 05 | COVERED |
| RESEARCH | Bounded decompression/JSON/base64/raster allocation | 02 | COVERED |
| RESEARCH | Resolved brush recipe, deterministic document-space anchoring/taper | 03, 06 | COVERED |
| RESEARCH | Transient gesture state and focus/cancel semantics | 09 | COVERED |
| RESEARCH | Single viewport/export graph, bounded tile residency and invalidation | 01, 06, 07 | COVERED |
| RESEARCH | Colour/soft alpha, small tiled/reference ≤1 channel | 06 | COVERED |
| RESEARCH | Bounded coast/mouth feasibility; D-20 fallback if unproven | 06, 10 | COVERED |
| RESEARCH | Frozen-revision streaming PNG validation/atomic publish/cancel | 07 | COVERED |
| RESEARCH | Fresh-process device/crash recovery and WAL/blob checks | 07 | COVERED |
| RESEARCH | Hardware resource ledger and correlated four-scenario acceptance | 06, 10 | COVERED |
| RESEARCH | Empty Background and solo semantics resolved in implementation/tests | 03, 06 | COVERED |
| RESEARCH | No new package or external service | all | COVERED; package legitimacy gate not triggered |
| UI-SPEC | Offline Godot Theme/fonts/icons/tokens/shared states | 08 | COVERED |
| UI-SPEC | Recent projects, two-card recovery, reports and exceptional import copy | 08 | COVERED |
| UI-SPEC | Title/command/tool/canvas/layers/history/status shell and compression | 08 | COVERED |
| UI-SPEC | Fixed two terrain rows, no grid/future controls | 08 | COVERED |
| UI-SPEC | Texture Brush, Land, River and target-specific controls | 09 | COVERED |
| UI-SPEC | D-28 spatial semantics, text/focus at 100%/150% | 09, 10 | COVERED |
| UI-SPEC | Numeric/shortcut/gesture/temporary pan contracts | 09 | COVERED |
| UI-SPEC | Honest save/older-history/export/recovery/job states | 08, 09 | COVERED |
| UI-SPEC | 1920×1080, 1366×768 and 150% verification | 08, 09, 10 | COVERED |
| DESIGN | 17 mockup frames and response as provenance, subordinate to UI-SPEC | 08, 09 | COVERED |

The final acceptance in plan 10 must reconcile each of the 17 `design/README.md` frames individually as compared, explicitly out-of-scope, or superseded, with applicable state/flow evidence and the governing UI-SPEC/decision reason. The visual plans provide implementation coverage; the final per-frame table prevents an unreviewed frame from being counted by association.

## Research open-question disposition

These are execution decision gates, not claims that the answers are known before implementation. Each owning task has a typed `<decision_gate>` ahead of dependent work, an explicit decision artifact, measurable inputs and options/fallback, and an automated verification that rejects an absent record. The selected option and evidence must appear in the artifact and plan SUMMARY, then be checked again in plan 10 acceptance where it affects a gate.

| Research question | Resolution owner and required evidence | Status |
|---|---|---|
| 1. Real .ink raster checkpoints, role mapping, transforms and dimensions | Q1 gate in Plan 02 precedes editable import; actual fixture IDs/order/dimensions/transforms, preview correspondence, trusted mapping versus partial/flattened fallback, and rejected candidates go to `01-Q1-DECISION.md`. | GATED; source semantics remain unverified |
| 2. Brush ranges/defaults, preset inventory and taper curve | Q2 gate in Plan 03 precedes recipe persistence; reference/renderer/replay tests compare candidate bounds, assets and curves, with choice and rejected candidates in `01-Q2-DECISION.md`. | GATED; values remain unselected |
| 3. Empty Background appearance and solo semantics | Q3 solo gate in Plan 03 records a domain truth table; Q3 underlay gate in Plan 06 compares transparent/project-colour viewport/export pixels. Both precede their dependent implementation and write `01-Q3-DECISION.md`. | GATED; behavior remains unselected |
| 4. Compatibility version and checkpoint cadence | Q4 gate in Plan 05 measures real-map replay/cache/cancellation before choosing full snapshots or periodic checkpoints plus replay; version policy and evidence go to `01-Q4-DECISION.md`. | GATED; cadence remains unmeasured |
| 5. Coast identity at mouths and tile boundaries | Q5 gate in Plan 06 precedes style implementation; real/synthetic fixtures and budget/contour measurements select proven fixed style or D-20 unstyled fallback in `01-Q5-DECISION.md`. | GATED; branch remains unproven |

## Spec-less edge probe: 45 input items → 45 plan destinations

Each classified item below is an individual `must_haves.truths` entry in the named plan. A flat `verification: backstop` marker is present where the predicate cannot be fully specified from source. Each unclassified item is a separate `FLAGGED UNRESOLVED` assumption; none is dismissed.

| Requirement | Probe category → plan destination |
|---|---|
| DOC-01 | unclassified → 01 assumption |
| DOC-02 | idempotency → 01 truth; concurrency → 01 truth |
| DOC-03 | adjacency → 02 truth; empty → 02 truth; ordering → 02 truth; idempotency → 02 truth; concurrency → 02 truth |
| IMPT-01 | unclassified → 01 assumption |
| IMPT-02 | adjacency → 02 truth; empty → 02 truth; ordering → 02 truth |
| IMPT-03 | unclassified → 02 assumption |
| LAYR-01 | idempotency → 03 truth; concurrency → 03 truth |
| TERR-01 | unclassified → 03 assumption |
| TERR-02 | unclassified → 03 assumption |
| MASK-01 | boundary → 04 truth; precision → 04 truth |
| WATR-01 | boundary → 04 truth; precision → 04 truth |
| HIST-01 | unclassified → 05 assumption |
| HIST-02 | unclassified → 05 assumption |
| HIST-04 | unclassified → 05 assumption |
| EXPT-01 | unclassified → 07 assumption |
| UIIN-01 | idempotency → 08 truth; concurrency → 08 truth |
| DURA-01 | adjacency → 05 truth; empty → 05 truth; ordering → 05 truth |
| DURA-02 | adjacency → 07 truth; empty → 07 truth; ordering → 07 truth; idempotency → 07 truth; concurrency → 07 truth |
| REND-01 | empty → 06 truth; encoding → 06 truth |
| REND-02 | unclassified → 06 assumption |
| REND-03 | boundary → 06 truth; precision → 06 truth |
| REND-04 | adjacency → 10 truth; empty → 10 truth; ordering → 10 truth |
| REND-05 | boundary → 06 truth; precision → 06 truth |

Count: 35 classified truths + 10 unclassified flagged assumptions = 45 report items. The `EDGE ID:category` and `FLAGGED UNRESOLVED ID unclassified` markers make the equality check deterministic.

## Scope and capability decisions

- Excluded by phase assignment: stamps/assets, lakes/paths/text, filters/grid/DPI/package/JSON integration and physical-tablet/Linux whole-product acceptance belong to Phases 2–5.
- Excluded by explicit deferral: additional terrain/mask layers, pressure, custom tip import, coastline controls, decorative fades/rings/isolines. The earlier DESIGN-RESPONSE grid/coast rows and stale DESIGN-SPEC wording do not override CONTEXT/REQUIREMENTS/approved UI-SPEC.
- The primary noun is **one authoritative project document/revision**. Recovery mode is a choice of initial base. Plan 01 promotes EditSession/SQLite as authority while preserving original .ink bytes and reading legacy ProjectStore input alongside it. No separate visual-mode document is created.
- The external API detector has no service integration here; local .ink parsing is a file-format trust boundary. No COVERAGE.md applies. A TypeScript ORM schema-push check does not apply to this Godot/C#/SQLite project.
