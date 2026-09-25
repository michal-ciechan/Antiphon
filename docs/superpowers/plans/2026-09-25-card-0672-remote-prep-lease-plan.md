# CARD-0672: remote dispatch off the land lease — lease-free launch of a prepared runner task, a dispatch-first turnstile at land admission, purpose-aware settlement waiting, and a hold ledger

Plan date: 2026-09-25. Plan task: `95d13463` (Frontier, server2 Linux runner, worktree).
Code inspected at `c663126df74c289fa64ddab528fa132d786cb9a8` (`origin/master` on 2026-09-25, which carries
CARD-0642 R1, CARD-0657 R1–R4, CARD-0664, CARD-0666 and the CARD-0688 plan; the task branch was rebased onto it).
Card `a053f9e4-d478-4f36-ada1-d13caa6d67dc` (CARD-0672), board `8988ca03-7414-47ad-b0b6-51556c701703`.
Evidence: the source files named below, the desktop API (`GET /api/agent-tasks/{id}` events for the card's
three tasks `1753ea5c`, `d81207e7`, `169fe4dd`, read 2026-09-25), the CARD-0657 plan
([runner settlement sync](2026-09-24-card-0657-runner-settlement-sync-plan.md)) and the CARD-0688 plan
([dedicated land worktree](2026-09-25-card-0688-dedicated-land-worktree-plan.md)). No code was changed, no build
or test was run, no database was written.

The deliverable is two bounded, sequential Code rounds. The verification design is folded into this plan
(the dispatch says `Next stage: Code`).

## Summary

The card's symptom is real and its remedy list is right, but its mechanism is off by one layer. The branch
push and the runner mirror (`RemoteWorkspacePreparer` → `RemoteWorkspaceService.PushBranchAsync` /
`MirrorAsync`) already run **without** the repository mutation lease, on a background task, off the tick
(CARD-0633). What queues behind lands is the dispatcher's claim, `AgentTaskDispatcher.DispatchOneAsync`, which
takes the lease with purpose `dispatch` around the whole claim transaction — and a runner-bound task passes
through that claim **twice**: once to cut its desktop worktree and start the preparer, and once more, after
the mirror is recorded, to launch. The second crossing runs no git at all. In the card's own example
(`1753ea5c`) the mirror took under 16 seconds and the launch then waited **20 min 46 s** through three
back-to-back lands, because the land queue never gaps: `AgentTaskLandQueue` hands the next land to
`RunRequestAsync` the moment the previous one returns, and the dispatcher's one non-blocking
`TryAcquireAsync` per 5-second tick only wins when a tick lands inside a gap of a few seconds.

The design here: (**D-1**) a prepared runner task launches without the lease, since nothing in that crossing
mutates the repository and a runner session never writes the desktop checkout; (**D-2**) a dispatch-first
turnstile — a queued task refused the lease registers as a *waiter*, and a land **yields at admission**
(before it acquires) while waiters exist, for a bounded time, so the dispatcher's next tick takes the lease
in the gap the queue would otherwise close; (**D-3**) the `HeldAged` escalation and the `DispatchHeld`
attention item carry a per-class wait ledger (`leaseWait=`, `prepWait=`, `runnerWait=`, `capWait=`,
`class=`) derived from the task's `Held` rows, so lease starvation and runner capacity are told apart without
a new column; (**D-4**) the CARD-0657 settlement sync registers as a waiter too and stops charging its
120-second budget while the holder is a *land* (a bounded, cooperating holder), blocking only past an
absolute ceiling. The lease primitive, the child journal, the land queue and the sweep are untouched, which
is also what CARD-0688 D-14 keeps; the two plans share one file (`AgentTaskLandService.cs`) at one
insertion point and land in either order.

Expected effect on the card's timeline: crossing 2 goes from 20 min 46 s to one tick (≤ 5 s); crossing 1
goes from "wait for a gap that never comes" to "the remainder of the land in flight, then ≤ 5 s"; three
queued runner tasks cost the land queue about 15 s of yield in total; a finished runner task's settlement
sync no longer blocks `runner_sync_lease_busy` behind consecutive lands.

## Ground truth

