# CARD-1805 Review report (task ec8d925b, ordinary round 1, Final)

Verdict: CLEAN. No blocker, no regression, no fail-open, no test that cannot fail. Advisories go to cards (CARD-1808 filed); none holds the land. A-4 was not reviewed, per the brief (Debug c4b4ad81).

Subject: `origin/feat/card-task-4919f750` at `571da60b6bc1acc8fb5974ee7b62e13da3c2563f` (Code task `4919f750-43a2-46fa-85b0-488affcf8144`). The last source commit is `c8000b195033360723a7b7f3f783e69f2d7bcbcb`; the tip adds only `.antiphon/task-4919f750.md`. Base master `a0e14b3e1e5c83425ac070fac5d9e953b8d1be74`. Reviewed read-only in the runner mirror `/work/worktrees/task-ec8d925b` (branch `feat/card-task-ec8d925b`, same tip); this report is the only commit.

## A-3: `ITaskProgressGit.HasObjectAsync` and the unfetched gate (product)

- Contract. Default interface method: `GitObjectId.IsFull` first, then `RunAsync(["cat-file","-e",sha])`; exit 0 is true, 1 is false, anything else is null. The gate (`server/Application/Services/TaskCompletionProgressService.cs:273-278`) takes only `== false` for `runner_sync_source_unfetched`; true and null fall through to `runner_sync_progress_unverified`. `SettlementSyncDebtPolicy.BlockReason` maps a Pending Indeterminate read with that reason to a Code block and only `SourceUnfetched` to null, so the new branch blocks a Code report and never passes as unfetched or clean. Fail-closed on every new branch.
- Exit codes probed on git 2.47.3 in a scratch repository: present 0; missing full sha 1 (silent); not a repository 128; unreadable objects directory 128; tree object 0; corrupt loose object 0 (`cat-file -e` checks presence, not integrity). The 0 cases give true, which is unverified: fail-closed. The Code report's "exit 1 absent, 128 not a repository" matches.
- Exceptions, timeout, cancellation. `RunAsync` is `LandingGit.ExecuteProcessAsync`: the budget throws `TimeoutException("git_timeout")`, a start failure throws `IOException("git_start_failed")`, cancellation propagates. Both production callers of `EvaluateAsync` (`AgentTaskReplyService.RecordRemoteEvidenceAsync` and the settlement path near line 3985) catch everything except `OperationCanceledException` and record Indeterminate fallback evidence (`prepared.Reason`, that is `runner_sync_lease_busy`, or `primary_status_unavailable`), which `BlockReason` turns into `runner_sync_progress_unverified` for a Code task. Same shape as the replaced `RevParseCommitAsync` path.
- Environment. `cat-file` is not an in-process libgit2 read (`InProcessGitReads.Handled` lists rev-parse, show-ref, symbolic-ref, merge-base, for-each-ref, diff-tree, ls-tree and worktree list), so the call is a real child through `ExecuteProcessAsync` with `GIT_TERMINAL_PROMPT=0`, `GIT_OPTIONAL_LOCKS=0`, `ServerGitIdentity.ScrubInherited` (GIT_AUTHOR_*, GIT_COMMITTER_*, GIT_CONFIG_PARAMETERS, GIT_CONFIG_COUNT, GIT_CONFIG_KEY_n, GIT_CONFIG_VALUE_n), the pinned `GIT_CONFIG_GLOBAL` and auto-maintenance off, identical to every sibling in `TaskProgressGit.cs` (which already runs `cat-file -e` in `ObserveExactRefAsync`). No `safe.directory` setting exists in `server/`; none is added or needed. `cat-file` is in `LandingGit.ReadOnlyCommands`, so no mutation journal.
- Reason text. The method returns `bool?`; the gate emits only the two constant codes; `LandingGit` never exposes stderr. No path or secret leak.
- Old callers. The gate had one call site. `RevParseCommitAsync` is unchanged for its six other callers. The old gate treated every rev-parse failure except `rev_parse_failed` as unfetched, so exit 128 (missing or unreadable) passed as unfetched; the new gate closes CARD-1802 Review M-2b.
- Fakes. `FakeTaskProgressGit.HasObjectAsync` reads the same graph as its `RevParseCommitAsync` (full sha to bool, non-full to null). The witness `C1805_UnfetchedGateTreatsARepositoryErrorAsUnverified` runs on `SyncWorld.RecordingGit : TaskProgressGit` with a `FailCommand` returning exit 128 for any command naming s, so the real default method and the real exit mapping are exercised, and the absent case asserts the `cat-file -e <s>` command and no sync command.
- Layering. The Application interface now imports `Antiphon.Server.Domain` for `GitObjectId`; `C1729LayeringGuardTests` green (CP-2).

