# CARD-1105: rescue, discard, worktree removal, ref delete, and audit (2026-10-09)

Task 6f1e3bff (Debug), desktop seat. Volume `antiphon-runner_work` (OLD runner, image
`antiphon-server2/session-testing:4358939ecd85`, id `sha256:743279186cff4b4ca4507c6a64d7a139caced22dd2f3c757ced00048c9eba620`).
Throwaway `docker run --rm --user 1654:1654 --entrypoint /bin/bash` with
`GIT_NO_LAZY_FETCH=1 GIT_CONFIG_SYSTEM=/dev/null GIT_CONFIG_GLOBAL=/dev/null GIT_OPTIONAL_LOCKS=0`
and `core.commitGraph=false`. No container, volume, or deploy phase was stopped or removed.

## Outcome

Rescue branch `rescue/card-0835-0eafbbee` is on origin at `6f9b58ede2aeb40dda93cecfbd7f5b1ec1a3fc61`.
`worktrees/task-0eafbbee` is clean at the same HEAD and branch. Linked worktree `c1005-rebase-rX0auR` is gone.
17 superseded refs were deleted (2186 to 2169). The repaired audit replay exited 0 with 0 refusals.
Two `fa0190b4` refs were skipped because their tips are not the rescued commit; both are already reachable
from an advertised origin head, and the audit did not refuse.

## Preconditions

`GET /api/agent-tasks/pipeline` showed Deploy and Merge with nothing in flight, and no in-flight title was a
`deploy-server2.ps1` phase. On server2 no `deploy-server2` / `c590` / `c1008` process was running. Runner
`server2` was draining, `sessions=0`, `queuedTasks=0`. `docker top` of the old runner did not mention
`task-0eafbbee`.

Read-only mount of `antiphon-runner_work` at `/work` showed `ro,relatime`, and `touch` failed with
`Read-only file system`. `worktrees/task-0eafbbee` was HEAD `8331a9cf1cbb1db564791b3acce5e9af2b298b3a` on
`feat/card-task-0eafbbee`, 30 porcelain entries (23 ` M`, 7 `??`). The three file sha256 values matched the
compare note:

