# CARD-0135 — a brief that lands in a busy TUI's composer queue is invisible to the delivery watchdog: plan

**Date:** 2026-08-23 · **Card:** CARD-0135 (`4fe908a0-07c1-4336-b45a-084180f2d7ed`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `0b71eac`. Every line number below was re-read out of the code on that
commit.

**Established fact, not re-derived here:**
`docs/investigations/2026-08-23-card-0135-queued-brief-watchdog-investigation.md` (Grok, `0b71eac`).
Its two findings are taken as given: the gap is **real and live-exploitable in current code**, and
the ten historical `queued_command` briefs are **not** evidence of it (they are CARD-0101's shredded
`--session-id` plus CARD-0064's then-blind C4, recovered or failed on an *empty* transcript). This
plan does not re-open either.

**Related:** CARD-0132 (S2 shipped `QueuedUserPrompt`; its S2.4 deliberately kept the kind inert
here, and this card is the follow-up it named), CARD-0077 (`TranscriptPromptSpan` — the shared span
this widens), CARD-0055 / CARD-0024 (transcript-confirmed delivery; "pull before you judge"),
CARD-0056 (a failed launch must not kill work; unclaimed never implies kill), CARD-0068 (the turn
window's upper bound is the next prompt — a cap that must not be computed over a narrower kind set
than the walk-back), CARD-0117 (arm 2 of the same method; **ordering dependency**, see D8),
CARD-0085 / CARD-0127 (`DelegateBindRefusalRecovery`, S4), CARD-0067 (`ChannelReplyDispatcher` —
the same blindness on a different route, scoped out, see "Cards to file").

---

## Verdict up front

**Widen the shared span. The one-line change is the right change — but not for the reason the card
offers, and not on its own.**

1. **The card's framing of the open question is wrong on the code, and the wrongness matters.** The
   card asks whether the watchdog needs a narrower fix because it is a stricter *"prove the brief
   specifically was received"* check than delivery confirmation's *"prove some body was received."*
   `HasTurnPromptSinceAsync` is **not** marker-scoped. It is time-scoped: *"is there any
   non-housekeeping prompt on this session after `DispatchedAt`"*
   (`TranscriptPromptSpan.cs:52-55`, `:78-80`). Delivery confirmation is *body*-scoped — a
   200-character head-window match against one specific body (`PromptSubmissionMatch.IsConfirmedBy`).
   Neither is strictly stronger than the other; they ask different questions over the same rows.
   There is no strictness gap to protect. There is a **kind** gap, and it is the same gap in both.

2. **CARD-0132 S2.4's reason for the inertness does not transfer to this consumer, and that is
   provable rather than a judgement call.** S2.4's trap is that a `queued_command` record's own
   timestamp is the **enqueue** time, measured *earlier* than records that precede it in file order
   (`17:58:06.291Z` written after a `17:58:37.774Z` record — the fixture
   `tests/Antiphon.Tests/Agents/Fixtures/queued-command.jsonl` is that exact triple). Any rule that
   **ranks one record against another record's timestamp** mis-orders it — which is precisely what
   the working/idle rules do, via their backfill timestamp override, and why feeding them this kind
   could report *idle while mid-turn*. `TranscriptPromptSpan` never does that. It orders by
   `Sequence` (drain order, correct) and uses `Timestamp` **only** as a lower bound against
   `dispatchedAt`, an external clock. The trap needs two records to bite; the span only ever
   compares one record to a task row.

3. **The narrow, watchdog-only fix is not merely smaller — it is wrong, and it makes the outcome
   worse.** Settlement's walk-back reads the *same* span (`AgentTaskReplyService.cs:1185-1190`), and
   a queued brief is the **only** prompt row its turn has — the drained `queued_command` attachment
   is not accompanied by a `user` record (measured; the fixture goes attachment → assistant, with no
   user record between). So a watchdog-only fix converts a 10-minute false kill into a multi-hour
   silent strand: the delegate works, reports, `ExtractMarkedTurnAsync` finds no prompt at all
   (`TurnOutcome.Nothing`), nothing settles, no incident is raised, arm 1 is now satisfied so the
   delivery clock waves it through, and the task sits `Dispatched` until `FailOverdueTasksAsync`
   kills it at the role deadline — **with the delegate's real report discarded.** Under this repo's
   own discipline that is the worse of the two failures, not the safer one.

4. **The widening also fixes a live mis-settlement nobody has filed.** Today a queued prompt is
   invisible, so the walk-back reaches *past* it to the brief, the marker gate passes, and the task
   settles on a turn that answered **something else** — a completion note or a human message that
   queued into a busy delegate. Widening turns that silent wrong answer into either a correct
   correlation (the queued row *is* the brief) or an honest `UncorrelatedReport`. Both are strictly
   better; the second is a **behaviour change with a blast radius**, which is what D8's ordering
   dependency is about.

5. **Nothing about kill timing moves.** No timeout is widened, no grace is added, no assertion is
   loosened. This change can only ever add rows to an evidence set that is read to decide whether to
   *withhold* a failure. CARD-0055/CARD-0056's invariants are untouched: the pull-before-you-judge
   round trip stays, the working-kill guard stays, the recovery arm stays, and "unclaimed never
   implies kill" is not in this path at all.

**One sentence:** `TranscriptPromptSpan.LoadAsync` counts `QueuedUserPrompt` alongside `UserPrompt`,
both of its consumers move together because that is the entire reason the type exists, the
working/idle lockstep does not move and is pinned as not moving, and S1 does not ship before
CARD-0117 S1.

---

## What the code does today, re-read on `0b71eac`

### The span

```csharp
// server/Application/Services/TranscriptPromptSpan.cs:52-56
var rows = await db.TranscriptEntries
    .Where(t => t.AgentSessionId == sessionId
        && t.Kind == TranscriptKinds.UserPrompt
        && (dispatchedAt == null || t.Timestamp == null || t.Timestamp > dispatchedAt))
    .OrderBy(t => t.Sequence)
```

Two consumers, and the type's doc comment states the contract in its first sentence — *"Shared by
settlement and the delivery watchdog so the two cannot disagree on what counts as 'this task
started'."*

| Consumer | Call | What it takes from the span |
|---|---|---|
| Delivery watchdog arm 1 | `AgentTaskDispatcher.cs:431-432` → `HasTurnPromptSinceAsync` (`:78-80`) | `TurnPrompts.Count > 0` — a boolean |
| Settlement | `AgentTaskReplyService.cs:1185` → `LoadPromptsInSpanAsync` (`:1397-1399`) | `:1186` the walk-back prompt, `:1190` the `nextPrompt` cap, `:1217` the marker gate over `prompt.Text`, and `span.Notifications` for CARD-0046's subagent wait |

`IsHousekeepingPrompt` (`:88-92`) passes the **literal** `TranscriptKinds.UserPrompt` as each
helper's `kind` argument, not the row's kind — the helpers all hard-gate `kind == UserPrompt`
(`SessionRunnerContracts.cs:210, 252, 301, 365`). Today that literal is incidental. After S1 it is
load-bearing and needs to say so: the span has *already* decided the row is a prompt by kind, and
what it is asking the helpers is a question about **text shape**.

### What a drained queued command actually looks like

From `Fixtures/queued-command.jsonl` (the real `cefed08a` triple, trimmed):

```
queue-operation  17:58:06.292Z   enqueue
user             17:58:37.774Z   tool_result
queue-operation  17:58:37.967Z   remove
attachment       17:58:06.291Z   queued_command  prompt="<the full body>"   <-- enqueue clock
assistant        17:58:45.644Z
```

**There is no `user` record for the drained command.** The attachment *is* the record of the body
reaching the model, and the assistant answers it directly. `TranscriptNormalizer.FromAttachment`
(`src/Antiphon.SessionRunner/TranscriptNormalizer.cs:197-221`) emits exactly one `QueuedUserPrompt`
part from it, carrying `attachment.prompt` verbatim — which for a brief is the full body, marker
included, at its head.

### The gap, in the two places it bites

| Question | Query | Sees a queued brief? |
|---|---|---|
| Delivery confirmation — did this body land? | `SessionMessageQueueService.cs:1514-1522` | **yes** (CARD-0132 S2) |
| Delivery watchdog — did anything land for this task? | `TranscriptPromptSpan.cs:52-55` | **no** |
| Settlement — which prompt did this turn answer? | same rows, same query | **no** |
| Transcript binding C4 | `TranscriptCandidateProbe.cs:190-237` harvests `queued_command` directly | **yes** (CARD-0064) |
| Working / idle (all three lockstep impls) | `SessionMessageQueueService.cs:2079-2082`, `TranscriptWorkingState.cs:47-49`, `transcriptModel.ts:113` | **no, deliberately, and must stay no** |

So binding can bind a session on a queued brief, and delivery can mark it `Sent` — and then the two
checks that decide whether the task lives cannot see it.

### The live shape, end to end

1. A brief is typed into a Claude TUI that is mid-turn — the cleanest trigger being a **warm-pool
   reuse onto a busy composer**, where inherited `UserPrompt` rows all predate `DispatchedAt` and are
   correctly excluded by CARD-0077's bound.
2. Claude queues it; on drain it writes the `queued_command` attachment; the runner normalizes it to
   `QueuedUserPrompt`; the server persists it.
3. `TryFindConfirmingRecordAsync` matches the head window → the queue row goes `Sent`.
4. The delegate does the work and reports. `ExtractMarkedTurnAsync` finds **no** span prompt →
   `TurnOutcome.Nothing` → not settled, no incident.
5. T+10: `FailNeverStartedAsync` pulls (`:425`), `started` is **false** (`:431`).
   `TryRecoverBindRefusalAsync` (`:442`) cannot save it — CARD-0127's JSONL arm requires a
   **user-type** record carrying the marker and there is none, and a first turn with no commits yet
   fails the git arm.
6. Task **Failed**, session **killed**, with the wording *"the brief is marked Sent, but the session
   never wrote a turn prompt for this task"* (`:469`) — which is, word for word, a description of a
   brief that arrived and was worked on.

---

## Design decisions

### D1 — the fix is one query, in `TranscriptPromptSpan.LoadAsync`, for both consumers

`Kind == UserPrompt` becomes `Kind == UserPrompt || Kind == QueuedUserPrompt`. Nothing else about the
span's shape changes: same `dispatchedAt` bound, same `Sequence` ordering, same four-way housekeeping
filter, same `Notifications` split.

**Rejected — a private predicate for the watchdog only** (`HasAnyPromptEvidenceSinceAsync` counting
both kinds, settlement left alone). This is the "narrower fix" the card asks about, and it is the one
concrete alternative that must be named and refused. It breaks the type's stated contract, and its
outcome is verdict §3: the false kill becomes a silent strand in which the delegate's finished report
is thrown away and the task dies hours later on the wall-clock ceiling. Two watchdogs disagreeing
about what "started" means is the exact defect CARD-0077 built this type to end.

**Rejected — widen the walk-back but leave `nextPrompt` (`:1190`) on `UserPrompt` only.** Asymmetric
bounds produce overlapping turn windows: a queued prompt would open a turn it cannot close, so the
following turn's text would be attributed to the earlier prompt. That is CARD-0068's defect inverted,
and it is worse than either consistent choice.

**Rejected — normalize `queued_command` to `UserPrompt` at ingest and synthesize a drain-order
timestamp.** Reopens S2.4's trap in all three lockstep working rules, and the drain timestamp does
not exist in the JSONL — inventing one is fabricating evidence about when the model saw the body.
CARD-0132 introduced a distinct kind precisely so that this option would stay closed.

**Rejected — confirm the watchdog off the `queue-operation: remove` record.** CARD-0132 S2.2 already
settled this: `remove` is ambiguous between *drained into the conversation* and *discarded*.

**Rejected — let the watchdog trust the brief's own `SessionQueuedMessages.Status == Sent` and skip
the transcript question.** The queue row is our own bookkeeping; the transcript is the independent
evidence, and the watchdog exists to audit the first against the second. It also throws away the
`dispatchedAt` bound and the housekeeping filter, so an inherited `Sent` row from a previous task
would answer for this one — which `a_previous_tasks_brief_is_not_this_tasks_queued_evidence`
(`AgentTaskDeliveryWatchdogTests.cs:188`) already exists to prevent.

### D2 — the enqueue clock is the *right* clock for the `dispatchedAt` bound

The bound asks "could this prompt be this task's brief?", and a body typed into the composer **before
this task was dispatched** cannot be — however late it drains. The enqueue timestamp is exactly when
it was typed, so `Timestamp > dispatchedAt` gives the correct answer for a queued row, arguably a
more correct one than the drain time would. Our own brief is enqueued strictly after `DispatchedAt`
(dispatch stamps the row, then the queue types it), on the same machine, from the same wall clock —
even a warm-pool reuse, where that gap is under a second, is positive.

The existing null-timestamp rule is unchanged and applies to queued rows too: an entry with no
timestamp *cannot be placed in time and is KEPT*, leaving the marker gate (settlement) or `started`
(watchdog) to judge it. That is the conservative direction here as well.

**The residual, named rather than hidden:** a body enqueued *before* dispatch that drains *after*
the brief is excluded by the bound and therefore does not cap the report window, so the walk-back can
attribute its answer to the brief. That exposure is not new — it is identical for a `UserPrompt` row
excluded by the same bound — and it is the price of the bound CARD-0077 deliberately added. It is not
widened by this change.

### D3 — `PromptRow` carries its kind; both consumers still treat the two kinds identically

`PromptRow(long Sequence, string? Text, DateTime? Timestamp)` gains `string Kind`. Neither consumer
branches on it — the marker gate is a text match and the cap is a sequence comparison, so a queued
brief satisfies both on exactly the evidence a typed one does. The field exists so the *watchdog can
say which evidence satisfied it* (D6), and so that a future consumer that genuinely needs to
distinguish has to do so explicitly rather than by re-querying.

**Rejected — a `bool IncludeQueued` parameter on `LoadAsync`.** A per-call-site switch is the
disagreement this type forbids, wearing a parameter.

### D4 — no configuration flag

There is no `EnableQueuedPromptEvidence` setting and no escape hatch. This repo's escape hatches
(`FinalMessageGraceSeconds <= 0`, `SubagentGraceMinutes <= 0`) exist where a change can make an
outcome worse and an operator may need yesterday's behaviour back. A flag here would be worse than
useless: the only failure mode worth reverting is settlement's enlarged uncorrelated population
(D8), and a flag that disabled the widening for settlement while leaving it on for the watchdog
would manufacture the exact split D1 rejects. If the widening ever has to be reverted, it is one
`||` in one query and it reverts as a whole.

### D5 — the housekeeping filter still applies to queued rows, and the literal `kind` argument gets a comment

`IsHousekeepingPrompt` keeps passing `TranscriptKinds.UserPrompt` into the four helpers. That is now
a deliberate statement — *"the row is already known to be a prompt by kind; what is being asked here
is a question about text shape"* — and it earns a doc line to that effect, because the alternative
(widening the helpers' own `kind` gates) would push `QueuedUserPrompt` into
`IsRawLocalCommandEcho`'s and `IsTaskNotificationPrompt`'s public contract, which the working/idle
implementations also read. That is the leak S2.4 forbids.

Practically the filter is near-inert on queued rows — `FromAttachment` only emits for
`attachment.type == queued_command` with a non-empty `prompt`, and command wrappers do not arrive
that way — but applying it costs nothing and is the conservative direction if Claude Code ever
queues a slash command.

### D6 — the watchdog says which evidence satisfied it

When `started` is true, arm 1 `continue`s and writes nothing anywhere. That silence is exactly what
made this investigation expensive: there is no way, after the fact, to tell "the watchdog saw a typed
prompt" from "the watchdog saw a queued one." One log line at Information when the *only* evidence is
`QueuedUserPrompt` closes that, and it is the line a future operator will grep for when the next
composer-queue defect appears.

Arm 1's failure reason (`:465-477`) also gets one word of honesty: the Sent-evidence sentence becomes
*"…the session wrote no turn prompt of either kind for this task"*, so a reader can tell a
kind-complete check from the kind-blind one this card is about.

### D7 — the delivery-clock's own doc comment is part of the fix

`DelegationSettings.DeliveryFailTimeoutMinutes` (`DelegationSettings.cs:277-296`) documents the
predicate in prose and was already corrected once, by CARD-0077, when it used to say "zero transcript
entries." It must be corrected again here, or the next reader learns the wrong predicate from the
place most likely to be read. Same for `TranscriptPromptSpan`'s own class comment, which should name
the queued shape and — importantly — **why the S2.4 trap does not apply to it** (verdict §2), so the
next person to touch this does not have to re-derive it from a fixture.

### D8 — ordering: S1 must not land before CARD-0117 S1

Widening the span changes settlement in **two** directions, and only one of them is this card's.

- *The queued row is the brief.* Correlation now succeeds where it previously found nothing. Pure
  gain.
- *The queued row is not the brief* — a completion note from a child, or a human typing into a busy
  delegate's terminal. The walk-back now lands on it, the marker gate fails, and the turn is reported
  `UncorrelatedReport` (`AgentTaskReplyService.cs:1217` → `:90` → `RecordUncorrelatedReportAsync`
  `:163`). That verdict is **correct** — the turn genuinely is not this task's report, and today's
  behaviour of walking past it and settling the task on someone else's answer is a silent wrong
  answer. But `FailNeverStartedAsync` arm 2 (`:480-482`) currently asks whether **any**
  `DelegateReportUncorrelated` incident exists **for the session**, unscoped, and turns it into a
  Failed task.

The population that grows is **nested delegation**: a parent delegate with its own open task,
receiving completion notes from its children while busy — which is precisely the shape that queues
(CARD-0132 measured 43 `QueuedUserPrompt` rows on `cefed08a`, all completion notes and human messages
into a busy caller). This is not a *new* class of failure — a note that lands as an ordinary
`UserPrompt` on an idle parent already does exactly this today, and arm 2's own doc comment names the
benign cause — but it is a larger population of it, and CARD-0117 S1 (one predicate for "an
uncorrelated report about **this task**") plus S4 (the delivery clock stops killing work it cannot
read) are the guard.

**Therefore:** S1 of this plan ships **after** CARD-0117 S1 is on master, or in the same deployment.
Not because it depends on it functionally, but because shipping it first would enlarge an
unscoped-incident hazard that is already known and already being closed. If CARD-0117 stalls, S1 is
still shippable — but only with the D9 test, which pins the enlarged case explicitly, and with the
card noting the exposure.

### D9 — what stays exactly where it is

- **All three working/idle rules.** Untouched, and the existing pin
  (`Queued_user_prompt_is_inert_for_the_server_working_rule`) stays green unedited. It is the test
  that proves the widening did not leak.
- **`AttentionService`'s `NeverStarted` preview** (`:296-300`) keys on `withTranscript` — *any*
  ingested entry — which a queued brief already satisfies. It does not false-positive today and does
  not move. (Its neighbouring `UncorrelatedReport` arm at `:322-325` is CARD-0117 S1's business.)
- **`TaskDeadlinePolicy.ClassifyPhase`** (`:186-200`): a `QueuedUserPrompt` tail falls to
  `_ => (DeadlineKind.Ceiling, ...)` — the *loosest* deadline, never a tighter one. The unrecognised
  kind can only withhold a failure, so this is the safe direction and needs no change. Naming it here
  so a reviewer does not have to re-derive it.
- **`SessionContextUsage`, `ApiErrorRecoveryService`, `CodexSubmitConfirmation`.** Out of scope;
  none of them can fail or kill a task on a missing prompt row. `CodexSubmitConfirmation` is a Codex
  path and `queued_command` is a Claude Code shape.
- **The pull, the recovery arm, the capability withhold, the kill.** Structurally unchanged.

---

## Slices

Independently testable and independently committable. S1 is the fix; S2 is what makes settlement
whole; S3 is diagnosis; S4 is droppable; S5 is verification.

### S1 — the span counts a queued prompt

- `TranscriptPromptSpan.LoadAsync` (`:52-55`): `t.Kind == TranscriptKinds.UserPrompt ||
  t.Kind == TranscriptKinds.QueuedUserPrompt`.
- `PromptRow` (`:39`) gains `string Kind`; the projection at `:57` carries it.
- Class doc comment: a paragraph naming the queued shape, the enqueue clock (D2), and **why S2.4's
  timestamp trap does not reach this type** — sequence orders, timestamp only ever meets
  `dispatchedAt`.
- `IsHousekeepingPrompt` (`:88-92`): the literal-`kind` comment from D5.
- Invert the pin `Queued_user_prompt_is_not_a_turn_prompt_for_settlement_or_the_delivery_watchdog`
  (`SessionMessageQueueDeliveryVerificationTests.cs:225-238`) into its positive form, keeping the
  CARD-0132 back-reference in the comment so the reversal is legible rather than looking like drift.

**Red before:** a task whose only post-dispatch prompt is a marker-bearing `QueuedUserPrompt` is
failed and its session killed at T+10.

### S2 — settlement correlates and caps across both kinds

No code change beyond S1 — `AgentTaskReplyService.cs:1186/1190/1217` read the widened span as-is.
The slice is the **tests and the doc comment** that make the new behaviour deliberate rather than a
side effect, plus the one comment `ExtractMarkedTurnAsync` needs: that the prompt it walks back to
may be a queued row, and that this is the only reason a queued brief's report can settle at all.

Split from S1 so the settlement behaviour change (D8's second direction) is reviewable on its own
diff, and so a bisect can land on it.

**Red before:** a report turn opened only by a queued brief never settles the task.

### S3 — the watchdog names its evidence, and the prose stops lying

- `FailNeverStartedAsync` (`:431`): take the `Result`, not the boolean, so the queued-only case can
  be logged at Information (D6). `HasTurnPromptSinceAsync` stays for any other caller.
- Arm 1's Sent-evidence sentence (`:469`) → "…wrote no turn prompt of either kind for this task".
- `DelegationSettings.DeliveryFailTimeoutMinutes` doc comment (`:277-296`): a sentence on the queued
  shape, in the same register as the CARD-0077 correction already there.

**Red before:** the failure reason and the settings comment both describe a `UserPrompt`-only check.

### S4 — bind-refusal recovery accepts a queued brief as JSONL evidence *(droppable)*

`DelegateBindRefusalRecovery` (`:186-223`) requires `type == "user"` before a needle counts, which
CARD-0127 tightened to stop recovery attaching the wrong file (the `753cdb4e` / Codex false
Succeeded). A brief that exists only as a `queued_command` attachment cannot satisfy it — which is
why the investigation found most of the historical ten would return null today.

Widen `isUserRecord` to *submitted-input record*: `type == "user"`, **or** `type == "attachment"`
with `attachment.type == "queued_command"`. Everything CARD-0127 added stays — C1's
another-known-session gate, C2 cwd, C3 first-timestamp, `JsonlNeedles`' marker-or-bounded-short-id.
The justification is that `attachment.prompt` is *verbatim submitted input*, the same evidence class
as a user record's content and strictly stronger than an assistant record quoting a marker, which is
the thing CARD-0127 was actually excluding.

Reachable only when `started` is false, so after S1 it covers the **unbound** queued brief, not
this card's bound one. Drop it, or file it as its own card against CARD-0127, if the slice budget is
tight — it fixes a different layer.

**Red before:** an unbound session whose on-disk JSONL carries the brief only as `queued_command`
recovers nothing.

### S5 — live verification and card close-out

Dispatch one small task onto a **warm Claude pool delegate that is mid-turn**, so the brief queues,
and confirm from stored rows: exactly one `QueuedUserPrompt` on the session carrying the marker; the
queue row `Sent`; the task **not** failed at T+10 and its session alive; and the task settling
normally on its report. Then update CARD-0135 with what shipped, and file the cards below.

---

## Test coverage

Server-side, `tests/Antiphon.Tests/Application/`. TUnit — `dotnet run --project tests/Antiphon.Tests
--property:OutputPath=bin-card0135/ --treenode-filter "/*/Antiphon.Tests.Application/*/*"`.

### `TranscriptPromptSpan` (new cases, alongside `DelegationUnitTests`' existing housekeeping set)

| # | Case | Assertion |
|---|---|---|
| 1 | Queued row after `DispatchedAt` | is a `TurnPrompt` |
| 2 | Queued row whose **enqueue** timestamp precedes `DispatchedAt`, with a **later** `Sequence` | excluded — D2's bound is the enqueue clock |
| 3 | Queued row with a null timestamp | kept (existing conservative rule, now on both kinds) |
| 4 | **The S2.4 skew, verbatim from the fixture:** a queued row stamped `17:58:06` sitting at a higher sequence than a typed row stamped `17:58:37` | span order is `[typed, queued]` by **sequence** — the timestamp does not reorder anything |
| 5 | A queued row whose text is a `<command-name>` wrapper / continuation prompt / `<task-notification>` | filtered as housekeeping, same as typed |
| 6 | `PromptRow.Kind` | reports the row's real kind on both |

Case 4 is the one that matters most: it is the direct, executable answer to "does CARD-0132's trap
transfer?", and it goes red the day someone adds a timestamp-based ordering to this type.

### Delivery watchdog — `AgentTaskDeliveryWatchdogTests.cs`

| # | Case | Assertion |
|---|---|---|
| 7 | **The card's shape.** Only post-dispatch prompt is a marker-bearing `QueuedUserPrompt` | task stays `Dispatched`, session **not** killed |
| 8 | **Warm-pool reuse.** Inherited `UserPrompt` rows all predate `DispatchedAt`; new brief is queued | not failed — extends `a_reused_session_with_a_real_prompt_after_dispatch_is_left_alone` (`:127`) to the queued kind |
| 9 | **The watchdog is not toothless.** No prompt of *either* kind after dispatch, brief `Sent` | still failed and killed, with the "either kind" wording |
| 10 | A queued row belonging to the **previous** task (enqueued before this `DispatchedAt`) | still failed — the queued twin of `a_previous_tasks_brief_is_not_this_tasks_queued_evidence` (`:188`) |
| 11 | Queued-only evidence | the Information log names it (D6) |

### Settlement — `AgentTaskReplyIntegrationTests.cs`

| # | Case | Assertion |
|---|---|---|
| 12 | Turn opened by a marker-bearing `QueuedUserPrompt`, ended with a report | task `Succeeded`, `Result` is the turn's final message |
| 13 | A queued prompt lands **after** the brief and before the `TurnEnd` | it is the `nextPrompt` cap — the report window closes at it (CARD-0068 discipline across both kinds) |
| 14 | Turn opened by a queued row **without** the task marker, brief typed earlier | `UncorrelatedReport`, **not** a settle on the brief. Comment names the mis-settlement this prevents (verdict §4) |
| 15 | Subagent notifications | `span.Notifications` unchanged — a `<task-notification>` is never a queued attachment; guards CARD-0046 slice 4 |

### Inertness that must stay green, unedited

- `Queued_user_prompt_is_inert_for_the_server_working_rule`
  (`SessionMessageQueueDeliveryVerificationTests.cs:209-222`).
- The client `isWorking` twin (`client/src/features/agents/transcriptModel.ts` tests) and the runner
  `TranscriptWorkingState` cases. **No client or runner change in this card at all** — if either
  needs an edit, the change has leaked and the diff is wrong.
- `TranscriptNormalizerTests`' `queued-command.jsonl` cases — the ingest shape is CARD-0132's and is
  not touched.

### S4, if taken

| # | Case | Assertion |
|---|---|---|
| 16 | Unbound session; on-disk JSONL carries the marker only in a `queued_command` attachment | recovers `Succeeded`, session not killed |
| 17 | The CARD-0127 regressions, re-run | `753cdb4e` (Codex vs a Claude JSONL), another-known-session file, assistant-record card id, C3-refused file — all still refuse |

---

## Deployment and retroactivity

- **Server-only.** `TranscriptPromptSpan` lives in `server/`; the normalizer is not touched, so **no
  session-runner restart** and no transcript-format bump. AppHost restart via
  `pwsh -File scripts/restart-apphost.ps1`.
- **Retroactive by construction**, like CARD-0041's working rules: the span recomputes over stored
  rows on every call, so every `QueuedUserPrompt` already in `TranscriptEntries` becomes evidence the
  moment the server restarts. No migration.
- **The deploy blast radius is currently zero, and that is measured, not assumed.** The investigation
  found all 45 existing `QueuedUserPrompt` rows on three sessions (`cefed08a` 43, `276811ea` 1,
  `9558d35f` 1) and **zero `AgentTasks` pointing at any of them**. Re-run that check immediately
  before deploying — if an open task has since attached to one of those sessions, its settlement
  verdict changes on restart, and the operator should know before it does rather than after.

---

## What this plan does not determine

- **Whether the mechanism has ever fired in production.** It has not, on this database, since
  CARD-0132 S2 shipped — the investigation is explicit that this is a live gap, not an active fire.
  The plan is written to that severity: no hotfix shape, no timeout touched.
- **How often a delegate brief actually queues.** Unmeasured. It requires the target composer to be
  busy at the moment of typing, which warm-pool reuse makes reachable and a fresh launch makes rare.
  S5's deliberate reproduction is the only planned measurement.
- **The size of D8's enlarged uncorrelated population.** Bounded to nested delegation, not
  quantified. Test 14 pins the behaviour; CARD-0117 S1/S4 own the consequence.
- **Whether Grok and Codex TUIs have a composer queue at all, and what they write when they do.**
  `queued_command` is a Claude Code JSONL shape. If either has an equivalent, it is invisible to
  every check discussed here and nothing in this plan would find it.

## Cards to file

1. **`ChannelReplyDispatcher` is blind to a queued prompt on both of its queries**
   (`ChannelReplyDispatcher.cs:188-193` — the prompt it matches a reply against — and `:718-722` —
   the turn-window cap). A channel message typed into a busy composer lands as `QueuedUserPrompt`, so
   the reply cannot be routed, and CARD-0067's design turns that into a **Critical
   `ChannelReplyLost`** incident with a human waiting on a dead line. Same defect class as this card,
   a different route, and a higher severity — it should not be folded in here, and it should not
   wait. `ReviewReplyDispatcher.cs:79, :171` is the same shape on a quieter path.
2. **S4, if dropped** — bind-refusal recovery cannot see a queued brief; file against CARD-0127.
3. **A composer-queue survey per agent kind** (the fourth bullet above), if Grok or Codex sessions
   are ever observed queuing input.

## Card housekeeping

- CARD-0135 moves to Ready with this plan linked, and carries the correction that its own open
  question ("a stricter *prove the brief specifically was received* check") does not match the code:
  arm 1 is time-scoped, not marker-scoped.
- CARD-0132 should carry a note that its S2.4 inertness decision is **partially superseded** — the
  working/idle half stands unchanged and is pinned; the `TranscriptPromptSpan` half is closed here,
  with the reason the trap did not transfer.
- CARD-0117 gains a line recording that CARD-0135 S1 waits on its S1 (D8).
