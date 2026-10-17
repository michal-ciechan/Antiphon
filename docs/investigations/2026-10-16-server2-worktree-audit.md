# server2 standing worktree audit (2026-10-16)

Task 358cdeb1. `redeploy-old` had refused before removing any volume:

`RecycleWorktreeDirty audit check=status status=0 repo=worktrees/task-2d835afa`

Evidence: `C:\src\Antiphon\.antiphon\rolling-server2\c7278850b26e2f71`. No rollout phase was run. Reads and the three rescues below were `docker exec -u 1654:1654` in `antiphon-runner-session-runner-1`. The temp runner was not used.

## Checks

Same Git environment as `consider_dirty` in `scripts/c590-remote.sh`: `GIT_OPTIONAL_LOCKS=0`, `GIT_NO_LAZY_FETCH=1`, `GIT_TERMINAL_PROMPT=0`, `LC_ALL=C`, `GIT_CONFIG_SYSTEM=/dev/null`, `GIT_CONFIG_GLOBAL=/dev/null`, `GIT_NO_REPLACE_OBJECTS=1`, `core.commitGraph=false`, `gc.writeCommitGraph=false`.

For each checkout: `git status --porcelain --untracked-files=all`, `git ls-files -v` (any tag other than `H`), and index blob size against the worktree file. Size mismatches were hashed with `git hash-object --no-filters`. An absent index with no extra file and no stash is the audit's seed shape and is clean. Antiphon publication used `git ls-remote --heads origin` (1819 advertised heads, 1815 present commits, 13400 reachable commits).

Scope: 778 directories under `/work/worktrees`, both repos under `/work/repos`, other `.git` directories under `/work`, and registered worktrees, 878 targets. One fixture blocked `hash-object` because a tracked path is a fifo; it is listed below and was not hashed.

## Before

| Checkout | Task | Branch / HEAD | What the audit would refuse |
|---|---|---|---|
| `/work/worktrees/task-2d835afa` | 2d835afa, Canceled, CARD-1241 | `feat/card-task-2d835afa` `41fc37326f168da528428d02164d01cc85320d6d`, already an origin head | status: 15 staged paths. Index flags all `H`. Index-content mismatches 0. |
| `/work/worktrees/task-768488ed` | 768488ed, Canceled, CARD-1192 | `feat/card-task-768488ed` `a61db0f08448b87421b8dd9334f9f2631691cde7`, reachable from origin | status: unstaged `scripts/c590-remote.sh`, `scripts/fixtures/c1192-workspace-contract.sh`. Both also failed `hash-object --no-filters`. |
| `/work/review-evidence/6c3dfed9/probe` | 6c3dfed9, Succeeded, CARD-1205 | detached `742e3404c26df504e30727a6178f66076cb41485`, the task's reviewed SHA, reachable from origin | status: unstaged `tests/Antiphon.Tests/Application/WorkingTranscriptRecoveryTests.cs` (32 insertions, 1 deletion). Not a CR-only diff. |

Every other `/work/worktrees/task-*` checkout was clean on status, flags, and the size/hash check. No Antiphon task HEAD was unpublished. `task-71fd0d46` stayed clean. `REBASE_HEAD` for `task-610137ee` is `93166eec1a2eccfdc35e89e130760fbe3525ec95`, which is that checkout's HEAD and is reachable from origin. `AUTO_MERGE` files exist for several admins, including `task-2d835afa`; none of those admins has `MERGE_HEAD`. `/work/repos/antiphon` and `/work/repos/slides` have no index, contain only `.git`, and have no stash, so they match the audit seed.

