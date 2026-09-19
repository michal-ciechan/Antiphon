# CARD-0535: Name the holder and escalate the age of a dispatcher hold

Status: Plan, written under stated defaults (D-1 to D-9). Verification design is included (§Verification design), so the next stage is Code.

Authoring baseline: `be690264` (branch `feat/card-task-e59e537b`). Plan task: `e59e537b`. Card: `CARD-0535` (Antiphon board). Source investigation: [`docs/investigations/2026-09-19-card-0535-create-time-dispatch-hold-retries-forever.md`](../../investigations/2026-09-19-card-0535-create-time-dispatch-hold-retries-forever.md) (commit `71a4e869`, landed to master).

Owners to read before changing these areas: [orchestration](../../orchestration-loop.md) (holds, `Held` events, land request and caller receipt), [ops HTTP](../../ops-http.md) (`/api/attention`), [testing and build](../../testing-and-build.md) (TUnit filters, alternate output path, Vitest), [project context](../../project-context.md) (layers, interfaces only at I/O seams).

## Outcome and scope

The investigation's verdict stands: the dispatcher re-evaluates every hold on every 5-second tick and dispatched the incident task once its conditions cleared. Nothing in the retry or re-evaluation path changes. What changes is what a human can see while a task waits:

1. **The repository-mutation-lease hold names its holder and writes once per holder**, through `TraceHeldAsync` like every other hold. 95 identical rows in 10 minutes become two rows naming two lands.
2. **The concurrency-cap skip writes a `Held` event**, through the same path. Today it writes nothing.
3. **A dispatcher-held Queued task escalates by age**: a `HeldAged` event with `Warning:` at `Delegation:DispatchHeldWarningSeconds` (300) and `Error:` at `Delegation:DispatchHeldErrorSeconds` (900), once each per queue stint, plus an attention row `DispatchHeld` computed from the same stored events. This mirrors `AgentTaskLandMonitorService` and `AttentionKind.LandHeld` for land requests.

Out of scope, per the card and the brief: any change to hold re-evaluation, ordering, backoff or retry; a tick-duration log for the unexplained 105 s gap (a follow-up card, see §Not done); a caller-facing notification for dispatch holds; recording an owner beside `landing.lock`; `delegate.ps1 -Status` output.

## Ground truth

