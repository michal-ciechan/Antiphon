# CARD-0561: a poisoned transcript line must not blind delivery confirmation, and a standing resume loop must end in a decision

Plan task `d7af0e48`, 2026-09-18, inspected checkout `6ab13481` (`feat/card-task-d7af0e48`; line numbers
below are at that SHA). Investigation:
[2026-09-18-card-0561-orchestrator-session-died.md](../../investigations/2026-09-18-card-0561-orchestrator-session-died.md)
(task `8ffa7df1`, commit `627d448f` on `feat/card-task-8ffa7df1`; not yet on this branch, referenced by commit).
Read-only against code; no fix was built. TestDesign is a separate stage; the acceptance table at the end
is written at guard granularity so it can be short.

Owners: [runtime invariants](../../session-runtime-invariants.md) (§ Standing conversation continuity,
Gotcha #57), [testing](../../testing-and-build.md), [conventions](../../project-context.md),
[HTTP](../../ops-http.md).

## Disposition in five lines

1. **NUL is replaced at the persist boundary, not rejected.** `PersistTranscriptAsync` maps every string
   member of every incoming `SessionRunnerTranscriptEvent` through one helper that turns U+0000 into
   U+FFFD before dedup and before the insert (D-1). The 2026-09-18 tool_result persists; nothing else changes.
2. **One bad row costs one row, never the flush.** When the batch `SaveChanges` fails with a database
   update error, the same rows are re-saved one at a time; a row that fails alone is retried once as a
   metadata-only stub, and only then skipped with a Warning that names it (D-2). Turn boundaries and
   UserPrompt rows land even when a sibling row cannot.
3. **A blind confirmation is not a kill verdict.** The runtime remembers the last persist failure per
   session; `HandleDeliveryFailureAsync` withholds the AlwaysOn kill and refunds the attempt when
   `NoTranscriptRecord` was reached while persistence for that session was failing (D-3). The message
   stays Pending for the stranded sweep, exactly like the CARD-0103 refund shape.
4. **Repeated failed resumes of one conversation end in the existing continuity decision, not auto-Fresh.**
   A new counter on supervision state counts non-infrastructure failed supervised attempts; at
   `Supervision:ResumeFailureHoldAttempts` (default 5, 0 disables) the supervisor places the CARD-0466
   durable hold with a new reason `RepeatedResumeFailure`, which is already an Error-severity Attention
   row with Retry / Select / Fresh actions (D-4). **This deliberately does not do what the brief's option 2
   asks** (honour `FreshAfterResumeFailures` or auto-Fresh after N): CARD-0466 removed count-authorised
   Fresh nine days ago after an incident of the opposite shape, and there is no trustworthy positive
   evidence in this incident that the conversation itself was unusable (the 463 % reading is suspect, see
   ground truth). The flip to auto-Fresh is one call at one site and is written down under D-4 so the
   decision can go the other way without re-planning.
5. **No remote-control change.** The `menu=true` typing race (investigation uncertainty 1) is not entangled
   with any of the four slices and stays a follow-on card (§ Out of scope).

## Ground truth

Verified on `6ab13481` on 2026-09-18 unless the row cites the investigation.

| Brief / investigation assumption | Observed | Consequence |
|---|---|---|
| A NUL in a tool_result made persist throw 22021 and the catch swallowed it. | `PersistTranscriptAsync` (`AgentSessionRuntime.cs:797-916`) builds every row (`:869-896`), saves them in one `SaveChangesAsync` (`:903`), and the outer `catch (Exception)` (`:911-915`) logs a Warning and returns `PersistResult.Empty`. The tailer decodes JSON `\u0000` escapes into real `'\0'` chars (`TranscriptTailer.cs:319` decodes the line as UTF-8; `System.Text.Json` unescapes), and Postgres `text` rejects `0x00` regardless of client encoding. No NUL handling exists anywhere in `server/` (grep for `'\0'`, `\u0000`, `22021`: none). | D-1 replaces at the boundary. |
| The whole flush is lost, not just the bad row. | One `SaveChanges` per batch; EF Core aborts the transaction on the first rejected row, so every row in that batch (UserPrompt, TurnEnd, AssistantText, …) is discarded together. The same all-or-nothing applies to any other per-row rejection: `Kind` 40, `Uuid` 64, `ParentUuid` 64, `Role` 40, `ToolName` 200, `ToolUseId` 120, `StopReason` 60 chars (`AppDbContext.cs:1188-1200`) — an MCP tool name over 200 chars would blank a flush today with SQLSTATE 22001. | D-2 is not NUL-specific; it is the general per-row fallback. |
| The poison line was retried 2 102 times in one day. | All three callers feed the same method: the live stream (`ObserveTranscriptAsync` `:349-362`, one entry per call), the grace-window pull (`CatchUpTranscriptAsync` `:688-700`), and the pump/backfill sync (`SyncTranscriptAsync` `:709-753`). Catch-up and sync re-send the runner's full snapshot, which still contains the line; dedup happens only after a successful save. Nothing marks a row as unpersistable. | Under D-1 the line persists once and dedups thereafter; under D-2 an unpersistable row is stubbed once, so the retry storm cannot recur for any per-row cause. |
| Delivery confirmation reads DB rows, so it went blind. | The confirm loop's deadline verdict is `NoTranscriptRecord` (`SessionMessageQueueService.cs:3078-3131`); the pre-kill grace (`:3748-3751`) re-pulls via `CatchUpTranscriptAsync`, which persists through the same failing method. `IsWorkingAsync` (`:4250`) reads the same rows. There is no signal from the runtime to the queue that persistence is failing; the only non-kill verdicts are `ForbiddenBody`, `LocalCommandNotAccepted`, `BackendUnreachable` (`:3883-3887`) and the CARD-0103 pre-first-turn refund (`:3833-3855`). | D-3 adds the signal and a refund arm; no new verdict. |
| The supervisor resumed the same session and `FreshAfterResumeFailures` was ignored. | By design since CARD-0466 D-1: `SupervisionSettings.FreshAfterResumeFailures` is documented "Deprecated compatibility setting, ignored" (`SupervisionSettings.cs:25-26`), `Program.cs:190-191` warns when configured, `StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff` pins that values 0, 1, 2 and `int.MaxValue` never restore Fresh, and `docs/session-runtime-invariants.md:365-373` states "Neither failure counter authorizes Fresh." The supervisor's due-attempt is always `StartAsync(Fresh: false, …, automatic: true)` (`AgentSupervisorService.cs:273-274`). | Option 2a of the brief ("honour `FreshAfterResumeFailures`") would reverse a landed decision and delete a pinning test. Rejected in D-4. |
| Backoff never gives up and never Freshes. | `Backoff` is `min(5s·2ⁿ, 30 d)` (`:460-463`); `EscalateIfTierCrossedAsync` (`:465-483`) records `BackoffEscalated` once at ≥1 h (Warning) and ≥1 day (Critical). `RestartFailurePolicy.Charge` (`RestartFailurePolicy.cs:49-56`) grows `RestartBackoffFailures` for every non-`ContinuityUnavailable` kind and `ConsecutiveFailures` only for `LaunchOrProcessFailure`. Reset requires Running plus `InteractiveLaunchCompletedAt` older than 10 min (`:154-183`). Confirmed. | D-4 adds a third, narrower counter and a trip point; the ladder itself is unchanged for the sub-threshold case and for `Infrastructure`. |
| What each wave-2 attempt was charged as. | A delivery-kill during the boot flush runs `KillGenerationAsync(…, SystemRequest)` (`:4002-4003` → `AgentSessionService.cs:1174-1222`): row `Stopped`, `FailureReason` null. The in-flight launch's `RequireCurrentCheckLaunchAsync` (`:552-565`) then throws `specialist_start_intent_revoked`; the launch catch stamps `RestartFailureKind = Classify(ConflictException)` = `Unknown` and `Status = Failed` (`:381-386`). `NotReadyMessage` (`:1931-1934`) is an `InvalidOperationException` → `Unknown` as well. `Observe` (`RestartFailurePolicy.cs:36-47`) therefore charged `RestartBackoffFailures` only; `ConsecutiveFailures` stayed 0. | The new counter must count `Unknown` and `LaunchOrProcessFailure`, never `Infrastructure` or `ContinuityUnavailable`. |
| "463 % of the 200 k ceiling" proves the conversation was unusable. | `SessionContextUsage.cs:66-105` takes the highest-sequence occupancy-bearing row and divides `input + cache_read + cache_creation` by `ContextWindowSettings.ResolveCeiling(modelId)`; the log line names model `claude-sonnet-5` with ceiling 200 000 (`ContextWindowSettings.cs:11`, no `[1m]` override matched). A single API call cannot carry 926 k prompt tokens on a 200 k model, so either the ceiling or the accounting for that row is wrong. The code already treats the reading as diagnostic only: `TryReadFullnessAsync` (`:1945-1966`) never throws and only decorates the not-ready message. CARD-0466 D-4: "generic readiness failure is not a corruption detector." | No decision in this plan is gated on fullness. The suspect reading is a follow-on (§ Out of scope). |
| A hold is the existing "stop and decide" mechanism. | `StandingContinuityState.HoldAsync` (`StandingContinuityState.cs:13-36`) sets `ContinuityHeldAt/SessionId/Reason/Evidence` (evidence column 1 000 chars, `AppDbContext.cs:181`), nulls `NextRestartAt`, and adds one `StandingContinuityHeld` Info incident. `SuperviseAsync` returns before scheduling while `ContinuityHeldAt` is set (`:146`). `AttentionService.BuildStandingContinuityItemsAsync` (`:1560-1569`) renders every hold as `StandingContinuityDecision` at `AlertSeverity.Error` with OpenAgent/OpenDrawer. Ordinary Start while held is 409 `standing_continuity_held` (`AgentControlService.cs:156-157`); `retryContinuity`, `resumeSessionId` and `fresh` acknowledge it. `Clear` (`:38-44`) runs inside `AcceptAsync` for **every** accepted launch, automatic included (`AgentControlService.cs:632`). | D-4 reuses all of it with one new enum member. The new counter must **not** be reset in `Clear` (that would reset it on every supervised resume); it resets on healthy uptime and on the `!automatic` branch (`:633`). |
| The client renders continuity reasons. | `client/src/api/agents.ts:260` is a closed union of the four reasons; `StandingSessionRecovery.tsx:26-30` has reason-specific copy with a generic fallback; `attentionVisuals.ts:54` keys on `AttentionKind`, not reason. | One union member and one copy branch; attention visuals unchanged. |
| Existing tests reach the seams this plan touches. | `AgentSupervisionTests.BuildHarness(root, adapters, supervision, …, definitionKind, configureDb)` (`:585-588`) with `FakeAgentProtocolAdapter.ThrowOnStart` / `ReadyResult` and `SessionExitObservation.ObserveMatchingAsync`; `StandingRestartAccountingTests.Real_failures_cap_escalate_and_reset_only_after_healthy_completion` (`:24-78`) drives three charged failures then a healthy reset with a `MutableTimeProvider`. `AgentSessionRuntimeTests` and `SessionContextUsagePersistenceTests` (`:47-60`) construct a real `AgentSessionRuntime` over the shared Postgres. `SessionMessageQueueDeliveryVerificationTests.CreateHarnessAsync(alwaysOn)` (`:35`) builds a `BridgeQueueHarness` with a real runtime singleton (`BridgeQueueHarness.cs:39,136`) and `ConfigureDbContext` (`:61`). `StandingRestartAccountingTests` already injects `SaveChangesInterceptor` failures (`:334`). | Every slice has an existing harness; no new fixture. |

## Decisions

### D-1. Replace U+0000 at the persist boundary

Add to `ColumnText` (`server/Application/Services/ColumnText.cs`):

```csharp
/// <summary>Postgres text cannot hold U+0000 (SQLSTATE 22021); the only choices are to drop the row or
/// mark the byte. Same instance back when there is nothing to do.</summary>
public static string? WithoutNul(string? text) =>
    text is null || text.IndexOf('\0') < 0 ? text : text.Replace('\0', '�');
```

and a private `Sanitize(SessionRunnerTranscriptEvent e)` in `AgentSessionRuntime` that returns `e` unchanged
when no string member contains `'\0'`, else `e with { Text = …, ToolInput = …, Kind, Uuid, ParentUuid, Role,
ToolName, ToolUseId, StopReason, ApiCallId, ApiErrorClass, Model = WithoutNul(…) }`. `PersistTranscriptAsync`
applies it to `entries` **first** (`:800`, before `incomingUuids` at `:819`), so dedup keys, `IsTurnBoundary`,
`IsManualCompactBoundary` and the stored row all see the same value. `IsUnseenTurnBoundaryAsync` (`:493`)
and `ToTranscriptPayload` (`:918`, SignalR) keep the raw event: a NUL in `Uuid`/`Kind` is theoretical
(Claude generates both), and the SignalR JSON serializer escapes NUL correctly.

Why U+FFFD and not deletion: the replacement character is the Unicode marker for "a code unit was here
and could not be represented"; deletion silently changes text that delivery confirmation and report
extraction match against. Why not the runner: the constraint is Postgres's, and the runner's JSONL
contract (`docs/session-runtime-invariants.md`) is "verbatim"; the `.ansi.log` and the SignalR feed
keep the byte.

Rejected:

- **A global EF value converter on every string column.** `ColumnText`'s own doc rejects silent
  global transforms; the persist method is the one Postgres-bound transcript writer.
- **Strip in `TranscriptTailer`.** Changes the runner's wire contract for a server storage limitation
  and does not cover `CatchUp`/`Sync` snapshots from an older runner.
- **Base64 / JSON-escape the text when it contains NUL.** Every reader of `TranscriptEntries.Text`
  (delivery matcher, report extraction, channel replies, UI) would need to decode.

### D-2. Per-row fallback: one unpersistable row costs one row

In `PersistTranscriptAsync`, keep building the `TranscriptEntry` list exactly as today, but collect
the rows in a local `List<(TranscriptEntry Row, SessionRunnerTranscriptEvent Source)>` alongside
`db.TranscriptEntries.Add`. Wrap the batch save (`:903`) as:

```csharp
try { await db.SaveChangesAsync(); }
catch (DbUpdateException batchFailure)
{
    db.ChangeTracker.Clear();
    stored = await PersistRowsIndividuallyAsync(db, sessionId, rows, batchFailure);
}
```

`PersistRowsIndividuallyAsync` re-adds and saves each row in source order:

1. Save succeeds → keep the row's flags (turn boundary, assistant text, manual compact boundary) and its
   `Sequence` in the result.
2. `DbUpdateException` whose innermost `DbException.SqlState` is `23505` (unique violation: a concurrent
   catch-up already stored this `(AgentSessionId, Sequence)` or uuid) → `ChangeTracker.Clear()`, skip
   silently at Debug; the row is present.
3. Any other `DbUpdateException` → `ChangeTracker.Clear()`, retry once as a **stub**: same `Id`,
   `AgentSessionId`, `Sequence`, `Kind`, `Uuid`, `ParentUuid`, `Timestamp`, `Role`, usage and error fields;
   `Text = $"[transcript text not persistable: {sqlState ?? exceptionTypeName}]"`, `ToolInput = null`,
   and the bounded columns (`Kind` 40, `Uuid` 64, `ParentUuid` 64, `Role` 40, `ToolName` 200, `ToolUseId`
   120, `StopReason` 60) passed through `ColumnText.Clip`. A stub is lossy by definition; clipping there
   is not the global truncation `ColumnText` warns against. If the stub also fails → skip the row and log
   one Warning per row: session id, sequence, uuid, kind, SqlState, exception type. Never the text.
4. Every stubbed or skipped row records a persist-failure mark (D-3) with the SqlState.

`PersistResult` is computed from the rows that actually landed (including stubs): `LastStoredSeq` is the
max stored sequence, and `AddedTurnBoundary` / `AddedAssistantText` / `AddedManualCompactBoundary` are
true only for landed rows. `IsManualCompactBoundary` is evaluated on the **source** text so a stubbed
manual boundary still flushes the queue; a stubbed `UserPrompt` cannot be text-matched (D-3 covers the
kill), and a stubbed `AssistantText` cannot become a channel reply (accepted; the alternative is losing
the whole turn).

The outer `catch (Exception)` (`:911-915`) stays for non-`DbUpdateException` failures (connection,
FK precheck, scope) and now also records the D-3 mark. A `DbUpdateException` from the **stub** path
that is `DbException { IsTransient: true }` is treated like the outer catch (mark and return what
landed so far) rather than skipping the row: a transient failure is not evidence about the row.

Rejected:

- **Savepoints inside one transaction.** EF Core's `SaveChanges` opens and commits its own
  transaction per call; the per-row loop is the same number of round trips as savepoints and needs no
  transaction management in the runtime.
- **Drop the batch save and always persist per row.** A typical catch-up snapshot is thousands of
  rows; one batch round trip is the normal path and stays.
- **Mark the runner-side entry as poisoned so it stops re-sending.** The runner has no persistence
  state and the server's dedup already stops re-sends once a row (or its stub) is stored.

### D-3. A failing transcript store withholds the delivery kill

`AgentSessionRuntime` gains `ConcurrentDictionary<Guid, TranscriptPersistFailure> _persistFailures`
with `internal sealed record TranscriptPersistFailure(DateTime LastFailedAtUtc, int Failures, string Detail)`
(`Detail` is a SqlState or exception type name, never text) and:

- `RecordTranscriptPersistFailure(Guid sessionId, string detail)` — called from the D-2 stub/skip arms
  and the outer catch; increments `Failures`, stamps `LastFailedAtUtc` from `_timeProvider`.
- `TryGetTranscriptPersistFailure(Guid sessionId, out TranscriptPersistFailure failure)` — `internal`,
  read by the queue service.
- A fully clean save (batch succeeded, or per-row loop with zero stubs and zero skips) removes the entry.
  `DisposeSessionAsync` (`:1173-1174` region) removes it with `_lastSequences`.

In `HandleDeliveryFailureAsync` (`SessionMessageQueueService.cs:3729`), after the grace look and before
the CARD-0103 refund block (`:3833`), add a second refund arm with the same shape:

```csharp
// CARD-0561: NoTranscriptRecord means "no UserPrompt row", and a UserPrompt row cannot exist when
// this session's transcript store has been refusing rows. That is a blind matcher, not a dead
// session: withhold the kill, refund the attempt, and let the stranded sweep retry once persistence
// recovers. Same refund shape as CARD-0103; same all-or-nothing rule for the kill.
var persistBlind = verdict == DeliveryVerdict.NoTranscriptRecord
    && _runtime.TryGetTranscriptPersistFailure(sessionId, out var persistFailure)
    && reverting.All(m => m.SentAt is { } sent && persistFailure.LastFailedAtUtc >= sent - clockTolerance);
```

where `clockTolerance` is 30 s, the same allowance `AgentSessionService.BootConfirmClockTolerance`
(`:932`) already grants between a typed prompt and its transcript timestamp. When `persistBlind`: `DeliveryAttempts--` for
every reverting row (never below 0), `kill` gets `&& !persistBlind`, and the `DeliveryVerificationFailed`
incident text becomes `"Message delivery could not be verified: {Describe(verdict)}. Transcript
persistence for this session failed {n} time(s), last at {t:u} ({detail}); the matcher was blind, so the
session was NOT restarted and the attempt was refunded."` at Warning (Error when channel-bound, as
today). The message stays Pending; no park, no cancel.

The `SentAt` comparison is what keeps this narrow: a persist failure **before** the attempt was typed
does not excuse a later verdict, and a mark is removed by the next clean save, so a session whose store
recovered mid-window is judged on the usual evidence.

Rejected:

- **A new `DeliveryVerdict.TranscriptStoreUnavailable`.** Touches the enum, `Describe`, the charge
  logic, the client union type and the queue UI for a state that is fully described by the incident
  text; the refund arm reuses the CARD-0103 shape that already exists for "verdict that is not evidence".
- **A durable `AgentSession.TranscriptPersistFailedAt` column.** Durable across restarts, but the mark
  is re-established within seconds of a restart by the pump's backfill sync (`SessionRunnerEventPump.cs:44`)
  before any flush can run, and a column adds a migration plus a write per failure for a transient signal.
- **Fall back to the runner's JSONL for confirmation.** "Treat transcript-confirmed UserPrompt evidence
  as the delivery verdict" (AGENTS.md) is a statement about stored rows; a second matcher over the raw
  file is a second contract.

### D-4. A repeated failed resume becomes a continuity decision, not an automatic Fresh

**New state.** `AgentSupervisionState.ContinuityResumeFailures` (int, default 0), migration
`AddContinuityResumeFailures` (CLI-generated, `docs/project-context.md:125`). Exposed on the supervision
DTO next to `RestartBackoffFailures` (`AgentDtos.cs:256`, `AgentService.cs:234`).

**Charging.** `RestartFailurePolicy.Charge` adds one line: `Unknown` and `LaunchOrProcessFailure`
increment `ContinuityResumeFailures`; `Infrastructure` and `ContinuityUnavailable` do not. Every
existing charge site (`Observe` at `AgentSupervisorService.cs:217`, `Charge` at `:364`, the model-hold
arm at `:357`) inherits it; capacity waits and Herdr holds return before charging and stay outside.

**Resetting.** To 0 in the healthy-uptime block (`:154-171`, next to `RestartBackoffFailures = 0`) and in
`AcceptAsync`'s `!automatic` branch (`AgentControlService.cs:633`): a human-accepted Start (plain, Fresh,
`resumeSessionId`, `retryContinuity`) is a new episode. **Not** in `StandingContinuityState.Clear`,
which runs for every accepted launch including supervised resumes (ground truth).

