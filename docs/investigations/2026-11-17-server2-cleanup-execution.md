# server2 cleanup execution (2026-11-17)

Task 5ed5b730, Custom. Executes the approved removals from the [/tmp census](2026-11-17-server2-tmp-census.md) (task 5390dd7c,
`17c954b5b230c99e4fd938cfd7df34ca44b2d055`) and the [worktree inventory](2026-11-17-server2-worktree-inventory.md) (task 3283ed4f,
`e6a0f53ff45abfc294604800e48634a358157991`). Run 19:57Z-20:09Z UTC inside the runner container (hostname `d0d4c9a390d9`, uid 1654).
`ssh mc@server2` answers `Permission denied (publickey)`, so host processes and other containers stay UNOBSERVED as holders.

## Outcome

| Phase | Removed | Skipped | du of removed (GiB) | df free before -> after (MiB) |
|---|---:|---:|---:|---:|
| A: clean /tmp worktrees | 18 of 30 | 12 (9 younger than 72 h, 3 inside `/tmp/claude-1654`) | 23.20 | 90,097 -> 138,054 (A total) |
| A: class D helper worktrees | 11 of 12 | 1 (`task-3e32f1e6-s2`, 2 h old) | 23.65 | (in A) |
| B: /tmp worktrees with unpushed commits | 26 of 42 | 16 (8 younger than 72 h, 6 dirty, 2 inside `/tmp/claude-1654`) | 36.54 | 141,328 -> 179,866 (B total) |
| B: class B mirrors | 4 of 5 | 1 (`c0824-compose-out`, 69 h old) | 1.07 | (in B) |
| C: empty six-character /tmp dirs older than 72 h | 20,500 of 20,500 | 0 | ~0 | 179,866 -> 179,205 (noise) |

