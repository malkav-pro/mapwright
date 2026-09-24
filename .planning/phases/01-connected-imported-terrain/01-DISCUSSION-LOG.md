# Phase 1: Connected Imported Terrain - Discussion Log

> **Audit trail only.** Do not use as input to planning, research or execution agents.
> Decisions are captured in 01-CONTEXT.md; this log preserves the alternatives considered.

**Date:** 2026-09-22
**Phase:** 01-connected-imported-terrain
**Areas discussed:** import/opening; tools; layers/targets; save/history/interruptions; brush shapes; terrain and coastline scope amendments.

## Import and first opening

### After importing your .ink map, what should happen first?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Quick recovery review (recommended) | Yes |
| 2 | Straight into editing |  |
| 3 | Guided setup |  |

**User's choice:** Quick recovery review: show original preview and short recovered/missing summary, then open editor.

### If editable terrain is recovered but artwork is missing, which starting view should be the default?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Editable terrain (recommended) |  |
| 2 | Original appearance: locked flattened background with new editing above |  |
| 3 | Choose during review, neither preselected | Yes |

**User's choice:** Choose during review each time; present both approaches with no preselected choice.

### Where should the new Mapwright project be saved?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Choose location each time (recommended) |  |
| 2 | Default projects folder | Yes |
| 3 | Beside the .ink file |  |

**User's choice:** Default configured projects folder, with option to change location.

### On later launches, what should Mapwright open?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Recent projects screen (recommended) | Yes |
| 2 | Last project automatically |  |
| 3 | Empty editor |  |

**User's choice:** Recent projects screen with Open and Import.

### More to discuss here, or move on?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Next area: tools and everyday controls | Yes |
| 2 | Discuss import and opening further |  |

**User's choice:** Move on.

## User terrain-model amendment

### Unsolicited scope correction accompanying the request to move on.

**User's choice:** User requests exactly Background and Foreground terrain layers, with Foreground owning the coastline mask and remaining object layers above them; simpler initial model instead of multiple brush layers with separate masks. Amend the spec.

**Notes:** Applied to working spec, architecture, active planning and all design briefs. Fixed terrain role/order and water targeting Foreground follow this model. No additional terrain/mask layers. Preserve original source/probe evidence. Numeric acceptance gates unchanged. Visual fallback uses locked preview-backed Background with empty Foreground, superseding earlier wording about arbitrary new terrain layers above the preview.

## Tools and everyday controls

### How should you choose where to texture-paint?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | One Texture Brush with a Background/Foreground selector (recommended) | Yes |
| 2 | Separate Background and Foreground brush buttons |  |

**User's choice:** One Texture Brush with a Background/Foreground selector.

### When switching between Background and Foreground, which settings should carry over?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Remember each layer texture; share brush settings (recommended) |  |
| 2 | Remember everything separately |  |
| 3 | Share everything | Yes |

**User's choice:** Share everything: current texture and all brush settings remain unchanged when switching targets.

### How should brush size be displayed?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Diameter in map pixels (recommended) | Yes |
| 2 | Relative slider such as 1-100 |  |
| 3 | Both slider and editable map-pixel diameter |  |

**User's choice:** Diameter in map pixels, independent of zoom.

### How should you pan while a painting tool is active?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Space + drag or middle-mouse drag (recommended) | Yes |
| 2 | Right-mouse drag |  |

**User's choice:** Space + drag or middle-mouse drag temporarily pans, then returns to painting on release. Mouse-wheel zoom is centred on the pointer.

**Notes:** Pointer-centred mouse-wheel zoom was specified as common to both choices and included in the accepted area summary.

### Next area: layers and editing targets, or discuss tool controls further?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Next area: layers and editing targets | Yes |
| 2 | Discuss tool controls further |  |

**User's choice:** Next area: layers and editing targets.

## Layers and editing targets

### If Background is selected and you choose the land-mask or river tool, what should happen?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Automatically target Foreground (recommended) |  |
| 2 | Require selecting Foreground first |  |

**User's choice:** Automatically select Foreground's mask itself and clearly highlight it.

**Notes:** User: autoselect the mask itself or Foreground; Inkarnate selects the mask. Assistant selected the mask-specific interpretation, carried into the next accepted choice.

### When switching back to Texture Brush, where should it paint?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Return to last texture-painting target (recommended) | Yes |
| 2 | Stay on Foreground |  |

**User's choice:** Return to the last texture-painting target (Background or Foreground).

### If you try to paint on a locked target, how should the editor respond?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Block painting and show inline Unlock (recommended) | Yes |
| 2 | Show confirmation asking whether to unlock |  |

**User's choice:** Block painting and show an inline Unlock action; keep locked until explicitly unlocked.

### If the selected terrain layer is hidden, what should happen?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Block painting and show Show layer action (recommended) | Yes |
| 2 | Automatically reveal it when trying to paint |  |

**User's choice:** Block painting and show a Show layer action; keep visibility unchanged until explicitly revealed.

### Next area: save, history and interruptions, or discuss layer behavior further?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Next area: save, history and interruptions | Yes |
| 2 | Discuss layer behavior further |  |

**User's choice:** Next area: save, history and interruptions.

## Coastline styling amendment

### User requested amending MASK-01, REND-02 and REND-03 to remove unneeded first-implementation controls.

**User's choice:** Defer visible coastline styling controls; retain basic mask editing and rendering correctness.

**Notes:** Assistant explained that REND-02 and REND-03 are colour/alpha and seam guarantees rather than UI controls.

### Automatic coastline or plain land mask for the first implementation?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Automatic coastline (recommended), customization later |  |
| 2 | Plain land mask, all decorative effects later |  |

