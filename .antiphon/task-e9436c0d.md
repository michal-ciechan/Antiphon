CARD-1601 implemented and pushed; the requested Linux selection passed 91/91 normally and 91/91 twice under four CPU burners. No post-fix failure or flake was observed. Ready for Review; rc/master publication is still pending, and the master port has conflicts.

Task: e9436c0d. Branch: feat/card-task-e9436c0d. Tested implementation: **25c715b051b046a2863caf74d67f2e2484ebe281**. Assigned parent: 112c6d3b016da12f8ad632b146292d136dab903f. Rc base: aa6dfd24a51db7c02e8722a05a21817bf4df407a. This report-only commit changes no tested code.

The compiled and embedded PowerShell readers now give one dedicated thread ownership of the open, retained handle, stream, buffer and content/hash sink. Native Linux FileStream reads execute synchronously on that thread through LinuxReadStream; its ReadAsync surface completes inline and never queues a read to the pool. Windows retains OVERLAPPED, OPEN_REPARSE_POINT and SEQUENTIAL_SCAN. The caller supervises monotonic progress through Monitor waits, without a pool timer. The existing injected asynchronous stream boundary remains usable for contract tests. Chunk size remains 256 KiB; size/growth, custody, cancellation and EOF reinspection contracts pass, including the Windows write's two opens and non-overlapped ReadAsync-only stall model.

The silence budget is **3 seconds**. CARD-1599's 6-second scheduling stopgap is unnecessary once Linux reads no longer queue behind blocked callers. Three seconds remains a filesystem-silence tolerance, comfortably above the prior real-read sample (p95 0.460 ms, max 274 ms), rather than an allowance for pool growth. The former 4-second-chunk acceptance test is superseded by a 2-second-per-answer test taking 4 seconds overall and by the real capped-pool witnesses.

The independent overall budget is **30 seconds + ceil(inspected bytes / 1 MiB) seconds**, measured from operation start. Before inspection the same formula uses the supplied content limit. It permits a 1 GiB stamp at 1 MiB/s with a 30-second allowance: 1054 seconds total. Tiny receipts get 31 seconds; the cap never restarts for short reads. Overall refusal uses `read-deadline-exceeded`; existing `read-stalled` and other refusal text remains intact apart from the chosen 3-second value.

Cancellation gets at most 100 ms to unwind and perform the existing retained-handle EOF check, always within the remaining overall budget. A truly uninterruptible syscall cannot be killed safely: the caller refuses, while its background I/O thread retains ownership until the syscall returns. No caller-disposed hash or buffer is used by late work. The blocked-inline witness checks refusal before release and disposal after release. OS scheduling can still delay observation; these are bounded waits, not real-time scheduling guarantees.

Changed files: src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs and CheckpointNativeIo.cs; scripts/lib/checkpoint-file-reader.ps1; scripts/test-checkpoint-pool-saturation.ps1, test-checkpoint-reader-deadline.ps1 and test-checkpoint-large-stamp.ps1; CheckpointReceiptStallBudgetTests, CheckpointEofStallTests, CheckpointScriptReaderDeadlineTests, HostCleanupCheckpointReadDeadlineTests; docs/testing-and-build.md. Production code is mirrored in the embedded reader, including overall and stall policy.

Every build/test driver took scripts/build-slot.ps1 with a granted lease. Builds used UseAppHost=false and isolated bin-c1601red/, bin-c1601red2/, bin-c1601fix/ outputs; tests used dotnet exec against those built DLLs. Three builds succeeded with zero errors (186s, 147s and 149s lease hold respectively). Existing analyzer/compiler warnings remain. No whole Unit or native Windows run was performed; the live desktop qualification was untouched.

| Source SHA | Selection / outcome | Counts | Duration |
|---|---|---|---|
| 26b696e8c61f14a501eb338bf8f0ce81f03eecb5 | ReceiptStallBudget red witnesses | 5 total, 3 failed, 2 passed | 30.0s |
| 8bb37e6d2407a67a8443302dc609a2f5542f79a9 | blocked_inline_read_is_refused_without_waiting_for_the_syscall | 1 total, 1 expected failure | 1.9s |
| 8bb37e6d2407a67a8443302dc609a2f5542f79a9 | C1532_content_and_receipt_limits_are_distinct baseline | 1 passed | 13.7s |
| 25c715b051b046a2863caf74d67f2e2484ebe281 | combined contract and executor | 91 passed, 0 failed/skipped | 78.6s |
| 25c715b051b046a2863caf74d67f2e2484ebe281 | identical selection, four CPU burners, run 1 | 91 passed, 0 failed/skipped | 86.2s (87.196s driver wall) |
| 25c715b051b046a2863caf74d67f2e2484ebe281 | identical selection, four CPU burners, run 2 | 91 passed, 0 failed/skipped | 81.9s (82.783s driver wall) |

The four expected red failures were the compiled and embedded `healthy_receipts_complete_with_every_pool_worker_occupied` cases, `one_byte_trickle_is_refused_by_an_overall_deadline`, and the blocked-inline method. The old 6-second compiled reader and 3-second embedded reader both failed the capped-pool proof: all four workers entered, then healthy reads did not complete in ten seconds. Pool settings were restored and the children drained. The old trickle was accepted without throwing. The old inline call stayed blocked until explicitly released. No build/fixture failure was credited as red.