| Card / brief assumption | What the code and the evidence show | Consequence |
|---|---|---|
| "Remote dispatch preparation (the branch push and mirror) takes the canonical repository's mutation lease, so it queues behind every land." | It does not. `RemoteWorkspacePreparer.RunAsync` runs on `Task.Run`, on its own scope, and calls `RemoteWorkspaceService.PushBranchAsync` (`git rev-parse HEAD` + `git push -u origin <branch>` in the desktop worktree; no `IRepositoryMutationLease` call) then `MirrorAsync` (a phone-home RPC). The lease is taken by `AgentTaskDispatcher.DispatchOneAsync` (`needsLease`, purpose `dispatch`, around line 3946) for the whole claim transaction; a runner-bound task returns `HeldForRemotePrep` from inside that leased claim, which is why the "remote workspace preparation is in flight" hold *follows* the lease hold in the events. | The push needs no new lock: it has none. The two leased crossings are the target (D-1, D-2). |
| "Pushing a task branch from a commit that already exists does not mutate the canonical checkout, so it could run under a shared or read lease." | True, and already the case. What the first crossing mutates is `git worktree add -b feat/card-task-<id>` (`WorktreeManager.CreateAsync`, reached through `DelegationWorktreeService.CreateForTaskAsync(task, lease, …)`, which requires `Owns(lease)`) plus the progress baseline pin (`TaskProgressGit.PinBaselineAsync`, which tolerates a null nested acquire and pins anyway — an existing lease-free write of a task-owned ref). The second crossing (`WorktreePath` set, `RemoteWorktreePath` set) reaches `PrepareRemoteWorkspaceAsync`, which returns the recorded mirror path and runs no git; the rest is DB writes, the credential probes and the runner launch RPC. | Crossing 1 stays leased (it registers a worktree; see the exclusivity table). Crossing 2 needs no lease (D-1). |
| "it also causes `runner_sync_lease_busy` blocks on finished runner tasks." | `RemoteWorkspaceService.SyncAsync` acquires the lease twice per attempt: untagged inside `TaskProgressGit.ObserveExactRefAsync` for the fetch into `refs/antiphon/progress/<task>/observe-*`, then tagged `worktree-settlement` for the `merge --ff-only` in the task's own worktree. `WhileLeaseBusyAsync` waits at most `LeaseWaitSlice` (10 s) per sweep and returns `runner_sync_lease_waiting`; `RunnerSyncLeaseWaits` keeps the first-busy instant in memory; once `now − firstBusy ≥ RunnerSyncBudgetSeconds` (120) the reason is `runner_sync_lease_busy` and the reply service blocks the task (CARD-0657 D-5 R4 row; `Sustained_lease_contention_waits_one_slice_per_sweep_and_blocks_after_the_budget` pins it). The clock is purpose-blind and wall-clock, so 120 s of wall time against a 9-minute land is a block. The card's tasks (`1753ea5c` 16:40:30, `169fe4dd` 16:44:53) show the pre-0657 form of the same collision, `progress=unavailable; reason=repository_lease_busy`, from the observation's untagged acquire. | Register the sync as a waiter (D-2) and stop charging the budget while a land holds the lease (D-4). |
| "server2 sat idle with free seats" / "a HeldAged metric that separates land-lease starvation from runner capacity." | `RemoteHoldForAsync` (backoff, in-flight prep, runner eligibility, `RunnerAtCapacity`) runs before the claim; the lease hold is traced after `DispatchOneAsync` returns `HeldOnLease`. `TraceHeldAsync` writes one `Held` row per distinct text; `EscalateHeldAgeAsync` writes `HeldAged` `Warning:`/`Error:` rows whose `reason=` is only the *last* `Held` text; `AttentionService.BuildDispatchHeldItems` says "Queued and held for Ns; <last detail>". `DispatchHoldDetails.Classify` maps a lease-by-land hold and a runner-capacity hold to the same `OrdinaryWait`. Nothing sums time per hold kind. | A ledger over the existing `Held` rows since the last `Dispatched` floor, restart-safe with no new column (D-3). |
| "A land takes about 9 minutes (CARD-0642)." | Median 521 s admission→confirmation at `ada146ea` (CARD-0688 plan measurements). The land holds the lease from `RunRequestAsync`'s `TryAcquireAsync` (purpose `land`, line 273) through `RunLeasedAsync`, i.e. admission, resolution, rebase, verify, push and cleanup, in one `await using`. `AgentTaskLandHostedService` "never sleeps": it drains `AgentTaskLandQueue` and runs the next request as soon as the previous returns; `SweepAsync` re-enqueues every pending request every `LandSweepSeconds` (5). | The inter-land gap is a few seconds of DB work, so a non-blocking probe once per 5 s tick loses the race almost always (measured: three of the six crossings tonight waited through at least one whole land, two more through the remainder of one). Priority must be granted by the land side at admission (D-2). |
| "give dispatch priority over queued lands, or let preparation run between land phases." | No priority exists: `RunRequestAsync` acquires unconditionally. Land phases are boundaries of one continuously held lease: CARD-0642 D-4's read cache is lease-scoped (`BeginOperationScope` "disposed before the lease it relies on"), the child journal fences acquisition on any unfinished mutating child, and CARD-0688 D-14 keeps "the lease, the approval/DB rechecks" exactly as they are. | Yield at admission only (D-2). Releasing mid-land is rejected. |
| CARD-0664 (consumer slots) and CARD-0666 (start-ref fetch) "are on master". | CARD-0664: `SyncAdmittedAsync` admits and releases a `Launch` consumer around the whole attempt; the dispatcher's `RequireConsumerAsync` at claim is DB-only. CARD-0666: `StartRefAvailability.AcquireLeaseAsync` polls the lease for up to a 30 s network deadline at create (`start-ref-fetch`), the one existing bounded-wait pattern; dispatch never fetches. | Neither changes here. The start-ref poll is the model for "bounded wait" but runs at create, not in the tick; it is not reused. |
| A runner task might share its worktree with a landing task. | `AgentTaskService` line 1184: "A runner-bound task cannot be a follow-up on an existing process"; runner tasks are Worktree-only (line 1172) and never pinned (line 1182). The dispatcher's `EvaluateCardSiblingBaseAsync` and `CreateForTaskAsync` run only when `WorktreePath` is null. CARD-0604 D-15: the desktop worktree is canonical and moves only through the settlement sync; the session writes the runner mirror. | A prepared runner task's launch cannot become a writer of any checkout a land admits against (invariant I-A below), so D-1 is safe with respect to `FindWriterAsync`. |

### The card's three tasks, from their events (2026-09-24, UTC)

| Task | Crossing 1: created → worktree cut (lease waits) | Prep (push + mirror) | Crossing 2: mirror recorded → launched (lease waits) | Total created → launched |
|---|---|---|---|---|
| `1753ea5c` | 15:59:56 → 16:06:25 (held behind land `a57a83df`, acquired 15:53:02) — 6 min 29 s | 16:06:44 → < 16:07:00 (≤ 16 s) | 16:07:00 → 16:27:46 (held behind `a8685a72` 16:06:52, `e070938a` 16:14:21, `fb612012` 16:20:13) — **20 min 46 s** | 27 min 50 s |
| `d81207e7` | 16:03:22 → 16:27:48 (`a57a83df`, a child-journal fence at 16:06:46, `a8685a72`, `e070938a`, `fb612012`) — **24 min 26 s** | 16:28:12 → < 16:29:06 (< 54 s) | 16:29:06 (queue empty by then) — < 1 min | 25 min 44 s |
| `169fe4dd` | 16:07:58 → 16:25:40 (`a8685a72`, `e070938a`, `fb612012`) — **17 min 42 s** | 16:26:04 → < 16:28:48 | 16:28:48 (remainder of `fb612012`) — < 2 min 44 s | 20 min 50 s |

The prep itself never exceeded a minute. Of the 74 minutes the three tasks spent Queued, roughly 68 were
spent refused at the lease by a land, and the one `HeldAged Error` row on each names only the last land.

## What each leased operation actually needs to be exclusive against

