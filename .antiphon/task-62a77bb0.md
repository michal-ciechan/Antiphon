# Group 29 Debug+Fix: 62a77bb0

Fixed the eight failures reproduced on Linux and a Windows path assertion defect. Final six-class selection: **234 passed, 0 failed, 0 skipped**. All existing assertions remain. Changes are confined to four test files; no product or timeout changes.

## Source and outcomes

Assigned branch: `feat/card-task-62a77bb0`.
Clean baseline: `ec5123c39db2a03e4b51a2abaebc74db17c32d9b`.
Final tested source: `1d05a0ab80b1eb631860f725b546636b5a7beab3`.
Host: assigned Linux runner mirror, Debian 12, .NET runtime 9.0.21, TUnit 1.44.

| Class | Baseline pass/fail | Final pass/fail | Skips |
|---|---:|---:|---:|
| ChannelPromptCorrelationTests | 23/1 | 24/0 | 0 |
| DataRetentionServiceTests | 68/5 | 73/0 | 0 |
| SessionRuntimeActionTests | 28/0 | 28/0 | 0 |
| AgentTaskCheckSweepTests | 35/2 | 37/0 | 0 |
| SettlementSyncRecoveryTests | 27/0 | 29/0 | 0 |
| AgentTaskLandNotificationRecoveryTests | 43/0 | 43/0 | 0 |
| Total | 224/8 | 234/0 | 0 |

The two additional cases are the Windows and Linux pure-string registration-path witnesses. Counts were checked against fresh TRX test definitions and results, not inferred from process exit alone.

The brief's historical native candidate `a97194ee7a4a` reported 32 failures of 1074 in group 29. That historical result remains unchanged. This task selected only the six assigned classes on the newer required baseline; it does not claim native qualification or the other group-29 classes.

## Cluster classification and repairs

1. **Data retention: five stale fixture failures.** `C768Fixture` still created the default adapter without a current generation-bound Claude dialog observation. Switched its factory to `ClaudeDialogFixture.CreateBridgeAsync`. This resolves both `C768_ConfirmedPayloadExpiresAfterReceiptGrace` rows, both `C768_CompleteBatchReceiptPurgesEveryMappedMember` rows, and `C768_LateCompleteReceiptStartsItsOwnRetentionGrace(uncertain)`. All 73 retention cases pass. No evidence supports a retention product defect or changing grace periods.
2. **Check sweep: two stale fixture failures.** The older runner fake supplied no dialog observation, so automated-input admission returned before check cancellation. Both flush cases now attach an observed fake with the persisted generation and the shared composer-screen builder. The occupied-composer case models the stale draft and still asserts cancellation, unchanged attempts, no writes, and the queue event; its empty-composer twin still asserts writes. Adapter writes feed the existing recording runner, preserving the assertions. All 37 cases pass.
3. **Channel correlation: one stale execution assumption.** `C584_AttemptFloorsAndLateConfirm` changed the persisted generation without rebinding its fake and expected transcript ingestion alone to execute durable confirmation work. It now rebinds the observation and drains the session's durable actions before asserting the late-confirm result. All 24 cases pass.
4. **Settlement: Windows test assertion defect.** `C1136_RecreatedRegistrationKeepsHeldDebtAndReschedules` compared Git porcelain's forward-slash path with a filesystem path containing Windows backslashes. Extracted the assertion and added Windows/Linux string witnesses. At `e519501cc025172daa36b3186d4b3c649e959318`, the exact witness method executed two cases: Windows failed at the intended containment assertion, Linux passed. The fix normalizes the expected path to Git separators. Both witnesses and the existing integration test pass in the final 29-case class. The witness starts no native process and acquires no native lock.
5. **Runtime actions / land notification: not reproduced on the required baseline.** All 28 runtime-action and 43 land-notification cases pass in both runs. In particular, `C1259_saturation_keeps_ingestion_live_and_recovers_overflow(production-capacity|poison-page)`, `C1259_shutdown_joins_and_restart_reconciles_submission(after-input)`, and `C467_V14_LandNotesDoNotCountAsReports` pass. No product fix, retry, or deadline increase was made. Saturation's Windows/load failure remains unconfirmed until a native rerun.

The baseline includes post-candidate fixture commits for `ChannelPromptCorrelationTests` (`0ec7a75da`), `SessionRuntimeActionTests` (`5b5fd5651`), and `AgentTaskLandNotificationRecoveryTests` (`bbf763169`), plus the shared factory. This explains source differences from the historical candidate; it does not substitute for native execution.

## Commits, each pushed

