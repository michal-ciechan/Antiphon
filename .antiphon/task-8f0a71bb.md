# rc top-up 47 — CARD-1514 and Remote custody assertion

## Provenance

- Starting rc and assigned branch base: `b37178b6bde70a79c40e2172a60eb05bb94895bb` (fresh origin fetch matched).
- Verified source after both series: `49322b8e0a40a8c94724385b4103d84f15b8fb61`.
- Assigned branch: `feat/card-task-8f0a71bb`. Every port commit was pushed separately. No rebases, resets, force pushes, tags, activation, runner/container operations or canonical checkout changes.
- `_PtyHostNativeProps` remains absent. No CARD-1609, CARD-1610 S2/S3 or group 104/105 fixture changes were ported.

## Series 1 — CARD-1514 manifest publication

31 commits selected from the `+` entries of `git cherry -v origin/rc/2026-10-27 origin/feat/card-task-79c0c349`, in its emitted order. Code-bound plan/investigation records and mixed commits were retained.

| Original full SHA | Port full SHA | Subject |
|---|---|---|
| `de4863ac37e4197fb1fd69e70c463069f8016476` | `250da35fa49eebf0764e64782b78d5cb15d76e74` | docs: adopt the reviewed CARD-1514 incremental manifest publication plan |
| `c214e671590f0aef6f4aa45c5a81b86fdce87b1f` | `dab73c3fea7c38d73d66fda1fd88ed67fccfdacd` | test: add CARD-1514 manifest publication red witnesses |
| `597848127038e5dc2f3cd32f42874a7f29380591` | `c2f2c65a01d3fe30e0d5414d4a8f631c0d81d6e5` | fix: publish executed manifests through an incremental journal |
| `28df13d78e4c3a514f65c6bcec0aa544e85d13ee` | `5714ca05ce53f0644a3de25162834d3251cfb9e0` | test: add CARD-1514 M2 qualification publication red witnesses |
| `3dd236bb3bb4d843a686256c168ddc84318f1b50` | `d78bf0149032a25b4aae24feb28fb9c238ca543c` | fix: project qualification manifests once from a persistent context |
| `469bc5ba89296e4b7dafb074eb51e8f39b272aaf` | `85708e8cdaa5d4e6f8bb8fbcbadd14ca5b3651fe` | test(card-1514): witness successor valid-prefix seed and projection |
| `6e159cc9dc58e73dddf387c0e4b095599f156f47` | `2f5271483dd14577167f7710fd30dedff92c2b6d` | test(card-1514): expose seeded summary label regression |
| `c6d3f0a15457d071ca3001310a26466ee2ebec0a` | `6c7ddebdb622d7f8f24618fe5b06615c80286d7d` | fix(card-1514): retain seeded chunk and suite summary joins |
| `dc29c0f5c2466ae23929efe9e4dba37af8ca97d9` | `54b219da83d762b6cd51fc1288c8f9a2e8ce9cc0` | test(card-1514): compare real qualification verdicts under publication faults |
| `b8f90c61e5b622efa4100b78e17272b8ab0ea008` | `51b1c060b73120726cba713b74bfb11699bd1367` | test(card-1514): verify crash bytes and explicit summary timing roots |
| `f4d99876b6e25d876f6eace1fdee39d7b9c11506` | `ee108f3f08d436e36a3702e83ff56416ec6f73d1` | test(card-1514): isolate prefix setup from publication counters |
| `ed37b3696cb32ad2880940bbe422aaedafb85297` | `4f2ccf5e7c00dfe501b36e360f4525270d0b1c50` | test(card-1514): add portable publication samples and ordered replay |
| `bc9d6b1ca3bd9f29fcfd3ab29a0821fd3ad44e3b` | `4dc795a60b3b6121803278c1957a3e07edb5e2c5` | test(card-1514): avoid automatic input variable in replay |
| `1c6883ea30ee50388d71cb6131005063ee3c0ba6` | `49917f4e665d44b1850957ebed997c906033c647` | test(card-1514): count numeric journal records in scale inventory |
| `01587587bae45ac24f30d7ca35f797c1b8ec6c11` | `48499350d9fca945bc2e3a0e8ae05bcfc4c26b83` | fix(card-1514): preserve literal PowerShell regex in inventory filter |
| `499fda76b82bde219f7b2e9b499980131c80b980` | `c86841a9f030d18f07574a18b464a6a19094fa4d` | test: separate loaded-host deadlines and retain successor seed witnesses |
| `5109b4000e8d254e6bbd94cd4162df88afcb8516` | `25b0acc18232163c76a216ed6118b3d515b8075b` | test: count bytes from the active timing scanners |
| `5e7a2dc4d3222e1405330e208aaac9b22d8a458f` | `f5c39a1c534273a59d43fa7c729c0805279cd443` | test: separate the inherited 600-journal wall-clock proxy |
| `2a22f6ee0e2a446b59fe55035e74f4342785e091` | `35b2addc1481ece2df9dfff5ef885df0b729929d` | docs: record CP-8 retained timing investigation baseline |
| `0d72d646fe92f33c6bf2525e0b20dbe1bf742449` | `b549f8de73409903df50650254bbdb7ba67c79a5` | test: retain FIFO and launch timings with structural guards |
| `2dff9f80f21e2d05f7c910b995183e33cbab2ec1` | `e3fd5840294e467942140bafa3840438504234d4` | test: replace retained CP-8 speed proxies with structural guards |
| `138fd1b1cd4a7a206f8f427921d869da6e586e2b` | `4da3033fa3b93974b726c3f85883da7e8e4671c8` | test: retain hostile aggregation failure timing and verdict facts |
| `e5c05093773738dc8365f327af37a0824e7fde5d` | `be1aa91e6337d4f353dc24f4bef486f8d686c126` | test: bound hostile aggregation structurally after base reproduction |
| `1c67818f5cabecbf7d0a8017ca304054a48052f9` | `ab8f7b5de0b18e9e892e4dcba68dbdf722c07076` | test: distinguish refused opens from hostile reader streams |
| `93334d7d4611328cbe40acbc0540b60283e63f9a` | `24bb99e7415a48643b4b5b2a268ffa8f48223727` | docs: record CP-8 structural control results and restoration |
| `d5769e36f7f9b7d16a6afd1c0c4226299757b70d` | `45b144d0cf2d8f74b655189d38a2ff0858fb3b32` | test(card-1514): inject reader budgets and expose publication recovery regression |
| `c99d98e9f368e54b862a1fc56ca66e9bc80d72fd` | `c8a2131f9056c61795d4d24c22d7c1571769c726` | fix(card-1514): recover transient timing gaps on the next publication |
| `521c340c49bdf17e8e0b572b0931bc37dc0c8c27` | `6a03bf39590845824dbf285165d06f32ee3c0c08` | test(card-1514): isolate foreign-journal correctness from reader budget expiry |
| `829e7b8c55dc1ac995e8ae5e78b64c681da20822` | `59149d6910abfa96c8665c74391e88d2010b9239` | test(card-1514): bound unterminated TRX reads independently of wall time |
| `65beb323fdd6f88d0ea478d390be66ea2f8a67e7` | `9a1c1c1388d275ac3a838d05d6a1c00ad57274ed` | docs(card-1514): report repairs and outstanding full qualification |
| `f2bff4a5d50904521e6fbefca9314049d4fefbe8` | `ff446d2cb063493aab45a28f8e13f2243a6f7f58` | docs: retain verification interrupted by Debug lifetime ceiling |

