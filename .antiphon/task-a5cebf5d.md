# CARD-0939 Code report — task a5cebf5d

Test-only slices are committed and pushed on `feat/card-task-a5cebf5d`; 59 net runtime results were added, and the original conflict result was strengthened to a genuine two-context EF conflict. Nine planned Linux results require unavailable/out-of-scope seams and remain PENDING. Native Windows V-5 and all 81 SourceLanding PC-n remain assigned to later tasks.

Owner/task: Code a5cebf5d, CARD-0939 / remaining CARD-0883 results. Workspace: `/work/worktrees/task-a5cebf5d`. Start SHA: `4453dab63e5005414b2ded095b674f7b0ef1919e`. Plan: `docs/superpowers/plans/2026-10-01-card-0883-land-half-reset-recovery-plan.md`. No production edit, new migration, application restart, runner restart, deploy, real-provider traffic, or native Windows execution occurred. Scratch production edits were confined to the owned detached disposable checkout and never committed or pushed.

## Changes and census

| Affected class | Measured start results | Final source results | Net added |
|---|---:|---:|---:|
| AgentTaskLandHalfResetTests | 27 | 55 | 28 |
| AgentTaskLandAdoptionConcurrencyTests | 5 | 7 | 2 |
| AgentTaskLandAdoptionTests | 21 | 21 | 0 |
| LandRecoveryCheckoutTests | 17 | 34 | 17 |
| LandingGitTests | 55 | 55 | 0 |
| LandRequestWriteDiagnosticTests | 3 | 15 | 12 |
| AgentTaskLandSourcePersistenceTests | 35 | 35 | 0 |
| AgentTaskLandFailureDiagnosticTests | 26 | 26 | 0 |
| AgentTaskLandMonitoringTests | 25 | 25 | 0 |

The final affected selections total 273 results: CP-2 83, CP-3 89, CP-4 101. This includes 98 of the frozen 107 ordinary V results plus 175 existing regression/extra results. The 59 added executable rows alone do not become 69: the commissioned remainder also included the original synthetic conflict result's missing real-EF requirement, now strengthened without adding a runtime row; 59 additions + 1 completed existing requirement + 9 pending = 69. Subassertions (equivalent duplicate witness, ordinary staged edit, cleanup-only compatibility, symlink preservation) do not increase any count. CARD-0954/0955 additions explain the measured 53/72 start counts rather than the older 51/71 brief counts.

Changed source files are the three affected application test classes, checkout tests, `LandHalfResetFixture.cs`, `LandingSafetyHarness.cs`, and only the plan's Checkpoints/trace section. Existing tests/assertions were retained. Helpers add real adoption source setup, same-scope failure settlement, controllable save/commit boundaries, instance Git injection, durable child-intent observations and synthetic liveness observations; those observations make no real custody-kill calls. PostgreSQL tests use owned cloned databases, with an owned predecessor-migration database for migration coverage. No shared template was altered.

## Pending and structural finding

* V-3 `ProbeErrorsRefuse(ancestry)`: checkout inspection has no ancestry I/O seam. The fresh-request resolver's real non-ancestor refusal is covered by V-1; it cannot stand in for this separate direct-inspector result.
* V-4 `ConflictNamesActualEntityAndTokens(other-entity)`: AgentTask.ConcurrencyToken is required but is not configured as an EF concurrency token in AppDbContext. The actual two-context AgentTask conflict did not occur; the newly attempted row is deferred, not counted, and no artificial test-only model was introduced.
* V-4 `WriterStampComesFromCommittedToken(merge)` and the six `DiagnosticReachesCaller` arguments: reply/Merge/BridgeQueueHarness/notifier/refinement-spill fixtures are CARD-0888's footprint, expressly excluded here. No caller transcript delivery is claimed.
* V-5's four native Windows cases: separate Windows task; not selected on Linux.
* PC-1..PC-81: separate method-scoped SourceLanding Mutation task. The Code sensitivity sample does not discharge them.

CARD-0954 now permits harmless ignored residue. New ignored cases obstruct paths tracked by S; existing benign-residue acceptance tests remain. The deleted-row diagnostic variant actually deletes its owned row: it verifies unknown provenance and the original concurrency classification in logs, then expects the existing LandFailurePersistenceException wrapping the missing-request lookup. It does not claim a persisted terminal row for the deleted entity. The two request conflict variants assert distinct original/attempted/fresh stored tokens, the actual entity/key, safe failure phase, task/request/attempt correlation, and contextual request provenance observed at the execution failure, without claiming to identify the original winning actor.

Filed and confirmed structural defect **CARD-0975**, ID `72c53887-9c66-4a8d-bb87-0b9ef9432300`: “Reviewed adoption can reset after a registered worktree backlink is corrupted.” An owned test fixture at `7ec41e1f` changed the owned worktree admin `gitdir` backlink at the last barrier; later refusal occurred but reset was already recorded. Publication was not established. Exhaustive card searches found no duplicate; related CARD-0642/0688 were inspected. The permitted registration test uses a real `git worktree move` and proves refusal before reset. The corrupt-backlink production repair is outside this test-only commission. The create command's post-create C-drive export failed on Linux; a fresh card get confirmed the one persisted card, so it was not retried.

## Verification procedure

All rows run serially with `TUNIT_MAX_PARALLEL_TESTS=1`, direct `scripts/run-checkpoint.ps1`, granted host build slots, fresh result directories, committed source and literal-pipe filters. CP-2 builds `bin-c939-final/`; CP-3/CP-4 and the final Unit row reuse exactly that output with `-NoBuild`. ExpectedSourceSha is the qualified code SHA recorded below. Tests use real Git in owned repositories; no sleeps or live runner/session fixture is introduced.

Rerun CP-2 with `pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.Tests -OutputPath bin-c939-final/ -Filter '/*/*/(AgentTaskLandHalfResetTests*)|(AgentTaskLandAdoptionConcurrencyTests*)|(AgentTaskLandAdoptionTests*)/*' -MinExecuted 83 -Expect AgentTaskLandHalfResetTests,AgentTaskLandAdoptionConcurrencyTests,AgentTaskLandAdoptionTests -ExpectedSourceSha <current-clean-HEAD>` and set the TUnit environment limit above. Reuse that build for CP-3 (`/*/*/(LandRecoveryCheckoutTests*)|(LandingGitTests*)/*`, Min 89), CP-4 (`/*/*/(LandRequestWriteDiagnosticTests*)|(AgentTaskLandSourcePersistenceTests*)|(AgentTaskLandFailureDiagnosticTests*)|(AgentTaskLandMonitoringTests*)/*`, Min 101), and the whole Unit lane (`/*/*/*/*[Category=Unit]`). Use a fresh ResultsRoot and matching Expect class names for each row.

Earlier implementation verification found and corrected test-only compiler issues (Shouldly overload arguments / expression-tree pattern), fixture diagnostic-code expectations, registration setup, cleanup commands counted as adoption CAS, and unrelated saves counted inside the adoption pair. Failed attempts are retained in `.antiphon/c939-final/` and their logs; they are not green receipts. CP-4 at 97ef60b5 executed 102: 100 passed and the two new rows failed (absent AgentTask EF concurrency and deleted-row persistence wrapper); the former is now PENDING and the latter asserts the actual failure path. No start-ref test/assertion was weakened. The final selected-class runs below supersede those attempts.


## Final verification outcome

Qualified C# code SHA: `f3c11e0a7c6d0cf3f3cd87518355a491a51eac35`. CP-2/CP-3/CP-4 receipts were each accepted by `scripts/validate-checkpoint-receipt.ps1` for that SHA. The final reporting commit changes only the measured plan trace and this report; it does not change the qualified C# source.

* c939-final-cp2-r6.log: 83 executed, 83 passed, 0 failed, 0 skipped.
* c939-final-cp3.log: 89 executed, 89 passed, 0 failed, 0 skipped.
* c939-final-cp4.log: 101 executed, 101 passed, 0 failed, 0 skipped.
* c939-unit.log: 3733 executed, 3733 passed, 0 failed, 40 skipped.

The exact filters and unedited receipt lines appear below. The report uses the checkpoint driver's counters, not a recomputed source/TRX count. Every in-scope C883 method/argument identity was independently matched to a Passed result in the final CP-2/CP-3/CP-4 TRX files; Windows was excluded. This checks identities without re-deriving checkpoint counters. The match record is .antiphon/c939-roster-check.txt.



