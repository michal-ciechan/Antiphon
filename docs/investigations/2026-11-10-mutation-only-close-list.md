# Mutation-only and owed-check close list (2026-11-10)

Read-only audit. No card was moved, closed, or edited. Enumeration used `scripts/card.ps1 list -Board Antiphon` for Review (125), InProgress (43) and Backlog (272); each list returned `truncated=false`. Full card bodies were read for all 440. Revision history was read for every Review and InProgress card, and for the Backlog candidates and the Done parents cited below. In-flight tasks are `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&status=Queued,Dispatched,Working,Blocked` at the end of the audit.

CLOSE requires all three: (a) the implementing commit is on `origin/master`, (b) a Clean Final Review with a task id, (c) the only open items are mutation positive controls, owed native Windows/Linux reruns, or the same kind of verification, with no unfixed finding, no unlanded fix branch, no failing test, no open Debug/Review/Code task, and no human decision. Excluded umbrellas, not classified: CARD-1514, CARD-1729, CARD-1832, CARD-1836, CARD-1546 (not in these three columns), CARD-1829, CARD-1830.

## Close

One card. The plain list is `docs/investigations/2026-11-10-mutation-only-close.txt`.

| Card | Title | Landed SHA | Review task | Remaining items | Verdict |
|---|---|---|---|---|---|
| CARD-1028 | Qualify Windows coverage symlink and selected-project reparse | `7ec17bf9023eb2b7f36f84771144a844178a730c` | a5c51cc2, Clean Final Review of CARD-1001 | NQ-1 in-root symlink acceptance and NQ-2 selected non-regular reparse. Both are unrun native Windows checks. The desktop account cannot create the symlink and has no headless debugger. | CLOSE |

CARD-1001 is Done. Its close revision says it landed at 7ec17bf9 (full SHA above; `git merge-base --is-ancestor` is true) after Clean Final Review task a5c51cc2, owner Code d579272f, source f4044e8f. CARD-1028 states that those two checks do not discharge PC-1 through PC-5. Those PCs stay named on Done CARD-1001 and on open CARD-1013, which also has an unresolved digest mismatch, so 1013 stays open. CARD-1028 has no open task.

## Kept open: verification-shaped, missing a close condition

