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

## Left in place

These are not Antiphon task checkouts. Each parent task is Succeeded. None of the fixture repos has an `origin`, except where noted, so the unpublished bytes cannot be pushed. They were not modified. A volume audit still refuses them.

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

`/work/repos/slides` `git ls-remote origin` fails under the audit environment (`could not read Username for 'https://github.com'`). Antiphon `ls-remote` succeeded in that same environment. Slides is only reached after the dirty-status pass, so it becomes the failure once the fixture repos above are no longer the first refusal.

`redeploy-old` was not run.

--- next stage ---
next: decide
handoff: The three settled Antiphon checkouts are published and clean. 778 `/work/worktrees/task-*` checkouts pass the audit mirror. Sixteen nested fixture repos have no origin and were left in place; they still fail status or index-content. `/work/repos/slides` `ls-remote` fails under the audit's empty global git config. Do not run `redeploy-old` until those refusals are retired or excluded.
artifact: docs/investigations/2026-10-16-server2-worktree-audit.md
