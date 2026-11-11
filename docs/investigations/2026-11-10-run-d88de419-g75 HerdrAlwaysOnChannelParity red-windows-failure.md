# Run d88de419 group 75: HerdrAlwaysOnChannelParity red on Windows

Read-only extract. Nothing under the qualification tree was created or changed. The running qualification was not stopped, locked, or rerun.

| Field | Value |
|---|---|
| Run | `d88de419` (`d88de419025b4d368fb75108e4de0c36` on the invocation line) |
| Line | `rc/2026-11-16` = `6f8741d872d63507827014f972ccf71196722d4a` |
| Candidate | `C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6` |
| Chunk | `antiphon-all-group-75` |
| Log dir | `C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6\logs\2026-11-11-1142-d88de419` |
| TRX | `antiphon-all-group-75.trx` (976,988 bytes) |
| Console | `antiphon-all-group-75-tests.log` |
| Diagnostic | `antiphon-all-group-75-execution\log_261111120404296.diag` |
| Timing | `antiphon-all-group-75.timing.json` |
| Host | Windows 10.0.19045, win-x64, .NET 9.0.22, TUnit 1.44.0.0 |
| Checkout under test | `C:\Antiphon\releases\checkout` |

`b6bbfa26be15fad1fb6a6d23d0ba75fd2b1bd2dc` is an ancestor of this candidate. That commit is the Herdr always-on startup writer that waits for a classifiable Claude frame.

## Chunk

TRX counters: total 453, executed 453, passed 452, failed 1, error 0, timeout 0, skipped 0. Times: start `2026-11-11T12:04:04.506574Z`, finish `2026-11-11T12:10:27.1441326Z`. Console summary: `Failed!`, duration `6m 22s 744ms`. Timing file: `lastPhase=complete`, `queueWaitSeconds=0.001`, `buildSeconds=710.733`, `buildSlot=not-used`, `runWallSeconds=383.688`, `testCount=453`, `p50Seconds=0.553`, `p95Seconds=7.997`, `maxSeconds=21.341`. The slowest rows are `C1205_Capacity_overflow_remains_visible` and `C1079_*` / `C510_*`. None of them overlap the failure window below.

Files matched by `antiphon-all-group-75*`: the TRX, `antiphon-all-group-75-tests.log`, `antiphon-all-group-75.timing.json`, and the execution directory containing the diag log. No other group-75 artifact is in that directory.

## Failed test

`HerdrAlwaysOnChannelParityTests.Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, False)`

| Field | Value |
|---|---|
| Outcome | Failed |
| Duration | `00:00:02.2792465` (console: `2s 279ms`) |
| Start | `2026-11-11T12:07:06.0302665+00:00` |
| End | `2026-11-11T12:07:08.3095130+00:00` |
| Phase id | `Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.1.1.Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(Antiphon.Server.Domain.Enums.AgentKind, System.Boolean).3.1.0` |
| Category | Integration |
| Source | `tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.StandingRecovery.cs` |

Assertion, from the TRX `ErrorInfo.Message` and the console:

```
ShouldAssertException: row.Status
    should be
SessionStatus.Running
    but was
SessionStatus.Failed

Additional Info:
    Automated Claude input withheld: stale-observation
```

Expected `SessionStatus.Running`. Actual `SessionStatus.Failed`. The additional info is `row.FailureReason`, passed as the Shouldly message at `RequireCompletedAsync` line 287:

`row.Status.ShouldBe(SessionStatus.Running, row.FailureReason)`

Product frames, top of the stack. TUnit frames below these are omitted in the TRX:

```
at HerdrAlwaysOnChannelParityTests.RequireCompletedAsync(Guid id)
    StandingRecovery.cs:line 287
at HerdrAlwaysOnChannelParityTests.StandingHistoryRecoveryAsync(...)
    StandingRecovery.cs:line 67
at HerdrAlwaysOnChannelParityTests.StandingHistoryRecoveryAsync(...)
    StandingRecovery.cs:line 178
at HerdrAlwaysOnChannelParityTests.StandingHistoryRecoveryAsync(...)
    StandingRecovery.cs:line 185
```

Line 67 is the first session, after the fresh launch that carries the history marker:

`await RequireCompletedAsync(a);`

PtyHost Claude is not the Herdr input-refused arm, so this call is the Running check. Lines 178 and 185 are later awaits in the same async method. The first session threw, so the second launch, the resume, and the queued-reply assertions did not run.

Stdout is the phase id plus C1020 cleanup for one session, `2c06c5ee-03db-4bb9-9673-177e08310efe`, three pids:

