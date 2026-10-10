# CARD-1175: server2-temp volume recycle survey (2026-10-10)

Task fd1f0c37 (Debug, read-only). `retire-temp` for
`pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 8892b7b759d96b3b5d6fa20518caf2c79c6be81a -Phase retire-temp`
stopped at the first refusal, `RecycleWorktreeDirty` on `worktrees/task-70b27342`.
This replay runs the same audit over every entry on the temp volume and records every refusal.

The temp volume still refuses. One task worktree is dirty, and that dirt is discardable.
139 further refusals are synthetic git repositories planted under
`review-evidence/4a14585d/audit-fixtures-*`. Removing the one untracked file and then
re-running `retire-temp` would stop on those fixtures. No Antiphon tip on this volume is
unpublished. The rescue-push list is empty.

## Method (read-only)

- Volumes `antiphon-runner-temp_work` at `/work` and `antiphon-runner-temp_runner-tmp` at
  `/runner-tmp`, both `readonly`, in throwaway `docker run --rm --name c1175-replay
  --user 1654:1654`. Private tmpfs `/tmp` (2 GiB, mode 1777). `C1008_TMP_MOUNT=/runner-tmp`.
  Default bridge network, so the publication fetch could run. The container is gone.
- Image `antiphon-server2/session-testing:8892b7b759d9`
  (`sha256:a935d19329ba5afb2353c95ae4c7aa9fc8e54e4fb02f1b44b7394664296f1dce`). Git 2.47.3.
  The process was uid 1654.
- Before any Git command the replay printed `WORK_MOUNT_RO`, `TMP_MOUNT_RO`, `SCRATCH_TMPFS`,
  and `WRITE_PROBE_FAILED` with `touch: cannot touch '/work/c1175-write-probe': Read-only file system`.
  Mountinfo was `/work ro,relatime`, `/runner-tmp ro,relatime`, `/tmp rw,nosuid,nodev,noexec,relatime`.
  A later `--network none` confirm container failed the same way on `touch /work/c1175-confirm-probe`.
- Program: `c1008_git_program` from `scripts/c590-remote.sh` at `8239e7d1077d7c376e8dfa30d6e0f1fa1496fdb5`.
  Same env (`GIT_OPTIONAL_LOCKS=0`, `GIT_NO_LAZY_FETCH=1`, `GIT_CONFIG_SYSTEM=/dev/null`,
  `GIT_CONFIG_GLOBAL=/dev/null`, `core.commitGraph=false`) and the same checks. Reporting-only
  changes record a refusal and continue (per entry, per common directory, per symlink). A dirty
  checkout prints status codes and paths, never file contents.
- No fetch, gc, prune, checkout, reset, clean, or stash ran against the volume. No ref was
  pushed. No worktree, container, or volume was removed, and no deploy phase ran.
- Replay window: 2026-10-10T08:00:28Z to 2026-10-10T08:19:02Z (18 min 34 s).
  Host proof: `/tmp/c1175-audit.out` (3790 lines, sha256
  `8973f19bf9e4361317b30ddecc6ce137b19dc042e3b1faa853bfc449ef509a47`) and
  `/tmp/c1175-replay.sh` (sha256 `e77e035ffad16fcc7fceae3e57d2562a6922d5a29cfa4f6b22e58a40800e8361`).
- Owning tasks came from `GET /api/agent-tasks/<id>` and `card.ps1 get`. Desktop
  `git fetch origin master` left `origin/master` at `7f19748f13acac6331f7015b0737549e63b76dfc`.
  "Tip on origin" means an advertised origin head contains the commit. The audit's own proof is
  the scratch fetch of `refs/heads/*` into `refs/c1008/origin/*`.

`TIP-CHECK-FAILED` is a line from an extra replay phase. That phase pointed `rev-list` at the
last leftover `proof-*` directory, which belongs to a fixture. The audit's publication result
is the `tip=` lines below. There is no `TIP-UNPUBLISHED` line.

## Aggregate

| Item | Count |
|---|---|
| Repository counter (`repositories=`) | 581 |
| `ENTRY-META` lines | 580: 425 `worktrees/task-*`, 1 `repos/antiphon`, 154 `review-evidence/` |
| Counted then refused before `ENTRY-META` | 1: `audit-fixtures-XC0HLc/lock` (`lock-index.lock`) |
| Refused before the counter | 1: `audit-fixtures-XC0HLc/path-newline` (`git-common-dir`, path contains a newline, printed as `repo=?`) |
| Entries marked partial | 470 |
| Common directories | 147 (21 passed, 126 refused) |
| Published tips (`tip=` lines) | 2124, across the 21 commons that passed |
| Antiphon common `365f0b8c…` | 1579 published tips, no refusal |
| Content refusals on `worktrees/` or `repos/` | 1 |
| `RecycleWorktreeDirty` | 10 (1 task worktree, 9 fixture checkouts) |
| `RecycleUnpublishedWork` | 18 (all fixtures) |
| `RecycleGitAuditUnknown` | 112 (all fixtures) |
| Final line | `repositories=581 partial=470` |

