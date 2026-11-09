# Why missed: cancelled Codex turn hook trailed the one-frame poll

One new rc-chunk cause. Qualification run `dce78ee0`, rc/2026-11-14 =
`7bee43614495293afbae1b0f4add019d11dacb9c`, chunk
`antiphon-all-group-3.trx`, Windows. CARD-1781.
Debug task 1db3f61f. The Windows read-out is
`docs/investigations/2026-11-09-run-231f573c-runner-codex-adapter-turn-complete-windows-failure.md`
on `origin/feat/card-task-9034f0cf` (`133b362d34eb1fde1be84a75dc8eb0687e73b506`).
That filename says run 231f573c; the body is run dce78ee0.

Previous run 231f573c on rc/2026-11-13 =
`653f1145de5b72c2617a018cd606e0e8ba3cfe6e` recorded failures in groups
2, 4, 71, 96 and 117 only. Group 3 was not among them. Those five
passed natively on dce78ee0 and are not this cause.

## Failure

`RunnerCodexAdapterTurnCompleteTests.C1322_Cancelled_verdict_cannot_become_screen_success`
failed in 6s 923ms on DESKTOP-KTLKPIF (Windows 10.0.19045, .NET 9.0.22,
TUnit 1.44). The chunk executed 409, passed 408, failed 1, skipped 0.

```
ShouldAssertException: interrupted.TurnCompleted
    should be
False
    but was
True
Additional Info:
    an aborted turn must not become transcript or screen success
```

Stack: `tests/Antiphon.Tests/Agents/RunnerCodexAdapterTurnCompleteTests.cs:80`
on that rc source. The line above it, `cancellationObserved.ShouldBeTrue`,
had already passed, so the scripted hook did append the cancelled
`TurnEnd`. The verdict was already sealed as screen success.

## What the one failure is

`WaitForTurnCompleteAsync` trusts a transcript `TurnEnd` whose stop
reason is not `cancelled`. A screen fallback can still return
`TurnCompleted: true` after the Working indicator has been seen, has
gone, and has stayed quiet for `CodexDoneQuietPeriodMs` (300 in this
test). After screen-done, a 2s `TranscriptGraceAfterScreenDone` lets a
later transcript row win, but the wait takes no further snapshot during
that grace. The final screen return then calls `SnapshotTextAsync`,
which snapshots once more, after `TurnCompleted` is already true.

The scripted client fires `BeforeSnapshotAsync` at the start of
`GetSnapshotAsync`, then builds the frame `Observe` judges. CARD-1322
armed the cancelled row at `IndicatorScreenReads + 2`. That offset
matched the old tick, which took two client snapshots per poll: the
second read of the first quiet poll was the same call that `Observe`
judged. CARD-1761 changed the Codex tick to one `GetSnapshotAsync` per
poll. The first quiet frame is then `IndicatorScreenReads + 1`.

Poll interval is 250ms and the quiet period is 300ms, about a 50ms
margin. If that first quiet frame arrives before 300ms, screen-done
waits for a later frame, the `+ 2` hook still runs first, and the next
loop's transcript check wins. If scheduling stretches the gap to
300ms or more, the first quiet frame seals screen-done, the grace
elapses with no snapshot, and the `+ 2` hook runs only inside the
post-verdict `SnapshotTextAsync`. Duration 6.923s is that extra 2s
grace. Screen-fallback siblings that always burn one grace were
4.13–4.31s. 195 other TRX results overlapped the 6.923s window,
including pty-host tests.

The adapter still classifies `StopReasons.Cancelled` as not a success.
`RunnerCodexAdapter.cs`, this test, `ScriptedCodexRunnerClient`,
`CodexTranscriptNormalizer`, `CodexTranscriptTailer`, `CodexDetectors.cs`
and `TranscriptTurnBaseline.cs` are byte-identical from
`653f1145de5b72c2617a018cd606e0e8ba3cfe6e` through
`7bee43614495293afbae1b0f4add019d11dacb9c` through master
`e06bcb3a0c17f4452db8b31ba92a22694ad3d5ab`. The only test-policy edit
in that rc range is the CARD-1811 linux exclusion of
`CapabilityScriptTests.issue_does_not_print_the_token_and_dpapi_round_trips`.
This class is `[Category("Unit")]` and is not excluded.

## Linux proof