| Card | Title | Landed SHA | Review task | Remaining items | Verdict |
|---|---|---|---|---|---|
| CARD-1824 | Owed activation measurements: CARD-0698, CARD-0712, CARD-1349 | dbb22ef37c9521d8472cb090d39aa36ed78818d8, 5f4e92e4e68235a767f9329fe3b27f3444662ef8, 69ffaffafd78d69c0626a7da56139d5d2fc9f9a1, a4207bf702eca3c911c43f4f056e490db484f6ea, 89e4c7d664bfa906ed614d4c0071057c64c674d6, 71a1809cfec74eebf01b1e27dc25140cbff741ff, 4e66a2c64c2e70c608557ae18b8dd63da64ddab9 | Parents Done. Review tasks settled Succeeded (0698: 5294bdc8, d9c6937d; 0712: 6d623e3b; 1349: f1303701, 4dd6a393, 7f9ed421, 8f22dedb). No Clean Final Review text. | Live pg_stat seq-scan remeasure, live DISCARD ALL remeasure, and the CARD-1349 qualification the commit subjects still call pending. | KEEP-OPEN |
| CARD-1644 | Owed native checks for CARD-1630 | 685ddb51ee22b061ff319a423b128c33abcddc80 (cherry-picked from 0dbadd9ef and 6e4f208f6) | None. The card says no separate Final Review of 0dbadd9ef. Parent CARD-1630 was closed by operator decision. | Windows reruns of C448_V28 and C1076_RunOptionsBudget, plus the native snapshot path. CARD-0752 still records C448_V28 failing on Windows at an older master. | KEEP-OPEN |
| CARD-1643 | Owed native checks for CARD-1619 | 043a886a2, 7718d8834, 16ccb04ae, 67f80cd7d, 651eb516b, 20f0801bd, 00ee6f4c3, d3f5d015f are on master | 76536812 re-check: defects closed, production sound. Not recorded as a Clean Final Review. | Windows CP-2, cgroup-v1 CP-12 (needs a v1 host or an operator waiver), and all positive controls. | KEEP-OPEN |
| CARD-1642 | Owed native checks for CARD-1609 | 87f4a868a86ce594d6dc9d1f1365315cb3f86df9; subject a08e4038baa82a3fbd02eb9179da78d0d8914205 | 761b0853 round 16, Clean, no blockers, at a08e4038b | Windows CP-11, CP-12, CP-13 and pending PCs, plus the delete-enabled qualification (CP-10 N2/N3, J6-J8, H6, final delete-path review). Apply stays off until that qualification finishes. | KEEP-OPEN |
| CARD-0972 | CARD-0954/0955 Windows native row and SourceLanding Mutation PCs | ad35ef95b06ddf1cb8ad35774e1d83171cb1f287 (same subject as reviewed 61e9071e, which is not itself on master) | d4447aac settled Succeeded. CARD-0954 close says PCs were not run (operator pause). | Unrun Windows half-reset row, method-scoped PCs, MUT-C survived, and the frame-cap usefulness note. | KEEP-OPEN |
| CARD-1013 | Native Windows confirmation of CARD-1001 | 7ec17bf9023eb2b7f36f84771144a844178a730c | a5c51cc2 on the parent | Verb works on Windows. CARD-0891 JSON digest does not match the Linux reference and the cause is unrecorded. Symlink and non-regular checks moved to CARD-1028. PC-1..PC-5 still pending. | KEEP-OPEN |
| CARD-0597 | Post-land verification: CARD-0490 | 723ac9534fc3fce49b378e287da08b7b11095716 | 8bd0813b was Interim (Clean-with-deferrals). Later parent review a10d7985 settled. | Operator hold: do not dispatch the 42 PCs without explicit confirmation. PC-12 plan target was unlanded when the hold was written. | KEEP-OPEN |
| CARD-0979 | 81 SourceLanding PCs and Windows V-5 after CARD-0939 | 8f85443046b23e6686ada97d7ae8707075b97da0 (same subject as f3c11e0a, which is not on master) | 2e000c83 Clean for CARD-0939 | Nine seam-dependent results that need new fixtures or a model change, Windows V-5, PC-1..PC-81, and a resolver guard whose mutant survived. | KEEP-OPEN |
| CARD-1078 | Deferred CARD-1029 matrix, Windows rows, PCs | bee608946 on master for the parent slice | Parent CARD-1029 closed | 114-vector matrix, F-families, Windows CP-2 and CP-18, 211 PCs. PC-274 is blocked by CARD-1038. | KEEP-OPEN |
| CARD-0894 | Eight positive controls cannot go red at their named labels | 1d994aac is not on master | e772aac0 found the gap | PC-5/7A/7B/8/9A/21/22/23 miss their named labels. That is a test defect, not an unrun battery. | KEEP-OPEN |
| CARD-0987 | CARD-0826 remaining S2-S5, native, PCs | b26c97658e712c53083e6777e5753bbf336011cc is the CARD-0826 record. Later CARD-0987 commits on master are S1a WIP and a ledger. | 87da3a46 of CARD-0826. CARD-0826 was later closed saying nothing remained on that card. | S2-S5, native adapters, and PC-1..PC-118 are still described as not done. The partial slice is unwired. | KEEP-OPEN |
| CARD-0812 | Per-repository runner mirrors | Later CARD-0812 commits are on master through a8b4e9e5a. Parked tip 318d3f0d is not an ancestor. | 1f3a0ecf settled before the park. No later Clean Final Review. | Windows CP-1, live acceptance, and the D-5 operator decision. | KEEP-OPEN |
| CARD-1233 | Inherited readers, PC-11, Windows qualification | Several CARD-1233 S2/S4/S8 commits are on master (for example ff2287125, 9e78a0888). | Not a single Clean Final of the remainder | Routing the inherited readers is still product work, plus PC-1..PC-11 and Windows R-2. | KEEP-OPEN |
| CARD-1033 | CARD-1017 S4/S5 and native qualification | S1 commits such as a1aa8c8d0 are on master | None for S4/S5 | Scheduler, Windows qualification, and CP-3-7 and CP-10-12 are still to implement. Execute stays off. | KEEP-OPEN |
| CARD-1038 | PC-274 fixture unbound | No CARD-1038 commit on master | None | Grok init-cleanup kills the recipient. Real fixture bug. Blocks PC-274. | KEEP-OPEN |
| CARD-0552 | Track Mutation as a pipeline stage | Plan docs only (7fc575c96, 0d7765c1b, 6bda48d3a) | None | The card is the tracking feature. It says the owed PC batteries are out of scope. | KEEP-OPEN |
| CARD-1732 | rc coverage follow-ups A-1..A-7 | 673cba20f is on master. Repair 7ed514f47 is not. | 8eb266f0 was clean before the follow-ups. The card says do not close. | A-2, A-3, A-5, A-6, A-7 open. CP-2 was 65 passed / 2 failed. PCs pending. Final gate incomplete. | KEEP-OPEN |
| CARD-1737 | S4a masked preservation fences | a7fa32102 and 4a686bc96 are on master | Reviews 1e6caa41, ebfed7ef, 6c9c0d27, a50818db settled. Not a clean close. | X-26 and other controls do not independently prove preservation. Native Windows evidence still owed. Plan required before more tests. | KEEP-OPEN |
| CARD-1717 | S4a audit B defects D-1..D-5 | Some later commits mention the card (987aa33da, 6f8fad460). | adbd81fe reproduced the defects. Interim scope. | Five reproduced deletion/switch defects. Repair, not a rerun. | KEEP-OPEN |
| CARD-1718 | S4a audit A defects A-D1..A-D5 | Report commit 7190d151c is on the review branch, not cited as master. | 03e7cd6d | Skip-worktree, case twins, ignored .env, silenced ls-files, and Windows ADS. Repair. | KEEP-OPEN |
| CARD-1653 | Pty-host socket hardening | Reviewed tip a5c9fdbb is not on master. Census pin 0ca1a0ffe is. | 2c8d00db S2 round 2 CLEAN at a5c9fdbb. Mutation 7dfff223 settled Succeeded. | S2 reviewed tip is unlanded. Description is still the original hardening ask. | KEEP-OPEN |
| CARD-1669 | Linux SessionRunner 123 reds | Slice A 897e65090 and g3 e72c27bdb are on master. Reviewed g3 tip 8ccf23633 is not. | e418768c CLEAN for g3 only. Mutation 2deb2419 settled Succeeded. | Groups (3) and (5) are not shown done. The clean review does not cover the whole card. | KEEP-OPEN |
| CARD-1662 | Per-sweep failure budget and cutoff witness | A-1 49a09ac9ddd3df076962cf84a36cb533a8942ee9 is on master. | 3501177a settled. No Clean text on the branch. Mutation b842c9bf settled Succeeded. | Card text still lists A-2, A-3, A-4, A-5 and PC-1..PC-5. | KEEP-OPEN |
| CARD-1668 | Stale-observation startup retry | a2bad29648af0412af6d18fbd0816096e928a17c | f0717652: no defects, ordinary scope Interim, not Final. Mutation 3af9f716 settled Succeeded. | Code is on master and mutation succeeded. The review that preceded it is Interim, so (b) is not met. | KEEP-OPEN |
| CARD-1698 | Host software-version warnings | No CARD-1698 feature tip was found as an ancestor beyond body mentions. | 2f58c570 settled, then mutation f2976027 succeeded, then Code 08a2e7f3 succeeded with no review after it. | Latest stage is unreviewed code. | KEEP-OPEN |
| CARD-1701 | S4a first-drain, backoff, ref spellings | Reviewed f3f92fc6 is not on master. | 189595a5 Clean at f3f92fc6, Interim scope. Mutation f303b3ec succeeded, then Code b3bd8985 with no review after it. | CP-25 and PC-1..PC-68 still named. Later code has no review. | KEEP-OPEN |
| CARD-0717 | Shared Polly resilience | 7e912b7bb and several earlier CARD-0717 commits are on master. | a74257d7 settled, then Mutation 81cb89f5 was dispatched on 2026-09-25 and has no settle line. It is not in the open-task list. | Original feature text is still the description. Not a verification-only wait. | KEEP-OPEN |
| CARD-1750 | Turn on per-slot worktrees at the next rollout | 57d0e3cb9c8f8021e1aad583a0599100fa3eda20 (flag passthrough, default false) | None for the turn-on | The card is the rollout and the desktop flag change. Both flags are still off. | KEEP-OPEN |
| CARD-0536 | 103 remaining CARD-0443 test targets | 6b2189863 is not on master | None | 103 targets unwritten, including Windows qualification, and 218 PCs never run. The ask is to finish them before land. | KEEP-OPEN |
| CARD-1460 | CARD-0105 Windows Playwright and missing touch tests | Not established | 447440a0 suggested the tests | CP-6 is owed, and the touch and single-guard tests are still unwritten. | KEEP-OPEN |
| CARD-1117 | Resume refusals have no negative controls | Not established | 74334872 disclosed the gap | Seven refusals need new red-first controls. | KEEP-OPEN |
| CARD-0943 | Autonomy pin self-compare block | Not established | 61b36860 disclosed it | Delete or replace the self-compare. A code/test edit, not an unrun PC. | KEEP-OPEN |
| CARD-0637 | Missing runner-reply tests | Not established | a24a626a reported the gaps | Three missing tests to add. | KEEP-OPEN |
| CARD-0602 | Real DockerStack fixture, CP-19-54 | CARD-0590 core landed. This follow-up has not. | bbf1854c | Implement Serve/Observe and run the deferred checkpoints. | KEEP-OPEN |
| CARD-1618 | Make 35 Windows-skipped checkpoint rows run | Plan tip 9d2ce60f8 is on feat/card-task-f8a94733, not cited as master | Three plan reviews | Implementation plan: lift runtime gates, port 10 POSIX fixtures. R-2 is an operator decision. | KEEP-OPEN |
| CARD-1770 | CARD-1731 evidence-path follow-ups | cf0aba324 reviewed; not checked as the master tip | 6c5b2084 CLEAN of CARD-1731 | A-1 error reporting is a code change. A-2 and A-3 are Windows witnesses. Do after CARD-1731 lands. | KEEP-OPEN |
| CARD-1568 | Outside-root containment witness | Parent landing 3c072ab97 | 4741bd04 of CARD-1538 | Add a failing witness and decide absent-root behaviour. Windows W-1..W-7 stay owed, and the witness itself is unwritten. | KEEP-OPEN |
| CARD-1786 | Load-telemetry retrospective | Investigation 5783827ce is on feat/card-task-4651491a | 4651491a Investigate | The retrospective is written. Prevention is CARD-1782, CARD-1783, CARD-1784, CARD-1785. Not a mutation or native-check wait. | KEEP-OPEN |
| CARD-0751 | HttpResilience tests flake under full-lane load | Not established | None | The text says the evidence does not close the card until the branch lands. | KEEP-OPEN |
| CARD-0513 | Land outcome receipts loop | A land SHA is quoted on master from 2026-09-13 | None | The master SHA is the observed duplicate, not a finished fix. | KEEP-OPEN |
| CARD-0318 | Cosmetic motion versus real progress | Not established | None | Duplicate-or-gap investigation. Not verification debt. | KEEP-OPEN |
| CARD-1294 | Auto-close advisory gaps | Not established | 2e17a0e2 advisory | Six follow-up gaps. Not verification debt. | KEEP-OPEN |

