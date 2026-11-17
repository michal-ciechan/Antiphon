# CARD-1964 Slice 1 plan: bound and watch phone-home lease re-recovery

Plan task 6652bca1 (Opus), 2026-11-17. This is a plan only. No code, setting, restart or host was touched.

**Amendment 1** (plan task 2bef90c6, Opus, 2026-11-17) answers round-1 review 21daa066 (Grok,
`f5ed3feabbc9e4d7ea10d80b9dac639e7249f3c4`, `docs/superpowers/reviews/2026-11-17-card-1964-plan-review.md`). It re-reads
master `300c8a99d0d267a39791410b7540189ab702f251` (unchanged). B-1: the watchdog is now a state machine that cancels at
most once per window, and every applied restart opens a new window (Watchdog, V-4, V-11). B-2: `catch_up_list_budget` is
witnessed only on a stalled send gate (V-10). V-7 stays a plain eligibility guard, and V-4 asserts only what a silent
List can show. B-3: every checkpoint Min is re-derived from the classes (CP-7 is 27). Four advisories are applied: the
next-cycle gate sentence, the events-stopped boundary of D-2, the L2 cardinality wording and the CARD-1960 file list.

**Amendment 2** (Code task cad08e40, Opus, 2026-11-17) folds in the three advisories of the clean round-2 review
5f96c990 (Grok, `3c51e4caa08c96f5222e537ef18d782c0c88b31d`, `docs/superpowers/reviews/2026-11-17-card-1964-plan-review-r2.md`)
and records what the implementation settled. A1: the P-2 floor sentence is corrected; only the stand-in case gives
`OverrunLimit ≥ 360`. A2: `LastListAt` is published under `WatchLock` before `ReadInventoryAsync` returns, and the
poll holds that lock across its read of `W`, its fire decision and its cancel. A3: the refresh catch filter reads the
stopping token, so a step-budget cancel reaches the `refresh_list_budget` Warning. Implementation notes: the
`budgetToken` starts after any test hold (D-10); `no_list` does not fire while an in-flight attempt's List has
completed (that attempt is bounded by its catch-up budget and `attempt_overrun`); a watchdog cancel after the List is
handled like the whole-budget exhaustion (L3, eligibility from the completed List); V-4 counts 4 Lists in total and
V-6 counts every refusal of the streak (4 at the second L4).

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
  - *Boundary (amendment 1):* this argument covers frames the runner put on the socket. If the runner's events also
    stopped during the expiry, List-only re-recovery does not fetch transcript frames the runner never sent. An event
    queue overflow still closes the socket (G-11), so that case is a reconnect and runs the full catch-up. A quiet gap
    on a live socket is left to the existing on-demand transcript pull (`AgentSessionRuntime.CatchUpTranscriptAsync`)
    and receipt readiness (`AgentSessionRuntime.ReceiptReadiness.cs:85-96`). The pump does not run those for every
    listed session. D-3 already accepts eligibility from the completed List, and each listed session's seat stays
    occupied. F-3 ships List-only with no rollback setting.
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
CTS, with no timer. `budgetToken` is `attemptToken` plus `CatchUpTimeoutSeconds` on `live.Clock`, started after any test
hold (amendment 2). Each step token is
`budgetToken` plus its own step budget on `live.Clock`. Every await below is `await X.WaitAsync(token)`.

| Await | Site today | Token today | Slice 1 bound | On timeout or cancel (stopping token not cancelled) |
|---|---|---|---|---|
| Test hold | `:351-353` | stopping | `attemptToken` (D-10) | Watchdog cancel only: the attempt returns false, and the next turn retries. |
| **List** (catch-up) | `:359`, then `ReadInventoryAsync` `:289` | stopping; the reply is 60 s on the clock but `_send` and the write are un-timed | step `CatchUpListTimeoutSeconds` (90) | The existing Warning (`:363-366`) with `{Code}` = `catch_up_list_budget` (or `catch_up_budget` / `watchdog_restart`). Returns false. `NextCatchUp` = now + `CatchUpRetrySeconds`. **Ineligible.** |
| **List** (inventory refresh) | `:251` | stopping | same step budget | The existing refresh Warning (`:264-266`) with `{Code}` = `refresh_list_budget`. Its catch filter (`:258`) reads the stopping token, not the step token, so the budget cancel reaches the Warning (amendment 2, A3). The existing failure count and stale bound apply. `cycle.Refresh` completes, so the next refresh can start. |
| **Owner lookup** | `:383`, `GetBindingAsync` (DB) | stopping | step `CatchUpSessionTimeoutSeconds` (30), shared by one session's three steps | L2, step=`owner`. The session is skipped and counted unverified. The loop continues. |
| **Transcript fetch** | `:387` | stopping; the reply is 60 s on the clock | same session step | L2, step=`transcript.fetch`. Skipped, unverified, continue. |
| **Per-session gate + save** | `:388`, then `SessionStateStore.cs:117` and `PersistTranscriptCoreAsync` | **none** | same session step (token passed as `ct`, plus `WaitAsync`) | L2, step=`transcript.persist`. Skipped, unverified, continue. A save that committed before its cancel is reconciled by the runtime's existing post-cancel path, which the loop no longer waits on. |
| Whole catch-up | `:349-401` | none | `CatchUpTimeoutSeconds` (300) | If the List has not completed, this is the List row above. If it has, L3: the remaining sessions are skipped, the attempt returns **true** (List done), and the runner becomes eligible (D-3). |

