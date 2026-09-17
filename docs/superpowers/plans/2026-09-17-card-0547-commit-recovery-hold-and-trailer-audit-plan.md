# CARD-0547: hold and record unresolved commit-recovery obligations; audit gate trailers through Git's parser

Plan task `91773d94`, 2026-09-17, inspected checkout `220988d7` (master). Investigation:
[2026-09-17-card-0547-commit-recovery-orphaned-and-audit-trailer-mismatch.md](../../investigations/2026-09-17-card-0547-commit-recovery-orphaned-and-audit-trailer-mismatch.md)
(task `b355c108`). Filed from CARD-0527 round-11 review. Both gaps are P3; both are confirmed on
master and untouched by CARD-0544.

Terminology correction carried from the investigation: the card's "Retry/Continue" reads
**"Retry (and the reroute/escalate requeues)"** throughout. `-Continue` requires `Blocked` and a
`Blocked` settlement never creates an obligation, so it cannot orphan one.

## Disposition in four lines

1. **Gap 1 is a missing reader, not a missing writer.** The `CommitRecoveryStarted` obligation is
   durable and correct; nothing outside `TryCommitOnSettleAsync` reads it. One static rule plus one
   loader (`CommitRecoveryObligations`, D-1) becomes the reader for the watchdog, the reconciler,
   every requeue, the attention projection and settlement itself.
2. **Holding is bounded, then honest.** The overdue watchdog and the dead-session reconciler hold a
   task whose obligation is younger than `Delegation:CommitRecoveryHoldMinutes` (default 720) and
   surface it as an Error attention row; past the hold they fail the task, close the obligation with
   a `CommitRecoveryAbandoned` row that names the sweep, and carry the event id, settlement digest
   and the `git log` recipe into the parent's failure note (D-3, D-5). Requeues refuse with 409
   `commit_recovery_pending` unless the caller abandons explicitly (D-4).
3. **`CommitFailed` gets its terminal resolution** only after a verified empty settlement search
   (D-2); a NotNeeded row stays a proof, never a guess.
4. **Gap 2 is a spelling bug fixed by not spelling.** The per-SHA trailer parse in
   `FindGatedCommitsAsync` is lifted into `GitWorkspaceService.ReadTrailersAsync`; the Commit-child
   audit asks Git's parser for the exact `antiphon-commit == gated` trailer (D-6). The
   `antiphon-task == child.Id` cross-check is rejected as a false-positive risk in a shared checkout.

## Ground truth

