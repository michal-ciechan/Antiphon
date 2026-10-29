# rc qualification 28bbb58e — authoritative result inventory

Run `28bbb58edca04b0ebb7afc914b74d386` (task 5906dbf2), candidate `rc-2026-10-27-4563e52529b2`,
SHA `4563e52529b23052856cb0f1e0d01b383cf7590f` (`origin/rc/2026-10-27` at launch), profile `rc`,
trigger `rc`, `-SelectionMode Full`, coverageScope `Full`, native Windows (DESKTOP), `nativeActiveMax` 1.
Started 2026-10-28T10:13:24Z, completed 2026-10-29T09:11:30Z, `durationSeconds` 82686.4 (22 h 58 m).
Verdict: `outcome TESTS`, `succeeded false`, `coverageComplete false`, `testsPassed false`, 52 reasons.

Sources (read-only): `C:\Antiphon\releases\candidates\rc-2026-10-27-4563e52529b2\logs\2026-10-28-1013-28bbb58e\`
(`summary.json`, 387 `suites[]` entries, every `*.trx`, every `*-tests.log`,
`antiphon-all-group-105-execution\log_261028210649163.diag`), the timing root
`...\logs\timing-28bbb58edca04b0ebb7afc914b74d386\`, and `C:\Antiphon\releases\task-5906dbf2\` (WATCH.md,
launch-rc.ps1). Fix mapping is against `C:\src\Antiphon` after `git fetch`, `origin/rc/2026-10-27` =
`91b45006b4d76f144c2651180039aece5a9d7938` (106 commits after the run SHA). Investigated at worktree HEAD
`f8bddb6efda7b0e8f50f3b45744087e52188bc66`.

## 0. Totals derived from the files, not the watcher

- 387 suite entries: 359 `pass`, 28 `FAIL`. Per suite: antiphon 232, e2e 75, pty-host 50, markdown-pdf 11,
  agents-pty 9, session-runner 6, client 1, scripts 1, messaging 1, observability 1.
- TRX totals over every TRX present: **22162 Passed, 36 Failed, 480 NotExecuted**. `client` and `scripts`
  have no TRX by design (`no-trx:client`, `no-trx:scripts`); both are exit 0 / `pass`. markdown-pdf,
  observability, messaging and pty-host have no Failed rows.
- One chunk has no TRX: `antiphon all-group-105` (timed out, killed). Its diag log shows **7 more failed
  tests and 1 hung test** that no TRX and no watcher message ever reported (section 1b).
- So the real failure count is **36 (TRX) + 7 (group 105 diag) = 43 failed tests, plus 1 hung test**.
- **Watcher discrepancy.** The watcher listed antiphon groups 32, 34, 36, 58, 61, 62, 63 and 66. All eight
  are `pass`, exit 0, and their TRX files hold no Failed row in this run. They do not belong in this
  run's inventory; they most likely came from the replaced run 79b742de. The watcher also called e2e
  splits 47 and 55 "C508". They are `C540_*` tests in the same class, and they fail on a different
  assertion.

### The 52 reasons, decoded

| Reason kind | Count | What it is |
|---|---|---|
| `failed-row` | 9 listed (27 suites carry it) | `summary.reasons` holds 52 entries. Per-suite `evidenceReasons` holds the full set: 27 suites have Failed rows (section 1). |
| `process-exit-2` | 9 listed | MTP exit 2 = at least one test failed. It appears together with `failed-row`. |
| `process-exit-8` | 1 | `antiphon all-split-19`. Its 132 tests were all NotExecuted, so the process ran zero tests and MTP returned exit 8 (zero tests ran). See section 2c. |
| `missing-trx` | 1 | `antiphon all-group-105`. The timeout killed the process before the TRX was written. |
| `timeout antiphon` | 1 | the same group 105 (`timedOut true`, runWall 3614.8 s against the 60-minute chunk watchdog). |
| `missing-expanded-row union …` | 1 | 331 UIDs: exactly the whole of group 105 (section 2b). |
| `unexecuted-outcome:…` | 30 | one per suite entry with NotExecuted rows; 30 suites, 480 rows (section 2). |

`summary.reasons` lists only the first appearance of each distinct string, so 9 `failed-row` is not the
number of failing chunks. Use the per-suite counts in section 1.

## 1. Every failing chunk and every failed test

Classification key: **P** product defect, **T** test/fixture defect, **W** Windows environment,
**L** load timeout, **U** unexecuted-by-contract.

Fix status: **RC** means the commit is in `origin/rc/2026-10-27` after the run SHA; rc SHAs are cited.
**BR** means a branch awaits merge; the caller's labels (top-up 33/34, review) are repeated because git
has no top-up marker to verify them. **NONE** means no fix was found.

The ids the caller cited (132f88be, 4bb681bd, …) are **task ids, not commits**: none of the 18 resolves as
a commit object. They resolve as `origin/feat/card-task-<id>` branches. Each branch commit was matched
to rc by exact subject.

### 1a. Chunks with a TRX (36 failed tests)

| # | Chunk | Test | Dur | First error | Cls | Root cause (one sentence) | Fix |
|---|---|---|---|---|---|---|---|
| 1 | antiphon g24 | `TerminalRunnerSeatReleaseTests.Answer_racing_release_preserves_one_owner` | 48.8 s | `TimeoutException: The operation has timed out.` | L | The seat-release wait was a fixed budget that a loaded Windows host overran. | RC: d077c2087, 5e1821b73, 7387b7496, 11496d792, e90bccb42 (seat-release wall cap) |
| 2 | antiphon g70 | `SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow(poison-page)` | 13.4 s | `TimeoutException` | L | The success wait was too short under load. | RC: 070d35b80 (bound waits at 70 s) |
| 3 | antiphon g74 | `AttentionFeedNoiseTests.C1085_Aged_receipt_and_resolved_refusal_emit_no_legacy_row` | 2.5 s | `InvalidOperationException: Sequence contains no elements.` | P | The attention feed threw on a historical land group whose task row is gone. | RC: d4c4d3f75 (witness), 7e731e941 (fix) |
| 4 | antiphon g78 | `AgentReportStoreTests.Ineffective_ignore_attempts_do_not_duplicate_the_exclude_rule(True)` | 4.9 s | `result.Reason should be "not-ignored-or-tracked" but was "io-budget-expired"` | L | Report custody ran on the 2 s hung-IO budget, which load expired. | RC: 366525a09 |
| 5 | antiphon g78 | `AgentReportStoreTests.Exact_content_and_full_ids_own_distinct_files` | 5.6 s | `result.Path should be "...main space é\...md" but was null — io-budget-expired` | L | Same 2 s hung-IO budget. | RC: 366525a09 |
| 6 | antiphon g78 | `CardFileGitFailureAcceptanceTests.Git_failures_are_sanitized_preserve_HEAD_and_retry_after_owned_fault_is_removed(cancel)` | 3.1 s | `File.Exists(...\"hook-ended\") should be False but was True` | T | The failure hook finished before the owned child was killed, so the test raced. | RC: 69576d451 |
| 7 | antiphon g80 | `GrokSignInRuntimeTests.A_sign_in_mode_fakegrok_pool_task_never_types_the_brief` | 2.5 s | `failed.Status should be Failed but was Dispatched` | T | The test read the status once, before the grace sweep had failed the task. | RC: 7de8b8a06, 5e1821b73, 5444e6200, d9ad1acc4 |
| 8 | antiphon g81 | `CardFileIgnoreAcceptanceTests.Failed_ignore_install_preserves_created_project_and_original_ignore_bytes(True)` | 2.9 s | `DbUpdateException` | T | Concurrent cases created projects with the same name (unique-key collision). | RC: 34a7d0b8f |
| 9 | antiphon g87 | `StandingRestartAccountingTests.Failed_outcome_save_recovers_as_unknown_without_fresh_authority` | 0.5 s | `fault.BookkeepingHits should be 2 but was 4` | P | A failed AlwaysOn tick stayed attached, so the next agent repeated the failed bookkeeping save. | RC: 3db6534c0, b7c88b9f2, fffa9e954 |
| 10 | antiphon g96 | `GrokRulesResumeMigrationTests.Standing_start_preserves_native_history_and_requires_explicit_migration(revision, False)` | 0.4 s | `currentReceipt.Generation should not be 267bf234-…` | P | A standing resume did not wait for a new rules generation. | RC: f3aee574a |
| 11 | antiphon g103 | `CheckpointRepeatBuildIsolationTests.repeat_two_then_default_then_three_rebuilds_the_same_project` | 300.0 s | `Test ... timed out after 00:05:00` | L | Repeated real builds exceeded the 5-minute test timeout. | RC: 47104f7ff (one real FilterHost build) |
| 12 | antiphon g113 | `LandingGitTests.C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal` | 32.8 s | `watch.Elapsed should be less than 00:00:15 but was 00:00:30.49` | P | The git budget kill did not end while a grandchild still held the output pipe. | BR: origin/feat/card-task-4bb681bd (tip 2f7dcc048, 6 commits, none in rc; caller: queued top-up 33) |
| 13 | antiphon g113 | `RepositoryChildRecoveryTests.C452_R2_AliasRecoveryUsesCanonicalCommonDirectory` | 1.1 s | `raw should be "C:\Users\...\admin" but was "C:/Users/.../admin"` | T | The test compared git's forward-slash common-dir spelling as text instead of as a canonical path. | RC: 7ba708e15, 2c248bef2 (task abced9f9) |
| 14 | antiphon g114 | `…C1502_CompleteCheckoutStillMarkedIncompleteIsHealed` | 2.7 s | `creation.GitDirectory should be "C:\...\card-task-deadbeef" but was "C:/.../card-task-deadbeef"` | P | Worktree GitDirectory kept git's forward-slash spelling. | RC: fd3434d43, 8004259c7 (task 46665d44) |
| 15 | antiphon g121 | `C1313_SelectorLineEndings` | 4.5 s | `test-test-selection.ps1:340 live selector is not LF: scripts/select-test-rosters.ps1` | W | The qualification clone checked out `*.ps1` as CRLF. | RC: fd60f421b, 4457f7184 (`*.ps1 eol=lf`; task 7f17c4c7) |
| 16 | antiphon g123 | `C1313_BaselineAuthority` | 300.8 s | `ScriptHarness test-change-aware-release.ps1 ... exceeded 00:05:00 execution budget. completion root=True stdout=False stderr=False` (12 PASS lines printed) | T | After the script passed, the harness drain kept waiting on a stdout holder (git auto-maintenance) until the budget ran out. | BR: origin/feat/card-task-31936c77 (tip 3f9a23218, 4 commits, none in rc; caller: top-up 33) |
| 17 | antiphon g123 | `EvidenceDiffGuard…Handles_literal_paths_and_case_variants` | 4.1 s | `JsonSerializer.Deserialize<string>(encoded) should be ".antiphon/a b\t\né.json"` | W | Evidence paths were emitted in the legacy console code page and lost `é`. | RC: a64a8d077, 79ac6df9d (task 3cb45e84) |
| 18 | antiphon g124 | `RunCheckpointRepeatScriptTests.C885_RepeatReceiptAndTimings` | 23.6 s | `valid.ExitCode should be 0 but was 2 — CHECKPOINT SOURCE INVALID reason=source_ineligible` | W | The qualification checkout's ownership markers counted as 2 dirty files, which made the source ineligible. | RC: a977664f4, 29ba78969, d393a0aab, 0ad615335 (task 7360d1bb) |
| 19 | antiphon g124 | `RunCheckpointRepeatScriptTests.C885_RepeatNoBuildAndLeaseCustody` | 27.7 s | `reused.ExitCode ... 2 — CHECKPOINT CP-1 ... dirty=2 source=4563e525…+dirty` | W | Same ownership-marker dirt. | RC: same as #18 |
| 20 | antiphon g126 | `C1008PlatformContractTests.C1050_Windows_remote_outcomes_are_exact` | 0.01 s | `File.Exists(stampPath) should be True but was False — c1050-child-source: outer row build stamp required` | T | The test needs `checkpoint-build-source.json`, which only a checkpoint-tool build writes; the qualification `dotnet build Antiphon.sln` does not (the file is absent from `checkout-5906dbf2\tests\Antiphon.Tests\bin\Debug\` and `net9.0\`). | **NONE.** This is not the dirty-source defect: it also failed in run 8f055f87 (`antiphon-all-group-76.trx`). Neither rc nor any branch changes `C1008PlatformContractTests.cs` after the run SHA. |
| 21 | antiphon g126 | `C1008PlatformContractTests.C1050_Windows_rolling_outcomes_are_exact` | 0.004 s | same | T | Same. | **NONE** |
| 22 | antiphon g126 | `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact` | 2.7 s | `source.DirtyFiles should be 0 but was 2 — c1210-child-source-stamp: clean source` | W | Same ownership-marker dirt as #18. | RC: same as #18. Remaining risk: the next statement binds the same absent build stamp (`CheckpointBuildBinding.Expected(...).Check()`), so C1210 may move on and fail the way #20 does. A rerun decides. It also failed in run 8f055f87. |
| 23 | antiphon g127 | `CardRelationshipScriptTests.Block_fails_loudly_when_relationships_returns_404` | 1.2 s | `error should contain "Relationship route unavailable" but was "␛[31;1mInvoke-Antiphon: ...card.ps1:795..."` | T | The card.ps1 404 message was ANSI-formatted and wrapped by the concise error view at 120 columns. | BR: origin/feat/card-task-8157256f (tip 369ae663f, 5 commits, none in rc; caller: top-up 33) |
| 24 | antiphon g127 | `NightlyDeadOwnerLockTests.C1386_DeadOwnerReclaimed` | 6.2 s | `FAIL ... refusal=The term 'Resolve-QualificationTimingRoot' is not recognized` | P | The nightly lock reclaim path called a timing resolver that was not in scope. | RC: 6d2380656 (task 1f182e26) |
| 25 | antiphon g127 | `NightlyDeadOwnerLockTests.C1386_LiveOwnerRefused` | 4.8 s | same `Resolve-QualificationTimingRoot` | P | Same. | RC: 6d2380656 |
| 26 | antiphon g127 | `NightlyDeadOwnerLockTests.C1386_AmbiguousOwnerRefused` | 4.5 s | same | P | Same. | RC: 6d2380656 |
| 27 | antiphon g128 | `Portable_scripts_lane_witness(replaced)` | 1.2 s | `test-scripts-lane-repairs.ps1:83 The term 'stat' is not recognized` | W | The witness shelled out to POSIX `stat`, which is not on the Windows PATH. | RC: 2dc948d0a, 4fa6e602a (task 6833e444). The wider harness cleanup is BR origin/feat/card-task-3addadc3 (tip eed0a6284; 4 commits not in rc; caller: queued) |
| 28 | antiphon g129 | `TestPhaseSummaryTests.C1514T_PS51AndRerun` | 12.6 s | `text should not contain "??"` | T | A naive text scan matched `??` in the C# embedded in `scripts/lib/test-phase-evidence.ps1` (lines 409 and 476 at the rc tip; still present). That `??` is legal C#, not a PS7 token. | RC: 91b45006b (test now scans PowerShell tokens; task cb858838) |
| 29 | antiphon split-15 | `LandNonCodeSkipTests.C1385_RegistryMatchesNarrowGraph` | 134.9 s | `process.ExitCode 0 but was 1 — evaluation-failed: evaluating Antiphon.sln exceeded 120 s; audit refused (exit 2)` | L | Land-graph evaluation of the solution exceeded its fixed 120 s cap on a loaded host. | BR: origin/feat/card-task-c9fe095a (tip 52237731c; caller: in review) |
| 30 | antiphon split-23 | `RemoteScriptContractTests.C1192_Cleanup_consumer_requires_positive_absence` | 60.1 s | `TimeoutException: the Linux shell harness did not finish in 60s` | L | The Git-bash shell harness reloaded the probe functions at every boundary and overran 60 s. | BR: origin/feat/card-task-c36954f1 (tip fb56d94c4, 3 commits; caller: top-up 33) |
| 31 | session-runner g4 | `GrokRulesHttpAcceptanceTests.Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled` | 1.7 s | `started.StatusCode should be Created but was InternalServerError` (empty body) | P? | The Grok session launch threw an exception that was mapped to an empty 500, so the underlying cause is **unconfirmed**. | BR: origin/feat/card-task-2dec24ba (tip 080c8ded3; reviewed clean; caller: top-up 34). **Diagnostic only**: it returns the exception text and bounds/redacts the 500. It does not show the original cause is gone; the rerun must show 201 or a named exception. |
| 32 | agents-pty g1 | `FakeClaudeContractTests.A_doubled_quote_argument_splits_for_a_native_parser_and_not_for_dotnet` | 0.3 s | `looseRaw should contain "--append-system-prompt␟a "quoted" word" but was "--echo-argsÔÉƒ..."` | W | The fake argv echo was decoded with the legacy console code page, so the U+241F separator became mojibake. | BR: origin/feat/card-task-d52e21f8 (tip 5d36d4f32, 2 commits; caller: top-up 34) |
| 33 | e2e split-45 | `DispatchBaseWarningDeliveryE2ETests.C508_MismatchWarningReachesCaller` | 166.8 s | `TimeoutException: one committed mismatch dispatch intent` | T | The test waited for a mismatch intent that the re-observed cut never writes. | BR: origin/feat/card-task-e150a772 (tip e5f9fef33; caller: in review) |
| 34 | e2e split-46 | `DispatchBaseWarningDeliveryE2ETests.C508_MismatchWarningWaitsForCaller` | 171.3 s | same | T | Same. | BR: e150a772 |
| 35 | e2e split-47 | `DispatchBaseWarningDeliveryE2ETests.C540_ClaimCrashRecoversCollapsedWarnings` | 105.3 s | `AgentTaskEvents.Count(Warning) should be 0 but was 1` | P/T? | After a claim crash, one Warning event remained where the test expects none; this is **not** the C508 stale-intent wait. | **NONE (targeted).** e150a772 edits only the two C508 methods plus the shared `LandDeliveryFixture`/`DispatchWaitDiagnostic`. Whether the fixture change also fixes C540 is unproven. |
| 36 | e2e split-55 | `DispatchBaseWarningDeliveryE2ETests.C540_ProjectionCrashRecoversOriginalPairs` | 88.4 s | same `should be 0 but was 1` | P/T? | Same mechanism, projection-crash variant. | **NONE (targeted)** |

### 1b. antiphon all-group-105: the hang and 7 failures that no TRX or watcher reported

Timing: started 2026-10-28T21:06:48.8Z; the diag stops at 21:14:39; killed at 22:07:03 with runWall
3614.8 s, `trxMissing true`, `why timeout`. The diag (`log_261028210649163.diag`) has the final state of
all 331 UIDs: **258 Passed, 65 Skipped, 7 Error, 1 InProgress**.

| # | Test | Dur | First error | Cls | Root cause | Fix |
|---|---|---|---|---|---|---|
| 37 | `CheckpointExecutorLogTests.concurrent_callbacks_append_each_line_once_without_overlap` (**hung**, InProgress from 21:06:53.76 until the kill) | ≥ 60 min | — | P | `ExecutorLogWriter.Note` blocked on the caller's sink task/context and deadlocked. | RC: b9d121bf4, 1341230f4, b78fd8efd, f1ad47a7c, f07825b12 (task 132f88be) |
| 38 | `CheckpointOrFilterTests.C0931_nobuild_zero_execution_does_not_rebuild` | 5.6 s | `IOException: receipt-read-refused: ...checkpoint-build-source.json: read-stalled: 0 of 826 bytes; no progress within 3s` | L | Checkpoint receipt reads ran on the starved thread pool; the blocked `Note` callers above probably made the starvation worse. | RC (probable): CARD-1601 1846fc31e, 3a8b73f69, cf65d74e9, ce8bd67f5, 4e3ed7bf5, 2c45b3965 ("supervise checkpoint reads independently of the thread pool"), 0f3e892a5 |
| 39 | `CheckpointLaunchCleanupTests.dead_executor_ack_recovers_without_progress` | 10.0 s | `receipt-read-refused: ...manifest.resolved.yaml: read-stalled: 0 of 417 bytes` | L | Same. | RC (probable): CARD-1601 as #38 |
| 40 | `CheckpointLaunchCleanupTests.custody_write_failure_is_not_success` | 9.8 s | `receipt-read-refused: ....checkpoint-runs.jsonl: read-stalled: 0 of 126 bytes` | L | Same. | RC (probable): CARD-1601 |
| 41 | `CheckpointAppTests.create_run_preserves_finished_and_live_sibling_tool_copies` | 16.3 s | `receipt-read-refused: ....checkpoint-runs.jsonl: read-stalled` | L | Same. | RC (probable): CARD-1601 |
| 42 | `CheckpointLaunchCleanupTests.starter_cannot_overwrite_child_progress` | 9.2 s | `IOException: checkpoint test root is sealed or invalid` (`TestRootGuard.RegisterRun`, TestRootGuard.cs:118) | L? | **Unconfirmed.** Probably a knock-on effect of the same starvation, since the shared checkpoint test root was sealed or refused while other tests failed. | Partly RC: 919038dbe (10 s WaitCommand bound). No targeted fix. Rerun decides. |
| 43 | `CheckpointAdmissionContractTests.C1531_receipts_keep_wait_execution_and_counts` | 18.6 s | `TaskCanceledException: A task was canceled.` (CheckpointAdmissionContractTests.cs:114/127) | L? | **Unconfirmed.** A wait was cancelled under the same starvation. | NONE targeted. Rerun decides. |
| 44 | `CheckpointSlotExecutorTests.executor_reachable_broker_grants_build_and_rows` | 3.7 s | `ShouldAssertException: (await CheckpointApp.ExecuteAsync(...` | L? | **Unconfirmed.** | RC (probable): b73045ca9, e598f905a, ecc0fe49f, 8f4e9e630, 2ff3d10db (slot-executor start-time/wall-cap) |

The 65 skipped rows in group 105 are Linux-only skips; see section 2d.

## 2. Unexecuted rows, the missing-expanded-row union, exit 8 and missing-trx

### 2a. Why rows are NotExecuted and why that fails the run

Every NotExecuted row is a **runtime platform skip**: the test throws `SkipTestException`, or calls an
equivalent gate, on Windows. The only exception is one opt-in canary. No row is a test left unrun because
its chunk failed: every chunk with a TRX finished, and the skip reason is printed in each `*-tests.log`.

The gate does not accept a skip. `ConvertTo-NightlyCoverageVerdict`
(`scripts/lib/nightly-coverage.ps1:1031-1041`) normalizes `notexecuted` to NotExecuted (`:529`) and
sets `coverageComplete=false` for any such row. The only admission route is a profile exclusion row
(`tests/test-execution-policy.json` → `profiles.rc.exclusions`, loaded by `Get-NightlyProfileExclusions`,
`scripts/lib/nightly-policy.ps1:400`). An exclusion row needs a `reason` and an `owner`; schema 4 adds an
optional `"platform": "windows"`. Excluded UIDs are filtered out before execution: this run has
antiphon suite `excludedUidCount 99`, and the rc profile has 88 exclusion rows across all suites, only 9 of which carry a `platform`.

**None of the 30 skipping classes below has an rc exclusion row**, and `tests/test-execution-policy.json`
is byte-identical between the run SHA and the rc tip, and also on master. The test-side
`WindowsUnitSkipRoster.cs` inventory (C1210) is not read by the gate.

So this is a **policy/code change, not a rerun**: every future Windows Full run will report the same
`unexecuted-outcome` reasons. The previous Full run 8f055f87 (rc-2026-10-25) had 27 of them.

### 2b. The 30 `unexecuted-outcome` suites (480 TRX rows)

| Suite / chunk | Rows | Class(es) and logged skip reason | Needs |
|---|---|---|---|
| antiphon g3 | 1 | ClaudeDialogWatchdogCanaryTests `C680_LiveNudgeRefusesPermissionAndQuestionDialogs`: "Native watchdog qualification is explicitly opt-in." | Policy row (opt-in, like CARD-0599 rows) |
| antiphon g27 | 1 | LandGraphEvaluationTests `C1483_S1_UnreadableLinkComponentDenies`: "Unix mode 000 has no Windows equivalent" | Policy row `platform: windows` |
| antiphon g80 | 1 | PhoneHomeCompletedCardTransportTests `C1032_TransportQueueRecipientReplay`: "Real phone-home runner join qualified on Linux" | Policy row |
| antiphon g88 | 6 | PhoneHomeMirrorJoinTests `C1032_Join*`: "CP-21 phone-home join requires Linux FIFOs and Unix file modes." | Policy row |
| antiphon g100 | 2 | UnixDelegateLaunchArgvTests: "Unix PTY argv witness" | Policy row |
| antiphon g103 | 2 | CheckpointConfinementLedgerTests (statx), CheckpointReceiptSafetyTests (Linux FIFO) | Policy rows |
| antiphon g104 | 22 | CheckpointBoundedReaderTests 11 (Linux FIFO/process-group); OutputCleanupTests 11 (statx 7, seal statx 2, hanging-git 1, "Windows junction variant is unverified" 1) | Policy rows. The junction row is a Windows obligation the rc description forbids excluding silently; it needs an owner decision. |
| antiphon g106 | 15 | AnchoredPathDeleteWitnessTests 3, CheckpointOwnedOutputTests 1, CheckpointScriptReaderDeadlineTests 2, CheckpointSourceGitBoundsTests 2, CheckpointVerifierBoundsTests 2, PlanCoverageHandleTests 5 (all Linux symlink/hardlink/FIFO/pipe) | Policy rows |
| antiphon g107 | 5 | CheckpointFixtureProcessTests 2, DetachedLauncherTests 1, SafeDeleteProcessBoundaryTests 2 (Linux setsid/FIFO/process-group) | Policy rows |
| antiphon g111 | 2 | CodexRootExecContractTests (CARD-1210, unshare namespaces) | Policy row |
| antiphon g114 | 11 | FileUseCaptureTests `C1539_*` (Linux /proc) | Policy row |
| antiphon g115 | 27 | GrokSha256PinTests 11, JqRunnerImageContractTests 16 (CARD-1210) | Policy rows |
| antiphon g116 | 7 | GithubCredentialHelperTests 6, GitMinimumVersionGuardTests 1 (CARD-1260) | Policy rows |
| antiphon g119 | 6 | RollingVolumeRecycleScriptTests (CARD-1050 C1008 host contracts) | Policy row |
| antiphon g122 | 3 | RetiredTempContainerScriptTests (CARD-1050) | Policy row |
| antiphon g124 | 21 | RetiredTempContainerHostTests (CARD-1050) | Policy row |
| antiphon g125 | 1 | BuildSlotScriptTests `C800_WrapperInterruptKillsChildAndReleasesLease`: "Linux SIGINT behavior only" | Policy row |
| antiphon g126 | 63 | HostJqPrerequisiteScriptTests 39 (CARD-1210), ScriptHarnessLinuxOwnershipTests 21, C1364BoundLandWaitProofTests 3 (CARD-1050) | Policy rows |
| antiphon g127 | 12 | RollingProductionMountTests (CARD-1050) | Policy row |
| antiphon g128 | 7 | RefreshGithubTokenRemoteCommandTests 3 (CARD-1245), NightlyProbeSupervisorTests 1 (CARD-1375), RetiredTempContainerDockerTests 1, RollingVolumeRecycleDockerTests 1, RollingVolumeRecycleCensusChurnTests 1 | Policy rows |
| antiphon split-19 | 132 | RemoteScriptContractTests (CARD-1050), the whole chunk | Policy row (also clears exit 8, section 2c) |
| antiphon split-20 | 56 | RemoteScriptContractTests | Same row |
| antiphon split-21 | 26 | RemoteScriptContractTests | Same row |
| antiphon split-22 | 17 | RemoteScriptContractTests | Same row |
| antiphon split-25 | 3 | RemoteScriptContractTests | Same row |
| session-runner g1 | 4 | CompletedCardWorkspaceRemovalTests (Unix exec bit 2, case-sensitive FS 1, Linux FIFO 1) | Policy rows. The case-sensitivity row needs a decision. |
| session-runner g2 | 1 | RunnerWorkspaceBindingTests `C1032_StoreIdentityRefused` (mode bits) | Policy row |
| session-runner g4 | 1 | HostStatsProbeLiveTests `Linux_probe_reads_live_proc` | Policy row |
| pty-host g1 | 8 | LinuxCgroupCustodyTests ("linux-cgroup-v1 backend executes on Linux only") | Policy row |
| agents-pty g1 | 17 | UnixPtyArgvTests ("requires a Unix PTY and node") | Policy row. The existing session-runner `UnixPtyArgvAdmissionTests` row does not cover this agents-pty class. |

Total: 480 rows (the TRX parse and the log skip-reason parse agree per chunk). RemoteScriptContractTests
alone accounts for 234 of them.

### 2c. `process-exit-8` and `missing-trx`

- **process-exit-8**: `antiphon all-split-19`, exit 8, TRX present, 132 NotExecuted and 0 executed. MTP
  exits 8 when a run executes zero tests. The chunk holds only skipped RemoteScriptContractTests
  methods, so the exit-8 reason is a side effect of section 2b. An exclusion row removes those UIDs
  before planning, which also removes the empty chunk.
- **missing-trx**: `antiphon all-group-105`. The runner timeout killed the process at 3614.8 s (chunk
  watchdog of about 60 min), so MTP never flushed the TRX. The cause is hung test #37. `timeout antiphon`
  is the same event.

### 2d. `missing-expanded-row union …` stated precisely

The native union check (`scripts/lib/nightly-tests-impl.ps1:1386-1398`, `Test-NightlySuiteUidUnion`)
compares the discovered antiphon UIDs, minus profile exclusions, with the terminal rows parsed from the
chunk TRX files. It named **331 UIDs from 30 classes**, and all 331 are the UIDs in group 105's diag:
331/331 match and none come from another chunk.

So exactly **one chunk (antiphon all-group-105) did not produce terminal rows, and the count is 331**.

Classes: CheckpointOrFilterTests 31, CheckpointTempRootSweepTests 28, CheckpointImportTests 26,
CheckpointTimingHarnessTests 21, CheckpointShapeConflictTests 17, CheckpointRecoveryWindowsTests 14,
CheckpointStampSafetyTests 14, CheckpointTrackedBaselineTests 13, CheckpointRunOwnershipTests 12,
PlanCoverageCensusTests 12, CheckpointTempScopeTests 12, CheckpointShadowCustodyTests 12,
CheckpointSlotContractTests 11, CheckpointLaunchCleanupTests 11, CheckpointSlotExecutorTests 10,
PlanCoverageCommandTests 10, CheckpointRecoveryLinuxTests 10, CheckpointTempUsageTests 9,
BaselineComparerTests 8, CheckpointTempLifecycleTests 7, RerunPolicyTests 6,
CheckpointOutputConfinementTests 6, EvidenceFolderTests 6, C1307CliCancellationTests 5,
CheckpointInheritedFileTests 4, CheckpointFixtureRoutingTests 4, CheckpointLargeStampScriptTests 3,
CheckpointAdmissionContractTests 3, CheckpointAppTests 3, CheckpointExecutorLogTests 3.

**Hidden follow-on.** Once group 105 completes, 65 of these rows will turn into NotExecuted Linux-only
skips and a new `unexecuted-outcome` reason. The breakdown: CheckpointShapeConflictTests 17,
CheckpointTrackedBaselineTests 13, CheckpointRecoveryLinuxTests 10, CheckpointStampSafetyTests 10,
CheckpointOutputConfinementTests 5, CheckpointFixtureRoutingTests 4 (CARD-1233),
CheckpointInheritedFileTests 4, CheckpointTempRootSweepTests 1, PlanCoverageCommandTests 1. Two of the
reasons say "Windows is unverified" / "Windows junctions remain an R2 disclosure" (CARD-1199): those are
Windows obligations, not other-platform ones, and they need an owner decision. Expected unexecuted total
without a policy change: **480 + 65 = 545 rows in 31 suites**.

## 3. Fix map summary

| Status | Items |
|---|---|
| In rc (91b45006b) | #1-11, #13-15, #17-19, #22 (dirt only), #24-28, #37. #38-41 and #44 are probable: the CARD-1601 and slot-executor commits match their symptoms. |
| Branch awaiting merge | #12 (4bb681bd), #16 (31936c77), #23 (8157256f), #27 extra (3addadc3), #29 (c9fe095a, in review), #30 (c36954f1), #31 (2dec24ba, diagnostic only), #32 (d52e21f8), #33-34 (e150a772, in review) |
| No fix | #20-21 (C1050 build stamp), #35-36 (C540 Warning count), #42-43 (group-105 knock-on, unconfirmed), #31's underlying 500 cause, and **all 545 Linux-only/opt-in skip rows (policy)** |

Every RC claim was checked by exact commit subject in `4563e525..origin/rc/2026-10-27` and on master.
Every BR claim was checked by `git log origin/rc/2026-10-27..origin/feat/card-task-<id>`, which shows the
branch's commits with no same-subject commit in rc.

## 4. Rerun plan

### How chunks are chosen today

`launch-rc.ps1` runs `scripts/nightly-run.ps1 -NoSync -Profile rc -Trigger rc -SelectionMode Full -RunId … -Ref rc/2026-10-27`.
`-SelectionMode` accepts only `Full|Shadow|ChangeAware` (`scripts/nightly-run.ps1:91`). ChangeAware
needs a bound Full baseline, and this run recorded `fallbackReasons: baseline-absent`. A `-Suites`
subset cannot be `Full`, because the rc profile's 10 `requiredSuites` must all be present
(`nightly-tests-impl.ps1:778-831`).

The packer orders items by planned seconds, largest first, then by key (`nightly-tests-impl.ps1:220`).
**No option runs previously failed chunks first.** Chunk ids such as `all-group-105` are planner
outputs that change whenever the UID set or cost file changes, so they cannot be pinned for a rerun.

### Recommended sequence ("previously failed batches first")

0. **Before the cut.** Merge the BR items of section 3 that the caller wants in this candidate, and
   land the NONE fixes of section 5. The most important is the policy rows; without them a Full run
   **cannot** reach `coverageComplete true`.
1. **Phase A: non-qualifying failed-first sweep** on the fresh rc clone, through
   `scripts/build-slot.ps1`, one `dotnet run --project tests/<P> -- --treenode-filter "/*/*/<Class>/*"` per
   failing class. Classes:
   - TerminalRunnerSeatReleaseTests, SessionRuntimeActionTests, AttentionFeedNoiseTests,
     AgentReportStoreTests, CardFileGitFailureAcceptanceTests, GrokSignInRuntimeTests,
     CardFileIgnoreAcceptanceTests, StandingRestartAccountingTests, GrokRulesResumeMigrationTests,
     CheckpointRepeatBuildIsolationTests, LandingGitTests, RepositoryChildRecoveryTests.
   - The C1502 worktree class (group 114).
   - The g121, g123 and g129 script classes: the C1313 selector, EvidenceDiffGuard and TestPhaseSummaryTests.
   - RunCheckpointRepeatScriptTests, C1008PlatformContractTests, WindowsUnitPlatformAdmissionTests,
     CardRelationshipScriptTests, NightlyDeadOwnerLockTests, the scripts-lane witness class,
     LandNonCodeSkipTests, RemoteScriptContractTests (C1192 only).
   - **All 30 group-105 classes** from section 2d, with CheckpointExecutorLogTests first.
   - session-runner GrokRulesHttpAcceptanceTests, agents-pty FakeClaudeContractTests, and e2e
     DispatchBaseWarningDeliveryE2ETests.

   This sweep does not count toward qualification. It only gives a quick red/green on every item
   before the full day is spent.
   Estimate: the failing chunks' own run walls sum to 252 min including the 60-min hang, or about
   190 min without it. Class-level filters cut that to roughly **60-90 min**, plus one 7.5-min build
   (`buildSeconds 449.8`).
2. **Phase B: the qualifying Full run**, the same command as `launch-rc.ps1` on the new SHA. Estimate
   from this run's timeline:
   - antiphon 1072.7 min, session-runner 31.5, pty-host 18.8, agents-pty 6.3, messaging 1.3,
     client 6.3, scripts 1.8, e2e 221.0, markdown-pdf 5.6, observability 1.2.
   - The 387 chunk walls sum to 1364 min. Drop the 55 min the group-105 hang wasted and add ~10 min
     for 105 to complete normally.
   - Expect **about 22-23 h**, versus 19.0 h (68220 s) for run 8f055f87.
   - Removing the 545 skipped rows by policy saves almost nothing: their chunks ran in about 121 min,
     and most of that went to other tests.

### What must be true for a Full green

- `summary.json` shows `succeeded true`, `coverageComplete true`, `testsPassed true` and an empty
  `reasons`, which means all of:
  - zero Failed rows;
  - zero `process-exit-*`;
  - no `timeout`/`missing-trx`;
  - no `missing-expanded-row`, so every chunk, group 105's successor included, writes its TRX;
  - **zero NotExecuted/skipped rows**, which needs section 5 item 1;
  - `sha`/`ref`/`policy` matching.
- `complete-green.json` is written under the candidate directory.

## 5. Items with no fix yet: recommended fixer briefs

1. **Linux-only/opt-in skips (545 rows, 31 suites):** add `profiles.rc.exclusions` rows in
   `tests/test-execution-policy.json`, `platform: "windows"` with reason+owner, for every class/method in
   sections 2b and 2d (e.g. RemoteScriptContractTests, HostJqPrerequisiteScriptTests…); route the "Windows
   is unverified"/junction/case-sensitive rows to an owner decision instead of excluding them silently.
2. **C1050 x2 (g126):** make `C1050_Windows_{remote,rolling}_outcomes_are_exact` work under the
   qualification build. Either write `checkpoint-build-source.json` there or bind the child audit to the
   gate's own build provenance. Check C1210 in the same change.
3. **e2e C540 x2 (splits 47/55):** run `DispatchBaseWarningDeliveryE2ETests.C540_*` on Windows with and
   without e150a772. One Warning event survives the claim and projection crash recovery; decide whether
   that is a product defect or a fixture defect.
4. **session-runner g4 Grok rules 500:** once 2dec24ba is in, rerun
   `GrokRulesHttpAcceptanceTests.Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled`
   and fix the exception the body now names.
5. **group-105 knock-ons (#42 TestRootGuard sealed, #43 C1531 TaskCanceled):** rerun the
   CheckpointLaunchCleanupTests and CheckpointAdmissionContractTests classes on an rc tip that has
   132f88be and CARD-1601. If either stays red, open a Debug task for it.

## Uncertainties

- The causes of #38-44 are inferred from timing and symptom: all came in the 8 minutes that the
  `ExecutorLogWriter.Note` deadlock overlapped, and they match the CARD-1601 thread-pool starvation
  symptom. They were not reproduced.
- #22 (C1210) may fail next on the same absent build stamp as C1050.
- Top-up numbers (21-34) cannot be checked from git: there are no tags or markers. Branch-to-rc
  membership was checked by commit subject, not by patch-id.
- The watcher's groups 32/34/36/58/61-63/66 are attributed to run 79b742de by inference. This run's files
  show them green.

## Not done, noted

- Fix ideas appear only as the one-line briefs in section 5; nothing was implemented. No run was started,
  and no file under `C:\Antiphon` was modified.