The pump records the current step in the attempt as one of `hold`, `list`, `owner`, `transcript.fetch`,
`transcript.persist` or `refresh`. L6 and the tests read it.

**Which List failure gets which code (amendment 1, B-2).** A List whose reply never comes ends at the connection's
60 s reply timer, which `RequestAsync` arms right after the write (`PhoneHomeLiveConnection.cs:434-446`). Both timers
run on `live.Clock`, so that 60 s timer always fires before the 90 s step budget. The Warning keeps its existing
`RequestTimeout` code, unchanged. The step budget is what bounds a List that never reaches that timer: one stuck in
`_send.WaitAsync(ct)` (`:410`), in `beforeWrite` (`:412-413`) or in the write itself (`:414`). Only such a List logs
`catch_up_list_budget` (or `refresh_list_budget`). V-10 witnesses both codes with a stalled send gate. The pump's
List catch must turn an `OperationCanceledException` from the step or budget token into the budget code while the
stopping token is not cancelled. Today that filter (`:361`) rethrows every `OperationCanceledException`.

**A later persist never joins an abandoned one (amendment 1).** The next connect catch-up's persist of the same
session gets a new step token and its own `WaitAsync`. The observer's `ObserveTranscriptAsync` (`:449`) keeps its own
token. Neither awaits the abandoned task. An abandoned persist is not retried on the lease-refusal
turn. Lease re-recovery is List-only (D-2), so it never re-enters persist. The persist is also not started again on
any watchdog poll, because the watchdog only cancels and flags (Watchdog). One limit remains. An abandoned persist that
was cancelled after it acquired the gate keeps the gate until `lease.ReconcileAsync(false, CancellationToken.None)`
(`AgentSessionRuntime.cs:1363`) returns. Until then, the observer that shares that gate can still wait on it. That
wait lasts as long as the gate is held today by a persist in progress. Fix 4 (E-4) owns that bound.

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

This section was rewritten by amendment 1 (B-1). The round-1 text cancelled the replacement attempt on every poll once
`no_list` was true, because a restart did not move the window.

- **Where it runs.** `PhoneHomeRecoveryPump.ExecuteAsync` starts `WatchdogLoopAsync(stoppingToken)` only when
  `RecoveryWatchdogIntervals > 0` **and** `RecoveryWatchdogPollSeconds > 0`. The loop runs `RunWatchdogOnce()` every
  `RecoveryWatchdogPollSeconds` of real time, and the `finally` at `:79-89` joins it.
  - **`RecoveryWatchdogPollSeconds <= 0` leaves the watchdog loop stopped.** That is the documented off switch for the
    loop. The recovery pump itself keeps running, with its step and catch-up budgets.
  - `RecoveryWatchdogIntervals <= 0` also leaves the loop stopped, and makes `RunWatchdogOnce()` return at once.
  - Each decision reads the connection's own `live.Clock`, so tests call `RunWatchdogOnce()` directly with a fake clock.
