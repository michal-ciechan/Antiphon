# CARD-1591 S1 mutation candidates (unbound)

Repair Code task 2ede815a; original landing owner 73c027ae-8ab4-4e6d-a145-50d426f513ea.
These are candidates, not a bound Mutation pack or executed controls. Every row and
variant is PENDING for caller-commissioned method-scoped SourceLanding Mutation
following ordinary Review and publication. Mutation owns binding to landed source,
deliberate mutants, assertion-red/restoration/green, and missing-control discovery.
A native pre-fix run of actual defects is ordinary evidence, not a Mutation cycle.

All detecting methods below belong to ChannelInputWithheldIncidentTests; a bound
pack must use the precise /*/*/ChannelInputWithheldIncidentTests/ExactMethod filter.
Do not run a whole class per mutation. Do not change timeouts or assertions.

| Candidate | Original | Pending variants | Detecting method |
|---|---|---|---|
| PC-1 | M1 | omit durable stamp update | C1591_RepeatScanStaysOneIncident (includes cursor wrap) |
| PC-2 | M2 | age by acceptance; age by creation, in both paging/claim checks | C1591_YoungWithholdStaysQuiet |
| PC-3 | M3 | remove WithholdCount predicate in both paging/claim checks | C1591_WorkingUnchargedRowStaysQuiet |
| PC-4 | M4 | call flush; acquire session semaphore | C1591_ScanWritesNoInput |
| PC-5 | M5 | wrong kind; wrong reason | C1591_AgedWithheldChannelRowRaisesOneIncident |
| PC-6 | Review | remove stamp guard from claim (including after cursor wrap) | C1591_RepeatScanStaysOneIncident |
| PC-7 | Review | remove Pending checks in both stages (cancelled control) | C1591_OpenDispositionDoesNotIncident |
| PC-8 | B2 | remove both transcript Working checks | C1591_WorkingSessionKeepsHistoricalWithholdQuiet |
| PC-9 | B2 | remove suspension; continuity; liveness (including legacy); compaction; Herdr hold exclusion | C1591_CurrentHoldKeepsHistoricalWithholdQuiet |
| PC-10 | B2 | ignore continuity; suspension; liveness inbound stamp | C1591_ExistingHoldStampSuppressesSecondIncident |
| PC-11 | B3 | omit both queue-row serialization claims | C1591_OpenDispositionDoesNotIncident |
| PC-12 | B4 | lose partial index; unbound page; stop cursor advancement; materialize envelope; per-row queries | C1591_BacklogPassIsBounded |
| PC-13 | B6 | let non-shutdown cancellation escape loop | C1591_NonShutdownCancellationContinues |
| PC-14 | B5 | commit stamp separately before incident persistence | C1591_FailedBatchRollsBackAndConcurrentHostsStayOnce |
| PC-15 | B5 | ignore persisted stamp after service recreation/history prune | C1591_RestartAfterPruneDoesNotReraise |

The actual scan never uses dialog snapshots as input authority and never calls a
queue flush. Any new unsupported mutation or missing assertion remains open for
Mutation to report; this list claims no red/green certification and no completeness.
