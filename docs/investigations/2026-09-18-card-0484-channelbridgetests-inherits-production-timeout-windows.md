# CARD-0484 investigation: ChannelBridgeTests inherits production timeout windows

Date: 2026-09-18. Card: CARD-0484 (High/Soon, rank 5). Source analysis:
`docs/investigations/2026-09-11-card-0474-full-category-timing-breakdown.md` §3 and §5 item 2.

## Outcome

Confirmed. `ChannelBridgeTests`' private `HarnessAsync` registers `SessionMessageQueueService`
with no `IOptions<SupervisionSettings>`, so the service falls back to
`new SupervisionSettings().DeliveryVerification` (production defaults: 30s transcript-confirm,
15s evidence, 30s post-submit, 500ms poll). Every bound-channel inbound in those tests delivers
INLINE through the queue's verified path, the fake adapter never produces a UserPrompt row, and the
confirm loop runs to its 30s deadline before returning a degraded screen-only `Delivered`. That
deadline is the entire cost of the 21 main-path cases.

Measured today at tip `3a62074e` (40/40 pass): **12m34.5s** TUnit duration, 764s outer wall.
With the sibling harness's compressed `DeliveryVerificationSettings` block injected verbatim into the
private harness (probe only, reverted, never committed): **2m40.4s**, 168s outer wall, 40/40 pass.
The 21 private-harness main-path cases went from 31–48s each to 3.9–5.4s each.

## 1. Exactly which timeouts are inherited, and where

### The inheritance point

`tests/Antiphon.Tests/Application/ChannelBridgeTests.cs:921-1010` (`HarnessAsync`) builds its own
`ServiceCollection`:

- line 935: `services.AddSingleton(TimeProvider.System);` (real clock)
- line 936: `services.AddSingleton<IOptions<AgentSessionSettings>>(...)`
- line 939: `services.AddSingleton(Options.Create(new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = debounceWindowMs, MaxAttachmentBytes = maxAttachmentBytes }));`
- line 947: `services.AddSingleton<SessionMessageQueueService>();`
- **no `IOptions<SupervisionSettings>` registration anywhere in the file** (verified by grep; the two
  in-file tests that use `BridgeQueueHarness` get it from the sibling harness).

`server/Application/Services/SessionMessageQueueService.cs:47-66`: the constructor takes
`IOptions<SupervisionSettings>? supervisionSettings = null` and does

```csharp
_verification = (supervisionSettings?.Value ?? new SupervisionSettings()).DeliveryVerification;
```

so a container with no registration silently gets the production object.

### The production values that apply (`server/Application/Settings/SupervisionSettings.cs`, class `DeliveryVerificationSettings`)

| Setting | Production default | Sibling harness value | Consumed at (SessionMessageQueueService.cs) |
|---|---:|---:|---|
| `Enabled` | true | true | 2806 (`verify` gate) |
| `EvidenceTimeoutSeconds` | 15 | 1 | `WaitForComposerEvidenceAsync` deadline |
| `PollIntervalMs` | 500 | 50 | every poll `Task.Delay` (3184, evidence, settle); floored to 1000 for the CatchUp pull cadence (2998) |
| `PostEvidenceSettleMs` | 500 | (default 500) | `SettlePostEvidenceAsync` settle window |
| `PostSubmitAdvanceTimeoutSeconds` | 30 | 1 | `WaitForSequenceAdvanceAsync` (legacy screen-only path; not reached when TranscriptConfirmEnabled) |
| `StrandedAgeSeconds` | 60 | 0 | stranded sweep cutoff (1088) |
| `TranscriptConfirmEnabled` | true | true | 2824-2827 |
| **`TranscriptConfirmTimeoutSeconds`** | **30** | **3** | **2984: `WaitForTranscriptConfirmAsync` deadline — the cost** |
| `ReEnterIntervalSeconds` | 7 | 1 | 2987 (re-press cadence inside the loop) |
| `SubmitAttempts` | 3 | (default 3) | 3147 (re-press cap) |
| `PostFailureConfirmGraceSeconds` | 20 | 3 | 3242/3284 grace after a `NoTranscriptRecord` verdict only |
| `UnobservableBaselineConfirmClockToleranceSeconds` | 30 | 30 | 2833 (wall-clock floor; not a wait) |
| `BootPromptRetryDelaySeconds` | 2 | 0 | boot-prompt retry pause (launch path; not exercised here) |

