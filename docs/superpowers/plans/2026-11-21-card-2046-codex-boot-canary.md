# CARD-2046 real Codex boot pointer

Code task `6cb0e1e8`. Base `35a23c90a7ad92cf08f769982459ebe8e87bc9d1`. Final verification.

The ready gate must open on the installed CLI, and only a fully consumed production
boot pointer may authorize Enter. The accepted-generation transcript and a loopback
Responses API request must observe the whole pointer with the requested model slug.

The canary uses the real RunnerCodexAdapter, SessionMessageQueueService, SessionRunnerRuntime,
and the owned PtyHost. The runner's production PtyBackendPolicy selects Porta Unix PTY on
Linux or ModernConPty on Windows. Windows native evidence is pending a Windows run of CP-1
with the installed candidate binary and the modern redistributable. No production OS branch
changes. The old Explicit probes use RealCliStubGate, which refuses Linux before resolving
the binary. The new ordinary Integration method enters the discovery-based rc roster
without an exclusion change. The expected live-context entry classifies the named missing-CLI skip.
The documented candidate-image command requires a real pass.

Limit scope to these classes and the impacted-classes row. No unbounded classes or
whole Unit lane. Cold-build floor 25 minutes across the listed estimates, with one
Antiphon.Tests build shared by CP-1 through CP-3. The canary launches one process
and uses a cloned test database. Its method envelope is 150 seconds. No repeat after green.

## Verification design

- V-1. Real Codex boot pointer canary, one executed case with the image's 0.170.0 CLI on Linux.
  Retain idle, typed/consumed and final screen evidence plus the model and write milestones.
- V-2. Standing nine-class guard union, including the impacted process-spawn roster, policy, mechanical contract, platform
  guidance and layering.
- V-3. DerivedImpactIndexTests, the Codex image contract and expected skip roster. The native derived-index driver
  is built before the index is generated twice, first changed and then unchanged. Commit
  a changed generated index last.
- V-4. CodexAccountBootstrapTests using the measured Sol and Astra typed frames from
  243f9924bc7333a4f7fc07b5eae4bf1ddfb72101. The class's empty-composer and chip-only
  negative controls remain in the same selection.
- R-1. Land-graph check, full-base evidence diff, and recomputed input pin.
- R-2. Fetch master and require merge-tree without conflicts.
- R-3. Run the same live method in a scratch checkout with only the classifier's 0.170
  warning-chip step removed. Require Ready, the pointer visible, zero Enter, and a failure
  at whole-consumption. The brief requires this pre-fix reproduction as manual acceptance.
- R-4. Run the exact method with an absent CLI override and confirm the named skip.
  Confirm rc discovery admission and the changed-path selectability.

The scratch pre-fix and missing-executable runs are unlisted manual executions for R-3 and
R-4. Checkpoint tool and index driver preparation are unlisted tooling builds. Mutation is
waived by the caller. There are no separate PC rows. Restart: none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---:|---|---|
| CP-1 | final | `tests/Antiphon.Tests -> bin-c2046/` | c2046-canary | `/*/*/CodexBootPointerCanaryTests/Real_Codex_consumes_and_submits_the_boot_pointer` | V-1 | 1 passed, 0 failed | 1 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | final | CP-1 | c2046-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | V-2 | all listed, 0 failed | 78 | 4 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | final | CP-1 | c2046-index | `/*/*/(DerivedImpactIndexTests*)\|(CodexRunnerImageContractTests*)\|(ExpectedPlatformSkipRosterTests*)/*` | V-3 | all listed, 0 failed | 27 | 4 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | final | `tests/Antiphon.Agents.Pty.Tests -> bin-c2046-pty/` | c2046-fixtures | `/*/*/CodexAccountBootstrapTests/*` | V-4 | all listed, 0 failed | 1 | 4 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | final | n/a | c2046-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-1 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | final | n/a | c2046-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 35a23c90a7ad92cf08f769982459ebe8e87bc9d1 -HeadRef HEAD` | R-1 | exit 0 | n/a | 1 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | final | n/a | c2046-pins | `pwsh -NoProfile -Command '$digest = @(& pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $pin = [regex]::Match((Get-Content -Raw scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + [char]39 + "([0-9a-f]{64})" + [char]39)).Groups[1].Value; Write-Output "computed=$digest pinned=$pin"; if (!$digest -or $digest -cne $pin) { throw "input digest drift" }'` | R-1 | exit 0 | n/a | 1 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
