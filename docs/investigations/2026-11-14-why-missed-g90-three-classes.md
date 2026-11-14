# g90: three CARD-1892 uncertainty consumers

Debug task `9c1feeba`, base `8cec1e74e75819ef20756c228add987ce4ae4170`.
The Windows extract was read at commit `d403313ab8d631effa851f46dc7cedad356ee8e4`:
`docs/investigations/2026-11-14-run-34d41b7e-g90-three-classes-windows-failure.md`.
Its run `34d41b7e` had 383 results, 380 passed and these three failures.
No live qualification, desktop, production service or production volume was changed.

One note per failing family:

- [PhoneHomeOutageMentionTests](2026-11-14-why-missed-g90-phonehomeoutagemention.md)
- [GrokRulesCompactionRecoveryTests](2026-11-14-why-missed-g90-grokrulescompactionrecovery.md)
- [StandingSessionSwitchConcurrencyTests](2026-11-14-why-missed-g90-standingsessionswitchconcurrency.md)

## Local red evidence

All three whole classes ran serially against the clean, unchanged base above,
using the native-output build with `UseAppHost=false`, the host slot gate and
the assembly's shared PostgreSQL fixture. These are assertion failures, not
build, discovery, fixture, zero-test, polling or timeout failures.

| Evidence directory under `.antiphon/evidence/g90/` | Executed | Passed | Failed | Skipped | Wall including admission |
|---|---:|---:|---:|---:|---:|
| `red-phone-2` | 8 | 7 | 1 | 0 | 87.33 s |
| `red-grok` | 11 | 10 | 1 | 0 | 84.41 s |
| `red-switch` | 24 | 23 | 1 | 0 | 93.16 s |

At witness commit `91fbecb735a3eb2c61ddd8f746f720068edd80ac`, the new
`ImpactedClassesSelectorTests.Input_uncertainty_paths_select_delivery_and_switch_witnesses`
ran three cases, all failed as intended (`red-selector`, 32.00 s). Both service
paths matched no uncertainty kind; the exception path omitted the PhoneHome
class. This independently witnesses the selection defect before its repair.

Each directory retains `run.trx`, `run.log`, source/dirty/load metadata and a
phase journal. Missing phase spans remain unknown rather than zero. Generated
evidence remains ignored. The first local wrapper (`red-phone`) was stopped
before acquiring a lease or starting a test: an unquoted native argument was
expanding the tree filter as a filesystem glob. The corrected wrapper quotes
the filter; that aborted wrapper is not red test evidence.

## Cheapest pre-land selection

Extend the existing `input-delivery-uncertainty` row of
`tests/impacted-classes.json`. It now watches these exact paths:

- `server/Application/Exceptions/InputDeliveryUncertainException.cs`
- `server/Application/Services/SessionMessageQueueService.cs`
- `server/Application/Services/AgentControlService.cs`

Its explicit classes are AgentTaskMidTurnRefineTests,
LandRefusalDeliveryWitnessTests, SessionMessageQueuePhoneHomeDropTests,
PhoneHomeOutageMentionTests, GrokRulesCompactionRecoveryTests,
StandingSessionSwitchConcurrencyTests, CodexCliObservationGapTests and
GrokRulesReadyOrderingTests. The new selector witness checks all three paths
and requires all eight classes on the emitted Antiphon.Tests filter.

The old row, added by `8cec1e74e`, watched only the exception file and named
only the first three classes. The behavior that broke these consumers lives
in SessionMessageQueueService's catch/recovery paths. An edit there therefore
did not select this row, and an exception-file edit still omitted all three
g90 families. The fixed Merge subset is not a substitute for these consumers.
The syntax-derived index is capped; explicit classes also prevent these
witnesses from being displaced by unrelated higher-ranked consumers.

