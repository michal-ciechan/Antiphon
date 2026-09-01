# CARD-0294 — classify unmarked idle-after-nudge as Blocked; do not teach the classifier to read "please approve"

**Date:** 2026-09-01 (Plan pass, task 7ffe5c92 — design only; no code changed)
**Card:** CARD-0294 "Orchestrator misses delegates that stop awaiting approval; surface and follow up promptly"
**Diagnosis:** done, on the card (Grok task `370e4f7c`, ContentEdit 2026-09-01 09:37Z). The Blocked / `needs_input` path already exists and is correct (`ClassifyReportAsync`, `AgentTaskReplyService.cs:1889-1953`). A delegate that emits `[antiphon-report:<id> blocked]` or ends its last two lines with `?` becomes `Blocked`, the parent gets the existing completion note, `AttentionKind.BlockedQuestion` is Critical with `Reply`, and `AnswerAsync` resumes the same session. The Codex/Terra Coesite E-03 child wrote `Please approve this design and I'll begin the recorded TDD cycles.` — no marker, no trailing `?` — so it fell through to the unmarked-narration branch: nudge the **child** once, leave the task `Dispatched`, tell the **parent** nothing. The child stayed idle ~8 hours. Herdr `done` is a confirmed red herring (CARD-0286: Herdr done ≠ task terminal). The gap is kind-agnostic; Codex is just the kind most likely to expose it.

**Sources (verified this pass):** `AgentTaskReplyService.cs` (`ClassifyReportAsync`, `LooksLikeAQuestion`, `NudgeForClosingLineAsync`, `SettleAsync`, `DeliverToParentAsync`, `AnswerAsync`), `AgentTaskEnums.cs` (`AgentTaskReportEvidence`), `AttentionDtos.cs`, `AttentionService.cs` (`BuildBlockedAsync`, `PastExpectedIdle`), `DelegationSettings.cs`, `delegate-basics.md`, CARD-0159/0248/0286/0288 plans. Diagnosis is not re-litigated.

---

## Decision

Three slices in, one policy decision out. The classifier is **not** taught to recognise approval prose.

1. **S1 — Contract clock, not a text heuristic.** `LooksLikeAQuestion` stays exactly "last two non-empty lines end with `?`" (`:2223-2233`). No approval-phrase matcher, no "this looks like it's waiting" NLP. After the existing one child-nudge is recorded, if the session stays idle on **that same boundary** with no closing line for `UnmarkedWaitingMinutes` (default **5**), classify `Blocked` with new evidence `UnmarkedWaiting`. Kind-agnostic. This cannot go through CARD-0248 settle-anyway (`ClassifyReportAsync` `:1941-1943` returns null on the same boundary) — it is a new sweep arm that Blocks, it does not Succeed.
2. **S2 — Herdr: no change.** `docs/herdr-sessions.md` and CARD-0286 already treat `agent_status: "done"` as post-turn chrome, never a task terminal. Do not touch `HerdrStatusCorroborationService`.
3. **S3 — Parent hears it now, operator sees it before PastExpectedIdle.** At the first child-nudge, enqueue a parent-session note (today the parent hears nothing). Project `AttentionKind.UnmarkedWaiting = 21` (Warning, read-time) for the Dispatched+nudged+idle+no-token window. Do **not** reuse CARD-0288's `ReportUnsettled` — that kind is "marker **present**, settlement missed". This kind is "no marker at all". Once S1 Blocks, `BlockedQuestion` takes over (first-match, Critical, `Reply`).
4. **Auto-continue on a prior blanket authorization ("start the remaining epics") is out of scope.** `AnswerAsync` only runs once `Blocked`. Inferring that a sequential-epic instruction matches a later design-approval wait is new policy, not a structural signal. The parent (human or orchestrator) replies. Do not silently `AnswerAsync`.

What already exists and is **not** rebuilt: the marked-`blocked` arm (`:1900-1901`), `LooksLikeAQuestion` (`:1909-1910`), the one child-nudge (`:1918-1921`, `:1993-2034`), `BlockedQuestion` attention (`AttentionService.cs:214-266`), `BlockedTaskNotifier`, `AnswerAsync` (`:231-261`), CARD-0286's worktree progress probe (observability in the parent note, **not** a gate — CARD-0159 rejected git as a settlement gate).