df `/tmp` (same LV as `/work`): 602,104 MiB used / 90,097 MiB free before; 512,995 used / 179,205 free after (87 % -> 75 %).
du of all removed trees: 84.45 GiB. `/tmp` after: 130,737 MiB (`du -sx`, about 80 unreadable foreign-uid dirs excluded as in the census).
Registered worktrees: 183 before, 120 after (59 by this task; the other 4 left by other activity, e.g. the retention job, during the run).
`git worktree prune --dry-run -v` printed nothing after A and at the end, so no real prune was run (the condition "lists exactly the
removed paths" did not hold: `git worktree remove` already deletes each admin dir).

## Gates applied before each removal

Script per path (no globs, no `--force`, no `rm`, no prune): the path is on the note's list exactly (`grep -xF`); not under the own slot,
`/tmp/claude-*`, `/tmp/antiphon-pty-hosts` or `/work/workspace-slots`, and not a ledger slot path; registered in
`git -C /work/repos/antiphon worktree list --porcelain`; item mtime and newest depth-1 child mtime both at least 72 h old;
`GIT_OPTIONAL_LOCKS=0 git status --porcelain` empty; zero `/proc/*/cwd|root|fd/*` links at or under the path (`fuser`/`lsof` are not
installed in the container); no Dispatched/Working/Blocked/Queued task (`GET /api/agent-tasks?status=...`, re-read per removal) whose id
prefix is an 8-hex token of the path (every real task id among the tokens reads Succeeded or Canceled in `delegate.ps1 -Status`);
then `git -C /work/repos/antiphon worktree remove <path>` and a check that the path is gone.
Phase A additionally required `git rev-list --count HEAD ^<every live origin tip>` = 0 (after `git fetch origin` and one `ls-remote`, 5,365
distinct tips, all present locally). Phase B required either every unpushed commit to have an equal `git patch-id --stable` among the
16,959 non-merge commits since 2026-10-15 reachable from live origin tips (`git cherry origin/master HEAD` recorded beside it), or a
salvage push of HEAD to a NEW ref verified with `ls-remote`. A merge commit has no patch-id and always counted as unequivalent.

## Salvage refs created (all new, verified equal to the tree HEAD with ls-remote)

| Ref | SHA | Unpushed / unequivalent |
|---|---|---|
| `salvage/tmp-c1877-comp-b` | `ea56909477b2bb54c1d230e6eb7826a84b4b1460` | 6 / 1 |
| `salvage/tmp-card-1915-review-compose` | `f14600eb07a1c79ed6c770c598717c540c9bec39` | 1 / 1 |
| `salvage/tmp-review-01e2734e-master2` | `61485745e0fc08e251fa3655c8745a1f1e370417` | 12 / 2 |
| `salvage/tmp-review-575ab1a1-master` | `e3c442d8607cac67f09ec4ec3f34681f146951a3` | 6 / 4 |
| `salvage/tmp-card-1886-s2-review-55233180` | `823c5c65135b2c4d9c04c0c8ac739ebd42385fa4` | 1 / 1 |
| `salvage/tmp-card-1886-s2-review-1fde603c` | `13af8d0fc8d9aa3dcb664f24f2430d285e57fba3` | 8 / 1 |
| `salvage/tmp-review-01e2734e-master` | `1531a0ee7acc019a454c54fba26e7d1ccd53a32b` | 11 / 2 |
| `salvage/tmp-card-1836-s3b-r2-1871-compose` | `949f85e5c7cc04aa2864fb9982211e9af6a108e5` | 10 / 1 |
| `salvage/c1886-s1-integrated` | `827ab0ee91857ab40996128ac763047094436e81` | 11 / 1 |

Unequivalent commits: two compose merges (`d0aa0cb167` in c1877-comp-b, `f14600eb07` in card-1915-review-compose), the CARD-1916..1919
repair commits in review-575ab1a1-master, two selector-fixture commits in each of review-01e2734e-master and -master2 ("test: dispose the temp location witness with the master fixture", "fix: classify ignored selector fixture repos ..."), the scratch compose
commits `823c5c6513` / `81ef11221b`, and regenerated impact-index commits (`8126fafefc`, `827ab0ee91`).
`task-34125850` and `task-9d003ac4` had 0 commits outside live origin refs (`salvage/stale-merge-34125850-reports` = `72a8014a92`, confirmed).

## Not removed (caller decision)

- Younger than 72 h (re-run after they age): `/tmp/cc5b4299-rc22`, `/tmp/cc5b4299-rc21`, `/tmp/card-1925-mutant`, `/tmp/review-18072e35-master`,
  `/tmp/card1901-e594649c-base` (newest child 63 h), `/tmp/card-1964-red-d32418928`, `/tmp/c1893-red-068308a85`, `/tmp/card1943-base-c1029`,
  `/tmp/c1947-master-23686c59`, `/work/worktrees/task-3e32f1e6-s2`, `/tmp/card-1943-review-compose`, `/tmp/s5c-review-d5ecb5af/merge`,
  `/tmp/card-1884-review-compose`, `/tmp/c0824-compose`, `/tmp/review-18072e35-compose2`, `/tmp/card-1925-compose`, `/tmp/c1947-compose-23686c59`,
  `/tmp/review-18072e35-compose`, `/work/worktrees/c0824-compose-out`.
- Dirty (porcelain not empty, no salvage pushed): `/tmp/c1836-s4a-combined-53820e0f`, `/tmp/s5a-review-master-253bead9`,
  `/tmp/s5a-review-master-90ae19f6`, `/tmp/s3b-r3-master-scratch`, `/tmp/c1915-compose` (each ` M tests/derived-impact-index.json`),
  `/tmp/c1906-compose-wt` (3 tracked edits). The census's 6 dirty trees were not touched.
- Inside `/tmp/claude-1654` (hard rule: never delete a Claude scratchpad): `.../task-6379c50f/.../scratchpad/{cut,w-de27ce2fd,w-135865831}`
  (clean, contained, 349 h), `.../task-1c23b333/.../scratchpad/onmaster` (clean, 7 unpushed), `.../task-5b164f58/.../scratchpad/mastertree`
  (`?? scripts/c1820-review-harness/`).

## Left for the sanctioned sweep (counted, not deleted)

| Family | Top-level entries | KiB |
|---|---:|---:|
| `antiphon-bridge-queue-*` | 10,146 | 92,768 |
| `c459-*` | 2,718 | 21,744 |
| `c487-tw-*` | 2,631 | 26,304 |
| `clr-debug-pipe-*` (FIFOs) | 60,487 | 0 |
| `dotnet-diagnostic-*` (sockets) | 30,216 | 0 |

Top-level `/tmp` entries after phase C: 127,644.

## Live work

Open tasks at 20:09Z (all Dispatched on server2): d2eef712 (`ws-02-d2`), 3efa98ab (`ws-03-d2`), e187afde (`ws-04-d2`) and this task
(`ws-01-d4`). No removed path is a slot path or contains one of those ids.

## Removal log (phases A and B)

The full log including the 20,500 phase C rows is `.antiphon/cleanup-log.txt` in the task mirror (ignored, 1.5 MB).

| UTC | Phase | Path | Result | GiB | Checks |
|---|---|---|---|---:|---|
| 2026-11-17T19:57:57Z | A | `/tmp/c1629-plain-one` | REMOVED | 0.22 | reg=yes item=373h child=373h por=0 hold=0 head=0e164b02df99807507e3b995b7b9d5cc31ae8987 out=0 task-inactive  |
| 2026-11-17T19:58:01Z | A | `/tmp/card1862-s1-b4ab4c34` | REMOVED | 2.63 | reg=yes item=114h child=111h por=0 hold=0 head=b4ab4c34ea0f5592f355d8eb6a0c68eb8421a2d7 out=0 task-inactive  |
| 2026-11-17T19:58:02Z | A | `/tmp/cc5b4299-rc22` | SKIP | 0.00 | young item=39h child=38h |
| 2026-11-17T19:58:02Z | A | `/tmp/card-1925-mutant` | SKIP | 0.00 | young item=63h child=63h |
| 2026-11-17T19:58:02Z | A | `/tmp/cc5b4299-rc21` | SKIP | 0.00 | young item=39h child=38h |
| 2026-11-17T19:58:04Z | A | `/tmp/review-18072e35-master` | SKIP | 0.00 | young item=71h child=71h |
| 2026-11-17T19:58:05Z | A | `/tmp/task-15406ecc-coverage2` | REMOVED | 2.29 | reg=yes item=76h child=74h por=0 hold=0 head=8cec1e74e75819ef20756c228add987ce4ae4170 out=0 task-inactive  |
| 2026-11-17T19:58:06Z | A | `/tmp/card1884-review-2db533c9/master` | REMOVED | 2.29 | reg=yes item=80h child=80h por=0 hold=0 head=b559bfba2e9a420495122d27171352fe9b7e1ba4 out=0 task-inactive  |
| 2026-11-17T19:58:07Z | A | `/tmp/g90-master-c1029` | REMOVED | 2.29 | reg=yes item=74h child=74h por=0 hold=0 head=8cec1e74e75819ef20756c228add987ce4ae4170 out=0 task-inactive  |
| 2026-11-17T19:58:08Z | A | `/tmp/card1901-e594649c-base` | SKIP | 0.00 | young item=76h child=63h |
| 2026-11-17T19:58:12Z | A | `/tmp/card-1912-base-8c06` | REMOVED | 2.29 | reg=yes item=81h child=81h por=0 hold=0 head=8c06c7e11f40cc224235ecbc68c2389913cc67ee out=0 task-inactive  |
| 2026-11-17T19:58:14Z | A | `/tmp/c1886-master-base` | REMOVED | 2.29 | reg=yes item=89h child=89h por=0 hold=0 head=f805ae444dcfdec07fd1ed18727817b16f2e5bc0 out=0 task-inactive  |
| 2026-11-17T19:58:15Z | A | `/tmp/antiphon-master-8592d536` | REMOVED | 1.94 | reg=yes item=304h child=304h por=0 hold=0 head=cc8845f030d934507e7a0d2db7d939be06d9f2bc out=0 task-inactive  |
| 2026-11-17T19:58:15Z | A | `/tmp/card-1964-red-d32418928` | SKIP | 0.00 | young item=11h child=11h |
| 2026-11-17T19:58:16Z | A | `/tmp/antiphon-review-ff132917/base` | REMOVED | 1.31 | reg=yes item=123h child=123h por=0 hold=0 head=898120436384365043957ff5e9022b79086effb8 out=0 task-inactive  |
| 2026-11-17T19:58:17Z | A | `/tmp/c2cf0b8a-review/red-d2` | REMOVED | 1.31 | reg=yes item=122h child=121h por=0 hold=0 head=2a7687d4b00067ea1add526b82de63d4bfd54694 out=0 task-inactive  |
| 2026-11-17T19:58:18Z | A | `/tmp/antiphon-review-ff132917/red-source` | REMOVED | 1.31 | reg=yes item=123h child=123h por=0 hold=0 head=b4cb13465e88db7f7083683291f3f757542a3ed4 out=0 task-inactive  |
| 2026-11-17T19:58:19Z | A | `/tmp/c2cf0b8a-review/red-d1` | REMOVED | 1.31 | reg=yes item=122h child=122h por=0 hold=0 head=1fc57f4b2145caaeb5c70ca64ea9aa54adfa8c40 out=0 task-inactive  |
| 2026-11-17T19:58:20Z | A | `/tmp/c1893-red-068308a85` | SKIP | 0.00 | young item=22h child=22h |
| 2026-11-17T19:58:25Z | A | `/tmp/antiphon-review-40d54591/master` | REMOVED | 0.38 | reg=yes item=92h child=92h por=0 hold=0 head=1fde603cce208ff8a9147715101b3976c737edd0 out=0 task-inactive  |
| 2026-11-17T19:58:26Z | A | `/tmp/card-1875-red-scratch` | REMOVED | 0.37 | reg=yes item=106h child=106h por=0 hold=0 head=e87ad93afcdd9a6b35b6e72842cef7fab82af902 out=0 task-inactive  |
| 2026-11-17T19:58:26Z | A | `/tmp/card1943-base-c1029` | SKIP | 0.00 | young item=62h child=62h |
| 2026-11-17T19:58:27Z | A | `/tmp/c1947-master-23686c59` | SKIP | 0.00 | young item=45h child=45h |
| 2026-11-17T19:58:28Z | A | `/tmp/antiphon-review-40d54591/historical` | REMOVED | 0.24 | reg=yes item=92h child=92h por=0 hold=0 head=3dc5e5c335c9ea0e3d622b042ebf809584ce0117 out=0 task-inactive  |
| 2026-11-17T19:58:29Z | A | `/tmp/antiphon-review-b9760da0/baseline` | REMOVED | 0.24 | reg=yes item=96h child=96h por=0 hold=0 head=a9a54e4d6e1603643a1919ab73cad11ef03edd6b out=0 task-inactive  |
| 2026-11-17T19:58:30Z | A | `/tmp/c1514-index-tip` | REMOVED | 0.24 | reg=yes item=101h child=101h por=0 hold=0 head=1a79c5119bce4c36e07e81bb43f5030ef072a4bf out=0 task-inactive  |
| 2026-11-17T19:58:31Z | A | `/tmp/c1877-master` | REMOVED | 0.24 | reg=yes item=104h child=104h por=0 hold=0 head=253bead91b5e735c99167824466b4e947f8f0eaf out=0 task-inactive  |
| 2026-11-17T19:58:31Z | A | `/tmp/claude-1654/-work-worktrees-task-6379c50f/0070ccc5-5710-438d-b7de-c56ddacfad1d/scratchpad/cut` | SKIP | 0.00 | protected-path |
| 2026-11-17T19:58:31Z | A | `/tmp/claude-1654/-work-worktrees-task-6379c50f/0070ccc5-5710-438d-b7de-c56ddacfad1d/scratchpad/w-de27ce2fd` | SKIP | 0.00 | protected-path |
| 2026-11-17T19:58:31Z | A | `/tmp/claude-1654/-work-worktrees-task-6379c50f/0070ccc5-5710-438d-b7de-c56ddacfad1d/scratchpad/w-135865831` | SKIP | 0.00 | protected-path |
| 2026-11-17T19:58:33Z | A | `/work/worktrees/task-8e452527-checks` | REMOVED | 3.53 | reg=yes item=83h child=82h por=0 hold=0 head=1af25a62f28f9346a9ad3e2568fda3645abb4152 out=0 task-inactive  |
| 2026-11-17T19:58:34Z | A | `/work/worktrees/task-71aaddba-measure` | REMOVED | 2.69 | reg=yes item=88h child=87h por=0 hold=0 head=1e09a851acef35a3d08eec4a79be2214d51c64df out=0 task-inactive  |
| 2026-11-17T19:58:36Z | A | `/work/worktrees/task-8e452527-repair` | REMOVED | 2.68 | reg=yes item=81h child=80h por=0 hold=0 head=a46bd7af4b37a30a5fddcbdaca7ab1e6747bc224 out=0 task-inactive  |
| 2026-11-17T19:58:37Z | A | `/work/worktrees/task-37cb8e72-master` | REMOVED | 2.31 | reg=yes item=98h child=97h por=0 hold=0 head=c1fd1b7bc1eb905d2285b4258879955dda4fe96d out=0 task-inactive  |
| 2026-11-17T19:58:38Z | A | `/work/worktrees/task-15406ecc-coverage2` | REMOVED | 2.29 | reg=yes item=74h child=74h por=0 hold=0 head=8cec1e74e75819ef20756c228add987ce4ae4170 out=0 task-inactive  |
| 2026-11-17T19:58:40Z | A | `/work/worktrees/task-ac7f437c-base` | REMOVED | 2.29 | reg=yes item=72h child=72h por=0 hold=0 head=f701b2ba967d503427d7064fe4fffa17b8c1738d out=0 task-inactive  |
| 2026-11-17T19:58:41Z | A | `/work/worktrees/task-37cb8e72-s1base` | REMOVED | 2.29 | reg=yes item=98h child=97h por=0 hold=0 head=70fd156d0677b78218010eb4e682c66db831ad24 out=0 task-inactive  |
| 2026-11-17T19:58:42Z | A | `/work/worktrees/task-37cb8e72-s1tip` | REMOVED | 2.29 | reg=yes item=98h child=97h por=0 hold=0 head=6a42fc4d7884951f0337eb976c7ed9df57269e1f out=0 task-inactive  |
| 2026-11-17T19:58:44Z | A | `/work/worktrees/task-8e452527-base` | REMOVED | 1.34 | reg=yes item=82h child=81h por=0 hold=0 head=ca8737e78170c9a441e216408da5fab636c34ca0 out=0 task-inactive  |
| 2026-11-17T19:58:45Z | A | `/work/worktrees/task-0a2ad036-base` | REMOVED | 1.32 | reg=yes item=80h child=80h por=0 hold=0 head=ca8737e78170c9a441e216408da5fab636c34ca0 out=0 task-inactive  |
| 2026-11-17T19:58:46Z | A | `/work/worktrees/task-71aaddba-messaging-measure` | REMOVED | 0.62 | reg=yes item=88h child=88h por=0 hold=0 head=1e09a851acef35a3d08eec4a79be2214d51c64df out=0 task-inactive  |
| 2026-11-17T19:58:46Z | A | `/work/worktrees/task-3e32f1e6-s2` | SKIP | 0.00 | young item=2h child=2h |
| 2026-11-17T20:03:47Z | B | `/tmp/c1877-comp-a` | REMOVED | 4.56 | reg=yes item=104h child=103h por=0 hold=0 head=dc590eb63f2e1ce451d3eefb084347f17496b9b2 out=5 task-inactive all-equivalent unpushed=5 equivalent-by-patch-id=5 |
| 2026-11-17T20:03:50Z | B | `/tmp/c1877-comp-b` | REMOVED | 4.56 | reg=yes item=104h child=102h por=0 hold=0 head=ea56909477b2bb54c1d230e6eb7826a84b4b1460 out=6 task-inactive salvaged=salvage/tmp-c1877-comp-b@ea56909477b2bb54c1d230e6eb7826a84b4b1460 unpushed=5 unequivalent=1 |
| 2026-11-17T20:03:52Z | B | `/tmp/card-1915-review-compose` | REMOVED | 3.71 | reg=yes item=78h child=78h por=0 hold=0 head=f14600eb07a1c79ed6c770c598717c540c9bec39 out=1 task-inactive salvaged=salvage/tmp-card-1915-review-compose@f14600eb07a1c79ed6c770c598717c540c9bec39 unpushed=0 unequivalent=1 |
| 2026-11-17T20:03:54Z | B | `/tmp/review-01e2734e-master2` | REMOVED | 2.41 | reg=yes item=100h child=99h por=0 hold=0 head=61485745e0fc08e251fa3655c8745a1f1e370417 out=12 task-inactive salvaged=salvage/tmp-review-01e2734e-master2@61485745e0fc08e251fa3655c8745a1f1e370417 unpushed=10 unequivalent=2 |
| 2026-11-17T20:03:54Z | B | `/tmp/c1514-r2-master` | REMOVED | 2.41 | reg=yes item=100h child=99h por=0 hold=0 head=bb04ca147d9850091b70844fa87a7b4ea2f06ae9 out=7 task-inactive all-equivalent unpushed=7 equivalent-by-patch-id=7 |
| 2026-11-17T20:03:56Z | B | `/tmp/card-1875-master-scratch` | REMOVED | 2.39 | reg=yes item=106h child=106h por=0 hold=0 head=d341b96f1805861289228d9d155f9408814df537 out=3 task-inactive all-equivalent unpushed=3 equivalent-by-patch-id=3 |
| 2026-11-17T20:03:57Z | B | `/tmp/card-1850-s2-review-scratch` | REMOVED | 2.35 | reg=yes item=105h child=105h por=0 hold=0 head=fd4bcd3ef9737d0aa6c75b62239a0cc773c4e3e9 out=8 task-inactive all-equivalent unpushed=8 equivalent-by-patch-id=8 |
| 2026-11-17T20:03:58Z | B | `/tmp/review-575ab1a1-master` | REMOVED | 2.31 | reg=yes item=84h child=84h por=0 hold=0 head=e3c442d8607cac67f09ec4ec3f34681f146951a3 out=6 task-inactive salvaged=salvage/tmp-review-575ab1a1-master@e3c442d8607cac67f09ec4ec3f34681f146951a3 unpushed=2 unequivalent=4 |
| 2026-11-17T20:03:59Z | B | `/tmp/c1886-master-repair` | REMOVED | 2.31 | reg=yes item=89h child=89h por=0 hold=0 head=44106d436ff61d691036e92954fe948f185242b6 out=10 task-inactive all-equivalent unpushed=10 equivalent-by-patch-id=10 |
| 2026-11-17T20:04:00Z | B | `/tmp/card-1820-s1r3e-review-master` | REMOVED | 2.31 | reg=yes item=106h child=105h por=0 hold=0 head=7b41baa5874b5b334aa0cb27896635c9a38142ee out=8 task-inactive all-equivalent unpushed=8 equivalent-by-patch-id=8 |
| 2026-11-17T20:04:01Z | B | `/tmp/card-1820-s1r6d-review-253bead91` | REMOVED | 2.30 | reg=yes item=104h child=103h por=0 hold=0 head=745acd865ac8f05e5616f4aaa89b2031e35c151c out=5 task-inactive all-equivalent unpushed=5 equivalent-by-patch-id=5 |
| 2026-11-17T20:04:02Z | B | `/tmp/card-1850-s3a-review-ea79a8f2` | REMOVED | 1.36 | reg=yes item=103h child=102h por=0 hold=0 head=73420536de2c4edaffee931c0b9b343a9f183b58 out=5 task-inactive all-equivalent unpushed=5 equivalent-by-patch-id=5 |
| 2026-11-17T20:04:03Z | B | `/tmp/card1887-master-scratch` | REMOVED | 0.39 | reg=yes item=91h child=90h por=0 hold=0 head=4f72821926f13aa877346ea8d4cc2526e45491e5 out=9 task-inactive all-equivalent unpushed=9 equivalent-by-patch-id=9 |
| 2026-11-17T20:04:04Z | B | `/tmp/card1887-master-now` | REMOVED | 0.26 | reg=yes item=90h child=90h por=0 hold=0 head=7585f86ed21dd45559cedcf9eed1c81f27e487cd out=9 task-inactive all-equivalent unpushed=9 equivalent-by-patch-id=9 |
| 2026-11-17T20:04:05Z | B | `/tmp/card1887-master-f805` | REMOVED | 0.26 | reg=yes item=90h child=90h por=0 hold=0 head=d8b6eba0b04d247a8cf8ab1fbde19d6aa7fd94f3 out=9 task-inactive all-equivalent unpushed=9 equivalent-by-patch-id=9 |
| 2026-11-17T20:04:06Z | B | `/tmp/card-1886-s2-review-55233180` | REMOVED | 0.26 | reg=yes item=94h child=94h por=0 hold=0 head=823c5c65135b2c4d9c04c0c8ac739ebd42385fa4 out=1 task-inactive salvaged=salvage/tmp-card-1886-s2-review-55233180@823c5c65135b2c4d9c04c0c8ac739ebd42385fa4 unpushed=0 unequivalent=1 |
| 2026-11-17T20:04:07Z | B | `/tmp/c1836-s5b-review-scratch` | REMOVED | 0.26 | reg=yes item=102h child=102h por=0 hold=0 head=3975dbe892284bbee237b57ae093bb4b6f1e8601 out=9 task-inactive all-equivalent unpushed=9 equivalent-by-patch-id=9 |
| 2026-11-17T20:04:08Z | B | `/tmp/card-1886-s2-review-1fde603c` | REMOVED | 0.24 | reg=yes item=93h child=93h por=0 hold=0 head=13af8d0fc8d9aa3dcb664f24f2430d285e57fba3 out=8 task-inactive salvaged=salvage/tmp-card-1886-s2-review-1fde603c@13af8d0fc8d9aa3dcb664f24f2430d285e57fba3 unpushed=7 unequivalent=1 |
| 2026-11-17T20:04:09Z | B | `/tmp/card-1885-review-fe5b7764` | REMOVED | 0.24 | reg=yes item=97h child=96h por=0 hold=0 head=4301f316e8c662d9fbd4d3059afc9eaedfc7374b out=19 task-inactive all-equivalent unpushed=19 equivalent-by-patch-id=19 |
| 2026-11-17T20:04:10Z | B | `/tmp/card-1894-review-scratch` | REMOVED | 0.24 | reg=yes item=98h child=98h por=0 hold=0 head=de586ecbdb80c88cb28caf13c4a4d184eb3782c6 out=2 task-inactive all-equivalent unpushed=2 equivalent-by-patch-id=2 |
| 2026-11-17T20:04:11Z | B | `/tmp/review-01e2734e-master` | REMOVED | 0.24 | reg=yes item=101h child=101h por=0 hold=0 head=1531a0ee7acc019a454c54fba26e7d1ccd53a32b out=11 task-inactive salvaged=salvage/tmp-review-01e2734e-master@1531a0ee7acc019a454c54fba26e7d1ccd53a32b unpushed=9 unequivalent=2 |
| 2026-11-17T20:04:13Z | B | `/tmp/c1514-index-m78442d9` | REMOVED | 0.24 | reg=yes item=101h child=101h por=0 hold=0 head=012e29e2febd76e64bb14762cfdf8967e98ba853 out=4 task-inactive all-equivalent unpushed=4 equivalent-by-patch-id=4 |
| 2026-11-17T20:04:14Z | B | `/tmp/c1836-s5b-review-scratch-3dc5e5c` | REMOVED | 0.24 | reg=yes item=101h child=101h por=0 hold=0 head=d832a42a4c0f5d6800181f7c662bf0540aabbaf3 out=9 task-inactive all-equivalent unpushed=9 equivalent-by-patch-id=9 |
| 2026-11-17T20:04:14Z | B | `/tmp/c1514-index-master` | REMOVED | 0.24 | reg=yes item=101h child=101h por=0 hold=0 head=a1b59d68cf5da345f848a666d778a24f0da9433d out=4 task-inactive all-equivalent unpushed=4 equivalent-by-patch-id=4 |
| 2026-11-17T20:04:15Z | B | `/tmp/card-1820-s1r3e-review-clean` | REMOVED | 0.24 | reg=yes item=106h child=106h por=0 hold=0 head=7b41baa5874b5b334aa0cb27896635c9a38142ee out=8 task-inactive all-equivalent unpushed=8 equivalent-by-patch-id=8 |
| 2026-11-17T20:04:16Z | B | `/tmp/card-1836-s3b-r2-1871-compose` | REMOVED | 0.24 | reg=yes item=106h child=106h por=0 hold=0 head=949f85e5c7cc04aa2864fb9982211e9af6a108e5 out=10 task-inactive salvaged=salvage/tmp-card-1836-s3b-r2-1871-compose@949f85e5c7cc04aa2864fb9982211e9af6a108e5 unpushed=9 unequivalent=1 |
| 2026-11-17T20:04:18Z | B | `/work/worktrees/c1886-s1-integrated` | REMOVED | 0.30 | reg=yes item=92h child=91h por=0 hold=0 head=827ab0ee91857ab40996128ac763047094436e81 out=11 task-inactive salvaged=salvage/c1886-s1-integrated@827ab0ee91857ab40996128ac763047094436e81 unpushed=10 unequivalent=1 |
| 2026-11-17T20:04:19Z | B | `/work/worktrees/task-63cdf1df-land` | REMOVED | 0.32 | reg=yes item=208h child=208h por=0 hold=0 head=48c8b0685eb0584e0c0cc7571d028098f4d5da55 out=2 task-inactive all-equivalent unpushed=2 equivalent-by-patch-id=2 |
| 2026-11-17T20:04:20Z | B | `/work/worktrees/task-34125850` | REMOVED | 0.22 | reg=yes item=323h child=322h por=0 hold=0 head=72a8014a92188a8c587c08008b820c51d179b4d3 out=0 task-inactive all-equivalent unpushed=0 equivalent-by-patch-id=0 |
| 2026-11-17T20:04:21Z | B | `/work/worktrees/task-9d003ac4` | REMOVED | 0.24 | reg=yes item=137h child=136h por=0 hold=0 head=2aef004ce5d4cffbc524dcb207a0aec15cfa1fce out=0 task-inactive all-equivalent unpushed=0 equivalent-by-patch-id=0 |