**User's choice:** Coastline should have a style while rivers/lakes probably should not. If distinguishing them is too hard, drop styling.

**Notes:** User supplied a conditional preference, not a numbered selection. Working spec/requirements/roadmap/design briefs now prefer one fixed outer-coast treatment with unstyled inland water and authorize an unstyled fallback. Style controls, decorative fades, rings and isolines deferred beyond P0. Distance checks conditional on a used distance path; colour/alpha and seam checks remain. Code inspected only: prototype styles combined coverage; no implementation or feasibility proof claimed.

## Save, history and interruptions

### How would you like to access undo history?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Collapsible history panel (recommended) | Yes |
| 2 | Always-visible history panel |  |
| 3 | Undo/Redo buttons only |  |

**User's choice:** Collapsible history panel; Undo/Redo buttons stay visible.

### If you press Escape during a brush stroke or property drag, what should happen?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Cancel the whole gesture (recommended) | Yes |
| 2 | Keep the current result and stop |  |

**User's choice:** Cancel the whole gesture, restore its previous state and add no undo step.

### During an older undo rebuild, should you be able to pan and zoom or wait behind a blocking dialog?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Pan and zoom, editing temporarily blocked (recommended) | Yes |
| 2 | Blocking progress dialog |  |

**User's choice:** Allow pan/zoom for inspection, temporarily block editing, show progress and Cancel.

### After reopening following a crash, how should recovery be presented?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Brief recovery banner (recommended) | Yes |
| 2 | Recovery summary dialog |  |

**User's choice:** Open recovered map with brief banner explaining that saved edits were restored and an unfinished gesture may be missing; details available.

### Save Phase 1 decisions and continue to Phase 2, or explore more Phase 1 decisions?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Save Phase 1 decisions and continue to Phase 2 |  |
| 2 | Explore more Phase 1 decisions | Yes |

**User's choice:** Explore more Phase 1 decisions: user wants to go into brush shapes.

## Brush shapes

### For brush footprints, what should the first version support?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Geometric shapes: circle/square with stretch and rotation (recommended) |  |
| 2 | Geometric plus built-in irregular shapes |  |
| 3 | Those shapes plus custom image tips |  |

**User's choice:** User supplied a screenshot of an Official Brushes preset library instead of a numbered selection.

**Notes:** Visual reference shows tip icons and stroke previews, hard/soft round and tapered variants, square, pencil, chalky, cloudy, grainy, hairy, streaky and dirt-like presets. Treat as reference for desired brush vocabulary/presentation; exact preset inventory, custom tip import, pressure and applicability to mask tools have not been approved by this image alone.

### Should the same brush preset library serve texture painting and land-mask editing?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Same preset library for both (recommended) |  |
| 2 | Textured presets for terrain painting; simple round/square brushes for masks |  |

**User's choice:** No. Land edges use either an edged polygon brush or a round soft brush. Texture brushes can also have an edged variant as in the supplied references.

**Notes:** Free-text correction overrides both proposed options: do not add square or textured-tip presets to Land on this basis. Land references show Add/Subtract and size; edged mode shows Roughness and Smooth, round mode shows Softness. Texture reference shows size, opacity, Roughness/Smooth, selected texture, texture scale and texture rotation. Exact defaults/ranges and Smooth behavior are not established by a screenshot alone. Earlier rich preset image remains a texture-painting reference, not a mask-brush requirement.

### For our edged brush, what should Smooth do?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Round off sharp outline corners (recommended) | Yes |
| 2 | Soften edge transparency |  |
| 3 | Smooth hand movement |  |

**User's choice:** Round off sharp outline corners while retaining an irregular shape with a firm edge.

### How should tapered texture presets work with mouse input?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Automatic taper at stroke ends (recommended) | Yes |
| 2 | Uniform width for now; defer tapering |  |

**User's choice:** Automatically taper at both stroke ends, without requiring pen pressure.

### Save Phase 1 decisions and continue to Phase 2, or go deeper into brushes?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Save Phase 1 decisions and continue to Phase 2 | Yes |
| 2 | Go deeper into brushes |  |

**User's choice:** Save Phase 1 decisions and continue to Phase 2.

## Design and engineering discretion

No blanket delegation was given. Existing design-agent briefs leave visual details and unspecified defaults open within accepted behavior. The user explicitly permits dropping generated coastline styling if coast/inland-water separation is too involved.

## Deferred Ideas

- Coastline style controls, decorative fades, wave rings and isolines beyond P0; fixed outer-coast styling also deferred if the authorized unstyled fallback is needed.
- Pressure stays P1; custom tip import was offered but not selected.

## Supporting references

- `docs/spec.md`
- `docs/architecture.md`
- `docs/engine-decision.md`
- `docs/reference/map-editor-spec-final.md`
- `.planning/PROJECT.md`
- `.planning/REQUIREMENTS.md`
- `.planning/ROADMAP.md`
- `.planning/phases/01-connected-imported-terrain/01-DESIGN-SPEC.md`
- `Shaders/terrain_coverage.glsl`
- `Shaders/terrain_sdf_seed.glsl`
- `Shaders/terrain_sdf_color.glsl`
- `src/Mapwright.Domain/MapModel.cs`
- `.planning/phases/01-connected-imported-terrain/references/brush-preset-reference.png`
- `.planning/phases/01-connected-imported-terrain/references/land-edged-brush-reference.png`
- `.planning/phases/01-connected-imported-terrain/references/land-round-brush-reference.png`
- `.planning/phases/01-connected-imported-terrain/references/texture-edged-brush-reference.png`
