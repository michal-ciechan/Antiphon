# CARD-0448 Code checkpoint: foundation only, not ready for Review or Land

The full S1-S6 implementation is unfinished. This checkpoint adds typed Git/source inspection,
remote observation, pinned push/ref primitives, the landing evidence schema, transition policy,
and a repository lease primitive. It retires the unsafe prose-based stage backfill (F2).
**The existing AgentTaskLandService, DelegationWorktreeService and WorktreeManager destructive
paths are still unchanged. This branch does not fix the production landing incident yet.**

The task checkout initially pointed at `f202fcc1`, despite the brief saying the plan was on
master. This task branch was fast-forwarded to the supplied `8ed4d68b` before changes. The shared
main checkout was not advanced, restarted or deployed. No live landing, board change, runner
launch, live-origin inspection or worktree cleanup was performed.

## Implemented foundation

- `LandingDtos`, `ILandingGit`, `LandingGit`: full-ref validation, exact symbolic branch/HEAD
  and registered-worktree identity; canonical common directory and junction resolution;
  status checks, active sequencer refusal and explicit enumeration of ignored paths. Detached
  HEAD is refused even at the same SHA. Re-reading identity around status detects movement.
- Separate push-endpoint observation uses `ls-remote`, a unique operation observation ref,
  a correlated fetch and tri-state ancestry. It does not use a cached tracking ref or
  `FETCH_HEAD`. Three mismatched observations refuse. Push takes an immutable SHA and explicit
  destination; source recovery pins use expected nonexistence and refuse collisions.
- `AgentTaskLanding`, enums, nullable `AgentTask.ActiveLandingId`, transition policy and
  CLI-generated `20260908113835_AddAgentTaskLandingEvidence` migration. The database has a
  filtered unique active-operation index and the operation concurrency token is an EF token.
  There is no legacy evidence backfill. A temporary model-only design-time context avoided
  booting hosted services during generation and was removed afterward; ordinary migration
  tooling configuration is unchanged.
- `RepositoryMutationLease` uses exclusive file ownership under canonical common Git dir.
  Its opaque handle rejects foreign-provider/disposed ownership and does not unlink lock files.
  Production/test DI registrations are present; participating writers do not use it yet.
- F1 is recorded in D-2/D-4, V17/C23 and new R19. The transition-policy predicate admits an
  explicit re-POST after fresh inspection of an in-place resolved interruption. **Endpoint
  operation replacement and the real interrupted-rebase regression remain unimplemented.**
- F2 removes the hosted backfill registration and retires its implementation. Replacement
  tests ensure old success/refusal prose and numeric future event value 29 cannot manufacture
  stage rows. Existing historical rows are retained. This does not migrate historical green
  rows into trustworthy evidence.
- F3 extends the plan census to remote deletion, force/mirror push and stage backfill.
- F4 explicitly documents that ordinary Git worktree removal deletes ignored files. Inspection
  enumerates ignored paths, but **the load-bearing deletion guard and real-Git PC7 are pending**.

## Required continuation

Implement the integrated durable protocol before treating any existing land outcome as safe.
Replace the old boolean shortcut; bind each mode to freshly inspected task coordinates under
the repository lease; increment attempts after claims/lease holds; commit acknowledged
checkpoints/pins before every dependent mutation; implement verification through the documented
TUnit runner; revalidate source/target at every named boundary; publish and independently prove
the pinned result; commit receipt/cleanup intent before guarded removal. Integrate Shared and
helper admission, settlement/merge-back, cancellation/process journaling and all cleanup callers.

S4 needs typed publication/local-merge/creation-rollback authority, independently re-read
receipts, fresh remote containment and ignored-content checks before each destructive step,
ordinary non-forcing removal, exact-ref old-SHA branch deletion and all-worktree checkout
inspection. Remove permissive defaults, forced/recursive fallbacks and TTL/event authority.

