# Phase 5 design specification: Windows and Linux Completion Workflow

Date: 2026-09-22. Status: ready for a design agent. This is an input brief; visual choices remain proposed until reviewed.

## Assignment and standalone context

Design and review the complete user experience of Malkav's Mapwright across Windows and Linux. This is a single-user offline native Godot/C# editor whose success is finishing an existing Inkarnate world map and publishing a seamless 16K PNG safely. This phase integrates and validates the earlier designs; it must not redefine their product scope or declare untested performance acceptable.

The editor must offer texture painting on exactly Background and Foreground, one Foreground land/coastline mask, editable rivers affecting that mask, lakes via Land subtraction or Foreground water texture/colour painting, managed assets and packs, ordered stamps and scatter, path tools, font controls and straight/curved labels, notes, grid/effects, durable undo/save and image/project/JSON publication. Background/Foreground order is fixed. Mixed object layers (stamps, paths and text) remain above both and support reordering; layer opacity/visibility/lock/solo remain available. The inherited shell has a map canvas, tool selector, contextual inspector, asset/layer panels, navigation and operation status. Use earlier phase designs when available; when absent, document the provisional common shell and which integration checks await those designs.

**Owned requirement IDs:** UIIN-02, UIIN-03, ACPT-01, ACPT-02.

Earlier-phase requirements are regression obligations here, not reassigned work. The design deliverable is an integrated flow, platform/input specifications and a review/test protocol. Real hardware and implementation evidence are required to mark product acceptance passed.

## Required surfaces and artifacts

Prefer one fixed outer-coast style with unstyled river/lake banks. If reliable separation is too involved, ship without generated edge styling; this fallback is authorized. Coastline style controls, decorative fades, wave rings and isolines are deferred beyond P0. Soft coverage, editable bank softness, colour/alpha and seam correctness remain required. Distance-field checks apply only where that path is used.

| Screen ID / surface | Required controls and content | Requirement coverage |
| --- | --- | --- |
| P5-01 Complete editor reference frames | Windows and Linux compositions with terrain, stamps, river/lake, paths and labels; consistent tool/layer/selection context, panel resizing, navigation and save status | ACPT-01, ACPT-02 |
| P5-02 Input/focus specification | Mouse and physical tablet-as-pointer behavior; keyboard navigation, text/numeric focus, canvas shortcuts, modal behavior, pointer capture and focus-loss outcomes | UIIN-02 |
| P5-03 Integrated status and jobs | Saved/unsaved/saving/failure, pending operations, background progress/cancel, resource budgets and missing-data reports without hiding the map | UIIN-03 |
| P5-04 Launch/reopen/recovery | Normal reopening, recovered committed work, lost in-flight gesture where known, missing/corrupt source data and unsupported RenderingDevice backend with usable inspection/recovery path | ACPT-01, UIIN-03 |
| P5-05 Cross-platform state matrix | Platform-specific file/font dialogs, disabled/empty/error/busy states, display scaling, long paths/names and reduced available panel space | UIIN-02, UIIN-03, ACPT-01 |
| P5-06 Full-map acceptance walkthrough | A traceable journey from real `.ink` recovery to finished map, safe reopen, export/package and recovery cases, with design-review evidence and untested engineering items separated | ACPT-02 |

## Integrated user journey

Storyboard one coherent session using the same project, tool vocabulary and layout throughout:

1. Open/import the existing map and understand recovery limits and missing artwork.
2. Paint terrain/masks across boundaries while panning/zooming; edit a river, cut a lake with Land Subtract and paint another with water texture/colour, keeping mask versus colour targets clear.
3. Browse a local asset pack, replace missing art, place/transform/reorder stamps and commit a repeatable scatter.
4. Edit a path and a curved label, choose an available font and inspect substitution where necessary.
5. Reorder mixed object layers (stamps, paths and text) above the fixed terrain pair; lock/solo terrain and object layers, adjust opacity, use notes and finish the image with effects/grid settings.
6. Undo and redo recent and older edits, observe durable save state, close/reopen and continue without relying on cached pixels.
7. Export the intended size/version with labels/grid options, observe progress and cancellation, and identify the published result. Package the project or export integration JSON through the distinct appropriate flow.
8. Repeat a disrupted session: save failure, cancelled export, unavailable source, or process/device failure. Show the last acknowledged state and usable next actions honestly.