`repos/antiphon` is `master` at `d985af05b5ace2f13ec3f0d886c15992e9c13c3f`, an ancestor of
`origin/master`. That is the temp rollback SHA named in the CARD-1105 close note. Its common
directory passed publication. 424 of the 425 task worktrees passed the content audit, and their
HEADs are among those 1579 published tips.

Eight other `review-evidence/` git directories were entered and passed. They stay:

| Path | Task | Card | Status |
|---|---|---|---|
| `review-evidence/4a14585d/resume-fixture-fscdYJ/work/repo` | `4a14585d` | CARD-1105 | Succeeded |
| `review-evidence/df0d419a/{baseline,candidate}` | `df0d419a` | CARD-1149 | Succeeded |
| `review-evidence/52d6dd7f/{baseline,candidate}` | `52d6dd7f` | CARD-1149 | Succeeded |
| `review-evidence/a9d35067/source` | `a9d35067` | CARD-1153 | Succeeded |
| `review-evidence/ca393fdc/scratch/{source,base}` | `ca393fdc` | CARD-1153 | Succeeded |

The two `ca393fdc` commons account for 263 and 264 of the published `tip=` lines. Both passed.

The `review-evidence/` name inside a common git directory is product state and is skipped there.
These paths are separate repositories on the work volume, so the volume walk audits them.

## 1. RecycleWorktreeDirty: `worktrees/task-70b27342` (discardable)

| Field | Value |
|---|---|
| Task | `70b27342-1aa4-4be7-976f-03d471d5a228`, Investigate, Succeeded 2026-10-06T21:51:52Z |
| Card | CARD-1120, Done |
| Branch | `feat/card-task-70b27342` |
| HEAD | `5b713f6855ee417737ff7d7e47cb340d66e3d9b8` |
| Tip on origin | Yes. Ancestor of `origin/master`, and `git ls-remote` advertises `refs/heads/feat/card-task-70b27342` at this sha |
| Uncommitted paths | 1: `?? docs/investigations/2026-10-06-card-1120-1122-inherited-delivery-reds.md` |
| Verdict | discardable |

A read-only `--network none` confirm container printed that same porcelain line and
`git hash-object` `f3ecd3324a455302c465c737bf73a9cd7589eba1`. Desktop
`git rev-parse origin/master:docs/investigations/2026-10-06-card-1120-1122-inherited-delivery-reds.md`
is the same blob. The path landed in `9463e1fba9a086b152df7517cb892f5f6c7e3fd2`
(`docs(CARD-1120): rescue the inherited delivery-reds investigation`, 2026-10-06 23:24:11 +0100),
which is an ancestor of `origin/master`. The uncommitted file is a byte-identical copy of a file
already on `origin/master`.

## 2. Fixture refusals under `review-evidence/4a14585d` (discardable, and they block recycle)

Owner: task `4a14585d-0c16-4692-80a3-8fb379573196`, "CARD-1105 audit fix Final Review", Review,
Succeeded 2026-10-07T18:57:23Z. Card CARD-1105 is Done. The task worktree
`worktrees/task-4a14585d` is branch `feat/card-task-4a14585d`, HEAD
`4bfc7379df6465df75481ac5a0f8069d435f3755`. That sha is the advertised
`refs/heads/feat/card-task-4a14585d`. It is also one of the 1579 published antiphon tips.
The worktree itself produced no refusal. The refusals are nested repositories the review left
under `/work/review-evidence/4a14585d/`.

| Directory | Entries | Role |
|---|---|---|
| `audit-fixtures-XC0HLc` | 87 | planted refusal shapes (dirty seeds, missing objects, locks, unpublished tips, `ls-remote` failures) |
| `audit-fixtures-erAIzF` | 24 | index-flag seeds and unpublished or bare shapes |
| `audit-fixtures-2iTk1m` | 19 | missing-ancestor, injected `ls-remote` failure, origin-unavailable |
| `audit-fixtures-j5fAVj` | 16 | bare and linked-private-ref shapes |
| `resume-fixture-fscdYJ` | 1 | passed; leave it |

### RecycleWorktreeDirty (9 fixture checkouts)

| Checkout | Nature |
|---|---|
| `audit-fixtures-XC0HLc/seed-staged-deletion/work/repo` | 2 paths, `D ` and ` D`, names `file` and `other` |
| `audit-fixtures-XC0HLc/seed-modified-tracked/work/repo` | 3 paths, two `D ` and one `??`, names `file` and `other` |
| `audit-fixtures-XC0HLc/seed-real-index/work/repo` | 2 staged `D` paths, names `file` and `other` |
| `audit-fixtures-XC0HLc/seed-untracked/work/repo` | 3 paths, two `D ` and one `?? untracked` |
| `audit-fixtures-XC0HLc/path-quotes-space-unicode/work/quote" space-π` | 3 paths, two `D ` and one `?? new` |
| `audit-fixtures-XC0HLc/REDACTED/work/REDACTED` | 3 paths, two `D ` and one `?? file` |
| `audit-fixtures-erAIzF/skip-worktree-modified/work/repo` | `index-flags` |
| `audit-fixtures-erAIzF/assume-unchanged-modified/work/repo` | `index-flags` |
| `audit-fixtures-erAIzF/full-skip-worktree-modified/work/repo` | `index-flags` |

