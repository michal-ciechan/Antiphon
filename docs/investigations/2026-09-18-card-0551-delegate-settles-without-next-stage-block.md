# CARD-0551 Delegate "settles without the next-stage block": two mechanisms, neither is the delegate

Investigate, 2026-09-18, task `7ad5a936`, worktree `card-task-7ad5a936` at Antiphon `02c019ed`.
Read-only: the code at that commit, the delegation database (`antiphon-postgres`, UTC), the
server Serilog files under `C:\src\Antiphon\server\logs\` (timestamps there are +01:00), and the
live server (`GET /api/version` = `0a4e9fbd`). Nothing was changed; fix ideas are confined to the
last section.

Related: [2026-09-18-card-0527-commit-on-task-settle.md](2026-09-18-card-0527-commit-on-task-settle.md)
diagnosed the 2026-09-17 settlement throws independently the same night; this document confirms
that finding from the CARD-0551 side and adds the second mechanism.

## Verdict

**Both mechanisms confirmed, from stored rows, the server log and the code. The card's premise
is wrong for every one of its own five examples: no delegate omitted the `--- next stage ---`
block.**

1. **Case (a), the 2026-09-17 trio (`c9140e5d`, `b355c108`, `117a4690`) plus six siblings.** All
   nine delegates ended their final turn with a well-formed `--- next stage ---` block and the
   `[antiphon-report:<id> done]` token, and did so again on every refinement reply. Settlement
   reached `SettleAsync`, then `TryCommitOnSettleAsync` (CARD-0527 commit-on-settle) threw
   `ServiceUnavailableException("Settlement recovery requires one exact identity")` because the
   recovery `git log --all --reflog …` was killed by the 15 s git timeout in `C:\src\Antiphon`.
   The exception was swallowed at `OnTurnEndLockedAsync`, the task stayed `Dispatched` with an
   empty `Result`, and the report sweep re-handed the same boundary every 60 s and threw again:
   362 throws on 09-17, all nine tasks eventually Canceled by the orchestrator. **Already fixed**
   by `fde2a8d64` (2026-09-18 01:28Z, in the live server) — but that degrade path has not yet
   been exercised live (section 4).
2. **Case (b), the CARD-0424 Review pair (`a2e66829`, `0823444c`) and at least three earlier
   tasks.** A session wedged mid-turn breaches a `TaskDeadlinePolicy` phase clock (20 min
   model-wait or 90 min local-execution) or the 240 min ceiling. `TryFailOverdueAsync` Gate 3
   then calls the CARD-0085 bind-refusal recovery **without the zero-transcript guard the other
   two call sites have**, the recovery's JSONL arm matches on nothing more than "this task's
   brief was typed into this session", and the task is written `Succeeded` with a `Result` of
   "Recovered from an unbound session" and a Warning that claims "zero ingested transcript rows"
   while 46 and 59 rows had been ingested. The path cannot distinguish "done, badly formatted"
   from "wedged" because it never looks at the transcript at all, and it fires only on sessions
   that are provably mid-turn.
3. **Ask #1** — the refinement message carries no reporting-contract text, and the closing-line
   nudge asks only for the token, not the block; neither mattered, because the delegates included
   the block on every refinement reply anyway. **Ask #2** — an artifact-file fallback would have
   changed nothing in any of the fifteen cases examined. **Ask #3** — the fable-only pattern is a
   confound: every affected 09-17 task was Frontier **and Shared in `C:\src\Antiphon`**, and every
   opus task that day ran in a Worktree; the slow git call was in the shared checkout.

## 1. Case (a): the report was there, settlement threw

### 1.1 The transcripts

Every affected session's final assistant message ends with the block and the token. Stored
`TranscriptEntries` (session → task), tail of the text:

| task | seq | UTC | tail of `Text` |
|---|---|---|---|
| `c9140e5d` | 87 | 09-17 08:56:52 | `--- next stage ---` / `next: decide` / `handoff: Decide OQ-1..OQ-6 …` / `artifact: docs/superpowers/plans/2026-09-17-card-0545-…md` / `[antiphon-report:c9140e5d done]` |
| `c9140e5d` | 91 | 09:18:11 (reply to refinement 1) | same block, same token |
| `c9140e5d` | 94 | 09:42:43 (reply to refinement 2) | same block, same token |
| `b355c108` | 132 | 10:30:34 | `next: plan` … `artifact: docs/investigations/2026-09-17-card-0547-…md` / `[antiphon-report:b355c108 done]` |
| `b355c108` | 135 | 13:21:52 (reply to refinement) | same |
| `117a4690` | 45 | 13:34:40 | `next: test-design` … `[antiphon-report:117a4690 done]` |
| `117a4690` | 48 | 13:38:36 (reply to refinement) | same |

Each final text row shares its `ApiCallId` with the `TurnEnd` row that follows it
(`stop_reason=end_turn`), and both were ingested within 2 s of their own timestamp (`CreatedAt`
08:56:54 for seq 87/88), so the live observer saw a complete, marked, correlated turn. The brief
prompt (seq 1) carries the `[antiphon-task:<id>]` marker, so the marker gate in
`ExtractMarkedTurnAsync` passed.

The card says the refinement replies were "ANOTHER plain-text summary … still without the
structured block". Rows 91, 94, 135 and 48 above are those replies; each carries the block.

### 1.2 The throw

Server log, `antiphon-20260917.log` (+01:00), first occurrence:

```
09:57:16.219 [WRN] git log --all --reflog --fixed-strings --all-match --grep=c9140e5d-…
             --grep=b2fa6fed… --format=%H timed out after "00:00:15" in C:\src\Antiphon; child killed
