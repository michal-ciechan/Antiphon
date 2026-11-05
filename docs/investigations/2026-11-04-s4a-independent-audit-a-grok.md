# S4a independent audit A (Grok), 2026-11-04

Review task `03e7cd6d-813a-4737-b76c-cacc1c832e66`, round 1 of 3, stringency full, ordinary scope Interim (reading and targeted scratch git; the Unit lane was not run). Subject is master `37d92a8a0e86ed7200b3015df0d21dc6571a6672` as checked out on `feat/card-task-03e7cd6d`. The brief names no Code-owner GUID and no `-FromTask`. This pass reviews that tree. Product code and tests were not modified. Scratch git was 2.47.3 under `/tmp/s4a-audit-03e7cd6d` (deleted after the quotes below were taken). Nothing ran on the desktop.

The invariant under test: a task worktree is removed only when every byte in it is reproducible from refs that are already pushed.

Half A covers git-visible and git-invisible content, git state outside the working tree, fail-open defaults, and the feature switch. TOCTOU and the delete/retry mechanics are out of scope except where a proof hole forces them into view. CARD-1717 (Astra audit B, defects D-1 through D-5) is not re-reported. Those defects are the in-flight and residue-sweep switch bypass, an untracked file written after the final status, evidence changed after it was copied, a retry that deletes new files at the recorded set-aside path, and a Linux symlink race that deleted files outside the target tree.

## Verdict

The porcelain status proof can come back empty while the worktree still holds bytes that are not in any pushed commit. Five defects follow. D-1 is the one that deletes a normal tracked file on Linux today.

## D-1. Skip-worktree and assume-unchanged edits are accepted as clean

Severity: high. Reproduced on Linux.

`DesktopLegacyReleasePolicyService.PrecheckAsync` (`server/Application/Services/DesktopLegacyReleasePolicyService.cs:416-421`) and `LandingGit.InspectAsync` (`server/Infrastructure/Git/LandingGit.cs:841-858`) both treat this command as the cleanliness proof:

`git -c core.longpaths=true status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none`

Neither proof runs `git ls-files -v`. A skip-worktree (`S`) or assume-unchanged (`h`) modification leaves that status at exit 0 with empty stdout and empty stderr. The file stays readable, and its bytes differ from the blob that would be pushed. Guarded removal then deletes the worktree. `InspectRecoveryCheckoutAsync` already refuses any index tag other than `H ` (`LandingGit.cs:1010-1013`). The S4a proofs do not.

Scratch, git 2.47.3, fresh repo `/tmp/s4a-audit-03e7cd6d/p2/flags`, one committed `tracked.txt` containing `PUSHED`:

```
git update-index --skip-worktree -- tracked.txt
printf 'SECRET-SKIP\n' > tracked.txt
git -c core.longpaths=true status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none
# exit 0, stdout 0 bytes, stderr 0 bytes
git ls-files -v
# S tracked.txt
```

```
git update-index --assume-unchanged -- tracked.txt
printf 'SECRET-ASSUME\n' > tracked.txt
# same status command: exit 0, stdout 0, stderr 0
git ls-files -v
# h tracked.txt
```

Smallest fix: in both proofs, run `git ls-files -v -z` and refuse unless every record starts with `H `, the same rule as `LandingGit.cs:1010-1013`. A sparse-checkout `S` flag with no worktree bytes is held by that rule as well; see the sparse note below.

## D-2. `core.ignorecase=true` hides a second file of the same name

Severity: medium. The status blindness reproduced on Linux. Losing both names requires a case-sensitive directory, which is the default on Linux and an opt-in on Windows (`fsutil file setCaseSensitiveInfo`, or a case-sensitive volume). Git for Windows defaults `core.ignorecase` to true. The proofs never pass `-c core.ignorecase=false`.