| Card/investigation assumption | Observed on `220988d7` | Consequence |
|---|---|---|
| Three paths orphan an unresolved obligation: overdue watchdog, dead-session reconciler, human Retry. | `TryFailOverdueAsync` gates 1-4 (`AgentTaskDispatcher.cs:1719-1770`) read `ApiErrorRecoveries`, the runner transcript, bind refusal and the boot stall; none reads `AgentTaskEvents`; `:1781` fails. Reconciler `:1599-1618`: `OnTurnEndAsync` last chance, reload, fail. `RetryAsync` (`AgentTaskService.cs:1854`) refuses only `Queued`, then `RequeueAsync` (`:2286`) nulls `AgentSessionId`/`DispatchedAt` so the stored digest can never match again. | D-3 gates in both sweeps; D-4 guard in `RequeueAsync`. |
| "Retry/Continue". | `ContinueWithAuthorityAsync` (`AgentTaskReplyService.cs:385-398`) requires `Blocked`; `CommitOnSettleEligibility.IsEligible` requires `Succeeded`. `RequeueAsync` has four callers: Retry `:1854`, Reroute `:1957`, Escalate `:2055`, wall reroute `:2178`. | Correction adopted; the guard lives in `RequeueAsync` so all four are covered by one line of code. |
| `CommitFailed` is never resolved. | `:3310-3319` writes `CommitRecoveryNotNeeded` for `NothingToCommit`, `IgnoreRulesChanged`, `IgnoredPathStaged`, `RepositoryBusy` only. `GatedCommitService.CommitHeldAsync` returns `CommitFailed` from nine sites: eight before `git commit` runs (status, check-ignore, index capture, stage, three post-stage refusals, index restore) and one on `git commit`'s non-zero exit (`:190`). | D-2: resolve after `FindSettlementCommitsAsync` proves no commit; never unconditionally. |
| Only `TryCommitOnSettleAsync` reads types 34/35. | `git grep CommitRecoveryStarted -- 'server/*.cs'`: `AgentTaskReplyService.cs` and the enum only. `recoveryStarted` (`:3157-3162`) is digest-scoped: `Detail == settlement` and no NotNeeded row whose Detail starts with `"{id:D} "`. | D-1 keeps the prefix convention (`"{startedId:D} "`) for every resolution row so the existing predicate and the new rule agree. |
| The `Committed` event closes the obligation. | `RecordCommitted` (`:3383-3391`) writes `Committed` with Detail `"{sha7} {files}"` in the outer context; it does not reference the obligation. Both rows share the settlement's `now`. | D-1 resolves an obligation by a `Committed` row with `At >= StartedAt` (same instant on the success path) rather than adding a fourth event type. |
| The failure note can carry a git fact. | `FailAndNotifyAsync` (`AgentTaskDispatcher.cs:2255-2290`) calls `BuildCompletionNote(task, _settings, reason, land: ...)`; the formatter already accepts `git:` and `warning:` (`DelegationReportFormatter.cs:605-610`, rendered `:645-646`, `:663-664`). `LandCompletionFacts.LoadAsync` is null for Shared tasks. | D-3: `FailAndNotifyAsync` gains an `orphaned` parameter and renders `git=commit-recovery-abandoned:<id8>` plus a warning paragraph. |
| The auto-escalate sweep would break on a 409. | `AutoEscalateStalledAsync` wraps `_tasks.EscalateAsync` in `catch (Exception) ... LogDebug ... skipped` (`:1068-1080`). | D-4: automatic requeues pass `abandonCommitRecovery: false`; the 409 is logged and the task holds. |
| Retry has a request body. | `POST /{id:guid}/retry` takes `(Guid id, AgentTaskService, ct)` only (`AgentTaskEndpoints.cs:101-104`); Escalate's optional body `EscalateAgentTaskRequest?` (`:113-117`, DTO `AgentTaskDtos.cs:544`) is the precedent; client `useRetryAgentTask` posts `{}` (`client/src/api/agentTasks.ts:714`); `scripts/delegate.ps1` has no `-Retry`. | D-4: optional `RetryAgentTaskRequest(bool AbandonCommitRecovery = false)`; no script change. |
| Attention kinds are appended, never renumbered. | Highest `AttentionKind` is `DispatchWarningUnconfirmed = 40` (`AttentionDtos.cs`); first-match order in `BuildOpenTaskItemsAsync` starts at `DeadSession` (`AttentionService.cs:825`); the client union is `client/src/api/attention.ts` and visuals `client/src/features/attention/attentionVisuals.ts`. | D-5: `CommitRecoveryPending = 41`, evaluated before `DeadSession`. |
| A setting home exists. | `DelegationSettings.CommitOnSettle` (`DelegationSettings.cs:45`); `ReportSweepRehandSeconds = 60` (`:513`); `DefaultTimeoutMinutes = 240` (`:379`). | `CommitRecoveryHoldMinutes` sits beside `CommitOnSettle`. |
| The audit matches a spelling Git never writes. | `AuditCommitChildAsync` (`AgentTaskReplyService.cs:3465-3472`): `CommitMessageAsync` (`%B`) then `Contains("antiphon-commit: gated") || Contains("antiphon-commit=gated")`. Git writes `key= value` under `trailer.separators = =` (investigation, Git 2.50.1). | D-6. |
| The resolver's parse is lift-able. | `FindGatedCommitsAsync` (`GitWorkspaceService.cs:971-988`): `log -1 --format=%(trailers:only,unfold,key_value_separator=%x00,separator=%x00)`, NUL split, parity check, local `Exact(key, value)`. `RunAsync` is `protected internal virtual` (`:992`), the `RecordingGitWorkspaceService` seam. | D-6: `ReadTrailersAsync` + `HasExactTrailer`; the resolver consumes them unchanged. |
| The audit has no separator test. | Only `C527_commit_child_audit_flags_a_commit_without_the_gate_trailer` (`AgentTaskReplyC527Tests.cs:491-517`, default separators). `Recovery_uses_Git_trailer_separators` (`GatedCommitTrailerTests.cs`, `:`/`=`/`%`) covers the resolver, not the audit. | S1 adds the audit variants and a body-prose case. |
| Test seams exist for every new control. | `ScratchGitRepo.InstallFailingPreCommitHookAsync` (`ScratchGitRepo.cs:80`) produces a real `CommitFailed`; `ThrowOnceSaveInterceptor` (`TestHelpers`) fails the terminal save after the commit; `AssertC527RecoveryPendingAsync` (`AgentTaskReplyC527EmptyRecoveryTests.cs:135`) pins the pending shape; `AgentTaskOverdueDeadlineTests.CreateHarness()` (`:454`, `TimeProvider.System`, ceiling 100 000 min) and `AgentTaskDeadSessionReconciliationTests.Scenario.Harness()` (`:602`, `FakeTimeProvider`) build the sweeps. | Slices S2/S3 name them. |
| Live precondition. | Zero rows of types 33/34/35 in production; 118 failures across the three paths since 2026-08-09. | Severity stays P3; the design is exercised only by tests until the gate commits in production. |

