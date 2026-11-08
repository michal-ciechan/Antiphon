# Post-land retrospective: why the rc regressions were not caught in review

Investigate task 4651491a (label `post-land-retrospective`), 2026-11-08. Operator question (2026-11-08): "Can we
send an agent to thoroughly investigate why it broke? Why wasn't it caught in review?"

Base master `b1e129b44855a4c6682a3b1fecc8fb445be7e11a`. All times UTC. Evidence sources: `git log` on
`origin/master` and `origin/rc/2026-11-08..10`, the agent-task API rows (`GET /api/agent-tasks/<id>`: briefs,
reports, events), the review reports on their task branches, the board (`scripts/card.ps1`), and the repo
docs. The desktop run directories (`C:\Antiphon\releases\candidates\...\logs\...`) are **not mounted** on
this runner, so Windows chunk counts and durations come from the run-start task reports (36d12cfa,
59e9866b, 3ca79566, 2271e6aa), not from the TRX files.

Cards filed from this retrospective: CARD-1782 (impacted-classes selector, High), CARD-1783 (Merge/cut
high-radius subset), CARD-1784 (Linux post-land early warning), CARD-1785 (migration review brief and docs
rule), CARD-1786 (this retrospective's record).

## Summary

- Every regression that the Windows qualification runs found in reviewed, landed work lived in a test that
  the per-task selection ("touched classes + guard batch") never runs. Reviews were honest about it: 42 of
  the 68 reviews in the 2026-11-06..08 window reported `ordinaryScopeCompleted: Interim`, and 59 of 68
  reports say the whole Unit lane stayed owed.
- The biggest miss, the load telemetry S1 migrations, was not a Unit-lane problem at all. All 52 failing
  tests are `[Category("Integration")]`, a lane that only the nightly backstop and the Windows rc run
  execute. The Unit lane leaving per-task Final (CARD-1728, 2026-11-07 12:15) changed nothing for them.
- The mechanism was known inside the S1 Code stage on 2026-11-07 07:37 for one class
  (`C0824RefOnlySnapshotMigrationTests`); the completion plan documents it verbatim. Nobody generalised the
  search to the other 50 files that rewind a database the same way. No Plan, TestDesign or Review brief
  asked for the blast radius.
- Every non-Windows-only regression reproduced on Linux at the batch tip in seconds to minutes. The first
  machine to run those tests was the Windows Full run, 6 to 20 hours after the land.

## 1. Each failure class: introducing commit, approving reviews, why the review did not run the test

Review selection in every row below was the stage-review SCOPE rule (`server/Bundles/stage-review.md:2`:
"Re-run the claimed rows and named class checks in one checkpoint-tool run") plus the guard batch (policy
sweep, `MechanicalPcContractTests`, `TaskPlatformGuidanceTests`, `C1729LayeringGuardTests`). Briefs for
every review in the window carried "NO whole Unit lane: CARD-1720/1728 and this host cannot complete it; the
operator accepts Interim for master-only work" (quoted from 8da334c5, fd0ebdc3, da7fff71, 23c76670,
61cede6f, 6c9c0d27).

