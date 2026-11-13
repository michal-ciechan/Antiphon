# CARD-1836 S2 execution scope

Code task `ff777e65`, base `cc48c70050a2932eddf4abe18cea8659d478c33e`.
Implements only S2 in [the landed plan](2026-11-09-card-1836-incremental-rc.md).
The original CP-2 red run is `20261112-235849-fee4`, source
`c9b7d6b338d6027934edfc657c40692ba8f38ee2`: 4 executed, 4 assertion failures,
each naming the absent boundary library. Build succeeded, source clean/verified,
slot granted, waited 0s. It cost 425.225s wall, including 384.001s build/preflight
and 17.463s execution. No failure is represented as green.

The four methods cover V-5, V-6, V-7 and V-8. Literal paths follow the ordered
layer rules; lane overrides precede them; namespace fallbacks are conservative.
The canonical ID encoding is compact UTF-8 JSON `[suite,boundary,[sorted keys]]`,
ordinal and case sensitive, without BOM or trailing newline. Duplicates and empty
membership refuse. Chunk construction rejects mixed suites/boundaries and marks
unassigned dirty. This is a library API, with no launch caller or packing change.

The exact S2 CP-2 filter is retained below. Its green output name differs because
the failed run retains its owned output. CP-11 is the brief's guard union plus
mechanical/platform/layering guards and the Antiphon impacted classes. CP-12 and
CP-13 are the other impacted `spawn-limit-rosters` projects. Scope was resolved
with `scripts/impacted-classes.ps1` over the full base-to-HEAD diff, with no cap.
These additional rows explain all verification beyond the original CP-2.
Direct harness probes, if needed for a failing script case, run under build-slot
and are diagnostic; they do not replace CP-2 or its fresh TRX.

Repair history: `20261113-000849-18b3` tested implementation SHA
`3cb3c32ccc2facef7c113bddec494b8c7d9a2816`. CP-2 passed 4/4; CP-11
ran 162 tests with 160 passed and 2 failed: `committed_index_matches_the_builder`
(stale index) and `regenerate_script_reports_changed_then_unchanged` (native
helper not yet built). CP-11 cost 662.462s execution; DerivedImpactIndexTests
accounted for 564s and TestClassificationPolicyTests for 70s. These are measured
single-run costs, not a p95. The run was explicitly stopped and joined at the
queued CP-12/CP-13 builds; those rows did not execute. The canceled aggregate
receipt is ineligible for final validation, even though CP-2's own source is
verified. Run all four rows at the repaired committed tip for one complete green
receipt. The red output is retained, hence the new final build directory.

The prescribed regeneration script built the native helper under a granted slot
(waited 45s, maxcpucount 6 forwarded by the script), then updated only
`tests/derived-impact-index.json`; commit that generated index separately.
Two diagnostic harness invocations passed `TwoBoundariesNeverShare` and
`StableIdIgnoresOrder` (each slot granted, waited 15s). No timeout or assertion
was changed. The final run uses the already-built checkpoint tool directly;
each isolated build/test driver acquires its own slot, without an outer launcher
lease held across the entire checkpoint loop.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1836-s2final/` | boundary | `/*/*/RcBoundaryChunkIdTests/(StableIdIgnoresOrder*)\|(MovedClassChangesId*)\|(UnassignedStaysAlone*)\|(TwoBoundariesNeverShare*)` | V-5 V-6 V-7 V-8 | all listed, 0 failed | 4 | 4 |
| CP-11 | S2 | `tests/Antiphon.Tests -> bin-c1836-s2final/` | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(ProcessSpawnLimitTests*)\|(TestLaneCategoryGuardTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/*` | guard union and impacted classes | each class nonzero, 0 failed | 11 | 6 |
| CP-12 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s2pty/` | pty-roster | `/*/*/ProcessSpawnLimitTests/*` | spawn-limit-rosters | nonzero, 0 failed | 1 | 2 |
| CP-13 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s2runner/` | runner-roster | `/*/*/ProcessSpawnLimitTests/*` | spawn-limit-rosters | nonzero, 0 failed | 1 | 2 |

## Performance budget

No AppHost, database, browser, provider or native pty fixture is needed. The new
four cases launch isolated PowerShell harness processes with the existing 90s
execution/10s cleanup budgets and 120s test timeout. The red test wall was 7.725s;
no green p95 exists yet. Original estimates remain provisional under CARD-1836;
guard estimates are provisional too. Record first green timings and phase fields
in the ignored checkpoint reports; unknown nested phases stay unknown. No run is
expected over 30 minutes. Builds remain isolated, sequential and slot gated.

## Deferred work and manual checks

S1's V-1..V-4 are historical, not rerun or re-credited here. V-9..V-28 and R-1/R-2
belong to S3..S10 and are deferred to those slices; they are not passed by S2.
PC-V-5 (ordinal IDs), PC-V-6 (membership omitted), PC-V-7 (unknown joins core),
PC-V-8 (mixed boundaries) remain pending for Mutation. Operator waived deliberate
mutation execution in this Code brief. No intentional mutant runs in Code.

Manual acceptance: inspect that the library has no production qualification caller,
the Full launch source is unchanged, and layers.txt is unchanged. No migrations.
There is no changed OS-selected behavior. The new scripts/lib files are portable
and not imported by startup; the broad startup-path census still requires a
pending Windows native startup receipt before a later rc pick. The orchestrator
owns that separate receipt; no restart is required for this library-only task.