| Operation | Repository mutation | Must be exclusive against | Under this plan |
|---|---|---|---|
| Crossing 1 (claim + `worktree add -b` + baseline pin + preparer start) | Registers a worktree and a branch; pins a task-owned ref | The single-mutator invariant the child journal and CARD-0642's lease-scoped listings assume; a land's cleanup listings (3 per land after CARD-0688) and, before CARD-0688, its 7 listings and 19 inspections; the land's `FindWriterAsync` view of claims for tasks that *do* write a checkout a land admits against | Leased, as today, with priority at the gap (D-2). |
| Crossing 2 (claim + launch into the recorded mirror) | None | Nothing in git. The claim is a DB row lock; the launch is a runner RPC. The task's worktree is its own (not a follow-up), and the session writes the mirror, never the desktop checkout | Lease-free (D-1). Invariant I-A: **a runner session never writes the desktop checkout; only the leased settlement sync moves it.** |
| Local Worktree dispatch (one crossing: claim + `worktree add` + launch) | Registers a worktree and a branch | As crossing 1 | Leased, with priority (D-2 applies to every `HeldOnLease` task, not only runner ones). |
| Settlement sync (`ObserveExactRefAsync` fetch, settlement pin, `merge --ff-only` in the task's worktree) | Task-owned observation refs; the task branch ref and its worktree | The single-mutator invariant; `GuardedWorktreeRemoval`'s listings and `Full` inspections of that same worktree when a land of *that task* cleans up (only after Succeeded, i.e. after this sync) | Leased, as today; registers as a waiter; land occupancy is not charged to its budget (D-4). |
| Land (admission through cleanup) | Everything CARD-0688 lists | Every other mutator | Unchanged, except that admission yields to registered waiters for a bounded time (D-2). |

## Decisions

- **D-1 — A prepared runner task launches without the repository mutation lease.** In `DispatchOneAsync`,
  `needsLease` becomes false when the task is runner-bound (`RunnerId` set), already has its desktop worktree
  (`WorktreePath` set), already has its mirror (`RemoteWorktreePath` set), and is not a repair
  (`RepairSourceTaskId` null), not a SourceLanding snapshot (`SourceLandingOperationId` null) and not an
  Interim verification (`VerificationRound != Interim`) — the predicate is a static
  `AgentTaskDispatcher.LaunchesPreparedMirror(AgentTask)` so it is unit-testable and the code reads it in one
  place. Every other path keeps the lease: the worktree cut (`CreateForTaskAsync(claimed, repositoryLease!, …)`
  is reached only when `WorktreePath` is null), `ValidateVerificationAsync` (SourceLanding only),
  `PrepareRepairSourceAsync` (repair only), and local Worktree/Shared dispatch. The claim transaction, the
  concurrency-token re-read, the workspace-use consumer, the credential probes and the launch are unchanged.
  Safety argument: the lease's stated purpose at that site is "admission and landing read running claims
  under the same common-directory lease"; a land's `FindWriterAsync` looks for Dispatched/Working/Blocked
  tasks that share the source worktree path or (Shared only) the common directory. A prepared runner task
  shares neither (it cannot be a follow-up, is Worktree-only, and its session writes the mirror), so a claim
  that flips it to Dispatched between a land's read and its mutation changes nothing the land relies on
  (I-A). Rejected: dropping the lease for every Worktree task whose `WorktreePath` is set (a local follow-up
  into an existing worktree *is* a desktop writer the land must see), and moving crossing 1 off the lease by
  pushing the base commit to the branch without cutting a desktop worktree (a redesign of CARD-0604 D-15
  and of the CARD-0657 sync that validates a registered desktop checkout; recorded as a follow-up).

- **D-2 — A dispatch-first turnstile at land admission, outside the lease primitive.** New singleton
  `RepositoryLeaseWaiters` (`server/Application/Services/RepositoryLeaseWaiters.cs`): an in-process registry
  keyed by the repository's canonical common directory (same `PathKey` rule as `RepositoryMutationLease`:
  full path, trailing separator trimmed, case-insensitive on Windows only), holding
  `RepositoryLeaseWaiter(Guid TaskId, string Purpose, DateTimeOffset Since)` entries, with `Register`,
  `Clear(taskId)`, `ReconcileDispatch(ISet<Guid> stillHeld)` (drops every `dispatch` entry not in the set),
  `Snapshot(common)`, `Any(common)` and `FirstYield(requestId, now)` / `EndYield(requestId)` for the land
  side's budget. Registered in `Program.cs` and `DelegationTestServices.AddDelegationWorktreeGraph`
  (`TryAddSingleton`); the dispatcher and the land service take it as an optional constructor parameter
  (null keeps today's behaviour, so existing harnesses do not change). *Dispatcher side:* when
  `DispatchOneAsync` returns `HeldOnLease`, the tick registers the task with purpose `dispatch` and the
  common directory (`ILandingGit.CommonDirectoryAsync(task.RepoPath)`, a new optional constructor parameter
  with a `Path.GetFullPath(RepoPath)` fallback); at the end of the tick `ReconcileDispatch` clears every task
  that was not refused the lease on this tick (dispatched, failed, cancelled, held for another reason, or no
  longer queued). *Land side:* in `RunRequestAsync`, immediately before `_leases.TryAcquireAsync(… Land …)`,
  when `Delegation:LandYieldToDispatchMaxSeconds` (new, default 90, 0 disables, validated 0..600) is
  positive and `Any(common)` is true, the land calls `YieldToDispatchAsync` and returns `LandRunResult.Held`
  unless this request's first yield is older than the setting, in which case it writes one `Warning` event
  ("yield budget exhausted after Ns; proceeding") and acquires. `YieldToDispatchAsync` sets the request to
  `Held` with the new hold code `repository_lease_yielded_to_dispatch`, a detail naming the waiters' short
  ids and purposes, `HeldSince` at the first yield, no holding task, one `Held` event per episode (deduped on
  the same reason like `HoldAsync`) and **no** land notification (a yield resolves itself within a sweep;
  the CARD-0641 note machinery is for holds that need a caller). Admission's existing `HeldReleased` event
  fires when the land is admitted. The sweep re-picks a yielded request every `LandSweepSeconds` (5); the
  dispatcher's tick (`PollIntervalSeconds`, 5) takes the lease in between; a crossing holds it for seconds,
  so N waiters cost the queue about N × 5 s. `AgentTaskLandMonitorService`/`AttentionService` report a
  `repository_lease_yielded_to_dispatch` hold as `LandHeld` only once it is older than `LandWarningSeconds`
  (a ten-second yield is not an attention item). Waiters are memory-only (a restart forgets them; the next
  tick re-registers), which matches `RunnerSyncLeaseWaits` and the lease owner registry; a second server
  process is already the "unknown owner" case and is unaffected.
  Rejected: (a) a reader/writer lease — `landing.lock` is one `FileStream` with `FileShare.None`; a shared mode
  needs a second lock file, reader accounting and a journal that understands concurrent mutators, and the
  only step it would free (the push) is already free; (b) a blocking wait for the lease inside the tick —
  CARD-0633 moved network work off the serial tick precisely so a stuck runner cannot hold every other
  dispatch, and a lease wait would put a 9-minute land in its place; (c) releasing the lease between land
  phases — see ground truth: the read cache, the journal and CARD-0688 D-14 all assume one continuous lease;
  (d) priority inside `RepositoryMutationLease.TryAcquireAsync` — it would turn the advisory owner tag into
  policy inside the OS-lock primitive, which three test fakes implement and CARD-0688 D-14 freezes;
  (e) waking the dispatcher from the lease's `DisposeAsync` — cuts the residual ≤ 5 s tick latency but adds
  a cross-service signal for a saving below the sweep cadence; recorded as a follow-up.

- **D-3 — A per-class hold ledger on the escalation and the attention item, derived from `Held` rows.** New
  public enum `DispatchHoldClass { Lease, RemotePrep, Runner, Cap, Scope, Agent, Landing, Routing, Other }`
  and `DispatchHoldDetails.ClassOf(string detail)` mapping every hold sentence the dispatcher writes
  (lease texts → `Lease`; "remote workspace preparation" in flight or backoff → `RemotePrep`; "at capacity"
  and `RunnerUnavailable` → `Runner`; "concurrency cap reached" and "waiting for … capacity" → `Cap`;
  "intersects … running task" and "already writing in this shared checkout" → `Scope`; "pinned agent '"
  and "standing agent '" → `Agent`; "is landing" → `Landing`; routing-pin and model-held texts →
  `Routing`; else `Other`). New static `DispatchHoldLedger.FromRows(rows since the Dispatched floor, now)`
  sums stints per class (a stint runs from a `Held` row's `At` to the next `Held` row's `At`, or `now`) and
  names the dominant class. `LoadQueuedHoldIndexAsync` already loads exactly these rows, so
  `EscalateHeldAgeAsync` appends `; leaseWait=Ns; prepWait=Ns; runnerWait=Ns; capWait=Ns; otherWait=Ns;
  class=<dominant>` to the `Escalation` text **after** `occupants=` (`ExtractEscalationReason` cuts at
  `; running=`, so `Reason`/`Classify`/`LandHolder` keep working on the new text) and logs the same values
  as structured properties. `AttentionService.BuildDispatchHeldItems` computes the ledger from the same
  rows, appends the fields to `Evidence`, and `AttentionItemDto` gains a trailing optional
  `string? HoldClass = null` (the dominant class, lower-case). `TickResult` gains `HeldOnLease` and
  `HeldOnRunner` counts (trailing defaulted positional parameters) and the hosted service's debug line prints
  them. Rejected: a new `AgentTaskEvent` column or a counters table (CARD-0535 made escalation restart-safe
  from rows on purpose); Prometheus-style meters (the server has no metrics pipeline; `Held`/`HeldAged`
  rows and the attention feed are the operator surface, per `docs/ops-http.md`).

- **D-4 — The settlement sync waits as a waiter and charges its budget only for non-land holders.**
  `RemoteWorkspaceService.WhileLeaseBusyAsync`, on each busy observation, reads
  `_leases.FindOwnerAsync(repo)` once per slice: when the owner is `Known` with purpose `land`, it registers
  the task in `RepositoryLeaseWaiters` (purpose `worktree-settlement`, cleared with `LeaseWaits.End`) and
  does **not** charge the slice; any other owner (`Untagged`, `Unknown`, a fake returning null, another
  purpose) charges the elapsed slice as today. `RunnerSyncLeaseWaits` gains a charged-time map next to the
  first-busy instant: `Spent` is `charged ≥ RunnerSyncBudgetSeconds` **or** `now − firstBusy ≥
  Delegation:RunnerSyncLandWaitCeilingSeconds` (new, default 1800, validated ≥ `RunnerSyncBudgetSeconds`), so
  a task cannot wait forever behind an endless land queue either. Reason codes are unchanged
  (`runner_sync_lease_waiting` while waiting, `runner_sync_lease_busy` when spent); the reply service's
  `StillWaitingForLease` and the CARD-0657 D-5 table keep their meaning. Rejected: raising
  `RunnerSyncBudgetSeconds` (a 120 s budget is right for an unknown holder and would hide the class of
  wait); making the sync lease-free (it moves a branch ref and a worktree the guarded cleanup inspects, and
  it would be the first mutator outside the single-mutator invariant); pausing on *any* known owner (a
  `gated-commit` or `worktree-provision` holder is seconds long and the budget already absorbs it).

- **D-5 — Composition with CARD-0688.** The plans touch disjoint code except `AgentTaskLandService.cs`:
  CARD-0688 S5 edits admission probes (D-10), the outcome line and the profile line; this plan inserts the
  yield immediately before the `Land`-purpose `TryAcquireAsync` in `RunRequestAsync` and adds
  `YieldToDispatchAsync`. Whichever lands second rebases a few lines. Both keep `RepositoryMutationLease`,
  the child journal, `AgentTaskLandQueue` and the sweep untouched. After CARD-0688 R1 a land holds the lease
  for ~3–4 minutes instead of ~9, which shortens the worst case for crossing 1 (the remainder of the land in
  flight) but does not remove the starvation, which is a property of the never-gapping queue, not of land
  length; and CARD-0688's land-worktree reset is a new ~2 s mutation *under* the lease, after admission, so
  the yield point stays ahead of it. CARD-0688's follow-up note ("the lease scope itself is that card's
  question") is answered here: the scope stays; the order of admission changes.

- **D-6 — Two sequential Code rounds.** R1 = D-1, D-2, D-3 and their docs (S1–S4): the card's headline
  (dispatch starvation) and the metric. R2 = D-4 and its docs (S5–S6), after R1 has landed and been
  activated (`GET /api/version`), because it builds on `RepositoryLeaseWaiters` and changes tests CARD-0657
  R4 just landed. Both rounds are additive and toggleable (D-8).

- **D-7 — Stable texts.** The dispatcher's `Held` details are unchanged (they are `TraceHeldAsync`'s dedupe
  keys and CARD-0535/0537 tests pin them). New texts: the land hold code
  `repository_lease_yielded_to_dispatch` with detail
  `Land yields the repository mutation lease to N queued dispatch(es): <short ids> (<purposes>); resumes within Delegation:LandSweepSeconds.`;
  the yield-budget `Warning`; the escalation and attention suffixes of D-3. `DispatchHoldDetails.Classify`
  keeps its existing classes (`ExpectationHoldClass`) untouched; `DispatchHoldClass` is a separate,
  finer-grained enum for the ledger.

