# CARD-0633: remote prep off the serial claim path, honest recovery eligibility, owned sweep lifetime

Date: 2026-09-23. Stage: Plan, with the verification design folded in so the Code rounds can run
its `### Checkpoints` table directly. Base inspected: `68d4383c` (origin/master), which contains
the emergency commits `912b195e` (sweep timeout), `ea6b603f` (RequestAsync budget) and the revert
`587fd147`. Review: task `a9164677` (Codex, reviewed `ea6b603f`), whose four findings are this
card's items 1-4. Related cards read: CARD-0629 (the outage), CARD-0632 (backoff, claim held across
the RPC; folded into this plan), CARD-0631 (runner always replies; lazy clone), CARD-0628 (Claude
on server2; ProviderAuth op 22 and the auth pre-flight in the dispatcher), CARD-0604 Cut B
(`741c8c0e`, landed).

This plan changes no production source. Every new test below is a **predicted baseline-red**
test; nothing was built or run in Plan.

## What is wrong, in one paragraph each

**Serial starvation and the transaction across the RPC.** `AgentTaskDispatcher.TickAsync` walks
every Queued task oldest-first and calls `DispatchOneAsync`, which opens the claim transaction
(`SELECT ... FOR UPDATE`) and, for a runner-bound task, awaits `PrepareRemoteWorkspaceAsync` ->
`RemoteWorkspaceService.PushBranchAsync` (git push to origin) and `MirrorAsync` ->
`PhoneHomeLiveConnection.RequestAsync` INSIDE that transaction. Since `ea6b603f` the mirror wait is
bounded at five minutes, so a silent runner no longer freezes dispatch forever, but each silent
runner task still holds the serial loop for five minutes per tick, the row lock stays held for
those five minutes (which is why `POST /cancel` 500'd), and a failed mirror is requeued with a
Warning and retried on the very next tick with no backoff. Three silent runner tasks ahead of a
local task delay it by fifteen minutes, every tick.

**False recovery eligibility.** `PhoneHomeRecoveryPump.CatchUpAsync` swallows a failed or
timed-out `ListAsync` (`catch ... return;`) and `ExecuteAsync` then calls `MarkRecovered`
unconditionally, so a runner whose List reply never arrives becomes dispatch-eligible with no
transcript catch-up. Before `ea6b603f` the List hung forever instead; the budget turned an
infinite wait into a wrong answer.

**Sweep abandonment reused the scoped DbContext.** `912b195e` wrapped each sweep in
`Task.WhenAny(sweep, delay)` and abandoned the loser. The abandoned sweep kept using the tick's
scoped `AppDbContext` (`_db`, 157 use sites in the class) while later sweeps and the dispatch loop
used it too, and the hosted service disposed the scope under it at tick end. `587fd147` reverted
the timeout; the class now has no per-sweep bound at all again.

**No checkpoint evidence.** The emergency commits carry one test (`PhoneHomeConnectionTests.
Unanswered_request_times_out_instead_of_waiting_forever`, Health only) and no plan or CP-n lines.

## Ground truth

| Card assumption / required outcome | Code and evidence at the inspected base | Consequence |
|---|---|---|
| The dispatcher awaits the runner inside its claim transaction | `DispatchOneAsync` (`AgentTaskDispatcher.cs:3419-3895`): `BeginTransactionAsync` at `:3433`, `PrepareRemoteWorkspaceAsync` at `:3647` with the transaction open; rollback + `ChangeTracker.Clear()` on null | D-4/D-5: commit the claim (task still Queued) before any network await; the RPC moves to an owned background operation |
| The push is a local operation | `RemoteWorkspaceService.PushBranchAsync` runs `git push -u origin <branch>` through `ILandingGit.RunAsync` (`LandingGit.cs:39`, 5-minute budget) | The push is network too; it moves with the mirror into the background operation |
| A mirror failure is retried with backoff | `PrepareRemoteWorkspaceAsync` (`:4611-4675`) returns null on any failure; the tick requeues (rollback) and the next tick retries immediately; `RemoteWarnAsync` writes one `Warning` event per attempt, every 5 s, forever | D-6/D-7: exponential per-task backoff persisted on the row; the recurring condition becomes a deduplicated `Held` trace |
| A disconnected runner is a hold | `_runners.Resolve(runnerId)` throws `ServiceUnavailableException` when not live or not `DispatchEligible` (`PhoneHomeRunnerDirectory.cs:61-71`); the dispatcher turns it into a `Warning` per tick (`:4625-4632`) | D-5 gate (c): a `Held` trace through `TraceHeldAsync` (deduplicated, age-escalated by CARD-0535) |
| The claim rollback returns the task cleanly to Queued | The rollback also discards the in-memory `WorktreePath` that `_worktrees.CreateForTaskAsync` set at `:3585` in the same transaction; whether the next tick's re-creation tolerates the existing directory was not verified here | D-5 commits the claim after worktree creation, so the desktop worktree is recorded exactly once |
| Cancel of a Queued runner task 500s | `AgentTaskService.CancelAsync` (`:2019-2043`) loads the row with tracking and `SaveChangesAsync`; the UPDATE blocks on the dispatcher's `FOR UPDATE` lock until the command timeout | D-8: with the claim committed before the RPC the lock is gone; V-14 proves cancel returns while the mirror is pending |
| `MarkRecovered` follows a successful catch-up | `PhoneHomeRecoveryPump.ExecuteAsync:54-60` calls `CatchUpAsync` then `MarkRecovered` unconditionally; `CatchUpAsync:82-90` returns on List failure at Debug level | D-2: `CatchUpAsync` returns `bool`; recovered only on true; retry with a delay |
| The pump loop is cheap when catch-up fails | The loop spins every 50 ms (`:50`, `:71`); a fast-failing List would send a request every 50 ms | D-2: `NextCatchUpAt` from `CatchUpRetrySeconds` (default 5) |
| Every request to the runner is bounded | `PhoneHomeLiveConnection.RequestTimeoutFor` (`:77-82`): mirror/remove 5 min, launch 2 min, others 60 s; `Task.Delay(timeout, Clock, linked.Token)` uses the connection's `TimeProvider` | Tests drive the budget with `FakeTimeProvider` on the phone-home host; no sleeps |
| The sweep loop has a timeout | `RunSweepAsync` (`:1278-1293`) is a bare `await sweep(ct)` with a catch; `912b195e` is reverted | D-3: owned scope per sweep, cooperative budget, abandonment only on an owned scope, never on the shared context |
| A sweep can run on any DbContext | Every sweep is an instance method on `_db`; `AgentTaskDispatcher` is `AddScoped` (`Program.cs:375`) and the hosted service already resolves one per tick (`AgentTaskDispatcherHostedService.cs:41-44`) | D-3 resolves a fresh dispatcher from an owned `AsyncServiceScope` for each sweep; the sweep methods are unchanged |
| Tests construct the dispatcher without a scope factory | ~60 harnesses in `tests/Antiphon.Tests` build their own graph through `DelegationTestServices`; `scopeFactory` is an optional constructor argument | D-3 fallback: with no `_scopeFactory` the budget cancels and awaits; nothing is abandoned |
| The runner mirror is idempotent for the same sha | CARD-0631 plan ground truth: `RunnerWorkspaceService.MirrorAsync` reuses an existing mirror at the same HEAD and refuses a different one | D-4's in-memory in-flight registry is restart-safe: a re-armed request after a restart is answered with the same mirror |
| CARD-0628 and CARD-0631 touch the same dispatcher | `origin/feat/card-task-2a1af42b` (0628, tip `35c3b5b2`) adds `IAgentTaskLaunchSink`, `TryFailClaudeCredentialProbeAsync` with `commitBeforeNotify`, `RefuseUnsupportedStart(..., remoteControl:)` and `_phoneHome.Project(spec, agent, remoteCwd)`; `origin/feat/card-task-08dbc127` (0631, tip `9618e7a7`) is runner-side only. Both are behind `origin/master` | Coordination table below; none of their hunks overlap D-3/D-5's regions except the launch-sink lines near `:3835` |
| The inherited red is known | Review `a9164677`: one Grok credential assertion in its "other dispatcher" group (25 methods) fails identically at `e992897c` | Not in this closed list; if a CP roster meets it, name it and reproduce at base before attributing |
| Existing phone-home tests give the fixtures needed | `PhoneHomeTestHost` (+ `PhoneHomeScriptedPeer` with `AutoReply`, `Reply` hook returning null = silence, `Sessions`, `Transcripts`), `PhoneHomeEventPumpTests` (pump + isolated schema), `QueuedReceiptAssertions.ConfirmQueuedReceiptAsync` (complete matching UserPrompt with busy/idle/queue-inserted/lost-wakeup cuts), `PostLandMutationDeliveryTests.DispatchWorkerBriefAsync` (tick -> worker session -> adapter) | No new fixture family; one new `SilentMirrorPeer` helper on the scripted peer |

## Decisions

- **D-1 — Three bounded Code rounds, each under 90 minutes with one ~3-minute checkpoint.**
  Round A: recovery eligibility (S1) and sweep lifetime (S2). Round B: the background preparer,
  the row columns and the dispatcher rewiring with backoff (S3-S5). Round C: the starvation proof
  with delivery cuts and the restart cut, plus documentation (S6-S7). Rejected: one Code dispatch
  (three unrelated failure classes in one review, over 90 minutes) and running the phone-home or
  dispatcher lanes broadly (25-minute assembly; the operator's policy caps ordinary verification
  near three minutes per round).
- **D-2 — A runner is dispatch-eligible only after a successful catch-up List.** `CatchUpAsync`
  returns `bool`: false when `ListAsync` throws or times out (`phone_home_request_timeout`); the
  pump marks recovered only on true and sets `NextCatchUpAt = now + CatchUpRetrySeconds` (new
  `PhoneHomeRunnerSettings.CatchUpRetrySeconds`, default 5, validated >= 1) on false so a fast
  failure is not a 50 ms hot loop. A per-session transcript failure is logged at Warning with the
  session id and counted, but does not block eligibility. Reason: List is the owner inventory, and
  without it owner matching is blind; one dead session must not fence the whole runner forever.
  Rejected: requiring every transcript (a permanent fence on one bad session) and keeping the
  Debug level (the incident was invisible in the logs).
- **D-3 — Each sweep runs on an owned lifetime; abandonment is legal only there.** `RunSweepAsync`
  takes `Func<AgentTaskDispatcher, CancellationToken, Task<int>>`. With `_scopeFactory` present it
  creates an `AsyncServiceScope`, resolves a fresh `AgentTaskDispatcher` from it and runs the sweep
  on that instance (its own `AppDbContext`, same singletons). A linked token is cancelled after
  `DelegationSettings.SweepBudgetSeconds` (default 60); after `SweepAbandonGraceSeconds` more
  (default 30) the sweep is abandoned, counted as one failure and logged at Error, and a
  continuation disposes the owned scope when the sweep eventually completes. A per-sweep-name
  in-flight guard skips (and counts, Error with age) a sweep still running from a previous tick,
  so a reproducible hang leaks at most one scope per sweep name. Without `_scopeFactory` (most
  test harnesses) the budget still cancels, but the sweep is always awaited and never abandoned.
  Both settings are validated > 0. Rejected: `912b195e`'s abandonment on the shared `_db` (the
  defect); a cooperative budget alone (an uncancellable hang freezes dispatch, the incident's
  shape); refactoring the 157 `_db` sites to per-sweep contexts (the owned scope gets the same
  isolation without touching the sweep bodies).