## Whole Unit lane

One normal Unit run at `f3c11e0a7c6d0cf3f3cd87518355a491a51eac35`: **3,733 executed / 3,733 passed / 0 failed / 40 skipped**, exit 0; granted build slot and a receipt reporting clean/verified source. The strict receipt validator requires zero skipped results and returned exit 2: `CHECKPOINT SOURCE INVALID reason=receipt_failed`. Its source check passed before the skipped-count rejection; this Unit receipt is not claimed as zero-skip qualified. No whole-lane rerun was performed.

All skips are pre-existing host/platform/dependency gates, including Windows npm-shim/path/ConPTY/locking rows and CARD-0912 cold-seed shell rows requiring jq absent from PATH. No skip was added to this task's affected landing classes. The brief's known-flake list (CARD-0751/0742/0820/0757/0882/0889/0890/0900/0917 and checkpoint-test-root-busy) produced no Unit failures in this run. CARD-0966's StandingSessionOwnershipTests are Category=Integration, outside the Unit filter and this task's affected integration selection; their inherited four Linux failures were not rerun or claimed green.

Existing skips recorded by the Unit run:

| Test result | Recorded reason |
|---|---|
| The_shipped_codex_definition_resolves_to_a_real_executable_on_this_machine | The npm shim layout is Windows-specific |
| Resolves_sibling_flavor_when_configured_one_is_gone | Sibling executable flavors (.exe/.cmd/.bat) are resolved only on Windows |
| V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash | Canonical pin cwd is a Windows drive-rooted path |
| Off_settings_path_round_trips_through_LaunchArgvGuard | CommandLineToArgvW is Windows-only |
| reported_repository_paths_normalize_relative_and_absolute_windows_forms | Drive-letter report paths resolve only against a Windows repo root |
| partial_leaf_matches_substring_within_child_name | Drive-letter directory listing needs a Windows MockFileSystem |
| trailing_slash_lists_children_of_that_directory | Drive-letter directory listing needs a Windows MockFileSystem |
| prefix_returns_matching_child_directories | Drive-letter directory listing needs a Windows MockFileSystem |
| existing_path_reports_exists_true | Drive-letter directory listing needs a Windows MockFileSystem |
| caches_within_ttl_and_refreshes_after | Drive-letter directory listing needs a Windows MockFileSystem |
| Unsafe_raw_rules_are_refused_server_side_before_runner_calls(duplicate_second) | CARD-0382 Grok rules argv guard is Windows-only by design |
| Unsafe_raw_rules_are_refused_server_side_before_runner_calls(env_flag) | CARD-0382 Grok rules argv guard is Windows-only by design |
| Unsafe_raw_rules_are_refused_server_side_before_runner_calls(env) | CARD-0382 Grok rules argv guard is Windows-only by design |
| Unsafe_raw_rules_are_refused_server_side_before_runner_calls(braced_env) | CARD-0382 Grok rules argv guard is Windows-only by design |
| Unsafe_raw_rules_are_refused_server_side_before_runner_calls(duplicate_first) | CARD-0382 Grok rules argv guard is Windows-only by design |
| Unsafe_raw_rules_are_refused_server_side_before_runner_calls(missing) | CARD-0382 Grok rules argv guard is Windows-only by design |
| Unsafe_raw_rules_are_refused_server_side_before_runner_calls(equals) | CARD-0382 Grok rules argv guard is Windows-only by design |
| Unsafe_raw_rules_are_refused_server_side_before_runner_calls(alias) | CARD-0382 Grok rules argv guard is Windows-only by design |
| Unsafe_raw_rules_are_refused_server_side_before_runner_calls(nul) | CARD-0382 Grok rules argv guard is Windows-only by design |
| Unsafe_raw_rules_are_refused_server_side_before_runner_calls(crlf) | CARD-0382 Grok rules argv guard is Windows-only by design |
| Unsafe_raw_rules_are_refused_server_side_before_runner_calls(lf) | CARD-0382 Grok rules argv guard is Windows-only by design |
| Unsafe_raw_rules_are_refused_server_side_before_runner_calls(cr) | CARD-0382 Grok rules argv guard is Windows-only by design |
| A_silent_runner_leaves_this_processes_own_decision_standing | no shipped conpty.dll: not Windows — there is no pseudoconsole to redirect |
| A_runner_on_the_inbox_conhost_downgrades_a_modern_server | no shipped conpty.dll: not Windows — there is no pseudoconsole to redirect |
| A_runner_on_the_modern_backend_confirms_the_raised_ceilings | no shipped conpty.dll: not Windows — there is no pseudoconsole to redirect |
| Phone_home_Claude_keeps_the_inbox_ceiling_for_its_own_kind | ConPTY only on Windows |
| Phone_home_Grok_never_uses_local_modern_evidence | ConPTY only on Windows |
| windows_row_timeout_kills_the_start_b_grandchild | The descendant sweep kills a start /b grandchild on Windows. |
| windows_quick_row_finishes_beside_a_slow_row | Concurrent Windows row processes must not inherit each other's stdout pipes. |
| windows_row_arguments_round_trip_intact | Windows row arguments are delivered by ProcessStartInfo.ArgumentList. |
| windows_chatty_row_drains_interleaved_stdout_and_stderr | Windows row pipes must drain a large interleaved stdout and stderr. |
| C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded | Sharing-mode locks are a Windows file-system behaviour. |
| C665_LockedFileMidDeleteResumesOnLaterPass | Sharing-mode locks are a Windows file-system behaviour. |
| C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries | CARD-0912: jq is not on the Linux shell PATH; install jq in the runner or WSL to run C912 cold-seed tests. |
| C912_Cold_marker_has_distinct_validation_and_full_context_refusal | CARD-0912: jq is not on the Linux shell PATH; install jq in the runner or WSL to run C912 cold-seed tests. |
| C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit | CARD-0912: jq is not on the Linux shell PATH; install jq in the runner or WSL to run C912 cold-seed tests. |
| C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets | CARD-0912: jq is not on the Linux shell PATH; install jq in the runner or WSL to run C912 cold-seed tests. |
| C912_Cold_volumes_seed_accepts_busy_main_without_packages | CARD-0912: jq is not on the Linux shell PATH; install jq in the runner or WSL to run C912 cold-seed tests. |
| C912_Cold_seed_refuses_when_created_volume_disappears_before_init | CARD-0912: jq is not on the Linux shell PATH; install jq in the runner or WSL to run C912 cold-seed tests. |
| C912_Cold_seed_with_unrelated_bind_initializes_only_three_labelled_roots | CARD-0912: jq is not on the Linux shell PATH; install jq in the runner or WSL to run C912 cold-seed tests. |

The unedited checkpoint receipt and fresh TRX path are preserved below.


## Code sensitivity proof

23 new argument results produced intended assertion failures across V-2, V-1, V-4, V-3. Each is listed below, with the compiling scratch mutation and actual failure assertion. Compiler/setup errors, timeouts, old rows and any secondary assertion/formatting failures are not counted. The pending-admission and restart sample rows hit a mutation-induced DbUpdateException instead of the expected ConflictException; those two rows are excluded from the 23-result sensitivity proof. The full affected class green runs above are the unmutated baseline; the restored scratch selection is independently green.

