# CARD-2052 Review round 2 repair: native Windows checkpoints (pending)

Native Windows rows for `2026-11-23-card-2052-r2-checkpoints.md`. These cases skip at runtime off Windows, and every
identity is in `WindowsUnitSkipRoster.WindowsOnly`. A Linux receipt cannot discharge them. They stay pending until a
Windows host runs this table (`-Platform Windows`) at the reviewed SHA. Review 2ff6a0bc asked for these executions.

- NW-1: the four classes that execute zero cases on Linux: `CheckpointRecoveryWindowsTests` (14),
  `CheckpointWindowsAnchoredDeleteTests` (7 results), `CheckpointWindowsAnchoredPathDeleteWitnessTests` (5) and
  `CardFilePrivacyPathTests` (3 results).
- NW-2: the Windows-only methods of classes the Linux table runs without them: OutputCleanupTests `windows_*` (four
  custody methods plus the docs check), `EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open`,
  `CardFilePrivacyGitTests.Junction_ancestors_refuse_root_and_managed_file_aliases` (3 results),
  `RcBridgeProbePlatformTests.Windows_probe_reports_a_measured_count` and
  `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c2052-r2-win/ | native-windows-classes | `/*/*/(CheckpointRecoveryWindowsTests*)\|(CheckpointWindowsAnchoredDeleteTests*)\|(CheckpointWindowsAnchoredPathDeleteWitnessTests*)\|(CardFilePrivacyPathTests*)/*` | NW-1 | >= 29 executed, 0 failed, 0 skipped | 29 | 12 | true |
| CP-2 | all | CP-1 | native-windows-output-cleanup | `/*/*/OutputCleanupTests/(windows_*)` | NW-2 | 5 executed, 0 failed, 0 skipped | 5 | 4 | true |
| CP-3 | all | CP-1 | native-windows-evidence-folder | `/*/*/EvidenceFolderTests/tool_copy_removal_retries_while_a_file_is_still_held_open` | NW-2 | 1 executed, 0 failed | 1 | 3 | true |
| CP-4 | all | CP-1 | native-windows-card-privacy-git | `/*/*/CardFilePrivacyGitTests/Junction_ancestors_refuse_root_and_managed_file_aliases` | NW-2 | 3 executed, 0 failed | 3 | 3 | true |
| CP-5 | all | CP-1 | native-windows-rc-bridge | `/*/*/RcBridgeProbePlatformTests/Windows_probe_reports_a_measured_count` | NW-2 | 1 executed, 0 failed | 1 | 3 | true |
| CP-6 | all | CP-1 | native-windows-platform-admission | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Windows_linux_only_outcomes_are_exact` | NW-2 | 1 executed, 0 failed | 1 | 5 | true |
