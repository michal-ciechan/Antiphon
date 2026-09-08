# CARD-0448 investigation: recovered task report

Recovered on 2026-09-08 by Plan task 3b3528b1 from the full `result` returned by `GET /api/agent-tasks/f86bd33b`. The original investigation artifact was absent from this checkout and the investigator's worktree. The report below is preserved verbatim; its statements about files changed and tests run describe the original investigation, not the recovery of this file. The current implementation plan is [the CARD-0448 landing safety plan](../superpowers/plans/2026-09-08-card-0448-land-publication-safety-plan.md).

---
Confirmed ac24e1af’s false success: land trusted a stale recorded branch while the reviewed work was on detached HEAD; the second incident’s alleged remote deletion is not supported by the land implementation or task events.

1. **ac24e1af took the wrong “already landed” shortcut.**

   The review session `c884e1d3-4641-441f-9d25-f5c88ee8c3af` provides direct evidence:

   - Transcript sequence 3 attempted to check out `feat/card-task-da22d5af`; Git refused because another worktree already owned it.
   - Sequences 6–7, at **09:49:10–12 UTC**, executed `git checkout --detach a18f250d` and confirmed detached HEAD.
   - Sequence 114, at **10:26:00 UTC**, still showed `a18f250d`.
   - The task row nevertheless retained `WorktreeBranch = feat/card-task-ac24e1af`. That original branch had started at `3b7efe32`; detaching did not advance it.

   `AgentTaskLandService.RunAsync` checks `IsAlreadyLandedAsync` **before** preparation, verification, sibling warnings, or incrementing `LandAttempt` (`server/Application/Services/AgentTaskLandService.cs:123`).

   `DelegationWorktreeService.IsAlreadyLandedAsync` checks the **recorded branch**, never the actual worktree HEAD (`server/Application/Services/DelegationWorktreeService.cs:172`). The original base was already contained in master, so it entered cleanup.

   The historical evidence matches this path precisely:

   - Land requested **10:27:07 UTC**.
   - `LandedWithResidue` recorded **10:27:13 UTC**, naming `f202fcc1`, without the normal path’s `verify:` text.
   - `LandAttempt` remains **0**.
   - Master’s reflog shows `f202fcc1` landed at **09:56:00 UTC**, over 31 minutes earlier.
   - `git rev-list --count f202fcc1..a18f250d` returns **4**.

   This was neither a concurrent merge race nor a silent stale-base refusal. It was a false inference that the reviewed work was already landed. The four commits were on detached HEAD and the sibling branch—not on the recorded review-task branch.

2. **Cleanup and reporting compound the identity error.**

   `CleanupAlreadyLandedAsync` (`DelegationWorktreeService.cs:240`) optionally pushes an ahead local target. When nothing needs pushing, it reads the existing target SHA and still returns success. `AgentTaskLandService.cs:269` formats that as **“landed … pushed”**, although this attempt performed no push.

   `WorktreeManager.TryRemoveAsync` (`server/Infrastructure/Git/WorktreeManager.cs:461`) then:

   - Resolves the current branch, falling back to metadata when HEAD is detached.
   - Attempts forced worktree removal and directory deletion.
   - **Only afterward** checks branch ancestry before deleting the local branch.

   Thus, the stale branch passes the ancestry guard while the detached worktree can be destroyed. The incident’s directory-lock residue prevented complete directory removal; its “branch deleted” text describes **local** deletion.

   Merely adding a remote-push check is insufficient: the stale recorded branch was already on origin/master. Cleanup must validate and preserve the **actual worktree HEAD and contents**, not just the recorded branch.

