# Q3 Terrain Solo Decision

Solo Decision: Solo is a non-destructive visibility filter over the two fixed terrain roles: when neither role is solo, each row follows its own Visible flag; when one or both roles are solo, only rows that are both solo and Visible render. Lock never changes visibility, and solo never overrides an explicitly hidden row.

Solo Evidence: D-06 requires independent visibility, lock, solo, opacity, and stable role identity while D-07 fixes Background below Foreground; the approved UI contract says basic terrain solo shows the chosen role without the other, forbids inferred mask-only solo, and keeps hidden and locked states explicit. Treating the solo set as the visible subset is the only candidate that gives deterministic outcomes for neither, either, and both solo buttons without changing role order, unlocking a row, or silently revealing a hidden row. Contract cases in `tests/Mapwright.ContractTests/BrushContracts.cs` are the acceptance oracle.

Underlay Decision: Transparent underlay is the project semantic beneath an empty or transparent Background; viewport pixels and PNG bytes both use straight-alpha sRGB, so a zero-content pixel is RGBA `(0, 0, 0, 0)` rather than an inferred chrome or project colour.

Underlay Evidence: The real source fixture retains its opaque/soft-alpha 3780 × 4097 preview and SHA-256 `54549d4c9b8cb6c762f7e271edcdf2c7cc797567c6617ecf7d0ae0493cfc760c` as flattened Background content, so transparency does not alter that source. Q1 records editable colour rasters as straight-alpha sRGB and mask alpha as scalar coverage; REND-02/EXPT-02 require linear-premultiplied composition followed by straight-alpha sRGB PNG output. The transparent fixture produces `(0, 0, 0, 0)` in both the shared viewport frame and row writer, while the rejected project-colour candidate would produce an opaque invented pixel without any document field defining that colour. The linear-soft-alpha oracle composites 50.2% sRGB red over opaque blue to `(188, 0, 187, 255)` within one channel, proving that the selected empty semantic and non-empty colour path share the same graph. Solo filtering remains upstream of this underlay and never creates a replacement colour.

## Visibility truth table

`Visible` is evaluated before `Solo`. `Locked` does not participate in rendering.

| Background solo | Foreground solo | Background effective visibility | Foreground effective visibility |
|---|---|---|---|
| false | false | `Background.Visible` | `Foreground.Visible` |
| true | false | `Background.Visible` | false |
| false | true | false | `Foreground.Visible` |
| true | true | `Background.Visible` | `Foreground.Visible` |

For every row above, a row whose own `Visible` value is false remains hidden. Painting is rejected when the target is not effectively visible or is locked; solo state does not unlock, reveal, or retarget a gesture.

## Alternatives rejected

- **Mutually exclusive solo:** rejected because the approved row contract exposes solo on both rows and does not authorize silently clearing the other row's state.
- **Last solo wins:** rejected because replay would depend on transient interaction order rather than the persisted two-row state.
- **Solo overrides Visible:** rejected because it would silently reveal a hidden row, contradicting D-10 and the UI's explicit **Show layer** action.
- **Mask-only Foreground solo:** rejected because the UI contract explicitly says mask-only solo is not approved.
