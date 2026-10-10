# CARD-1175: resume-fixture discard proof (2026-10-10)

Task 123c10d9 (Debug). The operator approved dropping
`review-evidence/4a14585d/resume-fixture-fscdYJ` after the discard in
`docs/investigations/2026-10-10-card-1175-discard-proof.md` left one
`RecycleGitAuditUnknown`. That refusal was `ls-remote` status 128 because
the fixture origin
`file:///work/review-evidence/4a14585d/audit-fixtures-XC0HLc/full-stash/origin.git`
had already been removed with the approved audit fixtures. This pass removed
only `resume-fixture-fscdYJ` from `antiphon-runner-temp_work`. `retire-temp`
remains for the orchestrator.

## Verdict

Planted CARD-1105 resume probe, not unpublished Antiphon work. Safe to drop.

| Check | Result |
|---|---|
| Path | Real directory `/work/review-evidence/4a14585d/resume-fixture-fscdYJ` (`readlink -e` matches; not a symlink) |
| Owner task | Review `4a14585d` (CARD-1105), beside `resume-probe.cjs`, `resume-results.json`, `base.sh`, `reviewed.sh` |
| Git dir | One: `work/repo/.git` |
| Branch / HEAD | `master` `3286cf323cbc1802e4785c93bed1b8d1031f9fd4` |
| Parent | `b8fe04d4e62d33f4033ba99e455285ba10603ed5` |
| Commits | 2. Subjects `A` and `B`. Reflog is only the clone |
| Status | Empty porcelain. No stash. Not shallow |
| Origin | The deleted `full-stash/origin.git` above. `git ls-remote origin` exit 128 |
| In `/work/repos/antiphon` | Neither commit |
| Work tree | `file` is `B\n` (git blob `223b7836fb19fdf64ba2d3cd6173c6a283141f78`). `other` is `second\n` (git blob `e019be006cf33489e2d0177a3837a2384eddebc5`) |
| Size | 38 files, 20 directories, 0 symlinks, 39424 bytes |

Commit `A` adds `file` (`A\n`) and `other` (`second\n`). Commit `B` changes `file` to `B\n`. The JSON receipts beside the repo are synthetic (`image` `sha256:aaa…`, `volumes.antiphon-runner_work.outcome=pending`). `base.sh` and `reviewed.sh` are `C1008_RESUME=1` stubs with a fake `docker()`. Nothing in the tree is an Antiphon tip.

## Mounts

Throwaway container `c1175-discard-replay`, removed after the delete. Image
`antiphon-server2/session-testing:8892b7b759d9`
(`sha256:a935d19329ba5afb2353c95ae4c7aa9fc8e54e4fb02f1b44b7394664296f1dce`).
`--user 1654:1654`, `--network none`.

| Mount | Destination | RW |
|---|---|---|
| volume `antiphon-runner-temp_work` | `/work` | true for the delete; readonly for the inspect |
| bind `/tmp/c1175-resume` | `/proof` | true |

No live volume was mounted (`antiphon-runner_work`, `antiphon-runner_runner-tmp`,
`antiphon-runner_runner-state`, `antiphon-runner_dind-data`). Inspect printed
`WORK_MOUNT_RO` and `touch` failed with `Read-only file system` before the
delete container was created. The delete container's host inspect showed
`/work` writable on `antiphon-runner-temp_work` only. `docker ps --filter
volume=antiphon-runner-temp_work` was empty before the delete.
`antiphon-runner-session-runner-1` was still `Up (healthy)` afterward.

The delete re-checked HEAD, origin URL, commit count 2, empty porcelain,
`ls-remote` exit 128, and the list sha256, and refused to remove the tree
if any of those differed.

## Proof on server2

The list is `F <sha256> <bytes> <path>`, `D <path>`, `L`, or `O`, relative
to `resume-fixture-fscdYJ`. It is not a content archive. The two work-tree
blobs above are restorable from this note. The pack and the probe scripts
are hashed only.

