# rc Linux /proc stat registration flake

Verdict: **flake**. A short-lived `git init` can be reaped after `Process.GetProcessById` succeeds and before `/proc/<pid>/stat` is read. The read throws `DirectoryNotFoundException`. Registration does not treat that as "process gone". The `42cbbd04` delta does not change this path. Ten isolated runs of the same method at that commit were 40/40 passed, including `outside` 10/10.

No product or test code was edited. This file is the proposal.

## The claim about the candidate delta

`1f72e20158b99434cb6398fb7efbbd133d1c5569` is an ancestor of `42cbbd0417a2611f3a690e6671b13c9ba51fbf34`. `git diff --stat` between them is three files, 23 insertions, 1 deletion:

| File | Change |
|---|---|
| `tests/test-execution-policy.json` | `e2e` watchdog `7200000`, policy hash updated |
| `tests/Antiphon.Tests/Scripts/ReleaseGatePolicyTests.cs` | `C599_E2EWatchdog` witness |
| `scripts/test-release-gate.ps1` | `Test-C599_E2EWatchdog`, the function that witness runs |

No file under `src/Antiphon.HostCleanup`, `tools/Antiphon.Checkpoints`, or `tests/Antiphon.Tests/Checkpoints` differs. These blobs are identical at `1f72e201`, `42cbbd04`, and this task's base `c8bc95276ad1dfa22fcfcd0d1a2b3b2c6a1d80b0`:

| Path | Blob |
|---|---|
| `src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs` | `0ed27e4571c20533273ae4f2009aa08a7f317eff` |
| `src/Antiphon.HostCleanup/Checkpoints/ProcessIdentity.cs` | `7ee5423d0f0a1e05a8b3dfa4e65849999df0ef1a` |
| `tests/Antiphon.Tests/Checkpoints/CheckpointTestScope.cs` | `4c9cc98475b3a6e0fce66ded1f55ff4e912eeb9a` |
| `tests/Antiphon.Tests/Checkpoints/CheckpointOwnedOutputTests.cs` | `44f37ca17094baf8759c72ae0aa8ad37964fd155` |

The previous Linux gate and this re-gate ran the same registration code. The policy-file-and-witness description of the delta holds, and the script the new witness calls changed with it.

`CheckpointOwnedOutputTests.cs` is also identical at `rc/2026-10-15` `61117dd1663d2f47ce4708c18b609b8936491986` (same blob `44f37ca1`). `CheckpointNativeIo.cs` and `CheckpointTestScope.cs` do differ that far back (`81c68d1b` and `a3005a8b`). That older difference is outside the re-gate delta. The throwing read and the `FileNotFoundException` handler were already in the passing `1f72e201` gate.

## Where `/proc/<pid>/stat` is read

Roster TRX `/tmp/rc-linux-541c1ad7/roster/run.trx` (still present). One failure: `disk_ledger_cannot_authorize_user_deletion(outside)`, duration `00:00:00.0098477`. Message: `DirectoryNotFoundException: Could not find a part of the path '/proc/19323/stat'.`

```
at System.IO.File.ReadAllText(String path, Encoding encoding)
at CheckpointNativeIo.StartToken(Int32 pid, Process process)  CheckpointNativeIo.cs:37
at CheckpointNativeIo.Process(Int32 pid)                     CheckpointNativeIo.cs:27
at ProcessIdentityProbe.Capture(Int32 pid)                   ProcessIdentity.cs:22
at CheckpointTestScope.Register(Process process)             CheckpointTestScope.cs:98
at CheckpointTestBase.RegisterCheckpointChild(Process child) CheckpointTestScope.cs:218
at CheckpointOwnedOutputTests.InitRepo()                     CheckpointOwnedOutputTests.cs:66
at CheckpointOwnedOutputTests.disk_ledger_cannot_authorize_user_deletion  line 23
```

`InitRepo` starts `git init -q` and registers that child before `WaitForExit`. Line 23 has not reached the ledger assertion. The `outside` argument is the argument that lost the race. The same method's `forged`, `truncated`, and `missing` arguments passed in that roster (about 121–145 ms, long enough to finish `git init` and the ledger checks). A 9.8 ms failure is the stat read throwing first.

`StartToken` (line 37) reads `/proc/<pid>/stat` for a kernel start-time token. `Process` (lines 24–28) calls `GetProcessById` and then `StartToken`. `Capture` (lines 20–25) does not catch. `Register` (lines 95–98) stores whatever `Capture` returns.

`Observe` already documents the race and catches the wrong exception (`ProcessIdentity.cs` lines 48–56, handler added in `b6c2ec3d7a9269f89aa5ff1443d0cc5a2a356a3c`):

```csharp
catch (FileNotFoundException)
{
    // The process may exit after GetProcessById and before /proc is read.
    return new(ProcessVerdict.Dead, "identity-dead");
}
catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException
                           or System.ComponentModel.Win32Exception)
{
    return new(ProcessVerdict.Unknown, "identity-unknown:" + ex.GetType().Name);
}
```

