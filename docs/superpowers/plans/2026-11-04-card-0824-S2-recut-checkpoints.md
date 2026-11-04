# CARD-0824 S2 re-cut on master: checkpoint amendment

Code task 684dc939, 2026-11-04, server2 (Linux). The S2 chain (reviewed at 718bf652d) is cherry-picked onto master
2b342761f with `LandingBranchDeletion = 42` beside master's `WorkspaceSlotDecision = 41`, the event type uniqueness
guard (0e0ddf801) and one policy re-record. This file amends only the `### Checkpoints` table of the
[round 2 amendment](2026-11-03-card-0824-S2-code-r2-checkpoints.md); its rows CP-12..CP-22 are copied verbatim.
CP-19 keeps its one Windows-only skip by design. The re-cut adds:

- CP-23: the `AgentTaskEventTypeLedgerTests` uniqueness guard (41 and 42 distinct).
- CP-24: `ReleaseGatePlatformTests` (the C1323 harness cases, `LinuxOnlyWindowsExclusionPolicy` and
  `C1686_LinuxOnlyCensus`) over the policy with S2's two Linux-only rows.
- CP-25..CP-28: `NativeLinuxPolicyWitnessTests` in every test project that compiles it.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-12 | S2 | tests/Antiphon.Tests -> bin-c0824-s2/ | portable-branch-delete | /*/Antiphon.Tests.Application/C0824PublishedBranchDeletionTests/* | V-14..V-19, V-38, V-39, V-41, V-42, V-45..V-49, review 370083f1 gaps 1-4, A-1..A-3 | class, 0 failed/skipped | 52 | 15 | true |
| CP-15 | S2 | CP-12 | portable-retention-regression | /*/Antiphon.Tests.Application/(AgentTaskLandRemoteSourceRetention*)/* | R-1 | 4 existing classes, 0 failed/skipped | 32 | 4 | true |
| CP-17 | S2 | CP-12 | linux-native-recycle-audit | /*/Antiphon.Tests.Scripts/C0824BranchDeletionRecycleAuditTests/* | V-43 | class, 0 failed/skipped | 3 | 3 | true |
| CP-18 | S2 | CP-12 | linux-native-branch-refs-delivery | /*/Antiphon.Tests.Application/C0824BranchRefsDeliveryTests/* | V-44 | class, 0 failed/skipped | 4 | 3 | true |
| CP-19 | S2 | CP-12 | portable-publication-regression | /*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/* | R-2 | existing class, 0 failed/skipped | 26 | 4 | true |
| CP-21 | S2 | CP-12 | portable-dispatcher-ops-regression | /*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/Supported_operations_preserve_contracts | dispatcher handles every PhoneHomeOperation | method, 0 failed/skipped | 1 | 2 | true |
| CP-22 | S2 | CP-12 | portable-migration-regression | /*/Antiphon.Tests.Application/AgentTaskInternalDecisionMigrationTests/* | no pending model change after Card0824BranchDeletion | existing class, 0 failed/skipped | 1 | 3 | true |
| CP-23 | S2 | CP-12 | portable-event-type-ledger | /*/Antiphon.Tests.Domain/AgentTaskEventTypeLedgerTests/* | AgentTaskEventType values unique (41 WorkspaceSlotDecision, 42 LandingBranchDeletion) | class, 0 failed/skipped | 1 | 2 | true |
| CP-24 | S2 | CP-12 | linux-policy-census | /*/Antiphon.Tests.Scripts/ReleaseGatePlatformTests/* | C1323 platform policy, LinuxOnlyWindowsExclusionPolicy, C1686_LinuxOnlyCensus over the re-recorded policy | class, 0 failed/skipped | 6 | 8 | true |
| CP-25 | S2 | CP-12 | native-linux-witness-antiphon | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | policy rows carry the pinned attribute (antiphon suite) | class, 0 failed/skipped | 3 | 2 | true |
| CP-16 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-s2r/ | portable-runner-refs | /*/Antiphon.SessionRunner.Tests/C0824RunnerBranchRefsTests/* | V-40, A-4 | class, 0 failed/skipped | 12 | 4 | true |
| CP-20 | S2 | CP-16 | portable-binding-regression | /*/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests/* | R-3 | existing class, 0 failed/skipped | 11 | 2 | true |
| CP-26 | S2 | CP-16 | native-linux-witness-session-runner | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | policy rows carry the pinned attribute (session-runner suite) | class, 0 failed/skipped | 3 | 2 | true |
| CP-27 | S2 | tests/Antiphon.PtyHost.Tests -> bin-c0824-s2h/ | native-linux-witness-pty-host | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | policy rows carry the pinned attribute (pty-host suite) | class, 0 failed/skipped | 3 | 4 | true |
| CP-28 | S2 | tests/Antiphon.Agents.Pty.Tests -> bin-c0824-s2a/ | native-linux-witness-agents-pty | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | policy rows carry the pinned attribute (agents-pty suite) | class, 0 failed/skipped | 3 | 5 | true |