- `44878509a9f52d16acea3d9f38431be13f147127`: retention fixture observation.
- `f732d6e0290baedcadf4a9c77eae0d6e975b4183`: check-sweep generation-bound composers.
- `75923cb22b788af1e7617744c89eadfd89165dae`: channel durable confirmation and generation binding.
- `e519501cc025172daa36b3186d4b3c649e959318`: failing Windows path witness, committed before its fix.
- `1d05a0ab80b1eb631860f725b546636b5a7beab3`: Git-separator comparison fix; final tested source.

## Execution and evidence

Every build/test driver ran through `scripts/build-slot.ps1`; all six leases were granted with zero admission wait and maxcpucount 6. No Unit lane or native Windows test was run. No desktop supervisor, native lock, tag, release script, or release activation was touched. No Review was commissioned, per the operator brief.

| Execution | Source | Result | Driver / test duration | Lease held |
|---|---|---|---:|---:|
| Baseline isolated build | ec5123c39 | 0 errors | 177.89s | 179s |
| Baseline six classes | ec5123c39 | 224 pass, 8 fail | 291.471s | 298s |
| Fixture + red-witness build | e519501cc | 0 errors | 124.70s | 126s |
| Exact path witness | e519501cc | 1 pass, 1 expected assertion failure | 1.025s | 5s |
| Final isolated build | 1d05a0ab8 | 0 errors | 112.38s | 113s |
| Final six classes | 1d05a0ab8 | 234 pass, 0 fail | 281.687s | 285s |

Builds had existing compiler/analyzer warnings. Each build used `--property:OutputPath=bin-group29/`. Rebuilds were required by committed source changes before the red witness and final green selection. Builds and six-class runs had 600-second hard bounds; the string-witness run had a 120-second outer bound. These were provisional task caps, with the final six-class cap approximately twice the measured baseline wall time. No test's own timeout was changed.

Evidence root: `/work/worktrees/task-62a77bb0/.antiphon/group29/` (generated payloads remain ignored):

- `base/baseline.trx`, `base.log`: inherited failures at the required base.
- `witness-red/witness-red.trx`, `witness-red.log`: exact Windows assertion failure.
- `final/final.trx`, `final.log`: final per-test identities, outcomes and durations.
- `build.log`, `fixes-build.log`, `final-build.log`: isolated build and lease evidence.
- `timing-base/`, `timing-final/`: per-test timing and available database fixture phases. Uninstrumented external-call phases remain unknown/remainder; no zero-cost inference is made.
- `output-inventory.txt`: all 33 producer-owned alternate-output directories, verified against build logs and deleted after the final run. No `bin-group29` directory remains.

Exact class filter used for both full selections:

```text
/*/Antiphon.Tests.Application/(ChannelPromptCorrelationTests*)|(DataRetentionServiceTests*)|(SessionRuntimeActionTests*)|(AgentTaskCheckSweepTests*)|(SettlementSyncRecoveryTests*)|(AgentTaskLandNotificationRecoveryTests*)/*
```

The driver was `pwsh -NoProfile -File scripts/build-slot.ps1 -Label <run> -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-group29/ -- --treenode-filter '<filter>' --report-trx --report-trx-filename <run>.trx --results-directory <run-directory>`. The exact red method filter was `/*/*/SettlementSyncRecoveryTests/C1136_RegistrationPathUsesGitSeparators`.

Fetched `origin/master` was `2cc2617ead0e0a71543b11f27a9dddea076a7beb`; `git merge-tree --write-tree origin/master HEAD` exited 0 with tree `3d5a805299ef07962b802605beea775bc3b686c1` before this report-only commit. No target merge or rebase was performed. Final report publication and history-policy validation are recorded in the caller-facing completion.

## Native rows still owed

Rerun all six class rows above on the landed/current Windows candidate using its normal isolated build and native qualification ownership, after the existing supervisor permits it. Keep the exact class filters and inspect the TRX counts. Linux does not close native qualification.

Prioritize the three `SessionRuntimeActionTests` rows named above, `C467_V14_LandNotesDoNotCountAsReports` (AgentTaskLandNotificationRecoveryTests), `C1136_RecreatedRegistrationKeepsHeldDebtAndReschedules` (SettlementSyncRecoveryTests), both named check-sweep flush rows, all channel correlation rows, and the five named retention rows. The saturation failures have no verified Windows root cause in this task; if they recur, retain their native timing/phase evidence rather than increasing deadlines.

--- next stage ---
next: land
handoff: Land the pushed test-only fixes under the operator's no-Review instruction; retain the native six-class rerun obligation.
artifact: /work/worktrees/task-62a77bb0/.antiphon/task-62a77bb0.md
