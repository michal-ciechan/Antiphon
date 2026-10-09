# CARD-1105: cleanup of leftover refs and reflogs on the server2 OLD runner volume (2026-10-09)

Task 7610ef93 (Debug). The operator authorized this cleanup: "Rescue the one commit and lose the ref log", and
for the 289 section 3b refs, "can't we just delete them? They should have been cleaned up after landing".
Volume `antiphon-runner_work` (project `antiphon-runner`, the OLD runner; it was draining with 0 sessions).
Source: `docs/investigations/2026-10-08-card-1105-old-volume-audit-replay.md` section 3, on branch
`feat/card-task-96b558c0` at `dec5614fc`.

## Outcome

**Not clean. STEPS 1-3 succeeded, and the audit now refuses only for items this task kept on purpose.** 620 refs were deleted (2806 -> 2186) and every reflog was expired (8315 entries -> 0). The STEP 4 replay of the `51f175db` audit shows 0 RecycleGitAuditUnknown and 2 remaining refusals: RecycleWorktreeDirty `worktrees/task-0eafbbee` (its discard was not run), and RecycleUnpublishedWork `repos/antiphon` with 29 commits and 6 tips, all of them KEEP refs or a worktree HEAD outside this task's scope. Next: decide (see the end of STEP 4).

## Preconditions (all held)

- (a) Rescue: `docs/investigations/2026-10-09-card-1105-fa0190b4-rescue.md` is on `origin/master`
  (`e83780718`). `git ls-remote origin refs/heads/rescue/card-0688-fa0190b4` returned
  `2b124092c6fd0c60789e5f651e70396e7ce33f33`, which is the commit both volume `fa0190b4` refs point at.
- (b) The compare task for `worktrees/task-0eafbbee` reported on `feat/card-task-769d81ba`
  (`docs/investigations/2026-10-09-card-1105-task-0eafbbee-compare.md`, `97c14deb9`). Its STEP 2 failed
  (26 of 30 files differ from master; 3 hold 7 changed lines that no commit records), so **the discard did not run**.
  That worktree was not touched here.
- (c) `GET /api/agent-tasks/pipeline` at 04:53Z showed the Deploy and Merge stages with 0 in flight. Nothing in flight
  was landing (only Code/Review/Debug tasks were working). On server2 no `deploy-server2`/`c590`/`c1008`
  process was running. No git process was running in the old runner container, and the runner `server2` reported
  `draining=true occupied=0`.

## Method

- Every step ran in a throwaway `docker run --rm` container on image
  `antiphon-server2/session-testing:4358939ecd85` (`sha256:743279186cff…`, the old runner's own image, git 2.47.3),
  `--user 1654:1654`, with git env `GIT_OPTIONAL_LOCKS=0 GIT_NO_LAZY_FETCH=1 GIT_CONFIG_SYSTEM=/dev/null
  GIT_CONFIG_GLOBAL=/dev/null` and `core.commitGraph=false`.
- STEP 1 (census) and the undo record: `/work` mounted `readonly`. Container mountinfo showed `/work ro,relatime`,
  and a write probe failed. `--network none`.
- STEP 2 and STEP 3: one container with `/work` read-write (mountinfo `/work rw,relatime`), `runner-tmp` read-only
  at `/runner-tmp` (used only to check that the three `/tmp` checkouts exist), and the delete list and the script as
  read-only bind mounts. `--network none`, and `gc.auto=0 maintenance.auto=false` were set. The script ran only
  `git update-ref --stdin` (delete lines carrying the old SHA, one transaction), `git reflog expire`, and read-only
  `for-each-ref`/`show-ref`/`find`/`wc`. No gc, prune, repack, worktree remove, fetch, checkout, reset, clean or stash
  ran, and no container, volume or deploy phase was touched.
- STEP 4: the audit program of master `51f175db` (`c1008_git_program` in `scripts/c590-remote.sh`), run with the same
  mounts as `c1008_audit` (`/work` readonly, private 2 GiB tmpfs `/tmp`, `runner-tmp` readonly with
  `C1008_TMP_MOUNT`, default network for the origin proof fetch, image by ID). Mountinfo inside the running
  container showed `/work ro,relatime`, and a write probe failed. Only reporting changed, in two places:
  `reap_dirty` records a refused worktree and continues instead of exiting, and each common directory's
  `consider_common` runs in its own subshell with `set -e` and the ERR trap still in force, so its first refusal is
  recorded and the next common directory is still audited. A refusal inside one common directory's pass still
  ends that pass, as in the real helper.
- Desktop side: `git fetch origin`. Current `origin/master` = `e837807183d163f26468757e3dada8949aaca2f6`.
  `git ls-remote origin 'refs/heads/*'` advertised 1579 heads. The mirror's own `origin/master` is the stale
  `af4d4c386e` (2026-10-05), which is an ancestor of current `origin/master`.

## STEP 1: census (read-only)

`repos/antiphon` held 2806 refs: 1261 under `refs/heads/` (1247 `feat/`, 6 `land/`, 5 `review*`, `land-card-0706`,
`tmp`, `master`) and 1545 under `refs/remotes/origin/`. The candidates were 2803 refs, everything except
`refs/heads/master`, `origin/master` and `origin/HEAD`. `git worktree list --porcelain` and the admin directories
showed 391 linked worktrees: 388 with a checkout under `/work` and 3 under the runner's `/tmp` (`c1005-master-261500e2`,
`c1005-rebase-rX0auR`, `c983-base-608ed47c`, all present in `runner-tmp`). No admin directory had a missing
checkout. Checked-out branches were read from both sources.

