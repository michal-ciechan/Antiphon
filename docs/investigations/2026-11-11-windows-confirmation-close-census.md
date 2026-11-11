# Windows-confirmation and mutation-only close census (2026-11-11)

Read-only. No card, run, host, session or container was touched. Operator question: which cards wait ONLY for Windows mutation checks or Windows/native confirmation, and can therefore be closed under the standing close rule (operator, 2026-11-10)? The rule: every named slice is on `origin/master`, and a Final Clean Review exists at a landed SHA (patch-id equivalence counts).

## Inputs

- `origin/master` = `3db1a43b02246706dc28075fce7e7b5e84804ddb` (2026-11-11 17:10Z). The live rc run is on the same SHA.
- Board Antiphon, read 2026-11-11 about 20:55Z through the server API. Review 127, InProgress 37 and Backlog 256, all untruncated. Full text for every Review/InProgress card came from `GET /api/cards/{id}`. Every stage-outcome row came from `GET /api/stage-outcomes?cardId=&latestOnly=false`. Open tasks came from `GET /api/agent-tasks?boardId=...&status=Queued|Dispatched|Working|Blocked`: 6 rows (CARD-1842 Code Queued f2fe07ed, CARD-1843 Code Queued 3e96732b, CARD-1514 Custom 6291197d (this task), CARD-1830 Code 024cf048, CARD-1304 Code 1ae8dfd4, CARD-1844 Code aec861c3). Bound-task history came from the board task list since 2026-10-15 (3308 rows).
- Seeds: `2026-11-10-card-census-review-inprogress.md` (5bbb6ecd), `2026-11-10-close56-verification.md` (9c351071), `2026-11-11-stale14-verification.md` (14a536e5) and `2026-11-11-unlanded-landing-queue.md` (991aa3f4). Each verdict below was re-checked against git and card text. Where a verdict repeats a seed note, it cites it.
- Live Windows Full rc run `ae84478dd83d4cde8f537ba8303794fb` on `rc/2026-11-17`:
  - Logs: `C:\Antiphon\releases\candidates\rc-2026-11-17-3db1a43b0224\logs\2026-11-11-1907-ae84478d`.
  - Schedule: `dispatch-schedule.json`, 1162 chunks, executed one at a time in `dispatchOrdinal` order.
  - Class-to-chunk map: `timing-ae84478d...\groups-planned-*.json`.
  - At about 21:10Z, 21 TRX were complete and `all-group-39` was active (ordinal 21). Every completed chunk was green, including the six failed-first chunks.
  - Schedule order is failed first, then the interrupted lane, then untouched chunks, then chunks that passed in earlier runs. So a class that passed before (e.g. `all-group-7`, `all-group-127`) runs near the END of this run.
- Patch-id test: `git cherry origin/master <sha> <sha>^` returns `-`. Ancestry test: `git merge-base --is-ancestor`.
- rc roster limits found while checking:
  - The rc `scripts` suite runs only the 13 `unattended` rows of `tests/test-execution-policy.json` `scriptCensus`, under pwsh 7. The live supervisor is `C:\Program Files\PowerShell\7\pwsh.exe ... nightly-run.ps1`.
  - So Windows PowerShell 5.1 rows and script-only witnesses are not rc confirmations.
  - `resilience` IS an rc suite in this run (`resilience all`, ordinal 671).

## Result

| Bucket | Cards |
|---|---|
| A, closable now | CARD-1294 |
| B, closable after a named ae84478d chunk passes | CARD-1510, CARD-1731 |
| C, needs a separate Windows run or Mutation/proof task | CARD-1532, CARD-1597, CARD-1013, CARD-1757, CARD-1368 |
| D, not closable | everything else with a Windows/mutation tail (table below), including the operator-named partials |

No card met A on Windows evidence from ae84478d itself. The chunks finished so far contain no class that is the only leftover of an otherwise-complete card. That was checked by matching every class in the 21 completed chunks against all 164 Review/InProgress card texts. The hits were CARD-0856, 1212, 1217, 1230, 1514, 1548, 1729 and 1820, and each is blocked for a non-Windows reason, listed in D.

