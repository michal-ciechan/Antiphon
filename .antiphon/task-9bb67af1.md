C514 test-only adequacy fix: the two-reads witness and permission-dialog witness are committed; the portable baseline passes 59/59 and all five requested controls produce their intended failures. Restored-source verification passed: 9/9, zero failures or skips, after a fresh clean-source rebuild.

Task: 9bb67af1. Branch: `feat/card-task-9bb67af1`. Inherited base: `e177509681c9ff5b0320f5a32a2e7c7a9d74c57a`. Tested source commit: `792b37741cde203245ab672f2b959a2b6f2e9853` (pushed).

Only executable source changed by this task is `tests/Antiphon.Tests/Application/RemoteControlMaintenanceQueueTests.cs`. This report is the other deliverable. No production changes are committed. No test or assertion was removed or relaxed. No definition file changed; no `-Update` was run. No native Windows tests, live qualification operations, tags, publication, rc rebase, or force push occurred.

The committed probes run through the production recovery service and runtime with the existing fake adapter:

- `R605_Unreadable_redraw_after_composer_snapshot_is_refreshed` adapts the review's source. The runner DTO must first prove the composer is exactly `/remote-control`; only then does the adapter advance its output sequence and paint an unreadable redraw. The test requires `ArmedObserved`, one command body, exactly one Enter, one submitted body and no raw runner input. This distinguishes a single snapshot fence from a second runner read.
- `R605_Permission_dialog_after_composer_snapshot_is_never_entered` uses the same composer-proving trigger, then paints `PermissionWaitFixture.CapturedFrame`. It requires `ArmUnconfirmed`, `modal-blocked` persisted evidence, command-body input only, zero Enters, no submitted body and no raw runner input.
- Every Maintenance test now has the existing 30-second ceiling; cancellation is passed into its recovery execution calls. The prior parameterized Enter-refresh ceiling is unchanged. Shared-store warmup remains outside the per-test ceiling. The provisional ceiling retains the existing cold-DI/EF rationale (the prior 10-second ceiling had failed twice); this run's coldest Maintenance case was 11.126 seconds. The single-run class p95 was 1.138 seconds; this is not a per-method historical p95 claim.

Portable ordinary selection at clean `792b37741cde203245ab672f2b959a2b6f2e9853`: 59 executed, 59 passed, 0 failed, 0 skipped, 2m08.893s framework duration. TRX definitions/results were joined by test ID to confirm the expanded census:

| Class | Passed / executed | TRX p95 / max seconds |
|---|---:|---:|
| RemoteControlMaintenanceQueueTests | 31/31 (29 retained + 2 new) | 1.138 / 11.126 |
| RemoteControlRecoveryPtyIntegrationTests | 9/9 | 5.197 / 5.197 |
| RemoteControlRecoveryTests | 17/17 | 3.845 / 3.845 |
| ClaudeDialogDeliveryTests | 2/2 | 11.792 / 11.792 |

Five controls ran sequentially, one effective environment-selected control per process, in one temporary instrumented build. Each used the exact method filter `/*/*/RemoteControlMaintenanceQueueTests/<method>`; no whole-class mutant run was used. The parameterized method expands to five cases. Every red is an assertion or the intended named timeout, with nonzero execution counts; none is a build, fixture, or discovery failure.

| Control | Exact method | Mutant passed / failed | Intended failure | Restored source |
|---|---|---:|---|---|
| a: refresh after Unknown too | C514_Enter_refresh_preserves_custody_and_ambiguity_barriers | 4 / 1 | `(unknown)` records repeated Enter requests instead of exactly one | 5/5 |
| b: refresh after every Enter | C514_Arm_without_UserPrompt_submits_one_Enter | 0 / 1 | ArmedObserved expected, ArmUnconfirmed observed | 1/1 |
| c: composer from live snapshot, fence from runner | RV514_Unreadable_frame_at_fence_sequence_is_refreshed_not_terminal | 0 / 1 | ArmedObserved expected, ArmUnconfirmed observed | 1/1 |
| c2: composer from runner A, fence from runner B | R605_Unreadable_redraw_after_composer_snapshot_is_refreshed | 0 / 1 | ArmedObserved expected, ArmUnconfirmed observed; closes D-1 | 1/1 |
| t: remove composer-loop time bound | C514_No_composer_evidence_withholds_arm_Enter | 0 / 1 | Named TimeoutException after 30 seconds, task Canceled; TRX duration 30.376s; process exited normally | 1/1 |

The first four failures are ShouldAssertException; t is TimeoutException. Each mutant process returned TUnit exit 2. The gated control driver completed with exit 0 after validating every expected red. Baseline and restored runs use no mutation variable.

