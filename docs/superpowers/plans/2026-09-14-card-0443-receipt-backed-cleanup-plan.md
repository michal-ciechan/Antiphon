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
the old raw `WorktreeManagerTests` held-file refusal is not a substitute.

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
