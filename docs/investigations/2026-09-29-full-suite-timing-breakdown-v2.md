# Full-suite timing breakdown v2: partial Linux pass (2026-09-29)

## Scope and method

Source SHA: `a75ccdfd1c9286515b90910fb000ce7ec9fc556f`. This is a single-pass
measurement from a Linux linked worktree. The canonical `rc` execution policy is
an automated Windows profile; this measurement is therefore a host-specific probe,
not release qualification. No lane was rerun to classify failures. The native
lanes ran separately except Messaging and Agents.Pty, which overlapped the long
PtyHost lane; Antiphon.Tests did not overlap Agents.Pty. Other tasks also used
the shared host. Build and test drivers used `scripts/build-slot.ps1`; outer times
include any broker wait and process shutdown. Raw logs, timing CSV and test-result
artifacts are under `.antiphon/timing-6871203f/` (worktree-local, ignored by Git).

The solution build used `--property:OutputPath=bin-timing687/` to avoid shared
daemon outputs. It failed because Linux apphost files named `fakeclaude` and
`fakegrok` occupied the paths that test projects' copy targets need as directories.
The generated output was rearranged after the failed build so produced test DLLs
could run. That repair was not a second build.

## Measurements

All times are one observed outer wall span, rounded to 0.1 seconds. An ended lane
without TRX has no reliable test count. `NotExecuted` is included in skipped.

| Phase/lane | Outer wall | Slot wait | Result |
|---|---:|---:|---|
| `npm ci` client setup | 17.1 s | n/a | success; 765 packages installed |
| Client bundle build | 36.6 s | 0 s | success |
| Client lint | 68.1 s | 0 s | failed: 3 errors, 0 warnings |
| .NET solution build | 156.2 s | 0 s | failed: 30 helper-copy errors, 664 warnings |
| Generated helper output repair | 0.03 s | n/a | moved conflicting apphosts and copied helper output; no rebuild |
| Antiphon.Tests Unit | 295.0 s | 0 s | stopped: no test child at the end, no TRX; count unknown |
| Client Vitest | 124.7 s | 0 s | 1,017 pass, 2 fail (1,019 tests; 114 files) |
| SessionRunner native | 401.4 s | 90 s | stopped after quiet, near-zero-CPU interval; no TRX; count unknown |
| PtyHost native | 1,288.3 s | 0 s | 98 pass, 8 fail, 37 skip (143) |
| Messaging native | 61.2 s | 0 s | 239 pass, 1 fail (240) |
| Agents.Pty native | 11.1 s | 0 s | 425 pass, 14 fail, 210 skip (649) |
| Antiphon.Tests Integration | 158.6 s | 0 s | stopped: test child gone, no TRX; count unknown |
| Script census (13 checks) | 90.5 s sum | 0 s each | 9 pass, 4 fail |

The client JSON first-to-last file interval was 115.8 s; its reported 927.3 s
of test work overlaps across files. Its two failed tests were the lint gate and
`RoutingSettingsTab C470 mutation can be selected and saved independently`.
The standalone lint errors were two `react-refresh/only-export-components` errors
in `placement.tsx` and one `react-hooks/set-state-in-effect` error in
`RunnerDefaultsSection.tsx`.

From the native TRXs: Messaging first-to-last test span was 56.5 s; the outer
command spent 2.5 s before the first test and 2.2 s after the last. Its failure
was `TestClassificationGuardTests.Registry_matches_compiled_metadata`.
Agents.Pty spent 2.2 s before its first test, 8.1 s from first to last and
0.8 s after the last; its 137.3 summed case-seconds overlap in parallel.
PtyHost spent 2.0 s before its first test, 1,285.5 s from first to last and
0.8 s after the last. Eight
`LinuxCgroupCustodyTests` accumulated 1,281.0 case-seconds, nearly the entire
lane wall time. Several cases each waited 120 or 300 s on an owned child and
failed in this environment. The pre-first and post-last intervals are process,
discovery/setup and reporting/teardown envelopes respectively, not precise
fixture-only times.

| Lane | Largest measured group | Case/file work | Interpretation |
|---|---|---:|---|
| PtyHost | `LinuxCgroupCustodyTests`, 8 cases | 1,281.0 s | Dominates its 1,288.3 s outer wall |
| Messaging | `KafkaInboundCommitTests`, 2 cases | 12.7 s | Overlaps other classes |
| Messaging | `KafkaConsumerGroupObservationTests`, 9 cases | 12.7 s | Overlaps other classes |
| Agents.Pty | `ClaudeEffortPromptTests`, 64 cases | 68.0 s | Overlaps heavily; whole lane 11.1 s |
| Agents.Pty | `ClaudeStartupReadinessTests`, 17 cases | 34.2 s | Overlaps heavily |
| Client | `DelegateModal.test.tsx`, 19 cases | 68.8 s file interval | Overlaps other files |
| Client | `AgentsPage.test.tsx`, 27 cases | 68.5 s file interval | Overlaps other files |
| Client | `CardEditModal.test.tsx`, 14 cases | 62.3 s file interval | Overlaps other files |