---

## Ground truth (checked, not guessed)

### What ran on the incident turn

`ClassifyReportAsync` (`:1889-1953`), in order:

| Step | Incident |
|---|---|
| `verdict == "done"\|"blocked"\|"failed"` | no token on the last line |
| `LooksLikeAQuestion(body)` | last line is `Please approve this design and I'll begin the recorded TDD cycles.` — no `?` → false. Deliberate: only a trailing `?` on the last two lines counts (`:2218-2233`), so a mid-report question mark does not Block. |
| `Role == Check` | not a check |
| session live, `ReportNudgedAt is null` | **nudge the child**, return null, task stays Dispatched |

`NudgeForClosingLineAsync` (`:1993-2034`) writes a Warning on the **child** task ("asked once for `[antiphon-report:…]`"), enqueues WhenIdle on the **child** session, and never calls `DeliverToParentAsync`. The parent session, the attention feed, and Telegram `BlockedTaskNotifier` all stay quiet. `PastExpectedIdle` (`AttentionService.cs:578-608`, threshold `2 × expected` floored at expected+30 min, `:720-725`) is hours away on a long Code estimate.

If the child stays idle, there is no second TurnEnd. CARD-0248 then **forbids** settle-anyway on the same boundary (`:1941-1943`). The task can sit `Dispatched` until a human asks, which is the incident.

A second unmarked end after a **delivered** nudge would today settle **Succeeded** (`UnmarkedAfterNudge`, `:1950-1953`) — the opposite of the acceptance test ("never terminal `done`"). S1 must Block the idle-same-boundary case **and** must not widen settle-anyway into Succeeded-on-idle.

### Why a smarter question heuristic is rejected

CARD-0159 (`docs/superpowers/plans/2026-08-30-card-0159-settlement-positive-evidence-plan.md:62-68`) explicitly rejected a "does this text look like narration/a question" classifier: the repo refuses generic shape-matching (CARD-0047), and a future-tense / "please approve" / "I'll begin" heuristic would misread half the real reports. The incident sentence is the exhibit: it is an approval wait in English and a statement to `LooksLikeAQuestion`. Leave that method alone; pin the sentence as a **negative** test.

Git is also not a gate (same CARD-0159 rejection; Plan/Review/investigation legitimately change nothing; Shared checkouts are unattributable, CARD-0227). CARD-0286's `IWorkspaceProgressProbe` may be **named** in the parent note (`git=no changes`) the way settlement already does; it must not decide Blocked vs nudge.

### Why CARD-0288 is a different shape

CARD-0288 (plan `ef69371a`, unimplemented) is "marked-done report already in the transcript, settlement never applied" → proposed `AttentionKind.ReportUnsettled = 21`. This card is "no marker at all". Reusing that kind would make the feed lie about which failure happened. Own kind, own predicate.

### AttentionKind numbering — live enum, not the plan docs

`AttentionDtos.cs:13-165` currently ends at shipped `QueuedInputStuck = 20`. Next free value is **21**. CARD-0288's plan also claims 21 on paper for `ReportUnsettled` and is unimplemented; CARD-0239's plan still claims 20 for `AgentOutlivedTask` and is unimplemented. Implementers of **all three** re-read `AttentionDtos.cs` at code time. Do not renumber.

---

## Slices

### S1 — Block unmarked idle-after-nudge on a 5-minute contract clock

**Classifier unchanged.** `LooksLikeAQuestion` is not edited. The first unmarked live end still nudges the child exactly once (CARD-0159 / CARD-0248). Check role stays exempt.

**New evidence** `AgentTaskReportEvidence.UnmarkedWaiting = 6` (`AgentTaskEnums.cs:176-198`): no closing line; the one nudge was issued; the session stayed idle on that boundary past the waiting clock. Distinct from `QuestionHeuristic` (trailing `?`) and `UnmarkedAfterNudge` (a **later** unmarked end settled Succeeded — that arm is untouched).

