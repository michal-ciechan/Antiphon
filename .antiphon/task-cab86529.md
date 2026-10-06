# CARD-1122 Final Review (task cab86529) of Code owner f307f4d4

Outcome: no defects. Groups B and C are test-only, stricter than before, and green at the reviewed SHA. The two remaining reds in the class are the CARD-1120 group A retention methods, reproduced as INHERITED at the base commit, and the CARD-1120 branch merges onto this one with no textual conflict.

--- review evidence ---
subjectTaskId: f307f4d4-ddf5-4f81-9465-d81c7b1f853d
reviewedSourceSha: 6aea1d5aa69b83df8ff9d3bc76ab4ecea427fbb0
reviewedSourceClean: true
ordinaryScopeCompleted: Full

Base: 303dc7853e8888b69bf987ac821d97c878da3d15. Branch under review: refs/heads/feat/card-task-f307f4d4 (one commit, two test files, 107+/26-). Evidence guard over the range: commits=1 entries=0 violations=0 (scripts/check-evidence-diff.ps1 -BaseRef 303dc78 -HeadRef 6aea1d5, exit 0).

## Rerun (one checkpoint-tool run, serial, one isolated build)

Run 20261006-223439-2fc3 at /work/worktrees/task-cab86529/.antiphon/checkpoints/20261006-223439-2fc3 (report.md, report.json, rows/CP-n/run.trx). Source 6aea1d5aa state=clean buildSource=verified; every build and row slot=granted (broker budget 4, maxcpucount 6, UseAppHost=false). Build 331 s on a loaded broker. Wall 11m36s. `validate --evidence report.json --expected-source-sha 6aea1d5aa --rows CP-2,CP-3,CP-4,CP-5` printed CHECKPOINT SOURCE VALID rows=4; validate over all rows refuses with row_failed because CP-1 carries the two inherited reds.

| CP | Filter | Executed | Passed | Failed | Skipped | Why in the selection |
|---|---|---:|---:|---:|---:|---|
| CP-1 | `/*/*/PostLandMutationDeliveryTests/*` | 67 | 65 | 2 | 0 | the changed class (both partial files; 46 distinct methods) |
| CP-2 | `/*/*/TestClassificationGuardTests/*` | 1 | 1 | 0 | 0 | registry guard named in the brief |
| CP-3 | `/*/*/SlowTestTripwireTests/*` | 2 | 2 | 0 | 0 | registry guard named in the brief |
| CP-4 | `/*/*/TestDbFixtureLazyInitializationTests/A_worker_child_exits_before_the_shared_store_warmup*` | 6 | 6 | 0 | 0 | parameterised over every worker marker; drives the changed PostLandMutationDeliveryWorker.RunAsync with a malformed payload and requires exit 1 before the shared-store warm-up |
| CP-5 | `/*/*/TestDbFixtureLifecycleTests/Worker_mode_list_names_every_owned_child_worker` | 1 | 1 | 0 | 0 | the worker-mode registry over every *Worker Marker constant |

CP-1 failures, both `SessionQueuedMessages.CountAsync(m => m.SourceTaskId == ...) should be 0 but was 1`: C478_CompletionReplayAfterRetention (line 236) and C478_CompletionReplayAfterPartialEnqueueRetention (line 309). The tool's baseline stage checked out 303dc78 in a detached worktree, built it, and ran the two methods there: executed 2, passed 0, failed 2, so baseline/classification.txt marks both INHERITED. These are CARD-1120 group A (land-note Delegation queue rows retained while channel reply discovery is open, CARD-0519 S10) and are owned by task ff6bc528.

Group B and C results in CP-1 all passed: C478_G134..G138, C478_G156_CompletionRestore, C478_V09a_LandProducerToCaller, C478_V09a_LandCrashMatrix for before-enqueue, queue-inserted and lost-wakeup (both busy arms), and C478_V09a_LandCrashMatrix(publication-commit, False). The five pre-seeded arms (receipt-before-save x2, after-receipt x2, post-submit-process) also passed. This matches the Code report's green TRX (70/68/2) and base TRX (67/51/16) in /work/worktrees/task-f307f4d4/.antiphon/c1122-*/, whose executed rosters and failed names I cross-checked.

Other users of the grep targets: PostLandMutationDeliveryWorker is consumed only by PostLandMutationDeliveryTests and the TestWorkerModes registry (CP-4, CP-5 cover the registry side). ConfirmLandReceiptAsync, AssertHarnessSubmitAndCatchUpReceipt and the c478-land- uuid are private to the class. IsConfirmedBy and IsCompleteIn live in src/Antiphon.SessionRunner.Contracts/PromptSubmissionMatch.cs and are untouched by this branch (the diff is limited to the two test files), so the roughly thirty classes that call them are not affected and were not run; a broad run of them would have no invariant to name. No whole-Unit run, per the brief.

## Checks