## A-1: recovery exception in the docs

All five places carry the narrowed canonical sentence "Lease contention alone never leaves Pending evidence of a correlated complete report without a debt row (CARD-1802)." and the exception sentence: rule 6 and the CARD-1082 design paragraph in `docs/orchestration-loop.md`, `docs/ops-http.md`, `docs/session-runtime-invariants.md`, and `scripts/delegate.ps1` line 910 (the shorter clause pinned as `scriptClause`). Rule 6 now says "never Blocks a correlated complete report"; the old "at all" wording is pinned absent. `scripts/delegate.ps1` has 0 non-ASCII bytes (`LC_ALL=C grep -P '[^\x00-\x7F]'`). The recovery arm's product code (`AgentTaskReplyService.RunnerRecoveryBlockReasonAsync`) is untouched, as the card demands.

Is leaving the arm without a debt row safe? The arm returns `prepared.Reason` for any unconfirmed sync, so lease contention gives Blocked on `runner_sync_lease_busy`, Pending evidence, no debt row, session and worktree kept, next stage Decide, handoff "fresh completion report" (`C1805_BindRefusalRecoveryUnderLeaseContentionStaysBlockedWithoutDebt`, 34-test class green). A debt row there would settle Succeeded on evidence no correlated report produced (CARD-0657 D-5) and widen A-2, so the narrow choice is right. The Reply is needed anyway because the report is genuinely missing, which is rule 6's own Reply case, so an orchestrator following rule 6 is not told to wait. The cost is that a lease-busy recovery that would have Succeeded holds a Blocked seat until the Reply; that is the general Blocked-seat condition CARD-1803 (Backlog: detection and escalation, SeatIdle at 30 min) covers. Not a blocker.

## A-2

The residual sentence is in the design paragraph and the runtime invariants and points at CARD-1109 (Review column, "CARD-1082 residual: Code task whose commits were never fetched during the lease budget still ends Blocked"). Pinned by the C1805 doc test.

## Witness and mutant evidence (read against the code, not rerun)

Red at `1b3f51484`: the gate witness injects exit 128 for any command naming s; the red-commit gate used `RevParseCommitAsync(s^{commit})`, whose exit 128 maps to `repair_source_identity_unavailable`, which is not `rev_parse_failed`, so it returned `SourceUnfetched` and the assertion on `broken.Reason` failed, as reported. The doc witnesses fail on the absent sentences and the two narrowed pins. The recovery witness is a characterization test, green by design, stated as such. The three mutants (M1 `_ => false`, M2 recovery arm returns `RemoteSyncBlockReason`, M3 recovery sentence removed from ops-http) each hit a distinct assertion. They, plus the fake mutant, stay pending for method-scoped SourceLanding Mutation; this round discharges no PC.

## Runs at the reviewed tip (checkpoint tool; one isolated build per run; every build and row slot=granted)

Review-only extract `.antiphon/card-1805-review-plan.md` (ignored). Three runs because one row flaked under overlap.

Run `20261109-074231-686d` (CP-1, CP-2, CP-3; CP-1 and CP-2 overlapped at host load about 21):

