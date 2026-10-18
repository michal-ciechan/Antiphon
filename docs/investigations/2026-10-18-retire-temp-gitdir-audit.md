# retire-temp gitdir-entry on the temp work volume (2026-10-18)

Read-only diagnosis. No rollout phase was run. Nothing on server2 was edited or deleted. The standing `antiphon-runner` containers were not exec'd and were not restarted.

**Decision needed:** approve an archive-then-delete of the 77 `antiphon-mirror-owner.json` stamps, then a fresh `retire-temp`. That is safe for git publication. Leaving the four temp volumes in place is also safe. Do not `git worktree prune` these admins.

## Outcome

`RecycleGitAuditUnknown audit check=gitdir-entry status=0 repo=repos/antiphon` is a closed-list miss, not a git failure. Status 0 is the literal `refuse_unknown` passes. The common directory `/work/repos/antiphon/.git` on `antiphon-runner-temp_work` is clean. All 77 linked worktree admins contain a regular file the list does not name: `antiphon-mirror-owner.json`. The receipt names the checkout because `consider_gitdir` does not change `audit_repo` when it walks `worktrees/<id>`.

`find` order, the same unsorted order the audit uses, hits `task-d3c62283` first. Inside that admin the byte sort checks `COMMIT_EDITMSG`, `FETCH_HEAD`, `HEAD`, and `ORIG_HEAD` (all allowed) and then refuses:

`/work/repos/antiphon/.git/worktrees/task-d3c62283/antiphon-mirror-owner.json`

Every other admin has the same file, so removing only the first one would refuse on the next admin. The file is mode 644, 164 bytes, owner 1654, and readable. It is not a missing `gitdir`, not a path outside the mount, and not an unreadable entry. Each `gitdir` is `/work/worktrees/task-<8hex>/.git`, and all 77 checkouts exist.

The writer is `RunnerWorkspaceBindingStore.WriteOwnerStamp` (`src/Antiphon.SessionRunner/RunnerWorkspaceBindingStore.cs`). The sample for `task-d3c62283` is `taskId` `d3c62283-34af-4c7a-b79a-16e068fde52e`, `creationGeneration` `695a5dab-0e8d-4efb-89ae-d4deb96d8045`, `preparationId` `148c04fe-58ac-4f89-b082-652dd77c4eca`. No `antiphon-mirror-owner.json.tmp` was present.

This is not the 2026-10-17 standing-volume case. That one was `for-each-ref` status 128, later traced to a full audit tmpfs, and the stale-admin cleanup archived admins whose `gitdir` target was already gone and then ran `git worktree prune`. Here the checkouts are present, so prune would not select them.

## What else the closed list saw

Top-level names under the 77 admins, and how `consider_gitdir` treats them:

| Name | Count | Verdict after the stamps are gone |
|---|---|---|
| `HEAD`, `ORIG_HEAD`, `commondir`, `gitdir`, `index`, `logs`, `refs` | 77 | allowed |
| `FETCH_HEAD` | 66 | allowed pseudoref |
| `COMMIT_EDITMSG` | 44 | allowed |
| `antiphon-mirror-owner.json` | 77 | `gitdir-entry` (the refusal) |
| `AUTO_MERGE` | 2 | allowed when it is a tree and no merge, rebase, sequencer, or bisect is in progress |

The two `AUTO_MERGE` files are on `task-89bf7b58` and `task-bd6bce3d`. Each is 41 bytes, a 40-hex id, `cat-file -t` says `tree`, and neither admin has `MERGE_HEAD`, `CHERRY_PICK_HEAD`, `REVERT_HEAD`, `rebase-merge`, `rebase-apply`, `sequencer`, or `BISECT_*`. `consider_auto_merge` accepts that shape.

