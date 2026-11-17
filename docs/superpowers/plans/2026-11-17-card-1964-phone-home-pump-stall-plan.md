# CARD-1964 Slice 1 plan: bound and watch phone-home lease re-recovery

Plan task 6652bca1 (Opus), 2026-11-17. This is a plan only. No code, setting, restart or host was touched.

- **Read at:** master `300c8a99d0d267a39791410b7540189ab702f251`. Every line citation below is at that SHA.
- **rc line:** `origin/rc/2026-11-24` tip `870a93d21f2b18c85a8b0905ab689b28e3099813` is the running desktop server. The seven
  files this slice touches or reads are byte-identical there (`git diff --stat` is empty).
- **Diagnosis:** `docs/investigations/2026-11-17-server2-phone-home-stale.md`, Debug e5d7e9da, commit
  `7e105faca3a5b938b89919f368489d4e7226cf9d` on `origin/feat/card-task-e5d7e9da`.
- **Scope:** fix 1 of the card only. That is a bounded deadline per recovery step with a visible failure log, a per-runner
  cycle watchdog, a log line for every lease expiry and every lease re-recovery refusal with its reason, and a lease
  re-recovery that requires only the List.

## Ground truth: what the card assumes vs what the code does

| # | Card / brief assumption | Code at 300c8a99d | Consequence for the plan |
|---|---|---|---|
| G-1 | The pump loop is serial (`PhoneHomeRecoveryPump.cs:61-77`). | True. `ExecuteAsync` awaits `RunCycleAsync`, which awaits `RunOneAsync` for every remote runner (`:108-117`, `Task.WhenAll` for several). One parked `RunOneAsync` parks every runner. | A step deadline must bound `RunOneAsync`. The watchdog cannot rely on the loop turning. |
| G-2 | "A fake runner whose List never answers: red at base, stays ineligible forever." | **False on paper.** The List reply is already bounded by the 60 s request timeout on the connection clock (CARD-0629, `PhoneHomeLiveConnection.cs:434-444`, `RequestTimeoutFor` default branch `:371`). `Silent_list_reply_leaves_the_runner_ineligible_until_catch_up_succeeds` (`PhoneHomeRecoveryEligibilityTests.cs:28`) proves that eligibility returns after a silent List. | A silent List reply fails red at base only on the new watchdog log line (V-4). A stall that never ends needs an await that sits outside the request timer. V-1, V-2 and V-9 use those awaits. |
| G-3 | The likeliest hang is the per-session gate (`SessionStateStore.cs:117`) reached from `:388`. | `CatchUpAsync` calls `runtime.PersistTranscriptAsync(session.SessionId, transcript.Entries)` with **no token** (`PhoneHomeRecoveryPump.cs:388`). `PersistTranscriptAsync(..., ct = default)` (`AgentSessionRuntime.cs:1351`) runs `BeginWriteAsync` and then `AcquireAsync`, and `entry.Gate.WaitAsync(ct)` (`SessionStateStore.cs:117`) waits forever while another holder keeps the gate. | Red witnesses V-1 and V-2 hold that gate from the test. |
| G-4 | Lease re-recovery re-reads the transcripts. | True. `recoveringLease` (`:146`) still runs the full `CatchUpAsync` (`:190`): the List, then the owner lookup, transcript fetch and persist for every owned listed session (`:380-397`). The observer group of a recovering lease is never stopped (`StartPump` only when `!recoveringLease`, `:201-206`). | List-only re-recovery (D-2) removes the gate exposure from the path where the incident happened. |
| G-5 | The lease checks require exactly 0 pending events and refuse silently. | True. `CanRecoverLeaseUnderLock` (`PhoneHomeRunnerDirectory.cs:432-442`) requires `PendingEvents == 0`. A refusal before catch-up returns false with no log (`PhoneHomeRecoveryPump.cs:179-180`). A refusal after catch-up also has no log and retries on the next 50 ms turn (`:196-197`). | Slice 1 logs the reason (L4). The threshold itself is fix 3. |
| G-6 | No log line for a lease expiry. | True. The only flip site is `ReadSnapshot` (`PhoneHomeRunnerDirectory.cs:984-994`). Its two callers, `SnapshotOf` (`:972-981`) and `GetLiveStoreId` (`:121-136`), call `Notify` after the lock and nothing else. Other expiry reads (`:234, :314, :438, :522, :561, :622, :684, :709, :834`) compute expiry without flipping. The pump calls `SnapshotLive` every 50 ms, so the flip happens within one turn of expiry. | L1 goes beside both `Notify(expired)` calls (`:134`, `:979`) through one helper, once per eligible-to-ineligible flip. `ReadSnapshot` also hands out the flipped connection for the log fields. |
| G-7 | Eligibility is cleared only at `:487` and `:984-994`. | True (`Disconnect` `:480-494` and `ReadSnapshot`). It is set only by `MarkRecovered` (`:400-419`) and `TryRecoverLease` (`:444-461`). | The fail-safe invariant I-1 holds by keeping those two as the only setters. Both are called only after a bounded await returned success. |
| G-8 | Catch-up transcripts fence eligibility. | False. One failed transcript is a Warning, not a fence (`:374-397`, CARD-0633, test `One_session_transcript_failure_is_a_warning_not_a_fence`). Eligibility rests on the List alone. | A step timeout follows the same rule (D-3). |
| G-9 | `_send.WaitAsync(ct)` (`PhoneHomeLiveConnection.cs:410`) is un-timed. | True, and the write itself (`:414`) is outside the 60 s reply timer too. | Fix 4 owns the in-file bound. Slice 1 covers it from outside with the List step budget plus a `WaitAsync` backstop, without editing that file. |
| G-10 | The exact hang site. | **UNOBSERVED.** No managed dump was taken (investigation, UNOBSERVED). | The design bounds every candidate await. L6 names the step the attempt was in, so the next incident records the site. |
| G-11 | A runner change is needed. | No. Everything here is server-side. Runner overflow already closes the connection (`PhoneHomeReconnectReason.cs:59-60`, `PhoneHomeProblemTypes.EventOverflow`), so a live connection with no recorded disconnect has lost no events. | There is no runner rollout, and the List-only argument in D-2 holds. |