```
CHECKPOINT CP-1 commit=571da60b6bc1acc8fb5974ee7b62e13da3c2563f build=ok filter=/*/*/(RunnerCompletionProgressTests*)|(RunnerTaskSettlementTests*)|(RunnerBranchContractDocumentationTests*)|(DelegateScriptLandStatusTests*)|(StandingPipelinePolicyDocumentationTests*)|(SettlementSyncDebt*)|(TaskCompletionProgressPolicyTests*)|(BlockedTaskSyncRecoveryTests*)|(SettlementSyncRecoveryTests*)/* executed=204 passed=203 failed=1 skipped=0 trx=/work/worktrees/task-ec8d925b/.antiphon/checkpoints/20261109-074231-686d/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=571da60b6bc1acc8fb5974ee7b62e13da3c2563f sourceState=clean buildSource=verified
FAILED Antiphon.Tests.Application.DelegateScriptLandStatusTests.C467_V18_StatusAndAcceptance (CP-1) ObjectDisposedException: Cannot access a disposed object. Object name: 'System.Net.HttpListener'. -> rows/CP-1/failures.md
CHECKPOINT CP-2 commit=571da60b6bc1acc8fb5974ee7b62e13da3c2563f build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=77 passed=77 failed=0 skipped=0 trx=/work/worktrees/task-ec8d925b/.antiphon/checkpoints/20261109-074231-686d/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=571da60b6bc1acc8fb5974ee7b62e13da3c2563f sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=571da60b6bc1acc8fb5974ee7b62e13da3c2563f build=reused filter=/*/*/*/(C1805_*)|(C1802_*)|(C1082_*)|(Sync_uncertainty*) executed=78 passed=78 failed=0 skipped=0 trx=/work/worktrees/task-ec8d925b/.antiphon/checkpoints/20261109-074231-686d/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=571da60b6bc1acc8fb5974ee7b62e13da3c2563f sourceState=clean buildSource=verified
```

The one red is the test helper `EphemeralHttpListener.BindLoopback` (line 22) reusing a disposed `HttpListener` after a failed `Start()` under port contention; the file is not in the diff and the failure is in stub construction before any assertion. `validate` refuses the whole run (`report_failed`), so the rows were rerun in their own runs.

Run `20261109-075442-f76f` (CP-1 alone):

```
CHECKPOINT CP-1 commit=571da60b6bc1acc8fb5974ee7b62e13da3c2563f build=ok filter=/*/*/(RunnerCompletionProgressTests*)|(RunnerTaskSettlementTests*)|(RunnerBranchContractDocumentationTests*)|(DelegateScriptLandStatusTests*)|(StandingPipelinePolicyDocumentationTests*)|(SettlementSyncDebt*)|(TaskCompletionProgressPolicyTests*)|(BlockedTaskSyncRecoveryTests*)|(SettlementSyncRecoveryTests*)/* executed=204 passed=204 failed=0 skipped=0 trx=/work/worktrees/task-ec8d925b/.antiphon/checkpoints/20261109-075442-f76f/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=571da60b6bc1acc8fb5974ee7b62e13da3c2563f sourceState=clean buildSource=verified
CHECKPOINT SOURCE VALID source=571da60b6bc1acc8fb5974ee7b62e13da3c2563f rows=1
```

Run `20261109-080518-88fd` (CP-2, CP-3):

```
CHECKPOINT CP-2 commit=571da60b6bc1acc8fb5974ee7b62e13da3c2563f build=ok filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=77 passed=77 failed=0 skipped=0 trx=/work/worktrees/task-ec8d925b/.antiphon/checkpoints/20261109-080518-88fd/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=571da60b6bc1acc8fb5974ee7b62e13da3c2563f sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=571da60b6bc1acc8fb5974ee7b62e13da3c2563f build=reused filter=/*/*/*/(C1805_*)|(C1802_*)|(C1082_*)|(Sync_uncertainty*) executed=78 passed=78 failed=0 skipped=0 trx=/work/worktrees/task-ec8d925b/.antiphon/checkpoints/20261109-080518-88fd/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=571da60b6bc1acc8fb5974ee7b62e13da3c2563f sourceState=clean buildSource=verified
CHECKPOINT SOURCE VALID source=571da60b6bc1acc8fb5974ee7b62e13da3c2563f rows=2
```