S5/S6 still need structured DTO/event/notification/pipeline consumers, append AlreadyPresent
without renumbering, atomic terminal event/evidence scheduling, all restart/retry outcomes,
and updated operator/help/bundle documentation. Recovery success cuts C09-C11 and C14-C18 are
not implemented. All C01-C24 and F01-F07 acceptance remains pending, despite the independent
schema upgrade/unique-index/CAS tests below. Complete the full real-service LandingSafetyHarness,
36 V families, 19 R rows after F1, 50 PC families and every required variant.

## Caller census disposition

The expanded census is `.antiphon/c448/caller-census.txt` in this task checkout. These are
review obligations, not a declaration that the unchanged paths are guarded.

| Caller | Authority / lease / final guard at this checkpoint | Required evidence |
|---|---|---|
| `AgentTaskLandService.RunAsync` and already-landed shortcut | Old branch ancestry/missing-ref inference; no durable protocol or repository lease | V1-V23, V26-V34, V36 pending end-to-end |
| `AgentTaskLandService.VerifyAsync` recursive `bin-land` deletion | No output ownership proof; unchanged | V35 / PC8 pending |
| `DelegationWorktreeService.PrepareLandAsync` / interrupted abort | Old unconditional interrupted-rebase abort; unchanged | V17 / PC24 pending |
| `DelegationWorktreeService.FinalizeLandAsync` / cleanup retry | Old local-target/push-exit inference and shared remover; unchanged | V7-V9, V15-V20, V27 pending |
| Settlement `AgentTaskReplyService.TryMergeBackAsync` -> `DelegationWorktreeService` commit/merge/remove | No shared repository lease or typed local-merge proof; unchanged | V25/V36 pending |
| `WorktreeManager.RemoveAsync` / `TryRemoveAsync` / interface default | Forced deletion, fallback recursion, branch guess and post-delete ancestry remain | V18-V20/V36 / PC30-PC39 pending |
| `WorktreeManager.TryDeleteBranchAsync` | `branch -D`, no expected-old SHA; unchanged | V18-V20 / PC35-PC38 pending |
| `WorktreeManager.TryHealStaleRegistrationAsync` | Forced remove/prune; no new operation ownership | V25/V36 pending |
| `WorktreeManager.RollbackFailedAddAsync` | Recursive rollback and forced removal without new typed ownership | V25/V36 / PC41 pending |
| `WorktreeManager.TryDeleteDirectory` | Recursive deletion primitive reachable from above | V20/V25/V35/V36 pending |
| `WorktreeResidueSweepService` and residue job | Existing classification/shared removal; legacy event authority remains | V24/V36 / PC40 pending |
| `WorktreeJanitorHostedService` -> `PruneStaleAsync` | TTL/shared remover; no receipt authority | V24/V36 pending |
| `WorkspaceHookService.RunBeforeRemoveAsync` | Runs configured hook; no production call site found by expanded search | V36 census/execution obligation remains |
| `GitService.DeleteBranchAsync` | Local `branch -D` and remote `push origin --delete`; unchanged | F3 static call graph: only production caller is `WorkflowEngine.DeleteWorkflowAsync`; no landing call found. Runtime no-remote-delete proof remains pending. |
| `WorkflowEngine.DeleteWorkflowAsync` | Explicit workflow-delete branch flag; no land caller found | F3 remote-delete census covered statically only |
| `CardReviewService.CommitAllChangesAsync` | Additional overlapping mutation caller found by expanded search; no new lease | V36 participation decision/integration pending |
| `StageOutcomeBackfillService` | Retired; no hosted registration, no scope/DB pass, no rows from prose | V33/F2 scoped tests and PC49 backfill subvariant below |
| New `LandingGit.PinAsync` / `PushAsync` / `ObserveAsync` | Validated immutable inputs and non-forcing Git primitives; callers must supply committed intent and lease | Foundation tests below; integrated checkpoint authority pending |

## Evidence interpretation

The new Git tests exercise the production I/O implementation against fixture-owned repositories
and bare remotes. They do **not** run the complete landing service. The state tests exercise the
policy; the database tests use private testcontainer databases, including an empty predecessor
upgrade database. An executed foundation test is only partial V coverage where the plan requires
the full service, durable journal, guarded deletion or restart process.

Runtime counts, mutation assertions, final build identity and the per-requirement acceptance
table are appended after the last run. Missing execution is recorded as pending, never green.

