# CARD-0971 Code evidence and continuation

Implemented and pushed the ignored nested-repository exception; Final verification is incomplete because the mandatory Unit lane timed out without TRX.

Original Code / landing owner: bc957ed9-9a6a-4503-a99d-fce2f677d4ec.
Branch: feat/card-task-bc957ed9. Worktree: /work/worktrees/task-bc957ed9.
Task base: 02275c3ba8ec6969a6c7bbec8dbe48609367d045.
Implementation tested SHA: 8af6e504024f88fe05c70652264b3e8819bfe13d.
Manifest-repair tested SHA: c451672030617f5f19ad6eef8bc2ae60cc37e01c.
Later changes are documentation only. The final publication SHA is in the task's final report.
Plan: docs/superpowers/plans/2026-10-12-card-0971-ignored-nested-repo.md.
Runtime binding: bound; token was present and was never printed. Runner defaults/catalogue were read; no host/OS pin, restart, landing, deployment or Mutation was performed.

## Change and safety boundary

Files: server/Infrastructure/Git/LandingGit.cs; tests/Antiphon.Tests/Infrastructure/LandRecoveryIgnoredNestedRepositoryTests.cs; the plan and this report.

Git emits an ignored nested repository as `dir/`; the old proof rejected its empty terminal segment. The exception strips exactly one terminal slash, validates the remaining relative path, requires a real directory-based `.git` without reparse components, confirms an outer ignore rule, and requires literal outer `ls-files --cached` evidence of no tracked content. Existing reviewed exact/parent/child collision, byte, index, unsupported gitlink, attribute, sparse and identity guards remain mandatory. Inspection executes no Git command inside the nested repository and does not reset, clean, stage, move a ref or publish.

The matrix has hand-written outcomes. Dirty/non-ignored, symlink/non-ignored and lookalike/non-ignored controls remain refused. Private dirt wholly inside an ignored independent repository cannot affect outer tracked state; that policy is documented, with no inner Git/config/filter execution. File-based `.git` pointers remain refused by the new exception. No existing assertion or timeout was changed.

## Ordinary outcomes

| ID | Actual outcome |
|---|---|
| V-1 | Original-master production red at `N.ignored.ExpectedProof`: True expected, False observed. Repaired production green. |
| V-2 | non-ignored repository refused, green before/after |
| V-3 | tracked gitlink refused, green before/after |
| V-4 | ignored `.git` file refused, green before/after |
| V-5 | non-ignored symlink refused, green before/after |
| V-6 | dirty non-ignored repository refused, green before/after |
| V-7 | non-ignored lookalike refused, green before/after |
| V-8 | reviewed exact/parent/child collisions refused, all 3 green before/after |
| V-9 | ignore-error/not-ignored/tracked-error/tracked-output/git-symlink refused, all 5 green before/after |
| R-1 | full LandRecoveryCheckoutTests: 34 passed, all 12 methods |
| R-2 | full LandRecoveryIgnoredObstructionTests: 5 passed, 2 Windows-only skips, all 6 methods represented |
| R-3 | full LandingRegistrationFreshnessTests: 7 passed, all 4 methods |
| R-4 | full AgentTaskLandHalfResetTests: 55 passed, all 20 methods, including fresh and same-request recovery/publication |
| R-5 | INCOMPLETE: whole Unit selection timed out at unchanged 24 minutes; no TRX/counts. Never marked passed. |

The corrected baseline at 31d6f697123dfc1a72bb6d9fa9d1a41edd97b179 had 15 executed, 14 passed, only V-1 failed. `LandingGit.cs` matched original master exactly. All 14 refusal controls passed before and after. CP-2..CP-6 cover 116 executed passes and 2 platform skips.

Initial CP-1 build failed on a missing DTO import and an unsupported Shouldly expression-tree pattern. Fixed in test-only commit e4d7064404759e08595bebf5842e4d7d6dad870e. Its first actual test run had 13 passes, V-1's intended assertion failure, and a collision fixture add failure. Linked worktrees share info/exclude; force-adding the fixture-owned reviewed obstruction fixed setup in 31d6f697123dfc1a72bb6d9fa9d1a41edd97b179. The final baseline then had the required 14/1 result. These were failure-driven repairs, not green repetitions.

CP-4's initial floor of 6 counted a Windows-only method incorrectly. Fresh TRX proved 5 executed passes plus 2 platform skips. The manifest arithmetic was corrected to 5 with the identical whole-class filter and unchanged assertions, then CP-4 reran green. The final rerun is at c451672030617f5f19ad6eef8bc2ae60cc37e01c.

The Unit console observed two assertion failures, both confirmed independently at the original base by exact method-scoped CP-8/CP-9:

* ChannelOutboundContractTests.Server_and_gateway_keep_source_and_transport_boundaries: InstructionBundles.All["orchestrator"].Text lacks `configured channel step` (line 48).
* GrokRunnerImageContractTests.Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers: installed.Success expected True, observed False (line 20).