- **D-8 — Settings and rollback.** `Delegation:LandYieldToDispatchMaxSeconds` (90; 0 disables D-2's yield;
  waiters are still registered so D-3's ledger and D-4's owner check keep working) and
  `Delegation:RunnerSyncLandWaitCeilingSeconds` (1800), both validated in the existing `Delegation` options
  validation block next to `RunnerSyncBudgetSeconds`. D-1 has no switch: a prepared runner launch never
  mutates git, so there is nothing to serialise. No migration.

- **D-9 — Left exactly as it is.** `RepositoryMutationLease` and `IRepositoryMutationLease` (no new member; the
  three test fakes compile unchanged); the child journal; `AgentTaskLandQueue` and both land hosted
  services; the preparer (`RemoteWorkspacePreparer`), the mirror RPC and the push; `RemoteHoldForAsync`'s
  gate order (CARD-0633 D-5, CARD-0653); the CARD-0657 sync algorithm (only its waiting accounting changes);
  `TaskCompletionProgressService`'s single bounded observation for local Code tasks
  (`progress=unavailable; reason=repository_lease_busy` is Indeterminate fail-open and outside this card).

## Design

### The dispatcher tick after R1

1. `RemoteHoldForAsync` gates (unchanged): backoff, prep in flight, runner eligibility, declared capacity.
2. `DispatchOneAsync`: `needsLease = Workspace != ReadOnly && !IsSpecialist && RepoPath != null &&
   !LaunchesPreparedMirror(task)`. Leased path unchanged; lease-free path: claim, consumer, probes,
   `PrepareRemoteWorkspaceAsync` (recorded mirror), launch, `Dispatched`.
