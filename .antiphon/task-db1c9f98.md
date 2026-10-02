CARD-0974 was mechanically replayed onto master 8cfd7dd95ea12b4f988cb8e7d8ddfaf287f34bca; the final service and cost-test content matches the clean Final Review of 2c2c99061475e5a23ff9ea914c20ecc69f7201b6 byte for byte.

Landing owner: Code task db1c9f98-0503-4ca6-aa8c-17699bbdc9ca. Branch: feat/card-task-db1c9f98. Worktree: /work/worktrees/task-db1c9f98. Prior Code task: 2e6d822d; prior clean Final Review: a9dc938e. Restart/deployment: none performed; service activation follows landing through the caller's normal server activation policy.

Six source commits were replayed in the requested order, with source attribution. The old .antiphon/task-2e6d822d.md report was excluded from pick 6. Both CARD-0953 and CARD-0974 allowlist blocks remain, in that order, with LF and a final newline.

History deviation: the first conflict-edit command used unavailable python3. I mistakenly continued pick 3 with conflict markers staged, and replayed picks 4–6 before detecting that failure. Rather than rewrite history, I added a seventh forward commit, 0631415ea07477027b0a3d7e451afa0be54cb374, which removes only the three conflict-marker lines. No tests ran against any intermediate mutation or conflict-marker state. The final diff passes the required proofs. Pick 6 used cherry-pick -x --no-commit, excluded the prior report, and committed the original subject/body plus the source attribution line.

Lesson: replaying prior-branch work onto a newer master is done by cherry-pick in a NEW task (no rebase, no merge). Check edit exit codes before staging or continuing a conflicted pick.

Changed files: server/Application/Services/AttentionService.TaskInputs.cs; tests/Antiphon.Tests/Application/TaskInputAttentionCostTests.cs; tests/Antiphon.Tests/slow-tests-allowlist.txt; this task's report only.

Pre-test proof outputs below were captured at clean source SHA 0631415ea07477027b0a3d7e451afa0be54cb374, after refreshing the initially stale local origin/master ref to the assigned master SHA. The task branch was pushed before CP-A.

```text
$ git diff 2c2c99061475e5a23ff9ea914c20ecc69f7201b6 HEAD -- server/Application/Services/AttentionService.TaskInputs.cs tests/Antiphon.Tests/Application/TaskInputAttentionCostTests.cs
```

Proof (a) output is empty (zero bytes).
```text
$ git diff origin/master HEAD --stat
 .../Services/AttentionService.TaskInputs.cs        | 103 +++++-
 .../Application/TaskInputAttentionCostTests.cs     | 388 +++++++++++++++++++++
 tests/Antiphon.Tests/slow-tests-allowlist.txt      |   2 +
 3 files changed, 477 insertions(+), 16 deletions(-)
```

```text
$ git diff origin/master HEAD -- tests/Antiphon.Tests/slow-tests-allowlist.txt
diff --git a/tests/Antiphon.Tests/slow-tests-allowlist.txt b/tests/Antiphon.Tests/slow-tests-allowlist.txt
index 7cb1a10ef..c849b4660 100644
--- a/tests/Antiphon.Tests/slow-tests-allowlist.txt
+++ b/tests/Antiphon.Tests/slow-tests-allowlist.txt
@@ -213,3 +213,5 @@ Antiphon.Tests.Application.CompletionWarningDeliveryTests
 Antiphon.Tests.Application.LandEvidenceWarningDeliveryTests
 # CARD-0953: isolated PostgreSQL schema plus two real runner registration/retirement cycles.
 Antiphon.Tests.Application.PhoneHomeRunnerRetirementCycleTests
+# CARD-0974: isolated migrated databases, 2,000-row transcript costs and behavior oracle matrix.
+Antiphon.Tests.Application.TaskInputAttentionCostTests
```

Verification: CP-A 206/206; CP-B 10/10; CP-C 1/1. Total 217 executed, 217 passed, 0 failed, 0 skipped. Three isolated builds, one exact filter per row, no reruns and no unlisted build/test invocations. All used scripts/run-checkpoint.ps1 directly, granted slots with zero wait, UseAppHost=false on Linux, expected source SHA 0631415ea07477027b0a3d7e451afa0be54cb374. The source stayed committed and unchanged across all rows.

CP-A roster: AttentionServiceTests 164 (123 main + 32 C691 + 9 CommitRecovery cases), TaskInputReadFailureTests 8, AttentionKindWireTests 3, ParkedMessageSweepServiceTests 12, TaskInputAttentionCostTests 19. Explicit delta from brief: 205 -> 206 because master added AttentionKindWireTests.Host_cleanup_kinds_have_distinct_appended_values_and_json_names (kinds 53–56). TUNIT_MAX_PARALLEL_TESTS=1 was applied to CP-A per the testing owner.

CP-B roster: PhoneHomeRunnerRetirementCycleTests 1; PhoneHomeRunnerRetirementIdentityTests 9. The identity class was located in the CARD-0953 commit history (e94588355 / 031230258); master tip 8cfd7dd95 only changes the cycle's Slow registration. PhoneHomeConnectionTests was not selected, as required for the inherited CARD-0996 flake.

