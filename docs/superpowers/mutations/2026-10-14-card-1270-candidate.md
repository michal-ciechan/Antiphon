# CARD-1270 unbound Mutation candidates

All pending. Code owner: `7b9b0246-8618-43db-9dc1-63b956a5ff72`.
Bind only after ordinary Review and landing; no deliberate mutant was run by Code.

| ID | Mutation | Exact witness | Variants |
|---|---|---|---|
| PC-1 | Drop `pin+chain` from the chain-read-failure unknown predicate (or drop the predicate's chain arm) | model `C1270 V-1 effective label preferred-chain-read-failure-default`; component `C1270 V-2 chain read failure with a Preferred pin on Medium` | Medium (RolePolicy appended), Easy; Required rows are controls |
| PC-2 | Render effective candidates despite routing-unknown, or keep stored reason/expiry when a chain or metadata read failed | component `C1270 V-2 chain read failure with a Preferred pin on Easy`; `C1270 V-2 chain metadata read failure with a Required pin on Easy` | effective list; reason; expiry (independent edits) |
| PC-3 | Remove the metadata-unknown line, or swap its chain/metadata wording | component `C1270 V-2 chain metadata read failure with a Preferred pin on Medium`; `C1270 V-2 chain read failure with a Required pin on Medium` | chain; chain metadata |
| PC-4 | Return the raw reason for an availability read failure, or match the marker anywhere in the text | model `C1270 V-1 availability availability-read-failure`, `availability-marker-is-not-free-text`; component `C1270 V-2 availability read failure with a Required pin on Easy` | prefix vs substring |
| PC-5 | Keep `(unavailable)` for a standing-agent read failure, or apply `(name unknown)` without the failure segment | model `C1270 V-1 effective label required-standing-agent-read-failure`, `standing-agent-missing-is-not-a-read-failure`; component `C1270 V-2 standing agent read failure with a Preferred pin on Easy` | failure vs plain missing agent |

Use method-scoped bound packs and red/restore/green receipts. Mutation owns final
binding and missing-control discovery (for example a segment-split change for `; `).