## Decisions

### D-1. `CommitRecoveryObligations`: one rule, one loader, no new table

`server/Application/Services/CommitRecoveryObligations.cs`, a `public static class`:

```csharp
public sealed record Pending(Guid EventId, string Settlement, DateTime StartedAt);

// Pure rule over one task's events (types 33..36). An obligation s (type 34) is unresolved
// unless: a CommitRecoveryNotNeeded (35) or CommitRecoveryAbandoned (36) row's Detail starts
// with $"{s.Id:D} ", or a Committed (33) row has At >= s.At.
public static IReadOnlyList<Pending> Unresolved(IEnumerable<AgentTaskEvent> events);

// AsNoTracking, one task; and one query for a set of tasks (attention projection).
public static Task<IReadOnlyList<Pending>> LoadUnresolvedAsync(AppDbContext db, Guid taskId, CancellationToken ct);
public static Task<IReadOnlyDictionary<Guid, IReadOnlyList<Pending>>> LoadUnresolvedAsync(
    AppDbContext db, IReadOnlyCollection<Guid> taskIds, CancellationToken ct);

// The type-36 row. Detail = $"{p.EventId:D} {by}: {reason}" (same "<started id> " prefix as NotNeeded).
public static AgentTaskEvent Abandon(Pending p, string by, string reason, DateTime now);

// Parent- and human-facing paragraph: event id, settlement digest, the recovery recipe, "nothing was pushed".
public static string Describe(Guid taskId, Pending p);
```

`Describe` renders the recipe the resolver itself uses, so a human can find the commit without
the server: `git log --all --reflog --fixed-strings --all-match --grep=<taskId:D> --grep=<digest> --format=%H`.

