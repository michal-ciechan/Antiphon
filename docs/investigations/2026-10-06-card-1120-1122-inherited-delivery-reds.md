# CARD-1120 and CARD-1122: 21 inherited delivery reds

Date: 2026-10-06. Investigate only. No product or test file was edited, and the 21 methods were not re-run. Failure text is the checkpoint baseline at master `def23ff2c5c17dbd9f7aaf83d5c8c49d5268488a` (CARD-1073 review, run `20261006-204403-bbb6`). This checkout `feat/card-task-70b27342` at `5b713f6855ee417737ff7d7e47cb340d66e3d9b8` is an ancestor of that master tip. `git diff` of the receipt, retention, resolver, land-notification, harness, and three test files against `def23ff2` is empty. The only dispatcher delta is three lines in `ReleaseUnownedPoolDelegatesAsync` (`ReclaimLegacyAsync`), unrelated to these assertions.

Classification file: `/work/worktrees/task-a36c6c6e/.antiphon/checkpoints/20261006-204403-bbb6/baseline/classification.txt` marks all 21 INHERITED. Baseline counters: CP-2 `AgentTaskLandReceiptTests` 5 executed / 4 passed / 1 failed; CP-13 `PostLandMutationDeliveryTests` 21 / 5 / 16; CP-15 `DispatchBaseNotificationTests` 9 / 5 / 4. Receipt confirmation inside the delivery tests still reached `State == Confirmed` and `ConfirmingPromptSequence == 11` before the census assert.

All 21 are **STALE TEST**. None is a live delivery, notification, or base-selection regression.

## Group A — 3 retention rows — STALE TEST (CARD-1120)

Results:

- `C467_V13_RetentionCancellationAndSupersession(confirmed)`. The other four arguments passed. Message: `AnyAsync(m => m.Id == row.Id) should be False but was True` at `tests/Antiphon.Tests/Application/AgentTaskLandReceiptTests.cs:120`.
- `C478_CompletionReplayAfterRetention`. Message: `CountAsync(m => m.SourceTaskId == settled.World.TaskId) should be 0 but was 1` at `tests/Antiphon.Tests/Application/PostLandMutationDeliveryTests.cs:236`.
- `C478_CompletionReplayAfterPartialEnqueueRetention`. Same shape at line 309.

Why: CARD-0519 S10 keeps a Sent machine-source row while channel-reply discovery is open. `ChannelOutboundEvidence.DiscoverySources` (`server/Application/Services/ChannelOutboundEvidence.cs:10-16`) includes Sent rows of origin Delegation, Check, System, Scheduled, or Channel-with-conversation-key, while `ChannelReplySettledAt`, `ChannelOutboundDeliveryId`, and `ChannelReplyDiscoveryClosedAt` are all null. `ProtectedSources` (`:28-39`) feeds `DataRetentionService.PruneQueuedMessagesAsync` (`server/Application/Services/DataRetentionService.cs:343` and the exclusion at `:359`). Land notes and completion notes enqueue as `QueuedMessageOrigin.Delegation` (`server/Application/Services/AgentTaskLandNotificationService.cs:131-132`). The confirmed arm seeds a UserPrompt at sequence 11 (`AgentTaskLandReceiptTests.cs:108-109`) and never sets `ChannelReplyDiscoveryClosedAt`. The ordinary control row, which has no land id, still prunes (`:119`).

Introducing commit: production `3e53e65834712faa4f26ae7bbaaa9c623e32fd44` (2026-10-05, CARD-0519 S10, "preserve recovery evidence"). The failing assertion line was introduced by `7091ad1d18` (2026-09-10, "Add recovery, retention, attention and upgrade regression coverage") and was not updated. The sibling that states the new contract is `C544_CompletionObligationRetention` in `29c241a33` (2026-10-05, S10 final repair): a confirmed row prunes only when `discoveryClosed: true`; a confirmed-open row is kept (`tests/Antiphon.Tests/Application/DataRetentionServiceTests.cs:1698-1712`, seed at `:1765`).

The CARD-0478 replay hole is not reopened. `PruneQueuedMessagesAsync` still calls `CompletionNoteStamp.RepairFromAsync` (`DataRetentionService.cs:344-349`) before delete. These tests fail because they require deletion of a row the new contract keeps.

Live effect of the production change: a confirmed Delegation land-note or task-completion queue row survives prune until discovery closes. That is the S10 rule. An abandoned mid-turn source with no TurnEnd stays protected; that bound is already the subject of CARD-1120 and is not what these three assertions measure.

Not done, noted: seed `ChannelReplyDiscoveryClosedAt` on the confirmed arm, mirroring C544, and keep asserting that an open discovery source is retained and that a closed one prunes.

Risk if left: these three stay red. Queue history for an open discovery source is retained by design.

## Group B — 13 second-UserPrompt rows — STALE TEST (CARD-1122)

