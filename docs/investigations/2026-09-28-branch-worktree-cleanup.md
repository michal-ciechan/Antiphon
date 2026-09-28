# Branch / worktree cleanup - 2026-09-28 (task b1bb8fc5)

Housekeeping run over `feat/card-task-<8hex>` remote branches and `C:\Antiphon\worktrees\card-task-<8hex>` worktrees. Rules: live `GET /api/agent-tasks/{shortId}` re-checked immediately before each deletion; terminal = Succeeded/Failed/Canceled (Canceled treated as terminal; Blocked never auto-deleted); SAFE = remote tip is an ancestor of `origin/master` OR equals the task's recorded `worktreeBaseSha`; worktrees removed only when clean (`git status --porcelain --untracked-files=all` empty), HEAD unchanged since classification, and HEAD in master / == base / == a SAFE remote tip. Remote deletes used `--force-with-lease` on the classified SHA. Local branch refs were NOT deleted (removing a worktree keeps `feat/card-task-*` locally, so no commit became unreachable). `origin/master` at classification: `cd133de9`.

## Totals

| Item | Count |
|---|---|
| Remote heads (all) | 632 |
| Remote heads matching `feat/card-task-<8hex>` | 611 |
| Worktree dirs under C:\Antiphon\worktrees (all) | 506 |
| Matching `card-task-<8hex>` worktree dirs / registrations | 486 |
| Non-matching dirs (skipped by rule 1) | 20 |
| Distinct task shortIds examined | 769 |
| Branches classified SAFE-DELETE | 200 |
| Remote branches deleted | 200 (+0 already gone at delete time) |
| Worktrees classified SAFE-DELETE | 200 |
| Worktrees removed | 200 (5 of them needed the recovery in the next section) |
| Removal failures (all subsequently recovered) | 6 |
| SAFE candidates skipped at execution (live re-check) | 0 |
| Task ids with anything NEEDS-A-LOOK (untouched) | 503 |
| NEEDS-A-LOOK remote branches | 414 |
| NEEDS-A-LOOK worktrees | 285 |

## Removal failures and execution skips

- `025d739b` (Succeeded): plain `git worktree remove` failed deleting files (Filename too long; core.longpaths unset) but git still deleted the admin dir, leaving an unregistered checkout; after re-checking task terminal, finished with `rd /s /q \?\<path>`
- `42c6b627` (Canceled): plain `git worktree remove` failed deleting files (Filename too long; core.longpaths unset) but git still deleted the admin dir, leaving an unregistered checkout; after re-checking task terminal, finished with `rd /s /q \?\<path>`
- `496f4fcd` (Succeeded): plain `git worktree remove` failed deleting files (Filename too long; core.longpaths unset) but git still deleted the admin dir, leaving an unregistered checkout; after re-checking task terminal, finished with `rd /s /q \?\<path>`
- `95d13463` (Succeeded): first push got GitHub 500 (remote rejected, Internal Server Error); retry with same lease succeeded
- `a31d53c1` (Failed): plain `git worktree remove` failed deleting files (Filename too long; core.longpaths unset) but git still deleted the admin dir, leaving an unregistered checkout; after re-checking task terminal, finished with `rd /s /q \?\<path>`
- `ed926edd` (Succeeded): removal was interrupted by my 560s batch timeout mid-delete (.git file gone, admin dir present); re-checked terminal, finished with `rd /s /q`, then `git worktree prune`

Net effect: every SAFE item ended deleted; no removal remains outstanding. Root cause of the long-path failures: `core.longpaths` is not set on C:\src\Antiphon, so `git worktree remove` cannot delete deep node_modules/bin paths and (git behaviour) still drops `.git/worktrees/<id>`. Later batches ran with `git -c core.longpaths=true`. Any future cleanup tooling should do the same.

## NEEDS-A-LOOK

Grouped by likely disposition. `ahead` = commits in tip not in origin/master; `cherry+` = of those, commits with no patch-equivalent in master; `files touched/differ now` = files the branch changed since its merge-base / how many of those differ from current origin/master (0 means master already has identical content for every touched file; non-zero is often just later master edits). `land` = recorded landing publication. Branch/worktree columns: LOOK = kept for review, SAFE = was deleted (only its other half needs a look), `-` = does not exist.

### A. Task not terminal (live/queued/blocked) - never touched (6)

| shortId | branch | status | card | remote | worktree | ahead | cherry+ | files touched/differ now | land | why |
|---|---|---|---|---|---|---|---|---|---|---|
| 34a18470 | feat/card-task-34a18470 | Dispatched | (earlier server2 attempt of this same cleanup task) | LOOK | LOOK | 0 | 0 | 0/ |  | task status Dispatched (not terminal) |
| 77a6073c | feat/card-task-77a6073c | Dispatched | CARD-0519 | LOOK | LOOK | 2 | 2 | 31/31 |  | task status Dispatched (not terminal) |
| a01b0301 | feat/card-task-a01b0301 | Blocked | CARD-0072 | LOOK | LOOK |  |  | / |  | task status Blocked (not terminal) |
| bc2b4ac7 | feat/card-task-bc2b4ac7 | Blocked |  | LOOK | LOOK | 0 | 0 | 0/ |  | task status Blocked (not terminal) |
| fb8fa4a9 | feat/card-task-fb8fa4a9 | Dispatched | CARD-0417 | LOOK | LOOK | 1 | 1 | 3/3 |  | task status Dispatched (not terminal) |
| ff6c2f00 | feat/card-task-ff6c2f00 | Queued |  | LOOK | LOOK |  |  | / |  | task status Queued (not terminal) |

### B. Worktree dirty, locked, or orphan directory (21)

| shortId | branch | status | card | remote | worktree | ahead | cherry+ | files touched/differ now | land | why |
|---|---|---|---|---|---|---|---|---|---|---|
| 047193d4 | feat/card-task-047193d4 | Succeeded | CARD-0661 | - | LOOK | 4 | 0 | 8/8 |  | worktree locked: initializing; uncommitted changes: D  .antiphon/task-2c40e79f.md; D  .claude/settings.json; D  .claude/skills/antiphon-delegate/SKILL.md; D  .claude/skills/antiphon-orchestrator/SKILL.md; D  .claude/skil |
| 0f9c42e4 | feat/card-task-0f9c42e4 | Canceled | CARD-0462 | - | LOOK | 22 | 0 | 53/18 |  | uncommitted changes: M tests/Antiphon.Tests/TestHelpers/HerdrLabelFollowHttpFixture.cs; worktree HEAD d6726dff09 not in origin/master, != base, != safe remote tip |
| 26e2c676 | feat/card-task-26e2c676 | Failed | CARD-0072 | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| 273b3a0d | feat/card-task-273b3a0d | Canceled | CARD-0688 | LOOK | LOOK | 18 | 1 | 61/29 |  | remote tip f188a3dce1 not ancestor of origin/master and != recorded base (45830f382e); worktree locked: initializing; uncommitted changes: D  .antiphon/task-2c40e79f.md; D  .claude/settings.json; D  .claude/skills/antiph |
| 31002bb4 | feat/card-task-31002bb4 | Succeeded | CARD-0072 | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| 35eb1011 | feat/card-task-35eb1011 | Succeeded |  | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| 3bfe742a | feat/card-task-3bfe742a | Canceled | CARD-0475 | - | LOOK | 15 | 0 | 32/23 |  | uncommitted changes: D  docs/investigations/2026-09-11-card-0475-p2-verification-and-c448-v36-timeout.md; M  tests/Antiphon.Tests/Application/SessionQueueReceiptPlumbingTests.cs; M  tests/Antiphon.Tests/TestHelpers/Contr |
| 56dcfa48 | feat/card-task-56dcfa48 | Succeeded | CARD-0072 | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| 597838fe | feat/card-task-597838fe | Succeeded | CARD-0043 | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| 5e046d60 | feat/card-task-5e046d60 | Succeeded |  | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| 63b72299 | feat/card-task-63b72299 | Canceled | CARD-0092 | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| 66eec4dd | feat/card-task-66eec4dd | Succeeded | CARD-0043 | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| 704c7117 | feat/card-task-704c7117 | Succeeded | CARD-0043 | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| 8242ad6b | feat/card-task-8242ad6b | Canceled | CARD-0475 | - | LOOK | 15 | 0 | 32/23 |  | uncommitted changes: D  docs/investigations/2026-09-11-card-0475-p2-verification-and-c448-v36-timeout.md; worktree HEAD 49c3d2fe1a not in origin/master, != base, != safe remote tip |
| 83820e7c | feat/card-task-83820e7c | Succeeded | CARD-0045 | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| 8a18e4d8 | feat/card-task-8a18e4d8 | Succeeded | CARD-0070 | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| aebd0705 | feat/card-task-aebd0705 | Succeeded | CARD-0072 | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| bb20bcb9 | feat/card-task-bb20bcb9 | Canceled | CARD-0092 | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| c933f24a | feat/card-task-c933f24a | Succeeded | CARD-0047 | - | LOOK |  |  | / |  | directory exists but is not a registered git worktree (orphan dir) |
| cd2866b8 | feat/card-task-cd2866b8 | Canceled | CARD-0544 | - | LOOK | 0 | 0 | 0/ |  | uncommitted changes: M docs/superpowers/plans/2026-09-16-card-0544-interim-final-verification-plan.md |
| dfc6f878 | feat/card-task-dfc6f878 | Succeeded | CARD-0395 | - | LOOK | 17 | 1 | 95/64 |  | uncommitted changes: M server/Application/Services/CardService.cs;  M src/Antiphon.SessionRunner/HerdrPaneChild.cs;  M src/Antiphon.SessionRunner/SessionRunnerRuntime.cs;  M tests/Antiphon.SessionRunner.Tests/GrokRulesFi |

### C. Land recorded Landed/AlreadyPresent (rebased onto master, so tip is not an ancestor) - probably safe (86)