The committed CARD-1892 Code and round-3 evidence enumerate CP-2/4/5/6/8/9
without these three classes. Those closed checkpoints supplied the ordinary
Review selection; none exercised these indirect callers. A distinct original
Merge execution receipt and the full Review transcript were not supplied, so
their actual execution is unknown; this note identifies the recorded roster
and selector omissions rather than inventing a Merge run result.

## Guard and caller audit

Product commit: `dcb068f89c33caebaa543f0413fd254e501b1261`.
The original concurrency failure is its red witness. The same queue semaphores,
transaction, source-pointer recheck, pending-row transfer rules, conflict code
and message remain in place. A separate session-scoped `AnyAsync` also refuses
source rows that are Sent, have no verdict and have at least one attempt.
Such rows are not added to the list of pending messages eligible for transfer.

`StartInteractiveSessionAsync` is the common reservation path for explicit
history, default continuation, explicit Fresh, automatic/supervised Fresh and
capacity recovery. The refusal remains inside `switching`: same-conversation
continuation/resume does not hit it. `ResumeAsync` for a compaction episode
calls `StartAsync` with that episode's session; resuming the current session
therefore remains allowed. Selecting a different session, including automatic
Fresh, intentionally holds while the source delivery remains unresolved.

The new controls prove that Delivered/LateConfirmed source rows permit a
switch, an unconfirmed row on selected history A does not block switching
away from current source B, and a stopped B with an unconfirmed row can resume
B while preserving its attempt. They exercise real reservation and launch.

For a running source, existing fresh/interrupted recovery and composer-less
watchdog reconciliation can accept complete matching UserPrompt evidence as
LateConfirmed. Composer-less recovery does not type or press Enter while the
attempt is unknown, and the next safe flush advances queued work. Existing
`RecordTransportFailureAsync` immediately records the lost reply as an error
incident for the pointed-to agent. Existing overdue composer-less discovery
records `input-delivery-unknown` attention, including while working, and a
later complete receipt may still confirm it.

A stopped session does not receive ordinary running-session delivery recovery;
it may resume itself first. If native history/evidence cannot be recovered,
release is not guaranteed: the existing incident and switch conflict surface
an operator decision hold. `CancelAsync` and the ordinary queue DTO expose
Pending rows only; they must not be described as a way to cancel a Sent
attempt. This repair adds no timer, automatic verdict, cancellation API or
replay authority. Review should attack this stuck-row scenario explicitly.

## Verification continuation

The caller's standing authority authorized the guard widening without another
approval. The expectation/clock repairs and selector expansion are test-only;
the widened guard requires ordinary Review. Final measured rows and source
provenance follow below.
## Final measured verification

All 12 repair/regression rows passed: 422 executed, 422 passed, 0 failed, 0 skipped. Fresh TRX TestDefinitions/TestMethod class identities were checked against all 25 intended classes; no missing class or zero-count row. Every row ran at clean source `369a1faefaa449d2bfaa63e76110b0c056fbb2c3` using the same leased native-output build and `UseAppHost=false`. This final documentation-only evidence update does not change that tested source identity.

| Row | Passed / executed | Wall s | Slot wait s | Driver held s | Per-test p95 / max s |
|---|---:|---:|---:|---:|---:|
| green-switch | 28 / 28 | 52.26 | 0 | 51 | 1.808 / 7.415 |
| green-phone-aged | 8 / 8 | 152.85 | 0 | 152 | 9.767 / 9.767 |
| green-grok | 11 / 11 | 97.33 | 0 | 96 | 9.175 / 9.175 |
| green-standing-resume | 54 / 54 | 110.78 | 0 | 110 | 7.73 / 24.913 |
| green-agent-control | 33 / 33 | 148.92 | 90 | 57 | 2.691 / 6.385 |
| green-standing-recovery | 63 / 63 | 239.16 | 45 | 192 | 9.023 / 44.613 |
| green-land-refusal | 24 / 24 | 307.33 | 195 | 111 | 8.078 / 12.432 |
| green-mid-turn | 39 / 39 | 208.69 | 120 | 87 | 1.154 / 7.354 |
| green-phone-drop | 5 / 5 | 129.5 | 75 | 53 | 9.966 / 9.966 |
| green-selector | 45 / 45 | 394.96 | 150 | 244 | 9.501 / 22.024 |
| green-rules-ready | 5 / 5 | 185.32 | 90 | 94 | 12.181 / 12.181 |
| green-guards | 107 / 107 | 275.46 | 60 | 214 | 8.163 / 48.48 |

