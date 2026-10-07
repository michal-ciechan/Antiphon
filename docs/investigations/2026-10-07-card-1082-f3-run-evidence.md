# CARD-1082 F3 run evidence and F3b rows

Amendment for the F3 Code report `.antiphon/task-ad0a4d41.md` (session `e694ce74-3e15-4467-966f-d6afdcd5f65d`, commit `4c0f48189bfae15ca2f2c48876e13a97dfd33da2`). The report omitted drivers. This note records what that report, its transcript tool calls, and the retained `c1136-green` terminal log actually show. Leases and counts are stated only where those artifacts state them.

## F3 first-round drivers

Source for the listed commands is the transcript tool-call ledger. The measurement method `C1136_MeasureHeldTickCost` was removed before the commit. The committed tree is `4c0f48189bfae15ca2f2c48876e13a97dfd33da2`.

| Seq | Label | Command | Outcome in the retained record |
|---|---|---|---|
| 115 | c1136-red | `dotnet run` `bin-c1136/` `UseAppHost=false` filter `/*/*/SettlementSyncRecoveryTests/C1136_*` | Command only. The transcript does not retain this driver's stdout, lease, or counts. |
| 142 | c1136-measure-build | `dotnet build` `tests/Antiphon.Tests` `bin-c1136/` `UseAppHost=false` | Command only. Stdout not retained. |
| 145 | c1136-measure | `dotnet run --no-build` filter `C1136_MeasureHeldTickCost` | Command only. The later narration says the pre-fix measurement was attempted 0 and 1 statement for an empty table and for Held rows. |
| 171 | c1136-green-build | `dotnet build` `bin-c1136/` `UseAppHost=false` | Command only. Stdout not retained. |
| 174 | c1136-green | `dotnet run --no-build` filter `C1136_Held*` or the docs method | Retained log: lease `50b498a2-3a35-4dc7-bdeb-4cedb5a1fe72`, waited 15s, held 61s. total 14, failed 1, succeeded 13, skipped 0, duration 56s. The one failure is `C1136_MeasureHeldTickCost`, thrown on purpose (`C1136-SQL empty=0/1 n3-null=0/13 n3-future=0/1`). Narration: 13 real results passed; three null-path Held rows cost 13 statements and were superseded because a blank path returned gone before the retirement query. |
| 212 / 219 | c1136-pc6a, c1136-pc6a-run | build, then `C1136_HeldDebtWithALiveWorktreeStaysHeldAndReschedules` | Code report: failed 1/1, state Superseded, expected Held. Lease `6c40a2ed`, waited 0s. Reverted. |
| 227 / 230 | c1136-pc6b, c1136-pc6b-run | build, then `C1136_HeldDebtEndsSupersededOnceTheWorktreeIsRetired` | Code report: failed 2/2, both arguments stayed Held. Lease `6ad2b50a`, waited 0s. Reverted. |
| 244 / 247 | c1136-pc7, c1136-pc7-run | build, then the live Held method | Code report: failed 1/1, `attempted=1 git=14 attempts=2`. Lease `6a71b606`, waited 0s. Reverted. |
| 258 / 263 | c1136-restored, c1136-restored-run | build, then `C1136_*` | Code report: passed 3/3. Lease `71934018`, waited 0s. |
| 269 | c1136-docs | `dotnet run --no-build` filter `C1082_settlement_sync_debt_is_documented` | Narration: passed in 624 ms. Lease not retained. |
| 286 | c1082fu-checkpoint-bootstrap | `dotnet build` `tools/Antiphon.Checkpoints` `bin-c1082fu-driver/` | Code report: checkpoint run `20261007-095008-cd4b` then used that driver. Bootstrap counts are not in the report. |
| 288 | checkpoint | `Antiphon.Checkpoints.dll run --plan` the follow-up plan `--rows CP-4,CP-5` | Code report: GREEN exit 0. CP-4 executed 13 passed 13 failed 0 skipped 0. CP-5 executed 5 passed 5 failed 0 skipped 0. Source clean, build verified, slot granted, waited 0s. |
| 306 | c1136-regress-build | `dotnet build` `bin-c1136/` | Command only. Stdout not retained. |
| 309 / 311 | c1136-list-fast, c1136-list-or | `dotnet run --no-build --list-tests` | List-only. Not execution. Narration: the combined filter was not trusted and classes were run separately. |
| 318 | c1136-regress | `pwsh -File /tmp/c1136-regress.ps1` inside one slot, `TUNIT_MAX_PARALLEL_TESTS=1` | Code report: lease `72ce0cbd`, waited 0s, held 574s, failed 0, skipped 0. SettlementSyncRecoveryTests 13, RunnerBranchContractDocumentationTests 5, SettlementSyncDebtPolicyTests 30, DelegateScriptLandStatusTests 19, RunnerSettlementSyncTests 29, RunnerTaskSettlementTests 32, ReviewEvidenceResettlementTests 10, BlockedTaskSyncRecoveryTests 2, AttentionServiceTests 164, DispatcherSweepLifetimeTests 6, DispatcherSweepLifetimeRegistrationTests 1, AppDbContextModelTests 2, TestClassificationGuardTests 1, SlowTestTripwireTests 2. |

