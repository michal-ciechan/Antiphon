# Windows Full group 135 native ownership investigation

`C1039_DescendantExit` was the sole failure in `antiphon-all-group-135`
of Windows Full run `b8cbfb24`, pinned at
`2fd61b643ea4231fa732cd4969b5284d43b33f39` on `rc/2026-12-05`. The operator
supplied the failed TRX row from the desktop. After 7.039 seconds the
test reported that `File.ReadAllText(capture)` did not contain
`root pid=138656 exited=false active=1`. The stack entered the fixture's
failure wrapper at `NightlyNativeOwnershipTests.cs:402`. The capture
excerpt begins with `idle-hang-capture` and reports a quiet interval of
0.08 minutes and CPU use of 2.06 seconds per minute. The excerpt ends
before the root and descendant records.

The operator confirmed that the same case passed in group 135 of
`acca11ba` at `6ed4cb58cd75fe386e94965165d96752452431a8` and that
`seed-from-acca11ba.json` mentions `group-135`. The earlier pin had no
idle-capture subcase. [Prior run evidence](2026-11-24-group-136-native-evidence.md)

## Introducing commit/card

CARD-2051 introduced the failed assertion in the native idle-capture
witness at `0095d1fd01dcf3b4e5e7861223316a22424626ba`. Master commit
`6fb8fa343a27dbbb75ca736e85bc48544c1369ab` folded that witness into
`C1039_DescendantExit`. The failing candidate includes
that change as `59db3604598e9b8a3bbbd6dba9b0086a91bd8875`, following
`a3d4090eedf862a4e8ec5c22a6b7824cca5c17dd`, which added the native idle
cutoff to the owner. The native owner, test class and child fixture at the
failing pin were byte-identical to master base
`f98e61aca24b6f0e99baa4c604b1dfa9a39d0310`.
[CARD-2051 investigation](2026-11-25-why-missed-quiet-host-hang.md)

## Approving reviews

CARD-2051 Debug `a589c78d-bb9d-46da-a69a-2fc1e2822a07` reported a portable
red and green on
`/*/*/QualificationSupervisorTests/Quiet_test_host_reaches_its_idle_budget_while_the_owner_and_other_logs_are_busy`
on Linux. The Windows replay of `C1039_DescendantExit` was pending at
land. The review
`230d1f5d-22d1-46bb-b83c-fbdc59968364` cited there was a Full-scope round
3 of the earlier CARD-1667 supervisor. Its executed selections were the
qualification-supervisor and orchestration-watchers scripts and
`/*/*/(RcQualificationWatcherScriptTests*)/*` on Linux. It predates the
CARD-2051 witness. No approving CARD-2051 review receipt was available
for this investigation.

## Outside the selection

`tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs` is an
Integration class. Its native fixture cases use a Windows guard and require
the staged `OwnedChild.exe`. Those cases skip on Linux. The short Merge gate runs the
subjects' narrow witness rows. The documented portable CARD-2051 selection
exercises the idle decision and leaves the real Job Object termination and
handle capture to the Windows witness.

The class uses a unique fixture directory for each case. Its membership
assertions use the private Job Object and fixture handles. The idle probe
samples the owned root process. Hang capture reads a system-wide handle
snapshot, then filters emitted rows to the root and descendants recorded
from that job. The idle case checks the root PID, root exit state and active
count from that private job. A concurrent qualification process affects the
size of the system handle snapshot, while its PID does not enter the job
count through that snapshot.

The idle subcase intentionally holds the root and sets a one-second idle
budget. A five-second sampler tick can therefore trigger the cutoff. The
test had already observed an idle reason, successful termination and
cleanup, a nonzero exit, a signaled root handle and the expected capture
path before it reached the failed text assertion. The neighboring cases
exercise assignment, child exit, drain, or environment handling without
setting an idle monitor. Their live marker reads already use `LiveFixtureText`
from the earlier sharing repair.

The test keeps the idle cutoff and checks that the capture carries the idle
header, the owner's exact reason and its live root PID. The private job
must contain at least one active member at capture. Its exact membership
count is a diagnostic snapshot. The fixture still checks that termination
succeeds, cleanup completes, and the retained root handle signals. No
product change was needed for the idle cutoff to run on this fixture.

The portable witness uses a synthetic `active=2` row. It rejects an empty
job, a different root PID and a root already marked exited. The truncated
Windows excerpt does not establish the actual value of `active`, so the
portable witness establishes the assertion change and the native replay
remains pending.

## Cheapest pre-land check

`tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs`,
`scripts/lib/Antiphon.Nightly/NativeProcessOwner.cs` and
`scripts/fixtures/nightly/owned-child/**` each map to
`/*/*/NightlyNativeOwnershipTests/*` on native Windows. Use the one failing
method from the TRX for the incident replay. For the CARD-2051 addition,
that filter is `/*/*/NightlyNativeOwnershipTests/C1039_DescendantExit` with
the existing 120-second case budget and a build-slot-gated run on an
isolated Windows runner.

The portable repair checkpoint selects
`/*/*/NightlyNativeOwnershipTests/Idle_capture_identifies_the_live_root_of_a_populated_job`.

## Exemption

None. The file read succeeded and returned a capture. The previous
[marker-sharing failure](2026-11-22-why-missed-c1039-native-ownership-sharing.md)
occurred while reading child-published marker files.