- **Bounds used below.** Each bound uses its setting, or a stand-in when the setting is disabled:

  | Name | Value | Default |
  |---|---|---|
  | `RefreshBasis` | `InventoryRefreshSeconds`, or 30 when that is `<= 0` | 30 s |
  | `ListBound` | `CatchUpListTimeoutSeconds`, or 60 (the List reply timer) when that is `<= 0` | 90 s |
  | `CatchUpBound` | `CatchUpTimeoutSeconds`, or 300 when that is `<= 0` | 300 s |
  | `WindowSeconds` | `max(RecoveryWatchdogIntervals × RefreshBasis, ListBound + CatchUpRetrySeconds + max(0, RecoveryWatchdogPollSeconds) + 30)` | `max(6 × 30, 90 + 5 + 10 + 30)` = `max(180, 135)` = 180 s |
  | `OverrunLimit` | `CatchUpBound + ListBound` | 390 s |

  The floor in `WindowSeconds` keeps a small `RecoveryWatchdogIntervals` from opening a window shorter than one List
  attempt plus its retry. A disabled budget never yields a zero limit.
- **State.** All watchdog state is per connection, in `ConnectionState`, so a new connection starts clean. The loop and
  the watchdog task share it, so every field is read and written under the new `ConnectionState.WatchLock`.

  | Field | Type | Written by | Meaning |
  |---|---|---|---|
  | `FirstSeenAt` | `DateTimeOffset` | `StateFor`, when it creates the state, from `live.Clock` | The pump's first sight of this connection. |
  | `LastListAt` | `DateTimeOffset?` | `ReadInventoryAsync` on success (catch-up and refresh), under `WatchLock`, before it returns (amendment 2, A2) | The last completed List. The poll holds `WatchLock` across its read of `W`, its fire decision and its cancel, so it never fires on a `W` read before that write. |
  | `LastRestartAt` | `DateTimeOffset?` | The loop, when it applies a restart | The start of the window that restart opened. |
  | `FiredWindowStart` | `DateTimeOffset?` | The watchdog, on a `no_list` fire | The window start that has already fired. |
  | `RestartRequested` | `bool` | Set by the watchdog, cleared by the loop | A restart is pending. |
  | `AttemptId` | `long` | The loop, +1 at each attempt start | The identity of the current attempt. |
  | `AttemptStartedAt` | `DateTimeOffset?` | The loop; null when no catch-up attempt is in flight | |
  | `AttemptStep` | `string` | The loop and the refresh | `none`, `hold`, `list`, `owner`, `transcript.fetch`, `transcript.persist` or `refresh`. |
  | `AttemptCts` | `CancellationTokenSource?` | The loop, at attempt start; disposed and nulled at attempt end | The watchdog CTS linked into `attemptToken`. |
  | `OverrunFiredAttemptId` | `long` | The watchdog, on an `attempt_overrun` fire | The attempt already cancelled for overrun. |

  - `refresh` is reported while a refresh List is in flight and no catch-up attempt is. A refresh is not an attempt: it
    has no `AttemptStartedAt` and no `AttemptCts`. Its own List budget bounds it, and an applied restart detaches it.
  - The window start is `W = max(FirstSeenAt, LastListAt, LastRestartAt)`, where a null field does not count.
- **States and what one poll does.** `RunWatchdogOnce()` takes each runner in `RemoteRunnerIds` with its current
  `SnapshotLive` connection, and acts on at most one trigger per runner per poll.

  | State | Condition | What the poll does |
  |---|---|---|
  | **Off** | `RecoveryWatchdogIntervals <= 0` | Returns at once. |
  | **Skip** | No current connection, or the socket is not open. Or the runner is `DispatchEligible`, `InventoryRefreshSeconds <= 0` and no attempt is in flight. | Nothing. An eligible runner with refresh off is expected to make no List, so `no_list` there would be a false alarm (V-11). |
  | **Watching** | `RestartRequested` is false | It checks two triggers in order and fires on the first that holds. **1. `attempt_overrun`:** `AttemptStartedAt` is set, `now − AttemptStartedAt > OverrunLimit`, and `OverrunFiredAttemptId ≠ AttemptId`. It sets `OverrunFiredAttemptId = AttemptId`. **2. `no_list`:** `now − W ≥ WindowSeconds` and `FiredWindowStart ≠ W`, and no in-flight attempt has completed its List (`LastListAt ≥ AttemptStartedAt`; amendment 2). It sets `FiredWindowStart = W`. If neither holds, it does nothing. |
  | **Pending** | `RestartRequested` is true | Nothing. It writes no log line and cancels nothing. |

  - **Fire** (Watching → Pending) does three things: log L6 once, cancel `AttemptCts` if it is not null, and set
    `RestartRequested = true`.
    - For L6, `{SinceListSeconds}` is `now − max(FirstSeenAt, LastListAt)`, the real time without a List.
      `{LimitSeconds}` is `WindowSeconds` or `OverrunLimit`.
    - The cancelled attempt returns through its `WaitAsync(attemptToken)` (D-1), so this runner's turn is no longer
      blocked.
  - **Apply** (Pending → Watching) happens in `RunOneAsync`, after the live and socket checks and before the
    `recoveringLease` branch, so an eligible runner's stuck refresh is handled too. Under the lock, if
    `RestartRequested` is set, the loop:
    - sets `RestartRequested = false` and `LastRestartAt = now`;
    - clears `NextCatchUp`, so it re-attempts at once, on this same turn;
    - detaches a `Refresh` task that has not completed, observing its fault at Debug, so a new refresh can start;
    - resets the refusal log timer.

    Then `W = LastRestartAt = now`. That is later than the fired window start, because that fire needed
    `now − W_old ≥ WindowSeconds > 0`. So a new window opens, and it can fire once.
  - The loop and the watchdog **never** touch `cycle.Pump`, `PumpStop` or `Recovered`, the socket, `Disconnect`, or
    `DispatchEligible`. Every eligibility change still goes through `MarkRecovered` or `TryRecoverLease` after a
    completed List (I-1).
