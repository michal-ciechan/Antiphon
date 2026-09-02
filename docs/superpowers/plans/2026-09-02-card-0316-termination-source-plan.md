# CARD-0316 — Never leave `TerminationSource.Unknown` when the platform made the stop decision

**Date:** 2026-09-02 (Plan pass, task 15c3cb72 — design only; no production code changed)
**Card:** CARD-0316 "StoppedBeforeFirstPrompt leaves TerminationSource.Unknown - the platform's own stop decisions go unrecorded"
**Investigation:** task 46621314 (`.antiphon/task-46621314.md`). Its call-site catalog is taken as complete; this pass re-read every site it names to fix the exact edit at each, and did not re-investigate.

---

## Decision

Two stacked gaps, fixed in the order that changes the next incident's message first.

1. **The classifier lies about stamped rows.** `AgentTaskLiveness.ClassifyFailure` (`server/Application/Services/AgentTaskLiveness.cs:94-110`) emits "the stop origin was not recorded" for **every** non-`OperatorRequest` empty `Stopped` row, including rows that already carry `SystemRequest` or `ProcessExit`. Until this switch names the source, stamping more writers changes nothing an operator can see. **S1.**
2. **Twelve platform closers write a terminal status without a source.** They kill through `adapter.KillAsync` / `_runtime.KillAsync` / `KillAndDisposeAsync`, or close a row whose process is already gone, and never touch `TerminationSource`. **S2**, through one shared stamp helper so the first-writer-wins rule has one home instead of fifteen copies.

**No new enum members.** `SystemRequest` already means "the platform decided"; `ProcessExit` already means "the process left and nobody had asked". Which watchdog decided is already in `FailureReason` on every `Failed` writer; the one `Stopped` writer with no reason (`MarkSuccessfulRuntimeStopped`) is a success path.

**Optional items, decided:**

- `terminationSource` on `AgentSessionSummaryDto` — **in scope (S3).** The card's own ask is that the incident record says what stopped the session instead of someone reverse-engineering it from `:17204/sessions/{id}`; the investigation could not even confirm the two CARD-0315 columns because the API omits the field. It is one additive optional record parameter, two builder sites, one TS field, one doc line.
- A source overload on `IDelegateSessionStopper.KillAsync` — **out of scope, follow-up.** Its only callers are `AgentTaskService.CancelAsync` (`:940`) and the escalation path (`:1079`). Neither knows who is calling: `POST /api/agent-tasks/{id}/cancel` is hit by humans and by orchestrator agents alike, and CARD-0256's rule is that `OperatorRequest` is written only on evidence, never guessed. An overload with no caller that can honestly pass anything but `SystemRequest` is dead API. When the cancel endpoint grows an actor, add the overload then.

---

## Ground truth (checked, not guessed)

### The rule the working writers already follow (CARD-0256)

`AgentSessionService.KillAsync(sessionId, source, ct)` (`AgentSessionService.cs:782-796`) stamps `source` **before** asking the runner to kill, and only when the row is still `Created|Starting|Running|Stopping` with `Unknown` on it. `CloseSessionOnExitAsync` (`AgentSessionRuntime.cs:172-173`) and the reconciler's Exited arm (`SessionReconciliationService.cs:194-195`) stamp `ProcessExit` only if still `Unknown`. First writer wins; an exit event never erases a request. Every new stamp in S2 obeys the same rule, and every existing stamp is re-expressed through the S2 helper so the rule stays in one place.

### Why the CARD-0315 rows read "not recorded"

Runner still holds both sessions as `Exited`, `exitCode=0`, `exitReason=ProcessExited`. The platform's writer for that shape is `CloseSessionOnExitAsync` → `ProcessExit`. Even if that stamp landed, the classifier's `else` branch produced the CARD-0316 sentence. If the column is literally `Unknown`, something wrote `Stopped` first and the exit event skipped the already-terminal row (race C.1 in the investigation) — S2's backfill covers that too.

