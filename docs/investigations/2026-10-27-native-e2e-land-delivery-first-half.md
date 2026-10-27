# Native E2E land-delivery cases — first half (76755674)

Status: batch 1 and its failure repeat complete, batch 2 in progress, 2026-10-27. Batch 1: 10 passed / 1 failed; repeat: 1 passed / 1 failed. The residue expectation mismatch is confirmed; no fix is implemented.

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
- Archive comparison at 11:43:03 UTC checked all 9,088 source files with zero differences (`source-verification-before.json`). Archive SHA-256: `A3CF26B1C6BE5A519474E2927F5ED1ADD0261122AA1D06F87913D5040813D97A`.
- Compiled `--list-tests --treenode-filter '/*/*/AgentTaskLandDeliveryE2ETests/*'` emits all 177 assembly cases on this pinned runner. Reflection of the compiled class supplies the 35 declared method names; joining those to discovery gives 52 class cases. Sorting fully qualified names ordinally selects the first 18 methods, expanding to 23 cases. Full roster, both halves and exact method filters: `compiled-roster.json`; assembly/runner hashes: `binary-hashes.json`.
- Batch 1: first nine methods (11 cases); batch 2: next nine methods (12 cases). Prefixes in the pinned TUnit OR syntax match no additional compiled methods. Actual TRX rosters will be checked before coverage is credited.
- Memory recovered to 9,406 MiB before execution; `preflight-batch-1.json` records the immediate process/memory census. Batch 1 started at approximately 11:46 UTC, leased with zero admission wait.

## Results available so far

Fresh TRX confirms exactly the intended batch-1 roster (nine methods / 11 expanded cases). No skips or unexpected methods. The one failed method was repeated once, including both of its arguments; its companion case passed both times.

| Invocation | Passed | Failed | Host seconds (TRX) | Outer seconds | Slot wait seconds |
|---|---:|---:|---:|---:|---:|
| batch-1 | 10 | 1 | 874.155 | 879.668 | 0 |
| residue-repeat | 1 | 1 | 120.572 | pending final inventory | 0 |
| batch-2 | pending | pending | pending | pending | pending |

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

Mechanism at the pinned RC: `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:329` writes `.antiphon/valuable.txt` containing `owned user residue`. The current retained-content default includes `**/.antiphon/**` (`server/Application/Settings/WorktreeCleanupSettings.cs:26`), and `WorktreeIgnoredContentClassifier.cs:55` classifies this path as Evidence. `server/Infrastructure/Git/GuardedWorktreeRemoval.cs:122` retains that evidence before removal. Clean removal sets cleanup Complete (`server/Application/Services/AgentTaskLandingProtocol.cs:459`); Complete with no canonical-advance reason selects event Landed (`server/Application/Services/AgentTaskLandService.cs:886`). The old residue expectation at `AgentTaskLandDeliveryE2ETests.cs:213` therefore disagrees with the exercised current contract.

`residue-proof.json` binds each failed TRX time interval to its unique fixture directory, original snapshot hash, event, notification, transcript prompt and retained payload. Original snapshots remain under `C:\Antiphon\scratch\76755674-rc\.antiphon\acceptance\card-0467\<root>\delivery-evidence.json`.

| Attempt | Fixture root | Task | Notification | Confirmed UTC / prompt |
|---|---|---|---|---|
| Initial | `604c8ceb27d543e7ad990c5e491479a9` | `00219b58-c6ef-404b-96f8-cc10369ca20e` | `6054d006-726a-47ab-9388-2cdda8c98250` | 11:59:53.679845 / sequence 4 |
| Repeat | `8e96afd54cd4472e886829e7dc311672` | `9405fb10-1185-4f58-8dfc-642cb58b432a` | `6f74ac31-987f-48d9-9ead-ed82b99ffbb6` | 12:02:56.236130 / sequence 4 |

In both snapshots the terminal event is type 21 (Landed), the notification is Confirmed, and the matching stored `UserPrompt` reports `outcome=Landed`, `cleanup=Complete`, and `retained=1 files`. Each retained `valuable.txt` contains 18 bytes with SHA-256 `D3C4C32B9814B7F134582E5F3CB34F5AB69F9B08E99E5B904F8956275229038E`, matching the fixture payload. This is not evidence of file loss, failed native publication, or failed caller delivery. Classification: inherited fixture contract drift; reproduced twice, not a demonstrated flake.

Screenshots are under `<evidence>/screenshots/`: `batch-1-failure-report.png`, `batch-1-failure-report-detail.png`, and the corresponding `residue-repeat-*` pair. They render the original generated TUnit HTML reports and their error details; they are **report screenshots**, not application failure screenshots. This protocol-only test creates no Playwright page. Database/transcript evidence above establishes the product behavior.

The assertion stops execution before the cleanup-retry block at `AgentTaskLandDeliveryE2ETests.cs:224`; that later behavior remains unverified by these failing executions.

## Remaining work

Batch 2, its duration rows, final integrity inventory and owned-output cleanup remain pending. Batch 2 started after an 8,653 MiB free-memory check. The sibling task `d594e971` had an active, separately owned E2E host at that point; it was not a leftover and was not stopped. Timings reflect shared-host load. Historical results and the other half's executions are not credited here.

## Not done, noted

Fix idea only: align the residue fixture with the protected-content contract (`LandDeliveryFixture.cs:329`, `AgentTaskLandDeliveryE2ETests.cs:213`); no fix designed or implemented.

--- next stage ---
next: investigate
handoff: Finish the native first-half rerun and classify any failures from fresh TRX and stored fixture evidence.
artifact: docs/investigations/2026-10-27-native-e2e-land-delivery-first-half.md