| # | Failure class (first seen) | Introducing commit on master | Card / stage | Reviews that approved it (round, scope, what they ran) | Lane of the failing test | Why the review did not run it |
|---|---|---|---|---|---|---|
| 1 | **Migration `Down` blast radius**: 52 tests in 37 files throw `NotSupportedException: Retention catch-up state cannot be discarded by a downgrade` (C467_V17 on master 2026-11-07 23:46; StandingSessionOwnershipTests x3 group-88 and CardFilePrivacyMigrationTests group-86 on run e737e9fe 2026-11-08 02:03; the rest by Debug sweep 5a1ec910) | `ff7cceaf4f` HostLoadTelemetryS1 (03:45), `4ab2687899` HostLoadSnapshotMembership (04:04), `c53a0fe802` HostLoadRetentionCatchup (10:43), all 2026-11-07, landed in batch 118 `9e7ff3498` (23:07) | CARD-1514 load telemetry S1: Plan a653d3f6, TestDesign 1ddc4796, Code 924f8ae2 (Failed, 210 min), 7e3e6bcc, repair 2b953d03 (Grok, USD 254) | 8da334c5 r1 Codex Frontier, **Interim**, six blockers; CP-1..7, CP-M1 (`C1514HostLoadMigrationTests` 6/6), CP-M2 (`C0824RefOnlySnapshotMigrationTests` 4/4), CP-G 76, and a whole-Unit row killed at 45 min with no TRX. d56a0975 r2 Claude Frontier, **Interim**, "Down throws like the earlier S1 pair"; CP-3 = `C1514HostLoadMigrationTests* \| C0824RefOnlySnapshotMigrationTests*` 12/12. a28cac94 r3 Claude Frontier, **Interim**, clean; CP-7 only (diff-only round). c7a30141 integration Claude Frontier, **Interim**, clean; CP-1/2/3/7/M1, "all Down throw, snapshot delta 312 insertions". Merge 116 (73b64ea8) excluded S1 on a conflict; Merge 118 (e6423203) ran 183/184 + migration classes, "Three additive migrations, Down throws". | **Integration** (`[Category("Integration")]`, many also `Slow`) | The briefs asked "migrations additive ... including Down" and every reviewer confirmed the Down throws; none asked which existing tests rewind through it. The two migration classes in CP-M1/M2/CP-3 are the new migration's own tests plus the one class the Code stage had already repaired. The Integration lane is never part of per-task Final. |
| 2 | **Stale contract snapshot `home-tasks.json`** (e2e `ContractSnapshotTests.Home_tasks_rail_contract`, run 8e938b6d e2e group-1, Debug 7f1c768f 2026-11-07 16:01) | `f194ff221` 2026-11-06 03:17 "separate RefOnly execution, source and park custody" adds `DesktopWorkspaceKind` to the home task DTOs | CARD-0824 S4a integration (Code 5ec136af, Merge fee547cb "Land the S4a stack" 07:49) | d8af9f97 r1 Codex Frontier **Interim**; e830e940 r2 **Interim** (diff only, B-1 witness). Neither brief names `ContractSnapshotTests`. | **E2E `OptIn`** (tests/Antiphon.E2E, needs the server boot) | E2E contract snapshots are run only by the nightly/rc e2e lane; no per-task rule maps a DTO edit to them. Fixed 2f8bae1a2 (Merge 114, 2026-11-07 17:07); on rc/2026-11-10 only. |
| 3 | **Stale contract snapshot `pipeline.json`** (`Pipeline_status_contract`, run e737e9fe e2e group-1, Debug 86db5737 2026-11-08 02:05) | `ebd31b2498` 2026-11-07 03:58 "wire durable ingest, rollups, task observations and read-only load surfaces" adds `load: null` to `/api/agent-tasks/pipeline` hosts and runners | CARD-1514 S1 (same chain as row 1) | Same four reviews as row 1; the S1 TestDesign row "Pipeline DTO/catalogue projection ... E4 owns regression" (`testdesign.md:767`) binds `HostStatsAntiphonCountersTests`, not the E2E snapshot. | **E2E `OptIn`** | As row 2. Fixed 573a1a7f2 / master `7e7f5a9663` (2026-11-08 02:35, batch 121); not on any rc. |
| 4a | **`TestTimingOperationLabelTests.C1620_EveryCallSiteOperationLabelIsKept`, first time**: labels `raw-reflog-fault`, `source-snapshot` dropped (run c3f65ddb group-131, Debug c59da059 2026-11-07 22:34) | `dcb973d896` 2026-11-06 16:41 "add R2 salvage source preservation witnesses" adds two `BeginPhase` literals in `C1708ReflogSalvageTests.cs` | CARD-1737 R2 (Code edd7cc0a, Merge batch 99 22:49) | 6c9c0d27 r1 Codex Frontier **Interim**, B-1 "Required Final Unit verification remains incomplete"; ebfed7ef r2 **Interim**; 1e6caa41 r3 **Interim** (blockers only). | **Unit** (2 tests, seconds) | The guard is in the Unit lane; the Unit lane was deferred in all three rounds, with the deferral written down as B-1. On 2026-11-06 the Unit lane was still nominally in Final but could not complete on server2 (CARD-1720/1728). Fixed a7fa32102c (batch 119, 2026-11-07 23:44). |
| 4b | **Same test, second time**: `historical migration database`, `host-load API read fixtures`, `host-load probe initialization` dropped (found on master by Debug 114129b4 2026-11-07 23:46) | S1 call sites (`MigrationTestDatabase.cs`, `C1514HostLoadFixture.cs`, probe) in batch 118 | CARD-1514 S1 | Same four reviews as row 1; none ran `TestTimingOperationLabelTests`. | **Unit** | After CARD-1728 landed (batch 113, 2026-11-07 16:35), the Unit lane was formally out of per-task Final; the test is a 2-case guard that a path rule (`tests/**/BeginPhase(` additions) would select in seconds. Fixed 1d86762c54 (batch 120). |
| 5 | **`HostCleanupCheckpointReceiptTests.Report_writes_require_verified_readback`**: refusal message lost the file name (run c3f65ddb group-116, Debug 63cae064 2026-11-07 17:56). Same commit also caused CARD-1772 (`receipt_row_and_build_identities_refuse_blank_without_writing` writes `.terminal-publication.lock` before refusing; found by Review 1c1cf07a of CARD-1747 as inherited, Debug 8161c466) | `ceda26b31` 2026-11-07 07:52 "seal truthful terminal receipts before stop waiters return" | CARD-1734 (Code bfe680c4, Merge batch 111 13:02) | 23c76670 r1 Codex Frontier **Interim**, B-2 "incomplete Unit gate: plan CP-2 hit its unchanged 36-minute timeout"; 61cede6f r2 Claude Frontier **Interim**, clean, CP-1..5 (66 touched-class tests, 75 guards, 10 `CheckpointTerminalReceiptTests`). | **Unit** (8 tests) | `HostCleanupCheckpointReceiptTests` was not among the "eight touched classes" although `ReportWriter.cs` is its subject; the Unit lane was owed. Fixed b7973e862e (batch 117). |
| 6 | **`RemoteScriptContractTests` deploy harness lacks `runner_workspace_slots_enabled`** (`C849_Deploy_temp_names_saved_import_when_seed_is_missing`, `C0995_Deploy_temp_checks_host_absence_before_checkout`; run e737e9fe profile-1899, Debug 8632fba5 2026-11-08 04:58) | `a54a6d4f75` 2026-11-06 22:40 "preserve and forward opt-in workspace slots through server2 rollout" makes `case_deploy_parent`/`case_deploy_temp_runner` call the new function; the test's extracted `Block()` harnesses never included it | CARD-0824 slot-flag Compose passthrough (Code c346b7a6 Failed, 1098f5e9; Merge batch 101 2026-11-07 00:21) | da7fff71 r1 Codex Frontier **Interim**, B-2 "whole Unit remains deferred after Code's timeout"; 18209fa8 r2 **Interim**, "210 tests passed". | **Unit** (class is 225 methods / 513 cases, 1h15m on loaded server2; the 5 deploy methods are short) | The whole class is too slow for a per-task run and the reviews ran the 210-case guard batch instead. rc/2026-11-08 was cut (18:14) before this commit; rc/2026-11-09's run c3f65ddb was stopped at 391 of 1098 chunks before reaching profile-1899; e737e9fe reached it. Fixed 3364197bf1 (2026-11-08 05:17); on no rc. |
| 7 | **Statement budget pin** `C1149_C1150_Statement_budgets(initial-refused-dispatch, 44)` now 45 (run c3f65ddb split-8, Debug dafbfb5b 2026-11-07 17:12) | `43d819da71` 2026-11-06 04:32 "fence RefOnly full-ref reservations across distinct identities" adds the `WorkspaceReservationJournal.OverlapAsync` SELECT | CARD-0824 S4a (landed with the S4a stack 07:49) | d8af9f97 / e830e940 (**Interim**), and the S5b-2 review fd0ebdc3 (**Interim**, migrations, CP-1..6 + FULL-REDIRECT) | **Integration** (`DelegationDispatchRecoveryBoundaryTests`, 382 cases; the budget method is 14 cases) | The budget test is in the Integration lane and is not named by any CARD-0824 checkpoint row. Fixed 1860139c6a (batch 116). |
| 8 | **`WorktreeNoFollowDeleteTests` / C1717 fixtures**: stale tail (run 8e938b6d profile-1701, Debug 0a59a861) then NTFS sharing violation (run c3f65ddb profile-1726, Debug c16f5b36) | `d9dfe336d9` 2026-11-05 10:35 C1717 witnesses; second defect latent in the same fixture | CARD-1717 (before the window) | Earlier reviews; the fixture passes on Linux | Unit/Windows-only behaviour | Not detectable on Linux (`FileShare` semantics). This class is a native-run finding, not a review miss. |
| 9 | **Windows scratch-git timing**: `C0824WorkspaceSlotRoleGateTests` (14 then 7 failures in group-88 across 8e938b6d and c3f65ddb; again in e737e9fe) and `C0824WorkspaceSlotServerTests` (group-86) | `735e80526b`/`3b7d80c66c` 2026-11-05 22:43/23:03 and limiter `44db908e4b` 2026-11-06 19:21 | CARD-0824 S4a/S5b test infrastructure | fd0ebdc3 and the S4a reviews (**Interim**) | Integration, Windows load | Linux green at every tip; the 1 s `GitBudget` only fails under Windows process-spawn cost. Native-run finding. Fixes e02965b225, 12cf15740c. |
| 10 | **C1217 CRLF command splice** (`UnsupportedCombinationsRefuse`, run c3f65ddb group-107, Debug 061b1495) | `1535993ed0`/`47922be5c7` 2026-11-06 21:11-21:28 | CARD-1217 S1 (Merge batch 104) | bfcf249b r1, ba5f7db7 r2, 38ee4b12 r3, 619682aa r4 (all **Interim**) | Windows-only (`core.autocrlf` checkout) | Not detectable on an LF checkout. Native-run finding. |
| 11 | **CARD-1757 sampler witness scanning dead code** | `c533d031cb`/`4e1582286e` 2026-11-07 15:27-15:53 (Code 67d2b6b3) | CARD-1757 | dc7e1fe2 r1 Claude Frontier **Full**: B-1 "the no-snapshot witness guards dead code"; Debug aa1cb08e repaired; ed6858e5 r2 clean | n/a | **Caught in review**, not post-land. Listed because the brief named it; it is the one case where the Full-scope review with a mutant replay worked as designed. |
| 12 | **CARD-1743/1768/1769/1766/1753/1740 repair loops** | n/a | Review-round repair loops (f442a78f B-1/B-2 then 64437049; 53194ebc A-1 -> CARD-1769; 08a29a5c A-1 -> CARD-1766; CARD-1753 three rounds; CARD-1740 three rounds) | | | Not rc regressions. These are ordinary round-2/3 repairs and advisory follow-ups; the only rc-visible item was the C459 co-scheduling flake found by Merge 118's guard batch at `9e7ff3498` and fixed by Debug 26b144e7 (`640e560542`). |

