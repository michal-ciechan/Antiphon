# Native E2E land-delivery cases — first half (76755674)

Status: investigation complete, 2026-10-27. All first-half methods executed: **18 methods / 23 cases, 22 passed / 1 failed, zero skipped**. The failed method was repeated once: 1 passed / 1 failed with the identical residue assertion. Total: **25 executions, 23 passed / 2 failed**. The inherited residue expectation mismatch is confirmed; no product or test source was changed. Automatic approval review refused output deletion; 16 owned build directories remain for caller cleanup.

## Provenance

- Assigned branch: `feat/card-task-76755674`; report base `718bd29f4a1504c64c491d607d31403fcb87dae2`.
- Requested RC fetched before execution: `9f00008facfc48e75555c08df59887019b3f8dfe` (`origin/rc/2026-10-27`).
- The assigned branch and RC have diverged (546 / 494 unique commits). Testing uses an exact `git archive` export at `C:\Antiphon\scratch\76755674-rc`; the assigned branch is unchanged except for this report.
- Evidence root: `C:\Antiphon\worktrees\card-task-76755674\.antiphon\native-76755674` (ignored). Build output name: `bin-native-76755674/`.
- Initial preflight at 11:32 UTC: 7,804 MiB free of 32,709 MiB; no `testhost`, `Antiphon.Tests`, or `Antiphon.E2E` process found. Existing desktop processes are untouched.
- All builds and test drivers use `scripts/build-slot.ps1`. The fixture starts isolated random-loopback server/runner and test Postgres; production ports 17202/17204 are not test endpoints.

## Build and compiled selection

- Native Windows SDK `10.0.300`; test runtime reports Windows `10.0.19045`, x64, .NET `9.0.22`, TUnit `1.44.0.0` / Microsoft Testing Platform `2.2.2`.
- Frontend `npm ci --no-audit --no-fund` and `npm run build` passed under one build slot, 234 seconds held / zero admission wait. The archive has no Git metadata, so Vite's version probe printed `not a git repository`; the bundle build still exited zero. The index hash is retained in `client-bundle.json`.
- One native build passed: 39 warnings / zero errors; MSBuild 371.12 seconds, slot held 377 seconds / zero admission wait. Exact output: `native-build.log`. OutputPath `bin-native-76755674/`; explicit `SourceRevisionId=9f00008facfc48e75555c08df59887019b3f8dfe`.
- The E2E, server and runner DLL product versions are all `1.0.0+9f00008facfc48e75555c08df59887019b3f8dfe` (`binary-versions.json`). Their hashes remained unchanged through execution.
- Archive comparison at 11:43:03 UTC checked all 9,088 source files with zero differences (`source-verification-before.json`). Archive SHA-256: `A3CF26B1C6BE5A519474E2927F5ED1ADD0261122AA1D06F87913D5040813D97A`.
- Compiled `--list-tests --treenode-filter '/*/*/AgentTaskLandDeliveryE2ETests/*'` emits all 177 assembly cases on this pinned runner. Reflection of the compiled class supplies the 35 declared method names; joining those to discovery gives 52 class cases. Sorting fully qualified names ordinally selects the first 18 methods, expanding to 23 cases. Full roster, both halves and exact method filters: `compiled-roster.json`; assembly/runner hashes: `binary-hashes.json`.
- Batch 1: first nine methods (11 cases); batch 2: next nine methods (12 cases). Prefixes in the pinned TUnit OR syntax match no additional compiled methods. Fresh TRX exactly matches all 23 expected cases, with no unexpected, missing or skipped cases (`first-half-summary.json`).
- Memory recovered to 9,406 MiB before execution; `preflight-batch-1.json` records the immediate process/memory census. Batch 1 started at approximately 11:46 UTC, leased with zero admission wait.

## Native results

Fresh TRX confirms both intended rosters. The one failed method was repeated once, including both of its arguments; its companion case passed both times. No broader suite was run because this brief assigns this exact compiled first half.

