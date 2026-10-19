# Windows rc new-red classification at 42cbbd04, 2026-10-19

Task `90dcf4ee`. No product or test source was edited. AppHost and the production runner on port 17204 were not touched. E2E runs used the fixture's own runner (`LandDeliveryFixture` asserts the owned runner port is not 17204).

The gate's "new-in-rc" label is not a regression inside `2b31b851..42cbbd04`. Every stable red fails the same way at `rc/2026-10-15` `61117dd1663d2f47ce4708c18b609b8936491986`. The websocket row passes alone at both commits.

## SHAs and runs

| Role | SHA | Worktree |
|---|---|---|
| rc tip | `42cbbd0417a2611f3a690e6671b13c9ba51fbf34` | `C:\Antiphon\worktrees\rc-tip-90dcf4ee` |
| rc/2026-10-15 | `61117dd1663d2f47ce4708c18b609b8936491986` | `C:\Antiphon\worktrees\rc-base-90dcf4ee` |
| Gate log | run `7daa77ac9d994e1a8afa619db0db6ae3` | `C:\Antiphon\releases\candidates\rc-2026-10-18-42cbbd0417a2\logs\2026-10-19-0407-7daa77ac` |

Builds went through `scripts/build-slot.ps1` with `--property:OutputPath=bin-90dcf4ee/`. Tip and base builds of `Antiphon.Tests`, `Antiphon.SessionRunner.Tests`, and `Antiphon.E2E` each exited 0. Each test was one filtered exe run, capped at 5 minutes. No suite was run.

`git diff 61117dd16 42cbbd04` does not change `tests/Antiphon.E2E`, `PhoneHomeConnectionService.cs`, `PhoneHomeConnectionServiceTests.cs`, or `AgentTaskLandBareRepositoryCleanupTests.cs`. The server delta in that range is KeepSix plus `CodexTranscriptNormalizer.cs`. Merges after `2b31b851` are CARD-1338, CARD-1341, CARD-1343, CARD-1342, CARD-1333, CARD-1344 (`1f72e2015`, read-only fixture delete), and CARD-1345 (`42cbbd04`, e2e watchdog key). None of those edits is the failing assertion.

## Gate failures that actually returned

The e2e console has 19 `failed` lines. All of them are the four signatures below. `WorktreeRetirementDeliveryE2ETests`, `AgentE2ETests`, and `BoardE2ETests` have no returned failure in that log. The earlier hangs note recorded those classes still publishing when the e2e watchdog closed the file.

### 1. `C1211_PublicationPreservesBareRepository(obj/head-only, head-only)` — inherited, Windows