## A. Closable now

### CARD-1294 (Review): B-3 auto-close advisory gaps: sibling task, workflow run, shadow cohort, tracker push

- **Slices.** Card items 1-5 were implemented by Code `12f9d69b-870a-4538-80dc-f5618192f9ad`, landed as:
  - `df369fb3c39e5e412e02ab7b50f6add3d337a1ad`: red witnesses for sibling, workflow, adjudicator and tracker close.
  - `3aadc16e123f6a3bebb50b4786db8129604ff6dd`: red shadow witnesses on an additive event table, migration `20261110213738_Card1294ShadowCohort`.
  - `d09f21982ec60e23d1b74209c0f37f19e1d463fb`: the fix.
- `git cherry -v origin/master 68dc04a79fe8 d27d19165db7` marks all three branch commits `-`. All three are ancestors of master.
- **Item 6.** The card text itself says the true-contention tests "belong to S6 (V-33..V-36, R-8)". That S6 is the CARD-1281 S6 wiring slice (`docs/superpowers/plans/2026-10-17-card-1281-open-repairs-plan.md`, V-33 at line 96). The Code report says "Item 6 stays deferred to S6".
- **Review.** `f929e906-3084-489b-9c3c-8accccb35e76`, recorded 2026-11-11: Clean, Final, ordinaryScopeCompleted Full, `reviewedSourceClean=true`, at `68dc04a79fe80192e38e1e103706d4121c7d80de`. That SHA is patch-id identical to `d09f21982`. CP-1 27/27 and CP-2 78/78 passed with validated receipts. It is the only Review row, and there is no later Found.
- **Open tasks and branches.** No open task. Only two tasks are bound (the Code and the Review), both Succeeded. No unlanded branch: `origin/feat/card-task-12f9d69b` has no `+` commits.
- **Remaining.** PC-27..PC-40 and PC-58..PC-64, the parent B-3 SourceLanding Mutation controls, are pending. No Windows obligation. The close ships Disabled, and the card names no activation.
- **Doubt recorded.** Item 6 is closed by pointing at CARD-1281 S6 rather than by a commit. If the operator wants item 6 tracked on its own card, this moves to D.

Close reason text:

> Shipped: CARD-1294 items 1-5 (unlanded succeeded Code sibling blocks close; workflow completion clears the active run; durable shadow cohort with adjudicator != actor; tracker push and Review checkpoint on close) on master as df369fb3c39e5e412e02ab7b50f6add3d337a1ad, 3aadc16e123f6a3bebb50b4786db8129604ff6dd, d09f21982ec60e23d1b74209c0f37f19e1d463fb. Final Review f929e906 Clean, Full, reviewedSourceClean=true at 68dc04a79fe80192e38e1e103706d4121c7d80de (patch-id of d09f21982). Remaining: SourceLanding Mutation PC-27..PC-40 and PC-58..PC-64 (separate stage). Item 6 (true contention tests V-33..V-36, R-8) belongs to CARD-1281 S6 as the card text states. Closed under the 2026-11-10 mutation/confirmation-only close rule.

## B. Closable after a named ae84478d chunk passes

### CARD-1510 (InProgress): Windows PATH probe test rejects the PowerShell launcher alias prefix

- **Slices.** S1 red `5c96c0b2f94dcf28289a40e422ec899c191991ef` and S2 resolver `6d91a99e108f310ed7e6603c3887011b15d189fe`. Plan: `f9836a507d47dd1fdc76fc4ce02fb1d470048d55`.
- `git cherry -v origin/master 705662142c95 ef121b23f` marks plan, S1 and S2 all `-`.
- **Review.** `269a3142-bcaa-4bce-a858-253d45a2a36c` (2026-10-23): Clean, Final, Full, `reviewedSourceClean=true`, at `705662142c95e9c3614a8a229f0fbd4b2bbf2c66`, which is the patch of `6d91a99e1`. Only Review row.
- **Open tasks and branches.** Code `5019ea31` is Canceled, but its commits are on master. Its branch has no `+` commits. No open task.
- **Remaining.**
  - Plan CP-6 (`docs/superpowers/plans/2026-10-23-card-1510-path-probe-plan.md:41`): native Windows green of `RunnerProcessProbeTests.Inherited_path_over_the_ceiling_is_bounded_on_an_entry_boundary_and_the_probe_runs`. The card text: "Validate the exact method on Windows with the installed/alias launch paths."
  - CP-5, the Windows red, is historical: rc run d2777e21 group-5, `antiphon-all-group-5.trx:221`.
  - PC-1 and PC-2 are Mutation.