## Decisions

- **D-1 How deadlines are applied.** Each recovery step gets a `CancellationTokenSource` timed on the **connection's clock**
  (`live.Clock`, so `FakeTimeProvider` drives it in tests) and linked to the attempt token. Every step await is also wrapped in
  `.WaitAsync(stepToken)`, so the loop gets control back even when the awaited code ignores the token. Two cases need that
  backstop: the post-cancel `lease.ReconcileAsync(false, CancellationToken.None)` in `AgentSessionRuntime.cs:1363`, and
  `_send` (G-9).
  - *Rejected:* token-only cancellation. It leaves the un-cancellable awaits above.
  - *Rejected:* timeouts inside `SessionStateStore`/`PhoneHomeLiveConnection`. That is fix 4, and it touches shared files
    (see collisions).
- **D-2 Lease re-recovery requires only the List.** `CatchUpAsync(live, listOnly: recoveringLease, …)` returns after a
  successful List.
  - *Why it is safe:* `CanRecoverLease` already requires the same `slot.Live`, the same epoch, an open socket and no
    recorded disconnect. The connection's observer group never stopped (G-4), and the runner closes the socket on overflow
    (G-11). So every transcript frame the runner sent during the expiry was delivered in order to the running observers.
    Re-fetching duplicates the stream, and it is the path that reaches the session gate (G-3).
  - *Rejected:* keeping the transcript pass with deadlines. Every expiry would pay N fetches plus persists under exactly
    the load that caused it.
- **D-3 A session step that times out skips that session.** The step is owner lookup, transcript fetch or persist, on a
  connect recovery. The pump logs it (L2), counts the session *unverified*, and continues with the next session. When the
  whole catch-up budget runs out, the remaining sessions are skipped (L3). The runner becomes eligible because the **List
  completed**.
  - *Why this fails safe:* dispatch and seat accounting read the List-derived inventory
    (`ReplaceKnownLiveSessions`, `PhoneHomeLiveConnection.cs:218`). A skipped session **stays in that inventory and is
    counted as an occupied seat**, so dispatch errs toward fewer free seats, never more. The skip leaves only the persisted
    transcript unverified. That same state exists today after a transcript failure (G-8), and the existing per-session
    pulls repair it (`AgentSessionRuntime.CatchUpTranscriptAsync`, `:963`). Receipt readiness re-pulls and persists for
    itself (`AgentSessionRuntime.ReceiptReadiness.cs:85-96`).
  - *Rejected:* fencing eligibility on every transcript. One wedged gate reproduces the 45-minute outage.
  - *Rejected:* retrying the whole catch-up. Same result.
- **D-4 The watchdog lives in `PhoneHomeRecoveryPump` as a second task beside the loop.** It only *signals*: it cancels the
  in-flight attempt token and sets a per-runner restart flag. The loop applies the restart on its next turn.
  - *Rejected:* restarting the whole BackgroundService. That drops every observer group and connection.
  - *Rejected:* detached per-runner cycles. They change the awaited `RunCycleAsync` contract that 58 test call sites in 9
    files use. The step and total budgets already cap how long one runner can delay another.
  - Per-runner independence is listed with the remaining work (E-6).
- **D-5 Log cadence.** L1 logs once per flip. L4 logs on the first refusal of a streak, on a change of stage or reason, then
  at most once per `RecoveryRefusalLogSeconds`; every refusal is counted in the line. L6 logs at most once per watchdog window
  per runner.
  - *Rejected:* one line per 50 ms refusal (20 lines/s).
  - *Rejected:* Debug level. The incident day's log is at Information.
- **D-6 Settings.** New settings go in `PhoneHomeRunnerSettings` with code defaults. Zero or less disables each bound (the
  rollback, as `DesktopCapabilitiesGraceSeconds` does). There is no `appsettings.json` change.
- **D-7 No public DTO, route or migration change.**
  - *Rejected for slice 1:* `lastListAtUtc`/`recoveryStep` on `PhoneHomeRunnerStatusDto`, a public DTO. See F-4.
- **D-8 Files the slice does not edit.** It does not edit `PhoneHomeLiveConnection.cs`, `SessionStateStore.cs` or
  `AgentSessionRuntime.cs`. The pump passes its step token into the existing optional `ct` of `PersistTranscriptAsync`.
- **D-9 The after-catch-up refusal keeps its retry timing.** `TryRecoverLease` returning false still retries on the next
  turn; it is only logged now.
  - *Rejected:* adding the 5 s `NextCatchUp`. It slows re-recovery under continuous output until fix 3 lands.
- **D-10 Test holds stay outside the budget.** `CatchUpHold`/`HoldCatchUp` wait on the *attempt* token (stopping token
  plus watchdog cancel, no timer), not on the timed budget. Existing hold-based tests therefore keep their semantics
  (`PhoneHomeEventPumpTests:81`, `PhoneHomeBackpressureTests.Bounds:241`, `MultiRunnerRecoveryTests:38`,
  `TurnEndGapHarness`, `PhoneHomeHostedRunner`).
