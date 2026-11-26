# CARD-2000 one copy-p95 source for the land-graph clock

Code task `5444bf45`, base master `36d0c758298b85516a29ee05a13225c15e14fc23`, branch `feat/card-task-5444bf45`.

CARD-1991 review `08f442c1c957ddb591643e6e32cdf6118d258235` advisories A-1 and A-2. No new positive control. Mutation execution is waived (operator 2026-11-12). SourceLanding still owns any later method-scoped mutation.

## Design

`--print-budget` with no `--windows` was choosing the host copy p95 itself (`MeasuredCopyP95Milliseconds` or `MeasuredLinuxCopyP95Milliseconds`). The production clock chooses the same pair in `EvaluationBudget.HostCopyP95Milliseconds`, then `CopyTimeout`. R-1 pins the printed host copy at 8. V-1 (`UnchangedDigestEightSecondCopyStaysApproved`) only checks that an 8 s copy is admitted, so a wider production clock passed both.

S2 makes the omitted-`--windows` path read `EvaluationBudget.HostCopyP95Milliseconds`. An explicit `--windows true|false` still selects that operating system's measured constant, so the R-1 override cases stay pinned. `HostCopyP95IsTheDefaultPrintAndProductionClock` asserts the printed host `copy=` equals `CopyTimeout` seconds, and that `Program.cs` uses that property when `windows` is null.

A local mutation, not a commit, widened `HostCopyP95Milliseconds` to 15000 on Linux (`CopyTimeout` 30 s) while `Program.cs` still used the constants. The new test failed on the equality (`copy=` stayed 8). R-1 and V-1 stayed green. The mutation was reverted. At S1 the same test is red because `Program.cs` does not yet call the property. S2 makes it green.

The rest of `LandGraphEvaluationTests` (canary, filesystem, kill, SDK, and graph-change fixtures) does not read the host copy selector. Those classes stay off this short list. The invariant is the default printed copy seconds and `CopyTimeout`.

## A-2 plans left unchanged

`ReportValidator` refuses a row with `skipped != 0`, so a row that contains `C1210_Windows_linux_only_outcomes_are_exact` cannot sit in a whole-run validated receipt. This plan excludes that method by operand (CP-8) and does not give it a skip row. These other plans still put the whole `WindowsUnitPlatformAdmissionTests` class on a checkpoint row. They were not edited:

- `docs/superpowers/plans/2026-10-13-card-1210-s3-d1-repair.md`
- `docs/superpowers/plans/2026-10-13-card-1210-test-design.md`
- `docs/superpowers/plans/2026-10-14-card-1265-windows-skip-roster.md` (CP-1; CP-2 is already method-scoped)
- `docs/superpowers/plans/2026-10-29-card-1610-failed-task-live-session-plan.md`
- `docs/superpowers/plans/2026-11-06-card-1544-source-closure-verification.md`
- `docs/superpowers/plans/2026-11-09-card-1832-platform-taxonomy.md`
- `docs/superpowers/plans/2026-11-11-card-1830-gitinprocess-tests.md`
- `docs/superpowers/plans/2026-11-14-card-0824-r2-checkpoints.md`
- `docs/superpowers/plans/2026-11-14-g90-operator-abandon.md`
- `docs/superpowers/plans/2026-11-15-card-1836-s7a-checkpoints.md`
- `docs/superpowers/plans/2026-11-15-card-1906-compiler-policy-plumbing.md`
- `docs/superpowers/plans/2026-11-15-card-1925-production-clock-witness-plan.md`
- `docs/superpowers/plans/2026-11-15-card-1943-operator-abandon.md`
- `docs/superpowers/plans/2026-11-16-card-1836-s5c-checkpoints.md`
- `docs/superpowers/plans/2026-11-16-card-1883-reflection-witness-plan.md`
- `docs/superpowers/plans/2026-11-16-card-1893-collector-plan.md`
- `docs/superpowers/plans/2026-11-16-card-1954-evidence-vs-fixture-separation-plan.md`
- `docs/superpowers/plans/2026-11-16-card-1958-receipt-placement-skip.md`
- `docs/superpowers/plans/2026-11-17-card-1878-advisories.md`
- `docs/superpowers/plans/2026-11-17-card-1893-s0-checkpoints.md`
- `docs/superpowers/plans/2026-11-17-card-1948-attention-page-counts-plan.md`
- `docs/superpowers/plans/2026-11-17-card-1961-checkpoints.md`
- `docs/superpowers/plans/2026-11-18-card-1893-checkpoints.md`
- `docs/superpowers/plans/2026-11-18-card-1896-checkpoints.md`
- `docs/superpowers/plans/2026-11-18-card-1976-checkpoints.md`
- `docs/superpowers/plans/2026-11-18-card-1978-checkpoints.md`
- `docs/superpowers/plans/2026-11-18-card-1981-s2-checkpoints.md`
- `docs/superpowers/plans/2026-11-18-card-1984-r2-etxtbsy-amendment.md`
- `docs/superpowers/plans/2026-11-18-card-1986-checkpoints.md`
- `docs/superpowers/plans/2026-11-18-claude-cli-2-1-327-checkpoints.md`
- `docs/superpowers/plans/2026-11-19-c488-fix-checkpoints.md`
- `docs/superpowers/plans/2026-11-19-card-1968-checkpoints.md`
- `docs/superpowers/plans/2026-11-19-card-1991-checkpoints.md`
- `docs/superpowers/plans/2026-11-19-card-1995-checkpoints.md`
- `docs/superpowers/plans/2026-11-19-incremental-rc-qualification-plan.md`

