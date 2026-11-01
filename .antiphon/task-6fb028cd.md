# CARD-1545 combined fix round 2 (task 6fb028cd)

Outcome: D-1 and D-2 are on `5e3dcc4c03420637236e67be7bdc9de2cf70b36c`. Every commissioned portable row on that commit is green. The land-graph `-Check` refuses on the digest alone. The evaluator, run directly, prints `phase=pass2` and `approved` (exit 0). `-Update` was not run.

Code tip: `5e3dcc4c03420637236e67be7bdc9de2cf70b36c` on `feat/card-task-6fb028cd`. Working tree was clean for every receipt below. Slot wait was 0s on every row.

## Status lines

- D-1: census seed is master `bf0b874e695a642118e2dd55913228e2f635d009`; 32 additive methods; exactly six LiveClaude additions required. Pushed `512cd4fcddb26184ec6e0fcaf89be897f30a57be` (seed merge `d5222fae7b58adcf22f811c96a36d57639f2415e`).
- D-2: the sink audit now catches the ordinary shapes the old walk missed. Pushed `5e3dcc4c03420637236e67be7bdc9de2cf70b36c`.

## History

| Commit | What |
|---|---|
| `d77ac25be1f994f433e5323d8bf62979517ca791` | Merge of then-current `origin/master` `bf0b874e695a642118e2dd55913228e2f635d009`. No conflicts. Policy hash left at `9cd8d7281b04ee66287f1c2bf5104d1e385937cd4e34881032f9fbca3d438eea`. `_PtyHostNativeProps` is absent. |
| `49b77d40bdfa42b7c36709e8f8e6c42caea1f17a` | Master-parented fixture seed. Ancestor of HEAD (`git merge-base --is-ancestor`). |
| `d5222fae7b58adcf22f811c96a36d57639f2415e` | Merge of that seed. Tagged `tests/live-lanes.json` kept; seed fixture kept. |
| `512cd4fcddb26184ec6e0fcaf89be897f30a57be` | D-1 pin plus the 32-method additive contract. |
| `5e3dcc4c03420637236e67be7bdc9de2cf70b36c` | D-2 audit. Code tip for every receipt below. |

Seed side history, not the task tip: `bf0b874e` -> `bd4938dd343c6353700356d23228b2bbcc288119` -> `808de327f593874469a8e84597ccb154f4994272` -> `49b77d40bdfa42b7c36709e8f8e6c42caea1f17a`. Fixture blob `c9581d74b2139326605c93829d924f86af753379`, no BOM. `producing.sourceSha=808de327f593874469a8e84597ccb154f4994272`. Projects: Antiphon.Tests CP-27 `17670be33894119b63bb9595ad835be286e5671187cf752fea3572ae1cf47bf3`; Antiphon.Agents.Pty.Tests CP-28 `a57ad50db951578c19362a800378ec94c1e7b57cfcd9def76162f4e435023ecd`. Pin file `tests/fixtures/live-lanes/pre-s1-base.sha` is master `bf0b874e695a642118e2dd55913228e2f635d009` and is not on the seed allow-list.

## What CP-30 proves

Run `20261101-041821-3e8a`, command row, exit 0, executed n/a, wall 2m08s, executed timing 126.9s, `buildSource=notApplicable`, source state clean, evidence `.antiphon/live-lane-runs/31b63cc3360a437481b285469ed52853/20261101-041821-3e8a/report.md`.

It proves a full census of this candidate against the true pre-S1 master discovery (fixture produced at `808de327f`, seed `49b77d40` an ancestor, pin and script constant both `bf0b874e`), with removed UIDs 0, exactly six observed LiveClaude additions on existing UIDs, and the declared 32 additive Unit methods. `FullCensusUnchanged` returns 0 only after that census is accepted and the in-suite witnesses pass. A self-seed, or an engine or pin edit inside the seed range, cannot hide drift: those are `self-seed` or `seed-engine`.

Direct `LiveLaneCensusEngine.Compare` before that receipt, master fixture vs combined candidate, both empty exclusions and the real linux exclusion: happy path empty; dropping one LiveClaude tag is `liveclaude-count` only; a seventh tag is `category-delta,liveclaude-count`; an unknown node is `undeclared-new-uid,nightly-disposition-changed` (not `liveclaude-count`); candidate vs itself is `nightly-disposition-changed,liveclaude-count` because the additive filter removes the 32 methods only from the after set.

