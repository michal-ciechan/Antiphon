# Review 8cef76c2: CARD task b20bf5ac state.json publication serialisation

Verdict: sound. No defects found. The fix is correct for the real concurrent publishers, and the new witness goes red without the fix on Linux. Scope: Interim, as the brief says. The rows commissioned in this brief ran; the whole Unit lane and the native-only witnesses did not.

Subject: b20bf5ac, tip e4ce848d8153fcbb9a5ab596581284c5db13ed31. Base: f7c77683ffb940291f26eb6dee63f92efed25bff. Code commits under review: 997fea882 (witness and diagnostic), 244aaf9e5 (fix) and 7e067f8a0 (limiters). The rest of the range is report-only.

## Who publishes concurrently, and whether the lock is cross-process

- Every concurrent writer is a thread inside one executor process. They all share one `RunStateStore` instance: the scheduler loop, the row tasks, the build task and their `Publish` calls (CheckpointApp.cs:161-183 and RunScheduler.cs:786-790). The author's native witness agrees: PID 216488 threads 18 and 19. So a per-instance `lock` (RunStateStore.cs:9,21-26) has the right scope.
- Writes from other processes are ordered, not concurrent:
  - `start` writes `starting` before it launches the executor (CheckpointApp.cs:652).
  - `stop` writes `stopped` only after the executor is observed dead or StopAndWait returns (Program.cs:327-386).
  - The terminal crash path builds a new store instance (CheckpointApp.cs:96). It runs only after RunScheduler's catch has drained every row task and the build task (RunScheduler.cs:323-355), so nothing overlaps it.
- The lock is a Monitor. It is released when an exception unwinds, and it dies with the process, so a crashed holder cannot leave a stuck lock. WriteCore takes no other lock and never calls back into Publish or the slot client, so there is no lock-order deadlock with the receipt write or the build-slot lease.
- Verification now reads under the lock, so it reads this publisher's own bytes. History is appended under the same lock, so entries are neither lost nor duplicated. Replacement is still a rename on Linux and File.Replace on Windows; neither changed.

## Probes (Linux server2, run through build-slot)

- **Pre-fix witness.** Test output from the tip, with Antiphon.Checkpoints.dll built from the f7c77683f RunStateStore.cs, filter CheckpointStatePublicationTests/*: 1 failed, 1 passed, 0 skipped. `concurrent_state_publications_preserve_each_verified_generation` failed at its intended assertion: "a sibling publication must not invalidate the first generation", IOException replacement-unverified. The older `state_publication_survives_contention...` passes before the fix. It is existing coverage, not a red witness.
- **Stress harness** (scratch console program, one shared store, N threads behind a Barrier):
  - Fixed code, 100x8: 0 failed iterations.
  - Fixed code, 300x16 (4,800 publications): 0 exceptions, 0 refusals, 0 lost or duplicate history entries, 0 corrupt lines, 0 leftover .tmp/.bak files. The last history line equalled the final state every time.
  - Pre-fix code, 100x8: all 100 iterations failed:
    - 372 IOException replacement-unverified, HRESULT 0x80131620, the native failure signature
    - 2 receipt-read-refused "multiple links"
    - 441 lost history entries, 8 corrupt history lines, 37 cases where the history tail did not match the final state
    - 372 leftover .bak files
  - So Linux reproduces the defect class, and the fix closes it.
- **Weakening check of 7e067f8a0 (and 997fea882):**
  - The only changes are three `[ParallelLimiter<ProcessSpawnLimit>]` attributes, two `using` lines, three roster entries, and a failure message on `completed.ShouldBe(finished.Task, sink.Diagnostic)`.
  - No assertion, Timeout attribute or two-second delay changed.
  - The roster guard (`Process_spawning_classes_carry_the_limiter`, ProcessSpawnLimitTests.cs:124) fails if any listed class loses the attribute, so it is meaningful.

## Tip rows (one build: `dotnet build tests/Antiphon.Tests -p:UseAppHost=false`, 0 errors, 1374 warnings; then `dotnet exec Antiphon.Tests.dll --treenode-filter`)

All counts are passed/failed/skipped.

| Row | Count |
|---|---|
| CheckpointStatePublicationTests | 2/0/0 |
| CheckpointSlotExecutorTests | 13/0/0, including executor_reachable_broker_grants_build_and_rows |
| ProcessSpawnLimitTests (both limiter guards) | 21/0/0 |
| CheckpointAppTests | 3/0/0 |
| CheckpointAdmissionContractTests | 3/0/0 |
| CheckpointExecutorLogTests | 6/0/0 |
| RcRosterRepairTests | 3/0/0 |
| WindowsUnitPlatformAdmissionTests | 4/0/1 (the skip is C1210_Windows_linux_only_outcomes_are_exact, native-only) |
| C1514LandingSplitBudgetTests C1404/C1577/C1584/C1410/G2865 | 5/0/0 |

- check-evidence-diff over f7c77683f..e4ce848d8: violations=0.
- `git diff --check`: clean.

## Witnesses that run only natively (still pending on Windows)

- C1210_Windows_linux_only_outcomes_are_exact.
- The `NativeContention()` branch of state_publication_survives_contention_and_preserves_completed_rows.
- The File.Replace path in CheckpointNativeIo.Move.
- The author's native 10x isolated repetitions and the 95-case batch.

## Merge notes

- Cherry-pick 997fea882, 244aaf9e5 and 7e067f8a0, in that order, and drop their `.antiphon/task-b20bf5ac.md` hunks. That file is a modify/delete conflict and the only one.
- Chained `merge-tree --merge-base` simulation is clean on rc cf1a12403 and on master e65343132.
- None of the picked files changed in the f7c77683f base range, so the picks do not depend on the verify branch. `ForwardingNativeIo` exists on both targets.
- The whole branch merges cleanly into cf1a12403, its fork point. Against master it has 17 conflicts, all from the verify-branch base commits.
- The picks were not compiled on rc or master.
- Names and the census are unchanged. The ProcessSpawnLimit roster gains three entries, and RcRosterRepairTests and the C1514 census rows are green.

## Advisories (pre-existing, not introduced)

- A-1: Two concurrent `stop` commands on the same run, or a manual second `execute` on a run directory, are still not serialised. Both fail closed with a replacement-unverified refusal. Only an interprocess lock (a lock file or named mutex) would cover them; the design does not have these as normal writers.
- A-2: The lock wait has no bound. The holder is bounded by the retry ladder (at most about 1.55 s of delay) plus synchronous I/O. A verify read that hangs would stall every publisher; the scheduler loop already blocked synchronously before the fix.
- A-3: Row tasks mutate scalar fields of the shared RunState outside the lock while another thread serialises it. Snapshots can tear but cannot throw, because the lists are replaced before concurrency begins. The last lock holder publishes the latest state.
- A-4: A failed history append is still swallowed silently (RunStateStore.cs:53-57).

## Cleanup

There are no bin-* directories in the worktree. The scratch clones (tip, prefix), the stress harness, the build outputs and the stress roots are left under the session scratchpad and nothing was deleted. The shared checkout was read-only. Nothing was pushed except this report.