- **D-4 — Remote preparation becomes an owned background operation.** New singleton
  `RemoteWorkspacePreparer` (`server/Application/Services/RemoteWorkspacePreparer.cs`):
  `bool TryBegin(Guid taskId)`, `bool IsInFlight(Guid taskId, out DateTime since)`,
  `Task WhenIdleAsync(CancellationToken)` (test seam). Each operation creates its own
  `AsyncServiceScope`, resolves `RemoteWorkspaceService` and `AppDbContext`, loads the task by id
  (`AsNoTracking`), runs `PushBranchAsync` then `MirrorAsync` under a token linked to
  `IHostApplicationLifetime.ApplicationStopping`, and writes the outcome with `ExecuteUpdateAsync`
  keyed by task id: success sets `RemoteWorktreePath`, `RemotePrepFailures = 0`,
  `DispatchNotBeforeAt = null`; failure adds a `Warning` event with the reason and sets
  `RemotePrepFailures + 1` and `DispatchNotBeforeAt = now + Backoff(n)`. The registry entry is
  removed in `finally`; shutdown cancellation writes nothing (the next process re-arms). Rejected:
  a new `AgentTaskStatus` (every `Status == Queued` query and the board would change); a persisted
  in-flight column (needs stale-owner expiry; the in-memory registry is restart-safe because the
  same-sha mirror is idempotent); a shorter synchronous budget inside the tick (still serial, still
  starves).
