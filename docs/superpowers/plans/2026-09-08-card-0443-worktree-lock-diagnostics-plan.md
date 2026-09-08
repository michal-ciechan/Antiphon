# CARD-0443: worktree cleanup diagnostics and bounded retry

Date: 2026-09-08. Stage: Plan. Next: TestDesign (separate).

Design baseline: `e4f542f8`, including the full
[landed investigation](../../investigations/2026-09-08-card-0443-worktree-locks.md).
The planning worktree was fast-forwarded to that commit before writing this plan.
Read the current card with `scripts/card.ps1 get CARD-0443`; its original acceptance
requires either fixing the common cleanup failure or naming the blocking process.
The Plan brief expressly permits reporting diagnostic unavailability without elevation.

## Outcome and scope

Capture handle evidence at the first observable removal failure, retry transient
Windows sharing violations within a small allowance, and carry owner names/PIDs or
an explicit diagnostic limitation through the existing `LandedWithResidue` outcome.
Capture root-directory handles even when no child files remain. Preserve evidence
when a retry succeeds. Do not add process termination or change delegate release order.

The historical cause remains unproven. This plan enables diagnosis; it does not
retroactively identify a PID or promise that every open handle prevents deletion.
On an unelevated deployment the permitted fallback is useful but does **not** establish
the original card's owner-identification acceptance. That acceptance must be reported
separately from implementing the fallback and passing simulated tests.

## Ground truth

| Assumption or question | Observed code/evidence | Consequence |
|---|---|---|
| A known process held all five reported worktrees. | Investigation found three empty roots and two absent roots; no blocking PID was proved. Old `fakeclaude` processes belonged to another worktree. | Do not attribute these failures to those processes or to an executable name. |
| An empty Handle result rules out locks. | Unelevated Handle missed a deliberately held file in both investigation controls. This Plan session's Windows administrator-token check also returned false. | Require capability checks and positive controls; an unavailable scan is never a negative finding. |
| A file-only API is sufficient. | Residual roots were empty. Microsoft documents directory-resource failure in Restart Manager. | Root handles are a separate required acceptance case. |
| Succeeded means the delegate is released. | `AgentTaskReplyService.PersistDeliverThenReleaseAsync` saves, delivers and publishes before `ReleaseDelegateAsync`. Release calls the stopper from a fresh scope; errors can be caught. | An immediate land can overlap release; completion and a returned release method are not proof of process exit. Preserve CARD-0319 ordering. |
| Landing cannot create a lock holder. | `AgentTaskLandService.VerifyAsync` runs builds in the worktree; `RunProcessAsync` waits only for the direct child and does not explicitly suppress MSBuild node reuse. Verification is skipped if the base is unchanged. | Correlate the actual verification run with captured identities. A skipped run cannot explain a new landing-build descendant. |
| Cleanup already retries sharing errors. | `WorktreeManager.TryRemoveAsync` runs one `git worktree remove --force`; ordinary failure/timeout or already-unregistered results lead to immediate filesystem fallback. `TryDeleteDirectory` returns only an exception message. | Add an async cleanup coordinator retaining typed failure evidence, with one diagnostic attempt and bounded filesystem retries. |
| Every leftover root reaches fallback. | A successful git removal with a remaining directory currently skips fallback. | Cover this residual-root arm as well as failed/unregistered removal. |
| A cleanup failure means the land failed. | `WorktreeRemoval.Residue` already flows through `DelegationWorktreeService` into `AgentTaskLandService`'s `LandedWithResidue` event and Cleanup Failed stage row. An already-landed request retries cleanup. | Enrich existing detail; retain the pushed SHA and all existing state semantics. |
| Only weekly cleanup can recover residue. | `PruneStaleAsync` retries metadata carrying `ResidueSince` before ordinary TTL; configured default janitor interval is 24 hours. The card also names a weekly Windmill mitigation. | Keep both backstops. Their effective live schedule was not established by this work. |