Counts: before 21579, after 21611, added 32, removed 0. The six additions are existing UIDs that gain LiveClaude and nothing else: C680 `ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs`; `ClaudeSubmitContractLiveTests.Submitting_via_two_writes_completes_a_turn(System.String)`; three `ClaudeRemoteControlAtStartupCanaryTests` methods (`Control_auto_connects_a_bridge_session_before_the_first_user`, `Settings_file_prevents_auto_connect_and_keeps_startup_records`, `Remote_control_slash_command_still_arms_after_settings_off`); `ClaudeRemoteControlMenuCanaryTests.C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt`. The 32 new methods are Unit, empty arguments, no LiveClaude.

## What CP-30 does not prove

Later policy or reader edits after this commission are not a permanent Unit test. Assembly hashes are path-dependent. Producer receipts and exports are unauthenticated JSON (advisory A-1). The token filter still admits values inside the 32-character threshold (A-2). The audit cannot see reflection, `dynamic`, a helper in another file that is not reached by name in the same type, or string mutation that does not flow through the named taint rules. Derived Shouldly subjects are text-preserving leaves (`Split`, `Trim`, `Replace`, `ToArray`, and the same family) plus arrays, collection expressions, and element access. Bool predicates such as `Contains` on a tainted string are not treated as Shouldly-actual leaks.

## Checkpoint receipts

All rows below are source `5e3dcc4c03420637236e67be7bdc9de2cf70b36c`, state clean, slot waited 0s. Failed is 0 unless noted.

| Row | Run | Result |
|---|---|---|
| CP-25 | `20261101-040349-eb43` | 1 executed, 1 passed. build 41.7s, testsWall 41.2s, wall 2m30s. `buildSource=verified`. |
| CP-1 | `20261101-040625-4f69` | 2/2. build 413.2s, testsWall 165.9s, wall 11m46s. `buildSource=verified`. |
| CP-30 | `20261101-041821-3e8a` | command exit 0. See above. |
| CP-27 | `20261101-042123-f64d` | 1/1 ExactCoreRoster. build 409.5s, testsWall 116.4s. Task tree, tagged manifest. |
| CP-28 | `20261101-042123-f64d` | 1/1 ExactPtyCoreRoster. build 73.4s, testsWall 21.5s. Wall for the pair 14m23s. `buildSource=verified`. |
| CP-20 | `20261101-043611-ce4e` | 2/2 `LiveClaudeEvidenceWiringTests`. testsWall 14.9s, build 60.8s. |
| CP-26 | `20261101-043611-ce4e` | 2/2 `LiveLaneEvidenceTests`. testsWall 135.8s, build 277.1s. Pair wall 9m59s. |
| CP-29 | `20261101-044618-df64` | command exit 0, wall 0m03s. `EVIDENCE-SCAN clean`, `publicationCount=1`. |
| CP-16 | `20261101-044931-2a17` | 6/6 `LiveLaneOutcomeTests`. testsWall 54.5s, build 357.7s, wall 9m38s. |
| CP-17 | `20261101-045917-cf82` | 3/3 `LiveClaudeExecutableWiringTests`. build 160.2s, testsWall 0.8s. |
| CP-23 | `20261101-045917-cf82` | 4/4 `LiveLaneExecutableTests` (s1c executable row, not the s1b runner rerun). build 478.0s, testsWall 2.1s. Pair wall 17m42s. |
| CP-18 | `20261101-051711-3e32` | 6/6 `LiveClaudeIsolationWiringTests`. build 54.7s, wall 2m10s. |
| CP-24 | `20261101-051931-d8fc` | 4/4 `LiveLaneIsolationTests`. build 238.9s. |
| CP-24-PARITY | `20261101-051931-d8fc` | 1/1 `StartupArgvMatchesProductionApplyOff`, build reused. Pair wall 4m45s. Producer receipt `20261101-051711-3e32`. |
| CP-19 | `20261101-052424-d356` | 4/4 `LiveClaudeDeliveryWiringTests`. build 55.0s, wall 1m36s. |
| PlanCoverageBindingTests (hand row CP-31) | `20261101-052607-348c` | 7/7. testsWall 135.0s, build 299.9s. One shared Antiphon.Tests build. |
| RcRosterRepairTests (hand row CP-32) | `20261101-052607-348c` | 3/3, build reused. Pair wall 9m35s. |

Seed capture that produced the fixture, on `808de327f593874469a8e84597ccb154f4994272`, run `20261101-033750-7d0e`: CP-27 1/1, CP-28 1/1, exit 0, wall 12m40s, `buildSource=verified`. Earlier red `20261101-031830-4c09` was the expected untagged-category mismatch.