Each base checkpoint executed exactly 1 method and failed at the same assertion. These are proven inherited failures, with no unrelated repair in this branch. The Unit timeout itself is not proven inherited. Its async sample shows RemoteScriptContractTests.C983_Required_jq_driver_rejects_successful_false_probe -> C983Fixture.Run -> Process.WaitForExitAsync, with other tests waiting behind semaphores. This identifies the sampled wait, not its root cause.

The lower-priority brief excluded broad Unit, but the Code-stage bundle expressly required the whole Unit lane for Final. The plan disclosed that precedence before running it. No full-assembly run was made. The mandatory Unit run and failure-driven builds exceeded the requested slice estimate/budget; the deadline was not widened.

## Receipts and provenance

Fresh TRX definitions/results were inspected for every completed class/method selection and nonzero counts. CP-7 has no TRX and no claimed counts. Original CHECKPOINT lines below are unedited, including failed attempts and slot/wait fields.

`validate-checkpoint-receipt.ps1` accepted the main report's CP-2,CP-3,CP-5,CP-6 at the implementation SHA:

```text
CHECKPOINT SOURCE VALID source=8af6e504024f88fe05c70652264b3e8819bfe13d rows=4
```

CP-4 has clean source and verified build provenance in its receipt, but the strict zero-skip validator returns `row_failed` because its two Windows-only cases did not execute. No all-scope clean Review certificate is claimed. CP-7 is incomplete; CP-8/CP-9 are intentional inherited-failure confirmation receipts, never green certificates.

Base evidence was copied into the task's ignored `.antiphon/c971-base-checkpoints/20261013-002644-5b30/` before removing the clean, task-owned detached baseline checkout. Original absolute source/TRX paths remain in its unedited receipts; the archive contains the retained payloads. Raw TRX/JSON/log/dump/checkpoint outputs stay ignored; only this individual Markdown report is committed.

Unlisted infrastructure: one slot-gated Checkpoints tool bootstrap build to bin-c971-tool/, UseAppHost=false, slot=granted waited=0s, 0 errors/1 existing nullable warning. Slot-gated diagnostic-only dotnet-stack/dotnet-dump installs: slot=granted waited=105s and 15s respectively. Stack/dump collection/analysis were read-only diagnostics, not extra tests. An initial mistyped expected SHA was refused before any driver; an external results-root request was also refused before any driver. All actual test/build rows used the closed checkpoint manifest and the host slot gate. No unleased or NoSlot run.

No green repetitions were requested. CP-1 had a compile retry and a fixture-failure retry. CP-4 had one roster-floor retry. No deliberate mutant/red/restore/green cycle was run.

### Run 20261012-233343-cce1 (exit 2)

```text
CHECKPOINT CP-1 commit=f929a40807537d9a519382d88a1571345b871aab build=failed filter=/*/*/LandRecoveryIgnoredNestedRepositoryTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=f929a40807537d9a519382d88a1571345b871aab sourceState=clean buildSource=unknown
```

Build bin-c971-base: state=failed, slot=granted, waited=0s, sourceState=clean, buildSource=unknown.

### Run 20261012-233729-a360 (exit 1)

```text
CHECKPOINT CP-1 commit=e4d7064404759e08595bebf5842e4d7d6dad870e build=ok filter=/*/*/LandRecoveryIgnoredNestedRepositoryTests/* executed=15 passed=13 failed=2 skipped=0 trx=/work/worktrees/task-bc957ed9/.antiphon/checkpoints/20261012-233729-a360/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=e4d7064404759e08595bebf5842e4d7d6dad870e sourceState=clean buildSource=verified
```

Build bin-c971-base: state=ok, slot=granted, waited=45s, sourceState=clean, buildSource=verified.

### Run 20261012-234503-2466 (exit 1)

```text
CHECKPOINT CP-1 commit=31d6f697123dfc1a72bb6d9fa9d1a41edd97b179 build=ok filter=/*/*/LandRecoveryIgnoredNestedRepositoryTests/* executed=15 passed=14 failed=1 skipped=0 trx=/work/worktrees/task-bc957ed9/.antiphon/checkpoints/20261012-234503-2466/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=31d6f697123dfc1a72bb6d9fa9d1a41edd97b179 sourceState=clean buildSource=verified
```

Build bin-c971-base: state=ok, slot=granted, waited=45s, sourceState=clean, buildSource=verified.

### Run 20261012-234911-338a (exit 5)

