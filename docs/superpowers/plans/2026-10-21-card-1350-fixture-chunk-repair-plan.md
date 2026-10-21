# CARD-1350 fixture class chunk repair

Original Code task e7e642d4. This branch is the landing owner. Base is
fa8534122789887930f789ccdb61d80697db3bdd.

## Decisions

D-1. Strip the TUnit constructor-fixture suffix before chunk grouping, class
filters, exact filters, roster matching, policy comparison, and TRX keys.
Parsed discovery nodes keep the full suffixed TypeName. TUnit 1.44 builds the
treenode path from the metadata class name and does not include constructor
arguments, so a filter that keeps `(Fixture)` does not select the test.
Two constructor instances of one class share one chunk because the class
segment cannot select one of them.

D-2. Do not widen the class-filter character class. Real Antiphon.Tests
discovery at this repair, after the suffix strip, has no `+` and no backtick
in a bare class name.

D-3. The fixture, prerequisite-gate, prerequisite-settle, and unattended
admission cases run from the harness default loops and from TUnit wrappers.
`C487_G087` stays a `-Case` invocation: its PASS lines do not use the `C487`
prefix, so a ScriptHarness wrapper would not count them.

## Verification design

- V-1: fixture and generic TypeName records parse, and an empty TypeName stays truncated.
- V-2: a fixture-named discovery builds rc chunks and an exact roster with zero invalid-class-filter, and the launched filters contain no `(`.
- V-3: the default harness loops call the fixture and prerequisite cases.
- V-4: a failed npm prerequisite stops the pipeline and summary testsPassed is false.
- V-5: prerequisite settle writes summary testsPassed from the last run.
- V-6: the card census follows pageToken (`C487_G087`).
- R-1: one checkpoint-tool run of the command rows below, exit 0, buildSource=notApplicable.
- No PC-n rows. Mutation owns any missing-control discovery. None are marked passed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | fixture-typename | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1044_FixtureTypeName` | V-1 | exit 0 | n/a | 2 | TEMP=/tmp |
| CP-2 | all | n/a | fixture-chunks | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1044_FixtureClassChunks` | V-2 | exit 0 | n/a | 2 | TEMP=/tmp |
| CP-3 | all | n/a | unattended-cases | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1044_UnattendedCases` | V-3 | exit 0 | n/a | 2 | TEMP=/tmp |
| CP-4 | all | n/a | prerequisite-gate | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C487_PrerequisiteGate` | V-4 | exit 0 | n/a | 2 | TEMP=/tmp |
| CP-5 | all | n/a | prerequisite-settle | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C487_PrerequisiteSettle` | V-5 | exit 0 | n/a | 2 | TEMP=/tmp |
| CP-6 | all | n/a | card-census | `pwsh -NoProfile -File scripts/test-nightly-report.ps1 -Case C487_G087` | V-6 | exit 0 | n/a | 2 | TEMP=/tmp |

### Unlisted

The real Antiphon.Tests `--list-tests` discovery is not a checkpoint row. It is
qualification evidence for D-1 and D-2: one isolated build, one list-tests
diagnostic, then the parser, rc chunking, and exact roster over that log.
It does not execute the suite. Measured on log_261021090433252.diag at the
committed repair before this plan: markers=19047, parsed=19047, fixture
nodes=569, fixture types=73, bare fixture classes=73, collapse=0, no `+` and
no backtick in any bare class name. rc antiphon on linux and on windows:
22 exclusions, 1346 chunks (1339 class, 7 exact), invalid=0, no `(` in a
filter. Exact roster of one member per fixture type: 73 filters, no error.
The earlier census was about 19012 records and 1344 chunks. This tree adds
NightlyDeadOwnerLockTests and NightlyPrerequisiteTests, which is the two
extra class chunks. Fixture counts are unchanged.

### Cost

Ordinary floor is 12 minutes.
