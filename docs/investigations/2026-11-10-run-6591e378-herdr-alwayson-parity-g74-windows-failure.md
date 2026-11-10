# Run 6591e378 group-74: HerdrAlwaysOnChannelParityTests Windows failure

Read-only extraction from the desktop qualification logs. No build or test was run. The running qualification was not touched.

- Run: `6591e378` (invocation `6591e378c04d47bc898cf0927352b01b`), rc/2026-11-15 at `ac6d00022e940ee3b90584c7d313567b4491c498`.
- Checkout: `C:\Antiphon\releases\checkout-4dd2a426` (`git rev-parse HEAD` = that SHA).
- Host: `lndco@DESKTOP-KTLKPIF`, Windows 10.0.19045, `ProcessorCount: 8`, `TotalAvailableMemoryBytes(GB): 34`. Test host PID 224732.
- Logs (name filter `antiphon-all-group-74*` only):
  - `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378\antiphon-all-group-74.trx` (902280 bytes)
  - `...\antiphon-all-group-74-tests.log` (6773 bytes; the console log)
  - `...\antiphon-all-group-74.timing.json` (863 bytes)
  - `...\antiphon-all-group-74-execution\log_261110160454234.diag` (the only file in the execution directory)

Chunk filter from the diag command line:

`/*/Antiphon.Tests.Application/(AgentTaskLandingStateTests*)|(C0824DesktopLegacyReleaseSwitchTests*)|(CapacityObsoleteQueueTests*)|(ComplexityChainRoleTests*)|(ComplexityDispatcherTests*)|(ComplexityWallRerouteTests*)|(DurableRunnerSpillReceiptTests*)|(HerdrAlwaysOnChannelParityTests*)|(RoutingPinCreateTests*)|(StaleWorkingTranscriptWatchdogTests*)|(StandingRestartEscalationTests*)|(WorkingTranscriptRecoveryTests*)/*`

## Verdict

**Assertion on returned session state.** One row of `HerdrAlwaysOnChannelParityTests` failed because the second fresh PtyHost Claude session was already `SessionStatus.Failed` with `FailureReason` `Automated Claude input withheld: observation-unknown`. `RequireCompletedAsync` reads that row once and asserts `Running`. The recorded duration is 4.669s. The test's own deadline is 90s and its launch-queue wait is 50s. TRX counters are `timeout=0`, `error=0`. Stderr is empty. No sharing-violation text and no poll-expiry text appear in the test output or in the diag lines for this result.

The same method's other three argument rows passed, including `(ClaudeCode, False)` which started 10ms after this result ended. Zero other TRX results overlap this test's interval. The chunk's peak concurrency was 28, at 16:05:08Z, on `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(common, True)`, about two minutes earlier.

## Failed test

| Field | Value |
|---|---|
| Test | `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, True)` |
| Class | `Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests` |
| Method | `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(AgentKind, Boolean)` |
| Category | `Integration`. Class attributes: ungrouped `[NotInParallel]`, `[ParallelLimiter<ProcessSpawnLimit>]` |
| TUnit node | `Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.1.1.Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(Antiphon.Server.Domain.Enums.AgentKind, System.Boolean).1.1.0` |
| Outcome | Failed |
| Duration | `00:00:04.6692775` (start `2026-11-10T16:07:22.8159711+00:00`, end `2026-11-10T16:07:27.4852486+00:00`) |
| Source | `tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.StandingRecovery.cs` |

TRX `ErrorInfo/Message` (same text in `antiphon-all-group-74-tests.log` and the diag `TrxExceptionProperty`):

```
ShouldAssertException: row.Status
    should be
SessionStatus.Running
    but was
SessionStatus.Failed

Additional Info:
    Automated Claude input withheld: observation-unknown
```

`RequireCompletedAsync` is the assertion. It loads `AgentSessions` by id and calls `row.Status.ShouldBe(SessionStatus.Running, row.FailureReason)`, so the additional info is the stored `FailureReason`.

```
private static async Task RequireCompletedAsync(Guid id)
{
    await using var db = CreateContext(); var row = (await db.AgentSessions.FindAsync(id))!;
    row.Status.ShouldBe(SessionStatus.Running, row.FailureReason); row.InteractiveLaunchCompletedAt.ShouldNotBeNull();
}
```

Stack top, as recorded (TUnit internals omitted by the runner). User frames:

