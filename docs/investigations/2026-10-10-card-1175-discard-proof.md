# CARD-1175: server2-temp discard proof (2026-10-10)

Task 2ab1545f (Debug). The operator approved the discard list in
`docs/investigations/2026-10-10-card-1175-temp-volume-survey.md`.
This pass removed those paths from the temp work volume and re-ran the
survey's read-only c1008 git walk. `retire-temp` was not run.

The approved deletes succeeded. One refusal remains, so a following
`retire-temp` still stops.

## Mounts

Throwaway container, image `antiphon-server2/session-testing:8892b7b759d9`
(`sha256:a935d19329ba5afb2353c95ae4c7aa9fc8e54e4fb02f1b44b7394664296f1dce`),
`--user 1654:1654`. The delete container used `--network none`. The later
walk used the default bridge, the same way the survey did.

| Mount | Destination | RW |
|---|---|---|
| volume `antiphon-runner-temp_work` | `/work` | true for the delete; readonly for the walk |
| volume `antiphon-runner-temp_runner-tmp` | `/runner-tmp` | true for the delete; readonly for the walk |
| tmpfs (2 GiB, mode 1777) | `/tmp` | true |
| bind `/tmp/c1175-discard` | `/proof` | true, delete container only |

Host inspect showed those four mounts and network `none` before any delete.
`runner-tmp` was not written. No live volume (`antiphon-runner_work`,
`antiphon-runner_runner-tmp`, `antiphon-runner_runner-state`,
`antiphon-runner_dind-data`) was mounted. `antiphon-runner-session-runner-1`
was still `Up (healthy)` after the walk.

## 1. Untracked file removed

`/work/worktrees/task-70b27342`, branch `feat/card-task-70b27342`,
HEAD `5b713f6855ee417737ff7d7e47cb340d66e3d9b8`.

Porcelain before the delete, from a copy of the index so the volume index
was not rewritten:

```
?? docs/investigations/2026-10-06-card-1120-1122-inherited-delivery-reds.md
```

| Check | Value |
|---|---|
| `git hash-object` | `f3ecd3324a455302c465c737bf73a9cd7589eba1` |
| desktop `git rev-parse origin/master:<path>` | `f3ecd3324a455302c465c737bf73a9cd7589eba1` |
| sha256 of the file bytes | `85f2e80d1eb60706230823dfb044c611f8627995448fcc12928ab3aeb3994b4b` |
| size | 14067 bytes |
| `git clean -fdn -- <path>` | `Would remove docs/investigations/2026-10-06-card-1120-1122-inherited-delivery-reds.md` |

The same three checks were repeated in the delete container immediately
before `git clean -fd --` that one path. Afterward porcelain was empty and
HEAD was still `5b713f6855ee417737ff7d7e47cb340d66e3d9b8`.

Restore that file from any clone that has the blob:
`git show f3ecd3324a455302c465c737bf73a9cd7589eba1`.

## 2. Four fixture directories removed

Each path was a real directory, not a symlink, and `readlink -e` was
`/work/review-evidence/4a14585d/<name>` on `antiphon-runner-temp_work`.
Top-level names are the planted audit cases. `rm -rf` was the four parents
only, which also removes the newline path inside `audit-fixtures-XC0HLc`.

| Directory | Git dirs | Bytes | List lines | List sha256 |
|---|---:|---:|---:|---|
| `audit-fixtures-XC0HLc` | 59 | 10950916 | 4513 | `0fdc83cdc44cdde2dbad5fe35a882de0ad1b0421454dbfc5421fdbed8434a799` |
| `audit-fixtures-erAIzF` | 16 | 3134572 | 1241 | `447b305720b7565e6c5e6b413c94fd6eedb2ba21a49eb5f89a095c9831f6826e` |
| `audit-fixtures-2iTk1m` | 13 | 2394831 | 942 | `d233e2c59a60926a82e33c3b14e9690cb6f34e4a32e11389eb0d670fc0749216` |
| `audit-fixtures-j5fAVj` | 10 | 1885112 | 702 | `28c7376ad536d7d63f1ff5ccc8e1d65e5b0708654df3a08f2cf95ab91cf1d728` |

`XC0HLc` top level: `antiphon`, `blobless-seed`, `branch`, `clean-real-index`,
`detached`, `full-antiphon`, `full-reflog`, `full-stash`, `full-worktree-ref`,
`linked-head`, `lock`, `missing-ancestor-commit`, `missing-ancestor-tree`,
`missing-head-tree`, `origin-unavailable`, `other-branch`, `path-credential`,
`path-newline`, `path-quotes-space-unicode`, `real-checkout-erased-index`,
`reflog`, `second-remote`, `seed-modified-tracked`, `seed-real-index`,
`seed-staged-deletion`, `seed-untracked`, `stash`, `tag`, `worktree-ref`.

`erAIzF` top level: `assume-unchanged-modified`, `bare-detached-unpublished`,
`bare-missing-head-tree`, `full-checkout-missing-ancestor-commit`,
`full-checkout-missing-ancestor-tree`, `full-skip-worktree-modified`,
`secondary-remote-only`, `skip-worktree-modified`.

`2iTk1m` top level: `environment-check`, `full-missing-ancestor-commit`,
`full-missing-ancestor-tree`, `injected-lsremote-failure`,
`missing-head-commit`, `test-fixture-shape`.