| Card premise or plausible shortcut | Confirmed behaviour at `be690264` | Design consequence |
|---|---|---|
| "The hold retries forever and never dispatches." | Investigation: every hold is decided from live state each tick; task `9fb36757` dispatched at 22:07:31Z and was canceled 51 s later. | No retry-path change. This plan is visibility only. |
| The lease hold is one more hold like the others. | `AgentTaskDispatcher.DispatchOneAsync` (`server/Application/Services/AgentTaskDispatcher.cs:3085-3100`) adds a `Held` row itself every tick, detail `Held: repository mutation lease is occupied or unavailable.`, no log, no holder. Every other hold calls `TraceHeldAsync` (`:713-726`): scope `:449`, dated routing pin `:480`, model hold `:545`, repair-source landing `:582`, sibling landing `:594`, capacity wait `:619`. | Route the lease hold through `TraceHeldAsync` (D-1). |
| Changing the lease detail text could break a reader. | The exact string appears in code only at `:3097` and as a seeded prior-hold fixture in `tests/Antiphon.Tests/Application/WallRerouteDispatchTests.cs:197`. No client, script or attention code matches it. | Free to rewrite; the fixture keeps seeding the old text as historical data (V-14). |
| `TraceHeldAsync` dedupes per task. | It compares the new detail with the most recent stored `Held` detail (`lastHeld`, `:378-386`) and writes only on a change. Any per-tick-varying text (age, tick count) would defeat it. | Hold details must be stable within a holder episode: no ages, no counters (D-1, D-2). |
| The cap skip is a hold. | `:427-432` increments `skippedConcurrency` and `continue`s before any trace. `lastHeld` is already in scope there. | Trace it (D-2). |
| Land requests escalate; dispatch holds do not. | `AgentTaskLandMonitorService.SweepAsync` writes `LandAged` events with `Warning:`/`Error:` prefixes at `LandWarningSeconds`/`LandErrorSeconds` (300/900), idempotent through `WarningAt`/`ErrorAt` on the request row, and publishes `AgentTaskChanged`. `DelegationSettingsValidator` (`DelegationSettings.cs:1083`) requires Error > Warning > 0. `AttentionService.BuildLandItemsAsync` (`AttentionService.cs:1215-1233`) projects `LandHeld`/`LandNoProgress` at the same thresholds with `ConditionKey` `land:{id}:held`. Nothing equivalent exists for a Queued task; `server/` has no queued-age threshold. | Mirror the pattern: event type, two settings, validator rule, attention kind (D-3 to D-6). |
| Escalation needs new columns for idempotency. | The dispatcher already loads every `Held` event for all queued tasks each tick (`:378-386`). Queued tasks carry few events. | Derive `heldSince`, `warned`, `errored` from stored events in the same query. No migration (D-3). |
| A task id is Queued exactly once. | `AgentTaskService.RequeueAsync` (`:2321`) and the routing-blocked resume (`AgentTaskDispatcher.cs:897`) return a Dispatched/Working/Blocked row to Queued, each after writing a `Retried`/`Rerouted`/`Escalated` event. Every requeue follows an earlier `Dispatched` event (`:3251`, `:3368`, `:4687`, `:4857`). | A queue stint starts after the task's latest `Dispatched` event (D-3). |
| The lease can say who holds it. | `RepositoryMutationLease` (`server/Infrastructure/Git/RepositoryMutationLease.cs`) is an exclusive open of `<common>/antiphon/landing.lock` plus the `RepositoryChildJournal.HasUnfinishedAsync` fence; it returns `null` for both with no reason. Twelve acquirers exist (land run, dispatch worktree creation, gated commit, verification cleanup, progress git, card review, notification probe, worktree manager). The only multi-minute holder class is a running land (`LandRequestState.Running` set at `AgentTaskLandService.cs:257`; `StartedAt` fixed at admission); the only indefinite one is the child-journal fence. | Name the holder from the database (a Running land on the same repo), ask the provider for the fence, else say "another process" (D-1). |
| The dispatcher clock is wall time. | `UtcNow()` reads `_timeProvider` (`:5343`); `CapacityRecoveryTaskTests.CreateDispatcherProvider(timeProvider:)` already injects a `FakeTimeProvider`; `IRepositoryMutationLease` is an optional constructor parameter (`:144`). | Tests simulate hours by advancing the clock between ticks and hold the lease with a fake provider (§Verification design). |
| The client needs a new renderer. | `AgentTaskEventType` and `AttentionKind` serialize as strings (`Program.cs:280`); the drawer timeline renders `event.type` as text (`TaskDetailBody.tsx:455-470`); `attentionVisuals.ts` is a `Record<AttentionKind, …>` whose test enumerates every kind. | Client work is three list entries (S3). |
| Next free enum values. | `AgentTaskEventType` ends at `CommitRecoveryAbandoned = 36`; `AttentionKind` ends at `CommitRecoveryPending = 41`. | `HeldAged = 37`, `DispatchHeld = 42`. Append; never renumber. |
| A dated routing pin is a hold. | `:476-489` traces `routing pin not before {instant}; dispatch paused ({reason}).` for CARD-0305 pins. | A pin is an operator's instant, not a stall: exclude it from escalation and from the attention row (D-3, D-6). |
| The release log names the hold. | `:678-683` logs `released: its scope '…' no longer intersects a running task` for any task in `lastHeld`, whatever the hold was. | Reword to quote the last hold detail (D-7). |
| Enum members need a migration. | `AppDbContext` has no `HasPostgresEnum`/`MapEnum`; enums are stored as integers. | No migration. |

