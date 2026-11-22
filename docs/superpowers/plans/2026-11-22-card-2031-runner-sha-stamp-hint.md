# CARD-2031 desktop runner SHA stamp activation

Base `e0e747772967f8ed4e2085e79aac21c4b056fa3b`. Code and landing owner `3a33b568`.
The first desktop runner restart onto deploy-only SHA stamping must refresh
the resident supervisor. After readiness confirms the checkout's runner, an
advisory reads its `/capabilities` build identity. A reported unstamped build
gets one hint. The current deploy launcher expects a full 40-hex stamp, so
full SHAs from different commits are the same kind. The hint leaves the
restart operations, readiness budget, result JSON and exit code intact.

## Verification design

- R-1 / CP-1. The real script entry runs with fake readiness, control and build
  responses. Before the fix, the hint assertions fail for unknown, null and
  short stamps. The other cases assert the existing controls and exit codes.
- R-2 / CP-2. The documentation guard fails before the activation instructions
  carry the required command and checkout identity check.
- V-1 / CP-3 and CP-4. Check the warning count, exit codes and control trace,
  ASCII parsing, readiness, restart preflight and the runner limiter census.
  The Windows PowerShell and Windows TCP owner cases remain native coverage.
  The runtime advisory uses the existing verified listener identity from
  `New-RunnerRestartPlatform`; the added decision and HTTP read are portable.
- V-2 / CP-5. Check the edited runbook sections, doc consumers and the standing
  guard batch, including the impacted `TestLaneCategoryGuardTests`.
- V-3 / CP-6 and CP-7. Run the derived index class and the remaining impacted
  limiter census.
- V-4 / manual. Check the committed scripts' ASCII bytes and syntax, inspect
  the existing `GET /capabilities` mapping and deploy build stamp contract,
  and verify full-range impacted classes and evidence-diff, land graph
  `-Check` and merge-tree against freshly fetched master. No desktop operation
  is part of Code acceptance.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c2031-red-runner/` | restart-hint-red | `/*/*/RunnerRestartScriptCompatibilityTests/Sha_stamp_hint_is_advisory` | R-1 | RunnerRestartScriptCompatibilityTests.Sha_stamp_hint_is_advisory | 11 | 5 | true |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c2031-red-docs/` | activation-docs-red | `/*/*/DockerStackDocumentationTests/First_desktop_runner_restart_after_sha_activation_requires_hard` | R-2 | DockerStackDocumentationTests.First_desktop_runner_restart_after_sha_activation_requires_hard | 2 | 8 | true |
| CP-3 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c2031-runner/` | restart-script | `/*/*/RunnerRestartScriptCompatibilityTests/*` | V-1 | RunnerRestartScriptCompatibilityTests | 11 | 5 | true |
| CP-4 | S2 | CP-3 | restart-fixtures | `/*/*/(RunnerRestartHealthTests*)\|(RunnerRestartPreflightSafetyTests*)\|(RunnerRestartPreflightCacheTests*)\|(ProcessSpawnLimitTests*)/*` | V-1 | RunnerRestartHealthTests,RunnerRestartPreflightSafetyTests,RunnerRestartPreflightCacheTests,ProcessSpawnLimitTests | 4 | 3 | true |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c2031-docs/` | doc-and-standing-guards | `/*/*/(DockerStackDocumentationTests*)\|(GrokRunnerImageContractTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(ZombieCensusPlatformTests*)\|(KeepSixDocumentationTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | V-2 | DockerStackDocumentationTests,GrokRunnerImageContractTests,StandingPipelinePolicyDocumentationTests,ZombieCensusPlatformTests,KeepSixDocumentationTests,TestClassificationPolicyTests,TestClassificationGuardTests,SlowTestRegistryMergeTests,SlowTestTripwireTests,NativeLinuxPolicyWitnessTests,MechanicalPcContractTests,TaskPlatformGuidanceTests,C1729LayeringGuardTests,TestLaneCategoryGuardTests,ProcessSpawnLimitTests | 78 | 8 | true |
| CP-6 | S2 | CP-5 | derived-impact-index | `/*/*/DerivedImpactIndexTests/*` | V-3 | DerivedImpactIndexTests | 1 | 4 | true |
| CP-7 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c2031-pty/` | pty-limiter-guard | `/*/*/ProcessSpawnLimitTests/*` | V-3 | ProcessSpawnLimitTests | 1 | 3 | true |

### Cost

Estimated checkpoint runtime is 36 minutes before build-slot and host-load
waits. Every row is bounded by named methods or classes. The checkpoint
driver and, if its inputs change, the derived index generator are separate
prerequisites. [Mutation candidates](../mutations/2026-11-22-card-2031-runner-sha-stamp-hint.md)
are pending.