### The two snapshot builders are not in lockstep

`AgentTaskDispatcher.FailDeadSessionTasksAsync` (`AgentTaskDispatcher.cs:932-937`) projects `TerminationSource` into `AgentTaskLiveness.SessionSnapshot`; `AttentionService` (`AttentionService.cs:455-458`) does not. Today that is harmless because the attention row uses `Describe`, not `ClassifyFailure`, but the class doc on `AgentTaskLiveness` says the two consumers must read the same table. S1 closes that drift while it is in the file.

### Entities are anemic

No `server/Domain/Entities/*.cs` carries behaviour. The stamp helper lives in Application, next to `AgentTaskLiveness`.

---

## Slices

### S1 — `ClassifyFailure` names the recorded source

**Files:** `server/Application/Services/AgentTaskLiveness.cs`, `server/Application/Services/AttentionService.cs` (`:455-458`), `server/Domain/Enums/AgentTaskEnums.cs` (`StoppedBeforeFirstPrompt` xml-doc, `:216-221`).

**Snapshot.** Add `int? ExitCode = null` as a fifth optional parameter on `SessionSnapshot`. Both builders select it (`AgentTaskDispatcher.cs:932`, `AttentionService.cs:455`) and `AttentionService` starts passing `TerminationSource` as well. The `ProcessExit` clause can then say which exit it was: the CARD-0315 rows would have read "exited with code 0", which is the trust-dialog signature.

**Classifier.** Replace the two-way branch at `:96-109` with one clause per source. Keep `StoppedBeforeFirstPrompt` as the failure code for every non-operator source — the repeat-dispatch guard (`AgentTaskService.cs:1569`) and `server/Bundles/orchestrator.md:11` key on the code, not the prose. Exact clauses:

| `TerminationSource` | Clause after `StoppedBeforeFirstPrompt: Antiphon observed no prompt before the session stopped` | Code |
|---|---|---|
| `OperatorRequest` | unchanged: `stopped by an operator request before any prompt was recorded` (no prefix) | none |
| `SystemRequest` | `; Antiphon itself ended it (SystemRequest)` | `StoppedBeforeFirstPrompt` |
| `ProcessExit` | `; the agent process exited on its own (ProcessExit, exit code N)` — `N` from the snapshot, `unknown` when null | `StoppedBeforeFirstPrompt` |
| `Unknown` | unchanged: `, and the stop origin was not recorded` | `StoppedBeforeFirstPrompt` |