| New test result | Compiling mutation | Intended assertion observed RED | Restored GREEN |
|---|---|---|---|
| C883_HeldWriterBetweenMoveAndResetDoesNotLoseHoldFacts (tests/Antiphon.Tests/Application/AgentTaskLandAdoptionConcurrencyTests.cs:260) | Zero HoldEpisode at the source checkpoint | acknowledged.HoldEpisode should be 1 but was 0 Additional Info: V2.HoldEpisodeRetainedAcrossReset | Yes |
| C883_ContentChangesBeforeResetRefuse(ignored) (tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:376) | Return Accepted=true from the checkout Refused factory | (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId)).SourceRefusalReason should be "source_dirty" but was null Additional Info: H.LateIgnoredObstructionRefused | Yes |
| C883_FreshRequestRejectsWitnessIdentity(source-identity) (tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:534) | Remove witness RecoverySourceTaskId binding | row.SourceRefusalReason should be "source_dirty" but was null Additional Info: H.source-identity.WitnessBindingRefused | Yes |
| C883_FreshRequestRequiresAncestor (tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:601) | Bypass fresh-request merge-base refusal | row.SourceRefusalReason should be "source_dirty" but was null Additional Info: H.NonAncestorRefused | Yes |
| C883_WriterStampComesFromCommittedToken(protocol) (tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:28) | Stamp Guid.Empty for source/protocol LastWriterToken | row.LastWriterToken should be 06f784a5-4aeb-4807-8430-0c3a4d8029b0 but was 00000000-0000-0000-0000-000000000000 Additional Info: D.source-checkpoint.CommitTokenLinked | Yes |
| C883_WriterStampComesFromCommittedToken(source) (tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:28) | Stamp Guid.Empty for source/protocol LastWriterToken | row.LastWriterToken should be f3b44d33-17d6-426f-85d4-a29df43bdea7 but was 00000000-0000-0000-0000-000000000000 Additional Info: D.source-checkpoint.CommitTokenLinked | Yes |
| C883_SecretPayloadIsAbsent (tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:258) | Append the hostile synthetic exception message to the concurrency summary | log.Message should not contain (case insensitive comparison) "C939_PRIVATE_SQL_password_and_worktree_payload" but was actually "Land operation failed for task 8cb85668-62a3-4b45-960e-255e51d635fd request 8e6be43b-7c27-412f-97f9-..." Additional Info: D.SafeLogMessage | Yes |
| C883_BinaryEolAndModeComparison(binary) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:294) | Return Accepted=true from the checkout Refused factory | edited.Accepted should be False but was True Additional Info: G.binary.RawEditRefused | Yes |
| C883_IdentityResampleRefusesChange (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:269) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.FinalIdentityChangeRefused | Yes |
| C883_SubmoduleAndFilterRefuse(ident) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:246) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.ident.UnsupportedTransformRefused | Yes |
| C883_SubmoduleAndFilterRefuse(working-tree-encoding) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:246) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.working-tree-encoding.UnsupportedTransformRefused | Yes |
| C883_SubmoduleAndFilterRefuse(custom-filter) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:246) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.custom-filter.UnsupportedTransformRefused | Yes |
| C883_SubmoduleAndFilterRefuse(gitlink) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:246) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.gitlink.UnsupportedTransformRefused | Yes |
| C883_SequencerRefuses(sequencer-directory) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:224) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.SequencerRecoveryRefused | Yes |
| C883_SequencerRefuses(cherry-pick) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:224) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.SequencerRecoveryRefused | Yes |
| C883_SequencerRefuses(rebase) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:224) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.SequencerRecoveryRefused | Yes |
| C883_SequencerRefuses(merge) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:224) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.SequencerRecoveryRefused | Yes |
| C883_ProbeErrorsRefuse(byte-io) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:182) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.byte-io.FailureRefused | Yes |
| C883_UnsafeIndexModesRefuse(sparse) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:151) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.sparse.UnsafeIndexRefused | Yes |
| C883_UnsafeIndexModesRefuse(unmerged) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:151) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.unmerged.UnsafeIndexRefused | Yes |
| C883_BinaryEolAndModeComparison(eol) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:294) | Return Accepted=true from the checkout Refused factory | edited.Accepted should be False but was True Additional Info: G.eol.RawEditRefused | Yes |
| C883_ProbeErrorsRefuse(blob-read) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:182) | Return Accepted=true from the checkout Refused factory | proof.Accepted should be False but was True Additional Info: G.blob-read.FailureRefused | Yes |
| C883_BinaryEolAndModeComparison(mode) (tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:294) | Return Accepted=true from the checkout Refused factory | edited.Accepted should be False but was True Additional Info: G.mode.RawEditRefused | Yes |

The aggregate 51-result Code sample is explicitly commissioned by the brief's “prove red once for a sample of at least 10” requirement. Its build/red/restored-green rows are additional drivers for that purpose only; no PC-n baseline/red/restore qualification was run. Scratch path: `/tmp/c939-sensitivity.LA9Zj4/checkout`, detached at the qualified code SHA. Mutations were restored from that exact HEAD before the restored build; `git diff --exit-code`, `git diff --cached --exit-code`, and `git status --short --untracked-files=all` were empty. Owned isolated outputs and the disposable checkout were removed after verification; production files in the assigned worktree never changed.

Scratch patch (uncommitted, never pushed):

```diff
diff --git a/server/Application/Dtos/LandRecoveryCheckoutInspection.cs b/server/Application/Dtos/LandRecoveryCheckoutInspection.cs
index fe019234..e65f3404 100644
--- a/server/Application/Dtos/LandRecoveryCheckoutInspection.cs
+++ b/server/Application/Dtos/LandRecoveryCheckoutInspection.cs
@@ -3,5 +3,5 @@ namespace Antiphon.Server.Application.Dtos;
 /// <summary>Proof that an interrupted adoption still has the pinned old checkout.</summary>
 public sealed record LandRecoveryCheckoutInspection(bool Accepted, string? Reason = null)
 {
-    public static LandRecoveryCheckoutInspection Refused(string reason = "recovery_checkout_unproven") => new(false, reason);
+    public static LandRecoveryCheckoutInspection Refused(string reason = "recovery_checkout_unproven") => new(true, reason);
 }
diff --git a/server/Application/Services/AgentTaskLandRequestWriter.cs b/server/Application/Services/AgentTaskLandRequestWriter.cs
index 28feb445..1d34cf34 100644
--- a/server/Application/Services/AgentTaskLandRequestWriter.cs
+++ b/server/Application/Services/AgentTaskLandRequestWriter.cs
@@ -157,6 +157,7 @@ internal sealed class AgentTaskLandRequestWriter(AppDbContext db, TimeProvider c

         var adoptedNow = request.RecoveryAdoptedAt is null && patch.RecoveryAdoptedAt is not null;
         ApplyPatch(request, patch);
+        if (request.HoldReasonCode == "repository_mutation_lease_busy") request.HoldEpisode = 0;
         LandRequestWriteProvenance.Stamp(request, "source-checkpoint", clock);
         var now = clock.GetUtcNow().UtcDateTime;
         if (adoptedNow)
diff --git a/server/Application/Services/AgentTaskLandService.cs b/server/Application/Services/AgentTaskLandService.cs
index ad4bec04..f77a4a6d 100644
--- a/server/Application/Services/AgentTaskLandService.cs
+++ b/server/Application/Services/AgentTaskLandService.cs
@@ -144,7 +144,7 @@ public sealed class AgentTaskLandService
             var pending = request is { IsPending: true } && task.LandRequestedAt is not null;
             var supersede = pending && request!.State == LandRequestState.NeedsResolution
                 && recoveryMode != LandRecoveryMode.None;
-            if (pending && !supersede)
+            if (false && pending && !supersede)
             {
                 if (recoveryMode != LandRecoveryMode.None || body.AdoptFromTaskId is not null)
                     throw new ConflictException("A live land request cannot change recovery authority.",
diff --git a/server/Application/Services/AgentTaskLandSourceResolver.cs b/server/Application/Services/AgentTaskLandSourceResolver.cs
index d4828ca4..733501c0 100644
--- a/server/Application/Services/AgentTaskLandSourceResolver.cs
+++ b/server/Application/Services/AgentTaskLandSourceResolver.cs
@@ -307,7 +307,7 @@ public sealed class AgentTaskLandSourceResolver(
                         expected, ownerObserved.Sha, expected, ct);
                 var ancestry = await git.RunAsync(repository,
                     ["merge-base", "--is-ancestor", witness.LocalBeforeSha, expected], ct);
-                if (!ancestry.Succeeded)
+                if (false)
                     return await RefuseAsync(task, request, baseline, "source_dirty",
                         expected, ownerObserved.Sha, expected, ct, detail: "recovery_checkout_unproven");
                 request.RecoveryLocalBeforeSha = witness.LocalBeforeSha;
diff --git a/server/Application/Services/LandFailureDiagnostic.cs b/server/Application/Services/LandFailureDiagnostic.cs
index c0b1884f..c46bc374 100644
--- a/server/Application/Services/LandFailureDiagnostic.cs
+++ b/server/Application/Services/LandFailureDiagnostic.cs
@@ -52,7 +52,7 @@ internal static class LandFailureDiagnostic
             }
             catch (Exception) { parts.Add($"entity={entity}; row=unknown"); }
         }
-        return parts.Count == 0 ? "entity=unknown" : string.Join(" | ", parts);
+        return (parts.Count == 0 ? "entity=unknown" : string.Join(" | ", parts)) + exception.Message;
     }

     public static string ObservedDatabaseWriter(AgentTaskLandRequest? row, bool unavailable = false)
diff --git a/server/Application/Services/LandRecoveryWitness.cs b/server/Application/Services/LandRecoveryWitness.cs
index 19c1e3ff..37c6292e 100644
--- a/server/Application/Services/LandRecoveryWitness.cs
+++ b/server/Application/Services/LandRecoveryWitness.cs
@@ -23,7 +23,6 @@ internal static class LandRecoveryWitnessFinder
         foreach (var old in candidates)
         {
             if (old.RecoveryMode != current.RecoveryMode || old.RecoveryMode == LandRecoveryMode.None
-                || old.RecoverySourceTaskId != current.RecoverySourceTaskId
                 || old.RecoverySourceFullRef != current.RecoverySourceFullRef
                 || old.RecoverySourceFingerprint != fingerprint
                 || old.SourceFullRefSnapshot != coordinates.SourceFullRef
diff --git a/server/Application/Services/LandRequestWriteProvenance.cs b/server/Application/Services/LandRequestWriteProvenance.cs
index b9ae4d81..d08168d6 100644
--- a/server/Application/Services/LandRequestWriteProvenance.cs
+++ b/server/Application/Services/LandRequestWriteProvenance.cs
@@ -10,7 +10,7 @@ internal static class LandRequestWriteProvenance
             throw new ArgumentException("Invalid land request writer label", nameof(operation));
         var token = Guid.NewGuid();
         request.ConcurrencyToken = token;
-        request.LastWriterToken = token;
+        request.LastWriterToken = operation is "source-checkpoint" or "protocol-progress" ? Guid.Empty : token;
         request.LastWriterOperation = operation;
         request.LastWriterAt = clock.GetUtcNow().UtcDateTime;
     }
```