| Invocation | Passed | Failed | Host seconds (TRX) | Outer seconds | Slot wait seconds |
|---|---:|---:|---:|---:|---:|
| batch-1 | 10 | 1 | 874.155 | 879.668 | 0 |
| residue-repeat | 1 | 1 | 120.572 | 126.111 | 0 |
| batch-2 | 12 | 0 | 1101.908 | 1107.258 | 0 |

Per-case wall durations below are the unrounded TRX durations rounded to three decimals for display. These are one-run shared-host observations, not stable p95 estimates. Class prefix for every row: `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.`

| Method / arguments | Result | Seconds |
|---|---|---:|
| C467_V22_AlreadyIdleGetsOutcomeWithoutNewInput | Passed | 111.926 |
| C467_V23_BusyCallerDoesNotBlockAnotherLand | Passed | 103.747 |
| C467_V24_BlockedWriterThenReleaseDeliversBothNotes | Passed | 138.202 |
| C467_V25_HardCrashAfterOutcomeCommitRecoversReceipt | Passed | 86.862 |
| C467_V26_HardCrashAfterQueueInsertReusesRow | Passed | 102.583 |
| C467_V27_LostRequestWakeupRecoversAtBoot | Passed | 86.354 |
| C467_V28_LostFlushWakeupRecoversOnIdleCaller | Passed | 54.205 |
| C467_V29_RealOutcomeProducerMatrix_Batch1(already-present) | Passed | 44.245 |
| C467_V29_RealOutcomeProducerMatrix_Batch1(landed) | Passed | 59.635 |
| C467_V29_RealOutcomeProducerMatrix_Batch2(preoperation-refusal) | Passed | 41.723 |
| C467_V29_RealOutcomeProducerMatrix_Batch2(residue-cleanup) | Failed | 43.584 |
| C467_V29_RealOutcomeProducerMatrix_Batch3(conflict) | Passed | 46.739 |
| C467_V29_RealOutcomeProducerMatrix_Batch3(operation-refusal) | Passed | 73.785 |
| C467_V30_ReceiptSaveFailureNeverRetypes(receipt) | Passed | 121.398 |
| C467_V30_ReceiptSaveFailureNeverRetypes(verdict) | Passed | 81.392 |
| C467_V31_EnqueueFailureRecoversAutomatically | Passed | 79.073 |
| C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(attempt) | Passed | 82.481 |
| C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(busy) | Passed | 90.283 |
| C488_ApprovalBusyCallerDoesNotBlock | Passed | 109.339 |
| C488_ApprovalDeliveryCrashMatrix | Passed | 95.243 |
| C488_ApprovalEnqueueFailureRecovers | Passed | 79.255 |
| C488_ApprovalLostFlushRecovers | Passed | 71.705 |
| C488_ApprovalOutcomeReceiptMatrix(stale-A) | Passed | 170.230 |
| Repeat: C467_V29_RealOutcomeProducerMatrix_Batch2(preoperation-refusal) | Passed | 39.202 |
| Repeat: C467_V29_RealOutcomeProducerMatrix_Batch2(residue-cleanup) | Failed | 80.735 |

## Confirmed: residue fixture selects retained evidence

Both executions fail at `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:211` with the same exact error (`batch-1/failures.log`, `residue-repeat/failures.log`):

```text
ShouldAssertException: terminal.Type
    should be
AgentTaskEventType.LandedWithResidue
    but was
AgentTaskEventType.Landed
```

Mechanism at the pinned RC: `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:329` writes `.antiphon/valuable.txt` containing `owned user residue`. The current retained-content default includes `**/.antiphon/**` (`server/Application/Settings/WorktreeCleanupSettings.cs:26`), and `server/Application/Services/WorktreeIgnoredContentClassifier.cs:55` classifies this path as Evidence. `server/Infrastructure/Git/GuardedWorktreeRemoval.cs:122` retains that evidence before removal. Clean removal sets cleanup Complete (`server/Application/Services/AgentTaskLandingProtocol.cs:459`); Complete with no canonical-advance reason selects event Landed (`server/Application/Services/AgentTaskLandService.cs:886`). The old residue expectation at `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:213` therefore disagrees with the exercised current contract.