## Other Review and InProgress cards

These were read in full, including revision history. None is waiting only on mutation or an owed native check. Verdict KEEP-OPEN. An in-flight task is named when one was open at the last task read.

| Card | Title | Verdict |
|---|---|---|
| CARD-0038 | Linux and Docker readiness epic: Unix PTY and self-contained Docker core shipped; remaining parity gaps tracked by linked cards (split 2026- | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0121 | CARD-0103 S3: prove collapsed-paste composer clear before any re-type | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0167 | API key + LLM proxy as first-class per-agent-kind setup (Claude/Grok/Codex env-var and arg asymmetry) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0416 | Prove the Grok rules refresh fires across two genuine auto-compactions (split from CARD-0395) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0465 | TerminalScreen: ESC M (Reverse Index) doesn't clear the pending-wrap flag like other cursor moves do | KEEP-OPEN: in-flight Review 44145bf7 |
| CARD-0500 | Interactive Codex submit-confirm flake, pre-existing, likely CARD-0108/0055 family | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0507 | Pluggable custom agents per stage (e.g. open-code-review for Review) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0511 | Restart loop: SessionRunner/Server capability desync after sessionGenerationV1 (2026-09-13, distinct from CARD-0509) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0512 | Investigated: a Bash tool_use with no tool_result wedged a session; the 120s harness timeout failed to fire (2026-09-13) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0529 | OutputDistillationGate anchor bugs: URL trailing-backtick and 2-char path fragment cause false RejectedOverCompressed | KEEP-OPEN: in-flight Review 5137cbf6 |
| CARD-0571 | eslint does not ignore client/storybook-static, adding noise to the lint gate when it's built | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0599 | Release-gate model: lighter per-change tests + periodic full-test RC releases | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0608 | Harden PosixProcessSpawner.CloseStdioAliases: restrict match to pipe:/socket: identities | KEEP-OPEN: in-flight Code 3838725c |
| CARD-0628 | Get Claude Code CLI authenticated and running on server2 (Opus 5.5 agents dispatchable there) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0644 | Worktree is the default: dispatch defaults to -Worktree (Shared opt-in); deploy/restart scripts allow worktrees at origin/master without ove | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0655 | server2: recurring phone-home HTTP 502 reconnects; deploy evidence not retained on server2 | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0667 | Runner seat not released when a runner-bound task settles Failed (only Succeeded releases); orphan sweep misses terminal-task sessions | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0669 | Land residue accumulates: source-recheck refs, refs/antiphon/land pins, land-verify temp artifacts never collected | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0692 | Post-land Cleanup stage: retire all task worktrees, branches, runner worktrees and sessions for a landed card | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0705 | Enable pg_stat_statements on the desktop Postgres (compose preload + idempotent extension + docs) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0707 | A Blocked task from another project (worktree gone) holds every Antiphon land as a repository/source writer | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0817 | server2 runner: push credential for non-Antiphon repositories | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0824 | Per-slot reusable worktrees instead of one worktree per task; delete remote branch after land | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0856 | Full Antiphon.Tests run is 270 min on Windows: effective parallelism 1.4x, Integration DB and git tests are 96% of the time; Herdr is 3% | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0886 | Speed up CARD-0835's two heaviest tests (table-driven landing harness, shared script fixtures) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-0931 | Flaky-fix plan CP-3/CP-15 method-OR checkpoint filters select zero tests on Linux (missing trailing * per operand) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1010 | Rolling recycle opt-ins -RecycleRunnerState / -RecycleCaches (D-6 maintenance proofs, lease-expiry contract) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1017 | Delete completed-card task worktrees including ignored evidence (CARD-1015 follow-up) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1022 | Deprecate and remove the legacy inbox ConPTY backend (keep ModernConPty only) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1023 | Compatibility matrix (CLI version x model x session-runner/pty-host/backend x host) replaces version-floor admission gating | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1030 | C1008 host-fixture tests fail on Windows: bash launcher path, git init argv, flock sentinel (12 tests, inherited) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1039 | Daily whole-Unit run: activate and verify the existing Windmill nightly | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1040 | Unit timeouts/inherited failures: resolve CARD-1021 jq preconditions and measure limits | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1068 | Fixture gate smokes the old 0744 runner image and leaves cleanup residue | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1080 | Invariant test: no task outside Dispatched/Working keeps a runner seat beyond a grace | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1081 | A Blocked task has no clock, and the attention feed reads every sync block as a human question | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1109 | CARD-1082 residual: Code task whose commits were never fetched during the lease budget still ends Blocked | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1148 | Bind Held sync debt to a worktree registration id before superseding | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1149 | Refused launch with no runner process fails the unattempted task | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1217 | Checkpoint receipts cannot validate rows with declared CONDITIONAL skips (blocks CARD-1210 S3 land on a no-symlink-privilege host) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1230 | C1017_TerminalTaskRequired fails deterministically on master (cleanup not clean after task reset to Succeeded) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1246 | Full client suite is load-sensitive: a different agents/hosts/board test fails each run on server2 (CP-3) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1279 | SettingsPage.test.tsx: two routing-tab fetch-count tests fail at master (expected 15 to be 1) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1281 | Pipeline speed: release-train model, minimal per-card verification | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1287 | Full Unit lane never completes on Linux: run on Windows, attribute failures | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1305 | RC 2026-10-15 environment and lane-placement defects D-2..D-6 (one section each) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1323 | RC lane placement: platform-scoped exclusions or Linux rc profile for the 69 LandDelivery cases; D-3 and D-5 leftovers | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1327 | Checkpoint output cleanup spawns one git per deleted entry | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1334 | GrokDelegateEndToEndTests harness registers SessionRuntimeActionCoordinator but never runs its workers; pump-driven settlement witnesses sta | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1347 | Scheduled dry-run-first sweep of old Codex pool-session history on server2 | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1348 | Test/tooling isolation: lease build slot only around build, per-test temp for Grok pin test, double dead observation before sweep | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1351 | Output-distiller prompt review: contract v5 checks length first and stops hoarding non-gate identifiers | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1352 | Seed-smoke pack pin leaves SDK 10.0.401 check and 9.0.20 path literals untied | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1354 | redeploy-old refused RecycleWorktreeDirty on a merge task's nested moving-tip scratch checkout | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1368 | Checkpoint cleanup refuses runtime-created TestResults under the row output (output-ledger-mismatch) and invalidates green receipts | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1376 | ChromiumRuntimeSmokeTests: thread the TUnit cancellation token into Playwright.CreateAsync (TUnit0015; CARD-1323 S4 follow-up) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1378 | Desktop API ingress: Caddy 502s from 3 s upstream connect timeouts to :17203 under task-detail read bursts (killed a recycle census) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1379 | Land worker holds the repository lease through land-verify builds and starves dispatch; Held lands not replayed in order after a restart | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1401 | Move the main Postgres to server2 Docker and take daily snapshots off-host (OneDrive) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1406 | server2 host Postgres service and scratch restore drill | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1439 | CARD-1421 follow-up F-3: 14 limited test files with no spawn found may not need a process limiter | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1466 | Inherited census reds: TestSchemaSnapshotTests registry, shape_conflict_rule negative qualification, C1155 roster, C1382 load race | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1470 | CARD-1461 option C review follow-ups: record the fallback on the land, bound the CPU count, missing witnesses | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1477 | Daily CLI updater force-kills active desktop Claude sessions at 08:00 | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1478 | Hold new desktop worker launches below a fresh memory floor | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1510 | Windows PATH probe test rejects the PowerShell launcher alias prefix | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1517 | Claude refinement needs one conditional Ctrl+C without killing the session | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1518 | Classify permission-blocked tool calls before the local-execution stall deadline | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1526 | Process: OS-gated code needs a real-type witness and a native smoke before rc picks that touch the AppHost restart path (CARD-1506 / CARD-15 | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1527 | AppHost watchdog skips dead launches behind fresh restart locks and never alerts on prolonged downtime | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1528 | CARD-1483 follow-up: canary misses System.IO.File.Exists property functions and metadata-only reads | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1532 | Checkpoint source stamps reject build DLLs above the 64 MiB receipt limit | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1545 | Scoped rc qualification: per-app batches and project-graph planner including downstream dependents (shadow-first) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1548 | Update action-owning retention and purge fixtures for CARD-1259 dependencies | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1553 | Repair C1115 tests whose nested lease-busy assumption changed with CARD-1430 | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1556 | C1275 matrix byte-bound witness counts Windows CR in the rendered line | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1570 | Checkpoint receipt: one red row invalidates every green row (still-present refusal) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1573 | Checkpoint output-inventory reader refuses Antiphon.Tests.dll above 64 MiB | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1574 | Standing resume kills accepted restart notes during transcript lag | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1581 | Same-card dispatch waiting for a land holds the repository lease; the land yields to it (lease cycle starves lands) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1589 | Cache preflight F-9 rejects supported absent offline temp deployment hold | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1591 | Dialog guard withholds all notices to an idle adopted orchestrator session (ObservationUnknown, 340 pending) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1593 | Capacity admission consumed before stale-claim rollback and during remote prep: redo with a lock-order design | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1594 | retire-temp dry-run preview refuses (receipt binding); two fix attempts failed Final Review | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1596 | Older path-based deletes (SafeDelete, ToolCopyAuthority, TempRootSweep) keep the parent-swap race | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1597 | Qualification timing recorder 256 KiB cap refuses most real chunks | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1598 | Timing reader cost still grows with journals in the shared folder (about 200 hosts fit in 200 ms) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1599 | Checkpoint receipt reader read-stalled 3 s window fails on a loaded host | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1600 | Quota and wall-reroute supersede take the provider lock before the task lock | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1601 | Receipt reader stall window measures thread-pool starvation (sync-over-async on Linux) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1608 | run-checkpoint.ps1 git output decoded with the Windows console code page: non-ASCII paths escape content-stamp tamper checks | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1613 | rc policy exclusion witness: replace the source lexer with a reflection check on a pinned skip attribute | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1614 | PtyHost tests: 10 unowned Linux reds (cgroup custody, launcher apphost, inbox) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1615 | Rollout Deploy task (Shared workspace) holds bound lands as a source writer, deadlocking redeploy-old RecycleLandInFlight | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1616 | Checkpoint tool: admission-timeout receipt defect and 814 s Windows finalization | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1623 | Land verification builds contend with the Windows qualification run | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1624 | SessionRunner CPU: unbound TranscriptTailer rescans projects root every 250ms; watchdog reclassifies full transcripts | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1625 | Land verification 3x slower: CARD-1506 build policy applied to land build | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1629 | Incremental builds: stable per-worktree output, then checkpoint build reuse | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1636 | Non-custody adoption does not verify the host session id (local exposure) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1638 | Test timing: budget headroom, planner cost refit, cross-run timing history | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1639 | CARD-1380 successor: equal-tree receipt land skip and lease doc drift | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1641 | Review advisories from the 2026-11-01 live-run repairs (tree-kill, reconciliation, harness, zone) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1655 | delegate.ps1 bind-400 diagnosis test fails on Linux: error text wraps | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1657 | delegate.ps1 land: transport failure after POST reports Queued and exits 0 (C495 red) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1658 | CARD-1654 review advisories: per-batch outbound ids, bounded later passes | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1659 | Unit-lane test hygiene on Linux: host-proof fixture, sudo probes, Windows-only gates | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1690 | Client test performance: slow Mantine tests, RoutingSettingsTab 250 s, setup guard | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1693 | Auto retire-temp: census scoped to the temp runner, proof snapshot retry, no asking | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1695 | Standing always-on orchestrator on the server2 runner, in its own checkout (slides-server2) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1699 | CARD-0824 S1c follow-ups: C0824 server test 35 s limit flakes; amendment text still says array | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1703 | E2E land verifier heartbeat follow-ups: Windows p95, wiring witness, confirmation | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1704 | LibGit2Sharp git abstraction with a shared repository for the whole land | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1708 | S4a release can destroy reflog-only commits (decision needed) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1715 | status_untrusted is opaque and invisible; two land sites mislabel it | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1723 | CARD-1591 S1 advisories: wake worker non-shutdown cancellation stops the whole host, scan page starvation, overload warnings | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1726 | Release-candidate red 2026-11-09: Antiphon.Tests exit 8, Antiphon.Tests exit 8, Antiphon.Tests exit 8, Antiphon.Tests exit 2, Antiphon.Sessi | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1728 | Debug Unit shard completion, coverage indexing cost and stop receipt | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1731 | Publication ledger tests (ExecutionLedger, Joins, Markers) sit at their 300 s timeout under load | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1733 | ReleaseTagAllocator reservation matrices exhaust 300s; full affected-class row cannot finish | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1744 | Cut the production control plane over from desktop AppHost to the server2 server | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1746 | Place the messaging broker and enable ChannelBridge for the Linux server | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1751 | Probe the existing server2 broker from the staging server without joining its group | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1757 | SessionRunner CPU: HostStatsSampler takes a whole-system process snapshot per sampled process every 5 s (10-20% of runner CPU) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1768 | CARD-1743 follow-ups: Hangfire Postgres lock lifecycle, error reporting, dependency doc, C459 flake | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1773 | CARD-1769 follow-ups: park-map leak, injected clock, Canceled watchdog predicate, stamp query cost | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1774 | CARD-1768 follow-ups: tick retry before stopping the host, own-backend false conflict, doc bounds | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1775 | Production Antiphon server compose on the server2 host with a session-runner sidecar (CARD-1744 S1) [linux,docker,deployment] | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1776 | Linux server land credential and project repository mirrors for the server2 control plane (CARD-1744 S2) [linux,docker,deployment] | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1777 | deploy-server2.ps1: require ANTIPHON_API and document the operator token after the control-plane move (CARD-1744 S3) [docker,deployment] | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1778 | Re-home always-on agents and specialists to the Linux server sidecar at cutover (CARD-1744 S4) [linux,docker,deployment,orchestrator] | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1784 | Linux post-land early warning: run Unit shards and impacted Integration classes on server2 after each land batch, not only at rc time | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1800 | Telegram: deliver files up to 20 MB through an out-of-band reference instead of inline base64 (slice 2) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1818 | Advisories: gc owner robustness and wiring tests (CARD-1817), stop-supervisor details (CARD-1507) | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1820 | Selector v2: derived reverse-reference index, keyword triggers, roster registry, what-ran receipt | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1823 | Cumulative failure ledger for rc seeds: every ever-failed chunk runs first until proven | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1828 | Coverage roster advisories: tolerated list in summary, reason census, drift, loose match | KEEP-OPEN: in-flight Review 4a65b072 |
| CARD-1831 | Daily database backup via Windmill with a Telegram status line | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1833 | Human review questions: the operator decision queue | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1834 | CARD-1825 advisories: root-normalising AllowedCwd, Windows case alias, lstat TOCTOU | KEEP-OPEN: open product, defect, or advisory work |
| CARD-1835 | CARD-1431 advisories: tie-break, class= parse, queue reasons doc, UI checks | KEEP-OPEN: open product, defect, or advisory work |

## Backlog

All 272 Backlog descriptions were read. The ones whose text says the work is done, or that only a measurement, mutation battery, or native rerun remains, are in the tables above. The other Backlog cards are open defects or unfinished features.

## In-flight tasks

- CARD-0608 Code 3838725c
- CARD-0465 Review 44145bf7
- CARD-0529 Review 5137cbf6
- CARD-1828 Review 4a65b072
- CARD-1514 Investigate 714aa9bf (this audit), Merge f077518c, Custom e0c86bd2 queued, Debug 7340be2f queued

Not done, noted: cards were not closed from this checkout.