## Requirement trace

Each argument is one frozen planned result. “Existing” includes strengthened setup/assertions; “new” is a net added runtime row. Mutation proposals below describe how the existing behavior can go red; only the sampled rows claim an executed red proof. No pure characterization/stub is counted. All 81 PC-n remain PENDING: this sample is the separately requested Code sensitivity check, not their method-scoped baseline/red/restore qualification.

### V-1

| Planned result / exact test | File:line | Executed | Added or strengthened | Can-go-red evidence / pending reason |
|---|---|---|---|---|
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRepairsPinnedAncestor(OwnerReviewedSource) [Arguments(false)] | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:218 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Remove the matching acceptance/preservation guard; require the named assertion to fail.. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRepairsPinnedAncestor(AdoptReviewedSource) [Arguments(true)] | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:218 | Yes; full affected class | New runtime row | Mutation proposal (not run): Remove the matching acceptance/preservation guard; require the named assertion to fail.. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRequiresInterruptedWitness(absent) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:335 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): When no candidate exists, fabricate L from the matching ancestor. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRequiresInterruptedWitness(not-started) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:335 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Ignore only witness SourceResolutionState. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRequiresInterruptedWitness(wrong-operation) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:335 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Ignore only source-adopt-reset match. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRequiresInterruptedWitness(already-adopted) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:335 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Ignore only RecoveryAdoptedAt. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRejectsWitnessIdentity(owner) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:534 | Yes; full affected class | New runtime row | Mutation proposal (not run): Drop owner comparison in witness eligibility. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRejectsWitnessIdentity(ref) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:534 | Yes; full affected class | New runtime row | Mutation proposal (not run): Drop witness ref comparison. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRejectsWitnessIdentity(worktree) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:534 | Yes; full affected class | New runtime row | Mutation proposal (not run): Drop witness worktree comparison. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRejectsWitnessIdentity(sha) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:534 | Yes; full affected class | New runtime row | Mutation proposal (not run): Drop witness ExpectedSourceSha comparison. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRejectsWitnessIdentity(fingerprint) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:534 | Yes; full affected class | New runtime row | Mutation proposal (not run): Drop witness fingerprint comparison. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRejectsWitnessIdentity(common-directory) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:534 | Yes; full affected class | New runtime row | Mutation proposal (not run): Drop witness common-directory comparison. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRejectsWitnessIdentity(source-identity) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:534 | Yes; full affected class | New runtime row | Proved RED: Remove witness RecoverySourceTaskId binding; row.SourceRefusalReason should be "source_dirty" but was null Additional Info: H.source-identity.WitnessBindingRefused. Restored sample GREEN. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRejectsWitnessIdentity(local-pin) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:534 | Yes; full affected class | New runtime row | Mutation proposal (not run): Ignore the request-owned local-before pin mismatch. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRejectsWitnessIdentity(source-pin) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:534 | Yes; full affected class | New runtime row | Mutation proposal (not run): Ignore the request-owned source pin mismatch. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRejectsWitnessIdentity(ambiguous) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:534 | Yes; full affected class | New runtime row | Mutation proposal (not run): Select the first conflicting valid witness instead of refusing. |
| V-1 AgentTaskLandHalfResetTests.C883_UnstagedEditSurvivesFreshAndSameRequest(fresh) [Arguments(false)] | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:281 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Remove the matching acceptance/preservation guard; require the named assertion to fail.. |
| V-1 AgentTaskLandHalfResetTests.C883_UnstagedEditSurvivesFreshAndSameRequest(same) [Arguments(true)] | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:281 | Yes; full affected class | New runtime row | Mutation proposal (not run): Remove the matching acceptance/preservation guard; require the named assertion to fail.. |
| V-1 AgentTaskLandHalfResetTests.C883_StagedEditSurvivesFreshAndSameRequest(fresh) [Arguments(false)] | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:253 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Omit cached-index difference rejection. |
| V-1 AgentTaskLandHalfResetTests.C883_StagedEditSurvivesFreshAndSameRequest(same) [Arguments(true)] | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:253 | Yes; full affected class | New runtime row | Mutation proposal (not run): Omit cached-index difference rejection. |
| V-1 AgentTaskLandHalfResetTests.C883_UntrackedAndIgnoredArePreserved(untracked) [Arguments(false)] | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:303 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Remove the matching acceptance/preservation guard; require the named assertion to fail.. |
| V-1 AgentTaskLandHalfResetTests.C883_UntrackedAndIgnoredArePreserved(ignored) [Arguments(true)] | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:303 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Remove the matching acceptance/preservation guard; require the named assertion to fail.. |
| V-1 AgentTaskLandHalfResetTests.C883_FreshRequestRequiresAncestor(none) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:601 | Yes; full affected class | New runtime row | Proved RED: Bypass fresh-request merge-base refusal; row.SourceRefusalReason should be "source_dirty" but was null Additional Info: H.NonAncestorRefused. Restored sample GREEN. |
| V-1 AgentTaskLandHalfResetTests.C883_IdentityChangesBeforeResetRefuse(head) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:630 | Yes; full affected class | New runtime row | Mutation proposal (not run): Omit final live identity resample (HEAD argument). |
| V-1 AgentTaskLandHalfResetTests.C883_IdentityChangesBeforeResetRefuse(branch) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:630 | Yes; full affected class | New runtime row | Mutation proposal (not run): Ignore final symbolic-ref mismatch. |
| V-1 AgentTaskLandHalfResetTests.C883_IdentityChangesBeforeResetRefuse(registration) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:630 | Yes; full affected class | New runtime row | Mutation proposal (not run): Ignore final registration mismatch. |
| V-1 AgentTaskLandHalfResetTests.C883_AuthorityChangesBeforeResetRefuse(review-sha) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:444 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Skip final LoadRecoveryEvidenceAsync SHA validation only. |
| V-1 AgentTaskLandHalfResetTests.C883_AuthorityChangesBeforeResetRefuse(review-clean-false) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:444 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Accept ReviewedSourceClean!=true at final check. |
| V-1 AgentTaskLandHalfResetTests.C883_AuthorityChangesBeforeResetRefuse(review-clean-null) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:444 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Accept ReviewedSourceClean!=true at final check. |
| V-1 AgentTaskLandHalfResetTests.C883_AuthorityChangesBeforeResetRefuse(review-superseded) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:444 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Skip final superseded-evidence query. |
| V-1 AgentTaskLandHalfResetTests.C883_AuthorityChangesBeforeResetRefuse(remote) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:444 | Yes; full affected class | New runtime row | Mutation proposal (not run): Accept changed source tip at final recheck. |
| V-1 AgentTaskLandHalfResetTests.C883_AuthorityChangesBeforeResetRefuse(fingerprint) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:444 | Yes; full affected class | New runtime row | Mutation proposal (not run): Accept changed fingerprint at final recheck. |
| V-1 AgentTaskLandHalfResetTests.C883_AuthorityChangesBeforeResetRefuse(owner) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:444 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Skip final owner-status recheck. |
| V-1 AgentTaskLandHalfResetTests.C883_UncertainPriorChildRefuses(live-request) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:821 | Yes; full affected class | New runtime row | Mutation proposal (not run): Treat live/unknown predecessor PID observations as dead. |
| V-1 AgentTaskLandHalfResetTests.C883_UncertainPriorChildRefuses(unknown-request) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:821 | Yes; full affected class | New runtime row | Mutation proposal (not run): Treat live/unknown predecessor PID observations as dead. |
| V-1 AgentTaskLandHalfResetTests.C883_UncertainPriorChildRefuses(live-journal) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:821 | Yes; full affected class | New runtime row | Mutation proposal (not run): Ignore live/unknown unfinished child journal when acquiring. |
| V-1 AgentTaskLandHalfResetTests.C883_UncertainPriorChildRefuses(unknown-journal) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:821 | Yes; full affected class | New runtime row | Mutation proposal (not run): Ignore live/unknown unfinished child journal when acquiring. |
| V-1 AgentTaskLandHalfResetTests.C883_ContentChangesBeforeResetRefuse(index) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:376 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Reuse first content proof rather than resampling at reset. |
| V-1 AgentTaskLandHalfResetTests.C883_ContentChangesBeforeResetRefuse(tracked) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:376 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Reuse first content proof rather than resampling at reset. |
| V-1 AgentTaskLandHalfResetTests.C883_ContentChangesBeforeResetRefuse(untracked) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:376 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Reuse first content proof rather than resampling at reset. |
| V-1 AgentTaskLandHalfResetTests.C883_ContentChangesBeforeResetRefuse(ignored) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:376 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == next.RequestId)).SourceRefusalReason should be "source_dirty" but was null Additional Info: H.LateIgnoredObstructionRefused. Restored sample GREEN. |
| V-1 AgentTaskLandHalfResetTests.C883_ContentChangesBeforeResetRefuse(local-pin) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:376 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Reuse initial L/S pins at reset. |
| V-1 AgentTaskLandHalfResetTests.C883_ContentChangesBeforeResetRefuse(source-pin) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:376 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Reuse initial L/S pins at reset. |
| V-1 AgentTaskLandHalfResetTests.C883_PostResetMismatchCannotPublish(dirty) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:672 | Yes; full affected class | New runtime row | Mutation proposal (not run): Acknowledge adoption without final clean-S inspection. |
| V-1 AgentTaskLandHalfResetTests.C883_PostResetMismatchCannotPublish(wrong-head) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:672 | Yes; full affected class | New runtime row | Mutation proposal (not run): Acknowledge adoption without final clean-S inspection. |
| V-1 AgentTaskLandHalfResetTests.C883_CurrentPendingRequestCannotBeStolen(none) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:701 | Yes; full affected class | New runtime row | Mutation proposal (not run): Allow RequestAsync to replace a running recovery request. |
| V-1 AgentTaskLandHalfResetTests.C883_CompletedResetIsIdempotent(none) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs:723 | Yes; full affected class | New runtime row | Mutation proposal (not run): Always hard-reset even when S already aligned. |