## Decisions

### D-1. The lease hold is traced through `TraceHeldAsync` with a database-named holder

`DispatchOneAsync` stops writing its own event. It returns a three-valued result (`Dispatched`, `HeldOnLease`, `NotClaimed`) instead of `bool`; the tick loop (`:624`) handles `HeldOnLease` exactly as the other holds do: `TraceHeldAsync(task, detail, lastHeld, ct)` and, on the transition, one Information log line `Task {ShortId} held: {Detail}`. `NotClaimed` (lost the row claim, or optional work expired under the claim) stays silent as today.

The detail comes from a new `DescribeLeaseHoldAsync(task, ct)`:

1. A pending land request in `LandRequestState.Running` whose task's repo key (`ScopeResolver.KeyFor(RepoPath, WorkingDirectory)`, the same key the scope lease uses) equals this task's → `Held: repository mutation lease is held by the land of task {holderShort} ({holder title, clipped 60}); land request {requestShort}, admitted {StartedAt:O}.` `StartedAt` is fixed at admission, so the text is stable for the life of that land and changes when the next land is admitted, which is the desired "reason change" trace.
2. Else the provider reports a fence: `IRepositoryMutationLease.DescribeUnavailableAsync(repository, ct)` (new, default interface implementation returns `null`; `RepositoryMutationLease` returns `unfinished repository child journal under {common}\antiphon\children; run scripts/recover-repository-children.ps1` when `RepositoryChildJournal.HasUnfinishedAsync` is true). The probe reads the journal only; it never opens `landing.lock`, so it cannot steal the lock from a racing acquirer. → `Held: repository mutation lease is fenced: {reason}.`
3. Else → `Held: repository mutation lease is occupied by another process (no running land on this repository; a worktree creation, gated commit, verification cleanup or another server instance holds landing.lock).`

The holder lookup runs only on the held path, so the normal tick pays nothing. The land side's `repository_mutation_lease_busy` hold ("owner unknown") is unchanged; it is out of this card's scope and its escalation already exists.

Rejected: an owner label parameter on `TryAcquireAsync` (twelve call sites and five test decorators to change, and the label would still be missing for a foreign process); an owner sidecar file beside `landing.lock` (a crash leaves a stale owner that lies, and file existence is explicitly never ownership in this provider); probing the lock by opening it (steals the lock for a tick from a racing land or dispatch); keeping the per-tick write but adding the holder name (the drawer would still fill with rows, and `TraceHeldAsync` exists for exactly this).

### D-2. The cap skip is traced with a stable detail

At `:427-432`, before `continue`: `TraceHeldAsync(task, "Held: concurrency cap reached ({MaxConcurrentTasks} process-spawning tasks running); waiting for a slot.", lastHeld, ct)` and the same transition log line. Skip counting is unchanged. The detail names the cap, not the occupants, because occupants churn and each change would write a row; the escalation event (D-3) names the occupants at the moment it fires.

Rejected: listing running task ids in the `Held` detail (a busy fleet would write a row every few minutes per waiting task, which is the problem this card removes for the lease).

### D-3. Held-age escalation is computed in the dispatcher tick from stored events

New `AgentTaskEventType.HeldAged = 37`. New settings `DelegationSettings.DispatchHeldWarningSeconds = 300` and `DispatchHeldErrorSeconds = 900`, validated like the land pair (positive, Error > Warning) in `DelegationSettingsValidator`.

The `lastHeld` query (`:378-386`) is widened to load, for the queued ids, events of type `Held`, `HeldAged` and `Dispatched` (`At`, `Type`, `Detail`), and to build one index per task: `lastHeldDetail` (as today), `stintFloor` = latest `Dispatched.At` or `DateTime.MinValue`, `heldSince` = earliest `Held.At` after the floor, `warned`/`errored` = whether a `HeldAged` after the floor starts with `Warning:`/`Error:`.

