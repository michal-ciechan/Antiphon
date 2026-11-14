# Run 34d41b7e group 132: WindowsUnitPlatformAdmissionTests Windows failure

Read-only extract. Task 5c3cf9f6, 2026-11-14.
Run 34d41b7e: rc/2026-11-20 = b559bfba2e9a420495122d27171352fe9b7e1ba4, desktop Windows, Full run, still running.
Sources: `C:\Antiphon\releases\candidates\rc-2026-11-20-b559bfba2e9a\logs\2026-11-14-1040-34d41b7e\`
`antiphon-all-group-132.trx`, `antiphon-all-group-132-tests.log`, `antiphon-all-group-132.timing.json`.
`antiphon-all-group-132-execution\log_261114155855967.diag` (1.49 MB) was not needed.
Checkout under test: `C:\Antiphon\releases\checkout-8e08368a`.

## Verdict

This is **deterministic** and the **same failure as the Linux red at master**. Two methods in
`VolumeRetirementContentTests` call `NativeLinuxGate.Require` but have no row in the
`WindowsUnitSkipRoster` source inventory. The class already has its `[NativeLinuxOnly]` marker and its
`profiles.rc` Windows exclusion. The `test-execution-policy.json` row is also present. Only the C# roster rows are missing.

## The failure

| Field | Value |
|---|---|
| Test | `Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates` |
| Outcome / duration | Failed, 00:00:14.5938451 |
| Start / end (UTC) | 2026-11-14 16:24:22.857 / 16:24:37.450 |
| Chunk | all-group-132: 439 total, 1 failed, 438 passed, 0 skipped, run 26m 42s 600ms (runWall 1604 s, build 583 s) |

Assertion (verbatim from tests.log):

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: entry
    should not be null but was

Additional Info:
    c1210-unrostered-gate: Antiphon.Tests.Scripts.VolumeRetirementContentTests.Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent
  at Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests.AssertClosure(SyntaxNode[] roots, Entry[] expected, Boolean requireLegacyHelper) in C:\Antiphon\releases\checkout-8e08368a\tests\Antiphon.Tests\Scripts\WindowsUnitPlatformAdmissionTests.cs:116
  at Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates() in C:\Antiphon\releases\checkout-8e08368a\tests\Antiphon.Tests\Scripts\WindowsUnitPlatformAdmissionTests.cs:71
```

Expected: a roster `Entry` whose identity is the method above. Actual: `null`. No such row exists.
Standard output had only the `ANTIPHON_TEST_PHASE_ID` line. There was no stderr.

### What the assertion does

`AssertClosure` (`WindowsUnitPlatformAdmissionTests.cs:90-128`) parses every `*.cs` under `tests/Antiphon.Tests`
from disk with Roslyn. For each `NativeLinuxGate.Require` or `C1008HostFixture.RequireNativeLinux` call, it
requires an entry in `WindowsUnitSkipRoster.Native`, `NativeSourceOnly` or `Existing` whose identity is
`namespace.class.method` (line 115-116). It also checks the reverse direction. The check reads source only.
It has no polling, no timeout, no OS branch and no process. The 14.6 s is the Roslyn parse of the whole test tree.
The concurrent sibling `C1210_Native_linux_entries_guard_before_work` parses the same tree in 16.8 s and passed.

### Two unrostered methods, not one

The test stops at the first unrostered call. A scan of the same source shows a second one, which is
reported only after the first is fixed:

| Method | Gate line (master 8cec1e74e = rc content) | Introduced by |
|---|---|---|
| `VolumeRetirementContentTests.Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent` (8 `[Arguments]` cases) | `VolumeRetirementContentTests.cs:30` | c4ba11dca6f9143ccaeb7856a2b07ea645ef8a2a, 2026-11-13 10:53Z, "test: witness selector fixture retirement refusal and checkout residue" (cherry-pick of 512d58221) |
| `VolumeRetirementContentTests.Selector_fixture_cannot_use_disposable_seed_parent_as_publication_proof` | `VolumeRetirementContentTests.cs:94` | b0fc2ff14191f6fe90747b1324a32dc3eca9e4ad, 2026-11-13 11:11Z, "test: require a published parent beyond disposable seed exceptions" (from 754306353) |

Both commits come from the selector-fixture retirement Debug, task 49ae044d, branch `feat/card-task-49ae044d`.
That work is the gate-9 follow-up to CARD-1866/CARD-1820, recorded in `docs/investigations/2026-11-13-selector-fixture-retirement-checkpoints.md`.
Both commits are ancestors of rc b559bfba2. Neither file changed between rc b559bfba2 and master 8cec1e74e,
so master is red on every platform for the same reason. The 32 older `VolumeRetirementContentTests` methods are
rostered in `NativeSourceOnly` (`WindowsUnitSkipRoster.cs:134-165`). The class itself dates from
6786554895585e9b1ec0a6e56c799f0178d2d237 (2026-10-30, CARD-1609).

## Siblings in the same class (all in this chunk)

| Test | Outcome | Duration |
|---|---|---|
| C1210_Guard_matches_native_platform | Passed | 0.002 s |
| C1210_Native_linux_entries_guard_before_work | Passed | 16.830 s |
| C1210_Windows_linux_only_outcomes_are_exact | Passed | 28.672 s |
| C1210_Host_roster_matches_windows_shaped_observation | Passed | 0.059 s |
| C1210_Audit_rejects_empty_or_incomplete_outcomes | Passed | 0.837 s |
| C1226_Pin_inspection_methods_skip_off_linux | Passed | 0.014 s |
| **C1210_Roster_closure_matches_source_gates** | **Failed** | 14.594 s |