```
at HerdrAlwaysOnChannelParityTests.RequireCompletedAsync(Guid id) in ...\HerdrAlwaysOnChannelParityTests.StandingRecovery.cs:line 287
at HerdrAlwaysOnChannelParityTests.StandingHistoryRecoveryAsync(...) in ...\HerdrAlwaysOnChannelParityTests.StandingRecovery.cs:line 82
at HerdrAlwaysOnChannelParityTests.StandingHistoryRecoveryAsync(...) in ...\HerdrAlwaysOnChannelParityTests.StandingRecovery.cs:line 178
at HerdrAlwaysOnChannelParityTests.StandingHistoryRecoveryAsync(...) in ...\HerdrAlwaysOnChannelParityTests.StandingRecovery.cs:line 185
at HerdrAlwaysOnChannelParityTests.StandingHistoryRecoveryAsync(...) in ...\HerdrAlwaysOnChannelParityTests.StandingRecovery.cs:line 185
```

Line 287 is the `ShouldBe`. Line 82 is the call on the second fresh session, after the first session was stopped:

```
await StopNativeAsync(h, agent.Id);
var second = await StartNativeAsync(h, agent.Id, new(Fresh: true));
var b = Guid.Parse(second.PersistentSessionId!); b.ShouldNotBe(a);
await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(50), deadline.Token);
if (inputRefused) await RequireClaudeHerdrInputRefusedAsync(h, b);
else await RequireCompletedAsync(b);   // line 82
```

`inputRefused` is `kind == AgentKind.ClaudeCode && backend == SessionBackend.Herdr`. This row is `SessionBackend.PtyHost`, so it takes `RequireCompletedAsync`. Lines 178 and 185 are the `finally` (`Task.WhenAll(proofs)`) and the method end, which the async stack shows as the exception leaves the method. The reported message is the line 82 status assertion, not a swallowed proof exception (those are caught in the `finally`).

The helper has not reached the later resume launch (`StartNativeAsync` with `ResumeSessionId` at line 99) or the channel-queue assertions. Cleanup stdout names exactly two session ids, which matches a first session plus this second fresh session.

## What ran at the same time

TRX interval overlap with this result, excluding itself: **0**. The class is ungrouped `[NotInParallel]` because the supervisor sweep is global on the shared test database.

Immediate neighbours, serial with this result:

| Start (Z) | Duration | Outcome | Test |
|---|---|---|---|
| 16:07:22.380 | 0.435s | Passed | `Standing_native_wire_missing_target_never_creates(ClaudeCode, Herdr)` ends `16:07:22.8155290Z` |
| 16:07:22.816 | 4.669s | Failed | `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, True)` |
| 16:07:27.496 | 2.024s | Passed | `Standing_claude_ptyhost_native_wire_missing_target_never_creates` |

Chunk-wide max concurrency from a start/end sweep of all 409 results: **28**, at `2026-11-10T16:05:08.2983344+00:00`, test `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(common, True)`. That window is not this failure.

## Stdout, stderr, diag

Stderr length 0.

Stdout (TRX `StdOut`, also the tests.log "Standard output" block and the diag `StandardOutputProperty`). No secret material. Session ids and pids are the fixture's captured processes:

```
ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.1.1.Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(Antiphon.Server.Domain.Enums.AgentKind, System.Boolean).1.1.0
C1020 cleanup operation=captured session=19c3bd3a-3e68-430d-929f-2043ca172f00 pid=79480 generation=639299236438083485 at=2026-11-10T16:07:24.1463341Z
C1020 cleanup operation=captured session=19c3bd3a-3e68-430d-929f-2043ca172f00 pid=58380 generation=639299236440800551 at=2026-11-10T16:07:24.3164461Z
C1020 cleanup operation=captured session=19c3bd3a-3e68-430d-929f-2043ca172f00 pid=44596 generation=639299236440977774 at=2026-11-10T16:07:24.3217746Z
C1020 cleanup operation=captured session=6af20a68-f711-4ec6-8f98-60460cb8e640 pid=421808 generation=639299236460329967 at=2026-11-10T16:07:26.6696254Z
C1020 cleanup operation=captured session=6af20a68-f711-4ec6-8f98-60460cb8e640 pid=391492 generation=639299236465524920 at=2026-11-10T16:07:26.9504495Z
C1020 cleanup operation=captured session=6af20a68-f711-4ec6-8f98-60460cb8e640 pid=149752 generation=639299236465891151 at=2026-11-10T16:07:26.9581346Z
C1020 cleanup operation=dispose-core session= pid= generation= at=2026-11-10T16:07:27.3791555Z
C1020 cleanup operation=stop-sessions session= pid= generation= at=2026-11-10T16:07:27.3904846Z
C1020 cleanup operation=observed-exit session=19c3bd3a-3e68-430d-929f-2043ca172f00 pid=79480 generation=639299236438083485 at=2026-11-10T16:07:27.3906935Z
C1020 cleanup operation=observed-exit session=19c3bd3a-3e68-430d-929f-2043ca172f00 pid=58380 generation=639299236440800551 at=2026-11-10T16:07:27.3906998Z
C1020 cleanup operation=observed-exit session=19c3bd3a-3e68-430d-929f-2043ca172f00 pid=44596 generation=639299236440977774 at=2026-11-10T16:07:27.3907023Z
C1020 cleanup operation=observed-exit session=6af20a68-f711-4ec6-8f98-60460cb8e640 pid=421808 generation=639299236460329967 at=2026-11-10T16:07:27.3907095Z
C1020 cleanup operation=observed-exit session=6af20a68-f711-4ec6-8f98-60460cb8e640 pid=391492 generation=639299236465524920 at=2026-11-10T16:07:27.3907113Z
C1020 cleanup operation=observed-exit session=6af20a68-f711-4ec6-8f98-60460cb8e640 pid=149752 generation=639299236465891151 at=2026-11-10T16:07:27.3907122Z
```