Put the per-source clause in a private `DescribeStop(SessionTerminationSource, int?)` and reuse it in the non-empty Stopped fall-through at `:119-121` ("stopped before the task settled, with no failure reason recorded" → append the same clause for `SystemRequest`/`ProcessExit`; `Unknown` keeps today's text). Code stays null there.

**Failure-code doc.** `AgentTaskFailureCode.StoppedBeforeFirstPrompt` currently reads "no persisted operator-stop source … it does not know who ended the session". Change to: no prompt was observed; the reason names the recorded source when one exists, and says "not recorded" only for `Unknown`.

**Tests.**

- `AgentTaskLivenessTests.ClassifyFailure_process_exit_and_legacy_unknown_are_not_operator_stops` keeps its two asserts (code = `StoppedBeforeFirstPrompt`, no "operator") for all three sources. Add `ClassifyFailure_names_SystemRequest_and_ProcessExit_and_reserves_not_recorded_for_Unknown`: `SystemRequest` reason contains `SystemRequest` and not `not recorded`; `ProcessExit` with `ExitCode: 0` contains `ProcessExit` and `exit code 0` and not `not recorded`; `Unknown` contains `not recorded`.
- `AgentTaskDeadSessionReconciliationTests`: extend `a_clean_process_exit_is_not_promoted_to_an_operator_stop` (`:180`) with `ShouldContain("ProcessExit")` / `ShouldNotContain("not recorded")`; extend `a_legacy_unknown_stop_is_not_promoted_to_an_operator_stop` (`:196`) with `ShouldContain("not recorded")`; add a `SystemRequest` sibling. `Scenario.AddTaskAsync` already takes `terminationSource`.
- `AttentionServiceTests`: no wording change expected; run it to prove the projection still compiles and the `DeadSession` rows are unchanged.

### S2a — One stamp helper, existing writers re-expressed through it

**New file:** `server/Application/Services/SessionTermination.cs` (static, next to `AgentTaskLiveness`).

```csharp
public static class SessionTermination
{
    /// First writer wins (CARD-0256). Returns true when it wrote.
    public static bool Record(AgentSession session, SessionTerminationSource source)
    {
        if (source == SessionTerminationSource.Unknown
            || session.TerminationSource != SessionTerminationSource.Unknown)
            return false;
        session.TerminationSource = source;
        return true;
    }

    /// A runner-side watchdog kill is the platform deciding, not the process leaving.
    public static SessionTerminationSource FromExitReason(AgentExitReason reason) =>
        reason is AgentExitReason.CpuSpinKilled or AgentExitReason.MemoryKilled
            ? SessionTerminationSource.SystemRequest
            : SessionTerminationSource.ProcessExit;
}
```

Re-express the three existing stamps through it, behaviour-preserving except where noted:

| Site | Today | After |
|---|---|---|
| `AgentSessionService.KillAsync` `:789-794` | inline `Unknown` + live-status guard | keep the live-status guard, call `Record(session, source)` inside it |
| `AgentSessionRuntime.CloseSessionOnExitAsync` `:172-173` | `ProcessExit` if `Unknown`, live rows only | `Record(session, FromExitReason(exitReason))` in the live branch **and** a new backfill arm: if the row is already `Stopped`/`Failed` and `Unknown`, `Record(...)` plus `session.ExitCode ??= exitCode`, set `changed = true`. This is the C.1 race fix: a closer that wrote the status first no longer leaves the column empty when the exit lands |
| `SessionReconciliationService` Exited arm `:194-195` | `ProcessExit` if `Unknown` | `Record(session, FromExitReason(runnerSession.ExitReason))` |

`FromExitReason` is the one deliberate behaviour change here: a `CpuSpinKilled` exit today lands `ProcessExit`; after S2a it lands `SystemRequest`, because the runner's CPU watchdog is Antiphon deciding. No existing test pins `ProcessExit` for `CpuSpinKilled` (checked: `AgentSessionRuntimeTests`, `SessionReconciliationServiceTests`). Do **not** widen the reconciler's pass-1 query beyond `LiveStatuses` to backfill terminal rows: that pass exists to close phantom live rows, and pass 3's arms (`ReconcileRunnerAliveSessionsAsync`, CARD-0056) only see `Running` runner sessions. The event-driven backfill in `CloseSessionOnExitAsync` plus the runner's liveness sweep re-emitting missed `SessionExited` is the backfill path.

**Tests (S2a).**

- `SessionTerminationSourcePersistenceTests`: the two existing pins stay green unchanged.
- `AgentSessionRuntimeTests`: existing `a_clean_process_exit_records_ProcessExit_when_no_prior_source` and `an_exit_event_does_not_overwrite_an_OperatorRequest_source` stay. Add `an_exit_event_backfills_ProcessExit_onto_an_already_closed_row_with_no_source` — `SeedRunningSessionAsync` hard-codes `Status = Running`; give it a `status` parameter, seed `Stopped` + `Unknown`, observe exit code 0, assert `ProcessExit` and `ExitCode == 0` and status still `Stopped`. Add `a_cpu_spin_watchdog_exit_records_SystemRequest`.
- `SessionReconciliationServiceTests.Runner_reported_exit_is_mirrored_to_the_db_session` stays (`ExitReason: Unknown` → `ProcessExit`). Add a `CpuSpinKilled` sibling asserting `SystemRequest`.

### S2b — Stamp every platform closer

All stamps are `SessionTermination.Record(session, SessionTerminationSource.SystemRequest)` placed **with the status write, before the kill call** where there is one (the CARD-0256 order). Nothing here changes `FailureReason` text, status, or kill behaviour.

| # | Site | Line | Terminal status written | Stamp goes |
|---|---|---|---|---|
| 1 | `AgentSessionService.StartAsync` first-delta timeout | `:218-221` | Failed | before `adapter.KillAsync` |
| 2 | `StartAsync` turn-complete timeout | `:244-247` | Failed | before `adapter.KillAsync` |
| 3 | `StartAsync` outer catch | `:274-278` | Failed | inside `if (session is not null)`. The kill already ran (`KillAndDisposeAsync` first, by design); the stamp is still correct because the launch context saves `TerminationSource` in the same `SaveChanges` and the exit-event scope only writes when it sees `Unknown` |
| 4 | `LaunchInteractiveAsync` catch | `:334-337` | Failed | with the status write. **This is the delegate launch path.** |
| 5 | `ResumeAsync` catch | `:966-969` | Failed | with the status write |
| 6 | `TryMarkMemoryKilledAsync` | `:2054-2057` | Failed | `Record(session, FromExitReason(adapter.ExitReason))` → `SystemRequest` |
| 7 | `EnsureHerdrLaunchAllowed` refusal | `:1192-1194` | Failed | with the status write (no process ever started; the platform refused) |
| 8 | `AgentControlService` herdr attach failure | `AgentControlService.cs:585-588` | Failed | with the status write |
| 9 | `RunAttemptStallDetector.ScanAsync` | `RunAttemptStallDetector.cs:61-64` | Failed | before `TryKillRuntimeSessionAsync` |
| 10 | `SessionReconciliationService` runner-unknown arm | `SessionReconciliationService.cs:169-174` | Failed | with the status write. `SystemRequest`, not `ProcessExit`: no process was observed; the reconciler closed the row |
| 11 | `OrchestratorService.MarkSuccessfulRuntimeStopped` | `OrchestratorService.cs:686-689` | Stopped | with the status write |
| 12 | `OrchestratorService.MarkMissingRuntimeCanceledAsync` | `:790-793` | Failed | with the status write |
| 13 | `AgentSessionLaunchQueue.CompleteClaimAsync` | `AgentSessionLaunchQueue.cs:310-313` | Stopped or Failed (caller's choice) | with the status write. On the `StopOnSuccess` path `KillAsync` has already stamped, so `Record` is a no-op there; the `Failed` callers (`:231`, `:250`) close a row whose attempt already ended |

Site 3's comment block must say why the stamp is after the kill here (teardown-first is CARD-0056's invariant; the DB write is what carries the source), so the next reader does not "fix" it back into the kill order.

**Do not touch:** the dead-session sweep (`FailDeadSessionTasksAsync` reads the row and must never infer a source), `FailNeverStartedAsync` / `FailWedgedAtLimitAsync` / pool retire / reply-service stop / `SessionHealthActions` / terminal-column kill (all already go through `KillAsync` → `SystemRequest`), the runner's kill endpoints (server-side bookkeeping is the exit observer's job), and every detection-only watchdog.