| Path | sha256 |
|---|---|
| `/tmp/c1175-resume-list.txt` and `/tmp/c1175-resume/list-resume-fixture-fscdYJ.txt` | `6596d018a301c8dabb24d731050915573fe40f278f3c6aea974cf3fcd76e4d84` |
| `/tmp/c1175-resume-before.txt` and `/tmp/c1175-resume/before.txt` | `b0f219faea60b2fafee26652cb97f8cc41fab558b5bcb9041e77f3e4fda2418e` |
| `/tmp/c1175-resume-after.txt` and `/tmp/c1175-resume/after.txt` | `678bbb88827a4b7339bf430de1d8c8f60ff5023de358646857e7fad86d7de794` |
| `/tmp/c1175-resume-inspect.out` | inspect log, 2026-10-10T10:38:42Z |
| `/tmp/c1175-resume-peek.out` | patches and the six JSON receipts |
| `/tmp/c1175-resume-delete.out` | delete log, 2026-10-10T10:41:02Z |

```
D .
F 74876545a61de95aebd82f5a3198056351549d6ad0ae9aebb72894e23184ca78 365 base-failed.json
F 74876545a61de95aebd82f5a3198056351549d6ad0ae9aebb72894e23184ca78 365 base-initial.json
F 74876545a61de95aebd82f5a3198056351549d6ad0ae9aebb72894e23184ca78 365 base-recovered.json
F 4bde06830af63571dd66262483ae0d58fbe3ccd3204404e2e0793c4656df4fbc 3814 base.sh
F 801cdd0a6e322ded6b4151bb221c2c0be7e820a2c525ca1621dceaa38c40de63 221 reviewed-failed.json
F 9b87237202367a14e7df59b2026b2f761d1e819d416ae08fb406692f16d6a46c 391 reviewed-initial.json
F 801cdd0a6e322ded6b4151bb221c2c0be7e820a2c525ca1621dceaa38c40de63 221 reviewed-recovered.json
F 168d856a473604017ae933ff7f7cf37629a6e6d558ba0aaae405fa76d5583d04 4310 reviewed.sh
D work
D work/repo
D work/repo/.git
F f6f2b945f6c411b02ba3da9c7ace88dcf71b6af65ba2e0d89aa82900042b5a10 23 work/repo/.git/HEAD
D work/repo/.git/branches
F e60de8f643ee0fb14208ff0cd8f5790e6c47320163e65701e50aebd0b746f9da 306 work/repo/.git/config
F 85ab6c163d43a17ea9cf7788308bca1466f1b0a8d1cc92e26e9bf63da4062aee 73 work/repo/.git/description
D work/repo/.git/hooks
F 0223497a0b8b033aa58a3a521b8629869386cf7ab0e2f101963d328aa62193f7 478 work/repo/.git/hooks/applypatch-msg.sample
F 1f74d5e9292979b573ebd59741d46cb93ff391acdd083d340b94370753d92437 896 work/repo/.git/hooks/commit-msg.sample
F e0549964e93897b519bd8e333c037e51fff0f88ba13e086a331592bf801fa1d0 4726 work/repo/.git/hooks/fsmonitor-watchman.sample
F 81765af2daef323061dcbc5e61fc16481cb74b3bac9ad8a174b186523586f6c5 189 work/repo/.git/hooks/post-update.sample
F e15c5b469ea3e0a695bea6f2c82bcf8e62821074939ddd85b77e0007ff165475 424 work/repo/.git/hooks/pre-applypatch.sample
F 57185b7b9f05239d7ab52db045f5b89eb31348d7b2177eab214f5eb872e1971b 1649 work/repo/.git/hooks/pre-commit.sample
F d3825a70337940ebbd0a5c072984e13245920cdf8898bd225c8d27a6dfc9cb53 416 work/repo/.git/hooks/pre-merge-commit.sample
F ecce9c7e04d3f5dd9d8ada81753dd1d549a9634b26770042b58dda00217d086a 1374 work/repo/.git/hooks/pre-push.sample
F 4febce867790052338076f4e66cc47efb14879d18097d1d61c8261859eaaa7b3 4898 work/repo/.git/hooks/pre-rebase.sample
F a4c3d2b9c7bb3fd8d1441c31bd4ee71a595d66b44fcf49ddb310252320169989 544 work/repo/.git/hooks/pre-receive.sample
F e9ddcaa4189fddd25ed97fc8c789eca7b6ca16390b2392ae3276f0c8e1aa4619 1492 work/repo/.git/hooks/prepare-commit-msg.sample
F a53d0741798b287c6dd7afa64aee473f305e65d3f49463bb9d7408ec3b12bf5f 2783 work/repo/.git/hooks/push-to-checkout.sample
F 44ebfc923dc5466bc009602f0ecf067b9c65459abfe8868ddc49b78e6ced7a92 2308 work/repo/.git/hooks/sendemail-validate.sample
F 8d5f2fa83e103cf08b57eaa67521df9194f45cbdbcb37da52ad586097a14d106 3650 work/repo/.git/hooks/update.sample
F 5dd0af1e38c7113c130f21785f92ac8c222cce05e0f1c6103ad7795707e99a18 209 work/repo/.git/index
D work/repo/.git/info
F 6671fe83b7a07c8932ee89164d1f2793b2318058eb8b98dc5c06ee0a5a3b0ec1 240 work/repo/.git/info/exclude
D work/repo/.git/logs
F 2590bff8306ccda7db20d77ef1a39d7030c88982d32b292af16b4d8ab0884ea7 223 work/repo/.git/logs/HEAD
D work/repo/.git/logs/refs
D work/repo/.git/logs/refs/heads
F 2590bff8306ccda7db20d77ef1a39d7030c88982d32b292af16b4d8ab0884ea7 223 work/repo/.git/logs/refs/heads/master
D work/repo/.git/logs/refs/remotes
D work/repo/.git/logs/refs/remotes/origin
F 2590bff8306ccda7db20d77ef1a39d7030c88982d32b292af16b4d8ab0884ea7 223 work/repo/.git/logs/refs/remotes/origin/HEAD
D work/repo/.git/objects
D work/repo/.git/objects/info
D work/repo/.git/objects/pack
F aa970aa1fb06bcec7069ceb60c847e09cc73f338d0ff34c2a54e01a689bb8503 1268 work/repo/.git/objects/pack/pack-5e4bc6835c05585b965e06a6a1295af1cc82250c.idx
F 5ffadf0b137f4d411f7b1738c687b307fe2effb92430814e05307a16abc22308 481 work/repo/.git/objects/pack/pack-5e4bc6835c05585b965e06a6a1295af1cc82250c.pack
F 676a6ad23f602a326f75b7837ad57ee0014130b645647daadf442a811a34cc45 80 work/repo/.git/objects/pack/pack-5e4bc6835c05585b965e06a6a1295af1cc82250c.rev
F 6384630bbc5c7ff8e0602461e8fcb001dd3909bc37732a1f06745c0a37518188 114 work/repo/.git/packed-refs
D work/repo/.git/refs
D work/repo/.git/refs/heads
F 9b7d6fe61912c3021f78822e10166a8372b23104adaac7051732fe897ed88459 41 work/repo/.git/refs/heads/master
D work/repo/.git/refs/remotes
D work/repo/.git/refs/remotes/origin
F cdc65e67690c4c6475174e5ec662b70655246a2f3924354778835ab3be70aa76 32 work/repo/.git/refs/remotes/origin/HEAD
D work/repo/.git/refs/tags
F c0cde77fa8fef97d476c10aad3d2d54fcc2f336140d073651c2dcccf1e379fd6 2 work/repo/file
F 480c2336b410f1ad5f8bf1b28944490255804b65350c527787e74ebdd511e3a4 7 work/repo/other
```

Removed at 2026-10-10T10:41:02Z. Afterward the path was absent. Still present:
`worktrees/task-4a14585d`, `worktrees/task-70b27342`, `repos/antiphon`,
`review-evidence/df0d419a`, `review-evidence/52d6dd7f`, `review-evidence/a9d35067`,
`review-evidence/ca393fdc`, and the rest of `review-evidence/4a14585d`
(`code-checkpoint`, `review-checkpoint`, `review.md`, and the probe files).
`docker volume rm` was not run.

## Read-only walk

Pending in this commit. The same `/tmp/c1175-replay.sh` (sha256
`e77e035ffad16fcc7fceae3e57d2562a6922d5a29cfa4f6b22e58a40800e8361`) runs
after this note is pushed. Nothing further is deleted.

## Next stage

`next: land`. The walk result follows in a later commit on this branch.
`retire-temp` stays with the orchestrator. Post-land Mutation does not apply.
