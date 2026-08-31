# CARD-0287 — Visible attention for a cardless Details-only start

**Date:** 2026-08-31
**Status:** Plan only — no implementation or tests run.

## Outcome and design decision

Replace the one-time, Information-level CARD-0283 clue with a Warning-level, read-time attention
row named `AttentionKind.CardlessDetailsNoPrompt = 19`. The row makes the exact actionable state
visible to a human or caller without changing the deliberately valid empty-shell start contract:
`Agent.Details` remains standing-job metadata and is never typed as work.

This is a state projection, not a persisted incident. `GET /api/attention` (and its existing
15-second client refresh) will derive the row every read; it will neither reject a start, synthesize
a prompt, send a queued message, wake a session, kill a process, add a setting, nor add a hosted
sweep. The only offered action is `OpenAgent`, so the caller can inspect the affected agent and use
the existing message/start APIs deliberately.

Append value **19** after the current highest value, `CallerNoteUndelivered = 18`. The enum is a
serialized client contract and must only ever be appended.

## Durable live predicate

`StartAgentRequest.Prompt` itself is request-only and is not a column. CARD-0283 already leaves a
durable, sufficient trail without adding any state: `AgentSessionService.LaunchInteractiveAsync`
enqueues every non-blank cardless start prompt as a `SessionQueuedMessage` with `Origin == Ui`; the
row remains after it becomes Sent. A later operator message uses the same durable UI-origin queue
route. A delivered message necessarily creates transcript evidence.

Implement a read-time helper in `AttentionService`, for example
`BuildCardlessDetailsNoPromptItemsAsync(now, ct)`, and call it with the other independent
DB-derived attention passes. It should produce one row per qualifying current session only when all
of these are true:

- The session is `Running`, has `CardId == null`, and has been running longer than a private fixed
  two-minute grace (`StartedAt < now - CardlessDetailsPromptGrace`). This gives boot and the
  `WhenIdle` launch queue time to create its durable row; it is not a new configuration setting.
- It is the new-row interactive launch shape that CARD-0283 actually logs: `CreatedAt == StartedAt`
  and `ComposedBundleStamp != null`. `StartInteractiveSessionAsync` stamps both timestamps from one
  `now` value and always records a composition (empty string is meaningful); resume paths restamp
  only `StartedAt`, while Herdr attach leaves the composition stamp null. This excludes an old,
  intentionally empty shell that has merely been resumed or attached.
- A current owning `Agent` has `PersistentSessionId` equal to this session id and a non-whitespace
  current `Details` value. Match the standing-owner pattern already used by
  `ResolveSessionOwnersAsync`: fetch only the candidate session ids, find the matching string ids,
  parse defensively, and apply `string.IsNullOrWhiteSpace` semantics rather than treating whitespace
  as a standing job.
- No `TranscriptEntries` exist for the session. With zero rows the shared transcript contract reads
  the session as idle; do not introduce another `IsWorkingAsync` implementation or consult a screen
  or runner for this DB-derived signal.
- No `SessionQueuedMessages` row with `Origin == Ui` exists for the session, regardless of message
  status. This is the persisted evidence that `StartAgentRequest.Prompt` (or a subsequently queued
  operator prompt) exists; it must suppress a Details-only warning even before the transcript arrives.

Batch those lookups: query the narrow eligible session snapshot as no-tracking, query matching
agents by the already formatted persistent-session ids, and collect distinct transcript/session-UI
queue ids for that small candidate set. The existing per-session transcript index
`IX_TranscriptEntries_AgentSessionId_Sequence` and queue index
`IX_SessionQueuedMessages_AgentSessionId_Status_Sequence` cover the two membership reads. Do not
add an index or migration for a read-time Warning projection.

This is intentionally current, not a historical accusation. The row disappears on the next
attention read when a transcript entry lands, the operator queues a UI message, Details is cleared,
the current session changes, the session becomes non-Running, or it gains card ownership. It may
also be absent during the first two minutes while boot is settling. No durable acknowledgement is
needed because the predicate itself is the lifecycle.

## Row content and user interaction

For each match return:

- `Kind = CardlessDetailsNoPrompt`, `Severity = Warning`, its `SessionId` and `AgentId`, no task or
  message id, `SinceUtc = session.StartedAt`, and `Actions = [OpenAgent]`.
- The agent name as the title. The headline should say that a cardless start is still idle after the
  grace because Details was not sent as a prompt. Evidence should state the three observed facts
  (current Details, no transcript, no UI start/message queue row), remind the reader that Details is
  standing metadata, and name the two deliberate recovery paths: send a session message now or pass
  `StartAgentRequest.Prompt` on a future cardless start.

The row belongs in the existing Warning/Suspect group and is counted by the existing attention
summary automatically. The client already targets an agent-scoped row at `/agents?agent=...`; no new
endpoint, SignalR event, action plumbing, alert sink, or queue operation is required.

