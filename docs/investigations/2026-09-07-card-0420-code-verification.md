# CARD-0420 Code verification

Implemented S1-S4: the 180-second bounded observer, healthy/wait-expired/action-failed results, read-only WaitOnly, correlated supervisor/runner milestones, and the preserved adoption-before-HTTP barrier. The required verification selections passed 92/92, with 0 failures and 0 skips. All 13 positive controls produced the intended regression and returned green after restoration. No live runner restart or Scheduled Task operation was used.

Based on TestDesign 66db3c35. That commit was present only on its separate branch when Code began; its single documentation change was cherry-picked onto this checkout as 928f8b2f before implementation. No caller decision was required.

## Implementation and operational details

- `scripts/restart-session-runner.ps1` is a thin entrypoint. `scripts/session-runner-restart-health.ps1` owns an inert import, dependency factory, monotonic observer, bounded log decoder and result formatting data. The internal platform intercepts every daemon-control operation in tests; an AST boundary check runs before the wrapper fixture executes.
- Listener ownership uses a local IPv4 TCP owner lookup plus process path/start identity. Both PowerShell hosts exercise this lookup against the fixture-owned HTTP listener. A non-200, foreign identity, build marker or response completed outside the budget cannot establish readiness.
- `scripts/run-daemon.ps1` gives each iteration a fresh attempt, preserves incremental builds/direct-executable launch and 5-second build/3-second service retries, and passes correlation only in child process environment. Milestone writes use explicitly shared handles: the isolated fixture exposed an Add-Content/child-stdout-open race that could cause a harmless service to exit 1 before it ran.
- `RunnerStartupDiagnostics`, `Program.cs` and `SessionRunnerRuntime.cs` record resolution, claims, Herdr, pty, per-session adoption and ApplicationStarted. Sink failures are isolated; existing adoption exceptions and disposal remain authoritative. The pty adopted return value keeps its original meaning.
- `docs/bootstrap.md` and `docs/session-runtime-invariants.md` document exit codes, the continuation command, timestamp limitations and the separately scheduled supervisor refresh. Repository callers recommend this command; none had timeout-specific exit-1 parsing to change.

## Required runs

All commands ran sequentially, using isolated `bin-c420/` outputs. The first command for each project rebuilt; subsequent commands used `--no-build` while compiled inputs were unchanged. Each command was the following template with the project and filter below:

```powershell
dotnet run --project tests/<Project> --property:OutputPath=bin-c420/ -- --treenode-filter "/*/*/<Filter>"
```

| Project | Filter | Pass / executed | Fail / skip | Wall seconds including invocation/build |
|---|---|---:|---:|---:|
| `Antiphon.SessionRunner.Tests` | `RunnerRestartHealthTests/*` | 43/43 | 0/0 | 93.64 |
| `Antiphon.SessionRunner.Tests` | `RunnerRestartScriptCompatibilityTests/*` | 8/8 | 0/0 | 24.8 |
| `Antiphon.SessionRunner.Tests` | `DaemonStartupDiagnosticsTests/*` | 8/8 | 0/0 | 35.33 |
| `Antiphon.SessionRunner.Tests` | `RunnerStartupDiagnosticsTests/*` | 3/3 | 0/0 | 2.47 |
| `Antiphon.SessionRunner.Tests` | `RunnerStartupReadinessTests/*` | 2/2 | 0/0 | 17.94 |
| `Antiphon.SessionRunner.Tests` | `DaemonLogRotationTests/*` | 4/4 | 0/0 | 7.3 |
| `Antiphon.SessionRunner.Tests` | `PtyHostDeploymentGuardTests/*` | 3/3 | 0/0 | 2.78 |
| `Antiphon.SessionRunner.Tests` | `PtyHostAdoptionTests/*` | 6/6 | 0/0 | 22.78 |
| `Antiphon.SessionRunner.Tests` | `GrokRulesAdoptionTests/*` | 4/4 | 0/0 | 5.56 |
| `Antiphon.SessionRunner.Tests` | `TranscriptAdoptionSafetyTests/Claims_are_restored_from_sidecars_before_new_adoption_runs` | 1/1 | 0/0 | 2.31 |
| `Antiphon.SessionRunner.Tests` | `TranscriptAdoptionSafetyTests/Sidecar_path_is_retailed_directly_after_restart_with_no_discovery` | 1/1 | 0/0 | 2.97 |
| `Antiphon.SessionRunner.Tests` | `TranscriptAdoptionSafetyTests/Restart_adopt_without_a_bound_transcript_stays_unbound_until_new_input` | 1/1 | 0/0 | 4.88 |
| `Antiphon.SessionRunner.Tests` | `TranscriptAdoptionSafetyTests/THE_CARD_0181_shape_exact_id_bind_displaces_a_stale_sidecar_claim_after_restart` | 1/1 | 0/0 | 2.47 |
| `Antiphon.SessionRunner.Tests` | `HerdrAdoptionSweepTests/R1_runner_restart_adopts_when_pane_lists_the_child` | 1/1 | 0/0 | 2.81 |
| `Antiphon.SessionRunner.Tests` | `HerdrAdoptionSweepTests/R2_restored_empty_pane_with_os_dead_is_RestartPresumedDead` | 1/1 | 0/0 | 2.39 |
| `Antiphon.SessionRunner.Tests` | `HerdrAdoptionSweepTests/R6_unreachable_at_restart_with_os_alive_is_pending_then_adopts_when_herdr_returns` | 1/1 | 0/0 | 3.12 |
| `Antiphon.SessionRunner.Tests` | `HerdrAdoptionSweepTests/R7_herdr_unreachable_and_os_dead_is_ChildGone_without_a_socket` | 1/1 | 0/0 | 2.59 |
| `Antiphon.Tests` | `SessionReconciliationServiceTests/Unreachable_runner_skips_the_session_pass` | 1/1 | 0/0 | 62.94 |
| `Antiphon.Tests` | `SessionReconciliationServiceTests/Session_unknown_to_runner_is_failed_and_its_agent_reset` | 1/1 | 0/0 | 19.42 |
| `Antiphon.Tests` | `SessionReconciliationServiceTests/Pending_herdr_session_is_not_failed_by_pass_1` | 1/1 | 0/0 | 20.12 |