CP-C: Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata, linked from tests/Shared/TestClassificationGuardTests.cs, passed. Per the explicit conditional rule, no Unit lane ran in this replay. The brief records the prior owner and two reviews at identical reviewed service/test content: 3,744 passed / 0 failed / 52 skipped. That is supplied prior evidence, not a new result from this task.

No mutation spot-checks or Windows runs were performed. Every PC/variant remains pending for method-scoped SourceLanding Mutation; these ordinary replay checks do not discharge that obligation. The preserved scratch commits are historical and the final implementation restores the reviewed query plan.

Literal checkpoint lines:
```text
CHECKPOINT CP-A commit=0631415ea07477027b0a3d7e451afa0be54cb374 build=ok filter=/*/*/(AttentionServiceTests*)|(TaskInputReadFailureTests*)|(AttentionKindWireTests*)|(ParkedMessageSweepServiceTests*)|(TaskInputAttentionCostTests*)/* executed=206 passed=206 failed=0 skipped=0 trx=/work/worktrees/task-db1c9f98/.antiphon/db1c9f98-checkpoints/CP-A-20261002-191318-e246/run.trx slot=granted waited=0s dirty=0 source=0631415ea07477027b0a3d7e451afa0be54cb374 sourceState=clean buildSource=verified
```
```text
CHECKPOINT CP-B commit=0631415ea07477027b0a3d7e451afa0be54cb374 build=ok filter=/*/*/(PhoneHomeRunnerRetirementCycleTests*)|(PhoneHomeRunnerRetirementIdentityTests*)/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-db1c9f98/.antiphon/db1c9f98-checkpoints/CP-B-20261002-191741-a7ab/run.trx slot=granted waited=0s dirty=0 source=0631415ea07477027b0a3d7e451afa0be54cb374 sourceState=clean buildSource=verified
```
```text
CHECKPOINT CP-C commit=0631415ea07477027b0a3d7e451afa0be54cb374 build=ok filter=/*/*/TestClassificationGuardTests*/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-db1c9f98/.antiphon/db1c9f98-checkpoints/CP-C-20261002-192052-134b/run.trx slot=granted waited=0s dirty=0 source=0631415ea07477027b0a3d7e451afa0be54cb374 sourceState=clean buildSource=verified
```
All three receipts were checked individually with scripts/validate-checkpoint-receipt.ps1 -Evidence <row/source.json> -ExpectedSourceSha 0631415ea07477027b0a3d7e451afa0be54cb374. Each returned exit 0 and the literal output:
```text
CHECKPOINT SOURCE VALID source=0631415ea07477027b0a3d7e451afa0be54cb374 rows=1
```
The source.json bytes are preserved below because the ignored local checkpoint outputs are not additional branch deliverables. A reviewer can extract each JSON block into a file and validate it with the command above. Original TRX paths and raw test logs remain in this worktree until retirement; the source receipts carry their counters and provenance.
CP-A source.json:
```json
{
  "version": 1,
  "name": "CP-A",
  "start": {
    "commit": "0631415ea07477027b0a3d7e451afa0be54cb374",
    "dirtyFiles": 0,
    "fingerprint": "9d026273cd461086f91cfac980bb75bccf6b5efd4395060d97e6f1798a813ac6",
    "observedAtUtc": "2026-10-02T19:13:18.8430896+00:00",
    "captureStatus": "known",
    "errorCode": null
  },
  "end": {
    "commit": "0631415ea07477027b0a3d7e451afa0be54cb374",
    "dirtyFiles": 0,
    "fingerprint": "9d026273cd461086f91cfac980bb75bccf6b5efd4395060d97e6f1798a813ac6",
    "observedAtUtc": "2026-10-02T19:16:28.0716242+00:00",
    "captureStatus": "known",
    "errorCode": null
  },
  "state": "clean",
  "buildSource": "verified",
  "reason": null,
  "receipt": "CHECKPOINT CP-A commit=0631415ea07477027b0a3d7e451afa0be54cb374 build=ok filter=/*/*/(AttentionServiceTests*)|(TaskInputReadFailureTests*)|(AttentionKindWireTests*)|(ParkedMessageSweepServiceTests*)|(TaskInputAttentionCostTests*)/* executed=206 passed=206 failed=0 skipped=0 trx=/work/worktrees/task-db1c9f98/.antiphon/db1c9f98-checkpoints/CP-A-20261002-191318-e246/run.trx slot=granted waited=0s dirty=0 source=0631415ea07477027b0a3d7e451afa0be54cb374 sourceState=clean buildSource=verified",
  "exitCode": 0,
  "executed": 206,
  "passed": 206,
  "failed": 0,
  "skipped": 0,
  "timings": {
    "slotWaitSeconds": 0.0,
    "buildSeconds": 115.5980231,
    "startupSeconds": 34.9357306,
    "testsWallSeconds": 34.8323811,
    "teardownSeconds": 2.6031842,
    "testHostWallSeconds": 72.3712856,
    "method": "utc-trx-and-monotonic-host",
    "unavailableReason": null
  }
}

```
CP-B source.json:
```json
{
  "version": 1,
  "name": "CP-B",
  "start": {
    "commit": "0631415ea07477027b0a3d7e451afa0be54cb374",
    "dirtyFiles": 0,
    "fingerprint": "9d026273cd461086f91cfac980bb75bccf6b5efd4395060d97e6f1798a813ac6",
    "observedAtUtc": "2026-10-02T19:17:42.3014957+00:00",
    "captureStatus": "known",
    "errorCode": null
  },
  "end": {
    "commit": "0631415ea07477027b0a3d7e451afa0be54cb374",
    "dirtyFiles": 0,
    "fingerprint": "9d026273cd461086f91cfac980bb75bccf6b5efd4395060d97e6f1798a813ac6",
    "observedAtUtc": "2026-10-02T19:20:26.9165219+00:00",
    "captureStatus": "known",
    "errorCode": null
  },
  "state": "clean",
  "buildSource": "verified",
  "reason": null,
  "receipt": "CHECKPOINT CP-B commit=0631415ea07477027b0a3d7e451afa0be54cb374 build=ok filter=/*/*/(PhoneHomeRunnerRetirementCycleTests*)|(PhoneHomeRunnerRetirementIdentityTests*)/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-db1c9f98/.antiphon/db1c9f98-checkpoints/CP-B-20261002-191741-a7ab/run.trx slot=granted waited=0s dirty=0 source=0631415ea07477027b0a3d7e451afa0be54cb374 sourceState=clean buildSource=verified",
  "exitCode": 0,
  "executed": 10,
  "passed": 10,
  "failed": 0,
  "skipped": 0,
  "timings": {
    "slotWaitSeconds": 0.0,
    "buildSeconds": 118.1721781,
    "startupSeconds": 39.5576265,
    "testsWallSeconds": 3.5963844,
    "teardownSeconds": 1.8215824,
    "testHostWallSeconds": 44.9755854,
    "method": "utc-trx-and-monotonic-host",
    "unavailableReason": null
  }
}

```
CP-C source.json:
```json
{
  "version": 1,
  "name": "CP-C",
  "start": {
    "commit": "0631415ea07477027b0a3d7e451afa0be54cb374",
    "dirtyFiles": 0,
    "fingerprint": "9d026273cd461086f91cfac980bb75bccf6b5efd4395060d97e6f1798a813ac6",
    "observedAtUtc": "2026-10-02T19:20:54.2349145+00:00",
    "captureStatus": "known",
    "errorCode": null
  },
  "end": {
    "commit": "0631415ea07477027b0a3d7e451afa0be54cb374",
    "dirtyFiles": 0,
    "fingerprint": "9d026273cd461086f91cfac980bb75bccf6b5efd4395060d97e6f1798a813ac6",
    "observedAtUtc": "2026-10-02T19:23:34.0676822+00:00",
    "captureStatus": "known",
    "errorCode": null
  },
  "state": "clean",
  "buildSource": "verified",
  "reason": null,
  "receipt": "CHECKPOINT CP-C commit=0631415ea07477027b0a3d7e451afa0be54cb374 build=ok filter=/*/*/TestClassificationGuardTests*/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-db1c9f98/.antiphon/db1c9f98-checkpoints/CP-C-20261002-192052-134b/run.trx slot=granted waited=0s dirty=0 source=0631415ea07477027b0a3d7e451afa0be54cb374 sourceState=clean buildSource=verified",
  "exitCode": 0,
  "executed": 1,
  "passed": 1,
  "failed": 0,
  "skipped": 0,
  "timings": {
    "slotWaitSeconds": 0.0,
    "buildSeconds": 146.2538261,
    "startupSeconds": 7.1286484,
    "testsWallSeconds": 2.7176381,
    "teardownSeconds": 0.8384673,
    "testHostWallSeconds": 10.6847399,
    "method": "utc-trx-and-monotonic-host",
    "unavailableReason": null
  }
}

```
To rerun, use the literal filters in the checkpoint lines with scripts/run-checkpoint.ps1 -Project tests/Antiphon.Tests, one fresh bin-db1c9f98-<row>-rerun/ OutputPath per row, -ExpectedSourceSha <current committed HEAD>, -MinExecuted 206/10/1 and -Expect the respective class names. Use a fresh ResultsRoot and TUNIT_MAX_PARALLEL_TESTS=1 for CP-A. Do not rerun green proof without a review reason.

Handoff: Final Review the replay. Verify empty service/cost-test diff against reviewed 2c2c99061475e5a23ff9ea914c20ecc69f7201b6, exactly the three implementation/metadata paths plus this report versus master, the two-line allowlist append and three green qualified checkpoint rows. Inspect the seventh forward conflict-cleanup commit; no history was rewritten. The final report-only commit follows the tested implementation SHA; no production or test file changed after that SHA. This task is the new landing owner; a plain land follows clean Final Review. No land or deployment was performed here.
