# CARD-1964 plan review, round 2

CLEAN. Amendment 1 closes B-1, B-2, and B-3. Advisories below do not hold the land.

Reviewed source `125222cb4072b9e19ce3e68711ea0b341f8d5ac9` on `feat/card-task-2bef90c6`. Diff only `aa75d062ab715466015b4af5128e3c83ccaf0356..125222cb4072b9e19ce3e68711ea0b341f8d5ac9` (one plan commit, one file). Plan owner `2bef90c6-2110-4a4b-8833-ad489da23947` (Claude Opus). Previous review `21daa066-2d83-4980-875b-922f59187503` at `aa75d062ab715466015b4af5128e3c83ccaf0356`. `origin/master` is `300c8a99d0d267a39791410b7540189ab702f251`. Reviewer is Grok. Mutation stays waived (operator 2026-11-12). Positive controls stay pending. `reviewedSourceClean` stays false: the import below is a manifest, and this plan review has no checkpoint-tool receipt.

## B-1. Watchdog state machine

The rewritten Watchdog section is a four-state machine (Off, Skip, Watching, Pending) with ten per-connection fields under `ConnectionState.WatchLock`: `FirstSeenAt`, `LastListAt`, `LastRestartAt`, `FiredWindowStart`, `RestartRequested`, `AttemptId`, `AttemptStartedAt`, `AttemptStep`, `AttemptCts`, `OverrunFiredAttemptId`.

The window start is `W = max(FirstSeenAt, LastListAt, LastRestartAt)`. A `no_list` fire requires `now − W ≥ WindowSeconds` and `FiredWindowStart ≠ W`. Pending writes no log and cancels nothing. Apply runs in `RunOneAsync` after the live and socket checks and before the `recoveringLease` branch: it clears `RestartRequested`, sets `LastRestartAt`, clears `NextCatchUp`, and the same turn falls through to catch-up when the runner is ineligible. That matches the current control flow. An eligible runner returns at `PhoneHomeRecoveryPump.cs:170-173` after refresh, so the same-turn replacement there is the detached refresh, which the plan states.

`RecoveryWatchdogPollSeconds <= 0` leaves `WatchdogLoopAsync` stopped. The pump loop keeps running. `RecoveryWatchdogIntervals <= 0` does the same and makes `RunWatchdogOnce` return at once.

### P-1, recomputed

Real constants at `300c8a99d`: `CatchUpRetrySeconds` defaults to 5 and the validator requires at least 1 (`PhoneHomeRunnerSettings.cs:71`, `:302`). `InventoryRefreshSeconds` defaults to 30 and `<= 0` disables refresh (`:78`, pump `:238`). `RequestTimeoutFor(List)` is 60 seconds (`PhoneHomeLiveConnection.cs:371`). The 50 ms `Task.Delay` at `PhoneHomeRecoveryPump.cs:76` is the pump tick. The plan's poll is the new `RecoveryWatchdogPollSeconds`, default 10.

`ListBound` 90, `CatchUpRetrySeconds` 5, poll 10, and the floor's 30 give `90 + 5 + 10 + 30 = 135`. `RecoveryWatchdogIntervals × RefreshBasis` is `6 × 30 = 180`. `WindowSeconds = max(180, 135) = 180`. The earliest `no_list` cancel of an attempt applied at `R` is `R + WindowSeconds`, so `R + 180`. The replacement List ends by `R + ListBound` (`R + 90`): it completes, the 60 s reply timer fires, or the 90 s step budget fires. `180 > 90`. The floor is `WindowSeconds ≥ ListBound + CatchUpRetrySeconds + max(0, RecoveryWatchdogPollSeconds) + 30`, which is strictly above `ListBound` for every legal retry (at least 1) and a positive poll. A silent reply also fits a second attempt inside the default window: `60 + 5 + 60 = 125 < 180`.

`OverrunLimit = CatchUpBound + ListBound`. `CatchUpBound` is the setting when it is positive and 300 when it is disabled, so `OverrunLimit > ListBound`. `attempt_overrun` cannot cancel the replacement before the List budget either.

### P-2, and the 390 second figure

