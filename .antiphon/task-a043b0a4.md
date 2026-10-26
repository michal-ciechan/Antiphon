Fixed the synchronous Windows checkpoint-read dependency in both reader implementations; portable red-to-green verification passed. Historical Windows failure closure remains pending native reruns.

Branch: feat/card-task-a043b0a4. Base: bedea1c03cd788d83b2ff54c40bfb603927bd69c (origin/rc/2026-10-26).
Pushed implementation/documentation tip: 9c1bbeb9f46467b9e8da638b461ac466af361d31. The completion message identifies the final report-only commit containing this file.
Product/test revision verified: 281542f75b74848819ac2340402ff9e2167ec5bd. The subsequent tip changes only the testing/build owner documentation.

The Windows digest at origin/feat/card-task-d5c669aa, 9d7bd87b67c786b28f50920c68cfd0611ab630a5, proves 17 zero-byte no-progress refusals in the verified content reader. It does not prove which host resource caused the silence. Source inspection confirms that both the tool and the standalone PowerShell reader opened synchronous Windows handles, then called ReadAsync and blocked waiting. Windows synchronous-handle ReadAsync queues synchronous work; an overlapped handle submits native async I/O. FileStream infers this mode from SafeFileHandle.IsAsync. Primary runtime references: https://github.com/dotnet/runtime/blob/v9.0.0/src/libraries/System.Private.CoreLib/src/System/IO/RandomAccess.Windows.cs and https://github.com/dotnet/runtime/blob/v9.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileStream.cs . Worker unavailability is a demonstrated dependency and a plausible explanation of the historical stalls, not a proven historical scheduling diagnosis. No evidence established a writer flush, sharing violation or atomic-replacement bug: the native reader already shares read/write/delete, and the Text writer returns from WriteBytes before replacement and digesting.

The product fix adds FILE_FLAG_OVERLAPPED (0x40000000) alongside OPEN_REPARSE_POINT and SEQUENTIAL_SCAN in:
- src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs
- scripts/lib/checkpoint-file-reader.ps1

NativeHandle.OpenStream already uses the FileStream constructor that detects the retained handle's mode, so no adapter or alternate reader was added. Deadline, per-chunk rearming, verification, size caps, retained-handle EOF checks, cancellation behavior, sharing and every existing assertion are preserved. This is a product change, not a fixture-only repair; the fixture-only exemption from Review does not apply.

New verification:
- CheckpointWindowsReadModeTests.Windows_reads_do_not_queue_synchronous_io_behind_blocked_workers: controlled clock models unavailable workers for the actual Windows open request; receipt, digest and write-content recording cover 32, 467, 808 and multi-chunk 524305-byte content. All six cases fail with the expected read-stalled zero-byte exception on 3cd774d2137274262a46de16547eedf671281a87, then all six pass on 281542f75b74848819ac2340402ff9e2167ec5bd.
- CheckpointScriptReaderDeadlineTests adds windows-open to scripts/test-checkpoint-reader-deadline.ps1. It compiles the embedded production C# and invokes its actual OpenWindows method with only the CreateFile kernel call substituted by a portable File.OpenHandle adapter. The production flags and FileStream mode detection stay real. Before the fix it fails at FileStream.IsAsync=false (d834e22271a1f6a445da20cf3edf9cfb25bd0d88); after the fix it passes, preserving flags and content digest. It does not qualify the Windows kernel call or historical load.
- docs/testing-and-build.md records the mode and limits of these witnesses.

Per-class disposition (counts below are Linux reruns; historical Windows counts are from the digest):

| Assigned class | Digest-confirmed failure boundary and fix disposition | Local result | Native work remaining |
|---|---|---|---|
| CheckpointBuildBindingPathTests | 3 ownership-content reads stall at zero bytes; shared tool fix | 3 passed | Rerun class on Windows |
| CheckpointAppTests | 1 run-owner content read stalls; shared tool fix | 3 passed | Rerun class on Windows |
| CheckpointAdmissionContractTests | 1 run-owner read stalls; shared tool fix | 3 passed | Rerun class on Windows |
| CheckpointToolSourceTests | 1 run-owner read stalls; shared tool fix | 2 passed | Rerun class on Windows |
| RerunPolicyTests | 4 output-owner content reads stall; shared tool fix | 6 passed | Rerun class on Windows |
| EvidenceFolderTests | 2 failure-report content reads stall; shared tool fix | 5 passed, 1 skipped | Rerun class, including native file-sharing retry; skipped method was tool_copy_removal_retries_while_a_file_is_still_held_open |
| RunStateStoreTests | 1 808-byte state content read stalls; shared tool fix | 1 passed | Rerun atomic publication witness on Windows |
| C1307CliCancellationTests | 3 owner-read stalls use shared fix; 2 barrier timeouts have no confirmed causal link | 5 passed | Rerun all 5; the two historical timeouts remain unclosed on Windows |
| CheckpointRecoveryWindowsTests | 1 staged-copy read stall uses shared fix; 5 cleanup refusals are separate evidence | Portable recovery_fixture_records_tool_custody_before_cleanup: 1 passed | Rerun the 6 failed native methods. Existing base commits de78f02f7ede6fa490eb35f04e72dfc199edf3d9 and 694ab10791b1b5c865c0c59bb31c5d157f02cc27 already added custody witness/repairs; no duplicate cleanup repair made here |
| CheckpointRepeatBuildIsolationTests | Build succeeded, inventory returned output-inventory-failed; underlying exception was masked. Standalone script reader shares the corrected defect, but this result is not proven to be a read stall | Not rerun; embedded factory/deadline witnesses passed | Rerun exact repeat method on Windows; retain detailed refusal if it persists |