Serial row wall sums to 2302.56 s; explicit build-slot admission totals 825 s, and driver leases total 1461 s. This Debug investigates the over-30-minute aggregate: 13m45s was admission and 24m21s was driver time, including discovery and fixtures. Each row remains below ten minutes including admission. These measurements support the caller's existing CARD-1906 host-contention investigation; no extra delegate was spawned. Phase journals retain finer attribution; absent spans are unknown. No timeout was widened or unleased run used. The p95/max values are distributions within this single run, not histories for per-test timeout calibration.

The extra standing-recovery row covers automatic/default resume callers identified by the guard audit. The extra rules-ready row covers a class explicitly named by the expanded uncertainty row. The baseline build, selector red build, index-driver build, final build and PhoneHome-clock rebuild each had a stated invariant/repair reason and a separate host lease; there was no assembly-wide or Unit-lane run.

The static full-range impacted resolver matched input-delivery-uncertainty and test-lane-category: 39 classes, two project filters, no Windows filters. This Debug executes the brief's named regression selection, not that entire derived closure. CodexCliObservationGapTests belongs to the separately commissioned g58 repair; its new selection entry is not credited here as test execution. Native Windows execution remains with qualification.

### Class roster

- green-switch: Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests 28/28.
- green-phone-aged: Antiphon.Tests.Application.PhoneHomeOutageMentionTests 8/8.
- green-grok: Antiphon.Tests.Application.GrokRulesCompactionRecoveryTests 11/11.
- green-standing-resume: Antiphon.Tests.Application.StandingSessionOwnershipTests 5/5; Antiphon.Tests.Application.StandingSessionQueueSwitchTests 9/9; Antiphon.Tests.Application.StandingSessionRecoveryHttpTests 1/1; Antiphon.Tests.Application.StandingSessionSelectionTests 39/39.
- green-agent-control: Antiphon.Tests.Application.AgentControlServiceIntegrationTests 33/33.
- green-standing-recovery: Antiphon.Tests.Application.StandingContinuityRecoveryTests 11/11; Antiphon.Tests.Application.StandingRestartEscalationTests 52/52.
- green-land-refusal: Antiphon.Tests.Application.LandRefusalDeliveryWitnessTests 24/24.
- green-mid-turn: Antiphon.Tests.Application.AgentTaskMidTurnRefineTests 39/39.
- green-phone-drop: Antiphon.Tests.Application.SessionMessageQueuePhoneHomeDropTests 5/5.
- green-selector: Antiphon.Tests.Scripts.ImpactedClassesSelectorTests 45/45.
- green-rules-ready: Antiphon.Tests.Application.GrokRulesReadyOrderingTests 5/5.
- green-guards: Antiphon.Tests.Application.MechanicalPcContractTests 11/11; Antiphon.Tests.Application.TaskPlatformGuidanceTests 5/5; Antiphon.Tests.Infrastructure.C1729LayeringGuardTests 10/10; Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests 5/5; Antiphon.Tests.Scripts.DerivedImpactIndexTests 27/27; Antiphon.Tests.TestHelpers.SlowTestTripwireTests 2/2; Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 42/42; Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests 1/1; Antiphon.TestSupport.NativeLinuxPolicyWitnessTests 3/3; Antiphon.TestSupport.TestClassificationGuardTests 1/1.

### Exact executed filters

