# CARD-1108: anchor the legacy park window on the server clock, gate and bound the reclaim sweep, and keep its counters truthful

Date: 2026-10-06. Plan task: `8b366539-391c-4df8-bb6a-73283c795300`.
Inspected source: `3599aa4b5` (`origin/master` with CARD-1065 S9 and S10 landed).
Assigned branch: `feat/card-task-8b366539`; fast-forward-only from `5b713f685`.
Status: Plan and verification design complete under the defaults in `## Decisions`;
the brief folds test design into this dispatch, so **next: land**, then Code S1.
Platform read before this plan: `GET /api/runner-defaults` global runner `server2`;
`GET /api/session-runners` shows `server2` draining and not accepting new work,
`server2-temp` (Linux, 6/10 sessions) and `desktop` (Windows) accepting. No runner pin
and no platform pin: every change is server-side and every checkpoint row runs on the
Linux lane.

## Outcome and scope

CARD-1065 S9 landed a legacy reclaim sweep that pages current Blocked tasks into the
same guarded park episode the fast path uses. The S9 Final Review (task `53759b67`)
found six Backlog items, none a defect while parking is default-off. This card closes
them without changing what parking authorises:

1. The post-discovery idle window is decided from the server clock only. The runner's
   wall-clock `FirstObservedAt` no longer enters the decision; its `StableFor` is used
   only as a duration, which is the convention the same file already states.
2. The sweep runs through one time-gated, overlap-free entry point shared by the
   dispatcher tick and the Hangfire job, visits each eligible Blocked row at most once
   per run, and reports a measured result.
3. The reconcile job's return counts confirmed releases only.
4. A Held episode is not re-prepared until its backoff elapses, in every caller.
5. The dispatcher hook has its own named test, and the disabled sweep is proven inert
   at the hook.
6. `AdvanceAsync` verifies the publication once per dispatch when no test boundary is
   installed.

Everything stays default-off with `BlockedTaskParking:Enabled`, `ReclaimExisting` and
`TerminalRunnerSeatRelease:AutomaticEnabled`. No new setting defaults on. No migration.
No production session, card, runner budget or deployment setting was changed during
this Plan; no test or build ran.

## Ground truth

| # | The card assumes | What the code does | Where |
|---|---|---|---|
| 1 | `FreshLegacyWindow` mixes runner wall-clock timestamps with the server's `park.CreatedAt`, against the file's own rule. | Confirmed. `first.UtcDateTime + observed.StableFor >= opened.AddSeconds(120)` compares the runner's `FirstObservedAt` (set from the runner's `clock.GetUtcNow()` when its proof is created) with the server-written `CreatedAt`. The rule "StableFor is measured by the runner; never subtract its timestamps from our clock" sits above the general 120 s gate. Under equal clocks the expression is exactly "the observation instant is at least 120 s after discovery"; a runner clock ahead by N seconds shortens that by N. Idleness (`StableFor >= 120 s`, transcript Idle, token) and both publication fences are unchanged, so it is fail-closed as the card says. The window is checked twice: before the reservation cut and again inside the reservation CAS. | `server/Application/Services/TerminalRunnerSeatReleaseService.cs:1133-1141` (window), `:1013` (rule), `:1013-1017` (idleness gate), `:1020-1023` and `:1081-1084` (callers), `:996` (observation RPC); `src/Antiphon.SessionRunner/TerminalSeatReleaseObservation.cs:46` (`clock.GetUtcNow()` is the runner clock) |
| 2 | Wrap-around revisits each Blocked row up to three times per call, on every 5 s tick and the 2-minute job, with no gate like `DiscoverScheduledAsync`; each visit can run Git inspection and observation RPCs; overlapping sweeps can collide at `park_workspace_reserved`. | Confirmed, and slightly worse than stated: `NextLegacyPageAsync` wraps whenever the tail is short, so with N < pageSize Blocked rows every pass returns all N rows and the `page.Count == 0` exit never fires; a call with the production arguments (32, 3) makes 3N visits (5 rows: 15 visits). The dispatcher calls it inside the "pool release" sweep on every tick (`Delegation:PollIntervalSeconds` default 5, sweep budget 60 s); the job calls it on `*/2 * * * *`, once at startup, and from the recovery pump. `DiscoverScheduledAsync`'s gate is a `SemaphoreSlim(1,1).WaitAsync(0)`, so it prevents overlap but not frequency. Per visit: `RegisterLegacyAsync` (two reads plus an insert under the task lock), then `TryHandleTaskAsync`, which for a Requested or Held park without a receipt runs `PrepareAsync` (identity capture, reservation admission, Git inspection and possibly a push) and for a Published park runs the observation RPC plus `VerifyAsync` at the gate and again in `AdvanceAsync`. Two callers preparing the same park collide at `TryAdmitConsumerAsync` and the loser is Held `park_workspace_reserved`; the next `PrepareAsync` re-requests it through `PersistIntentAsync`. | `TerminalRunnerSeatReleaseService.cs:107-135` (loop), `:137-147` (discovery gate), `:59-99` (`TryHandleTaskAsync`); `BlockedTaskParkingService.cs:128-141` (page and wrap), `:143-158` (cursor); `AgentTaskDispatcher.cs:7440-7444` (hook), `:381` (pool-release sweep), `server/Application/Settings/DelegationSettings.cs:18` and `:608`; `RunnerSlotReconcileJob.cs:40-49`; `PhoneHomeRunnerSettings.cs:113` (cron), `HangfireConfiguration.cs:38-47`, `PhoneHomeRecoveryPump.cs:158`; `TaskParkPublicationService.cs:89-139` (`PrepareAsync`), `:122-124` (collision hold), `:286-319` (re-request), `:142-150` (`VerifyAsync`) |
| 3 | The reconcile job's `released` return includes visit counts. | Confirmed: `released = DiscoverScheduledAsync(...)` then `released += ReclaimLegacyAsync(32, 3, ...)`, and the latter returns the visit count. Nothing branches on the return; Hangfire stores it. The two existing job assertions (`1` and `0`) run without the seat service and are unaffected by the fix. | `RunnerSlotReconcileJob.cs:46-48`; `tests/Antiphon.Tests/Application/RunnerSlotEndpointTests.cs:162`, `:205` |
| 4 | The S9 Code report credits hook coverage to `AgentTaskPoolTests`, which runs without the seat service; the hook is covered only by the `SweepAsync` arm of CP-9. | Confirmed. `AgentTaskPoolTests.CreateHarness` constructs its dispatcher without `terminalSeatRelease`, so the `_terminalSeatRelease is not null` branch is never taken there. `RunnerSeatReleaseFixture` passes the service, and the only execution of the hook is the `sweep` arm inside `C1065_LegacySweepRequiresFreshPublicationAndIdleWindow`. | `tests/Antiphon.Tests/Application/AgentTaskPoolTests.cs:1396`; `RunnerSeatReleaseFixture.cs:212-225`, `:311-318`; `BlockedTaskParkReclaimTests.cs` (the `sweep` block after the cold resume) |
| 5 | The red commit carried most production code and stubbed only the sweep loop, so G-171..G-181 rest on the pending PCs. | Confirmed. `59737e78c` touches 14 files (+11391): the registration, paging, cursor, holds and fresh-window code, the entity, migration and snapshot, with `ReclaimLegacyAsync` returning 0. The feat commit `5ad7d2682` adds only +25/-3 in the service. The S9 report lists PC-23, PC-24 and G-171..G-181 as "Pending for method-scoped SourceLanding Mutation"; CARD-1065 sits in Review, so that Mutation has not run. S1 of this card changes the production line PC-171 targets, which makes the original PC-171 mutation a no-op (see D-7). | `git show --stat 59737e78c`, `5ad7d2682`; `.antiphon/task-120c40b2.md` (Positive controls); CARD-1065 plan `:1138-1148`, `:1388-1398` |
| 6 | `AdvanceAsync` verifies source twice per dispatch when the boundary is null. | Confirmed. Both calls are `ReleaseSourceAsync(release, verify: true, ct)`, each a `VerifyAsync` that re-inspects the real checkout (`merge-base`, `status`, `ls-remote` through the local publisher). `BoundaryAsync` is an internal seam that only test fixtures assign; production never sets it, so the cut between the two reads does not exist in production and the second read is a duplicate. | `TerminalRunnerSeatReleaseService.cs:519-551` (`:542`, `:546`), `:46` (seam), `:735-744`; `server/Infrastructure/Git/LocalTaskParkPublisher.cs:38-170` |
| 7 | (Not on the card.) Four sentences in the owner doc describe the S9 behaviours above and are pinned by a Unit test. | `C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` requires "A short tail wraps, a call makes at most three visits, and there is no discovery gate.", "The reconcile job's released total includes those visit counts.", "FreshLegacyWindow mixes the runner observation clock with park.CreatedAt. ..." and "ReclaimLegacyAsync(32, 3) runs from the dispatcher pool-release sweep and from RunnerSlotReconcileJob." Each slice that changes a behaviour rewrites its sentence and its pin in the same commit. | `docs/session-runtime-invariants.md:108-112`; `tests/Antiphon.Tests/Application/BlockedTaskParkProjectionTests.cs:69-73` |
| 8 | (Not on the card.) A per-row backoff needs new schema. | No. `AgentTaskPark.NextAttemptAt` exists with the index `(State, NextAttemptAt)` and no park service reads or writes it (only `SyncNextAttemptAt` is used). D-4 uses it without a migration. | `server/Domain/Entities/AgentTaskPark.cs:58`; `server/Infrastructure/Data/AppDbContext.cs:143` |

