# CARD-0403: restore API-error recovery in the reply integration harness

Plan at `d5901ee47907820baf285d3e7d08e9ba71b0064d`, followed by TestDesign and the mandatory design-review amendments below. The original duplicate verification section was removed (D4); this document has one executable verification contract. See Code evidence below for measured results.

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
6. Apply design-review amendment D1: use unkeyed `[NotInParallel]` because `ModelAlias.Normalize` accepts only known canonical aliases. Snapshot the active kind/alias hold before settlement, capture the persisted upsert row ID immediately after the wall-triggering call (also on failure), and restore the full prior state or delete the new row **by ID** in `finally`. Include the parked case and the diagnostic test's retry. Verify the alias has exactly its prior row IDs and every prior property is restored, with zero residual fixture rows. Never delete by session/source/alias or globally sweep the table.

The seven original cases that must pass are:

- `a_retryable_api_error_defers_the_task_and_never_stores_the_error_text`
- `a_dirty_shared_checkout_is_named_in_the_api_error_incident`
- `a_parked_recovery_fails_the_task_naming_exhaustion`
- `the_api_error_incident_is_critical_when_the_agent_is_channel_bound`
- `the_api_error_incident_is_warning_when_the_agent_is_not_channel_bound`
- `a_second_on_turn_end_on_the_same_stub_adds_nothing`
- `real_narration_beside_the_stub_still_does_not_settle_on_it`

## Verification design

TestDesign addendum for task `4afe77e4`, against landed plan `590e996d` on 2026-09-07. This section supplies the executable verification requested by the separate TestDesign dispatch. It superseded the removed earlier verification scope, PC-1, cost and verification handoff; D-1 through D-3 and the production fix design remain unchanged. No runtime tests were executed in the TestDesign stage. Expected outcomes in this section are requirements; measured Code results are recorded separately below.

### Proves it works now

- **V-1: all seven original regressions exercise recovery | integration | `AgentTaskReplyIntegrationTests` | every named case below passes, with a newly adopted recovery for its marked stub and its original behavioral assertions intact.** The new diagnostic test must also pass. Report actual discovered/executed/passed/failed/skipped counts; neither the historical 111 nor a zero exit code alone proves that these eight methods ran.
- **V-2: current Wall policy | integration | `ApiErrorRecoveryServiceTests` | zero failures.** Keep `Session_limit_stub_schedules_one_resume_at_reset_plus_padding`, `Wall_parks_after_three_deaths`, and `Fable_5_stub_writes_a_fallback_hold_and_does_not_enqueue` unchanged. These remain the clock-controlled owners of reset + 2 minutes, the third-death cap, and the model-cap fallback hold. Do not introduce a wall-clock equality assertion against the reply fixture's `6:10pm` text.
- **V-3: retained production boundary | diff inspection | final diff and restored-source comparison | no production changes survive controls.** Only the reply test file, this plan's evidence and the testing/build owner's measured filter caveat should change. Preserve the `ex is not OperationCanceledException` filter, the Warning call, and its original exception argument.
- **V-4: observable settlement failure and successful retry | integration | `an_unexpected_settlement_exception_is_logged_and_releases_the_settle_lock` | the actual production catch emits exactly the diagnostic below, releases its lock, and the same service subsequently adopts/defer-settles the same stub.** An `AgentIncident` with `AlertSeverity.Warning` is a different signal and does not satisfy this test.

#### Evidence required from each original case

Extend the seven existing methods in place. Use fresh database reads after the service call and preserve all existing assertions. A small local assertion helper is sufficient; no production instrumentation, fake recovery, constructor-only DI test, or pre-adoption call is needed.

After seeding the turn, read its API-error **TurnEnd** sequence, not the sibling AssistantText sequence. Before the first call, assert no recovery exists for that exact session/sequence and the task is Dispatched. The parked case must still have exactly its two older `Replaced` Wall rows. For example, the shared recovery oracle is:

```csharp
var stubSequence = await verify.TranscriptEntries
    .Where(t => t.AgentSessionId == sessionId
        && t.Kind == TranscriptKinds.TurnEnd && t.IsApiError == true)
    .MaxAsync(t => t.Sequence);
var recovery = (await verify.ApiErrorRecoveries.AsNoTracking()
    .Where(r => r.AgentSessionId == sessionId && r.StubSequence == stubSequence)
    .ToListAsync()).ShouldHaveSingleItem();
recovery.Classification.ShouldBe(ApiErrorClassification.Wall);
recovery.ApiErrorClass.ShouldBe("rate_limit");
recovery.ApiErrorStatus.ShouldBe(429);
```

This query belongs **after** `OnTurnEndAsync`; obtain `stubSequence` and check absence with a separate context before the call. Do not seed a current recovery or invoke `EnsureAdoptedAsync`, `SweepAsync`, or a dispatcher tick in any of these tests. Thus the change from absent to present is evidence from this invocation, and the parked test cannot accidentally assert against an older seeded row.

Use the S-1 recording logger in all seven methods, passed as `CreateService(logger: logger)`. After the behavioral assertions, require no entry whose rendered message starts with `Failed to settle a delegated task for session `, at **any** level. Do not assert that all Warning entries are absent: successful defer and terminal API-error paths already log at Warning. The recorder must retain all levels, rendered text and the original exception object; it may also capture structured state but does not need a new logging dependency.

| Existing method | Required successful-handler evidence in addition to the common recovery oracle |
|---|---|
| `a_retryable_api_error_defers_the_task_and_never_stores_the_error_text` | One recovery for the session, unresolved with a non-null `NextAttemptAt`; Working, null result/failure/completion; exactly one task-scoped `ApiErrorDeferred` event containing `Wall`, `resume scheduled`, and the stub sequence; no parent message; agent remains Running. |
| `real_narration_beside_the_stub_still_does_not_settle_on_it` | One unresolved scheduled recovery; Working with null result/failure/completion; exactly one task-scoped defer event. Retain the narration fixture and the assertion that neither narration nor the stub is a result. |
| `a_second_on_turn_end_on_the_same_stub_adds_nothing` | After the **first** call, assert one unresolved scheduled recovery, Working and exactly one defer event; save recovery/event IDs and `NextAttemptAt`. After the second call, read with a new context and require the same IDs/time and still exactly one recovery/event, with null result/failure/completion. Equality of two empty snapshots is not idempotency evidence. |
| `the_api_error_incident_is_warning_when_the_agent_is_not_channel_bound` | One unresolved scheduled recovery; Working with null result/failure/completion and one defer event; exactly one session-scoped `ApiErrorTurnDied` incident with Warning severity, task short ID and `NOT stored`. |
| `the_api_error_incident_is_critical_when_the_agent_is_channel_bound` | The same scheduled-recovery/Working/defer evidence, plus exactly one session-scoped `ApiErrorTurnDied` incident with Critical severity. Retain the channel binding. |
| `a_parked_recovery_fails_the_task_naming_exhaustion` | Three session-scoped recoveries total: both seeded `Replaced` rows retained and the new row `WallParked`, resolved, with null `NextAttemptAt`. Failed with `WallParked` in `FailureReason`, non-null completion and null result; exactly one task-scoped Failed event and no defer event. Keep the default cap and fixture. |
| `a_dirty_shared_checkout_is_named_in_the_api_error_incident` | One unresolved scheduled recovery; Working with null result/failure/completion and one defer event; exactly one session-scoped `ApiErrorTurnDied` incident still naming `uncommitted-work.cs`. Retain the real scratch git checkout. |

An unresolved scheduled recovery means `ResolvedAt == null`, `ResolvedReason == null`, `NextAttemptAt != null`. Scope defer/failed events to `AgentTaskId == task.Id`; include `seq {stubSequence}` in defer-event assertions. The six non-parked cases must have one recovery for their session, not just one matching row among duplicates.

**Design-review amendments D1/D2 supersede the original cleanup and parallelization instructions.**
The seven Wall cases and diagnostic retry use S-1's ID-based snapshot/restore helper under
unkeyed `[NotInParallel]`. An `await using` fixture supplies the `finally` cleanup even when an
assertion fails. The production upsert return value is discarded, so capture that persisted
row's ID with a fresh kind/alias read immediately around each settlement call. Restore all
mapped fields of a pre-existing row, including a Manual hold's provenance; otherwise delete
only the captured new ID. Assert the exact prior row set and zero residual fixture rows after
cleanup. Known-alias normalization rules preclude a GUID-suffixed fixture alias.

