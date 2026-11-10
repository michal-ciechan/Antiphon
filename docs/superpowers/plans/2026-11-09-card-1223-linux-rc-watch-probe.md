# CARD-1223 Linux RC-watch probe: supported omission

Plan for task `559e69c7` on `feat/card-task-559e69c7`. Base `ef7afd44927206f6c03547670a5add84130bdce9`.
Recommendation: record a supported omission. Off Windows, `WindowsRcBridgeProbe.Probe` returns an unmeasured connection count and `SessionHealthService` does not treat that miss as zero connections. Do not add a `/proc` counter and do not refuse startup.

`GET /api/runner-defaults` and `GET /api/session-runners` were not readable from this runner (`127.0.0.1:17202` connection refused). This plan names no fleet host. Checkpoints omit `-Runner` and `-Platform`. The lane is the host that runs the checkpoint tool. The Windows-native public probe is owed and skips off Windows.

This plan adds no session that waits for input. The CARD-1083 release checklist does not apply.

The omission sentence, copied verbatim into the probe log and the three docs, is: `Claude RC connection count is a supported omission off Windows. A missing count is not zero connections.`

## Decisions

- **D-1.** The Linux contract is a supported omission. CARD-1221 left Claude RC watch "Supported only on the desktop control plane" and assigned the silent zero to this card. The connection count is not visible to the server process that owns `IRcBridgeProbe`. A `/proc` port in that process would count the wrong network namespace.
- **D-2.** `RcProbeResult` gains `bool ConnectionCountMeasured = true` as the last positional parameter. Existing three-argument constructors stay measured. `false` means `BridgeConnections` is not a count. The omission value is `RcProbeResult(Armed: false, BridgeConnections: 0, StateFileFound: false, ConnectionCountMeasured: false)`. The stored `0` is not a measurement.
- **D-3.** `WindowsRcBridgeProbe.Probe(int)` calls `Probe(int, bool)` with `OperatingSystem.IsWindows()`. The bool false returns the omission value and does not read a session file and does not call `GetExtendedTcpTable`. The bool true is today's body, including the catch that returns `0`. Registration in `Program.cs` stays `WindowsRcBridgeProbe`.
- **D-4.** `SessionHealthService.WatchRcAsync` checks `ConnectionCountMeasured` immediately before the `BridgeConnections > 0` branch, after the idle return. False logs the omission sentence once per session (`Entry.OmissionWarned`), then returns false. It does not increment or clear `ConsecutiveZeroConnProbes`, and it does not record `RcDegraded`. Measured zero is unchanged.
- **D-5.** The omission result is classifier `Unknown` because `StateFileFound` is false. `RemoteControlBridgeClassifier`, launch preflight, and resume-arm stay as they are. Unknown already withholds automatic `/remote-control`. This card does not add a withhold and does not add an incident.
- **D-6.** No startup refusal. `RcWatch.Enabled` stays default true (`RcWatchSettings.cs:274`). `appsettings.json` does not set it. Compose does not set it. `SessionHealthHostedService` still ticks. The card boundary forbids reporting this as a server-startup failure.
- **D-7.** No migration, no route, no public HTTP DTO, no compose change, no change to `Program.cs`. `tests/Antiphon.Tests/Layering/layers.txt:18` already maps `server/Infrastructure/Supervision/**` to Application. Do not edit `layers.txt`.

## Ground truth