Diag around the result (`log_261110160454234.diag`):

- `16:07:22.8157497Z` in-progress for this node, file location `StandingRecovery.cs` line 29 (the PtyHost test method, which forwards to `StandingHistoryRecoveryAsync`).
- `16:07:27.4954210Z` error state with the message above.

The diag repeats the same assertion and the same C1020 lines. It does not add a dialog snapshot, a generation, or an `InputHistoryKnown` value, so which `ProviderDialogObservationService.Refusal` clause produced `observation-unknown` is not in this chunk's evidence.

`ProviderDialogInputRefusedException` formats `Automated Claude input withheld: ` plus the reason (`server/Application/Services/ProviderDialogObservationService.cs`). `Refusal` returns `ConditionalInputOutcomes.ObservationUnknown` (`"observation-unknown"`) when the observation is null, the session id differs, the runtime epoch is empty, input history is unknown, the accepted generation differs, or the dialog state is neither `Clear` nor `Blocked`.

## Passing siblings

Class total in this TRX: 26 results, 25 passed, 1 failed. All are `Integration`. Listed in start order.

Same method, the bound that isolates the failing arguments:

| Arguments | Duration | Outcome | Start (Z) |
|---|---|---|---|
| `(Grok, True)` | 7.221s | Passed | 16:07:06.779 |
| `(Grok, False)` | 8.166s | Passed | 16:07:14.000 |
| `(ClaudeCode, True)` | 4.669s | Failed | 16:07:22.816 |
| `(ClaudeCode, False)` | 6.732s | Passed | 16:07:29.727 |

The shared helper's Herdr rows, which use a different expectation for Claude (`RequireClaudeHerdrInputRefusedAsync` asserts this same `FailureReason` and `SessionStatus.Failed`):

| Arguments | Duration | Outcome |
|---|---|---|
| `Standing_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, Herdr, False)` | 2.318s | Passed |
| `Standing_history_recovery_preserves_native_identity_and_queued_reply(Grok, Herdr, True)` | 3.212s | Passed |
| `Standing_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, Herdr, True)` | 1.264s | Passed |
| `Standing_history_recovery_preserves_native_identity_and_queued_reply(Grok, Herdr, False)` | 2.652s | Passed |

Other passed methods of the same class:

- `Herdr_launch_definition_starts_adopts_and_exits(Codex)` 0.771s
- `Herdr_launch_definition_starts_adopts_and_exits(Grok)` 0.529s
- `Herdr_launch_definition_starts_adopts_and_exits(ClaudeCode)` 0.416s
- `Named_Grok_AlwaysOn_agent_lands_on_its_labelled_tab_across_repeated_resume` 1.604s
- `Generic_herdr_codex_banner_is_not_startup_ready` 0.000s
- `AlwaysOn_channel_bound_survives_child_death_and_replies(Herdr, Grok)` 2.594s
- `AlwaysOn_channel_bound_survives_child_death_and_replies(PtyHost, ClaudeCode)` 1.516s
- `AlwaysOn_channel_bound_survives_child_death_and_replies(Herdr, ClaudeCode)` 0.471s
- `Named_AlwaysOn_agent_lands_on_its_labelled_tab_across_repeated_resume` 0.462s
- `Standing_native_wire_missing_target_never_creates(Grok, PtyHost)` 0.214s
- `Standing_native_wire_missing_target_never_creates(ClaudeCode, Herdr)` 0.435s
- `Standing_native_wire_missing_target_never_creates(Grok, Herdr)` 0.206s
- `Standing_claude_ptyhost_native_wire_missing_target_never_creates` 2.024s
- `Named_AlwaysOn_herdr_agent_holds_after_three_failures_keeps_its_timeout_shell_and_resumes_only_on_explicit_retry` 2.991s
- `Unsafe_grok_rules_on_the_named_seat_refuse_before_any_pane_and_leave_the_streak_untouched` 0.097s
- `Named_placement_refusal_is_non_qualifying_not_a_detect_failure` 0.320s
- `Standing_grok_unavailable_native_store_never_creates(PtyHost)` 0.156s
- `Standing_grok_unavailable_native_store_never_creates(Herdr)` 0.241s

`AlwaysOn_channel_bound_survives_child_death_and_replies(PtyHost, ClaudeCode)` passed in this same chunk (1.516s). That is a different method. It does not cover this history-recovery row.

## Chunk summary

- TUnit v1.44.0.0, Microsoft Testing Platform v2.2.2, .NET 9.0.22, win-x64. Module `C:\Antiphon\releases\checkout-4dd2a426\tests\Antiphon.Tests\bin\Debug\net9.0\Antiphon.Tests.dll`.
- TRX counters: total 409, executed 409, passed 408, failed 1, skipped 0, timeout 0, error 0. `Times` creation/start `2026-11-10T16:04:54.4420603Z`, finish `2026-11-10T16:09:34.9482364Z`.
- tests.log: `Test run summary: Failed!` total 409, failed 1, succeeded 408, skipped 0, duration 4m 40s 579ms. `RunInfo outcome="Error"` text is `Exit code indicates failure: '2'`.
- timing.json: `why=timing-read-refused`, `testCount=0`, `buildSlot=not-used`, `runWallSeconds=281.74`, `buildSeconds=655.862`, `startedAt=2026-11-10T16:04:53.9648953Z`, `completedAt=2026-11-10T16:09:36.2701479Z`. Per-test p50/p95 are null. Durations above are from the TRX.

## Cause for Debug

The failing row is PtyHost, ClaudeCode, `alwaysOn: true`. The agent is created with `AlwaysOn: true`, then the test sets `AlwaysOn` to the argument. The first fresh session got far enough for the helper to stop it and start a second fresh session (`b`). `RequireCompletedAsync(b)` then saw `SessionStatus.Failed` and `FailureReason` `Automated Claude input withheld: observation-unknown`.

That string is the guarded-input refusal. On the Herdr Claude rows the helper expects it (`RequireClaudeHerdrInputRefusedAsync`). On this PtyHost row it expects `Running` and a non-null `InteractiveLaunchCompletedAt`. The three sibling argument rows of the same PtyHost method passed in this chunk, so the recorded miss is this argument combination's second fresh session, not the whole method and not the Herdr refusal path.

The chunk evidence does not include the observation fields that select `observation-unknown`. Debug has to distinguish a null snapshot, an id or generation mismatch, unknown input history, an empty runtime epoch, and a dialog state other than Clear or Blocked. It also has to explain why `alwaysOn: true` takes that path on the second fresh PtyHost Claude session when `alwaysOn: false` completed.

## Facts for the Debug why-missed note

- This extract is the Windows read. The five-item why-missed note is the Debug deliverable (`docs/orchestration-loop.md`, CARD-1781).
- The class is `[Category("Integration")]` and ungrouped `[NotInParallel]`, so a Unit-lane run does not execute it.
- `docs/investigations/2026-11-09-why-missed-pty-host-fixture.md` is a different cause: `Card1277PtyHostFixtureTests.Claude_two_write_submit_is_a_normalizer_turn` (assistant record missing after a user-only JSONL poll). That note records that CARD-1277 CP-3 ran `HerdrAlwaysOnChannelParityTests/Standing_ptyhost_history_recovery_*` serially on Linux. It does not record this `observation-unknown` status failure.
- Herdr Claude already treats `Automated Claude input withheld: observation-unknown` as the expected outcome. A selector that only reruns the Herdr arguments would stay green while this PtyHost `alwaysOn: true` row fails.