- **D-5 — `DispatchOneAsync` never awaits the runner, and the tick holds cheaply before claiming.**
  For a task with `RunnerId`, `RemoteWorktreePath == null` and no `SourceLandingOperationId`, the
  claim proceeds as today through worktree creation, then `ConcurrencyToken` is rotated, the claim
  is committed with the task still Queued, `_remotePrep.TryBegin(id)` is called after the commit,
  and a new `DispatchOneResult.HeldForRemotePrep` is returned; the tick traces
  `DispatchHoldDetails.RemoteMirrorRequested(runnerId, since)` through `TraceHeldAsync`
  (`HoldKind.RemotePrep`). Before claiming, three gates run in the tick loop for runner-bound tasks
  and `continue` with a deduplicated `Held` trace: (a) `DispatchNotBeforeAt > now`
  (`HoldKind.RemotePrepBackoff`, detail names the failure count and the instant); (b)
  `_remotePrep.IsInFlight` (`HoldKind.RemotePrep`); (c) `_runners.Resolve(runnerId)` throws
  (`HoldKind.RunnerUnavailable`, detail carries the exception message). With `RemoteWorktreePath`
  set, `PrepareRemoteWorkspaceAsync` returns it with no RPC; it keeps the SourceLanding snapshot
  branch and the "remote workspaces unavailable in this process" guard, and loses the push and
  mirror. `RemoteWarnAsync` remains only for the no-live-store-identity case. The existing hold
  age escalation (CARD-0535) applies to all three new kinds unchanged. Rejected: a Warning event
  per tick (the current event spam, twelve rows an hour per task).
- **D-6 — Exponential per-task backoff, capped, reset on success.** `Backoff(n) =
  min(RemotePrepBackoffBaseSeconds * 2^(n-1), RemotePrepBackoffMaxSeconds)` with defaults 30 and
  900 on `DelegationSettings`, validated base >= 1 and max >= base. Reason: with CARD-0631 landed
  the runner answers a broken mirror with an error frame immediately, so without backoff the
  failure loop is a 5 s spin of push + error; with a silent runner it is one 5-minute budget per
  attempt, now off the serial path but still wasteful. Rejected: failing the task after N attempts
  (CARD-0604 D-15's contract is Queued with a warning, never a silent local fallback or a
  spontaneous failure while the operator may be fixing the runner); a fixed delay.
- **D-7 — Two new columns, one migration.** `AgentTask.RemotePrepFailures` (`int`, not null,
  default 0) and `AgentTask.DispatchNotBeforeAt` (`timestamptz`, null), migration
  `AddRemotePrepBackoff` created with `dotnet ef migrations add AddRemotePrepBackoff --project
  server` (docs/project-context.md rule 9), then `dotnet ef migrations
  has-pending-model-changes --project server` expecting no changes. `DispatchNotBeforeAt` is
  generic on purpose so a later hold class can reuse it. Rejected: deriving both from `Warning`
  events (a per-tick event scan; a count read from prose is fragile).
- **D-8 — Cancel and late success.** With the claim committed before the RPC, `CancelAsync` no
  longer blocks on a row lock. A mirror that succeeds after the task was cancelled still records
  `RemoteWorktreePath` (the update is keyed by id, not by status) so
  `RemoteWorkspaceService.RemoveMirrorAsync` can remove it at retirement instead of leaving
  runner residue. Rejected: conditioning the update on `Status == Queued` (a successful mirror
  nobody knows about is residue by construction).
- **D-9 — Clocks in tests.** The phone-home host and its live connection take a
  `FakeTimeProvider`, so the 5-minute mirror budget and the 60-second List budget are advanced,
  never slept. Dispatcher-only tests (Round B) give the dispatcher the same fake clock so
  `DispatchNotBeforeAt` gating is deterministic. The Round C delivery test keeps the
  `BridgeQueueHarness` on the system clock (the message queue's flush and receipt paths are
  proven on it in `PostLandMutationDeliveryTests`) and puts the fake clock on the phone-home host
  only; that test asserts starvation and eventual failure, and leaves not-before gating to V-10.
- **D-10 — Log levels.** A recurring hold is a deduplicated `Held` trace, never a per-tick
  Warning; a sweep over budget, abandoned or still running is Error and counts into
  `TickResult.SweepFailures` (the hosted service already logs DEGRADED from that); a failed
  catch-up List is Warning with the runner id and the transport code.
- **D-11 — Checkpoint evidence for the emergency commits.** CP-1 executes `ea6b603f`'s test
  class (`PhoneHomeConnectionTests`, 14 methods) and the new sweep tests; CP-2 and CP-3 execute
  the dispatcher-side rosters. Each Code round reports its CHECKPOINT line; that is the evidence
  item 4 asks for. The inherited Grok credential red stays out of the closed list by name.
- **D-12 — Land order and rebase.** Round A touches only `PhoneHomeRecoveryPump.cs`,
  `PhoneHomeRunnerSettings*.cs`, `DelegationSettings.cs` and `RunSweepAsync`'s region, none of
  which CARD-0628 or CARD-0631 edit; it can land first. Round B's `DispatchOneAsync` hunk sits
  between the worktree-creation block and the `ResolveAgentAsync` call, and the tick-loop gates sit
  before the `try { ... DispatchOneAsync }` block; CARD-0628's dispatcher hunks (launch sink,
  Claude probe, `remoteControl:` argument, `Project(spec, agent, remoteCwd)`) do not intersect
  them, so the merge is textual, not semantic. If CARD-0628 lands first, Round B keeps its
  `remoteCwd` variable feeding `Project`; if Round B lands first, CARD-0628 rebases onto it and its
  `PhoneHomeTaskDispatchProjectionTests` must run a second tick after `WhenIdleAsync()` (the
  first tick now returns `HeldForRemotePrep`). That is one line in its test and is named here so
  neither side is surprised.

