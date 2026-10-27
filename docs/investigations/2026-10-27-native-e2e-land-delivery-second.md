# Native E2E land-delivery cases: second half

Task `d594e971`, native Windows, 2026-10-27. **All 17 assigned methods / 29 cases were executed: 28 passed, 1 failed, 0 skipped. The failed Apply case passed its one diagnostic rerun; that method's raw control also passed. Total: 31 executions, 30 passes / 1 failure.** The initial failure remains recorded. Its immediate startup-timeout mechanism is confirmed; the underlying isolated-runner HTTP 500 is not explained. No product or test source was changed.

## Provenance and isolation

- Tested RC, fetched before export: **`9f00008facfc48e75555c08df59887019b3f8dfe`**. The assigned report branch is **`feat/card-task-d594e971`**, starting at **`718bd29f4a1504c64c491d607d31403fcb87dae2`**. That branch differs from the RC. To preserve the assigned branch without a rebase, reset or merge, execution used an untouched Git archive export of the requested RC at **`C:\Antiphon\scratch\d594e971-rc`**. The report branch's executable source was not the tested subject.
- Generated evidence is retained at **`C:\Antiphon\evidence\d594e971`**. Relative evidence paths below resolve there. The source archive, raw console output, TRX, exact commands, case/phase tables, snapshots, screenshots, process ownership and hashes are retained. `final-binary-provenance.json` records the native E2E/server/runner/FakeGrok DLL hashes and freshly built frontend index.
- `preflight.json` records **8,413 MiB free** and **zero test hosts** at **11:34:22 UTC**. Runtime: Windows **10.0.19045 x64**, .NET **9.0.22**, TUnit **1.44.0**, Microsoft.Testing.Platform **2.2.2**. Host was the shared desktop; other workloads were not fully quantified. No memory-pressure cause is asserted for the startup failure.
- The frontend was installed and rebuilt from this export before tests (`npm ci --no-audit --no-fund`, `npm run build`). One native build used **`bin-native-d594e971/`** and passed with **39 warnings / 0 errors**. Every build, discovery and execution driver took `scripts/build-slot.ps1`; no slot bypass or slot timeout occurred.
- Tests own random loopback servers, random isolated runners and disposable Postgres containers (`tests/Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs:51`, `AntiphonAppFixture.cs:162`). The fixture rejects runner port 17204 (`LandDeliveryFixture.cs:89`). `isolation-and-snapshots.json` records observed identities and snapshot hashes; the initial failed caller's runner port was 58461. The only use of the shared runner was its mandated build-slot broker. No live AppHost or desktop runner restart occurred.

## Compiled roster and exact selection

The compiled class contains **35 methods / 52 argument-expanded cases**. Sorting fully qualified method names gives 18 first-half methods and **17 second-half methods / 29 cases**, starting at method 19, `C488_ApprovalPollingCannotConfirm`, and ending at method 35, `C498_FailureOutcomeReachesCaller`. The common namespace/class prefix is `Antiphon.E2E.AgentTaskLandDeliveryE2ETests`.

`roster.json` joins the requested compiled `--list-tests --treenode-filter "/*/*/AgentTaskLandDeliveryE2ETests/*"` output to the compiled class's PE metadata. TUnit's listing returned 177 discovery entries across the assembly despite the filter; it was not treated as scoped execution evidence. Actual method identities came from fresh TRX `TestDefinitions/UnitTest/TestMethod` records. `coverage-proof.json` confirms **zero missing or unexpected methods**.

The first batch contains the first eight second-half methods in the table below; the second contains the remaining nine. `batches.json` and each `run.json` preserve the literal selectors. An initial OR expression with bare method operands selected zero tests (exit 8, `batch-1/results.trx`); it contributes no coverage. The pinned runner requires a trailing wildcard on each OR operand (`docs/testing-and-build.md:1113`). Corrected selectors use `/*/*/AgentTaskLandDeliveryE2ETests/(ExactMethodA*)|(ExactMethodB*)...`; the fresh TRX proves these prefixes selected exactly the intended names.

