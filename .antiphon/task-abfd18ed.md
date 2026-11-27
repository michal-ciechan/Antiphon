# Server2 disk incident, 2026-11-27

The cleanup removed 325 paths with 54,420,221,952 measured allocated bytes. The post-cleanup sample had 128,911,204,352 bytes available at 83% usage.

| Sample | Used bytes | Available bytes | Usage |
| --- | --- | --- | --- |
| 08:37 UTC, after the incident builder prune | 658,181,447,680 | 67,642,425,344 | 91% |
| Fri Nov 27 09:16:31 UTC 2026 | 596,912,668,672 | 128,911,204,352 | 83% |

/work and /tmp are mounts from the same root filesystem. Total filesystem capacity was 756,634,804,224 bytes. The filesystem change includes writes and releases by other work on the host. The path ledger records allocations measured before removal. After the checkpoint tool build, 126,899,109,888 bytes remained available at 83% usage.

## Deletions

| Category | Paths | Allocated bytes |
| --- | --- | --- |
| build-output | 291 | 39,983,398,912 |
| evidence-build-output | 8 | 9,735,028,736 |
| expired-evidence | 1 | 236,670,976 |
| test-fixture | 25 | 4,465,123,328 |

The top ten paths are below. The full ledger follows the census.

| Allocated bytes | Exact path |
| --- | --- |
| 1,231,708,160 | `/work/review-evidence/task-cc129403/base-source/tests/Antiphon.Tests/bin-cc129403-base` |
| 1,231,384,576 | `/tmp/antiphon-review-a2668d12-8T7zUfp3/base/tests/Antiphon.Tests/bin-base-a266` |
| 1,230,917,632 | `/work/review-evidence/task-efd29e5f/base-source/tests/Antiphon.Tests/bin-efd29e5f-base` |
| 1,230,868,480 | `/work/review-evidence/task-efd29e5f/red-source/tests/Antiphon.Tests/bin-efd29e5f-red` |
| 1,222,455,296 | `/work/review-evidence/task-379afc29/base-source/tests/Antiphon.Tests/bin-g138b-base-379afc29` |
| 1,222,377,472 | `/work/review-evidence/task-379afc29/red-source/tests/Antiphon.Tests/bin-g138b-old-379afc29` |
| 1,215,131,648 | `/work/evidence/task-fe7f88e7/master-base/tests/Antiphon.Tests/bin-c1842-s2` |
| 1,190,801,408 | `/work/evidence/task-fe7f88e7/rc/tests/Antiphon.Tests/bin-c1842-s2` |
| 1,190,768,640 | `/work/evidence/task-072b9661/rc/tests/Antiphon.Tests/bin-g39` |
| 1,021,259,776 | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tests/Antiphon.Tests/bin` |

Whole worktree removals and Docker volume removals both totaled zero.

Build directories belonged to settled task worktrees, scratch checkouts or aged evidence on previous runs. The evidence check used the owning task and its card discussion, board search, Git references and active task and landing scopes. Only identified test fixture producer families entered the temporary fixture pass. The PowerShell apply loops checked exact paths, age, ownership, protected descendants, mount points and visible process holders. Root-owned supervisors were outside the runner uid process view. Whole-tree closure stayed with the existing worktree retention endpoint, which refused this task token with HTTP 403 caller_scope.

Two bounded passes stopped at their time limits. The ledger contains completed removals that were checked absent. A subsequent pass retried large held paths after the holder check cleared and collected dated build outputs from settled evidence.

## Initial runner census

Allocated bytes are from bounded du samples. Age buckets classify immediate child directories by their directory mtime. Deep file age was checked separately before deletion. Flat metadata and direct files account for residual differences from the directory buckets. /tmp is a lower bound because fixture-owned directories were unreadable. The total /work census was 213,357,674,496 bytes.

| Path | Total bytes | >72 h | 24–72 h | <24 h |
| --- | --- | --- | --- | --- |
| `/work/worktrees` | 79,445,196,800 | 29,706,579,968 | 1,552,961,536 | 48,185,626,624 |
| `/work/review-evidence` | 70,980,313,088 | 15,189,336,064 | 20,317,593,600 | 35,473,379,328 |
| `/work/code-evidence` | 34,554,302,464 | 3,075,022,848 | 1,707,069,440 | 29,772,206,080 |
| `/work/evidence` | 18,937,679,872 | 7,294,418,944 | 39,329,792 | 11,603,927,040 |
| `/work/merge-evidence` | 4,546,162,688 | 97,718,272 | 3,675,185,152 | 773,255,168 |
| `/work/retained` | 4,182,286,336 | 2,105,126,912 | 1,373,118,464 | 704,000,000 |
| `/work/repos` | 631,910,400 | 631,906,304 | 0 | 0 |
| `/work/test-evidence` | 70,184,960 | 70,176,768 | 0 | 0 |
| `/work/cache` | 393,216 | 389,120 | 0 | 0 |
| `/work/s5a-mut-hash-9afb7a90` | 81,920 | 77,824 | 0 | 0 |
| `/work/s5a-mut-timeout-9afb7a90` | 49,152 | 45,056 | 0 | 0 |
| `/work/s5a-mut-bound-9afb7a90` | 49,152 | 45,056 | 0 | 0 |
| `/work/workspace-bindings` | 9,007,104 | — | — | — |
| `/work/workspace-slots` | 49,152 | — | — | — |
| `/work/worktrees-retiring` | 4,096 | — | — | — |
| `/tmp` | 195,379,630,080 | 150,780,649,472 | 18,149,691,392 | 25,045,884,928 |

### Largest task evidence

| Allocated bytes | Exact path |
| --- | --- |
| 16,335,347,712 | `/work/code-evidence/task-f7313ad4` |
| 9,448,255,488 | `/work/code-evidence/task-0a9d6cee` |
| 8,975,998,976 | `/work/review-evidence/task-204055ca` |
| 7,480,532,992 | `/work/review-evidence/task-6c753207` |
| 7,234,904,064 | `/work/review-evidence/task-3d0d4674` |
| 7,104,843,776 | `/work/review-evidence/task-b9e53333` |
| 7,097,774,080 | `/work/evidence/task-c2b1cd64` |
| 5,499,977,728 | `/work/review-evidence/7280f7d0-c0e4-4168-a360-a1bce7541fdf` |
| 4,060,782,592 | `/work/review-evidence/task-8b2639b7` |
| 3,827,412,992 | `/work/review-evidence/task-dd57df06` |
| 3,698,057,216 | `/work/code-evidence/task-d335ece1` |
| 3,257,544,704 | `/work/review-evidence/a34bcfae-88dc-40a8-8f6b-facffb43f051` |
| 3,256,590,336 | `/work/review-evidence/task-6ea7f02f` |
| 3,246,280,704 | `/work/merge-evidence/task-3a44eb7e-red-pin` |
| 3,103,756,288 | `/work/review-evidence/task-379afc29` |

### Growth attribution

The incident record reports roughly +98 GiB of filesystem usage over 22 hours. Before pruning, Docker reported 56.69 GB of build cache across 929 entries. The reported filesystem use fell from 676 GiB to 613 GiB across the prune interval. The seven biggest newly created evidence directories in the runner census were born within the growth window and occupied 63,677,657,088 bytes at census time. These are independent endpoint observations. Concurrent deletion, allocation, volume accounting and unit differences prevent a complete reconciliation of the 98 GiB delta.

Code task-f7313ad4 occupied 16.34 GB. Its two largest qualification source trees contained 12.29 GB. Review task-204055ca occupied 8.98 GB, of which base-source, red-source and tip-source held 8.30 GB. One project within base-source alone held 1.09 GB in bin and 124 MB in obj. Review task-3d0d4674 similarly stored built master, red and candidate trees. The filesystem evidence points to repeated built snapshots as the dominant measured contributor inside these folders.

### Docker ownership

The incident baseline for the outer daemon was 34 local volumes, 32 active, 489.8 GB total and 76 MB reclaimable. It also reported 31.8 GB of images and 59.4 GB of container writable layers. The runner Docker context points to its nested daemon. An outer-host SSH read failed strict host-key verification, so individual outer volumes and host JSON log sizes could not be verified from this runner. The visible outer mounts were inventoried by name and owner below.

| Outer volume or configured owner | Measured allocated bytes | Contents |
| --- | --- | --- |
| `antiphon-runner_work` | 213,357,674,496 | Runner worktrees and external evidence |
| `antiphon-runner_runner-tmp` | 195,379,630,080 | Runner temporary filesystem, partial permissions |
| `antiphon-runner_runner-state` | 43,280,105,472 | Runner state, credential and provider-home exclusions |
| `antiphon-runner-cache-nuget-packages` | 4,910,858,240 | Shared NuGet packages |
| `antiphon-runner-cache-npm-content` | 1,518,538,752 | Shared npm content |
| `antiphon-runner-cache-nuget-scratch` | 40,755,200 | Shared NuGet scratch |
| `antiphon-runner_dind-data` | Unavailable | Nested Docker images, cache, container data and volumes |
| `antiphon-server2-postgres_pgdata` | Unavailable | Production Postgres, configured name |
| `antiphon-server2-server_work` | Unavailable | Server work, configured name |
| `antiphon-server2-server_server-state` | Unavailable | Server state, configured name |
| `antiphon-server2-server_runner-state` | Unavailable | Server sidecar runner state, configured name |

Nested Docker reported the following 21 local volumes. Its JSON log paths were unreadable by the app uid. The outer volume and log census needs a read from the trusted host context, including the check for any single JSON log above 1 GB.

| Nested volume | Links | Size | Owner |
| --- | --- | --- | --- |
| `45c39ba34cb04175640c0bb3ee598444f8b6d45839b2494d9d734918d10aab07` | 0 | 40.2MB | Anonymous nested test volume, owner unavailable |
| `4ac6ffce6a2b244ed1561873cc37bb1039ba107406dcf84e9cdfec23a3774a25` | 1 | 381.8MB | Nested test Postgres |
| `4f0ba7691e6c92d21cc0108ce74d54cdcc31fbd7971ea18da387626eb6506a0f` | 0 | 55.82MB | Anonymous nested test volume, owner unavailable |
| `b19c359493b9736c6cf1388d5c6e88f5062dcfa3c82b004649e8b3c613c3a17b` | 0 | 884.5MB | Anonymous nested test volume, owner unavailable |
| `c1609rda1b88c089c722bb7-n1-5328c1b1cache-nuget-scratch` | 0 | 54B | Nested C1609 retirement fixture |
| `c1609rda1b88c089c722bb7-n1-5328c1b1sentinel` | 0 | 43B | Nested C1609 retirement fixture |
| `fc85155e17ae6961cc8a7cf9d7572ab54f205682828f70f280c2b2df6ea69681` | 0 | 40.2MB | Anonymous nested test volume, owner unavailable |
| `c1609rda1b88c089c722bb7-n1-5328c1b1main_dind-data` | 0 | 49B | Nested C1609 retirement fixture |
| `c1609rda1b88c089c722bb7-n1-5328c1b1cache-npm-content` | 0 | 52B | Nested C1609 retirement fixture |
| `c1609rda1b88c089c722bb7-n1-5328c1b1main_runner-tmp` | 0 | 50B | Nested C1609 retirement fixture |
| `c1609rda1b88c089c722bb7-n1-5328c1b1main_work` | 0 | 44B | Nested C1609 retirement fixture |
| `c1609rda1b88c089c722bb7-n1-5328c1b1temp_runner-state` | 0 | 52B | Nested C1609 retirement fixture |
| `c1609rda1b88c089c722bb7-n1-5328c1b1temp_work` | 0 | 27.54kB | Nested C1609 retirement fixture |
| `fe350ca3eb31703d760e21bf5327f6bc3ff6aa82a7002a21a193e96c3945cedd` | 0 | 0B | Anonymous nested test volume, owner unavailable |
| `63c883bb5a1b83eaa91cae99c42698bc7acd3ff6a80034f166f1f8758d95ee4d` | 0 | 130.4MB | Anonymous nested test volume, owner unavailable |
| `711e05d97bab650cef427b267ff7e7d3037f9094cb05c1834860bbcba0062630` | 0 | 0B | Anonymous nested test volume, owner unavailable |
| `967c130666e69837f047a8b790dcb475c7cf9464e06a6632425611552f8f2c10` | 0 | 0B | Anonymous nested test volume, owner unavailable |
| `c1609rda1b88c089c722bb7-n1-5328c1b1cache-nuget-packages` | 0 | 55B | Nested C1609 retirement fixture |
| `c1609rda1b88c089c722bb7-n1-5328c1b1main_runner-state` | 0 | 52B | Nested C1609 retirement fixture |
| `c1609rda1b88c089c722bb7-n1-5328c1b1temp_dind-data` | 0 | 49B | Nested C1609 retirement fixture |
| `c1609rda1b88c089c722bb7-n1-5328c1b1temp_runner-tmp` | 0 | 65B | Nested C1609 retirement fixture |
| `ddd23827f81877cc03f7d1c389b671fda56e721c640e0881865566f9f5e87b97` | 0 | 0B | Anonymous nested test volume, owner unavailable |

## Code handoff

- The checkpoint entry returns exit 9 with workspace_disk_low below 20 GiB available on a checked filesystem. The dispatcher keeps new tasks queued when the configured runner volume has a fresh low-space reading. The existing WorkspaceDiskLow attention item supplies the 85% warning.
- Server2 builds use the task workspace or scratch. MSBuild rejects project and standard output paths inside the evidence roots. The sweep expires eligible released evidence after at least 72 hours, with a preview default, a per-tick bound, and task, board, Git and filesystem checks.
- The systemd timer runs the filtered builder prune hourly on the outer host after installation. The evidence sweep and timer are staged for Review and operator activation.

### Verification

- The build-slotted launcher witness executed one test on `9c10363af9af2c1f0347d8441cddbe560dc58776`. The expected red was a missing workspace_disk_low diagnostic before dotnet startup.
- The same selection executed one test on `5a189a419bc6964711a6e752c60235263aab5511`. It passed the low-space, boundary and recovery assertions.
- The leased server build at `097915ba99d23cae6e989461dc1f498c28af310f` reported 0 errors and 43 warnings. The server source is identical at the published code tip.
- The leased checkpoint tool build at `cfdf502c2e4b4f6877a3ec9c3b2636047f1054a9` reported 0 errors and 61 warnings.
- The evidence sweep preview reported would-remove for `/work/evidence/task-ff3ef62e` and kept active-reference for `/work/review-evidence/task-b72ac3e7`.

## Complete deletion ledger

| Allocated bytes | Category | Task prefix or test owner | Exact path |
| --- | --- | --- | --- |
| 1,231,708,160 | evidence-build-output | `cc129403` | `/work/review-evidence/task-cc129403/base-source/tests/Antiphon.Tests/bin-cc129403-base` |
| 1,231,384,576 | build-output | `a2668d12` | `/tmp/antiphon-review-a2668d12-8T7zUfp3/base/tests/Antiphon.Tests/bin-base-a266` |
| 1,230,917,632 | evidence-build-output | `efd29e5f` | `/work/review-evidence/task-efd29e5f/base-source/tests/Antiphon.Tests/bin-efd29e5f-base` |
| 1,230,868,480 | evidence-build-output | `efd29e5f` | `/work/review-evidence/task-efd29e5f/red-source/tests/Antiphon.Tests/bin-efd29e5f-red` |
| 1,222,455,296 | evidence-build-output | `379afc29` | `/work/review-evidence/task-379afc29/base-source/tests/Antiphon.Tests/bin-g138b-base-379afc29` |
| 1,222,377,472 | evidence-build-output | `379afc29` | `/work/review-evidence/task-379afc29/red-source/tests/Antiphon.Tests/bin-g138b-old-379afc29` |
| 1,215,131,648 | evidence-build-output | `fe7f88e7` | `/work/evidence/task-fe7f88e7/master-base/tests/Antiphon.Tests/bin-c1842-s2` |
| 1,190,801,408 | evidence-build-output | `fe7f88e7` | `/work/evidence/task-fe7f88e7/rc/tests/Antiphon.Tests/bin-c1842-s2` |
| 1,190,768,640 | evidence-build-output | `072b9661` | `/work/evidence/task-072b9661/rc/tests/Antiphon.Tests/bin-g39` |
| 1,021,259,776 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tests/Antiphon.Tests/bin` |
| 1,020,461,056 | build-output | `56e21b88` | `/tmp/antiphon-review-56e21b88-base.f3Hi6LS0/checkout/tests/Antiphon.Tests/bin` |
| 1,010,708,480 | build-output | `0b230b01` | `/tmp/c1514-review-0b230b01.SzFC3M/base-1714/tests/Antiphon.Tests/bin` |
| 1,010,688,000 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/tests/Antiphon.Tests/bin` |
| 1,006,186,496 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/tests/Antiphon.Tests/bin` |
| 1,006,182,400 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/tests/Antiphon.Tests/bin` |
| 1,006,178,304 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/tests/Antiphon.Tests/bin` |
| 1,006,084,096 | build-output | `4fa334d8` | `/tmp/graph-merge-4fa334d8-b/tests/Antiphon.Tests/bin` |
| 1,006,022,656 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/tests/Antiphon.Tests/bin` |
| 1,006,002,176 | build-output | `4fa334d8` | `/tmp/graph-merge-4fa334d8/tests/Antiphon.Tests/bin` |
| 1,005,961,216 | build-output | `df54fc26` | `/tmp/antiphon-df54fc26-master/tests/Antiphon.Tests/bin` |
| 963,375,104 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/tests/Antiphon.Tests/bin` |
| 961,802,240 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/tests/Antiphon.Tests/bin` |
| 961,695,744 | build-output | `18072e35` | `/tmp/review-18072e35-master/tests/Antiphon.Tests/bin` |
| 961,302,528 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/tests/Antiphon.Tests/bin` |
| 961,036,288 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/tests/Antiphon.Tests/bin` |
| 960,933,888 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/tests/Antiphon.Tests/bin` |
| 960,733,184 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/tests/Antiphon.Tests/bin` |
| 587,898,880 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-cbd9755059484dcda9704f87eda3cc25` |
| 587,886,592 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-d76e8e3248724fb99c7879c76b2c9519` |
| 587,870,208 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-1c781a619d304894a89d1f37103d4e9c` |
| 587,870,208 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-f78bb2c04f89444ba7244c579de044ae` |
| 587,853,824 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-38e7185c4b9a4c549901d8b3f6046f97` |
| 587,845,632 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-d65478caa526415c89453b39a6d74dac` |
| 587,808,768 | test-fixture | `antiphon-c514-pty` | `/tmp/antiphon-c514-pty-00ee148a5a94485e9ee94c500109ffde` |
| 236,670,976 | expired-evidence | `619682aa` | `/work/review-evidence/task-619682aa` |
| 183,562,240 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/server/bin` |
| 183,439,360 | build-output | `56e21b88` | `/tmp/antiphon-review-56e21b88-base.f3Hi6LS0/checkout/server/bin` |
| 181,874,688 | build-output | `0b230b01` | `/tmp/c1514-review-0b230b01.SzFC3M/base-1714/server/bin` |
| 181,874,688 | build-output | `4fa334d8` | `/tmp/graph-merge-4fa334d8-b/server/bin` |
| 181,874,688 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/server/bin` |
| 181,874,688 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/server/bin` |
| 181,870,592 | build-output | `df54fc26` | `/tmp/antiphon-df54fc26-master/server/bin` |
| 181,870,592 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/server/bin` |
| 181,870,592 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/server/bin` |
| 181,870,592 | build-output | `4fa334d8` | `/tmp/graph-merge-4fa334d8/server/bin` |
| 181,870,592 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/server/bin` |
| 181,149,696 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/server/bin` |
| 180,920,320 | build-output | `18072e35` | `/tmp/review-18072e35-master/server/bin` |
| 180,916,224 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/server/bin` |
| 180,879,360 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/server/bin` |
| 180,862,976 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/server/bin` |
| 180,858,880 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/server/bin` |
| 180,793,344 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/server/bin` |
| 175,505,408 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 175,378,432 | build-output | `56e21b88` | `/tmp/antiphon-review-56e21b88-base.f3Hi6LS0/checkout/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 173,809,664 | build-output | `0b230b01` | `/tmp/c1514-review-0b230b01.SzFC3M/base-1714/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 173,809,664 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 173,805,568 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 173,805,568 | build-output | `4fa334d8` | `/tmp/graph-merge-4fa334d8-b/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 173,805,568 | build-output | `4fa334d8` | `/tmp/graph-merge-4fa334d8/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 173,805,568 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 173,801,472 | build-output | `df54fc26` | `/tmp/antiphon-df54fc26-master/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 173,801,472 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 173,801,472 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 173,719,552 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tests/Antiphon.DockerStack.Fixture/bin` |
| 173,596,672 | build-output | `56e21b88` | `/tmp/antiphon-review-56e21b88-base.f3Hi6LS0/checkout/tests/Antiphon.DockerStack.Fixture/bin` |
| 172,032,000 | build-output | `0b230b01` | `/tmp/c1514-review-0b230b01.SzFC3M/base-1714/tests/Antiphon.DockerStack.Fixture/bin` |
| 172,032,000 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/tests/Antiphon.DockerStack.Fixture/bin` |
| 172,032,000 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/tests/Antiphon.DockerStack.Fixture/bin` |
| 172,032,000 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/tests/Antiphon.DockerStack.Fixture/bin` |
| 172,027,904 | build-output | `df54fc26` | `/tmp/antiphon-df54fc26-master/tests/Antiphon.DockerStack.Fixture/bin` |
| 172,027,904 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/tests/Antiphon.DockerStack.Fixture/bin` |
| 172,027,904 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/tests/Antiphon.DockerStack.Fixture/bin` |
| 172,027,904 | build-output | `4fa334d8` | `/tmp/graph-merge-4fa334d8-b/tests/Antiphon.DockerStack.Fixture/bin` |
| 172,027,904 | build-output | `4fa334d8` | `/tmp/graph-merge-4fa334d8/tests/Antiphon.DockerStack.Fixture/bin` |
| 171,307,008 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/tests/Antiphon.DockerStack.Fixture/bin` |
| 171,073,536 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/tests/Antiphon.DockerStack.Fixture/bin` |
| 171,073,536 | build-output | `18072e35` | `/tmp/review-18072e35-master/tests/Antiphon.DockerStack.Fixture/bin` |
| 171,036,672 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/tests/Antiphon.DockerStack.Fixture/bin` |
| 171,012,096 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/tests/Antiphon.DockerStack.Fixture/bin` |
| 170,983,424 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/tests/Antiphon.DockerStack.Fixture/bin` |
| 170,946,560 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/tests/Antiphon.DockerStack.Fixture/bin` |
| 163,151,872 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tests/Antiphon.HostLoad.Probe/bin` |
| 163,028,992 | build-output | `56e21b88` | `/tmp/antiphon-review-56e21b88-base.f3Hi6LS0/checkout/tests/Antiphon.HostLoad.Probe/bin` |
| 161,468,416 | build-output | `0b230b01` | `/tmp/c1514-review-0b230b01.SzFC3M/base-1714/tests/Antiphon.HostLoad.Probe/bin` |
| 161,464,320 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/tests/Antiphon.HostLoad.Probe/bin` |
| 161,464,320 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/tests/Antiphon.HostLoad.Probe/bin` |
| 161,460,224 | build-output | `df54fc26` | `/tmp/antiphon-df54fc26-master/tests/Antiphon.HostLoad.Probe/bin` |
| 161,460,224 | build-output | `4fa334d8` | `/tmp/graph-merge-4fa334d8-b/tests/Antiphon.HostLoad.Probe/bin` |
| 161,460,224 | build-output | `4fa334d8` | `/tmp/graph-merge-4fa334d8/tests/Antiphon.HostLoad.Probe/bin` |
| 161,456,128 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/tests/Antiphon.HostLoad.Probe/bin` |
| 161,456,128 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/tests/Antiphon.HostLoad.Probe/bin` |
| 161,456,128 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/tests/Antiphon.HostLoad.Probe/bin` |
| 160,698,368 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/tests/Antiphon.HostLoad.Probe/bin` |
| 160,456,704 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/tests/Antiphon.HostLoad.Probe/bin` |
| 160,452,608 | build-output | `18072e35` | `/tmp/review-18072e35-master/tests/Antiphon.HostLoad.Probe/bin` |
| 160,337,920 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/tests/Antiphon.HostLoad.Probe/bin` |
| 160,317,440 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/tests/Antiphon.HostLoad.Probe/bin` |
| 160,296,960 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/tests/Antiphon.HostLoad.Probe/bin` |
| 160,256,000 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/tests/Antiphon.HostLoad.Probe/bin` |
| 157,851,648 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 157,614,080 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 157,614,080 | build-output | `18072e35` | `/tmp/review-18072e35-master/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 157,577,216 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 157,556,736 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 157,532,160 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 157,503,488 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/tests/Antiphon.ChannelOutbound.Probe/bin` |
| 144,273,408 | build-output | `a2668d12` | `/tmp/antiphon-review-a2668d12-8T7zUfp3/base/tests/Antiphon.SessionRunner.Tests/bin-base-runner-a266` |
| 119,197,696 | build-output | `56e21b88` | `/tmp/antiphon-review-56e21b88-base.f3Hi6LS0/checkout/tests/Antiphon.Tests/obj` |
| 119,021,568 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tests/Antiphon.Tests/obj` |
| 116,604,928 | build-output | `0b230b01` | `/tmp/c1514-review-0b230b01.SzFC3M/base-1714/tests/Antiphon.Tests/obj` |
| 115,945,472 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/tests/Antiphon.Tests/obj` |
| 114,917,376 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/tests/Antiphon.Tests/obj` |
| 114,495,488 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/tests/Antiphon.Tests/obj` |
| 114,270,208 | build-output | `df54fc26` | `/tmp/antiphon-df54fc26-master/tests/Antiphon.Tests/obj` |
| 114,237,440 | build-output | `4fa334d8` | `/tmp/graph-merge-4fa334d8-b/tests/Antiphon.Tests/obj` |
| 114,237,440 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/tests/Antiphon.Tests/obj` |
| 114,200,576 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/tests/Antiphon.Tests/obj` |
| 114,089,984 | build-output | `4fa334d8` | `/tmp/graph-merge-4fa334d8/tests/Antiphon.Tests/obj` |
| 112,132,096 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/tests/Antiphon.Tests/obj` |
| 110,948,352 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/tests/Antiphon.Tests/obj` |
| 110,764,032 | build-output | `18072e35` | `/tmp/review-18072e35-master/tests/Antiphon.Tests/obj` |
| 110,620,672 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/tests/Antiphon.Tests/obj` |
| 110,616,576 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/tests/Antiphon.Tests/obj` |
| 110,555,136 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/tests/Antiphon.Tests/obj` |
| 110,112,768 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/tests/Antiphon.Tests/obj` |
| 97,861,632 | build-output | `f9d27fc2` | `/tmp/f9d27fc2-review-myXGymje/red/tests/Antiphon.SessionRunner.Tests/bin` |
| 92,631,040 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 92,610,560 | build-output | `56e21b88` | `/tmp/antiphon-review-56e21b88-base.f3Hi6LS0/checkout/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 92,602,368 | build-output | `df54fc26` | `/tmp/antiphon-df54fc26-master/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 92,602,368 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 92,602,368 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 92,602,368 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 92,602,368 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 92,602,368 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 69,718,016 | build-output | `18072e35` | `/tmp/review-18072e35-master/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 69,709,824 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 69,709,824 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 69,709,824 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 69,697,536 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 69,693,440 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 69,689,344 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/tests/Antiphon.Checkpoints.LifecycleHost/bin` |
| 58,712,064 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/server/obj` |
| 56,217,600 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/server/obj` |
| 56,217,600 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/server/obj` |
| 56,213,504 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/server/obj` |
| 56,213,504 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/server/obj` |
| 56,209,408 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/server/obj` |
| 55,230,464 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/server/obj` |
| 54,935,552 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/server/obj` |
| 54,915,072 | build-output | `18072e35` | `/tmp/review-18072e35-master/server/obj` |
| 54,906,880 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/server/obj` |
| 54,861,824 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/server/obj` |
| 54,853,632 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/server/obj` |
| 48,656,384 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/tests/Antiphon.TestTiming.Probe/bin` |
| 48,652,288 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/tests/Antiphon.TestTiming.Probe/bin` |
| 48,652,288 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/tests/Antiphon.TestTiming.Probe/bin` |
| 48,652,288 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tests/Antiphon.TestTiming.Probe/bin` |
| 48,652,288 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/tests/Antiphon.TestTiming.Probe/bin` |
| 48,652,288 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/tests/Antiphon.TestTiming.Probe/bin` |
| 48,652,288 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/tests/Antiphon.TestTiming.Probe/bin` |
| 48,652,288 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/tests/Antiphon.TestTiming.Probe/bin` |
| 48,652,288 | build-output | `18072e35` | `/tmp/review-18072e35-master/tests/Antiphon.TestTiming.Probe/bin` |
| 48,652,288 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/tests/Antiphon.TestTiming.Probe/bin` |
| 48,652,288 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/tests/Antiphon.TestTiming.Probe/bin` |
| 48,652,288 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/tests/Antiphon.TestTiming.Probe/bin` |
| 48,652,288 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/tests/Antiphon.TestTiming.Probe/bin` |
| 48,615,424 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,611,328 | build-output | `18072e35` | `/tmp/review-18072e35-master/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,607,232 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,607,232 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,607,232 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,607,232 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,607,232 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,607,232 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,607,232 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,607,232 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,607,232 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,607,232 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,607,232 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/tests/Antiphon.NativeLinuxPolicy.Probe/bin` |
| 48,582,656 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,582,656 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,582,656 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,582,656 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,582,656 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,582,656 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,582,656 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,582,656 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,582,656 | build-output | `18072e35` | `/tmp/review-18072e35-master/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,582,656 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,582,656 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,582,656 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,582,656 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/tests/Antiphon.Checkpoints.RepeatHost/bin` |
| 48,562,176 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 48,558,080 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 48,558,080 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 48,558,080 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 48,558,080 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 48,558,080 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 48,558,080 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 48,558,080 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 48,558,080 | build-output | `18072e35` | `/tmp/review-18072e35-master/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 48,558,080 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 48,558,080 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 48,558,080 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 48,558,080 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/tests/Antiphon.Checkpoints.FilterHost/bin` |
| 44,048,384 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tools/Antiphon.Checkpoints/bin` |
| 44,019,712 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/tools/Antiphon.Checkpoints/bin` |
| 44,019,712 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/tools/Antiphon.Checkpoints/bin` |
| 44,019,712 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/tools/Antiphon.Checkpoints/bin` |
| 44,019,712 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/tools/Antiphon.Checkpoints/bin` |
| 44,019,712 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/tools/Antiphon.Checkpoints/bin` |
| 36,900,864 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/src/Antiphon.NightlyWatchdog/bin` |
| 36,900,864 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/src/Antiphon.NightlyWatchdog/bin` |
| 36,900,864 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/src/Antiphon.NightlyWatchdog/bin` |
| 36,900,864 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/src/Antiphon.NightlyWatchdog/bin` |
| 36,900,864 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/src/Antiphon.NightlyWatchdog/bin` |
| 36,900,864 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/src/Antiphon.NightlyWatchdog/bin` |
| 36,900,864 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/src/Antiphon.NightlyWatchdog/bin` |
| 36,900,864 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/src/Antiphon.NightlyWatchdog/bin` |
| 36,900,864 | build-output | `18072e35` | `/tmp/review-18072e35-master/src/Antiphon.NightlyWatchdog/bin` |
| 36,900,864 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/src/Antiphon.NightlyWatchdog/bin` |
| 36,900,864 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/src/Antiphon.NightlyWatchdog/bin` |
| 36,900,864 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/src/Antiphon.NightlyWatchdog/bin` |
| 36,900,864 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/src/Antiphon.NightlyWatchdog/bin` |
| 32,772,096 | build-output | `f9d27fc2` | `/tmp/f9d27fc2-review-myXGymje/red/src/Antiphon.SessionRunner/bin` |
| 32,620,544 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/src/Antiphon.SessionRunner/bin` |
| 32,600,064 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/src/Antiphon.SessionRunner/bin` |
| 32,600,064 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/src/Antiphon.SessionRunner/bin` |
| 32,600,064 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/src/Antiphon.SessionRunner/bin` |
| 32,600,064 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/src/Antiphon.SessionRunner/bin` |
| 32,600,064 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/src/Antiphon.SessionRunner/bin` |
| 32,542,720 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/src/Antiphon.SessionRunner/bin` |
| 32,538,624 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/src/Antiphon.SessionRunner/bin` |
| 32,538,624 | build-output | `18072e35` | `/tmp/review-18072e35-master/src/Antiphon.SessionRunner/bin` |
| 32,444,416 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/src/Antiphon.SessionRunner/bin` |
| 32,440,320 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/src/Antiphon.SessionRunner/bin` |
| 32,440,320 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/src/Antiphon.SessionRunner/bin` |
| 32,440,320 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/src/Antiphon.SessionRunner/bin` |
| 21,082,112 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/tools/Antiphon.Checkpoints/bin` |
| 21,082,112 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/tools/Antiphon.Checkpoints/bin` |
| 21,082,112 | build-output | `18072e35` | `/tmp/review-18072e35-master/tools/Antiphon.Checkpoints/bin` |
| 21,082,112 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/tools/Antiphon.Checkpoints/bin` |
| 21,065,728 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/tools/Antiphon.Checkpoints/bin` |
| 21,061,632 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/tools/Antiphon.Checkpoints/bin` |
| 21,061,632 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/tools/Antiphon.Checkpoints/bin` |
| 19,525,632 | build-output | `c0b3cb9c` | `/tmp/antiphon-review-c0b3cb9c/probe/bin` |
| 19,509,248 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-case-5c78b016/scripts/regenerate-derived-impact-index/bin` |
| 19,509,248 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-merged-5c78b016/scripts/regenerate-derived-impact-index/bin` |
| 19,509,248 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/scripts/regenerate-derived-impact-index/bin` |
| 19,509,248 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/scripts/regenerate-derived-impact-index/bin` |
| 19,509,248 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/scripts/regenerate-derived-impact-index/bin` |
| 19,509,248 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/scripts/regenerate-derived-impact-index/bin` |
| 19,509,248 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/scripts/regenerate-derived-impact-index/bin` |
| 19,509,248 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/scripts/regenerate-derived-impact-index/bin` |
| 19,509,248 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/scripts/regenerate-derived-impact-index/bin` |
| 19,496,960 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-33a1d2d12bbf45edae32a16158c19ffe` |
| 19,496,960 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-ce463f9ad9b4479aa6fc6a2469a1c4f2` |
| 19,492,864 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-1af5f3e0b41c477bab497c836da384d0` |
| 19,492,864 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-8e7b8f0073144d579b8c63b8147ace01` |
| 19,492,864 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-c51e0ee367aa49f4a974770007962000` |
| 19,447,808 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-a02780bffb2b4f4fa6bf5b9dd534dbba` |
| 19,447,808 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-d6efc15628334e1389ff85125074d44c` |
| 19,443,712 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-0ed4e85a26eb473c90d242061a5aff1b` |
| 19,443,712 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-48f52ce81bb34522a5a171de509e370a` |
| 19,443,712 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-a7414cfe511a4f2aaf727a6b05afc0f3` |
| 19,443,712 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-eb118a6c4c7c4d9eb94abd4aa9749715` |
| 19,427,328 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-2dbf56ea3138492fa975f5fa3c2aeaa0` |
| 19,427,328 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-52fafd39dacd4d9c875da88dfcbed4c6` |
| 19,427,328 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-90046b4bc600460280d2806e2ef33ca1` |
| 19,427,328 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-c8eeda5cab37491ea8e53c0340ed53f1` |
| 19,423,232 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-3cd209564abc4c4ca2b35e3aae7d9a5d` |
| 19,423,232 | test-fixture | `c502-exit-cap` | `/tmp/c502-exit-cap-f679a21172c7440fb1655a53b3ad39c3` |
| 19,390,464 | test-fixture | `antiphon-c514-pty` | `/tmp/antiphon-c514-pty-421e65e45d4349e69bd8e85732c6dc14` |
| 18,788,352 | build-output | `d16edc11` | `/tmp/card1873-review-d16edc11/combined/tools/Antiphon.TimeoutAudit/bin` |
| 18,784,256 | build-output | `d1a9978d` | `/tmp/antiphon-review-d1a9978d/combined/tools/Antiphon.TimeoutAudit/bin` |
| 18,780,160 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/tools/Antiphon.TimeoutAudit/bin` |
| 18,780,160 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/tools/Antiphon.TimeoutAudit/bin` |
| 18,780,160 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/tools/Antiphon.TimeoutAudit/bin` |
| 18,780,160 | build-output | `18072e35` | `/tmp/review-18072e35-master/tools/Antiphon.TimeoutAudit/bin` |
| 18,780,160 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/tools/Antiphon.TimeoutAudit/bin` |
| 18,776,064 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/tools/Antiphon.TimeoutAudit/bin` |
| 18,776,064 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/tools/Antiphon.TimeoutAudit/bin` |
| 18,776,064 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tools/Antiphon.TimeoutAudit/bin` |
| 18,776,064 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/tools/Antiphon.TimeoutAudit/bin` |
| 18,776,064 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/tools/Antiphon.TimeoutAudit/bin` |
| 18,776,064 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/tools/Antiphon.TimeoutAudit/bin` |
| 12,095,488 | build-output | `f9d27fc2` | `/tmp/f9d27fc2-review-myXGymje/red/tests/Antiphon.SessionRunner.Tests/obj` |
| 4,665,344 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/src/Antiphon.PtyHost/bin` |
| 4,665,344 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/src/Antiphon.PtyHost/bin` |
| 4,665,344 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/src/Antiphon.PtyHost/bin` |
| 4,665,344 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/src/Antiphon.PtyHost/bin` |
| 4,657,152 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/src/Antiphon.PtyHost/bin` |
| 4,657,152 | build-output | `18072e35` | `/tmp/review-18072e35-master/src/Antiphon.PtyHost/bin` |
| 4,657,152 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/src/Antiphon.PtyHost/bin` |
| 4,644,864 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/src/Antiphon.PtyHost/bin` |
| 4,644,864 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/src/Antiphon.PtyHost/bin` |
| 3,907,584 | build-output | `f9d27fc2` | `/tmp/f9d27fc2-review-myXGymje/red/src/Antiphon.SessionRunner/obj` |
| 3,829,760 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/src/Antiphon.SessionRunner/obj` |
| 3,825,664 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/src/Antiphon.SessionRunner/obj` |
| 3,825,664 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/src/Antiphon.SessionRunner/obj` |
| 3,825,664 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/src/Antiphon.SessionRunner/obj` |
| 3,825,664 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/src/Antiphon.SessionRunner/obj` |
| 3,825,664 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/src/Antiphon.SessionRunner/obj` |
| 3,780,608 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/src/Antiphon.SessionRunner/obj` |
| 3,776,512 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/src/Antiphon.SessionRunner/obj` |
| 3,776,512 | build-output | `18072e35` | `/tmp/review-18072e35-master/src/Antiphon.SessionRunner/obj` |
| 3,665,920 | build-output | `53820e0f` | `/tmp/c1836-s4a-combined-53820e0f/src/Antiphon.SessionRunner/obj` |
| 3,665,920 | build-output | `e594649c` | `/tmp/card1901-e594649c-base/src/Antiphon.SessionRunner/obj` |
| 3,096,576 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/scripts/lib/Antiphon.Nightly/obj` |
| 2,785,280 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/scripts/lib/Antiphon.Scripts/bin` |
| 2,621,440 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/tests/Antiphon.ScriptHarnessHost/bin` |
| 2,437,120 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/scripts/lib/Antiphon.Nightly/bin` |
| 2,265,088 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/scripts/lib/Antiphon.Nightly/obj` |
| 1,810,432 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/scripts/lib/Antiphon.Nightly/bin` |
| 1,613,824 | build-output | `3ac24cbf` | `/tmp/antiphon-review-3ac24cbf.5eMxwaHy/baseline/src/Antiphon.Agents.Pty/bin` |
| 1,613,824 | build-output | `f9d27fc2` | `/tmp/f9d27fc2-review-myXGymje/red/src/Antiphon.Agents.Pty/bin` |
| 1,605,632 | build-output | `9e6138ac` | `/tmp/antiphon-g139-master-9e6138ac/src/Antiphon.Agents.Pty/bin` |
| 1,605,632 | build-output | `5c78b016` | `/tmp/antiphon-owner-map-r2-master-5c78b016/src/Antiphon.Agents.Pty/bin` |
| 1,605,632 | build-output | `18072e35` | `/tmp/review-18072e35-compose2/src/Antiphon.Agents.Pty/bin` |
| 1,605,632 | build-output | `18072e35` | `/tmp/review-18072e35-master/src/Antiphon.Agents.Pty/bin` |
| 1,605,632 | build-output | `76b77db6` | `/tmp/review-76b77db6-master/src/Antiphon.Agents.Pty/bin` |
| 1,605,632 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge-now/src/Antiphon.Agents.Pty/bin` |
| 1,605,632 | build-output | `76b77db6` | `/tmp/review-76b77db6-merge/src/Antiphon.Agents.Pty/bin` |
| 1,605,632 | build-output | `d5ecb5af` | `/tmp/s5c-review-d5ecb5af/merge/src/Antiphon.Agents.Pty/bin` |

## 24-hour refinement

The operator widened on-host eligibility to trees older than 24 hours, including terminal worktrees with local changes. The follow-up removed 47 trees with 94,460,014,592 measured allocated bytes. The scheduled evidence sweep now defaults to 24 hours and retains its release-list and reference checks.

| Sample | Used bytes | Available bytes | Usage |
| --- | --- | --- | --- |
| 09:23 UTC, observed baseline after the operator’s Docker passes | 600,294,375,424 | 125,529,497,600 | 83% |
| Fri Nov 27 09:31:43 UTC 2026 | 507,596,857,344 | 218,227,015,680 | 70% |

| Category | Trees | Allocated bytes |
| --- | --- | --- |
| evidence | 9 | 22,566,338,560 |
| temp | 38 | 71,893,676,032 |

Twenty deleted paths were registered scratch or evidence checkouts. Git unregistered those paths by name. The branch records were preserved. The pass reached 70% before the terminal task mirrors in /work/worktrees entered the deletion order.

The current project scope included its unscoped tasks and reconciled zero exclusions. The pass held open task and pending-land paths, current slot bindings, running Docker mounts visible in the runner, and the four protected evidence paths. It refreshed the open and pending-land census every 12 candidates and confirmed terminal status for each named task worktree. Each exact directory was checked for age, mount boundaries, links, protected descendants, registration and visible process holders. The filesystem totals include other workloads on the shared mount. The three protected evidence directories were present after cleanup. The protected timing JSON was verified at /work/evidence/invariant-qualification/card-2082-windows-class-timing.json.

The orchestrator had already completed the Docker prune passes. The nested Docker socket and missing trusted SSH host key still limited visibility into the outer host. Volume-by-volume sizes for the outer 34 and outer JSON log sizes remain unverified. This pass performed zero Docker removals. A PowerShell parser and parameter metadata check confirmed the revised 24-hour sweep minimum. The first-pass checkpoint and dispatcher build verification remains applicable.

### Fifteen largest removals

| Allocated bytes | Previous directory mtime UTC | Path |
| --- | --- | --- |
| 4,060,782,592 | 2026-11-25T17:28:46.745Z | `/work/review-evidence/task-8b2639b7` |
| 3,257,544,704 | 2026-11-25T16:28:49.237Z | `/work/review-evidence/a34bcfae-88dc-40a8-8f6b-facffb43f051` |
| 3,256,590,336 | 2026-11-25T14:15:22.571Z | `/work/review-evidence/task-6ea7f02f` |
| 3,246,280,704 | 2026-11-26T08:24:27.092Z | `/work/merge-evidence/task-3a44eb7e-red-pin` |
| 3,032,457,216 | 2026-11-25T15:07:28.516Z | `/tmp/card2054-a8bd4504-r2.5ZvywbbS` |
| 2,886,332,416 | 2026-11-14T19:20:04.620Z | `/tmp/c1915-compose` |
| 2,842,841,088 | 2026-11-25T16:52:22.156Z | `/tmp/16611b04-review-Ay41w7W8` |
| 2,841,227,264 | 2026-11-25T14:30:17.177Z | `/tmp/6445cb21-master-proof` |
| 2,806,116,352 | 2026-11-26T02:47:11.079Z | `/tmp/c2078-red-Z7i2XWXt` |
| 2,734,497,792 | 2026-11-25T09:19:40.739Z | `/tmp/c2061-ec24e3e3-base.2eKL8S` |
| 2,716,901,376 | 2026-11-25T13:09:57.367Z | `/work/review-evidence/02226efb-6f05-4ca9-bf35-ea3b5081f7d0` |
| 2,606,223,360 | 2026-11-22T01:51:02.457Z | `/tmp/c2015-lock-base.PuZDDc` |
| 2,592,649,216 | 2026-11-21T10:26:36.449Z | `/tmp/card-2046-base.RByisG` |
| 2,486,358,016 | 2026-11-15T07:20:42.401Z | `/tmp/card-1884-review-compose` |
| 2,482,110,464 | 2026-11-14T21:27:24.869Z | `/tmp/c0824-compose` |

### Refinement deletion ledger

| Category | Allocated bytes | Previous directory mtime UTC | Task prefix | Path |
| --- | --- | --- | --- | --- |
| evidence | 4,060,782,592 | 2026-11-25T17:28:46.745Z | — | `/work/review-evidence/task-8b2639b7` |
| evidence | 3,257,544,704 | 2026-11-25T16:28:49.237Z | — | `/work/review-evidence/a34bcfae-88dc-40a8-8f6b-facffb43f051` |
| evidence | 3,256,590,336 | 2026-11-25T14:15:22.571Z | — | `/work/review-evidence/task-6ea7f02f` |
| evidence | 3,246,280,704 | 2026-11-26T08:24:27.092Z | — | `/work/merge-evidence/task-3a44eb7e-red-pin` |
| temp | 3,032,457,216 | 2026-11-25T15:07:28.516Z | — | `/tmp/card2054-a8bd4504-r2.5ZvywbbS` |
| temp | 2,886,332,416 | 2026-11-14T19:20:04.620Z | — | `/tmp/c1915-compose` |
| temp | 2,842,841,088 | 2026-11-25T16:52:22.156Z | — | `/tmp/16611b04-review-Ay41w7W8` |
| temp | 2,841,227,264 | 2026-11-25T14:30:17.177Z | — | `/tmp/6445cb21-master-proof` |
| temp | 2,806,116,352 | 2026-11-26T02:47:11.079Z | — | `/tmp/c2078-red-Z7i2XWXt` |
| temp | 2,734,497,792 | 2026-11-25T09:19:40.739Z | — | `/tmp/c2061-ec24e3e3-base.2eKL8S` |
| evidence | 2,716,901,376 | 2026-11-25T13:09:57.367Z | — | `/work/review-evidence/02226efb-6f05-4ca9-bf35-ea3b5081f7d0` |
| temp | 2,606,223,360 | 2026-11-22T01:51:02.457Z | — | `/tmp/c2015-lock-base.PuZDDc` |
| temp | 2,592,649,216 | 2026-11-21T10:26:36.449Z | — | `/tmp/card-2046-base.RByisG` |
| temp | 2,486,358,016 | 2026-11-15T07:20:42.401Z | — | `/tmp/card-1884-review-compose` |
| temp | 2,482,110,464 | 2026-11-14T21:27:24.869Z | — | `/tmp/c0824-compose` |
| temp | 2,478,768,128 | 2026-11-14T03:25:09.360Z | — | `/tmp/c1910-merged-now` |
| temp | 2,467,389,440 | 2026-11-16T04:53:37.000Z | — | `/tmp/cc5b4299-rc22` |
| temp | 2,465,370,112 | 2026-11-15T04:05:26.375Z | — | `/tmp/card-1925-mutant` |
| temp | 2,464,980,992 | 2026-11-16T04:53:38.705Z | — | `/tmp/cc5b4299-rc21` |
| temp | 1,974,333,440 | 2026-11-16T18:06:12.605Z | — | `/tmp/card-1908-cf58c961` |
| temp | 1,856,069,632 | 2026-11-13T10:50:00.150Z | — | `/tmp/rh-bacbef2b6dba4fc0b4ca520d051692f2` |
| temp | 1,856,065,536 | 2026-11-13T03:06:20.407Z | — | `/tmp/task-08e79898-stream-phases` |
| temp | 1,856,065,536 | 2026-11-13T03:03:04.455Z | — | `/tmp/task-08e79898-stream-target-green` |
| temp | 1,856,065,536 | 2026-11-13T02:57:39.467Z | — | `/tmp/task-08e79898-stream-target-red` |
| temp | 1,856,053,248 | 2026-11-13T05:26:35.802Z | — | `/tmp/rhp-f84b96d344cb496c81991cfd69c5fefb` |
| temp | 1,856,049,152 | 2026-11-13T05:27:08.511Z | — | `/tmp/rhp-e055c0b20ab949f7bc1a9e129e731182` |
| temp | 1,767,399,424 | 2026-11-25T15:50:38.936Z | — | `/tmp/c2060-base.NCb9Xu` |
| evidence | 1,524,285,440 | 2026-11-23T02:30:10.704Z | — | `/work/review-evidence/task-a5ebf1e9` |
| evidence | 1,507,180,544 | 2026-11-22T15:49:40.591Z | — | `/work/review-evidence/task-b6bdc5c9` |
| evidence | 1,503,088,640 | 2026-11-22T21:28:31.526Z | — | `/work/review-evidence/task-7a972462` |
| evidence | 1,493,684,224 | 2026-11-22T05:15:28.953Z | — | `/work/code-evidence/c0824-base-d2cYmT` |
| temp | 1,493,024,768 | 2026-11-21T19:53:20.664Z | — | `/tmp/c1514-r2-base-7589c103` |
| temp | 1,492,733,952 | 2026-11-21T19:30:23.499Z | — | `/tmp/c2013-base-e3531ec6.8KN3bY` |
| temp | 1,482,358,784 | 2026-11-21T10:40:50.130Z | — | `/tmp/card-2046-historical.DC3er5` |
| temp | 1,457,893,376 | 2026-11-11T00:42:53.314Z | — | `/tmp/card0395` |
| temp | 1,424,162,816 | 2026-11-17T08:53:23.252Z | — | `/tmp/card-1964-red-d32418928` |
| temp | 1,219,379,200 | 2026-11-14T02:29:18.509Z | — | `/tmp/c1886-layering-red` |
| temp | 1,163,411,456 | 2026-11-20T00:37:43.716Z | — | `/tmp/bin-codex170-20261120-002635-170a` |
| temp | 1,161,170,944 | 2026-11-17T19:29:12.502Z | — | `/tmp/c1893-bin-c1893s0-old` |
| temp | 1,160,552,448 | 2026-11-18T08:26:14.952Z | — | `/tmp/card-1976-kept-proof` |
| temp | 1,160,552,448 | 2026-11-18T09:07:48.382Z | — | `/tmp/card-1976-kept-proof-6b8e` |
| temp | 1,158,455,296 | 2026-11-15T03:15:03.658Z | — | `/tmp/c1820-r4-green-run1-4f8dc817` |
| temp | 1,154,383,872 | 2026-11-13T15:40:51.944Z | — | `/tmp/c1884-bin-guards-344c8f13` |
| temp | 1,154,269,184 | 2026-11-13T10:53:23.909Z | — | `/tmp/card-1850-s2-bin-c1850-s2-green-kept` |
| temp | 1,153,765,376 | 2026-11-13T11:12:24.182Z | — | `/tmp/s5a-bin-c1836-s5a-kept-9afb7a90` |
| temp | 1,045,970,944 | 2026-11-14T21:35:07.103Z | — | `/tmp/c0824-witness` |
| temp | 946,167,808 | 2026-11-18T08:40:57.295Z | — | `/tmp/card-1976-kept-contract` |
