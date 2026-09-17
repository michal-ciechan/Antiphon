# CARD-0547: commit-recovery obligations orphaned by watchdog/reconciler/Retry; Commit-child audit trailer spelling mismatch

Investigate task `b355c108`, 2026-09-17, against master `16f4268a` (post CARD-0544 rebase-and-unify, commit `00ed5d88`).
Filed from CARD-0527 round-11 review (task `d17aff7f`, evidence `C:/Antiphon/evidence/review-d17aff7f/review-findings.md`).

## Verdict

Both gaps are **confirmed on current master** with the mechanism reconstructed from code and the live database. CARD-0544's rebase-and-unify did not touch either mechanism. One correction to the card: the human `-Continue` path is **not** an orphaning path; only `Retry` is (see Gap 1, path 3). Both stay **P3**: Gap 1's precondition has occurred **zero times** in production to date, and Gap 2 fires only under a non-default `trailer.separators`, which no config layer on this machine sets.

## Gap 1: three failure paths orphan an unresolved `CommitRecoveryStarted` obligation

### The obligation and its only reader

- The obligation is written before Git mutates: `server/Application/Services/AgentTaskReplyService.cs:3298-3308` saves a `CommitRecoveryStarted` event (ordinal 34, `server/Domain/Enums/AgentTaskEnums.cs:249`) in an **independent scope** so it survives a failed terminal save, then calls `gated.CommitAsync` at `:3309`.
- The obligation's identity is the settlement digest `{task.Id}|{AgentSessionId}|{DispatchedAt:O}|{report}` (`:3153-3154`), stored in `Detail`.
- The **only** reader is the same method, `TryCommitOnSettleAsync` (`:3157-3166`): `recoveryStarted` is true when a `CommitRecoveryStarted` row matches the current digest and no `CommitRecoveryNotNeeded` row (ordinal 35) references it. `git grep CommitRecoveryStarted -- 'server/*.cs'` returns only `AgentTaskReplyService.cs` and the enum. Nothing in `AgentTaskDispatcher`, `AgentTaskService`, `AttentionService`, or `AgentTaskCheckService` reads ordinal 34 or 35.
- `CommitRecoveryNotNeeded` is written only for `NothingToCommit`, `IgnoreRulesChanged`, `IgnoredPathStaged`, `RepositoryBusy` (`:3310-3319`). **`CommitFailed` is not resolved**, so `CommitFailed` followed by a failed terminal save leaves a `CommitRecoveryStarted` row that can never match a commit.

### How an unresolvable obligation keeps the task open

- On re-settlement with the same digest, `recoveryStarted && existing.Items.Count == 0` throws `ServiceUnavailableException("settlement_recovery_unavailable")` (`:3176-3178`). The same code is thrown for `!existing.Succeeded` and `Items.Count > 1`.
- `OnTurnEndAsync` swallows it: `catch (Exception ex) when (ex is not OperationCanceledException)` at `:268` logs a warning. The task stays `Dispatched`, the settlement save never runs.
- The report sweep's arm 0 (`server/Application/Services/AgentTaskDispatcher.cs:2378-2449`) re-hands the same marked boundary once per `ReportSweepRehandSeconds` (60 s) and throws again each time. This is the "pending until reachable again" behaviour the F17 repair document claims.

### Path 1: overdue watchdog (confirmed)

