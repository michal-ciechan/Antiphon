# CARD-0443: worktree cleanup handle investigation

Investigation completed 2026-09-08. No historical blocking PID was proven. The
live census, a failed positive-control experiment, and the cleanup code identify
what the next implementation must capture. This report does not implement the
card's production acceptance criterion.

## Measured evidence

- Read CARD-0443 in full through `scripts/card.ps1 get CARD-0443`.
- Of the five named worktrees, `00afd0a8`, `31740fa8`, and `d4769008` still existed
  at inspection, with no children (including hidden children). `06a0799e` and
  `b984305b` were absent. No deletion or land retry was attempted.
- A `Win32_Process` census found no accessible executable path or command line
  referring to any of those five worktrees. This cannot rule out a process whose
  current directory is the worktree, nor inaccessible process metadata.
- The only executable paths found under `C:\Antiphon\worktrees` were two
  `fakeclaude.exe` instances from **another** worktree, `card-task-510b73f8`, under
  `tests\Antiphon.Tests\bin-card0208tests\fakeclaude`. PIDs 38020 and 51544,
  parent PIDs 17072 and 45232, creation times 2026-08-31 21:56 and 22:05 local.
  These are evidence of old test processes, not evidence that they blocked any
  of CARD-0443's five directories. Neither was killed.
- Seven visible `dotnet.exe` instances were MSBuild nodes created around 10:00
  on September 8; their command lines did not contain a worktree path. No
  `testhost.exe` or `Antiphon.Tests.exe` appeared in the census. The visible
  `Antiphon.Server.exe` PID 57160 ran from the canonical main checkout's
  `server\bin\Debug\net9.0`, not a test worktree. No causal claim follows from
  the mere presence of MSBuild nodes.

## Why the Handle result is not exculpatory

`handle.exe -nobanner card-task` and full-path searches returned
`No matching handles found.` However, Handle also missed a newly created temp
file held open by the probing PowerShell process with `FileShare.Read`.
The stream stayed open for the entire Handle invocation; it was disposed and
the temporary file removed afterwards. The direct packaged executable had the
same failure as the WindowsApps alias. A PID-only query returned a couple of
Section objects, not the held file.