New enum member `AgentTaskEventType.CommitRecoveryAbandoned = 36` ("a terminal or requeue path
proceeded without recovering the obligation; Detail names the started event, the path and the
reason"). Appended; nothing renumbered.

`TryCommitOnSettleAsync` loads types 33..36 (today 34/35) and computes `recoveryStarted` as
`Unresolved(recoveryEvents).Any(p => p.Settlement == settlement)`. `nothingToCommit` is unchanged.
This is a no-behaviour refactor for every existing C527 case (no Abandoned rows exist there, and a
saved `Committed` row implies the status already flipped), and it makes an Abandoned obligation
invisible to a later same-digest re-entry, which is the intent.

**Why static + loader:** every consumer already holds an `AppDbContext`; there is nothing to mock;
the rule is testable as a unit. **Rejected:** a DI service (no dependency to inject); a new table
(the C527 tests pin the event shape and the rows already exist); a fourth "completed" event on the
success path (an extra write on every gated commit to record what `Committed` already says).

### D-2. `CommitFailed` resolves after a verified empty search

In `TryCommitOnSettleAsync`, when `result.Outcome == GatedCommitOutcome.CommitFailed`:

```csharp
var absent = await git.FindSettlementCommitsAsync(repo, task.Id, settlement, ct);
if (absent.Succeeded && absent.Items.Count == 0)
    // independent scope, same as the four existing no-op outcomes
    recoveryDb.AgentTaskEvents.Add(NewEvent(task.Id, CommitRecoveryNotNeeded, $"{recoveryAttempt.Id:D} {result.Outcome}", now));
```

A found commit or a failed search leaves the obligation open; the next re-hand recovers the commit
as `committed:` or holds under D-3. The Warning event and the Commit child spawn that follow
(`:3340-3360`) are unchanged.

**Why not unconditional:** eight of the nine `CommitFailed` sites precede `git commit`, and Git
leaves HEAD unchanged on a non-zero `commit`, but the same value comes back when the git process
is torn down mid-run; the existing comment at `:3172` is explicit that "only a durable, explicitly
known no-commit result clears it". One `git log --all --reflog` in a failure path is the price of
keeping NotNeeded a proof. **Rejected:** a distinct "CommitFailed" resolution type (NotNeeded's
Detail already carries the outcome name).

### D-3. Bounded hold in the watchdog and the reconciler, then an honest failure

New `DelegationSettings.CommitRecoveryHoldMinutes { get; set; } = 720`. `<= 0` disables the hold
(annotate-only; every terminal path still closes the obligation and carries it in the note).

**Overdue watchdog.** `TryFailOverdueAsync` gains Gate 1b, immediately after Gate 1 (API-error
recovery) and before the transcript pull, because it is DB-only and a held task must not cost a
runner round-trip per tick for twelve hours:

```csharp
var pending = await CommitRecoveryObligations.LoadUnresolvedAsync(_db, task.Id, ct);
if (pending.Count > 0 && hold > TimeSpan.Zero && UtcNow() - pending.Min(p => p.StartedAt) < hold)
{
    _logger.LogDebug("Task {ShortId} is past its deadline but holds an unresolved commit-recovery obligation {Event} (started {At}); leaving it to settlement", ...);
    return false;
}
```

and passes `orphaned: pending` to `FailAndNotifyAsync` at the existing `:1781` call.

**Dead-session reconciler.** The same test sits after the `OnTurnEndAsync` last chance (which is
itself a recovery attempt) and after `ClassifyFailure`, before `FailAndNotifyAsync` (`:1618`):
holding is `continue` without `_deadSessions.Forget` (the session is still dead; the grace
bookkeeping stays); expiry passes `orphaned: pending`.

**`FailAndNotifyAsync(task, reason, sweep, ct, failureCode = null, IReadOnlyList<Pending>? orphaned = null)`.**
When `orphaned` is non-empty: add `CommitRecoveryObligations.Abandon(p, sweep, reason, now)` for
each before `SaveChangesAsync` (same transaction as the Failed status); append one sentence to
`reason` naming the obligation id and digest (so `FailureReason` and the `RecentFailure` row carry
it); pass `git: $"commit-recovery-abandoned:{p.EventId:N}[..8]"` and `warning: Describe(...)` to
`BuildCompletionNote`. Shared tasks have no `land:` facts, so the `git=` bit is the note's only
git fact and is unambiguous.

**Why bounded at 720:** the watchdog fires at the 240-minute ceiling, so a hold shorter than a
working day still orphans a commit behind an overnight git outage; an unbounded hold lets a
systemic recovery bug fill every role slot silently, and the recovery path has produced zero
production rows to date. Twelve hours is seen at the next attention check (D-5) and expires before
the next nightly. The sweep's 60 s re-hand (`ReportSweepRehandSeconds`) is what actually resolves a
held task; the hold only stops the two sweeps from pre-empting it.

**Rejected:** (a) annotate-only with no hold: a transient git failure at the ceiling converts a
real `committed:` into `Failed`, the exact orphaning the card names; (b) unbounded hold: see
above; (c) stopping `OnTurnEndAsync` from swallowing `settlement_recovery_unavailable`: the
observer must survive settlement failures, and the re-hand is the retry; (d) holding in
`FailAndNotifyAsync` itself: the `grok-rules` and never-started callers have no obligation to
consult and the decision belongs where the sweep decides to fail; (e) a hold on `CancelAsync`:
a human cancel after the attention row is an informed terminal decision (§ Scope boundaries).

### D-4. `RequeueAsync` is the single requeue guard; abandonment is explicit

`RequeueAsync(task, type, level, detail, ct, bool abandonCommitRecovery = false)`, guard first,
before `StopDelegateAsync`:

- unresolved obligations and `!abandonCommitRecovery` → `throw new ConflictException(
  $"Task {short} has an unresolved commit-recovery obligation; retry with abandonCommitRecovery=true to discard it, or wait for settlement to recover the commit.",
  "commit_recovery_pending", extensions { obligationEventId, settlement, startedAt, holdExpiresAt })`;
- unresolved and `abandonCommitRecovery` → `Abandon(p, $"requeue:{type}", detail, now)` rows are
  added before the requeue's own `SaveChangesAsync`.

`RetryAsync(Guid id, CancellationToken ct, bool abandonCommitRecovery = false)`; endpoint
`POST /api/agent-tasks/{id:guid}/retry` takes an optional body
`RetryAgentTaskRequest(bool AbandonCommitRecovery = false)` (mirror of `EscalateAgentTaskRequest?`;
absent body is `false`). `RerouteAsync`, `EscalateAsync` and `RerouteOnWallAsync` pass the default:
none of them is a place a human decides to discard a commit, and the auto-escalate sweep's catch
turns the 409 into a Debug line and a hold. Client `useRetryAgentTask` accepts an optional
`abandonCommitRecovery` and sends it in the body; no new UI control (the attention row's evidence
carries the exact request).

**Rejected:** a separate `/commit-recovery/abandon` endpoint (two calls for one decision, and the
sweeps abandon inline); guarding only `RetryAsync` (misses the requeues the investigation named).

### D-5. `AttentionKind.CommitRecoveryPending = 41`, Error, evaluated first among open-task arms

In `BuildOpenTaskItemsAsync`: one `LoadUnresolvedAsync(db, openTaskIds, ct)` before the loop; arm 0
before `DeadSession` (it explains why every later arm is not being acted on). Membership: task in
`Dispatched`/`Working`, oldest unresolved obligation older than a fixed 2 minutes
(`CommitRecoveryVisibleAfter`, the `NeverStartedGrace` pattern, so the ordinary seconds-long
settlement never flashes). Row: `Severity = Error` (counted in Broken); `Title` = task title;
`Headline` = "Settled and committed under the gate; the record could not be saved. Held for
recovery {age} of {hold} min."; `Evidence` = event id, settlement digest, the `Describe` recipe,
hold expiry, and the abandon request; `SinceUtc = StartedAt`; `Actions = [OpenDrawer, Cancel]`
(Retry is deliberately absent: it would 409 until abandoned, and the evidence says how).

Client: add `'CommitRecoveryPending'` to the union in `client/src/api/attention.ts`, a visual entry
(label "Commit recovery pending", error colour, git-commit icon, hint) in `attentionVisuals.ts`,
and the kind to the exhaustive list in `attentionVisuals.test.ts`. `docs/antiphon-api.md` records
`= 41` under the append rule.

**Rejected:** reusing `Overdue` (a preview of a failure, not a hold); an incident/alert row (AGENTS.md:
a decision belongs on the attention feed, never an alert sink); showing closed tasks (the failure
path closes the obligation with provenance and the note carries it; `RecentFailure` already lists
the task).

### D-6. Gap 2: shared per-SHA trailer reader; the audit asks Git's parser

`GitWorkspaceService`:

```csharp
public sealed record GitTrailer(string Key, string Value);

// git log -1 --format=%(trailers:only,unfold,key_value_separator=%x00,separator=%x00) <sha>
// Empty block -> Succeeded, []. Odd field count -> Succeeded=false, ExitCode=-1, "Malformed parsed Git trailers."
public async Task<GitStrictList<GitTrailer>> ReadTrailersAsync(string repo, string sha, CancellationToken ct);

// The lifted Exact rule: exactly one key match (OrdinalIgnoreCase) and its value ordinal-equal.
public static bool HasExactTrailer(IReadOnlyList<GitTrailer> trailers, string key, string value);
```

`FindGatedCommitsAsync` consumes both; its results and failure modes are unchanged
(`Recovery_uses_Git_trailer_separators` and `GatedCommitRecoveryIdentityTests` stay green).

`AuditCommitChildAsync`: replace `CommitMessageAsync` + `Contains` with `ReadTrailersAsync`:
`!Succeeded` → Warning `commit {sha7} audit unavailable: {error}` (the existing unavailable style,
`:3427-3433`), never "outside the gate"; else `!HasExactTrailer(items, "antiphon-commit", "gated")`
→ the existing `commit {sha7} was made outside the gate` line. `CommitMessageAsync` stays for its
other callers.

**`antiphon-task == child.Id` is not added.** (1) `%(trailers:only)` reads Git's trailer block
only, so a body sentence mentioning `antiphon-commit: gated` is no longer accepted: the prose
false negative the investigation raised is closed by the parser alone. (2) The audited range
`CommitBaselineSha..HEAD` in a shared checkout legitimately contains other Shared tasks' tier-1
gated commits (`antiphon-task = <other>`) landing concurrently; requiring the child's id would
flag them "outside the gate", a false positive beside a REVERT line. (3) The child's own commits
go through `POST /{id}/commit` with `antiphon-task = child.Id`, so a mismatch is not a gate
violation. A hand-written trailer block is forgery, outside the audit's threat model and the
same acceptance the resolver has. If a later card wants it, the shape is one extra informational
line (`commit {sha7} was gated by task {other8}`), not a change to this predicate.

### D-7. Out of this card: the `-Refine` re-settlement path

A later settlement of the same session on a different report computes a different digest and
finds no obligation (investigation, "additional path"). With D-1's task-scoped loader the
condition is now visible (unresolved obligations whose digest differs from the current one), but
acting on it means choosing which commit the note describes, a CARD-0527 identity change.
Recorded in § Scope boundaries with the recommended shape; no code here.

## Components (exact)

| File | Change |
|---|---|
| `server/Domain/Enums/AgentTaskEnums.cs` | `CommitRecoveryAbandoned = 36` |
| `server/Application/Services/CommitRecoveryObligations.cs` (new) | D-1 |
| `server/Application/Settings/DelegationSettings.cs` | `CommitRecoveryHoldMinutes = 720` beside `CommitOnSettle` |
| `server/Application/Services/AgentTaskReplyService.cs` | `TryCommitOnSettleAsync`: load 33..36, `recoveryStarted` via the rule, D-2 CommitFailed resolution; `AuditCommitChildAsync`: D-6 |
| `server/Application/Services/AgentTaskDispatcher.cs` | `TryFailOverdueAsync` Gate 1b; `FailDeadSessionTasksAsync` gate; `FailAndNotifyAsync` `orphaned` |
| `server/Application/Services/AgentTaskService.cs` | `RequeueAsync` guard + flag; `RetryAsync` flag |
| `server/Application/Dtos/AgentTaskDtos.cs` | `RetryAgentTaskRequest(bool AbandonCommitRecovery = false)` |
| `server/Api/Endpoints/AgentTaskEndpoints.cs` | `/retry` optional body |
| `server/Application/Dtos/AttentionDtos.cs` | `CommitRecoveryPending = 41` |
| `server/Application/Services/AttentionService.cs` | arm 0 in `BuildOpenTaskItemsAsync` |
| `server/Application/Services/GitWorkspaceService.cs` | `GitTrailer`, `ReadTrailersAsync`, `HasExactTrailer`; `FindGatedCommitsAsync` consumes them |
| `client/src/api/attention.ts`, `client/src/features/attention/attentionVisuals.ts`, `attentionVisuals.test.ts` | kind 41 |
| `client/src/api/agentTasks.ts` | `useRetryAgentTask` optional `abandonCommitRecovery` |
| `docs/orchestration-loop.md` § Commit on settle | hold, abandon, 409, the note's `git=` bit |
| `docs/antiphon-api.md` | `/retry` body and 409 `commit_recovery_pending`; attention kind 41 |

## Slices

### S1. Gap 2: trailer reader and audit (independent; may land first)

Files: `GitWorkspaceService.cs`, `AgentTaskReplyService.cs` (`AuditCommitChildAsync`).
Tests: `tests/Antiphon.Tests/Application/GatedCommitTrailerTests.cs` (new unit-level cases on
`ReadTrailersAsync`/`HasExactTrailer` over `:`/`=`/`%`, empty block, duplicate key, odd field
count via `RecordingGitWorkspaceService.OverrideRun`); new
`tests/Antiphon.Tests/Application/AgentTaskReplyC547AuditTests.cs` (partial of
`AgentTaskReplyIntegrationTests`, reusing `SeedC527Async`/`C527Factory`): a gated commit under
each separator is not flagged; the existing no-trailer case still flags; a body-prose mention with
no trailer block flags; a trailer read that fails (`OverrideRun` on `log` with the `%(trailers`
format) yields `audit unavailable` and no `outside the gate`.

### S2. Gap 1 core: rule, loader, enum, setting, settlement

Files: `AgentTaskEnums.cs`, `CommitRecoveryObligations.cs`, `DelegationSettings.cs`,
`AgentTaskReplyService.cs` (`TryCommitOnSettleAsync`).
Tests: `tests/Antiphon.Tests/Application/CommitRecoveryObligationsTests.cs` (`[Category("Unit")]`,
the rule over hand-built event lists: unresolved; NotNeeded resolves; Abandoned resolves;
Committed at/after resolves; Committed before does not; prefix must be the full `{id:D} `);
`AgentTaskReplyC547RecoveryTests.cs`: `InstallFailingPreCommitHookAsync` → `CommitFailed` writes
`NotNeeded "{id} CommitFailed"`, a second `OnTurnEndAsync` neither throws nor holds; the same with
`OverrideRun` failing `--all` → no NotNeeded row and the obligation stays pending
(`AssertC527RecoveryPendingAsync`). Carried: every C527 recovery test.

### S3. Gap 1 paths: sweeps, requeue, endpoint, note

Files: `AgentTaskDispatcher.cs`, `AgentTaskService.cs`, `AgentTaskDtos.cs`,
`AgentTaskEndpoints.cs`, `client/src/api/agentTasks.ts`.
Tests: `AgentTaskOverdueDeadlineTests.cs` (seed a type-34 row on a task past the ceiling: held
within the hold, still `Working`, session not killed; failed after the hold with a type-36 row
whose Detail starts with the started id and names `overdue-task deadline`, `FailureReason` naming
the obligation, the parent note containing `git=commit-recovery-abandoned:` and the digest;
`CommitRecoveryHoldMinutes = 0` fails immediately with the same annotation);
`AgentTaskDeadSessionReconciliationTests.cs` (held; failed after the hold; the fake clock advances
the hold); `AgentTaskServiceIntegrationTests.cs` (Retry on a Dispatched task with a pending
obligation is 409 `commit_recovery_pending` with the event id in extensions and the delegate not
stopped; `abandonCommitRecovery: true` requeues, writes `requeue:Retried`, and the attention loader
returns nothing; Escalate on the same task is 409); an endpoint test that the body flag reaches the
service and that an absent body is `false`. End-to-end in `AgentTaskReplyC547HoldTests.cs`: seed
with `DispatchedAt` far past the overdue harness's ceiling **before** settling (the digest includes
`DispatchedAt`), settle a real gated commit under `ThrowOnceSaveInterceptor`, run
`FailOverdueTasksAsync` from a harness built like `AgentTaskOverdueDeadlineTests.CreateHarness()`
against the same database → still `Dispatched`; re-run `OnTurnEndAsync` → `committed:` recorded
and the obligation resolved by the `Committed` row.