## Measured verification

This is a **failed full-card delivery / next: code** checkpoint. No integrated landing safety acceptance is claimed.

Counts below are fresh TRX executed/passed/failed/skipped. Class names were checked against TRX test definitions. Each command used `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c448/ -- --treenode-filter "/*/*/<Class>/*" --report-trx --report-trx-filename <unique-name>`.

| Final class | Executed | Passed | Failed | Skipped | TRX basename |
|---|---:|---:|---:|---:|---|
| `LandingGitTests` | 20 | 20 | 0 | 0 | `final-LandingGitTests-9c10c9b9cf02488183da40832e1769f5.trx` |
| `LandSourceIdentityTests` | 20 | 20 | 0 | 0 | `final-LandSourceIdentityTests-a3dda965dad546719574ba7d0549c791.trx` |
| `AgentTaskLandingStateTests` | 5 | 5 | 0 | 0 | `final-policy-shape-01.trx` |
| `AgentTaskLandingPersistenceTests` | 2 | 2 | 0 | 0 | `final-AgentTaskLandingPersistenceTests-9348bba9568c4512b007dbba47796e1a.trx` |
| `RepositoryMutationLeaseTests` | 2 | 2 | 0 | 0 | `final-RepositoryMutationLeaseTests-8c1a4b632fca4136b03797ecb552ae29.trx` |
| `StageOutcomeBackfillTests` | 7 | 7 | 0 | 0 | `final-StageOutcomeBackfillTests-4a2f2c645e8e4f7fa9efcd1140d71f1e.trx` |
| `DelegationWorktreeTests` | 27 | 27 | 0 | 0 | `final-DelegationWorktreeTests-92edc12d9d514278826565abc6b39586.trx` |
| `WorktreeResidueSweepTests` | 6 | 6 | 0 | 0 | `final-WorktreeResidueSweepTests-b1030cee68d7490f93b3b8cd4c0f6709.trx` |
| `AgentTaskLandRequestTests` | 7 | 7 | 0 | 0 | `final-AgentTaskLandRequestTests-bd033bee3cfb48499dbd8bca78ee7411.trx` |
| `AgentTaskLandSweepTests` | 7 | 7 | 0 | 0 | `final-AgentTaskLandSweepTests-2b8cb6a8e3d14e3fa145b97636f78ef9.trx` |
| `AgentTaskLandStageOutcomeTests` | 10 | 10 | 0 | 0 | `final-AgentTaskLandStageOutcomeTests-2bba41ef703a4da9ae273fa2e169600e.trx` |
| `AgentTaskPipelineStatusTests` | 29 | 29 | 0 | 0 | `final-AgentTaskPipelineStatusTests-a6514ebb3cff4945a365ec86abcb52db.trx` |
| `WorktreeManagerTests` | 4 | 4 | 0 | 0 | `final-WorktreeManagerTests-ef300d7327b54d7c9f3cf6cec4c76b4a.trx` |
| `WorktreeManagerSafetyTests` | 3 | 3 | 0 | 0 | `final-WorktreeManagerSafetyTests-af2fb4a197fc4addb75e640d5744bfdc.trx` |
| `WorktreeManagerGitIntegrationTests` | 15 | 15 | 0 | 0 | `final-WorktreeManagerGitIntegrationTests-2a2fee0fcbf5465382bf73d38f043c57.trx` |
| `DelegationTestServicesTests` | 4 | 4 | 0 | 0 | `final-DelegationTestServicesTests-da33cc537fb34d5e9006c697759caa27.trx` |
| `DelegationHarnessCensusTests` | 4 | 4 | 0 | 0 | `final-DelegationHarnessCensusTests-00f018b4472046598fdd4f27187e2150.trx` |
| `TestLaneCategoryGuardTests` | 1 | 0 | 1 | 0 | `final-TestLaneCategoryGuardTests-6c23eabc333b4b9ba53aeee0505201d2.trx` |
| `ProcessSpawnLimitTests` | 3 | 3 | 0 | 0 | `final-ProcessSpawnLimitTests-3948b3d915ad4f58bc16455875bffb57.trx` |