- `FailOverdueTasksAsync` (`AgentTaskDispatcher.cs:1668-1706`) walks every `Dispatched`/`Working` task with a session; `TryFailOverdueAsync` (`:1709-1782`) evaluates `TaskDeadlinePolicy` (`server/Application/Services/TaskDeadlinePolicy.cs:122-158`).
- For an idle session that has already reported, only the **hard ceiling** applies: wall-clock from `DispatchedAt` against `RolePolicyEntry.TimeoutMinutes` (`server/Application/Settings/DelegationSettings.cs:938`, default 240) or `DefaultTimeoutMinutes` (`:379`, 240). The phase clocks require `IsWorkingAsync` (`TaskDeadlinePolicy.cs:163-168`).
- Gates before failing (`:1719`, `:1731`, `:1742`, `:1746`) consult `ApiErrorRecoveries`, the runner transcript, bind refusal and the boot stall. **None reads `AgentTaskEvents`.** `:1781` calls `FailAndNotifyAsync(task, reason, "overdue-task deadline", ct)`.
- Window: a task that reported at 3h50m after dispatch has ten minutes of 60 s re-hands before this path fires.

### Path 2: dead-session reconciler (confirmed)

- `AgentTaskDispatcher.cs:1598-1624`: for a dead session with a transcript it calls `_replies.OnTurnEndAsync` (`:1602`) as the "last chance on the Fail path", reloads the task, and if still open calls `FailAndNotifyAsync(task, classified.Reason, "dead-session reconciler", ct, ...)` at `:1618`.
- Because `OnTurnEndAsync` swallows `settlement_recovery_unavailable` at `AgentTaskReplyService.cs:268`, the reconciler sees an open task and fails it. The reconciler cannot distinguish "no report" from "report with an unresolved gated-commit obligation".

### What the failure note carries

- `FailAndNotifyAsync` (`AgentTaskDispatcher.cs:2255-2290`): `FailAsync`, `RemoveEphemeralAgentAsync`, `SaveChangesAsync`, then `DelegationReportFormatter.BuildCompletionNote(task, _settings, reason, land: LandCompletionFacts.LoadAsync(...))`.
- `LandCompletionFacts.LoadAsync` returns null for non-Worktree tasks (`server/Application/Services/LandCompletionFacts.cs:14`). No `git:` header, no `Committed`, no `CommitRecoveryStarted` fact reaches the parent. The reason text names the session (`:1776-1779`), which is the "loud" part.

### Path 3: human Retry (confirmed); `-Continue` (not applicable)

- `AgentTaskService.RetryAsync` (`server/Application/Services/AgentTaskService.cs:1854-1886`) refuses only `Queued`; a `Dispatched` task with a pending obligation is retryable. It calls `RequeueAsync` (`:2286-2340`), which sets `AgentSessionId = null` (`:2299`) and `DispatchedAt = null` (`:2309`). The next attempt's digest therefore never equals the stored `Detail`, so the old obligation is never consulted again. Events are kept (nothing deletes them), so the row remains as forensics.
- `-Continue` (`scripts/delegate.ps1:680-684`) posts `/api/agent-tasks/{id}/continue` (`server/Api/Endpoints/AgentTaskEndpoints.cs:137-146`) which calls `AgentTaskReplyService.ContinueWithAuthorityAsync` (`AgentTaskReplyService.cs:385-398`). It **requires `Status == Blocked`** and does not requeue. `CommitOnSettleEligibility.IsEligible` (`server/Application/Services/CommitOnSettleEligibility.cs:9-15`) requires `Succeeded`, so a `Blocked` settlement never creates an obligation. `-Continue` cannot orphan one. The card and the review's "Retry/Continue" wording should read "Retry" (and `RequeueAsync`'s other callers: reroute `:1957`, `:2178`; escalate `:2055`).

### Additional path noted (reconstructed, not reproduced)

The digest includes the report text. Any later settlement of the same session on a **different** report (for example after a human `-Refine` steer produces a new marked turn) computes a new digest, finds no obligation, and proceeds. The earlier gated commit is then in history but unrecorded, and because the footprint is now clean the parent receives `landed`. This is the same identity-keying limit as Retry, reached without a status change. Human-driven; not observed.

### CARD-0544's effect: none on these paths