### S4. Attention row, client, docs

Files: `AttentionDtos.cs`, `AttentionService.cs`, the three client files, the two docs.
Tests: `AttentionServiceTests.cs` (row present for an open task with a 3-minute-old obligation,
Error, `SinceUtc` = start, evidence carries the digest and recipe; absent within 2 minutes; absent
once a NotNeeded/Abandoned/Committed row resolves it; absent for a Failed task; a single query for
the open set); `attentionVisuals.test.ts` exhaustive-kind list.

Order: S1 ∥ S2 → S3 → S4. Commit and push each slice.

## Acceptance cases and guard candidates for TestDesign

IDs are prefixed `G-547-`/`PC-547-` to avoid collision with CARD-0527's G-1..G-31 and
CARD-0545's G-545-n. TestDesign finalizes methods and assertion sites; the PC sketches name the
mutation and the expected red.

| ID | Guards | Test (S) | PC sketch |
|---|---|---|---|
| G-547-1 | A gated commit under `trailer.separators` `=` or `%` is not flagged "outside the gate" | S1 audit variants | restore `Contains("antiphon-commit=gated")` in the audit → `=` variant red |
| G-547-2 | The audit reads Git's trailer block, not the body: a prose mention with no trailer block is flagged | S1 prose case | replace `HasExactTrailer` with `message.Contains(...)` → red |
| G-547-3 | A failed trailer read is `audit unavailable`, never `outside the gate` | S1 read-failure case | treat `!Succeeded` as an empty trailer list → red |
| G-547-4 | `HasExactTrailer` requires exactly one key occurrence | carried `Recovery_uses_Git_trailer_separators` (duplicate-key arm) + unit | accept `>= 1` occurrence → red |
| G-547-5 | Odd NUL-field count is malformed, not partial data | S1 unit | drop the parity check → red |
| G-547-6 | NotNeeded referencing the start resolves | S2 unit; carried C527 NoOp tests | require an exact Detail equality instead of the prefix → red |
| G-547-7 | Abandoned referencing the start resolves | S2 unit; S3 retry-abandon | ignore type 36 in the rule → red |
| G-547-8 | A `Committed` row at/after the start resolves; before it does not | S2 unit | drop the `At >=` arm → red |
| G-547-9a | `CommitFailed` writes NotNeeded after a verified empty search | S2 hook case | never write → second settle throws/holds → red |
| G-547-9b | `CommitFailed` with a failed search leaves the obligation open | S2 `--all` failure case | write unconditionally → red |
| G-547-10 | Overdue watchdog holds within the window; nothing killed | S3 overdue held | remove Gate 1b → Failed → red |
| G-547-11 | Past the window the watchdog abandons by name and the note carries `git=commit-recovery-abandoned:` and the digest | S3 overdue expired | skip the Abandon write → red; drop the `git:` bit → red |
| G-547-12 | Reconciler holds within the window | S3 reconciler held | remove the gate → red |
| G-547-13 | Reconciler past the window annotates identically | S3 reconciler expired | same as G-547-11 |
| G-547-14 | `CommitRecoveryHoldMinutes <= 0` disables the hold, not the annotation | S3 hold-zero | treat `<= 0` as infinite → red |
| G-547-15 | Retry refuses 409 `commit_recovery_pending` before stopping the delegate | S3 retry 409 | move the guard after `StopDelegateAsync` → stopper red; drop the guard → red |
| G-547-16 | `abandonCommitRecovery` requeues and writes `requeue:Retried` | S3 retry abandon | skip the write → red |
| G-547-17 | Escalate/Reroute share the guard | S3 escalate 409 | guard only in `RetryAsync` → red |
| G-547-18 | Endpoint body flag reaches the service; absent body is `false` | S3 endpoint | ignore the body → red |
| G-547-19 | Attention row present/absent per D-5 membership; one query | S4 | drop the resolution filter → resolved case red; drop the 2-minute gate → fresh case red |
| G-547-20 | Settlement's digest-scoped predicate is unchanged by the loader widening | carried C527 recovery tests | filter by task only, not digest → `C527_repeated_report_selects_this_settlements_notification` family red |
| G-547-21 | After a hold the re-hand records `committed:` and resolves the obligation | S3 E2E | hold + skip Gate 1b's `LoadUnresolvedAsync` → red |