**Tests (S2b).** One assert per writer on the persisted row, in the file that already exercises that path:

| Writer | Test file | Change |
|---|---|---|
| 1 | `AgentSessionServiceIntegrationTests.AgentSessionService_first_delta_timeout_ignores_startup_output_before_prompt` | add `session.TerminationSource.ShouldBe(SystemRequest)` next to the existing `FailureReason` assert |
| 2 | `AgentSessionServiceIntegrationTests.AgentSessionService_turn_completion_timeout_marks_session_failed_and_retry_reuses_worktree` | same |
| 3 | `AgentSessionLaunchFailureTests.Card_launch_failure_outside_the_timeout_branches_kills_before_disposing` | same |
| 4 | `AgentSessionLaunchFailureTests.Interactive_launch_failure_kills_the_process_before_disposing_it` | same |
| 5 | `AgentSessionLaunchFailureTests.Resume_not_found_kills_the_first_process_before_the_fallback_relaunch` (or the resume-failure test nearest it) | same, on the row the failed resume leaves |
| 6 | `AgentSessionServiceIntegrationTests.RunAttempt_records_memory_killed_exit_reason` | same |
| 7, 8 | `AgentAttachHerdrTests` (`Refuses_when_runner_lacks_attach_capability` is the nearest to the `:583` catch) and the herdr-pairing refusal test in `AgentControlServiceIntegrationTests`; use whichever already reads the Failed row back, and add a row read-back if neither does | same |
| 9 | `RunAttemptStallDetectorTests.StallDetector_fires_after_configured_idle` | same, after the `Status == Failed` assert |
| 10 | `SessionReconciliationServiceTests.Session_unknown_to_runner_is_failed_and_its_agent_reset` | same |
| 11 | `OrchestratorServiceIntegrationTests.Reconcile_moves_succeeded_missing_runtime_session_to_review_without_retry` | same, next to `storedSession.Status.ShouldBe(Stopped)` |
| 12 | `OrchestratorServiceIntegrationTests.Reconcile_terminal_missing_runtime_clears_claim_without_retry` | same on the session row |
| 13 | the `AgentSessionLaunchQueue` failure-path test that seeds a session and drives a failed attempt (`CardWorkTransitionServiceTests` / `AgentStartRecoveryTests` are the candidates; pick the one that already reads the row back) | same |

