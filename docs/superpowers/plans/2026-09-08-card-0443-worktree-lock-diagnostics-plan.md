# CARD-0443: worktree cleanup diagnostics and bounded retry

**Superseded implementation design (2026-09-14).** The authoritative amendment is
[receipt-backed cleanup diagnostics and one guarded retry](2026-09-14-card-0443-receipt-backed-cleanup-plan.md).
It resolves B1/B2/B3 against `27e46463`, preserves CARD-0448, and returns to
**TestDesign**. The original design and 68-PC review below are retained as history;
their filesystem fallback, four-pass retry, LastReason sizing and log-only capture
assumptions must not be implemented. The old PC matrix and cost require revision
against the amendment before Code.

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


## Verification design

TestDesign review: **return to Plan; do not dispatch Code from this revision.**
Inspected 2026-09-14 at `f93b29b3` after the requested fetch/reset to
`origin/master`. The fix design above is preserved verbatim. Its diagnostic goals
remain useful, but D-2/D-3/S2 describe a removal implementation superseded by
CARD-0448. This section records the test obligations and the exact reasons the
Code-readiness gate cannot pass; it does not authorize restoring the old fallback.

Three seams require a Plan amendment:

1. **B1 — the destructive operation is no longer the proposed operation.**
   `WorktreeManager.TryRemoveAsync(string,...)` now returns
   `typed_removal_authority_required`; the typed overload routes to
   `GuardedWorktreeRemoval.RemoveAsync`. That implementation uses ordinary
   `git worktree remove -- <path>`, refuses an unregistered existing directory,
   preserves ignored/dirty/changed sources, and has no recursive filesystem
   fallback. `TryDeleteDirectory` is absent. Retrying four filesystem passes
   would introduce a new destructive authority contrary to the current owner.
   Plan must identify a permitted removal operation and its fresh lease,
   receipt, source, registration and content checks, or explicitly narrow this
   card to first-failure diagnostics. Retrying Git would also change D-3's
   explicit “Git executes once” contract; TestDesign cannot choose that redesign.
2. **B2 — the proposed native error classifier has no producer on this path.**
   `LandingGitResult` carries exit code, output and diagnostic text, not the
   Win32 code from Git's internal deletion. A Git exit 128 or an English sharing
   message cannot establish error 32/33. Plan must name a supported typed
   observation source and where its evidence is captured, or remove the
   native-code retry promise. A fabricated filesystem result is useful to test
   a pure classifier but cannot prove that production reaches it.
3. **B3 — the evidence handoff must follow the current landing protocol.**
   `AgentTaskLandingProtocol.CleanupAsync` assigns `removed.Residue` to
   `op.LastReason`; `AgentTaskLandService` formats that into an outcome and
   commits the terminal event and `AgentTaskLandNotification` together.
   D-4's old `DirectoryResidueReason/ComposeResidue` helpers are not the live
   removal path. Plan must place capture ID/summary/log correlation on the
   typed path through this protocol, preserving the existing transaction,
   notification identity and verifier child observer. It must specify what
   survives a crash after capture but before terminal commit, including the
   recovered-clean case. Rolling-log emission alone is not a durable recipient
   receipt or a guaranteed fsync boundary.

This is an engineering Plan continuation, with no human permission decision.
The lack of an established elevated Handle lane is separately recorded as
unverified acceptance; the allowed unavailable default remains implementable.

### Inspection

All paths below are repository-relative. Only named bodies/ranges were used as
coverage evidence; test names by themselves are not evidence.

| Test / fixture bodies read | Boundaries -> verification/regression or exclusion |
|---|---|
| `Infrastructure/WorktreeManagerTests.cs`: raw-removal, held-file, intentional-lock, local-ancestry, unmerged, janitor and touch methods; `BuildManager`, `MutableTimeProvider`, complete `GitTestEnvironment` | V-4/V-10, R-1. The held-file test asserts only `typed_removal_authority_required`. Its untracked `held.bin` and missing typed receipt cannot reach a real deletion. UTC-only clock cannot test retry timers. |
| `Infrastructure/WorktreeRemovalDefaultTests.cs`, `WorktreeRemovalAuthorityTests.cs` | R-1; interface/legacy entry points preserve bytes. The build-junk test is excluded: its expectation concerns a separately reverted script policy, not this card. No claim about its current pass/fail. |
| Complete `Application/AgentTaskLandRemovalMatrixTests.cs`; complete `TestHelpers/LandingGitFixture.cs` and `LandingSafetyHarness.cs` | V-4/V-5, R-1/R-2. Real receipt-backed Git; both content boundaries, locked registration, removal timeout/error, held tracked file, ref movement, new checkout, registration failure. Fresh schemas and before/after command barriers are available. |
| Complete `Application/AgentTaskLandStageOutcomeTests.cs`, including `CreateLand`, `SeedSucceededWorktreeAsync`, `RequestHeadAsync`, `SeedBuildableAsync`, temporary-directory helper | V-6/V-9, R-3. Real ordinary landing, skipped/failed verification, cleanup-only repeat; `ReplyTo.None` and null message queue cannot prove recipient delivery. |
| `Application/AgentTaskSettlementRaceTests.cs`: worktree pool race, `BuildHarness`, `FlushingSessionStopper`, worktree/shared/session seed bodies | V-9, R-4. Stopper makes real scoped DB writes and runs retirement; final assertion stops at queue insertion. No native process-exit proof. |
| `Application/WorktreeResidueSweepTests.cs`: `execute_never_treats_legacy_landed_event_as_cleanup_authority`, `job_logs_information_summary_and_warning_per_kept_row` | V-10, R-1. Legacy history is not cleanup authority; original Plan's early janitor deletion expectation is obsolete. |
| Complete `TestHelpers/DelegationTestServices.cs`, `DelegationTestServicesTests.cs`, `DelegationHarnessCensusTests.cs` and `ProcessSpawnLimit.cs` | V-10, R-5. Singleton/scoped graph and TryAdd behavior; direct `CreateGitGraph` is a second wiring path. Every newly process-spawning class takes this assembly's limiter. |
| Complete `Application/AgentTaskLandReceiptTests.cs`, `AgentTaskLandNotificationRecoveryTests.cs`; `AgentTaskLandNotificationPersistenceTests.C488_ApprovalOutcomeTransactionAtomic` and `C467_V05_OutcomeObligationMatrix`; `AgentTaskLandPersistenceFailureTests.C467_V06_AtomicSettlementFaultMatrix` | V-7/V-8, R-6. Queue identities, immutable destinations, commit and enqueue cuts, false receipt matrix. Existing seeded receipt tests establish matcher/recovery behavior, not producer-to-recipient delivery. |
| `TestHelpers/BridgeQueueHarness.cs`: options, service construction, session/runtime binding, submission callback; complete `QueuedReceiptAssertions.cs` and `SessionQueueTranscriptPump.cs`; `FakeAgentProtocolAdapter.SendInputAsync`/submission state | V-7/V-8, R-6. Production queue with fake receiving TUI. The callback uses the actually submitted composer body. The file transcript pump currently uses default DB options, so it needs an explicit isolated connection before use here. |
| `PostLandMutationDeliveryTests.ConfirmLandReceiptAsync` and nearby land notification cut arms | Substitute audit: helper overwrites queue receipt fields and injects a runner UserPrompt from the queue body. Do not reuse it as CARD-0443's end-to-end receipt verdict. |
| Production: complete `GuardedWorktreeRemoval`, raw/typed removal and janitor in `WorktreeManager`; protocol cleanup, terminal notification transaction and verifier launch/wait; settlement persist/deliver/release; complete `AgentTaskLandNotificationService` | B1/B2/B3 and delivery inventory. Existing child observers and standing journals are part of the baseline, unlike the original Plan assumption. |
| Full CARD-0443 description, all five revision records; investigation and original plan; owner sections in project context, orchestration loop, testing/build, ops HTTP and session runtime invariants | Original acceptance still requires an actual cause/fix or owner-identification evidence. No Code implementation is recorded in card history. No acceptance inferred from the failed historical Handle controls. |