There is no 390 constant in the phone-home code. 390 is `CatchUpTimeoutSeconds` default 300 plus `CatchUpListTimeoutSeconds` default 90, both new settings. At those defaults two cancels on one connection are `min(180, 390) = 180` seconds apart. Between fires the existing pacing still applies: a failed attempt sets `NextCatchUp` to now plus `CatchUpRetrySeconds` (`PhoneHomeRecoveryPump.cs:192`). The watchdog clears that delay at most once per applied restart. Pending suppresses both triggers until apply, so a parked loop does not cancel on every poll.

The plan's sentence "never under 135 s, since `OverrunLimit ≥ 60 + 300`" is stronger than the machine. The 60 and 300 stand-ins apply only when those settings are `<= 0`. A small positive budget makes `OverrunLimit` smaller than 135 while remaining strictly above `ListBound`. That does not reopen the every-poll cancel. See advisories.

### Skip, and a healthy runner with refresh off

The analysis is right. When `DispatchEligible` is true, `RunOneAsync` calls `RefreshInventoryIfDue` and returns (`:170-173`). That helper returns immediately when `InventoryRefreshSeconds <= 0` (`:238`). An eligible runner with refresh off sends no List. `no_list` on that runner would be a false Error every window. Skip requires eligible, refresh off, and no attempt in flight. V-11 pins zero L6 after 400 s of heartbeats. At base the watchdog seam logs nothing, so V-11 is a green guard.

### Races

A completion publishes `LastListAt` from `ReadInventoryAsync` on success, and every watchdog field is read and written under `WatchLock`. The production loop is one `WatchdogLoopAsync`, so two polls do not overlap if each call is awaited. The plan does not say the success write and the fire decision are one critical section that re-reads `W` immediately before cancel. Under P-1 the replacement List is already finished before its own window elapses, so that gap cannot cancel the replacement during its 90 s budget. A list that completes in the same instant an older window expires can still be cancelled if the poll sampled the old `W`. The next window then waits a full `WindowSeconds`. That is one extra cycle, which is the advisory below, and it is the opposite of the round-1 every-poll cancel.

## B-2. List codes and the new red rows

`RequestAsync` takes `_send` at `:410`, awaits `beforeWrite` at `:412-413`, and writes the frame at `:415`. The 60 s reply delay is armed at `:435-437`, after that write. A `beforeWrite` that returns the test's uncompleted `release.Task` holds the gate and writes nothing. The connection has no timer that completes that task. `PhoneHomeRunnerClient.ListAsync` (`:191-193`) calls `RequestAsync(List, null, ct)`, so the pump's List waits on the same semaphore. Default `MaxInFlightRequests` is 32, so the one parked holder does not fail the pump with the in-flight limit. Heartbeats in the existing fixtures are inbound `EmitAsync` frames (`PhoneHomeRecoveryEligibilityTests.Lease.cs:195-198`); they do not take `_send`.

At base the pump passes the stopping token. Advancing `live.Clock` does not release `_send.WaitAsync`. V-10 `catch_up` therefore times out its 5 s cycle wait, and V-10 `refresh` sees no refresh warning. Both are red. After the step budget, that same wait is the only timer that can end the parked List, so the warning code can be `catch_up_list_budget` or `refresh_list_budget`. The catch-up filter at `:361` still rethrows every `OperationCanceledException`; the plan says the new catch maps a step or budget cancel while the stopping token is not cancelled.

V-7 asserts eligibility, the recovery exception, an early retry, and the List count. It does not assert a budget code. A silent List still fails at the 60 s reply timer, so both V-7 cases stay green. V-4 asserts `RequestTimeout` on the silent-List warnings and one L6 per window. Its red at base is step (2): the seam is a no-op and no L6 line exists. The second-window asserts sit after that failure, so the repeated-fire checks do not turn the row green at base.

Recount of the method table: 11 methods and 14 cases. Red at base: V-1, V-2, V-3, V-4, V-5, V-6 ×2, V-8, V-9, V-10 ×2 (11). Green guards: V-7 ×2 and V-11 (3).

## B-3. Checkpoint floors

Member `[Test]` methods, with each `[Arguments]` row as one case and string-literal fixture text excluded. No roster method has `MethodDataSource`, `Matrix`, `Skip`, or `Explicit`. Three classes carry a shared `ClassDataSource` fixture (`TestClassificationPolicyTests`, `C1729LayeringGuardTests`, `DerivedImpactIndexTests`); that fixture adds no cases. `NotInParallel` does not skip.