- **Confirming chunk.**
  - `antiphon all-group-7`, 294 tests, holds `RunnerProcessProbeTests` (33 discovered, 0 excluded) and `RunnerProbePowerShellFixtureTests` (15, 0 excluded).
  - Dispatch ordinal **1141 of 1162**, in the "passed before" tail, so it is near the end of the run. Not run yet.
- **Supporting, not ae84478d.** The exact method passed natively in four earlier Windows rc runs whose cuts contain `6d91a99e1`:
  - 231f573c (rc-2026-11-13-653f1145de5b)
  - dce78ee0 (rc-2026-11-14-7bee43614495)
  - 6591e378 (rc-2026-11-15-ac6d00022e94)
  - d88de419 (rc-2026-11-16-6f8741d872d6): `antiphon-all-group-7.trx` 294/294, all 15 `RunnerProbePowerShellFixtureTests` cases green.
  - If the operator accepts an earlier rc run as confirmation, this card is A now.

Close reason text (after `antiphon-all-group-7` passes in ae84478d):

> Shipped: PATH probe resolves the installed PowerShell and rejects WindowsApps launcher aliases; S1 5c96c0b2f94dcf28289a40e422ec899c191991ef, S2 6d91a99e108f310ed7e6603c3887011b15d189fe, plan f9836a507d47dd1fdc76fc4ce02fb1d470048d55. Final Review 269a3142 Clean, Full, reviewedSourceClean=true at 705662142c95e9c3614a8a229f0fbd4b2bbf2c66 (patch-id of 6d91a99e1). Windows CP-6 confirmed natively: rc run ae84478d antiphon-all-group-7 <N>/<N> including RunnerProcessProbeTests.Inherited_path_over_the_ceiling_is_bounded_on_an_entry_boundary_and_the_probe_runs (also green in rc runs 231f573c, dce78ee0, 6591e378, d88de419). Remaining: SourceLanding Mutation PC-1/PC-2. Closed under the 2026-11-10 mutation/confirmation-only close rule.

### CARD-1731 (InProgress): Publication ledger tests (ExecutionLedger, Joins, Markers) sit at their 300 s timeout under load

- **Slices on master.**
  - Batch-91 snapshot repair `1038d07e66a54b630a1bc1d394923bc7049b73dd` (ancestor).
  - Code 317b5f73: `0af14029dc48ce06802eae3feb591157a66080f2` (G-287 witnesses and walk budget), `df019b160454a5e0a396107264e7bcc6919846f5` (one attribute read per component), `70aec174ed3f6d2fff2fc53ba59428fd9e821448` (pending PC record). `git cherry -v origin/master cf0aba324bdf 9e7ff34988cd` marks all three `-`.
  - Budget decision doc: `8b75f315467f7b25c021ec1c0299c04e7398c0b8`.
- **Review.** `6c5b2084-dc6c-444c-b081-d281e754f272` (2026-11-08): Clean, Final, Full, `reviewedSourceClean=true`, round 1, at `cf0aba324bdfff15e10f4ef0120c84a6c380f345`, which is the patch of `70aec174`. It measured under load 25-28: Ledger 63.2 s (gate 19.6 s, was 106.6 s), Markers 63.3, Joins 40.9. Only Review row.
- **Open tasks and branches.**
  - Code 0915902c Failed: its branch has no `+` commits.
  - Code 317b5f73 Canceled: its commits are on master.
  - Debug b8770621 has one unlanded docs-only commit, `11cdf6ecbeb42ce7b08b4771943192395572507d` ("investigate CARD-1731 publication ledger timeouts"). It is a report, not a slice. Its finding is superseded by `8b75f3154`.
  - No open task.