3. On `HeldOnLease`: `DescribeLeaseHoldAsync` (unchanged) → `TraceHeldAsync` (unchanged) →
   `_leaseWaiters.Register(common, task.Id, "dispatch", now)`; `heldThisTick` records `HoldKind.Lease`.
4. End of tick: `_leaseWaiters.ReconcileDispatch(ids held on lease this tick)`; `EscalateHeldAgeAsync`
   builds the ledger from `holdIndex` rows and appends it to any escalation it writes; `TickResult`
   carries `HeldOnLease`/`HeldOnRunner`.

### The land request after R1

1. `RunRequestAsync`: eligibility and cancellation arms unchanged; `_boundary.ReachedAsync("before-execution")`.
2. **Yield check** (new): `common = CommonDirectoryAsync(task.RepoPath)`; if the setting is positive and
   `_leaseWaiters.Any(common)`: `first = FirstYield(request.Id, now)`; if `now − first < max` →
   `YieldToDispatchAsync` → `Held`; else one `Warning` and fall through. `EndYield(request.Id)` on admission
   and on every terminal return.
3. `TryAcquireAsync(Land)` → `HoldOnBusyLeaseAsync` on null (unchanged) → `RunLeasedAsync` (unchanged;
   CARD-0688 rewrites its inside independently).

### The settlement sync after R2

`WhileLeaseBusyAsync(taskId, attempt, busy, ct, caller)`: first attempt; if busy: `since = FirstBusy`;
loop per slice: observe the owner (`FindOwnerAsync`), register/clear the waiter and decide `charge`; wait
`LeaseRetryInterval`; re-attempt; on slice end return `(result, Spent)` where `Spent = LeaseWaits.Charged(taskId)
≥ SyncBudget || now − since ≥ Ceiling`. `LeaseWaits.End` clears both maps and the waiter.

### Slices

- **S1 — Lease-free prepared launch** (R1, D-1). `AgentTaskDispatcher.cs`: `LaunchesPreparedMirror`,
  `needsLease`; a comment at the lease site stating I-A. Tests V-1, V-12.
- **S2 — Waiters and the land yield** (R1, D-2). New `RepositoryLeaseWaiters.cs`; `Program.cs` and
  `DelegationTestServices.cs` registration; `AgentTaskDispatcher.cs` (optional `RepositoryLeaseWaiters?` and
  `ILandingGit?` constructor parameters, register/reconcile in `TickAsync`); `AgentTaskLandService.cs`
  (optional `RepositoryLeaseWaiters?` parameter, yield check, `YieldToDispatchAsync`, `EndYield` calls);
  `DelegationSettings.cs` (`LandYieldToDispatchMaxSeconds` + validation); `AttentionService.cs` and
  `AgentTaskLandMonitorService.cs` (age rule for the yield code). Tests V-4, V-5, V-13.
- **S3 — Hold ledger** (R1, D-3). `DispatchHoldDetails.cs` (`DispatchHoldClass`, `ClassOf`, `Escalation`
  suffix), new `DispatchHoldLedger.cs`, `AgentTaskDispatcher.cs` (`EscalateHeldAgeAsync`, `TickResult`),
  `AgentTaskDispatcherHostedService.cs` (debug line), `AttentionService.cs` and `AttentionDtos.cs`
  (`HoldClass`). Tests V-6, V-7, V-8.
- **S4 — Docs for R1.** `docs/ops-http.md` (the runner-bound task row: a prepared task launches without the
  lease; the `DispatchHeld` row: `holdClass` and the ledger fields; the land section: the
  `repository_lease_yielded_to_dispatch` hold and its age rule); `docs/orchestration-loop.md` ("Launching an
  agent": the two crossings, I-A, the turnstile and `LandYieldToDispatchMaxSeconds`; §5 land: the yield at
  admission); `docs/session-runtime-invariants.md` (I-A in one line, next to the CARD-0604 mirror rule).
- **S5 — Purpose-aware sync waiting** (R2, D-4). `RemoteWorkspaceService.cs` (`WhileLeaseBusyAsync`, waiter
  registration, ceiling), `RunnerSyncLeaseWaits.cs` (charged map, `Charge`, `Charged`, `End`),
  `DelegationSettings.cs` (`RunnerSyncLandWaitCeilingSeconds` + validation). Tests V-9, V-10, V-11.