| What the card or the 2026-11-07 decision says | What this checkout does |
|---|---|
| `Program.cs:599` registers `WindowsRcBridgeProbe` (card evidence, commit `d7648f8e9`). `docker-stack.md` cites `Program.cs:655-656`. | The registration is `server/Program.cs:662-663`. It is unconditional. |
| `WindowsRcBridgeProbe.cs:57` calls `GetExtendedTcpTable` and `:90` returns zero on failure. | `CountAnthropicConnections` is `WindowsRcBridgeProbe.cs:52-95`. The P/Invoke is `iphlpapi.dll` at `:111-118`. A non-zero native status returns 0 at `:61-62`. Any exception returns 0 at `:90-94`. |
| `SessionHealthService.cs:197` consumes the probe. | `WatchRcAsync` calls `Probe` at `:197`. A throw is logged at Debug and the tick returns false (`:199-202`). A returned `BridgeConnections` of 0 on an idle session that is not `Unarmed` increments `ConsecutiveZeroConnProbes` and, at the threshold, records `AgentIncidentKind.RcDegraded` (`:236-268`). The incident text says the child reports armed. The code does not check `Armed` on that path. |
| `StateFileFound` false means the result is untrustworthy (`IRcBridgeProbe.cs:25-26`). | `RemoteControlBridgeClassifier.Classify` maps that to `Unknown` (`RemoteControlBridgeClassifier.cs:9-14`). `WatchRcAsync` still applies the zero-connection streak to `Unknown`. `An_unreadable_session_file_is_not_treated_as_unarmed` keeps `Connections = 2` and a moving sequence (`SessionHealthTests.cs:313-339`), so it never enters that streak. |
| A Linux server should distinguish "no connections" from "probe unavailable". | The only production implementation returns `RcProbeResult(armed, connections, found)` with an `int` count (`WindowsRcBridgeProbe.cs:23`). There is no unavailable flag. |
| The watch is desktop-only. | `docs/docker-stack.md:1060` says that, then says a Linux failure returns 0 and "A zero is not evidence." `:1204` lists "RC watch returning 0 on Linux and being read as no Claude connections" as a risk the contract already refuses. |

The 2026-11-07 investigation and `docs/investigations/2026-10-13-card-0038-linux-docker-readiness.md` stay historical. Do not edit them. The living text is `docs/docker-stack.md`, `docs/agent-kinds.md`, and `docs/session-runtime-invariants.md`.

`tests/impacted-classes.json` has no glob for `IRcBridgeProbe.cs`, `WindowsRcBridgeProbe.cs`, `SessionHealthService.cs`, `SessionHealthTests.cs`, or those three docs. The contracts row matches `server/**/Dtos/**`, not `server/Application/Interfaces`. CARD-1782 selects nothing from this diff. No migration, public HTTP DTO, route, operation label, remote script, or statement budget changes. `RcProbeResult` is an in-process record. The new parameter has a default, so every current `new RcProbeResult(...)` call stays measured.

## Intended behaviour

### Before

1. `Program.cs:662-663` registers `WindowsRcBridgeProbe` on every OS.
2. `Probe(int)` reads `%USERPROFILE%\.claude\sessions\<pid>.json` (`WindowsRcBridgeProbe.cs:16-17`, `:26-44`) and then counts established IPv4 connections to `160.79.0.0/16:443` owned by that pid (`:52-84`).
3. The pid is `live.Pid` from `ISessionRunnerClient.ListAsync` (`SessionHealthService.cs:108-131`), the runner's child pid.
4. On Linux the session file is absent from the server user's profile, so the result is `Armed false`, `StateFileFound false`. `GetExtendedTcpTable` cannot load `iphlpapi.dll`. The catch returns `0`. The result is a measured zero.
5. Classifier: `Unknown`. Health does not take the unarmed path. Once the session is idle, the `0` increments `ConsecutiveZeroConnProbes`. After `RcWatch.ConsecutiveFailedProbesBeforeAction` (default 5) the tick records `RcDegraded` with text that claims the child reported armed.
6. Launch preflight already treats `Unknown` as withhold (`AgentSessionService.cs:2877-2885`). A probe throw is also `Unknown` (`RemoteControlRecoveryService.cs:97-109`). Today's Linux result is already `Unknown` because the file is missing. The silent part is the health streak, not the launch withhold.

### After

1. Registration is the same line. `Probe(int)` passes `OperatingSystem.IsWindows()` into `Probe(int, bool)`.
2. Bool false returns the omission value and does not touch the disk or `iphlpapi.dll`. Bool true is the current Windows body, and the catch still returns a measured `0`.
3. Health, on an idle session that was not classified `Unarmed`, sees `ConnectionCountMeasured` false, logs the omission sentence once for that session id, and returns. No `RcDegraded`. No change to the zero streak.
4. Classifier stays `Unknown`. Launch preflight still withholds automatic `/remote-control` for that `Unknown`. That withhold is the same outcome Linux already has from a missing server-side session file.
5. A measured zero (`ConnectionCountMeasured` true and `BridgeConnections` 0), including a Windows hiccup and `C514_Armed_zero_connections_never_reserves_arm`, still records `RcDegraded` and still does not arm or kill.

