# CARD-0302 — a Check-role LOOKS STUCK / BLOCKED reading must not Block the Check task

**Date:** 2026-09-01 (Plan pass, task 4cd36bcc — design only; no production code changed)
**Card:** CARD-0302 "Stale Check BlockedQuestion rows: LOOKS STUCK/BLOCKED verdicts mark the Check task itself blocked"
**Diagnosis:** done, on the card. Six `Role = Check` rows sit in `GET /api/attention` as Critical `BlockedQuestion` ("waiting on a human answer"). None of them asked a question. The standing check-interpreter wrote a LOOKS STUCK / BLOCKED verdict about some *other* task; settlement then set the Check row itself `Status = Blocked`.

**Sources (verified this pass):** `AgentTaskReplyService.ClassifyReportAsync` (`:1958-2022`), `LooksLikeAQuestion` (`:2417-2428`), `DelegationReportFormatter.BuildBrief` / `ReportingContract` (`:156-187`, `:240-278`), `CheckInterpretation` + `server/Bundles/check-interpreter.md` (contract v2), `AgentTaskCheckService.InterpretAsync` / `WaitForInterpretationAsync` (`:380-473`, `:616-639`), `AttentionService.BuildBlockedAsync` (`:155-160`, `:232-285`), `BlockedTaskNotifier`, `ReleaseDelegateAsync` (`:1247-1256`), CARD-0047 slice 4 amendment, CARD-0159, CARD-0294. Diagnosis is not re-litigated.

The six rows named on the card (`7a7817b4`, `616882d4`, `49b326d4`, `1f4ea55a`, `c4ffe38e`, `b415cb32`) are **stale evidence** for the bug. Do not Reply or Cancel them (they share standing session `37ef8766`). Do not enumerate them in the implementation. A class-wide remap of `Role=Check + Blocked + non-empty Result` is in scope; hand-clearing those ids is not.

---

## Decision

The card's recommended direction is the design, not a prompt:

> Settle the Check task **Succeeded** — the interpretation *is* the deliverable — and keep the stuck/blocked verdict as evidence on the **checked** task. Do not map it onto the Check row's own `Status=Blocked`.

Three slices in, one policy out. The classifier is **not** taught to read `LOOKS STUCK` / leading `BLOCKED` as English.

1. **S1 — Role before token.** `ClassifyReportAsync` classifies `Role == Check` *before* `verdict == "blocked"` and before `LooksLikeAQuestion`. A produced Check reading (including one that closes `[antiphon-report:<check-id> blocked]` or ends in `?`) is `Succeeded`. `failed` still Fails: that token is the interpreter saying it could not produce a reading.
2. **S2 — Stop asking for the wrong token.** Check-role briefs must not offer `blocked` as *this Check task's* status. Contract v3: close with `done` when a verdict word was produced (including LOOKS STUCK); `failed` only if there is no reading; never `blocked`. "Never say complete" applies to the **checked** task, not to finishing the interpretation.
3. **S3 — Stale population and the attention feed.** A cheap idempotent remap turns existing `Role=Check + Blocked + non-empty Result` rows into `Succeeded`. `BuildBlockedAsync` and `BlockedTaskNotifier` skip `Role=Check` so a residual Blocked Check row cannot occupy Critical or trigger a Telegram ping. Reply/Cancel/Escalate stay off those rows because they are no longer `BlockedQuestion`.

What already exists and is **not** rebuilt: the Check event on the **checked** task already stores the reading (`AgentTaskCheckService.ComposeEventDetail` / `TryReadInterpretation`; CARD-0035 slice 5). `WaitForInterpretationAsync` already treats `Blocked` as an answer so the LOOKS STUCK text is delivered and filed even today. `ReleaseDelegateAsync` already no-ops on `!IsPoolDelegate`, so Succeeding a Check task does not kill `antiphon-check-interpreter`. `InstructionBundles.ForDelegate` already returns no `delegate-basics` for Check. CARD-0159's rejection still holds: a model's LOOKS STUCK opinion must not gate the **checked** task's state machine.

