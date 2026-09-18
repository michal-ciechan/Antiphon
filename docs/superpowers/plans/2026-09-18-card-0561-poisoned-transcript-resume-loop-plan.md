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

## Verification design (TestDesign, task c96af23c)

TestDesign dispatch 2026-09-18 on checkout `c184dba8` (`feat/card-task-c96af23c`: master `627d448f` plus the
plan commit `566addea` cherry-picked). Decision taken before this stage: **D-4 as written** (the resume-failure
loop ends in the CARD-0466 continuity hold, reason `RepeatedResumeFailure`, `Supervision:ResumeFailureHoldAttempts`
default 5, Attention at Error; no auto-Fresh). Nothing below changes D-1..D-4; it finalizes the controls Code
implements, records the seams the tests need, and splits the plan's acceptance rows where one row bundled two
independently bypassable checks. Stage rules every row follows:

1. IDs are `G-561-n` / `PC-561-n` / `V-561-n` / `R-561-n` / `DL-561-x`. The plan's G-1..G-19 keep their numbers with
   the `561` infix; bundled rows are split with a letter suffix (2a/2b, 4a/4b/4c, 8a/8b/8c, 9a/9b/9c, 12a/12b,
   13a/13b/13c, 18a/18b/18c). G-561-20..27 are new. `PC-561-n` maps 1:1 to `G-561-n`; two PCs may share a test
   method only when they break different code and go red at different named assertions.
2. Every C# case runs the real `AgentSessionRuntime`, `SessionMessageQueueService`, `AgentSupervisorService`,
   `AgentControlService`, `RestartFailurePolicy`, `StandingContinuityState` and `AttentionService` over the shared
   test Postgres (`TestDbFixture`). The only fakes are the ones the named suites already use: `FakeAgentProtocolAdapter`
   (the terminal), `MockEventBus`, `AgentSupervisionTests.MutableTimeProvider` (an offset over the real clock, never
   frozen), `BridgeQueueHarness.EmptyRunnerClient`, the `QueueAdapterFactory` adapter queue, and EF interceptors of
   the shapes `StandingRestartAccountingTests` already carries (`SaveChangesInterceptor` / `DbCommandInterceptor`).
   Postgres errors are **real** wherever the schema can produce them: 22021 (NUL, the ignition), 22001 (a 201-char
   `ToolName` against `varchar(200)`), 23505 (a racing row on the unique `(AgentSessionId, Sequence)` index).
   Only the "row fails for a reason the schema cannot produce" (`XX000`), the transient `DbException` and the
   non-database throw are synthesised by an interceptor, and each such row says so.
3. The transcript row is the delivery verdict. Where a test's subject is the queue (D-3) the recipient evidence is
   the session's `UserPrompt` transcript record for the body (the harness's `OnSubmitted` writes it stamped, as every
   `SessionMessageQueueDeliveryVerificationTests` case relies on); a `Pending` row, a refunded counter or an
   incident is a substitute and is labelled as such where it is the only assertion.
4. Time: D-3's `SentAt` versus mark arithmetic is exercised by seeding `SentAt` through
   `SeedPendingMessageAsync(lastDeliveryStartedAt:)` against a `MutableTimeProvider` shared by runtime and queue
   (`HarnessOptions.TimeProvider`); D-4's ladder is advanced through `h.Clock.Advance` exactly as
   `Real_failures_cap_escalate_and_reset_only_after_healthy_completion` does. Nothing waits on a wall clock except the
   queue harness's compressed 3 s confirm deadline and 3 s post-failure grace, which every existing D-3-shaped test
   already pays.
5. Method-scoped filters: `--treenode-filter "/*/*/<Class>/<Method>"`; Vitest:
   `pwsh -File scripts/test-client.ps1 StandingSessionRecovery`; the client type union is checked by
   `npm run build` in `client/` (`tsc -b`), which Vitest does not do.

### Settled here (the plan left these open or did not say)

- **`PersistResult` is observable.** D-2's contract ("`PersistResult` is computed from the rows that actually
  landed") has no public observer: `PersistTranscriptAsync` and `PersistResult` are `private`. Code makes both
  `internal` (no behaviour change; `Antiphon.Server.csproj` already has `InternalsVisibleTo Antiphon.Tests`, and the
  runtime already exposes `IsTurnBoundary` and the D-3 `TryGetTranscriptPersistFailure` the same way). Driving the
  flags through `SyncTranscriptAsync`'s flush side effects would need the queue registered and a queued row per
  assertion; the visibility change is the honest seam.
- **The D-3 arm must read `SentAt` before the revert nulls it.** `HandleDeliveryFailureAsync` sets
  `message.Status = Pending; message.SentAt = null` for every reverting row (`SessionMessageQueueService.cs:3803-3807`)
  *before* the CARD-0103 block at `:3833` where the plan places `persistBlind`. Written as quoted,
  `reverting.All(m => m.SentAt is { } sent && …)` is always false and the kill proceeds. Code captures each row's
  `SentAt` (a local dictionary or a `sentAt` snapshot taken from `reverting` before the loop) and evaluates the
  arm against that. V-561-9 is red if this is missed, so no separate guard is needed; it is recorded so Code does
  not rediscover it.
- **Blind-refund incident dedup.** The plan says the refund arm has "the same shape" as CARD-0103. That includes its
  one-Warning-per-message rule (`alreadyReported` since the oldest refunded `CreatedAt`): two blind verdicts on the
  same message produce one `DeliveryVerificationFailed` row, not two. V-561-28's still-failing arm pins it.
- **Threshold count for G-17.** Twelve outcomes, not twenty: the trip point is 5 and the hourly escalation tier is
  crossed after the 10th charged failure (`5·2^10 = 5120 s ≥ 3600 s`), so twelve outcomes prove both "never
  holds" and "escalation exactly as today"; eight more add wall time and nothing else.