- **P-1: a replacement attempt gets its full List budget.** The loop applies a restart and starts the replacement
  attempt in the same `RunOneAsync` turn, at time `R`. From then on `W ≥ R`. `no_list` needs `now − W ≥ WindowSeconds`,
  so its earliest cancel is at `R + WindowSeconds`. A later poll can only make it later. The replacement's List ends by
  `R + ListBound`: it completes, or fails at the 60 s reply timer, or fails at its budget. `attempt_overrun` cannot
  cancel it earlier either, since `OverrunLimit > ListBound`. The required inequality is therefore
  `WindowSeconds > ListBound`. The floor guarantees more than that:
  `WindowSeconds ≥ ListBound + CatchUpRetrySeconds + RecoveryWatchdogPollSeconds + 30`. With the defaults that is
  `180 ≥ 90 + 5 + 10 + 30 = 135 > 90`, which leaves 90 s of margin. For a silent reply (60 s), a second attempt also fits
  (`60 + 5 + 60 = 125 < 180`).
- **P-2: a List that can never complete is retried at a bounded rate.**
  - A `no_list` fire needs `FiredWindowStart ≠ W` and `now − W ≥ WindowSeconds`. Every applied restart sets
    `W = R`. An `attempt_overrun` fire needs an attempt that started at or after `R` and ran past `OverrunLimit`. So two
    watchdog cancels on one connection are at least `min(WindowSeconds, OverrunLimit)` apart: 180 s by default.
    `WindowSeconds` never drops under its 135 s floor (defaults), but `OverrunLimit ≥ 60 + 300` holds only when both
    budgets are disabled and use their stand-ins (amendment 2, A1). A small positive `CatchUpTimeoutSeconds` or
    `CatchUpListTimeoutSeconds` makes `OverrunLimit`, and so the gap, shorter than 135 s. It stays strictly above
    `ListBound`, so the replacement keeps its List budget (P-1) and no poll cancels it early. That is at most one L6
    Error and one cancel per window.
  - `attempt_overrun` fires at most once per attempt. Pending suppresses both triggers until the loop applies the
    restart, so a parked loop gets no repeated log or cancel either.
  - Between fires, the existing pacing holds. Every failed attempt sets `NextCatchUp = now + CatchUpRetrySeconds`
    (`:190-193`), and a silent List lasts until its 60 s reply timer or its 90 s budget. The watchdog adds at most one
    immediate attempt, by clearing `NextCatchUp`, per window.
  - There is no tight cancel loop. A runner whose List never completes logs one L6 Error and gets one watchdog
    cancel every `WindowSeconds`, plus the existing per-attempt Warning about every 65 s.
- **Pending while another runner holds the loop.** The loop is serial (G-1). Another runner's attempt can delay the
  apply, but only by that attempt's own bounded time (at most `OverrunLimit`). During that delay this runner's poll is a
  no-op. E-6 removes the cross-runner delay.

### Settings

All of these go in `PhoneHomeRunnerSettings` (`server/Application/Settings/PhoneHomeRunnerSettings.cs`). Each is an int,
and a value of zero or less disables it.

