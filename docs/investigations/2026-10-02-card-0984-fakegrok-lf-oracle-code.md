# CARD-0984 Code evidence

Code task / landing owner: 037ab247. Branch: feat/card-task-037ab247.
Worktree: /work/worktrees/task-037ab247.
Start SHA: bb082f64745bc014a9be6e9ebdcbff3a69af5979.
Implementation SHA: 626c6954ec8b6f63abc88f94bb64e64065a3bd5e.
Plan: docs/superpowers/plans/2026-10-02-card-0984-fakegrok-lf-oracle-plan.md.

Only FakeGrokContractTests source changed. FlattenLfSubmitEcho is used only by the LF oracle and its pure regression. CR/LF/U+0008 removal repairs legacy inbox-console wrap artifacts; existing assertions are retained. The Windows method additionally checks exactly one native user_message_chunk, whole body equality with only LFs dropped, and exactly one turn_completed through existing fake log support. No production/fake/UnixPtyArgv/provider-canary source changed; no timeout or assertion was loosened.

V-1: baseline total 18, executed 0, passed 0, failed 0, skipped 18. All are named Windows-only skips; fresh TRX class/method definitions inspected.
V-2: extraction total 18, executed 0, passed 0, failed 0, skipped 18; exact baseline roster comparison matched.
V-3: exactly one executed pure test failed at label LF submit echo must remain intact across console wrap artifacts. Actual contained TAIL last\\u0008 line; expected TAIL last line. This is the original defect, recorded before changing the helper.
R-1: rerun CP-3 at the fix SHA executed 1, passed 1, failed 0, skipped 0. Fresh TRX method inspected and checkpoint source validator passed. This is one failure-driven rerun across invocations; neither repeat nor known-flaky was requested.
V-4: final class row CP-4 is intentionally scheduled after this evidence commit, to qualify the final HEAD. Its actual receipt and complete report will be .antiphon/task-037ab247.md. Required roster: the identical 18 skips plus exactly this one passing pure method. Windows native assertions compile here but are not claimed executed.
V-5: deferred to a separate Windows inbox-host confirmation task; not passed.
PC-1: pending method-scoped post-land SourceLanding Mutation. The brief requested a scratch mutant, but the standing Code stage contract assigns deliberate mutants and missing-control discovery to Mutation. No mutant was applied. Missing controls for the added Windows native assertions remain Mutation-owned.

Scope follows the explicit brief exception: no whole Unit lane, no whole Pty assembly and no shared helper affecting other classes. No unbounded class impact exists. No real-provider CLI runs, no Windows run, no server/runner restart, no land/deploy. Restart: none.

Unlisted build: checkpoint-tool bootstrap through scripts/build-slot.ps1, necessary to execute the standing manifest requirement. BUILD SLOT granted lease=d70bd0f3-3554-488a-848e-1edadf2f26ff waited=0s maxcpucount=6; released held=4s. Build succeeded with one inherited CS8602 warning in unchanged TaskOwnerGuard.cs:170. The brief requested direct run-checkpoint.ps1, but the standing Code stage requires the checkpoint tool; the task token was present and owner verification succeeded. Read /api/runner-defaults and /api/session-runners before work; no fleet location was embedded or runner/platform pin added.

All checkpoint rows use committed expected source SHA and report dirty=0 sourceState=clean buildSource=verified slot=granted waited=0s. Baseline/extraction zero executed counts are the required Linux skips, not a passing Windows behavior claim. The final row requires a nonzero executed pure test.

## Receipts (unedited)

CHECKPOINT CP-1 commit=bb082f64745bc014a9be6e9ebdcbff3a69af5979 build=ok filter=/*/*/FakeGrokContractTests*/* executed=0 passed=0 failed=0 skipped=18 trx=/work/worktrees/task-037ab247/.antiphon/checkpoints/20261002-224942-1482/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=bb082f64745bc014a9be6e9ebdcbff3a69af5979 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=3274cae4afece61e601fa46d1b111633ec26e70e build=ok filter=/*/*/FakeGrokContractTests*/* executed=0 passed=0 failed=0 skipped=18 trx=/work/worktrees/task-037ab247/.antiphon/checkpoints/20261002-225035-bec6/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=3274cae4afece61e601fa46d1b111633ec26e70e sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=6b508f9836a8bbe8d6218fd229a173a8e11f3df4 build=ok filter=/*/*/FakeGrokContractTests/Lf_submit_echo_flattens_console_wrap_backspaces_and_line_endings executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-037ab247/.antiphon/checkpoints/20261002-225141-4cc0/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=6b508f9836a8bbe8d6218fd229a173a8e11f3df4 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=626c6954ec8b6f63abc88f94bb64e64065a3bd5e build=ok filter=/*/*/FakeGrokContractTests/Lf_submit_echo_flattens_console_wrap_backspaces_and_line_endings executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-037ab247/.antiphon/checkpoints/20261002-225335-49cb/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=626c6954ec8b6f63abc88f94bb64e64065a3bd5e sourceState=clean buildSource=verified
