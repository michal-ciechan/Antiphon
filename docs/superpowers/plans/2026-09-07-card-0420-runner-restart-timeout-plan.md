# CARD-0420: Give runner restarts an honest wait budget and outcome

Plan date: 2026-09-07. Inspected checkout: `2843bcc1` on `master`.

Raise the restart script's default health wait to 180 seconds, distinguish an expired observation window from a failed restart action, and allow waiting again without another kill. Add lightweight startup timing and phase diagnostics while preserving the complete startup-adoption sweep before HTTP starts. This is a Plan artifact for separate TestDesign; no runtime change or live restart was performed.

## Evidence and ground truth

The full [measured investigation](../../investigations/2026-09-06-card-0420-runner-restart-timeout.md), committed as `2843bcc1`, is the evidence baseline. Its timing table covers two restarts on 2026-09-06, not a sampled latency distribution.

| Card assumption or question | Observed implementation / evidence | Consequence |
|---|---|---|
| A normal restart takes a few seconds. | `scripts/run-daemon.ps1` sleeps three seconds between service runs, then invokes an incremental build before launching the built executable. The incident took about 99.3 seconds from old-process exit to listening. | Replace the `~3s` restart promise with the actual sequence: supervisor delay, build, adoption, HTTP readiness. |
| Sixty seconds is enough for build and adoption. | Build-begin to listening was about 36.5 seconds in the comparison and 90.3 seconds in the incident. Build wall intervals were about 17 and 52 seconds; launch-request to listening about 19.5 and 38.3 seconds. | The default is demonstrably insufficient for the incident. Choose explicit headroom, without claiming a p95 or maximum. |
| Adoption might run after HTTP becomes available. | `src/Antiphon.SessionRunner/Program.cs` awaits `AdoptOrphanedHostsAsync` before starting HTTP. `SessionRunnerRuntime` restores claims, processes Herdr sidecars, then processes pty-host manifests serially. | Keep the barrier and ordering. No background or parallel adoption in this card. |
| Serving early health is a harmless optimization. | `server/Application/Services/SessionReconciliationService.cs` skips the session pass when the runner is unreachable; a responding runner's missing session is marked Failed. | A healthy response cannot precede the initial adoption sweep. Keep `/sessions` unavailable during that sweep as well. |
| Session count predicts a suitable deadline. | There were 42 versus 41 adopted pty-host sessions, but the interval from restored claims to adoption summary was 3.446 versus 23.225 seconds. That interval includes more than pipe connection work. | No session-count multiplier or claimed per-session throughput. |
| Unchanged runner source means its build can be skipped. | Both the Scheduled Task and AppHost launch paths use `run-daemon.ps1 -BuildProjectDir`. Referenced projects, props/targets, package/content inputs, and the repository SHA stamp also affect outputs. | Preserve incremental build on each launch. No new freshness cache or no-build option. |
| Timeout means restart failed. | `restart-session-runner.ps1` starts its deadline after kill/relaunch selection, exits 1 on expiry, and leaves supervision running. The current polling operations can overrun that deadline. | Separate action failure from wait expiry; report elapsed time and the last supported observation. |
| Existing logs can identify the slow phase precisely. | They do not record first successful health observations, exact claim-restore duration, or build-target costs. Runtime resolution is included in a proxy interval. | Add coarse phase measurements now. Detailed build-target profiling is deferred because it is unnecessary to correct the script contract. |

Sources inspected in addition to the investigation: the three runner scripts; `Program.cs`, `SessionRunnerRuntime.cs`, `RunnerBuildIdentity.cs`; the reconciliation service; `DaemonLogRotationTests`, `PtyHostDeploymentGuardTests`, and the existing adoption/reconciliation test surfaces. The relevant owners are [bootstrap](../../bootstrap.md), [session runtime invariants](../../session-runtime-invariants.md), [testing/build operations](../../testing-and-build.md), and [project conventions](../../project-context.md).

## Decisions

### D-1: Correct the operational contract; make no startup speed claim

Choose the card's timeout/reporting acceptance arm. Preserve current incremental builds, serial adoption, transcript-claim restoration before adoption, and the existing Herdr Pending/exited/live decisions. Keep launching the built executable directly: replacing it with `dotnet run` would reintroduce the kill-on-close job that destroys detached pty-hosts.