- **D-11 The red commit compiles.** It carries the six settings properties (unused), a no-op `internal void
  RunWatchdogOnce()`, `internal int LastCatchUpUnverifiedSessions => 0`, the peer helper `SilentWhen`, and the tests. Red is
  by assertion or by the test's own 5 s wait on the cycle, never by a compile error.

## (a) Design

### Budgets and what each await gets

The attempt is one `RunOneAsync` catch-up for one connection. `attemptToken` is the stopping token linked to a watchdog
CTS, with no timer. `budgetToken` is `attemptToken` plus `CatchUpTimeoutSeconds` on `live.Clock`. Each step token is
`budgetToken` plus its own step budget on `live.Clock`. Every await below is `await X.WaitAsync(token)`.

| Await | Site today | Token today | Slice 1 bound | On timeout or cancel (stopping token not cancelled) |
|---|---|---|---|---|
| Test hold | `:351-353` | stopping | `attemptToken` (D-10) | Watchdog cancel only: the attempt returns false, and the next turn retries. |
| **List** (catch-up) | `:359`, then `ReadInventoryAsync` `:289` | stopping; the reply is 60 s on the clock but `_send` and the write are un-timed | step `CatchUpListTimeoutSeconds` (90) | The existing Warning (`:363-366`) with `{Code}` = `catch_up_list_budget` (or `catch_up_budget` / `watchdog_restart`). Returns false. `NextCatchUp` = now + `CatchUpRetrySeconds`. **Ineligible.** |
| **List** (inventory refresh) | `:251` | stopping | same step budget | The existing refresh Warning (`:264-266`) with `{Code}` = `refresh_list_budget`. The existing failure count and stale bound apply. `cycle.Refresh` completes, so the next refresh can start. |
| **Owner lookup** | `:383`, `GetBindingAsync` (DB) | stopping | step `CatchUpSessionTimeoutSeconds` (30), shared by one session's three steps | L2, step=`owner`. The session is skipped and counted unverified. The loop continues. |
| **Transcript fetch** | `:387` | stopping; the reply is 60 s on the clock | same session step | L2, step=`transcript.fetch`. Skipped, unverified, continue. |
| **Per-session gate + save** | `:388`, then `SessionStateStore.cs:117` and `PersistTranscriptCoreAsync` | **none** | same session step (token passed as `ct`, plus `WaitAsync`) | L2, step=`transcript.persist`. Skipped, unverified, continue. A save that committed before its cancel is reconciled by the runtime's existing post-cancel path, which the loop no longer waits on. |
| Whole catch-up | `:349-401` | none | `CatchUpTimeoutSeconds` (300) | If the List has not completed, this is the List row above. If it has, L3: the remaining sessions are skipped, the attempt returns **true** (List done), and the runner becomes eligible (D-3). |

The pump records the current step in the attempt as one of `hold`, `list`, `owner`, `transcript.fetch`,
`transcript.persist` or `refresh`. L6 and the tests read it.

### Fail-safe invariants

The tests below pin each of these.

- **I-1** Eligibility becomes true only through `MarkRecovered` or `TryRecoverLease`. Each is called only after a List
  completed within the current attempt's budget, on the same connection and epoch. A timeout, a cancel or a watchdog
  restart never sets eligibility (V-7).
- **I-2** An abandoned (timed-out) task holds no path to `MarkRecovered`, `TryRecoverLease` or `StartPump`. Those calls
  stay in `RunOneAsync` after the bounded await. An abandoned `CatchUpAsync` can at most touch the inventory through a late
  List. That is harmless because `ReplaceKnownLiveSessions` is stamp-ordered (`PhoneHomeLiveConnection.cs:211-245`), and a
  timed-out request's waiter is already removed (`PhoneHomeLiveConnection.cs:441`), so its late reply is dropped (V-7).
- **I-3** A List that timed out never replaces the inventory. Inventory replacement happens only when `ListAsync` returns.
- **I-4** A skipped session is never removed from the inventory because of a timeout, so its seat stays counted (asserted
  in V-2).
- **I-5** Lease re-recovery still requires `CanRecoverLease` before and `TryRecoverLease` after the List, including
  `PendingEvents == 0`. Slice 1 only shortens the race window, from List plus all transcripts down to the List alone.

### Watchdog

- **Where it runs.** `PhoneHomeRecoveryPump.ExecuteAsync` starts `WatchdogLoopAsync(stoppingToken)` when
  `RecoveryWatchdogIntervals > 0`. The loop runs `RunWatchdogOnce()` every `RecoveryWatchdogPollSeconds` of real time, and
  the `finally` at `:79-89` joins it. Each decision reads the connection's own `live.Clock`, so tests call
  `RunWatchdogOnce()` directly with a fake clock.
- **Window.** `RecoveryWatchdogIntervals × (InventoryRefreshSeconds > 0 ? InventoryRefreshSeconds : 30)`. With the defaults
  that is 6 × 30 = 180 s.
- **Triggers.** These apply per runner, for a current, socket-open connection.
  - `no_list`: no List completed on this connection (catch-up or refresh) within the window. The window counts from the
    later of the last successful List and the time the pump first saw the connection. `ConnectionState.LastListAt` is set
    in `ReadInventoryAsync` on success.
  - `attempt_overrun`: an attempt has been in flight longer than `CatchUpTimeoutSeconds + CatchUpListTimeoutSeconds`
    (390 s). Once the budgets are in place this happens only if something outside them parks the loop.
