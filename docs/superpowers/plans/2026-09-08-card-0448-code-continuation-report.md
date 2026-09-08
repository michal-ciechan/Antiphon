# CARD-0448 Code continuation: integrated checkpoint, acceptance unfinished

This continuation replaces the live landing shortcut with a durable protocol and an explicit
ignored-content deletion guard. It is **not ready for Review, Land or deployment**. The full
V/R/PC/C/F acceptance matrix is not complete, and the legacy regression suite has unresolved
failures. This report describes a checkpoint, not a successful full-card delivery.

Worktree: `C:\Antiphon\worktrees\card-task-c86499fb`, branch `feat/card-task-c86499fb`,
starting commit `44b829bc`. Shared main remains untouched. No live land, cleanup, board change,
runner launch, restart or deployment was performed. All destructive verification used disposable
fixture repositories and bare remotes.

## Implemented

- `AgentTaskLandService.RunAsync` now acquires the repository lease, reloads the task, checks
  Shared and exact-source claims, counts the admitted attempt, and invokes `AgentTaskLandingProtocol`.
  The old branch-existence/ancestry shortcut is no longer called. Legacy split land APIs now
  refuse; their obsolete signatures remain temporarily for callers/tests that must be migrated.
- The protocol saves inspection, immutable source/target recovery pins, rebase intent, prepared
  commit, verification, local-advance intent/result, push intent and independently observed
  publication. It resumes matching target-advance/push/cleanup checkpoints. Ambiguous interrupted
  rebase is preserved; a later explicit re-POST can create a new operation with unchanged clean
  coordinates under F1. Old operations and pins remain.
- `GuardedWorktreeRemoval` independently reads committed evidence through a fresh context,
  checks task/operation coordinates, live lease, recovery pins, fresh remote containment,
  source registration/HEAD/ref, tracked/untracked and ignored contents. Opaque ignored paths
  always prevent deletion, including `.antiphon`, `.claude` and arbitrary `bin-*`.
  It uses ordinary worktree removal followed by exact expected-old-SHA local ref deletion,
  rechecking all worktree registrations before branch deletion. Missing components need a
  matching saved cleanup intent. No recursive/force/prune fallback remains in WorktreeManager.
- Natural REDs found two additional early-authority gaps: an unknown removal-purpose value
  borrowed a valid publication receipt and deleted its fixture; an unknown checkpoint schema
  resumed target mutation before the transition validator rejected it. Both now refuse at
  entry. `continuation-authority-red-02.trx` contains the two intended preservation/boundary
  assertion failures; restored/final GREENs are listed below. The earlier schema RED hit a
  Shouldly error formatter and is not credited as assertion-specific evidence.
- Legacy raw removal and TTL cleanup refuse without typed authority. Residue classification
  no longer ignores untracked files because of old success events and retains legacy residue.
  Failed-add rollback and stale-registration healing preserve uncertain state instead of
  deleting it. This is conservative protection, **not the planned positive creation-recovery
  implementation**.
- Dispatcher admission holds the same repository lease through the durable claim. Local
  settlement/merge-back also takes it and supplies a distinct LocalMerge removal request.
  Card-review commit/push acquires repository exclusion. The final caller scope passes 29/29,
  including a real held-file-lease refusal before the PR caller's commit/push. Its two old PR
  harnesses now register the real lease and use disposable Git worktrees. These integrations
  still need the complete two-direction/multi-process/caller matrix.
- Rebase/local-target/push children have persisted PID plus start identity. Cancellation kills
  and awaits the owned process. Recovery rejects live/unknown recorded children without killing
  unrelated/reused PIDs. **This does not yet fence admission against a surviving child before
  the recovering landing service runs**, and does not journal every verifier/cleanup child.
- Added AlreadyPresent=29 without renumbering existing events. Task detail returns structured
  `landing` evidence and the drawer shows publication and cleanup separately. Retry text names
  its mode and does not claim a push on a no-push path. Post-publication failure handling retains
  historical publication. Successful terminal cleanup evidence/event/pending changes share the
  final SaveChanges transaction. Refusal/conflict terminal atomicity still needs full F07 work.