Staged paths on `task-2d835afa`: `.antiphon/task-c4110a03.md`, `docs/superpowers/reports/2026-10-14-card-1241-code.md`, `docs/superpowers/reports/2026-10-14-card-1241-code-repair.md`, `docs/superpowers/reports/2026-10-14-card-1241-code-repair-2.md`, `docs/superpowers/reports/2026-10-14-card-1241-code-repair-3.md`, `docs/testing-and-build.md`, `scripts/lib/checkpoint-usage.ps1`, `tests/Antiphon.Tests/Checkpoints/CheckpointConfinementLedgerTests.cs`, and `tools/Antiphon.Checkpoints/` files `BuildConfinement.cs`, `Execution/IDriver.cs`, `Execution/ProcessDriver.cs`, `Execution/RunScheduler.cs`, `LedgerStat.cs`, `Manifest/CheckpointManifest.cs`, `Manifest/ManifestValidator.cs`.

## Rescue

Each rescue branch was created in that checkout, committed, and pushed with `git push origin HEAD:refs/heads/<branch>` only after `git ls-remote` showed the branch absent. The pushed tip matched the local tip. `git status --porcelain --untracked-files=all` was then empty, including under the audit environment. The rescue branch was left checked out.

| Rescue branch | Tip | Parent task HEAD | Commit |
|---|---|---|---|
| `rescue/2d835afa-20261016` | `81ba8eda3ecf0add444a101a510ab1ee06c79e13` | `41fc37326f168da528428d02164d01cc85320d6d` | 15 files, +1541/−51 |
| `rescue/768488ed-20261016` | `c59b27e164512c8b6347c78583293d895ee85c5e` | `a61db0f08448b87421b8dd9334f9f2631691cde7` | 2 files, +866/−55 |
| `rescue/6c3dfed9-20261016` | `dd2ead856a38f4ab323c7e568833d92284d2645c` | `742e3404c26df504e30727a6178f66076cb41485` | 1 file, +32/−1 |

## After

A second pass over all 778 `/work/worktrees/task-*` checkouts, plus a separate pass over the probe, was clean: status, flags, size, and hash. `task-2d835afa`, `task-768488ed`, and the probe are on the rescue branches above.

## Fixtures found before removal

These are not Antiphon task checkouts. Each parent task is Succeeded. None of the fixture repos has an `origin`. This table is the state before the preserve-and-remove pass. Every path in it was later archived and removed.

| Path | Parent task | Refusal |
|---|---|---|
| `/work/repos/antiphon/.git/antiphon/reviews/9f1c8a2e-60f1-4bd3-9c93-7c6fbeb3bedd/portable-count-abc458f6e18b4a4986151d91835fefaf` | 9f1c8a2e CARD-1241 | untracked `notes.txt`; no origin |
| `/work/review-evidence/261a43a2/scratch/reference` | 261a43a2 CARD-1199 | modified `Lib/bin-probe2/Lib.dll`; no origin |
| `/work/review-evidence/261a43a2/scratch/wrapper` | 261a43a2 | modified `App/bin-wrap/App.dll`; no origin |
| `/work/review-evidence/261a43a2/scratch/smoke` | 261a43a2 | `AM victim.txt`; no origin |
| `/work/review-evidence/45932800/scratch/imported-wrapper` | 45932800 CARD-1199 | modified `Lib/bin-probe/Lib.dll`; no origin |
| `/work/review-evidence/45932800/scratch/imported-reference` | 45932800 | modified `Lib/bin-probe/Lib.dll`; no origin |
| `/work/review-evidence/task-25ac8542/scratch/git-tool` | 25ac8542 CARD-1199 | staged `tracked`, untracked `hook-called` and `monitor.sh`; no origin |
| `/work/review-evidence/task-25ac8542/scratch/git-script` | 25ac8542 | same shape; no origin |
| `/work/review-evidence/task-25ac8542/scratch/safe-real` | 25ac8542 | staged `tracked`, untracked `descendant.pid` and `monitor.sh`; no origin |
| `/work/review-evidence/task-25ac8542/scratch/safe-real-delay` | 25ac8542 | same shape; no origin |
| `/work/review-evidence/task-25ac8542/scratch/safe-held` | 25ac8542 | `fatal: not a git repository`; no origin |
| `/work/review-evidence/task-25ac8542/scratch/safe-control` | 25ac8542 | `fatal: not a git repository`; no origin |
| `/work/review-evidence/task-3a8d77b0/fifo-enumeration` | 3a8d77b0 CARD-1199 | absent index but extra files; no origin |
| `/work/review-evidence/task-3a8d77b0/settlement-fifo-fixture` | 3a8d77b0 | tracked `user.txt` is a fifo, so index-content refuses it; no origin |
| `/work/worktrees/task-1aefbc9b/.antiphon/test-output/c1017/deb4f6ae58a04bee830b1c0f588bc466/canonical` | 1aefbc9b CARD-1032 | absent index, untracked `.gitignore`, `fixture-owner.txt`, `keep.txt`; no origin |
| `/work/worktrees/task-1aefbc9b/.antiphon/test-output/c1017/f637b0401c8b432d88a340ce480154f2/canonical` | 1aefbc9b | same shape; no origin |

