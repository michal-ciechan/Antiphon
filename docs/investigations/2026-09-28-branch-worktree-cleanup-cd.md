# Branch / worktree cleanup, groups C and D: content verification (task 07248498)

This follow-up covers task b1bb8fc5 (report: `docs/investigations/2026-09-28-branch-worktree-cleanup.md` on `feat/card-task-b1bb8fc5` at `d616dc38`). It re-derives every group C (86) and group D (163) candidate from scratch. `origin/master` was `cdcad651` at classification and deletion time. The server restarted to `49a7c8a8` afterwards.

## Method

- **Live status:** `GET /api/agent-tasks/{shortId}` `.summary.status` was queried at classification. It was queried again immediately before each deletion. Only Succeeded, Failed and Canceled count as deletable; Blocked is excluded, which is more conservative than the brief. Eighteen first-pass queries returned nothing during a server restart. A re-query returned Succeeded for all 18, and every deletion made its own live re-query.
- **Content check:** a candidate is "EMPTY" only when `git merge-tree --write-tree origin/master <tip>` exits 0 and its result tree equals `origin/master^{tree}`. In other words, merging the branch into current master would change nothing, so everything the branch changed is already in master's current tree.
  - A literal `git diff master...branch` measures from the merge base and is non-empty for every branch with real commits, so it is not used.
  - A three-way merge also covers a branch that was rebased and then squashed or cherry-picked. It fails safe on two cases: a merge conflict (master later edited the same lines) and a clean merge that still changes master (the branch has content master lacks).
- **Remote deletion:** `git push --force-with-lease=refs/heads/<br>:<classified sha> origin --delete <br>`. Before each delete, the status and the merge-tree check were run again.
- **Worktree removal:** only when the worktree is clean (`status --porcelain --untracked-files=all` empty), unlocked, has an unchanged HEAD, and that HEAD passes the content check itself (or equals a remote tip that passed). Removal used `git -c core.longpaths=true worktree remove --force` (clean only), then `git worktree prune`. The prior task's `rd /s /q \?\<path>` fallback was ready, but no removal needed it.
- Local `feat/card-task-*` branch refs were kept for every removed worktree, so no commit became unreachable. Verified: 0 of 30 missing.
- **Annotation for NEEDS-A-LOOK:** for each file the branch touched since its merge base, the column shows whether the branch-tip blob exists anywhere in `origin/master` history (`git rev-list --objects`). `unseen=0` means master once held exactly the branch's version of every touched file and has since moved on (superseded). `unseen>0` lists files whose branch version never appeared in master.

## Counts

