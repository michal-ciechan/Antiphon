# RC Full qualification speed-ups

Task `e197fb96`; planning base `e4e65f69078f6340ba5fb8e0cee27f6778d58802`.
CARD-1514 is the existing timing/fixture workstream, not a newly assigned card identity.
Status: measurement slice grounded; remaining slices and checkpoint handoff follow.

Evidence: [run 28bbb58e breakdown](../../investigations/2026-10-29-rc-run-28bbb58e-time-breakdown.md), read in full. Its historical Windows run lasted 1378.1 minutes: setup 11.0, chunks 1365.7, inter-chunk gaps 1.4. No desktop files were read by this task.

## Decisions

- D-1: Measure first. The process responsible for 114.2 minutes of post-session delay is unknown; telemetry is only a hypothesis.
- D-2: Preserve Full coverage, exclusion rules, process custody and frozen candidate identity. Speed is not gate credit.
- D-3: Keep native and in-process concurrency at 1 until an independent Debug comparison supports one change at a time.
- D-4: Implement reviewed changes on both master and the active rc line before a new Full run; do not mutate an existing sealed attempt.
- D-5: Route portable work by live runner defaults, native Job Object evidence by Windows platform. Read runner-defaults and session-runners on 2026-10-29; both portable and Windows lanes are available. Do not pin a fleet location.
- D-6: Reject changing `nativeActiveMax` as though it were a setting. It is a measured output and `>1` currently fails coverage. Prefer the existing in-process limiter for the first diagnostic trial; retain native serialization.

## Ground truth

| Brief assumption | Evidence / code fact to preserve or verify |
|---|---|
| Qualification could be slowed by retries | All attempts were 1; duplicate chunks absent; retry overhead 0. |
| Process exit delay has an obvious cause | 384 chunks lingered after MTP session end; root versus descendant was not captured. |
| Setup dominates every small chunk | 190 Antiphon chunks show about 14 s pre-first-test; fixture attribution is missing. |
| Parallelism is safe because CPU was low | No host-wide CPU/memory samples exist; the live-tree CPU counter loses exited processes. |
| Planner evidence supports regrouping | Per-suite plans were overwritten; only the final suite survived. |
| Persisting `Observation` alone answers which process lingered | `scripts/lib/nightly-owned-process.cs:37` has PID/start identity, durations, cleanup/drain flags and descendant PID/start strings, but no root exit stamp, descendant image or exit stamp. Add those observations as well as persistence. |
| `nativeActiveMax=2` is a supported option | `scripts/lib/nightly-tests-impl.ps1:1135` throws on overlap; line 1431 rejects coverage when the observed maximum exceeds one. No scheduling knob exists. |
| All 138 small launches can simply be merged | `Get-NightlyProfileExecutionChunks` emits exact methods for partially excluded classes; `ConvertTo-NightlyProfileLaunchGroups` already packs safe whole classes by namespace/cost but appends these exact leaves separately. Prefix-unsafe class/method names must remain exact. |
| Chunk discovery costs 21.377 s each | `$chunkDiscoverySeconds = $suiteDiscoverySeconds` copies one suite value. MTP initialization is not a measurement of chunk discovery. |
| The 14 s setup is repeated per test | `TestDbFixture` has assembly hooks and a lazy singleton lifecycle; each chunk starts a new assembly process. `TestDbOperations` already emits DB start/migrate spans; enclosing setup/teardown stamps and reliable joins are still needed. |
| The existing host-stats script is a sampler | `scripts/measure-host-stats-overhead.ps1` compares a private runner's CPU with sampling on/off. Reuse its isolation/overhead pattern, not its output as host-wide run telemetry. |

## A. Instrumentation before optimization

Slice **S1** is required on rc and master before another Full attempt. Direct saving is **0 min** for every A item. Collectors are diagnostic and must not change the TRX/custody verdict. The instrumentation prerequisite is separately checked by the commissioner. Test names below are proposed new tests, not claims of existing execution. Commit each failing witness before its fix; fixture/build/zero-test failures do not count as RED.