D2 requires **one combined invocation** of the reply, ComplexityDispatcher,
RoutingPinCandidateDispatch and ComplexityWallReroute classes (or the whole Application
namespace), using normal runner parallelism. Use the [testing/build owner's combined-filter form](../../testing-and-build.md#combined-class-filters-card-0403) for the pinned TUnit runner. Record all four class counts and the fresh
post-cleanup alias count. The named routing suites currently use cloned databases; unkeyed
serialization additionally protects this shared-database reply fixture against other writers.

#### Recording-logger diagnostic fixture

Keep the optional `ILogger<AgentTaskReplyService>` last in `CreateService`, defaulting to the current `NullLogger`. Implement a local recorder using the `ListLogger<T>` pattern with `IsEnabled` returning true and a synchronized collection of `(Level, Message, Exception)` snapshots. `Log<TState>` must record `formatter(state, exception)` and the supplied exception itself. Add the `Microsoft.Extensions.Logging` import. Do not make the logger throw and do not call `LogWarning` from the test.

For V-4, seed a fresh Dispatched task and the default marked Wall stub. Save the stub sequence and prove that it has no recovery. Create the service with the recorder and a sentinel `InvalidOperationException("CARD-0403 settlement sentinel")`. Set `DelayAfterOpenTaskLoadedAsync` to check the observed session ID and `IsSettleInFlight(sessionId) == true`, then return `Task.FromException(sentinel)`.

Await `OnTurnEndAsync(sessionId, CancellationToken.None)` directly, without a test-side catch. From the recorder, select entries with the settlement-failure prefix above and require exactly one. Assert:

```csharp
entry.Level.ShouldBe(LogLevel.Warning);
entry.Message.ShouldBe($"Failed to settle a delegated task for session {sessionId}");
ReferenceEquals(entry.Exception, sentinel).ShouldBeTrue();
```

Require the task to remain Dispatched with null result/failure/completion, no current-stub recovery, no task-scoped defer/failed event, and `IsSettleInFlight(sessionId) == false`. Clear the hook, then call the **same service** again. Require the normal unresolved scheduled Wall recovery, Working/null result/failure/completion and exactly one defer event for this stub. Require the lock to be released again and the settlement-failure entry count to remain one; the successful defer may add its own ordinary Warning. Also clear the hook in `finally` and perform the scoped hold cleanup.

The injected fault happens before `HandleApiErrorTurnAsync`; it tests the existing outer diagnostic boundary. PC-1 separately exercises the actual missing-registration failure, and PC-3 proves that the successful recovery evidence depends on the handler call.

### Guards the regression

- **R-1: concrete registration omitted, changed to interface-only, or recovery made optional | caught by all seven V-1 cases.** No current-stub recovery or a terminal fallback cannot satisfy the recovery + task/event/incident assertions. PC-1 reinstates the original missing dependency and requires all seven red.
- **R-2: handler skipped, or failures hidden behind another successful-return/catch path | caught by all seven V-1 cases.** Each starts without the current recovery and requires durable changes from the handler; PC-3 demonstrates that doing nothing cannot pass. The idempotency test explicitly proves a successful first call.
- **R-3: Warning removed, severity/context/exception lost, containment removed, or lock leaked | caught by V-4.** It checks the production logger's record, exception identity, unchanged state after the injected failure, lock release and successful retry. PC-2 makes the diagnostic assertion red while retaining the catch.
- **R-4: parked exhaustion treated as another scheduled retry, or error/narration text accepted as a completed result | caught by V-1's exact terminal/deferred/null-result assertions and V-2's existing policy tests.** PC-1 and PC-3 must fail the parked and narration cases too; no `Working OR Failed` expectations, no alternate accepted exhaustion reason, and no alteration of the current reset schedule are allowed.

### Positive controls

Apply one mutation at a time **after implementing the tests**, rebuild for every run, and restore only the precise edit. Run all controls in the foreground. Record the original production file bytes/hash before the first mutation; compare them after restoration, as well as inspecting `git diff`. Never use `git reset`, a broad checkout or a shared-workspace stash to restore these one-line controls.

The executable command wrapper below emits a distinct TRX for each phase and captures the native exit code. A red phase is accepted only when its fresh TRX contains the specified test assertion failures; a build failure, fixture/container error, skip, zero discovered tests or unrelated failure is not a positive control. Locate each TRX at the path printed by the runner and retain its assertion messages for the Code report. A green phase requires exit zero and every expected method Passed.

```powershell
function Invoke-C403Verification {
    param([string]$Phase, [string]$Filter, [switch]$ExpectRed)
    $ErrorActionPreference = 'Stop'
    $c403Root = Join-Path (Get-Location) '.antiphon/c403-results'
    $c403Results = Join-Path $c403Root $Phase
    if (Test-Path -LiteralPath $c403Results) { throw "Fresh phase directory required: $c403Results" }
    New-Item -ItemType Directory -Path $c403Results -Force | Out-Null
    $c403Start = Get-Date
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c403/ -- --treenode-filter $Filter --report-trx --report-trx-filename "c403-$Phase.trx" --results-directory $c403Results *> (Join-Path $c403Results 'run.log')
    $c403Exit = $LASTEXITCODE
    $c403Trx = Join-Path $c403Results "c403-$Phase.trx"
    Write-Host "CARD-0403 $Phase native exit: $c403Exit; elapsed: $((Get-Date) - $c403Start); TRX: $c403Trx"
    Get-Content -LiteralPath (Join-Path $c403Results 'run.log') -Tail 16
    if (-not (Test-Path -LiteralPath $c403Trx)) { throw 'No fresh TRX: build/runner failures are not positive controls' }
    [xml]$c403Xml = Get-Content -Raw -LiteralPath $c403Trx
    $c403Counters = $c403Xml.TestRun.ResultSummary.Counters
    if ([int]$c403Counters.total -le 0 -or [int]$c403Counters.executed -le 0) { throw 'No discovered/executed tests' }
    $c403Expected = @(
        'a_retryable_api_error_defers_the_task_and_never_stores_the_error_text',
        'real_narration_beside_the_stub_still_does_not_settle_on_it',
        'a_second_on_turn_end_on_the_same_stub_adds_nothing',
        'the_api_error_incident_is_warning_when_the_agent_is_not_channel_bound',
        'the_api_error_incident_is_critical_when_the_agent_is_channel_bound',
        'a_parked_recovery_fails_the_task_naming_exhaustion',
        'a_dirty_shared_checkout_is_named_in_the_api_error_incident',
        'an_unexpected_settlement_exception_is_logged_and_releases_the_settle_lock'
    )
    if ($Phase -like 'pc3-*') {
        $c403Expected += @('a_needs_human_error_is_critical_even_without_a_channel', 'a_codex_401_fails_as_authentication_required_with_no_resume')
    }
    if ($Phase -like 'pc2-*') { $c403Expected = @($c403Expected[-1]) }
    if ($Phase -eq 'final-v2') {
        $c403Expected = @('Session_limit_stub_schedules_one_resume_at_reset_plus_padding', 'Wall_parks_after_three_deaths', 'Fable_5_stub_writes_a_fallback_hold_and_does_not_enqueue')
    }
    if ($Phase -like 'd2-combined*') {
        $c403Classes = @('AgentTaskReplyIntegrationTests', 'ComplexityDispatcherTests', 'RoutingPinCandidateDispatchTests', 'ComplexityWallRerouteTests')
        foreach ($c403Class in $c403Classes) {
            $c403Definitions = @($c403Xml.TestRun.TestDefinitions.UnitTest | Where-Object { $_.TestMethod.className -eq "Antiphon.Tests.Application.$c403Class" })
            if ($c403Definitions.Count -eq 0) { throw "Combined run did not discover $c403Class" }
            Write-Host "D2 $c403Class discovered=$($c403Definitions.Count)"
        }
    }
    $c403Outcome = if ($ExpectRed) { 'Failed' } else { 'Passed' }
    foreach ($c403Method in $c403Expected) {
        $c403Definition = @($c403Xml.TestRun.TestDefinitions.UnitTest | Where-Object { $_.TestMethod.name -eq $c403Method })
        if ($c403Definition.Count -ne 1) { throw "Expected exactly one discovered definition for $c403Method" }
        $c403Result = @($c403Xml.TestRun.Results.UnitTestResult | Where-Object { $_.testId -eq $c403Definition[0].id })
        if ($c403Result.Count -ne 1 -or $c403Result[0].outcome -ne $c403Outcome) { throw "$c403Method did not report $c403Outcome" }
        if ($Phase -like 'd2-combined*' -and $c403Result[0].Output.StdOut -notmatch 'CARD-0403 hold cleanup ClaudeCode/sonnet: 0 rows; baseline 0; residual fixture rows 0') {
            throw "$c403Method lacks zero-residual alias evidence"
        }
        if ($ExpectRed) {
            $c403Error = $c403Result[0].Output.ErrorInfo.Message
            $c403ErrorPattern = switch ($c403Method) {
                'a_needs_human_error_is_critical_even_without_a_channel' { 'InvalidOperationException: Sequence contains no elements' }
                'a_codex_401_fails_as_authentication_required_with_no_resume' { '(?s)ShouldAssertException: failed.Status.*AgentTaskStatus.Failed.*AgentTaskStatus.Dispatched' }
                default { '(?s)ShouldAssertException:.*single item.*0' }
            }
            if ($c403Error -notmatch $c403ErrorPattern) {
                throw "Unexpected assertion for ${c403Method}: $c403Error"
            }
            Write-Host "$c403Method : $c403Error"
        }
    }
    if ($ExpectRed) {
        if ($c403Exit -eq 0 -or [int]$c403Counters.failed -ne $c403Expected.Count) { throw 'Unexpected red exit/failure count' }
    } elseif ($c403Exit -ne 0 -or [int]$c403Counters.failed -ne 0 -or [int]$c403Counters.passed -ne [int]$c403Counters.total) {
        throw 'Green requires every discovered test passed and exit zero'
    }
    Write-Host "VERIFIED $Phase total=$($c403Counters.total) executed=$($c403Counters.executed) passed=$($c403Counters.passed) failed=$($c403Counters.failed) notExecuted=$($c403Counters.notExecuted)"
}
$c403Reply = '/*/*/AgentTaskReplyIntegrationTests/*'
$c403Diagnostic = '/*/*/AgentTaskReplyIntegrationTests/an_unexpected_settlement_exception_is_logged_and_releases_the_settle_lock'
$c403Recovery = '/*/*/ApiErrorRecoveryServiceTests/*'
$c403ProductionPath = 'server/Application/Services/AgentTaskReplyService.cs'
$c403SourceHash = (Get-FileHash -LiteralPath $c403ProductionPath).Hash
```

Execute in the following order so PC-3's restored class run also supplies the final V-1 evidence:

1. **PC-2: remove the diagnostic, keep the catch.** In `OnTurnEndLockedAsync`, temporarily comment out exactly `_logger.LogWarning(ex, "Failed to settle a delegated task for session {SessionId}", sessionId);`. Keep the exception filter and catch body otherwise intact.

   ```powershell
   Invoke-C403Verification 'pc2-red' $c403Diagnostic -ExpectRed
   # Restore the exact LogWarning line before proceeding.
   Invoke-C403Verification 'pc2-green' $c403Diagnostic
   ```

   Red: one executed test fails on the missing settlement diagnostic (zero matching entries), not on a later retry assertion. Green: one executed test passes, including Warning level, session ID, sentinel identity and successful retry. This is direct evidence that the production log call fires and is observable without NullLogger.

2. **PC-1: restore the original missing-dependency defect.** Temporarily comment out only the new `services.AddScoped<ModelAvailability>();` inside the local `TestScopeFactory`. Leave `ApiErrorRecoveryService`, the recorder and every new assertion registered/intact.

   ```powershell
   Invoke-C403Verification 'pc1-red' $c403Reply -ExpectRed
   # Restore the scoped concrete registration before proceeding.
   Invoke-C403Verification 'pc1-green' $c403Reply
   ```

   Red: **each of the seven original methods must fail**, from a missing current-stub recovery, unchanged Dispatched state, missing event or missing incident. The two seeded parked rows must not count as adoption. V-4 also fails when its second call cannot recover; eight failing methods are therefore expected. Inspect/report the first failed assertion for every original method and the diagnostic test. Green: the entire class passes, including all eight. If even one original method stays green under the missing registration, strengthen its missing execution oracle rather than claiming the control passed.

3. **PC-3: bypass the handler without throwing.** With the registration and Warning call restored, temporarily comment out only `await HandleApiErrorTurnAsync(scope.ServiceProvider, db, task, sessionId, stub, ct);` in the `turn.ApiErrorStub` branch of `OnTurnEndLockedAsync`. Keep the following `return;` in place, so there is no ordinary-report fallback and no new exception to catch.

   ```powershell
   Invoke-C403Verification 'pc3-red' $c403Reply -ExpectRed
   # Restore the exact HandleApiErrorTurnAsync call before proceeding.
   Invoke-C403Verification 'pc3-green-final-v1' $c403Reply
   Invoke-C403Verification 'final-v2' $c403Recovery
   $c403Combined = '/*/Antiphon.Tests.Application/(AgentTaskReplyIntegrationTests*)|(ComplexityDispatcherTests*)|(RoutingPinCandidateDispatchTests*)|(ComplexityWallRerouteTests*)/*'
   Invoke-C403Verification 'd2-combined-v2' $c403Combined
   if ((Get-FileHash -LiteralPath $c403ProductionPath).Hash -ne $c403SourceHash) {
       throw 'Production source does not match the pre-control snapshot'
   }
   git diff --check
   git diff -- server/Application/Services/AgentTaskReplyService.cs
   git diff -- tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs docs/superpowers/plans/2026-09-07-card-0403-api-error-recovery-test-scope-plan.md
   ```

   Red: the same seven original methods must all fail on their positive recovery/settlement evidence; the measured full-class bypass also fails the two existing NeedsHuman/Codex-401 handler tests (ten failures total), so the wrapper checks those exact names and failure messages too; V-4 fails on its successful-retry half. There is no settlement exception in those seven cases, so absence of an exception/log cannot make them pass. Green: all eight pass with the call restored, and the full reply and recovery classes have zero failures. The production diff must be empty and the recorded production source hash unchanged. This establishes that the seven greens depend on executing recovery, not just on swallowing a failure.

Code's evidence table must include V-1 through V-4, R-1 through R-4 and all three PCs. For PC-1 and PC-3, include seven named red/green rows, each with its actual failing assertion and restored Passed result, plus the diagnostic test's expected retry failure. For PC-2, name the missing-log assertion and the restored observed Warning/exception/session evidence. Record TRX paths and native exit codes; do not paste passing output or mark an unexecuted control as passed.

### Out of scope

- This stage appends design only; implementation, runtime red/green execution and measured evidence belong to Code. No restart, deploy, browser, provider session, live runner or full assembly run is needed. D2 adds the combined four-class invocation above.
- Do not change the production exception policy, cancellation filter, parser, death cap, model-alias fallback or retry schedule. The sentinel test uses the existing hook and exercises no live delivery or global recovery sweep.
- No harness consolidation, DI census, unrelated tests or new logging packages. The extra assertions reuse the seven diagnosed fixtures and one planned diagnostic fixture. Any unrelated failure needs exact scoped reproduction and a report, not a relaxed assertion or a widened suite.

### Cost

- Suites forced: `Antiphon.Tests` only, the reply and recovery classes plus D2's three routing classes. Four reply-class invocations (PC-1 red/green, PC-3 red/green with the last also V-1), one recovery-class invocation (V-2), and two diagnostic-method invocations (PC-2 red/green): **eight foreground test invocations, each rebuilding**, including D2's additional combined four-class run, never concurrent with `Antiphon.Agents.Pty.Tests`.
- Estimated verification floor **10-20 minutes**, unmeasured, including isolated builds and test-Postgres startup. Four reply-class runs are justified by checking all seven failure names under both mutations; single retry-test controls cannot supply that evidence. Report measured time in Code.
- After verification, enumerate only `bin-c403` directories inside the resolved checkout, verify each absolute target remains under that checkout and is not a reparse point, then remove those exact paths with native PowerShell `Remove-Item -LiteralPath ... -Recurse -Force`. Preserve TRX evidence first. Do not delete another task's output directories or mutate `.perfmon/` (untracked at this stage's start).