- **Action.** "Restart the cycle without dropping the connection" means:
  - log L6 (once per window);
  - cancel the in-flight attempt's watchdog CTS (thread-safe);
  - set `cycle.RestartRequested`.

  On its next turn for that runner, the loop then:
  - clears `NextCatchUp`, so it re-attempts at once;
  - detaches a `Refresh` task that has not completed, observing its fault at Debug, so a new refresh can start;
  - resets the refusal log timer.

  It **never** touches `cycle.Pump`, `PumpStop` or `Recovered`, the socket, `Disconnect`, or `DispatchEligible`.

### Settings

All of these go in `PhoneHomeRunnerSettings` (`server/Application/Settings/PhoneHomeRunnerSettings.cs`). Each is an int,
and a value of zero or less disables it.

| Setting | Default | Reason for the default |
|---|---|---|
| `CatchUpListTimeoutSeconds` | 90 | The List reply timer is 60 s. The extra 30 s covers the send-gate wait and the write, which are outside it. |
| `CatchUpSessionTimeoutSeconds` | 30 | Observed slow phases under the incident load were 1.0-3.5 s (output.activity max 3.0 s, output.publish 3.5 s, transcript.persist 1.03 s). 30 s is about 8x the worst. Transcript fetch p95 is UNOBSERVED (F-1). |
| `CatchUpTimeoutSeconds` | 300 | One List (90) plus about 7 slow sessions at 30 s. Capacity is 10. Most sessions take seconds. |
| `RecoveryWatchdogIntervals` | 6 | 180 s with the 30 s refresh: two full List budgets plus retries before an Error. |
| `RecoveryWatchdogPollSeconds` | 10 | Cheap in-memory checks. |
| `RecoveryRefusalLogSeconds` | 60 | Matches `DesktopCapabilitiesFailureLogSeconds` (60). |

The lease length (90 s) and the heartbeat interval (15 s) are **unchanged**.

### Log lines

All lines are structured templates. Cardinality is bounded: `RunnerId` comes from configured runners, `Epoch` is an int,
every `{Step}`/`{Reason}`/`{Stage}`/`{Trigger}`/`{Code}` is from a fixed set, and `SessionId` is at most the listed
sessions (no more than capacity). The lines carry no payload, ticket, shared secret, transcript text or runner error
detail. Only `ProblemCode` codes appear, as today.

- **L1** (Warning, `PhoneHomeRunnerDirectory`, after the lock in both `ReadSnapshot` callers, `SnapshotOf` and `GetLiveStoreId`, once per eligible-to-ineligible flip):
  `Phone-home runner {RunnerId} epoch {Epoch} lease expired: no heartbeat for {SinceHeartbeatSeconds}s (lease {LeaseSeconds}s, socket {SocketState}, pendingEvents {PendingEvents}); dispatch-ineligible until lease re-recovery`
- **L2** (Warning, pump):
  `Phone-home catch-up step {Step} for session {SessionId} on runner {RunnerId} epoch {Epoch} exceeded its {BudgetSeconds}s budget; the session is skipped and counted unverified ({Unverified} so far in this catch-up)`.
  `{Step}` is one of `owner`, `transcript.fetch`, `transcript.persist`.
- **L3** (Warning, pump):
  `Phone-home catch-up for runner {RunnerId} epoch {Epoch} exhausted its {BudgetSeconds}s budget after {Completed} of {Listed} listed sessions; {Unverified} sessions counted unverified; eligibility rests on the completed List`
- **L-List** reuses the existing `:363-366` and `:264-266` templates with the new `{Code}` values `catch_up_list_budget`,
  `catch_up_budget`, `watchdog_restart` and `refresh_list_budget`. This keeps current log searches working.
- **L4** (Warning, pump, cadence D-5):
  `Phone-home lease re-recovery for runner {RunnerId} epoch {Epoch} refused {Stage}: {Reason} (pendingEvents {PendingEvents}; {Refusals} refusals since {FirstRefusedAt:O})`.
  - `{Stage}` is `before_catch_up` or `after_catch_up`.
  - `{Reason}` is one of `replaced`, `epoch_changed`, `disabled`, `socket_closed`, `lease_expired`, `disconnected`,
    `pending_events`, `draining`, `retired`.
  - This needs a new `internal string? LeaseRecoveryRefusal(PhoneHomeLiveConnection)` in the directory. It is the reason
    form of `CanRecoverLeaseUnderLock`, which keeps its boolean shape. `TryRecoverLease` gains an `out string? refusal`
    overload.
- **L5** (Information, pump, every successful lease re-recovery):
  `Phone-home runner {RunnerId} epoch {Epoch} re-recovered its lease after {IneligibleSeconds}s (List {ListMs} ms, {Refusals} refusals); dispatch-eligible again`
- **L6** (Error, pump, watchdog, once per window per runner):
  `Phone-home recovery watchdog: runner {RunnerId} epoch {Epoch} {Trigger} (no List for {SinceListSeconds}s, attempt step {AttemptStep} running {AttemptSeconds}s, limit {LimitSeconds}s, eligible {DispatchEligible}, last refusal {LastRefusal}, pendingEvents {PendingEvents}); restarting its recovery cycle on the same connection`.
  - `{Trigger}` is `no_list` or `attempt_overrun`.
  - `{AttemptStep}` is `none` or one of the step names.