Also inspected: `TryReserveAsync` is called directly by one seat-release test, so its
signature change in S1 keeps a defaulted parameter
(`tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs:1036`).
`BlockedTaskParkReclaimTests` is already in `tests/Antiphon.Tests/slow-tests-allowlist.txt:3`;
every new test in this card lives in that class, so no Slow registration changes.
`RunnerSeatReleaseFixture.CreateAsync(..., parking:, reclaim:)` builds the options
in-process (`:171-175`), so new option values are fixture parameters, not configuration.

## Decisions

Defaults, not questions. None needs a human call; D-4's backoff length is the only value
an operator may want to tune later and it is configuration.

### D-1. The legacy window is decided from the server clock and a runner duration

`FreshLegacyWindow(park, observed, observedAt)` returns true for a legacy episode only
when `observed.StableFor >= 120 s` and `observedAt - park.CreatedAt >= 120 s`, where
`observedAt` is `clock.GetUtcNow()` read on the server immediately before the
observation RPC is issued. `FirstObservedAt` is ignored; it stays on the wire as a
diagnostic. The pre-RPC timestamp is a lower bound for the observation instant, so
latency can only make the check stricter. The same `observedAt` is passed into
`TryReserveAsync` (new defaulted parameter, default "now") so the CAS fence re-checks
the same instant against the park it re-reads under lock.

Why it is sufficient: the runner's proof resets on any input, output, transcript,
generation or epoch change, so `StableFor` is a contiguous idle interval ending at the
observation. An interval of at least 120 s ending at least 120 s after discovery
contains at least 120 s of post-discovery idleness whether it began before or after
discovery. Under synchronised clocks this is exactly today's rule; the ancient
`CompletedAt` arm (30 h stable, discovered now) still holds.

Rejected: (a) the Review's suggested `StableFor >= now - park.CreatedAt`: it requires
stability since before discovery, so a session that was Working at discovery can never
qualify (elapsed grows as fast as `StableFor`), a permanent hold on legitimate
candidates. (b) Persisting a server-side first-stable timestamp on the park: a
migration, and it duplicates the runner proof's reset semantics on the server. (c)
Keeping the `FirstObservedAt is null` refusal: a fail-closed check on a field the
decision no longer reads. (d) Changing the runner: the runner already reports a clean
duration; the defect is server-side.

Consequences: the two boundary arms of V-23 that vary `FirstObservedAt` against
`CreatedAt` are re-expressed by advancing the fixture clock (`119.999 s` held,
`120.001 s` released) with `StableFor` fixed at `120.001 s`; the owner sentence and its
pin change; CARD-1065 PC-171 is superseded (D-7).

### D-2. One gated scheduled sweep shared by both callers

`TerminalRunnerSeatReleaseService.ReclaimScheduledAsync(ct)` is the entry point for the
dispatcher hook and the reconcile job. Order of checks: the existing enabled test
(`Enabled && ReclaimExisting && parks is not null`) first, so a disabled sweep touches
no state and runs no query; then `TerminalRunnerSeatDiscoveryState.LegacyGate`
(`SemaphoreSlim(1,1).WaitAsync(0)`, mirrors `DiscoverScheduledAsync`); then
`NextLegacySweepAt` on the same singleton, set to `now + ReclaimIntervalSeconds` after
each run. `BlockedTaskParkingOptions.ReclaimIntervalSeconds` defaults to 120; 0 means
every call sweeps (the gate still prevents overlap); a negative value is treated as 0.
`ReclaimLegacyAsync(pageSize, passBudget, ct)` stays the ungated primitive that the
tests and a future operator trigger call.