- **S6 — Docs for R2.** `docs/orchestration-loop.md` and `docs/ops-http.md` (the D-5 R4 row: land occupancy
  is not charged; the ceiling); the CARD-0657 plan is not edited (plans are history).

### Code rounds

| Round | Slices | Files | Exit |
|---|---|---|---|
| R1 | S1–S4 | `AgentTaskDispatcher.cs`, `AgentTaskDispatcherHostedService.cs`, `AgentTaskLandService.cs`, `AgentTaskLandMonitorService.cs`, `AttentionService.cs`, `AttentionDtos.cs`, `DispatchHoldDetails.cs`, `DispatchHoldLedger.cs` (new), `RepositoryLeaseWaiters.cs` (new), `DelegationSettings.cs`, `Program.cs`, `DelegationTestServices.cs`, docs, tests | CP-1..CP-6 green; the orchestrator restarts, dispatches two server2 tasks while a land is running and posts their `Held`→`Dispatched` timestamps on the card |
| R2 | S5–S6 | `RemoteWorkspaceService.cs`, `RunnerSyncLeaseWaits.cs`, `DelegationSettings.cs`, docs, tests | CP-7..CP-9 green; one server2 task settled while a land is in flight shows `runner_sync_lease_waiting` then `Synchronized`, never `runner_sync_lease_busy` |

## Migration and rollback

- **Deploy R1.** No migration. Restart via `restart-apphost.ps1` from the main checkout; confirm
  `GET /api/version`. Queued runner tasks already prepared launch on the first tick. A land in flight at the
  restart is re-run by the sweep as before; waiters start empty.
- **Roll back R1** (build rollback): waiters and the ledger disappear; `HeldAged` rows already written keep
  their longer text (`Reason` still parses it). Setting `LandYieldToDispatchMaxSeconds=0` disables the
  yield without a rollback.
- **R2** rolls back independently; `RunnerSyncLandWaitCeilingSeconds` has no effect under the old build.

## Verification design

Vocabulary: V-n is a new red-first test; R-n is an existing class kept green as the regression net. "Red"
states what fails today at `c663126d`. All tests are cross-platform: paths through `Path.Combine`, git through
the existing fixtures (`LandingGitFixture`, `SyncWorld`, the `Rig` of `RemoteWorkspacePreparerTests`), the
waiter key's case rule asserted through `OperatingSystem.IsWindows()`, no Linux-only strings; real-git and
phone-home classes stay under `[ParallelLimiter<ProcessSpawnLimit>]`/`[Category("Integration")]` as their
neighbours are.

### Round 1

- **V-1** `RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease(string arm)`,
  arguments `prepared`, `unprepared`, `repair-source`, `snapshot`, `interim`. Rig changes first: today the
  Rig's seeded tasks carry no `RepoPath`, so `needsLease` is false and the lease is never consulted there,
  and its `PushOnlyGit.CommonDirectoryAsync` throws; the seed gains `RepoPath = WorkspacePath`,
  `PushOnlyGit.CommonDirectoryAsync` returns `Path.GetFullPath(repository)`, and a `HoldableLease`
  (`IRepositoryMutationLease` with a `Held` switch and a `FindOwnerAsync` that answers a Known `land`
  owner while held, like `DispatchHoldVisibilityTests.FakeLease`) is registered after
  `AddDelegationWorktreeGraph`. The test holds it; a seeded runner task
  (`WorktreePath` set as the Rig already does) has `RemoteWorktreePath` set (`prepared`) or null
  (`unprepared`), or additionally `RepairSourceTaskId`/`SourceLandingOperationId`/`VerificationRound =
  Interim`; one tick. `prepared`: status `Dispatched`, the launch sink has one launch, no `Held` row whose
  detail contains "repository mutation lease". Every other arm: status `Queued`, one `Held` row
  `LeaseHeldByOwner(…, "land", …)`, no launch. Red today: the `prepared` arm is held; the other arms are
  green and pin the predicate's boundary (a Mutation stage that widens D-1 must fail them).
- **V-2** `AgentTaskDispatcherPredicateTests.C672_LaunchesPreparedMirror` (new Unit class, `[Arguments]` ×7):
  the predicate over minimal `AgentTask` rows. Red: method absent.
- **V-4** `DispatchHoldVisibilityTests.C672_lease_hold_registers_a_waiter_and_dispatch_clears_it`: `FakeLease
  { Held = true }` and a `RepositoryLeaseWaiters` registered in the world; tick → `Snapshot(common)` has the
  task with purpose `dispatch` and `Since` = the tick's clock; second tick → still one entry, same `Since`;
  `Held = false` → tick → dispatched and the snapshot is empty; a task cancelled between ticks is also
  cleared. Red: type absent.
- **V-5** `AgentTaskLandDispatchYieldTests` (new, real git through `LandingSafetyHarness`, `Clock` =
  `FakeTimeProvider`, `ConfigureServices` injects the waiters and `DelegationSettings`): (a) a registered
  `dispatch` waiter for the fixture repository's common directory → `RunAsync()` returns `Held`;
  `request.State == Held`, `HoldReasonCode == "repository_lease_yielded_to_dispatch"`, `HoldDetail` names the
  waiter's short id, exactly one `Held` event, no `AgentTaskLandNotification` row, the fixture trace has no
  `rebase`/`push`/`worktree`, and the harness's `IRepositoryMutationLease` (resolved from `Services`) still
  acquires in the test (the lease was never taken); (b) a second `RunAsync()` with the waiter still present writes no second `Held` event; (c) the
  waiter cleared → `RunAsync()` lands and a `HeldReleased` event precedes the admission; (d) the clock
  advanced 91 s with the waiter present → `RunAsync()` lands with one `Warning` "yield budget exhausted";
  (e) `LandYieldToDispatchMaxSeconds = 0` → no yield; (f) a waiter keyed to another directory → no yield;
  (g) a `worktree-settlement` waiter yields exactly like a `dispatch` one. Red: hold code absent.
