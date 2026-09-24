# Q4 — Durable History Checkpoint and Compatibility Decision

Decision: Use periodic versioned checkpoints every 64 committed commands plus the complete authoritative command stream; keep at most 16 compatible resident tile deltas in memory, and rebuild disposable checkpoints/deltas from the baseline and retained commands after deletion or compatibility mismatch.

Evidence: The real-map geometry fixture (`3780 × 4097`, source SHA-256 `67b69858634b1a2eee8082efd80043e915dcebb36e29c8dc66e42c6406e54fab`) measured 256 resolved texture commands with renderer `history-replay-v1`, recipe `texture-brush-v1`: full snapshots consumed 42,619,231 bytes, interval 8 consumed 5,472,566 bytes with 7-command worst replay in 4.490 ms, interval 32 consumed 1,492,571 bytes with 31-command worst replay in 8.036 ms, and interval 64 consumed 829,250 bytes with 63-command worst replay in 6.080 ms; cancellation after 17 command units left the visible revision unchanged.

## Measurement setup

- Fixture identity: the imported-map SHA-256 already verified by the connected tracer, projected at its real 3,780 × 4,097 document dimensions.
- Workload: 256 immutable resolved texture commands distributed across the document, using the retained texture recipe and seeded replay path.
- Snapshot measurement: UTF-8 bytes for every retained materialized checkpoint. Complete command payloads remain authoritative and are not charged to the 4 GiB acceleration budget.
- Replay measurement: 25 in-process repetitions from the nearest candidate checkpoint to revision 255. These measurements choose policy; the plan 09 hardware/p95 gate remains authoritative for product acceptance.
- Cancellation measurement: reconstruction checked cancellation in command units and discarded its private candidate snapshot before any durable cursor or visible-state change.

## Candidate results

| Candidate | Retained snapshot bytes (256 commands) | Worst replay units | Measured replay | 10,000-command snapshot extrapolation | Result |
|---|---:|---:|---:|---:|---|
| Every revision | 42,619,231 | 0 | 0.000 ms | ~63.5 GiB | Rejected: exceeds the 4 GiB acceleration budget by a wide margin |
| Every 8 commands | 5,472,566 | 7 | 4.490 ms | ~8.2 GiB | Rejected: extrapolated checkpoint growth alone exceeds budget |
| Every 32 commands | 1,492,571 | 31 | 8.036 ms | ~2.2 GiB | Viable, but leaves less headroom for resident deltas, indexes, and filesystem overhead |
| Every 64 commands | 829,250 | 63 | 6.080 ms | ~1.2 GiB | Selected: best measured budget headroom with bounded replay |

The extrapolation applies the observed quadratic snapshot-growth curve of append-only stroke arrays and is deliberately conservative. Runtime cache accounting still enforces the 4 GiB cap and may evict any non-baseline checkpoint; eviction never removes commands, sources, the baseline, or the cursor.

## Compatibility policy

- Compatibility identity is the tuple `(renderer version, recipe version, immutable source identity)`.
- A matching resident delta may accelerate an adjacent transition only when both revisions and the complete compatibility tuple match.
- A matching periodic checkpoint may seed older reconstruction. Progress is reported as completed/total **commands**, not an invented percentage.
- Missing, corrupt, deleted, or incompatible acceleration is ignored and rebuilt from the immutable baseline plus the retained command stream.
- An output-changing renderer or recipe version never rewrites source commands or original assets. Reconstruction reports the mismatch/fallback so the later shell can warn before publishing changed output.
- Cursor commit remains the final durable step after reconstruction succeeds. Cancellation or failure before that transaction leaves the old cursor and visible snapshot intact.

## Rejected alternative

Retained full snapshots were rejected. The measured 256-command fixture is small enough to open, but its growth extrapolates far beyond the separate 4 GiB acceleration budget and would silently turn disposable acceleration into the only practical history representation.