With the defaults the dispatcher runs the sweep on at most one tick in 24 and the job
run usually finds the gate closed; across both callers the sweep runs about once per
two minutes instead of up to thirteen times.

Rejected: (a) a time gate inside `ReclaimLegacyAsync`: V-23 calls it in a loop on a
clock that does not advance, and the primitive is the right tool for a one-off
operator sweep. (b) A new Hangfire job: the brief and S9 say no new job. (c) A durable
gate row: losing the in-memory timestamp on restart costs one extra sweep, the same
trade `TerminalRunnerSeatDiscoveryState` already makes for the discovery cursor.

### D-3. A measured, bounded run; the job counts releases only

`ReclaimLegacyAsync` returns `LegacyReclaimResult(int Visited, int Registered, int
Released, int Eligible, int Cap, TimeSpan Elapsed)`. `Eligible` is the count of Blocked
rows at run start; `Cap = Math.Min(Eligible, pageSize * passBudget)`; the loop stops at
the cap or at an empty page, so each eligible row is visited at most once per run
while the cursor still wraps for restart fairness. `Registered` counts visits whose
`RegisterLegacyAsync` returned an id; `Released` counts visits after which
`FindAttemptReleaseAsync` is `IsConfirmed` (one indexed read per visit, the same
disposition rule discovery uses). `Elapsed` is `Stopwatch` wall time, diagnostic only.
One information log line per run carries the six fields; the singleton stores
`LastLegacyReclaim` and `LegacySweptAt` for the hook test and for operators reading
logs. `ReclaimScheduledAsync` returns the same record or a zero record when gated.

`RunnerSlotReconcileJob.ExecuteAsync` returns `finished.Count + discovery.Released +
reclaim.Released`. The fixture's `ReclaimAsync` keeps returning `int` (`Visited`) so
the existing V-23/V-24 assertions do not change; it also exposes the record.

Rejected: counting handled or `ReleasePending` parks as released (neither is an exit
certificate); dropping the wrap (restart fairness, G-174); a run deadline separate from
the dispatcher's 60 s sweep budget (the cap already bounds work, and the job has no
budget to borrow).

### D-4. Held episodes back off through the existing `NextAttemptAt`

Every writer that persists `AgentTaskParkState.Held` stamps `park.NextAttemptAt = now +
ReclaimHeldBackoffSeconds` (default 600; 0 disables; negative treated as 0), with one
exception: `park_workspace_reserved` is the transient loser of a reservation race and
gets no backoff (`NextAttemptAt = null`). `PersistStateAsync` takes an optional
`nextAttemptAt` and writes it alongside the state; `TaskParkPublicationService.HoldAsync`
and the two Held writes in `TryHandleTaskAsync` pass it; `PersistIntentAsync` clears it
when it re-requests a Held park. `TryHandleTaskAsync` returns true without preparing
when the current park is `Held` and `NextAttemptAt > now`.

The gate sits in `TryHandleTaskAsync` because that is the single decision every caller
reaches: the sweep, the fast path and discovery (which re-handles every Blocked seat in
inventory on each tick while automatic release is on). A dirty, binding-missing or
ambiguously owned park is therefore inspected once per ten minutes rather than every
tick or every sweep; a Published park waiting for its idle window is not Held and keeps
its two-minute observation cadence from D-2.

Rejected: (a) a new column and migration (`NextAttemptAt` and its index exist unused);
(b) excluding backed-off rows in `NextLegacyPageAsync` as well: a second guard on the
same fact with no independent witness, while a backed-off visit is two reads and a
cursor write; (c) reason-specific backoff tables: one duration and one named transient
exception is enough until an operator asks for more.

### D-5. The hook gets its own named test, including the inert proof

A dedicated method drives `AgentTaskDispatcher.ReleaseUnownedPoolDelegatesAsync` with
the seat service present and reads `TerminalRunnerSeatDiscoveryState` afterwards:
enabled, the hook registers one legacy park and stamps the sweep; called again inside
the interval it is gated; with `ReclaimExisting` off (and with `Enabled` off) it
registers nothing, stamps nothing and leaves no cursor row. The S9 report's attribution
to `AgentTaskPoolTests` is replaced by this test; `AgentTaskPoolTests` stays as it is.

### D-6. `AdvanceAsync` re-reads the source only when a boundary is installed

The first `ReleaseSourceAsync(verify: true)` and its silent early return stay exactly as
today. The `BeforeDispatch` cut and the second verified read run only when
`BoundaryAsync is not null`; with the cut, a missing source still records
`PublicationRequired` as today. Production therefore verifies once per dispatch (plus
the publication-gate verify before reservation, which is a different fence) and every
CARD-1065 "source changes at `BeforeDispatch`" arm keeps its re-read.
`TaskParkPublicationService` gains `internal Func<Guid, CancellationToken, Task>?
BeforeVerifyAsync { get; init; }` next to `BeforeReceiptSaveAsync`, and
`RunnerSeatReleaseFixture` registers the service through a factory that wires both
seams from the wire object, so the witness counts verifies.