`residue-proof.json` binds each failed TRX time interval to its unique fixture directory, original snapshot hash, event, notification, transcript prompt and retained payload. Original snapshots remain under `C:\Antiphon\scratch\76755674-rc\.antiphon\acceptance\card-0467\<root>\delivery-evidence.json`.

| Attempt | Fixture root | Task | Notification | Confirmed UTC / prompt |
|---|---|---|---|---|
| Initial | `604c8ceb27d543e7ad990c5e491479a9` | `00219b58-c6ef-404b-96f8-cc10369ca20e` | `6054d006-726a-47ab-9388-2cdda8c98250` | 11:59:53.679845 / sequence 4 |
| Repeat | `8e96afd54cd4472e886829e7dc311672` | `9405fb10-1185-4f58-8dfc-642cb58b432a` | `6f74ac31-987f-48d9-9ead-ed82b99ffbb6` | 12:02:56.236130 / sequence 4 |

In both snapshots the terminal event is type 21 (Landed), the notification is Confirmed, and the matching stored `UserPrompt` reports `outcome=Landed`, `cleanup=Complete`, and `retained=1 files`. Each retained `valuable.txt` contains 18 bytes with SHA-256 `D3C4C32B9814B7F134582E5F3CB34F5AB69F9B08E99E5B904F8956275229038E`, matching the fixture payload. This is not evidence of file loss, failed native publication, or failed caller delivery. Classification: inherited fixture contract drift; reproduced twice, not a demonstrated flake.

Screenshots are under `<evidence>/screenshots/`: `batch-1-failure-report.png`, `batch-1-failure-report-detail.png`, and the corresponding `residue-repeat-*` pair. They render the original generated TUnit HTML reports and their error details; they are **report screenshots**, not application failure screenshots. This protocol-only test creates no Playwright page. Database/transcript evidence above establishes the product behavior.

The assertion stops execution before the cleanup-retry block at `AgentTaskLandDeliveryE2ETests.cs:224`; that later behavior remains unverified by these failing executions.

## Performance evidence

The two initial invocations took **14m34s** and **18m22s** of test-host time, **32m56s combined**, before the separate 2m01s repeat. The 23 initial case durations sum to 1973.988 seconds: minimum 41.723, median 82.481, empirical p95 138.202, maximum 170.230. This is a mixed-case distribution from one native run, not a repeated-sample p95 for any method and not a basis for changing timeouts.

Every driver acquired a slot immediately; admission wait was zero for frontend build, native build, discovery and all three executions. Batch 2 began after an 8,653 MiB free-memory check. The sibling task `d594e971` had an active, separately owned E2E host at that point; it was not a leftover and was not stopped. These are shared-host measurements on `DESKTOP-KTLKPIF`, Windows 10.0.19045, pinned RC above.

`phase-excerpts.txt` preserves original file:line log records. `phase-records.json` deduplicates by run/phase/operation/duration; `phase-summary.json` has **41 host/fixture samples**, including deliberate restarts, across the 25 executions. These are not 41 independent test cases. Observed timings:

| Phase | Median seconds | Empirical p95 seconds | Maximum seconds |
|---|---:|---:|---:|
| Database create | 0.134 | 0.292 | 0.303 |
| Database migration | 6.852 | 26.089 | 36.102 |
| Fixture setup, including migration | 8.087 | 27.900 | 38.137 |
| Host start | 1.626 | 2.811 | 3.734 |

Nested phase times overlap and must not be added. Separate test-body/teardown timing and comprehensive external-call attribution were not extracted; those components remain unknown/remainder. Original TRX/HTML reports retain captured traces. No timeouts, assertions, fixture setup or process limits were changed.

