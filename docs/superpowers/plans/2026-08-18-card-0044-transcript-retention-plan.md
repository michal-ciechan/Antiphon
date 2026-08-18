# CARD-0044 — Retention for TranscriptEntries (and the three sibling tables): plan

**One nightly-ish retention pass, per-table windows, and — the load-bearing decision — transcript
deletion is per-session all-or-nothing, never a partial trim.** Partial trims of a session's history
are not merely risky, they are *actively harmful* under the current ingestion design (§2), and
all-or-nothing sidesteps every hazard the card names without renumbering anything.

## 1. Ground truth (measured 2026-08-18, dev Postgres)

The card said 41,629 rows; today it is **62,063 rows / 58 MB** (`pg_total_relation_size`), growing
~20k rows in the days since the card was written. Distribution:

| Session status | Sessions | Transcript rows | Rows > 30d old |
|---|---|---|---|
| Running (2) | 13 | 4,476 | 0 |
| Stopped (4) | 107 | 18,815 | 663 |
| Failed (5) | 96 | 38,772 | 306 |

Two consequences for the design:

- **The bulk is recent dead sessions, not old rows.** Only 18 terminal sessions (293 rows) are fully
  stale today, so the first prune deletes little — retention is a steady-state cap, not a one-shot
  cleanup, and the 57.5k rows on the 203 dead sessions become eligible as they age. The pass that
  eventually removes the mass is the **session-row** deletion at 90d (§4), whose FK cascade takes
  the transcripts with it; the 30d transcript pass covers the 30–90d window.
- Row age within a session is mixed (663 of 18,815 Stopped-session rows are >30d), which is exactly
  the shape a naive `WHERE CreatedAt < cutoff` would partially trim. It must not (§2).

Queue: 556 Sent / 11 Pending / 3 Canceled. Tasks: 3 Blocked, 149 Succeeded, 13 Failed, 15 Canceled.

## 2. Why partial trims are forbidden (the card's two hazards, grounded)

**Hazard A — resurrection via uuid-dedup.** `AgentSessionRuntime.PersistTranscriptAsync`
(`server/Application/Services/AgentSessionRuntime.cs:490-544`) dedups incoming entries by *uuid
presence in the DB*, deliberately not by sequence (a fresh tailer generation restarts numbering
from 1). Any path that re-emits full history — pty-host adoption after a runner restart, `/clear`
fork re-discovery, a `--resume` relaunch of the same session row, the reconnect catch-up sync
(`SessionRunnerEventPump` catches up ALL runner sessions on every reconnect) — checks each record's
uuid against stored rows and **re-inserts anything missing, rebased past the session's current max**
(`storedSeq = maxSeq + 1`, line 544). Delete a session's *old* rows while its *new* rows survive and
the next re-emission resurrects the deleted history ABOVE the kept rows: stale activity above the
last TurnEnd — the exact arrival-order shape that badged Antiphon-Opus "Working" forever on
2026-08-08 — and the retention achieved nothing because the data came back. So: **for any session
that could ever be re-tailed, the only safe deletion is all rows or none.** (An *empty* transcript
is safe even against a later resume: with `maxSeq = 0` re-ingestion is indistinguishable from first
ingestion, in file order.)

**Hazard B — activity without an end marker.** `SessionMessageQueueService.IsWorkingAsync`
(`server/Application/Services/SessionMessageQueueService.cs:1214-1288`) compares max activity
sequence against max end-marker sequence (TurnEnd / restart boundary / manual compact boundary /
interrupt marker). A trim that deleted a session's last end-marker while keeping newer activity
reads "working" forever. All-or-nothing makes the shape unrepresentable: zero rows reads **idle**
(`activity 0 <= end 0`), which is correct for a dead session and is already a relied-upon invariant
at launch (CARD-0006: the launch note is queued WhenIdle before any transcript exists). Sequences
are never renumbered; the unique `(AgentSessionId, Sequence)` index is untouched.

## 3. Slice 1 — TranscriptEntries + SessionQueuedMessages (the measured problem)

### Transcript pass: whole sessions only

A session's transcript is deleted (all rows, one `ExecuteDeleteAsync` per session — natural
batching and per-session logging) when **all** of:

1. `Status` is terminal: `Stopped` or `Failed`. Never `Created/Starting/Running/Stopping` — this is
   how a live always-on agent running since 2026-08-09 is protected: not by age arithmetic but by
   never touching a non-terminal session at all.
2. The session is **no agent's `PersistentSessionId`**. That column is a loose string (no FK) —
   load the agents' parsed ids into memory first, mirroring
   `AgentSupervisorService.FindPersistentSessionAsync`'s `Guid.TryParse` semantics (14 of 16 agents
   carry one today). This guards the CARD-0056 re-adoption path (which re-adopts *Failed* rows) and
   any operator resume of an agent's current-but-stopped session.
3. **Activity-relative cutoff**, per the card's suggestion: the session's newest transcript row
   (`MAX(CreatedAt)`) *and* its `LastSeenAt` are both older than `TranscriptRetentionDays` (30).
   Keying off the session's own last activity, not wall-clock alone, means a session is never
   caught mid-history: either its whole story is stale or none of it is deleted.

