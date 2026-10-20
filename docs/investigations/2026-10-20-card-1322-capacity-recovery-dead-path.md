# CARD-1322: capacity recovery dead path

Confirmed at S2 tip `30b27427d1c4d77940b29481764a3d27c42d34cd`: `DispatchCapacityRecoveryTranscriptAsync` is dead production code, but S2 also fixes the reachable deferred-action path. The existing S2 witness exercises that path before its reflection-only arm. No production reachability repair is required.

Investigation date: 2026-10-20. Task: `ef59a120`. Assigned branch: `feat/card-task-ef59a120`. This is a source/history reconstruction using committed transcript fixtures and the existing witness's database operations, not a new live capture or test execution. No builds, tests, production database reads, provider input, or product/test edits were performed. File:line references below are at the S2 tip unless explicitly qualified with another commit.

## 1. The helper is dead, and its caller moved in CARD-1259

The complete tracked-code search:

```text
git grep -n DispatchCapacityRecoveryTranscriptAsync HEAD -- server src tests

HEAD:server/Application/Services/AgentSessionRuntime.cs:568:    private async Task DispatchCapacityRecoveryTranscriptAsync(
HEAD:tests/Antiphon.Tests/Application/CapacityRecoveryTranscriptTests.cs:125:            var dispatch = typeof(AgentSessionRuntime).GetMethod("DispatchCapacityRecoveryTranscriptAsync",
```

The method is private (`server/Application/Services/AgentSessionRuntime.cs:568-583`); the only invocation found is test reflection at `tests/Antiphon.Tests/Application/CapacityRecoveryTranscriptTests.cs:125-127`. There is no production reference. The real caller of `CapacityRecoveryService.ObserveTranscriptAsync` is now `ExecuteActionPhaseAsync`, phase 1 (`server/Application/Services/AgentSessionRuntime.cs:388-392`).

Pickaxe over the assigned tip's ancestry identifies both introduction and caller removal:

```text
git log --date=iso-strict --format='%H %ad %s' \
  -S 'DispatchCapacityRecoveryTranscriptAsync' \
  -- server/Application/Services/AgentSessionRuntime.cs

dd039a405c9425d7cffc83b64d60a1032d940636 2026-10-15T18:09:47Z WIP CARD-1259 S2: persist deferred transcript actions and own bounded workers
dc57b859da743dc50442a19ec751852801234215 2026-09-08T07:55:21+01:00 fix(CARD-0412): hold provider advisory locks across grant/redeem/wave
```

`dc57b859da743dc50442a19ec751852801234215` introduced the helper and its call after transcript persistence. The removal commit is `dd039a405c9425d7cffc83b64d60a1032d940636`, authored October 15 at 18:09:47 UTC and committed October 16 at 02:10:41 +01:00:

| Stored revision | Evidence |
|---|---|
| Parent `ee5e0fede35da4685b37b9f0deca3f251e3250b2` | `server/Application/Services/AgentSessionRuntime.cs:382-388` persists the event, then calls the helper for a newly stored UserPrompt, QueuedUserPrompt or TurnEnd. |
| Removal `dd039a405c9425d7cffc83b64d60a1032d940636` | The same file at `:369-371` reduces ingestion to persistence; `:388-391` moves capacity observation into deferred phase 1. The old helper body remains at `:561-576`. |
| S1 `c28e7b52b097210ae62e541a0c5a55a8b85d0ad9` | The only production-tree name occurrence is the definition at `server/Application/Services/AgentSessionRuntime.cs:567`. Thus S1 did not cause the dead path. |

The recorded reason for moving the call was to stop the event reader awaiting delivery confirmation whose UserPrompt was still waiting to be read, while retaining ordered effects in durable, bounded workers (`docs/superpowers/plans/2026-10-15-card-1259-transcript-pump-decoupling-plan.md:12-16`). That plan explicitly places capacity recovery after each committed prompt/TurnEnd (`:156-162`). The diff implements this migration and leaves the old helper behind. Why its body was not removed is not recorded; attributing that to a particular author's intent would be speculation.

## 2. A cancelled Codex turn reaches the S2 predicate in production

The committed input witness is `tests/Antiphon.SessionRunner.Tests/CodexTranscriptNormalizerTests.cs:178-184`:

