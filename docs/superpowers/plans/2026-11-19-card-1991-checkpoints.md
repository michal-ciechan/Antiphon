# CARD-1991 land-graph check budget under load, checkpoints

Code task `6e03880d`, base master `42d7af1f33cfd0236f3bbcdcea57c60bf948e98d`, branch `feat/card-task-6e03880d`. The `-Check` row stays. It still evaluates when the recorded digest matches. The Linux canary-copy clock was 5 s (`TwiceSeconds(2253)`). Under load that clock refused an already-approved graph (`evaluating Antiphon.sln copy exceeded 5 s`, CARD-1976 CP-6 and merge 259).

## Measurement

Twenty `scripts/audit-land-verification-graph.ps1 -Check` runs on this host, 2026-11-19, tool built from base `42d7af1f` with the copy clock raised only so the phase was not censored at 5 s. Each run waited until the 1-minute load was below 16. Every run exited 0 with digest `C5B7C0CEC63A241101BD6ED60C1C293CF797CB50F4B111FFED169B4529EC43FC` count 80. End-of-run load climbed as high as 31.31.

Copy phase milliseconds, sorted: 1558, 1745, 1796, 2036, 2036, 2238, 2265, 2290, 2291, 2315, 2532, 2537, 2540, 2611, 2674, 3364, 3575, 3588, 3751, 3857.

Nearest-rank for n=20: p50 is rank `ceil(0.50*20)=10` = 2315 ms. p95 is rank `ceil(0.95*20)=19` = 3751 ms. `TwiceSeconds(3751) = (3751*2+999)/1000 = 8`.

What dominates a successful check is solution evaluation. Pass 1 was about 10–23 s (max 23044 ms). Pass 2 was about 5–15 s (max 14939 ms). The copy in this sample was 1.6–3.9 s and did not hit 5 s. Wall time above the three phases is a few seconds (git, pre-check, startup). The red receipts are the censored tail of the 5 s copy clock, not this sample's median. Pass clocks stayed under the existing 45 s Linux pass budget, so that budget stays.

## Design

Keep evaluation. A digest match does not skip it: SDK imports and globs are visible only while the solution is evaluated (`CheckDifferentSdkRequiresEvaluation`, `docs/orchestration-loop.md`).

Set the Linux copy budget to 8 s, which is 2× the measured p95 (`docs/test-performance-standards.md` R5) and exactly the synthetic witness bound. New constant `EvaluationBudget.MeasuredLinuxCopyP95Milliseconds = 3751`, used only when the host is not Windows, for `CopyTimeout`, `ToolCeilingSeconds`, and the `--print-budget` default. `MeasuredCopyP95Milliseconds` stays 2253 so the Windows copy budget stays `2253 * 30 / 1000 = 68` s. Linux tool ceiling becomes `45*2+8+60 = 158` s. Windows ceiling is unchanged.

`ManualPhaseClock` admits a phase when elapsed `<=` budget, so an 8 s copy equals the new Linux budget and is approved. The same call against the 5 s clock returns exit 2 and `copy exceeded 5 s`. That is the red witness on this Linux host. Windows already admits 8 s inside 68 s, so the red failure is Linux-only; after the fix both hosts approve.

A committed project edit on a fixture still fails `-Check` at the pre-check (`manifest does not match the selected graph`, exit 1) before the evaluation record is compared. That is the graph-change witness.

No new positive control. Mutation execution is waived (operator 2026-11-12). SourceLanding still owns any later method-scoped mutation.

## Verification design