## Carried-forward controls (must stay green, none renumbered)

`GatedCommitServiceTests.Recovery_uses_Git_trailer_separators`; `GatedCommitRecoveryIdentityTests`;
`AgentTaskReplyC527Tests.C527_commit_child_audit_flags_a_commit_without_the_gate_trailer` and
`..._flags_upstream_movement`; `AgentTaskReplyC527RecoveryTests.C527_unavailable_child_audit_is_reported_to_parent`;
every `AgentTaskReplyC527{Empty,Literal,NoOp,Inspection,MovedBranch,StagedManifest}RecoveryTests`
case (`AssertC527RecoveryPendingAsync` users pin the pending shape S2 must not change);
`AgentTaskOverdueDeadlineTests` (all 15, in particular `the_caller_is_told_rather_than_left_to_discover_it`);
`AgentTaskDeadSessionReconciliationTests`; `AgentTaskServiceIntegrationTests.retrying_a_*`,
`a_queued_task_cannot_be_retried`, `escalating_moves_one_rung_up_the_ladder`; `AttentionServiceTests`.

## Scope boundaries

- **`CancelAsync`** does not consult or close the obligation. A human cancelling after the
  attention row is an informed decision; the row stays as forensics and the recipe still finds
  the commit. Closing it there is a two-line follow-up if wanted.
