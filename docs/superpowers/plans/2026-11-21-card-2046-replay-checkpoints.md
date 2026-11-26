# CARD-2046 replay checkpoints

Code task `0f59f07a`. Base `6be80dcccb9a9748f46d7c77f9c418d80e32f8bf`. Final.
The [original canary plan](2026-11-21-card-2046-codex-boot-canary.md) defines V-1 through V-4 and R-1 through R-4.
The invariant is that Codex becomes Ready and submits the complete production boot pointer exactly once, with a matching generation, transcript, and loopback request.
The account fixtures exercise the measured 0.170 warning-chip frames. The explicit selection includes the changed canary, fixture class, and the affected lane and process-spawn rosters. No unbounded class selection.

## Performance budget

Budget 45 minutes for replay verification, with the shared Antiphon.Tests build, one Pty build, one SessionRunner test build, and the scratch build for R-3. The existing 150-second canary timeout is unchanged. The earlier Review measured one 49-second Linux canary sample, so timing is provisional.
The canary launches one CLI and clones from the test-owned Postgres fixture. Checkpoint receipts and TRX record per-row and per-test time. Canary milestones record readiness, composer progress, Enter and the receipt, and the screen captures retain state for diagnosis.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---|---|
| CP-1 | final | `tests/Antiphon.Tests -> bin-c2046-replay/` | c2046-canary | `/*/*/CodexBootPointerCanaryTests/Real_Codex_consumes_and_submits_the_boot_pointer` | V-1 | 1 passed, 0 failed | 1 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | final | CP-1 | c2046-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-2 | all nine classes, 0 failed | 84 | 4 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | final | CP-1 | c2046-index | `/*/*/(DerivedImpactIndexTests*)\|(CodexRunnerImageContractTests*)\|(ExpectedPlatformSkipRosterTests*)\|(DerivedImpactIndexInvariantTests*)/*` | V-3 | all three classes, 0 failed | 49 | 4 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | final | `tests/Antiphon.Agents.Pty.Tests -> bin-c2046-replay-pty/` | c2046-fixtures | `/*/*/CodexAccountBootstrapTests/*` | V-4 | 24 passed, 0 failed | 24 | 4 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | final | n/a | c2046-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-1 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | final | n/a | c2046-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 6be80dcccb9a9748f46d7c77f9c418d80e32f8bf -HeadRef HEAD` | R-1 | exit 0 | n/a | 1 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | final | n/a | c2046-pins | `pwsh -NoProfile -Command '$digest = @(& pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $pin = [regex]::Match((Get-Content -Raw scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + [char]39 + "([0-9a-f]{64})" + [char]39)).Groups[1].Value; Write-Output "computed=$digest pinned=$pin"; if (!$digest -or $digest -cne $pin) { throw "input digest drift" }'` | R-1 | exit 0 | n/a | 1 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | final | CP-1 | c2046-main-spawn | `/*/*/ProcessSpawnLimitTests/*` | V-2 | 0 failed | 1 | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | final | CP-4 | c2046-pty-spawn | `/*/*/ProcessSpawnLimitTests/*` | V-2 | 0 failed | 1 | 1 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | final | `tests/Antiphon.SessionRunner.Tests -> bin-c2046-replay-runner/` | c2046-runner-spawn | `/*/*/ProcessSpawnLimitTests/*` | V-2 | 0 failed | 1 | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-11 | final | CP-4 | c2046-startup | `/*/*/CodexStartupReadinessTests/*` | V-4 | 32 passed, 0 failed | 32 | 1 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

R-2 uses a fresh master fetch and merge-tree. R-3 is the one manual canary run with historical classifier files restored in a scratch copy. R-4 uses an absent executable override on the exact canary method and checks the skip and rc selection. The tool and index driver preparation builds are outside the verification rows. Restart: none.