Final class totals (latest run per class): 176 executed, 175 passed, 1 failed, 0 skipped.

The four required new end-to-end classes do not exist yet: `AgentTaskLandPublicationTests`, `AgentTaskLandConcurrencyTests`, `AgentTaskLandRecoveryTests`, `AgentTaskLandCleanupSafetyTests`. The two client V33 runs are pending; no client DTO/event union was changed. No Pty/full-assembly suite or live acceptance was run.

An initial test compilation error (CS8122: pattern matching inside a Shouldly expression tree) was fixed before execution. It earns no RED credit. Preliminary green runs and their artifacts remain in `.antiphon/c448`; they are not added again to final totals.

### Baseline incident

The one ordinary final-suite failure is pre-existing: `TestLaneCategoryGuardTests` reports
untagged `GrokRulesLiveEvidenceTests` and `HerdrLaunchContextResolverTests`. Neither file was
changed by this task. The same exact missing-class assertion reproduces in the isolated
`f202fcc1` checkout: `c448-baseline-lane-red.trx`, 1 executed / 0 passed / 1 failed / 0 skipped.
This is separate from the incident reproduction below and earns no positive-control credit.
The unrelated mixed-lane test classification was left unchanged.

`f202fcc1`, separate shared-object clone at `.antiphon/c448/baseline`, with only the baseline-compatible test adapter transplanted. Fresh `c448-baseline-incident-red.trx`: **1 executed, 0 passed, 1 failed, 0 skipped**. It reached the real `IsAlreadyLandedAsync` and `CleanupAlreadyLandedAsync` / `WorktreeManager` remover. Assertion: `Directory.Exists(info.Path) should be True but was False` (unique detached HEAD must survive). The remote source stayed at S; local directory and recorded branch were deleted. This is the required baseline RED half of PC1, not a restored integrated GREEN.

Adapter retained at `tests/Antiphon.Tests/Fixtures/Landing/C448IncidentBaselineTests.cs.txt`. Copy it to `tests/Antiphon.Tests/Application/C448IncidentBaselineTests.cs` only in the separate baseline checkout; run `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c448-baseline/ -- --treenode-filter "/*/*/C448IncidentBaselineTests/*" --report-trx --report-trx-filename <unique-name>`. All destructive work occurs in the adapter-owned temporary repository, never the checkout containing this report.

### Foundation mutation experiments

All five localized experiments ran GREEN -> mutation -> assertion RED -> byte-exact restore with refreshed timestamp -> rebuilt GREEN. These are subvariant/foundation evidence; none completes its broader PC family or the missing integrated protocol.

| Run | Executed | Passed | Failed | Exit | TRX basename |
|---|---:|---:|---:|---:|---|
| `pc04-status-identity-pre-green` | 1 | 1 | 0 | 0 | `pc04-status-identity-pre-green-37fffb1563b94d95bc568079ad719dc6.trx` |
| `pc04-status-identity-red` | 1 | 0 | 1 | 2 | `pc04-status-identity-red-abd9ed5312c64ab485dfbea686f3edbc.trx` |
| `pc04-status-identity-restored-green` | 1 | 1 | 0 | 0 | `pc04-status-identity-restored-green-ff248bf12a6f4134802c84c549723c16.trx` |
| `pc05-ancestry-error-pre-green` | 3 | 3 | 0 | 0 | `pc05-ancestry-error-pre-green-6c55bc808dce488aaf19ca7989b37806.trx` |
| `pc05-ancestry-error-red` | 3 | 2 | 1 | 2 | `pc05-ancestry-error-red-bab0ffd4b44a4a7db3f653b784659087.trx` |
| `pc05-ancestry-error-restored-green` | 3 | 3 | 0 | 0 | `pc05-ancestry-error-restored-green-d6191fb9d108440c8fd26af47d78a8eb.trx` |
| `pc13-push-endpoint-pre-green` | 2 | 2 | 0 | 0 | `pc13-push-endpoint-pre-green-2717ba6e31974f919bdecfc287e2e2e7.trx` |
| `pc13-push-endpoint-red` | 2 | 0 | 2 | 2 | `pc13-push-endpoint-red-29c9423579804c09b1ef1274823750a8.trx` |
| `pc13-push-endpoint-restored-green` | 2 | 2 | 0 | 0 | `pc13-push-endpoint-restored-green-933befa2cf784308b5801c03dbe8e5f4.trx` |
| `pc46-cas-pre-green` | 1 | 1 | 0 | 0 | `pc46-cas-pre-green-38e6ff24ac1942cf9448d9d2b99fe01f.trx` |
| `pc46-cas-red` | 1 | 0 | 1 | 2 | `pc46-cas-red-b1be4cdb779b492a858e63a43dc40f9f.trx` |
| `pc46-cas-restored-green` | 1 | 1 | 0 | 0 | `pc46-cas-restored-green-f6b513113e164c1aa46de50952987682.trx` |
| `pc49-backfill-pre-green` | 6 | 6 | 0 | 0 | `pc49-backfill-pre-green-63c40e23ccb149bc9570875d61d8658f.trx` |
| `pc49-backfill-red` | 6 | 1 | 5 | 2 | `pc49-backfill-red-c19a0a8988e04005a3a9fcd440afdaa4.trx` |
| `pc49-backfill-restored-green` | 6 | 6 | 0 | 0 | `pc49-backfill-restored-green-162ea7f8f0d74283a966b91954850208.trx` |