### Skipped commits

Every entry below changed only `.antiphon/task-*.md`; these were the report-only skips required by the brief. Entries marked `-` by git cherry were already patch-equivalent on rc and were not selected.

| Full SHA | Subject | Reason |
|---|---|---|
| `9e485d2d1bf398025e473842bf63ec4d4f88785d` | docs: review CARD-1514 S1 instrumentation at 440b282af | report-only `.antiphon/task-*.md` |
| `d2ec4e6fbf19f28576ffd6b3aba12e7c44eb4272` | docs: record CARD-1514 S1 round-2 repair | report-only `.antiphon/task-*.md` |
| `6394a4e53e36fabe95878b4c9e4b4e739d5a5784` | docs: review CARD-1514 S1 round 2 at d2ec4e6fb | report-only `.antiphon/task-*.md` |
| `d12ccc80c3d75b9bc5d235a3dc44dcc9f12faf9d` | docs: record the S1 round-3 checkpoint and mutation results | report-only `.antiphon/task-*.md` |
| `43a6441a9d635155d5f4092bd95d100ec833800e` | docs: review CARD-1514 S1 round 3 at d12ccc80c | report-only `.antiphon/task-*.md` |
| `9f9a5aaa73c8dc80b57420ff6b129d33684a6088` | docs: record the S1 round-4 checkpoint and mutation results | report-only `.antiphon/task-*.md` |
| `db282ed7a7827feb38af67887a45f3399d999a1c` | docs: review CARD-1514 S1 round 4 at 9f9a5aaa7 | report-only `.antiphon/task-*.md` |
| `cbdf5f76bf4b14845f1e15fd8961065ace0c14c6` | docs: record the S1 round-5 checkpoint and mutation results | report-only `.antiphon/task-*.md` |
| `194705e096adbe801adda08a839249f75aa5c325` | docs: record the S3 hang-admission checkpoint | report-only `.antiphon/task-*.md` |
| `659dc8534906d1bbb60400e3b3301751e1dfc5e7` | docs: review CARD-1514 S3 at 194705e09 | report-only `.antiphon/task-*.md` |
| `d1d49d8906c814152cd61ef31600156d257eb2ff` | docs: record the S3 round-2 checkpoint and mutation results | report-only `.antiphon/task-*.md` |
| `c3adcdbf99313ef0d1ce8fd791a770a40501a29d` | docs: review CARD-1514 S3 round 2 at d1d49d890 | report-only `.antiphon/task-*.md` |
| `192f15240f757e04fb5fdb032e447f926e563757` | docs: record the S3 round-3 checkpoint and mutation results | report-only `.antiphon/task-*.md` |
| `190d9c17ed5baebd99bfd7834ffbbe878a5f39d5` | docs: record CARD-1514 M2 checkpoint inventory | report-only `.antiphon/task-*.md` |
| `a43e8ed04cc7f2d17d508ed5dd570b319e9b5f1d` | docs: CARD-1514 M1+M2 review report (task ea1c8636) | report-only `.antiphon/task-*.md` |
| `14fb68067167a4ec433d98511de69bb61eaa903d` | docs(card-1514): preserve fix-round receipts and timing failures | report-only `.antiphon/task-*.md` |
| `7661c3a4292e7030173841621cccf692610ca28d` | docs(card-1514): record portable scale replay controls and owed gates | report-only `.antiphon/task-*.md` |
| `1f6065cce08bc5ee74014076c90b0855044bc236` | docs: CARD-1514 final review of d99e4f23 at 7661c3a42 (task 93994c70) | report-only `.antiphon/task-*.md` |
| `eaea94cffa8363f6074d51e146916992545a32da` | report: record CP-8 retained timing failures and incomplete gate | report-only `.antiphon/task-*.md` |
| `a7cb0a929734eb1d10a160b1588c286647804618` | report: preserve CP-8 stress failures and incomplete verification gate | report-only `.antiphon/task-*.md` |

