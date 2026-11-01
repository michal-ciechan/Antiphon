# Debug 28f88d1f: land rebase_conflict on merge-commit branch feat/card-task-7d513c4d

Outcome: root cause found and reproduced. `-Land` always runs a plain `git rebase <target>`. With merge
commits in the branch, git cannot fast-forward, so it flattens the branch. It replays the set's 107
non-merge commits, and the first one already conflicts (add/add on a plan doc that master carries
from its own commits). Recommended fix: the documented Merge-role fast-forward push, followed by a
same-SHA land re-POST that closes the request as AlreadyPresent. I prepared no linear branch, because
path (c) breaks both the review binding and CP-30 seed ancestry.

## (1) What the land service does with merge commits

- `server/Application/Services/AgentTaskLandingProtocol.cs:247`: the only short-circuit is
  "source already contained in the target" (`IsAncestor(source, targetBefore)`, giving
  exact_remote_containment/AlreadyPresent). There is no check the other way round, "the target is
  already an ancestor of the source, so fast-forward".
- `AgentTaskLandingProtocol.cs:269-270`: `git -c rebase.autoStash=false -c rebase.updateRefs=false rebase <TargetBeforeSha>`.
  It passes no `--rebase-merges` and no `-m`, and no flag preserves merges. On a conflict it runs
  `diff --diff-filter=U`, then `rebase --abort`, and returns reason `rebase_conflict` (`:281`).
- Policy: `-Land` is rebase plus fast-forward only, and a merge commit is "not this operation".
  See docs/orchestration-loop.md:464-467, docs/release-gates.md:25-26 and
  release-gates.md "Merging the rc line into master (Merge role)", plus CARD-1311 plan D-9.
  A merge is published by a Merge-role task that starts at the target tip and runs
  `git push origin HEAD:refs/heads/<target>` (a fast-forward).
- Reproduction (git 2.47.3, scratch detached worktree at 0493af102, since removed):
  - The range df92aa7b7..0493af102 has 113 commits, 107 of them non-merge. It contains 6 merges:
    8f721e971, d5222fae7 (seed 49b77d40b), d77ac25be (master bf0b874e6), d14f2e9f8, 43ccd0f5d and 0c88ee765.
  - A plain rebase gets to `Rebasing 1/107` and stops: `CONFLICT (add/add)` in
    `docs/superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md` at
    a7a666c72. Master already carries that file through its own commits (d355b0b70, 97be1fafe and
    3cca82a9e). The set's merge with master (d77ac25be) resolved that conflict once, but the flattened
    replay drops merge resolutions, so every such conflict comes back.
  - `git rebase --rebase-merges df92aa7b7` succeeds (127/127), and HEAD stays exactly
    0493af102923b4d0d34e736376fd67030dbe12cb with tree 06a5fd0925f5383f17a4c1abd582b041a55a9308. Every
    pick fast-forwards because master is the first parent.
- Ancestry facts: 49b77d40b (seed) is an ancestor of 0493af102 and not of master. ffeaa28f0 (reviewed)
  is an ancestor of 0493af102.

## (2) Smallest safe way to land this exact tree

- (a) Product change: at `AgentTaskLandingProtocol.cs:265` (before `PrepareLandWorktreeAsync`), when
  `IsAncestor(TargetBeforeSha, InputSha)` holds, skip the rebase and set `RebasedSourceSha = InputSha`
  so it is a pure fast-forward. Verification and publication stay unchanged. The risk is policy:
  merge commits would reach master through `-Land`, which overturns rebase-only and CARD-1311 D-9.
  Gate it behind an explicit request flag (for example `preserveMerges`) or the Merge role. The other
  option, adding `--rebase-merges`, is riskier. When the target has moved it re-merges without the
  recorded resolutions (no rerere), so it can conflict, or worse, silently produce a different
  resolution.
- (b) No land option preserves merges. delegate.ps1 `-Land` has no strategy or merge parameter, and
  the land body is `{expectedSourceSha, reviewEvidenceId, verify, recoverReviewedSource|adoptFromTaskId}`.
- (c) Linear squash with an identical tree: a successor SHA has no approval. Review evidence binds
  the exact SHA, subject task and ref, with no tree equivalence (`LandApproval.cs:107-112`,
  review_evidence_sha_mismatch/ref_mismatch). It would need a fresh Clean Final/Full Review at the new
  SHA. That 7d513c4d land was ExplicitCaller with no evidence, so the server would accept a caller-only
  land of a new SHA, but that launders the review. It also breaks CP-30 seed ancestry, because 49b77d40b
  would no longer be an ancestor. Land verification does not run CP-30: the verifier is
  `dotnet build; dotnet run --project tests/Antiphon.Tests` with a filter, or skip(non-code)
  (`AgentTaskLandingProtocol.cs:310`, and LandVerificationPolicy has no checkpoint use). CP-30 is
  only the commission gate. I did not prepare the branch, by choice.