09:57:16.295 [WRN] Failed to settle a delegated task for session "93b80b28-…"
Antiphon.Server.Application.Exceptions.ServiceUnavailableException: Settlement recovery requires
one exact identity; inspect git history.
   at AgentTaskReplyService.TryCommitOnSettleAsync(...) AgentTaskReplyService.cs:line 3177
   at AgentTaskReplyService.SettleAsync(...) AgentTaskReplyService.cs:line 752
   at AgentTaskReplyService.OnTurnEndLockedAsync(...) AgentTaskReplyService.cs:line 201
09:57:58.968 [WRN] Task c9140e5d: marked report already in transcript at boundary 88 — re-invoking settlement
```

The chain, with anchors on this worktree's HEAD (`02c019ed`, which already contains the fix so
the throw line has moved from 3177 to 3184 and is now guarded):

1. `AgentSessionRuntime` → `AgentTaskReplyService.OnTurnEndAsync` → `ExtractMarkedTurnAsync`
   (`AgentTaskReplyService.cs:2145`) correlates the turn → `ClassifyReportAsync` (`:625`) reads the
   `done` token → `SettleAsync` (`:201`).
2. `SettleAsync` → `TryCommitOnSettleAsync` (`:3139`) → `GitWorkspaceService.FindSettlementCommitsAsync`
   runs the `git log --all --reflog …` search; `GitSettings.TimeoutSeconds` is 15
   (`server/Application/Settings/GitSettings.cs:6`); the child is killed; `existing.Succeeded` is
   false; `:3184` throws `settlement_recovery_unavailable`.
3. `OnTurnEndLockedAsync` catches every non-cancellation exception (`:203`) and logs
   "Failed to settle a delegated task". Nothing was persisted: no `Completed` or `Warning`
   event, `Result` empty, status still `Dispatched`, `ReportNudgedAt` null.
4. `AgentTaskDispatcher.SettleDeferredReportsAsync` arm 0 ("marked report already in
   transcript … re-invoking settlement") re-hands the unchanged boundary once per
   `ReportSweepRehandSeconds` (60, `DelegationSettings.cs:521`), and step 2 throws again.

Counts: `grep -c "Failed to settle a delegated task"` = 0 on 09-14/15/16, **362 on 09-17**
(09:57:16 → 22:20:43 +01:00, nine distinct sessions), 0 on 09-18.

### 1.3 The nine tasks

All nine: `AgentKind=ClaudeCode`, `ModelLevel=Frontier`, `Workspace=Shared`, `RepoPath`
`C:\src\Antiphon`, `Status=Canceled`, `ReportEvidence=Legacy(0)`, `ReportNudgedAt=null`,
transcript contains ≥1 `AssistantText` with both `--- next stage ---` and `[antiphon-report:`:

| task | role | dispatched (UTC) | marked final turn (UTC) | canceled (UTC) | throws |
|---|---|---|---|---|---|
| `c9140e5d` | Plan | 08:35:22 | 08:56:52 | 10:22:35 | 83 |
| `b355c108` | Investigate | 10:22:37 | 10:30:34 | 13:21:59 | 165 |
| `117a4690` | Plan | 13:22:06 | 13:34:40 | 13:48:46 | 14 |
| `4a2497b2` | TestDesign | 13:53:54 | — | 14:59:20 | (one of 38/22/17/12/6/5) |
| `91773d94` | Plan | 14:59:21 | — | 15:24:38 | " |
| `2b64d75a` | TestDesign | 15:25:02 | — | 16:16:52 | " |
| `0d3ff398` | Investigate | 19:37:51 | — | 19:54:18 | " |
| `b5cd5ee0` | Plan | 19:54:41 | — | 20:21:25 | " |
| `fb65210d` | Investigate | 20:21:26 | — | 21:20:48 | " |

The six throw counts for the last six sessions were not individually mapped; their sum with the
three above is 362.

### 1.4 Why the nudge, the watchdog and the check interpreter all stayed silent

- The closing-line nudge (`NudgeForClosingLineAsync`, `ClassifyReportAsync`) exists only for an
  **unmarked** report. These reports were marked; classification returned `Succeeded/Marked` and
  the throw happened later, inside `SettleAsync`.
- The delivery watchdog (`AgentTaskDispatcher.cs:1248`) needs `!started`; the brief prompt was
  ingested, so `started` was true. `TaskDeadlinePolicy`'s phase arm needs `IsWorkingAsync` true;
  the session's last row was a `TurnEnd`, so it read idle and only the 240 min ceiling applied —
  the orchestrator canceled before it.
- Every Check interpreter on these tasks (`ecdcf945`, `c431409a`, `15df6045`, …) is `Canceled`
  with `FailureReason` "The caller that asked for it stopped waiting", so no digest ever
  described the task either (CARD-0501 territory).
- The orchestrator's refinements were delivered (`SessionQueuedMessages` rows Sent within 50 ms
  of enqueue) and answered with a fresh, correctly formatted report each time; each new boundary
  re-entered step 2 and threw again. The only surfaces the orchestrator could read — the task
  row (`Result` empty) and the session buffer — never showed a settlement.

### 1.5 Ask #3: tier is a confound

ClaudeCode worker tasks dispatched 2026-09-17 00:00–23:00Z, by tier and workspace:

| tier | Shared | Worktree |
|---|---|---|
| Frontier (fable) | 12 Succeeded, **9 Canceled** | 14 Succeeded |
| High (opus) | 0 | 8 Succeeded, 2 Failed |
| Medium | 1 Succeeded | 0 |

Every opus task that day was a Code-role Worktree dispatch; the throw needs a Shared settle in the
Antiphon checkout (Worktree settles take a different route, see the CARD-0527 document §5). The
12 Frontier Shared successes are markdown-package tasks and pre-08:18 dispatches, per the same
document. Nothing in the settle path reads `ModelLevel`.

### 1.6 Ask #1: what the refinement and nudge texts say

- `DelegationReportFormatter.BuildRefinement` (`:919`) and `BuildRefinementPointer` (`:942`) carry
  the task marker and "do NOT end your turn just to acknowledge it"; they carry **no** reporting
  contract and no `StageHandoffContract` (`:332`). Only the brief (`ReportingContract`, `:424`)
  does.
- `NudgeForClosingLineAsync` asks for the `[antiphon-report:…]` token only, never the block.
- Observed effect: none. All five delivered refinement replies in this card's cases (rows 91, 94,
  135, 48 above; `0823444c`'s two refinements were never typed, see §2) contain the block.
- Population since 2026-09-08: every Succeeded/Marked stage-role ClaudeCode task has a stored
  `NextStage` (Frontier: Plan 18, Code 8, Review 26, Investigate 12, TestDesign 13; High: Plan 2,
  Code 16, Review 41, Investigate 5, TestDesign 3, Mutation 1). Zero stage tasks settled
  `UnmarkedAfterNudge`, `FinalMessageMissing` or `UnmarkedWaiting` in that window.
- Four stage tasks were nudged since 09-08 (`131d57ee`, `924d72d5`, `e28d4c3d`, `9edf8224`; all
  Frontier); all settled Marked with a block 3–13 min later. In all four the nudge fired on an
  **intermediate** boundary — `StopReason=max_tokens` (`131d57ee` seq 82/84, `924d72d5` seq
  134/136) or an `end_turn` record followed one second later by tool calls with the same
  `ApiCallId` (`e28d4c3d` seq 34, `9edf8224` seq 25) — while the delegate was still mid-work.
  `TranscriptKinds.IsReportBoundary` (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:441`)
  treats every non-cancelled `TurnEnd` as a report boundary. Not this card's defect; recorded so
  the "4 nudges, all fable" figure is not misread as footer omissions.