Every one fails with UserPrompt count `should be 1 but was 2`, after `State == Confirmed` and `ConfirmingPromptSequence == 11` have already passed where the helper asserts them. The Inputs equality on the next line is not reached.

Results, all count 1 versus 2:

- `C478_G134_LandAtomic`, `C478_G135_LandEnqueue`, `C478_G136_LandQueueKey`, `C478_G137_LandWakeup`, `C478_G138_LandBusy` — assert at `PostLandMutationDeliveryTests.cs:972`.
- `C478_G156_CompletionRestore` — its own assert at `:560`.
- `C478_V09a_LandProducerToCaller` — `:972`.
- `C478_V09a_LandCrashMatrix` arguments `before-enqueue` true and false, `queue-inserted` true and false, `lost-wakeup` true and false — `:972`.

The five crash-matrix arguments that passed are `receipt-before-save` true and false, `after-receipt` true and false, and `post-submit-process`. `publication-commit` is group C.

Why the failing arms have two rows: `ConfirmLandReceiptAsync` (`:916-973`) flushes when `alreadySubmitted` is false and the row is not yet Sent (`:941-944`). The harness `OnSubmitted` (`tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:319-346`, CARD-0055) inserts a UserPrompt at the next sequence, typically 1, with no receipt uuid, plus a TurnEnd. The helper then forces Sent, baseline 10, and `SetTranscript` of a second UserPrompt at sequence 11 with uuid `c478-land-` plus the note id (`:962-964`). `ReconcileAsync` calls `CatchUpTranscriptAsync` (`AgentTaskLandNotificationService.cs:240`, `AgentSessionRuntime.cs:706-710`). `PersistTranscriptCoreAsync` dedups a uuid event by uuid, not by sequence (`AgentSessionRuntime.cs:903-909`). Sequence 11 is free, so CatchUp stores the receipt. `LandNoteReceipt` only counts prompts with sequence above the floor of 10, so the harness row at sequence 1 does not change `ConfirmingPromptSequence`.

Why the passing arms stay at 1: they call the helper with `alreadySubmitted: true` (`:692`, `:753`, `:758`). Flush is skipped, so `OnSubmitted` does not add a row. `receipt-before-save` and `post-submit-process` pre-seed a UserPrompt at sequence 11 (`:683-688`, `:742-747`). The helper's first `ReconcileAsync` confirms from that stored row and saves. The later reconcile returns immediately because state is already Confirmed (`AgentTaskLandNotificationService.cs:45`), before CatchUp, so the injected snapshot is not stored. `G156` uses `ConfirmQueuedReceiptAsync` (`:555-556`, `:976-1002`): a still-pending row is flushed (`OnSubmitted` writes one UserPrompt), then `ConfirmPersistedQueuedReceiptAsync` (`:1021-1099`) CatchUp-persists a second receipt snapshot. That helper only requires `Count >= 1` (`:1099`). The test's own `ShouldBe(1)` at `:560` is what fails.

The production queue does not insert `TranscriptEntries`. A live session records the submitted prompt once. The extra row is the harness model of that prompt plus the helper's separately injected receipt snapshot, at a different sequence and uuid, so uuid-dedup cannot collapse them. Unique `(AgentSessionId, Sequence)` (`server/Infrastructure/Data/AppDbContext.cs:1495-1498`) is why a same-sequence replay is skipped (`AgentSessionRuntime.cs:1044-1060`); it does not apply here because the two writers use different sequences.

Introducing commits: the count assert at `:972` is `c989e884b8` (2026-09-11, CARD-0478). G156's count at `:560` is `7467fe0966` (same day, CARD-0478). The harness UserPrompt insert is `013496469` (2026-08-16, CARD-0055), which is earlier than the assert. This week's CARD-0519 slices are not required to explain the split.

Remaining uncertainty: the stored TRX stops at the count, so it does not itself prove the adapter submitted only once. The code path is one flush, then a reconcile that confirms and does not enqueue again.

Not done, noted: assert the confirming prompt at sequence 11 and that adapter Inputs did not grow during reconcile, and stop requiring the whole session to contain exactly one UserPrompt.

Risk if left: thirteen reds in the land-receipt class. Confirmation at sequence 11 already holds, so this is not a lost receipt and not a second prompt typed into a live composer.

## Group C — 1 publication-commit ready file — STALE TEST (CARD-1122, CARD-0890 host)

`C478_V09a_LandCrashMatrix(publication-commit, False)` fails at `PostLandMutationDeliveryTests.cs:799`: `File.Exists(ready) should be True but was False`. Additional info: `ParentContainsErrorRecordException` from `protocol-worker.ps1:5`, `Exception calling "Invoke": The type initializer for '<Module>' threw an exception.` The child is still `ProcessStartInfo("pwsh")` at `:785` and loads the test assembly with `Assembly.LoadFrom` (`:777-783`) before `RunCrashWorkerAsync` can write `worker-ready.json`.