## Coordination with in-flight cards

| Shared file / area | Other card's change | This card's change | Merge rule |
|---|---|---|---|
| `AgentTaskDispatcher.cs` constructor | 0628 adds `IAgentTaskLaunchSink? taskLaunchSink` | adds `RemoteWorkspacePreparer? remotePrep` | Both optional trailing parameters; keep both, any order |
| `AgentTaskDispatcher.cs` `DispatchOneAsync` `:3641-3656` | 0628 unchanged here | D-5 commit-then-`TryBegin`, new result | No overlap; 0628's `Project(spec, agent, remoteCwd)` at `:3835` keeps reading `remoteCwd` |
| `AgentTaskDispatcher.cs` credential pre-flight `:3554-3563` | 0628 adds `TryFailClaudeCredentialProbeAsync` + `commitBeforeNotify` | none | Take 0628's version |
| `AgentTaskDispatcher.cs` `RunSweepAsync` `:1278-1293` and `TickAsync` `:265-350` | none | D-3 signature and lambdas | Take this card's version |
| `AgentTaskDispatcher.cs` tick loop before `try` `:520-620` | none | D-5 gates (a)-(c) | Take this card's version |
| `PhoneHomeRunnerSettings.cs` / validator | 0628 adds `ClaudeAuthProbeEnabled`, `ChildClaudeHome` | adds `CatchUpRetrySeconds` | Additive; keep both |
| `DelegationSettings.cs` / validator | none | `SweepBudgetSeconds`, `SweepAbandonGraceSeconds`, `RemotePrepBackoffBaseSeconds`, `RemotePrepBackoffMaxSeconds` | Additive |
| `tests/.../PhoneHomeTaskDispatchProjectionTests.cs` (0628 branch) | first tick dispatches | first tick returns held; needs `await preparer.WhenIdleAsync(); await dispatcher.TickAsync()` | Whoever lands second adds the second tick (D-12) |
| Runner (`src/Antiphon.SessionRunner/*`) | 0631 always replies, lazy clone, op 22 | none | No overlap; 0631's error replies are what D-6's backoff absorbs |

Preferred order: Round A lands independently; CARD-0628 lands; Round B rebases onto both and
runs CP-2 at the rebased commit (a justified rerun); Round C follows Round B.

## Implementation slices

| Round / slice | Work and files | Tests and exit |
|---|---|---|
| A / S1: recovery eligibility, ~20 min | `server/Infrastructure/Agents/SessionRunner/PhoneHomeRecoveryPump.cs`: `CatchUpAsync` -> `Task<bool>`, new internal `RunCycleAsync(ct)` returning whether it marked recovered, `NextCatchUpAt` keyed by the live instance, Warning logs. `server/Application/Settings/PhoneHomeRunnerSettings.cs` + `PhoneHomeRunnerSettingsValidator.cs`: `CatchUpRetrySeconds`. First commit is the baseline-red seam (old semantics: mark regardless) with the V-1..V-3 tests; second commit is the fix | New `tests/Antiphon.Tests/Application/PhoneHomeRecoveryEligibilityTests.cs` (V-1..V-3); existing `PhoneHomeEventPumpTests` (5) keep passing |
| A / S2: sweep lifetime, ~45 min | `AgentTaskDispatcher.cs`: `RunSweepAsync` made `internal` with the `Func<AgentTaskDispatcher, CancellationToken, Task<int>>` shape, owned scope, budget, grace, in-flight guard (a `ConcurrentDictionary<string, DateTime>` singleton `SweepInFlightState`, registered in `Program.cs` beside `DeadSessionFirstSeenState`), fallback mode; the 13 call sites in `TickAsync` become lambdas `(d, ct2) => d.<Sweep>Async(ct2)` including `RunCompactionSweepAsync`; internal `Db` accessor for tests. `DelegationSettings.cs` + validator: `SweepBudgetSeconds`, `SweepAbandonGraceSeconds`. Baseline-red seam commit first (new signature, old body) | New `tests/Antiphon.Tests/Application/DispatcherSweepLifetimeTests.cs` (V-4..V-6), harness through `DelegationTestServices` with a `FakeTimeProvider` and a real scope factory. Commit S1+S2 before CP-1 |
| B / S3: preparer + columns, ~30 min | New `RemoteWorkspacePreparer.cs`; `AgentTask.cs` two properties; `AppDbContext.cs` entity config (default 0); migration `AddRemotePrepBackoff` + `has-pending-model-changes` clean; `Program.cs` `AddSingleton<RemoteWorkspacePreparer>()`; `DelegationSettings.cs` + validator: the two backoff settings | V-8..V-11, V-13, V-14 are written against S3+S4 together; the seam commit for B is "preparer exists, dispatcher still awaits inline" so V-8/V-14/V-15 go red on the tick's wall clock and cancel latency |
| B / S4: dispatcher rewiring, ~30 min | `AgentTaskDispatcher.cs`: D-5's commit-then-begin in `DispatchOneAsync`, `HeldForRemotePrep`, gates (a)-(c) in the tick loop, `HoldKind.{RemotePrep, RemotePrepBackoff, RunnerUnavailable}`, `PrepareRemoteWorkspaceAsync` trimmed; `DispatchHoldDetails.cs`: `RemoteMirrorRequested`, `RemotePrepBackoff`, `RunnerUnavailable` texts | New `tests/Antiphon.Tests/Application/RemoteWorkspacePreparerTests.cs` (V-8..V-11, V-13, V-14) using `PhoneHomeTestHost` + a `DelegationTestServices` dispatcher graph with the fake clock, a `PushGit` stub (copy the shape from the 0628 branch's projection test) and the real `RemoteWorkspaceService` |
| B / S5: backoff, ~10 min | `RemoteWorkspacePreparer.Backoff(n, settings)` static, validator rows | V-10; commit S3-S5 before CP-2 |
| C / S6: starvation proof, ~40 min | New `tests/Antiphon.Tests/Application/DispatcherRemotePrepStarvationTests.cs` (V-15) on `BridgeQueueHarness` with `ConfigureServices` adding the dispatcher graph, `host.Directory` as `ISessionRunnerDirectory`, `RemoteWorkspaceService`, `RemoteWorkspacePreparer`, `MutationDispatchTestsCapture` as the adapter factory, and the `PushGit` stub; recipient path through `QueuedReceiptAssertions.ConfirmQueuedReceiptAsync` | V-15 with four `[Arguments]` cuts; regression `PostLandMutationDeliveryTests.C478_V09b_AcceptedTaskToWorker` |
| C / S7: documentation, ~15 min | `docs/session-runtime-invariants.md`: two invariants (eligibility after successful List; no runner RPC inside a claim transaction or on the serial tick). `docs/orchestration-loop.md` under "Launching an agent": what a Queued runner-bound task's `Held` traces mean and how backoff reads. `docs/ops-http.md` runner row: the three hold texts. `docs/testing-and-build.md`: one line under the CARD-0490 phone-home section naming the new fixture pattern (silent peer via `Reply => null`) | No test; commit S6+S7 before CP-3 |

