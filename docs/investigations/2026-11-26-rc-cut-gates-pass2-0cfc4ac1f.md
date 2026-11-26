# rc/2026-12-06 cut gates, second pass

`PIN=0cfc4ac1ffb41f5af159a0c1db423fe119950267`

The server2 gates passed at the pinned master SHA. SDK 10.0.404 ran the graph audit, index regeneration and checkpoint union with source validation for the guard row from a clean checkout. The Windows group 113 replay remains part of the next Windows qualification. [First-pass evidence](2026-11-26-rc-cut-gates-at-0b07453e4.md)

## Cut gates at the pin

| Gate | Result |
| --- | --- |
| `./scripts/antiphon land-graph-audit --check` | Approved both passes and the docs canary. Digest `2D9312A5DAA6CF5E305A3BF1B5F0697FB83A2A5FE205F17FA5BDD3D5928CC937`, 80 definitions. Lease `770a8e67-54d7-4c5b-8a88-1e9996a3234a`. |
| `./scripts/antiphon regenerate-derived-impact-index` | Printed `derived-impact-index unchanged`. Index SHA-256 `d90f9986634355b9a8ec194fac2c8e01f8950dd42e5f88ca06faa14f69824ba6`. Lease `fe1b9424-e45a-4b67-8cb8-96722807e230`. |
| Checkpoint run `20261126-074134-4c4f` | 317 passed, zero failed, 11 Windows skips. Every selected row and build had a host lease. |

| Row | Coverage | Passed | Failed | Skipped |
| --- | --- | ---: | ---: | ---: |
| CP-1 | Six cut guard classes and `PlanCheckpointClassSelectionGuardTests` | 105 | 0 | 1 |
| CP-2 | Groups 113, 142, 86 and 54 | 58 | 0 | 0 |
| CP-3 | Groups 112 and 54 and both Linux split-8 wakeup variants | 8 | 0 | 0 |
| CP-4 | Group 135 idle capture and native Windows placement | 1 | 0 | 10 |
| CP-5 | Owned Git, custody helper, caller wake and skip roster | 89 | 0 | 0 |
| CP-6 | CARD-2061 real Linux holder transport | 1 | 0 | 0 |
| CP-7 | CARD-2060 Linux placement handshake | 16 | 0 | 0 |
| CP-8 | CARD-2061 rehome and CARD-2060 placement probe and ledger | 39 | 0 | 0 |

CP-1 includes 27 passes for the whole `DerivedImpactIndexTests` class and both passes for `PlanCheckpointClassSelectionGuardTests`. The latter checked 12,635 class operands across 6,922 rows in 893 plans, recognized 575 retired operands and reported zero unresolved operands and zero unused retirements. It skipped 131 entries outside its class-filter scan. The CP-1 Windows skip is `C1210_Windows_linux_only_outcomes_are_exact`.

CP-2 includes all seven `PlanCoverageBindingTests` methods, including `historical_row_timeout_checklist_binds_the_advisory_method` (142.557 s) and `historical_plans_bind_without_suppression` (342.119 s). The Windows replay filter for the whole class is `/*/Antiphon.Tests.Checkpoints/PlanCoverageBindingTests/*`. The qualifying Windows chunk must contain and pass both methods.

The original attempt `20261126-073143-bfeb` ended with exit 7, `owner-unverified`, after the task API returned HTTP 502. Its first build completed with verified source. CP-1 was interrupted before a TRX and the remaining rows lacked execution evidence. Receipt validation returned `cleanup_refused`. The HTTP health and task-owner endpoints both recovered to 200 before the same eight filters ran again with fresh output paths.

The CP-7 stage was compiled from the pinned checked-in C file with `-DANTIPHON_PLACEMENT_FIXTURE` under lease `b1aeb477-7a52-4ff5-9dcb-faa1988f5d0f` in the local CARD-2060 compiler image. The compiler image, source and binary digests are retained in `fixture-provenance.txt`.

The terminal checkpoint receipt records 50m27s wall time, including 7m30s of admission-only wait and 7m03s of finalization. The validator qualified CP-1 on its own and qualified CP-1, CP-2, CP-3, CP-5, CP-6, CP-7 and CP-8 together. CP-4 passed its Linux idle-capture case with 10 Windows-only skips. The validator returned `skip_identity_unlisted` for CP-4 and for the full eight-row selection because the Windows Job Object skips lack validator roster entries. The CP-4 raw TRX and its source and build provenance are retained with the other rows.

## Master advance during verification

