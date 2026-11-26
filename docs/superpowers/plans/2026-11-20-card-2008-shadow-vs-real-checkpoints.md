# CARD-2008 shadow Passed versus the real native check

Code task f6865b8e. Base 2c662e3bda11cd36dd4a98ba191f20079bea9cf9. Branch feat/card-task-f6865b8e. Worktree /work/worktrees/ws-12.

The recorded native chunk outcome is Passed only when `Get-NightlyRcChunkOutcome` says Passed and the real `ConvertTo-NightlyNativeSuiteVerdict` reports `testsPassed` and `coverageComplete` and `Test-NightlyFreshEvidence` on the chunk log reports `Ok`. Anything unreadable is NotRun. The shadow envelope stays `effectiveMode` Full, `gateCredit` false, envelope `coverageComplete` false, `bindingState` diagnostic-only, `publish` false. Nothing new is wired for credit, activation, or publish.

Windows is UNOBSERVED. This Linux task does not restart.

A-5 is a port note only. This slice does not change `RcPort.cs`, `RcShadowSample.cs`, `RcDirtyOrder.cs`, `rc-shadow-sample.ps1`, or `rc-dirty-order.ps1`.

## Port notes (A-5, not this slice)

The C# port of `rc-run-evidence.ps1` must keep or tighten each of these. PowerShell behavior is the witness; do not widen it here.

1. PowerShell `ConvertFrom-Json` throws on case-variant duplicate keys (`executed` with `Executed`), which is NotRun `results-unparseable`. `System.Text.Json` accepts duplicates and lets the last win. The port must refuse duplicate and case-variant keys.
2. Upper-case keys such as `EXECUTED` and `ROWS` are accepted, because PowerShell member access ignores case. A case-sensitive port is stricter and must be a deliberate choice.
3. A row Outcome given as `["Passed"]` becomes `Passed` through `[string]`. A `rows` value given as an object instead of an array is accepted as one row. The port should refuse non-string Outcome and Identity values and non-array rows.
4. `Test-NightlyRcWholeNumber` rejects bools and arrays, and accepts the string `1` and the double `1.0`. The port must keep the bool and array rejection.
5. `ConvertFrom-Json` turns an ISO-looking Identity into a DateTime. `[string]` then reformats it, so it no longer matches a roster. The effect is stricter. The port should read raw strings.
6. `rows` set to null, `[]`, or `[null]` all give NotRun. A null roster becomes `@()` and must never load the production roster. A roster with Platform `''` matches nothing.
7. Duplicate JSON row identities are not refused. The TRX path refuses duplicate testIds. The port should refuse duplicates on the JSON path too.
8. A lower-case row outcome `passed` is accepted through `ClassifyOutcomes`. The summary outcome is compared case-sensitively, so `completed` gives Failed `summary-outcome`. Keep both.

## Verification design

