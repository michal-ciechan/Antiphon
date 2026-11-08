# Why missed: CARD-1761 runner read poll

Debug task 521d50c9 wrote the first version of this note. Qualification run
4e0d1159, chunk `antiphon-all-group-89.trx`, rc cut
`3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e` (equal to master at the cut).
The desktop paths
`C:\Antiphon\releases\candidates\rc-2026-11-11-3364197bf1cb\logs\2026-11-08-0936-4e0d1159\antiphon-all-group-89.trx`
and `antiphon-all-group-89-tests.log` were not mounted on that runner.
The chunk summary then was 1 failed in `Card1761RunnerReadPollTests`. The
method name, message and stack were not readable there. Debug task aa15dd73
extends this note from the Windows extract. CARD-1781.

## Windows facts (run 4e0d1159 group 89)

The failed method is `Card1761_Pty_output_wait_refreshes_the_frame_when_the_sequence_moves`
at `tests/Antiphon.Tests/Application/Card1761RunnerReadPollTests.cs:252`.
`matched` should be True but was False. The test duration was 1.44 s
(1.4357709 s, start 11:37:52.2326837, end 11:37:53.6684546 UTC). Its own
limit is `TimeSpan.FromMilliseconds(400)`.

`WaitForOutputAsync` set `deadline = DateTime.UtcNow + timeout` and called
`Task.Delay(50)` between polls. The fake runner moves the frame on the 3rd
`GetAsync`, which needs about 125 ms of the 400 ms. On the loaded Windows
host, 137 tests overlapped that 1.44 s window on 8 logical processors
(DESKTOP-KTLKPIF, ProcessorCount 8). Every timed wait in the class overran
its limit by about 1 s, so the 3rd poll never ran before the deadline.
The 250 ms pty witness took 1.358 s and the 80 ms Herdr quiet witness took
1.580 s; both still passed because they do not need a third poll inside
400 ms. This was thread-pool or CPU starvation, not a product regression
in the buffer-versus-session route.

The earlier guess that the failed method was the 250 ms witness or the
80 ms quiet witness stands only as what was knowable without the TRX.
The extract names the 400 ms refresh witness.

## What the one failure is

The class had 19 cases at the cut. Eighteen are exact route counts with no
wall-clock budget. Two were already named as load flakes by the CARD-1766
review (advisory A-5, not filed as a card):

- `Card1761_Pty_output_wait_snapshots_only_when_the_sequence_moves`
  waits 250 ms and requires a second session GET inside that window.
  The poll is `Task.Delay(50)` in `WaitForOutputAsync`. One stretched
  delay past the deadline leaves `SessionGets` at 1 and fails
  `ShouldBeGreaterThan(1)` while `SnapshotReads` stays 1.
- `Card1766_Herdr_quiet_follows_the_buffer_sequence_not_a_flat_session_record`
  needs the buffer sequence to stop moving and then stay quiet for
  80 ms, inside a 2 s cap. The fake advances the sequence on the first
  three buffer reads. Three stretched delays that blow the 2 s cap
  return false.

The 400 ms refresh witness is the same shape and the one the Windows host
hit: it needs the 3rd poll inside the wall-clock window. A Linux run of
the class at the rc SHA passed 19/19 in 6s 281ms, and the guard batch
passed 69/69. Linux and a narrow filter do not reproduce a 1 s pool stall.

## Classification

Windows-only timing fixture. The assertions were not widened and no retry
was added. The repair gives `RunnerTerminalSession` an optional
`TimeProvider`. `TimeProvider.System` still uses `DateTime.UtcNow` and
`Task.Delay(50)`. The 400 ms, 250 ms and 80 ms witnesses drive a clock
that advances one 50 ms step per poll, so a stall inside a read does not
consume the deadline. The 2 s Herdr and unknown-backend frame waits, and
the Codex 800 ms turn wait, stay on the wall clock: the frame waits need
one delay inside 2 s and finished in about 81 ms before this stall, and
the Codex assertion is the timeout outcome.

## Introducing commit/card

introducing commit/card: `abad143eff62d2f61dc811441df586a1a66e71e5` (CARD-1766)

That commit is the master cherry-pick of
`91c703532826ca742ee9932ab1850a1839468137` (2026-11-08 06:31 UTC). It
keeps pty sequence polls on the session record and moves Herdr and an
omitted backend onto the buffer route, and it adds the 80 ms quiet
witness. The 250 ms pty witness arrived earlier, in
`3f833441a6c32bee66c9fd71d3b519b2b4404870` (CARD-1761), the cherry-pick
of `825296b0ed6e275a52951128beab1dff7f953e78`. The 400 ms refresh witness
is in that same CARD-1761 commit. Both commits are ancestors of the rc
cut.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `ca2107c0`, scope Full (commissioned Final). Reviewed
  `91c703532826ca742ee9932ab1850a1839468137`. Report
  `66f04f3958905a2074d2dff36310583a5e8c3ed6`
  (`.antiphon/task-ca2107c0.md` on `feat/card-task-ca2107c0`).
  Platform Any, observed linux, runner server2. CP-1
  `/*/*/(Card1761RunnerReadPollTests*)/*` 19/19 twice. CP-2 guard
  batch `/*/*/(TestClassificationPolicyTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/*`
  69/69 twice. The report's A-5 says the 250 ms and 80 ms wall-clock
  witnesses can flake under heavy load and records that both passed at
  that load. The 400 ms refresh witness was inside that same 19/19.
- `08a29a5c`, scope Full (commissioned Final). Reviewed
  `825296b0ed6e275a52951128beab1dff7f953e78`. The 13 `Card1761_*`
  witnesses, including the 250 ms pty wait and the 400 ms refresh,
  were inside CP-1 (6 unit classes, 65/65). Observed linux, runner
  server2. The quiet witness did not exist yet.

## Outside the selection

why the failing test was outside that selection (lane/category/path): native Windows qualification lane, `[Category("Unit")]`, path `tests/Antiphon.Tests/Application/Card1761RunnerReadPollTests.cs`.

The class was inside both Linux review selections and passed. It is
not excluded by `tests/test-execution-policy.json`. No Windows native
rerun was owed: both tasks are `requiredPlatform=Any` and ran on
server2. The previous Windows rc (e737e9fe, `rc/2026-11-10`) ran the
pre-CARD-1766 class and did not fail it. Nothing ran this class on
Windows between the CARD-1766 cherry-pick (06:31 UTC) and run
4e0d1159 (log folder `2026-11-08-0936`). That full antiphon-all
qualification is the lane that sees the 1 s stall. A Linux rerun of
the same filter does not, unless the witness itself stalls a poll
past the old deadline.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/Application/Card1761RunnerReadPollTests.cs` -> `/*/*/Card1761RunnerReadPollTests/Card1761_Pty_output_wait_reaches_the_moved_frame_when_a_poll_stalls_past_the_deadline*`

That witness sleeps 500 ms on the first session read, past the 400 ms
deadline, and still requires the third poll to refresh the frame. It
fails while the deadline is `DateTime.UtcNow` and passes once the poll
clock advances per poll. It runs on Linux. The whole class is 20 cases
and about 11 seconds once the assembly is built. A Linux row of the
class without this stall stays green, which is why the reviews missed it.

## Exemption

same cause as an already written note: none.