### V-2

| Planned result / exact test | File:line | Executed | Added or strengthened | Can-go-red evidence / pending reason |
|---|---|---|---|---|
| V-2 AgentTaskLandAdoptionConcurrencyTests.C883_Save409FaultThenFreshRequestCompletes(none) | tests/Antiphon.Tests/Application/AgentTaskLandAdoptionConcurrencyTests.cs:18 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Reinstate requirement for the fresh request's own source-adopt-reset field. |
| V-2 AgentTaskLandAdoptionConcurrencyTests.C883_PreIntentConflictLeavesCheckoutUnmoved(none) | tests/Antiphon.Tests/Application/AgentTaskLandAdoptionConcurrencyTests.cs:125 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Remove expected-old operand from adoption update-ref. |
| V-2 AgentTaskLandAdoptionConcurrencyTests.C883_MonitorBetweenMoveAndResetDoesNotPreventReset(none) | tests/Antiphon.Tests/Application/AgentTaskLandAdoptionConcurrencyTests.cs:56 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Bypass journal Begin for adoption RunAsync mutations. |
| V-2 AgentTaskLandAdoptionConcurrencyTests.C883_HeldWriterBetweenMoveAndResetDoesNotLoseHoldFacts(none) | tests/Antiphon.Tests/Application/AgentTaskLandAdoptionConcurrencyTests.cs:260 | Yes; full affected class | New runtime row | Proved RED: Zero HoldEpisode at the source checkpoint; acknowledged.HoldEpisode should be 1 but was 0 Additional Info: V2.HoldEpisodeRetainedAcrossReset. Restored sample GREEN. |
| V-2 AgentTaskLandAdoptionConcurrencyTests.C883_PostResetSaveConflictFreshRequestCompletes(none) | tests/Antiphon.Tests/Application/AgentTaskLandAdoptionConcurrencyTests.cs:160 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Remove the matching acceptance/preservation guard; require the named assertion to fail.. |
| V-2 AgentTaskLandAdoptionConcurrencyTests.C883_RestartAfterMoveResumesPendingBeforeFreshAdmission(none) | tests/Antiphon.Tests/Application/AgentTaskLandAdoptionConcurrencyTests.cs:303 | Yes; full affected class | New runtime row | Mutation proposal (not run): Remove the matching acceptance/preservation guard; require the named assertion to fail.. |
| V-2 AgentTaskLandAdoptionConcurrencyTests.C883_HistoricalMonitorSaveConflictThenFreshRequestCompletes(none) | tests/Antiphon.Tests/Application/AgentTaskLandAdoptionConcurrencyTests.cs:205 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Remove the matching acceptance/preservation guard; require the named assertion to fail.. |

### V-3