- Verifier uses `dotnet run --project tests/Antiphon.Tests`, isolated SDK artifacts outside the
  source tree, and requires a fresh nonzero passing TRX for selected tests. It performs no
  recursive deletion of pre-existing build output. A tiny real TUnit fixture passes **3/3**
  selected-pass, selected-failure and zero-match cases, and checks pre-existing `bin-land`
  bytes plus absence of source-tree obj output. Full V34 integration/ownership/cancellation
  companions remain pending.
- F2 remains explicitly retired: StageOutcomeBackfillService has no hosted registration and
  never creates stage rows from historical prose. Documentation and API/drawer descriptions
  distinguish confirmed publication, unconfirmed local advance and cleanup residue.

## Remaining implementation and acceptance

1. Finish standing admission fencing for surviving children after server death; journal and
   account for verification, cleanup and other mutating children. Execute real worker-termination
   cuts C03/C05/C09/C12/C14/C16/C17/C24 and both-order writer races. In-process exceptions are not
   credited as OS-crash coverage.
2. Implement the separate creation-rollback ownership protocol and supported recovery. Retaining
   ambiguous state is safe but does not satisfy the positive V25/R17 companion. Migrate obsolete
   split landing APIs/tests and legacy removal expectations to their typed replacements.
3. Complete per-boundary metadata/source/target guards and corresponding independent mutations,
   including local child merge target fencing, all direct removal/caller cases and admission
   variants. Complete F01-F07, crossed-operation receipts, unknown schema and notification faults.
4. Finish structured event history/consumer consistency, exact stage durations and refusal-stage
   accounting, terminal refusal/conflict atomicity, and every V33/V34/V35 case. The current
   historical timeline remains a textual event alongside the authoritative task evidence.
   Cleanup retries still append publication-classified outcome events with the same operation
   ID and an explicit CleanupRetry mode; the no-second-publication consumer/count contract needs
   its full implementation and tests, not merely truthful text.
5. Complete all required PC families and variants, the final named regression suites, Windows
   real-process/alias/locked-file evidence, and a reviewer-approved extended caller census.
   No aggregate passing count authorizes land while these rows remain pending.

## Evidence

Measured run counts and the per-family acceptance table are appended after verification.
The local evidence directory is `.antiphon/c448-continuation/`; logs/TRX paths are absolute
in its manifest. The refreshed searches are `.antiphon/continuation-caller-census-final.txt`
and `.antiphon/continuation-caller-census-extended.txt` (server, src, scripts and client).

The 65-test legacy run exposed 30 failures, including old force-removal/TTL/blind-abort
expectations and missing stage accounting. Subsequent changes fix some stage behavior;
this initial count must not be presented as a final rerun. See the final run table below.

## Extended destructive-caller census (F3)