- **Why B, not A.** `8b75f3154` (2026-11-07) leaves "the residual" C599A_ExecutionLedger 300 s overrun, seen in the Windows pre-cut smoke c3f65ddb, "on open CARD-1731". The 11-08 fix is the answer, so a Windows pass of the class is the confirmation.
- **Other remaining.** PC-1..PC-6 are Mutation. The review advisories A-1..A-4, including the Windows-only shape rows and a Windows 15 s sample, are already filed as CARD-1770.
- **Confirming chunk.** `antiphon all-group-127`, 40 tests, holds `ReleaseGatePublicationAuthorityTests` (`C599A_ExecutionLedger`, `...Joins`, `...Markers` and the other 12 C599A cases). Dispatch ordinal **1070 of 1162**. Not run yet.
- **Supporting, not ae84478d.** `all-group-127` passed 40/40 natively in earlier runs whose cuts contain `70aec174`: 231f573c, dce78ee0, 6591e378. C599A_ExecutionLedger took 30.6 s, 26.4 s and 34.3 s; Joins 32.3, 27.2 and 34.8 s; Markers 28.7, 26.4 and 30.6 s. That is far under 300 s.

Close reason text (after `antiphon-all-group-127` passes in ae84478d):

> Shipped: C599 publication ledger cost cut. Redundant snapshot restores removed (1038d07e66a54b630a1bc1d394923bc7049b73dd); release evidence paths resolved with one attribute read per component plus G-287 witnesses (0af14029dc48ce06802eae3feb591157a66080f2, df019b160454a5e0a396107264e7bcc6919846f5, 70aec174ed3f6d2fff2fc53ba59428fd9e821448); budget decision 8b75f315467f7b25c021ec1c0299c04e7398c0b8. Final Review 6c5b2084 Clean, Full, reviewedSourceClean=true at cf0aba324bdfff15e10f4ef0120c84a6c380f345 (patch-id of 70aec174). Windows confirmation: rc run ae84478d antiphon-all-group-127 <N>/<N>, C599A_ExecutionLedger/Joins/Markers well under the 300 s budget (also 26-35 s in rc runs 231f573c, dce78ee0, 6591e378). Remaining: SourceLanding Mutation PC-1..PC-6; review advisories A-1..A-4 live on CARD-1770. Closed under the 2026-11-10 mutation/confirmation-only close rule.

## C. Needs a separate Windows run or Mutation/proof task

All five have every named slice on master and a Clean, Final, Full review with `reviewedSourceClean=true` at a patch-id-landed SHA. What is left is a native check the rc roster cannot supply. Once that check is green, each closes like the A card.