The readiness assertion means the **initial sweep has completed and every processed surviving session has its existing representation**. It does not require every session to be Running: current terminal registrations and Herdr Pending are valid sweep results. It also does not mean all transcript tailers have finished asynchronous catch-up. Do not strengthen or weaken those existing contracts through diagnostics.

Do not add an adoption cancellation deadline, truncate a sweep when the script stops waiting, change per-host pipe timeouts, or change reconciliation. The script's timer never reaches the runner's cancellation token or session lifecycle.

### D-2: Use a bounded 180-second observation window

Change `-TimeoutSec` from 60 to **180**, retaining a positive integer override. This gives roughly 80 seconds of margin beyond the measured old-exit-to-listen incident, before allowing for the unmeasured health-observation delay. It is a conservative operational default based on two observations, **not** a percentile, guaranteed startup bound, or proof that every future restart fits.

Keep the parameter's existing meaning: the health-wait budget begins after kill/relaunch selection finishes. Record command entry, relaunch-request completion, and wait start separately so total command time and wait time cannot be confused. `-WaitOnly` starts its own observation budget immediately after argument validation and read-only setup.

Use a monotonic stopwatch for the budget and elapsed durations; use UTC round-trip timestamps with milliseconds for correlation. Probe `/health` immediately, then at up to two-second intervals. Bound each HTTP request by the lesser of five seconds and the remaining budget, and clamp sleeps to that remainder. Avoid the current repeated TCP-enumeration gate: connection refusal is already a valid HTTP probe result. Do not start another probe once the budget is exhausted, extend/reset the budget on phase progress, or accept a response completed after the budget as in-budget success. Cancellation must stop only the probe, with a small documented scheduling tolerance in its test.

Success still requires a real HTTP 200 from the expected runner listener; a build-success event, phase marker, live PID, or open port is insufficient. Retain listener ownership verification when reporting the service PID, and do not describe a foreign listener as the runner. Use existing process identity (PID plus start time/path) and, where needed, the existing `/capabilities` build identity; no health body or public DTO change is needed.

### D-3: Make the final result machine-readable and truthful

Emit a stable final line beginning `RUNNER RESTART RESULT:`, with a compact JSON object carrying `outcome`, `mode` (`restart` or `wait-only`), `waitElapsedMs`, `commandElapsedMs`, `timeoutSec`, `observedAtUtc`, `lastObservedPhase`, and known process/attempt identities. Optional fields contain `firstHealth200ObservedAtUtc`, `lastProbeResult`, and the latest current-attempt error code. Missing evidence is null/unknown, never invented. Keep diagnostic text before this line and use the same outcome vocabulary in human-readable text.

| Exit | Outcome | Meaning and message |
|---|---|---|
| 0 | `healthy` | A successful health response was observed within the budget. Give the first 200 observation time, elapsed wait, and verified runner identity. |
| 2 | `wait-expired` | No qualifying 200 was observed within the budget. Say **"Health wait expired; runner readiness is unconfirmed. Startup may still be in progress."** Give the last observed phase and its age, latest probe result, and whether the identified supervisor/runner is still alive. This is not a declaration that startup succeeded or failed. |
| 1 | `action-failed` | A concrete restart operation failed, for example `Start-ScheduledTask` threw or an owned service could not be stopped. Report the operation and actual error, without claiming that no other launch can be running. Invalid argument combinations also fail before mutation. |

Elapsed time alone, absent health, a missing PID file, or an unreadable log can never yield `action-failed`. A build failure followed by the supervisor's existing retry is **retrying after a failed attempt**: continue waiting, retain that error in diagnostics, and return `wait-expired` if the budget ends. Likewise show a positively observed exited supervisor/runner or launch error without calling a transient retry a permanently broken service. A subsequent successful attempt can still yield `healthy`.

At expiry do not kill/cancel any supervisor, build, runner, pty-host or session; do not rewrite desired state, trigger another Scheduled Task, or request a hard restart. State explicitly that the script has stopped waiting and has not stopped the background startup. The exit-code change is intentional: callers that only accept 0 remain conservative, while callers can now distinguish incomplete observation from an action error. Inspect repository callers during Code and update any timeout-specific handling; the inspected references currently recommend the command rather than parsing its exit 1.

### D-4: Add a non-destructive way to continue waiting

Add `-WaitOnly` to `scripts/restart-session-runner.ps1`. It uses the identical health observer and result contract, but skips desired-state writes, process stopping, PID-file deletion, and Scheduled Task operations. `-WaitOnly` combined with `-Hard` or `-KillSessions` is an argument error before any side effect. Diagnostic logging is allowed; daemon control is not.