These passes bound the cause to the reverse-closure (source to roster) direction. The `Native` runtime child
roster and its exact outcome counts are intact, as is the Windows-shaped observation. The Native-reason
constants also match. `VolumeRetirementContentTests` itself is not in this chunk.

## Running at the same time (16:24:22.857 to 16:24:37.450)

- `WindowsUnitPlatformAdmissionTests.C1210_Native_linux_entries_guard_before_work` (16:24:22.844, 16.8 s, Passed)
- `TaskCeilingScriptTests`: 14 sequential cases (`C1610_Ceiling_reply(*)`, `C1610_Declared_budget(*)`,
  `C1610_Status(seat-held)`), each 0.8 to 1.8 s, all Passed.

The failure has no timing or load evidence: it is not a poll expiry, timeout or sharing violation.

## Repair (for the Debug task)

1. In `tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs`, add **two** rows to `NativeSourceOnly`. Put them next to
   the existing `VolumeRetirementContentTests` rows, using the class's exact reason string:
   - `new("Antiphon.Tests.Scripts", "VolumeRetirementContentTests", "Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent", 8, "CARD-1609: content retirement proofs require native Linux (bash, flock, real Git and POSIX mounts); Windows skips explicitly."),`
   - `new("Antiphon.Tests.Scripts", "VolumeRetirementContentTests", "Selector_fixture_cannot_use_disposable_seed_parent_as_publication_proof", 1, "CARD-1609: ...same reason..."),`

   `NativeSourceOnly` is the right list. `Native` would require these methods in the frozen child selection
   (`ChildFilter`) and in its exact Windows-shaped outcome counts. The closure compares identities only, so
   `Results` is not checked; 1 matches every neighbour.
2. Keep the closure witness strict. Do not add an exemption to `AssertClosure`.
3. **No policy-file edit and no re-pin should be needed.** The class already has its `[NativeLinuxOnly(NativeLinuxReason)]`
   marker. It already has the `test-execution-policy.json` Windows row (`"class": "VolumeRetirementContentTests"`,
   reason `Linux-only by construction: ...`, owner CARD-1609, around line 1110). Neither method is in
   `tests/expected-platform-skips.json`, so `Get-NightlyExpectedSkipRosterDrift` (`scripts/lib/nightly-coverage.ps1:1801`)
   has no overlap to compare. `Get-NightlyExpectedSkipRosterDigest` hashes the JSON skip roster, not this `.cs`.
   Still, run `regenerate-derived-impact-index.ps1` once: `WindowsUnitSkipRoster.cs` has an index edge.
   Commit any byte change as its own slice.
4. Verify on Linux with `/*/*/WindowsUnitPlatformAdmissionTests/*` plus `DerivedImpactIndexTests` and
   `NativeLinuxPolicyWitnessTests`. The Windows proof is the next rc run of this chunk.

### Conflicts with in-flight branches

- CARD-1901 round 2 `origin/feat/card-task-e594649c` (tip 6a14d77f9) edits `VolumeRetirementContentTests.cs`,
  `test-execution-policy.json` and `derived-impact-index.json`, but **not** `WindowsUnitSkipRoster.cs`. Its copy of the
  class still has 34 gated tests, so it adds no new unrostered gate. A roster-only fix **does not conflict** textually.
  The only possible overlap is `derived-impact-index.json` if the regenerate changes it. That file is generated:
  regenerate on the combined tree.
- CARD-0824 branches (`feat/card-task-3c766de7`, `d684bc25`) touch `test-execution-policy.json` only;
  `19b61d3a` and `6130909c` touch none of the three files. **No conflict**.
- The brief's planned `test-execution-policy.json` edit and re-pin are unnecessary. Skipping them avoids that
  file's conflict with e594649c and CARD-0824.

## Why it was missed

- **Producer selection.** Task 49ae044d's closed checkpoint list (`2026-11-13-selector-fixture-retirement-checkpoints.md`)
  ran `VolumeRetirementContentTests` (CP-1), the gate classes, audit regressions and a CP-4 "required guard union".
  That union contains `NativeLinuxPolicyWitnessTests` but **not** `WindowsUnitPlatformAdmissionTests`.
  `NativeLinuxPolicyWitnessTests` passes because the class marker and policy row already exist.
- **Standing guard union.** `docs/testing-and-build.md` defines the union, and Merge adds `DerivedImpactIndexTests` +
  `C1584_E2EExpandedBudget`. It does not include `WindowsUnitPlatformAdmissionTests`, so no Merge or Review selection
  that follows the standard union runs this roster witness.
- **Impact selection is blind to it.** The witness reads `tests/Antiphon.Tests/**/*.cs` from disk at runtime
  (`Directory.EnumerateFiles`). It does not use type references, so `tests/derived-impact-index.json` has no edge from
  `VolumeRetirementContentTests.cs` to `WindowsUnitPlatformAdmissionTests`. Only `WindowsUnitSkipRoster.cs`
  maps to it. Adding a gated method to any already-rostered class never selects the witness.
- The commits were cherry-picks landed on 2026-11-13 and reached the rc/2026-11-20 cut. The Windows Full run is
  the first selection that executes the witness after them. Linux reports from CARD-1895 r2/r3 and CARD-0824
  F1-F4 saw the same red but treated it as inherited.

Follow-up worth a card: add `WindowsUnitPlatformAdmissionTests` (or just `C1210_Roster_closure_matches_source_gates`, about 15 s)
to the standing guard union. The other option is an impact rule: any change adding `NativeLinuxGate.Require` /
`RequireNativeLinux` selects it.
