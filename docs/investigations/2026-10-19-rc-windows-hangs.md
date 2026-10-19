# RC Windows watchdog kills, 2026-10-19

Task `5b8ad483`. No product or test code was edited. The AppHost and the production runner on port 17204 were not touched.

The named set does not hang. On the second Windows rc gate, and again in one slotted rerun of each class or method at the same SHA, KeepSix, the effort-prompt class, and the two Codex launch-policy methods all return. `AgentTaskLandDeliveryE2ETests` does not deadlock either: it is a strictly serial class whose cases return in about 36 seconds to 3 minutes, and that class alone fills the 60 minute e2e fallback. Three of its cases return and then fail an assertion. The watchdog kills are a misread of TUnit's `--no-progress` diagnostics, plus one e2e class that needs more than an hour.

## How a finished test was counted as a hang

`docs/investigations/2026-10-19-rc-watchdog-sizing.md` (task `210e914f`) read the first `InProgressTestNodeStateProperty` for each UID, and treated "no console line" as still running. Both signals are wrong for this runner.

- Console output is `--no-progress`. A pass is not printed. A `failed` or `skipped` line means the body returned. The kill deletes the process before the end-of-run summary, so a passing test has no console line by construction.
- The passing or failing state is often the next physical line of the same `TestNodeUpdateMessage`, after the UID line. A parser that keeps only the first `InProgress` on the UID line never sees it.
- Two-argument UIDs contain a space (`C488_ReviewToLandReceiptMatrix(System.String, System.String)`). A UID token that stops at whitespace drops that test and under-counts the suite.

The read used here keeps the last state property for each UID, and takes the UID through the closing ` }`. A `Duration` means the body returned. `Error` with a duration is a returned failure (TUnit did not flush a `Failed` state; the console line is `failed`). `InProgress` with no duration is the only still-running set.

Gate logs, read-only: `C:\Antiphon\releases\candidates\rc-2026-10-18-1f72e20158b9\logs\2026-10-18-2318-d081f742`. SHA `1f72e20158b99434cb6398fb7efbbd133d1c5569`. Watchdogs are `tests/test-execution-policy.json:737-749`: `antiphon` 3600000, `session-runner` 1200000, and no `e2e` key. `Get-NightlyWatchdogMs` (`scripts/lib/nightly-tests-impl.ps1:66-71`) falls back to 3600000 at the call on line 524.

Last state of every test UID in that gate:

| Suite | Test UIDs | Passed | Error, with duration | Skipped | InProgress, no duration |
|---|---:|---:|---:|---:|---:|
| antiphon | 2771 | 2697 | 69 | 4 | 1, at `00:32:55`, the kill second |
| session-runner | 1548 | 1499 | 31 | 17 | 1, at `00:52:59`, the kill second |
| e2e | 74 | 67 | 3 | 3 | 1, at `02:00:56`, the kill second |

The antiphon leftover is `AgentTaskLandBareRepositoryCleanupTests.C1211_NestedTrackedHeadKeepsExistingAdmission(bool)` argument 2. The session-runner leftover is `CompletedCardWorkspaceRemovalTests.C1032_RemovalEligiblePhysical`. Neither is in the named set. Each was admitted on the kill timestamp (`n=1`, no duration). The e2e leftover is one land-matrix row, below.

Suite maxima among tests that returned: antiphon 1m 26s, session-runner 2m 36s, e2e 3m 08s. The four `TranscriptIngestionLatencyProbeTests` the sizing note also called silent all passed (0.01s to 30.4s).

## What was run

Detached checkout of `1f72e20158b99434cb6398fb7efbbd133d1c5569` in this worktree. Each command went through `pwsh -NoProfile -File scripts/build-slot.ps1` with `--property:OutputPath=bin-rc5b8a/`. A 5 minute wrapper killed the process tree on expiry. Those output directories were removed afterwards. Nothing hung, so nothing was re-run at `61117dd1663d2f47ce4708c18b609b8936491986`. That commit is an ancestor of the tip.

`git diff 61117dd1 1f72e201` is empty for `RunnerClaudeAdapterEffortPromptTests.cs`, `CodexWindowsLaunchPolicyTests.cs`, `CodexWindowsLaunchPolicy.cs`, and `AgentTaskLandDeliveryE2ETests.cs`. The three KeepSix factory files do not exist at `61117dd1` (1235 insertions at the tip).