- `git diff 8b3931e9..HEAD -- AgentTaskDispatcher.cs` (reviewed SHA to master) shows only the Interim hold (`:88-150`, `:3065-3080`, `:4175-4197`). `FailAndNotifyAsync`, `TryFailOverdueAsync` and the reconciler are byte-identical to what round 11 reviewed.
- `TaskCompletionNotification.Applies` (`server/Application/Services/TaskCompletionNotification.cs:36-41`) covers only profile-v1 Code/Review settlements and is minted only inside reply settlement (`AgentTaskReplyService.cs:806-808`, `:1944`). The dispatcher's failure paths do not mint it.
- The unified `LandNotificationKind.TaskCompletion` (`server/Domain/Enums/LandingEnums.cs:15`) only changes which producer mints the outbox row on a **successful** settlement (`AgentTaskReplyService.cs:1560-1590`). Gap 1's paths never reach that code.

### Live database evidence (antiphon-postgres, 2026-09-17)

| Fact | Value |
|---|---|
| `AgentTaskEvents` rows of type 33/34/35 (Committed / RecoveryStarted / NotNeeded) | 0 |
| Tasks total (since 2026-08-09) / Failed | 4660 / 335 |
| Failed by the 240-minute ceiling ("Ran 4h00m against the 240-minute ceiling") | 7 |
| Failed by phase deadlines ("Mid-turn and waiting on the model") | 72 |
| Failed by the dead-session reconciler ("Session died before the task settled") | 39 |
| `Retried` events (type 7) | 7 |
| Running server SHA (`GET /api/version`) | `f091e84d` (includes CARD-0527, landed `e2a49f3a` 01:18Z) |
| Shared tasks with `RepoPath` succeeded since that server started (08:29Z) | 2, neither with a `committed:`/`uncommitted:` header |
| `AgentTaskLandNotifications` of kind `TaskCompletion` (6) | 1, profiled (CARD-0544), task `4435b705` Review/Worktree |

The gate has produced no commit in production yet, so the precondition (an unresolvable pending recovery) has never occurred. The three terminating paths fire routinely (118 failures across them), so once the gate commits regularly the interaction is reachable at the 240-minute ceiling and on every runner restart.

### Severity confirmation

P3 stands. Direction is a loud `Failed` naming the session, not a false `Succeeded`; the `CommitRecoveryStarted` row and the trailer-bearing commit (findable by `FindSettlementCommitsAsync` on the stored digest) survive; no occurrence to date. It is not more likely after CARD-0544. Test coverage: no C527 test exercises overdue, reconciler or Retry against a pending recovery (`grep -rn "overdue|dead-session|Retry|RequeueAsync" tests/Antiphon.Tests/Application/AgentTaskReplyC527*.cs` is empty).

## Gap 2: Commit-child audit trailer spelling mismatch

### Current code

- `AuditCommitChildAsync` (`AgentTaskReplyService.cs:3417-3487`), called only for `Role == Commit` at `:763-768`. For each SHA in `CommitBaselineSha..HEAD` it reads the raw message via `GitWorkspaceService.CommitMessageAsync` (`server/Application/Services/GitWorkspaceService.cs:909-913`, `git log -1 --format=%B`) and at `:3466-3467` tests `message.Contains("antiphon-commit: gated") || message.Contains("antiphon-commit=gated")` (ordinal-ignore-case). A miss adds the `Warning` event and parent line `commit <sha7> was made outside the gate` (`:3469-3471`); no incident.
- Trailers are written with `git commit --trailer key=value` (`GitWorkspaceService.cs:829-833`), so Git formats them with the configured separator.

### Reproduced with Git 2.50.1.windows.1 (scratch repository, 2026-09-17)

| `trailer.separators` | Raw `%B` line written | Parsed `%(trailers:only,unfold,key_value_separator=%x00,separator=%x00)` |
|---|---|---|
| `:` (default) | `antiphon-commit: gated` | `antiphon-commit\0gated` |
| `=` | `antiphon-commit= gated` | `antiphon-commit\0gated` |
| `%` | `antiphon-commit% gated` | `antiphon-commit\0gated` |

