# CARD-1893 collector hardening — Final Review, round 1

Verdict: CLEAN. No behavior blocker. Advisories below do not hold the land.
Landing owner: `f99cc46c-d77b-45de-ab1f-a409e2ed1469`.
Reviewed source: `0f1f7bf4892a1dfa8005f910917b0885ccddb571` on `feat/card-task-92c62298`
(fast-forward of `origin/feat/card-task-74b623f9`).
Range: `48a4615dcb4d67033886a3e31629dc7351ed0852..0f1f7bf4892a1dfa8005f910917b0885ccddb571`.
Production change: `40730e2ebae97b8060bece919fb7cbd441da9573`. The tip adds the Code receipt note.
`reviewedSourceClean` is false. The only invalid receipt is CP-3 `skipped=1`
(`C1210_Windows_linux_only_outcomes_are_exact` NotExecuted). Every other required receipt validated.
PC-1 through PC-6 stay pending. Restart: none.

## Questions

1. Path safety. `HostLoadStoragePath.Resolve` returns a fully qualified path or throws
   `ArgumentException` with a fixed token. Empty or relative local-application data is skipped.
   A fully qualified local-data path becomes `local/antiphon/host-load`. Otherwise a fully
   qualified profile becomes `profile/.local/share/antiphon/host-load`. A configured value must
   already be fully qualified; `Path.GetFullPath` normalizes it and the worker does not replace it.
   The worker resolves on each retry with `SpecialFolderOption.DoNotVerify` and no longer binds
   `HistoryPath` at startup. Open and resolve failures call `ReportFailure` (stage, path class,
   error class, writable false, fixed action, failures counter, Warning at most once a minute).
   The spool still rejects a reparse-point root (`host_load_spool_link`).

2. Diagnostics. Records are `HostLoadCollectorDiagnostic(At, Stage, PathClass, ErrorClass, Writable, Action)`
   and long counters. Recent queue length is 8. The warning template interpolates only those tokens
   and the dropped count. The 503 problem attaches that record and does not copy the `IOException`.
   `Repeated_errors_are_capped_rate_limited_and_scrubbed` passed: recent length 8, log lines exactly 2
   across twenty retries, planted secret and path absent, JSON under 6000 characters.

3. Cursor and hot path. `Encode` serializes `Cursor(Store, From, Through)` plus HMAC.
   `cursorAdvances` increments only after a successful ledger `Persist`, with rollback on throw.
   A duplicate read of the same cursor does not increment it. `TryCapture` takes one existing
   diagnostic lock and increments `accepted` for an accepted sample. Sampling cadence is unchanged.

4. Scope. The range touches the collector, its contracts, `HostStatsRoutes`, `HostStatsStore`,
   `C1893CollectorDiagnosticsTests`, `tests/derived-impact-index.json`, `docs/testing-and-build.md`,
   and this plan. Settings, broker endpoint, deployment, retention, and compose are untouched.
   No assertion was deleted or loosened. `SessionRunnerContracts` gains the existing `/host-stats`
   collector field the plan allows.

5. Witnesses. At `068308a85de9b8aab200111b4bf15fc8365a97d5` (last red commit) the five witnesses
   that existed there executed and failed on assertions: missing `HostLoadStoragePath`, and a null
   `Diagnostics` property. At the reviewed tip the same class is 7 passed inside CP-2 (52/52).
   Removing the cap or interpolating the exception message would fail `Repeated_errors`. Deliberate
   mutants were not run; PC-1..PC-6 stay owed.

## Verification at the reviewed SHA

Linux, `UseAppHost=false`, `TEMP=/tmp`, load under 16 before each row, one slot per row.
`scripts/run-checkpoint.ps1` (the checkpoint tool's nine-minute deadline had already killed cold
`Antiphon.Tests` builds; this script records `buildSource=verified`).

