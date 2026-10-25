# CARD-1584: provisional E2E launch bounds

The caller supplied Windows rc run a779085c09ec4397a4b49941fd3cad07 at
candidate a97194ee7a4adf215454fd82c453ba46362596ee: the 166-case E2E
launch was priced at 119.5 seconds, reached its 7201.2-second watchdog and
lost TRX. Only 89 cases started; 88 finished (83 passed, five errors).
The 51 completed landing cases summed 7147.9 seconds; the slowest supplied
completed case was ReviewToLandReceiptMatrix(idle, spill), 348.1 seconds.
These are caller-supplied single-run observations, not measured p95 or an
independent full-class wall time. Native execution and live run db2c2088
are outside this task's scope and custody.

## Change

Price each expanded landing case at 600 seconds (1.72x the slowest supplied
observation), including unobserved siblings. Preserve each original matrix
argument exactly once while exposing four large matrices as methods with
at most two rows. Their existing bodies become private helpers; assertions,
timeouts, class-level process limits and fixture setup are preserved.

Every E2E class has an explicit cost row. Unmeasured classes have provisional
conservative bounds, clearly distinguished from observed bounds: 600 seconds
for native dispatch delivery, 500 for retirement delivery, 300 for app/browser
cases, and 10 for local guards/metadata. These are estimates to replace with
complete Windows qualification measurements; this work does not establish
native correctness, native runtime savings or phase attribution.

The existing first-fit packer targets 900 seconds. E2E classes above 1500
seconds use its existing method splitter. E2E launches above 1800 seconds
refuse before execution, including a future unsplittable argument matrix.
Other suites retain their 2400-second class/launch limits. The E2E watchdog
stays 7200000 ms. No receipt schema or exclusion policy changes.

A committed source roster at base e5a867361937752b959c1451824ab9716fac83c0
records all 177 expanded cases (synthetic planner identities, not TUnit UIDs).
The witness rereads current source, normalizes only the wrapper batch suffix,
and compares every original method/argument identity against that roster.
It then evaluates the real planner's emitted execution filters against all
nodes, including excluded ones, and verifies all 170 required cases exactly
once and all seven excluded canaries zero times. No E2E body executes.

## Verification scope and performance budget

Caller-authorized Debug scope: direct red planner witness; one isolated Linux
build of Antiphon.Tests and the changed Antiphon.E2E project in one driver;
then C1514LandingSplitBudgetTests with --no-build, including the full-listing
planner census regression. No Unit lane, E2E or native test execution.
The temporary two-project solution is .antiphon/c1584-build.slnx. Build through
scripts/build-slot.ps1 into bin-c1584/. Run the four-method planner class
through that same gate with --no-build and --property:OutputPath=bin-c1584/.
Expected planner row under three minutes; build and slot wait are separate.
No infrastructure setup is added to planner tests. Native startup/setup/body
and teardown timings remain unknown here. Cost rows are provisional until a
release-owned Windows run; do not widen production/test timeouts.

Red at d8f453ff8b03bb916990c630d82eba18df66013d: one expected assertion failure,
three launches; original 166-case group independently bounded at 60440 s,
with the unchanged 7200000 ms watchdog. No setup error or census failure.
An intermediate cost-only attempt still made a 2400-second class launch;
the E2E class budget now splits it. Initial repaired witness: one pass,
74 launches, largest conservative bound 1500 seconds, 170 required cases
once, seven excluded. Full build/TUnit results are recorded in the task report.

CARD-1585 is deferred: preserving per-suite plans must also preserve their
assembly/source bindings and summary joins. Merely appending groups under
the last suite's existing top-level binding would produce misleading evidence.
It needs its own multi-suite manifest/summary witness; no sidecar schema is
changed in this focused repair.

The isolated two-project build at 6c35542b2b2afa4298369c228e03adca17c00c95
completed in 176.12 seconds (slot held 177 s, waited 0 s, max CPU count 6),
zero errors and 1201 compiler warnings. The four-method compiled planner
run at that SHA had three passes, one failure, zero skips in 45.955 seconds:
only C1410 suite-hours failed. Its full-listing fixture also contains the shared
TestClassificationGuardTests node, whose cost changed from 0.5 to 10 seconds.
The exact golden total increases by 9.5 to 52631.653116531146 seconds; all
107 launches, 19284 required nodes including synthetic probes, zero missing/
double/extra selections and existing packing/watchdog assertions were intact.
The final repair changes only the PowerShell expected arithmetic and this
record; the already built host and E2E binaries remain applicable. Rerun only
C1410_MeasuredCostGroups with --no-build. Historical red evidence is retained.
