# Debug 3593eb2f: CARD-0747 stale land/completion note "replay"

**Outcome:** I diagnosed the problem, fixed it test-first and documented it. The live dry run is done, from
a read-only query that mirrors the new sweep. **Nothing was applied**, because the supported path exists
only on this branch and the live server does not run it yet.

## Diagnosis (full write-up: docs/investigations/2026-11-11-card-0747-receipt-replay.md)

- **It is a backlog drain, not a replay.** Orchestrator b2f1a869 was an adopted session from 2026-10-24 to
  2026-11-11. Every queue row for it was withheld with "Claude dialog observation unavailable: adopted
  session", and about 1,900 keyed rows built up Pending and never typed. The session restarted at
  2026-11-11T17:49:46Z, which ended the withhold. The queue then typed the backlog oldest-first, one row
  per idle turn, at 25–40 per hour. No note was typed twice: 117 land headers and 85 task headers were
  each typed exactly once.
- **Held noise.** Request b8858374 is Completed but has 46 Held notes, and each one is still typed.
- **The CARD-0747/0513 core.** A standing agent's fresh restart carries its Pending rows to the successor
  session (`AgentControlService` AcceptAsync), but the note keeps the old parent. CARD-1157 made the receipt
  scan follow the carried row only for Held/Aged/Conflict/Outcome. A carried TaskCompletion never
  confirms: profiled ones stop at `completion_queue_identity_mismatch`, and 11 live notes have their
  receipt on the carried session; unprofiled ones stop at `completion_delivery_generation_changed`. No
  open note ever reached an end state, so they were paged every 5 s forever and stayed in attention.

## Step 1 census (2026-11-11 ~22:15Z)

| Measure | Count |
|---|---:|
| Open AwaitingReceipt notes | 1,829 |
| Open DestinationUnavailable notes | 142 |
| Canceled notes | 13 |
| Of the AwaitingReceipt notes, linked to b2f1a869 (all Pending, withheld) | 1,787 |
| Older than 7 days and terminal | 1,305 (1,122 untyped Pending, 41 Sent, 142 unlinked) |
| Older than 7 days, typed Pending, left alone | 3 |

The doc has the per-kind and per-destination tables. Queue destinations other than the parent: 18 notes, all
carried rows (11 profiled TaskCompletion, 7 from the 09-13 CARD-0513 sighting).

## Fix (feat/card-task-3593eb2f)

- **Carried completion.** `ReceiptScanTarget.FollowsCarriedCompletion`: the identity check, delivery
  generation and receipt scan use the session the keyed row was typed to.
- **Aged terminal expiry**, in reconcile (`Supervision:DeliveryVerification:LandNoteReceiptExpiryDays`,
  default 7, 0 disables). It applies to an AwaitingReceipt or DestinationUnavailable note older than the
  threshold whose request is not pending (or, for a note with no request, whose task is
  Succeeded/Failed/Canceled). The note settles as `LegacyUnverified` with `receipt_expired_aged`. A
  never-typed row is canceled under the queue lock first, with the lock wait bounded to 1 s in the scan.
  Left alone: a row typed once that is still Pending, and a Sent row whose receipt is already stored (it
  confirms). Fresh notes are unchanged.
- **Held supersession.** A Held note whose request is terminal is settled `NotRequired` with
  `held_superseded_by_terminal_outcome` before it is typed. A typed Held row keeps its receipt scan.
- **Attention.** `LegacyUnverified` notes no longer appear as LandOutcomeUnconfirmed.
- **Operator sweep.** `POST /api/agent-tasks/land-notifications/expire {apply?, olderThanDays?}`. It is a
  dry run by default, takes at least 7 days, refuses apply from a child token, never deletes, and is
  idempotent. It is documented in docs/ops-http.md.
- **Migrations.** None are needed; the existing states are reused.

## SHAs