**New setting** `DelegationSettings.UnmarkedWaitingMinutes { get; set; } = 5`. `<= 0` disarms the Blocked sweep (S3 attention can still show). Not a new settings class — one number, same file as `ReportNudgeResponseSeconds` (`:423`) and `ReportSweepRehandSeconds` (`:433`). 5 minutes is the SLA the card asked for; it is far under `PastExpectedIdle`'s 30-minute floor and far over a legitimate "about to type the marker" pause. `ReportNudgeResponseSeconds` (240) is a different clock (text-less post-nudge **boundary**); do not reuse it.

**New sweep arm**, not settle-anyway. `SettleDeferredReportsAsync` (`AgentTaskDispatcher.cs:1383`) already selects Dispatched/Working sessions with a report-boundary TurnEnd every `PollIntervalSeconds` (5). After the existing arms, for each such task where:

- `ReportNudgedAt` is set (the one ask happened),
- `now - ReportNudgedAt >= UnmarkedWaitingMinutes` (clock from enqueue, **not** `SentAt` — a WhenIdle nudge that never typed because of a false-working verdict, CARD-0264, must not disable the Block),
- `IsWorkingAsync` is false (shared verdict; mid-turn is never waiting),
- no `AssistantText` on the session contains this task's report token (`DelegationReportFormatter.ReportToken` / `TryReadReportVerdict` — identity, not prose),
- newest report-boundary sequence is still `ReportNudgedSequence` (same turn; a later unmarked end is CARD-0248's settle-anyway, not this arm),
- role is not Check,

call a new `AgentTaskReplyService.BlockUnmarkedWaitingAsync(sessionId, ct)` — **do not** call `OnTurnEndAsync`. That method's same-boundary gate would no-op (`:1941-1943`) or, on a later boundary, Succeed (`:1950-1953`).

`BlockUnmarkedWaitingAsync`:

- Load the Dispatched/Working task; no-op if missing or already settled.
- `Status = Blocked`, `ReportEvidence = UnmarkedWaiting`, `Result` = the turn's final message (same extraction `ExtractMarkedTurnAsync` already did — the unmarked body the parent needs to read), `CompletedAt = now` (same as today's question-Blocked path in `SettleAsync` `:438-440`).
- Event `Blocked`: "Turn ended without `[antiphon-report:…]`; asked once and the session stayed idle. Waiting on a human."
- **Keep the session and worktree** — `AnswerAsync` is the resume (`SettleAsync` `:518, :539` already keep Blocked delegates).
- Cancel the closing-line nudge if that queue row is still `Pending` (`SessionMessageQueueService.CancelAsync` `:362`, or the same in-sweep cancel used when a task settles — `:815, :910`). A Blocked child must not later receive "send the closing line" as a surprise prompt; the parent replies.
- `DeliverToParentAsync` with the unmarked body, evidence header `unmarked`, and optional `git=` line from the existing `TryDescribeGitAsync` (observability only).
- `PublishAsync`. No merge-back, no `ReleaseDelegateAsync`.

`BlockedQuestion` attention and `BlockedTaskNotifier` then fire on the next poll with no extra wiring — they already key on `Status == Blocked`.

Kind-agnostic on purpose. A Plan/Review/Debug child that sits idle after the nudge is waiting too; Codex is not special-cased.

### S2 — Herdr done-handling: confirm and do not touch

No code. The plan's test matrix includes a pin that a Herdr `agent_status: "done"` plus an unmarked idle transcript does **not** by itself settle or Block — S1's clock and idle verdict do. `HerdrStatusCorroborationService` stays Warning-only disagreement. `docs/herdr-sessions.md` is not edited.

### S3 — Parent note at nudge; `AttentionKind.UnmarkedWaiting = 21` for the waiting window