V-1. A stale TRX whose rows would otherwise pass is shadow-parse Passed and real `stale-timestamp`. The recorded outcome is not Passed.
V-2. A TRX with only class A when classes A and B are required is shadow-parse Passed and real `missing-required-class`. The recorded outcome is not Passed.
V-3. A TRX/diagnostic method mismatch is shadow-parse Passed and real `trx-diagnostic-mismatch`. The recorded outcome is not Passed.
V-4. A passing TRX with no discovery file is shadow-parse Passed and real `missing-discovery`. The recorded outcome is not Passed.
V-5. Counters `failed="x"` and `failed="+0"` are NotRun `results-summary-unreadable`, not Passed.
V-6. A fresh TRX with matching discovery, execution diagnostic, and log stays Passed on both the real native verdict and the recorded outcome.
V-7. A null native verdict or a null freshness result is NotRun `results-unreadable`.
V-8. `nightly-tests-impl.ps1` calls `Resolve-NightlyRcChunkOutcome` after `ConvertTo-NightlyNativeSuiteVerdict` and `Test-NightlyFreshEvidence`, and still registers one chunk revision.
R-1. Existing `RcRunEvidenceTests` stay green, including `ShadowPassedIsAtLeastTheRealCheck` against `ConvertTo-NightlyNativeSuiteVerdict`.
R-2. `TestClassificationPolicyTests` once (42 executed).
R-3. Build `Antiphon.DerivedImpactIndex`, then `DerivedImpactIndexTests` alone (27 executed). Regenerate `tests/derived-impact-index.json` only when the driver reports a byte change, as its own last commit, run twice (changed, then unchanged).
R-4. Guard union below. C1729 stays VIOLATIONS 0 LISTED 162. Linux skips `C1210_Windows_linux_only_outcomes_are_exact`.
R-5. `scripts/audit-land-verification-graph.ps1 -Check`. An inherited mismatch is a warning. A mismatch this diff causes is a land-graph refresh commit, then the closed list is rerun.
R-6. Recompute nightly pins and report them. Protected inputs are the list in `scripts/test-live-lane-run.ps1`. `nightly-tests-impl.ps1` is not one of them. Re-pin `PinnedInputDigest` in its own last commit only if a protected input changes. Committed `policyHash` is `8c376b03c4e012e3700de15e8b99bdbe851187bce4d3c5defe2b6b6df1f782e2`. Committed `PinnedInputDigest` is `99297dde12befbe23fd15dff6f60b3c99c0df82bca5a2817f2c6bef5e4fef6df`.
R-7. `scripts/check-evidence-diff.ps1` over base `2c662e3bda11cd36dd4a98ba191f20079bea9cf9`..HEAD.
R-8. `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` has no conflict.

No new positive control. Mutation is waived (operator 2026-11-12). Existing PC rows stay pending for SourceLanding Mutation. The Unit lane is nightly/rc.

The red commit adds `ShadowPassedMatchesNativeBranch` against the base library. At that commit the four membership/freshness rows fail with `shadow=Passed` and `real=failure`, and the non-numeric counter rows fail with `shadow=Passed`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | shadow-bound | `tests/Antiphon.Tests -> bin-c2008/` | rc-run-evidence | `/*/*/RcRunEvidenceTests/*` | V-1 through V-8, R-1 | 0 failed, 0 skipped | 10 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | shadow-bound | CP-1 | policy | `/*/*/TestClassificationPolicyTests/*` | R-2 | 0 failed, 0 skipped | 42 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | shadow-bound | n/a | derived-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-3 | exit 0 | n/a | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | shadow-bound | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-3 | 0 failed, 0 skipped | 27 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | shadow-bound | CP-1 | guard-union | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-4 | 0 failed; Linux skips C1210_Windows_linux_only_outcomes_are_exact; C1729 VIOLATIONS 0 LISTED 162 | 41 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | shadow-bound | n/a | land-audit-build | `dotnet build tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj -c Release -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-5 | exit 0 | n/a | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | shadow-bound | n/a | land-graph-check | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Release/net10.0/Antiphon.LandGraphAudit.dll` | R-5 | exit 0, or an inherited mismatch named in the log | n/a | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-8 | shadow-bound | n/a | nightly-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $pin = "99297dde12befbe23fd15dff6f60b3c99c0df82bca5a2817f2c6bef5e4fef6df"; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); if ($d -ne $pin) { throw ("input digest drift computed=" + $d) }'` | R-6 | exit 0, printed hashes match the committed pin | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-9 | shadow-bound | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 2c662e3bda11cd36dd4a98ba191f20079bea9cf9 -HeadRef HEAD` | R-7 | exit 0 | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-10 | shadow-bound | n/a | merge-tree | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-8 | exit 0, tree oid printed, no conflict | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |

### Cost

Ordinary floor is 66 minutes (the `EstimatedMinutes` sum). The green run is one isolated `Antiphon.Tests` build plus the rows above. CP-1 includes `OneEvidenceRootPerRun`.

### Restart

Restart target: none. `scripts/lib/nightly-tests-impl.ps1` and `scripts/lib/rc-run-evidence.ps1` are on the startup census. A later rc pick owes the Windows startup probe. This Linux task does not run it.