Intended RED assertions:

- `pc04-status-identity-red` / `C448_V10_SourceMutationInvalidatesVerification`: ShouldAssertException: result.Reason should be "source_changed" but was null
- `pc05-ancestry-error-red` / `C448_V05_AncestryExitIsTriState(128)`: ShouldAssertException: observation.ContainsSource should be False but was True
- `pc13-push-endpoint-red` / `C448_V09_ConfirmationUsesPushEndpoint(False)`: ShouldAssertException: observation.ContainsSource should be False but was True
- `pc13-push-endpoint-red` / `C448_V09_ConfirmationUsesPushEndpoint(True)`: ShouldAssertException: fixture.Git.Trace.Where(a => a[0] == "ls-remote") should satisfy the condition Contains(a, other) but [["ls-remote", "--refs", "--exit-code", "C:\Users\lndco\AppData\Local\Temp\antiphon-c448-bf9ee8a80adf4c3db4752429d587a230\remote.git", "refs/heads/master"]] do not
- `pc46-cas-red` / `C448_V31_ConcurrentOperationsAreFenced`: ShouldAssertException: Task `stale.SaveChangesAsync()` should throw Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException but did not
- `pc49-backfill-red` / `C448_V33_BackfillCannotInventLandingEvidence(24, build OK, pushed, cleanup incomplete)`: ShouldAssertException: await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None) should be 0 but was 3
- `pc49-backfill-red` / `C448_V33_BackfillCannotInventLandingEvidence(24, build skipped, cleanup incomplete)`: ShouldAssertException: await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None) should be 0 but was 3
- `pc49-backfill-red` / `C448_V33_BackfillCannotInventLandingEvidence(21, build OK, pushed, worktree removed)`: ShouldAssertException: await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None) should be 0 but was 3
- `pc49-backfill-red` / `C448_V33_BackfillCannotInventLandingEvidence(21, build skipped (base unchanged))`: ShouldAssertException: await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None) should be 0 but was 3
- `pc49-backfill-red` / `C448_V33_BackfillCannotInventLandingEvidence(22, push rejected; could not delete; build failed)`: ShouldAssertException: await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None) should be 0 but was 3

Mutation patches, byte backups, per-run DLL hashes and logs: `.antiphon/c448/pc*.patch`, `mutation-runs.jsonl`, `run-foundation-mutants.ps1`. Final verification reruns the full affected classes after restoration. The last state-policy shape tightening is verified by its final targeted rerun.

### Per-case foundation execution