## Series 2 — Remote custody assertion

| Original full SHA | Port full SHA | Subject |
|---|---|---|
| `e4239eebba41642916935d64b3d034bd7c37c3d3` | `49322b8e0a40a8c94724385b4103d84f15b8fb61` | test: align remote custody residue guard with namespace counter |

## Conflicts

One modify/delete conflict: `.antiphon/task-79c0c349.md` in `65beb323fdd6f88d0ea478d390be66ea2f8a67e7`. Its creation was in a skipped report-only predecessor. Retained the incoming report file alongside the code-bound investigation update; the next mixed report/investigation commit applied cleanly. There were no production, test, plan or build-definition conflicts. The newer rc edits remained intact.

## Land graph approval

Real `scripts/audit-land-verification-graph.ps1 -Check`, run under `scripts/build-slot.ps1`, exit 0; both evaluator passes and canary comparison approved. Recorded and computed digest both `E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806`, recorded and computed count both 69. `-Update` ran zero times. Manifest, evaluation, pins and allow-list unchanged. Audit-tool build exit 0, zero errors. Granted audit-build lease `8d5bbe88-cf7f-401d-8a6e-523f2c8a724f` (wait 30s, held 7s), check lease `920d6b55-5f66-4e8c-9d9f-ee5d2a782c69` (wait 45s, held 26s). Logs in `.antiphon/rc47-evidence/`.

