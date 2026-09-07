# CARD-0403: restore API-error recovery in the reply integration harness

Plan at `d5901ee47907820baf285d3e7d08e9ba71b0064d`. Ready for Code; verification design is folded into this narrow, diagnosed fix. This commit is a plan only. The brief reports 7 failures out of 111 tests; this planning task did not rerun them.

## Ground truth

| Brief assumption | Current code and implication |
|---|---|
| `TestScopeFactory` omits the dependency required by recovery. | Confirmed: `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs:4034` registers `ApiErrorRecoveryService` but not `ModelAvailability`. `ApiErrorRecoveryService.BuildNewRowAsync` resolves the concrete `ModelAvailability` at `server/Application/Services/ApiErrorRecoveryService.cs:343` for Wall stubs. Its database, clock, logging and supervision-options dependencies are already registered. |
| `HandleApiErrorTurnAsync` is not entered. | More precisely, `AgentTaskReplyService.cs:128` enters it; its call to `EnsureAdoptedAsync` at `:1012` fails while building the recovery row, before defer/fail settlement. That explains unchanged task state and missing incidents/events. Constructor resolution alone would not exercise this dynamic dependency. |
| `OnTurnEndAsync` silently swallows the exception. | Its inner `OnTurnEndLockedAsync` catches non-cancellation exceptions at `AgentTaskReplyService.cs:202` and calls `LogWarning(ex, "Failed to settle a delegated task for session {SessionId}", sessionId)`. Production has a diagnostic, including the exception. `CreateService` at test line 3500 explicitly supplies `NullLogger<AgentTaskReplyService>`, hiding it in these tests. |
| Production may also be missing the registration. | `server/Program.cs:476-477` registers scoped `ModelAvailability` and maps `IModelAvailability` to it. `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs` already registers the concrete scoped service too. |
| The old retryable-wall fixture might no longer defer. | The default stub text at test line 3321 contains `reset at 6:10pm (Europe/London)`, which `UsageLimitWallParser.ResetRegex` accepts. The session's task supplies a canonical model alias through `ResolveFallbackAliasAsync`. A parsed session limit schedules at reset + 2 minutes; it is not the historical 30-minute WallPrompt ladder. |
| `WallParked` might be obsolete. | `ApplyWallAsync` at recovery lines 373-425 still counts same-session Wall rows except `Superseded`, including prior `Replaced` rows. With the default `WallDeathCap = 3`, the test's two prior deaths plus the new stub resolve as `WallParked`, with no next attempt. `ApiErrorRecoveryServiceTests.Wall_parks_after_three_deaths` independently pins this. |

Owners: [project conventions](../../project-context.md), [testing/build](../../testing-and-build.md), [session invariants](../../session-runtime-invariants.md) (Gotcha 76), and [stage workflow](../../orchestration-loop.md).

## Decisions

- **D-1: add `services.AddScoped<ModelAvailability>();` beside the recovery registration in the local `TestScopeFactory`.** Match production lifetime and the existing bridge harness. The call site requires the concrete writer, so registering only `IModelAvailability` would not fix it. Reject a fake availability service, removing recovery registration, or making the required lookup optional: each would bypass the behavior these tests must exercise. A shared registration refactor is unnecessary for this one missing line.
- **D-2: retain the production exception boundary and its Warning diagnostic.** It already reports this exception class. Rethrowing every `InvalidOperationException` would also catch unrelated runtime failures, and would skip the queue work that follows task settlement in `AgentSessionRuntime.FlushQueueOnIdleAsync` before its outer catch logs again. No production catch, severity, incident schema, or task-failure policy change is justified by this test-harness defect. Pin the existing diagnostic with a recording logger so a genuinely silent catch cannot replace it unnoticed. Cancellation must continue to propagate through the existing filter; do not broaden it.
- **D-3: retain all seven existing behavioral expectations, including `WallParked`.** Clarify the retry test's comment to say a parsed session-limit Wall defers; with a resolved model alias, a model cap or unparseable reset instead ends as `WallModelPaused` with a timed model hold (default six hours), unless the death cap parks it. Without an alias, the uncapped outcome is `WallUnparsed`. Do not loosen Failed/Working assertions, accept either outcome, or reintroduce the old 30-minute schedule.
- **D-4: one Code slice, with verification here.** No user decision or separate TestDesign dispatch is needed. The implementation is confined to the reply integration test file and any resulting evidence update to this plan. Production source edits occur only temporarily for the specified positive control and must be restored.

## Implementation slice

### S-1: complete the test graph and pin its error diagnostic

File: `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs`.

1. Add the scoped concrete registration from D-1. Keep `AddDelegationWorktreeGraph` as the git-graph owner.
2. In `a_retryable_api_error_defers_the_task_and_never_stores_the_error_text`, additionally assert that the session has one Wall recovery with `ResolvedAt == null`, `ResolvedReason == null`, and `NextAttemptAt != null`. Keep all existing result, event, parent-message and ownership assertions. Update only the outdated generic-Wall comment.
3. In `a_parked_recovery_fails_the_task_naming_exhaustion`, retain the two prior `Replaced` Wall rows and the `WallParked` failure assertion. Additionally select the newest recovery for this session and assert `ResolvedReason == ApiErrorRecoveryReasons.WallParked`, `ResolvedAt != null`, and `NextAttemptAt == null`. Do not change the cap or the stub fixture to make the test green.
4. Let `CreateService` accept an optional `ILogger<AgentTaskReplyService>` at the end of its arguments, defaulting to its current logger. Add a small local recorder retaining level, rendered message and original exception, following the local `ListLogger<T>` pattern in `AgentTaskCatchUpSettlementTests`; no logging package is needed.
5. Add `an_unexpected_settlement_exception_is_logged_and_releases_the_settle_lock`. Seed a dispatched task and marked retryable stub, use the recorder, and set the existing `DelayAfterOpenTaskLoadedAsync` hook to return a faulted task containing a sentinel `InvalidOperationException`. Await `OnTurnEndAsync`; require one Warning with that same exception object, the settlement-failure message and the session id. Verify the task remains Dispatched with null result/completion and `IsSettleInFlight(sessionId)` is false. Clear the hook and call the same service again: the task must become Working and have one defer event. This checks containment, visibility and retryability together; it does not pretend to exercise DI failure itself, which PC-1 covers.
6. The restored wall path now writes real `ModelAvailabilityHolds`. Clean up holds owned by each wall-test session in `finally`, including the new test's successful retry. Scope cleanup to `SourceSessionId == sessionId` and `Source == AutoDetected`; never delete by alias or clear another test's manual holds. Keep every recovery/event/incident assertion scoped to the test's task or session. Do not use a global sweep in this class.