| Card | Column | Title | What is owed | Task needed |
|---|---|---|---|---|
| CARD-1532 | InProgress | Checkpoint source stamps reject build DLLs above the 64 MiB receipt limit | S3 CP-9 / V-10, the Windows PowerShell 5.1 parser row. The plan (`docs/superpowers/plans/2026-10-23-card-1532-streaming-source-stamps-plan.md:121,283`) calls a missing 5.1 parse an incomplete gate. Review `f5885ccd` (Clean Full true at `7932e60205a3`, patch on master): "CP-9/V-10 on Windows PowerShell 5.1 Desktop remains owed". Not in rc: the scripts suite runs under pwsh 7, and the script is not in `scriptCensus`. Mutation variants are also pending, but those alone would not block. | Desktop Windows Debug, about 15 min: `powershell.exe -NoProfile -NonInteractive -File scripts/test-checkpoint-stamp-ps51-parse.ps1`. It must print `PARSER PASS version=5.1... edition=Desktop ascii=4 parsed=4 negative=2`. Record the line on the card. |
| CARD-1597 | Review | Qualification timing recorder 256 KiB cap refuses most real chunks | Review `c5516a19` (Clean Full true at `17cb417c3b3d`, patch on master; branch base `b71d9b31` is an ancestor) leaves A-4: "PS5.1 native witness on the desktop lane is still owed". That is `scripts/test-qualification-timing.ps1` row `ps51-powershell-exe-load-pending-native`. The card's A3 also owes "CP-8 overhead check on Windows". Not in rc: the script is not in `scriptCensus`, and the rc supervisor is pwsh 7. Supporting native evidence: ae84478d's own timing sidecars parse real large TRX without refusal. `antiphon-all-group-130.timing.json` has testCount 439 and p95 18.16 s from a 765 KB TRX with `why=''`; group-75 and session-runner group-2 look the same. | Desktop Windows Debug, about 20 min: run `scripts/test-qualification-timing.ps1` on the desktop, where `powershell.exe` exists, so the ps51 load row executes rather than SKIPs. Record the CP-8 overhead numbers from the ae84478d timing root. |
| CARD-1013 | InProgress | Debug: native Windows confirmation of CARD-1001 opened-handle coverage | The Windows run was done (2026-10-03 result on the card). The one unverified acceptance item is "an in-repo symlink to an in-repo file is still accepted". The account lacks the symlink privilege, so `PlanCoverageCommandTests.CreateProjectLink` Skips, and rc on the same account cannot prove it. Review `a87b9e00` is Clean Full true at `1ca05321119d` (patch on master). PC-1..PC-5 belong to the landed owner CARD-1001, not to this card. | Desktop Windows Debug with Developer Mode or an elevated token to create the symlink: run the coverage verb on an in-repo symlink to an in-repo file and expect exit 0, same JSON. Or the operator waives the symlink row and closes. |
| CARD-1757 | Review | SessionRunner CPU: HostStatsSampler takes a whole-system process snapshot per sampled process | Fix is on master: `c533d031cbe160d7a829f35b87bf02dd13926cfe`, `4e1582286efd09cb39e82feb08689dcf3c73e24a`, `2e0dbd1c095a02819afd742abaae78ffbb5c460e`, `e54425152eb8817f6adb7975fe2793a8f60a3388`. `4e1582286` is in rc/2026-11-17. Review `ed6858e5` is Clean Full true at `86b1a964b481` (patch on master). The acceptance "a re-trace shows WorkingSet under 1% of runner CPU" is a measurement, not a test. "Existing HostStats tests pass" is in rc: `HostStatsSamplerTests` is in `session-runner all-group-3`, ordinal 538. | Desktop Windows measurement task: a 5-minute `dotnet-trace` of the session runner once the runner runs a build containing `4e1582286`, compared against `docs/investigations/2026-11-07-desktop-server-runner-cpu-trace.md`. |
| CARD-1368 | Review | Checkpoint cleanup refuses runtime-created TestResults under the row output | Adoption and the 1 GiB stamp are on master: `a323cc6d03828dd84eae8a4e208ef02df17749aa`, `7c20ea9eb98a35febc2f915cfd15203ec6d333ee` (stale14). Review `b874a8b3` is Clean Full true at `359cc912235d` (patch on master). Still owed: (1) the card's "one red-first witness", where W-1's guarded red is unproved because the original fixture lacked Git (`docs/superpowers/evidence/2026-10-19-card-1368-code.md:111`); (2) four Windows-only `OutputCleanupTests` rows; (3) deferred CP-8..CP-13. Items 2 and 3 are in rc: OutputCleanupTests per-method chunks are ordinals 772-810, `CheckpointSourceExecutionTests` is all-group-109 (ordinal 80), CheckpointOwnedOutput, ToolCopyCleanup and RunScheduler are all-group-110 (ordinal 81), and HostCleanupCheckpointAuthorityTests is all-group-119 (ordinal 60). Item 1 is not an rc check. | A method-scoped proof task (Mutation stage or Debug) that shows W-1 (`CheckpointSourceExecutionTests/host_written_entries_under_the_row_output_are_adopted_and_cleaned_on_green`) red at pre-fix `bc3f7a828a81` with the corrected Git fixture. Then the card is B on the rc chunks above. |

## D. Not closable

Operator-named partials first, then the remaining Review/InProgress cards with a Windows or mutation tail.

