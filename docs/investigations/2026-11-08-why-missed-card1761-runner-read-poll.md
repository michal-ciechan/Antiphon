# Why missed: CARD-1761 runner read poll

Debug task 521d50c9. Qualification run 4e0d1159, chunk
`antiphon-all-group-89.trx`, rc cut
`3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e` (equal to master at the cut).
The desktop paths
`C:\Antiphon\releases\candidates\rc-2026-11-11-3364197bf1cb\logs\2026-11-08-0936-4e0d1159\antiphon-all-group-89.trx`
and `antiphon-all-group-89-tests.log` are not mounted on this runner.
The chunk summary is 1 failed in `Card1761RunnerReadPollTests`. The
method name, message and stack for run 4e0d1159 were not readable here.

The class file and `RunnerTerminalSession` are unchanged from that cut
to master `f54741ef3bc08baa0493f9e0bebd2425acab6fb5`. A leased Linux run
of the class at that SHA passed: 19 executed, 19 passed, 0 failed,
0 skipped, 6s 281ms. The guard batch passed 69/69. No product or test
change. CARD-1781.

## What the one failure is

The class has 19 cases. Eighteen are exact route counts with no
wall-clock budget. Two are wall-clock bounded, and the CARD-1766
review already named both as load flakes (advisory A-5, not filed as
a card; a board search for that text returned no card):

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

The 250 ms witness is the one a qualification host hits first. The
quiet witness needs delays past about two thirds of a second. Both
passed on Linux here, and both passed twice in the CARD-1766 review
at load about 50. Run 4e0d1159 is the heavy native Windows pass.

The 250 ms witness existed on the previous rc
`74a8004f405bc54666fea33889839f0ff4d62ec7` and that class was not a
failure of run e737e9fe. The quiet witness did not exist on that rc.
This cut is its first native Windows execution. Without the TRX, the
failed method is one of those two, not a route-count witness.

## Classification

Windows-only timing fixture. Linux at the rc SHA and at master is
green, so this is not a product defect in the buffer-versus-session
route. The assertions were not widened and no retry was added.

## Introducing commit/card

introducing commit/card: `abad143eff62d2f61dc811441df586a1a66e71e5` (CARD-1766)

That commit is the master cherry-pick of
`91c703532826ca742ee9932ab1850a1839468137` (2026-11-08 06:31 UTC). It
keeps pty sequence polls on the session record and moves Herdr and an
omitted backend onto the buffer route, and it adds the 80 ms quiet
witness. The 250 ms pty witness arrived earlier, in
`3f833441a6c32bee66c9fd71d3b519b2b4404870` (CARD-1761), the cherry-pick
of `825296b0ed6e275a52951128beab1dff7f953e78`. Both commits are
ancestors of the rc cut and of master `f54741ef3bc08baa0493f9e0bebd2425acab6fb5`.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `ca2107c0`, scope Full (commissioned Final). Reviewed
  `91c703532826ca742ee9932ab1850a1839468137`. Report
  `66f04f3958905a2074d2dff36310583a5e8c3ed6`
  (`.antiphon/task-ca2107c0.md` on `feat/card-task-ca2107c0`).
  Platform Any, observed linux, runner server2. CP-1
  `/*/*/(Card1761RunnerReadPollTests*)/*` 19/19 twice. CP-2 guard
  batch `/*/*/(TestClassificationPolicyTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/*`
  69/69 twice. The report's A-5 says those two wall-clock witnesses
  can flake under heavy load and records that both passed at that load.
- `08a29a5c`, scope Full (commissioned Final). Reviewed
  `825296b0ed6e275a52951128beab1dff7f953e78`. The 13 `Card1761_*`
  witnesses, including the 250 ms pty wait, were inside CP-1 (6 unit
  classes, 65/65). Observed linux, runner server2. The quiet witness
  did not exist yet.

## Outside the selection

why the failing test was outside that selection (lane/category/path): native Windows qualification lane, `[Category("Unit")]`, path `tests/Antiphon.Tests/Application/Card1761RunnerReadPollTests.cs`.

The class was inside both Linux review selections and passed. It is
not excluded by `tests/test-execution-policy.json`. No Windows native
rerun was owed: both tasks are `requiredPlatform=Any` and ran on
server2. The previous Windows rc (e737e9fe, `rc/2026-11-10`) ran the
pre-CARD-1766 class and did not fail it. Nothing ran this class on
Windows between the CARD-1766 cherry-pick (06:31 UTC) and run
4e0d1159 (log folder `2026-11-08-0936`). That full antiphon-all
qualification is the lane that sees the load flake. A Linux rerun of
the same filter does not.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/Application/Card1761RunnerReadPollTests.cs` -> `/*/*/Card1761RunnerReadPollTests/(Card1766_Herdr_quiet_follows_the_buffer_sequence_not_a_flat_session_record*)|(Card1761_Pty_output_wait_snapshots_only_when_the_sequence_moves*)`

Those two methods are the wall-clock pair. The whole class is 19
cases and about 6 seconds once the assembly is built, and that is the
filter the review already ran on Linux. A Linux row would have stayed
green. The check that would have caught this chunk is that same class
filter on the native Windows qualification host. The CARD-1782
impacted-classes table is not on this master.

## Exemption

same cause as an already written note: none.