The current Windows token is **not elevated**. Microsoft explicitly requires
administrator privilege for [Handle](https://learn.microsoft.com/en-us/sysinternals/downloads/handle).
Thus this tool's negative result is unusable as evidence of no file handles.
The exact internal reason for the silent partial enumeration was not established.
`openfiles /query /fo csv` additionally reported local object tracking disabled
and access denied for remote-share enumeration. No global tracking flag was changed.

Resolved installed executable:

```text
C:\Program Files\WindowsApps\Microsoft.SysinternalsSuite_2026.8.1.0_x64__8wekyb3d8bbwe\Tools\handle.exe
```

For a future failure, run the following in an already authorized elevated
diagnostic context while the lock is present (substitute the failing short ID):

```powershell
handle.exe -nobanner -v 'C:\Antiphon\worktrees\card-task-31740fa8'
```

Require a positive-control held-file check under that same identity before
trusting an empty result. Preserve PID, process name, handle path, capture time,
tool exit status and diagnostic availability. A matching open handle identifies
an owner; some owners share deletion, so it is not by itself proof that every
listed process prevents removal. Do not use Handle's close-handle option.

## Code findings and hypotheses

1. `server/Infrastructure/Git/WorktreeManager.cs:461` runs `git worktree remove
   --force`, then on ordinary failure immediately attempts recursive directory
   deletion and prune. The already-unregistered arm also attempts directory
   deletion. `TryDeleteDirectory` at line 1271 catches an exception and returns
   only its message. There is no handle-owner capture or delayed retry.
   `DirectoryResidueReason` and `ComposeResidue` at lines 1027 and 1038 carry
   that message through `WorktreeRemoval.Residue` to the land outcome. This is
   the narrow integration point for a diagnostic; no new card state is needed.
2. `AgentTaskReplyService.PersistDeliverThenReleaseAsync` publishes completion
   and delivers to the parent **before** `ReleaseDelegateAsync` stops a worktree
   delegate. `AgentTaskLandService.RunAsync` accepts Succeeded worktree tasks
   without a delegate-release barrier. An immediate land can therefore overlap
   shutdown. This is a possible transient directory-current-working-directory
   lock, not a demonstrated explanation of tonight's five cases. Preserve the
   documented persist/deliver-before-release ordering (CARD-0319).
3. `AgentTaskLandService.VerifyAsync` at line 485 starts `dotnet build` with
   `WorkingDirectory=worktree`, without explicit node-reuse suppression.
   `RunProcessAsync` at line 504 waits for the direct process only and has no
   explicit child ownership/teardown. A surviving build descendant holding a
   directory is another plausible source, including after the delegate itself
   is gone. Capture must distinguish landing verification descendants from
   delegate/test descendants. Base-unchanged lands skip verification, so this
   cannot explain such a case without another build source.
4. Empty residual roots make a root-directory handle a useful hypothesis; they
   are not proof of one. An image/DLL lock would usually leave files too.
5. The repository already has `PruneStaleAsync`, which makes residue eligible
   before the ordinary TTL, and settings default the janitor interval to 24h.
   This is separate from the weekly Windmill mitigation in the card. Effective
   production scheduling/settings were not verified here.

## Proposed implementation for the next stages

Start with automatic evidence plus bounded retry; defer process termination
until captures establish an owner and lifecycle defect.

- Add an injectable infrastructure diagnostic beside WorktreeManager. Trigger
  it on the **first** deletion failure, before retries erase evidence. Search
  the exact worktree root and descendants, including root-directory handles.
  Invoke a configured absolute Handle executable using structured arguments,
  from the main checkout, never from the directory being removed. Validate
  executable availability, supported identity/privileges, and the held-file
  positive control. Do not elevate the application server merely for diagnosis;
  an elevated capture must use an explicitly configured operator-approved lane.
  A normal server without that lane must report `diagnostic unavailable:
  insufficient privileges`, not `no blockers`. Planning must settle this
  deployment prerequisite before claiming automatic PID capture is delivered.
- Bound diagnostic execution (suggest 5 seconds), output size, and cancellation.
  Keep failure cleanup non-throwing if the tool is absent, times out, or loses
  access. Record a machine-readable status and sanitized evidence outside the
  worktree. Include process name/PID and matched relative path in Residue;
  include creation time and parent PID where accessible in structured logs.
  Do not log arbitrary command lines or environments; a PID/path match alone
  must never grant kill authorization.
- Add cancellation-aware delayed retries for OS sharing/lock failures, for
  example delays of 250ms, 1s, and 2s within one total cleanup allowance.
  Preserve Git's intentional `worktree lock` refusal; do not double-force it.
  Recheck directory existence and registration and retain existing branch and
  metadata safety. Do not multiply the existing 300s git-remove timeout by the
  retry count; retry the failed filesystem operation within a bounded budget.
- Preserve `LandedWithResidue` and the already-landed cleanup-only retry path.
  A useful outcome is `directory ... still exists (...); handle owners:
  dotnet.exe PID <n>, path .; branch deleted`. If capture fails, preserve the
  original deletion error alongside the explicit diagnostic status.
- If captured owners are retiring task sessions, coordinate/wait for owned
  release. If they are landing-created MSBuild nodes, reproduce in an isolated
  temp project and validate child-scoped node-reuse suppression before changing
  verification launch. Never use global `taskkill /IM dotnet.exe`, global build
  server shutdown, or termination based solely on an executable name.

## Required validation

Extend `tests/Antiphon.Tests/Infrastructure/WorktreeManagerTests.cs`, which
already has `WorktreeManager_try_remove_reports_directory_residue_when_a_file_is_held`:

- A separate owned child holds a file without delete sharing: identify its PID
  and name in residue when the configured diagnostic is available.
- A separate child holds the root as its current directory: identify that owner
  too (an empty-directory case, not only an inner file).
- Release a transient holder during the retry window: cleanup becomes clean.
- Persistent lock, unavailable/unelevated diagnostic, timeout, inaccessible
  process and cancellation retain truthful errors and bounded behavior.
- Intentional Git lock, cleanup-only repeat, and branch-retention cases continue
  to obey their existing safety rules. No unrelated process is stopped.

Any new process-spawning test takes the assembly-local ProcessSpawnLimit.
Run the narrow class through `dotnet run --project tests/Antiphon.Tests`, following
`docs/testing-and-build.md`; do not run the full assembly for this change.

No application tests or builds were run during this investigation. Two
held-file diagnostic-control attempts (alias and direct binary) both failed to
identify the owner; these are tooling limitations, not passing lock tests.
Only this report was added. No shared service, process, worktree or card was
modified.