```
C1020 cleanup operation=captured session=2c06c5ee-03db-4bb9-9673-177e08310efe pid=369832 generation=639299956270432237 at=2026-11-11T12:07:07.5420441Z
C1020 cleanup operation=captured session=2c06c5ee-03db-4bb9-9673-177e08310efe pid=79856 generation=639299956274466866 at=2026-11-11T12:07:07.8129570Z
C1020 cleanup operation=captured session=2c06c5ee-03db-4bb9-9673-177e08310efe pid=240368 generation=639299956274696885 at=2026-11-11T12:07:07.8225346Z
C1020 cleanup operation=dispose-core ... at=2026-11-11T12:07:08.2241376Z
C1020 cleanup operation=stop-sessions ... at=2026-11-11T12:07:08.2296282Z
C1020 cleanup operation=observed-exit ... pid=369832 at=2026-11-11T12:07:08.2297170Z
C1020 cleanup operation=observed-exit ... pid=79856 at=2026-11-11T12:07:08.2297213Z
C1020 cleanup operation=observed-exit ... pid=240368 at=2026-11-11T12:07:08.2297233Z
```

The TRX `Output` for this result has `StdOut` and no `StdErr` element. The console log has no separate error stream. The diag log repeats the same assertion and the same stdout. It has no `Claude startup:` line and no screen dump.

## What ran at the same time

The class is `[NotInParallel]` and `[ParallelLimiter<ProcessSpawnLimit>]`. Every `UnitTestResult` whose interval overlaps `12:07:06.0302665Z`–`12:07:08.3095130Z` was counted. Overlap count is 0. No other test in this chunk ran during the failure.

The serial neighbour before it ended as this row started: `Standing_native_wire_missing_target_never_creates(Grok, PtyHost)`, passed, `00:00:00.1630506`, end `2026-11-11T12:07:06.030`. The serial neighbour after it: `Standing_claude_ptyhost_native_wire_missing_target_never_creates`, passed, `00:00:02.3670772`, start `2026-11-11T12:07:08.3179869Z`.

## Passing siblings

25 other results in `HerdrAlwaysOnChannelParityTests` passed. Names:

- `Generic_herdr_codex_banner_is_not_startup_ready` (`00:00:00.0043340`)
- `Unsafe_grok_rules_on_the_named_seat_refuse_before_any_pane_and_leave_the_streak_untouched` (`00:00:00.1450264`)
- `Standing_native_wire_missing_target_never_creates(Grok, PtyHost)` (`00:00:00.1630506`)
- `Standing_grok_unavailable_native_store_never_creates(PtyHost)` (`00:00:00.2025671`)
- `Standing_native_wire_missing_target_never_creates(Grok, Herdr)` (`00:00:00.2111396`)
- `Standing_grok_unavailable_native_store_never_creates(Herdr)` (`00:00:00.2218040`)
- `Named_placement_refusal_is_non_qualifying_not_a_detect_failure` (`00:00:00.3705574`)
- `Standing_native_wire_missing_target_never_creates(ClaudeCode, Herdr)` (`00:00:00.4345677`)
- `Named_AlwaysOn_agent_lands_on_its_labelled_tab_across_repeated_resume` (`00:00:00.5999568`)
- `AlwaysOn_channel_bound_survives_child_death_and_replies(Herdr, ClaudeCode)` (`00:00:00.6217383`)
- `Herdr_launch_definition_starts_adopts_and_exits(ClaudeCode)` (`00:00:00.6364605`)
- `Herdr_launch_definition_starts_adopts_and_exits(Grok)` (`00:00:00.8232919`)
- `Standing_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, Herdr, False)` (`00:00:01.3360360`)
- `Herdr_launch_definition_starts_adopts_and_exits(Codex)` (`00:00:01.7472295`)
- `Named_Grok_AlwaysOn_agent_lands_on_its_labelled_tab_across_repeated_resume` (`00:00:02.0006329`)
- `Standing_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, Herdr, True)` (`00:00:02.1109504`)
- `AlwaysOn_channel_bound_survives_child_death_and_replies(PtyHost, ClaudeCode)` (`00:00:02.1874381`)
- `Standing_claude_ptyhost_native_wire_missing_target_never_creates` (`00:00:02.3670772`)
- `AlwaysOn_channel_bound_survives_child_death_and_replies(Herdr, Grok)` (`00:00:02.8696930`)
- `Named_AlwaysOn_herdr_agent_holds_after_three_failures_keeps_its_timeout_shell_and_resumes_only_on_explicit_retry` (`00:00:02.9804285`)
- `Standing_history_recovery_preserves_native_identity_and_queued_reply(Grok, Herdr, True)` (`00:00:03.2919199`)
- `Standing_history_recovery_preserves_native_identity_and_queued_reply(Grok, Herdr, False)` (`00:00:04.5563379`)
- `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(Grok, True)` (`00:00:07.2056573`)
- `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, True)` (`00:00:07.2664705`)
- `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(Grok, False)` (`00:00:08.5925166`)

The same method's other PtyHost rows passed in this chunk, including `(ClaudeCode, True)` at `2026-11-11T12:06:57.5821400+00:00` for 7.266s. That is the full three-launch body. `(Grok, True)` and `(Grok, False)` also passed. `AlwaysOn_channel_bound_survives_child_death_and_replies(PtyHost, ClaudeCode)` passed.

