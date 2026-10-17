# Redeploy-old `for-each-ref` status 128

rc-line defect, label rc-2026-10-15. Read-only diagnosis. The script and the volume were not changed. `redeploy-old` was not started.

## Outcome

`RecycleGitAuditUnknown audit check=for-each-ref status=128` is `git for-each-ref` dying while the preflight audit appends to `$scratch/tips` on its private 2GiB tmpfs. The directory in the diagnosis is the worktree admin `consider_tips` was given. That admin is fine. The scratch disk is not.

Retry-once on status 128 is the wrong fix. Copying `$scratch/ref-errors` into the diagnosis is also the wrong fix. Both were tried against a full tmpfs: the command exits 128 and the stderr file stays empty. The repair is to delete each `consider_dirty` payload when `reap_dirty` finishes with it.

## What the receipt is

| Receipt | Diagnosis |
|---|---|
| `C:\src\Antiphon\.antiphon\rolling-server2\c727a1f82f2ababd` | `for-each-ref` status 128, `repo=repos/antiphon/.git/worktrees/master` |
| `C:\src\Antiphon\.antiphon\rolling-server2\c727e5269bca698d` | same check and status, `repo=repos/antiphon/.git/worktrees/card1259-base-d636249e` |

Both are preflight refusals: `phase=preflight`, `outcome=refused`, `stopReceipts` and `removeReceipts` empty. Operation `c10084fa7d62ec9ba4845bf071445f0d516b1` (the master one) ran about 92 minutes and did not hit the 10800s cap (that would be check `audit-timeout`, status 124 or 137). `liveZero` at census was server2, sessions 0, runnerSessions 0, queuedTasks 0, draining, `acceptingNewWork` false. The runner container was still up. Image `sha256:ea8bafe24373694b3421d63afdffb4533faf0710f709d96dea661123b55e6835` (`antiphon-server2/session-testing:7507b3cfe0e2`), still the running container `fc2983ea4991`.

## `$directory` is the path that was printed

`consider_tips` does not set `audit_repo`. Callers do.

- `consider_linked` sets `audit_repo="$admin"` and then calls `consider_tips "$admin"` (`scripts/c590-remote.sh` 6699-6715).
- `consider_common` calls `consider_tips "$top"` only after `audit_repo="$rep"` (6898-6900), and the printed repo would be the checkout (`repos/antiphon`), not `worktrees/master`.

`rel_repo` strips the `/work` prefix. `repos/antiphon/.git/worktrees/master` is `/work/repos/antiphon/.git/worktrees/master`, and that is the `--git-dir`. The worktree loop is `sort -z`, so the name is the admin `find` returned, not a cwd-relative collision with `refs/heads/master`.

`fail` (6198-6202) prints `status` from `$?` of that git process and exits 2. Stderr is `2> "$scratch/ref-errors"` (6587). The comment at 6164-6165 says git stderr is never copied. The EXIT trap deletes `$scratch` (6980-6988). A non-empty `ref-errors` with exit 0 would be status 2 (6588), not 128. Status 128 is git's `die()`.

Preflight `c1008_audit_checked` is at 7656. `docker stop` of the runner is at 7675. The helper is a different container: `/work` and the runner-tmp volume read-only, `/tmp` a tmpfs of `2147483648` bytes (7272).

## The admin is healthy now

Reproduced 2026-10-17 as uid 1654 with the audit environment (`GIT_OPTIONAL_LOCKS=0`, `GIT_NO_LAZY_FETCH=1`, `GIT_TERMINAL_PROMPT=0`, `LC_ALL=C`, `GIT_CONFIG_SYSTEM=/dev/null`, `GIT_CONFIG_GLOBAL=/dev/null`, `GIT_NO_REPLACE_OBJECTS=1`, `core.commitGraph=false`, `gc.writeCommitGraph=false`). Git 2.47.3. Bash 5.2.15. `ulimit -n` 1048576 in both the runner and the helper.

`/work/repos/antiphon/.git/worktrees/master`:

- directory, mode 755, owner 1654:1654, same as the common dir
- `commondir` is `../..`, no `config.worktree`, no `reftable`, no lock, no `gc.pid`
- `gitdir` is `/work/evidence/task-20826ceb/master/.git` and that path exists
- `HEAD` is detached `59ed791fa4c51ce72c7adb8581a10a383575d1ad`
- common config keys include `core.repositoryformatversion=1`, `remote.origin.promisor`, `remote.origin.partialclonefilter=blob:none`. `extensions.partialclone` is unset. The mounted gitconfig has no `safe.directory`. Ownership matches, so that does not matter.
- `packed-refs` is 278791 bytes. No non-directory entries directly under `worktrees` (810 directories).

`git --git-dir=<master> for-each-ref --format='%(objectname)'`: exit 0, empty stderr, 3457 tips, 45ms in the runner (read-write) and 31ms in a helper with the audit's read-only mounts. The same command on the common dir exits 0. All 810 worktree admins exit 0 with empty stderr on that read-only mount.

`card1259-base-d636249e` is not there anymore. `master` is the 16th sorted admin (15 names before it).

Host memory at the check was about 27GB used of 129GB, about 99GB available. Kernel log 08:00-10:20 UTC had no OOM kill. `docker top` showed no git process. Only `antiphon-runner-session-runner-1` had `antiphon-runner_work` mounted, read-write.

Shapes that do return 128 on this git, all created on the helper tmpfs, not on the volume:

| Shape | Status | Stderr |
|---|---|---|
| `--git-dir` path absent | 128 | `fatal: not a git repository` |
| worktree admin directory with no `HEAD` | 128 | `fatal: not a git repository` |
| `packed-refs` whose first line is not a packed-refs header | 128 | `fatal: unexpected line in .git/packed-refs` |
| tmpfs `df` available 0, stdout and stderr on that tmpfs | 128 | stderr file is 0 bytes |

Shapes that do not:

- a ref whose object was deleted, including with promisor config and `GIT_NO_LAZY_FETCH=1` (`%(objectname)` does not load the object): exit 0
- a stale `packed-refs.lock` next to a real `packed-refs`: exit 0
- an empty or truncated loose ref: exit 0, with `warning: ignoring broken ref` on stderr (the audit would then `fail 2`, not 128)
- 2113 `for-each-ref` runs against a synthetic repo while another process looped `update-ref` and `pack-refs`: 0 failures

## Why the scratch is the cause

`consider_dirty` keeps, under `$scratch/dirty-$n`, the `ls-files -v` output, the `ls-files --stage` output, the absolute path list, the expected oids, and the `hash-object` output. `reap_dirty` reads only `dirty-$n.out` and leaves the rest until the EXIT trap. The first pass queues every checkout `find` sees, then `drain_dirty`, and only then `consider_common`. Reaching `for-each-ref` means that first pass finished, so every queued checkout was clean enough to write those files (a dirty checkout refuses earlier, as `RecycleWorktreeDirty`).

Measured on the live admins:

- 810 indexes, 795,639,570 bytes, each between 952,899 and 1,035,849
- `master` index 1,028,649; `ls-files -v -z` 494,527 bytes; `ls-files --stage -z` 878,575 bytes; 8,001 files; relative `-z` paths 478,525 bytes
- checkout prefix `/work/evidence/task-20826ceb/master/` is 36 bytes, so the absolute path file is 470,524 + 8,001×36 + 8,001 = 766,561
- expected and actual oid files are 8,001×41 = 328,041 each
- one clean `master` payload is 2,795,745 bytes

Scaled by average-index / master-index (982,271 / 1,028,649): about 2.67MB × 810 = 2.16GB. The tmpfs is 2,147,483,648 bytes. The first pass is within a few tens of MB of the cap. The band is the checkout-prefix length; a shorter prefix than 36 saves about 16×8,001×810 bytes per 16 bytes.