## Per test

| Test | Gate | Isolated rerun at the tip | Since `61117dd1` | Cause | Proposed fix |
|---|---|---|---|---|---|
| `KeepSixReminderTests` (11) | Passed, body sum 1m 09s. Slowest case 26.4s | 11/11 passed, class 1m 07s | New | Returns. First cases pay the shared `AntiphonWebAppFactory` schema warm-up. The factory points the runner at a dead URL and refuses port 17204 | None. Do not skip it and do not raise the antiphon watchdog for it |
| `KeepSixSettingsTests` (5) | Passed, body sum 39.8s. First case 38.7s, the other four under 0.4s | 5/5 passed, class 34.6s | New | Same factory warm-up, then the cases return | None |
| `KeepSixShortfallTests` (6) | Passed, body sum 46.4s. First case 38.9s | 6/6 passed, class 33.2s. Exit 0. After the summary, one canceled GET to `http://127.0.0.1:1/capabilities` | New | That GET is the factory's dead runner URL, not a wait | None |
| `RunnerClaudeAdapterEffortPromptTests` (10 expanded) | Passed, body sum 27.9s. Slowest 7.43s (`Disabling_the_probe(true)`). `An_effort_dialog_that_never_clears_fails_within_its_budget` passed in 0.52s | 10/10 passed, class 7.8s | Unchanged | Unit tests on an in-memory screen. Budgets are a few seconds of wall clock. They cannot sit for an hour | None. Do not raise the antiphon watchdog for this class |
| `CodexWindowsLaunchPolicyTests.Quote_expansion_is_counted_not_estimated` | Passed, 17.7ms | 1/1 passed, 424ms | Unchanged | Unit. Hermetic layout, no process. `Measure` is argument length | None. The sizing note's "raise session-runner to 1800000 after these two return" condition is not a hang |
| `CodexWindowsLaunchPolicyTests.The_incident_fixture_is_accepted_on_the_node_path` | Passed, 12.6ms | 1/1 passed, 384ms | Unchanged | Same. The incident fixture is a fixed string, not a live Codex CLI | None |
| `AgentTaskLandDeliveryE2ETests`, 30 passed rows | Passed. Returned rows of this class sum to 57m 43s including the three errors below | Class filter hit the 5:00 wrapper with only the TUnit banner. No pass or fail line. Not a hang finding: `--no-progress` plus serial cases of 40s to 3 min, including fixture startup, does not print inside 5 min | Unchanged | `[NotInParallel("C467LandDelivery")]` (`AgentTaskLandDeliveryE2ETests.cs:12-14`). 24 `[Test]` methods expand to 52 cases. 34 were admitted. The class occupied the whole e2e window (diag `01:03:12Z`–`02:00:56Z`, 57.73 min). The other three e2e UIDs are the opening headed skips (17ms, 0ms, 5ms) | Add an `e2e` watchdog. Do not exclude the class |
| `C488_ReviewToLandReceiptMatrix(idle, Apply)` | Error, 3m 04.938s. Console `failed` | Not reached inside the 5:00 class cap | Unchanged | Returned. `delegate.ps1` land POST 409 `review_evidence_source_not_clean` ("Review evidence does not assert a verified clean source."). Stack: `LandDeliveryFixture.RequestAsync` line 178, `LandReviewedAsync` line 369, test line 316. The other five argument rows were not admitted | Code: the Apply completion form's review evidence is not accepted as clean. Fix the evidence the fixture submits, or the server check if that evidence is clean. Not a skip |
| `C467_V28_LostFlushWakeupRecoversOnIdleCaller` | Error, 46.151s | Not reached inside the 5:00 class cap | Unchanged | Returned. Receipt, remote, and one-prompt asserts passed. Line 154 then failed: `completion-scan-*.observation.json` was absent. `lost-flush` drops only the `completion` wakeup (`LandDeliveryOptions.cs:121`), which skips `flushes.TryEnqueue` after the queue row commits (`AgentTaskLandNotificationService.cs:161-162`). The scan file is written only from `ReachedAsync("completion-scan")` (`LandDeliveryOptions.cs:126-128`), called by `CompletionNoteWorkHostedService.SweepAsync` line 78 and `RecoverMissingAsync` line 121. The receipt arrived by a path that never fired that boundary | Code: fire `completion-scan` on the path that actually delivers this receipt, or stop asserting that file on the `lost-flush` cut. Not a deadlock and not a file wait |
| `C488_ApprovalLostFlushRecovers` | Error, 45.649s. Same assertion, test line 375 | Not reached inside the 5:00 class cap | Unchanged | The method is `await C467_V28_LostFlushWakeupRecoversOnIdleCaller()`. One fix covers both | Same as V28 |
| `C488_ReviewDeliveryCrashMatrix` argument 1 | InProgress, no duration, admitted `02:00:56` | Not reached inside the 5:00 class cap | Unchanged | Next serial row at the kill. Sibling rows of this method returned in 2m 57s to 3m 08s. Not a demonstrated deadlock | Covered by the longer e2e watchdog. No skip |
| 18 land cases never admitted | No UID | Not run | Unchanged | Still queued behind the serial class when the hour ended. At the observed mean of the 33 that returned (105s), about 33 more minutes | Same watchdog. Not an external-CLI exclusion |