Post-fix selection totals: 54 results, 53 passed, 1 native Windows-only skip, 0 failed. Fresh TRX evidence:
- .antiphon/a043-green/green.trx: 6 passed, 800 ms.
- .antiphon/a043-guards2/guards.trx: 17 passed, 16.364 s (CheckpointEofStallTests 10, HostCleanupCheckpointReadDeadlineTests 2, CheckpointScriptReaderDeadlineTests 5).
- .antiphon/a043-consumers/consumers.trx: 28 passed, 1 skipped, 4.307 s (the eight classes above).
- .antiphon/a043-custody/custody.trx: 1 passed, 839 ms (Entry_change_during_read_is_not_adopted covers invalid metadata, growth, Windows no-follow request and caller/stall cancellation).
- .antiphon/a043-recovery/recovery.trx: 1 passed, 808 ms (portable custody witness only).

All build/test drivers took a granted host build slot; no desktop/native-lock access, background/orphaned process, tags, release scripts, branch switches, rebases or target merges. The brief requested one build; two isolated builds were necessary to execute compiled failing witnesses before the product fix and then the changed product/test code. Both built tests/Antiphon.Tests with OutputPath=bin-a043/ and UseAppHost=false. Red build: 218.60 s; green build: 204.59 s; both 0 errors and 1196 compiler/analyzer warnings (warning attribution was not separately established by a base build). No unrelated full suite or repeated-build integration was run. Slot admission printed waited=0s; per-test durations are retained in TRX, external IO/process phase breakdowns are unavailable/unknown, not claimed zero. No timeouts or retries were widened.

Preserved unsuccessful attempts, not pass credit:
- .antiphon/a043-red/red.trx: expected 6 failed, 0 passed (810 ms), all zero-byte read-stalled exceptions.
- .antiphon/a043-script-red.log: fixture reflection argument wrapping error; not a product red. Fixed in d834e22271a1f6a445da20cf3edf9cfb25bd0d88.
- .antiphon/a043-script-red2.log: expected product-mode assertion red, FileStream.IsAsync=false, exit 1.
- .antiphon/a043-guards/guards.trx: zero tests, exit 8; corrected the pinned TUnit OR suffix syntax and reran once in a043-guards2.

Rebuild and rerun on Windows only once the caller's native lane is available. Wrap every driver with scripts/build-slot.ps1; build once to a fresh bin-a043-native/ output, then use dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-a043-native/ -- with --report-trx and a fresh results directory. Exact filters:

Consumers:
/*/*/(CheckpointBuildBindingPathTests*)|(CheckpointAppTests*)|(CheckpointAdmissionContractTests*)|(CheckpointToolSourceTests*)|(C1307CliCancellationTests*)|(EvidenceFolderTests*)|(RunStateStoreTests*)|(RerunPolicyTests*)/*

Recovery failed methods:
/*/*/CheckpointRecoveryWindowsTests/(detached_staged_copy_survives_starter_exit*)|(locked_tool_delete_is_reported_then_retried*)|(starter_death_before_launch_reclaims_partial_copy*)|(done_live_executor_retains_image*)|(native_identity_keeps_live_then_removes_dead*)|(launch_unknown_survives_until_late_ack*)

Repeat:
/*/*/CheckpointRepeatBuildIsolationTests/repeat_two_then_default_then_three_rebuilds_the_same_project

Reader witnesses/guards:
/*/*/(CheckpointWindowsReadModeTests*)|(HostCleanupCheckpointReadDeadlineTests*)|(CheckpointEofStallTests*)|(CheckpointScriptReaderDeadlineTests*)/*

The repeat method has an existing 300 s bound and the captured Windows failure cost was 211.386 s. These are pending commands, not execution receipts. Ordinary product Review/native qualification remain necessary before claiming Windows closure or release readiness.

Publication/landing:
- Every slice pushed to the assigned branch. Implementation/documentation commits: 3cd774d2137274262a46de16547eedf671281a87; 885dad0997b5f5aaf379d9292ffdcfaf78cee1f4; d834e22271a1f6a445da20cf3edf9cfb25bd0d88; 281542f75b74848819ac2340402ff9e2167ec5bd; 9c1bbeb9f46467b9e8da638b461ac466af361d31. This optional Markdown report is committed individually so the caller can retrieve it; raw generated evidence remains ignored.
- check-evidence-diff.ps1 base..9c1bbeb9f46467b9e8da638b461ac466af361d31: 5 commits, 0 entries, 0 violations, exit 0. git diff --check: exit 0. Landing checks below bind that implementation/documentation tip, before the report-only commit.
- origin/rc/2026-10-26 = bedea1c03cd788d83b2ff54c40bfb603927bd69c. merge-tree --write-tree origin/rc/2026-10-26 HEAD: exit 0, tree 748f98493cd33734c5098742fbf9cf8880bdd8bf.
- origin/master = b6db2de3dd0c0bad2ba30823d01882ca6fc9388e. merge-tree against tip: exit 1, add/add conflicts in .antiphon/task-af2b78c8.md and docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md. Both conflicts reproduce against the assigned base, independently of this delta. Do not call the whole branch master-landable; landing owns its rebase/target reconciliation.
- Removed all 33 producer-owned bin-a043 directories; retained ignored logs/TRX/report and output inventory. Tracked worktree is clean.

--- next stage ---
next: land
handoff: Implementation/documentation tip 9c1bbeb9f46467b9e8da638b461ac466af361d31 targets rc/2026-10-26 and master; the completion message binds the report-only final tip. This is a product fix with portable proof; arrange ordinary Review and the pending native Windows reruns before crediting closure. Reconcile the inherited master report conflicts during landing; no merge/rebase was performed here.
artifact: /work/worktrees/task-a043b0a4/.antiphon/task-a043b0a4.md