`j5fAVj` top level: `full-linked-private-ref`, `partial-linked-private-ref`,
`real-bare-detached-tip`, `real-bare-missing-head-tree`.

The per-file lists record `F <sha256> <bytes> <path>`, `D <path>`, `L`, or `O`.
They are not a content archive, so they do not restore the trees.

Left in place, checked after the delete:

- `/work/review-evidence/4a14585d/resume-fixture-fscdYJ`
- `/work/review-evidence/df0d419a`
- `/work/review-evidence/52d6dd7f`
- `/work/review-evidence/a9d35067`
- `/work/review-evidence/ca393fdc`
- `/work/worktrees/task-4a14585d`
- `/work/worktrees/task-70b27342`
- `/work/repos/antiphon`

`docker volume rm` was not run. `worktrees/task-4a14585d` was not deleted.

### Host proof

Written before the delete, then copied to stable `/tmp` names.

| Path | sha256 |
|---|---|
| `/tmp/c1175-discard/before.txt` and `/tmp/c1175-discard-before.txt` | `1b724ec4730e1579df45a6adb8da5be4124600a18ce1d63a4cd1c6f12c668241` |
| `/tmp/c1175-discard/list-audit-fixtures-XC0HLc.txt` | `0fdc83cdc44cdde2dbad5fe35a882de0ad1b0421454dbfc5421fdbed8434a799` |
| `/tmp/c1175-discard/list-audit-fixtures-erAIzF.txt` | `447b305720b7565e6c5e6b413c94fd6eedb2ba21a49eb5f89a095c9831f6826e` |
| `/tmp/c1175-discard/list-audit-fixtures-2iTk1m.txt` | `d233e2c59a60926a82e33c3b14e9690cb6f34e4a32e11389eb0d670fc0749216` |
| `/tmp/c1175-discard/list-audit-fixtures-j5fAVj.txt` | `28c7376ad536d7d63f1ff5ccc8e1d65e5b0708654df3a08f2cf95ab91cf1d728` |
| `/tmp/c1175-discard/after.txt` and `/tmp/c1175-discard-after.txt` | `c295ad4c9c5dee0f3678877031ca96b2aa41d88968dc7c8eb8fad2c67eb73675` |
| `/tmp/c1175-discard-audit.out` (3007 lines) | `29c024a6d569645c5086da2156d878963d61c5d0c05b3e2fe2f5451e02f6af7b` |
| `/tmp/c1175-replay.sh` (survey script, unchanged) | `e77e035ffad16fcc7fceae3e57d2562a6922d5a29cfa4f6b22e58a40800e8361` |

Delete finished at 2026-10-10T10:00:30Z.

## 3. Read-only walk after the delete

Same `/tmp/c1175-replay.sh`, readonly temp volumes, `--user 1654:1654`,
`C1008_TMP_MOUNT=/runner-tmp`, private tmpfs `/tmp`. The script printed
`WORK_MOUNT_RO`, `TMP_MOUNT_RO`, `SCRATCH_TMPFS`, and `WRITE_PROBE_FAILED`.
Window 2026-10-10T10:00:54Z to 2026-10-10T10:25:01Z. Container exit 0.

| Item | Survey | After this discard |
|---|---:|---:|
| `repositories=` | 581 | 434 |
| `partial=` | 470 | 431 |
| `ENTRY-META` | 580 | 434 |
| `RecycleWorktreeDirty` | 10 | 0 |
| `RecycleUnpublishedWork` | 18 | 0 |
| `RecycleGitAuditUnknown` | 112 | 1 |
| `tip=` lines | 2124 | 2106 |
| `TIP-UNPUBLISHED` | 0 | 0 |

The antiphon common `365f0b8c…` still published 1579 tips. The two
`ca393fdc` commons still published 263 and 264. `TIP-CHECK-FAILED` is the
survey replay's extra tip phase, the same line the survey recorded. It is
not an audit refusal. There is no `TIP-UNPUBLISHED` line.

### The refusal that remains

```
audit check=ls-remote status=128 repo=review-evidence/4a14585d/resume-fixture-fscdYJ/work/repo
RecycleGitAuditUnknown
```

A later read-only probe left that tree in place. Its origin is

`file:///work/review-evidence/4a14585d/audit-fixtures-XC0HLc/full-stash/origin.git`

(sha256 of that URL string `9997f84eff367ef3af0fef68b6bc091ba9765cfb13798540857b8d1fc1466cde`).
That directory was inside the approved `audit-fixtures-XC0HLc` removal and is
now absent. `git ls-remote` exits 128:

```
fatal: '/work/review-evidence/4a14585d/audit-fixtures-XC0HLc/full-stash/origin.git' does not appear to be a git repository
fatal: Could not read from remote repository.
```

The survey counted `resume-fixture-fscdYJ` as passed while that origin still
existed. This pass did not delete `resume-fixture-fscdYJ`. The kept
CARD-1149 and CARD-1153 review-evidence repositories are still in the
`ENTRY-META` lines and produced no refusal.

`retire-temp` stops at the first refusal. It will stop on this
`RecycleGitAuditUnknown` until `resume-fixture-fscdYJ` is dropped or that
origin repository is restored. This pass did neither. The hash lists cannot
rebuild `origin.git`.

## Next stage

`next: decide`. The approved discard is complete. Choose whether to also
drop `resume-fixture-fscdYJ` before `retire-temp`, or keep it and accept
that `retire-temp` still refuses.
