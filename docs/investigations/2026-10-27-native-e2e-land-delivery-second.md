# Native E2E land-delivery cases: second half

Task `d594e971`, 2026-10-27. Investigation and timing evidence only; execution in progress.

## Provenance and scope

- Requested RC, fetched and pinned: `9f00008facfc48e75555c08df59887019b3f8dfe`.
- Assigned report branch: `feat/card-task-d594e971`, starting at `718bd29f4a1504c64c491d607d31403fcb87dae2`. This branch differs from the RC. To preserve the assigned branch without rebasing or merging, native runs use a fresh Git archive export at `C:\Antiphon\scratch\d594e971-rc`.
- Generated evidence: `C:\Antiphon\evidence\d594e971`; `preflight.json` records 8,413 MiB available and no test hosts at 11:34:22 UTC. The source archive is retained there.
- Compiled output will be `bin-native-d594e971/`. Each driver takes `scripts/build-slot.ps1`; tests use the isolated fixture server/runner. No live AppHost/runner restart is authorized or performed.
- The compiled class roster will be sorted by fully qualified method name, then split at `ceil(N/2)`. This task owns the remaining methods, including all argument cases for each selected method.

## Current evidence

The fresh frontend build passed (220 seconds leased, zero slot wait). One native build passed with 39 warnings / 0 errors, compiler elapsed 359.95 seconds; lease held 367 seconds after 123 seconds admission wait. TUnit reports Windows 10.0.19045 x64, .NET 9.0.22, TUnit 1.44.0 and Microsoft.Testing.Platform 2.2.2.

Compiled discovery contains 35 class methods / 52 expanded cases. The split is 18 first-half methods and 17 second-half methods (29 cases). `roster.json` combines the requested compiled `--list-tests` output with PE metadata for `Antiphon.E2E.AgentTaskLandDeliveryE2ETests`; listing itself is unscoped on this pinned runner. Method 19 is `C488_ApprovalPollingCannotConfirm`; method 35 is `C498_FailureOutcomeReachesCaller`.

An initial bare-operand OR filter selected zero tests (exit 8, `batch-1/results.trx`). It is not coverage. The owner documentation requires a trailing wildcard on each OR operand (`docs/testing-and-build.md:1113`). Corrected exact-name-prefix operands select only the intended methods; fresh TRX identities validate that claim.

First batch: **12 passed / 0 failed / 0 skipped**, exactly eight intended methods, no unexpected methods. Host duration **2340.818 seconds**; outer wall **2346.266 seconds**, including zero admission wait. This exceeds the 30-minute performance threshold and requires a separately commissioned Debug task; no timeout or assertion was changed. The remaining nine methods / 17 cases are pending.

Per-case times below are TRX elapsed durations, not separately measured test-body CPU time. ApprovalReceiptSaveFailureNeverRetypes runs two internal scenarios in one result (`tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:508`).
| Run | Case | Result | TRX seconds |
|---|---|---|---:|
| batch-1-valid | `C488_ApprovalPollingCannotConfirm` | Passed | 78.603 |
| batch-1-valid | `C488_ApprovalQueueInsertCrashReusesRow` | Passed | 109.230 |
| batch-1-valid | `C488_ApprovalReceiptSaveFailureNeverRetypes` | Passed | 182.199 |
| batch-1-valid | `C488_ReviewDeliveryCrashMatrix_Batch1(settlement-before-commit)` | Passed | 206.220 |
| batch-1-valid | `C488_ReviewDeliveryCrashMatrix_Batch1(settlement-before-save)` | Passed | 254.368 |
| batch-1-valid | `C488_ReviewDeliveryCrashMatrix_Batch2(enqueue-exception)` | Passed | 208.870 |
| batch-1-valid | `C488_ReviewDeliveryCrashMatrix_Batch2(queue-before-commit)` | Passed | 200.549 |
| batch-1-valid | `C488_ReviewDeliveryCrashMatrix_Batch3(queue-committed-before-wakeup)` | Passed | 195.764 |
| batch-1-valid | `C488_ReviewDeliveryCrashMatrix_Batch3(queue-inserted-before-outbox-link)` | Passed | 189.424 |
| batch-1-valid | `C488_ReviewDeliveryCrashMatrix_Batch4(before-typing)` | Passed | 325.022 |
| batch-1-valid | `C488_ReviewDeliveryCrashMatrix_Batch4(idle-lost-wakeup)` | Passed | 192.261 |
| batch-1-valid | `C488_ReviewDeliveryCrashMatrix_Batch5(prompt-before-verdict)` | Passed | 197.117 |


## Not done, noted

No product or test fix designed or implemented; existing C508 admission findings belong to task `1794d6c7`.

--- next stage ---
next: investigate
handoff: Complete the pinned native second-half roster, exact batches, per-case timing and failure classification; no current result is established.
artifact: docs/investigations/2026-10-27-native-e2e-land-delivery-second.md