The common directory's top level is only `HEAD`, `FETCH_HEAD`, `branches`, `config`, `description`, `hooks`, `info`, `logs`, `objects`, `packed-refs`, `refs`, and `worktrees`. `info/` holds `exclude` and `refs`. There is no `antiphon/` directory, no `reftable`, no lock, and no editor swap or backup. `core.worktree` is unset. No other `.git` directory without `commondir` exists under `/work` (78 `.git` entries: this directory plus 77 worktree pointer files).

## Publication

Checked as uid 1654, Git 2.47.3, audit environment (`GIT_OPTIONAL_LOCKS=0`, `GIT_NO_LAZY_FETCH=1`, `GIT_CONFIG_GLOBAL=/dev/null`, `GIT_CONFIG_SYSTEM=/dev/null`, `GIT_NO_REPLACE_OBJECTS=1`, `core.commitGraph=false`). The work volume and `antiphon-runner-temp_runner-tmp` were mounted read-only. Origin heads were fetched into the helper's tmpfs (blobless, alternates pointing at the volume's object store). The volume was not written. Proof is `fetched`, 1888 origin heads.

| Check | Result |
|---|---|
| Worktrees | 77 present, 0 absent |
| `diff-index` / staged / untracked-not-ignored / stash | 0 / 0 / 0 / 0 |
| Local branches | 79 |
| Distinct tips (branch tips, worktree HEADs, stashes) | 51 |
| `rev-list --count` of those tips against the fetched heads | 0 |
| Reflog commits not in local `refs/remotes/origin` | 0 |
| Common `refs/stash` | none |
| Main checkout `/work/repos/antiphon` | no index, no file outside `.git`, porcelain is 8066 `D ` lines, no stash |

The main checkout is the documented no-index seed. The live audit reached `gitdir-entry` only after `drain_dirty`, so that pass had already accepted every queued checkout's content. This read agrees.

`/work/workspace-bindings` has 77 files, every one `phase=complete`, one per task id. The audit does not read that directory. Removing the volume deletes those records. They are not commits.

`antiphon-runner-temp_runner-tmp` has one git directory the antiphon worktrees do not register: `/runner-tmp/c1008-measure-6Pp568/src/.git`, HEAD `ec8ffdab30fa7cef064390520af2bbde31dfc0f4`, subject `A`, dirty 0, stash none, 0 commits ahead of its own origin. No `gitdir` under `repos/antiphon` points at `/tmp`. The audit does not classify the rest of runner-tmp. That rest is session scratch (temp dirs, pipes, notes, patches such as `s3-new.patch`). Volume removal deletes it. It is not an unpublished branch, uncommitted checkout, or stash.

196 symlinks on the work volume sit inside four task checkouts (`task-da0e4f4d`, `task-7a5b409f`, `task-4a7999f1`, `task-20ca1e8b`, 49 each). Those checkouts were clean, so the links are ignored or match the index. The audit does not follow links inside a checkout.

No task id has an unpublished tip.