`/work/repos/slides` `git ls-remote origin` fails under the audit environment (HTTPS username prompt). Under the runner's normal git config, every local slides ref and commit was already on origin: `LS_REMOTE=ok`, unpublished commits 0, reflog-only commits 0, stash none, notes 0. Antiphon `ls-remote` succeeded in the empty-config environment as well.

## Preserve and remove

Copies live on the desktop at `C:\src\Antiphon\.antiphon\server2-rescue-20261016\`. `manifest.tsv` has 84 data rows (`source`, `kind`, `archive`, `sha256`, `size`, `specials`, `verified`). Each archive's desktop sha256 matched the container sha256 before the source was removed. Removals were `docker exec -u 1654:1654`, one path at a time, after printing the path. Nothing outside those paths was deleted. `redeploy-old` was not run.

A repo with commits was stored with `git bundle --all`. A repo with no commits, or with files a bundle does not carry, was also stored as a tar of the directory including `.git`. Fifos were excluded from tar and named in `specials` (`p <name>`). Bundles were `git bundle verify`'d (`is okay`) before removal.

Round 1 removed the 16 fixture directories in the table above, plus `/work/worktrees/task-1d5f5588` and `/work/worktrees/task-73233c64` (slides worktrees) and `/work/repos/slides`. Slides archive: `slides.bundle` sha256 `43d91a1d62bd43b801e75dc90bcc6b7e147c1c15ae747146aebc906da084bc4d` size 578337.

`/work/repos/antiphon` stayed. It is still the seed shape (only `.git`, no index, no stash). Its normal-config publication check found one scratch branch and three reflog-only commits, so the repo was not deleted. Those objects were bundled, then only the scratch ref and the reflog lines that named the unpublished commits were removed. After that, unpublished commits and reflog-only commits were both 0. The removed reflog lines are `removed-reflog-lines.txt` in that same desktop folder (9506 bytes). Published tips were not rewritten: `feat/card-task-72f96b7b` `81fd3cab41d29b74aa37746221c51a32240bad23`, `feat/card-task-c604f0d3` `db9e8c7af9359a46cd93592fb7a2dfd64a1fd0be`, `feat/card-task-23285d2c` `0f6a38956bb649970ccf590579afe3552e181fe7`, `feat/card-task-610137ee` `93166eec1a2eccfdc35e89e130760fbe3525ec95`. Scratch bundle: `antiphon-scratch-rebase-610137ee.bundle` sha256 `83967f152f38386d0f211703c53a152d7c6e2e0733900bde0d9b66bfb45e7f80` size 118720703, tip `32cf9a016ff9bbb2831d79b9b1a82a2c119ca855`. Reflog bundle: `antiphon-reflog-orphans.bundle` sha256 `7037b4d894f7d768af8dc150ebca3bcc098e22abc729cd3bcade610f79bc8f63` size 112711148, commits `090bca6d18d241fccb17f14c6ae40a2179838e68`, `74ea599ea3838f3a17009f5bdd44f4c7637bf1d9`, `a9f2d0e3924b7c0138e68bd309dfdd9a8fe71d9c`.

Later rounds removed every other common directory the audit would open except antiphon (38 repos: no usable origin, or a sibling origin that disappears with the fixture), five broken `.git` pointer checkouts left behind by those clones, and the symlink fixtures below. Parent checkouts `task-3f0e5491`, `task-059e44d6`, `task-1aefbc9b`, and `task-610137ee` stayed porcelain-clean. The rescue branches above were not touched.

The empty-config mirror then passed content and antiphon publication and stopped on dangling symlinks whose `readlink -e` fails and whose owner is not a work tree (a link under `/.git/` or outside every checkout). Census found 11. Parent tasks are Succeeded: 157c4013 CARD-1241 review 2, 46f143ee CARD-1199 repair 13f review, 25ac8542 CARD-1199 repair 13i review. Archived, hash-checked, then removed:

| Source | Archive | sha256 | Size | Specials |
|---|---|---|---|---|
| `.../link-fixture-455d09c3dd154008901eea12932c3ec6` | `r4/link-fixture-157c4013.bundle` | `3ef43c3922d39af08620dd030e6e8040fe9afcd12e95ba1bda6e36c4a2cd9e7f` | 519 | none |
| same directory (1 commit, no remote) | `r4/link-fixture-157c4013.tar` | `050426e9e1dd6c0fd2aaf8fc2cfd56cc4f592b10748c5b014f57dd5018f5d170` | 122880 | none |
| `/work/review-evidence/task-46f143ee/file-types/dangling` | `r4/46f143ee-dangling.tar` | `2b49b3c15b64438f9cbc5a55e620d3c6dccc9082bde71651012db242ed1a5f32` | 71680 | none |
| `/work/review-evidence/task-25ac8542/scratch/files` | `r4/25ac8542-files.tar` | `ff4ea8bf05dab767ae4baf0b4c119cbad52bfe1b0a856ba8481294f00e89032e` | 10240 | `p fifo` |

The whole link-fixture directory and the whole `file-types/dangling` directory were removed. Review files beside them (`review.md`, `report.md`) stayed. For `scratch/files`, only the dangling symlink was removed. `ordinary`, the resolving `symlink`, the `fifo`, and `directory` stayed. A second census then scanned 3735 symlinks with 0 failures.

The other 80 manifest rows (rounds 1–3, slides, and the two antiphon bundles) are the same TSV. Their desktop sha256 values matched before removal.

## Audit mirror

The read-only body of `c1008_audit` (`scripts/c590-remote.sh`, from `set -euo pipefail` through `repositories=%s partial=%s`) was run inside the standing container as uid 1654. Environment matched the rollout audit, including empty system and global git config. `C1008_TMP_MOUNT=/tmp` was set because the standing container mounts `antiphon-runner_runner-tmp` at `/tmp`, and nine registered checkouts live there. Without that variable the mirror refuses `worktree-confine` even when those checkouts exist. No rollout helper was started.

Result on 2026-10-17: exit 0, `repositories=800 partial=800`, zero `Recycle` lines. Elapsed 5225 seconds. `partial=800` counts promisor checkouts; the body treats that as a count, and exit 0 is the pass. The rollout helper's own cap is `timeout 3000s`. This body needed 5225 seconds, so that cap would still turn a clean volume into `RecycleGitAuditUnknown` (status 124) until deploy raises it. This task did not run `redeploy-old` or any rollout phase.

--- next stage ---
next: deploy
handoff: The standing server2 runner volume passes the `c1008_audit` body: exit 0, `repositories=800 partial=800`, no Recycle line, with `C1008_TMP_MOUNT=/tmp` and the empty git config. Preserved archives and sha256 manifest are at `C:\src\Antiphon\.antiphon\server2-rescue-20261016\`. The helper wrapper still kills the body at 3000 seconds; this pass took 5225 seconds, so deploy must raise that cap before `redeploy-old` or the preflight will time out on a clean volume. This task did not run a rollout phase.
artifact: docs/investigations/2026-10-16-server2-worktree-audit.md
