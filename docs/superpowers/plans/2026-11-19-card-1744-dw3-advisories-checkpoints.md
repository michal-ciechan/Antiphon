# CARD-1744 DW-3 helper advisories (Code task 1eb957bd)

Base master `5be178005f06aa868bbb038e561c2ef22a654cb5`. Branch
`feat/card-task-1eb957bd`. The historical plan
`docs/superpowers/plans/2026-11-19-dw3-checkpoints.md` stays as the record of
task 066a9073. This plan is the closed list for the advisory repair.

The helper lists leftover `.dw3part` and `.dw3bak` files by path and prints
one recovery line. It does not read or print their contents. Sweeping them
requires `-SweepStranded`. Without that switch the run prints `sweep: refused`
and continues. After a temp or a backup is created, `Confirm-Dw3OwnerOnly`
checks the mode or ACL and, on failure, prints `owner-only-failed:` plus the
path and stops. A residue delete that leaves the file prints `kept:` and the
path. Saved byte arrays are held on a list and cleared with `[Array]::Clear`
in `finally`. The list is indexed. Wrapping it in `@()` throws
`ArgumentException` on this host when an item is a `byte[]`, which was turning
exit 0 and exit 2 into exit 1 and skipping the clear probe. `changed: none`
is printed only when both halves match the saved bytes and this run did not
create the phase 1 example. A run that created the example prints
`changed: phase1-config`, including when a later step fails.
`Repair-Dw3Half` uses `$halfMatches`. A case-variant of a planned flat key,
or a nested `PhoneHomeRunner` object of any case, is `shared-secret-ambiguous`
on `-Execute` and on `-Execute -Rotate`, and `shared-secret-key: ambiguous`
on a dry-run. An exact flat key still uses `shared-secret-present`. A nested
object that is not `PhoneHomeRunner` still round-trips.

A-R3-4 (shared-directory residue outside this helper's own temps) is not in
this brief. Linux mode 600 is witnessed. The Windows ACL
(`FileSystemAccessRule`) and the Windows `File.Replace` backup path are
UNOBSERVED on this Linux runner. No migration. No positive-control row.
Mutation execution is waived (operator 2026-11-12) and is not discharged.
Nightly inputs are not re-pinned. `C1210_Windows_linux_only_outcomes_are_exact`
skips on Linux. It is not a closed-list row, because a skipped test fails
whole-run receipt validation. The six portable methods are CP-5.

The red witness is `ab3201a9acb770b42beb570e2deb4afe8279175b` (passed=237
failed=38). The 197 witnesses already on the base stayed green. The 38
failures were the missing advisory lines, the missing sweep, the missing
owner-only name, the uncleared probe, `changed: none` after a phase 1 copy,
the `$matches` assignment, and the ambiguous-key refusal. The short green
witness before this plan was passed=279 failed=0. CP-1 is that script again,
at the committed tip.

## Verification design

- V-1. `scripts/test-desktop-win-phase1-secrets.ps1`. Floor from the green witness: passed=279 failed=0. Includes the stranded list, the refused sweep, `-SweepStranded`, owner-only failure names, `kept:`, the clear probe, `changed: phase1-config`, the renamed repair variable, and the ambiguous-key refusals. Existing leak checks stay in the same script.
- V-2. `TestClassificationPolicyTests` once. Floor 42.
- V-3. Eight guard classes, without `WindowsUnitPlatformAdmissionTests` and without `DerivedImpactIndexTests`. Floor 39, 0 skipped.
- V-4. `DerivedImpactIndexTests` alone. Floor 27. Regenerate the index only if this row fails, as its own last commit, driver built first, twice (changed, then unchanged).
- V-5. Six portable `WindowsUnitPlatformAdmissionTests` methods. Floor 6, 0 failed, 0 skipped. `C1210_Windows_linux_only_outcomes_are_exact` is owed on Windows and is not a Linux pass.
- V-6. `scripts/audit-land-verification-graph.ps1 -Check`.
- V-7. `scripts/test-live-lane-run.ps1 -PrintInputDigest`. Report the digest. Do not re-pin.
- V-8. `scripts/check-evidence-diff.ps1` from `5be178005f06aa868bbb038e561c2ef22a654cb5` to HEAD.

C1729 must stay VIOLATIONS 0 LISTED 162. `merge-tree` against `origin/master` is reported beside this list and is not a row.

## OS branch witnesses

Changed production behavior is `Set-OwnerOnlyFile` in
`scripts/desktop-win-phase1-secrets.ps1`. The branch anchor is
`$env:OS -eq 'Windows_NT'`. The production type is
`System.Security.AccessControl.FileSystemAccessRule`. The test script's
`$env:OS` branches only choose the observation.

- DW3-LINUX. targetOS Linux. testCase `execute-linux-mode-600`. checkpoint CP-1. executionMode native. Outcome is mode 600 via `chmod`.
- DW3-WIN. targetOS Windows. testCase `unobserved-windows-acl`. checkpoint CP-1. executionMode native. The Windows ACL is pending. A label is not a pass.

Startup impact is not required: this helper is not on the AppHost or restart path. The ignored declaration is `.antiphon/dw3-adv-os-witnesses.json`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | dw3 | n/a | dw3-script | `pwsh -NoProfile -File scripts/test-desktop-win-phase1-secrets.ps1` | V-1 | RESULT passed=279 failed=0, exit 0 | n/a | 15 | true | n/a |
| CP-2 | dw3 | tests/Antiphon.Tests -> bin-dw3adv/ | dw3-policy | `/*/*/TestClassificationPolicyTests/*` | V-2 | 0 failed, executed at least 42 | 42 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | dw3 | CP-2 | dw3-guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-3 | 0 failed, executed at least 39 | 39 | 12 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | dw3 | CP-2 | dw3-index | `/*/*/DerivedImpactIndexTests/*` | V-4 | 0 failed, executed at least 27 | 27 | 12 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | dw3 | CP-2 | dw3-windows | `/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform*)\|(C1210_Native_linux_entries_guard_before_work*)\|(C1210_Roster_closure_matches_source_gates*)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes*)\|(C1210_Host_roster_matches_windows_shaped_observation*)\|(C1226_Pin_inspection_methods_skip_off_linux*)` | V-5 | 6 executed, 0 failed, 0 skipped | 6 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | dw3 | n/a | dw3-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | V-6 | exit 0 | n/a | 8 | true | n/a |
| CP-7 | dw3 | n/a | dw3-pins | `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest` | V-7 | exit 0, digest printed, inputs not re-pinned | n/a | 5 | true | n/a |
| CP-8 | dw3 | n/a | dw3-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 5be178005f06aa868bbb038e561c2ef22a654cb5 -HeadRef HEAD` | V-8 | exit 0 | n/a | 5 | true | n/a |