Every hold branch in the loop records `(taskId, HoldKind)` in a per-tick list when it `continue`s; `HoldKind` is a small private enum (`Scope, RoutingPin, ModelHeld, CapacityWait, RepairSourceLanding, SiblingLanding, Lease, ConcurrencyCap`). After the loop, `EscalateHeldAgeAsync` walks that list:

- skip `RoutingPin` (an operator's instant is not a stall);
- `age = now - heldSince`; a task first held this tick has `age` ≈ 0 and is skipped;
- if `age >= DispatchHeldWarningSeconds` and not `warned`: add `HeldAged` with detail `Warning: dispatch held {seconds}s since {heldSince:O}; created {CreatedAt:O}; reason={lastHeldDetail}; running={active + dispatchedAgainstCap} of {MaxConcurrentTasks}; occupants={short ids of busyScopes, at most 8}.`; log Warning;
- if `age >= DispatchHeldErrorSeconds` and not `errored`: the same with `Error:`; log Error. Both can be written in one tick when a dead dispatcher comes back (the land monitor does the same);
- one `SaveChangesAsync`, then `AgentTaskChanged` per changed task through `_eventBus` (the dispatcher already publishes it at `:1364`, `:2332`, `:3143`).

Idempotency and restart safety come from the stored `HeldAged` rows, not memory. A requeued task starts a fresh stint because the floor is its latest `Dispatched` event.

Rejected: a separate hosted sweep like `AgentTaskLandMonitorHostedService` (the tick already holds every fact and is the only place that knows a task was held this tick; the attention row in D-6 is the independent, tick-health-agnostic reader of the same events); new `HeldSince`/`HeldWarningAt`/`HeldErrorAt` columns (the land request needs them because it has no event stream of its own to derive from; a migration plus clearing them on every requeue path buys nothing here); escalating model holds separately or not at all (the fleet-level `ModelAvailabilityHold` row does not name tasks; the per-task line is what the drawer shows); measuring from `CreatedAt` (a dated pin or a create-time hold would escalate on age alone; the first `Held` is the honest start).

### D-4. Age thresholds mirror land: 300 s and 900 s, separate knobs

The brief asks for the land monitor's pattern. The values are the same by default; the knobs are separate because a board that tolerates long cap queues can raise the dispatch pair without touching land. Not added to `appsettings.json` (the land pair is not there either; defaults rule).

Rejected: reusing `LandWarningSeconds`/`LandErrorSeconds` (the name lies in the dispatch context and couples two unrelated tolerances).

### D-5. Details are prefixed exactly `Warning:` / `Error:`

The severity is read back from the detail prefix, as `AttentionService` and `delegate.ps1` already do for `LandAged`. A shared constant pair lives in a small static `DispatchHoldDetails` class next to the dispatcher (`WarningPrefix`, `ErrorPrefix`, `RoutingPinPrefix = "routing pin not before"`, `ConcurrencyCap(int max)`, `LeaseHeldByLand(...)`, `LeaseFenced(string)`, `LeaseOccupiedUnknown`) so the dispatcher, the attention builder and the tests build and match the same strings.

### D-6. Attention row `DispatchHeld = 42`, derived from events, excluding dated pins

`AttentionService.BuildDispatchHeldItemsAsync(now, ct)`: for each `Queued`, non-specialist task, load its `Held`/`HeldAged`/`Dispatched` events; compute `heldSince` as in D-3; require the latest event to be `Held` or `HeldAged` (a task whose latest event is `Dispatched`, `Retried` or `Rerouted` is not held); skip when the latest `Held` detail starts with `RoutingPinPrefix`; emit when `now - heldSince >= DispatchHeldWarningSeconds`, severity `Error` at `DispatchHeldErrorSeconds`. Title = task title; Headline = `Queued and held for {seconds}s; {latest Held detail}`; Evidence = `task={id:N}; created={CreatedAt:O}; heldSince={heldSince:O}; escalations={count of HeldAged in stint}`; `SinceUtc = heldSince`; `Actions = [OpenDrawer]`; `CardId`; `ConditionKey = dispatch-held:{id:N}`. Registered in `GetAsync` next to `BuildLandItemsAsync`. Severity places it in the client's `suspect` (Warning) or `broken` (Error) group through the existing `groupOf`.

Client: `'DispatchHeld'` in `client/src/api/attention.ts`; `attentionVisuals.ts` entry `{ label: 'Dispatch held', color: 'warning', icon: TbClockPause, hint: 'A queued task has waited on a dispatcher hold past the warning age.' }`; `ALL_KINDS` in `attentionVisuals.test.ts`. `'HeldAged'` in the `AgentTaskEventType` union in `client/src/api/agentTasks.ts`.

Rejected: a caller-session note like `LandNotificationKind.Aged` (AGENTS.md: visibility belongs on the attention feed, never a new sink; the orchestrator that dispatched the task already polls it); a `Cancel` action on the row (the land row offers only `OpenDrawer`; a hold is not a fault to cancel).

### D-7. The release log quotes the hold

`:678-683` becomes `Task {ShortId} released: hold cleared (last reason: {Detail})` with `lastHeld[task.Id]`. Information level, transition only, as today.

### D-8. `WallRerouteDispatchTests` keeps its historical fixture

The prior-lease fixture at `:187-197` seeds three old-style lease rows as prior data and asserts the capacity trace still writes. It must keep passing unchanged: `TraceHeldAsync` compares against the latest stored detail regardless of its text.

### D-9. No `delegate.ps1 -Status` change

The status printer shows land holds (`scripts/delegate.ps1:563`) and nothing for a Queued task's dispatcher hold. Adding a `Held:` line needs the detail DTO's latest `Held` event; it is a one-line follow-up, listed under §Not done rather than widening this card.

## Slices

### S1. Lease hold and cap skip through `TraceHeldAsync`

Files: `server/Application/Services/AgentTaskDispatcher.cs` (`DispatchOneAsync` result type; `:624` handling; `:427-432` trace; `DescribeLeaseHoldAsync`; `:678-683` wording), `server/Application/Services/DispatchHoldDetails.cs` (new, static string builders and prefixes), `server/Application/Interfaces/IRepositoryMutationLease.cs` (`DescribeUnavailableAsync` with default `null`), `server/Infrastructure/Git/RepositoryMutationLease.cs` (journal-only implementation).

Tests: `tests/Antiphon.Tests/Application/DispatchHoldVisibilityTests.cs` (new; V-1 to V-4), `tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseDescribeTests.cs` (new; V-2b), plus the existing `DelegationScopeHoldTests`, `AgentTaskDispatchBaseGuardTests` (real lease through `LeaseHook`) and `WallRerouteDispatchTests` re-run (V-14, V-15).

### S2. Held-age escalation

Files: `server/Domain/Enums/AgentTaskEnums.cs` (`HeldAged = 37` with a doc comment naming CARD-0535), `server/Application/Settings/DelegationSettings.cs` (two settings with doc comments; validator rule), `server/Application/Services/AgentTaskDispatcher.cs` (widened hold index; `HoldKind`; per-tick held list; `EscalateHeldAgeAsync`).

Tests: `DispatchHoldVisibilityTests` V-5 to V-9; `DelegationSettingsValidator` matrix V-11 (in `DispatchHoldVisibilityTests` or beside `AgentTaskLandMonitoringTests.C467_V15_ThresholdConfiguration`).

### S3. Attention row and client lists

Files: `server/Application/Dtos/AttentionDtos.cs` (`DispatchHeld = 42`, doc comment), `server/Application/Services/AttentionService.cs` (`BuildDispatchHeldItemsAsync`, registration in `GetAsync`), `client/src/api/attention.ts`, `client/src/api/agentTasks.ts`, `client/src/features/attention/attentionVisuals.ts`, `client/src/features/attention/attentionVisuals.test.ts`.

Tests: `tests/Antiphon.Tests/Application/DispatchHeldAttentionTests.cs` (new; V-12), Vitest `attentionVisuals.test.ts` (V-13).

### S4. Docs

`docs/orchestration-loop.md`: extend the CARD-0063 `Held` paragraph (Preserved Gotcha #4, `:1006`) with one CARD-0535 sentence block: the lease and cap holds now trace like the others and name their holder; `HeldAged` at `Delegation:DispatchHeldWarningSeconds`/`DispatchHeldErrorSeconds` (300/900); attention `DispatchHeld`; dated pins excluded. `docs/ops-http.md`: one line under the attention endpoint naming `DispatchHeld` and its `ConditionKey`. `DelegationSettings` XML comments carry the rest.

## Verification design

Ordinary verification (Code runs all of it; Review reads it; PCs are for Mutation). Guards are numbered V-n; positive controls PC-n. Every dispatcher test uses `TestDbFixture.CreateIsolatedSchemaAsync()`, a `FakeTimeProvider` injected through `CapacityRecoveryTaskTests.CreateDispatcherProvider(timeProvider:)` (or a local copy of that factory with an `IRepositoryMutationLease` override registered before `AddDelegationWorktreeGraph`), a pinned warm agent as in `DelegationScopeHoldTests.SeedWarmAgentAsync` so a dispatch is a delivery into a warm session and spawns nothing, and `[Category("Integration")]`. No test waits on wall time.

### Simulating the conditions

- **A long-held task**: the dispatcher writes every `At` from `_timeProvider`. Tick at T0 (writes `Held`), `clock.Advance(...)`, tick again. Five ticks cover fifteen simulated minutes in well under a second.
- **A held lease**: a fake `IRepositoryMutationLease` whose `TryAcquireAsync` returns `null` while a flag is set and delegates to nothing otherwise (return a trivial `RepositoryLease` subclass); `DescribeUnavailableAsync` returns a configurable string. No file lock, no git, no interaction with any other test's repository. The real provider is exercised only in V-2b on a scratch repository.
- **A running land holder**: seed an `AgentTask` (Worktree, Succeeded) with the same `RepoPath` and an `AgentTaskLandRequest { IsPending = true, State = Running, StartedAt = T0 - 60 s }` bound through `CurrentLandRequestId`/`LandRequestedAt`. No land service runs.
- **A cap skip**: `MaxConcurrentTasks = 1` and one seeded non-specialist task in `Dispatched` with `CapacityWaitRetained = false`; the queued task is skipped at `:427` every tick.
- **A requeue**: seed the prior stint as rows (`Held` at T0 - 3000 s, `HeldAged Warning:` and `Error:` at T0 - 2000 s, `Dispatched` at T0 - 1000 s) on a task that is now Queued again.
- **A dated pin**: seed a `RoutingPin` for the task's role with `NotBefore = T0 + 1 day` (the provider already registers `RoutingPinService`).

### Guards

| V | Slice | Test | Assertion |
|---|---|---|---|
| V-1 | S1 | `DispatchHoldVisibilityTests.lease_hold_traces_once_per_holder_and_names_the_running_land` | Fake lease held; Running land for task H on the same repo. Three ticks → exactly one `Held` row; detail contains `Short(H)`, H's title and the request short id; `TickResult.Dispatched == 0`. Replace H's request with a Completed one and seed a Running request for task K → one tick → second `Held` row naming K. Release the fake → tick → task `Dispatched`; `Held` count stays 2; one `Dispatched` event. |
| V-2 | S1 | `…lease_fence_is_named_from_the_provider` | Fake `DescribeUnavailableAsync` returns the journal reason, no Running land → single `Held` row contains `recover-repository-children.ps1`; stable across three ticks. |
| V-2b | S1 | `RepositoryMutationLeaseDescribeTests` | Real provider on a `git init` scratch repo (`LandingGitFixture` or a temp dir with `LandingGit`): empty `antiphon/children` → `null`; a non-`.json` file there → string containing `children` and the script name; `TryAcquireAsync` after the probe still succeeds when the directory is empty (the probe left no lock). |
| V-3 | S1 | `…unknown_lease_holder_is_stable_text` | Fake held, provider returns `null`, no Running land → one `Held` row equal to `DispatchHoldDetails.LeaseOccupiedUnknown`; three ticks, still one row. |
| V-4 | S1 | `…cap_skip_writes_one_held_event_and_still_counts` | Cap 1, one active, one queued. Three ticks → `SkippedConcurrency == 1` each; exactly one `Held` row equal to `DispatchHoldDetails.ConcurrencyCap(1)`. Settle the active task → tick → queued task `Dispatched`; `Held` count 1. |
| V-5 | S2 | `…held_age_escalates_at_warning_then_error_once_each` | Cap hold at T0. Ticks at T0, +299 s → 0 `HeldAged`. +300 s → one row, detail starts `Warning:`, contains `reason=` and `ConcurrencyCap(1)` text and `running=1 of 1`. +400 s → still one. +900 s → second row starts `Error:`. +1000 s → still two. |
| V-6 | S2 | `…escalation_is_idempotent_across_a_restart` | After V-5's Warning, build a fresh provider/dispatcher (same schema, clock at +950 s) → tick → total `HeldAged` rows is two (Error added once), then another tick → still two. |
| V-7 | S2 | `…both_thresholds_crossed_in_one_tick_write_both_rows` | Tick at T0 (Held), next tick at +1000 s → two `HeldAged` rows in that tick, one `Warning:` one `Error:`, same `At`. |
| V-8 | S2 | `…a_requeue_starts_a_fresh_escalation_stint` | Prior-stint rows seeded (see above); task Queued and cap-held at T0. Tick T0 → one new `Held`; tick +300 s → one new `Warning:` row (total `HeldAged` = 3); its detail's `since` is T0's `Held.At`, not the old one. |
| V-9 | S2 | `…a_dated_routing_pin_never_escalates` | Pin `NotBefore = T0 + 1 day`. Ticks at T0, +301 s, +901 s → one `Held` row starting with `RoutingPinPrefix`; zero `HeldAged`. |
| V-10 | S1 | review-only | `:678-683` log text quotes `lastHeld[task.Id]`; no test (log text). |
| V-11 | S2 | `…threshold_configuration` `[Arguments]` | `(0,900,false)`, `(300,300,false)`, `(900,300,false)`, `(300,900,true)` through `DelegationSettingsValidator`; defaults are 300/900. |
| V-12 | S3 | `DispatchHeldAttentionTests` | (a) Queued task, `Held` at now-301 s → one item `DispatchHeld`, `Warning`, `ConditionKey == dispatch-held:{id:N}`, `SinceUtc == Held.At`, `Actions == [OpenDrawer]`, `TaskId`, `CardId`. (b) `Held` at now-901 s → `Error`. (c) `Held` at now-100 s → no item. (d) routing-pin `Held` at now-2000 s → no item. (e) `Held` at now-2000 s then `Dispatched` at now-1000 s, task `Dispatched` → no item. (f) same as (e) but task Queued again with a new `Held` at now-100 s → no item; at now-400 s → `Warning`, `SinceUtc` is the new `Held.At`. (g) two reads return the same `ConditionKey` (dedupe). |
| V-13 | S3 | Vitest `attentionVisuals.test.ts` | `ALL_KINDS` includes `'DispatchHeld'`; the `Record` type fails compilation if the visuals entry is missing. `pwsh -File scripts/test-client.ps1`. |
| V-14 | S1 | existing `WallRerouteDispatchTests.Prior_lease_hold_does_not_mute_the_capacity_trace` | Unchanged and green. |
| V-15 | S1 | existing `DelegationScopeHoldTests`, `AgentTaskDispatchBaseGuardTests` | Unchanged and green: the base-guard suite drives the real lease through `DispatchOneAsync`'s refactored path. |

Run commands (from the worktree, daemons hold `bin/`):

```
dotnet build --property:OutputPath=bin-c535/
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c535/ -- --treenode-filter "/*/*/DispatchHoldVisibilityTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c535/ -- --treenode-filter "/*/*/DispatchHeldAttentionTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c535/ -- --treenode-filter "/*/*/RepositoryMutationLeaseDescribeTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c535/ -- --treenode-filter "/*/*/WallRerouteDispatchTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c535/ -- --treenode-filter "/*/*/DelegationScopeHoldTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c535/ -- --treenode-filter "/*/*/AgentTaskDispatchBaseGuardTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c535/ -- --treenode-filter "/*/*/AgentTaskLandMonitoringTests/*"
pwsh -File scripts/test-client.ps1
```

Delete every `bin-c535/` directory afterwards. Do not run the full `Antiphon.Tests` assembly for this card (testing guide: class filters for a one-service change).

### Positive controls (for the Mutation stage; method-scoped, red then green)

| PC | Mutation | Expected red |
|---|---|---|
| PC-1 | Restore the direct `Held` write in `DispatchOneAsync` (bypass `TraceHeldAsync`) | V-1: three rows after three ticks |
| PC-2 | `DescribeLeaseHoldAsync` skips the Running-land lookup | V-1: holder short id missing |
| PC-3 | Remove the cap trace at `:427` | V-4: zero `Held` rows |
| PC-4 | `>=` → `>` on the warning comparison | V-5: no row at exactly +300 s |
| PC-5 | Ignore stored `HeldAged` when deciding `warned` | V-5 (+400 s second row), V-6 |
| PC-6 | Ignore the `Dispatched` stint floor | V-8: no new `Warning:` row (old one counts) |
| PC-7 | Drop the `RoutingPin` exclusion | V-9: a `HeldAged` row appears |
| PC-8 | Attention builder measures from `CreatedAt` | V-12 (f): item appears at now-100 s; (c) also flips if `CreatedAt` is old |
| PC-9 | Remove the validator rule | V-11 `(300,300,false)` passes validation |
| PC-10 | Real `DescribeUnavailableAsync` returns `null` unconditionally | V-2b: non-json journal file case |
| PC-11 | Attention builder ignores the "latest event is Held/HeldAged" rule | V-12 (e): item appears for a Dispatched task |

## Risks

- **A busy fleet writes more `Held` rows than before for cap waits.** One row per waiting task per stint, not per tick; bounded by queue length. Acceptable and intended.
- **A hold detail that varies per tick would silently regress to per-tick rows.** V-1/V-3/V-4 assert row counts across three ticks; `DispatchHoldDetails` centralises the strings so a future edit sees the stability rule in one place.
- **`Error` rows on every queued task during a provider outage.** Model holds escalate like any other hold (D-3). The fleet-level `ModelAvailabilityHold` row continues to name the cause; the per-task rows say how long each task has waited. If this proves noisy in practice, excluding `HoldKind.ModelHeld` from escalation is one line and one new guard.
- **Occupant list in the escalation detail.** Clipped to eight short ids; the detail column has no length limit but the drawer clamps to three lines with "show all".
- **Refactoring `DispatchOneAsync`'s return type.** Single call site (`:624`); the base-guard tests drive the real path.

## Not done, noted

- Tick duration at Information above a threshold (the 105 s gap in the investigation). A separate card; it is observability of the tick loop, not of a hold.
- `delegate.ps1 -Status` printing a Queued task's latest `Held` detail (D-9).
- The land side's `repository_mutation_lease_busy` hold still says "owner unknown"; `DescribeLeaseHoldAsync`'s Running-land lookup could be reused there, but the land side already escalates and is outside this card.