- (d) RECOMMENDED, the documented Merge-role path. Master is still df92aa7b7, the first parent of
  8f721e971, so publishing 0493af102 is a pure fast-forward. It keeps the reviewed ffeaa28f0 commits
  and the seed ancestry exactly, and needs no new evidence or rebase. Commands (the operator, an
  orchestrator under autonomy, or a Merge-role task runs these; I ran none):

  ```
  git fetch origin master feat/card-task-7d513c4d
  test "$(git rev-parse origin/master)" = df92aa7b7886b278514dc73dc9e53dc461baa781
  test "$(git rev-parse origin/feat/card-task-7d513c4d)" = 0493af102923b4d0d34e736376fd67030dbe12cb
  git merge-base --is-ancestor origin/master 0493af102923b4d0d34e736376fd67030dbe12cb
  git push origin 0493af102923b4d0d34e736376fd67030dbe12cb:refs/heads/master   # rejected as non-FF if master moved
  # then close the stuck request (it resumes and settles AlreadyPresent via exact_remote_containment):
  pwsh -NoProfile -File scripts/delegate.ps1 -Land 7d513c4d-d9a6-48ec-bbc0-0bdca70afbe8 -ExpectedSourceSha 0493af102923b4d0d34e736376fd67030dbe12cb
  ```

  Why the re-POST works: `AgentTaskLandService.cs:105-110,164-175` lets the Blocked owner whose
  request is NeedsResolution with RecoveryMode None resume with the same SHA and no evidence, as the
  original was. The request goes to Queued and the task to Succeeded. The protocol at `:247` then sees
  the source contained in the new target, giving AlreadyPresent and cleanup. If master moved before
  the push, re-cut with a Merge task (`-StartRef` at the new master tip, `git merge` ffeaa28f0 plus the
  graph-record commit), as release-gates.md describes.

## (3) Second defect: the resolver went to the desktop with Grok

- Original 7d513c4d had runnerId=server2 (selection Explicit) and kind Grok. Helper 6d1dead7 had
  runnerId=desktop, runnerSelectionSource=null, kind Grok, complexity Medium. It failed with
  ProviderSignInRequired (`C:\Users\lndco\.grok\auth.json`).
- `server/Application/Services/AgentTaskService.cs:4216-4309` (`CreateMergeTaskAsync`):
  - The task initializer (`:4249-4307`) never copies `RunnerId`, `RequiredPlatform` or
    `RunnerSelectionSource` from `conflicted`. Placement therefore falls to the desktop default, and
    `WorkingDirectory = worktree` (`:4292`) is the desktop worktree path, so the child is
    desktop-bound by construction.
  - The kind comes from `ResolveServerChildRoutingAsync(Merge, cardId, Legacy)` (`:4244-4246`), a
    complexity-routing walk. It does not inherit `conflicted.AgentKind`.
  - Candidate admission (`ComplexityRoutingService.cs:524-549`) checks stage-forbid, holds and quota
    only, never provider sign-in. The sign-in gate (`RefuseUnauthenticatedGrokAsync`,
    `AgentTaskService.cs:4966`) runs on create/retry paths that a system spawn bypasses. The failure
    surfaced only at dispatch (`AgentTaskDispatcher.cs:4339-4368`, `TryFailGrokCredentialProbeAsync`).
- The helper's goal also tells it to `git rebase` onto master and force-push the owner branch. For a
  merge-commit branch that would flatten and rewrite the reviewed topology, so the helper failing to
  launch was arguably the safe outcome here.
- Card-ready fix: "CreateMergeTaskAsync must inherit the conflicted task's RunnerId,
  RequiredPlatform, RunnerSelectionSource=Inherited, remote mirror and AgentKind (routing only when
  that kind is held, over quota or known unauthenticated on that runner, with a warning event, never a
  silent reroute). Run the provider-auth and Codex-hold admission before Queued and leave the helper
  Blocked with the reason instead of failing it at dispatch. Refuse to spawn a rebase resolver when
  the owner branch contains merge commits, and point instead to the Merge-role fast-forward path."

## (4) What the stuck NeedsResolution request blocks

- It does not block the land queue or the repository lease. The land run aborted, the run returned,
  and the queue entry is released on the next POST (`AgentTaskLandService.cs:128-130`). Sibling
  inventory counts only Queued/Held/Running (`CardSiblingInventory.cs:31-32`).
- It does block, for task 7d513c4d only:
  - Interim verification of that owner (`InterimVerificationPolicy.cs:222`)
  - mirror cleanup ("recovery_debt", `CardWorktreeMirrorCleanupService.cs:164`)
  - abandoned-landing reconciliation (`AbandonedLandingReconciler.cs:97`)
  - an AwaitingReceipt Conflict notification
- There is no cancel-land endpoint. Ways to clear it:
  1. Re-POST the same SHA, as in (2)(d). After the fast-forward push this settles AlreadyPresent.
     Before the push it would re-rebase and conflict again, so do not retry first.
  2. Supersede it with a reviewed recovery request (Code owners only, so it does not apply to this
     Custom role).
  3. Cancel the owner 7d513c4d. The land sweep then marks the request Canceled
     (`AgentTaskLandService.cs:1255-1266`, task_no_longer_eligible). This loses the land, so it is
     a last resort.
- I cancelled and retried nothing.
