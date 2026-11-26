# Group 139 Windows scratch-removal portability

Code task: `34eae6f8`. Base: `a12ff2eefdbcec889353802f50fb11a76a06390c`.
Branch: `feat/card-task-34eae6f8`. Tests only. No production change.
Mutation is waived (operator 2026-11-12). No PC rows. Restart: none.

## What changed

`OwnedTempLeakTests.Review_scratch_remove_refuses_a_symlinked_foreign_worktree`
normalises CRLF to LF before comparing the foreign README to `seed\n`.
`Review_scratch_remove_refuses_a_dirty_nested_worktree` compares
`git worktree list --porcelain` after `Path.GetFullPath` and `/` on both sides.
The case-insensitive `ShouldContain` stays. The uncommitted `keep-me\n` check
stays byte-exact: the Windows run passed it.

## Performance budget

CP-1's 15 minutes is the cold `Antiphon.Tests` build plus the class. Later rows
reuse that build. No repeat block after green. No whole Unit lane.
`WindowsUnitPlatformAdmissionTests` is not a row: on Linux it skips, and a skip
is not a pass. The desktop rc rerun owns it. Cost floor is the sum of
`EstimatedMinutes` (65).

## Unlisted runs

These are not builds or test filters. Results are reported with the checkpoint lines.

- `scripts/check-evidence-diff.ps1` over `a12ff2eefdbcec889353802f50fb11a76a06390c..HEAD`.
- `scripts/check-os-witnesses.ps1` with an ignored declaration. The diff is test and docs only.
- Nightly `policyHash`, `PinnedInputDigest`, and `LegacyExclusionDigest` recomputed and reported. Re-pin only if a guard fails.
- `scripts/regenerate-derived-impact-index.ps1` twice, after CP-4's driver build, only if `DerivedImpactIndexTests` or the script says the index changed. The json commit is last and only then.

## Verification design

- V-1: the ten `OwnedTempLeakTests` methods pass on Linux, including both repaired witnesses. The foreign README is still exactly `seed` modulo newline. The nested worktree is still registered, compared with `/` and `Path.GetFullPath`.
- V-2: `TestClassificationPolicyTests` passes.
- V-3: the guard classes named below pass, and C1729 stays `VIOLATIONS 0 LISTED 162`.
- V-4: `DerivedImpactIndexTests` passes against the committed index.
- R-1: `scripts/check-evidence-diff.ps1` reports 0 violations.
- R-2: `scripts/audit-land-verification-graph.ps1 -Check` exits 0.
- R-3: merge-tree against fetched `origin/master` is clean. OS-witness inventory is complete. The three nightly pins match the tree or are reported.

Windows, not run here: `/*/*/OwnedTempLeakTests/Review_scratch_remove_*` on the desktop after run `e7f92f66`. UNOBSERVED on this Linux host.

No PC variants. Every PC stays pending for method-scoped SourceLanding Mutation; this task defines none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-g139/ | owned-temp | `/*/*/OwnedTempLeakTests/*` | V-1 | 10 methods, 0 failed, 0 skipped | 10 | 15 | true |
| CP-2 | S1 | CP-1 | policy | `/*/*/TestClassificationPolicyTests/*` | V-2 | all listed, 0 failed, 0 skipped | 42 | 6 | true |
| CP-3 | S1 | CP-1 | guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-3 | all listed, 0 failed, 0 skipped | 34 | 8 | true |
| CP-4 | S1 | CP-1 | slow-registry | `/*/*/SlowTestRegistryMergeTests/*` | V-3 | 5 cases, 0 failed, 0 skipped | 5 | 8 | true |
| CP-5 | S1 | n/a | derived-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false --nologo` | V-4 | exit 0, 0 errors | n/a | 8 | true |
| CP-6 | S1 | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | V-4 | 27 methods, 0 failed, 0 skipped | 27 | 10 | true |
| CP-7 | S1 | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-2 | exit 0 | n/a | 10 | true |

CP-3 Min is the seven named classes: TestClassificationGuardTests 1, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, MechanicalPcContractTests 11, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 11, TestLaneCategoryGuardTests 1. `RunnerDefaultGuidanceTests` is four methods in the same file as `TaskPlatformGuidanceTests` and is outside this filter.