Temporary controls were confined to `WaitComposerAndSubmitArmAsync`. The patch is retained in ignored scratch at `.antiphon/c514-9bb67af1/mutations.patch`, SHA256 `d236b4990113c90367f225327a822602a6da5597641752c794fc6e7b5d198bd9`. It selects a, b, c, c2, or t with `ANTIPHON_C514_9BB_MUTATION`. Source was restored before the fresh rebuild: both HEAD and restored working bytes of `server/Application/Services/RemoteControlRecoveryService.cs` have SHA256 `8f359558a64f82482468a6d146d16d4bb8970581863256b2b6f80b07366b19eb`. `git diff --exit-code` for that path passed, and the tracked tree was clean. Restoration receipt: `.antiphon/c514-9bb67af1/restoration.json`.

Build/test execution used server2 Linux, .NET 9.0.22, SDK 10.0.403, `TEMP=/tmp`, `UseAppHost=false`, and `OutputPath=bin-c5149bb/`. Every build/test driver ran through `scripts/build-slot.ps1`; all received a granted lease. The five no-build control processes share their parent's standing lease. The restored method processes likewise share one lease. These are gated direct runs with fresh TRX, not checkpoint-tool receipts, and do not claim a qualified Final/Full Review or whole Unit coverage. Per-test durations and membership are retained in TRX and derived JSON; fixture/discovery/teardown and external-call phase splits not separately instrumented are unknown/remainder.

Reproduce the ordinary selection:

```sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label c514-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c5149bb/ --property:UseAppHost=false --nologo
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label c514-tests -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c5149bb/ --property:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(RemoteControlMaintenanceQueueTests*)|(RemoteControlRecoveryPtyIntegrationTests*)|(RemoteControlRecoveryTests*)|(ClaudeDialogDeliveryTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c514-fresh-results
```

Build/slot accounting (from producer logs): initial build 5m07.03s, wait 16s; mutation build 59.39s, wait 120s; baseline test wait 30s; mutation-driver wait 15s, held 204s. Restored build 2m21.03s, wait 0s; restored-driver wait 0s, held 286s. It passed all nine expanded cases with no failures or skips. The extra graph-tool build was required to perform the requested real evaluation; it took 5.02s after a 120s slot wait.

The real land graph `-Check` returned exit 0 and `approved` after both evaluation passes and the canary. Digest `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`, 68 definition inputs. It waited 135s and held its granted slot 27s. Commands:

```sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label c514-land-graph-build -- dotnet build tools/Antiphon.LandGraphAudit -c Release --property:OutputPath=bin-c5149bb-audit/Release/net10.0/ --property:UseAppHost=false --nologo
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label c514-land-graph-check -- pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin-c5149bb-audit/Release/net10.0/Antiphon.LandGraphAudit.dll
```

Evidence-diff check against the inherited base through tested source 792b37741cde203245ab672f2b959a2b6f2e9853 passed with zero violations. The final caller receipt records the post-report-commit range check too. `git fetch origin master` succeeded (observed origin/master: `31f990317038635c2f1e652b6a950905fcf07a31`). `git merge-tree --write-tree origin/master HEAD` has ten conflict paths; the same check against inherited base e177509681c9ff5b0320f5a32a2e7c7a9d74c57a has exactly the same conflict list. No merge or conflict repair was performed. Use the requested selective rc picks, not the whole branch/master merge; this task does not assert the full branch is landable to master.

A-1 remains note-only: no delayed-composer witness was added, and b2 was not rerun. A-3 remains note-only: a deterministic health-marker witness would require another held-transcript-save scenario rather than a small assertion addition. A-2 is addressed and mutation-proven above.

Native Windows remains OWED: C514 class once after qualification `d47848cc` releases its lock, then the failing case ALONE20 and LOAD10 under recorded load. Keep each failing row's MaintenanceEvidence and input revision. The live qualification and C:\Antiphon were untouched. The whole Unit lane was not run.

Ignored producer evidence root: `/work/worktrees/task-9bb67af1/.antiphon/c514-9bb67af1/`. It contains full build/gate logs, the mutation patch, fresh per-run TRX, timings, restoration receipt, exact output inventory and merge-tree comparisons. Generated payloads remain gitignored; this individual Markdown report is the only evidence file committed. Cleanup: all 37 exact producer-owned `bin-c5149bb` / `bin-c5149bb-audit` roots were removed after every owned run completed. Generated evidence remains in the ignored scratch root; no files outside owned scratch/output paths were removed. No owned run remains in flight. Post-land SourceLanding verification remains a separate obligation at the landed source identity.

--- next stage ---
next: Merge the chain to rc
handoff: Pick b65503281 43a5291f9 c86e6249d 6b16d3394 2bdd51ad9 4dab6ff57 d4a293992 1c0eb2f13 c10010668, then 792b37741cde203245ab672f2b959a2b6f2e9853 and this report commit. Drop the flake-doc hunks as in review 605d42ef. Native rows remain owed; preserve MaintenanceEvidence.
artifact: /work/worktrees/task-9bb67af1/.antiphon/task-9bb67af1.md