| shortId | branch | status | card | remote | worktree | ahead | cherry+ | files touched/differ now | land | why |
|---|---|---|---|---|---|---|---|---|---|---|
| 015dc1ee | feat/card-task-015dc1ee | Succeeded | CARD-0640 | LOOK | - | 2 | 0 | 4/3 | Landed | remote tip a0c6682e7c not ancestor of origin/master and != recorded base (b8aeaa51eb) |
| 0506a1cd | feat/card-task-0506a1cd | Succeeded | CARD-0681 | LOOK | - | 2 | 0 | 4/0 | Landed | remote tip 4d6b87782b not ancestor of origin/master and != recorded base (d511515702) |
| 05522ab3 | feat/card-task-05522ab3 | Succeeded | CARD-0550 | LOOK | - | 2 | 0 | 1/1 | Landed | remote tip b893f665ed not ancestor of origin/master and != recorded base (77e080f3a6) |
| 08084c5f | feat/card-task-08084c5f | Succeeded | CARD-0738 | LOOK | - | 1 | 0 | 1/0 | Landed | remote tip 02137b7407 not ancestor of origin/master and != recorded base (e4b4159d89) |
| 08e6f2d2 | feat/card-task-08e6f2d2 | Succeeded | CARD-0726 | LOOK | - | 18 | 0 | 1/1 | Landed | remote tip 396aa8161d not ancestor of origin/master and != recorded base (4fe3bce933) |
| 0c270f73 | feat/card-task-0c270f73 | Succeeded | CARD-0692 | LOOK | - | 1 | 0 | 1/0 | Landed | remote tip d3426fa848 not ancestor of origin/master and != recorded base (e0be7f5e62) |
| 0d279883 | feat/card-task-0d279883 | Succeeded | CARD-0718 | LOOK | - | 7 | 0 | 25/3 | Landed | remote tip 05f0253f90 not ancestor of origin/master and != recorded base (e343024ef9) |
| 176e26d6 | feat/card-task-176e26d6 | Succeeded | CARD-0651 | LOOK | - | 6 | 0 | 5/4 | Landed | remote tip 8f7bb031d1 not ancestor of origin/master and != recorded base (b2e0ad83eb) |
| 1fe6de8e | feat/card-task-1fe6de8e | Succeeded | CARD-0772 | LOOK | - | 11 | 0 | 8/2 | Landed | remote tip a0edbbdfe8 not ancestor of origin/master and != recorded base (ed11e83ffd) |
| 2060e274 | feat/card-task-2060e274 | Succeeded | CARD-0653 | LOOK | - | 11 | 0 | 40/34 | Landed | remote tip 96dc68c7de not ancestor of origin/master and != recorded base (f6207fbb3a) |
| 24bc78b2 | feat/card-task-24bc78b2 | Succeeded | CARD-0694 | LOOK | - | 1 | 0 | 1/0 | Landed | remote tip 434133060c not ancestor of origin/master and != recorded base (cbe635a531) |
| 26d9c3d8 | feat/card-task-26d9c3d8 | Succeeded | CARD-0590 | LOOK | - | 14 | 1 | 54/26 | Landed | remote tip c308a510f6 not ancestor of origin/master and != recorded base (f0972c42a6) |
| 2837720d | feat/card-task-2837720d | Succeeded | CARD-0464 | LOOK | - | 8 | 0 | 7/0 | Landed | remote tip d2e733a653 not ancestor of origin/master and != recorded base (897b3cc4b9) |
| 2b722beb | feat/card-task-2b722beb | Succeeded | CARD-0642 | LOOK | - | 12 | 0 | 13/12 | Landed | remote tip 4e6b27414a not ancestor of origin/master and != recorded base (b979a2cc62) |
| 2be23858 | feat/card-task-2be23858 | Succeeded | CARD-0611 | LOOK | - | 4 | 0 | 3/0 | Landed | remote tip b1e8a43686 not ancestor of origin/master and != recorded base (0a97e75eb2) |
| 2c621fdc | feat/card-task-2c621fdc | Succeeded | CARD-0457 | LOOK | - | 2 | 0 | 5/1 | Landed | remote tip 7d8d279f93 not ancestor of origin/master and != recorded base (eaee40f8fc) |
| 320b47d9 | feat/card-task-320b47d9 | Succeeded | CARD-0504 | LOOK | - | 6 | 0 | 5/0 | Landed | remote tip b52e76bb13 not ancestor of origin/master and != recorded base (ddf0c51a70) |
| 354b15be | feat/card-task-354b15be | Succeeded | CARD-0678 | LOOK | - | 2 | 0 | 1/1 | Landed | remote tip 514dfaaf58 not ancestor of origin/master and != recorded base (822a0d160f) |
| 35b8f15d | feat/card-task-35b8f15d | Succeeded | CARD-0662 | LOOK | - | 1 | 0 | 7/0 | Landed | remote tip 73c491dbcd not ancestor of origin/master and != recorded base (b151098235) |
| 364eea2f | feat/card-task-364eea2f | Succeeded | CARD-0768 | LOOK | - | 3 | 0 | 14/2 | Landed | remote tip 624492d445 not ancestor of origin/master and != recorded base (1a97191167) |
| 3895b67b | feat/card-task-3895b67b | Succeeded | CARD-0727 | LOOK | - | 6 | 0 | 1/1 | Landed | remote tip d65a17c9ef not ancestor of origin/master and != recorded base (4063a46d75) |
| 39d0e7e2 | feat/card-task-39d0e7e2 | Succeeded | CARD-0767 | LOOK | - | 54 | 0 | 46/5 | Landed | remote tip 673cd50c6d not ancestor of origin/master and != recorded base (f5632b80be) |
| 443b21c2 | feat/card-task-443b21c2 | Succeeded | CARD-0659 | LOOK | - | 9 | 0 | 9/9 | Landed | remote tip 5b1c2e0fbd not ancestor of origin/master and != recorded base (e51c7ff56b) |
| 4745f0d3 | feat/card-task-4745f0d3 | Succeeded | CARD-0644 | LOOK | - | 2 | 0 | 9/5 | Landed | remote tip b5329541c9 not ancestor of origin/master and != recorded base (25c093944d) |
| 492a3ddb | feat/card-task-492a3ddb | Succeeded | CARD-0679 | LOOK | - | 1 | 0 | 1/0 | Landed | remote tip 0595e8e93b not ancestor of origin/master and != recorded base (2b7024a134) |
| 520f3e0a | feat/card-task-520f3e0a | Succeeded | CARD-0647 | LOOK | - | 8 | 0 | 17/7 | Landed | remote tip 2b47c3d166 not ancestor of origin/master and != recorded base (0ba3c55a4b) |
| 53529ab7 | feat/card-task-53529ab7 | Succeeded | CARD-0696 | LOOK | - | 1 | 0 | 1/0 | Landed | remote tip b91a821a70 not ancestor of origin/master and != recorded base (e0be7f5e62) |
| 5b76bd03 | feat/card-task-5b76bd03 | Succeeded | CARD-0716 | LOOK | - | 2 | 0 | 2/0 | Landed | remote tip 47c5f72462 not ancestor of origin/master and != recorded base (07df1561e7) |
| 5b8e6931 | feat/card-task-5b8e6931 | Succeeded | CARD-0664 | LOOK | - | 7 | 0 | 12/3 | Landed | remote tip 8c377d6277 not ancestor of origin/master and != recorded base (44552e7e29) |
| 5dc73c10 | feat/card-task-5dc73c10 | Succeeded | CARD-0650 | LOOK | - | 12 | 0 | 15/2 | Landed | remote tip fcd657c2d9 not ancestor of origin/master and != recorded base (a0a5b7b624) |
| 5ef9bb0e | feat/card-task-5ef9bb0e | Succeeded | CARD-0657 | LOOK | - | 1 | 0 | 1/1 | Landed | remote tip 5f32849af5 not ancestor of origin/master and != recorded base (25c093944d) |
| 609f77b4 | feat/card-task-609f77b4 | Succeeded | CARD-0718 | LOOK | - | 14 | 0 | 1/1 | Landed | remote tip 59a93e5156 not ancestor of origin/master and != recorded base (fa86dc6420) |
| 6123de4a | feat/card-task-6123de4a | Succeeded | CARD-0701 | LOOK | - | 1 | 0 | 1/0 | Landed | remote tip 196f21e068 not ancestor of origin/master and != recorded base (316a4975a7) |
| 6195a018 | feat/card-task-6195a018 | Succeeded | CARD-0711 | LOOK | - | 12 | 0 | 2/0 | Landed | remote tip 75c7a683cd not ancestor of origin/master and != recorded base (989abc9f58) |
| 65d09da0 | feat/card-task-65d09da0 | Succeeded | CARD-0710 | LOOK | - | 2 | 0 | 1/0 | Landed | remote tip 38c96afb87 not ancestor of origin/master and != recorded base (f6519816f8) |
| 66d7aac9 | feat/card-task-66d7aac9 | Succeeded | CARD-0651 | LOOK | - | 4 | 0 | 1/0 | Landed | remote tip 1711e11156 not ancestor of origin/master and != recorded base (2b963bb777) |
| 67e84356 | feat/card-task-67e84356 | Succeeded | CARD-0714 | LOOK | - | 2 | 0 | 2/1 | Landed | remote tip 16f0a8caf3 not ancestor of origin/master and != recorded base (38fb98975a) |
| 695a54a1 | feat/card-task-695a54a1 | Succeeded | CARD-0644 | LOOK | - | 5 | 0 | 6/5 | Landed | remote tip 4c3f23efe0 not ancestor of origin/master and != recorded base (fb81f3cfe8) |
| 7102ff54 | feat/card-task-7102ff54 | Succeeded | CARD-0558 | LOOK | - | 1 | 0 | 1/0 | AlreadyPresent | remote tip 40527766b9 not ancestor of origin/master and != recorded base (3a62074ebf) |
| 7404e327 | feat/card-task-7404e327 | Succeeded | CARD-0735 | LOOK | - | 13 | 0 | 9/4 | Landed | remote tip 4646a8ba6b not ancestor of origin/master and != recorded base (6d986b061a) |
| 743f9bdd | feat/card-task-743f9bdd | Succeeded | CARD-0691 | LOOK | - | 1 | 0 | 1/0 | Landed | remote tip eef41cb471 not ancestor of origin/master and != recorded base (e6c94f8946) |
| 7e73872f | feat/card-task-7e73872f | Succeeded | CARD-0467 | LOOK | - | 7 | 6 | 9/9 | Landed | remote tip d0a26a5689 not ancestor of origin/master and != recorded base (da781133df) |
| 7f644884 | feat/card-task-7f644884 | Succeeded | CARD-0660 | LOOK | - | 2 | 0 | 7/4 | Landed | remote tip 125f8dda23 not ancestor of origin/master and != recorded base (d511515702) |
| 86684fa2 | feat/card-task-86684fa2 | Succeeded | CARD-0718 | LOOK | - | 12 | 0 | 42/10 | Landed | remote tip 6759af616f not ancestor of origin/master and != recorded base (315df03964) |
| 8a0ddf38 | feat/card-task-8a0ddf38 | Succeeded | CARD-0711 | LOOK | - | 21 | 0 | 24/10 | Landed | remote tip c206cd7fc4 not ancestor of origin/master and != recorded base (75c7a683cd) |
| 8afa8f97 | feat/card-task-8afa8f97 | Succeeded | CARD-0664 | LOOK | - | 1 | 0 | 1/1 | Landed | remote tip 2fc8b3d2ce not ancestor of origin/master and != recorded base (c8f4cdf9c4) |
| 924d72d5 | feat/card-task-924d72d5 | Succeeded | CARD-0558 | LOOK | - | 4 | 0 | 2/1 | Landed | remote tip 9bda7bb2d5 not ancestor of origin/master and != recorded base (3a62074ebf) |
| 98f352ee | feat/card-task-98f352ee | Succeeded | CARD-0723 | LOOK | LOOK | 13 | 0 | 81/31 | Landed | remote tip c2f58530cf not ancestor of origin/master and != recorded base (6da15ef9c8); worktree HEAD c2f58530cf not in origin/master, != base, != safe remote tip |
| a1ee8274 | feat/card-task-a1ee8274 | Succeeded | CARD-0679 | LOOK | - | 12 | 0 | 22/9 | Landed | remote tip 40f30b38d1 not ancestor of origin/master and != recorded base (68b3977375) |
| a2eb29f3 | feat/card-task-a2eb29f3 | Succeeded | CARD-0753 | LOOK | - | 17 | 0 | 45/16 | Landed | remote tip 61c765510a not ancestor of origin/master and != recorded base (0f2f355070) |
| a57a83df | feat/card-task-a57a83df | Succeeded | CARD-0659 | LOOK | - | 4 | 0 | 7/7 | Landed | remote tip 9f36fd341b not ancestor of origin/master and != recorded base (c8f4cdf9c4) |
| a8685a72 | feat/card-task-a8685a72 | Succeeded | CARD-0664 | LOOK | - | 1 | 0 | 1/0 | Landed | remote tip f8b914cb23 not ancestor of origin/master and != recorded base (42eecdea2c) |
| ac940931 | feat/card-task-ac940931 | Succeeded | CARD-0671 | LOOK | - | 2 | 0 | 4/4 | Landed | remote tip dc188f9cbe not ancestor of origin/master and != recorded base (822a0d160f) |
| ad64242f | feat/card-task-ad64242f | Succeeded | CARD-0726 | LOOK | - | 7 | 0 | 16/5 | Landed | remote tip 89a00a7bc4 not ancestor of origin/master and != recorded base (9c1c689803) |
| b09e4b51 | feat/card-task-b09e4b51 | Succeeded | CARD-0758 | LOOK | - | 6 | 0 | 5/1 | Landed | remote tip c1b319423c not ancestor of origin/master and != recorded base (a806ce19ab) |
| b18bb8e3 | feat/card-task-b18bb8e3 | Succeeded | CARD-0666 | LOOK | - | 14 | 0 | 16/8 | Landed | remote tip c642611c96 not ancestor of origin/master and != recorded base (6373eb7c62) |
| b2059dec | feat/card-task-b2059dec | Succeeded | CARD-0749 | LOOK | - | 6 | 0 | 9/3 | Landed | remote tip 6dfb0131c9 not ancestor of origin/master and != recorded base (772a050a77) |
| b84e8f78 | feat/card-task-b84e8f78 | Succeeded | CARD-0692 | LOOK | - | 1 | 0 | 3/0 | Landed | remote tip 171cdc1c85 not ancestor of origin/master and != recorded base (d2236e9ec3) |
| b98941a2 | feat/card-task-b98941a2 | Succeeded | CARD-0559 | LOOK | - | 2 | 0 | 2/1 | Landed | remote tip 4813063ac4 not ancestor of origin/master and != recorded base (670f1d6328) |
| bc1b49f8 | feat/card-task-bc1b49f8 | Succeeded | CARD-0593 | LOOK | - | 36 | 0 | 28/11 | Landed | remote tip de02b4d6a0 not ancestor of origin/master and != recorded base (c29adc51b4) |
| bc527408 | feat/card-task-bc527408 | Succeeded | CARD-0727 | LOOK | - | 13 | 0 | 33/15 | Landed | remote tip 0467ca3b79 not ancestor of origin/master and != recorded base (6b332e6f49) |
| bddfb3cc | feat/card-task-bddfb3cc | Succeeded | CARD-0641 | LOOK | - | 4 | 0 | 9/6 | Landed | remote tip cde78c6e3b not ancestor of origin/master and != recorded base (449a28c840) |
| bf7e81ee | feat/card-task-bf7e81ee | Succeeded | CARD-0660 | LOOK | - | 8 | 0 | 15/8 | Landed | remote tip 131c7b4a08 not ancestor of origin/master and != recorded base (2ee0387ae3) |
| bf99de0b | feat/card-task-bf99de0b | Succeeded | CARD-0728 | LOOK | - | 1 | 0 | 1/0 | Landed | remote tip c8caa818bd not ancestor of origin/master and != recorded base (598a522f92) |
| ca27166b | feat/card-task-ca27166b | Succeeded | CARD-0654 | LOOK | - | 1 | 0 | 1/0 | Landed | remote tip e246af8406 not ancestor of origin/master and != recorded base (57543b8556) |
| cac8ee15 | feat/card-task-cac8ee15 | Succeeded | CARD-0677 | LOOK | - | 3 | 0 | 4/2 | Landed | remote tip 1f2df231aa not ancestor of origin/master and != recorded base (822a0d160f) |
| cb2632b3 | feat/card-task-cb2632b3 | Succeeded | CARD-0661 | LOOK | - | 5 | 0 | 8/8 | Landed | remote tip b0590de160 not ancestor of origin/master and != recorded base (137b683125) |
| d0498628 | feat/card-task-d0498628 | Succeeded | CARD-0649 | LOOK | - | 1 | 0 | 8/4 | Landed | remote tip 64c1f1b6dc not ancestor of origin/master and != recorded base (c5e32c2f90) |
| d0abdf16 | feat/card-task-d0abdf16 | Succeeded | CARD-0676 | LOOK | - | 3 | 0 | 10/5 | Landed | remote tip ec4ff7ff54 not ancestor of origin/master and != recorded base (ada146ea69) |
| d5219854 | feat/card-task-d5219854 | Succeeded | CARD-0679 | LOOK | - | 2 | 0 | 2/1 | Landed | remote tip eca307e287 not ancestor of origin/master and != recorded base (0595e8e93b) |
| da543237 | feat/card-task-da543237 | Succeeded | CARD-0557 | LOOK | - | 12 | 0 | 23/3 | Landed | remote tip 1ebfb5fb6b not ancestor of origin/master and != recorded base (9b4aa8384a) |
| da9b64d7 | feat/card-task-da9b64d7 | Succeeded | CARD-0558 | LOOK | - | 10 | 0 | 54/6 | Landed | remote tip 42091a7f11 not ancestor of origin/master and != recorded base (f529bc7d51) |
| db094bcc | feat/card-task-db094bcc | Succeeded | CARD-0660 | LOOK | - | 5 | 0 | 21/16 | Landed | remote tip c4febb1504 not ancestor of origin/master and != recorded base (125f8dda23) |
| db8122fd | feat/card-task-db8122fd | Succeeded | CARD-0679 | LOOK | - | 4 | 0 | 9/5 | Landed | remote tip b43c3a5e30 not ancestor of origin/master and != recorded base (b9b5cfdf67) |
| e070938a | feat/card-task-e070938a | Succeeded | CARD-0642 | LOOK | - | 1 | 0 | 1/0 | Landed | remote tip 4f954303f4 not ancestor of origin/master and != recorded base (42eecdea2c) |
| e730ae22 | feat/card-task-e730ae22 | Succeeded | CARD-0589 | LOOK | - | 1 | 0 | 1/0 | Landed | remote tip 86beefe64c not ancestor of origin/master and != recorded base (e8874bfb56) |
| e9f20adc | feat/card-task-e9f20adc | Succeeded | CARD-0650 | LOOK | - | 9 | 0 | 21/11 | Landed | remote tip 777cc629b8 not ancestor of origin/master and != recorded base (6a231f038d) |
| ea7d1a1c | feat/card-task-ea7d1a1c | Succeeded | CARD-0726 | LOOK | - | 4 | 0 | 1/1 | Landed | remote tip 4fe3bce933 not ancestor of origin/master and != recorded base (bafc336632) |
| ee01d18e | feat/card-task-ee01d18e | Succeeded | CARD-0688 | LOOK | - | 1 | 0 | 1/1 | Landed | remote tip 44d1a8d7a5 not ancestor of origin/master and != recorded base (ada146ea69) |
| f3e2953f | feat/card-task-f3e2953f | Succeeded | CARD-0679 | LOOK | - | 3 | 0 | 7/7 | Landed | remote tip 7dc2bca118 not ancestor of origin/master and != recorded base (ffa87d01b8) |
| f5d7a783 | feat/card-task-f5d7a783 | Succeeded | CARD-0550 | LOOK | LOOK | 5 | 0 | 16/0 | Landed | remote tip 629d2dccd7 not ancestor of origin/master and != recorded base (8e6807783b); worktree HEAD 629d2dccd7 not in origin/master, != base, != safe remote tip |
| f5fe113e | feat/card-task-f5fe113e | Succeeded | CARD-0759 | LOOK | LOOK | 24 | 0 | 29/2 | Landed | remote tip 6663fab76e not ancestor of origin/master and != recorded base (0eb4cccac9); worktree HEAD 6663fab76e not in origin/master, != base, != safe remote tip |
| f602d622 | feat/card-task-f602d622 | Succeeded | CARD-0660 | LOOK | - | 1 | 0 | 1/1 | Landed | remote tip 597fe2693f not ancestor of origin/master and != recorded base (b151098235) |
| f646569d | feat/card-task-f646569d | Succeeded | CARD-0646 | LOOK | - | 3 | 0 | 10/3 | Landed | remote tip 145ac6028b not ancestor of origin/master and != recorded base (b7849c32cc) |
| f908a771 | feat/card-task-f908a771 | Succeeded | CARD-0727 | LOOK | - | 2 | 0 | 1/1 | Landed | remote tip 4063a46d75 not ancestor of origin/master and != recorded base (25e41f3551) |
| fb612012 | feat/card-task-fb612012 | Succeeded | CARD-0665 | LOOK | - | 2 | 0 | 1/1 | Landed | remote tip 7b1abc8dcf not ancestor of origin/master and != recorded base (42eecdea2c) |

### D. Every commit patch-equivalent to one in master (git cherry: 0 unmerged) - probably safe (163)