```text
CHECKPOINT CP-2 commit=8af6e504024f88fe05c70652264b3e8819bfe13d build=ok filter=/*/*/LandRecoveryIgnoredNestedRepositoryTests/* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-bc957ed9/.antiphon/checkpoints/20261012-234911-338a/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=8af6e504024f88fe05c70652264b3e8819bfe13d sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=8af6e504024f88fe05c70652264b3e8819bfe13d build=reused filter=/*/*/LandRecoveryCheckoutTests/* executed=34 passed=34 failed=0 skipped=0 trx=/work/worktrees/task-bc957ed9/.antiphon/checkpoints/20261012-234911-338a/rows/CP-3/run.trx slot=granted waited=15s dirty=0 source=8af6e504024f88fe05c70652264b3e8819bfe13d sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=8af6e504024f88fe05c70652264b3e8819bfe13d build=reused filter=/*/*/LandRecoveryIgnoredObstructionTests/* executed=5 passed=5 failed=0 skipped=2 trx=/work/worktrees/task-bc957ed9/.antiphon/checkpoints/20261012-234911-338a/rows/CP-4/run.trx slot=granted waited=30s dirty=0 source=8af6e504024f88fe05c70652264b3e8819bfe13d sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=8af6e504024f88fe05c70652264b3e8819bfe13d build=reused filter=/*/*/LandingRegistrationFreshnessTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-bc957ed9/.antiphon/checkpoints/20261012-234911-338a/rows/CP-5/run.trx slot=granted waited=15s dirty=0 source=8af6e504024f88fe05c70652264b3e8819bfe13d sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=8af6e504024f88fe05c70652264b3e8819bfe13d build=reused filter=/*/*/AgentTaskLandHalfResetTests/* executed=55 passed=55 failed=0 skipped=0 trx=/work/worktrees/task-bc957ed9/.antiphon/checkpoints/20261012-234911-338a/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=8af6e504024f88fe05c70652264b3e8819bfe13d sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=8af6e504024f88fe05c70652264b3e8819bfe13d build=reused filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=24m slot=granted waited=0s dirty=0 source=8af6e504024f88fe05c70652264b3e8819bfe13d sourceState=clean buildSource=verified
```

Build bin-c971-final: state=ok, slot=granted, waited=0s, sourceState=clean, buildSource=verified.

### Run 20261013-002645-3f14 (exit 0)

```text
CHECKPOINT CP-4 commit=c451672030617f5f19ad6eef8bc2ae60cc37e01c build=ok filter=/*/*/LandRecoveryIgnoredObstructionTests/* executed=5 passed=5 failed=0 skipped=2 trx=/work/worktrees/task-bc957ed9/.antiphon/checkpoints/20261013-002645-3f14/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=c451672030617f5f19ad6eef8bc2ae60cc37e01c sourceState=clean buildSource=verified
```

Build bin-c971-final: state=ok, slot=granted, waited=0s, sourceState=clean, buildSource=verified.

### Run 20261013-002644-5b30 (exit 1)

```text
CHECKPOINT CP-8 commit=02275c3ba8ec6969a6c7bbec8dbe48609367d045 build=ok filter=/*/*/ChannelOutboundContractTests/Server_and_gateway_keep_source_and_transport_boundaries executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-bc957ed9-baseline/.antiphon/checkpoints/20261013-002644-5b30/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=02275c3ba8ec6969a6c7bbec8dbe48609367d045 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=02275c3ba8ec6969a6c7bbec8dbe48609367d045 build=reused filter=/*/*/GrokRunnerImageContractTests/Qualified_pin_agrees_with_install_assertion_docs_fixtures_and_verifiers executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-bc957ed9-baseline/.antiphon/checkpoints/20261013-002644-5b30/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=02275c3ba8ec6969a6c7bbec8dbe48609367d045 sourceState=clean buildSource=verified
```

Build bin-c971-inherited: state=ok, slot=granted, waited=0s, sourceState=clean, buildSource=verified.

## Pending Mutation and continuation

All PCs and every variant remain pending for method-scoped SourceLanding Mutation after ordinary Code, Review and original-owner landing:

* PC-1: C971_NestedRepositoryMatrix / ignored; restore terminal-slash refusal.
* PC-2: same method / nonignored, dirty, symlink, lookalike; remove outer untracked refusal.
* PC-3: same method / gitlink; accept before unsupported index/tree/blob guards.
* PC-4: same method / git-file; permit file-based administration.
* PC-5: C971_IgnoredNestedRepositoryCollision / exact, parent, child; bypass normalized collision guards.
* PC-6: C971_IgnoredNestedRepositoryEvidenceRefused / ignore-error, not-ignored, tracked-error, tracked-output, git-symlink; accept unknown/unsupported evidence.

Filters must remain `/*/*/LandRecoveryIgnoredNestedRepositoryTests/<ExactMethod>` for each PC cycle. Mutation owns deliberate mutants and missing-control discovery. None was run or discharged here.

Next stage: Code, because R-5 remains incomplete. Preserve this pushed branch/source and original landing owner. Complete the Final Unit scope using bounded committed selections; separately triage the sampled C983 script wait. Preserve the two confirmed inherited assertion failures as facts; do not loosen their assertions, widen deadlines or repair unrelated slices silently. Proceed to Review only when ordinary verification is complete. Restart: none. No land/deploy requested.

The final task report supplies the final HEAD/remote identity, evidence-range guard, merge-tree result, tracked-file count and clean status. The report-writing commit contains documentation only; no production/test changes followed the green feature verification.