Gate (`antiphon-all-tests.log:1644`): `UnauthorizedAccessException: Access to the path '762c69ea31f95db2ca5f82abc63edb10d1cf46' is denied` at `AgentTaskLandBareRepositoryCleanupTests.cs:40` (`Directory.Delete` of the bare repo's `objects` directory). The filename is a git loose object. Windows `Directory.Delete` does not clear the read-only bit git sets on objects.

Isolated rerun, same method filter (5 argument rows):

| SHA | Result |
|---|---|
| tip | total 5, failed 1, succeeded 4, 2m 29s. head-only failed in 4.5s, path `52956cf058f824fd8441e502e5e53d16dac8c0` |
| base | total 5, failed 1, succeeded 4. head-only failed in 4.3s, path `cb99b9c54ff7f136f4edf9dcfe583889f5d068` |

The other four shapes passed at both SHAs. The test file last changed at `4f6c24cf6` on 2026-10-13, before this rc window. CARD-1344 added `tests/Shared/ReadOnlyDirectory.cs` for this exact exception and did not switch this call site.

### 2. `Websocket_connect_that_hangs_ends_within_the_connect_timeout` — flake

Gate (`session-runner-all-tests.log:220`): 145ms. `Should.ThrowAsync<OperationCanceledException>` got `WebSocketException` at `PhoneHomeConnectionServiceTests.cs:559`. The test opens a TCP listener that accepts and never speaks HTTP, sets `ConnectTimeoutSeconds` to 1, and expects the connect to cancel.

Isolated rerun: tip 1 passed in 1.583s; base 1 passed. The test and `PhoneHomeConnectionService.RunConnectionAsync` are unchanged across `61117dd16..42cbbd04`. Last test-file change is `16804f367` on 2026-09-28.

### 3. E2E land exit 1 / `review_evidence_source_not_clean` — inherited (6 gate rows)

Gate rows, all `ShouldAssertException: process.ExitCode` should be 0 but was 1, `LandDeliveryFixture.RequestAsync` at `LandDeliveryFixture.cs:178`, called from `C488_ReviewToLandReceiptMatrix`:

- `(idle, Apply)` 2m 52s
- `(busy, spill)` 2m 32s
- `(busy, Apply)` 2m 32s
- `(busy, raw)` 3m 06s
- `(idle, spill)` 2m 42s
- `(idle, raw)` 2m 37s

The idle/Apply gate text is HTTP 409 `review_evidence_source_not_clean`: "Review evidence does not assert a verified clean source." That is `LandApproval.LoadUsableEvidenceAsync` when `ReviewedSourceClean != true` (`server/Application/Services/LandApproval.cs:77`).

Isolated representative: `C488_ReviewToLandReceiptMatrix(idle, raw)` at both SHAs, 2m 55s and 2m 54s, same exit 1 and the same 409 code. The other five rows were not executed in isolation. A fail-fast on the method filter cancelled them after the first failure (`TaskCanceledException`); that cancellation is not an assertion result. The gate already shows those five failing the same exit-code assertion.

### 4. E2E stale review text — inherited (1 gate row)

`C488_ApprovalOutcomeReceiptMatrix(stale-A)`, 2m 33s in the gate. `RefuseStaleReviewAsync` (`LandDeliveryFixture.Review.cs:406`) expects the delegate text to contain `review_evidence_sha_mismatch`. The text was a truncated `Antiphon POST .../land/v2 failed` body.

Isolated: tip failed 2m 53s; base failed 2m 51s. Same assertion, same truncated POST failure. The 409 code is not in the assertion text (Shouldly truncates it). Signature 3 shows the success-path land of the same fixture family refusing with `review_evidence_source_not_clean` before a sha-mismatch refusal would be reached.

### 5. E2E missing `completion-scan-*.observation.json` — inherited (2 gate rows)

- `C467_V28_LostFlushWakeupRecoversOnIdleCaller` 46s, `AgentTaskLandDeliveryE2ETests.cs:154`
- `C488_ApprovalLostFlushRecovers` 46s, same assertion, same frame in the gate extract

Isolated `C467_V28`: tip failed 1m 11s; base failed 1m 09s. `Directory.GetFiles(..., "completion-scan-*.observation.json")` was empty. The sibling was not rerun. This row is already recorded as pre-existing in `docs/investigations/2026-10-16-release-candidate-native-e2e.md` and the 2026-09-27 Windows verification note.

### 6. E2E C540 quiet-60s — inherited (10 gate rows)

All ten are `TimeoutException` from `ProgressAwareWait.UntilAsync` (`tests/Shared/ProgressAwareWait.cs:38`) via `DispatchBaseWarningDeliveryE2ETests.CrashAsync`. Each returned after progress, then 60s quiet. Not a hang.

Isolated representative `C540_PreEnqueueCrashRecoversWarnings`: tip failed 1m 57s, `before-enqueue (quiet 60s after 66.4s; progress 46)`; base failed 2m 02s, `quiet 60s after 66.1s; progress 46`. The other nine gate rows use the same wait with different boundary names (`CollapsedWarningsReachIdleCaller`, `ClaimCrashRecoversCollapsedWarnings`, `QueueInsertCrashReusesRows`, `EnqueueFailureRetriesWarnings`, `ProjectionCrashRecoversOriginalPairs`, `ReceiptCrashDoesNotRetype`, `PostPromptCrashDoesNotRetype`, `CollapsedWarningsWaitForBusyCaller`, `PreTypingCrashRecoversWarnings`). They were not rerun one by one.

## Ranked fixes

1. **rc-line, small.** In `C1211_PublicationPreservesBareRepository`, delete the head-only `objects` / `refs` tree with `ReadOnlyDirectory.Delete` (CARD-1344) instead of `Directory.Delete`. That is the one stable Windows red with a helper already on this rc. The four other argument rows already pass. A master-only edit does not clear `rc/2026-10-18`.

2. **rc-line, not this window, not small.** The 19 e2e rows are inherited from `61117dd16`. Do not treat them as a merge in `2b31b851..42cbbd04`. A later Code task on the rc line can make the review fixture record `ReviewedSourceClean == true` (signatures 3 and 4), restore the `completion-scan` observation (signature 5), and unblock the C540 `before-enqueue` wait (signature 6). A master-only land does not move this rc.

3. **No code change.** The websocket failure passes alone at both commits. It is a suite flake, not a stable new red.