Scratch `/tmp/s4a-audit-03e7cd6d/p2/case` holds `Readme.txt` (7 bytes, `README`) and `readme.txt` (5 bytes, `TWIN`) as two files. The repo's own `core.ignorecase` is `false`.

```
git -c core.ignorecase=true -c core.longpaths=true status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none
# exit 0, stdout 0 bytes, stderr 0 bytes

git -c core.ignorecase=false -c core.longpaths=true status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none
# exit 0, stdout: ?? readme.txt
```

With the true setting, both proofs accept the tree. Removal deletes `readme.txt`. Those bytes are not in the commit that contains `Readme.txt`.

Smallest fix: pin `-c core.ignorecase=false` on the status command and on `ls-files` in both proofs.

## D-3. An ignored `.env` inside a disposable directory is deleted with no copy

Severity: medium. The listing reproduced on Linux. The classification is the server classifier as it stands; this pass did not execute the C# classifier (no product build).

`WorktreeIgnoredContentClassifier.Classify` (`server/Application/Services/WorktreeIgnoredContentClassifier.cs:52-56`) tests protected patterns, then evidence, then disposable, and otherwise protects the path. `WorktreeCleanupSettings.DefaultProtectedIgnored` (`server/Application/Settings/WorktreeCleanupSettings.cs:34-39`) has no `.env` pattern. `DefaultDisposableIgnored` (`:12-16`) includes `**/node_modules/**`, `**/bin/**`, `**/obj/**`, `**/dist/**`, `**/out/**`, and `**/.tmp/**`. `server/appsettings.json:266-279` ships that disposable list and does not add a protected pattern, so the code default is what runs. The runner policy protects `**/.env*` and `**/.env*/**` even inside those directories (`src/Antiphon.SessionRunner/RunnerWorkspaceIgnoredContent.cs:19-22`). The server comment on the protected list says those names refuse removal wherever they sit, including inside a disposable directory. `.env` is missing from that list. `WorktreeIgnoredContentClassifierTests` already pins `client/node_modules/x/y.js` as disposable.

Scratch `/tmp/s4a-audit-03e7cd6d/p2/envcase`, gitignore of `.env` and `scratch/`, `core.excludesFile` naming `global-secret`, `info/exclude` naming `exclude-me.txt`:

```
git -c core.longpaths=true status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none
# exit 0, stdout 0, stderr 0
git ls-files --others --ignored --exclude-standard
# .env
# exclude-me.txt
# global-secret
# node_modules/pkg/.env
# scratch/notes.txt
```

`.env`, `exclude-me.txt`, `global-secret`, and `scratch/notes.txt` match none of the three lists, so they are protected and the whole tree is kept. `node_modules/pkg/.env` matches `**/node_modules/**` and does not match a protected pattern, so it is disposable: no evidence copy, then deleted. The same is true of `bin/.env`, `dist/.env`, `obj/.env`, and `out/.env`. The destroying tree is one whose ignored paths are all disposable, so nothing else trips `ignored_content_preserved` (`GuardedWorktreeRemoval.cs:120-121`).

Smallest fix: add `**/.env*` and `**/.env*/**` to `DefaultProtectedIgnored`. Protected is tested first, so those names win inside `node_modules`, `bin`, `dist`, `obj`, and `out`.

A notes file or local database whose name is not a protected pattern and whose path sits under a disposable glob is deleted by the allowlist as written. One that sits outside those globs is listed and default-denied, which is what `scratch/notes.txt` did above.

## D-4. Ignored-listing stderr is discarded, so an incomplete census is accepted

Severity: medium. Reproduced on Linux. On this tree the same-user bytes survive, because a later walk throws before the move. The proofs still accept the census.

Scratch `/tmp/s4a-audit-03e7cd6d/p2/unreadable`: ignored `secret-dir/` mode `000`, containing `notes.txt` (`PRECIOUS-UNREADABLE`). The owner cannot list the directory.

