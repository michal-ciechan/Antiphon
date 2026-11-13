# CARD-1857 Review round 2 (diff only): task cdd7d74c

Subject: origin/feat/card-task-511a436a, tip 05d66fa35362fbab3ad9acd7bd062c54755596f7 (Code task 511a436a-f4fe-4b53-a254-a45a282aeab8, Grok author). Range reviewed: a997e156e2b60f21dd509457e68f5747f42db4b3..05d66fa35 (2 commits), fixes for Review d7868261 B-1 and B-2.

Verdict: **CLEAN.** Both round-1 blockers are fixed. No regression, no new blocker.

## Diff checks
- Files changed a997e156e..05d66fa35: `tests/derived-impact-index.json` and `tests/Antiphon.Tests/Application/C0747QueueRowExpiryTests.cs` only. cc48c7005..05d66fa35 touches the same two files, so no production file changed.
- B-1 (afa7505ff): the index diff is exactly the PromptSubmissionMatch.cs cap entry (classes 58 -> 59, owed 33 -> 34, `first` list reordered), the SessionRunnerContracts.cs cap entry (427 -> 428, owed 402 -> 403), and the PromptSubmissionMatch edge list gaining C0747QueueRowExpiryTests. Those are the entries round 1 predicted, and nothing else changed.
- B-2 (05d66fa35): four `[Timeout(15_000)]` changed to `[Timeout(180_000)]` at lines 293, 336, 368 and 417. Every [Timeout] in the class is now 180_000, except the scale test, which is 300_000. The diff changes nothing else.

## Runs (reviewed SHA 05d66fa35, native default output, UseAppHost=false, TEMP=/tmp, one build-slot lease per command)
- `scripts/regenerate-derived-impact-index.ps1` (with build) at the tip: `derived-impact-index unchanged`, and the worktree stayed clean afterwards.
- `dotnet build tests/Antiphon.Tests`: exit 0.
- Fresh process, `/*/*/C0747QueueRowExpiryTests/C1857_*`: 4/4 passed, 0 skipped, 41.6 s. This cold-start run hit the 15 s timeout in round 1.
- Warm, one run of `/*/*/(C0747QueueRowExpiryTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)/*`: 92 total, 92 passed, 0 failed, 0 skipped, 12 m 25 s. Results by class:
  - C0747QueueRowExpiryTests 13/13
  - DerivedImpactIndexTests 27/27, including committed_index_matches_the_builder in 26.7 s
  - TestClassificationPolicyTests 41/41
  - TestClassificationGuardTests 1/1
  - SlowTestRegistryMergeTests 5/5
  - SlowTestTripwireTests 2/2
  - NativeLinuxPolicyWitnessTests 3/3

  C1857 durations in that run: Delivery_claim 8.2 s (it started first), Survivor 1.3 s, Held_queue_lock 1.3 s, Stop_between_batches 0.6 s.
- `check-evidence-diff.ps1 -Repository . -BaseRef cc48c7005 -HeadRef 05d66fa35`: commits=3, entries=0, violations=0, exit 0.
- `git merge-tree --write-tree origin/master HEAD` (origin/master = 586be574b88d1957dbf2a96c5dad771e3bcd8b37): clean, tree dff09c3364ef9aab972c09b5277afcfe7d738131. Master also edited the index, for CardFileRepositoryTryKillTests. To check the merge, I ran the regen (`-NoBuild -Repo`) against a detached scratch worktree of that merged tree. It printed `unchanged`. The scratch worktree was then removed.
- Not run: the checkpoint tool. CARD-1857 has no plan and no ### Checkpoints table, so the named classes ran as direct build-slot runs, the same as in round 1.

## Advisories
Round-1 A-1 (reflection on the private `ExpireAgedQueueRowsIfDueAsync`) and A-2 (owed impacted selectors) still stand. They go to cards and do not hold the land. This round adds no new advisory.

PCs: none discharged. Every PC stays pending for SourceLanding Mutation.