Include Windows and Linux examples of the same key actions. Do not invent platform features or claim the prototype can recover in-process after GPU loss.

## Input and focus contract

- A tablet is a pointing device in P0. Brush size/opacity remain controlled by the UI; pen pressure and advanced pen/eraser mappings are deferred. Specify hover/contact/drag/release where available, with accessible mouse/keyboard alternatives for any optional button mapping.
- Define pointer capture for brush strokes, transforms and curve handles. Cover the pointer leaving the canvas/window, release outside, application focus loss, a modal opening and device switching. Prevent a stale drag from committing an accidental stroke on return.
- Define navigation modifiers and the return to the prior tool. Avoid requiring hover-only information or a tiny handle as the only route to an exact edit; numeric inspector controls remain available.
- Audit shortcut scopes across all tools. Text entry and numeric editing take precedence over single-key tool bindings. Specify Escape, Enter, Delete, copy/paste and undo/redo in canvas, text, numeric, list and modal contexts. Recommend bindings and resolve collisions explicitly.
- Specify tab order, focus ring, focused versus active tool appearance, list navigation, modal focus return, keyboard access to resizers where feasible, and opening/dismissing context menus.
- Make selected layer, selected object and active tool legible independently. A switch between mouse and tablet must not silently change selection, brush settings or document state.

## Platform, scaling and accessibility review

Use 1920 × 1080 for the main frames and acceptance viewport. Review 1366 × 768 and 125%/150%/200% display scaling as design stress cases; these are evaluation suggestions, not new minimum-window or hardware promises. Explain the units used for frame dimensions and scaling.

Show how the canvas keeps usable space when panels resize, labels grow or paths become long. Define minimum panel widths, truncation with access to full values, wrapping rules, scroll ownership and where controls overflow. Tool properties must remain reachable without covering every part of the canvas.

Provide paired Windows/Linux file and font selection flows with native platform differences recorded. Preserve application action names and outcomes across platforms. Do not assume fonts, file permissions, path separators or toolkit accessibility behavior are identical. Platform differences should not change map geometry or export intent.

Supply colour/contrast values, text sizes, icon/handle target sizes, labels and non-colour status indicators. Review keyboard-only access to the main workflow and how labels/focus can be exposed through the native toolkit. State any accessibility limitations requiring implementation verification; do not claim formal compliance from mockups alone.

## Status, jobs and failure design

Use one coherent status vocabulary across save, import, history rebuilding, rendering, export and packaging. Show concurrent work without an overload of competing banners. A saved indicator is based on durable acknowledgement, not merely a finished animation or visible stroke preview.

Define when to use an inline message, persistent status, progress surface or modal. Failures requiring action must remain findable after a transient notification disappears. Include retry/cancel/details or a safe alternate local action as appropriate, with clear scope and retained work.

Resource UI should distinguish configured limits, measured/estimated usage and unmeasured engine/driver overhead. Core source targets: editor GPU allocation 25% VRAM capped at 4 GiB; decoded CPU cache 512 MiB; export buffers 512 MiB; total RAM ≤25% of system RAM; history acceleration 4 GiB on disk. Do not label an incomplete allocation counter as total GPU memory usage. Keep sources, retained history and disposable caches distinct.

Required error/recovery cases: disk full or permission denied during save; missing/corrupt authoritative source; unsupported graphics backend; cache rebuilding; missing font/art; partial import; stalled/cancelled/failed output job; failed final output validation; project packaging failure; native device loss/process termination; restart with recovered acknowledged work. A crash may prevent any in-session error dialog: design the fresh-launch experience too.

Missing RenderingDevice support means the rendering editor is unsupported in P0. File inspection/recovery remains usable. Show that capability boundary clearly; do not depict a full CPU rendering fallback. Recovery must never depend on post-loss saving or GPU readback.

## Acceptance and evidence contract

At the recorded 1920 × 1080 viewport, the unchanged targets are input-to-visible p95 ≤50 ms/p99 ≤100 ms; frame intervals p95 ≤20 ms/p99 ≤33.3 ms; recent undo affecting ≤16 resident tiles p95 ≤100 ms. Run each boundary-crossing paint, pan/zoom while painting, river point/width edit and undo/redo scenario for ≥60 seconds under warm, cold and forced-eviction conditions during implementation validation.