Verification handoff: implement S-1 with this addendum's seven-case recovery/logger assertions, execute PC-2 then PC-1 then PC-3, the final recovery class and D2 combined run, report the per-method mutation evidence, and commit/push the test-only fix with production source restored.

## Code evidence — task 195a9de0, 2026-09-07

Implemented S-1 and D1–D4. The concrete scoped registration exercises real recovery. The
seven existing methods now prove absent-to-present adoption for the API-error TurnEnd,
correct recovery/task/event/incident state, and absence of the settlement-failure diagnostic.
The new sentinel test proves the existing Warning boundary, original exception identity,
released lock and successful retry. Production Warning/cancellation/WallParked behavior is unchanged.

D1 uses unkeyed `NotInParallel`; unknown fixture aliases are rejected by normalization.
`WallRecoveryFixture` snapshots the complete detached active hold before settlement, reads
the persisted upsert ID immediately afterwards in `finally` (production discards the returned
entity), and restores the prior row or deletes the new row by ID in `DisposeAsync`. Its
fresh verification checks the exact prior ID set and all mapped prior properties. All eight
ordinary fixtures recorded zero `ClaudeCode/sonnet` rows after cleanup in every measured run.
No source/session-based deletion or alias/global sweep was added.

### Counts and artifacts

All executed phases used `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c403/`
with the filters and TRX flags above, sequentially, against test Postgres. All discovered tests
executed; zero skipped/not-executed in every nonempty run. Measured build-plus-run time for the
eight required invocations: **10m38s**. An additional rejected zero-test filter attempt took 15s;
it is excluded from verification success. Discovery-only probes performed no tests.