---

## Ground truth (checked, not guessed)

### Why the Check row becomes Blocked

`ClassifyReportAsync` (`AgentTaskReplyService.cs:1958-2022`), in order:

| Step | Check-interpreter turn that leads with LOOKS STUCK |
|---|---|
| `verdict == "done"` | no |
| `verdict == "blocked"` | **yes, if the last line is `[antiphon-report:<check-id> blocked]`** → `Blocked` + `Marked`. The Check-role carve-out at `:1980-1981` is never reached. |
| `verdict == "failed"` | no (would Fail the Check row and `InterpretAsync` would **discard** the reading as degraded) |
| `LooksLikeAQuestion(body)` | **yes, if the last two lines end in `?`** (a plausible AMBIGUOUS / LOOKS STUCK close). Same: `Blocked` + `QuestionHeuristic`, carve-out skipped. |
| `Role == Check` | only reached for unmarked, non-question readings → `Succeeded` + `Exempt` (the existing test `a_check_role_task_is_never_nudged` pins this with `"LOOKS FINE — last tool 2m ago."`) |

There is **no** leading-word parser. `LooksLikeAQuestion` is still only "last two non-empty lines end with `?`" (`:2417-2428`). The card's "settlement parser treated the verdict word BLOCKED / LOOKS STUCK as this Check task is blocked" is the **marked-`blocked` arm plus the generic reporting contract**, not a new heuristic.

### Why the interpreter emits `blocked`

Two contracts ride the same turn and disagree.

1. **Check contract v2** (`server/Bundles/check-interpreter.md`): lead with `DOING / PRODUCED / LOOKS STUCK / SETTLED / AMBIGUOUS`. "NEVER say the task is complete, done, or successful."
2. **Generic `ReportingContract`** (`DelegationReportFormatter.cs:271-274`), appended by `BuildBrief` for **every** role including Check (`AgentTaskDispatcher.cs:2210`): close with `[antiphon-report:id done|blocked|failed]`. `blocked` = "you need a decision or an answer to continue."

Check tasks are not exempt from `BuildBrief`. They *are* exempt from `delegate-basics` (`InstructionBundles.ForDelegate` returns `[]` for Check). So the specialist is told, in the same prompt: never say done, and also pick done/blocked/failed. LOOKS STUCK is "the checked task needs a human"; the generic contract's word for that is `blocked`. The token is correlated to the **Check** task id (the brief's own marker), so `TryReadReportVerdict` accepts it and S1's current order maps it onto the Check row.

Unmarked Check already settles `Succeeded`/`Exempt` without a token (`a_check_role_task_is_never_nudged`). The token is optional for Check today; offering `blocked` is what makes it harmful.

### What already happens to the reading

`WaitForInterpretationAsync` (`:630`) returns on `IsSettled || Status == Blocked`, with an explicit comment that a trailing `?` is a plausible AMBIGUOUS close and throwing the text away would be worse. `InterpretAsync` then:

- `Failed` / `Canceled` → degraded digest, reading discarded
- empty `Result` → degraded
- anything else with text, **including Blocked** → the reading is the note body and is written onto the **checked** task's `Check` event (`:196-205`)

So the evidence-on-the-checked-task half of the card's recommendation is already shipped. The bug is only that the Check row stays `Blocked` (`CompletedAt` set, `IsSettled` false — Blocked is not Succeeded/Failed/Canceled).

### Why that Blocked row is Critical

`AttentionService.GetAsync` loads every `Status == Blocked` row with **no role filter** (`:158-160`). `BuildBlockedAsync` emits Critical `BlockedQuestion` with actions `[Reply, Cancel, Escalate]` (`:269-281`). `BlockedTaskNotifier` pings the same set.

Reply on a Check row calls `AnswerAsync` (`:249-262`): flips the Check task to Working and enqueues a marked body into `task.AgentSessionId` — the **standing interpreter** session (`37ef8766`). Cancel calls `KillAsync` on that same session. That is why the card forbade both.