### Unchanged

`GetExtendedTcpTable`, `MibTcpRowOwnerPid`, the Windows catch, `Program.cs` registration, `RemoteControlBridgeClassifier`, `RemoteControlRecoveryService.ProbeBridge`, `AgentSessionService` resume and preflight, `RcWatchSettings`, `appsettings.json`, every compose file, `SessionHealthHostedService`, and the unarmed-busy repair path.

## Design

### What the runner can read, and why the server cannot use it

The production server and its direct runner are two containers in `docker-compose.server2-server.yml`. The server calls `http://server-session-runner:8080` (`:53`). The runner sets `CLAUDE_CONFIG_DIR=/state/claude` (`:149`) and mounts `runner-state` at `/state` (`:150-151`). The server mounts that volume read-only at `/runner-state` (`:111-114`). Neither service sets `pid:` or `network_mode`. Each has its own pid namespace and its own network namespace.

Inside the runner container the child shares the runner's pid and network namespace, so the runner process could read `$CLAUDE_CONFIG_DIR` session files and join `/proc/<pid>/fd` socket inodes to `/proc/net/tcp` and `/proc/net/tcp6`. That is a real local signal. `IRcBridgeProbe` does not run in the runner. It runs in the server. The server's `/proc/net/tcp` does not list the runner child's sockets, and `live.Pid` is not a process in the server's pid namespace. The probe's session path is the server user's `%USERPROFILE%\.claude\sessions`, not `CLAUDE_CONFIG_DIR` and not `/runner-state/claude`.

Phone-home sessions are a second project (`docker-compose.server2-runner.yml`). `PhoneHomeRunner__Runners__server2__ChildClaudeHome` (`docker-compose.server2-server.yml:66`) is a path on that runner. Those sockets and session files are not on the server's disk.

A server-side `/proc` count would report the server container's own sockets and would miss the agent. A zero from that table is another silent zero. The shared volume does not carry TCP state, so reading an armed flag off `/runner-state` still leaves the connection count as a guess, and it misses phone-home.

Rejected a runner RPC that returns the count. The runner can see the sockets. Shipping that is a new route and a phone-home contract, and it would claim Linux support that CARD-1221 left on the desktop. This card only makes the server probe honest.

Rejected startup refusal when `RcWatch.Enabled` is true. That is the CARD-1222 shape. This card's boundary says the probe is not a server-startup failure. The flag defaults true and the hosted service also flushes stranded queues. Forcing it false is a different behaviour change.

Rejected throwing `PlatformNotSupportedException`. `WatchRcAsync` already ignores a throw, but the result would still not be an explicit unavailable value, and the log would stay Debug, which is the hiccup path. The bool is the value health can branch on. The Windows catch stays a measured zero so a transient `GetExtendedTcpTable` failure still uses the consecutive threshold.

Rejected a negative `BridgeConnections` sentinel. Health treats every value that is not `> 0` as the zero streak. A sentinel would still be consumed unless every caller changed. The bool leaves `> 0` meaning a measured positive count.

Rejected treating `StateFileFound` false as "do not count". On Windows a missing file plus a measured zero still reaches the streak, because `Unknown` is not `Unarmed`. Changing that changes Windows.

Rejected editing the classifier. `Unknown` already withholds writes. The defect is the later streak, which does not consult the classifier.

### Omission result and health

`Probe(int, bool)` is `internal`. `Antiphon.Server.csproj` already has `InternalsVisibleTo` for `Antiphon.Tests`. Tests call it directly. Production calls only `Probe(int)`.

`FakeRcProbe` gains `ConnectionCountMeasured` defaulting to true and passes it into the record. Existing health tests leave it true.

`Entry.OmissionWarned` is not cleared on generation change. One warning per session id per process. A later measured sample still uses the existing healthy or zero paths.