| Planned result / exact test | File:line | Executed | Added or strengthened | Can-go-red evidence / pending reason |
|---|---|---|---|---|
| V-3 LandRecoveryCheckoutTests.C883_EqualIndexAndWorktreeAccepted(none) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:53 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Remove the matching acceptance/preservation guard; require the named assertion to fail.. |
| V-3 LandRecoveryCheckoutTests.C883_IndexOnlyDifferenceRefused(none) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:65 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Remove the matching acceptance/preservation guard; require the named assertion to fail.. |
| V-3 LandRecoveryCheckoutTests.C883_WorktreeOnlyDifferenceRefused(none) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:82 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Remove the matching acceptance/preservation guard; require the named assertion to fail.. |
| V-3 LandRecoveryCheckoutTests.C883_UntrackedAndIgnoredRefused(untracked) [Arguments(false)] | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:96 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Ignore nonempty ordinary untracked output. |
| V-3 LandRecoveryCheckoutTests.C883_UntrackedAndIgnoredRefused(ignored) [Arguments(true)] | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:96 | Yes; full affected class | New runtime row | Mutation proposal (not run): Ignore nonempty ignored output. |
| V-3 LandRecoveryCheckoutTests.C883_UnsafeIndexModesRefuse(unmerged) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:151 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.unmerged.UnsafeIndexRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_UnsafeIndexModesRefuse(assume-unchanged) [Arguments(assume)] | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:151 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Remove assume-unchanged flag refusal. |
| V-3 LandRecoveryCheckoutTests.C883_UnsafeIndexModesRefuse(skip-worktree) [Arguments(skip)] | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:151 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Remove skip-worktree flag refusal. |
| V-3 LandRecoveryCheckoutTests.C883_UnsafeIndexModesRefuse(sparse) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:151 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.sparse.UnsafeIndexRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_SequencerRefuses(merge) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:224 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.SequencerRecoveryRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_SequencerRefuses(rebase) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:224 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.SequencerRecoveryRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_SequencerRefuses(cherry-pick) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:224 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.SequencerRecoveryRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_SequencerRefuses(sequencer-directory) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:224 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.SequencerRecoveryRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_SubmoduleAndFilterRefuse(gitlink) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:246 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.gitlink.UnsupportedTransformRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_SubmoduleAndFilterRefuse(custom-filter) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:246 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.custom-filter.UnsupportedTransformRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_SubmoduleAndFilterRefuse(working-tree-encoding) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:246 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.working-tree-encoding.UnsupportedTransformRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_SubmoduleAndFilterRefuse(ident) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:246 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.ident.UnsupportedTransformRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_ProbeErrorsRefuse(cached-diff) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:182 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Treat cached-diff exit 128 as empty. |
| V-3 LandRecoveryCheckoutTests.C883_ProbeErrorsRefuse(worktree-diff) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:182 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Treat worktree-diff exit 128 as equality. |
| V-3 LandRecoveryCheckoutTests.C883_ProbeErrorsRefuse(untracked) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:182 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Treat untracked exit 128 as empty. |
| V-3 LandRecoveryCheckoutTests.C883_ProbeErrorsRefuse(ignored) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:182 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Treat ignored exit 128 as empty. |
| V-3 LandRecoveryCheckoutTests.C883_ProbeErrorsRefuse(ancestry) | Not authored in scope | No | Pending | PENDING — Production checkout inspector has no ancestry I/O seam; resolver ancestry is tested separately in V-1. |
| V-3 LandRecoveryCheckoutTests.C883_ProbeErrorsRefuse(index-flags) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:182 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Treat malformed index flag response as no flags. |
| V-3 LandRecoveryCheckoutTests.C883_ProbeErrorsRefuse(attributes) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:182 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Treat failed attribute lookup as no attributes. |
| V-3 LandRecoveryCheckoutTests.C883_ProbeErrorsRefuse(blob-read) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:182 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.blob-read.FailureRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_ProbeErrorsRefuse(byte-io) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:182 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.byte-io.FailureRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_IdentityResampleRefusesChange(none) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:269 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; proof.Accepted should be False but was True Additional Info: G.FinalIdentityChangeRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_BinaryEolAndModeComparison(binary) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:294 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; edited.Accepted should be False but was True Additional Info: G.binary.RawEditRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_BinaryEolAndModeComparison(eol) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:294 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; edited.Accepted should be False but was True Additional Info: G.eol.RawEditRefused. Restored sample GREEN. |
| V-3 LandRecoveryCheckoutTests.C883_BinaryEolAndModeComparison(mode) | tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs:294 | Yes; full affected class | New runtime row | Proved RED: Return Accepted=true from the checkout Refused factory; edited.Accepted should be False but was True Additional Info: G.mode.RawEditRefused. Restored sample GREEN. |

### V-4

| Planned result / exact test | File:line | Executed | Added or strengthened | Can-go-red evidence / pending reason |
|---|---|---|---|---|
| V-4 LandRequestWriteDiagnosticTests.C883_ConflictNamesActualEntityAndTokens(request-direct) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:79 | Yes; full affected class | Existing row; real two-context conflict strengthened | Mutation proposal (not run): Move safe entry capture after Clear and derive it from tracker. |
| V-4 LandRequestWriteDiagnosticTests.C883_ConflictNamesActualEntityAndTokens(request-hosted) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:79 | Yes; full affected class | New runtime row | Mutation proposal (not run): Move safe entry capture after Clear and derive it from tracker. |
| V-4 LandRequestWriteDiagnosticTests.C883_ConflictNamesActualEntityAndTokens(other-entity) | Not authored in scope | No | Pending | PENDING — AgentTask.ConcurrencyToken is not configured as an EF concurrency token; actual two-context AgentTask conflict is unavailable without a production/model change. |
| V-4 LandRequestWriteDiagnosticTests.C883_WriterStampComesFromCommittedToken(monitor) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:28 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Set LastWriterToken to the old concurrency token. |
| V-4 LandRequestWriteDiagnosticTests.C883_WriterStampComesFromCommittedToken(hold) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:28 | Yes; full affected class | New runtime row | Mutation proposal (not run): Set LastWriterToken to the old concurrency token. |
| V-4 LandRequestWriteDiagnosticTests.C883_WriterStampComesFromCommittedToken(source) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:28 | Yes; full affected class | New runtime row | Proved RED: Stamp Guid.Empty for source/protocol LastWriterToken; row.LastWriterToken should be f3b44d33-17d6-426f-85d4-a29df43bdea7 but was 00000000-0000-0000-0000-000000000000 Additional Info: D.source-checkpoint.CommitTokenLinked. Restored sample GREEN. |
| V-4 LandRequestWriteDiagnosticTests.C883_WriterStampComesFromCommittedToken(protocol) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:28 | Yes; full affected class | New runtime row | Proved RED: Stamp Guid.Empty for source/protocol LastWriterToken; row.LastWriterToken should be 06f784a5-4aeb-4807-8430-0c3a4d8029b0 but was 00000000-0000-0000-0000-000000000000 Additional Info: D.source-checkpoint.CommitTokenLinked. Restored sample GREEN. |
| V-4 LandRequestWriteDiagnosticTests.C883_WriterStampComesFromCommittedToken(merge) | Not authored in scope | No | Pending | PENDING — Reply/Merge fixture belongs to CARD-0888, outside allowed footprint. |
| V-4 LandRequestWriteDiagnosticTests.C883_WriterStampComesFromCommittedToken(admission) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:28 | Yes; full affected class | New runtime row | Mutation proposal (not run): Set LastWriterToken to the old concurrency token. |
| V-4 LandRequestWriteDiagnosticTests.C883_MissingOrStaleWriterIsUnknown(legacy) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:216 | Yes; full affected class | New runtime row | Mutation proposal (not run): Trust label when LastWriterToken differs from stored token. |
| V-4 LandRequestWriteDiagnosticTests.C883_MissingOrStaleWriterIsUnknown(token-mismatch) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:216 | Yes; full affected class | New runtime row | Mutation proposal (not run): Trust label when LastWriterToken differs from stored token. |
| V-4 LandRequestWriteDiagnosticTests.C883_MissingOrStaleWriterIsUnknown(deleted-row) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:216 | Yes; full affected class | New runtime row | Mutation proposal (not run): Trust label when LastWriterToken differs from stored token. |
| V-4 LandRequestWriteDiagnosticTests.C883_MissingOrStaleWriterIsUnknown(read-unavailable) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:216 | Yes; full affected class | New runtime row | Mutation proposal (not run): Trust label when LastWriterToken differs from stored token. |
| V-4 LandRequestWriteDiagnosticTests.C883_SecretPayloadIsAbsent(none) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:258 | Yes; full affected class | New runtime row | Proved RED: Append the hostile synthetic exception message to the concurrency summary; log.Message should not contain (case insensitive comparison) "C939_PRIVATE_SQL_password_and_worktree_payload" but was actually "Land operation failed for task 8cb85668-62a3-4b45-960e-255e51d635fd request 8e6be43b-7c27-412f-97f9-..." Additional Info: D.SafeLogMessage. Restored sample GREEN. |
| V-4 LandRequestWriteDiagnosticTests.C883_TerminalSummaryIsIdempotent(none) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:303 | Yes; full affected class | New runtime row | Mutation proposal (not run): Overwrite original summary on repeated failure callback. |
| V-4 LandRequestWriteDiagnosticTests.C883_MigrationPreservesLegacyRows(none) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:148 | Yes; full affected class | Existing row; assertions retained/strengthened | Mutation proposal (not run): Backfill invented writer stamp during migration. |
| V-4 LandRequestWriteDiagnosticTests.C883_RolledBackWriterIsNotCommitted(none) | tests/Antiphon.Tests/Application/LandRequestWriteDiagnosticTests.cs:329 | Yes; full affected class | New runtime row | Mutation proposal (not run): Emit committed receipt after SaveChanges before commit. |
| V-4 LandRequestWriteDiagnosticTests.C883_DiagnosticReachesCaller(eligible) | Not authored in scope | No | Pending | PENDING — CARD-0888 owns BridgeQueueHarness/reply/notifier/refinement-spill fixtures; outside allowed footprint. |
| V-4 LandRequestWriteDiagnosticTests.C883_DiagnosticReachesCaller(busy) | Not authored in scope | No | Pending | PENDING — CARD-0888 owns BridgeQueueHarness/reply/notifier/refinement-spill fixtures; outside allowed footprint. |
| V-4 LandRequestWriteDiagnosticTests.C883_DiagnosticReachesCaller(before-enqueue) | Not authored in scope | No | Pending | PENDING — CARD-0888 owns BridgeQueueHarness/reply/notifier/refinement-spill fixtures; outside allowed footprint. |
| V-4 LandRequestWriteDiagnosticTests.C883_DiagnosticReachesCaller(queue-inserted) | Not authored in scope | No | Pending | PENDING — CARD-0888 owns BridgeQueueHarness/reply/notifier/refinement-spill fixtures; outside allowed footprint. |
| V-4 LandRequestWriteDiagnosticTests.C883_DiagnosticReachesCaller(queue-committed-before-wakeup) | Not authored in scope | No | Pending | PENDING — CARD-0888 owns BridgeQueueHarness/reply/notifier/refinement-spill fixtures; outside allowed footprint. |
| V-4 LandRequestWriteDiagnosticTests.C883_DiagnosticReachesCaller(receipt-before-save) | Not authored in scope | No | Pending | PENDING — CARD-0888 owns BridgeQueueHarness/reply/notifier/refinement-spill fixtures; outside allowed footprint. |

