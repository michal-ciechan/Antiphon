# CARD-1514 S1 ordinary verification completion

Code task `7e3e6bcc`; original Code landing owner
`924f8ae2-e285-400d-8efe-fcd1a16a9ced`. Base product source
`7c1fc93e35f54a9d266dace4b73e800b38db5f82`; full feature base
`fbb2be214a849549cfa72f2e9a17646cc848cfeb`.

This supplements the [frozen S1 TestDesign](../testdesign/2026-11-06-load-telemetry-s1-testdesign.md).
Admission stays unchanged. The explicit brief commissions portable CP-1..6 and
named guards, and defers whole Unit, CP-7/8, V-49..52 and Windows rc startup.
The appended Final profile does not turn those deferred obligations into passes.
Review must retain the incomplete Full/Final gate. All PC-1..102 and their frozen
variants remain pending method-scoped SourceLanding Mutation; no deliberate
mutants are part of this task.

## Findings and invariants

Run `20261107-064711-da0c` at the base product source built successfully. CP-4
had 28 executions, 27 passed and one setup failure; all eleven H3 crash cuts
passed, including exact recovered task-window count/sum/min/max/p95. CP-5 was
40/40 and CP-6 6/6. The sole red case was
`C1514HostLoadApiTests.RoutesReturnDurableProvenanceAndPages`: the first cursor
query was canceled by the unchanged three-second production poll deadline.

The world warmed `Db(false)` with different Npgsql options and a literal query.
The repair warms the actual context resolved from the pump's service provider,
using the parameterized query shape. It does not execute or enqueue a history
poll during setup, change an assertion, or change a timeout. The full red CP-4
row is rerun. CP-5 follows because its script cases share that world fixture.

The two new migration IDs must follow master's previous migration linearly;
the generated snapshot must match the runtime model; Up may only add tables,
columns and indexes; source identity must retain a unique index. Both Down
paths must refuse before losing durable sample bytes, migration history or a
legacy row. The new migration tests inspect registered migrations and exercise
the real PostgreSQL migrator on private historical databases.

The full adjacent `C0824RefOnlySnapshotMigrationTests` class is selected in a
separate expected-red packet before repair: it assumes its three migrations are last and starts historical Down
round trips from the newest template. New irreversible telemetry migrations
make that fixture invalid. Preserve that red before repairing the fixture to
start at its actual historical migration. Do not bypass either Down guard.

The separate run `20261107-073709-5238` at
`87cf5fd6ae717c6f9b5b4f32a3a7d23aa3444f37` executed all four old cases red:
the tail assertion saw the two new migration IDs, and the round trips hit the
new membership Down refusal before reaching their historical subject. Repair
keeps the strict tail assertion, now including all five known IDs, preserves
the exact old Postgres refusal and stored-byte assertions, and applies current
Up migrations before the existing no-pending/model checks. Neither a timeout
nor a destructive-downgrade guard is relaxed.

## Performance and evidence

One assembly container/template per run. Migration baselines use fresh private
databases in that same container because migration history is the subject;
creation/migration phases are recorded. Historical setup has a finite 60-second
budget. Existing 60-second migration bodies and the frozen CP-4/5 deadlines
remain unchanged. New migration bodies use a provisional 60-second limit under
CARD-1514; measure their first results, without timing-only repetitions.

The first current-source cold server build took 835.44 seconds in compiler
output; the checkpoint build phase was 890.98 seconds. Build admission was
195 seconds; each test admission was zero. The whole packet was 1400.49 seconds
wall, 1205.46 execution, 195.03 admission-only. These are observations under
load, not a measured p95. The inherited eleven-build historical p95 of 817.1
seconds supports the unchanged 28-minute build allowance.

Use a source-qualified checkpoint YAML per committed group, at most three
rows, one isolated build with `UseAppHost=false`, row caps CP-4=8 minutes,
CP-5=6 minutes, CP-M1/CP-OLD/CP-M2=6 minutes and CP-G=6 minutes. Build allowance stays
28 minutes, admission 10 minutes per acquisition. Group FIXTURE uses execution
allowance 50 minutes and hard wall 90 minutes; group MIGRATION-REPAIR uses 42
and 72 minutes; HISTORICAL-RED uses 36 and 56 minutes. These phase sums include settlement and retain separate
admission accounting; the manifest records a total override reason. Expected
ordinary wall without queue waits is below 30 minutes per group; a run above
30 minutes requires bounded performance Debug before repeating it.

