# Mapwright phase design specifications

Date: 2026-09-22. Status: input briefs for a design agent; visual designs have not been produced or approved.

## Files to hand off

| Phase | Design specification | Main output |
| --- | --- | --- |
| 1 | [Connected Imported Terrain](01-connected-imported-terrain/01-DESIGN-SPEC.md) | Shared editor shell, control system, terrain/river editing and durable workflow states |
| 2 | [Recovered Assets and Stamps](02-recovered-assets-and-stamps/02-DESIGN-SPEC.md) | Asset browser, packs, replacement, transforms, ordering and scatter |
| 3 | [Water, Paths and Labels](03-water-paths-and-labels/03-DESIGN-SPEC.md) | Geometry editing, text/fonts, mixed object layers, notes and structured recovery |
| 4 | [Project Finishing and Safe Publication](04-project-finishing-and-safe-publication/04-DESIGN-SPEC.md) | Effects, grid, export settings, packaging, integration JSON and storage/history |
| 5 | [Windows and Linux Completion Workflow](05-windows-and-linux-completion-workflow/05-DESIGN-SPEC.md) | Integrated workflow, platform/input adaptations, feedback, recovery and usability review |

Each file includes product context, owned requirement IDs, screens, controls, state transitions, edge cases, creative decisions and an output checklist. It can be pasted into a design-agent task on its own. Repository links provide deeper context when the receiving agent has file access.

## Suggested handoff prompt

> Design the Mapwright phase described in the attached design specification. Follow its fixed product constraints, choose and explain the open visual/interaction decisions, and produce its requested frames, prototype flows, component specifications and coverage checklist. Identify assumptions explicitly. Write your response and assets beside the brief without overwriting it. The task is UI/UX design; do not claim the application has been implemented or its performance validated.

## Design order and shared decisions

Design Phase 1 first. It establishes editor regions, visual tokens, typography, icons, numeric controls, selection/focus conventions and the distinction between preview, commit and save. Phases 2–4 extend that system. Phase 5 reviews their integrated experience on both platforms.

User amendment, 2026-09-22: P0 terrain is exactly Background below Foreground; only Foreground owns the editable land/coastline mask. Rivers subtract from that mask to reveal Background. Phase 3 lake clarification uses ordinary Land subtraction or water texture/solid-colour painting on Foreground; dedicated spline-lake modifiers are deferred. Additional mixed object layers (stamps, paths and text) remain above the terrain pair. Do not design extra brush or independent mask layers. The phase briefs incorporate this decision; historical four-terrain-layer references are superseded. Completed discussion choices constrain later design proposals; remaining visual/interaction choices stay open.

When a previous design is unavailable, a later design agent can work from the standalone baseline in its brief. Label the shared shell and tokens provisional, list the assumptions and reconcile them with Phase 1 before implementation. Do not invent five different editor shells.

The roadmap's fixed requirements constrain function and acceptance. The briefs' suggested evaluation sizes, sample names and presentation options are design exercises, not new product requirements. Style, exact placement, dimensions, shortcut choices and unspecified defaults are for the design agent to propose. Inkarnate-like editing capabilities are required; an exact visual clone is not a requirement.

Coastline follow-up: Prefer one fixed outer-coast style with unstyled river/lake banks. If reliable separation is too involved, ship without generated edge styling; this fallback is authorized. Coastline style controls, decorative fades, wave rings and isolines are deferred beyond P0. Soft coverage, editable bank softness, colour/alpha and seam correctness remain required. Distance-field checks apply only where that path is used. REND-02/03 are rendering contracts, not UI controls.

## Deliverable convention

Inside each phase directory, the design agent should create `DESIGN-RESPONSE.md` and a `design-assets/` directory for editable frames, exports or an optional interactive prototype. Give every screen/state an ID and connect it to the requirement IDs in the brief. Record component dimensions, tokens, control behavior, units, focus/keyboard behavior and important user-facing copy. Choose a format the design agent can actually produce; a particular commercial design tool is not required.

Keep the input `NN-DESIGN-SPEC.md` intact. Returned designs remain proposed until reviewed. Creating these briefs does not complete a phase, a GSD implementation plan or an approved `UI-SPEC.md` contract.

Sources: [roadmap](../ROADMAP.md), [requirements](../REQUIREMENTS.md), [working specification](../../docs/spec.md), [architecture](../../docs/architecture.md).

Phase 3 discussion is complete: use [03-CONTEXT.md](03-water-paths-and-labels/03-CONTEXT.md) and its synchronized brief for side-panel label entry, three label modes, mixed object layers, note pins and initial-import-only comparison. Do not reinstate superseded freeform label handles, typed layers or later reconstruction wizards.

Phase 4 discussion is complete: [04-CONTEXT.md](04-project-finishing-and-safe-publication/04-CONTEXT.md) and its synchronized brief govern map units, independent resolution presets, topmost Grid, Appearance, no-history ZIP export, manual JSON and storage cleanup. Earlier map-pixel and automatic-JSON descriptions are superseded.
