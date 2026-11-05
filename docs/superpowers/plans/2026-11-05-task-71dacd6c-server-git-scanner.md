# Server git launch scanner repair

Owner: Code task 71dacd6c. Base: d10e1c51a72852aa51895109b66e567f81346dca.
Test-only repair; no product, delivery, landing, lease or persistence changes.

S1 commits the source-text seam and regression cases with the old scanner, red.
S2 replaces construction-segment matching with syntax-based method/local identities.
Only LandingGit.InProcessEligible's local probe may be non-launching. All thirteen
actual launches require their own guard after configuration and before launch.
Messages identify file, method and local/start site. New launch constructions fail
closed, even when the inventory also changes. Comments cannot supply a guard.

## Performance budget

Debug 3e640ea9 measured the affected six-case class at 6.3 seconds and a cold
build at 254-710 seconds on Linux. New source-text cases require no processes or
shared setup. The existing real-git fixture remains responsible for its owned
repositories and children. Build admission is separate (broker default 45 minutes).
CP-1's nine-minute estimate is provisional against that small build sample;
the tool's existing derived build/row deadlines apply without assertion or
test-timeout changes. Guards are provisionally one minute. The mandatory whole
Unit lane is cross-cutting repository policy verification, not product impact;
its classes cannot be bounded while claiming the whole lane. Its current cost is
unknown and docs/testing-and-build.md records a >30-minute historical warning;
CP-3's 20-minute estimate is provisional and requires Debug follow-up if observed
over 30 minutes. The task's 45-minute budget may leave that receipt incomplete.
No whole-assembly selection is authorized. Retain fresh TRX durations, exact
membership and checkpoint build/admission timings in ignored output. Phase timing
is opt-in; unattributed fixture/IO/setup costs remain unknown, not zero.
No OS-selected production branch or startup path changes; restart: none.

## Verification design

V-1: current source accepts fourteen constructions and thirteen guarded launches;
the explicit probe never reaches a launch. Entire ServerGitProcessTests class.
V-2: source-text regression cases reject either missing launch guard, a third
unguarded launch, and static/wrapper/instance probe launches with site names.
R-1: preserve existing real git gc.auto/maintenance.auto and append/invalid-count cases.
R-2: all four named classification/slow-registry guard classes.
R-3: whole Unit lane; no substitutions or deferred IDs credited as passed.
Manual: product diff empty; full-range evidence guard; conflict-free merge-tree;
fresh TRX identity/count inspection and source/provenance validation.

PC-1 pending Mutation: remove ExecuteProcessAsync guard.
PC-2 pending Mutation: remove RunGitInputBytesAsync guard.
PC-3 pending Mutation: launch the probe via Process.Start, StartProcess, instance Start.
PC-4 pending Mutation: third unguarded launch; comments/wrong-local/late guards.
These deliberate red/restore/green cycles belong to SourceLanding Mutation;
ordinary synthetic regression assertions do not discharge PCs.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1432/` | server-git | `/*/*/ServerGitProcessTests*/*` | V-1, V-2, R-1 | all listed, 0 failed | 13 | 9 |
| CP-2 | all | CP-1 | guard-policy | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/*` | R-2 | all listed, 0 failed | 4 | 1 |
| CP-3 | all | CP-1 | unit | `/*/*/*/*[Category=Unit]` | R-3 | >= 1 executed, 0 failed | 1 | 20 |
