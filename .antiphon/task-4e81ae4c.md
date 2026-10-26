# Debug outcome: antiphon-all-group-75

No Linux reproduction: ScriptHarnessWindowsOwnershipTests selected 10 tests,
executed 0, passed 0, failed 0 and skipped 10. Windows qualification remains
incomplete. No product or fixture fix is justified by the available evidence;
the only committed change is this report.

Source/build provenance: assigned branch `feat/card-task-4e81ae4c`, clean source
`bedea1c03cd788d83b2ff54c40bfb603927bd69c`, equal to fetched
`origin/rc/2026-10-26` when work began. No branch change, rebase or target merge.
Linux/server2, .NET SDK 10.0.403. The built test DLL's informational version is
`1.0.0+bedea1c03cd788d83b2ff54c40bfb603927bd69c` and its SHA256 was
`4279B629A55B98BF4F885356C91D640C2C46CF8D9171AE0CDDFFFBC4E2338F40`.
The relevant ownership tests, Windows fixture, Windows adapter and helper program
have no diff from failed candidate `105792f7d22f297746cdb338976f52e424b76fa7`.

## Classification

This is native Windows process/Job Object ownership qualification, rather than
file-owner SID or ACL qualification. Every test is guarded by
`RequireNativeWindows`; the adapter and independent fixture also reject Linux.
The assertions require Windows retained process handles, suspended creation,
private/nested Job Objects, inherited standard handles, native pipe EOF and job
accounting. Removing the guards or emulating the handles would not reproduce
their acceptance behavior. There is no Claude session/input-guard interaction.

The supplied failure identifies only the class and one failed case. The failing
method, assertion, stack and native timing are unavailable here. No evidence
supports attributing it to a specific handle race, path, load or leaked fixture
state. Existing W7 object-identity and W10 serialized-reap protections are already
present in both the candidate and assigned source.

## Verification

One isolated build, through the host gate:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-4e81ae4c-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-debug-4e81ae4c/ --nologo
```

Exit 0; 0 errors, 1196 warnings; MSBuild elapsed 4m09.68s. No warning cleanup was
attempted. Exact unedited gate lines:

```text
BUILD SLOT granted lease=75410452-d1e7-4570-a6b0-e74a60079a9b waited=0s maxcpucount=6
BUILD SLOT released lease=75410452-d1e7-4570-a6b0-e74a60079a9b held=250s
```

The brief expressly requires the compiled output with `--no-build`, avoiding the
checkpoint assembly reader's 64 MiB limit. No CHECKPOINT receipt is claimed.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-4e81ae4c-linux -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-debug-4e81ae4c/ -- --treenode-filter '/*/*/ScriptHarnessWindowsOwnershipTests/*' --report-trx --report-trx-filename windows-ownership-linux.trx --results-directory .antiphon/debug-4e81ae4c/linux
```

Exit 8, explicitly reported as zero tests ran; total 10, passed 0, failed 0,
skipped 10. Runtime 1.052s. All skips say
`Native Windows job ownership qualification requires Windows.` This is not green
qualification and does not resolve the native failure.

```text
BUILD SLOT granted lease=80d88fc3-5889-4466-9a0f-f0810c81a4c8 waited=0s maxcpucount=6
BUILD SLOT released lease=80d88fc3-5889-4466-9a0f-f0810c81a4c8 held=4s
```

Fresh TRX: `.antiphon/debug-4e81ae4c/linux/windows-ownership-linux.trx`, SHA256
`38FAB9580A6ACE9EA2260AE9919CE68BBDF9170BD494A24108083EC7D3CD81BD`.
Per-method skip durations are retained in it (0.130-17.690ms). Native setup/body/
teardown and external-call timing are unavailable because no native body ran;
unattributed runner time remains remainder, not zero.

An auxiliary pure-string Windows witness extracted the existing `QuoteArgument`
and shared `IsWindowsAppExecutionAlias` method bodies directly from source and
compiled them in an isolated wrapper, changing only method visibility. No owner,
Windows API, native qualification lock or native child was used. Forced Windows
inputs: ordinary absolute path, path with spaces, trailing separator, embedded
quote after separator, empty argument; alias paths with backslashes, forward
slashes and uppercase WindowsApps, plus installed PowerShell as a negative
classification control. All 9 assertions passed, exit 0. This diagnostic does
not qualify native launch, executable attributes or kernel ownership.

Its first attempt to invoke the full test assembly through reflection failed
before assertions with a TUnit module initializer error; this was a diagnostic
loader failure, not a red product witness. The failed log is retained separately.
The corrected witness ran under a fresh granted gate:

```text
BUILD SLOT granted lease=e442e932-8f51-47ec-8b33-3b2984f476a5 waited=0s maxcpucount=6
WINDOWS STRING WITNESS: 9 passed, 0 failed; no owner constructed or native API invoked.
BUILD SLOT released lease=e442e932-8f51-47ec-8b33-3b2984f476a5 held=2s
```

Ignored diagnostics are under `/work/worktrees/task-4e81ae4c/.antiphon/debug-4e81ae4c/`:
`build.log`, `linux.log`, the TRX/HTML, `windows-string-witness.ps1`, its passing
log and `windows-string-witness-loader-error.log`. Corrected witness script SHA256:
`5C001415D992019694814E3E9AFDE1C35C16432C2662C655D4407A280A751CAF`.
Generated evidence is not committed. Producer-owned alternate build directories
are removed before settlement.

## Native follow-up

Retrieve the failed method, error/stack and duration from the existing desktop
TRX under `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87`.
This delegate did not access the desktop, stop/restart the supervisor, take its
native lock, or run desktop tests.

Pending row NATIVE-1: on Windows, after the caller's qualification lane is free,
reuse the source-matched compiled qualification DLL and execute the exact filter
below through `scripts/build-slot.ps1`. Substitute its absolute DLL path for
`<qualified-Antiphon.Tests.dll>` and use a fresh results directory. Existing
fixture budgets remain 5s execution/2s cleanup, independent 30s watchdog, 5s
emergency cleanup; retain the existing plan's 10-minute row cap. Expected 10
passed, 0 failed, 0 skipped.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-4e81ae4c-native -- dotnet '<qualified-Antiphon.Tests.dll>' --treenode-filter '/*/*/ScriptHarnessWindowsOwnershipTests/*' --report-trx --report-trx-filename ownership-native.trx --results-directory '.antiphon/debug-4e81ae4c-native'
```

If it fails, commission the exact failing method with its native assertion
evidence. No source fix, fixture-only review exemption, release credit or native
green verdict is claimed here. The authorized investigation completed without an
approval pause; the native acceptance failure remains unresolved.