New files named below are test specifications, **not files already implemented**.
Nearest fixtures were read before naming them: diagnostic/cleanup files use
`LandingGitFixture` plus the existing WorktreeManager Git helper; outcome files
use `LandingSafetyHarness` and `BridgeQueueHarness`; lifecycle files use the
stage-outcome and settlement-race harnesses.

Missing setup, with concrete resolution:

- Add a fixture-owned child helper outside the worktree with four modes:
  descendant file without delete sharing, exact-root current directory,
  explicit root directory handle, and file with delete sharing. It reports
  PID, OS creation ticks and readiness over a pipe, then waits for a release
  message. No arbitrary sleep establishes ownership or unlock timing.
- Add recording external-I/O doubles for executable/token checks, process
  start/read/exit, metadata, and the operation Plan selects for B1/B2. Use
  `Microsoft.Extensions.Time.Testing.FakeTimeProvider` for timer tests, with
  explicit advancement and awaited barriers. Keep `TimeProvider.System` in
  the real queue graph; do not freeze its polling clock.
- Capture sanitized fixtures from the specifically configured installed Handle
  CSV version, with its version/hash and schema recorded. Include actual file,
  root and no-match output. There is no such pinned fixture in the inspected
  test area. Synthetic CSV exercises parsing but cannot qualify that binary.
- Extend the landing harness to combine its exact schema with the bridge
  recipient, fresh notification service/hosted worker and completion flush
  worker. A task must have `ReplyTo.Session` and the bridge session before
  `RequestAsync` snapshots the destination. Do not call `SeedAsync` to
  manufacture the terminal notification in the producer test.
- Add log capture with structured fields and a throwing-sink double. Preserve
  exact request/operation/capture IDs across scopes and observe the actual
  verifier child observer rather than replacing it with a logger.
- Elevated qualification is an explicit separate lane. Configured absolute
  executable, completed license setup and already-authorized elevated identity
  are preconditions. Missing prerequisites or failed file/directory controls
  fail that requested lane; ordinary CI does not silently mark it passed.
  Neither this TestDesign task nor the plan provisions elevation.

### Delivery inventory

There is one changed asynchronous business payload: cleanup diagnostic detail
in the existing land Outcome notification. Lifecycle/capture logs are a separate
observability output; adding those logs must not change settlement delivery.

| Producer -> destination | Persistence boundary and durable identity | Recovery and observable receipt |
|---|---|---|
| Typed removal capture -> structured server logging | Unique capture ID plus task ID, landing operation ID, attempt and normalized source. D-4 requests rolling logs outside target; it does not specify a transactional durable store. | Recovered-clean capture must remain queryable in the log sink. A crash before the log sink persists can lose it; B3 requires explicit treatment. A log entry is observation evidence only. |
| `AgentTaskLandingProtocol.CleanupAsync` -> `AgentTaskLandService.CompleteTerminalLockedAsync` -> committed outcome/notification | `AgentTaskLanding.Id`, `AgentTaskLandRequest.Id`, terminal `AgentTaskEvent.Id`, `AgentTaskLandNotification.Id/SourceEventId/RequestId/LandingOperationId`. Diagnostic capture ID is payload correlation, not a substitute for those keys. Terminal event, completed request and notification obligation commit in the existing transaction. | Before commit: recovery follows saved cleanup intent and emits only the recovered authoritative result; old capture must not be mislabeled a fresh observation. After commit/lost acknowledgment: recover the same immutable notification. Require the recipient evidence below. |
| Notification worker -> production `SessionMessageQueueService` -> original parent session | Unique `SourceLandNotificationId` links queue row to notification; queue ID, destination session and immutable body/content digest remain stable. Enqueue can commit before notifier acknowledgment. | Before-enqueue failure retries when due. Committed insert/lost ack reuses the row. Lost completion wakeup recovers through hosted/queue recovery. Busy recipient stays pending until TurnEnd; already-eligible recipient receives without requiring a future turn. |
| Queue -> receiving protocol adapter -> persisted transcript -> receipt reconciler | Original session ID and queued body including request/notification/capture IDs; recorded attempt sequence or time floor; confirming complete `UserPrompt` sequence | Lost queue ack, persisted prompt before receipt-save failure, and worker restart must recover the original row/receipt without submitting the body twice. Only the complete matching UserPrompt after the attempt floor discharges the obligation. |
| Existing `AgentTaskReplyService` completion -> parent queue; added release observations | Existing source task ID + report content digest and parent session. Settlement persists before enqueue/publish before stop. No new queue or destination is introduced. | V-9 proves log failures/stop failures leave this order and eventual parent receipt intact. It does not assert OS exit from a returned stop request. |

V-7 is a producer-to-recipient test: request an authorized real Git landing,
cause first-failure diagnostics on the permitted removal operation, allow the
real terminal transaction to create the note, start the real notification and
completion-flush workers, and let the real queue submit to the receiving
adapter. Read the complete UserPrompt via a **fresh** DB context. Assert its
normalized complete body, original session, request ID, notification ID, capture
ID, owner name/PID or unavailable reason, and sequence/time floor, then assert
the durable notification points at that same confirming sequence. Run both
busy and already eligible recipients. Busy state is set before the producer
starts; the idle test must not manually call TurnEnd to rescue a lost wakeup.

V-8 repeats that chain at each handoff cut: terminal before-save, after-save
before-commit, commit failure, after-commit before publish; before-enqueue;
queue-inserted before acknowledgment; notifier saved before flush wakeup;
submitted prompt before transcript commit; transcript committed before receipt
save; receipt-save acknowledgment loss. Use fixture barriers, a fresh service
graph and the same schema. For the native/crash lane, terminate only an owned
worker after its readiness handshake and await it; keep the recipient child and
its transcript owned by the parent so recovery can inspect actual receipt.
At every cut record pre-recovery DB rows, post-recovery identity, actual submitted
body count and full recipient evidence. Pre-commit cuts may produce a new
capture; they must not claim the original capture survived when it did not.