Code anchors: `server/Infrastructure/Git/WorktreeManager.cs` (`TryRemoveAsync`,
`TryDeleteDirectory`, `DirectoryResidueReason`, `ComposeResidue`, `PruneStaleAsync`),
`server/Application/Services/AgentTaskReplyService.cs`,
`server/Application/Services/AgentTaskLandService.cs`, and
`server/Application/Services/DelegationWorktreeService.cs`.

## Decisions

### D-1: resolve the diagnostic privilege prerequisite explicitly

Use configured Sysinternals Handle only on Windows under an **already elevated,
previously authorized execution identity**. It must be a trusted absolute executable
path, installed and with its license/setup completed for that identity. Do not discover
an arbitrary executable on PATH or hard-code a versioned WindowsApps package path.
Configuration does not confer privileges.

No elevated diagnostic lane is established by the investigation or this session.
Repository searches found no existing Handle/Restart Manager diagnostic integration.
Do not infer the live server's token from this delegate's token: the implementation
checks its own identity at invocation time. This plan does not provision a broker,
Scheduled Task, service, UAC prompt, or elevation of AppHost/server. The configured
direct provider can work in an already authorized elevated host; otherwise return
`diagnostic unavailable: insufficient privileges` (and a missing-tool/configuration
reason where applicable). This is the brief's stated default, not an outstanding
permission request or a claim of automatic PID capture in today's deployment.