Timeline facts that matter for "was telemetry already landed":

- CARD-1728's policy change ("take the whole Unit lane out of per-task Final", Code b1ceb641 2026-11-07
  11:52..13:23, commit `03e8946e6f` author date 12:15) landed with Merge batch 113 (16:00..16:35). The
  operator's words in the Code brief: "Nightly and rc runs make sense".
- Load telemetry S1 landed later, in batch 118 (23:07). Its round-1 review (08:22) **predates** the policy
  landing and still attempted the whole Unit lane (killed at 45 min, no TRX); rounds 2 and 3 and the
  integration review ran no Unit lane. The briefs quoted the deferral from round 1 on, so the practice
  preceded the doc change.
- None of the 52 migration reds is in the Unit lane, so the CARD-1728 change is not causal for row 1. It is
  causal only in the sense that rows 4b and 5 (Unit guards) had no per-task owner after 16:35.
- rc/2026-11-08 (`d164e93ae`, cut 2026-11-06 18:14) contains rows 2, 7, 8, 9. rc/2026-11-09 (`0e7aef0379`,
  12:51 2026-11-07) adds rows 4a, 5, 6, 10. rc/2026-11-10 (`74a8004f4`, pushed 2026-11-08 01:36) adds rows
  1, 3, 4b. The rc/2026-11-10 cut smoke (Merge 6552d9b1: 1315 passed, 3 failed, 54 min) already knew the
  C467_V17 and label reds from Debug 114129b4 and cut anyway because "Land was not requested, and master
  was still the pin".

