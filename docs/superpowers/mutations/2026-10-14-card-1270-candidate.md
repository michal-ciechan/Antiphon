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

## Repair round 3 candidates (Review 036839b4 D-1..D-3; Code task `dfc1166f-9815-466a-8264-30363b97e1f5`)

All pending; no deliberate mutant was run by Code.

| ID | Mutation | Exact witness | Variants |
|---|---|---|---|
| PC-9 | `pendingReadLabel`: return null for a paused read, or key on `isPending && fetchStatus === 'fetching'` (the old `isLoading`) | model `C1270 D-1 pending read label 'paused offline'`; component `C1270 D-1 'complexity chains list' read 'paused'`, `… 'Plan effective cells' read 'paused'`, `… 'session runners' read 'paused'`, `… 'runner default revisions' read 'paused'` | paused; loading text vs paused text |
| PC-10 | `routingPinsReadLabel`: drop the paused arm (back to null for a paused read) | model `C1270 D-1 routing pins read label 'paused offline'`; component `C1270 D-1 'routing pins' read 'paused'` | — |
| PC-11 | Editor: drop `readPending` from `saveBlocked` or from `clearBlocked`, or stop passing `readPending` from `RoleCellEditor` | component `C1270 D-1 editor whose cell read returns to pending blocks Save until the read loads`, `… blocks Clear override …` | save gate; clear gate; host wiring |
| PC-12 | `clearOverrideCopy`: ignore `chainUnknown` (definite fallback copy), or drop the metadata sentence | model `C1270 D-2 clear copy after a chain read failure falls back to 'any'`, `… chain metadata read failure names the unread reason and expiry ('any')`; component `C1270 D-2 clear confirmation after a chain read failure on 'Medium'`, `… chain metadata … on Medium …` | chain; metadata |