The script checks took 90.5 s in aggregate. `test-apphost-graceful-stop` (32.4 s)
and `test-deploy-am-service` (20.7 s) accounted for 58.7% of that sum. The four
failed checks were `test-cleanup-claude-sessions`,
`test-deploy-nightly-watchdog`, `test-nightly-report` and
`test-reap-zombie-agents`. Each was run once; their detailed failure output is
in the worktree-local script logs.

The SessionRunner log showed Windows named-pipe and `powershell.exe` failures
before it stopped advancing. The Unit and Integration lanes generated no TRX;
their wrappers held build slots after their test children disappeared. Neither
is a completed test verdict, so no failure count is inferred from their logs.
The canonical RC E2E lane was outside this Linux host's scope: the policy
defines it as an automated Windows profile. No E2E duration is claimed.

The four one-time install/build/lint commands totalled 278.0 s. That is an
observed cost to prepare this fresh worktree, including failed lint and failed
.NET build; it is not a clean-build baseline. The experiment from the first
`npm ci` start to the final script exit took 48m 50s of wall time, with gaps
between commands and some lane overlap. PtyHost alone occupied 21m 28s.

| Script check | Wall | Exit |
|---|---:|---:|
| `test-apphost-graceful-stop` | 32.4 s | 0 |
| `test-apphost-lock-age` | 2.4 s | 0 |
| `test-apphost-main-worktree-guard` | 9.0 s | 0 |
| `test-apphost-probe-class` | 2.0 s | 0 |
| `test-cleanup-claude-sessions` | 2.8 s | 1 |
| `test-cleanup-codex-test-residue` | 2.2 s | 0 |
| `test-client-mode` | 3.9 s | 0 |
| `test-deploy-am-service` | 20.7 s | 0 |
| `test-deploy-nightly-watchdog` | 3.0 s | 1 |
| `test-hooks` | 3.4 s | 0 |
| `test-nightly-report` | 3.5 s | 1 |
| `test-reap-zombie-agents` | 2.4 s | 1 |
| `test-stage-value-report` | 2.6 s | 0 |

### What the present evidence cannot separate

The existing TRX records case start/end and assembly run start/end, but no
explicit timestamps for discovery, assembly hooks, shared database setup,
container teardown or result serialization. The pre-first-test interval is only
an upper bound for setup, and the post-last interval is only an upper bound for
teardown. In particular, the Antiphon.Tests database migration is inside its
assembly hook and has no emitted duration here. A failed or interrupted native
run may leave no TRX at all, as three lanes did in this pass. Vitest's transform,
setup, import, test and environment totals overlap across workers and cannot be
added to recover wall time.

## What to instrument

1. Add start/end/elapsed fields to `Invoke-NightlyOwnedProcess` and
   `Wait-NightlyOwnedCleanup` in `scripts/lib/nightly-tests-impl.ps1`. The existing
   `summary.json` contains whole-run `durationSeconds`, but its `builds` and
   `suites` rows have no durations. Record each preflight, `npm ci`, client build,
   lint, .NET restore/build, Playwright install, native chunk, Vitest invocation
   and script-census child. In `scripts/build-slot.ps1` and the checkpoint runner,
   separate slot queue time from command execution and result publication.
   The 90-second SessionRunner slot wait would then be visible as queue cost.
2. In the TUnit hooks, emit start/end events for discovery and assembly startup,
   `TestDbFixtureLifecycle.BootstrapAsync` with `CreateAsync`, `StartAsync`,
   `MigrateAsync` and protection/template work, then `DisposeCoreAsync`. Emit
   class-level hooks for serial/Slow groups. In E2E, time
   `AntiphonAppFixture.InitializeAsync`/`DisposeAsync` (isolated runner,
   PostgreSQL, host) and `PlaywrightFixture.InitializeAsync`/`DisposeAsync`
   (browser launch/close) separately. Keep case-body duration in TRX and Vitest
   JSON. Concurrent case and file durations are additive work, never wall time;
   use event intervals to identify the critical path.
3. Persist an atomic `timing-summary.json` beside each TRX, with a stable schema:
   run ID, SHA, profile, host/OS, filter, start/end UTC, monotonic elapsed,
   phase name and parent ID, slot wait, result/counts, and paths to TRX, Vitest
   JSON and logs. Write an initial record before work starts, update it after
   each phase, and finalize it on pass, fail, timeout and cancellation. That
   preserves partial timing when a native runner never writes TRX. Add a
   run-level rollup for critical-path wall time and phase costs, without summing
   overlapping work. Compare p50/p95 across runs with the same host class,
   profile and selection; retain raw phase rows to spot distribution shifts in
   Code, Review and Land.

For stage attribution, store the same run ID on Code checkpoints, Review
verification and Land verification receipts. The rollup should distinguish
authoring/waiting time from the measured build/test driver, including slot queue,
fixture setup, test execution, teardown and report publication. One Linux red
sample is insufficient to set thresholds; first collect several complete Windows
RC and ordinary stage runs after instrumentation.