## 2. Case (b): an overdue mid-turn session is "recovered" as Succeeded

### 2.1 The two CARD-0424 reviews

| | `a2e66829` | `0823444c` |
|---|---|---|
| dispatched (UTC) | 09-18 17:21:47 | 09-18 17:53:47 |
| rows ingested before recovery (`CreatedAt < RecoveredAt`) | 46 (17:22:11 → 17:33:01) | 59 (17:54:19 → 18:02:47) |
| last ingested row | seq 46 `ToolResult` @ 17:33:00.444 | seq 59 `ToolCall` Bash @ 18:02:46.609 |
| `IsWorkingAsync` | true (activity outranks the last `TurnEnd`) | true |
| `ClassifyPhase(last.Kind)` (`TaskDeadlinePolicy.cs:219`) | `ModelWait`, 20 min | `LocalExecution`, 90 min |
| `RecoveredAt` | 17:53:05.417 = last + **20m05s** | 19:32:48.298 = last + **90m02s** |
| `Status` / `ReportEvidence` / `NextStage` | Succeeded / Legacy(0) / null | same |
| `Result` | "Recovered from an unbound session; work is at transcript file …5a15388a….jsonl. C1–C4 were not changed." | same shape, …67f40c03….jsonl |
| Warning event | "…recovered from an unbound session (**zero ingested transcript rows**)…" | same |
| also | "Merge-back failed: source_branch_mismatch" | same; incident kind 32 at 18:32:48 "Working with no novel progress for 30m … 1 refinement waiting" |
| refinements | none | two (18:20:07, 19:00:25) — `Status=Pending`, `SentAt=null`, `DeliveryAttempts=0`: `WhenIdle` never types into a session that reads working, so the operator's "select No (2)" never reached the prompt |
| session afterwards | retired 60 min later: "reads mid-turn but has been silent since 17:33:01" | "…silent since 18:02:47" |

