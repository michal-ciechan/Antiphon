# CARD-0346 — GitHub sync notification: name individual issues, show label add/remove detail

Plan pass, 2026-09-03 (task 71fa95dc, Sol). Design only, transcribed here after the delegate
reported its decision but did not commit a doc — captured verbatim from its report, no
re-investigation performed.

## Current behaviour (confirmed against source before this plan)

`TrackerSyncSummaryFormatter.cs`: `MaxIdentifiersPerLine = 5` collapses any line over 5 cards to
`"CARD-a, CARD-b, ..., +N more"`. `TrackerSyncChangeKind.LabelsChanged` renders count-only by
design ("labels are the least interesting kind and the flap-prone one"). `TrackerSyncDtos.cs` has
no per-label add/remove detail captured anywhere on `TrackerSyncChange`, and a bare `StateChanges`
int counter with no itemized kind/line.

## Decisions

1. **Identifier cap**: `MaxAffectedIssuesPerKind = 20`, counted by DISTINCT AFFECTED CARDS, not raw
   comment events. At 21+ for a given kind, render that kind count-only rather than a misleading
   partial list. This keeps rare close/reopen events named in normal runs without making any kind
   unbounded.
2. **Message budget**: keep `MaxChars = 3500`. Telegram allows 1–4096 chars after entity parsing,
   so the current budget leaves 595–596 chars of headroom (Telegram Bot API `sendMessage` limits).
   Replace raw string slicing with budget-aware rendering in `TrackerSyncSummaryFormatter.cs`:
   render complete detailed-or-count-only kind blocks, preserve every included block intact,
   downgrade low-priority/high-volume detail (labels/comments) first when the combined message
   exceeds budget, and emit an explicit omission summary if even compact board blocks cannot fit.
   State transitions (close/reopen) get first claim on detail. Never cut an identifier or label
   diff mid-text.
3. **Label add/remove detail**: extend `TrackerSyncChange` (`TrackerSyncDtos.cs`) with an optional
   structured label delta (`Added`, `Removed` string lists) — no DB migration needed, additive
   optional property, existing fields/counters unchanged.
   - In `TrackerBidirectionalSyncService.SyncLabelsAsync`: compute the case-insensitive
     remote→desired set delta before writing, sort/dedupe for stable output, attach it only AFTER
     the corresponding GitHub write succeeds.
   - Export-origin `ReplaceLabelsAsync` yields its full set delta.
   - Import-origin records each successful stale-managed-label removal and status-label add.
   - Render format: nested per-issue list, e.g. `CARD-0123 (#42): +status:done, -status:active`.
     Above the 20-issue threshold (or when the global message budget requires downgrade), fall back
     to the existing concise count-only form.
4. **`StateChanges`**: already outbound GitHub issue state transitions only —
   `SyncStateAsync` increments it after a successful `SetStateAsync` close/open call, and those are
   ALREADY itemised as `ClosedOnGitHub`/`ReopenedOnGitHub`. Inbound terminal-card reopens are
   separately itemised as `ReopenedFromGitHub`/`ExternalReopens`. **No new state-change kind or
   formatter line needed** — this was a non-gap once traced.

## Explicitly out of scope for this card

- A real label delta makes recurring managed-label corrections understandable but does not
  eliminate flap: repeatedly re-adding `status:*` is still a real remote mismatch each run. Any
  label-only notification gate/dedup policy is a separate decision, not part of this card.
- Capturing inbound free-form GitHub label edits (as opposed to the existing outbound
  `LabelsChanged` signal) would need a new directional change kind — out of scope here.

## Verification (not yet run — Execute's responsibility)

- `TrackerSyncSummaryFormatterTests`: 20/21-issue threshold behavior, label add/remove rendering,
  full-budget no-partial-output cases.
- `TrackerBidirectionalSyncTests`: export and import label deltas, case-insensitive steady state
  (no delta when nothing actually changed).
- Run: `dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/*/<ClassName>/*"` per
  class, per `docs/testing-and-build.md`'s isolated-output convention.