```
git -c core.longpaths=true status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none
# exit 0, stdout 0, stderr 0

git ls-files --others --ignored --exclude-standard -z
# exit 0, stdout 0 bytes
# stderr: warning: could not open directory 'secret-dir/': Permission denied
```

The pre-check sees a silent empty status and would file the release (`DesktopLegacyReleasePolicyService.cs:418-421`). `InspectAsync` runs the ignored listing without `-c core.longpaths=true` and returns `ignored_status_error` only when the exit code is a failure (`LandingGit.cs:845-852`). It does not read `WroteStandardError` for that command. The omitted path is absent from `IgnoredPaths`, so the classifier never protects it.

What keeps the bytes today is `WorktreeNestedRepositories.Find` (`GuardedWorktreeRemoval.cs:440`), which enumerates every directory and does not catch `UnauthorizedAccessException`. That throw hits `RemoveDirectoryAsync`'s outer catch and returns `cleanup_inspection_error` before the move-aside (`GuardedWorktreeRemoval.cs:243-246`). `directoryGone` stays false. The policy records a refusal, and the file is still at its original path. An unreadable ignored file (mode `000`, parent readable) is a different case: status is empty and silent, and `ls-files` lists `secret.txt` with empty stderr, so default-deny keeps the tree.

The census is still wrong. A walker that skips an inaccessible directory, or a deleter that can enumerate a directory git could not list, deletes the omitted file with no copy, because the allowlist never saw the name. `PathLimit` (`WorktreePathLimit.cs:14-17`) still refuses a Windows path of 260 characters or more under the longer set-aside name before anything moves, which is the CARD-1714 hold for a deep path. It does not read git's stderr.

Smallest fix: treat ignored-listing stderr the way status stderr is treated, and refuse `status_untrusted` before classification or the move. Pass `-c core.longpaths=true` on that `ls-files` as well.

## D-5. Windows alternate data streams are never scanned

Severity: low. Windows only. Not executed on this Linux runner.

No product C# calls `FindFirstStreamW` or otherwise reads `::$DATA` siblings. Git's status and `ls-files` listings do not include them. `WorktreeNoFollowDelete.MarkDeleted` (`server/Infrastructure/Git/WorktreeNoFollowDelete.cs:118-130`) sets the delete disposition on the file. On Windows that removes every stream of the file. Stream bytes are not in the pushed blob.

Smallest fix: on Windows, before the move-aside, `FindFirstStreamW` each file and refuse when any stream other than `::$DATA` exists.

## CARD-1708, confirmed and not numbered

Reflog-only commits are a known parked exposure. Scratch git 2.47.3, status command as in D-1, each repo's own origin.

Amend (`/tmp/s4a-audit-03e7cd6d/p2/amend`). After amend and `update-ref` deletion of `refs/heads/task`, HEAD is detached at `2bc257594200c3fce844a7b214c18914a890c57b` (`origin/task`). Status exit 0, stdout 0, stderr 0. Pre-amend `fa91ad4` is a commit, is not an ancestor of `origin/task` (`merge-base --is-ancestor` exit 1), and appears in the HEAD reflog. `git show-ref` has no `refs/heads/task`.

Reset (`p2/reset`). Status empty. HEAD `b4e10de57a0ad020d16b488eb2a74f1f4f6c1f58` is an ancestor of `origin/task`. Lost commit `4641b11` is not (exit 1), is still a commit, and is in the branch reflog. `extra.txt` is gone from the worktree.

Detached commit, then checkout of the pushed branch (`p2/detach`). Status empty. `symbolic-ref HEAD` is `refs/heads/task`. HEAD equals `refs/heads/task` at `a27941915b0d9329bf00fb827a8f9f3eab4f8925`. Detached commit `c716780e5b4708799e20492425fc0254169c826e` is not an ancestor of `origin/task`, is absent from the branch reflog, and is present in the HEAD reflog. `only.txt` is not on disk. The per-worktree `logs/HEAD` is the only name, and it dies with `common/worktrees/<name>/` when the worktree administrative directory is removed. `CompleteBranchAsync` deletes only `source.SourceFullRef` via `update-ref --no-deref -d` (`GuardedWorktreeRemoval.cs:310-311`).