```
CHECKPOINT CP-2 commit=0f1f7bf4892a1dfa8005f910917b0885ccddb571 build=ok filter=/*/*/(C1893CollectorDiagnosticsTests*)|(C1514HostLoadSpoolTests*)|(C1514HostLoadWireTests*)|(C1514HostLoadSampleTests*)|(C1514HostLoadAuthorityTests*)|(HostStatsEndpointTests*)|(HostStatsSamplerTests*)|(HostStatsStoreTests*)|(RunnerCapabilitiesTests*)/* executed=52 passed=52 failed=0 skipped=0 trx=/work/worktrees/ws-03/.antiphon/checkpoints-r92c62298/CP-2-20261116-205603-9448/run.trx slot=granted waited=75s dirty=0 source=0f1f7bf4892a1dfa8005f910917b0885ccddb571 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=0f1f7bf4892a1dfa8005f910917b0885ccddb571 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)/* executed=114 passed=114 failed=0 skipped=1 trx=/work/worktrees/ws-03/.antiphon/checkpoints-r92c62298/CP-3-20261116-212129-b78f/run.trx slot=granted waited=0s dirty=0 source=0f1f7bf4892a1dfa8005f910917b0885ccddb571 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=0f1f7bf4892a1dfa8005f910917b0885ccddb571 build=reused filter=/*/*/(HostTopConsumerUpgradeReplayTests*)|(C1514HostLoadHistoryTests*)/* executed=26 passed=26 failed=0 skipped=0 trx=/work/worktrees/ws-03/.antiphon/checkpoints-r92c62298/CP-4-20261116-212516-11c0/run.trx slot=granted waited=30s dirty=0 source=0f1f7bf4892a1dfa8005f910917b0885ccddb571 sourceState=clean buildSource=verified
CHECKPOINT DI commit=0f1f7bf4892a1dfa8005f910917b0885ccddb571 build=reused filter=/*/*/DerivedImpactIndexTests/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/ws-03/.antiphon/checkpoints-r92c62298/DI-20261116-213135-cbed/run.trx slot=granted waited=0s dirty=0 source=0f1f7bf4892a1dfa8005f910917b0885ccddb571 sourceState=clean buildSource=verified
CHECKPOINT TIMING commit=0f1f7bf4892a1dfa8005f910917b0885ccddb571 build=reused filter=/*/*/TestTimingOperationLabelTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-03/.antiphon/checkpoints-r92c62298/TIMING-20261116-213413-3a50/run.trx slot=granted waited=0s dirty=0 source=0f1f7bf4892a1dfa8005f910917b0885ccddb571 sourceState=clean buildSource=verified
CHECKPOINT IMPACT commit=0f1f7bf4892a1dfa8005f910917b0885ccddb571 build=reused filter=/*/*/ImpactedClassesSelectorTests/* executed=50 passed=50 failed=0 skipped=0 trx=/work/worktrees/ws-03/.antiphon/checkpoints-r92c62298/IMPACT-20261116-213432-c41c/run.trx slot=granted waited=0s dirty=0 source=0f1f7bf4892a1dfa8005f910917b0885ccddb571 sourceState=clean buildSource=verified
CHECKPOINT GUARDS commit=0f1f7bf4892a1dfa8005f910917b0885ccddb571 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(NativeLinuxPolicyWitnessTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=53 passed=53 failed=0 skipped=0 trx=/work/worktrees/ws-03/.antiphon/checkpoints-r92c62298/GUARDS-20261116-213830-a1d2/run.trx slot=granted waited=0s dirty=0 source=0f1f7bf4892a1dfa8005f910917b0885ccddb571 sourceState=clean buildSource=verified
CHECKPOINT RED commit=068308a85de9b8aab200111b4bf15fc8365a97d5 build=ok filter=/*/*/C1893CollectorDiagnosticsTests/* executed=5 passed=0 failed=5 skipped=0 trx=/tmp/c1893-red-068308a85/.antiphon/checkpoints-r92c-red/RED-20261116-214355-2b02/run.trx slot=granted waited=0s dirty=0 source=068308a85de9b8aab200111b4bf15fc8365a97d5 sourceState=clean buildSource=verified
```

