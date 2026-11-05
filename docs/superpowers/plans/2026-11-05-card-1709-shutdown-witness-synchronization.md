# CARD-1709 shutdown witness synchronization

Original Code/landing owner: 51b0c438-dbd2-4840-9ad0-05e32af02333.
Base: e6da2453013e7312c52c6ac6c117fc86b6c790d3. S1 changes the notice-only
shutdown witness and its test-only timer observer. Production is unchanged.

## Verification design

Mutation 6569dd36 identified B11b's notice-only witness race: an enqueued row
does not establish that the recovery pass reached its next wait. TimerClock
signals a TaskCompletionSource after the real fake-clock timer for the
fifteen-minute backstop is created. The witness awaits that signal after the
retry enqueues the notice, before StopAsync. At this boundary the recovery wait
has already received its cancellation token; cancellation cannot bypass that
call. No wall-clock settling delay is introduced. Existing twenty-second
observation and ten-second shutdown bounds and all assertions are retained.

| ID | Selection | Observable outcome |
|---|---|---|
| V-1 | TaskSupervisionReceiptTests.C1709_Pass_whose_only_busy_item_is_a_supervision_notice_retries_before_the_backstop | Five-second busy retry queues the notice, then the actual backstop timer is created; shutdown completes within the existing ten-second bound. |
| R-1 | Full TaskSupervisionReceiptTests and CompletionNoteDurableSweepTests | Recovery, ordering, retry and shutdown behavior remains green. |
| R-2 | Full ChannelBridge*, ChannelInboundRecoveryTests and ChannelInputWithheldIncidentTests | Messaging neighbors remain green. |
| R-3 | Full TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire* | Classification and registry guards remain green; wildcard names must be checked against the actual compiled roster. |
| R-4 | Whole Unit lane | All Category=Unit cases execute, including method-level categories. Unit alone does not cover delivery, landing, leases or persistence. |

PC-1 / B11b remains pending for method-scoped SourceLanding Mutation: replace
the cancellation token passed to flushes.Recovery.WaitAsync with
CancellationToken.None in CompletionNoteWorkHostedService.RecoverAsync. Exact
selection: `/*/*/TaskSupervisionReceiptTests*/C1709_Pass_whose_only_busy_item_is_a_supervision_notice_retries_before_the_backstop`.
Expected red is the ten-second StopAsync timeout after the backstop signal;
restore, fresh build and the same method green are Mutation's responsibility.
No deliberate product mutant is authored by Code. No twenty-repeat proof is
claimed: the standing repeat cap is three normal plus two loaded executions,
and the supplied Mutation report is not a Review exception. No repeat is needed
after an ordinary green result.

## Performance budget

One native incremental compile is explicitly required by the brief before the
cold checkpoint build. A separately leased native build bootstraps the checkpoint
tool. These two non-checkpoint builds are declared exceptions, not test credit.
Every build uses native defaults with UseAppHost=false and TEMP=/tmp. Cold
checkpoint output is isolated and shared by the three serial rows. At most three
rows run in one invocation. Use --expected-source-sha with committed HEAD.

Debug 5ae98d31 measured cold builds at 7-14 minutes under seven tasks sharing four
slots. Native and checkpoint build envelopes are 28 minutes, twice that measured
maximum; admission is separately bounded at ten minutes per native build and by
the checkpoint broker's configured allowance. Recovery's historical 43-case pack
was seconds-to-minutes (Mutation 6569dd36); all named integrations provisionally
budget 8 minutes excluding build, guards 2 minutes. Per-row timeout is explicitly
28 minutes, unchanged throughout this task, not increased after a failure.

Whole Unit is a required cross-cutting lane, not an inference from this tiny
change. Its class set cannot be bounded because both class and method Unit
categories define the lane. CARD-1720 and the testing owner's Unit-lane section
record its unmeasured runtime and missing Full baseline; the killed 535-second
run is not a p95. Provisionally budget 28 minutes, then report timeout/incomplete
without weakening selection or treating class shards as Full. Any actual run
over 30 minutes requires caller-owned Debug follow-up; this delegate cannot
sub-delegate. Total importer allowance is derived from estimates, not lowered.

Existing shared Postgres/schema fixture ownership is unchanged. Record fresh TRX
per-class/method counts, per-test durations, build and admission separately in
ignored checkpoint evidence. Uninstrumented setup/database/IO/teardown phases
remain unknown. No full assembly, desktop, rc startup, optional configuration or
OS-selected production behavior changes; OS witness applicability is none.
Restart: none. Manual acceptance is inspection of timer/cancellation ordering.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1709-shutdown/` | affected-integration | `/*/*/(TaskSupervisionReceiptTests*)\|(CompletionNoteDurableSweepTests*)\|(ChannelBridge*)\|(ChannelInboundRecoveryTests*)\|(ChannelInputWithheldIncidentTests*)/*` | V-1, R-1, R-2 | all listed, 0 failed | 50 | 22 | true | `TEMP=/tmp` |
| CP-2 | S1 | CP-1 | classification-guards | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/*` | R-3 | all listed, 0 failed | 4 | 2 | true | `TEMP=/tmp` |
| CP-3 | S1 | CP-1 | whole-unit | `/*/*/*/*[Category=Unit]` | R-4 | >= 1 executed, 0 failed | 1 | 28 | true | `TEMP=/tmp` |