Plus one negative pin that the helper never downgrades: `SessionTerminationSourcePersistenceTests` gains `Record_never_overwrites_a_prior_source` (pure, no DB: seed `OperatorRequest`, `Record(SystemRequest)` returns false and leaves `OperatorRequest`).

### S3 — Expose the column

**Files:** `server/Application/Dtos/BoardDtos.cs` (`AgentSessionSummaryDto`, append `SessionTerminationSource? TerminationSource = null` after `HerdrOrigin`), `server/Application/Services/AgentService.cs:239` and `BoardService.cs:331` (pass `TerminationSource: s.TerminationSource` by name), `client/src/api/boards.ts` (`terminationSource?: SessionTerminationSource | null` with a four-value string union; enums already serialise as strings, `Program.cs:249`), `docs/ops-http.md` (one sentence near `:130`: the session summary now carries `terminationSource`; `Unknown` on a row closed after this card ships is a bug to file, not a state).

No UI rendering in this card. `SessionContextUsage.AttachAsync` uses `with`, so the new parameter flows through untouched.

### S4 — Docs

`docs/session-runtime-invariants.md` has no CARD-0256 bullet today. Add one bullet covering both cards, in the existing style: every platform-initiated closer records a `TerminationSource` with its terminal status, first writer wins (`SessionTermination.Record`), `ProcessExit` is the exit observer's fallback and backfills a source-less terminal row, runner watchdog kills are `SystemRequest`, `Unknown` means legacy or a missed writer, the dead-session classifier names the source and says "not recorded" only for `Unknown`, and the reconciler's runner-unknown arm is `SystemRequest` because no process was observed. Pinned-by list: `AgentTaskLivenessTests`, `AgentTaskDeadSessionReconciliationTests`, `SessionTerminationSourcePersistenceTests`, `AgentSessionRuntimeTests`, `SessionReconciliationServiceTests`, plus the per-writer asserts above.

`docs/agent-card-lifecycle.md` and `docs/orchestration-loop.md` do not mention the failure code; no change. `server/Bundles/orchestrator.md:11` keys on the code only; no change.

---

## What this card does not do

- Add enum members (`DeliveryWatchdog`, `StallDetector`, …). `FailureReason` already names the watchdog on every `Failed` writer.
- An `IDelegateSessionStopper` source overload (no caller can honestly pass anything but `SystemRequest` until the cancel endpoint knows its actor).
- Widen reconciler pass 1 to terminal rows.
- Any inference in the dead-session sweep or the attention projection.
- Richer `StoppedBeforeFirstPrompt` context (last screen) — CARD-0312.
- Any auto-kill. Every stamp in S2b sits on a path that already kills or already has no process.