Every round commits and pushes its meaningful slices with the real outcome in the message and
reports its CHECKPOINT line at the tested SHA. No daemon restart, image build or live runner is
needed for any round.

## Verification design

### Inspection

Read before coding: `AgentTaskDispatcher.TickAsync` (`:261-796`), `RunSweepAsync` (`:1278`),
`DispatchOneAsync` (`:3419-3895`), `PrepareRemoteWorkspaceAsync`/`RemoteWarnAsync`
(`:4611-4703`), `LoadQueuedHoldIndexAsync`/`TraceHeldAsync`/`EscalateHeldAgeAsync` (`:798-1015`);
`PhoneHomeRecoveryPump.cs` whole; `PhoneHomeLiveConnection.RequestAsync`;
`PhoneHomeRunnerDirectory.Resolve`/`MarkRecovered`; `RemoteWorkspaceService.cs` whole;
`AgentTaskDispatcherHostedService.cs`; `DelegationSettingsValidator`;
`tests/.../TestHelpers/PhoneHomeTestHost.cs` (peer `Reply`/`AutoReply`), `PhoneHomeEventPumpTests`,
`QueuedReceiptAssertions.cs`, `PostLandMutationDeliveryTests.DispatchWorkerBriefAsync`
(`:1439-1500`) and `DispatchOptions` (`:1527`), `DelegationTestServices.cs`, `DispatchHoldVisibilityTests`
(hold-trace assertions), `scripts/run-checkpoint.ps1`, and on the 0628 branch
`PhoneHomeTaskDispatchProjectionTests.cs` for the runner-bound seed and `PushGit` stub.

Missing setup to implement: a silent-for-one-operation peer. `PhoneHomeScriptedPeer.Reply`
returning `null` for `WorkspaceMirror` (and default replies for everything else) is exactly that;
add a tiny helper `SilentFor(PhoneHomeOperation)` on the peer so the tests read plainly. The
starvation harness needs the dispatcher registered inside `BridgeQueueHarness.ConfigureServices`
through `DelegationTestServices.AddDelegationWorktreeGraph` (CARD-0297 rule), never by hand.

### Delivery inventory

| Producer -> destination | Identity and persistence | Failure / recovery | Receipt used here |
|---|---|---|---|
| Tick -> `RemoteWorkspacePreparer` -> runner `WorkspaceMirror` -> `AgentTasks.RemoteWorktreePath` | task id, branch, pushed sha, mirror name `task-<8hex>`; the row is the durable record, the registry entry is memory-only | Timeout/error -> `Warning` event + failures + not-before; process restart -> registry empty, next tick re-arms, same-sha mirror is idempotent; shutdown cancellation writes nothing | Row fields after `WhenIdleAsync`, exact count of `WorkspaceMirror` request frames on the peer, `Held` event details |
| Tick -> `SessionMessageQueueService.EnqueueAsync` -> fake adapter -> `TranscriptEntries` UserPrompt (local recipient) | session id, queued message id, brief body | Existing CARD-0499 cuts: busy caller, idle, queue-inserted, lost-wakeup; the recovered harness redelivers | Complete matching UserPrompt through `ConfirmQueuedReceiptAsync`; count exactly one |
| Pump -> runner `List`/`Transcript` -> `TranscriptEntries`; `MarkRecovered` | live connection instance, runner id + store id owner match | List timeout/error -> not eligible, retry after `CatchUpRetrySeconds`; per-session transcript failure -> Warning, eligibility proceeds | `live.DispatchEligible`, `RunCycleAsync` return value, peer `List` request count |
| Tick -> owned sweep scope -> DB | sweep name; `TickResult.SweepFailures` | Over budget -> cancel; over grace -> abandon on its own scope; still running next tick -> skipped and counted | Return value of `RunSweepAsync`, invocation counter, disposal probe on the owned `AppDbContext` |

There is no new durable queue. The in-flight registry is memory-only by design (D-4) and its loss
is recovered by re-arming, not by replay. The owned-child crash cut of CARD-0574 (a child process
killed mid-dispatch) is not in the three-minute scope; V-13 covers the restart shape in-process by
rebuilding the provider, which loses the registry exactly as a process death would.

### Proves it works now

Exactly fifteen new methods. Baseline-red is against the seam commit of the same round (old
semantics behind the new signature), so every method compiles at the red commit and fails on the
named assertion, never on a fixture error.

