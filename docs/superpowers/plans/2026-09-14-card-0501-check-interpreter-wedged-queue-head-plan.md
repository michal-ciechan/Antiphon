# CARD-0501: A replayed history must never earn an Enter-only retry, and a wedged queue head must charge, park, and clear itself

Date: 2026-09-14. Stage: Plan (task `800c3f0a`). Verification design is included below
(`## Verification design`), so the next stage is Code. Based on the Investigate report
[docs/investigations/2026-09-14-card-0501-check-interpreter-repeatedly-down.md](../../investigations/2026-09-14-card-0501-check-interpreter-repeatedly-down.md)
and checkout `ad545507` (origin/master at planning time).

## Outcome and scope

The check interpreter has produced no reading since 2026-09-12 17:00 UTC because one queue row is
wedged at the head of its session's queue and nothing in the delivery path can move it. Four
facts combine into the loop; each gets its own fix.

1. **The retry gate is satisfied by history.** A row that has been typed once is retried as
   "Enter only" when `ComposerDeliveryEvidence.HeadFragmentIsVisible` finds any one 10-character
   window of the body's first 40 characters on the rendered screen. Every brief starts
   `[antiphon-task:`, so the previous brief's marker line, replayed by a `--resume`, satisfies it
   for every later brief. The composer is empty; Enter submits nothing.
2. **The composer being tested is not the composer that was typed into.** The row was typed into
   generation `17:06:48`; the screen belongs to generation 78. A composer never survives a
   kill-and-resume, so the body cannot be standing in it, but nothing records or compares the
   generation.
3. **Enter-only failures cost nothing.** `EnterOnlyConfirmLockedAsync` neither increments
   `DeliveryAttempts` nor captures a generation, so the row never parks at
   `MaxDeliveryAttempts` and the always-on kill is declined every time. The cycle repeats every
   60 seconds forever (1,219 cycles on 2026-09-14 alone).
4. **Orphaned briefs accumulate behind it.** 64 later check briefs sit Pending with zero
   attempts. Their tasks are Failed (watchdog) or Canceled, but the standing interpreter's run
   path sets no `ExecutionDeadlineAt`, so `CancelExpiredBriefsAsync` never applies and no path
   cancels a brief whose task is already over.

In scope: a whole-head visibility predicate; a per-attempt generation on the queue row and a
generation gate in front of both Enter-only sites; attempt charging and generation capture on the
Enter-only failure arm; cancellation of briefs whose execution task is terminal; the invariants
and ops docs. No new settings.

Out of scope, deliberately: `SubmitEvidence.IsEmptiedComposer` (Codex/Grok post-submit evidence
keeps the windowed predicate; different card if it needs the same treatment); the dispatcher's
10-minute watchdog killing a standing agent's session (after this fix its brief delivers, so the
arm stops firing; making it cancel the brief row directly is a follow-up); why the single real
typing attempt on 2026-09-12 17:11:47 saw no screen output (investigation uncertainty 1); the
supervisor's backoff reset at 10 healthy minutes; the pre-existing gap that a collapsed
`[Pasted text #N]` body shows no head and therefore never takes the Enter-only arm.

## Ground truth

Verified against `ad545507` on 2026-09-14.