---

## Test matrix

| Layer | Run |
|---|---|
| Pure | `AgentTaskLivenessTests` (4 → 5), the new `Record_never_overwrites_a_prior_source` |
| Integration, shared Postgres | `AgentTaskDeadSessionReconciliationTests`, `SessionTerminationSourcePersistenceTests`, `AgentSessionRuntimeTests`, `SessionReconciliationServiceTests`, `RunAttemptStallDetectorTests`, `AgentSessionLaunchFailureTests`, `AgentSessionServiceIntegrationTests`, `OrchestratorServiceIntegrationTests`, `AgentAttachHerdrTests`, `AgentControlServiceIntegrationTests`, `AttentionServiceTests` |
| Client | `pwsh -File scripts/test-client.ps1` (type-only change; the existing boards API tests must still compile) |

Run targeted first (`dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/Antiphon.Tests.Application/<Class>/*"`), then the `Antiphon.Tests.Application` namespace once, chunked per `docs/testing-and-build.md`. Build to `--property:OutputPath=bin-c0316/` (forward slash) while the daemons hold `bin/`, and delete the `bin-c0316` directories afterwards.

**Live acceptance** (commit message): after deploy, launch a Grok delegate into a fresh worktree with the trust answer temporarily disabled or an unreachable cwd, let the dead-session sweep fail it, and confirm (a) `GET /api/agent-tasks/{id}` reason names `ProcessExit` or `SystemRequest`, never "not recorded", and (b) the session summary on the card or agent carries the same `terminationSource`. Then `POST /api/agents/{id}/stop` a fresh one and confirm `OperatorRequest` still wins over the exit event.

---

## Sequencing and risks

**Order: S1 → S2a → S2b → S3 → S4.** S1 alone changes the message for every already-correct `ProcessExit`/`SystemRequest` row (the majority). S2a alone would still print "not recorded" for the rows it fixes. S2b depends on the helper. S3 and S4 are independent of each other. One PR, one commit per slice, each commit message naming the pins it turned.

| Risk | Standing |
|---|---|
| Launch scope vs exit-event scope both write the column (investigation race C.4) | Both now write; whichever saves last wins on that one property. Exit scope only writes when it reads `Unknown`, so it cannot erase a request. Launch scope writing `SystemRequest` over a `ProcessExit` the exit scope raced in first is the truer value (the platform gave up on the launch). Acceptable and documented on site 3 |
| `FromExitReason` flips `CpuSpinKilled` from `ProcessExit` to `SystemRequest` | No pin asserts the old value. The runner's CPU watchdog is Antiphon; recording it as "the process left" was the less truthful of the two. Named in the S4 bullet |
| `CompleteClaimAsync` stamping `SystemRequest` on a row `KillAsync` already stamped | `Record` is a no-op on a non-`Unknown` row by construction; pinned by the negative test |
| Site 3 stamps after the kill, not before | Kill-first is CARD-0056's invariant on that catch and outranks the stamp order; the exit scope's `Unknown` guard makes the order safe. Comment on the site says so |
| A future writer forgets the stamp | S3 exposes the column, S4 says a post-ship `Unknown` is a bug. Not enforceable by test without an interceptor; not worth one for this card |
| `SessionSnapshot` gains a fifth field | Both builders updated in S1; the record-struct default keeps every existing test constructor compiling |

---

## Execution notes

Write the S2b stamps by grepping for `Status = SessionStatus.Failed` and `Status = SessionStatus.Stopped` across `server/Application/Services` and checking each hit against the table above; the table is the investigation's catalog and should be the complete set, but the grep is the cheap proof. Any hit not in the table and not going through `KillAsync` is a writer this plan missed — stamp it and add it to the commit message rather than leaving it.