**Tripping.** New `SupervisionSettings.ResumeFailureHoldAttempts` (int, default 5; `0` disables and
restores today's never-give-up ladder). After each charge in `SuperviseAsync`'s scheduling branch
(`:203-246`) and in `RecordStartFailureAsync` (`:313-385`), a private
`TryHoldRepeatedResumeFailureAsync(agent, state, sessionId, ct)`:

1. Returns false when the setting is 0, the count is below it, or `ContinuityHeldAt` is already set.
2. Records the ordinary Crash / StartFailure incident first (unchanged), then calls
   `StandingContinuityState.HoldAsync(agent.Id, sessionId, StandingContinuityReason.RepeatedResumeFailure,
   detail, ct)` — `HoldAsync` gains an optional `string? detail` appended to its evidence sentence and
   clipped with `ColumnText.Clip(…, 1000)`. Detail: `"{n} consecutive supervised resumes ended before
   healthy uptime; last outcome {Status}/{RestartFailureKind}; see the Crash incidents on this agent."`
   No `FailureReason` text (it can carry provider output; `HoldAsync`'s own comment).
3. Skips `RestartScheduled` and `EscalateIfTierCrossedAsync` for this tick (`HoldAsync` already nulls
   `NextRestartAt`), publishes `AgentChanged`, returns true.

Everything downstream already exists: `SuperviseAsync` returns at `:146` while held; Attention shows
`StandingContinuityDecision` at Error with OpenAgent/OpenDrawer; Start refuses with
`standing_continuity_held` until `retryContinuity` (same target, count reset by the `!automatic` branch),
`resumeSessionId` or `fresh:true`; `An_unsuccessful_explicit_retry_restores_the_same_continuity_hold`
already pins that a failed retry re-holds. `StandingContinuityReason` gains `RepeatedResumeFailure`
(`RestartFailureKind.cs:11-17`); the client union (`agents.ts:260`) gains the member and
`StandingSessionRecovery.tsx:26-30` gains the copy: *"Antiphon resumed this conversation repeatedly
without it staying up. Its history is intact. Retry after repair, select an owned conversation, or
start fresh."*

**Why 5.** Wave 1 (investigation) needed three failed attempts and succeeded on the fourth; a
threshold of 5 leaves that self-healing shape untouched. In wave 2 the fifth non-infrastructure failure
was attempt 6 at ~04:14 UTC, 9 minutes after ignition, against the operator's Fresh at 05:31. Five
charged failures cost at least 5+10+20+40+80 s of ladder plus five attempt durations, so a false hold
needs roughly ten minutes of consecutive non-infrastructure failure — the same window an operator would
want paged on anyway.

**Why a hold and not auto-Fresh (the brief's option 2b).**

- CARD-0466 D-1 (`docs/superpowers/plans/2026-09-09-card-0466-standing-session-continuity-plan.md:55-78`)
  removed every count-authorised Fresh after the 57P03 incident and pinned it with tests; "a repeated
  inability to launch does not demonstrate irrecoverable history." This incident does not add positive
  evidence: the resumes died of a blind matcher (fixed by D-1..D-3) and an RC menu race, and the only
  candidate positive signal (463 % fullness) is suspect (ground truth). Auto-Fresh here would be
  count-authorised Fresh under a new name.
- A Fresh keeps the old row and JSONL (CARD-0466 D-4), so what it costs is the standing agent's working
  memory. For the orchestrator that memory is the in-flight pipeline state; an operator choosing Fresh
  can first read the held conversation, and the hold's Attention row is the prompt to do so.
- The hold pages **earlier and louder** than today's ladder: an Error Attention row at attempt 6 instead
  of a Warning `BackoffEscalated` at attempt 11 (~1 h 20 m in).
- Availability cost: an AlwaysOn agent under a self-healing-but-slow outage (Claude CLI broken for an
  hour, classified `LaunchOrProcessFailure`) now waits for a human instead of coming back on the next
  ladder tick. `ResumeFailureHoldAttempts = 0` restores the old behaviour per deployment.

**The one-site flip if the decision goes to auto-Fresh.** In `TryHoldRepeatedResumeFailureAsync`,
replace the `HoldAsync` call with `_control.StartAsync(agent.Id, new StartAgentRequest(Fresh: true,
IgnoreSubscriptionQuota: …), ct, automatic: true)` after lifting the `automatic && request.Fresh`
validation at `AgentControlService.cs:143` for this caller, and record `StandingFreshSelected` with a
message naming the count. Tests V-8..V-10 would assert the new row/ID instead of the hold, and
`Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff` keeps passing because 57P03 is
`Infrastructure` and never charges the new counter. Everything else in this plan is unchanged.

Rejected alongside:

- **Honour `FreshAfterResumeFailures` (brief option 2a).** Reverses CARD-0466 D-1 outright and deletes its
  pinning test; the deprecated property stays deprecated.
- **Only a new Attention row, ladder continues.** "Not silent" but still "forever"; the card asks for the
  loop to end, and the hold is the existing shape for that.
- **Auto-Fresh gated on fullness ≥ 100 %.** The one reading in this incident is implausible for its
  ceiling; a destructive automatic decision cannot rest on a diagnostic the code itself only logs.
- **Count `RestartBackoffFailures` instead of a new counter.** It includes `Infrastructure`, which
  CARD-0466 forbids from authorising anything beyond pacing.

## Implementation slices

| Slice | Files / changes | Test targets (class filters) |
|---|---|---|
| S1 — NUL replacement (D-1) | `server/Application/Services/ColumnText.cs` (`WithoutNul`); `server/Application/Services/AgentSessionRuntime.cs` (`Sanitize`, applied at the top of `PersistTranscriptAsync`). | `ColumnTextTests` (extend, `tests/Antiphon.Tests/Application/ColumnTextTests.cs`); `AgentSessionRuntimeTests`: persist an event whose `Text` is the investigation's literal `"No Instance(s) Available.\r\r\n\r\0\n\0"` through `ObserveTranscriptAsync` and read the row back. |
| S2 — per-row fallback (D-2) | `AgentSessionRuntime.cs`: `PersistRowsIndividuallyAsync`, stub construction, result computed from landed rows, outer catch marks. | `AgentSessionRuntimeTests`: a batch with one row whose `ToolName` is 201 chars lands every other row and a clipped stub; a `SaveChangesInterceptor` that throws a non-transient `DbUpdateException` for one uuid only; a `23505` race is skipped silently. |
| S3 — blind-matcher refund (D-3) | `AgentSessionRuntime.cs` (`_persistFailures`, record/try-get/clear, dispose); `SessionMessageQueueService.cs` (`persistBlind` arm in `HandleDeliveryFailureAsync`). | `AgentSessionRuntimeTests` (mark set by S2 skip and outer catch; cleared by a clean save); `SessionMessageQueueDeliveryVerificationTests` (mark newer than `SentAt` → no kill, attempt refunded, incident names the failure; mark older than `SentAt` → today's kill). |
| S4 — bounded resume loop → hold (D-4) | `server/Domain/Entities/AgentSupervisionState.cs`, `server/Domain/Enums/RestartFailureKind.cs` (`RepeatedResumeFailure`), `server/Migrations/<ts>_AddContinuityResumeFailures.cs`, `server/Application/Settings/SupervisionSettings.cs` (`ResumeFailureHoldAttempts`, doc-comment on the ladder), `server/Application/Services/RestartFailurePolicy.cs`, `AgentSupervisorService.cs` (reset + trip in both charge paths), `StandingContinuityState.cs` (`detail`), `AgentControlService.cs:633` (reset), `AgentDtos.cs` / `AgentService.cs` (DTO field), `client/src/api/agents.ts`, `client/src/features/agents/StandingSessionRecovery.tsx` (+ `.test.tsx`). | `RestartFailureClassificationTests` (charge table); `StandingRestartAccountingTests` (trip at N with `Unknown`/`LaunchOrProcessFailure`, never with `Infrastructure`; reset on healthy uptime and on `!automatic` accept; `0` disables); `StandingContinuityAttentionTests` (new reason renders); `StandingContinuityRecoveryTests` (`retryContinuity` clears count and re-holds on the next N); `pwsh -File scripts/test-client.ps1` for the recovery component test. |
| S5 — owner docs | `docs/session-runtime-invariants.md` § Standing conversation continuity (bounded resume paragraph, `ResumeFailureHoldAttempts`, new reason) and a new Gotcha #91 (NUL/per-row/blind-matcher); `docs/ops-http.md` (supervision DTO field, reason value); `SupervisionSettings.cs` summary line ("never gives up" → "never gives up below `ResumeFailureHoldAttempts`"). Historical plans and the 2026-07-20 spec are not rewritten. | Doc-only. |

Order: S1 → S2 → S3 → S4 → S5. S1 alone closes the ignition; S2 and S3 are independent of S4 and can land
before the D-4 decision is taken.

Verification commands (build to an alternate output because the daemons hold `bin/`):

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c561/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c561/ -- --treenode-filter "/*/*/AgentSessionRuntimeTests/*"
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c561/ -- --treenode-filter "/*/*/SessionMessageQueueDeliveryVerificationTests/*"
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c561/ -- --treenode-filter "/*/*/(RestartFailureClassificationTests*)|(StandingRestartAccountingTests*)|(StandingContinuityAttentionTests*)|(StandingContinuityRecoveryTests*)/*"
pwsh -File scripts/test-client.ps1
```

Delete every `bin-c561/` directory afterwards.

## Acceptance table (guard granularity, for TestDesign)

| Guard | Case | Expected |
|---|---|---|
| G-1 D-1 | `ObserveTranscriptAsync` with the investigation's literal text (two `\0`) | Row stored; `Text` has U+FFFD at both positions; no `Failed to persist` log; `LastStoredSeq` set. |
| G-2 D-1 | Same text in `ToolInput`, `ToolName`, `Uuid` | All stored; dedup on the sanitised uuid holds on a second delivery of the same event (one row). |
| G-3 D-1 | Text without NUL | The exact same string instance reaches the row (no copy). |
| G-4 D-2 | Batch of UserPrompt + ToolCall(`ToolName` 201 chars) + TurnEnd | UserPrompt and TurnEnd stored verbatim; ToolCall stored as a stub with clipped `ToolName` and marker `Text`; `AddedTurnBoundary` true; one Warning naming sequence/uuid/kind/SqlState `22001`, no text. |
| G-5 D-2 | Interceptor throws non-transient `DbUpdateException` for one uuid on batch **and** stub | That row skipped; siblings stored; result flags reflect landed rows only. |
| G-6 D-2 | Interceptor raises `23505` for one row | Row skipped at Debug; no mark; no stub attempted. |
| G-7 D-2 | Interceptor throws `DbException { IsTransient: true }` on the stub | Loop stops; landed rows reported; mark recorded; no skip Warning for that row. |
| G-8 D-3 | After G-5 | `TryGetTranscriptPersistFailure` true with `Failures = 1`, `Detail` = SqlState; after a subsequent clean save → false. |
| G-9 D-3 | `HandleDeliveryFailureAsync(NoTranscriptRecord)`, AlwaysOn, idle, mark `LastFailedAtUtc ≥ SentAt` | No `KillGenerationAsync`; `DeliveryAttempts` refunded to its pre-attempt value; message Pending; one `DeliveryVerificationFailed` Warning whose message contains "matcher was blind" and the SqlState. |
| G-10 D-3 | Same, mark older than `SentAt − tolerance` | Today's behaviour: kill, attempt charged. |
| G-11 D-3 | Same, verdict `NoComposerEvidence` with a mark | CARD-0103 rules apply unchanged; the new arm does not fire. |
| G-12 D-4 | `Charge(Unknown)`, `Charge(LaunchOrProcessFailure)` | `ContinuityResumeFailures` +1 each; `Charge(Infrastructure)`, `Charge(ContinuityUnavailable)` leave it. |
| G-13 D-4 | Five consecutive `Unknown` outcomes via `Observe`/`RecordStartFailureAsync`, `ResumeFailureHoldAttempts = 5` | Fourth failure: `RestartScheduled` as today, no hold. Fifth: Crash incident, then `ContinuityHeldAt` set, `ContinuityReason = RepeatedResumeFailure`, evidence names the count, `NextRestartAt` null, no `RestartScheduled`/`BackoffEscalated` for that tick, `StandingContinuityHeld` incident, `AgentChanged` published. Next ticks make no Start attempt. |
| G-14 D-4 | Five `Infrastructure` outcomes (wrapped 57P03) | Ladder grows, never holds; `Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff` still green. |
| G-15 D-4 | Four `Unknown` then healthy uptime reset | Counter 0 with the other counters. |
| G-16 D-4 | Held; operator `retryContinuity:true` | Accepted; counter 0; hold cleared; a further five failures hold again (`An_unsuccessful_explicit_retry_restores_the_same_continuity_hold` shape). |
| G-17 D-4 | `ResumeFailureHoldAttempts = 0` | Twenty `Unknown` outcomes never hold; ladder and escalation exactly as today. |
| G-18 D-4 | Attention | `BuildStandingContinuityItemsAsync` renders the new reason at Error with both actions; client union accepts it and the recovery component shows the new copy. |
| G-19 D-4 | Supervised resume accepted (`automatic: true`) | `Clear` runs, counter **unchanged**. |

## Out of scope (follow-on cards, not folded)

- **Remote-control modal hold not honoured on a resumed generation** (investigation uncertainty 1:
  `IsModalBlockedLockedAsync` at `SessionMessageQueueService.cs:4405-4416` keys the open episode on
  `AcceptedStartedAt`; boot typed 30 s after `menu=true`). Contributing in every wave-2 attempt, causal in
  none; needs its own card against CARD-0514.
- **Context fullness above 100 % on a 200 k ceiling** (`SessionContextUsage.cs:66-105`): either the
  ceiling for the running model or the occupancy accounting for that row is wrong. Diagnostic only today.
- **Persist retries against an exited session for hours** (uncertainty 4): moot under D-1/D-2 for
  per-row causes; whether the tailer should unbind on exit is a runner question.
- **Which tool wrote `No Instance(s) Available` with NULs** (uncertainty 3): a Windows WMI/`wmic` shape;
  immaterial once D-1 lands.

--- next stage ---
next: decide
handoff: Plan for CARD-0561 is written under D-4 (bounded resume loop ends in the CARD-0466 continuity hold at ResumeFailureHoldAttempts=5, no auto-Fresh), which contradicts the brief's option 2; accept D-4 and dispatch test-design, or flip D-4 to auto-Fresh (one call site, documented in the plan) and dispatch test-design with that change.
artifact: docs/superpowers/plans/2026-09-18-card-0561-poisoned-transcript-resume-loop-plan.md
