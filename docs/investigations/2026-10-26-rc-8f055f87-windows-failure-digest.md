# Windows rc qualification failure digest — run 8f055f87

Capture completed (UTC): `2026-10-26T19:20:57.962684+00:00`. Candidate: `rc/2026-10-25-105792f7d22f`, commit `105792f7d22f297746cdb338976f52e424b76fa7`. Report task: `d5c669aa`; assigned branch: `feat/card-task-d5c669aa`.

**74 failed test results across 17 failing chunk TRX files; 63 completed TRX files inspected, 15,428 result rows.** This is a point-in-time digest of a live run, not its final qualification verdict. No test/build was launched or rerun. No run file, release checkout, process, native lock or original artifact was changed.

## Provenance, scope and reading convention

All raw evidence paths below are relative to `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87`. Only `antiphon-all-group-*.trx` and per-chunk `*-tests.log` were read there. Candidate source cited in the analysis was read from Git objects using `git show 105792f7d22f297746cdb338976f52e424b76fa7:<path>` in this task worktree, without accessing the running release checkout. Source file:line citations and original stack paths refer to that candidate, not to the report branch tip.

Each failed result is retained separately (including all argument variants), with its fully qualified method, TRX display name, execution ID, duration, source TRX result line, exact first 12 decoded XML message lines, and first six stack frames as stored. Fewer than six means the TRX supplies fewer frames. Framework frames and duplicate application frames are preserved; a frame without file:line was emitted that way. Message excerpts are not character-truncated. Additional log evidence is labeled separately. XML parsing normalizes XML line endings; no error text is reworded. SHA-256 values and synthetic test IDs are provenance, not credentials. Credential-shaped values are redacted if matched.

## Coverage and known gaps

| Chunk | Results | Failed |
|---|---:|---:|
| 19 | 232 | 1 |
| 26 | 589 | 1 |
| 29 | 1037 | 1 |
| 32 | 769 | 1 |
| 34 | 615 | 2 |
| 35 | 555 | 8 |
| 36 | 520 | 10 |
| 37 | 441 | 2 |
| 38 | 415 | 1 |
| 42 | 280 | 1 |
| 49 | 154 | 4 |
| 51 | 142 | 1 |
| 54 | 88 | 3 |
| 58 | 30 | 1 |
| 61 | 444 | 12 |
| 62 | 201 | 9 |
| 63 | 64 | 16 |

Completed TRX chunk numbers: 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64.
Per-chunk logs without a TRX at capture: 28. In particular, chunk 28 has only its invocation/platform/diagnostic header, not a result summary; absence of its TRX is not a pass. Later-running chunks without completed TRX are outside this snapshot.

The scan used outcome=Failed across every available chunk, not only the supplied list. No original artifacts were copied into Git.

## Class triage table

Counts are failed result instances, not unique method names. Labels distinguish confirmed failure boundaries from probable ownership; they are not completed root-cause verdicts for unresolved clusters. Application classes use namespace `Antiphon.Tests.Application`; checkpoint classes use `Antiphon.Tests.Checkpoints`.

| Class | Count | One-line cause/signature | Product versus fixture/Windows semantics |
|---|---:|---|---|
| DispatcherSiblingInspectionSchedulingTests | 1 | Outstanding queue remains 1, expected 0 | Product/fixture timing unresolved |
| LandGraphEvaluationTests | 1 | Audit refuses markdown selected by Content glob | Fixture/audit admission mismatch |
| SessionRuntimeActionTests | 1 | Poison-page hosted execution yield times out | Product/fixture timing unresolved |
| TerminalRunnerSeatReleaseTests | 1 | Dispose cannot delete landing.lock: sharing violation | Likely Windows fixture cleanup |
| HerdrAlwaysOnChannelParityTests | 2 | Pending message; Herdr observation-unknown prevents Running | Product/fixture terminal lane unresolved |
| SessionMessageQueuePtyIntegrationTests | 5 | No verified submit, swallowed Enter markers or transcript prompts | Product/fixture Windows terminal lane unresolved |
| RemoteControlRecoveryPtyIntegrationTests | 2 | Missing persisted prompt; unresolved cleanup capture | Mixed delivery/fixture cleanup |
| CardFilePrivacySyncTests | 1 | 23505 duplicate fixed external issue identity before sync | Likely fixture isolation |
| SessionQueueReceiptPlumbingTests | 9 | Queue remains blocked, fault cuts unhit, no payload/receipt | Product/fixture terminal lane unresolved |
| CodexCliObservationGapTests | 1 | Remote Grok launch rejects rules receipt path; six vectors fail | Product/fixture remote lane unresolved |
| ChannelOutboundRecoveryTests | 2 | Barrier read conflicts with another process handle | Likely Windows fixture observation |
| SessionMessageQueueGrokPtyIntegrationTests | 1 | ConflictException: submitting Enter produced no output | Product/fixture terminal lane unresolved |
| AgentTaskStandingAgentDispatchTests | 1 | 23503 failure event references missing AgentTask | Product-path candidate; fixture possible |
| GrokDelegateEndToEndTests | 3 | Delegate remains Dispatched across nudge/interrupt/settlement | Product/fixture Grok lifecycle unresolved |
| PhoneHomeCompletedCardTransportTests | 1 | Live phone-home connection not accepted in time | Product/fixture host setup unresolved |
| AgentTaskPipelineEndpointTests | 1 | Expected advisory stage recommendation absent | Product contract/fixture setting unresolved |
| GitWorkspaceServiceIndexLockTests | 1 | Direct Git status positive control does not change index mtime | Likely fixture/Git Windows semantics |
| DelegationBriefCeilingPtyTests | 2 | Neither pointer nor inline control reaches submission | Product/fixture receiver lane unresolved |
| GrokSignInRuntimeTests | 1 | Launch queue idle wait cancels | Product/fixture sign-in launch unresolved |
| CheckpointConfinementLedgerTests | 3 | Fixture App.csproj already dirty during Prepare | Likely fixture normalization; unconfirmed |
| CheckpointRecoveryWindowsTests | 6 | Five unexpected refusals/exit 2; one staged read stall | Mixed cleanup product/Windows fixture |
| OutputCleanupTests | 3 | Two require /bin/false; one separator-string mismatch | Confirmed portability/assertion failures |
| CheckpointBuildBindingPathTests | 3 | 32-byte ownership receipt read stalls at zero bytes | Product read path; underlying stall unknown |
| CheckpointAppTests | 1 | Run owner receipt read stalls at zero bytes | Product read path; underlying stall unknown |
| CheckpointAdmissionContractTests | 1 | Run owner receipt read stalls at zero bytes | Product read path; underlying stall unknown |
| BaselineComparerTests | 2 | Expected inherited failures classified NEW | Product/fixture census unresolved |
| CheckpointTempUsageTests | 2 | Execution census is short by 24 in two tests | Likely roster expectation drift; unconfirmed |
| C1307CliCancellationTests | 5 | Three owner receipt stalls, two cancellation wait timeouts | Mixed product read path/fixture waits |
| EvidenceFolderTests | 2 | 467-byte failure report content stamp stalls at zero bytes | Product read path; underlying stall unknown |
| CheckpointToolSourceTests | 1 | Run owner receipt read stalls at zero bytes | Product read path; underlying stall unknown |
| PlanCoverageBindingTests | 2 | Historical-plan coverage exits 2, expected 0 | Product/fixture binding unresolved |
| RunStateStoreTests | 1 | 808-byte state content stamp stalls at zero bytes | Product read path; underlying stall unknown |
| RerunPolicyTests | 4 | 32-byte output owner content stamps stall at zero bytes | Product read path; underlying stall unknown |
| CheckpointRepeatBuildIsolationTests | 1 | Build exits 0, output inventory refuses; no tests executed | Product inventory/fixture state unresolved |

## Supplemental log evidence

`antiphon-all-group-26-tests.log:16` (through line 18):

```text
16:   Additional Info:
17:       definition file item selects a markdown or text document the audit does not allow; -Update cannot approve it: tests/Antiphon.Tests/Antiphon.Tests.csproj Content Include=Fixtures\Card0418\** -> tests/Antiphon.Tests/Fixtures/Card0418/current-snapshots.md
18:   audit refused: land graph pre-check refused
```

`antiphon-all-group-35-tests.log:79` (through line 87):

```text
79:   TUnit.Engine.Exceptions.TestFailedException: [Test Failure] AggregateException: test-owned disposal unresolved elapsed=00:00:00.0971358 (cleanup-unresolved elapsed=00:00:00.0915945 (capture session=a2b376e7-545e-4738-8a82-3af873f494b9 manifest=C:\Users\lndco\AppData\Local\Temp\antiphon-c514-pty-a2b376e7545e47388a823af873f494b9\pty-hosts\manifests\a2b376e7545e47388a823af873f494b9.json))
80:     at Antiphon.Tests.TestHelpers.TestOwnedPtyHost.DisposeCoreAsync(Boolean kill, Action supplement, Func`2 stop, Func`1 disposeRuntime) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\TestHelpers\TestOwnedPtyHost.cs:157
81:     at Antiphon.Tests.TestHelpers.RemoteControlPtyLane.DisposeAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\TestHelpers\RemoteControlPtyLane.cs:342
82:     at Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests.C514_Idle_modal_clearance_delivers_three_complete_prompts() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\RemoteControlRecoveryPtyIntegrationTests.cs:117
83:        --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
84:   --->  System.AggregateException: cleanup-unresolved elapsed=00:00:00.0915945 (capture session=a2b376e7-545e-4738-8a82-3af873f494b9 manifest=C:\Users\lndco\AppData\Local\Temp\antiphon-c514-pty-a2b376e7545e47388a823af873f494b9\pty-hosts\manifests\a2b376e7545e47388a823af873f494b9.json)
85:     at Antiphon.Tests.TestHelpers.TestOwnedPtyHost.CleanupAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\TestHelpers\TestOwnedPtyHost.cs:184
86:     at Antiphon.Tests.TestHelpers.TestOwnedPtyHost.DisposeCoreAsync(Boolean kill, Action supplement, Func`2 stop, Func`1 disposeRuntime) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\TestHelpers\TestOwnedPtyHost.cs:142
87:   --->  capture session=a2b376e7-545e-4738-8a82-3af873f494b9 manifest=C:\Users\lndco\AppData\Local\Temp\antiphon-c514-pty-a2b376e7545e47388a823af873f494b9\pty-hosts\manifests\a2b376e7545e47388a823af873f494b9.json
```

`antiphon-all-group-35-tests.log:281` (through line 281):

```text
281:   --->  Npgsql.PostgresException: 23505: duplicate key value violates unique constraint "IX_ExternalIssueRefs_TrackerKind_ExternalId"
```

`antiphon-all-group-36-tests.log:357` (through line 365):

```text
357:     C1029 VECTOR grok/remote=False/busy=False task=f02dcc80-645a-4814-acd4-59684a194ec2 queue=2b5c71e0-4d76-43a4-9fce-ff7a6b1c5731 session=9d946645-b7d4-48a2-971c-d64291b92fa1 receipt=complete submits=1
358:     C1029 DISPATCH task=82efaa2b-1f5c-471b-8062-0b89b3f5841a attempt=1 status=Dispatched session=a240aebe-04ef-419e-963c-a377e56f9ce8
359:     C1029 VECTOR grok/remote=False/busy=True task=82efaa2b-1f5c-471b-8062-0b89b3f5841a queue=f931c9d4-e6a5-4a9d-b9b3-a7e2f8861a14 session=a240aebe-04ef-419e-963c-a377e56f9ce8 receipt=complete submits=1
360:     C1029 DISPATCH task=4c20e9aa-4d78-4c6a-8bdf-aa9986cde3c5 attempt=1 status=Dispatched session=a5a1ca72-9a07-48d3-95fb-872553e0f4fe
361:     C1029 GROK launch status=Failed failure=grok_rules_receipt_invalid: path rules=
362:     C1029 CASE FAILED grok/remote=True/busy=False: TaskCanceledException: A task was canceled.
363:     C1029 DISPATCH task=0a2e32d5-7d20-4e7f-a6fd-162c6fd695c3 attempt=1 status=Dispatched session=0a8957ca-7709-4423-abab-725b0da1c161
364:     C1029 GROK launch status=Failed failure=grok_rules_receipt_invalid: path rules=
365:     C1029 CASE FAILED grok/remote=True/busy=True: TaskCanceledException: A task was canceled.
```

`antiphon-all-group-36-tests.log:397` (through line 404):

```text
397:     C1029 CASE FAILED grok-fault/remote=True/busy=False/after=False: ShouldAssertException: w.Fault.Hits
398:         should be
399:     1
400:         but was
401:     0
402:     
403:     Additional Info:
404:         C1029 Grok queue-save cut follows actual provider readiness and rules ACK
```

`antiphon-all-group-36-tests.log:406` (through line 413):

```text
406:     C1029 CASE FAILED grok-fault/remote=True/busy=False/after=True: ShouldAssertException: w.Fault.Hits
407:         should be
408:     1
409:         but was
410:     0
411:     
412:     Additional Info:
413:         C1029 Grok queue-save cut follows actual provider readiness and rules ACK
```

`antiphon-all-group-36-tests.log:415` (through line 422):

```text
415:     C1029 CASE FAILED grok-fault/remote=True/busy=True/after=False: ShouldAssertException: w.Fault.Hits
416:         should be
417:     1
418:         but was
419:     0
420:     
421:     Additional Info:
422:         C1029 Grok queue-save cut follows actual provider readiness and rules ACK
```

`antiphon-all-group-36-tests.log:424` (through line 431):

```text
424:     C1029 CASE FAILED grok-fault/remote=True/busy=True/after=True: ShouldAssertException: w.Fault.Hits
425:         should be
426:     1
427:         but was
428:     0
429:     
430:     Additional Info:
431:         C1029 Grok queue-save cut follows actual provider readiness and rules ACK
```

`antiphon-all-group-42-tests.log:23` (through line 23):

```text
23:   --->  Npgsql.PostgresException: 23503: insert or update on table "AgentTaskEvents" violates foreign key constraint "FK_AgentTaskEvents_AgentTasks_AgentTaskId"
```