The final group also reruns exact CP-6 with its unchanged six-minute cap because
the new Slow class/registry changes its compiled-metadata inputs. It shares the
same build with CP-M2/CP-G, keeping three rows and the existing 42-minute charged
and 72-minute hard-wall budgets. CP-G and CP-6 have five cheap guard executions
in common; retain both closed filters and do not count this overlap as a PC.

Store raw TRX, unchanged CHECKPOINT lines, clean source receipts and verified
build provenance under ignored `.antiphon/checkpoints/`. Inspect every intended
class/method and nonzero count. Never rerun unchanged green rows. CP-M2 is only
needed for actual red migration cases. The cheap named guards run as one union
filter after the final registry/test changes. The adjacent category census is
included because this packet adds a test class. Whole Unit is explicitly owed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-4 | FIXTURE | tests/Antiphon.Tests -> bin-c1514-7e3e6bcc-f1b/ | s1-delivery | `/*/*/(C1514HostLoadHistoryTests*)\|(C1514TaskLoadWindowTests*)\|(C1514HostLoadApiTests*)/*` | V-19..27,V-31..35,V-41..44 | full affected classes, including all 11 H3 cuts | 28 | 4 | true |
| CP-5 | FIXTURE | CP-4 | s1-surfaces | `/*/*/(C1514HostLoadAggregateTests*)\|(C1514HostLoadRecommendationTests*)\|(C1514HostLoadScriptTests*)\|(HostStatsCacheTests*)\|(HostStatsPollServiceTests*)\|(HostStatsAntiphonCountersTests*)\|(HostStatsEndpointTests*)\|(SessionRunnerHttpClientHostStatsTests*)/*` | V-28..30,V-36..40,V-45..48,R-1..4 | full frozen row after common world setup repair | 40 | 2 | true |
| CP-M1 | FIXTURE | CP-4 | migration-behavior | `/*/*/C1514HostLoadMigrationTests*/*` | additive migration chain and durable downgrade refusal | all six results, zero failed/skipped | 6 | 3 | true |
| CP-OLD | HISTORICAL-RED | tests/Antiphon.Tests -> bin-c1514-7e3e6bcc-old/ | historical-red | `/*/*/C0824RefOnlySnapshotMigrationTests*/*` | historical fixture incompatibility before repair | retain exact assertion/Down failures separately from green packets | 4 | 2 | true |
| CP-M2 | MIGRATION-REPAIR | tests/Antiphon.Tests -> bin-c1514-7e3e6bcc-f2/ | migration-green | `/*/*/C0824RefOnlySnapshotMigrationTests*/*` | repair historical migration fixture | all four results, zero failed/skipped | 4 | 2 | true |
| CP-G | MIGRATION-REPAIR | CP-M2 | named-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-5 and explicit mechanical/platform/layering guards | prior named packet 75 plus one adjacent category census, zero failed/skipped | 76 | 3 | true |
| CP-6 | MIGRATION-REPAIR | CP-M2 | s1b-guards | `/*/*/(TestClassificationGuardTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestLaneCategoryGuardTests*)\|(LiveLaneRunnerTests*)/(Registry_matches_compiled_metadata*)\|(Policy_rows_require_pinned_attribute*)\|(Pinned_attribute_contract_matches_native_platform*)\|(Markers_do_not_expand_policy_exclusions*)\|(every_test_class_is_tagged_unit_xor_integration*)\|(WrapperOnlyPaidManifest*)` | R-5 after new class/registry | all six frozen guard methods, zero failed/skipped | 6 | 2 | true |

## Policy resolution

Do not add Windows-on-Linux nightly exclusions: the native Windows methods
remain explicit opposite-OS skips, and the scoped CP-7/8 selections choose the
proper OS methods. The frozen legacy-exclusion digest remains intact. No new
conditional-skip contract is needed for portable zero-skip rows under the landed
CARD-1217 S1 receipt rules. Native execution and Windows qualification remain
owed; an opposite-OS skip is no native pass.

The stored nightly policy hash and live-lane protected-input digest both match.
This packet does not change their inputs. If a later policy change is necessary,
refresh `policyHash` and `PinnedInputDigest` together; do not loosen the guard or
silently grant a conditional skip. The detailed report retains old/new pins and
the per-method disposition of historical preservation-red evidence gaps.