## 2. Did any Plan, TestDesign or Review list the blast radius?

- **Plan** `docs/superpowers/plans/2026-11-06-card-1514-load-based-admission-plan.md` (670 lines): no
  section on existing tests affected by the schema change. "regression" appears only for admission (R-2/R-3
  wrapper regressions) and CARD-1217/1581 bindings.
- **TestDesign** `docs/superpowers/testdesign/2026-11-06-load-telemetry-s1-testdesign.md` (1321 lines, 52
  proposed methods, eight checkpoint rows): every CP row selects new `C1514*` classes or the legacy
  `HostStats*` classes. "Guards the regression" (R-3) is about `HostStatsAntiphonCountersTests`. The row
  "Pipeline DTO/catalogue projection ... E4 owns regression" binds a unit test, not the E2E contract
  snapshot. No mention of downgrade, `MigrateAsync`, `MigrationTestDatabase` or `CreateIsolatedSchemaAsync`.
- **Code brief** 924f8ae2: "Any EF migration you add must be additive, nullable/defaulted, sorted AFTER
  master's latest, and Down must fail safely where data would be lost; say if you add one." Nothing about
  existing tests.
- **Code completion plan** `docs/superpowers/plans/2026-11-07-card-1514-s1-verification-completion.md`
  lines 38-51 (Code 7e3e6bcc, 2026-11-07 07:13..07:47, commits `4966494c87`, `6242cbe476`): "The full
  adjacent `C0824RefOnlySnapshotMigrationTests` class is selected in a separate expected-red packet before
  repair: it assumes its three migrations are last and starts historical Down round trips from the newest
  template. New irreversible telemetry migrations make that fixture invalid." Run `20261107-073709-5238`
  executed all four old cases red and the fixture was repaired (CP-OLD, CP-M2). **The mechanism was fully
  understood for one class at 07:37 on 2026-11-07, 15.5 hours before the land, and the search stopped at
  that class.** `git show --stat 6242cbe476`: the plan doc and `C0824RefOnlySnapshotMigrationTests.cs` only.