Stored repeat-build outcome/provenance (from this test's TRX message beyond the first 12 lines):

```text
DOTNET build EXIT CODE: 0
refused: output-inventory-failed
CHECKPOINT CP-repeat-2 build=failed project=tests/Antiphon.Checkpoints.FilterHost outputPath=bin-c885-script-c723-01a17998026275038318ceebd208253d-sequence-2/ exit=2 slot=skipped waited=0s
CHECKPOINT CP-repeat-2 commit=105792f7d22f297746cdb338976f52e424b76fa7 build=failed filter=/*/*/FilterHostTests*/* executed=0 passed=0 failed=0 skipped=0 trx=C:\Users\lndco\AppData\Local\Temp\c723-01a17998026275038318ceebd208253d\results-2\CP-repeat-2-20261026-190250-3778\run.trx slot=skipped waited=0s dirty=2 source=105792f7d22f297746cdb338976f52e424b76fa7+dirty:c21323dc003df40740ec666ec6bb69b92d164aa4812095dfa8dd14e17e3a53dc sourceState=dirty buildSource=unknown repeat=2 repetitions=0/2 hostInvocations=1
CHECKPOINT CP-repeat-2 EXIT CODE: 2
```

## Failed results grouped by probable mechanism

### A. Checkpoint receipt/content reads exceed the no-progress deadline (17 results)

The repeated signature is `receipt-read-refused` wrapping `read-stalled: 0 of N bytes; no progress within 3s`. File sizes range from 32-byte ownership stamps to a 68,985,344-byte staged DLL. The stored stacks reconstruct `CheckpointWrite.RecordContent` / `Describe` -> `ReceiptFile.Digest` -> `CheckpointFileReader.Consume`. Candidate source `src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs:71` creates the stall timer before opening and inspecting; `:102` synchronously waits for `ReadAsync`, and `:110`-`:115` maps its cancellation to this error. `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:88`-`:93` performs that digest when recording created content. **Confirmed failure mechanism:** bounded content verification aborts before recording any bytes. **Root not confirmed:** stored evidence cannot separate synchronous read scheduling starvation, a slow file/filter operation, or consumed deadline before reading. These are product-path failures, not proof of defective test expectations.

#### F044 — `Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.detached_staged_copy_survives_starter_exit`

- TRX display name: `detached_staged_copy_survives_starter_exit`
- Chunk 61; duration `00:00:09.1919825`; execution ID `4cefcea0-cf3d-4793-9ded-8a2eb18144f9`.
- Evidence: `antiphon-all-group-61.trx:1267` (UnitTestResult; test ID `bc974a26-0c8b-e114-28cc-e9f0e8ff9620`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a1798bc8ac7120ade26b2524aaf5bc\staged\Antiphon.Tests.dll: read-stalled: 0 of 68985344 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.CopyOverwrite(String source, String destination, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 273
   at Antiphon.Checkpoints.ShadowCopy.CopyFiles(CheckpointCleanupContext context, String source, String destination, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\Execution\ShadowCopy.cs:line 31
   at Antiphon.Checkpoints.ShadowCopy.CopyToolOutput(String toolDirectory, String destination, CheckpointCleanupContext context) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\Execution\ShadowCopy.cs:line 17
```

#### F050 — `Antiphon.Tests.Checkpoints.CheckpointBuildBindingPathTests.Producer_stamp_is_accepted_by_reconstructed_audit_path`

- TRX display name: `Producer_stamp_is_accepted_by_reconstructed_audit_path`
- Chunk 62; duration `00:00:03.1015974`; execution ID `61a59bfe-f1de-40d2-bd1d-de99adc791e6`.
- Evidence: `antiphon-all-group-62.trx:98` (UnitTestResult; test ID `3a56c4c0-b8d9-27f6-70fd-46a6790c0ab4`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c1210-binding-3lsjrofx.h52\tests\bin-c1210-producer\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```

#### F051 — `Antiphon.Tests.Checkpoints.CheckpointBuildBindingPathTests.Equivalent_directory_spellings_preserve_binding`

- TRX display name: `Equivalent_directory_spellings_preserve_binding`
- Chunk 62; duration `00:00:03.6661335`; execution ID `db2fa8d5-216c-40d3-9b33-16a1adb6f7e3`.
- Evidence: `antiphon-all-group-62.trx:123` (UnitTestResult; test ID `6ab78541-e6fa-3e4f-0405-f4755232398b`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c1210-binding-a2xrkjme.znj\tests\bin-c1210-producer\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```

#### F052 — `Antiphon.Tests.Checkpoints.CheckpointAppTests.create_run_uses_the_manifest_results_root`

- TRX display name: `create_run_uses_the_manifest_results_root`
- Chunk 62; duration `00:00:03.6677449`; execution ID `2741a6bc-ccdc-4f7c-840e-f28c9f493407`.
- Evidence: `antiphon-all-group-62.trx:148` (UnitTestResult; test ID `942c6bb8-db0f-029e-742c-b6997c93c326`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17990f5af7fa6a0f67d24bae52ad8\.antiphon\custom-root\20261026-185505-0faf\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.CheckpointApp.CreateRun(CheckpointManifest manifest, RunRequest request, String repo, Runtime runtime) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\CheckpointApp.cs:line 568
```

#### F053 — `Antiphon.Tests.Checkpoints.CheckpointBuildBindingPathTests.Different_directory_or_provenance_is_refused`

- TRX display name: `Different_directory_or_provenance_is_refused`
- Chunk 62; duration `00:00:03.5963944`; execution ID `ee820c75-8346-4cad-af38-45ae14796dfd`.
- Evidence: `antiphon-all-group-62.trx:175` (UnitTestResult; test ID `29ee6426-99b0-d88a-96f0-75800aba1825`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c1210-binding-ed3eerha.mdm\tests\bin-c1210-producer\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```

#### F054 — `Antiphon.Tests.Checkpoints.CheckpointAdmissionContractTests.C1531_receipts_keep_wait_execution_and_counts`

- TRX display name: `C1531_receipts_keep_wait_execution_and_counts`
- Chunk 62; duration `00:00:03.7098879`; execution ID `72a2efca-322e-423e-9316-30b873fcf1e6`.
- Evidence: `antiphon-all-group-62.trx:239` (UnitTestResult; test ID `eca1db75-9840-61a3-aca5-0d436037b9c8`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17990f5967484b6bfe9c14172c69d\.antiphon\checkpoints\20261026-185505-7f08\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.CheckpointApp.CreateRun(CheckpointManifest manifest, RunRequest request, String repo, Runtime runtime) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\CheckpointApp.cs:line 568
```

#### F059 — `Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Cancel_before_request_read_is_not_crash`

- TRX display name: `Cancel_before_request_read_is_not_crash`
- Chunk 63; duration `00:00:04.4720216`; execution ID `72ad71b7-5413-46a1-b68d-c2e932a37721`.
- Evidence: `antiphon-all-group-63.trx:89` (UnitTestResult; test ID `2d9dddc3-a520-8f8e-7da2-9ffc5600a033`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997e73270c3918f020eb706032f\.antiphon\checkpoints\20261026-190240-2482\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.CheckpointApp.CreateRun(CheckpointManifest manifest, RunRequest request, String repo, Runtime runtime) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\CheckpointApp.cs:line 568
```

#### F060 — `Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Executor_cancel_persists_130`

- TRX display name: `Executor_cancel_persists_130`
- Chunk 63; duration `00:00:04.4719908`; execution ID `2b13a7f5-e0ea-4a5f-abac-d49ad0b26a57`.
- Evidence: `antiphon-all-group-63.trx:117` (UnitTestResult; test ID `b973cbcc-95bc-5e9f-6fa9-7da7d72d70fa`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997e733761e989cecf1df8a6b12\.antiphon\checkpoints\20261026-190240-c874\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.CheckpointApp.CreateRun(CheckpointManifest manifest, RunRequest request, String repo, Runtime runtime) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\CheckpointApp.cs:line 568
```

#### F061 — `Antiphon.Tests.Checkpoints.EvidenceFolderTests.rerun_text_is_reset_for_each_row`

- TRX display name: `rerun_text_is_reset_for_each_row`
- Chunk 63; duration `00:00:03.8670928`; execution ID `9da3cb03-11fe-445f-842e-1bc8bcab92c6`.
- Evidence: `antiphon-all-group-63.trx:152` (UnitTestResult; test ID `616b372c-4157-51fd-0858-8c3dfbf55b21`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997e990766f8c59def8fd460e13\rows\CP-2\failures.md: read-stalled: 0 of 467 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.EvidenceFolder.Write(String runDirectory, ReportModel model, Boolean removeToolCopy, String imageDirectory, CheckpointCleanupContext context) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\Evidence\EvidenceFolder.cs:line 47
   at Antiphon.Tests.Checkpoints.EvidenceFolderTests.rerun_text_is_reset_for_each_row() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\EvidenceFolderTests.cs:line 80
```

#### F062 — `Antiphon.Tests.Checkpoints.CheckpointToolSourceTests.tiny_source_is_really_copied_to_the_launched_image`

- TRX display name: `tiny_source_is_really_copied_to_the_launched_image`
- Chunk 63; duration `00:00:04.4727286`; execution ID `05205f89-bd46-46f4-b901-f374555072e9`.
- Evidence: `antiphon-all-group-63.trx:177` (UnitTestResult; test ID `adf5afbe-b582-7c2a-2127-d70fa8f7f549`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997e7317f27985ae74a286d50f8\.antiphon\checkpoints\20261026-190240-6f2e\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.CheckpointApp.CreateRun(CheckpointManifest manifest, RunRequest request, String repo, Runtime runtime) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\CheckpointApp.cs:line 568
```

#### F063 — `Antiphon.Tests.Checkpoints.C1307CliCancellationTests.No_caller_cancel_preserves_existing_results`

- TRX display name: `No_caller_cancel_preserves_existing_results`
- Chunk 63; duration `00:00:04.4720261`; execution ID `27139c88-63ed-4293-a7dc-199bd1db268f`.
- Evidence: `antiphon-all-group-63.trx:216` (UnitTestResult; test ID `380861a3-d3d4-e68b-3f27-0ded83820a7e`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997e7327d0eb5f72641ea0046ec\.antiphon\checkpoints\20261026-190240-c26e\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.CheckpointApp.CreateRun(CheckpointManifest manifest, RunRequest request, String repo, Runtime runtime) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\CheckpointApp.cs:line 568
```

#### F064 — `Antiphon.Tests.Checkpoints.EvidenceFolderTests.red_run_writes_failures_with_message_stack_stdout_and_commands`

- TRX display name: `red_run_writes_failures_with_message_stack_stdout_and_commands`
- Chunk 63; duration `00:00:04.3975427`; execution ID `37673d7d-71f7-49eb-b92e-598e3fdd58f2`.
- Evidence: `antiphon-all-group-63.trx:250` (UnitTestResult; test ID `8a16550e-014f-b30e-ac35-7bc8e38077fe`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997e77a7145b5c483a7e0bb3ab6\rows\CP-2\failures.md: read-stalled: 0 of 467 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.EvidenceFolder.Write(String runDirectory, ReportModel model, Boolean removeToolCopy, String imageDirectory, CheckpointCleanupContext context) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\Evidence\EvidenceFolder.cs:line 47
   at Antiphon.Tests.Checkpoints.EvidenceFolderTests.red_run_writes_failures_with_message_stack_stdout_and_commands() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\EvidenceFolderTests.cs:line 52
```

#### F066 — `Antiphon.Tests.Checkpoints.RunStateStoreTests.readers_never_see_a_partial_file`

- TRX display name: `readers_never_see_a_partial_file`
- Chunk 63; duration `00:00:03.6845339`; execution ID `f4cc53ed-a44d-476f-96ce-31c06ffbd914`.
- Evidence: `antiphon-all-group-63.trx:477` (UnitTestResult; test ID `4b9150fa-4e63-b8c3-cacc-ac08e62bf504`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997f9227ccda6911cfc82033b74\state.json: read-stalled: 0 of 808 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.RunStateStore.Write(String path, RunState state) in C:\Antiphon\releases\checkout-997186e6\tools\Antiphon.Checkpoints\State\RunStateStore.cs:line 35
   at Antiphon.Tests.Checkpoints.RunStateStoreTests.readers_never_see_a_partial_file() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\RunStateStoreTests.cs:line 44
```

#### F067 — `Antiphon.Tests.Checkpoints.RerunPolicyTests.row_green_only_when_rerun_passed_and_nothing_else_failed`

- TRX display name: `row_green_only_when_rerun_passed_and_nothing_else_failed`
- Chunk 63; duration `00:00:03.7537854`; execution ID `964054b1-5d6a-43a9-95af-60dc40457b52`.
- Evidence: `antiphon-all-group-63.trx:493` (UnitTestResult; test ID `0d64ced1-73e1-2d85-ca6d-6f62dd2b741c`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997f8de7bd7ab1bae87fd0bbf1e\tests\Antiphon.Tests\bin-ex\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```

#### F068 — `Antiphon.Tests.Checkpoints.RerunPolicyTests.unlisted_failure_never_reruns`

- TRX display name: `unlisted_failure_never_reruns`
- Chunk 63; duration `00:00:03.7409293`; execution ID `fe2421bd-db21-4d4e-98f8-6db2ae5fc9ec`.
- Evidence: `antiphon-all-group-63.trx:528` (UnitTestResult; test ID `953b7ab0-50e9-67d1-359e-3b1985f3a9e5`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997f8ea7c16a3bb78d9ad05695f\tests\Antiphon.Tests\bin-ex\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```

#### F069 — `Antiphon.Tests.Checkpoints.RerunPolicyTests.rerun_lines_and_reruns_suffix`

- TRX display name: `rerun_lines_and_reruns_suffix`
- Chunk 63; duration `00:00:03.7544439`; execution ID `107cca2a-2772-471a-8621-9172cba9b541`.
- Evidence: `antiphon-all-group-63.trx:563` (UnitTestResult; test ID `725427e1-dfc6-908a-f3ff-bacc22195699`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997f8de7d2092423646bdae9d91\tests\Antiphon.Tests\bin-ex\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```

#### F070 — `Antiphon.Tests.Checkpoints.RerunPolicyTests.reruns_only_known_flaky_names_once`

- TRX display name: `reruns_only_known_flaky_names_once`
- Chunk 63; duration `00:00:03.7204669`; execution ID `6cad0b96-734c-42dc-a266-24e101633595`.
- Evidence: `antiphon-all-group-63.trx:598` (UnitTestResult; test ID `f08af7c5-d35e-5f96-fbfd-844ad9777985`).

Error message (first 12 lines, or all if shorter):

```text
IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17997f8ff7db897661c7db331cf5d\tests\Antiphon.Tests\bin-ex\.checkpoint-output-owner.json: read-stalled: 0 of 32 bytes; no progress within 3s
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Checkpoints.ReceiptFile.Digest(CheckpointCleanupContext context, String path, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\ReceiptFile.cs:line 39
   at Antiphon.Checkpoints.CheckpointWrite.Describe(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 92
   at Antiphon.Checkpoints.CheckpointWrite.RecordContent(String path) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 88
   at Antiphon.Checkpoints.CheckpointWrite.Text(String path, String content, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 191
   at Antiphon.Checkpoints.CheckpointWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 323
   at Antiphon.Checkpoints.SafeWrite.CreateFreshDirectory(String path, WriteOwnership ownership) in C:\Antiphon\releases\checkout-997186e6\src\Antiphon.HostCleanup\Checkpoints\SafeWrite.cs:line 51
```

### B. Checkpoint recovery refuses removal or completion before the expected lifecycle outcome (5 results)

Five recovery tests see `Refused` or wait exit 2 instead of removal/completion. The TRX does not identify the refusal reason; do not equate these with group A just because the same class also contains a read-stall failure. Product cleanup/identity validation versus fixture setup remains unresolved. Required evidence: the specific removal refusal detail and recorded native identity/ownership state from an isolated reproduction.

#### F041 — `Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.locked_tool_delete_is_reported_then_retried`

- TRX display name: `locked_tool_delete_is_reported_then_retried`
- Chunk 61; duration `00:00:00.1520952`; execution ID `21def76f-8121-4b0c-b159-5216ad961ab2`.
- Evidence: `antiphon-all-group-61.trx:1161` (UnitTestResult; test ID `fe9f54ba-7559-8015-b2f7-496a7b30a2e9`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: Remove(run).Outcome
    should be
"Failed"
    but was
"Refused"
    difference
Difference     |  |    |    |    |    |    |    |   
               | \|/  \|/  \|/  \|/  \|/  \|/  \|/  
Index          | 0    1    2    3    4    5    6    
Expected Value | F    a    i    l    e    d         
Actual Value   | R    e    f    u    s    e    d    
Expected Code  | 70   97   105  108  101  100       
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.locked_tool_delete_is_reported_then_retried() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 252
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```

#### F042 — `Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.starter_death_before_launch_reclaims_partial_copy`

- TRX display name: `starter_death_before_launch_reclaims_partial_copy`
- Chunk 61; duration `00:00:00.2392466`; execution ID `8977bef6-6feb-445e-a824-c1be6e3724be`.
- Evidence: `antiphon-all-group-61.trx:1197` (UnitTestResult; test ID `93f2142d-269d-7979-f91d-ea12949eb20b`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: Remove(run).Outcome
    should be
"Removed"
    but was
"Refused"
    difference
Difference     |            |    |    |             
               |           \|/  \|/  \|/            
Index          | 0    1    2    3    4    5    6    
Expected Value | R    e    m    o    v    e    d    
Actual Value   | R    e    f    u    s    e    d    
Expected Code  | 82   101  109  111  118  101  100  
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.starter_death_before_launch_reclaims_partial_copy() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 113
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.starter_death_before_launch_reclaims_partial_copy() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 115
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```

#### F043 — `Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.done_live_executor_retains_image`

- TRX display name: `done_live_executor_retains_image`
- Chunk 61; duration `00:00:00.2765299`; execution ID `eefb7ff8-e3a5-4403-8841-a52de106fc6e`.
- Evidence: `antiphon-all-group-61.trx:1236` (UnitTestResult; test ID `fec2ffd0-36e7-4220-0b23-1adc772c9c19`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: await WaitAsync(run, TimeSpan.FromSeconds(1))
    should be
0
    but was
2
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.done_live_executor_retains_image() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 95
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.done_live_executor_retains_image() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 98
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```

#### F045 — `Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.native_identity_keeps_live_then_removes_dead`

- TRX display name: `native_identity_keeps_live_then_removes_dead`
- Chunk 61; duration `00:00:00.1739246`; execution ID `2a33cb4c-46c1-46b0-953b-be0cc81d2206`.
- Evidence: `antiphon-all-group-61.trx:1325` (UnitTestResult; test ID `93561419-39e1-dca6-a914-2753f66d5228`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: Remove(run, cleanup).Outcome
    should be
"Removed"
    but was
"Refused"
    difference
Difference     |            |    |    |             
               |           \|/  \|/  \|/            
Index          | 0    1    2    3    4    5    6    
Expected Value | R    e    m    o    v    e    d    
Actual Value   | R    e    f    u    s    e    d    
Expected Code  | 82   101  109  111  118  101  100  
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.native_identity_keeps_live_then_removes_dead() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 45
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.native_identity_keeps_live_then_removes_dead() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 47
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```

#### F046 — `Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.launch_unknown_survives_until_late_ack`

- TRX display name: `launch_unknown_survives_until_late_ack`
- Chunk 61; duration `00:00:00.1843888`; execution ID `8aaee1d9-26cd-4299-b4ed-0ae5b34ee77d`.
- Evidence: `antiphon-all-group-61.trx:1364` (UnitTestResult; test ID `129a0972-f5c8-93bd-52cb-2209865490bb`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: Remove(run).Outcome
    should be
"Removed"
    but was
"Refused"
    difference
Difference     |            |    |    |             
               |           \|/  \|/  \|/            
Index          | 0    1    2    3    4    5    6    
Expected Value | R    e    m    o    v    e    d    
Actual Value   | R    e    f    u    s    e    d    
Expected Code  | 82   101  109  111  118  101  100  
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.launch_unknown_survives_until_late_ack() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 237
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.launch_unknown_survives_until_late_ack() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRecoveryWindowsTests.cs:line 239
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```

### C. Unconditional Unix executable assumptions in Windows fixtures (2 results)

Both failures are at `File.Exists("/bin/false").ShouldBeTrue()` before the intended Git-failure behavior is exercised. Candidate `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:1158` and `:1196` require the Unix executable; `:1159` passes it as the Git executable. **Confirmed root for these two failures:** the fixture requires a path absent on this Windows host. This is fixture portability evidence; it does not exercise a product error path.

#### F047 — `Antiphon.Tests.Checkpoints.OutputCleanupTests.preflight_git_failure_exits_2_without_writing`

- TRX display name: `preflight_git_failure_exits_2_without_writing`
- Chunk 61; duration `00:00:00.3082688`; execution ID `e77e0d09-f26b-4482-bbe1-847879c28a4c`.
- Evidence: `antiphon-all-group-61.trx:4188` (UnitTestResult; test ID `a6206b3b-6643-e9a3-99d3-cfc7c7c59055`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: File.Exists("/bin/false")
    should be
True
    but was
False
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.OutputCleanupTests.preflight_git_failure_exits_2_without_writing() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\OutputCleanupTests.cs:line 1196
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```

#### F048 — `Antiphon.Tests.Checkpoints.OutputCleanupTests.safe_write_refuses_tracked_linked_unrecorded_and_git_failure`

- TRX display name: `safe_write_refuses_tracked_linked_unrecorded_and_git_failure`
- Chunk 61; duration `00:00:01.9765477`; execution ID `76715f03-6d7a-411c-83b5-82b256aa025c`.
- Evidence: `antiphon-all-group-61.trx:4238` (UnitTestResult; test ID `1cf2bf0c-21ac-16d2-72b0-32868218ceac`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: File.Exists("/bin/false")
    should be
True
    but was
False
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.OutputCleanupTests.safe_write_refuses_tracked_linked_unrecorded_and_git_failure() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\OutputCleanupTests.cs:line 1158
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```

### D. Path separator spelling differs in a diagnostic assertion (1 results)

The same unknown report is refused and retained, but the actual diagnostic uses `checkpoint-lifecycle-host/TestResults/unknown.txt` while the expected string uses backslashes. Candidate `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:215`-`:227` builds the expected value with `Path.Combine` and compares the whole string. **Confirmed assertion mechanism:** string spelling differs at separators. Fixture expectation versus product diagnostic normalization contract remains an ownership question; the stored failure does not show unsafe deletion.

#### F049 — `Antiphon.Tests.Checkpoints.OutputCleanupTests.unknown_lifecycle_report_after_seal_is_refused_and_kept`

- TRX display name: `unknown_lifecycle_report_after_seal_is_refused_and_kept`
- Chunk 61; duration `00:00:17.8016905`; execution ID `fb1e097b-42d1-4608-a48b-9bac9e38051d`.
- Evidence: `antiphon-all-group-61.trx:4587` (UnitTestResult; test ID `05c6bae7-d0ac-b032-1111-8701867e72a6`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: result.Refusals
    should be
["output-ledger-mismatch; left in place: C:\Users\lndco\AppData\Local\Temp\c723-01a17990462170069dc146b8920594a7\bin-c723a\checkpoint-lifecycle-host\TestResults\unknown.txt"]
    but was (case sensitive comparison)
["output-ledger-mismatch; left in place: C:\Users\lndco\AppData\Local\Temp\c723-01a17990462170069dc146b8920594a7\bin-c723a\checkpoint-lifecycle-host/TestResults/unknown.txt"]
    difference
[*"output-ledger-mismatch; left in place: C:\Users\lndco\AppData\Local\Temp\c723-01a17990462170069dc146b8920594a7\bin-c723a\checkpoint-lifecycle-host/TestResults/unknown.txt"*]
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.OutputCleanupTests.unknown_lifecycle_report_after_seal_is_refused_and_kept() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\OutputCleanupTests.cs:line 227
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```

### E. Fresh checkpoint fixture is already classified dirty (3 results)

All three fail in `Prepare` before their intended confinement scenarios: `dirty-tracked`, `DIRTY App/App.csproj`. Candidate `tests/Antiphon.Tests/Checkpoints/CheckpointConfinementLedgerTests.cs:553` writes a multiline raw project string; `:562`-`:564` adds/commits it without a fixture-local line-ending setting, and `:601` asserts clean preparation. Windows CRLF/Git normalization is a live hypothesis, not a confirmed cause. Required evidence: fixture project/index byte comparison, attributes and effective Git normalization settings at failure.

#### F038 — `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it`

- TRX display name: `ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it`
- Chunk 61; duration `00:00:09.5650190`; execution ID `50eefe42-786a-4572-9b47-51e39b5f9b8f`.
- Evidence: `antiphon-all-group-61.trx:623` (UnitTestResult; test ID `c5e2bf44-a43b-61a1-a09a-7d73174250f7`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: prepared.Refusal
    should be null but was
"dirty-tracked"

Additional Info:
    refused: dirty-tracked
DIRTY App/App.csproj
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.Prepare(String root, Ledger ledger, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointConfinementLedgerTests.cs:line 601
   at Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointConfinementLedgerTests.cs:line 32
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

#### F039 — `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.in_slot_baseline_entry_unreadable_at_settlement_is_refused_unreadable`

- TRX display name: `in_slot_baseline_entry_unreadable_at_settlement_is_refused_unreadable`
- Chunk 61; duration `00:00:06.3129856`; execution ID `5e930e9f-61cb-4be8-8909-a63e6aef0da6`.
- Evidence: `antiphon-all-group-61.trx:709` (UnitTestResult; test ID `ddfbefb3-868b-fb59-d3b2-c073aa18608c`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: prepared.Refusal
    should be null but was
"dirty-tracked"

Additional Info:
    refused: dirty-tracked
DIRTY App/App.csproj
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.Prepare(String root, Ledger ledger, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointConfinementLedgerTests.cs:line 601
   at Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.in_slot_baseline_entry_unreadable_at_settlement_is_refused_unreadable(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointConfinementLedgerTests.cs:line 427
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

#### F040 — `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.changed_file_under_an_output_root_is_still_refused_inside_the_slot`

- TRX display name: `changed_file_under_an_output_root_is_still_refused_inside_the_slot`
- Chunk 61; duration `00:00:06.0743989`; execution ID `041d5c8c-ec0b-4857-8b30-7b2421899988`.
- Evidence: `antiphon-all-group-61.trx:777` (UnitTestResult; test ID `8d031dbc-6461-300e-d331-d3241b303256`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: prepared.Refusal
    should be null but was
"dirty-tracked"

Additional Info:
    refused: dirty-tracked
DIRTY App/App.csproj
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.Prepare(String root, Ledger ledger, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointConfinementLedgerTests.cs:line 601
   at Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.changed_file_under_an_output_root_is_still_refused_inside_the_slot(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointConfinementLedgerTests.cs:line 73
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

### F. Checkpoint census, baseline classification, plan coverage and output inventory diverge (7 results)

Two baseline tests classify `N.Kept.bad` as NEW instead of INHERITED; two census tests report 646 versus 670 and 1646 versus 1670 (both short by 24); two historical plan coverage checks exit 2; repeat-build isolation exits 2 after a successful build with `refused: output-inventory-failed`. These may have separate roots. The equal 24-test deficit is measured correlation, not evidence that all seven failures share one bug. Required evidence: current native execution roster versus fixture roster, baseline census output, coverage refusal output and inventory refusal details. The repeat failure message preserves candidate commit provenance and zero executed tests; it is not a test assertion failure inside FilterHost.

#### F055 — `Antiphon.Tests.Checkpoints.BaselineComparerTests.red_at_base_census_is_inherited_when_the_census_skips_build_output_folders`

- TRX display name: `red_at_base_census_is_inherited_when_the_census_skips_build_output_folders`
- Chunk 62; duration `00:00:04.0385550`; execution ID `11b15db0-d9a7-42ee-a9d7-0ae8e967e174`.
- Evidence: `antiphon-all-group-62.trx:362` (UnitTestResult; test ID `63683ab3-35fc-c4f7-333f-14c2835b3f91`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: kinds.Single().Kind
    should be
"INHERITED"
    but was
"NEW"
    difference
Difference     |  |    |    |    |    |    |    |    |    |   
               | \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  
Index          | 0    1    2    3    4    5    6    7    8    
Expected Value | I    N    H    E    R    I    T    E    D    
Actual Value   | N    E    W                                  
Expected Code  | 73   78   72   69   82   73   84   69   68   
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.BaselineComparerTests.red_at_base_census_is_inherited_when_the_census_skips_build_output_folders() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\BaselineComparerTests.cs:line 88
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```

#### F056 — `Antiphon.Tests.Checkpoints.BaselineComparerTests.classifies_inherited_introduced_new`

- TRX display name: `classifies_inherited_introduced_new`
- Chunk 62; duration `00:00:04.0726505`; execution ID `1c2e59c5-fe7b-4932-9de3-5e90808bbc7d`.
- Evidence: `antiphon-all-group-62.trx:438` (UnitTestResult; test ID `2d87753a-4133-e50d-4434-82e091b55ee8`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: kinds.Single(item => item.Name == "N.Kept.bad").Kind
    should be
"INHERITED"
    but was
"NEW"
    difference
Difference     |  |    |    |    |    |    |    |    |    |   
               | \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  
Index          | 0    1    2    3    4    5    6    7    8    
Expected Value | I    N    H    E    R    I    T    E    D    
Actual Value   | N    E    W                                  
Expected Code  | 73   78   72   69   82   73   84   69   68   
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.BaselineComparerTests.classifies_inherited_introduced_new() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\BaselineComparerTests.cs:line 48
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```

#### F057 — `Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.namespace_census_uses_the_native_execution_roster`

- TRX display name: `namespace_census_uses_the_native_execution_roster`
- Chunk 62; duration `00:00:06.5975701`; execution ID `66ec182d-e9eb-470c-a6e6-48341934a232`.
- Evidence: `antiphon-all-group-62.trx:2734` (UnitTestResult; test ID `ea437a5e-654c-96f3-988a-597d4a010232`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: evidence.GetProperty("executed").GetInt32()
    should be
670
    but was
646
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.namespace_census_uses_the_native_execution_roster() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointTempUsageTests.cs:line 115
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```

#### F058 — `Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.full_suite_checkpoint_floor_uses_the_current_census`

- TRX display name: `full_suite_checkpoint_floor_uses_the_current_census`
- Chunk 62; duration `00:00:06.3043807`; execution ID `0bf6cb88-398c-4d09-8af5-7ebfcb44d62c`.
- Evidence: `antiphon-all-group-62.trx:2779` (UnitTestResult; test ID `b928c723-6da3-9bcf-1099-b6f844b3153e`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: result.RootElement.GetProperty("native").GetProperty("executed").GetInt32()
    should be
1670
    but was
1646
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.full_suite_checkpoint_floor_uses_the_current_census() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointTempUsageTests.cs:line 153
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```

#### F065 — `Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.historical_row_timeout_checklist_binds_the_advisory_method`

- TRX display name: `historical_row_timeout_checklist_binds_the_advisory_method`
- Chunk 63; duration `00:00:03.8723345`; execution ID `f21fef97-b68e-49b2-baaa-9b95e5468d59`.
- Evidence: `antiphon-all-group-63.trx:372` (UnitTestResult; test ID `76b88250-e2a4-7874-6109-1e8a23292808`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: CheckpointNativeDiagnostics.Coverage(exit, root, output)
    should be
0
    but was
2

Additional Info:
    c1055-0891
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.<historical_row_timeout_checklist_binds_the_advisory_method>g__Bind|5_0(String plan, String[] tests, String id, Int32 obligations, String label, <>c__DisplayClass5_0&) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\PlanCoverageBindingTests.cs:line 258
   at Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.historical_row_timeout_checklist_binds_the_advisory_method(CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\PlanCoverageBindingTests.cs:line 235
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

#### F071 — `Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.historical_plans_bind_without_suppression`

- TRX display name: `historical_plans_bind_without_suppression`
- Chunk 63; duration `00:00:06.2180368`; execution ID `d158c4e9-4702-4684-a0ae-3b4754a3dad3`.
- Evidence: `antiphon-all-group-63.trx:633` (UnitTestResult; test ID `17f390bd-7f33-757b-d1ea-a0cfe7029492`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: CheckpointNativeDiagnostics.Coverage(firstExit, CheckpointFixtures.RepoRoot, first)
    should be
0
    but was
2

Additional Info:
    c1046-real-plans
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.PlanCoverageBindingTests.historical_plans_bind_without_suppression() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\PlanCoverageBindingTests.cs:line 177
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```

#### F074 — `Antiphon.Tests.Checkpoints.CheckpointRepeatBuildIsolationTests.repeat_two_then_default_then_three_rebuilds_the_same_project`

- TRX display name: `repeat_two_then_default_then_three_rebuilds_the_same_project`
- Chunk 63; duration `00:03:31.3858939`; execution ID `57570e4a-b0a0-4dda-a62b-4f3ca4460696`.
- Evidence: `antiphon-all-group-63.trx:869` (UnitTestResult; test ID `cd62c2ac-4e2d-b550-2b34-9b4bccdcd221`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: process.ExitCode
    should be
0
    but was
2

Additional Info:
    sequence-repeat-2: BUILD SLOT skipped by -NoSlot

  Determining projects to restore...
  Restored C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Checkpoints.FilterHost\Antiphon.Checkpoints.FilterHost.csproj (in 777 ms).
  Antiphon.Checkpoints.FilterHost -> C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Checkpoints.FilterHost\bin-c885-script-c723-01a17998026275038318ceebd208253d-sequence-2\Antiphon.Checkpoints.FilterHost.dll
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.CheckpointRepeatBuildIsolationTests.AssertRunAsync(String root, Int32 repeat, Int32 expectedExecuted, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRepeatBuildIsolationTests.cs:line 55
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Checkpoints.CheckpointRepeatBuildIsolationTests.AssertRunAsync(String root, Int32 repeat, Int32 expectedExecuted, CancellationToken cancellationToken) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\CheckpointRepeatBuildIsolationTests.cs:line 84
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```

### G. Terminal input, observation and recipient receipt do not complete (24 results)

Fake Claude/Grok/Herdr paths show no verified submission, missing SWALLOWED-ENTER markers, empty forwarded payloads, pending/modal-blocked queue entries, missing recipient prompts, observation-unknown or an explicit no-output Enter rejection. C1029 aggregates six failing remote Grok vectors; its log records local Grok vectors with receipt=complete and submits=1. For the two ordinary remote vectors, the launch status is Failed with `grok_rules_receipt_invalid: path` before the wait cancels; the four remote fault vectors report Fault.Hits=0. Candidate `src/Antiphon.SessionRunner.Contracts/GrokRulesTransport.cs:86` calls ValidateReceiptPath; `:94`-`:110` rejects invalid path grammar or the wrong session-specific suffix. This reconstructs a rules-receipt path refusal before delivery for those two launches; the actual offending receipt path is absent from the stored log, so its precise invalid component is unconfirmed. This narrows that test to its remote lane and startup validation but does not establish the cause of other failures. **Root not confirmed:** product delivery/observation regression, fake receiver/platform behavior and timing remain live. A missing marker can mean delivery never reached the intended fault, not that retry behavior was tested. ConPTY is a Windows-path hypothesis; these TRX files do not prove a ConPTY API error. Required evidence: transcript UserPrompt records plus runner delivery/observation receipts for the same failed attempt; screen output alone is insufficient.

#### F005 — `Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies`

- TRX display name: `AlwaysOn_channel_bound_survives_child_death_and_replies(PtyHost)`
- Chunk 34; duration `00:00:00.9490657`; execution ID `dc910347-49ab-4ece-8cb7-7c582c3a587a`.
- Evidence: `antiphon-all-group-34.trx:3504` (UnitTestResult; test ID `f147b6c4-4ff9-88ce-e03c-5a0339a3ecf5`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: queued.Status
    should be
QueuedMessageStatus.Sent
    but was
QueuedMessageStatus.Pending
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 218
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 228
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 257
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

#### F006 — `Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies`

- TRX display name: `AlwaysOn_channel_bound_survives_child_death_and_replies(Herdr)`
- Chunk 34; duration `00:00:20.4183448`; execution ID `9305bb30-6913-4c21-8bcf-5bede3ebeba5`.
- Evidence: `antiphon-all-group-34.trx:3589` (UnitTestResult; test ID `00b1ad14-634f-e526-8961-e6551898bc22`).

Error message (first 12 lines, or all if shorter):

```text
TimeoutException: Agent fdadde4a-e1df-410b-a705-c974d5695ab4 never reached a Running persistent session. agent=Failed persistent=b12b8878-4559-4c34-b6b5-50a2b8f395d1 live= sessions=[b12b8878-4559-4c34-b6b5-50a2b8f395d1:Failed:Automated Claude input withheld: observation-unknown:None::NonQualifying]
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.WaitForPersistentSessionAsync(Harness harness, Guid agentId) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 712
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.WaitForPersistentSessionAsync(Harness harness, Guid agentId) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 712
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 107
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 257
   at Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\HerdrAlwaysOnChannelParityTests.cs:line 264
```

#### F007 — `Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path`

- TRX display name: `Queued_message_submits_through_the_real_runtime_runner_pty_path`
- Chunk 35; duration `00:00:12.1241571`; execution ID `dfe007d0-0f15-497e-a160-3287299fcb9a`.
- Evidence: `antiphon-all-group-35.trx:467` (UnitTestResult; test ID `bbc66679-1fe9-e056-865c-45e3c47bf3ca`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: submitted
    should be
True
    but was
False

Additional Info:
    a queued message must submit through the real runtime -> runner -> PTY path
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 134
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 153
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 153
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

#### F008 — `Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_swallowed_enter_is_re_pressed_and_the_body_lands_in_the_transcript_exactly_once`

- TRX display name: `A_swallowed_enter_is_re_pressed_and_the_body_lands_in_the_transcript_exactly_once`
- Chunk 35; duration `00:00:01.6926857`; execution ID `02212ad3-d7d8-4ca7-8d42-c516162b9f7c`.
- Evidence: `antiphon-all-group-35.trx:514` (UnitTestResult; test ID `4a0d390e-b964-3d52-222b-507de7801825`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: snapshot.RawOutput ?? string.Empty
    should contain (case insensitive comparison)
"SWALLOWED-ENTER:"
    but was actually
"\u001b[2J\u001b[m\u001b[H\u001b]0;C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\bin\Debug\net9.0\fakeclaud..."

Additional Info:
    the fake must have eaten the first Enter; without that this asserts nothing
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_swallowed_enter_is_re_pressed_and_the_body_lands_in_the_transcript_exactly_once() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 697
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_swallowed_enter_is_re_pressed_and_the_body_lands_in_the_transcript_exactly_once() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 725
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_swallowed_enter_is_re_pressed_and_the_body_lands_in_the_transcript_exactly_once() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 725
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

#### F010 — `Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests.C514_Health_request_reaches_busy_and_idle_recipients`

- TRX display name: `C514_Health_request_reaches_busy_and_idle_recipients`
- Chunk 35; duration `00:00:22.0446075`; execution ID `d5e7b0f7-275a-4f65-ab82-ad64f94802d9`.
- Evidence: `antiphon-all-group-35.trx:702` (UnitTestResult; test ID `6b3d49c9-962f-d6fa-7b02-198963b9fea7`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: await lane.WaitForPersistedPromptsAsync([work], TimeSpan.FromSeconds(20))
    should be
True
    but was
False

Additional Info:
    the child must record its prompt in its own JSONL
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests.C514_Health_request_reaches_busy_and_idle_recipients() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\RemoteControlRecoveryPtyIntegrationTests.cs:line 39
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests.C514_Health_request_reaches_busy_and_idle_recipients() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\RemoteControlRecoveryPtyIntegrationTests.cs:line 74
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```

#### F011 — `Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Large_multiline_channel_body_submits_as_one_intact_turn`

- TRX display name: `Large_multiline_channel_body_submits_as_one_intact_turn`
- Chunk 35; duration `00:00:01.7565200`; execution ID `e3dd0f3b-c6b0-49c8-aa67-abea0ebc9d8e`.
- Evidence: `antiphon-all-group-35.trx:741` (UnitTestResult; test ID `f95d0f73-7d91-0ecf-d3d4-9e4defeb2e0b`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: SessionQueueTranscriptPump.FileUserPrompts(transcriptPath)
    should be
["[Telegram "Family" - Mike 01:55] HEAD-MARKER add these to my calendar:

booking line 1 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 2 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 3 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 4 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 5 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 6 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 7 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
booking line 8 xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Large_multiline_channel_body_submits_as_one_intact_turn() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 512
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Large_multiline_channel_body_submits_as_one_intact_turn() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 545
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Large_multiline_channel_body_submits_as_one_intact_turn() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 545
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

Supplement: after the first 12 message lines, the expected multiline body ends and the actual prompt collection is `[]`; no recipient prompt was recorded in the asserted collection.

#### F012 — `Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Batched_multiline_body_passes_composer_delivery_verification_and_submits_once`

- TRX display name: `Batched_multiline_body_passes_composer_delivery_verification_and_submits_once`
- Chunk 35; duration `00:00:12.1204747`; execution ID `c20bc120-0c6a-43a2-9485-ce7b3b29b91b`.
- Evidence: `antiphon-all-group-35.trx:1107` (UnitTestResult; test ID `0437cf85-f5fe-1a34-d9f8-3fb672317fff`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: submitted
    should be
True
    but was
False

Additional Info:
    the batched body must pass verification and submit as one turn
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Batched_multiline_body_passes_composer_delivery_verification_and_submits_once() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 389
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Batched_multiline_body_passes_composer_delivery_verification_and_submits_once() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 413
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.Batched_multiline_body_passes_composer_delivery_verification_and_submits_once() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 413
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

#### F013 — `Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_delivery_whose_every_enter_is_swallowed_reverts_and_never_reaches_the_transcript`

- TRX display name: `A_delivery_whose_every_enter_is_swallowed_reverts_and_never_reaches_the_transcript`
- Chunk 35; duration `00:00:02.0298740`; execution ID `eabf82f1-4fd5-49ed-bd50-fc2201babb3a`.
- Evidence: `antiphon-all-group-35.trx:1166` (UnitTestResult; test ID `55d9752e-49ce-f261-b0b7-fe7b248dfa5c`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: raw.Split("SWALLOWED-ENTER:").Length
    should be
4
    but was
1

Additional Info:
    the confirm loop must press Enter SubmitAttempts times and no more. Screen:
\u001b[2J\u001b[m\u001b[H\u001b]0;C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\bin\Debug\net9.0\fakeclaude\fakeclaude.exe\u0007\u001b[?25hFake Claude ready   
SWALLOWENTER:count=9
\u001b]0;✳\u0007
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_delivery_whose_every_enter_is_swallowed_reverts_and_never_reaches_the_transcript() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 776
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_delivery_whose_every_enter_is_swallowed_reverts_and_never_reaches_the_transcript() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 799
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests.A_delivery_whose_every_enter_is_swallowed_reverts_and_never_reaches_the_transcript() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueuePtyIntegrationTests.cs:line 799
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

#### F015 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_AlreadyIdleWhenIdleHasRecipientReceipt`

- TRX display name: `C475_AlreadyIdleWhenIdleHasRecipientReceipt`
- Chunk 36; duration `00:00:02.6479156`; execution ID `1599f563-fcd4-41fd-a6cd-d4747c6a19b6`.
- Evidence: `antiphon-all-group-36.trx:717` (UnitTestResult; test ID `e4819977-9111-078a-e5cb-0dab6e31687a`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: dto.Messages
    should be empty but had
1
    item and was
[QueuedMessageDto { Id = 4ca998cf-f78b-41aa-9ed3-ae3ecb0e0dff, Sequence = 1, Body = already idle, Status = Pending, CreatedAt = 26/10/2026 16:45:34, DeliveryAttempts = 0, Origin = Ui, Parked = False, NoteHeader = , ModalBlocked = True }]
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_AlreadyIdleWhenIdleHasRecipientReceipt() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 172
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_AlreadyIdleWhenIdleHasRecipientReceipt() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 180
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```

#### F016 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C1022_Incomplete_or_stale_receipts_do_not_confirm`

- TRX display name: `C1022_Incomplete_or_stale_receipts_do_not_confirm`
- Chunk 36; duration `00:00:17.4420661`; execution ID `8673253c-23a6-4d0a-a9d9-9ad23d17c76a`.
- Evidence: `antiphon-all-group-36.trx:800` (UnitTestResult; test ID `31422562-6c41-498f-9573-af0362a6ae07`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: Task `attempt`
    should throw
System.InvalidOperationException
    but did not
```

Top stack frames (up to six, exact order):

```text
   at System.Threading.Tasks.ContinuationResultTaskFromResultTask`2.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

#### F017 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`

- TRX display name: `C475_QueueCommitAndTransportRecovery(receipt-before-verdict)`
- Chunk 36; duration `00:00:01.9211807`; execution ID `df20942e-eb02-4b94-b49f-c8ed0035ceb1`.
- Evidence: `antiphon-all-group-36.trx:3105` (UnitTestResult; test ID `9c790327-f229-bf8f-dc0a-be0597591d56`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: Task `world.Queue.EnqueueAsync(world.SessionId, text, MessageSendMode.WhenIdle, CancellationToken.None)`
    should throw
System.InvalidOperationException
    but did not
```

Top stack frames (up to six, exact order):

```text
   at System.Threading.Tasks.ContinuationResultTaskFromResultTask`2.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 114
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 156
```

#### F018 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`

- TRX display name: `C475_QueueCommitAndTransportRecovery(body-before-enter)`
- Chunk 36; duration `00:00:01.6995352`; execution ID `3e84544e-11c2-498a-af39-0a009a3a2cef`.
- Evidence: `antiphon-all-group-36.trx:3303` (UnitTestResult; test ID `cc06b78c-a4ae-6625-56fe-b7eb5a488bd6`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: Task `world.Queue.EnqueueAsync(world.SessionId, text, MessageSendMode.WhenIdle, CancellationToken.None)`
    should throw
System.InvalidOperationException
    but did not
```

Top stack frames (up to six, exact order):

```text
   at System.Threading.Tasks.ContinuationResultTaskFromResultTask`2.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 114
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 156
```

#### F019 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`

- TRX display name: `C475_QueueCommitAndTransportRecovery(attempt-before-write)`
- Chunk 36; duration `00:00:17.2786900`; execution ID `c677a3a5-6d59-4e77-b6a2-077a63035470`.
- Evidence: `antiphon-all-group-36.trx:3477` (UnitTestResult; test ID `5b5ff3a5-c43a-ba5d-23d5-9c27c41f5d90`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: Task `attempt`
    should throw
System.InvalidOperationException
    but did not
```

Top stack frames (up to six, exact order):

```text
   at System.Threading.Tasks.ContinuationResultTaskFromResultTask`2.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 92
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 156
```

#### F020 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`

- TRX display name: `C475_QueueCommitAndTransportRecovery(pending-before-flush)`
- Chunk 36; duration `00:00:17.3130708`; execution ID `2aee0055-b59c-44b1-b075-d9e1b30f5098`.
- Evidence: `antiphon-all-group-36.trx:3609` (UnitTestResult; test ID `67ec57fa-31ad-7141-03e9-1d239914dd90`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: received
    should be
True
    but was
False

Additional Info:
    recipient-receipt: the whole native UserPrompt must reach the destination
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.PtyWorld.WaitForReceiptAsync(String text) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 725
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 150
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 156
```

#### F021 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`

- TRX display name: `C475_QueueCommitAndTransportRecovery(insert-fails)`
- Chunk 36; duration `00:00:17.0877034`; execution ID `7defd97a-484f-42d4-8a05-60094b13fa19`.
- Evidence: `antiphon-all-group-36.trx:3665` (UnitTestResult; test ID `b8867967-97ef-29f1-e7d3-8bbb5e931ecf`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: received
    should be
True
    but was
False

Additional Info:
    recipient-receipt: the whole native UserPrompt must reach the destination
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.PtyWorld.WaitForReceiptAsync(String text) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 725
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 150
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 156
```

#### F022 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_MultilineWritesKeepPasteMarkers`

- TRX display name: `C475_MultilineWritesKeepPasteMarkers`
- Chunk 36; duration `00:00:02.3094303`; execution ID `4d450bfa-c7a5-45d4-a43b-addc6aa2fc2a`.
- Evidence: `antiphon-all-group-36.trx:3730` (UnitTestResult; test ID `fc2f5628-b9c2-00a2-8fd7-067a0c380925`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: world.Forward.Payloads
    should be
["\u001b[200~line one é
line two 世界\u001b[201~", "
"]
    but was (case sensitive comparison)
[] (System.Collections.Generic.List`1[System.String])
    difference
[*, *]

Additional Info:
    normalized LF, both paste markers, then a separate Enter write
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_MultilineWritesKeepPasteMarkers() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 563
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_MultilineWritesKeepPasteMarkers() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 570
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

#### F023 — `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery`

- TRX display name: `C475_QueueCommitAndTransportRecovery(recipient-before-ingestion)`
- Chunk 36; duration `00:00:02.6079918`; execution ID `81592668-f3c5-4e2e-bd11-0a2938cea09c`.
- Evidence: `antiphon-all-group-36.trx:3789` (UnitTestResult; test ID `b41b6401-cdaa-27f7-1722-36103d408ae1`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: Task `world.Queue.EnqueueAsync(world.SessionId, text, MessageSendMode.WhenIdle, CancellationToken.None)`
    should throw
System.InvalidOperationException
    but did not
```

Top stack frames (up to six, exact order):

```text
   at System.Threading.Tasks.ContinuationResultTaskFromResultTask`2.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 114
   at Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionQueueReceiptPlumbingTests.cs:line 156
```

#### F024 — `Antiphon.Tests.Application.CodexCliObservationGapTests.C1029_Per_kind_receipts`

- TRX display name: `C1029_Per_kind_receipts`
- Chunk 36; duration `00:03:37.5612551`; execution ID `8883eb0c-c493-42b0-a574-1a0fdd25d27e`.
- Evidence: `antiphon-all-group-36.trx:3843` (UnitTestResult; test ID `3116f61b-4011-9be3-f49b-8e2130cfd871`).

Error message (first 12 lines, or all if shorter):

```text
AggregateException: C1029 per-kind receipt cases failed (grok/remote=True/busy=False) (grok/remote=True/busy=True) (grok-fault/remote=True/busy=False/after=False) (grok-fault/remote=True/busy=False/after=True) (grok-fault/remote=True/busy=True/after=False) (grok-fault/remote=True/busy=True/after=True)
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.CodexCliObservationGapTests.C1029_Per_kind_receipts() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\CodexCliObservationGapTests.cs:line 127
```

#### F027 — `Antiphon.Tests.Application.SessionMessageQueueGrokPtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path`

- TRX display name: `Queued_message_submits_through_the_real_runtime_runner_pty_path`
- Chunk 38; duration `00:00:53.9168666`; execution ID `e2c2405f-ee0e-4c6a-a5d0-7d74739ab8f8`.
- Evidence: `antiphon-all-group-38.trx:1979` (UnitTestResult; test ID `d9c7d474-813d-cf53-cfb5-e9aceda9519b`).

Error message (first 12 lines, or all if shorter):

```text
ConflictException: Message delivery could not be verified — the terminal did not accept it (the submitting Enter produced no output). See the agent's incidents.
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Server.Application.Services.SessionMessageQueueService.EnqueueCoreAsync(Guid sessionId, String body, MessageSendMode mode, QueuedMessageOrigin origin, String conversationKey, Nullable`1 sourceTaskId, String contentDigest, String noteHeader, Nullable`1 sourceScheduleId, Action`1 onCreated, Boolean deliverIfIdle, Nullable`1 holdUntil, Nullable`1 executionDeadlineAt, Nullable`1 executionTaskId, String capacityRecoveryActionKey, Nullable`1 capacityWaitId, Nullable`1 sourceLandNotificationId, Func`3 afterLandQueueInsert, Nullable`1 mentionOccurrenceId, Nullable`1 sourceChannelInboundId, IReadOnlyList`1 channelMemberInboundIds, Nullable`1 reviewThreadId, String runtimeActionKey, CancellationToken ct) in C:\Antiphon\releases\checkout-997186e6\server\Application\Services\SessionMessageQueueService.cs:line 661
   at Antiphon.Tests.Application.SessionMessageQueueGrokPtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueueGrokPtyIntegrationTests.cs:line 212
   at Antiphon.Tests.Application.SessionMessageQueueGrokPtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueueGrokPtyIntegrationTests.cs:line 226
   at Antiphon.Tests.Application.SessionMessageQueueGrokPtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionMessageQueueGrokPtyIntegrationTests.cs:line 226
```

#### F029 — `Antiphon.Tests.Application.GrokDelegateEndToEndTests.an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge`

- TRX display name: `an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge`
- Chunk 49; duration `00:01:15.2939770`; execution ID `8387b38a-dcf3-4d1f-a64e-7c4297e9b6af`.
- Evidence: `antiphon-all-group-49.trx:711` (UnitTestResult; test ID `38ddcde5-6e61-7368-3788-6f9b1767d926`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: task cd7e56d1 id=cd7e56d1-fb79-4451-8a7a-fade7a27a1fe status=Dispatched nudgedSeq= boundarySeq=7 nudge.CreatedAt= boundary.CreatedAt=2026-10-26T18:20:34.1666270Z nudge.SentAt=. Screen:
\u001b[1t\u001b[c\u001b[?1004h\u001b[?9001hFake Grok ready
\u001b]0;grok\u0007\u001b[23;1H\u001b[2K\u001b[25;1H  ╭──────────────────────────────────────────────────────────────────────────────────────────────────────────────────╮\u001b[26;1H  │ >                                                                                                                │\u001b[27;1H  ╰────────────────────────────────────────────────────────────────────────────────────── grok-4.7 · always-approve ─╯\u001b[29;1H  Shift+Tab:mode  │  Ctrl+x:shortcuts\u001b[30;1H[antiphon-grok-rules:a75949b5cb2546479944f319bf6fe86f]Read the ENTIRE standing rules file "C:\Users\lndco\AppData\Local\Temp\antiphon-e2e-runner-a1271e35a2b444cdb74b09c3128585cc\instructions\grok\d6a741a74ec84da58832d9f3003d3107\rules.md"; use continuation reads beyond the 1,000-line tool default. Do no task work yet. Version=1 generation=11d3717d9673449799a9f431622176cf sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c byteCount=9959. On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id=a75949b5cb2546479944f319bf6fe86f generation=11d3717d9673449799a9f431622176cf sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4cOtherwise reply: ANTIPHON_RULES_FAILED id=a75949b5cb2546479944f319bf6fe86f generation=11d3717d9673449799a9f431622176cf reason=unreadable (or incomplete or revision_mismatch).\u001b]0;Responding - grok\u0007
SUBMITTED:[antiphon-grok-rules:a75949b5cb2546479944f319bf6fe86f]Read the ENTIRE standing rules file "C:\Users\lndco\AppData\Local\Temp\antiphon-e2e-runner-a1271e35a2b444cdb74b09c3128585cc\instructions\grok\d6a741a74ec84da58832d9f3003d3107\rules.md"; use continuation reads beyond the 1,000-line tool default. Do no task work yet. Version=1 generation=11d3717d9673449799a9f431622176cf sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c byteCount=9959. On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id=a75949b5cb2546479944f319bf6fe86f generation=11d3717d9673449799a9f431622176cf sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4cOtherwise reply: ANTIPHON_RULES_FAILED id=a75949b5cb2546479944f319bf6fe86f generation=11d3717d9673449799a9f431622176cf reason=unreadable (or incomplete or revision_mismatch).
ANTIPHON_RULES_ACK id=a75949b5cb2546479944f319bf6fe86f generation=11d3717d9673449799a9f431622176cf sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c
Worked for 1.7s
\u001b]0;grok\u0007[antiphon-task:cd7e56d1] role=Code tier=Frontier workspace=Shared verification=Final CARD-0329 unmarked nudge [antiphon-task:cd7e56d1] YOUR BRIEF IS NOT IN THIS MESSAGE. It is 3,605 characters — too long to type into a terminal without the transport dropping part of it, so it was written out instead. Read it in full before you do anything else: 'C:\Users\lndco\AppData\Local\Temp\antiphon-grok-e2ecnur1pkc.as3\.antiphon\task-cd7e56d1-brief.md' Everything you need is there. Do not start from this summary. --- how to report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire report the caller receives. Close with the `--- next stage ---` block above the token. [antiphon-task:cd7e56d1]\u001b]0;Responding - grok\u0007
SUBMITTED:[antiphon-task:cd7e56d1] role=Code tier=Frontier workspace=Shared verification=Final CARD-0329 unmarked nudge [antiphon-task:cd7e56d1] YOUR BRIEF IS NOT IN THIS MESSAGE. It is 3,605 characters — too long to type into a terminal without the transport dropping part of it, so it was written out instead. Read it in full before you do anything else: 'C:\Users\lndco\AppData\Local\Temp\antiphon-grok-e2ecnur1pkc.as3\.antiphon\task-cd7e56d1-brief.md' Everything you need is there. Do not start from this summary. --- how to report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire report the caller receives. Close with the `--- next stage ---` block above the token. [antiphon-task:cd7e56d1]
FAKE response to: [antiphon-task:cd7e56d1] role=Code tier=Frontier workspace=S
Worked for 1.7s
\u001b]0;grok\u0007
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.WaitUntilAsync(Func`1 condition, TimeSpan timeout, Func`1 failure) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 1063
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 285
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 326
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 334
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 334
```

#### F031 — `Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt`

- TRX display name: `a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt`
- Chunk 49; duration `00:01:38.2701307`; execution ID `9461cae3-44e8-4c1d-b847-bdb60fddf8c8`.
- Evidence: `antiphon-all-group-49.trx:2072` (UnitTestResult; test ID `ca416394-f5b2-cd25-7fa1-9b3aa43b9490`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: status=Dispatched reason= sawOpen=True ends=4:end_turn,6:cancelled
\u001b[1t\u001b[c\u001b[?1004h\u001b[?9001hFake Grok ready
\u001b]0;grok\u0007\u001b[23;1H\u001b[2K\u001b[25;1H  ╭──────────────────────────────────────────────────────────────────────────────────────────────────────────────────╮\u001b[26;1H  │ >                                                                                                                │\u001b[27;1H  ╰────────────────────────────────────────────────────────────────────────────────────── grok-4.7 · always-approve ─╯\u001b[29;1H  Shift+Tab:mode  │  Ctrl+x:shortcuts\u001b[30;1H[antiphon-grok-rules:2752078149834605a40ee2ba99ed1f20]Read the ENTIRE standing rules file "C:\Users\lndco\AppData\Local\Temp\antiphon-e2e-runner-9b5fbb676c2b4d00b7cff96b5f7e80fb\instructions\grok\9721dd41f5404086991f047ba92a654e\rules.md"; use continuation reads beyond the 1,000-line tool default. Do no task work yet. Version=1 generation=e8a149ea91b440dbbd1efab247eeeb26 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c byteCount=9959. On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id=2752078149834605a40ee2ba99ed1f20 generation=e8a149ea91b440dbbd1efab247eeeb26 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4cOtherwise reply: ANTIPHON_RULES_FAILED id=2752078149834605a40ee2ba99ed1f20 generation=e8a149ea91b440dbbd1efab247eeeb26 reason=unreadable (or incomplete or revision_mismatch).\u001b]0;Responding - grok\u0007
SUBMITTED:[antiphon-grok-rules:2752078149834605a40ee2ba99ed1f20]Read the ENTIRE standing rules file "C:\Users\lndco\AppData\Local\Temp\antiphon-e2e-runner-9b5fbb676c2b4d00b7cff96b5f7e80fb\instructions\grok\9721dd41f5404086991f047ba92a654e\rules.md"; use continuation reads beyond the 1,000-line tool default. Do no task work yet. Version=1 generation=e8a149ea91b440dbbd1efab247eeeb26 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c byteCount=9959. On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id=2752078149834605a40ee2ba99ed1f20 generation=e8a149ea91b440dbbd1efab247eeeb26 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4cOtherwise reply: ANTIPHON_RULES_FAILED id=2752078149834605a40ee2ba99ed1f20 generation=e8a149ea91b440dbbd1efab247eeeb26 reason=unreadable (or incomplete or revision_mismatch).
ANTIPHON_RULES_ACK id=2752078149834605a40ee2ba99ed1f20 generation=e8a149ea91b440dbbd1efab247eeeb26 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c
Worked for 1.7s
\u001b]0;grok\u0007[antiphon-task:83ac8980] role=Code tier=Frontier workspace=Shared verification=Final CARD-0491 interrupt [antiphon-task:83ac8980] YOUR BRIEF IS NOT IN THIS MESSAGE. It is 3,321 characters — too long to type into a terminal without the transport dropping part of it, so it was written out instead. Read it in full before you do anything else: 'C:\Users\lndco\AppData\Local\Temp\antiphon-grok-e2ehyvaomdx.20q\.antiphon\task-83ac8980-brief.md' Everything you need is there. Do not start from this summary. --- how to report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire report the caller receives. Close with the `--- next stage ---` block above the token. [antiphon-task:83ac8980]\u001b]0;Responding - grok\u0007
SUBMITTED:[antiphon-task:83ac8980] role=Code tier=Frontier workspace=Shared verification=Final CARD-0491 interrupt [antiphon-task:83ac8980] YOUR BRIEF IS NOT IN THIS MESSAGE. It is 3,321 characters — too long to type into a terminal without the transport dropping part of it, so it was written out instead. Read it in full before you do anything else: 'C:\Users\lndco\AppData\Local\Temp\antiphon-grok-e2ehyvaomdx.20q\.antiphon\task-83ac8980-brief.md' Everything you need is there. Do not start from this summary. --- how to report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire report the caller receives. Close with the `--- next stage ---` block above the token. [antiphon-task:83ac8980]
REPORT-HOLD
\u001b[28;1H\u001b[2K\u001b[30;1H\u001b[2K\u001b[23;1H\u001b[2K\u001b[25;1H  ╭──────────────────────────────────────────────────────────────────────────────────────────────────────────────────╮\u001b[26;1H  │ >                                                                                                                │\u001b[27;1H  ╰────────────────────────────────────────────────────────────────────────────────────── grok-4.7 · always-approve ─╯\u001b[29;1H  Shift+Tab:mode  │  Ctrl+x:shortcuts\u001b[30;1H\u001b]0;grok\u0007\u001b[28;1H\u001b[2K\u001b[30;1H\u001b[2K\u001b[23;1H\u001b[2K\u001b[25;1H  ╭──────────────────────────────────────────────────────────────────────────────────────────────────────────────────╮\u001b[26;1H  │ >                                                                                                                │\u001b[27;1H  ╰────────────────────────────────────────────────────────────────────────────────────── grok-4.7 · always-approve ─╯\u001b[29;1H  Shift+Tab:mode  │  Ctrl+x:shortcuts\u001b[30;1H
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.WaitUntilAsync(Func`1 condition, TimeSpan timeout, Func`1 failure) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 1063
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 788
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 850
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 860
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 860
```

#### F032 — `Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder`

- TRX display name: `a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder`
- Chunk 49; duration `00:01:07.9647981`; execution ID `ee6a9b27-d0d2-4908-9705-10f2020be9c9`.
- Evidence: `antiphon-all-group-49.trx:4155` (UnitTestResult; test ID `6748b93c-8b96-4a76-82a1-fe4edfe43932`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: \u001b[1t\u001b[c\u001b[?1004h\u001b[?9001hFake Grok ready
\u001b]0;grok\u0007\u001b[23;1H\u001b[2K\u001b[25;1H  ╭──────────────────────────────────────────────────────────────────────────────────────────────────────────────────╮\u001b[26;1H  │ >                                                                                                                │\u001b[27;1H  ╰────────────────────────────────────────────────────────────────────────────────────── grok-4.7 · always-approve ─╯\u001b[29;1H  Shift+Tab:mode  │  Ctrl+x:shortcuts\u001b[30;1H[antiphon-grok-rules:de54c6a55eb541889d9748ebfb2ccbb7]Read the ENTIRE standing rules file "C:\Users\lndco\AppData\Local\Temp\antiphon-e2e-runner-d809430ad4df4227b016e5b295b87b81\instructions\grok\557512f7e3df489187b10dc04d497924\rules.md"; use continuation reads beyond the 1,000-line tool default. Do no task work yet. Version=1 generation=90f8bc8baefd4a68a4b08b561fbcb256 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c byteCount=9959. On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id=de54c6a55eb541889d9748ebfb2ccbb7 generation=90f8bc8baefd4a68a4b08b561fbcb256 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4cOtherwise reply: ANTIPHON_RULES_FAILED id=de54c6a55eb541889d9748ebfb2ccbb7 generation=90f8bc8baefd4a68a4b08b561fbcb256 reason=unreadable (or incomplete or revision_mismatch).\u001b]0;Responding - grok\u0007
SUBMITTED:[antiphon-grok-rules:de54c6a55eb541889d9748ebfb2ccbb7]Read the ENTIRE standing rules file "C:\Users\lndco\AppData\Local\Temp\antiphon-e2e-runner-d809430ad4df4227b016e5b295b87b81\instructions\grok\557512f7e3df489187b10dc04d497924\rules.md"; use continuation reads beyond the 1,000-line tool default. Do no task work yet. Version=1 generation=90f8bc8baefd4a68a4b08b561fbcb256 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c byteCount=9959. On a complete matching read, reply with this standalone line: ANTIPHON_RULES_ACK id=de54c6a55eb541889d9748ebfb2ccbb7 generation=90f8bc8baefd4a68a4b08b561fbcb256 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4cOtherwise reply: ANTIPHON_RULES_FAILED id=de54c6a55eb541889d9748ebfb2ccbb7 generation=90f8bc8baefd4a68a4b08b561fbcb256 reason=unreadable (or incomplete or revision_mismatch).
ANTIPHON_RULES_ACK id=de54c6a55eb541889d9748ebfb2ccbb7 generation=90f8bc8baefd4a68a4b08b561fbcb256 sha256=f5ed22d22d62b4a0ad24b56479ddfed705f127eac33dfb2c9207a6f722e81a4c
Worked for 1.7s
\u001b]0;grok\u0007[antiphon-task:f3a4ea32] role=Code tier=Frontier workspace=Shared verification=Final CARD-0084 S6 [antiphon-task:f3a4ea32] YOUR BRIEF IS NOT IN THIS MESSAGE. It is 3,605 characters — too long to type into a terminal without the transport dropping part of it, so it was written out instead. Read it in full before you do anything else: 'C:\Users\lndco\AppData\Local\Temp\antiphon-grok-e2e3y1xbbcn.2rm\.antiphon\task-f3a4ea32-brief.md' Everything you need is there. Do not start from this summary. --- how to report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire report the caller receives. Close with the `--- next stage ---` block above the token. [antiphon-task:f3a4ea32]\u001b]0;Responding - grok\u0007
SUBMITTED:[antiphon-task:f3a4ea32] role=Code tier=Frontier workspace=Shared verification=Final CARD-0084 S6 [antiphon-task:f3a4ea32] YOUR BRIEF IS NOT IN THIS MESSAGE. It is 3,605 characters — too long to type into a terminal without the transport dropping part of it, so it was written out instead. Read it in full before you do anything else: 'C:\Users\lndco\AppData\Local\Temp\antiphon-grok-e2e3y1xbbcn.2rm\.antiphon\task-f3a4ea32-brief.md' Everything you need is there. Do not start from this summary. --- how to report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire report the caller receives. Close with the `--- next stage ---` block above the token. [antiphon-task:f3a4ea32]
FAKE response to: [antiphon-task:f3a4ea32] role=Code tier=Frontier workspace=S
Worked for 1.7s
\u001b]0;grok\u0007
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.WaitUntilAsync(Func`1 condition, TimeSpan timeout, Func`1 failure) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 1063
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 188
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 217
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 217
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 222
   at Antiphon.Tests.Application.GrokDelegateEndToEndTests.a_Kind_Grok_worker_dispatched_from_the_delegate_script_reads_rules_settles_and_prices_on_the_grok_ladder() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokDelegateEndToEndTests.cs:line 222
```

#### F035 — `Antiphon.Tests.Application.DelegationBriefCeilingPtyTests.An_oversized_brief_reaches_the_child_whole_through_the_pointer_path`

- TRX display name: `An_oversized_brief_reaches_the_child_whole_through_the_pointer_path`
- Chunk 54; duration `00:00:17.4462409`; execution ID `37a52ac1-f85b-4da9-b939-aaee8868d6c3`.
- Evidence: `antiphon-all-group-54.trx:452` (UnitTestResult; test ID `273dcfe3-190f-fd49-f38b-07bc9a35d1b5`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: arrived
    should be
True
    but was
False

Additional Info:
    the pointer must reach the child whole through a CLIPPING receiver — it is the one thing standing between a delegate and a headless brief. Raw:
\u001b[2J\u001b[m\u001b[H\u001b]0;C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\bin\Debug\net9.0\fakeclaude\fakeclaude.exe\u0007\u001b[?25hFake Claude ready\u001b[51X
CLIP:mode=deterministic chunkBytes=1024 keep=last seed=1 splitPct=25
\u001b]0;✳\u0007
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.DelegationBriefCeilingPtyTests.An_oversized_brief_reaches_the_child_whole_through_the_pointer_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\DelegationBriefCeilingPtyTests.cs:line 108
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.DelegationBriefCeilingPtyTests.An_oversized_brief_reaches_the_child_whole_through_the_pointer_path() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\DelegationBriefCeilingPtyTests.cs:line 117
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```

#### F036 — `Antiphon.Tests.Application.DelegationBriefCeilingPtyTests.The_same_brief_typed_inline_silently_loses_a_whole_chunk`

- TRX display name: `The_same_brief_typed_inline_silently_loses_a_whole_chunk`
- Chunk 54; duration `00:00:16.4120432`; execution ID `39b9561f-3462-4b13-bd99-61c906420bdc`.
- Evidence: `antiphon-all-group-54.trx:500` (UnitTestResult; test ID `91af6e0f-6bcd-0560-6c73-adb0ab4dac7e`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: submitted
    should be
True
    but was
False

Additional Info:
    something must submit. Raw:
\u001b[2J\u001b[m\u001b[H\u001b]0;C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\bin\Debug\net9.0\fakeclaude\fakeclaude.exe\u0007\u001b[?25hFake Claude ready\u001b[51X
CLIP:mode=deterministic chunkBytes=1024 keep=last seed=1 splitPct=25
\u001b]0;✳\u0007
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.DelegationBriefCeilingPtyTests.The_same_brief_typed_inline_silently_loses_a_whole_chunk() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\DelegationBriefCeilingPtyTests.cs:line 149
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.DelegationBriefCeilingPtyTests.The_same_brief_typed_inline_silently_loses_a_whole_chunk() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\DelegationBriefCeilingPtyTests.cs:line 162
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

### H. Test-owned pty cleanup cannot capture a session (1 results)

The disposal exception names an unresolved capture and manifest. This is a cleanup failure in `TestOwnedPtyHost.DisposeCoreAsync` and `RemoteControlPtyLane.DisposeAsync`; it may mask the original test result. The brief permits TRX/log reads only, so the manifest and live processes were not inspected. Root and product-versus-fixture ownership remain unresolved.

#### F009 — `Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests.C514_Idle_modal_clearance_delivers_three_complete_prompts`

- TRX display name: `C514_Idle_modal_clearance_delivers_three_complete_prompts`
- Chunk 35; duration `00:00:21.7792411`; execution ID `c100dd1b-2a4f-4c74-8973-8574190c4ad3`.
- Evidence: `antiphon-all-group-35.trx:581` (UnitTestResult; test ID `048bd99f-d81f-c692-9f1c-e6018088835d`).

Error message (first 12 lines, or all if shorter):

```text
AggregateException: test-owned disposal unresolved elapsed=00:00:00.0971358 (cleanup-unresolved elapsed=00:00:00.0915945 (capture session=a2b376e7-545e-4738-8a82-3af873f494b9 manifest=C:\Users\lndco\AppData\Local\Temp\antiphon-c514-pty-a2b376e7545e47388a823af873f494b9\pty-hosts\manifests\a2b376e7545e47388a823af873f494b9.json))
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.TestHelpers.TestOwnedPtyHost.DisposeCoreAsync(Boolean kill, Action supplement, Func`2 stop, Func`1 disposeRuntime) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\TestHelpers\TestOwnedPtyHost.cs:line 157
   at Antiphon.Tests.TestHelpers.RemoteControlPtyLane.DisposeAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\TestHelpers\RemoteControlPtyLane.cs:line 342
   at Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests.C514_Idle_modal_clearance_delivers_three_complete_prompts() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\RemoteControlRecoveryPtyIntegrationTests.cs:line 117
```

### I. Windows file sharing rejects fixture read or teardown (3 results)

One teardown cannot delete `landing.lock`; two outbound admission tests cannot read their `barrier` file because another process is using it. Candidate `tests/Antiphon.Tests/Application/ChannelOutboundRecoveryTests.cs:684` performs `File.Exists` followed by `File.ReadAllTextAsync` while the child is running. **Confirmed failure mechanism:** the OS rejects the file operation for an existing conflicting handle. The barrier failures occur at the fixture observation boundary; the landing.lock failure occurs during fixture disposal. Handle ownership and release ordering remain unknown; these are likely Windows fixture/process-lifecycle issues, not proof of lost production ownership.

#### F004 — `Antiphon.Tests.Application.TerminalRunnerSeatReleaseTests.Answer_racing_release_preserves_one_owner`

- TRX display name: `Answer_racing_release_preserves_one_owner`
- Chunk 32; duration `00:00:30.9840065`; execution ID `a391982f-b0e7-46ff-bc0f-15de6d4ba95e`.
- Evidence: `antiphon-all-group-32.trx:3676` (UnitTestResult; test ID `5a19551f-01df-a5c0-6aa7-816c9722b3df`).

Error message (first 12 lines, or all if shorter):

```text
IOException: The process cannot access the file 'landing.lock' because it is being used by another process.
```

Top stack frames (up to six, exact order):

```text
   at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
   at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
   at Antiphon.Tests.TestHelpers.ReadOnlyDirectory.Delete(String path) in C:\Antiphon\releases\checkout-997186e6\tests\Shared\ReadOnlyDirectory.cs:line 15
   at Antiphon.Tests.Application.RunnerSeatReleaseFixture.DisposeAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\RunnerSeatReleaseFixture.cs:line 651
   at Antiphon.Tests.Application.TerminalRunnerSeatReleaseTests.Answer_racing_release_preserves_one_owner() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\TerminalRunnerSeatReleaseTests.cs:line 664
```

#### F025 — `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary`

- TRX display name: `Process_death_preserves_ownership_at_each_boundary(conversion-dispatched)`
- Chunk 37; duration `00:00:18.9176942`; execution ID `027bf41d-3fa3-4851-a82d-acb00ed64b4e`.
- Evidence: `antiphon-all-group-37.trx:8581` (UnitTestResult; test ID `9b6c028c-dbbd-cad9-08de-7ac7954b2b2d`).

Error message (first 12 lines, or all if shorter):

```text
IOException: The process cannot access the file 'C:\Users\lndco\AppData\Local\Temp\antiphon-outbound-admission-453e02b901d044948913af72afb79178\barrier' because it is being used by another process.
```

Top stack frames (up to six, exact order):

```text
   at Microsoft.Win32.SafeHandles.SafeFileHandle.CreateFile(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options)
   at Microsoft.Win32.SafeHandles.SafeFileHandle.Open(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable`1 unixCreateMode)
   at System.IO.Strategies.OSFileStreamStrategy..ctor(String path, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable`1 unixCreateMode)
   at System.IO.File.InternalReadAllTextAsync(String path, Encoding encoding, CancellationToken cancellationToken)
   at Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\ChannelOutboundRecoveryTests.cs:line 684
   at Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\ChannelOutboundRecoveryTests.cs:line 1011
```

#### F026 — `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Running_converter_reconciles_after_dispatcher_death`

- TRX display name: `Running_converter_reconciles_after_dispatcher_death`
- Chunk 37; duration `00:00:17.7743489`; execution ID `46c10ad2-e2da-40c6-9212-af1902e6c5a5`.
- Evidence: `antiphon-all-group-37.trx:8610` (UnitTestResult; test ID `68afbaa4-8818-af3f-9ab8-3cb5b1c790d1`).

Error message (first 12 lines, or all if shorter):

```text
IOException: The process cannot access the file 'C:\Users\lndco\AppData\Local\Temp\antiphon-outbound-admission-1adab64a74394502b7fbe61834295dd6\barrier' because it is being used by another process.
```

Top stack frames (up to six, exact order):

```text
   at Microsoft.Win32.SafeHandles.SafeFileHandle.CreateFile(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options)
   at Microsoft.Win32.SafeHandles.SafeFileHandle.Open(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable`1 unixCreateMode)
   at System.IO.Strategies.OSFileStreamStrategy..ctor(String path, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable`1 unixCreateMode)
   at System.IO.File.InternalReadAllTextAsync(String path, Encoding encoding, CancellationToken cancellationToken)
   at Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\ChannelOutboundRecoveryTests.cs:line 684
   at Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary(String cut) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\ChannelOutboundRecoveryTests.cs:line 1011
```

### J. Database constraint violations in fixture setup or dispatch failure recording (2 results)

The TRX outer exceptions are generic DbUpdateException; the per-chunk logs supply the actual constraints. Privacy sync hits PostgreSQL 23505 on `IX_ExternalIssueRefs_TrackerKind_ExternalId`; standing dispatch hits 23503 on `FK_AgentTaskEvents_AgentTasks_AgentTaskId`. Candidate `tests/Antiphon.Tests/Application/CardFilePrivacySyncTests.cs:184`-`:193` inserts fixed ExternalId `C408_PRIVATE_CARD_ID` and fails on SaveChanges before sync. **Confirmed database rejection mechanisms**, but cross-fixture collision versus retained data, and the source of the missing AgentTask parent row, remain unknown. Treat privacy setup as likely fixture isolation; dispatch failure recording is a product-path candidate.

#### F014 — `Antiphon.Tests.Application.CardFilePrivacySyncTests.Private_card_contributes_no_filename_index_count_or_metadata`

- TRX display name: `Private_card_contributes_no_filename_index_count_or_metadata(False)`
- Chunk 35; duration `00:00:00.9096688`; execution ID `dc679527-0a8c-46b6-9873-34801ac76ccf`.
- Evidence: `antiphon-all-group-35.trx:2254` (UnitTestResult; test ID `7378f997-1c0b-660c-b1ac-1df016f564d7`).

Error message (first 12 lines, or all if shorter):

```text
DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.
```

Top stack frames (up to six, exact order):

```text
   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Storage.RelationalDatabase.SaveChangesAsync(IList`1 entries, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)
```

#### F028 — `Antiphon.Tests.Application.AgentTaskStandingAgentDispatchTests.and_it_writes_nothing_at_all_on_the_agent_row`

- TRX display name: `and_it_writes_nothing_at_all_on_the_agent_row`
- Chunk 42; duration `00:00:08.2883642`; execution ID `68c6ddcd-0449-45a0-ba72-84dd6b86d083`.
- Evidence: `antiphon-all-group-42.trx:416` (UnitTestResult; test ID `7b55b892-458d-9413-bf93-3ae72dfd4dc7`).

Error message (first 12 lines, or all if shorter):

```text
DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.
```

Top stack frames (up to six, exact order):

```text
   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Storage.RelationalDatabase.SaveChangesAsync(IList`1 entries, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)
```

### K. Scheduling, transport acceptance or launch completion does not reach its boundary (6 results)

Inspection fairness leaves one outstanding item; runtime poison-page ingestion times out; phone-home live connection is not accepted; sign-in launch queue idle wait cancels; two checkpoint CLI cancellation tests time out waiting for their boundary. These are distinct waiting boundaries, not a single proven timeout cause. Product scheduling/launch/transport versus fixture timing remains unresolved. Durations below are result durations, not measured component latencies.

#### F001 — `Antiphon.Tests.Application.DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers`

- TRX display name: `C1349_InspectionQueueRecovers(fairness)`
- Chunk 19; duration `00:00:16.7432972`; execution ID `971c0351-1ecc-4290-90d6-459196fd55a9`.
- Evidence: `antiphon-all-group-19.trx:1035` (UnitTestResult; test ID `4e1a114b-240b-fdde-913f-90f699c6a3c4`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: f.Queue.Outstanding
    should be
0
    but was
1
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers(String scenario) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\DispatcherSiblingInspectionSchedulingTests.cs:line 377
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.DispatcherSiblingInspectionSchedulingTests.C1349_InspectionQueueRecovers(String scenario) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\DispatcherSiblingInspectionSchedulingTests.cs:line 401
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```

#### F003 — `Antiphon.Tests.Application.SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow`

- TRX display name: `C1259_saturation_keeps_ingestion_live_and_recovers_overflow(poison-page)`
- Chunk 29; duration `00:00:11.9641459`; execution ID `0fa2dbb1-d263-4af6-bacc-b0405b320fb0`.
- Evidence: `antiphon-all-group-29.trx:4467` (UnitTestResult; test ID `bff40ae1-85e2-a475-741d-10a7bd39e970`).

Error message (first 12 lines, or all if shorter):

```text
TimeoutException: The operation has timed out.
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.SessionRuntimeActionTests.VerifyHostedExecutionPageYieldAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionRuntimeActionTests.cs:line 580
   at Antiphon.Tests.Application.SessionRuntimeActionTests.VerifyHostedExecutionPageYieldAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionRuntimeActionTests.cs:line 603
   at Antiphon.Tests.Application.SessionRuntimeActionTests.VerifyHostedExecutionPageYieldAsync() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionRuntimeActionTests.cs:line 603
   at Antiphon.Tests.Application.SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow(String variant) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\SessionRuntimeActionTests.cs:line 248
```

#### F030 — `Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests.C1032_TransportClientUnsupported`

- TRX display name: `C1032_TransportClientUnsupported`
- Chunk 49; duration `00:00:31.0872162`; execution ID `d1515894-fb8b-4a14-8af0-803efb3c984d`.
- Evidence: `antiphon-all-group-49.trx:2057` (UnitTestResult; test ID `31a9bcfd-bf8e-5473-14d7-a2be43eb1aa3`).

Error message (first 12 lines, or all if shorter):

```text
TimeoutException: Phone-home live connection was not accepted.
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.TestHelpers.PhoneHomeTestHost.WaitLiveAsync(Nullable`1 timeout, String runnerId) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\TestHelpers\PhoneHomeTestHost.cs:line 200
   at Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests.World.Connect() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\PhoneHomeCompletedCardTransportTests.cs:line 181
   at Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests.World.Start(Boolean configured) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\PhoneHomeCompletedCardTransportTests.cs:line 150
   at Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests.World.Start(Boolean configured) in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\PhoneHomeCompletedCardTransportTests.cs:line 151
   at Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests.C1032_TransportClientUnsupported() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\PhoneHomeCompletedCardTransportTests.cs:line 25
```

#### F037 — `Antiphon.Tests.Application.GrokSignInRuntimeTests.A_sign_in_mode_fakegrok_pool_task_never_types_the_brief`

- TRX display name: `A_sign_in_mode_fakegrok_pool_task_never_types_the_brief`
- Chunk 58; duration `00:00:31.5917129`; execution ID `947e36e2-4f1f-4c41-8a3f-21449f4426f3`.
- Evidence: `antiphon-all-group-58.trx:301` (UnitTestResult; test ID `7d5c9f60-7133-40d4-429f-4a36c1a4e710`).

Error message (first 12 lines, or all if shorter):

```text
TaskCanceledException: A task was canceled.
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Server.Application.Services.AgentSessionLaunchQueue.WaitForIdleAsync(TimeSpan timeout, CancellationToken ct) in C:\Antiphon\releases\checkout-997186e6\server\Application\Services\AgentSessionLaunchQueue.cs:line 216
   at Antiphon.Tests.Application.GrokSignInRuntimeTests.A_sign_in_mode_fakegrok_pool_task_never_types_the_brief() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokSignInRuntimeTests.cs:line 109
   at Antiphon.Tests.Application.GrokSignInRuntimeTests.A_sign_in_mode_fakegrok_pool_task_never_types_the_brief() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokSignInRuntimeTests.cs:line 161
   at Antiphon.Tests.Application.GrokSignInRuntimeTests.A_sign_in_mode_fakegrok_pool_task_never_types_the_brief() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GrokSignInRuntimeTests.cs:line 161
```

#### F072 — `Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Main_interrupt_reaches_command_and_unsubscribes`

- TRX display name: `Main_interrupt_reaches_command_and_unsubscribes`
- Chunk 63; duration `00:00:09.4743584`; execution ID `c4b8119c-7436-434a-800a-6931734bd20a`.
- Evidence: `antiphon-all-group-63.trx:817` (UnitTestResult; test ID `690314bd-172d-94d6-f1a7-97eb56de1b3e`).

Error message (first 12 lines, or all if shorter):

```text
TimeoutException: The operation has timed out.
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Main_interrupt_reaches_command_and_unsubscribes() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\C1307CliCancellationTests.cs:line 109
   at Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Main_interrupt_reaches_command_and_unsubscribes() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\C1307CliCancellationTests.cs:line 128
```

#### F073 — `Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Direct_row_cancel_reaches_slot_and_driver`

- TRX display name: `Direct_row_cancel_reaches_slot_and_driver`
- Chunk 63; duration `00:00:09.4728261`; execution ID `5f8381f5-ec2a-49f0-90ef-be1fff66fcb5`.
- Evidence: `antiphon-all-group-63.trx:839` (UnitTestResult; test ID `601d96ab-4f96-494a-c714-b2af9f727fc5`).

Error message (first 12 lines, or all if shorter):

```text
TimeoutException: The operation has timed out.
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Direct_row_cancel_reaches_slot_and_driver() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\C1307CliCancellationTests.cs:line 226
   at Antiphon.Tests.Checkpoints.C1307CliCancellationTests.Direct_row_cancel_reaches_slot_and_driver() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Checkpoints\C1307CliCancellationTests.cs:line 240
```

### L. Land graph refuses a selected markdown fixture, violating the test expectation (1 results)

The failed assertion expected audit exit 0 but got 1. Its exact message identifies `Content Include=Fixtures\Card0418\**` selecting `tests/Antiphon.Tests/Fixtures/Card0418/current-snapshots.md`, a markdown item the audit disallows; `-Update` cannot approve it. **Confirmed reconstructed trigger:** definition-file expansion hits a disallowed document, and pre-check refuses before the expected evaluation-record success. Whether the fixture or audit admission policy should change requires the audit owner; no policy exception was designed.

#### F002 — `Antiphon.Tests.Application.LandGraphEvaluationTests.C1483_S5_CheckRequiresEvaluationRecord`

- TRX display name: `C1483_S5_CheckRequiresEvaluationRecord`
- Chunk 26; duration `00:00:35.7066861`; execution ID `7dfd11b0-5f56-4d41-89ef-d6f52d7ddfc3`.
- Evidence: `antiphon-all-group-26.trx:1476` (UnitTestResult; test ID `7b9aecfb-4830-1e1d-838c-ea05d46c1083`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: real.ExitCode
    should be
0
    but was
1

Additional Info:
    definition file item selects a markdown or text document the audit does not allow; -Update cannot approve it: tests/Antiphon.Tests/Antiphon.Tests.csproj Content Include=Fixtures\Card0418\** -> tests/Antiphon.Tests/Fixtures/Card0418/current-snapshots.md
audit refused: land graph pre-check refused
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.LandGraphEvaluationTests.C1483_S5_CheckRequiresEvaluationRecord() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandGraphEvaluationScriptTests.cs:line 123
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.LandGraphEvaluationTests.C1483_S5_CheckRequiresEvaluationRecord() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\LandGraphEvaluationScriptTests.cs:line 123
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

### M. Pipeline advisory expectation is absent from returned stages (1 results)

The endpoint result has no stage with Role=1 and RecommendedInFlight=1. The TRX does not include the DTO values or effective settings, so stale fixture expectations and product settings precedence are both live. Required evidence: the returned stages and effective setting sources in that fixture.

#### F033 — `Antiphon.Tests.Application.AgentTaskPipelineEndpointTests.pipeline_route_is_literal_and_returns_the_advisory_contract`

- TRX display name: `pipeline_route_is_literal_and_returns_the_advisory_contract`
- Chunk 51; duration `00:00:02.0547946`; execution ID `c22f0383-56ed-4618-ace3-eaa41299b801`.
- Evidence: `antiphon-all-group-51.trx:951` (UnitTestResult; test ID `eeb15078-e2f1-c8c9-7f58-95f24dfc944d`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: dto.Stages
    should contain an element satisfying the condition
((s.Role == 1) && (s.RecommendedInFlight == 1))
    but does not
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.AgentTaskPipelineEndpointTests.pipeline_route_is_literal_and_returns_the_advisory_contract() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\AgentTaskPipelineStatusTests.cs:line 993
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
```

### N. Git index timestamp positive control does not change (1 results)

The failing line is the direct `git status --porcelain` positive control, not the service no-write assertion. Candidate `tests/Antiphon.Tests/Application/GitWorkspaceServiceIndexLockTests.cs:28` first checks that the service kept the timestamp; `:31` runs direct status and `:32` expects a new timestamp. **Confirmed failing boundary:** the direct command leaves the mtime equal to before. Git behavior/configuration or timestamp granularity remains unresolved; this result alone does not show the service taking an optional index lock.

#### F034 — `Antiphon.Tests.Application.GitWorkspaceServiceIndexLockTests.Status_never_takes_the_optional_index_lock`

- TRX display name: `Status_never_takes_the_optional_index_lock`
- Chunk 54; duration `00:00:03.1334863`; execution ID `91035feb-61be-4408-8ba2-547ba069e2b6`.
- Evidence: `antiphon-all-group-54.trx:247` (UnitTestResult; test ID `b1492f98-1db0-33d6-41d6-2364a68b438b`).

Error message (first 12 lines, or all if shorter):

```text
ShouldAssertException: File.GetLastWriteTimeUtc(index)
    should not be
2026-10-26T18:33:10.1478699Z
    but was
```

Top stack frames (up to six, exact order):

```text
   at Antiphon.Tests.Application.GitWorkspaceServiceIndexLockTests.Status_never_takes_the_optional_index_lock() in C:\Antiphon\releases\checkout-997186e6\tests\Antiphon.Tests\Application\GitWorkspaceServiceIndexLockTests.cs:line 32
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
```

## Remaining uncertainties and resolution evidence

The digest is complete for this captured set. A universal root cause is **not confirmed**. Stored evidence confirms the Unix-path fixture failure, diagnostic separator mismatch, database rejection types, and OS sharing rejection boundaries; it does not prove one defect explains all 74 results. The per-group notes specify the missing evidence. No reproduction, base-commit comparison, fixture repair, process inspection or policy change was performed. In particular, do not attribute the read stalls to antivirus, the delivery failures to ConPTY, or dirty tracking to CRLF without the additional evidence named above.

## Not done, noted

No fix was designed or implemented; this artifact transfers the exact Windows failure evidence for separate root-cause and repair work.

## Evidence fingerprints

Fingerprints cover each failing chunk TRX and its chunk log at capture. They identify the local originals; no raw JSON/log/archive payload is committed.

| Source file | Bytes | SHA-256 |
|---|---:|---|
| antiphon-all-group-19.trx | 451304 | `447c43250d3c9adbe921553b9ac14c541f2f048c52524a18a8bbade51f678744` |
| antiphon-all-group-19-tests.log | 3108 | `d297d3fc2a1938ca5dda5d235b48ebbb5d61298bdca2ab43476a1e1949d5362a` |
| antiphon-all-group-26.trx | 1122534 | `a882dcbea3c42a8a7345dd69b839ed1be44a4067602eab8d4388013a8d3ee2a4` |
| antiphon-all-group-26-tests.log | 3610 | `34067b2a426ece849a4a8b6dd86cbea1d21c4cc1c3e429d47c3d800512abfbe4` |
| antiphon-all-group-29.trx | 1886109 | `833c8b45ac6384bb5342ae88552d8efc3b7aa6be1a8da26c319c92ed1b065080` |
| antiphon-all-group-29-tests.log | 2401 | `04a55643bdfbbaa03097d359618d78219fdd378a16dfa1e1593ea71369adad3c` |
| antiphon-all-group-32.trx | 1413793 | `55d0c52fcd60f846d018b6d88db702572d5a792a2c89ec84c976d3cf3f47beb0` |
| antiphon-all-group-32-tests.log | 4294 | `d876f635fb0076afaf0b3b0bc740825254428b2ea25944e934ac0f641fc0de16` |
| antiphon-all-group-34.trx | 1130472 | `dd36c42fd738b4b143b0ff4796e805b55e4bfe076f061cb49b5e8ee87ca2dddf` |
| antiphon-all-group-34-tests.log | 6460 | `6d661915c937e018d8c7e7a25094c2277134ecb77c0310fd2ec29e669091255e` |
| antiphon-all-group-35.trx | 1055745 | `47ea31fe0ca315b1835f515dd8a480e80dfae7862da6552d875ec1c5cc646102` |
| antiphon-all-group-35-tests.log | 41300 | `7a98d788ac210e588deb5c950bd2a36eb21b48b089a0a6c9d050b2f1e9aecf8b` |
| antiphon-all-group-36.trx | 1109590 | `40e4a8fc7299d1450dfc8b50748657f9abdb00acb72f10641d118ec45f1a3f42` |
| antiphon-all-group-36-tests.log | 56329 | `ce762eadc30d5fbbc438f4f3d970a03979f07584d4bb7e306eb5b62d3be788f8` |
| antiphon-all-group-37.trx | 1097672 | `1446b6dc88c82bb10622945ace83ec01a193ed0d7f3a70a1cb249a6355bbdf7e` |
| antiphon-all-group-37-tests.log | 6695 | `e0d66ab41d6b9609358d4f5d112cb31582f0bb03c29789b90f66bdf755fcdbc1` |
| antiphon-all-group-38.trx | 775459 | `23254a7aeeea278ac2666e6971fdb56593e3e8f017a5bf163895fbc6e07d463c` |
| antiphon-all-group-38-tests.log | 4426 | `d8223f25c054bc158f7feb6dc3082bf6525ff6df1d55c496d736df73f57cdc0e` |
| antiphon-all-group-42.trx | 540121 | `5c21be0331d8516fbe02cd06ca6d9b6dd8eedcbb0dad815b9c7817398f251fa5` |
| antiphon-all-group-42-tests.log | 6504 | `70e8177603d7aff961965d4d944df36957d75d4b4b7aaa44162e424429ce7832` |
| antiphon-all-group-49.trx | 779031 | `4aa498fc73ce89887c468089719598aa1f0f97759ca32f9d2b0a86dbf0becbe1` |
| antiphon-all-group-49-tests.log | 482571 | `edc04f983cac8e37b4a1e483a24d909f7fb633667539f616ac67428c9a6d7584` |
| antiphon-all-group-51.trx | 267258 | `357f0e098e1c6f7ddb8b32a20b4a5821fa1d7f6ae8deeb2af5b790389d10b9fc` |
| antiphon-all-group-51-tests.log | 2529 | `b4bc9691c3160ea8af7798f5b9c620b7b8b09e515292c2b32f323e0668d4e5ee` |
| antiphon-all-group-54.trx | 188377 | `bce89a6354efec8b3277c34cb2d2e20630eb426c15eefbc18b430eda4d6d85cb` |
| antiphon-all-group-54-tests.log | 12113 | `6c2546cc480c8d40dac4bcd9eebc90b9e402629fe461717ee146acfab5afdaed` |
| antiphon-all-group-58.trx | 81584 | `a450ee76a3d4c8fa0f962fe150151ae004755872a6c814db3da97be3c6d86ea5` |
| antiphon-all-group-58-tests.log | 5467 | `ebc16321455afb6e0fc321ff3b7d2bf4fdbfdfa3869f1af4f43f183adf82e2a8` |
| antiphon-all-group-61.trx | 911747 | `c1f1e06959aedeebab41c6a12334978bf0579aac46d87928260b59f76a79af0b` |
| antiphon-all-group-61-tests.log | 60953 | `b41375c015f0f1362c608d199e15f2b998d6cffaf71d023dfd24e09d0fbe8a72` |
| antiphon-all-group-62.trx | 531637 | `6ee03c72c773cad9384fc05a9eb29d1ccb48ef467eb644b27a01b14f2eef28a9` |
| antiphon-all-group-62-tests.log | 54364 | `183fdb1329111dda4999f920ad9d7ee05d840aa9f6ecc4623b0614dda3c8ac28` |
| antiphon-all-group-63.trx | 191317 | `8a3db773ceec7ed2285c574274164d2e03f9d970d284076e34c868227c27801f` |
| antiphon-all-group-63-tests.log | 81824 | `1c73b8c227631d10f4f9adb1f031a21439066fc08fe54bc43ae178d5576ea7f0` |

## Validation

The extractor enumerated every available TRX and retained every Failed UnitTestResult exactly once. Digest has 74 result sections and 34 class rows; class counts and group counts both sum to 74. No test or build was run for this documentation-only task. Redacted credential-shaped matches: 0.

`git diff --cached --check` exits 1 with 41 trailing-whitespace warnings, all inside verbatim evidence code blocks. Those original spaces are intentionally retained to satisfy exact error/log excerpt preservation; no formatting warning was suppressed.

--- next stage ---
next: investigate
handoff: Use the per-result Windows TRX/log evidence to resolve the remaining root causes; terminal receipts, checkpoint read stalls and cleanup refusals are not yet causally confirmed.
artifact: docs/investigations/2026-10-26-rc-8f055f87-windows-failure-digest.md
