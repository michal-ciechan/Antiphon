# CARD-1102 inherited land-gate reds

Confirmed at `5b713f6855ee417737ff7d7e47cb340d66e3d9b8` on 2026-10-06. Two stale tests, two mechanisms. The land path is doing what CARD-0835 and CARD-0494 specified. No production change is indicated.

Card `a17568c9-e539-40aa-9be3-f2c06ccc28ea`. The same three assertions are red at slice base `4eb9241807ca89f8ca9ec3b4593a8c08db0e339b`. `git diff 4eb92418 HEAD` is empty for `LandApproval.cs`, `AgentTaskReplyService.cs`, `ReviewEvidence.cs`, `RepairSourceRecoveryLandingTests.cs`, `VerificationRoundSettlementTests.cs`, and `C544World.cs`.

## Reproduction

One leased build, then two `--no-build` runs of that output (`OutputPath=bin-c1102/`, `UseAppHost=false`). Slots granted, waited 0s. Build exit 0.

| Filter | Result |
|---|---|
| `/*/*/VerificationRoundSettlementTests/C544_SettlementAtomic*` | 1 executed, 0 passed, 1 failed |
| `/*/*/RepairSourceRecoveryLandingTests/*` | 4 executed, 2 passed, 2 failed |

Passed arms: `C603_RepairSuccessDoesNotAuthorizeUnreviewedOwnerLand(plain)` and `(missing-review)`.

## C603 is a fixture that omits the clean-source bit

`RepairSourceRecoveryLandingTests.AddReviewAsync` inserts a Clean Final/Full `StageOutcome` and never sets `ReviewedSourceClean` (`tests/Antiphon.Tests/Application/RepairSourceRecoveryLandingTests.cs:199-206`). A bool? left unset is null. The same initializer was introduced that way in `656b413c94025ce93fef27bf06e0a49f5043765a` (2026-09-30 10:05) and has not gained the field.

`LandApproval.LoadUsableEvidenceAsync` refuses that row before it compares the subject (`server/Application/Services/LandApproval.cs:77-79`, then subject at `:107-108`):

```text
Review evidence does not assert a verified clean source.
code review_evidence_source_not_clean
```

That predicate was added in `31411ea310024568d45c3130b9678fd4da63d68f`. `ReviewEvidence.Parse` stores null when the report omits `reviewedSourceClean`, when the value is not exactly `true` or `false`, or when the key is repeated (`server/Application/Services/ReviewEvidence.cs:83-88`). First settlement copies that nullable through (`ReviewEvidenceBindingService.cs:63`). The owner doc requires true for evidence-backed admission and says a missing or false value returns `review_evidence_source_not_clean`; old null outcomes stay null (`docs/orchestration-loop.md:1854-1860`).

Admission order in `AgentTaskLandService.RequestAsync`:

- `plain` sets `RecoverReviewedSource` false and passes no evidence. A Failed owner hits the succeeded-status refusal, whose default code is `conflict` (`AgentTaskLandService.cs:108-111`, `ConflictException` ctor at `server/Application/Exceptions/ConflictException.cs:8`). This arm passed.
- `missing-review` asks for recovery with a null evidence id and throws `recovery_review_required` at `AgentTaskLandService.cs:196-198`, before `LoadRecoveryEvidenceAsync` (`:232-235`). This arm passed.
- `C603_FailedOwnerLandsReviewedRepairedTip` seeds an owner review and expects `RequestAsync(..., RecoverReviewedSource: true)` to queue. Reproduced at `RepairSourceRecoveryLandingTests.cs:34`: `ConflictException` "Review evidence does not assert a verified clean source."
- `repair-subject` seeds that same null-clean review against the repair task. The test expects `review_evidence_subject_mismatch` (`RepairSourceRecoveryLandingTests.cs:120-125`). Reproduced actual code `review_evidence_source_not_clean`, because cleanliness is checked while the row is still unusable, before subject identity.

`LandContractSeeds.SeedReviewAsync` already defaults `reviewedSourceClean` to true (`tests/Antiphon.Tests/TestHelpers/LandContractSeeds.cs:18-29`). The recovery fixture was not updated with that helper.