Expiry instructions must give this concrete command:

```powershell
pwsh -NoProfile -File scripts/restart-session-runner.ps1 -WaitOnly -TimeoutSec 180
```

Also retain the command to inspect `logs\session-runner.log`, and point to the dated runner log documented in bootstrap. Do not recommend re-running the ordinary restart or `-Hard` to resolve a mere health-wait expiry.

### D-5: Add lightweight, correlated instrumentation in this card

**Instrumentation is required**, but detailed build-target profiling is not. The script needs evidence to report a phase honestly, and future tuning needs the currently missing health and coarse phase intervals. Use versioned structured milestone records in the existing bounded logs; do not add a database table, readiness endpoint, control-state file, or general monitoring framework.

Use a fixed `ANTIPHON_STARTUP` marker followed by one JSON object, with schema version, event name, UTC timestamp, producer PID/start time, and attempt ID. Add only relevant numeric durations/counts, phase/outcome names and exit codes. No prompts, rules text, environment dumps, credentials, or build command argument dumps. Existing logging retention remains in force. Keep new PowerShell source ASCII-only and compatible with the Windows PowerShell 5.1 fallback.

1. **Supervisor (`run-daemon.ps1`).** Give each build/launch iteration a fresh attempt ID. Log build start/completion with monotonic elapsed milliseconds and exit code; launch requested/wrapper started; service exit; retry scheduled; and supervisor stopping. Retain the current build invocation, output capture, retry delays and launch semantics. Record supervisor and wrapper identities distinctly: the current service PID file names `cmd.exe`, not necessarily the runner executable. Pass the attempt ID and supervisor identity to the child through that `ProcessStartInfo`'s environment only, without modifying machine/user environment. Both autostart and AppHost already use this supervisor, so both paths get the same records. Non-runner daemons must retain their current behavior.
2. **Runner (`Program.cs`, `SessionRunnerRuntime.cs`, a small instance-owned `RunnerStartupDiagnostics` helper if useful).** Consume optional correlation data in the composition root; a standalone runner generates its own local attempt identity. Record managed startup entry, runtime resolution start/end, adoption-sweep start/end, and `ApplicationStarted`. Within the existing sweep measure claim restoration, the Herdr pass (including its existing cleanup), and the pty-host manifest pass separately. Record zero-work phases and failed phases too. Enrich existing per-host adoption outcomes with elapsed time around the adoption attempt; do not instrument or alter each pipe/replay/rules substep in this card. Preserve the meaning of the current returned adopted count; distinguish pty-host success counts from Herdr and exited/Pending counts instead of relabeling it a fleet total.
3. **Observer (`restart-session-runner.ps1` and a small script helper).** Timestamp wait start, each change in last observed phase, first successful 200, and the final result. Report phase changes promptly and one continuing-wait line at most every 15 seconds; no success-style heartbeat during an unconfirmed phase. The recorded health time is **this observer's first completed 200**, not the unknowable first instant the endpoint could answer or the first request made by another client. Keep polling resolution visible in the measurement record.

Treat milestone parsing as optional diagnostics. Read bounded appended log data, retaining an incomplete last line until it completes; support file rotation/truncation and begin `-WaitOnly` with a bounded tail. Suggested limits are 256 KiB per read and 8 KiB per milestone. If a gap, malformed record, old schema, stale attempt, or I/O failure prevents attribution, report phase unknown. Do not grep unversioned prose such as "Adopted" or reuse an old "Build succeeded" as current evidence. Accept current phase attribution only for matching attempt and process start identities, excluding pre-restart service events. `lastObservedPhase` plus age means exactly that; a live process stuck in a phase is not proof of continued progress.

Log-write and parse failures must not fail, delay materially, or change adoption; they must never change the HTTP success predicate. The diagnostics helper cannot swallow/reclassify an adoption exception, skip disposal, or replace its existing fate decision. Use existing injected logging and instance state, with monotonic timers local to the measured operation.

The first rollout may use an old, long-lived supervisor that has not loaded the new script. Missing supervisor markers must degrade to `unknown`; health observation still works. Document a single planned supervisor refresh during deployment to load the new instrumentation, rather than forcing a hard restart from the health observer.

### D-6: Defer optimizations with explicit evidence thresholds

