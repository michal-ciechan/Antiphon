# CARD-1281 cheap-slice checkpoint manifest

This is the importable copy of the checkpoint table in the [verification design](2026-10-15-card-1281-pipeline-speed-plan.md#verification-design). The parent plan retains an earlier planning inventory; its first Checkpoints heading is not executable. Keep these rows identical to the verification appendix.

Status: **design only; next Plan**. Resolve B-1/B-2/B-3 before commissioning the dependent S2/S3/S4 rows. S5 is portable guard preflight, not live qualification. Methods in the three proposed C1281 test classes do not exist yet. The parent owns the recipes, oracles, 64 pending PCs, operating gates and exclusions.

Run one committed slice group at a time with the checkpoint tool and the host build-slot gate. Use `--after S2`, `--after S3`, `--after S4` or `--after S5`; never run the whole manifest against a partially implemented slice. Reuse builds only within that group. Source must be committed and frozen, with exact SHA receipts. Observe actual result rosters; Min counts TUnit executions, not internal matrix/assertion rows.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1281-a/` | advisory-unit | `/*/*/*/*[Category=Unit]` | R-1 | >=1 executed; full Unit roster, 0 failed/skipped | 1 | 7 |
| CP-2 | S2 | CP-1 | advisory-acceptance | `/*/*/(AdvisoryChangePolicyTests*)\|(StageOutcomeSummaryTests*)/*` | V-1,V-2,V-3,V-4,V-5,V-6,V-7,R-1 | A 7 + summary 5 methods, all variants, 0 failed/skipped | 12 | 6 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1281-b/` | handoff-unit | `/*/*/*/*[Category=Unit]` | R-2 | >=1 executed; full Unit roster, 0 failed/skipped | 1 | 7 |
| CP-4 | S3 | CP-3 | handoff-acceptance | `/*/*/PipelineHandoffPilotTests/*` | V-8,V-9,V-10,V-11,R-2 | 12 executions, every cut reached, 0 failed/skipped | 12 | 12 |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c1281-c/` | close-unit | `/*/*/*/*[Category=Unit]` | R-3 | >=1 executed; full Unit roster, 0 failed/skipped | 1 | 7 |
| CP-6 | S4 | CP-5 | close-acceptance | `/*/*/(PublishedCardCompletionTests*)\|(CardWorkTransitionServiceTests*)/*` | V-12,V-13,V-14,V-15,V-16,V-17,V-18,R-3 | C 7 + transition 13 methods, all variants, 0 failed/skipped | 20 | 8 |
| CP-7 | S5 | `tests/Antiphon.Tests -> bin-c1281-d/` | qualification-admission | `/*/*/ReleaseGateActivationTests/(C599_Admission*)\|(C599_QueuedReadiness*)` | V-19,R-4 | 2 methods, every negative and valid control, 0 failed/skipped | 2 | 6 |
| CP-8 | S5 | CP-7 | qualification-credit | `/*/*/ReleaseGateRunTests/C599_CreditVerdicts` | V-20,R-4 | 1 execution; 24 named harness assertions, 0 failed/skipped | 1 | 2 |
| CP-9 | S5 | CP-7 | qualification-baseline | `/*/*/InterimVerificationPolicyTests/C544_BaselineCompletion` | V-21,R-4 | 1 execution, Found Full control admits, 0 failed/skipped | 1 | 3 |
| CP-10 | S5 | CP-7 | qualification-final | `/*/*/InterimVerificationLandGuardTests/C544_FinalFullRequired` | V-21,R-4 | 1 execution, all scope variants, 0 failed/skipped | 1 | 3 |
| CP-11 | S5 | CP-7 | qualification-watchdog | `/*/*/NightlyWatchdogCoreTests/(C545_IntentBeforeNetwork*)\|(C545_ReceiptIdempotent*)\|(C545_ReaderHeldWithoutSession*)` | R-4 | 3 executions, all assertion rows, 0 failed/skipped | 3 | 2 |

### Cost

Estimated ordinary Code floor: 63 minutes (S2 13, S3 19, S4 15, S5 16), including 16 minutes build/setup. The parent's 453-minute Mutation floor and live qualification budget are separate; these rows run no PCs or live operations.

