# CARD-1486 S5 Final Review — found

Code owner `75c32a51-a96a-4e1f-af75-359c9bf55d17`, reviewed source `5425942116c201283ae5cc0fa47f1f727cd9838b` on `feat/card-task-75c32a51`. Review branch `feat/card-task-1bada46b`. One defect: the staged-reply witness cannot go red when the two finding tokens are reordered inside `FindingContract`. Product wires, parsers, and the other two mutants behaved as specified. Do not land until that assertion is independent of `FindingContract`.

## Defect

Where: `tests/Antiphon.Tests/Application/BlockedReplyReportRetentionTests.cs`, `Blocked_reply_wire_to_a_stage_task_restates_the_finding_marker` (lines 251–267). The verbatim check is `body.ShouldContain(DelegationReportFormatter.FindingContract(...))`.

Failure: Swapping the `found` and `clean` arguments inside `DelegationReportFormatter.FindingContract` left that witness green. Run: 1 executed, 1 passed, 0 failed. TRX `/tmp/c1486-mut-reorder/run.trx`. Slot lease `2e038ecc-2670-4dd5-be59-2ebfdca4f6c3`.

Why: The reply inserts `FindingContract`, and the assertion reads the same method. Reordering the two token lines changes the expected string and the queued body together. The verdict loop only checks that each token exists and precedes the `done` / `blocked` / `failed` report tokens. Nothing requires the `found` token to appear before the `clean` token independently of `FindingContract`.

Fix: On the queued body, assert `IndexOf(FindingToken(taskId, "found"))` is less than `IndexOf(FindingToken(taskId, "clean"))`. `FindingToken` does not move when the contract lines are swapped, so this mutant goes red. Keep the existing presence, caller-text, and report-token checks.

The mutant was restored with `git checkout --`. HEAD is still `5425942116c201283ae5cc0fa47f1f727cd9838b` aside from this report.

## Mutations that did go red

Both were restored before the next edit. Each failure is an assertion, not a build or fixture error.

- Drop the stage argument at `AgentTaskReplyService.cs` (`BlockedReplyContract(taskId, null)`). `Blocked_reply_wire_to_a_stage_task_restates_the_finding_marker` failed at line 254: the body lacked `[antiphon-finding:{id} found]`. 1 failed, 0 passed. TRX `/tmp/c1486-mut-drop/run.trx`.
- Always emit `FindingContract` when stage is null (`stage ?? OrchestrationStage.Review`). `Blocked_reply_wire_requires_a_complete_report_and_closing_token` failed at line 223: the body contained `[antiphon-finding:`. 1 failed, 0 passed. TRX `/tmp/c1486-mut-stageless/run.trx`.

## Receipts

Both validated with `scripts/validate-checkpoint-receipt.ps1` (`CHECKPOINT SOURCE VALID`, exit 0) at `5425942116c201283ae5cc0fa47f1f727cd9838b`.

- CP-10 run `20261023-030230-e6bb`: executed 2, passed 2, failed 0, skipped 0. `sourceState=clean`, `buildSource=verified`, `dirty=0`, slot granted, waited 0s. Receipt `.antiphon/checkpoints/20261023-030230-e6bb/report.json`.
- CP-11 run `20261023-024942-685d`: executed 365, passed 365, failed 0, skipped 0. Same clean provenance, slot granted, waited 15s. Class split 341 + 19 + 5. Receipt `.antiphon/checkpoints/20261023-024942-685d/report.json`. S1–S4 retention methods in that TRX passed.

The earlier combined run `20261023-024001-a19e` is not a receipt. CP-11 was refused because the isolated test DLL exceeded the source-guard size limit (`cleanup_refused`). Do not validate that report.

`scripts/check-evidence-diff.ps1` over `247e615da70ec1f75a05d5aa56a9c7941ae94911..5425942116c201283ae5cc0fa47f1f727cd9838b`: commits 3, entries 1, violations 0.

## Wire and parser

A throwaway harness sent `BlockedReplyContract` for null and for every `OrchestrationStage` value, then fed the echoed contract, each backtick-quoted token line, a fenced copy, and a placeholder id through `TryReadFindingLine`, `TryReadReportVerdict`, `IsClosingLinesOnly`, and the progress-claim parser. `FAILURES 0`. Null stage has no finding token and is byte-identical to the pre-S5 contract (749 vs 749). Rebase, Verify, Cleanup, Review, FollowUp, and Deploy each contain `FindingContract` verbatim, with `found` before `clean` and both before `done` / `blocked` / `failed`. Quoted, inline, and fenced instruction text is not a finding, not a report verdict, not closing-lines-only, and not a progress claim. `DefaultFor` is null for Check and Custom, and Review for Review. CP-10 already queued a real Reply for Review and for null and checked `Replied.InputBody`.

## Landability of this SHA

`git merge-tree --write-tree origin/master HEAD` exit 0, tree `613ac46fb356a22a923412b08e134c1afd4c08dc`. `origin/master` is `4d4718a5bc3305565c26f8c42069e5513951753c`, a descendant of `ef121b23f3d78bbeb31e09886dcf8448e4a633e5`. Merge-base is `247e615da`. The tip is not on master.

Cherry-pick onto `origin/rc/2026-10-20` at `5a126f6fc5ae4e16d803b628716b8f0485a37174` applied cleanly in this order (docs-only report commits omitted). No rc ref moved. Scratch tip was `5ca6c19a78cf7270b2b49c5f530396b858577fee` and was removed.

1. `9404e8d92774d01f7686b07b95f6ac639d559270` S4 test
2. `66a5fe702b43828500c02d95bd9f1f68ed907558` S4 fix
3. `43ae1dc44ec2fced171830106e16478e6bc3dc24` repair, mixed and foreign markers
4. `c4aee39948ac6f41e67c1884d7d5f81c9f08d14f` repair, settlement-event selection
5. `3bd2b76d1e793cd23d5ee940f1cbb1c11506710d` S5 test
6. `0c14ce6657dcf1484403317da3c1235b7147098c` S5 Stage.Review correction
7. `5425942116c201283ae5cc0fa47f1f727cd9838b` S5 fix

`server/Antiphon.Server.csproj` on that scratch built with 0 errors and 22 warnings (lease `b057f454-0267-4966-a6fc-49432c478070`).

## Not defects

- Plan Expect cells still say CP-11 is about 161. The executed count is 365. That stale text is advisory.
- The plan's `Stage=Code` is not an `OrchestrationStage`. Code already corrected the witness to `OrchestrationStage.Review`.
- Check and Custom stay stage-less unless a caller stores an explicit stage. The reply keys off stored stage, which is D-5.
- The plan has zero PC rows. SourceLanding Mutation still owns any bound controls. This review does not discharge them.
- The Unit lane was out of this round's commissioned scope.

PCs remain pending.