TRX root: `C:\src\Antiphon\.antiphon\c403-results`. Each table phase identifies the exact relative
TRX `<phase>/c403-<phase>.trx`; its sibling `run.log` preserves runner output. Artifacts live
outside the isolated build directories and are retained after their cleanup.

| Phase / TRX directory | Native exit | Discovered / executed | Passed | Failed | Build + run |
|---|---:|---:|---:|---:|---:|
| `pc2-red` | 2 | 1 / 1 | 0 | 1 | 113.0s |
| `pc2-green` | 0 | 1 / 1 | 1 | 0 | 48.1s |
| `pc1-red` | 2 | 121 / 121 | 113 | 8 | 84.4s |
| `pc1-green` | 0 | 121 / 121 | 121 | 0 | 80.1s |
| `pc3-red` | 2 | 121 / 121 | 111 | 10 | 94.2s |
| `pc3-green-final-v1` | 0 | 121 / 121 | 121 | 0 | 75.2s |
| `final-v2` | 0 | 20 / 20 | 20 | 0 | 58.4s |
| `d2-combined-v2` | 0 | 152 / 152 | 152 | 0 | 84.9s |

PC-2 red failed exactly on `logger.SettlementFailures.ShouldHaveSingleItem()`:
“should have single item but had 0”. Restored green observed Warning, the exact session message
and the sentinel exception object; the same service then adopted and deferred the stub.
The first validator expected a namespace-qualified exception name; TRX emits
`ShouldAssertException`. The validator was corrected and the retained fresh TRX revalidated.

