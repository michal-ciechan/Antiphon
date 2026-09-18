# CARD-0551 case (b): an overdue mid-turn session must not be "recovered" as Succeeded

Plan task `199e8dad`, 2026-09-18, worktree `card-task-199e8dad` at `1666d39b` (= `origin/master`
plus the CARD-0551 investigation commit). Investigation:
[2026-09-18-card-0551-delegate-settles-without-next-stage-block.md](../../investigations/2026-09-18-card-0551-delegate-settles-without-next-stage-block.md)
(task `7ad5a936`). Read-only against the code; the delegation database was not re-queried, so the
two live examples are cited from the investigation. No fix was built. The verification design is
in this document (the brief asked for acceptance guards and a regression test plan), so the next
stage is Code.

## Disposition in five lines

1. **Gate 3 gets the dead-session reconciler's guard, verbatim.** `TryFailOverdueAsync` asks
   "does this session have any ingested transcript row" after Gate 2's pull, and calls the
   CARD-0085 recovery only when the answer is no (D-1). Every recovery the investigation traced
   to Gate 3 had a bound transcript; under D-1 each would have taken the existing non-killing
   Failed tail ("read session … before you decide").
2. **The JSONL arm needs a report, not a brief.** `DelegateBindRefusalRecovery` keeps its
   C1–C3 rules and the submitted-input needle match, and then requires a LATER assistant record
   whose text ends with this task's `[antiphon-report:<id> done]` line — the same
   `DelegationReportFormatter.TryFindReportToken` rule settlement applies to stored rows (D-2).
   A file that carries the brief and no report is "the delegate is working unbound", which is
   evidence against a kill, never evidence of completion (D-2b).
3. **The warning says what is true.** `RecoverFromBindRefusalAsync` counts the session's
   ingested rows and writes "zero ingested transcript rows" only when the count is zero (D-3).
4. **Case (a) is not replanned.** `fde2a8d64` is an ancestor of this branch; its degrade path is
   live and unexercised, and CARD-0527 owns the one deliberate Shared settle that confirms it (D-4).
5. **No new knobs, no new attention kind, no data repair** (D-5, D-6).

## Ground truth

Verified on `1666d39b` on 2026-09-18 (line numbers at that SHA) unless the row cites the investigation.