`ListAsync` already hides `Role == Check` unless `includeChecks=true`. The attention feed is how these rows reach an operator. The six parents are already `Canceled` or `Succeeded`; after the Check row is no longer Blocked there is nothing left to answer.

Dispatcher serialisation (`WaitForAgent`) keys on Dispatched/Working, not Blocked, so these rows do not stall the specialist. The damage is the feed, the ping, and the unsafe verbs.

### What CARD-0159 / CARD-0294 already forbade

- CARD-0159: do not make settlement read the check interpreter's LOOKS STUCK as a gate on the **checked** task. A model's opinion must not drive a state machine.
- CARD-0294: "Does not let the check-interpreter write Status." "Do not parse check-interpreter readings." `LooksLikeAQuestion` stays the trailing-`?` test.

This card obeys both. Check-role status is "did the interpreter finish a reading", never "what did it think of the delegate." LOOKS STUCK stays prose on the Check event of the checked task. PastExpectedIdle / ChecksSpent / DeadSession already surface actually-stuck **delegates**; they already attach the latest interpretation as evidence via `TryReadInterpretation`.

---

## Slices

### S1 — Classify Check-role before `blocked` / `LooksLikeAQuestion`

**File:** `server/Application/Services/AgentTaskReplyService.cs` (`ClassifyReportAsync`).

At the top of the method, before the `done`/`blocked`/`failed` arms:

```
if (task.Role == AgentTaskRole.Check)
    return ClassifyCheckReport(body, verdict);
```

`ClassifyCheckReport` (private, same class):

| Input | Status | Evidence | FailureReason |
|---|---|---|---|
| `verdict == "failed"` | `Failed` | `Marked` | first line, same as today |
| `verdict == "done"` | `Succeeded` | `Marked` | null |
| anything else (`blocked`, empty, trailing `?`) | `Succeeded` | `Exempt` | null |

`blocked` remapped to Succeeded is **Exempt**, not `Marked`. A Succeeded row whose evidence says "marked blocked" would lie on every surface that prints `report=`. The generic token was the wrong vocabulary; we do not record it as a successful use of the contract.

Do **not** call `TryClassifyCompletedWithoutProgressAsync` for Check (it already no-ops: Check is not Code+Worktree). Do **not** edit `LooksLikeAQuestion`. Do **not** parse leading `LOOKS STUCK` / `BLOCKED`.

`SettleAsync` is unchanged: it already writes `Result = settledBody` (token stripped), `CompletedAt`, a `Completed` event on Succeeded, and `ReleaseDelegateAsync` which returns immediately when the agent is not a pool delegate (`:1255-1256`). Check tasks stay pinned to the standing interpreter.

`WaitForInterpretationAsync`'s `|| Status == Blocked` stays for the deploy race (an in-flight turn that still Blocks under the old binary). After S1 it is dead for new turns; do not remove it in this card.

**Tests** (extend `AgentTaskReplyIntegrationTests`, next to `a_check_role_task_is_never_nudged`):

- Check + body `LOOKS STUCK — session idle 28m.` + `[antiphon-report:<id> blocked]` → `Succeeded`, `Exempt`, `Result` is the LOOKS STUCK body (token stripped), `Completed` event not `Blocked`, standing agent row untouched, no parent note (`ReplyTo` may be Session in this harness — pin `ReplyTo=None` or assert no completion note if None).
- Check + AMBIGUOUS body whose last line ends `?`, no token → `Succeeded`, `Exempt` (today this is `Blocked` + `QuestionHeuristic`).
- Check + `[antiphon-report:<id> failed]` → still `Failed`, `Marked` (regression pin).
- Check + unmarked `LOOKS FINE` → still `Succeeded`, `Exempt` (existing test, unchanged).
- Non-Check + `[antiphon-report:<id> blocked]` → still `Blocked`, `Marked` (existing CARD-0159 pin, must stay green).
- Non-Check + trailing `?` → still `Blocked`, `QuestionHeuristic`.