| V | Exact new test and file | Setup and decisive outcome | Why baseline is red |
|---|---|---|---|
| V-1 | `Silent_list_reply_leaves_the_runner_ineligible_until_catch_up_succeeds` in `PhoneHomeRecoveryEligibilityTests.cs` | `PhoneHomeTestHost.StartAsync(fakeClock)`; peer `SilentFor(List)`; `pump.RunCycleAsync` started; `peer.WaitForAsync(List)`; advance `RequestTimeoutFor(List) + 1s`; await the cycle -> false, `live.DispatchEligible` false, one `List` frame. Then `peer.Reply = null` (defaults), advance `CatchUpRetrySeconds`, second cycle -> true, eligible true | Old semantics mark recovered after the swallowed timeout: `DispatchEligible.ShouldBeFalse()` fails |
| V-2 | `Failed_catch_up_waits_the_retry_delay_before_asking_again` in the same file | Peer replies an Error frame to `List`; cycle 1 -> false; cycle 2 immediately -> false without a second `List` frame; advance `CatchUpRetrySeconds`; cycle 3 sends the second frame | Old semantics have no delay: the second cycle sends a second `List` (count 2) |
| V-3 | `One_session_transcript_failure_is_a_warning_not_a_fence` in the same file | Isolated schema + `BridgeQueueHarness` (as `PhoneHomeEventPumpTests.Catchup_commits_before_live_release`); two owned sessions; peer answers `Transcript` for the first, Error for the second; cycle -> true, eligible, first transcript persisted, one Warning log entry naming the failed session (recording logger) | Guards D-2's chosen boundary; red at the seam because the seam's `CatchUpAsync` still returns false on any failure in its first shape, and the logger assertion sees Debug, not Warning |
| V-4 | `Abandoned_sweep_runs_on_its_own_scope_and_that_scope_is_disposed_after_it_ends` in `DispatcherSweepLifetimeTests.cs` | Graph with a real scope factory and `FakeTimeProvider`; `RunSweepAsync("probe", (d, ct) => ...)` where the body records `d`, awaits a TCS ignoring `ct`, then calls `d.Db.Database.CanConnectAsync()`; advance budget + grace; the wrapper returns 1 while the TCS is pending (`WaitAsync(5s)` bounds the assertion); `ReferenceEquals(d, tick)` false and `d.Db != tick.Db`; complete the TCS; the in-body DB call succeeded; afterwards `d.Db.ChangeTracker` throws `ObjectDisposedException` | Seam shape awaits forever on `this`: the `WaitAsync(5s)` throws `TimeoutException`; the instance identity assertion also fails |
| V-5 | `A_sweep_still_running_from_the_previous_tick_is_skipped_and_counted` in the same file | Same fixture; first call abandoned as V-4; second `RunSweepAsync("probe", ...)` returns 1 immediately with the body invoked once (counter) and an Error log naming the age; complete the TCS; third call runs the body (counter 2) | Seam has no in-flight guard: counter reaches 2 on the second call |
| V-6 | `Without_a_scope_factory_the_budget_cancels_but_never_abandons` in the same file | Dispatcher constructed with `scopeFactory: null`; body A observes `ct` and throws OCE at cancel -> returns 1 after budget, awaited; body B ignores `ct`: advance budget + grace + 1 min, the wrapper task is still not complete; complete B; wrapper returns 1; `d` is the same instance | Seam never cancels: body A's `ct` is never signalled (`WaitAsync(5s)` red) |
| V-7 | reserved; not used | | |
| V-8 | `Mirror_runs_outside_the_claim_and_the_task_stays_queued_with_an_in_flight_hold` in `RemoteWorkspacePreparerTests.cs` | Fake-clock host, peer recovered and `SilentFor(WorkspaceMirror)`; one runner-bound Worktree task seeded as the 0628 projection test does; `TickAsync` under a 15 s `WaitAsync`; task Queued, `RemoteWorktreePath` null, exactly one `Held` event whose detail starts with `DispatchHoldDetails.RemoteMirrorRequested` prefix, exactly one `WorkspaceMirror` frame; second `TickAsync` -> still one frame, no second `Held` row | Seam awaits the mirror inline with the fake clock frozen: the first tick never returns (`WaitAsync` red) |
| V-9 | `Mirror_success_is_recorded_and_the_next_tick_launches_into_it` in the same file | Peer holds the mirror reply behind a TCS; tick 1 -> held; release the reply; `await preparer.WhenIdleAsync()`; row has `RemoteWorktreePath == "/work/worktrees/task-<8hex>"`, `RemotePrepFailures 0`; tick 2 -> Dispatched, `AgentSessions.RunnerCwd` equals the mirror, `Cwd` equals the desktop worktree | Seam dispatches on tick 1 only if the reply arrives inline; with the held reply tick 1 never returns |
| V-10 | `Mirror_failure_backs_off_exponentially_and_resets_on_success` in the same file | Settings base 30 s, max 120 s; silent peer; tick -> held; advance 5 min; `WhenIdleAsync`; `RemotePrepFailures 1`, `DispatchNotBeforeAt == now + 30s`, one `Warning` whose detail contains `phone_home_request_timeout`; tick -> no new frame, `Held` detail names 1 failure and the instant; advance 30 s -> tick -> second frame; repeat: 60 s, 120 s, 120 s (cap); then peer replies -> success -> failures 0, not-before null | Seam has no backoff: the tick right after the failure sends a second frame (count 2) |
| V-11 | `Runner_not_eligible_is_one_held_trace_not_a_warning_per_tick` in the same file | Host with a connected but NOT recovered peer; three ticks; exactly one `Held` event (detail contains `RunnerUnavailable` and "has not completed recovery"), zero `Warning` events, zero claim (no `Dispatched` worktree event beyond the seed) | Today: `RemoteWarnAsync` writes three `Warning` rows |
| V-12 | folded into V-10 (reset on success) | | |
| V-13 | `A_process_restart_re_arms_the_mirror_and_the_row_is_written_once` in the same file | Tick 1 on provider A -> held, one frame; dispose provider A (registry lost); provider B (same schema, same host) tick -> second frame for the same task with the same branch, sha and name; peer answers both with the same path; `WhenIdleAsync` on B; row has the path once, failures 0 | Seam has no registry to lose: the inline await on A never returned |
| V-14 | `Cancel_of_a_queued_runner_task_returns_while_its_mirror_is_pending_and_a_late_success_is_still_recorded` in the same file | Tick -> held with the mirror pending; `AgentTaskService.CancelAsync(taskId)` completes under a 5 s `WaitAsync` and the row is Canceled; then the peer replies; `WhenIdleAsync`; `RemoteWorktreePath` is set on the Canceled row | Seam holds the `FOR UPDATE` lock across the mirror: cancel blocks (`WaitAsync` red); the CARD-0629 500 in miniature |
| V-15 | `Silent_remote_tasks_ahead_do_not_delay_an_eligible_local_recipient` in `DispatcherRemotePrepStarvationTests.cs`, `[Arguments("after-receipt", false)]`, `[Arguments("after-receipt", true)]`, `[Arguments("queue-inserted", false)]`, `[Arguments("lost-wakeup", false)]` | `BridgeQueueHarness` (system clock, AlwaysOn false, isolated schema) + fake-clock phone-home host with a recovered `SilentFor(WorkspaceMirror)` peer; three runner-bound Worktree tasks created at T-3..T-1 minutes and one local Shared `Raw` task created at T (`DispatchWorkerBriefAsync` shape with `MutationDispatchTestsCapture`); one `TickAsync` under a 20 s `WaitAsync`; the local task has `AgentSessionId`; `AgentSessionLaunchQueue.WaitForIdleAsync`; `ConfirmQueuedReceiptAsync(..., busy, cut)` proves exactly one complete matching UserPrompt on the recipient; the three remote tasks are Queued with `RemoteMirrorRequested` holds and three frames were sent; advance the phone-home clock 5 min; `WhenIdleAsync`; three `Warning` rows and three not-before instants | Seam serialises three frozen 5-minute waits ahead of the local task: the tick never returns (`WaitAsync` red) |