- **Review briefs**: r1 asked for "Up and Down on a throwaway database (Down refuses destructive
  downgrade), additive and COMPATIBLE WITH THE PREVIOUS BINARY"; r2 "any migration here must be additive
  ... check it, including Down"; integration "migrations additive, sort after master's latest, Down throws,
  snapshot matches". All satisfied as asked. None asked "which existing tests call Down".
- **First written recognition of the general pattern**: Debug 51e26ceb (CardFilePrivacyMigrationTests,
  settled 2026-11-08 03:06): "Other tests still downgrade a current clone and will hit the same Down. They
  were outside this group and were left unchanged." The sweep Debug 5a1ec910 was dispatched at 03:08.
- **The operator rule** "Migrations should be additive; Down must fail safely" (2026-11-07) is quoted in
  briefs (d56a0975, 5a1ec910) and in no owner doc: `grep` of docs/project-context.md,
  docs/orchestration-loop.md, docs/testing-and-build.md and AGENTS.md for "additive migration" finds nothing.

## 3. Were the failures detectable cheaply before land? The path-based "affected tests" map

Mechanism for row 1 (`tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:422`
`CreateIsolatedSchemaAsync` returns a clone already at the latest migration;
`StandingSessionOwnershipTests.cs:64` at `9e7ff3498` then calls `migrator.MigrateAsync(migrations[target-1])`,
so EF walks `Down` from the newest migration back to the target; the first throwing `Down` is
`20261107034430_HostLoadTelemetryS1.cs`). A grep of `tests/` for `MigrateAsync(<target>)`,
`IMigrator`, `MigrationTestDatabase.Create` finds **51 files / 127 call lines** today; the sweep had to edit
37 of them.

