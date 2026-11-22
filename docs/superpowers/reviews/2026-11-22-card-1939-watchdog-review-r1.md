# CARD-1939 Final Review, round 1

Verdict: the watchdog behavior is clean. No product blocker. Evidence-backed land is not certified: the parent checkpoint report is RED, so `reviewedSourceClean` stays false.

- Reviewed source: `a1d36b0d4898710edc9e317de9a95d523536220f`
- Code owner: `582a015c-869f-40ad-b0e2-65b576039c29`
- Base: `2f544fb373d2f2fbca49515fdd3f2f0431e527e8`
- Range: `2f544fb373d2f2fbca49515fdd3f2f0431e527e8..a1d36b0d4898710edc9e317de9a95d523536220f`
- Parent receipt: `.antiphon/checkpoints/20261122-031959-87d2` (exit 1, verdict RED)
- CP-4 rerun receipt: `.antiphon/checkpoints/CP-4-20261122-040047-d547` (exit 0; `validate-checkpoint-receipt.ps1` accepted it)
- Positive control PC-1 stays pending. SourceLanding Mutation owns it.

## Behavior

The 10-minute boot-prompt watchdog now reads lifetime attempt history from existing `AuditRecord` rows (`AuditEventType.BootPromptDelivery = 18`). There is no migration. `EventType` is an unconstrained int. `DescribeAsync` returns null only when there is no attempt row, the charged count is not positive, `LastDeliveryStartedAt` is null, and there is no composer failure. "was never attempted" is only the Pending arm where `deliveryAttempts <= 0` and that history is null.

Attempt rows are append-only. The first-attempt time is the earliest exact `delivery attempt started` audit `CreatedAt`, and a later attempt does not overwrite it. An inexact history says "attempted at least" and uses `Max(recorded + 1, chargedAttempts)` with `LastDeliveryStartedAt` as the last recorded time. The composer clause is the constant `required composer evidence was not observed`. The refund count is the number of exact `, attempt refunded` summaries. The same `FailAsync` string is the task `FailureReason` and the Failed event `Detail` (both capped at 4000). The text is a count, one ISO timestamp, and that constant. It does not copy screen, prompt, path, or token text.

Refund admission is the old skip, written the other way: `baseline == null && attempts > 0 && age < grace`, then `DeliveryAttempts--` only when that is true. Codex pointer defer still does not decrement attempts and records the composer failure with `refunded: false`. The persist-blind transcript refund still decrements and does not write this history. The attempt audit from the send remains, so a later watchdog read is not "never attempted". The history writer is not Claude-only. Other watchdog arms were not rewritten.

Audit inserts share the caller's `SaveChanges` with the queue stamp (before typing) and with the refund. There is no separate try/catch. A failed insert rolls back that stamp or refund. The summaries are a queue GUID plus a short constant, under the 2000-character summary cap, with null foreign keys. Retention nulls `FullContent` and does not delete rows; these rows already have null `FullContent`. During the 8-minute grace the stranded sweep is about once a minute, then `MaxDeliveryAttempts` parks the row. That is the same permanent-summary audit design, not a new retry loop. `AuditService` still parses only LLM and tool rows. The client audit badge chain has no exhaustive switch on the new value.

## Verification

Fresh checkpoint `20261122-031959-87d2` at `a1d36b0d4898710edc9e317de9a95d523536220f`. One shared build `tests/Antiphon.Tests -> bin-c1939-batched/`. Slot granted, waited 0s. Every row: `dirty=0 sourceState=clean buildSource=verified`. CP-1 was not re-run. Unlisted builds: none in that run.

| Row | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| CP-2 | 89 | 89 | 0 | 0 |
| CP-3 | 125 | 125 | 0 | 0 |
| CP-4 | 111 | 110 | 1 | 0 |
| CP-5 | 53 | 53 | 0 | 0 |
| CP-6 | 34 | 34 | 0 | 0 |
| CP-7 | 320 | 320 | 0 | 0 |
| CP-8 | 1 | 1 | 0 | 0 |
| CP-9 | 110 | 110 | 0 | 0 |
| CP-10 | 193 | 193 | 0 | 0 |
| CP-11 | 14 | 14 | 0 | 0 |