| Class | Actual data row | Result |
|---|---|---|
| `LandingGitTests` | `C448_V12_RemoteMovementNeedsCorrelatedContainment(0)` | Passed |
| `LandingGitTests` | `C448_V05_RemoteErrorsAreUnknown(2)` | Passed |
| `LandingGitTests` | `C448_V09_ConfirmationUsesPushEndpoint(False)` | Passed |
| `LandingGitTests` | `C448_V05_AncestryExitIsTriState(0)` | Passed |
| `LandingGitTests` | `C448_V09_ConfirmationUsesPushEndpoint(True)` | Passed |
| `LandingGitTests` | `C448_V05_RemoteErrorsAreUnknown(128)` | Passed |
| `LandingGitTests` | `C448_V05_RemoteErrorsAreUnknown(1)` | Passed |
| `LandingGitTests` | `C448_V30_RefsParsingAndDestinationsFailClosed(refs/heads/··/invalid)` | Passed |
| `LandingGitTests` | `C448_V30_RefsParsingAndDestinationsFailClosed(refs/tags/tag)` | Passed |
| `LandingGitTests` | `C448_V30_RefsParsingAndDestinationsFailClosed(--all)` | Passed |
| `LandingGitTests` | `C448_V30_NulRegistrationPreservesPathSpelling` | Passed |
| `LandingGitTests` | `C448_V30_PushUsesPinnedCommitAndExplicitDestination` | Passed |
| `LandingGitTests` | `C448_V30_RecoveryPinCannotOverwriteAnotherCommit` | Passed |
| `LandingGitTests` | `C448_V12_RemoteMovementNeedsCorrelatedContainment(3)` | Passed |
| `LandingGitTests` | `C448_V12_RemoteMovementNeedsCorrelatedContainment(2)` | Passed |
| `LandingGitTests` | `C448_V30_RefsParsingAndDestinationsFailClosed(HEAD~1)` | Passed |
| `LandingGitTests` | `C448_V12_RemoteMovementNeedsCorrelatedContainment(1)` | Passed |
| `LandingGitTests` | `C448_V05_AncestryExitIsTriState(1)` | Passed |
| `LandingGitTests` | `C448_V05_RemoteErrorsAreUnknown(0)` | Passed |
| `LandingGitTests` | `C448_V05_AncestryExitIsTriState(128)` | Passed |
| `LandSourceIdentityTests` | `C448_V04_ProtectedContentsArePreserved(CHERRY_PICK_HEAD)` | Passed |
| `LandSourceIdentityTests` | `C448_V04_ProtectedContentsArePreserved(bin-land/keep·txt)` | Passed |
| `LandSourceIdentityTests` | `C448_V04_ProtectedContentsArePreserved(·claude/notes·txt)` | Passed |
| `LandSourceIdentityTests` | `C448_V04_ProtectedContentsArePreserved(MERGE_HEAD)` | Passed |
| `LandSourceIdentityTests` | `C448_V04_ProtectedContentsArePreserved(·antiphon/report·txt)` | Passed |
| `LandSourceIdentityTests` | `C448_V04_ProtectedContentsArePreserved(rebase-merge)` | Passed |
| `LandSourceIdentityTests` | `C448_V04_ProtectedContentsArePreserved(bin-private/keep·txt)` | Passed |
| `LandSourceIdentityTests` | `C448_V03_UnknownIdentityIsPreserved(missing_ref, source_ref_missing)` | Passed |
| `LandSourceIdentityTests` | `C448_V04_ProtectedContentsArePreserved(untracked)` | Passed |
| `LandSourceIdentityTests` | `C448_V04_ProtectedContentsArePreserved(staged)` | Passed |
| `LandSourceIdentityTests` | `C448_V02_SwitchedBranchIsPreserved(True)` | Passed |
| `LandSourceIdentityTests` | `C448_V02_SwitchedBranchIsPreserved(False)` | Passed |
| `LandSourceIdentityTests` | `C448_V01_DetachedHeadIsPreserved(True)` | Passed |
| `LandSourceIdentityTests` | `C448_V01_DetachedHeadIsPreserved(False)` | Passed |
| `LandSourceIdentityTests` | `C448_V04_ProtectedContentsArePreserved(unstaged)` | Passed |
| `LandSourceIdentityTests` | `C448_V03_UnknownIdentityIsPreserved(wrong_repo, wrong_repository)` | Passed |
| `LandSourceIdentityTests` | `C448_V03_UnknownIdentityIsPreserved(status_error, status_error)` | Passed |
| `LandSourceIdentityTests` | `C448_V03_UnknownIdentityIsPreserved(ref_error, source_ref_error)` | Passed |
| `LandSourceIdentityTests` | `C448_V04_ProtectedContentsArePreserved(sequencer)` | Passed |
| `LandSourceIdentityTests` | `C448_V10_SourceMutationInvalidatesVerification` | Passed |
| `AgentTaskLandingStateTests` | `C448_V31_TransitionsRequireEvidence` | Passed |
| `AgentTaskLandingStateTests` | `C448_V17_ResolvedInterruptedRebaseCanOpenNewOperation(False, True)` | Passed |
| `AgentTaskLandingStateTests` | `C448_V17_ResolvedInterruptedRebaseCanOpenNewOperation(True, True)` | Passed |
| `AgentTaskLandingStateTests` | `C448_V17_ResolvedInterruptedRebaseCanOpenNewOperation(False, False)` | Passed |
| `AgentTaskLandingStateTests` | `C448_V17_ResolvedInterruptedRebaseCanOpenNewOperation(True, False)` | Passed |
| `AgentTaskLandingPersistenceTests` | `C448_V31_ConcurrentOperationsAreFenced` | Passed |
| `AgentTaskLandingPersistenceTests` | `C448_V31_MigrationAndConcurrentOperations` | Passed |
| `RepositoryMutationLeaseTests` | `C448_V13_WindowsJunctionAndOtherProcessShareTheLease` | Passed |
| `RepositoryMutationLeaseTests` | `C448_V13_LeaseUsesCommonRepositoryIdentity` | Passed |
| `StageOutcomeBackfillTests` | `Retired_hosted_service_does_not_open_a_scope` | Passed |
| `StageOutcomeBackfillTests` | `C448_V33_BackfillCannotInventLandingEvidence(24, build skipped, cleanup incomplete)` | Passed |
| `StageOutcomeBackfillTests` | `C448_V33_BackfillCannotInventLandingEvidence(29, already present, no push attempted)` | Passed |
| `StageOutcomeBackfillTests` | `C448_V33_BackfillCannotInventLandingEvidence(22, push rejected; could not delete; build failed)` | Passed |
| `StageOutcomeBackfillTests` | `C448_V33_BackfillCannotInventLandingEvidence(21, build skipped (base unchanged))` | Passed |
| `StageOutcomeBackfillTests` | `C448_V33_BackfillCannotInventLandingEvidence(24, build OK, pushed, cleanup incomplete)` | Passed |
| `StageOutcomeBackfillTests` | `C448_V33_BackfillCannotInventLandingEvidence(21, build OK, pushed, worktree removed)` | Passed |

