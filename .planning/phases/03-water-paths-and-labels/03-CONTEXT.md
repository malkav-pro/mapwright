# Phase 3: Water, Paths and Labels - Context

**Gathered:** 2026-09-22; finalized 2026-09-23
**Status:** Ready for planning; visual design and implementation remain pending

<domain>
## Phase Boundary

Complete lake painting, editable paths, labels/fonts, mixed object layers, pinned notes and initial-import comparison. Owned requirements: IMPT-04, IMPT-05, LAYR-02, WATR-02, PATH-01, TEXT-01, TEXT-02 and NOTE-02.

The user explicitly amended earlier requirements during discussion. Lakes use ordinary Land subtraction or Foreground water paint; labels use three parameterized modes; object layers mix stamps, paths and text; reconstruction occurs only at initial import. These decisions supersede conflicting earlier briefs and the lake-modifier portion of Phase 1 D-08. They do not replace river modifiers, ordinary undo, durable saves or rendering correctness. Grid implementation stays in Phase 4. Phase 1 execution is separate ongoing work; this context is not completion evidence. The design brief is an input, not an approved visual design or GSD requirements SPEC.
</domain>

<decisions>
## Implementation Decisions

### Lakes and paths
- **D-01:** Provide both lake workflows: Land Subtract edits Foreground coverage to reveal Background; water texture or solid-colour painting on Foreground changes colour without changing its mask. Neither workflow replaces the other.
- **D-02:** Mask-cut lakes use existing edged/round Land brushes; painted water uses the texture/colour painting brushes. Retain solid-colour values with each stroke for replay. Land Add can refill mask-cut water; later colour paint can cover painted water. Ordinary gesture undo, save/reopen and export apply.
- **D-03:** Dedicated closed-spline lake entities/modifiers and their handles, disable/delete controls and protected shapes are deferred. River centreline/width/bank-softness editing and post-mask modifier behavior remain. Prefer a fixed outer-coast style with unstyled inland banks; retain the authorized no-generated-style fallback if separation is too involved. Colour-only lakes add no mask contour; ordinary mask cuts do not establish semantic lake identity by themselves.
- **D-04:** Create new paths by drawing freehand, converting the completed stroke into editable geometry. Keep supported imported polyline/Bezier editing and width, colour, dash and cap controls.
- **D-05:** Apply gentle automatic smoothing to reduce small hand wobbles and produce a manageable number of editable points. An adjustable smoothing-strength control was not selected.
- **D-06:** After completing a path, remain ready to draw another. Select an existing path to enter point editing; do not automatically expose the new path's handles after every stroke.

### Text and fonts
- **D-07:** Enter and edit label content in the side panel, with a live canvas preview. Direct in-canvas text editing is not the selected workflow. Keep text-field focus and ordinary editing shortcuts distinct from document tools.
- **D-08:** Provide exactly three label modes: Straight, Curve and S-shape. Parameter controls replace the earlier mandatory freeform label-curve handle workflow. Internal geometry is an engineering choice, not a user-facing Bezier editor requirement.
- **D-09:** Curve uses peak deflection from -100% to +100%; 0% is straight, +100% is a semicircle, and -100% bends the opposite way. The endpoint is half a circle, not a closed circular baseline. Store the mode and resolved parameters for consistent reopen/export.
- **D-10:** S-shape has two linked, equal bends in opposite directions controlled by one shared deflection percentage. Changing the sign reverses the S. Independent bend values were offered and rejected. Use the same signed percentage convention; exact interpolation is to be documented and visually verified, not inferred as an arbitrary handle layout.
- **D-11:** Show curated bundled fonts first, with installed fonts in a separate section. Keep offline font access, family/face identification and readable previews. No cloud font service.
- **D-12:** Missing-font replacement defaults to every label using that missing font in the current map, with affected-label preview and intentional Apply. Preserve the original face/version identity where available and ordinary undo. This is not permission to silently apply substitutions or modify other projects. Size, tracking, colour, outline, shadow and matching viewport/export shaping remain required.

