# Windows Full group 135 native ownership investigation

The watcher reported one failure in `NightlyNativeOwnershipTests` in
`antiphon-all-group-135` of Windows Full run `b8cbfb24`, pinned at
`2fd61b643ea4231fa732cd4969b5284d43b33f39` on `rc/2026-12-05`. The failure
record and failed-first seed are on the desktop. The Linux worktree has no
copy of those files, so the failed method, assertion, stack and cause are
still open. The watcher reports counts by class.

The [prior run's native evidence](2026-11-24-group-136-native-evidence.md)
records that groups 133 through 135 passed in `acca11ba` at
`6ed4cb58cd75fe386e94965165d96752452431a8`. That observation establishes
the previous group verdict. Comparing the same test across runs requires
the per-method TRX row. Seed membership requires the actual seed entry and
the chunk identity mapping because group ordinals can change between runs.

## Introducing commit/card

The introducing commit for this failure is open. The native owner, test
class and child fixture at the failing pin are byte-identical to those at
master base `f98e61aca24b6f0e99baa4c604b1dfa9a39d0310`. Between `acca11ba`
and `b8cbfb24`, CARD-2051 changed the owner in
`a3d4090eedf862a4e8ec5c22a6b7824cca5c17dd`, and
`59db3604598e9b8a3bbbd6dba9b0086a91bd8875` added an idle-capture check to
`C1039_DescendantExit`. A prior pass of that method would cover its older
ownership assertions. [CARD-2051 investigation](2026-11-25-why-missed-quiet-host-hang.md)

## Approving reviews

The approving review for the group 135 failure cannot be identified until
the failed assertion identifies the introducing change. The CARD-2051 note
records a portable red and green on
`/*/*/QualificationSupervisorTests/Quiet_test_host_reaches_its_idle_budget_while_the_owner_and_other_logs_are_busy`
and an outstanding Windows replay of `C1039_DescendantExit`. The review
`230d1f5d-22d1-46bb-b83c-fbdc59968364` cited there was a Full-scope round
3 of the earlier CARD-1667 supervisor. Its executed selections were the
qualification-supervisor and orchestration-watchers scripts and
`/*/*/(RcQualificationWatcherScriptTests*)/*` on Linux.

## Outside the selection

`tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs` is an
Integration class. Every case enters a Windows guard and requires the staged
`OwnedChild.exe`. These cases skip on Linux. The short Merge gate runs the
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

The neighboring assignment and PowerShell cases also open the fixture PID
from `child.pid`. The retained-writer case reads `writer.handle`. Those
reads already use `LiveFixtureText` from the earlier marker-sharing repair.
The assignment case still reads `argv.json` directly, after waiting for the
descendant marker. The fixture writes and closes `argv.json` before it
starts that descendant. The drain and environment cases read outputs after
owner completion. The new idle check separately depends on system-clock
sampling and a 25-second completion wait around a held root. The Windows
failure record is needed to distinguish those paths.

## Cheapest pre-land check

`tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs`,
`scripts/lib/Antiphon.Nightly/NativeProcessOwner.cs` and
`scripts/fixtures/nightly/owned-child/**` each map to
`/*/*/NightlyNativeOwnershipTests/*` on native Windows. Use the one failing
method from the TRX for the incident replay. For the CARD-2051 addition,
that filter is `/*/*/NightlyNativeOwnershipTests/C1039_DescendantExit` with
the existing 120-second case budget and a build-slot-gated run on an
isolated Windows runner. No build or test was run for this investigation.

## Exemption

Open. The assertion and stack are needed to decide whether this is the
[previous marker-sharing failure](2026-11-22-why-missed-c1039-native-ownership-sharing.md)
or a new cause.