```text
green-switch /*/*/StandingSessionSwitchConcurrencyTests/*
green-phone-aged /*/*/PhoneHomeOutageMentionTests/*
green-grok /*/*/GrokRulesCompactionRecoveryTests/*
green-standing-resume /*/*/(StandingSessionSelectionTests*)|(StandingSessionOwnershipTests*)|(StandingSessionQueueSwitchTests*)|(StandingSessionRecoveryHttpTests*)/*
green-agent-control /*/*/AgentControl*/*
green-standing-recovery /*/*/(StandingContinuityRecoveryTests*)|(StandingRestartEscalationTests*)/*
green-land-refusal /*/*/LandRefusalDeliveryWitnessTests*/*
green-mid-turn /*/*/AgentTaskMidTurnRefineTests*/*
green-phone-drop /*/*/SessionMessageQueuePhoneHomeDropTests*/*
green-selector /*/*/ImpactedClassesSelectorTests*/*
green-rules-ready /*/*/GrokRulesReadyOrderingTests*/*
green-guards /*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/*
```

Build once with `pwsh -NoProfile -File scripts/build-slot.ps1 -Label g90-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo`. For each filter above, run serially through `pwsh -NoProfile -File scripts/build-slot.ps1 -Label g90-row -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter "<filter>" --report-trx`, supplying a fresh results directory. Keep quotes around the native tree argument.

### TRX provenance

All paths are under `/work/worktrees/task-9c1feeba/.antiphon/evidence/g90/<row>/run.trx`; generated receipts/logs remain ignored.

- green-switch: SHA-256 `f6267cfd6df2f3bbb1361547b269875d7ef27db067cbadd9aed6c77afe30bc40`.
- green-phone-aged: SHA-256 `e48c6c4fdb3089344e0593889230ca0c7f8c7481497b60a537cf191a61def356`.
- green-grok: SHA-256 `1d16f7ad8473f1a09411813eebbb851f9e4b860a2952c107e6413cd24d40b9cf`.
- green-standing-resume: SHA-256 `d2115d374fea6592d9686660579251ee84af09e6699e4d794e3f3c4c1a70960a`.
- green-agent-control: SHA-256 `a8e1464dd49ab0de4f2c0329a86055e89bc4fbbe6a43d15b34c86a7388553aed`.
- green-standing-recovery: SHA-256 `56e49f4b1129a3147a22efb3fd47c2be8f1f650ca93f5b14cc65d2e5b2a21095`.
- green-land-refusal: SHA-256 `fb79fea5236517f9a1898f8f260461a5363a5d537c8e2ba0c5e0105a1a0b95fe`.
- green-mid-turn: SHA-256 `397b4c5e655ebde431eb36fc2ceb0a3b0a150535053771c5ff61064cc938bf97`.
- green-phone-drop: SHA-256 `7d562137fd25717fe9473e8dac0143d8ea14f326336fe656384df1fb50acec10`.
- green-selector: SHA-256 `f25591fd9cffcb4a6a4187dc8bda653dfeb966ac7b4524c218175cdd4af63eae`.
- green-rules-ready: SHA-256 `16aa2c833a618300a1a6628aecb8993778c8a6f8e9268f0e044d1ace54458123`.
- green-guards: SHA-256 `9ff1cbc3b4234a6d29ffb3e14c3f85319142072a2ede03827b09f89ddb78557e`.

The first repaired PhoneHome run at 9192820aec remains failed (7/8): its previously masked age-gate assumption is explained in the family note. It is replaced only for current repair qualification by green-phone-aged, not rewritten or relabeled. Baseline reds and all three selector reds remain historical failed evidence.

The derived-index driver was built before regeneration; the first generation printed `changed`, the second `unchanged`, and the index has its own commit. Protected nightly/live-lane inputs were unchanged, so neither policyHash nor PinnedInputDigest was re-pinned.

### Host and build provenance