Census scripts, `TEMP=/tmp`, each exit 0: C1404 launches=8 nodes=158 missing=0; C1584 required=176 excluded=7 launches=75; C1577 nodes=148 launches=6; G2865 g28chunks=5 g65chunks=6 launches=11; C1410 launches=154 suiteHours=29.54 required=19120 missing=0 overTarget=0. A first run without `TEMP` failed four of them in harness setup; the `TEMP=/tmp` rerun is the green evidence. G2865 had already passed.

## D-2 mutants

`CanarySinksCannotBypassSanitizer` calls `ProveCallerMutants`. CP-20 passing means each injected shape is caught. Shape ids in that method: SM-Alias, SM-AliasOut, SM-Group, SM-GroupArg, SM-Cond, SM-CondW, SM-Create, SM-Move, SM-Lines, SM-Dir, SM-Stream, SM-When, SM-Unless, SM-Sb, SM-Hold, SM-Box, SM-Pair, SM-Split, SM-Arr, SM-Fast, SM-Dbg, SM-Using, SM-UAlias, plus the earlier caller ids SM-S1, SM-S2, SM-S2b, SM-S1a, SM-S1w, SM-S2w, SM-Out, SM-Err, SM-Fmt, SM-File, SM-Proc, SM-Ctx. The red-first run of the old audit failed at `C1545 SM-Alias did not reach expected assertion` after the prior mutants stayed red. `EvidenceIsSanitized` still runs PC-33 through PC-37 and the closed-token checks; that test is the other CP-20 pass.

The audit still cannot see reflection, `dynamic`, cross-file helpers, or arbitrary mutation outside the named taint rules. Directory.CreateDirectory is a carry (`IsFileCarry`), not a safe emit. There is no Other==0 gate.

## Not run, named, not chased

- s1b CP-2 / s1b CP-23 (`LiveLaneRunnerTests`, including inherited `WrapperOnlyPaidManifest`). The executable CP-23 above is the s1c row.
- `NativeLinuxPolicyWitnessTests` `8397984d` in Pty, SessionRunner, and PtyHost. Pin text is still `8397984d90368a0ef1cbc5991610ae50328165b40ac2503559ae0562d72b554d` at `tests/Shared/NativeLinuxPolicyWitness.cs:46`. Those three suites were not executed this round. The policy file was not edited after the merge, whose hash matched `bf0b874e`.
- `Custody_residue_counts_subdirectories_only`; load flakes that pass alone; `Exit_while_detached_survives_adoption`; intermittent `Native_exit_reaches_database_and_api`.
- Native CP-4, CP-5, CP-6, CP-7, and CP-21 stay owed. No native run, no live Claude.

## Land graph

`-Check` on the final tree (`scripts/audit-land-verification-graph.ps1 -Check`) exit 1:

- live digest `D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE` count 69
- recorded manifest `E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806` count 69
- message: `manifest does not match the selected graph`, then `audit refused: land graph pre-check refused`

Same count, different digest. The only definition-file diff against merged master `bf0b874e` (and against current `origin/master`) is `tests/Antiphon.Tests/Antiphon.Tests.csproj` and `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj`: each adds the `Microsoft.CodeAnalysis.CSharp` 5.9.0 package reference and the shared LiveClaude `Compile` links (40 insertions, no deletions). `_PtyHostNativeProps` is not in the tree. The allow-list was not widened.

Direct evaluator, no `-Update`: `tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/Antiphon.LandGraphAudit.dll --repo <worktree> --solution Antiphon.sln`. Budget ceiling 155s. Result `phase=pass2`, `approved`, exit 0. The canary directory was removed by the tool.

## Evidence and merge-tree

`git rev-parse --is-shallow-repository` is `false`. This mirror did not take the shallow exit.

`origin/master` moved after step 0. Fetched tip is `d7fdee50be88e8e757d06542969b425a1b492b1f`. It is not an ancestor of HEAD, so `check-evidence-diff` with `-BaseRef origin/master` exits 2, `base_not_ancestor`. That is not a content violation.

The ancestor range is the merge-base `bf0b874e695a642118e2dd55913228e2f635d009`..HEAD: `check-evidence-diff` exit 0, commits=109, entries=28, violations=0.

`git merge-tree --write-tree origin/master HEAD` exit 0, tree `4145ea5fdede7670a0dc1dfadda4f42bc678557b`. `--name-only` printed that tree and no conflict paths. Current master was not merged into the task branch. `-Update` was not run. The Merge records the digest once.

## Standing authority

"Keep fixing and make sure 5-7 tasks always on server2" covered finishing the rows and the land-graph check without a pause.