### Full acceptance ledger

Every V family below remains pending at the full-service level. Counts refer only to the foundation rows above, not to completed acceptance. All unimplemented variant subrows named in the amended plan remain pending (zero executed); no collapsed checkmark grants them credit.

| Requirement | Foundation executed/passed/failed/skipped | Full acceptance status |
|---|---|---|
| V-1 | 2/2/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-2 | 2/2/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-3 | 4/4/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-4 | 11/11/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-5 | 7/7/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-6 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-7 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-8 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-9 | 2/2/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-10 | 1/1/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-11 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-12 | 4/4/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-13 | 2/2/0/0 | Primitive includes real Windows junction and separate-process lock contention; writer/admission/service integration pending. |
| V-14 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-15 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-16 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-17 | 4/4/0/0 | Pending: only F1 replacement predicate tested; real interrupted rebase/re-POST and old pins not integrated. |
| V-18 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-19 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-20 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-21 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-22 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-23 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-24 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-25 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-26 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-27 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-28 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-29 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-30 | 7/7/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-31 | 3/3/0/0 | Partial: real predecessor upgrade, unique active index and stale-context rejection pass; protocol transitions, corrupt/crossed receipts and all variants pending. |
| V-32 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-33 | 6/6/0/0 | F2 retirement subrow passed (plus hosted companion); DTO/event/pipeline/client combinations pending. |
| V-34 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-35 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |
| V-36 | 0/0/0/0 | Pending: full-service setup, boundaries and all named variants not implemented. |

