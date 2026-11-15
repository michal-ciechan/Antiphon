# rc run 34d41b7e: final result (rc/2026-11-20 = b559bfba2e9a)

Read-only extract of Windows Full qualification run `34d41b7e3ccc4ad4b29ed259749ec847`, pinned to
`b559bfba2e9a420495122d27171352fe9b7e1ba4` (`rc/2026-11-20`, candidate `rc-2026-11-20-b559bfba2e9a`).
It was read on 2026-11-15 after the run ended. Nothing on the desktop was stopped, signalled or written.

Sources:

- `C:\Antiphon\releases\candidates\rc-2026-11-20-b559bfba2e9a\logs\2026-11-14-1040-34d41b7e\` (summary.json, 1,204 TRX, per-suite HTML reports, dispatch-schedule.json, client-all.json, script-test logs)
- `C:\Antiphon\releases\state-8e08368a\last-run.json`. This is the only `state-*` dir whose last-run.json names this run id. The other four (`1d7cc1fe`, `231427bc`, `25e35cdf`, `500bb290`) do not.
- `C:\Antiphon\releases\task-8e08368a\` (supervisor.log, supervisor-heartbeat.json, own-last-run.json, commission-*.log, launch-record.json, seed-cumulative*.json)

## Verdict

| Field | Value | Source |
|---|---|---|
| succeeded | `false` | summary.json, last-run.json |
| testsPassed | `false` | summary.json, last-run.json |
| coverageComplete | `true` | summary.json, last-run.json |
| reportDelivered | `true` (nightly-report updated CARD-1726) | last-run.json (summary.json has no such field) |
| outcome | `"TESTS"` | summary.json |
| harnessHang | `""` | summary.json |
| reasons | `[]` | summary.json, last-run.json |
| phase | `"final"` | last-run.json |
| testExit / reportExit / exitCode | `1` / `0` / `1` | last-run.json |
| commission exit | `1` (`commission-exit.txt`; supervisor `EXIT 1`, `SUPERVISOR-END` 2026-11-15T03:17:40.564Z) | task-8e08368a |
| coverageScope / effectiveMode | `Full` / `Full` (fallbackReasons `baseline-absent`) | summary.json selection |
| startedAt | 2026-11-14T10:40:07.2119619Z (state: 10:40:01.17Z; launch-rc 10:39:42Z) | summary.json |
| completedAt | 2026-11-15T03:11:19.3696619Z (state/report final 03:17:39.29Z) | summary.json |
| durationSeconds | 59,472.158 s = **16 h 31 m 12 s** of test wall time; launch to supervisor end about 16 h 38 m | summary.json |
| builds | npm ci, npm run build, client lint, dotnet build Antiphon.sln all `pass` | summary.json |

The run ended red: the test gate failed and every other gate held. Coverage was complete and the report was delivered.

## summary.json structure

The file is 958,736,671 bytes and 17,948,961 lines. It was streamed line by line and never loaded whole. Its top-level fields, in order:
`startedAt, completedAt, durationSeconds, sha, gitRef, trigger, profile, expectedSha, requiredSuites, runId,
clone, logDir, selection, timingRoot, timingChunks[1206], suiteTimeline, platform, coverageScope, outcome,
harnessHang, succeeded, coverageComplete, testsPassed, policyHash, reasons, warnings[25],
expectedSkipRosterDigest, toleratedSkips[28], nativeActiveMax (1), preflight, builds[4], suites[1206],
invocationId, dispatchProgress{schemaVersion, liveness:"closed", sha, completedAt, canonicalPlanDigest, suites[1206]}`.
Nearly all of the size comes from the per-chunk `exclusions` arrays, which appear in both `suites` and `dispatchProgress.suites` (779,129 exclusion rows each).

## Counts

| Item | Count |
|---|---|
| Chunks scheduled (dispatch-schedule.json `entries`, `unresolved` 0) | 1,206 |
| Chunks executed (`timingChunks.lastPhase == complete`) | 1,206 |
| Chunks pass / FAIL | 1,195 / **11** |
| Chunks `timedOut` | 0 |
| Chunks `skipped` | 0 |
| Chunks skipped by seed | 0. The seed only reorders a Full run; it never skips execution. |
| Seed "invalidated" (seed-cumulative-report `basePassedInvalidated`) | 741 of 1,162 base chunks; seededFromBase 418; failed-first 3 |
| Chunks with `processExitTolerated` (exit 8, zero executed tests; every test in the chunk is a tolerated platform skip) | 12, all `pass` |
| TRX files | 1,204 |
| Tests in TRX: total / executed / passed / failed / notExecuted | 26,544 / 26,516 / 26,503 / **13** / 28 |
| Client (Vitest, client-all.json) | 2,059 / 2,059 passed, 131 files |
| Scripts (13 script-test logs) | suite `pass`, exit 0 |
| Excluded by policy (UIDs never scheduled on Windows) | 1,222 (Antiphon.Tests 810, SessionRunner 189, Agents.Pty 144, PtyHost 68, E2E 7, MarkdownPdf 4) |

Per suite (chunks / failing chunks / tests / failed / notExecuted): Antiphon.Tests 928/8/22,555/10/27;
SessionRunner 101/1/2,376/1/1; E2E 76/2/187/2/0; Agents.Pty 29/0/763/0/0; PtyHost 53/0/135/0/0;
MarkdownPdf 12/0/17/0/0; Messaging, GitInProcess, Observability, Resilience, Server.Git 1 chunk each, all green; client 1; scripts 1.
In every TRX-backed suite the test total equals `requiredUidCount`.

**The two scheduled chunks with no TRX** are `client/all` and `scripts/all`. Neither runner writes a TRX by design (`why: no-trx:client` / `no-trx:scripts`, `trxMissing: true`). Both executed and passed. Nothing was skipped, seeded away, merged or missing.

21 chunks carry `why: timing-read-refused`. That is a missing timing receipt only; all 21 passed with their TRX present. They are antiphon all-group-10, 11, 12, 14, 15, 18, 29, 32, 33, 51, 59, 84, 88, 115 to 120, all-profile-422 and all-split-13.

## Failing chunks and tests (complete, exact)

All 11 FAIL chunks have exit code 2, `timedOut=false` and `skipped=false`. No other chunk failed. None timed out, hung, was killed, was retried or was marked inconclusive: the TRX outcomes are only Passed, Failed and NotExecuted. The watcher's list of 11 chunks and 13 tests is complete and exact.

| Chunk | Test (class.method) | Cause class (from TRX message) | Repair on origin/master d7ad08b71 (not in the pin) |
|---|---|---|---|
| antiphon-all-group-54 | LandRefusalDeliveryWitnessTests.C1196_Pretyping_transport_failure_recovers_the_same_row | expectation: reverted row not Pending/attempt 1 (`C1196:pretyping-reverted`) | yes: `8cec1e74e` model C1196 pre-typing fault as definitive refusal |
| antiphon-all-group-58 | CodexCliObservationGapTests.C1029_Unobservable_timestamp_floor_is_original | expectation: row Sent, expected Pending (CARD-1892 delivery-unknown contract) | **yes**: `b95cd2bfe` (+ why-missed `34c645b68`, `f701b2ba9`, docs/investigations/2026-11-10-why-missed-g58-codexcliobservationgap.md) |
| antiphon-all-group-83 | GrokRulesReadyOrderingTests.Ambiguous_input_preserves_launch_and_waits_for_transcript(timeout) | timing: `grok_rules_initialization_failed: timeout`. A 1 s real-clock rules deadline inside the launch budget fails under Windows load. | **yes**: `5918cb3d0` keep rules deadline outside launch budget (+ `27a43f252`, 2026-11-10-why-missed-g83 and 2026-11-14-run-34d41b7e-g83 notes) |
| antiphon-all-group-90 | PhoneHomeOutageMentionTests.In_flight_mention_Enter_late_confirms_without_retyping | expectation: Sent vs Pending (CARD-1892) | yes: `b455f90fa`, `2c523d501` (Merge 232) |
| antiphon-all-group-90 | GrokRulesCompactionRecoveryTests.Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(in-flight, standing) | expectation: no refresh submitted (0 bodies) | yes: `285855b27` (Merge 232) |
| antiphon-all-group-90 | StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders(True) | product: switch not refused past unresolved sent input | yes: product `db878ff16` fix(standing) + `2b34a5dd0`, `928086bfa`, `7fbd4a6a6` (Merge 232) |
| antiphon-all-group-111 | Checkpoints.CheckpointStepDeadlineTests.step_child_that_never_finishes_a_step_times_out_naming_startup | Windows file lock: After(Test) teardown failed, temp `c723-...` "being used by another process" (pwsh child holds the fixture dir) | **yes**: `7a1818123`, `36f29dc0f` run the step child outside the fixture tree / from system temp (+ 2026-11-10-why-missed-g111 note) |
| antiphon-all-group-121 | Infrastructure.LinuxSharedCompilerPolicyTests.Only_compiling_commands_receive_toolset_properties | Windows path separator: `Path.Combine` gives `/cache/tools\Microsoft.Build...` and the test expected `/` | **yes**: `fdc6cca44` expect the host path join (+ 2026-11-10-why-missed-g121 note; the test comment cites this run's group 121) |
| antiphon-all-group-132 | Scripts.WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates | roster: unrostered gate `VolumeRetirementContentTests.Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent` | yes: `50d2dfdd1` roster the two CARD-1609 selector-fixture gates |
| antiphon-all-group-138 | TestHelpers.TestTimingOperationLabelTests.C1620_EveryCallSiteOperationLabelIsKept | labels dropped: 3 `host-load ...` labels | yes: `4eafa4921` preserve three C1514 timing labels; selector `d724062fb` |
| session-runner-all-group-2 | C0824WorkspaceSlotCustodyTests.C0824_LeaseWaitsForTheDispatcherMutationLock | expectation/race: lease entered while mutation lock held | yes: `a4449fa6b`, `f463e7639` (CARD-1926) |
| e2e-all-split-9 | AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch4(before-typing) | timeout: "complete native Review receipt" (CARD-1892 unknown-input hold) | yes: `0f205bfd9` (+ `e8ca4e7ea`, Merge 233) |
| e2e-all-split-54 | DispatchBaseWarningDeliveryE2ETests.C540_PreTypingCrashRecoversWarnings | timeout: "complete native Land receipt", DispatchBase notification stuck AwaitingReceipt | yes: `0f205bfd9` (Merge 233); the DispatchBase Queued flake repair (Merge 234) is also on master |

Every repair commit above was checked with `git merge-base --is-ancestor` against origin/master `d7ad08b71a19fa79e744284e5ffe7f0168de561a`. None is an ancestor of the pin. Every failing test has a repair on master. **The four previously unmapped chunks (g58, g83, g111, g121) all have repairs on master.**
Native Windows proof is still owed for the Windows-specific causes (g111 file lock, g121 path separator, g83 load timing). It comes from the next Windows run.

## Supervisor, locks, processes

- The supervisor (pid 26440) exited cleanly. supervisor.log ends `STATE-COPY own-last-run.json`, `EXIT 1`, `SUPERVISOR-END` (03:17:40Z). The build-slot lease `3982a5fb...` was released after 59,878 s. The last heartbeat (03:17:15Z) recorded `hangSuspected=false` and trx=1204.
- Native lock: `candidates\rc-2026-11-20-b559bfba2e9a\coordination\` is empty, so `native-run.lock` is released. `state-8e08368a` contains only `history\` and `last-run.json`, with no `run.lock`.
- Processes, read-only: none of pids 26440, 146756, 64496, 57580, 104724, 70828, 93588, 57420 or 112840 is alive. No testhost or Antiphon test-host process is running. No dotnet, pwsh, node or postgres command line references `checkout-8e08368a`, `task-8e08368a`, `34d41b7e` or `rc-2026-11-20`.

## Seed for a failed-first rerun

- Input seed used by this run: `C:\Antiphon\releases\task-8e08368a\seed-cumulative.json` (digest `76dc9514...`, the same digest as dispatch-schedule `historyDigest`). It was built from the final 97150a0b export plus the task-cf82bfb0 prior. A copy is in the log dir as `prior-failures.json`. Its 3 failed-first chunks (antiphon g76, g109, session-runner profile-16) all **passed** in this run.
- **No next seed was written.** The supervisor and run produced no seed, failed-first or stopped-run export in task-8e08368a, the candidate dir or the coordination dir. A failed-first seed for the next cut must be built from this run's results: summary.json and the TRX, with 1,206 completed chunks, 11 failed rows, no in-flight or hung chunks, and the `scripts/lib/nightly-stopped-run-seed.ps1` / `build-cumulative-seed.ps1` pattern. As a run record the inputs are complete (`liveness: closed`, all chunks `complete`). The seed file itself does not exist yet.

## Not executed or not covered

- 28 tests reported NotExecuted. All 28 are tolerated skips listed in `toleratedSkips` and in the 25 `stale-roster:` warnings: 27 `platform` and 1 `live-context`.
  - C1734 CheckpointColdBuild (3, g110)
  - VolumeRetirementApplyGate (7, g133)
  - C0824RefOnlyCleanup Remote_cleanup (3, g87)
  - C0824RefOnlySync Reported_tip (1, g91)
  - C1717 WorktreeNoFollowDelete (8, profiles 1831 to 1841)
  - C1105_Resume_extraction (1, profile 2052)
  - C1008 Host_proof (2, profiles 2244 and 2245)
  - LandGraphEvaluation EvaluationBudgetRefusesSlowDotnet (1, profile 966)
  - SessionRunner Claude_launch_seeds_trust_in_the_runner_config_dir (1, sr g5)
  - C680_LiveNudgeRefusesPermissionAndQuestionDialogs (live-context, g5)
- 1,222 UIDs are excluded by policy and never scheduled on Windows. Most are Linux-only by construction (CARD-1050, 1609, 1210, 0590, 1619, 0824, 1653 and others). The rest are opt-in real-provider, headed or live-session workflows (CARD-0599, 1313, 1305) and QEMU/native placeholders (CARD-0490). Notable owed gaps:
  - the 27 phone-home recipient methods say "Separate Windows handle qualification remains owed" (CARD-1561);
  - CARD-1850 says "Windows transport is owed by S3";
  - CARD-1545 and CARD-1605 are manual measurement obligations;
  - CARD-1566 is a manual live-provider obligation.
- The run is pinned to b559bfba2e9a. It contains none of the landings above, including Merges 232 to 235, CARD-1926, CARD-1927 and CARD-1928, or anything else after the cut. None of those repairs has native Windows Full evidence yet.