## Migration note

The selected delta adds or changes no EF migration or model snapshot. The source ancestor contains `20261028170715_AddAgentSessionCodexTokenCount` (from original `3bbcb3a4c09d055beae16d0d1192a0f4691025e1`), already present on starting rc; no duplicate migration was added. rc newest is `20261030141651_AddSessionExitSignalEvidence`, retained unchanged.

## Prior verification limits

Read full `origin/feat/card-task-c171db78:.antiphon/task-c171db78.md`. Its verified source is `b831ee9e150783f943a32a59f82c12b0cde10ad4`, containing final code `829e7b8c55dc1ac995e8ae5e78b64c681da20822`. Portable CP-1..9 matrix passed 38/38 (8,6,6,1,3,10,2,1,1); loaded CP-8 passed 5/5. Strict-alone certification remains unverified under the caller shared-host disposition. These are prior-source results, not relabeled as results of this port. Production shared 200ms reader deadline is unchanged. Native Windows CP-10/11 remain owed.

## Verification of this port

Built tests once at verified source above, `TEMP=/tmp`, `UseAppHost=false`, isolated absolute OutputPath/ArtifactsPath under `tests/Antiphon.Tests/bin-rc47-tests/`, through build-slot. Exit 0, 1,425 warnings, zero errors; build elapsed 307.93s. Lease `046083bc-3a0a-46b2-92ec-1e616b8ad17b`, waited 30s, held 309s. Every test invocation uses `dotnet exec` of that compiled DLL (no build) through a fresh build-slot wrapper; rows run sequentially. This Merge brief explicitly selects these narrow class/case regressions instead of rerunning the full Code checkpoint matrix or assembly suite. Timing classes run alone in this task; no strict-exclusive shared-host claim.

**73 executed, 73 passed, zero failed/skipped across the required selections.**

| Class / selection | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| `Antiphon.Tests.TestHelpers.RcManifestJournalTests` | 4 | 4 | 0 | 0 |
| `Antiphon.Tests.TestHelpers.RcManifestRecoveryTests` | 4 | 4 | 0 | 0 |
| `Antiphon.Tests.TestHelpers.RcManifestIntegrationTests` | 6 | 6 | 0 | 0 |
| `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` | 39 | 39 | 0 | 0 |
| `Antiphon.Tests.Scripts.PosixHarnessToolTests` | 7 | 7 | 0 | 0 |
| `Antiphon.Tests.Scripts.RemoteScriptContractTests` (custody case only) | 1 | 1 | 0 | 0 |
| `Antiphon.Tests.Scripts.RcRosterRepairTests` | 3 | 3 | 0 | 0 |
| `Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests` (four Linux cases only) | 4 | 4 | 0 | 0 |
| `Antiphon.Tests.Scripts.C1514LandingSplitBudgetTests` | 5 | 5 | 0 | 0 |

The full CP-4 class selection executed 39 tests; the full CP-8 class selection executed seven. The repaired CP-8 exact method passed within the latter. Census executed C1404_Group10MeasuredBudget, C1577_Group71MeasuredBudget, C1584_E2EExpandedBudget, C1410_MeasuredCostGroups and G2865_Group28Group65MeasuredBudget, all passed. No unrelated pre-existing assertion red was observed.

### Preserved unsuccessful selector attempt

Before the successful Linux selection, the initial method-OR filter omitted the mandatory trailing `*` on each operand. TUnit exit 8, 0 executed/passed/failed/skipped. This was invalid discovery, not a product assertion failure or a green. Its original `windows-linux/run.trx` and log remain. After consulting the documented pinned-runner syntax, reran only that missing selection once with canonical starred operands, in `windows-linux-corrected/`: 4/4 passed, zero failed/skipped. The earlier six passing selections were not repeated. Census ran once afterward. No source, deadline or assertion was altered.

### Exact selections and granted leases