Two attempted OR filters omitted TUnit's required trailing wildcards and produced zero tests (one pre-fix combined-method invocation and the first post-fix combined-class invocation). Both are retained as invalid selections and receive no credit; exact red methods and the corrected combined filter supplied all required execution.

The 91 tests comprise: CheckpointReceiptStallBudgetTests 6; CheckpointWindowsReadModeTests 10; CheckpointEofStallTests 10; CheckpointLargeStampTests 6; HostCleanupCheckpointReadDeadlineTests 2; HostCleanupCheckpointReceiptTests 8; HostCleanupCheckpointCustodyTests 13; C1307CancellationTests 5; C1307ToolRemovalTests 9; CheckpointSlotExecutorTests 13; CheckpointScriptReaderDeadlineTests 6; CheckpointLargeStampScriptTests 3. Fresh TRX files were checked for all 12 classes and nonzero counts on each of the three final runs. The 1 GiB inclusive stamp, full digest and growth refusal pass in both C# and PowerShell coverage.

During burner run 1 the capped-pool tests took 1.174s compiled and 2.504s embedded including child startup/compilation; blocked-inline refusal/cleanup took 0.313s, trickle 0.080s, and the 1 GiB C# boundary test 12.690s. These are a small provisional sample, not a measured p95. Each run retains per-test durations and membership in TRX/summary.json; internal phase attribution is unknown. Host: server2 Linux x86_64, Debian 12, 24 CPUs, .NET 9.0.22. Four owned CPU burner processes ran for each entire loaded invocation and were terminated and joined before its wrapper released the slot.

Rerun from the task checkout (one build; repeat the second command for the combined selection). Add four owned burners around execution to reproduce the loaded runs; the retained burn-run.cjs does this and joins them in finally.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1601-review-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1601review/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1601-review -- dotnet exec tests/Antiphon.Tests/bin-c1601review/Antiphon.Tests.dll --treenode-filter '/*/*/(CheckpointReceiptStallBudgetTests*)|(CheckpointWindowsReadModeTests*)|(CheckpointEofStallTests*)|(CheckpointLargeStampTests*)|(HostCleanupCheckpointReadDeadlineTests*)|(HostCleanupCheckpointReceiptTests*)|(HostCleanupCheckpointCustodyTests*)|(C1307CancellationTests*)|(C1307ToolRemovalTests*)|(CheckpointSlotExecutorTests*)|(CheckpointScriptReaderDeadlineTests*)|(CheckpointLargeStampScriptTests*)/*' --report-trx --report-trx-filename review.trx --results-directory .antiphon/c1601-review
```

Evidence root: **/work/worktrees/task-e9436c0d/.antiphon/e9436c0d/**. It contains build/run logs, fresh TRX, summary.json, tested-hashes.txt, burner wrapper, merge-tree checks and cleanup.json. Generated evidence remains gitignored. These are the brief's direct gated runs, not checkpoint-tool source-clean certificates; no release/Full Unit or reviewedSourceClean claim is made. All 99 task-created bin-c1601red/, bin-c1601red2/ and bin-c1601fix/ directories were removed after hashing the tested assemblies. No foreign output was deleted.

Tested DLL SHA256: Antiphon.HostCleanup.dll 67879A5D42EE4B854BE621B97F4270D18D03F47207A5D8F8EC89673A6A8A1D9F; Antiphon.Tests.dll D6CF8D58B6C44F62FBEFFEEFA2F36791CBB1926009D9A22C22AD0A6250A45474. Embedded reader source SHA256 C9CFD887635A5AF4A9432D9A81B83D99943F85B81BB1484EE2FC7F4DAF0CC7F9.

Evidence-policy inspection at the tested implementation: base aa6dfd24a51db7c02e8722a05a21817bf4df407a, head 25c715b051b046a2863caf74d67f2e2484ebe281; commits=6, entries=0, violations=0. The final report commit is checked separately before settlement.

Landing status: remote rc/2026-10-27 was still aa6dfd24a51db7c02e8722a05a21817bf4df407a, and merge-tree against that target was conflict-free. Freshly fetched master was fe73cfd2eed86de506a6a307e870c44b05b94451. Full-branch merge-tree against master conflicts in:

- .antiphon/task-af2b78c8.md (inherited add/add).
- docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md (inherited add/add).
- docs/testing-and-build.md (already conflicted at assigned parent).
- src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs (already conflicted at assigned parent).
- scripts/lib/checkpoint-file-reader.ps1 (new conflict after this change).

The assigned parent was checked with merge-tree to establish the four inherited conflicts. No merge, rebase, force-push, tag, publication or native-host action was performed. The branch is not advertised as landable to master. The caller should Review this rc-based candidate, then arrange rc landing and a reviewed master port resolving these conflicts. Both publications remain obligations.

Committed slices: a657c71a41041340b71c11ded081d62ef4337e6d (isolated pool script), 26b696e8c61f14a501eb338bf8f0ce81f03eecb5 (pool/trickle tests), 8bb37e6d2407a67a8443302dc609a2f5542f79a9 (inline-read witness), 25c715b051b046a2863caf74d67f2e2484ebe281 (fix and matching contracts). Every slice was pushed before its run.

--- next stage ---
next: review
handoff: Review the rc-based implementation and the 91-test evidence, then arrange rc landing and the master port; master merge conflicts remain as listed.
artifact: /work/worktrees/task-e9436c0d/.antiphon/task-e9436c0d.md
