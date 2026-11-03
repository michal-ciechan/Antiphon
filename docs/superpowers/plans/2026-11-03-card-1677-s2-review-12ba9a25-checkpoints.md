# CARD-1677 S2 review 12ba9a25 round-1 repairs: checkpoint amendment

The test design (`2026-11-03-card-1677-test-design.md`) keeps its rows and numbering. This amendment re-runs the
S2 rows CP-7, CP-8, CP-13 and CP-9 at the repaired tip with their raised floors, adds CP-14 for the new runner
dispatch class, and re-runs the S1 retention server lane CP-5 alone. It is a separate file because the test design
is already on master (editing it here would conflict at land).

Repairs and their witnesses:

- **D-1** (every family regex ended in `$`, which in .NET also matches before a final `\n`): patterns end in `\z`
  and a name carrying any control character is never matched. `C1677TempFamilyRegistryTests.Family_regexes_are_anchored_and_exact`
  rejects `Sample+"\n"`, `+"\r"`, `+"\r\n"` and `"\n"+Sample` for every family; `C1677TempSweepNativeTests`
  `Family_name_with_a_line_break_is_unregistered_and_kept` and `Control_character_name_is_refused_before_any_family_match`
  sweep real entries so named. All four were red at `b15f66352` (the line-break entries were removal candidates).
- **D-2** (no runner-side op 47 or production-composition witness): `C1677TempSweepDispatchTests` (portable; the
  dispatcher's tempSweepV1 gate, Linux-only advertisement through the adapter's `TempSweepIsLinux` OS parameter,
  outside the mutation lock, DryRun keeps every byte, unknown mode refuses); `C1677TempSweepNativeTests.Production_owner_seam_reads_each_entrys_own_uid`
  (the real statx owner seam of `RunnerTempSweep.CreateDefault` over a test root); and four server cases in
  `C1677TempSweepServerTests` (no capability, preview stays DryRun, Execute sends Execute, retention disabled sends nothing).
- **A-2** (the plain recursive delete mutant survived): `C1677TempSweepNativeTests.Tree_changed_after_the_delete_inventory_is_refused`.

### Checkpoints

All rows are server2 (Linux). One run at the repaired tip.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-7 | all | tests/Antiphon.SessionRunner.Tests -> bin-c1677-s2r2/ | c1677-runner-temp-r2 | /*/*/(C1677TempSweepTests*)\|(C1677TempFamilyRegistryTests*)/* | V-40..V-48c, D-1 | 2 classes, 0 failed/skipped | 27 | 4 | true |
| CP-14 | all | CP-7 | c1677-runner-temp-dispatch | /*/*/C1677TempSweepDispatchTests/* | D-2(a) | class, 0 failed/skipped | 10 | 1 | true |
| CP-8 | all | CP-7 | c1677-runner-temp-native-r2 | /*/*/C1677TempSweepNativeTests/* | V-49, V-49b, V-50, D-1, D-2(b), A-2 | class, 0 failed/skipped | 7 | 1 | true |
| CP-13 | all | CP-7 | c1677-native-policy-regression-s2r2 | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | R-2 | existing class, 0 failed/skipped | 3 | 1 | true |
| CP-9 | all | tests/Antiphon.Tests -> bin-c1677-s2s2/ | c1677-server-temp-r2 | /*/*/C1677TempSweepServerTests/* | V-51..V-51c, D-2(c) | class, 0 failed/skipped | 7 | 4 | true |
| CP-5 | all | CP-9 | c1677-server-retention-r2 | /*/*/(C1677RetentionServerTests*)\|(C1677RetentionSettingsTests*)/* | V-30..V-39c, V-74..V-80 | 2 classes, 0 failed/skipped | 66 | 7 | true |

Min: CP-7 27 (unchanged count; V-48c gained cases inside one method). CP-14: 2 + 2 + 1 + 2 + 3 = 10. CP-8: 3 + 4 = 7.
CP-9: 3 + 1 + 2 + 1 = 7. CP-5 keeps the test design's floor of 66. Every row is serial, so CP-5 runs alone.
