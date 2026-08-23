# CARD-0135 investigation — a queued TUI brief is invisible to the delivery watchdog

**Date:** 2026-08-23 (task `11d8fc6b`)
**Card:** CARD-0135 (`4fe908a0-07c1-4336-b45a-084180f2d7ed`)
**Status:** investigation complete. No app code was changed. No fix designed.
**Verified against:** this worktree at `5bb839d` (equals `origin/master` at investigation time),
live Postgres on `antiphon-postgres:17280`, and the Claude JSONL files still on disk under
`%USERPROFILE%\.claude\projects`.

---

## Verdict, in one sentence

The theoretical gap is **real in current code and deliberately pinned**: a `queued_command` brief
is now ingested as `QueuedUserPrompt` (CARD-0132 S2 shipped) and **delivery confirmation can see
it**, but the 10-minute delivery watchdog still asks `TranscriptPromptSpan.HasTurnPromptSinceAsync`,
which filters `Kind == UserPrompt` only. The five (actually ten still-findable) historical cases
are **not** proof of that gap firing: every one of their JSONL files is a Claude-chosen UUID, not
the task's `AgentSessionId`, the watchdog failed or recovered them on an **empty ingested
transcript** (unbound session), and the stacked cause is CARD-0101 (shredded `--session-id` +
stray `"green"` turn) plus CARD-0064's then-blind C4, with CARD-0085 recovery converting some of
those empty-table hits into Succeeded.

---

## 1. Current code — CARD-0132 S2 shipped; the watchdog did not move with it

### 1.1 `QueuedUserPrompt` exists

CARD-0132 S2 landed as `a57e644` (`fix(transcripts): confirm queued Claude prompts`, 2026-08-21;
on `origin/master`). The kind is real:

```155:155:src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs
    public const string QueuedUserPrompt = "QueuedUserPrompt";
```

`TranscriptNormalizer.FromAttachment` (`src/Antiphon.SessionRunner/TranscriptNormalizer.cs:197-221`)
emits exactly one `QueuedUserPrompt` part when `attachment.type == "queued_command"` and
`attachment.prompt` is non-empty. Other attachment types still produce nothing.
`queue-operation` is still not normalized (S2.2: enqueue is not proof of submit).

CARD-0132 S1 (`a244dae`, suppress settled check-ins) is also on master. It does not touch this
predicate.

### 1.2 Delivery confirmation sees the new kind. The watchdog does not.

The one query behind in-window confirm, grace-confirm, and late-confirm:

```1514:1522:server/Application/Services/SessionMessageQueueService.cs
        var texts = await db.TranscriptEntries
            .AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.UserPrompt
                    || t.Kind == TranscriptKinds.QueuedUserPrompt)
                && t.Sequence > baselineSequence)
```

The delivery watchdog's landed-detection check is **not** that query. It is still:

```431:432:server/Application/Services/AgentTaskDispatcher.cs
            var started = await TranscriptPromptSpan.HasTurnPromptSinceAsync(
                _db, sessionId, task.DispatchedAt, ct);
```

`FailNeverStartedAsync` itself starts at `AgentTaskDispatcher.cs:399`. Plan-time line `:424` has
moved to `:431`.

`HasTurnPromptSinceAsync` (`TranscriptPromptSpan.cs:78-80`) is `LoadAsync(...).TurnPrompts.Count > 0`.
`LoadAsync` still filters **only** `UserPrompt`:

```52:55:server/Application/Services/TranscriptPromptSpan.cs
        var rows = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && (dispatchedAt == null || t.Timestamp == null || t.Timestamp > dispatchedAt))
```