| Commit | SHA |
|---|---|
| Red | 785d5db11e44a2c47459ee4e728391995c35b4fd (8 witnesses red, 6 controls green) |
| Fix | d5358ec9943816b80895a803b7363d8850ea4937 |
| Fix follow-up (no expiry of a typed note with a stored receipt; Held fixture) | 448e1e59f5ee2b51389023f1e82d3ecb3b9d50af |
| Test strengthening | 8a4ec2ca1218e9b113edf66d56a03e5da7a4b53a, 02d556fe120e9b265736b9c55d2a744143b3b80f |
| Docs, tested tip | 6dfcb3749dbe93e0adefdd828229af93769c9fe0 |

## Tests (all on Linux server2, through leased runs with explicit timeouts)

- `C0747LandReceiptReplayTests` (16) plus `AgentTaskLandQueuedReceiptTests` (22): 38/38 at 6dfcb374.
- Guard union at 448e1e59, after which the server code did not change: 55 classes, 1,785 tests in 8 shards.
  - The only red is `AgentTaskWorktreeLockOutcomeTests` C443 ×20. It is **inherited**: the same
    PtyDeliveryProfile `SingleWriteMaxBytes 1024 vs 86400` assertion fails 20/21 at base 3db1a43b, run in
    a detached worktree.
  - `C508_GuardRefMismatchWarnedOnce(2,True)` failed once under load and passed alone.
- Doc-pinning classes (48 that read ops-http or investigations): 1,368 at 6dfcb374. 1,367 passed, 0
  failed, 1 skipped.
- Mutants: 15 runs (17 edits), all killed: carried rule, generation, scan target, delivered-to, aged
  gate, cancel, TypedPending guard (killed after strengthening its control), terminal check,
  stored-receipt check, NotAged, NotOpen, Held unlinked, Held linked, Held status/attempts gate,
  attention filter, sweep dry-run and sweep minimum.
- `check-evidence-diff`: 0 violations. `merge-tree origin/master` is clean; master is still 3db1a43b.

## Step 3: dry run (live database, 2026-11-12T00:53Z, read-only SELECT mirroring the sweep)

| Outcome | Count |
|---|---:|
| Would settle, untyped Pending (cancel, then settle) | 1,082 |
| Would settle, unlinked DestinationUnavailable | 142 |
| Sent, settles unless a stored receipt confirms it | 46 |
| TypedPending, left | 2 |
| Non-terminal ProviderInputWait, left | 4 |
| **Scanned** | **1,276** |

For b2f1a869 this covers 1,085 of its 1,091 aged notes. **Its 663 notes younger than 7 days are left
alone by rule**, so after deploy they will keep being typed for about 17–27 h.

**Apply: not done.** No before/after apply counts exist yet. They require a server build that contains this
branch, then `{"apply":false}` followed by `{"apply":true}`. With the fix deployed, the hosted reconcile
settles the same rows within about 5 min on its own.

## Decisions for the caller

1. Land the branch and activate it on the rc and production line, then run the sweep and record the
   counts.
2. If the 663 younger b2f1a869 notes are unacceptable now, there are two options. The existing per-message
   queue cancel route (`DELETE /api/sessions/{id}/messages/{messageId}`) can cancel them; the reconcile then marks
   each note Canceled. Or lower the threshold. Both go against the brief's 7-day floor, which is why
   neither was done.

## Owed

- Native Windows proof of the touched classes.
- Apply counts after deploy.
- Not pinned: the busy-lock path of the bounded cancel, and `ExpiryDays <= 0`.
- Candidate follow-ups:
  - `TerminalRunnerSeatReleaseService.HasPendingDeliveryAsync` and the dispatcher's DeliveryFailure
    recovery still treat `LegacyUnverified` as open (unchanged behaviour).
  - The adopted-session withhold itself, which silently builds a backlog for weeks (separate card).

--- next stage ---
next: Review
handoff: Review feat/card-task-3593eb2f 3db1a43b..6dfcb374 (CARD-0747 aged-note expiry, Held supersession, carried completion receipt, expire sweep); then land and run the sweep dry-run/apply on the activated server.
artifact: docs/investigations/2026-11-11-card-0747-receipt-replay.md