| Path | sha256 |
|---|---|
| `tools/Antiphon.Checkpoints/CheckpointApp.cs` | `558ffd074839f6dce5264da148be0e6fbaad9d85b15b7fa8367946ba72ee5afe` |
| `tools/Antiphon.Checkpoints/Report/ReportMerger.cs` | `e7694c77ada48aa398d48c1a0211d2317f4e263bc8c52a3e209af4b19b367532` |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs` | `05dc5a608d5fda54b5ed60bfa7ddc2a88984057d52a8b7570706fcc594681aef` |

The same three hashes were reproduced on the desktop after the copy. Blob ids in the rescue commit are
`c20ebb58e1b87a8d96fc851ca5413f8d373d08a4`, `9328d6c6df6a8c389613a4f7ce864be7e5117f17`, and
`1194c5735b5833310c9ca1c47979ecbe21a45dff`. The three paths are checkpoint sources; none is a secret-shaped name.

## STEP 1 rescue

Proof bundle of the 30 entries: host `/tmp/c1105-6f1e3bff/out/task-0eafbbee-30.tar`, sha256
`d9dd44da06d186b967526dacf2a38fea75a95e04f7459b4903d633f9a82c20a9`. The three-file tar beside it is sha256
`7e7cfbe3559b6298d321266a361b9a0e2219b3fb21e1612852d9bd7befc3e456`.

Desktop scratch clone, branch `rescue/card-0835-0eafbbee` from `31411ea310024568d45c3130b9678fd4da63d68f`.
`tools/Antiphon.Checkpoints/Report/ReportValidator.cs` exists at that commit. `git show --stat` of
`6f9b58ede2aeb40dda93cecfbd7f5b1ec1a3fc61` is exactly those three files: `CheckpointApp.cs` +5/-0,
`ReportMerger.cs` +1/-1, `ReportValidator.cs` +1/-1 (7 insertions, 2 deletions). Message:
`rescue: 7 unrecorded lines of failed CARD-0835 task 0eafbbee (post-failure edits, not reviewed)`.
`git ls-remote origin refs/heads/rescue/card-0835-0eafbbee` returned the same sha. No other branch was pushed.
No pull request.

## STEP 2 discard

Read-write mount was only `antiphon-runner_work` at `/work` (`rw,relatime`). Before: 30 entries, HEAD
`8331a9cf1cbb1db564791b3acce5e9af2b298b3a`, branch `feat/card-task-0eafbbee`. Commands were `git checkout -- .`
and `git clean -fd` in that worktree. After: 0 entries, same HEAD, same branch. `git clean` removed the seven
untracked paths, including `tools/Antiphon.Checkpoints/Report/ReportValidator.cs`.

## STEP 3 remove `c1005-rebase-rX0auR`

Admin `HEAD` and `gitdir` were copied to the host proof directory before removal. `HEAD` was
`6018e6aae0ee66d67fe79cb62a2691c8bf6fa096`. `gitdir` was `/tmp/c1005-rebase-rX0auR/.git`. The checkout was
present on `antiphon-runner_runner-tmp`. With that volume mounted at `/tmp`, `git worktree list --porcelain`
showed `worktree /tmp/c1005-rebase-rX0auR`, detached, and did not mark it prunable. An earlier list that
mounted runner-tmp at `/runner-tmp` instead of `/tmp` reported prunable, because git looks up the recorded
`/tmp/...` path.

The desktop clone does not contain `6018e6aae`, so patch equivalence was not recomputed here. Removal relied
on the cleanup note's census (18 commits, all patch-equivalent on master). `git rev-list` against the volume's
stale `origin/master` still listed 18 commits.

`git worktree remove --force /tmp/c1005-rebase-rX0auR` exited 0. Worktree paths: 392 before, 391 after. The
only path removed was `/tmp/c1005-rebase-rX0auR`. `/tmp/c1005-master-261500e2` and `/tmp/c983-base-608ed47c`
remained. The admin directory and the checkout directory are gone. No prune.

## STEP 4 ref delete

`git ls-remote` before the delete: `refs/heads/rescue/card-0688-fa0190b4` =
`2b124092c6fd0c60789e5f651e70396e7ce33f33`. Origin still advertises
`refs/heads/feat/card-task-fa0190b4-rebased` at `04c598ac39edf0320070abc181714af4383b208e`. It does not
advertise `feat/card-task-fa0190b4`, `feat/card-task-fa0190b4-first-verified`, or
`feat/card-task-fa0190b4-second-verified`.

Cherry against desktop `origin/master` (`9aba6961d8761726748a2e35aabe68d9cd147265`), `GIT_NO_LAZY_FETCH=1`:

| Tip | `+` | `-` | Notes |
|---|---|---|---|
| `14dbe1110a590679e65eca40512e991e8dfbf37f` | 0 | 21 | only off-master merge `52f5dd05a2909aac94f346d5988a6cd816997a8b` |
| `ce4cce1e8c03f6a769506eaa44a185d289a12b44` | 0 | 18 | same merge |
| `03603d9d88808e798e2495d628f8a621deb33da1` | 0 | 31 | |
| `bac3ddeb988bb43ee26c4117aba2ef545bd8433c` | 3 | 18 | the three `+` commits below |
| `5475ddee667c1150a38186f6685d0e31820a54bb` | 3 | 26 | same three |
| `1dcc03c0a596dbd1fa857ee93293c6dfbf05828a` | 3 | 24 | same three |

The three `+` commits are `f10c7293055c7e74a5e501438bc369c1153045f9`, `9352ebf8775a00c7de09dc177c832b66745c77f4`,
and `1546dc446f0d98da11e4026d2d0f5169757cd59d`, the pre-rebase commits the identification note already classed
SUPERSEDED. `d0630bde` from that note is now `-`. Tips on the volume matched the identification note, so those
refs were deleted.

Undo record, written from a read-only mount before any delete: `/tmp/old-volume-refs-20261009T064828Z.txt`,
2186 lines, sha256 `7c624d001c788d022cc7a046b055686c52782c351301f66a98ce0a233a25c1dd`.

Deleted with `git update-ref --stdin` (`delete <ref> <oldsha>`), 17 refs, rc 0. Refs 2186 before, 2169 after.
Each deleted name was gone. No gc, prune, or repack.

Skipped, tips unchanged after the delete:

| Ref | Tip | Why skipped |
|---|---|---|
| `refs/heads/feat/card-task-fa0190b4-second-verified` | `04c598ac39edf0320070abc181714af4383b208e` | not `2b124092`; this sha is the advertised rebased tip |
| `refs/remotes/origin/feat/card-task-fa0190b4` | `f188a3dce16879cbf9faa4a58c75e2d2b2ffb339` | note does not record this sha; contained in `origin/feat/card-task-273b3a0d` |

`refs/heads/feat/card-task-fa0190b4-rebased` and `refs/remotes/origin/feat/card-task-fa0190b4-rebased` stayed at
`04c598ac39edf0320070abc181714af4383b208e`.

## STEP 5 audit

Same repaired helper the cleanup task ran (`/tmp/c1105-audit-replay.sh`, sha256
`407a5297972abbb5cafaf4de53d169246eef2bf19cf9c3dbafbde15ac767f043`), master `51f175db` program with the two
reporting changes (continue after a dirty worktree, one subshell per common directory). Mounts: `/work` readonly,
2 GiB tmpfs `/tmp`, `antiphon-runner_runner-tmp` readonly at `/runner-tmp` (`C1008_TMP_MOUNT`). Mountinfo showed
`/work ro,relatime`. Write probe failed. Default network. 2026-10-09T06:49:02Z to 2026-10-09T07:14:34Z. Container
exit 0. Stderr was only the read-only probe.

| Result | Count |
|---|---|
| `entry` lines | 390 |
| Content REFUSE (`RecycleWorktreeDirty`, `REPLAY-CONTINUE`, `audit check=`) | 0 |
| Common PASS | 2 (`repos/antiphon/.git`, `repos/markdown-package/.git`) |
| Common REFUSE | 0 |
| `RecycleUnpublishedWork` | 0 |
| `RecycleGitAuditUnknown` | 0 |
| Final line | `repositories=390 partial=390` |

## Worktree directories

`/work/worktrees` has 387 directories: 386 `task-*` and `c1025-base-137ab344`. `git worktree list` is 391 paths
after STEP 3 (392 before), including `/work/repos/antiphon` and the two remaining `/tmp` checkouts.

Of the 386 `task-*` directories, card status from `GET /api/agent-tasks/{8}` and `GET /api/cards/{id}`:

| Card status | Worktrees |
|---|---|
| Done | 221 |
| Canceled | 0 |
| Review | 84 |
| InProgress | 79 |
| no card (`task-0223149e`) | 1 |
| not an 8-char task id (`task-d73d91aa-baseline`) | 1 |

80 distinct cards: 59 Done, 13 Review, 8 InProgress. Task rows for the 385 resolved ids: 318 Succeeded, 42 Failed,
25 Canceled. The cleanup note's 389 was linked checkouts, not this `task-*` count.