V-15's four cuts are the busy, idle, enqueue (queue-inserted) and lost-wakeup cuts the review
named; the "crash" cut is V-13's provider rebuild (registry loss), stated as such. V-8 and V-15
must bound with `WaitAsync`, never with `Task.Delay` polling on the fake clock.

### Guards the regression

- **R-1:** CP-1 runs all 14 `PhoneHomeConnectionTests` methods (including `ea6b603f`'s
  `Unanswered_request_times_out_instead_of_waiting_forever`) and all 5 `PhoneHomeEventPumpTests`
  methods (owner matching, catch-up-before-live ordering, replay idempotence, overflow recovery).
- **R-2:** CP-2 runs all 6 `RemoteWorktreeMirrorTests` (push/sync/mirror-name contracts the
  preparer now calls), all 6 `PhoneHomeTaskRoutingTests` (runner-bound admission shape) and all
  10 `DispatchHoldVisibilityTests` methods (13 executions: `Held` dedup, age escalation, the
  restart-idempotent escalation that the three new hold kinds must inherit).
- **R-3:** CP-3 runs `PostLandMutationDeliveryTests.C478_V09b_AcceptedTaskToWorker`, the
  existing tick-to-UserPrompt path the starvation test extends.
- **R-4 (named, not run):** the Grok credential assertion the a9164677 review found failing at
  `e992897c` is inherited and outside this closed list.

### Guard inventory

| Guard | Decision / invariant | Control |
|---|---|---|
| G-1 | D-2 recovered only after a successful List | PC-1 |
| G-2 | D-2 failed catch-up waits `CatchUpRetrySeconds` | PC-2 |
| G-3 | D-3 the budget cancels the sweep token | PC-3 |
| G-4 | D-3 no abandonment without an owned scope | PC-4 |
| G-5 | D-3 the owned scope outlives the abandoned sweep | PC-5 |
| G-6 | D-3 a still-running sweep is not started again | PC-6 |
| G-7 | D-5 the claim is committed before the runner RPC | PC-7 |
| G-8 | D-5 the tick never awaits the preparer | PC-8 |
| G-9 | D-6 not-before gate holds the task | PC-9 |
| G-10 | D-6 backoff doubles and caps | PC-10 |
| G-11 | D-4 success resets failures and not-before | PC-11 |
| G-12 | D-5 runner-unavailable is a deduplicated hold | PC-12 |
| G-13 | D-4 one in-flight operation per task per process | PC-13 |
| G-14 | D-4 the registry is per process (restart re-arms) | PC-14 |
| G-15 | D-8 late success is recorded regardless of status | PC-15 |

### Positive controls

Mutation runs after ordinary Review and confirmed land. Every cycle is method-scoped (exact
`--treenode-filter "/*/*/Class/Method"`), compiles, reaches the named red assertion, restores
source and proves the same method green. Code rounds do not spend their checkpoint budget on PCs.

| PC | Compiling mutation | Exact test / expected red assertion |
|---|---|---|
| PC-1 | `RunCycleAsync` calls `MarkRecovered` regardless of the catch-up result | V-1: `DispatchEligible.ShouldBeFalse()` |
| PC-2 | `NextCatchUpAt = now` on failure | V-2: `List` frame count 2 before the delay |
| PC-3 | remove `CancelAfter(SweepBudget)` | V-6 body A: `WaitAsync(5s)` TimeoutException |
| PC-4 | abandon in fallback mode too | V-6 body B: wrapper completes before the body |
| PC-5 | dispose the owned scope at abandonment time | V-4: the in-body `CanConnectAsync` throws `ObjectDisposedException` |
| PC-6 | drop the per-name in-flight check | V-5: invocation counter 2 |
| PC-7 | move `transaction.CommitAsync` after `await preparer.RunAsync(...)` (re-inline the await) | V-14: cancel `WaitAsync(5s)` red |
| PC-8 | `await` the operation's task inside `DispatchOneAsync` before returning `HeldForRemotePrep` | V-15: tick `WaitAsync(20s)` red |
| PC-9 | remove gate (a) | V-10: a second `WorkspaceMirror` frame before 30 s elapsed |
| PC-10 | `Backoff(n) = base` | V-10: `DispatchNotBeforeAt` at attempt 2 is +30 s, expected +60 s |
| PC-11 | omit the reset in the success update | V-10 final step: `RemotePrepFailures` still 4 |
| PC-12 | restore `RemoteWarnAsync` for the unavailable runner | V-11: `Warning` count 3 |
| PC-13 | `TryBegin` always returns true | V-8: two frames after two ticks |
| PC-14 | make the registry `static` | V-13: no second frame after the provider rebuild |
| PC-15 | condition the success `ExecuteUpdate` on `Status == Queued` | V-14: `RemoteWorktreePath` null on the Canceled row |

### Out of scope

Runner-side behaviour (CARD-0631), Claude authentication and the launch sink (CARD-0628), a
persisted in-flight marker, multi-server dispatch coordination, failing a task after N remote-prep
failures, a whole-operation runner budget (CARD-0631 D-12), the CARD-0574 owned-child crash cut,
UI rendering (the board already renders `Held` events generically), and exposing the two new
columns on the task DTO (their content is readable in the `Held` details; add to
`AgentTaskDetailDto` only if a later card needs it). Existing full-suite failures are not in this
closed list; a selected existing test that fails is reproduced on the base before it is
attributed. Never loosen an assertion or widen a deadline to hide a red.