| Item | Count |
|---|---|
| Task IDs examined (C 86 + D 163) | 249 |
| Skipped because no longer terminal | 0 (all Succeeded 207 / Failed 22 / Canceled 20) |
| Remote branches among candidates | 233 |
| Remote branches verified EMPTY and deleted | 58 (0 failures; ls-remote confirms all gone) |
| Worktrees among candidates | 110 (none dirty or locked) |
| Worktrees verified EMPTY and removed | 30 (0 failures; no fallback needed; local refs kept) |
| Task IDs fully cleaned (nothing left) | 62 |
| Task IDs moved to NEEDS-A-LOOK (content check not empty) | 187 (175 remote branches, 80 worktrees) |
| of which L1: superseded (conflict, but every touched file's branch version is in master history) | 117 |
| of which L2: branch holds a file version never seen in master | 70 |

The verdict breakdown for the look items: 185 were CONFLICT and 2 were NONEMPTY (clean merge that still changes master: `1aa86155` docs/ops-http.md and `520c70c6` scripts/host-stats-hub-receipt.test.mjs). For 3 IDs (`0bebfaa7`, `c5885cc9`, `c99e35fa`), the worktree HEAD was EMPTY and was removed while the remote branch CONFLICTs and stays.

## Other groups (not touched)

- **A (6), live re-check:**
  - `a01b0301` and `bc2b4ac7` are Blocked. `ff6c2f00` is Queued.
  - `34a18470`, `77a6073c` and `fb8fa4a9` are now terminal (Failed, Succeeded, Succeeded).
  - Read-only content check on those three: `34a18470` has zero commits at `cd133de9` (EMPTY), `fb8fa4a9` remote is EMPTY, and `77a6073c` is NONEMPTY (32 files).
  - All were left untouched per the brief. `34a18470` and `fb8fa4a9` can be deleted on a later pass.
- **B (21), dirty, locked or orphan, left for a human:** 
047193d4 0f9c42e4 26e2c676 273b3a0d 31002bb4 35eb1011 3bfe742a 56dcfa48 597838fe 5e046d60 63b72299 66eec4dd 704c7117 8242ad6b 83820e7c 8a18e4d8 aebd0705 bb20bcb9 c933f24a cd2866b8 dfc6f878 
- **F (227), real commits not in master, never auto-delete:** 0100d53d 022820cb 0505ce7d 050cb713 058eda4e 062bfba3 0998ad05 09f3dc47 0a79bc28 0d48a707 0d678e51 0de0cc0c 0e3d0465 0eb35cd2 1300c9b8 1302a18c 151b4e13 157b7f62 17c504bb 1857c5d9 1a5f192c 1ac18f06 1bcc1479 1d0539bc 1d339bcd 1ef5f77e 1f1c67b6 1f39ac17 1fff5c74 2031f04a 2198db1d 261a4a51 27871064 27d48d06 27dd8efb 2853f966 28dcbf6d 298f4c9f 29bb901f 2b553928 2f178455 318daeb7 31ea7c27 32c1f938 341c1bad 34794d32 360e223e 3678a831 3775565a 37ec0983 38caa745 3986b2f9 3a106641 3bf6c1f4 3cfdd4a3 3d5a2cc2 40597978 426eb7b7 48888860 49545b22 49e32a6e 4a1a4c4a 4adbee37 4d9c7019 4e9f5c29 4f849a0e 4fe68cac 5120c098 51e46dfa 578728aa 59550e71 59594204 59909fb5 5a8dada2 5bc00d31 5c9eda0c 5e3ba4d4 5f4d2a5b 60616b1c 62d06f4f 636d4b7e 6409a3c7 64734e3f 6630ad4e 687a7f27 69514278 6a536d8d 6a7c1d47 6b028949 6b037196 6b457b41 6c8bdb64 6cb9c0d9 6d2a2060 6e332582 700c3a06 706a7582 72b7c77e 73423389 75089dd7 77bbad75 79253363 7a33c7c8 7a479eaf 7ae56a95 7caf792c 7dd45165 7ef54b1c 7fa08907 7fb01e03 7fd8b013 80155ada 8224692f 82a076e4 835dd574 848385e4 84de0093 85f476c7 88485f49 8ac49e37 8b6b42a9 8b8a542d 90228735 903bf8a7 9156f49a 957902d6 968d7fd5 96ae83d8 9775fe45 9823bd64 98d80cb8 990d4a82 9979ed47 998b6d6a 9edf8224 9f69ac66 a1d37ec8 a24a626a a5e37919 a6276b12 aa4febb4 aaab8f81 ad020e8c addeb569 adeb92eb ae40541f af90420b aff1b9c6 b03a74a0 b1ffd29e b23741b2 b23d0fe2 b4e6383d b9c5b5e6 b9ca189f ba063df7 ba318e38 babf30bb bb485c68 bb9da5d3 bbf1854c be035022 bf31273c c03af147 c0543045 c4a9ddcc c60d9c3b c9a3d250 ca4eef9c cb060b4e cb16cc6f cc0dab3d cd1d39f7 ce048398 cebd6310 cecb5ee5 d00498a9 d09d94da d1ab684b d2117ffc d2a0d121 d3319023 d39c5ba8 d77c21f4 d98e015b d9b02a4c d9b1f124 d9db97b9 dae3ad6b dd3ada6f dd3c58b5 dde7cd59 e0b220e4 e1c0cc33 e229b8cb e32907bf e3316373 e406088d e5d0294c e67555ad e839b406 e9b646bf ea257e64 ec0d8cc3 ecea46a3 edd44e28 ef7c4ee0 f1139c44 f122b9de f1f3b5a8 f32006d5 f37f0252 f450ecba f4ef4af4 f5408ebf f7cf0fe7 f94c8fa4 fa0fe66c fa16db12 fa71cb77 fb15a6fe fb9092cf fc62d370 fcf948b8 fe37038c ff25672d ff5ae210 

## Deleted

Remote branches (58): 0506a1cd 08084c5f 0c270f73 0d279883 1fe6de8e 24bc78b2 2837720d 2be23858 2c621fdc 320b47d9 35b8f15d 364eea2f 4745f0d3 492a3ddb 53529ab7 5b76bd03 6123de4a 6195a018 65d09da0 66d7aac9 7102ff54 743f9bdd a8685a72 ad64242f b09e4b51 b2059dec b84e8f78 b98941a2 bf99de0b ca27166b da543237 da9b64d7 e070938a e730ae22 f5d7a783 1668ddd6 1c52bd82 22b951cd 29c28d0c 3e89af3c 49cc02b8 57758b77 5b2256d5 6801de9a 68a64314 77629b10 778fa5d5 ac059bce b5e6521b bb813687 c01a66ce ce55c8ef d4689430 dafa05c9 e9516943 efb4ff41 efebc413 fcb1344d 

Worktrees removed (30): f5d7a783 07cabe87 0bebfaa7 1668ddd6 1c52bd82 22b951cd 23559109 29c28d0c 3e89af3c 3fefbc65 49cc02b8 57758b77 5b2256d5 6801de9a 68a64314 6cfbb6f5 77629b10 778fa5d5 ac059bce b5e6521b bb813687 c5885cc9 c99e35fa ce55c8ef d4689430 dafa05c9 e9516943 efb4ff41 efebc413 fcb1344d 

## NEEDS-A-LOOK from C/D: L2, branch holds content never seen in master (70)

| shortId | grp | status | card | remote | worktree | remote verdict | worktree verdict | branch version of touched files seen in master history (remote \| wt) |
|---|---|---|---|---|---|---|---|---|
| 26d9c3d8 | C | Succeeded | CARD-0590 | LOOK | - | CONFLICT;touched=54;differNow=26 | - | seen=50 unseen=4 docker/session-runner-grok/Dockerfile,docs/bootstrap.md,docs/testing-and-build.md,tests/Antiphon.Tests/Application/GrokDelegateDispatchTests.cs |
| 2b722beb | C | Succeeded | CARD-0642 | LOOK | - | CONFLICT;touched=13;differNow=12 | - | seen=11 unseen=2 server/Application/Services/AgentTaskLandService.cs,server/Infrastructure/Git/LandingGit.cs, | wt: - |
| 5b8e6931 | C | Succeeded | CARD-0664 | LOOK | - | CONFLICT;touched=12;differNow=3 | - | seen=11 unseen=1 server/Program.cs, | wt: - |
| 7404e327 | C | Succeeded | CARD-0735 | LOOK | - | CONFLICT;touched=9;differNow=4 | - | seen=8 unseen=1 docs/testing-and-build.md, | wt: - |
| 7e73872f | C | Succeeded | CARD-0467 | LOOK | - | CONFLICT;touched=9;differNow=9 | - | seen=1 unseen=8 client/src/api/agentTasks.ts,client/src/features/delegations/TaskDrawer.test.tsx,tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs,tests/Antip |
| 86684fa2 | C | Succeeded | CARD-0718 | LOOK | - | CONFLICT;touched=42;differNow=10 | - | seen=41 unseen=1 server/appsettings.json, | wt: - |
| 8a0ddf38 | C | Succeeded | CARD-0711 | LOOK | - | CONFLICT;touched=24;differNow=10 | - | seen=19 unseen=5 docs/ops-http.md,docs/orchestration-loop.md,docs/session-runtime-invariants.md,server/Application/Settings/DelegationSettings.cs,server/appsett |
| 98f352ee | C | Succeeded | CARD-0723 | LOOK | LOOK | CONFLICT;touched=81;differNow=31 | SAMEASREMOTE | seen=79 unseen=2 docs/orchestration-loop.md,docs/testing-and-build.md, | wt: (=remote) |
| a1ee8274 | C | Succeeded | CARD-0679 | LOOK | - | CONFLICT;touched=22;differNow=9 | - | seen=21 unseen=1 server/Application/Services/AgentSessionService.cs, | wt: - |
| a2eb29f3 | C | Succeeded | CARD-0753 | LOOK | - | CONFLICT;touched=45;differNow=16 | - | seen=43 unseen=2 docs/orchestration-loop.md,scripts/delegate.ps1, | wt: - |
| ac940931 | C | Succeeded | CARD-0671 | LOOK | - | CONFLICT;touched=4;differNow=4 | - | seen=3 unseen=1 docs/testing-and-build.md, | wt: - |
| bc1b49f8 | C | Succeeded | CARD-0593 | LOOK | - | CONFLICT;touched=28;differNow=11 | - | seen=25 unseen=3 docs/session-runtime-invariants.md,server/Infrastructure/Data/AppDbContext.cs,server/Migrations/AppDbContextModelSnapshot.cs, | wt: - |
| cac8ee15 | C | Succeeded | CARD-0677 | LOOK | - | CONFLICT;touched=4;differNow=2 | - | seen=3 unseen=1 docs/bootstrap.md, | wt: - |
| cb2632b3 | C | Succeeded | CARD-0661 | LOOK | - | CONFLICT;touched=8;differNow=8 | - | seen=7 unseen=1 docs/orchestration-loop.md, | wt: - |
| d0498628 | C | Succeeded | CARD-0649 | LOOK | - | CONFLICT;touched=8;differNow=5 | - | seen=7 unseen=1 server/Application/Services/AgentTaskDispatcher.cs, | wt: - |
| db094bcc | C | Succeeded | CARD-0660 | LOOK | - | CONFLICT;touched=21;differNow=16 | - | seen=18 unseen=3 server/Application/Services/AgentTaskDispatcher.cs,server/Application/Services/AgentTaskService.cs,tests/Antiphon.Tests/Application/PhoneHomeTa |
| db8122fd | C | Succeeded | CARD-0679 | LOOK | - | CONFLICT;touched=9;differNow=5 | - | seen=8 unseen=1 src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs, | wt: - |
| e9f20adc | C | Succeeded | CARD-0650 | LOOK | - | CONFLICT;touched=21;differNow=11 | - | seen=19 unseen=2 server/Infrastructure/Data/AppDbContext.cs,server/Migrations/AppDbContextModelSnapshot.cs, | wt: - |
| f5fe113e | C | Succeeded | CARD-0759 | LOOK | LOOK | CONFLICT;touched=29;differNow=2 | SAMEASREMOTE | seen=28 unseen=1 docs/testing-and-build.md, | wt: (=remote) |
| 0bebfaa7 | D | Failed | CARD-0593 | LOOK | REMOVED | CONFLICT;touched=28;differNow=11 | EMPTY | seen=25 unseen=3 docs/session-runtime-invariants.md,server/Infrastructure/Data/AppDbContext.cs,server/Migrations/AppDbContextModelSnapshot.cs, | wt: - |
| 0f6a815b | D | Succeeded | CARD-0718 | LOOK | LOOK | CONFLICT;touched=23;differNow=13 | SAMEASREMOTE | seen=22 unseen=1 src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs, | wt: (=remote) |
| 130b15ef | D | Failed | CARD-0593 | LOOK | LOOK | CONFLICT;touched=28;differNow=13 | CONFLICT | seen=25 unseen=3 docs/session-runtime-invariants.md,server/Infrastructure/Data/AppDbContext.cs,server/Migrations/AppDbContextModelSnapshot.cs, | wt: seen=24 uns |
| 1753ea5c | D | Succeeded | CARD-0664 | LOOK | - | CONFLICT;touched=11;differNow=3 | - | seen=10 unseen=1 server/Program.cs, | wt: - |
| 1e77c1d1 | D | Succeeded | CARD-0666 | LOOK | - | CONFLICT;touched=13;differNow=8 | - | seen=8 unseen=5 server/Application/Services/AgentTaskService.cs,server/Infrastructure/Git/LandingGit.cs,server/Infrastructure/Git/RepositoryChildJournal.cs,serv |
| 263541a1 | D | Succeeded | CARD-0653 | LOOK | - | CONFLICT;touched=29;differNow=28 | - | seen=22 unseen=7 docs/ops-http.md,server/Application/Services/AgentTaskDispatcher.cs,server/Application/Services/DispatchHoldDetails.cs,server/Infrastructure/Ag |
| 2824ba4c | D | Succeeded | CARD-0650 | LOOK | - | CONFLICT;touched=15;differNow=12 | - | seen=14 unseen=1 docs/antiphon-api.md, | wt: - |
| 2b6ade7e | D | Canceled | CARD-0679 | LOOK | - | CONFLICT;touched=15;differNow=11 | - | seen=14 unseen=1 server/Application/Services/SessionMessageQueueService.cs, | wt: - |
| 2cc36aa6 | D | Succeeded | CARD-0735 | LOOK | LOOK | CONFLICT;touched=9;differNow=6 | SAMEASREMOTE | seen=8 unseen=1 docs/testing-and-build.md, | wt: (=remote) |
| 3002779f | D | Succeeded | CARD-0442 | LOOK | LOOK | CONFLICT;touched=42;differNow=15 | SAMEASREMOTE | seen=36 unseen=6 docs/antiphon-api.md,docs/ops-http.md,server/Domain/Entities/AgentTask.cs,server/Infrastructure/Data/AppDbContext.cs,server/Migrations/AppDbCon |
| 32c16f61 | D | Succeeded | CARD-0653 | LOOK | - | CONFLICT;touched=38;differNow=34 | - | seen=35 unseen=3 server/Application/Services/AgentTaskDispatcher.cs,src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs,tests/Antiphon.SessionRunner.Tests/ |
| 33b9032e | D | Succeeded | CARD-0691 | LOOK | LOOK | CONFLICT;touched=17;differNow=13 | SAMEASREMOTE | seen=15 unseen=2 docs/bootstrap.md,docs/ops-http.md, | wt: (=remote) |
| 3416c8aa | D | Failed | CARD-0646 | LOOK | - | CONFLICT;touched=6;differNow=4 | - | seen=5 unseen=1 docs/testing-and-build.md, | wt: - |
| 387b2714 | D | Succeeded | CARD-0718 | LOOK | LOOK | CONFLICT;touched=23;differNow=4 | SAMEASREMOTE | seen=22 unseen=1 docs/ops-http.md, | wt: (=remote) |
| 3ad57002 | D | Succeeded | CARD-0749 | LOOK | LOOK | CONFLICT;touched=7;differNow=7 | SAMEASREMOTE | seen=6 unseen=1 docs/orchestration-loop.md, | wt: (=remote) |
| 48bbc16e | D | Succeeded | CARD-0664 | LOOK | - | CONFLICT;touched=6;differNow=5 | - | seen=3 unseen=3 server/Application/Services/AgentTaskDispatcher.cs,server/Application/Services/AgentTaskReplyService.cs,server/Application/Services/AgentTaskSer |
| 48d0495d | D | Succeeded | CARD-0749 | LOOK | LOOK | CONFLICT;touched=8;differNow=8 | SAMEASREMOTE | seen=7 unseen=1 docs/orchestration-loop.md, | wt: (=remote) |
| 4aa8cc21 | D | Canceled | CARD-0664 | LOOK | - | CONFLICT;touched=11;differNow=8 | - | seen=10 unseen=1 server/Application/Services/AgentSessionService.cs, | wt: - |
| 520c70c6 | D | Succeeded | CARD-0718 | LOOK | LOOK | NONEMPTY;touched=24;differNow=4 | SAMEASREMOTE | seen=23 unseen=1 docs/ops-http.md, | wt: (=remote) |
| 52e52800 | D | Succeeded |  | LOOK | LOOK | CONFLICT;touched=7;differNow=7 | SAMEASREMOTE | seen=6 unseen=1 docs/orchestration-loop.md, | wt: (=remote) |
| 539915bf | D | Canceled | CARD-0691 | LOOK | LOOK | CONFLICT;touched=20;differNow=12 | CONFLICT | seen=18 unseen=2 docs/bootstrap.md,docs/ops-http.md, | wt: seen=15 unseen=2 docs/bootstrap.md,docs/ops-http.md, |
| 58011e5e | D | Succeeded | CARD-0696 | LOOK | LOOK | CONFLICT;touched=26;differNow=13 | SAMEASREMOTE | seen=23 unseen=3 docs/ops-http.md,docs/session-runtime-invariants.md,server/Program.cs, | wt: (=remote) |
| 64db976e | D | Succeeded | CARD-0735 | LOOK | LOOK | CONFLICT;touched=9;differNow=6 | SAMEASREMOTE | seen=8 unseen=1 docs/testing-and-build.md, | wt: (=remote) |
| 65d9f56e | D | Failed | CARD-0566 | LOOK | LOOK | CONFLICT;touched=9;differNow=3 | CONFLICT | seen=7 unseen=2 docs/testing-and-build.md,server/Application/Services/AgentTaskDispatcher.cs, | wt: seen=1 unseen=0  |
| 67ddf317 | D | Succeeded | CARD-0735 | LOOK | LOOK | CONFLICT;touched=9;differNow=5 | SAMEASREMOTE | seen=8 unseen=1 docs/testing-and-build.md, | wt: (=remote) |
| 70269020 | D | Succeeded | CARD-0653 | LOOK | - | CONFLICT;touched=31;differNow=29 | - | seen=24 unseen=7 server/Application/Services/AgentTaskDispatcher.cs,server/Application/Services/DispatchHoldDetails.cs,server/Infrastructure/Agents/SessionRunne |
| 71060b3f | D | Succeeded | CARD-0738 | LOOK | LOOK | CONFLICT;touched=23;differNow=12 | SAMEASREMOTE | seen=22 unseen=1 docs/ops-http.md, | wt: (=remote) |
| 75491ae6 | D | Succeeded | CARD-0650 | LOOK | - | CONFLICT;touched=21;differNow=11 | - | seen=18 unseen=3 server/Application/Services/DispatchHoldDetails.cs,server/Infrastructure/Data/AppDbContext.cs,server/Migrations/AppDbContextModelSnapshot.cs, | |
| 81c898bc | D | Succeeded | CARD-0698 | LOOK | LOOK | CONFLICT;touched=17;differNow=12 | SAMEASREMOTE | seen=16 unseen=1 server/Migrations/AppDbContextModelSnapshot.cs, | wt: (=remote) |
| 8b51f4e4 | D | Canceled | CARD-0723 | LOOK | LOOK | CONFLICT;touched=81;differNow=34 | CONFLICT | seen=79 unseen=2 docs/orchestration-loop.md,docs/testing-and-build.md, | wt: seen=73 unseen=7 AGENTS.md,Antiphon.sln,docs/orchestration-loop.md,docs/testing-and |
| 8d32f2c5 | D | Succeeded | CARD-0718 | LOOK | LOOK | CONFLICT;touched=42;differNow=13 | SAMEASREMOTE | seen=41 unseen=1 server/appsettings.json, | wt: (=remote) |
| 8d4cd03e | D | Canceled | CARD-0679 | LOOK | - | CONFLICT;touched=18;differNow=12 | - | seen=16 unseen=2 server/Application/Services/SessionMessageQueueService.cs,tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs, | wt: - |
| 8f5d5036 | D | Succeeded | CARD-0698 | LOOK | LOOK | CONFLICT;touched=17;differNow=8 | SAMEASREMOTE | seen=16 unseen=1 server/Migrations/AppDbContextModelSnapshot.cs, | wt: (=remote) |
| 99efb48f | D | Succeeded | CARD-0759 | LOOK | LOOK | CONFLICT;touched=26;differNow=12 | SAMEASREMOTE | seen=25 unseen=1 docs/testing-and-build.md, | wt: (=remote) |
| 9e8d8472 | D | Succeeded | CARD-0716 | LOOK | LOOK | CONFLICT;touched=29;differNow=14 | SAMEASREMOTE | seen=27 unseen=2 AGENTS.md,server/Program.cs, | wt: (=remote) |
| a61c829b | D | Succeeded | CARD-0679 | LOOK | - | CONFLICT;touched=5;differNow=4 | - | seen=4 unseen=1 server/Application/Services/AgentSessionService.cs, | wt: - |
| a7d678ce | D | Succeeded | CARD-0679 | LOOK | - | CONFLICT;touched=11;differNow=9 | - | seen=10 unseen=1 server/Application/Services/AgentSessionService.cs, | wt: - |
| c087dcaf | D | Succeeded | CARD-0657 | LOOK | - | CONFLICT;touched=13;differNow=11 | - | seen=10 unseen=3 server/Application/Services/AgentTaskDispatcher.cs,server/Application/Services/RemoteWorkspaceService.cs,server/Program.cs, | wt: - |
| c99b5b44 | D | Succeeded | CARD-0696 | LOOK | LOOK | CONFLICT;touched=27;differNow=12 | SAMEASREMOTE | seen=24 unseen=3 docs/ops-http.md,docs/session-runtime-invariants.md,server/Program.cs, | wt: (=remote) |
| c99e35fa | D | Failed | CARD-0442 | LOOK | REMOVED | CONFLICT;touched=27;differNow=14 | EMPTY | seen=21 unseen=6 docs/antiphon-api.md,docs/ops-http.md,server/Domain/Entities/AgentTask.cs,server/Infrastructure/Data/AppDbContext.cs,server/Migrations/AppDbCon |
| d75b52ff | D | Succeeded | CARD-0759 | LOOK | LOOK | CONFLICT;touched=29;differNow=6 | SAMEASREMOTE | seen=28 unseen=1 docs/testing-and-build.md, | wt: (=remote) |
| d81207e7 | D | Failed | CARD-0661 | LOOK | - | CONFLICT;touched=8;differNow=8 | - | seen=7 unseen=1 docs/orchestration-loop.md, | wt: - |
| d9f039a1 | D | Succeeded | CARD-0716 | LOOK | LOOK | CONFLICT;touched=30;differNow=9 | SAMEASREMOTE | seen=28 unseen=2 AGENTS.md,server/Program.cs, | wt: (=remote) |
| e296379b | D | Succeeded | CARD-0642 | LOOK | - | CONFLICT;touched=13;differNow=12 | - | seen=11 unseen=2 server/Application/Services/AgentTaskLandService.cs,server/Infrastructure/Git/LandingGit.cs, | wt: - |
| ead491e1 | D | Succeeded | CARD-0726 | LOOK | LOOK | CONFLICT;touched=14;differNow=10 | SAMEASREMOTE | seen=11 unseen=3 docs/ops-http.md,docs/orchestration-loop.md,docs/session-runtime-invariants.md, | wt: (=remote) |
| ed1112b2 | D | Succeeded | CARD-0679 | LOOK | - | CONFLICT;touched=11;differNow=11 | - | seen=7 unseen=4 docs/session-runtime-invariants.md,server/Application/Services/AgentSessionRuntime.cs,server/Application/Settings/PhoneHomeRunnerSettings.cs,ser |
| ee823591 | D | Succeeded | CARD-0718 | LOOK | LOOK | CONFLICT;touched=22;differNow=6 | SAMEASREMOTE | seen=21 unseen=1 docs/ops-http.md, | wt: (=remote) |
| eec32965 | D | Succeeded | CARD-0642 | LOOK | - | CONFLICT;touched=12;differNow=11 | - | seen=10 unseen=2 server/Application/Services/AgentTaskLandService.cs,server/Infrastructure/Git/LandingGit.cs, | wt: - |
| eff1371c | D | Succeeded | CARD-0650 | LOOK | - | CONFLICT;touched=14;differNow=4 | - | seen=12 unseen=2 server/Infrastructure/Data/AppDbContext.cs,server/Migrations/AppDbContextModelSnapshot.cs, | wt: - |
| f0a7c7cd | D | Succeeded | CARD-0759 | LOOK | LOOK | CONFLICT;touched=27;differNow=10 | SAMEASREMOTE | seen=26 unseen=1 docs/testing-and-build.md, | wt: (=remote) |
| f727964e | D | Succeeded | CARD-0666 | LOOK | - | CONFLICT;touched=10;differNow=5 | - | seen=8 unseen=2 server/Application/Services/AgentTaskService.cs,server/Program.cs, | wt: - |

## NEEDS-A-LOOK from C/D: L1, superseded by later master edits (117)

> **Resolved by task 1807540f (below):** 116 of these 117 were re-verified file by file and deleted. `05a66230` stays in NEEDS-A-LOOK because its worktree holds a nested registered worktree. See "L1 follow-up: per-file re-verification and deletion (task 1807540f)".

The content check failed, but master history holds the branch's exact version of every touched file, so master most likely absorbed the branch and then changed further. These are very likely safe, but they were not deleted because the brief requires an empty diff against current master.

| shortId | grp | status | card | remote | worktree | remote verdict | worktree verdict | branch version of touched files seen in master history (remote \| wt) |
|---|---|---|---|---|---|---|---|---|
| 015dc1ee | C | Succeeded | CARD-0640 | LOOK | - | CONFLICT;touched=4;differNow=3 | - | seen=4 unseen=0  | wt: - |
| 05522ab3 | C | Succeeded | CARD-0550 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| 08e6f2d2 | C | Succeeded | CARD-0726 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| 176e26d6 | C | Succeeded | CARD-0651 | LOOK | - | CONFLICT;touched=5;differNow=4 | - | seen=5 unseen=0  | wt: - |
| 2060e274 | C | Succeeded | CARD-0653 | LOOK | - | CONFLICT;touched=40;differNow=34 | - | seen=40 unseen=0  | wt: - |
| 354b15be | C | Succeeded | CARD-0678 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| 3895b67b | C | Succeeded | CARD-0727 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| 39d0e7e2 | C | Succeeded | CARD-0767 | LOOK | - | CONFLICT;touched=46;differNow=5 | - | seen=46 unseen=0  | wt: - |
| 443b21c2 | C | Succeeded | CARD-0659 | LOOK | - | CONFLICT;touched=9;differNow=9 | - | seen=9 unseen=0  | wt: - |
| 520f3e0a | C | Succeeded | CARD-0647 | LOOK | - | CONFLICT;touched=17;differNow=9 | - | seen=17 unseen=0  | wt: - |
| 5dc73c10 | C | Succeeded | CARD-0650 | LOOK | - | CONFLICT;touched=15;differNow=2 | - | seen=15 unseen=0  | wt: - |
| 5ef9bb0e | C | Succeeded | CARD-0657 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| 609f77b4 | C | Succeeded | CARD-0718 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| 67e84356 | C | Succeeded | CARD-0714 | LOOK | - | CONFLICT;touched=2;differNow=1 | - | seen=2 unseen=0  | wt: - |
| 695a54a1 | C | Succeeded | CARD-0644 | LOOK | - | CONFLICT;touched=6;differNow=5 | - | seen=6 unseen=0  | wt: - |
| 7f644884 | C | Succeeded | CARD-0660 | LOOK | - | CONFLICT;touched=7;differNow=4 | - | seen=7 unseen=0  | wt: - |
| 8afa8f97 | C | Succeeded | CARD-0664 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| 924d72d5 | C | Succeeded | CARD-0558 | LOOK | - | CONFLICT;touched=2;differNow=1 | - | seen=2 unseen=0  | wt: - |
| a57a83df | C | Succeeded | CARD-0659 | LOOK | - | CONFLICT;touched=7;differNow=7 | - | seen=7 unseen=0  | wt: - |
| b18bb8e3 | C | Succeeded | CARD-0666 | LOOK | - | CONFLICT;touched=16;differNow=8 | - | seen=16 unseen=0  | wt: - |
| bc527408 | C | Succeeded | CARD-0727 | LOOK | - | CONFLICT;touched=33;differNow=15 | - | seen=33 unseen=0  | wt: - |
| bddfb3cc | C | Succeeded | CARD-0641 | LOOK | - | CONFLICT;touched=9;differNow=6 | - | seen=9 unseen=0  | wt: - |
| bf7e81ee | C | Succeeded | CARD-0660 | LOOK | - | CONFLICT;touched=15;differNow=8 | - | seen=15 unseen=0  | wt: - |
| d0abdf16 | C | Succeeded | CARD-0676 | LOOK | - | CONFLICT;touched=10;differNow=5 | - | seen=10 unseen=0  | wt: - |
| d5219854 | C | Succeeded | CARD-0679 | LOOK | - | CONFLICT;touched=2;differNow=1 | - | seen=2 unseen=0  | wt: - |
| ea7d1a1c | C | Succeeded | CARD-0726 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| ee01d18e | C | Succeeded | CARD-0688 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| f3e2953f | C | Succeeded | CARD-0679 | LOOK | - | CONFLICT;touched=7;differNow=7 | - | seen=7 unseen=0  | wt: - |
| f602d622 | C | Succeeded | CARD-0660 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| f646569d | C | Succeeded | CARD-0646 | LOOK | - | CONFLICT;touched=10;differNow=3 | - | seen=10 unseen=0  | wt: - |
| f908a771 | C | Succeeded | CARD-0727 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| fb612012 | C | Succeeded | CARD-0665 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| 00f66c8f | D | Succeeded | CARD-0727 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 02983cde | D | Canceled | CARD-0558 | LOOK | LOOK | CONFLICT;touched=54;differNow=7 | CONFLICT | seen=54 unseen=0  | wt: seen=54 unseen=0  |
| 05a66230 | D | Succeeded | CARD-0462 | - | LOOK | - | CONFLICT | - | wt: seen=49 unseen=3 server/Infrastructure/Data/AppDbContext.cs,server/Migrations/AppDbContextModelSnapshot.cs,server/Program.cs, |
| 06805739 | D | Succeeded | CARD-0691 | LOOK | LOOK | CONFLICT;touched=27;differNow=19 | SAMEASREMOTE | seen=27 unseen=0  | wt: (=remote) |
| 07df9f1d | D | Succeeded | CARD-0737 | LOOK | LOOK | CONFLICT;touched=7;differNow=7 | SAMEASREMOTE | seen=7 unseen=0  | wt: (=remote) |
| 0aa9717c | D | Failed | CARD-0452 | LOOK | LOOK | CONFLICT;touched=12;differNow=3 | CONFLICT | seen=12 unseen=0  | wt: seen=1 unseen=0  |
| 0cc1623f | D | Succeeded | CARD-0767 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 10632f25 | D | Succeeded | CARD-0657 | LOOK | - | CONFLICT;touched=5;differNow=5 | - | seen=5 unseen=0  | wt: - |
| 125f62dc | D | Succeeded | CARD-0735 | - | LOOK | - | CONFLICT | - | wt: seen=8 unseen=1 docs/testing-and-build.md, |
| 131508fb | D | Succeeded | CARD-0759 | - | LOOK | - | CONFLICT | - | wt: seen=26 unseen=1 docs/testing-and-build.md, |
| 15a86ac9 | D | Failed | CARD-0650 | LOOK | - | CONFLICT;touched=7;differNow=7 | - | seen=7 unseen=0  | wt: - |
| 169fe4dd | D | Succeeded | CARD-0659 | LOOK | - | CONFLICT;touched=8;differNow=8 | - | seen=8 unseen=0  | wt: - |
| 16d7e477 | D | Succeeded | CARD-0611 | LOOK | LOOK | CONFLICT;touched=3;differNow=2 | SAMEASREMOTE | seen=3 unseen=0  | wt: (=remote) |
| 19c7ff68 | D | Succeeded | CARD-0727 | LOOK | LOOK | CONFLICT;touched=8;differNow=4 | SAMEASREMOTE | seen=8 unseen=0  | wt: (=remote) |
| 1aa86155 | D | Succeeded | CARD-0705 | LOOK | LOOK | NONEMPTY;touched=5;differNow=3 | SAMEASREMOTE | seen=5 unseen=0  | wt: (=remote) |
| 1ceae9ac | D | Succeeded | CARD-0710 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 1d5b779b | D | Succeeded | CARD-0759 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 1d94da9e | D | Canceled | CARD-0650 | LOOK | - | CONFLICT;touched=16;differNow=5 | - | seen=16 unseen=0  | wt: - |
| 22c54e3f | D | Succeeded | CARD-0735 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 2cd14d4c | D | Failed | CARD-0666 | LOOK | - | CONFLICT;touched=3;differNow=2 | - | seen=3 unseen=0  | wt: - |
| 2dd9fbf3 | D | Succeeded | CARD-0457 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 2e841047 | D | Canceled | CARD-0693 | LOOK | - | CONFLICT;touched=3;differNow=3 | - | seen=3 unseen=0  | wt: - |
| 30103883 | D | Failed | CARD-0650 | LOOK | - | CONFLICT;touched=8;differNow=7 | - | seen=8 unseen=0  | wt: - |
| 359c1a1c | D | Succeeded | CARD-0735 | - | LOOK | - | CONFLICT | - | wt: seen=8 unseen=1 docs/testing-and-build.md, |
| 365d4862 | D | Succeeded | CARD-0464 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 36b868ff | D | Failed | CARD-0711 | - | LOOK | - | CONFLICT | - | wt: seen=22 unseen=0  |
| 37a70801 | D | Failed | CARD-0599 | LOOK | - | CONFLICT;touched=6;differNow=5 | - | seen=6 unseen=0  | wt: - |
| 3816b58e | D | Canceled | CARD-0681 | LOOK | - | CONFLICT;touched=9;differNow=3 | - | seen=9 unseen=0  | wt: - |
| 3c593d7c | D | Succeeded | CARD-0550 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| 3d4eb382 | D | Succeeded | CARD-0735 | - | LOOK | - | CONFLICT | - | wt: seen=8 unseen=1 docs/testing-and-build.md, |
| 47be9689 | D | Succeeded | CARD-0557 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 4ce3b0ad | D | Succeeded | CARD-0653 | LOOK | - | CONFLICT;touched=38;differNow=33 | - | seen=38 unseen=0  | wt: - |
| 50a2b232 | D | Succeeded | CARD-0768 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 51b599cb | D | Succeeded | CARD-0452 | LOOK | LOOK | CONFLICT;touched=19;differNow=3 | SAMEASREMOTE | seen=19 unseen=0  | wt: (=remote) |
| 5704b46b | D | Succeeded | CARD-0727 | - | LOOK | - | CONFLICT | - | wt: seen=33 unseen=0  |
| 5ef74c0f | D | Succeeded | CARD-0641 | LOOK | - | CONFLICT;touched=8;differNow=6 | - | seen=8 unseen=0  | wt: - |
| 69c7a060 | D | Succeeded | CARD-0767 | LOOK | LOOK | CONFLICT;touched=46;differNow=5 | SAMEASREMOTE | seen=46 unseen=0  | wt: (=remote) |
| 6ca40cd0 | D | Canceled | CARD-0727 | LOOK | LOOK | CONFLICT;touched=33;differNow=16 | CONFLICT | seen=33 unseen=0  | wt: seen=33 unseen=3 docs/ops-http.md,docs/session-runtime-invariants.md,server/Program.cs, |
| 6eed34e6 | D | Failed | CARD-0650 | LOOK | - | CONFLICT;touched=15;differNow=2 | - | seen=15 unseen=0  | wt: - |
| 70302dc6 | D | Succeeded | CARD-0660 | LOOK | - | CONFLICT;touched=12;differNow=8 | - | seen=12 unseen=0  | wt: - |
| 74da5d1b | D | Succeeded | CARD-0753 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 78c5f868 | D | Succeeded | CARD-0717 | LOOK | LOOK | CONFLICT;touched=2;differNow=1 | CONFLICT | seen=2 unseen=0  | wt: seen=2 unseen=0  |
| 7dc235bc | D | Succeeded | CARD-0647 | LOOK | - | CONFLICT;touched=16;differNow=13 | - | seen=16 unseen=0  | wt: - |
| 80103e29 | D | Succeeded | CARD-0726 | - | LOOK | - | CONFLICT | - | wt: seen=11 unseen=3 docs/ops-http.md,docs/orchestration-loop.md,docs/session-runtime-invariants.md, |
| 86d683e9 | D | Succeeded | CARD-0452 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 86e57e3d | D | Succeeded | CARD-0452 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 907b5d3e | D | Succeeded | CARD-0759 | - | LOOK | - | CONFLICT | - | wt: seen=28 unseen=1 docs/testing-and-build.md, |
| 92783ab1 | D | Succeeded | CARD-0599 | LOOK | - | CONFLICT;touched=6;differNow=5 | - | seen=6 unseen=0  | wt: - |
| 92b9022f | D | Succeeded | CARD-0711 | - | LOOK | - | CONFLICT | - | wt: seen=19 unseen=5 docs/ops-http.md,docs/orchestration-loop.md,docs/session-runtime-invariants.md,server/Application/Settings/DelegationSettings.cs,server |
| 94d4b1de | D | Succeeded | CARD-0566 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| 993405ad | D | Failed | CARD-0640 | LOOK | - | CONFLICT;touched=4;differNow=4 | - | seen=4 unseen=0  | wt: - |
| 9b83888e | D | Canceled | CARD-0684 | LOOK | - | CONFLICT;touched=1;differNow=1 | - | seen=1 unseen=0  | wt: - |
| 9e28086d | D | Failed | CARD-0658 | LOOK | - | CONFLICT;touched=20;differNow=11 | - | seen=20 unseen=0  | wt: - |
| a103fe6f | D | Succeeded | CARD-0651 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| a36875a7 | D | Succeeded | CARD-0650 | LOOK | - | CONFLICT;touched=8;differNow=7 | - | seen=8 unseen=0  | wt: - |
| a7ab0cda | D | Failed | CARD-0650 | LOOK | - | CONFLICT;touched=9;differNow=5 | - | seen=9 unseen=0  | wt: - |
| a8f042bb | D | Succeeded | CARD-0558 | LOOK | - | CONFLICT;touched=2;differNow=1 | - | seen=2 unseen=0  | wt: - |
| aadd05ab | D | Succeeded | CARD-0660 | LOOK | - | CONFLICT;touched=15;differNow=10 | - | seen=15 unseen=0  | wt: - |
| b0e67a1d | D | Succeeded | CARD-0566 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| b560c49a | D | Succeeded | CARD-0699 | LOOK | LOOK | CONFLICT;touched=16;differNow=7 | SAMEASREMOTE | seen=16 unseen=0  | wt: (=remote) |
| bca19885 | D | Succeeded | CARD-0657 | LOOK | - | CONFLICT;touched=5;differNow=5 | - | seen=5 unseen=0  | wt: - |
| c0a787fe | D | Succeeded | CARD-0464 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| c107ce5c | D | Succeeded | CARD-0504 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| c4154b22 | D | Succeeded | CARD-0593 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| c4b35a78 | D | Succeeded | CARD-0758 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| c5885cc9 | D | Canceled | CARD-0651 | LOOK | REMOVED | CONFLICT;touched=1;differNow=1 | EMPTY | seen=1 unseen=0  | wt: - |
| cb96c3a4 | D | Succeeded | CARD-0691 | LOOK | LOOK | CONFLICT;touched=28;differNow=16 | SAMEASREMOTE | seen=28 unseen=0  | wt: (=remote) |
| d24e1b4d | D | Canceled | CARD-0650 | LOOK | - | CONFLICT;touched=16;differNow=9 | - | seen=16 unseen=0  | wt: - |
| d6481d37 | D | Canceled | CARD-0711 | - | LOOK | - | CONFLICT | - | wt: seen=19 unseen=5 docs/ops-http.md,docs/orchestration-loop.md,docs/session-runtime-invariants.md,server/Application/Settings/DelegationSettings.cs,server |
| d76ef522 | D | Canceled | CARD-0651 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | CONFLICT | seen=1 unseen=0  | wt: seen=1 unseen=0  |
| d806b77b | D | Canceled | CARD-0664 | LOOK | - | CONFLICT;touched=6;differNow=5 | - | seen=6 unseen=0  | wt: - |
| db55819f | D | Succeeded | CARD-0727 | LOOK | LOOK | CONFLICT;touched=32;differNow=2 | SAMEASREMOTE | seen=32 unseen=0  | wt: (=remote) |
| db7a34db | D | Failed | CARD-0659 | LOOK | - | CONFLICT;touched=9;differNow=9 | - | seen=9 unseen=0  | wt: - |
| dbdfa428 | D | Succeeded | CARD-0655 | LOOK | - | CONFLICT;touched=4;differNow=4 | - | seen=4 unseen=0  | wt: - |
| dca8c233 | D | Failed | CARD-0661 | LOOK | - | CONFLICT;touched=6;differNow=6 | - | seen=6 unseen=0  | wt: - |
| e09045ba | D | Succeeded | CARD-0767 | LOOK | LOOK | CONFLICT;touched=46;differNow=5 | SAMEASREMOTE | seen=46 unseen=0  | wt: (=remote) |
| e8eae8df | D | Succeeded | CARD-0718 | - | LOOK | - | CONFLICT | - | wt: seen=41 unseen=1 server/appsettings.json, |
| f012a1d3 | D | Succeeded | CARD-0558 | LOOK | - | CONFLICT;touched=2;differNow=1 | - | seen=2 unseen=0  | wt: - |
| f67e6efa | D | Succeeded | CARD-0647 | LOOK | - | CONFLICT;touched=16;differNow=10 | - | seen=16 unseen=0  | wt: - |
| f7833ef2 | D | Canceled | CARD-0660 | LOOK | - | CONFLICT;touched=12;differNow=6 | - | seen=12 unseen=0  | wt: - |
| fa3a1135 | D | Failed | CARD-0599 | LOOK | - | CONFLICT;touched=5;differNow=4 | - | seen=5 unseen=0  | wt: - |
| fa8d1265 | D | Succeeded | CARD-0759 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| fc89bde9 | D | Succeeded | CARD-0711 | LOOK | LOOK | CONFLICT;touched=2;differNow=1 | SAMEASREMOTE | seen=2 unseen=0  | wt: (=remote) |
| fcb2e819 | D | Succeeded | CARD-0772 | LOOK | LOOK | CONFLICT;touched=1;differNow=1 | SAMEASREMOTE | seen=1 unseen=0  | wt: (=remote) |
| fd089677 | D | Succeeded | CARD-0599 | LOOK | - | CONFLICT;touched=7;differNow=1 | - | seen=7 unseen=0  | wt: - |

## L1 follow-up: per-file re-verification and deletion (task 1807540f)

This section covers only the 117 L1 IDs above. Groups A, B, F and L2 were not touched. Every ID was re-verified from scratch against `origin/master` `49a7c8a8`. Master had only moved forward by deletion time; `49a7c8a8` was an ancestor of the fetched tip.

### Method

- **Live status:** `GET /api/agent-tasks/{shortId}` `.summary.status` was queried at verification. It was queried again immediately before each deletion, retrying empty responses. Seventeen first-pass queries came back empty while the server was momentarily unresponsive; each was retried and returned a terminal status. Only Succeeded, Failed and Canceled were acted on. The branch and worktree came from the task's own `worktreeBranch`, from `git worktree list` and from `ls-remote`.
- **Per-file check:** each ref was checked separately: the remote tip, and the worktree HEAD when it differed from the remote tip. For each ref, `mb = merge-base(origin/master, ref)` and every file in `git diff --name-status --no-renames mb ref` was classified:
  1. `identical`: master's current blob equals the branch's blob.
  2. `blob-in-history`: the branch's exact blob appears as a new blob in `git log --full-history -m --raw mb..origin/master -- <file>`. Master held exactly this version after the merge base and then changed it further.
  3. `lines-only`: the blob never appeared. Every non-blank line the branch added (`git diff -U0 mb ref -- <file>`, `+` lines) is present in current master's file, or among the lines master added to that file in `mb..origin/master`.
  4. A deleted file passes only when master no longer has it, or master deleted it after `mb`. No L1 branch deleted a file.
  Any file that failed all of these would have moved its whole ID to NEEDS-A-LOOK.
- **Worktree:** it must be clean (`status --porcelain --untracked-files=all` empty), unlocked, have an unchanged HEAD at deletion time and contain no nested registered worktree. It was removed with `git -c core.longpaths=true worktree remove --force` (clean only), then `git worktree prune`.
- **Remote deletion:** `git push --force-with-lease=refs/heads/<br>:<verified sha> origin --delete <br>`, preceded by an `ls-remote` check that the tip had not moved.
- **Reachability:** local `feat/card-task-*` refs were kept. Eleven removed worktrees had a detached HEAD, or one on another branch (`review-rebased`), that no `feat/card-task-<id>` ref contained. Those HEADs were pinned as `refs/cleanup-kept/wt-head/<id>` in `C:\src\Antiphon`. Twenty-six deleted remote tips were not contained in any local branch and were pinned as `refs/cleanup-kept/remote-tip/<id>`. No verified commit became unreachable. After a final review, `git update-ref -d` drops a pin.

### Counts

| Item | Count |
|---|---|
| L1 task IDs examined | 117 (Succeeded 92, Failed 13, Canceled 12) |
| Skipped because active | 0 |
| Verified fully superseded (every touched file passed) | 116 |
| Moved to NEEDS-A-LOOK | 1 |
| Remote branches deleted | 105 (0 failures; `ls-remote` confirms none of the 116 remain) |
| Worktrees removed | 49 (0 failures, none dirty or locked; no directories left) |
| Task IDs fully cleaned | 116 |
| Files checked (remote and worktree refs combined) | 1,333: 699 identical, 609 blob-in-history, 25 lines-only, 0 failed |

The 25 lines-only files span 11 worktree-only IDs: `05a66230` `125f62dc` `131508fb` `359c1a1c` `3d4eb382` `6ca40cd0` `80103e29` `907b5d3e` `92b9022f` `d6481d37` `e8eae8df`. The prior pass listed them with `unseen>0` because the worktree had been rebased, so its blob combined the branch change with a master state that never existed exactly. Each diff was also read by hand. The added lines are all in master; examples are `LandTargetRaceRetries`, the CARD-0727 drain invariants, the CARD-0726 alarm docs and `HostStats` settings. The lines the branch removed are gone from master too. In `131508fb`, `907b5d3e` and `6ca40cd0`, one or two added lines exist only in master history, because master later rewrote those lines. An example is the checkpoint-tool exit-code line, which now includes `7 owning task ended`.

### NEEDS-A-LOOK (1)

- `05a66230` (CARD-0462, Succeeded). The worktree-only HEAD `048b6a18` passes all 52 files. It was not removed because `C:\Antiphon\worktrees\card-task-05a66230\.antiphon\c462-base` is a nested registered worktree (detached at `7a7dac4a`). Removing the parent would delete it. It has no remote branch.

### Deleted

Remote branches (105): 015dc1ee 05522ab3 08e6f2d2 176e26d6 2060e274 354b15be 3895b67b 39d0e7e2 443b21c2 520f3e0a 5dc73c10 5ef9bb0e 609f77b4 67e84356 695a54a1 7f644884 8afa8f97 924d72d5 a57a83df b18bb8e3 bc527408 bddfb3cc bf7e81ee d0abdf16 d5219854 ea7d1a1c ee01d18e f3e2953f f602d622 f646569d f908a771 fb612012 00f66c8f 02983cde 06805739 07df9f1d 0aa9717c 0cc1623f 10632f25 15a86ac9 169fe4dd 16d7e477 19c7ff68 1aa86155 1ceae9ac 1d5b779b 1d94da9e 22c54e3f 2cd14d4c 2dd9fbf3 2e841047 30103883 365d4862 37a70801 3816b58e 3c593d7c 47be9689 4ce3b0ad 50a2b232 51b599cb 5ef74c0f 69c7a060 6ca40cd0 6eed34e6 70302dc6 74da5d1b 78c5f868 7dc235bc 86d683e9 86e57e3d 92783ab1 94d4b1de 993405ad 9b83888e 9e28086d a103fe6f a36875a7 a7ab0cda a8f042bb aadd05ab b0e67a1d b560c49a bca19885 c0a787fe c107ce5c c4154b22 c4b35a78 c5885cc9 cb96c3a4 d24e1b4d d76ef522 d806b77b db55819f db7a34db dbdfa428 dca8c233 e09045ba f012a1d3 f67e6efa f7833ef2 fa3a1135 fa8d1265 fc89bde9 fcb2e819 fd089677

Worktrees removed (49): 00f66c8f 02983cde 06805739 07df9f1d 0aa9717c 0cc1623f 125f62dc 131508fb 16d7e477 19c7ff68 1aa86155 1ceae9ac 1d5b779b 22c54e3f 2dd9fbf3 359c1a1c 365d4862 36b868ff 3d4eb382 47be9689 50a2b232 51b599cb 5704b46b 69c7a060 6ca40cd0 74da5d1b 78c5f868 80103e29 86d683e9 86e57e3d 907b5d3e 92b9022f 94d4b1de a103fe6f b0e67a1d b560c49a c0a787fe c107ce5c c4154b22 c4b35a78 cb96c3a4 d6481d37 d76ef522 db55819f e09045ba e8eae8df fa8d1265 fc89bde9 fcb2e819

### Evidence files

- `2026-09-28-branch-worktree-cleanup-l1.tsv`: one row per L1 ID with status, branch, verified SHAs, verdict, the actions taken, per-class file counts and pins.
- `2026-09-28-branch-worktree-cleanup-l1-files.tsv`: one row per checked file with its class and evidence (`added`, and `missingNow`, the count of branch-added lines not in current master).
