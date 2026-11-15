# CARD-1925 production EF clock witnesses

Task 8112d494, Code Final. Base d7ad08b71a19fa79e744284e5ffe7f0168de561a.
Test-only; no production query, selector, host-load, migration or policy changes.

## Verification design

V-1: execute AgentTaskDispatcher.DispatchQueuedAsync twice with an injected clock advanced
seven minutes; the optional expiry UPDATE has bound clock values, identical command text,
one observed compilation, and gen_random_uuid() in SQL.
V-2: execute ChannelOutboundDeliveryPump.ClaimAsync twice (existing private operation via
reflection); both the claim cutoff and lease expiry are parameters and compilation occurs once.
V-3: execute SessionMessageQueueService.CancelExpiredBriefsAsync twice with fresh expired
messages; assert canceled messages, distinct bound completion times, stable SQL and one compilation.
V-4: execute OutboundConversionTaskRunner.CreateAsync through its real validation and fence;
a nonexistent delivery deliberately loses its fence. Assert two executed UPDATE commands,
one compilation, database now(), and no DateTime parameter. No synthetic LINQ is used.
R-1: neighbouring EfLambdaClockGuardTests and EfCompiledQueryCacheTests.
R-2: the brief's entire guard union. Matching impacted-classes glob is test-lane-category,
covered here. No BeginPhase calls, limiter, production, configuration or shared harness edits.

The probe implements EF's compilation interceptor and command interceptor. Every method
owns a fresh internal EF service provider, so a prior test cannot prewarm its cache. It
observes two actual calls, requires nonzero command counts, and checks parameter values
against the externally controlled clock, not just against each other. The private calls
use normal service constructors; unused external dependencies remain null and no runner,
worktree manager or message producer is invoked. Zero affected rows intentionally keeps
these SQL-shape witnesses independent of dispatch/transport; existing neighbours cover behavior.

## Performance budget

One standard TestDbFixture container/template per session and one clone per witness class,
lazily created and disposed by a class-shared data source. SQL is executed on that clone;
no production database/container is used. Two calls per method are the proof selection,
not repeated runs. Estimates are provisional: witness 1 minute, neighbours 1 minute,
each guard group 2 minutes; cold build up to 10 minutes and broker admission separately
up to its existing 45-minute bound. No new test timeout. Use serial checkpoint rows and
a 10-minute driver deadline. Record checkpoint phase journal, TRX test durations, SQL
stdout, source receipts and /proc/loadavg in ignored evidence. Missing phase detail is unknown.
No full Unit/assembly run: this task has bounded test-only impact.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1925-a/ | production-clock | `/*/*/EfProductionClockQueryTests/*` | V-1,V-2,V-3,V-4 | 4 passed | 4 | 1 |
| CP-2 | S1 | CP-1 | adjacent-clock | `/*/*/(EfLambdaClockGuardTests*)\|(EfCompiledQueryCacheTests*)/*` | R-1 | all passed | 14 | 1 |
| CP-3 | S1 | CP-1 | policy-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | R-2 | all passed | 5 | 2 |
| CP-4 | S1 | CP-1 | contract-guards | `/*/*/(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-2 | all passed; layering VIOLATIONS 0 LISTED 162 | 5 | 2 |
| CP-5 | S1 | CP-1 | derived-guard | `/*/*/DerivedImpactIndexTests*/*` | R-2 | all passed | 1 | 2 |

## Manual acceptance

Inspect fresh TRX for each intended method/class and nonzero counts. Validate exact committed
source provenance; retain unedited CHECKPOINT lines and report slot and waited. Verify nightly
policy/digest pins unchanged. Run evidence-diff over full base..HEAD and merge-tree against
fresh origin/master. Inspect OS branches: no OS-selected production behavior or startup
changes; restart none. No native witness obligation introduced.

## Positive controls

All pending for caller-commissioned method-scoped SourceLanding Mutation. The operator
brief waives Mutation checks, but this Code stage does not discharge these controls.
PC-1: re-inline dispatcher deadline UtcNow() (V-1).
PC-2: re-inline outbound ClaimAsync cutoff UtcNow() (V-2).
PC-3: re-inline queue CompletedAt UtcNow() (V-3).
PC-4: hoist conversion DateTime.UtcNow into a local (V-4).
PC-5: hoist dispatcher bulk Guid.NewGuid() into one local token (V-1).
Each must fail an assertion in its exact test method, then restore and pass. Binding,
missing-control discovery and mutation execution belong to Mutation, not this Code task.