- **Which failures drive D-4.** The `Observe` path is driven by queued `FakeAgentProtocolAdapter { ThrowOnStart =
  new InvalidOperationException(...) }` (the launch catch stamps `RestartFailureKind = Classify(...) = Unknown`, the
  next tick's `Observe` charges it) after an initial healthy session whose `ProcessExited` exit charges
  `LaunchOrProcessFailure`; both counted kinds therefore appear in every D-4 run. The `RecordStartFailureAsync` path
  is driven by `StandingRestartAccountingTests.CompositionFailure { Failure = () => new InvalidOperationException(...) }`
  (thrown inside `StartAsync`, classified `Unknown`); Infrastructure uses its default wrapped 57P03. Every adapter
  queue ends with a healthy **sentinel** adapter whose `Started.ShouldBeFalse()` is the "no further attempt"
  assertion, because an exhausted `QueueAdapterFactory` throws `InvalidOperationException`, which would itself be
  charged as `Unknown` and could mask the hold.
- **Stub marker and Warning text.** `Text = "[transcript text not persistable: {SqlState ?? exception type name}]"`;
  the per-row Warning names `sessionId`, the stored `Sequence`, `Uuid`, `Kind`, SqlState and exception type, never
  `Text` or `ToolInput`. Tests pin the marker prefix, the SqlState token and the absence of a sentinel substring
  planted in the source `ToolInput`; Code may word the rest.
- **`HoldAsync(detail)` clip** is `ColumnText.Clip(evidence + " " + detail, 1000)` on the whole evidence sentence,
  so the stored value always fits the 1000-char column regardless of the sentence's own length.

### Inspection

Bodies read in full at `c184dba8` (line counts as inspected):

- `tests/Antiphon.Tests/Application/ColumnTextTests.cs` (66): `[Category("Unit")]`, five `Clip`/`ClipOrNull`
  cases (budget includes the marker, surrogate pair never split, zero budget, null survives). Boundaries -> V-561-3
  adds `WithoutNul` (null, empty, clean same-instance, one NUL, two adjacent NULs, NUL at index 0 and at the end).
- `tests/Antiphon.Tests/Application/AgentSessionRuntimeTests.cs` (894): `[Category("Integration")] [Category("Slow")]`,
  not `partial`; runtime built inline (`new AgentSessionRuntime(MockEventBus, Options.Create(AgentSessionSettings
  { SessionLogPath }), provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger)` over a
  `ServiceCollection` with `AddDbContext<AppDbContext>(UseNpgsql(TestDbFixture.ConnectionString, MigrationsAssembly,
  SetPostgresVersion(16,0)))`); `Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart`
  (dedup by uuid, rebase `[1,2,3]`), `Error_turn_end_is_acted_on_once_replay_dedups` (uuid replay stores one
  TurnEnd); helpers `TranscriptEvent(sessionId, seq, kind, uuid, text)` (13-arg positional, role `user`),
  `TurnEndEvent(stopReason, sessionId, uuid)`, `SeedRunningSessionAsync`, `CleanupSessionAsync`,
  `DeleteDirectoryBestEffort`, `WaitUntilAsync`, `GetPayloadValue<T>`, `StaticSessionRunnerClient`
  (`GetTranscriptAsync` returns an empty snapshot). Logger is `NullLogger` everywhere. Boundaries -> the class becomes
  `partial`; a new partial `AgentSessionRuntimeTests.Persist.cs` adds a `PersistFixture` (seeded `AgentSession`,
  DI with optional interceptor, `ListLogger<AgentSessionRuntime>` sink, `MutableTimeProvider`, optional
  `ISessionRunnerClient` snapshot for V-561-26, `finally` cleanup of `TranscriptEntries`/`AgentSessions`/log dir)
  and reuses `TranscriptEvent`/`TurnEndEvent`.
- `tests/Antiphon.Tests/Application/SessionContextUsagePersistenceTests.cs` (:47-60): same inline runtime shape,
  confirms the fixture pattern above is the house one.
- `tests/Antiphon.Tests/Application/AgentTaskCatchUpSettlementTests.cs` (:222-262, :268, :305): `RuntimeFor(snapshot,
  logs:)` registers `ILogger<AgentSessionRuntime>` as a private `ListLogger<T>` and a private
  `SnapshotRunnerClient(SessionRunnerTranscriptDto)`; both are private nested and are copied (12 and ~30 lines),
  not shared, into the new partial.
- `tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.cs` (2662): `[Category("Slow")]`,
  not `partial`; `CreateHarnessAsync(alwaysOn)` -> `BridgeQueueHarness.CreateAsync(HarnessOptions { AlwaysOn })`;
  `ObservableHarnessAsync` = one `InsertTurnAsync` so the session is bound and idle;
  `A_deferred_re_check_of_a_row_sent_without_a_retained_generation_declines_the_recovery_kill` is the direct
  `HandleDeliveryFailureAsync(sessionId, [id], NoTranscriptRecord, ct, capturedGeneration:)` shape with
  `SeedPendingMessageAsync(status: Sent, deliveryAttempts: 1, baselineSequence: floor)`, asserting
  `KillGenerationCalls`, `Killed`, `Inputs`, row `Pending`/`SentAt null`/`DeliveryAttempts`; the CARD-0103 block
  (:1511-1670) pins refund-and-withhold on `NoComposerEvidence` and the negative
  `A_session_that_worked_and_then_stalled_still_charges_the_attempt_and_is_killed`;
  `An_idle_always_on_session_with_no_record_is_still_killed` (`SwallowSubmits = 99`) is the kill the new arm must
  leave alone. Boundaries -> the class becomes `partial`; a new partial
  `SessionMessageQueueDeliveryVerificationTests.C561.cs` adds `BlindHarnessAsync(clock, channelBound:)`,
  `PoisonToolCall(sessionId, seq)` (`ToolName` 201 chars, `ToolInput` carries a sentinel substring),
  `StubbedUserPrompt(sessionId, body)` (`Uuid` 65 chars, `Text = body`) and `StartedAtAsync(h)` for
  `capturedGeneration`.
- `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs` (647): `HarnessOptions { AlwaysOn, TimeProvider,
  Supervision, ConfigureDbContext, ConfigureDeliveryVerification, ConfigureServices }`; verification settings
  `TranscriptConfirmTimeoutSeconds = 3`, `PostFailureConfirmGraceSeconds = 3`, `StrandedAgeSeconds = 0`,
  `UnobservableBaselineConfirmClockToleranceSeconds = 30`; `Runtime` and `Queue` are the DI singletons over the
  shared `TimeProvider`; `Adapter.OnSubmitted` (:237-259) inserts a **stamped** `UserPrompt` + unstamped `TurnEnd`
  straight into the DB (`InsertEntryAsync`), so the healthy recipient evidence is a real transcript row but not a
  runtime persist; `SeedPendingMessageAsync` (:403-450) sets `SentAt = lastDeliveryStartedAt ?? created` for
  `Sent`, `LastDeliveryGeneration = Normalize(session.StartedAt)` when attempts > 0; `InsertTurnAsync`,
  `MarkWorkingAsync`, `CurrentTranscriptMaxSequenceAsync`, `BindChannelAsync`; `Adapter.KillGenerationCalls`,
  `Killed`, `Inputs`. Boundaries -> V-561-9/10/11/24/28 use only these.
- `tests/Antiphon.Tests/Application/StandingRestartAccountingTests.cs` (420) + `.Boundaries.cs` (176):
  `[Category("Integration")] [NotInParallel] partial`; `Real_failures_cap_escalate_and_reset_only_after_healthy_completion`
  is the driver shape (adapter queue, `ObserveMatchingAsync(ProcessExited)`, tick/tick/verify/`Clock.Advance(5·2^k+1)`/
  tick/`WaitForIdleAsync`, healthy reset after `HealthyUptimeResetMinutes = 5`);
  `Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff` seeds a Failed row + due `NextRestartAt` and uses
  `CompositionFailure { AgentId, Remaining, Failure }` (`DbCommandInterceptor` on the `AgentBundleAttachments` read
  inside `StartAsync`; default failure `DbUpdateException(PostgresException 57P03)`), asserting
  `ContinuityHeldAt.ShouldBeNull()` and `--resume` afterwards; `Async_infrastructure_failure_is_consumed_once_across_recreation`
  (`ThrowOnStart = PostgresException 57P03`); `OutcomeStorageFailure`/`AfterReservationFailure`/`EarlyRunningFailure`
  interceptors. Boundaries -> new partial `StandingRestartAccountingTests.ResumeFailureHold.cs` reuses
  `CompositionFailure` and adds a private driver `FailSupervisedResumesAsync(h, agent, id, failures, expectHold)`.
- `tests/Antiphon.Tests/Application/AgentSupervisionTests.cs` (843): `BuildHarness(root, adapters, supervision,
  runner, includeModelAvailability, definitionKind, configureDb)` (:585-687; default `SupervisionSettings { TickSeconds
  = 1, BackoffBaseSeconds = 5, HealthyUptimeResetMinutes = 10 }`, `MutableTimeProvider`, `ListLogger<AgentSupervisorService>`
  into `SupervisorLog`), `Harness` record (`Provider`, `Scope`, `Control`, `LaunchQueue`, `EventBus`, `Clock`,
  `SupervisorLog`, `Runner`; `Supervisor()` is a fresh scope), `CreateAlwaysOnAgentAsync`, `CreateContext`,
  `NewTempRoot`, `CleanupAsync`; `Backoff_ladder_reaches_30_day_cap_and_escalates_once_per_tier` (seeded
  `RestartBackoffFailures = 10` crosses the hourly tier once). Boundaries -> V-561-13/15/16/17/19 are built on this
  harness with `definitionKind: "ClaudeCode"` and a `SupervisionSettings` carrying `ResumeFailureHoldAttempts`.
- `tests/Antiphon.Tests/Application/StandingContinuityAttentionTests.cs` (57): one parameterised method over the
  four reasons: seeds a held fixture, `HoldAsync(reason)`, `StopAsync`, builds `AttentionService` twice, asserts one
  `StandingContinuityDecision` item with `OpenAgent`, evidence `<= 1000`, then `RetryContinuity` clears the hold.
  Boundaries -> V-561-18a adds the fifth `[Arguments]`; V-561-20 is a new method in the same class.
- `tests/Antiphon.Tests/Application/StandingContinuityRecoveryTests.cs` (160) + `StandingRecoveryFixture.cs` (60):
  `AgentControlServiceIntegrationTests.BuildHarness` (no supervisor clock), `SeedAsync(legacy, held)`,
  `StartAsync(request)`, `IdleAsync`, `Db()`; `An_unsuccessful_explicit_retry_restores_the_same_continuity_hold`
  (reason stays `NativeSessionMissing`), `Held_retry_selection_and_fresh_have_separate_accepted_decisions`
  (incident text `repaired-target retry`). Boundaries -> carried as R rows; the count-shaped retry (G-16) needs
  the supervisor and lives in `StandingRestartAccountingTests` instead.
- `tests/Antiphon.Tests/Application/RestartFailureClassificationTests.cs` (57): `[Category("Unit")]`;
  `Classify` evidence table, private `SyntheticTransientDbException : DbException { IsTransient => true }`,
  `Terminal_generation_is_charged_once_and_healthy_completion_defines_a_process_failure` (Observe once per
  generation, `ConsecutiveFailures`/`RestartBackoffFailures` arithmetic). Boundaries -> V-561-12 is a new method; the
  existing Observe test gains `ContinuityResumeFailures` assertions (0 after Infrastructure, 1 after
  LaunchOrProcessFailure).
- `tests/Antiphon.Tests/TestHelpers/SessionExitObservation.cs` (30), `tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs`
  (`ThrowOnStart`, `ThrowOnStartFactory`, `ReadyResult`, `StartupOutput`, `Started`, `StartedArgs`,
  `StartedSessionId`, `Killed`, `KillGenerationCalls`, `OnSubmitted`, `Inputs`, `EchoTypedInputToScreen`,
  `SwallowSubmits`).
- `client/src/features/agents/StandingSessionRecovery.test.tsx` (99) and `.tsx` (1-60): fixture `agent` with
  `supervision.continuityReason: 'NativeSessionMissing'`; copy branches for `NativeSessionMissing` and
  `OwnershipUnproven` with a generic fallback; `unproven ownership does not claim history was deleted` is the
  copy-assertion shape. `client/src/api/agents.ts:260` closed union. `attentionVisuals.ts:54` keys on
  `AttentionKind` only (unchanged). Boundaries -> V-561-18b copies the `OwnershipUnproven` case shape.
- Production seams read for the controls: `AgentSessionRuntime.PersistTranscriptAsync` (:797-916), `ObserveTranscriptAsync`
  (:349-362), `CatchUpTranscriptAsync` (:688-700), `SyncTranscriptAsync` (:709-753), `TryRemove`/`DisposeSessionAsync`
  (:1168-1180); `SessionMessageQueueService.HandleDeliveryFailureAsync` (:3729-4010), `IsWorkingAsync` (:4250; any
  non-housekeeping row after the last boundary reads working, so the D-3 poison row is written **before** the idle
  turn); `RestartFailurePolicy` (56); `AgentSupervisorService` scheduling (:203-246), due attempt (:250-300),
  `RecordStartFailureAsync` (:313-385), `EscalateIfTierCrossedAsync` (:465-483); `StandingContinuityState` (45);
  `AgentControlService.StartAsync` guards (:139-160) and `AcceptAsync` (:620-646); `AgentSessionService` launch catch
  (:375-415, `RestartFailureKind = Classify(ex)`), `WaitForReadyOrThrowAsync` (:1913-1926, `InvalidOperationException`
  -> `Unknown`), `KillGenerationAsync` (:1174-1222, generation-equal or no-op); `AppDbContext` column limits
  (:1188-1200) and the unique `(AgentSessionId, Sequence)` index; `AttentionService.BuildStandingContinuityItemsAsync`
  (:1560-1569, reason-agnostic); `AgentService` supervision DTO mapping (:228-235).

Missing setup recorded for Code (all test-side except the first): `PersistTranscriptAsync`/`PersistResult` ->
`internal`; `AgentSessionRuntimeTests` and `SessionMessageQueueDeliveryVerificationTests` -> `partial` with the two
new partial files; `StandingRestartAccountingTests.ResumeFailureHold.cs`; private copies of `ListLogger<T>`,
`SnapshotRunnerClient`, `SyntheticTransientDbException` in the runtime partial; three interceptors in the runtime
partial: `RowFailure : SaveChangesInterceptor` (target uuid; throws `DbUpdateException(PostgresException "XX000")`
on every save that tracks an Added `TranscriptEntry` with that uuid, or, in `TransientOnStub` mode, throws
`DbUpdateException(SyntheticTransientDbException)` once the tracked row's `Text` starts with the stub marker;
counts `Hits` per phase), `RacingCatchUp : SaveChangesInterceptor` (on first sight of the target uuid tracked as
Added, inserts a row with the same `AgentSessionId`/`Sequence`/`Uuid`/`Kind` and `Text = "racing copy"` through its
own `AppDbContext`, then lets the save proceed so Postgres raises a real 23505), `NonDatabaseFailure :
SaveChangesInterceptor` (throws `InvalidOperationException` once from `SavingChangesAsync`).

### Delivery inventory

Acceptance is at the recipient. For the one session destination the evidence is the `UserPrompt` transcript record
carrying the body, exactly once, plus the row `Sent`.

| ID / producer -> destination / durable identity | Persistence boundaries and recovery cuts | Observable receipt and tests |
|---|---|---|
| DL-561-A **queued message after a blind verdict** (D-3): confirm loop -> `NoTranscriptRecord` -> `HandleDeliveryFailureAsync` -> grace look (`CatchUpTranscriptAsync`, empty runner) -> revert + **refund** (`Status = Pending`, `SentAt = null`, `DeliveryAttempts--`, verdict stamped) in the one existing `SaveChangesAsync` -> `FlushStrandedQueuesAsync` (60 s sweep; `StrandedAgeSeconds = 0` in the harness) or the next turn-end flush -> session terminal -> `UserPrompt` row. Identity: `SessionQueuedMessage.Id` (+ `Body`, `Sequence`). No new persistence boundary is introduced: the refund shares today's revert transaction. | Cut 1 (crash between verdict and the revert save): the row stays `Sent` with `SentAt`/generation and the attempt charged, exactly as today; the deferred re-check path (`A_deferred_re_check_of_a_row_sent_without_a_retained_generation_declines_the_recovery_kill`, R-561-8) recovers it. Declared substitute: not re-tested here because the card adds no write to that boundary. Cut 2 (store still failing at the sweep): the sweep's own attempt reaches `NoTranscriptRecord` again with a **fresh** mark (the stubbed `UserPrompt` cannot be text-matched, plan D-2) -> refunded again, one incident, never killed (V-561-28 `still-failing`). Cut 3 (recipient busy): the sweep leaves the row (`IsWorkingAsync`), the turn-end flush delivers later (V-561-28 `busy`; R-561-11). Cut 4 (recipient idle, store recovered): the sweep delivers (V-561-28 `recovered`). | Substitute (V-561-9/10/11/24): row `Pending`, `DeliveryAttempts`, `Killed`, `KillGenerationCalls`, incident text prove the decision, not receipt. Recipient evidence (V-561-28 `recovered`): `Adapter.Inputs` carries the body exactly once more, the session has one `UserPrompt` row whose `Text == body` past the pre-sweep max sequence, the row is `Sent` with `DeliveryAttempts == 1` (the successful attempt is charged, the refunded one is not), and there is no `Restarting the session` incident. |
| DL-561-B **transcript line -> `TranscriptEntries` row** (D-1/D-2): runner tailer -> `ObserveTranscriptAsync` (live), `CatchUpTranscriptAsync` (grace), `SyncTranscriptAsync` (pump/backfill) -> `PersistTranscriptAsync` -> one batch `SaveChangesAsync`, per-row on `DbUpdateException`. Identity: `(Uuid, Kind)` per session (sequence-dedup for uuid-less rows). | Cut 1 (whole batch rejected today): the storm, 2 102 retries; under D-1 the NUL row lands once (V-561-1 live, V-561-26 sync); under D-2 a rejected row becomes a stub once and every later snapshot dedups against it (V-561-23). Cut 2 (server dies mid per-row loop, or a transient failure stops it): rows already saved persist; the next snapshot re-sends everything; dedup lets only the missing rows land (V-561-7 second delivery). Cut 3 (concurrent catch-up stored the same row first): real 23505, skipped, present once (V-561-6). | Row or stub present exactly once with the expected `Text`/clipped columns; `PersistResult` reports only landed rows (V-561-4/5/7/22); no `Failed to persist` Warning on the sanitised path (V-561-1/26). |
| DL-561-C **continuity hold -> Attention -> human** (D-4): `TryHoldRepeatedResumeFailureAsync` -> `HoldAsync` (`AgentSupervisionStates` row + `StandingContinuityHeld` incident, one save) -> `AttentionService.BuildStandingContinuityItemsAsync` (read model) -> `AgentChanged` publish (best-effort UI refresh). No asynchronous delivery; the durable identity is the `AgentSupervisionStates.AgentId` row with `ContinuityHeldAt`/`ContinuityReason`. | Cut 1 (hold save fails): `HoldAsync` throws inside the supervisor tick, the tick logs and the next tick re-observes the same dead row (`LastObservedRestartSessionId` unchanged) and re-trips; no test (it is the existing tick contract, `Failed_outcome_save_recovers_as_unknown_without_fresh_authority` shape) — declared. | V-561-13: `ContinuityHeldAt`/`Reason`/`Evidence`/`NextRestartAt`, `StandingContinuityHeld` count 1, `AgentChanged` on `EventBus.PublishedEvents`, DTO `ContinuityResumeFailures == 5`; V-561-18a: Attention item at Error with `OpenAgent` + `OpenDrawer`. |

What the substitutes cannot prove: the fake terminal cannot prove a real pty accepted the keystrokes or that
Claude wrote the record; the healthy arm's `UserPrompt` row is written by the harness's `OnSubmitted` directly, so
"the store recovered" is modelled by the direct insert succeeding, not by a runtime persist of a runner event; a real
concurrent `CatchUpTranscriptAsync` race (two runtimes) is modelled by the interceptor-inserted racing row, which
does produce the real 23505 the code has to classify.

### Proves it works now

Format: `V-561-n: behaviour | layer | test/command | expected`. Layers: U unit, I integration (shared Postgres),
S service-harness (BridgeQueueHarness / supervision harness), C client.

- V-561-1: the investigation's literal tool_result persists with U+FFFD at both NUL positions and the SignalR relay keeps the raw bytes | I | `AgentSessionRuntimeTests.C561_a_NUL_in_tool_result_text_persists_as_U_FFFD` | `PersistFixture` with `logs`; `ObserveTranscriptAsync(TranscriptEvent(sid, 1, ToolResult, "u-nul", "No Instance(s) Available.\r\r\n\r\0\n\0"))`; DB: one row, `Text.ShouldBe("No Instance(s) Available.\r\r\n\r�\n�")`, `Text.ShouldNotContain('\0')`; `logs.ShouldNotContain(l => l.Contains("Failed to persist"))`; `GetPayloadValue<string>(eventBus.PublishedEvents.Single(e => e.EventName == "SessionTranscript").Payload, "text").ShouldContain('\0')` (relay untouched); then `(await runtime.PersistTranscriptAsync(sid, [same event with Uuid "u-nul-2", Sequence 2])).LastStoredSeq.ShouldBe(2)`.
- V-561-2: NUL in `ToolInput`, `ToolName`, `Uuid`, `ParentUuid`, `Role`, `StopReason`, `Model` is replaced in every stored column, and a second delivery of the same raw event dedups on the sanitised uuid without a failure | I | `AgentSessionRuntimeTests.C561_NUL_in_every_string_member_is_replaced_and_the_sanitised_uuid_dedups` | event `Uuid = "u\0id"`, `ParentUuid = "p\0"`, `Role = "us\0er"`, `ToolName = "Ba\0sh"`, `ToolInput = "{\"x\":\"\0\"}"`, `StopReason = "end\0"`, `Model = "m\0"`, `Text = "t\0"`; first persist `LastStoredSeq.ShouldBe(1)`; row: each column equals the U+FFFD form, `Uuid.ShouldBe("u�id")`; second `PersistTranscriptAsync` of the identical raw event: `LastStoredSeq.ShouldBeNull()` (nothing new), rows for the session `ShouldHaveSingleItem()`, `logs.ShouldNotContain(l => l.Contains("Failed to persist"))`.
- V-561-3: `WithoutNul` contract | U | `ColumnTextTests.WithoutNul_replaces_each_NUL_and_returns_the_same_instance_when_clean` | `WithoutNul(null).ShouldBeNull()`; `ReferenceEquals(WithoutNul(""), "")` true; `var s = "clean"; ReferenceEquals(WithoutNul(s), s).ShouldBeTrue()`; `WithoutNul("a\0b").ShouldBe("a�b")`; `WithoutNul("\0\0").ShouldBe("��")`; `WithoutNul("\0x\0").ShouldBe("�x�")`; `WithoutNul("a\0b").Length.ShouldBe(3)` (replacement, not deletion).
- V-561-4: one over-long column costs one row: siblings verbatim, the bad row as a clipped stub, boundary flags from landed rows, one Warning without the text | I | `AgentSessionRuntimeTests.C561_a_row_that_exceeds_a_column_lands_as_a_clipped_stub_and_its_siblings_land_verbatim` | batch `[UserPrompt "u1" "hello", ToolCall "u2" ToolName new string('x', 201) ToolInput "SECRET-INPUT-MARKER {...}", TurnEnd "u3" end_turn]` via `PersistTranscriptAsync`; DB rows ordered by sequence: 3; `rows[0].Text == "hello"`; `rows[2].Kind == TurnEnd && StopReason == "end_turn"`; stub `rows[1]`: `Uuid == "u2"`, `Kind == ToolCall`, `ToolName.Length == 200`, `ToolName.ShouldEndWith("…")`, `ToolName[..199] == new string('x', 199)`, `Text == "[transcript text not persistable: 22001]"`, `ToolInput.ShouldBeNull()`, `Sequence == 2`; result `AddedTurnBoundary.ShouldBeTrue()`, `LastStoredSeq.ShouldBe(3)`; `logs.Where(l => l.StartsWith("[Warning]"))` `ShouldHaveSingleItem()`, that line contains `sid`, `"u2"`, `"ToolCall"`, `"22001"`, `" 2"` (sequence) and `ShouldNotContain("SECRET-INPUT-MARKER")`, `ShouldNotContain(new string('x', 201))`; `runtime.TryGetTranscriptPersistFailure(sid, out var mark).ShouldBeTrue()`, `mark.Failures.ShouldBe(1)`, `mark.Detail.ShouldBe("22001")`.
- V-561-5: a row that fails alone and again as a stub is skipped; the result reflects landed rows only | I | `AgentSessionRuntimeTests.C561_a_row_that_fails_as_a_stub_is_skipped_and_the_result_reflects_landed_rows_only` | `RowFailure { Uuid = "u2" }` (always `XX000`); batch `[UserPrompt "u1", TurnEnd "u2", AssistantText "u3"]`; rows `Select(Uuid).ShouldBe(["u1","u3"])`; `result.AddedTurnBoundary.ShouldBeFalse()`, `result.AddedAssistantText.ShouldBeTrue()`, `result.LastStoredSeq.ShouldBe(3)`; `fault.Hits.ShouldBe(3)` (batch, row, stub — the stub was tried exactly once); one Warning containing `"u2"`, `"skipped"`, `"XX000"`; mark `Failures == 1`, `Detail == "XX000"`.
- V-561-6: a unique violation from a concurrent catch-up is skipped silently: no stub, no mark, the row is present once | I | `AgentSessionRuntimeTests.C561_a_unique_violation_from_a_concurrent_catch_up_is_skipped_without_a_stub_or_a_mark` | `RacingCatchUp { Uuid = "u2" }`; batch `[UserPrompt "u1", UserPrompt "u2" "ours", AssistantText "u3"]`; rows: 3, `Single(r => r.Uuid == "u2").Text.ShouldBe("racing copy")`, `Count(r => r.Uuid == "u2").ShouldBe(1)`; `fault.SightingsOf("u2").ShouldBe(2)` (batch + per-row, never a third stub save); `TryGetTranscriptPersistFailure(sid, out _).ShouldBeFalse()`; `logs.ShouldNotContain(l => l.StartsWith("[Warning]"))`; `logs.ShouldContain(l => l.StartsWith("[Debug]") && l.Contains("u2") && l.Contains("23505"))`; `result.LastStoredSeq.ShouldBe(3)`.
- V-561-7: a transient failure on the stub stops the loop, reports what landed, records the mark, and a later delivery lands the rest and clears the mark | I | `AgentSessionRuntimeTests.C561_a_transient_failure_on_the_stub_stops_the_loop_and_a_later_delivery_recovers` | `RowFailure { Uuid = "u2", TransientOnStub = true }`; batch `[UserPrompt "u1", TurnEnd "u2", AssistantText "u3"]`; first result: `LastStoredSeq.ShouldBe(1)`, `AddedTurnBoundary.ShouldBeFalse()`, `AddedAssistantText.ShouldBeFalse()`; rows `Select(Uuid).ShouldBe(["u1"])` (u3 **not** attempted); mark `Failures == 1`, `Detail.ShouldContain("SyntheticTransientDbException")`; `logs.ShouldNotContain(l => l.Contains("skipped") && l.Contains("u2"))`; then `fault.Armed = false`, redeliver the same batch: `LastStoredSeq.ShouldBe(3)`, `AddedTurnBoundary.ShouldBeTrue()`, rows `["u1","u2","u3"]` with `u2.Text.ShouldBeNull()` (a real TurnEnd, not a stub), `TryGetTranscriptPersistFailure(sid, out _).ShouldBeFalse()` (clean save clears).
- V-561-8c: a non-database persist failure records the mark and returns Empty | I | `AgentSessionRuntimeTests.C561_a_non_database_persist_failure_records_the_mark` | `NonDatabaseFailure` (throws `InvalidOperationException` once); `PersistTranscriptAsync(sid, [UserPrompt "u1"])` returns `LastStoredSeq null`; one Warning `Failed to persist`; mark `Failures == 1`, `Detail == "InvalidOperationException"`; second persist lands `u1` and `TryGet` is false.
- V-561-9: a blind `NoTranscriptRecord` withholds the kill, refunds the attempt, and reports one Warning naming the failure | S | `SessionMessageQueueDeliveryVerificationTests.C561_a_blind_matcher_verdict_withholds_the_kill_and_refunds_the_attempt(int sentAfterMarkSeconds, bool channelBound)` `[Arguments(10,false)] [Arguments(30,false)] [Arguments(10,true)]` | `BlindHarnessAsync(clock)`; `t0 = clock.GetUtcNow().UtcDateTime`; `h.Runtime.ObserveTranscriptAsync(PoisonToolCall(h.SessionId, 1))` (stub + mark at t0, Detail 22001); **then** `InsertTurnAsync("earlier prompt", "earlier answer")` (idle, bound); `floor = CurrentTranscriptMaxSequenceAsync()`; if `channelBound` `BindChannelAsync()`; `id = SeedPendingMessageAsync("the brief", deliveryAttempts: 1, baselineSequence: floor, status: Sent, lastDeliveryStartedAt: t0.AddSeconds(sentAfterMarkSeconds))`; `HandleDeliveryFailureAsync(h.SessionId, [id], NoTranscriptRecord, ct, capturedGeneration: await StartedAtAsync(h))`; `h.Adapter.Killed.ShouldBeFalse()`, `KillGenerationCalls.ShouldBeEmpty()`, `Inputs.ShouldBeEmpty()`; row: `Status == Pending`, `SentAt == null`, `DeliveryAttempts.ShouldBe(0)`, `CanceledAt == null`, `DeliveryVerdict == NoTranscriptRecord`; incidents of kind `DeliveryVerificationFailed` for the agent `ShouldHaveSingleItem()`, `Severity.ShouldBe(channelBound ? Error : Warning)`, `Message.ShouldContain("matcher was blind")`, `.ShouldContain("22001")`, `.ShouldContain("1 time(s)")`, `.ShouldContain("NOT restarted")`, `.ShouldNotContain("Restarting the session")`.
- V-561-10: a mark older than `SentAt - 30 s` does not excuse the verdict: today's kill and charge | S | `SessionMessageQueueDeliveryVerificationTests.C561_a_persist_failure_older_than_the_attempt_does_not_excuse_the_verdict` | as V-561-9 with `lastDeliveryStartedAt: t0.AddSeconds(31)` (`clock.Advance(31 s)` before seeding so `SentAt` is in the harness's present); `h.Adapter.Killed.ShouldBeTrue()`, `KillGenerationCalls.ShouldHaveSingleItem()`; row `DeliveryAttempts.ShouldBe(1)`, `Status == Pending`; incident `Severity == Error`, `Message.ShouldContain("Restarting the session")`, `.ShouldNotContain("matcher was blind")`. (With V-561-9's 30 s arm this pins `>=` at the boundary.)
- V-561-11: the arm fires only for `NoTranscriptRecord` | S | `SessionMessageQueueDeliveryVerificationTests.C561_a_no_composer_evidence_verdict_with_a_mark_keeps_the_card_0103_rules` | same setup as V-561-9 (mark at t0, turn, `SentAt = t0 + 10 s`, baseline non-null) but verdict `NoComposerEvidence`; `Killed.ShouldBeTrue()`, `DeliveryAttempts.ShouldBe(1)`, incident `Severity == Error`, `Message.ShouldNotContain("matcher was blind")`.
- V-561-24: all-or-nothing across a batch: one row within tolerance and one older take the destructive default | S | `SessionMessageQueueDeliveryVerificationTests.C561_a_mixed_batch_takes_the_destructive_default` | two seeded `Sent` rows, `lastDeliveryStartedAt` `t0 + 10 s` and `t0 + 45 s` (after `clock.Advance(45 s)`); `HandleDeliveryFailureAsync(sid, [a, b], NoTranscriptRecord, …)`; `Killed.ShouldBeTrue()`; both rows `DeliveryAttempts == 1`; incident `ShouldNotContain("matcher was blind")`.
- V-561-28: after a refund the message is still delivered through the real queue path | S | `SessionMessageQueueDeliveryVerificationTests.C561_after_a_refund_the_stranded_sweep_delivers_once_the_store_recovers(string shape)` `[Arguments("recovered")] [Arguments("busy")] [Arguments("still-failing")]` | V-561-9 setup (10 s arm) through the refund, `preSweepMax = CurrentTranscriptMaxSequenceAsync()`; `recovered`: `FlushStrandedQueuesAsync` -> `Inputs.Count(i => i == "the brief").ShouldBe(1)`, row `Status == Sent`, `DeliveryAttempts == 1`, DB `TranscriptEntries.Single(t => t.Kind == UserPrompt && t.Sequence > preSweepMax).Text.ShouldBe("the brief")`, `Killed.ShouldBeFalse()`, incidents of kind `DeliveryVerificationFailed` count 1; `busy`: `MarkWorkingAsync()` then `FlushStrandedQueuesAsync` -> `Inputs.ShouldBeEmpty()`, row `Pending`, `DeliveryAttempts == 0`; `still-failing`: `h.Adapter.OnSubmitted = body => h.Runtime.ObserveTranscriptAsync(StubbedUserPrompt(h.SessionId, body), ct)` then `FlushStrandedQueuesAsync` -> `Inputs.Count(i => i == "the brief").ShouldBe(1)`, row `Pending`, `DeliveryAttempts.ShouldBe(0)` (refunded again on the fresh mark), `Killed.ShouldBeFalse()`, `DeliveryVerificationFailed` count `ShouldBe(1)` (dedup), stub `UserPrompt` row present with `Text.StartsWith("[transcript text not persistable")`.
- V-561-12: only non-infrastructure outcomes charge the counter, saturating | U | `RestartFailureClassificationTests.C561_only_non_infrastructure_outcomes_charge_the_resume_failure_counter` | fresh state: `Charge(Unknown)` -> `ContinuityResumeFailures == 1`, `RestartBackoffFailures == 1`; `Charge(LaunchOrProcessFailure)` -> 2 / 2 / `ConsecutiveFailures == 1`; `Charge(Infrastructure)` -> `ContinuityResumeFailures` still 2, `RestartBackoffFailures == 3`; `Charge(ContinuityUnavailable)` -> 2 / 3; `state.ContinuityResumeFailures = int.MaxValue; Charge(Unknown)` -> `int.MaxValue` (no overflow). Plus in `Terminal_generation_is_charged_once_and_healthy_completion_defines_a_process_failure`: `ContinuityResumeFailures.ShouldBe(0)` after the Infrastructure observe and `ShouldBe(1)` after the LaunchOrProcessFailure observe.
- V-561-13: the fifth non-infrastructure outcome via `Observe` holds instead of scheduling; the fourth schedules as today | S | `StandingRestartAccountingTests.C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling` | `BuildHarness(root, [healthy, t1, t2, t3, t4, sentinel], new SupervisionSettings { HealthyUptimeResetMinutes = 5, ResumeFailureHoldAttempts = 5 }, definitionKind: "ClaudeCode")` where `tN = new FakeAgentProtocolAdapter { ThrowOnStart = new InvalidOperationException($"synthetic unknown {N}") }`; manual Start (healthy), `ObserveMatchingAsync(ProcessExited)` (failure 1, `LaunchOrProcessFailure`); driver loops supervised attempts: after each of failures 1..4 `ContinuityResumeFailures.ShouldBe(k)`, `RestartBackoffFailures.ShouldBe(k)`, `ContinuityHeldAt.ShouldBeNull()`, `NextRestartAt.ShouldNotBeNull()`, `RestartScheduled` count `k`; sessions `RestartFailureKind == Unknown` for t1..t4; on the tick after failure 5: `ContinuityHeldAt.ShouldNotBeNull()`, `ContinuityReason.ShouldBe(RepeatedResumeFailure)`, `ContinuitySessionId.ShouldBe(id)`, `ContinuityEvidence.ShouldContain("5 consecutive supervised resumes")`, `ContinuityEvidence.ShouldContain("Unknown")`, `ContinuityEvidence!.Length <= 1000`, `NextRestartAt.ShouldBeNull()`, `RestartScheduled` count still 4, `BackoffEscalated` count 0, `Crash` incidents for the fifth session `>= 1`, `StandingContinuityHeld` count 1 at Info, `h.EventBus.PublishedEvents.Any(e => e.EventName == "AgentChanged" && payload agent id == agent.Id)` after the hold tick; `Supervision.ContinuityResumeFailures.ShouldBe(5)` from `AgentService.GetByIdAsync(agent.Id)`; `h.Clock.Advance(1 day)`, two more ticks, `WaitForIdleAsync`: `sentinel.Started.ShouldBeFalse()`, `RestartScheduled` still 4; `Should.ThrowAsync<ConflictException>(() => h.Control.StartAsync(agent.Id, new(), default))` `.Code.ShouldBe(StandingContinuityState.HeldCode)`.
- V-561-13b: a `StartAsync` that throws charges the same counter and trips the same hold | S | `StandingRestartAccountingTests.C561_a_start_that_throws_charges_the_same_counter_and_trips_the_same_hold` | `Wrapped_57P03` seeding (Failed row, due `NextRestartAt`, `LastObserved*` stamped so `Observe` does not double-charge) with `CompositionFailure { AgentId, Remaining = 5, Failure = () => new InvalidOperationException("synthetic composition failure") }` and settings `ResumeFailureHoldAttempts = 5`; attempts 1..4: `ContinuityResumeFailures == k`, `RestartBackoffFailures == k`, `StartFailure` incidents `k`, `ContinuityHeldAt` null, advance `5·2^k + 1`; attempt 5: `interceptor.Hits == 5`, `StartFailure` count 5 (recorded first), `ContinuityHeldAt` set, reason `RepeatedResumeFailure`, `NextRestartAt` null, `BackoffEscalated` 0; `Clock.Advance(1 day)`, tick: `interceptor.Hits` still 5, `resumed.Started.ShouldBeFalse()`.
- V-561-14: Infrastructure outcomes grow the ladder and never hold | S | `StandingRestartAccountingTests.C561_infrastructure_outcomes_grow_the_ladder_and_never_hold` | `CompositionFailure { Remaining = 6 }` (default wrapped 57P03), settings `ResumeFailureHoldAttempts = 5`, seeded as `Wrapped_57P03`; after six due ticks: `RestartBackoffFailures.ShouldBe(6)`, `ContinuityResumeFailures.ShouldBe(0)`, `ContinuityHeldAt.ShouldBeNull()`, `PersistentSessionId == id`; then a healthy `resumed` adapter: `StartedArgs.ShouldContain("--resume")`, `StartedSessionId == id`. R-561-1 (`Wrapped_57P03`, thresholds 0/1/2/MaxValue) stays green under the default 5.
- V-561-15: healthy uptime resets the counter with the others | S | `StandingRestartAccountingTests.C561_healthy_uptime_resets_the_resume_failure_counter` | adapters `[healthy0, t1, t2, t3, healthyResume]`, `HealthyUptimeResetMinutes = 5`; exit + 3 throws -> `ContinuityResumeFailures.ShouldBe(4)`; the 5th supervised attempt resumes healthily (`healthyResume.StartedArgs.ShouldContain("--resume")`); `Clock.Advance(4 min)`, tick -> still 4; `Clock.Advance(2 min)`, tick -> `ContinuityResumeFailures.ShouldBe(0)`, `RestartBackoffFailures == 0`, `ConsecutiveFailures == 0`, one `Recovered` incident.
- V-561-16: a human-accepted retry resets the counter and clears the hold; the next five hold again | S | `StandingRestartAccountingTests.C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again` | adapters `[healthy0, t1..t4, healthyRetry, t5..t8, sentinel]`; reach the hold as V-561-13; `h.Control.StartAsync(agent.Id, new(RetryContinuity: true), default)`, `WaitForIdleAsync`: `healthyRetry.StartedSessionId == id`, `StartedArgs.ShouldContain("--resume")`, `ContinuityResumeFailures.ShouldBe(0)`, `ContinuityHeldAt.ShouldBeNull()`, `StandingResumeSelected` incident `Message.ShouldContain("repaired-target retry")`; then `ObserveMatchingAsync(ProcessExited)` + t5..t7 (four charged): `ContinuityResumeFailures.ShouldBe(4)`, `ContinuityHeldAt.ShouldBeNull()`; t8 (fifth): held again, reason `RepeatedResumeFailure`, `StandingContinuityHeld` count 2, `sentinel.Started.ShouldBeFalse()`.
- V-561-17: `ResumeFailureHoldAttempts = 0` restores the never-give-up ladder | S | `StandingRestartAccountingTests.C561_a_zero_threshold_restores_the_never_give_up_ladder` | settings `ResumeFailureHoldAttempts = 0`, adapters `[healthy0, t1..t11, sentinel]` (twelve outcomes); after each: `ContinuityHeldAt.ShouldBeNull()`; at the end `RestartBackoffFailures.ShouldBe(12)`, `ContinuityResumeFailures.ShouldBe(12)`, `LastEscalationTier.ShouldBe(1)`, `BackoffEscalated` incidents `ShouldHaveSingleItem()` at Warning, `RestartScheduled` count 12, `NextRestartAt.ShouldNotBeNull()`; `sentinel.Started.ShouldBeFalse()` before the clock is advanced past the 13th delay.
- V-561-18a: the new reason renders at Error with both actions and a retry clears it | I | `StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts` new `[Arguments(StandingContinuityReason.RepeatedResumeFailure)]` | existing assertions: one `StandingContinuityDecision` item, `Severity` Error (add `row.Severity.ShouldBe(AlertSeverity.Error)` and `row.Actions.ShouldContain(AttentionAction.OpenDrawer)` for every arm), `Title.ShouldContain("RepeatedResumeFailure")`, evidence `<= 1000`, hold cleared by `RetryContinuity`.
- V-561-18b: the recovery component explains the new reason without claiming the history is gone | C | `StandingSessionRecovery.test.tsx` `it('repeated resume failure explains the hold without claiming the history is gone')` | render with `continuityReason: 'RepeatedResumeFailure'`; `getByText(/resumed this conversation repeatedly/)` present; `getByText(/history is intact/i)` present; `queryByText(/provider could not find/)` null; `getByRole('button', { name: 'Retry after repair' })` present. Run: `pwsh -File scripts/test-client.ps1 StandingSessionRecovery`.
- V-561-18c: the client union accepts `'RepeatedResumeFailure'` | C | `cd client; npm run build` (`tsc -b && vite build`) | exit 0 with the V-561-18b fixture typed `as AgentSummaryDto` without a cast on `continuityReason`.
- V-561-19: a supervised resume runs `Clear` but leaves the counter | S | `StandingRestartAccountingTests.C561_a_supervised_resume_clears_the_hold_fields_but_not_the_counter` | seed state `ContinuityResumeFailures = 3`, `ContinuityEvidence = "stale-evidence"` (`ContinuityHeldAt` null), Failed session, `LastObserved*` stamped, due `NextRestartAt`; healthy adapter; tick + `WaitForIdleAsync`: `adapter.StartedArgs.ShouldContain("--resume")`, `ContinuityResumeFailures.ShouldBe(3)`, `ContinuityEvidence.ShouldBeNull()` (Clear ran), `ContinuityHeldAt.ShouldBeNull()`.
- V-561-20: `HoldAsync` detail is clipped to the evidence column | I | `StandingContinuityAttentionTests.C561_hold_detail_is_clipped_to_the_evidence_column` | `StandingRecoveryFixture` seeded (not held); `HoldAsync(f.Agent.Id, f.B.Id, RepeatedResumeFailure, new string('d', 2000), ct)` inside `Should.NotThrowAsync`; state `ContinuityEvidence!.Length.ShouldBe(1000)`, `ShouldEndWith("…")`, `ShouldStartWith("Standing conversation")`, `ShouldContain("RepeatedResumeFailure")`; the `StandingContinuityHeld` incident `Message == ContinuityEvidence`; a second `HoldAsync` with a 10-char detail keeps `ContinuityHeldAt` and adds no incident when reason/session are unchanged (existing `changed` rule).
- V-561-21: disposing the session removes the mark | I | `AgentSessionRuntimeTests.C561_disposing_the_session_removes_the_persist_failure_mark` | after V-561-8c's mark, `await runtime.DisposeSessionAsync(sid)`; `TryGetTranscriptPersistFailure(sid, out _).ShouldBeFalse()`.
- V-561-22: a stubbed manual compaction boundary still reports the boundary | I | `AgentSessionRuntimeTests.C561_a_stubbed_manual_compaction_boundary_still_reports_the_boundary` | `RowFailure { Uuid = "u2" }` in `FailOnceThenStub` mode (batch and per-row throw `XX000`, the stub save is allowed); batch `[AssistantText "u1", CompactBoundary "u2" Text "Compacted (manual) summary…"]`; rows: 2, stub `Text == "[transcript text not persistable: XX000]"`; `result.AddedManualCompactBoundary.ShouldBeTrue()` (evaluated on the source), `result.AddedTurnBoundary.ShouldBeFalse()`.
- V-561-23: a redelivered unpersistable row dedups against its stub (the retry storm cannot recur) | I | `AgentSessionRuntimeTests.C561_a_redelivered_unpersistable_row_dedups_against_its_stub` | after V-561-4's batch, deliver `[ToolCall "u2" (same 201-char ToolName)]` twice more via `ObserveTranscriptAsync` and once via `PersistTranscriptAsync`; rows with `Uuid == "u2"` `Count.ShouldBe(1)`; total rows still 3; `logs.Count(l => l.StartsWith("[Warning]")).ShouldBe(1)` (only the first delivery warned); last `PersistTranscriptAsync` returns `LastStoredSeq null`; `TryGetTranscriptPersistFailure(sid, out var m)` true with `m.Failures.ShouldBe(1)` (dedup is not a failure).
- V-561-26: the pump/backfill path (`SyncTranscriptAsync`) over a runner snapshot containing the poison line persists it once and a second sync adds nothing | I | `AgentSessionRuntimeTests.C561_sync_over_a_runner_snapshot_with_the_poison_line_persists_once_and_never_retries` | `PersistFixture(runner: SnapshotRunnerClient(new SessionRunnerTranscriptDto(sid, [UserPrompt "u1" "hi", ToolResult "u2" "No Instance(s) Available.\r\r\n\r\0\n\0", TurnEnd "u3"], 3)))`; `SyncTranscriptAsync(sid)` twice; rows 3, `u2.Text` is the U+FFFD form; `logs.ShouldNotContain(l => l.Contains("Failed to persist"))`; `logs.ShouldNotContain(l => l.Contains("Transcript sync skipped"))`.
- V-561-27: the model and the migration agree | I | `StandingSessionOwnershipTests` (carries `HasPendingModelChanges().ShouldBeFalse()`) | green after `AddContinuityResumeFailures`.

### Guards the regression

- R-561-1: no count-authorised Fresh (CARD-0466 D-1) under the new default threshold | `StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff` (all four thresholds); decisive: `state.ContinuityHeldAt.ShouldBeNull()` on every attempt and `resumed.StartedArgs.ShouldContain("--resume")`.
- R-561-2: three charged non-infrastructure failures still schedule and still reset | `StandingRestartAccountingTests.Real_failures_cap_escalate_and_reset_only_after_healthy_completion`; decisive: `state.ContinuityHeldAt.ShouldBeNull()` at failure 3 and `ConsecutiveFailures.ShouldBe(0)` after the healthy reset.
- R-561-3: the idle AlwaysOn kill without a mark is unchanged | `SessionMessageQueueDeliveryVerificationTests.An_idle_always_on_session_with_no_record_is_still_killed`; decisive: `h.Adapter.Killed.ShouldBeTrue()` and `Message.ShouldContain("Restarting the session")`.
- R-561-4: CARD-0103's negative case is untouched by the new arm | `SessionMessageQueueDeliveryVerificationTests.A_session_that_worked_and_then_stalled_still_charges_the_attempt_and_is_killed`; decisive: `DeliveryAttempts.ShouldBe(1)` and `Killed.ShouldBeTrue()`.
- R-561-5: dedup by uuid and sequence rebase are unchanged under the sanitiser and the per-row loop | `AgentSessionRuntimeTests.Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart` (`Sequence.ShouldBe([1,2,3])`) and `Error_turn_end_is_acted_on_once_replay_dedups` (`ShouldHaveSingleItem`).
- R-561-6: a failed explicit retry re-holds with its own reason, not the new one | `StandingContinuityRecoveryTests.An_unsuccessful_explicit_retry_restores_the_same_continuity_hold`; decisive: `ContinuityReason.ShouldBe(NativeSessionMissing)`.
- R-561-7: the four existing reasons still render once and clear | `StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts` (existing four arms).
- R-561-8: the missing-generation decline is not altered by the blind arm | `SessionMessageQueueDeliveryVerificationTests.A_deferred_re_check_of_a_row_sent_without_a_retained_generation_declines_the_recovery_kill`; decisive: `DeliveryAttempts.ShouldBe(1)` and the `Destructive recovery was declined` incident.
- R-561-9: escalation tiers unchanged | `AgentSupervisionTests.Backoff_ladder_reaches_30_day_cap_and_escalates_once_per_tier`; decisive: one Warning at tier 1, one Critical at tier 2.
- R-561-10: the recovery component's five existing cases | `StandingSessionRecovery.test.tsx` (all `it`s).
- R-561-11: the stranded sweep skips a working session | `SessionMessageQueueDeliveryVerificationTests.Stranded_watchdog_skips_non_always_on_agents_and_working_sessions`; decisive: `Inputs.ShouldBeEmpty()` for the working session (the `busy` arm of V-561-28 rests on it).
- R-561-12: the healthy-uptime reset still requires a completed boot | `StandingRestartAccountingTests.An_old_start_timestamp_without_completed_boot_cannot_reset_failure_counters` (add `ContinuityResumeFailures = 4` to the seeded state and `ShouldBe(4)` before / `ShouldBe(0)` after, alongside the existing counters).

### Guard inventory

| ID | Guard (plan reference) | Test method | PC |
|---|---|---|---|
| G-561-1 | D-1: `Text` is mapped through `WithoutNul` before the row is built, so the ignition line persists | V-561-1 | PC-561-1 |
| G-561-2a | D-1: every string member of the event is sanitised, not only `Text` | V-561-2 | PC-561-2a |
| G-561-2b | D-1: sanitisation precedes the dedup key computation (`incomingUuids`/`seenUuids`) | V-561-2 | PC-561-2b |
| G-561-3 | D-1: `WithoutNul` returns the same instance when clean and replaces, never deletes | V-561-3 | PC-561-3 |
| G-561-4a | D-2: a batch `DbUpdateException` falls back to per-row saves; siblings land | V-561-4 | PC-561-4a |
| G-561-4b | D-2: the stub clips the bounded columns and nulls `ToolInput`, so it can land where the row could not | V-561-4 | PC-561-4b |
| G-561-4c | D-2: the per-row Warning names sequence/uuid/kind/SqlState and never the text or tool input | V-561-4 | PC-561-4c |
| G-561-5 | D-2: a row that fails as a stub is skipped and `PersistResult` reflects landed rows only | V-561-5 | PC-561-5 |
| G-561-6 | D-2: SqlState 23505 is skipped at Debug with no stub attempt and no mark | V-561-6 | PC-561-6 |
| G-561-7 | D-2: a transient `DbException` on the stub stops the loop (mark, landed rows, no skip) instead of skipping the row | V-561-7 | PC-561-7 |
| G-561-8a | D-3: a stubbed or skipped row records the mark with `Failures` and `Detail` = SqlState | V-561-4, V-561-5 | PC-561-8a |
| G-561-8b | D-3: a fully clean save removes the mark | V-561-7 | PC-561-8b |
| G-561-8c | D-3: the outer catch records the mark for non-database failures | V-561-8c | PC-561-8c |
| G-561-9a | D-3: `NoTranscriptRecord` with a mark at or after `SentAt - 30 s` withholds the AlwaysOn kill | V-561-9 | PC-561-9a |
| G-561-9b | D-3: the attempt is refunded (never below zero) | V-561-9 | PC-561-9b |
| G-561-9c | D-3: the incident is one Warning (Error when channel-bound) naming the failure and "matcher was blind", never "Restarting" | V-561-9, V-561-28 | PC-561-9c |
| G-561-10 | D-3: a mark older than `SentAt - 30 s` does not excuse the verdict (`>=` at the boundary) | V-561-10 (+ V-561-9 30 s arm) | PC-561-10 |
| G-561-11 | D-3: the arm fires only for `NoTranscriptRecord` | V-561-11 | PC-561-11 |
| G-561-12a | D-4: `Charge(Unknown)`/`Charge(LaunchOrProcessFailure)` increment `ContinuityResumeFailures`, saturating | V-561-12 | PC-561-12a |
| G-561-12b | D-4: `Charge(Infrastructure)`/`Charge(ContinuityUnavailable)` leave it | V-561-12 | PC-561-12b |
| G-561-13a | D-4: the `Observe` path trips at exactly the configured count (fourth schedules, fifth holds) | V-561-13 | PC-561-13a |
| G-561-13b | D-4: the `RecordStartFailureAsync` path trips the same hold | V-561-13b | PC-561-13b |
| G-561-13c | D-4: the tripping tick does not schedule or escalate; `NextRestartAt` stays null; later ticks make no attempt | V-561-13 | PC-561-13c |
| G-561-14 | D-4: the trip reads the new counter, never `RestartBackoffFailures` (Infrastructure cannot hold) | V-561-14 | PC-561-14 |
| G-561-15 | D-4: healthy uptime resets the counter | V-561-15 | PC-561-15 |
| G-561-16 | D-4: a human-accepted Start (`!automatic`) resets the counter | V-561-16 | PC-561-16 |
| G-561-17 | D-4: `ResumeFailureHoldAttempts = 0` disables the trip | V-561-17 | PC-561-17 |
| G-561-18a | D-4: Attention renders the new reason (not special-cased out) at Error with both actions | V-561-18a | PC-561-18a |
| G-561-18b | D-4: the recovery component has copy for the new reason | V-561-18b | PC-561-18b |
| G-561-18c | D-4: the client union carries the member (compile-time) | V-561-18c | none: a mutation is a `tsc` build failure, which the PC rules exclude; V-561-18c is the check |
| G-561-19 | D-4: `StandingContinuityState.Clear` does not reset the counter (a supervised resume keeps it) | V-561-19 | PC-561-19 |
| G-561-20 | D-4: `HoldAsync` clips the detailed evidence to the 1000-char column | V-561-20 | PC-561-20 |
| G-561-21 | D-3: `TryRemove`/`DisposeSessionAsync` drops the mark | V-561-21 | PC-561-21 |
| G-561-22 | D-2: `AddedManualCompactBoundary` is evaluated on the source text, so a stubbed manual boundary still flushes | V-561-22 | PC-561-22 |
| G-561-23 | D-2: a stub keeps `Uuid`/`Kind`, so redelivery dedups and the storm cannot recur | V-561-23 | PC-561-23 |
| G-561-24 | D-3: the refund is all-or-nothing across the reverting batch | V-561-24 | PC-561-24 |
| G-561-25 | D-4: `ContinuityResumeFailures` is mapped onto the supervision DTO | V-561-13 | PC-561-25 |
| G-561-26 | D-1: the sanitiser sits in `PersistTranscriptAsync`, below all three callers (sync path covered) | V-561-26 | PC-561-26 |
| G-561-27 | D-4: model snapshot and migration agree | V-561-27 | PC-561-27 |

Guards = 39, mapped = 38, justified without a PC = 1 (G-561-18c, compile-time), duplicate PC mappings = 0.

### Positive controls

Mutation runs each PC method-scoped on the local inherited SourceLanding child:
`dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-pc/ -- --treenode-filter "/*/*/<Class>/<Method>"`
after `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-pc/` (parameterised methods run every argument; the
expected red names the arm) and `pwsh -File scripts/test-client.ps1 StandingSessionRecovery` for the Vitest row. Each
cycle: apply the mutation, run red at the named assertion, restore, refresh the restored file's timestamp, run green.
Zero tests, build failures and fixture errors are not red. Rows that touch the same file must run in separate
cycles: `AgentSessionRuntime.cs` (1, 2a, 2b, 4a, 4b, 4c, 5, 6, 7, 8a, 8b, 8c, 21, 22, 23, 26), `SessionMessageQueueService.cs`
(9a, 9b, 9c, 10, 11, 24), `AgentSupervisorService.cs` (13a, 13b, 13c, 14, 15, 17), `RestartFailurePolicy.cs` (12a, 12b),
`StandingContinuityState.cs` (19, 20); single-row files (`ColumnText.cs` 3, `AgentControlService.cs` 16,
`AttentionService.cs` 18a, `StandingSessionRecovery.tsx` 18b, `AgentService.cs` 25, `AppDbContextModelSnapshot.cs`
27) may batch with one row from each of the multi-row files.

- PC-561-1: break G-561-1 by deleting the `Sanitize` application at the top of `PersistTranscriptAsync` (rows built from the raw event); expect `AgentSessionRuntimeTests.C561_a_NUL_in_tool_result_text_persists_as_U_FFFD` red at `rows.ShouldHaveSingleItem()` (22021 -> outer catch -> zero rows) and at `logs.ShouldNotContain("Failed to persist")`.
- PC-561-2a: break G-561-2a by making `Sanitize` map `Text` only (all other members copied raw); expect `C561_NUL_in_every_string_member_is_replaced_and_the_sanitised_uuid_dedups` red at `LastStoredSeq.ShouldBe(1)` (NUL in `ToolInput` -> 22021 -> Empty).
- PC-561-2b: break G-561-2b by applying `Sanitize` at row construction only, after `incomingUuids`/`seenUuids` are computed; expect the same method red at `logs.ShouldNotContain("Failed to persist")` (the dedup query parameter carries the NUL -> 22021) and at `ShouldHaveSingleItem()`.
- PC-561-3: break G-561-3 by returning `text.Replace('\0', '�')` unconditionally; expect `ColumnTextTests.WithoutNul_replaces_each_NUL_and_returns_the_same_instance_when_clean` red at `ReferenceEquals(WithoutNul(s), s).ShouldBeTrue()`.
- PC-561-4a: break G-561-4a by removing the `catch (DbUpdateException)` fallback so the outer catch takes the batch failure; expect `C561_a_row_that_exceeds_a_column_lands_as_a_clipped_stub_and_its_siblings_land_verbatim` red at `rows.Count.ShouldBe(3)` (zero rows).
- PC-561-4b: break G-561-4b by copying `ToolName` into the stub unclipped; expect the same method red at `stub.ShouldNotBeNull()` (the stub fails 22001 too and the row is skipped).
- PC-561-4c: break G-561-4c by appending `Source.ToolInput` to the per-row Warning; expect the same method red at `warning.ShouldNotContain("SECRET-INPUT-MARKER")`.
- PC-561-5: break G-561-5 by computing `AddedTurnBoundary`/`AddedAssistantText`/`LastStoredSeq` from the attempted rows (the pre-fallback values) instead of the landed ones; expect `C561_a_row_that_fails_as_a_stub_is_skipped_and_the_result_reflects_landed_rows_only` red at `result.AddedTurnBoundary.ShouldBeFalse()`.
- PC-561-6: break G-561-6 by deleting the 23505 arm (unique violations take the stub path); expect `C561_a_unique_violation_from_a_concurrent_catch_up_is_skipped_without_a_stub_or_a_mark` red at `TryGetTranscriptPersistFailure(sid, out _).ShouldBeFalse()` and at `fault.SightingsOf("u2").ShouldBe(2)` (a third, stub save is seen).
- PC-561-7: break G-561-7 by treating a transient stub failure like a non-transient one (skip the row and continue); expect `C561_a_transient_failure_on_the_stub_stops_the_loop_and_a_later_delivery_recovers` red at `rows.Select(Uuid).ShouldBe(["u1"])` (`u3` landed) and at `LastStoredSeq.ShouldBe(1)`.
- PC-561-8a: break G-561-8a by removing `RecordTranscriptPersistFailure` from the stub and skip arms; expect `C561_a_row_that_exceeds_a_column_lands_as_a_clipped_stub_and_its_siblings_land_verbatim` red at `TryGetTranscriptPersistFailure(sid, out var mark).ShouldBeTrue()`.
- PC-561-8b: break G-561-8b by never removing the mark on a clean save; expect `C561_a_transient_failure_on_the_stub_stops_the_loop_and_a_later_delivery_recovers` red at the final `TryGetTranscriptPersistFailure(sid, out _).ShouldBeFalse()`.
- PC-561-8c: break G-561-8c by removing the mark from the outer `catch (Exception)`; expect `C561_a_non_database_persist_failure_records_the_mark` red at `TryGetTranscriptPersistFailure(sid, out var mark).ShouldBeTrue()`.
- PC-561-9a: break G-561-9a by dropping `&& !persistBlind` from the `kill` predicate; expect `C561_a_blind_matcher_verdict_withholds_the_kill_and_refunds_the_attempt` red at `h.Adapter.Killed.ShouldBeFalse()` (arm `10,false`).
- PC-561-9b: break G-561-9b by omitting `DeliveryAttempts--` in the blind arm (kill still withheld); expect the same method red at `DeliveryAttempts.ShouldBe(0)` (arm `10,false`).
- PC-561-9c: break G-561-9c by letting the ordinary Error incident branch run for the blind shape (no blind text, `Restarting` sentence present); expect the same method red at `Message.ShouldContain("matcher was blind")` (arm `10,false`) and `Severity.ShouldBe(Warning)`.
- PC-561-10: break G-561-10 by dropping the `SentAt` comparison (`persistBlind = verdict == NoTranscriptRecord && TryGet…`); expect `C561_a_persist_failure_older_than_the_attempt_does_not_excuse_the_verdict` red at `h.Adapter.Killed.ShouldBeTrue()`.
- PC-561-11: break G-561-11 by dropping `verdict == DeliveryVerdict.NoTranscriptRecord` from `persistBlind`; expect `C561_a_no_composer_evidence_verdict_with_a_mark_keeps_the_card_0103_rules` red at `Killed.ShouldBeTrue()` and `DeliveryAttempts.ShouldBe(1)`.
- PC-561-12a: break G-561-12a by not incrementing `ContinuityResumeFailures` for `Unknown`; expect `RestartFailureClassificationTests.C561_only_non_infrastructure_outcomes_charge_the_resume_failure_counter` red at the first `ContinuityResumeFailures.ShouldBe(1)`.
- PC-561-12b: break G-561-12b by incrementing it for `Infrastructure` too; expect the same method red at `ContinuityResumeFailures.ShouldBe(2)` after `Charge(Infrastructure)`.
- PC-561-13a: break G-561-13a by tripping on `count > threshold` instead of `>=`; expect `StandingRestartAccountingTests.C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling` red at `ContinuityHeldAt.ShouldNotBeNull()` after the fifth failure.
- PC-561-13b: break G-561-13b by removing the `TryHoldRepeatedResumeFailureAsync` call from `RecordStartFailureAsync`; expect `C561_a_start_that_throws_charges_the_same_counter_and_trips_the_same_hold` red at `ContinuityHeldAt.ShouldNotBeNull()` after attempt 5 (V-561-13 stays green: different path).
- PC-561-13c: break G-561-13c by continuing into scheduling after `HoldAsync` returns true (no early return); expect `C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling` red at `NextRestartAt.ShouldBeNull()` and `RestartScheduled` count `ShouldBe(4)`.
- PC-561-14: break G-561-14 by reading `state.RestartBackoffFailures` instead of `state.ContinuityResumeFailures` in the trip; expect `C561_infrastructure_outcomes_grow_the_ladder_and_never_hold` red at `ContinuityHeldAt.ShouldBeNull()` after the fifth 57P03 (V-561-13 stays green because both counters read 5 there).
- PC-561-15: break G-561-15 by omitting `ContinuityResumeFailures = 0` from the healthy-uptime block; expect `C561_healthy_uptime_resets_the_resume_failure_counter` red at `ContinuityResumeFailures.ShouldBe(0)` after the reset tick.
- PC-561-16: break G-561-16 by omitting the reset in `AcceptAsync`'s `!automatic` branch; expect `C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again` red at `ContinuityResumeFailures.ShouldBe(0)` after the retry accept.
- PC-561-17: break G-561-17 by dropping the `setting == 0` guard (0 reads as "hold at any count"); expect `C561_a_zero_threshold_restores_the_never_give_up_ladder` red at `ContinuityHeldAt.ShouldBeNull()` after the first failure.
- PC-561-18a: break G-561-18a by filtering `ContinuityReason != StandingContinuityReason.RepeatedResumeFailure` in `BuildStandingContinuityItemsAsync`; expect `StandingContinuityAttentionTests.Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts` red at `ShouldHaveSingleItem()` for the `RepeatedResumeFailure` arm only.
- PC-561-18b: break G-561-18b by deleting the `RepeatedResumeFailure` copy branch in `StandingSessionRecovery.tsx` (generic fallback renders); expect Vitest `repeated resume failure explains the hold without claiming the history is gone` red at `getByText(/resumed this conversation repeatedly/)`.
- PC-561-19: break G-561-19 by adding `state.ContinuityResumeFailures = 0` to `StandingContinuityState.Clear`; expect `C561_a_supervised_resume_clears_the_hold_fields_but_not_the_counter` red at `ContinuityResumeFailures.ShouldBe(3)`.
- PC-561-20: break G-561-20 by storing the detailed evidence unclipped; expect `C561_hold_detail_is_clipped_to_the_evidence_column` red at `Should.NotThrowAsync` (Postgres 22001 on the 1000-char column) or, if the driver truncates silently, at `Length.ShouldBe(1000)`/`ShouldEndWith("…")`.
- PC-561-21: break G-561-21 by omitting `_persistFailures.TryRemove` from `TryRemove`; expect `C561_disposing_the_session_removes_the_persist_failure_mark` red at `TryGetTranscriptPersistFailure(sid, out _).ShouldBeFalse()`.
- PC-561-22: break G-561-22 by evaluating `IsManualCompactBoundary` on the stub's `Text`; expect `C561_a_stubbed_manual_compaction_boundary_still_reports_the_boundary` red at `AddedManualCompactBoundary.ShouldBeTrue()`.
- PC-561-23: break G-561-23 by building the stub with `Uuid = null`; expect `C561_a_redelivered_unpersistable_row_dedups_against_its_stub` red at `Count(r => r.Uuid == "u2").ShouldBe(1)` (0) and at `logs.Count(Warning).ShouldBe(1)`.
- PC-561-24: break G-561-24 by using `reverting.Any(...)` instead of `All(...)`; expect `C561_a_mixed_batch_takes_the_destructive_default` red at `Killed.ShouldBeTrue()`.
- PC-561-25: break G-561-25 by not mapping `ContinuityResumeFailures` in `AgentService`'s supervision projection (DTO default 0); expect `C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling` red at `Supervision.ContinuityResumeFailures.ShouldBe(5)` only.
- PC-561-26: break G-561-26 by moving the `Sanitize` application from `PersistTranscriptAsync` into `ObserveTranscriptAsync` only; expect `C561_sync_over_a_runner_snapshot_with_the_poison_line_persists_once_and_never_retries` red at `rows.Count.ShouldBe(3)` (the sync path persists raw -> 22021 -> zero rows) while V-561-1 stays green.
- PC-561-27: break G-561-27 by removing the `ContinuityResumeFailures` property from `AppDbContextModelSnapshot.cs`; expect `StandingSessionOwnershipTests` red at `HasPendingModelChanges().ShouldBeFalse()`.

### Out of scope

- The plan's own follow-ons: the remote-control modal hold on a resumed generation, context fullness above 100 %,
  tailer unbind on exit, and which tool wrote the NUL tool_result.
- A real Claude `--resume` process and a real pty: the fake adapter is the terminal; `ClaudeTrustPromptCanaryTests`
  is unaffected by this card.
- The 30 s `clockTolerance` value itself; only the `>=` boundary (30 refunds, 31 kills) is pinned.
- A real two-runtime concurrent catch-up race; the interceptor-inserted racing row produces the same 23505.
- The SignalR relay keeping the raw NUL (asserted inside V-561-1 as "nothing else changes", not a guard: it is
  today's behaviour and the serializer escapes it).
- The `IsUnseenTurnBoundaryAsync` raw-event query with a NUL `Uuid`/`Kind` (theoretical; Claude generates both;
  fails open by design).
- Twenty outcomes for G-17 (twelve settled above).
- S5 owner docs: Review reads them; no test.
- Concurrency of the in-memory `_persistFailures` map (a `ConcurrentDictionary`; no interleaving test).
- The migration's `defaultValue: 0` on existing rows beyond V-561-27 (every harness row is created by
  `GetOrCreateStateAsync`).

### Cost

All figures are **estimated** (nothing was built or run in this dispatch); assumptions: one foreground owner,
isolated `bin-c561/` output, warm NuGet cache, no concurrent `Antiphon.Agents.Pty.Tests`, the shared local Postgres up.

| Ordinary V/R floor, per Code or independent Review pass | Minutes |
|---|---:|
| Setup: restore + build `tests/Antiphon.Tests` into `bin-c561/` | 12 |
| `ColumnTextTests` + `RestartFailureClassificationTests` (Unit) | 1 |
| `AgentSessionRuntimeTests` full class (existing 20 incl. one pty spawn + 11 new DB cases) | 3 |
| `SessionMessageQueueDeliveryVerificationTests` full class (existing ~90 with 1-3 s deadlines + 5 new with 3 s grace, V-561-28 three arms with sweeps) | 11 |
| `StandingRestartAccountingTests` full (existing 10 + 7 new; V-561-17 twelve iterations) | 4 |
| `StandingContinuityAttentionTests` (5 arms + 1) + `StandingContinuityRecoveryTests` (existing) | 2 |
| `StandingSessionOwnershipTests` (migration guard) | 1 |
| Vitest `StandingSessionRecovery` + `npm run build` (tsc) in `client/` | 3 |
| **Per-pass setup + ordinary V/R** | **37** |

Code floor 37; independent ordinary Review floor another 37. Band per pass: 30-45.

| PC floor (Mutation), method-scoped red/restore/green | Controls | Min per cycle | Minutes |
|---|---:|---:|---:|
| Runtime rows (`AgentSessionRuntime.cs`, DB tests ~5 s each) | 16 | 6 | 96 |
| Queue rows (`SessionMessageQueueService.cs`; each run pays the 3 s grace per arm) | 6 | 6.5 | 39 |
| Supervisor/policy/control/state/attention/service/snapshot rows (C#) | 15 | 6 | 90 |
| Vitest row (PC-561-18b) | 1 | 1.5 | 2 |
| Mutation setup: `bin-pc/` build, one green run of the touched classes | - | - | 20 |
| Restoration inventory, timestamp refresh, evidence | - | - | 15 |
| **Mutation floor, all 38 controls, unbatched** | **38** | - | **262** |

Band 220-290. Per cycle = incremental build (~2.5 min) x 2 + method-scoped run (~30-60 s) x 2. Batching one runtime
row with one queue row and one supervisor-side row per cycle (different files, different methods) bounds the count
at the 16 runtime cycles: about 16 x 6.5 + 2 + 35 = **141 minutes batched**, a saving of roughly 120 minutes. No
concurrency saving is assumed (one managed snapshot).

**Total verification floor** = setup/build 12 + ordinary V/R 25 + every PC red/restore/green 227 + Mutation
setup/evidence 35 = **299 minutes, estimated**, unbatched; about 178 batched.

**Plan cost-band check.** The plan's acceptance table had 19 rows; splitting bundled rows and adding the seven
guards the plan stated but did not table (mark lifecycle, stub identity, manual boundary, DTO, snapshot, all-or-nothing,
sync path) gives 39. The runtime file's sixteen same-file rows are the floor's long pole; nothing here changes the
plan's slice order (S1 -> S2 -> S3 -> S4 -> S5) or its verification commands, which remain the class filters listed
under Implementation slices plus `StandingSessionOwnershipTests` and the client build.

--- next stage ---
next: code
handoff: Code implements S1-S5 of the CARD-0561 plan against this verification design (39 guards, 27 V, 12 R rows): PersistTranscriptAsync/PersistResult internal, capture SentAt before the revert nulls it, the three test partials and interceptors under Missing setup, then the ordinary V/R floor (class filters, StandingSessionOwnershipTests, client build); Mutation runs the 38 PCs after land.
artifact: docs/superpowers/plans/2026-09-18-card-0561-poisoned-transcript-resume-loop-plan.md
