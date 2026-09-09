# CARD-0448 D1/D4 correction and D3 restoration

D1 and D4 are fixed, D3 is reverted, and scoped regression passes 34/34. Ready for
the final land-readiness review; no landing or deployment was performed.

## Branch reconciliation

The requested `feat/card-task-8bae07b3` at `8b9af49f` contained the review report but
none of the reviewed implementation. Merge `8cdbfda8` brings the exact reviewed
commit `b719b3b4` into that branch without conflicts. This round's changes follow
that merge; existing CARD-0450 instructions and review documents remain intact.

Implementation checkpoints: `745721e8` (D1 recovery and D3 restoration),
`2f6b8e6c` and `1019c0e4` (D4 assertion and enum-name correction). Final regression
ran on `1019c0e4` with no mutations or uncommitted source changes.

## Changes

- `scripts/recover-repository-children.ps1`: explicit operator recovery for
  `<git-common-dir>/antiphon/children/`. Resolves the common directory from either
  checkout, takes the same exclusive `landing.lock` handle as admission, validates
  journal schema/repository/process identity, and clears only dead/reused identities.
  Preview is the default. Execution additionally requires
  `-ConfirmDescendantsExited`, because a dead root cannot prove descendant exit.
  Live, unknown, malformed, torn, wrong-repository and busy cases retain the fence.
  It kills no processes and never removes the lock file or Git/publication state.
- `RepositoryMutationLeaseTests`: the real killed-worker test now proves refusal
  while its Git child lives, retention during preview and missing confirmation,
  then admission through the main checkout after recovery through its linked
  worktree. Seven additional cases cover live/reused/unknown/malformed/torn/
  wrong-repository/busy evidence. The original C24 `exited` semantics are unchanged.
- `AgentTaskLandStageOutcomeTests`: query the original single `Landed` event as
  Fresh, and the latest `LandingCleanup` as CleanupRetry with completed cleanup;
  also assert structured `LandingMode`. Production landing behavior is unchanged.
- `scripts/cleanup-build-junk.ps1`: restored exactly from `563e4b18^`; Git diff
  against that version is empty. The weekly cleanup behavior is restored.
- `docs/orchestration-loop.md`, `docs/testing-and-build.md`, and the journal's
  code comment document recovery and remove the inventory-only cleanup claim.

D2 and V-07/V-21/V-29 are unchanged and remain deferred to CARD-0452.

## Verification

| Check | Executed | Passed | Failed |
|---|---:|---:|---:|
| D4 original query, exact reland method (expected red) | 1 | 0 | 1 |
| D4 corrected query, same method | 1 | 1 | 0 |
| D1 deletion omitted, exact killed-worker method (expected red) | 1 | 0 | 1 |
| D1 restored deletion, same method | 1 | 1 | 0 |
| RepositoryMutationLeaseTests, final regression | 19 | 19 | 0 |
| AgentTaskLandStageOutcomeTests, final regression | 10 | 10 | 0 |
| AgentTaskDispatchBaseGuardTests, final regression | 5 | 5 | 0 |

D4 red failed on `mode=CleanupRetry` versus the first event's `mode=Fresh`.
D1 red replaced only `Remove-Item -LiteralPath $file.FullName -Force` with a
comment in the recovery script. It failed at `recovered.ShouldNotBeNull`, proving
the test detects persistent admission deadlock. The script was restored using a
fresh write before green. Each red/green invocation selected one exact method.
No mutation remains. An intermediate D4 build had an incorrect enum type name;
`1019c0e4` corrected it to `LandOperationMode` before green execution.

The full initial build completed with 0 errors and 133 warning lines. The final
combined regression executed exactly the three named classes, 34 passed and no
failures/skips, in 8m09s. TRX counters and executed class identities were checked.
No full-suite, E2E, live-stack restart or production-runner validation was attempted.

Preserved logs and five TRX files:
`C:\Antiphon\worktrees\card-task-8bae07b3\.antiphon\verification-653f00ef\`
(`d1-red`, `d1-green`, `d4-red`, `d4-green`, `regression`).

The 14 `bin-c448d1` output directories created by this run remain in the worktree.
Automatic approval review rejected their cleanup command as "blocked by policy"
without a more specific reason; no deletion occurred.

Rerun from `C:\Antiphon\worktrees\card-task-8bae07b3`:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c448d1/ -- --treenode-filter '/*/*/(RepositoryMutationLeaseTests*)|(AgentTaskLandStageOutcomeTests*)|(AgentTaskDispatchBaseGuardTests*)/*' --report-trx --report-trx-filename regression.trx
```

For the individual controls, replace the filter with
`/*/*/RepositoryMutationLeaseTests/C448_C24_KilledWorkerLeavesLiveGitChildFenced`
or `/*/*/AgentTaskLandStageOutcomeTests/reland_of_an_already_landed_task_runs_cleanup_only`.

## Operator recovery

```powershell
pwsh -NoProfile -File scripts/recover-repository-children.ps1 -Repository C:\src\Antiphon
# After inspecting and confirming that the recorded children's descendants exited:
pwsh -NoProfile -File scripts/recover-repository-children.ps1 -Repository C:\src\Antiphon -Execute -ConfirmDescendantsExited
```

Exit 3 means busy or retained evidence; exit 0 means no child journal files remain.
The command was exercised only against isolated test repositories in this round.
Unknown start intents still require investigation. A server restart alone does
not prove descendant exit; a machine reboot does. Clearing the journal restores
admission, not the validity of Git state: retry landing/dispatch through its normal
recovery path. This preserves the original fail-closed admission contract while
providing the missing explicit recovery route.

## Handoff

Review the D1 recovery command and D4 event assertions on `feat/card-task-8bae07b3`,
then make the final land-readiness decision. D3 is an exact restoration; CARD-0452
continues to own D2 and the deferred acceptance gaps.