## Tests

New unit class `tests/Antiphon.Tests/Infrastructure/RcBridgeProbePlatformTests.cs`, `[Category("Unit")]`. It reads docs and `SessionHealthService.cs` through `DockerStackDocuments.Read`. Linux-only and Windows-only methods throw `TUnit.Core.Exceptions.SkipTestException` after checking `OperatingSystem.IsWindows()`.

S1 commits these against the still-measured seam. `Probe(int, bool)` exists and ignores the bool, so it calls today's body. `RcProbeResult` has the new parameter defaulting to true. `FakeRcProbe.ConnectionCountMeasured` exists and is not forwarded. Run the class and the one new health method once under the build slot, record the failures, then fix in S2. Those runs are unlisted: a checkpoint row that expects failures is exit 1.

- `Linux_probe_does_not_report_a_measured_zero`. Skip on Windows. On Linux, `OperatingSystem.IsWindows().ShouldBeFalse()` then `new WindowsRcBridgeProbe().Probe(424242)`. Assert `ConnectionCountMeasured` is false, `Armed` is false, and `StateFileFound` is false. Red on S1 because the public method still returns the default true and a caught `0`.
- `Forced_unsupported_platform_is_unmeasured`. Runs on both OS. `const bool platformCanCount = false`, then `platformCanCount.ShouldBeFalse()`, then `Probe(424242, platformCanCount)`. Assert `ConnectionCountMeasured` is false. Red on S1 because the overload ignores the bool.
- `Windows_probe_reports_a_measured_count`. Skip on Linux. On Windows, `OperatingSystem.IsWindows().ShouldBeTrue()` then public `Probe(424242)`. Assert `ConnectionCountMeasured` is true. Do not assert a positive count. A pid with no Anthropic sockets is a measured zero and that is Windows behaviour. On S1 this skips on Linux.
- `Docker_stack_records_the_supported_omission`. `docs/docker-stack.md` contains the omission sentence. It does not contain `A zero is not evidence.` or `RC watch returning 0 on Linux and being read as "no Claude connections."`. Red until S2.
- `Agent_kinds_records_the_linux_omission`. `docs/agent-kinds.md` contains the omission sentence. Red until S2.
- `Session_runtime_records_the_linux_omission`. `docs/session-runtime-invariants.md` contains the omission sentence. Red until S2.
- `Health_source_logs_the_omission_sentence`. `server/Application/Services/SessionHealthService.cs` contains the omission sentence as one contiguous literal. Red until S2.

S1 also adds `SessionHealthTests.Unavailable_connection_count_does_not_record_rc_degraded`. Same harness as `C514_Armed_zero_connections_never_reserves_arm` (`ConsecutiveFailedProbesBeforeAction` 2, idle). Set `Armed` false, `StateFileFound` false, `Connections` 0, `ConnectionCountMeasured` false. Two ticks. Assert both ticks return 0, `AutomaticArms` is empty, `KilledSessions` is empty, and no `RcDegraded` row exists. Red on S1 because the fake does not forward the flag, so health still sees a measured zero. On Linux the S1 unit class is 6 failed and 1 skipped. The health method is 1 failed. Count them from the TRX. Do not weaken an assertion to make S1 green.

S2 makes `Probe(int, bool)` honour the bool, forwards the fake flag, teaches health the branch, and writes the sentence into the three docs and the log. No new test methods in S2.

`C514_Armed_zero_connections_never_reserves_arm` stays the measured-zero control: armed, file found, connections 0, flag left at its default true, and `RcDegraded` is still recorded. Do not edit its assertions.

## Migrations

None. No schema change, no `server/Migrations` file, no `Down`.

## Slices

### S1 — red witnesses (about 70 minutes)

- `server/Application/Interfaces/IRcBridgeProbe.cs`. Add `bool ConnectionCountMeasured = true` and a one-line summary that false means the int is not a count.
- `server/Infrastructure/Supervision/WindowsRcBridgeProbe.cs`. Add `internal Probe(int, bool)` that ignores the bool and calls the current body. Public `Probe(int)` still calls the current body only.
- `tests/Antiphon.Tests/Application/SessionHealthTests.cs`. Add the fake property, leave `Probe` constructing the three-argument record, and add `Unavailable_connection_count_does_not_record_rc_degraded`.
- `tests/Antiphon.Tests/Infrastructure/RcBridgeProbePlatformTests.cs` with the seven methods above.

