# CARD-1373: make MarkWorkingAsync refresh the loaded store

Date: 2026-10-20. Base: `df869f5651bc1400a8e27ef0b85f89db6300fc3c`
(CARD-1362 S2). Target: **master only; test-only**. Next: **code, after
CARD-1362 S1 and S2 land**. Verification design is folded into this plan.
Code budget: **30-45 minutes**. This Plan ran no builds or tests.

Read the card with `scripts/card.ps1 get CARD-1373 -Board Antiphon`.
Owners: [testing/build](../../testing-and-build.md),
[runtime invariants](../../session-runtime-invariants.md),
[project conventions](../../project-context.md),
[orchestration](../../orchestration-loop.md), and [HTTP operations](../../ops-http.md).
All source line numbers below refer to the base above.

## Ground truth

| Card assumption | Code at the base | Plan consequence |
|---|---|---|
| MarkWorkingAsync bypasses a loaded store. | `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:500` calls `InsertTranscriptEntryAsync` (`:395`), which calls the raw EF insert/save at `:410`; neither publishes state. The harness registers a singleton store at `:175`. | Fix this shared helper once. Preserve its optional session ID and SQL row shape. |
| ReadWorkingAsync sees stale idle state. | `server/Application/Services/SessionMessageQueueService.cs:5394` prefers the enabled DI store. `SessionStateStore.cs:49-52` reads the cached projection. SQL-only `SessionMessageQueueService.IsWorkingAsync` (`:5409`) would miss this regression. | Warm the cache idle before the witness's write, and observe the store-backed queue read afterward. |
| The S2 test uses a workaround. | `SessionMessageQueueWedgedHeadTests.cs:373`, especially `:380-390`, publishes an AssistantText event through `ObserveTranscriptAsync` and asserts the store directly inside OnSubmitted. | Restore the callback to `h.MarkWorkingAsync()` after repairing that helper; preserve all delivery/kill assertions. |
| A small test-only repair is possible. | `SessionStateStore.BeginWriteAsync` (`:83`) and `Lease.ReconcileAsync` (`:304`) already provide a gated reload. `server/Antiphon.Server.csproj:12` exposes internals to Antiphon.Tests. | No production API or visibility change is needed. |
| Every same-named helper is this helper. | The source census finds six definitions, with 82 call sites: 57 use BridgeQueueHarness and 25 use five independent helpers. | Enumerate both sets below; only BridgeQueueHarness is in this repair. |

## Decisions

- **D-1 — Reload under the existing store lease.** In
  `BridgeQueueHarness.MarkWorkingAsync`, resolve `sessionId ?? SessionId`, acquire
  `Provider.GetRequiredService<SessionStateStore>().BeginWriteAsync(id, CancellationToken.None)`,
  perform the existing awaited AssistantText insert, then await
  `lease.ReconcileAsync(reset: false, CancellationToken.None)` before disposing the lease.
  Hold the gate across the SQL commit and reload. Keep `"working on it"`, null transcript
  timestamp, harness `Now`, `ConnectionString`, next-sequence allocation, and the optional
  session ID unchanged. The reload incorporates earlier raw fixture inserts too.
  Reject direct `PublishAsync` here because it needs the actual committed row and an
  accurate prior projection; reject `ObserveTranscriptAsync` because its event identity,
  timestamps and runtime effects widen a fixture-only repair. Do not move this behavior
  into the generic insert helper or change the five independent helpers.
- **D-2 — Prove the private reader through its public read path.**
  `GetQueueAsync` (`SessionMessageQueueService.cs:1182`) calls `BuildQueueDtoAsync`,
  which calls `ReadWorkingAsync` at `:5441`; the result is `SessionQueueDto.Working`.
  Use that path in the witness, without changing production visibility or using reflection.
  Reject a cold-only read and the static SQL reader: both can pass with the broken helper.
- **D-3 — One red invocation, one narrow green invocation.** Add one witness to the
  existing wedged-head class; the green filter selects that method and the existing
  working-session kill regression only. The caller census is impact information,
  not permission for a full suite. Keep all existing assertions in the kill regression.
- **D-4 — Portable integration lane, placement resolved at dispatch.** Read
  `GET /api/runner-defaults` and `GET /api/session-runners` on 2026-10-20:
  defaults revision 2; the configured default was stale/draining and another Linux
  runner was eligible and accepting work. Re-read at Code dispatch; omit `-Runner`
  and `-Platform`. These tests need test PostgreSQL and fake adapters, with no native
  terminal or desktop requirement. No fleet address or host pin belongs in this plan.