CP-4's only failure was `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`. The script is invoked with `-NoBuild`, and `dotnet run --no-build` reported that `scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll` did not exist. The other 110 guard tests passed, including the committed-index checks. Building that driver (native output, `UseAppHost=false`, slot `40360dfc-1b5f-475d-8493-f4829091447e`) and rerunning only that method through the checkpoint row command passed:

`CHECKPOINT CP-4 commit=a1d36b0d4898710edc9e317de9a95d523536220f build=reused filter=/*/*/DerivedImpactIndexTests/regenerate_script_reports_changed_then_unchanged executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/CP-4-20261122-040047-d547/run.trx slot=granted waited=0s dirty=0 source=a1d36b0d4898710edc9e317de9a95d523536220f sourceState=clean buildSource=verified`

`scripts/validate-checkpoint-receipt.ps1` on that rerun report exited 0. The same script on the parent report exited 2 with `reason=report_failed` because the parent verdict is RED. That is why `reviewedSourceClean` is false. The rerun started at 1-minute load 15.34, under the brief header gate of 16 and over the attack gate of 14. It was already inside the checkpoint tool and was not stopped.

A direct `dotnet run --no-build -p:UseAppHost=false --property:OutputPath=bin-c1939-batched/` said the test project OutputType was Library and did not execute. The row command above is the execution that matches the cold output (`UseArtifactsOutput=true`, absolute output, `UseAppHost=false`).

Scratch mutant, restored before this commit: `RecordComposerFailure` returned immediately when `refunded` was true, so the attempt audit still wrote and the refund-time audit did not. `Refunded_boot_prompt_attempts_are_reported_with_the_first_start_and_composer_failure` executed 1, failed 1, at the `ShouldContain("refunded")` assertion (test line 128). Native build slot `b425f6ba-d3b1-4941-bea8-7c00fcf3f908` (load 9.97, 0 errors). Run slot `6bd63d17-ae11-405d-ace6-13eea1bd60d1` (load 12.57). `git checkout -- server/Application/Services/BootPromptDeliveryHistory.cs` restored the method. The green result for that method remains CP-2 (89/89) on the unmodified source.

`scripts/antiphon evidence-diff --base 2f544fb373d2f2fbca49515fdd3f2f0431e527e8 --head a1d36b0d4898710edc9e317de9a95d523536220f`: commits 4, entries 0, violations 0, exit 0. `git fetch origin master` then `git merge-tree --write-tree origin/master a1d36b0d4898710edc9e317de9a95d523536220f`: master `70c13dfe001c5df6c2108000584da6151ce42e43`, exit 0, tree `bbfa848a117c6ca8e7a90bf7323b4bc47ce9c297`. `scripts/audit-land-verification-graph.ps1 -Check`: approved, digest `A9BB27C595EA171463F0ED13725E198AA64385B1744B9E0BA2D211829C65A7CF`, count 80, exit 0. `scripts/impacted-classes.ps1 -MergeSubset -Range <base>..<tip> -Json`: `migrations=false`. OS witness exit 0 (`CLASSIFY unrecognized tests/derived-impact-index.json`; path candidates in `AgentTaskDispatcher` classified non-behavior).

Windows `AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch4` and the cold Windows witness stay owed. They are not blockers.

## Advisories (do not hold the land)

- The audit insert is in the same `SaveChanges` as the stamp and the refund, as the plan says. A permanent insert failure would roll back that stamp or refund. The stored text cannot trip the summary length or a new check constraint.
- Those audit rows are not deleted by retention. Volume is bounded by the grace window and `MaxDeliveryAttempts`.
- `A_legacy_refunded_boot_prompt_reports_only_its_known_attempt_history` asserts `FailureReason` and not the Failed event detail. Both are the same `FailAsync` string.
- Code reported nullable warnings on three new assertions. They are not a behavior defect.

--- review evidence ---
subjectTaskId: 582a015c-869f-40ad-b0e2-65b576039c29
reviewedSourceSha: a1d36b0d4898710edc9e317de9a95d523536220f
reviewedSourceClean: false
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: 2f544fb373d2f2fbca49515fdd3f2f0431e527e8..a1d36b0d4898710edc9e317de9a95d523536220f