| Claim (card, brief, or a tempting shortcut) | Evidence | Consequence |
|---|---|---|
| Any single 10-char window of the head is enough. | `src/Antiphon.Agents.Pty/ComposerDeliveryEvidence.cs:212-224` (`HeadFragmentIsVisible`) → `FragmentVisible` `:254-266`: windows of `WindowLength = 10` at stride 5 over the first `FragmentSpan = 40` normalised chars, **any one** suffices. The investigation reproduced `"phon-task:"` and `"[antiphon-"` as VISIBLE against the live snapshot. | D-2: the Enter-only decision needs the head **whole**. The windowed predicate stays for `SubmitEvidence`. |
| The Enter-only path never charges an attempt. | `server/Application/Services/SessionMessageQueueService.cs:2059-2150`: no write to `DeliveryAttempts`. Success is pinned as "Enter-only finishes the original attempt" (`DeliveryAttempts == 1`) in `SessionMessageQueueInterruptedAttemptTests` ×3 and `SessionMessageQueueGrokPtyIntegrationTests:589`. | D-3 charges on **failure** only; success keeps the pinned meaning. |
| The Enter-only path declines the kill by construction. | `:2148` passes `capturedGeneration: null`; `HandleDeliveryFailureAsync` `:3729-3745` logs "retained no generation; declining the recovery kill". | D-3 captures the generation before the Enter, exactly as the typed path does at `:1777`. |
| Parked rows do not block the queue. | `:1542-1548`: `deliverable = pending.Where(m => m.DeliveryAttempts < MaxAttempts)`, then `pending = deliverable` and `head = pending[0]` (`:1675`). | Charging is itself the unblock: at `MaxDeliveryAttempts` the head parks and the next row becomes the head. |
| A supervised restart is a resume of the same row; only the generation advances. | `AgentControlService.cs:498` → `SessionGeneration.Next(prior, now)` (`src/Antiphon.SessionRunner.Contracts/SessionGeneration.cs:24-29`): `max(now, prior + 1 µs)`, microsecond-normalised, an opaque equality token (CARD-0502). | D-1: "the composer that took the body is the composer on screen" is a generation equality, and a fresh generation is *proof* the composer is empty. |
| The row does not record which generation an attempt typed into. | `server/Domain/Entities/SessionQueuedMessage.cs`: `LastDeliveryStartedAt`, `LastDeliveryBaselineSequence`, no generation. Precedent for a per-row generation: `MaintenanceAcceptedStartedAt` (CARD-0514). | D-1 adds `LastDeliveryGeneration`; legacy null rows use the attempt clock as a fallback. |
| `CancelExpiredBriefsAsync` would sweep the orphans. | `:1401-1418` requires `ExecutionDeadlineAt <= now && DeliveryAttempts == 0 && ExecutionTaskId != null`. The standing (unrouted) interpreter goes through `SpecialistTaskRunner.RunAsync` (`AgentTaskCheckService.cs:458-467`) → `CreateRunTaskAsync` at `SpecialistTaskRunner.cs:247` with **no** deadline; only the routed `RunWithPolicyAsync` (`:133`) sets one. The 64 rows have null deadlines. | D-4: cancel on the execution task being terminal, not on a deadline. |
| The dispatcher cancels a failed task's brief. | `AgentTaskDispatcher.cs` reads `SessionQueuedMessages` at `:1204`, `:2627`, `:2765` only; `FailAsync` + kill at `:1297-1303`; `CancelIfStillQueuedAsync` (`SpecialistTaskRunner.cs:384`) touches only `Queued` tasks. Nothing cancels a Pending brief of a Dispatched-then-Failed task. | D-4 lives in the queue's flush, which already walks every Pending row of the session. |
| Late-confirm runs before any retry. | `:1537` (`LateConfirmAttemptedMessagesAsync`) precedes the retry branch at `:1728-1740`; `RecoverDeliveryRunLockedAsync` `:2022` does the same for interrupted `Sent` rows. | Unchanged. Transcript identity still outranks every screen test. |
| Both Enter-only sites share the same gate. | `:1738-1740` (Pending, `DeliveryAttempts > 0`) and `:2042-2043` (interrupted `Sent`, null verdict) both call `HeadFragmentIsVisible` then `EnterOnlyConfirmLockedAsync`. | D-1 and D-2 apply at both sites. |
| A brief's first 40 characters identify the task. | `DelegationReportFormatter.BuildBrief` `:161-164` and `BuildBriefPointer` `:737-740` open with `[antiphon-task:{8-hex}] role=… tier=… workspace=…`; normalised: `[antiphon-task:419b8b34]role=Checktier=L` (40 chars, id at 15..22). | D-2 needs no marker knowledge in the Pty library: "whole head" is task-id-specific for every brief and pointer. |
| The typed path stamps before typing for crash safety. | `:1742-1766`: attempts++, `LastDeliveryStartedAt`, baseline stamped and saved before `DeliverAsync`; "a crash between here and the write costs one attempt, which is the safe direction". | D-3 need not stamp first: an Enter-only cycle types nothing, so a crash inside it cannot start an unbounded re-type. |
| `LastDeliveryStartedAt` is a late-confirm floor. | `:1911` (null-baseline wall-clock arm), `:2078-2081` (Enter-only's `unobservableFrom`), `:1984-1994` (interrupted-run grouping), `ParkedMessageSweepService.cs:61`. | D-3 must **not** re-stamp it; the floor must keep pointing at the typing. |
| The fake adapter can show history without a composer body. | `tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs:471-489`: screen = `RenderedScreenOverride ?? rawOutput`, plus `"\n> " + composer` only when the composer is non-empty; `PrimeComposer`, `SwallowSubmits`. | Every replayed-history scenario is expressible with `RenderedScreenOverride`; every held-body scenario with `PrimeComposer`. |
| The seed helper does not know about generations. | `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:403-441`: seeds `LastDeliveryStartedAt = now − 4 min` when `deliveryAttempts > 0`; the harness session's `StartedAt` is the harness creation time, i.e. **later** than that. | The seed helper must default `LastDeliveryGeneration` to the session's current generation, or D-1's legacy fallback would flip every existing Enter-only test to "generation changed". |
| An expired routed brief that was typed once loops without charge. | `:2069-2073`: `SpecialistInputPolicyJson != null && ExecutionDeadlineAt <= now` → `FlushResult.Nothing` before the Enter. | Residual, bounded: the 10-minute watchdog kills and resumes (new generation), then D-4 cancels the row. Not widened here. |

## Decisions

### D-1. An Enter-only retry requires the composer's own generation

Add `SessionQueuedMessages.LastDeliveryGeneration` (`DateTime?`): the `SessionGeneration`-normalised
`AgentSessions.StartedAt` of the process an attempt typed into. Stamped wherever `LastDeliveryStartedAt`
is stamped (`DeliverNextLockedAsync` `:1760`, `SendNowAsync` `:984`), cleared wherever it is cleared
(`BackendUnreachable` refund `:1830`, SendNow `:624`). `DeliverNextLockedAsync` already loads
`sessionGeneration` (`:1546-1549`); reuse it for the stamp. Migration generated with `dotnet ef`
(`AddQueuedMessageDeliveryGeneration`), never hand-authored.

Gate, evaluated at both retry sites before any snapshot is read:

- `SessionGeneration.Equal(row.LastDeliveryGeneration, current)` → same process; proceed to D-2.
- Not equal → the composer that took the body is dead. `DeliverNextLockedAsync`: skip the retry
  branch and fall through to the ordinary typed path (late-confirm has already run). 
  `RecoverDeliveryRunLockedAsync`: take the revert arm (Pending, attempts kept) with a log line
  naming the generation change.
- `LastDeliveryGeneration == null` (rows stamped before this migration, including the live head
  row `95f57091`): `SessionGeneration.Compare(current, row.LastDeliveryStartedAt) > 0` means a resume
  happened after the typing → treat as changed; otherwise unknown → proceed to D-2 as today.

Why a column rather than the timestamp inequality alone: CARD-0502 makes the generation an opaque
equality token, "not a clock-skew or elapsed-time test"; the inequality compares two timestamps of
different meaning and is kept only as the legacy fallback. Why gate before the screen test: a fresh
generation is positive proof the composer is empty, so it must win even when the replayed screen
looks like a held body. Rejected: killing the session again to guarantee a fresh composer (that is
the outer loop this card is ending).

### D-2. The head must be visible whole

New `ComposerDeliveryEvidence.HeadFragmentIsVisibleWhole(screen, body)`: true when the normalised
screen contains the **entire** normalised head fragment (first `FragmentSpan` chars; the whole body
when shorter) as one contiguous substring. Same `Normalize` (whitespace, box-drawing, prompt glyphs
stripped), so ordinary wrapping and trimmed rows still match. Both Enter-only sites switch to it.

For every brief and pointer the first 40 normalised characters contain the task id, so a replayed
marker line, a previous brief of the same role and tier, and a bare closing marker all fail it. The
Pty library learns nothing about task markers.

Rejected: (a) a marker-aware predicate (`[antiphon-task:` + id contiguous) in the Pty library — the
library is provider-neutral and "whole head" subsumes it for marked bodies while also tightening
unmarked ones; (b) a higher window quorum (5 of 7) — still satisfied by a same-role, same-tier
previous brief; (c) a paste-placeholder arm — the row records no paste index. Accepted cost: a ghost
row interleaved inside the first 40 characters would read as "not on screen" and route to D-1's
decision (same generation → the ordinary typed path, which is today's outcome for every collapsed
paste). The measured ghost rows were inside long wrapped tails, never the first row.

### D-3. A failed Enter-only cycle is charged and carries its generation

In `EnterOnlyConfirmLockedAsync`:

- Capture `capturedGeneration = await CaptureSessionGenerationAsync(sessionId, ct)` before the Enter,
  as the typed path does at `:1777`.
- `Delivered` and `Truncated`: unchanged (attempts stay as they were; "Enter-only finishes the
  original attempt" remains true and pinned).
- Any other verdict (`NoTranscriptRecord`, `NoSubmitOutput`, `NoComposerEvidence`, …): increment
  `DeliveryAttempts` on every row of the run **and save** before calling
  `HandleDeliveryFailureAsync(…, capturedGeneration)`, which reloads the rows in its own scope and
  computes `parked` from the count it reads. `LastDeliveryStartedAt`, `LastDeliveryBaselineSequence`
  and `LastDeliveryGeneration` are **not** re-stamped: they describe the typing, and late-confirm
  keys on them.
- Incident text for this arm names the shape: "after an Enter-only recovery: the composer showed
  the body's head but no prompt was recorded".

Effect: `MaxDeliveryAttempts` (3) bounds Enter-only cycles the way it bounds typed ones. Cycle 2
and 3 each charge; at 3 the row parks, `deliverable` skips it, the next row becomes the head. The
always-on kill fires under its existing guards (`!working`, `!allSupervision`, `!preFirstTurn`,
verdict not transport) through the generation-checked `KillGenerationAsync`, so it can never kill a
process newer than the one Enter was pressed into.

Rejected: charging before the Enter — no byte is typed on this path, so the crash-safety reason for
stamp-before-type does not apply, and a successful cycle would then read `DeliveryAttempts == 2`,
breaking `FirstInputDeadline` (`:1445-1447`) and `CancelJustClaimedExpiredBriefsAsync` (`:1431`),
both of which read `== 1` as "the one typing". Rejected: charging but withholding the kill — with
D-1 and D-2 this arm is reached only when *this* generation's composer shows *this* body's head
whole; three Enters producing no prompt there is precisely the wedged-composer shape CARD-0055's
fresh-composer kill exists for, and the ordinary typed path would kill on the next row anyway.

### D-4. A brief whose execution task is over is cancelled at the next flush

`CancelExpiredBriefsAsync` becomes `CancelDeadBriefsAsync(db, messages, currentGeneration, ct)` and
cancels a Pending row with `ExecutionTaskId != null` when either:

- **expired**, exactly as today (`ExecutionDeadlineAt <= now && DeliveryAttempts == 0`; the optional
  task is cancelled as today), or
- **orphaned**: its execution task is terminal (`AgentTaskService.IsSettled(status)`) **and** the
  composer provably does not hold the body: `DeliveryAttempts == 0`, or D-1 says the attempt's
  generation is not the current one. The task row is not touched.

Called from `DeliverNextLockedAsync` (`:1521`), which already walks every Pending row of the
session, so an orphan behind a live head is cancelled in the same pass. `SendNowAsync` (`:979`)
keeps the expired-only rule: send-now is a human decision and may deliberately re-send. Log one
Information line per cancelled orphan naming the task short id, its status, and which clause held.

Why the generation clause: a typed row in the same generation may be standing in the composer;
cancelling it would let the next delivery's Enter submit it under a different row's name (the
CARD-0055 `15c9150e` shape). Such rows stay on the D-2/D-3 recovery path, which now bounds them.

Effect on the live board: at the first stranded sweep after deploy (≤60 s), the head row (task
Failed on 2026-09-12, generation long since changed, legacy null generation → clock fallback) and
the 64 rows behind it (tasks Failed by the watchdog or Canceled, never typed) are cancelled; the
newest Dispatched task's brief is typed into an empty composer. No manual intervention is needed,
but the operator lever exists today: `DELETE /api/sessions/{sessionId}/queue/{messageId}` (row id
prefixed `95f57091`).

### D-5. Detection is parking plus the incident, not a new sink

With D-3 a wedged head cannot be re-evaluated more than `MaxDeliveryAttempts` times; the durable
signals are the row's `DeliveryAttempts` climbing, the `Parked` flag on `GET /api/sessions/{id}/queue`,
the `DeliveryVerificationFailed` incident (Error; Critical when channel-bound) and the existing
`ParkedMessage` attention item. No new incident kind, column, or setting. The invariants doc gets the
rule; the ops doc gets the two-line "wedged head" runbook (inspect, clear).

### Defaults taken

- D-6. Column name `LastDeliveryGeneration`; migration `AddQueuedMessageDeliveryGeneration`.
- D-7. New predicate name `HeadFragmentIsVisibleWhole`; `HeadFragmentIsVisible` untouched.
- D-8. New test class `SessionMessageQueueWedgedHeadTests` (`[Category("Integration")]`,
  `[NotInParallel("MessageQueue")]`) rather than growing `SessionMessageQueueInterruptedAttemptTests`
  past its CARD-0340/0342 remit; the Enter-only success tests there stay as the contrast.
- D-9. `BridgeQueueHarness.SeedPendingMessageAsync` grows `DateTime? lastDeliveryGeneration = null`
  with a sentinel meaning "current session generation" when `deliveryAttempts > 0`, and an explicit
  way to seed a legacy null.

## Slices

### S1 — whole-head predicate (Pty library)

Files: `src/Antiphon.Agents.Pty/ComposerDeliveryEvidence.cs`,
`tests/Antiphon.Agents.Pty.Tests/ComposerDeliveryEvidenceTests.cs`.

Add `HeadFragmentIsVisibleWhole`. Doc comment states the CARD-0501 shape and that the windowed
sibling remains for `SubmitEvidence`.

Tests (all pure, CI tier):

- `Whole_head_is_visible_when_the_brief_stands_in_the_composer` — composer row
  `> [antiphon-task:419b8b34] role=Check tier=Low workspace=Shared`, wrapped after `tier=` → true.
- `A_replayed_marker_of_another_task_is_not_the_whole_head` — the investigation's screen: history
  ending `[antiphon-task:a42e10c4]` / `[antiphon-report:a42e10c4 done]`, composer `> ` empty, body
  for `419b8b34` → whole is **false** while `HeadFragmentIsVisible` is **true** (the contrast is
  asserted in the same test so the defect stays named).
- `A_previous_brief_with_the_same_role_and_tier_is_not_the_whole_head` — history shows
  `[antiphon-task:11111111] role=Check tier=Low`, body id `22222222` → false.
- `Whole_head_survives_wrapping_and_trimmed_rows` — head split mid-token across two rows with
  trailing spaces → true.
- `A_body_shorter_than_the_span_must_be_visible_entirely` — 25-char body, 24 chars on screen → false.
- `Empty_screen_and_empty_body_match_the_windowed_predicate` — false and false.

### S2 — per-attempt generation and the gate

Files: `server/Domain/Entities/SessionQueuedMessage.cs`, generated migration +
`server/Migrations/AppDbContextModelSnapshot.cs`, `server/Application/Services/SessionMessageQueueService.cs`
(stamp `:1760`, `:984`; clear `:1830`, `:624`; gate at `:1728-1740` and `:2036-2043`),
`tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs` (D-9),
`tests/Antiphon.Tests/Application/SessionMessageQueueWedgedHeadTests.cs` (new),
`tests/Antiphon.Tests/Application/SessionMessageQueueInterruptedAttemptTests.cs` (unchanged
assertions, re-run as the contrast).

Tests:

- `Typed_attempt_records_the_session_generation` — after `EnqueueAsync`,
  `row.LastDeliveryGeneration == SessionGeneration.Normalize(session.StartedAt)`.
- `Backend_unreachable_refund_clears_the_generation`.
- `Head_row_typed_into_a_previous_generation_is_retyped_not_Entered` — seed attempts 1, verdict
  `NoTranscriptRecord`, generation = old; advance `AgentSessions.StartedAt` with
  `SessionGeneration.Next`; `RenderedScreenOverride` shows the old marker line; flush →
  `Inputs == [Body, "\r"]`, attempts 2, `Delivered`. Never a bare `"\r"` first.
- `Replayed_marker_of_another_task_does_not_earn_Enter_only` — same generation, screen override
  with a different-id marker, composer empty → typed path (`Inputs == [Body, "\r"]`).
- `Held_body_in_the_same_generation_still_gets_Enter_only` — `PrimeComposer(Body)`, same generation
  → `Inputs == ["\r"]`, attempts 1 (the CARD-0342 contract, unchanged).
- `Interrupted_Sent_row_from_a_previous_generation_reverts_then_retypes` — `Sent`, null verdict, old
  generation, old marker on screen → `Inputs == [Body, "\r"]`, attempts 2.
- `Legacy_row_without_a_generation_uses_the_attempt_clock` — two cases in one test:
  `LastDeliveryStartedAt` older than `StartedAt` → retyped; `StartedAt` older than the attempt with
  `PrimeComposer` → Enter-only.
- Existing `Verdict_less_Sent_with_head_on_screen_sends_Enter_only`,
  `Pending_NoSubmitOutput_with_head_on_screen_sends_Enter_only`,
  `Multi_row_batch_is_recovered_as_one_composed_body` stay green with the D-9 default.

### S3 — charge and generation on the Enter-only failure arm

Files: `server/Application/Services/SessionMessageQueueService.cs` (`EnterOnlyConfirmLockedAsync`
`:2059-2150`, incident text in `HandleDeliveryFailureAsync`),
`tests/Antiphon.Tests/Application/SessionMessageQueueWedgedHeadTests.cs`.

Tests:

- `Failed_Enter_only_recovery_charges_an_attempt` — `PrimeComposer(Body)`, `SwallowSubmits` large,
  attempts 1 → after flush: attempts 2, Pending, verdict `NoTranscriptRecord`, every input `"\r"`,
  `LastDeliveryStartedAt` and baseline unchanged.
- `Third_failed_Enter_only_recovery_parks_and_unblocks_the_queue` — head attempts 2 + one Pending
  row behind it; swallowed Enters → head attempts 3 and `Parked` in the DTO; next flush types the
  second row (`Inputs` contains its body).
- `Failed_Enter_only_recovery_kills_the_generation_it_pressed_Enter_into` — always-on harness,
  swallowed Enters → the session is killed with the captured generation (use the kill observation
  the CARD-0055 tests in `SessionMessageQueueDeliveryVerificationTests` already use) and the
  incident carries the Enter-only wording.
- `Working_session_still_withholds_the_kill_after_a_failed_Enter_only` — `MarkWorkingAsync` →
  charged, not killed.
- `Successful_Enter_only_recovery_still_finishes_the_original_attempt` — cite the four existing
  tests; no new code.

### S4 — cancel briefs of terminal tasks

Files: `server/Application/Services/SessionMessageQueueService.cs` (`CancelExpiredBriefsAsync` →
`CancelDeadBriefsAsync`, call site `:1521`; `SendNowAsync` `:979` keeps expired-only),
`tests/Antiphon.Tests/Application/SessionMessageQueueWedgedHeadTests.cs` (or
`SessionMessageQueueServiceTests` if the AgentTask seeding helpers live there).

Tests:

- `Untyped_brief_of_a_failed_task_is_canceled_at_flush` — seed `AgentTasks` row Failed + Pending
  brief with `ExecutionTaskId`, attempts 0 → Canceled, nothing typed, task row untouched.
- `Typed_brief_of_a_failed_task_from_a_previous_generation_is_canceled`.
- `Typed_brief_of_a_failed_task_in_this_generation_is_left_to_recovery` — `PrimeComposer` →
  Enter-only submits it.
- `Brief_of_a_dispatched_task_is_untouched`.
- `Canceled_orphans_do_not_block_the_next_deliverable_row` — the live shape in miniature: head orphan
  (attempts 1, old generation) + two untyped orphans + one live brief → one flush cancels three
  and types the fourth.
- `Expired_untyped_brief_still_cancels_its_optional_task` — existing behaviour retained.
- `Send_now_does_not_cancel_an_orphan` — the human path stays expired-only.

### S5 — docs

Files: `docs/session-runtime-invariants.md` (amend the CARD-0340 S3 / CARD-0342 bullet at `:234`
and add the CARD-0501 rule: Enter-only requires the attempt's generation and the whole head, a
failed Enter-only charges, a brief of a terminal task is cancelled when its composer is provably
empty), `docs/ops-http.md` (queue section: reading `parked` on `GET /api/sessions/{id}/queue`,
clearing with `DELETE /api/sessions/{id}/queue/{messageId}`), and a one-line pointer from the
investigation doc to this plan.

## Verification design

### Ordinary coverage IDs (Code task 326c349a)

These IDs name the existing S1-S5 checks for the ordinary Code/Review handoff. Commands may be
shared; fresh TRX must enumerate every intended class and every new method/argument variant.

| ID | Coverage | Class / check |
|---|---|---|
| V-1 | S1 whole-head semantics, all six named predicate cases | `ComposerDeliveryEvidenceTests` |
| V-2 | S2 generation stamps, refund, both gates and legacy fallback | `SessionMessageQueueWedgedHeadTests` (S2 methods; Enqueue, SendNow and persisted immediate variants) |
| V-3 | S3 charge, park/unblock, captured-generation kill, working guard | `SessionMessageQueueWedgedHeadTests` (S3 methods) |
| V-4 | S4 terminal-task cancellation, safe composer, expiry, human SendNow | `SessionMessageQueueWedgedHeadTests` (S4 methods; Failed/Canceled/Succeeded and legacy variants) |
| V-5 | Generated nullable timestamp migration and matching model | `dotnet ef` generation; integration fixture migration; model diff inspection |
| V-6 | S5 invariant, queue inspect/clear runbook, investigation pointer | Read-only documentation/diff review |
| R-1 | Ordinary Unit lane | `Antiphon.Tests`: `/*/*/*/*[Category=Unit]` |
| R-2 | Windowed post-submit evidence unchanged | `SubmitEvidenceTests` |
| R-3 | Interrupted late-confirm, snapshot absence and successful Enter-only remain valid | `SessionMessageQueueInterruptedAttemptTests` |
| R-4 | Delivery verification, retry, working/kill and transport invariants | `SessionMessageQueueDeliveryVerificationTests` |
| R-5 | Queue ordering, persistence and transcript behavior | `SessionMessageQueueServiceTests` |
| R-6 | Parked-row recovery | `ParkedMessageSweepServiceTests` |
| R-7 | Task delivery watchdog | `AgentTaskDeliveryWatchdogTests` |
| R-8 | Native Grok delivery including successful Enter-only attempt count | `SessionMessageQueueGrokPtyIntegrationTests` |

Code executes ordinary V/R, then read-only Review. PC-1 through PC-8 and missing-control discovery
remain pending for explicitly commissioned post-land SourceLanding Mutation. This stage ordering
supersedes the original request to execute positive controls during Code. No namespace or full
assembly exception is needed. Backend-unreachable deferral retains its existing uncharged rule;
the charged arm describes failed delivery verdicts after transport acceptance.

Generation-gate controls replay the **exact body** in history, strengthening the original
different-marker fixture so whole-head matching alone cannot make PC-2/PC-3 pass.
PC-1's integration method has both Pending and interrupted Sent variants. PC-8's method has
Enqueue, SendNow and persisted-immediate variants; its named mutant must kill the SendNow case.
The activation observation below is an operator follow-up, not ordinary V/R. The original head
was manually removed before this Code task; do not require an exact 65-row cancellation count.

Run S1 in `Antiphon.Agents.Pty.Tests`, then S2–S4 in `Antiphon.Tests`; never concurrently.
Always build to an alternate output path with a forward slash.

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c501/ -- --treenode-filter "/*/*/(ComposerDeliveryEvidenceTests*)|(SubmitEvidenceTests*)/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c501/ -- --treenode-filter "/*/*/(SessionMessageQueueWedgedHeadTests*)|(SessionMessageQueueInterruptedAttemptTests*)|(SessionMessageQueueDeliveryVerificationTests*)|(SessionMessageQueueServiceTests*)|(ParkedMessageSweepServiceTests*)|(AgentTaskDeliveryWatchdogTests*)/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c501/ -- --treenode-filter "/*/*/SessionMessageQueueGrokPtyIntegrationTests/*"
```

`SubmitEvidenceTests` and the Grok pty test are regression guards for behaviour this card must not
change (windowed predicate for Codex/Grok; Enter-only success keeps attempts at 1). Delete the
`bin-c501` directories before finishing.

Positive controls (red-then-green, method-scoped filters; also the seed list for a Mutation stage):

| PC | Mutation | Must go red |
|---|---|---|
| PC-1 | `HeadFragmentIsVisibleWhole` delegates to the windowed `FragmentVisible` | `A_replayed_marker_of_another_task_is_not_the_whole_head`, `Replayed_marker_of_another_task_does_not_earn_Enter_only` |
| PC-2 | Remove the generation gate at `:1728` (always fall into the screen test) | `Head_row_typed_into_a_previous_generation_is_retyped_not_Entered` |
| PC-3 | Remove the gate in `RecoverDeliveryRunLockedAsync` | `Interrupted_Sent_row_from_a_previous_generation_reverts_then_retypes` |
| PC-4 | Drop the `DeliveryAttempts++` on the Enter-only failure arm | `Failed_Enter_only_recovery_charges_an_attempt`, `Third_failed_Enter_only_recovery_parks_and_unblocks_the_queue` |
| PC-5 | Pass `capturedGeneration: null` again | `Failed_Enter_only_recovery_kills_the_generation_it_pressed_Enter_into` |
| PC-6 | Drop the orphan clause in `CancelDeadBriefsAsync` | `Untyped_brief_of_a_failed_task_is_canceled_at_flush`, `Canceled_orphans_do_not_block_the_next_deliverable_row` |
| PC-7 | Drop the generation clause in the orphan rule (cancel any terminal-task brief) | `Typed_brief_of_a_failed_task_in_this_generation_is_left_to_recovery` |
| PC-8 | Stamp the generation only in `DeliverNextLockedAsync`, not `SendNowAsync` | `Typed_attempt_records_the_session_generation` (SendNow variant) |

Post-land activation check (operator, after `restart-apphost.ps1` and `GET /api/version` shows the
landed SHA): within two minutes, `GET /api/sessions/cea73d57-3072-4cc2-8cb0-ab859e7d3415/queue`
shows no Pending row older than the restart, the server log shows the orphan-cancel lines for the
65 rows, and the next check task on any live delegate settles `Succeeded` for agent `be5d4502`
instead of INTERPRETER DOWN. If the head row somehow survives, the manual lever is the `DELETE`
route above; do not kill the session to clear it.