- **Early HTTP / background adoption:** rejected here because it changes the safety contract. A future design would have to keep every session-list consumer from treating partial readiness as absence, not merely introduce a separate health check.
- **Bounded parallel adoption:** deferred. First measure phase and per-host totals, then examine shared claims, interpretation/replay, Herdr effects and resource contention. Similar session counts with different totals do not establish safe concurrency or a speedup.
- **Skip unchanged builds:** deferred. A future design needs a complete graph/input and output-validity policy, including SHA stamping, and evidence that this saves meaningful time. Do not substitute runner source timestamps for that policy.
- **Adaptive timeout by session count or indefinite waiting:** rejected. Neither is supported by the measurements; a fixed configurable budget plus an honest expiry remains useful under long tails or stalls.
- **Detailed MSBuild target tracing:** deferred from the shipped path. The coarse build timer answers whether build remains dominant. If it does, collect a separate controlled target-performance summary under the testing/build guide before proposing a cache or build change. Do not turn on default binlogs or dump environment/property values into diagnostics.

No operator decision blocks this plan. D-1 through D-6 are implementation defaults; a separate TestDesign is required because the change touches process supervision and the startup safety barrier.

## Implementation slices

| Slice | Files / work | Tests and completion evidence |
|---|---|---|
| S1: Supervisor timing | `scripts/run-daemon.ps1`; small shared milestone-writing helper under `scripts/` if needed. Add identities, durations and events around existing operations. Keep direct-exe launch and log rotation behavior. | New `DaemonStartupDiagnosticsTests` in `tests/Antiphon.SessionRunner.Tests`, using the existing throwaway-script approach in `DaemonLogRotationTests`; exercise successful launch, failed build followed by retry, service exit and rotation. Re-run `DaemonLogRotationTests` and `PtyHostDeploymentGuardTests`. |
| S2: Runner phase timing | `src/Antiphon.SessionRunner/Program.cs`, `SessionRunnerRuntime.cs`, optional `RunnerStartupDiagnostics.cs`. Add observations around the current barrier and phase sequence. | New `RunnerStartupDiagnosticsTests` for phase ordering/durations, zero manifests, errors and log-write failure. Behavioral HTTP barrier proof in new `RunnerStartupReadinessTests`; existing adoption tests below remain the fate/claims regression evidence. |
| S3: Wait/result contract | `scripts/restart-session-runner.ps1`, proposed `scripts/session-runner-restart-health.ps1` helper containing the observer/classifier. Default 180; monotonic bounded probes; exit/result vocabulary; phase reading; `-WaitOnly`. Separate importing the observer from executing destructive entrypoint code. | New `RunnerRestartHealthTests` in `tests/Antiphon.SessionRunner.Tests`. Run real helper/script control flow against temp files, an isolated endpoint and injected process/task/clock seams; never invoke the production restart path. Test actual outcome and side effects, not only source strings. |
| S4: Operator contract | Update `docs/bootstrap.md` with timeout semantics, exit codes, continuing-wait command, instrumentation records, first-observed-health limitation and supervisor-refresh rollout. Update script help/examples and add a short barrier/diagnostics clarification to `docs/session-runtime-invariants.md`. | Verify documented commands/parameters and final result examples against S3. All touched PowerShell scripts must parse under pwsh and Windows PowerShell 5.1 and remain ASCII. |

No server/client/database change is required. Do not edit generated `docs/cards/` files. Keep the plan and its future TestDesign in this tracked document.

## Acceptance and TestDesign handoff

TestDesign should append the repository's required `## Verification design` with executable V-n/R-n/PC-n items. At minimum it must settle these proof obligations:

1. Reproduce the timing defect with a controlled observer clock: a build/adoption/health sequence taking approximately 100 seconds returns `healthy` with the new default. An explicit 60-second wait returns `wait-expired`, not `action-failed`; a later `-WaitOnly` returns `healthy` with no additional control operation. Also cover startup beyond 180 seconds, a stalled phase, and no diagnostic evidence. Use fake time for the 60/100/180-second cases, plus a short real-clock endpoint check for the HTTP cancellation boundary; do not add minutes of sleeps to every test run.
2. Cover immediate health, refusals/non-200 responses, a hung HTTP response at the end of the budget, wall-clock jumps, and a 200 completing after expiry. Assert elapsed bounds, first-200 fields and exact result/exit meanings. A process, listener or build-success event alone cannot pass readiness.
3. Prove `-WaitOnly`, invalid combinations, and wait expiry never invoke stop/tree-kill, Scheduled Task start, desired-state writes or PID deletion. Distinguish pre-wait stop/launch errors from health timeout. Retrying build/start failures remain observable and can recover within the same wait.
4. Exercise stale attempts/PID reuse, partial/malformed records, log rotation/truncation, missing logs, and an old supervisor with no new markers. Diagnostics must degrade to unknown without creating false success or action failure. Attributable build, adoption, awaiting-HTTP and retry states must be distinguishable in output.
5. **Behaviorally prove the barrier through the actual production startup path** using an isolated runner with a deliberately held adoption operation. Before release, neither `/health` nor `/sessions` may produce a successful response; after release, the first successful session list contains the expected adopted/terminal/Pending representations. The harness must positively establish adoption was entered and held; an arbitrary delay or failed process start is not a passing proof. TestDesign must select a controllable fake host/Herdr server or narrow startup-composition seam without adding a production "skip adoption" flag.
6. Keep a positive control that starts HTTP before the held sweep finishes and makes that test red, then restore the barrier and get green. Also require controls for admitting a late 200 and accidentally executing a restart from `-WaitOnly`. Code reports the red/revert/green results explicitly.
7. Re-run the existing relevant regressions: `PtyHostAdoptionTests`, `HerdrAdoptionSweepTests`, `GrokRulesAdoptionTests`, `TranscriptAdoptionSafetyTests`, and `SessionReconciliationServiceTests` (especially `Unreachable_runner_skips_the_session_pass` and `Session_unknown_to_runner_is_failed_and_its_agent_reset`). TestDesign may select exact relevant methods where a full class is costly, with reasons. Do not replace adoption/claim behavior tests with log-text tests.

Use `dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c420/ -- --treenode-filter "/*/*/<ClassName>/*"` for named runner classes, and the corresponding `tests/Antiphon.Tests` invocation for reconciliation. Run assemblies sequentially. New process-spawning tests carry that assembly's `ParallelLimiter<ProcessSpawnLimit>`. All child runners, manifests, transcript roots, logs, endpoints and process cleanup belong to the isolated fixture; no production port 17204, live provider login or live Scheduled Task is a test fixture. A host booting real server `Program` must keep `ProductionRunnerGuard`/the refusing runner client. No browser/client/E2E suite is forced by this card.

## Rollout, measurement and rollback

Code-stage acceptance is isolated. A live runner restart is a later, deliberately scheduled deployment operation from the canonical checkout, after verifying checkout/build ownership; do not restart the caller's running fleet to collect a Plan or TestDesign sample.

The deployer should check the committed implementation is in the main checkout, record current runner/session identities through the documented operations lane, and perform one planned `-Hard` refresh if needed to replace the old supervisor code. Do not use `-KillSessions`, re-register the Scheduled Task, or launch a second supervisor. If the wait expires, continue with `-WaitOnly` and the logs. Verify both new supervisor milestone schema and the running runner's existing `/capabilities` build identity: HTTP 200 alone cannot prove that new code loaded. Read back the expected sessions and account for any genuine exits under the existing lifecycle rules.

Keep one evidence row per actual restart: commit/attempt/process identities; observed build duration; managed-entry/runtime-resolution/claims/Herdr/pty/adoption durations; `ApplicationStarted`; first observer 200; command and wait elapsed; configured budget; outcome; and relevant session counts. Do not equate `ApplicationStarted` with first successful HTTP, or compare measurements with different start points as the same metric. Collect subsequent normal restarts without creating a scheduled benchmark or extra production restarts. This card can close on the corrected behavior and preserved barrier; a statistically meaningful latency distribution or a startup speedup is not a hidden acceptance requirement.

Rollback is revert of the implementation plus a planned supervisor/runner refresh to load it. There is no data migration. The previous script's 60-second reporting limitation returns on rollback; operators can still supply a longer `-TimeoutSec`. Diagnostic loss alone is not a reason to kill sessions or loosen readiness.

--- next stage ---
next: test-design
handoff: Append executable verification for CARD-0420's 180-second bounded wait, healthy/wait-expired/action-failed outcomes, non-destructive WaitOnly, correlated phase diagnostics, and preserved adoption-before-HTTP barrier; include positive controls and isolated process/clock/endpoint fixtures.
artifact: docs/superpowers/plans/2026-09-07-card-0420-runner-restart-timeout-plan.md
