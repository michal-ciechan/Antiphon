# Why missed: CARD-1885 deadline and warm sample counting

## Introducing commit/card

CARD-1885 plan `ffd194909d6f9252dd37866487f80ab88e1c4525` used provisional
CP-1/CP-2 estimates of five/four minutes. The Code request at
`b0144514db48ea5bd56035574c1fef3b8dea5c49` independently selected
`rowTimeoutMinutes: 6`, replacing the imported 18-minute shared-build deadline.
That invocation is the proximate cause, not an established launcher slowdown or
product deadline regression. Its rationale was not retained.

Measurement script introduction `36ad63cda7912e031ab24c38a0ad326460add6bb`
assigned an array through an if-expression pipeline. PowerShell unwrapped a
singleton compiler hashtable; `.Count` counted seven fields instead of one
process. Debug fixed the collection shape, preserving the exactly-one assertion.

## Approving reviews

No approving ordinary Review exists. Full/Interim: neither. Code explicitly
retained incomplete Final verification. No Review or land is retroactively
credited; no released rc regression is claimed.

## Outside the selection

CP-1..4 were selected but never reached TUnit because their shared build timed
out: zero tests/no fresh TRX, zero queue wait, 458.070 s execution, six-minute
driver bound. Confinement/graph/source work shares that bound. On Debug's fresh
run MSBuild took 303.530 s, but checkpoint execution took 393.929 s plus 60 s
admission. CP-2's serial harness took 12m46s instead of the estimated four minutes.
The plan's samples were not checkpoint p95s for this loaded SDK/graph.

Original resource qualification ran restore and one stock solution prime, then
stopped at the 30-minute expansion gate. The warm measured path never executed,
so the singleton Count defect had no opportunity to fail. Tiny adapter witnesses
prove launch/affinity/sharing/idle behavior, not this measurement path. Separate
command lifetimes also lost private warm processes; the foreground controller
now owns lifetime while every build still takes its own lease.

## Cheapest pre-land check

Existing contract mapping for CARD-1782:

`tools/Antiphon.Checkpoints/**` ->
`/*/*/RunSchedulerTests/build_deadline_is_the_largest_dependent_row_timeout_and_the_row_override_replaces_it`

That test verifies override semantics, not whether a chosen budget is adequate.
Inspect the resolved request's `rowTimeoutMinutes` and report's `deadlineMinutes`
against measured execution, with admission separate.

Measurement coverage follow-up:

`scripts/measure-linux-shared-compiler.ps1` -> proposed
`/*/*/LinuxSharedCompilerMeasurementTests/*`.

This class does not exist and is not credited as coverage. Its required behavior
is one small native prime-plus-warm call verifying the same compiler identity
across separately leased commands. The committed bounded controller supplies that
real witness now: six final measured calls pass the unchanged warm assertion.
A permanent TUnit binding should execute the behavior, not inspect script text or
duplicate the array expression. No stub or fictitious roster was added.

## Exemption

None; first note for these causes. Numeric gates, limitations, raw evidence roots,
retained costs and cleanup are in [the Debug investigation](2026-11-13-card-1885-debug.md).