A `QueuedUserPrompt` row after `DispatchedAt` is invisible to this span, therefore invisible to
the watchdog, and therefore invisible to settlement's walk-back (`AgentTaskReplyService.cs:1394-1399`
calls the same `LoadAsync`). That is exactly CARD-0132 S2.4 ("the new kind must be inert everywhere
except delivery confirmation"). It is not an accidental miss of S2; S2's plan named this card as
the follow-up and refused to widen the kind into `TranscriptPromptSpan`.

The inertness is pinned, not inferred:

```225:238:tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.cs
    public async Task Queued_user_prompt_is_not_a_turn_prompt_for_settlement_or_the_delivery_watchdog()
    {
        // ...
        span.TurnPrompts.ShouldBeEmpty();
        (await TranscriptPromptSpan.HasTurnPromptSinceAsync(...)).ShouldBeFalse();
    }
```

Working/idle lockstep also ignores the kind, by construction (S2.4 timestamp trap: the record's
own timestamp is enqueue time, which can predate preceding file-order records):

| Surface | File:line | Behaviour |
|---|---|---|
| Server `IsWorkingAsync` | `SessionMessageQueueService.cs:2079-2082` | `Kind != QueuedUserPrompt` excluded from activity |
| Runner | `TranscriptWorkingState.cs:47-49` | skipped as housekeeping |
| Client `isWorking` | `client/src/features/agents/transcriptModel.ts:113` | excluded from activity |

### 1.3 What the watchdog does after a false `started == false`

Still in `FailNeverStartedAsync`, after the pull (`CatchUpTranscriptAsync` at `:425`):

1. `TryRecoverBindRefusalAsync` (`:442`, implementation `:1874-1897`) — CARD-0085 recovery. This
   runs on `!started`, **not** only on an empty `TranscriptEntries` table. JSONL recovery now
   requires a **user-type** record carrying the task marker or bounded short-id
   (`DelegateBindRefusalRecovery.cs:186-223`, `JsonlNeedles` at `:247-251`; CARD-0127, `a8de6ae`).
   A brief that exists only as `queued_command` / `QueuedUserPrompt` does **not** satisfy that arm.
   The git arm still can (any commit on a worktree branch; needle-matching commits on Shared).
2. Capability-mismatch withhold-kill (CARD-0112) (`:448`).
3. Else fail + kill, with the brief-status sentence at `:465-477`. The Sent arm's wording is now
   *"the brief is marked Sent, but the session never wrote a turn prompt for this task"* — the
   exact shape a correctly-bound `QueuedUserPrompt` brief would produce.

The second arm (`:480-482`) still asks whether **any** `DelegateReportUncorrelated` incident exists
**for the session**, with no `DispatchedAt` filter. CARD-0117's plan (`ee9df73`) would scope that
incident the way `AttentionService` already does. **That plan has not been implemented** — only
`docs/investigations/2026-08-23-card-0117-mid-turn-dispatch-drop-investigation.md` and
`docs/superpowers/plans/2026-08-23-card-0117-pool-reuse-compact-and-uncorrelated-scope-plan.md`
landed. Arm 2 is orthogonal to queued-brief blindness; it is recorded here because the brief
asked for a re-read of current `FailNeverStartedAsync` after CARD-0117.

`DelegationSettings.DeliveryFailTimeoutMinutes` is still 10 (`DelegationSettings.cs:278-296`). The
comment there already names `HasTurnPromptSinceAsync` as the predicate, not "zero transcript
entries" (CARD-0077).

### 1.4 Binding can see a queued brief; the watchdog still cannot

CARD-0064 taught `TranscriptCandidateProbe` to harvest `queue-operation` / `queued_command` into
C4's prompt list **without** going through the normalizer
(`TranscriptCandidateProbe.cs:190-237`). `RememberPromptText` still keeps only `Kind == UserPrompt`
parts from the normalizer (`:208-213`), so the new `QueuedUserPrompt` kind does not feed C4; the
harvest path does. A correctly-bound session is therefore a reachable state for a queued brief.

---

## 2. The gap is currently exploitable, independent of the historical cases

On a session that **is** bound, after CARD-0132 S2:

1. A brief typed into a busy Claude TUI lands as `attachment.type = "queued_command"`.
2. The runner normalizes it to `QueuedUserPrompt` and persists it.
3. `TryFindConfirmingRecordAsync` confirms it → the queue row is `Sent`.
4. Ten minutes later `FailNeverStartedAsync` pulls, then `HasTurnPromptSinceAsync` returns false
   (no `UserPrompt` after `DispatchedAt`).
5. CARD-0085 JSONL recovery will not save it unless a later **user** record also carries the
   marker (CARD-0127). A live first turn with no commits yet also fails the git arm.
6. The task is Failed and the session is killed, with the Sent/no-turn-prompt wording.

The cleanest current shape is a **warm-pool reuse onto a busy Claude composer**: inherited
`UserPrompt` rows are older than `DispatchedAt` so CARD-0077's span correctly ignores them; the
new brief exists only as `QueuedUserPrompt`; `started` is false. A fresh launch after CARD-0101
was fixed (no stray `"green"` `UserPrompt`) is the same if the composer is already busy (trust
dialog leftover, mid-turn, overlay).

This path has **not** been observed as an ingested `QueuedUserPrompt` on any **delegate** session
in the live DB. The 45 current `QueuedUserPrompt` rows sit on three sessions only, all
orchestrator/standing rather than task delegates:

| Session | Rows | What they are |
|---|---|---|
| `cefed08a` | 43 | completion notes and human messages into the caller TUI (CARD-0132's original duplicate-note evidence) |
| `276811ea` | 1 | resume system note |
| `9558d35f` | 1 | resume system note |

Zero `AgentTasks` point at those sessions. S2 is documented as non-retroactive (the tailer does
not re-read skipped `attachment` lines), which is why the historical delegate JSONLs never
produced `QueuedUserPrompt` rows.

The gap is still live in the code. The pin test will fail if someone "fixes" it by accident in
the wrong direction, and will stay green if the watchdog remains blind.

---

## 3. Historical cases — the JSONL files are not those tasks' session ids

The CARD-0132 plan (and this card, copied from it) said five briefs appeared as `queued_command`
and their tasks all hit the 10-minute watchdog (2 Failed, 3 CARD-0085 recovery). It listed no
ids and flagged session-id binding as unconfirmed.

A scan of `%USERPROFILE%\.claude\projects\**\*.jsonl` for `"type":"queued_command"` whose prompt
contains `[antiphon-task:` found **ten** files still on disk. All ten tasks still exist in
Postgres. For **every one**, the JSONL filename is a Claude-chosen UUID; the
`AgentSessionId`-named file in the same project directory **does not exist**.

| Task | Dispatched (UTC) | `AgentSessionId` | JSONL stem (brief lives here) | Outcome at T+10 min | Ingested rows on the *session* today |
|---|---|---|---|---|---|
| `45fd150a` | 2026-08-17 17:43 | `6b94703f` | `e842cc93` (`C--src-Antiphon`) | **Failed** | 0 |
| `9a5b93a3` | 2026-08-18 21:05 | `396f32aa` | `69d97268` (worktree) | **Failed** (CARD-0085's named incident) | 0 |
| `db49e6fa` | 2026-08-19 08:48 | `6867dc7c` | `bd07230d` (`C--src-Antiphon`) | **Failed** | 0 |
| `861c4f19` | 2026-08-20 02:46 | `5409c537` | `a450d4b3` (worktree) | Recovered | 103 / 1 `UserPrompt` / 0 queued |
| `c6bc61f7` | 2026-08-20 03:30 | `a96a2b74` | `ad816a29` (worktree) | Recovered | 427 / 1 / 0 |
| `d2477fd1` | 2026-08-20 03:44 | `a3fab8b6` | `0d1e4393` (worktree) | Recovered | 261 / 1 / 0 |
| `ec9031d4` | 2026-08-20 03:58 | `42edbea6` | `aba82546` (worktree) | Recovered | 159 / 2 / 0 |
| `29faba7d` | 2026-08-20 04:30 | `d8c32379` | `49f47739` (`C--src-Antiphon`) | Recovered | 0 |
| `1a1b6b7b` | 2026-08-20 05:49 | `b42dc25b` | `c38831de` (`C--src-Antiphon`) | Recovered | 0 |
| `c0097a9b` | 2026-08-20 07:30 | `013c4ef8` | `c223dc5c` (worktree) | Recovered | 374 / 1 / 0 |

The card's "five / 2 Failed / 3 recovered" is a **subset or snapshot**; the currently-findable
set is 3 Failed + 7 Recovered. None of the JSONL stems above are rows in `AgentSessions`.

The three Failed reasons are the **pre-CARD-0077** empty-table wording, stored on
`AgentTask.FailureReason`:

> Boot prompt was never delivered: 10 minutes after dispatch the session has **zero transcript
> entries** (the brief is marked Sent, but the session never wrote a transcript).

That is not the current `HasTurnPromptSinceAsync` / "no turn prompt since this task was
dispatched" sentence. They were judged on an unbound session, not on a bound `QueuedUserPrompt`.

In the JSONL files themselves, the brief is almost never a `type: "user"` record:

| Task | `queued_command` + marker | `type:user` + marker |
|---|---|---|
| `9a5b93a3` | 1 | **0** |
| `861c4f19` | 1 | **0** |
| `45fd150a` | 1 | **0** |
| `db49e6fa` | 1 | **0** |
| `c0097a9b` | 1 | **0** |
| `d2477fd1` | 1 | **0** |
| `ec9031d4` | 1 | **0** |
| `29faba7d` | 1 | **0** |
| `c6bc61f7` | 1 | 1 |
| `1a1b6b7b` | 1 | 3 |

CARD-0064's named case `d52298ac` / session `8fb1c60e` is **partially gone**:
`8fb1c60e.jsonl` is not on disk. The CARD-0085 recovery pointer is
`C--src-Antiphon\d12e3c6d-….jsonl` (that stem is also **not** an `AgentSessions` id). That file
still contains both a `queued_command` with `[antiphon-task:d52298ac]` and later user records
with the marker. Postgres: Recovered at 20:36:36Z (T+10.1 min), 1152 ingested rows on
`8fb1c60e` *today* (bound later; CARD-0064 measured bind at 21:15 via an unrelated `/compact`).

S2 is not retroactive, so none of these historical sessions have `Kind = QueuedUserPrompt` rows.

---

## 4. Alternative explanation: unbound transcript (CARD-0101 + CARD-0064 + CARD-0085), not watchdog-blindness on a bound queued brief

The historical cluster is the same stacked failure CARD-0101 already measured
(`docs/investigations/2026-08-20-delegate-command-line-shred.md`, task `1a1b6b7b` — itself one
of the ten JSONL hits):

1. **`--session-id` never reached Claude.** From 2026-08-17 07:21 UTC (`28afb5f`, the
   `delegate-basics` bundle with an embedded `"`), `ModernConPtyConnection.BuildCommandLine`
   doubled quotes and `CommandLineToArgvW` shredded `--append-system-prompt`, swallowing
   `--session-id` into a later argv slot. Claude picked its own conversation id. That is why
   `<AgentSessionId>.jsonl` is missing for every row in the table above, and why the brief lives
   in a file whose stem is not in `AgentSessions`.
2. **`"green"` became the first turn.** The same shred left `green` as a positional (argv[8]).
   Claude submitted it. The composer was then busy. The actual brief was queued and written as
   `queued_command`. CARD-0085's `9a5b93a3` investigation already recorded the first user record
   as `"green"` (5 chars, below `MinMatchChars`). This is not a coincidence with CARD-0135; it is
   CARD-0101's measured mechanism.
3. **C4 could not see the queued brief** until CARD-0064's harvest (`94947f1`). CARD-0101 also
   recorded that the deployed session-runner at the time predated that fix. Bind refused →
   `TranscriptEntries` stayed empty → the watchdog's then-predicate ("zero transcript entries")
   fired at 10 minutes.
4. **CARD-0085 recovery** (`DelegateBindRefusalRecovery`) then converted empty-table hits into
   Succeeded once it shipped, by attaching whichever JSONL passed C2/C3 and contained a needle —
   including `queued_command` / assistant / any-record matches. That is why 7 of the 10 are
   Recovered with a pointer at the Claude-UUID file, and why 3 that predated recovery stayed
   Failed. CARD-0126/0127 later tightened that JSONL arm (kind gate, C1 against other
   `AgentSessions` ids, user-type + marker/short-id only, `RecoveredAt`). Those tightenings
   **do not explain** the original empty-table watchdog hits; they explain how recovery could
   attach a *wrong* file (`753cdb4e` / Codex / `cc704d7c` is the measured false Succeeded,
   CARD-0127). `753cdb4e` has **zero** ingested rows and is AgentKind Codex — it is not a
   `queued_command` Claude brief.

CARD-0126/0127/CARD-0085 are therefore the right **alternative for the observed watchdog
outcomes** (empty table → fail or recover), and CARD-0101 is the right **cause of the unbound
JSONL identity**. CARD-0135's queued-brief-invisible-to-`HasTurnPromptSinceAsync` mechanism is
real, but it is **not what those ten rows demonstrate**. They never reached a bound
`QueuedUserPrompt` row for the watchdog to ignore.

A `queued_command` brief *did* contribute to the bind failure (C4 blindness, CARD-0064), which
is upstream of the empty table. That is a different bug, and it is the one CARD-0064's harvest
already targeted. The watchdog gap this card describes is one layer up: even after bind and
ingestion succeed, `HasTurnPromptSinceAsync` still would not count the row.

---

## 5. What remains uncertain

- **Which five the CARD-0132 plan counted.** Ten marker-bearing `queued_command` briefs are
  still on disk; the plan named no ids. The "2 Failed / 3 recovered" split does not match the
  currently-findable 3/7. Nothing material hangs on the count: every findable case has the
  same session-id mismatch and the same empty-table watchdog wording or CARD-0085 recovery.
- **`8fb1c60e.jsonl` (CARD-0064's original bind-storm file) is gone.** The recovery pointer
  `d12e3c6d-….jsonl` remains. Cannot re-read the 20:26Z enqueue/remove/attachment triple from
  the session's own file.
- **No post-S2 delegate brief has been observed as an ingested `QueuedUserPrompt`.** The
  currently-exploitable path in §2 is code-true and test-pinned, not live-incident-true on
  this database. Confirming it in production needs a bound Claude session whose first
  post-dispatch prompt is only `QueuedUserPrompt` (warm-pool reuse onto a busy composer is
  the shape to watch).
- **Whether CARD-0127's user-type JSONL rule would still recover the historical files.** Most
  of the ten have **zero** `type:user` records carrying the marker, so the JSONL arm would
  return null today. Worktree rows would still recover via the git arm if the branch had
  commits; Shared rows with no matching commit would now take the honest Failed. That is a
  CARD-0127 consequence, not evidence about CARD-0135.

Nothing further from the live DB or remaining JSONL would distinguish "the watchdog ignored a
bound `QueuedUserPrompt`" — those rows were never ingested under that kind.

---

## Citations (current `5bb839d`)

| What | Where |
|---|---|
| Kind constant | `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:155` |
| Normalizer | `src/Antiphon.SessionRunner/TranscriptNormalizer.cs:197-221` |
| Watchdog entry | `server/Application/Services/AgentTaskDispatcher.cs:399` |
| Landed-detection call | `AgentTaskDispatcher.cs:431-432` |
| Span filter (UserPrompt only) | `server/Application/Services/TranscriptPromptSpan.cs:52-55, 78-80` |
| Recovery before fail | `AgentTaskDispatcher.cs:442`, `DelegateBindRefusalRecovery.cs:186-223, 247-251` |
| Arm-2 incident (still session-wide) | `AgentTaskDispatcher.cs:480-482` |
| Delivery confirmation (sees queued) | `SessionMessageQueueService.cs:1517-1518` |
| Working-rule exclusion | `SessionMessageQueueService.cs:2079-2082`; `TranscriptWorkingState.cs:47-49`; `client/src/features/agents/transcriptModel.ts:113` |
| Pin test | `tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.cs:225-238` |
| C4 harvest (bind can see queued) | `src/Antiphon.SessionRunner/TranscriptCandidateProbe.cs:190-237` |
| Timeout comment | `server/Application/Settings/DelegationSettings.cs:278-296` |
| CARD-0132 S2 (shipped) | `a57e644` |
| CARD-0132 S2.4 / this card's origin | `docs/superpowers/plans/2026-08-21-card-0132-stale-checkin-duplicate-note-plan.md` §"Deliberately not in scope" |
| CARD-0117 (plan only) | `ee9df73` |
| CARD-0127 JSONL tighten (shipped) | `a8de6ae` |
| CARD-0101 shred | `docs/investigations/2026-08-20-delegate-command-line-shred.md` |
| CARD-0064 queued C4 | `docs/superpowers/plans/2026-08-19-card-0064-transcript-bind-storm-plan.md` |
| CARD-0085 recovery | `docs/superpowers/plans/2026-08-19-card-0085-false-negative-delivery-plan.md` |
| CARD-0126/0127 | `docs/superpowers/plans/2026-08-21-card-0126-0127-unbound-recovery-plan.md` |