Consumers checked and unaffected: `IsWorkingAsync` (idle on zero rows, correct — §2);
CARD-0055 delivery confirmation and late-confirm (`WaitForTranscriptConfirmAsync`,
`LateConfirmAttemptedMessagesAsync`) only run against sessions taking deliveries, i.e. non-terminal
ones; `DelegationUsageRollup.ForSessionAsync` is read at task settle time and its result is stamped
onto the `AgentTask` row (`CostUsd` etc.), so pruning a settled session's transcript later loses no
cost data; `AgentTaskReplyService` correlation and `DelegateCheckProbe` read only running tasks'
sessions. The one visible loss is intended: the UI transcript view of a >30d-dead session renders
empty. (Optional nicety, not in scope: a "transcript pruned by retention" note in the session view.)

### Queued-message pass: settled rows only

Direct prune (independent of session liveness — this is what keeps a long-lived always-on session's
queue bounded): delete rows where `Status ∈ {Sent, Canceled}` **and** `CreatedAt <
now − QueuedMessageRetentionDays` (30) **and** `(Origin != Channel OR ChannelReplySettledAt IS NOT
NULL)`.

- **Never delete `Pending`, at any age.** A *parked* message (CARD-0055) is deliberately Pending —
  visible in the queue UI, excluded from redelivery — and a parked channel reply is a human waiting
  on a dead line. Retention silently removing it would convert a Critical-incident-worthy state
  into nothing.
- **Never delete an unsettled channel correlation** (`ChannelReplySettledAt IS NULL` on a
  Channel-origin row). CARD-0067 made that row the *only* store of the reply route; its TTL sweep
  raises `ChannelReplyLost` (Critical) — retention deleting the row would erase the owed-reply
  evidence instead of settling it. (In practice such rows are minutes old, not 30 days, precisely
  because the sweep fires; the predicate is belt-and-braces.) The partial index
  `IX_SessionQueuedMessages_OpenChannelCorrelations` is unaffected.

### Service shape — follow the incident/alert precedent, one pass for everything

- **`RetentionSettings`** (new, bound to `"Retention"` in `Program.cs`, next to
  `SupervisionSettings`/`AlertsSettings` at `Program.cs:130`): `TranscriptRetentionDays = 30`,
  `QueuedMessageRetentionDays = 30`, `SessionRetentionDays = 90`, `TaskRetentionDays = 180`,
  `SweepHours = 6`. Convention: a value `<= 0` disables that table's pass (an explicit off-switch,
  unlike the audit knob's silent deadness the card complains about).
- **`DataRetentionService`** (scoped, `TimeProvider`-injected like `AgentSupervisorService`): one
  public method per table returning the deleted count — the exact shape of
  `AgentSupervisorService.PruneIncidentsAsync` (`ExecuteDeleteAsync`, testable without the timer) —
  plus `RunOnceAsync` running them in order **tasks → sessions → transcripts → queued messages →
  audit** (task pruning frees sessions, session pruning cascades transcripts; order only affects
  how soon a row becomes eligible, never correctness).
- **`DataRetentionHostedService`**: `PeriodicTimer`, sweeps every `SweepHours` like
  `AlertDigestFlushHostedService`'s 6h prune, logs per-table counts, and uses the mandatory
  `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)` shutdown guard.
  This satisfies the card's "one retention pass with per-table windows, not four services": ONE new
  hosted service covers all four tables plus audit. The existing incident/alert prunes stay where
  they are — they work and are test-pinned; consolidating them is optional follow-up, not this card.

No migration: nothing here changes schema.

## 4. Slice 2 — AgentSessions rows (90d; this is the eventual bulk deleter)

**Cascade behaviour today (the card asked):** the FK constraints are real Postgres constraints, so
`ExecuteDeleteAsync` on sessions fires them at the DB level. Deleting an `AgentSession` row
cascades `TranscriptEntries` (**CASCADE**, `AppDbContext.cs:1037`) and `SessionQueuedMessages`
(**CASCADE**, `:1064`), and nulls `RunAttempts.AgentSessionId` (**SET NULL**, `:1098`) and
`Cards.OwnerSessionId` (**SET NULL**). Nothing cascades *into* sessions that matters here. The
interaction with the transcript pass is benign and useful: the session-row delete is the mechanism
that ultimately removes the 57.5k-row mass; the slice-1 transcript pass only covers the 30–90d gap.

Two references are **loose Guids with no FK** and would dangle: `AgentTask.AgentSessionId` and
`AgentTask.ParentSessionId` (index but no constraint, `AppDbContext.cs:1283`), and
`Agent.PersistentSessionId` (string). A dangling task-session link breaks the task-detail UI's
session view and the check-in `CallerIsListeningAsync` probe of the parent session, so:

Delete a session row when: terminal status; `LastSeenAt < now − SessionRetentionDays` (90); not any
agent's `PersistentSessionId`; and **no surviving `AgentTask` references it** via `AgentSessionId`
*or* `ParentSessionId`. The last exclusion makes the windows self-sequencing: a session outlives
its tasks' 180d window automatically, then becomes eligible on a later sweep — no cross-table date
arithmetic. (A handful of tasks already reference no existing session today, from pre-retention
manual deletions, and the UI survives it — but new danglings should not be manufactured.)

## 5. Slice 3 — AgentTasks (180d, whole trees only)

The task tree is the durable delegation ledger (goal, verdict, report path, stamped cost) — the
card is right that it deserves a longer window: **180 days**, configurable. Rules:

- A tree qualifies only when **every** task sharing its `RootTaskId` is terminal
  (`Succeeded/Failed/Canceled` — never `Queued/Dispatched/Working/Blocked`) and the tree's newest
  `COALESCE(CompletedAt, CreatedAt)` is past the window. Never delete part of a tree: the
  `ParentTaskId` FK is **Restrict** *on purpose* ("deleting a parent must NOT cascade a whole
  subtree away — the tree is the audit trail", `AppDbContext.cs:1286-1291`), and retention honours
  the same principle by taking whole stale trees or nothing.
- Delete children-first (order by `Depth` descending, or per-tree in one transaction) so Restrict
  never fires. `AgentTaskEvents` cascade (`:1308`).
- Cost reporting: each task row carries its own stamped `CostUsd`/token totals (rolled up at settle
  time), so any dashboard summing history simply loses visibility past 180d — stated trade, and the
  knob is per-table if half a year is ever too short.

## 6. Slice 4 — the audit knob that only looks wired

Card's fix-or-delete: **wire it** (small, and NFR24 wants archival). Precise current state, slightly
different from the card's wording: `AuditService.ArchiveFullContentAsync` *is* reachable — via
`DELETE /api/audit/archive` (`AuditEndpoints.cs:100-120`) — but nothing schedules it, and
`AuditSettings.RetentionDays` is read by **nothing**: the endpoint hardcodes its own `?? 90`
default. Fix in one slice:

- `DataRetentionService.RunOnceAsync` calls `ArchiveFullContentAsync(auditSettings.RetentionDays)`
  (respecting the `<= 0` disable convention).
- The endpoint's default becomes `auditSettings.RetentionDays` instead of the literal 90.
- While touching it: `ArchiveFullContentAsync` loads full entities to null one column — convert to
  `ExecuteUpdateAsync`. It only nulls `FullContent`, preserving relational summaries and the cost
  ledger, so it stays an archive, not a delete.

## 7. Tests (tests/Antiphon.Tests, TUnit)

Two suite-wide constraints from CLAUDE.md apply with unusual force here:

- **Retention sweeps are global**, so every test that drives `RunOnceAsync` or a per-table pass
  needs `[NotInParallel]` with **no group key** (the AgentSupervisionTests lesson), and every
  assertion scoped to rows the test created.
- The sweep deletes by age, and the shared testcontainer holds other suites' rows. Other tests
  create rows stamped *now*; retention tests must be the only ones creating back-dated rows, and
  must assert deletion/survival only of their own ids — never "the table shrank by N".

Pin at minimum:

1. A **Running** session's rows survive at any age (even all-rows-stale).
2. A terminal session whose *newest* row is recent keeps **all** rows — no partial trim, even of
   rows well past the cutoff (this is the test that encodes §2).
3. A fully-stale terminal session loses all transcript rows, and `IsWorkingAsync` then reads false.
4. A stale, Stopped session that is some agent's `PersistentSessionId` is excluded.
5. Queue: `Pending` survives at any age; old `Sent` is deleted; an old unsettled Channel-origin row
   survives; an old *settled* Channel row is deleted.
6. Session rows: excluded while an `AgentTask` references the session (either column); deleted once
   the task is gone; and the cascade proof the card asked for — transcripts and queued messages gone
   with it, `RunAttempt.AgentSessionId` nulled.
7. Task trees: a tree with one non-terminal member survives whole; a fully-terminal stale tree
   deletes children-first with events gone and no Restrict violation; a stale *leaf* of a fresh tree
   survives.
8. Audit: the setting now feeds the pass — `FullContent` nulled past cutoff, summary columns and
   younger rows intact.
9. `<= 0` disables exactly that table's pass.

Run with `--property:OutputPath=bin-ret/` (trailing forward slash) while the daemons hold `bin/`.

## 8. Deliberately not done, and why

- **No trimming inside a live (or any) session's history** — §2. If a single always-on session's
  own row count ever becomes the hot-path problem (today the 13 running sessions hold only 4,476
  rows), that needs an ingestion-side design (e.g. sequence-keyed tombstones the dedup can see),
  not a retention query — file a new card then.
- **Cards** — the card's own scope-out stands (archive-over-delete, CARD-0019/0005).
- **Consolidating the existing incident/alert prunes** into `DataRetentionService` — optional
  hygiene, listed here so it isn't mistaken for forgotten.
- **No schema changes, no migration** anywhere in the four slices.

Deploy note: server restart only (`scripts/restart-apphost.ps1`); the first sweep will delete
little (§1) — the visible effect arrives over the following weeks as dead sessions age past 30/90d.