| Item / evidence | Smallest change and files | RED witness first / checkpoint | Risk and measurement acceptance |
|---|---|---|---|
| A1 exit attribution: breakdown sections 1, 5, 114.2 min | Extend `NativeProcessResult` and `CaptureDescendants` in `scripts/lib/nightly-owned-process.cs`; carry the result from `Invoke-NightlyOwnedProcess` in `scripts/lib/nightly-tests-impl.ps1` into `scripts/lib/test-phase-evidence.ps1`. Record root start/exit, job-empty observation, retained descendant identity/image/exit, launch/execution/cleanup/drain durations and custody flags. Correlate bounded post-exit MTP session-end parsing with those stamps. | New `tests/Antiphon.Tests/Scripts/RcFullNativeTimingTests.cs`: `RootExitIsAttributed`, `DescendantExitIsAttributed`, `DrainIsSeparateFromExit`, `TimeoutRetainsPartialObservation`; extend owned-child fixture in `tests/Antiphon.Nightly.OwnedChild` and `scripts/test-nightly-native.ps1`. Each controlled barrier must expose the missing field/phase on baseline. CP-1, CP-2. | Metadata only, no arguments/environment/user paths. Retained handles/start identities avoid PID-reuse attribution. If a process exits before capture, record unknown/missed identity, never guess. Diagnostic failure cannot release a live child. Reconcile phases to wall with an explicit remainder and observation resolution. |
| A2 durable plans: sections 2, 5, only last suite survives | Change `Write-TestPhasePlannedManifest`, `Complete-TestPhaseLaunch`, `Invoke-TestPhaseRunSummary` in `scripts/lib/test-phase-evidence.ps1` and both `groups-planned.json` readers in `nightly-tests-impl.ps1`. Write `groups-planned-<suite>.json` plus a run index; bind run/attempt/suite/source/assembly/policy/cost digests, exact filters, expanded UIDs and estimated costs. | New `RcFullTimingContractTests.PlansRetainEverySuite`: two suites with the same `all-group-1` id must both survive and join their own execution. Old singleton overwrite must fail the assertion. CP-1. | Update producers and readers together; support historical singleton input explicitly without pretending it contains all suites. Use atomic writes and allowlisted suite filenames. Include non-native client/script membership in the run index. |
| A3 fixture stamps: sections 1, 5, 47.1 min unattributed | Wrap once-only bootstrap and dispose in `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs` with `TestTiming.BeginFixturePhase`; inspect registration ordering in `tests/Shared/TestTimingRegistration.cs` and `TestTimingAttribute.cs` so recorder starts before assembly setup and flushes after teardown. Instrument `tests/Antiphon.E2E/Fixtures/{AntiphonAppFixture,PlaywrightFixture}.cs` only where existing fixture spans are absent. | New `tests/Antiphon.Tests/TestHelpers/RcFullFixtureTimingTests.cs`: `BootstrapStampedOnce`, `TeardownStampedAfterUse`, `FailedSetupRemainsIncomplete`. Controlled lifecycle gates assert stamps bracket actual work and eight callers share one setup; removing enclosing stamps fails. CP-3. | Separate fixture rows from test bodies; no multiplying shared costs across tests. Preserve lazy DB-free selections and startup-test exceptions. Record readiness, migrations, restore/clone, web-host/browser start and teardown separately; unavailable phases null. |
| A4 host pressure: sections 4, 5, no host-wide samples | Add `scripts/lib/qualification-host-stats.ps1` called by `nightly-tests-impl.ps1`; bounded 30 s sampling of host CPU, available/committed memory and disk queue where supported, plus core count and load identity. Use existing host-stats collector semantics after adapter inspection; `measure-host-stats-overhead.ps1` supplies an isolation/overhead example only. | `RcFullTimingContractTests.HostSamplesPreserveUnknownAndStop`: fake clock/provider emits two samples, unavailable metric stays null, cancellation joins sampler, no writes after owner completes. Baseline lacks samples. Add native collection observation to CP-2. CP-1. | Sample host totals, not disappearing live-tree CPU. Missing/unsupported/stale metrics remain explicit. Collector bounded calls, no broker setting changes, no new daemon. Sampler completion is owned by the qualification and joined on every exit. |
| A5 discovery/setup: sections 1, 3, suite constant copied; 3.2 min planning gap | Separate `suiteDiscoverySeconds` (once) from `chunkDiscoverySeconds` (actual pinned diagnostic markers) in `nightly-tests-impl.ps1` and `test-phase-evidence.ps1`; record planning and npm/build/lint start/end. `nightly-coverage.ps1` owns pinned diagnostic parsing; add bounded timing extraction beside it without changing verdict parsing. | `RcFullTimingContractTests.DiscoveryIsNotCopiedFromSuite` uses distinct suite/chunk stamps and missing chunk markers; baseline copied constant must fail. `SetupPhasesAreNonoverlapping` verifies planning/build costs are not summed once per chunk. CP-1. | Do not equate host init or session-start-to-first-test with discovery. If pinned MTP lacks unambiguous begin/end markers, retain null and an explicit remainder; TestDesign must inspect one fresh native diagnostic before choosing a hook. No extra discovery launch per chunk. |
| A6 result/timing joins: sections 2, 5, 54 zero counts; 81 torn lines | Repair aggregate readers/writer finalization in `test-phase-evidence.ps1`, `tests/Shared/TestTiming.cs` and `TestTimingRegistration.cs` as attribution requires. Join native test id from TRX `C1514_NATIVE` StdOut to phase records, bound by run/attempt/suite/chunk/assembly; TRX owns counts/outcomes. | `RcFullTimingContractTests.CountsAndJoinsPreserveEvidence` feeds >200 ms worth of bounded input, truncated final JSONL, duplicate native markers and same chunk ids across suites. Assert completed rows retained, missing is unknown, torn input incomplete, no false zero or pass. CP-1; extend `TestPhaseSummaryTests` regressions. | Keep current 200 ms inline read bound; if insufficient, stream remaining aggregation after child exit with a separately measured finite budget rather than widening it silently. Partial evidence stays incomplete. Retain client JSON per-case durations and script census per-script walls; no invented TRX for either lane. |

