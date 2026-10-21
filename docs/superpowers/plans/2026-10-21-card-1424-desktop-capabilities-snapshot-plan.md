# CARD-1424 plan: last-known-good desktop capabilities snapshot

Plan stage, 2026-10-21. Branch `feat/card-task-039de4fc`, start `100f06ff2`.
Investigation: [2026-10-21-card-1424-desktop-runner-flap.md](../../investigations/2026-10-21-card-1424-desktop-runner-flap.md).

## Problem in one paragraph

For the desktop, `PhoneHomeRunnerDirectory.DescribeAsync` makes a live `GET /capabilities` with a
2 s limit and keeps no copy of the last good answer. `PhoneHomeRunnerDirectory.cs:745-772` has the
code. Any failed probe returns `Platform = null`, `Available = false`, `DispatchEligible = false`,
`Stale = true` and `Capabilities = null`. The failure can be the 2 s deadline, the shared limiter
(32 permits, queue 0) or an open read circuit. One failed probe when the desktop is saturated
blanks the desktop row. The orchestrator settings version then flips and a settings-changed note
goes out. Windows-pinned dispatch is held
(`AgentTaskDispatcher.cs:6342-6363`, "runner platform evidence for Windows is not current on
'desktop'"). The next good sweep reverses it. The fix keeps the last good answer for a bounded
grace period. A runner that has really gone away still goes stale, either immediately or once the
grace period ends.

## Ground truth (card / brief assumption vs code at `100f06ff2`)

| # | Assumption | What the code does | Consequence for the plan |
|---|---|---|---|
| G-1 | The desktop row comes from a probe with no last-known-good | True. The desktop branch of `DescribeAsync` (745-772) never reads `_slots`. Every failure path ends in `caps = null` (756-763). | The snapshot lives in the directory, beside the probe. |
| G-2 | Remote runners already have a grace period | True. They use `RegisteredPlatform`/`PlatformObservedAt` plus the lease `PhoneHomeProtocol.DefaultLeaseSeconds = 90` (`PhoneHomeContracts.cs:20`), checked at 774-784. | Precedent for a time-bounded grace. This plan does not change remote runners. |
| G-3 | The failure reason (deadline / limiter / circuit) can be logged | **Only partly.** The directory's own 2 s deadline reaches the directory as `OperationCanceledException`. Limiter and circuit rejections become `HttpRequestException` in `AdmittedReadHandler.cs:98-108`, and `SessionRunnerHttpClient.GetCapabilitiesAsync` (357-369) **swallows** them into `null`. The resilience deadline becomes `TaskCanceledException`, which is also swallowed. | A probe that returns a reason is needed (S1). Changing `GetCapabilitiesAsync` is not enough, because it is documented as "null on ANY failure" and its other callers rely on that. |
| G-4 | Only the desktop's descriptor consumers need changing | The consumers call `DescribeAsync`, so they all pick up the snapshot with no edits. The table in D-6 lists them. | No consumer is edited. Every consumer shares one source. |
| G-5 | The settings flap comes from `Platform`/`DispatchEligible`/`Features` | True. `OrchestratorInstructionsSnapshotBuilder.cs:143-155` copies `Platform`, `DispatchEligible` and `Features`, plus capacity fields. It does not copy `PlatformObservedAt`. | `PlatformObservedAt` can show the real age of the snapshot (D-4) without changing the settings version. |
| G-6 | The 60 s sweep is the only prober | **False.** `DescribeAsync(null)` is also called by the dispatcher platform gate, `HostStatsPollService`, `GET /api/session-runners`, seat release, placement and expectations, each at its own rate. | The number of probes depends on how many callers there are, not on how much time has passed. Staleness is therefore based on time (D-2), not on a failure count. |
| G-7 | The fix cherry-picks onto `rc/2026-10-20` | The brief named tip `806b23084`, and the rc tip now is `9662288b2`. At both tips, `git diff HEAD <tip>` is empty for `PhoneHomeRunnerDirectory.cs`, `SessionRunnerHttpClient.cs`, `ISessionRunnerClient.cs`, `PhoneHomeRunnerSettings.cs`, `AdmittedReadHandler.cs` and `PhoneHomeDirectoryTests.cs`. | It should cherry-pick cleanly (D-9). Landing confirms this with `git merge-tree`. |

## Decisions

**D-1. One snapshot, kept in `PhoneHomeRunnerDirectory`, used through `DescribeAsync`.**
Add a private `DesktopCapabilitiesSnapshot` to the directory, which is a singleton
(`Program.cs:339`). It holds:
- `LastGood` (`RunnerCapabilitiesDto?`);
- `ObservedAt` (the clock time of the last successful probe);
- `ConsecutiveFailures`;
- `Absent` (bool), set by a definite-absence failure (D-3) and cleared only by a success;
- `LastReason`, `StreakStartedAt` and `LastLoggedAt` (for D-5).

A lock (`_desktopGate`) guards it. The probe runs outside the lock, and only the fold into the
snapshot runs inside it.
*Rejected:* caching inside `SessionRunnerHttpClient`. It already has a 5-minute cache for launch
checks, with different freshness rules (`CapabilityProbeTtl`), and other callers of
`GetCapabilitiesAsync` must keep "null on any failure".
*Rejected:* a separate hosted poller that owns the snapshot. It adds a second clock and a second
place to read from, and is more than an RC fix should carry.

**D-2. Staleness is time-based: `DesktopCapabilitiesGraceSeconds`, default 180, with 0 restoring
today's behaviour.** After a failed probe, the descriptor serves `LastGood` if all three hold:
- `LastGood` is not null;
- `Absent` is false;
- `now - ObservedAt <= Grace`.

Otherwise it returns today's stale descriptor unchanged.

Why 180 s:
- The sweep runs every 60 s (`OrchestratorInstructionsSettings.cs:12`).
- The observed note-to-note recoveries took 60–130 s. That means at most two sweeps in a row
  failed.
- Measured server stalls peaked at 22 s.
- 180 s is three sweep intervals. Two failed sweeps in a row still find a snapshot no older than
  about 120 s plus jitter, with roughly 60 s to spare.
- Any successful probe from another caller resets the age (G-6), so in practice the window rarely
  gets near the limit.

The cost is how long a hung desktop runner (process alive but not answering) stays visible: at
most 180 s after the last good probe, plus the time until the next probe. That is twice the
remote lease of 90 s, which was rejected as the default: a 90 s grace still lets the 130 s cases
flap whenever no other caller succeeded between sweeps.
*Rejected:* N consecutive failures, either alone or combined with the time limit. Each caller of
`DescribeAsync` probes on its own (G-6). Under a 20 s stall, the dispatcher, the host-stats poll
and the catalogue can together build up any N within seconds, so N measures caller traffic, not
time. `ConsecutiveFailures` is kept only for the log line.
*Rejected:* single-flight probing, or a minimum interval between probes. Either would reduce load
and limiter pressure, but it changes how fresh the answer is for every consumer, including seat
release's runner store id. Follow-up candidate only.

**D-3. Definite absence ends the grace period at once (no dispatch to a dead runner).**
S1 classifies each failed probe as one of the reasons below:

| Reason | Kind | Source |
|---|---|---|
| `deadline` | ambiguous | the directory's 2 s timer fired, or the resilience `TimeoutRejectedException` became `TaskCanceledException` |
| `limiter` | ambiguous | `HttpRequestException` whose inner exception is `RateLimiterRejectedException` |
| `circuit` | ambiguous | `HttpRequestException` whose inner exception is `BrokenCircuitException` |
| `transport` | ambiguous | any other `HttpRequestException` with no status code |
| `refused` | **definite** | `HttpRequestException.HttpRequestError == ConnectionError` (localhost, nothing listening: the runner process is gone) |
| `http-<code>` | **definite** | `HttpRequestException.StatusCode` is set (the runner answered and refused) |
| `bad-body` | **definite** | `JsonException` / `NotSupportedException` |
| `no-answer` | ambiguous | the default interface body: a client that returns null without a reason (test fakes, old clients) |

- An ambiguous reason gets the grace period.
- A definite reason sets `Absent = true`, so later failures cannot revive the snapshot, including
  `circuit` once the breaker opens on those refusals.
- On the desktop, a stopped runner refuses the connection, so it goes stale on the first probe.
- A runner that is alive but not answering goes stale when the grace period ends.

Either way, no dispatch is sent to a dead desktop runner. The `Hold` in
`ClassifyRequiredPlatformAsync` applies again within the grace bound. The launch itself also still
goes to the runner and fails closed if that runner is gone.

**D-4. The descriptor served during the grace period.**
- Values copied from `LastGood`: `Platform`, `Capabilities`,
  `DispatchEligible = features.Contains(RunnerPlatformWire.Feature)`, `Available = true`,
  `Stale = false`.
- `PlatformObservedAt = ObservedAt`. This is the time of the last success, not now, so the
  catalogue shows the real age.

A successful probe behaves as today: `PlatformObservedAt = now`, and the snapshot is replaced.
After a success, `Absent` is false and `ConsecutiveFailures` is 0. No new field is added to
`RunnerDescriptor`, so no consumer or DTO changes.

**D-5. One Warning per failure streak, then rate-limited.**
The probe logs a failure in four cases:
- the first failure of a streak;
- a change of reason;
- the change to stale (grace expired, or a definite failure);
- otherwise, at most once every `DesktopCapabilitiesFailureLogSeconds`, default 60.

The log template is:
`Desktop runner capabilities probe failed reason {Reason} consecutive {Failures} snapshotAgeSeconds {AgeSeconds} serving {Serving}`,
where `Serving` is `last-good` or `stale`.

The first success after a streak logs at Information:
`Desktop runner capabilities recovered after {Failures} failed probes in {Seconds}s`.

The rate limit uses `_clock`, so tests drive it with `FakeTimeProvider`. Uncertainty 1 in the
investigation (which failure actually happened) becomes answerable from the desktop server log
from now on.

**D-6. Consumers, all reached through `DescribeAsync` with no edits.**

| Consumer | Site | Effect of the snapshot |
|---|---|---|
| Dispatch platform gate | `AgentTaskDispatcher.cs:6342` `ClassifyRequiredPlatformAsync` | Windows-pinned desktop work is no longer held by one failed probe. The Hold comes back once the grace period expires or the runner is definitely absent. |
| Runner catalogue / `GET /api/session-runners` | `SessionRunnerCatalogue.cs:25` | Platform and features stay set. `platformObservedAt` shows the age of the snapshot. |
| Orchestrator settings snapshot | `OrchestratorInstructionsSnapshotBuilder.cs:107-121` (through the catalogue) | The version stops flipping, so no paired settings-changed notes. |
| Host stats | `HostStatsPollService.cs:134-150` | Platform and features are no longer blanked. It already forces desktop available/eligible. |
| Seat release discovery / readiness / peer hold / release | `TerminalRunnerSeatReleaseService.cs:343, 515, 817, 1088` | It reads `RunnerStoreId` and `TerminalSeatReleaseV1` from the snapshot. That is safe: the release itself is a runner RPC fenced by store id and generation, so it fails and holds if the runner is gone. The store id is durable, and a runner restart is picked up by the first successful probe. |
| Placement at task create | `AgentTaskService.cs:1971` `DescribePlacementAsync` (callers 1405, 1420, 2025, 2053, 2064, 2076, 2094) | Desktop placement stops failing during a short probe failure. |
| Expectation watchdog | `ExpectationObservationAdapter.cs:61` | Reports available during the grace period. |
| Worktree card-branch check | `DelegationWorktreeService.cs:305` | First gate only. It still makes its own live `GetCapabilitiesAsync` call at 308, which keeps the final say. |
| Park publication | `TaskParkPublicationService.cs:50, 252` | Remote binding only, so not reached for the desktop. Listed only to cover every caller. |
| Remote workspace / mirror publisher | `RemoteWorkspaceService.cs:328, 972`, `PhoneHomeRunnerMirrorPublisher.cs:17` | Remote mirror paths. Not reached for the desktop in practice. |

**D-7. A probe that returns its reason: a new default interface method.**
Add `Task<RunnerCapabilitiesProbe> ProbeCapabilitiesAsync(CancellationToken ct)` to
`ISessionRunnerClient`. The default body wraps `GetCapabilitiesAsync` and reports `no-answer` on
null, so every fake and older client keeps compiling.

`RunnerCapabilitiesProbe(RunnerCapabilitiesDto? Capabilities, string? FailureReason, bool Definite)`
is a small record in `server/Application/Interfaces`.

`SessionRunnerHttpClient` overrides it. It makes the same
`ReadJsonAsync("capabilities", ResilienceOperations.RunnerCapabilities, ct)` call and passes the
exception to a static, pure `RunnerCapabilitiesFailure.Classify(Exception)` that applies the D-3
table. Caller cancellation still propagates.

`GetCapabilitiesAsync` becomes `(await ProbeCapabilitiesAsync(ct)).Capabilities`, so its behaviour
is unchanged. The directory classifies its own 2 s timer firing as `deadline`.

**D-8. Settings.** Two settings go on `PhoneHomeRunnerSettings`, beside `LeaseSeconds`, because
the directory already binds that class:
- `DesktopCapabilitiesGraceSeconds = 180`;
- `DesktopCapabilitiesFailureLogSeconds = 60`.

The desktop branch reads them whether or not `Enabled` is set. Code defaults are enough, so
`appsettings.json` does not change. Setting the grace to 0 is the rollback switch.

Docs: one sentence on the `GET /api/session-runners` row in `docs/ops-http.md`, saying that
the desktop row keeps its last good capabilities for the grace period. No other owner doc
describes the descriptor.

**D-9. Fit for the RC.** The change touches four production files and two new test files.
- **Production:** the directory, the HTTP client, the client interface and the settings.
- **Tests:** two new files.
- **Schema, API shape, DTO, client:** no change.

G-7 shows the touched files are identical on master and on both rc tips, so a
`git cherry-pick` onto `rc/2026-10-20` should apply cleanly. The orchestrator decides on the
cherry-pick at landing. This plan does not perform it.

**D-10. No session waits for input here.** This plan adds no session that waits for input and
changes none, so the CARD-1083 release checklist does not apply.

## Slices

### S1: probe that returns its reason (D-3, D-7)

Files:
- `server/Application/Interfaces/ISessionRunnerClient.cs`: add the default `ProbeCapabilitiesAsync`.
- `server/Application/Interfaces/RunnerCapabilitiesProbe.cs`: new record.
- `server/Infrastructure/Agents/SessionRunner/RunnerCapabilitiesFailure.cs`: new static
  `Classify(Exception) -> (string Reason, bool Definite)`.
- `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`: override
  `ProbeCapabilitiesAsync`, and route `GetCapabilitiesAsync` through it.
- Test: `tests/Antiphon.Tests/Application/RunnerCapabilitiesFailureTests.cs`.

Commits: test commit (red: `Classify` does not exist, so the build is red; see V-5), then the
implementation commit.

### S2: snapshot, grace, absence and log in the directory (D-1, D-2, D-4, D-5, D-8)

Files:
- `server/Application/Settings/PhoneHomeRunnerSettings.cs`: the two settings.
- `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs`: the snapshot, the
  fold, and the desktop branch of `DescribeAsync`.
- Test: `tests/Antiphon.Tests/Application/DesktopCapabilitiesSnapshotTests.cs`. It uses
  `FakeTimeProvider` and a scripted fake `ISessionRunnerClient` whose `ProbeCapabilitiesAsync`
  answers from a queue: caps, a failure with reason/definite, or a hang until cancelled. Construct
  the directory like `PhoneHomeDirectoryTests` does, with `NoScopeFactory` and settings
  `Enabled = false`. No real runner, no network.

Commits:
- **S2a:** test commit only. CP-1 (the red witness) is run on this commit.
- **S2b:** the implementation.

### S3: docs (D-8)

File: `docs/ops-http.md`, the runner catalogue row: one sentence on the desktop grace period and
`platformObservedAt`. No test.

### Checkpoints

Lane: server2 (`GET /api/runner-defaults` → `globalRunnerId: server2`). Every row uses fakes and
a fake clock, so no Windows host is needed. Omit `-Platform` and `-Runner`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2a | `tests/Antiphon.Tests -> bin-c1424/` | red-witness | `/*/*/DesktopCapabilitiesSnapshotTests/A_single_failed_probe_after_a_good_one_keeps_windows_and_dispatch_eligible` | V-1 | **red**: 1 executed, 1 failed (`Platform` null at the S2a test-only commit). Run via `scripts/run-checkpoint.ps1`, exit 1 expected. Not a checkpoint-tool row. | 1 | 6 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1424/` | classify | `/*/*/RunnerCapabilitiesFailureTests/*` | V-5 | all listed, 0 failed | 8 | 6 |
| CP-3 | S2b | CP-2 | snapshot | `/*/*/DesktopCapabilitiesSnapshotTests/*` | V-1, V-2, V-3, V-4, V-6 | all listed, 0 failed | 7 | 2 |
| CP-4 | S2b | CP-2 | directory-regress | `/*/*/PhoneHomeDirectoryTests/*` | V-7 | all listed, 0 failed | 7 | 2 |
| CP-5 | S2b | CP-2 | catalogue-regress | `/*/*/RunnerCatalogueTests/*` | V-7 | all listed, 0 failed | 4 | 4 |

CP-1 is the single red witness. CP-3 is the narrow green that turns CP-1 green. CP-2, CP-4 and
CP-5 are guard rows. CP-2 checks classification. CP-4 and CP-5 run the two existing suites that
build the real `PhoneHomeRunnerDirectory`, and `RunnerCatalogueTests` drives its desktop branch
through `GET /api/session-runners`. `TaskPlatformPlacementTests` uses its own stub directory, so it
cannot see this change and is not listed. CP-2 `Min` 8 is one `[Arguments]` case per D-3 row.

### Unlisted

None planned. The full `Antiphon.Tests` suite is not part of this card's profile.

## Verification design

| V | Claim | Test (class `DesktopCapabilitiesSnapshotTests` unless noted) | Goes red when |
|---|---|---|---|
| V-1 | One ambiguous failure after a success keeps the last good answer | `A_single_failed_probe_after_a_good_one_keeps_windows_and_dispatch_eligible`: probe 1 returns windows + `RunnerPlatformWire.Feature`. Advance 60 s. Probe 2 fails `deadline`. Assert `Platform == windows`, `DispatchEligible`, `Available`, `!Stale`, `Capabilities` equal to probe 1, and `PlatformObservedAt` == the probe 1 time. | The snapshot is not served (today's code: red) |
| V-2 | The grace period is bounded | `Failures_past_the_grace_window_go_stale`: success at t0. Failures (`limiter`) at t0+60, t0+120, t0+179 all serve last-good. A failure at t0+181 returns `Platform == null`, `!DispatchEligible`, `Stale`. Also `Zero_grace_restores_todays_stale_on_first_failure`. | The grace check is missing or off by one, or 0 is not honoured |
| V-3 | Definite absence goes stale at once and stays stale | `Refused_probe_goes_stale_immediately_and_circuit_does_not_revive_it`: success at t0. `refused` at t0+5 returns stale. `circuit` at t0+10 is still stale. A success at t0+20 is fresh again. | `Absent` is ignored, or not cleared by a success |
| V-4 | A real hang uses the directory's 2 s deadline | `A_hung_probe_is_classified_as_deadline`: the fake hangs until cancelled. Advance `FakeTimeProvider` 2 s. Assert the snapshot is served and the logged reason is `deadline`. | Deadline handling breaks |
| V-5 | Classification table | `RunnerCapabilitiesFailureTests.Classify_maps_exception_to_reason` (`[Arguments]`, one row per D-3 reason). The exceptions are built directly: `new HttpRequestException("x", new BrokenCircuitException())`, `new HttpRequestException("x", new RateLimiterRejectedException())`, `new HttpRequestException(HttpRequestError.ConnectionError, "x")`, `new HttpRequestException("x", null, HttpStatusCode.ServiceUnavailable)`, `new TaskCanceledException("x", new TimeoutRejectedException())`, `JsonException`, `NotSupportedException`, a plain `HttpRequestException`. | A reason or its definite flag changes |
| V-6 | The log is rate-limited and states the reason | `Failure_log_is_rate_limited_and_names_the_reason`: use a capturing `ILogger<PhoneHomeRunnerDirectory>`. Five `deadline` failures 10 s apart give one Warning. A `limiter` failure gives a second Warning (reason change). A failure 61 s after the last log gives a third. The change to stale gives a Warning with `serving stale`. A success gives one Information "recovered after". | The log is missing, not rate-limited, or has no reason |
| V-7 | No regression in the existing directory and catalogue fixtures | CP-4 and CP-5 (existing suites) | The default interface body or descriptor shape changes behaviour for fakes |

Mutation targets for the later Mutation stage:
- the `<=` in the grace comparison;
- `Absent` being set on a definite failure;
- `Absent` being cleared on a success;
- `PlatformObservedAt = ObservedAt` versus now;
- the log rate-limit comparison.

## Rollback

Set `PhoneHomeRunner:DesktopCapabilitiesGraceSeconds = 0`. The desktop branch then returns
today's descriptor on every failure. The log line stays.