A currently detached HEAD is refused. `IdentityAsync` maps `symbolic-ref -q HEAD` exit 1 to `detached_head` (`LandingGit.cs:1270-1271`), which is the X-26 hold. The parked hole is a commit that is no longer HEAD and is reachable only from a reflog.

## What was tested and held

- `status.showUntrackedFiles=no` does not hide an untracked file when the proof passes `--untracked-files=all`. Scratch: stdout `?? new-untracked.txt` (21 bytes), stderr 0.
- Ordinary ignored precious files outside the disposable globs (root `.env`, `info/exclude`, `core.excludesFile`, a subdirectory gitignore) are listed and default-denied. One protected path refuses the whole removal (`GuardedWorktreeRemoval.cs:120-121`). Evidence retention does not run in that case.
- Unreadable ignored file, mode `000`, parent readable: status empty and silent; `ls-files` lists the name with empty stderr; default-deny applies.
- Sparse checkout, git 2.47.3, non-cone `keep/*` on `p2/sparse2`. After `outside.txt` was rewritten, status stdout was ` M outside.txt` (15 bytes) and `ls-files -v` showed `H outside.txt`. A clean filter (`p2/filter`, a `strip.sh` clean filter that drops `LOCAL-ONLY`) likewise shows ` M tracked.txt`. The status proof holds both.
- Submodule (`p2/subparent`). Parent status empty. `git ls-tree HEAD vendor` is `160000 commit f6377f0133df2e25905749df1ac0fc11fe79bb92` (subject `local only`). `vendor/.git` is the file `gitdir: ../.git/modules/vendor`. `Find` adds that directory to Protected (`GuardedWorktreeRemoval.cs:440-442`), so removal refuses. `origin/master` did not resolve in that scratch.
- Nested git repo and a nested worktree (`p2/nestedwt`). After `inner/` is ignored, parent status is empty, `ls-files --ignored` lists `inner/`, and `inner/.git` is `gitdir: .../worktrees/inner`. Default-deny plus the `.git` marker refuse removal.
- Unusual names on Linux (`p2/names`): status lists `?? CON`, `?? link-out`, and `?? trail ` (trailing space), stderr 0. An ignored symlink or junction is refused when classification marks it evidence or disposable, because `ReparsePoints` does not follow links (`WorktreeIgnoredContentGate.cs:37-52`). The pre-check also refuses a reparse point at the worktree root (`DesktopLegacyReleasePolicyService.cs:399-401`). Trailing-dot names and the Windows device behaviour of `CON`/`NUL` were not executed here.
- Stash and a second local branch (`p2/other`). Status of `task` empty. `refs/stash` is commit `27d0265c2ec997ad1cdcc20cd74566d5c1c3e780`. Branch `other` (`15e8918`) is not an ancestor of `origin/task`. Branch deletion removes only the task ref, so the stash, the other branch, tags, and notes in the common git directory stay.
- In-progress merge (`p2/ours`): `git merge -s ours --no-commit` left status empty (stdout 0, stderr 0), `symbolic-ref HEAD` at `refs/heads/task`, and `MERGE_HEAD` present. The pre-check would file. `HasSequencerAt` (`LandingGit.cs:811-819`) sees `MERGE_HEAD` and `InspectAsync` returns `active_sequencer` (`:836`) before any delete. The same directory list covers `rebase-merge`, `rebase-apply`, `CHERRY_PICK_HEAD`, `REVERT_HEAD`, and `sequencer`. Bisect detaches HEAD, which `IdentityAsync` refuses as `detached_head`.
- Long paths. `WorktreePathLimit.System` is 260 on Windows and null on Linux (`WorktreePathLimit.cs:14-17`). `FirstTooLong` measures every entry under the longer `.removing-` name and `GuardedWorktreeRemoval` returns `worktree_path_too_long` before the move (`:147-148`). The policy holds that row (`DesktopLegacyReleasePolicyService.cs:345-351`). This is the CARD-1714 close of the old MAX_PATH status hole. Enabling `core.longpaths` does not raise the limit.

