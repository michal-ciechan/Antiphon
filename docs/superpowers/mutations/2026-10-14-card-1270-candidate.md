# CARD-1270 unbound Mutation candidates

All pending. Code owner: `7b9b0246-8618-43db-9dc1-63b956a5ff72`; PC-2 revision and PC-6..PC-8 from repair
Code task `35080534-0f87-4490-bef3-ad25549454e3` (Review ce90839f D1-D4).
Bind only after ordinary Review and landing; no deliberate mutant was run by Code.

| ID | Mutation | Exact witness | Variants |
|---|---|---|---|
| PC-1 | Drop `pin+chain` from the chain-read-failure unknown predicate (or drop the predicate's chain arm) | model `C1270 V-1 effective label preferred-chain-read-failure-default`; component `C1270 V-2 chain read failure with a Preferred pin on Medium` | Medium (RolePolicy appended), Easy; Required rows are controls |
| PC-2 | Render effective candidates despite routing-unknown, or keep stored reason/expiry when a chain or metadata read failed (drop `&& !metadataUnknown`) | effective list: component `C1270 V-2 chain read failure with a Preferred pin on Easy`. Reason/expiry: component `C1270 V-2 chain read failure beside a stored reason on Easy`, `C1270 V-2 chain metadata read failure beside a stored reason on Easy` (the `$read read failure with a $strength pin` rows carry null reason/expiry, as the server sends on a failed read, so they stay green under this variant) | effective list; reason; expiry (independent edits) |
| PC-3 | Remove the metadata-unknown line, or swap its chain/metadata wording | component `C1270 V-2 chain metadata read failure with a Preferred pin on Medium`; `C1270 V-2 chain read failure with a Required pin on Medium` | chain; chain metadata |
| PC-4 | Return the raw reason for an availability read failure, or match the marker anywhere in the text | model `C1270 V-1 availability availability-read-failure`, `availability-marker-is-not-free-text`; component `C1270 V-2 availability read failure with a Required pin on Easy` | prefix vs substring |
| PC-5 | Keep `(unavailable)` for a standing-agent read failure, or apply `(name unknown)` without the failure segment | model `C1270 V-1 effective label required-standing-agent-read-failure`, `standing-agent-missing-is-not-a-read-failure`; component `C1270 V-2 standing agent read failure with a Preferred pin on Easy` | failure vs plain missing agent |
| PC-6 | Editor: drop `cellEditorFailedReads` chain arm (default draft, Unset note, Clear hidden), prefill stored reason/expiry on a metadata failure, or drop `saveBlocked`/`clearBlocked` | component `C1270 V-2 editor after a 'chain' read failure on 'Easy'`, `… 'chain metadata' read failure on 'Easy'`, `… 'chain metadata beside a stored reason' read failure on 'Easy'`; controls `… 'none' …`, `… 'stage pin' …` | chain draft; chain Clear; metadata prefill; Save gate; Clear gate |
| PC-7 | Routing pins: render stale `pinsQuery.data` after an error, or drop the loading arm of `routingPinsReadLabel` (back to `No card-specific pins.`) | component `C1270 V-2 routing pins stale after a failed refetch`, `C1270 V-2 routing pins loading`, `C1270 V-2 routing pins failed` | stale; loading; failed |
| PC-8 | Runner defaults: drop `historyUnknown` or label an unread catalogue `missing from catalogue` | component `C1270 V-2 runner defaults history read failed`, `… history read loading`, `C1270 V-2 runner defaults catalogue read failed`; controls `… history read empty`, `… catalogue read loaded` | history failed; history loading; catalogue |

Use method-scoped bound packs and red/restore/green receipts. Mutation owns final
binding and missing-control discovery (for example a segment-split change for `; `).
