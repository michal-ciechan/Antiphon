# pwsh children abort with Stack overflow (exit 134) on server2

Investigate task 0850f5cc, 2026-11-08, server2 container `6f03ea7dd3f9`.
Source `f54741ef3bc08baa0493f9e0bebd2425acab6fb5` (branch `feat/card-task-0850f5cc`).
No product code was changed.

## Verdict

The abort is pwsh 7.5.4 dying at startup, not a recursive Antiphon script and not a
shrunk stack. Every non-interactive `pwsh` on this runner shares one JIT profile,
`/home/app/.cache/powershell/StartupProfileData-NonInteractive`. Concurrent starts
tear that file. The next `pwsh -NoProfile` then prints `Stack overflow.` and dies
with SIGABRT (exit 134) until a later process rewrites a readable profile. That is
PowerShell 7.5.4 / .NET `ProfileOptimization.StartProfile` (PowerShell/PowerShell#26528,
PowerShell/PowerShell#26431, dotnet/runtime#121977).

Antiphon's scripts are the victims. The test harness is what makes the race likely:
`ParallelLimiter<ProcessSpawnLimit>` is one lane inside one test process
(`tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs:15-17`), so other hosts on
this same container still start `pwsh` at the same time, all as user `app` with
`XDG_CACHE_HOME` unset. Nothing in the repo sets that variable.

This session did not fire the abort again. Three combined runs of
`TestClassificationPolicyTests` with `C0824WorkspaceSlotRoleGateTests` were 59/59,
and 400 mixed private-cache starts stayed exit 0. The 11:11 incident is reconstructed
from the checkpoint logs below. Remaining uncertainties are listed at the end.

## What overflowed

Not a thread stack, a cgroup, or a script call stack.

| Check | Result |
|---|---|
| `ulimit -s` / `/proc/self/limits` | 8192 KB soft stack (8388608 bytes). Hard stack unlimited. |
| `DOTNET_DefaultStackSize` / `COMPlus_*` | Unset. Only `DOTNET_VERSION=9.0.22` and `DOTNET_RUNNING_IN_CONTAINER=true`. |
| cgroup memory | `memory.limit_in_bytes=9223372036854771712` (no limit). |
| cgroup pids | `pids.max=max`. `pids.current` was 1297 at the start of this task. |
| dmesg 10:50-11:20 | veth attach/detach only. No OOM-killer line across the 11:11 window. |
| `pwsh` | `/usr/local/bin/pwsh` 7.5.4, `$PSHOME=/opt/powershell`, runtime .NET 9.0.10. No profile file under `/opt/powershell`, `/etc/powershell`, or `~/.config/powershell`. |
| docker inspect of this container | The nested daemon (`docker info`: server 27.5.1, 0 running) does not see container `6f03ea7dd3f9`. Limits above are from this container's cgroup. |

`scripts/test-duration-tripwire.ps1` does not recurse. It reads an allowlist, parses
one XML document, and walks the result nodes. `C487_G073`..`G076` start it with
`pwsh -NoProfile -NonInteractive -File` (`tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs:436-461`).
The live-lane and host-jq failures start a different script the same way
(`tests/Antiphon.ScriptHarnessHost/Program.cs:46-50`). `-NoProfile` rules out a
profile that imports itself. Sub-second failures (0.46s-1.21s) are startup, not
the script body.

## The 11:11 burst

Review `4c5fe834` (CARD-1777), report `.antiphon/task-4c5fe834.md` on
`origin/feat/card-task-4c5fe834` (`bed9b8f976`), lines 36-37 and 42. Checkpoint run
`20261108-105005-17a1` in `/work/worktrees/task-4c5fe834/.antiphon/checkpoints/`,
source `73ebc2c5d5`, same container hostname.

- R-1: 12 failed. 11 of them are `but was 134` with additional info `Stack overflow.`
  Host-jq cases and `LiveLaneRunnerTests` (`WrapperOnlyPaidManifest`,
  `ChildEnvironmentIsScoped`, `DeadlineJoinsOwnedChildren`, `LockBeforeSlot`).
  `LiveLaneRunnerTests` does not run `deploy-server2.ps1`.
- The twelfth R-1 failure is `C1058_Changed_leaf_cannot_emit_success_proof`: the
  marker file was absent (`but was False`), which is what a child that dies before
  the marker looks like.
- R-2: 1 failed. `C849_Seed_publishes_complete_payloads_before_its_marker` captured
  `retired-container=true exit=134` and
  `bash: line 12: 20872 Aborted (core dumped) pwsh -NoProfile -File "$repo/scripts/deploy-server2.ps1"`.
- G-1 of that same run has zero `Stack overflow` lines. Its result directory is
  stamped 11:14:10, after the review's 11:11:28-11:12:21 window. The same SHA was
  green on runs `20261108-095553-d366` and `20261108-111505-5da2`.

So one 53-second window, unrelated scripts, then a healthy rewrite. The shared
profile file's mtime moved with that story: 112032 bytes at 11:44 (after the
window), 84824 bytes at 11:57 while this task's tests were starting `pwsh`. The
bytes from 11:11 were not kept.