`consider_linked` calls `consider_tips` and then `queue_dirty` again, with a different `dirty_seen` key (`admin|checkout` rather than the checkout), so the second pass writes another payload and does not reuse the first. `dirty_parallel` only caps how many run at once. It does not delete finished ones. The sorted loop therefore adds another ~2.7MB at each of the first admins while `for-each-ref` appends to `$scratch/tips` on the same tmpfs. `master` is that 16th admin. The earlier receipt naming `card1259-base-d636249e` is the same slot on that day's sort, not a second broken directory. That name sorts earlier than `master` and the directory has since been removed.

A fresh `for-each-ref` does not redo those files, so it exits 0. That is the reproduction in the brief, and it does not exercise the audit.

The 92 minutes is the documented cost of walking this volume (a clean 800-repository pass was measured at 5225s), not a hung `for-each-ref`. One admin's `for-each-ref` is ~30ms.

## What not to change

Retry-once on status 128: with the tmpfs full, a second `for-each-ref` exits 128 again. It heals a torn `packed-refs` or a directory that vanished mid-walk. This volume did not show that race (0/2113), and a retry cannot free scratch the first pass still holds.

Copying `ref-errors` into the diagnosis: on the full tmpfs the file was created and stayed 0 bytes, because the fatal line is written to the same filesystem. The credential rule at 6164-6165 also forbids pasting git stderr. This repo is a promisor; a fetch fatal can contain the remote URL. An empty file makes the paste useless anyway.

Raising `tmpfs-size` and still retaining every payload: the first pass is already the size of the cap, and the second pass adds more. The next handful of worktrees dies in the same place.

## Fix

1. In `reap_dirty` (`scripts/c590-remote.sh` 6351-6362), after `dirty-$n.out` has been read, delete that generation: `dirty-$n.out`, `.flags`, `.index`, `.paths`, `.expected`, `.actual`, `.stash`. Do not delete the `.out` before it is read. Live scratch is then about `dirty_parallel` payloads (8 × ~2.7MB) plus one `tips` file (~810 × 3,457 oids, ~114MB), inside the existing 2GiB tmpfs.
2. When `for-each-ref` returns non-zero and `ref-errors` is empty, append `scratch-avail=<bytes>` from `df -B1 /tmp` to the diagnosis line `fail` already writes to container stdout (docker captures that fd; it is not the tmpfs). Do this in `consider_tips` (6584-6590) / `fail` (6198-6202). Do not copy `ref-errors`.

No other check should change. A stable "not a git repository" or a bad `packed-refs` still refuses with status 128, and those leave a non-empty `ref-errors` when the disk has room.

## Red-first witness

One new test, not an addition to the `C1008PlatformContractTests` remote filter:

`/*/*/RemoteScriptContractTests/C1008_Recycle_reaps_dirty_scratch`

`RequireNativeLinux`, same as `C1008_Recycle_refuses_uninspectable_git`. Add the Windows skip-roster row the sibling tests have, or the Windows host will try to run it.

Plumb an optional `C1008_SCRATCH_QUOTA_BYTES` the way `C1008_AUDIT_TIMEOUT_SECONDS` is passed into the helper (`c1008_audit` at 7292, and the audit body). Unset in production, and unset means no check. When set, `consider_tips` runs `du -sb "$scratch"` and `fail`s with `check=scratch-quota` if the total is over the quota. The fake docker must forward that env.

The test builds one published clean repository whose `consider_dirty` payload is larger than the quota (one commit of enough files that `ls-files --stage` exceeds it; the control graph in `C1008_Recycle_refuses_unpublished_and_dirty_work` is the pattern) and sets the quota just below that payload. Before the `reap_dirty` deletion, the first pass keeps the payload, `consider_tips` trips `scratch-quota`, and the test fails. After the deletion, `du` stays under the quota and the recycle reaches removal. That is the red-then-green pair. Do not assert on a pasted git fatal line.

## Not done

The 92-minute audit was not re-run. The historical scratch directory was deleted by the EXIT trap, so this pass did not see its `df`. The size above is from the live indexes and from `master`'s `ls-files` output, scaled across 810 admins.