- **`-Refine` re-settlement** (D-7): recommended shape for a later card is a report-independent
  attempt identity in the digest (`task|session|dispatchedAt` plus a per-attempt nonce stored on
  the row) so every settlement of an attempt sees the same obligation.
- **Attention UI affordance** for abandon: none; the API and the evidence text carry it.
- **`scripts/delegate.ps1`**: no `-Retry` exists; nothing added.
- **Informational "gated by another task" audit line** (D-6): not added.

## Cost and next stage

Code: S1 ≈ 0.5 d, S2 ≈ 0.5 d, S3 ≈ 1 d, S4 ≈ 0.5 d, sequential with S1 optionally parallel.
Mutation band after land: 21 guards, roughly 24 PCs (G-547-9 and G-547-11 carry two each).

TestDesign is a separate stage for this card: it finalizes the 21 guard rows above into test
methods and assertion sites, adds the delivery inventory for the failure note (producer
`FailAndNotifyAsync`, destination the parent session's `UserPrompt`), and settles the E2E harness
composition for S3 (one dispatcher and one reply service over the same test database).

## Stated defaults (decide only if the operator objects)

- D-3: `CommitRecoveryHoldMinutes = 720`; `<= 0` disables the hold.
- D-5: visible after a fixed 2 minutes; Error severity; actions `[OpenDrawer, Cancel]`.
- D-4: no new UI control for abandon; API body flag only.
- D-6: `antiphon-task == child.Id` not required by the audit.