### Cost

Estimated, not measured: ordinary V/R = **3 + 3 + 3 = 9 minutes** across the three rounds, one
isolated build and one filter each. Authoring plus checkpoint: Round A about 75 min, Round B about
85 min, Round C about 65 min (about 225 min over three dispatches). Each round's `-ExpectAbout` is
its authoring estimate plus its 3-minute row. Warm package caches assumed; a cold build or an
over-budget row is reported, not absorbed by widening scope. Mutation: 15 method-scoped cycles at
about 3 min plus about 8 min setup = **53 min**, 30 method executions across red/green, separate
from the ordinary roster.

### Checkpoints

Closed Code list, partitioned by round; execute only the commissioned round's row after its
slices are committed. In table cells `\|` is a literal filter OR; the commands below carry plain
`|`. Counts are TUnit executed results: V-15 is one method with four `[Arguments]` (4);
`DispatchHoldVisibilityTests` is 10 methods, 13 executions.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c633-a/` | recovery-and-sweeps | `/*/*/(PhoneHomeRecoveryEligibilityTests*)\|(DispatcherSweepLifetimeTests*)\|(PhoneHomeEventPumpTests*)\|(PhoneHomeConnectionTests*)/*` | V-1..V-6, R-1 | 3 + 3 new, 5 + 14 existing = 25 methods; 0 failed/skipped | 25 | 3 |
| CP-2 | S3-S5 (after CARD-0628 lands, else rerun at the rebased commit) | `tests/Antiphon.Tests -> bin-c633-b/` | remote-prep | `/*/*/(RemoteWorkspacePreparerTests*)\|(RemoteWorktreeMirrorTests*)\|(PhoneHomeTaskRoutingTests*)\|(DispatchHoldVisibilityTests*)/*` | V-8..V-11, V-13, V-14, R-2 | 6 new + 6 + 6 + 13 existing executions = 31; 0 failed/skipped | 31 | 3 |
| CP-3 | S6-S7 | `tests/Antiphon.Tests -> bin-c633-c/` | starvation-receipt | `/*/*/(DispatcherRemotePrepStarvationTests*)\|(PostLandMutationDeliveryTests*)/(Silent_remote_tasks_ahead_do_not_delay_an_eligible_local_recipient*)\|(C478_V09b_AcceptedTaskToWorker*)` | V-15, R-3 | 4 + 1 executions; 0 failed/skipped | 5 | 3 |

## Checkpoint execution and reporting

Run from the Code worktree, foreground, once per row; the wrapper builds once, runs with
`--no-build`, parses the fresh TRX and prints the CHECKPOINT line. The shared local Postgres must
be up (`docker compose -f docker-compose.dev.yml up -d`); the DB-backed classes create isolated
schemas. Delete the `bin-c633-*` directories (one per project) before settlement, after checking
each resolved path is inside the worktree.

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c633-a/ -Filter '/*/*/(PhoneHomeRecoveryEligibilityTests*)|(DispatcherSweepLifetimeTests*)|(PhoneHomeEventPumpTests*)|(PhoneHomeConnectionTests*)/*' -MinExecuted 25 -Expect Silent_list_reply_leaves_the_runner_ineligible_until_catch_up_succeeds,Failed_catch_up_waits_the_retry_delay_before_asking_again,One_session_transcript_failure_is_a_warning_not_a_fence,Abandoned_sweep_runs_on_its_own_scope_and_that_scope_is_disposed_after_it_ends,A_sweep_still_running_from_the_previous_tick_is_skipped_and_counted,Without_a_scope_factory_the_budget_cancels_but_never_abandons,Unanswered_request_times_out_instead_of_waiting_forever -ResultsRoot .antiphon/c633-checkpoints
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.Tests -OutputPath bin-c633-b/ -Filter '/*/*/(RemoteWorkspacePreparerTests*)|(RemoteWorktreeMirrorTests*)|(PhoneHomeTaskRoutingTests*)|(DispatchHoldVisibilityTests*)/*' -MinExecuted 31 -Expect Mirror_runs_outside_the_claim_and_the_task_stays_queued_with_an_in_flight_hold,Mirror_success_is_recorded_and_the_next_tick_launches_into_it,Mirror_failure_backs_off_exponentially_and_resets_on_success,Runner_not_eligible_is_one_held_trace_not_a_warning_per_tick,A_process_restart_re_arms_the_mirror_and_the_row_is_written_once,Cancel_of_a_queued_runner_task_returns_while_its_mirror_is_pending_and_a_late_success_is_still_recorded,held_age_escalates_at_warning_then_error_once_each -ResultsRoot .antiphon/c633-checkpoints
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.Tests -OutputPath bin-c633-c/ -Filter '/*/*/(DispatcherRemotePrepStarvationTests*)|(PostLandMutationDeliveryTests*)/(Silent_remote_tasks_ahead_do_not_delay_an_eligible_local_recipient*)|(C478_V09b_AcceptedTaskToWorker*)' -MinExecuted 5 -Expect Silent_remote_tasks_ahead_do_not_delay_an_eligible_local_recipient,C478_V09b_AcceptedTaskToWorker -ResultsRoot .antiphon/c633-checkpoints
```

Report `CHECKPOINT CP-n commit=<sha> build=<ok|reused|failed> filter=<filter> executed=N passed=N
failed=N skipped=N trx=<path>` plus `reruns=k` with the reason for each rerun. A red row is fixed
and rerun as the same row. Any other build or test run is unlisted and needs a stated reason. No
source edits while a row is running. Ordinary green leaves the PCs pending for post-land Mutation.

## Handoff

Next: **Code, Round A only** (S1-S2, CP-1) from `origin/master`. Round B (S3-S5, CP-2) follows
after CARD-0628 lands or rebases onto it; Round C (S6-S7, CP-3) follows Round B. Each round is a
separate bounded Code dispatch with ordinary Review and landing, pointing at this artifact's
`### Checkpoints` with `checkpoints: docs/superpowers/plans/2026-09-23-card-0633-dispatcher-remote-prep-plan.md@<plan commit sha> section "### Checkpoints"`.