Log (`antiphon-20260918.log`, +01:00), the recovery itself:

```
18:53:06.536 [WRN] Task a2e66829 recovered from bind refusal (transcript file …5a15388a….jsonl); settled Succeeded. Session not killed.   (AgentTaskReplyService)
18:53:06.652 [WRN] Task a2e66829 recovered from an unbound session (transcript file …); C1–C4 were not changed. Session "5a15388a-…" was not killed.   (AgentTaskDispatcher)
20:32:49.404 [WRN] Task 0823444c recovered from bind refusal (…67f40c03….jsonl); settled Succeeded. Session not killed.
```

### 2.2 The path, reconstructed

`TryRecoverBindRefusalAsync` (`AgentTaskDispatcher.cs:4309`) has three callers:

| caller | guard | applies here? |
|---|---|---|
| delivery watchdog, `:1248` | `!started` — no turn prompt since dispatch | no: the brief is seq 1 of both transcripts |
| dead-session reconciler, `:1589` | `!hasTranscript` **and** the runner no longer serves the session | no: 46/59 rows; the runner served both sessions until their 60-min retirement |
| `TryFailOverdueAsync` Gate 3, `:1767-1770` | only "no unresolved commit-recovery obligation" | **yes** |

Gate 3 is reached only after `TaskDeadlinePolicy.EvaluateAsync` (`TaskDeadlinePolicy.cs:122`)
reports `Breached`, and its phase arm runs only when `IsWorkingAsync` is true (`:168`) — a
session that is mid-turn by the harness's own definition. The comment at `:1767` calls it "Same
call, same contract, as the two sweeps above", but the two sweeps above are guarded by the
absence of a transcript and this one is not. The commit message of `d1c8e3f4` / `29e5b45fd`
(2026-09-17, CARD-0547) touched the ordering of this gate; the transcript guard was never there.