Substitutes and their limits:

- Controlled filesystem/Git/provider doubles prove ordering and fault handling.
  They cannot prove a Windows sharing code was observed from production Git or
  that Handle can enumerate the deployed identity.
- `BridgeQueueHarness` + `QueuedReceiptAssertions` uses the real queue and a
  fake TUI's actually submitted body. This proves application handoff and
  complete receipt matching; it cannot prove ConPTY/Handle behavior or an
  authenticated provider's native transcript.
- A native transcript helper, if commissioned for transport evidence, must
  parse the recipient's own file and use this test's schema. Manually inserting
  a prompt copied from `queued.Body`, setting Sent, transport ack, queue insert
  and terminal event are never substitutes for the producer-to-recipient test.
- Log sink observations prove emitted evidence fields; they do not prove durable
  logging across abrupt machine failure or receipt by the parent session.

Review must reject a verification result that stops at any of those substitutes
without the required recipient evidence. Delivery guards G-55 through G-66 each
have their own positive control below.

### Proves it works now

“Expected” below means a requirement for later Code execution, not a claim that
CARD-0443 currently works. B-marked tests depend on the Plan correction. Use the
exact `C443_...` methods in the PC table as the named safety cases within these
classes; add the real Windows methods explicitly named in V-5.

- V-1: configuration, platform, current token, fixed argument vector and
  calibration | unit, `WorktreeLockDiagnosticsTests` | PC-1..9 methods |
  explicit unsupported/not configured/tool missing/insufficient privilege
  states; no launch on refusal; target query then one file+directory control.
  Cross configured/unconfigured with elevated/unelevated and supported/unsupported
  OS; a valid path cannot override either platform or privilege refusal.
- V-2: parser, path and identity correctness | unit,
  `WorktreeLockDiagnosticsTests` | PC-32..39 methods |
  quoted commas/escaped quotes/CRLF, invalid headers/columns/PID, exact root,
  child, sibling prefix, mixed case, supported extended prefix, unresolved
  device alias, inaccessible/exited/reused PID and parent; partial results retain
  known owners. Zero owners is permitted only with successful controls and
  complete parseable output; wording never asserts “no blockers.”
- V-3: bounded capture and cancellation | unit plus owned process helper,
  `WorktreeLockDiagnosticsTests` | PC-25..31 methods |
  combined output 262143/262144/262145 bytes across either/both streams;
  31/32/33 distinct owners; serialized UTF-8 size 32767/32768/32769;
  target/control/enrichment hangs share five seconds. Cancellation before launch,
  during either query and during enrichment propagates. Diagnostic child/readers
  finish before return; no owner process is stopped.
- V-4: first-failure sequencing and permitted retry | deterministic I/O,
  `WorktreeDirectoryCleanupTests` | PC-10..16,19..24,40,68 methods |
  B1/B2 must be resolved first. Preserve the original three-arm matrix (ordinary
  Git error/timeout, already-unregistered existing root, success-with-root-left)
  as explicit Plan decisions; current unregistered root **refuses** deletion.
  Retryable/nonretryable, persistent/released holder, vanished/recreated path,
  newly locked/unknown registration and changed authority/content are crossed
  at every selected retry boundary. Test 9.999/10/10.001-second starts and
  synchronous calls returning after expiry. No extra work after success,
  cancellation, nonretryable code or exhausted allowance.
- V-5: real Windows owner evidence | integration,
  `WorktreeLockDiagnosticsWindowsTests` |
  `C443_SeparateFileHolder`, `C443_EmptyRootCwdHolder`,
  `C443_ExplicitRootDirectoryHandle`, `C443_TransientHolderRelease`,
  `C443_DeleteSharingOwnerCanBeClean` |
  a **different owned child** supplies expected PID/name/creation identity,
  with root path `.` in both directory modes. File holder opens an already
  committed tracked file so dirty/untracked guards cannot win first.
  For the empty-root case establish Git/receipt authority before Git removes
  child entries, then prove the residual root is empty at capture; do not delete
  its registration/admin metadata merely to force the experiment. Assert actual
  removal failure before accepting the snapshot. Persistent holder stays alive;
  transient holder is released only after first snapshot is recorded. The
  delete-sharing control may have observable handles and clean deletion.
  Run unelevated-unavailable coverage separately. Real elevated owner lane not
  run means automatic owner capture and original owner-identification
  acceptance remain unverified.
- V-6: bounded residue reaches durable landing output | integration,
  `AgentTaskWorktreeLockOutcomeTests` | PC-41,43..45 methods |
  summaries at 949/950/951 source characters, stage input near 999/1000/1001,
  long original/last errors, 0/1/32 owners plus omissions. Complete formatted
  stage row and event, not just the inner residue string, retain diagnostic
  minimum fields. Include unavailable/partial/timed-out diagnostics and
  clean-after-retry; remote observer confirms unchanged publication SHAs.
- V-7: diagnostic outcome producer-to-recipient | integration,
  `AgentTaskWorktreeLockOutcomeTests` |
  `C443_BusyRecipientGetsOutcomeWhenIdle`,
  `C443_EligibleRecipientGetsOutcome` | complete matching UserPrompt and
  durable confirming sequence; busy recipient receives only after eligibility,
  idle recipient requires no future turn. Run OwnersObserved and
  InsufficientPrivileges payloads in both arms, including the longest
  accepted bounded summary.
- V-8: all delivery handoff recovery | integration,
  `AgentTaskWorktreeLockOutcomeTests` | PC-55..57,66 methods with the cut
  matrix above, plus `C443_TranscriptCommitRecovery` for the receiving
  adapter/transcript boundary | original keys/digest/destination maintained;
  producer outcome replay never substitutes a second publication;
  eventual complete UserPrompt; no duplicate submission after actual receipt.
  Pair each cut with busy/eligible recipient where no receipt exists yet.
  After receipt, busy/eligible is excluded because recovery must not type at all.
- V-9: release/verification observations | integration,
  `AgentTaskWorktreeLockLifecycleTests` | PC-48..53,67 methods |
  fresh-context settlement/queue evidence precedes stopper entry;
  returned/throwing stopper observations differ without asserting OS exit;
  Worktree versus Shared/standing; built versus skipped; child PID plus true OS
  start identity when launched; observer callback fault leaves its journal and
  existing cleanup semantics intact. Sink failure cannot suppress parent receipt.
- V-10: baseline composition and preservation | existing unit and named
  integration methods | R-1..5 below | real graph resolves through both helper
  paths, existing fake retained, legacy janitor refuses, source/remote bytes and
  cleanup-only semantics remain protected.