Published master advanced to `1cc664b3e36ac10de7299948e657a2b682e94b81` and then `771bc1a46afe5893894b050f4da47effb6c758e7` during verification. CARD-2079 commit `a9c44a7524c58cdf1b1951797c8429fbc57178ea` changed the guarded `DerivedImpactIndexTests.cs` class. Its companion commit updates `.gitignore` and adds the CARD-2079 investigation. CARD-2081 commits `44a57b269e3d9bbe86ab6945fb566dd465f1cd4c` and `771bc1a46afe5893894b050f4da47effb6c758e7` change `LinuxRunnerCustodyLifecycleTests.cs` and add the CARD-2081 investigation. The generated land-graph and derived-index files are absent from all four commits' changes. The receipt covers the original pin.

## Why group 113 was red on master

The November 25 repair was published on the task branch for `114bddd4`. At the failing master `49a35f12da10d5b53b28f790cd36fefc902f9e6c`, both `8b5a34e4a2daf1b7aed62eecd70a1ee023c52bc9` and `b323bae783534e77fe5982d94f7d3cab6eea8c6d` are absent from ancestry and show `+` in `git cherry 49a35f12 b323bae7 2fd61b64`. The CARD-1005 and CARD-1037 plans and the binding test have identical blobs at the failing master and at the earlier Windows pin `2fd61b643ea4231fa732cd4969b5284d43b33f39`. The CARD-1037 plan, binding test and census fixture at this pass's pin match the payload on `b323bae7`. That payload first arrived on master in `0b07453e48b7eaab1985ec0f7537b056a4b80ed3`.

| Method | Cause at failing master |
| --- | --- |
| `historical_row_timeout_checklist_binds_the_advisory_method` | Unit 20 (`3937ffdb7f4502c1b08bcb97c83e805fd4f10b55`) deleted the declaration selected by CARD-1005 CP-1. The coverage binder returned exit 2 for the unresolved class. CARD-2071 ledgered that historical selector for the class guard. The binding method needed the retired declaration in its own fixture to resolve the row and bind all 177 obligations, including R-4's live advisory timeout method. |
| `historical_plans_bind_without_suppression` | The original Windows failure came from the other unit 20 deletion, `RunCheckpointScriptTests` in CARD-1035 CP-2. CARD-2071 (`0207fa9e9e4f80c6dd9013697a5749359d7a3e99`) copied the CARD-1035 selector repair from task `114bddd4`, so the method proceeded to CARD-1037. Unit 28 (`2159d023d917593a1cb1a5f96d4bcd7a82386bd8`) had moved the live refusal assertions to CLI flags. The old V-1 checklist expected three PowerShell spellings, so coverage returned exit 1. The published repair binds `--worktree`, `--start-ref` and `without --on-agent`. |

CARD-2071 exposed the existing CARD-1037 mismatch by repairing the preceding plan. CARD-2026 (`7356da7dcb839d223efb238bcf7332643a85e7e3`) changed two FT-5 expected counts and added its investigation note. The first pass observed the two outstanding faults after the partial publication.

## Exposure in other plans

At the pin, the retirement ledger contains 38 checkpoint rows in 27 plans selecting at least one of the same two deleted classes. That includes CARD-1005 CP-1 and its peer rows in CARD-1046 CP-5, CARD-0820 and CARD-1199, plus older `RunCheckpointScriptTests` rows such as CARD-0589 CP-4 and CARD-0723 CP-5. Replaying one of those historical rows against current source requires the original source pin or an explicit historical fixture. [CARD-2071 census](2026-11-25-card-2071.md)

CARD-1046 CP-1 exercises the repaired CARD-1035 and CARD-1037 bindings through V-5. Its historical design table still records the original PowerShell spellings. Later selectors of `RemotePoolFollowUpAdmissionTests`, such as CARD-1065 CP-11 and CARD-1143 CP-15, select the class or method by identity. The three literal obligations belonged to CARD-1037 V-1. `PlanCoverageCommandTests.WriteFrozenWorld` combines a frozen plan with eight current source files, all present at the pin. A future change to those sources can affect that replay.

The next Windows qualification must verify the chunk containing both group 113 methods. The prior Windows TRX at `2fd61b64` shows both original failures. This pass verifies the repaired class on Linux against the source intended for Windows.

## Retained receipts

The exact selections, build-slot logs, failed first attempt and successful report with all eight TRX files are in `/work/evidence/rc-cut-pass2-5b7e6588/`. The successful `report.json` SHA-256 is `422cacba281ea40d558f5256303fa71d440dfc2de463e04ed8b1fae17b5ec385`.