The refusal line covers lease re-recovery refusals only. A `Resolve` refusal ("has not completed recovery",
`PhoneHomeRunnerDirectory.cs:163-164`) is already logged by its callers (RunnerUnavailable holds, StopFailed). L1 and L4
now give those caller lines a cause.

## Verification design

The brief folds the red witnesses into this plan, so Build can execute after the plan review.

**New class:** `tests/Antiphon.Tests/Application/C1964RecoveryPumpStallTests.cs`, tagged `[Category("Integration")]`, not
Slow, `[Timeout(30_000)]` per test. It follows the `PhoneHomeRecoveryEligibilityTests` patterns:

- `PhoneHomeTestHost` plus `PhoneHomeScriptedPeer` plus `FakeTimeProvider`;
- the DB world from `One_session_transcript_failure_is_a_warning_not_a_fence`
  (`TestDbFixture.CreateIsolatedSchemaAsync`, `BridgeQueueHarness`, `SeedAsync`);
- pump logs through `RecordingLogger<PhoneHomeRecoveryPump>`;
- directory logs through `host.Logs` (`CapturingLoggerProvider`, wired to the directory at `PhoneHomeTestHost.cs:119`).

The gate is held through `h.Provider.GetRequiredService<SessionStateStore>().BeginWriteAsync(id, ct)`, which is
internal. The test knows the pump has reached the gate through `SessionStateStore.NextGateWait` (`:97`, fired at `:116`), and it
releases the gate in `finally`. Before moving the fake clock past a request timer, the test waits for the request
(`peer.WaitForAsync`) and then 100 ms, as `PhoneHomeRecoveryEligibilityTests.cs:40-43` does.

**Test helper change:** add `PhoneHomeScriptedPeer.SilentWhen(Func<PhoneHomeFrame,bool>)` next to `SilentFor`
(`PhoneHomeTestHost.cs:414`), so one session's Transcript can stay silent.

Every log assertion also checks that no captured message contains `host.Secret` or the seeded transcript text.

| V | Method | Asserts | Red at base, proven from the code |
|---|---|---|---|
| V-1 | `Lease_rerecovery_requires_only_the_list_while_a_session_gate_is_held` | Setup: DB world. One owned session S is listed with a transcript. Initial cycle leaves the runner eligible. Then: hold S's gate, expire (advance 91 s), heartbeat. **Green:** the cycle returns true within 5 s, the runner is eligible, the Transcript request count is unchanged since the initial recovery, and there is one L5. | `recoveringLease` runs the full `CatchUpAsync` (G-4). The S fetch answers, then `:388` waits on S's gate with no token (G-3). `cycle.WaitAsync(5 s)` throws `TimeoutException`. This is the incident's likeliest mechanism. |
| V-2 | `Connect_catch_up_skips_a_session_whose_gate_never_opens` | Setup: DB world, new connection. Sessions are listed B (gate held) then G. After `NextGateWait` fires for B, advance `CatchUpSessionTimeoutSeconds`+1. **Green:** cycle true, eligible, G's entry persisted, one L2 naming B with step `transcript.persist`, `LastCatchUpUnverifiedSessions == 1`, and B still in the connection's known-live inventory (I-4). | Same un-timed gate wait at `:388`. Moving the clock forward does not matter at base, so the cycle never completes and the test times out. |
| V-3 | `Connect_catch_up_bounds_a_transcript_fetch_that_never_answers` | Setup: DB world. Sessions B (Transcript silent via `SilentWhen`) then G. After B's request arrives, advance 31 s. **Green:** cycle true, eligible, G persisted, one L2 naming B with step `transcript.fetch`. | At base the only bound on B's fetch is `RequestTimeoutFor(Transcript)` = 60 s (`PhoneHomeLiveConnection.cs:371`). At +31 s it has not fired, so G is never requested and the cycle does not complete within 5 s. |
| V-4 | `Silent_list_trips_the_watchdog_once_and_recovery_follows_the_answer` | Setup: no DB, refresh 0, window 180 s. Expire, heartbeat, `SilentFor(List)`. One attempt times out (existing Warning). Advance the clock past 180 s since the last List, with a heartbeat to keep the lease fresh. Then call `RunWatchdogOnce` twice. **Green:** exactly one L6 with `no_list` and limit 180. Then `Speak(List)`, advance the retry, heartbeat; the cycle returns true, the runner is eligible, and there is one L5. | The red is the L6 assertion: the base seam is a no-op, and no such line exists anywhere. Eligibility after the answer is already green at base (G-2), and the test keeps it as a guard. |
| V-5 | `Lease_expiry_logs_once_per_flip` | No DB. Recover, advance 91 s. Call `GetLiveStoreId` once, `SnapshotLive` 3 times and `Status` once. **Green:** exactly one L1, with `LeaseSeconds` 90, the runner and the epoch. Then heartbeat, recover, expire again: two L1 lines in total. | `SnapshotOf` (`:972-981`) and `GetLiveStoreId` (`:121-136`) call only `Notify` and have no logger call, so there are 0 lines. |
| V-6 | `Lease_recovery_refusal_logs_its_reason` with `[Arguments("before")]` and `[Arguments("after")]` | **before:** `EndPumpForTest`, emit one event, and hold it (PendingEvents 1, as `Lease.cs:120-125` does). Run the cycle 3 times: one L4 (`before_catch_up`, `pending_events`, Refusals 3 at the next line). Advance 61 s with a heartbeat: a second L4. Release the event and run a cycle: one L5 with its refusal count. **after:** silent List, apply draining, reply. **Green:** one L4 (`after_catch_up`, `draining`). | The refusals return false at `:179-180` and `:196-197` with no log, so there are 0 lines. |
| V-7 | `Timed_out_list_never_makes_the_runner_eligible` with `[Arguments("connect")]` and `[Arguments("lease")]` | Silent List. Advance past the List budget with a heartbeat. The cycle returns false. The peer then emits the late Result for that request id. **Assert:** `DispatchEligible` is false; `Directory.Resolve(id)` throws "has not completed recovery"; a cycle before the retry delay returns false; the List count is 1 (connect) or 2 (lease). | **Guard. Green at base and must stay green** (I-1, I-2, I-3). It kills the mutants "a step timeout counts as success" and "an abandoned attempt flips eligibility". It is the fail-safe negative. |
| V-8 | `Catch_up_total_budget_skips_the_remaining_sessions` | Setup: DB world, `CatchUpTimeoutSeconds`=45. Sessions B1 and B2 are fetch-silent, then G. Advance 31 s (B1 gets L2), then 15 s. **Green:** cycle true, eligible, one L3 with "2 of 3 listed" and Unverified 3, and G is never requested. | At base B1 waits for the 60 s fetch timer. +46 s < 60 s, so the cycle does not complete. |
| V-9 | `Watchdog_cancels_an_attempt_that_outlives_its_budgets` | No DB. `HoldCatchUp(runner, hold)`, then run a cycle. Advance 391 s with a heartbeat, then call `RunWatchdogOnce`. **Green:** one L6 with `attempt_overrun` and step `hold`, and the cycle returns false within 5 s with the runner ineligible. Release the hold, heartbeat, run a cycle: true. | The base seam is a no-op, and the hold waits on the stopping token (`:353`), so the cycle never returns and the L6 line is absent. |

