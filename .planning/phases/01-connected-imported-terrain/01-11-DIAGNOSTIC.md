# Phase 1 gap diagnostic: first causal stage sample

The pinned 74,541,363-byte `.ink` fixture passed `ImportBounds` (19 assertions). The import case emitted `peak_process_bytes=552529920 peak_process_measured=true` after real editable import and project creation, before the later reference render and exports. The marker includes the fixture SHA-256 `67b69858634b1a2eee8082efd80043e915dcebb36e29c8dc66e42c6406e54fab`. Its peak is below one quarter of the recorded 33,443,094,528-byte system RAM (8,360,773,632 bytes). The full REND-05 envelope still needs fresh consolidated acceptance evidence.

One short real-Vulkan PaintAcrossTiles/Warm run captured a revision- and draw-generation-matched sample (duration 6.697 seconds). This is a diagnostic, **not** a qualifying ≥60-second matrix run.

| Stage | Measured ms |
|---|---:|
| Durable command | 26.923 |
| Full 1920×1080 reference render | 5,797.765 |
| PNG encode | 830.983 |
| PNG decode / texture creation | 32.415 |
| Matched canvas draw to postdraw | 1.517 |
| Input to matched postdraw | 6,692.840 |

One sample has identical median and worst stage costs; no distribution or p95 performance claim is made. Rendering is ~87% of observed end-to-end time and PNG encode ~12%. The correct next change is bounded region/tile evaluation and direct revision-tagged texture presentation. The report does not assume the product has passed REND-04.

Raw evidence: `artifacts/phase1-11/diagnostic.json`, SHA-256 `e7c7750f8f569c4603ae54f68ae1d04665598dab418acedc5aa320b17d859de1`.