Host `0c7fc078dc23`, Debian GNU/Linux 12 (bookworm); test runtime .NET 9.0.23.
The following are `/proc/loadavg` 1/5/15-minute samples at row start and end:

| Row | Start | End |
|---|---|---|
| green-switch | 15.81 19.55 21.66 | 12.54 18.01 21.01 |
| green-phone-aged | 12.54 18.01 21.01 | 21.49 18.82 20.79 |
| green-grok | 21.49 18.82 20.79 | 21.65 20.38 21.20 |
| green-standing-resume | 20.48 20.16 21.12 | 19.83 19.85 20.89 |
| green-agent-control | 19.83 19.85 20.89 | 35.30 27.54 23.71 |
| green-standing-recovery | 35.30 27.54 23.71 | 17.66 24.35 23.43 |
| green-land-refusal | 17.66 24.35 23.43 | 14.43 21.32 22.68 |
| green-mid-turn | 14.08 21.14 22.61 | 13.43 16.96 20.57 |
| green-phone-drop | 13.43 16.96 20.57 | 32.42 22.45 22.07 |
| green-selector | 32.42 22.45 22.07 | 11.85 18.37 20.95 |
| green-rules-ready | 12.50 18.40 20.95 | 24.94 21.54 21.70 |
| green-guards | 24.94 21.54 21.70 | 12.79 17.58 20.15 |

All five builds passed, using native default output and `UseAppHost=false`:

| Build | Source | Slot wait / held s |
|---|---|---:|
| base-build | `8cec1e74e75819ef20756c228add987ce4ae4170` | 45 / 210 |
| witness-build | `91fbecb735a3eb2c61ddd8f746f720068edd80ac` | 15 / 255 |
| index-driver-build | `c28a06c46d8d85006fdd1f260315ad367b45ba30` | 30 / 4 |
| final-build (before masked PhoneHome age repair) | `9192820aec5a906587acb8e48e2eef9185f06a30` | 15 / 186 |
| phone-clock-build (all final rows) | `369a1faefaa449d2bfaa63e76110b0c056fbb2c3` | 0 / 191 |

Index generation used the separately built driver, one lease per invocation:

```text
derived-impact windowsOnly count=31 linuxOnly count=1
derived-impact-index changed
BUILD SLOT released lease=f7710545-c20c-4bea-97cb-1d5d7aeaec88 held=45s
derived-impact windowsOnly count=31 linuxOnly count=1
derived-impact-index unchanged
BUILD SLOT released lease=674617ee-fd0b-4244-98e7-73fb63feb628 held=38s
```

### Pushed slices and Review handoff

- `6cbfc7e6f0ab332ab54fd600a4f5ab144e53fd1b`: PhoneHome Sent/null-verdict expectation.
- `13c17adc217a897c1246f48ff69c5f567deabf0f`: Grok receipt-only boundary and subsequent flush.
- `91fbecb735a3eb2c61ddd8f746f720068edd80ac`: switch non-blocking controls and selector red witness.
- `dcb068f89c33caebaa543f0413fd254e501b1261`: **product guard widening**.
- `c28a06c46d8d85006fdd1f260315ad367b45ba30`: watched paths and explicit uncertainty witnesses.
- `2cb6fd4e68e2731c1ee8fa21c2ec6fd9d0c40cc3`: deterministic derived-index regeneration.
- `9192820aec5a906587acb8e48e2eef9185f06a30`: initial per-family why-missed notes and caller audit.
- `369a1faefaa449d2bfaa63e76110b0c056fbb2c3`: test-only advance through the unchanged interrupted-attempt age gate.

Next is ordinary Review. Attack the widened guard, its shared-lock/transaction
boundary and every resume/Fresh caller; the stuck-row/attention scenario; and
the Delivered/LateConfirmed, other-session and same-session non-blocking
controls. The expectation/clock and selector/index repairs can land with the
reviewed product change. No mutation-control or native Windows verdict is
claimed by this Debug.