3. **4e84689f’s refusal behaved differently; remote deletion remains unproven.**

   Its task events show:

   - Settlement kept the branch because no merge target was set.
   - Land requested **10:36:54 UTC**.
   - `LandRefused` at **10:36:58 UTC**: origin/master ahead of local master.
   - `LandAttempt = 1`; no landed or cleanup-success event.

   The refusal is explained by Git history: manual recovery had pushed origin/master to `e138c41b` at **10:35:43 UTC**, while local master remained `f202fcc1`. `PrepareLandAsync` detects exactly that condition (`DelegationWorktreeService.cs:111`). The caller records refusal and returns before finalization or cleanup (`AgentTaskLandService.cs:172`).

   **There is no remote branch deletion in this land path.** Worktree cleanup uses local `git branch -D`. `scripts/delegate.ps1`’s Land arm only queues the API request. Settlement’s `CommitAllChangesAsync` commits locally without publishing the source branch.

   The separate `GitService.DeleteBranchAsync` does run `git push origin --delete`, but its production caller is `WorkflowEngine.DeleteWorkflowAsync`, not land.

   Consequently, absence from origin does not establish deletion. The available evidence is consistent with the source branch **never having been pushed**. Commit `3dfbb10a` has the automatic settlement message and timestamp; recovered `60c4fcd1` retains its author identity/time with a later rebase committer time. Neither proves prior remote publication.

   Attribution of an actual remote deletion would require evidence of an earlier successful source-branch push and a subsequent delete operation. I found neither. Plan should not encode “refusal runs remote cleanup” as the established root cause.

4. **Relationship to CARD-0442.**

   Missing worktree continuity contributed to the manual detached checkout, but the landing defect is independent. The reviewed implementation predates CARD-0442: relevant changes trace to CARD-0328 and CARD-0331. `f202fcc1` itself is a CARD-0442 **documentation** commit.

   Continuity improvements can reduce this trigger. They cannot make missing refs, detached HEAD, branch switching, dirty worktrees, or crash recovery safe without fixing landing’s source-identity contract.

5. **Required fix design for Plan.**

   - **Validate source identity before every shortcut or destructive operation.** Resolve the worktree registration, symbolic HEAD, actual HEAD SHA, recorded branch SHA, and dirty state. Refuse detached or mismatched identity with explicit diagnostics and preserve the work. Do not silently adopt a sibling branch from report prose.
   - **Persist landing evidence.** Record original source SHA, rebased/verified SHA, target-before SHA, destination ref, and independently confirmed remote target SHA. Missing branch or failed Git commands must mean unknown/refused, not already landed.
   - **Separate outcomes.** “Landed” means confirmed publication of the intended result. “Already present” means positively verified containment of that same result. “Cleanup retry” requires durable prior publication evidence. Never claim “pushed” merely because a target ref resolves.
   - **Confirm publication before cleanup.** Push an explicit destination ref, read the remote ref independently, and verify it contains the intended post-rebase commit. Handle concurrent remote advancement through containment checks; target movement alone proves nothing.
   - **Guard deletion before removing the directory.** Revalidate actual HEAD, branch identity, and uncommitted/untracked work before `worktree remove --force`. Preserve recovery refs across rebase and crash windows. Cleanup must not delete a different branch selected through current-checkout fallback.
   - **Serialize and revalidate mutations.** Protect each repository’s landing operation and detect source/target changes during verification. The early cleanup shortcut currently also bypasses the Shared-writer hold.
   - **Preserve recovery behavior explicitly.** A crash after local fast-forward but before push must resume publication; a crash after confirmed push may retry cleanup. Neither state should be inferred solely from branch disappearance.

   Required regression cases: detached HEAD with unique commits and an ancestor recorded branch; switched branch; missing recorded ref; dirty ancestor worktree; failed fetch; stale local target; rejected push; source movement during verification; concurrent target advancement; and crash/retry boundaries. Assert preservation of both branch refs **and worktree contents** on refusal. Include an explicitly published remote source branch to verify it survives refusal.

   Existing tests cover happy landing, push rejection, and cleanup retries, but the inspected cases do not cover the detached-HEAD identity mismatch that caused this incident.

No source files changed. No builds or tests run; findings use source inspection, persisted task/transcript evidence, and read-only Git history checks. Working tree is clean.

--- next stage ---
next: plan
handoff: Design CARD-0448 around verified source identity, durable publication evidence, and pre-deletion worktree guards. ac24e1af reviewed detached a18f250d while its recorded branch stayed at an ancestor base, triggering false already-landed cleanup. 4e84689f refused before cleanup; remote deletion is unproven and land contains no remote-delete operation.
