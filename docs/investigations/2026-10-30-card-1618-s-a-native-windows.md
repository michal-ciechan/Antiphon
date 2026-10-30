# CARD-1618 S-A native Windows verification

Outcome: **qualification incomplete**. Bootstrap and import succeeded, but the
requested checkpoint command was refused before any test/build row started.
Both attempts returned exit 2, `refused: git-unavailable`. No tests passed,
failed or skipped; all 13 assigned rows are **NOT RUN**. This is not green.

## Source and host

- Assigned branch: `feat/card-task-d656c9d6`.
- Attempted source: `823c5526425d17efe0258e3e96afe40ed9c15e7d`.
- Linux evidence source: `1deb00ebd3bb34d3890d2e8b9080aab87c64aa1c`.
  `git diff --stat 1deb00ebd3bb34d3890d2e8b9080aab87c64aa1c HEAD`
  showed only `.antiphon/task-dd567e5f.md` (87 added report lines).
  No source change, branch switch, reset or source-SHA relabeling occurred.
- Native host: `DESKTOP-KTLKPIF`, Windows, 2026-10-30; SDK `10.0.300`,
  Git `2.50.1.windows.1`. Existing shared desktop stack was left alone.
- Tracked source was clean during both attempts. Only this report is delivered.

## Per-row results and budgets

No run ID, checkpoint receipt, row log or TRX was created. `n/a` is unknown/not
measured, not zero elapsed time. Counts below describe absence of execution,
not successful discovery. Execution budgets exclude the shared build; deadlines
are the plan's explicit overlay.

| Row | Result | Executed | Passed | Failed | Skipped | Row wall (s) | Execution budget (s) | Deadline (s) | Over budget |
|---|---|---:|---:|---:|---:|---|---:|---:|---|
| CP-1 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 60 | 120 | unknown |
| CP-2 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 120 | 240 | unknown |
| CP-3 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 60 | 120 | unknown |
| CP-4 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 60 | 120 | unknown |
| CP-5 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 60 | 120 | unknown |
| CP-6 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 60 | 120 | unknown |
| CP-7 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 60 | 120 | unknown |
| CP-8 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 180 | 360 | unknown |
| CP-9 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 120 | 240 | unknown |
| CP-10 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 60 | 120 | unknown |
| CP-11 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 60 | 120 | unknown |
| CP-12 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 180 | 360 | unknown |
| CP-36 | NOT RUN | 0 | 0 | 0 | 0 | n/a | 120 | 240 | unknown |

Expected counts remain 1 passed/no skips for each CP-1..12 and 3 passed/no
skips for CP-36. No Windows runtime gate lift has been qualified by this task.
The two rejected foreground invocations took approximately 1.92s and 2.22s
(command observer wall times), before row admission. There is no test execution,
shared-build, test slot-wait, phase distribution or p95 measurement to report.
AppHost, database, migrations and containers are not applicable to this selection.

## Common failure analysis

This is a native Windows checkpoint-harness command-size gap, not a missing Git
installation, Git-config leak, POSIX fixture assertion or timeout. At
`tools/Antiphon.Checkpoints/CheckpointApp.cs:625`, `StartAsync` iterates **all eight
manifest builds**, even though the command selected only the S-A rows. It appends
each project closure at line 634 and passes the combined roots to preflight at
line 637. `src/Antiphon.HostCleanup/Checkpoints/SafeDelete.cs:308` appends every
root to a single `git -C <owner> ls-files -z -- ...` invocation, launched at line
311. Reproducing that preflight read-only with the exact eight build IDs produced
561 roots and 566 arguments, totaling 44,331 characters before the runner's Git
hardening and Windows argument quoting. The existing runner throws
`Win32Exception (206)` at `scripts/lib/CheckpointProcessRunner.cs:133`:
**“The filename or extension is too long.”** `SafeDelete.cs:363` catches the
startup exception and returns `git-unavailable` at line 366. A small Git probe
through that same compiled runner succeeds and returns the assigned HEAD.
Thus the requested Windows invocation was real, but **none of the actual test
rows ran**. Attribution is inherited harness behavior at the assigned source;
no product or test source was changed. The manifest was not reduced to bypass
the refusal, and no implementation repair was attempted.

Assertion text: **none**, because no assertion executed. Last 40 lines of each
checkpoint invocation are the entire one-line log:

```text
refused: git-unavailable
```

Diagnostic log excerpt (full log contains fewer than 40 lines):

```text
PROBE writeRoots=561
PROBE cwd=C:\Antiphon\worktrees\card-task-d656c9d6 exe=git argc=566 argumentCharacters=44331
System.ComponentModel.Win32Exception (206): An error occurred trying to start process 'git' with working directory 'C:\Antiphon\worktrees\card-task-d656c9d6'. The filename or extension is too long.
at System.Diagnostics.Process.StartWithCreateProcess(ProcessStartInfo startInfo)
at Antiphon.Checkpoints.CheckpointProcessRunner.RunCoreAsync[T](...) in scripts/lib/CheckpointProcessRunner.cs:line 133
```