```json
{"timestamp":"2026-08-13T15:24:10.773Z","type":"event_msg","payload":{"type":"turn_aborted","turn_id":"01a0abort","reason":"interrupted","duration_ms":865}}
```

This is the reconstructed row already documented at `docs/investigations/2026-10-17-card-0242-investigation.md:44-47`, not a new capture. The test also retains the no-turn-ID row at `CodexTranscriptNormalizerTests.cs:186-192`.

The production chain preserves the relevant fields:

| Step | Source evidence and consequence |
|---|---|
| Normalize the persisted Codex line | `src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs:177-185` selects `FromTurnAborted`; `:374-386` emits one TurnEnd with `StopReason = Cancelled` and optional ApiCallId, without setting IsApiError. The constant is exactly `cancelled` in `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:408-411`. |
| Publish the normalized runner event | `src/Antiphon.SessionRunner/CodexTranscriptTailer.cs:351-363` reads complete JSONL lines; `:372-397` normalizes, copies StopReason and IsApiError into `RunnerTranscriptEvent`, and publishes SessionTranscript. |
| Preserve fields across transport | Phone-home `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs:555-564` parses frames through `RunnerContractMapper`; that mapper deserializes SessionTranscript at `RunnerContractMapper.cs:116-121` and copies StopReason/IsApiError at `:62-68`. The HTTP stream's equivalent mapping is `SessionRunnerHttpClient.cs:999-1004` and `:1047-1072`. |
| Ingest through the runtime | `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs:419-420` invokes runtime ObserveTranscriptAsync; `server/Application/Services/AgentSessionRuntime.cs:369-371` invokes persistence. No legacy helper is involved. |
| Store the row and its action | `AgentSessionRuntime.cs:1196-1226` stores Kind, StopReason (`:1211`) and IsApiError (`:1217`). `:1229` admits actions, and `:1261-1262` saves. `:1297-1303` admits an action for every new row, with the row's boundary classification. `server/Application/Services/SessionRuntimeActionCoordinator.cs:38-49` links it through TranscriptEntryId. |
| Schedule real production execution | `AgentSessionRuntime.cs:1074-1075` publishes committed state and hints the coordinator. `server/Program.cs:694,750,916-917` registers the coordinator, capacity service, hosted action worker and event pump. `server/Infrastructure/Agents/SessionRunner/SessionRuntimeActionHostedService.cs:5-7` runs the coordinator; `SessionRuntimeActionCoordinator.cs:79-84,120-128` starts workers and drains hints. |
| Reload and execute phase 1 | `SessionRuntimeActionCoordinator.cs:156-175` selects durable actions; `:203-214` reloads the TranscriptEntry by ID and executes phases 0 through 7. `AgentSessionRuntime.cs:388-392` admits TurnEnd by kind and passes **row.StopReason** to the capacity service. This is an S2 change in `30b27427d1c4d77940b29481764a3d27c42d34cd`. |
| Reject cancellation as progress | `server/Application/Services/CapacityRecoveryService.cs:724-731` selects an eligible wait. `:739` requires `IsReportBoundary(kind, stopReason) && !isApiError`. `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:474-475` excludes exact `cancelled`. The Progressed/Outcome/Version/save block at `CapacityRecoveryService.cs:741-746` is therefore skipped. |

The two boundary rules are distinct. Cancellation remains an idle boundary (`AgentSessionRuntime.cs:499-507`), so the transcript/action is not dropped. Capacity phase 1 is gated by row kind, not `action.TurnBoundary` or successful report status. Even the coordinator's non-boundary removal occurs **after** phases 0-7 (`SessionRuntimeActionCoordinator.cs:207-222`). The later report predicate rejects progress without suppressing ingestion.

Catch-up also persists newly discovered rows through the same admission path (`AgentSessionRuntime.cs:961-967`). Duplicate UUID/kind rows are skipped at `:1183-1186`; they do not need the old helper to replay effects already represented by durable action debt.

At S1, the reachable phase-1 call omitted StopReason (`c28e7b52b097210ae62e541a0c5a55a8b85d0ad9:server/Application/Services/AgentSessionRuntime.cs:388-391`), and the capacity service accepted every non-error TurnEnd (`same commit:server/Application/Services/CapacityRecoveryService.cs:738-745`). This reconstructs the original bug: a cancelled non-error TurnEnd changed an eligible wait to Progressed. S2 changes both this reachable call and the service predicate, in addition to updating the dead helper.