- **V-6** `DispatchHoldVisibilityTests.C672_held_aged_carries_the_per_class_wait_ledger`: seeded `Held` rows
  after the floor — lease at t0, remote prep at t0+120 s, lease at t0+150 s — and a tick at t0+350 s under
  a held `FakeLease`; the `Warning:` `HeldAged` detail contains `leaseWait=320s`, `prepWait=30s`,
  `runnerWait=0s`, `capWait=0s`, `class=lease`, still contains `running=` and `occupants=`, and
  `DispatchHoldDetails.Reason(detail)` equals the last lease sentence. Red: fields absent.
- **V-7** `DispatchHeldAttentionTests.C672_dispatch_held_item_names_the_dominant_hold_class`: seeded rows lease
  (200 s) then runner capacity (150 s) → the item's `HoldClass == "lease"`, `Evidence` contains
  `leaseWait=200s` and `runnerWait=`; plus `C672_land_yield_hold_is_not_an_attention_item_before_the_warning_age`:
  a `Held` land request with the yield code aged 20 s → no `LandHeld` item; aged 301 s → one. Red: property
  and rule absent.
- **V-8** `DispatchHoldLedgerTests` (new, Unit): `ClassOf` over every `DispatchHoldDetails` text
  (`[Arguments]` ×11 incl. an escalation row, whose reason is unwrapped first, and an unknown sentence →
  `Other`); `FromRows` sums stints, ignores rows before the floor, treats the open stint as running to
  `now`, and picks the dominant class with `Lease` winning ties. Red: types absent.
- **V-13** `RepositoryLeaseWaitersTests` (new, Unit): register/snapshot/clear; `ReconcileDispatch` keeps
  `worktree-settlement` entries and drops absent `dispatch` ones; `KeyFor` equates a trailing-separator
  path with its trimmed form and equates case only when `OperatingSystem.IsWindows()`; `FirstYield` is
  stable per request until `EndYield`. Red: type absent.
- **V-12** `RemoteWorkspacePreparerTests.C672_three_queued_runner_tasks_cross_the_lease_once_and_launch_behind_the_next_land`
  (the card's scenario, on the V-1 Rig): three seeded runner tasks; (1) `HoldableLease.Held = true` → tick →
  three `Held` lease rows, three `dispatch` waiters in the Rig's `RepositoryLeaseWaiters`; (2) released →
  tick → three `RemoteMirrorRequested` holds, waiters empty, the peer answers three `WorkspaceMirror`
  requests, `WhenIdleAsync`; (3) held again → tick → all three `Dispatched`, three launches, no new `Held` row. Red today:
  step 3 leaves all three `Queued` with a lease hold.
- **R-1** existing, kept green: `DispatchHoldVisibilityTests`, `DispatchHeldAttentionTests`,
  `ExpectationSnapshotTests`, `ExpectationPipelineTests`, `ExpectationLedgerTests` (they parse `Held` and
  `HeldAged` text through `Classify`/`Reason`), `RemoteWorkspacePreparerTests`,
  `DispatcherRemotePrepStarvationTests`, `AgentTaskLandAdmissionTests`, `AgentTaskLandAdmissionControlledTests`
  (dispatch/land exclusion in every order: with no waiter registered nothing changes; with the harness's
  `InterceptedLease` the yield path is off because no waiters exist), `AgentTaskLandHoldVisibilityTests`,
  `AgentTaskLandHeldNotificationTests` (a yield emits no note), `AgentTaskLandConcurrencyTests`,
  `AgentTaskLandConcurrencyControlledTests`, `AgentTaskDispatchBaseGuardTests`,
  `PhoneHomeTaskDispatchProjectionTests`, `DefaultRunnerPinTests`, and R2's classes (R1 must not change
  sync behaviour: CP-5).

### Round 2

- **V-9** `RunnerSettlementSyncTests.C672_land_occupancy_does_not_spend_the_sync_budget`: the world's lease
  acquired by the test with `new RepositoryLeaseOwnerTag(landTaskId, RepositoryLeasePurposes.Land)`; the
  sweep timeline of `Sustained_lease_contention…` extended to 600 s of clock in 10 s slices → every result
  `runner_sync_lease_waiting`, the task is registered as a `worktree-settlement` waiter, `Charged` stays
  zero; release → next sweep `Synchronized`. Red today: `runner_sync_lease_busy` at 120 s.
- **V-10** `RunnerSettlementSyncTests.C672_land_wait_ceiling_blocks`: as V-9 with the clock advanced to
  1799 s (still waiting) then 1800 s (`runner_sync_lease_busy`); the waiter is cleared on the block. Red:
  blocks at 120 s.
- **V-11** `RunnerSettlementSyncTests.C672_untagged_and_foreign_purpose_holders_still_charge` (`[Arguments]`
  untagged, `gated-commit`, `worktree-provision`): the existing 120 s behaviour holds; plus
  `DelegationLeaseSettingsTests` (new, Unit): `LandYieldToDispatchMaxSeconds` outside 0..600 and
  `RunnerSyncLandWaitCeilingSeconds < RunnerSyncBudgetSeconds` are validation failures naming the key. Red:
  the validation rows are absent (the holder arms are green pins).
- **R-2** existing: `RunnerSettlementSyncTests` (incl. `Sustained_lease_contention…`, unchanged because its
  holder is untagged), `RunnerTaskSettlementTests`, `RemoteWorktreeMirrorTests`, `TaskProgressGitTests`, plus
  R-1's S2/S3 classes.

### Checkpoints