| Change kind (path glob) | Cheap selector | Lane / size | Measured wall | Would have caught |
|---|---|---|---|---|
| `server/Migrations/**`, model snapshot | every class in the grep above (51 files) plus `AppDbContextModelTests`, `C1514HostLoadMigrationTests` | Integration, 368 cases in the sweep's union filter | the sweep's red run (368 cases, 52 failed at `6ccf8896e`) was queued about 03:30 and the fix commit is 04:03:53, so under 35 minutes including a four-deep slot queue on a loaded host; exact wall not recorded | row 1 (all 52), on round 1 |
| `server/**/Dtos/**`, `*Routes.cs`, `client/src/api/**` | `tests/Antiphon.E2E ContractSnapshotTests` (5 methods) | E2E OptIn; needs the e2e server boot | not recorded in the Debug reports; the class ran 5/5 twice on server2 inside 40-minute Debug tasks | rows 2, 3 |
| `tests/**` adding a `BeginPhase(` literal, `tests/Shared/TestTiming.cs` | `TestTimingOperationLabelTests` (2 Unit tests) | Unit, seconds | green run 2/2 in Debug c59da059; 14/14 with a neighbour in 114129b4 | rows 4a, 4b |
| `scripts/c590-remote.sh`, `scripts/lib/*.sh` | the 5 `RemoteScriptContractTests` deploy methods (method-scoped; the class is 513 cases) | Unit | 5/5 green in 8632fba5; whole class 1h 15m 33s loaded | row 6 |
| dispatcher SQL path (`WorkspaceReservationJournal`, `Dispatch*`) | `DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*` (14 cases) | Integration | narrow 14/14; class 382/382 in dafbfb5b | row 7 |
| `tools/Antiphon.Checkpoints/**` | `CheckpointReceiptSafetyTests`, `CheckpointTerminalReceiptTests`, `HostCleanupCheckpointReceiptTests` | Unit, 8+10+5 | 72 tests with guards in 63cae064; 8/8 + 10/10 in 8161c466 | row 5 and CARD-1772 |

Six selectors cover 8 of the 11 named classes (rows 1..7 and CARD-1772); rows 8, 9 and 10 need the native
Windows run (NTFS sharing, scratch-git spawn cost, CRLF checkout) and are not review misses. **No such map
exists today**: `antiphon.areas.json` maps scope names to paths for task-collision only; `grep -ril
'impacted\|affected test\|pathGlob' scripts/ tools/ tests/test-execution-policy.json` finds no test
selector. `scripts/test-nightly-tests.ps1:2499` knows `RemoteScriptContractTests` and
`DelegationDispatchRecoveryBoundaryTests` only as "big" classes for chunking.

## 4. Cost of the miss, and of the preventive check

Windows qualification (from the run-start reports; desktop logs not mounted):

| Run | rc line / pin | Started | Stopped | Chunks | Failed before stop |
|---|---|---|---|---|---|
| 472c7495 | rc/2026-11-07 `1038d07e` | 2026-11-06 06:00 | 2026-11-06 21:30 (15.5 h) | 963 of 1098 completed, 97 never started | 26 failed identities in 18 chunks |
| 8e938b6d | rc/2026-11-08 `d164e93ae` | 2026-11-06 21:54 | 2026-11-07 16:03 (18.1 h) | 962 TRX | 15 tests in 8 chunks + `ContractSnapshotTests` (home-tasks) |
| c3f65ddb | rc/2026-11-09 `0e7aef0379` | 2026-11-07 16:08 | 2026-11-08 01:40 (9.5 h) | 391 TRX | 8 tests in 8 chunks (rows 4a, 5, 7, 9, 10 and `ContractSnapshotTests`) |
| e737e9fe | rc/2026-11-10 `74a8004f4` | 2026-11-08 01:44 | running (5.8 h at 07:35) | | rows 1 (group-88, group-86), 3 (group-1), 6 (profile-1899) so far |

About 43 hours of Windows Full-run time were started in 48 hours and no cut has completed a Full run. Each
stop-and-recut also cost a Merge "Cut the new rc line" task (4ec4c7d9 2 h, ac04d0ad 2 h, 6552d9b1 1.7 h) and
a run-start task (about 25 min each).

Tasks created 2026-11-06 00:00 .. 2026-11-08 07:00 (board Antiphon, from the task API): 284 tasks, USD
4,916 in total; 35 Debug tasks (USD 505), of which 25 are rc-failure Debugs (USD 393); 44 Merge tasks, of
which at least 10 batches carried only rc-failure fixes (92, 98, 100, 102, 103, 114, 115, 117, 119, 121) and
4 were rc cuts. The load telemetry S1 chain alone (Plan, TestDesign, three Code, four Reviews, two Merges, the
Docs amendment, and the four Debugs its migrations caused) cost about USD 585, of which about USD 90 is the
migration blast-radius repair (114129b4 in part, 5a1ec910, fd4853ff, 51e26ceb) plus their share of batches
120, 121 and 123.