That is 9 methods and 11 cases. At base, 9 cases are red: V-1, V-2, V-3, V-4, V-5, both V-6 cases, V-8 and V-9. The 2
V-7 cases are green guards.

Each new log line has a witness:

| Line | Witness |
|---|---|
| L1 | V-5 |
| L2 | V-2, V-3 |
| L3 | V-8 |
| L4 | V-6 |
| L5 | V-1, V-4, V-6 |
| L6 | V-4 (`no_list`), V-9 (`attempt_overrun`) |
| L-List codes | V-4 and V-7 assert `{Code}` = `catch_up_list_budget` when the clock passes 90 s before the 60 s reply timer is relevant. Code chooses the order in which the clock is advanced, so the budget fires first. |

## Existing tests affected

The CARD-1782 impacted-classes table names no other classes. These are the classes the change can affect:

| Class (files) | Why it is affected | Expected change |
|---|---|---|
| `PhoneHomeRecoveryEligibilityTests` (+ `.Lease.cs`) | Lease re-recovery becomes List-only, and the List gets a budget. | None. The lease tests assert only List counts (`Lease.cs:41, 78, 80, 106`). Code must re-run all 13 cases. |
| `MultiRunnerRecoveryTests` | It uses `HoldCatchUp`, and `:143` counts Transcript after a **reconnect**. | None. A reconnect is a new connection, so the full catch-up still runs. Holds stay outside the budget (D-10). |
| `PhoneHomeEventPumpTests`, `PhoneHomeBackpressureTests` | They use `CatchUpHold`. | None (D-10). |
| `PhoneHomeDirectoryTests`, `PhoneHomeRunnerRetirement*Tests`, `PhoneHomeImmediateSendTests`, `PhoneHomeRollingRunnerTests` | They advance past the lease, so L1 now fires. | Only if a test asserts an empty directory log. Code must run them (CP-4, CP-5). |
| `DerivedImpactIndexTests` | The new test class adds to `tests/derived-impact-index.json`. | Regenerate with `scripts/regenerate-derived-impact-index.ps1` and commit the index (CP-7). |

There are no migrations, public DTOs, routes, operation labels, remote script functions or statement budgets. The slice
does not change any session that waits for input, so the CARD-1083 release checklist does not apply.

### Checkpoints

Lane: Linux, server2. That is the `globalRunnerId` from `GET /api/runner-defaults` (revision 2), and
`GET /api/session-runners` shows server2 linux available and eligible at 04:39Z. The rows run on the task's own runner;
there is no `-Runner` or `-Platform` pin, because the classes are portable.

CP-1 and CP-2 each carry a cold build of `tests/Antiphon.Tests`. Both get estimate 15 (a 45-minute deadline, the CARD-1958
lesson): the green group cannot reuse the red build.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | red | tests/Antiphon.Tests -> bin-c1964-red/ | stall-red | `/*/*/C1964RecoveryPumpStallTests/*` | V-1,V-2,V-3,V-4,V-5,V-6,V-7,V-8,V-9 | 9 intended failures (V-1,V-2,V-3,V-4,V-5,V-6 x2,V-8,V-9) and 2 passed (V-7 x2) before implementation | 11 | 15 |
| CP-2 | green | tests/Antiphon.Tests -> bin-c1964-green/ | recovery | `/*/*/(C1964RecoveryPumpStallTests*)\|(PhoneHomeRecoveryEligibilityTests*)\|(MultiRunnerRecoveryTests*)\|(PhoneHomeEventPumpTests*)/*` | V-1..V-9,R-1 | all listed, 0 failed | 40 | 15 |
| CP-3 | green | CP-2 | recovery-adjacent | `/*/*/(PhoneHomeDeferredKillTests*)\|(PhoneHomeStrandedQueueTests*)\|(PhoneHomeTranscriptIncidentTests*)\|(DefaultRunnerEligibilityTests*)/*` | R-1 | all listed, 0 failed | 27 | 4 |
| CP-4 | green | CP-2 | directory-lease | `/*/*/(PhoneHomeDirectoryTests*)\|(PhoneHomeRunnerRetirementIdentityTests*)\|(PhoneHomeRunnerRetirementCycleTests*)\|(PhoneHomeImmediateSendTests*)/*` | R-2 | all listed, 0 failed | 29 | 4 |
| CP-5 | green | CP-2 | backpressure-rolling | `/*/*/(PhoneHomeBackpressureTests*)\|(PhoneHomeRollingRunnerTests*)/*` | R-1,R-2 | all listed, 0 failed | 42 | 6 |
| CP-6 | green | CP-2 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-3 | all listed, 0 failed | 60 | 6 |
| CP-7 | green | CP-2 | impact-index | `/*/*/DerivedImpactIndexTests/*` | R-3 | all listed, 0 failed, after the regenerate script ran and the index was committed | 42 | 12 |