## Implementation slices

1. **Append the server and client attention contract**

   - In `server/Application/Dtos/AttentionDtos.cs`, append and document
     `CardlessDetailsNoPrompt = 19`. Its XML documentation must state the narrow current-session
     predicate and that it is detection only; Details must not be delivered or auto-fixed.
   - In `client/src/api/attention.ts`, append `'CardlessDetailsNoPrompt'` with the same semantics.
     Do not alter query keys, polling, summary logic, or action types.

2. **Project the state at attention read time**

   - In `server/Application/Services/AttentionService.cs`, add the private two-minute
     `CardlessDetailsPromptGrace` constant and call the new no-tracking builder from `GetAsync`
     alongside `BuildParkedMessageItemsAsync` and `BuildCallerNoteUndeliveredItemsAsync`.
   - Implement the batched ownership, transcript, and UI-origin queue evidence described above.
     Keep the original CARD-0283 log in `AgentControlService` for immediate server diagnostics; do
     not duplicate the launch logic there, persist a new session/agent flag, or mutate anything from
     the attention service.
   - Explicitly retain the fresh/new-row and composition-stamp guards. A generic empty-session
     timeout would flag legitimate AlwaysOn, channel, UI-start, resumed, and Herdr-attached shells,
     which CARD-0283 deliberately declined to treat as an error.

3. **Present the Warning clearly**

   - Add a concise `ATTENTION_VISUALS.CardlessDetailsNoPrompt` entry in
     `client/src/features/attention/attentionVisuals.ts`, using the existing Warning palette and a
     prompt/attention icon. Its badge and hint should explain “Details needs a prompt,” not imply
     that Details failed to be delivered or that the product will send it automatically.
   - Add the kind to `ALL_KINDS` in
     `client/src/features/attention/attentionVisuals.test.ts`. Existing `Record` completeness,
     severity grouping, agent navigation, and `OpenAgent` action rendering provide the mechanics;
     do not create special panel behavior.
   - Add an `AttentionPanel.test.tsx` case that serves a Warning row for this kind and asserts its
     title, idle/no-prompt headline, and new badge are visible.

4. **Pin the projection’s positive path and exclusions**

   Extend `tests/Antiphon.Tests/Application/AttentionServiceTests.cs` and its isolated `Scenario`
   helpers. Keep each assertion scoped to the ids seeded by the test, consistent with the shared
   Postgres test discipline.

   - Seed an explicitly new-row interactive session (`CreatedAt == StartedAt`, non-null composed
     stamp), make it current on an agent with Details, leave it `Running`, cardless, transcript-free,
     and without UI queue rows. After the fixed grace, assert exactly one Warning with the expected
     kind, agent/session ids, `StartedAt`-based `SinceUtc`, explanatory copy, and only `OpenAgent`.
   - Use an injected `FakeTimeProvider` to pin the grace boundary: a session exactly two minutes old
     is absent and an older otherwise identical session is present. This preserves the strict
     comparison and avoids a start-time flicker.
   - Cover the scope guards independently: blank/whitespace Details, non-current owner, non-Running
     session, card-owned session, resumed/attached shape (`CreatedAt != StartedAt` or null composed
     stamp), and any transcript entry must not emit this kind.
   - Seed both Pending and Sent UI-origin queue evidence while keeping zero transcript. Each must
     suppress the row, proving that the durable `StartAgentRequest.Prompt` evidence survives
     delivery status and that an operator message clears the signal before transcript ingestion.
     Re-read after adding a transcript entry to a previously qualifying session and assert
     disappearance, pinning the non-sticky lifecycle.
   - Keep existing CARD-0283 launch tests unchanged as the source-of-truth contract that Details is
     never typed and an explicit Prompt is queued. The new tests belong in the attention projection
     suite; no hosted-service or launch-path test is needed.

## Validation for the later code pass

Run the focused `AttentionServiceTests` through TUnit:

```powershell
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "AttentionServiceTests"
```

Then run the client suite with `pwsh -File scripts/test-client.ps1`. Do not use `dotnet test`; do
not run `Antiphon.Tests` concurrently with `Antiphon.Agents.Pty.Tests`. A normal code pass should
also run the affected server project/build checks using the repository’s isolated-output guidance if
the always-on processes hold `bin/`.

## Explicit non-goals

- No migration, new entity field, index, configuration setting, hosted service, incident, alert
  sink, acknowledgement state, or API response field.
- No generic idle timeout and no warning for every empty AlwaysOn, channel, UI-start, resumed, or
  Herdr-attached session.
- No mutation from attention: no auto-typed Details, forced delivery, auto-start, retry, queue
  flush, cancel, kill, stop, or session reclassification.
- No removal or severity escalation of the CARD-0283 Information log; it remains useful immediate
  forensic evidence while the attention row supplies the caller-visible, self-clearing state.