| Regression | Status |
|---|---|
| R-1 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-2 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-3 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-4 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-5 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-6 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-7 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-8 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-9 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-10 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-11 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-12 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-13 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-14 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-15 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-16 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-17 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-18 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |
| R-19 | Pending full-service regression and required mutation coverage; see amended plan row and foundation evidence above. |

| Crash cut | Executed/passed/failed/skipped | Status |
|---|---|---|
| C01 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C02 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C03 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C04 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C05 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C06 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C07 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C08 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C09 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C10 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C11 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C12 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C13 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C14 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C15 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C16 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C17 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C18 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C19 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C20 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C21 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C22 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C23 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |
| C24 | 0/0/0/0 | Not implemented or executed; every unchanged/changed/crossed-receipt/process subrow pending. |

| Persistence-failure group | Executed/passed/failed/skipped | Status |
|---|---|---|
| F01 | 0/0/0/0 | Not implemented or executed; save-before-write, rollback and acknowledgement-loss subrows pending. |
| F02 | 0/0/0/0 | Not implemented or executed; save-before-write, rollback and acknowledgement-loss subrows pending. |
| F03 | 0/0/0/0 | Not implemented or executed; save-before-write, rollback and acknowledgement-loss subrows pending. |
| F04 | 0/0/0/0 | Not implemented or executed; save-before-write, rollback and acknowledgement-loss subrows pending. |
| F05 | 0/0/0/0 | Not implemented or executed; save-before-write, rollback and acknowledgement-loss subrows pending. |
| F06 | 0/0/0/0 | Not implemented or executed; save-before-write, rollback and acknowledgement-loss subrows pending. |
| F07 | 0/0/0/0 | Not implemented or executed; save-before-write, rollback and acknowledgement-loss subrows pending. |

| Positive-control family | Evidence / missing work |
|---|---|
| PC-1 | Baseline RED reproduced. Integrated fixed-service GREEN and detached-guard mutant pending. |
| PC-2 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-3 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-4 | Status/identity comparison mutant: 1 assertion RED, 1 restored GREEN; full-service boundary integration pending. |
| PC-5 | Ancestry-error subvariant: 1 assertion RED among 3 cases, 3 restored GREEN. Other error/timeout/status/ref variants pending. |
| PC-6 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-7 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-8 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-9 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-10 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-11 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-12 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-13 | Push-endpoint subvariant: 2 assertion RED, 2 restored GREEN. Configuration/multiple-destination and service variants pending. |
| PC-14 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-15 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-16 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-17 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-18 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-19 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-20 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-21 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-22 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-23 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-24 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-25 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-26 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-27 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-28 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-29 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-30 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-31 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-32 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-33 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-34 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-35 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-36 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-37 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-38 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-39 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-40 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-41 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-42 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-43 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-44 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-45 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-46 | Operation EF CAS subvariant: 1 assertion RED, 1 restored GREEN. Constraint mutant, retry/evidence/schema variants pending. |
| PC-47 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-48 | Not executed; all independently named mutants and restored GREEN runs pending. |
| PC-49 | Backfill restoration subvariant: 5 assertion RED among 6 cases, 6 restored GREEN. Other structured consumers pending. |
| PC-50 | Not executed; all independently named mutants and restored GREEN runs pending. |

The integrated real-Git ignored-file guard/PC7, cancellation/journal/locked-file deletion checks, S/P/R recovery pin retention after actual cleanup, terminal transaction faults and supervised production rollout are all pending. No green aggregate substitutes for them.

### Build and source identity

Final server DLL SHA-256: `87edd853e0c949a6c101a891c5663c39e92037decb71516ecf5f9f29fa7751cb`.
Source mutation backups were restored byte-for-byte. `git diff --check` passes. The final staged diff contains no mutant. The additive migration was generated by `dotnet ef migrations add AddAgentTaskLandingEvidence --project server --output-dir Migrations`; no shared database was migrated.