### The mechanism, per delivery, under production defaults

Reconstructed from source and matched against the measured 31.2–33.4s per case:

1. `ChannelBridgeService.FlushLaneAsync` (`server/Application/Services/ChannelBridgeService.cs:222`)
   calls `_queue.EnqueueAsync(sessionId, prompt, MessageSendMode.WhenIdle, ...)` with the default
   `deliverIfIdle: true`.
2. `EnqueueAsync` (`SessionMessageQueueService.cs:477-483`) inserts the row and, because the harness
   session is live (fake adapter registered), accepting input, and `IsWorkingAsync` is false (zero
   transcript rows), calls `DeliverNextLockedAsync` **inline** — the test's `await
   HandleInboundAsync(...)` blocks on the whole verification.
3. `DeliverAsync` (2806): `verify` = `Enabled` && `IsVerifiedDeliverySessionAsync` → the session is
   `AgentKind.ClaudeCode` (`ChannelBridgeTests.cs:975`), which is a Supported delivery-verification
   kind → verified path.
4. Composer evidence: `FakeAgentProtocolAdapter.SendInputAsync` echoes typed text into
   `SnapshotRenderedScreen()` synchronously (`tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs`,
   `EchoTypedInputToScreen = true`), so `WaitForComposerEvidenceAsync` returns on its first check
   (≈0s of the 15s budget).
5. `SettlePostEvidenceAsync`: ≈500ms (`PostEvidenceSettleMs`), then a 20ms delay and the Enter.
6. The Enter: the fake's `SubmitAck = "\n"` is emitted through `OnTextDelta`, which
   `AgentSessionRuntime.Register`'s delta handler (`AgentSessionRuntime.cs:96-102`) turns into
   `_lastSequences++`, so `LastSequence` advances. The fake has **no `OnSubmitted` hook wired** in
   this harness, so **no UserPrompt row is ever written**.
7. `WaitForTranscriptConfirmAsync`: the baseline is unobservable (zero transcript rows at type time,
   `ChannelBridgeTests.cs:1029-1052` `BindChannelAsync` seeds with `Text = null`, which is recorded
   but never routed). The loop polls `TryFindUnobservableConfirmingRecordAsync` every 500ms, re-presses
   Enter at 7s and 14s (no-op on an empty composer), and at the **30s** deadline (3074) takes the
   `!observable` branch: `sawPositiveSubmit` is true for ClaudeCode via `sawSequenceAdvance` (3068), so
   it calls `RecordDeliveryUnverifiedAsync` and returns `Confirmed(DeliveryConfirmedBy.Screen)`
   (3105-3113). `RecordDeliveryUnverifiedAsync` needs `AgentSupervisorService`, which the private
   harness does not register; the `GetRequiredService` throw is swallowed by its own catch
   (2497-2500), so no incident row is written and the test observes plain success.
8. Verdict `Confirmed(Screen)` is not `NoTranscriptRecord`, so `HandleDeliveryFailureAsync`'s 20s
   `GraceConfirmAsync` (3750-3751) does **not** run. Total ≈ 0.5s + 30s + poll granularity ≈ 31s.

Every routed case in the class pays step 7 once. Under the sibling budgets step 7 is 3s and the same
degraded verdict is produced, which is why the probe run passed 40/40 with no assertion changes:
nothing in `ChannelBridgeTests` asserts on the delivery verdict, `SentAt`, or incidents from the
private harness (grep: the only incident assertions are in the two `BridgeQueueHarness`-based tests,
lines 69 and 892, and they are about `ChannelReplyLost`).

## 2. The sibling harness and its pattern

"Its sibling harness" in the source analysis is `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs`
(the `source-findings.md` wording: "Shared BridgeQueueHarness instead selects explicit short
verification budgets (post-submit 1 s, transcript-confirm 3 s)"). It is used by 82 test files in
`Antiphon.Tests`, including two tests already inside `ChannelBridgeTests` itself (lines 35-71 and
825-904).

The proven pattern, `BridgeQueueHarness.cs:96-121`:

```csharp
var verification = new DeliveryVerificationSettings
{
    Enabled = true,
    EvidenceTimeoutSeconds = 1, // fast wedge verdicts in tests
    PollIntervalMs = 50,
    PostSubmitAdvanceTimeoutSeconds = 1,
    StrandedAgeSeconds = 0,
    TranscriptConfirmTimeoutSeconds = 3,
    ReEnterIntervalSeconds = 1,
    PostFailureConfirmGraceSeconds = 3,
    UnobservableBaselineConfirmClockToleranceSeconds = 30,
    BootPromptRetryDelaySeconds = 0,
};
options.ConfigureDeliveryVerification?.Invoke(verification);
services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(
    options.Supervision ?? new SupervisionSettings { DeliveryVerification = verification }));