### Per-method positive controls

For every row below, both PC-1 and PC-3 red failed at the fresh current-stub query in
`WallRecoveryFixture.AssertRecoveryAsync`: `ShouldHaveSingleItem()` expected one recovery
but found zero (`r.AgentSessionId == sessionId && r.StubSequence == StubSequence`). The diagnostic
method reached this assertion on its retry, after successfully verifying containment and lock
release. The parked fixture's two older Replaced rows did not satisfy the new recovery oracle.

| Method | PC-1 red → restored | PC-3 red → restored | D2 combined |
|---|---|---|---|
| `a_retryable_api_error_defers_the_task_and_never_stores_the_error_text` | Failed: 0 recoveries → Passed | Failed: 0 recoveries → Passed | Passed |
| `real_narration_beside_the_stub_still_does_not_settle_on_it` | Failed: 0 recoveries → Passed | Failed: 0 recoveries → Passed | Passed |
| `a_second_on_turn_end_on_the_same_stub_adds_nothing` | Failed: 0 recoveries → Passed | Failed: 0 recoveries → Passed | Passed |
| `the_api_error_incident_is_warning_when_the_agent_is_not_channel_bound` | Failed: 0 recoveries → Passed | Failed: 0 recoveries → Passed | Passed |
| `the_api_error_incident_is_critical_when_the_agent_is_channel_bound` | Failed: 0 recoveries → Passed | Failed: 0 recoveries → Passed | Passed |
| `a_parked_recovery_fails_the_task_naming_exhaustion` | Failed: 0 recoveries → Passed | Failed: 0 recoveries → Passed | Passed |
| `a_dirty_shared_checkout_is_named_in_the_api_error_incident` | Failed: 0 recoveries → Passed | Failed: 0 recoveries → Passed | Passed |
| `an_unexpected_settlement_exception_is_logged_and_releases_the_settle_lock` | Failed: 0 recoveries → Passed | Failed: 0 recoveries → Passed | Passed |