The code report's statement table after the fix: empty table 1 statement; one not-due Held row 1 statement; one due live Held row 6 statements; three due Held rows with a null path 13 statements. `scripts/check-evidence-diff.ps1` over `a77c7a3a575471ba0a5bffdaf486935b15c0d660..4c0f48189bfae15ca2f2c48876e13a97dfd33da2` exited 0 with commits 1, entries 0, violations 0.

## Producer-to-recipient acceptance is not an F3 row

The review's Pending UserPrompt finding is about `RunnerTaskSettlementTests` stopping at `AwaitingReceipt` plus a queued message. The F3 plan rows are CP-4 (`SettlementSyncRecoveryTests`, V-8, V-9, V-10, R-4, R-5) and CP-5 (`RunnerBranchContractDocumentationTests`, V-7). None of those rows name a UserPrompt, a receipt crash cut, or the C544 rig. The Pending settlement producer is F2 (`RunnerTaskSettlementTests`, CP-2) and the final CP-11. This repair does not add that producer-to-recipient test.

## F3b checkpoint rows

Gone is defined on `HeldRegistrationPermanentlyGoneAsync` and in the plan's F3b note. The repair runs these rows from the follow-up plan, plus CP-4 and CP-5. CP-4's roster is 19. CP-12's later final roster is 19 for the same class.

| CP | Filter | Expect |
|---|---|---|
| CP-4N | `/*/*/SettlementSyncRecoveryTests/(C1136_RevokedRetirementKeepsHeldDebtAndReschedules*)` | exact 2, 0 failed/skipped |
| CP-4T | `/*/*/SettlementSyncRecoveryTests/(C1136_TemporaryWorktreeAbsenceKeepsHeldDebtAndReschedules*)` | exact 1, 0 failed/skipped |
| CP-4U | `/*/*/SettlementSyncRecoveryTests/(C1136_UncertainHeldRegistrationKeepsHeldDebtAndReschedules*)` | exact 3, 0 failed/skipped |
| CP-4X | `/*/*/SettlementSyncRecoveryTests/(C1136_HeldRecheckReadFailureKeepsHeldDebtAndReschedules*)` | exact 1, 0 failed/skipped |

Regression rows on the same plan, reusing the CP-4 build: RV-POLICY 30, RV-CLI 19, RV-SYNC 29, RV-SETTLE 32, RV-EVIDENCE 10, RV-BLOCKED 2, RV-ATTENTION 164, RV-LIFETIME 6, RV-REGISTRATION 1, RV-MODEL 2, RV-CLASSIFICATION 1, RV-SLOW 2. The whole Unit lane is not in this repair's closed list.
