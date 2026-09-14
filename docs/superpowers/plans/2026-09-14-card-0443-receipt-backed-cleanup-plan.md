# CARD-0443: receipt-backed cleanup diagnostics and one guarded retry

Date: 2026-09-14. Stage: Plan amendment. Next: TestDesign (separate).
Baseline: `27e46463597eadb6872240df4d9ab699a1c27411`, fetched from
`origin/master` and reset onto the clean assigned task branch before inspection.

This is the authoritative implementation plan. It supersedes the implementation
sections of the [September 8 plan](2026-09-08-card-0443-worktree-lock-diagnostics-plan.md).
That document retains the original design and the September 14 TestDesign audit
as historical evidence. Its 68-PC matrix is **pending revision**, not permission
to implement the removed filesystem fallback or proceed to Code.

## Outcome and scope

On a failed receipt-backed publication cleanup, record the actual Git outcome,
capture handle-owner evidence or an honest availability limitation, and observe
native delete-sharing conflicts without deleting anything. Permit at most one
additional ordinary Git removal when that observation and fresh cleanup checks
allow it. Persist the capture before retry, and include it in the existing durable
land outcome even when cleanup subsequently succeeds or the worker restarts.

This does not promise recovery of every lock failure. Ordinary Git removal can
partially remove a checkout; if that makes it dirty, unregistered or unverifiable,
CARD-0448 requires retaining what remains. The historical blocking PID is still
unknown. Neither an observed handle nor an independent sharing conflict proves
which process caused Git's particular failure.

## Ground truth

| Card / old-plan assumption | Code at the baseline | Amendment |
|---|---|---|
| Retry `TryDeleteDirectory` after `git worktree remove --force`. | `WorktreeManager.TryRemoveAsync(string,...)` refuses with `typed_removal_authority_required`. Its typed overload checks the managed root and delegates to `GuardedWorktreeRemoval.RemoveAsync`. `TryDeleteDirectory` is gone. | Retry only the ordinary guarded Git operation; never introduce filesystem deletion or force. D-2. |
| An unregistered or empty leftover can be erased. | `GuardedWorktreeRemoval` refuses `unregistered_directory`; missing directory with registration is also refused. Ignored content, dirty source, uncertain identity and registration are preserved. | Preserve all those outcomes, including after partial removal. No janitor rescue arm. |
| A source SHA and old Landed event suffice. | `AuthorityAsync` checks the genuine common-directory lease, a fresh committed landing/task receipt, exact coordinates/SHAs, recovery pins and current remote containment. It runs again before deletion. `WorktreeRemovalEvidence.ReadAsync` uses a fresh scope/context. | Repeat the complete authority and content sequence before the additional command. A diagnostic record grants no removal authority. |
| Git's failure supplies Win32 32/33. | `LandingGitResult` contains exit code/output/generated diagnostic. `LandingGit.ExecuteAsync` deliberately suppresses raw stderr; Git's internal native deletion error is unavailable. | Record Git and independent `CreateFileW` observations as different typed facts. No message parsing or fabricated native Git error. D-3. |
| A successful Git exit means clean. | Removal checks directory and registration absence, then refreshes authority/registrations before an exact old-SHA `update-ref --no-deref -d`. Failures preserve component residue. | Keep postconditions and branch checks; never retry the branch delete automatically. |
| Only direct children are tracked. | `LandingGit.ExecuteAsync` writes `RepositoryChildJournal` around mutating commands and waits for exit plus stream drain. Verification has `ILandingChildObserver` and external artifacts. | Preserve this custody path for both Git attempts and the existing verifier. D-5. |
| A 950-character residue fits the landing row. | `AppDbContext` limits `AgentTaskLanding.LastReason` to **400** characters; `StageOutcome.DetailMaxLength` is **1,000**. | Keep the reason code separate from diagnostic evidence and display summary. Do not put a 950-character capture in `LastReason`. D-4. |
| Rolling logs deliver and retain the capture. | `CleanupAsync` copies component flags and `removed.Residue` into the operation; terminal cleanup state is committed later with the event/request/notification. Logs have no such transaction. | Add a committed cleanup-attempt/capture journal; consume it in the current terminal transaction and notification payload. |
| A terminal event is caller receipt. | `CompleteTerminalLockedAsync` adds an immutable `AgentTaskLandNotification`; `SourceLandNotificationId` deduplicates the queue. `AgentTaskLandNotificationService` confirms a complete matching UserPrompt after its attempt floor. | Keep request/event/notification/queue identities and actual recipient verification. No separate diagnostic message. |
| A re-land republishes or resets history. | A published operation runs `CleanupRetry`; later terminal publication types become `LandingCleanup`. | Keep the operation and publication proof. A new explicit request gets a new bounded cleanup attempt, preserving earlier captures. |
| Age-based cleanup can erase old residue. | `PruneStaleAsync` and the residue sweep retain legacy/unknown work; old event prose is not authority. | No janitor, Windmill, ignored-content policy or legacy cleanup changes. |

Inspected owners: `docs/project-context.md`, `docs/orchestration-loop.md`
(CARD-0448 and caller-receipt contracts), `docs/agent-card-lifecycle.md`, and
`docs/testing-and-build.md`. The [investigation](../../investigations/2026-09-08-card-0443-worktree-locks.md)
remains evidence of failed historical Handle controls, not evidence of a blocker.

## Decisions

### D-1: retain the explicit diagnostic availability default

Use `IWorktreeLockDiagnostics` (Application external-I/O seam), a typed
`WorktreeLockSnapshot`, and an Infrastructure Windows Handle provider. Configure
a trusted absolute executable through typed settings; no PATH discovery, shell,
versioned WindowsApps hardcoding, interactive setup or elevation. Handle runs only
under an already authorized elevated invocation identity with installation/license
setup complete. Other identities/platforms return explicit unavailability. Native
probe availability in D-3 is independent of Handle availability.

The fixed Handle arguments remain `-nobanner`, `-v`, and the validated root without
a trailing separator. The target query precedes a single calibration query against
an owned temporary directory outside all worktrees. Hold both a known file without
delete sharing and a directory handle during calibration; require both paths and
the expected PID. Release owned controls in `finally`. No close-handle switches.
The executable cwd and all evidence/control files remain outside the target.

The provider's queries, stream reads and identity enrichment share five seconds,
256 KiB combined output, 32 parsed owners, and the capture's overall 32 KiB UTF-8
serialized limit. Await owned diagnostic children/readers before return. Parse
pinned actual CSV fixtures with quoted fields; reject unknown schemas. Match the
exact normalized root (`.`) or descendants at a separator boundary, never sibling
prefixes. Unresolved device paths, truncation and inaccessible identity enrichment
are partial evidence. Preserve positive owners when coverage is partial.

Statuses remain `OwnersObserved`, `NoOwnersObserved`, `Partial`, `Unavailable`,
`TimedOut`, `Failed`, `PathGone`, with stable reasons for unsupported platform,
missing configuration/tool, insufficient privileges, failed controls and malformed
output. Only complete valid output and successful controls permit
`NoOwnersObserved`, displayed as “no matching handles observed at <UTC>”. Preserve
cancellation. Do not turn provider failure into a publication refusal.

