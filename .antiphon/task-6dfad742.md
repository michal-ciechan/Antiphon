# Code 6dfad742: CARD-1714, removal re-check trusts stderr; refuse paths past MAX_PATH

**Outcome: both items done test-first, and every named class is green on server2 (Linux).** CP-25 (Windows
native) and the whole Unit lane were not run; see "Not run".

Branch `feat/card-task-6dfad742`. Worktree `/work/worktrees/task-6dfad742`. Base
`5233faefdedd2d024fcbbabeb02e1e9146b03a70` (Debug 52410842 tip; Review ff7ab869 CLEAN at 29e6816ce).

| What | SHA |
|---|---|
| red (witnesses + seam, server code = base) | `0cd3f164d5dae1586fdd44a0ec870f6ffc6aecf4` |
| green / tested | `a670e82a9d0d5aef73bd168f5d0d5bcc7fbfa3b9` |

## Item 1: `LandingGit.InspectAsync` (lands and the removal's re-check)

- The status now runs as `git -c core.longpaths=true status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none`.
- A clean listing (exit 0, empty output) that came with **any** stderr output returns
  `new(null, "status_untrusted", diag(git status --porcelain=v1, exit 0))`.
- The check comes after `source_changed` and `source_dirty`, so a dirty tree keeps its existing code.
- **Path reused:** every caller already turns a non-null `Reason` into a refusal under that code. No caller
  needed new handling. `cleanup_inspection_error` is the *thrown*-I/O path (the catch in
  `RemoveDirectoryAsync`). It does not fit, because this is a decided refusal with a stable code, and it
  matches the S4a pre-check's `status_untrusted`.
- Retries of a release whose command already started (policy :274 skips the pre-check) are now guarded by
  this re-check. The witness is `C1714_Removal_refuses_a_status_that_could_not_read_the_whole_tree(1|2)`,
  which covers both readings.

### Callers of `InspectAsync`

Before this fix, a stderr-warned clean status was treated as clean.

| Caller | Scope | Before | After |
|---|---|---|---|
| `GuardedWorktreeRemoval.RemoveDirectoryAsync` (first and final reading) | Full | proceeded to move-aside, unregister and delete | refuses `status_untrusted`; nothing moved or unregistered, branch kept |
| `CardWorktreeCleanupExecutor:49` | Full | cleanup proceeded | `RefuseAsync(endpoint, "status_untrusted")` |
| `AgentTaskLandSourceResolver:287` | IdentityAndStatus | land proceeded | it is not `source_dirty`, so no adoption-recovery branch; the land refuses `status_untrusted` with the diagnostic |
| `AgentTaskLandSourceResolver:353` / `:494` (after adopt reset) | IdentityAndStatus | accepted | `adopt_local_changed` |
| `AgentTaskLandSourceResolver:466` (before the CAS move) | IdentityAndStatus | accepted | refuses `status_untrusted` before the ref moves |
| `DelegationWorktreeService:767` (local merge source) | Full | merged | `MergeResult.Failed`, receipt `status_untrusted` |
| `DelegationWorktreeService:801` (prepared) | Full | accepted | `source_changed` |
| `DelegationWorktreeService:906` (local-merge removal) | Full | removed | residue `status_untrusted` |
| `TaskWorktreeRetirementService:626` | Full | read GitDirectory only | as for any refusal: falls back to `<repo>/.git`; the removal re-proves |
| `LandingGit:841/:1085`, `InspectRecoveryCheckoutAsync` | IdentityOnly | no status | unchanged |
| `ControlledLandingGit` and the `LandingRemovalPolicyControlTests` world | (own `InspectAsync`) | — | unchanged doubles, so the 25 ControlledLandingGit classes were not run |

**Test doubles.** These sites matched `args[0] == "status"` on the real InspectAsync path. They now use
`GitSubcommand.Of(args)`, which skips leading global options like `DesktopLegacyWorld.Subcommand`:

- LandSourceIdentityTests, LandingGitTests, LandingIdentityControlTests, LandingRegistrationFreshnessTests
- SettledWorktreeRemovalTests, WorktreeGuardedCleanupTests, SettledRemovalHarness, CompletedCardRemovalFixture
- AgentTaskLandRegistrationSafetyTests, AgentTaskLandRemovalMatrixTests

The change keeps the meaning of every status call that has no prefix. The C527 Reply and Park doubles
serve other git paths and were left unchanged.

## Item 2: refuse before the move-aside (option b)

- New `WorktreePathLimit` (public, `server/Infrastructure/Git/WorktreePathLimit.cs`):
  - `WindowsMaxPath = 260`. `System` is 260 on Windows and none on Linux, so the Linux behaviour is
    unchanged: no walk runs.
  - `FirstTooLong(tree, aside)` names each entry under the set-aside root (`aside + relative`). It returns
    the first entry with `length >= limit`, and it checks the aside root itself.
  - The walk is read-only, never descends a reparse point or symlink, and uses `AttributesToSkip=0`.
    `IgnoreInaccessible=false`, so an unreadable directory throws, which becomes `cleanup_inspection_error`
    (fail-closed).
  - On Windows it enumerates through `\\?\` or `\\?\UNC\` (for enumeration only).
- `GuardedWorktreeRemoval` takes an optional constructor parameter `WorktreePathLimit? pathLimit`. DI falls
  back to `System` when nothing is registered.
  - The check sits after the final reading's evidence comparison. That is before the command slot is
    consumed, before the set-aside record and before the move.
  - It returns `Finish("worktree_path_too_long", "path: <set-aside path>")`.
- **S4a:** a removal residue of `worktree_path_too_long` with nothing changed is recorded as a **Held** row
  (`DesktopLegacyReleasePolicyService.PathTooLong`) with the retry backoff (`StampRetry`). It is no longer
  Refused or Partial. The release stays active. Its spent intent means later ticks retry the removal, which
  refuses again.
- `docs/orchestration-loop.md`: the S4a Held reasons now include `worktree_path_too_long`, plus a note on
  the re-check's `status_untrusted`.
- Native (CP-25):
  - New `Long_path_ignored_tree_is_held_not_partial`.
  - N-2 `Long_path_never_leaves_an_unregistered_orphan` now expects `LastReason == worktree_path_too_long`
    with the directory present. Before, it expected Complete or `worktree_removal_incomplete`.
- Out of scope: a tree that an earlier pass **already** set aside and unregistered still goes straight to
  `Delete(aside)` (the pending branch). The guard cannot help a tree that has already moved. Option (a),
  `\\?\` in the delete, remains the follow-up.

## Runs (server2, one build-slot lease per build or run, UseAppHost=false, native output, TEMP=/tmp)

| Run | SHA | Selection | Result |
|---|---|---|---|
| build red | 0cd3f164d | tests/Antiphon.Tests | exit 0, 3m36s, waited=0s |
| red | 0cd3f164d | the 8 new cases | **7 failed, 1 passed** (the `under` control). Removal cases had Residue `null`: removed. The S4a row was `Removed`. Each LandingGit case was `Accepted`. |
| build green | a670e82a9 | tests/Antiphon.Tests | exit 0, 2m00s, waited=0s |
| A | a670e82a9 | CP-23 + guards + LandingGitTests, SettledWorktreeRemovalTests, LandingIdentityControlTests, LandSourceIdentityTests, LandingRegistrationFreshnessTests | **415: 414 passed, 0 failed, 1 skipped** (C459_ManagedRootRequired(junction), Windows-only). Policy 123, Settings 32 (CP-23 = 155), guards 47 = 39 + 1 + 5 + 2, LandingGit 75, Settled 82, IdentityControl 26, LandSourceIdentity 22, RegistrationFreshness 8 |
| B1 | a670e82a9 | LandingRemovalPolicyControl, DelegationWorktree, LandAdoption (incl. Concurrency), LandMonitoring, LandRequest, LandTargetRace (incl. Fake), AbandonedLandingReconciler | **250: 248 passed, 0 failed, 2 skipped** (C721/C665 Windows sharing). RemovalPolicyControl 94, DelegationWorktree 49, Monitoring 25, Request 30, Adoption 21+7, TargetRace 11+6, Abandoned 7 |
| B2 | a670e82a9 | LandRefRetention, LandReplacementApproval, LandingProtocolGuard, LandingProtocolHarness, StageOutcomeSummary, TaskWorktreeRetirementTests, WorktreeResidueSweepTests, WorktreeGuardedCleanupTests, AgentTaskLandRegistrationSafety, AgentTaskLandRemovalMatrix, CompletedCardWorktreeRemoval | **290: 287 passed, 0 failed, 3 skipped** (C448_V20 windows-file-handle, C665 locked-file and junction, all Windows-only) |

These runs go beyond the brief's list:

- In A, the four classes whose doubles changed, plus SettledWorktreeRemovalTests, which holds the new
  witnesses.
- In B2, the three classes whose doubles changed (RegistrationSafety, RemovalMatrix, and
  CompletedCardWorktreeRemoval through its fixture).

Other checks:

- `check-evidence-diff` gave violations=0 on both ranges: 5233faefd..a670e82a9 (commits=2) and 84e19071e..a670e82a9
  (commits=6, entries=1).
- `merge-tree` with `origin/master` (`f205fdcec`) is clean.
- `merge-tree` with `origin/feat/card-task-9f577891` (`abbda246b`) is clean. It changes the two
  `Delete(aside)` calls to `DeleteSettled` and touches WorktreeGuardedCleanupTests and
  LandingRemovalPolicyControlTests. Neither peer adds a new `args[0] == "status"` site.

## PCs pending for SourceLanding Mutation (method-scoped)

| PC | Mutant | Expected red |
|---|---|---|
| PC-1 | drop `-c core.longpaths=true` in InspectAsync | `LandingGitTests/C1714_Clean_status_that_wrote_stderr_is_untrusted` (pin) |
| PC-2 | drop the `WroteStandardError` refusal | `LandingGitTests/C1714_*` (3), `SettledWorktreeRemovalTests/C1714_Removal_refuses_*` (2) |
| PC-3 | remove the path-limit check in GuardedWorktreeRemoval | `SettledWorktreeRemovalTests/C1714_Path_past_*(over-once-set-aside)`, `C0824DesktopLegacyReleasePolicyTests/Path_past_the_delete_limit_holds_the_tree_not_partial` |
| PC-4 | measure the original path, not the set-aside name | `C1714_Path_past_*(over-once-set-aside)` |
| PC-5 | `>=` becomes `>` in `FirstTooLong` | `C1714_Path_past_*(over-once-set-aside)` (aside length == limit exactly) |
| PC-6 | the policy's Held mapping removed | `Path_past_the_delete_limit_holds_the_tree_not_partial` (Refused) |
| PC-7 | check after the move-aside or unregister | both item-2 witnesses (set-aside or unregistered asserts) |

Unpinned, for Mutation to discover:

- (a) `System` given a limit on Linux. No witness checks that Linux is unchanged beyond the whole suite staying green.
- (b) The walk following a symlink or reparse point. No witness.
- (c) The `WindowsMaxPath` value. Only CP-25 covers it.
- (d) A-1 analogue: the flag carries no text, so a text-narrowing mutant is not expressible here.

## Not run

- **CP-25** (C0824DesktopLegacyReleaseNativeTests, desktop lane) is the real proof for the Windows long path.
  It should show N-2b `Held source_dirty`, the new `Long_path_ignored_tree_is_held_not_partial`
  `Held worktree_path_too_long`, and N-2 `LastReason worktree_path_too_long`. Nothing ran on the desktop
  because qualification run b19a9880 is live.
- **Whole Unit lane**: not run. It takes about 3 h on loaded server2, past the 60-minute budget.
  This is deferred-to-final and has not passed.
- Checkpoint-tool receipts: none. There is no plan or checkpoint table, and the runs were direct
  build-slot runs as the brief prescribed. reviewedSourceClean stays false.
- rc/2026-11-06 needs this ported together with the 52410842 commits. Their order matters because
  `WroteStandardError` comes from 29e6816ce.