| Path | Current disposition | Evidence still required |
|---|---|---|
| AgentTaskLandService -> AgentTaskLandingProtocol | Durable source/target/publication checkpoints and lease; old shortcut unused | Full V1-V17/V22-V23/V28-V34 and PC boundary matrix |
| Protocol -> IWorktreeManager typed removal -> GuardedWorktreeRemoval | Fresh DB receipt, remote proof, identity/content/ignored guards; ordinary remove; exact-ref CAS | Full direct-forgery/Windows/race variants V18-V20/V27/V36 |
| WorktreeManager raw Remove/TryRemove | Refuses typed-removal-authority-required | Legacy remover sentinel regression; complete old interface-fake census pending |
| WorktreeManager PruneStale -> raw TryRemove; WorktreeJanitorHostedService | Retains, no TTL authority | Full janitor/residue-job entry-point V24 companions |
| WorktreeResidueSweepService | Classifies uncertain legacy state as kept; no event-based untracked exemption | Full structured positive cleanup and stale-preview execution cases |
| WorktreeManager failed-add rollback/stale healing | Preserve checkout/ref/registration; forced recursive fallbacks removed | Positive ownership-based creation recovery remains incomplete |
| DelegationWorktreeService settlement/merge-back | Lease plus distinct LocalMerge low-level proof; no remote receipt or remote push | Exact parent/source mutation/race cases and all supported local behaviors |
| AgentTaskReplyService no-change reporting | Reports actual cleanup detail instead of unconditional removed | Full stage/accounting regression |
| AgentTaskLandService verifier | Unique external SDK artifacts; no recursive cleanup; owned cancellation | Real selected/nonselected/zero-test/verifier-crash matrix |
| AgentTaskDispatcher | Lease through normal/warm/helper claim paths inside DispatchOne | Both directions, all modes, restart/admission and alias matrix |
| CardReviewService commit/push | Lease around commit/push; one held-file-lease refusal test passes, existing PR paths restored in the fixture | Full races, aliases and process-survival variants pending |
| WorkspaceHookService.RunBeforeRemoveAsync | No production call site found by census | Positive route/caller proof pending |
| LandingGit Pin/Observe | Namespaced pins and independent push-endpoint observation; no remote source deletion | Remaining error/config/checkpoint mutants |
| GitService.DeleteBranchAsync -> WorkflowEngine.DeleteWorkflowAsync | Existing explicit workflow deletion is the only production caller found; includes local branch -D and remote push --delete | No landing call path found; full F3 refusal-family runtime trace matrix pending |
| StageOutcomeBackfillService | Retired, including appended AlreadyPresent | Existing F2 tests rerun in final checks |
| scripts/cleanup-build-junk.ps1 | Separate scheduled/manual script still treats age plus `bin-*` as disposable and uses robocopy mirror/recursive removal; no landing caller found | Unchanged and never executed here. Extended F3 review must adjudicate/protect this path; the new low-level land guard does not protect files from this separate script |
| scripts/nightly-run.ps1 and test-* fixture cleanup | Separate nightly-output and disposable fixture cleanup found by extended census | No live landing caller found; complete F3 ownership/caller classification remains pending |
| ShadowCopyStore, PtySessionAudit, GrokRulesFileStore | Process-runtime storage cleanup found by broad C# census | No landing caller found; not changed or executed by this task |

## Reproduction