Test project `tests/Antiphon.Tests`; isolated outputs `bin-c672a/` (R1) and `bin-c672b/` (R2), forward slash;
one build per round, every other row `--no-build`; on server2 `run-checkpoint.ps1` adds `UseAppHost=false`
itself. `Min` is the count of `[Test]` methods in the named classes at `c663126d` (argument-expanded rows
counted per argument) plus the new methods, minus a 10 % margin; a floor, not a census.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c672a/` | hold-unit | `/*/*/(DispatchHoldLedgerTests*)\|(RepositoryLeaseWaitersTests*)\|(AgentTaskDispatcherPredicateTests*)\|(DispatchHoldVisibilityTests*)\|(DispatchHeldAttentionTests*)\|(ExpectationSnapshotTests*)\|(ExpectationPipelineTests*)\|(ExpectationLedgerTests*)/*` | V-2, V-4, V-6, V-7, V-8, V-13, R-1 | all listed, 0 failed | 50 | 8 |
| CP-2 | S1-S3 | CP-1 | remote-prep-integration | `/*/*/(RemoteWorkspacePreparerTests*)\|(DispatcherRemotePrepStarvationTests*)/*` | V-1, V-12, R-1 | all listed, 0 failed | 15 | 8 |
| CP-3 | S1-S3 | CP-1 | land-admission | `/*/*/(AgentTaskLandDispatchYieldTests*)\|(AgentTaskLandAdmissionTests*)\|(AgentTaskLandAdmissionControlledTests*)\|(AgentTaskLandHoldVisibilityTests*)\|(AgentTaskLandHeldNotificationTests*)/*` | V-5, R-1 | all listed, 0 failed | 60 | 10 |
| CP-4 | S1-S3 | CP-1 | dispatch-land-concurrency | `/*/*/(AgentTaskLandConcurrencyTests*)\|(AgentTaskLandConcurrencyControlledTests*)\|(AgentTaskDispatchBaseGuardTests*)\|(PhoneHomeTaskDispatchProjectionTests*)\|(DefaultRunnerPinTests*)/*` | R-1 | all listed, 0 failed | 55 | 10 |
| CP-5 | S1-S3 | CP-1 | sync-unchanged-in-r1 | `/*/*/(RunnerSettlementSyncTests*)\|(RunnerTaskSettlementTests*)\|(RemoteWorktreeMirrorTests*)/*` | R-1 (sync behaviour untouched by R1) | all listed, 0 failed | 34 | 6 |
| CP-6 | S4 | n/a | docs-r1 | `git grep -n -e "repository_lease_yielded_to_dispatch" -e "LandYieldToDispatchMaxSeconds" -e "holdClass" -e "never writes the desktop checkout" -- docs/ops-http.md docs/orchestration-loop.md docs/session-runtime-invariants.md` | S4 | ≥ 5 matching lines across all three files, exit 0 | n/a | 1 |
| CP-7 | S5 | `tests/Antiphon.Tests -> bin-c672b/` | sync-wait | `/*/*/(RunnerSettlementSyncTests*)\|(RunnerTaskSettlementTests*)\|(RemoteWorktreeMirrorTests*)\|(TaskProgressGitTests*)/*` | V-9, V-10, V-11, R-2 | all listed, 0 failed | 45 | 8 |
| CP-8 | S5 | CP-7 | waiters-and-settings | `/*/*/(DelegationLeaseSettingsTests*)\|(RepositoryLeaseWaitersTests*)\|(AgentTaskLandDispatchYieldTests*)/*` | V-11, V-13, V-5(g), R-2 | all listed, 0 failed | 12 | 4 |
| CP-9 | S6 | n/a | docs-r2 | `git grep -n -e "RunnerSyncLandWaitCeilingSeconds" -e "runner_sync_lease_waiting" -- docs/ops-http.md docs/orchestration-loop.md` | S6 | ≥ 2 matching lines, exit 0 | n/a | 1 |

The pipe characters inside the `Filter` cells are escaped for the table; the command line uses a plain
`|`, quoted as [docs/testing-and-build.md](../../testing-and-build.md#combined-class-filters-card-0403)
shows. Run each row with `scripts/run-checkpoint.ps1 -Name CP-n -Project tests/Antiphon.Tests -OutputPath
bin-c672x/ -Filter '<filter>' -MinExecuted <Min> -Expect <classes> -ResultsRoot .antiphon/c672-checkpoints`
(`-NoBuild` for reuse rows). CP-2 hosts `PhoneHomeTestHost` like CARD-0633's verification; if the Code
runner cannot host it, the row is reported as not run with the reason and Review runs it on the desktop
before the land. Unlisted runs need a stated reason; a compile error found by a row's own build is fixed
and the same row rerun. No E2E row: nothing in the E2E fixtures registers a waiter, so the yield path is
inert there; Review may run `AgentTaskLandDeliveryE2ETests` on the desktop as an unlisted confirmation.

### Cost

Ordinary Code floor: R1 = 43 minutes of checkpoints plus authoring (the waiters registry, the dispatcher
register/reconcile and predicate, the land yield with its budget, the ledger and its two consumers, three
new test classes and seven new tests in existing classes, three doc edits: about 5 h); R2 = 13 minutes of
checkpoints plus about 2 h (the waiting accounting, the ceiling, three tests, two doc edits). `-ExpectAbout`
for each Code dispatch is that sum. Review reads the CHECKPOINT lines against the table and, for R1, the
two server2 dispatch timelines the orchestrator posts after activation.

## Follow-ups (not in this card)

- **Wake the dispatcher on lease release** (D-2 rejected (e)): `RepositoryMutationLease.OwnedLease.DisposeAsync`
  could signal a waiting tick; saves at most one `PollIntervalSeconds` per crossing. File only if the posted
  timelines show the residual tick latency matters.
- **Desktop worktree optional for runner tasks** (D-1 rejected alternative): push the base commit to the task
  branch without cutting a desktop worktree and let the CARD-0657 sync *create* the checkout at settlement.
  Removes crossing 1's mutation entirely; needs CARD-0604 D-15 and CARD-0657 D-2 revisited, after CARD-0688 R1.
- **Local progress observation behind a land**: `TaskCompletionProgressService` for local Code tasks makes
  one untagged lease attempt and reports `progress=unavailable; reason=repository_lease_busy` (Indeterminate,
  fail-open). It could register as a waiter and retry once at the next sweep, the way the runner sync does.
- **Child-journal fence during dispatch** (`d81207e7` 16:06:46): a `Held: repository mutation lease is fenced`
  row appeared for 15 s while another task's crossing 1 ran; the fence is the journal's design, but the
  dispatcher could classify it as `Lease` in the ledger (it does, via `ClassOf`) and the sweep could note
  which child was unfinished.

--- next stage ---
next: code
handoff: CARD-0672 R1 per docs/superpowers/plans/2026-09-25-card-0672-remote-prep-lease-plan.md: S1 lease-free launch of a prepared runner task (LaunchesPreparedMirror), S2 RepositoryLeaseWaiters + land yield at admission (repository_lease_yielded_to_dispatch, LandYieldToDispatchMaxSeconds), S3 per-class hold ledger on HeldAged and DispatchHeld (HoldClass), S4 docs; run CP-1..CP-6 as a closed list; R2 (S5-S6, purpose-aware sync wait) after R1 activates.
artifact: docs/superpowers/plans/2026-09-25-card-0672-remote-prep-lease-plan.md
