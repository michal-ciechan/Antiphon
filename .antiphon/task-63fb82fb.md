# CARD-1634 Review round 2 (diff-only) — Clean

Subject: origin/feat/card-task-96661539 tip df92aa7b7886b278514dc73dc9e53dc461baa781.
Range reviewed: d7fdee50be88e8e757d06542969b425a1b492b1f..df92aa7b7886b278514dc73dc9e53dc461baa781 (dd8b72512, 1186580e2, e8610de8b, df92aa7b7).
No blocker defects.

## (a) A-1: retry only a missing reply
- `PtyHostClient.ConnectAsync` (src/Antiphon.PtyHost.Client/PtyHostClient.cs:61-90): null reply -> `HelloEofException` -> retried inside the one deadline. A close mid-frame is `EndOfStreamException`, which is an IOException, so it is still retried. Connect IOException/TimeoutException and a non-caller OCE are still retried. Non-ack frame (`InvalidOperationException`), `InvalidDataException` and `JsonException` fail fast. `HelloEofException : InvalidOperationException` keeps the final TimeoutException's inner "EOF" message.
- Host hello handler (`src/Antiphon.PtyHost/PtyHostServer.cs:106-117`): it answers only `helloAck`, or `ErrorMessage("protocol")` when the client's first frame is not hello, and this client always sends hello first. There is no busy or wrong-generation reply at hello. The pipe is single-instance (`maxNumberOfServerInstances: 1`), so a busy host shows up as a connect wait or refusal, which is still retried.
- Callers: launch (`SessionRunnerRuntime.cs:3383`) and adoption (`:3501`), custody and non-custody. They judge identity after connect, from `Hello` fields; none of them uses a hello error frame. So nothing relied on the old retry of non-ack replies. Fail-fast is correct in every adoption case.

## (b) Mutants (client-dll swap into a copied test output, one lease per build and per run)
| Mutant | Red test (TRX) |
|---|---|
| M2 deadline ignored on EOF | Eof_every_hello_times_out_inside_the_connect_budget Failed 8.10 s |
| M5 deadline reset on each EOF | Eof_every_hello... Failed 8.03 s |
| M4 retry every non-caller error | Corrupt_hello_frame_fails_fast Failed 4.09 s; Non_ack_hello_reply_fails_without_retry Failed 4.00 s |
The source was restored with `git checkout` after each mutant build. Tree clean at df92aa7b7.
Exit_while_detached_survives_adoption: 20/20 passed from one build under 16 busy loops; the loops were killed afterwards.

## (c) Custody clock
- Card1619LinuxStopIntentTests still asserts the same things. BeforeKill requires `Requested` before the helper runs; `Issued` and custody container come after; `LastRecord == true`. The blocked-writer test requires the signal to return while the writer is still pending, and `LastRecord == false` after +100 ms on the store clock.
- Production wiring: `HostSession.cs:131` constructs the store with no clock, so it uses `TimeProvider.System`. The store's 100 ms `WriteOffer` budget is unchanged (`ProcessStopIntentStore.cs:254-270`; this diff does not touch it). Real-clock witness: PtyHost `Card1619StopIntentTests.Blocked_record_write_still_signals_within_bound` uses the system clock with a writer that never finishes. `LastRecord == false` within 1 s and signal latency is under 250 ms. Passed.

## (d) Runs (UseAppHost=false, TEMP=/tmp, build-slot)
| Selection | Result |
|---|---|
| SessionRunner: Card1619RunnerExitTests, Card1619LinuxStopIntentTests, NativeLinuxPolicyWitnessTests | 24/24 passed |
| PtyHost: HostCustodyTests, Card1619LinuxHostExitTests, Card1619StopIntentTests, policy | 39 total, 24 passed, 15 skipped (Windows-only), 0 failed |
| Agents.Pty: policy witnesses | 3/3 passed |
| Antiphon.Tests (one build): RcRosterRepairTests (Mirror/Explicit/Costs), C1404, C1577, C1584, C1410, G2865, policy | 11/11 passed, 1 m 19 s |
| audit-land-verification-graph.ps1 -Check | exit 0, digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69 phase=pass2 approved (no -Update) |
| check-evidence-diff d7fdee50b..df92aa7b7 | commits=4 entries=1 violations=0, exit 0 |
| merge-tree origin/master (d7fdee50b, an ancestor) | exit 0, tree 1890755deb164cef534e07afa5c898b9bbc68fc9 |
C502_V25/V26/V27 were not run (known UseAppHost=false reds).
All bin-r63fb* and bin-land-audit directories were removed by literal path.

## Advisories (card candidates, not blockers)
1. The `clock.GetElapsedTime(started) < 100 ms` checks in Card1619LinuxStopIntentTests (lines 175 and 247) read a frozen FakeTimeProvider before any Advance, so each is a constant 0 and cannot go red. The real ordering assertions still carry the intent. Drop these checks or rename them as decoration.
2. Carried from the Code report: non-custody adoption does not compare Hello.SessionId with the manifest session. This is not new in this diff.