The sibling `post-submit-process` arm launches through `dotnet` in `PostLandMutationDeliveryWorker.CrashAtReceiptSaveAsync` (`tests/Antiphon.Tests/TestHelpers/PostLandMutationDeliveryWorker.cs:34`) and passed. `docs/investigations/2026-10-02-flaky-test-root-causes.md` already records this host for `AgentTaskLandRecoveryTests` and names the inner `FileLoadException: Could not load System.Text.Json, Version=10.0.0.0`. That inner exception was not re-probed in this baseline; the baseline message stops at the module initializer. `AgentTaskLandRecoveryTests.cs` is still on `pwsh` as well and is not one of these 21. The in-process publication path is not what failed.

Not done, noted: launch `PublicationCommitCrashAsync` through `dotnet` and the existing worker mode, the way `CrashAtReceiptSaveAsync` already does, and keep `File.Exists(ready)`.

Risk if left: the publication-commit crash boundary is untested on this host. Independent of group B, and smaller.

## Group D — 4 dispatch-base results — STALE TEST (CARD-1122)

The other `C508_RefusedClaimHasNoIntent` arguments passed (invalid-ref, optional-expiry, failed-baseline, stale-claim, lease).

1. `C508_RefusedClaimHasNoIntent(sibling-hold)`. The fixture sets only `sibling.LandRequestedAt` (`DispatchBaseNotificationTests.Coverage.cs:539`). Intent count 0 passes. Status fails at `:584`: `should be AgentTaskStatus.Queued but was AgentTaskStatus.Dispatched`. The hold predicate (`AgentTaskDispatcher.cs:4345`) is a pending land request in state Queued, Held, or Running. It was changed from `LandRequestedAt is not null` by `0d9dfb6f69` (2026-09-26, CARD-0753). The resolver uses the same pending-request set (`AgentTaskWorktreeBaseResolver.cs:81-85` and the WaitForLand returns at `:208-210` and `:327-328`). A live land writes `CurrentLandRequestId` and `LandRequestedAt` together (`AgentTaskLandService.cs:297-298` and `:389-390`). `LandRequestedAt` alone is a stale fixture signal. The repair-source owner loop still reads `LandRequestedAt` for a different owner (`AgentTaskDispatcher.cs` around `:767`); that is not this sibling hold.

2. `C508_ClaimCapturesWarningIntents`. First failure is `DispatchBaseNotificationTests.cs:72`: `WorktreeBaseSource should be RepoHead but was CardCurrent`. Later intent-count asserts were not reached. The test comment (`:58-59`) expects a deleted trunk to fall back to HEAD and to owe sibling, mismatch, and unresolved-default warnings.

3. `C508_ClaimIntentAtomic` (`Coverage.cs:273`): `interceptor.Fired should be True but was False`. `ClaimCommitFailure` (`:695-716`) throws only when an `AgentTaskDispatchWarningIntent` for the task is in the change tracker. No drafts means the interceptor does not fire and the claim commits.

4. `C508_IntentCaptureRoute` (`Coverage.cs:320`): `intents should not be empty`.

Cause of 2–4: `ab9094127b` (2026-09-27, CARD-0442). A succeeded kept sibling with unmerged commits returns `CardWorktreeBaseDecision.Continue` (`AgentTaskWorktreeBaseResolver.cs:315-319`). The dispatcher sets `WorktreeBaseSource.CardCurrent` (`AgentTaskDispatcher.cs:4864-4866`) and, when that base is set, clears `siblingObservation` (`:4874`). `BuildDispatchWarningDrafts` (`:5329-5366`) then emits no sibling, mismatch, or unresolved-default drafts, so `CaptureAsync` stores nothing. Suppressing a sibling warning when the base is that sibling is the continuity contract. A real pending land still returns WaitForLand (`:4839-4842`). The unresolved-default draft still exists when the resolver returns Target and the decision records `UnresolvedDefault` (`:5359-5363`). The tests were last edited before both commits.

CARD-1087 and CARD-1065 do not own these lines. The three-line dispatcher delta on master is the seat-release reclaim above, not this selection.

Not done, noted: point the four fixtures at the continuity contract (assert CardCurrent and no sibling warning when the kept sibling is the base; sibling-hold must seed a pending land request, not only `LandRequestedAt`), or add a separate fixture with no eligible sibling when a vanished-default warning is still required.

Risk if left: four reds in the dispatch-base class. Empty intents here are the continuity contract, not a broken capture path.

## Ordering

1. Group C, the publication-commit host. Independent, and the sibling worker already shows the shape.
2. Group A, test-only update to the S10 contract already pinned by C544. CARD-1120.
3. Group D, fixture update to the CARD-0753 hold and the CARD-0442 continuity contract.
4. Group B last. Test-only census. Confirmation at sequence 11 already holds.

## Not a live regression

No result shows a land receipt lost, a second prompt typed into a live composer, a completion note re-enqueued after prune, or a missing warning for a base that is not the sibling. Next stage is plan: four test-only slices, closed by the named methods going green without weakening the receipt, retention-after-close, pending-land hold, or continuity assertions.