- **D-5 — Dependency and publication scope.** Start Code only once CARD-1362 S1/S2
  publication to master is confirmed. Land this test repair to master only; no activation
  or release-line change. This adds no session that waits for input and changes no
  session release policy.

## Caller census

Reproduce with `rg -n 'MarkWorkingAsync' --glob '*.cs'` at the recorded base.
Paths in both tables are relative to `tests/Antiphon.Tests/`; each comma-separated
line is another call in that file. Definitions and the comment at
`TestHelpers/BridgeQueueHarness.cs:358` are excluded from the 82-call count.

### BridgeQueueHarness.MarkWorkingAsync — all 57 calls

| File:line(s) | File:line(s) |
|---|---|
| `Application/AgentSystemPromptLaunchTests.cs:341` | `Application/AgentTaskAnswerTests.cs:28` |
| `Application/AgentTaskReplyOverlayTests.cs:34,640` | `Application/ChannelBatchingTests.cs:55,80,109,124,142,158,226` |
| `Application/ChannelInboundRecoveryTests.cs:2058,2127,3133` | `Application/ChannelOutboundUnifiedTransportTests.cs:161` |
| `Application/CheckCompactionReceiptTests.cs:70,117,307,349,373` | `Application/CheckNoteDeliveryHandoffTests.cs:483,674` |
| `Application/CheckNoteDeliveryHandoffTests.H5.cs:29` | `Application/CodexStartupDeliveryTests.cs:600,629,702` |
| `Application/CompactionRecoveryTests.cs:168` | `Application/ContextCompactionSweepTests.cs:112` |
| `Application/DefaultRunnerRerouteTests.cs:241` | `Application/DelegationDispatchRecoveryBoundaryTests.AbsentLaunchRepair.cs:191` |
| `Application/DelegationDispatchRecoveryBoundaryTests.BootStall.cs:235` | `Application/DelegationDispatchRecoveryBoundaryTests.DeadSessionFailedReceipt.cs:109,414` |
| `Application/ExpectationDirectDeliveryTests.cs:27,369` | `Application/ExpectationReceiptTests.cs:29` |
| `Application/ExpectationWatchdogScenarioTests.cs:81` | `Application/GrokRulesCompactionRecoveryTests.cs:101` |
| `Application/MultiRunnerRecoveryTests.cs:252` | `Application/OrchestratorInstructionsRefreshTests.cs:544` |
| `Application/PhoneHomeOutageMentionTests.cs:58` | `Application/PolicyRefreshServiceTests.cs:60,201,534` |
| `Application/ReadyGateFailureNoticeDeliveryTests.cs:93` | `Application/RolePolicyToolBundleDispatchTests.cs:219` |
| `Application/RoutingExhaustedPlacementTests.cs:265` | `Application/SessionMessageQueueDeliveryVerificationTests.cs:211,254,936,1920` |
| `Application/SessionMessageQueueDeliveryVerificationTests.C561.cs:165` | `Application/SessionMessageQueueInterruptedAttemptTests.cs:163` |
| `Application/SessionMessageQueueSpillTests.cs:154` | `Application/SessionMessageQueueSupervisionTests.cs:30` |
| `Application/SubscriptionUsageMonitorTests.cs:115` | `TestHelpers/TaskInputSpillFixture.cs:198` |

The workaround means `SessionMessageQueueWedgedHeadTests.cs:373` currently has no
MarkWorkingAsync call; S2 below restores it. `MultiRunnerRecoveryTests.cs:252`
passes an explicit session ID, so the helper must use the resolved ID for both
the SQL insert and the lease.

### Other definitions — all 25 calls, unchanged

| Definition | Callers, file:line(s) |
|---|---|
| `TestHelpers/LandRefusalDeliveryHarness.cs:158` | `Application/LandRefusalDeliveryWitnessTests.cs:84,211` |
| `TestHelpers/RemoteControlRecoveryHarness.cs:175` | `Application/RemoteControlMaintenanceQueueTests.cs:114,349`; `Application/RemoteControlModalWatchTests.cs:29,69,90`; `Application/RemoteControlRecoveryTests.cs:74,96`; `Application/RemoteControlRecoveryPtyIntegrationTests.cs:125` |
| `TestHelpers/RemoteControlPtyLane.cs:312` | `Application/RemoteControlRecoveryPtyIntegrationTests.cs:42` |
| `Application/ScheduleSweepTests.cs:486` | `Application/ScheduleSweepTests.cs:207` |
| `Application/SessionMessageQueueServiceTests.cs:725` | `Application/SessionMessageQueueServiceTests.cs:96,117,181,204,224,252,283,312,336,366,480,594,625` |