Commit the red tests before the omission return, the health branch, or the doc edits. Push that commit. The unlisted runs, each under `pwsh -NoProfile -File scripts/build-slot.ps1`:

- Label `c1223-s1-probe`. `dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/*/RcBridgeProbePlatformTests/*"`. Expect 6 failed, 1 skipped, on Linux.
- Label `c1223-s1-health`. Same project, filter `/*/*/SessionHealthTests/Unavailable_connection_count_does_not_record_rc_degraded`. Expect 1 failed.

One build at a time. The second run may reuse the first build only when it passes no `-o`, `--output`, or `OutputPath` and the tree is unchanged. Record executed, failed, and skipped from the TRX.

### S2 — omission (about 90 minutes)

- `WindowsRcBridgeProbe.Probe(int)` passes `OperatingSystem.IsWindows()`. `Probe(int, bool)` returns the omission value when the bool is false, before `ReadArmedState` and before `CountAnthropicConnections`. The true branch is the existing body and the existing three-argument return.
- `SessionHealthService`. Add `Entry.OmissionWarned`. Insert the unmeasured branch immediately before `if (probe.BridgeConnections > 0)`. Log the omission sentence once with `LogWarning`, including the agent name and session id as structured fields around that contiguous sentence. Return false.
- `FakeRcProbe.Probe` passes `ConnectionCountMeasured`.
- `docs/docker-stack.md`. In the Claude RC watch row, keep the disposition "Supported only on the desktop control plane". Replace the blocker text that describes a silent zero with the omission sentence, and cite `WindowsRcBridgeProbe.Probe` rather than a stale `Program.cs:655-656` failure claim. Replace the risk bullet at the current `:1204` with a bullet that contains the omission sentence. Leave the 2026-11-07 investigation unchanged.
- `docs/agent-kinds.md`. Add the omission sentence to the Claude remote-control bullet (`:344-348`).
- `docs/session-runtime-invariants.md`. Add the omission sentence to Gotcha #57 (`:1471`).

Commit S2 and push. Then run CP-1 and CP-2.

## Performance budget

Both rows are provisional. This class has no measured p95 (CARD-1404 / CARD-1720). `scripts/lib/nightly-class-costs.json` records `SessionHealthTests` at 16 serial seconds for 12 nodes and says that figure is not a p95. The first green checkpoint run is the collection. Do not repeat a green row to manufacture a p95. If either row's charged execution exceeds 30 minutes, stop and hand the run id, SHA, host, and phase times to a Debug task. Do not widen an assertion or a timeout to absorb that.

CP-1 wall is dominated by one cold `tests/Antiphon.Tests` build plus seven unit methods, one of which skips on Linux. The bodies construct a probe or read a file. No web host, database, migration, or Docker. Provisional estimate 10 minutes. CP-2 reuses that build. Its selection is two integration methods on the existing `SessionHealthTests` harness. `TestDbFixture` is the assembly's shared store, started once per run, not once per test. The methods clean up their own agent rows. Provisional estimate 4 minutes, above the 16-second class measurement, because a cold fixture start is allowed for and is not a measured p95. If a receipt shows a container start, record that phase. Do not report an unseen phase as zero.

Importer row deadlines are `max(15, 3 * EstimatedMinutes)`: 30 minutes for CP-1 and 15 minutes for CP-2. That 3x floor is the CARD-1720 importer exception, not an R5 measurement. Expected body time after the build is seconds for CP-1 and well under a minute for CP-2 once the fixture is up. About 2x a future measured p95 replaces these provisional deadlines only after that p95 exists. Admission is the build-slot gate, separate from charged execution, default 45 minutes. Derived total allowance is `max(30, 2*14+10)` = 38 minutes. No `totalTimeoutMinutes` override.