### S2 — Check-role reporting contract v3

**Files:** `server/Bundles/check-interpreter.md`, `CheckInterpretation.cs` (`ContractVersion = "3"`, `OutputFormatReminder`), `DelegationReportFormatter.cs` (`BuildBrief`).

`BuildBrief`: when `task.Role == AgentTaskRole.Check`, append a Check-specific closer instead of `ReportingContract`. The generic `done|blocked|failed` paragraph must not appear on a Check brief. Pointer path (`BuildBriefPointer`) is used when the brief spills; Check briefs are small (bundle + reminder) but the closer must follow the same role branch so a spilled Check brief cannot re-introduce `blocked`.

Check closer, short:

- End with `[antiphon-report:<id> done]` when you produced a verdict word (`DOING / PRODUCED / LOOKS STUCK / SETTLED / AMBIGUOUS`). That token means **this interpretation is finished**, not that the checked task is complete.
- End with `[antiphon-report:<id> failed]` only if you could not produce a reading.
- Never emit `blocked`. LOOKS STUCK / "needs a human" is the **reading**, filed on the checked task. This Check task does not ask the operator a question.

Contract v3 in `check-interpreter.md` (reconciled onto the agent row by `CheckInterpreterProvisioner` on the next `EnsureAsync`):

- Keep the five verdict words. Lead with the verdict word.
- Replace "NEVER say the task is complete, done, or successful" with "NEVER say the **checked** task is complete, done, or successful. Completion of that work is decided by its own report. Closing *this* Check task with `done` after a verdict word is required."
- Explicit: `LOOKS STUCK` is a reading about the checked task. It is not `[antiphon-report:… blocked]`.

`OutputFormatReminder` gains one clause: "Close with the Check task's `done` token after the reading; never `blocked`."

Bump `ContractVersion` to `"3"` and the literal `contract v3` in the bundle. `InstructionBundleTests` already asserts `Contract.ShouldContain($"contract v{ContractVersion}")`. `CheckInterpreterProvisionerTests` currently hard-codes `"contract v2"` and `ContractVersion.ShouldBe("2")` (`:142`, `:184`) — update those to v3; they are the tripwire the constant's remarks name.

**Tests:**

- `DelegationUnitTests`: `BuildBrief` for `Role=Check` contains the Check `done` token, does **not** contain the Check `blocked` token, does **not** contain "if you need a decision or an answer to continue". Non-Check `BuildBrief` still contains all three tokens (existing `the_reporting_contract_asks_for_the_closing_verdict_line`).
- `CheckInterpreterProvisionerTests` / `InstructionBundleTests`: v3 pins.
- `AgentTaskCheckInterpreterTests.the_interpretation_task_is_its_own_root_pinned_and_answers_to_nobody`: Goal still contains the output-format reminder; add that it mentions never `blocked` if the reminder text is the assertion site.

### S3 — Remap the stale class; hide Check from BlockedQuestion

**Files:** `AgentTaskCheckService.cs` (new remap), `AgentTaskDispatcher.cs` (`TickAsync` calls it), `AttentionService.cs` (`BuildBlockedAsync` / the blocked query), `BlockedTaskNotifier.cs`.

**Remap** (`RemapBlockedInterpretationsAsync`), idempotent, no new hosted service:

- `Role == Check && Status == Blocked && Result != null && Result != ""`
- Set `Succeeded`, `ReportEvidence = Exempt` (same as S1's remapped-blocked arm), keep `Result` / `CompletedAt` (already set), new `ConcurrencyToken`, `Completed` event detail `CARD-0302: Check-role interpretation is the deliverable; remapped from Blocked.`
- Do **not** `AnswerAsync`, `KillAsync`, `ReleaseDelegateAsync`, or touch `AgentSessionId`. The standing interpreter must stay up.
- Do **not** take a list of the six ids. Shared-Postgres tests seed their own rows and assert on those ids only.

Call it from `AgentTaskDispatcher.TickAsync` once per tick (cheap `ExecuteUpdate` + event inserts for the few rows that match; after the first pass it is a zero-row update). Not from `AttentionService` (read-only). Not from `CheckInterpreterProvisioner` (that class owns the agent row).

Empty-Result Blocked Check rows are left Blocked; they are not readings. Attention skip (below) still hides them. They should be rare; do not invent a Fail for them here.

**Attention / notifier:** the blocked load in `GetAsync` (`:158-160`) and `BlockedTaskNotifier.SweepAsync` (`:30`) add `&& t.Role != AgentTaskRole.Check`. `BuildBlockedAsync` can keep its current body. Away-digest `NeedsYou` reads `BlockedQuestion` from attention, so it follows for free. Pipeline status and `ListAsync` already exclude Check.

**Tests:**

- Remap: seed `Role=Check`, `Blocked`, non-empty `Result` looking like `LOOKS STUCK — …`, `CompletedAt` set, pinned to a standing AlwaysOn non-pool agent with a live session → after remap, `Succeeded`, `Exempt`, Result unchanged, session still Running, agent still present, one new `Completed` event naming CARD-0302. A second tick is a no-op (no second event).
- Remap does not touch a non-Check Blocked row (question-Blocked Code task stays Blocked).
- Remap does not touch Check Blocked with empty Result.
- `AttentionServiceTests`: a Check-role Blocked row is **not** `BlockedQuestion`, even before remap. A Code-role Blocked row still is.
- `BlockedTaskNotifier` (or its existing suite): Check-role Blocked is not pinged.
- Existing `AgentTaskCheckInterpreterTests` success path (settle interpretation in-harness with `DOING`) stays green; add one sibling that settles the interpretation row through the **real** reply service with a LOOKS STUCK + `blocked` token and asserts the **checked** task's Check event still stores the reading (`TryReadInterpretation`). If that is too much harness for the reply-service test, keep the event pin in `AgentTaskCheckInterpreterTests` by writing `Result` + `Succeeded` as today — S1's integration test already covers the token remap.

### S4 — Docs (same PR, last)

One paragraph in `docs/orchestration-loop.md` §4 (Checking on a delegate): a Check-role task settles `Succeeded` when it has produced a reading; `LOOKS STUCK` / `BLOCKED` in that reading is evidence on the **checked** task (Check event / parent note), never the Check row's own `Status`. Reply/Cancel on a Check row is unsafe because it shares the standing interpreter session.

`docs/agent-card-lifecycle.md` already says Check rows never bind; no change unless a sentence would otherwise still imply Blocked Check work moves a card (it cannot; they have no `CardId`).

Do not add an `AttentionKind`. Do not add a column or migration (`Exempt` already exists; remap writes it).

---

## What this card does not do

- **Does not parse `LOOKS STUCK` / leading `BLOCKED` as a classifier input.** Role is the gate.
- **Does not Block, Fail, or otherwise mutate the checked task** because the interpreter said LOOKS STUCK (CARD-0159).
- **Does not change `LooksLikeAQuestion`.**
- **Does not add `AttentionKind` for interpreter stuckness.** PastExpectedIdle / ChecksSpent / DeadSession already exist; they already carry the reading as evidence.
- **Does not Reply, Cancel, or name the six stale ids in code.** Class remap only.
- **Does not kill or compact the standing interpreter** on Check Succeeded (`ReleaseDelegateAsync` already no-ops non-pool).
- **Does not give Check tasks `delegate-basics`.**
- **Does not treat Check `failed` as Succeeded.** Empty/failed interpretations stay degraded in `InterpretAsync`.
- **Does not remove `WaitForInterpretationAsync`'s Blocked-as-answer** (deploy race / residual rows).

---

## Test matrix

Existing pins that must stay green: `a_check_role_task_is_never_nudged`, `a_check_role_is_never_blocked_as_unmarked_waiting` (CARD-0294), DelegationUnitTests marked `blocked`/`failed` on non-Check, `LooksLikeAQuestion` trailing-`?` suite, `CheckInterpreterProvisionerTests` shape tests (AlwaysOn, deny-all hook, not pool), `AgentTaskCheckInterpreterTests` success/timeout/backlog, `AttentionServiceTests` Code-role `BlockedQuestion`.

| Layer | Test |
|---|---|
| `Antiphon.Tests` Application | **S1 incident shape:** Check + LOOKS STUCK body + `[antiphon-report:id blocked]` → Succeeded, Exempt, Result is the reading. |
| `Antiphon.Tests` Application | **S1 question close:** Check + trailing `?` unmarked → Succeeded, Exempt. |
| `Antiphon.Tests` Application | **S1 failed:** Check + `failed` token → Failed, Marked. |
| `Antiphon.Tests` Application | **S1 non-Check regression:** Code + `blocked` token still Blocked; Code + trailing `?` still QuestionHeuristic. |
| `Antiphon.Tests` unit | **S2 brief:** Check `BuildBrief` has `done`, no `blocked` token, no "need a decision" sentence; Worker brief unchanged. |
| `Antiphon.Tests` Application | **S2 contract:** `ContractVersion == "3"`, bundle contains `contract v3` and the checked-task wording; provisioner reconciles it. |
| `Antiphon.Tests` Application | **S3 remap:** Check Blocked + Result → Succeeded; second tick no-op; non-Check Blocked untouched; empty Result Check Blocked untouched; standing session not killed. Shared-Postgres: seeded ids only. |
| `Antiphon.Tests` Application | **S3 attention:** Check Blocked is not `BlockedQuestion`; Code Blocked still is. Notifier does not ping Check. |
| `Antiphon.Tests` Application | **Evidence on checked task:** LOOKS STUCK reading still round-trips through `TryReadInterpretation` on the checked task's Check event. |

No client change (no new `AttentionKind`). No Pty tests. No E2E.

Run per `docs/testing-and-build.md`: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0302/` (forward slash), `--treenode-filter "/*/Antiphon.Tests.Application/*/*"` then the unit filter for `DelegationUnitTests`. Delete `bin-card0302` directories afterwards.

---

## Sequencing and risks

S1 and S2 ship together — classifier without the brief keeps teaching the model the wrong token; brief without the classifier still Blocks on in-flight turns and on any model that emits `blocked` anyway. S3 ships in the same PR so the six stale rows leave Critical on the next tick without a human touching the standing session. S4 is the paragraph that stops the next planner re-deriving this.

One PR.

| Risk | Standing |
|---|---|
| Succeeding Check `blocked` hides a *real* interpreter question | The Check contract forbids asking; AMBIGUOUS is the "bundle does not say" reading. Reply into the shared session is the greater hazard (card). `failed` remains Failed. |
| Remap Succeeds a Check that is genuinely waiting | Predicate requires non-empty `Result` — that is a finished reading, which is exactly the population the card described (`Result` + `completedAt` already set). |
| Remap / Succeed kills the interpreter | `ReleaseDelegateAsync` returns on `!IsPoolDelegate`. Remap does not call it. Test pins the session Running. |
| LOOKS STUCK no longer reaches an operator | It already does: parent `[check …]` note + Check event on the **checked** task. If that delegate is itself stuck, PastExpectedIdle / ChecksSpent / DeadSession still fire, now without a fake Critical on the interpreter. |
| Teaching the classifier LOOKS STUCK in review | Reject. Role is the gate. CARD-0159 / CARD-0294. |
| `failed` Check still degrades the parent note | Intended. No reading → digest + prefix, today. |

---

## Execution notes

After deploy, `TickAsync` remaps the current Blocked Check-with-Result population (the six on the card, and any twin) on the first pass. New LOOKS STUCK turns settle `Succeeded` via S1. Do not `delegate.ps1 -Reply` or `-Cancel` those ids while they still share `antiphon-check-interpreter`. No follow-up card is required for the stale rows if S3 lands in this PR.