Run from this branch's checkout. Execute these server commands sequentially; the test runner
builds the isolated output itself. Every named method/data row and its exact method filter is
listed in the continuation matrix. Do not substitute zero-test discovery or a build exit for
executed-test evidence.

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c448/ -- --treenode-filter '/*/*/AgentTaskLandVerifierTests/*' --report-trx --report-trx-filename verifier.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c448/ -- --treenode-filter '/*/*/(AgentTaskLandPublicationTests*)|(AgentTaskLandCleanupSafetyTests*)|(AgentTaskLandRecoveryTests*)|(AgentTaskLandConcurrencyTests*)|(AgentTaskLandPersistenceFailureTests*)|(AgentTaskLandStageOutcomeTests*)/*' --report-trx --report-trx-filename integrated.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c448/ -- --treenode-filter '/*/*/(AgentTaskLandRequestTests*)|(AgentTaskLandSweepTests*)|(AgentTaskLandingStateTests*)|(AgentTaskLandingPersistenceTests*)|(LandSourceIdentityTests*)|(LandingGitTests*)|(RepositoryMutationLeaseTests*)|(StageOutcomeBackfillTests*)|(WorktreeRemovalAuthorityTests*)|(WorktreeManagerTests*)|(WorktreeManagerSafetyTests*)|(WorktreeManagerGitIntegrationTests*)|(WorktreeResidueSweepTests*)|(DelegationWorktreeTests*)|(ProcessSpawnLimitTests*)|(TestLaneCategoryGuardTests*)/*' --report-trx --report-trx-filename regression.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c448/ -- --treenode-filter '/*/*/(AgentTaskDispatchBaseGuardTests*)|(DelegationScopeHoldTests*)|(SharedWriterLeaseProjectionTests*)|(CardReviewServiceIntegrationTests*)/*' --report-trx --report-trx-filename callers.trx
pwsh -File scripts/test-client.ps1 TaskDrawer.test
pwsh -File scripts/test-client.ps1 pipelineStageModel.test
npm --prefix client run build
```

## Interpretation of the final runs

- Latest integrated protocol: **57 executed, 57 passed, 0 failed, 0 skipped**.
- Latest caller scope: **29/29 passed**, superseding the initial 26/28 caller run after the
  missing lease dependency and real-Git setup were added to the two PR fixtures. This includes
  one additional held-lease/no-commit/no-push case.
- Real verifier: **3/3 passed**, including its rerun after adding the embedded probe category.
  The combined verifier/category rerun is **3 passed / 1 failed out of 4**: its only failure
  names the two pre-existing untagged classes, `GrokRulesLiveEvidenceTests` and
  `HerdrLaunchContextResolverTests`. The new probe omission seen in the earlier regression
  run was fixed and is absent from this rerun.
- Named regression run: **134 executed, 108 passed, 26 failed, 0 skipped**. Twenty-five failures
  remain associated with this change: two old wording assertions; two absence-as-success cases;
  five obsolete split-land/blind-abort cases; three residue eligibility/execution cases; four
  creation rollback/stale-healing cases; and nine raw-removal/TTL cases. The remaining failing
  test is the category census described above. The full method/assertion list is in the ledger.
  These failures are not dismissed as pre-existing, nor made green by declaring every retained
  worktree a supported recovery success.
- PC-7 has three separate intended REDs, each **2 passed / 1 failed out of 3**, followed by
  three rebuilt **3/3 GREENs**. Natural unsupported-purpose/schema regressions are **0/2 RED**
  at their intended assertions, then **2/2 GREEN**. The first aborted PC-7 restoration build
  and the first schema assertion-formatting error receive no acceptance credit.

The run tables retain superseded results for audit; do not add reruns together as distinct
coverage. Source and build identity is in
`.antiphon/c448-continuation/source-and-build-identity.json`: 43 changed source hashes, two
DLL hashes, exact restored ignored-content predicate, and rebuilt authority/test symbols.
`git diff --check` passes. No full V/R/C/F/PC acceptance claim follows from these scoped results.

<!-- FINAL-EVIDENCE -->

## Final measured evidence

Full per-family/variant ledger: [continuation matrix](2026-09-08-card-0448-code-continuation-matrix.md). Exact per-test filters, errors and absolute TRX paths are also in `.antiphon/c448-continuation/test-manifest.json`.

| Run | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| continuation-final-callers-01.trx | 28 | 26 | 2 | 0 |
| continuation-final-callers-02.trx | 29 | 29 | 0 | 0 |
| continuation-final-integrated-01.trx | 57 | 57 | 0 | 0 |
| continuation-final-regression-01.trx | 134 | 108 | 26 | 0 |
| continuation-final-verifier-01.trx | 3 | 3 | 0 | 0 |
| continuation-final-verifier-category-02.trx | 4 | 3 | 1 | 0 |

Client: TaskDrawer **15/15**, pipelineStageModel **34/34**, built client bundle succeeded. Logs: `.antiphon/continuation-client.log`, `.antiphon/continuation-pipeline-client.log`, `.antiphon/continuation-client-build.log`. These are component/build checks; no browser/E2E or real-stack validation was run.

PC-7: see the matrix for three independent mutation REDs and rebuilt restoration GREENs. Raw-removal regression: the original implementation failed **5/5** sentinel-preservation cases; the fail-closed replacement passed **5/5**, in `continuation-removal-red-01.trx` and `continuation-removal-green-01.trx`.

Rerun commands are saved in `.antiphon/run-c448-final.ps1`; the ignored-file mutation runner is `.antiphon/run-c448-pc7.ps1`. Both use the assembly-local process limiter and disposable fixture repositories/databases. No live production runner, remote source branch, shared-stack process or board was mutated.

The unresolved acceptance gaps above are implementation/test work, not a request for a user decision. Keep `next: code`; do not land or deploy this checkpoint.