Required selections took 338.6 wall seconds in aggregate. Exact commands, counts and full logs: `C:\src\Antiphon\.antiphon\c420-verification\results.json` and its sibling logs.

## Coverage of the TestDesign IDs

Counts below overlap: several obligations use the same executable test. The unique required-run count is 92, not the sum of these rows. Method names are in the TestDesign and the command manifest above.

| ID | Evidence | Passing test rows |
|---|---|---:|
| V-1 | Default 100,000 ms startup succeeds at 180; explicit 60,000 ms expiry continues with WaitOnly for 40,000 ms and no new mutation. | 2 |
| V-2 | 7,000 ms action time excluded from wait; +/-1-hour UTC jumps and phase changes never extend 180,000 ms. | 4 |
| V-3 | Immediate probe, 1/750 ms remainder clamps, 1 ms late 200 rejected, real immediate/hung endpoints on both shells. | 8 |
| V-4 | Refusal/204/302/404/503, foreign PID and reused start identity cannot pass; verified 200 can pass without logs. | 8 |
| V-5 | WaitOnly healthy/eventual/expired/missing PID/stopped-state cases retain sentinel bytes and empty mutation traces; invalid combinations/budgets fail before control. | 9 |
| V-6 | Soft/Hard/WaitOnly expiry has no post-wait control calls; the real hung endpoint keeps accepting after observer expiry. | 5 |
| V-7 | Stop/task action errors name the operation; build failure, launch error and service exit retries can recover or expire. | 8 |
| V-8 | Shared wrapper assertions enforce one final JSON line, matching exit/outcome, timing fields and no trailing output; direct result contract assertion included. | 43 |
| V-9 | Current-attempt/start matching, stale producers, schema mismatch, partial records, malformed data, bounded gaps, replacement/truncation and absent files; phase throttling and age. Real producer records also pass the production decoder in V-10/V-12. | 5 direct |
| V-10 | Real supervisor with intercepted build and harmless service, both shells; failure/retry, repeat builds, fresh correlation, wrapper identity and generic no-build daemon. | 8 |
| V-11 | Empty phases, original cancellation and throwing instrumentation sink preserve fate/count. Actual mixed-backend ordering is additionally asserted in V-12. | 3 |
| V-12 | Actual executable reaches held pane.read with owned live processes; both endpoints unavailable for five seconds. First list after release has Herdr Running or Pending plus the terminal pty row. | 2 |
| V-13 | All three touched production scripts parse as ASCII on pwsh and Windows PowerShell 5.1; actual wrapper/HTTP APIs run on both. | 8 |
| R-1 | V-1/V-2/V-3 deadline and transport tests. | 51 across health/compatibility classes |
| R-2 | V-5/V-6/V-7/V-8 classification and mutation traces. | 43 across health class |
| R-3 | V-3/V-4/V-9 negative readiness/correlation evidence. | 43 across health class |
| R-4 | Production readiness barrier and first complete list. | 2 |
| R-5 | Diagnostics (3), PtyHost adoption (6), Grok rules adoption (4), four required transcript safety methods. | 17 |
| R-6 | Four required Herdr fate methods plus both real readiness rows. | 6 |
| R-7 | Unreachable skips, unknown session fails/resets, Pending Herdr stays represented. ProductionRunnerGuard remains active. | 3 |
| R-8 | Supervisor (8), compatibility (8), rotation (4), direct-executable deployment guards (3). | 23 |