## Why it is a burst, and why C0824 is not the cause

`ProcessSpawnLimit.Limit` is 1 and the comment says the limiter is per process
(`ProcessSpawnLimit.cs:9-17`). `GitProcessSpawnLimit` is a second lane, width
`Clamp(ProcessorCount / 2, 2, 4)` (`GitProcessSpawnLimit.cs:12-28`). On this
24-core host that width is 4, and it runs beside the strict lane. Neither limiter
is visible to the next `dotnet run`. This container was already running other
checkpoint hosts while this task ran.

`C0824WorkspaceSlotRoleGateTests` is on the git lane (`C0824WorkspaceSlotRoleGateTests.cs:29-30`).
A search of `C0824*.cs` finds no `SetEnvironmentVariable`. The class starts git,
not pwsh. The debug note in the brief (task `51caad8a`: `C487_G073`..`G076` failed
in the same process as this class, each child printed `Stack overflow.`, and the
guard filter alone was 50/50) is not on the branch. `origin/feat/card-task-51caad8a`
tip `7d9f37e438` has no `.antiphon/task-51caad8a.md`.

Repro requested by the brief, one leased process, filter
`/*/*/(TestClassificationPolicyTests*)|(C0824WorkspaceSlotRoleGateTests*)/*`
(the whole policy class, because a method-segment OR cannot also name another
class). That selection includes `C487_G073(full)`, `C487_G073(simple)`, `C487_G074`,
`C487_G075`, and `C487_G076(empty|malformed|missing)`. Build was
`dotnet build tests/Antiphon.Tests --property:UseAppHost=false` through
`scripts/build-slot.ps1` (lease `a6cdac9a`, 0 errors). `TEMP=/tmp`.

| Try | Lease | Result | Duration |
|---|---|---|---|
| 1 | `b2d4df3b` waited 0s | 59 passed, 0 failed | 1m 55s |
| 2 | `d5e44186` waited 0s | 59 passed, 0 failed | 2m 47s |
| 3 | `e61fcabd` waited 75s | 59 passed, 0 failed | 2m 47s |

TRX under `/tmp/pwsh-repro-0850f5cc-try{1,2,3}/`. Co-scheduling C0824 does not
reproduce the abort. The debug failure fits the same host-wide window, not
pollution from that class.

A private `XDG_CACHE_HOME=/tmp/pwsh-cache-probe-0850f5cc` does redirect the
profile: `StartupProfileData-NonInteractive` there changed hash and size, and a
new `telemetry.uuid` appeared beside it. Against that private file:

- 48 identical `pwsh -NoProfile -NonInteractive -Command 'exit 0'`: 48 exit 0.
- 25 rounds of 8 tiny `-File` plus 8 fatter `-File`: 0 non-zero exits. A follow-up
  single start was exit 0.
- Replacing the file with the seven bytes `GARBAGE` did not abort. The next start
  exited 0 and rewrote the file. A torn MulticoreJIT profile is a different
  shape than short garbage. Upstream notes that identical writers often heal the
  file before the next start observes it, which matches a short burst rather than
  a permanent red.

## The merge timeouts are a different flake

`origin/feat/card-task-79675854` is at `f54741ef`, the same commit as this base.
`docs/investigations/2026-11-08-why-missed-land-diagnostic-watermark-held.md:41`
records `C498_FailureAfterPublicationRetainsPublication(landed)` as
`TimeoutException: terminal event` from the existing 30-second poll, and the
sibling cases as a 2-second sleep that loses the race on a loaded host. There is
no exit 134 in that note. Same load, different mechanism.

## Fix proposal (not implemented)

Give every test process its own profile root before any child `pwsh` starts.
Setting `XDG_CACHE_HOME` to a directory unique to that process is enough: children
inherit it, and in-process `ProcessSpawnLimit` already keeps those children
serial. The spawn that crashed in the checkpoint is
`tests/Antiphon.ScriptHarnessHost/Program.cs:46`. The tripwire spawn is
`TestClassificationPolicyTests.cs:439`. The same variable should be set once for
the test host, not copied onto every call site, and the runner entrypoint should
do the same for agent shells. They are the same user and the same file.

Do not `chmod -w` the shared file as the product fix. That freezes a torn profile
if the file is already bad, and it does not help a second user. Do not raise
`ulimit -s`, `ProcessSpawnLimit`, or any test timeout.

Not done, noted: per-process `XDG_CACHE_HOME` for the test host and the runner session.

## Remaining uncertainties

- No torn profile from 11:11 was saved, and this session did not produce exit 134
  (400 private starts, three 59/59 test runs). The rate upstream is low until the
  writers record different method sets.
- A second, rarer pwsh abort with the same text is not excluded. It would still
  have to be startup-wide, because the failing scripts do not share code.
- Nested `docker inspect` of this container was not available.