| Setting | Default | Reason for the default |
|---|---|---|
| `CatchUpListTimeoutSeconds` | 90 | The List reply timer is 60 s. The extra 30 s covers the send-gate wait and the write, which are outside it. |
| `CatchUpSessionTimeoutSeconds` | 30 | Observed slow phases under the incident load were 1.0-3.5 s (output.activity max 3.0 s, output.publish 3.5 s, transcript.persist 1.03 s). 30 s is about 8x the worst. Transcript fetch p95 is UNOBSERVED (F-1). |
| `CatchUpTimeoutSeconds` | 300 | One List (90) plus about 7 slow sessions at 30 s. Capacity is 10. Most sessions take seconds. |
| `RecoveryWatchdogIntervals` | 6 | 180 s with the 30 s refresh: two full List budgets plus retries before an Error. The effective window never drops below `ListBound + CatchUpRetrySeconds + RecoveryWatchdogPollSeconds + 30` (135 s with the defaults), so a replacement attempt always gets its full List budget (Watchdog, P-1). |
| `RecoveryWatchdogPollSeconds` | 10 | Cheap in-memory checks. A value of zero or less leaves the watchdog loop stopped, and the pump keeps running. |
| `RecoveryRefusalLogSeconds` | 60 | Matches `DesktopCapabilitiesFailureLogSeconds` (60). |

The lease length (90 s) and the heartbeat interval (15 s) are **unchanged**.

### Log lines