`docs/superpowers/plans/2026-11-19-longpaths-checkpoints.md` and `docs/superpowers/plans/2026-11-19-card-1972-checkpoints.md` already omit the class. `docs/superpowers/plans/2026-11-15-card-1820-s1r4-checkpoints.md` already splits the six portable methods from the skip.

## Verification design

- V-1. `HostCopyP95IsTheDefaultPrintAndProductionClock`: default `copy=` equals `EvaluationBudget.CopyTimeout` seconds, and `Program.cs` reads `HostCopyP95Milliseconds` when `windows` is null. Red at S1 on the source pin. The local 15000 ms mutation is red on the equality while R-1 and the 8 s copy witness stay green.
- R-1. `EvaluationBudgetDerivationUsesTwiceMeasuredP95AndWindowsFloor` stays green (printed host copy 8, Windows 68).
- R-2. `UnchangedDigestEightSecondCopyStaysApproved` stays green (an 8 s copy is still admitted).
- R-3. `TestClassificationPolicyTests` once (42 executed).
- R-4. Build `Antiphon.DerivedImpactIndex`, then `DerivedImpactIndexTests` alone (27 executed). Regenerate `tests/derived-impact-index.json` only if the driver reports a byte change, as its own last commit, run twice (changed, then unchanged).
- R-5. Guard union without `WindowsUnitPlatformAdmissionTests`: classification guard, slow-registry merge, slow tripwire, native Linux policy witness, mechanical PC contract, task platform guidance, C1729 layering, lane category. Floor 39 executed, 0 skipped. `TaskPlatformGuidanceTests` is 5 methods; the four `RunnerDefaultGuidanceTests` methods share that file and are a different class, so they are outside this filter. C1729 stays VIOLATIONS 0 LISTED 162.
- R-6. The six portable `WindowsUnitPlatformAdmissionTests` methods, excluding `C1210_Windows_linux_only_outcomes_are_exact`. Floor 6 executed, 0 skipped.
- R-7. Release-build `Antiphon.LandGraphAudit`, then `scripts/audit-land-verification-graph.ps1 -Check` approves digest `C5B7C0CEC63A241101BD6ED60C1C293CF797CB50F4B111FFED169B4529EC43FC` count 80. This change does not edit a proj, props, targets, or sln file. A mismatch gets one `scripts/land-graph-land-refresh.ps1 -Base 36d0c758298b85516a29ee05a13225c15e14fc23` commit and the closed list is rerun at that SHA.
- R-8. Recompute nightly pins and report them. Do not re-pin unless a guard fails. Committed `policyHash` is `0396d6a543917cd112d2f28dd0adfe8bd80ae549f2803df2bb866a751b261542`. Committed `PinnedInputDigest` is `a08d61c11698379cd0b1e464c3032681d9cd10ea8eb78fb0f40255838e32050c`. Legacy exclusion digest stays `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`.
- R-9. `scripts/check-evidence-diff.ps1` over base `36d0c758298b85516a29ee05a13225c15e14fc23`..HEAD.
- R-10. `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` has no conflict.

S1 is the red witness commit. S2 is the `Program.cs` change. CP-1 is run alone at S1 and must fail. The closed green run is the whole table at the S2 SHA (or the refresh SHA if R-7 needs it).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c2000-proof/ | host-copy-clock | `/*/*/LandGraphEvaluationTests/HostCopyP95IsTheDefaultPrintAndProductionClock` | V-1 | 1 executed, 0 failed | 1 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | all | CP-1 | budget-derivation | `/*/*/LandGraphEvaluationTests/EvaluationBudgetDerivationUsesTwiceMeasuredP95AndWindowsFloor` | R-1 | 1 executed, 0 failed | 1 | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | all | CP-1 | eight-second-copy | `/*/*/LandGraphEvaluationTests/UnchangedDigestEightSecondCopyStaysApproved` | R-2 | 1 executed, 0 failed | 1 | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | all | CP-1 | policy | `/*/*/TestClassificationPolicyTests/*` | R-3 | 42 executed, 0 failed | 42 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | all | n/a | derived-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-4 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | all | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-4 | 27 executed, 0 failed | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | all | CP-1 | guard-union | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-5 | 39 executed, 0 failed, 0 skipped | 39 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | all | CP-1 | windows-portable | `/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform*)\|(C1210_Native_linux_entries_guard_before_work*)\|(C1210_Roster_closure_matches_source_gates*)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes*)\|(C1210_Host_roster_matches_windows_shaped_observation*)\|(C1226_Pin_inspection_methods_skip_off_linux*)` | R-6 | 6 executed, 0 failed, 0 skipped | 6 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | all | n/a | land-audit-build | `dotnet build tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj -c Release -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-7 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | all | n/a | land-graph-check | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Release/net10.0/Antiphon.LandGraphAudit.dll` | R-7 | exit 0, approved, digest C5B7C0CEC63A241101BD6ED60C1C293CF797CB50F4B111FFED169B4529EC43FC count 80 | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-11 | all | n/a | nightly-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $pin = "a08d61c11698379cd0b1e464c3032681d9cd10ea8eb78fb0f40255838e32050c"; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); if ($d -ne $pin) { throw ("input digest drift computed=" + $d) }'` | R-8 | exit 0, printed hashes match the committed pin | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-12 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 36d0c758298b85516a29ee05a13225c15e14fc23 -HeadRef HEAD` | R-9 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-13 | all | n/a | merge-tree | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-10 | exit 0, tree oid printed, no conflict | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 80 minutes (the `EstimatedMinutes` sum). The green run is one isolated `Antiphon.Tests` build plus the rows above.