**Parent note at first nudge.** In `NudgeForClosingLineAsync`, after the child enqueue succeeds, also enqueue on `task.ParentSessionId` when `ReplyTo == Session`: one WhenIdle Delegation note, Origin=Delegation, `SourceTaskId = task.Id`, body clearly **not** a `[task … done]` (e.g. `[task {short} waiting] Child ended a turn without the closing report line; asked once for \`[antiphon-report:{short} done|blocked|failed]\`. Session is idle. Reply after it Blocks, or Refine now.`). ConversationKey must **not** batch this with completion notes (`task:{root}` would fuse a wait-signal into a sibling's done-note — use a distinct key, e.g. `task-wait:{task.Id:N}`). CARD-0267's `CallerNoteUndelivered` then covers a busy parent for free.

This is the orchestrator-facing signal at T+0. Without it, S1's Blocked note only arrives at T+5 min, and a busy orchestrator still depends on WhenIdle.

**Attention.** Append `UnmarkedWaiting = 21` in `AttentionDtos.cs` (doc-comment: Dispatched/Working, closing-line nudge issued, session idle, no report token; detection only; not `ReportUnsettled`). Insert in `BuildOpenTaskItemsAsync` after `BriefUndelivered` and before `UncorrelatedReport` / `PastExpectedIdle`. Predicate:

- Status Dispatched or Working,
- session not dead (DeadSession already won),
- `ReportNudgedAt != null`,
- no report token for this task in `AssistantText`,
- `!IsWorkingAsync` (founding non-membership rule — mid-turn is not waiting).

No git gate. `SinceUtc = ReportNudgedAt`. Warning. Actions: `OpenDrawer`, `Retry`, `Cancel` — **not** `Reply` (that verb requires `Status == Blocked`; offering it here would 409). Headline: `Ended a turn with no closing line; asked once, still idle.` Evidence names the nudge age and that S1 will Block at `UnmarkedWaitingMinutes` if they stay idle.

Once S1 Blocks, the open-task pass no longer sees the row; `BuildBlockedAsync` emits `BlockedQuestion` instead. Self-clearing, no incident, no migration, not on the away-digest allow-list (`AwayDigestProjection.cs:36, :43`). Nav badge counts it (non-`RecentFailure`).

Client: `'UnmarkedWaiting'` on the `AttentionKind` union (`client/src/api/attention.ts:17`) and one `ATTENTION_VISUALS` entry — label "Waiting unmarked", color `warning`, icon `TbHourglassHigh` / `TbClockExclamation`, hint "The delegate ended a turn without done/blocked/failed and is idle. It will Block if it stays that way; do not read Herdr done as finished." Add the string to `attentionVisuals.test.ts`.

Do **not** parse check-interpreter readings. Do **not** reuse `ReportUnsettled`.

### Out of scope — auto-continue

The card's item 3 ("if matching user authorization exists, continue automatically only where that policy is explicit") is **not** that policy. Matching "start the remaining epics" to "please approve this design" is a judgement, not a row. `AnswerAsync` stays human/orchestrator-driven after Blocked. `RefineAsync` remains available during the 5-minute Dispatched window. No new "continue with prior authority" verb.

---

## What this card does not do

- **Does not change `LooksLikeAQuestion`.** The incident sentence stays a negative.
- **Does not add an approval/waiting prose classifier.**
- **Does not use git progress as a Blocked gate.** Named in the parent note only.
- **Does not touch Herdr status handling.**
- **Does not reuse or implement CARD-0288 `ReportUnsettled`.**
- **Does not auto-Answer from a prior user instruction.**
- **Does not let the check-interpreter write Status.**
- **Does not make settle-anyway Succeeded on the same idle boundary** — that stays forbidden (CARD-0248).
- **Does not add `AgentIncidentKind`, a hosted service, a column, or an EF migration** (evidence enum is an int on an existing column; 6 is a new member, no migration).

---

## Test matrix

Existing pins that must stay green: `DelegationQuestionDetectionTests` (trailing `?` Blocks; buried `?` does not; plain outcome does not), CARD-0248 same-boundary / undelivered-nudge never settles, CARD-0159 marked `blocked` / `done` / `failed`, CARD-0286 `CompletedWithoutProgress` on **marked** done + zero worktree progress (different arm — `verdict == "done"` only).

| Layer | Test |
|---|---|
| `Antiphon.Tests` unit | **Incident sentence:** `LooksLikeAQuestion("Please approve this design and I'll begin the recorded TDD cycles.")` is false. No other `LooksLikeAQuestion` behaviour changes. |
| `Antiphon.Tests` Application | **S1 happy:** Dispatched Code (or Plan — kind-agnostic) task, unmarked report-boundary, child nudged, clock +5 min, `IsWorkingAsync` false, no token → `Blocked`, `ReportEvidence = UnmarkedWaiting`, session still Running, worktree kept, parent note enqueued, pending child-nudge Canceled, `Blocked` event. `AnswerAsync` then resumes Working and types the reply. |
| `Antiphon.Tests` Application | **S1 clock:** at T+4 min 59 s still Dispatched; at T+5 min Blocked. `UnmarkedWaitingMinutes = 0` never Blocks. |
| `Antiphon.Tests` Application | **S1 mid-turn:** nudged, 10 min elapsed, `IsWorkingAsync` true → not Blocked. |
| `Antiphon.Tests` Application | **S1 later boundary:** a **new** unmarked TurnEnd after a delivered nudge still takes CARD-0248 settle-anyway (Succeeded / `UnmarkedAfterNudge`), not this arm. Pin so S1 cannot steal that path. |
| `Antiphon.Tests` Application | **S1 marked token:** child emits `[antiphon-report:id blocked]` (or done/failed) before the clock → existing marked path wins; S1 does not Block. |
| `Antiphon.Tests` Application | **S1 Check exempt:** Check role still never nudged, never UnmarkedWaiting-Blocked. |
| `Antiphon.Tests` Application | **S3 parent note:** first unmarked end → child nudge **and** parent WhenIdle note with `SourceTaskId`, distinct ConversationKey, body is not a done-note. Busy-parent later covered by existing `CallerNoteUndelivered` if it sits. |
| `Antiphon.Tests` Application | **S3 attention:** Dispatched + `ReportNudgedAt` + idle + no token → one `UnmarkedWaiting`, Warning, no `Reply`. After S1 Blocks → gone, `BlockedQuestion` instead. Mid-turn → none. Dead session → `DeadSession` wins. Shared-Postgres: seeded ids only. |
| Client vitest | `attentionVisuals.test.ts` totality picks up `UnmarkedWaiting`. |
| `Antiphon.Tests` Application | **S2 pin:** a fixture with Herdr `done` and an unmarked idle transcript does not settle or Block until the S1 clock; no call into Herdr status code. |

Run per `docs/testing-and-build.md`: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0294/` (forward slash), chunked `--treenode-filter "/*/Antiphon.Tests.Application/*/*"`. This card should not need `Antiphon.Agents.Pty.Tests`. Delete `bin-card0294` directories afterwards. Client: `pwsh -File scripts/test-client.ps1`.

---

## Sequencing and risks

S1 and S3 ship together — Blocked without the T+0 parent note recreates a 5-minute silent window; the note without Blocked leaves `Reply` 409ing until someone notices. S2 is documentation of a non-change. One PR.

| Risk | Standing |
|---|---|
| False Block of a delegate about to type the marker | 5 minutes idle after an explicit "if it is not finished, continue" nudge. Mid-turn is excluded. `AnswerAsync("continue")` is the recovery, session kept. |
| False Block of a long think that emits no rows | `IsWorkingAsync` is the shared mid-turn verdict. A provider that goes silent without a TurnEnd is a different card (stall / overdue). This arm requires the nudged report-boundary. |
| CARD-0248 settle-anyway vs S1 | Gated on **same** vs **later** boundary. Tests pin both. |
| WhenIdle parent note sits behind a busy orchestrator | CARD-0267 `CallerNoteUndelivered` already covers that. S3 attention is the 15 s poll that does not wait for the caller to go idle. |
| CARD-0288 implementer takes 21 | Live enum is source of truth. This plan takes 21 **today**; whoever lands second re-reads `AttentionDtos.cs` and takes 22. |
| Pending child-nudge types after Blocked | S1 cancels it. If cancel races with an in-flight send, the child may still see one "send the closing line" prompt — harmless; `OnTurnEndAsync` ignores Blocked. |
| Auto-continue temptation on Coesite-like briefs | Explicitly out of scope. Do not add it in review. |

---

## Execution notes

The original Coesite E-03 child was continued by hand at 08:48Z; nothing to repair live. After deploy, any Dispatched+nudged+idle+no-token task Blocks on the next tick past 5 minutes from `ReportNudgedAt` (including nudges issued before deploy — `ReportNudgedAt` is already on the row). That is intended catch-up, not a surprise Fail.

Implementer: do not trust this file or CARD-0288 for the next `AttentionKind` number. Read `server/Application/Dtos/AttentionDtos.cs`.
