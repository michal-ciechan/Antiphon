# FT-2 checkpoints (Code task df40f8cb)

Closed list for the docs-only target-manifest skip. The first row's cold build uses
`EstimatedMinutes` 15. Later rows reuse that build. Positive controls stay pending.
Mutation is waived (operator 2026-11-12). Windows is unobserved: `WindowsUnitPlatformAdmissionTests`
is not in this list.

`LandNonCodeSkipTests` also holds the C1409 audit-evaluator methods. Those are not this
slice. The production graph check is the audit command row. CP-3 is the existing policy
skip, the registry match, and the manifest-parse witnesses.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | FT-2 | tests/Antiphon.Tests -> bin-ft2/ | ft2-witness | `/*/*/LandNonCodeSkipTests/(C_FT2*)\|(C1380_TargetGraphDrift*)` | FT-2 (a) current target manifest skips, (b) one build-definition byte builds, (c) candidate graph differs, (d) stale malformed missing and unreadable manifests build, (e) embedded skip stays non-code, plus the stale-manifest drift case | 12 executed, 0 failed/skipped | 12 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | FT-2 | CP-1 | ft2-publication | `/*/*/AgentTaskLandingStateTests/C1380_NonCode*` | publication accepts docs-skip-target-manifest on pinned master schema 3 | 7 executed, 0 failed/skipped | 7 | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | FT-2 | CP-1 | ft2-policy | `/*/*/LandNonCodeSkipTests/(C1380*)\|(C1385_Registry*)\|(C1385_Unrelated*)\|(C1390_M*)` | existing land-verification policy skips and the registry graph witness | 46 executed, 0 failed/skipped | 46 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | FT-2 | CP-1 | ft2-classify | `/*/*/TestClassificationPolicyTests/*` | classification policy, once | 40 executed, 0 failed/skipped | 40 | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | FT-2 | CP-1 | ft2-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | guard union without the Windows admission class and without DerivedImpactIndexTests, which has its own row | 83 executed, 0 failed/skipped | 83 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | FT-2 | CP-1 | ft2-index | `/*/*/DerivedImpactIndexTests/*` | derived impact index alone, after the driver build | 27 executed, 0 failed/skipped | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | FT-2 | n/a | ft2-audit | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | committed land-graph manifest matches the tree; an inherited mismatch is a warning | exit 0 | n/a | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | FT-2 | n/a | ft2-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef df89ff2cda7cf501922781b33a2fcb4ac3edf708 -HeadRef HEAD` | evidence diff over the task base | exit 0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | FT-2 | n/a | ft2-pins | `digest=$(pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest \| tr -d '\r\n'); pin=$(sed -n "s/^\\$script:PinnedInputDigest = '\\([0-9a-f][0-9a-f]*\\)'.*/\\1/p" scripts/test-live-lane-run.ps1); echo "live-lane printed=$digest pinned=$pin"; test "$digest" = "$pin"; pwsh -NoProfile -Command '. ./scripts/lib/nightly-policy.ps1; $p = Get-Content tests/test-execution-policy.json -Raw \| ConvertFrom-Json; $h = Get-NightlyPolicyHash -Object $p; Write-Output ("policy computed=" + $h + " stored=" + $p.policyHash); if ($h -cne [string]$p.policyHash) { exit 1 }'` | recompute the live-lane input digest and the nightly policy hash; fail if either differs from the pin in this tree | both digests match | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | FT-2 | n/a | ft2-merge | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | merge-tree against origin/master has no conflicts | exit 0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