### Layers and independent tool settings
- **D-13:** Each tool retains its parameters independently. Stamp size, brush size and text size never carry across tools. This does not reset same-tool settings when changing a stamp asset (Phase 2 D-12) or Texture Brush terrain target (Phase 1); it separates different tools' state.
- **D-14:** Every object layer can mix stamps, paths and text. Separate typed layers are not required. Object layers stay above fixed Background/Foreground and retain create, reorder, rename, visibility, lock, solo and opacity controls without an arbitrary count cap. Their mixed entity order must remain explicit and visually correct.
- **D-15:** Object-placing tools keep the selected object layer when switching tools. If Background or Foreground is selected, visibly switch to the topmost object layer. Do not restore a different last-used layer per tool. Existing Land-mask auto-targeting and Texture Brush terrain targeting remain unchanged. Grid selection behavior belongs to the Phase 4 grid discussion.
- **D-16:** Every map starts with one automatically created object layer alongside Background, Foreground and Grid. It exists at map setup, not on first object placement. Grid implementation remains in Phase 4; this decision does not assign its order, default visibility or deletion behavior.
- **D-17:** The last remaining object layer cannot be deleted, but its contents can be cleared. Additional object layers are removable within ordinary document/undo rules.
- **D-18:** Existing hidden/locked target protections remain: visibly identify the target and offer Show layer or Unlock; never silently expose or unlock it. Switching to the topmost object layer does not bypass these guards.

### Pinned notes
- **D-19:** Support both Note tool then map click and right-click map then Add note. Place a pin and edit its content in the side panel.
- **D-20:** Revealing notes shows pins only. Select a pin to read/edit its content in the side panel; no default preview snippets or full floating note cards.
- **D-21:** Notes remain hidden by default and excluded from image export by default. Keep a discoverable reveal action. The note-creation flow must expose the active pin/editor sufficiently to complete the gesture; its precise temporary visibility treatment is a design detail, not a new global visibility preference. Integration JSON note inclusion remains a Phase 4 requirement.

### Initial import comparison and saved-project recovery
- **D-22:** Compare original preview and reconstructed content side by side during initial import, with linked pan/zoom. Keep a concise summary of editable, partial or visual-only content, missing assets/fonts and unsupported content. The original preview is a reference, not proof of editability or pixel parity.
- **D-23:** Reconstruction happens only during initial import: review, accept and continue editing the saved Mapwright project. Post-import base reconstruction from changed mappings, reapplication of later native edits and prior-base comparison workflows are deferred. Ordinary asset/font replacement and undo remain available.
- **D-24:** Crash recovery reopens the latest durably saved Mapwright version, including acknowledged autosaved edits. Only an active unacknowledged gesture may be lost. No manual reconstruction wizard, post-loss save or GPU readback is needed; the inherited fresh-process recovery guarantee remains.
- **D-25:** Retain original source bytes/rasters, IDs, unsupported properties and tested import semantics. Keep scoped replacement suggestions from Phase 2 and the unselected editable-versus-flattened initial import choice from Phase 1. Unknown state-changing operations still stop trusted affected import; preserve a visual fallback rather than double painting or inventing supported content. Detailed reconstruction-report UI is not required.

### Design and engineering discretion
No blanket delegation was given. Propose layouts, labels, units/defaults not settled above, smoothing tolerance and implementation, text shaping and parameter-to-baseline math within these constraints. Document and verify intermediate Curve/S-shape examples, long strings and negative deflection. Do not introduce freeform label handles, independent S bends or a reconstruction wizard as mandatory P0 controls. Standard pointer/keyboard accessibility, focus rules, property commit boundaries and inherited gesture cancellation still need design detail.
</decisions>

<canonical_refs>
## Canonical References

Read before planning or implementing; all paths are repository-relative.

