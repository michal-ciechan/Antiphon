# CARD-1544 unbound mutation candidate

Original Code/landing owner: `5fb6b1b2-44dd-47c3-8a99-8eea293f1ad6`.
The repair already landed as `01231c66d570daceba09608edb59343647d4ce82`.
The initial slice introduced no executable change. The repair slice retargets two
existing test guards to the formatter's current Core path. All guard obligations
remain for caller-commissioned SourceLanding Mutation and missing-control discovery.
Bind the exact landed source and method pack before execution.

| ID | Deliberate defect / variant | Exact method filter | Expected assertion | Status |
|---|---|---|---|---|
| PC-1 | Remove only the C1374 host caller from Existing | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates` | `c1210-unrostered-gate` naming the host caller | Pending |
| PC-2 | Restore the old conditional helper gate and remove the caller gate | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates` | `c1210-unrostered-gate` naming C1374CensusProbes.Check | Pending |
| PC-3 | Remove the caller gate, retaining the roster | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates` | `c1210-roster-without-gate` | Pending |
| PC-4 | Replace both `[antiphon-progress:` prefixes in server/Application/Core/DelegationReportFormatter.cs; confirm stage-code.md still contains no such prefix | `/*/*/RepairSourceDocumentationTests/C499_V31_DocsAndSkillNameTheRepairContract` | ShouldBeTrue assertion fails after both real sources lack the marker; an IO/build error is not red | Pending |
| PC-5 | Add a compiling static method comparing a fully qualified AgentTaskRole parameter to AgentTaskRole.Check inside server/Application/Services/SessionExitEvidence.cs | `/*/*/SpecialistRoleContractTests/no_Check_comparison_survives_outside_the_allowlist` | hits.ShouldBeEmpty fails naming SessionExitEvidence.cs | Pending |

Mutation owns deliberate red/restore/green cycles and any missing-control finding.
No timeout widening, assertion loosening or whole-class PC runs.