## Receipt and host

Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c7274aeaa51dc996`. Journal `/home/mc/antiphon-server2/recycle/c1008a24aceabd9ff4ec9a174752ae3ca8475.json` is `phase=preflight`, `outcome=refused`, `ownedRemoved=false`, project `antiphon-runner-temp`, all four temp volume outcomes `pending`. Temp `retiredAt` is `2026-10-18T11:45:09.0716214Z`, `retireReason` idle. Image `sha256:a9be72ebb98bddae896fd93649897cdc7d8c59449a36629e4dd2100fd956b50c` (`antiphon-server2/session-testing:09e48c5d6f1d`).

`docker ps -a --filter name=antiphon-runner` showed no temp containers. Standing `antiphon-runner-session-runner-1` was `Up 12 hours (healthy)` on `antiphon-server2/session-testing:09e48c5d6f1d`. `antiphon-runner-state-init-1` is exited 0. `antiphon-runner-build-slots-1` was left as it was.

`du -sx` on the volume mountpoints (GNU 1 KiB units):

| Volume | KiB |
|---|---|
| `antiphon-runner-temp_work` | 22037520 (~21.0 GiB) |
| `antiphon-runner-temp_runner-tmp` | 297976 (~291 MiB) |
| `antiphon-runner-temp_dind-data` | 304284 (~297 MiB) |
| `antiphon-runner-temp_runner-state` | 269208 (~263 MiB) |

Together about 21.9 GiB. `df -Pk /` and the docker data root are the same LV: 193013896 KiB free (~184 GiB), capacity 73%. `docker system df` reported local volumes 328.9 GB with 21.35 GB reclaimable. The 20 GiB cache gate is already met. Keeping the four volumes is not a disk emergency.

`Assert-NoIncompleteRecycle` applies to project `antiphon-runner` during `redeploy-old`. This refused journal is `antiphon-runner-temp`, so a new `retire-temp` does not need `-ResumeRecycle`.

## Ways through

### (a) Archive the stamps, then retire-temp

This is the analogue of the earlier archive-then-remove, applied to the offending files rather than to live admins. Do not `git worktree prune`. Do not delete admin directories, checkouts, or `workspace-bindings`.

Approval 1, before any write: archive and then delete only the 77 stamp files on `antiphon-runner-temp_work`.

```bash
ssh mc@server2 'mkdir -p /home/mc/antiphon-server2/rescue-20261018 && docker run --rm --user 1654:1654 -v antiphon-runner-temp_work:/work:ro --entrypoint bash antiphon-server2/session-testing:09e48c5d6f1d -lc "cd /work/repos/antiphon/.git/worktrees && find . -mindepth 2 -maxdepth 2 -name antiphon-mirror-owner.json -print0 | tar --null -T - -cf -" > /home/mc/antiphon-server2/rescue-20261018/antiphon-mirror-owner-stamps.tar && tar -tf /home/mc/antiphon-server2/rescue-20261018/antiphon-mirror-owner-stamps.tar | wc -l && sha256sum /home/mc/antiphon-server2/rescue-20261018/antiphon-mirror-owner-stamps.tar'
```

Require 77 paths and keep the sha256. Then, still only those files:

```bash
ssh mc@server2 'docker run --rm --user 1654:1654 -v antiphon-runner-temp_work:/work --entrypoint bash antiphon-server2/session-testing:09e48c5d6f1d -lc "find /work/repos/antiphon/.git/worktrees -mindepth 2 -maxdepth 2 -name antiphon-mirror-owner.json -print -delete"'
```

Confirm a read-only `find` of that name prints nothing. Copy the tar off the host before the volume removal if the host copy should survive.

Approval 2, after that confirmation: from `C:\src\Antiphon` (not a worktree),

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase retire-temp
```

Do not pass `-ResumeRecycle`. Do not target the standing project. The phase re-audits and, if the audit passes, removes the four temp volumes. That removal is destructive. It is acceptable on the publication result above. `-DryRun` only previews targets. It does not prove the audit.

The same image writes this stamp into new linked admins. The next standing `redeploy-old` can refuse the same way once a standing admin has the file. Allowing `antiphon-mirror-owner.json` in `consider_gitdir` is a separate Code change. It is not required to finish this temp retirement after the files are removed.

### (b) Documented skip

None. The closed list is the contract. No retire-temp flag ignores `gitdir-entry`.

### (c) Leave the volumes

Do nothing. Cost is about 21.9 GiB on a filesystem with about 184 GiB free. The journal stays `preflight` / `refused`. Standing server2 stays on `09e48c5d` and is not involved.

## Recommendation

Do (a). Approval 1 is the stamp archive and delete. Approval 2 is `retire-temp`, which deletes the four volumes. There is no unpublished branch tip, uncommitted checkout, or stash to recover first. Do not prune the admins. Do not touch the standing runner.

--- next stage ---
next: decide
handoff: Approve the stamp archive-and-delete, then a fresh retire-temp from C:\src\Antiphon, or leave the four temp volumes. No unpublished git tips. Do not worktree-prune and do not touch standing server2.
artifact: docs/investigations/2026-10-18-retire-temp-gitdir-audit.md
