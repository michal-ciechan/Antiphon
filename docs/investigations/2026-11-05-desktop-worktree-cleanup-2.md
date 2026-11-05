# Desktop worktree cleanup, pass 2 — 2026-11-05

Removed **400** registered worktrees; **275** observed paths are held or deferred. The run stopped at the authorized 400-deletion limit.

Operator authority was already explicit; no additional go-ahead was requested. The explicit pass-2 brief governed removal instead of S4a release. S4a was neither invoked nor enabled. No builds, tests, service restarts, runner restarts, branch deletion, ref deletion, `git clean`, or Docker operations were performed. Protected checkout, release and nightly directories and existing evidence were left intact. This report is the only task-branch deliverable; raw receipts and scripts are external evidence. New evidence was written only under the evidence directory named below.

## Counts and disk space

| Metric | Value |
| --- | --- |
| Operational inventory start UTC | 2026-11-05T13:43:09.856135+00:00 |
| Final observation UTC | 2026-11-05T14:52:47.838683+00:00 |
| Task branch | feat/card-task-4b63abcc |
| Initial task-branch HEAD | 388ff482046a62f813a251208bfeabe7c21808ea |
| Initial paths / all observed paths | 675 / 675 |
| Initial canonical registrations / final registrations | 649 / 249 |
| Requested project task list / extended ownership list | 7327 / 7970 |
| Deleted / held or deferred | 400 / 275 |
| Deletion batches | 26 |
| Deleted logical file bytes / files | 83877854225 / 3857838 |
| Deleted logical file size | 83.878 GB (78.117 GiB) |
| Free C: bytes before / after | 275188506624 / 377582096384 |
| Observed C: free-space gain | 102.394 GB (95.361 GiB) |
| Created salvage refs | 0 |

Logical deleted bytes are the measured file sizes in directories successfully removed, excluding shared Git objects and link targets. Free-space gain is the live volume delta, affected by concurrent activity and this pass’s evidence writes; it is not an exact allocation-based attribution to cleanup. The batch table records C: free space before and after every batch.

## Admission and preservation

Paths came from the pass-owned `list.json`, formed from direct children of `C:\Antiphon\worktrees` plus canonical Git registrations. Every removal path matched `^C:\\Antiphon\\worktrees\\[A-Za-z0-9._-]+$` and was a registered linked worktree of `C:\src\Antiphon\.git`. The only remover used was `git -C <own worktree> -c core.longpaths=true worktree remove --force <exact absolute path>`. No recursive-delete fallback was used. The five previously known unregistered canary copies were absent from this pass’s initial census, so their special remover exception was unused.

Task eligibility used the server’s terminal state and completedAt more than 24 hours before checking, not directory age. The requested project list since 2026-09-01 was supplemented by the same project since 2000-01-01 with unscoped tasks included; short-ID detail lookups resolved missing rows. Nonterminal worktree/working-directory ownership, standing agents, live-session cwd values, the orchestrator and this task were excluded. Task and agent ownership was refreshed at every batch.

Every deleted row had empty, successful, stderr-free `git -c core.longpaths=true status --porcelain=v2 --untracked-files=all`, repeated immediately before removal with an unchanged HEAD. Ignored paths were enumerated with `ls-files --others --ignored --exclude-standard -z`; any listed secret-shaped basename held the tree. Filesystem traversal checked nested .git files/directories, links and traversal failures; the index was checked for submodules, and the administrative directory was checked for rebase/merge/cherry-pick/revert/sequencer state and index.lock. Direct nested-bare-layout detection was added after batch 5; a final audit of tracked and ignored filename receipts for every deleted tree found no HEAD+config bare-repository candidates. No file contents were read for secret detection.

Each HEAD was checked with `git branch -a --contains HEAD` and `git tag --contains HEAD`. An uncontained HEAD would have received `refs/salvage/desktop-worktree-<dirname>` before removal. The final reachability census independently checked all deleted HEADs against branches, tags and remote branches. Unpublished commits were not a hold condition; local and remote branch refs were not deleted by this pass.

## Held and deferred counts

| Primary failing rule or deferral | Paths |
| --- | --- |
| R0/cap | 83 |
| R1/age | 144 |
| R2/owner | 9 |
| R2/path | 22 |
| R7/unregistered | 17 |

Counts use the first failing rule per path; additional reasons appear in the complete held list. R0/cap is a run-limit deferral, not a failed content proof. Uninspected paths are not asserted clean. Initial age holds are retained for this pass even if they crossed the threshold while it ran.

## Salvage refs

None created; every removed HEAD was already referenced and remained reachable in the final branch/tag census.

## Throttle and command custody

Every shell command set its own process priority to BelowNormal; Python inherited that priority and launched Git children explicitly at BelowNormal. Batches were sequential, the first had 25 paths and later batches at most 15. A monotonic five-minute budget guarded each batch, including probes and removal. No batch exceeded five minutes. At least 20 seconds separated batch finish and the next batch start. All commands and children were awaited.

Minimum measured inter-batch gap: 20.001 seconds. After batch 5, the task-owned local driver was stopped during its inter-batch pause with no active child, awaited, and relaunched to load the direct bare-repository check. Its deliberate driver exit 1 was not a removal failure; every deletion command in that batch had exited 0. No application, runner or other agent process was stopped.

A transient API turn interruption occurred during batch 15. The original inherited local driver remained running at BelowNormal priority. The resumed turn attached to the same driver and transport session, using the saved audit (229 deletions and 14 complete batches at recovery); it did not reinitialize or replay completed candidates. `api-recovery.json` records this continuity, and the original driver was awaited through completion.

| Batch | Started UTC | Seconds | Deleted | Held | C: free bytes before | C: free bytes after |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 2026-11-05T13:43:19.144043+00:00 | 256.422 | 25 | 0 | 275186675712 | 280100913152 |
| 2 | 2026-11-05T13:48:02.326715+00:00 | 165.578 | 15 | 0 | 280098992128 | 283211472896 |
| 3 | 2026-11-05T13:51:28.017074+00:00 | 145.578 | 15 | 0 | 283207864320 | 286463807488 |
| 4 | 2026-11-05T13:54:13.604189+00:00 | 146.547 | 15 | 0 | 286462685184 | 289726189568 |
| 5 | 2026-11-05T13:57:00.162788+00:00 | 140.828 | 15 | 0 | 289724256256 | 292976640000 |
| 6 | 2026-11-05T13:59:41.001603+00:00 | 139.531 | 15 | 0 | 292983201792 | 296480702464 |
| 7 | 2026-11-05T14:02:20.536958+00:00 | 137.422 | 15 | 0 | 296478650368 | 299976249344 |
| 8 | 2026-11-05T14:04:57.965507+00:00 | 130.563 | 15 | 0 | 299978846208 | 314011369472 |
| 9 | 2026-11-05T14:07:28.539950+00:00 | 145.125 | 15 | 0 | 314013925376 | 317524312064 |
| 10 | 2026-11-05T14:10:13.661691+00:00 | 142.281 | 15 | 0 | 317522608128 | 321012670464 |
| 11 | 2026-11-05T14:12:55.938554+00:00 | 149.765 | 15 | 0 | 321010556928 | 324532240384 |
| 12 | 2026-11-05T14:15:45.699973+00:00 | 149.000 | 15 | 0 | 324530733056 | 328045588480 |
| 13 | 2026-11-05T14:18:34.694000+00:00 | 131.375 | 15 | 0 | 328055885824 | 331572383744 |
| 14 | 2026-11-05T14:21:06.069971+00:00 | 130.016 | 15 | 0 | 331570601984 | 335103283200 |
| 15 | 2026-11-05T14:23:36.091364+00:00 | 124.938 | 15 | 0 | 335101509632 | 338625105920 |
| 16 | 2026-11-05T14:26:01.027656+00:00 | 128.828 | 15 | 0 | 338623406080 | 342147428352 |
| 17 | 2026-11-05T14:28:29.852761+00:00 | 124.781 | 15 | 0 | 342154907648 | 345679306752 |
| 18 | 2026-11-05T14:30:54.626659+00:00 | 128.860 | 15 | 0 | 345681956864 | 349223682048 |
| 19 | 2026-11-05T14:33:23.479946+00:00 | 134.781 | 15 | 0 | 349222178816 | 352751017984 |
| 20 | 2026-11-05T14:35:58.258257+00:00 | 127.172 | 15 | 0 | 352753434624 | 356293160960 |
| 21 | 2026-11-05T14:38:25.428236+00:00 | 124.860 | 15 | 0 | 356291330048 | 359838466048 |
| 22 | 2026-11-05T14:40:50.290720+00:00 | 121.031 | 15 | 0 | 359844139008 | 363387510784 |
| 23 | 2026-11-05T14:43:11.319528+00:00 | 124.406 | 15 | 0 | 363384991744 | 366928166912 |
| 24 | 2026-11-05T14:45:35.727373+00:00 | 132.437 | 15 | 0 | 366934884352 | 370479665152 |
| 25 | 2026-11-05T14:48:08.156868+00:00 | 122.500 | 15 | 0 | 370478092288 | 374035931136 |
| 26 | 2026-11-05T14:50:30.655191+00:00 | 119.891 | 15 | 0 | 374033444864 | 377584713728 |

## Final verification and pruning

All 400 deleted directories were rechecked absent and their Git registrations were rechecked absent. SHA-256 validation covered every per-worktree receipt. No concurrent arrivals appeared in the final census. `git worktree prune --dry-run --verbose` was empty. Ordinary `git worktree prune --verbose` then exited 0 and removed no metadata.

Concurrent ref census drift: 0 removed refs, 4 moved refs, 8 added refs. These observations include concurrent actors; this pass issued no ref deletion. Full names and before/after SHAs are in `ref-drift.json`. All deleted HEADs remained reachable.

## Complete held list