Rejected: a single read followed by `PendingAsync(PublicationRequired)` (changes the
production path from a silent return to a ledger write); dropping the post-cut read
(breaks G-178's send arm).

Amendment 2026-10-07 (CARD-1141): the code measures 3 verifies without a boundary and 4 with one, because `RegisterAndReserveAsync` verifies at the entry gate and again after `BeforeReservation`; the committed pins are 3 and 4, and PC-11's red is `VerifyCalls` 4 against the pin 3.

### D-7. Mutation coverage of the S9 guards

This card's Mutation (post-land, method-scoped SourceLanding) runs PC-1..PC-11 below and
re-runs CARD-1065 PC-172..PC-181, PC-23 and PC-24 at the landed SHA, because S1-S4 edit
the production lines those controls target (`TerminalRunnerSeatReleaseService.cs`).
PC-171 ("initialize first observation from old CompletedAt") targets `FirstObservedAt`,
which D-1 stops reading, so after S1 lands that mutation compiles to a no-op and cannot
go red; PC-1 and PC-2 of this plan supersede it. S1 adds a one-line amendment under
PC-171 in the CARD-1065 plan pointing here. If CARD-1065's own Mutation runs at a SHA
before this card lands, it uses the original PC-171 unchanged.

### D-8. Dormant by default, documented where parking is documented

No new entry point runs unless `Enabled` and `ReclaimExisting` are both true; the new
options only shape an enabled sweep. `docs/session-runtime-invariants.md` keeps one
sentence per changed behaviour (D-1, D-2, D-3, D-4) and the Unit pins follow;
`docs/antiphon-api.md` and `docs/ops-http.md` name the two new settings beside
`ReclaimExisting`. No AGENTS.md change: the safety core already says parking defaults
off and never stops a Working session.

### D-9. Checkpoints name the Linux lane and keep one class family per row

Server-only change, isolated PostgreSQL per test, no Windows-specific path: every row
runs on a Linux runner through the checkpoint tool. Rows combine classes only where
the S9 Review or CARD-1082 already ran them together or where every class carries
`[NotInParallel("MessageQueue")]`; `TerminalRunnerSeatReleaseTests` and
`RunnerSeatOrphanSweepTests` run alone and serial (the S9 Code task hit Postgres 53300
on combined filters). The brief's regression classes run after every slice.

## Implementation slices

Each slice is 30-60 minutes including its checkpoint allowance. All four touch
`TerminalRunnerSeatReleaseService.cs` and `BlockedTaskParkReclaimTests.cs`, so they
run in order S1, S2, S3, S4, one Code task at a time. Commit and push each slice; run
its rows through the checkpoint tool before the next slice starts. Every new test goes
into `tests/Antiphon.Tests/Application/BlockedTaskParkReclaimTests.cs` (already Slow
and allowlisted; no new class, no new Slow registration). Red first: the new method
must fail against the inspected source before the production edit.

| Slice | Files and boundary | Tests / budget |
|---|---|---|
| S1 (D-1, D-7) | `server/Application/Services/TerminalRunnerSeatReleaseService.cs`: read `observedAt` before the observation RPC (`:975`), pass it to `FreshLegacyWindow` and `TryReserveAsync` (defaulted parameter), rewrite `FreshLegacyWindow` to `StableFor >= 120 s && observedAt - CreatedAt >= 120 s`, update its doc comment. `docs/session-runtime-invariants.md:112` new sentence: "FreshLegacyWindow uses StableFor only as a duration and requires the server-clock interval since park.CreatedAt to reach 120 seconds; the runner's FirstObservedAt is ignored. An old CompletedAt is not the idle window."; `tests/.../BlockedTaskParkProjectionTests.cs:73` pin follows. CARD-1065 plan: one-line amendment under PC-171. | New `C1108_LegacyWindowIsServerAnchored` (V-1); V-23's two boundary arms advance `f.Clock` instead of moving `FirstObservedAt`. CP-1..CP-5. 34 author + 18 check = 52 min. |
| S2 (D-2, D-3, D-5) | `TerminalRunnerSeatReleaseService.cs`: `LegacyReclaimResult`, cap and counters in `ReclaimLegacyAsync`, new `ReclaimScheduledAsync`; `TerminalRunnerSeatDiscoveryState` (same file) gains `LegacyGate`, `NextLegacySweepAt`, `LegacySweptAt`, `LastLegacyReclaim`. `server/Application/Settings/BlockedTaskParkingOptions.cs`: `ReclaimIntervalSeconds = 120`. `AgentTaskDispatcher.cs:7443` and `RunnerSlotReconcileJob.cs:47-48` call `ReclaimScheduledAsync`; the job adds `.Released`. Docs: sentences for D-2/D-3 replace the "three visits, no discovery gate" and "released total includes those visit counts" lines and the callers line names `ReclaimScheduledAsync`; `antiphon-api.md` and `ops-http.md` name `ReclaimIntervalSeconds`; pins follow. `RunnerSeatReleaseFixture.cs`: `ReclaimAsync` returns `Visited`, new `ReclaimScheduledAsync()` and `ReclaimResultAsync(...)` helpers, `State` accessor for the singleton. | New `C1108_ScheduledSweepIsGatedAndBoundedPerRun` (V-2), `C1108_ReconcileJobCountsOnlyReleases` (V-3), `C1108_DispatcherHookRunsTheGatedSweep` (V-4); V-23 asserts `Released` on its releasing run. CP-6..CP-12. 34 + 26 = 60 min. |
| S3 (D-4) | `BlockedTaskParkingService.cs`: `PersistStateAsync(..., DateTime? nextAttemptAt = null)` writes `NextAttemptAt`. `TaskParkPublicationService.cs`: `HoldAsync` computes the backoff (null for `park_workspace_reserved`); `PersistIntentAsync` sets `NextAttemptAt` null. `TerminalRunnerSeatReleaseService.cs`: the gate at the top of the Blocked branch in `TryHandleTaskAsync` and the backoff on its two Held writes. `BlockedTaskParkingOptions.cs`: `ReclaimHeldBackoffSeconds = 600`. Docs sentence for D-4 plus the setting name in `antiphon-api.md`/`ops-http.md`; pin follows. `RunnerSeatReleaseFixture.cs`: `reclaimHeldBackoffSeconds` parameter, `HandleAsync(taskId)` helper calling `TryHandleTaskAsync`. | New `C1108_HeldEpisodesBackOffUntilNextAttempt` (V-5). CP-13..CP-17. 36 + 19 = 55 min. |
| S4 (D-6) | `TerminalRunnerSeatReleaseService.cs:542-551`: boundary-conditional re-read. `TaskParkPublicationService.cs`: `BeforeVerifyAsync` seam invoked at the top of `VerifyAsync`. `RunnerSeatReleaseFixture.cs`: factory registration of `TaskParkPublicationService` wiring `BeforeVerifyAsync` and `BeforeReceiptSaveAsync` from the wire; `VerifyCalls` counter. Docs: drop CARD-1108 from the "Known limits stay on ..." sentence in `docs/session-runtime-invariants.md:113` (not pinned). | New `C1108_AdvanceVerifiesSourceOnceWithoutBoundary` (V-6). CP-18..CP-22, then CP-23..CP-29 at the final SHA. 26 + 19 + 26 final = 71 min, of which the final set is run once by the S4 Code task after its own rows. |

No `antiphon.areas.json` change. No migration: S3 writes an existing column. The
fixture changes are confined to `RunnerSeatReleaseFixture.cs`; `BridgeQueueHarness`
and other fixtures are not touched.

## Verification design

### Inspection

| Read | Why |
|---|---|
| `TerminalRunnerSeatReleaseService.cs:40-147` (constructor, `TryHandleTaskAsync`, `ReclaimLegacyAsync`, `DiscoverScheduledAsync`), `:438-460` (`FindAttemptReleaseAsync`, `IsConfirmed`), `:519-600` (`AdvanceAsync`), `:735-744`, `:900-1090` (observation and reservation), `:1133-1141` | Every production line S1-S4 edit, and the fences that must not move. |
| `BlockedTaskParkingService.cs:19-67`, `:73-110`, `:117-158`, `:193-201` | Registration, state CAS, legacy paging and cursor, allowed transitions. |
| `TaskParkPublicationService.cs:89-150`, `:286-319`, `:344-350` | Prepare, Verify, re-request and hold writers. |
| `AgentTaskDispatcher.cs:1758-1900`, `:7438-7444` | Sweep lifetime and the hook. |
| `RunnerSlotReconcileJob.cs`; `TerminalSeatReleaseObservation.cs:1-60`; `TerminalSeatRelease.cs` (contracts) | Job arithmetic; what the runner measures and reports. |
| Tests: `BlockedTaskParkReclaimTests.cs` (entire), `RunnerSeatReleaseFixture.cs:86-135`, `:160-232`, `:296-345`, `:481-495`, `:866-960`, `BlockedTaskParkProjectionTests.cs:30-95`, `TerminalRunnerSeatReleaseTests.cs:128-171`, `:1020-1040`, `RunnerSlotEndpointTests.cs:150-210` | Fixture seams, the arms to re-express, the pins, the direct `TryReserveAsync` caller and the job assertions. |

### Proves it works now

Every V names its negative-control arm; a V is not done while any arm is missing.

| V | Behaviour | Test (class-qualified) |
|---|---|---|
| V-1 | Legacy window from the server clock. One legacy episode on a clean pushed Code task (30 h old block event and `CompletedAt`), five arms in order: (a) discovered now, runner reports `StableFor` 30 h and `FirstObservedAt` 30 h ago, server elapsed 0: held, session Running. (b) clock advanced 60 s, runner clock 10 h ahead (`FirstObservedAt = CreatedAt + 10 h`, `StableFor` 120.001 s): held, although the old rule releases. (c) clock advanced to 119.999 s, runner clock 10 h behind (`FirstObservedAt = CreatedAt - 10 h`): held. (d) clock advanced to 120.001 s, runner still 10 h behind: released with one version-2 request, zero force commands; the old rule would never release this arm. (e) negative control at the same clock on a second seeded row with `StableFor` 119.999 s: held (the duration gate at `:1013` still applies). | `BlockedTaskParkReclaimTests.C1108_LegacyWindowIsServerAnchored` |
| V-2 | Gated, bounded, measured. Five Blocked rows; `ReclaimScheduledAsync` with the production arguments: `Visited == 5`, `Eligible == 5`, `Cap == 5`, `Registered == 5`, `Released == 0`, five episodes, one each. Second call on the same clock: gated zero record, no new visit, cursor unchanged. Clock advanced 120 s: runs again, `Visited == 5` and still one episode per row. Gate held by the test through the singleton: zero record. Negative controls: the ungated `ReclaimLegacyAsync(2, 3)` on eight rows still visits exactly 6 (V-23 unchanged), and `ReclaimIntervalSeconds = 0` sweeps on every call. | `BlockedTaskParkReclaimTests.C1108_ScheduledSweepIsGatedAndBoundedPerRun` |
| V-3 | Job counts releases only. Reclaim on, one Blocked task, no idle observation: `JobAsync()` returns 0 while one legacy episode exists, the sweep record shows `Visited == 1`, `Released == 0`. Positive count: V-23's releasing run records `Released` equal to the number of ledger rows `IsConfirmed` after that run (2). | `BlockedTaskParkReclaimTests.C1108_ReconcileJobCountsOnlyReleases`; `BlockedTaskParkReclaimTests.C1065_LegacySweepRequiresFreshPublicationAndIdleWindow` (`Released` assertion) |
| V-4 | Dispatcher hook. Reclaim on: `ReleaseUnownedPoolDelegatesAsync` registers one legacy episode, `LegacySweptAt == now`, `LastLegacyReclaim.Visited == 1`; a second call inside the interval leaves both unchanged and the episode count at 1. Negative controls, separate fixtures: `Enabled` on with `ReclaimExisting` off, and both off: zero episodes, `LegacySweptAt` null, no `BlockedTaskParkReclaimCursors` row. | `BlockedTaskParkReclaimTests.C1108_DispatcherHookRunsTheGatedSweep` |
| V-5 | Held backoff. Rows: dirty source, clean pushed source whose observation stays below 120 s (Published, waiting), and a row whose reservation key the test admits through `IWorkspaceReservationJournal` before the first run. Run 1: dirty is Held `park_dirty` with `NextAttemptAt == now + 600 s` and revision r; the published row is observed once; the admitted row is Held `park_workspace_reserved` with `NextAttemptAt` null. Run 2 (same clock, consumer released): dirty revision still r and a direct `TryHandleTaskAsync` on it changes nothing; the published row is observed a second time; the reserved row is re-requested (revision bumped, reason no longer `park_workspace_reserved`). Clock advanced 600 s, run 3: dirty re-inspected (revision bumped, still `park_dirty`, new `NextAttemptAt`). Negative control in a second fixture with `reclaimHeldBackoffSeconds: 0`: run 2 re-inspects the dirty row. | `BlockedTaskParkReclaimTests.C1108_HeldEpisodesBackOffUntilNextAttempt` |
| V-6 | One verify per dispatch. The V-23 local-lane release (clean pushed tip, qualified observation, clock past the window) driven by `ReclaimAsync` with no boundary: `VerifyCalls == 2` for the dispatched release (publication gate, then `AdvanceAsync`), one version-2 request, ledger `IsConfirmed`. Negative control in a second fixture with a no-op `BoundaryAsync`: `VerifyCalls == 3` and the release still completes. Amendment 2026-10-07 (CARD-1141): the code measures 3 verifies without a boundary and 4 with one, because `RegisterAndReserveAsync` verifies at the entry gate and again after `BeforeReservation`; the committed pins are 3 and 4, and PC-11's red is `VerifyCalls` 4 against the pin 3. | `BlockedTaskParkReclaimTests.C1108_AdvanceVerifiesSourceOnceWithoutBoundary` |

### Guards the regression

| R | Negative control | Test |
|---|---|---|
| R-1 | S9 behaviour: fresh window at the boundary, publication and binding holds, cursor restart, cold resume, and every list/reserve/send invalidation. | whole `BlockedTaskParkReclaimTests` (existing V-23 with its arms re-expressed in S1, V-24 unchanged) |
| R-2 | The seat-release ledger, discovery, `BeforeDispatch` cuts and the direct `TryReserveAsync` call are unchanged. | whole `TerminalRunnerSeatReleaseTests` |
| R-3 | Fast-path park release, sync debt, resume and continuation delivery are unchanged. | whole `BlockedTaskParkReleaseTests`, `BlockedTaskSyncRecoveryTests`, `BlockedTaskParkResumeTests`, `BlockedTaskParkDeliveryTests` |
| R-4 | Publication policy, identity capture and the CAS are unchanged by the hold backoff and the verify seam. | whole `TaskParkPublicationTests`, `TaskParkRunnerIdentityTests` |
| R-5 | The owner docs still carry every CARD-1065 sentence, with the four rewritten ones. | whole `BlockedTaskParkProjectionTests` |
| R-6 | Sweep lifetime, budget, abandonment and the singleton registration are unchanged. | whole `DispatcherSweepLifetimeTests`, `DispatcherSweepLifetimeRegistrationTests` |
| R-7 | The job still finishes pending slot intents and returns their count without the seat service. | whole `RunnerSlotEndpointTests`, `PhoneHomeDeferredKillTests`, `RunnerSlotRulesTests` |
| R-8 | The pool-release sweep that hosts the hook behaves as before for pool delegates. | whole `RunnerSeatOrphanSweepTests` |

### Guard inventory

| G | Decision: guard | PC |
|---|---|---|
| G-1 | D-1: a legacy episode needs 120 s of server-clock time since `park.CreatedAt` | PC-1 |
| G-2 | D-1: the runner's `FirstObservedAt` never shortens or opens the window | PC-2 |
| G-3 | D-2: the scheduled sweep runs at most once per interval across both callers | PC-3 |
| G-4 | D-3: a run visits each eligible Blocked row at most once | PC-4 |
| G-5 | D-2: a disabled sweep touches no gate state and runs no query | PC-5 |
| G-6 | D-3: the job's return excludes visit counts | PC-6 |
| G-7 | D-3: the sweep counts only confirmed ledger releases | PC-7 |
| G-8 | D-4: a Held park inside its backoff is not re-prepared by any caller | PC-8 |
| G-9 | D-4: `park_workspace_reserved` gets no backoff | PC-9 |
| G-10 | D-4: a Published candidate is never backed off | PC-10 |
| G-11 | D-6: `AdvanceAsync` verifies once when no boundary is installed | PC-11 |

### Positive controls

Mutation runs break/red/restore/green after land; Code runs ordinary V/R; Review judges
guard independence. Each row is one compiling production defect applied singly with
every other fact valid. Use the exact method filter, never the class. Zero executions, a
build or fixture failure, or a timeout is not red. Restore source and rebuild before the
green run. All eleven controls share `BlockedTaskParkReclaimTests`, and PC-1/PC-2,
PC-3/PC-4, PC-6/PC-7 and PC-8/PC-9/PC-10 share production files, so they run
sequentially, never batched.

| PC | Break guard by compiling defect | Exact detecting filter | Expected red assertion |
|---|---|---|---|
| PC-1 | G-1: `FreshLegacyWindow` drops the `observedAt - CreatedAt >= 120 s` term. | `/*/*/BlockedTaskParkReclaimTests/C1108_LegacyWindowIsServerAnchored` | `G-1`: arm (a) releases; `Released(...)` is true at server elapsed 0. |
| PC-2 | G-2: `FreshLegacyWindow` ORs in the old rule `first + StableFor >= CreatedAt + 120 s`. | `/*/*/BlockedTaskParkReclaimTests/C1108_LegacyWindowIsServerAnchored` | `G-2`: arm (b) releases at server elapsed 60 s. |
| PC-3 | G-3: `ReclaimScheduledAsync` ignores `NextLegacySweepAt`. | `/*/*/BlockedTaskParkReclaimTests/C1108_ScheduledSweepIsGatedAndBoundedPerRun` | `G-3`: the second call on the same clock reports `Visited == 5`, not 0. |
| PC-4 | G-4: `Cap = pageSize * passBudget` without the eligible count. | `/*/*/BlockedTaskParkReclaimTests/C1108_ScheduledSweepIsGatedAndBoundedPerRun` | `G-4`: the first call reports `Visited == 15`, not 5. |
| PC-5 | G-5: `ReclaimScheduledAsync` stamps `LegacySweptAt` before the enabled check. | `/*/*/BlockedTaskParkReclaimTests/C1108_DispatcherHookRunsTheGatedSweep` | `G-5`: the `ReclaimExisting` off arm finds `LegacySweptAt` set. |
| PC-6 | G-6: the job adds `Visited` to its return. | `/*/*/BlockedTaskParkReclaimTests/C1108_ReconcileJobCountsOnlyReleases` | `G-6`: `JobAsync()` returns 1, not 0. |
| PC-7 | G-7: `ReclaimLegacyAsync` increments `Released` on every visit. | `/*/*/BlockedTaskParkReclaimTests/C1108_ReconcileJobCountsOnlyReleases` | `G-7`: the sweep record shows `Released == 1` with no confirmed ledger row. |
| PC-8 | G-8: `TryHandleTaskAsync` drops the `Held && NextAttemptAt > now` gate. | `/*/*/BlockedTaskParkReclaimTests/C1108_HeldEpisodesBackOffUntilNextAttempt` | `G-8`: the dirty park's revision changes on run 2. |
| PC-9 | G-9: `HoldAsync` stamps the backoff for `park_workspace_reserved` too. | `/*/*/BlockedTaskParkReclaimTests/C1108_HeldEpisodesBackOffUntilNextAttempt` | `G-9`: the reserved row keeps `park_workspace_reserved` and its revision on run 2. |
| PC-10 | G-10: the gate skips any park with `NextAttemptAt > now`, and the publication CAS stamps `NextAttemptAt` on `Published`. | `/*/*/BlockedTaskParkReclaimTests/C1108_HeldEpisodesBackOffUntilNextAttempt` | `G-10`: the published row's observe-call count does not increase on run 2. |
| PC-11 | G-11: `AdvanceAsync` restores the unconditional pre-boundary read. | `/*/*/BlockedTaskParkReclaimTests/C1108_AdvanceVerifiesSourceOnceWithoutBoundary` | `G-11`: `VerifyCalls == 3` in the no-boundary fixture. Amendment 2026-10-07 (CARD-1141): the code measures 3 verifies without a boundary and 4 with one, because `RegisterAndReserveAsync` verifies at the entry gate and again after `BeforeReservation`; the committed pins are 3 and 4, and PC-11's red is `VerifyCalls` 4 against the pin 3. |

Carried from CARD-1065 (D-7), to be executed by this card's Mutation at the landed SHA
with the filters recorded in that plan's `:1388-1398`: PC-172..PC-181 and PC-23/PC-24.
PC-171 is superseded by PC-1 and PC-2 above once S1 lands.

### Out of scope

- CARD-1097, CARD-1103 and CARD-1104 remain as recorded in the S9 and S10 reports.
- Discovery's per-tick cadence (`DiscoverScheduledAsync` has an overlap gate and no
  interval); D-4 bounds its re-preparation of Held parks, nothing else changes.