`DirectoryNotFoundException` extends `IOException`, so this handler classifies it as `identity-unknown:DirectoryNotFoundException`. That catch is not on the failing frame. The roster throw left `Capture` during `Register`, before any `Observe`.

On this host, `File.ReadAllText("/proc/99999999/stat")` throws `DirectoryNotFoundException` with the same message shape. `File.ReadAllText("/proc/1/does-not-exist")` throws `FileNotFoundException`. A reaped `/bin/true` pid makes `GetProcessById` throw `ArgumentException` ("Process with an Id of N is not running"), so a fully gone pid never reaches line 37. The roster stack reached line 37, which means `GetProcessById` still found the process and the directory vanished before the second read.

A separate 200-start probe of `/bin/true`, reading `/proc/<pid>/stat` immediately after `Process.Start` and without waiting, got 198 hits and 2 misses. Both misses were `DirectoryNotFoundException`. The window is real and narrow. The full roster widens it: this case shares `ParallelLimiter<ProcessSpawnLimit>` with the rest of the suite, and the stat read is several calls after `Process.Start`.

## Isolated rerun at 42cbbd04

Detached worktree `/tmp/rc-regate-541c1ad7` was already at `42cbbd0417a2611f3a690e6671b13c9ba51fbf34`. Nothing in it was edited. `UseAppHost=false`, `OutputPath=bin-procstat75ae/`, `MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`. Headed, distiller, broker, and `TUNIT_MAX_PARALLEL_TESTS` were unset. The test assembly's own guard points `SessionRunner__BaseUrl` at `http://127.0.0.1:1`.

| Piece | Slot | Wait | Hold | Result |
|---|---|---:|---:|---|
| `dotnet build tests/Antiphon.Tests` | `f55aa00a-42a9-4099-8b70-717a777f5462` | 0s | 184s | 1081 warnings, 0 errors. Elapsed 00:03:03 |
| Ten no-build runs of the method | `8bfe2ff3-1286-4662-a7d7-4ff71e7b1256` | 0s | 45s | 10 process exits 0 |

Filter `/*/*/CheckpointOwnedOutputTests/disk_ledger_cannot_authorize_user_deletion*`. Each run executed 4, passed 4, failed 0, skipped 0. Across the ten TRX files under `/tmp/rc-proc-stat-75ae5020/run-{1..10}/run.trx`, `disk_ledger_cannot_authorize_user_deletion(outside)` passed 10 times and failed 0. The method did not fail when run alone. The 28 `bin-procstat75ae` directories were removed afterward.

## Smallest fix

Two catch sites. Both are required for a stable green; a sentinel start token is not enough.

1. `CheckpointTestScope.Register` (`tests/Antiphon.Tests/Checkpoints/CheckpointTestScope.cs` line 98), the frame that threw. Catch `DirectoryNotFoundException`, `FileNotFoundException`, and `ArgumentException` from `Capture` and do not record the child. A reaped `git init` has no process left to kill. The test still holds the `Process` and calls `WaitForExit`. Do not store `StartUtcTicks <= 0`: `Observe` returns `identity-missing` for that, and dispose then fails the test as an unsafe child.

2. `ProcessIdentityProbe.Observe` (`src/Antiphon.HostCleanup/Checkpoints/ProcessIdentity.cs` line 48). Catch `DirectoryNotFoundException` in the same arm as `FileNotFoundException` and return `Dead` / `identity-dead`. That is the race the comment already describes. Today the exception falls through to the `IOException` arm and becomes `Unknown`, which dispose treats as unsafe.

Leave `StartToken`'s happy path on the kernel start-time field. `Process.StartTime` stays the non-Linux path only.

## Red-first witness

A live reaped pid does not reach line 37, and the 2/200 miss rate is too unstable to be the red test. Inject the exception the roster actually threw.

`CheckpointTestScope` already takes a `ProcessIdentityProbe`. `ProcessIdentityProbe` takes a `CheckpointCleanupContext` whose `ICheckpointCleanupIo.Process` can throw. `VirtualCheckpointIo.Process` currently throws `ArgumentException` for an absent pid, so use a small io that throws `new DirectoryNotFoundException("Could not find a part of the path '/proc/19323/stat'.")`.

- `Observe` on an identity with a positive start token, matching host, boot, and pid namespace: today `ProcessVerdict.Unknown` and reason `identity-unknown:DirectoryNotFoundException`. After the catch widening: `Dead` / `identity-dead`.
- `Register` on any `Process`, with a probe whose `Capture` throws that `DirectoryNotFoundException`: today the exception escapes (the roster failure). After `Register` ignores a reaped child: it does not throw, and dispose has no child to kill.

Those two assertions go red on `42cbbd04` before the catches change, and they do not depend on winning the race.
