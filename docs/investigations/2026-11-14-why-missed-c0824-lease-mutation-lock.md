# CARD-1926: stale C0824 lease mutation-lock witness

The live Windows run `34d41b7e`, `session-runner/all-group-2`, failed
`C0824WorkspaceSlotCustodyTests.C0824_LeaseWaitsForTheDispatcherMutationLock`.
The product behavior is intentional: CARD-1892 removed Input and ConditionalInput
from `PhoneHomeCommandDispatcher.MutateAsync`; their existing per-session input
gate remains responsible for input serialization. WorkspaceMirror still takes
the dispatcher mutation semaphore. The fixture incorrectly treated a held Input
as proof that semaphore was held.

## Why the earlier checks missed it

CARD-1892 round 2's CP-1 selected `C1677RetentionDispatchTests`,
`PhoneHomeCommandDispatcherTests` and `C1722GitTimeoutBudgetTests`, but not the
slot-custody class. Round 3 retained that runner evidence and selected server-side
attention/recovery classes. Its Reviews therefore never executed this dependent
runner witness. See the [parent selection](../superpowers/plans/2026-11-13-card-1892-input-uncertainty.md),
[round-3 selection](../superpowers/plans/2026-11-13-card-1892-round3.md) and
[round-3 evidence](../superpowers/plans/2026-11-13-card-1892-round3-evidence.md).

This was a cross-project selection gap: a SessionRunner-side test depends on the
lock behavior changed in the server delivery work. The CARD-1892 sweep on
`origin/feat/card-task-15406ecc` measured 44 reverse consumers of
`src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`, with 19 beyond the
25-class cap. The committed index at both the round-3 tip `15afa484617287e68cbbd7e2ab15044305f53f80`
and this repair's base already retains `C0824WorkspaceSlotCustodyTests`.
Consequently the cap itself does **not** explain this particular omission:
the named checkpoint execution failed to carry this dependent class through.

The cheapest pre-land check was the single lease-lock method, followed by the
whole custody class when changing the dispatcher lock owners. A hand-maintained
impacted-classes row for the dispatcher path should select the custody class
even for Merge's hand-only selection, independent of derived ranking.

## Baseline proof before repair

Assigned branch `feat/card-task-0055a160` started clean at current origin/master
`21c9f51b4a01c3bd07547c46a5e6da4e58e6c243`. The native-output SessionRunner build
passed (193 warnings, zero errors; build 45.52 s, slot wait 60 s).

The exact method ran alone on that unmodified base: **1 executed, 0 passed,
1 failed, 0 skipped**, exit 2, TUnit wall 1.798 s, test 0.778 s; slot wait 90 s.
It failed at the required assertion:

```text
entered.Task.IsCompleted should be False but was True
the lease entered the pool while the mutation lock was held
```