| Exact path | Exists at final census | Failing rule(s) |
| --- | --- | --- |
| `C:\Antiphon\evidence\code-67a63a4f\base-checkout` | True | R2/path: outside validated direct-child scope |
| `C:\Antiphon\scratch\089ef857-master` | True | R2/path: outside validated direct-child scope |
| `C:\Antiphon\scratch\089ef857-rc-check` | True | R2/path: outside validated direct-child scope |
| `C:\Antiphon\scratch\78334d64-rc` | True | R2/path: outside validated direct-child scope |
| `C:\Antiphon\scratch\a37ceaf1-rc` | True | R2/path: outside validated direct-child scope |
| `C:\Antiphon\scratch\ae4df578-baseline` | True | R2/path: outside validated direct-child scope |
| `C:\Antiphon\scratch\ae4df578-rc` | True | R2/path: outside validated direct-child scope |
| `C:\Antiphon\worktrees\.antiphon` | True | R2/path: retained evidence, operational directory or renamed residue |
| `C:\Antiphon\worktrees\.card-task-5fcba512.removing-ec201e2d085b1894` | True | R2/path: retained evidence, operational directory or renamed residue |
| `C:\Antiphon\worktrees\.card-task-608ed47c.removing-07bc3fac8dc6f57f` | True | R2/path: retained evidence, operational directory or renamed residue |
| `C:\Antiphon\worktrees\.card-task-9888335c.removing-c972aef0726f2b9a` | True | R2/path: retained evidence, operational directory or renamed residue |
| `C:\Antiphon\worktrees\.card-task-d493e467.removing-8302484fa7c3c828` | True | R2/path: retained evidence, operational directory or renamed residue |
| `C:\Antiphon\worktrees\.card-task-fa69124f.removing-e8a88f3848672c76` | True | R2/path: retained evidence, operational directory or renamed residue |
| `C:\Antiphon\worktrees\.card-task-ffc7e9ad.removing-79405ad685d2cf98` | True | R2/path: retained evidence, operational directory or renamed residue |
| `C:\Antiphon\worktrees\.retained` | True | R2/path: retained evidence, operational directory or renamed residue |
| `C:\Antiphon\worktrees\card-CARD-0004` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-00a0b2d1` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-03e7cd6d` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-05081946` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-0726e42d` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-08a2e7f3` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-08cca1b8` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-0c0e9de1` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-0c3b7de3` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-0c839994` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-0cd23a4c` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-0e8a1b50` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-0efa38ca` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-11a0b0b8` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-11a6d8bb` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-122f2dcf` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-12c87cda` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-133255e3` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-157eb737` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-1893101c` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-189595a5` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-18d59e3e` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-1999cb95` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-19a14614` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-1c022492` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-1c0653a5` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-1c2d93f1` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-1c44bc80` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-1c4f8dd2` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-1c7946d0` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-1cf13574` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-1d5f5588` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-1dc468a4` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-1edbc845` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-1ee29680` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-212363b0` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-23889c9b` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-23fce9a6` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-250a07fb` | True | R2/owner: agent:3d81957d-cbc5-4b9a-bab8-13550c0a8b50, task:250a07fb-0376-45a0-a147-b9040d7a716e:Blocked |
| `C:\Antiphon\worktrees\card-task-27ca134f` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-27e2f0b5` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-291753f8` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-29664acc` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-29cd334a` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-2e0375f1` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-2ede815a` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-2f58c570` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-306148cc` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-314297be` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-31e20f5a` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-34125850` | True | R2/owner: agent:5fd767cd-269e-4ee5-bdfc-e96e3742d127, task:34125850-eccf-49d3-b77e-8eece792d992:Blocked |
| `C:\Antiphon\worktrees\card-task-35623cb8` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-362f7434` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-36b3dc05` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-374d7fa0` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-37b01055` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-37f1e778` | True | R2/owner: agent:ca56d9e7-17b8-466f-8da6-545e9b4afb73, task:37f1e778-3a76-452b-b28c-3f373075de6d:Dispatched |
| `C:\Antiphon\worktrees\card-task-3a179edb` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-3b906877` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-3c2aac7d` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-3e640ea9` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-3f52b55a` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-3fd6747e` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-4047a80b` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-406bca79` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-449c541d` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-459edfef` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-47169b54` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-47d31366` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-47f9be84` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-4809596b` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-4987d1e9` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-4a75ce55` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-4af5405a` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-4b63abcc` | True | R2/owner: agent:abf58a1f-1e99-45bc-9d20-f5073f64f213, own task, task:4b63abcc-9390-41da-ad8a-a7c228d34b4f:Dispatched |
| `C:\Antiphon\worktrees\card-task-4b9d4604` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-4c2d90db` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-4ed13efe` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-52410842` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-5242d604` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-56b0f9aa` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-56bf971d` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-56e7e9b7` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-577b2ee0` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-59ab5d9a` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-5baa0ee3` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-5bdb0e6f` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-5c10955b` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-5e05fc80` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-605c6f4e` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-606b7d8d` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-60cb0a38` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-613f2230` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-64fb3f5d` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-6569dd36` | True | R2/owner: agent:0aa8761b-d7f8-4132-a110-1f7e125e0a22, task:6569dd36-5266-4f91-b797-60e984e4be6c:Blocked |
| `C:\Antiphon\worktrees\card-task-6578802d` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-66eec4dd` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-67077ff7` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-684dc939` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-692b70e4` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-6949514d` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-6c1b0f87` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-6c65bbc8` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-6dfad742` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-704c7117` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-71dacd6c` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-727a57dd` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-7289f7c7` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-73233c64` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-73837e64` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-73c027ae` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-73c94e0b` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-75c9410c` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-75d1358a` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-78e606d5` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-7ab0d65a` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-7c7132b3` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-7eb005f3` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-7ef9aa63` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-7f675cf6` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-801ad6c3` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-8049d727` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-821a7c5b` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-8225fae1` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-8347d02a` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-84692314` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-84b44899` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-8592d536` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-86a12341` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-86a941a3` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-8719af3e` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-871c4033` | True | R2/owner: agent:9164219c-2d07-4358-9bbc-80504cac6102, task:871c4033-05ab-41b6-8f2b-93023ec3aedc:Dispatched |
| `C:\Antiphon\worktrees\card-task-87232051` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-880edccc` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-8a2a0e71` | True | R2/owner: agent:e11e2d31-a7da-4708-a0c6-f0e71c65961e, task:8a2a0e71-1378-4222-93ce-59c8f3a3aeeb:Dispatched |
| `C:\Antiphon\worktrees\card-task-8ac2299c` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-8acb8a4d` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-8b609b8a` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-8e38d51f` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-927b4796` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-931a28cf` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-93a16649` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-93aae0fa` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-9478d178` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-9602645b` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-96f2fc86` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-977203e7` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-9843b768` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-99e23919` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-9c415aaf` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-9e4c7963` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-9f03d97e` | True | R2/owner: agent:f87a2672-ce0d-4e1f-a994-b73534234bca, task:9f03d97e-bb40-4f1b-99c8-5961aded0191:Dispatched |
| `C:\Antiphon\worktrees\card-task-9f577891` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-a1fb2dc7` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-a22bdfed` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-a2cfcdc1` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-a41890aa` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-a42d888f` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-a4933b4f` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-a695169d` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-a6c5c347` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-a6fe70f2` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-a7b2b64a-rc` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-a8d643be` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-aa58fb53` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-acd85970` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-adbd81fe` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-ae6457ac` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-b02d7898` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-b054e10c` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-b055e88c` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-b1cdbed3` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-b3bd8985` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-b3c01691` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-b578f67a` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-b5b7324a` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-b6a88568` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-b7350767` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-ba8ec885` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-bbd446f6` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-bdaa665c` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-c0b9f2f7` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-c0cea8f5` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-c0e71ca7` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-c1c1c031` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-c1e2a660` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-c3ba1792` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-c490e425` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-c5d82758` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-c682149c` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-c781f7f8` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-c7f02607` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-c81faedc` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-c8274f67` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-c9ebcc1d` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-cb6b4d80` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-cc42b674` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-ccfe679f` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-cf18faac` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-cfccf493` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-d19080aa` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-d2564da4` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-d5e8910e` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-d6e48431` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-d707a486` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-d933f966` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-da743107` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-dc12c438` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-dd35b058` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-e056f0ff` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-e23c0f83` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-e24018e3` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-e36a0edf` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-e379bcd9` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-e38f32c6` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-e3922461` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-e3c30656` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-e438ae14` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-e45ab432` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-e5258e8c` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-e601cbb4` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-e6f03caf` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-e778bd07` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-e80ac465` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-e8242d52` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-e857ef3c` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-ea048200` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-eab3a4fc` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-ec8c178b` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-edbaee26` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-f059674d` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-f1a2101f` | True | R7/unregistered: no permitted directory remover |
| `C:\Antiphon\worktrees\card-task-f2976027` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-f2ed8c32` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-f2f59326` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-f303b3ec` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-f35f1237` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-f54a5386` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-f5e96e75` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-f5eb193f` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-f674c79a` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-f67b2762` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-f6e3cc3c` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-f7703a17` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-f85985fd` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-f893d1a3` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-f8ac5aa5` | True | R2/owner: agent:df34edb5-d7c1-4f5d-96f7-3c41737e845b, task:f8ac5aa5-5b4f-4176-86ee-4460cf20280f:Dispatched |
| `C:\Antiphon\worktrees\card-task-fada17c6` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-fc2bcb80` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-ff3ef62e` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-ff7ab869` | True | R1/age: terminal completion not more than 24h ago |
| `C:\Antiphon\worktrees\card-task-ff8f8f3f` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\card-task-ffe310fe` | True | R0/cap: 400-deletion run limit; remaining proofs not attempted |
| `C:\Antiphon\worktrees\land` | True | R2/path: retained evidence, operational directory or renamed residue |
| `C:\Antiphon\worktrees\land\0f9784a67773` | True | R2/path: outside validated direct-child scope |
| `C:\src\Antiphon` | True | R2/path: outside validated direct-child scope |
| `C:\src\Antiphon-card0417` | True | R2/path: outside validated direct-child scope |
| `C:\src\rc-bundle-c-final` | True | R2/path: outside validated direct-child scope |
| `C:\src\rc-c1525` | True | R2/path: outside validated direct-child scope |
| `C:\temp\antiphon-add64715-base` | True | R2/path: outside validated direct-child scope |

## Deleted worktrees

| Exact path | Preserved HEAD | Batch | Logical bytes |
| --- | --- | --- | --- |
| `C:\Antiphon\worktrees\card-task-09beefe7` | cdfaf93b3913b8eef9452ae8f13d81783e3b2197 | 24 | 212727868 |
| `C:\Antiphon\worktrees\card-task-3af9f716` | 91aa1636816e64bf4cf08aa4b631207f0e9df47a | 17 | 211656840 |
| `C:\Antiphon\worktrees\card-task-3be64a0a` | e7de1ab498629de66cf38e223c2b6e056593767f | 15 | 211607002 |
| `C:\Antiphon\worktrees\card-task-3c6f388b` | 5053dd18e2d84233a978903802b6b94de17a477c | 4 | 209518567 |
| `C:\Antiphon\worktrees\card-task-3d638b84` | 6e4f208f60c9247c75a87f205b9b48b3e4ba54fb | 1 | 190335324 |
| `C:\Antiphon\worktrees\card-task-3deae49f` | e04a68f4455d9b50ea8629b71e16b2c1338d03bb | 6 | 209716969 |
| `C:\Antiphon\worktrees\card-task-3e4381e4` | 79f8a376d35f54d801c8547d328f851a7fa8f61f | 25 | 212722657 |
| `C:\Antiphon\worktrees\card-task-3f46a92a` | 960120797bf326b3ed0190db5581924f94b777e5 | 16 | 211598907 |
| `C:\Antiphon\worktrees\card-task-3fa792e4` | e2a886bfa26cedefc7f6f98e31fe52f9b8dceaf6 | 14 | 211437657 |
| `C:\Antiphon\worktrees\card-task-3fdf288c` | 960cf236152e9a2e0c333e23072018e1826e4e4d | 2 | 209459518 |
| `C:\Antiphon\worktrees\card-task-413110f7` | 604a3924dbdf25fce9f04bb3410d55b185be002e | 6 | 209694112 |
| `C:\Antiphon\worktrees\card-task-4170d4cf` | 18c1cf8556a52fcfce09ad55f2ae5d8ce74db4fd | 3 | 209467846 |
| `C:\Antiphon\worktrees\card-task-421e496e` | f5283ad087512bde775f4a43669441e4790f7d86 | 9 | 209845630 |
| `C:\Antiphon\worktrees\card-task-4230f0b9` | 8401321fdb26b8e729065cd2afe6d6a3ac47b216 | 25 | 212706448 |
| `C:\Antiphon\worktrees\card-task-42e16a50` | 346e1684c2d633183f5a7dc915e7a2f9f8e292a1 | 20 | 212091461 |
| `C:\Antiphon\worktrees\card-task-436ebdc6` | c2f305044afd500ffeb93031abd1382b81f23640 | 17 | 211637685 |
| `C:\Antiphon\worktrees\card-task-43f316b5` | e108286b2bf1319a3951883db6aee6209a26cbd7 | 23 | 212701671 |
| `C:\Antiphon\worktrees\card-task-44670038` | fdc4d4ddc8e4b412d15fa08a550ff6383a6de400 | 22 | 212701778 |
| `C:\Antiphon\worktrees\card-task-4468a62d` | fae47f85d86476741645815f43ac50910ef73a5d | 17 | 211651494 |
| `C:\Antiphon\worktrees\card-task-4468bc78` | 71beedc82a1f1d3553fc39e62972a3734adbca2a | 6 | 209682828 |
| `C:\Antiphon\worktrees\card-task-44be0c59` | 2ec1b156ebce59544b056e3ee13143837f8766c6 | 5 | 209540627 |
| `C:\Antiphon\worktrees\card-task-4521968e` | 97c185b0ba67bb5c12ff26a1bb3fcb4b88882116 | 10 | 209840118 |
| `C:\Antiphon\worktrees\card-task-4555c064` | 851bae3a19fbfa9126da72543f959dccef9aebb4 | 18 | 211917571 |
| `C:\Antiphon\worktrees\card-task-455c5755` | 55c4fb1b625d99d31575f7e947b00ec6165f52d0 | 16 | 211644294 |
| `C:\Antiphon\worktrees\card-task-456cfb77` | ad3c2dbe7c2809dbb6c240b6c40caa276d51f073 | 22 | 212697270 |
| `C:\Antiphon\worktrees\card-task-460ee5a4` | 36212ffab5856a851d566f4557c787cac0579b09 | 1 | 190259298 |
| `C:\Antiphon\worktrees\card-task-46541b3e` | 16e94bdf3d3691c4aaa63e69fd48263d4c2a79ab | 20 | 212145704 |
| `C:\Antiphon\worktrees\card-task-48e15e43` | b032722199a0f32e9d03da52d3611351508dcc82 | 9 | 209792142 |
| `C:\Antiphon\worktrees\card-task-49f0e291` | bc313acf9255ea15e62f4975008d3e6a8360b56b | 4 | 209524836 |
| `C:\Antiphon\worktrees\card-task-4aa6e6f8` | d4345c226ecef44b3aca333ac3d56a4ed55d7f70 | 9 | 209799887 |
| `C:\Antiphon\worktrees\card-task-4b2ef72c` | 53d8002188e39646801a6a29937b769fd7af0dc1 | 18 | 212104073 |
| `C:\Antiphon\worktrees\card-task-4b605c04` | 35da6d8cc83a6c256464713180dcbb940109f50c | 11 | 211024438 |
| `C:\Antiphon\worktrees\card-task-4bf5220c` | ed4104d5311ab9f629648662de01119b4014253c | 18 | 212077074 |
| `C:\Antiphon\worktrees\card-task-4c956d48` | 2aab4d4d07888e49d8ddcadaee75d58e29d6fc98 | 12 | 211162392 |
| `C:\Antiphon\worktrees\card-task-4cf9afe1` | 21e647ae7b9b2a390201874ec4f1e7bf4f61af94 | 3 | 209467635 |
| `C:\Antiphon\worktrees\card-task-4d4e6cda` | 46cb2960717363f6b62378a3940637d8e5398c1d | 19 | 212085817 |
| `C:\Antiphon\worktrees\card-task-4dc48523` | 0c7279252ec6994a300f7b65df235cf70d0625d9 | 5 | 209682411 |
| `C:\Antiphon\worktrees\card-task-4ec701ad` | 10b62c007d520287023b139740b14dcc6d9543e8 | 1 | 190452874 |
| `C:\Antiphon\worktrees\card-task-4f64e784` | 4ca16a502dfaf16056d8ade00891f62ceac2ddfa | 23 | 212691917 |
| `C:\Antiphon\worktrees\card-task-506ad7e2` | 09e96de2d48523e924e661434300ac548cfeb9cf | 11 | 211038550 |
| `C:\Antiphon\worktrees\card-task-5073e215` | a7fa13954056f65f3d950d48cd4e9de9c1f2a3d1 | 7 | 209782441 |
| `C:\Antiphon\worktrees\card-task-50d6bd21` | 7ea938f31dcc7b33e8077e604c792e7b3bab6f2a | 6 | 209706307 |
| `C:\Antiphon\worktrees\card-task-516b3a0a` | 91aa1636816e64bf4cf08aa4b631207f0e9df47a | 16 | 211656840 |
| `C:\Antiphon\worktrees\card-task-52219abc` | 679b696bd178e3c8bb14b32fcd04a1c26b148435 | 5 | 209466839 |
| `C:\Antiphon\worktrees\card-task-52590db3` | ccbb84f70cde4f54efd1952d1f95f5038096b4a2 | 1 | 188387430 |
| `C:\Antiphon\worktrees\card-task-53c61115` | 919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd | 18 | 212068734 |
| `C:\Antiphon\worktrees\card-task-53edd261` | e2a886bfa26cedefc7f6f98e31fe52f9b8dceaf6 | 14 | 211437657 |
| `C:\Antiphon\worktrees\card-task-54b40aa3` | 1cd086082c86d83adde3431cb1536326f6e90e4b | 6 | 209692462 |
| `C:\Antiphon\worktrees\card-task-54c47f10` | ad9bf0c14bb0248b6be070e6c9df509503e1b646 | 15 | 211616031 |
| `C:\Antiphon\worktrees\card-task-551ea479` | a538f98b294d2f6f395345b325178cd65054b179 | 11 | 211050944 |
| `C:\Antiphon\worktrees\card-task-55b2b668` | c0666337845eb7d08078384b5aa04300940b04df | 26 | 213043436 |
| `C:\Antiphon\worktrees\card-task-56972c6a` | 0f3598ea5491f4cb398d8a2ac6876a411634c7a4 | 6 | 209736921 |
| `C:\Antiphon\worktrees\card-task-57e28053` | c2f1f83a30b324af81149918e8a6360565b95344 | 24 | 212805808 |
| `C:\Antiphon\worktrees\card-task-5864e36c` | c53d2583ccce9f4da7808846f7dadc44112016a3 | 10 | 209993604 |
| `C:\Antiphon\worktrees\card-task-5a6278c8` | 289eef0ba68c7a98e36e03576f1fca9174bbe571 | 12 | 211232967 |
| `C:\Antiphon\worktrees\card-task-5a733e90` | 97e77f9f1fd6f71e2719bc661b48f8f89f129029 | 26 | 213026539 |
| `C:\Antiphon\worktrees\card-task-5aaa94f1` | f422433cd3dc8843dffe34653076273bdbd00ab8 | 24 | 212693999 |
| `C:\Antiphon\worktrees\card-task-5b21d0be` | 2db78f306716a62279880a030f4263911f877dbb | 10 | 210034209 |
| `C:\Antiphon\worktrees\card-task-5b7656a9` | 7fcbc999eec061fccc9adb621f358537e0df1855 | 19 | 212087037 |
| `C:\Antiphon\worktrees\card-task-5b84b043` | 329ee2b50faa80def7cd56cb77e84b74c9119373 | 15 | 211671369 |
| `C:\Antiphon\worktrees\card-task-5c28c416` | fbd95993f51ad438df98686a891248a4e48fe49f | 5 | 209547201 |
| `C:\Antiphon\worktrees\card-task-5d97c7d9` | e1a7614f6605e5a6b6d84494bb5c95a111d92d53 | 17 | 211652849 |
| `C:\Antiphon\worktrees\card-task-5dd200c3` | 9261c368b000951e1178e37b57af6d0395a42edb | 1 | 202868981 |
| `C:\Antiphon\worktrees\card-task-5e86c3d0` | 1fa8b7a9ea638aab2b3779390648e12fc8c2c122 | 8 | 209796389 |
| `C:\Antiphon\worktrees\card-task-5e9340ec` | 5529ba181eaa5e54f09add8cd92d27c9719705cc | 23 | 212700675 |
| `C:\Antiphon\worktrees\card-task-5ee33c48` | 87f4a868a86ce594d6dc9d1f1365315cb3f86df9 | 2 | 209442869 |
| `C:\Antiphon\worktrees\card-task-5ef0b4bc` | ec25d5f1aaa90f38796b53cf7126fb36c015c932 | 7 | 209884067 |
| `C:\Antiphon\worktrees\card-task-5f63ea92` | c9ab2497cd6034385b4d168261f529feff1df954 | 22 | 212781836 |
| `C:\Antiphon\worktrees\card-task-5f69c8ce` | d11315319a998352fb5569511c21002d82c0b29f | 22 | 212686038 |
| `C:\Antiphon\worktrees\card-task-5fd6ac3e` | 0e88f5adba7249c356f3b06e1626a9250525156b | 13 | 211197939 |
| `C:\Antiphon\worktrees\card-task-5fefd110` | dd5837a40ecb438563dfb6d161909e02d433998e | 19 | 212296091 |
| `C:\Antiphon\worktrees\card-task-5ff161fd` | 09022f2fe0f16e82fa1bcd914fb9f428bd893eb7 | 11 | 211017666 |
| `C:\Antiphon\worktrees\card-task-6026001a` | 21d7e868568555dbfa6f4f61f11ee20bebfc7f6e | 6 | 209727303 |
| `C:\Antiphon\worktrees\card-task-60a7cb17` | 87acce7be9d82fb78f04456f69f3860b4c7698da | 2 | 194433941 |
| `C:\Antiphon\worktrees\card-task-62afb5fc` | 907edaebbd301af95629c31095f1b530803baea2 | 14 | 211570698 |
| `C:\Antiphon\worktrees\card-task-632e079b` | e2cfadf36b928ffd0b3b9bbc3d492e4e83befc7b | 13 | 211197563 |
| `C:\Antiphon\worktrees\card-task-6379c50f` | 74178698ea316af2902c5ef653e8ecedecf3c698 | 18 | 212057925 |
| `C:\Antiphon\worktrees\card-task-6399a4f7` | 19cc96020c316bfec21dc000c7f8a807d9726313 | 1 | 194392211 |
| `C:\Antiphon\worktrees\card-task-63fb82fb` | d201995bf367097aeeef8381d38cce2e4e4c2473 | 2 | 194420557 |
| `C:\Antiphon\worktrees\card-task-654750f8` | 7500c1ea01c0a00bfd47529c7f8788a35c4caacc | 14 | 211440392 |
| `C:\Antiphon\worktrees\card-task-675b622d` | e114313d36ef0892676c4e0f4be2c956808c7978 | 7 | 209758805 |
| `C:\Antiphon\worktrees\card-task-67640bd1` | c9c92e50b49ddc242e4d0dfc5d742242ad719e98 | 13 | 211431583 |
| `C:\Antiphon\worktrees\card-task-67e63dbb` | 43ae7371d7e9e173e33b69efdc061ac2891c9d98 | 10 | 210083146 |
| `C:\Antiphon\worktrees\card-task-689df4fd` | 59a1e15dd1329df378ec5715766086244862a875 | 18 | 211924429 |
| `C:\Antiphon\worktrees\card-task-68ebcb70` | 815a16e1f0508aaeaf9cdecb2671eba5b026460b | 4 | 209523746 |
| `C:\Antiphon\worktrees\card-task-69381933` | e49cbddfd5349a8d955be0971178efcd57183d82 | 2 | 190374906 |
| `C:\Antiphon\worktrees\card-task-6a12a89d` | e59ca744cf36b7f6828f93201acadb513d3bad02 | 6 | 209804838 |
| `C:\Antiphon\worktrees\card-task-6ae75a92` | e7a18676c91899374bc3fc1361bee7bcf9ea4910 | 21 | 212677539 |
| `C:\Antiphon\worktrees\card-task-6b6471d0` | f58111666ada41ad13ccf0d7f965ca312fba7902 | 25 | 212711582 |
| `C:\Antiphon\worktrees\card-task-6c3a5d56` | 508b5fda96abb63bc26bcd42433e636e14d958e5 | 24 | 212724300 |
| `C:\Antiphon\worktrees\card-task-6c414224` | 4d7169394d974205638bdca45b6e336715e241f3 | 12 | 211214374 |
| `C:\Antiphon\worktrees\card-task-6d8dd96a` | f578f5daad13ce20db8ee657eb18564902d395f1 | 21 | 212688376 |
| `C:\Antiphon\worktrees\card-task-6db87da1` | 13d5f15e0e307ef6fd308db3c3e0ab348e3b9b7a | 25 | 212943350 |
| `C:\Antiphon\worktrees\card-task-6e58ba06` | 892db366c6232263e854b7bd510b557593e3c00d | 22 | 212690075 |
| `C:\Antiphon\worktrees\card-task-6f3ab7aa` | bb0947982a3c170c14ee93d72b4a4f635436bb40 | 19 | 212289354 |
| `C:\Antiphon\worktrees\card-task-6f66c43e` | 4f9999e70e74c693ddb40ce5b3690f75993d04f4 | 23 | 212806744 |
| `C:\Antiphon\worktrees\card-task-6f817352` | f41d357de0a31bf2344cbed719de53bb206acd20 | 12 | 211045700 |
| `C:\Antiphon\worktrees\card-task-6fb028cd` | ffeaa28f0fd5dfcb4c6589fc6fb2c62645ac9370 | 2 | 207975511 |
| `C:\Antiphon\worktrees\card-task-6fd10cb5` | 6bc6be278e8bf9f1353703e84d2f4928dc39250e | 18 | 212096431 |
| `C:\Antiphon\worktrees\card-task-6ff01870` | f547852cdcde0fad6708e036072171a8b0a0f16d | 18 | 212096692 |
| `C:\Antiphon\worktrees\card-task-6ffbb647` | 1cdcbb408a51183fc0d1840c8a430cb8cd34e0ed | 25 | 213051045 |
| `C:\Antiphon\worktrees\card-task-70cde4e7` | 809e7b7a916a303f16ba124c082d91b7f9cec508 | 7 | 209816661 |
| `C:\Antiphon\worktrees\card-task-7114858c` | 7fcbc999eec061fccc9adb621f358537e0df1855 | 19 | 212087037 |
| `C:\Antiphon\worktrees\card-task-737b54c5` | 4d7169394d974205638bdca45b6e336715e241f3 | 12 | 211214374 |
| `C:\Antiphon\worktrees\card-task-73a9b0f6` | b032722199a0f32e9d03da52d3611351508dcc82 | 9 | 209792142 |
| `C:\Antiphon\worktrees\card-task-748b0862` | 7a18da136db643454478ba763e0ba92c7c92c9a5 | 23 | 212707921 |
| `C:\Antiphon\worktrees\card-task-7519a931` | 0cab341017478283169d007bb6cd28cbe5e744a5 | 16 | 211574358 |
| `C:\Antiphon\worktrees\card-task-75ff83d9` | 028fcf8c8621dabd488aee4584b45d38eb845cb2 | 5 | 209666830 |
| `C:\Antiphon\worktrees\card-task-761b0853` | 74f3b4abf5925a1390f369028f9b710212dc8eb5 | 2 | 195463138 |
| `C:\Antiphon\worktrees\card-task-78641295` | 5edb19bec5fa9b362a41af98fb9481605e85f8c7 | 1 | 190345712 |
| `C:\Antiphon\worktrees\card-task-78b36185` | 41629e7b21c2ca6b239c8bc1291b178c723dba24 | 8 | 209765074 |
| `C:\Antiphon\worktrees\card-task-7909871a` | 1fa8b7a9ea638aab2b3779390648e12fc8c2c122 | 8 | 209796389 |
| `C:\Antiphon\worktrees\card-task-79245209` | 9d8260fa24716e73d7c60a18ecf14f694933aaac | 20 | 212338013 |
| `C:\Antiphon\worktrees\card-task-7982c828` | f6c15619feeb8d08de8014f858debae2c9e32569 | 23 | 212717540 |
| `C:\Antiphon\worktrees\card-task-79d3d1ba` | e56e4d1b6a727b3127b473307b860b7117980f09 | 23 | 212700612 |
| `C:\Antiphon\worktrees\card-task-79f6a820` | 3aadf16ed2f288c1381f52ee2a2dac91fa26c798 | 16 | 211627281 |
| `C:\Antiphon\worktrees\card-task-7b966444` | 93f10fbb7934bf0652d612e142143e74ab8a6bc3 | 16 | 211809234 |
| `C:\Antiphon\worktrees\card-task-7c02ed68` | 2d9cd9232ac668b23f1c82721ed630029499b04a | 20 | 212157519 |
| `C:\Antiphon\worktrees\card-task-7c150718` | 0d2bc0cd6ac2cc9cc71acc12c5a136bf537fcc98 | 15 | 211587484 |
| `C:\Antiphon\worktrees\card-task-7c36e01b` | c052585d9b43de14ecc28015a19652fc31b92e55 | 26 | 212940475 |
| `C:\Antiphon\worktrees\card-task-7d513c4d` | 0493af102923b4d0d34e736376fd67030dbe12cb | 2 | 208015172 |
| `C:\Antiphon\worktrees\card-task-7d7761ce` | 2b1e7494a9b100982e4e1f95a1be622fb8e918f9 | 9 | 209805880 |
| `C:\Antiphon\worktrees\card-task-7dfff223` | bcfe77b3d685301030ed1a84bc4ccfdb7a6190a6 | 19 | 212080403 |
| `C:\Antiphon\worktrees\card-task-7e95ec84` | 21e647ae7b9b2a390201874ec4f1e7bf4f61af94 | 3 | 209467635 |
| `C:\Antiphon\worktrees\card-task-7f1d7c3b` | 3a4f73374a027204ddd79b32070c5f8ebcd44222 | 12 | 211034580 |
| `C:\Antiphon\worktrees\card-task-80170e29` | c52a96a026bf321c7a3866cb442297be54b17fb9 | 19 | 212084793 |
| `C:\Antiphon\worktrees\card-task-80663342` | dc8baf408cdddcc6892f82586a7157e660c19301 | 12 | 211177975 |
| `C:\Antiphon\worktrees\card-task-815b655c` | fcf45564cddb49592de17b75f29bdb6d835b511c | 9 | 209774564 |
| `C:\Antiphon\worktrees\card-task-81e40a23` | 87f4eeabe3ae24b1c24e27ab91bb555a70fc748d | 10 | 210099971 |
| `C:\Antiphon\worktrees\card-task-81ea09a8` | f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0 | 23 | 212688044 |
| `C:\Antiphon\worktrees\card-task-82074cc2` | c6b35eb1b247eca61a98e22480239b5262dc32c4 | 16 | 211580685 |
| `C:\Antiphon\worktrees\card-task-82977d6d` | a8d3823fb80eaf6980c7e1a002f7dc1405442044 | 1 | 190055471 |
| `C:\Antiphon\worktrees\card-task-83096283` | 99e618ea848677cf08b6cdfeb7ed516fc415a75c | 9 | 209818646 |
| `C:\Antiphon\worktrees\card-task-833a515a` | 37a427aaedc43ebbd59de1aae26ccb7f45f79746 | 20 | 212641505 |
| `C:\Antiphon\worktrees\card-task-833bd9b4` | 41841661c9c0efd53b684cdf4e6a633c9ec89015 | 24 | 212725029 |
| `C:\Antiphon\worktrees\card-task-84c4fa59` | 19660b25260af3c7ef02f87f987b332bcbd7912a | 24 | 212712427 |
| `C:\Antiphon\worktrees\card-task-84d0a026` | ed67056c5e82389e95fdb82932f67396195a89f3 | 24 | 212701700 |
| `C:\Antiphon\worktrees\card-task-8562dc2a` | 426211ca25cb9a545e909a81611b2916b10d04b9 | 1 | 190049897 |
| `C:\Antiphon\worktrees\card-task-872e19d8` | 0d41eb05a3e58d24432531f22bad47068a3eb821 | 8 | 209797585 |
| `C:\Antiphon\worktrees\card-task-87cbe3ef` | d9d9b217a913cded71e72b277d344da06591c81b | 16 | 211626770 |
| `C:\Antiphon\worktrees\card-task-87d98f50` | a2584ef6e30029cae4332aec469ada1e9e95c729 | 19 | 212331248 |
| `C:\Antiphon\worktrees\card-task-889a1e4d` | 679b696bd178e3c8bb14b32fcd04a1c26b148435 | 3 | 209466839 |
| `C:\Antiphon\worktrees\card-task-88cb4849` | df823414b9536f4b277e8d7a109ae7901be692c1 | 14 | 211157265 |
| `C:\Antiphon\worktrees\card-task-88d1caf5` | c064d29b55b82b32874b240e42030731e5bd2e33 | 21 | 212678553 |
| `C:\Antiphon\worktrees\card-task-88ebc077` | 843654fc16915e7a00262760f73754200d06fc6a | 4 | 209528340 |
| `C:\Antiphon\worktrees\card-task-894ccf98` | c04630cd75d0fc00431478b72cd47ed1923c4365 | 22 | 212689690 |
| `C:\Antiphon\worktrees\card-task-89a976ea` | be8c6fb9e5f4c70ef64d9c62ebc8229882b4bfb7 | 21 | 212644303 |
| `C:\Antiphon\worktrees\card-task-89cb5516` | bac78948348cbf690d9742edfb0651212e215868 | 14 | 211554444 |
| `C:\Antiphon\worktrees\card-task-89df5cef` | 98427261bc6df2c69d3a50cc7f04f8ea5994d0d7 | 5 | 209557318 |
| `C:\Antiphon\worktrees\card-task-8a3a40d6` | 3d96c6a27ef4d23de5f5b8fb3121aaa95a44c027 | 15 | 211628262 |
| `C:\Antiphon\worktrees\card-task-8a7dbe6d` | 515f6cd1fda7bc0f6e0262ffbc4a3360bf4deb2c | 10 | 209774930 |
| `C:\Antiphon\worktrees\card-task-8b043bee` | 88dcc247087b1e0515aa2471f7865a5b1f7441c6 | 18 | 212090513 |
| `C:\Antiphon\worktrees\card-task-8b317bb8` | bac78948348cbf690d9742edfb0651212e215868 | 14 | 211554444 |
| `C:\Antiphon\worktrees\card-task-8b9a4097` | c320980084484b74c9c9a301cabb0bd7c9b09db9 | 22 | 212698378 |
| `C:\Antiphon\worktrees\card-task-8bc75abf` | 622bce61318f60fd615a1e1839a8fa59361080a8 | 26 | 213032942 |
| `C:\Antiphon\worktrees\card-task-8bd5eef3` | 0cab341017478283169d007bb6cd28cbe5e744a5 | 15 | 211574358 |
| `C:\Antiphon\worktrees\card-task-8c49a43b` | 4b34692d0fb3a5c3206448259729e5deedfcd44c | 24 | 212826237 |
| `C:\Antiphon\worktrees\card-task-8cb9f7d5` | df92aa7b7886b278514dc73dc9e53dc461baa781 | 2 | 194415922 |
| `C:\Antiphon\worktrees\card-task-8cf13b6c` | e7a0b7ac14df91fe135abeede8a20b912b51da9d | 2 | 209477742 |
| `C:\Antiphon\worktrees\card-task-8d58928b` | 7a62b81d32154a151847940887f78cb442b595f4 | 9 | 209792199 |
| `C:\Antiphon\worktrees\card-task-8d62df4b` | 9333625c141016aed7241ef7b7ac935741d32843 | 3 | 209481781 |
| `C:\Antiphon\worktrees\card-task-8f4389b2` | 41da14612bdbbf3809a1798194fcacbd15e10383 | 6 | 209689458 |
| `C:\Antiphon\worktrees\card-task-8f4c359b` | 6a4f522962ba6c7598fbad9d4e8439298a035370 | 20 | 212655375 |
| `C:\Antiphon\worktrees\card-task-8f51d41b` | 622bce61318f60fd615a1e1839a8fa59361080a8 | 26 | 213032942 |
| `C:\Antiphon\worktrees\card-task-8faaee74` | 4a0abd68507a5e3105bf7b258849b1abb033890a | 10 | 209995764 |
| `C:\Antiphon\worktrees\card-task-8ff99f6a` | 2421a7a50edf0f33c408774d45918e1f544e8c47 | 8 | 209795751 |
| `C:\Antiphon\worktrees\card-task-90e0bae3` | 679b696bd178e3c8bb14b32fcd04a1c26b148435 | 22 | 209466839 |
| `C:\Antiphon\worktrees\card-task-91dd821a` | d92701adb882bb15606e1d5ccffcae459d066891 | 1 | 190417426 |
| `C:\Antiphon\worktrees\card-task-92047d67` | fe2b5e418dda1ff93772dc6d64ffa1ece9ae6ec9 | 14 | 211350296 |
| `C:\Antiphon\worktrees\card-task-92a2c7bb` | 3c66ddd5465007908f6d752aea55a829205c0ac8 | 10 | 209826736 |
| `C:\Antiphon\worktrees\card-task-92fb5616` | fc8c91ee7c21d14dc485ac69b2a8d82662880a70 | 11 | 211056181 |
| `C:\Antiphon\worktrees\card-task-932a7025` | 861f4627d2916be5fef1adeae00ef9b2d9a9556d | 5 | 209550833 |
| `C:\Antiphon\worktrees\card-task-9360f65d` | 62beb934d40f616d8a3e26482fb1a3f7deb00154 | 19 | 212095900 |
| `C:\Antiphon\worktrees\card-task-9362d9e7` | 7746c7aee0fdd44501718cf8b0bfec68c39b833c | 21 | 212665773 |
| `C:\Antiphon\worktrees\card-task-94984bfe` | 90c986421f97dd06d574087c4f87f829e0daf8cc | 21 | 212683995 |
| `C:\Antiphon\worktrees\card-task-94a1008d` | 8b72e914eebf1b4008c0bda5e6018aad098cf8fa | 8 | 209793754 |
| `C:\Antiphon\worktrees\card-task-951d7ac2` | 46b5bd566df92938b077ccd6953c1322dce6fb69 | 1 | 192609024 |
| `C:\Antiphon\worktrees\card-task-95f39888` | 5010e23f0b394ecd4a1ea70e47f7f0c868f24f15 | 2 | 190369514 |
| `C:\Antiphon\worktrees\card-task-9654dc57` | f19058c7b09aaa8eb4c3756a5f5fb5392b486d25 | 3 | 209478118 |
| `C:\Antiphon\worktrees\card-task-9688c1d8` | 70ce0759ae213a91418f10f56479d0e567830f4f | 15 | 211627760 |
| `C:\Antiphon\worktrees\card-task-97a782cd` | a621a6944bd9961bc51fb77d6c0a299e21ee38fb | 13 | 211428150 |
| `C:\Antiphon\worktrees\card-task-97d93984` | 892db366c6232263e854b7bd510b557593e3c00d | 23 | 212690075 |
| `C:\Antiphon\worktrees\card-task-9851e64c` | 3700519929f2e08563844cca3ecd9c548edd1c59 | 4 | 209514219 |
| `C:\Antiphon\worktrees\card-task-988017cf` | 958af85ee6069a204e827074f7f05fcb8c4d371d | 17 | 211808891 |
| `C:\Antiphon\worktrees\card-task-999446a2` | bce0dd39171d1ff9dce6940459d5675ac2e71017 | 5 | 209561676 |
| `C:\Antiphon\worktrees\card-task-9ac68027` | b33cf1b2caa0aec3eb1d65a87a935680e82b6ee2 | 26 | 213503671 |
| `C:\Antiphon\worktrees\card-task-9aef59e9` | 7a6bf8ddfca056bb47b623724d6b111d58edcb05 | 11 | 211027472 |
| `C:\Antiphon\worktrees\card-task-9b6f690f` | 13a36c7eac0ba1b148d151fa17e00dd0d123359e | 25 | 213008538 |
| `C:\Antiphon\worktrees\card-task-9c0b4fa3` | 71beedc82a1f1d3553fc39e62972a3734adbca2a | 6 | 209682828 |
| `C:\Antiphon\worktrees\card-task-9c944aab` | 98274803fe9f3fc270cdc891e3313613478398ff | 4 | 209509358 |
| `C:\Antiphon\worktrees\card-task-9d568461` | f6ca2986d8713c74763b99704e79e5378c7f4258 | 3 | 209482073 |
| `C:\Antiphon\worktrees\card-task-9da7d9cf` | 269daf8fee45081464df99b122b845ae1c4d1b13 | 11 | 211019249 |
| `C:\Antiphon\worktrees\card-task-9e9cf510` | 4282088c9fb633f0ece763a8123d1e379666c536 | 22 | 212692395 |
| `C:\Antiphon\worktrees\card-task-9ee6f8ca` | f1d72c4f9f8f2040dbe93b3f453d2aad7d45af2a | 25 | 213450747 |
| `C:\Antiphon\worktrees\card-task-9f29e134` | 01e0a62045111ea390a8865fd28bd8c52c08bd40 | 26 | 213068387 |
| `C:\Antiphon\worktrees\card-task-9f89bb21` | 3ab6fc3840693e96b1a7a11ad24324dc3c5435ee | 13 | 211344273 |
| `C:\Antiphon\worktrees\card-task-a0362fd8` | 6b6221c03400cfb61743c3877e4c94e31b8c51cc | 7 | 209763813 |
| `C:\Antiphon\worktrees\card-task-a14e0f34` | 9d8260fa24716e73d7c60a18ecf14f694933aaac | 20 | 212338013 |
| `C:\Antiphon\worktrees\card-task-a361af41` | 606ff0d500e706a72e19c2651893ec3b0c3ec681 | 6 | 209687176 |
| `C:\Antiphon\worktrees\card-task-a382d6ef` | 15f1bfffeedbf2ff0046e14f2c501925c3f534a4 | 17 | 211910161 |
| `C:\Antiphon\worktrees\card-task-a3a942ab` | 2f1a9d762c82883c6f405fa509c7c73a02feba34 | 7 | 209839884 |
| `C:\Antiphon\worktrees\card-task-a3ce2552` | 24def89c7f44c3f56176539c39c7e11818b5c161 | 14 | 211576969 |
| `C:\Antiphon\worktrees\card-task-a3ff6d0b` | 4c399355c9e12791f6e50bb2641eccae7617b6ea | 14 | 211588154 |
| `C:\Antiphon\worktrees\card-task-a437c696` | f83e656f3960eeab62de886fa68dbe6d878154cb | 19 | 212097764 |
| `C:\Antiphon\worktrees\card-task-a45306f3` | 639f78a890f35093fe8366a6d2881b1dc649f005 | 26 | 213083139 |
| `C:\Antiphon\worktrees\card-task-a45f88c8` | c245a828b2acb6ef5914b597b0795c82ae9009e1 | 7 | 209759578 |
| `C:\Antiphon\worktrees\card-task-a494d175` | f41d357de0a31bf2344cbed719de53bb206acd20 | 11 | 211045700 |
| `C:\Antiphon\worktrees\card-task-a4a77bb8` | 70ce0759ae213a91418f10f56479d0e567830f4f | 16 | 211627760 |
| `C:\Antiphon\worktrees\card-task-a549ddb0` | cabedfbb3c31fa6275c2952330becd5d835b73a1 | 9 | 209806916 |
| `C:\Antiphon\worktrees\card-task-a584c99e` | b1c6889fa7fb7ca420e71f0c7ac20a38f49a6477 | 1 | 194281174 |
| `C:\Antiphon\worktrees\card-task-a59abe7c` | d2d2afe140f4a38194f926b8291cd4d3159203e3 | 8 | 209772527 |
| `C:\Antiphon\worktrees\card-task-a5f3d226` | b2ea37b4d981a2c75c8f97fd2c3e3475a8fbdcbb | 20 | 212618667 |
| `C:\Antiphon\worktrees\card-task-a6025c48` | b6bb58e763c9a8cdc5c8ff86f31e344b92e36a93 | 6 | 209759385 |
| `C:\Antiphon\worktrees\card-task-a64bf439` | 3e39e0534f1942137dc856ab932f9e9d6d6a0aaf | 7 | 209713137 |
| `C:\Antiphon\worktrees\card-task-a7108dd6` | 9b93c143ace45df5d03580c1985337807d7dc9b8 | 11 | 210045396 |
| `C:\Antiphon\worktrees\card-task-a753db19` | a42b70583bb57e1557a6b51878d1eb74ee6a03e3 | 26 | 213036480 |
| `C:\Antiphon\worktrees\card-task-a7aa3a7a` | 52a3b9ed410e6fd5bed0d49d9bf075380b276faa | 21 | 212685024 |
| `C:\Antiphon\worktrees\card-task-a7de4ea4` | 6d268b2b887dc6311787180e61d0db597f41dc86 | 14 | 211570698 |
| `C:\Antiphon\worktrees\card-task-a8bd6706` | 9be1461ce11311b2083bfec05c125cc5f2cbb6fa | 11 | 211015624 |
| `C:\Antiphon\worktrees\card-task-a921ab9d` | b2d974a9ca92f2fbaac6f2ff494c1359697fa3bf | 20 | 212647919 |
| `C:\Antiphon\worktrees\card-task-a94fabc4` | f98a865a8f3b5be4328a1dc420194f04bf6465c4 | 21 | 212685009 |
| `C:\Antiphon\worktrees\card-task-a9634687` | 0efeaa25ac5cd4228da6f2d25ab7fe971361892d | 1 | 188354087 |
| `C:\Antiphon\worktrees\card-task-a97c9731` | 77262cf7254b8ae0c4df91772e959c40c759e17e | 25 | 212717740 |
| `C:\Antiphon\worktrees\card-task-a9b3bc09` | 585c39908224bb7b4e251d09a24990f3bc8366b3 | 7 | 209760378 |
| `C:\Antiphon\worktrees\card-task-a9f45276` | babc58867db8772721a13a8d3be6f45ab47ff47a | 25 | 213041271 |
| `C:\Antiphon\worktrees\card-task-aa89c309` | 4d2b722d266babfeecb2d00285b318cc4d2b13d5 | 8 | 210633915 |
| `C:\Antiphon\worktrees\card-task-aaefd3a1` | 72bd481f475744140c282ac8a27da509b3126d0c | 3 | 209499634 |
| `C:\Antiphon\worktrees\card-task-ab00c63e` | dae23c2dbfe58053c20af3fb7a2b869fc2061046 | 11 | 211011615 |
| `C:\Antiphon\worktrees\card-task-ab50b2b5` | be8c6fb9e5f4c70ef64d9c62ebc8229882b4bfb7 | 21 | 212644303 |
| `C:\Antiphon\worktrees\card-task-ab6cf694` | c6d739ac8766369bd95bfd611ec1033567bc6bcd | 5 | 209560973 |
| `C:\Antiphon\worktrees\card-task-ac3dd93d` | 2d523e7eb7cd76e4438a12e8c2b27ed0bfc2b6ab | 13 | 211378472 |
| `C:\Antiphon\worktrees\card-task-ac4f532e` | 72e64ea0e5ebf34bd2672567c7daa46a3e23602b | 3 | 209471760 |
| `C:\Antiphon\worktrees\card-task-ace48db2` | dc1cbc7a3b5233b9efccd971203b8598a9fec3a4 | 14 | 211434781 |
| `C:\Antiphon\worktrees\card-task-ad29ef52` | 36c8287552141a4a15df7c7bc7864d6eb3955a1a | 15 | 211595221 |
| `C:\Antiphon\worktrees\card-task-ad4999db` | 7f801e8dc71d2477f7ccc979867680d1ee40a968 | 1 | 188372388 |
| `C:\Antiphon\worktrees\card-task-ad89cb02` | 679b696bd178e3c8bb14b32fcd04a1c26b148435 | 3 | 209466839 |
| `C:\Antiphon\worktrees\card-task-ae950679` | a08e4038baa82a3fbd02eb9179da78d0d8914205 | 2 | 195454540 |
| `C:\Antiphon\worktrees\card-task-aea81717` | e660864f969d39f63124307bc0fca4a30ec05ed0 | 10 | 210002899 |
| `C:\Antiphon\worktrees\card-task-aeecbb03` | 7f12e2d1bbbf1a6451e22346b49f9c92ac368e62 | 16 | 211626849 |
| `C:\Antiphon\worktrees\card-task-af497073` | 709aa7765394c579fa23a0b5351105572661a2a4 | 4 | 209516859 |
| `C:\Antiphon\worktrees\card-task-b02344a7` | bcfe77b3d685301030ed1a84bc4ccfdb7a6190a6 | 20 | 212080403 |
| `C:\Antiphon\worktrees\card-task-b0715b3f` | 8e1dea4e26c26e8b23d1cdafc2c0db83aa39a6af | 3 | 209508867 |
| `C:\Antiphon\worktrees\card-task-b0bdc516` | cc7fc10615158d9d44fd82fe41c2c260d783b156 | 25 | 213007142 |
| `C:\Antiphon\worktrees\card-task-b124e131` | a00962fd6a055c513fa567c3a930fb875660dbef | 12 | 211177219 |
| `C:\Antiphon\worktrees\card-task-b169bb27` | 679b696bd178e3c8bb14b32fcd04a1c26b148435 | 2 | 209466839 |
| `C:\Antiphon\worktrees\card-task-b1a8061c` | 06fe39d299cf6ae7dacffac10077bca68cf3310e | 21 | 212683995 |
| `C:\Antiphon\worktrees\card-task-b26811ff` | 2383ac1bc55b9a05e433884b200352074d4a21f9 | 25 | 213057489 |
| `C:\Antiphon\worktrees\card-task-b2d063aa` | b477d7ccf4faafa94cb91fd785ced348192885dc | 17 | 211721723 |
| `C:\Antiphon\worktrees\card-task-b384b934` | 49f5a8236f9e3782c522c910ee54edff0752134e | 14 | 211583769 |
| `C:\Antiphon\worktrees\card-task-b38dbb26` | cc7fc10615158d9d44fd82fe41c2c260d783b156 | 25 | 213007142 |
| `C:\Antiphon\worktrees\card-task-b608b7dc` | f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0 | 22 | 212688044 |
| `C:\Antiphon\worktrees\card-task-b6b7abdc` | 6211b45ccb9c33b1b5d24723bc3f8fec05761157 | 13 | 211353381 |
| `C:\Antiphon\worktrees\card-task-b842c9bf` | 7dc9e1168a2b4f714a4e3e29ac99e2aad485524c | 16 | 211617523 |
| `C:\Antiphon\worktrees\card-task-b8f46d8a` | 2b1e7494a9b100982e4e1f95a1be622fb8e918f9 | 10 | 209805880 |
| `C:\Antiphon\worktrees\card-task-b922ebf8` | dc9eafeeef2c8b5c8c3a5f771be4f4444ddec7f4 | 10 | 210000505 |
| `C:\Antiphon\worktrees\card-task-b9729f4b` | c4a7042913200d95d5f3564bf4f1a6f7706176b9 | 6 | 209709137 |
| `C:\Antiphon\worktrees\card-task-b9b25805` | 6a4f522962ba6c7598fbad9d4e8439298a035370 | 21 | 212655375 |
| `C:\Antiphon\worktrees\card-task-ba1cbbcc` | 49f5a8236f9e3782c522c910ee54edff0752134e | 15 | 211583769 |
| `C:\Antiphon\worktrees\card-task-ba64aba6` | c6b896facbf315cbac9d9ff3f2c0c3ec36bc906c | 2 | 194777457 |
| `C:\Antiphon\worktrees\card-task-ba83460f` | fa0166fb5debbf9f2a2bec9cb556a755532259a7 | 26 | 213036235 |
| `C:\Antiphon\worktrees\card-task-ba9f47c2` | 95e6d23982f9be66893d5b60aed48a0473b0e583 | 12 | 211130897 |
| `C:\Antiphon\worktrees\card-task-baacd1ef` | dc8baf408cdddcc6892f82586a7157e660c19301 | 13 | 211177975 |
| `C:\Antiphon\worktrees\card-task-bacbedc4` | 28bd4056478e8ec2871939540035dfd71d1f9171 | 17 | 211655867 |
| `C:\Antiphon\worktrees\card-task-bb0cd0ec` | b0374226426b8aada42e8d370b95a4e53c4612cf | 20 | 212338353 |
| `C:\Antiphon\worktrees\card-task-bb924129` | bb0947982a3c170c14ee93d72b4a4f635436bb40 | 20 | 212289354 |
| `C:\Antiphon\worktrees\card-task-bbd026a3` | 3ae8fc6fcaed32629f7acaf83b2190f31a025507 | 17 | 211916807 |
| `C:\Antiphon\worktrees\card-task-bbe3899e` | 8e7a72fa2206ba740a27aae32de4ea94c87e1fb7 | 23 | 212716900 |
| `C:\Antiphon\worktrees\card-task-bce1e9ec` | b7745271a20601d7da2a1d86385d7f0fbbf91266 | 13 | 211439852 |
| `C:\Antiphon\worktrees\card-task-bd1133db` | 8ccf236338dc2d7df2aec891f95ef9cd66618e73 | 16 | 211626614 |
| `C:\Antiphon\worktrees\card-task-bd1e86ea` | 8eac52a3ead202ef4bcac3a29d650e698ad47bc4 | 7 | 209729404 |
| `C:\Antiphon\worktrees\card-task-bd90f6b5` | 875fb6e431ab56d50628cf68d6d0ebbc3504c237 | 7 | 209718057 |
| `C:\Antiphon\worktrees\card-task-bdf87233` | 4518ce64b70c13a4c03a375d9630152342ee732c | 6 | 209709137 |
| `C:\Antiphon\worktrees\card-task-bf30e8de` | c064d29b55b82b32874b240e42030731e5bd2e33 | 21 | 212678553 |
| `C:\Antiphon\worktrees\card-task-bf34cd7b` | f58111666ada41ad13ccf0d7f965ca312fba7902 | 25 | 212711582 |
| `C:\Antiphon\worktrees\card-task-bf8f2d94` | 4518ce64b70c13a4c03a375d9630152342ee732c | 7 | 209709137 |
| `C:\Antiphon\worktrees\card-task-bfaef603` | 98274803fe9f3fc270cdc891e3313613478398ff | 4 | 209509358 |
| `C:\Antiphon\worktrees\card-task-bffd3e70` | 740e2a6a2391fec61d52f143f9086d73f2f9782c | 9 | 209943414 |
| `C:\Antiphon\worktrees\card-task-c1cb4001` | 71b100052e2ea19b8565b27f3d26c4294d12928c | 14 | 211589023 |
| `C:\Antiphon\worktrees\card-task-c238f9ca` | 61636195d4278262668f5bf5ec15a1a42767aa8a | 18 | 212256732 |
| `C:\Antiphon\worktrees\card-task-c28b88b9` | 38e67ae1579dc71da848fb2c89cc9c55aaa83543 | 24 | 212867373 |
| `C:\Antiphon\worktrees\card-task-c389a57b` | e56e4d1b6a727b3127b473307b860b7117980f09 | 23 | 212700612 |
| `C:\Antiphon\worktrees\card-task-c3ba3e10` | e7c2ce97636db523e022bf2be10d290884383636 | 19 | 212092878 |
| `C:\Antiphon\worktrees\card-task-c3ef4038` | bf08bf4d3ee6f801edf53b5e0e81a9ea98005026 | 18 | 212105558 |
| `C:\Antiphon\worktrees\card-task-c44579d7` | e38dcea2838e1b340339c3dde94b256689d15c57 | 23 | 212703195 |
| `C:\Antiphon\worktrees\card-task-c4f068b8` | 679b696bd178e3c8bb14b32fcd04a1c26b148435 | 3 | 209466839 |
| `C:\Antiphon\worktrees\card-task-c7653f3f` | c715f486d3a6cf0656ebf899d656f851c3b0193d | 13 | 211382939 |
| `C:\Antiphon\worktrees\card-task-c7a43e0c` | ce291773d354e150de324ca80460804ba0098c42 | 3 | 209473292 |
| `C:\Antiphon\worktrees\card-task-c7ae562e` | 204745bc854e99559d47398189a85cbbf4d91193 | 17 | 211651198 |
| `C:\Antiphon\worktrees\card-task-c7e4a230` | 3163976062d9107a2b01dbd6b161601edf123f1d | 19 | 212301562 |
| `C:\Antiphon\worktrees\card-task-c7fb4007` | 7c89fb3808b005f19f2f85df1d5d04747b28e014 | 4 | 209541900 |
| `C:\Antiphon\worktrees\card-task-c7fd24a5` | 0b724fea08ba15ddb02f989caea295f3f4ac81f5 | 15 | 211575352 |
| `C:\Antiphon\worktrees\card-task-c9cb7349` | ed67056c5e82389e95fdb82932f67396195a89f3 | 24 | 212701700 |
| `C:\Antiphon\worktrees\card-task-ca347801` | bce601bf589563a591fc7d3d360f36525a7a3a1d | 15 | 211610690 |
| `C:\Antiphon\worktrees\card-task-cbc38f6e` | c28a27087b38d2d55d191b0731eb8be3ce721e61 | 21 | 212685009 |
| `C:\Antiphon\worktrees\card-task-cc80b479` | 0096f7c5adcedc64fba0f2190d7c3f8bd09c0e74 | 4 | 209540143 |
| `C:\Antiphon\worktrees\card-task-ccbcfec4` | 728e04c07d21a43dc2c4483f5868f89a47067375 | 1 | 190066098 |
| `C:\Antiphon\worktrees\card-task-ccd13def` | d456b2d9146527a813ce721b52fc10cae53d7127 | 15 | 211619963 |
| `C:\Antiphon\worktrees\card-task-ccde064e` | afa651cf274b889b8daee7a9c8f9e7c0e1b58eac | 12 | 211139576 |
| `C:\Antiphon\worktrees\card-task-cd1c4f80` | 9333625c141016aed7241ef7b7ac935741d32843 | 3 | 209481781 |
| `C:\Antiphon\worktrees\card-task-cd2caf13` | ead93feb00fcaf8bf31018599437f17bee8e934c | 5 | 209676988 |
| `C:\Antiphon\worktrees\card-task-cddf06e9` | 3c273ba05cf38311dae7d9cbb5cffd40c4ec1fb3 | 5 | 209729104 |
| `C:\Antiphon\worktrees\card-task-cde14651` | 97e77f9f1fd6f71e2719bc661b48f8f89f129029 | 26 | 213026539 |
| `C:\Antiphon\worktrees\card-task-cf298805` | 41e63343b6449c6a97ee7c8df3bfe52da0354ea9 | 7 | 210558222 |
| `C:\Antiphon\worktrees\card-task-cf8b6982` | f5cab1275d9c0a635b9c942e5acf2367d9f36e8a | 1 | 192923983 |
| `C:\Antiphon\worktrees\card-task-cfe29503` | b2bee56345b5cb139a491053529ce27a4d4a8136 | 8 | 209714449 |
| `C:\Antiphon\worktrees\card-task-cffa0c4b` | 8d61900c9a98681a0c54d3f2aa6af1223d301a6d | 1 | 206468469 |
| `C:\Antiphon\worktrees\card-task-d092ee10` | ef52220e0fd4e8901ab3d53a376da6b4e37969f4 | 14 | 211442617 |
| `C:\Antiphon\worktrees\card-task-d0b57b9c` | c106f6dca4a6a7f13ae2b367ac3ce3bf850f4e96 | 24 | 212799759 |
| `C:\Antiphon\worktrees\card-task-d0ce3b58` | 1f79161b8fb16b6c23a6d9eab243ff9dc5cbd302 | 12 | 211147608 |
| `C:\Antiphon\worktrees\card-task-d26dbaaf` | 8b04a1123dfa84a1af5aeac9448ad0f66caf5623 | 22 | 212697704 |
| `C:\Antiphon\worktrees\card-task-d2864242` | b2bee56345b5cb139a491053529ce27a4d4a8136 | 8 | 209714449 |
| `C:\Antiphon\worktrees\card-task-d2c3674f` | 890f33d4002a2afb456c7a3c2de03af0f3b15a6d | 22 | 212693361 |
| `C:\Antiphon\worktrees\card-task-d340e01f` | 3c66ddd5465007908f6d752aea55a829205c0ac8 | 9 | 209826736 |
| `C:\Antiphon\worktrees\card-task-d4b80a8c` | 169ed60c103bddddd2f9afcfdf017e5da382b6e1 | 12 | 211140023 |
| `C:\Antiphon\worktrees\card-task-d63efd0a` | 9418c99f2e2747174ef804e4606213055f4ef8f3 | 1 | 190241760 |
| `C:\Antiphon\worktrees\card-task-d670e3ae` | 65862ec4b603786ff5eb79befdb00ca05455f570 | 7 | 209683055 |
| `C:\Antiphon\worktrees\card-task-d67c4ca2` | f9d52f91ec1b35c21f5d8d6f73b9c5a4e5859fc9 | 24 | 212719620 |
| `C:\Antiphon\worktrees\card-task-d6824038` | 679b696bd178e3c8bb14b32fcd04a1c26b148435 | 3 | 209466839 |
| `C:\Antiphon\worktrees\card-task-d6b0addf` | 265271be949d659f4fa7ba219453ce0f077a3068 | 15 | 211594942 |
| `C:\Antiphon\worktrees\card-task-d7614fb7` | 056f7ff59f6a67aff2a96073c6fa2ec60ba29cda | 11 | 210007548 |
| `C:\Antiphon\worktrees\card-task-d8072595` | adf7c4d2c9bcea9fbadf0b9cf61e5c84d2d022ff | 16 | 211594287 |
| `C:\Antiphon\worktrees\card-task-d84c5448` | 25e1d0f0e6e666f2152da631936f12a2c3a3bdf7 | 10 | 209866374 |
| `C:\Antiphon\worktrees\card-task-d86bdf31` | bedb350de80f2db29ae5ce8d362fff0131f8c53b | 13 | 211385026 |
| `C:\Antiphon\worktrees\card-task-d8d8a562` | d08bf72b657d9f9fcff61c63f241b0cb52e418a0 | 18 | 212236223 |
| `C:\Antiphon\worktrees\card-task-d94b0c85` | ffc8f8fc109f9520c24c8a0c277943911e850ad7 | 24 | 212762504 |
| `C:\Antiphon\worktrees\card-task-d954539b` | c534ea8a1ed015d2e172ae01720510a3d892bbc0 | 4 | 209550030 |
| `C:\Antiphon\worktrees\card-task-d99c29ee` | 962e3d0aabd1c8a51416d1cf4e3a55801d7b1f64 | 23 | 212704797 |
| `C:\Antiphon\worktrees\card-task-d9de1c95` | 253f2aba5febfd3d1cd0b7871864a90af73e09ab | 9 | 209953292 |
| `C:\Antiphon\worktrees\card-task-d9f29397` | 2421a7a50edf0f33c408774d45918e1f544e8c47 | 8 | 209795751 |
| `C:\Antiphon\worktrees\card-task-da0006c8` | 61dda8df4ebf0669c9f146493088687f41ef63c5 | 24 | 212707734 |
| `C:\Antiphon\worktrees\card-task-da1c6eeb` | a1c95a0144c5e9c2e0559c47ea0b15b29ca1d62f | 16 | 211604452 |
| `C:\Antiphon\worktrees\card-task-da2d3ae4` | 7ea938f31dcc7b33e8077e604c792e7b3bab6f2a | 5 | 209706307 |
| `C:\Antiphon\worktrees\card-task-da5dcdfe` | e216cc4af796ece143bfea1f9a1296fda33b0cb4 | 12 | 211127046 |
| `C:\Antiphon\worktrees\card-task-db2df8ea` | 0c71d13c5268883ac2fa83e41f16dbd14d742654 | 8 | 209811551 |
| `C:\Antiphon\worktrees\card-task-db8d32c0` | faf08c36a538e346e4c3cf49ba075e199b74ba96 | 17 | 211822698 |
| `C:\Antiphon\worktrees\card-task-dc569690` | 679b696bd178e3c8bb14b32fcd04a1c26b148435 | 20 | 209466839 |
| `C:\Antiphon\worktrees\card-task-dc60bbd7` | 6815f88b39f515e2c48b4521c31c7cb98800f84e | 26 | 213091595 |
| `C:\Antiphon\worktrees\card-task-dcfe6922` | 30cb5279bd4e471ba9ac67676f1ad5cad76a36bb | 12 | 211159679 |
| `C:\Antiphon\worktrees\card-task-dd1fb91b` | 41897f471a40107ce1d2378ed563d7a0b214bd2c | 13 | 211220843 |
| `C:\Antiphon\worktrees\card-task-dd90f1b7` | 2e7023b4280fe8e5d8630fd6a29cf169e7db2624 | 23 | 212709835 |
| `C:\Antiphon\worktrees\card-task-de901847` | 80fc0450afe44f5150996df8d01a5bb398ac1359 | 21 | 212687015 |
| `C:\Antiphon\worktrees\card-task-df8ec44a` | 7b920d84aae97139998f940ee2cce3f457bb6e70 | 17 | 211906163 |
| `C:\Antiphon\worktrees\card-task-e0c34d39` | 8b8dc6268aaac866f33b51dcb82a4fa29afa0dc1 | 12 | 211125962 |
| `C:\Antiphon\worktrees\card-task-e0de6690` | 01e0a62045111ea390a8865fd28bd8c52c08bd40 | 26 | 213068387 |
| `C:\Antiphon\worktrees\card-task-e28c82d0` | 591fafecbe85bb9c0cb65a94824e37dcd39892a5 | 26 | 213041052 |
| `C:\Antiphon\worktrees\card-task-e38d6427` | 1ae4ca380782735c4bc85bdb7b42d67c3cd37847 | 1 | 192959524 |
| `C:\Antiphon\worktrees\card-task-e418768c` | afa709e369a188b265f2b365038fd462db413aaa | 16 | 211631855 |
| `C:\Antiphon\worktrees\card-task-e52b2bed` | db58e77b4595598048cd8ab9cb2f6784eb556b9d | 11 | 211200541 |
| `C:\Antiphon\worktrees\card-task-e63ff4c2` | 33c80715e832ce04e272121ba0819deb81224e15 | 24 | 212717263 |
| `C:\Antiphon\worktrees\card-task-e6f6e5ac` | 80fc0450afe44f5150996df8d01a5bb398ac1359 | 22 | 212687015 |
| `C:\Antiphon\worktrees\card-task-e72099f7` | b208243f3eb59681387c5eaf3393360e7a5e1b0e | 9 | 209811002 |
| `C:\Antiphon\worktrees\card-task-e76d3792` | fe2c913757fa912e2c3b1af79d285d4f740d69a8 | 1 | 192596278 |
| `C:\Antiphon\worktrees\card-task-e7c70293` | e2cfadf36b928ffd0b3b9bbc3d492e4e83befc7b | 13 | 211197563 |
| `C:\Antiphon\worktrees\card-task-e7d4f999` | afcc1b26d6ba6b8c9c4ba6c3f2440ac18aa7dea5 | 10 | 209998819 |
| `C:\Antiphon\worktrees\card-task-e814f8ee` | 65194d01476a4a2ea4dfcfea05a44d86df2c39f1 | 26 | 213101486 |
| `C:\Antiphon\worktrees\card-task-e8784f40` | 4bd1473f0b59620db8832eb781a57a8d1da6e78c | 13 | 211370753 |
| `C:\Antiphon\worktrees\card-task-e87df8c7` | e56e4d1b6a727b3127b473307b860b7117980f09 | 25 | 212700612 |
| `C:\Antiphon\worktrees\card-task-e937b0c3` | 8ee214394e54ebad5050eb56ba41daa831eb5d00 | 17 | 211678191 |
| `C:\Antiphon\worktrees\card-task-ea320b29` | 289c06287db17459207a18d58e589d4a267975d4 | 4 | 209492886 |
| `C:\Antiphon\worktrees\card-task-ea7eebeb` | 93876186a4cda86120203b09cd18999bfc44a8cc | 10 | 210000915 |
| `C:\Antiphon\worktrees\card-task-eb43fc0e` | e47abeddbdd13c489bf8fda6573e0c684499407b | 18 | 212078580 |
| `C:\Antiphon\worktrees\card-task-ec6f244f` | 2921957643833588c201a9fcc0c84bd98676242e | 20 | 212558239 |
| `C:\Antiphon\worktrees\card-task-ece4f83a` | 6dbd129b791e0ca13b2cff1a0eb5930cd5af53e2 | 11 | 211125261 |
| `C:\Antiphon\worktrees\card-task-ecf8b748` | aaa1a7592ae74eee0a414e8e40dabe9fc8c53b0c | 15 | 211561916 |
| `C:\Antiphon\worktrees\card-task-ed18c421` | 2a5f324a0708ebefe59c7aad1485c51a968c2393 | 13 | 211148499 |
| `C:\Antiphon\worktrees\card-task-ed8199e4` | 3f6dfd0f1eb1dd8e50a6c23d8ecb025c2b9aef0f | 22 | 212696769 |
| `C:\Antiphon\worktrees\card-task-ee22148a` | a048b928d0f6982e56a3e1be82d88f8b868256aa | 1 | 190655301 |
| `C:\Antiphon\worktrees\card-task-eeee4db1` | b2ea37b4d981a2c75c8f97fd2c3e3475a8fbdcbb | 20 | 212618667 |
| `C:\Antiphon\worktrees\card-task-ef4c37b4` | 4cedfe0a2c9a244299f4b6f99468d5a767184ba3 | 23 | 212697234 |
| `C:\Antiphon\worktrees\card-task-ef6babe1` | fffc8dca1ba60ea88f3ba8914f19963f7bd49bfa | 1 | 194294442 |
| `C:\Antiphon\worktrees\card-task-f0717652` | eace3de1eb99fd6c6b690ca1f79d1ca2668482c0 | 15 | 211594700 |
| `C:\Antiphon\worktrees\card-task-f08f77e3` | e0a563b3d96ccd74b63f098f7460010fc4a99c2e | 5 | 209648598 |
| `C:\Antiphon\worktrees\card-task-f09d003e` | 680671c1df6ec598323be736817435635a82f120 | 5 | 209675507 |
| `C:\Antiphon\worktrees\card-task-f0ad975a` | 978cb913974abd52f10ffe3bc89cc03ad71acd23 | 4 | 209548060 |
| `C:\Antiphon\worktrees\card-task-f123dae7` | 61840ae4cede2ecf98d7a5f07fa578ae5b9828de | 22 | 212698448 |
| `C:\Antiphon\worktrees\card-task-f189bd64` | 5d37396b319e9880783e119809601187a037e583 | 6 | 209822309 |
| `C:\Antiphon\worktrees\card-task-f1a11dd3` | 97e77f9f1fd6f71e2719bc661b48f8f89f129029 | 25 | 213026539 |
| `C:\Antiphon\worktrees\card-task-f2d12082` | 7d2dfc7ecc48f942b445a98f3185a77b43c60d00 | 17 | 211644316 |
| `C:\Antiphon\worktrees\card-task-f3227c47` | 19d2b9858dd08f6908f7c46519f931111e4beb5f | 5 | 209533982 |
| `C:\Antiphon\worktrees\card-task-f3973a3a` | d9587915cd4a82edef4c75ac434d500bf103d02f | 10 | 211015636 |
| `C:\Antiphon\worktrees\card-task-f4830a56` | d6b174d6906dc8b4707af491577c5e29ac01ed8d | 19 | 212136921 |
| `C:\Antiphon\worktrees\card-task-f4c293bc` | 5c917b6676764cc89a1e77d406581c3cb674a9ce | 8 | 210620448 |
| `C:\Antiphon\worktrees\card-task-f4cf239f` | 2d0533c2817d3ce948fbceb34461031f4c6fe734 | 11 | 210000933 |
| `C:\Antiphon\worktrees\card-task-f5fcf533` | 58c89afa42d18c5d4e3317436c88b74def7652a2 | 8 | 209897427 |
| `C:\Antiphon\worktrees\card-task-f686ba8a` | 9cf4f74fa9c704b2e767fee8f0708df928330008 | 18 | 211939046 |
| `C:\Antiphon\worktrees\card-task-f6dfc56e` | 2ef25eaf8757e88f0113cbc2c6144dda8c999a7c | 1 | 190740734 |
| `C:\Antiphon\worktrees\card-task-f70595b8` | 3e3b238236419a38b38349a054546b0fff4fca15 | 2 | 194410331 |
| `C:\Antiphon\worktrees\card-task-f7273caf` | 3360b94d19ba57274e8d58d21e26941d6c915e43 | 1 | 190440406 |
| `C:\Antiphon\worktrees\card-task-f742a321` | 4d2b722d266babfeecb2d00285b318cc4d2b13d5 | 9 | 210633915 |
| `C:\Antiphon\worktrees\card-task-f84c519b` | d2d2afe140f4a38194f926b8291cd4d3159203e3 | 9 | 209772527 |
| `C:\Antiphon\worktrees\card-task-f8fefbc6` | 7a62b81d32154a151847940887f78cb442b595f4 | 8 | 209792199 |
| `C:\Antiphon\worktrees\card-task-f9696a82` | 62beb934d40f616d8a3e26482fb1a3f7deb00154 | 19 | 212095900 |
| `C:\Antiphon\worktrees\card-task-f9d4a394` | 330c600417eccc2fb7da9a4caea813c745f7142f | 1 | 206446401 |
| `C:\Antiphon\worktrees\card-task-fbb67b8e` | 3163976062d9107a2b01dbd6b161601edf123f1d | 19 | 212301562 |
| `C:\Antiphon\worktrees\card-task-fbd9be0e` | 81ad930de34c0a371a2e7d2e991351297e3f9678 | 4 | 209510867 |
| `C:\Antiphon\worktrees\card-task-fcf7ce80` | c4e7aa06e9f8c119f7dd59f52e38ed413293c9f2 | 17 | 211673076 |
| `C:\Antiphon\worktrees\card-task-fd701dd9` | f3794e0a98a1100063efd396d07ceb14a1efdb1d | 7 | 209722656 |
| `C:\Antiphon\worktrees\card-task-fda2f329` | fa431565179c5506be7632f9984c0c7b1e472ffc | 18 | 211703114 |
| `C:\Antiphon\worktrees\card-task-fe8ebbac` | 52a3b9ed410e6fd5bed0d49d9bf075380b276faa | 21 | 212685024 |
| `C:\Antiphon\worktrees\card-task-fed106e2` | d66f7a298063636e55726f0078e962cd9a1a458b | 4 | 209502500 |

## Evidence

External evidence root: `C:\Antiphon\evidence\desktop-cleanup-4b63abcc`. No generated evidence is staged or committed.

`cleanup.py` records the admission and deletion operations; `list.json` is the exact initial candidate list with completed row summaries, while `list-final.json` includes cap deferrals and concurrent arrivals. `rows/*.json` contains per-worktree Git/status/ignored-file/removal receipts. `batch-*.json` records throttle and free-space observations, and `batch-*-owners.json` contains the ownership exclusions. `tasks-requested.json`, `tasks-extended.json` and `agents-before.json` retain selected metadata only. `deleted-verification.json`, `registrations-after.json`, `prune.json`, `ref-drift.json`, and `final-state.json` record final checks.

| Evidence file | SHA-256 |
| --- | --- |
| cleanup.py | b2555d10ac4b09fed73604f1ae6c5bde61bbc14ec8377afd3fd332bfa503a69e |
| finalize.py | e4c3a43e9ca442a2d6afdc0bcb8e6b907f325cb2382c4034571e63338dab5fca |
| list.json | cf7f44ec6d128e500b59b4b991661534234a256a77d9ff5b4c77fa5d06c330a1 |
| list-final.json | 9e0bfea4275b7e5a776a4f1fd4c8741eaff883d42672ea057950f05dac635e01 |
| meta.json | 16185326383139f962c98a9f4ec0b858e4a8725066f1351ecf2a8782b774f1e1 |
| deleted-verification.json | 4bb65e8072d95d2c553ba07589ff51c44684aee9e55c75874b667f9739cbd5f9 |
| prune.json | 0094d84e54c4641f60fac246247b2965ec48d9197368503fc15273f334d04b91 |
| final-state.json | 168c112916aa6c55db329af54fcdbdb6752b9b6ee65bb47a9f32cb67923412b2 |
| ref-drift.json | aaf08970d50df4e889c95ba86b639a3e8968fdac95e07501722adee73629ba15 |
| api-recovery.json | 61abf5cecf4d9fd5f4014940bcd8ffed6e90a550bb6f154d6e22bd3f65fbe936 |

--- next stage ---
next: done
handoff: Cleanup pass complete within its deletion and throttle limits; held and deferred paths remain for a later authorized pass.
artifact: docs/investigations/2026-11-05-desktop-worktree-cleanup-2.md