All lines are structured templates. Cardinality is bounded: `RunnerId` comes from configured runners, `Epoch` is an int,
every `{Step}`/`{Reason}`/`{Stage}`/`{Trigger}`/`{Code}` is from a fixed set, and `SessionId` is a session from the
runner's List reply: at most one L2 line per listed session per catch-up. That List is not capped by the capacity setting
(amendment 1). The lines carry no payload, ticket, shared secret, transcript text or runner error
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
| V-4 | `Silent_list_trips_the_watchdog_once_per_window_and_recovery_follows_the_answer` (amendment 1, B-1 and B-2) | Setup: no DB, refresh 0, window 180 s. Every advance below is in steps under the 90 s lease with a heartbeat after each, except the expiry itself. T0 is the initial recovery List. (1) Expire (advance 91 s, `SnapshotLive`), heartbeat, `SilentFor(List)`. Run cycle 1, wait for the List and 100 ms, advance 61 s: cycle 1 returns false, and its catch-up List Warning has `{Code}` = `RequestTimeout` (the 60 s reply timer, not a budget code). (2) Advance to T0+181 s. Call `RunWatchdogOnce` twice: **exactly one** L6 with `no_list` and limit 180. The second poll is Pending and logs and cancels nothing. (3) Run cycle 2, which applies the restart at R and sends List 2 at once. Wait for it and 100 ms, advance 30 s, call `RunWatchdogOnce`: still one L6, and after 200 ms cycle 2 has **not** completed. Advance 31 s: cycle 2 returns false, and its Warning `{Code}` is `RequestTimeout`, not `watchdog_restart`, so the replacement kept its List budget (P-1). (4) Advance to R+181 s and call `RunWatchdogOnce` twice: **a second** L6 `no_list` (`{SinceListSeconds}` ≥ 361), two L6 in total, so one per window (P-2). (5) `Speak(List)`, run cycle 3: true, eligible, one L5, List count 4 in total (the initial recovery plus three re-recovery Lists; amendment 2). | The red is step (2): the base seam is a no-op, and no L6 line exists anywhere. The round-1 defect (cancel on every poll) would also fail step (3): the +30 s poll would cancel cycle 2 early with `watchdog_restart`. A silent List cannot show `catch_up_list_budget` (B-2), so V-4 does not assert it. Eligibility after the answer is already green at base (G-2), and the test keeps it as a guard. |
| V-5 | `Lease_expiry_logs_once_per_flip` | No DB. Recover, advance 91 s. Call `GetLiveStoreId` once, `SnapshotLive` 3 times and `Status` once. **Green:** exactly one L1, with `LeaseSeconds` 90, the runner and the epoch. Then heartbeat, recover, expire again: two L1 lines in total. | `SnapshotOf` (`:972-981`) and `GetLiveStoreId` (`:121-136`) call only `Notify` and have no logger call, so there are 0 lines. |
| V-6 | `Lease_recovery_refusal_logs_its_reason` with `[Arguments("before")]` and `[Arguments("after")]` | **before:** `EndPumpForTest`, emit one event, and hold it (PendingEvents 1, as `Lease.cs:120-125` does). Run the cycle 3 times: one L4 (`before_catch_up`, `pending_events`, Refusals 1). Advance 61 s with a heartbeat and run a cycle: a second L4 with Refusals 4, every refusal of the streak counted (amendment 2). Release the event and run a cycle: one L5 with its refusal count. **after:** silent List, apply draining, reply. **Green:** one L4 (`after_catch_up`, `draining`). | The refusals return false at `:179-180` and `:196-197` with no log, so there are 0 lines. |
| V-7 | `Timed_out_list_never_makes_the_runner_eligible` with `[Arguments("connect")]` and `[Arguments("lease")]` | Silent List. Advance past the List budget with a heartbeat. The cycle returns false. The peer then emits the late Result for that request id. **Assert:** `DispatchEligible` is false; `Directory.Resolve(id)` throws "has not completed recovery"; a cycle before the retry delay returns false; the List count is 1 (connect) or 2 (lease). | **Guard. Green at base and must stay green** (I-1, I-2, I-3). It kills the mutants "a step timeout counts as success" and "an abandoned attempt flips eligibility". It is the fail-safe negative. |
| V-8 | `Catch_up_total_budget_skips_the_remaining_sessions` | Setup: DB world, `CatchUpTimeoutSeconds`=45. Sessions B1 and B2 are fetch-silent, then G. Advance 31 s (B1 gets L2), then 15 s. **Green:** cycle true, eligible, one L3 with "2 of 3 listed" and Unverified 3, and G is never requested. | At base B1 waits for the 60 s fetch timer. +46 s < 60 s, so the cycle does not complete. |
| V-9 | `Watchdog_cancels_an_attempt_that_outlives_its_budgets` | No DB. `HoldCatchUp(runner, hold)`, then run a cycle. Advance 391 s with a heartbeat, then call `RunWatchdogOnce`. **Green:** one L6 with `attempt_overrun` and step `hold`, and the cycle returns false within 5 s with the runner ineligible. Release the hold, heartbeat, run a cycle: true. | The base seam is a no-op, and the hold waits on the stopping token (`:353`), so the cycle never returns and the L6 line is absent. |
| V-10 | `Stalled_send_gate_ends_the_list_at_its_budget` with `[Arguments("catch_up")]` and `[Arguments("refresh")]` (amendment 1, B-2) | **How the fake stalls the send.** No DB. The test takes the live connection (`host.WaitLiveAsync()`). It calls the existing public overload `live.RequestAsync(PhoneHomeOperation.List, null, beforeWrite: _ => { entered.TrySetResult(); return new ValueTask<string?>(release.Task); }, CancellationToken.None)`. That holder takes `_send` (`PhoneHomeLiveConnection.cs:410`) and parks in `beforeWrite` (`:412-413`), inside the send gate. `_send` stays held and no frame is written. The test awaits `entered` before the step below. **catch_up:** new connection, refresh 0. Run a cycle, wait 100 ms, advance 91 s (45 s + 46 s, a heartbeat after each). **Green:** the cycle returns false within 5 s, the runner is ineligible, there is one catch-up List Warning with `{Code}` = `catch_up_list_budget`, and `peer.RequestCount(List)` is 0 (no List frame left: the stall was in the send). Then `release.SetResult("test release")`, so the holder throws `PhoneHomeRequestRefusedException` with nothing written. Advance `CatchUpRetrySeconds`, heartbeat, run a cycle: true, eligible, List count 1. **refresh:** refresh 30 s, initial recovery eligible (List count 1). Stall the send, advance 31 s with a heartbeat, run a cycle (it starts the refresh, which parks in `_send`). Advance 91 s as above. **Green:** within 5 s (polling the recording logger), one refresh List Warning with `{Code}` = `refresh_list_budget`. The runner stays eligible, and the List count is still 1. Both cases release the holder in `finally`. | The pump passes the stopping token to `ListAsync`, so `_send.WaitAsync(ct)` waits until the holder lets go. Moving the fake clock arms nothing, because the 60 s reply timer starts only after the write (`:434-436`). **catch_up:** `cycle.WaitAsync(5 s)` throws `TimeoutException`. **refresh:** no refresh Warning appears within 5 s. Both red. |
| V-11 | `Watchdog_stays_quiet_for_an_eligible_runner_without_refresh` (amendment 1) | No DB, refresh 0. Recover, so the runner is eligible with List count 1. Advance 400 s in steps under the lease with a heartbeat after each, and call `RunWatchdogOnce` after each step. **Assert:** zero L6, the runner stays eligible, and the List count is still 1. | **Guard. Green at base and must stay green.** The base seam logs nothing. It kills the mutant "`no_list` ignores the Skip rule", which would raise an Error every window on a healthy runner. |