Performance follow-up for the caller: the combined selection exceeds 30 minutes, so R1 in `docs/test-performance-standards.md:10` requires a Debug task with this exact roster and timing evidence. Both individual batches remain below 30 minutes. This delegate cannot sub-delegate; existing CARD-1514 owns slow-fixture work. The split and these measurements do not establish a fixture optimization or waive that follow-up.

## Reproduction

Run from the exact RC export `C:\Antiphon\scratch\76755674-rc`. The original build command was:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label native-76755674-build -- dotnet build tests/Antiphon.E2E --property:OutputPath=bin-native-76755674/ --property:SourceRevisionId=9f00008facfc48e75555c08df59887019b3f8dfe --nologo
```

The unchanged output remains available because cleanup was refused. Exact failure rerun (choose a fresh results directory):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label native-residue-rerun -- dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-native-76755674/ -- --treenode-filter '/*/*/AgentTaskLandDeliveryE2ETests/C467_V29_RealOutcomeProducerMatrix_Batch2' --report-trx --report-trx-filename results.trx --results-directory <fresh-evidence-directory>
```

Both batch filters and all literal invocation arguments are retained in `compiled-roster.json` and each run's `run.json`. Method membership is the sorted first nine / next nine methods in the table above, ignoring the two repeat rows. The pinned OR form is `/*/*/AgentTaskLandDeliveryE2ETests/(MethodA*)|(MethodB*)`; every selected operand was checked against the fresh TRX. Use the slot wrapper and existing native output; do not execute against the report branch's different runtime history.

## Final integrity, custody and limits

- Final archive comparison at **12:23:41 UTC** checked all **9,088** source files with **zero differences** (`source-verification-after.json`). Only this Markdown report is tracked on the assigned branch.
- Binary hashes are unchanged. All **27** primary/shared-fixture identity records use isolated ports, and all **27** loaded-runner records match the owned compiled runner hash. The build-slot broker is used only for host admission; no E2E fixture used the production server or runner endpoint.
- All foreground runs and screenshot renderers were awaited. The post-run process census found **zero owned processes**. TRX records identify **28** created containers; Docker inventory found **zero remaining** (`runtime-cleanup-receipt.json`). Foreign task processes and containers were left untouched.
- **Output cleanup was rejected before execution by automatic approval review**, with only the reason `blocked by policy`. No deletion occurred. All **16** owned `bin-native-76755674` directories remain below `C:\Antiphon\scratch\76755674-rc`; their exact paths are in `<evidence>/owned-output-inventory.json`, with the refusal in `output-cleanup-receipt.json`. The brief assigns cleanup to the caller/orchestrator; this remains that operational follow-up.
- Original snapshots, input records, server/child logs, TRX, generated HTML reports, report screenshots and hash inventories remain local under the stated evidence/export roots. Generated payloads are not committed.
- The other half's **17 methods / 29 cases** and C508 dispatch-warning cases are outside this task's execution. No claim is made about the review receipt race or the capacity-admission question owned by task `1794d6c7`.
- The residue cleanup-retry block remains unexecuted because the earlier assertion fails. No product delivery failure or environment defect was demonstrated. The fixture mismatch is established from source plus two independent native executions and their stored transcript/database evidence.
- The caller's standing authority covered the execution and evidence collection; no additional approval was requested. No live restart, source fix, deployment, tag or promotion occurred.

## Not done, noted

Fix idea only: align the residue fixture with the protected-content contract (`LandDeliveryFixture.cs:329`, `AgentTaskLandDeliveryE2ETests.cs:213`); no fix designed or implemented.

--- next stage ---
next: plan
handoff: Plan the confirmed residue-fixture contract correction; 18 first-half methods yielded 22/23 passing cases and the same residue failure twice. Preserve the unexecuted cleanup-retry block, commission the R1 performance Debug follow-up, and arrange cleanup of the 16 policy-blocked owned output directories.
artifact: docs/investigations/2026-10-27-native-e2e-land-delivery-first-half.md