| shortId | branch | status | card | remote | worktree | ahead | cherry+ | files touched/differ now | land | why |
|---|---|---|---|---|---|---|---|---|---|---|
| 00f66c8f | feat/card-task-00f66c8f | Succeeded | CARD-0727 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip 25e41f3551 not ancestor of origin/master and != recorded base (598a522f92); worktree HEAD 25e41f3551 not in origin/master, != base, != safe remote tip |
| 02983cde | feat/card-task-02983cde | Canceled | CARD-0558 | LOOK | LOOK | 9 | 0 | 54/7 |  | remote tip f529bc7d51 not ancestor of origin/master and != recorded base (959e5aa8a5) |
| 05a66230 | feat/card-task-05a66230 | Succeeded | CARD-0462 | - | LOOK | 20 | 0 | 52/21 |  | worktree HEAD 048b6a180c not in origin/master, != base, != safe remote tip |
| 06805739 | feat/card-task-06805739 | Succeeded | CARD-0691 | LOOK | LOOK | 8 | 0 | 27/19 |  | remote tip 88e25816ae not ancestor of origin/master and != recorded base (e8874bfb56); worktree HEAD 88e25816ae not in origin/master, != base, != safe remote tip |
| 07cabe87 | feat/card-task-07cabe87 | Succeeded | CARD-0726 | - | LOOK | 7 | 0 | 16/5 |  | worktree HEAD 89a00a7bc4 not in origin/master, != base, != safe remote tip |
| 07df9f1d | feat/card-task-07df9f1d | Succeeded | CARD-0737 | LOOK | LOOK | 2 | 0 | 7/7 |  | remote tip a3396e08c4 not ancestor of origin/master and != recorded base (b22ef9fb17); worktree HEAD a3396e08c4 not in origin/master, != base, != safe remote tip |
| 0aa9717c | feat/card-task-0aa9717c | Failed | CARD-0452 | LOOK | LOOK | 15 | 0 | 12/3 |  | remote tip fcbb594d72 not ancestor of origin/master and != recorded base (26c1393a1e) |
| 0bebfaa7 | feat/card-task-0bebfaa7 | Failed | CARD-0593 | LOOK | LOOK | 35 | 0 | 28/11 |  | remote tip c29adc51b4 not ancestor of origin/master and != recorded base (0f2f355070) |
| 0cc1623f | feat/card-task-0cc1623f | Succeeded | CARD-0767 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip 0ac283301b not ancestor of origin/master and != recorded base (a56c3a7e78); worktree HEAD 0ac283301b not in origin/master, != base, != safe remote tip |
| 0f6a815b | feat/card-task-0f6a815b | Succeeded | CARD-0718 | LOOK | LOOK | 16 | 0 | 23/13 |  | remote tip 2fd6d7f6b1 not ancestor of origin/master and != recorded base (59a93e5156); worktree HEAD 2fd6d7f6b1 not in origin/master, != base, != safe remote tip |
| 10632f25 | feat/card-task-10632f25 | Succeeded | CARD-0657 | LOOK | - | 4 | 0 | 5/5 |  | remote tip f1195ea118 not ancestor of origin/master and != recorded base (a1c6c59ead) |
| 125f62dc | feat/card-task-125f62dc | Succeeded | CARD-0735 | - | LOOK | 10 | 0 | 9/6 |  | worktree HEAD 3df764bd37 not in origin/master, != base, != safe remote tip |
| 130b15ef | feat/card-task-130b15ef | Failed | CARD-0593 | LOOK | LOOK | 31 | 0 | 28/13 |  | remote tip dbb8937887 not ancestor of origin/master and != recorded base (e9061777a0) |
| 131508fb | feat/card-task-131508fb | Succeeded | CARD-0759 | - | LOOK | 12 | 0 | 27/10 |  | worktree HEAD 8289bdc747 not in origin/master, != base, != safe remote tip |
| 15a86ac9 | feat/card-task-15a86ac9 | Failed | CARD-0650 | LOOK | - | 4 | 0 | 7/7 |  | remote tip bc619b4a35 not ancestor of origin/master and != recorded base (822a0d160f) |
| 1668ddd6 | feat/card-task-1668ddd6 | Succeeded | CARD-0716 | LOOK | LOOK | 1 | 0 | 1/0 |  | remote tip 07df1561e7 not ancestor of origin/master and != recorded base (813ac55169); worktree HEAD 07df1561e7 not in origin/master, != base, != safe remote tip |
| 169fe4dd | feat/card-task-169fe4dd | Succeeded | CARD-0659 | LOOK | - | 3 | 0 | 8/8 |  | remote tip 04bb109d56 not ancestor of origin/master and != recorded base (926b335426) |
| 16d7e477 | feat/card-task-16d7e477 | Succeeded | CARD-0611 | LOOK | LOOK | 2 | 0 | 3/2 |  | remote tip 0a97e75eb2 not ancestor of origin/master and != recorded base (3962357414); worktree HEAD 0a97e75eb2 not in origin/master, != base, != safe remote tip |
| 1753ea5c | feat/card-task-1753ea5c | Succeeded | CARD-0664 | LOOK | - | 4 | 0 | 11/3 |  | remote tip 44552e7e29 not ancestor of origin/master and != recorded base (f8b914cb23) |
| 19c7ff68 | feat/card-task-19c7ff68 | Succeeded | CARD-0727 | LOOK | LOOK | 3 | 0 | 8/4 |  | remote tip 7526a1e197 not ancestor of origin/master and != recorded base (bafc336632); worktree HEAD 7526a1e197 not in origin/master, != base, != safe remote tip |
| 1aa86155 | feat/card-task-1aa86155 | Succeeded | CARD-0705 | LOOK | LOOK | 1 | 0 | 5/3 |  | remote tip 1d10961adf not ancestor of origin/master and != recorded base (e0be7f5e62); worktree HEAD 1d10961adf not in origin/master, != base, != safe remote tip |
| 1c52bd82 | feat/card-task-1c52bd82 | Succeeded | CARD-0767 | LOOK | LOOK | 2 | 0 | 1/0 |  | remote tip a8b48dbbea not ancestor of origin/master and != recorded base (0ac283301b); worktree HEAD a8b48dbbea not in origin/master, != base, != safe remote tip |
| 1ceae9ac | feat/card-task-1ceae9ac | Succeeded | CARD-0710 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip f6519816f8 not ancestor of origin/master and != recorded base (fbdc3c6e66); worktree HEAD f6519816f8 not in origin/master, != base, != safe remote tip |
| 1d5b779b | feat/card-task-1d5b779b | Succeeded | CARD-0759 | LOOK | LOOK | 2 | 0 | 1/1 |  | remote tip 97efafaab1 not ancestor of origin/master and != recorded base (7c7e685a08); worktree HEAD 97efafaab1 not in origin/master, != base, != safe remote tip |
| 1d94da9e | feat/card-task-1d94da9e | Canceled | CARD-0650 | LOOK | - | 22 | 0 | 16/5 |  | remote tip 14dbe1110a not ancestor of origin/master and != recorded base (ce4cce1e8c) |
| 1e77c1d1 | feat/card-task-1e77c1d1 | Succeeded | CARD-0666 | LOOK | - | 10 | 0 | 13/8 |  | remote tip d87edfc70d not ancestor of origin/master and != recorded base (e8bfdeb004) |
| 22b951cd | feat/card-task-22b951cd | Succeeded | CARD-0749 | LOOK | LOOK | 2 | 0 | 12/10 |  | remote tip 57781e5903 not ancestor of origin/master and != recorded base (f6d419732d); worktree HEAD 57781e5903 not in origin/master, != base, != safe remote tip |
| 22c54e3f | feat/card-task-22c54e3f | Succeeded | CARD-0735 | LOOK | LOOK | 4 | 0 | 1/1 | Refused | remote tip 993a664c43 not ancestor of origin/master and != recorded base (e4b4159d89); worktree HEAD 993a664c43 not in origin/master, != base, != safe remote tip |
| 23559109 | feat/card-task-23559109 | Canceled | CARD-0740 | - | LOOK | 2 | 0 | 4/2 |  | worktree HEAD 023f2ac228 not in origin/master, != base, != safe remote tip |
| 263541a1 | feat/card-task-263541a1 | Succeeded | CARD-0653 | LOOK | - | 2 | 0 | 29/28 |  | remote tip eb8344d337 not ancestor of origin/master and != recorded base (93ab36c434) |
| 2824ba4c | feat/card-task-2824ba4c | Succeeded | CARD-0650 | LOOK | - | 15 | 0 | 15/12 |  | remote tip 0c36c6816c not ancestor of origin/master and != recorded base (dd76c862aa) |
| 29c28d0c | feat/card-task-29c28d0c | Succeeded | CARD-0758 | LOOK | LOOK | 2 | 0 | 1/0 |  | remote tip a806ce19ab not ancestor of origin/master and != recorded base (b96ed07906); worktree HEAD a806ce19ab not in origin/master, != base, != safe remote tip |
| 2b6ade7e | feat/card-task-2b6ade7e | Canceled | CARD-0679 | LOOK | - | 4 | 0 | 15/11 |  | remote tip 7c89dcd8c3 not ancestor of origin/master and != recorded base (d511515702) |
| 2cc36aa6 | feat/card-task-2cc36aa6 | Succeeded | CARD-0735 | LOOK | LOOK | 9 | 0 | 9/6 |  | remote tip b76714d9c3 not ancestor of origin/master and != recorded base (993a664c43); worktree HEAD b76714d9c3 not in origin/master, != base, != safe remote tip |
| 2cd14d4c | feat/card-task-2cd14d4c | Failed | CARD-0666 | LOOK | - | 4 | 0 | 3/2 |  | remote tip 4eed6c9699 not ancestor of origin/master and != recorded base (57543b8556) |
| 2dd9fbf3 | feat/card-task-2dd9fbf3 | Succeeded | CARD-0457 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip eaee40f8fc not ancestor of origin/master and != recorded base (a56c3a7e78); worktree HEAD eaee40f8fc not in origin/master, != base, != safe remote tip |
| 2e841047 | feat/card-task-2e841047 | Canceled | CARD-0693 | LOOK | - | 2 | 0 | 3/3 |  | remote tip fa1897c39b not ancestor of origin/master and != recorded base (e91a78cf0d) |
| 3002779f | feat/card-task-3002779f | Succeeded | CARD-0442 | LOOK | LOOK | 22 | 0 | 42/13 |  | remote tip 480cd6f2f8 not ancestor of origin/master and != recorded base (27ae0bc2c3); worktree HEAD 480cd6f2f8 not in origin/master, != base, != safe remote tip |
| 30103883 | feat/card-task-30103883 | Failed | CARD-0650 | LOOK | - | 7 | 0 | 8/7 |  | remote tip 2c85ee0a64 not ancestor of origin/master and != recorded base (bc619b4a35) |
| 32c16f61 | feat/card-task-32c16f61 | Succeeded | CARD-0653 | LOOK | - | 6 | 0 | 38/34 |  | remote tip a578dd405e not ancestor of origin/master and != recorded base (b06427d285) |
| 33b9032e | feat/card-task-33b9032e | Succeeded | CARD-0691 | LOOK | LOOK | 3 | 0 | 17/13 |  | remote tip 8106b49e21 not ancestor of origin/master and != recorded base (65c29b7422); worktree HEAD 8106b49e21 not in origin/master, != base, != safe remote tip |
| 3416c8aa | feat/card-task-3416c8aa | Failed | CARD-0646 | LOOK | - | 1 | 0 | 6/4 |  | remote tip b7849c32cc not ancestor of origin/master and != recorded base (93ab36c434) |
| 359c1a1c | feat/card-task-359c1a1c | Succeeded | CARD-0735 | - | LOOK | 13 | 0 | 9/4 |  | worktree HEAD 4646a8ba6b not in origin/master, != base, != safe remote tip |
| 365d4862 | feat/card-task-365d4862 | Succeeded | CARD-0464 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip 00fcd926b6 not ancestor of origin/master and != recorded base (a56c3a7e78); worktree HEAD 00fcd926b6 not in origin/master, != base, != safe remote tip |
| 36b868ff | feat/card-task-36b868ff | Failed | CARD-0711 | - | LOOK | 9 | 0 | 22/9 |  | worktree HEAD 00e710d5e0 not in origin/master, != base, != safe remote tip |
| 37a70801 | feat/card-task-37a70801 | Failed | CARD-0599 | LOOK | - | 4 | 0 | 6/5 |  | remote tip 0e845e9875 not ancestor of origin/master and != recorded base (a95add1e9b) |
| 3816b58e | feat/card-task-3816b58e | Canceled | CARD-0681 | LOOK | - | 2 | 0 | 9/3 |  | remote tip 7629fe0dc0 not ancestor of origin/master and != recorded base (d511515702) |
| 387b2714 | feat/card-task-387b2714 | Succeeded | CARD-0718 | LOOK | LOOK | 5 | 0 | 23/4 |  | remote tip c66d59a186 not ancestor of origin/master and != recorded base (5c51f16d3e); worktree HEAD c66d59a186 not in origin/master, != base, != safe remote tip |
| 3ad57002 | feat/card-task-3ad57002 | Succeeded | CARD-0749 | LOOK | LOOK | 3 | 0 | 7/7 |  | remote tip 42daf278fc not ancestor of origin/master and != recorded base (55d4082f5c); worktree HEAD 42daf278fc not in origin/master, != base, != safe remote tip |
| 3c593d7c | feat/card-task-3c593d7c | Succeeded | CARD-0550 | LOOK | - | 1 | 0 | 1/1 |  | remote tip dde854b521 not ancestor of origin/master and != recorded base (77e080f3a6) |
| 3d4eb382 | feat/card-task-3d4eb382 | Succeeded | CARD-0735 | - | LOOK | 12 | 0 | 9/5 |  | worktree HEAD 6d986b061a not in origin/master, != base, != safe remote tip |
| 3e89af3c | feat/card-task-3e89af3c | Succeeded | CARD-0593 | LOOK | LOOK | 2 | 0 | 1/0 |  | remote tip dbce1aae36 not ancestor of origin/master and != recorded base (d68203aafc); worktree HEAD dbce1aae36 not in origin/master, != base, != safe remote tip |
| 3fefbc65 | feat/card-task-3fefbc65 | Canceled | CARD-0749 | - | LOOK | 2 | 0 | 12/10 |  | worktree HEAD 57781e5903 not in origin/master, != base, != safe remote tip |
| 47be9689 | feat/card-task-47be9689 | Succeeded | CARD-0557 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip 4eacef936b not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD 4eacef936b not in origin/master, != base, != safe remote tip |
| 48bbc16e | feat/card-task-48bbc16e | Succeeded | CARD-0664 | LOOK | - | 3 | 0 | 6/5 |  | remote tip 5e2beb6903 not ancestor of origin/master and != recorded base (ffa87d01b8) |
| 48d0495d | feat/card-task-48d0495d | Succeeded | CARD-0749 | LOOK | LOOK | 5 | 0 | 8/8 |  | remote tip 772a050a77 not ancestor of origin/master and != recorded base (42daf278fc); worktree HEAD 772a050a77 not in origin/master, != base, != safe remote tip |
| 49cc02b8 | feat/card-task-49cc02b8 | Succeeded | CARD-0557 | LOOK | LOOK | 2 | 0 | 1/0 |  | remote tip 9b4aa8384a not ancestor of origin/master and != recorded base (4eacef936b); worktree HEAD 9b4aa8384a not in origin/master, != base, != safe remote tip |
| 4aa8cc21 | feat/card-task-4aa8cc21 | Canceled | CARD-0664 | LOOK | - | 4 | 0 | 11/8 |  | remote tip b14e563828 not ancestor of origin/master and != recorded base (1e4554ab1e) |
| 4ce3b0ad | feat/card-task-4ce3b0ad | Succeeded | CARD-0653 | LOOK | - | 9 | 0 | 38/33 |  | remote tip f6207fbb3a not ancestor of origin/master and != recorded base (a578dd405e) |
| 50a2b232 | feat/card-task-50a2b232 | Succeeded | CARD-0768 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip b0a6d5bc3b not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD b0a6d5bc3b not in origin/master, != base, != safe remote tip |
| 51b599cb | feat/card-task-51b599cb | Succeeded | CARD-0452 | LOOK | LOOK | 23 | 0 | 19/3 |  | remote tip 60cb80df93 not ancestor of origin/master and != recorded base (fcbb594d72); worktree HEAD 60cb80df93 not in origin/master, != base, != safe remote tip |
| 520c70c6 | feat/card-task-520c70c6 | Succeeded | CARD-0718 | LOOK | LOOK | 6 | 0 | 24/4 |  | remote tip e343024ef9 not ancestor of origin/master and != recorded base (c66d59a186); worktree HEAD e343024ef9 not in origin/master, != base, != safe remote tip |
| 52e52800 | feat/card-task-52e52800 | Succeeded |  | LOOK | LOOK | 2 | 0 | 7/7 |  | remote tip 55d4082f5c not ancestor of origin/master and != recorded base (2404f17a2b); worktree HEAD 55d4082f5c not in origin/master, != base, != safe remote tip |
| 539915bf | feat/card-task-539915bf | Canceled | CARD-0691 | LOOK | LOOK | 8 | 0 | 20/12 |  | remote tip 7b2be1272d not ancestor of origin/master and != recorded base (8106b49e21) |
| 5704b46b | feat/card-task-5704b46b | Succeeded | CARD-0727 | - | LOOK | 11 | 0 | 33/16 |  | worktree HEAD 6b332e6f49 not in origin/master, != base, != safe remote tip |
| 57758b77 | feat/card-task-57758b77 | Succeeded | CARD-0714 | LOOK | LOOK | 1 | 0 | 1/0 |  | remote tip 38fb98975a not ancestor of origin/master and != recorded base (813ac55169); worktree HEAD 38fb98975a not in origin/master, != base, != safe remote tip |
| 58011e5e | feat/card-task-58011e5e | Succeeded | CARD-0696 | LOOK | LOOK | 7 | 0 | 26/13 |  | remote tip 0a0834693d not ancestor of origin/master and != recorded base (f801109b56); worktree HEAD 0a0834693d not in origin/master, != base, != safe remote tip |
| 5b2256d5 | feat/card-task-5b2256d5 | Succeeded | CARD-0559 | LOOK | LOOK | 1 | 0 | 1/0 |  | remote tip 670f1d6328 not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD 670f1d6328 not in origin/master, != base, != safe remote tip |
| 5ef74c0f | feat/card-task-5ef74c0f | Succeeded | CARD-0641 | LOOK | - | 3 | 0 | 8/6 |  | remote tip 449a28c840 not ancestor of origin/master and != recorded base (c8f4cdf9c4) |
| 64db976e | feat/card-task-64db976e | Succeeded | CARD-0735 | LOOK | LOOK | 10 | 0 | 9/6 |  | remote tip 3df764bd37 not ancestor of origin/master and != recorded base (b76714d9c3); worktree HEAD 3df764bd37 not in origin/master, != base, != safe remote tip |
| 65d9f56e | feat/card-task-65d9f56e | Failed | CARD-0566 | LOOK | LOOK | 10 | 0 | 9/3 |  | remote tip 01d3cbcbce not ancestor of origin/master and != recorded base (4efce814f0) |
| 67ddf317 | feat/card-task-67ddf317 | Succeeded | CARD-0735 | LOOK | LOOK | 12 | 0 | 9/5 |  | remote tip 6d986b061a not ancestor of origin/master and != recorded base (3df764bd37); worktree HEAD 6d986b061a not in origin/master, != base, != safe remote tip |
| 6801de9a | feat/card-task-6801de9a | Succeeded | CARD-0737 | LOOK | LOOK | 4 | 0 | 7/6 |  | remote tip ca10727295 not ancestor of origin/master and != recorded base (a3396e08c4); worktree HEAD ca10727295 not in origin/master, != base, != safe remote tip |
| 68a64314 | feat/card-task-68a64314 | Succeeded | CARD-0706 | LOOK | LOOK | 1 | 0 | 1/0 | Unconfirmed | remote tip 34600eb8e3 not ancestor of origin/master and != recorded base (b0a5829763); worktree HEAD 34600eb8e3 not in origin/master, != base, != safe remote tip |
| 69c7a060 | feat/card-task-69c7a060 | Succeeded | CARD-0767 | LOOK | LOOK | 48 | 0 | 46/5 |  | remote tip f5632b80be not ancestor of origin/master and != recorded base (654c3c09d0); worktree HEAD f5632b80be not in origin/master, != base, != safe remote tip |
| 6ca40cd0 | feat/card-task-6ca40cd0 | Canceled | CARD-0727 | LOOK | LOOK | 11 | 0 | 33/16 |  | remote tip 6b332e6f49 not ancestor of origin/master and != recorded base (7062c5cb10) |
| 6cfbb6f5 | feat/card-task-6cfbb6f5 | Succeeded | CARD-0464 | - | LOOK | 8 | 0 | 7/0 |  | worktree HEAD d2e733a653 not in origin/master, != base, != safe remote tip |
| 6eed34e6 | feat/card-task-6eed34e6 | Failed | CARD-0650 | LOOK | - | 12 | 0 | 15/2 |  | remote tip fcd657c2d9 not ancestor of origin/master and != recorded base (8035c0eae8) |
| 70269020 | feat/card-task-70269020 | Succeeded | CARD-0653 | LOOK | - | 4 | 0 | 31/29 |  | remote tip b06427d285 not ancestor of origin/master and != recorded base (eb8344d337) |
| 70302dc6 | feat/card-task-70302dc6 | Succeeded | CARD-0660 | LOOK | - | 6 | 0 | 12/8 |  | remote tip 212128028d not ancestor of origin/master and != recorded base (125f8dda23) |
| 71060b3f | feat/card-task-71060b3f | Succeeded | CARD-0738 | LOOK | LOOK | 11 | 0 | 23/12 |  | remote tip 66eacd80b0 not ancestor of origin/master and != recorded base (bafc336632); worktree HEAD 66eacd80b0 not in origin/master, != base, != safe remote tip |
| 74da5d1b | feat/card-task-74da5d1b | Succeeded | CARD-0753 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip 956380a535 not ancestor of origin/master and != recorded base (4e873cf20a); worktree HEAD 956380a535 not in origin/master, != base, != safe remote tip |
| 75491ae6 | feat/card-task-75491ae6 | Succeeded | CARD-0650 | LOOK | - | 6 | 0 | 21/11 |  | remote tip 6a231f038d not ancestor of origin/master and != recorded base (06b3db87b0) |
| 77629b10 | feat/card-task-77629b10 | Succeeded | CARD-0768 | LOOK | LOOK | 2 | 0 | 1/0 |  | remote tip 1a97191167 not ancestor of origin/master and != recorded base (b0a6d5bc3b); worktree HEAD 1a97191167 not in origin/master, != base, != safe remote tip |
| 778fa5d5 | feat/card-task-778fa5d5 | Succeeded | CARD-0730 | LOOK | LOOK | 3 | 0 | 4/2 |  | remote tip 3eda4e57e8 not ancestor of origin/master and != recorded base (1cebe72805); worktree HEAD 3eda4e57e8 not in origin/master, != base, != safe remote tip |
| 78c5f868 | feat/card-task-78c5f868 | Succeeded | CARD-0717 | LOOK | LOOK | 1 | 0 | 2/1 | Unconfirmed | remote tip 5a8c688bd8 not ancestor of origin/master and != recorded base (813ac55169); worktree HEAD 314ec59f20 not in origin/master, != base, != safe remote tip |
| 7dc235bc | feat/card-task-7dc235bc | Succeeded | CARD-0647 | LOOK | - | 6 | 0 | 16/12 |  | remote tip 9c5702bdbc not ancestor of origin/master and != recorded base (dd83ae9220) |
| 80103e29 | feat/card-task-80103e29 | Succeeded | CARD-0726 | - | LOOK | 6 | 0 | 14/10 |  | worktree HEAD 9c1c689803 not in origin/master, != base, != safe remote tip |
| 81c898bc | feat/card-task-81c898bc | Succeeded | CARD-0698 | LOOK | LOOK | 7 | 0 | 17/12 |  | remote tip 8962057b58 not ancestor of origin/master and != recorded base (d2236e9ec3); worktree HEAD 8962057b58 not in origin/master, != base, != safe remote tip |
| 86d683e9 | feat/card-task-86d683e9 | Succeeded | CARD-0452 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip 29924ec485 not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD 29924ec485 not in origin/master, != base, != safe remote tip |
| 86e57e3d | feat/card-task-86e57e3d | Succeeded | CARD-0452 | LOOK | LOOK | 2 | 0 | 1/1 |  | remote tip 26c1393a1e not ancestor of origin/master and != recorded base (29924ec485); worktree HEAD 26c1393a1e not in origin/master, != base, != safe remote tip |
| 8b51f4e4 | feat/card-task-8b51f4e4 | Canceled | CARD-0723 | LOOK | LOOK | 10 | 0 | 81/34 |  | remote tip 6da15ef9c8 not ancestor of origin/master and != recorded base (49872e9a17) |
| 8d32f2c5 | feat/card-task-8d32f2c5 | Succeeded | CARD-0718 | LOOK | LOOK | 11 | 0 | 42/13 |  | remote tip 315df03964 not ancestor of origin/master and != recorded base (8805577f19); worktree HEAD 315df03964 not in origin/master, != base, != safe remote tip |
| 8d4cd03e | feat/card-task-8d4cd03e | Canceled | CARD-0679 | LOOK | - | 8 | 0 | 18/12 |  | remote tip 0cc8db569b not ancestor of origin/master and != recorded base (7c89dcd8c3) |
| 8f5d5036 | feat/card-task-8f5d5036 | Succeeded | CARD-0698 | LOOK | LOOK | 11 | 0 | 17/8 |  | remote tip 1ec489f3dc not ancestor of origin/master and != recorded base (8962057b58); worktree HEAD 1ec489f3dc not in origin/master, != base, != safe remote tip |
| 907b5d3e | feat/card-task-907b5d3e | Succeeded | CARD-0759 | - | LOOK | 19 | 0 | 29/6 |  | worktree HEAD 0eb4cccac9 not in origin/master, != base, != safe remote tip |
| 92783ab1 | feat/card-task-92783ab1 | Succeeded | CARD-0599 | LOOK | - | 7 | 0 | 6/5 |  | remote tip 3c26019989 not ancestor of origin/master and != recorded base (0e845e9875) |
| 92b9022f | feat/card-task-92b9022f | Succeeded | CARD-0711 | - | LOOK | 21 | 0 | 24/10 |  | worktree HEAD c206cd7fc4 not in origin/master, != base, != safe remote tip |
| 94d4b1de | feat/card-task-94d4b1de | Succeeded | CARD-0566 | LOOK | LOOK | 2 | 0 | 1/1 |  | remote tip 4efce814f0 not ancestor of origin/master and != recorded base (2c363d9cf9); worktree HEAD 4efce814f0 not in origin/master, != base, != safe remote tip |
| 993405ad | feat/card-task-993405ad | Failed | CARD-0640 | LOOK | - | 1 | 0 | 4/4 |  | remote tip b8aeaa51eb not ancestor of origin/master and != recorded base (25c093944d) |
| 99efb48f | feat/card-task-99efb48f | Succeeded | CARD-0759 | LOOK | LOOK | 7 | 0 | 26/12 |  | remote tip 9b646f4e69 not ancestor of origin/master and != recorded base (97efafaab1); worktree HEAD 9b646f4e69 not in origin/master, != base, != safe remote tip |
| 9b83888e | feat/card-task-9b83888e | Canceled | CARD-0684 | LOOK | - | 1 | 0 | 1/1 |  | remote tip bb4db96a98 not ancestor of origin/master and != recorded base (e6c94f8946) |
| 9e28086d | feat/card-task-9e28086d | Failed | CARD-0658 | LOOK | - | 3 | 0 | 20/11 |  | remote tip e91e3ac847 not ancestor of origin/master and != recorded base (c06a6ae5b0) |
| 9e8d8472 | feat/card-task-9e8d8472 | Succeeded | CARD-0716 | LOOK | LOOK | 13 | 0 | 29/14 |  | remote tip 07b852113c not ancestor of origin/master and != recorded base (47c5f72462); worktree HEAD 07b852113c not in origin/master, != base, != safe remote tip |
| a103fe6f | feat/card-task-a103fe6f | Succeeded | CARD-0651 | LOOK | LOOK | 2 | 0 | 1/1 |  | remote tip ae6956e72c not ancestor of origin/master and != recorded base (53e0bebfba); worktree HEAD ae6956e72c not in origin/master, != base, != safe remote tip |
| a36875a7 | feat/card-task-a36875a7 | Succeeded | CARD-0650 | LOOK | - | 11 | 0 | 8/7 |  | remote tip dd76c862aa not ancestor of origin/master and != recorded base (2c85ee0a64) |
| a61c829b | feat/card-task-a61c829b | Succeeded | CARD-0679 | LOOK | - | 3 | 0 | 5/4 |  | remote tip da7481d4dc not ancestor of origin/master and != recorded base (e91a78cf0d) |
| a7ab0cda | feat/card-task-a7ab0cda | Failed | CARD-0650 | LOOK | - | 5 | 0 | 9/5 |  | remote tip a0a5b7b624 not ancestor of origin/master and != recorded base (c8f4cdf9c4) |
| a7d678ce | feat/card-task-a7d678ce | Succeeded | CARD-0679 | LOOK | - | 8 | 0 | 11/9 |  | remote tip 68b3977375 not ancestor of origin/master and != recorded base (da7481d4dc) |
| a8f042bb | feat/card-task-a8f042bb | Succeeded | CARD-0558 | LOOK | - | 2 | 0 | 2/1 |  | remote tip 2f7cb8497a not ancestor of origin/master and != recorded base (3a62074ebf) |
| aadd05ab | feat/card-task-aadd05ab | Succeeded | CARD-0660 | LOOK | - | 5 | 0 | 15/10 |  | remote tip 2ee0387ae3 not ancestor of origin/master and != recorded base (125f8dda23) |
| ac059bce | feat/card-task-ac059bce | Failed | CARD-0740 | LOOK | LOOK | 2 | 0 | 4/2 |  | remote tip 023f2ac228 not ancestor of origin/master and != recorded base (30b8f7a52c) |
| b0e67a1d | feat/card-task-b0e67a1d | Succeeded | CARD-0566 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip 2c363d9cf9 not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD 2c363d9cf9 not in origin/master, != base, != safe remote tip |
| b560c49a | feat/card-task-b560c49a | Succeeded | CARD-0699 | LOOK | LOOK | 5 | 0 | 16/7 |  | remote tip eba6afa327 not ancestor of origin/master and != recorded base (fbdc3c6e66); worktree HEAD eba6afa327 not in origin/master, != base, != safe remote tip |
| b5e6521b | feat/card-task-b5e6521b | Succeeded | CARD-0708 | LOOK | LOOK | 2 | 0 | 4/0 |  | remote tip aacf6ca5e2 not ancestor of origin/master and != recorded base (fbdc3c6e66); worktree HEAD aacf6ca5e2 not in origin/master, != base, != safe remote tip |
| bb813687 | feat/card-task-bb813687 | Succeeded | CARD-0721 | LOOK | LOOK | 2 | 0 | 4/1 |  | remote tip 7013379252 not ancestor of origin/master and != recorded base (8a79fbef31); worktree HEAD 7013379252 not in origin/master, != base, != safe remote tip |
| bca19885 | feat/card-task-bca19885 | Succeeded | CARD-0657 | LOOK | - | 3 | 0 | 5/5 |  | remote tip a1c6c59ead not ancestor of origin/master and != recorded base (799776677b) |
| c01a66ce | feat/card-task-c01a66ce | Failed | CARD-0599 | LOOK | - | 11 | 0 | 7/0 |  | remote tip b8859c8428 not ancestor of origin/master and != recorded base (38ddf39505) |
| c087dcaf | feat/card-task-c087dcaf | Succeeded | CARD-0657 | LOOK | - | 10 | 0 | 13/11 |  | remote tip 7dbfaefcf7 not ancestor of origin/master and != recorded base (a1c6c59ead) |
| c0a787fe | feat/card-task-c0a787fe | Succeeded | CARD-0464 | LOOK | LOOK | 2 | 0 | 1/1 |  | remote tip 897b3cc4b9 not ancestor of origin/master and != recorded base (00fcd926b6); worktree HEAD 897b3cc4b9 not in origin/master, != base, != safe remote tip |
| c107ce5c | feat/card-task-c107ce5c | Succeeded | CARD-0504 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip 10fe51b0f6 not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD 10fe51b0f6 not in origin/master, != base, != safe remote tip |
| c4154b22 | feat/card-task-c4154b22 | Succeeded | CARD-0593 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip d68203aafc not ancestor of origin/master and != recorded base (4e873cf20a); worktree HEAD d68203aafc not in origin/master, != base, != safe remote tip |
| c4b35a78 | feat/card-task-c4b35a78 | Succeeded | CARD-0758 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip b96ed07906 not ancestor of origin/master and != recorded base (2404f17a2b); worktree HEAD b96ed07906 not in origin/master, != base, != safe remote tip |
| c5885cc9 | feat/card-task-c5885cc9 | Canceled | CARD-0651 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip 53e0bebfba not ancestor of origin/master and != recorded base (3962357414) |
| c99b5b44 | feat/card-task-c99b5b44 | Succeeded | CARD-0696 | LOOK | LOOK | 10 | 0 | 27/12 |  | remote tip 8c1b73d1a6 not ancestor of origin/master and != recorded base (0a0834693d); worktree HEAD 8c1b73d1a6 not in origin/master, != base, != safe remote tip |
| c99e35fa | feat/card-task-c99e35fa | Failed | CARD-0442 | LOOK | LOOK | 2 | 0 | 27/12 |  | remote tip 27ae0bc2c3 not ancestor of origin/master and != recorded base (a0e73c976a) |
| cb96c3a4 | feat/card-task-cb96c3a4 | Succeeded | CARD-0691 | LOOK | LOOK | 11 | 0 | 28/16 |  | remote tip 6a75ab72d7 not ancestor of origin/master and != recorded base (88e25816ae); worktree HEAD 6a75ab72d7 not in origin/master, != base, != safe remote tip |
| ce55c8ef | feat/card-task-ce55c8ef | Canceled | CARD-0740 | LOOK | LOOK | 2 | 0 | 4/2 |  | remote tip 9b7ef95746 not ancestor of origin/master and != recorded base (023f2ac228); worktree HEAD 9b7ef95746 not in origin/master, != base, != safe remote tip |
| d24e1b4d | feat/card-task-d24e1b4d | Canceled | CARD-0650 | LOOK | - | 19 | 0 | 16/9 |  | remote tip ce4cce1e8c not ancestor of origin/master and != recorded base (0c36c6816c) |
| d4689430 | feat/card-task-d4689430 | Succeeded | CARD-0740 | LOOK | LOOK | 2 | 0 | 4/2 |  | remote tip 023f2ac228 not ancestor of origin/master and != recorded base (30b8f7a52c); worktree HEAD 023f2ac228 not in origin/master, != base, != safe remote tip |
| d6481d37 | feat/card-task-d6481d37 | Canceled | CARD-0711 | - | LOOK | 21 | 0 | 24/10 |  | worktree HEAD c206cd7fc4 not in origin/master, != base, != safe remote tip |
| d75b52ff | feat/card-task-d75b52ff | Succeeded | CARD-0759 | LOOK | LOOK | 19 | 0 | 29/6 |  | remote tip 0eb4cccac9 not ancestor of origin/master and != recorded base (8289bdc747); worktree HEAD 0eb4cccac9 not in origin/master, != base, != safe remote tip |
| d76ef522 | feat/card-task-d76ef522 | Canceled | CARD-0651 | LOOK | LOOK | 3 | 0 | 1/1 |  | remote tip 2b963bb777 not ancestor of origin/master and != recorded base (ae6956e72c) |
| d806b77b | feat/card-task-d806b77b | Canceled | CARD-0664 | LOOK | - | 6 | 0 | 6/5 |  | remote tip 2d1c9af841 not ancestor of origin/master and != recorded base (5e2beb6903) |
| d81207e7 | feat/card-task-d81207e7 | Failed | CARD-0661 | LOOK | - | 4 | 0 | 8/8 |  | remote tip 137b683125 not ancestor of origin/master and != recorded base (7a1021209d) |
| d9f039a1 | feat/card-task-d9f039a1 | Succeeded | CARD-0716 | LOOK | LOOK | 19 | 0 | 30/9 |  | remote tip 477253a95a not ancestor of origin/master and != recorded base (07b852113c); worktree HEAD 477253a95a not in origin/master, != base, != safe remote tip |
| dafa05c9 | feat/card-task-dafa05c9 | Succeeded | CARD-0504 | LOOK | LOOK | 2 | 0 | 1/0 |  | remote tip ddf0c51a70 not ancestor of origin/master and != recorded base (10fe51b0f6); worktree HEAD ddf0c51a70 not in origin/master, != base, != safe remote tip |
| db55819f | feat/card-task-db55819f | Succeeded | CARD-0727 | LOOK | LOOK | 18 | 0 | 32/2 |  | remote tip cdf994beb3 not ancestor of origin/master and != recorded base (c84604053a); worktree HEAD cdf994beb3 not in origin/master, != base, != safe remote tip |
| db7a34db | feat/card-task-db7a34db | Failed | CARD-0659 | LOOK | - | 7 | 0 | 9/9 |  | remote tip e51c7ff56b not ancestor of origin/master and != recorded base (04bb109d56) |
| dbdfa428 | feat/card-task-dbdfa428 | Succeeded | CARD-0655 | LOOK | - | 1 | 0 | 4/4 |  | remote tip 7cfc72ce36 not ancestor of origin/master and != recorded base (024e9f87fc) |
| dca8c233 | feat/card-task-dca8c233 | Failed | CARD-0661 | LOOK | - | 1 | 0 | 6/6 |  | remote tip 7a1021209d not ancestor of origin/master and != recorded base (42eecdea2c) |
| e09045ba | feat/card-task-e09045ba | Succeeded | CARD-0767 | LOOK | LOOK | 42 | 0 | 46/5 |  | remote tip 654c3c09d0 not ancestor of origin/master and != recorded base (a8b48dbbea); worktree HEAD 654c3c09d0 not in origin/master, != base, != safe remote tip |
| e296379b | feat/card-task-e296379b | Succeeded | CARD-0642 | LOOK | - | 8 | 0 | 13/12 |  | remote tip b979a2cc62 not ancestor of origin/master and != recorded base (c8cd3d47b8) |
| e8eae8df | feat/card-task-e8eae8df | Succeeded | CARD-0718 | - | LOOK | 11 | 0 | 42/13 |  | worktree HEAD 315df03964 not in origin/master, != base, != safe remote tip |
| e9516943 | feat/card-task-e9516943 | Failed | CARD-0697 | LOOK | LOOK | 5 | 0 | 5/2 |  | remote tip b6879b1512 not ancestor of origin/master and != recorded base (fbdc3c6e66) |
| ead491e1 | feat/card-task-ead491e1 | Succeeded | CARD-0726 | LOOK | LOOK | 6 | 0 | 14/10 |  | remote tip 9c1c689803 not ancestor of origin/master and != recorded base (4e873cf20a); worktree HEAD 9c1c689803 not in origin/master, != base, != safe remote tip |
| ed1112b2 | feat/card-task-ed1112b2 | Succeeded | CARD-0679 | LOOK | - | 4 | 0 | 11/11 |  | remote tip 7ad846cba2 not ancestor of origin/master and != recorded base (e91a78cf0d) |
| ee823591 | feat/card-task-ee823591 | Succeeded | CARD-0718 | LOOK | LOOK | 4 | 0 | 22/6 |  | remote tip 5c51f16d3e not ancestor of origin/master and != recorded base (0f2f355070); worktree HEAD 5c51f16d3e not in origin/master, != base, != safe remote tip |
| eec32965 | feat/card-task-eec32965 | Succeeded | CARD-0642 | LOOK | - | 4 | 0 | 12/11 |  | remote tip c8cd3d47b8 not ancestor of origin/master and != recorded base (ffa87d01b8) |
| efb4ff41 | feat/card-task-efb4ff41 | Succeeded | CARD-0772 | LOOK | LOOK | 2 | 0 | 1/0 |  | remote tip ed11e83ffd not ancestor of origin/master and != recorded base (00b5e151b5); worktree HEAD ed11e83ffd not in origin/master, != base, != safe remote tip |
| efebc413 | feat/card-task-efebc413 | Succeeded | CARD-0730 | LOOK | LOOK | 1 | 0 | 1/0 |  | remote tip 1cebe72805 not ancestor of origin/master and != recorded base (e4b4159d89); worktree HEAD 1cebe72805 not in origin/master, != base, != safe remote tip |
| eff1371c | feat/card-task-eff1371c | Succeeded | CARD-0650 | LOOK | - | 4 | 0 | 14/4 |  | remote tip 06b3db87b0 not ancestor of origin/master and != recorded base (93ab36c434) |
| f012a1d3 | feat/card-task-f012a1d3 | Succeeded | CARD-0558 | LOOK | - | 3 | 0 | 2/1 |  | remote tip 62ab4bdf74 not ancestor of origin/master and != recorded base (3a62074ebf) |
| f0a7c7cd | feat/card-task-f0a7c7cd | Succeeded | CARD-0759 | LOOK | LOOK | 12 | 0 | 27/10 |  | remote tip 8289bdc747 not ancestor of origin/master and != recorded base (9b646f4e69); worktree HEAD 8289bdc747 not in origin/master, != base, != safe remote tip |
| f67e6efa | feat/card-task-f67e6efa | Succeeded | CARD-0647 | LOOK | - | 7 | 0 | 16/9 |  | remote tip 0ba3c55a4b not ancestor of origin/master and != recorded base (9c5702bdbc) |
| f727964e | feat/card-task-f727964e | Succeeded | CARD-0666 | LOOK | - | 7 | 0 | 10/5 |  | remote tip e8bfdeb004 not ancestor of origin/master and != recorded base (4eed6c9699) |
| f7833ef2 | feat/card-task-f7833ef2 | Canceled | CARD-0660 | LOOK | - | 10 | 0 | 12/6 |  | remote tip 2744fc835d not ancestor of origin/master and != recorded base (212128028d) |
| fa3a1135 | feat/card-task-fa3a1135 | Failed | CARD-0599 | LOOK | - | 2 | 0 | 5/4 |  | remote tip a95add1e9b not ancestor of origin/master and != recorded base (c8f4cdf9c4) |
| fa8d1265 | feat/card-task-fa8d1265 | Succeeded | CARD-0759 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip 7c7e685a08 not ancestor of origin/master and != recorded base (8805577f19); worktree HEAD 7c7e685a08 not in origin/master, != base, != safe remote tip |
| fc89bde9 | feat/card-task-fc89bde9 | Succeeded | CARD-0711 | LOOK | LOOK | 5 | 0 | 2/1 | Unconfirmed | remote tip 989abc9f58 not ancestor of origin/master and != recorded base (fa86dc6420); worktree HEAD 989abc9f58 not in origin/master, != base, != safe remote tip |
| fcb1344d | feat/card-task-fcb1344d | Succeeded |  | LOOK | LOOK | 1 | 0 | 11/10 |  | remote tip f6d419732d not ancestor of origin/master and != recorded base (b6a262e2ae); worktree HEAD f6d419732d not in origin/master, != base, != safe remote tip |
| fcb2e819 | feat/card-task-fcb2e819 | Succeeded | CARD-0772 | LOOK | LOOK | 1 | 0 | 1/1 |  | remote tip 00b5e151b5 not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD 00b5e151b5 not in origin/master, != base, != safe remote tip |
| fd089677 | feat/card-task-fd089677 | Succeeded | CARD-0599 | LOOK | - | 10 | 0 | 7/1 |  | remote tip 38ddf39505 not ancestor of origin/master and != recorded base (3c26019989) |