Phase times come from the checkpoint report (admission, build, host, tests, teardown). File-read and fixture-startup subphases stay unknown until a receipt splits them. Evidence stays under `.antiphon/checkpoints/<run>/`.

## Risks and operator steps

No operator step. Do not deploy, restart, or change `RcWatch`. Desktop Windows production keeps a measured count, including a measured zero after a native hiccup. Nothing in this card runs on the desktop.

A Linux server that already records `RcDegraded` from the silent zero stops adding new rows of that kind for an unmeasured probe. It does not delete old incidents. The one warning per session is the loud signal. It is not an incident and it is not a startup error.

The Windows public method is owed, not run here, and a Linux skip is not a pass: execute `Windows_probe_reports_a_measured_count` on Windows and confirm public `Probe(int)` returns `ConnectionCountMeasured` true. Do not add a live `GetExtendedTcpTable` success assertion to the Linux rows.

## OS witness obligations

Code writes version-1 JSON under the ignored evidence root and runs:

`pwsh -NoProfile -File scripts/check-os-witnesses.ps1 -BaseRef ef7afd44927206f6c03547670a5add84130bdce9 -HeadRef <full S2 sha> -EvidencePath <ignored-json> -Plan docs/superpowers/plans/2026-11-09-card-1223-linux-rc-watch-probe.md`

Inventory every changed non-document path. The branch anchor is the literal `OperatingSystem.IsWindows()` passed by public `Probe(int)`. Inspected paths include `server/Infrastructure/Supervision/WindowsRcBridgeProbe.cs` (`os-gated`), `server/Application/Interfaces/IRcBridgeProbe.cs` (`portable`, the new flag has no OS read), `server/Application/Services/SessionHealthService.cs` (helper: caller of `Probe`, portable bool consumer), and the two test files (`test-only`). Docs are documentation.

Witnesses:

- Linux public probe. `productionType` `WindowsRcBridgeProbe`, `factory` `Probe`, `testCase` `Linux_probe_does_not_report_a_measured_zero`, `targetOS` Linux, `executionMode` native, `checkpointId` CP-1. `setupAssertion` is `OperatingSystem.IsWindows().ShouldBeFalse()`. `nativeObligation` recorded only with the CP-1 receipt; until then pending is honest.
- Forced false. `factory` `Probe(int, bool)`, `testCase` `Forced_unsupported_platform_is_unmeasured`, `executionMode` portable-forced, `targetOS` Linux, `nativeObligation` `not-required` because the bool is the input and the public method has its own witness. `setupAssertion` is `platformCanCount.ShouldBeFalse()`.
- Windows public probe. `testCase` `Windows_probe_reports_a_measured_count`, `executionMode` native, `targetOS` Windows, `nativeObligation` `pending`, command is the CP-1 filter on a Windows host. `setupAssertion` is `OperatingSystem.IsWindows().ShouldBeTrue()`. A Linux skip stays pending.

Input `kind` is `other`, not optional-config. The discriminator is the OS bool, not a configuration key. `RcWatch:Enabled` is not read by the probe.

`restartImpact.required` is false. None of the named restart-script paths change. `Program.cs` is unchanged. The host still starts with `RcWatch.Enabled` true. `additionalPaths` includes `server/Infrastructure/Supervision/WindowsRcBridgeProbe.cs` because the watch calls it after start. This card restarts nothing. If the checker disagrees, keep the inventory and record the obligation it asks for. Do not delete a candidate to make exit 0.

Also run `scripts/check-evidence-diff.ps1` over `ef7afd44927206f6c03547670a5add84130bdce9..HEAD` before the Code report. Both scripts are unlisted because their SHAs are the S2 tip. Say so in the report.

## Verification design

V-1: `RcBridgeProbePlatformTests.Linux_probe_does_not_report_a_measured_zero` asserts the public probe on Linux returns `ConnectionCountMeasured` false, `Armed` false, and `StateFileFound` false.

V-2: `RcBridgeProbePlatformTests.Forced_unsupported_platform_is_unmeasured` asserts `Probe(int, false)` returns `ConnectionCountMeasured` false.