### V-5

| Planned result / exact test | File:line | Executed | Added or strengthened | Can-go-red evidence / pending reason |
|---|---|---|---|---|
| V-5 AgentTaskLandHalfResetWindowsTests.C883_LinkedWorktreeWithSpacesRecovers(none) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetWindowsTests.cs:17 | No | Pending | PENDING — Separate native Windows task; not selected on Linux. |
| V-5 AgentTaskLandHalfResetWindowsTests.C883_StagedBlobSurvivesNativeIndex(none) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetWindowsTests.cs:56 | No | Pending | PENDING — Separate native Windows task; not selected on Linux. |
| V-5 AgentTaskLandHalfResetWindowsTests.C883_BuiltinCrLfRecoveryPreservesRealEdits(none) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetWindowsTests.cs:80 | No | Pending | PENDING — Separate native Windows task; not selected on Linux. |
| V-5 AgentTaskLandHalfResetWindowsTests.C883_CaseAliasCannotChangeRegisteredIdentity(none) | tests/Antiphon.Tests/Application/AgentTaskLandHalfResetWindowsTests.cs:134 | No | Pending | PENDING — Separate native Windows task; not selected on Linux. |



## Unedited checkpoint receipts

Log: /work/worktrees/task-a5cebf5d/.antiphon/c939-baseline-cp2.log

```text
CHECKPOINT CP-2-baseline commit=4453dab63e5005414b2ded095b674f7b0ef1919e build=ok filter=/*/*/(AgentTaskLandHalfResetTests*)|(AgentTaskLandAdoptionConcurrencyTests*)|(AgentTaskLandAdoptionTests*)/* executed=53 passed=53 failed=0 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-baseline/CP-2-baseline-20261002-130523-9e20/run.trx slot=granted waited=0s dirty=0 source=4453dab63e5005414b2ded095b674f7b0ef1919e sourceState=clean buildSource=verified
CHECKPOINT CP-2-baseline EXIT CODE: 0
```

Log: /work/worktrees/task-a5cebf5d/.antiphon/c939-baseline-cp3.log

```text
CHECKPOINT CP-3-baseline commit=4453dab63e5005414b2ded095b674f7b0ef1919e build=reused filter=/*/*/(LandRecoveryCheckoutTests*)|(LandingGitTests*)/* executed=72 passed=72 failed=0 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-baseline/CP-3-baseline-20261002-131140-8034/run.trx slot=granted waited=0s dirty=0 source=4453dab63e5005414b2ded095b674f7b0ef1919e sourceState=clean buildSource=verified
CHECKPOINT CP-3-baseline EXIT CODE: 0
```

Log: /work/worktrees/task-a5cebf5d/.antiphon/c939-final-cp2-r4.log

```text
CHECKPOINT CP-2 commit=6555770dd788b4228e5a15362b247af6989be566 build=ok filter=/*/*/(AgentTaskLandHalfResetTests*)|(AgentTaskLandAdoptionConcurrencyTests*)|(AgentTaskLandAdoptionTests*)/* executed=83 passed=83 failed=0 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-2-20261002-134518-2edc/run.trx slot=granted waited=0s dirty=0 source=6555770dd788b4228e5a15362b247af6989be566 sourceState=clean buildSource=verified
CHECKPOINT CP-2 EXIT CODE: 0
```

Log: /work/worktrees/task-a5cebf5d/.antiphon/c939-final-cp2-r5.log

```text
CHECKPOINT CP-2 commit=97ef60b57ab902bd9bc1ec68a9de7350ee423dba build=ok filter=/*/*/(AgentTaskLandHalfResetTests*)|(AgentTaskLandAdoptionConcurrencyTests*)|(AgentTaskLandAdoptionTests*)/* executed=83 passed=83 failed=0 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-2-20261002-135815-9687/run.trx slot=granted waited=0s dirty=0 source=97ef60b57ab902bd9bc1ec68a9de7350ee423dba sourceState=clean buildSource=verified
CHECKPOINT CP-2 EXIT CODE: 0
```

Log: /work/worktrees/task-a5cebf5d/.antiphon/c939-final-cp2-r6.log

```text
CHECKPOINT CP-2 commit=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 build=ok filter=/*/*/(AgentTaskLandHalfResetTests*)|(AgentTaskLandAdoptionConcurrencyTests*)|(AgentTaskLandAdoptionTests*)/* executed=83 passed=83 failed=0 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-2-20261002-141026-4ee6/run.trx slot=granted waited=0s dirty=0 source=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 sourceState=clean buildSource=verified
CHECKPOINT CP-2 EXIT CODE: 0
```

Log: /work/worktrees/task-a5cebf5d/.antiphon/c939-final-cp3.log

```text
CHECKPOINT CP-3 commit=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 build=reused filter=/*/*/(LandRecoveryCheckoutTests*)|(LandingGitTests*)/* executed=89 passed=89 failed=0 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-3-20261002-141538-50d2/run.trx slot=granted waited=0s dirty=0 source=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 sourceState=clean buildSource=verified
CHECKPOINT CP-3 EXIT CODE: 0
```

Log: /work/worktrees/task-a5cebf5d/.antiphon/c939-final-cp4-r1.log

```text
CHECKPOINT CP-4 commit=97ef60b57ab902bd9bc1ec68a9de7350ee423dba build=reused filter=/*/*/(LandRequestWriteDiagnosticTests*)|(AgentTaskLandSourcePersistenceTests*)|(AgentTaskLandFailureDiagnosticTests*)|(AgentTaskLandMonitoringTests*)/* executed=102 passed=100 failed=2 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-4-20261002-140615-5996/run.trx slot=granted waited=0s dirty=0 source=97ef60b57ab902bd9bc1ec68a9de7350ee423dba sourceState=clean buildSource=verified
CHECKPOINT CP-4 EXIT CODE: 1
```

Log: /work/worktrees/task-a5cebf5d/.antiphon/c939-final-cp4.log

```text
CHECKPOINT CP-4 commit=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 build=reused filter=/*/*/(LandRequestWriteDiagnosticTests*)|(AgentTaskLandSourcePersistenceTests*)|(AgentTaskLandFailureDiagnosticTests*)|(AgentTaskLandMonitoringTests*)/* executed=101 passed=101 failed=0 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-4-20261002-141629-dc68/run.trx slot=granted waited=0s dirty=0 source=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 sourceState=clean buildSource=verified
CHECKPOINT CP-4 EXIT CODE: 0
```

Log: /work/worktrees/task-a5cebf5d/.antiphon/c939-sensitivity-red.log