## Positive controls: red, restore, green

Each control changed one production line, ran only its named method (including its data rows), restored the exact original bytes, and reran green. C# controls rebuilt isolated outputs on both arms. No mutant was committed. Accepted controls total 37 baseline rows, 37 mutant rows (28 intended failures, 9 unaffected passes), and 37 restored-green rows; zero skips.

| ID | Temporary change | Red assertion | Red failed / executed | Restored pass / executed |
|---|---|---|---:|---:|
| PC-1 | default 180 -> 60 | 100-second startup becomes wait-expired | 1/1 | 1/1 |
| PC-2 | remove completion-budget predicate | late response becomes healthy | 1/1 | 1/1 |
| PC-3 | probe budget always 5000 | 1/750 ms remainder receives 5000 | 2/2 | 2/2 |
| PC-4 | WaitOnly guard always true | forbidden write-state in trace | 5/5 | 5/5 |
| PC-5 | stop-service on expiry | post-wait mutation detected | 3/3 | 3/3 |
| PC-6 | expiry maps to action-failed/1 | expected wait-expired/2 | 1/1 | 1/1 |
| PC-7 | HTTP status predicate always true | non-200 becomes healthy | 5/8 | 8/8 |
| PC-8 | identity predicate always true | foreign/reused identity becomes healthy | 2/8 | 8/8 |
| PC-9 | bypass milestone identity match | stale process supplied a phase | 2/2 | 2/2 |
| PC-10 | remove awaited adoption barrier | HTTP became ready while adoption was held (both rows) | 2/2 | 2/2 |
| PC-11 | omit transcript claim restore | surviving transcript no longer claimed against another session | 1/1 | 1/1 |
| PC-12 | throw instrumentation sink failure | IOException prevents unchanged adoption completion | 1/1 | 1/1 |
| PC-13 | skip build guard | expected two builds, observed zero | 2/2 | 2/2 |

Exact mutation strings, affected files, commands, counts and logs are in `C:\src\Antiphon\.antiphon\c420-controls\results.json`. PC-10 accepted evidence is the replacement run in `C:\src\Antiphon\.antiphon\c420-controls-repeat\results.json`: its first run had a fake-server cleanup exception mask one row's primary assertion; cleanup now preserves the primary failure, and both replacement red rows show early HTTP explicitly.

## Timing and artifacts

After the final fixture evidence/cleanup refinements and preservation of the existing
KillSessions endpoint-unavailable fallback, the compatibility class was rebuilt and
rerun 8/8, followed by readiness 2/2, with no failures or skips. Logs are
`C:\src\Antiphon\.antiphon\c420-compat-refresh.log` and
`C:\src\Antiphon\.antiphon\c420-barrier-refresh.log`. The accepted 13-control
baseline/red/restored runs took 362.0 seconds in aggregate, including isolated
rebuilds. `git diff --check` passed and every mutation was restored.

- The real two-second hung-response fixtures measured 2076.554 ms (pwsh) and 2107.267 ms (Windows PowerShell 5.1), within the specified 2000..3000 ms scheduling tolerance. Both established an accepted request and subsequent server acceptance after expiry.
- Structured wrapper results, synthetic control traces and fixture log inputs: `C:\src\Antiphon\.antiphon\c420-evidence\antiphon-c420-*\`.
- Real HTTP timings: `C:\src\Antiphon\.antiphon\c420-evidence\http-*\result.json`.
- Real producer milestones, child inheritance and driver output: `C:\src\Antiphon\.antiphon\c420-evidence\daemon-*\`.
- Held RPC requests, endpoint observations, process identities and first complete session lists: `C:\src\Antiphon\.antiphon\c420-evidence\barrier-*\`.
- No startup speedup, fleet latency percentile, deployment or live session survival observation is claimed. Deployment remains the planned canonical-checkout supervisor refresh documented in bootstrap.

--- next stage ---
next: review
handoff: Review CARD-0420 S1-S4 and the isolated 92-test/13-control evidence; verify bounded identity-qualified health, read-only WaitOnly, optional diagnostics and the unchanged complete-adoption barrier. No live deployment was performed.
artifact: docs/investigations/2026-09-07-card-0420-code-verification.md