The seven original cases that must pass are:

- `a_retryable_api_error_defers_the_task_and_never_stores_the_error_text`
- `a_dirty_shared_checkout_is_named_in_the_api_error_incident`
- `a_parked_recovery_fails_the_task_naming_exhaustion`
- `the_api_error_incident_is_critical_when_the_agent_is_channel_bound`
- `the_api_error_incident_is_warning_when_the_agent_is_not_channel_bound`
- `a_second_on_turn_end_on_the_same_stub_adds_nothing`
- `real_narration_beside_the_stub_still_does_not_settle_on_it`

## Verification design

### Proves it works now

- **V-1:** Reply integration behavior | integration | run all `AgentTaskReplyIntegrationTests` using the command below | zero failures, including the seven original cases and the new exception-boundary test. The brief's 111 is historical; report actual discovered/passed/failed counts.
- **V-2:** Current wall contract | integration | run all `ApiErrorRecoveryServiceTests` separately | zero failures; specifically `Session_limit_stub_schedules_one_resume_at_reset_plus_padding`, `Wall_parks_after_three_deaths`, and `Fable_5_stub_writes_a_fallback_hold_and_does_not_enqueue` retain their assertions unchanged. This also covers the recent sibling-AssistantText recovery fix without duplicating those fixtures here.
- **V-3:** Scope | diff inspection | `git diff --check` and inspect the final diff | only the planned test changes and evidence; no production catch/parser/schedule changes survive positive controls.

Run from the checkout using the established isolated-output form, sequentially:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c403/ -- --treenode-filter "/*/*/AgentTaskReplyIntegrationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c403/ -- --treenode-filter "/*/*/ApiErrorRecoveryServiceTests/*"
```

### Guards the regression

- **R-1:** Omitting `ModelAvailability` again is caught by the retryable-wall test's recovery-row and Working assertions; the real dynamic lookup must execute successfully. A constructor-only registration test would miss this failure point.
- **R-2:** Replacing the Warning catch with a silent catch, losing its exception/session context, rethrowing the contained failure, or leaking the settle lock is caught by the new exception-boundary test and its successful second call.
- **R-3:** Treating every Wall as a retry, using the old interval, or dropping the death cap is caught by V-2 and the reply test's explicit `WallParked`/no-next-attempt assertions. Existing narration, result-null and idempotency assertions continue to protect against a stub being reported as completed work.

### Positive controls

Run one mutation at a time. Rebuild on every source change, record the assertion failure, restore the exact edit, and rerun the same filter green. Restore mutations before committing; do not use a broad checkout/reset in the shared workspace.

- **PC-1:** Temporarily remove the new `services.AddScoped<ModelAvailability>();` line. Run V-1's command with filter `/*/*/AgentTaskReplyIntegrationTests/a_retryable_api_error_defers_the_task_and_never_stores_the_error_text`. Expect red because no recovery is adopted and the task stays Dispatched. Restore the registration and require green. This is the original defect as the control, not an assertion-only mutation.
- **PC-2:** Temporarily remove only `_logger.LogWarning(ex, "Failed to settle a delegated task for session {SessionId}", sessionId);` from `OnTurnEndLockedAsync`. Run V-1's command with filter `/*/*/AgentTaskReplyIntegrationTests/an_unexpected_settlement_exception_is_logged_and_releases_the_settle_lock`. Expect red for the missing diagnostic. Restore the line and require green.

Run the two final class filters after both controls are restored; report V/R/PC evidence in the Code report.

### Out of scope

- No full assembly, namespace-wide filter, client, E2E, headed/provider canary, or live-runner probe: production behavior and process delivery are unchanged. Tests use the established test Postgres, never the dev database or runner on 17204.
- No production logging redesign, new incident, retry-policy rewrite, DI census, harness consolidation, or unrelated service-lifetime cleanup.
- No restart or deploy is needed for this test-only fix.

### Cost

Suites forced: two class filters in `Antiphon.Tests`, plus four single-test control runs (red/green for each mutation). Estimated verification floor 5-10 minutes including isolated builds and test-container startup; not measured in this Plan task. Do not overlap with `Antiphon.Agents.Pty.Tests`. Clean up only verified in-checkout `bin-c403` directories using the testing/build guide's native PowerShell procedure after verification.

## Handoff

Implement S-1, run and report V-1 through V-3, R-1 through R-3, and PC-1/PC-2. Commit and push the actual test fix. Retain the existing logged exception boundary and `WallParked` semantics. If the scoped tests uncover another defect after the registration is restored, report its exact assertion and cause rather than weakening the contract or widening this card silently.