## Fail-open inventory

These paths fail closed. Line numbers are in the files named above.

- Invalid `WorktreeLegacyRelease` settings: `ReadSettings` returns disabled (`DesktopLegacyReleasePolicyService.cs:101-107`). A tick with settings null or `Enabled` false returns before any row (`:128-129`), except the operator preview.
- Status non-success or nonempty stdout: `source_dirty` (`:418-419`) and, in `InspectAsync`, `status_error` or `source_dirty` (`LandingGit.cs:843-844`, `:856`).
- Status stderr: `status_untrusted` (`DesktopLegacyReleasePolicyService.cs:420-421`, `LandingGit.cs:857-858`). `WroteStandardError` is stderr length (`LandingGit.cs:285`).
- Short, empty, or malformed `ls-remote` tip, missing local object, `merge-base` exit other than 0: `remote_unverified` or `unpushed` (`DesktopLegacyReleasePolicyService.cs:436-454`). Ancestor exit 1 is `unpushed`.
- HEAD different from the local branch tip: `source_changed` (`:426-428`). Detached HEAD at removal: `detached_head` (`LandingGit.cs:1271`).
- `IOException`, `UnauthorizedAccessException`, `ArgumentException`, `TimeoutException` in the pre-check: `inspection_error` (`:457-459`). The same set in removal: `cleanup_inspection_error` (`GuardedWorktreeRemoval.cs:243-246`). `ActAsync` exceptions other than the caller's own cancellation are deferred and retried (`DesktopLegacyReleasePolicyService.cs:370-381`).
- Registration query failure throws `registration_query_error` (`GuardedWorktreeRemoval.cs:423-424`). Nested-repo attribute errors other than not-found propagate (`WorktreeNestedRepositories.cs:93-98`).
- Active sequencer: `active_sequencer` (`LandingGit.cs:836`).

The open fail-opens are D-1 (empty status accepted for an index tag other than `H`), D-2 (`core.ignorecase` left at the repo default), and D-4 (ignored `ls-files` exit 0 with a stderr warning accepted).

## Feature switch

`WorktreeLegacyReleaseSettings` defaults `Enabled` and `Execute` to true, and `server/appsettings.json:230-242` ships both true. `Execute` false makes the tick pre-check and record `would_release`, then return before `ReleaseSystemAsync` and `TryRetireAsync` (`DesktopLegacyReleasePolicyService.cs:290-298`). `StopReason` re-reads settings and returns `tick_stopped_execute_off` before the next row when Execute has been turned off (`:519-520`). A row already inside `ActAsync` finishes that call. The residue sweep calls `TryRetireAsync` when its own `WorktreeResidue:Execute` is true and does not read `WorktreeLegacyRelease` (`WorktreeResidueSweepService.cs:205`). That bypass is CARD-1717 D-1 and is not re-filed. Shipped `WorktreeResidue:Execute` is false (`server/appsettings.json:224`), so the sweep does not delete on this configuration.

## Advisories

- The proofs do not pin `-c core.fsmonitor=` or `-c core.untrackedCache=false`. No clean reproduction was made.
- LFS objects that were never pushed, and partial clones, were not executed.
- Disposable globs delete every non-protected ignored name under them. D-3 is the `.env` parity gap. A database or notes file under `dist/` or `node_modules/` with some other name is the allowlist as written.

## Scope note

No Unit lane, no checkpoint run, no build slot. PCs stay pending. `reviewedSourceClean` is false: a clean git status of this checkout is not a SHA-validated checkpoint receipt.