The `=gated` spelling (no space) never appears in Git's own output; under any non-colon separator every gated commit is flagged "made outside the gate". `Contains` over the whole body also accepts a prose mention as proof of gating (false negative). No config layer on this machine sets `trailer.separators` (`git config --get` repo/global/system all unset), so the default `:` spelling matches today.

The no-space spelling originates in the plan (`docs/superpowers/plans/2026-09-15-card-0527-commit-on-settle-plan.md:211`). The only test, `AgentTaskReplyC527Tests.C527_commit_child_audit_flags_a_commit_without_the_gate_trailer` (`tests/Antiphon.Tests/Application/AgentTaskReplyC527Tests.cs:491-517`), commits with no trailer under default separators; no separator variant exists. Both the Code (67a63a4f) and Review (d17aff7f) records list this as unqualified.

### Is the round-10 shared resolver reusable?

- `GitWorkspaceService.FindGatedCommitsAsync` (`:945-990`) is **private** and shaped as a search: `(repo, taskId, identityKey, identity) -> SHAs`, used by `FindSettlementCommitsAsync` (`:934-936`) and `FindCommitOperationAsync` (`:938-940`). The audit's question is the inverse: given a SHA, is it gated?
- The genuinely reusable part is `:971-988`: per-SHA `git log -1 --format=%(trailers:only,unfold,key_value_separator=%x00,separator=%x00)`, NUL split, even-count check, and the `Exact(key, value)` rule (exactly one occurrence of the key, value equal). None of that depends on the search.
- Adaptation is modest: lift those lines into a per-SHA trailer reader on `GitWorkspaceService`, have `FindGatedCommitsAsync` consume it (no behaviour change; V-80/V-81 still cover it), and have the audit call it instead of `CommitMessageAsync` + `Contains`. A failed trailer read should be treated like the existing "audit unavailable" branch (`:3427-3433`), not as "outside the gate".
- A Plan decision: the Commit child commits through `POST /api/agent-tasks/{id}/commit` with `antiphon-task = <child id>` (`AgentTaskEndpoints.cs:236-241`), and `CommitBaselineSha` is HEAD at child creation, after the parent's own gate attempt (`AgentTaskReplyService.cs:3396-3399`). The audit could therefore require `antiphon-task == child.Id` as well as `antiphon-commit == gated`, which would also close the prose false negative. Today it checks gate presence only.

### Severity confirmation

P3, cosmetic. Warning-only, conservative direction, only under non-default separators, unchanged by CARD-0544 (no diff to `AuditCommitChildAsync` since the reviewed SHA).

## Remaining uncertainties

- Gap 1 was reconstructed from code and DB, not reproduced end-to-end: no production task has entered the gate yet, and no test rig exists for watchdog-versus-pending-recovery. A reproduction would seed a `CommitRecoveryStarted` row with a digest whose commit is absent, advance the clock past 240 minutes, and run `FailOverdueTasksAsync`.
- Whether the additional `-Refine` re-settlement path is worth guarding is a Plan call; it was not observed.
- The plan wording "Retry/Continue" in the card should be corrected to "Retry (and reroute/escalate requeues)".

## Not done, noted

- Fix idea, Gap 1: one shared "unresolved commit recovery" query consulted by `TryFailOverdueAsync` (before `:1781`), the reconciler (before `:1618`) and `RetryAsync` (before `RequeueAsync`), holding with an Attention row or at minimum carrying the event id and settlement digest into the failure note; also resolve `CommitFailed` with a `CommitRecoveryNotNeeded`-style row so it cannot pend forever.
- Fix idea, Gap 2: extract the per-SHA trailer parse from `FindGatedCommitsAsync` and use it in `AuditCommitChildAsync`, with a separator-variant test (`:`/`=`/`%`) alongside the existing no-trailer case.