```text
CHECKPOINT CP-1 commit=0cfc4ac1ffb41f5af159a0c1db423fe119950267 build=ok filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(DerivedImpactIndexTests*)|(C1729LayeringGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(NativeLinuxPolicyWitnessMutationTests*)|(PlanCheckpointClassSelectionGuardTests*)/* executed=105 passed=105 failed=0 skipped=1 trx=/work/worktrees/ws-03-d4/.antiphon/checkpoints/20261126-074134-4c4f/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=0cfc4ac1ffb41f5af159a0c1db423fe119950267 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=0cfc4ac1ffb41f5af159a0c1db423fe119950267 build=reused filter=/*/*/(PlanCoverageBindingTests*)|(IncrementalSelectionSoundnessTests*)|(StatusSnapshotScriptTests*)|(TaskProgressStallSweepTests*)|(SessionRuntimeActionTests*)/* executed=58 passed=58 failed=0 skipped=0 trx=/work/worktrees/ws-03-d4/.antiphon/checkpoints/20261126-074134-4c4f/rows/CP-2/run.trx slot=granted waited=285s dirty=0 source=0cfc4ac1ffb41f5af159a0c1db423fe119950267 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=0cfc4ac1ffb41f5af159a0c1db423fe119950267 build=reused filter=/*/*/(CheckpointAdmissionContractTests*)|(AgentSessionRuntimeTests*)|(TurnEndGapRestartDeliveryTests*)/(C1616_*)|(C1259_*)|(C488_Wakeup_cut_holds_the_row_only_when_in_process_hints_are_dropped*) executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/ws-03-d4/.antiphon/checkpoints/20261126-074134-4c4f/rows/CP-3/run.trx slot=granted waited=30s dirty=0 source=0cfc4ac1ffb41f5af159a0c1db423fe119950267 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=0cfc4ac1ffb41f5af159a0c1db423fe119950267 build=reused filter=/*/*/NightlyNativeOwnershipTests/* executed=1 passed=1 failed=0 skipped=10 trx=/work/worktrees/ws-03-d4/.antiphon/checkpoints/20261126-074134-4c4f/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=0cfc4ac1ffb41f5af159a0c1db423fe119950267 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=0cfc4ac1ffb41f5af159a0c1db423fe119950267 build=reused filter=/*/*/(LinuxOwnedGitProcessTests*)|(CustodyHelperContractTests*)|(BlockedReplyReportRetentionTests*)|(CallerWakeTests*)|(ExpectedPlatformSkipRosterTests*)/* executed=89 passed=89 failed=0 skipped=0 trx=/work/worktrees/ws-03-d4/.antiphon/checkpoints/20261126-074134-4c4f/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=0cfc4ac1ffb41f5af159a0c1db423fe119950267 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=0cfc4ac1ffb41f5af159a0c1db423fe119950267 build=reused filter=/*/*/C0824WorkspaceSlotServerTests/C2061_SettledQuarantineRoundTripWithLinuxHolder executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-03-d4/.antiphon/checkpoints/20261126-074134-4c4f/rows/CP-6/run.trx slot=granted waited=60s dirty=0 source=0cfc4ac1ffb41f5af159a0c1db423fe119950267 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=0cfc4ac1ffb41f5af159a0c1db423fe119950267 build=ok filter=/*/*/Card2060PlacementHandshakeTests/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/ws-03-d4/.antiphon/checkpoints/20261126-074134-4c4f/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=0cfc4ac1ffb41f5af159a0c1db423fe119950267 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=0cfc4ac1ffb41f5af159a0c1db423fe119950267 build=ok filter=/*/*/(C0824WorkspaceSlotRehomeTests*)|(LinuxCustodyProbeTests*)|(Card2060PlacementLedgerTests*)/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/ws-03-d4/.antiphon/checkpoints/20261126-074134-4c4f/rows/CP-8/run.trx slot=granted waited=15s dirty=0 source=0cfc4ac1ffb41f5af159a0c1db423fe119950267 sourceState=clean buildSource=verified
CHECKPOINT SOURCE VALID source=0cfc4ac1ffb41f5af159a0c1db423fe119950267 rows=1
CHECKPOINT SOURCE VALID source=0cfc4ac1ffb41f5af159a0c1db423fe119950267 rows=7
CHECKPOINT SOURCE INVALID reason=skip_identity_unlisted
```