| Row | Counted cases | Plan Min |
|---|---|---|
| CP-1 | `C1964RecoveryPumpStallTests` 14 (11 methods; V-6, V-7, V-10 have 2 arguments each) | 14 |
| CP-2 | 14 + `PhoneHomeRecoveryEligibilityTests` 13 (main file 3, `.Lease.cs` 4 methods / 10 cases) + `MultiRunnerRecoveryTests` 8 + `PhoneHomeEventPumpTests` 8 | 43 |
| CP-3 | `PhoneHomeDeferredKillTests` 3 + `PhoneHomeStrandedQueueTests` 16 + `PhoneHomeTranscriptIncidentTests` 5 + `DefaultRunnerEligibilityTests` 3 | 27 |
| CP-4 | `PhoneHomeDirectoryTests` 8 + `PhoneHomeRunnerRetirementIdentityTests` 9 (7 methods) + `PhoneHomeRunnerRetirementCycleTests` 1 + `PhoneHomeImmediateSendTests` 13 | 31 |
| CP-5 | `PhoneHomeBackpressureTests` 9 (`.cs` 6, `.Bounds.cs` 3; `.Scheduling.cs` and `.Protocol.cs` have no tests) + `PhoneHomeRollingRunnerTests` 34 (drain 23 from 22 methods, retire 4, main 7) | 43 |
| CP-6 | policy 42 (28 methods), guard 1, merge 5 (3), tripwire 2, native Linux 3, mechanical 11 (7), platform guidance 9, layering 11, lane 1. The two `tests/Shared` classes are linked at `Antiphon.Tests.csproj:185` and `:209` | 85 |
| CP-7 | `DerivedImpactIndexTests` 27 member `[Test]` methods at lines 134-877 | 27 |

`docs/testing-and-build.md` still says the nine-class union is 78. This tree's attribute expansion is 85 (policy 42, platform guidance 9, layering 11). CP-6's Min matches the tree. The class filter matches the guard batch at that doc's line 323.

Import at this SHA, load 14.35, slot `402a0ec4-9b39-43dc-b48b-fafd4816d9e1`, waited 135 s, held 17 s. `dotnet build tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj -p:UseAppHost=false` exited 0 with 0 errors and 25 compiler warnings. `import` printed `imported 7 rows -> .antiphon/c1964-plan-review-r2/manifest.yaml`, exit 0, no importer warnings. `minExecuted` is 14, 43, 27, 31, 43, 85, 27. Timeouts are 45, 45, 15, 15, 18, 18, 36 minutes. Builds are `bin-c1964-red/` and `bin-c1964-green/`, trailing slash, and neither name contains `ref` or `runtimes`.

## Checks at the reviewed SHA

| Check | Result |
|---|---|
| `git merge-tree --write-tree origin/master HEAD` | tree `285c01c92a9f8fcec6e3924e04d07e66f839f745`, exit 0 |
| `scripts/check-evidence-diff.ps1` `aa75d062a..HEAD` | commits=1, entries=0, violations=0, exit 0 |
| `scripts/impacted-classes.ps1 -Range` | kinds and filter empty |
| `scripts/impacted-classes.ps1 -MergeSubset -Range` | `migrations=False`; downgrade selector count 0 |
| Checkpoint import | 7 rows, exit 0, no warnings; Mins match the table above |
| Product tests | Omitted. The subject changes no code or tests. The brief forbids builds beyond this import. |

Producer/recipient delivery does not apply. The diff changes no session that waits for input. Parking disabled (CARD-1083) is unchanged. No OS-selected production path changed.

## Advisories

These do not hold the land (D-2).

- Say that "never under 135 s, since `OverrunLimit ≥ 60 + 300`" is the disabled-stand-in case. 390 is the sum of the two new defaults, and a small positive `CatchUpTimeoutSeconds` or `CatchUpListTimeoutSeconds` makes the gap `min(WindowSeconds, OverrunLimit)` shorter than 135 s while keeping it above `ListBound`.
- Publish `LastListAt` under `WatchLock` before `ReadInventoryAsync` returns, and hold that lock across the poll's read of `W` and its cancel, so a poll cannot fire on a `W` it sampled before the success write. One awaited loop already prevents overlapping production polls; the same critical section covers a direct second call.
- The refresh filter at `PhoneHomeRecoveryPump.cs:258-259` rethrows `OperationCanceledException` when its token is already cancelled. Passing the step token as that `ct` skips the `:264` warning unless that filter maps the budget cancel the way `:361` must. V-10 `refresh` depends on the warning.