Cost of the preventive check, had it existed: for batch 118 the downgrade selector is one Integration run of
about 370 cases (under an hour on a loaded server2, minutes on an idle one), the contract snapshot class is 5
E2E methods, and the label guard is 2 Unit tests. For a batch with no migration the fixed subset is minutes.

## 5. Recommendations, in order of value

Replay column: for each of the eight Linux-reproducible regression classes (rows 1, 2, 3, 4a, 4b, 5, 6, 7),
would this measure alone have caught it before land?

| # | Measure | Effort | Risk | Replay on this history |
|---|---|---|---|---|
| a | **Impacted-classes selector** (CARD-1782, High): one reviewable table `path glob -> exact TUnit filter -> time bucket` (seed rows in section 3), a mechanical `impacted` verb that resolves `git diff --name-only base..HEAD` to filters, wired into the stage-code and stage-review SCOPE sentences and the Merge batched run, with a guard test that fails when a glob matches nothing, a class is gone, or a new `MigrateAsync(<target>)` call site appears outside the derived list | Code (tool verb + table + guard test) and the bundle witness re-pin | Low; it adds selections, never removes one. The downgrade row is the only slow one (under an hour when migrations land) | 8 of 8 (rows 1, 2, 3, 4a, 4b, 5, 6, 7). Row 1 on round 1 (08:22), 15 hours before batch 118 |
| b | **Merge batch and rc cut run a fixed cheap high-radius subset** (CARD-1783): `TestTimingOperationLabelTests`, `AppDbContextModelTests`, `ContractSnapshotTests`, `CheckpointReceiptSafetyTests` + `HostCleanupCheckpointReceiptTests`, plus the downgrade selector when the batch diff touches `server/Migrations/**`; its own CHECKPOINT row; the rc cut refuses to push the ref while the subset is red | Code (Merge/cut brief recipe + one row) | Low; a few minutes per batch, under an hour when migrations land | 6 of 8 at the batch (rows 1, 3, 4b at batch 118; row 2 at the S4a land; row 4a at batch 99; row 5 at batch 111). Rows 6 and 7 need their method-scoped selectors from (a) |
| c | **Linux post-land early warning** (CARD-1784): after each land batch, run the CARD-1728 Unit shards and the impacted Integration classes on server2 detached, write TRX per shard and post new reds to the orchestrator attention feed with the introducing batch; failures get Debug tasks before the cut | Code/ops (a scheduled or land-triggered task) | Medium: server2 load contention with reviews (review d56a0975 already failed a timing row under two foreign whole-Unit lanes); bound to one shard set at a time | 8 of 8 within about an hour of the land, instead of 6 to 20 hours later on Windows; the three recuts would have carried the fixes |
| d | **Review brief and docs rule for migrations** (CARD-1785): record the operator's additive-migration rule in the owner docs; the Review recipe for any `server/Migrations/**` change must run the downgrade selector and report its count; Plan/TestDesign templates get a required "Existing tests affected" row for migrations, public DTOs/routes, operation labels, remote script functions and statement budgets; the Code brief's "say if you add one" becomes "run the downgrade selector and report red/green" | Docs plus a small Code change to the bundle witness tests | Low | Row 1 on round 1; rows 2..7 only as far as the template row makes authors name them (a reminder, not a mechanism) |
| e | **Stop re-cutting on a known red**: the rc/2026-11-10 cut smoke knew two reds (C467_V17, labels) with fixes already on branches and cut anyway; make "a known master red with a fix in flight" a stop gate for the cut, or land the fix first (standing rule 1: failures get a Debug task; add: a cut waits for its Debug fixes) | Docs (orchestration-loop cut rules) | Low | Would have saved one recut (rc/2026-11-10 would have carried `72771947e`); does not catch anything new |

Not recommended: putting the whole Unit or Integration lane back into per-task Final. CARD-1728's
measurement stands (the A-L shard alone is about 45 minutes; a killed host writes no TRX), and the Unit lane
would not have caught the largest miss.

## 6. Cards filed

Duplicate search first (`card.ps1 search ... -Board Antiphon -All`): "impacted classes", "affected tests",
"blast radius", "post-land retrospective", "downgrade", "contract snapshot", "migration Down", "high-radius"
returned no card covering these asks (CARD-1648 is the Linux path case of the contract snapshot; CARD-0063 is
the scope areas file).

