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

## Verification design (TestDesign, task 2b64d75a)

TestDesign dispatch 2026-09-17 on checkout `22f010a0` (master). Nothing below changes the fix
design (D-1..D-7); it finalizes the controls Code implements, settles the three things the plan
left to this stage, and adds the guards the candidate list bundled or left untested. Stage rules
every row follows:

1. IDs are `G-547-n`/`PC-547-n`. The plan's candidates G-547-1..21 keep their numbers; where a
   candidate bundled two independently bypassable checks it is split with a letter suffix
   (8a/8b, 9a/9b, 11a/11b, 15a/15b, 18a/18b, 19a/19b). G-547-22..48 are new. `PC-547-n` maps
   1:1 to `G-547-n`; two PCs may share a test method only when they break different code and go
   red at different named assertions.
2. Every integration test runs the real `GitWorkspaceService`/`GatedCommitService` against a real
   `ScratchGitRepo`, the real `AgentTaskReplyService`, `AgentTaskDispatcher`, `AgentTaskService`,
   `AttentionService` and `SessionMessageQueueService` over the shared test Postgres. The only
   fakes are the ones the existing suites already use: `RecordingGitWorkspaceService` (a spy over
   `RunAsync`), `FakeAgentProtocolAdapter` (the parent's terminal), `RecordingSessionStopper`,
   the dead-session suite's `FakeRunnerClient`, `FakeTimeProvider`, and the settlement-scoped
   `ThrowOnceSaveInterceptor` nested in `AgentTaskReplyIntegrationTests` (throws once on the
   settlement save, which is the only interceptor the C527 recovery tests use; the `TestHelpers`
   one throws on any first save and is not used here).
3. Obligation rows are produced by a real settlement wherever the test is about settlement or the
   re-hand (S2, S3 E2E). Sweep, requeue, endpoint and attention tests seed a type-34 row by hand
   through a scenario helper, because those consumers only read the row and the shape is pinned
   by D-1 (`Detail` = settlement digest, `At` = start). Every hand-seeded row uses a 64-hex
   lowercase digest so the recipe and the evidence have production length.
4. Delivery acceptance is the parent's `UserPrompt` transcript record, asserted through the
   existing `AssertParentReceivedNoteAsync` (row `Sent` + `Delivered` + digest match + the prompt
   text is the note body, exactly once). A queued row or `NoteHeader` alone is a substitute and is
   labelled as such wherever it is the only assertion (the sweep-level tests).
5. Time: the sweeps' hold arithmetic is exercised by back-dating rows (`At` = now - N minutes) and,
   in the reconciler, by advancing its `FakeTimeProvider`; nothing waits on a wall clock. The
   attention gate is pinned at exactly 120 s (absent) and 121 s (present) through the existing
   `ItemsForAsync(scenario, TimeProvider)` overload.
6. Method-scoped filters: `--treenode-filter "/*/*/<Class>/<Method>"`; Vitest:
   `pwsh -File scripts/test-client.ps1 attentionVisuals`.

### Settled here (the plan left these to TestDesign)

- **E2E harness composition (S3).** Two containers over one database, one git spy and one parent
  terminal. The reply side is the existing `C527Factory` (settlement, parent terminal via
  `AttachTerminal`, delivery via `Queue(factory).FlushSessionAsync`). The dispatcher side is a new
  shared builder `tests/Antiphon.Tests/TestHelpers/OverdueSweepHarness.cs` extracted from
  `AgentTaskOverdueDeadlineTests.CreateHarness()` verbatim (same 100 000-minute ceiling, 50 000
  model wait, 60 000 local execution, 40 000 boot wait, 30 repeat hold, `TimeProvider.System`),
  taking `Action<DelegationSettings>? configure`, `RecordingGitWorkspaceService? gitSpy` (passed
  to `AddDelegationWorktreeGraph(..., workspaceGit:)`) and a `claudeProjectsRoot` that defaults to
  an empty temp directory (the dead-session suite's guard against scanning the machine), and
  returning `Built(AgentTaskDispatcher Dispatcher, RecordingSessionStopper Stopper, ServiceProvider Provider)`.
  `AgentTaskOverdueDeadlineTests.CreateHarness()` becomes a two-line delegation so its 15 carried
  tests keep their `var (harness, stopper) = CreateHarness();` shape. The E2E lives in a new
  partial of `AgentTaskReplyIntegrationTests` (`AgentTaskReplyC547HoldTests.cs`) because that
  class is already global `[NotInParallel]` and `[ParallelLimiter<ProcessSpawnLimit>]`, which is
  what a fleet-global sweep needs, and because `SeedC527Async`/`C527Factory`/`AttachTerminal`/
  `AssertC527RecoveredReceiptAsync` are its private helpers. Other suites' open rows are at most
  400 minutes old and cannot reach the 80 000-minute preview gate, so the sweep run from here can
  only ever judge the row the test back-dated. The failure note enqueued by the dispatcher side
  (its runtime has no terminal, so the row stays `Pending`) is flushed and verified through the
  reply side's queue; `FlushSessionAsync` is database-backed, so a row enqueued by one container
  is delivered by the other.
- **Failure-note delivery inventory.** See § Delivery inventory, DL-547-A. The obligation
  sentence is appended to `reason` before `FailAsync`, so `FailureReason`, the `Failed` event, the
  note body and `DelegationNoteDigest.Compute(reason)` all agree; the E2E's
  `AssertParentReceivedNoteAsync(parent, task, stored.FailureReason!)` is red if Code computes the
  digest before appending.
- **D-5 evidence is not excerpted.** `AttentionService.Evidence(...)` caps evidence at 400 chars
  (`EvidenceChars`). The D-5 evidence (event id 36 + digest 64 + the 177-char recipe + hold expiry
  + the abandon request) is about 440 chars, so it must be passed to the DTO directly. Layout,
  one item per line, in this order so the pieces a human needs first are never the ones cut:
  `obligation {EventId:D}` / `settlement {Settlement}` / the recipe / `hold expires {At+hold:O}`
  (or `hold disabled` when the setting is <= 0) / `to discard: POST /api/agent-tasks/{taskId:D}/retry {"abandonCommitRecovery":true}`.
  G-547-39 pins it.
- **`Describe` text (D-1).** `Commit recovery obligation {EventId:D} for task {taskId:D} (settlement {Settlement}) was not resolved. The gated commit, if it exists, is local and unpushed; find it with: git log --all --reflog --fixed-strings --all-match --grep={taskId:D} --grep={Settlement} --format=%H. Nothing was pushed.`
  Tests pin the event id, the digest, the exact recipe substring and `nothing was pushed`
  (case-insensitive); Code may reword the rest.
- **Shared hold predicate.** Both sweeps decide through one static rule so the `<= 0` and
  oldest-obligation arithmetic is written once and unit-tested:
  `CommitRecoveryObligations.ShouldHold(IReadOnlyList<Pending> pending, TimeSpan hold, DateTime now)`
  = `pending.Count > 0 && hold > TimeSpan.Zero && now - pending.Min(p => p.StartedAt) < hold`.
  Gate 1b and the reconciler call it; G-547-14/34 test the rule, G-547-10/12 test each call site.
- **Guard placement for the plan's D-3 reconciler gate.** After the `OnTurnEndAsync` last chance
  and `ClassifyFailure`, before `FailAndNotifyAsync`; holding is `continue` without
  `_deadSessions.Forget`, and G-547-33 is the test that would go red if a hold reset the grace.

### Inspection

Bodies read in full at `22f010a0` (line counts as inspected):

- `tests/Antiphon.Tests/Application/GatedCommitTrailerTests.cs` (50): one parameterised method,
  `:`/`=`/`%`, real repo, gate commit, `%(trailers:...)` operation read, resolver round trip,
  duplicate differently-cased key rejected, `--all` failure. Boundaries -> the reader's own
  cases V-547-1..4 reuse `SeedAsync()`/`Gate()`; the duplicate-key arm stays the resolver-level
  guard for G-547-4/25.
- `tests/Antiphon.Tests/Application/AgentTaskReplyC527Tests.cs` (811): `SeedC527Async`
  (real repo, `.gitignore`, `tracked.txt`, parent with history, Dispatched Shared Custom task
  `DispatchedAt = -1 min`, `configure` runs last), `C527Factory` (delivery verification 1 s
  budgets, `gitSpy`, `saveInterceptor`), the hook-failure case (`InstallFailingPreCommitHookAsync`
  -> `CommitFailed` Warning + child brief with stderr), the three audit cases (baseline sha,
  `UpstreamSnapshot`, Commit-role child), the nested settlement-scoped `ThrowOnceSaveInterceptor`.
  Boundaries -> new partials reuse every helper; the accept/prose/read-failure audit cases seed
  exactly like `C527_commit_child_audit_flags_a_commit_without_the_gate_trailer`.
- `AgentTaskReplyC527EmptyRecoveryTests.cs` (145): `AssertC527RecoveryPendingAsync` (Dispatched,
  no child, exactly one type-34, no Committed, no land notification) and the two-attempt hold loop
  shape. `AgentTaskReplyC527RecoveryTests.cs` (384): `C527_unavailable_child_audit_is_reported_to_parent`
  (`OverrideRun` on `log -z`/`diff-tree`/`check-ignore`/`symbolic-ref`, receipt contains
  `audit unavailable`), the outbox/boundary tests, `C527ReconcileAsync`.
  `AgentTaskReplyC527MovedBranchTests.cs` (76): `OverrideRun` on `--all` after the commit;
  `AgentTaskReplyC527NoOpTests.cs` (99): `NotNeeded` `Detail.ShouldBe($"{started.Id:D} NothingToCommit")`,
  `spy.Verbs.Contains("add") && args[0]=="status"` post-commit override pattern (the pattern
  S2's failed-search case copies with `commit`/`--all`). `AgentTaskReplyC527InspectionTests.cs`
  (1-100 + `AssertC527RecoveredReceiptAsync` at 192): `BeforeRun` ordering assertions.
- `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs` (helpers 4351-4610,
  4947-5030): class is `[Category("Integration")] [ParallelLimiter<ProcessSpawnLimit>] [NotInParallel]`;
  `AttachTerminal` seeds `UserPrompt`+`TurnEnd` on submit; `AssertParentReceivedNoteAsync` is the
  delivery verdict (one Delegation-origin row for the parent, `Sent`, `Delivered`, `SourceTaskId`,
  `ConversationKey == task:{root:N}`, `ContentDigest == Compute(report)`, exactly one prompt
  carrying the report, byte-equal to the body, past the baseline); `TestScopeFactory` registers
  `AgentTaskService`, `AgentSessionRuntime`, `SessionMessageQueueService`, `GatedCommitService`
  through `AddDelegationWorktreeGraph`.
- `tests/Antiphon.Tests/TestHelpers/RecordingGitWorkspaceService.cs` (55), `ScratchGitRepo.cs`
  (115), `ThrowOnceSaveInterceptor.cs` (39), `RecordingSessionStopper.cs` (24),
  `DelegationTestServices.cs` (`AddDelegationWorktreeGraph`: `TryAddSingleton<GitWorkspaceService>(workspaceGit)`,
  `GatedCommitService`, `RepositoryMutationLease`), `AntiphonWebAppFactory.cs` (isolated cloned
  schema per factory, `RefusingSessionRunnerClient`), `FakeAgentProtocolAdapter.cs` (`OnSubmitted`,
  `SubmittedBodies`), `ProductionRunnerGuard.cs`.
- `tests/Antiphon.Tests/Application/AgentTaskOverdueDeadlineTests.cs` (720): `[NotInParallel]`,
  `CreateHarness()` (returns `(Dispatcher, Stopper)`; `TimeProvider.System`; `AgentTaskReplyService`
  singleton; `DelegateBindRefusalRecoverySettings` default), `Scenario` (`SeedTaskAsync(dispatchedMinutesAgo, status, replyToSession, ...)`
  Role Code, `SeedEntriesAsync`, dispose deletes events/tasks/sessions/queued messages by id),
  `the_caller_is_told_rather_than_left_to_discover_it` asserts the queued row's `Body` only.
  Boundaries -> `CreateHarness` delegates to the shared builder; `Scenario.SeedCommitRecoveryObligationAsync`;
  the dispatcher's `internal CatchUpOverride` (line 1968) counts pulls for G-547-29.
- `tests/Antiphon.Tests/Application/AgentTaskDeadSessionReconciliationTests.cs` (991):
  `[NotInParallel]`, `Scenario.Harness(params Guid[] gone)` (`FakeTimeProvider(UtcNow)`,
  `DelegationSettings` defaults so `DeadSessionFailGraceMinutes = 3`, `AgentTaskReplyService`
  singleton, empty `ClaudeProjectsRoot`), `PastGraceAsync` (sweep, +5 min, sweep), `AddTaskAsync`
  (`DispatchedAt = -1 min`), `AddTranscriptNoiseAsync`, `ParentNoteBodiesAsync`, `ReadTaskAsync`,
  `FakeRunnerClient` (everything Running except `Gone`; `KillAsync` throws). Boundaries ->
  `Scenario.AddObligationAsync`; no harness change (defaults hold 720).
- `tests/Antiphon.Tests/Application/AgentTaskServiceIntegrationTests.cs` (1773):
  `[NotInParallel("AgentQueue")]`, `SeedTaskAsync(kind, dir, status:, level:, sessionId:)`
  (no `DispatchedAt`), `CreateService(db, stopper:)` (explicit `DelegationSettings` without the
  hold, so the default 720 applies), `retrying_a_running_task_stops_the_delegate_first`
  (`Killed.ShouldBe([sessionId])`), `a_queued_task_cannot_be_retried`, `escalating_*`,
  `DeleteTaskTreeAsync` (events, queued messages, tasks, sessions). Boundaries ->
  `SeedObligationAsync(taskId, minutesAgo)`; the three new methods clean up in `finally`.
- `tests/Antiphon.Tests/Application/AgentTaskCommitEndpointTests.cs` (1-90, 283-360):
  `sealed partial`, `[NotInParallel] [ClassDataSource<CommitEndpointWebAppFactory>(PerClass)] [ParallelLimiter<ProcessSpawnLimit>]`,
  `SeedTaskAsync` via `_factory.Services` scope, factory registers `RecordingSessionStopper` as
  `IDelegateSessionStopper` (singleton, cumulative `Killed`), `/api/agent-tasks` group has no
  authorization requirement (the land-contract tests POST without headers). Boundaries -> a new
  partial file shares the booted factory; assertions use `Contains(sessionId)` not emptiness.
- `tests/Antiphon.Tests/Application/AttentionServiceTests.cs` (1-60, 344-372, 2392-2440, 2471-2477,
  2495-2530, 2606-2734): `[Category("Integration")] [Category("Slow")]`, parallel, id-scoped
  `Owns`, `AddSessionAsync(status, endedMinutesAgo)`, `AddTaskAsync(session, status, dispatchedMinutesAgo, ...)`
  (Dispatched/Working, `ReplyTo Session`), `AddTaskEventAsync(taskId, type, detail, minutesAgo)`
  (returns nothing), `AddTranscriptAsync`, `ItemsForAsync(scenario, TimeProvider)`, `BuildService`
  (fresh `CreateContext()`), `a_dispatched_task_whose_session_has_written_nothing_is_listed`.
  Boundaries -> `AddTaskEventAsync` returns `(Guid Id, DateTime At)` and gains `DateTime? at`;
  `BuildService` gains `AppDbContext? db`; new `TestHelpers/CountingCommandInterceptor.cs`.
- `client/src/features/attention/attentionVisuals.test.ts` (80-140 + `ALL_KINDS`): exhaustive
  list, `toBeDefined`/label/icon/hint per kind, group and target checks, the
  `DispatchWarningUnconfirmed` dedicated case as the template for the new one.
- Production seams read for assertion sites: `AgentTaskReplyService.TryCommitOnSettleAsync`
  (3140-3360: `recoveryStarted` predicate, independent recovery scope, the four no-op NotNeeded
  writes, `RecordCommitted`), `AuditCommitChildAsync` (3417-3480: unavailable style
  `commit {sha7} audit unavailable: {error}`, `commit {sha7} was made outside the gate`),
  `GitWorkspaceService.FindGatedCommitsAsync` (945-988), `AgentTaskDispatcher.TryFailOverdueAsync`
  (1708-1781), `FailDeadSessionTasksAsync` (1486-1625), `FailAsync`/`FailAndNotifyAsync`
  (2255-2290: `EnqueueAsync(parent, note.Body, WhenIdle, Delegation, task:{root:N}, task.Id, Compute(reason), header)`,
  enqueue failure caught and logged), `AgentTaskService.RetryAsync`/`RequeueAsync` (1854-2340),
  `AttentionService.BuildOpenTaskItemsAsync` (825-960: `open` = Dispatched|Working, DeadSession is
  the first arm, `NeverStartedGrace = 2 min`, `Evidence`/`Excerpt` 400), `ExceptionMiddleware`
  (67: `code` and `Extensions` merged into the problem document), `SessionMessageQueueService`
  (`EnqueueAsync` requires the session row: `NotFoundException` otherwise; `FlushSessionAsync` is
  database-backed), `DelegationReportFormatter.BuildCompletionNote` (605-666: `git=` bit, warning
  paragraph between header and body), `TaskDeadlinePolicy.EvaluateAsync` (ceiling from
  `DispatchedAt`; role Custom uses `DefaultTimeoutMinutes`).

Boundaries covered (-> IDs) or excluded:

| Boundary | Covered by |
|---|---|
| trailer separators `:` / `=` / `%` | V-547-1, V-547-6 |
| empty trailer block; duplicate differently-cased key; odd NUL field count; git failure | V-547-2, V-547-4, V-547-3, V-547-5 |
| prose mention with no trailer block | V-547-7 |
| resolution: NotNeeded / Abandoned / Committed same instant / +1 tick / -1 tick; other id; N-format id; missing separator space | V-547-10..14 |
| hold: young / expired / 0 / -5 / oldest-of-two / two obligations both abandoned | V-547-15, 16, 22, 23, 24 |
| requeue: retry / escalate / abandon true; body omitted / `{}` / false / true | V-547-25..29 |
| attention: 120 s / 121 s; NotNeeded / Abandoned / Committed-after / Committed-before; Failed task; dead session + obligation | V-547-31..35 |
| parent busy / idle for the failure note; hook fixed / still failing after a CommitFailed | V-547-19, V-547-17 |
| `RerouteAsync` / `RerouteOnWallAsync` direct calls | excluded: one `RequeueAsync` seam; G-547-17 (escalate) proves the seam and PC-547-17 is the guard-moved-to-Retry mutation. Reroute needs a routable second kind and adds nothing the seam does not already prove. |
| `-Refine` re-settlement | excluded (D-7, out of card); V-547-18 pins that a foreign-digest obligation does not hold this settlement, and that the settlement's own task-scoped `Committed` row then resolves it under D-1 (correct by design, not an open gap; see D-7). |
| `CancelAsync` with an open obligation | excluded (§ Scope boundaries: informed terminal decision). |
| real 720-minute wall clock | excluded: rows are back-dated and the reconciler clock is fake; the arithmetic is unit-tested. |
| pty keystroke delivery of the failure note | excluded: `FakeAgentProtocolAdapter` seeds the `UserPrompt` exactly as the tailer would; keystrokes are `Antiphon.Agents.Pty.Tests`. |

### Delivery inventory

Acceptance is at the recipient. For every session destination the evidence is the matching
complete `UserPrompt` transcript record (byte-equal body, digest-matched row, exactly once).

| ID / producer -> destination / durable identity | Persistence boundaries and recovery cuts | Observable receipt and tests |
|---|---|---|
| DL-547-A **expired-hold failure note**: `AgentTaskDispatcher.FailAndNotifyAsync` (watchdog `overdue-task deadline` or `dead-session reconciler`) -> `FailAsync` (Failed status, `Failed` event, one `CommitRecoveryAbandoned` row per obligation, one transaction: the rows are tracked before `FailAsync`'s `SaveChangesAsync`) -> `SessionMessageQueueService.EnqueueAsync(parent, note.Body, WhenIdle, Delegation, "task:{root:N}", task.Id, Compute(reason), header)` -> `SessionQueuedMessages` row -> turn-end flush / stranded sweep -> parent terminal -> parent `UserPrompt`. Identity: `SourceTaskId` + `ContentDigest = Compute(reason)` + `ConversationKey`; the note body carries the obligation id and digest, so a human can match it to the type-36 row without the queue. | Cut 1: the Failed save fails -> nothing durable changes (status, event, type-36 rows share the transaction), the next tick re-evaluates. Cut 2: `EnqueueAsync` throws (missing parent row, transport) -> caught and logged; the Failed status, the type-36 provenance and `FailureReason` stand; **there is no outbox for this note** (pre-existing `FailAndNotifyAsync` contract, unchanged by this card): the loss is visible only as the `RecentFailure` attention row and the task's `FailureReason`. V-547-21 pins that the abandonment record survives the enqueue failure; the undelivered note itself is a declared gap, not a tested recovery. Cut 3: row `Pending`, parent busy -> held until the parent's `TurnEnd`; parent idle -> next flush. Both exercised. | Substitute (sweep level): the queued row's `Body`/`NoteHeader` (V-547-16, V-547-23) proves the note was composed and enqueued, not received. Recipient evidence (E2E): `AssertParentReceivedNoteAsync(parent, task, stored.FailureReason!)` in V-547-19, busy and idle arms, prompt text contains `git=commit-recovery-abandoned:{id8}`, the digest and the recipe. |
| DL-547-B **recovered `committed:` note after a hold**: `TryCommitOnSettleAsync` recovery (`existing.Items.Count == 1`, `RecordCommitted`) -> settlement outbox `AgentTaskLandNotifications` -> queue -> parent `UserPrompt`. Identity: `SourceLandNotificationId`, `ContentDigest`, the `Committed` row (`At >= StartedAt`) that closes the obligation. | Cuts and recovery are CARD-0527's (carried `C527_completion_outbox_recovers_complete_receipt`, the five recovery families). New here: the sweep must not close, alter or lose the obligation while holding (V-547-15 asserts no type-35/36 row and an unchanged `AgentSessionId`/`DispatchedAt`), and the later `Committed` row must resolve it (V-547-20, G-547-21). | `AssertC527RecoveredReceiptAsync(task, parent, report, committed, 1)` in V-547-20; `LoadUnresolvedAsync` empty afterwards; one `commit`, no `push`. |
| DL-547-C **409 `commit_recovery_pending`**: synchronous HTTP response; no asynchronous delivery. | None. | Problem document `code` + `obligationEventId`/`settlement`/`startedAt`/`holdExpiresAt` (V-547-28). |
| DL-547-D **attention row**: read model over the same rows; no delivery. | None. | V-547-31..36. |

What the substitutes cannot prove: the dispatcher-harness runtime has no terminal, so the
production case where `EnqueueAsync(WhenIdle)` delivers inline from inside `FailAndNotifyAsync`
on an idle parent is not exercised here; it is the existing queue contract carried by the
`SessionMessageQueueService` suites. The fake terminal cannot prove that a real pty accepted the
keystrokes.

### Proves it works now

Layers: **U** = `[Category("Unit")]` pure rule, **G** = `GatedCommitServiceTests` partial (real
repo, `ProcessSpawnLimit`), **R** = `AgentTaskReplyIntegrationTests` partial (settlement, real
repo, parent terminal), **W** = `AgentTaskOverdueDeadlineTests`, **D** =
`AgentTaskDeadSessionReconciliationTests`, **S** = `AgentTaskServiceIntegrationTests`, **E** =
`AgentTaskCommitEndpointTests` partial (booted `Program`, isolated schema), **A** =
`AttentionServiceTests`, **C** = Vitest.

- V-547-1: `ReadTrailersAsync` parses each configured separator into exact pairs | G | `GatedCommitServiceTests.C547_ReadTrailersAsync_parses_each_configured_separator(":" \| "=" \| "%")` | `SeedAsync()`, `config trailer.separators <sep>`, `commit --allow-empty -m "subject\n\nantiphon<sep> true\nantiphon-commit<sep> gated"`, fixture check `%B` contains `antiphon-commit<sep> gated`; `spy.ReadTrailersAsync(repo.Path, head)`: `Succeeded` true, `Items` == `[("antiphon","true"),("antiphon-commit","gated")]` in order; `HasExactTrailer(items,"antiphon-commit","gated")` true; `HasExactTrailer(items,"Antiphon-Commit","gated")` true (key case-insensitive); `HasExactTrailer(items,"antiphon-commit","Gated")` false (value ordinal).
- V-547-2: empty block is success with no items | G | `C547_ReadTrailersAsync_empty_block_is_success_with_no_items` | commit `-m "subject only"`; `Succeeded` true, `Items` empty, `ExitCode` 0; `HasExactTrailer(items, "antiphon-commit", "gated")` false.
- V-547-3: odd field count is malformed | G | `C547_ReadTrailersAsync_odd_field_count_is_malformed_not_partial` | `spy.OverrideRun = args => args[0]=="log" && args.Any(a => a.StartsWith("--format=%(trailers:", Ordinal)) ? (0, "antiphon\0true\0antiphon-commit\n", "") : null`; `Succeeded` false, `ExitCode` -1, `Error` == `Malformed parsed Git trailers.`, `Items` empty.
- V-547-4: `HasExactTrailer` requires exactly one key occurrence | G | `C547_HasExactTrailer_requires_exactly_one_key_occurrence` | rows (Shouldly custom messages): `single` -> true; `duplicate key, differently cased` (`antiphon-commit=gated`, `Antiphon-Commit=other`) -> false; `duplicate key, same value twice` -> false; `absent` -> false; `other keys around it` -> true.
- V-547-5: a git failure is reported, not an empty success | G | `C547_ReadTrailersAsync_git_failure_is_reported_not_empty` | override on the trailers format returns `(128, "", "trailers unavailable")`; `Succeeded` false, `ExitCode` 128, `Error` contains `trailers unavailable`.
- V-547-6: the audit accepts a gated commit under each separator | R | `AgentTaskReplyIntegrationTests.C547_commit_child_audit_accepts_a_gated_commit_under_each_separator(":" \| "=" \| "%")` | seed as `C527_commit_child_audit_flags_a_commit_without_the_gate_trailer`, plus `config trailer.separators <sep>`, `commit --trailer antiphon-commit=gated`; fixture check `%B` contains `antiphon-commit<sep> gated`; child settles (`AttachTerminal` + flush); `AgentTaskEvents.Any(child, Warning, Detail.Contains("outside the gate"))` false; `Any(... "audit unavailable")` false; no `AgentIncident` for the child session; parent prompt does not contain `outside the gate`.
- V-547-7: a body prose mention is flagged | R | `C547_commit_child_audit_flags_a_body_prose_mention_without_a_trailer_block` | commit `-m "ungated\n\nSee antiphon-commit: gated in the docs.\n\nTrailing prose line without separator"`; fixture check `log -1 --format=%(trailers:only)` is empty; `Warning` `Single` for the child with `Detail` containing `commit {sha7} was made outside the gate`; no incident; `Detail` does not contain `REVERT`.
- V-547-8: a failed trailer read is `audit unavailable`, never `outside the gate` | R | `C547_commit_child_audit_read_failure_is_unavailable_not_outside_the_gate` | gated child commit (`--trailer antiphon-commit=gated`); `spy.OverrideRun` on the trailers format -> `(128, "", "trailers unavailable")`; `AttachTerminal`; a `Warning` with `Detail` == `commit {sha7} audit unavailable: trailers unavailable`; no `Warning` containing `outside the gate`; parent prompt contains `audit unavailable` and `trailers unavailable` and not `outside the gate`.
- V-547-9: the rule lists an unresolved obligation | U | `CommitRecoveryObligationsTests.Unresolved_lists_a_started_obligation_with_no_resolution` | one type-34 (`Detail` digest, `At` t0) -> `Single`: `EventId`, `Settlement` == digest, `StartedAt` == t0; an unrelated `Warning` row and a type-34 for another task id are ignored.
- V-547-10: NotNeeded with the prefix resolves | U | `NotNeeded_whose_detail_starts_with_the_started_id_and_a_space_resolves` | rows `NothingToCommit` (`$"{id:D} NothingToCommit"`) and `CommitFailed` (`$"{id:D} CommitFailed"`) -> `Unresolved` empty.
- V-547-11: resolution rows naming another obligation do not resolve | U | `Resolution_rows_naming_another_obligation_do_not_resolve` | rows: `other D id`, `N-format id` (`$"{id:N} NothingToCommit"`), `no separating space` (`$"{id:D}NothingToCommit"`), each as NotNeeded and as Abandoned -> still `Single`.
- V-547-12: Abandoned with the prefix resolves | U | `Abandoned_whose_detail_starts_with_the_started_id_resolves` | type-36 `$"{id:D} overdue-task deadline: reason"` -> empty.
- V-547-13: Committed at/after resolves, before does not | U | `Committed_at_or_after_the_start_resolves_and_before_does_not` | rows `same instant` -> empty; `+1 tick` -> empty; `-1 tick` -> `Single`.
- V-547-14: `Abandon` and `Describe` shapes; enum and default pinned | U | `Abandon_builds_the_type_36_row_with_the_prefix_convention`, `Describe_names_the_event_the_digest_and_the_recipe`, `Default_hold_is_720_minutes` | `Abandon(p, "overdue-task deadline", "why", now)`: `Type` == `CommitRecoveryAbandoned`, `(int)Type` == 36, `AgentTaskId`, `Detail` == `$"{p.EventId:D} overdue-task deadline: why"`, `At` == now, `Unresolved([started, row])` empty. `Describe(taskId, p)` contains `p.EventId:D`, `p.Settlement`, `git log --all --reflog --fixed-strings --all-match --grep={taskId:D} --grep={p.Settlement} --format=%H`, `nothing was pushed` (`Case.Insensitive`). `new DelegationSettings().CommitRecoveryHoldMinutes` == 720.
- V-547-15: `ShouldHold` rules | U | `ShouldHold_rules` | rows: `empty` -> false; `hold 0` -> false; `hold -5 min` -> false; `young (10 min, hold 720)` -> true; `expired (800 min)` -> false; `at the boundary (720 min exactly)` -> false; `oldest of two decides (10 min + 800 min)` -> false.
- V-547-16: loader is task-scoped; the set overload is one query | Integration (`CommitRecoveryObligationsLoaderTests`, `[Category("Integration")]`, seeds two task ids and deletes its events by id) | `LoadUnresolvedAsync_is_task_scoped_and_applies_the_rule`, `LoadUnresolvedAsync_for_a_set_runs_one_query_and_omits_tasks_with_nothing_pending` | task A: 34 + 35 (resolved) + 34 (open); task B: 34 (open); `LoadUnresolvedAsync(db, A)` -> the one open A row; set `[A, B, C]` (C has nothing) -> keys `{A, B}` only, each with its rows; a `CountingCommandInterceptor` on the context counts exactly 1 command containing `"AgentTaskEvents"` for the set call.
- V-547-17: `CommitFailed` after a verified empty search resolves; a later settle proceeds | R | `C547_CommitFailed_after_a_verified_empty_search_resolves_and_a_later_settle_proceeds("fixed" \| "still-failing")` | `SeedC527Async`, `InstallFailingPreCommitHookAsync("gate says no")`, `a.md` + `SeedFileEditAsync`, spy with `BeforeRun` recording `string.Join(' ', args)`, factory with the nested settlement `ThrowOnceSaveInterceptor`; settle 1: task `Dispatched`, one type-34 (`Detail` == digest), one type-35 with `Detail` == `$"{started.Id:D} CommitFailed"`, the recorded calls contain a `log --all --reflog` entry after the `commit` entry, `LoadUnresolvedAsync` empty, no `Committed`, no child persisted. `fixed`: delete `.git/hooks/pre-commit`; settle 2 (fresh factory + terminal): `Succeeded`, `AssertC527RecoveredReceiptAsync(..., 1)`-style receipt with `git=committed:`, events: two 34, one 35, one 33, `Unresolved` empty. `still-failing`: settle 2: `Succeeded`, child `Goal` contains `gate says no`, receipt contains `commit task `, events: two 34, two 35 (each prefixed by a distinct started id), no 33, `Unresolved` empty.
- V-547-18: an obligation from another digest does not hold this settlement | R | `C547_an_unresolved_obligation_from_another_digest_does_not_hold_this_settlement` | hand-insert a type-34 with a foreign 64-hex digest at -5 min; ordinary settle -> `Succeeded`, `git=committed:`, HEAD advanced; `LoadUnresolvedAsync` is then empty: the settlement's task-scoped `Committed` row (`At >= StartedAt`) resolves every earlier obligation regardless of digest, per D-1. Confirmed correct in CARD-0547 round-1 Review (a surviving row would have no consumer and would only make Retry 409 on a Succeeded task); naming which commit the note describes is the D-7 identity change, out of this card (§ Out of scope).
- V-547-19: expired hold: the parent hears it (busy and idle) | R (E2E) | `C547_overdue_sweep_past_the_hold_abandons_the_obligation_and_the_parent_hears_it(busy: false \| true)` | `SeedC527Async(t => t.DispatchedAt = UtcNow.AddMinutes(-150_000))`; settle under the settlement `ThrowOnceSaveInterceptor` -> `AssertC527RecoveryPendingAsync`, `committed` = HEAD; back-date the type-34 `At` to -800 min (`ExecuteUpdateAsync`); busy arm seeds parent `AssistantText`; `OverdueSweepHarness.Create(gitSpy: spy)`.`Dispatcher.FailOverdueTasksAsync` with git live (no `--oneline` override: CARD-0085's history probe can find the `task <short>:` commit, G-547-11c) -> `Failed`, `FailureReason` contains the started id (D) and the digest, type-36 `Detail` starts with `$"{started.Id:D} "` and contains `overdue-task deadline`, `Stopper.Killed` empty; reply factory `AttachTerminal` + `FlushSessionAsync(parent)`; busy: `SubmittedBodies` empty, seed `TurnEnd`, `Queue.OnTurnEndAsync(parent)`; `AssertParentReceivedNoteAsync(parent, task, stored.FailureReason!)`: prompt contains `git=commit-recovery-abandoned:{started.Id:N}[..8]`, the digest, `git log --all --reflog`; HEAD still `committed`; `spy.Verbs.Count("commit") == 1`, no `push`; a further `OnTurnEndAsync(session)` + flush leaves `SubmittedBodies.Count == 1` and no `Committed` row.
- V-547-20: young hold: the sweep holds and the re-hand records `committed:` | R (E2E) | `C547_overdue_sweep_holds_a_settled_gated_commit_until_the_rehand_records_it` | same seed and interrupted settle; sweep -> `Dispatched`, no `Failed` event, no type-35/36, `AgentSessionId`/`DispatchedAt` unchanged, `Killed` empty, no Delegation-origin queued row for the parent; re-hand (`CreateService(C527Factory(gitSpy: spy))` + terminal + flush) -> `AssertC527RecoveredReceiptAsync(task, parent, report, committed, 1)`, `LoadUnresolvedAsync(task)` empty, `Committed.At` > started `At`, `Verbs.Count("commit") == 1`, no `push`.
- V-547-21: the abandonment record survives an enqueue failure | W | `an_expired_hold_whose_note_cannot_be_enqueued_still_records_the_abandonment` | `SeedTaskAsync(150_000, replyToSession: Guid.NewGuid())` (no parent row -> `EnqueueAsync` throws `NotFoundException`, caught); obligation -800 min; sweep -> `Failed`, type-36 present, `FailureReason` names the obligation, no `SessionQueuedMessages` row, `Killed` empty. **Substitute**: proves durability, not delivery; the note is lost by the existing contract (DL-547-A cut 2).
- V-547-22: the watchdog holds a young obligation before asking the runner | W | `an_overdue_task_with_a_young_commit_recovery_obligation_is_held_before_the_runner_is_asked` | task 150 000 min, entries `UserPrompt`/`TurnEnd`, parent seeded, obligation -10 min; `harness.CatchUpOverride` counts; sweep returns; `Status` `Working`, `FailureReason` null, no `Failed` event, no type-36, `Killed` empty, no queued row, `pulled == 0`.
- V-547-23: past the hold the watchdog abandons by name and the note carries the git bit | W | `an_overdue_task_whose_commit_recovery_hold_expired_is_failed_with_the_obligation_abandoned_by_name` | obligation -800 min; sweep -> `Failed`; type-36 `Single`: `Detail` starts with `$"{eventId:D} "`, contains `overdue-task deadline`; `FailureReason` contains `eventId:D` and the digest; `Failed` event `Detail` == `FailureReason`; queued row (`Single`, parent, Delegation): `NoteHeader` contains `git=commit-recovery-abandoned:{eventId:N}[..8]`, `Body` contains the digest, `git log --all --reflog --fixed-strings --all-match --grep={task:D} --grep={digest} --format=%H`, `nothing was pushed` (`Case.Insensitive`); `Killed` empty; `LoadUnresolvedAsync` empty. **Substitute** for delivery (row only); recipient evidence is V-547-19.
- V-547-24: every unresolved obligation is abandoned; non-positive hold fails now but still annotates | W | `an_expired_hold_abandons_every_unresolved_obligation` (obligations -10 and -800 min -> `Failed`, two type-36 rows, one per started id, `LoadUnresolvedAsync` empty); `a_non_positive_commit_recovery_hold_fails_immediately_but_still_annotates(0 \| -5)` (`OverdueSweepHarness.Create(s => s.CommitRecoveryHoldMinutes = hold)`, obligation -1 min -> `Failed`, type-36 present, `NoteHeader` contains `git=commit-recovery-abandoned:`).
- V-547-25: the reconciler holds a young obligation and keeps its grace | D | `a_dead_session_task_with_a_young_commit_recovery_obligation_is_held_and_its_grace_is_kept` | `AddTaskAsync(Dispatched, SessionStatus.Failed, failureReason: "the pty-host exited (code 1)")`, `AddTranscriptNoiseAsync`, `AddObligationAsync(task, 10)`; `PastGraceAsync` -> `Dispatched`, no `Failed` event, no type-36, `ParentNoteBodiesAsync()` empty, `Stopper.Killed`/`Runner.Killed` empty; `Clock.Advance(720 min)`; **one** `FailDeadSessionTasksAsync` -> `Failed` (no fresh grace), type-36 contains `dead-session reconciler`.
- V-547-26: past the hold the reconciler annotates identically | D | `a_dead_session_task_whose_commit_recovery_hold_expired_is_failed_with_the_obligation_abandoned_by_name` | obligation -800 min; `PastGraceAsync` -> `Failed`; type-36 `Detail` starts with the id and contains `dead-session reconciler`; `FailureReason` contains the id and digest and still `the pty-host exited (code 1)`; `ParentNoteBodiesAsync()` has a body containing `git=commit-recovery-abandoned:` and the digest; nothing killed; ephemeral agent removed (as the carried test asserts).
- V-547-27: Retry refuses 409 before stopping the delegate | S | `retrying_a_task_with_a_pending_commit_recovery_obligation_is_refused_before_the_delegate_is_stopped` | `SeedTaskAsync(Worker, dir, status: Dispatched, sessionId: s)`, `SeedObligationAsync(task, 5)`; `Should.ThrowAsync<ConflictException>(RetryAsync(id, ct))`: `Code` == `commit_recovery_pending`, `Message` contains `abandonCommitRecovery=true`, `Extensions["obligationEventId"]` == eventId, `["settlement"]` == digest, `["startedAt"]` == the row's `At` (within 1 s), `["holdExpiresAt"]` == `At + 720 min`; `stopper.Killed` empty; task still `Dispatched`, `Attempt` 1, `AgentSessionId` == s; no type-36; `DeleteTaskTreeAsync` in `finally`.
- V-547-28: the endpoint refuses without the flag and requeues with it | E | `AgentTaskCommitEndpointTests.Retry_without_the_abandon_flag_is_a_409_commit_recovery_pending_problem("omitted" \| "empty-object" \| "false")`, `Retry_with_abandonCommitRecovery_true_requeues_through_the_endpoint` | seed a Dispatched task + type-34 via `_factory.Services`; `PostAsync(url, null)` / `PostAsJsonAsync(url, new {})` / `new { abandonCommitRecovery = false }` -> 409, `application/problem+json`, `code` == `commit_recovery_pending`, `obligationEventId` == eventId, `settlement` == digest; the factory's `RecordingSessionStopper.Killed` does not contain the session id; task still `Dispatched`. `true` -> 200, `status` == `Queued`, `Killed` contains the session id, type-36 `Detail` starts with `$"{eventId:D} requeue:Retried: "`.
- V-547-29: abandon requeues and records; Escalate shares the guard | S | `retrying_with_abandonCommitRecovery_requeues_and_records_the_abandonment` (`RetryAsync(id, ct, abandonCommitRecovery: true)`: `Status` `Queued`, `Attempt` 2, `Killed` == `[s]`, type-36 `Single` with `Detail` starting `$"{eventId:D} requeue:Retried: "` and containing `Retried at`, `Retried` event present, `LoadUnresolvedAsync` empty); `escalating_a_task_with_a_pending_commit_recovery_obligation_is_refused_the_same_way` (Failed task, level Medium, obligation; `EscalateAsync(id, null, ct)` throws `ConflictException` `Code` == `commit_recovery_pending`; `ModelLevel` still Medium; `Attempt` 1).
- V-547-30: an abandoned obligation is invisible to a later same-digest settlement | R | `C547_an_abandoned_obligation_is_invisible_to_a_later_same_digest_settlement` | settle under the settlement interceptor; `reset --hard baseline` + `reflog expire --expire=now --all` (the empty-history shape); hand-insert `Abandon(started, "test", "discarded", now)`; rewrite `a.md`; settle 2 -> `Succeeded`, `git=committed:` with a NEW sha; events: two 34, one 36, one 33; `Unresolved` empty; `Verbs.Count("commit") == 2`.
- V-547-31: attention row shape and evidence | A | `a_task_holding_a_commit_recovery_obligation_older_than_two_minutes_is_an_error_row_with_the_recipe` | session Running, task Dispatched 10 min with a transcript, `AddTaskEventAsync(task, CommitRecoveryStarted, digest, 3)` returning `(id, at)`; `Single(i => i.TaskId == task)`: `Kind` `CommitRecoveryPending`, `(int)Kind` == 41, `Severity` `Error`, `Headline` contains `Held for recovery`, `SinceUtc` within 10 ticks of `at`, `Actions` == `[OpenDrawer, Cancel]`, `Evidence` contains `obligation {id:D}`, `settlement {digest}`, the exact recipe, `hold expires`, `"abandonCommitRecovery":true`, and `Evidence.Length > 400` (fixture sanity for G-547-39); `AttentionSummaryDto.From(...)` counts it under Broken.
- V-547-32: exact two-minute gate | A | `a_commit_recovery_obligation_becomes_visible_only_after_two_full_minutes` | `FakeTimeProvider(now)`; `at: now - 120 s` -> no `CommitRecoveryPending` item for the task; `at: now - 121 s` -> present.
- V-547-33: resolved obligations are not listed | A | `a_resolved_commit_recovery_obligation_is_not_listed("not-needed" \| "abandoned" \| "committed-after" \| "committed-before")` | started -5 min; resolution row NotNeeded `$"{id:D} NothingToCommit"` / Abandoned `$"{id:D} overdue-task deadline: x"` / Committed at -4 min -> absent; Committed at -6 min -> present.
- V-547-34: Failed task and dead session cases | A | `a_failed_task_with_an_open_commit_recovery_obligation_is_not_listed` (status Failed, completed 1 min ago, obligation -5 min -> no `CommitRecoveryPending` item); `a_dead_session_holding_a_commit_recovery_obligation_is_listed_as_the_hold_not_the_death` (session `Failed`, ended 5 min ago; task Dispatched 10 min; obligation -3 min -> `Single` item for the task, `Kind` `CommitRecoveryPending`, no `DeadSession` item for it).
- V-547-35: one events query for the whole open set | A | `commit_recovery_rows_cost_one_events_query_for_the_whole_open_set` | `BuildService(db: context with CountingCommandInterceptor)`; one obligated open task -> `GetAsync` -> `c1` = commands containing `"AgentTaskEvents"`; three more obligated open tasks (own sessions) -> fresh context + counter -> `c4`; `c4.ShouldBe(c1)`; both `> 0`.
- V-547-36: client draws the kind | C | `attentionVisuals.test.ts`: `'CommitRecoveryPending'` in `ALL_KINDS`; `it('draws CommitRecoveryPending as a held commit recovery')`: label `Commit recovery pending`, color `danger`, hint contains `commit`, `groupOf(Error row)` == `broken`, `targetOf` == `/orchestrator?tab=delegations&task=task-547`, `keyOf` contains `CommitRecoveryPending`.
- V-547-37: docs and client build | inspection | `grep -n commit_recovery_pending docs/antiphon-api.md docs/orchestration-loop.md`, `grep -n "CommitRecoveryPending = 41" docs/antiphon-api.md`, `grep -n abandonCommitRecovery client/src/api/agentTasks.ts`; `pwsh -File scripts/client-mode.ps1 -Status` after the client rebuild (typecheck is the build, Vitest does not typecheck). No PC (documentation).

### Guards the regression

- R-547-1: resolver identity unchanged by the shared reader | `GatedCommitServiceTests.Recovery_uses_Git_trailer_separators` (all three) and `Recovery_finds_exact_attempt_across_refs_and_reflogs` (branch/reflog/unborn); decisive: `found.Items.ShouldBe([head])` after the duplicate-key commit and `Succeeded.ShouldBeFalse()` on the `--all` failure.
- R-547-2: the audit's existing verdicts | `C527_commit_child_audit_flags_a_commit_without_the_gate_trailer` (still `was made outside the gate`, no incident), `..._flags_an_ignored_path_committed_outside_the_gate`, `..._flags_upstream_movement`, `C527_unavailable_child_audit_is_reported_to_parent` (all four inspections), `C527_child_audit_distinguishes_ignore_exceptions_in_complete_parent_receipt`.
- R-547-3: every C527 recovery family stays green after the loader widening | `C527_equals_separator_delivers_exact_parent_receipt`, `C527_successful_empty_history_preserves_recovery_until_parent_receipt`, `C527_branch_movement_after_failed_save_recovers_original_parent_receipt`, `C527_all_selected_reverted_delivers_no_op_without_Commit_child` (decisive: `resolved.Detail.ShouldBe($"{started.Id:D} NothingToCommit")`), `C527_retry_prerequisite_failure_preserves_commit_until_complete_parent_receipt`, `C527_post_commit_inspection_failure_defers_settlement_until_complete_receipt`, `C527_settle_save_failure_after_the_commit_recovers_without_a_second_commit`, the `StagedManifest`/`Literal` families, `C527_hook_failure_is_CommitFailed_and_the_child_brief_carries_stderr` (now also writes a type-35; its existing assertions unchanged), `C527_completion_outbox_recovers_complete_receipt`, `C527_repeated_report_selects_this_settlements_notification`.
- R-547-4: the watchdog's fifteen carried cases, in particular `the_caller_is_told_rather_than_left_to_discover_it` and `a_task_under_its_ceiling_is_left_completely_alone` | full `AgentTaskOverdueDeadlineTests` class after the `CreateHarness` delegation.
- R-547-5: the reconciler's carried cases, in particular `a_session_the_runner_still_serves_is_left_alone` and `attention_and_the_sweep_agree_on_every_dead_session_shape` | full `AgentTaskDeadSessionReconciliationTests` class.
- R-547-6: requeue mechanics | `retrying_a_failed_task_requeues_it_at_the_same_tier`, `a_requeued_task_gets_a_fresh_token`, `a_queued_task_cannot_be_retried` (the Queued refusal still wins, message `has not run yet`), `retrying_a_running_task_stops_the_delegate_first` (no obligation -> still killed), `escalating_moves_one_rung_up_the_ladder`, `escalating_a_queued_task_changes_its_tier_without_spending_an_attempt`.
- R-547-7: attention first-match order for existing arms | `AttentionServiceTests` full class; decisive: `a_dispatched_task_whose_session_has_written_nothing_is_listed` still `NeverStarted` (no obligation seeded).
- R-547-8: endpoint contract | `AgentTaskCommitEndpointTests` full class (shared factory unchanged) and `attentionVisuals.test.ts` exhaustive list (every existing kind still drawable).

### Guard inventory

| ID | Guard (plan reference) | Test method | PC |
|---|---|---|---|
| G-547-1 | D-6: a gated commit under `=`/`%` is not flagged outside the gate | R `C547_commit_child_audit_accepts_a_gated_commit_under_each_separator` | PC-547-1 |
| G-547-2 | D-6: the audit reads the trailer block, not the body | R `C547_commit_child_audit_flags_a_body_prose_mention_without_a_trailer_block` | PC-547-2 |
| G-547-3 | D-6: a failed trailer read is `audit unavailable`, never `outside the gate` | R `C547_commit_child_audit_read_failure_is_unavailable_not_outside_the_gate` | PC-547-3 |
| G-547-4 | D-6: `HasExactTrailer` requires exactly one key occurrence | G `C547_HasExactTrailer_requires_exactly_one_key_occurrence`; carried `Recovery_uses_Git_trailer_separators` | PC-547-4 |
| G-547-5 | D-6: odd NUL field count is malformed, not partial data | G `C547_ReadTrailersAsync_odd_field_count_is_malformed_not_partial` | PC-547-5 |
| G-547-6 | D-1: a NotNeeded row whose Detail starts with `{id:D} ` resolves (prefix, not equality) | U `NotNeeded_whose_detail_starts_with_the_started_id_and_a_space_resolves` | PC-547-6 |
| G-547-7 | D-1: an Abandoned row resolves | U `Abandoned_whose_detail_starts_with_the_started_id_resolves`; S `retrying_with_abandonCommitRecovery_...` | PC-547-7 |
| G-547-8a | D-1: a Committed row at/after the start resolves | U `Committed_at_or_after_the_start_resolves_and_before_does_not` (rows `same instant`, `+1 tick`) | PC-547-8a |
| G-547-8b | D-1: a Committed row before the start does not resolve | same method (row `-1 tick`) | PC-547-8b |
| G-547-9a | D-2: `CommitFailed` + verified empty search writes NotNeeded; a later settle proceeds | R `C547_CommitFailed_after_a_verified_empty_search_resolves_and_a_later_settle_proceeds("fixed")` | PC-547-9a |
| G-547-9b | D-2: `CommitFailed` + failed search leaves the obligation open | R `C547_CommitFailed_with_a_failed_history_search_leaves_the_obligation_pending` | PC-547-9b |
| G-547-10 | D-3: the watchdog holds within the window; nothing killed, no note | W `an_overdue_task_with_a_young_commit_recovery_obligation_is_held_before_the_runner_is_asked` | PC-547-10 |
| G-547-11a | D-3: past the window the watchdog abandons by name (type-36 names the sweep) | W `an_overdue_task_whose_commit_recovery_hold_expired_is_failed_with_the_obligation_abandoned_by_name` | PC-547-11a |
| G-547-11b | D-3: the note header carries `git=commit-recovery-abandoned:{id8}` | same method; E2E V-547-19 | PC-547-11b |
| G-547-11c | D-3: past the window the watchdog skips CARD-0085 bind-refusal recovery for a task holding an obligation, so the abandonment is reached with git live | E2E V-547-19 `C547_overdue_sweep_past_the_hold_abandons_the_obligation_and_the_parent_hears_it` | PC-547-49 |
| G-547-11d | D-3: the type-36 rows share `FailAsync`'s single `SaveChangesAsync` with the Failed status (DL-547-A cut 1), not a later save | W `an_expired_hold_writes_the_abandonment_in_the_same_save_as_the_failed_status` | PC-547-50 |
| G-547-12 | D-3: the reconciler holds within the window | D `a_dead_session_task_with_a_young_commit_recovery_obligation_is_held_and_its_grace_is_kept` | PC-547-12 |
| G-547-13 | D-3: the reconciler past the window annotates identically | D `a_dead_session_task_whose_commit_recovery_hold_expired_is_failed_with_the_obligation_abandoned_by_name` | PC-547-13 |
| G-547-14 | D-3: `CommitRecoveryHoldMinutes <= 0` disables the hold, not the annotation | U `ShouldHold_rules` (`hold 0`, `hold -5`); W `a_non_positive_commit_recovery_hold_fails_immediately_but_still_annotates` | PC-547-14 |
| G-547-15a | D-4: Retry refuses 409 `commit_recovery_pending` | S `retrying_a_task_with_a_pending_commit_recovery_obligation_is_refused_before_the_delegate_is_stopped` | PC-547-15a |
| G-547-15b | D-4: the refusal precedes `StopDelegateAsync` | same method (`Killed.ShouldBeEmpty()`) | PC-547-15b |
| G-547-16 | D-4: `abandonCommitRecovery` requeues and writes `requeue:Retried` | S `retrying_with_abandonCommitRecovery_requeues_and_records_the_abandonment` | PC-547-16 |
| G-547-17 | D-4: every requeue shares the guard (`RequeueAsync`, not `RetryAsync`) | S `escalating_a_task_with_a_pending_commit_recovery_obligation_is_refused_the_same_way` | PC-547-17 |
| G-547-18a | D-4: the endpoint body flag reaches the service | E `Retry_with_abandonCommitRecovery_true_requeues_through_the_endpoint` | PC-547-18a |
| G-547-18b | D-4: an absent or empty body is `false` | E `Retry_without_the_abandon_flag_is_a_409_commit_recovery_pending_problem("omitted")` | PC-547-18b |
| G-547-19a | D-5: membership requires an unresolved obligation | A `a_resolved_commit_recovery_obligation_is_not_listed` | PC-547-19a |
| G-547-19b | D-5: fixed two-minute visibility gate | A `a_commit_recovery_obligation_becomes_visible_only_after_two_full_minutes` | PC-547-19b |
| G-547-20 | D-1: settlement's `recoveryStarted` stays digest-scoped | R `C547_an_unresolved_obligation_from_another_digest_does_not_hold_this_settlement` | PC-547-20 |
| G-547-21 | D-1/D-3: after a hold the re-hand's later Committed row resolves the obligation | R E2E `C547_overdue_sweep_holds_a_settled_gated_commit_until_the_rehand_records_it` | PC-547-21 |
| G-547-22 | D-6: `ReadTrailersAsync` parses each configured separator into exact pairs | G `C547_ReadTrailersAsync_parses_each_configured_separator` | PC-547-22 |
| G-547-23 | D-6: a git failure in `ReadTrailersAsync` is reported, not an empty success | G `C547_ReadTrailersAsync_git_failure_is_reported_not_empty` | PC-547-23 |
| G-547-24 | D-6: an empty trailer block is success with no items (so untrailered commits stay `outside the gate`) | G `C547_ReadTrailersAsync_empty_block_is_success_with_no_items`; carried `C527_commit_child_audit_flags_a_commit_without_the_gate_trailer` | PC-547-24 |
| G-547-25 | D-6: the resolver consumes the shared reader with unchanged identity rules | carried `Recovery_uses_Git_trailer_separators(":")` | PC-547-25 |
| G-547-26 | D-1: resolution rows are obligation-specific (prefix carries the started id) | U `Resolution_rows_naming_another_obligation_do_not_resolve` | PC-547-26 |
| G-547-27 | D-2: the `CommitFailed` NotNeeded row carries the prefix and the outcome name | R `C547_CommitFailed_after_a_verified_empty_search_...("fixed")` (`Detail.ShouldBe`) | PC-547-27 |
| G-547-28 | D-1: settlement loads type 36, so an abandoned obligation is invisible to a later same-digest settle | R `C547_an_abandoned_obligation_is_invisible_to_a_later_same_digest_settlement` | PC-547-28 |
| G-547-29 | D-3: Gate 1b runs before the transcript pull | W `..._is_held_before_the_runner_is_asked` (`pulled.ShouldBe(0)`) | PC-547-29 |
| G-547-30 | D-3: `FailureReason` (and the digest) carry the obligation id and settlement | W `..._hold_expired_..._abandoned_by_name` (`FailureReason.ShouldContain`); E2E digest match | PC-547-30 |
| G-547-31 | D-3: the note carries the `Describe` warning paragraph (recipe) | W same method (`Body.ShouldContain("git log --all --reflog")`) | PC-547-31 |
| G-547-32 | D-3: every unresolved obligation is abandoned, not only the first | W `an_expired_hold_abandons_every_unresolved_obligation` | PC-547-32 |
| G-547-33 | D-3: a reconciler hold keeps the grace bookkeeping (no `Forget`) | D `..._is_held_and_its_grace_is_kept` (post-advance `Status.ShouldBe(Failed)`) | PC-547-33 |
| G-547-34 | D-3: the hold is measured from the oldest obligation | U `ShouldHold_rules` (row `oldest of two decides`) | PC-547-34 |
| G-547-35 | D-4: the 409 carries `obligationEventId`/`settlement`/`startedAt`/`holdExpiresAt` | S `retrying_..._refused_before_the_delegate_is_stopped`; E 409 method | PC-547-35 |
| G-547-36 | D-5: severity Error (counted in Broken) | A `a_task_holding_..._is_an_error_row_with_the_recipe` (`Severity`) | PC-547-36 |
| G-547-37 | D-5: actions are `[OpenDrawer, Cancel]`; Retry is absent | same (`Actions`) | PC-547-37 |
| G-547-38 | D-5: `SinceUtc` is the obligation start | same (`SinceUtc`) | PC-547-38 |
| G-547-39 | D-5: evidence is passed untruncated (digest, recipe and abandon request all present) | same (`Evidence.ShouldContain("\"abandonCommitRecovery\":true")`) | PC-547-39 |
| G-547-40 | D-5: arm 0 precedes DeadSession | A `a_dead_session_holding_a_commit_recovery_obligation_is_listed_as_the_hold_not_the_death` | PC-547-40 |
| G-547-41 | D-5: one obligations query for the open set | A `commit_recovery_rows_cost_one_events_query_for_the_whole_open_set` | PC-547-41 |
| G-547-42 | D-5: the client draws `CommitRecoveryPending` | C exhaustive `maps every kind...` + dedicated case | PC-547-42 |
| G-547-43 | D-1: `CommitRecoveryAbandoned = 36`, appended | U `Abandon_builds_the_type_36_row_with_the_prefix_convention` (`(int)`) | PC-547-43 |
| G-547-44 | D-5: `CommitRecoveryPending = 41`, appended | A `..._is_an_error_row_with_the_recipe` (`(int)Kind`) | PC-547-44 |
| G-547-45 | D-3: default hold is 720 minutes | U `Default_hold_is_720_minutes`; S `holdExpiresAt == At + 720 min` | PC-547-45 |
| G-547-46 | D-1: the single-task loader is task-scoped | Loader `LoadUnresolvedAsync_is_task_scoped_and_applies_the_rule` | PC-547-46 |
| G-547-47 | D-1: the set loader is one query and omits tasks with nothing pending | Loader `LoadUnresolvedAsync_for_a_set_runs_one_query_and_omits_tasks_with_nothing_pending` | PC-547-47 |
| G-547-48 | D-3: abandonment provenance is written in the Failed transaction, before the note is enqueued | W `an_expired_hold_whose_note_cannot_be_enqueued_still_records_the_abandonment` | PC-547-48 |

Guards = 56 rows (48 ids, eight split), mapped = 56, missing = 0, duplicate PC mappings = 0.
Justified without a PC: none. V-547-34's Failed-task case has no guard row: the arm iterates
the `open` set (Dispatched/Working) by construction, so a Failed task cannot enter it without
changing an unrelated query; the case is kept as cheap documentation.

### Positive controls

Mutation runs each PC method-scoped on a local inherited SourceLanding child:
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-pc/ -- --treenode-filter "/*/*/<Class>/<Method>"`
(C# rows; parameterised methods run every argument, the expected red names the arm) and
`pwsh -File scripts/test-client.ps1 attentionVisuals` (Vitest row). Each cycle: apply the
mutation, run red at the named assertion, restore, refresh the restored file's timestamp, run
green. Zero tests, build failures and fixture errors are not red. Rows that touch the same file
(`CommitRecoveryObligations.cs`: 6, 7, 8a, 8b, 14, 21, 26, 34, 43, 45; `AgentTaskReplyService.cs`:
1, 2, 3, 9a, 9b, 20, 27, 28; `GitWorkspaceService.cs`: 4, 5, 22, 23, 24, 25;
`AgentTaskDispatcher.cs`: 10, 11a, 11b, 13, 29, 30, 31, 32, 33, 48; `AgentTaskService.cs`: 15a,
15b, 16, 17, 35; `AttentionService.cs`: 19a, 19b, 36..41, 44) run separately from each other;
rows from different files may batch.

| PC | Break (compiling defect) | Exact method | Expected red |
|---|---|---|---|
| PC-547-1 | `AuditCommitChildAsync`: replace the `ReadTrailersAsync`+`HasExactTrailer` check with `CommitMessageAsync` + `Contains("antiphon-commit: gated", OrdinalIgnoreCase)` only | `AgentTaskReplyIntegrationTests.C547_commit_child_audit_accepts_a_gated_commit_under_each_separator` | `=` and `%` arms: `outsideTheGate.ShouldBeFalse()` fails (Warning `was made outside the gate` written); `:` arm green |
| PC-547-2 | `AuditCommitChildAsync`: accept when `CommitMessageAsync` body `Contains("antiphon-commit: gated")` (skip the trailer read) | `..._flags_a_body_prose_mention_without_a_trailer_block` | `SingleAsync` for the Warning throws (no `outside the gate` event) |
| PC-547-3 | `AuditCommitChildAsync`: when `!trailers.Succeeded` continue with an empty list (no unavailable line) | `..._read_failure_is_unavailable_not_outside_the_gate` | `Detail == "commit {sha7} audit unavailable: trailers unavailable"` `SingleAsync` throws; `ShouldNotContain("outside the gate")` fails on the prompt |
| PC-547-4 | `HasExactTrailer`: `Count(...) >= 1` | `GatedCommitServiceTests.C547_HasExactTrailer_requires_exactly_one_key_occurrence` | row `duplicate key, differently cased` `ShouldBeFalse` fails |
| PC-547-5 | `ReadTrailersAsync`: drop the parity check, ignore a trailing unpaired field | `C547_ReadTrailersAsync_odd_field_count_is_malformed_not_partial` | `Succeeded.ShouldBeFalse()` fails |
| PC-547-6 | `Unresolved`: NotNeeded resolves only when `Detail == $"{s.Id:D} {GatedCommitOutcome.NothingToCommit}"` | `CommitRecoveryObligationsTests.NotNeeded_whose_detail_starts_with_the_started_id_and_a_space_resolves` | row `CommitFailed` `ShouldBeEmpty` fails |
| PC-547-7 | `Unresolved`: ignore type 36 | `Abandoned_whose_detail_starts_with_the_started_id_resolves` | `ShouldBeEmpty` fails |
| PC-547-8a | `Unresolved`: drop the Committed arm | `Committed_at_or_after_the_start_resolves_and_before_does_not` | rows `same instant`, `+1 tick` `ShouldBeEmpty` fail |
| PC-547-8b | `Unresolved`: a Committed row resolves regardless of `At` | same | row `-1 tick` `ShouldHaveSingleItem` fails |
| PC-547-9a | `TryCommitOnSettleAsync`: never write NotNeeded on `CommitFailed` | `C547_CommitFailed_after_a_verified_empty_search_resolves_and_a_later_settle_proceeds("fixed")` | settle-1 `LoadUnresolvedAsync(...).ShouldBeEmpty()` fails; settle-2 `Status.ShouldBe(Succeeded)` fails (`settlement_recovery_unavailable` holds it Dispatched) |
| PC-547-9b | `TryCommitOnSettleAsync`: write NotNeeded on `CommitFailed` without the search | `C547_CommitFailed_with_a_failed_history_search_leaves_the_obligation_pending` | `AssertC527RecoveryPendingAsync`: `Status.ShouldBe(Dispatched)` fails (settled Succeeded) |
| PC-547-10 | `TryFailOverdueAsync`: remove Gate 1b | `AgentTaskOverdueDeadlineTests.an_overdue_task_with_a_young_commit_recovery_obligation_is_held_before_the_runner_is_asked` | `Status.ShouldBe(Working)` fails (Failed) |
| PC-547-11a | watchdog expiry: call `FailAndNotifyAsync` with `orphaned: null` | `..._hold_expired_is_failed_with_the_obligation_abandoned_by_name` | type-36 `SingleAsync` throws |
| PC-547-11b | `FailAndNotifyAsync`: omit the `git:` argument (keep the warning) | same | `NoteHeader.ShouldContain("git=commit-recovery-abandoned:")` fails |
| PC-547-12 | reconciler: remove the hold gate | `AgentTaskDeadSessionReconciliationTests.a_dead_session_task_with_a_young_commit_recovery_obligation_is_held_and_its_grace_is_kept` | first `Status.ShouldBe(Dispatched)` fails |
| PC-547-13 | reconciler expiry: `orphaned: null` | `..._whose_commit_recovery_hold_expired_is_failed_with_the_obligation_abandoned_by_name` | type-36 `SingleAsync` throws |
| PC-547-14 | `ShouldHold`: `hold <= TimeSpan.Zero` returns `pending.Count > 0` (infinite hold) | `ShouldHold_rules`; `a_non_positive_commit_recovery_hold_fails_immediately_but_still_annotates(0)` | rows `hold 0`/`hold -5` fail; watchdog case `Status.ShouldBe(Failed)` fails |
| PC-547-15a | `RequeueAsync`: drop the guard | `AgentTaskServiceIntegrationTests.retrying_a_task_with_a_pending_commit_recovery_obligation_is_refused_before_the_delegate_is_stopped` | `Should.ThrowAsync<ConflictException>` fails (no exception) |
| PC-547-15b | `RequeueAsync`: evaluate the guard after `StopDelegateAsync` | same | `stopper.Killed.ShouldBeEmpty()` fails |
| PC-547-16 | `RequeueAsync`: with `abandonCommitRecovery` skip the `Abandon` rows | `retrying_with_abandonCommitRecovery_requeues_and_records_the_abandonment` | type-36 `SingleAsync` throws; `LoadUnresolvedAsync(...).ShouldBeEmpty()` fails |
| PC-547-17 | move the guard from `RequeueAsync` into `RetryAsync` | `escalating_a_task_with_a_pending_commit_recovery_obligation_is_refused_the_same_way` | `Should.ThrowAsync` fails |
| PC-547-18a | endpoint: pass `abandonCommitRecovery: false` regardless of the body | `AgentTaskCommitEndpointTests.Retry_with_abandonCommitRecovery_true_requeues_through_the_endpoint` | `StatusCode.ShouldBe(OK)` fails (409) |
| PC-547-18b | endpoint: `request?.AbandonCommitRecovery ?? true` | `Retry_without_the_abandon_flag_is_a_409_commit_recovery_pending_problem("omitted")` | `StatusCode.ShouldBe(Conflict)` fails (200) |
| PC-547-19a | attention arm: use every type-34 row (drop `Unresolved`) | `AttentionServiceTests.a_resolved_commit_recovery_obligation_is_not_listed` | arms `not-needed`, `abandoned`, `committed-after`: `ShouldBeFalse` fails |
| PC-547-19b | attention arm: drop the two-minute gate | `a_commit_recovery_obligation_becomes_visible_only_after_two_full_minutes` | 120 s arm: item present, `ShouldBeFalse` fails |
| PC-547-20 | `TryCommitOnSettleAsync`: `recoveryStarted = Unresolved(recoveryEvents).Count > 0` | `C547_an_unresolved_obligation_from_another_digest_does_not_hold_this_settlement` | `Status.ShouldBe(Succeeded)` fails |
| PC-547-21 | `Unresolved`: Committed resolves only when `c.At == s.At` | `C547_overdue_sweep_holds_a_settled_gated_commit_until_the_rehand_records_it` | after recovery `LoadUnresolvedAsync(task).ShouldBeEmpty()` fails |
| PC-547-22 | `ReadTrailersAsync`: read `%B` and split each line on `:` | `C547_ReadTrailersAsync_parses_each_configured_separator` | `=`/`%` arms `Items.ShouldBe([...])` fail |
| PC-547-23 | `ReadTrailersAsync`: non-zero exit returns `new(true, [], 0)` | `C547_ReadTrailersAsync_git_failure_is_reported_not_empty` | `Succeeded.ShouldBeFalse()` fails |
| PC-547-24 | `ReadTrailersAsync`: empty stdout returns `Succeeded = false` | `C547_ReadTrailersAsync_empty_block_is_success_with_no_items` | `Succeeded.ShouldBeTrue()` fails |
| PC-547-25 | `FindGatedCommitsAsync`: `HasExactTrailer(pairs, identityKey, identity.ToUpperInvariant())` | `Recovery_uses_Git_trailer_separators(":")` | `found.Items.ShouldBe([head])` fails (empty) |
| PC-547-26 | `Unresolved`: NotNeeded/Abandoned resolve by type alone | `Resolution_rows_naming_another_obligation_do_not_resolve` | row `other D id` `ShouldHaveSingleItem` fails |
| PC-547-27 | `TryCommitOnSettleAsync`: `CommitFailed` NotNeeded `Detail = $"{result.Outcome}"` | `C547_CommitFailed_after_a_verified_empty_search_...("fixed")` | `notNeeded.Detail.ShouldBe($"{started.Id:D} CommitFailed")` fails |
| PC-547-28 | `TryCommitOnSettleAsync`: load types 34/35 only | `C547_an_abandoned_obligation_is_invisible_to_a_later_same_digest_settlement` | `Status.ShouldBe(Succeeded)` fails |
| PC-547-29 | `TryFailOverdueAsync`: evaluate Gate 1b after Gate 2 | `..._is_held_before_the_runner_is_asked` | `pulled.ShouldBe(0)` fails (1) |
| PC-547-30 | `FailAndNotifyAsync`: do not append the obligation sentence to `reason` | `..._hold_expired_..._abandoned_by_name` | `FailureReason.ShouldContain(eventId.ToString("D"))` fails |
| PC-547-31 | `FailAndNotifyAsync`: omit the `warning:` argument | same | `Body.ShouldContain("git log --all --reflog")` fails |
| PC-547-32 | `FailAndNotifyAsync`: abandon `orphaned[0]` only | `an_expired_hold_abandons_every_unresolved_obligation` | type-36 `CountAsync(...).ShouldBe(2)` fails (1) |
| PC-547-33 | reconciler hold: `_deadSessions.Forget(task.Id)` before `continue` | `..._is_held_and_its_grace_is_kept` | post-advance `Status.ShouldBe(Failed)` fails (Dispatched) |
| PC-547-34 | `ShouldHold`: `Max(StartedAt)` | `ShouldHold_rules` | row `oldest of two decides` fails |
| PC-547-35 | guard throws `new ConflictException(msg, "commit_recovery_pending")` without extensions | `retrying_..._refused_before_the_delegate_is_stopped` | `ex.Extensions!["obligationEventId"]` throws (null) |
| PC-547-36 | attention arm: `AlertSeverity.Warning` | `a_task_holding_..._is_an_error_row_with_the_recipe` | `Severity.ShouldBe(Error)` fails |
| PC-547-37 | attention arm: `[OpenDrawer, Retry, Cancel]` | same | `Actions.ShouldBe([OpenDrawer, Cancel])` fails |
| PC-547-38 | attention arm: `SinceUtc = task.DispatchedAt` | same | `SinceUtc` range fails |
| PC-547-39 | attention arm: pass the evidence through `Evidence(text, digest)` | same | `Evidence.ShouldContain("\"abandonCommitRecovery\":true")` fails (tail excerpted) |
| PC-547-40 | attention arm: move it after the DeadSession arm | `a_dead_session_holding_a_commit_recovery_obligation_is_listed_as_the_hold_not_the_death` | `Kind.ShouldBe(CommitRecoveryPending)` fails (`DeadSession`) |
| PC-547-41 | attention arm: call the single-task `LoadUnresolvedAsync` per task inside the loop | `commit_recovery_rows_cost_one_events_query_for_the_whole_open_set` | `c4.ShouldBe(c1)` fails |
| PC-547-42 | delete the `CommitRecoveryPending` entry from `ATTENTION_VISUALS` | `attentionVisuals.test.ts` `maps every kind to a label, a colour, an icon and a hint` | `expect(visual, 'CommitRecoveryPending').toBeDefined()` fails |
| PC-547-43 | `CommitRecoveryAbandoned = 37` | `Abandon_builds_the_type_36_row_with_the_prefix_convention` | `((int)row.Type).ShouldBe(36)` fails |
| PC-547-44 | `CommitRecoveryPending = 42` | `a_task_holding_..._is_an_error_row_with_the_recipe` | `((int)item.Kind).ShouldBe(41)` fails |
| PC-547-45 | `CommitRecoveryHoldMinutes` default 240 | `Default_hold_is_720_minutes` | `ShouldBe(720)` fails |
| PC-547-46 | single-task loader: drop the `AgentTaskId == taskId` filter | `CommitRecoveryObligationsLoaderTests.LoadUnresolvedAsync_is_task_scoped_and_applies_the_rule` | result contains task B's obligation: `ShouldHaveSingleItem` fails |
| PC-547-47 | set loader: loop the single-task loader per id | `LoadUnresolvedAsync_for_a_set_runs_one_query_and_omits_tasks_with_nothing_pending` | `commands.ShouldBe(1)` fails (3) |
| PC-547-48 | `FailAndNotifyAsync`: add the `Abandon` rows after `EnqueueAsync` (inside the try) | `an_expired_hold_whose_note_cannot_be_enqueued_still_records_the_abandonment` | type-36 `AnyAsync(...).ShouldBeTrue()` fails |
| PC-547-49 | `TryFailOverdueAsync` Gate 3: drop the `pending.Count == 0 &&` condition | `AgentTaskReplyIntegrationTests.C547_overdue_sweep_past_the_hold_abandons_the_obligation_and_the_parent_hears_it(false)` | `Status.ShouldBe(Failed)` fails (Succeeded via `RecoverFromBindRefusalAsync`) |
| PC-547-50 | `FailAndNotifyAsync`: move the three `Abandon` add lines back to AFTER the `await FailAsync(...)` call (the round-1 defect) | `AgentTaskOverdueDeadlineTests.an_expired_hold_writes_the_abandonment_in_the_same_save_as_the_failed_status` | the save carrying `Status == Failed` holds no Added type-36 entry: the `ShouldContain` on `failing` fails (the rows land in `FailAndNotifyAsync`'s later save) |

### Out of scope

- `RerouteAsync`, `RerouteOnWallAsync`, `AutoEscalateStalledAsync` end to end: one `RequeueAsync`
  seam (G-547-17). The auto-escalate catch that turns the 409 into a Debug line is existing code.
- `-Refine` re-settlement (D-7): V-547-18 pins that a later successful settlement's `Committed` row resolves a foreign-digest obligation (D-1, correct, not a gap); choosing which commit the note names is the deferred D-7 change.
- `CancelAsync` with an open obligation (§ Scope boundaries).
- A real 720-minute wall clock; the arithmetic is `ShouldHold` and rows are back-dated.
- The failure note's inline-idle delivery from within `FailAndNotifyAsync` and pty keystroke
  acceptance: existing queue and pty suites.
- An outbox for the failure note (DL-547-A cut 2): not in this card; the gap is declared and the
  durable abandonment is tested (V-547-21).
- Client `useRetryAgentTask` body change: no hook test exists; the client build (typecheck) is the
  check (V-547-37).
- Docs wording: inspection only (V-547-37).

### Cost

All figures are **estimated** (nothing was built or run in this dispatch); assumptions: one
foreground owner, isolated `bin-<name>/` output, warm NuGet cache, no concurrent
`Antiphon.Agents.Pty.Tests`.

| Ordinary V/R floor, per Code or independent Review pass | Minutes |
|---|---:|
| Setup: restore + build `tests/Antiphon.Tests` into `bin-c547/` | 12 |
| Unit lane `--treenode-filter "/*/*/CommitRecoveryObligationsTests/*"` and `CommitRecoveryObligationsLoaderTests` | 1 |
| `GatedCommitServiceTests` full class (existing + 5 new, real repos, `ProcessSpawnLimit`) | 3 |
| `AgentTaskReplyIntegrationTests` `C527*` + `C547*` (`/*/*/AgentTaskReplyIntegrationTests/C527*` then `/C547*`; ~120 cases, real repos) | 12 |
| `AgentTaskOverdueDeadlineTests` full (15 + 5) | 2 |
| `AgentTaskDeadSessionReconciliationTests` full | 2 |
| `AgentTaskServiceIntegrationTests` `retrying_*`/`escalating_*`/`a_queued_task_cannot_be_retried`/`a_requeued_task_gets_a_fresh_token` | 2 |
| `AgentTaskCommitEndpointTests` full class (one factory boot) | 4 |
| `AttentionServiceTests` full class (Slow) | 6 |
| Vitest `attentionVisuals` + client rebuild | 3 |
| **Per-pass setup + ordinary V/R** | **47** |

Code floor 47; independent ordinary Review floor another 47. Band per pass: 40-60.

| PC floor (Mutation), method-scoped red/restore/green | Controls | Min per cycle | Minutes |
|---|---:|---:|---:|
| C# controls, in-process or single-repo (all but the endpoint and Vitest rows) | 53 | 3.5 | 186 |
| Endpoint controls (PC-547-18a, 18b; each run boots the factory) | 2 | 5 | 10 |
| Vitest control (PC-547-42) | 1 | 1.5 | 2 |
| Mutation setup: snapshot build of `bin-pc/`, one green run of the touched classes | - | - | 20 |
| Restoration inventory, timestamp refresh, evidence | - | - | 15 |
| **Mutation floor, all 56 controls, unbatched** | **56** | - | **233** |

Band 190-260. The six single-file groups above cannot batch within themselves; batching across
files in groups of three (for example one obligations-rule row, one dispatcher row, one attention
row per cycle) shares one build pair per group and saves about 60 minutes. No concurrency saving
is assumed (one managed snapshot).

**Total verification floor** = setup/build 12 + ordinary V/R 35 + every PC red/restore/green 198
+ Mutation setup/evidence 35 = **280 minutes, estimated**, unbatched.

**Plan cost-band check.** The plan priced Mutation at "roughly 24 PCs"; the finalized inventory
has 56 because the bundled candidates split into independently bypassable checks and the D-1/D-3/
D-5 invariants the candidate list left untested (loader scoping, `ShouldHold` arithmetic,
abandonment durability, evidence truncation, arm order) gained controls. Code's per-slice
estimates (S1 0.5 d, S2 0.5 d, S3 1 d, S4 0.5 d) stand; the new fixtures (`OverdueSweepHarness`,
`CountingCommandInterceptor`, three scenario obligation seeders, the endpoint partial) add about
half a day to S3.

--- next stage ---
next: code
handoff: Code implements S1-S4 against the finalized verification design (54 guards, 37 V rows, the OverdueSweepHarness extraction, settlement-scoped ThrowOnce for S2/S3, D-5 evidence passed untruncated, ShouldHold shared predicate) and runs the ordinary V/R floor; Mutation runs the 54 PCs after land.
artifact: docs/superpowers/plans/2026-09-17-card-0547-commit-recovery-hold-and-trailer-audit-plan.md