| Card | Column | Title (short) | Blocker |
|---|---|---|---|
| CARD-1304 | Review | CARD-1259 verification depth | Latest Review `7415c025` (2026-11-11 13:04Z) is **Found** at `185cc85a2685`: "DueAt clock witness cannot go red, and V-5 parent H0-H4 plus V-9, V-10, and V-12 cuts are still missing". Code `1ae8dfd4` is Dispatched. |
| CARD-1832 | Review | Declared test platform taxonomy | Umbrella. S1/S2 plus probe fixes landed (`acaa9d9b1`, `c83846158`, `a3917c155`); plan slices S3-S9 are outstanding. |
| CARD-1224 | Review | Linux UnixPty delivery ceilings | S2+S3 landed (`b35ed9863`, review `b6ebea9c` Clean Full true at `bb4f2811c251`). Acceptance requires transcript-confirmed, version-bound measurements against the real Linux Claude/Codex/Grok binaries. The landed receipt is a fake-agent one (`822d1ab44`). PC-1..PC-10 pending. |
| CARD-1829 | Review | Antiphon.Resilience.Tests mirror | Slice of umbrella CARD-1729. Code is on master (`55dd78b35`..`1e4785252`). Review `09847c78` is clean but `reviewedSourceClean=false` (leased runs, no receipts), and its tip `013d29a0` (land-graph record) is not patch-equal. Note: `Antiphon.Resilience.Tests` IS in the rc roster as suite `resilience`, ordinal 671 in ae84478d, not run yet. |
| CARD-1830 | Backlog | Antiphon.GitInProcess.Tests mirror | Code `024cf048` Dispatched. |
| CARD-1750 | Review | Turn on per-slot worktrees | No Review row; it is an activation card. |
| CARD-1844 | Review | Plan: move AppHost Postgres to server2 | Code `aec861c3` Dispatched. |
| CARD-1842 | Review | Test and batch timing model | Code `f2fe07ed` Queued. |
| CARD-1843 | Backlog | Mirrored test projects Get-Project mapping | Code `3e96732b` Queued. |
| CARD-1514 | InProgress | Speed up slowest test classes | In flight (this census is bound to it). |
| CARD-1818 | Review | gc owner / stop-supervisor advisories | Near miss; details after this table. |
| CARD-1233 | InProgress | CARD-1199 inherited readers, PC-11, Windows R-2 | Only S2, S4 and S8 of S1-S16 landed (`c04b5cd7b`, `9e78a0888`, `ff2287125`). R-2 is S16. |
| CARD-1636 | Review | Non-custody adoption session id | Review `4521968e` is Interim with `reviewedSourceClean=false`. It says "Still pending: the Windows lane for Card1636, the Windows-only custody rows". Needs a Final review first. |
| CARD-1598 | Review | Timing reader cost grows with journals | Review `614831b6` is Interim. Windows junction refusal and the PS5.1 load row are pending. |
| CARD-1591 | Review | Dialog guard withholds notices | PARKED 2026-10-27 after two failed attempts; not shipped. |
| CARD-1770 | Review | CARD-1731 follow-ups incl. Windows witnesses | A-1..A-4 have no landed commit naming the card. The reviewed SHA `34d383692e89` is a different change (close56). |
| CARD-1641 | Review | 2026-11-01 live-run advisories | Culture-parse, overlapped-read and live-claude ancestor-guard groups are not landed (close56). |
| CARD-1728 | Review | Unit shard completion | Requires a complete Unit receipt; the Clean review approved a SCOPE-line edit (stale14). |
| CARD-1784 | Review | Linux post-land early warning | Approving SHA `6fa1d709ecbb` is cherry `+`. Later repair commits through `3a8deb98a` have no new review. |
| CARD-1773 | Review | CARD-1769 follow-ups | Approving SHA `aa07b33d55df` is cherry `+`; A-6/A-7 advisories are not landed. |
| CARD-1022 | InProgress | Remove legacy inbox ConPTY backend | Approving SHA `9dd710fceb3b` is cherry `+`; native proof pending; the deprecation is unfinished. |
| CARD-1033 | InProgress | CARD-1017 S4/S5 + Windows qualification | S4/S5 are not on master; only inert columns (`a1aa8c8d`). |
| CARD-0812 | InProgress | Phone-home runner single repo | PARKED; Windows CP-1, live markdown acceptance and the D-5 operator decision are owed. |
| CARD-0987 | Review | CARD-0826 remaining S2-S5/native/PCs | `reviewedSourceClean=false`; the title itself lists open S2-S5. |
| CARD-1526 | InProgress | Process: real-type witness + native smoke | Native smoke and the release-doc rehearsal line are not shown (close56). |
| CARD-1323 | InProgress | RC lane placement D-3/D-5 | D-3/D-5 leftovers and the 69 LandDelivery placement decision are still named. |
| CARD-1230 | Review | C1017_TerminalTaskRequired fails | Review `4999f0e5` is clean, but "the card is only partly explained": the non-temp CP-4 red is unexplained. Not a Windows tail. |
| CARD-1752 | Review | Enable ChannelBridge on Linux server | S1 compose only (`c59ec9232`); acceptance is the live enable, which the review says not to do. |
| CARD-1548, CARD-1553 | Review / InProgress | C1259/C1115 fixture repairs | Repairs are on master (`29f1f908bf0a`, `a705977dccb1`) but there is no Review row (stale14). CARD-1548's `SessionStateWarmupTests` already passed natively in ae84478d `antiphon-all-group-75` 453/453. A Final Review would make 1548 closable. |
| CARD-0972 | Review | Windows half-reset row + SourceLanding PCs | No Review row; the Windows row was never executed (stale14). |
| CARD-1618, 1623, 1625, 1703, 1030, 1334, 1556, 1287, 0856, 1212, 0931, 1023, 1327, 1401, 1477, 1568, 0038, 0597, 0692, 1744 | Review / InProgress | Windows-tagged cards | No Review stage-outcome row, so no Final Clean Review exists. |
| CARD-0824, 1217, 1305, 1614, 1653, 1701, 1729, 1613, 1616, 1658, 1698, 1715, 1717, 1718, 1732, 1737 | Review / InProgress | Windows/mutation-tagged cards | Latest approval is Interim, or the latest Review is Found. Fails the Final bar. |