The names are the fixture's own (`file`, `other`, `untracked`, `new`). They are seeds for the
recycle audit, not Antiphon source.

### RecycleUnpublishedWork (18, all `rev-list`)

| Fixture set | Count |
|---|---|
| `audit-fixtures-XC0HLc` | 14 |
| `audit-fixtures-j5fAVj` | 3 |
| `audit-fixtures-erAIzF` | 1 |

Case names include `second-remote`, `tag`, `antiphon`, `linked-head`, `branch`, `worktree-ref`,
`detached`, `full-stash`, `other-branch`, `reflog`, `stash`, `partial-linked-private-ref`,
`real-bare-detached-tip`, `full-linked-private-ref`, `secondary-remote-only`, and
`bare-detached-unpublished`. Each is a planted unpublished tip inside that fixture.

### RecycleGitAuditUnknown (112)

| Check | Count | Where |
|---|---|---|
| `ls-remote` | 97 | XC0HLc 60, erAIzF 16, 2iTk1m 13, j5fAVj 8 |
| `rev-list-objects` | 8 | XC0HLc 3, 2iTk1m 2, erAIzF 2, j5fAVj 1 |
| `status` | 2 | XC0HLc 1, 2iTk1m 1 |
| `git-common-dir` | 1 | `XC0HLc/path-newline/work/new<newline>line/.git` (`repo=?`) |
| `lock-index.lock` | 1 | `XC0HLc/lock/work/repo/.git` |
| `origin-present` | 1 | 2iTk1m |
| `bare-index` | 1 | erAIzF |
| `core-bare` | 1 | erAIzF |

Case names include `missing-ancestor`, `full-missing-ancestor-tree`, `full-missing-ancestor-commit`,
`injected-lsremote-failure`, `missing-head-commit`, `missing-head-tree`, `origin-unavailable`,
`real-bare-missing-head-tree`, and `bare-missing-head-tree`. These are the audit's own negative
fixtures. Verdict for every row in this section: discardable.

## Rescue-push list

Empty. `repos/antiphon` published all 1579 retained tips. The only uncommitted task-worktree path
is already the blob on `origin/master`. A rescue branch would republish that same blob.

## Discard list (proposed, not run)

Run these only after operator approval. Both containers use `--network none` and do not push.
Leave `resume-fixture-fscdYJ` and the CARD-1149 / CARD-1153 review-evidence directories in place.
Do not `docker volume rm`. Do not delete `worktrees/task-4a14585d`.

1. Drop the one untracked file on `task-70b27342`. Dry run, then apply:

```
docker run --rm --network none --user 1654:1654 \
  --mount type=volume,source=antiphon-runner-temp_work,target=/work \
  --entrypoint git \
  antiphon-server2/session-testing:8892b7b759d9 \
  -C /work/worktrees/task-70b27342 \
  clean -fdn -- docs/investigations/2026-10-06-card-1120-1122-inherited-delivery-reds.md
```

```
docker run --rm --network none --user 1654:1654 \
  --mount type=volume,source=antiphon-runner-temp_work,target=/work \
  --entrypoint git \
  antiphon-server2/session-testing:8892b7b759d9 \
  -C /work/worktrees/task-70b27342 \
  clean -fd -- docs/investigations/2026-10-06-card-1120-1122-inherited-delivery-reds.md
```

The dry run must name only that path. The mount is read-write; that is the discard.

2. Remove the four fixture directories. The newline path is inside `audit-fixtures-XC0HLc`, so
   the removal is the parent directory:

```
docker run --rm --network none --user 1654:1654 \
  --mount type=volume,source=antiphon-runner-temp_work,target=/work \
  --entrypoint /bin/bash \
  antiphon-server2/session-testing:8892b7b759d9 \
  -c 'rm -rf -- /work/review-evidence/4a14585d/audit-fixtures-XC0HLc /work/review-evidence/4a14585d/audit-fixtures-erAIzF /work/review-evidence/4a14585d/audit-fixtures-2iTk1m /work/review-evidence/4a14585d/audit-fixtures-j5fAVj'
```

After both, from the canonical checkout `C:\src\Antiphon` (a linked worktree refuses the deploy script):

```
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 8892b7b759d96b3b5d6fa20518caf2c79c6be81a -Phase retire-temp
```

## Next stage

`next: decide`. The operator approves the discard list above, or keeps the fixtures and accepts
that `retire-temp` keeps refusing. Nothing here is unpublished Antiphon work.