| Card | Title | Importance |
|---|---|---|
| CARD-1782 | Impacted-classes selector: path-glob to test-class table run by Code, Review and Merge for high-blast-radius change kinds | High / Soon |
| CARD-1783 | Merge batch and rc cut run a fixed cheap high-radius subset | Normal / Soon |
| CARD-1784 | Linux post-land early warning: Unit shards and impacted Integration classes on server2 after each land batch | Normal |
| CARD-1785 | Migration changes: review brief must run the downgrade selector; record the additive-migration rule and an "existing tests affected" row | Normal / Soon |
| CARD-1786 | Post-land retrospective: load telemetry S1 migration blast radius (label `post-land-retrospective`) | Normal |

## 7. Plain-English summary for the operator

- **What broke.** The load telemetry work added three database migrations whose "undo" step throws on
  purpose (your additive-migrations rule). 52 existing tests rewind a fresh database to an older schema as
  part of their setup, so every one of them now hits that throw. Separately, two API shapes changed without
  their contract snapshots being recaptured, new timing labels were added without being registered, a bash
  function was added without the test harness learning it, one new SQL statement moved a pinned statement
  count, and one refusal message lost its file name.
- **Why review missed it.** Reviews run the classes the change touched plus a fixed guard batch. Every one
  of these failures lives in a test outside that set. The reviewers checked exactly what the brief asked
  (the migrations are additive and the undo throws) and said so; nobody asked which existing tests would
  call the undo.
- **The Unit-lane decision is not the cause of the biggest miss.** All 52 migration tests are Integration
  tests, which only the nightly and the Windows rc run execute. Taking the Unit lane out of per-task Final
  (your "nightly and rc runs make sense") did remove the only owner of three small Unit guards (labels,
  receipt message, remote-script harness), and those did reach the rc run.
- **The pattern was known early.** The S1 Code stage hit the same failure in one class at 07:37 on
  2026-11-07, understood it, fixed that class and wrote it down. The search stopped there. A one-line grep
  would have listed the other 50 files.
- **What it cost.** Four Windows Full runs started in 48 hours, three of them stopped and recut, none
  complete; roughly 43 hours of qualification wall-clock; 25 rc-failure Debug tasks (about USD 390); about
  ten Merge batches that carried only rc fixes; three rc cuts of about 2 hours each. The migration miss alone
  cost about USD 90 in Debug plus a recut.
- **The failures were cheap to find.** Every non-Windows-only failure reproduced on Linux at the landed
  tip in seconds to minutes; the migration set in well under an hour. The first machine to run those tests
  was the Windows run, 6 to 20 hours later.
- **What we do about it.** CARD-1782 (High): a single table mapping kinds of change to the tests they
  endanger, applied mechanically by Code, Review and Merge, with a guard that keeps it current. CARD-1783:
  the Merge batch and the rc cut always run a small fixed high-radius set. CARD-1784: run the Linux half of
  the rc suite right after each land, not only at rc time. CARD-1785: write the additive-migration rule into
  the owner docs and make a migration review run the downgrade check.
- **What stays Windows-only.** NTFS sharing, CRLF checkouts and scratch-git spawn cost cannot be seen on
  Linux; those three classes are what the Windows run is for, and they are not review misses.

## Remaining uncertainties

- Windows chunk durations and the exact failing-method lists per run come from the run-start task reports;
  the TRX and `antiphon-all-*.log` files under `C:\Antiphon\releases\candidates\...` were not readable from
  this runner.
- The downgrade selector's wall time is bounded by the sweep's timeline (red run queued about 03:30, fix
  commit 04:03:53), not measured; a dedicated timing run is needed before it becomes a checkpoint row.
- Whether the nightly backstop ran on master between the batch-118 land (2026-11-07 23:07) and the rc cut
  (2026-11-08 01:36) is not verifiable from this runner (Windmill is desktop-side); the rc cut's own smoke
  did not include the Integration lane.
- Costs are the task rows' `costUsd` sums and exclude Windows host time and operator time.

## Not done, noted

- Fix idea for the migration class is already landed (forward-built fixtures, batches 121/123); the structural
  fix is CARD-1782's derived downgrade list plus a guard test, not more fixture edits.
