# CARD-0243 — four settlement tests assert the pre-CARD-0159 contract: verdict and fix plan

**Card:** CARD-0243 · **Task:** 0841d6b2 (Plan, Frontier; retry of d75a5e82, killed by a 429) ·
**Date:** 2026-09-02 · **Code re-read and tests re-run on:** `e0385e95` (master)
**Related:** CARD-0159 (closing report line; design at
`docs/superpowers/plans/2026-08-30-card-0159-settlement-positive-evidence-plan.md`), CARD-0248
(settle-anyway gated on a delivered nudge and a later boundary), CARD-0294 (unmarked idle after
the nudge → `Blocked`), CARD-0302 (Check role classified first), CARD-0047 (the check sweep and
the caller-turn control pair), CARD-0084 S6 (the Grok end-to-end capstone), CARD-0230 (why that
capstone sat red before).

This is a design document. No production code was written for it. The four tests were run at
HEAD to re-establish the failure list; nothing else was changed.

## Verdict up front

1. **The test assertions are stale. CARD-0159 S2 deliberately made "a marked prompt plus any
   assistant text" insufficient for settlement, for every non-Check task on a live session, and
   the design doc says so in its own table (§4.2: *"none, task not yet nudged, session live →
   not settled"*). There is no carve-out for a sub-orchestrator's own task, a standing agent's
   session, or a self-owned task in the design, and none was intended: CARD-0248 and CARD-0294
   then built two more layers on exactly that two-step contract (delivered-nudge gate; Block
   after five idle minutes). Reverting the classifier for "caller-owned" tasks would undo three
   shipped cards to satisfy tests written before the contract existed.**

2. **All four failures are one mechanism**, reproduced at `e0385e95` this pass:
   `ClassifyReportAsync` (`AgentTaskReplyService.cs:2099-2167`) sees a live session, no verdict
   token, no trailing `?`, `ReportNudgedAt == null` → `NudgeForClosingLineAsync` → returns null.
   The test then reads `Dispatched`. The Grok capstone's second symptom from the card
   ("SingleAsync more than one element") is the *same* nudge landing as a second
   `SessionQueuedMessages` row on the delegate session before the test's step-4 `SingleAsync`
   runs — a race the test loses some runs and wins others (this pass it won; the 60 s settle
   wait then failed instead). Not a second bug.

3. **The "caller/self-owned task" the card worries about is a test-fixture shape, not a
   production dispatch path.** `SeedCallerTaskAsync` models a sub-orchestrator whose own task
   is on the session that later receives a check note about its subordinate (CARD-0047's
   positive/negative control pair). In production that task is a normal `Kind=Orchestrator`
   dispatch, typed through `BuildBrief` with the full `ReportingContract` — including the
   orchestrator roll-up paragraph and the closing-line instruction. Since CARD-0159 deployed
   (2026-08-30) the live database holds **zero** tasks with `AgentSessionId == ParentSessionId`
   and zero `Kind != Worker` tasks, so no self-owned settlement has happened to regress. There is
   no "self-dispatch prompt template" to fix because there is no self-dispatch template.

4. **Production evidence says the contract works, and the nudge is rare.** Tasks dispatched on
   or after 2026-08-30 (read-only query, this pass): 284 settled `Marked` (221 + 57 Succeeded, 6
   Blocked), 2 `UnmarkedAfterNudge`, 1 `FinalMessageMissing`, 8 `Exempt` (Check role), and 2
   Marked *after* a nudge (the nudge did its job). Roughly 1 % of settlements needed the second
   step. The card's "EVERY plain-language completion … will now cost an extra idle-turn round
   trip" describes a non-compliant delegate, which the data shows is the exception.

5. **Fix scope: tests and the Grok fake only. No production change, no new settings knob.**
   Four test edits (append the closing token where a compliant delegate would have written it,
   using the same helper shape `AgentTaskReplyIntegrationTests.ApplyClosingVerdict` already
   established for this exact migration), one opt-in fakegrok knob so the capstone's *launched*
   delegate honours the contract it is given, one contract test for that knob, and two new pins so
   the two-step contract is asserted in the caller-owned and standing-agent shapes and nobody
   "fixes" these tests back. Estimate 2–3 h for a Codex/Grok build delegate.

6. **One residual finding, out of this card's scope (§8):** CARD-0248's "boundary predates the
   ask" gate compares the boundary's `CreatedAt` with the nudge's `SentAt`. A delegate that
   answers the nudge inside one tailer-poll interval stores its `TurnEnd` *before* the confirm
   loop stamps `SentAt`, and that boundary is then refused for ever — and CARD-0294 cannot Block
   it either, because it only Blocks on the *nudged* boundary. The fakegrok capstone hits this
   deterministically (the screen capture in §2.3 shows the nudge delivered, answered unmarked,
   and no settle in 60 s). Real models are unlikely to answer in under ~2 s, so this is a
   follow-up card, not a blocker; the fix here (a marked reply) sidesteps it.

## 1. What the code does today

### 1.1 The classifier, arm by arm (`AgentTaskReplyService.cs:2099-2167`, HEAD)

| Arm | Result | Card |
|---|---|---|
| `Role == Check` | `ClassifyCheckReport` — never nudged (`Exempt` unless a `done`/`failed` token) | CARD-0302 |
| verdict `done` | `Succeeded`/`Marked` (after the CARD-0286 Code-Worktree zero-progress probe) | CARD-0159 |
| verdict `blocked` / `failed` | `Blocked` / `Failed`, `Marked` | CARD-0159 |
| `LooksLikeAQuestion(body)` (last two lines end `?`) | `Blocked`, `QuestionHeuristic` | pre-0159, kept |
| session live, `ReportNudgedAt == null` | **`NudgeForClosingLineAsync`, return null — task stays as it was** | CARD-0159 S2 |
| session live, already nudged | settle-anyway only if the nudge row has `SentAt`, the boundary is later than the nudged one **and** stored after `SentAt`, and (text-less boundary) the 240 s response window has passed; otherwise null | CARD-0248 |
| session dead | `Succeeded`, `UnmarkedAfterNudge` / `FinalMessageMissing` | CARD-0159 S2 |

`NudgeForClosingLineAsync` (`:2228-2270`) records `ReportNudgedAt` and the nudged boundary's
sequence, writes a Warning event, enqueues one marked WhenIdle message on the delegate session
(`"[antiphon-task:id] Your turn ended without the closing report line…"`), stores the queue
message id, and (CARD-0294 S3) queues a `[task id waiting]` note to the parent session.
`BlockUnmarkedWaitingAsync` (`:2310-2385`) Blocks the task as `UnmarkedWaiting` if the session
sits idle on the **same** boundary for `UnmarkedWaitingMinutes` (5).

So the card's summary is accurate as far as it goes — settlement needs a verdict token, or a
second unmarked boundary after a delivered nudge, or a dead session — and the "or never" clause
is not quite right: an idle nudged session Blocks after five minutes (CARD-0294), it does not
sit `Dispatched` for ever. The one shape that *can* strand is §8.

### 1.2 Every prompt producer that carries the task marker, and whether it teaches the closing line

| Producer | Marker | Closing-line instruction |
|---|---|---|
| `DelegationReportFormatter.BuildBrief` (`:156-191`) — every dispatch, spawned, pooled or standing (`AgentTaskDispatcher.FitBriefForTyping` callers `:2413`, `:2551`, `:3552`) | yes | **yes** — `ReportingContract(task.Id, task.Kind, …)` for every role but Check; `CheckReportingContract` for Check |
| `BuildBriefPointer` (`:478`) — the spilled-brief pointer (Grok, long briefs) | yes, both ends | "The full reporting contract is in the brief above — read it there." — the spill file has it |
| `AnswerAsync` blocked body (`AgentTaskReplyService.cs:297`) | yes | not restated; the delegate already holds the contract from its brief in the same session |
| `BuildRefinementPointer` (`:603-640`) | yes | not restated, same reasoning; "When you report, open with one line saying the refinement…" |
| `NudgeForClosingLineAsync` | yes | **yes** — names the exact token |
| Check note (`AgentTaskCheckService`) | **no, by design** (CARD-0047 §1.4) | n/a — must not settle anything |

There is no dispatch path that types a marked prompt without the delegate having received the
contract. In particular the `Kind == Orchestrator` brief carries the roll-up paragraph *and* the
closing line (`ReportingContract` `:244-281`), and `server/Bundles/orchestrator.md:31-34` tells a
sub-orchestrator what the token means when it reads its own delegates' notes.

## 2. The four failures, reproduced at HEAD

Build and runs this pass (alternate output `bin-c243/`, deleted afterwards):

| Class | Result |
|---|---|
| `AgentTaskCheckSweepTests` | 32 run, 1 failed: `the_same_caller_turn_DOES_settle_when_the_prompt_carries_its_own_marker` — `parent.Status should be Succeeded but was Dispatched` |
| `AgentTaskStandingAgentDispatchTests` | 9 run, 1 failed: `a_marked_turn_on_the_standing_session_settles_the_pinned_task_and_leaves_the_agent_alone` — `Succeeded but was Dispatched` |
| `GrokDelegateEndToEndTests` | 2 run, 2 failed: the Grok capstone (`the delegate's finished turn never settled the task`, 60 s) and the Claude control (`Succeeded but was Dispatched`) |

### 2.1 `AgentTaskCheckSweepTests.the_same_caller_turn_DOES_settle_…` (`:479-495`)

Seeds a caller session (`SessionStatus.Running`, `EndedAt` null → `IsSessionLiveAsync` true), a
`Kind=Orchestrator, Role=Plan` task on that session with its marked brief, then one turn whose
prompt carries the marker and whose reply is `"Chunk owned: three delegates ran, all merged."`
and calls `OnTurnEndAsync` directly. Marker gate passes; no token; no `?`; live; not yet nudged
→ nudge, null. The harness's `SeedTurnAsync` (`:1263-1310`) has no closing-verdict parameter —
it predates CARD-0159 and was not in CARD-0159's verification list.

Its partner `a_parent_reacting_to_a_check_note_does_not_settle_its_own_task` (`:445-475`) still
passes for the reason it always did: the check note carries no marker, the walk-back refuses the
turn at the marker gate (`ExtractMarkedTurnAsync` `:1687`), and the verdict token is never even
parsed. The control pair stays a control pair after the fix (§5.1).

### 2.2 `AgentTaskStandingAgentDispatchTests.a_marked_turn_on_the_standing_session_…` (`:325-361`)

Real dispatch onto a seeded `Running` standing session, then the `Turn()` helper (`:396-410`)
emits prompt/text/`TurnEnd` through `AgentSessionRuntime.ObserveTranscriptAsync` — the ingestion
path production uses, which is what the test exists to prove fires for a session the dispatcher
did not launch. Reply `"Producing: two commits in the last window. Looks healthy."`, no token →
nudge, null. The trigger *did* fire — the nudge on the standing session is the evidence — but
the test reads only `Status`.

Worth noting for §5.2: the nudge is a WhenIdle queue message typed into a **standing agent's**
session. That interaction is new since CARD-0159 and is pinned nowhere.

### 2.3 `GrokDelegateEndToEndTests.a_Kind_Grok_worker_…` (`:96-311`)

The launched fakegrok replies `"FAKE response to: <first 60 chars of the prompt>"` (`FakeGrok/
Program.cs:390-420`) with no token — the fake has never been taught the contract. The screen
captured by the failing assertion this pass:

```
FAKE response to: [antiphon-task:b2321c27] role=Code tier=Frontier workspace=S
Worked for 1.7s
[antiphon-task:b2321c27] Your turn ended without the closing report line. If the work is
finished, send the report now, ending with `[antiphon-report:b2321c27 done]` (or `blocked` /
`failed`). If it is not finished, continue.
SUBMITTED:[antiphon-task:b2321c27] Your turn ended without the closing report line. …
FAKE response to: [antiphon-task:b2321c27] Your turn ended without the closing
Worked for 1.7s
```

So in this harness the whole S2 round trip works end to end: the first unmarked `end_turn`
nudged, the WhenIdle flush out of `SyncTranscriptAsync` typed the nudge, fakegrok answered it,
a second unmarked boundary arrived — and the task still did not settle in 60 s. That last step
is the CARD-0248 gate ordering described in §8 (with `FinalMessageGraceSeconds = 0` the outcome
is `Landed`, not `FinalMessageMissing`, so the 240 s response window is *not* what held it).

The card's "SingleAsync more than one element" reading is step 4 (`:216`), which asserts exactly
one `SessionQueuedMessages` row on the delegate session — true until the nudge is enqueued.
Whether the nudge lands before step 4 depends on how long `LaunchQueue.WaitForIdleAsync` and the
`summary.json` read take against fakegrok's instant reply plus the 150 ms pump. Same mechanism,
earlier assertion.

### 2.4 `GrokDelegateEndToEndTests.a_ClaudeCode_worker_…` (`:317-420`)

Real fakeclaude launch (session `Running`), then a **seeded** Claude-shaped turn
(`"Done. 3 passed, 0 failed."`, no token) and a direct `OnTurnEndAsync`. Nudge, null. Nothing
Grok-specific and nothing to do with the fake: the seeded text is the whole story.

## 3. Re-deriving the list (not trusting the card's four)

Method: every test file that drives settlement (`OnTurnEndAsync`, `ObserveTranscriptAsync`,
`SyncTranscriptAsync`, `SettleDeferredReportsAsync`) — 27 files — minus those that already use
`ReportToken` / `antiphon-report` / a `closingVerdict` helper — leaves 23; of those, the ones
that assert `AgentTaskStatus.Succeeded` after such a drive are **exactly the four above**
(`PolledCompletionNoteShrinkTests:174` seeds `Succeeded`, it does not assert it). Only one other
token-less driver even seeds a task marker: `tests/Antiphon.E2E/DelegationSequencingE2ETests`,
a `[Category("Headed")]` canary whose delegates are *real* Claude sessions given the real brief,
so they emit the token themselves; unaffected.

Files that already migrated (for the pattern, not for editing): `AgentTaskReplyIntegrationTests`
(`SeedTurnAsync(… closingVerdict = true)` + `ApplyClosingVerdict` `:3117-3170`),
`AgentTaskDeliveryWatchdogTests:2436`, `AgentTaskDeadSessionReconciliationTests:743`,
`AgentTaskCatchUpSettlementTests:103`, `AgentTaskSettlementRaceTests:359`.

## 4. The design question, answered

**Q: Is the assertion stale, or is nudge-then-settle an unintended regression for caller /
self-owned tasks?** Stale. Three independent reasons, any one sufficient:

1. *Intent is on the record.* CARD-0159 §4.2's settlement table has one row for "none, task not
   yet nudged, session live" and it says "not settled". §4.2 also lists the only exemption
   (`Role == Check`) and why. The card's "is it intentional?" is answered by the design doc the
   card itself points at.
2. *Two later cards depend on it.* CARD-0248 exists because settle-anyway fired on the *same*
   boundary; its whole contract is "we asked once and it ended ANOTHER turn unmarked". CARD-0294
   exists because a nudged child sat idle and nobody heard. A caller-owned carve-out would put
   the sub-orchestrator — the task with the most delegates under it and the most to misreport —
   on the one path those cards were written to close.
3. *There is nothing to carve out.* No production task has had `AgentSessionId ==
   ParentSessionId` since the deploy; no `Kind=Orchestrator` task has been dispatched since the
   deploy; every dispatch path types the contract (§1.2). A carve-out keyed on Kind, on
   `AgentId` (standing agent), or on `AgentSessionId == ParentSessionId` would be dead code with a
   footgun attached.

**What would be wrong to do:** make the tests pass by seeding the sessions `Stopped` (they would
then settle as `UnmarkedAfterNudge` and the tests would silently assert the dead-session arm
instead of what they are about); add a `DelegationSettings` switch that disables the nudge for
harnesses (a production knob nobody should ever flip, and the fake would still be modelling a
delegate that ignores its brief); exempt `Kind == Orchestrator` or pinned agents in
`ClassifyReportAsync` (reason 2 above).

## 5. Fix scope

No production code. Areas: `tests`, `grok` (the fake). Sequential, one delegate.

### 5.1 S1 — `AgentTaskCheckSweepTests` (`tests/Antiphon.Tests/Application/AgentTaskCheckSweepTests.cs`)

- `Harness.SeedTurnAsync(sessionId, prompt, reply)` gains `bool closingVerdict = true` and applies
  the same rule as `AgentTaskReplyIntegrationTests.ApplyClosingVerdict`: when the **prompt**
  carries a task marker (`DelegationReportFormatter.TryReadTaskMarkerId`), the reply is not a
  question and does not already contain `[antiphon-report:`, append
  `"\n" + DelegationReportFormatter.ReportToken(shortId, "done")`. Copy the helper (it is
  `private static` in the other file); do not make it shared across suites in this card.
- Effect on the control pair, with **no change to either test body**: the negative test's prompt
  is a check note with no marker → nothing appended → still refused at the marker gate. The
  positive test's prompt is marked → token appended → `Succeeded`, `Result` contains "three
  delegates ran" (the token is stripped into `body` at `:526-529`). The two tests still differ
  only in the prompt, which was the point of the pair. Add
  `parent.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked)` to the positive test.
- New pin, same harness, next to the pair:
  `the_same_caller_turn_without_the_closing_line_is_nudged_once_not_settled` — seed with
  `closingVerdict: false`; assert `Dispatched`, `ReportNudgedAt` set, exactly one
  `SessionQueuedMessages` row on the caller session with `Origin == Delegation`, `Pending`,
  body containing the caller task's marker and "closing report line"; and that
  `NotesToCallerAsync` (which filters `Origin == Check`, `:1048`) still returns nothing — the
  nudge must not be mistaken for a check note by this suite's own accounting.

### 5.2 S2 — `AgentTaskStandingAgentDispatchTests` (`…/AgentTaskStandingAgentDispatchTests.cs`)

- `Turn(sessionId, prompt, reply)` (`:396`) gains `bool closingVerdict = true` with the same rule
  (the prompt is `TaskMarker(task.Id) + "\n\nRead the bundle."`, so the id is read from it, not
  passed separately — keeps the helper honest about what a real reply is correlated on).
- Existing test: unchanged body, now settles `Marked`; add the `ReportEvidence` assertion.
- New pin: `an_unmarked_turn_on_the_standing_session_is_nudged_and_still_leaves_the_agent_alone`
  — same setup, `closingVerdict: false`, driven through `ObserveTranscriptAsync` as the original
  is; assert `Dispatched`, `ReportNudgedAt` set, one `Pending` WhenIdle Delegation-origin row on
  the **standing** session carrying the marker, and `AgentSnapshotAsync(agentId)` unchanged
  before/after — the nudge into a standing agent's session must not touch the agent row any more
  than settlement does (§2.2: this interaction has no pin today).

### 5.3 S3 — `GrokDelegateEndToEndTests` and the fake

- **Claude control** (`:394-396`): seed the AssistantText as
  `"Done. 3 passed, 0 failed.\n" + DelegationReportFormatter.ReportToken(queued.Id, "done")`.
  Assertions `Result` contains "3 passed", tokens 1/1 and the price are unchanged (the token is
  stripped from `Result`, the turn count is still one). Add `ReportEvidence == Marked`.
- **fakegrok** (`src/Antiphon.FakeGrok/Program.cs`): opt-in `ANTIPHON_FAKE_REPORT_LINE=1`, in
  the fake's existing knob style (`SubmitWhileWorkingCancels`, `QuestionToolEnabled`, `:61-76`).
  When set and the submitted text contains `[antiphon-task:XXXXXXXX]`, the **assistant text
  written to `updates.jsonl`** (the `assistant` argument of `AppendSessionFiles` on the plain
  path, `:420`) becomes `$"FAKE response to: {echo}\n[antiphon-report:XXXXXXXX done]"`. The
  screen echo may stay as it is; settlement reads the transcript, not the pty. Read the id from
  the **last** marker in the prompt (the pointer carries one at each end, `ReportingContract`
  `:236-244`; both are the same id). Leave the question-tool and submit-while-working paths
  unchanged — those model other measured shapes. Document the knob in the file header comment
  list (`:31-40`).
- **`FakeGrokContractTests`** (`tests/Antiphon.Agents.Pty.Tests/FakeGrokContractTests.cs`): one
  test, `Report_line_knob_appends_the_task_token_to_the_agent_message_chunk` — launch with the
  knob and `GROK_HOME`, submit a marked prompt, read `updates.jsonl`, assert the
  `agent_message_chunk` text's last line is `[antiphon-report:<id> done]` and that without the
  knob it is not (control). Pattern: `Session_id_writes_grok_session_files_under_GROK_HOME`
  (`:234`).
- **Grok capstone** (`:104-112`): add `["ANTIPHON_FAKE_REPORT_LINE"] = "1"` to the grok
  definition's `Env` next to `GROK_HOME` (`:494`) — the definition env is the launch env the
  test already asserts on (`spec.Env` `:178`). Keep step 4's `SingleAsync` (`:216`): with a
  marked reply no nudge is enqueued, and "exactly one message was typed at the delegate" is the
  stronger assertion. Add `settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked)` at
  `:268`. Tokens stay 1/1 and the price assertions stay as written — one turn, one
  `turn_completed`.
- The `FinalMessageGraceSeconds = 0` escape hatch and its comment stay: fakegrok's chunks still
  carry no promptId, which is a different property of the fake (§2.3, §8).

### 5.4 S4 — verification and close-out

- Run (alternate output, forward slash, delete afterwards):
  `AgentTaskCheckSweepTests`, `AgentTaskStandingAgentDispatchTests`, `GrokDelegateEndToEndTests`
  (all in `tests/Antiphon.Tests`), `FakeGrokContractTests` and `GrokTranscriptTailerTests` (in
  `tests/Antiphon.Agents.Pty.Tests`, run after the first project, never concurrently), plus
  `AgentTaskReplyIntegrationTests` as the regression floor for the classifier (untouched, must
  stay 80+/80+).
- **Positive controls** (this repo's habit): with the fix in place, flip `closingVerdict: false`
  on the CheckSweep positive test and it must fail on `Dispatched` with the "asked once for
  `[antiphon-report:…]`" Warning event present; unset the fakegrok knob and the capstone must
  fail with the nudge visible on the screen, as in §2.3.
- Card close-out text: the verdict (§0.1), the four tests, the fake knob, and a pointer to the
  §8 follow-up card once filed. `docs/testing-and-build.md` gets one line under the
  delegation-test notes: *seeded settlement turns must end with
  `DelegationReportFormatter.ReportToken(id, "done")` unless the test is about the nudge.*

### 5.5 Files

| File | Change |
|---|---|
| `tests/Antiphon.Tests/Application/AgentTaskCheckSweepTests.cs` | helper parameter + rule; 1 assertion; 1 new test |
| `tests/Antiphon.Tests/Application/AgentTaskStandingAgentDispatchTests.cs` | helper parameter + rule; 1 assertion; 1 new test |
| `tests/Antiphon.Tests/Application/GrokDelegateEndToEndTests.cs` | seeded text; definition env; 2 assertions |
| `src/Antiphon.FakeGrok/Program.cs` | opt-in knob, ~15 lines, header comment |
| `tests/Antiphon.Agents.Pty.Tests/FakeGrokContractTests.cs` | 1 new test |
| `docs/testing-and-build.md` | 1 line |

Estimate: verification floor ~40 min (three Tests classes ≈ 4 min, Pty contract tests ≈ 2 min,
`AgentTaskReplyIntegrationTests` ≈ 3 min, two builds to an alternate path ≈ 10 min, positive
controls ≈ 10 min) plus ~1.5 h authoring → **2–3 h**, Worktree, Codex terra or Grok.

## 6. Rejected alternatives

- **Model the two-step contract inside the Grok capstone** (let the nudge fire, let fakegrok
  answer it, assert `UnmarkedAfterNudge`). It would double the token counters (two turns), move
  every hard-coded price in the test, and — today — never settle at all because of §8. The
  capstone is about the *kind* travelling end to end and Grok pricing; the nudge round trip
  deserves its own test with its own numbers, if anyone wants one, after §8 is fixed.
- **Default-on report line in fakegrok.** Every other knob in both fakes is opt-in and named in
  the header; a default-on change would alter the transcript text every fakegrok consumer sees
  (`SessionMessageQueueGrokPtyIntegrationTests`, `GrokTranscriptTailerTests` fixtures).
- **A shared test helper for the closing verdict** across the four suites. Each harness builds
  entries differently (`TranscriptEntry` rows in two, `SessionRunnerTranscriptEvent` in one,
  a launched process in one); a shared helper would be four adapters around ten lines. Copy the
  rule, cite the source.
- **Any change to `ClassifyReportAsync`.** §4.

## 7. What the card got right and wrong

- Right: the mechanism, the four tests, "surfaced incidentally, not caught by 0159's own list",
  and that the CheckSweep failure is independent of everything else on master that night.
- Wrong or imprecise: "or never if the session isn't live to receive the nudge" — a dead
  session settles *immediately* (`UnmarkedAfterNudge`); a live-but-idle nudged session Blocks
  after 5 min (CARD-0294). "every plain-language completion … will now cost an extra idle-turn"
  — only a delegate that ignores the contract pays it (~1 % in production). "may be a distinct
  'SingleAsync' bug" — same nudge, earlier assertion (§2.3).

## 8. Residual finding for a follow-up card (CARD-0248 gate ordering)

`ClassifyReportAsync` settle-anyway (`:2138-2160`) refuses a boundary when
`boundary.CreatedAt <= sentAt` of the nudge row. `SentAt` is stamped by the queue's confirm loop
*after* it finds the nudge's `UserPrompt` row; the delegate's reply and its `TurnEnd` can be
tailed and stored in the same pump pass as that prompt row when the reply is fast enough (in the
capstone, fakegrok writes prompt, reply and `turn_completed` in one synchronous write; the pump
syncs every 150 ms; the confirm loop polls at 200 ms). Then every later attempt on that boundary
returns "one that predates the ask", no third boundary ever arrives, and
`BlockUnmarkedWaitingAsync` declines because the idle boundary is not the *nudged* one
(`:2351-2354`). The task sits `Dispatched` until the deadline policy.

Not proven from rows this pass — the Testcontainer database is gone with the run — but it is the
only arm consistent with the §2.3 screen (nudge delivered, answered unmarked, `Landed` outcome,
no settle in 60 s), and the code reads that way. Real Claude/Grok/Codex turns take seconds, so
the window is narrow in production; the honest fix is to key "after the ask" on the nudge's own
`UserPrompt` row (sequence, or that row's `CreatedAt`) rather than on `SentAt` wall-clock, and to
let `BlockUnmarkedWaitingAsync` consider a post-nudge unmarked boundary that settle-anyway has
refused. File it against CARD-0248's area; do not fold it into this card.

## 9. Verification commands

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c243/ -- --treenode-filter "/*/*/AgentTaskCheckSweepTests/*"
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c243/ -- --treenode-filter "/*/*/AgentTaskStandingAgentDispatchTests/*"
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c243/ -- --treenode-filter "/*/*/GrokDelegateEndToEndTests/*"
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c243/ -- --treenode-filter "/*/*/AgentTaskReplyIntegrationTests/*"
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c243/ -- --treenode-filter "/*/*/FakeGrokContractTests/*"
Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-c243 | Remove-Item -Recurse -Force
```

Baseline at `e0385e95` this pass: CheckSweep 31/32, Standing 8/9, GrokE2E 0/2 (the four named
tests are the only failures).

## 10. Environment / cleanup

Built to `bin-c243/` (reusing the killed task's directories) and deleted them afterwards.
`bin-hangfirefix/` and `bin-card0208/` directories from other tasks were present under the same
projects and were left alone. Production evidence came from one read-only query against the
live `AgentTasks` table. No card, session, or queue row was written.