**Import check (2026-11-17, plan task):** `dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll import --plan <this plan> --out .antiphon/c1964-plan/manifest-1.yaml` (ignored path). It printed `imported 7 rows`, exit 0, with **no warnings**. The row timeouts it derived are 45, 45, 15, 15, 18, 18 and 36 minutes.

Row meanings:

- **R-1:** the recovery, hold and pump regressions.
- **R-2:** the classes that advance past the lease and now emit L1.
- **R-3:** the guard batch from `docs/testing-and-build.md` line 323, and the derived impact index.

The roster has no suffix collisions. Each operand prefix matches exactly one class:

- `PhoneHomeRollingRunnerTests*` covers its `…RetireTests.cs` partial file, and no other class starts with that name.
- `PhoneHomeRecoveryEligibilityTests*` is one partial class.
- `PhoneHomeRunnerRetirementIdentityTests` and `PhoneHomeRunnerRetirementCycleTests` are distinct full names.

Inherited reds are to be confirmed at base before anyone is blamed (git-common). Two are known candidates on Linux:

- `NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute` (legacy digest red, memory);
- `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`, which needs the driver built first.

## Performance budget

- **Selection:** the 7 rows above. These are the only builds: `tests/Antiphon.Tests` twice, the red group and the green
  group. The test project graph covers server and Contracts.
- **Measurements:** per-test p95 for the new class is **UNOBSERVED**, so every number here is provisional.
  - The new tests reuse the existing `PhoneHomeTestHost` per-test pattern: an in-process WebApplication and a socket, with
    no AppHost and no container per test. Per-test isolation is required because each test owns one socket and one fake
    clock (as `LeaseWorld` notes).
  - The DB tests (V-1, V-2, V-3, V-8) use `TestDbFixture`'s **shared** assembly Postgres and an isolated schema each, the
    same as `One_session_transcript_failure…`.
- **Expected durations:**
  - green cases about 1-4 s each;
  - red-at-base cases are bounded by their own 5 s cycle wait, so at most about 10 s each;
  - the class takes about 1-2 min;
  - every test has `[Timeout(30_000)]`, the existing lease-test convention.
- **Code's job:** record the per-test durations from the CP-1 and CP-2 TRX, and replace the provisional timeouts with about
  2x the measured p95 if the p95 is under 15 s.
- **Phase budgets per row:**
  - cold build about 10-22 min on loaded server2 (memory: 1336 s at load 34-62), inside the 45-minute deadline from
    estimate 15;
  - CP-3 to CP-6 about 1-5 min each;
  - CP-7 above 8 min on loaded server2 (memory), so estimate 12.
- **Slot waits** are reported separately (`BUILD SLOT` lines). No row is expected near 30 min. Any row or run over 30
  minutes gets a Debug task with its timing evidence.
- **External calls:** none. The peer is in-process. Each fake-clock wait is bounded by the test's 5 s real-time waits.
- **Artifacts:** checkpoint run output under the ignored `.antiphon/` and checkpoint directories, as usual.

## (d) Files, must-not-touch, collisions, rollout, Review/Merge

**Files to change** (5 production/test files plus 1 new):

- `server/Application/Services/PhoneHomeRecoveryPump.cs`: the attempt and step budgets, List-only lease re-recovery,
  refusal streaks, L2-L6, the watchdog loop and `RunWatchdogOnce`, `LastCatchUpUnverifiedSessions`, and
  `ConnectionState.LastListAt`.
- `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs`: L1 in both `ReadSnapshot` callers, `LeaseRecoveryRefusal`,
  and the `TryRecoverLease(live, out refusal)` overload. `CanRecoverLease` keeps its signature.
- `server/Application/Settings/PhoneHomeRunnerSettings.cs`: the 6 settings.
- `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs`: `PhoneHomeScriptedPeer.SilentWhen`.
- **new** `tests/Antiphon.Tests/Application/C1964RecoveryPumpStallTests.cs`.
- `tests/derived-impact-index.json`: regenerated, not hand-edited.

**Must not touch:**

- any runner code (`src/Antiphon.SessionRunner/**`, `src/Antiphon.SessionRunner.Contracts/**`);
- `PhoneHomeContracts.cs` `DefaultLeaseSeconds` (90) and the heartbeat interval (15 s);
- `PhoneHomeLiveConnection.cs` (fixes 2 and 4; also next to CARD-1960);
- `SessionStateStore.cs` and `AgentSessionRuntime.cs` (fix 4 and fix 5);
- `PhoneHomeRunnerStatusDto` and any route (D-7);
- `appsettings*.json`, migrations, docs other than this plan;
- `CanRecoverLeaseUnderLock`'s `PendingEvents == 0` rule (fix 3).