## Slices

**S1 — Add the failing witness.** Change only
`tests/Antiphon.Tests/Application/SessionMessageQueueWedgedHeadTests.cs`, adding
`C1373_MarkWorkingAsync_updates_the_loaded_working_snapshot` as one non-parameterized
`[Test]`. Use the existing class's Integration/Slow/MessageQueue isolation.
Commit and push this witness before CP-1; leave the old helper and workaround intact.
The expected outcome is one assertion failure, not a build or fixture failure.

**S2 — Repair the helper and remove the workaround.** Change
`tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:500` as D-1 specifies; update
its summary to promise an awaited, store-visible write. In
`tests/Antiphon.Tests/Application/SessionMessageQueueWedgedHeadTests.cs:373`, replace
the runtime-event callback with `h.Adapter.OnSubmitted = _ => h.MarkWorkingAsync();`.
Remove its obsolete workaround comments/direct-store assertion and the now-unused
`Antiphon.SessionRunner.Contracts` using. Preserve the assertions for attempts = 2,
NoTranscriptRecord, three Enters, Killed = false, and no generation kills.
Commit and push before CP-2. Production files and other callers need no edits.

### Cost

Code: 30-45 minutes total: 10-15 authoring/reviewing the two small commits,
12 minutes estimated checkpoints (6 + 6), and 8-18 minutes for queue waits,
receipt inspection, output cleanup and reporting. No whole-class, namespace,
full-suite or repetition run is planned.

## Verification design

**V-1 — Red-first loaded-cache witness.** The new
`SessionMessageQueueWedgedHeadTests.C1373_MarkWorkingAsync_updates_the_loaded_working_snapshot`
creates the usual fresh harness, then performs exactly:

```csharp
(await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Working.ShouldBeFalse();
await h.MarkWorkingAsync();
(await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Working.ShouldBeTrue();
```

The first read loads the singleton store's idle snapshot. MarkWorkingAsync is the
only transcript write in this witness; the final queue read calls ReadWorkingAsync
through the path in D-2. No explicit reload, direct SQL working check, runtime
observation, flush, sleep, or retry is allowed in the witness. At S1 the final
assertion must report false instead of true. At S2 it must pass.

**R-1 — Workaround removed without losing the safety assertion.** Execute
`SessionMessageQueueWedgedHeadTests.Working_session_still_withholds_the_kill_after_a_failed_Enter_only`
with the repaired helper callback. Retain its existing receipt/attempt/Enter/kill
assertions. This also exercises the queue-lock -> store-gate order; the helper must
release its state lease before returning and must not invoke queue callbacks.

Run the checkpoint tool once per committed slice, selecting `--rows CP-1` then
`--rows CP-2` with this `--plan` and each slice's full `--expected-source-sha`.
Follow the host build-slot and foreground `run`/`wait` workflow in the testing owner;
if wait returns 75, continue waiting until terminal. CP-1 is intentional red:
retain its failing receipt and exact assertion, and proceed to S2 only for that
failure. A build error, zero tests or unrelated failure is not the red witness.
Keep source frozen during each run. CP-2 must report exactly the two named methods,
2 passed, 0 failed/skipped. Preserve unedited CHECKPOINT lines, counts, commit SHAs
and ignored receipt paths. Code runs `scripts/check-evidence-diff.ps1` over its
base..HEAD, and removes only the build outputs it owns after verification.

Both rows use the **portable PostgreSQL integration / MessageQueue lane**, with
fake adapters. The trailing class/method wildcards and separately parenthesized
method OR operands are required for the pinned TUnit discovery. Table pipes inside
the filter are Markdown-escaped. CP-2 rebuilds because its source differs from CP-1.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1373-red/` | portable-integration-red | `/*/*/SessionMessageQueueWedgedHeadTests*/C1373_MarkWorkingAsync_updates_the_loaded_working_snapshot*` | V-1 red | exactly 1 executed, 1 expected final Working assertion failure, 0 skipped | 1 | 6 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1373-green/` | portable-integration-green | `/*/*/SessionMessageQueueWedgedHeadTests*/(C1373_MarkWorkingAsync_updates_the_loaded_working_snapshot*)\|(Working_session_still_withholds_the_kill_after_a_failed_Enter_only*)` | V-1 green, R-1 | exactly the 2 named methods, 2 passed, 0 failed/skipped | 2 | 6 |