## Classification

The recorded check is an assertion on returned session state. Expected Running, actual Failed, reason `stale-observation`. It is not a poll expiry, a harness timeout, or a sharing violation. TRX `timeout=0`. The test deadline in source is 90s and the launch-idle wait is 50s. This row ended in 2.279s. Stderr is absent. No other test shared the window.

The state was stored because startup input was refused. `AgentSessionService.LaunchInteractiveAsync` copies `ex.Message` into `FailureReason`. `ProviderDialogInputRefusedException` formats that message as `Automated Claude input withheld: ` plus the outcome. The outcome here is `stale-observation`.

## Same method, different failure from the b6bbfa26b target

Run 6591e378 final result, `origin/feat/card-task-d6579463` at `7a3c45c3f82e36c7b992d5b4b66316cd52127b47`, file `docs/investigations/2026-11-11-run-6591e378-final-result.md`, group 74 row:

`Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, True)` at `2026-11-10T16:07:22.8159711+00:00`. Status should be Running but was Failed: `observation-unknown`. Fix named there: `b6bbfa26be15fad1fb6a6d23d0ba75fd2b1bd2dc`.

The g74 why-missed note `docs/investigations/2026-11-10-why-missed-herdr-alwayson-parity-g74.md` records that same row at 4.669s, on the second fresh PtyHost Claude session, with `FailureReason` `Automated Claude input withheld: observation-unknown`. It records that `(ClaudeCode, False)`, `(Grok, True)` and `(Grok, False)` passed in that run.

This chunk is the opposite corner of that matrix:

| Run | Row | Reason | Result |
|---|---|---|---|
| 6591e378 group 74 | `(ClaudeCode, True)` | `observation-unknown` | failed, 4.669s, second session |
| 6591e378 group 74 | `(ClaudeCode, False)` | | passed |
| d88de419 group 75 | `(ClaudeCode, True)` | | passed, 7.266s, full body |
| d88de419 group 75 | `(ClaudeCode, False)` | `stale-observation` | failed, 2.279s, first session, line 67 |

`b6bbfa26b` is in this cut and the row it names now passes. The red row is the other Claude PtyHost argument, and the refusal string is the one that commit newly throws.

## Cause

`WriteStartupAsync` in `server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs` waits, and writes nothing, while custody holds and the frame is neither Clear nor Blocked. That wait is `b6bbfa26b`. After a wait, this guard throws before any byte:

```csharp
if (heldUnclassified && !IsComposerProbeToken(input))
    throw new ProviderDialogInputRefusedException(ConditionalInputOutcomes.StaleObservation);
```

`IsComposerProbeToken` is true only for a string of length at least 2 whose every character is an ASCII letter or digit. `ComposerInputProbe.TokenFor` (`"zz"` plus 8 hex chars) passes. `ComposerInputProbe.KillLine` is `"\x15"` and does not. A trust digit and an effort key do not.

Each `write(...)` from `ClaudeStartupReadiness` is its own `WriteStartupAsync` call. `heldUnclassified` does not carry from the probe-token call into the kill-line call. The probe token can wait out the banner and be sent. The next call, if its own first snapshot is still unclassified and a later snapshot is the composer, takes the new guard and fails the launch with `stale-observation`. `WaitForReadyAsync` does not catch `ProviderDialogInputRefusedException`, so the outcome is stored as `FailureReason` rather than as `ProbeFailed`.

The TRX does not record which input was refused. Two producers in this method format the same string:

1. The new guard above, immediate, zero send attempts. This string did not exist before `b6bbfa26b`.
2. `SendConditionalInputAsync` returning `stale-observation` until `StartupStaleObservationAttempts` (3) is exhausted, then `RequireWritten`. That path is the probe token, which is allowed through the new guard.

The 2.279s row, with the process tree captured about 1.5s after the test start and disposed by 2.2s, fits one unclassified wait (50ms) plus harness setup better than a probe budget expiry. The sibling `(ClaudeCode, True)` completed the whole method, including its probe, 1.8s before this row. The unit test `A_dialog_key_is_not_typed_into_the_composer_that_replaces_an_unclassified_banner` pins producer 1 for input `"j"`. It does not send `KillLine`.

The boot prompt on this first launch is the history marker (`native-history-marker-` plus a guid). Hyphens make it fail `IsComposerProbeToken`, but that body is sent through `SendGuardedLineAsync` / `WriteGuardedAsync`, which `b6bbfa26b` did not change. A single conditional `stale-observation` from that path would use the same message. The result text does not separate that path from producer 1.

Debug should keep the banner wait for the probe token, and keep a real dialog key (`j`, a trust digit, an effort key) refused with nothing typed. The kill-line write that follows a banner must be allowed to send once the frame is the composer. Re-applying the observation-unknown wait does not describe this row: that row passed in this chunk.

Why-missed note: `docs/investigations/2026-11-11-why-missed-herdr-ptyhost-stale-observation-g75.md`.