```

Two things the sibling does that the private harness does not:

1. **Registers `IOptions<SupervisionSettings>` with the compressed `DeliveryVerificationSettings`
   above.** This alone is what the probe changed, and it accounts for the whole 12m34s → 2m40s.
2. **Wires `adapter.OnSubmitted`** (`BridgeQueueHarness.cs:236-252`) to insert a stamped
   `UserPrompt` + `TurnEnd` row on every submit, so a delivery normally confirms via transcript within
   one poll rather than running to the deadline at all. This was deliberately **not** probed: many
   `ChannelBridgeTests` cases then insert their own `UserPrompt`/`QueuedUserPrompt` rows for the
   delivered prompt (`InsertTurnAsync`, lines 120, 150, 177, 215, 250, ...) and reason about sequence
   order for reply matching (CARD-0154, CARD-0233 cases), so auto-inserting rows would change what
   those tests exercise. The source analysis' own caveat applies: "Preserve receipt requirements; do
   not simply lower production timeouts or manufacture transcript proof."

## 3. Current measurement (tip 3a62074e, 2026-09-18)

Command (from the worktree, built to an isolated output, since deleted):

```
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c484/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c484/ -- --treenode-filter '/*/*/ChannelBridgeTests/*' --report-trx --report-trx-filename channelbridge.trx --results-directory .antiphon/c484-run
```

Result: 40 total, 40 passed, 0 failed. TUnit duration **12m34.491s**; TRX run window
2026-09-18T01:30:24Z → 01:42:58Z; outer wall 764s (includes host start, discovery and the
Testcontainers Postgres bootstrap). Sum of per-case durations 751.72s. Host interference: two other
`Antiphon.Tests.exe` processes (worktrees `card-task-e67555ad` and `card-task-a3e92650`) were running
concurrently for the whole window, which is the likely reason today's figure is ~14% above the
2026-09-11 658.76s.

Per-case (seconds, baseline → probe), sorted by baseline:

| Case | Baseline | Probe | Harness |
|---|---:|---:|---|
| A_turn_whose_stop_marker_precedes_the_text_still_replies | 48.25 | 4.42 | private |
| Herdr_held_agent_inbound_is_dropped_with_one_incident_and_no_start_notice_or_reroute | 45.38 | 66.85 | BridgeQueueHarness |
| Redelivered_message_is_not_routed_twice | 41.24 | 3.98 | private |
| Bound_channel_routes_the_message_into_the_agent_session | 33.37 | 5.37 | private |
| A_response_ending_in_a_question_is_typed_as_question | 33.11 | 4.25 | private |
| An_attachment_only_message_routes_with_the_saved_file_path | 32.69 | 3.93 | private |
| A_captioned_document_routes_with_text_and_file_path | 32.59 | 4.02 | private |
| A_marker_only_reply_sends_the_document_with_no_text | 32.46 | 4.05 | private |
| An_attach_marker_sends_the_file_inline_and_strips_the_marker_line | 32.29 | 3.94 | private |
| Turn_end_sends_the_agents_answer_down_the_channel | 32.18 | 4.44 | private |
| Follow_up_stops_once_the_next_queued_prompt_starts | 32.01 | 4.03 | private |
| Trailing_text_still_follow_ups_when_the_next_prompt_landed_in_the_same_batch | 31.99 | 4.36 | private |
| A_turn_the_bridge_did_not_start_sends_no_reply | 31.95 | 4.16 | private |
| A_mid_turn_launch_note_does_not_steal_the_channel_reply | 31.93 | 4.46 | private |
| A_metadata_only_attachment_becomes_a_visible_could_not_import_note | 31.87 | 4.14 | private |
| A_queued_channel_prompt_still_routes_the_reply | 31.78 | 3.91 | private |
| Text_arriving_after_an_interim_reply_was_sent_still_reaches_the_chat | 31.78 | 4.33 | private |
| Attachment_file_names_from_the_channel_are_sanitized | 31.58 | 3.96 | private |
| An_oversized_attachment_is_skipped_with_a_note | 31.52 | 3.90 | private |
| Follow_up_stops_once_the_next_turn_starts | 31.36 | 4.53 | private |
| Main_path_send_stamps_LastReplyAt_without_touching_inbound_columns | 31.19 | 3.97 | private |
| A_missing_attachment_file_becomes_a_visible_note_not_a_lost_reply | 31.16 | 4.08 | private |
| A_second_distinct_message_on_the_same_conversation_reuses_the_row | 3.76 | <1 | private (unbound, no delivery) |
| Rapid_fire_same_sender_inbound_merges_within_window | 1.93 | 1.12 | private (debounced flush on a background task; test returns on `SubmittedBodies`, does not await confirm) |
| Held_agent_without_fallback_sends_notice_and_does_not_throw | 1.20 | 0.69 | BridgeQueueHarness |
| 15 remaining cases | ≤0.34 each | ≤0.17 each | unbound / working / pure-function |

Totals: baseline 22 cases >20s, 18 <5s; probe 1 case >10s (the Herdr-held one), 38 <5s.

The 21 private-harness routed cases are exactly the set that binds the channel and routes a text or
attachment message while the session is idle (`BindChannelAsync` + `HandleInboundAsync`, no
`MarkSessionWorkingAsync` first). `Bridge_enqueues_with_channel_origin_and_conversation_key`
(line 514) is the control: it calls `MarkSessionWorkingAsync` first, so `IsWorkingAsync` is true, no
inline delivery happens, and it runs in 0.24s under the same production settings.

## 4. Knobs to change, and to what

Scope: `tests/Antiphon.Tests/Application/ChannelBridgeTests.cs`, `HarnessAsync` only. Register an
`IOptions<SupervisionSettings>` whose `DeliveryVerification` is the sibling's block above, verbatim
(values in the "Sibling harness value" column of the §1 table). The probe used exactly that block,
inserted after line 936, and produced the §3 probe column with 40/40 passing and no test edits.

Which of those values matter here: `TranscriptConfirmTimeoutSeconds` 30 → 3 is the whole saving
(each routed case runs the confirm loop to its deadline). `PollIntervalMs` 500 → 50 and
`ReEnterIntervalSeconds` 7 → 1 tighten the loop's granularity and keep the re-press schedule
(3 attempts) inside the 3s window, matching the sibling's "same shape as production, compressed"
intent. `EvidenceTimeoutSeconds`, `PostSubmitAdvanceTimeoutSeconds`, `PostFailureConfirmGraceSeconds`
and `StrandedAgeSeconds` are not reached on the measured path (evidence is immediate; the verdict is
`Confirmed(Screen)`, not `NoTranscriptRecord`; no stranded sweep runs) but mirroring the sibling
verbatim is what the card asks for and costs nothing.

Expected result: class wall time ≈2m40s (≈1m45s of which is the one `BridgeQueueHarness`-based
Herdr-held case, see below), i.e. roughly 5x faster; ~10 minutes off every full `Antiphon.Tests`
run.

The card's framing is confirmed as stated: no new fakes, no assertion changes, no production code.
`[Category("Slow")]` was added to the class by CARD-0487 (`afae299d`, 2026-09-11); whether it still
qualifies as Slow after the fix is a classification question for that card's criteria, not this one.

## Remaining uncertainties

- **`Herdr_held_agent_inbound_is_dropped_...` (45s baseline, 67s probe)** is NOT this mechanism: it
  already uses `BridgeQueueHarness` with the compressed budgets, never types into the adapter, and got
  slower under the probe run, so it is load-sensitive and driven by something else (two inbound
  messages against a Stopped session with a Herdr hold; `EnsureAgentSessionAsync` with
  `AgentStartTimeoutSeconds = 5` and a 2s `SessionPollInterval`, then `AgentControlService.StartAsync`
  raising the hold). Its cause was not measured. After the CARD-0484 fix it is ~40% of the class.
- **The two baseline outliers** (stop-marker 48.25s, redelivered 41.24s) both collapsed to ~4s under
  the probe, so their extra 10–17s over the 31s norm was not a second timeout window; shared-host load
  (two concurrent `Antiphon.Tests.exe` runs) is the likely cause but was not isolated.
- The 2026-09-11 evidence bundle (`C:\Antiphon\worktrees\card-task-bb781960\.antiphon\profile-bb781960\`)
  still exists, but its `remaining-measured.log` contains no `ChannelBridgeTests` lines any more (the
  658.76s/40 figure was reconstructed there via `live-progress.py` against the TRX definitions); today's
  TRX-based per-case table is the first direct per-case record.
- Probe run and baseline run were sequential on the same loaded host (01:30–01:43Z, then
  01:48–01:51Z), not interleaved; the 5x ratio is robust to that but the absolute seconds are not.

## Not done, noted

- Fix idea (one line): register the sibling `DeliveryVerificationSettings` block in `HarnessAsync`
  (or migrate the private harness onto `BridgeQueueHarness` with `ConfigureServices` for
  `ChannelInboundDebouncer`, as the two in-file tests already do; the helper surface matches:
  `BindChannelAsync`, `InsertTurnAsync`, `MarkWorkingAsync`, `InsertTranscriptEntryAsync`,
  `InsertTranscriptEntriesInOneBatchAsync`, `Dispatcher`).
- The same "private harness with no `SupervisionSettings`" shape exists in 13 other test files
  (`AgentChannelServiceIntegrationTests`, `AgentControlServiceIntegrationTests`,
  `AgentStartRecoveryTests`, `BoardServiceIntegrationTests`, `CardCorrectionIntegrationTests`,
  `CardReviewServiceIntegrationTests`, `CardVerificationPolicyTests`, `CardWorkTransitionServiceTests`,
  `DelegationBriefCeilingPtyTests`, `OrchestratorServiceIntegrationTests`,
  `OrchestratorTrackerCadenceTests`, `ReviewLoopTests`, `TestHelpers/OutputDistillationHarness.cs`).
  Whether any of them drives an inline verified delivery (and so pays the same 30s) was not measured.
- `Rapid_fire_same_sender_inbound_merges_within_window` disposes its provider while a 30s confirm loop
  is still running on the debouncer's background flush; harmless today, and goes away with the fix.

## Evidence

- Baseline TRX: `.antiphon/c484-run/channelbridge.trx` (gitignored; per-case table above is its
  full content for cases ≥1s). Probe TRX: `.antiphon/c484-probe/probe.trx`.
- Source lines cited are at tip `3a62074e` (`feat/card-task-0b63c33d`, identical to master for the
  files named).
- Probe diff (reverted, not committed): 17 inserted lines after
  `ChannelBridgeTests.cs:936`, the `DeliveryVerificationSettings` block from
  `BridgeQueueHarness.cs:96-121` wrapped in `services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(new SupervisionSettings { DeliveryVerification = ... }))`.