V-3: `RcBridgeProbePlatformTests.Windows_probe_reports_a_measured_count` asserts the public probe on Windows returns `ConnectionCountMeasured` true. Off Windows it skips. The skip is pending native work.

V-4: `RcBridgeProbePlatformTests.Docker_stack_records_the_supported_omission` asserts `docs/docker-stack.md` contains the omission sentence and does not contain the two stale sentences named in S1.

V-5: `RcBridgeProbePlatformTests.Agent_kinds_records_the_linux_omission` asserts `docs/agent-kinds.md` contains the omission sentence.

V-6: `RcBridgeProbePlatformTests.Session_runtime_records_the_linux_omission` asserts `docs/session-runtime-invariants.md` contains the omission sentence.

V-7: `RcBridgeProbePlatformTests.Health_source_logs_the_omission_sentence` asserts `SessionHealthService.cs` contains the omission sentence as one contiguous literal.

V-8: `SessionHealthTests.Unavailable_connection_count_does_not_record_rc_degraded` asserts two idle ticks of an unmeasured zero record no `RcDegraded` and reserve no automatic arm.

R-1: `SessionHealthTests.C514_Armed_zero_connections_never_reserves_arm` asserts a measured zero still records `RcDegraded` and does not arm or kill.

| PC | Mutation | Filter | Detects |
|---|---|---|---|
| PC-1 | delete the non-Windows early return so the public probe falls through to the measured catch | `/*/*/RcBridgeProbePlatformTests/Linux_probe_does_not_report_a_measured_zero` | `RcBridgeProbePlatformTests.Linux_probe_does_not_report_a_measured_zero` |
| PC-2 | ignore the bool and always run the Windows body | `/*/*/RcBridgeProbePlatformTests/Forced_unsupported_platform_is_unmeasured` | `RcBridgeProbePlatformTests.Forced_unsupported_platform_is_unmeasured` |
| PC-3 | return unmeasured from the Windows body | `/*/*/RcBridgeProbePlatformTests/Windows_probe_reports_a_measured_count` | `RcBridgeProbePlatformTests.Windows_probe_reports_a_measured_count` |
| PC-4 | restore the stale silent-zero sentence in docker-stack | `/*/*/RcBridgeProbePlatformTests/Docker_stack_records_the_supported_omission` | `RcBridgeProbePlatformTests.Docker_stack_records_the_supported_omission` |
| PC-5 | remove the omission sentence from agent-kinds | `/*/*/RcBridgeProbePlatformTests/Agent_kinds_records_the_linux_omission` | `RcBridgeProbePlatformTests.Agent_kinds_records_the_linux_omission` |
| PC-6 | remove the omission sentence from session-runtime | `/*/*/RcBridgeProbePlatformTests/Session_runtime_records_the_linux_omission` | `RcBridgeProbePlatformTests.Session_runtime_records_the_linux_omission` |
| PC-7 | log a different sentence | `/*/*/RcBridgeProbePlatformTests/Health_source_logs_the_omission_sentence` | `RcBridgeProbePlatformTests.Health_source_logs_the_omission_sentence` |
| PC-8 | drop the unmeasured branch so health still records RcDegraded | `/*/*/SessionHealthTests/Unavailable_connection_count_does_not_record_rc_degraded` | `SessionHealthTests.Unavailable_connection_count_does_not_record_rc_degraded` |
| PC-9 | treat a measured zero as healthy | `/*/*/SessionHealthTests/C514_Armed_zero_connections_never_reserves_arm` | `SessionHealthTests.C514_Armed_zero_connections_never_reserves_arm` |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | tests/Antiphon.Tests -> bin-c1223/ | omission | `/*/*/RcBridgeProbePlatformTests/*` | V-1..V-7 | all listed, 0 failed; Linux skips the Windows method; Windows skips the Linux method | 6 | 10 |
| CP-2 | S2 | CP-1 | health | `/*/*/SessionHealthTests/(Unavailable_connection_count_does_not_record_rc_degraded*)\|(C514_Armed_zero_connections_never_reserves_arm*)` | V-8, R-1 | all listed, 0 failed | 2 | 4 |