CARD-1818 detail (near miss):

- **Code on master.** `58454a7a7`, `4c5eb59b4`, `e753cc6e9`, `df1248539`, `09d80f11a`. The reviewed range `a6cdc410..a8e4325b2` is all cherry `-`.
- **Native confirmation already exists.** Its witnesses passed natively in ae84478d:
  - `session-runner-all-group-2` 464/464, including all 10 `C1818GcOwnerRobustnessTests` methods. `C1818_symlinked_git_directory_is_not_a_repository` failed in 6591e378 and d88de419 and now passes, after `a4ea62dd3` and `974e67ed6`.
  - `C1818RepositoryMaintenanceStatusTests` in `antiphon-all-group-35` 54/54.
- **Still blocking.**
  - Review `5b3c6c26` round 2 has `reviewedSourceClean=false` (leased runs, no receipts).
  - The round-2 advisory A-4 (pre-gc `packed_refs_lock_present` skip and a lasting signal) has no card. A search of the Review/InProgress text found none.
  - The card text's CARD-1507 A-3/A-4 stop-supervisor native Windows witnesses (identity branch, `taskkill /T` record) are script-only and not in rc.
- **To close.** File the A-4 advisory card, run the stop-supervisor smoke natively on the desktop, and accept or re-receipt the review.

Other Clean/Full cards re-checked and still open for the non-Windows reasons recorded in close56: CARD-0167, 0507, 0667, 1017, 1039, 1149, 1246, 1379, 1518, 1527, 1629, 1639, 1695, 1768, 1774, 1776, 1820, 1823 and 1836. No new review rows since close56 change those verdicts. CARD-1820 is a plan card. CARD-1823, 1832 and 1836 have later slices outstanding.

## Not done

- No card was moved, closed or edited.
- The six failed-first chunks (session-runner g2 464/464, antiphon g75 453/453, g130 439/439, g2 163/163, g60 101/101, g28 37/37) were read from their TRX.
- The B close texts carry `<N>/<N>` placeholders. Fill them from the chunk TRX `Counters` once `all-group-7` (ordinal 1141) and `all-group-127` (ordinal 1070) complete.
