# CARD-1964 plan review, round 1

BLOCKERS. Three defects hold the land. Advisories below do not.

Reviewed source `aa75d062ab715466015b4af5128e3c83ccaf0356` on `feat/card-task-6652bca1`, plan owner `6652bca1-1f94-4925-8926-35aee917e42f` (Claude Opus). Base and `origin/master` are `300c8a99d0d267a39791410b7540189ab702f251`. The diff is only `docs/superpowers/plans/2026-11-17-card-1964-phone-home-pump-stall-plan.md`. Reviewer is Grok. Mutation stays waived (operator 2026-11-12). Positive controls stay pending. `reviewedSourceClean` stays false: the import below is a manifest, and this plan review has no checkpoint-tool receipt.

## Confirmed

### Diagnosis (check 1)

G-2 is right. A List whose reply never arrives is already bounded. `RequestTimeoutFor` returns 60 seconds on the default arm (`PhoneHomeLiveConnection.cs:371`). `RequestAsync` starts that delay only after `_send.WaitAsync` and the write (`:410-416`), then waits with `Task.WhenAny` on `live.Clock` (`:435-446`). A timeout removes the waiter (`:441`). `Silent_list_reply_leaves_the_runner_ineligible_until_catch_up_succeeds` (`PhoneHomeRecoveryEligibilityTests.cs:28`) already proves the cycle returns false, the runner stays ineligible, and a later List answer recovers it.

The unbounded path is the one the plan names. `CatchUpAsync` calls `PersistTranscriptAsync` with no token (`PhoneHomeRecoveryPump.cs:388`). The runtime default is `CancellationToken.None` (`AgentSessionRuntime.cs:1351`), and that waits on `entry.Gate.WaitAsync` (`SessionStateStore.cs:117`). A lease re-recovery (`recoveringLease`) still runs that full transcript pass today. The only eligibility flip on expiry is `ReadSnapshot` (`PhoneHomeRunnerDirectory.cs:984-994`). Its two callers, `GetLiveStoreId` (`:121-136`) and `SnapshotOf` (`:972-981`), call `Notify` and do not log. One eligible-to-ineligible transition produces one `expiredRunnerId`. L1 beside both `Notify` calls is the right place.

### Fail-safe (check 2)