Measurements must connect each input/edit sequence to its rendered update and report the endpoint, excluded presentation latency, sample generation/processing/coalescing/drops, p50/p95/p99, queue depth, worst and all >100 ms stalls with causes, RAM and GPU allocation evidence. A generic frame callback or smooth design prototype does not establish those results. Design a compact optional diagnostics presentation only if useful; local diagnostics are opt-in and are not ordinary toolbar content.

Full acceptance also includes cache deletion/reopen, degraded imports, transparent ordering across atlas pages, sequential radius-20 blur dependency fixtures, offset shadows, corner-crossing stamps, coasts/grain/textures, cross-tile water, persistent history, interruption and publication recovery. These are engineering test obligations that the design must make understandable or operable, not invitations to add P1 blur controls.

Export duration is observational, with no pass/fail limit. Long exports need honest progress/cancel and must preserve editing responsiveness. A small software-rendered fixture cannot certify physical tablet behavior, real GPU drivers or Linux support.

## Deliverables and completion checklist

Write `DESIGN-RESPONSE.md` and place editable frames, exports or an optional interactive prototype in `design-assets/` beside this file. Preserve this brief.

- Paired P5-01 platform frames and an annotated integrated walkthrough covering all eight journey steps using the same sample project.
- The complete input/focus/shortcut matrix, including tablet contact, pointer capture, text editing, modal interruption and window focus loss.
- A component consistency review of Phases 1–4 with concrete proposed corrections. Reuse their tokens; identify any deliberate platform adaptation.
- Status and failure matrix mapping each event to persistence, visible message, user action and recovery outcome; include P5-03 and P5-04 screens.
- Scaling, keyboard, contrast, long-content and platform review sheets. Mark every observation as design-reviewed, prototype-tested, implementation-tested or still untested; never fabricate results.
- A manual acceptance script with setup, action, expected visible outcome and evidence to collect for each platform/input scenario. Reference the unchanged technical thresholds separately.
- Coverage mapping for UIIN-02, UIIN-03, ACPT-01 and ACPT-02, plus an inventory of outstanding integration dependencies.

Completion means a reviewer can follow the full map-making session across both platforms, operate the primary workflow with clearly scoped input, understand what is saved or running, and recover from failure without guessing which work or output survived.

## Boundaries and sources

This phase completes and validates P0. It does not add pressure, arbitrary docking, themes, multi-window, cloud features, a software renderer, 64K or extra export formats. Proposed review fixtures are not evidence of platform support until the implementation is tested.

[Roadmap](../../ROADMAP.md) · [Requirements and acceptance contracts](../../REQUIREMENTS.md) · [Working specification §§2–3,8,10–13](../../../docs/spec.md) · [Architecture §§8–10](../../../docs/architecture.md) · [Design index](../DESIGN-INDEX.md).

## Settled Phase 3 integration follow-up

Exercise side-panel text entry with Straight/Curve/S-shape parameters, mixed object layers with one protected default layer, independent tool settings and pin-only note reveal. Compare reconstruction side by side only during initial import; ordinary crash recovery reopens the latest durable save, including acknowledged autosaves. No post-import base-reconstruction wizard is required. See [Phase 3 context](../03-water-paths-and-labels/03-CONTEXT.md).

## Settled Phase 4 integration follow-up

Exercise a fixed 1,000-map-unit longest edge with independent editing (1K/2K/3K/4K, fixed at creation/import) and PNG export presets (1K/2K/3K/4K/8K/16K). Verify unchanged positions/sizes and source-asset rendering across output scales, with imported raster detail limits described honestly. Grid is topmost and initially hidden; PNG inclusion follows canvas with export-only overrides and optional 300 DPI metadata. Appearance is a dedicated toolbar panel.

ZIP is a current-editable-state export without inherited undo/redo; stay in the working project and preserve its history. Project Settings > Storage separates sources, history and caches with deliberate history-list cutoff. JSON is manual, includes hidden entities/notes with visibility and uses source-revision labels. PNG/ZIP/JSON use map-name/date/time default filenames. See [Phase 4 context](../04-project-finishing-and-safe-publication/04-CONTEXT.md). The separately inserted Phase 1.1 supplies normalization/legacy compatibility; do not reinterpret older projects silently.
