# CARD-1465 Code task 16c3d5af report

Test-only fix: `AgentTaskReplyIntegrationTests.continue_replays_standing_authority_on_a_question_block`
now compares `BlockedNote.ContinueMessage(authority).ReplaceLineEndings("\n")` against the
LF-normalised queued body (tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs:1115-1116).
No product change. Plan: docs/superpowers/plans/2026-10-22-card-1465-crlf-literal-plan.md.

Base 08765c68cdd05f4c80be092e06d39abe704c814e (= origin/master at dispatch).
Commits: a9cfd36230bcc94b1af1d1eefd1f4f6a108b31e1 (plan), cb148bcc70194b7a4244fe76e1a15431caacc45a (fix),
then this report commit; CP-1 runs on the report commit (the final tip), results in the final message.

## Scan

Only line 1115 is the defect. Other raw literals in AgentTaskReply*/Blocked* tests (AgentTaskReplyIntegrationTests
~322, 5140, 5224, 5253; BlockedQuestionTests:15) are parser/turn inputs, not expected text compared against an
LF-normalised body (322 already normalises). StandingAuthorityBlock is asserted only by its single-line header.

## W-1 scratch CRLF witness (manual, unlisted build-slot runs)

Detached scratch worktree at a9cfd3623 under the session scratchpad; `sed -i 's/$/\r/'` on
server/Application/Services/BlockedNote.cs and the test file (every line CRLF, as an autocrlf checkout).
`build-slot.ps1 -Label c1465-w1-red -- dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c1465w/ --
--treenode-filter '/*/*/AgentTaskReplyIntegrationTests/continue_replays_standing_authority_on_a_question_block'`:
failed 1 / succeeded 0, ShouldContain red at :1115 with expected "...proceed\r\nwithout..." vs LF body
(slot waited=15s). Then line 1115 of the scratch test edited to the S1 form (CRLF kept), same command
(-Label c1465-w1-green): total 1, succeeded 1, failed 0 (waited=0s). Scratch worktree removed.
Real Windows CRLF reproduction is owed to the Windows lane; nothing ran on the desktop.

## Landing

`git merge-tree --write-tree origin/master HEAD` clean at origin/master 08765c68c.
RC: both commits cherry-pick cleanly onto origin/rc/2026-10-20 (ceb6e3d6b; per-commit merge-tree with
--merge-base), rc carries the identical line 1115. rc carries no plan docs; pick cb148bcc7 only if the plan is unwanted.

## Not run

No Unit lane, full AgentTaskReplyIntegrationTests class (144 tests), assembly or qualification run: the brief
asks for minimal testing; the change is one assertion's expected side. PC-1 pending for SourceLanding Mutation.