`validate-checkpoint-receipt.ps1 -ExpectedSourceSha 0f1f7bf4892a1dfa8005f910917b0885ccddb571` returned
`CHECKPOINT SOURCE VALID` for CP-2, CP-4, DI, TIMING, IMPACT, and GUARDS.
CP-3 returned `CHECKPOINT SOURCE INVALID reason=receipt_failed` with `skipped=1`. The NotExecuted
result is `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`.
An earlier CP-3 attempt (`CP-3-20261116-210210-25f3`) failed one test because
`Antiphon.DerivedImpactIndex.dll` was absent under `-NoBuild`. The driver was then built and that
row was re-run. The passing receipt is `CP-3-20261116-212129-b78f`.

GUARDS class counts: TestClassificationPolicyTests 42, TestClassificationGuardTests 1,
SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3.
CP-2 includes all seven `C1893CollectorDiagnosticsTests` methods, outcome Passed.
CP-4 is HostTopConsumerUpgradeReplayTests and C1514HostLoadHistoryTests, 26/26.
Pins match the tree: policyHash `c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909`,
PinnedInputDigest `2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`,
LegacyExclusionDigest `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`.
`NativeLinuxPolicyWitnessTests` passed inside CP-3 and GUARDS.
`C1729LayeringGuardTests.Repository_graph_satisfies_the_committed_allowlist` passed inside CP-3
(`Unlisted` empty and `Passes` true). Code's earlier receipt recorded
`VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`. This run did not keep that stdout line.
`DerivedImpactIndexTests.committed_index_matches_the_builder` passed, and the regenerate script
printed `derived-impact windowsOnly count=31 linuxOnly count=1`.

Derived-index driver built once, then `regenerate-derived-impact-index.ps1 -NoBuild` twice on the
tip and twice against merge tree `d7503775e57ea1558292851768e7e852efb97cda`. All four reported
`derived-impact-index unchanged` (windowsOnly 31, linuxOnly 1). The merge index bytes did not change.
`git merge-tree --write-tree origin/master HEAD` exited 0. The merge tree differs from the reviewed
tip only by the two CARD-1883 documentation files, so the tip CP-3 and GUARDS receipts cover the
merged collector and guard code. The full guard union was not executed a second time on that tree.

`scripts/check-evidence-diff.ps1` over `48a4615dcb4d67033886a3e31629dc7351ed0852..0f1f7bf4892a1dfa8005f910917b0885ccddb571`:
`EVIDENCE result commits=10 entries=0 violations=0`.
Downgrade selector: the range has no migration file. Impacted-classes kinds were `host-stats` only,
with owed/capped `SessionRunnerContracts.cs` count=406. Downgrade count 0.
OS-witness lint exit 0 (`/tmp/c1893-os-witness.json`). `GetFolderPath` has no lexical OS token;
`HostLoadCaptureService` is recorded os-gated with witness `W-default-path` (portable-forced,
checkpoint CP-2, production type `HostLoadCaptureService`).

## Advisories (do not hold the land)

- The resolver requires a fully qualified candidate. It does not also compare that path with
  `AppContext.BaseDirectory`. On this host `Production_factory` observes the resolved default
  outside the application directory. A fully qualified special-folder path that itself lay under
  the content root would still be used. An operator-configured absolute path is kept as named.
- `Directory.CreateDirectory` follows intermediate links. The spool rejects a reparse-point final
  directory after creation.
- `Production_factory` invokes `Resolve` and does not start the worker. `Unwritable_default`
  drives the worker open path for a configured absolute blocked directory.
- The scrub witness denies two planted strings. Emission is the fixed-token record above.
  PC-2 remains the scrub-bypass control.
- `Diagnostics` copies its dictionary under the same lock when a reader coincides with a sample.
  The sampler still accepts one sample per existing cadence.
- `C1893CollectorDiagnosticsTests` carries `[Timeout(10_000)]` without a `CancellationToken`
  (TUnit0015). Behavior is unaffected.
- `HostStatsSettings.Validate` still accepts a nonempty relative `HistoryPath` at bind time.
  The worker classifies it `configured-relative` and throws on each retry.

PC-1 default path regression, PC-2 scrub bypass, PC-3 rate-limit bypass, PC-4 record-cap bypass,
PC-5 drop/append counters, PC-6 persisted cursor counter: all pending Mutation.
