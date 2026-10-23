# CARD-1563 timeout audit execution and verification

Code owner b81094be; base 4497af9b48136a8003def54331f49ac66fd2dec9.
This is the Code execution manifest, not a separate Plan-stage commission.

Smallest scope: inventory and lint test/tool timeout declarations, join comparable receipt
measurements and the existing nightly cost model, propose rounded 2x nearest-rank p95
budgets, and report provisional collection obligations. Do not tighten existing deadlines
from partial, failed or unknown-load data. Do not change checkpoint execution (CARD-1531),
fixtures (CARD-1562), or live startup instrumentation (CARD-1564).

## Performance budget

The independent Unit test project exercises real source inventory, TRX/checkpoint import,
statistics and the policy check using temporary files, without expensive infrastructure.
Six tests should take under 5 seconds; build estimate 30 seconds; both are provisional
CARD-1563 values until observed. Each test has a 30-second initial ceiling. Row and build
execution are explicitly bounded to 3 minutes for this first observation; admission wait
is separate: launcher wait is 5 minutes; checkpoint admission retains its 45-minute default. Record fresh TRX, checkpoint source receipts,
slot wait, build and execution phases under .antiphon/checkpoints; missing phases remain
unknown. No full-assembly Antiphon.Tests run; no shared runtime invariant changes.
No repeats; one initial red selection, then one green selection. No AppHost impact.

V-1: inventory attributes, helper deadlines, policy watchdogs, tools and checkpoint rows.
V-2: comparable nearest-rank p95; preserve failures, exclude them from successful estimates.
V-3: check rejects excessive deadlines unless exact declaration has reason, measurement, card.
V-4: row execution uses test-host wall, excluding admission and build; exact filter matching.
R-1: legacy cost-model totals and missing history remain provisional, never p95.
R-2: malformed or unattributed evidence remains visible and cannot qualify a measurement.

PC-1 through PC-6 pending SourceLanding Mutation, one per test method and guarded behavior;
Mutation owns deliberate defects and discovery of missing controls. No mutation performed here.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---:|---|
| CP-1 | S1 | tests/Antiphon.TimeoutAudit.Tests -> bin-c1563-red/ | initial-red | `/*/*/TimeoutAuditTests/*` | V-1,V-2,V-3,V-4,R-1,R-2 red | TimeoutAuditTests | 6 | 1 | true |
| CP-2 | S2 | tests/Antiphon.TimeoutAudit.Tests -> bin-c1563-green/ | audit-green | `/*/*/TimeoutAuditTests/*` | V-1,V-2,V-3,V-4,R-1,R-2 | TimeoutAuditTests | 6 | 1 | true |
| CP-3 | S3 | n/a | census-red | `pwsh -NoProfile -File scripts/test-test-selection.ps1 -Case C1313_ProjectAndCatalogUniverse` | R-3 red | exit 1; C1313 project-universe fails on unclassified audit test project | n/a | 1 | true |
| CP-4 | S4 | tests/Antiphon.Tests -> bin-c1563-main/ | linked-audit | `/*/*/TimeoutAuditTests/*` | V-1,V-2,V-3,V-4,R-1,R-2,R-3 | TimeoutAuditTests | 6 | 1 | true |
| CP-5 | S4 | CP-4 | census-regression | `/*/*/TestSelectionCatalogTests/*` | R-3 and existing catalog/live/mixed smoke | TestSelectionCatalogTests | 3 | 1 | true |

## S3-S4 qualification census integration

The real-repository audit exposed that a new IsTestProject must join the existing project
census. S3 records the existing census witness red. S4 will register the small standalone
checkpoint host as a child whose same test source is linked into the primary Antiphon.Tests
assembly; main qualification thereby runs these tests without adding a suite or watchdog.
A main-assembly build and its bounded classes are necessary integration checks. Historical
build evidence at 647183914215a3bbcea84a225859f1fcf2e9501d was 207.08 seconds;
a fresh main build is provisionally bounded to 8 minutes (about twice that observation,
rounded) separately from admission. No test attribute timeout is widened. The two new
main-assembly execution rows expect under 60 seconds each including host startup, with
CARD-1563 provisional collection obligations. No repeats beyond the changed integration.
R-3: the shipped test-project universe remains closed, with seven primary/six child projects,
and main qualification includes the six audit tests. CP-3 is its pre-fix script red witness;
CP-4 and CP-5 cover the linked audit source and the full affected TestSelectionCatalogTests.
PC-7 pending SourceLanding Mutation for the source-link/census obligation.