Execution recipe for Code, **after an amended Plan/TestDesign passes the gate**:

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c443/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c443/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c443-unit
$c443Classes = @(
  'WorktreeLockDiagnosticsTests',
  'WorktreeDirectoryCleanupTests',
  'AgentTaskWorktreeLockOutcomeTests',
  'AgentTaskWorktreeLockLifecycleTests',
  'WorktreeManagerTests',
  'AgentTaskLandRemovalMatrixTests',
  'AgentTaskLandStageOutcomeTests',
  'AgentTaskSettlementRaceTests',
  'AgentTaskLandReceiptTests',
  'DelegationTestServicesTests',
  'DelegationHarnessCensusTests'
)
foreach ($c443Class in $c443Classes) {
  dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c443/ -- --treenode-filter "/*/*/$c443Class/*" --report-trx --report-trx-filename "$c443Class.trx" --results-directory ".antiphon/c443-$c443Class"
  if ($LASTEXITCODE -ne 0) { throw "Failed: $c443Class" }
}
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c443/ -- --treenode-filter '/*/*/WorktreeResidueSweepTests/execute_never_treats_legacy_landed_event_as_cleanup_authority' --report-trx --report-trx-filename residue.trx --results-directory .antiphon/c443-residue
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c443/ -- --treenode-filter '/*/*/WorktreeRemovalDefaultTests/C448_V36_InterfaceDefaultsNeverDelegateDeletion' --report-trx --report-trx-filename defaults.trx --results-directory .antiphon/c443-defaults
```

Windows explicit lane selects `/*/*/WorktreeLockDiagnosticsWindowsTests/*`
with the same isolated output/TRX shape after its declared preconditions are
verified. Do not silently include a skipped elevated lane in the ordinary count.
Each result directory must be fresh for the run. Validate the intended method
names and nonzero execution counts in TRX; command exit zero or discovery alone
does not suffice. Add only constructor callers actually changed by Code to this
inventory. Do not run a full assembly for this bounded change.

### Guards the regression

- R-1: no cleanup authority from legacy input, age or ancestry |
  `WorktreeRemovalDefaultTests.C448_V36_InterfaceDefaultsNeverDelegateDeletion`,
  the raw-refusal/held-file/janitor methods read above, and
  `WorktreeResidueSweepTests.execute_never_treats_legacy_landed_event_as_cleanup_authority` |
  exact refusal, zero removal/prune count and retained source/ref bytes.
- R-2: diagnostics cannot weaken receipt-backed deletion |
  `AgentTaskLandRemovalMatrixTests.C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries`,
  `C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate`,
  `C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent` |
  invalid authority performs no destructive command; valid case uses exactly
  nonforcing Git remove and old-SHA compare/delete; each failed component
  preserves remaining bytes/ref and remote source.
- R-3: cleanup detail does not rewrite publication |
  `AgentTaskLandStageOutcomeTests.reland_of_an_already_landed_task_runs_cleanup_only`
  plus V-6 outcome cases | one publication, subsequent `LandingCleanup`,
  same operation and remote evidence, separate Cleanup stage; diagnostics failure
  cannot turn a completed delete into refusal.
- R-4: release observations cannot recreate CARD-0319 |
  `AgentTaskSettlementRaceTests.worktree_pool_settle_delivers_parent_note_when_kill_savechanges_races_retire`
  plus V-9 receipt extension | one keyed parent note persisted before stop and
  a complete matching recipient prompt after the receiver becomes eligible.
- R-5: DI wiring cannot silently replace an installed fake |
  `DelegationTestServicesTests` whole graph, settings and TryAdd bodies;
  `DelegationHarnessCensusTests` | exact fake by reference, one registration,
  real graph resolves from both helper paths, no new hand-rolled graph.
- R-6: observed output is not caller receipt |
  `AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts` and its six
  `C488_ApprovalReceiptNeeds...` wrappers, plus V-7/V-8 |
  only complete correlated UserPrompt after the attempt floor confirms;
  queue-enqueue, queued-prompt, Sent, screen Delivered, wrong session/identity,
  head-only, head/tail splice and old sequence/time all leave receipt unconfirmed.

### Guard inventory

Inventory covers the original D-1..D-5/S1..S4 obligations and the existing safety
boundaries that those edits would cross. All guards are recorded, including
the ones blocked by the obsolete retry design. The unchanged internals of
CARD-0448 publication/verification admission retain their own full PC inventory;
R-2 here protects the intersection, not a replacement of that plan's matrix.

Each row has exactly one distinct PC. “B” in the PC table marks an unresolved
B1/B2/B3-dependent operation, rather than permission to implement the obsolete
destructive fallback. There are no “none” guard exemptions.

| Guard | Plan reference and safety-critical invariant | Positive control |
|---|---|---|
| G-1 | D-1 unsupported platform stays unavailable | PC-1 |
| G-2 | D-1 trusted absolute configured executable only | PC-2 |
| G-3 | D-1 invocation identity must already be elevated | PC-3 |
| G-4 | D-2 fixed read-only argument vector; no shell/close-handle switches | PC-4 |
| G-5 | D-2 diagnostic cwd, controls and logs outside target | PC-5 |
| G-6 | D-2 target query precedes calibration | PC-6 |
| G-7 | D-2 known-file calibration independently required | PC-7 |
| G-8 | D-2 known-directory calibration independently required | PC-8 |
| G-9 | D-2 owned calibration resources always released | PC-9 |
| G-10 | D-2 first-failure capture before next destructive operation | PC-10 |
| G-11 | D-2 one capture per cleanup invocation | PC-11 |
| G-12 | D-2 intentional initial Git lock is a refusal | PC-12 |
| G-13 | D-3 newly observed intentional Git lock stops retry | PC-13 |
| G-14 | D-3 indeterminate registration never authorizes retry | PC-14 |
| G-15 | D-3 confinement rechecked at retry/finalization | PC-15 |
| G-16 | D-3 current receipt/content/identity safety survives a retry interval | PC-16 |
| G-17 | S2 versus CARD-0448 raw removal grants no authority | PC-17 |
| G-18 | S2 versus CARD-0448 no forced removal or recursive fallback | PC-18 |
| G-19 | D-3 only native sharing/lock errors 32/33 retry | PC-19 |
| G-20 | D-3 maximum four filesystem passes | PC-20 |
| G-21 | D-3 prescribed backoff; stop after success | PC-21 |
| G-22 | D-3 one monotonic ten-second additional-work allowance | PC-22 |
| G-23 | D-3 registration reads consume remaining allowance | PC-23 |
| G-24 | D-3 synchronous filesystem operation must finish before return | PC-24 |
| G-25 | D-2/D-3 outer cancellation remains cancellation | PC-25 |
| G-26 | D-2 shared five-second diagnostic budget includes both queries/enrichment | PC-26 |
| G-27 | D-2 combined stdout/stderr cap 256 KiB | PC-27 |
| G-28 | D-2 parsed-owner cap 32 with omitted count | PC-28 |
| G-29 | D-2 structured evidence cap 32 KiB | PC-29 |
| G-30 | D-2 timeout/overflow reaps diagnostic child and drains readers | PC-30 |
| G-31 | D-2/D-5 discovered owners never terminated | PC-31 |
| G-32 | D-2 installed CSV schema validated and quoted fields parsed | PC-32 |
| G-33 | D-2 exact root and descendant separator containment | PC-33 |
| G-34 | D-2 unresolved device/alias paths make coverage partial | PC-34 |
| G-35 | D-2 empty finding requires complete query and successful controls | PC-35 |
| G-36 | D-2 partial coverage preserves positively observed owners | PC-36 |
| G-37 | D-5 PID creation identity prevents reused-process enrichment | PC-37 |
| G-38 | D-5 uncertain ancestry never becomes exclusive lifecycle attribution | PC-38 |
| G-39 | D-2 target disappears before capture | PC-39 |
| G-40 | D-3/D-4 original and last typed failures retained | PC-40 |
| G-41 | D-4 bounded summary reserves useful diagnostic fields | PC-41 |
| G-42 | D-4 no sensitive or unrelated raw diagnostic data | PC-42 |
| G-43 | D-4 recovered cleanup retains first-failure structured log | PC-43 |
| G-44 | D-4 diagnostics cannot turn successful deletion into failure | PC-44 |
| G-45 | D-4 publication/SHA and residue remain separate facts | PC-45 |
| G-46 | D-4 cleanup-only repeat preserves operation and does not republish | PC-46 |
| G-47 | D-3 retained directory/registration/ref prevents false clean | PC-47 |
| G-48 | D-5 settlement persistence and delivery attempt precede release | PC-48 |
| G-49 | D-5 actual stopper failure is distinct from request return/OS exit | PC-49 |
| G-50 | D-5 skipped verification has no new verification PID | PC-50 |
| G-51 | D-5 added observations preserve verifier child journaling/drain ordering | PC-51 |
| G-52 | D-5 new lifecycle observations limited to Worktree tasks | PC-52 |
| G-53 | D-5 no additional stopper calls | PC-53 |
| G-54 | S4 explicit diagnostics registration preserves harness overrides | PC-54 |
| G-55 | D-4 terminal result and notification obligation commit atomically | PC-55 |
| G-56 | D-4 enqueue recovery retains durable notification key/body | PC-56 |
| G-57 | D-4 lost in-memory wakeup recovers committed outcome | PC-57 |
| G-58 | D-4 busy recipient waits until eligible | PC-58 |
| G-59 | D-4 already eligible recipient receives through production queue | PC-59 |
| G-60 | D-4 receipt requires UserPrompt kind | PC-60 |
| G-61 | D-4 receipt requires original recipient session | PC-61 |
| G-62 | D-4 receipt requires matching notification identity | PC-62 |
| G-63 | D-4 receipt requires complete submitted body | PC-63 |
| G-64 | D-4 receipt must be later than attempt sequence floor | PC-64 |
| G-65 | D-4 null sequence uses bounded attempt-time floor | PC-65 |
| G-66 | D-4 receipt-save failure recovers without typing again | PC-66 |
| G-67 | D-5 logging/enrichment failures never change release/delivery | PC-67 |
| G-68 | D-3 create/recovery paths do not inherit cleanup retries or diagnostics | PC-68 |

### Positive controls

These are mutation specifications for the later implementation, not executed
controls. Code implements the V/R methods; ordinary Review judges the diff and
their evidence before land. SourceLanding Mutation reports **break, intended
assertion red, restore, fresh-build green** at the landed source for every PC.
No snapshot commit/push; use its assigned external evidence root.

“U” = deterministic unit; “I” = integration/owned helper; “B” = operation or
evidence seam blocked by the Plan findings. For each row use the **exact**
`/*/*/ClassName/MethodName` formed from the two named columns, never a
whole class. A data-driven method runs its declared boundary rows; report all
expanded outcomes. In the mutation column, change only the named production
branch/expression/call, preserving signatures and types. Process-start/stop
mutations use recording doubles; never pass forbidden arguments to real Handle
or terminate a discovered real process. The lifecycle tests' stopper and logging
failures are injected test seams, not live session operations.

B controls cannot yet be asserted to be compiling/executable against a valid
implementation: the underlying operation is precisely what Plan must resolve.
They are retained as concrete rejected-contract tests, so a redesign cannot
silently drop their safety obligations. Other newly named methods also require
Code implementation before mutation qualification. Existing methods have been
read, not executed or mutated in this stage.

| PC | Class | Exact method | Break the guard by this defect | Expected red assertion | Lane |
|---|---|---|---|---|---|
| PC-1 | `WorktreeLockDiagnosticsTests` | `C443_UnsupportedPlatform` | Replace the unsupported-platform result with NoOwnersObserved | Status == Unavailable and Reason == UnsupportedPlatform | U |
| PC-2 | `WorktreeLockDiagnosticsTests` | `C443_RejectUntrustedToolPath` | Accept a relative executable path in the configured-path validator | LaunchCount == 0 for relative, empty and missing paths; explicit unavailable reason | U |
| PC-3 | `WorktreeLockDiagnosticsTests` | `C443_InsufficientPrivileges` | Replace the invocation-token check with true | LaunchCount == 0 and Reason == InsufficientPrivileges with a configured existing tool | U |
| PC-4 | `WorktreeLockDiagnosticsTests` | `C443_ReadOnlyArgumentVector` | Append -c to the argument vector passed to the recording process runner | Arguments equals [-nobanner, -v, normalizedRoot] and UseShellExecute == false | U |
| PC-5 | `WorktreeLockDiagnosticsTests` | `C443_DiagnosticLocationsOutsideTarget` | Set the diagnostic WorkingDirectory to the removal target | Every launch/control/log path is outside the target and controls are outside every fixture worktree | U |
| PC-6 | `WorktreeLockDiagnosticsTests` | `C443_TargetQueryPrecedesControls` | Swap the target and control query calls | Trace starts TargetQuery, then ControlQuery, with target output preserved before calibration | U |
| PC-7 | `WorktreeLockDiagnosticsTests` | `C443_MissingFileControl` | Ignore the held-file match when computing the calibration verdict | Reason == PositiveControlFailed when only the directory and expected PID are observed | U |
| PC-8 | `WorktreeLockDiagnosticsTests` | `C443_MissingDirectoryControl` | Ignore the held-directory match when computing the calibration verdict | Reason == PositiveControlFailed when only the file and expected PID are observed | U |
| PC-9 | `WorktreeLockDiagnosticsTests` | `C443_ControlResourcesDisposed` | Remove the control-resource disposal from finally | Both handle disposal counts == 1; only the unique owned control directory is removed on error and cancellation | U |
| PC-10 | `WorktreeDirectoryCleanupTests` | `C443_CaptureBeforeNextMutation` | Move capture after the first retry/fallback call | Trace places Capture immediately after FirstFailure and before any later Delete; first error/time unchanged | B |
| PC-11 | `WorktreeDirectoryCleanupTests` | `C443_OneCapturePerInvocation` | Reset the captured flag inside the retry loop | CaptureCount == 1 after repeated retryable failures | B |
| PC-12 | `WorktreeDirectoryCleanupTests` | `C443_InitialGitLockRefuses` | Remove the initial locked-registration return | CaptureCount == 0, RetryCount == 0 and subsequent destructive calls == 0 | B |
| PC-13 | `WorktreeDirectoryCleanupTests` | `C443_NewGitLockStopsRetry` | Ignore Locked in the registration recheck before the second pass | No deletion after the barrier installs an explicit Git lock; lock remains | B |
| PC-14 | `WorktreeDirectoryCleanupTests` | `C443_UnknownRegistrationStopsRetry` | Convert a failed registration query into an empty registration list | Destructive calls after unknown/error/timeout registration == 0; residue retained | B |
| PC-15 | `WorktreeDirectoryCleanupTests` | `C443_RecheckConfinement` | Reuse the initially normalized path after a fixture-controlled target replacement | No mutation under the outside-root sentinel; returned result is nonclean | B |
| PC-16 | `WorktreeDirectoryCleanupTests` | `C443_RetryRevalidatesAuthorityAndContents` | Reuse the first accepted removal inspection for a later retry | At each changed receipt/source/ignored-content boundary, no further mutation and exact sentinel bytes remain | B |
| PC-17 | `WorktreeManagerTests` | `WorktreeManager_try_remove_reports_directory_residue_when_a_file_is_held` | Change the raw overload's returned residue to null and all three completion flags to true | Residue == typed_removal_authority_required and DirectoryGone == false | I |
| PC-18 | `AgentTaskLandRemovalMatrixTests` | `C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent` | Add --force to GuardedWorktreeRemoval's ordinary Git remove arguments | Trace contains neither --force nor prune; failed-removal sentinel remains | I |
| PC-19 | `WorktreeDirectoryCleanupTests` | `C443_RetryOnlyNativeSharingCodes` | Treat any IOException as retryable | RetryCount == 0 for codes 5, 145 and an unclassified IOException; positive rows 32 and 33 retry | B |
| PC-20 | `WorktreeDirectoryCleanupTests` | `C443_FourPassMaximum` | Increase maximum deletion passes from 4 to 5 | DeletePassCount == 4 for a persistent sharing failure | B |
| PC-21 | `WorktreeDirectoryCleanupTests` | `C443_PrescribedBackoff` | Change the second delay from 1 second to 250 ms | ScheduledDelays equals [250ms, 1s, 2s]; no scheduled delay after success | B |
| PC-22 | `WorktreeDirectoryCleanupTests` | `C443_SharedAdditionalWorkDeadline` | Restart the allowance after capture completes | No new delay/pass starts at or after firstFailureTimestamp + 10 seconds | B |
| PC-23 | `WorktreeDirectoryCleanupTests` | `C443_RegistrationUsesRemainingBudget` | Give each retry registration query a fresh 30-second token | Captured registration deadline is no later than the original ten-second deadline | B |
| PC-24 | `WorktreeDirectoryCleanupTests` | `C443_NoAbandonedDelete` | Return on cancellation without joining the owned simulated synchronous deletion operation | Cleanup task remains incomplete until the held deletion exits; no mutation occurs after return | B |
| PC-25 | `WorktreeLockDiagnosticsTests` | `C443_PropagateOuterCancellation` | Catch OperationCanceledException and return Failed | Thrown OperationCanceledException carries the caller token; no subsequent launch/retry | U |
| PC-26 | `WorktreeLockDiagnosticsTests` | `C443_SharedDiagnosticBudget` | Allocate a fresh five-second budget for the control query | Control/enrichment deadline <= target-start + 5 seconds; outcome TimedOut at exhaustion | U |
| PC-27 | `WorktreeLockDiagnosticsTests` | `C443_CombinedOutputBound` | Apply the byte limit independently to stdout and stderr | Combined retained bytes <= 262144 and overflow reports OutputTruncated | U |
| PC-28 | `WorktreeLockDiagnosticsTests` | `C443_OwnerCountBound` | Increase the retained-owner limit to 33 | Owners.Count == 32 for 33 unique matches and OmittedOwners == 1 | U |
| PC-29 | `WorktreeLockDiagnosticsTests` | `C443_StructuredEvidenceBound` | Bypass the final serialized evidence size check | UTF8 serialized size <= 32768 and truncation is explicit | U |
| PC-30 | `WorktreeLockDiagnosticsTests` | `C443_DiagnosticChildReaped` | Return TimedOut before awaiting the started diagnostic child's exit/readers | Owned child exit and both reader-completion observations precede provider completion | I |
| PC-31 | `WorktreeLockDiagnosticsTests` | `C443_OwnersAreObservationOnly` | Route a discovered owner PID to the injected process-stop recorder | OwnerStopCalls == 0, including an unrelated same-name owner; diagnostic child disposal is separate | U |
| PC-32 | `WorktreeLockDiagnosticsTests` | `C443_ValidateCsvSchema` | Replace CSV field parsing with Split(',') | Quoted comma/escaped-quote row yields exact process/path; unknown or malformed schema never yields NoOwnersObserved | U |
| PC-33 | `WorktreeLockDiagnosticsTests` | `C443_ExactRootAndDescendants` | Use StartsWith(root) without the separator boundary | Root is '.', descendant included, sibling-prefix owner excluded under mixed case and trailing separators | U |
| PC-34 | `WorktreeLockDiagnosticsTests` | `C443_UnresolvedAliasIsPartial` | Drop unresolved paths without setting partial coverage | Status == Partial and unresolved coverage reason survives alongside valid owners | U |
| PC-35 | `WorktreeLockDiagnosticsTests` | `C443_EmptyRequiresCompleteCoverage` | Return NoOwnersObserved whenever the parsed owner list is empty | Every failed/partial/timed-out/malformed/control-failed row is not NoOwnersObserved; completed calibrated empty row is | U |
| PC-36 | `WorktreeLockDiagnosticsTests` | `C443_PartialRetainsOwners` | Clear Owners when changing the result status to Partial | Expected owner identity/path remains in the partial snapshot and summary | U |
| PC-37 | `WorktreeLockDiagnosticsTests` | `C443_ReusedPidDoesNotEnrichOldOwner` | Join process metadata on PID alone | Reused-PID start identity does not attach the new name/parent; unavailable/exited metadata is explicit | U |
| PC-38 | `WorktreeLockDiagnosticsTests` | `C443_UnknownAncestryStaysUnknown` | Label an owner as landing-created from parent PID alone | Attribution remains unknown for exited/reused parent and shared build server | U |
| PC-39 | `WorktreeLockDiagnosticsTests` | `C443_PathGone` | Return NoOwnersObserved when the target existence check reports absent | Status == PathGone and target-query launch count == 0 | U |
| PC-40 | `WorktreeDirectoryCleanupTests` | `C443_PreserveFirstAndLastErrors` | Overwrite FirstFailure with the last retry error | FirstFailure retains error 32/time/operation; LastFailure retains distinct code 5/time/operation | B |
| PC-41 | `AgentTaskWorktreeLockOutcomeTests` | `C443_SummaryPreservesEvidenceAtLimit` | Append diagnostics after a long raw error then rely on the later Clip | Residue <= 950 chars, diagnostic allocation <= 450, first name/PID or unavailable reason plus UTC/capture ID survives stage and outcome | I |
| PC-42 | `WorktreeLockDiagnosticsTests` | `C443_SanitizedStructuredEvidence` | Include raw stdout in the structured log payload | Synthetic secret/command/environment/user/unrelated-handle markers absent from every captured field and log | U |
| PC-43 | `AgentTaskWorktreeLockOutcomeTests` | `C443_RecoveredCleanupKeepsCapture` | Write the capture event only when final cleanup is nonclean | Exactly one capture event remains with first/last codes, attempt count and final clean result after holder release | B |
| PC-44 | `AgentTaskWorktreeLockOutcomeTests` | `C443_DiagnosticFailureDoesNotRefuseClean` | Set cleanup failure whenever the diagnostic status is Unavailable | Publication stays confirmed, cleanup Complete, terminal Landed for successful authorized cleanup | I |
| PC-45 | `AgentTaskWorktreeLockOutcomeTests` | `C443_ResidueKeepsPublication` | Map a cleanup diagnostic error to LandRefused | Terminal LandedWithResidue, Cleanup Failed stage, same VerifiedSourceSha/ObservedRemoteTargetSha/RemoteConfirmedAt | I |
| PC-46 | `AgentTaskLandStageOutcomeTests` | `reland_of_an_already_landed_task_runs_cleanup_only` | Remove the alreadyReported conversion to LandingCleanup in CompleteTerminalLockedAsync | Exactly one Landed publication and one LandingCleanup; second run adds only one Cleanup stage row | I |
| PC-47 | `AgentTaskLandRemovalMatrixTests` | `C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent` | Report branchGone true after update-ref failure | IsClean == false and the raced source ref equals retained SHA in the branch-moved row | I |
| PC-48 | `AgentTaskWorktreeLockLifecycleTests` | `C443_PersistDeliverBeforeStop` | Move ReleaseDelegateAsync before DeliverToParentAsync | At stopper entry a fresh DB context sees settled task and keyed parent note; event trace orders persist, enqueue, publish, stop | I |
| PC-49 | `AgentTaskWorktreeLockLifecycleTests` | `C443_StopFailureIsNotExit` | Emit stop-succeeded from the stopper catch | Failure observation exists, success/OS-exit observation absent, parent receipt still completes | I |
| PC-50 | `AgentTaskWorktreeLockLifecycleTests` | `C443_SkippedVerifyHasNoChild` | Assign the server PID to the skipped verification observation | Skipped log has no child PID/start time and verifier launch count == 0 | I |
| PC-51 | `AgentTaskWorktreeLockLifecycleTests` | `C443_ObserverOrderingUnchanged` | Call the new success observation before awaiting observer.ExitedAsync | Existing before-start/start/exit/drained trace completes before final observation; injected observer failure retains existing failure outcome | I |
| PC-52 | `AgentTaskWorktreeLockLifecycleTests` | `C443_WorktreeOnlyObservations` | Remove the Workspace == Worktree log guard | New observation count == 0 for Shared and standing-agent controls | I |
| PC-53 | `AgentTaskWorktreeLockLifecycleTests` | `C443_NoAdditionalStopperCalls` | Invoke the stopper again after emitting its return observation | Owned worktree stopper call count == 1; standing/shared holder stop count unchanged | I |
| PC-54 | `DelegationTestServicesTests` | `C443_DiagnosticRegistrationPreservesOverride` | Change diagnostics registration from TryAddSingleton to AddSingleton | Previously installed fake resolves by reference and exactly one diagnostics descriptor exists | U |
| PC-55 | `AgentTaskWorktreeLockOutcomeTests` | `C443_OutcomeTransactionRecovery` | Remove AddNotification from CompleteTerminalLockedAsync | After terminal commit there is exactly one same-request/source-event Outcome notification; recovery reaches matching UserPrompt | I |
| PC-56 | `AgentTaskWorktreeLockOutcomeTests` | `C443_EnqueueAcknowledgementRecovery` | Omit sourceLandNotificationId in AgentTaskLandNotificationService's enqueue call | After committed insert/lost acknowledgment one queue row has original notification ID/digest and recipient receives once | I |
| PC-57 | `AgentTaskWorktreeLockOutcomeTests` | `C443_LostWakeupRecoversReceipt` | Exclude AwaitingReceipt notifications from the hosted recovery scan | Already-eligible caller eventually has matching complete UserPrompt after wakeup loss and worker restart | I |
| PC-58 | `AgentTaskWorktreeLockOutcomeTests` | `C443_BusyRecipientGetsOutcomeWhenIdle` | Enqueue the land outcome with MessageSendMode.Now | Busy barrier has zero matching UserPrompts and zero submitted bodies; after TurnEnd one complete matching prompt | I |
| PC-59 | `AgentTaskWorktreeLockOutcomeTests` | `C443_EligibleRecipientGetsOutcome` | Remove the completion flush enqueue while suppressing the recovery scan in the bounded first-attempt fixture | Eligible caller gets one complete matching UserPrompt within the fixture deadline, without a synthetic TurnEnd trigger | I |
| PC-60 | `AgentTaskLandReceiptTests` | `C488_ApprovalReceiptNeedsUserPrompt` | Remove the TranscriptKinds.UserPrompt predicate and accept the Sent row directly | ConfirmedAt remains null in the sent-only control | I |
| PC-61 | `AgentTaskLandReceiptTests` | `C488_ApprovalReceiptNeedsDestination` | Remove the AgentSessionId predicate from the prompt query | ConfirmedAt remains null for a complete body in the wrong session | I |
| PC-62 | `AgentTaskLandReceiptTests` | `C488_ApprovalReceiptNeedsIdentity` | Compare only a fixed outcome heading instead of the correlated body | ConfirmedAt remains null for the wrong-notification-ID UserPrompt | I |
| PC-63 | `AgentTaskLandReceiptTests` | `C488_ApprovalReceiptNeedsCompleteBody` | Replace full-body matching with a matching first 200 characters | ConfirmedAt remains null for the head-only UserPrompt | I |
| PC-64 | `AgentTaskLandReceiptTests` | `C488_ApprovalReceiptNeedsSequenceFloor` | Remove the Sequence > floor filter | ConfirmedAt remains null for the equal-floor UserPrompt | I |
| PC-65 | `AgentTaskLandReceiptTests` | `C488_ApprovalReceiptNeedsTimeFloor` | Remove the Timestamp >= floorTime filter | ConfirmedAt remains null for the hour-old UserPrompt | I |
| PC-66 | `AgentTaskWorktreeLockOutcomeTests` | `C443_ReceiptCommitRecoveryDoesNotRetype` | Reset QueueMessageId to null on receipt-save failure | Recovered confirmation uses the original queue row and prompt sequence; submitted body count remains exactly one | I |
| PC-67 | `AgentTaskWorktreeLockLifecycleTests` | `C443_ObservationFailureDoesNotChangeOutcome` | Let an injected observation-sink exception escape the added observation wrapper | Task still settles, stopper count unchanged and parent receives matching complete UserPrompt | I |
| PC-68 | `WorktreeDirectoryCleanupTests` | `C443_CreationDoesNotEnterCleanupCoordinator` | Route the creation rollback/recovery path through the cleanup coordinator | Cleanup coordinator calls == 0, diagnostic calls == 0 and scheduled retry delays == 0 during owned creation failure | B |

Execution rules: take an unmutated method baseline; apply only the named compiling
defect; build and run that exact method; require the specified assertion failure,
not build/fixture failure, timeout unrelated to the guard or zero tests. Restore
exact tracked bytes, refresh source timestamps or force rebuild, then run the
same exact method green. Keep per-PC source diff, SHA, TRX counts and assertion
text. If an existing assertion also fails at the base, establish it with the
exact method at that base before reporting an inherited failure. Do not weaken
assertions, add retries or widen deadlines. Controls sharing a production method
run sequentially; no savings from speculative parallel mutation is budgeted.

### Out of scope

- Historical PID attribution, automatic process termination, release barriers,
  MSBuild node-reuse changes, global build-server shutdown and elevated-service
  provisioning: the investigation did not establish a cause and D-1/D-5 exclude
  those changes. Real first-failure owner evidence remains a separate acceptance
  obligation; neither this document nor fake tests satisfy it.
- Repairing the existing build-junk script test or changing Windmill: not the
  CARD-0443 implementation. No script was run and no pre-existing red is claimed.
- Automatic cleanup of legacy/unregistered/opaque residue: now expressly refused
  by CARD-0448. The old Plan arm cannot be treated as an excluded test with an
  implementation still silently performing deletion; B1 must revise that arm.
- New session transports, authenticated provider/browser E2E, DB migrations and
  frontend changes: the payload uses the existing notification/queue contract.
  The fake-recipient substitute proves application delivery only; any claim
  about native transport requires the separately described native receipt lane.
- A hard ten-second whole-land deadline: synchronous filesystem calls are
  nonpreemptible and the Plan only bounds scheduled additional work. Tests
  explicitly cover an overrun returning before the coordinator settles.
- Exhaustive Cartesian multiplication of independent CSV formatting and every
  retry time: parser/path cases are deterministic; run their edge rows directly,
  then cross retry timing with every safety state change. Busy/idle is crossed
  with every pre-receipt handoff cut; post-receipt recovery deliberately has zero
  sends in either state. Both file and directory calibration failures are separate
  cases and separate PCs.

### Cost

**Estimated planning allowances, not measured run times or an accepted execution
floor.** B1/B2/B3 prevent an honest claim that all controls are executable.
The amended TestDesign must retain or replace every affected row and recompute
the floor before `next: code`.

| Work | Estimated minutes | Basis |
|---|---:|---|
| Code setup/build | 5 | Isolated output, initial compile, lazy PostgreSQL fixture startup |
| Code ordinary V/R: Unit lane and diagnostics/parser/budget cases | 6 | Unit filter plus new deterministic cases |
| Code ordinary V/R: receipt-backed cleanup/legacy/DI integration selections | 10 | V-4/V-10 and R-1/R-2/R-5 named filters |
| Code ordinary V/R: outcome/settlement/lifecycle selections | 8 | V-6/V-9 and R-3/R-4 |
| Code ordinary V/R: real queue delivery and all fault cuts | 10 | V-7/V-8/R-6, busy and eligible branches |
| Code ordinary V/R: explicit real Windows controls | 6 | V-5; record “not run” separately if prerequisites unavailable |
| **Code ordinary V/R floor** | **40** | Excludes the separate 5-minute setup/build |
| Mutation setup, baseline discovery/build and final evidence audit | 9 | 5 setup + 4 evidence/restoration audit |
| Mutation: 68 method-scoped red/restore/green cycles | 142.8 | Per PC: 0.75 red build + 0.25 red run + 0.10 restore/hash + 0.75 restored build + 0.25 green run = 2.10 |
| **Mutation PC floor** | **151.8** | 9 + 68 × 2.10; all 68 controls, including the 16 currently B-marked obligations |
| **Total verification planning floor** | **196.8** | 5 setup/build + 40 ordinary V/R + 151.8 Mutation |

These allowances exclude implementation/test-authoring, Plan rework and time to
obtain an explicitly authorized elevated identity. No elevation labor is hidden
inside a zero-minute setup. Code reports ordinary counts; Mutation reports all
PC/variant counts and each intended red/restore/green cycle. Refresh estimates
with measured TRX and build durations once the revised design exists.

Estimated selection saving: a deliberately conservative whole-class cycle budget
of 8 minutes versus 2.10 minutes per exact-method cycle saves
`68 × (8 - 2.10) = 401.2` minutes. This is a scheduling comparison, **not a measured
performance improvement**. Batching/parallel-run saving is budgeted at 0 because
most controls share provider/coordinator/delivery code and require independent
restoration. No full-assembly run is added: the touched graph is bounded by named
classes and the Unit lane; nightly owns the broad run.

Output custody: inventory all newly produced `bin-c443/` directories before the
first build and after each producing command. Before recursive deletion verify
each absolute path remains under this worktree and was created by this task;
await owned children and remove only that inventory with native PowerShell
`Remove-Item -LiteralPath`. Keep TRX/evidence outside any cleanup target.
This TestDesign task produced no build directories.

Handoff audit: bodies read; **guards=68, mapped=68, missing mappings=0,
duplicate PC mappings=0**. There are **16 B-marked controls with an unresolved
operation/evidence seam**. Consequently **all PCs executable = false** and the
Code-readiness gate is rejected. No application tests, builds, Handle queries,
lock reproductions or mutation cycles were run by this TestDesign task.
Return to **Plan** for B1/B2/B3, then to **TestDesign** to finalize the amended
executable matrix. Do not report `next: code` from this document.