Rule per ref. DELETABLE only if all three hold:
(i) origin does not advertise the branch name (desktop `ls-remote`);
(ii) every commit reachable from the ref and not from current `origin/master` (the volume's `rev-list <ref>
^af4d4c386e` minus the desktop's `rev-list origin/master`) is either empty (tree equals its parent's tree) or has
a `git patch-id --stable` equal to a non-merge commit on current `origin/master`. Patch-ids were computed on the
desktop, which held every one of these commits. A merge commit, a root commit or a patch not on master fails (ii);
(iii) the branch is not checked out in any worktree. The `fa0190b4` refs were eligible because the rescue is
verified, but (ii) still applied to them.

| Decision | `refs/heads/*` | `refs/remotes/origin/*` | Total |
|---|---|---|---|
| DELETABLE | 296 | 324 | **620** |
| KEEP | 964 | 1219 | **2183** |

KEEP reasons: 1230 advertised on origin; 491 advertised and also holding unproven commits; 322 advertised and checked
out; 63 advertised, checked out and unproven; **75 not advertised but unproven** (71 hold a patch not on master or a
merge, and 4 are the `fa0190b4` refs, see below).

### Why 620 and not 289 (the >10% gate)

The replay's 289 counted only refs that the audit's graph rule calls unpublished. Rule (ii) is about content on
master, so it also admits refs that the graph rule already accepted:

| DELETABLE ref's tip | heads | origin | Total |
|---|---|---|---|
| Unpublished by graph (the replay's 3b class) | 121 | 154 | **275** (replay: 127 + 162 = 289, within 5%) |
| Reachable from current `origin/master` (0 commits off master; fast-forward lands, or master advanced past the mirror's stale copy) | 125 | 117 | 242 |
| Contained in another advertised origin head, every off-master commit patch-equivalent on master | 50 | 53 | 103 |

The 14 replay refs not in the 275 were not mapped one by one. They match KEEP refs here:
`heads/feat/card-task-3895bec1`, whose name origin advertises, and the 13 unpublished refs listed below, whose off-master
commits include a merge or a patch that `patch-id` does not find on master.
The other 345 refs hold nothing that master lacks, by graph or by patch, so deleting them loses nothing. The
deletion went ahead on that explanation.

The caller's refinement arrived after STEP 2 had already run. It asked for the same reconciliation by category,
and the deleted set satisfies it as it stands:

| Category | Count | Inside the 620 | (i)-(iii) per ref |
|---|---|---|---|
| A: tip unpublished by graph (the audit's class; replay 289) | 275 | all 275 | yes |
| B: tip reachable from current `origin/master` (242) or from another advertised head (103) | 345 | all 345 | yes |
| C: anything else | 0 | n/a | n/a |

Every one of the 620 was checked against (i), (ii) and (iii) individually by the census script. The replay counted
14 more refs in its 289 than A holds. None of them was deleted: they are KEEP for the reasons above, which
is the refinement's "KEEP the unexplained". No `fa0190b4` ref was deleted.

### KEEP refs that origin no longer advertises and whose tip no origin head contains (13)

These hold work that this rule cannot prove is on master, so they stay. They will keep the audit refusing
`RecycleUnpublishedWork` until a human decides:

| Ref | Off-master commits | Unproven |
|---|---|---|
| `heads/feat/card-task-193a4a79`, `origin/feat/card-task-193a4a79` | 22 | 1 merge |
| `heads/feat/card-task-1d94da9e`, `origin/feat/card-task-1d94da9e` | 22 | 1 merge |
| `heads/feat/card-task-97ea55ef`, `origin/feat/card-task-97ea55ef` | 19 | 1 merge |
| `heads/feat/card-task-d24e1b4d`, `origin/feat/card-task-d24e1b4d` | 19 | 1 merge |
| `heads/feat/card-task-d9b74136`, `origin/feat/card-task-d9b74136` | 21 | 4 patches not on master |
| `origin/feat/card-task-5638a6b5` | 21 | 4 patches not on master |
| `origin/feat/card-task-2db75657`, `origin/feat/card-task-ba64b974` | 29 | 4 patches not on master |

In each of these, every other off-master commit is patch-equivalent on master. Commits were not diffed by hand,
so a land that resolved conflicts may have changed a patch-id.

All six `fa0190b4` refs are KEEP, because `70076885` is not on master by patch. Four of them are names origin no
longer advertises: `heads/feat/card-task-fa0190b4`, `heads/…-fa0190b4-first-verified`, `heads/…-fa0190b4-second-verified`
and `origin/feat/card-task-fa0190b4`. Their tips are contained in an advertised origin head (such as
`rescue/card-0688-fa0190b4`), so they are published and do not make the audit refuse. The other two,
`heads/…-fa0190b4-rebased` and `origin/…-fa0190b4-rebased`, are advertised. Deleting the four is safe now
that the rescue is on origin, but rule (ii) does not allow it. The operator can approve that separately.

## STEP 2: ref deletion

- Undo record, saved before deleting: `/tmp/old-volume-refs-20261009T050214Z.txt` on the server2 host, written by
  `git for-each-ref --format='%(refname) %(objectname)'` from a read-only container. 2806 lines, sha256
  `e75030b33c3d82864f0c4c10d5dd3f0abffbee2744997acfe83300811e927a2d`. Restore one ref with
  `git update-ref <ref> <sha>` while the objects remain (no gc ran).
- Delete list: `/tmp/c1105-delete.txt` on the server2 host, 620 lines of `delete <ref> <old-sha>`, sha256
  `6d7b84077cbbc2306a7772ac52023fd0a9d6890ea6d5ba5b6307e09bb80b3e3f`. All 620 ref/SHA pairs matched the undo record.
- `git update-ref --stdin` rc 0. Refs **2806 before, 2186 after**. 0 of the 620 remain.

## STEP 3: reflogs

- `git -C repos/antiphon reflog expire --expire=now --expire-unreachable=now --all` (rc 0). That covers every
  worktree. Then a `--single-worktree` pass per linked worktree: 388 via `git -C <checkout>` and the 3 `/tmp` ones via
  their admin directory, because their checkouts live in `runner-tmp`. 391 passes, rc 0, 0 missing checkouts listed.
- `repos/markdown-package`: the same command (rc 0).
- Reflog entries: common directory **6601 before, 0 after** (the census read 8006 before STEP 2, and the 1405
  difference went with the deleted refs' own logs); worktree `HEAD` logs **1711 before, 0 after** (in 391 files);
  `markdown-package` **3 before, 0 after**. 0 non-empty log files remain. Objects were not touched, and the only lock
  file is the product's `antiphon/landing.lock`, which was there before.

## STEP 4: audit replay (read-only)

The run took 05:07:29Z to 05:40:56Z (33.5 min at load 8 to 39). The container exited 0, stderr was empty, and the final line was
`repositories=390 partial=390`.

| Result | Count | Receipt / code | Repo |
|---|---|---|---|
| Content audit (`consider_dirty`) PASS | 389 of 390 entries | | |
| Content audit REFUSE | 1 | `audit check=status status=0` / **RecycleWorktreeDirty** | `worktrees/task-0eafbbee` |
| Common pass REFUSE | 1 | `audit check=rev-list status=0 proof=fetched commits=29 tips=6 gone-tracking=8 unadvertised-local=5` / **RecycleUnpublishedWork** | `repos/antiphon` |
| Common pass PASS | 1 | (12 commit tips) | `repos/markdown-package` |
| RecycleGitAuditUnknown | **0** | no `worktree-confine` (the `runner-tmp` mount resolves the 3 `/tmp` checkouts), no layout, lock, shallow, dot-git, symlink or timeout refusal | |

`rev-list` is the last check of `consider_common`, so every earlier check of `repos/antiphon` passed: entry layout,
linked admin directories including the 3 `/tmp` ones, reflogs, pseudorefs and the object traversal. Before this
cleanup the replay refused there with 728 commits and 428 tips. Now it refuses with **29 commits and 6 tips**. Those 6 tips
are all accounted for by refs this task KEPT on purpose:

- 4 distinct tips behind the **13 KEEP refs** listed in STEP 1 (`gone-tracking=8` and `unadvertised-local=5` match
  the 8 `origin/…` and 5 `heads/…` refs exactly): `193a4a79`/`1d94da9e` (tip `14dbe1110`, holds a merge),
  `97ea55ef`/`d24e1b4d` (tip `ce4cce1e8`, holds a merge), `d9b74136`/`5638a6b5` (tip `bac3ddeb9`, 4 patches not on
  master), `2db75657`/`ba64b974` (tip `5475ddee6`, 4 patches not on master).
- `heads/feat/card-task-3895bec1` (tip `1dcc03c0a`). Origin still advertises this name, so rule (i) kept it, but the
  local branch has diverged from origin's branch. The replay's 3b put it at 1 commit whose patch is on master.
- The detached `HEAD` of linked worktree `c1005-rebase-rX0auR` (`/tmp/c1005-rebase-rX0auR` in `runner-tmp`, HEAD
  `6018e6aae`). A worktree HEAD is not a ref under `refs/heads` or `refs/remotes`, so it was outside this task's scope.
  The replay found its 18 commits not on origin to be all patch-equivalent on master.

No reflog-only tip remains. The 3c reflog states are gone.

### Decisions left for the operator

1. `worktrees/task-0eafbbee`: rescue the 7 lines that no commit records (see the compare note), or approve the loss and discard.
2. The 13 KEEP refs (4 tips: two with a merge, two with 4 patches that `patch-id` does not find on master): delete them (content review or accept the loss), or rescue-push them.
3. `heads/feat/card-task-3895bec1`: delete the diverged local branch (its origin branch stays).
4. Linked worktree `c1005-rebase-rX0auR` (a CARD-1005 verification checkout in `runner-tmp`): `git worktree remove` it, or accept its detached HEAD (18 commits, all patch-equivalent on master per the replay).
5. Optionally, the four unadvertised `fa0190b4` refs, which are safe since the rescue but kept by rule (ii). They do not cause a refusal.


## Census table (2803 refs)

`heads/` = `refs/heads/`, `origin/` = `refs/remotes/origin/`. "Off master" = reachable from the ref and not from
current `origin/master`. Order: DELETABLE, then KEEP refs that origin does not advertise, then KEEP refs it advertises.

| Ref | Decision | Reason |
|---|---|---|
| `heads/feat/card-task-002c8489` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=23 (of 23 off master) |
| `heads/feat/card-task-00f54e22` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-00f66c8f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-0112b9b0` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-014fddf9` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-02983cde` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=9 (of 9 off master) |
| `heads/feat/card-task-031276a1` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-0353ecae` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-03aa0437` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-0506a1cd` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=1 (of 2 off master) |
| `heads/feat/card-task-0520c00d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-06788c9e` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-06805739` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=8 (of 8 off master) |
| `heads/feat/card-task-0685ad6c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-07df9f1d` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-08084c5f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-08e6f2d2` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=18 (of 18 off master) |
| `heads/feat/card-task-097ed5ae` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-0aa9717c` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=15 (of 15 off master) |
| `heads/feat/card-task-0c270f73` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-0cc1623f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-0d279883` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=7 (of 7 off master) |
| `heads/feat/card-task-10b8e177` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-12cd8c8b` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=4 (of 5 off master) |
| `heads/feat/card-task-1336064f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-137c1631` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=2 (of 3 off master) |
| `heads/feat/card-task-142c2756` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-15a86ac9` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=3 (of 4 off master) |
| `heads/feat/card-task-16178635` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-169fe4dd` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=2 (of 3 off master) |
| `heads/feat/card-task-16d7e477` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-179f4c3c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-18f52a40` | DELETABLE | origin does not advertise; tip in another advertised head; empty=2,patch-on-master=6 (of 8 off master) |
| `heads/feat/card-task-18fd01a3` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-19c7ff68` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=3 (of 3 off master) |
| `heads/feat/card-task-1aa86155` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-1c52bd82` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-1ceae9ac` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-1d0bcab2` | DELETABLE | origin does not advertise; tip in another advertised head; empty=3,patch-on-master=9 (of 12 off master) |
| `heads/feat/card-task-1d5b779b` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-1f9820dd` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=1 (of 2 off master) |
| `heads/feat/card-task-1fe6de8e` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=11 (of 11 off master) |
| `heads/feat/card-task-20a0e763` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-213e34d6` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-214023c6` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-22b951cd` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-22c54e3f` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=4 (of 4 off master) |
| `heads/feat/card-task-24354988` | DELETABLE | origin does not advertise; tip in another advertised head; empty=2,patch-on-master=6 (of 8 off master) |
| `heads/feat/card-task-244b654c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-24bc78b2` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-2700ba1e` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-275c5757` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=5 (of 6 off master) |
| `heads/feat/card-task-27d798b2` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-2837720d` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=8 (of 8 off master) |
| `heads/feat/card-task-290483e4` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-29c28d0c` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-2be23858` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=4 (of 4 off master) |
| `heads/feat/card-task-2c621fdc` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-2cd14d4c` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=3 (of 4 off master) |
| `heads/feat/card-task-2dd9fbf3` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-2e841047` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-2e8b3a67` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=5 (of 7 off master) |
| `heads/feat/card-task-2f0b267a` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-2fc0cff8` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=7 (of 7 off master) |
| `heads/feat/card-task-30103883` | DELETABLE | origin does not advertise; tip in another advertised head; empty=2,patch-on-master=5 (of 7 off master) |
| `heads/feat/card-task-303a8c1f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-320b47d9` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=6 (of 6 off master) |
| `heads/feat/card-task-33d2d16a` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-354b15be` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=1 (of 2 off master) |
| `heads/feat/card-task-364eea2f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `heads/feat/card-task-365d4862` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-37a70801` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=4 (of 4 off master) |
| `heads/feat/card-task-37c9d0e5` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `heads/feat/card-task-3816b58e` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=1 (of 2 off master) |
| `heads/feat/card-task-384a9033` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-3895b67b` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=6 (of 6 off master) |
| `heads/feat/card-task-39d0e7e2` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=54 (of 54 off master) |
| `heads/feat/card-task-3c919969` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-3ce33fdc` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-3e89af3c` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-417f39b7` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-4219092b` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-42bbf9fc` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=2 (of 3 off master) |
| `heads/feat/card-task-43198086` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-43ac843d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-443b21c2` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=3,patch-on-master=6 (of 9 off master) |
| `heads/feat/card-task-44bf1e4a` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-4779fa4c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-47be9689` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-47f087f7` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-49cc02b8` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-5057e9b9` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-5068986a` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-506cc44f` | DELETABLE | origin does not advertise; tip in another advertised head; empty=2,patch-on-master=5 (of 7 off master) |
| `heads/feat/card-task-507b17cf` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-50a2b232` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-520a5e94` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=2 (of 3 off master) |
| `heads/feat/card-task-5294bdc8` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-53529ab7` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-567aeea4` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=3 (of 4 off master) |
| `heads/feat/card-task-57758b77` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-57e2bc5d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-57fa2e6a` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=3 (of 4 off master) |
| `heads/feat/card-task-58f6d83c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-5b2256d5` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-5b76bd03` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-5c22c88c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-5dc73c10` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=8 (of 10 off master) |
| `heads/feat/card-task-5e69d93d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-5ef74c0f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `heads/feat/card-task-604d313f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-609f77b4` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=14 (of 14 off master) |
| `heads/feat/card-task-611187dd` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=8 (of 10 off master) |
| `heads/feat/card-task-6123de4a` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-6195a018` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=12 (of 12 off master) |
| `heads/feat/card-task-624f9824` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=8 (of 8 off master) |
| `heads/feat/card-task-65d09da0` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-663a02ac` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-66d7aac9` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=4 (of 4 off master) |
| `heads/feat/card-task-67e84356` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-68a64314` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-691831a7` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-69c7a060` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=48 (of 48 off master) |
| `heads/feat/card-task-6ad25498` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-6c2cf966` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-6ca40cd0` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=11 (of 11 off master) |
| `heads/feat/card-task-6d623e3b` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-6eed34e6` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=3,patch-on-master=9 (of 12 off master) |
| `heads/feat/card-task-70302dc6` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=5 (of 6 off master) |
| `heads/feat/card-task-7268d5be` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-743f9bdd` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-74da5d1b` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-75472579` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-7558d5cb` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-77629b10` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-7775c8ed` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-778fa5d5` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `heads/feat/card-task-78c5f868` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-79140c38` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-793dfb87` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-795c10ba` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-7a978aa8` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-7b16669f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-7c33fc2f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-7f644884` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-81ca855b` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-81cb89f5` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-82140e9d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-837ff8d2` | DELETABLE | origin does not advertise; tip in another advertised head; empty=2,patch-on-master=5 (of 7 off master) |
| `heads/feat/card-task-8532af35` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-867de79e` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-86d683e9` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-86e57e3d` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-87fe30e6` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=4 (of 6 off master) |
| `heads/feat/card-task-8a3a78a0` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-8afa8f97` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-8c094bd8` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-8d5a188d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-8ea13a79` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-9063a47c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-914a96fd` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=3 (of 4 off master) |
| `heads/feat/card-task-9216c4a9` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-92783ab1` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=6 (of 7 off master) |
| `heads/feat/card-task-93442387` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-94d4b1de` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-95d13463` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-9718fd22` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-9798c2bc` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-98443ea1` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-993405ad` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-9958b325` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-9b83888e` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-9c55f7c3` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-9d1256aa` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-9df5bbae` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=8 (of 10 off master) |
| `heads/feat/card-task-9e28086d` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `heads/feat/card-task-a36875a7` | DELETABLE | origin does not advertise; tip in another advertised head; empty=3,patch-on-master=8 (of 11 off master) |
| `heads/feat/card-task-a57a83df` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=3 (of 4 off master) |
| `heads/feat/card-task-a65cecb5` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=1 (of 2 off master) |
| `heads/feat/card-task-a74257d7` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-a7ab0cda` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=4 (of 5 off master) |
| `heads/feat/card-task-a8685a72` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-a8a41f3c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-a8aae6c7` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-a8b2b9d5` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-a9bd4089` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-aa8e6802` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-aadd05ab` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=4 (of 5 off master) |
| `heads/feat/card-task-ac059bce` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-acaadd6c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-ad64242f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=7 (of 7 off master) |
| `heads/feat/card-task-b09e4b51` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=6 (of 6 off master) |
| `heads/feat/card-task-b0e67a1d` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-b2059dec` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=6 (of 6 off master) |
| `heads/feat/card-task-b2264aad` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-b560c49a` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=5 (of 5 off master) |
| `heads/feat/card-task-b5e6521b` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-b69ddb64` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-b84e8f78` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-b98941a2` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-bb47e1f5` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-bbd467bc` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-bc527408` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=13 (of 13 off master) |
| `heads/feat/card-task-bc91b68e` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-bca19885` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=3 (of 3 off master) |
| `heads/feat/card-task-bf7e81ee` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=6 (of 8 off master) |
| `heads/feat/card-task-bf99de0b` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-c01a66ce` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=9 (of 11 off master) |
| `heads/feat/card-task-c0a787fe` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-c107ce5c` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-c4154b22` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-c4b10def` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-c4b35a78` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-c531e682` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-c5885cc9` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-c75ce791` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=13 (of 13 off master) |
| `heads/feat/card-task-c7ba2641` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-c94fb1eb` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-c9570fad` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-ca27166b` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-ca2ccd03` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-cb96c3a4` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=11 (of 11 off master) |
| `heads/feat/card-task-cc0fc72f` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-ce55c8ef` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-cea3768c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-cf3436ed` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=11 (of 11 off master) |
| `heads/feat/card-task-cf6b1915` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-cff882c7` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `heads/feat/card-task-d0abdf16` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=2 (of 3 off master) |
| `heads/feat/card-task-d0b96418` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-d16587fd` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=3 (of 3 off master) |
| `heads/feat/card-task-d36aeec1` | DELETABLE | origin does not advertise; tip in another advertised head; empty=2,patch-on-master=6 (of 8 off master) |
| `heads/feat/card-task-d44699fc` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=9 (of 11 off master) |
| `heads/feat/card-task-d4689430` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-d59497d9` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-d76ef522` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `heads/feat/card-task-d806b77b` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=4 (of 6 off master) |
| `heads/feat/card-task-d917251d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-d9c6937d` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=7 (of 7 off master) |
| `heads/feat/card-task-da543237` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=12 (of 12 off master) |
| `heads/feat/card-task-da9b64d7` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=10 (of 10 off master) |
| `heads/feat/card-task-dafa05c9` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-db55819f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=18 (of 18 off master) |
| `heads/feat/card-task-db7a34db` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=5 (of 7 off master) |
| `heads/feat/card-task-dc933b90` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-dca8c233` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-df7e2e7b` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-e070938a` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-e09045ba` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=42 (of 42 off master) |
| `heads/feat/card-task-e1ea172f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-e4c8db54` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-e4f304dd` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-e617855b` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-e6317b27` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-e730ae22` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-e9516943` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=5 (of 5 off master) |
| `heads/feat/card-task-e9c2f76a` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-ea7d1a1c` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=4 (of 4 off master) |
| `heads/feat/card-task-eb7a06f3` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-ee01d18e` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-efb4ff41` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-efebc413` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-f1590d3d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-f15a5cfa` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-f22995f6` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-f23338dc` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-f3e2953f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `heads/feat/card-task-f447734d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-f4f02624` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-f6fff836` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-f7833ef2` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=8 (of 10 off master) |
| `heads/feat/card-task-f908a771` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-fa3a1135` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-fa8d1265` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-fb3b2b1c` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-fb612012` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `heads/feat/card-task-fc89bde9` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=5 (of 5 off master) |
| `heads/feat/card-task-fc909dcd` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-fcb1344d` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-fcb2e819` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `heads/feat/card-task-fd089677` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=8 (of 10 off master) |
| `heads/feat/card-task-fd9aa660` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-fe766b99` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/feat/card-task-ffe0545b` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/land-card-0706` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/land/card-0599-b1` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/land/card-0650-s4-193a4a79` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/land/card-0658-37c9d0e5` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/land/card-0660-611187dd` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/land/card-0664-b1` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/land/card-0681-p2` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/review-0593` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=36 (of 36 off master) |
| `heads/review/card-0672-r1-repair3-land` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/review/card-0679-r6-repair2-ad73820a` | DELETABLE | origin does not advertise; tip in another advertised head; empty=3,patch-on-master=9 (of 12 off master) |
| `heads/review/card-0693-81ca855b` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `heads/tmp/card-0684-land-20a0e763` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-002c8489` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=23 (of 23 off master) |
| `origin/feat/card-task-00f54e22` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-00f66c8f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-0112b9b0` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-014fddf9` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-015671f2` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-015dc1ee` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-02983cde` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=9 (of 9 off master) |
| `origin/feat/card-task-031276a1` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-0353ecae` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-03aa0437` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-04a8451b` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-0506a1cd` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=1 (of 2 off master) |
| `origin/feat/card-task-0520c00d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-05522ab3` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-0627264d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-06788c9e` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-06805739` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=8 (of 8 off master) |
| `origin/feat/card-task-0685ad6c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-07df9f1d` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-08084c5f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-08e6f2d2` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=18 (of 18 off master) |
| `origin/feat/card-task-097ed5ae` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-0aa9717c` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=15 (of 15 off master) |
| `origin/feat/card-task-0c270f73` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-0cc1623f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-0d279883` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=7 (of 7 off master) |
| `origin/feat/card-task-10632f25` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=4 (of 4 off master) |
| `origin/feat/card-task-10b8e177` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-12cd8c8b` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=4 (of 5 off master) |
| `origin/feat/card-task-1336064f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-137c1631` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=2 (of 3 off master) |
| `origin/feat/card-task-142c2756` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-15a86ac9` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=3 (of 4 off master) |
| `origin/feat/card-task-1668ddd6` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-169fe4dd` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=2 (of 3 off master) |
| `origin/feat/card-task-16d7e477` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-176e26d6` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=6 (of 6 off master) |
| `origin/feat/card-task-179f4c3c` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-18f52a40` | DELETABLE | origin does not advertise; tip in another advertised head; empty=2,patch-on-master=6 (of 8 off master) |
| `origin/feat/card-task-18fd01a3` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-19c7ff68` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=3 (of 3 off master) |
| `origin/feat/card-task-1aa86155` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-1c52bd82` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-1ceae9ac` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-1d0bcab2` | DELETABLE | origin does not advertise; tip in another advertised head; empty=3,patch-on-master=9 (of 12 off master) |
| `origin/feat/card-task-1d5b779b` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-1d5c05e5` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-1f9820dd` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=1 (of 2 off master) |
| `origin/feat/card-task-1fe6de8e` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=11 (of 11 off master) |
| `origin/feat/card-task-2060e274` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=3,patch-on-master=8 (of 11 off master) |
| `origin/feat/card-task-20a0e763` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-214023c6` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-22b951cd` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-22c54e3f` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=4 (of 4 off master) |
| `origin/feat/card-task-24354988` | DELETABLE | origin does not advertise; tip in another advertised head; empty=2,patch-on-master=6 (of 8 off master) |
| `origin/feat/card-task-244b654c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-24bc78b2` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-2700ba1e` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-275c5757` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=5 (of 6 off master) |
| `origin/feat/card-task-27d798b2` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-2837720d` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=8 (of 8 off master) |
| `origin/feat/card-task-290483e4` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-29c28d0c` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-2be23858` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=4 (of 4 off master) |
| `origin/feat/card-task-2c621fdc` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-2cd14d4c` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=3 (of 4 off master) |
| `origin/feat/card-task-2dd9fbf3` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-2e841047` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-2e8b3a67` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=5 (of 7 off master) |
| `origin/feat/card-task-2f0b267a` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-2fc0cff8` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=7 (of 7 off master) |
| `origin/feat/card-task-30103883` | DELETABLE | origin does not advertise; tip in another advertised head; empty=2,patch-on-master=5 (of 7 off master) |
| `origin/feat/card-task-303a8c1f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-3130100c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-320b47d9` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=6 (of 6 off master) |
| `origin/feat/card-task-354b15be` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=1 (of 2 off master) |
| `origin/feat/card-task-35b8f15d` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-364eea2f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `origin/feat/card-task-365d4862` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-37a70801` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=4 (of 4 off master) |
| `origin/feat/card-task-37c9d0e5` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `origin/feat/card-task-3816b58e` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=1 (of 2 off master) |
| `origin/feat/card-task-384a9033` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-3895b67b` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=6 (of 6 off master) |
| `origin/feat/card-task-39d0e7e2` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=54 (of 54 off master) |
| `origin/feat/card-task-3c593d7c` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-3c919969` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-3ce33fdc` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-3e3b33ac` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-3e89af3c` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-417f39b7` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-4219092b` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-42bbf9fc` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=2 (of 3 off master) |
| `origin/feat/card-task-43198086` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-43ac843d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-443b21c2` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=3,patch-on-master=6 (of 9 off master) |
| `origin/feat/card-task-44bf1e4a` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-4745f0d3` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-4779fa4c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-479c6aca` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-47be9689` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-47f087f7` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-492a3ddb` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-49cc02b8` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-4ce3b0ad` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=7 (of 9 off master) |
| `origin/feat/card-task-5057e9b9` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=5 (of 5 off master) |
| `origin/feat/card-task-5068986a` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-506cc44f` | DELETABLE | origin does not advertise; tip in another advertised head; empty=2,patch-on-master=5 (of 7 off master) |
| `origin/feat/card-task-507b17cf` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-50a2b232` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-5131091c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-51b599cb` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=23 (of 23 off master) |
| `origin/feat/card-task-520a5e94` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=2 (of 3 off master) |
| `origin/feat/card-task-520f3e0a` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=8 (of 8 off master) |
| `origin/feat/card-task-5294bdc8` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=11 (of 11 off master) |
| `origin/feat/card-task-5324df1a` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-53529ab7` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-56217783` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-567aeea4` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=3 (of 4 off master) |
| `origin/feat/card-task-57758b77` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-57fa2e6a` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=3 (of 4 off master) |
| `origin/feat/card-task-58f6d83c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-5b2256d5` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-5b76bd03` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-5c22c88c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-5dc73c10` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=3,patch-on-master=9 (of 12 off master) |
| `origin/feat/card-task-5e69d93d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-5ef74c0f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `origin/feat/card-task-5ef9bb0e` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-604d313f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-609f77b4` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=14 (of 14 off master) |
| `origin/feat/card-task-611187dd` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-6123de4a` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-6195a018` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=12 (of 12 off master) |
| `origin/feat/card-task-624f9824` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=8 (of 8 off master) |
| `origin/feat/card-task-64aedf93` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-65d09da0` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-663a02ac` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-66d7aac9` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=4 (of 4 off master) |
| `origin/feat/card-task-67e84356` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-6801de9a` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=3 (of 4 off master) |
| `origin/feat/card-task-68a64314` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-691831a7` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-695a54a1` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=5 (of 5 off master) |
| `origin/feat/card-task-69c7a060` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=48 (of 48 off master) |
| `origin/feat/card-task-6ad25498` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-6c2cf966` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=10 (of 10 off master) |
| `origin/feat/card-task-6ca40cd0` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=11 (of 11 off master) |
| `origin/feat/card-task-6d623e3b` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-6eed34e6` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=3,patch-on-master=9 (of 12 off master) |
| `origin/feat/card-task-70302dc6` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=5 (of 6 off master) |
| `origin/feat/card-task-7102ff54` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-71cc7048` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-7268d5be` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-743f9bdd` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-74da5d1b` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-75472579` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-7558d5cb` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-77629b10` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-7775c8ed` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-778fa5d5` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `origin/feat/card-task-78c5f868` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-79140c38` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-793dfb87` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-795c10ba` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-7c33fc2f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-7d58c913` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-7dc235bc` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=6 (of 6 off master) |
| `origin/feat/card-task-7f644884` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-81ca855b` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-81cb89f5` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-82140e9d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-837ff8d2` | DELETABLE | origin does not advertise; tip in another advertised head; empty=2,patch-on-master=5 (of 7 off master) |
| `origin/feat/card-task-8532af35` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-867de79e` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-86d683e9` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-86e57e3d` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-87fe30e6` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=4 (of 6 off master) |
| `origin/feat/card-task-8a3a78a0` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-8afa8f97` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-8c094bd8` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-8d5a188d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-8ea13a79` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-9063a47c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-914a96fd` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=3 (of 4 off master) |
| `origin/feat/card-task-9216c4a9` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-924d72d5` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=4 (of 4 off master) |
| `origin/feat/card-task-92783ab1` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=6 (of 7 off master) |
| `origin/feat/card-task-93442387` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-94d4b1de` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-95c5ebcf` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-95d13463` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-9718fd22` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-9798c2bc` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-98443ea1` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-993405ad` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-9b635cec` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-9b83888e` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-9c55f7c3` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-9d1256aa` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-9df5bbae` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=8 (of 10 off master) |
| `origin/feat/card-task-9e28086d` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `origin/feat/card-task-9e6c8c3a` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=18 (of 18 off master) |
| `origin/feat/card-task-a103fe6f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-a143618c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-a36875a7` | DELETABLE | origin does not advertise; tip in another advertised head; empty=3,patch-on-master=8 (of 11 off master) |
| `origin/feat/card-task-a57a83df` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=3 (of 4 off master) |
| `origin/feat/card-task-a65cecb5` | DELETABLE | origin does not advertise; tip in another advertised head; empty=1,patch-on-master=1 (of 2 off master) |
| `origin/feat/card-task-a7ab0cda` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=4 (of 5 off master) |
| `origin/feat/card-task-a8685a72` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-a8a41f3c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-a8aae6c7` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-a8f042bb` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-a9bd4089` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-aadd05ab` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=4 (of 5 off master) |
| `origin/feat/card-task-ac059bce` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-ac0d495e` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-acaadd6c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-ad64242f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=7 (of 7 off master) |
| `origin/feat/card-task-b09e4b51` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=6 (of 6 off master) |
| `origin/feat/card-task-b0e67a1d` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-b18bb8e3` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=4,patch-on-master=10 (of 14 off master) |
| `origin/feat/card-task-b2059dec` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=6 (of 6 off master) |
| `origin/feat/card-task-b2264aad` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-b560c49a` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=5 (of 5 off master) |
| `origin/feat/card-task-b5e6521b` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-b69ddb64` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-b84e8f78` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-b98941a2` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-bb47e1f5` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-bb813687` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-bbd467bc` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-bc527408` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=13 (of 13 off master) |
| `origin/feat/card-task-bc91b68e` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-bca19885` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=3 (of 3 off master) |
| `origin/feat/card-task-bddfb3cc` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=4 (of 4 off master) |
| `origin/feat/card-task-be4f9e5f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-bf7e81ee` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=6 (of 8 off master) |
| `origin/feat/card-task-bf99de0b` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-c01a66ce` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=9 (of 11 off master) |
| `origin/feat/card-task-c0a787fe` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-c107ce5c` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-c4154b22` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-c4b10def` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=3 (of 4 off master) |
| `origin/feat/card-task-c4b35a78` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-c531e682` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-c5885cc9` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-c75ce791` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=13 (of 13 off master) |
| `origin/feat/card-task-c7ba2641` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=19 (of 19 off master) |
| `origin/feat/card-task-c94fb1eb` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-c9570fad` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-ca27166b` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-ca2ccd03` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-cb96c3a4` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=11 (of 11 off master) |
| `origin/feat/card-task-cc0fc72f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-ce55c8ef` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-cea3768c` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-cf3436ed` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=11 (of 11 off master) |
| `origin/feat/card-task-cf6b1915` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-cff882c7` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `origin/feat/card-task-d0abdf16` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=2 (of 3 off master) |
| `origin/feat/card-task-d0b96418` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-d16587fd` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=3 (of 3 off master) |
| `origin/feat/card-task-d2ce3ed6` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-d36aeec1` | DELETABLE | origin does not advertise; tip in another advertised head; empty=2,patch-on-master=6 (of 8 off master) |
| `origin/feat/card-task-d44699fc` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=9 (of 11 off master) |
| `origin/feat/card-task-d4689430` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-d5219854` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-d6a88a73` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-d76ef522` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `origin/feat/card-task-d806b77b` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=4 (of 6 off master) |
| `origin/feat/card-task-d917251d` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-d92a0f97` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-d9c6937d` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=7 (of 7 off master) |
| `origin/feat/card-task-da543237` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=12 (of 12 off master) |
| `origin/feat/card-task-da9b64d7` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=10 (of 10 off master) |
| `origin/feat/card-task-dafa05c9` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-db55819f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=18 (of 18 off master) |
| `origin/feat/card-task-db7a34db` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=5 (of 7 off master) |
| `origin/feat/card-task-dbdfa428` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-dc933b90` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=5 (of 5 off master) |
| `origin/feat/card-task-dca8c233` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-df7e2e7b` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-e070938a` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-e09045ba` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=42 (of 42 off master) |
| `origin/feat/card-task-e1ea172f` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-e4c8db54` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-e4f304dd` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-e617855b` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-e6317b27` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-e730ae22` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-e9516943` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=5 (of 5 off master) |
| `origin/feat/card-task-e9c2f76a` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-ea7d1a1c` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=4 (of 4 off master) |
| `origin/feat/card-task-eb7a06f3` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-ee01d18e` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-efb4ff41` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-efebc413` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-f012a1d3` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `origin/feat/card-task-f15a5cfa` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-f22995f6` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-f23338dc` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-f3e2953f` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=3 (of 3 off master) |
| `origin/feat/card-task-f4f02624` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-f5d7a783` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=5 (of 5 off master) |
| `origin/feat/card-task-f602d622` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-f646569d` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=1,patch-on-master=2 (of 3 off master) |
| `origin/feat/card-task-f67e6efa` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=7 (of 7 off master) |
| `origin/feat/card-task-f6fff836` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-f7833ef2` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=8 (of 10 off master) |
| `origin/feat/card-task-f908a771` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-fa3a1135` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-fa8d1265` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-fb3b2b1c` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-fb612012` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=2 (of 2 off master) |
| `origin/feat/card-task-fc89bde9` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=5 (of 5 off master) |
| `origin/feat/card-task-fc909dcd` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-fcb1344d` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-fcb2e819` | DELETABLE | origin does not advertise; tip unpublished by graph; patch-on-master=1 (of 1 off master) |
| `origin/feat/card-task-fd089677` | DELETABLE | origin does not advertise; tip unpublished by graph; empty=2,patch-on-master=8 (of 10 off master) |
| `origin/feat/card-task-fd9aa660` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-fe766b99` | DELETABLE | origin does not advertise; tip on master; 0 commits off master |
| `origin/feat/card-task-ffe0545b` | DELETABLE | origin does not advertise; tip in another advertised head; patch-on-master=8 (of 8 off master) |
| `heads/feat/card-task-0c382461` | KEEP | unproven:patch-NOT-on-master=2; tip in another advertised head |
| `heads/feat/card-task-193a4a79` | KEEP | unproven:merge=1; tip unpublished by graph |
| `heads/feat/card-task-1d94da9e` | KEEP | unproven:merge=1; tip unpublished by graph |
| `heads/feat/card-task-254c8ed2` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-28fc3bab` | KEEP | unproven:patch-NOT-on-master=7; tip in another advertised head |
| `heads/feat/card-task-3088cbd1` | KEEP | unproven:patch-NOT-on-master=2; tip in another advertised head |
| `heads/feat/card-task-3b42ea58` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-5021291e` | KEEP | unproven:patch-NOT-on-master=3; tip in another advertised head |
| `heads/feat/card-task-56117908` | KEEP | unproven:patch-NOT-on-master=3; tip in another advertised head |
| `heads/feat/card-task-563b68b4` | KEEP | unproven:patch-NOT-on-master=3; tip in another advertised head |
| `heads/feat/card-task-5f1c5ca3` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-6ab39a3d` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-72340b63` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-7632092d` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-7c33f9d7` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-8752034b` | KEEP | unproven:patch-NOT-on-master=4; tip in another advertised head |
| `heads/feat/card-task-87af1bf6` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-89005d18` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-8a662a98` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-97ea55ef` | KEEP | unproven:merge=1; tip unpublished by graph |
| `heads/feat/card-task-b3f1660f` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-bd812298` | KEEP | unproven:patch-NOT-on-master=3; tip in another advertised head |
| `heads/feat/card-task-be23e6d7` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-be4f9e5f` | KEEP | unproven:patch-NOT-on-master=3; tip in another advertised head |
| `heads/feat/card-task-c30ee854` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-c739cb90` | KEEP | unproven:patch-NOT-on-master=2; tip in another advertised head |
| `heads/feat/card-task-d24e1b4d` | KEEP | unproven:merge=1; tip unpublished by graph |
| `heads/feat/card-task-d9b74136` | KEEP | unproven:patch-NOT-on-master=4; tip unpublished by graph |
| `heads/feat/card-task-dac395c9` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-dbded577` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-f87b49a7` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-f9f7f1a9` | KEEP | unproven:patch-NOT-on-master=2; tip in another advertised head |
| `heads/feat/card-task-fa0190b4-first-verified` | KEEP | fa0190b4(rescue verified); unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-fa0190b4-second-verified` | KEEP | fa0190b4(rescue verified); unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-task-fa0190b4` | KEEP | fa0190b4(rescue verified); unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-0c382461` | KEEP | unproven:patch-NOT-on-master=2; tip in another advertised head |
| `origin/feat/card-task-16178635` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-193a4a79` | KEEP | unproven:merge=1; tip unpublished by graph |
| `origin/feat/card-task-1d94da9e` | KEEP | unproven:merge=1; tip unpublished by graph |
| `origin/feat/card-task-254c8ed2` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-28fc3bab` | KEEP | unproven:patch-NOT-on-master=7; tip in another advertised head |
| `origin/feat/card-task-2db75657` | KEEP | unproven:patch-NOT-on-master=4; tip unpublished by graph |
| `origin/feat/card-task-3088cbd1` | KEEP | unproven:patch-NOT-on-master=2; tip in another advertised head |
| `origin/feat/card-task-3b42ea58` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-5021291e` | KEEP | unproven:patch-NOT-on-master=3; tip in another advertised head |
| `origin/feat/card-task-56117908` | KEEP | unproven:patch-NOT-on-master=3; tip in another advertised head |
| `origin/feat/card-task-5638a6b5` | KEEP | unproven:patch-NOT-on-master=4; tip unpublished by graph |
| `origin/feat/card-task-563b68b4` | KEEP | unproven:patch-NOT-on-master=3; tip in another advertised head |
| `origin/feat/card-task-5f1c5ca3` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-6ab39a3d` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-72340b63` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-7632092d` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-7a978aa8` | KEEP | unproven:patch-NOT-on-master=2; tip in another advertised head |
| `origin/feat/card-task-7c33f9d7` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-8752034b` | KEEP | unproven:patch-NOT-on-master=4; tip in another advertised head |
| `origin/feat/card-task-87af1bf6` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-89005d18` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-8a662a98` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-97ea55ef` | KEEP | unproven:merge=1; tip unpublished by graph |
| `origin/feat/card-task-a74257d7` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-b3f1660f` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-ba64b974` | KEEP | unproven:patch-NOT-on-master=4; tip unpublished by graph |
| `origin/feat/card-task-bd812298` | KEEP | unproven:patch-NOT-on-master=3; tip in another advertised head |
| `origin/feat/card-task-be23e6d7` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-c30ee854` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-c739cb90` | KEEP | unproven:patch-NOT-on-master=2; tip in another advertised head |
| `origin/feat/card-task-d24e1b4d` | KEEP | unproven:merge=1; tip unpublished by graph |
| `origin/feat/card-task-d59497d9` | KEEP | unproven:patch-NOT-on-master=2; tip in another advertised head |
| `origin/feat/card-task-d9b74136` | KEEP | unproven:patch-NOT-on-master=4; tip unpublished by graph |
| `origin/feat/card-task-dac395c9` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-dbded577` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-f40ea0b7` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-f87b49a7` | KEEP | unproven:patch-NOT-on-master=1; tip in another advertised head |
| `origin/feat/card-task-f9f7f1a9` | KEEP | unproven:patch-NOT-on-master=2; tip in another advertised head |
| `origin/feat/card-task-fa0190b4` | KEEP | fa0190b4(rescue verified); unproven:patch-NOT-on-master=1; tip in another advertised head |
| `heads/feat/card-0580-code-acaadd6c` | KEEP | advertised-on-origin |
| `heads/feat/card-0753-recovery-eb7a06f3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-002c7d4e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0035c5ba` | KEEP | advertised-on-origin |
| `heads/feat/card-task-00de7c7a` | KEEP | advertised-on-origin |
| `heads/feat/card-task-01cee5a0` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-0223149e` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-0293f857` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-02a60422` | KEEP | advertised-on-origin |
| `heads/feat/card-task-02b47ebb` | KEEP | advertised-on-origin |
| `heads/feat/card-task-02b52e5b` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-02c16198` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-02db8872` | KEEP | advertised-on-origin |
| `heads/feat/card-task-02e747d6` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-031e7c04` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-0323b204` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-03460307` | KEEP | advertised-on-origin |
| `heads/feat/card-task-036e79e2` | KEEP | advertised-on-origin |
| `heads/feat/card-task-043024dc` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-044f2398` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-0494e8d7` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-058eda4e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-05d5ec9e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0665711a` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=15 |
| `heads/feat/card-task-07590946` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-08164059` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-09747830` | KEEP | advertised-on-origin |
| `heads/feat/card-task-09a4e24c` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-0ad1c3c5` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-0b054a60` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-0b36421a` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0bebfaa7` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0bf6b526` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0c0689e5` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-0c0d9592` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0c31538a` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-0c638c88` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-0c961739` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-0cb2a72c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0cfcbf62` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0d4b53e9` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-0d765df1` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0d9b3bbd` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0db701ec` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0e946df2` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0ea0a623` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0eafbbee` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-0eccf2b4` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-0f59e5c1` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-0f6a815b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0f6dcde0` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0f936bc2` | KEEP | advertised-on-origin |
| `heads/feat/card-task-0fd79d67` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-0feceec0` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-101a6d71` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-105d1679` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-1064523d` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-10955a8f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-1135e745` | KEEP | advertised-on-origin |
| `heads/feat/card-task-1195268b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-11bd835e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-11f476f4` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-120cc365` | KEEP | advertised-on-origin |
| `heads/feat/card-task-12869ac9` | KEEP | advertised-on-origin |
| `heads/feat/card-task-12b2b535` | KEEP | advertised-on-origin |
| `heads/feat/card-task-12ca6c77` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-12cf7bf7` | KEEP | advertised-on-origin |
| `heads/feat/card-task-1300c9b8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-1302a18c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-130b15ef` | KEEP | advertised-on-origin; unproven:merge=1 |
| `heads/feat/card-task-130b283f` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-13149836` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-137ab344` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-13c90416` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-14f4b0eb` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-15310834` | KEEP | advertised-on-origin |
| `heads/feat/card-task-15ae6c85` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-15d06aa2` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-15d28060` | KEEP | advertised-on-origin |
| `heads/feat/card-task-15d5a0c9` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-1660f385` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-167104d9` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-167e5110` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-16f4b2bd` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-170e6f0d` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-1753ea5c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-184d9e56` | KEEP | advertised-on-origin |
| `heads/feat/card-task-18a3e6bc` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-1982599f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-19e7f181` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-1a73906f` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-1b2000a7` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-1b2e83e3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-1b431dcf` | KEEP | advertised-on-origin |
| `heads/feat/card-task-1b43dce9` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-1bdc7843` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-1c31680d` | KEEP | advertised-on-origin |
| `heads/feat/card-task-1cbe654d` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-1cbff6f5` | KEEP | advertised-on-origin |
| `heads/feat/card-task-1d339bcd` | KEEP | advertised-on-origin |
| `heads/feat/card-task-1d3e0d92` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-1dd9b908` | KEEP | advertised-on-origin |
| `heads/feat/card-task-1df2bada` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-1e0b8578` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-1e11a5d2` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-1e39a459` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-1e3dddab` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-1e6789cf` | KEEP | advertised-on-origin |
| `heads/feat/card-task-1e77c1d1` | KEEP | advertised-on-origin |
| `heads/feat/card-task-1f1ecffb` | KEEP | advertised-on-origin |
| `heads/feat/card-task-1f39ac17` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-1f3a0ecf` | KEEP | advertised-on-origin |
| `heads/feat/card-task-201510e2` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-207a2ec7` | KEEP | advertised-on-origin |
| `heads/feat/card-task-20e088d8` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-21412cdd` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2142d8e5` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-21cd95be` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-21e85ec1` | KEEP | advertised-on-origin |
| `heads/feat/card-task-21fd71fe` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-22882994` | KEEP | advertised-on-origin |
| `heads/feat/card-task-22910b98` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-22b182d6` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-23e91309` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-23ef39bb` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-2419fc72` | KEEP | advertised-on-origin |
| `heads/feat/card-task-244c2ff6` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-245a0a09` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-24777cc8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=12 |
| `heads/feat/card-task-24825bfb` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-24a51e38` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=15 |
| `heads/feat/card-task-24ab463d` | KEEP | advertised-on-origin |
| `heads/feat/card-task-24ca7610` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-2503d3a1` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2653d2b6` | KEEP | advertised-on-origin |
| `heads/feat/card-task-26622af0` | KEEP | advertised-on-origin |
| `heads/feat/card-task-26727f07` | KEEP | advertised-on-origin |
| `heads/feat/card-task-26d2f555` | KEEP | advertised-on-origin |
| `heads/feat/card-task-26d60c5d` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-271c88b9` | KEEP | advertised-on-origin |
| `heads/feat/card-task-273b3a0d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-2787b080` | KEEP | advertised-on-origin |
| `heads/feat/card-task-27dd8efb` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-27f2985f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-27fdf8c7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-28194b17` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-2822aabb` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=6 |
| `heads/feat/card-task-285cc907` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2892af2e` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-28dcbf6d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-28eaa3bf` | KEEP | advertised-on-origin |
| `heads/feat/card-task-293dbccc` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-298f4c9f` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-29fba8af` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-2a293dd6` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2a4ade56` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-2a5af10a` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2aa8cd10` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=10 |
| `heads/feat/card-task-2b6ade7e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2b722beb` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2ba26998` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2c35a27d` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-2cc36aa6` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2cfabeca` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-2d6f8680` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-2de224e3` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2e6d822d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-2e70e88b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2eabeec3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-2f178455` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-2fa25fee` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-2fbcb85f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2fcfcbf6` | KEEP | advertised-on-origin |
| `heads/feat/card-task-2fd07875` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3002779f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-307b9e62` | KEEP | advertised-on-origin |
| `heads/feat/card-task-307e1a7f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-30ae7b2a` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-310a9e1c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3111804b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-31ea7c27` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-322eb329` | KEEP | advertised-on-origin |
| `heads/feat/card-task-324cba42` | KEEP | advertised-on-origin |
| `heads/feat/card-task-32bb3e28` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-33b9032e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-340b7c92` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3416c8aa` | KEEP | advertised-on-origin |
| `heads/feat/card-task-34a18470` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3500c094` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3525d6b5` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3573055a` | KEEP | advertised-on-origin |
| `heads/feat/card-task-366502de` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3678a831` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-3692320a` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-36d70ef3` | KEEP | advertised-on-origin |
| `heads/feat/card-task-373933b4` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3775565a` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-37ad6b6c` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-387b2714` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3895bec1` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `heads/feat/card-task-38caa745` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-39327712` | KEEP | advertised-on-origin |
| `heads/feat/card-task-395283bc` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-39d02a5b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-3a21a1f7` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3a378666` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-3a3d8a89` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3a8fa5b1` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3acb6645` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3ad57002` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3ade011c` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3aebd909` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3b559ad9` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=5 |
| `heads/feat/card-task-3c03449e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3c486b6e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3c7f2103` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3cb582ee` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3cfdd4a3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-3d00cfc8` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3d5a2cc2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-3d677fcd` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3e370582` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3eceba1b` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3ed5b81c` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3efb29d0` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3f4700b0` | KEEP | advertised-on-origin |
| `heads/feat/card-task-3f5ad2f4` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3f946161` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3fa63bb3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-3fd04220` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-3fdafc77` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-4012cd3d` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-402bbca2` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-410c1bd2` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4170230f` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-418b258e` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-41df9d7f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-42198ac1` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-421f08bd` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-423562de` | KEEP | advertised-on-origin |
| `heads/feat/card-task-423c17b0` | KEEP | advertised-on-origin |
| `heads/feat/card-task-426eb7b7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-429e016c` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-42c99e08` | KEEP | advertised-on-origin |
| `heads/feat/card-task-42ffa25b` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-43597c0d` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-43883361` | KEEP | advertised-on-origin |
| `heads/feat/card-task-43cac7c9` | KEEP | advertised-on-origin |
| `heads/feat/card-task-43e89e4e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4407eec6` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-450924c6` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-4529ab28` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-456be05e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-45703b71` | KEEP | advertised-on-origin |
| `heads/feat/card-task-458db087` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-459aa7ef` | KEEP | advertised-on-origin |
| `heads/feat/card-task-463018b0` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4632d072` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-4671bbf7` | KEEP | advertised-on-origin |
| `heads/feat/card-task-46c8c351` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-47866837` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-47aa3487` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-47bff480` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=13 |
| `heads/feat/card-task-47e7ec64` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-489d8623` | KEEP | advertised-on-origin |
| `heads/feat/card-task-489e7487` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-48bbc16e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-48d0495d` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4926ab2c` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-4946f96d` | KEEP | advertised-on-origin |
| `heads/feat/card-task-49eea982` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4a1a4c4a` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4a41059c` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-4a97e419` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=8 |
| `heads/feat/card-task-4aa8cc21` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4b33c4d0` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4b6b25be` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-4b9c3cad` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4bb38374` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4bbc24bd` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4bbcbf05` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-4bd3b253` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-4c1697dc` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-4c1708dc` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-4c2309e1` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4ca86971` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4cdcf5c2` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-4d1acb84` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-4d9c7019` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-4ddd7b1b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-4deb485d` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4def6570` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-4e6ed8fa` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-4f4b7611` | KEEP | advertised-on-origin |
| `heads/feat/card-task-4f95b2ea` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-4fec0494` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-502c5876` | KEEP | advertised-on-origin |
| `heads/feat/card-task-503dd287` | KEEP | advertised-on-origin |
| `heads/feat/card-task-5064c0a9` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-50781993` | KEEP | advertised-on-origin |
| `heads/feat/card-task-50d85f42` | KEEP | advertised-on-origin |
| `heads/feat/card-task-50dfbb6c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-50ec2c2e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-51e09137` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-520c70c6` | KEEP | advertised-on-origin |
| `heads/feat/card-task-52bffc69` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-52c9f7d9` | KEEP | advertised-on-origin |
| `heads/feat/card-task-52e52800` | KEEP | advertised-on-origin |
| `heads/feat/card-task-530ed557` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-5320c5ab` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-535715ec` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-539915bf` | KEEP | advertised-on-origin |
| `heads/feat/card-task-53c7ab00` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-53e8eb86` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-53f435f3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=12 |
| `heads/feat/card-task-54118627` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-557f2fa3` | KEEP | advertised-on-origin |
| `heads/feat/card-task-55ea4e28` | KEEP | advertised-on-origin |
| `heads/feat/card-task-561e2b31` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-56a6e788` | KEEP | advertised-on-origin |
| `heads/feat/card-task-5724b53e` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-578728aa` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-57c0d543` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-58011e5e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-583b6151` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-59206213` | KEEP | advertised-on-origin |
| `heads/feat/card-task-594f207a` | KEEP | advertised-on-origin |
| `heads/feat/card-task-59594204` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-59909fb5` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-5a11f02c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-5a2e33f1` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=4 |
| `heads/feat/card-task-5ba28b37` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-5c29a0da` | KEEP | advertised-on-origin |
| `heads/feat/card-task-5cbbdf8f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-5dbe617b` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-5dd894aa` | KEEP | advertised-on-origin |
| `heads/feat/card-task-5e3ecbdd` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-5edeaddb` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-5ee2c9a3` | KEEP | advertised-on-origin |
| `heads/feat/card-task-5f187792` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-5f6b3388` | KEEP | advertised-on-origin |
| `heads/feat/card-task-5fcba512` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-60161398` | KEEP | advertised-on-origin |
| `heads/feat/card-task-60519379` | KEEP | advertised-on-origin |
| `heads/feat/card-task-60616b1c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-606486b1` | KEEP | advertised-on-origin |
| `heads/feat/card-task-608ed47c` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-60b7a87d` | KEEP | advertised-on-origin |
| `heads/feat/card-task-60eb81e0` | KEEP | advertised-on-origin |
| `heads/feat/card-task-6141b0b0` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=12 |
| `heads/feat/card-task-6159b95b` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-61b36860` | KEEP | advertised-on-origin |
| `heads/feat/card-task-62fc2d31` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-63057c79` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-632da924` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-63b3b3ea` | KEEP | advertised-on-origin |
| `heads/feat/card-task-63d584d5` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-643ebcc5` | KEEP | advertised-on-origin |
| `heads/feat/card-task-646d8caf` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-64734e3f` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-647c2693` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=7 |
| `heads/feat/card-task-64a87ae9` | KEEP | advertised-on-origin |
| `heads/feat/card-task-64b35564` | KEEP | advertised-on-origin |
| `heads/feat/card-task-64b6e881` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-64db976e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-65170902` | KEEP | advertised-on-origin |
| `heads/feat/card-task-65991c1e` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-65d9f56e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-65ec6671` | KEEP | advertised-on-origin |
| `heads/feat/card-task-662b5d7a` | KEEP | advertised-on-origin |
| `heads/feat/card-task-67ddf317` | KEEP | advertised-on-origin |
| `heads/feat/card-task-67ee8f94` | KEEP | advertised-on-origin |
| `heads/feat/card-task-68138e50` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-6871203f` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-687a7f27` | KEEP | advertised-on-origin |
| `heads/feat/card-task-68adb121` | KEEP | advertised-on-origin |
| `heads/feat/card-task-68ce5371` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-693fd5d6` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-697ca82c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-698c0e44` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=7 |
| `heads/feat/card-task-6a536d8d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-6a59d61b` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-6a7c1d47` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-6ab7ea2f` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-6b457b41` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=7 |
| `heads/feat/card-task-6b692111` | KEEP | advertised-on-origin |
| `heads/feat/card-task-6bc6293f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-6bc6dd56` | KEEP | advertised-on-origin |
| `heads/feat/card-task-6c123506` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-6c3e5034` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=4 |
| `heads/feat/card-task-6c6ca773` | KEEP | advertised-on-origin |
| `heads/feat/card-task-6c8bdb64` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-6cb82403` | KEEP | advertised-on-origin |
| `heads/feat/card-task-6cdcb609` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-6d82654c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-6d9d9275` | KEEP | advertised-on-origin |
| `heads/feat/card-task-6d9e7db0` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-6da81ee9` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-6dfe4f27` | KEEP | advertised-on-origin |
| `heads/feat/card-task-6e483022` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-6eb56d67` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-6ecce8bc` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-6f85f65f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-6fea402b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-6ff18828` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-700c3a06` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-7043d3b7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=15 |
| `heads/feat/card-task-706a7582` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-708672f3` | KEEP | advertised-on-origin |
| `heads/feat/card-task-70cdf499` | KEEP | advertised-on-origin; checked-out; unproven:merge=1,patch-NOT-on-master=14 |
| `heads/feat/card-task-71060b3f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-711458ee` | KEEP | advertised-on-origin |
| `heads/feat/card-task-716721c3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-719e9a27` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-71a5997b` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-71b33378` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-72445eaa` | KEEP | advertised-on-origin |
| `heads/feat/card-task-72657ad5` | KEEP | advertised-on-origin |
| `heads/feat/card-task-73423389` | KEEP | advertised-on-origin |
| `heads/feat/card-task-73486072` | KEEP | advertised-on-origin |
| `heads/feat/card-task-734d7f83` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-73b8b2ae` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-7404e327` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7492f59e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7494ba8a` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-74a9749b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-75089dd7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-75358b73` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-75a8a4fb` | KEEP | advertised-on-origin |
| `heads/feat/card-task-75abdd52` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-75cbfff8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-75df9046` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-760316da` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7647cb6a` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-76611588` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-76f2158d` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-774a1cc8` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-77a6073c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `heads/feat/card-task-77bbad75` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-78b9203d` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-78bf7628` | KEEP | advertised-on-origin |
| `heads/feat/card-task-78ef07b4` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-79011cb4` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-79264973` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-79295e2c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-79bf1ba7` | KEEP | advertised-on-origin |
| `heads/feat/card-task-79ee81f1` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-7a33c7c8` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7ae56a95` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7c60794c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7c7fdeeb` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-7c98e07e` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-7caf792c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-7cc04bb4` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7cc255f0` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-7cd0d0df` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7cdb752c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7ceae2ae` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7d285cb5` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-7d47772c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7d61d9fe` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-7df1caab` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7e08d8c6` | KEEP | advertised-on-origin |
| `heads/feat/card-task-7e75fc38` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-7ebf8aa2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-7ee266d3` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=12 |
| `heads/feat/card-task-7f1aaff2` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-7f356209` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-7fa08907` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=5 |
| `heads/feat/card-task-7fd885e9` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8013fd45` | KEEP | advertised-on-origin |
| `heads/feat/card-task-804aca36` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-818582a5` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-81922318` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-81bc7f5d` | KEEP | advertised-on-origin |
| `heads/feat/card-task-81c898bc` | KEEP | advertised-on-origin |
| `heads/feat/card-task-82a076e4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-82acf2fd` | KEEP | advertised-on-origin |
| `heads/feat/card-task-82ba618b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-835dd574` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-83ce0cea` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-84de0093` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-8500df23` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-851453eb` | KEEP | advertised-on-origin |
| `heads/feat/card-task-859094b8` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-8609ed9b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-86462e93` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-8647113f` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-86684fa2` | KEEP | advertised-on-origin |
| `heads/feat/card-task-867c55df` | KEEP | advertised-on-origin |
| `heads/feat/card-task-86c3c949` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-86fa9c01` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-86fe6165` | KEEP | advertised-on-origin |
| `heads/feat/card-task-870ad53e` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-87609c55` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-8798ecd3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-880ce135` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8845f4b0` | KEEP | advertised-on-origin |
| `heads/feat/card-task-896f3d65` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-89719e41` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-899e6f5a` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-89af9e59` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8a0ddf38` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8a25ce6e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=13 |
| `heads/feat/card-task-8a662a98-integrated` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8a7132cb` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8a7d4d16` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-8ac49e37` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-8b2ab56f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8b31ec87` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-8b35c4ef` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8b460fc4` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8b51f4e4` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8b5bba6f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8b690c54` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-8b6b42a9` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=6 |
| `heads/feat/card-task-8bb4bae8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=16 |
| `heads/feat/card-task-8bc1672b` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-8cc5f317` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8d32f2c5` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8d4cd03e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8d5aa4fc` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8db64eb9` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-8e27c0a0` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8eacb518` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8eacfa87` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-8ec6b28e` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-8ec6d6ff` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `heads/feat/card-task-8eceacaf` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-8ed118ba` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8ee59620` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8eed758c` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-8f5d5036` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8f9997da` | KEEP | advertised-on-origin |
| `heads/feat/card-task-8fa6b6ce` | KEEP | advertised-on-origin |
| `heads/feat/card-task-90228735` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-903bf8a7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-90dc342a` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-90f3f838` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-9179f089` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-92023e45` | KEEP | advertised-on-origin |
| `heads/feat/card-task-928f0ce3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-929f4620` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-92c4f810` | KEEP | advertised-on-origin |
| `heads/feat/card-task-93eea7de` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-957902d6` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-9579b548` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-9617960b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-961ee768` | KEEP | advertised-on-origin |
| `heads/feat/card-task-963170ad` | KEEP | advertised-on-origin |
| `heads/feat/card-task-96ae83d8` | KEEP | advertised-on-origin |
| `heads/feat/card-task-96d373ab` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-97c00028` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-97e2c86f` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-98260fdf` | KEEP | advertised-on-origin |
| `heads/feat/card-task-982783af` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-9828b7ff` | KEEP | advertised-on-origin |
| `heads/feat/card-task-98362da3` | KEEP | advertised-on-origin |
| `heads/feat/card-task-9888335c` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-98c9aab2` | KEEP | advertised-on-origin |
| `heads/feat/card-task-98f2ad4f` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-99b691d8` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-99efb48f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-99ff98dc` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-9a1c17e8` | KEEP | advertised-on-origin |
| `heads/feat/card-task-9ad681e2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-9b021c06` | KEEP | advertised-on-origin |
| `heads/feat/card-task-9b28be80` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-9bae45cf` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-9bb5cdee` | KEEP | advertised-on-origin |
| `heads/feat/card-task-9bbc9566` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-9bcf4ea9` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-9c43f660` | KEEP | advertised-on-origin |
| `heads/feat/card-task-9d394a6d` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-9dbe1877` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-9de0189e` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-9e8d8472` | KEEP | advertised-on-origin |
| `heads/feat/card-task-9e98878b` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-9ea91912` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-9ec344d4` | KEEP | advertised-on-origin |
| `heads/feat/card-task-9ed7da3b` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-9f4b5eae` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-9f69ac66` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-9f6a89ca` | KEEP | advertised-on-origin |
| `heads/feat/card-task-9fe76654` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-a062496e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a073b2d8` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a08e6d8d` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=16 |
| `heads/feat/card-task-a09a10b1` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a0ea230a` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-a13bca47` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a1db6174` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a1ee8274` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a2558b01` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a2601af5` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a284cf76` | KEEP | advertised-on-origin; checked-out; unproven:merge=1,patch-NOT-on-master=13 |
| `heads/feat/card-task-a2eb29f3` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a30c729b` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a3265594` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a3c7520f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a402da69` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a41dc3ed` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a4290b71` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a5c4d4a7` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a5c51cc2` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a61c829b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a650f4b4` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a6a7fba7` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a79d4131` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a7b63d96` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a7ce7cf4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-a7d678ce` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a7e02b29` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a83e4b62` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a87b9e00` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a8a78ab3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-a8e5a1b9` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a946111c` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-a9848fbe` | KEEP | advertised-on-origin |
| `heads/feat/card-task-a9dc938e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-a9ff548b` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-aa0904bc` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-aa2efb99` | KEEP | advertised-on-origin |
| `heads/feat/card-task-aa7cf6db` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-aab580ff` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-aae956f6` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-ab6c59b3` | KEEP | advertised-on-origin |
| `heads/feat/card-task-abc66011` | KEEP | advertised-on-origin |
| `heads/feat/card-task-abc9f7d5` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-abea11d8` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ac940931` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ad020e8c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ad5581cc` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ad910b87` | KEEP | advertised-on-origin |
| `heads/feat/card-task-adbad12b` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-addeb569` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-ae40541f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ae805b71` | KEEP | advertised-on-origin |
| `heads/feat/card-task-aee6182e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-af2a57eb` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-aff1b9c6` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-b016a2d1` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-b01f2e7b` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b09dfba4` | KEEP | advertised-on-origin |
| `heads/feat/card-task-b0c746ed` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b11d7d2d` | KEEP | advertised-on-origin |
| `heads/feat/card-task-b12e3f04` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b1ffd29e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-b227ba87` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b249882f` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-b288ec96` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b29278e3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b2a94f8e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-b2b9c307` | KEEP | advertised-on-origin |
| `heads/feat/card-task-b2c70f5f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-b32c3e95` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b367debd` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b3777342` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b3ff9b03` | KEEP | advertised-on-origin |
| `heads/feat/card-task-b46ffcfa` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-b491b9c4` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b49221c2` | KEEP | advertised-on-origin |
| `heads/feat/card-task-b5228641` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b5603fc8` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=15 |
| `heads/feat/card-task-b5c4f994` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=6 |
| `heads/feat/card-task-b5c8e753` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b5dff6bc` | KEEP | advertised-on-origin |
| `heads/feat/card-task-b6180921` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b61ac812` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b630f3bf` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b657a1e2` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-b71467ba` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b73275c8` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b7c17822` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b8ab036f` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-b8f22709` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-b9ca189f` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-ba063df7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-bb32fef5` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-bb485c68` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-bbc126e0` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-bbfbb7ac` | KEEP | advertised-on-origin |
| `heads/feat/card-task-bc0176ff` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-bc1b49f8` | KEEP | advertised-on-origin |
| `heads/feat/card-task-bc463e02` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-bc8ae8b9` | KEEP | advertised-on-origin |
| `heads/feat/card-task-bc986d96` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-bc9c67d3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-bd02f8d9` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-bd63f38c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-be035022` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-be710c61` | KEEP | advertised-on-origin |
| `heads/feat/card-task-bed39f89` | KEEP | advertised-on-origin; checked-out; unproven:merge=1,patch-NOT-on-master=8 |
| `heads/feat/card-task-befec73c` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-bf96365b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=11 |
| `heads/feat/card-task-bfc79905` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-c0204cae` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-c03af147` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `heads/feat/card-task-c0543045` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-c087dcaf` | KEEP | advertised-on-origin |
| `heads/feat/card-task-c126dc89` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-c163f2e6` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-c1f1f18e` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-c25c13c0` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-c34420cd` | KEEP | advertised-on-origin |
| `heads/feat/card-task-c37846cc` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-c389db24` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-c3e154a8` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-c43eeef4` | KEEP | advertised-on-origin |
| `heads/feat/card-task-c495f1d8` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-c5253cbc` | KEEP | advertised-on-origin |
| `heads/feat/card-task-c5973812` | KEEP | advertised-on-origin |
| `heads/feat/card-task-c5d2ae3a` | KEEP | advertised-on-origin |
| `heads/feat/card-task-c6ab7ea4` | KEEP | advertised-on-origin |
| `heads/feat/card-task-c6fe67e8` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-c703a9f8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `heads/feat/card-task-c74bc66c` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-c7de934f` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-c84abfd3` | KEEP | advertised-on-origin |
| `heads/feat/card-task-c8655038` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-c8699754` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-c86a05b3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-c916f049` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-c9945af6` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-c99b5b44` | KEEP | advertised-on-origin |
| `heads/feat/card-task-c99e35fa` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ca3d4a6b` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-ca6c0df5` | KEEP | advertised-on-origin |
| `heads/feat/card-task-cab6a109` | KEEP | advertised-on-origin |
| `heads/feat/card-task-cac8ee15` | KEEP | advertised-on-origin |
| `heads/feat/card-task-cc0dab3d` | KEEP | advertised-on-origin |
| `heads/feat/card-task-cc63c869` | KEEP | advertised-on-origin |
| `heads/feat/card-task-cc67ba1a` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ccabb6db` | KEEP | advertised-on-origin |
| `heads/feat/card-task-cd0b1657` | KEEP | advertised-on-origin |
| `heads/feat/card-task-cdd8d229` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-ce76a4e3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-cea6682c` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-cec70c48` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-cef1a2a9` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-cffd614e` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-d0491157` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-d09d94da` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-d149ce14` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-d1505c60` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d170a7e9` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-d17e4301` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d1c1630d` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-d1dbb697` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d20ed3ac` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-d276cf4b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d2a0d121` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-d2cd1c50` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d2cfaefd` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d34a2249` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d39c5ba8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-d3bc1416` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d3e917ce` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-d417892b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d4195168` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d422c5a9` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-d47edbbf` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-d493e467` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-d4df602a` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d5234cdc` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-d534fa92` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d535eeec` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-d579272f` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-d5b6713d` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d662028c` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=10 |
| `heads/feat/card-task-d697ff85` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-d6ff6ce4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=7 |
| `heads/feat/card-task-d7091f08` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-d72a027c` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-d73d91aa` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-d800c0b4` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-d81207e7` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d8a7975c` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d8aebb25` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d944a661` | KEEP | advertised-on-origin |
| `heads/feat/card-task-d9b1f124` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-d9f039a1` | KEEP | advertised-on-origin |
| `heads/feat/card-task-da4026d2` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=5 |
| `heads/feat/card-task-da49d22e` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-da6e8e47` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-da7e1d0b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-db094bcc` | KEEP | advertised-on-origin |
| `heads/feat/card-task-db1c9f98` | KEEP | advertised-on-origin |
| `heads/feat/card-task-db8122fd` | KEEP | advertised-on-origin |
| `heads/feat/card-task-dbb3ad22` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-dbb940c9` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-dc343d8e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-dcb476c9` | KEEP | advertised-on-origin |
| `heads/feat/card-task-dcc71cc0` | KEEP | advertised-on-origin |
| `heads/feat/card-task-dd036af2` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-dd3c58b5` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-dd9af3a7` | KEEP | advertised-on-origin |
| `heads/feat/card-task-dde7cd59` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `heads/feat/card-task-ddf1b667` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ddfe9f27` | KEEP | advertised-on-origin |
| `heads/feat/card-task-de5b6f9b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-de732954` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-deedf9dd` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-df74a022` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-dfc07c3a` | KEEP | advertised-on-origin |
| `heads/feat/card-task-dfc60293` | KEEP | advertised-on-origin |
| `heads/feat/card-task-dfcc7e0d` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-dfea030e` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-dff4e872` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e0fc3119` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-e116f6ae` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e137ad15` | KEEP | advertised-on-origin |
| `heads/feat/card-task-e191838e` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-e1b6cb4a` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e21405f0` | KEEP | advertised-on-origin |
| `heads/feat/card-task-e21bd32f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-e283c453` | KEEP | advertised-on-origin |
| `heads/feat/card-task-e29ffab8` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e2a0f2c9` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-e2b98a9a` | KEEP | advertised-on-origin |
| `heads/feat/card-task-e2b9bf48` | KEEP | advertised-on-origin |
| `heads/feat/card-task-e2c04614` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e41a7005` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e4b3d539` | KEEP | advertised-on-origin |
| `heads/feat/card-task-e4cfbe7e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-e5cf17ba` | KEEP | advertised-on-origin |
| `heads/feat/card-task-e5d0294c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-e5ed1dbf` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e6f1295a` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e6f3b241` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e739d9aa` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e772aac0` | KEEP | advertised-on-origin |
| `heads/feat/card-task-e78354e6` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-e7c20aaa` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-e839b406` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-e846c5d0` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e86672a3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e873d3aa` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-e9225bea` | KEEP | advertised-on-origin |
| `heads/feat/card-task-e982d170` | KEEP | advertised-on-origin |
| `heads/feat/card-task-e98a01c2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=11 |
| `heads/feat/card-task-ea3525d6` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ead491e1` | KEEP | advertised-on-origin |
| `heads/feat/card-task-eaf65f8b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-eb416191` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ebc14e49` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ec0d8cc3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-ec59b256` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ec5f895d` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-ec646a15` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-ecea46a3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-ecf5a387` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-ed1112b2` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ed41cbac` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-ed5dcb00` | KEEP | advertised-on-origin |
| `heads/feat/card-task-edd44e28` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-ee18a226` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-ee5c31b8` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-ee823591` | KEEP | advertised-on-origin |
| `heads/feat/card-task-eea9b11b` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-eec32965` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ef329fb0` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-ef4f4ec4` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ef7c4ee0` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-efcd5920` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f0251152` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f04e4312` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=7 |
| `heads/feat/card-task-f065f946` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-f09056f4` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-f0a7c7cd` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f0aad56e` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-f0e10c86` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f0ed11da` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f122b9de` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `heads/feat/card-task-f1713f40` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-f218db85` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-f2306cc6` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-f2308a09` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f268c00e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f2788b47` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-f32006d5` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `heads/feat/card-task-f376ed36` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f450c05a` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-f4ac59a9` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f4ef4af4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-f500932f` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-f5408ebf` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-f5cb8590` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f62de453` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-f69e8b6e` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-f6e2a0f6` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f6ea65ba` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-f727964e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f772ba73` | KEEP | advertised-on-origin |
| `heads/feat/card-task-f8613557` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-f9183542` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=4 |
| `heads/feat/card-task-f92f723a` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-f94c8fa4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-fa0190b4-rebased` | KEEP | advertised-on-origin; fa0190b4(rescue verified); unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-fa16db12` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-fa23b50e` | KEEP | advertised-on-origin |
| `heads/feat/card-task-fa3ed513` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-fa627fa3` | KEEP | advertised-on-origin |
| `heads/feat/card-task-fa69124f` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-fa74e03b` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-fb1a06fc` | KEEP | advertised-on-origin |
| `heads/feat/card-task-fb8aaac5` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-fb8fa4a9` | KEEP | advertised-on-origin |
| `heads/feat/card-task-fbe3745d` | KEEP | advertised-on-origin |
| `heads/feat/card-task-fc539741` | KEEP | advertised-on-origin |
| `heads/feat/card-task-fc62d370` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `heads/feat/card-task-fc7e26a7` | KEEP | advertised-on-origin |
| `heads/feat/card-task-fc94cf6f` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `heads/feat/card-task-fcb8859a` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-fcc274e3` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-fcc6014b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-fceb9bcc` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-fd423b29` | KEEP | advertised-on-origin; checked-out; unproven:patch-NOT-on-master=2 |
| `heads/feat/card-task-fdb14a5c` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-fddbeaf6` | KEEP | advertised-on-origin |
| `heads/feat/card-task-fddf3e2b` | KEEP | advertised-on-origin |
| `heads/feat/card-task-fe24dc11` | KEEP | advertised-on-origin |
| `heads/feat/card-task-fe74068f` | KEEP | advertised-on-origin |
| `heads/feat/card-task-fea04260` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-fea45f54` | KEEP | advertised-on-origin; checked-out |
| `heads/feat/card-task-fede62ef` | KEEP | advertised-on-origin |
| `heads/feat/card-task-ffc43849` | KEEP | advertised-on-origin; checked-out; unproven:merge=1,patch-NOT-on-master=1 |
| `heads/feat/card-task-ffc7e9ad` | KEEP | advertised-on-origin; checked-out |
| `heads/review/card-0665-final-28fc3bab` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/backup/card-task-022820cb-f58aa5bf` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/card-0322-routing-pin-candidates` | KEEP | advertised-on-origin |
| `origin/card-0417-phone` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=8 |
| `origin/docs/card-0420-test-design-ae2d76bf` | KEEP | advertised-on-origin |
| `origin/feat/card-0580-code-acaadd6c` | KEEP | advertised-on-origin |
| `origin/feat/card-0753-recovery-eb7a06f3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-1006-a2-capture-receipt` | KEEP | advertised-on-origin |
| `origin/feat/card-cut-b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-002c7d4e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0035c5ba` | KEEP | advertised-on-origin |
| `origin/feat/card-task-00de7c7a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0100d53d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-01cee5a0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0223149e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-022820cb` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=6 |
| `origin/feat/card-task-0293f857` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-02a60422` | KEEP | advertised-on-origin |
| `origin/feat/card-task-02b47ebb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-02b52e5b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-02c16198` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-02db8872` | KEEP | advertised-on-origin |
| `origin/feat/card-task-02e747d6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-031e7c04` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0323b204` | KEEP | advertised-on-origin |
| `origin/feat/card-task-03460307` | KEEP | advertised-on-origin |
| `origin/feat/card-task-034f38be` | KEEP | advertised-on-origin |
| `origin/feat/card-task-036e79e2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-037ab247` | KEEP | advertised-on-origin |
| `origin/feat/card-task-043024dc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-044f2398` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0494e8d7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0505ce7d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-050cb713` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-058eda4e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-05d5ec9e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-062bfba3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-0665711a` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=15 |
| `origin/feat/card-task-0684da8e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-07248498` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-07590946` | KEEP | advertised-on-origin |
| `origin/feat/card-task-08164059` | KEEP | advertised-on-origin |
| `origin/feat/card-task-08dfb9cc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0929eb35` | KEEP | advertised-on-origin |
| `origin/feat/card-task-09747830` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0998ad05` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-09a4e24c` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-0ad1c3c5` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-0b054a60` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0b309def` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0b36421a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0bebfaa7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0bf6b526` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0c0689e5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0c0d9592` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0c31538a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0c45d6e7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0c638c88` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-0c961739` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0cb2a72c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0cfcbf62` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0d48a707` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-0d4b53e9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0d678e51` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-0d765df1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0d9b3bbd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0db701ec` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0de0cc0c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-0e946df2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0ea0a623` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0eafbbee` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0eb35cd2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-0eccf2b4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0f59e5c1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0f6a815b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0f6dcde0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0f936bc2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0fb7a5d8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-0fd79d67` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-0feceec0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-101a6d71` | KEEP | advertised-on-origin |
| `origin/feat/card-task-105d1679` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1064523d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-10955a8f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1135e745` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1195268b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-11bd835e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-11f476f4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-120cc365` | KEEP | advertised-on-origin |
| `origin/feat/card-task-12869ac9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-12b2b535` | KEEP | advertised-on-origin |
| `origin/feat/card-task-12ca6c77` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-12cf7bf7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1300c9b8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-1302a18c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-130b15ef` | KEEP | advertised-on-origin; unproven:merge=1 |
| `origin/feat/card-task-130b283f` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-13149836` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1363353b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-137ab344` | KEEP | advertised-on-origin |
| `origin/feat/card-task-13c90416` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-14f4b0eb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-151b4e13` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=1 |
| `origin/feat/card-task-15310834` | KEEP | advertised-on-origin |
| `origin/feat/card-task-157b7f62` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-15ae6c85` | KEEP | advertised-on-origin |
| `origin/feat/card-task-15d06aa2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-15d28060` | KEEP | advertised-on-origin |
| `origin/feat/card-task-15d5a0c9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1660f385` | KEEP | advertised-on-origin |
| `origin/feat/card-task-167104d9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-167e5110` | KEEP | advertised-on-origin |
| `origin/feat/card-task-16f4b2bd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-170e6f0d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1753ea5c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-178a95ab` | KEEP | advertised-on-origin |
| `origin/feat/card-task-17c504bb` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-1807540f` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-184d9e56` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1857c5d9` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-18a3e6bc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1978e4de` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-1982599f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-19e7f181` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-1a0d1215` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-1a1a64fc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1a73906f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1b2000a7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1b2e83e3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1b431dcf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1b43dce9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1bcc1479` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-1bdc7843` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-1c31680d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1cbe654d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1cbff6f5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1d0f6684` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1d339bcd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1d3e0d92` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-1dd9b908` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1df2bada` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-1e0b8578` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-1e11a5d2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1e39a459` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1e3dddab` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1e6789cf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1e77c1d1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1ef5f77e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-1f1c67b6` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-1f1ecffb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1f39ac17` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-1f3a0ecf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-1fff5c74` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-201510e2` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-207a2ec7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-20b4b6ad` | KEEP | advertised-on-origin |
| `origin/feat/card-task-20e088d8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-21412cdd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2142d8e5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-214410b4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2198db1d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-21cd95be` | KEEP | advertised-on-origin |
| `origin/feat/card-task-21e85ec1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-21fd71fe` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-22882994` | KEEP | advertised-on-origin |
| `origin/feat/card-task-22910b98` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-22b182d6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-23e91309` | KEEP | advertised-on-origin |
| `origin/feat/card-task-23ef39bb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2419fc72` | KEEP | advertised-on-origin |
| `origin/feat/card-task-244c2ff6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-245a0a09` | KEEP | advertised-on-origin |
| `origin/feat/card-task-24777cc8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=12 |
| `origin/feat/card-task-24825bfb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-24a51e38` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=15 |
| `origin/feat/card-task-24ab463d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-24ca7610` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2503d3a1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-25cb35dd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-261a4a51` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-263541a1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2653d2b6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-26622af0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-26727f07` | KEEP | advertised-on-origin |
| `origin/feat/card-task-26d2f555` | KEEP | advertised-on-origin |
| `origin/feat/card-task-26d60c5d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-26d9c3d8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-271c88b9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-273b3a0d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-2787b080` | KEEP | advertised-on-origin |
| `origin/feat/card-task-27d48d06` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-27dd8efb` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-27f2985f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-27fdf8c7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-28194b17` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2822aabb` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=6 |
| `origin/feat/card-task-2824ba4c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2853f966` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-285cc907` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2892af2e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-28dcbf6d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-28eaa3bf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-292d0813` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-293dbccc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-298f4c9f` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-29bb901f` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-29fba8af` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2a293dd6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2a4ade56` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2a5af10a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2aa8cd10` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=10 |
| `origin/feat/card-task-2b4441a8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2b553928` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-2b6ade7e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2b722beb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2ba26998` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2ba71f6e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2c35a27d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2cc36aa6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2cfabeca` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2d6f8680` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2d76eab4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2de224e3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2e000c83` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-2e6d822d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-2e70e88b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2eabeec3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2f178455` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-2fa25fee` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2fbcb85f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2fcfcbf6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-2fd07875` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3002779f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-307b9e62` | KEEP | advertised-on-origin |
| `origin/feat/card-task-307e1a7f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-30ae7b2a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-310a9e1c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3111804b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-31859561` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-318daeb7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-31ea7c27` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-322eb329` | KEEP | advertised-on-origin |
| `origin/feat/card-task-324cba42` | KEEP | advertised-on-origin |
| `origin/feat/card-task-32bb3e28` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-32c16f61` | KEEP | advertised-on-origin |
| `origin/feat/card-task-33a85072` | KEEP | advertised-on-origin |
| `origin/feat/card-task-33b9032e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-340b7c92` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3416c8aa` | KEEP | advertised-on-origin |
| `origin/feat/card-task-341c1bad` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=5 |
| `origin/feat/card-task-34794d32` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-34a18470` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3500c094` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3525d6b5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3573055a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-360e223e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-366502de` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3678a831` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-3692320a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-36d70ef3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-373933b4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3775565a` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-37ad6b6c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-37ec0983` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-386a95bf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-387b2714` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3895bec1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-38caa745` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-39327712` | KEEP | advertised-on-origin |
| `origin/feat/card-task-395283bc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3986b2f9` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-39d02a5b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-3a21a1f7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3a378666` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-3a3d8a89` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3a8fa5b1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3acb6645` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3ad57002` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3ade011c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3aebd909` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3b559ad9` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=5 |
| `origin/feat/card-task-3bf6c1f4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=5 |
| `origin/feat/card-task-3c03449e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3c486b6e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3c7f2103` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3cb582ee` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3cfdd4a3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-3d00cfc8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3d5a2cc2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-3d677fcd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3e370582` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3eceba1b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3ed5b81c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3efb29d0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3f4700b0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3f5ad2f4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3f946161` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3fa63bb3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-3fd04220` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-3fdafc77` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4012cd3d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-402bbca2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-40597978` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-4095e3f8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-410c1bd2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4170230f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-418b258e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-41df9d7f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-42198ac1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-421f08bd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-423562de` | KEEP | advertised-on-origin |
| `origin/feat/card-task-423c17b0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-426eb7b7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-429e016c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-42c99e08` | KEEP | advertised-on-origin |
| `origin/feat/card-task-42ffa25b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-43597c0d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-43883361` | KEEP | advertised-on-origin |
| `origin/feat/card-task-43cac7c9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-43e89e4e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4407eec6` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-450924c6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4529ab28` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-456be05e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-45703b71` | KEEP | advertised-on-origin |
| `origin/feat/card-task-458db087` | KEEP | advertised-on-origin |
| `origin/feat/card-task-459aa7ef` | KEEP | advertised-on-origin |
| `origin/feat/card-task-463018b0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4632d072` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-4671bbf7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-46c55ae6` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-46c8c351` | KEEP | advertised-on-origin |
| `origin/feat/card-task-47866837` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-47aa3487` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-47b86ea8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-47bff480` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=13 |
| `origin/feat/card-task-47e7ec64` | KEEP | advertised-on-origin |
| `origin/feat/card-task-48888860` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-489d8623` | KEEP | advertised-on-origin |
| `origin/feat/card-task-489e7487` | KEEP | advertised-on-origin |
| `origin/feat/card-task-48bbc16e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-48d0495d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4926ab2c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4946f96d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-49545b22` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-49e32a6e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-49eea982` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4a1a4c4a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4a41059c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4a97e419` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=8 |
| `origin/feat/card-task-4aa8cc21` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4adbee37` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-4b33c4d0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4b429f09` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4b6b25be` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-4b9c3cad` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4bb38374` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4bbc24bd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4bbcbf05` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4bd3b253` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4c1697dc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4c1708dc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4c2309e1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4c46371a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4c9b617b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4ca86971` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4cdcf5c2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4d1acb84` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4d9c7019` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-4ddd7b1b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-4deb485d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4def6570` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4e6ed8fa` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-4e9f5c29` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-4f067d65` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4f4b7611` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4f849a0e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-4f95b2ea` | KEEP | advertised-on-origin |
| `origin/feat/card-task-4fe68cac` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-4fec0494` | KEEP | advertised-on-origin |
| `origin/feat/card-task-502c5876` | KEEP | advertised-on-origin |
| `origin/feat/card-task-503dd287` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5064c0a9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-50781993` | KEEP | advertised-on-origin |
| `origin/feat/card-task-50a6b1fb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-50d85f42` | KEEP | advertised-on-origin |
| `origin/feat/card-task-50dfbb6c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-50ec2c2e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5102d194` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5120c098` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-51e09137` | KEEP | advertised-on-origin |
| `origin/feat/card-task-51e46dfa` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-520c70c6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-52bffc69` | KEEP | advertised-on-origin |
| `origin/feat/card-task-52c9f7d9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-52e52800` | KEEP | advertised-on-origin |
| `origin/feat/card-task-530ed557` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5320c5ab` | KEEP | advertised-on-origin |
| `origin/feat/card-task-535715ec` | KEEP | advertised-on-origin |
| `origin/feat/card-task-539915bf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-53c7ab00` | KEEP | advertised-on-origin |
| `origin/feat/card-task-53e8eb86` | KEEP | advertised-on-origin |
| `origin/feat/card-task-53f435f3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=12 |
| `origin/feat/card-task-54118627` | KEEP | advertised-on-origin |
| `origin/feat/card-task-557f2fa3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-55ea4e28` | KEEP | advertised-on-origin |
| `origin/feat/card-task-561e2b31` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-56a6e788` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5724b53e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-578728aa` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-5799d88c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-579e4eba` | KEEP | advertised-on-origin |
| `origin/feat/card-task-57c0d543` | KEEP | advertised-on-origin |
| `origin/feat/card-task-58011e5e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-583b6151` | KEEP | advertised-on-origin |
| `origin/feat/card-task-59206213` | KEEP | advertised-on-origin |
| `origin/feat/card-task-594f207a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-59550e71` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-59594204` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-59909fb5` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-5a11f02c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5a2e33f1` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-5a4af2eb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5a8dada2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-5b8e6931` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5ba28b37` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5c29a0da` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5c599b40` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5cbbdf8f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5dbe617b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5dd894aa` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5e3ba4d4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=5 |
| `origin/feat/card-task-5e3ecbdd` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-5edeaddb` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-5ee2c9a3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5f187792` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5f6b3388` | KEEP | advertised-on-origin |
| `origin/feat/card-task-5fcba512` | KEEP | advertised-on-origin |
| `origin/feat/card-task-60161398` | KEEP | advertised-on-origin |
| `origin/feat/card-task-60519379` | KEEP | advertised-on-origin |
| `origin/feat/card-task-60616b1c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-606486b1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-608ed47c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-60b7a87d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-60eb81e0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6141b0b0` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=12 |
| `origin/feat/card-task-6159b95b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-61b36860` | KEEP | advertised-on-origin |
| `origin/feat/card-task-62fc2d31` | KEEP | advertised-on-origin |
| `origin/feat/card-task-63057c79` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-632da924` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-63b3b3ea` | KEEP | advertised-on-origin |
| `origin/feat/card-task-63d584d5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-643ebcc5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-646d8caf` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-64734e3f` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-647c2693` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=7 |
| `origin/feat/card-task-64a87ae9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-64b35564` | KEEP | advertised-on-origin |
| `origin/feat/card-task-64b6e881` | KEEP | advertised-on-origin |
| `origin/feat/card-task-64db976e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-65170902` | KEEP | advertised-on-origin |
| `origin/feat/card-task-65991c1e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-65d9f56e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-65ec6671` | KEEP | advertised-on-origin |
| `origin/feat/card-task-662b5d7a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6630ad4e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-67ddf317` | KEEP | advertised-on-origin |
| `origin/feat/card-task-67ee8f94` | KEEP | advertised-on-origin |
| `origin/feat/card-task-68138e50` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6871203f` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-687a7f27` | KEEP | advertised-on-origin |
| `origin/feat/card-task-68adb121` | KEEP | advertised-on-origin |
| `origin/feat/card-task-68ce5371` | KEEP | advertised-on-origin |
| `origin/feat/card-task-693fd5d6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-69514278` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-697ca82c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-698c0e44` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=7 |
| `origin/feat/card-task-6a536d8d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-6a59d61b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6a7c1d47` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-6a98e94f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6ab7ea2f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6b037196` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=5 |
| `origin/feat/card-task-6b457b41` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=7 |
| `origin/feat/card-task-6b692111` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6bc6293f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6bc6dd56` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6bdbf52e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6c0b3739` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6c123506` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-6c3e5034` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-6c6ca773` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6c8bdb64` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-6cb82403` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6cb9c0d9` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-6cdcb609` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6d2a2060` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-6d82654c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6d9d9275` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6d9e7db0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6da81ee9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6dfe4f27` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6e483022` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6eb56d67` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6ecce8bc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6f85f65f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6f9ca8b4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6fea402b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-6ff18828` | KEEP | advertised-on-origin |
| `origin/feat/card-task-700c3a06` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-70269020` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7043d3b7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=15 |
| `origin/feat/card-task-706a7582` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-708672f3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-708c6a6e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-70cdf499` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=14 |
| `origin/feat/card-task-71060b3f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-711458ee` | KEEP | advertised-on-origin |
| `origin/feat/card-task-716721c3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-719e9a27` | KEEP | advertised-on-origin |
| `origin/feat/card-task-71a5997b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-71b33378` | KEEP | advertised-on-origin |
| `origin/feat/card-task-72445eaa` | KEEP | advertised-on-origin |
| `origin/feat/card-task-72657ad5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-72b7c77e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-73423389` | KEEP | advertised-on-origin |
| `origin/feat/card-task-73486072` | KEEP | advertised-on-origin |
| `origin/feat/card-task-734d7f83` | KEEP | advertised-on-origin |
| `origin/feat/card-task-73b8b2ae` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-7404e327` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7492f59e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7494ba8a` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-74a9749b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-75089dd7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-75358b73` | KEEP | advertised-on-origin |
| `origin/feat/card-task-75491ae6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-75a8a4fb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-75abdd52` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-75cbfff8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-75df9046` | KEEP | advertised-on-origin |
| `origin/feat/card-task-760316da` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7647cb6a` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-76611588` | KEEP | advertised-on-origin |
| `origin/feat/card-task-76f2158d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-7716e5f0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-77232fc3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-774a1cc8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-77a6073c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-77bbad75` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-78b9203d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-78bf7628` | KEEP | advertised-on-origin |
| `origin/feat/card-task-78e0c0e5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-78ef07b4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-79011cb4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-79264973` | KEEP | advertised-on-origin |
| `origin/feat/card-task-79295e2c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-794265da` | KEEP | advertised-on-origin |
| `origin/feat/card-task-79bf1ba7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-79db6546` | KEEP | advertised-on-origin |
| `origin/feat/card-task-79ee81f1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7a33c7c8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7a479eaf` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=16 |
| `origin/feat/card-task-7ae56a95` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7b3d03a4` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-7c60794c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7c7fdeeb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7c98e07e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7caf792c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-7cc04bb4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7cc255f0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7cd0d0df` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7cdb752c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7ceae2ae` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7d285cb5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7d47772c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7d61d9fe` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7df1caab` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7e08d8c6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7e73872f` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=6 |
| `origin/feat/card-task-7e75fc38` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-7ebf8aa2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-7ee266d3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=12 |
| `origin/feat/card-task-7f1aaff2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7f356209` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7fa08907` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=5 |
| `origin/feat/card-task-7fd710f4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-7fd885e9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8013fd45` | KEEP | advertised-on-origin |
| `origin/feat/card-task-804aca36` | KEEP | advertised-on-origin |
| `origin/feat/card-task-80f79d82` | KEEP | advertised-on-origin |
| `origin/feat/card-task-818582a5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-81922318` | KEEP | advertised-on-origin |
| `origin/feat/card-task-81bc7f5d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-81c898bc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-82a076e4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-82acf2fd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-82ba618b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-82d40810` | KEEP | advertised-on-origin |
| `origin/feat/card-task-835dd574` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-83ce0cea` | KEEP | advertised-on-origin |
| `origin/feat/card-task-840ae3fa` | KEEP | advertised-on-origin |
| `origin/feat/card-task-84de0093` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-8500df23` | KEEP | advertised-on-origin |
| `origin/feat/card-task-851453eb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-859094b8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8609ed9b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-863ee584` | KEEP | advertised-on-origin |
| `origin/feat/card-task-86462e93` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8647113f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-86684fa2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-867c55df` | KEEP | advertised-on-origin |
| `origin/feat/card-task-86c3c949` | KEEP | advertised-on-origin |
| `origin/feat/card-task-86fa9c01` | KEEP | advertised-on-origin |
| `origin/feat/card-task-86fe6165` | KEEP | advertised-on-origin |
| `origin/feat/card-task-870ad53e` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-87609c55` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8798ecd3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-87da3a46` | KEEP | advertised-on-origin |
| `origin/feat/card-task-880ce135` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8845f4b0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-88485f49` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-88a6e4ef` | KEEP | advertised-on-origin |
| `origin/feat/card-task-896f3d65` | KEEP | advertised-on-origin |
| `origin/feat/card-task-89719e41` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-899e6f5a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-89af9e59` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8a0ddf38` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8a25ce6e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=13 |
| `origin/feat/card-task-8a662a98-integrated` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8a7132cb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8a7d4d16` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8a9a6d39` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8ac49e37` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-8b2ab56f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8b31ec87` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8b35c4ef` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8b460fc4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8b51f4e4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8b5bba6f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8b690c54` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8b6b42a9` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=6 |
| `origin/feat/card-task-8bb4bae8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=16 |
| `origin/feat/card-task-8bc1672b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-8cc5f317` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8d32f2c5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8d4cd03e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8d5aa4fc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8db64eb9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8e27c0a0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8e56eb15` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8eacb518` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8eacfa87` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8ec6b28e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8ec6d6ff` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-8eceacaf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8ed118ba` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8ee59620` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8eed758c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8f5d5036` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8f9997da` | KEEP | advertised-on-origin |
| `origin/feat/card-task-8fa6b6ce` | KEEP | advertised-on-origin |
| `origin/feat/card-task-90228735` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-903bf8a7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-90dc342a` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-90e8c43c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-90f3f838` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9179f089` | KEEP | advertised-on-origin |
| `origin/feat/card-task-92023e45` | KEEP | advertised-on-origin |
| `origin/feat/card-task-928f0ce3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-929f4620` | KEEP | advertised-on-origin |
| `origin/feat/card-task-92c4f810` | KEEP | advertised-on-origin |
| `origin/feat/card-task-93a3d93f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-93eea7de` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-94914cdf` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=18 |
| `origin/feat/card-task-957902d6` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-9579b548` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9617960b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-961ee768` | KEEP | advertised-on-origin |
| `origin/feat/card-task-963170ad` | KEEP | advertised-on-origin |
| `origin/feat/card-task-96ae83d8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-96d373ab` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9775fe45` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-97c00028` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-97e2c86f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-98260fdf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-982783af` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9828b7ff` | KEEP | advertised-on-origin |
| `origin/feat/card-task-98362da3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-987ccd9a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9888335c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-98c9aab2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-98d80cb8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-98f2ad4f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-98f352ee` | KEEP | advertised-on-origin |
| `origin/feat/card-task-99b691d8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-99efb48f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-99ff98dc` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-9a1c17e8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9ad681e2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-9b021c06` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9b28be80` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9bae45cf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9bb5cdee` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9bbc9566` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-9bcf4ea9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9bd89aac` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9c43f660` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9d394a6d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9dbe1877` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9de0189e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9e8d8472` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9e98878b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9ea91912` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-9ec344d4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9ed7da3b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-9ed93938` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9edf8224` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-9f4b5eae` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-9f69ac66` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-9f6a89ca` | KEEP | advertised-on-origin |
| `origin/feat/card-task-9fe76654` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-a062496e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a073b2d8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a08e6d8d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=16 |
| `origin/feat/card-task-a09a10b1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a0ea230a` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-a13bca47` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a1d37ec8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-a1db6174` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a1ee8274` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a24a626a` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-a2558b01` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a2601af5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a284cf76` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=13 |
| `origin/feat/card-task-a2b4c9ee` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a2eb29f3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a30c729b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a3265594` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a3c7520f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a402da69` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a41dc3ed` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a4290b71` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a5c4d4a7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a5c51cc2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a5cebf5d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a5e37919` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-a61c829b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a650f4b4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a6a7fba7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a79d4131` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a7b63d96` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a7c42072` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a7ce7cf4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-a7d678ce` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a7e02b29` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a83e4b62` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a87b9e00` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a8a78ab3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a8e5a1b9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a946111c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-a9848fbe` | KEEP | advertised-on-origin |
| `origin/feat/card-task-a9dc938e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-a9ff548b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-aa0904bc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-aa2efb99` | KEEP | advertised-on-origin |
| `origin/feat/card-task-aa4febb4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-aa7cf6db` | KEEP | advertised-on-origin |
| `origin/feat/card-task-aaab8f81` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-aab580ff` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-aae956f6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ab6c59b3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-abc66011` | KEEP | advertised-on-origin |
| `origin/feat/card-task-abc9f7d5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-abea11d8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ac940931` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ad020e8c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ad5581cc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ad910b87` | KEEP | advertised-on-origin |
| `origin/feat/card-task-adbad12b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-addeb569` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-ae40541f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ae805b71` | KEEP | advertised-on-origin |
| `origin/feat/card-task-aee6182e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-af2a57eb` | KEEP | advertised-on-origin |
| `origin/feat/card-task-af90420b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-aff1b9c6` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-b016a2d1` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-b01f2e7b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b03a74a0` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-b03e83b7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b070fab0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b09dfba4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b0c746ed` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b11d7d2d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b12e3f04` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b172e455` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-b1bb8fc5` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-b1ffd29e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-b227ba87` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b23741b2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-b23d0fe2` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=7 |
| `origin/feat/card-task-b249882f` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-b288ec96` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b29278e3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b2a94f8e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b2b9c307` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b2c70f5f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b32c3e95` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b33c60a0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b367debd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b3777342` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b3864165` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b3ff9b03` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b46ffcfa` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-b491b9c4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b49221c2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b4e6383d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-b5228641` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b5370eca` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b5603fc8` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=15 |
| `origin/feat/card-task-b5c4f994` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=6 |
| `origin/feat/card-task-b5c8e753` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b5dff6bc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b5fa256c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b6180921` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b61ac812` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b630f3bf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b6377f0c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b657a1e2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-b6884a21` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b71467ba` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b73275c8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b7c17822` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b8ab036f` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-b8f22709` | KEEP | advertised-on-origin |
| `origin/feat/card-task-b9ca189f` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-b9d3b16c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ba063df7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-ba318e38` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-ba33544f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bb32fef5` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-bb485c68` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-bb9da5d3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-bbc126e0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bbfbb7ac` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bc0176ff` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bc1b49f8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bc463e02` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bc8ae8b9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bc986d96` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bc9c67d3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bd02f8d9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bd36f4b7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bd63f38c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-be011a4c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-be035022` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-be710c61` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bed0cdb8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bed39f89` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=8 |
| `origin/feat/card-task-befec73c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-bf31273c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-bf96365b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=11 |
| `origin/feat/card-task-bfc79905` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-c0204cae` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c03af147` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-c0543045` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-c087dcaf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c0d955a5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c126dc89` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-c1280630` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c1305a37` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c163f2e6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c1f1f18e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c25c13c0` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-c34420cd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c37846cc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c389db24` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-c3e154a8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c43eeef4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c495f1d8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c5253cbc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c5973812` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c5d2ae3a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c6ab7ea4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c6fe67e8` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-c703a9f8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-c74bc66c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c7de934f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c7ff2da0-verification` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-c84abfd3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c8655038` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c8699754` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c86a05b3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c916f049` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-c9945af6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c99b5b44` | KEEP | advertised-on-origin |
| `origin/feat/card-task-c99e35fa` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ca3d4a6b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ca4eef9c` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=1 |
| `origin/feat/card-task-ca59249d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ca6c0df5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-cab6a109` | KEEP | advertised-on-origin |
| `origin/feat/card-task-cac8ee15` | KEEP | advertised-on-origin |
| `origin/feat/card-task-cb060b4e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-cb2632b3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-cc0dab3d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-cc5fd1a7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-cc63c869` | KEEP | advertised-on-origin |
| `origin/feat/card-task-cc67ba1a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ccabb6db` | KEEP | advertised-on-origin |
| `origin/feat/card-task-cd0b1657` | KEEP | advertised-on-origin |
| `origin/feat/card-task-cd1d39f7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-cdd8d229` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-ce048398` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-ce76a4e3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-cea6682c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-cebd6310` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-cec70c48` | KEEP | advertised-on-origin |
| `origin/feat/card-task-cecb5ee5` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-cef1a2a9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-cffd614e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d0356d7e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d0491157` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d0498628` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d09d94da` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d149ce14` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-d1505c60` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d170a7e9` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-d17e4301` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d1ab684b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d1c1630d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d1dbb697` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d20ed3ac` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d2117ffc` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d276cf4b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d2a0d121` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d2cd1c50` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d2cfaefd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d3319023` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d34a2249` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d39c5ba8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d3bc1416` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d3e917ce` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d417892b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d4195168` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d422c5a9` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d4447aac` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d47edbbf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d493e467` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d4df602a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d5234cdc` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-d534fa92` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d535eeec` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d579272f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d5b6713d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d65b42d1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d662028c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=10 |
| `origin/feat/card-task-d67b3e4e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d697ff85` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d6ff6ce4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=7 |
| `origin/feat/card-task-d7091f08` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d72a027c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-d73d91aa` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d75b52ff` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d77c21f4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d8001f76` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d800c0b4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d804025d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d81207e7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d8a7975c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d8a81266` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d8aebb25` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d944a661` | KEEP | advertised-on-origin |
| `origin/feat/card-task-d9b02a4c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d9b1f124` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-d9db97b9` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-d9f039a1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-da4026d2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=5 |
| `origin/feat/card-task-da49d22e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-da6e8e47` | KEEP | advertised-on-origin |
| `origin/feat/card-task-da7e1d0b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dae3ad6b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-db094bcc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-db1c9f98` | KEEP | advertised-on-origin |
| `origin/feat/card-task-db8122fd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dbb3ad22` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dbb940c9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dc343d8e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dcb476c9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dcc71cc0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dd036af2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dd3ada6f` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-dd3c58b5` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-dd9af3a7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dde7cd59` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-ddf1b667` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ddfe9f27` | KEEP | advertised-on-origin |
| `origin/feat/card-task-de5b6f9b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-de5c3888` | KEEP | advertised-on-origin |
| `origin/feat/card-task-de732954` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-deedf9dd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-df74a022` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dfc07c3a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dfc60293` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dfcc7e0d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dfd19fd2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dfea030e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-dff4e872` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e0b220e4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-e0fc3119` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-e116f6ae` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e137ad15` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e191838e` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-e1b6cb4a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e1c0cc33` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-e21405f0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e21bd32f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e283c453` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e296379b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e29ffab8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e2a0f2c9` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-e2b98a9a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e2b9bf48` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e2c04614` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e32907bf` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-e3316373` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=10 |
| `origin/feat/card-task-e41a7005` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e4b3d539` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e4c16286` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e4cfbe7e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e5cf17ba` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e5d0294c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-e5de4f68` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-e5ed1dbf` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e67555ad` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=13 |
| `origin/feat/card-task-e6f1295a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e6f3b241` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e739d9aa` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e772aac0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e78354e6` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-e7c20aaa` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-e839b406` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-e846c5d0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e86672a3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e873d3aa` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e9225bea` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e982d170` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e98a01c2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=11 |
| `origin/feat/card-task-e9d313a9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e9e898c2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-e9f20adc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ea257e64` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-ea3525d6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ea8f29db` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-ead491e1` | KEEP | advertised-on-origin |
| `origin/feat/card-task-eaf65f8b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-eb416191` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ebc14e49` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ec0d8cc3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-ec59b256` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ec5f895d` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-ec646a15` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-ecea46a3` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-ecf5a387` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ed1112b2` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ed41cbac` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ed5dcb00` | KEEP | advertised-on-origin |
| `origin/feat/card-task-edd44e28` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-ee18a226` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ee5c31b8` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ee823591` | KEEP | advertised-on-origin |
| `origin/feat/card-task-eea9b11b` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-eec32965` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ef013090` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ef329fb0` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ef4f4ec4` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ef7c4ee0` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-efcd5920` | KEEP | advertised-on-origin |
| `origin/feat/card-task-eff1371c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f0251152` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f04e4312` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=7 |
| `origin/feat/card-task-f065f946` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f09056f4` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-f0a7c7cd` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f0aad56e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f0b5d62a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f0e10c86` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f0ed11da` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f1139c44` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=6 |
| `origin/feat/card-task-f122b9de` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-f1713f40` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f18c8069` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f1f3b5a8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-f218db85` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f2306cc6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f2308a09` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f268c00e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f2788b47` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-f32006d5` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-f3498053` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f376ed36` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f450c05a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f450ecba` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-f4ac59a9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f4ef4af4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-f500932f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f5127656` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f52b79a3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f5408ebf` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-f5cb8590` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f5fe113e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f62de453` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f67bf133` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-f69e8b6e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f6ca2ac6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f6e2a0f6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f6ea65ba` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f727964e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f772ba73` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f7cf0fe7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=6 |
| `origin/feat/card-task-f8613557` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f9183542` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/feat/card-task-f92f723a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-f94c8fa4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-fa0190b4-rebased` | KEEP | advertised-on-origin; fa0190b4(rescue verified); unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-fa10e904` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fa16db12` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-fa23b50e` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fa3ed513` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fa627fa3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fa69124f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fa71cb77` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-fa74e03b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fb15a6fe` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-fb1a06fc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fb8aaac5` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fb8fa4a9` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fbb1b139` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fbe3745d` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fc39aaef` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fc539741` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fc62d370` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-fc7e26a7` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fc94cf6f` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-fcb8859a` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fcc274e3` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fcc6014b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fceb9bcc` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fcf948b8` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-fd423b29` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/feat/card-task-fdb14a5c` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fddbeaf6` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fddf3e2b` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fe24dc11` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fe37038c` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=3 |
| `origin/feat/card-task-fe74068f` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fea04260` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fea45f54` | KEEP | advertised-on-origin |
| `origin/feat/card-task-fedd9303` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=3 |
| `origin/feat/card-task-fede62ef` | KEEP | advertised-on-origin |
| `origin/feat/card-task-ff5ae210` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/feat/card-task-ffc43849` | KEEP | advertised-on-origin; unproven:merge=1,patch-NOT-on-master=1 |
| `origin/feat/card-task-ffc7e9ad` | KEEP | advertised-on-origin |
| `origin/fix/card-0443-18b18cb2` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/fix/card-0443-691f2941` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/fix/card-0443-d9-864691da` | KEEP | advertised-on-origin |
| `origin/fix/card-0478-rebase-f16f576c` | KEEP | advertised-on-origin |
| `origin/fix/grok-1.0.24-trust-prompt-settle` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=1 |
| `origin/merge/card-0497-b1258980` | KEEP | advertised-on-origin |
| `origin/mutation/card-0476-1ebe2253` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/mutation/card-0478-702f5ce7` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=5 |
| `origin/mutation/card-0478-c113100d` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=5 |
| `origin/preserve/card-0835-wip` | KEEP | advertised-on-origin |
| `origin/rescue/server2-wipe-c03af147` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=4 |
| `origin/rescue/server2-wipe-card-0679-r6-repair2-ad73820a` | KEEP | advertised-on-origin |
| `origin/rescue/server2-wipe-cc0fc72f` | KEEP | advertised-on-origin |
| `origin/rescue/server2-wipe-review-0593` | KEEP | advertised-on-origin |
| `origin/review/card-0665-final-28fc3bab` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
| `origin/throwaway/c590-credential-smoke-3537de6b7be4` | KEEP | advertised-on-origin; unproven:patch-NOT-on-master=2 |