At unmodified master `e06bcb3a0c17f4452db8b31ba92a22694ad3d5ab`,
`/*/*/RunnerCodexAdapterTurnCompleteTests/*` passed 9/9 in 12s 672ms,
then 20/20 more idle runs (pass=20 fail=0). Host load average during
that repeat moved through 37.07, 41.01, 30.25 and later 22.52, 28.95,
29.10. External CPU load did not stretch the in-process gap.

The same method with only `doneQuietMs: 300` changed to `1`, hook still
`+ 2`, failed on Linux in 6s 535ms with the same assertion and the same
message. That is the red witness. It was not committed.

Moving the hook to `IndicatorScreenReads + 1` admits the cancelled row
on the first quiet frame, before `Observe` returns. With `doneQuietMs: 1`
that hook passed 1/1 in 15s 304ms. The committed test restores
`doneQuietMs: 300`. Assertions are unchanged. No timeout was widened
and no retry was added. The adapter is unchanged: CARD-1761 stopped
the extra snapshot on purpose.

## Introducing commit/card

introducing commit/card: `933b5c016046fbcccbad04ed54da6b8b793b5a14` (CARD-1761)

That commit is the master cherry-pick of
`825296b0ed6e275a52951128beab1dff7f953e78` (2026-11-07 16:42 UTC),
"poll sequence and one frame instead of the ANSI tail". The `+ 2` hook
arrived earlier and was not edited by that commit:
`fb20a92953b4b4b09392d6f3bf74bd4eb213eb85` (CARD-1322, 2026-10-20),
"witness cancelled Codex adapter verdict". It was the right offset for
the two-snapshot tick.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `08a29a5c`, scope Full (commissioned Final). Reviewed
  `825296b0ed6e275a52951128beab1dff7f953e78`. Report
  `626c67dd3d5455efc3b74a494c09b9f379ffcd18`. CP-1
  `/*/*/(Card1761RunnerReadPollTests*)|(RunnerCodexAdapterReadyTests*)|(RunnerStartupStaleObservationTests*)|(RunnerTerminalSessionGenerationTests*)|(RunnerTerminalSessionInputEncodingTests*)|(AgentSessionRuntimeBoundedReadTests*)/*`
  executed 65, passed 65, failed 0. CP-2 the watchdog and delivery
  classes, 191/191. CP-3 remote-control maintenance and pty
  integration, 40/40. CP-4 the guard batch, 75/75. The whole Unit
  lane was not run. `RunnerCodexAdapterReadyTests` was in CP-1.
  `RunnerCodexAdapterTurnCompleteTests` was not in CP-1, CP-2, CP-3
  or CP-4.

`9f6d9c6529eae471e3261e6c90a99dec40fd84fc` is a later Final review of
the CARD-1761 TimeProvider seam (task aa15dd73). That is the other
cause, written up in
`docs/investigations/2026-11-08-why-missed-card1761-runner-read-poll.md`.
It does not review this poll diff.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Unit lane, `[Category("Unit")]`, path `tests/Antiphon.Tests/Agents/RunnerCodexAdapterTurnCompleteTests.cs`.

The review named the Ready sibling and the new read-poll witnesses.
It did not name this class. A quiet Linux run of the class stays
inside the 50ms window, which is why a server2 Unit selection of the
same filter stays green. The Windows qualification is the lane that
stretches the first quiet gap to the 300ms period. The class is not
excluded by `tests/test-execution-policy.json`.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs` -> `/*/*/RunnerCodexAdapterTurnCompleteTests/*`

CARD-1801 reads that table. This note does not edit
`tests/impacted-classes.json`. The class is nine Unit tests and about
13 seconds once the assembly is built. A quiet Linux row of the old
hook stays green. The row that fails on Linux without a loaded Windows
host is the same method with `doneQuietMs: 1` and the hook still at
`+ 2` (6s 535ms, `TurnCompleted` true). After the hook moves to `+ 1`,
that 1ms quiet passes. The selector still belongs on the adapter
path: the review's ReadyTests filter does not arm this hook.

## Exemption

same cause as an already written note: none.

`docs/investigations/2026-11-08-why-missed-card1761-runner-read-poll.md`
is `Card1761RunnerReadPollTests` wall-clock starvation on run
4e0d1159 group 89. This failure is the cancelled-turn screen hook
against the one-frame Codex tick.