Reason: owner evidence, including empty-root handles, remains valuable while
unavailability is an authorized default. Reject automatic server elevation and
Restart Manager as the sole substitute: it does not cover the required directory
case. See [Handle](https://learn.microsoft.com/en-us/sysinternals/downloads/handle)
and [RmGetList](https://learn.microsoft.com/en-us/windows/win32/api/restartmanager/nf-restartmanager-rmgetlist).

### D-2: B1 resolved by one fully guarded ordinary Git retry

The only additional destructive command is:

```text
git worktree remove -- <validated recorded worktree path>
```

Add a concrete coordinator around the publication directory-removal portion of
`GuardedWorktreeRemoval`. Extract/reuse the existing pre-removal checks without
weakening or bypassing any. `WorktreeManager` remains the managed-root entry point;
the coordinator also revalidates the recorded canonical root and Git identities
before the second command. Do not recursively call `RemoveAsync`, which would
reset the allowance or accidentally repeat ref deletion.

The feature applies to `Purpose.Publication` invoked by the landing protocol with
its durable request/cleanup-attempt context. LocalMerge, Verification, failed-add
rollback/recovery and raw legacy APIs keep their existing behavior. A low-level
publication caller without that context retains its existing one-pass behavior;
it cannot request an automatic retry or manufacture a journal identity. Test the
protocol wiring so a missing context cannot silently disable its required capture.

The sequence, under the same genuine repository mutation lease, is:

1. Commit the D-4 attempt identity. Run the existing authority, registration,
   source inspection, ignored-content check, authority refresh, second source
   inspection/ignored-content check. Durably consume the initial command slot,
   then perform the final fresh authority read immediately before Git. Start through
   `ILandingGit.RunAsync`, preserving its standing child journal.
2. If the completed command errors/times out or its postcondition leaves residue,
   commit the first failure marker, run the one D-1/D-3 capture, and commit its
   bounded evidence. Capture is after Git has returned and its owned I/O has
   drained; no instrumentation inside Git is claimed. A disappeared root is
   `PathGone`. A timeout/inspection error is recorded but does not enable retry.
3. Only a normally exited, nonzero first Git command plus a positive D-3 observation
   can nominate a retry. Handle owner names, an English error, exit 128 alone,
   error 5/145, unavailable evidence, a successful-but-incomplete removal, and an
   unregistered root do not nominate one. Commit capture successfully before any
   further destructive action; otherwise retain residue with an evidence-storage
   limitation.
4. Wait once for 250 ms using `TimeProvider`. Repeat **all** initial checks,
   including fresh committed task/receipt state and remote containment, both
   source/content boundaries and the final authority read. Recheck the same lease,
   managed/canonical path, common/Git directories, exact source/target SHAs,
   symbolic branch, registrations/intentional lock/prunable flags, sequencer,
   dirty/untracked/ignored content, and recovery pins. Unknown is refusal. Commit
   consumption of the additional slot, then perform the final authority read and
   start the same ordinary Git command. No cached inspection authorizes it.
5. After success, retain the existing directory/registration postconditions and
   exact-SHA branch cleanup sequence. Never auto-retry branch deletion, prune,
   repair registrations, clear attributes, reset/checkout missing files, or delete
   an unregistered directory. Persist the final independent component facts.

Maximum: **two ordinary Git removals per land request**, initial plus one additional
command. There are zero filesystem deletion passes. A persistent conflict consumes
at most the two slots; a partial removal usually fails the new inspection before
the second command. A new Git lock or replaced receipt likewise stops it.

Use one monotonic ten-second additional-work allowance starting when the first
failure is observed. The failure checkpoint, capture, save, delay and retry
preflight/command all consume it. Pass a linked remaining-budget token to every
extra asynchronous operation, including Git reads/remote refresh and the second
remove; never grant another five-minute command allowance. Do not start work at
or after expiry. Initial removal retains its baseline timeout. Final evidence
settlement is required even when the extra-work allowance is spent.

This bounds scheduled work, not a hard whole-land deadline: synchronous native
calls and the existing Git exit/stream-drain join can overrun. Await them; never
abandon a background deletion/child to meet the clock. Budget cancellation stops
the additional attempt and produces cleanup residue; outer cancellation remains
cancellation. Preserve the existing owned-child teardown and uncertain-journal
admission fence, including across restart.

Reason: repeating an ordinary removal after new proof has no broader filesystem
authority than its first attempt. One retry limits lease occupancy and new process
work. Rejected alternatives: restoring recursive fallback, using `--force`, treating
missing tracked files as owned disposable output, retrying four filesystem passes,
or blindly repeating a five-minute Git command. Diagnostics-only is safe but drops
the permitted opportunity to recover an intact, transiently locked checkout.

### D-3: B2 resolved by a typed, nonmutating native observation

Add `IWorktreeDeleteAccessProbe` and a Windows implementation independent of Handle.
Its result is `WorktreeNativeObservation`: operation `DeleteAccessOpen`, relative
path, observation UTC, success/failure, and nullable native error code. Immediately
read the P/Invoke last error on failed `CreateFileW`; do not infer it from exception
text, Git output, or low bits of an arbitrary HResult.

Open existing paths using `DELETE` access, share READ/WRITE/DELETE,
`OPEN_EXISTING`, `FILE_FLAG_BACKUP_SEMANTICS` and `FILE_FLAG_OPEN_REPARSE_POINT`.
Use noninheritable safe handles and close each successful open immediately.
No `DELETE_ON_CLOSE`, disposition setter, rename, attribute mutation, content
write or delete API is permitted. This requests delete access without performing
deletion. Incompatible sharing can produce a typed error; it does **not** recover
Git's internal errno. This is the design inference from Microsoft's
[CreateFileW contract](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew).

After the Handle provider returns (including an unavailable return), probe the
root first and then at most 64 existing entries below it, within two seconds and the shared ten-second
allowance. Use bounded enumeration without reading file contents or following
reparse points; prioritize validated paths already observed by Handle, then
remaining entries. Count candidate/enumeration work as well as opens toward the
bound. Revalidate root/path components before and after each failed open; validate
the final handle path on successful opens. Discard any aliased, escaped, replaced
or unverifiable observation from retry nomination. Do not probe Git admin storage or
outside-root paths. Cap/cancellation/access limitations yield partial/unavailable
coverage, not a claim that every lock was inspected. A transient conflict missed
by the bounded walk simply receives no automatic retry.

Native error 32 or 33 from this exact probe operation nominates the D-2 guarded
retry; other errors and successful opens do not. A positive observation remains
valid evidence even when the remaining scan is partial, but is never deletion
authority. Preserve code 5, 145, unknown, missing-path and unsupported outcomes
as distinct nonretryable observations. Error definitions:
[Windows system errors](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-).

Store `GitFailure { operation, exitCode?, generatedCode, exceptionType?, at }`
separately from native observations and Handle owner snapshots. Retry nomination
means “a delete-access sharing conflict was observed after Git failed”, not
“Git returned Win32 32”. Code 33 is a supported classification, not a promise that
the chosen open operation normally produces it; its deterministic case cannot
substitute for a real error-32 production-path control. Successful opens do not
establish that removal would succeed or that the target has no blockers.

Reason: the operating-system producer is concrete and independently testable,
including under an unelevated identity. Rejected alternatives: fabricating a
`LandingGitResult.Win32Code`, localized stderr matching, Handle owners as error
codes, and a destructive deletion probe. No privileges are enabled or provisioned.

### D-4: B3 resolved by committed capture evidence and the existing notification

Add a plain Domain `WorktreeCleanupAttempt` entity and an Infrastructure
`IWorktreeCleanupJournal` implementation using independent scopes/EF contexts.
The Application interface is an external persistence seam, not a second authority
reader. Keep `IWorktreeRemovalEvidence.ReadAsync` and its fresh contexts unchanged.
Generate the migration with the EF CLI; no hand-written migration.

One row per `AgentTaskLandRequest.Id`, with a generated attempt/capture ID, schema
version, operation/task/request IDs, immutable source coordinates and deletion SHA,
created time, initial/retry command-slot intent and completion times, first Git
failure, separate last Git outcome, capture state/time, bounded sanitized capture JSON, retry decision/reason,
final component facts, and optional terminal event ID. Unique RequestId and a
concurrency token make recovery idempotent; foreign keys retain evidence with its
request/operation. Capture state distinguishes NotNeeded, Pending, Captured and
Interrupted; provider availability remains a separate status inside the capture.
Persisted first-failure/capture fields are write-once, while final disposition is
completed transactionally. Bound the entire UTF-8 capture JSON to 32 KiB and its
display summary to 600 characters. Retention follows the parent landing history;
this card adds no independent pruning job or per-worktree evidence files.

`AgentTaskLandingProtocol.CleanupAsync` obtains/reuses the row for its **current**
land request (not the operation's original approval request), passes its identity
through an optional typed context on `WorktreeRemovalRequest`, and receives a
separate diagnostic reference on `WorktreeRemoval`. The coordinator durably writes
intent immediately before each command, first failure before collection, and
capture before the optional retry. A failed write before launch grants no command
slot. Unknown commit acknowledgement is resolved by a fresh keyed read; never
start from an assumed save. Read/write errors retain residue and cannot manufacture
an observation or authorize more cleanup.

Keep `WorktreeRemoval.Residue` / `op.LastReason` as the bounded baseline cleanup
reason (at most 400 characters). A clean removal retains `Residue == null` and
`LastReason == null`; diagnostic evidence must not change `IsClean`. Do not widen
or overload LastReason. The typed reference and journal are the other channel.

Before terminal settlement, load the current attempt and applicable capture from
the journal in a fresh read. `AgentTaskLandService` composes diagnostic detail into
the Cleanup stage and `FormatOutcome` separately from LastReason. A complete stage
detail is at most 950 characters, with up to 600 for diagnostics; reserve the
capture ID/time and first owner name/PID or explicit unavailable reason before
clipping optional display text. Preserve the Git reason, probe provenance and
component facts. Record omitted counts/explicit truncation. Full bounded sanitized
evidence remains in the journal. A clean-after-retry outcome says cleanup Complete
and carries the first-failure capture; it does not become `LandedWithResidue`.

Extend `LandingEvidenceDto` and `AgentTaskService`'s existing task-detail projection with the current
cleanup-attempt identity and latest bounded capture for that operation. This makes
the evidence queryable without log access or a new endpoint/UI. For a later explicit
cleanup request that succeeds without collecting again, retain the earlier capture
and label it “prior attempt”, with its original request/attempt/time. Never relabel
old owners as current or rewrite an already committed notification.

In `CompleteTerminalLockedAsync`, complete the attempt's final disposition/event
link **in the same transaction** as operation cleanup state, stage rows, terminal
event, completed request, pending flags and `AgentTaskLandNotification`. Keep
`LandNotificationPayload.Create` as the sole Outcome producer and retain its
immutable body/destination/content digest. Repeated cleanup still yields
`LandingCleanup` on the same published operation, without push or source reapproval.
Publication SHAs and confirmation remain unchanged by diagnostic failure.

Sanitized structured logs are secondary copies keyed by capture, request, operation
and task. Include tool version/identity, duration, controls, bounded owner
name/PID/path, process creation identity and available ancestry; omit raw stdout,
stderr, commands/environments, usernames and unrelated handles. Log failures cannot
affect cleanup or settlement. Storage failure is explicitly different: it prevents
the optional retry until capture is committed. Already completed deletion remains
completed even if subsequent storage is temporarily unavailable; recovery must
reconcile it rather than invent failure or repeat publication.

#### Crash and delivery contract

| Boundary | Required recovery / durable evidence |
|---|---|
| Before attempt intent commits | No removal started by this feature. Reload the unique request row; a new worker may run its unconsumed initial slot after all baseline checks. |
| After a command slot is consumed, before result/capture commits | The slot remains spent, including death before actual launch. Standing child journals retain existing admission authority. No automatic replay of that directory-removal slot on this request; after admission is safe, inspect postconditions. If residue remains, finish the request with interrupted evidence; an explicit new cleanup request can try again. |
| Git returned but failure/capture was not committed | Preserve the durable intent; mark the unrecorded observation Interrupted. Do not invent lost native codes/PIDs or claim that rolling logs committed them. No restart replenishes the optional retry. |
| First failure saved; provider query incomplete | Retain first Git error/time and the Pending capture identity. Recovery marks capture Interrupted and reports that limitation; it does not rerun the old observation and label it first failure. |
| Capture committed, before retry/terminal commit | Reload the same capture. The automatic retry is live-invocation-only; restart closes that allowance. Reconcile actual component state under fresh authority. If tree and registration are gone, existing receipt-backed branch completion is still allowed. Otherwise retain residue for an explicit new request. |
| Removal completed, before terminal commit | Recheck directory/registration/ref facts and preserve the committed capture, including clean recovery. Commit the result with the same request and one terminal notification; no second publication. |
| Terminal committed, before publish/wakeup | The existing independent notification/queue recovery delivers the same immutable Outcome. No additional diagnostic event/message obligation is created. |
| Queue insert committed, acknowledgement lost | Reuse the unique `SourceLandNotificationId` row, original queue/body/digest/destination. |
| Busy / already eligible recipient | Use existing WhenIdle queue and completion flush/recovery. Busy waits for eligibility; eligible needs no future TurnEnd. |
| Submitted prompt / transcript / receipt-save cuts | Recover against the actual complete matching UserPrompt in the original session after the attempt sequence/time floor. Sent, queue insertion, screen output and a log are insufficient. A persisted receipt must not cause a second submission. |

Reconciliation after an interrupted command may query state and perform the existing
guarded branch-only completion when both directory and registration are absent;
it never spends a third directory-removal slot. This restriction deliberately makes
uncertain recovery more conservative. The original operation/pins remain intact.

Reason: a committed DB capture survives the relevant worker/process failure cuts
and reaches the established delivery obligation. Reject rolling logs as the sole
store, saving only at final failure, stuffing capture JSON into LastReason, changing
notification identity on retry, and treating a notification row as recipient receipt.
The unavoidable pre-commit observation-loss window is explicit, not presented as
perfect capture across abrupt machine failure.

### D-5: preserve lifecycle and custody behavior

Retain narrow Worktree-only completion/release observations around the actual
stopper call/catch in `AgentTaskReplyService`; preserve persist, enqueue/deliver,
publish, then release order. Stop return is not OS-exit proof. Add no stopper calls,
wait barriers, process census, owner termination, build-server shutdown or changed
MSBuild reuse policy. Failure of an observation sink cannot change behavior.

For landing verification, add correlation to the existing `ILandingChildObserver`
and `RunProcessAsync` path, preserving before-start intent, started identity, exit
and I/O-drain ordering. Reuse actual child PID/OS creation time; skipped verification
has no child. Do not replace the standing journal with logs. Join observed owners
to lifecycle evidence only with compatible process creation identities/ancestry;
exited/reused parents or shared build servers remain unknown attribution.

Reason: CARD-0319 ordering and CARD-0448 child custody are established safeguards.
A historical hypothesis is insufficient reason to change release or kill behavior.

## Implementation slices

Names for new files are proposed. Implement only after the separate TestDesign
amends its executable coverage and PC matrix. Each slice names ordinary evidence;
none below claims the tests exist or have passed.

| Slice | Files / change | Tests to add or extend |
|---|---|---|
| S1: durable capture model and journal | Add `server/Domain/Entities/WorktreeCleanupAttempt.cs`, `server/Application/Interfaces/IWorktreeCleanupJournal.cs`, `server/Application/Dtos/WorktreeCleanupDiagnostics.cs`, `server/Infrastructure/Data/WorktreeCleanupJournal.cs`; update `AppDbContext.cs`; CLI-generated `server/Migrations/*AddWorktreeCleanupAttempts*` and snapshot. Implement keyed immutable capture, slot accounting and bounded storage. | New `Application/WorktreeCleanupJournalTests.cs`: unique current-request binding, immutable first capture, unknown commit acknowledgement, restart/spent slots, size boundaries and terminal rollback with isolated PostgreSQL schemas. |
| S2: concrete observation producers | Add `Application/Interfaces/IWorktreeLockDiagnostics.cs`, `IWorktreeDeleteAccessProbe.cs`; Infrastructure/Git Handle parser/provider and `WindowsWorktreeDeleteAccessProbe.cs`; typed settings/validator. Follow D-1/D-3 bounds and fixed nonmutating calls. | New `Infrastructure/WorktreeLockDiagnosticsTests.cs`, `WorktreeDeleteAccessProbeTests.cs`, `WorktreeLockDiagnosticsWindowsTests.cs`; real owned tracked-file, empty-root CWD, explicit directory handle and delete-sharing controls. Native probe tests do not require Handle elevation. |
| S3: guarded retry and typed result | Update `Infrastructure/Git/GuardedWorktreeRemoval.cs`, `WorktreeManager.cs`, `Application/Dtos/WorktreeRemovalRequest.cs`, `WorktreeRemoval.cs`; add concrete `WorktreeGuardedCleanup.cs`. Preserve `LandingGit` command/journal path; pass remaining-budget tokens, no alternate deleter. | New `Infrastructure/WorktreeGuardedCleanupTests.cs`; extend `Application/AgentTaskLandRemovalMatrixTests.cs` using real receipt and command barriers. Cross every preflight guard after capture and delay; two-slot cap, partial removal refusal and no automatic retry for nonpublication purposes. |
| S4: current protocol and durable caller outcome | Update `Application/Services/AgentTaskLandingProtocol.cs`, `AgentTaskLandService.cs`, `AgentTaskService.cs`, `Application/Dtos/LandingEvidenceDto.cs`. Compose evidence separately from LastReason; finalize attempt/event/notification atomically. Notification service/queue semantics remain the baseline. | New `Application/AgentTaskWorktreeLockOutcomeTests.cs`; extend `AgentTaskLandNotificationPersistenceTests`, `AgentTaskLandPersistenceFailureTests`, `AgentTaskLandNotificationRecoveryTests`, `AgentTaskLandReceiptTests` for new capture cuts plus real producer-to-recipient busy/eligible chains. |
| S5: lifecycle correlation and complete wiring | Update `Application/Services/AgentTaskReplyService.cs`, existing verifier observation path in `AgentTaskLandService.cs`, `server/Program.cs`, `tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs` (both graph builders) and affected harness constructors. Singletons use journal scope factories, not captured DbContexts; tests preserve TryAdd overrides. Update `docs/orchestration-loop.md` and `docs/testing-and-build.md` with delivered behavior/explicit Windows lane. | New `Application/AgentTaskWorktreeLockLifecycleTests.cs`; existing `AgentTaskSettlementRaceTests`, `AgentTaskLandStageOutcomeTests`, `DelegationTestServicesTests`, `DelegationHarnessCensusTests`, raw-removal/legacy sweep tests. Preserve verifier observer faults/journals and zero extra stop calls. |

## TestDesign handoff and disposition of the previous matrix

The old test bodies/fixtures reviewed by TestDesign remain useful. This amendment
also read `AgentTaskLandRemovalMatrixTests` and the current production removal,
Git, protocol, terminal, notification, payload, persistence limits and DI helpers.
In particular, its held-file row reaches **ordinary receipt-backed Git removal**;
the old raw `WorktreeManagerGitIntegrationTests` held-file refusal is not a substitute.

| Previous obligation | Required amendment / executable seam |
|---|---|
| B1, PC-10..16 and PC-68 | Replace proposed `WorktreeDirectoryCleanupTests` with `WorktreeGuardedCleanupTests` over S3 plus the real `LandingSafetyHarness`/`LandingGitFixture`. Trace checkpoint/capture before the additional ordinary command. Preserve initial/new Git-lock and every fresh authority/content refusal, and exclude create/LocalMerge/Verification paths. |
| B2, PC-19 and PC-40 | Target the real D-3 probe, separate Git/native fields and nomination predicate. Include actual error 32 under an owned child, unknown/5/145/non-Windows negatives, path replacement/escape/reparse/scan caps, probe success with nondeletable target, and deterministic 33 without claiming it was measured. |
| PC-20/21 | Replace four filesystem passes/three delays with **two Git slots and one 250 ms delay**. Test initial slot plus additional slot accounting, no delay after clean, no implicit reset on restart and no retry of a successful-but-incomplete remove. |
| PC-22..24 | Retain the shared ten-second boundary, now including journal saves/probe/revalidation and retry Git. Test child/drain completion before return, spent-slot recovery and remaining-budget propagation rather than an invented synchronous deleter. |
| B3, PC-41/43..46 and PC-55..66 | Test LastReason's 400-character boundary separately from 600/950-character summary limits; committed capture after clean recovery; current-request versus approval-request identity; old capture provenance; and the same terminal/queue/recipient chain. Add every D-4 pre-capture/commit cut to the old delivery matrix. |
| PC-1..9,25..39,42,47..54,67 | Retain provider/identity/parser, cancellation/custody, privacy, component, lifecycle and DI guards; retarget changed budgets/DTOs/wiring as necessary. Existing guard count is not an execution count. |

Add distinct positive controls for each new journal immutability/slot/provenance/
atomicity guard and native-probe nonmutation/confinement/last-error guard; do not
force the new inventory to remain 68. Replace all 16 old B-marked controls with
executable methods/defects or explicit revised obligations, and recompute cost
from the resulting matrix. The historical 196.8-minute planning estimate is stale.

Real Windows qualification must distinguish two outcomes: a held tracked-file or
empty-root removal may leave dirty/unregistered residue that **must remain**, even
after releasing the holder; a transient positive case must demonstrate that the
checkout and registration actually remained intact before the fully guarded retry.
Do not restore missing files in the fixture between failure and retry to fake that
condition. Deterministic I/O barriers can establish retry behavior, but the report
must name their limit. No claim of common-case recovery without a real intact-tree
case. Native/Handle controls are performed only on owned temporary repositories and
children with readiness/release handshakes and the assembly-local process limiter.

For delivery, combine the real landing producer and terminal transaction with the
real notification worker, queue and completion flush. `BridgeQueueHarness` may
supply the receiving adapter, using its actually submitted complete body and the
same isolated schema. Read the UserPrompt and confirming sequence through a fresh
context. Manually copying a notification into a transcript, queue insertion alone,
log capture, or `ReplyTo.None` cannot satisfy producer-to-recipient coverage.

TestDesign must name ordinary filters/count expectations, method-scoped compiling
PC defects, fresh TRX evidence and all substitutions. Follow the current testing
owner: TUnit via `dotnet run`, isolated `OutputPath=bin-c443/`, sequential process
test assemblies, awaited children and verified task-owned output cleanup. No
elevated test silently passes when its declared prerequisites are missing.

## Rollout and acceptance

Ship the journal migration, provider/probe, guarded coordinator, protocol projection,
terminal formatting and DI wiring together after ordinary verification and Review.
Existing rows without captures remain valid; do not backfill owners, native codes,
cleanup authority or caller receipts from historical event prose. Missing Handle
configuration continues to produce useful unavailable evidence only on failure;
healthy cleanup does not invoke Handle or the native probe.

No human decision is required for these engineering defaults; the required next
stage is **test-design**. That stage must complete the revised verification design
before Code. No runtime change, operational configuration, application build/test,
Handle execution, lock reproduction or mutation cycle was performed by this Plan
amendment. Validation is source/contract inspection plus documentation checks.

Automatic owner identification remains separately unverified until the real
file/root/CWD controls pass under the recorded authorized identity/tool and actual
first-failure evidence is obtained. The fallback, typed probe and simulated retry
results cannot close the original historical cause/owner-identification acceptance.

## Verification design

TestDesign amendment, 2026-09-14, inspected at `9e3e95d2`. This appendix
supersedes the September 8 verification design, including its 16 B-marked
controls and 196.8-minute estimate. D-1 through D-5 above are unchanged.
This is an implementation-ready test specification, not executed test or
mutation evidence. All new test methods below must be implemented by Code.
There is no remaining design dependency on a filesystem deleter or a fabricated
native Git error.

### Inspection

Paths in this table are under `tests/Antiphon.Tests/` unless prefixed
`server/`. "Read" means the bodies and the named supporting code, not just
discovered names.

| Test/fixture bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| Complete `Application/AgentTaskLandRemovalMatrixTests.cs`: C448_V18, V36, V20, ForgedLease | V-4/V-5, R-1/R-2. Two content reads; 20 authority-coordinate cases; real held tracked file, command timeout/error, new checkout/ref movement and postcondition error. Its raw request has no new cleanup context, so retain its one-pass assertions. |
| Complete `TestHelpers/LandingSafetyHarness.cs`, `LandingGitFixture.cs`, `ScratchGitRepo.cs` | V-4..V-9. Real isolated repositories/remotes, command barriers, fresh-context authority observation, restart graph, SaveChanges/transaction interception and OS worker entry. Seed defaults to ReplyTo.None; explicitly change before RequestAsync for delivery. FixtureGit.BeforeCommand can return without starting Git: simulated failures do not prove a native failure or child custody. |
| `Infrastructure/WorktreeManagerTests.cs`: held-file body, BuildManager, MutableTimeProvider and GitTestEnvironment; complete `WorktreeRemovalDefaultTests.cs`, `WorktreeRemovalAuthorityTests.cs` | R-1. Raw held-file test proves typed-authority refusal only; its untracked held.bin never reaches ordinary removal. UTC-only MutableTimeProvider cannot exercise timers. Build-junk-script policy test excluded because that script policy was reverted independently. |
| `Application/WorktreeResidueSweepTests.cs`: execute_never_treats_legacy_landed_event_as_cleanup_authority; ScratchGitRepo helper above | R-1. Legacy Landed prose and age grant no cleanup authority. No sweep policy changes. |
| Complete `Application/AgentTaskLandReceiptTests.cs`, `AgentTaskLandNotificationRecoveryTests.cs`, `AgentTaskLandNotificationPersistenceTests.cs`; `AgentTaskLandPersistenceFailureTests.C467_V06_AtomicSettlementFaultMatrix` | V-6..V-8, R-3/R-6. Transaction cuts, keyed queue race, destination snapshot, receipt-save recovery, complete/flattened/false receipt variants. Seeded notes/prompts are matcher substitutes, not producer delivery acceptance. |
| `Application/AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries`, including owned worker launch/kill/restart/finally; `Infrastructure/RepositoryMutationLeaseTests.C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput`, C448_C24_KilledWorkerLeavesLiveGitChildFenced, RunChildAsync | V-5/V-6, R-2. Actual worker death, inherited readers, native identity and standing admission journal. Extend owned entry/barriers; do not treat process-root death as descendant proof. |
| Complete `TestHelpers/BridgeQueueHarness.cs`, `QueuedReceiptAssertions.cs`; `Agents/FakeAgentProtocolAdapter.SendInputAsync`, SubmittedBodies and OnSubmitted | V-7/V-8/V-9. Real queue/runtime graph; recipient callback records only actually submitted composer text. Helper's direct FlushIfIdleAsync is insufficient to test lost-wakeup worker recovery: use hosted workers in the new chain. No fake runner launch. |
| `Application/AgentTaskLandStageOutcomeTests`: reland_of_an_already_landed_task_runs_cleanup_only, CreateLand, SeedSucceededWorktreeAsync, RequestHeadAsync, SeedBuildableAsync, row/assertion and temporary helpers | V-9, R-3. Cleanup-only repeat, independent graph construction and real verifier buildable fixture. Null message queue/ReplyTo.None cannot prove receipt. |
| `Application/AgentTaskSettlementRaceTests`: worktree_pool_settle_delivers_parent_note_when_kill_savechanges_races_retire, BuildHarness, FlushingSessionStopper, Shared/Worktree/session seed helpers | V-9, R-4. Fresh stopper scope/retirement race; assertion ends at insertion. Port its setup to the isolated bridge store and add recipient evidence. |
| Complete `TestHelpers/DelegationTestServices.cs`, `DelegationTestServicesTests.cs`, `DelegationHarnessCensusTests.cs`, `TestDbFixture.cs`, `ProcessSpawnLimit.cs` | V-6/V-10, R-5. Both service-collection and direct graph builders, TryAdd, singleton scopes and assembly-local child limiter. IsolatedTestSchema currently clones a database; it is not a SearchPath schema. |
| `server/Infrastructure/Git/GuardedWorktreeRemoval.cs` complete; WorktreeManager raw/typed removal; LandingGit.ExecuteAsync; WorktreeRemovalEvidence.ReadAsync; AgentTaskLandingState publication/identity checks | V-4/V-5, R-2. Exact ordinary Git/old-SHA deletion, repeated committed authority and standing child journal, suppressed raw stderr. No actual native Git-error producer exists. |
| `server/Application/Services/AgentTaskLandingProtocol.CleanupAsync`, AgentTaskLandService terminal/formatting/verifier path, AgentTaskReplyService persist/deliver/release; complete AgentTaskLandNotificationService, CompletionNoteWork, CompletionNoteWorkHostedService and AgentTaskLandNotificationHostedService | V-6..V-9. Durable terminal producer, separate periodic notification and pending-queue scans, complete receipt predicate and released repository lease. The pending-queue scan, not merely the notification scan, repairs a lost completion wakeup. |
| Project-context, orchestration-loop CARD-0448/CARD-0467/CARD-0478 contracts, testing-and-build, session runtime owners; authoritative amendment and historical verification/investigation | All V/R. Code -> ordinary Review -> land -> SourceLanding Mutation. Historical failed Handle controls do not identify a blocker. |

New files use these nearest fixtures: diagnostics/native/guarded cleanup use
LandingGitFixture and the owned child patterns above; journal/outcome use
LandingSafetyHarness plus TestDbFixture and BridgeQueueHarness; lifecycle uses
the stage-outcome and settlement-race harnesses. No test source was changed here.

Required setup to implement, with no unresolved design choice:

- Add `TestHelpers/WorktreeLockChild.cs`: a locally inherited, owned helper with
  tracked-file without delete sharing, root CWD, explicit directory-handle and
  delete-sharing modes. Use readiness/release pipe handshakes with PID and OS
  creation ticks. Child scripts/control files/cwd/evidence live outside the target.
  Dispose all owned handles and await child exit plus both readers in finally.
  Replace fixture sleep-based unlock timing with handshakes.
- Add recording external-I/O implementations for configured executable/token,
  process creation/streams/identity, native open/last-error/path/entry enumeration,
  and journal commit boundaries. Keep the coordinator concrete. Fakes must execute
  the same production parsing, classification, preflight and journal logic.
  Dangerous PC argument/flag/stop defects go only to recording native/process I/O;
  never pass them to real Handle or an unrelated process.
- Use FakeTimeProvider for monotonic/timer tests with awaited barriers. Keep
  TimeProvider.System in the recipient queue graph; its polling must advance.
  Independent first-failure, five-second Handle, two-second probe and ten-second
  additional-work clocks share the required deadlines without freezing delivery.
- Extend SaveFault with keyed attempt/slot/failure/capture/final-disposition
  save and commit barriers. Install interceptors in every independent journal
  context, not just the protocol's context. Resolve uncertain acknowledgements
  with fresh keyed reads. Generate migration and any migration PC with the EF CLI.
- Create the bridge with `ConnectionString = h.Schema.ConnectionString`,
  AlwaysOn=false, and bind its session to the landing task before RequestAsync.
  Keep the landing graph's real WorktreeManager. Use bridge only for the receiving
  graph; its NoWorktreeManager must not replace producer removal.
  Register both real hosted workers, the same CompletionNoteFlushQueue, and
  SpecialistFailureQueue; stop and await both hosted workers on teardown.
- Extend the existing locally inherited crash worker to use the same parent-owned
  cloned database and fixture repository; it never disposes the parent's store.
  Durability tests terminate only this worker after a reached-and-acknowledged cut,
  then await it and its readers. Preserve actual child-journal admission on cuts
  with live Git; exercise safe recovery only after all owned descendants exit.
- Pin sanitized actual Handle CSV fixtures with tool version/hash/schema and
  acquisition identity. None exists in the inspected test area. Author synthetic
  malformed/schema/quoted-field cases separately; they cannot qualify a binary.
  Real Windows Handle qualification requires configured trusted absolute tool,
  completed license setup and an already authorized elevated invocation. A
  requested qualification lane fails clearly if these prerequisites are absent;
  do not provision elevation or call a skip a passing qualification.

Boundary combinations are deliberate. Every retry test first proves initial
authority passed and the intended first nonzero command/capture occurred. All
other guards remain valid until the designated barrier. Each independently
skippable first/second content/ignored/final-authority call has its own PC.
The 36 `C443_Authority_...` methods each have two explicit TUnit arguments,
Initial and Retry, and vary only their named predicate at that preflight.
A boundary trace records the next external I/O call after that guard: refusal
must occur before it. Thus a later redundant refusal cannot mask removal of
the earlier guard. PC red must be at this assertion or its stronger preserved
bytes/zero-command assertion, never at fixture setup/null dereference.

For matches that require precise earlier state, use a transparent ILandingGit
decorator to supply only that read's mismatched snapshot while forwarding real
lease, committed receipt, pins and all other reads; label this substitute in
evidence. Data/code must not "repair" a real partially removed tree. Cross
staged/unstaged/untracked/submodule changes and ignored .antiphon/.claude/bin-private
sentinels at both initial and both retry content boundaries. Inspect accepted=false
separately for detached/switched branch, duplicate/locked/prunable registration,
active sequencer and dirty contents. Existing InspectAsync internals retain their
CARD-0448 inventory; the accepted-result guard and each call position are listed here.

### Delivery inventory

Durable join: TaskId -> LandingOperationId -> current AgentTaskLandRequest.Id ->
WorktreeCleanupAttempt.Id (capture ID) -> TerminalEventId/SourceEventId ->
AgentTaskLandNotification.Id -> SessionQueuedMessage.SourceLandNotificationId and
QueueMessageId -> original ParentSessionId + ConfirmingPromptSequence. Preserve
notification body/content digest and original destination through every recovery.

| Producer -> destination | Persistence boundary | Recovery and observable receipt |
|---|---|---|
| CleanupAsync/coordinator -> cleanup journal | Unique current RequestId row; command intent before either launch; first failure before collection; bounded capture before retry | JC matrix below. Read fresh committed attempt, never cached observation. Journal evidence is durable diagnostic capture, not authority or recipient receipt. |
| Handle provider/native probe -> journal capture | One sanitized bounded capture under the attempt identity | Incomplete query becomes Interrupted after restart; do not rerun and label it the first observation. Captured remains immutable, including clean recovery. |
| Journal + protocol -> terminal transaction | Attempt disposition/event link, operation component state, Cleanup stage, terminal event, request completion/pending flags and one immutable Outcome notification commit together | DC-1..3. Rollback/restart reloads the same attempt; no publication replay. The eventual recipient body includes its capture reference or explicit Interrupted/storage limitation. |
| Terminal Outcome notification -> original session queue | Unique SourceLandNotificationId; original QueueMessageId/body/digest/destination | DC-4..6. Failed enqueue retries; insert with lost acknowledgement resolves the same keyed row. Accepted request/event/insert alone proves no receipt. |
| Notification worker + completion flush/scan -> queue submitter -> recipient | Queue attempt sequence/time floor saved before input; actual submitted body | DC-7..8, V-7 busy and already-eligible paths. CompletionNoteWorkHostedService repairs dropped wakeup for existing pending queue; notification recovery alone cannot flush it. |
| Recipient adapter transcript -> runtime catch-up -> notification receipt | Complete matching UserPrompt in original session, after sequence floor; if sequence unavailable use existing timestamp floor/tolerance; persist confirming sequence | DC-8..11. Query through a fresh context. Actual complete recipient body must contain current/prior capture identity and expected diagnostic minimum. Sent, transport ack, redraw, log and stage are insufficient. |
| Worktree settlement observation -> existing parent completion note | Existing settled task and keyed parent note before release | V-9 runs real queue to matching complete UserPrompt despite stopper/sink failure. New lifecycle observations are secondary; no new message obligation or stopper call is introduced. |

Cleanup crash cuts, all implemented in
`WorktreeCleanupJournalTests.C443_CleanupWorkerDeathMatrix` with ten arguments:

| Cut | Expected state after fresh-worker recovery | PC coverage |
|---|---|---|
| JC-1 before attempt commit | Zero launch; absent row can be created once under current request and baseline authority | PC-105,107,108 |
| JC-2 initial slot committed, before launch | Initial spent even though no child started; residue/Interrupted; no replay | PC-109 |
| JC-3 initial Git returned, before first-failure commit | Initial spent; lost outcome unknown/Interrupted, no invented code/PID | PC-112,128 |
| JC-4 first failure committed, provider not finished | Same failure/Pending ID becomes Interrupted; no recapture | PC-113,122,123 |
| JC-5 capture committed, before retry intent | Same capture retained; restart closes optional retry, even intact tree | PC-114,124,127 |
| JC-6 retry slot committed, before launch | Both slots spent; no replay | PC-110,111 |
| JC-7 retry child launched, before result | Standing journal fences admission; after owned child/drain recovery no directory replay | PC-111,201,202 |
| JC-8 removal complete, before terminal save | Reconcile directory/registration/ref; guarded branch-only completion allowed when tree and registration both absent | PC-125,183..193 |
| JC-9 terminal SaveChanges returned, before transaction commit | No final disposition/event/note exposed; original committed capture survives; eventual one receipt | PC-126,55 |
| JC-10 terminal commit returned, acknowledgement lost | Final attempt/event/request/note all committed exactly once; recovered caller receipt | PC-55,126,209 |

Each cut asserts it was reached, native worker died, fresh scopes were used and
no forbidden directory command followed. JC-7 is the standing-journal case,
not license to clear unknown records. Before/after-commit EF faults are also
run for attempt creation, each slot, first failure and capture, including
commit-success/readback-failure. Those faults are in PC-107..127 methods and
cannot replace the ten physical worker-death cases.

Delivery cuts, implemented in
`AgentTaskWorktreeLockOutcomeTests.C443_DeliveryCrashMatrix`:

| Cut | Recovery expectation | PC |
|---|---|---|
| DC-1 terminal before save | Rollback final disposition; settle from durable capture; one complete recipient prompt | PC-126,55 |
| DC-2 after terminal save, before commit | No partial terminal facts; same recovery verdict | PC-126 |
| DC-3 terminal committed, before publish | Independent notification worker finds owed Outcome | PC-55 |
| DC-4 before enqueue / enqueue failure | RetryPending until due; then original immutable Outcome reaches recipient | PC-55,56 |
| DC-5 insert committed, acknowledgement lost | Same SourceLandNotificationId and queue ID reused | PC-56,205 |
| DC-6 queue acknowledgement saved, before wakeup | Same pending row; completion recovery scan delivers | PC-57 |
| DC-7 completion wakeup dropped and workers rebuilt | Already eligible recipient receives without future TurnEnd; busy remains pending until actual eligibility | PC-57,58,59 |
| DC-8 submit occurred, transcript persistence/catch-up interrupted | Preserve recipient-side actual submitted body; catch-up records that UserPrompt; do not derive it from queued payload | PC-66 |
| DC-9 complete UserPrompt persisted, before notification confirmation | Fresh reconciliation confirms original prompt, no additional submission | PC-66,206..208 |
| DC-10 receipt save failed | Reload original queue/prompt and persist one confirming sequence | PC-66 |
| DC-11 receipt committed, acknowledgement lost | Confirmed short-circuit; no duplicate input or note | PC-209 |

DC-1..7 each cross busy/eligible and OwnersObserved/InsufficientPrivileges
payloads (28 cases). DC-8..11 each cross both payloads (8 cases): a submitted
prompt already implies eligibility; reintroducing busy state after receipt
does not authorize retyping. Total: 36 delivery cases. Also test maximum bounded
summary in both V-7 direct recipient methods. Polling the task detail does not
discharge any delivery obligation.

Substitutes and limits: BridgeQueueHarness's receiving TUI is fake; its callback
is admissible here only because it records the actual body at the separate Enter.
It proves production producer/transaction/queue/catch-up/receipt behavior, not a
real provider's native TUI. EF fault injection proves transactional cuts, not
process death; JC adds the latter. I/O barriers prove guarded retry on an intact
tree, not typical Windows Git lock recovery. Native doubles prove flags,
classification, error33 and races, not measured Win32 sharing32. Sanitized CSV
proves parser behavior, not working elevated Handle. Logs never replace journal
or receipt. SeedAsync, manually copying notification text to transcript,
ReplyTo.None, direct flush-only recovery and pointer-only spill evidence cannot
satisfy the new Outcome's complete-body delivery acceptance.

Test-design/ordinary Review must reject a design or evidence report that ends
at any producer/queue/terminal stage without the specified recipient UserPrompt.

### Proves it works now

"Expected" is the required Code result, not a claim about the unimplemented
feature. All table PC methods are ordinary V/R tests first; Mutation later changes
production code to demonstrate each guard is observed.

- V-1: Handle provider/configuration/parser | unit + owned diagnostic child |
  WorktreeLockDiagnosticsTests, PC-1..9,25..39,42,69..72 |
  fixed vector; trustworthy availability; quoted CSV and path/identity correctness;
  no owner stop; joined diagnostic child/readers. Cross OS supported/unsupported,
  elevated/unelevated and configured/missing/relative/untrusted executable.
  File-control, directory-control and PID-control failures are independent.
- V-2: native observation | unit | WorktreeDeleteAccessProbeTests, PC-73..97 |
  exact nonmutating flags, immediate last-error capture, root first, bounded
  candidates/enumeration, component/final-handle path validation, no admin or
  reparse traversal. Test 63/64/65 entries; 1.999/2/2.001 seconds; 32/33/5/145,
  unknown/missing/unsupported/success; retain valid32 with partial remainder.
- V-3: budgets and data size | unit/integration | provider/native/guarded/journal
  methods PC-22..30,41,85,86,115,116,195..200,210,211 |
  combined streams 262143/262144/262145 bytes, owners 31/32/33,
  total JSON UTF8 32767/32768/32769 including multibyte values and metadata;
  display 599/600/601, whole stage 949/950/951, LastReason 399/400/401.
  UTC jumps do not extend monotonic ten seconds; delay and every extra await
  consume remaining budget. Canceled budget still permits required settlement
  under live outer token; outer cancellation propagates.
- V-4: guarded retry | real Git/receipt with controlled read/command barriers |
  WorktreeGuardedCleanupTests and added C443_Authority methods in
  AgentTaskLandRemovalMatrixTests |
  exactly two ordinary directory-removal slots, one 250 ms delay and one capture;
  capture commit before retry; initial success no probes; only normally exited
  nonzero Git plus independent valid32/33 nominates. Recheck every boundary
  in PC-135..182; all mismatches preserve sentinels/index/refs. Partial removal,
  unregistered or success-incomplete cannot be "repaired" into retry eligibility.
- V-5: real Windows qualification | owned Git/native children |
  WorktreeLockDiagnosticsWindowsTests has eight named methods:
  C443_NativeFileSharing32, C443_NativeRootDirectorySharing32,
  C443_SeparateFileHolder, C443_EmptyRootCwdHolder,
  C443_ExplicitRootDirectoryHandle, C443_DeleteSharingOwnerCanBeClean,
  C443_TransientIntactTreeRetry, C443_PartialRemovalIsRetained.
  Native first two use the real unelevated-capable D-3 provider with an owned
  child; assert actual DeleteAccessOpen error32 and unchanged files/attributes.
  Handle file/root/CWD methods require real failed ordinary receipt-backed Git
  and expected child PID/start/path (root '.') with successful file/directory
  calibration. Empty root is observed after Git removed entries, not fabricated
  by unregistering or erasing metadata. Delete-sharing owner may remain observed
  while cleanup succeeds. Persistent conflict retains actual remaining bytes.
  The transient qualification must first prove checkout/status/index/registration
  remained intact after the real first failure, then release the holder after
  capture commit and observe the guarded retry. Never restore missing files.
  If real Git partially removes it, assert safe retention and report "intact
  transient recovery not qualified"; do not claim that case as the transient
  positive control. Deterministic V-4 still proves the retry contract, not
  common-case Windows recovery. Missing Handle qualification remains explicit
  and cannot close historical owner-identification acceptance.
- V-6: durable journal and migration | isolated PostgreSQL + real worker |
  WorktreeCleanupJournalTests, all its table methods plus
  C443_CleanupWorkerDeathMatrix and C443_MigrationPreservesLegacyRows |
  unique current request, immutable evidence, spent slots, capture-before-retry,
  ten JC cuts, all terminal facts atomically linked. CLI migration applies twice
  idempotently; pre-migration landing rows survive with no invented captures.
  Include concurrent same-request writers, new explicit request, lost commit
  acknowledgement and unavailable database/readback.
- V-7: producer-to-recipient | integration | C443_BusyRecipientGetsOutcomeWhenIdle
  and C443_EligibleRecipientGetsOutcome in AgentTaskWorktreeLockOutcomeTests |
  each has OwnersObserved and InsufficientPrivileges arguments; fresh UserPrompt
  equals normalized complete immutable Outcome, same capture/current or prior
  provenance, original session and persisted confirming sequence. Busy barrier
  has zero submissions/prompts; eligible completes without artificial TurnEnd.
- V-8: all delivery/clean-recovery cuts | integration |
  AgentTaskWorktreeLockOutcomeTests table methods and C443_DeliveryCrashMatrix |
  36 DC cases plus clean-after-retry/restart and later explicit cleanup provenance.
  One original notification/queue/body/receipt per request, no second publication
  or submission after actual matching receipt.
- V-9: lifecycle/custody | integration + owned verifier |
  AgentTaskWorktreeLockLifecycleTests, PC-48..53,67 |
  completed persistence, enqueue and publish precede stop; returned/throwing
  stopper never implies OS exit; Shared/standing adds no observations/stops.
  Run real minimal buildable verifier and actual ILandingChildObserver
  before-start/start/exit/drain, cancellation and observer-fault paths;
  skipped verification has no child. Throwing log sink still permits parent
  complete UserPrompt. PC-24,201,202 retain actual Git child custody.
- V-10: composition | unit/integration | DelegationTestServicesTests,
  DelegationHarnessCensusTests and C443_JournalScopeLifetime |
  both graph builders and production registrations resolve all new seams;
  singleton journal opens scoped contexts; fake overrides remain by reference.
  Construction without a database must remain possible for pure graph tests;
  attempting persistence still requires a real registered scope/context.

Code execution recipe (fresh unique TRX directory on every invocation):

~~~powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c443/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c443/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c443-unit
$c443Classes = @(
  'WorktreeLockDiagnosticsTests', 'WorktreeDeleteAccessProbeTests',
  'WorktreeGuardedCleanupTests', 'WorktreeCleanupJournalTests',
  'AgentTaskWorktreeLockOutcomeTests', 'AgentTaskWorktreeLockLifecycleTests',
  'AgentTaskLandRemovalMatrixTests', 'AgentTaskLandNotificationPersistenceTests',
  'AgentTaskLandPersistenceFailureTests', 'AgentTaskLandNotificationRecoveryTests',
  'AgentTaskLandReceiptTests', 'AgentTaskLandStageOutcomeTests',
  'AgentTaskSettlementRaceTests', 'DelegationTestServicesTests',
  'DelegationHarnessCensusTests'
)
foreach ($c443Class in $c443Classes) {
  $c443Results = '.antiphon/c443-' + $c443Class + '-' + [Guid]::NewGuid().ToString('N')
  dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c443/ -- --treenode-filter "/*/*/$c443Class/*" --report-trx --report-trx-filename run.trx --results-directory $c443Results
  if ($LASTEXITCODE -ne 0) { throw "Failed: $c443Class" }
}
~~~

Run the four R-1 exact methods below separately with the same command flags;
run V-5's Windows class in its declared explicit qualification lane.
Use fresh TRX to require every selected class and all 217 distinct PC target
methods, not exit zero/discovery. Minimum specified V executions: 309
(217 target methods +36 extra Initial/Retry authority arguments +2 extra
V-7 payload arguments +10 JC +36 DC +7 additional Windows +1 migration).
This is a conservative minimum, not the expected entire assembly count:
existing parameterized methods and R-only methods add executions. Report exact
expanded counts/failures/skips from TRX. Windows prerequisites/qualification
limits must be reported separately, never absorbed into a green count.
Do not broaden a failed run: reproduce specific inherited failures at the base.
Code runs V/R and commits/pushes before ordinary Review; PCs execute after land.

### Guards the regression

- R-1: legacy/default/age are not authority | exact methods
  WorktreeManagerGitIntegrationTests.WorktreeManager_try_remove_reports_directory_residue_when_a_file_is_held;
  WorktreeRemovalDefaultTests.C448_V36_InterfaceDefaultsNeverDelegateDeletion;
  WorktreeRemovalAuthorityTests.C448_V24_LegacyRemovalCannotEraseTaskContents;
  WorktreeResidueSweepTests.execute_never_treats_legacy_landed_event_as_cleanup_authority |
  typed refusal, no removal, retained opaque source/ref bytes. Do not run the
  unrelated build-junk-policy test as this card's evidence.
- R-2: receipt-backed removal remains conservative |
  AgentTaskLandRemovalMatrixTests.C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries,
  C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate,
  C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent plus V-4 |
  no mutation under changed authority/content; exact ordinary remove and old-SHA
  ref vector; component absence independently confirmed; remote source intact.
- R-3: diagnostics never rewrite publication |
  AgentTaskLandStageOutcomeTests.reland_of_an_already_landed_task_runs_cleanup_only
  and V-6/V-8 outcome tests | same operation/publication SHAs/pins/confirmation;
  one publication event then LandingCleanup; clean LastReason null, capture retained.
- R-4: observation cannot reorder release |
  AgentTaskSettlementRaceTests.worktree_pool_settle_delivers_parent_note_when_kill_savechanges_races_retire
  plus V-9 | exactly one parent note before stopper and complete recipient prompt
  after eligibility, despite real scoped retirement/save races.
- R-5: graph remains usable | DelegationTestServicesTests and
  DelegationHarnessCensusTests, V-10 | preserved fake instance, no duplicate
  registration or captured DbContext; constructors updated at both graph builders.
- R-6: receipt cannot be forged by intermediate signals |
  AgentTaskLandReceiptTests.C467_V11_RejectFalseReceipts and six C488 wrappers,
  notification persistence/recovery classes plus V-7/V-8 |
  queue/log/Sent/screen/wrong-session/wrong-ID/head-only/splice/old-floor do not
  confirm. Complete/flattened and existing clock-tolerance positives still confirm.
  No new dual sequence-and-time requirement is introduced.

### Guard inventory

Each row is one guard and maps 1:1 to a distinct PC; no "none" exemptions.
Repeated external-check call positions and independently skipped bounds are
separate rows. Shared predicate internals in unchanged HasPublication,
InspectAsync, repository lease admission and verification-custody policy retain
their owning CARD-0448/CARD-0478 matrices: this card inventories their entry
guards, freshness and invocation positions, not a rewrite of those algorithms.

| Guard | Plan reference and safety-critical invariant | Positive control |
|---|---|---|
| G-1 | D-1; unsupported platform remains unavailable | PC-1 |
| G-2 | D-1; trusted absolute configured executable only | PC-2 |
| G-3 | D-1; invocation identity is already elevated | PC-3 |
| G-4 | D-1; fixed Handle argument vector without close switches | PC-4 |
| G-5 | D-1; diagnostic cwd stays outside target | PC-5 |
| G-6 | D-1; target query precedes calibration | PC-6 |
| G-7 | D-1; known file calibration is required | PC-7 |
| G-8 | D-1; known directory calibration is required | PC-8 |
| G-9 | D-1; owned calibration resources released in finally | PC-9 |
| G-10 | D-2; committed first-failure capture precedes additional Git | PC-10 |
| G-11 | D-2; one capture per request live invocation | PC-11 |
| G-12 | D-2; initial intentional Git lock refuses removal | PC-12 |
| G-13 | D-2; new intentional Git lock stops retry | PC-13 |
| G-14 | D-2; unknown registration refuses retry | PC-14 |
| G-15 | D-2; managed canonical root rechecked before retry | PC-15 |
| G-16 | D-2; retry preflight entry reads fresh authority | PC-16 |
| G-17 | D-2; raw removal grants no authority | PC-17 |
| G-18 | D-2; ordinary Git removal never uses force | PC-18 |
| G-19 | D-2; only independently observed native codes 32 or 33 nominate | PC-19 |
| G-20 | D-2; two directory-removal slots per request | PC-20 |
| G-21 | D-2; one 250 ms delay after nomination | PC-21 |
| G-22 | D-2; one monotonic ten-second additional-work allowance | PC-22 |
| G-23 | D-2; retry registration reads use remaining budget | PC-23 |
| G-24 | D-2; owned Git exit and stream drain precede cleanup return | PC-24 |
| G-25 | D-1/D-5; outer cancellation remains cancellation | PC-25 |
| G-26 | D-1/D-5; one five-second Handle query/control/enrichment allowance | PC-26 |
| G-27 | D-1/D-5; combined stdout/stderr cap is 256 KiB | PC-27 |
| G-28 | D-1/D-5; parsed owner cap is 32 with omissions | PC-28 |
| G-29 | D-1/D-5; provider serialized snapshot cap is 32 KiB UTF-8 | PC-29 |
| G-30 | D-1/D-5; diagnostic child and readers joined before return | PC-30 |
| G-31 | D-1/D-5; observed owner processes are never stopped | PC-31 |
| G-32 | D-1/D-5; quoted CSV fields parsed correctly | PC-32 |
| G-33 | D-1/D-5; root/descendant separator containment | PC-33 |
| G-34 | D-1/D-5; unresolved device paths make coverage partial | PC-34 |
| G-35 | D-1/D-5; empty observation requires complete valid calibrated coverage | PC-35 |
| G-36 | D-1/D-5; partial coverage preserves observed owners | PC-36 |
| G-37 | D-1/D-5; PID creation identity gates enrichment | PC-37 |
| G-38 | D-1/D-5; uncertain ancestry remains unknown | PC-38 |
| G-39 | D-1/D-5; vanished target reported PathGone | PC-39 |
| G-40 | D-4; first and last Git outcomes remain separate | PC-40 |
| G-41 | D-4; bounded display preserves minimum diagnostic identity | PC-41 |
| G-42 | D-4; sanitize all stored/logged evidence | PC-42 |
| G-43 | D-4; clean retry retains committed capture | PC-43 |
| G-44 | D-4; diagnostic availability does not alter clean component facts | PC-44 |
| G-45 | D-4; cleanup residue does not erase publication proof | PC-45 |
| G-46 | D-4; explicit re-land is cleanup-only on same operation | PC-46 |
| G-47 | D-4; branch failure cannot report clean | PC-47 |
| G-48 | D-5; settlement persistence and parent enqueue precede stop | PC-48 |
| G-49 | D-5/S5; stop failure is not OS-exit proof | PC-49 |
| G-50 | D-5/S5; skipped verification has no child | PC-50 |
| G-51 | D-5/S5; verifier observation follows existing exit/drain ordering | PC-51 |
| G-52 | D-5/S5; new lifecycle observations are Worktree-only | PC-52 |
| G-53 | D-5/S5; no additional stopper calls | PC-53 |
| G-54 | D-5/S5; TryAdd registrations preserve overrides | PC-54 |
| G-55 | D-4; terminal commit owes one notification | PC-55 |
| G-56 | D-4; enqueue retry uses durable notification key | PC-56 |
| G-57 | D-4; lost wakeup is recovered by standing queue recovery | PC-57 |
| G-58 | D-4; busy recipient waits for eligibility | PC-58 |
| G-59 | D-4; already eligible recipient needs no future TurnEnd | PC-59 |
| G-60 | D-4; Sent without UserPrompt is unconfirmed | PC-60 |
| G-61 | D-4; receipt matches original session | PC-61 |
| G-62 | D-4; receipt matches notification identity | PC-62 |
| G-63 | D-4; receipt contains complete body | PC-63 |
| G-64 | D-4; receipt is beyond sequence floor | PC-64 |
| G-65 | D-4; null sequence requires bounded time floor | PC-65 |
| G-66 | D-4; receipt-save failure does not resubmit | PC-66 |
| G-67 | D-5; observation sink failure cannot alter settlement/delivery | PC-67 |
| G-68 | D-2; creation paths never enter new cleanup coordinator | PC-68 |
| G-69 | D-1; unknown CSV schemas refuse complete coverage | PC-69 |
| G-70 | D-1; calibration matches the expected PID | PC-70 |
| G-71 | D-1; control files stay outside every worktree | PC-71 |
| G-72 | D-1/D-4; evidence destinations stay outside target | PC-72 |
| G-73 | D-3; probe requests DELETE access | PC-73 |
| G-74 | D-3; probe permits all share flags | PC-74 |
| G-75 | D-3; probe opens existing paths only | PC-75 |
| G-76 | D-3; directory opens use backup semantics | PC-76 |
| G-77 | D-3; native open does not follow final reparse target | PC-77 |
| G-78 | D-3; probe performs no mutation | PC-78 |
| G-79 | D-3; successful native handles are promptly disposed | PC-79 |
| G-80 | D-3; native handles are noninheritable | PC-80 |
| G-81 | D-3; last error read immediately from failed native call | PC-81 |
| G-82 | D-3; root is probed before descendants | PC-82 |
| G-83 | D-3; at most 64 descendant candidates | PC-83 |
| G-84 | D-3; bounded enumeration counts rejected candidates | PC-84 |
| G-85 | D-3; probe has one two-second budget | PC-85 |
| G-86 | D-2/D-3; probe shares remaining additional-work allowance | PC-86 |
| G-87 | D-3; probe reads no file contents | PC-87 |
| G-88 | D-3; enumeration never traverses reparse directories | PC-88 |
| G-89 | D-3; root identity validated before scan | PC-89 |
| G-90 | D-3; failed-open path checked before native call | PC-90 |
| G-91 | D-3; failed-open path checked after native call | PC-91 |
| G-92 | D-3; successful handle final path must match | PC-92 |
| G-93 | D-3; probe excludes Git administrative storage | PC-93 |
| G-94 | D-3; Handle-prioritized candidates need native confinement validation | PC-94 |
| G-95 | D-3; positive sharing observation survives partial scan | PC-95 |
| G-96 | D-3; unsupported native platform is explicit | PC-96 |
| G-97 | D-3; limited native coverage never claims absence of blockers | PC-97 |
| G-98 | D-1/D-3; native probe independent of Handle availability | PC-98 |
| G-99 | D-2/D-3; Git error and owner evidence cannot nominate retry | PC-99 |
| G-100 | D-2; successful-but-incomplete Git result cannot nominate | PC-100 |
| G-101 | D-2; timed-out first command cannot nominate | PC-101 |
| G-102 | D-2; context-free publication keeps one-pass behavior | PC-102 |
| G-103 | D-2; LocalMerge excluded from diagnostic retry | PC-103 |
| G-104 | D-2; Verification excluded from diagnostic retry | PC-104 |
| G-105 | D-4/S1; one attempt row per current request | PC-105 |
| G-106 | D-4; attempt immutable source coordinates | PC-106 |
| G-107 | D-4; unknown attempt commit acknowledgement is reread | PC-107 |
| G-108 | D-2/D-4; initial slot consumption commits before command | PC-108 |
| G-109 | D-4; initial consumed slot never replays after restart | PC-109 |
| G-110 | D-2/D-4; retry slot consumption commits before command | PC-110 |
| G-111 | D-4; retry consumed slot never replays after restart | PC-111 |
| G-112 | D-4; first failure marker commits before collection | PC-112 |
| G-113 | D-4; first Git failure is write-once | PC-113 |
| G-114 | D-4; capture JSON is write-once | PC-114 |
| G-115 | D-4; journal enforces entire JSON UTF-8 bound | PC-115 |
| G-116 | D-4; LastReason remains separately bounded to 400 | PC-116 |
| G-117 | D-4; clean capture reference never changes IsClean | PC-117 |
| G-118 | D-4; protocol uses current request, not original approval | PC-118 |
| G-119 | D-4; typed context must match journal request identity | PC-119 |
| G-120 | D-4; journal reads use independent fresh scopes | PC-120 |
| G-121 | D-2/D-4; capture storage failure prevents retry | PC-121 |
| G-122 | D-4; Pending capture recovers as Interrupted | PC-122 |
| G-123 | D-4; recovery never reruns lost first capture | PC-123 |
| G-124 | D-4; committed capture closes automatic retry on restart | PC-124 |
| G-125 | D-4; completed removal is reconciled before terminal commit | PC-125 |
| G-126 | D-4; attempt final disposition commits with terminal transaction | PC-126 |
| G-127 | D-4; capture commit ambiguity resolved before dependent retry | PC-127 |
| G-128 | D-4; lost uncommitted observation stays unknown | PC-128 |
| G-129 | D-4; prior capture is labelled with original provenance | PC-129 |
| G-130 | D-4; task-detail projection exposes committed capture | PC-130 |
| G-131 | D-4; capture journal retains parent landing history | PC-131 |
| G-132 | D-4; concurrent updates cannot replenish slots | PC-132 |
| G-133 | D-4; unknown journal schema refuses destructive recovery | PC-133 |
| G-134 | D-2/D-4; logging failures do not block evidence settlement | PC-134 |
| G-135 | D-2; initial first inspection cannot be skipped | PC-135 |
| G-136 | D-2; initial second inspection cannot be skipped | PC-136 |
| G-137 | D-2; initial first ignored-content check is independent | PC-137 |
| G-138 | D-2; initial second ignored-content check is independent | PC-138 |
| G-139 | D-2; initial authority refresh between content checks is required | PC-139 |
| G-140 | D-2; initial final authority follows committed slot | PC-140 |
| G-141 | D-2; retry first content inspection is fresh | PC-141 |
| G-142 | D-2; retry second content inspection is fresh | PC-142 |
| G-143 | D-2; retry first ignored-content check is independent | PC-143 |
| G-144 | D-2; retry second ignored-content check is independent | PC-144 |
| G-145 | D-2; retry authority refresh between content checks is required | PC-145 |
| G-146 | D-2; retry final authority follows committed second slot | PC-146 |
| G-147 | D-2/CARD-0448; genuine lease ownership | PC-147 |
| G-148 | D-2/CARD-0448; Git common-directory identity | PC-148 |
| G-149 | D-2/CARD-0448; full immutable deletion/target object IDs | PC-149 |
| G-150 | D-2/CARD-0448; source and target references differ | PC-150 |
| G-151 | D-2/CARD-0448; fresh committed receipt required | PC-151 |
| G-152 | D-2/CARD-0448; receipt stays active | PC-152 |
| G-153 | D-2/CARD-0448; committed publication proof required | PC-153 |
| G-154 | D-2/CARD-0448; receipt belongs to exact task | PC-154 |
| G-155 | D-2/CARD-0448; receipt source ref matches request | PC-155 |
| G-156 | D-2/CARD-0448; receipt target ref matches request | PC-156 |
| G-157 | D-2/CARD-0448; receipt target-before SHA matches request | PC-157 |
| G-158 | D-2/CARD-0448; receipt deletion SHA matches request | PC-158 |
| G-159 | D-2/CARD-0448; receipt verified SHA matches deletion | PC-159 |
| G-160 | D-2/CARD-0448; receipt has committed cleanup intent | PC-160 |
| G-161 | D-2/CARD-0448; receipt is in cleanup/complete phase | PC-161 |
| G-162 | D-2/CARD-0448; receipt repository coordinate matches | PC-162 |
| G-163 | D-2/CARD-0448; receipt worktree coordinate matches | PC-163 |
| G-164 | D-2/CARD-0448; receipt Git admin coordinate matches | PC-164 |
| G-165 | D-2/CARD-0448; receipt common-directory coordinate matches | PC-165 |
| G-166 | D-2/CARD-0448; fresh task still owns active receipt | PC-166 |
| G-167 | D-2/CARD-0448; fresh task is Succeeded | PC-167 |
| G-168 | D-2/CARD-0448; fresh task source metadata matches receipt | PC-168 |
| G-169 | D-2/CARD-0448; fresh task target metadata matches receipt | PC-169 |
| G-170 | D-2/CARD-0448; fresh task repository matches receipt | PC-170 |
| G-171 | D-2/CARD-0448; fresh task worktree matches receipt | PC-171 |
| G-172 | D-2/CARD-0448; publication cannot delete sourced Mutation snapshot | PC-172 |
| G-173 | D-2/CARD-0448; Mutation role never borrows publication cleanup | PC-173 |
| G-174 | D-2/CARD-0448; original-source recovery pin remains exact | PC-174 |
| G-175 | D-2/CARD-0448; target-before recovery pin remains exact | PC-175 |
| G-176 | D-2/CARD-0448; prepared recovery pin remains exact | PC-176 |
| G-177 | D-2/CARD-0448; fresh push endpoint still contains source | PC-177 |
| G-178 | D-2/CARD-0448; unknown remote observation refuses | PC-178 |
| G-179 | D-2/CARD-0448; inspection HEAD equals recorded deletion SHA | PC-179 |
| G-180 | D-2/CARD-0448; source inspection common directory matches | PC-180 |
| G-181 | D-2/CARD-0448; source inspection Git directory matches | PC-181 |
| G-182 | D-2/CARD-0448; source inspection must be accepted | PC-182 |
| G-183 | D-2; unregistered existing directory is preserved | PC-183 |
| G-184 | D-2; missing directory with registration is not clean | PC-184 |
| G-185 | D-2; directory absence is checked after Git success | PC-185 |
| G-186 | D-2; registration absence is checked after Git success | PC-186 |
| G-187 | D-2; branch completion refreshes authority | PC-187 |
| G-188 | D-2; recreated directory stops branch completion | PC-188 |
| G-189 | D-2; source checked out elsewhere stops branch deletion | PC-189 |
| G-190 | D-2; branch deletion uses exact old SHA | PC-190 |
| G-191 | D-2; branch deletion never dereferences symbolic ref | PC-191 |
| G-192 | D-2; branch absence must be confirmed | PC-192 |
| G-193 | D-2; branch failure gets no automatic retry | PC-193 |
| G-194 | D-2; no filesystem fallback or registration rescue | PC-194 |
| G-195 | D-2; failure checkpoint consumes shared allowance | PC-195 |
| G-196 | D-2; capture save consumes shared allowance | PC-196 |
| G-197 | D-2; retry authority/remote I/O consumes remaining allowance | PC-197 |
| G-198 | D-2; retry source inspection consumes remaining allowance | PC-198 |
| G-199 | D-2; additional Git command cannot receive new five-minute allowance | PC-199 |
| G-200 | D-2/D-4; spent extra-work budget still permits final evidence settlement | PC-200 |
| G-201 | D-2/D-5; both ordinary removals retain standing child journal | PC-201 |
| G-202 | D-2/D-5; uncertain child evidence fences recovery admission | PC-202 |
| G-203 | S5; singleton journal resolves fresh scoped contexts | PC-203 |
| G-204 | D-4; notification destination remains immutable | PC-204 |
| G-205 | D-4; key collision cannot replace immutable queue payload | PC-205 |
| G-206 | D-4; receipt eligibility requires actual attempted row | PC-206 |
| G-207 | D-4; queued transcript kinds are not receipt | PC-207 |
| G-208 | D-4; receipt without sequence needs known attempt time | PC-208 |
| G-209 | D-4; already confirmed receipt never resubmits | PC-209 |
| G-210 | D-4/S4; display summary has independent 600-character limit | PC-210 |
| G-211 | D-4/S4; whole Cleanup stage detail has independent 950-character limit | PC-211 |
| G-212 | D-4/S4; landing protocol must supply its required diagnostic context | PC-212 |
| G-213 | D-4/S4; clean first removal invokes no diagnostics or retry | PC-213 |
| G-214 | D-2/CARD-0448; authority evidence reader uses a fresh committed context | PC-214 |
| G-215 | D-2/D-4; coordinator distinguishes outer cancellation from extra-work expiry | PC-215 |
| G-216 | D-3; native probe preserves outer cancellation | PC-216 |
| G-217 | D-3; native interop preserves the failed call's last error | PC-217 |
| G-218 | D-1; diagnostic process launch never uses a shell | PC-218 |

### Positive controls

Every PC below breaks the matching G-n by the named compiling production defect
and must make the exact Class.Method red at the listed decisive assertion.
New method names and new I/O seams are specified for Code; no absent method is
claimed already implemented, compiled or executed. There are no B/unknown-operation
rows. Code must retain these names or amend every reference and cost before
ordinary Review; absence of any method is a coverage defect.

Use only `/*/*/ClassName/ExactTestMethod` for each red and restored-green
cycle, with all declared arguments. The table's separate class and method
columns give the literal components of that filter. Do not use a class filter
for a PC, even when two PCs share an existing method (PC-18 and PC-47).
No duplicate PC maps to multiple guards. Boundary substitutes may be recorded
outside the target; they do not replace durable or recipient evidence.

All native/process flag, close-handle/stop and filesystem-rescue mutants execute
against recording I/O or the explicitly owned temporary sentinel. Real native
qualification runs restored production code. PC-217 additionally exercises the
real owned-file probe with only its error-capture metadata changed; its open flags
remain the nonmutating production flags. Mutation never kills a discovered
external owner, passes a destructive flag to real Handle, or clears an unknown
child journal. For timeout mutants hold the test task at a bounded barrier,
assert its token/completion state, then release in finally and await it; do not
turn an intentionally uncanceled await into an orphan/hung suite.

For data-dependent shared predicates, mutate only the named conjunct, preserving
types, signatures and other predicates. Force unrelated checks valid through
the designated boundary. A snapshot-producing I/O decorator is permitted where
coherent disk mutation would necessarily violate an earlier guard; record that
substitution. A compile error, null reference, fixture failure, zero tests, a
different refusal than the designated assertion, or an equivalent mutation is
not a positive control. A survivor is a finding, not permission to weaken tests.
PC-105/131 mutate the EF model and apply a CLI-generated mutation migration in
their owned clone; restore both generated migration and model before green.

Mutation reports break, intended red, restore and fresh-build green at the
confirmed landed source L, with exact method/expanded counts, assertion and TRX
path for each PC. Refresh restored file timestamps and verify fresh output.
Code runs ordinary V/R; separate ordinary Review judges tests and pending PCs
before land. SourceLanding Mutation runs after land, writes evidence/restoration
outside the snapshot, and never commits/pushes from it.

| PC | Class | Exact method | Compiling defect: break matching G | Decisive red assertion | Cost lane |
|---|---|---|---|---|---|

| PC-1 | `WorktreeLockDiagnosticsTests` | `C443_UnsupportedPlatform` | Replace the unsupported-platform result with NoOwnersObserved | Status == Unavailable and Reason == UnsupportedPlatform | U |
| PC-2 | `WorktreeLockDiagnosticsTests` | `C443_RejectUntrustedToolPath` | Accept a relative executable path in the configured-path validator | LaunchCount == 0 for relative, empty and missing paths; explicit unavailable reason | U |
| PC-3 | `WorktreeLockDiagnosticsTests` | `C443_InsufficientPrivileges` | Replace the invocation-token check with true | LaunchCount == 0 and Reason == InsufficientPrivileges with a configured existing tool | U |
| PC-4 | `WorktreeLockDiagnosticsTests` | `C443_ReadOnlyArgumentVector` | Append -c to the argument vector passed to the recording process runner | Arguments equals [-nobanner, -v, normalizedRoot] and UseShellExecute == false | U |
| PC-5 | `WorktreeLockDiagnosticsTests` | `C443_DiagnosticLocationsOutsideTarget` | Set the diagnostic WorkingDirectory to the removal target | Every ProcessStartInfo.WorkingDirectory is outside the target; owned controls/evidence have separately checked external roots | U |
| PC-6 | `WorktreeLockDiagnosticsTests` | `C443_TargetQueryPrecedesControls` | Swap the target and control query calls | Trace starts TargetQuery, then ControlQuery, with target output preserved before calibration | U |
| PC-7 | `WorktreeLockDiagnosticsTests` | `C443_MissingFileControl` | Ignore the held-file match when computing the calibration verdict | Reason == PositiveControlFailed when only the directory and expected PID are observed | U |
| PC-8 | `WorktreeLockDiagnosticsTests` | `C443_MissingDirectoryControl` | Ignore the held-directory match when computing the calibration verdict | Reason == PositiveControlFailed when only the file and expected PID are observed | U |
| PC-9 | `WorktreeLockDiagnosticsTests` | `C443_ControlResourcesDisposed` | Remove the control-resource disposal from finally | Both handle disposal counts == 1; only the unique owned control directory is removed on error and cancellation | U |
| PC-10 | `WorktreeGuardedCleanupTests` | `C443_CaptureCommittedBeforeRetry` | Move journal capture commit after the additional RunAsync | At second-remove entry a fresh context has Captured JSON and first Git outcome; ordered trace FailureCommitted, CaptureCommitted, RetryIntentCommitted, FinalAuthority, Remove | I |
| PC-11 | `WorktreeGuardedCleanupTests` | `C443_OneCapturePerInvocation` | Collect again after the second nonzero Git result | Handle capture count == 1 and native capture count == 1; first capture ID/UTC unchanged | I |
| PC-12 | `WorktreeGuardedCleanupTests` | `C443_InitialGitLockRefuses` | Ignore Locked in the initial source inspection | At locked-registration refusal CaptureCount == 0, RemoveCount == 0; intentional lock remains | I |
| PC-13 | `WorktreeGuardedCleanupTests` | `C443_NewGitLockStopsRetry` | Ignore Locked in the retry source inspection | After delay barrier installs Git lock, RemoveCount == 1; no retry slot consumed; lock remains | I |
| PC-14 | `WorktreeGuardedCleanupTests` | `C443_UnknownRegistrationStopsRetry` | Treat a failed retry registration query as the previously accepted registration list | RemoveCount == 1 for error, timeout and malformed output; residue and index bytes remain | I |
| PC-15 | `WorktreeGuardedCleanupTests` | `C443_RecheckConfinement` | Reuse the original canonical root after the delay barrier replaces the path with a junction | Second command count == 0; outside sentinel bytes unchanged; refusal precedes next preflight boundary | I |
| PC-16 | `WorktreeGuardedCleanupTests` | `C443_RetryStartsWithFreshAuthority` | Skip AuthorityAsync at retry-preflight entry | Fresh committed receipt invalidation after delay refuses before retry status read; second-remove count == 0 | I |
| PC-17 | `WorktreeManagerGitIntegrationTests` | `WorktreeManager_try_remove_reports_directory_residue_when_a_file_is_held` | Change the raw overload's returned residue to null and all three completion flags to true | Residue == typed_removal_authority_required and DirectoryGone == false | I |
| PC-18 | `AgentTaskLandRemovalMatrixTests` | `C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent` | Add --force to GuardedWorktreeRemoval's ordinary Git remove arguments | Trace contains neither --force nor prune; failed-removal sentinel remains | I |
| PC-19 | `WorktreeGuardedCleanupTests` | `C443_RetryOnlyNativeSharingCodes` | Include native code 5 in the DeleteAccessOpen nomination predicate | AdditionalRemoveCount == 0 for 5,145,null,unknown and successful opens; == 1 for valid failed opens 32 and deterministic 33 | I |
| PC-20 | `WorktreeGuardedCleanupTests` | `C443_TwoGitSlotsMaximum` | After the second nonzero result, invoke the same ordinary git.RunAsync removal a third time with the remaining token | Persistent nominated conflict has exactly 2 consumed slots, 2 ordinary removals, 1 capture and no third command | I |
| PC-21 | `WorktreeGuardedCleanupTests` | `C443_SingleBackoff` | Change retry delay from 250 ms to 1 second | ScheduledDelays == [250 ms] for nominated retry; [] after initial clean or nonnomination | I |
| PC-22 | `WorktreeGuardedCleanupTests` | `C443_SharedAdditionalWorkDeadline` | Reset the ten-second timestamp after capture commit | At 9.999/10/10.001 seconds no extra operation starts at or after original deadline; wall-clock jumps do not replenish allowance | I |
| PC-23 | `WorktreeGuardedCleanupTests` | `C443_RegistrationUsesRemainingBudget` | Pass CancellationToken.None to retry RegistrationsAsync | A blocked registration read observes cancellation at original deadline; no second removal | I |
| PC-24 | `WorktreeGuardedCleanupTests` | `C443_NoAbandonedGitChild` | Return from additional-command budget cancellation before awaiting RunAsync completion | Cleanup remains incomplete while owned Git/redirected readers are held; child exit and both stream drains precede return | P |
| PC-25 | `WorktreeLockDiagnosticsTests` | `C443_PropagateOuterCancellation` | Catch OperationCanceledException and return Failed | OperationCanceledException carries caller cancellation; no subsequent query/enrichment. Exercise cancellation before launch, during target/control and during metadata enrichment | U |
| PC-26 | `WorktreeLockDiagnosticsTests` | `C443_SharedDiagnosticBudget` | Allocate a fresh five-second budget for the control query | Control/enrichment deadline <= target-start + 5 seconds; outcome TimedOut at exhaustion | U |
| PC-27 | `WorktreeLockDiagnosticsTests` | `C443_CombinedOutputBound` | Apply the byte limit independently to stdout and stderr | Combined retained bytes <= 262144 and overflow reports OutputTruncated | U |
| PC-28 | `WorktreeLockDiagnosticsTests` | `C443_OwnerCountBound` | Increase the retained-owner limit to 33 | Owners.Count == 32 for 33 unique matches and OmittedOwners == 1 | U |
| PC-29 | `WorktreeLockDiagnosticsTests` | `C443_StructuredEvidenceBound` | Bypass provider final serialized snapshot size check | Provider snapshot UTF8 bytes <= 32768 at 32767/32768/32769 input boundaries; explicit omissions | U |
| PC-30 | `WorktreeLockDiagnosticsTests` | `C443_DiagnosticChildReaped` | Return TimedOut before awaiting the started diagnostic child's exit/readers | Owned child exit and both reader-completion observations precede provider completion | P |
| PC-31 | `WorktreeLockDiagnosticsTests` | `C443_OwnersAreObservationOnly` | Add a stop request for the observed owner through the process-I/O recorder | OwnerStopCalls == 0; diagnostic child teardown remains distinct; no real external process receives the mutation | P |
| PC-32 | `WorktreeLockDiagnosticsTests` | `C443_ValidateCsvSchema` | Replace CSV field parsing with Split(',') | Pinned quoted comma/escaped-quote fixture yields exact process and path; malformed/header guard is independently covered by PC-69 | U |
| PC-33 | `WorktreeLockDiagnosticsTests` | `C443_ExactRootAndDescendants` | Use StartsWith(root) without the separator boundary | Root is '.', descendant included, sibling-prefix owner excluded under mixed case and trailing separators | U |
| PC-34 | `WorktreeLockDiagnosticsTests` | `C443_UnresolvedAliasIsPartial` | Drop unresolved paths without setting partial coverage | Status == Partial and unresolved coverage reason survives alongside valid owners | U |
| PC-35 | `WorktreeLockDiagnosticsTests` | `C443_EmptyRequiresCompleteCoverage` | Return NoOwnersObserved whenever the parsed owner list is empty | Every failed/partial/timed-out/malformed/control-failed row is not NoOwnersObserved; completed calibrated empty row is | U |
| PC-36 | `WorktreeLockDiagnosticsTests` | `C443_PartialRetainsOwners` | Clear Owners when changing the result status to Partial | Expected owner identity/path remains in the partial snapshot and summary | U |
| PC-37 | `WorktreeLockDiagnosticsTests` | `C443_ReusedPidDoesNotEnrichOldOwner` | Join process metadata on PID alone | Reused-PID start identity does not attach the new name/parent; unavailable/exited metadata is explicit | U |
| PC-38 | `WorktreeLockDiagnosticsTests` | `C443_UnknownAncestryStaysUnknown` | Label an owner as landing-created from parent PID alone | Attribution remains unknown for exited/reused parent and shared build server | U |
| PC-39 | `WorktreeLockDiagnosticsTests` | `C443_PathGone` | Return NoOwnersObserved when the target existence check reports absent | Status == PathGone and target-query launch count == 0 | U |
| PC-40 | `WorktreeGuardedCleanupTests` | `C443_PreserveFirstAndLastGitOutcomes` | Assign the second Git failure into FirstGitFailure | FirstGitFailure retains initial exit 128/generated code/time; LastGitOutcome retains exit 1/later time; NativeObservation separately holds 32; no Git Win32 field | I |
| PC-41 | `AgentTaskWorktreeLockOutcomeTests` | `C443_SummaryPreservesEvidenceAtLimit` | Compose optional long text before reserving capture ID/time and first owner or unavailable reason | Persisted diagnostic summary <= 600; whole Cleanup detail <= 950; ID/UTC, first name/PID or explicit availability reason survive task DTO, stage and Outcome | I |
| PC-42 | `WorktreeLockDiagnosticsTests` | `C443_SanitizedStructuredEvidence` | Include raw stdout in the structured log payload | Synthetic secret/command/environment/user/unrelated-handle markers absent from every captured field and log | U |
| PC-43 | `AgentTaskWorktreeLockOutcomeTests` | `C443_RecoveredCleanupKeepsCapture` | Omit journal reference when final removal is clean | Fresh DB, task DTO and recipient UserPrompt contain original capture ID/time; cleanup Complete and LastReason null | I |
| PC-44 | `AgentTaskWorktreeLockOutcomeTests` | `C443_DiagnosticFailureDoesNotRefuseClean` | Set cleanup failure whenever the diagnostic status is Unavailable | With committed unavailable Handle evidence and positive independent probe, successful guarded retry is Complete/Landed with LastReason null; clean explicit recovery retains prior unavailable capture | I |
| PC-45 | `AgentTaskWorktreeLockOutcomeTests` | `C443_ResidueKeepsPublication` | Map a cleanup diagnostic error to LandRefused | Terminal LandedWithResidue, Cleanup Failed stage, same VerifiedSourceSha/ObservedRemoteTargetSha/RemoteConfirmedAt | I |
| PC-46 | `AgentTaskLandStageOutcomeTests` | `reland_of_an_already_landed_task_runs_cleanup_only` | Remove the alreadyReported conversion to LandingCleanup in CompleteTerminalLockedAsync | Exactly one Landed publication and one LandingCleanup; second run adds only one Cleanup stage row | I |
| PC-47 | `AgentTaskLandRemovalMatrixTests` | `C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent` | Report branchGone true after update-ref failure | IsClean == false and the raced source ref equals retained SHA in the branch-moved row | I |
| PC-48 | `AgentTaskWorktreeLockLifecycleTests` | `C443_PersistDeliverBeforeStop` | Move ReleaseDelegateAsync before DeliverToParentAsync | At stopper entry fresh DB has settled task and keyed parent note; trace Persist, Enqueue, Publish, Stop; complete matching UserPrompt eventually observed | I |
| PC-49 | `AgentTaskWorktreeLockLifecycleTests` | `C443_StopFailureIsNotExit` | Emit stop-succeeded from the stopper catch | Throwing stopper produces failure observation without success/OS-exit claim; one complete matching parent UserPrompt | I |
| PC-50 | `AgentTaskWorktreeLockLifecycleTests` | `C443_SkippedVerifyHasNoChild` | Assign the server PID to the skipped verification observation | Skipped log has no child PID/start time and verifier launch count == 0 | I |
| PC-51 | `AgentTaskWorktreeLockLifecycleTests` | `C443_ObserverOrderingUnchanged` | Call the new success observation before awaiting observer.ExitedAsync | BeforeStart, Started with actual PID/start ticks, exit plus both reader drains, ExitedAsync, added completion observation; observer fault preserves baseline failure/journal | P |
| PC-52 | `AgentTaskWorktreeLockLifecycleTests` | `C443_WorktreeOnlyObservations` | Remove the Workspace == Worktree log guard | New observation count == 0 for Shared and standing-agent controls | I |
| PC-53 | `AgentTaskWorktreeLockLifecycleTests` | `C443_NoAdditionalStopperCalls` | Invoke the stopper again after emitting its return observation | Owned worktree stopper call count == 1; standing/shared holder stop count unchanged | I |
| PC-54 | `DelegationTestServicesTests` | `C443_DiagnosticRegistrationPreservesOverride` | Change diagnostics registration from TryAddSingleton to AddSingleton | Preinstalled diagnostics, probe and journal fakes resolve by reference; helper called twice gives one descriptor each; cover service-collection and direct graph builders | U |
| PC-55 | `AgentTaskWorktreeLockOutcomeTests` | `C443_OutcomeTransactionRecovery` | Remove AddNotification from CompleteTerminalLockedAsync | One Outcome linked to same request/event/capture after recovered terminal commit, followed by complete matching recipient UserPrompt | I |
| PC-56 | `AgentTaskWorktreeLockOutcomeTests` | `C443_EnqueueAcknowledgementRecovery` | Omit sourceLandNotificationId in AgentTaskLandNotificationService's enqueue call | After committed insert/lost acknowledgment one queue row has original notification ID/digest and recipient receives once | I |
| PC-57 | `AgentTaskWorktreeLockOutcomeTests` | `C443_LostWakeupRecoversReceipt` | Exclude land rows from CompletionNoteWorkHostedService.ScanAsync pending-row query | After dropped completion wakeup and rebuilt workers, eligible recipient has one complete matching UserPrompt and confirmed sequence | I |
| PC-58 | `AgentTaskWorktreeLockOutcomeTests` | `C443_BusyRecipientGetsOutcomeWhenIdle` | Enqueue the land outcome with MessageSendMode.Now | Busy barrier has zero matching UserPrompts and zero submitted bodies; after TurnEnd one complete matching prompt | I |
| PC-59 | `AgentTaskWorktreeLockOutcomeTests` | `C443_EligibleRecipientGetsOutcome` | Remove flushes.TryEnqueue after successful land enqueue, with periodic backstop held at fixture barrier | Already eligible recipient receives complete body by completion-flush barrier without a future TurnEnd; then durable confirming sequence | I |
| PC-60 | `AgentTaskLandReceiptTests` | `C488_ApprovalReceiptNeedsUserPrompt` | Accept row.Status == Sent as a receipt before querying transcript | ConfirmedAt == null in existing sent-only method; zero prompt rows | I |
| PC-61 | `AgentTaskLandReceiptTests` | `C488_ApprovalReceiptNeedsDestination` | Remove the AgentSessionId predicate from the prompt query | ConfirmedAt remains null for a complete body in the wrong session | I |
| PC-62 | `AgentTaskLandReceiptTests` | `C488_ApprovalReceiptNeedsIdentity` | Compare only a fixed outcome heading instead of the correlated body | ConfirmedAt remains null for the wrong-notification-ID UserPrompt | I |
| PC-63 | `AgentTaskLandReceiptTests` | `C488_ApprovalReceiptNeedsCompleteBody` | Replace full-body matching with a matching first 200 characters | ConfirmedAt remains null for the head-only UserPrompt | I |
| PC-64 | `AgentTaskLandReceiptTests` | `C488_ApprovalReceiptNeedsSequenceFloor` | Remove the Sequence > floor filter | ConfirmedAt remains null for the equal-floor UserPrompt | I |
| PC-65 | `AgentTaskLandReceiptTests` | `C488_ApprovalReceiptNeedsTimeFloor` | Remove Timestamp >= floorTime filtering in the null-sequence branch | ConfirmedAt == null for hour-old UserPrompt; keep existing 30-second tolerance contract | I |
| PC-66 | `AgentTaskWorktreeLockOutcomeTests` | `C443_ReceiptCommitRecoveryDoesNotRetype` | Enqueue an unkeyed copy of the Outcome after receipt-before-save failure | After actual first submission/catch-up and fresh-context recovery, one original queue ID, one UserPrompt, SubmittedBodies.Count == 1, same confirming sequence | I |
| PC-67 | `AgentTaskWorktreeLockLifecycleTests` | `C443_ObservationFailureDoesNotChangeOutcome` | Let an injected observation-sink exception escape the added observation wrapper | Task still settles, stopper count unchanged and parent receives matching complete UserPrompt | I |
| PC-68 | `WorktreeGuardedCleanupTests` | `C443_CreationDoesNotEnterCleanupCoordinator` | Invoke new diagnostics coordinator from the owned failed-add rollback path | CoordinatorCalls == 0, CaptureCount == 0, DelayCount == 0 during creation/rollback/recovery; surviving bytes remain | I |
| PC-69 | `WorktreeLockDiagnosticsTests` | `C443_UnknownCsvSchema` | Accept an unknown CSV header as the pinned schema | Unknown header/column/PID and malformed quoting yield Failed or Partial, never NoOwnersObserved | U |
| PC-70 | `WorktreeLockDiagnosticsTests` | `C443_ControlPidMustMatch` | Ignore PID when matching both calibration paths | Both paths reported only by another PID produce PositiveControlFailed | U |
| PC-71 | `WorktreeLockDiagnosticsTests` | `C443_ControlDirectoryOutsideWorktrees` | Place calibration directory inside the target | ControlRoot is outside every registered fixture worktree before handles or queries start | U |
| PC-72 | `WorktreeLockDiagnosticsTests` | `C443_EvidencePathsOutsideTarget` | Select target as the evidence scratch directory | Every evidence writer destination is external; target inventory remains byte-identical | U |
| PC-73 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeRequestsDeleteAccess` | Replace DELETE desired access with GENERIC_READ | Recorded DesiredAccess == DELETE; owned conflicting file real native lane still returns error 32 | U |
| PC-74 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeSharesReadWriteDelete` | Remove FILE_SHARE_DELETE | Recorded ShareMode == READ + WRITE + DELETE; delete-sharing control remains a successful open | U |
| PC-75 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeNeverCreates` | Replace OPEN_EXISTING with OPEN_ALWAYS | CreationDisposition == OPEN_EXISTING; nonexistent candidate remains absent | U |
| PC-76 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeOpensDirectories` | Remove FILE_FLAG_BACKUP_SEMANTICS | Recorded flags include BACKUP_SEMANTICS and owned root open succeeds | U |
| PC-77 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeOpensReparsePointItself` | Remove FILE_FLAG_OPEN_REPARSE_POINT | Recorded flags include OPEN_REPARSE_POINT; no open of outside junction target | U |
| PC-78 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeCannotDeleteOrAlter` | Add FILE_FLAG_DELETE_ON_CLOSE to native open flags | Exact permitted flag set excludes DELETE_ON_CLOSE; owned file/directory hashes, names and attributes unchanged; no write/delete/rename/disposition APIs in executed I/O trace | U |
| PC-79 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeClosesEachHandle` | Remove disposal of a successful native handle | OutstandingHandleCount == 0 before next candidate and on every return | U |
| PC-80 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeHandlesNotInherited` | Set native security attributes inherit flag true | Recorded InheritHandle == false for every open | U |
| PC-81 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeCapturesLastErrorImmediately` | Read last error after a second successful native metadata call overwrites it | Failed-open error remains 32 under last-error clobber double; operation == DeleteAccessOpen, relative path and UTC exact | U |
| PC-82 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeRootFirst` | Enumerate a descendant before probing root | First Open path == root and first relative path == '.' | U |
| PC-83 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeCandidateCap` | Raise descendant limit to 65 | 65th descendant has zero opens; root plus at most 64 candidates; omitted count/Partial at cap | U |
| PC-84 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeEnumerationCap` | Increment work counter only after an accepted native open | Next-entry calls stop at 64 descendants even when every candidate is excluded; no unbounded traversal | U |
| PC-85 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeTwoSecondDeadline` | Renew two-second budget for each open | No next candidate/enumeration starts at 1.999/2/2.001-second deadline; held synchronous call is joined before return | U |
| PC-86 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeUsesRemainingBudget` | Ignore the caller remaining-budget token | With 0.5 seconds remaining, probe stops new work at shared deadline, not at two seconds | U |
| PC-87 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeDoesNotReadContents` | Read candidate contents before opening for DELETE | ContentReadCalls == 0; enumeration/metadata/open trace only | U |
| PC-88 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeDoesNotTraverseReparse` | Recurse into a directory marked ReparsePoint | Outside sentinel child has zero enumeration/open calls | U |
| PC-89 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeRootIdentityRequired` | Skip canonical root comparison at scan entry | Replaced root produces no Open call and no eligible observation | U |
| PC-90 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeValidatesBeforeOpen` | Skip component validation immediately before Open | Aliased/replaced candidate never reaches Open; no retry nomination | U |
| PC-91 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeValidatesAfterFailure` | Skip post-failure component identity comparison | Replacement during failed Open discards returned 32 from EligibleObservations | U |
| PC-92 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeValidatesFinalHandlePath` | Accept a successful handle whose final path is outside root | Observation marked unverifiable, never successful coverage for requested path | U |
| PC-93 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeSkipsGitAdmin` | Include .git/admin candidate in probe walk | Common directory, linked .git file and admin storage all have zero opens | U |
| PC-94 | `WorktreeDeleteAccessProbeTests` | `C443_ProbeValidatesOwnerCandidates` | Feed an outside Handle owner path directly to native Open | Outside/sibling/alias owner candidates never opened; validated descendant first after root | U |
| PC-95 | `WorktreeDeleteAccessProbeTests` | `C443_PartialProbeKeepsPositive` | Clear eligible observations when enumeration reaches cap | Valid failed 32 remains eligible while Coverage == Partial; unsupported/error-only scan has none | U |
| PC-96 | `WorktreeDeleteAccessProbeTests` | `C443_NativeUnsupportedPlatform` | Return successful complete scan on unsupported platform | Unavailable/UnsupportedPlatform, zero native calls and zero eligible observations | U |
| PC-97 | `WorktreeDeleteAccessProbeTests` | `C443_NativeLimitationsArePartial` | Return complete coverage after enumeration access denial | Coverage == Partial with limitation; successful opens are described as observations only | U |
| PC-98 | `WorktreeGuardedCleanupTests` | `C443_UnavailableHandleStillProbes` | Return after Handle reports InsufficientPrivileges | NativeCallCount == 1; valid 32 nominates and, with fresh checks, second ordinary removal runs | I |
| PC-99 | `WorktreeGuardedCleanupTests` | `C443_GitAndOwnersCannotNominate` | Nominate retry from Git exit 128 or a nonempty Handle owner list | Zero retry with English/localized 'sharing' output, exit128, owner PID and only successful/unknown probes | I |
| PC-100 | `WorktreeGuardedCleanupTests` | `C443_IncompleteSuccessDoesNotRetry` | Treat successful Git with directory residue as a retryable first failure | One slot consumed; capture committed; zero delay/second remove; residue remains | I |
| PC-101 | `WorktreeGuardedCleanupTests` | `C443_TimeoutDoesNotRetry` | Treat first Git timeout like a normally exited nonzero result | Even positive native32 gives one consumed slot and no second removal; timeout evidence preserved | I |
| PC-102 | `WorktreeGuardedCleanupTests` | `C443_ContextFreePublicationIsOnePass` | Create a synthetic attempt ID for a context-free low-level caller | One ordinary remove maximum; no journal row/capture/retry; real typed authority still required | I |
| PC-103 | `WorktreeGuardedCleanupTests` | `C443_LocalMergeIsOnePass` | Allow LocalMerge into retry coordinator | CoordinatorCalls == 0, CaptureCount == 0, additional remove count == 0 | I |
| PC-104 | `WorktreeGuardedCleanupTests` | `C443_VerificationIsOnePass` | Route Verification through publication coordinator | CoordinatorCalls == 0; original guarded verification result retained; no capture/additional removal | I |
| PC-105 | `WorktreeCleanupJournalTests` | `C443_AttemptRequestUnique` | Remove unique RequestId model/index definition using CLI-generated mutation migration | Two concurrent inserts for one request leave one row; duplicate DB insert throws 23505; different request succeeds | DB |
| PC-106 | `WorktreeCleanupJournalTests` | `C443_AttemptCoordinatesImmutable` | Permit an update to persisted deletion SHA | Fresh reload keeps original task/operation/request/source path/ref/SHA; attempted differing write is rejected | I |
| PC-107 | `WorktreeCleanupJournalTests` | `C443_UnknownAttemptCommitReloaded` | Treat thrown commit acknowledgement as successful without keyed read | Postcommit-loss trace contains fresh read of RequestId before any slot; precommit failure launches zero commands | I |
| PC-108 | `WorktreeCleanupJournalTests` | `C443_InitialSlotBeforeLaunch` | Move initial slot-intent commit after RunAsync | Fresh independent observer at first Git start sees initial consumed slot; save rejection starts zero Git | I |
| PC-109 | `WorktreeCleanupJournalTests` | `C443_InitialSlotSpentAcrossRestart` | Reset InitialIntentAt on journal reload | Restart after intent-before-launch and launch-before-result has zero additional directory removals for that slot | I |
| PC-110 | `WorktreeCleanupJournalTests` | `C443_RetrySlotBeforeLaunch` | Move retry slot-intent commit after second RunAsync | Fresh observer at second Git start sees both spent slots and captured JSON; failed retry-intent save starts no second Git | I |
| PC-111 | `WorktreeCleanupJournalTests` | `C443_RetrySlotSpentAcrossRestart` | Reset RetryIntentAt during recovery | Restart after retry intent/launch has no more directory-removal command on the request | I |
| PC-112 | `WorktreeCleanupJournalTests` | `C443_FailureCommittedBeforeCollection` | Start diagnostics before first-failure save acknowledgement | At provider entry fresh row has first Git result/time and Pending capture ID; failed save launches no provider | I |
| PC-113 | `WorktreeCleanupJournalTests` | `C443_FirstFailureWriteOnce` | Overwrite nonnull first failure on a later journal save | First Git failure bytes and timestamp unchanged after second result and competing writer | I |
| PC-114 | `WorktreeCleanupJournalTests` | `C443_CaptureWriteOnce` | Allow new JSON to replace a Captured row | Fresh reload hash and capture time equal original after duplicate, competing and recovery writes | I |
| PC-115 | `WorktreeCleanupJournalTests` | `C443_JournalUtf8Bound` | Check JSON.Length instead of UTF8 byte count | Stored UTF8 <=32768 with multibyte owner data and all metadata; 32769 rejected/truncated explicitly, not DB exception | I |
| PC-116 | `AgentTaskWorktreeLockOutcomeTests` | `C443_LastReasonIsNotCaptureStorage` | Assign diagnostic JSON/600-character summary to op.LastReason | Long input persists with LastReason.Length <=400; summary/evidence separate; no varchar overflow | I |
| PC-117 | `AgentTaskWorktreeLockOutcomeTests` | `C443_CleanResidueRemainsNull` | Populate WorktreeRemoval.Residue with capture ID on clean | All clean component facts imply IsClean true, Residue null, LastReason null despite nonnull diagnostic reference | I |
| PC-118 | `AgentTaskWorktreeLockOutcomeTests` | `C443_CurrentRequestOwnsAttempt` | Look up cleanup journal by op.ApprovalLandRequestId instead of current request | Two explicit cleanup requests share operation but have distinct attempts/current RequestId; first capture/history unchanged | I |
| PC-119 | `WorktreeCleanupJournalTests` | `C443_ContextCannotBorrowAttempt` | Accept another request's attempt ID without keyed comparison | Cross-task/operation/request/context IDs reject before any slot or command; no borrowed capture | I |
| PC-120 | `WorktreeCleanupJournalTests` | `C443_JournalDoesNotUseTrackedEvidence` | Return cached attempt from earlier scope | Unsaved local state never appears durable; separately committed capture is seen after restart; fresh observer hash exact | I |
| PC-121 | `WorktreeCleanupJournalTests` | `C443_CaptureSaveFailureStopsRetry` | Continue retry after capture commit throws without a confirmed keyed read | Before-commit failure or failed readback leaves second-remove count 0 and evidence-storage limitation; publication unchanged | I |
| PC-122 | `WorktreeCleanupJournalTests` | `C443_PendingCaptureInterrupted` | Convert Pending to Captured during recovery | After worker death mid-provider fresh row is Interrupted, retains first failure/ID, contains no invented owners/native codes | I |
| PC-123 | `WorktreeCleanupJournalTests` | `C443_InterruptedCaptureNotReobserved` | Invoke diagnostics again for a recovered Pending row | ProviderCalls after restart == 0; Interrupted old capture ID/time retained | I |
| PC-124 | `WorktreeCleanupJournalTests` | `C443_CapturedRestartClosesAllowance` | Nominate retry on reloading a Captured row | Capture-before-retry restart performs zero directory removals even with stored32/intact tree; explicit new request can consume new initial slot | I |
| PC-125 | `AgentTaskWorktreeLockOutcomeTests` | `C443_RemovalRecoveryUsesComponents` | Re-run directory removal when terminal state was not saved | At removal-before-terminal crash recovery no remove/push; fresh directory/registration/ref facts Complete and same committed capture reaches UserPrompt | I |
| PC-126 | `WorktreeCleanupJournalTests` | `C443_FinalDispositionAtomic` | Save final attempt/event link in an independent transaction before terminal save | Before-save/after-save/precommit cuts leave no finalized attempt link/event/note; after-commit has all linked facts exactly once | I |
| PC-127 | `WorktreeCleanupJournalTests` | `C443_CaptureAcknowledgementReloaded` | Use in-memory JSON as proof after capture commit acknowledgement is lost | Fresh keyed read confirms identical committed capture before retry; failed readback has no second command | I |
| PC-128 | `WorktreeCleanupJournalTests` | `C443_LostResultIsNotInvented` | Populate a default native32/owner on an intent-only recovery | Interrupted row has null unobserved fields; original spent slot retained; recipient reports interruption | I |
| PC-129 | `AgentTaskWorktreeLockOutcomeTests` | `C443_PriorCaptureStaysPrior` | Replace prior attempt time/request with current request values | Later clean no-capture request DTO and UserPrompt say prior attempt and retain original capture/request/time; old notification byte-identical | I |
| PC-130 | `AgentTaskWorktreeLockOutcomeTests` | `C443_TaskDetailProjectsCapture` | Omit latest capture from AgentTaskService projection | Fresh GetAsync landing DTO matches journal operation/current attempt/capture identity and bounded sanitized evidence | I |
| PC-131 | `WorktreeCleanupJournalTests` | `C443_JournalHistoryOwnership` | Configure request foreign key to SetNull instead of required retained relationship | Model/migration have required request/operation FKs; unrelated request deletion cannot orphan/reparent history; parent-retention behavior matches landing rows | DB |
| PC-132 | `WorktreeCleanupJournalTests` | `C443_JournalConcurrencyRejectsLostUpdate` | Remove concurrency-token predicate from journal update | Stale writer is rejected; fresh row preserves spent retry slot, first capture and final state | I |
| PC-133 | `WorktreeCleanupJournalTests` | `C443_UnknownAttemptSchemaRefuses` | Accept schemaVersion 999 as current | Unknown row remains retained; zero slot/command/capture reuse; explicit storage/compatibility limitation | I |
| PC-134 | `AgentTaskWorktreeLockOutcomeTests` | `C443_LogFailureKeepsCommittedCapture` | Let secondary log sink exception escape after journal capture commit | Captured row and Outcome still settle; matching complete UserPrompt; publication/components unchanged | I |
| PC-135 | `WorktreeGuardedCleanupTests` | `C443_InitialFirstInspectionRequired` | Reuse a pre-coordinator accepted inspection instead of first InspectAsync | At first content barrier, new tracked/staged/untracked/submodule changes refuse before second boundary; zero removes | I |
| PC-136 | `WorktreeGuardedCleanupTests` | `C443_InitialSecondInspectionRequired` | Reuse first accepted inspection instead of second InspectAsync | Change after authority refresh refuses before initial slot; zero removes; bytes/index retained | I |
| PC-137 | `WorktreeGuardedCleanupTests` | `C443_InitialFirstIgnoredBoundary` | Remove first HasProtectedIgnored refusal | Ignored .antiphon/.claude/bin-private sentinel refuses at first inspection; second status boundary not reached | I |
| PC-138 | `WorktreeGuardedCleanupTests` | `C443_InitialSecondIgnoredBoundary` | Remove second HasProtectedIgnored refusal | Ignored sentinel introduced only at second inspection refuses before initial slot consumption | I |
| PC-139 | `WorktreeGuardedCleanupTests` | `C443_InitialAuthorityRefreshRequired` | Skip AuthorityAsync between initial content inspections | Receipt revision after first content read refuses before second status boundary | I |
| PC-140 | `WorktreeGuardedCleanupTests` | `C443_InitialFinalAuthorityAfterSlot` | Move final AuthorityAsync before initial slot-intent save | Receipt invalidated at slot commit refuses with spent slot and zero first commands | I |
| PC-141 | `WorktreeGuardedCleanupTests` | `C443_RetryFirstInspectionRequired` | Reuse initial accepted inspection at retry first content boundary | Change introduced after delay refuses at first retry status boundary; no later status/second remove | I |
| PC-142 | `WorktreeGuardedCleanupTests` | `C443_RetrySecondInspectionRequired` | Reuse retry first inspection at retry second content boundary | Change at retry second status refuses before retry slot; first failure/capture remain | I |
| PC-143 | `WorktreeGuardedCleanupTests` | `C443_RetryFirstIgnoredBoundary` | Remove retry first ignored-content refusal | Ignored sentinel introduced at retry first status refuses before retry authority refresh | I |
| PC-144 | `WorktreeGuardedCleanupTests` | `C443_RetrySecondIgnoredBoundary` | Remove retry second ignored-content refusal | Ignored sentinel introduced at retry second status refuses before retry slot | I |
| PC-145 | `WorktreeGuardedCleanupTests` | `C443_RetryAuthorityRefreshRequired` | Skip AuthorityAsync between retry content inspections | Fresh receipt invalidation after retry first status refuses before retry second status | I |
| PC-146 | `WorktreeGuardedCleanupTests` | `C443_RetryFinalAuthorityAfterSlot` | Move final AuthorityAsync before retry-intent save | Receipt changed at retry-intent commit leaves both slots spent but second command count zero | I |
| PC-147 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_GenuineLease` | In the shared preflight, replace leases.Owns result with true | For forged/missing/disposed or foreign-common-directory lease, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-148 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_CommonDirectory` | In the shared preflight, remove common-directory equality from AuthorityAsync | For resolved common directory differs from recorded common, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-149 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ValidOids` | In the shared preflight, bypass IsOid validation | For invalid/abbreviated source or target OID, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-150 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_DistinctRefs` | In the shared preflight, remove source==target refusal | For equal source/target references with equal SHA, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-151 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ReceiptExists` | In the shared preflight, substitute a cached accepted receipt when ReadAsync returns null | For removed/uncommitted receipt, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-152 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ReceiptActive` | In the shared preflight, remove op.Active check | For receipt deactivated in another context, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-153 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_PublishedReceipt` | In the shared preflight, skip HasPublication(op) in removal authority | For RemoteConfirmedAt cleared while other coordinates remain valid, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-154 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ReceiptTask` | In the shared preflight, remove op.TaskId equality | For request TaskId differs from receipt TaskId, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-155 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ReceiptSourceRef` | In the shared preflight, remove op.SourceFullRef equality | For request source ref changed to equal-SHA branch, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-156 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ReceiptTargetRef` | In the shared preflight, remove op.TargetFullRef equality | For request target ref changed to equal-SHA branch, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-157 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ReceiptTargetSha` | In the shared preflight, remove op.TargetBeforeSha equality | For request target-before SHA changed, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-158 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ReceiptDeletionSha` | In the shared preflight, remove op.ExpectedDeletionSha equality | For only receipt ExpectedDeletionSha changed, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-159 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ReceiptVerifiedSha` | In the shared preflight, remove op.VerifiedSourceSha equality | For request ExpectedSourceSha and receipt ExpectedDeletionSha set to different valid commit while verified receipt remains valid, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-160 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_CleanupIntent` | In the shared preflight, remove CleanupStartedAt nonnull check | For receipt CleanupStartedAt cleared, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-161 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_CleanupPhase` | In the shared preflight, remove receipt phase predicate | For valid publication receipt phase changed to PublicationConfirmed, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-162 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ReceiptRepository` | In the shared preflight, remove repository path equality | For equal-ref independent repository supplied, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-163 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ReceiptWorktree` | In the shared preflight, remove worktree path equality | For different recorded worktree supplied, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-164 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ReceiptGitDirectory` | In the shared preflight, remove GitDirectory equality | For request GitDirectory differs, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-165 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_ReceiptCommonDirectory` | In the shared preflight, remove op.CommonDirectory equality | For only recorded receipt CommonDirectory differs, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-166 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_CurrentTask` | In the shared preflight, remove task.ActiveLandingId check in WorktreeRemovalEvidence | For separately committed active operation pointer changed, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-167 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_TaskSucceeded` | In the shared preflight, remove Succeeded predicate in WorktreeRemovalEvidence | For separately committed task status changes to Working, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-168 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_TaskCoordinates` | In the shared preflight, remove WorktreeBranch equality in WorktreeRemovalEvidence | For task source branch changes after inspection, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-169 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_TaskTarget` | In the shared preflight, remove MergeTargetRef equality in WorktreeRemovalEvidence | For task target ref changes after inspection, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-170 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_TaskRepository` | In the shared preflight, remove task RepoPath equality in WorktreeRemovalEvidence | For task RepoPath changes after inspection, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-171 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_TaskPath` | In the shared preflight, remove task WorktreePath equality in WorktreeRemovalEvidence | For task WorktreePath changes after inspection, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-172 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_TaskNotSourced` | In the shared preflight, remove SourceLandingOperationId exclusion in WorktreeRemovalEvidence | For task gets a SourceLandingOperationId while request purpose remains Publication, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-173 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_TaskNotMutation` | In the shared preflight, remove task.Role Mutation exclusion in WorktreeRemovalEvidence | For task Role changes to Mutation with source-landing ID null, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-174 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_OriginalPin` | In the shared preflight, skip source pin verification | For source recovery pin missing or moved, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-175 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_TargetPin` | In the shared preflight, skip target-before pin verification | For target-before recovery pin missing or moved, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-176 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_PreparedPin` | In the shared preflight, skip prepared pin verification | For prepared pin missing/moved on actual rebased source, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-177 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_RemoteContainment` | In the shared preflight, accept observed.ContainsSource false | For remote target loses source after first failure, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-178 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_RemoteRead` | In the shared preflight, ignore observed.Reason | For failed/timed-out/changed-endpoint remote observation, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-179 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_InspectionHead` | In the shared preflight, remove snapshot.HeadSha equality in Matches | For new committed HEAD after capture, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-180 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_InspectionCommon` | In the shared preflight, remove snapshot.CommonDirectory equality in Matches | For source canonical common identity differs, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-181 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_InspectionAdmin` | In the shared preflight, remove snapshot.GitDirectory equality in Matches | For source canonical admin identity differs, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-182 | `AgentTaskLandRemovalMatrixTests` | `C443_Authority_AcceptedInspection` | In the shared preflight, remove result.Accepted term in Matches while returning false for null snapshots | For nonaccepted inspection with populated snapshot for detached/switched branch, locked/prunable/duplicate registration, sequencer or dirty/untracked/submodule source, targeted guard refuses before the next recorded I/O boundary; no subsequent destructive command; source/index/ref sentinels and remote proof retained. Run initial and retry positions separately | F |
| PC-183 | `WorktreeGuardedCleanupTests` | `C443_UnregisteredRootPreserved` | Permit directory removal when registration list has no target | Initial unregistered root has zero commands/captures; root unregistered after failed first command has no second remove; retained bytes unchanged | I |
| PC-184 | `WorktreeGuardedCleanupTests` | `C443_MissingRegisteredRootPreserved` | Treat directoryGone as sufficient even with registration present | RegistrationGone false, IsClean false; no ref deletion/repair/prune | I |
| PC-185 | `WorktreeGuardedCleanupTests` | `C443_DirectoryPostcondition` | Set DirectoryGone=true from exit code zero | Successful-but-incomplete command with retained root returns nonclean; no branch deletion | I |
| PC-186 | `WorktreeGuardedCleanupTests` | `C443_RegistrationPostcondition` | Set Unregistered=true from exit code zero | Retained registration after zero exit returns nonclean; no branch deletion | I |
| PC-187 | `WorktreeGuardedCleanupTests` | `C443_BranchAuthorityFresh` | Skip post-directory AuthorityAsync before ref delete | Receipt/pin/remote invalidated after second remove preserves branch; no update-ref -d | I |
| PC-188 | `WorktreeGuardedCleanupTests` | `C443_RecreatedSourceStopsBranch` | Skip source recreation check before ref deletion | Directory recreated after successful remove retains source branch and replacement sentinel | I |
| PC-189 | `WorktreeGuardedCleanupTests` | `C443_CheckedOutBranchPreserved` | Ignore matching branch in refreshed registrations | New checkout after removal retains branch and all new checkout bytes | I |
| PC-190 | `WorktreeGuardedCleanupTests` | `C443_BranchDeleteUsesOldSha` | Omit expected old SHA argument in update-ref delete | Exact argument vector includes expected SHA; branch advanced at deletion barrier remains at raced SHA and result nonclean | I |
| PC-191 | `WorktreeGuardedCleanupTests` | `C443_BranchDeleteNoDeref` | Remove --no-deref argument | Exact update-ref vector includes --no-deref; symbolic replacement cannot remove target ref | I |
| PC-192 | `WorktreeGuardedCleanupTests` | `C443_BranchAbsentConfirmed` | Set BranchDeleted=true after delete exit zero without show-ref | Ref recreated at post-delete barrier yields nonclean; retained ref SHA observed | I |
| PC-193 | `WorktreeGuardedCleanupTests` | `C443_BranchNotRetried` | Loop update-ref after first nonzero exit | Exactly one update-ref delete; branch failure/residue retained even when probe32 exists | I |
| PC-194 | `WorktreeGuardedCleanupTests` | `C443_NoCleanupRescue` | After failed Git call File.Delete on owned sentinel instead of retaining residue | Sentinel hash unchanged and no filesystem mutation/prune/reset/attribute-clear command in refusal trace; separate ordinary Git vector remains exact | I |
| PC-195 | `WorktreeGuardedCleanupTests` | `C443_CheckpointUsesRemainingBudget` | Pass unbounded token to first-failure save | Held journal save observes original deadline cancellation; no capture/retry starts after expiry | I |
| PC-196 | `WorktreeGuardedCleanupTests` | `C443_CaptureSaveUsesRemainingBudget` | Pass unbounded token to capture save | Held capture save observes original deadline; zero second removal | I |
| PC-197 | `WorktreeGuardedCleanupTests` | `C443_AuthorityUsesRemainingBudget` | Pass unbounded token to retry AuthorityAsync | Held fresh receipt/read/fetch/ancestry observes original deadline; zero second removal | I |
| PC-198 | `WorktreeGuardedCleanupTests` | `C443_InspectionUsesRemainingBudget` | Pass unbounded token to retry InspectAsync | Held first/second status observes original deadline; zero second removal | I |
| PC-199 | `WorktreeGuardedCleanupTests` | `C443_RetryGitUsesRemainingBudget` | Pass outer token instead of linked remaining token to second RunAsync | Recorded second Git token cancels at original ten-second deadline; owned command/readers subsequently joined | P |
| PC-200 | `AgentTaskWorktreeLockOutcomeTests` | `C443_BudgetExpiryStillSettles` | Use the already-canceled additional-work token for terminal transaction | With outer token live, cleanup residue and interruption/budget reason commit and reach UserPrompt despite expired extra allowance | I |
| PC-201 | `WorktreeGuardedCleanupTests` | `C443_BothGitChildrenJournaled` | Exclude remove from LandingGit mutating-command predicate | At each real owned Git launch, before result/drain, standing child journal exists with actual PID/start identity; journals clear only after join | P |
| PC-202 | `WorktreeGuardedCleanupTests` | `C443_UncertainChildJournalFencesRecovery` | Ignore an unfinished remove journal in repository admission | After owned worker death, fresh lease acquisition refuses; no capture/retry/reconciliation mutation until established child drain/owned recovery | P |
| PC-203 | `WorktreeCleanupJournalTests` | `C443_JournalScopeLifetime` | Cache the first scope DbContext in journal singleton | After disposing first caller scope a second journal read succeeds with fresh committed row; ValidateScopes passes | I |
| PC-204 | `AgentTaskWorktreeLockOutcomeTests` | `C443_OutcomeDestinationImmutable` | Re-read ParentSessionId from task on recovered enqueue | Edited task destination cannot redirect original Outcome; original session has matching UserPrompt, new session has none | I |
| PC-205 | `AgentTaskWorktreeLockOutcomeTests` | `C443_OutcomeQueuePayloadImmutable` | Allow existing SourceLandNotificationId row to change Body/digest | Collision rejects; original queue ID/body/digest/session unchanged and its original complete UserPrompt confirms | I |
| PC-206 | `AgentTaskWorktreeLockOutcomeTests` | `C443_ReceiptNeedsAttempt` | Remove DeliveryAttempts > 0 condition | Before any submit, planted matching transcript does not confirm; after actual attempt/floor complete body confirms | I |
| PC-207 | `AgentTaskWorktreeLockOutcomeTests` | `C443_ReceiptRejectsQueuedKinds` | Remove Kind == UserPrompt predicate | QueueEnqueue/QueuedUserPrompt containing exact body after floor leave ConfirmedAt null | I |
| PC-208 | `AgentTaskWorktreeLockOutcomeTests` | `C443_ReceiptNeedsKnownFloor` | Remove return when both attempt sequence and start time are null | Exact later body with no trustworthy floor leaves receipt unconfirmed | I |
| PC-209 | `AgentTaskWorktreeLockOutcomeTests` | `C443_ConfirmedReceiptNoReplay` | Clear queue identity and enqueue a copy after loading a Confirmed note | Second worker pass after receipt commit preserves one submission/one matching UserPrompt and original confirming sequence | I |
| PC-210 | `AgentTaskWorktreeLockOutcomeTests` | `C443_SummarySixHundredLimit` | Raise diagnostic display summary cap to 601 | 599/600/601-character summaries persist at <=600 independently of 32KiB JSON and 950-character stage cap | I |
| PC-211 | `AgentTaskWorktreeLockOutcomeTests` | `C443_StageNineFiftyLimit` | Raise composed Cleanup detail cap to 951 | 949/950/951-character composed detail persists <=950 even though StageOutcome allows1000; minimum evidence fields remain | I |
| PC-212 | `AgentTaskWorktreeLockOutcomeTests` | `C443_ProtocolWiresCurrentCleanupContext` | Omit typed cleanup context when CleanupAsync invokes WorktreeManager | Real first failed receipt-backed protocol removal creates current-request journal attempt and Captured/Interrupted reference; cannot silently use context-free path | I |
| PC-213 | `WorktreeGuardedCleanupTests` | `C443_InitialCleanHasNoCapture` | Collect diagnostics before the first removal's success check | Initial clean result has CaptureState NotNeeded, zero Handle/probe calls, zero delay, one consumed slot and no retry intent | I |
| PC-214 | `AgentTaskLandRemovalMatrixTests` | `C443_AuthorityReaderIgnoresTrackedState` | Read the already tracked operation/task instead of opening WorktreeRemovalEvidence's fresh scope | Unsaved tracked approval never authorizes removal; separately committed task/receipt invalidation is seen before next I/O; zero subsequent destructive command | I |
| PC-215 | `WorktreeGuardedCleanupTests` | `C443_CoordinatorOuterCancellation` | Catch caller OperationCanceledException as additional-budget exhaustion and return residue | Caller cancellation propagates at failure save, capture, delay, both retry preflight reads and second Git; no later command; budget-only cancellation still settles residue | I |
| PC-216 | `WorktreeDeleteAccessProbeTests` | `C443_NativeOuterCancellation` | Catch caller OperationCanceledException and return Unavailable | Caller cancellation throws with matching token before/during enumeration and opens; successful owned handle is disposed; no later open | U |
| PC-217 | `WorktreeLockDiagnosticsWindowsTests` | `C443_NativeFileSharing32` | Set SetLastError=false on the CreateFileW interop declaration | After seeding a distinct P/Invoke last-error value, the real owned-file DELETE open reports native error32; target bytes unchanged; no Handle/elevation prerequisite | P |
| PC-218 | `WorktreeLockDiagnosticsTests` | `C443_DiagnosticsNeverUseShell` | Set ProcessStartInfo.UseShellExecute=true | Recording process boundary has UseShellExecute == false before launch; configured executable and argument array retained | U |

### Out of scope

- No repeated filesystem delete passes, force, prune, reset/repair, attribute
  clearing, registration rescue or owner termination in production. The removed
  four-pass/three-delay controls are replaced by PC-20/21 for two Git slots and
  one 250 ms delay. PC-194 guards against reintroducing filesystem rescue.
- No changes to janitor, Windmill/build-junk retention, ignored-content ownership,
  source approval/publication algorithms, verification-snapshot custody or native
  terminal input policy. Their full existing matrices remain owned elsewhere.
  R-1/R-2 and the explicitly inventoried entry/freshness guards protect this seam.
- No live production repository deletion, synthetic gateway traffic, provider
  account access, automatic elevation or diagnostic binary installation.
  All repros use owned repositories/children/cloned databases.
- Error33 is deterministic classification coverage; actual Windows error32
  must come from the real nonmutating probe. Neither names/PIDs nor that probe
  identify the native cause of Git's own failure. A snapshot timestamped after
  Git failure cannot reconstruct a vanished conflict.
- No claim that the historical blocker is identified, that automatic Handle
  capture works under the deployment identity without qualification, or that
  transient Windows failures usually leave an intact retryable checkout.
  These are explicit acceptance limits, not missing retry-authority seams.
- The complete Cartesian product of every invalid guard with every other invalid
  guard is excluded: one independently armed invalidity per position proves the
  refusal. Mandatory combinations retained are every content/ignored boundary,
  every authority predicate in Initial/Retry, supported/privileged/configured
  provider states, budget/size boundaries, partial+positive native observation,
  and all defined busy/eligible delivery cuts.
- No new standalone diagnostic message. Journal capture and lifecycle logs are
  secondary evidence; the existing immutable Outcome owns caller receipt.
  Pointer-only transcript evidence and manually copied payloads cannot qualify
  the new complete diagnostic Outcome.

### Cost

All minutes below are **estimated planning floors**, not measurements. The
historical 196.8-minute total and 68-control count are superseded. No application
build/test, Handle/native qualification or PC cycle ran in this TestDesign turn.

Code's ordinary floor uses the named filters above. Build once and reuse its
output for subsequent classes; fresh execution/TRX remains mandatory.

| Code item | Minutes |
|---|---:|
| Tool/fixture preflight, parent-owned cloned database, isolated output inventory and initial build | 10.0 |
| Unit lane | 3.0 |
| Named provider/native methods | 6.0 |
| Guarded cleanup and authority/content matrix | 12.0 |
| Journal/restart and ten physical JC cuts | 14.0 |
| Outcome, direct recipients and 36 DC cases | 16.0 |
| Lifecycle/owned verifier | 8.0 |
| Existing named R-only checks and graph census | 7.0 |
| Eight explicit Windows qualification cases including Handle controls | 12.0 |
| CLI migration upgrade/idempotence/legacy-row test | 4.0 |
| **Ordinary V/R execution subtotal (Code)** | **82.0** |
| **Code setup/build + ordinary V/R floor** | **92.0** |

Per-PC floor includes incremental build on red (0.6), exact-method red run,
restore/timestamp verification (0.1), incremental build on green (0.6), and
the same exact-method green run. U = deterministic I/O, I = scoped integration,
F = two-position authority integration, P = owned native children/drain,
DB = model plus CLI-generated mutation migration. The two-position F time
already includes both Initial and Retry arguments; it is not charged as one
argument. DB includes generating/applying the mutation migration in its test time.


| Mutation lane | PCs | Red test + green test per PC | Build + restore per PC | Per-PC total | Lane minutes |
|---|---:|---:|---:|---:|---:|
| U | 55 | 0.1 + 0.1 | 1.3 | 1.5 | 82.5 |
| I | 117 | 0.5 + 0.5 | 1.3 | 2.3 | 269.1 |
| F | 36 | 1.5 + 1.5 | 1.3 | 4.3 | 154.8 |
| P | 8 | 2.0 + 2.0 | 1.3 | 5.3 | 42.4 |
| DB | 2 | 2.5 + 2.5 | 1.3 | 6.3 | 12.6 |
| **All PC red/restore/green cycles** | **218** | | | | **561.4** |
| Mutation snapshot/output preflight and initial build | | | | | 8.0 |
| Discovery and exact-method/TRX inventory checks | | | | | 4.0 |
| External evidence/restoration/output manifest finalization | | | | | 6.0 |
| **Mutation floor** | | | | | **579.4** |
| **Total verification floor: Code 92.0 + Mutation 579.4** | | | | | **671.4** |

This is approximately 11.2 hours of verification floor, excluding authoring,
ordinary Review, deployment and investigation/repair of failures. It is a
serial estimate; no uncommissioned shards or subagents are assumed. New timing
evidence may revise estimates, never drop guards to fit the former budget.

Quantified scoping savings (estimated, not measured): per-PC exact-method test
execution totals 278.0 minutes. A deliberately class-wide red+green
counterfactual for the same 218 controls totals 3682.2 test minutes using these
whole-class assumptions: provider4, native3, guarded12, journal8, outcome12,
lifecycle6, removal-matrix10, WorktreeManager2, stage-outcome3,
DelegationTestServices0.1, receipt3 and Windows12 minutes per run. Thus method scoping
saves 3404.2 test minutes (92.5%) in that counterfactual; the same 283.4 minutes
of per-cycle build/restore is excluded from both comparison sides.
Code's single initial build avoids 14 repeated builds across 15 named class
invocations: 14.0 minutes at an estimated one minute each. These savings
are relative to redundant work, not evidence of current host performance.
No full-suite saving is claimed: the testing owner says its present duration
is unmeasured. Full assembly remains CI/nightly; affected classes are bounded here.

Capture exact producer-owned bin-c443 outputs before/after the run. Before any
recursive cleanup resolve each absolute path, check it remains inside the
assigned workspace, ensure it belongs to this run and remove only those outputs
using native PowerShell LiteralPath operations. Await every foreground command;
do not run Antiphon.Tests concurrently with Antiphon.Agents.Pty.Tests, edit source
under a test run, or delete outputs held by the daemons. Sourced Mutation stores
TRX/evidence/restoration externally and follows its separate cleanup contract.

Before handoff audit: **bodies read; guards=218, mapped=218, missing=0,
duplicate PC mappings=0; all 218 PCs have executable method-scoped specifications
and concrete compiling defects; 217 distinct target methods; no B-marked
obligation remains.** Old B controls 10..16,19..24,40,43,68 retain their numbers
with revised executable targets. Additional guards are 69..218; no old guard
was silently discarded. Implementation, ordinary V/R and post-land red/green
evidence are still owed; this appendix records design completion only.

Next: **code**. Implement S1..S5 and the specified tests together; run ordinary
V/R and report precise Windows qualification/substitution limits. Return to
ordinary Review before land, retaining every PC for SourceLanding Mutation.