## Run timing and outcomes

| Invocation | Methods / cases | Pass / fail / skip | Native exit | Test-host seconds | Outer wall seconds | Slot wait seconds |
|---|---|---|---:|---:|---:|---:|
| `batch-1-valid` | 8 / 12 | 12 / 0 / 0 | 0 | 2340.818 | 2346.266 | 0 |
| `batch-2` | 9 / 17 | 16 / 1 / 0 | 2 | 2651.242 | 2657.689 | 0 |
| `rerun-review-idle` | 1 / 2 | 2 / 0 / 0 | 0 | 415.927 | 422.194 | 0 |

Frontend admission was immediate, with 220 seconds held / 222.497 seconds outer wall. Native compilation took 359.950 seconds; its lease was held 367 seconds after **123 seconds admission wait**, with 489.733 seconds outer wall. Discovery took 0.519 seconds inside a 7.210-second gated invocation. The diagnostic rerun overlapped part of batch 2; these durations must not be added to estimate elapsed task wall time.

Both ordinary batches exceeded 30 minutes (**39m01s** and **44m11s** host time). A separate **Debug task is required** under `docs/test-performance-standards.md:9`, using this exact selection, SHA and timing evidence. This delegate did not sub-delegate or design a performance repair. The requested 8/9-method packaging itself is therefore a measured over-30-minute selection.

### Per-method initial results

These are sums of constituent TRX elapsed case durations, not separately measured CPU time. `C488_ApprovalReceiptSaveFailureNeverRetypes` runs two internal scenarios within one result (`AgentTaskLandDeliveryE2ETests.cs:508`).
| Method | Initial cases | Pass / fail | Sum of TRX seconds |
|---|---:|---|---:|
| `C488_ApprovalPollingCannotConfirm` | 1 | 1 / 0 | 78.603 |
| `C488_ApprovalQueueInsertCrashReusesRow` | 1 | 1 / 0 | 109.230 |
| `C488_ApprovalReceiptSaveFailureNeverRetypes` | 1 | 1 / 0 | 182.199 |
| `C488_ReviewDeliveryCrashMatrix_Batch1` | 2 | 2 / 0 | 460.588 |
| `C488_ReviewDeliveryCrashMatrix_Batch2` | 2 | 2 / 0 | 409.420 |
| `C488_ReviewDeliveryCrashMatrix_Batch3` | 2 | 2 / 0 | 385.187 |
| `C488_ReviewDeliveryCrashMatrix_Batch4` | 2 | 2 / 0 | 517.283 |
| `C488_ReviewDeliveryCrashMatrix_Batch5` | 1 | 1 / 0 | 197.117 |
| `C488_ReviewEvidenceCrashRecovers` | 1 | 1 / 0 | 191.024 |
| `C488_ReviewToLandReceiptMatrix_Batch1` | 2 | 1 / 1 | 381.923 |
| `C488_ReviewToLandReceiptMatrix_Batch2` | 2 | 2 / 0 | 417.749 |
| `C488_ReviewToLandReceiptMatrix_Batch3` | 2 | 2 / 0 | 389.751 |
| `C494_ReviewReceiptRejectsFalseEvidence_Batch1` | 2 | 2 / 0 | 297.380 |
| `C494_ReviewReceiptRejectsFalseEvidence_Batch2` | 2 | 2 / 0 | 296.515 |
| `C494_ReviewReceiptRejectsFalseEvidence_Batch3` | 2 | 2 / 0 | 295.054 |
| `C494_ReviewReceiptRejectsFalseEvidence_Batch4` | 2 | 2 / 0 | 296.874 |
| `C498_FailureOutcomeReachesCaller` | 2 | 2 / 0 | 83.149 |