PC-3 additionally failed two existing handler-dependent authentication tests, both restored
Passed in final V-1 (and D2):

- `a_needs_human_error_is_critical_even_without_a_channel`: incident `SingleAsync` threw
  `InvalidOperationException: Sequence contains no elements` because the bypass created no incident.
- `a_codex_401_fails_as_authentication_required_with_no_resume`: `failed.Status` expected
  `AgentTaskStatus.Failed`, observed `AgentTaskStatus.Dispatched`.

These are direct consequences of bypassing the whole API-error handler, not unrelated failures.
The red validator initially rejected the ten-versus-eight total, then was amended to check
these two exact method names and messages as well. The fresh PC-3 TRX was revalidated; no
assertion was weakened and no source mutation was repeated or retained to accommodate them.

### V/R coverage and D2 concurrency

| Requirement | Measured evidence |
|---|---|
| V-1 / R-1 / R-2 | Final reply class 121/121; all seven current-stub assertions red under both missing-registration and silent-handler-bypass controls, then green. |
| V-2 / R-4 | Recovery class 20/20, including unchanged `Session_limit_stub_schedules_one_resume_at_reset_plus_padding`, `Wall_parks_after_three_deaths`, `Fable_5_stub_writes_a_fallback_hold_and_does_not_enqueue`; explicit reply WallParked and narration/null-result contracts pass. |
| V-3 | Final production diff empty; original/restored SHA-256 `3FFB7829ADCDD04B08332E70858DC8026170EBF7370AE5AB6F95C04F3DC79820`; `git diff --check` clean. Only tests and documentation changed. |
| V-4 / R-3 | Sentinel Warning/exception/session/containment/lock/retry assertions passed; removing just the production Warning call made the diagnostic assertion fail. |
| D1 / D2 | One combined invocation: reply 121, ComplexityDispatcher 9, RoutingPinCandidateDispatch 11, ComplexityWallReroute 11; 152/152 passed. Eight fixture cleanup outputs each report `ClaudeCode/sonnet: 0 rows; baseline 0; residual fixture rows 0`. |