`MarkRecovered` and `TryRecoverLease` run only after `CatchUpAsync` returns true. A List failure returns false (`PhoneHomeRecoveryPump.cs:361-367`) and leaves the runner ineligible. A removed waiter drops the late Result (`PhoneHomeLiveConnection.cs:441` and the receive loop's `TryRemove`). Inventory replacement happens only when `ListAsync` returns. A skipped session stays in the inventory `ReplaceKnownLiveSessions` already stored, so its seat stays occupied. V-7's method row (eligible false, `Resolve` throws "has not completed recovery", early retry false, List count 1 or 2) is green at base because the 60-second timer already fails a silent List.

That holds when the step token is the token `ListAsync` observes, so a budget cancel removes the waiter. `WaitAsync` alone, around a List that still uses the stopping token, would let the abandoned request replace the inventory when the reply arrives. The plan specifies both. Both have to stay.

Heartbeats are the only lease renewal. Event frames do not renew it. While events kept flowing, those frames were delivered, the observer group stays up on lease recovery (`StartPump` only when `!recoveringLease`), and live observers persist through `ObserveTranscriptAsync`. List-only re-recovery is safe for that case.

### Deadlines and the gate (check 3)

Cancel before the gate is acquired does not hold it (`SessionStateStore.cs:120`, `releaseGate: false`). Cancel after acquire holds the lease until `ReconcileAsync(false, CancellationToken.None)` returns (`AgentSessionRuntime.cs:1363`). The pump's `WaitAsync` returns while that task still runs. `MarkRecovered`, `TryRecoverLease`, and `StartPump` stay in `RunOneAsync` after the bounded await, so the abandoned save cannot set eligibility. List-only re-recovery does not re-enter persist, so the next lease cycle does not join that save. This is implementable. The missing sentence is an advisory, below.

### Watchdog structure that is sound

The watchdog is a second task. It logs, cancels the attempt CTS, and sets `RestartRequested`. It does not call `RunOneAsync`, and it does not touch `Pump`, `PumpStop`, `Recovered`, the socket, `Disconnect`, or `DispatchEligible`. The pump loop stays serial, so a restart does not double-run a cycle. `attempt_overrun` (390 seconds of the current attempt) does not re-fire on a fresh attempt. Rollback that sets `RecoveryWatchdogIntervals` to 0 does not start the loop.

### Red witnesses other than the L-List row

Eleven cases: nine methods, V-6 twice, V-7 twice. Nine are red at base and the two V-7 cases are green, if Code follows the method rows.

| Case | Why it is red at base |
|---|---|
| V-1, V-2 | Lease re-recovery and connect catch-up both reach the untimed gate at `:388` / `SessionStateStore.cs:117`. A 5-second cycle wait cannot finish. |
| V-3 | The only fetch bound is the 60-second reply timer. Advancing 31 seconds leaves the cycle unfinished. |
| V-4 method row | Red only on the missing L6 line. The base watchdog seam is a no-op. Eligibility after the List answers is already green (G-2). |
| V-5 | `GetLiveStoreId` and `SnapshotOf` emit zero log lines. |
| V-6 both | The false returns at `:179-180` and `:196-197` do not log. |
| V-7 both | Green guard. The 60-second timer already fails a silent List and leaves the runner ineligible. |
| V-8 | 46 seconds is inside the 60-second fetch timer, and the new total budget is unused at base. |
| V-9 | The hold waits on the stopping token (`:353`) and `RunWatchdogOnce` is a no-op, so L6 is absent and the cycle does not return. |

L1-L6 use fixed `Step`, `Reason`, `Stage`, `Trigger`, and `Code` sets. `SessionId` is a listed session. The templates carry no payload, ticket, secret, or transcript text. The "no more than capacity" phrase is slightly high; see advisories.

### Checkpoints other than CP-7

Re-import at this SHA, load 13.21, slot `4490399b-1c27-4412-94d6-7260aaea7b5f`, held 14 seconds: `imported 7 rows -> .antiphon/c1964-plan-review/manifest.yaml`, import exit 0. The tool build printed ordinary compiler warnings. The importer printed none. Rows CP-1..CP-7. Timeouts 45, 45, 15, 15, 18, 18, 36 minutes. Builds `bin-c1964-red/` and `bin-c1964-green/`, trailing slash, neither name contains `ref` or `runtimes`. CP-3..CP-7 reuse `bin-c1964-green`. One exact filter per row. Ids are `CP-<digits>`. CP-1 and CP-2 are separate cold builds at 15 minutes each. The named regression classes are in `tests/Antiphon.Tests` except the new `C1964RecoveryPumpStallTests`, which the red commit adds. `DerivedImpactIndexTests` has 27 class-level `[Test]` methods and no `[Arguments]`.

### Collisions and activation (check 7)

`origin/feat/card-task-aaabade9` at `5d0ac1366096de492324e2bf57634f389c332409` (CARD-1960 code) changes `SessionRunnerEndpoints.cs`, `PhoneHomeRunnerClient.cs`, `RemoteWorkspaceMirrorResult.cs`, `scripts/workspace-slots.ps1`, docs, and its own tests. None of those paths are in this plan's diff, and none are in the slice-1 edit set (pump, directory, settings, `PhoneHomeTestHost.cs`, the new stall tests, `tests/derived-impact-index.json`). CARD-1960 plan commit `a84420b03` does not edit `PhoneHomeLiveConnection.cs`. This slice does not either. The change is server-only: AppHost restart, no runner change. A scan of the 60 newest `origin/feat` refs against the pump, directory, settings, live connection, test host, eligibility tests, `SessionStateStore`, and `AgentSessionRuntime` found no other branch touching them from its merge base with `origin/master`.

## Blockers

### B-1. The watchdog re-cancels every poll while `no_list` stays true

Where: plan "Watchdog", window and action (`docs/superpowers/plans/2026-11-17-card-1964-phone-home-pump-stall-plan.md` around the `no_list` trigger and the three action bullets).

Failure: after the first `no_list` fire, every later `RunWatchdogOnce` cancels the replacement attempt. The default poll is 10 seconds and the List budget is 90 seconds, so the replacement never finishes. The runner stays ineligible. That is the outage again. `RecoveryWatchdogPollSeconds <= 0` is also unspecified against the start condition `RecoveryWatchdogIntervals > 0`: a zero poll with a positive interval has no "do not start" sentence, so a `Delay(0)` loop is allowed by the text.

Why: the window counts from the later of the last successful List and the first time the pump saw the connection. A restart does not move that start. "Once per window" is attached to the L6 log. Cancel and `RestartRequested` are separate and unbounded. V-4 checks that two calls emit one L6. It does not check that the second call leaves the new attempt's CTS alone. `attempt_overrun` does not have this hole, because it measures the current attempt.

Fix: on each applied restart, open a new `no_list` window from the later of the last successful List, the first sight of the connection, and that restart, and cancel once per window. A 90-second List then gets its budget. `RecoveryWatchdogPollSeconds <= 0` leaves the loop stopped.

### B-2. `catch_up_list_budget` cannot be the code a silent List produces

Where: the L-List witness row. It says V-4 and V-7 assert `{Code}=catch_up_list_budget` when the clock passes 90 seconds before the 60-second reply timer is relevant, and that Code chooses the order so the budget fires first.

Failure: on one `live.Clock` the 60-second reply timer completes a silent List first. The existing catch logs `ProblemCode` of `PhoneHomeTransportException` `RequestTimeout`, which is not `catch_up_list_budget`. If Code adds that assertion to V-7, the two cases the plan calls green guards fail at base and stay failed after implementation. CP-1 expects those two passed and nine failed.

Why: `CatchUpListTimeoutSeconds` defaults to 90 and `RequestTimeoutFor(List)` is 60. Both read the same clock. A scripted silent List has already passed `_send` and the write, so the reply delay is running. Advancing the clock to 90 seconds has already passed 60 seconds. There is no order that makes the 90-second budget win. The 90-second budget covers the wait before that delay: a stuck `_send` or write (`PhoneHomeLiveConnection.cs:410-416`). The V-4 method row already states the red correctly (missing L6 only). The witness row contradicts it, and it contradicts V-7's method row, which asserts eligibility and does not assert this code.

Fix: witness `catch_up_list_budget` with a stalled send gate. A silent reply keeps the existing request-timeout code. Leave V-7 on its method-row asserts so it stays a green guard. Leave V-4 red only on L6.

### B-3. CP-7 Min 42 cannot be met

Where: checkpoint CP-7, Min 42, filter `/*/*/DerivedImpactIndexTests/*`. The imported manifest stores `minExecuted: 42`.

Failure: `RowRunner` fails the row when executed is below Min (`tools/Antiphon.Checkpoints/Execution/RowRunner.cs:414`). This class cannot produce 42 executions.

Why: `tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs` has 27 class-level `[Test]` methods (lines 134 through 877) and no `[Arguments]`. The other `[Test]` tokens in the file are fixture text. Regenerating `tests/derived-impact-index.json` does not add methods to this class. A prior execution of the same class recorded 27 (CARD-1883 plan review).

Fix: set CP-7 Min to 27.

## Advisories

These do not hold the land (D-2).

- Say the next-cycle rule in one sentence: a later persist of that session uses a new step token and `WaitAsync`, and does not join the abandoned task; one abandoned persist is not retried on the lease-refusal turn and is not spawned on each watchdog poll. The design already avoids a forever pump wait. An abandoned reconcile can still stall the observer that shares the gate until `ReconcileAsync` returns. That is the same holder duration as an in-progress persist today.
- When events also stopped, List-only recovery does not fetch transcript frames the runner never put on the socket. A queue overflow still closes the socket. Quiet gaps stay on the existing on-demand transcript pull and receipt readiness, which the pump does not run for every listed session. D-3 already accepts eligibility from the completed List, and the seat stays occupied. F-3 ships List-only with no rollback setting. Record the boundary; it does not overturn the design.
- L2's "no more than capacity" overstates the cap. `ReadInventoryAsync` lists Running and Starting sessions, which can exceed the capacity setting. Cardinality is still one line per listed session per catch-up, with a fixed template and no secret.
- CARD-1960's branch also carries `PhoneHomeRunnerClient.cs`, the mirror DTO, and its tests. The plan's collision paragraph names a shorter set. The sets stay disjoint.

## Checks at the reviewed SHA

| Check | Result |
|---|---|
| `git merge-tree --write-tree origin/master HEAD` | tree `c851c8645b439fa8ba589daf421a1590ed62a8e0`, no conflicts |
| `scripts/check-evidence-diff.ps1` base..HEAD | commits=1, entries=0, violations=0 |
| `scripts/impacted-classes.ps1 -Range` | kinds, classes, files, derivedClasses, filters, and windowsFilters empty; downgrade count 0 |
| Checkpoint import | 7 rows, exit 0, no importer warnings; CP-7 minExecuted 42 is B-3 |
| Product tests | Omitted. The subject changes no code or tests. The brief forbids builds beyond this import. |

Producer/recipient delivery and CARD-1083 input-wait release do not apply. No session that waits for input changes. No OS-selected production path changed.