### Per-case elapsed time, including the diagnostic rerun

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
| batch-2 | `C488_ReviewEvidenceCrashRecovers` | Passed | 191.024 |
| batch-2 | `C488_ReviewToLandReceiptMatrix_Batch1(idle, Apply)` | Failed | 139.877 |
| rerun-review-idle | `C488_ReviewToLandReceiptMatrix_Batch1(idle, Apply)` | Passed | 189.083 |
| batch-2 | `C488_ReviewToLandReceiptMatrix_Batch1(idle, raw)` | Passed | 242.047 |
| rerun-review-idle | `C488_ReviewToLandReceiptMatrix_Batch1(idle, raw)` | Passed | 225.906 |
| batch-2 | `C488_ReviewToLandReceiptMatrix_Batch2(busy, raw)` | Passed | 220.172 |
| batch-2 | `C488_ReviewToLandReceiptMatrix_Batch2(idle, spill)` | Passed | 197.577 |
| batch-2 | `C488_ReviewToLandReceiptMatrix_Batch3(busy, Apply)` | Passed | 189.204 |
| batch-2 | `C488_ReviewToLandReceiptMatrix_Batch3(busy, spill)` | Passed | 200.547 |
| batch-2 | `C494_ReviewReceiptRejectsFalseEvidence_Batch1(QueuedUserPrompt)` | Passed | 148.882 |
| batch-2 | `C494_ReviewReceiptRejectsFalseEvidence_Batch1(Sent-only)` | Passed | 148.497 |
| batch-2 | `C494_ReviewReceiptRejectsFalseEvidence_Batch2(wrong-id)` | Passed | 147.692 |
| batch-2 | `C494_ReviewReceiptRejectsFalseEvidence_Batch2(wrong-session)` | Passed | 148.823 |
| batch-2 | `C494_ReviewReceiptRejectsFalseEvidence_Batch3(partial-middle)` | Passed | 147.098 |
| batch-2 | `C494_ReviewReceiptRejectsFalseEvidence_Batch3(wrong-sha)` | Passed | 147.956 |
| batch-2 | `C494_ReviewReceiptRejectsFalseEvidence_Batch4(old-sequence)` | Passed | 148.007 |
| batch-2 | `C494_ReviewReceiptRejectsFalseEvidence_Batch4(old-time)` | Passed | 148.867 |
| batch-2 | `C498_FailureOutcomeReachesCaller(False)` | Passed | 39.665 |
| batch-2 | `C498_FailureOutcomeReachesCaller(True)` | Passed | 43.484 |

## Failure investigation: intermittent caller startup

`C488_ReviewToLandReceiptMatrix_Batch1(idle, Apply)` failed on its initial attempt with `TimeoutException: native caller ready`. The stack names `tests/Shared/ProgressAwareWait.cs:36`, `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:117`, and `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:329`. The exact error and stack remain in `batch-2/results.trx` and `failure-initial/assertion.txt`.

The immediate mechanism is reconstructed from the original server log:

- At **12:30:58.971 UTC**, `POST http://127.0.0.1:58461/sessions` returned HTTP 500 after 630.318 ms (`failure-initial/server.log:291`). This is the fixture's isolated runner.
- At **12:30:59.050**, session **02644e9c-3770-4f1e-827b-a571566dbe34** failed to launch (`:297`), with `HttpRequestException: Session runner returned 500: Internal Server Error` (`:298`). The asynchronous launch queue recorded the same failure (`:307`).
- The fixture polls for `InteractiveLaunchCompletedAt` and caller transcript/idle evidence, with a 120-second deadline; it does not terminate that wait on a failed launch (`LandDeliveryFixture.cs:117`). The HTTP client rejects the failed runner response (`server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:220`), while the completed-launch timestamp is saved only at the end of successful startup (`server/Application/Services/AgentSessionService.cs:695`). This explains the later generic readiness timeout.
- Fixture root **85f6a1e19b044bdeb4504079b978969e** retained a 99-byte `delivery-evidence.json` with empty request, operation, event, notification, intent, queue and caller-prompt arrays. The main Land task is seeded only after the readiness wait (`LandDeliveryFixture.cs:126`). The case did not reach its Review-to-Land assertions. No database session-state row from the failed fixture was retained, so none is inferred from that empty snapshot.