That is 11 methods and 14 cases (amendment 1; round 1 had 9 methods and 11 cases). At base, 11 cases are red: V-1, V-2,
V-3, V-4, V-5, both V-6 cases, V-8, V-9 and both V-10 cases. 3 cases are green guards: both V-7 cases and V-11.

Each new log line has a witness:

| Line | Witness |
|---|---|
| L1 | V-5 |
| L2 | V-2, V-3 |
| L3 | V-8 |
| L4 | V-6 |
| L5 | V-1, V-4, V-6 |
| L6 | V-4 (`no_list`, once per window, twice across two windows), V-9 (`attempt_overrun`), V-11 (no L6 on a healthy runner) |
| L-List codes | V-10 `catch_up` asserts `catch_up_list_budget` and V-10 `refresh` asserts `refresh_list_budget`, both on a stalled send gate (amendment 1, B-2). A silent List reply always ends at the 60 s reply timer first, so V-4 asserts `RequestTimeout` there and V-7 asserts no code. `catch_up_budget` and `watchdog_restart` share the same exception-to-code mapping and have no dedicated assertion in slice 1. |

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
| CP-1 | red | tests/Antiphon.Tests -> bin-c1964-red/ | stall-red | `/*/*/C1964RecoveryPumpStallTests/*` | V-1,V-2,V-3,V-4,V-5,V-6,V-7,V-8,V-9,V-10,V-11 | 11 intended failures (V-1,V-2,V-3,V-4,V-5,V-6 x2,V-8,V-9,V-10 x2) and 3 passed (V-7 x2,V-11) before implementation | 14 | 15 |
| CP-2 | green | tests/Antiphon.Tests -> bin-c1964-green/ | recovery | `/*/*/(C1964RecoveryPumpStallTests*)\|(PhoneHomeRecoveryEligibilityTests*)\|(MultiRunnerRecoveryTests*)\|(PhoneHomeEventPumpTests*)/*` | V-1..V-11,R-1 | all listed, 0 failed | 43 | 15 |
| CP-3 | green | CP-2 | recovery-adjacent | `/*/*/(PhoneHomeDeferredKillTests*)\|(PhoneHomeStrandedQueueTests*)\|(PhoneHomeTranscriptIncidentTests*)\|(DefaultRunnerEligibilityTests*)/*` | R-1 | all listed, 0 failed | 27 | 4 |
| CP-4 | green | CP-2 | directory-lease | `/*/*/(PhoneHomeDirectoryTests*)\|(PhoneHomeRunnerRetirementIdentityTests*)\|(PhoneHomeRunnerRetirementCycleTests*)\|(PhoneHomeImmediateSendTests*)/*` | R-2 | all listed, 0 failed | 31 | 4 |
| CP-5 | green | CP-2 | backpressure-rolling | `/*/*/(PhoneHomeBackpressureTests*)\|(PhoneHomeRollingRunnerTests*)/*` | R-1,R-2 | all listed, 0 failed | 43 | 6 |
| CP-6 | green | CP-2 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-3 | all listed, 0 failed | 81 | 6 |
| CP-7 | green | CP-2 | impact-index | `/*/*/DerivedImpactIndexTests/*` | R-3 | all listed, 0 failed, after the regenerate script ran and the index was committed | 27 | 12 |

**Import check (2026-11-17, plan task):** `dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll import --plan <this plan> --out .antiphon/c1964-plan/manifest-1.yaml` (ignored path). It printed `imported 7 rows`, exit 0, with **no warnings**. The row timeouts it derived are 45, 45, 15, 15, 18, 18 and 36 minutes.

**Import check, amendment 1 (2026-11-17, plan task 2bef90c6):** the same command with `--out .antiphon/c1964-plan-amend1/manifest.yaml` (ignored path) printed `imported 7 rows`, exit 0, with **no warnings**. The manifest carries `minExecuted` 14, 43, 27, 31, 43, 85 and 27 and the same timeouts, 45, 45, 15, 15, 18, 18 and 36 minutes.

**CP-6 correction (amendment 2).** The CP-6 run at `e191ac858` (run `20261117-071442-5148`) executed 81 tests, 81 passed,
and stopped on its floor of 85. `TaskPlatformGuidanceTests.cs` has 9 `[Test]` methods, but only 5 are in
`TaskPlatformGuidanceTests`; the other 4 belong to `RunnerDefaultGuidanceTests` in the same file, which the class filter
does not select. The TRX per-class counts match the table below. The filter is unchanged; the floor is the exact
count of that filter.