**Collisions:**

- **CARD-1960** (InProgress, the quarantine release route) touches `SessionRunnerEndpoints.cs`, `scripts/workspace-slots.ps1`
  and the docs. It may read the `WorkspaceSlotQuarantineRelease` arm of `RequestTimeoutFor` in `PhoneHomeLiveConnection.cs`.
  This slice edits **none** of those files, so there are **no shared files** and the two can land in either order.
  - If a later reviewer pushes fix 4 into this slice, `PhoneHomeLiveConnection.cs` becomes shared. Land CARD-1960 first and
    then rebase.
- A scan of the 60 most recent `origin/feat/*` branches found none touching the pump, directory, settings, live connection,
  test host or eligibility test files against their merge base.

**Rollout:**

- The change is server-only and needs **no runner change**.
- Product hosts track the rc tip, never master (AGENTS.md). So the fix lands on master, then is ported to `rc/2026-11-24`
  as a reviewed cherry-pick. That is a bug-fix rc port (`docs/orchestration-loop.md` §9 item 9), which makes a new rc
  **tip** on the existing line, not a new rc line.
  - The pick should apply cleanly, because the touched files are identical (see header).
  - Drop the plan doc from the pick: the rc carries no `docs/superpowers/plans`.
  - Whether a new `-rc.K` pre-release tag is cut follows `docs/release-gates.md`, and is not decided here.
- Activation is an **AppHost restart** from the main checkout at the new rc tip, using `scripts/restart-apphost.ps1`.
  - Check the restart and launch locks first.
  - Time the restart around any live Windows qualification run (the investigation's C1514 note).
  - Confirm `GET /api/version` equals the rc tip.
  - Then confirm on the live log that L1, L5 or L6 appear when they should. The first lease expiry on server2 should now
    log L1 and then L5.
- **Rollback:** set `PhoneHomeRunner:CatchUpListTimeoutSeconds`, `CatchUpSessionTimeoutSeconds`, `CatchUpTimeoutSeconds`
  and `RecoveryWatchdogIntervals` to 0, then restart. List-only re-recovery has no setting of its own; see F-3.

**Review/Merge:**

1. Code commits the red tests and scaffold (D-11) and runs CP-1.
2. Code commits the implementation and runs CP-2 to CP-6.
3. Code runs the regenerate script, commits the index, and runs CP-7.
4. Code runs `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 300c8a99d0d267a39791410b7540189ab702f251 -HeadRef <tip>`
   and records the merge-tree against `origin/master`.
5. Review, at most three rounds. Mutation is waived (operator, 2026-11-12).
6. Land on master.
7. Reviewed rc port.
8. AppHost restart, then post-activation checks.

## (e) What remains for fixes 2 to 5

- **E-2 (fix 2):** renew the lease on any in-order frame from the runner, not only Heartbeat
  (`PhoneHomeLiveConnection.cs:509-511` vs `:540-570`).
- **E-3 (fix 3):** replace `PendingEvents == 0` (`PhoneHomeRunnerDirectory.cs:440`) with a small threshold or an
  oldest-pending age.
- **E-4 (fix 4):** in-file bounds on `_send.WaitAsync` and the write (`PhoneHomeLiveConnection.cs:410-414`), and on
  `SessionStateStore` `Gate.WaitAsync` for recovery-path callers (`:117`).
- **E-5 (fix 5):** coalesce SessionOutput activity writes per session, and rank the diagnostics `sessions` list top-N by
  bytes instead of the first 16 seen.
- **E-6 (left from fix 1, D-4):** run each runner's recovery attempt independently, so one runner's bounded budget never
  delays another.

## (f) Operator decisions

The plan proceeds under each recommended default.

- **F-1** Is a 30 s per-session catch-up budget (`CatchUpSessionTimeoutSeconds`) acceptable? Transcript fetch p95 is
  UNOBSERVED; persist was about 1 s and the worst phase 3.5 s under the incident load. **Recommend yes.** A false skip
  costs only an unverified transcript that later pulls repair.
- **F-2** Should a connect recovery become eligible when sessions were skipped, because the List completed (D-3)?
  **Recommend yes.** Fencing on transcripts reproduces the outage, and skipped sessions stay counted as occupied seats.
- **F-3** Should List-only lease re-recovery (D-2) ship without a rollback setting? **Recommend yes.** It is the
  incident's direct fix, and the budgets still bound the old path. Answer no to add `LeaseRecoveryListOnly` (default true).
- **F-4** Should `PhoneHomeRunnerStatusDto` stay unchanged in slice 1 (no `lastListAtUtc`/`recoveryStep`)?
  **Recommend yes.** L5 and L6 carry those facts in the log. A DTO change is a public-contract card of its own.
- **F-5** Is a 180 s window (6 × 30 s) the right watchdog Error threshold? **Recommend yes.** It is two full List budgets
  plus retries, so a single transient failure never triggers it.
- **F-6** Should a catch-up step timeout also set the runtime's transcript-persist-failure mark
  (`AgentSessionRuntime._persistFailures`), so receipt readiness answers Unknown until a fresh persist? **Recommend no for
  slice 1.** It touches `AgentSessionRuntime.cs` (fix 4 and fix 5 territory), and receipt readiness already re-pulls for
  itself.