Command (each build/run takes a separate host slot):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1926-base-build -- dotnet build tests/Antiphon.SessionRunner.Tests --nologo -p:UseAppHost=false
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1926-base-red -- dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/C0824WorkspaceSlotCustodyTests/C0824_LeaseWaitsForTheDispatcherMutationLock' --report-trx --report-trx-filename base-red.trx --results-directory .antiphon/task-0055a160/base-red
```

Ignored evidence: `.antiphon/task-0055a160/`, including `base-build.log`,
`base-red.log` and `base-red/base-red.trx`. Initial `/proc/loadavg`:
`9.08 11.05 16.81 9/3949 12560`; red admission sample:
`21.25 17.18 18.00 6/4067 18692`. TRX retains individual duration and membership;
unrecorded fixture/external-call phases remain unknown.

## Repair and validation

The repaired fixture dispatches a genuine non-slot WorkspaceMirror and holds it
at the existing `WorkspaceAdmissionForTest` hook, inside `MutateAsync`. The slot
mirror must stay outside the pool until that owner is released. Every original
serialization assertion and timeout remains; a finally block releases the gate
and joins both operations after assertion failure. The obsolete Input-gate
implementation was removed from this class's private runtime helper. Production
locking is unchanged.

`tests/impacted-classes.json` now has a `workspace-mutation-lock` row for
`src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs` selecting the entire
custody class. A direct `-MergeSubset -Path` check resolved
`CP-merge-subset-sessionrunner` to
`/*/*/(C0824WorkspaceSlotCustodyTests*)/*`, independently of derived selection.

The index driver was built before regeneration. The first generation printed
`derived-impact-index changed`; the second printed `derived-impact-index unchanged`.
The index was committed separately. Promoting custody to the hand list freed a
derived slot for `RunnerMultilinePromptDeliveryTests`; no dependency was discarded.

Commits:

- Baseline red evidence: `8101b3d17f64fac6e4e14e1ad7acee782506aeee`.
- Test repair and hand row: `05d3c92065f8040a9c960a1f8df2a306dc893fef`.
- Regenerated index and tested source: `658aa3885c98bd4f95b7270fd9467021ebcea010`.

Both test binaries reported product version
`1.0.0+658aa3885c98bd4f95b7270fd9467021ebcea010`. These are direct Debug
measurements, not a validated checkpoint/Review receipt or a reviewedSourceClean
assertion. Subsequent narrative changes do not change the tested executable inputs.

| Row | Result | Test/build duration | Slot wait / held |
|---|---|---|---|
| Base runner build | 0 errors, 193 warnings | 45.52 s | 60 / 47 s |
| Base exact method | 0 passed, 1 expected failure, 0 skipped | 1.798 s | 90 / 4 s |
| Index driver build | 0 errors, 0 warnings | 5.80 s | 30 / 7 s |
| Index generation 1 | changed, exit 0 | included in held time | 60 / 35 s |
| Index generation 2 | unchanged, exit 0 | included in held time | 45 / 40 s |
| Repaired runner build | 0 errors, 193 warnings | 26.81 s | 0 / 28 s |
| Runner slots and guards | **211 passed, 0 failed, 0 skipped** | 42.994 s | 0 / 45 s |
| Antiphon guard build | 0 errors, 2054 warnings | 337.40 s | 0 / 339 s |
| Antiphon guards and selector | **155 passed, 0 failed, 1 skipped** | 518.198 s | 0 / 522 s |

All rows were serial, each build/run obtained its own host slot, and every row
finished below ten minutes. Direct builds used native default outputs and
`UseAppHost=false`; runs used the same property and `--no-build`. Source remained
frozen during builds and tests. The guard batch's 8m38s runtime is an observed
single sample, not a p95; it did not require a retry or a timeout change.

The runner TRX expands to these class counts: custody **19**, cold reset 12,
native rehome 1, overflow 12, process scan 3, rehome 18, reset 10, slot timeout
budget 20, C1722 Git timeout budget 98, retention dispatch 14, native Linux
policy witness 3, classification guard 1. The exact repaired lease-lock method
passed in **2.4244506 s**. Retention dispatch's Input/ConditionalInput independence
witnesses remain green.

The Antiphon TRX expands to classification policy 42, classification guard 1,
slow registry merge 5, slow tripwire 2, native Linux policy 3, derived index 27,
mechanical contract 11, platform guidance 5, layering guard 10, lane-category
guard 1, Windows admission 7 and impacted selector **42**. The one skip is
`WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`:
its child skip-roster audit requires Windows placement. No C1620 failure was
observed in this selection. This is Linux evidence; no native Windows audit or
operation on live run `34d41b7e` was performed.

Exact filters, used with the gated `dotnet run --project <project> --no-build
-p:UseAppHost=false -- --treenode-filter <filter> --report-trx` form above:

```text
tests/Antiphon.SessionRunner.Tests
/*/*/(C0824WorkspaceSlot*)|(C1722GitTimeoutBudgetTests*)|(C1677RetentionDispatchTests*)|(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)/*

tests/Antiphon.Tests
/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(ImpactedClassesSelectorTests*)/*
```

Fresh results are `runner-slots/runner-slots.trx` and
`antiphon-guards/antiphon-guards.trx` under the ignored evidence root above.
Individual durations/membership are in TRX; Antiphon phase records are under
`timing/`. Uninstrumented fixture/external-call phases remain unknown, not zero.
Runner admission load was `15.34 16.22 16.88 5/3717 19310`; its completion sample
was `10.22 14.69 16.33 6/3721 9831`. Guard admission/completion samples were
`26.24 27.56 22.20 17/3745 8166` and `10.73 18.59 21.24 6/3761 29465`.
The observed loaded-build sample peaked at `41.12 23.26 19.14 83/4153 27210`.

Both commissioned nightly pins were verified unchanged: policyHash
`ecc5d4fabc326645bf8b7e7970cdc22c1a9d195a83e737a66ccf157f9cb0c0c1` and
the computed protected-input digest
`67e50252fffbb65df7cf45d9187bb8a9cdaaec9ef12333103b883c60f313f3b8`.
None of their protected inputs changed, so no re-pin was needed. No live host,
desktop, Docker volume, restart or production source was changed.