All A records live under the invocation's ignored `TimingRoot`/`LogRoot`, including timeout/no-TRX paths; add collector quality and unobserved-phase flags. Freeze sample source/assembly digests and record platform, host, runtime, load, lane, category, run and attempt. No historical output is rewritten. A1/A4 native proof requires Windows; portable seam tests cannot discharge it.

## B. Cheap safe wins after attribution

- Exit delay: 114.2 min measured; **estimated 60-110 min** recoverable after identifying the owner. Never release custody early.
- Group 105: 7.8 min active then 52.4 min idle; prove the hang repair and derive a watchdog from real healthy chunk distributions. The reported 35-40 min suggestion is not twice the largest 32.3 min healthy chunk.
- Packing: 138 profile chunks paid repeated fixed cost; **estimated 35-45 min** before exit-delay improvements, subject to isolation and exact coverage census.
- Setup sharing: about 47.1 min observed; **estimated 20-40 min** only if fixture stamps identify reusable setup. Fresh startup tests retain fresh instances.

Savings overlap; no additive overall promise. Slow individual tests (854.5 s maximum), 45 tests near 30 s and E2E's 220.9 min are follow-up profiling candidates, not proven sleep removals.

## C. Risky parallelism trial

122 serial Antiphon chunks consumed 657.5 min. Historical **estimated 150-330 min** saving is an upper opportunity, not acceptance evidence. Trial nativeActiveMax 2 OR ProcessSpawnLimit 2 in measured Debug with the same SHA, exact test roster and comparable host load. Specify repeat count, flake acceptance, resource isolation and rollback before Code. Do not parallelize E2E or co-schedule Antiphon.Tests with Antiphon.Agents.Pty.Tests as part of this trial.

## D. Contracts that must not change

Full discovered-test census, exact selected identities, exclusions and executed coverage ledger remain mandatory. Existing failed receipts stay failed. Frozen rc source/binaries and attempt identity remain sealed; reviewed fixes require a fresh candidate/attempt. No tags, publish, rc rebase or product code edits in this Plan task.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards) and [R1-R9](../../test-performance-standards.md). This documentation task runs no builds/tests. Future checkpoints need per-row phase costs, measured p95 and approximately 2x timeouts; missing samples are provisional, never fabricated. Every expected/observed selection over 30 minutes requires a Debug task. Shared expensive setup belongs once per run/assembly, with test isolation preserved. Measurement locations and bounded sampling will be completed with the checkpoints.

## Verification handoff

Plan review by a different company's model is requested by the brief. Verification remains a separate TestDesign stage after review; this skeleton is not executable Code authority.
