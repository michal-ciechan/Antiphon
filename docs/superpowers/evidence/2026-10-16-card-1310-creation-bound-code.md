# CARD-1310 round 3: Windows creation bound

Incomplete: the Windows lower-bound repair is committed, but the required E2E
receipt still fails on a different file. The brief explicitly requires reporting
the new refusal and stopping rather than widening this repair. No final green or
complete ordinary scope is claimed.

Task: e9f573b2-52b2-48cf-bcc0-ef3867f27ac1.
Original Code / landing owner: a5c09b23-f3a1-4956-a4bd-221d9bbf7628.
Branch: feat/card-task-e9f573b2.
Worktree: C:\Antiphon\worktrees\card-task-e9f573b2.
Task base: e755bfe355d8573605824d47ab1cfee11e95e602.
Original task base: 6e23e0b4c8d6b2ae12218c2c0058593dbf773530.
Plan: docs/superpowers/plans/2026-10-16-card-1310-coverage-mapping.md.
Mutation candidate: docs/superpowers/mutations/2026-10-16-card-1310-candidate.md.
Restart: none. No landing or production session/AppHost operation was performed.

## Change and red proof

Test commit ee8d4f3bf8184288ef7012ce453c9e6543474218 adds two native Windows
regressions. The copied-output case preserves old source change/write timestamps
but a fresh destination creation time. The pre-root case moves an old-created
file into the root and gives it recent change/write times. Both failed on their
intended custody assertions, not fixture setup, at this test-only commit.

Fresh TRX inspected: 19 results, 17 passed, 2 failed, 0 skipped. Class identities:
CheckpointCoverageMappingTests 14; CheckpointCoverageMappingNativeTests 5.
Failures:

- Windows_copied_old_timestamps_inside_creation_window_are_adopted: expected null,
  received output-ledger-mismatch for copied.dll.
- Windows_file_created_before_root_is_retained: expected refusal, received null.

The previous 17 cases passed. These are red-first results, not post-fix proof.
Run 20261016-092202-f8c8 ended RED exit=1 in 345 seconds; outputs were retained.

```text
CHECKPOINT CP-1 commit=ee8d4f3bf8184288ef7012ce453c9e6543474218 build=ok filter=/*/*/(CheckpointCoverageMappingTests*)|(CheckpointCoverageMappingNativeTests*)/* executed=19 passed=17 failed=2 skipped=0 trx=C:\Antiphon\worktrees\card-task-e9f573b2\.antiphon\checkpoints\20261016-092202-f8c8\rows\CP-1\run.trx slot=granted waited=0s dirty=0 source=ee8d4f3bf8184288ef7012ce453c9e6543474218 sourceState=clean buildSource=verified
```

Repair commit 0d3e9d731cc70f4636d6647421d0883cc87d4a1b changes only the Windows
lower-bound selection in OutputCustody: creation time must be at least the root's
recorded creation. Existing change/write upper bounds, identity, link, owner and
root ACL checks remain. Linux keeps the existing change-time lower bound. The
creation observation participates in the adoption before/after Entry comparison.
No receipt reader limit, timeout or assertion was loosened.

## Decisive receipt: still refused

Run 20261016-093151-aa91 used the repaired tool and fresh
tests/Antiphon.E2E/bin-c1310-r3-final-e2e. CP-3 selected exactly
OutputDistillationCanaryGuardTests.Configuration_refuses_unowned_resources_before_start.
Fresh TRX inspected: all 12 argument-expanded identities match that class/method;
12 passed, 0 failed, 0 skipped. This case starts no server, runner or broker.
Run wall time was 183.4836759 seconds. The row passed, but the run ended RED exit=2.

```text
CHECKPOINT CP-3 commit=0d3e9d731cc70f4636d6647421d0883cc87d4a1b build=ok filter=/*/*/OutputDistillationCanaryGuardTests/Configuration_refuses_unowned_resources_before_start executed=12 passed=12 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-e9f573b2\.antiphon\checkpoints\20261016-093151-aa91\rows\CP-3\run.trx slot=granted waited=0s dirty=0 source=0d3e9d731cc70f4636d6647421d0883cc87d4a1b sourceState=clean buildSource=verified
```

Exact terminal refusal:

```text
refused: output-ledger-mismatch; left in place: C:\Antiphon\worktrees\card-task-e9f573b2\tests\Antiphon.E2E\bin-c1310-r3-final-e2e\.playwright/node/win32_x64/node.exe
```

Both validators returned exit 2 and the exact line
`CHECKPOINT SOURCE INVALID reason=cleanup_refused`:

