# Checkpoint project contract round 2

Code task `a320d237`, based on `2bde7a4b4f8f79bd8db9e6fc9187340f5aaac715`. Round-one review `614c28b4` identified a stale derived impact index (B-1) and a missing explicit Merge selector (A-1).

An edit to any of the three project files read by `CheckpointToolProjectContractTests` must select that class in Merge. The committed derived index must match the tracked-path builder byte for byte. No production behavior, native OS branch, migration, package allow-list, or startup input changes in this slice.

## Verification design

- V-1. Run the seven project contract tests.
- V-2. Run all 27 derived-index cases, including `committed_index_matches_the_builder`. Capture the committed pre-fix index identity. The same builder must change those committed bytes on the first regeneration and produce identical bytes on the second.
- V-3. Run the impacted-classes selector class, including its table audit.
- V-4. Run the nine guards for policy, classification, slow cases, native Linux policy, mechanical controls, platform guidance, layering and test lanes.
- R-1. Run the land graph and pin check at committed HEAD.
- R-2. Check the evidence diff over the complete task range.
- R-3. Recompute the pinned input digest and compare it with the tracked pin.
- R-4. Run `scripts/impacted-classes.ps1 -MergeSubset -Range <base>..HEAD` on a Git range containing a tool csproj change. Show that the original table omits the contract class and the repaired table adds the named kind and the contract class to the tests filter. Also check the two library project paths using `-Path`.
- R-5. Fetch master and check merge-tree. Inspect the actual task-range selector and record whether it calls for any additional class or Windows row. Check the OS-branch declaration against the committed range.

The charged estimates are 26 minutes across the closed rows, including one cold `Antiphon.Tests` build and sequential reuse for the three later TUnit rows. The selector is the largest class in scope. The Unit lane, product integrations, and any broader assembly are outside the selection. Keep the compiler policy opt-out and the per-driver Linux load gate. The native default-output derived-index driver build and the two pre-commit regeneration calls are required preparation outside the closed checkpoint rows. If the checkpoint launcher itself needs a build, lease it separately before launching the checkpoint tool. No PC rows are assigned.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | index | tests/Antiphon.Tests -> bin-a320d237/ | project-contract | `/*/Antiphon.Tests.Invariants/CheckpointToolProjectContractTests/*` | V-1 | 0 failed, 0 skipped | 7 | 8 | true |
| CP-2 | index | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | V-2 | 0 failed, 0 skipped | 27 | 2 | true |
| CP-3 | index | CP-1 | selector | `/*/Antiphon.Tests.Scripts/ImpactedClassesSelectorTests/*` | V-3 | 0 failed, 0 skipped | 31 | 5 | true |
| CP-4 | index | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-4 | 0 failed, 0 skipped | 78 | 3 | true |
| CP-5 | index | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-1 | exit 0 | n/a | 5 | true |
| CP-6 | index | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 2bde7a4b4f8f79bd8db9e6fc9187340f5aaac715 -HeadRef HEAD` | R-2 | exit 0 | n/a | 1 | true |
| CP-7 | index | n/a | pins | `pwsh -NoProfile -Command '$d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $p = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)).Groups[1].Value; if ($d -cne $p -or -not $p) { throw "pinned input digest mismatch" }; Write-Output ("inputDigest=" + $d + " pinnedInputDigest=" + $p)'` | R-3 | exit 0 | n/a | 2 | true |