Microsoft requires administrator privileges for Handle and describes file **and
directory** inspection. Its name argument is a case-insensitive substring search,
so returned paths need a second containment check. [Handle documentation](https://learn.microsoft.com/en-us/sysinternals/downloads/handle).

Restart Manager is rejected as the sole non-elevated alternative: `RmGetList`
returns `ERROR_ACCESS_DENIED` for a registered directory. Enumerating child files
cannot cover an empty locked root. No supported, tested non-elevated replacement
covering this case has been established; do not claim Windows makes all such
alternatives impossible. A command-line/working-directory census is contextual
evidence, not handle ownership. [RmGetList documentation](https://learn.microsoft.com/en-us/windows/win32/api/restartmanager/nf-restartmanager-rmgetlist).

An elevated deployment or a new privileged collector would need its own explicit
operational authorization and validation. It is not a hidden prerequisite for
shipping the permitted unavailable outcome. Do not close the original acceptance
as owner-identification complete without the real controls below.

### D-2: one read-only capture at the first observable failure

Add `IWorktreeLockDiagnostics` as an external-I/O seam in Application, a typed
`WorktreeLockSnapshot` DTO, and a Windows Handle implementation in Infrastructure/Git.
Use an unsupported/unavailable result on other platforms. No new Domain dependency,
database column, task status, event type, or client UI is required.

In `TryRemoveAsync`, create one cleanup-attempt context after existing repository,
managed-branch and root-confinement checks. The context owns the first failure,
snapshot and retry allowance. Capture before the next destructive operation when:

- `git worktree remove` exits with an ordinary error or times out and the root remains;
- the first filesystem deletion fails in the already-unregistered arm;
- git reports success but its postcondition leaves a directory.

An intentional `git worktree lock` refusal is not a Windows sharing failure: keep
the refusal, skip diagnostic/retry/delete fallback, and never double-force it.
An already-unregistered response alone is not a deletion failure; try the directory
once, then capture if that attempt fails. If the root has already disappeared,
record `PathGone` rather than inventing an owner. Capture means immediately after
git returns; this change does not instrument errors inside the running git process.

Run the target query first using `ProcessStartInfo.ArgumentList` with fixed arguments
`-nobanner`, `-v`, and the absolute worktree root **without a trailing separator**.
Working directory is the verified repository outside the removal target; temporary
controls and logs are also outside it. Never use `-c`, `-y`, a shell command, process
name filtering, or arbitrary per-card tool arguments.

After preserving the target output, perform a single control query against a unique
temporary directory outside every worktree. Hold both a known file without delete
sharing and a known directory handle there, owned by the diagnostic identity, for
the full query. Require the expected PID and both paths. Dispose both handles and
remove only this owned control directory in `finally`. The target query precedes
controls so calibration cannot erase the transient evidence we wanted to collect.

Both queries, stream reads and metadata enrichment share one 5-second allowance.
Cap stdout/stderr at 256 KiB combined, parsed owners at 32, and final structured
evidence at 32 KiB. Drain safely without unbounded accumulation. A timeout or output
overflow stops/reaps only the diagnostic child this invocation started; it never
affects a discovered owner. Do not leave readers or a helper running after return.
Respect outer cancellation; preserve it as cancellation, not an ordinary tool failure.

Parse the installed tool's actual CSV schema with quoted-field handling and explicit
validation. Test pinned real fixtures; reject unknown/malformed schemas rather than
silently returning an empty list. Compare normalized Windows paths case-insensitively:
an exact root match is relative path `.`; descendants require the separator boundary.
Exclude sibling prefixes such as `card-task-12345678-extra`. Normalize supported
extended-length prefixes; unresolved device/alias paths make coverage partial, not
empty. Do not traverse reparse points to search beyond the authorized root.

Status and evidence are distinct: `OwnersObserved`, `NoOwnersObserved`, `Partial`,
`Unavailable`, `TimedOut`, `Failed`, `PathGone`, with stable reason codes such as
`InsufficientPrivileges`, `NotConfigured`, `ToolMissing`, `PositiveControlFailed`,
`OutputTruncated`, `UnsupportedPlatform`, and `MalformedOutput`. Preserve positively
observed owners even when coverage is partial. Only a completed, parseable query
with successful controls may say `NoOwnersObserved`; render that as
`no matching handles observed at <UTC>`, never `no blockers`.

### D-3: retain typed failures and retry only transient sharing violations

Extract a single-pass filesystem deletion result retaining exception type, HResult,
Win32 code when present, message, operation and timestamp. Avoid localized-message
matching. Keep clearing file attributes as the existing best effort does. Catch
operational filesystem errors without swallowing caller cancellation.

Use a new async coordinator for the `TryRemoveAsync` cleanup path. Keep create
rollback/recovery callers of `TryDeleteDirectory` on their existing behavior; do not
silently add delays, diagnostics or new Git force semantics to worktree creation.
An injectable filesystem I/O seam may expose the single pass for deterministic tests;
do not add a service interface solely to mirror a concrete pure coordinator.

For Windows error 32 (`ERROR_SHARING_VIOLATION`) or 33 (`ERROR_LOCK_VIOLATION`), allow
three additional filesystem passes after delays of 250 ms, 1 second and 2 seconds.
Maximum four filesystem deletion passes per cleanup invocation. Stop on success,
cancellation, a nonretryable failure or allowance exhaustion. Permission denied,
arbitrary IOExceptions and error 145 (directory not empty) do not imply a sharing
violation. Still attach available diagnostic evidence for those failures.
These numeric classifications follow the [Windows system error definitions](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-).

Start a monotonic 10-second additional-work allowance at the first observable
failure; capture and retry delays consume the same allowance. Do not start a delay
or another pass after expiry. Use the injected TimeProvider for deadlines/delays
and a timer-capable fake in tests. Never rerun the 300-second git remove as a retry.
Existing registration/prune/branch operations retain their separate budgets.

The bounds are on attempt count and scheduled extra work. Synchronous
`Directory.Delete` and individual Windows filesystem calls are not preemptible;
they can overrun the allowance. Do not promise a hard 10-second whole-land deadline
or abandon a `Task.Run` deletion that can continue mutating files after return.
Check cancellation between operations and after a synchronous call returns.

Recheck confinement, directory existence and Git registration before finalizing.
Use the existing prune eligibility and ancestor-aware branch deletion rules; retain
metadata/`ResidueSince` when anything remains, and delete metadata only when clean.
Recheck a registration's explicit locked state before a retry; never remove a
newly observed intentional Git lock. Unreadable/indeterminate registration state
must not be treated as affirmative permission for another destructive retry.
Any new registration query inside the retry coordinator uses the remaining
additional-work allowance, not a fresh 30-second budget per retry. Keep its
unknown/error result distinct from the existing `IsRegisteredAsync` helper's false
on git failure; do not use that false as the retry authorization.

### D-4: preserve the landing contract and put useful evidence first

Compose a bounded diagnostic summary into `DirectoryResidueReason`/`ComposeResidue`.
Keep the original deletion error and last error if different. The snapshot describes
owners **at first failure**, not proof that they still own a handle at settlement.
Deduplicate by process identity and relative path. `StageOutcome.DetailMaxLength`
is 1,000 characters at this baseline. Cap the complete composed residue at 950
characters, allocating up to 450 to diagnostics and reserving space for at least one
name/PID or the unavailable reason, capture time and capture ID. Clip display
errors/paths with an explicit marker; retain the full bounded originals in structured
evidence. Preserve branch/registration facts too. Indicate omitted owner count and
evidence truncation; do not rely on a later blind `Clip` to keep the useful fields.

Examples (names/PIDs are illustrative):

```text
landed ... pushed ..., cleanup incomplete: directory ... still exists
(sharing violation); handle owners observed at <UTC>: dotnet.exe PID 1234 path .
[capture=<id>]; branch deleted

landed ... pushed ..., cleanup incomplete: directory ... still exists
(sharing violation); diagnostic unavailable: insufficient privileges
[capture=<id>]; branch deleted
```

Retain a structured log event outside the worktree for every first failure, including
failures recovered by retry: capture ID, normalized target, first/last failure codes,
capture status/reason, UTC and duration, tool identity/version/exit code, control
verdicts, attempt count, final cleanup result, and sanitized owner name/PID/path.
Include process creation time, parent PID and parent creation time where accessible;
record inaccessible or exited metadata explicitly. Do not store raw stdout, arbitrary
command lines, environments, usernames or unrelated handles. Use existing rolling
server logging and its retention; no extra unbounded per-worktree log files.

Final clean cleanup remains `Landed` with Cleanup Clean even when diagnostics failed.
Remaining directory/registration/branch remains `LandedWithResidue` with Cleanup
Failed after a successful push. Preserve already-landed cleanup-only retries and
SHA/remote facts. Diagnostic failure must never become `LandRefused`, undo a push,
hide the original filesystem error, or prevent an otherwise successful delete.

### D-5: separate lifecycle evidence before changing lifecycle behavior

Add narrow structured observations, without new waits or kill calls:
Limit the added lifecycle observations to Worktree tasks.

| Observation | Correlation fields and meaning |
|---|---|
| Delegate completion and release | Task ID, session ID, worktree, completion/publish time, stopper request/return/failure time. Instrument the actual stopper call/catch, not just method return, which can hide a caught failure. A returned request is not an independently verified OS exit. |
| Landing verification | Task ID, land attempt, worktree, step (`build`/`tests`), direct child PID and OS creation time, start/exit time and outcome; emit explicit skipped verification. No command-line or environment dump. |
| Handle-owner snapshot | PID plus creation time and available ancestry, capture time and path. Evidence can be joined to either lifecycle, or remain unknown. |

Use existing runner session identity/host PID evidence for later correlation where
available; this card does not introduce a runner-wide process census or a new session
API. Do not equate `RunnerSessionDto.StartedAt` with an OS process creation time.
Emit lifecycle observations through the existing log/correlation infrastructure;
diagnostic/enrichment failures cannot change delivery or release behavior.

A handle owner matching a task's owned session (or a verified descendant) during
stop/release supports a delegate-release race. A verified descendant of a recorded
landing verification process supports a landing-build source. A later snapshot's
parent PID alone is insufficient if that parent exited or its PID was reused; unknown
ancestry stays unknown. A shared/reused build server is not exclusively landing-owned
merely because it performed work for that build. Both causes can occur in one capture.

No automatic process classification is required to manufacture missing evidence.
There is **no termination slice**. If future captures establish a release race, plan
coordination with the existing owned-session release while preserving persist/deliver
ordering. If captures establish landing-created build descendants, reproduce with an
isolated project and test child-scoped MSBuild node-reuse suppression before changing
launch behavior. Global build-server shutdown, process-name killing, and Handle's
close-handle option are rejected. These speculative root-cause fixes are not included
just because their process names look plausible.

## Implementation slices

Implement in order after TestDesign lands. New names below are proposed paths.

| Slice | Files and change | Tests to add/extend |
|---|---|---|
| S1: diagnostic seam/provider | Add `server/Application/Interfaces/IWorktreeLockDiagnostics.cs`, `server/Application/Dtos/WorktreeLockSnapshot.cs`, `server/Infrastructure/Git/WindowsWorktreeLockDiagnostics.cs`; put absolute tool-path settings in existing `Application/Settings/GitSettings.cs`; wire singleton-safe dependencies in `server/Program.cs`. Keep unavailable results cheap. | New `Infrastructure/WorktreeLockDiagnosticsTests.cs` for status/parser/path/budget behavior and `Infrastructure/WorktreeLockDiagnosticsWindowsTests.cs` for owned child controls. |
| S2: capture and retry | `server/Infrastructure/Git/WorktreeManager.cs`; optionally a concrete `WorktreeDirectoryCleanup.cs` plus filesystem I/O seam. Cover all three cleanup arms, typed sharing failures, first capture, deadlines and unchanged create rollback. Enrich residue without altering `WorktreeRemoval`'s clean semantics. | Extend `Infrastructure/WorktreeManagerTests.cs`; new `Infrastructure/WorktreeDirectoryCleanupTests.cs` for deterministic state sequencing. Preserve `Application/WorktreeResidueSweepTests.cs` behavior. |
| S3: lifecycle context and outcomes | `server/Application/Services/AgentTaskLandService.cs`, `AgentTaskReplyService.cs`; `DelegationWorktreeService.cs` only if a correlation scope is needed. Add the D-5 observations, preserve the direct launch/wait and settlement semantics. | Extend `Application/AgentTaskLandStageOutcomeTests.cs`, `Application/AgentTaskSettlementRaceTests.cs`; verify diagnostic detail survives event, Cleanup row and parent delivery. New focused lifecycle-observation tests if existing harnesses cannot cover direct launch logging. |
| S4: wiring and operations | Update `tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs` and its tests; update direct real `new WorktreeManager(...)` constructors with an explicit no-op/fake diagnostic, rather than optional production fallback that hides DI mistakes. Update `docs/orchestration-loop.md` landing/cleanup guidance and `docs/testing-and-build.md` for the Windows controls/configuration. | `DelegationTestServicesTests`, `DelegationHarnessCensusTests`; run affected constructor callers as named by the implementation diff. No migration, frontend build or E2E browser work. |

## TestDesign handoff requirements

TestDesign owns the executable `## Verification design` section, numbered V/R/PC
items and exact red/green procedures. It must name these cases and the relevant
classes, rather than dispatch a full suite or merely mirror string construction:

1. A separate owned child holds a descendant file without delete sharing. Demonstrate
   an actual removal failure and the expected child PID/name/path in a real available
   provider snapshot and residue. Parent/test-host PID is not the expected owner.
2. A separate owned child has its **current directory equal to the worktree root**.
   Remove children so the root is empty, verify the failing removal, and require that
   child's PID/name with path `.`. Also exercise an explicit held directory handle;
   a nested-file test does not substitute for either directory case.
3. Persistent and transient holders: release after a synchronized first-failure capture,
   not an arbitrary sleep. Persistent lock exhausts at most four filesystem passes;
   released holder becomes clean and leaves a recoverable structured first snapshot.
4. Fake provider/filesystem sequencing: capture precedes fallback/retry; exactly one
   capture per invocation; actual 32/33 codes retry, access denied does not; disappearing
   paths and a new Git lock stop further deletion. Git remove executes only once.
5. Non-elevated, absent/unconfigured tool, unsupported OS, failing controls, malformed
   CSV, timeout, overflow, inaccessible/exited/reused-PID metadata, diagnostic exception,
   and cancellation. Preserve original error and meaningful status; never claim no
   blockers. Unknown ancestry cannot become delegate/build ownership.
6. Exact-root/sibling-prefix/extended-path/quoted-CSV fixtures, output truncation, and
   a holder that permits delete sharing. Observed handles are not necessarily blockers;
   a successful deletion must remain clean with such an owner present.
7. Intentional Git lock, unmerged branch retention, already-unregistered empty root,
   git-success-with-root-leftover, janitor retry, metadata retention, already-landed
   cleanup-only repeat, and diagnostic summaries near existing detail limits.
8. Preserve persist/deliver-before-stop in settlement race tests. Distinguish stopper
   failure from success in observations; verify skipped builds have no new build PID.
   Assert this change makes no additional calls to the session stopper and never
   terminates a holder or an unrelated same-name process.

All newly process-spawning tests use this assembly's
`[ParallelLimiter<ProcessSpawnLimit>]`. Own helpers by `Process` instance/PID plus
creation identity; use a readiness handshake and release channel, await exit, and
clean only their verified temporary paths in `finally`. Any last-resort test teardown
is limited to the test's own child. No production runner, existing leftover worktree,
live build server, or application restart belongs in these tests.

Keep ordinary CI meaningful with fake diagnostics and real unelevated unavailable
coverage. Elevated real-owner tests are a separate explicit lane: if requested, missing
tool/elevation or a failed control is a failure, not a passing skip. If that lane is
unavailable during Code, report it as **not run** and leave automatic owner capture
unverified. TestDesign must not treat the investigation's failed controls as coverage.

Run TUnit through `dotnet run --project tests/Antiphon.Tests`, following the current
testing owner. Example single-class command (for the later Code stage, not run here):

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c443/ -- --treenode-filter "/*/*/WorktreeManagerTests/*" --report-trx --report-trx-filename c443-worktree.trx
```

Use the same shape for each touched/new class above and verify nonzero executed
method names/counts in fresh TRX files. Run suites sequentially; do not run
`Antiphon.Agents.Pty.Tests` concurrently. Use a timer-capable fake or real monotonic
clock for retry tests, not the existing UTC-only mutable clock. Refresh restored
source timestamps/rebuild after red controls so stale DLLs cannot fake restored-green.

## Rollout and acceptance record

No runtime changes or operational configuration are part of this Plan commit.
Code should ship the provider, honest fallback, bounded retry and observations
together. Default absent diagnostic configuration reports unavailability on failure;
healthy landings launch no diagnostic. Existing janitor/Windmill mitigation stays.

The Code report must separately state: deterministic regression results, real
file/root/CWD-control results under the recorded identity/tool version, and any
live first-failure evidence. Do not label the historical root cause fixed on the
strength of passing simulations or successful transient retries alone.

No decision is needed to implement these defaults. A requirement for automatic PID
identification on an unelevated production server would expand scope to a validated
alternative or an operator-approved elevated collector; that requirement would need
an explicit follow-up decision, not silent server elevation.

Planning validation: reviewed the investigation and relevant cleanup/landing/release
code and owner documents, checked the current card and local token, and verified
the Microsoft Handle/Restart Manager contracts above. No application tests, builds,
real lock reproduction or elevated diagnostic runs were performed in Plan.