### Scope and inherited decisions
- docs/spec.md §§3-6,8,11 — amended lake, path, label, layer, import and durability requirements.
- docs/architecture.md — engine-independent authority, commands, snapshots and renderer boundaries.
- docs/engine-decision.md — selected Godot stack and fresh-process device recovery.
- docs/README.md — source precedence and preserved historical evidence.
- .planning/PROJECT.md — project purpose and accepted amendments.
- .planning/REQUIREMENTS.md — eight Phase 3 IDs and unchanged shared acceptance gates.
- .planning/ROADMAP.md — phase ownership; grid remains Phase 4.
- .planning/phases/01-connected-imported-terrain/01-CONTEXT.md — fixed terrain, brush semantics, target guards and durable editing; only its spline-lake modifier wording is superseded here.
- .planning/phases/02-recovered-assets-and-stamps/02-CONTEXT.md — shared assets, replacements, independent scale/group rotation and retained stamp settings.
- .planning/phases/03-water-paths-and-labels/03-DESIGN-SPEC.md — synchronized standalone input brief, not an approved visual design.

### User-provided brush references
- .planning/phases/01-connected-imported-terrain/references/brush-preset-reference.png — texture brush preset examples.
- .planning/phases/01-connected-imported-terrain/references/land-edged-brush-reference.png — Land Roughness/Smooth controls.
- .planning/phases/01-connected-imported-terrain/references/land-round-brush-reference.png — Land Softness controls.
- .planning/phases/01-connected-imported-terrain/references/texture-edged-brush-reference.png — texture painting controls distinct from mask coverage.

### Codebase orientation
- .planning/codebase/STACK.md — Godot/C# pins and dependencies.
- .planning/codebase/ARCHITECTURE.md — mapped editor, import, domain/application and storage boundaries.
- .planning/codebase/CONVENTIONS.md — repository conventions and contract-test patterns.
</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable assets
- Existing Land/Texture Brush work provides the two lake workflows; reuse its command and paint-source contracts rather than creating a parallel lake entity system.
- The mapped EditSession/SQLite pipeline provides committed revisions, source retention and recovery; this phase adds commands/entities and rendering adapters through that boundary.
- Existing import services and source IDs provide a starting point for supported entities, missing assets and original-preview comparison; they do not establish full import fidelity.

### Established patterns
- Domain/Application stay independent of Godot; the UI submits commands and the renderer consumes immutable revisions. Completed commands become durable before acknowledgement or dependent GPU work.
- Original source data and native edits are authoritative; derived GPU tiles/caches are disposable. Initial import semantics and later native commands are distinct.
- Shared viewport/export geometry and text shaping must be verified; a visual design alone cannot certify rendering correctness.

### Integration points
- Extend the common shell, contextual inspector, object list, asset/font replacement and layer selection feedback.
- Add mixed-entity ordering and label parameters to durable document state; retain per-tool editing settings separately from per-entity persisted geometry/style.
- The codebase maps describe an earlier inspected starting point. Phase 1 implementation is progressing concurrently; research must read the current source before deciding specific integration seams. No build or runtime acceptance is claimed by this discussion.
</code_context>

<specifics>
## Specific Ideas

- User: use the same Land tool for lake cuts, and also allow painting water texture/colour.
- User-defined label vocabulary is Straight, Curve and S-shape; a 100% Curve is a semicircle, and S bends are linked/opposite.
- User explicitly rejected tool-size carryover and separate stamp/path/text layer types.
- User wants a simpler recovery experience. Initial import retains side-by-side review; later recovery reopens saved work.
</specifics>

<deferred>
## Deferred Ideas

- Dedicated closed-spline lake entities/modifiers and their editing UI.
- Post-import base reconstruction from changed asset mappings and reapplication of subsequent native edits.
- Arbitrary-path text attachment remains deferred; no mandatory freeform label-curve editor or independent S-bend controls in this implementation.
- Grid implementation and its placement/appearance decisions remain Phase 4, with one default object layer already settled.
- Earlier deferrals (area-fill scatter, advanced coast controls, pressure, cloud fonts and automatic label placement) remain in force.
</deferred>

---
*Phase: 03-water-paths-and-labels*
*Context gathered: 2026-09-22*