**Min derivation (amendment 1, B-3).** Each Min is the exact case count at `300c8a99d`: class-level `[Test]` methods
at member indentation, where a method with k `[Arguments]` counts k, fixture text inside string literals is excluded, and
no class in the roster uses `MethodDataSource` or `Matrix` (three use a `ClassDataSource` fixture, which adds no cases).
No roster method carries a skip attribute.

| Row | Class: cases (methods) | Min |
|---|---|---|
| CP-1 | `C1964RecoveryPumpStallTests` 14 (11; V-6, V-7 and V-10 have 2 `[Arguments]` each) | 14 |
| CP-2 | `C1964RecoveryPumpStallTests` 14; `PhoneHomeRecoveryEligibilityTests` 13 (7: `.Lease.cs` 4 methods with 10 cases, the main file 3); `MultiRunnerRecoveryTests` 8; `PhoneHomeEventPumpTests` 8 | 43 |
| CP-3 | `PhoneHomeDeferredKillTests` 3; `PhoneHomeStrandedQueueTests` 16; `PhoneHomeTranscriptIncidentTests` 5; `DefaultRunnerEligibilityTests` 3 | 27 (unchanged) |
| CP-4 | `PhoneHomeDirectoryTests` 8; `PhoneHomeRunnerRetirementIdentityTests` 9 (7); `PhoneHomeRunnerRetirementCycleTests` 1; `PhoneHomeImmediateSendTests` 13 | 31 (was 29) |
| CP-5 | `PhoneHomeBackpressureTests` 9 (`.cs` 6, `.Bounds.cs` 3; `.Scheduling.cs` and `.Protocol.cs` hold no tests); `PhoneHomeRollingRunnerTests` 34 (`…DrainTests.cs` 23 from 22 methods, `…RetireTests.cs` 4, the main file 7) | 43 (was 42) |
| CP-6 | `TestClassificationPolicyTests` 42 (28); `TestClassificationGuardTests` 1; `SlowTestRegistryMergeTests` 5 (3); `SlowTestTripwireTests` 2; `NativeLinuxPolicyWitnessTests` 3; `MechanicalPcContractTests` 11 (7); `TaskPlatformGuidanceTests` 5; `C1729LayeringGuardTests` 11; `TestLaneCategoryGuardTests` 1. The two `tests/Shared` classes are linked into `Antiphon.Tests` (`Antiphon.Tests.csproj:185`, `:209`). | 81 (amendment 2; amendment 1 said 85) |
| CP-7 | `DerivedImpactIndexTests` 27 (`[Test]` at lines 134-877; the other `[Test]` tokens in the file are fixture text) | 27 (was 42) |

Row meanings:

- **R-1:** the recovery, hold and pump regressions.
- **R-2:** the classes that advance past the lease and now emit L1.
- **R-3:** the guard batch from `docs/testing-and-build.md` line 323, and the derived impact index.

The roster has no suffix collisions. Each operand prefix matches exactly one class:

- `PhoneHomeRollingRunnerTests*` covers its `…RetireTests.cs` and `…DrainTests.cs` partial files (both declare
  `partial class PhoneHomeRollingRunnerTests`), and no other class starts with that name.
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
  refusal streaks, L2-L6, the watchdog loop and `RunWatchdogOnce`, `LastCatchUpUnverifiedSessions`, and the
  `ConnectionState` watchdog fields and `WatchLock` (Watchdog, State; amendment 1).
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

- **CARD-1960** (InProgress, the quarantine release route). Its code branch `origin/feat/card-task-aaabade9` at
  `5d0ac1366096de492324e2bf57634f389c332409` changes, against its merge base with master (amendment 1, full list):
  `server/Api/Endpoints/SessionRunnerEndpoints.cs`, `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs`,
  `RemoteWorkspaceMirrorResult.cs` (the mirror DTO), `scripts/workspace-slots.ps1`, `docs/docker-stack.md`,
  `docs/ops-http.md`, its plan checkpoint doc, and the tests `C1960QuarantineReleaseRouteTests.cs`,
  `C1960WorkspaceSlotsScriptTests.cs`, `C0824SlotTransportWorld.cs` and `RemoteWorkspacePreparerTests.cs`. The pump calls
  `PhoneHomeRunnerClient` but this slice does not edit it. CARD-1960 does not edit `PhoneHomeLiveConnection.cs`; it may
  read the `WorkspaceSlotQuarantineRelease` arm of `RequestTimeoutFor` there.
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
