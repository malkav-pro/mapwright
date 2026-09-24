---
phase: 01-connected-imported-terrain
plan: 13
status: complete
gap_closure: true
requirements: [HIST-01, HIST-02, HIST-04, DURA-01, REND-04]
---

# 01-13 — Resident history and correlated presentation

Recent Undo/Redo now uses a compatible resident delta and an adjacent baseline load without redundant checkpoint replay. It still persists the cursor before publishing a state change; redo-branch replacement, restart replay, older reconstruction and cancellation retain their contract coverage. Adjacent one-stroke history changes invalidate their conservative bounds instead of the whole canvas, while unknown changes remain full-invalidating. The hardware recorder correlates each Undo and Redo by unique sequence, exact revision and draw generation at `FramePostDraw`, after excluding the initial setup draw from the measured interval.

On the pinned real fixture and physical Vulkan adapter, three 60-second UndoRedo rows each produced 15 correlated samples and passed the unchanged interaction and recent resident-undo gates. Input-to-draw p95 was 31.981 ms warm, 16.838 ms cold and 28.581 ms evicted; resident-undo p95 was 29.816, 11.861 and 11.693 ms respectively, touching one tile. All frame p95 readings were about 16.7 ms, with no >100 ms stalls. Evidence: `artifacts/phase1-15/diagnostic-undo-trio-60s.json`, SHA-256 `10bcea65c30aead01ae24da7511a1b279caa4d8518f1e537e649eecc0165d700`.

The full 12-row canonical matrix and independent connected cases remain Plan 01-14 work. These history rows alone do not close Phase 1; see `01-VERIFICATION.md` for the current phase verdict.