| Row | Filter | Lease | Admission seconds | Held seconds |
|---|---|---|---:|---:|
| manifest-core | `/*/Antiphon.Tests.TestHelpers/(RcManifestJournalTests*)\|(RcManifestRecoveryTests*)/*` | `f3eaa605-5799-4430-bd77-0b59b70a4dcb` | 60 | 212 |
| manifest-integration | `/*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/*` | `db2deadd-f47f-4507-89a7-77b6d1f554b1` | 90 | 128 |
| classification | `/*/Antiphon.Tests.TestHelpers/TestClassificationPolicyTests/*` | `1699ecf9-f9e8-48a3-bbf8-2db52e5209db` | 30 | 93 |
| posix | `/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/*` | `a4f213fb-8ab6-4773-bcdd-d16b0527e11d` | 826 | 154 |
| remote | `/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/Custody_residue_counts_subdirectories_only` | `8aed4564-766c-46e5-b9ca-af78f6ec5dd2` | 405 | 3 |
| roster | `/*/Antiphon.Tests.Scripts/RcRosterRepairTests/*` | `b7cc991d-bf33-4b59-8915-971dd027372f` | 240 | 18 |
| windows-linux | `/*/Antiphon.Tests.Scripts/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform)\|(C1210_Native_linux_entries_guard_before_work)\|(C1210_Roster_closure_matches_source_gates)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes)` | `5a95f5a9-0edd-4134-a5fe-cc865cd52e40` | 150 | 2 |
| windows-linux-corrected | `/*/Antiphon.Tests.Scripts/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform*)\|(C1210_Native_linux_entries_guard_before_work*)\|(C1210_Roster_closure_matches_source_gates*)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes*)` | `fa26b0d4-6619-4bb8-8959-507d32809399` | 406 | 22 |
| census | `/*/Antiphon.Tests.Scripts/C1514LandingSplitBudgetTests/*` | `e6fca0c8-08eb-47f0-bb5f-2b984cd6cdd5` | 105 | 62 |

Total test-wrapper admission 2312s; total held 694s, including the 2s zero-discovery attempt. The aggregate wall exceeds 30 minutes because of these explicit broker waits, including the prolonged POSIX queue; individual selections stayed narrow, no build/test bound was widened and no suite run was added.

All build/test children were awaited. Source remained tracked-clean at the producing build and across the test sequence; source/build binding is the frozen full SHA above and one successful producing build, with no rebuild or tracked mutation. Generated JSON/TRX/log/HTML evidence remains ignored under `/work/worktrees/task-8f0a71bb/.antiphon/rc47-evidence/`.

### Reproduction

Use the successful filter rows; the unstarred `windows-linux` row is historical zero-discovery evidence only.

```sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc47-tests-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=/work/worktrees/task-8f0a71bb/tests/Antiphon.Tests/bin-rc47-tests/ --property:ArtifactsPath=/work/worktrees/task-8f0a71bb/tests/Antiphon.Tests/bin-rc47-tests/artifacts/ --property:UseArtifactsOutput=true --property:UseAppHost=false --nologo
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc47-row -- dotnet exec /work/worktrees/task-8f0a71bb/tests/Antiphon.Tests/bin-rc47-tests/Antiphon.Tests.dll --treenode-filter '<one exact filter from table>' --report-trx --report-trx-filename run.trx --results-directory '<fresh evidence directory>'
```

## Publication and cleanup

After all commands exited, removed only these two producer-owned alternate output roots by exact literal path:

- `/work/worktrees/task-8f0a71bb/tests/Antiphon.Tests/bin-rc47-tests`
- `/work/worktrees/task-8f0a71bb/tools/Antiphon.LandGraphAudit/bin-rc47-audit`

The only subsequent tracked change is this narrative report; product/test/definition source remains exactly the verified source. Final caller report carries the full pushed report-commit SHA, the fast-forward rc publication result and the fresh `git ls-remote` confirmations. No graph digest or allow-list record was regenerated.

--- next stage ---
next: port to master after CARD-1610 lands
handoff: Port these two series to master only after CARD-1610 lands; retain the prior strict-alone and native-Windows qualification limits.
artifact: the final rc tip SHA in the caller report