## 3. The existing witness already exercises the real path

`tests/Antiphon.Tests/Application/CapacityRecoveryTranscriptTests.cs:23-24` iterates `stored = true` **before** `false`, with normal, null and other completion reasons. For the stored arm:

- `:38-49` registers, grants and redeems a Codex wait, then confirms its prompt.
- `:112-119` calls public runtime ObserveTranscriptAsync, rebuilds the container, and drains the real SessionRuntimeActionCoordinator.
- `tests/Antiphon.Tests/TestHelpers/SessionStateTestFixture.cs:43-73` constructs that container over the same isolated PostgreSQL schema, with real runtime/coordinator services and the supplied capacity registration. This is not a mock capacity callback.
- `CapacityRecoveryTranscriptTests.cs:61-83` submits cancellation, reloads the wait from a fresh context, checks unchanged state/outcome/version/prompt sequence, checks persisted StopReason/IsApiError, and requires no outstanding action rows. Reflection occurs only in the separate `stored=false` branch at `:121-127`.

These are the existing witness's row-level checks, reconstructed from its source; this investigation did not execute or query them:

| Row/event in the stored arm | Asserted database result |
|---|---|
| Sequence 1, UserPrompt | CapacityRecoveryWait.State = PromptConfirmed; ConfirmedPromptSequence = 1 (`:46-49`). Call its version V. |
| Sequence 2, TurnEnd/end_turn, IsApiError = true | State remains PromptConfirmed; Version = V (`:51-59`). |
| Sequence 3, TurnEnd/cancelled, IsApiError = false | State/Outcome remain PromptConfirmed; Version = V; prompt sequence = 1, including replay (`:61-75`). The stored TranscriptEntry retains cancelled/false, and SessionRuntimeActions is empty (`:76-83`). |
| Sequence 4, TurnEnd with end_turn/null/future_reason, IsApiError = false | State/Outcome = Progressed; Version = V+1 (`:86-95`). Replay and a subsequent completion do not increment again (`:97-100`). |

The compiling witness commit `63e1f18706369651fb949d8e4653ecaa34411aa6` already has this stored-first structure. Against its pre-S2 capacity code, the source-predicted failure is `afterCancel.State.ShouldBe(PromptConfirmed)` at `:67`, because the live phase writes Progressed. That assertion occurs before the reflection arm. This is a reconstruction of the failure mechanism, not a newly measured red result.

The existing plan's V-2 and CP-2 identify the same witness (`docs/superpowers/plans/2026-10-20-card-1322-codex-turn-aborted-follow-ups-plan.md:65,89`). Its exact filter is `/*/*/CapacityRecoveryTranscriptTests/C1322_Cancelled_boundary_does_not_progress_recovery`. No new witness, filter, or execution is requested by this finding: the brief's conditional production repair is unnecessary.

## Remaining uncertainties and scope

- No tests or builds ran, as commissioned. This report does not revalidate historical red/green receipts or replace the supplied S1/S2 Review outcomes.
- No running server SHA or live cancelled session was inspected. The confirmed claim is reachability and behavior at the assigned S2 source tip, conditional on successful persistence and eventual action execution. It is not a deployment/activation claim.
- The witness drives normalized events and manually drains the real coordinator. It does not itself run Codex, transport, or hosted scheduling. Those links above are established by source inspection; the raw row's provenance remains the earlier reconstruction.
- A prior failed phase can defer capacity execution; the coordinator retains failures for retry (`SessionRuntimeActionCoordinator.cs:177-188`). That existing execution condition is not evidence that phase 1 is dead.
- This conclusion covers the S2 capacity-recovery concern only. It does not dispose of the other CARD-1322 slices or the separate replaced-turn ordering measurement.

## Not done, noted

Optional cleanup: remove the unused helper and its reflection-only test arm while retaining the existing stored-action witness; no implementation or test changes were made here.

--- next stage ---
next: none
handoff: S2 already forwards persisted StopReason through production phase 1 and the existing stored-action witness covers it; no reachability repair is required. The unused helper predates S1 and is optional cleanup.
artifact: docs/investigations/2026-10-20-card-1322-capacity-recovery-dead-path.md
