# CARD-2052 Final Review, round 1

Return to Code. Cleanup can authorize an unlink using a tracked-file listing it has already observed to be stale. The performance improvement is genuine.

Code owner `800d74b2-65f4-45ae-8c4f-5d1e8261ade6`. Reviewed `ecc9bfaa5bfa91c80aa022bca09f86aa7b693a91..c0173afa346eb64aa022f80e4a749504e03179d9`.

## Deletion blockers

| Where | Failure | Why it matters | Fix and acceptance |
|---|---|---|---|
| D-1. `SafeDelete.cs` lines 333–354, `OwnedOutputLedger.cs` lines 113–121 | When the post-read stamp differs, the code clears the cache and still scans the same listing for the current entry. | If Git staged that entry during the read, it can pass as untracked. Staging preserves its content and creation identity, so the anchored unlink can remove it. The final check-to-unlink race predates this change. The new branch detects instability and proceeds anyway. | Refuse the pending unlink or obtain a new, verified decision. Protect the decision through unlink against concurrent Git writers. Verify file survival when the index changes during a real removal. |
| D-2. `TrackedIndexScope.cs` lines 50–58 and 103–105 | The cache treats matching filesystem metadata as proof that index contents match. | An equal-size in-place index update can retain the inode and both timestamps within one filesystem tick. Ordinary single lock-file replacement changes the inode. The cache holds neither an index generation nor a content identity that rules out the equal-stamp case. | Bind reuse to an authoritative index version or decline reuse on ambiguous filesystems. Verify the owned file survives an ambiguous version change. |
| D-3. `TrackedIndexScope.cs` lines 106–127, `InProcessGitStatus.cs` lines 350–354 | The cache omits included config files. In a linked worktree it also omits the common directory’s `config.worktree`, which the fresh reader checks. | A change to an included `core.worktree` can alter which absolute paths the index protects while every stamped config file still matches. The cached negative bypasses the fresh reader’s fallback to Git. | Bind the answer to the effective repository and worktree mapping, or decline reuse when it cannot be established. Cover linked-worktree config invalidation through cleanup. |

The tool calls the correct library through `CleanOwnedOutputs → CheckpointDelete.Apply → CheckpointOutputLedger.Remove`. Only `Remove` creates the scope. Older-run pruning also reaches it when custody permits. Owned-prefix, link, creation-record, content and anchored-unlink checks are still in the path. The remaining diff contains instrumentation, two tests and eight generated index entries.

A fresh lookup defers sparse, skip-worktree and intent-to-add layouts to Git. Windows uses a case-insensitive comparer. Linux keeps ordinal matching, including on a case-folded mount. The new staging test covers an ordinary add between two completed preflights in a plain repository. Neither new test establishes file survival in D-1 through D-3.

## Performance and red-first evidence

Code session `a7784931-cddf-4ae6-85f3-6ee10503d5c7`, tool results 46 and 114, records **369.22 s → 77.26 s** for 2,190 files (1.716 GB), one output deleted and zero refusals in each probe. Each is one sample. The first probe finished at 20:58:20 UTC, before the red commit at 21:01:30 UTC. The Code report’s attribution of that first timing to the red commit is unsubstantiated.

At red commit `07dfd665784d96b0b07f07ebd9ce818e935821e8`, the named witness failed with 53 reads against the limit of 10 (transcript results 73–79). At fix `35bd6346983a6b50f8b6dc3820f41369ba2f861f`, both witnesses passed (results 97–105). The prior CARD-2050 advisories A-1 through A-8 concern the intermediate-output cache and its documentation. A-9 affects this receipt.

## Independent verification

The eight named classes ran together in checkpoint `20261122-215504-c468`, TRX `53e99bf8-cec6-4627-b209-8ecd8d4df7bf`. All eight had passing identities, including both new witnesses. **148 passed, 0 failed, 4 Windows-only skips.** The build and row each held a slot. The run took 13m23s, including 210s of slot wait. Output cleanup took **109.805 s** and deleted the row’s output. The validator exited 2 with `skip_identity_unlisted`. Native Windows behavior remains unverified.

CHECKPOINT CP-1 commit=c0173afa346eb64aa022f80e4a749504e03179d9 build=ok filter=/*/*/(OutputCleanupTests*)|(CheckpointOwnedOutputTests*)|(HostCleanupCheckpointExtractionTests*)|(CheckpointToolCopyCleanupTests*)|(PreflightCommandLineBudgetTests*)|(SafeDeleteProcessBoundaryTests*)|(HostCleanupCheckpointCustodyTests*)|(HostCleanupCheckpointCompatibilityTests*)/* executed=148 passed=148 failed=0 skipped=4 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-215504-c468/rows/CP-1/run.trx slot=granted waited=105s dirty=0 source=c0173afa346eb64aa022f80e4a749504e03179d9 sourceState=clean buildSource=verified

The Code brief and report supplied no closed CP table or CP lines. CP-1 above expresses the eight named classes for this Review. The explicit short-selection instruction left the additional Final guards and impacted rows pending. The impact selector returned 41 class names across three project filters, with zero migration downgrade rows. PCs remain pending for SourceLanding. No product or test source changed in Review.

Two preliminary attempts ran zero tests. I stopped `20261122-215201-b301` before build admission to correct a missing duration unit. `20261122-215313-7285` refused the earlier attempt’s existing output. The completed run used a fresh output path. My initial evidence-diff invocation built its launcher outside the slot. The gated rerun recorded three commits and zero violations. The checkpoint-tool bootstrap also held a build slot. Merge-tree succeeded against master `b7810daa5088f7f6161e387cb4fa62f3315a66f8`, tree `0ae4dcab0dad9701fb5b1b269c0ddf0aefb7ebf8`.
