# CARD-0132 S3 — shrink a completion note the caller already read through a status poll: plan

**Date:** 2026-08-22 · **Card:** CARD-0132 (`e43131a4-6e23-40b2-8a5e-21068a8e6c75`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `e9be40d`; every line/behaviour claim below was read out of the code on
that commit.

**Not a bug fix.** The investigation recorded on the card and in `docs/orchestration-findings.md`
(2026-08-22) established that the three "duplicate" notes were one queue row each, delivered once
each. There is nothing broken to repair. This slice is a noise reduction with a hard safety
property: **content-confirmed, never poll-triggered.**

**Supersedes** `docs/superpowers/plans/2026-08-22-card-0132-polled-completion-note-plan.md`
(committed `f4841a6`, reverted `e9be40d`). That plan reached the same goal by persisting a canonical
`AgentTask.CompletionNote`, extending `AgentTaskDetailDto`, and changing what
`delegate.ps1 -Status` prints so that the polled string and the delivered string were *byte*
identical. Its diagnosis was right — the two strings are not identical today — but the remedy is
larger than the problem: it duplicates the report into a second column, changes a caller-facing
script's output, and churns the `agent-task-detail.json` contract snapshot. §"What exactly gets
hashed" reaches the same guarantee by hashing the *report content* on both sides instead of forcing
one composed body, with no new caller-facing text and no API contract change.

---

## Verdict

Four facts decide the design:

1. **The poll and the note carry the same report but not the same string.**
   `AgentTaskService.GetAsync` (`server/Application/Services/AgentTaskService.cs:404`) returns
   `task.Result` and `task.FailureReason` untouched; `delegate.ps1 -Status` prints a one-line
   summary then `$task.result` verbatim (`scripts/delegate.ps1:128-135`). The queued note is
   `DelegationReportFormatter.BuildCompletionNote` (`DelegationReportFormatter.cs:170`) =
   `header \n\n [warning \n\n] FitReport(report)`. Hashing the composed *bodies* against each other
   can therefore never match. Hashing the *report* matches exactly, because the settle path assigns
   `task.Result = report` (`AgentTaskReplyService.cs:382`) and hands that same `report` to
   `DeliverToParentAsync` (`:503`), and the two dispatcher failure paths hand the same `reason` to
   both `FailAsync` (which writes `task.FailureReason = reason`,
   `AgentTaskDispatcher.cs:1745`) and `BuildCompletionNote` (`:516`, `:935`).

2. **The note's header carries values that move after the note is composed.** `CostUsd` is rewritten
   later by `DelegationCostBackfillService`; the header also holds title, tier, duration and the
   worktree merge outcome. None of it may enter a hash. Hashing the report only makes the volatile
   half structurally unreachable rather than carefully excluded.

3. **The queue deliberately knows nothing about delegation.** At flush,
   `SessionMessageQueueService.DeliverNextLockedAsync` has a `SessionQueuedMessage` whose
   `ConversationKey` is `task:{RootTaskId:N}` — the **root**, shared by every sibling under that
   run. It cannot name the task the note is about. The row must be made self-describing, exactly as
   CARD-0067 made the channel round trip's return leg durable instead of inferring it.

4. **The note body must be the text that gets typed, because CARD-0055/CARD-0024 compare against
   it.** Confirmation, late-confirm, `PromptSubmissionMatch.IsConfirmedBy` and `IsCompleteIn` all
   read `SessionQueuedMessage.Body`. A shrink therefore has to *replace* `Body` before the `Sent`
   stamp — the same discipline CARD-0025's spill already follows
   (`SessionMessageQueueService.cs:818-826`) — and the full report has to survive somewhere else.

The rule, stated once:

> At flush, a **Pending, never-yet-typed, `Origin = Delegation`** row whose recorded report digest
> equals the digest its parent session already polled off that task is delivered as
> `header + [warning] + one short pointer line` instead of the full report. Any mismatch, any
> missing stamp, any earlier delivery attempt: unchanged full delivery.

---

## Ground truth this plan stands on

All reads on master `e9be40d`.

| Fact | Where |
|---|---|
| GET returns `Result` / `FailureReason` untouched | `AgentTaskService.GetAsync`, `:404-422` |
| `-Status` prints `$task.result`, else `"failed: $($task.failureReason)"` | `scripts/delegate.ps1:128-135` |
| `-Status` sends `X-Antiphon-Task-Token` when `ANTIPHON_TASK_TOKEN` is set | `scripts/delegate.ps1:102-105` |
| GET `/{id}` is the only unauthenticated task read; POSTs resolve a caller | `AgentTaskEndpoints.cs:41-47`, `:110-121` |
| Token → `(Task?, SessionId?, Cwd)`; **throws** `ForbiddenException` on an unknown token | `AgentTaskService.AuthenticateAsync:62-84` |
| Note = header + optional warning + `FitReport(report)` | `DelegationReportFormatter.BuildCompletionNote:170-192` |
| **Seven** Delegation-origin enqueue sites; only **three** are completion notes | notes: `AgentTaskReplyService.cs:1097`, `AgentTaskDispatcher.cs:519`, `:938` — briefs/refinements/answers/pool-reuse: `AgentTaskReplyService.cs:244`, `:314`, `AgentTaskDispatcher.cs:1378`, `:2072` |
| The three notes are the only Delegation rows carrying a `ConversationKey` | same lines; the other four pass none |
| CARD-0132 S1's supersession loop, the insertion point for this check | `SessionMessageQueueService.cs:744-780` |
| Late-confirm runs before it and only touches `DeliveryAttempts > 0` rows | `SessionMessageQueueService.cs:727-737` |
| Delegation rows batch by `ConversationKey` with a size budget | `SessionMessageQueueService.cs:790-810` |
| `_delegationSettings` is already injected into the queue service | `SessionMessageQueueService.cs:36`, `:58` |
| `HasCompletionNoteAsync` counts Delegation rows with `Status != Canceled` | `AgentTaskCheckService.cs:230-237` |
| An idle caller gets the note immediately — no poll window exists | `SessionMessageQueueService.cs:212-219` |

**Consequence of the last row:** this only ever fires on the observed shape — a *busy* caller whose
note sat `WhenIdle` for minutes while it polled. An idle caller's note is typed inside the enqueue
call and can never be shrunk. That is correct and needs no special case.

---

## Design decisions

### D1 — the poll hash lives on `AgentTask`, not on the queue row

**Decision: `AgentTask.LastPolledResultHash` + `AgentTask.LastPolledResultAt`.**

The alternative (stamp a `SeenByCallerAt` on the pending Delegation row at poll time, and compare
nothing at flush) is one column cheaper and was seriously considered. Rejected for four reasons:

1. **It depends on an ordering nobody guaranteed.** `AgentTaskReplyService.SettleAsync` commits
   settlement at `:471` (`await db.SaveChangesAsync(ct)`) and only then calls `DeliverToParentAsync`
   at `:503`, which opens *its own scope* and enqueues. A GET landing between those two points sees
   a settled result with no queue row to stamp. The window is sub-second and the failure is benign
   (full delivery), but "the row is always there first" is precisely the class of unstated
   assumption CARD-0067 was written about. Task-side state has no window at all.
2. **A poll is a fact about the task, not about a message.** "This task's report was read by its
   caller at T" stays true whether a queue row existed, was cancelled, was retried, or was pruned.
   `DataRetentionService` prunes queue rows; the task keeps its history.
3. **The decision stays at one point.** CARD-0132 S1 put the supersession decision inside the flush.
   Splitting this one across poll time and flush time would mean two places to reason about, in a
   method that already carries late-confirm, parking, supersession, batching and spill.
4. **It is queryable evidence.** `SELECT "LastPolledResultAt" FROM "AgentTasks"` answers "did the
   caller already read this?" for any future surface, without joining a queue table.

### D2 — the *note's* content is recorded on the queue row

**Decision: `SessionQueuedMessage.SourceTaskId`, `ContentDigest`, `NoteHeader`.**

The card asks for a check on "the note's actual body", not on the task's current state. Comparing
`Hash(task.Result)` at flush against the stored poll hash would be *nearly* the same thing, and
diverges in exactly the cases that matter: a `RetryAsync` on a settled task overwrites `Result`
while a stale note still sits Pending. Recording what the note carries, at the moment it is
composed, removes every such case — the comparison becomes *what the note carries* vs *what the
caller read*, with no third value involved.

- `SourceTaskId` (`uuid`, null) — which delegated task this note reports. `ConversationKey` names
  the **root**, so with five siblings under one run it cannot. Needed to read the poll hash and to
  write the timeline event. Also independently useful: it makes any Delegation row traceable.
- `ContentDigest` (`text`, null) — digest of the report text this note was built from, before
  `FitReport`. Pre-fit deliberately: an excerpted note carries *less* than the caller polled, so
  shrinking it is more justified, not less, and hashing post-fit would make every long report
  mismatch.
- `NoteHeader` (`text`, null) — the exact prefix of `Body` that is not the report: the header line
  plus, when present, CARD-0046's caller-facing warning. This is what makes the shrink lossless.

**Why `NoteHeader` is a stored string rather than an offset or a recomputation.** An offset dies if
`Body` is rewritten (CARD-0025's spill does exactly that). Recomposing the header at flush would
need `workspaceNote` and `warning`, neither of which is stored anywhere reconstructable — the merge
outcome is prose built inside `MergeBackAsync`, and the warning is assembled across
`AgentTaskReplyService.cs:433-452`. Storing the prefix verbatim is the only version that cannot
silently drop a `WARNING: this task was recovered from an unbound session … Do not redispatch`
line, which is the single most dangerous thing in any note this system sends.

### D3 — what exactly gets hashed, and how it is normalized

One helper, `server/Application/Services/DelegationNoteDigest.cs` — pure, static, no database, next
to `DelegationReportFormatter` and under the same charter.

```
public static string Compute(string? reportText)   // lowercase SHA-256 hex, 64 chars
public static string Normalize(string? reportText)
```

`Normalize` does exactly three things and nothing else:

1. `ReplaceLineEndings("\n")` — the note body is normalized this way by `BuildCompletionNote`
   (`:191`) while `Result` is stored as the delegate wrote it. Without this, one CRLF report
   mismatches forever.
2. Strip trailing whitespace from every line — a stray `\r` or a trailing space is not a content
   difference and must not read as one.
3. `Trim()` the whole string — `FitReport` trims (`:200`), the queue trims on enqueue
   (`SessionMessageQueueService.cs:140`), and `Result` may not be trimmed.

It does **not** collapse interior blank lines, lowercase, strip punctuation, or touch interior
whitespace. Anything more aggressive trades a false-mismatch (harmless: full delivery) for a
false-match (harmful: content suppressed that the caller never saw). The asymmetry decides it.

**What is hashed, on each side:**

| Side | Input |
|---|---|
| Poll (`GetAsync`) | `task.Result` when non-empty, else `task.FailureReason` — i.e. the exact report field the GET returns and `-Status` prints. The script's `"failed: "` prefix is client-side rendering and is **not** hashed. |
| Note (`ContentDigest`) | The `report` / `reason` argument passed to `BuildCompletionNote` at that call site, pre-`FitReport`, pre-header, pre-warning. |

Those two are the same string in all three enqueue paths on `e9be40d`
(`task.Result = report` at `AgentTaskReplyService.cs:382`; `task.FailureReason = reason` at
`AgentTaskDispatcher.cs:1745` feeding the same `reason` to the note). Where a future path makes them
diverge, the digests differ and the note is delivered in full — the safe direction, with no code
needed to keep it that way.

**Nothing volatile is reachable by construction.** The header (cost, duration, title, tier, merge
note), the excerpt banner's character counts, and every timestamp live outside the hashed value.
There is no "remember to exclude the elapsed string" rule to forget.

### D4 — the substituted message, and where the full report survives

The shrink replaces the report, never the whole note. `NoteHeader` is kept verbatim, so the header
line and any warning are delivered exactly as they are today, and the report becomes:

```
[task 1a2b3c4d done] CARD-0132 S3 plan · Frontier · 12m03 · $1.842

Report withheld — you already read it: this task's result was returned to your
status poll at 2026-08-22T14:03:11Z and has not changed since (4,812 chars).
Re-read it with: pwsh -File scripts/delegate.ps1 -Status 1a2b3c4d
```

Composed by a new pure static
`DelegationReportFormatter.BuildPolledNoteBody(string noteHeader, AgentTask task, int reportChars, DateTime polledAt)`,
tested directly the way `BuildCompletionNote` already is in `DelegationUnitTests`. Wording rules, in
priority order: it must say the report is **withheld** rather than missing; it must say **why**; it
must carry the poll timestamp so the caller can confirm it really did read it; and it must name the
one command that brings the full text back. It must not exceed a few lines, or the slice defeats
itself.

**Where the original survives — three places, none of them the queue row:**

1. `AgentTask.Result` (or `FailureReason`) — untouched, always, and by charter
   (`AgentTask.cs`: *"Forwarding may excerpt it but this always holds the original"*).
2. The task timeline — a new `AgentTaskEventType.NoteShrunk = 16`, following CARD-0132 S1's own
   precedent of leaving a queryable trace of a message it chose not to send in full. `Detail` names
   the poll time, the digest's first 8 hex, and how many characters were withheld. It does **not**
   duplicate the report: `AgentTaskEvent.Detail` is capped at 4 000 chars in this codebase
   (`AgentTaskDispatcher.cs:1753`) and `Result` already holds the text.
3. An `Information` log line at the swap, matching `AgentTaskCheckService`'s
   `"Check #… {Outcome} for session …"` shape.

The queue row's `Body` **is** overwritten — it must be, or CARD-0055 confirmation and CARD-0024
completeness would compare the typed short line against a stored full report and park every shrunk
note. That is a deliberate, stated loss of one redundant copy, with the canonical copy on the task.

### D5 — who may cause a shrink

**Only the session that will receive the note.** `GetAsync` stamps the poll hash only when **all**
hold:

- `AgentTaskService.IsSettled(task.Status)`;
- the report text is non-empty;
- a caller resolved from `X-Antiphon-Task-Token` whose `SessionId` **equals `task.ParentSessionId`**.

This is the whole guard against the obvious hazard: the web UI's task drawer opens
`GET /api/agent-tasks/{id}` too. With no token it resolves to no caller, so opening the drawer can
never silence an orchestrator's note. A human running `delegate.ps1 -Status` in a plain terminal
(no `ANTIPHON_TASK_TOKEN`) likewise stamps nothing — correct: a human's terminal read is not the
agent's read. And a *different* agent polling somebody else's task cannot alter what the real
recipient receives.

Because `ParentSessionId` is the gate, no `LastPolledBySessionId` column is needed.

**Resolution must be best-effort.** `AuthenticateAsync` *throws* `ForbiddenException` on an
unrecognised token (`:82`). GET `/{id}` is unauthenticated today, and turning a stale token into a
403 would break `-Status` for anyone holding one. The endpoint gets a small local helper that calls
`ResolveCallerAsync` and swallows `ForbiddenException`, returning `null`.

### D6 — the flush-side guard

Inserted in `DeliverNextLockedAsync` immediately after CARD-0132 S1's `Origin = Check` loop
(`SessionMessageQueueService.cs:780`) and before head/run selection, so a shrunk row participates in
batching with its new small body. Same `changed…` flag + single `SaveChangesAsync` shape S1 uses.

A row is shrunk only when every one of these holds:

| # | Condition | Why |
|---|---|---|
| G1 | `Origin == QueuedMessageOrigin.Delegation` | Check rows are S1's; Ui/Channel/Supervision rows have no report. **Not sufficient on its own** — four of the seven Delegation enqueue sites are briefs, refinements, caller answers and pool-reuse bodies typed into the *delegate's* session. G3 is what excludes them. |
| G2 | `DeliveryAttempts == 0` | **Load-bearing.** A row typed once is the baseline for `LateConfirmAttemptedMessagesAsync`, which containment-matches the *stored* `Body` against the transcript. Swapping `Body` after an attempt would compare a short line against a transcript record holding the full report, fail `IsCompleteIn` forever, and park the message. |
| G3 | `SourceTaskId` and `ContentDigest` are both non-null | A row written before this slice, or by a future path that did not stamp them, is not a candidate. Non-retroactive by design and stated as such. |
| G4 | the task is settled and `LastPolledResultHash` is non-null | No poll, no shrink. |
| G5 | `ContentDigest == LastPolledResultHash` (ordinal) | **The content confirmation.** The card's explicit requirement: never shrink because "a poll happened". |
| G6 | `NoteHeader` is non-null | Without it the shrink could not preserve a warning; degrade to full delivery rather than compose a header-less note. |
| G7 | `DelegationSettings.ShrinkPolledCompletionNotes` is true (default **true**) | An operator off switch for a feature whose failure mode is withheld content. |

On a match: `message.Body = DelegationReportFormatter.BuildPolledNoteBody(...)`, add the
`NoteShrunk` event, log. **`Status` is untouched** — the row is delivered, not cancelled, so
`AgentTaskCheckService.HasCompletionNoteAsync` (which counts `Status != Canceled`) keeps seeing it
and CARD-0132 S1's suppression is unaffected.

### D7 — plumbing the three enqueue sites

`DelegationReportFormatter.Note` becomes `Note(string Body, bool Excerpted, string Header)` — the
header block is already composed inside `BuildCompletionNote`, so returning it is a two-line change
and no caller is forced to reconstruct anything. `SessionMessageQueueService.EnqueueAsync` grows
three optional trailing parameters (`Guid? sourceTaskId = null, string? contentDigest = null,
string? noteHeader = null`), so every existing call compiles unchanged. The three Delegation
call-sites each pass `task.Id`, `DelegationNoteDigest.Compute(report)`, and `note.Header`.

Not worth a shared helper: the three sites already duplicate the enqueue-and-swallow shape and
collapsing them is a separate refactor with its own review surface.

---

## Migration

**Yes, one migration** — `AddPolledCompletionNoteShrink`, `server/Migrations/`, generated with
`dotnet ef migrations add` so the designer file and `AppDbContextModelSnapshot` stay consistent.

| Table | Column | Type | Null | Default |
|---|---|---|---|---|
| `AgentTasks` | `LastPolledResultHash` | `text` | yes | none |
| `AgentTasks` | `LastPolledResultAt` | `timestamp with time zone` | yes | none |
| `SessionQueuedMessages` | `SourceTaskId` | `uuid` | yes | none |
| `SessionQueuedMessages` | `ContentDigest` | `text` | yes | none |
| `SessionQueuedMessages` | `NoteHeader` | `text` | yes | none |

`text` rather than `varchar(64)` for the two digests: this repo's string columns are configured
`HasColumnType("text")` throughout (`AppDbContext.cs:1118`), and a length constraint on a hash buys
nothing on Postgres.

**No backfill, no index, no FK.**

- *No backfill*, and it is safe in both directions — unlike CARD-0067's, whose absence would have
  answered a days-old prompt. Every pre-existing row has null `ContentDigest`, fails **G3**, and is
  delivered in full: today's behaviour exactly. Nothing can be wrongly suppressed by the deploy.
- *No index*: both lookups are by primary key (`SourceTaskId` → `AgentTasks.Id`) inside a loop over
  the handful of Pending rows for one session, which already runs under the per-session lock.
- *No FK on `SourceTaskId`*: `AgentTasks` rows outlive nothing here, but a cascade would give the
  queue a delete-time dependency on the task table it deliberately does not have. It is a
  provenance stamp, and a dangling id fails G4 harmlessly.

**`Down`** drops the five columns. No data loss beyond the feature's own bookkeeping.

---

## Slices

Two, independently shippable and reviewable, in this order.

### S3a — record the two facts (no behaviour change)

1. `DelegationNoteDigest` (`Compute`, `Normalize`) + unit tests.
2. Entity + `AppDbContext` config + migration.
3. `Note` record gains `Header`; `BuildCompletionNote` returns it.
4. `EnqueueAsync` gains the three optional parameters; the three Delegation sites pass them.
5. `AgentTaskEndpoints` GET `/{id}` resolves the caller best-effort and passes
   `Guid? pollingSessionId` to `GetAsync`; `GetAsync` stamps under D5's three conditions via
   `ExecuteUpdateAsync` on the two new columns only.

**Nothing shrinks yet.** After S3a the system records who read what and what each note carries,
and behaves identically. Green here is a real checkpoint.

**Why `ExecuteUpdateAsync`:** `GetAsync` reads `AsNoTracking` and a poll is not a state change. It
must not bump `ConcurrencyToken` (that guards dispatcher claims), must not add an event, and must
not publish `AgentTaskChanged` — a status poll that made the board flicker would be its own bug.
Precedent: `AgentTaskCheckService.cs:653`, `AgentTaskDispatcher.cs:1198`.

### S3b — the shrink

6. `BuildPolledNoteBody`.
7. `AgentTaskEventType.NoteShrunk = 16`.
8. `DelegationSettings.ShrinkPolledCompletionNotes` (default `true`).
9. The G1–G7 loop in `DeliverNextLockedAsync`, plus the event and the log line.

---

## Test coverage

The four the brief names, plus the four the guards demand. New file
`tests/Antiphon.Tests/Application/PolledCompletionNoteShrinkTests.cs`, on the shared
`BridgeQueueHarness` (`tests/Antiphon.Tests/TestHelpers/`) that
`SessionMessageQueueDeliveryVerificationTests` already uses — it gives a live session, the queue,
and a `FakeAgentProtocolAdapter` whose `Inputs` are what was actually typed. `[Category("Integration")]`,
`[NotInParallel("MessageQueue")]`, matching that suite. Every assertion scoped to rows the test made
(CLAUDE.md: the fixture Postgres is shared).

**Flush-side (`PolledCompletionNoteShrinkTests`):**

| Test | Shape | Asserts |
|---|---|---|
| `a_note_whose_content_the_caller_already_polled_is_shrunk` | poll hash == row digest | typed body contains the header line and the pointer, **does not contain** the report text; stored `Body` equals the typed body; one `NoteShrunk` event; `Status == Sent` |
| `a_note_whose_content_differs_from_the_poll_is_delivered_in_full` | poll hash = digest("old report"), row digest = digest("new report") | full report typed; no `NoteShrunk` event. **This is the test that proves the guard guards** — it must fail if G5 is deleted |
| `a_never_polled_task_delivers_its_note_in_full` | `LastPolledResultHash` null | today's behaviour, byte for byte |
| `a_note_already_typed_once_is_not_shrunk` | `DeliveryAttempts = 1` | body unchanged (G2 — protects late-confirm) |
| `a_shrunk_note_keeps_the_caller_facing_warning` | `NoteHeader` carries CARD-0046's `WARNING: …` | the warning is in the typed body |
| `an_excerpted_note_still_shrinks_on_a_matching_poll` | report over `ReplyInlineMaxChars` | shrinks (pre-fit digest), and the `[... THIS REPORT IS AN EXCERPT` banner is gone |
| `a_check_origin_row_is_untouched_by_the_delegation_guard` | `Origin = Check` | S1 still owns it; no `NoteShrunk` |
| `the_flag_off_delivers_in_full` | `ShrinkPolledCompletionNotes = false` | G7 |

**Poll-side (extend `tests/Antiphon.Tests/Application/AgentTaskServiceIntegrationTests.cs`):**

| Test | Asserts |
|---|---|
| `a_parent_session_poll_of_a_settled_task_stamps_the_result_hash` | hash == `DelegationNoteDigest.Compute(Result)`, `LastPolledResultAt` set |
| `a_poll_of_an_unsettled_task_stamps_nothing` | both null |
| `a_token_less_poll_stamps_nothing` | both null — the UI drawer case |
| `a_poll_by_a_session_that_is_not_the_parent_stamps_nothing` | both null |
| `a_poll_does_not_bump_the_concurrency_token_or_publish` | token unchanged, no event added |
| `a_failed_task_hashes_its_failure_reason` | the `Result`-null branch |

**Pure-unit (extend `tests/Antiphon.Tests/Application/DelegationUnitTests.cs`):**

- `Compute` is stable across CRLF vs LF, trailing spaces, and leading/trailing blank lines.
- `Compute` differs for two reports differing by one interior word.
- `BuildCompletionNote(...).Header` is a prefix of `.Body`, with and without a warning — the
  invariant the whole shrink rests on. If this ever stops holding, the shrink produces a malformed
  note, so it is pinned directly.
- `BuildPolledNoteBody` names the task's short id and the character count.

**Contract snapshot:** none. `AgentTaskDetailDto` is deliberately unchanged, so
`tests/Antiphon.E2E/Fixtures/agent-task-detail.json` does not move. If a later pass decides to
surface `LastPolledResultAt`, that snapshot has to be regenerated — flagged here so it is a decision
rather than a surprise.

---

## Risks and what is deliberately not done

- **A shrunk note is content the caller does not see again in-terminal.** Mitigated by G5 (it saw
  exactly this content), by keeping the header and warning, by `AgentTask.Result`, by the
  `NoteShrunk` event, and by the off switch. This is the whole risk of the slice and it is worth
  stating plainly in the commit message.
- **Batching interaction.** A shrunk head makes `budget = Math.Max(head.Body.Length, ReplyInlineMaxChars)`
  smaller in the first term only, so the ceiling still governs and *more* sibling notes fit one
  turn. No change needed; worth one assertion if a batching test is cheap to extend.
- **Not retroactive.** Rows queued before the deploy have no digest and deliver in full. S2's report
  already had to explain a non-retroactive gap on this card; saying it up front here is cheaper than
  explaining it after.
- **`-Status` output is unchanged**, and so is the API contract. That is the main way this plan is
  smaller than the reverted `f4841a6`.
- **No new terminal status for the queue row.** CARD-0091 owns that question; a shrink is a
  delivered message, not a new state.
- **Not attempted:** shrinking a note for a caller that read the report some other way (the board
  UI, a log tail, an `.antiphon/task-*.md` file). Only the authenticated status poll leaves evidence
  strong enough to act on.