Inside the recovery, `DelegateBindRefusalRecovery.TryFindAsync` (`DelegateBindRefusalRecovery.cs:50`):

- git arm (`:62`): for a Worktree task, any commit on the task branch counts. Both branches were
  fresh from `master` with no commits → empty.
- JSONL arm (`:102` → `TryMatchJsonl` `:171`): after the cwd (C2) and first-timestamp (C3) checks
  it returns true on the **first submitted-input record containing the task marker** (`:228`).
  The brief is such a record. For a ClaudeCode task whose brief was typed, this arm is always
  positive; it carries no information about completion. `Describe()` produces exactly the
  "transcript file …" wording seen in the events.

`AgentTaskReplyService.RecoverFromBindRefusalAsync` (`:927`) then writes `Succeeded`, the fixed
`Result`, and the Warning whose text at `:944` hard-codes "zero ingested transcript rows".
`PipelineHandoff.TryParse` on that `Result` finds no block, so the completion note carries
`next=unmarked` for a stage role (`PipelineHandoff.cs:118`).

### 2.3 Can it tell "done, badly formatted" from "wedged"? No, and it never meets the first

The recovery reads git and the JSONL; it never reads `TranscriptEntries`. The discriminator it
would need is already in the rows it ignores: a wedged session's last row is a `ToolCall` or
`ToolResult` (mid-turn, `IsWorkingAsync` true) and no `[antiphon-report:` text exists; a finished
but badly formatted delegate ends with an idle `TurnEnd` and is handled elsewhere —
`ClassifyReportAsync` nudges once, then settles `UnmarkedAfterNudge` or Blocks `UnmarkedWaiting`
(`BlockUnmarkedWaitingAsync`, `:3018`). The phase arm excludes `TurnEnd` tails by design
(`TaskDeadlinePolicy` `ClassifyPhase`), so Gate 3 in its phase form only ever sees mid-turn
sessions. The two populations do not overlap; the fix for each is different.

### 2.4 Population: every recovery since 2026-09-03 had a bound transcript

`AgentTasks` with `RecoveredAt` set, created since 2026-09-01:

| task | role / ws | since dispatch | since last row | last row | report token ever | rows before |
|---|---|---|---|---|---|---|
| `1f1c67b6` | Code / Worktree | **10m02s** | — | none | no | **0** |
| `a68e0a88` | Code / Worktree | **4h00m05s** | 3m | ToolCall | yes | 680 |
| `dfc6f878` | Code / Worktree | **4h00m04s** | 4m | AssistantText | yes | 74 |
| `7e73872f` | Code / Worktree | **4h00m05s** | 1m | AssistantText | **no** | 97 |
| `a5e37919` | Code / Worktree | **4h00m02s** | 1m | AssistantText | yes | 23 |
| `439ba588` | Custom / Shared | **4h00m06s** | 1m | ToolCall | **no** | 273 |
| `e67555ad` | Code / Worktree | **4h00m02s** | 8m | ToolCall | yes | 564 |
| `308ba49f` | Mutation / Worktree | 3h13m | **1h30m03s** | ToolCall | **no** | 515 |
| `a2e66829` | Review / Worktree | 31m | **20m05s** | ToolResult | **no** | 46 |
| `0823444c` | Review / Worktree | 1h39m | **90m02s** | ToolCall | **no** | 59 |
| `fbd37f59` | Custom / Shared | 4h05m | 1h32m | TurnEnd | yes | 272 |
| `a571d6c1`, `c420add0`, `62229bff`, `bdaadf67` | mixed | 2–5 days | 2–5 days | mixed | yes | 54–110 |

- One genuine CARD-0085 case (`1f1c67b6`: zero rows, delivery watchdog at
  `DeliveryFailTimeoutMinutes=10`).
- Ten recoveries land within 6 s of a `TaskDeadlinePolicy` limit: six at the 240 min role
  ceiling (`DelegationSettings.DefaultTimeoutMinutes` / `RolePolicyEntry.TimeoutMinutes`, both
  240), two at `LocalExecutionDeadlineMinutes=90`, one at `ModelWaitDeadlineMinutes=20`, plus
  `308ba49f` at 90 min after its last tool call. All ten had a bound, ingested transcript; the
  "zero ingested transcript rows" warning was false for every one of them.