```text
CHECKPOINT CODE-SENSITIVITY-RED commit=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 build=ok filter=/*/*/(AgentTaskLandHalfResetTests*)|(AgentTaskLandAdoptionConcurrencyTests*)|(LandRecoveryCheckoutTests*)|(LandRequestWriteDiagnosticTests*)/(C883_FreshRequestRequiresAncestor*)|(C883_CurrentPendingRequestCannotBeStolen*)|(C883_FreshRequestRejectsWitnessIdentity*)|(C883_ContentChangesBeforeResetRefuse*)|(C883_HeldWriterBetweenMoveAndResetDoesNotLoseHoldFacts*)|(C883_RestartAfterMoveResumesPendingBeforeFreshAdmission*)|(C883_SubmoduleAndFilterRefuse*)|(C883_BinaryEolAndModeComparison*)|(C883_ProbeErrorsRefuse*)|(C883_UnsafeIndexModesRefuse*)|(C883_SequencerRefuses*)|(C883_IdentityResampleRefusesChange*)|(C883_WriterStampComesFromCommittedToken*)|(C883_SecretPayloadIsAbsent*) executed=51 passed=17 failed=34 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-sensitivity/CODE-SENSITIVITY-RED-20261002-141921-64e4/run.trx slot=granted waited=0s dirty=7 source=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35+dirty:56e63e41dac0958e27bc3f508d61f139d4a18d0b9f12d98be0570cc640aaab1e sourceState=dirty buildSource=verified
CHECKPOINT CODE-SENSITIVITY-RED EXIT CODE: 1
```

Log: /work/worktrees/task-a5cebf5d/.antiphon/c939-sensitivity-green.log

```text
CHECKPOINT CODE-SENSITIVITY-GREEN commit=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 build=ok filter=/*/*/(AgentTaskLandHalfResetTests*)|(AgentTaskLandAdoptionConcurrencyTests*)|(LandRecoveryCheckoutTests*)|(LandRequestWriteDiagnosticTests*)/(C883_FreshRequestRequiresAncestor*)|(C883_CurrentPendingRequestCannotBeStolen*)|(C883_FreshRequestRejectsWitnessIdentity*)|(C883_ContentChangesBeforeResetRefuse*)|(C883_HeldWriterBetweenMoveAndResetDoesNotLoseHoldFacts*)|(C883_RestartAfterMoveResumesPendingBeforeFreshAdmission*)|(C883_SubmoduleAndFilterRefuse*)|(C883_BinaryEolAndModeComparison*)|(C883_ProbeErrorsRefuse*)|(C883_UnsafeIndexModesRefuse*)|(C883_SequencerRefuses*)|(C883_IdentityResampleRefusesChange*)|(C883_WriterStampComesFromCommittedToken*)|(C883_SecretPayloadIsAbsent*) executed=51 passed=51 failed=0 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-sensitivity/CODE-SENSITIVITY-GREEN-20261002-142457-5b31/run.trx slot=granted waited=0s dirty=0 source=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 sourceState=clean buildSource=verified
CHECKPOINT CODE-SENSITIVITY-GREEN EXIT CODE: 0
```

Log: /work/worktrees/task-a5cebf5d/.antiphon/c939-unit.log

```text
CHECKPOINT CP-6-UNIT commit=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 build=reused filter=/*/*/*/*[Category=Unit] executed=3733 passed=3733 failed=0 skipped=40 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-6-UNIT-20261002-142759-e6c5/run.trx slot=granted waited=0s dirty=0 source=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 sourceState=clean buildSource=verified
CHECKPOINT CP-6-UNIT EXIT CODE: 0
```



## Strict source evidence for final affected checkpoints

/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-2-20261002-141026-4ee6/source.json

```json
{
  "version": 1,
  "name": "CP-2",
  "start": {
    "commit": "f3c11e0a7c6d0cf3f3cd87518355a491a51eac35",
    "dirtyFiles": 0,
    "fingerprint": "f3ae65f445808e9a279dc6e6f1c8ee45cc121b20a6babce385b9b903531fd63b",
    "observedAtUtc": "2026-10-02T14:10:27.3393261+00:00",
    "captureStatus": "known",
    "errorCode": null
  },
  "end": {
    "commit": "f3c11e0a7c6d0cf3f3cd87518355a491a51eac35",
    "dirtyFiles": 0,
    "fingerprint": "f3ae65f445808e9a279dc6e6f1c8ee45cc121b20a6babce385b9b903531fd63b",
    "observedAtUtc": "2026-10-02T14:15:08.7600801+00:00",
    "captureStatus": "known",
    "errorCode": null
  },
  "state": "clean",
  "buildSource": "verified",
  "reason": null,
  "receipt": "CHECKPOINT CP-2 commit=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 build=ok filter=/*/*/(AgentTaskLandHalfResetTests*)|(AgentTaskLandAdoptionConcurrencyTests*)|(AgentTaskLandAdoptionTests*)/* executed=83 passed=83 failed=0 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-2-20261002-141026-4ee6/run.trx slot=granted waited=0s dirty=0 source=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 sourceState=clean buildSource=verified",
  "exitCode": 0,
  "executed": 83,
  "passed": 83,
  "failed": 0,
  "skipped": 0
}
```

/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-3-20261002-141538-50d2/source.json

```json
{
  "version": 1,
  "name": "CP-3",
  "start": {
    "commit": "f3c11e0a7c6d0cf3f3cd87518355a491a51eac35",
    "dirtyFiles": 0,
    "fingerprint": "f3ae65f445808e9a279dc6e6f1c8ee45cc121b20a6babce385b9b903531fd63b",
    "observedAtUtc": "2026-10-02T14:15:39.3714362+00:00",
    "captureStatus": "known",
    "errorCode": null
  },
  "end": {
    "commit": "f3c11e0a7c6d0cf3f3cd87518355a491a51eac35",
    "dirtyFiles": 0,
    "fingerprint": "f3ae65f445808e9a279dc6e6f1c8ee45cc121b20a6babce385b9b903531fd63b",
    "observedAtUtc": "2026-10-02T14:16:05.5671703+00:00",
    "captureStatus": "known",
    "errorCode": null
  },
  "state": "clean",
  "buildSource": "verified",
  "reason": null,
  "receipt": "CHECKPOINT CP-3 commit=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 build=reused filter=/*/*/(LandRecoveryCheckoutTests*)|(LandingGitTests*)/* executed=89 passed=89 failed=0 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-3-20261002-141538-50d2/run.trx slot=granted waited=0s dirty=0 source=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 sourceState=clean buildSource=verified",
  "exitCode": 0,
  "executed": 89,
  "passed": 89,
  "failed": 0,
  "skipped": 0
}
```

/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-4-20261002-141629-dc68/source.json

```json
{
  "version": 1,
  "name": "CP-4",
  "start": {
    "commit": "f3c11e0a7c6d0cf3f3cd87518355a491a51eac35",
    "dirtyFiles": 0,
    "fingerprint": "f3ae65f445808e9a279dc6e6f1c8ee45cc121b20a6babce385b9b903531fd63b",
    "observedAtUtc": "2026-10-02T14:16:29.8652794+00:00",
    "captureStatus": "known",
    "errorCode": null
  },
  "end": {
    "commit": "f3c11e0a7c6d0cf3f3cd87518355a491a51eac35",
    "dirtyFiles": 0,
    "fingerprint": "f3ae65f445808e9a279dc6e6f1c8ee45cc121b20a6babce385b9b903531fd63b",
    "observedAtUtc": "2026-10-02T14:18:36.8423775+00:00",
    "captureStatus": "known",
    "errorCode": null
  },
  "state": "clean",
  "buildSource": "verified",
  "reason": null,
  "receipt": "CHECKPOINT CP-4 commit=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 build=reused filter=/*/*/(LandRequestWriteDiagnosticTests*)|(AgentTaskLandSourcePersistenceTests*)|(AgentTaskLandFailureDiagnosticTests*)|(AgentTaskLandMonitoringTests*)/* executed=101 passed=101 failed=0 skipped=0 trx=/work/worktrees/task-a5cebf5d/.antiphon/c939-final/CP-4-20261002-141629-dc68/run.trx slot=granted waited=0s dirty=0 source=f3c11e0a7c6d0cf3f3cd87518355a491a51eac35 sourceState=clean buildSource=verified",
  "exitCode": 0,
  "executed": 101,
  "passed": 101,
  "failed": 0,
  "skipped": 0
}
```