| Brief / investigation assumption | Observed | Consequence |
|---|---|---|
| Gate 3 is "around line 1770" and calls the recovery without a transcript guard. | `AgentTaskDispatcher.cs:1767-1771`: `if (pending.Count == 0 && await TryRecoverBindRefusalAsync(task, sessionId, ct)) return false;`. The only condition is the CARD-0547 obligation check. The comment calls it "Same call, same contract, as the two sweeps above". | D-1 adds the missing condition in place; the comment is corrected. |
| The other two call sites have a zero-ingested-transcript guard. | Delivery watchdog `:1248`: `!started`, where `started = span.TurnPrompts.Count > 0` — prompts **since dispatch** (`:1200`). Dead-session reconciler `:1585-1589`: `hasTranscript = TranscriptEntries.AnyAsync(session)` — **any** row, no date filter. The two guards differ. | D-1 uses the reconciler's any-row form: it is the claim the warning text makes, and the watchdog's since-dispatch form already owns the warm-session shape at 10 minutes (`DeliveryFailTimeoutMinutes`, `DelegationSettings.cs:379`). |
| Gate 3 is reached only for a mid-turn session. | `TaskDeadlinePolicy.EvaluateAsync` (`TaskDeadlinePolicy.cs:122`): the phase arm runs only when `IsWorkingAsync` is true (`:168`) and `ClassifyPhase` maps `UserPrompt/ToolResult/Thinking/AssistantText` → ModelWait (20 min) and `ToolCall` → LocalExecution (90 min) (`:219-236`); `TurnEnd` tails get the 240-minute ceiling only. The ceiling arm applies to any tail, including zero rows. | Gate 3 sees three populations: mid-turn with rows (phase or ceiling), idle with rows (ceiling), zero rows (ceiling). Only the third is CARD-0085's case; D-1 selects exactly it. |
| Gate 2 pulls the runner's transcript before Gate 3. | `:1756` `await CatchUpTranscriptAsync(sessionId, ct)` then a second `EvaluateAsync`; Gate 3 follows at `:1770`. | D-1's `AnyAsync` is placed after Gate 2 so it reads the freshest rows (CARD-0055: never judge on a stale transcript). |
| The JSONL arm matches on the brief marker alone. | `DelegateBindRefusalRecovery.TryMatchJsonl` (`:171-238`) returns `true` on the first submitted-input record (`user`, or `attachment` with `queued_command`, `:240-251`) matching `JsonlNeedles` (`:271-275`: bounded short id or the literal `[antiphon-task:<id>]` marker). `DelegationReportFormatter.TaskMarker` is the first line of every brief, so a typed ClaudeCode brief always matches. Nothing after the match is read. | D-2 keeps that match as the file-identity step and adds a completion step after it. |
| "Requiring a stop-reason boundary after the brief" is an adequate completion test. | `TranscriptNormalizer.cs:145-148` (CARD-0282): claude-fable-5 stamps `stop_reason:"end_turn"` on records of a tool-use response that is still mid-turn. Investigation §1.6: four nudges fired on `end_turn` records followed one second later by tool calls with the same `ApiCallId`. | Rejected in D-2: a stop reason says the response ended, not the task. |
| Settlement's own completion rule is reusable on a JSONL record. | `DelegationReportFormatter.TryFindReportToken(Guid, string?, out verdict)` (`:60-68`) → `TryReadReportVerdict` (`:70-`): the LAST non-empty line must be `[antiphon-report:<8-hex> done\|blocked\|failed]` and the id must be this task's. `TranscriptNormalizer.FromAssistant` (`:88-130`) reads `message.content[]` blocks of `type:"text"` into `AssistantText`; `thinking` and `tool_use` are separate kinds. | D-2 applies `TryFindReportToken` to the concatenated `text` blocks of an assistant record, so the file rule and the row rule are the same rule. |
| Under a stricter JSONL arm, a still-working unbound delegate is simply "not recovered". | Delivery watchdog tail (`:1305-1336`): after `TryRecoverBindRefusalAsync` returns false the task is Failed **and the session is killed** unless `withholdKill` is set — by the capability-mismatch branch (`:1254-1260`, CARD-0112) or by `IsWorkingAsync` (`:1308-1312`), which reads zero rows for an unbound session and returns false. | D-2 alone would turn today's false Succeeded into a kill of a live unbound worker (the CARD-0056 shape). D-2b maps "brief typed, no report yet" to `withholdKill`. |
| The recovery's warning is fixed text. | `AgentTaskReplyService.cs:943-947`: `"WARNING: this task was recovered from an unbound session (zero ingested transcript rows). …"`; the note at `:941-942` and the incident text at `:967-970` also say "unbound". The method receives `taskId` and `evidence` only; nothing counts rows. | D-3 counts rows in the method's own scope and parametrises the phrase. |
| Case (a) is already fixed. | `git merge-base --is-ancestor fde2a8d64 HEAD` succeeds on this branch; the investigation §4 found the live server `0a4e9fbd` contains it and no Shared Antiphon settle has run since. | D-4: noted, not replanned. |
| The two live examples are Gate 3 recoveries. | Investigation §2.1: `a2e66829` recovered at last row + 20m05s (ToolResult tail, model-wait clock), `0823444c` at last row + 90m02s (ToolCall tail, local-execution clock); 46 and 59 ingested rows; the "zero ingested transcript rows" warning on both. Not re-queried here. | The two acceptance shapes in T-2 reproduce those tails exactly, without the wait. |
| An overdue-but-working session needs a multi-hour wait to test. | `OverdueSweepHarness` (`tests/Antiphon.Tests/TestHelpers/OverdueSweepHarness.cs`) arms the limits at 100 000 / 50 000 / 60 000 / 40 000 minutes and accepts `claudeProjectsRoot:`; `AgentTaskOverdueDeadlineTests.Scenario` seeds `DispatchedAt` and `TranscriptEntries` back-dated by `MinutesAgo` (`:675-760`). `IsWorkingAsync` and `LoadLastEntryAsync` read rows only; Gate 2's pull tolerates the absent runner (every existing overdue test passes through it). The existing `a_mid_turn_session_past_the_model_wait_deadline_is_failed_naming_the_phase` (`:129-155`) is the recipe. | The regression tests below run in seconds. |
| The existing Gate 3 recovery test pins today's behaviour. | `bind_refusal_recovery_still_wins_over_the_deadline` (`:236-264`) seeds a Shared repo with a `CARD-0083` commit and **no transcript at all**; the ceiling is breached on the wall clock. | Stays green under D-1 unchanged: it is the zero-row population D-1 keeps. |
| Existing JSONL fixtures carry a report. | `zero_transcript_plus_later_jsonl_needle_recovers_without_ingesting` (`AgentTaskDeliveryWatchdogTests.cs:1639`) and `a_queued_command_attachment_is_jsonl_recovery_evidence` (`:1691`) write the brief plus `JsonlAssistant(cwd, "done.", …)` — no token. The four negatives (`:1743`, `:1782`, `:1820`, `:1858`) would fail for a second reason under D-2. | S2 adds a token-bearing assistant record to all six so each test keeps testing its own property. |
| Direct callers construct the evidence record. | `new DelegateBindRefusalEvidence(["abc1234"], null)` in `AgentTaskPoolTests.cs:457`, `PostLandMutationCustodyTests.cs:504`, `PostLandMutationWorktreeTests.cs:289`. | The record's shape is kept; the scan result is a new type. |
| A new test file that builds a dispatcher harness must use the shared graph. | `DelegationHarnessCensusTests` (`docs/testing-and-build.md:205`). `TryMatchJsonl` is `private static`; `Antiphon.Server` has `InternalsVisibleTo Antiphon.Tests`. | The new unit file tests `TryMatchJsonl` as `internal static` with no harness; sweep-level tests live in the two existing files. |