## Commands and retained evidence

Working directory for all commands:
`C:\Antiphon\worktrees\card-task-d656c9d6`.

The initial bootstrap specified `-nodeReuse:false`; shell argument splitting
turned it into `-nodeReuse: false`, so MSBuild rejected it with MSB1041 before
compilation. It held lease `837c74d7-d35c-4ea2-9653-7e9ce95a6316` for 2s, wait 0s.
The corrected invocation uses the existing `Directory.Build.rsp` setting:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1618-native-bootstrap -SlotWaitMinutes 10 -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1199-plan-tool/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1618-native-import -SlotWaitMinutes 10 -- dotnet tools/Antiphon.Checkpoints/bin-c1199-plan-tool/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-30-card-1199-windows-runnable-plan.md --out .antiphon/c1199-checkpoints.imported.yaml
python .antiphon/c1618-windows/overlay.py
dotnet tools/Antiphon.Checkpoints/bin-c1199-plan-tool/Antiphon.Checkpoints.dll run .antiphon/c1199-checkpoints.yaml --rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12,CP-36 --serial
pwsh -NoProfile -File .antiphon/c1618-windows/git-probe.ps1
dotnet tools/Antiphon.Checkpoints/bin-c1199-plan-tool/Antiphon.Checkpoints.dll run .antiphon/c1199-checkpoints.yaml --rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12,CP-36 --serial
pwsh -NoProfile -File .antiphon/c1618-windows/preflight-probe.ps1
```

Bootstrap succeeded: 5 warnings, 0 errors, MSBuild wall 20.84s; lease
`45711381-aff5-445c-be96-2f6abc061ec6`, wait 0s, held 23s, maxcpucount=4.
Import succeeded: 53 rows, eight builds; lease
`0a8f9f78-b3d9-4d48-8ec3-ea0003a68706`, wait 0s, held 3s. Its CP-41/42
default-deadline advisories were retained and addressed by the explicit overlay.

The ignored overlay script sets every CP-1..35 deadline to twice its per-row
execution minutes, CP-36..53 to the plan's explicit deadline table,
`timeouts.rowMinutes=20`, `build.slotWaitMinutes=10`, and phase metadata only.
It round-trips YAML and asserts full equality to the unedited import after
restoring only those allowed fields: 53 IDs and eight builds, with After,
filters, groups, counts, estimates and all other values unchanged. No blanket
row timeout was supplied. Metadata identifies this Windows attempt at its actual
source SHA, suite, per-row group and attempt 1.

Retained ignored files under this worktree:

- `.antiphon/c1199-checkpoints.imported.yaml` and `.antiphon/c1199-checkpoints.yaml`.
- `.antiphon/c1618-windows/overlay.py`, `overlay-validation.json`.
- `.antiphon/c1618-windows/bootstrap.log`, `bootstrap-retry.log`, `import.log`.
- `.antiphon/c1618-windows/run.log`, `run-retry.log` (complete rejection logs).
- `.antiphon/c1618-windows/git-probe.ps1`, `git-probe.log` (successful small probe).
- `.antiphon/c1618-windows/preflight-probe.ps1`, `preflight-probe-full.log`
  (full 561-root diagnostic, including exception and stack).

Diagnostics were read-only calls into the built helpers, not extra test rows.
The first diagnostic revision checked the one-root preflight successfully;
the final revision reproduces the eight-build closure. No generated evidence,
credential values or helper scripts are committed.

## Cleanup and handoff

These two task-created bootstrap output directories **remain in place**.
Both literal absolute paths were resolved and confirmed under this worktree,
with no link type. Automatic approval review rejected recursive cleanup with
`blocked by policy`, first as a combined verified cleanup and then as a single
literal-path removal. The following cleanup commands were not executed:

```powershell
Remove-Item -LiteralPath 'C:\Antiphon\worktrees\card-task-d656c9d6\src\Antiphon.HostCleanup\bin-c1199-plan-tool' -Recurse -Force
Remove-Item -LiteralPath 'C:\Antiphon\worktrees\card-task-d656c9d6\tools\Antiphon.Checkpoints\bin-c1199-plan-tool' -Recurse -Force
```

The caller needs to arrange permitted cleanup of these two owned directories;
no alternative deletion mechanism was used to bypass the rejection.
No S-A test output directory was created. No foreign process was killed, no
shared stack was touched, and no tags or target refs were published. The task
branch receives this report only. Existing authority covered the work, but did
not override the tool policy's cleanup refusal; no further approval was requested.

Required follow-up: a separate Code/Debug commission must address the native
Windows preflight argument-size failure without weakening source/output
preflight, then run the exact missing selection at a newly identified source.
All required rows remain open; Review cannot credit the Windows gate yet.

--- next stage ---
next: code
handoff: Fix the inherited Windows checkpoint preflight command-size refusal (561 roots, Win32 error 206), then execute CP-1,CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12,CP-36. Every row is NOT RUN; no Windows qualification credit.
artifact: docs/investigations/2026-10-30-card-1618-s-a-native-windows.md