- V-1. `UnchangedDigestEightSecondCopyStaysApproved`: the production copy clock approves an 8 s copy, pass 2 runs, one `approved` line. Red at S1 on Linux: exit 2, `evaluating Antiphon.sln copy exceeded 5 s`.
- V-2. `RealGraphChangeStillRefusesCheck`: a committed project change makes `-Check` exit 1 with `manifest does not match the selected graph` and no approval.
- R-1. `EvaluationBudgetDerivationUsesTwiceMeasuredP95AndWindowsFloor` keeps the 2253 Windows base and the explicit `--print-budget` cases, and records Linux host copy 8 from `MeasuredLinuxCopyP95Milliseconds = 3751`.
- R-2. `TestClassificationPolicyTests` once (42 executed).
- R-3. Build `Antiphon.DerivedImpactIndex` in its own command, then `DerivedImpactIndexTests` alone (27 executed). Regenerating `tests/derived-impact-index.json` is not required: this change does not edit its inputs.
- R-4. The remaining named guard union, one parenthesized filter: classification guard, slow-registry merge, slow tripwire, native Linux policy witness, mechanical PC contract, task platform guidance, C1729 layering, lane category, Windows unit admission. Floor 48 executed. `C1210_Windows_linux_only_outcomes_are_exact` skips on Linux.
- R-5. Release-build `Antiphon.LandGraphAudit` into the native default output, then `scripts/audit-land-verification-graph.ps1 -Check` approves digest `C5B7C0CEC63A241101BD6ED60C1C293CF797CB50F4B111FFED169B4529EC43FC` count 80. Definition inputs are proj/props/targets/sln, so this docs-and-tool-source change does not refresh the manifest. A mismatch gets one `scripts/land-graph-land-refresh.ps1 -Base 42d7af1f33cfd0236f3bbcdcea57c60bf948e98d` commit and the closed list is rerun at that SHA.
- R-6. Recompute nightly pins and report them. `Read-NightlyExecutionPolicy` throws when the file hash disagrees with its body. `scripts/test-live-lane-run.ps1 -PrintInputDigest` must equal committed `c7b07add99e7176abe148bf5d3c5cdd3010bcff0e39a2f66a2ca56b6753a31b3`. The legacy exclusion digest stays `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a` via R-4. Re-pin only if a guard fails, in its own commit.
- R-7. `scripts/check-evidence-diff.ps1` over base `42d7af1f33cfd0236f3bbcdcea57c60bf948e98d`..HEAD.
- R-8. `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` has no conflict.

S1 is the red witness commit. S2 is the budget fix. CP-1 is run alone at S1 and must fail for the 5 s copy text. The same row is green after S2. The closed green run is `--after all` at the S2 SHA (or the refresh SHA if R-5 needs it).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c1991-proof/ | eight-second-copy | `/*/*/LandGraphEvaluationTests/UnchangedDigestEightSecondCopyStaysApproved` | V-1 | 1 executed, 0 failed | 1 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | all | CP-1 | graph-change | `/*/*/LandGraphEvaluationTests/RealGraphChangeStillRefusesCheck` | V-2 | 1 executed, 0 failed | 1 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | all | CP-1 | budget-derivation | `/*/*/LandGraphEvaluationTests/EvaluationBudgetDerivationUsesTwiceMeasuredP95AndWindowsFloor` | R-1 | 1 executed, 0 failed | 1 | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | all | CP-1 | policy | `/*/*/TestClassificationPolicyTests/*` | R-2 | 42 executed, 0 failed | 42 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | all | n/a | derived-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-3 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | all | CP-1 | derived-index | `/*/*/DerivedImpactIndexTests/*` | R-3 | 27 executed, 0 failed | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | all | CP-1 | guard-union | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-4 | 48 executed, 0 failed, 1 Linux skip | 48 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | all | n/a | land-audit-build | `dotnet build tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj -c Release -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-5 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | all | n/a | land-graph-check | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Release/net10.0/Antiphon.LandGraphAudit.dll` | R-5 | exit 0, approved, digest C5B7C0CEC63A241101BD6ED60C1C293CF797CB50F4B111FFED169B4529EC43FC count 80 | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | all | n/a | nightly-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $pin = "c7b07add99e7176abe148bf5d3c5cdd3010bcff0e39a2f66a2ca56b6753a31b3"; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); if ($d -ne $pin) { throw ("input digest drift computed=" + $d) }'` | R-6 | exit 0, printed hashes match the committed pin | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-11 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 42d7af1f33cfd0236f3bbcdcea57c60bf948e98d -HeadRef HEAD` | R-7 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-12 | all | n/a | merge-tree | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-8 | exit 0, tree oid printed, no conflict | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 77 minutes (the `EstimatedMinutes` sum). The green run is one isolated `Antiphon.Tests` build plus the rows above.
