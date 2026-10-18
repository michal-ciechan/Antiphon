# server2 standing worktree task-71fd0d46 (2026-10-16)

Task 41ae0aac (Custom, Debug). `redeploy-old` preflight refused before removing any volume:

`RecycleWorktreeDirty audit check=index-content status=0 repo=worktrees/task-71fd0d46`

Evidence: `C:\src\Antiphon\.antiphon\rolling-server2\c727727675c15e1c\deploy-parent\c590-result.json` and `recycle.json` (`phase=preflight`, `outcome=refused`, `ownedRemoved=false`). Standing `antiphon-runner` was drained to `server2-temp`, idle, on build `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`. No rollout phase was run here.

## What was dirty

Container `antiphon-runner-session-runner-1` (`fc2983ea4991`), exec as uid 1654. Checkout `/work/worktrees/task-71fd0d46`.

| Fact | Value |
|---|---|
| Branch | `feat/card-task-71fd0d46` |
| HEAD | `d5e8e47983924e35b5d2aa3da1e1e73525f0a5f1` |
| `origin` tip (`git ls-remote`) | same SHA |
| Commits not in any `origin` remote-tracking ref | 0 |
| `git status --porcelain --untracked-files=all` | empty |
| `git diff` / `git diff --cached` | empty |
| Index flags | 7754 entries, all `H` |
| Index-content mismatches | 1 |

The mismatch was `docs/superpowers/evidence/2026-10-14-card-1254-code.md`. Index blob `8e23c6ceac8531c538f6453effe923550673b834` (43801 bytes). Working tree raw hash `b0ae89d2f5b2aa927e035f018c720985a0c09f35` (43802 bytes). `* text=auto` cleans that extra byte, so status stays empty while `hash-object --no-filters` does not match. The extra byte was one CR at offset 42256, in `blank_without_writing(<TAB><CR><LF>) | Passed`. The filtered hash already equalled the index blob.

## Task

`delegate.ps1 -Status 71fd0d46-2382-4ffd-b704-35a9896333d7`: Succeeded, CARD-1254 checkpoint tool leftovers, Worker/Code, server2. Land Completed. Approved and resolved source `d5e8e47983924e35b5d2aa3da1e1e73525f0a5f1`. Publication landed; verified remote `d748676ba88352f76ac65df09f78e1b71f356881`. Cleanup Complete. Branch `feat/card-task-71fd0d46`. Worktree `/work/worktrees/task-71fd0d46`.

Settled, and the only unpublished bytes were that one CR on top of a pushed blob. No rescue branch.

## Clear

Printed, then run as uid 1654 in `antiphon-runner-session-runner-1`:

`git -c safe.directory=* -C /work/worktrees/task-71fd0d46 cat-file blob HEAD:docs/superpowers/evidence/2026-10-14-card-1254-code.md > /work/worktrees/task-71fd0d46/docs/superpowers/evidence/2026-10-14-card-1254-code.md`

The write was the blob bytes (43801). HEAD stayed `d5e8e47983924e35b5d2aa3da1e1e73525f0a5f1`. No commit in the container.

The index stat cache still stored size 43802. With `GIT_OPTIONAL_LOCKS=0` (the audit's setting), `git status` then reported that path modified even though both hashes matched. After the racy second passed:

`git -c safe.directory=* -C /work/worktrees/task-71fd0d46 update-index --refresh -- docs/superpowers/evidence/2026-10-14-card-1254-code.md`

under the audit environment except with the index lock allowed. Exit 0. Index size became 43801. The oid did not change.

## Audit equivalent

Same environment as `consider_dirty` in `scripts/c590-remote.sh`: `GIT_OPTIONAL_LOCKS=0`, `GIT_NO_LAZY_FETCH=1`, `GIT_CONFIG_SYSTEM=/dev/null`, `GIT_CONFIG_GLOBAL=/dev/null`, `core.commitGraph=false`. Scoped to this worktree. Not a rollout phase, and not the full volume helper.

| Check | Result |
|---|---|
| `status --porcelain --untracked-files=all` | 0 bytes |
| `diff-files --raw` | 0 bytes |
| `ls-files -v` non-`H` | 0 |
| `hash-object --no-filters` vs index oid | 7754 files, 0 mismatches |
| Target file vs `HEAD` blob | identical |
| HEAD | `d5e8e47983924e35b5d2aa3da1e1e73525f0a5f1` |

`redeploy-old` can be retried by the caller. This pass does not prove every other repo on the volume is clean; the preflight stops at the first refusal, and that refusal was this worktree.