TRX timestamps confirm real cross-class parallel execution:
`RoutingPinCandidateDispatchTests.Reroute_nulls_RoutingPinId` ran 05:37:32.891–05:37:35.706 UTC,
while `ComplexityDispatcherTests.Reroute_on_Blocked_queues_with_Complexity_null` ran
05:37:32.891–05:37:35.784 UTC. No reply-test interval overlapped a routing-test interval,
confirming the chosen unkeyed serialization. The three routing classes currently use isolated
cloned databases; the reply harness uses the shared assembly database.

The first D2 filter used a non-wildcard OR segment and ran zero tests (native exit 8;
`d2-combined/c403-d2-combined.trx`), correctly rejected by the nonzero-count guard. The corrected
per-operand-parentheses/wildcard filter is recorded above and selected exactly the four intended
classes. The measured pinned-runner discovery caveat is now in `docs/testing-and-build.md`.

Cleanup residue: all 14 `bin-c403` directories were resolved under `C:\src\Antiphon` and checked for reparse points before deletion. Automatic approval review rejected the native PowerShell deletion as "blocked by policy", including the separate cleanup-only command. The ignored outputs remain; the exact list is `C:\src\Antiphon\.antiphon\c403-output-directories.txt`. All nine TRX files and logs are retained independently. `.perfmon/` was left untouched.

Ready for Review: S-1 and required controls are complete. No restart or deployment is needed.