**Repeat:** the same compiled method was invoked once in a fresh native process, selecting both its raw control and Apply argument. Both passed: raw **225.906 s**, Apply **189.083 s**. No rebuild, source edit, timeout change or retry policy was introduced. The diagnostic driver overlapped the independent second batch from **12:45:25.665 to 12:52:27.859 UTC**, each under its own granted host lease and with fixture-owned random ports/databases. This demonstrates an intermittent startup failure; it does not establish the cause of the original HTTP 500.

The successful Apply rerun used fixture root **c7d6c7e9a1b3485d9b2af461c73f61fa**, caller **fd8e5884-073d-4ffc-ba0c-aa8e03be7f12**, runner **65191**, server **65272**, and the same server MVID **f6190d9e-7889-4744-bf84-8c1867dcadfe**. Its outcome notification **75a28b4b-be0e-43c4-b9a3-0cfe09676a09** was confirmed at **12:52:15.058270 UTC** by caller prompt sequence **7**, row **ce8438bd-0fda-4235-a7e0-030d62c26e61**. `rerun-receipt-proof.json` binds these rows to snapshot SHA-256 **53801595F34AC8DAA019FE4DDADF4488908AE12BD21D4541A8F636ECABB5D2F5** and the TRX result.

**Classification and limits:** the native runner launch failed before the subject delivery behavior. The proximate cause of the test timeout is confirmed; the deeper product-versus-host cause of the runner's 500 is **unconfirmed**. A successful rerun does not prove a repair. The Apply configuration also enables the fixture output distiller (`LandDeliveryOptions.cs:36`, `:50`); this run does not establish any causal link between that setting and the runner error.

There is a confirmed diagnostic-retention limitation: `AntiphonAppFixture.cs:495` stops the runner and `:497` deletes its run directory after a clean census without consulting the test assertion result. The original failed runner directory was already absent when the failure was inspected. The original server log preserves only the generic HTTP error; the runner's underlying exception is unavailable. Subsequent runner logs were copied read-only before teardown, with runner PID/parent-PID provenance under `runner-observations/`. To resolve the remaining cause, capture that underlying runner exception during a fresh occurrence before fixture cleanup.

These protocol tests open no browser. The original startup failure has no screenshot. The diagnostic rerun has real browser captures of its isolated Agents page at `screenshots/rerun-raw-control.png` and `screenshots/rerun-idle-apply.png`, plus HTML, UTC/URL metadata, API observations and owning PID/port records. They document the passing rerun, not a recreated image of the original failure.

## Historical issues and coverage limits

The previously reported `C488_ReviewDeliveryCrashMatrix_Batch5(prompt-before-verdict)` race did not reproduce: the current case passed in **197.117 s**. `prompt-before-verdict-proof.json` binds its barrier to Review task **01abc38c-fca4-41ba-8e25-8cea79b5053c**, queue **5d8d2b23-43b0-4ffa-944b-726e4cc4cac4**, and exactly one stored caller UserPrompt at sequence **4** (row **7119cc3b-6c66-4c46-8829-3f5e0549614e**, **12:18:48.702 UTC**). The original snapshot hash is **237F8F0B8FC48B38806810E4F8D243D21E77CBBD1437AA522BC07D70443ABD5B**. The assertion still requires null confirmation after the queue barrier (`LandDeliveryFixture.Review.cs:269`), while notification confirmation has its own transcript scan (`server/Application/Services/AgentTaskLandNotificationService.cs:300`, `:355`). This pass does not remove the earlier reconstructed race.

C508 belongs to `DispatchBaseWarningDeliveryE2ETests`, outside this compiled class/half. It was not selected or repaired here; task `1794d6c7` retains the separate admission question. First-half coverage belongs to the other lane. This report qualifies only the pinned RC and selected second-half methods, not a newer moving RC or the full E2E project.


## Phase evidence and remaining timing limits

`phase-evidence.json` preserves original log paths, line numbers and unedited LIVE_TIMING messages. `phase-summary.json` deduplicates 57 host/fixture samples, including restarts and the diagnostic rerun; these are not 57 independent test cases. Representative phases:

| Phase | Samples | Median seconds | Empirical p95 seconds | Maximum seconds |
|---|---:|---:|---:|---:|
| Database create | 57 | 0.145 | 0.354 | 0.365 |
| Database migration | 57 | 6.784 | 31.389 | 35.657 |
| Seed | 57 | 0.170 | 1.772 | 1.984 |
| Fixture setup, including migration | 57 | 8.075 | 33.004 | 38.077 |
| Host start | 57 | 1.786 | 3.374 | 3.510 |

These nested phases overlap and must not be summed. The console logs retain Testcontainers creation/readiness timings and individual HTTP spans; protocol waits and Git records remain in each fixture root. A complete joined breakdown of Docker startup, filesystem/CLI work, test body and teardown was not produced: those attribution gaps and remaining elapsed time are **unknown**, not zero. One observation per ordinary case is provisional for cost packing, not a stable per-method p95 or a basis for changing timeouts. No timeout was changed.

## Reproduction and retained artifacts

Build used the following command from the pinned export; native output directories have since been removed, so a future rerun must rebuild before using `--no-build`:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label native-d594e971-build -- dotnet build tests/Antiphon.E2E --property:OutputPath=bin-native-d594e971/ --property:SourceRevisionId=9f00008facfc48e75555c08df59887019b3f8dfe --nologo
```

The affected method's diagnostic invocation was:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label native-d594e971-rerun-review-idle -- dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-native-d594e971/ --property:SourceRevisionId=9f00008facfc48e75555c08df59887019b3f8dfe -- --treenode-filter "/*/*/AgentTaskLandDeliveryE2ETests/C488_ReviewToLandReceiptMatrix_Batch1" --report-trx --report-trx-filename results.trx --results-directory <fresh-results-directory> --output Detailed
```

For either whole batch, replace the selector with the literal `filter` in `C:\Antiphon\evidence\d594e971\batches.json`; `run-native.ps1` there is the retained foreground driver. It records argv, source SHA, timestamps, native exit, fresh TRX and parsed cases. During the diagnostic overlap its coarse directory-difference root inventories can include the other invocation's fixtures; use TRX identities, the screenshot/runner process ownership and `rerun-receipt-proof.json` for that rerun's exact root association. All 31 results are in `all-cases.json`; original TRX and console logs remain unedited.

## Final integrity and cleanup

- At **13:12:04 UTC**, byte-hash comparison against the pinned Git archive verified all **9,088 source files**, with **zero differences** (`source-integrity.json`). No product, test, configuration or screenshot-baseline source changed.
- Every foreground driver and diagnostic process was awaited. Cleanup found **zero running executables under the owned source export**, checked containment and reparse points, and removed all **16** producer-owned `bin-native-d594e971` directories; **zero remained** (`cleanup-receipt.json`, **13:12:06 UTC**). Other worktrees and processes were not removed.
- The logs identify **35** Testcontainers containers created by these invocations. Final Docker inventory found **zero remaining** (`container-cleanup-receipt.json`).
- The source export, rebuilt frontend, original fixture snapshots/logs and external evidence remain for inspection. Binary hashes were recorded before cleanup. `evidence-hashes.json` binds retained artifacts; generated logs, JSON, TRX, PNG and archives are not committed. The only tracked deliverable is this Markdown report.
- The caller's standing authority covered execution and evidence collection; no further approval was needed. No source fix, deployment, tag or promotion was performed.

## Not done, noted

Fix idea only: surface an owned caller's failed launch immediately at `LandDeliveryFixture.cs:117` and retain runner diagnostics through failed-test teardown at `AntiphonAppFixture.cs:497`; no fix was designed or implemented, and the underlying runner 500 still needs evidence.

--- next stage ---
next: investigate
handoff: Investigate the intermittent isolated-runner HTTP 500 behind idle Apply caller startup; its single rerun passed, but original runner diagnostics were deleted. Capture the underlying exception before teardown. Commission performance Debug for the measured 39m01s and 44m11s batches; no source fix is established.
artifact: docs/investigations/2026-10-27-native-e2e-land-delivery-second.md