### F. Real commits not in master (possible unlanded / abandoned work) (227)

| shortId | branch | status | card | remote | worktree | ahead | cherry+ | files touched/differ now | land | why |
|---|---|---|---|---|---|---|---|---|---|---|
| 0100d53d | feat/card-task-0100d53d | Succeeded | CARD-0118 | LOOK | - | 1 | 1 | 1/1 |  | remote tip b564297782 not ancestor of origin/master and != recorded base (none) |
| 022820cb | feat/card-task-022820cb | Succeeded | CARD-0079 | LOOK | LOOK | 22 | 6 | 87/48 | Unconfirmed | remote tip 712fb59a2a not ancestor of origin/master and != recorded base (d0ab05f74d); worktree HEAD f58aa5bf10 not in origin/master, != base, != safe remote tip; land request state NeedsResolution (unresolved) |
| 0505ce7d | feat/card-task-0505ce7d | Failed | CARD-0478 | LOOK | - | 5 | 1 | 34/29 |  | remote tip 08d2d11176 not ancestor of origin/master and != recorded base (11b6c7ff0a) |
| 050cb713 | feat/card-task-050cb713 | Succeeded | CARD-0497 | LOOK | - | 7 | 1 | 38/17 |  | remote tip 21e9abed31 not ancestor of origin/master and != recorded base (2a19fb40ba) |
| 058eda4e | feat/card-task-058eda4e | Canceled | CARD-0558 | LOOK | LOOK | 8 | 1 | 54/7 |  | remote tip ee5e5b6893 not ancestor of origin/master and != recorded base (5600dcae30) |
| 062bfba3 | feat/card-task-062bfba3 | Succeeded | CARD-0590 | LOOK | - | 8 | 1 | 51/26 |  | remote tip 234bca2835 not ancestor of origin/master and != recorded base (723ac9534f) |
| 0998ad05 | feat/card-task-0998ad05 | Failed | CARD-0408 | LOOK | - | 10 | 2 | 105/46 |  | remote tip 67f056a820 not ancestor of origin/master and != recorded base (none) |
| 09f3dc47 | feat/card-task-09f3dc47 | Succeeded | CARD-0718 | - | LOOK | 20 | 1 | 42/22 |  | worktree HEAD 733523ef0f not in origin/master, != base, != safe remote tip |
| 0a79bc28 | feat/card-task-0a79bc28 | Succeeded | CARD-0443 | - | LOOK | 30 | 4 | 56/28 |  | worktree HEAD 8cc17f5187 not in origin/master, != base, != safe remote tip |
| 0d48a707 | feat/card-task-0d48a707 | Succeeded | CARD-0527 | LOOK | - | 21 | 1 | 63/41 |  | remote tip 29708a88ff not ancestor of origin/master and != recorded base (b97abfd838) |
| 0d678e51 | feat/card-task-0d678e51 | Succeeded | CARD-0575 | LOOK | - | 3 | 3 | 1/1 |  | remote tip 17c414fc84 not ancestor of origin/master and != recorded base (bb7b75353f) |
| 0de0cc0c | feat/card-task-0de0cc0c | Succeeded | CARD-0628 | LOOK | - | 17 | 3 | 49/40 |  | remote tip 3f73603a56 not ancestor of origin/master and != recorded base (f9b3d11a7d) |
| 0e3d0465 | feat/card-task-0e3d0465 | Succeeded | CARD-0459 | - | LOOK | 7 | 2 | 60/44 |  | worktree HEAD 1d2a14db4d not in origin/master, != base, != safe remote tip |
| 0eb35cd2 | feat/card-task-0eb35cd2 | Succeeded | CARD-0585 | LOOK | - | 1 | 1 | 1/1 |  | remote tip 5378b63407 not ancestor of origin/master and != recorded base (3c7a4057bc) |
| 1300c9b8 | feat/card-task-1300c9b8 | Canceled | CARD-0665 | LOOK | LOOK | 20 | 2 | 36/20 |  | remote tip c83c647670 not ancestor of origin/master and != recorded base (1625dea2d9) |
| 1302a18c | feat/card-task-1302a18c | Succeeded | CARD-0688 | LOOK | LOOK | 14 | 1 | 60/29 |  | remote tip 45830f382e not ancestor of origin/master and != recorded base (2d90f22a78); worktree HEAD 45830f382e not in origin/master, != base, != safe remote tip |
| 151b4e13 | feat/card-task-151b4e13 | Succeeded | CARD-0527 | LOOK | - | 36 | 1 | 74/37 |  | remote tip 1ef6296782 not ancestor of origin/master and != recorded base (b15d1dfc17) |
| 157b7f62 | feat/card-task-157b7f62 | Succeeded | CARD-0146 | LOOK | - | 1 | 1 | 12/12 |  | remote tip ab19c051eb not ancestor of origin/master and != recorded base (88c79d3992) |
| 17c504bb | feat/card-task-17c504bb | Succeeded | CARD-0210 | LOOK | - | 1 | 1 | 1/1 |  | remote tip 8552dcbb80 not ancestor of origin/master and != recorded base (none) |
| 1857c5d9 | feat/card-task-1857c5d9 | Succeeded | CARD-0005 | LOOK | - | 1 | 1 | 3/2 |  | remote tip a0cd6ed9e5 not ancestor of origin/master and != recorded base (none) |
| 1a5f192c | feat/card-task-1a5f192c | Succeeded | CARD-0490 | - | LOOK | 20 | 1 | 88/59 |  | worktree HEAD da25108bbd not in origin/master, != base, != safe remote tip |
| 1ac18f06 | feat/card-task-1ac18f06 | Succeeded | CARD-0443 | - | LOOK | 21 | 4 | 54/32 |  | worktree HEAD c2e3f97181 not in origin/master, != base, != safe remote tip |
| 1bcc1479 | feat/card-task-1bcc1479 | Failed | CARD-0590 | LOOK | LOOK | 12 | 1 | 54/27 |  | remote tip 2c80c976dc not ancestor of origin/master and != recorded base (f0972c42a6); worktree HEAD 7359932694 not in origin/master, != base, != safe remote tip |
| 1d0539bc | feat/card-task-1d0539bc | Succeeded | CARD-0527 | - | LOOK | 36 | 1 | 74/37 |  | worktree HEAD 1ef6296782 not in origin/master, != base, != safe remote tip |
| 1d339bcd | feat/card-task-1d339bcd | Failed | CARD-0494 | LOOK | LOOK | 14 | 14 | 6/6 |  | remote tip d89594cdf1 not ancestor of origin/master and != recorded base (93c4273615) |
| 1ef5f77e | feat/card-task-1ef5f77e | Succeeded | CARD-0494 | LOOK | LOOK | 21 | 21 | 17/17 |  | remote tip 4260428d9b not ancestor of origin/master and != recorded base (0c8d1cf521); worktree HEAD 4260428d9b not in origin/master, != base, != safe remote tip |
| 1f1c67b6 | feat/card-task-1f1c67b6 | Succeeded | CARD-0415 | LOOK | - | 10 | 3 | 14/10 |  | remote tip 23502557ca not ancestor of origin/master and != recorded base (d9d1f90ee4) |
| 1f39ac17 | feat/card-task-1f39ac17 | Succeeded | CARD-0573 | LOOK | LOOK | 1 | 1 | 1/1 |  | remote tip 0baac55837 not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD 0baac55837 not in origin/master, != base, != safe remote tip |
| 1fff5c74 | feat/card-task-1fff5c74 | Succeeded | CARD-0665 | LOOK | - | 8 | 3 | 25/16 |  | remote tip 5f4161c5ba not ancestor of origin/master and != recorded base (d4dfde724a) |
| 2031f04a | feat/card-task-2031f04a | Succeeded | CARD-0478 | - | LOOK | 33 | 4 | 109/85 |  | worktree HEAD 3b021db9dd not in origin/master, != base, != safe remote tip |
| 2198db1d | feat/card-task-2198db1d | Succeeded | CARD-0511 | LOOK | - | 1 | 1 | 1/1 |  | remote tip 8c312c48eb not ancestor of origin/master and != recorded base (3a62074ebf) |
| 261a4a51 | feat/card-task-261a4a51 | Succeeded | CARD-0628 | LOOK | - | 19 | 4 | 53/43 |  | remote tip fbcf25601c not ancestor of origin/master and != recorded base (3f73603a56) |
| 27871064 | feat/card-task-27871064 | Succeeded | CARD-0478 | - | LOOK | 19 | 4 | 100/83 |  | worktree HEAD c604692a74 not in origin/master, != base, != safe remote tip |
| 27d48d06 | feat/card-task-27d48d06 | Failed | CARD-0665 | LOOK | - | 6 | 3 | 23/19 |  | remote tip cc92f47640 not ancestor of origin/master and != recorded base (7b1abc8dcf) |
| 27dd8efb | feat/card-task-27dd8efb | Canceled | CARD-0657 | LOOK | - | 25 | 1 | 19/8 | Unconfirmed | remote tip 64b350d17d not ancestor of origin/master and != recorded base (11ea499a25) |
| 2853f966 | feat/card-task-2853f966 | Succeeded | CARD-0527 | LOOK | - | 21 | 1 | 63/41 |  | remote tip 29708a88ff not ancestor of origin/master and != recorded base (b97abfd838) |
| 28dcbf6d | feat/card-task-28dcbf6d | Succeeded | CARD-0672 | LOOK | LOOK | 10 | 1 | 28/17 |  | remote tip 0497e0da55 not ancestor of origin/master and != recorded base (9eddea8ae0); worktree HEAD 0497e0da55 not in origin/master, != base, != safe remote tip |
| 298f4c9f | feat/card-task-298f4c9f | Succeeded | CARD-0491 | LOOK | LOOK | 1 | 1 | 1/1 |  | remote tip a3c4d26b46 not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD a3c4d26b46 not in origin/master, != base, != safe remote tip |
| 29bb901f | feat/card-task-29bb901f | Failed | CARD-0459 | LOOK | LOOK | 22 | 2 | 68/42 |  | remote tip 8b2f61e1a2 not ancestor of origin/master and != recorded base (3c7a4057bc); worktree HEAD 1a163836a5 not in origin/master, != base, != safe remote tip |
| 2b553928 | feat/card-task-2b553928 | Succeeded | CARD-0716 | LOOK | LOOK | 11 | 1 | 18/6 |  | remote tip e4af52c711 not ancestor of origin/master and != recorded base (48ea8fa516); worktree HEAD e4af52c711 not in origin/master, != base, != safe remote tip |
| 2f178455 | feat/card-task-2f178455 | Succeeded | CARD-0454 | LOOK | LOOK | 1 | 1 | 1/1 |  | remote tip ce3b4cd1e7 not ancestor of origin/master and != recorded base (2404f17a2b); worktree HEAD ce3b4cd1e7 not in origin/master, != base, != safe remote tip |
| 318daeb7 | feat/card-task-318daeb7 | Succeeded | CARD-0575 | LOOK | LOOK | 3 | 3 | 1/1 |  | remote tip 17c414fc84 not ancestor of origin/master and != recorded base (bb7b75353f); worktree HEAD c391749673 not in origin/master, != base, != safe remote tip |
| 31ea7c27 | feat/card-task-31ea7c27 | Succeeded | CARD-0416 | LOOK | LOOK | 1 | 1 | 1/1 |  | remote tip 63e4f4dedf not ancestor of origin/master and != recorded base (a56c3a7e78); worktree HEAD 63e4f4dedf not in origin/master, != base, != safe remote tip |
| 32c1f938 | feat/card-task-32c1f938 | Succeeded | CARD-0527 | - | LOOK | 21 | 1 | 63/41 |  | worktree HEAD 29708a88ff not in origin/master, != base, != safe remote tip |
| 341c1bad | feat/card-task-341c1bad | Succeeded | CARD-0644 | LOOK | - | 7 | 5 | 20/20 |  | remote tip 5583f9cd98 not ancestor of origin/master and != recorded base (9cb69aed8d) |
| 34794d32 | feat/card-task-34794d32 | Succeeded | CARD-0657 | LOOK | - | 12 | 1 | 14/12 |  | remote tip 240907422f not ancestor of origin/master and != recorded base (7dbfaefcf7) |
| 360e223e | feat/card-task-360e223e | Succeeded | CARD-0527 | LOOK | - | 18 | 1 | 59/41 |  | remote tip 4d2a955080 not ancestor of origin/master and != recorded base (b97abfd838) |
| 3678a831 | feat/card-task-3678a831 | Succeeded | CARD-0701 | LOOK | LOOK | 9 | 2 | 31/12 |  | remote tip ffed154faa not ancestor of origin/master and != recorded base (4afedf12ee); worktree HEAD ffed154faa not in origin/master, != base, != safe remote tip |
| 3775565a | feat/card-task-3775565a | Failed | CARD-0418 | LOOK | LOOK | 20 | 20 | 94/94 |  | remote tip c3e2f51bb1 not ancestor of origin/master and != recorded base (fdbadcac4e) |
| 37ec0983 | feat/card-task-37ec0983 | Succeeded | CARD-0723 | LOOK | LOOK | 7 | 3 | 80/40 |  | remote tip 49872e9a17 not ancestor of origin/master and != recorded base (91f4fbbecc); worktree HEAD 49872e9a17 not in origin/master, != base, != safe remote tip |
| 38caa745 | feat/card-task-38caa745 | Failed | CARD-0418 | LOOK | LOOK | 28 | 28 | 105/105 |  | remote tip 4543edb6aa not ancestor of origin/master and != recorded base (5ae4483701) |
| 3986b2f9 | feat/card-task-3986b2f9 | Succeeded | CARD-0079 | LOOK | LOOK | 24 | 4 | 88/46 |  | remote tip 6170f38b51 not ancestor of origin/master and != recorded base (f17b2ca45b); worktree HEAD abc7b90d17 not in origin/master, != base, != safe remote tip |
| 3a106641 | feat/card-task-3a106641 | Succeeded | CARD-0443 | - | LOOK | 24 | 4 | 55/32 |  | worktree HEAD 9fdf55f5ef not in origin/master, != base, != safe remote tip |
| 3bf6c1f4 | feat/card-task-3bf6c1f4 | Succeeded | CARD-0665 | LOOK | LOOK | 10 | 5 | 30/18 |  | remote tip 96da2576df not ancestor of origin/master and != recorded base (5f4161c5ba); worktree HEAD 96da2576df not in origin/master, != base, != safe remote tip |
| 3cfdd4a3 | feat/card-task-3cfdd4a3 | Failed | CARD-0643 | LOOK | - | 3 | 3 | 3/3 |  | remote tip d2e353d6ac not ancestor of origin/master and != recorded base (93ab36c434) |
| 3d5a2cc2 | feat/card-task-3d5a2cc2 | Succeeded | CARD-0589 | LOOK | LOOK | 16 | 1 | 50/33 |  | remote tip 56e9ef065d not ancestor of origin/master and != recorded base (cf5e42e5eb); worktree HEAD 56e9ef065d not in origin/master, != base, != safe remote tip |
| 40597978 | feat/card-task-40597978 | Succeeded | CARD-0208 | LOOK | - | 1 | 1 | 1/1 |  | remote tip a567c18e9d not ancestor of origin/master and != recorded base (none) |
| 426eb7b7 | feat/card-task-426eb7b7 | Succeeded | CARD-0632 | LOOK | LOOK | 1 | 1 | 1/1 |  | remote tip 3fd3bdc590 not ancestor of origin/master and != recorded base (4e873cf20a); worktree HEAD 3fd3bdc590 not in origin/master, != base, != safe remote tip |
| 48888860 | feat/card-task-48888860 | Succeeded | CARD-0478 | LOOK | LOOK | 33 | 4 | 109/85 |  | remote tip 3b021db9dd not ancestor of origin/master and != recorded base (11b6c7ff0a); worktree HEAD d3dff839aa not in origin/master, != base, != safe remote tip |
| 49545b22 | feat/card-task-49545b22 | Succeeded | CARD-0459 | LOOK | LOOK | 20 | 2 | 67/42 |  | remote tip 1a163836a5 not ancestor of origin/master and != recorded base (8c2be39633); worktree HEAD 972fac2035 not in origin/master, != base, != safe remote tip |
| 49e32a6e | feat/card-task-49e32a6e | Failed | CARD-0494 | LOOK | LOOK | 20 | 20 | 16/16 |  | remote tip 0c8d1cf521 not ancestor of origin/master and != recorded base (5acef0c583); worktree HEAD 0c8d1cf521 not in origin/master, != base, != safe remote tip |
| 4a1a4c4a | feat/card-task-4a1a4c4a | Succeeded | CARD-0330 | LOOK | LOOK | 1 | 1 | 3/3 |  | remote tip f14fee8b4d not ancestor of origin/master and != recorded base (cd133de977); worktree HEAD f14fee8b4d not in origin/master, != base, != safe remote tip |
| 4adbee37 | feat/card-task-4adbee37 | Succeeded | CARD-0478 | LOOK | - | 30 | 4 | 108/84 |  | remote tip 2680b1c6a9 not ancestor of origin/master and != recorded base (e682612081) |
| 4d9c7019 | feat/card-task-4d9c7019 | Failed | CARD-0418 | LOOK | LOOK | 14 | 14 | 89/89 |  | remote tip 220f944350 not ancestor of origin/master and != recorded base (f76c9f1762) |
| 4e9f5c29 | feat/card-task-4e9f5c29 | Succeeded | CARD-0511 | LOOK | - | 4 | 4 | 2/2 |  | remote tip 72cec68bf9 not ancestor of origin/master and != recorded base (3a62074ebf) |
| 4f849a0e | feat/card-task-4f849a0e | Succeeded | CARD-0515 | LOOK | - | 2 | 1 | 2/2 |  | remote tip 75eb16986b not ancestor of origin/master and != recorded base (none) |
| 4fe68cac | feat/card-task-4fe68cac | Succeeded | CARD-0575 | LOOK | - | 1 | 1 | 33/33 |  | remote tip 56d5ad47d7 not ancestor of origin/master and != recorded base (bb7b75353f) |
| 5120c098 | feat/card-task-5120c098 | Succeeded | CARD-0657 | LOOK | - | 14 | 1 | 14/12 |  | remote tip 1f1cb2e7c8 not ancestor of origin/master and != recorded base (240907422f) |
| 51e46dfa | feat/card-task-51e46dfa | Succeeded | CARD-0647 | LOOK | - | 1 | 1 | 18/15 |  | remote tip 520ca7306b not ancestor of origin/master and != recorded base (b8ac2d9648) |
| 578728aa | feat/card-task-578728aa | Succeeded | CARD-0716 | LOOK | LOOK | 9 | 1 | 16/6 |  | remote tip 48ea8fa516 not ancestor of origin/master and != recorded base (e4b4159d89); worktree HEAD 48ea8fa516 not in origin/master, != base, != safe remote tip |
| 59550e71 | feat/card-task-59550e71 | Failed | CARD-0490 | LOOK | - | 3 | 1 | 2/1 |  | remote tip 44e5f2e4fc not ancestor of origin/master and != recorded base (3c7a4057bc) |
| 59594204 | feat/card-task-59594204 | Succeeded | CARD-0726 | LOOK | LOOK | 27 | 3 | 30/9 |  | remote tip 32733b4b3c not ancestor of origin/master and != recorded base (c221227a49); worktree HEAD 32733b4b3c not in origin/master, != base, != safe remote tip |
| 59909fb5 | feat/card-task-59909fb5 | Succeeded | CARD-0727 | LOOK | LOOK | 10 | 1 | 36/19 |  | remote tip 7062c5cb10 not ancestor of origin/master and != recorded base (7526a1e197); worktree HEAD 7062c5cb10 not in origin/master, != base, != safe remote tip |
| 5a8dada2 | feat/card-task-5a8dada2 | Succeeded | CARD-0499 | LOOK | LOOK | 13 | 2 | 50/37 |  | remote tip b0cbcaeaf3 not ancestor of origin/master and != recorded base (096c16dfc2); worktree HEAD 6f1a53d2b8 not in origin/master, != base, != safe remote tip |
| 5bc00d31 | feat/card-task-5bc00d31 | Canceled | CARD-0527 | - | LOOK | 39 | 1 | 79/37 |  | worktree HEAD 284d9cc1f1 not in origin/master, != base, != safe remote tip |
| 5c9eda0c | feat/card-task-5c9eda0c | Succeeded | CARD-0497 | - | LOOK | 6 | 1 | 38/19 |  | worktree HEAD 3bfc7ae0fc not in origin/master, != base, != safe remote tip |
| 5e3ba4d4 | feat/card-task-5e3ba4d4 | Succeeded | CARD-0575 | LOOK | - | 5 | 5 | 1/1 |  | remote tip e25550e78a not ancestor of origin/master and != recorded base (d599b91bbc) |
| 5f4d2a5b | feat/card-task-5f4d2a5b | Succeeded | CARD-0544 | - | LOOK | 20 | 3 | 75/56 |  | worktree HEAD 4b90d575aa not in origin/master, != base, != safe remote tip |
| 60616b1c | feat/card-task-60616b1c | Succeeded | CARD-0519 | LOOK | LOOK | 1 | 1 | 1/1 |  | remote tip da96f385bf not ancestor of origin/master and != recorded base (cd133de977); worktree HEAD da96f385bf not in origin/master, != base, != safe remote tip |
| 62d06f4f | feat/card-task-62d06f4f | Failed | CARD-0079 | - | LOOK | 20 | 4 | 87/47 |  | worktree HEAD abc7b90d17 not in origin/master, != base, != safe remote tip |
| 636d4b7e | feat/card-task-636d4b7e | Succeeded | CARD-0478 | - | LOOK | 30 | 4 | 108/84 |  | worktree HEAD 2680b1c6a9 not in origin/master, != base, != safe remote tip |
| 6409a3c7 | feat/card-task-6409a3c7 | Failed | CARD-0459 | - | LOOK | 20 | 2 | 67/42 |  | worktree HEAD 1a163836a5 not in origin/master, != base, != safe remote tip |
| 64734e3f | feat/card-task-64734e3f | Succeeded | CARD-0672 | LOOK | LOOK | 14 | 1 | 28/14 |  | remote tip a11cdde418 not ancestor of origin/master and != recorded base (b633755fe5); worktree HEAD a11cdde418 not in origin/master, != base, != safe remote tip |
| 6630ad4e | feat/card-task-6630ad4e | Succeeded | CARD-0476 | LOOK | - | 6 | 1 | 15/9 |  | remote tip fc404dc760 not ancestor of origin/master and != recorded base (e0ccbcc4df) |
| 687a7f27 | feat/card-task-687a7f27 | Succeeded | CARD-0654 | LOOK | LOOK | 23 | 23 | 45/45 |  | remote tip f6ccaa6bc3 not ancestor of origin/master and != recorded base (cd133de977); worktree HEAD f6ccaa6bc3 not in origin/master, != base, != safe remote tip |
| 69514278 | feat/card-task-69514278 | Failed | CARD-0079 | LOOK | LOOK | 12 | 3 | 69/42 |  | remote tip ff8906b00b not ancestor of origin/master and != recorded base (f0972c42a6); worktree HEAD e7e1eafc01 not in origin/master, != base, != safe remote tip |
| 6a536d8d | feat/card-task-6a536d8d | Canceled | CARD-0679 | LOOK | LOOK | 12 | 1 | 16/15 |  | remote tip ad73820a0d not ancestor of origin/master and != recorded base (e819bc27f8) |
| 6a7c1d47 | feat/card-task-6a7c1d47 | Succeeded | CARD-0717 | LOOK | LOOK | 3 | 1 | 38/15 |  | remote tip 8a5f953a5f not ancestor of origin/master and != recorded base (536b241c81); worktree HEAD 8a5f953a5f not in origin/master, != base, != safe remote tip |
| 6b028949 | feat/card-task-6b028949 | Succeeded | CARD-0726 | - | LOOK | 20 | 3 | 30/12 |  | worktree HEAD c221227a49 not in origin/master, != base, != safe remote tip |
| 6b037196 | feat/card-task-6b037196 | Succeeded | CARD-0575 | LOOK | LOOK | 5 | 5 | 1/1 |  | remote tip e25550e78a not ancestor of origin/master and != recorded base (bb7b75353f); worktree HEAD 3cfe11ae2c not in origin/master, != base, != safe remote tip |
| 6b457b41 | feat/card-task-6b457b41 | Succeeded | CARD-0665 | LOOK | LOOK | 17 | 7 | 36/21 |  | remote tip 1625dea2d9 not ancestor of origin/master and != recorded base (96da2576df); worktree HEAD 1625dea2d9 not in origin/master, != base, != safe remote tip |
| 6c8bdb64 | feat/card-task-6c8bdb64 | Succeeded | CARD-0519 | LOOK | LOOK | 1 | 1 | 1/1 |  | remote tip a851f92be0 not ancestor of origin/master and != recorded base (4e873cf20a); worktree HEAD a851f92be0 not in origin/master, != base, != safe remote tip |
| 6cb9c0d9 | feat/card-task-6cb9c0d9 | Failed | CARD-0443 | LOOK | - | 18 | 4 | 48/33 |  | remote tip 6b2189863a not ancestor of origin/master and != recorded base (b85ff09197) |
| 6d2a2060 | feat/card-task-6d2a2060 | Canceled | CARD-0527 | LOOK | - | 8 | 2 | 56/45 |  | remote tip a4b35bf3ae not ancestor of origin/master and != recorded base (b97abfd838) |
| 6e332582 | feat/card-task-6e332582 | Failed | CARD-0459 | - | LOOK | 20 | 2 | 67/42 |  | worktree HEAD 1a163836a5 not in origin/master, != base, != safe remote tip |
| 700c3a06 | feat/card-task-700c3a06 | Failed | CARD-0727 | LOOK | LOOK | 1 | 1 | 1/1 |  | remote tip 1b2f33240c not ancestor of origin/master and != recorded base (4063a46d75) |
| 706a7582 | feat/card-task-706a7582 | Succeeded | CARD-0753 | LOOK | LOOK | 16 | 1 | 44/24 |  | remote tip 8e9b7bbf10 not ancestor of origin/master and != recorded base (8805577f19); worktree HEAD 8e9b7bbf10 not in origin/master, != base, != safe remote tip |
| 72b7c77e | feat/card-task-72b7c77e | Succeeded | CARD-0494 | - | LOOK | 21 | 21 | 17/17 |  | worktree HEAD 40fd10cd23 not in origin/master, != base, != safe remote tip |
| 73423389 | feat/card-task-73423389 | Succeeded | CARD-0649 | LOOK | LOOK | 1 | 1 | 4/4 |  | remote tip 7fb7bc5cb1 not ancestor of origin/master and != recorded base (cd133de977); worktree HEAD 7fb7bc5cb1 not in origin/master, != base, != safe remote tip |
| 75089dd7 | feat/card-task-75089dd7 | Succeeded | CARD-0589 | LOOK | LOOK | 13 | 1 | 49/33 |  | remote tip cf5e42e5eb not ancestor of origin/master and != recorded base (3fdd91abb7); worktree HEAD cf5e42e5eb not in origin/master, != base, != safe remote tip |
| 77bbad75 | feat/card-task-77bbad75 | Succeeded | CARD-0700 | LOOK | LOOK | 14 | 3 | 29/15 |  | remote tip 9f50188076 not ancestor of origin/master and != recorded base (30c1d6bb74); worktree HEAD 9f50188076 not in origin/master, != base, != safe remote tip |
| 79253363 | feat/card-task-79253363 | Succeeded | CARD-0511 | - | LOOK | 6 | 6 | 30/30 |  | worktree HEAD 63cab35e78 not in origin/master, != base, != safe remote tip |
| 7a33c7c8 | feat/card-task-7a33c7c8 | Succeeded | CARD-0494 | LOOK | LOOK | 1 | 1 | 1/1 |  | remote tip 1ca54fa18c not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD 1ca54fa18c not in origin/master, != base, != safe remote tip |
| 7a479eaf | feat/card-task-7a479eaf | Succeeded | CARD-0418 | LOOK | - | 16 | 16 | 85/85 |  | remote tip 0b314a9eaa not ancestor of origin/master and != recorded base (f26aa721e8) |
| 7ae56a95 | feat/card-task-7ae56a95 | Succeeded | CARD-0649 | LOOK | LOOK | 3 | 3 | 8/8 |  | remote tip 4f1cb43a02 not ancestor of origin/master and != recorded base (7fb7bc5cb1); worktree HEAD 4f1cb43a02 not in origin/master, != base, != safe remote tip |
| 7caf792c | feat/card-task-7caf792c | Succeeded | CARD-0516 | LOOK | LOOK | 1 | 1 | 1/1 |  | remote tip 46104c5b3d not ancestor of origin/master and != recorded base (a56c3a7e78); worktree HEAD 46104c5b3d not in origin/master, != base, != safe remote tip |
| 7dd45165 | feat/card-task-7dd45165 | Succeeded | CARD-0497 | - | LOOK | 4 | 1 | 37/21 |  | worktree HEAD 11eadbac90 not in origin/master, != base, != safe remote tip |
| 7ef54b1c | feat/card-task-7ef54b1c | Succeeded | CARD-0527 | - | LOOK | 29 | 1 | 64/39 |  | worktree HEAD 59e5499b72 not in origin/master, != base, != safe remote tip |
| 7fa08907 | feat/card-task-7fa08907 | Succeeded | CARD-0710 | LOOK | LOOK | 21 | 5 | 137/56 |  | remote tip b4a91c4eed not ancestor of origin/master and != recorded base (fdf3778a41); worktree HEAD b4a91c4eed not in origin/master, != base, != safe remote tip |
| 7fb01e03 | feat/card-task-7fb01e03 | Succeeded | CARD-0478 | - | LOOK | 28 | 4 | 104/82 |  | worktree HEAD 252de2bf68 not in origin/master, != base, != safe remote tip |
| 7fd8b013 | feat/card-task-7fd8b013 | Succeeded | CARD-0478 | - | LOOK | 26 | 4 | 103/81 |  | worktree HEAD c86c927aaa not in origin/master, != base, != safe remote tip |
| 80155ada | feat/card-task-80155ada | Succeeded | CARD-0527 | - | LOOK | 33 | 1 | 68/37 |  | worktree HEAD 9f2cd5f168 not in origin/master, != base, != safe remote tip |
| 8224692f | feat/card-task-8224692f | Succeeded | CARD-0461 | - | LOOK | 10 | 2 | 45/21 |  | worktree HEAD 6d659918fa not in origin/master, != base, != safe remote tip |
| 82a076e4 | feat/card-task-82a076e4 | Succeeded | CARD-0723 | LOOK | LOOK | 5 | 2 | 74/47 |  | remote tip 91f4fbbecc not ancestor of origin/master and != recorded base (54248ad4c2); worktree HEAD 91f4fbbecc not in origin/master, != base, != safe remote tip |
| 835dd574 | feat/card-task-835dd574 | Succeeded | CARD-0784 | LOOK | LOOK | 22 | 22 | 101/101 |  | remote tip ca65d64fe2 not ancestor of origin/master and != recorded base (5ae4483701); worktree HEAD ca65d64fe2 not in origin/master, != base, != safe remote tip |
| 848385e4 | feat/card-task-848385e4 | Succeeded | CARD-0497 | - | LOOK | 7 | 1 | 38/17 |  | worktree HEAD 21e9abed31 not in origin/master, != base, != safe remote tip |
| 84de0093 | feat/card-task-84de0093 | Succeeded | CARD-0701 | LOOK | LOOK | 6 | 2 | 27/12 |  | remote tip 4afedf12ee not ancestor of origin/master and != recorded base (196f21e068); worktree HEAD 4afedf12ee not in origin/master, != base, != safe remote tip |
| 85f476c7 | feat/card-task-85f476c7 | Succeeded | CARD-0590 | - | LOOK | 12 | 1 | 54/27 |  | worktree HEAD 2c80c976dc not in origin/master, != base, != safe remote tip |
| 88485f49 | feat/card-task-88485f49 | Succeeded | CARD-0672 | LOOK | LOOK | 13 | 1 | 28/15 |  | remote tip b633755fe5 not ancestor of origin/master and != recorded base (0497e0da55); worktree HEAD b633755fe5 not in origin/master, != base, != safe remote tip |
| 8ac49e37 | feat/card-task-8ac49e37 | Failed | CARD-0418 | LOOK | LOOK | 21 | 21 | 94/94 |  | remote tip 5ae4483701 not ancestor of origin/master and != recorded base (c3e2f51bb1) |
| 8b6b42a9 | feat/card-task-8b6b42a9 | Failed | CARD-0593 | LOOK | LOOK | 31 | 6 | 28/14 |  | remote tip 63d26492a8 not ancestor of origin/master and != recorded base (dbce1aae36) |
| 8b8a542d | feat/card-task-8b8a542d | Succeeded | CARD-0443 | - | LOOK | 18 | 4 | 48/33 |  | worktree HEAD 6b2189863a not in origin/master, != base, != safe remote tip |
| 90228735 | feat/card-task-90228735 | Succeeded | CARD-0718 | LOOK | LOOK | 20 | 1 | 42/22 |  | remote tip 733523ef0f not ancestor of origin/master and != recorded base (2fd6d7f6b1); worktree HEAD 733523ef0f not in origin/master, != base, != safe remote tip |
| 903bf8a7 | feat/card-task-903bf8a7 | Failed | CARD-0647 | LOOK | - | 1 | 1 | 1/1 |  | remote tip b3f7942bfa not ancestor of origin/master and != recorded base (e923753569) |
| 9156f49a | feat/card-task-9156f49a | Failed | CARD-0590 | - | LOOK | 11 | 1 | 54/27 |  | worktree HEAD 7359932694 not in origin/master, != base, != safe remote tip |
| 957902d6 | feat/card-task-957902d6 | Succeeded | CARD-0700 | LOOK | LOOK | 6 | 2 | 15/10 |  | remote tip 275749c48d not ancestor of origin/master and != recorded base (fbdc3c6e66); worktree HEAD 275749c48d not in origin/master, != base, != safe remote tip |
| 968d7fd5 | feat/card-task-968d7fd5 | Succeeded | CARD-0478 | - | LOOK | 19 | 4 | 100/83 |  | worktree HEAD c604692a74 not in origin/master, != base, != safe remote tip |
| 96ae83d8 | feat/card-task-96ae83d8 | Succeeded | CARD-0407 | LOOK | LOOK | 3 | 3 | 22/22 |  | remote tip 20805c83ce not ancestor of origin/master and != recorded base (cd133de977); worktree HEAD 20805c83ce not in origin/master, != base, != safe remote tip |
| 9775fe45 | feat/card-task-9775fe45 | Failed |  | LOOK | - | 1 | 1 | 9/8 |  | remote tip 191347e7d0 not ancestor of origin/master and != recorded base (none) |
| 9823bd64 | feat/card-task-9823bd64 | Succeeded | CARD-0478 | - | LOOK | 26 | 4 | 103/81 |  | worktree HEAD c86c927aaa not in origin/master, != base, != safe remote tip |
| 98d80cb8 | feat/card-task-98d80cb8 | Succeeded | CARD-0633 | LOOK | - | 10 | 1 | 19/14 |  | remote tip c1272a746f not ancestor of origin/master and != recorded base (3bd003eab5) |
| 990d4a82 | feat/card-task-990d4a82 | Failed | CARD-0459 | - | LOOK | 17 | 2 | 66/45 |  | worktree HEAD 972fac2035 not in origin/master, != base, != safe remote tip |
| 9979ed47 | feat/card-task-9979ed47 | Succeeded | CARD-0476 | - | LOOK | 6 | 1 | 15/9 |  | worktree HEAD fc404dc760 not in origin/master, != base, != safe remote tip |
| 998b6d6a | feat/card-task-998b6d6a | Succeeded | CARD-0478 | - | LOOK | 22 | 4 | 103/84 |  | worktree HEAD 1ff834bb6d not in origin/master, != base, != safe remote tip |
| 9edf8224 | feat/card-task-9edf8224 | Succeeded | CARD-0511 | LOOK | - | 2 | 2 | 2/2 |  | remote tip 5eb6e0332a not ancestor of origin/master and != recorded base (3a62074ebf) |
| 9f69ac66 | feat/card-task-9f69ac66 | Succeeded | CARD-0726 | LOOK | LOOK | 20 | 3 | 30/12 |  | remote tip c221227a49 not ancestor of origin/master and != recorded base (5a19e2f808); worktree HEAD c221227a49 not in origin/master, != base, != safe remote tip |
| a1d37ec8 | feat/card-task-a1d37ec8 | Succeeded | CARD-0134 | LOOK | - | 1 | 1 | 1/1 |  | remote tip 7a2f11d28b not ancestor of origin/master and != recorded base (none) |
| a24a626a | feat/card-task-a24a626a | Succeeded | CARD-0631 | LOOK | - | 16 | 2 | 34/26 |  | remote tip 89b32d92b7 not ancestor of origin/master and != recorded base (f9b3d11a7d) |
| a5e37919 | feat/card-task-a5e37919 | Succeeded | CARD-0461 | LOOK | - | 13 | 3 | 45/18 |  | remote tip 69de448905 not ancestor of origin/master and != recorded base (10140e3d5e) |
| a6276b12 | feat/card-task-a6276b12 | Failed | CARD-0590 | - | LOOK | 13 | 1 | 54/26 |  | worktree HEAD 22f19c5483 not in origin/master, != base, != safe remote tip |
| aa4febb4 | feat/card-task-aa4febb4 | Succeeded | CARD-0657 | LOOK | - | 18 | 1 | 16/12 |  | remote tip 50565aa552 not ancestor of origin/master and != recorded base (1f1cb2e7c8) |
| aaab8f81 | feat/card-task-aaab8f81 | Succeeded | CARD-0544 | LOOK | - | 20 | 3 | 75/56 |  | remote tip 4b90d575aa not ancestor of origin/master and != recorded base (75e42cf30b) |
| ad020e8c | feat/card-task-ad020e8c | Succeeded | CARD-0417 | LOOK | LOOK | 1 | 1 | 3/3 |  | remote tip a12de73304 not ancestor of origin/master and != recorded base (cd133de977); worktree HEAD a12de73304 not in origin/master, != base, != safe remote tip |
| addeb569 | feat/card-task-addeb569 | Succeeded | CARD-0584 | LOOK | LOOK | 9 | 1 | 20/7 |  | remote tip 0423d1a2b4 not ancestor of origin/master and != recorded base (6c4e5906a7); worktree HEAD 0423d1a2b4 not in origin/master, != base, != safe remote tip |
| adeb92eb | feat/card-task-adeb92eb | Failed | CARD-0459 | - | LOOK | 20 | 2 | 67/42 |  | worktree HEAD 1a163836a5 not in origin/master, != base, != safe remote tip |
| ae40541f | feat/card-task-ae40541f | Failed | CARD-0418 | LOOK | LOOK | 2 | 2 | 28/28 |  | remote tip f76c9f1762 not ancestor of origin/master and != recorded base (a0e73c976a) |
| af90420b | feat/card-task-af90420b | Succeeded | CARD-0459 | LOOK | LOOK | 17 | 2 | 66/45 |  | remote tip 972fac2035 not ancestor of origin/master and != recorded base (3c7a4057bc); worktree HEAD 0b1c94fb09 not in origin/master, != base, != safe remote tip |
| aff1b9c6 | feat/card-task-aff1b9c6 | Failed | CARD-0628 | LOOK | - | 1 | 1 | 1/1 |  | remote tip ddd06ce7aa not ancestor of origin/master and != recorded base (b8ac2d9648) |
| b03a74a0 | feat/card-task-b03a74a0 | Failed | CARD-0590 | LOOK | - | 12 | 1 | 54/27 |  | remote tip 2c80c976dc not ancestor of origin/master and != recorded base (f0972c42a6) |
| b1ffd29e | feat/card-task-b1ffd29e | Succeeded | CARD-0700 | LOOK | LOOK | 8 | 2 | 25/16 |  | remote tip 30c1d6bb74 not ancestor of origin/master and != recorded base (275749c48d); worktree HEAD 30c1d6bb74 not in origin/master, != base, != safe remote tip |
| b23741b2 | feat/card-task-b23741b2 | Succeeded | CARD-0527 | LOOK | - | 29 | 1 | 64/39 |  | remote tip 59e5499b72 not ancestor of origin/master and != recorded base (b97abfd838) |
| b23d0fe2 | feat/card-task-b23d0fe2 | Canceled | CARD-0527 | LOOK | - | 9 | 8 | 42/42 |  | remote tip b05c5f0a46 not ancestor of origin/master and != recorded base (b97abfd838) |
| b4e6383d | feat/card-task-b4e6383d | Succeeded | CARD-0034 | LOOK | - | 1 | 1 | 2/2 |  | remote tip 016fda9f55 not ancestor of origin/master and != recorded base (none) |
| b9c5b5e6 | feat/card-task-b9c5b5e6 | Failed | CARD-0490 | - | LOOK | 24 | 1 | 89/58 |  | worktree HEAD c648f83a4a not in origin/master, != base, != safe remote tip |
| b9ca189f | feat/card-task-b9ca189f | Succeeded | CARD-0505 | LOOK | LOOK | 1 | 1 | 1/1 |  | remote tip e0959ec68f not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD e0959ec68f not in origin/master, != base, != safe remote tip |
| ba063df7 | feat/card-task-ba063df7 | Succeeded | CARD-0666 | LOOK | - | 13 | 1 | 16/9 |  | remote tip ae9a27c11d not ancestor of origin/master and != recorded base (d87edfc70d) |
| ba318e38 | feat/card-task-ba318e38 | Succeeded | CARD-0575 | LOOK | - | 1 | 1 | 1/1 |  | remote tip 6f16fb6128 not ancestor of origin/master and != recorded base (bb7b75353f) |
| babf30bb | feat/card-task-babf30bb | Succeeded | CARD-0478 | - | LOOK | 22 | 4 | 103/84 |  | worktree HEAD 1ff834bb6d not in origin/master, != base, != safe remote tip |
| bb485c68 | feat/card-task-bb485c68 | Succeeded | CARD-0584 | LOOK | LOOK | 4 | 1 | 20/11 |  | remote tip e7228921e8 not ancestor of origin/master and != recorded base (e0be7f5e62); worktree HEAD e7228921e8 not in origin/master, != base, != safe remote tip |
| bb9da5d3 | feat/card-task-bb9da5d3 | Succeeded | CARD-0490 | LOOK | - | 1 | 1 | 1/1 |  | remote tip 6e21b472da not ancestor of origin/master and != recorded base (723ac9534f) |
| bbf1854c | feat/card-task-bbf1854c | Succeeded | CARD-0590 | - | LOOK | 11 | 1 | 54/27 |  | worktree HEAD 7359932694 not in origin/master, != base, != safe remote tip |
| be035022 | feat/card-task-be035022 | Succeeded | CARD-0710 | LOOK | LOOK | 8 | 2 | 77/40 |  | remote tip 66d67bdfad not ancestor of origin/master and != recorded base (13d8cac294); worktree HEAD 66d67bdfad not in origin/master, != base, != safe remote tip |
| bf31273c | feat/card-task-bf31273c | Succeeded | CARD-0057 | LOOK | - | 1 | 1 | 35/21 |  | remote tip dc2a7fd600 not ancestor of origin/master and != recorded base (eab2608849) |
| c03af147 | feat/card-task-c03af147 | Failed | CARD-0649 | LOOK | LOOK | 4 | 4 | 7/7 |  | remote tip b7bd0b7483 not ancestor of origin/master and != recorded base (7fb7bc5cb1) |
| c0543045 | feat/card-task-c0543045 | Succeeded | CARD-0714 | LOOK | LOOK | 8 | 1 | 16/3 |  | remote tip 51997d8cf6 not ancestor of origin/master and != recorded base (339edfb236); worktree HEAD 51997d8cf6 not in origin/master, != base, != safe remote tip |
| c4a9ddcc | feat/card-task-c4a9ddcc | Succeeded | CARD-0476 | - | LOOK | 5 | 1 | 14/9 |  | worktree HEAD afdabd320f not in origin/master, != base, != safe remote tip |
| c60d9c3b | feat/card-task-c60d9c3b | Succeeded | CARD-0726 | - | LOOK | 27 | 3 | 30/9 |  | worktree HEAD 32733b4b3c not in origin/master, != base, != safe remote tip |
| c9a3d250 | feat/card-task-c9a3d250 | Canceled | CARD-0527 | - | LOOK | 39 | 1 | 79/37 |  | worktree HEAD 284d9cc1f1 not in origin/master, != base, != safe remote tip |
| ca4eef9c | feat/card-task-ca4eef9c | Succeeded | CARD-0527 | LOOK | - | 33 | 1 | 68/37 |  | remote tip 9f2cd5f168 not ancestor of origin/master and != recorded base (8435fb16a1) |
| cb060b4e | feat/card-task-cb060b4e | Succeeded | CARD-0628 | LOOK | - | 13 | 1 | 29/23 |  | remote tip f9b3d11a7d not ancestor of origin/master and != recorded base (4bc8b5d595) |
| cb16cc6f | feat/card-task-cb16cc6f | Canceled | CARD-0459 | - | LOOK | 20 | 2 | 67/42 |  | worktree HEAD 1a163836a5 not in origin/master, != base, != safe remote tip |
| cc0dab3d | feat/card-task-cc0dab3d | Succeeded | CARD-0494 | LOOK | LOOK | 2 | 2 | 1/1 |  | remote tip 93c4273615 not ancestor of origin/master and != recorded base (1ca54fa18c); worktree HEAD 93c4273615 not in origin/master, != base, != safe remote tip |
| cd1d39f7 | feat/card-task-cd1d39f7 | Failed | CARD-0478 | LOOK | - | 10 | 3 | 90/77 |  | remote tip 244316125c not ancestor of origin/master and != recorded base (11b6c7ff0a) |
| ce048398 | feat/card-task-ce048398 | Succeeded | CARD-0079 | LOOK | - | 24 | 4 | 88/46 | Unconfirmed | remote tip 6170f38b51 not ancestor of origin/master and != recorded base (d0ab05f74d); land request state NeedsResolution (unresolved) |
| cebd6310 | feat/card-task-cebd6310 | Succeeded | CARD-0459 | LOOK | - | 14 | 2 | 64/43 |  | remote tip 0b1c94fb09 not ancestor of origin/master and != recorded base (3c7a4057bc) |
| cecb5ee5 | feat/card-task-cecb5ee5 | Succeeded | CARD-0726 | LOOK | LOOK | 12 | 3 | 29/14 |  | remote tip 5a19e2f808 not ancestor of origin/master and != recorded base (b6a262e2ae); worktree HEAD 5a19e2f808 not in origin/master, != base, != safe remote tip |
| d00498a9 | feat/card-task-d00498a9 | Succeeded | CARD-0459 | - | LOOK | 20 | 2 | 67/42 |  | worktree HEAD 1a163836a5 not in origin/master, != base, != safe remote tip |
| d09d94da | feat/card-task-d09d94da | Succeeded | CARD-0717 | LOOK | LOOK | 7 | 1 | 39/10 |  | remote tip 9f70d391b9 not ancestor of origin/master and != recorded base (9729957bd1); worktree HEAD 9f70d391b9 not in origin/master, != base, != safe remote tip |
| d1ab684b | feat/card-task-d1ab684b | Succeeded | CARD-0552 | LOOK | - | 1 | 1 | 1/1 |  | remote tip 94a242150d not ancestor of origin/master and != recorded base (7fc575c96d) |
| d2117ffc | feat/card-task-d2117ffc | Succeeded | CARD-0544 | LOOK | - | 22 | 1 | 76/51 | Unconfirmed | remote tip ba8e0717f5 not ancestor of origin/master and != recorded base (e2a49f3a59); land request state NeedsResolution (unresolved) |
| d2a0d121 | feat/card-task-d2a0d121 | Succeeded | CARD-0589 | LOOK | LOOK | 22 | 1 | 51/31 |  | remote tip 28151cecc9 not ancestor of origin/master and != recorded base (56e9ef065d); worktree HEAD 28151cecc9 not in origin/master, != base, != safe remote tip |
| d3319023 | feat/card-task-d3319023 | Failed | CARD-0490 | LOOK | LOOK | 20 | 1 | 88/59 |  | remote tip da25108bbd not ancestor of origin/master and != recorded base (3c7a4057bc); worktree HEAD 481ef905cd not in origin/master, != base, != safe remote tip |
| d39c5ba8 | feat/card-task-d39c5ba8 | Succeeded | CARD-0679 | LOOK | - | 8 | 1 | 11/11 |  | remote tip e819bc27f8 not ancestor of origin/master and != recorded base (7ad846cba2) |
| d77c21f4 | feat/card-task-d77c21f4 | Succeeded | CARD-0772 | LOOK | LOOK | 1 | 1 | 2/2 |  | remote tip f9f08cf2b4 not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD f9f08cf2b4 not in origin/master, != base, != safe remote tip |
| d98e015b | feat/card-task-d98e015b | Succeeded | CARD-0499 | - | LOOK | 13 | 2 | 50/37 |  | worktree HEAD b0cbcaeaf3 not in origin/master, != base, != safe remote tip |
| d9b02a4c | feat/card-task-d9b02a4c | Succeeded | CARD-0206 | LOOK | - | 1 | 1 | 1/1 |  | remote tip 4dab0fb33d not ancestor of origin/master and != recorded base (none) |
| d9b1f124 | feat/card-task-d9b1f124 | Succeeded | CARD-0688 | LOOK | LOOK | 8 | 1 | 58/34 |  | remote tip 2d90f22a78 not ancestor of origin/master and != recorded base (e91a78cf0d); worktree HEAD 2d90f22a78 not in origin/master, != base, != safe remote tip |
| d9db97b9 | feat/card-task-d9db97b9 | Failed | CARD-0508 | LOOK | LOOK | 4 | 4 | 5/5 |  | remote tip 0e69f6fba5 not ancestor of origin/master and != recorded base (eb3e45157a) |
| dae3ad6b | feat/card-task-dae3ad6b | Failed | CARD-0590 | LOOK | LOOK | 14 | 1 | 54/26 |  | remote tip c308a510f6 not ancestor of origin/master and != recorded base (f0972c42a6); worktree HEAD 234bca2835 not in origin/master, != base, != safe remote tip |
| dd3ada6f | feat/card-task-dd3ada6f | Succeeded | CARD-0496 | LOOK | - | 5 | 4 | 12/12 |  | remote tip 34542cadc0 not ancestor of origin/master and != recorded base (3b0ca030ba) |
| dd3c58b5 | feat/card-task-dd3c58b5 | Succeeded | CARD-0584 | LOOK | LOOK | 7 | 1 | 20/11 |  | remote tip 6c4e5906a7 not ancestor of origin/master and != recorded base (e7228921e8); worktree HEAD 6c4e5906a7 not in origin/master, != base, != safe remote tip |
| dde7cd59 | feat/card-task-dde7cd59 | Succeeded | CARD-0644 | LOOK | - | 5 | 4 | 16/16 |  | remote tip 9cb69aed8d not ancestor of origin/master and != recorded base (c8f4cdf9c4) |
| e0b220e4 | feat/card-task-e0b220e4 | Succeeded | CARD-0494 | LOOK | LOOK | 19 | 19 | 14/14 |  | remote tip 5acef0c583 not ancestor of origin/master and != recorded base (d89594cdf1); worktree HEAD 5acef0c583 not in origin/master, != base, != safe remote tip |
| e1c0cc33 | feat/card-task-e1c0cc33 | Succeeded | CARD-0633 | LOOK | - | 7 | 1 | 18/14 |  | remote tip 3bd003eab5 not ancestor of origin/master and != recorded base (2c8574a1e8) |
| e229b8cb | feat/card-task-e229b8cb | Succeeded | CARD-0478 | - | LOOK | 32 | 4 | 109/85 |  | worktree HEAD 4b2041de05 not in origin/master, != base, != safe remote tip |
| e32907bf | feat/card-task-e32907bf | Failed | CARD-0079 | LOOK | LOOK | 20 | 4 | 87/47 |  | remote tip abc7b90d17 not ancestor of origin/master and != recorded base (f0972c42a6); worktree HEAD ff8906b00b not in origin/master, != base, != safe remote tip |
| e3316373 | feat/card-task-e3316373 | Succeeded | CARD-0442 | LOOK | - | 10 | 10 | 37/37 |  | remote tip 32473bfb65 not ancestor of origin/master and != recorded base (none) |
| e406088d | feat/card-task-e406088d | Succeeded | CARD-0478 | - | LOOK | 33 | 4 | 109/85 |  | worktree HEAD 3b021db9dd not in origin/master, != base, != safe remote tip |
| e5d0294c | feat/card-task-e5d0294c | Failed | CARD-0710 | LOOK | LOOK | 5 | 1 | 69/35 |  | remote tip 13d8cac294 not ancestor of origin/master and != recorded base (813ac55169) |
| e67555ad | feat/card-task-e67555ad | Succeeded | CARD-0552 | LOOK | - | 13 | 13 | 59/59 |  | remote tip b2353ff6d1 not ancestor of origin/master and != recorded base (77e080f3a6) |
| e839b406 | feat/card-task-e839b406 | Succeeded | CARD-0657 | LOOK | LOOK | 22 | 1 | 17/11 |  | remote tip 11ea499a25 not ancestor of origin/master and != recorded base (50565aa552) |
| e9b646bf | feat/card-task-e9b646bf | Succeeded | CARD-0726 | - | LOOK | 12 | 3 | 29/14 |  | worktree HEAD 5a19e2f808 not in origin/master, != base, != safe remote tip |
| ea257e64 | feat/card-task-ea257e64 | Failed | CARD-0499 | LOOK | - | 14 | 2 | 51/36 | Unconfirmed | remote tip 8e8f3e6b21 not ancestor of origin/master and != recorded base (3510fc1410) |
| ec0d8cc3 | feat/card-task-ec0d8cc3 | Succeeded | CARD-0714 | LOOK | LOOK | 6 | 1 | 16/6 |  | remote tip 339edfb236 not ancestor of origin/master and != recorded base (8479729fe8); worktree HEAD 339edfb236 not in origin/master, != base, != safe remote tip |
| ecea46a3 | feat/card-task-ecea46a3 | Failed | CARD-0418 | LOOK | LOOK | 18 | 18 | 94/94 |  | remote tip 714028d478 not ancestor of origin/master and != recorded base (220f944350) |
| edd44e28 | feat/card-task-edd44e28 | Failed | CARD-0418 | LOOK | LOOK | 19 | 19 | 94/94 |  | remote tip fdbadcac4e not ancestor of origin/master and != recorded base (714028d478) |
| ef7c4ee0 | feat/card-task-ef7c4ee0 | Failed | CARD-0550 | LOOK | LOOK | 3 | 1 | 15/0 |  | remote tip 8e6807783b not ancestor of origin/master and != recorded base (a0e73c976a) |
| f1139c44 | feat/card-task-f1139c44 | Succeeded | CARD-0511 | LOOK | - | 6 | 6 | 30/30 |  | remote tip 63cab35e78 not ancestor of origin/master and != recorded base (3a62074ebf) |
| f122b9de | feat/card-task-f122b9de | Succeeded | CARD-0727 | LOOK | LOOK | 3 | 3 | 17/0 |  | remote tip 872eca26c9 not ancestor of origin/master and != recorded base (2f050e3d2b); worktree HEAD 872eca26c9 not in origin/master, != base, != safe remote tip |
| f1f3b5a8 | feat/card-task-f1f3b5a8 | Succeeded | CARD-0398 | LOOK | - | 1 | 1 | 1/1 |  | remote tip 9043b818f2 not ancestor of origin/master and != recorded base (3c41c26ce6) |
| f32006d5 | feat/card-task-f32006d5 | Canceled | CARD-0710 | LOOK | LOOK | 15 | 4 | 124/64 |  | remote tip fdf3778a41 not ancestor of origin/master and != recorded base (66d67bdfad) |
| f37f0252 | feat/card-task-f37f0252 | Failed | CARD-0467 | - | LOOK | 6 | 5 | 8/8 |  | worktree HEAD eaf7a53c5e not in origin/master, != base, != safe remote tip |
| f450ecba | feat/card-task-f450ecba | Succeeded | CARD-0472 | LOOK | - | 1 | 1 | 1/1 |  | remote tip f20ff0430b not ancestor of origin/master and != recorded base (none) |
| f4ef4af4 | feat/card-task-f4ef4af4 | Succeeded | CARD-0558 | LOOK | LOOK | 7 | 1 | 53/10 |  | remote tip 5600dcae30 not ancestor of origin/master and != recorded base (eb3e45157a); worktree HEAD 5600dcae30 not in origin/master, != base, != safe remote tip |
| f5408ebf | feat/card-task-f5408ebf | Canceled | CARD-0558 | LOOK | LOOK | 9 | 1 | 54/7 | Unconfirmed | remote tip 959e5aa8a5 not ancestor of origin/master and != recorded base (ee5e5b6893); worktree HEAD 959e5aa8a5 not in origin/master, != base, != safe remote tip |
| f7cf0fe7 | feat/card-task-f7cf0fe7 | Succeeded | CARD-0575 | LOOK | - | 6 | 6 | 1/1 |  | remote tip 7e07c7c302 not ancestor of origin/master and != recorded base (3c7a4057bc) |
| f94c8fa4 | feat/card-task-f94c8fa4 | Succeeded | CARD-0672 | LOOK | - | 5 | 1 | 24/16 |  | remote tip 9eddea8ae0 not ancestor of origin/master and != recorded base (917cdbcd14) |
| fa0fe66c | feat/card-task-fa0fe66c | Succeeded | CARD-0497 | - | LOOK | 6 | 1 | 38/19 |  | worktree HEAD 3bfc7ae0fc not in origin/master, != base, != safe remote tip |
| fa16db12 | feat/card-task-fa16db12 | Succeeded | CARD-0714 | LOOK | LOOK | 3 | 1 | 16/6 |  | remote tip 8479729fe8 not ancestor of origin/master and != recorded base (16f0a8caf3); worktree HEAD 8479729fe8 not in origin/master, != base, != safe remote tip |
| fa71cb77 | feat/card-task-fa71cb77 | Succeeded | CARD-0163 | LOOK | - | 1 | 1 | 1/1 |  | remote tip 8c9c43ce99 not ancestor of origin/master and != recorded base (none) |
| fb15a6fe | feat/card-task-fb15a6fe | Succeeded | CARD-0032 | LOOK | - | 1 | 1 | 1/1 |  | remote tip dce786f70d not ancestor of origin/master and != recorded base (none) |
| fb9092cf | feat/card-task-fb9092cf | Succeeded | CARD-0478 | - | LOOK | 28 | 4 | 104/82 |  | worktree HEAD 252de2bf68 not in origin/master, != base, != safe remote tip |
| fc62d370 | feat/card-task-fc62d370 | Succeeded | CARD-0717 | LOOK | LOOK | 5 | 1 | 38/10 |  | remote tip 9729957bd1 not ancestor of origin/master and != recorded base (8a5f953a5f); worktree HEAD 9729957bd1 not in origin/master, != base, != safe remote tip |
| fcf948b8 | feat/card-task-fcf948b8 | Failed | CARD-0305 | LOOK | - | 1 | 1 | 22/18 |  | remote tip 4370b0075e not ancestor of origin/master and != recorded base (8e8773ecd3) |
| fe37038c | feat/card-task-fe37038c | Succeeded | CARD-0665 | LOOK | - | 8 | 3 | 24/18 |  | remote tip d4dfde724a not ancestor of origin/master and != recorded base (cc92f47640) |
| ff25672d | feat/card-task-ff25672d | Succeeded | CARD-0461 | - | LOOK | 10 | 2 | 45/21 |  | worktree HEAD 6d659918fa not in origin/master, != base, != safe remote tip |
| ff5ae210 | feat/card-task-ff5ae210 | Succeeded | CARD-0698 | LOOK | LOOK | 1 | 1 | 17/17 |  | remote tip f6b54d8e54 not ancestor of origin/master and != recorded base (3fdd91abb7); worktree HEAD f6b54d8e54 not in origin/master, != base, != safe remote tip |

## Post-run counts

After the run (includes anything created while it ran): 439 remote heads (418 matching), 310 registered worktrees, 312 entries under C:\Antiphon\worktrees. Local `feat/card-task-*` branch refs were intentionally kept.

## Skipped non-matching worktree directories (rule 1)

- `.antiphon`
- `base-46fbc379`
- `c443-base-6cb9c0d9`
- `c459-base`
- `c467-next-7e73872f`
- `c508-base`
- `c508-base-6bb32617`
- `c665-base-1fff5c74`
- `c710rv-results`
- `card-CARD-0004`
- `card-CARD-0512`
- `card-task-40927996-baseline`
- `card-task-4d7a799b-base`
- `card-task-5e4076c9-base`
- `card-task-7f3e581c-base`
- `card-task-8747995c-base`
- `card-task-e8cdc7fa-base`
- `hotfix-snapshot-0698`
- `land`
- `mutation-card-0055`
