# Why missed: CARD-1928 owned Herdr peer assembly loading

## Introducing commit/card

The incompatible host assumption was introduced by
`735cdd49cfe17273a03bff4d198da64d78572387` (CARD-0801):
`HerdrTestProcess.StartOwnedFixtureAsync` loaded the TUnit test assembly into bare
PowerShell with `Assembly.LoadFrom`. This did not give the child the application's
runtimeconfig/deps context. The later tracing change
`de27ce2fd8c66d260e83e1cb0f431aae54035d53` linked `TestTrace.cs` into this assembly
and installed its module initializer on 2026-11-03. This is a test-host defect;
production Herdr delivery is not reached.

On fetched current master `08cc3dcac230658c9bb7f580d32ad91906665fbd`, both
`C801_CrashedOwnerLeaseReclaimed` and
`C801_ChangedAndSymlinkSocketsAreNotReclaimed` fail before READY: 2 executed,
0 passed, 2 failed, 0 skipped. A separate bounded reflection probe captured
`MethodInvocationException` -> `TypeInitializationException (<Module>)` ->
`FileLoadException: System.Diagnostics.DiagnosticSource, Version=10.0.0.0`,
HRESULT `0x80131040`. The native test output contains DiagnosticSource 10.0.0.0;
the PowerShell .NET 9.0.10 host has already loaded DiagnosticSource 9.0.0.0.
The exact initiating initializer frame was not exposed by the wrapper, so the
November tracing commit is the identified load-time dependency change, not a
separately bisected first-red commit.

Evidence from this task is retained under
`.antiphon/card-1928/{baseline-build.log,baseline-red.log,baseline-red/baseline-red.trx,inner-exception.log}`.
The baseline build took 70.61 seconds; the two-test run took 2.823 seconds
(wrapper lease held 7 seconds). Initial `/proc/loadavg` was
`4.97 2.84 2.97`; during the build it reached `13.55 5.30 3.79`.

## Approving reviews

CARD-0801's board history records Final Review 5, task `708672f3`, Clean Full at
`9146736502cd349c871db03efaeb7d98c40f864c`, Linux CP-2 through CP-14, evidence
`3933ce2b-4eda-40d6-b391-1a354e4edf48`. Its plan's CP-2 includes
`/*/*/(FakeHerdrServerListenerTests*)|(HerdrTransportTests*)|(HerdrClientTests*)|(TestClassificationGuardTests*)/*`.
Windows W-1 task `e5c2d317` at `4e22b0cb` records 641 executed in CP-2 through
CP-13, zero skipped, with separately tracked inherited failures. These historical
receipts predate the November module initializer; they do not establish current
child-host compatibility. `TestTrace.cs` does not exist at that reviewed SHA.
No approving review/selection for the November tracing commit was identifiable
from its commit metadata; do not infer Full coverage or blame the earlier green.

## Outside the selection

The two failing methods are Integration cases in
`tests/Antiphon.SessionRunner.Tests/FakeHerdrServerListenerTests.cs` and invoke
`tests/Shared/HerdrTestProcess.cs`. The Linux-only replacement/symlink body is
not exercised on Windows; the crashed-owner body runs on both platforms.
The CARD-1928 triage and card history say the later CARD-1892 repair had a fixed
class list that omitted these helpers and found them only by extending its
IOException scan to deferred constructor failures. The CARD-1892 sweep's 230/232
runner row is historical evidence, separate from this task's fresh 0/2.
The original CARD-0801 selection included this class, so the miss is later
shared-assembly initialization drift plus insufficient child-host coverage,
not proof that the original review omitted the class.

## Cheapest pre-land check

Suggested path-glob -> class-filter entries for CARD-1782 (no selector policy is
changed by this repair):

- `tests/Shared/HerdrTestProcess.cs` -> `/*/*/FakeHerdrServerListenerTests*/*`
- `tests/Shared/TestTrace*.cs` -> `/*/*/FakeHerdrServerListenerTests*/*`
- `tests/Antiphon.SessionRunner.Tests/HerdrOwnedPeerWorker.cs` -> `/*/*/FakeHerdrServerListenerTests*/*`

A narrow first witness is the crashed-owner method, with the Linux symlink method
alongside it on Linux. Assembly/module initializer edits need a real child
startup, because in-process tests run in the correct dependency context already.

## Repair and verification

The repair uses the existing owned .NET worker pattern: launch the test executable
through `dotnet`, select one method to admit its assembly hook, and enter a
child-only serving/reclaiming mode before any test body. The child creates the
same `FakeHerdrEndpoint`/`FakeHerdrTransport`, reports READY only when its native
listener is ready, and serves the same pong. The parent retains the 15-second
startup refusal and kills/joins its own child on failed startup and disposal.
The other-process live-lease witness uses the same compatible host. All existing
assertions are retained, including its exit-code assertion.

Verification of the committed repair is pending at this note's initial commit.
Mutation checks are operator-waived; proof obligations remain pending. No native
Windows execution is claimed from this Linux task.

## Exemption

Same cause family as the CARD-0890 bare-PowerShell worker-host finding in
[the 2026-10-02 investigation](2026-10-02-flaky-test-root-causes.md), which captured
System.Text.Json 10 instead. This note records the distinct current Herdr
DiagnosticSource inner exception and exact witnesses.