(a) Census strictness, by reading tests/Antiphon.Tests/Application/PostLandMutationDeliveryTests.cs lines 961-997. In the flush arms (`alreadySubmitted == false`) AssertHarnessSubmitAndCatchUpReceipt requires: receipts with the receipt uuid Count == 1 (a duplicate receipt gives 2, a missing receipt gives 0, both fail); the receipt text non-null, IsConfirmedBy and IsCompleteIn the submitted body (a clipped or foreign receipt fails; the body carries publication= and the task id, so RequiresTextMatch is true and the check is not vacuous); the remaining prompts Count == 1 (a second non-receipt prompt gives 2, zero prompts fails already at receipts.Count); that remaining prompt's uuid null and IsCompleteIn the body (a stray foreign-uuid prompt or a clipped harness row fails). ConfirmLandReceiptAsync then pins the receipt at sequence 11 and the harness prompt at or below 10, and still requires Adapter.Inputs.Count unchanged across reconcile. The pre-seeded arms keep `prompts.Count.ShouldBe(1)`, equal to the old assertion. The new census is strictly stronger than the old `Count == 1` in the flush arms: the old one passed when catch-up stored no receipt at all (harness row only), the new one fails there. G156 uses the same helper with the c478-queued- uuid. I showed each case by reading; no local mutation was run (the review is read-only and each mutation costs a five-minute rebuild on this host).

(b) Publication-commit (lines 762-812 and the worker). The test launches the dotnet worker for cut C14, polls for worker-ready.json for up to two minutes, and asserts File.Exists(ready) with the worker's stderr as the message: a worker that exits without writing it fails, a worker that hangs without writing it fails on the budget cancellation. The C14 pause sits in LandingSafetyHarness.RunCrashWorkerAsync after PublicationConfirmed is acknowledged, so the kill is a crash past the publication commit. The resume launch uses the same launcher with cut resume and asserts `resumed.ExitCode.ShouldBe(0, stderr)`; RunAndExitAsync exits 1 on any exception and a timed-out resume is killed and reports a non-zero code, so a non-zero exit fails. Then the Outcome note must exist with publication= and the stricter land-receipt census from (a) runs. The worker is the sibling's shape: `dotnet <Antiphon.Tests.dll> --treenode-filter ...` with the ANTIPHON_C478_DELIVERY_WORKER payload, dispatched by TestWorkerModes before the shared-store warm-up, plus ANTIPHON_C448_TEST_CONNECTION for RunCrashWorkerAsync; the ownership guard mirrors LandingRemovalCrashWorker (temp root, antiphon-c448- prefix, ready inside the root, cut in {C14, resume}, canonical/fixture-owner.txt equal to the task id). The pwsh script and the pwsh ProcessStartInfo are gone from this test. tests/Antiphon.Tests/Application/AgentTaskLandRecoveryTests.cs line 135 still uses the pwsh LoadFrom host; that is CARD-0890 (Backlog), whose 2026-10-02 note already prescribes this dotnet worker pattern, so no new card.

(c) PostLandMutationDeliveryWorker is `internal static` inside the test assembly; the census helper and ConfirmLandReceiptAsync are private. No file under src/ or server/ changed.

(d) No assertion was deleted. The two replaced `Count == 1` lines became the stronger census above (and stay `Count == 1` for the pre-seeded arms). The publication-commit test gained the exit-code assertion and drains stdout as well as stderr. One pre-existing lenient assertion is unchanged and outside this branch: ConfirmPersistedQueuedReceiptAsync ends with `prompts.Count >= 1` plus an Any(IsCompleteIn); G156's own census now covers that path exactly.

## Conflict with CARD-1120 (task ff6bc528)

origin/feat/card-task-ff6bc528 is at 4aef5c37f10f0d87614eb71985da075b8d82b6f3 (two commits from the same base 303dc78: eac53e5a1 CARD-1120 retention, 4aef5c37f CARD-1122 group D). `git merge-tree --write-tree --name-only 6aea1d5aa 4aef5c37f` exits 0 with tree 1e7ee7b843c9832d287b0076b18119b40ac14bae and no conflicted path. On the shared file CARD-1120's hunks are at lines 225, 298 and 1578 (a new private StampDiscoveryClosedAsync); this branch's are at 556, 773, 803 and 969; no overlap and no duplicate member name. This branch also merges cleanly onto origin/master 9463e1fba (tree 56d1364c3; master moved by four unrelated commits since the base). When CARD-1120 lands, the two inherited reds in CP-1 close. Note for the orchestrator: the group D fix (DispatchBaseNotificationTests) travels on the CARD-1120 branch, not this one.

## Code report audit

There is no plan document and no `### Checkpoints` table for this task; the brief defined the selection and the Code report says so. The Code report lists each run with its slot lease id and held time (base build 163 s, base red 251 s, green build 116 s, green tests 243 s), UseAppHost=false, counts per class, the two residual failures by name, zero PC rows (test-only change, nothing to mutate; PCs stay pending for SourceLanding Mutation), and evidence guard 0 violations. No unlisted build or test run. Counts reproduce here exactly.

## Cleanup

The tool's `clean --run 20261006-223439-2fc3` removed the 27 kept bin-c1122rv/ outputs; the driver bin-c1122rv-drv was removed with a root-confined delete; `git status --short --untracked-files=all` is empty.

--- next stage ---
next: land
handoff: Land Code owner f307f4d4-ddf5-4f81-9465-d81c7b1f853d at 6aea1d5aa69b83df8ff9d3bc76ab4ecea427fbb0 with -ExpectedSourceSha; Final Review found no defects; the two CP-1 reds are CARD-1120 group A, INHERITED at base, closed by ff6bc528 (4aef5c37f) which merge-trees cleanly onto this branch.
artifact: .antiphon/task-cab86529.md