`[Category("OptIn")]` does not remove this class from the rc profile. `docs/testing-and-build.md` (the rc-profile paragraph) says only a declared exclusion row with a reason and an owner excludes a case. The rc exclusions (`tests/test-execution-policy.json:40-58`) name `DelegationPipelineE2ETests`, `DelegationSequencingE2ETests`, and `OutputDistillationApplyCanaryTests`. They do not name `AgentTaskLandDeliveryE2ETests`. The class boots its own server, runner, Postgres, and fakegrok. It is not blocked on a missing CLI, Docker daemon, or the production runner. An OptIn or exclusion row would drop the land proof to make the watchdog look green. Do not add one.

## Ranked fixes that can land on the rc line

1. Add `"e2e": 7200000` to `watchdogs` in `tests/test-execution-policy.json` (the object ends at line 749). There is no `e2e` key today, so the suite uses the 60 minute fallback. Thirty-three land cases already consumed 57m 43s of body time, one more had just been admitted, and 18 cases had not started. At 105s each those 19 are about 33 minutes, so this class is about 91 minutes before any other e2e class runs. 120 minutes covers that class and leaves about 30 minutes of tail. The other e2e classes were not reached (108 `[Test]` methods in `tests/Antiphon.E2E`, 24 of them this class), so a later full run can still show that 120 minutes is short. It is the smallest policy number that matches the measured class. Do not raise the 3600000 fallback in `nightly-tests-impl.ps1` instead: that fallback is shared. Quarantine chunks do not make `coverageComplete` while this class is still required.

2. No watchdog change, and no skip, for the KeepSix factory classes, `RunnerClaudeAdapterEffortPromptTests`, or the two Codex methods. They are not why the process lives until the cap. Do not raise `antiphon` off 3600000 for them. Do not raise `session-runner` from 1200000 to 1800000 for the two Codex methods or for `TranscriptIngestionLatencyProbeTests`; those probes returned in at most 30.4s. Each of those suites did have one unrelated UID admitted on the kill timestamp. That is a tail, not these tests, and this parse cannot see tests that had not been admitted yet.

3. A Code task for the three returned land failures, separate from the kill. V28 and `C488_ApprovalLostFlushRecovers` are one missing `completion-scan` boundary on the `lost-flush` cut. `C488_ReviewToLandReceiptMatrix(idle, Apply)` is the 409 `review_evidence_source_not_clean` on form `Apply`. Making those three pass does not get the class inside 60 minutes.

4. Any consumer of these diags, including the sizing note, should use the last state per UID and treat `Duration` as terminal. Sixty-nine of the antiphon rows and 31 of the session-runner rows that a first-`InProgress` read calls silent are `Error` with a duration. The console already has the `failed` line.

## Not done

The first gate (`a1d24b6f`, run `42d2c784`) was not reparsed. Its e2e suite never started (`npm ci` / client bundle). The same first-`InProgress` misread explains that note's antiphon and session-runner "silent" set; this task's evidence is the second gate plus the slotted reruns.

The 5 minute land-class rerun produced no diagnostic file because the wrapper killed the process before TUnit flushed. The gate diag at the same SHA is the class evidence.

No discovery census was written by the killed suites. The 52-case land count is the `[Test]` and `[Arguments]` expansion in `AgentTaskLandDeliveryE2ETests.cs` at this SHA, not a TUnit list-tests dump.