```powershell
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261016-093151-aa91/report.json -ExpectedSourceSha 0d3e9d731cc70f4636d6647421d0883cc87d4a1b -Rows CP-3
dotnet tools/Antiphon.Checkpoints/bin-c1310-r3-tool/Antiphon.Checkpoints.dll validate --evidence .antiphon/checkpoints/20261016-093151-aa91/report.json --expected-source-sha 0d3e9d731cc70f4636d6647421d0883cc87d4a1b --rows CP-3
```

Read-only diagnosis: node.exe is 93,580,104 bytes, created at 08:34:26 UTC during
this build, with LastWriteTime 2026-09-23 20:16:54 UTC. CheckpointWrite.Describe
uses ReceiptFile.ReadBytes, whose unchanged limit is 64 MiB (67,108,864 bytes).
ConfinedFileReader rejects larger files, and AdoptCreated catches the IOException
without recording content. This is a separate barrier to investigate; no size
limit or reader was changed and no extra diagnostic build/test was run.

Source observations were clean with verified build provenance at both tested
SHAs. The failed receipts are not clean qualification certificates.

## Ordinary verification status

| ID | Actual outcome |
|---|---|
| V-1 | Passed in pre-fix CP-1; repaired-source CP-1 pending after stop gate. |
| V-2 | All seven variants passed in pre-fix CP-1; repaired-source proof pending. |
| V-3 | All six variants passed in pre-fix CP-1; repaired-source proof pending. |
| V-4 | Intended red demonstrated; post-fix green pending. |
| V-5 | Intended red demonstrated; post-fix green pending. |
| R-1 / CP-2 | Not run this round after the explicit stop gate; current failure comparison pending. |
| R-2 / CP-4 | Whole Unit lane not run after the explicit stop gate. |
| M-1 / CP-3 | 12/12 tests passed; both required receipt validators failed. |

Final scope is incomplete. CP-1 repaired-source, CP-2, CP-4 and validating M-1
remain required; none is deferred under an Interim claim or marked passed.
PC-1 through PC-6 and every listed variant remain pending method-scoped
SourceLanding Mutation, including missing-control discovery. No deliberate mutant
was run in Code.

Review 85782542's named remote branch was absent. Its full API result, existing
worktree plan/evidence note and fresh 31-name CP-2 failure list were recovered.
The API result is saved under .antiphon/c1310-r3/review-85782542.txt; failure names
under .antiphon/c1310-r3/review-cp2-failures.txt, extracted from its
20261016-090148-28fc CP-2 TRX. The Review records 69 executed, 38 passed, 31 failed,
12 skipped and matching original-base failures. That history is not a current
CP-2 run or comparison.

## Execution and custody

Two necessary unlisted CLI bootstrap builds used build-slot.ps1:

- c1310-r3-bootstrap-red: slot=granted waited=0s, held=6s, succeeded, 4 warnings.
- c1310-r3-bootstrap-fixed: slot=granted waited=0s, held=39s, succeeded, 4 warnings.

Checkpoint builds and rows each show slot=granted waited=0s. Imported manifests
set UseAppHost=false as the plan requires and use unique output names. No timeout
was widened. There were no repetitions of unchanged proof selections.
One invocation used unsupported `--manifest` syntax and refused before launching;
the subsequent positional manifest invocation is the recorded run. Every run was
awaited through terminal reporting, including tool-copy removal after executor
death. Generated receipts, TRX and manifests remain ignored in .antiphon.

GET /api/runner-defaults and /api/session-runners were read and saved under
.antiphon/c1310-r3. This task requires native Windows proof; no fleet placement
setting was embedded or changed.

The evidence-history guard passed over e755bfe..0d3e9d7 (2 commits) and over
6e23e0b..0d3e9d7 (9 commits), zero violations. A fresh fetch and merge-tree against
origin/master 59ed791fa4c51ce72c7adb8581a10a383575d1ad were conflict-free, tree
a4b62d7ec5d18b01a9a69ee350b574975918646d. No merge was made. The final report
records the evidence-only tip and final history check separately from tested SHAs.

Automatic approval review rejected removal of completed task-owned outputs,
including explicit absolute paths verified inside this worktree, with "blocked by
policy". No deletion bypass was attempted. Retained output roots:

- tests/Antiphon.Tests/bin-c1310-r3-red/
- tests/Antiphon.E2E/bin-c1310-r3-final-e2e/
- tools/Antiphon.Checkpoints/bin-c1310-r3-tool-red/
- src/Antiphon.HostCleanup/bin-c1310-r3-tool-red/
- tools/Antiphon.Checkpoints/bin-c1310-r3-tool/
- src/Antiphon.HostCleanup/bin-c1310-r3-tool/

The two checkpoint tool shadow copies were removed by their normal validated
cleanup. No foreign output was deleted. The requested next work is a separately
authorized bounded repair for the newly exposed large-output custody refusal,
then the pending ordinary verification and initial Review.