- Five of those ten (`7e73872f`, `439ba588`, `308ba49f`, `a2e66829`, `0823444c`) never wrote a
  report token anywhere in their transcript: five tasks recorded `Succeeded` with no report and
  no unbound session. The card's two are the most recent, not the first.
- The five unmatched rows (`fbd37f59` and the four multi-day ones) were not traced to a path.

## 3. Ask #2: would an artifact-file fallback have helped?

No, in none of the fifteen cases:

- Case (a): the block was already in the final message and was parsed; the failure was an
  exception after classification. A second source for the same block changes nothing.
- Case (b): no report exists and, for `a2e66829` / `0823444c`, no review artifact was ever
  written (both sessions died inside their test batch). The fallback would find nothing.
- Structurally, `PipelineHandoff` is "enrichment, never a settlement gate" (`PipelineHandoff.cs:9`):
  the block never decides whether a task settles; the `[antiphon-report:…]` token does. Adding a
  block source cannot unstick a task.

## 4. State of the fix for case (a)

`fde2a8d64` (2026-09-18 01:28Z, "bound the settlement recovery search so a slow checkout degrades
instead of re-handing forever"): the four `settlement_recovery_unavailable` throws now fire only
when `holds` (a durable obligation or an already-found commit) is true; otherwise
`CommitSkippedNoteAsync` writes an "uncommitted … Commit-on-settle skipped" Warning and the task
settles once (`AgentTaskReplyService.cs:3175-3195`, `:3407`). `--reflog` is gone from the search
(`GitWorkspaceService.cs:944-979`).

- Live server `0a4e9fbd` (commit 09-18 09:40Z, restarts 10:31 and 10:42 +01:00) contains
  `fde2a8d64` (`git merge-base --is-ancestor`), and it is on `origin/master`.
- **Unexercised.** No Shared ClaudeCode task in `C:\src\Antiphon` has settled since 09-18 02:30Z
  (query on `Workspace=0`, `RepoPath`, `CompletedAt`), and `antiphon-20260918.log` has zero
  "Commit-on-settle skipped" lines. The zero throws on 09-18 are explained by the orchestrator's
  worktree-default policy, not by the fix having run.

## 5. Remaining uncertainties

1. The case-(a) degrade path is live but unobserved; the CARD-0527 document also reasons that the
   Worktree route shares the same search after a dirty tree. One deliberate Shared settle in the
   Antiphon checkout would close this.
2. `a2e66829`'s last ingested row is a `ToolResult`, not the Bash `ToolCall` that CARD-0566 says
   wedged on the `rm -rf` prompt; the JSONL tail was not read to see whether the tool_use record
   preceded the prompt. Does not affect the mechanism (the 20 min model-wait clock fired either
   way).
3. Whether `7e73872f`, `439ba588` and `308ba49f` were wedged or merely slow was not examined;
   they are cited only as "Succeeded with no report ever written".
4. The five unmatched recoveries in §2.4 (one at 4h05, four multi-day) were not traced; a Gate 1
   `ApiErrorRecovery` stand-down or a restart-time sweep delaying a ceiling breach are the
   plausible routes.
5. The four intermediate-boundary nudges in §1.6 (`max_tokens`, same-`ApiCallId` `end_turn`) are a
   separate settlement-shape question (CARD-0046 territory) and were not investigated.

## Not done, noted

- Case (b): give `TryFailOverdueAsync` Gate 3 (`AgentTaskDispatcher.cs:1770`) the same
  zero-ingested-transcript condition as `:1248`/`:1589` (or require `!IsWorkingAsync` and a
  `TurnEnd` tail), so an overdue mid-turn session takes the existing non-killing `Failed` tail
  ("session NOT killed — read it") instead of recovering as `Succeeded`; and have
  `RecoverFromBindRefusalAsync`'s warning state the actual ingested-row count.
- Case (b): make `DelegateBindRefusalRecovery`'s JSONL arm require an assistant record carrying
  the task's report token (or a stop-reason boundary after the brief), not the brief's own marker.
- Case (a): already fixed by `fde2a8d64`; observe one Shared Antiphon settle live to confirm the
  degrade path (CARD-0527 owns that).
- The wedge itself (unanswered `rm -rf` confirmation; prompt-state detection) stays with
  CARD-0566.
