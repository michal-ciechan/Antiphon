# Why missed in rc run 3528e6b1

Windows Full on `rc/2026-11-30` at `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` reported one failure in 1,257 results in `session-runner-all-group-1.trx`. The failing method was `C1722GitTimeoutBudgetTests.Budget_table_covers_every_git_method`.

## Cause

C1722 reflects the public `[Test]` methods of eleven named SessionRunner test classes and subtracts the nine named pure methods. It compares those names with the `[Arguments]` rows of `Git_budget_covers_measured_method`. The compiled metadata is available with any TUnit filter. At the failing tip there were 99 required methods and 98 distinct rows. A programmatic diff of the original assertion lists at the rc tip matched the source census. The only discrepancy was the missing row for `C0824WorkspaceSlotColdResetTests.C0824_QualificationFenceDefersColdAndKeepsWarm`. The fence witness runs real Git through its slot fixture and inherits its class's 60 s timeout.

The Windows rc profile applies per-suite exclusions, including the SessionRunner live and native-Linux cases, before packing the remaining discovery leaves into launch groups. `LaunchPlanner.ProfileLaunchGroups` packs classes by namespace and estimated cost, and splits oversized classes by method. The dispatch schedule reorders references to the prepared chunks while preserving their membership. `Invoke-NightlyScheduledChunk` launches a separate test process with the chunk's class or method tree filter. C1722 belongs in the normal SessionRunner Unit roster and can run in group 1 regardless of where its referenced classes are scheduled. [Launch planner](../../scripts/lib/Antiphon.Nightly/LaunchPlanner.cs) · [Dispatch schedule](../../scripts/lib/Antiphon.Nightly/DispatchSchedule.cs) · [Chunk launch](../../scripts/lib/nightly-tests-impl.ps1)

A search of C# test code for recorded coverage, test-identity collection and assembly/session hooks found zero assertions that require other tests in the same project to execute and record their calls. `C0824WorkspaceSlotTimeoutBudgetTests.C0824_BudgetTableCoversEveryCustodyCase` is the closest match and also compares reflected attributes. [C0824 budget census](../../tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotTimeoutBudgetTests.cs)

## Linux reproduction

At the original commit, the exact C1722 method ran alone and failed (one result). The diagnostic-only build at `f74a3177c5bcb192d53deba896a12da101e05e2e` ran `/*/*/C1722GitTimeoutBudgetTests/*` with 99 passed and one failed. The concise assertion named the same missing row as the source census. The unfiltered run used this same build and reached the requested 570 s timeout. TUnit reported 235 passed, four skipped and one failed among 240 reported results, then aborted without a TRX or a C1722 outcome. The recorded failure was `Card1619LinuxCustodyStopIntentTests.Dead_host_recovery_records_intent_and_remains_unknown` with `verification_custody_identity_mismatch`. That method alone reproduced the same failure in the detached checkout of the original commit (one result).

The fix records the fence-test sample and prints only missing, unexpected or duplicate rows on a future mismatch. Five isolated Linux runs of the exact fence method at the diagnostic commit all passed. TRX durations were 0.9385284, 1.1705055, 1.1505079, 0.9292346 and 0.8098774 s. The nearest-rank sample p95, rounded up to milliseconds, is 1.171 s. The existing 60 s Git floor covers the budget. The next CARD-1722 Windows timing collection can measure the same case on the qualification host.

The C1722 class passed all 101 results at `0f96d965b946f7f2cf671396478c56c2cff9a3e6` (99 budget rows and both census checks). All Linux builds and runs used the host build-slot wrapper and `ANTIPHON_LINUX_COMPILER_POLICY=0`.

## Introducing commit/card

CARD-0824 S3 commit `a7e595705faec9e72544287f48c0d43bc66d3146` added the fence witness without its budget row. The affected fixture and budget table were identical on the failing rc tip and the master tip `6be80dcccb9a9748f46d7c77f9c418d80e32f8bf`.

## Approving reviews

- Review `7951bfb5` (round 1, Final) ran CP-2 `C0824WindowsSlotHolderTests/*`, CP-3 `C0824WindowsSlotLongPathTests/*`, CP-4 `C0824WindowsSlotAdmissionTests/*`, and a supplemental runner selection of all `C0824*` classes plus `NativeLinuxPolicyWitnessTests` and `TestClassificationGuardTests`. The supplemental selection reached the fence witness and included the separate C0824 custody budget guard. [Round 1 report at 351140f132](https://github.com/michal-ciechan/Antiphon/blob/351140f132c9620092b4fb8e38f85a97eb4911ec/docs/superpowers/reviews/2026-11-19-card-0824-s3-windows-slot-review.md)
- The A-1 follow-up Review `1281fbe2` (round 2, Final, Grok) covered the sentinel fix. Its listed selections were CP-4, `C0824WorkspaceSlotServerTests`, `RemoteWorkspaceReadinessTests`, `DerivedImpactIndexTests`, and the five guard classes for classification policy, classification, slow registry, slow tripwire, and native Linux policy. [Round 2 report at 7b22a12e55](https://github.com/michal-ciechan/Antiphon/blob/7b22a12e5541272d78e9dc376f4af078cace8a14/docs/superpowers/reviews/2026-11-19-card-0824-s3-a1-grok-round2-review.md)

## Outside the selection

The failure is in the SessionRunner Unit lane, category `Unit`, in `tests/Antiphon.SessionRunner.Tests/C1722GitTimeoutBudgetTests.cs`. The closed S3 checkpoints selected the Windows holder, longpaths, and admission classes. The supplemental `C0824*` selection omitted `C1722*`. The Code/Review `-Range` selector on the introducing commit includes C1722 through the derived reverse edge from `C0824WorkspaceSlotColdResetTests.cs`. The short `-MergeSubset` selection on the same range omits it. [S3 checkpoint manifest](../superpowers/plans/2026-11-19-desktop-slots-s3-checkpoints.md) · [Impact selector](../../scripts/impacted-classes.ps1)

## Cheapest pre-land check

`tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotColdResetTests.cs` → `/*/*/C1722GitTimeoutBudgetTests/Budget_table_covers_every_git_method`

That exact method covers the missing-row failure in about 0.1 s of test time. Include the derived C1722 selection when a closed checkpoint list changes a fixture covered by the table. A hand-table row for those fixtures would also select the guard in short Merge.

## Exemption

The same missing-budget-row cause was documented for two CARD-1961 reset methods. This occurrence adds a different fixture method and has a different introducing commit and review selection. [Prior why-missed note](2026-11-18-why-missed-session-runner-all-group-1.md)
