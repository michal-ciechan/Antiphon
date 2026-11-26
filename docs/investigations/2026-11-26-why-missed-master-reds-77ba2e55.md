# Linux master failures at 77ba2e55

Task `b8aaa188` recorded 11 passes and 5 failures in CP-2080-Handshake, 81 passes and 2 failures in CP-2069-Compat, and 153 passes and 4 failures in CP-merge-subset-tests. The master source was `77ba2e55239dcbbf9e14e9b25e8d1b754e2ee994`. The comparison used the pinned assembly hashes, original runner image and exact failing-method selections. The earlier full rows and the replays retain their own filters and results. [Original evidence](/work/merge-evidence/task-b8aaa188/master/rows/) · [Debug evidence](/work/evidence/task-39c60e5b/)

## Placement handshake

The batch harness used `docker run --entrypoint /bin/bash` and ended the script with `exec setpriv … dotnet exec`. The managed test host therefore became PID 1. Four stage tests launched the actual native test fixture from the image. The stage returned its normal refusal before connecting because `getppid() <= 1`. The managed caller kept waiting in `Socket.AcceptAsync` until its 20-second bound. The fifth test set the attested parent to literal `1` in a negative control when the real fixture parent already had that value. The test sent an unchanged frame and received GO.

A method-filtered replay produced five failures with the original command. The same source, image, managed assembly, account and filter passed all five with Docker's `--init`, in 0.058–0.209 seconds per test. The runner load at invocation was 40.36. The PID requirement was already present in the CARD-2060 stage. CARD-2075 retained the test's literal parent `1`. The CARD-2080 batch change raised the product cutoff from 5 to 16 seconds and kept the stage fixture's 15-second absolute deadline and 20-second test cancellation budgets. Adding `--init` to the native row command repairs the harness with the existing budgets. [Before](/work/evidence/task-39c60e5b/samples/handshake-2/) · [After](/work/evidence/task-39c60e5b/with-init/handshake-1/)

## Compatibility and merge-subset

The three live-lane failures came from the offline PowerShell fixture's 30-second execution budget. `WrapperOnlyPaidManifest` timed out after 20 passing assertions in the original master compatibility row and before any assertion output on the batch candidate. `DeadlineJoinsOwnedChildren` timed out before assertion output and passed the same day on the same master assembly in the merge subset. `LockBeforeSlot` passed compatibility in 7.994 seconds and timed out in the merge subset after `ordered` and `busy`, with the root exited and both output drains incomplete. The original transport recorded no phase spans for these tests. The varying boundaries support a timing diagnosis and do not localize the wait inside the child. Unit 56, already published to master, replaced these PowerShell drivers with in-process fixtures and a virtual clock at `47f3b96b79dcc1e477e1fa31ed9ed3ab0eb494c2`. The input-owner correction is `d6e6565b2f142164873739a3c3b08bb06c725563`. The old batch candidate still carries the PowerShell fixture.

Three receipt identity parameters reported different children exceeding a four-second exit deadline: null lost `status` and `stop`, the healthy `CP-fixture` lost `report-merge-json` and `status`, and the empty string lost `clean`. Some children emitted the intended refusal before they were stopped. These failures sampled 34 CLI processes per parameter across two receipts, two collections and nine verbs. The batch candidate passed all four parameters with identical test source. CARD-2052 Review had already reported a whitespace-parameter child emitting the intended refusal at a four-second timeout on 2026-11-23. The previous receipt-fixture speedup reduced disk setup and left all 136 process invocations in place. [Historical timing and run identities](2026-11-10-slow-chunk-trace-and-in-process-plan.md) · [CARD-2062 prior observation](../superpowers/plans/2026-11-24-card-2062-checkpoint-output-cleanup-plan.md#timing-evidence)

The fixture change retains native `state.json/rows/status` and `report.json/rows/validate` controls for every identity using the same four-second hard process bound. The other combinations use the actual checkpoint command dispatcher with isolated cleanup contexts and four-second cancellation. The full combination table, exit and refusal checks, file inventory and byte assertions still run. This reduces the method from 136 CLI startups to eight.

## Gate coverage

The latest retained Linux cut verification, run `20261126-074134-4c4f` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267`, recorded CP-7 at 16 passed, 0 failed, 0 skipped. It used a dedicated source-built placement fixture and a host-launched test executable. The earlier cut run `20261126-063330-717f` also recorded 16 passing CP-7 cases. This is why the Docker PID assumption escaped the cut. Neither Linux cut selected `LiveLaneRunnerTests` or `CheckpointReceiptSafetyTests`. The later CARD-2082 targeted Linux runs selected other methods. The Windows runs `b8cbfb24` and `1fe763a9` supply no Linux execution for these cases. [Latest cut report](/work/evidence/rc-cut-pass2-5b7e6588/20261126-074134-4c4f/report.md) · [Earlier cut report](2026-11-26-rc-cut-gates-at-0b07453e4.md)

## Five-observation comparison

Each count combines the original incident-bearing result and four completed exact-method replays of the same assembly. The original rows ran wider selections. Queue time and test durations are recorded separately. A selector attempt executed zero tests, and three watchdog-aborted attempts produced no completed TRX. Those attempts are excluded. The narrow successful samples give observed ranges rather than a qualified p95.

| Case | Failed out of five | Attribution |
| --- | ---: | --- |
| `DropPreservesPayloadBoundary` | 5 | Stable when the Docker test host is PID 1, fixture refuses before connect |
| `StageWaitsForGo` | 5 | Stable under PID 1, fixture refuses before connect |
| `MixedFreezerCannotAttest` | 5 | Stable under PID 1, fixture refuses before connect |
| `FailedReleaseSendSuppressesExec` | 5 | Stable under PID 1, fixture refuses before connect |
| `MismatchedPlacedWithholdsGo` | 5 | Stable under PID 1, parent mutation is a no-op |
| `WrapperOnlyPaidManifest` | 2 | Intermittent 30-second PowerShell harness timeout. Successful narrow cases 19.332–22.083 s |
| `DeadlineJoinsOwnedChildren` | 2 | Intermittent 30-second PowerShell harness timeout. Successful narrow cases 11.735–13.213 s |
| `LockBeforeSlot` | 1 | Intermittent PowerShell harness timeout with incomplete stdout/stderr drains. Successful narrow cases 5.157–26.712 s |
| Receipt identity, empty | 1 | Intermittent four-second child exit timeout. Successful narrow cases 17.370–32.650 s for the entire parameter |
| Receipt identity, null | 1 | Intermittent four-second child exit timeout. Successful narrow cases 23.040–34.855 s for the entire parameter |
| Receipt identity, `CP-fixture` | 1 | Intermittent four-second child exit timeout. Successful narrow cases 36.181–41.511 s for the entire parameter |
| Receipt identity, whitespace control | 0 | All five passed |

The operator's R5 budget derives from comparable per-operation observations. These mixed-selection results and censored child timings support leaving the budgets in place and reducing fixture cost. The original and replays supply evidence of timing failures at the committed master source, with no product regression established in the three requested rows. The batch candidate's distinct timing-label assertion failure has its own CARD-2069 attribution in task `b8aaa188`.

[Case outcomes](/work/evidence/task-39c60e5b/outcomes.tsv) · [Per-attempt method durations](/work/evidence/task-39c60e5b/results.json) · [R5 test performance standards](../test-performance-standards.md#rules)