Selection. CP-1 is the brief's touched classes (RunnerCompletionProgressTests, RunnerTaskSettlementTests, RunnerBranchContractDocumentationTests, DelegateScriptLandStatusTests, StandingPipelinePolicyDocumentationTests, SettlementSyncDebt* = Policy, Landing, Schema and Attention, TaskCompletionProgressPolicyTests, BlockedTaskSyncRecoveryTests, SettlementSyncRecoveryTests): 204, the Code's 201 plus the three Debt classes the prefix adds. CP-3 is every `C1805_`, `C1802_`, `C1082_` and `Sync_uncertainty` method in any class: 47 methods, 78 cases with `[Arguments]`. CP-2 is the guard batch: 77. Receipts carry fresh identities (three run ids, fresh TRX) and nonzero counts; no `--list-tests`, no skipped.

Unlisted: the checkpoint tool build (`build-slot.ps1`, 8 s) and the scratch `git cat-file -e` probe (no build, no tests). No other build or test driver ran; the tool reports `unlisted: none` for each run.

`check-evidence-diff.ps1 -Repository . -BaseRef a0e14b3e1e5c83425ac070fac5d9e953b8d1be74 -HeadRef 571da60b6bc1acc8fb5974ee7b62e13da3c2563f`: commits=4 entries=1 violations=0, exit 0.

`git fetch origin master`: origin/master is `8acb630c643a0a7bcf730122f7802cd7165c08f0` (it moved past the brief's a0e14b3e1). `git merge-tree --write-tree origin/master HEAD` = `2c0b801726409d5776e42a14267520b30e366974`, exit 0, no conflicts. Landable.

No migration in the diff (downgrade selector not applicable, 0 impacted rows). No changed input waits. Performance: the tool flags RunnerTaskSettlementTests 128 s for 34 tests, TestClassificationPolicyTests 103 s for 41 and SlowTestRegistryMergeTests 61 s for 5 as SLOW CLASS; all pre-existing, the diff adds one short test to the first.

## Advisories (cards, never the land)

- CARD-1808 (filed by this review, labels tests, flaky): `EphemeralHttpListener.BindLoopback` retries on `HttpListenerException` but the managed Linux `HttpListener` disposes itself when `Start()` fails, so attempt 2 always throws `ObjectDisposedException` at `Prefixes`; the documented retry never works. 27 callers. Fix idea and red witness are on the card.
- Fake fidelity (note for the SourceLanding Mutation's fake mutant, no card): `FakeTaskProgressGit.HasObjectAsync` cannot return null for a full sha, so a fake-based policy test cannot witness the unreadable-repository branch except by `Inject` throwing; the real-git witness covers it. `RecordingTaskProgressGit` (LeaseSpanRecorder) and `RemoteWorktreeMirrorTests.FakeProgressGit` forward `RunAsync` but not `HasObjectAsync`, so the default method runs over the inner `RunAsync`; with a `TaskProgressGit` inner that is the real contract, with a scripted inner `cat-file -e` falls to the script's default. Nothing in the touched classes depends on it.
- Note, no card: `cat-file -e` returns 0 for any object type and does not check integrity, so the gate no longer requires `^{commit}`; both outcomes are true, which is unverified, so fail-closed.

## Cleanup

Kept red-run output `/work/worktrees/task-ec8d925b/tests/Antiphon.Tests/bin-c1805-r1` (1.2 GB) removed by literal path after `git check-ignore`; runs 2 and 3 deleted their own outputs. Checkpoint evidence under `.antiphon/checkpoints/` is retained and ignored. The scratch git probe lives in the session scratchpad.

--- review evidence ---
subjectTaskId: 4919f750-43a2-46fa-85b0-488affcf8144
reviewedSourceSha: 571da60b6bc1acc8fb5974ee7b62e13da3c2563f
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: a0e14b3e1e5c83425ac070fac5d9e953b8d1be74..571da60b6bc1acc8fb5974ee7b62e13da3c2563f
