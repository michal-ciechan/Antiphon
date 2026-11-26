# Why missed — lost-host release left a live descendant

The native CARD-1614 fixture sent SIGKILL to PtyHost and immediately requested lost-host
release. If the pipe closed before the process liveness probe observed host exit, recovery
returned `Unknown` with `custody_host_still_present`. The runner correctly withheld the
cgroup kill. The fixture expected any `Unknown` response to mean recovery had already run.

## Introducing commit and card

CARD-1614's native witness at `0f1eb1a5c728faf63544ac9f13552922671f7153` called an
unjoined `Process.Kill` before checking release. CARD-2067's kernel-generation comparison
allowed the test to get past the prior UTC timestamp mismatch.

| Source | Executed | Failed | Failure |
| --- | ---: | ---: | --- |
| CARD-2067 Merge task f789ddb3, candidate `83ad8cc5112c15ab8e360d4b271cc07816e2e8b9` | 20 | 7 | The descendant was sleeping when the assertion expected it gone. |
| Same exact candidate, fresh run | 20 | 6 | All six descendants were alive two seconds after fixture disposal. No recovery record appeared in those trials. |
| Merge master base `0cfc4ac1ffb41f5af159a0c1db423fe119950267`, scratch comparison patch | 20 | 11 | The same assertion failed. All 11 descendants were alive two seconds after disposal, with no recovery record. |
| Exact candidate plus the assertion delta from `6933e72e5a81ac799d98bfbf9062ac792879ec80`, scratch | 20 | 5 | All five reported `custody_host_still_present` where the witness expected `released_unknown`. |

The master scratch patch skipped only UTC start-time equality for the Linux pair while
requiring the same host instance ID and PID. The exact candidate and the two scratch
diagnostics each used one leased build and 20 executions in one TUnit host. Their
TRXs independently account for all 20 repeat ordinals. The scratch changes were
restored after the runs.

The descendant already belonged to the recorded cgroup before release. Its parent
changed after PtyHost received SIGKILL, and the descendant kept running. The early
return in `RunnerCustodyLedger.ReleaseDeadHost` preceded the helper dispatch. This
accounts for the sustained survivor, even on the cgroup-v1 host where a successful
helper waits for its cgroup to drain. The fixture's old teardown could also remove
the release marker before the descendant read it. The observer restored the marker
in its private fixture directory after measuring the two-second interval and checked
that those descendants exited.

## Approving review

CARD-2067 Review `1e8ee5d5`, Full, exercised the native CP-2 filter
`/*/*/(LinuxRunnerCustodyLifecycleTests*)|(Card1619LinuxCustodyStopIntentTests*)/*`
on `46417306d6c85d6ce2a1e1bdad734fa68c1e6b0a`. The initial run and six further
CP-2 runs each passed eight cases. CP-3 also passed 59 guard cases.

## Selection and assertion gap

The witness was in the native Linux Integration/Slow selection at
`tests/Antiphon.SessionRunner.Tests/LinuxRunnerCustodyLifecycleTests.cs`.
Each selected case passed in Review under its observed scheduling. The assertion
accepted the same `Unknown` state for the live-host refusal and for drained dead-host
recovery, so only the immediate descendant check exposed the difference.

## Cheapest pre-land check

`{src/Antiphon.SessionRunner/RunnerCustodyLedger.cs,tests/Antiphon.SessionRunner.Tests/LinuxRunnerCustodyLifecycleTests.cs}`
→ `/*/*/LinuxRunnerCustodyLifecycleTests/C1614_Lost_host_explicit_release_kills_but_stays_unknown`.

The witness deliberately offers dead-host recovery while the native host is still
live, checks that the gate refuses and leaves the descendant in its recorded cgroup,
then uses the bounded host exit wait already added by
[CARD-2081](2026-11-26-card-2081.md). After the join, the explicit runtime request
must return `released_unknown`, record a successful kill for that same cgroup,
leave the cgroup empty, and leave the receipt absent. The debug branch carries the
reviewed CARD-2067 identity change over the master fixture that joins the owned host.

Use the SessionRunner.Tests checkpoint row on Linux with the installed custody
helpers and a private custody namespace. For the repeated proof use `-Repeat 50`
and `-MinExecuted 1` with the exact filter above. Repeat minimums apply per ordinal.

## Exemption

None.