A land that succeeded in this session presented evidence the gate accepts. The gate requires `ReviewedSourceClean == true` plus subject, SHA, ref, and repository. Those lands do not show the gate refusing a verified clean review, and they do not exercise this fixture.

## C544 is the after-commit cut firing before commit

`C544_SettlementAtomic` loops `before-save`, `before-commit`, `after-commit` (`VerificationRoundSettlementTests.cs:122-141`). The failing assertion is only the third cut, at `:141`:

```text
outcomes should be 1 but was 0
Additional Info: after-commit: committed
```

The message `after-commit: committed` is that line's custom text. `fault.Throws.ShouldBe(1, cut)` at `:133` uses the message `after-commit` alone, so the interceptor did throw once. The first two cuts run earlier in the same method and use different messages; reaching `:141` means both of those cuts, including their recovery to one outcome, completed.

The after-commit interceptor throws `IOException` from `SavedChangesAsync` when a `StageOutcome` for the task is tracked (`VerificationRoundSettlementTests.cs:390-397`). Review settlement now saves inside an explicit transaction and commits only after `SaveChangesAsync` returns (`AgentTaskReplyService.cs:2219-2236`):

```text
BeginTransactionAsync
SaveChangesAsync          // SavedChanges runs here
settlement-before-commit
CommitAsync
```

`OnTurnEndLockedAsync` swallows that `IOException` (`AgentTaskReplyService.cs:120-124`). Disposing the uncommitted transaction rolls the insert back, so a fresh context sees 0 rows. The report this test settles omits `reviewedSourceClean` (`C544World.ReviewReport` default at `C544World.cs:367-375`; call at `VerificationRoundSettlementTests.cs:130`). Settlement still adds the outcome with a null clean bit. The count of 0 is the rollback, not the clean-source refusal. This test never calls land.

`3b0b67c4a297ecbdabd85599a964597c194ac2e3` (2026-09-28, CARD-0494) introduced that transaction. Its parent called `settlement-before-save` and `settlement-before-commit`, then one `SaveChangesAsync`, with no explicit transaction. The test file's last commit is `00ed5d88f` on 2026-09-17. CARD-0494's diff does not touch `VerificationRoundSettlementTests`.

## Why CARD-0835 did not catch either red

The CARD-0835 test design was finalized in `83ccea1a3` on 2026-09-30 09:19, before `RepairSourceRecoveryLandingTests` existed (10:05 the same day). `git log -S RepairSourceRecoveryLandingTests` on `docs/superpowers/plans/2026-09-30-card-0835-checkpoint-receipt-dirty-tree-plan.md` is empty. The seed table names `AgentTaskLandAdoptionTests.AddReviewAsync` and says the full `VerificationRoundSettlementTests` class is outside the closed roster (plan around the seed-audit table and `:317`). The checkpoint table is CP-1 through CP-12 (`:711-731`) and does not name either class. The gate commit's own message says it was not built, not tested, and not reviewed. Later CARD-0835 commits the same day continued that roster.

CARD-0494 is the earlier break for C544. CARD-0835 is why the C603 fixture, already on master, became a false refusal, and why neither class was re-run when the gate landed.

## Risk if left red

`C603_FailedOwnerLandsReviewedRepairedTip` is the witness that a Failed owner with a Clean Final/Full review of its repaired tip is queued and published. While it is red, that publication path has no green run. `repair-subject` no longer witnesses `review_evidence_subject_mismatch`. `plain` and `missing-review` still pass. `C544_SettlementAtomic` no longer witnesses that a fault after the Review settlement commit leaves exactly one outcome. Its pre-commit cuts still do. A later real change in these assertions is easy to file as the same inherited red. Live lands of reports that carry `reviewedSourceClean: true` are unaffected.

## Not done, noted

Test-only: set `ReviewedSourceClean = true` in `AddReviewAsync`, and move the C544 after-commit throw to `TransactionCommitted` after `CommitAsync`, keeping the one-outcome expectation.

## Uncertainties

The C544 start commit is reconstructed from the `3b0b67c4a` diff and the unchanged test. This session did not execute the parent of that commit. No debugger session showed the SQL rollback; the fresh-context count of 0 after a `SavedChanges` throw inside the open transaction is the measured result. Other `StageOutcome` seeds outside these two classes were not re-run.