- A per-run deadline distinct from the dispatcher's sweep budget.
- Windows checkpoint rows: no runner or native path changes.

### Checkpoints

Exactly one isolated build and one filter per row; reuse rows name the build row of
the same `After` group. Counts are TUnit executed results. Existing rosters at
`3599aa4b5`: `BlockedTaskParkReclaimTests` 2, `TerminalRunnerSeatReleaseTests` 39 (29
methods), `BlockedTaskParkReleaseTests` 3, `BlockedTaskSyncRecoveryTests` 2,
`BlockedTaskParkResumeTests` 3, `BlockedTaskParkDeliveryTests` 4, `TaskParkPublicationTests`
3, `TaskParkRunnerIdentityTests` 2, `BlockedTaskParkProjectionTests` 2,
`DispatcherSweepLifetimeTests` 6 (4 methods), `DispatcherSweepLifetimeRegistrationTests` 1,
`RunnerSlotEndpointTests` 8, `PhoneHomeDeferredKillTests` 3, `RunnerSlotRulesTests` 2,
`RunnerSeatOrphanSweepTests` 17. The reclaim class grows by one method per slice except
S2, which adds three. Confirm the TRX roster equals the expected set, not merely at
least `Min`. The table was validated at planning time with the checkpoint importer
(`import --plan`, tool built through `scripts/build-slot.ps1` to `bin-c1108-driver/`,
output deleted afterwards): 29 rows imported, exit 0, no advisory warnings, no tests run.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1108-cp1/` | reclaim-s1 | `/*/*/BlockedTaskParkReclaimTests/*` | V-1, R-1 | exact 3 results (2 existing + 1 new), 0 failed/skipped | 3 | 6 | true |
| CP-2 | S1 | CP-1 | seat-s1 | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-2 | exact 39 results, 0 failed/skipped | 39 | 3 | true |
| CP-3 | S1 | CP-1 | park-s1 | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkResumeTests*)\|(BlockedTaskParkDeliveryTests*)/*` | R-3 | exact 12 results (3 + 2 + 3 + 4), 0 failed/skipped | 12 | 4 | true |
| CP-4 | S1 | CP-1 | publication-s1 | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)/*` | R-4, R-5 | exact 7 results (3 + 2 + 2), 0 failed/skipped | 7 | 2 | |
| CP-5 | S1 | CP-1 | lifetime-s1 | `/*/*/(DispatcherSweepLifetimeTests*)\|(DispatcherSweepLifetimeRegistrationTests*)/*` | R-6 | exact 7 results (6 + 1), 0 failed/skipped | 7 | 3 | true |
| CP-6 | S2 | `tests/Antiphon.Tests -> bin-c1108-cp6/` | reclaim-s2 | `/*/*/BlockedTaskParkReclaimTests/*` | V-2, V-3, V-4, R-1 | exact 6 results (3 + 3 new), 0 failed/skipped | 6 | 7 | true |
| CP-7 | S2 | CP-6 | seat-s2 | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-2 | exact 39 results, 0 failed/skipped | 39 | 3 | true |
| CP-8 | S2 | CP-6 | park-s2 | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkResumeTests*)\|(BlockedTaskParkDeliveryTests*)/*` | R-3 | exact 12 results, 0 failed/skipped | 12 | 4 | true |
| CP-9 | S2 | CP-6 | publication-s2 | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)/*` | R-4, R-5 | exact 7 results, 0 failed/skipped | 7 | 2 | |
| CP-10 | S2 | CP-6 | lifetime-s2 | `/*/*/(DispatcherSweepLifetimeTests*)\|(DispatcherSweepLifetimeRegistrationTests*)/*` | R-6 | exact 7 results, 0 failed/skipped | 7 | 3 | true |
| CP-11 | S2 | CP-6 | job-s2 | `/*/*/(RunnerSlotEndpointTests*)\|(PhoneHomeDeferredKillTests*)\|(RunnerSlotRulesTests*)/*` | R-7 | exact 13 results (8 + 3 + 2), 0 failed/skipped | 13 | 3 | true |
| CP-12 | S2 | CP-6 | orphan-s2 | `/*/*/RunnerSeatOrphanSweepTests/*` | R-8 | exact 17 results, 0 failed/skipped | 17 | 4 | true |
| CP-13 | S3 | `tests/Antiphon.Tests -> bin-c1108-cp13/` | reclaim-s3 | `/*/*/BlockedTaskParkReclaimTests/*` | V-5, R-1 | exact 7 results (6 + 1 new), 0 failed/skipped | 7 | 7 | true |
| CP-14 | S3 | CP-13 | seat-s3 | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-2 | exact 39 results, 0 failed/skipped | 39 | 3 | true |
| CP-15 | S3 | CP-13 | park-s3 | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkResumeTests*)\|(BlockedTaskParkDeliveryTests*)/*` | R-3 | exact 12 results, 0 failed/skipped | 12 | 4 | true |
| CP-16 | S3 | CP-13 | publication-s3 | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)/*` | R-4, R-5 | exact 7 results, 0 failed/skipped | 7 | 2 | |
| CP-17 | S3 | CP-13 | lifetime-s3 | `/*/*/(DispatcherSweepLifetimeTests*)\|(DispatcherSweepLifetimeRegistrationTests*)/*` | R-6 | exact 7 results, 0 failed/skipped | 7 | 3 | true |
| CP-18 | S4 | `tests/Antiphon.Tests -> bin-c1108-cp18/` | reclaim-s4 | `/*/*/BlockedTaskParkReclaimTests/*` | V-6, R-1 | exact 8 results (7 + 1 new), 0 failed/skipped | 8 | 7 | true |
| CP-19 | S4 | CP-18 | seat-s4 | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-2 | exact 39 results, 0 failed/skipped | 39 | 3 | true |
| CP-20 | S4 | CP-18 | park-s4 | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkResumeTests*)\|(BlockedTaskParkDeliveryTests*)/*` | R-3 | exact 12 results, 0 failed/skipped | 12 | 4 | true |
| CP-21 | S4 | CP-18 | publication-s4 | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)/*` | R-4, R-5 | exact 7 results, 0 failed/skipped | 7 | 2 | |
| CP-22 | S4 | CP-18 | lifetime-s4 | `/*/*/(DispatcherSweepLifetimeTests*)\|(DispatcherSweepLifetimeRegistrationTests*)/*` | R-6 | exact 7 results, 0 failed/skipped | 7 | 3 | true |
| CP-23 | all | `tests/Antiphon.Tests -> bin-c1108-final/` | reclaim-final | `/*/*/BlockedTaskParkReclaimTests/*` | V-1-V-6, R-1 | exact 8 results, 0 failed/skipped | 8 | 7 | true |
| CP-24 | all | CP-23 | seat-final | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-2 | exact 39 results, 0 failed/skipped | 39 | 3 | true |
| CP-25 | all | CP-23 | park-final | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkResumeTests*)\|(BlockedTaskParkDeliveryTests*)/*` | R-3 | exact 12 results, 0 failed/skipped | 12 | 4 | true |
| CP-26 | all | CP-23 | publication-final | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)/*` | R-4, R-5 | exact 7 results, 0 failed/skipped | 7 | 2 | |
| CP-27 | all | CP-23 | lifetime-final | `/*/*/(DispatcherSweepLifetimeTests*)\|(DispatcherSweepLifetimeRegistrationTests*)/*` | R-6 | exact 7 results, 0 failed/skipped | 7 | 3 | true |
| CP-28 | all | CP-23 | job-final | `/*/*/(RunnerSlotEndpointTests*)\|(PhoneHomeDeferredKillTests*)\|(RunnerSlotRulesTests*)/*` | R-7 | exact 13 results, 0 failed/skipped. Amendment 2026-10-07 (CARD-1141): Expect exact 15 (10 + 3 + 2) and Min 15, since CARD-1124 added two `RunnerSlotEndpointTests` methods before the S4 base. | 13 | 3 | true |
| CP-29 | all | CP-23 | orphan-final | `/*/*/RunnerSeatOrphanSweepTests/*` | R-8 | exact 17 results, 0 failed/skipped | 17 | 4 | true |

Run each group once its slice is committed, through the checkpoint tool:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1108-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1108-driver/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c1108-driver/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-06-card-1108-park-reclaim-follow-ups-plan.md --after S1 --expected-source-sha "$(git rev-parse HEAD)"
```

`--after S2`, `--after S3` and `--after S4` select the later groups; the S4 Code task
then runs `--rows CP-23,CP-24,CP-25,CP-26,CP-27,CP-28,CP-29` at its final committed SHA.
Continue `wait` while the exit is 75. Exit 4 is a slot timeout: report the row as not
run. Preserve every tool-produced `CHECKPOINT` line unedited. No unlisted build or test
loop; a failure-driven rerun is the same `CP-n` with its reason and commit. Code and
Review run `scripts/check-evidence-diff.ps1` over the task range. Delete only
producer-owned `bin-c1108-*` outputs; evidence stays ignored.

### Cost

Estimated, not measured: ordinary Code V/R floor = **109 minutes**, the sum of the
`EstimatedMinutes` column (S1 18, S2 26, S3 19, S4 19, final 27). Each building row
includes about 2 minutes of isolated build (S9 measured 111 s) and 40 s of host start.
Checkpoint-driver bootstrap allowance is **5 minutes**, not double-counted.

Slices stay inside 30-60 minutes: S1 34 + 18 = 52; S2 34 + 26 = 60; S3 36 + 19 = 55;
S4 26 + 19 = 45 plus the 27-minute final set, which is check time, not authoring.
Total author = **130 minutes**; Code floor with bootstrap = **244 minutes** across four
Code tasks. If a slice exceeds its bound, split it at the slice boundary before
starting the next one; do not widen a row.

## Mutation handoff

After land, dispatch a method-scoped SourceLanding Mutation for PC-1..PC-11 of this plan
plus CARD-1065 PC-172..PC-181, PC-23 and PC-24 (filters in that plan's `:1388-1398`),
all at the landed SHA. Every control selects one method of `BlockedTaskParkReclaimTests`
with `/*/*/BlockedTaskParkReclaimTests/<Method>`; none batches. Keep per-PC baseline,
red and restored-green evidence in the external evidence root. This is the closing of
item 5: the S9 guards are executed here, and PC-171 is replaced because its target line
no longer exists.

## Rollout and activation

Nothing activates. The three switches stay false by default and the two new settings
shape only an enabled sweep (`BlockedTaskParking:ReclaimIntervalSeconds` 120,
`BlockedTaskParking:ReclaimHeldBackoffSeconds` 600). When an operator enables
`Enabled` and `ReclaimExisting` with `AutomaticEnabled`, the sweep runs about once per
two minutes, visits each Blocked row once per run, re-inspects a Held row once per ten
minutes, and the `LegacyReclaim` log line gives the measured shape of each run. No
restart is required by this plan; the Code tasks restart nothing.

## Handoff

The brief folded test design into this dispatch and the verification design above is
complete, so the next stage is land, then Code S1 on `feat/card-task-8b366539`'s landed
successor with `checkpoints: docs/superpowers/plans/2026-10-06-card-1108-park-reclaim-follow-ups-plan.md@<plan commit sha> section "### Checkpoints"`,
`--after S1`.