## Decisions

### D-1. Gate 3 recovers only a session with zero ingested transcript rows

In `TryFailOverdueAsync`, after Gate 2's pull and re-evaluation (`:1756-1765`), replace `:1767-1771` with:

```csharp
// Gate 3 — CARD-0085, and ONLY for the population CARD-0085 was written for: a session that
// ingested nothing. Same predicate as the dead-session reconciler. A session with rows is
// judged by its rows (the report path settles it; this sweep fails it, non-destructively) —
// CARD-0551 case (b): ten of eleven recoveries traced to this gate had a bound transcript.
// Skipped when the task holds a commit-recovery obligation (CARD-0547).
var hasTranscript = await _db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == sessionId, ct);
if (!hasTranscript && pending.Count == 0
    && (await TryRecoverBindRefusalAsync(task, sessionId, ct)).Outcome == BindRefusalOutcome.Recovered)
    return false;
```

with one Information log line when `hasTranscript` is true and the sweep proceeds ("… past its
deadline with {Count} ingested row(s); bind-refusal recovery not attempted"). The Failed tail
(`:1795-1803`) is unchanged: the reason names the clock and "The session was NOT killed — read
session …". Nothing else in the gate order moves.

Rejected:

- **Remove Gate 3.** It is the only recovery for a zero-row session when the delivery watchdog is
  off (`DeliveryFailTimeoutMinutes <= 0`), deferred (`LaunchResumedAt` inside the window,
  CARD-0340) or did not yet have evidence at 10 minutes. `bind_refusal_recovery_still_wins_over_the_deadline`
  pins that population.
- **Guard on `!IsWorkingAsync` and a `TurnEnd` tail instead.** The phase arm already implies
  working; for the ceiling arm with an idle tail and rows the report path (`ClassifyReportAsync`,
  `BlockUnmarkedWaitingAsync`) owns the outcome and the sweep's Failed is correct. The zero-row
  predicate subsumes both and matches the warning's claim.
- **Guard on rows since `DispatchedAt` (the watchdog's `started` form).** A warm-pool session
  with inherited rows and a refused brief is the watchdog's case at 10 minutes; at Gate 3 the same
  session either has rows since dispatch (judge them) or is the watchdog's leftover. The any-row
  form is the stricter of the two and is what the reconciler uses.
- **Query before Gate 2.** Cheaper, but it would judge stale rows; CARD-0055 forbids it.

### D-2. The JSONL arm requires this task's `done` report in a later assistant record

`DelegateBindRefusalRecovery.TryMatchJsonl` (`:171-238`) keeps C2, C3 and the submitted-input
needle match, but the match no longer returns. It sets `briefSeen = true` and the scan continues;
the method returns `true` only when, after `briefSeen`, a record with `type:"assistant"` has
`message.content[]` `text` blocks whose concatenation (joined with `\n`) satisfies
`DelegationReportFormatter.TryFindReportToken(taskId, text, out verdict)` with `verdict == "done"`.
EOF without that record returns `false`. The needle list and `IsSubmittedInputRecord` are unchanged.
`TryMatchJsonl` gains a `Guid taskId` parameter and becomes `internal static` for the unit tests.

Excluded by construction and pinned by tests: the token inside a `user` or `attachment` record
(the brief quotes it); the token inside `thinking` or `tool_use` blocks (a delegate writing its
report to `.antiphon/task-<id>.md` through the Write tool carries the token in `tool_use.input`);
a token naming another task; a token before the brief record; `blocked` / `failed` verdicts.

`TryFindAsync` (`:50-60`) returns a new `DelegateBindRefusalScan(DelegateBindRefusalEvidence? Recovery, string? UnreportedJsonlPath)`:
`Recovery` is non-null when the git arm found commits or the JSONL arm found a report (the
existing `DelegateBindRefusalEvidence(Commits, JsonlPath)` record, shape unchanged);
`UnreportedJsonlPath` names the first file that passed C1–C3 and the brief match but had no
`done` report. `Describe()` for a JSONL hit reads `transcript file <path> (reported done)`.

Rejected:

- **A `stop_reason` boundary after the brief.** CARD-0282 and investigation §1.6: `end_turn` is
  stamped on mid-turn records. A boundary is not a report.
- **Any assistant record after the brief.** Activity is what the two live examples had (46 and
  59 rows of it). The distinguishing fact is the report, and settlement already knows how to read it.
- **Accept `blocked` / `failed` as recovery evidence.** Recovery writes Succeeded; a `failed`
  verdict recovered as Succeeded is a lie the caller acts on. Those files stay "unreported" (D-2b);
  the Failed tails point the human at the session.
- **The token anywhere in the record's text rather than on the last line.** `TryReadReportVerdict`
  is settlement's rule; two rules for one token is the defect this repo's standing rule forbids.

### D-2b. An unbound file that carries the brief but no report withholds the watchdog's kill

`AgentTaskDispatcher.TryRecoverBindRefusalAsync` (`:4309-4334`) returns
`BindRefusalResult(BindRefusalOutcome Outcome, string? UnreportedJsonlPath)` with
`BindRefusalOutcome { NoEvidence, Recovered, UnreportedActivity }`. `Recovered` behaves exactly as
today's `true` (settle, detach, log). `UnreportedActivity` is returned when `Recovery` is null and
`UnreportedJsonlPath` is set; `NoEvidence` otherwise.

Call sites:

- Delivery watchdog (`:1248`): `Recovered` → `continue` (unchanged). `UnreportedActivity` →
  `withholdKill = true` and `reason` = "Boot prompt reached the session but its transcript is
  unbound: {timeout} minutes after dispatch Antiphon ingested no transcript row for this task, while
  transcript file {path} carries this task's brief and no closing report token yet. The delegate may
  be WORKING with no transcript bound to read (CARD-0064); the session was NOT killed — read it
  before re-running." This is the CARD-0112 branch's shape (`:1254-1260`), evaluated before it.
  `NoEvidence` → today's branches.
- Dead-session reconciler (`:1589`): `Recovered` → unchanged. Anything else → proceed; the session
  is already dead, so the kill question does not arise. The `UnreportedActivity` path appends
  "transcript file {path} carries the brief and no report" to the classified reason.
- Gate 3 (`:1770`, after D-1): `Recovered` → `return false`. Anything else → the existing
  non-killing Failed tail; `UnreportedActivity` appends the same pointer to `reason`.

Rejected:

- **Keep the kill.** The whole CARD-0085/CARD-0056 line is that an empty table is not evidence the
  work did not happen; killing on it is the disaster that line exists to prevent.
- **Defer the task to the ceiling instead of failing it.** Four hours of a Dispatched row with no
  attention and no note, for a session Antiphon cannot observe. Failed-not-killed at 10 minutes
  with the file path tells the orchestrator now, and the work on disk is untouched either way.

### D-3. The recovery text states the real ingested-row count

In `RecoverFromBindRefusalAsync` (`:927-1000`), after the task is loaded and before the strings are
built: `var ingested = task.AgentSessionId is Guid sid ? await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == sid, ct) : 0;`.
The note, the Warning event, the incident text and the two log lines use one phrase:

| `ingested` | phrase |
|---|---|
| 0 | `recovered from an unbound session (zero ingested transcript rows)` — today's text, unchanged |
| N > 0 | `settled from workspace evidence with a bound transcript ({N} ingested transcript row(s); no report was read)` |

Everything else in the method — Succeeded, `RecoveredAt`, the incident kind, the non-killing
release, merge-back — is unchanged. Under D-1/D-2 every sweep reaches this method with zero rows;
the N > 0 phrase exists for the method's public contract (three tests call it directly) and so
that no future caller can make the text lie.

Rejected: **refuse at the settlement layer when N > 0.** The sweeps own the evidence guard; a
refusal here would leave a caller-approved settlement neither Succeeded nor Failed, with the task
stranded open and nothing on the board to say why.

### D-4. Case (a) is already fixed; confirmation stays with CARD-0527

`fde2a8d64` ("bound the settlement recovery search so a slow checkout degrades instead of
re-handing forever") is on this branch and in the live server per the investigation. Its degrade
path has not run live because no Shared ClaudeCode task has settled in `C:\src\Antiphon` since
2026-09-18 02:30Z. The CARD-0527 plan (D-6, "observe one Shared Antiphon settle live") owns that
confirmation; nothing here re-plans it.

### D-5. No new configuration, no new attention kind, no incident kind

The two new refusals ("rows present, recovery not attempted"; "brief typed, no report") are one
log line each plus, where a task is failed, a sentence in the reason the caller already receives.
`DelegateBindRefusalRecovered` (kind 24) keeps its meaning; the phrase in D-3 is the only text change.

### D-6. Out of scope, noted

- The wedge itself (an unanswered `rm -rf` confirmation, prompt-state detection): CARD-0566.
- The four intermediate-boundary nudges (`max_tokens`, same-`ApiCallId` `end_turn`): CARD-0046 territory.
- The five untraced recoveries in investigation §2.4 and whether `7e73872f` / `439ba588` /
  `308ba49f` were wedged or slow.
- Repairing the historical rows (`a2e66829`, `0823444c` and the eight earlier false Succeeded
  recoveries): the orchestrator's call; no migration is planned.
- The Failed tail's reason text for the general deadline is unchanged; the "Failed, not escalated
  and not retried" policy is CARD-0020's and is not revisited.

## Components (exact)

| File | Change |
|---|---|
| `server/Application/Services/AgentTaskDispatcher.cs` `:1767-1771` | D-1 `hasTranscript` guard after Gate 2; comment rewritten; pointer suffix on the Failed reason (D-2b). |
| `server/Application/Services/AgentTaskDispatcher.cs` `:1240-1260` | D-2b `UnreportedActivity` → `withholdKill` + reason, before the CARD-0112 branch. |
| `server/Application/Services/AgentTaskDispatcher.cs` `:1585-1596` | D-2b reconciler reads `.Outcome == Recovered`; pointer suffix. |
| `server/Application/Services/AgentTaskDispatcher.cs` `:4309-4334` | `TryRecoverBindRefusalAsync` returns `BindRefusalResult`; new `BindRefusalOutcome` enum (file-local or `Application/Services`). |
| `server/Application/Services/DelegateBindRefusalRecovery.cs` `:50-60`, `:102-144`, `:171-238`, `:362-375` | D-2 completion step, `DelegateBindRefusalScan`, `TryMatchJsonl(…, Guid taskId)` internal, `Describe()` suffix. |
| `server/Application/Services/AgentTaskReplyService.cs` `:927-1000` | D-3 `ingested` count and phrase in note, warning, incident, logs. |
| `tests/Antiphon.Tests/Application/DelegateBindRefusalRecoveryTests.cs` (new, harness-free) | J-1..J-9 against `TryMatchJsonl`. |
| `tests/Antiphon.Tests/Application/AgentTaskOverdueDeadlineTests.cs` | T-1..T-4; `Scenario.SeedTaskAsync` unchanged (Shared task, `workingDirectory` = cwd). |
| `tests/Antiphon.Tests/Application/AgentTaskDeliveryWatchdogTests.cs` `:1639-1910` | Fixtures gain a `done` record (six tests); W-1..W-3 new. |
| `tests/Antiphon.Tests/Application/AgentTaskDeadSessionReconciliationTests.cs` `:665`, `:702-706` | R-2 (one case). `Scenario.Harness(params Guid[] gone)` hard-codes the projects root to an empty `antiphon-deadsession-no-jsonl` dir; give it an optional `claudeProjectsRoot` parameter (default unchanged) so R-2 can point it at a fixture. |
| `tests/Antiphon.Tests/Application/AgentTaskPoolTests.cs` (next to `:441`) | R-1 direct-call text pin. |
| `docs/session-runtime-invariants.md` after the CARD-0221 bullet (`:168-175`) | One invariant bullet (S4). |
| `docs/orchestration-loop.md` after the boot-stall paragraph (`:553-566`) | One paragraph on the general deadline's outcome (S4). |

## Slices

### S1. Gate 3 guard (D-1) — lands alone, is the production fix for the two live examples

`AgentTaskDispatcher.cs:1767-1771` only, keeping `TryRecoverBindRefusalAsync`'s `bool` until S2.
Tests T-1, T-2 (two `[Arguments]` variants), T-3, T-4 in `AgentTaskOverdueDeadlineTests`;
`bind_refusal_recovery_still_wins_over_the_deadline` unchanged and green.

### S2. JSONL completion evidence and the watchdog's kill guard (D-2, D-2b)

`DelegateBindRefusalRecovery.cs`, `AgentTaskDispatcher.cs` (three call sites and the helper's
return shape; S1's Gate 3 line takes the `.Outcome == Recovered` form here). New
`DelegateBindRefusalRecoveryTests` J-1..J-9; fixture updates and W-1..W-3 in
`AgentTaskDeliveryWatchdogTests`; R-2 in `AgentTaskDeadSessionReconciliationTests`.

### S3. Truthful recovery text (D-3)

`AgentTaskReplyService.cs:927-1000`; R-1 next to the existing direct caller in `AgentTaskPoolTests`.
The three existing direct-call tests stay green (they seed no rows; the phrase is the zero-row one).

### S4. Docs

- `docs/session-runtime-invariants.md`: "**A bind-refusal recovery needs zero ingested rows and a
  `done` report** (CARD-0551): all three sweeps (delivery watchdog, dead-session reconciler,
  overdue Gate 3) attempt `RecoverFromBindRefusalAsync` only for a session with no ingested
  transcript row; the JSONL arm accepts a file only when a later assistant record ends with the
  task's `[antiphon-report:<id> done]` line; a file with the brief and no report withholds the
  watchdog's kill and fails the task with the file path. Live miss 2026-09-18: two mid-turn
  Reviews with 46 and 59 rows were written Succeeded at exactly the phase-clock boundary."
- `docs/orchestration-loop.md`: after the boot-stall paragraph, one paragraph: the general
  model-wait (20) and local-execution (90) clocks and the 240-minute ceiling fail the task without
  killing or retrying, the reason names the clock and the session, and no recovery is attempted on
  a session that has ingested rows.
- `docs/cards/` is generated; do not edit.

Verification profile for Code (ordinary): `AgentTaskOverdueDeadlineTests`,
`AgentTaskDeliveryWatchdogTests`, `AgentTaskDeadSessionReconciliationTests`, `AgentTaskPoolTests`,
`TaskDeadlinePolicyTests`, `DelegateBindRefusalRecoveryTests`, `PostLandMutationCustodyTests`,
`PostLandMutationWorktreeTests`, `DelegationHarnessCensusTests`, each with
`--treenode-filter "/*/*/<Class>/*"`; the full `Antiphon.Tests` assembly is not required
(CARD-0110 profile). Build to an alternate output path. Re-verify any new red at `1666d39b` before
attributing it.

## Verification design

### How an overdue-but-still-working session is simulated (no wait)

`OverdueSweepHarness.Create(claudeProjectsRoot: <temp>)` arms the ceiling at 100 000 min,
model-wait at 50 000, local-execution at 60 000, boot-wait at 40 000. `Scenario.SeedTaskAsync(dispatchedMinutesAgo:, workingDirectory:)`
back-dates `DispatchedAt` and creates the session with `Cwd = workingDirectory`,
`StartedAt = DispatchedAt`, `AgentKind = ClaudeCode`. `Scenario.SeedEntriesAsync((Kind, Text, MinutesAgo)…)`
back-dates rows. The sweep reads rows only (`IsWorkingAsync`, `LoadLastEntryAsync`); Gate 2's
runner pull is tolerant of the absent runner. Two shapes reproduce the live examples:

| shape | rows | clock that fires |
|---|---|---|
| `a2e66829` | `(AssistantText, "earlier work", 69_500)`, `(UserPrompt, brief, 69_000)`, `(ToolCall, "Bash", 51_500)`, `(ToolResult, "…", 51_000)` | ModelWait: tail `ToolResult`, age 51 000 ≥ 50 000; ceiling 70 000 < 100 000 |
| `0823444c` | same head, `(ToolCall, "Bash", 61_000)` as the tail | LocalExecution: tail `ToolCall`, age 61 000 ≥ 60 000 |

Both read `IsWorkingAsync == true` (activity outranks the absent `TurnEnd`) and are not boot turns
(the `AssistantText` row precedes the brief). A JSONL fixture lives at
`<projectsRoot>/<EncodeClaudeProjectDir(cwd)>/<sessionId>.jsonl`, written with the existing
`JsonlUser` / `JsonlQueuedCommand` / `JsonlAssistant` helpers (copied into the overdue file or
lifted into a shared helper), timestamps `DateTime.UtcNow` (after `StartedAt`, so C3 passes).
A git fixture is `ScratchGitRepo` with a `CARD-0083` commit, as the existing Gate 3 test does.

### Acceptance cases

| ID | Slice | Setup | Expected |
|---|---|---|---|
| T-1 | S1 | Shared task, `ScratchGitRepo` with a `CARD-0083` commit (the existing Gate 3 fixture), **plus** rows: the `a2e66829` shape | `Failed`; `FailureReason` contains `waiting on the model` and `NOT killed`; `RecoveredAt` null; no `Warning` event; no kind-24 incident; `stopper.Killed` empty; `Result` null. |
| T-2 | S1 | `[Arguments]` over the two shapes; task cwd = temp dir; JSONL file with the brief (`JsonlUser`, marker) and an assistant record ending with `[antiphon-report:<id> done]` | `Failed` for both; never `Succeeded`; the warning text `ingested transcript rows` absent from every event of the task. (Red today for both variants: the file recovers it.) |
| T-3 | S1 | Zero rows; ceiling breached (`dispatchedMinutesAgo: 150_000`); JSONL with brief + `done` record | `Succeeded`, `RecoveredAt == CompletedAt`, `Result` contains the file path; not killed. (The CARD-0085 population survives D-1 at Gate 3.) |
| T-4 | S1 | Rows present (idle tail: brief then `TurnEnd`, 149 000 / 148 000), ceiling breached, JSONL with brief + `done` | `Failed` (ceiling), not recovered — the report path owns an idle bound session; the sweep must not read the file. |
| J-1 | S2 | `TryMatchJsonl`: `user` marker record, then assistant text `"Report…\n[antiphon-report:<id> done]"` | `true`. |
| J-2 | S2 | `user` marker record only, then assistant `"done."` (today's fixture) | `false`. |
| J-3 | S2 | `user` record whose last line is the `done` token (no assistant record) | `false`. |
| J-4 | S2 | brief, then assistant record with the token only in a `thinking` block; variant with it only in `tool_use.input` | `false` for both. |
| J-5 | S2 | brief, then assistant text ending with `[antiphon-report:<other-id> done]` | `false`. |
| J-6 | S2 | brief, then assistant text ending with `blocked`; variant `failed` | `false` for both. |
| J-7 | S2 | assistant `done` record **before** the brief record, nothing after | `false`. |
| J-8 | S2 | `queued_command` attachment brief, then assistant `done` | `true`. |
| J-9 | S2 | C3-refused first timestamp, brief and `done` present | `false` (the existing refusal still stops the read). |
| W-1 | S2 | Watchdog, zero rows, JSONL brief + `done` (updated `zero_transcript_plus_later_jsonl_needle_recovers_without_ingesting` and `a_queued_command_attachment_is_jsonl_recovery_evidence`) | `Succeeded`; `Result` contains the path and `reported done`; zero `TranscriptEntries`; not killed. |
| W-2 | S2 | Watchdog, zero rows, JSONL brief only (today's fixture content) | `Failed`; `FailureReason` contains `transcript is unbound` and the file path; **not killed** (`stopper.Killed` excludes the session); no kind-24 incident. (Red today: recovers.) |
| W-3 | S2 | The four existing negatives (`753cdb4e` Codex, another-known-session, card-id-in-assistant, C3-refused) with a `done` record added to each file | Each still `Failed` and killed exactly as asserted today — each fails for its own rule, not for the missing report. |
| R-1 | S3 | Direct `RecoverFromBindRefusalAsync` with two seeded rows on the session | `Succeeded`; the Warning event and `Result` contain `2 ingested transcript row(s)` and do not contain `zero ingested`; the zero-row sibling (existing `a_recovered_worktree_task_marks_its_delegate_for_retirement`) still says `zero ingested transcript rows`. |
| R-2 | S2 | Dead-session reconciler, zero rows, JSONL brief only, past grace | `Failed` with the classified reason plus the file pointer; no recovery; `Runner.Killed` empty. |
| DOC-1 | S4 | — | The two doc paragraphs exist and name `zero ingested`, `done`, and `NOT killed`. |

### Guard candidates for Mutation (method-scoped PCs)

- Drop `!hasTranscript` from Gate 3 → T-1 and T-2 red.
- Invert it (`hasTranscript &&`) → T-3 red.
- Query `hasTranscript` before Gate 2's pull → not observable in the harness (no runner); Review
  confirms by reading, and the comment says why the order matters.
- `TryMatchJsonl` returns on the brief match → J-2, J-7, W-2 red.
- Accept the token in a `user` record → J-3 red.
- Accept `thinking` / `tool_use` blocks → J-4 red.
- Accept any verdict → J-6 red.
- Drop the "after the brief" ordering → J-7 red.
- Map `UnreportedActivity` to `NoEvidence` in the watchdog → W-2 red (killed).
- Hard-code the zero-row phrase → R-1 red.

### Live confirmation after land (read-only, for the Review or the orchestrator)

- `antiphon-2026MMDD.log`: no `recovered from an unbound session` line for a session with rows;
  every future Gate 3 breach on a mid-turn session logs "ingested row(s); bind-refusal recovery
  not attempted" and the task shows `Failed` with the clock-named reason.
- Delegation DB: `AgentTasks` with `RecoveredAt > <land time>` joined to a non-zero
  `TranscriptEntries` count for the same session must be empty.

## Scope boundaries

In: D-1, D-2, D-2b, D-3, D-5, S1..S4. Out: D-4 (CARD-0527), D-6 items, any change to
`TaskDeadlinePolicy`'s clocks or classification, the boot arm (CARD-0353), the report path
(`ClassifyReportAsync`, nudges), the git arm of the recovery, and the CARD-0547 obligation hold.

## Cost and next stage

Code: about 2 hours including the fixture updates and nine targeted runs (about 1–3 minutes per
class). S1 is the production fix on its own and may land first if S2's fixture work runs long.
Review: ordinary, different company from Code. Mutation guards above are owed after land.
