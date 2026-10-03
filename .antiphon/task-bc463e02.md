# CARD-1015 Code Final report

Implemented S1/S2/S2b/S3, pushed every slice, and completed ordinary verification with disclosed inherited skips. All caused assertion failures are repaired. No land/deploy or PC mutation was performed.

Original Code task / landing owner: bc463e02-3fd6-4c05-9883-0ef2f876aaf2.
Branch: feat/card-task-bc463e02. Worktree: /work/worktrees/task-bc463e02.
Immutable dispatch base B: 90a936e39f899655e48dddb9d2ed8287222d92e8.
Implementation/repair tip tested: 07f6c4fb029d4a0652104f105e18f66da0bf8014.
Plan: docs/superpowers/plans/2026-10-03-card-1015-evidence-git-policy-plan.md.
The publication tip is the later, individually committed Markdown report; its full SHA is in the final caller message. Earlier receipts retain their actual source SHAs. Final Review must qualify the exact pushed publication tip. This record supersedes the historical admission-blocked report in docs/investigations/2026-10-03-card-1015-code-admission-bc463e02.md.

## Admission and deletion authority

Independent native Git tree metadata at B gave 312 root evidence paths / 38,101,068 bytes. D(B) is 289 paths / 37,541,892 bytes; NUL-terminated Git-order path SHA-256: 4e0a2ec36b9817c7cba9a2d5e05e85ecaa3f5a23b56daeb7469b3fd55b7684ed. K(B) is 23 paths / 559,176 bytes; path digest 2e663b26148771f50e1cb73c6e4821b9e3df52b14190504a8cd199588c868ace. Delta from the inspected freeze is zero. No payloads/private notes were inspected.

Original anchor bb5fa774cd56f85ee6f0b1122c198192427e5ddf remains 108 = 88 rejected + 20 permitted, with unchanged identities/classifications at B. Classification signature f5082847ba6f5e2b1db50b407ff80b7f8cb36575ef9fe8f90c825c7b4ca09c2b. The admission record contains all partition counts/bytes/digests. The independent census remains literal 377; source roster predicted 4,020 eligible Unit results and fresh TRX selected exactly 4,020. No census edit was made.

Read runner-defaults, session-runners and pipeline via the environment-resolved authenticated API. Defaults revision 2: no overrides; eligible desktop/Linux, temporary runner unavailable/draining. No fleet location/platform pin was added. CARD-1008 continuation 52bffc69 has no current planned-footprint intersection; CARD-0959 818582a5 ended. CARD-1011 owner 698c0e44 overlaps two files, explicitly ACCEPTED by caller before implementation. Its idle Windows gates do not block this work.

Deletion-only S3: 37ddd0501605d7117736436fa6cd794f572ce02a. Parent: 0145516f904b36c9aed41b926f823a33ce74a333. Exact trailer: Antiphon-Evidence-Deletion: CARD-1015. Recomputed InventoryOnly agreed with the independent inventory before staging. Staged NUL/literal paths matched both directions of D(B), with old mode/OID equal to B; no extra change. All 289 historical blobs remain recoverable, all 23 permitted paths retain identity, and final root policy violations are zero. No history rewrite or force push occurred.

## Implementation and accepted-overlap hunks

- scripts/lib/evidence-policy.ps1 and the two check-evidence wrappers: owned/drained byte Git I/O, strict UTF-8/NUL/full-OID metadata, pinned refs, complete reachable commit/first-parent/root diffs, committed byte sizes, fail-closed CI event ranges, independent anchored inventory and exact deletion/final-tree/preservation/recoverability proof. Exit 0 compliant, 1 violation, 2 unverifiable.
- .github/workflows/evidence-policy.yml: full-history SHA-bound checkout, master/feat/** pushes and explicit manual ranges, env data arguments, contents read, actual guard call and failed-exit propagation.
- delegate-basics/stage-code/stage-review and docs/testing-and-build.md: ignored generated evidence, individual allowed Markdown, full history guard, actual tested source/unedited receipts, authority preservation. Review's compact ? placeholder means true/false; common instructions require SHA-validated clean receipts and verified build provenance for true. Existing caps/assertions were preserved. Code is 2,406 bytes; Review normalizes to 2,480 characters.
- New EvidenceDiffGuardTests (18 Integration), EvidenceDeletionGuardTests (six Integration), EvidencePolicyWorkflowTests (four Unit), shared owned Git fixture and four bundle Unit methods. Category/process limiters and independent anchored rule oracle are included. No runtime cleanup/delivery implementation changed; CARD-1017 owns whole-worktree cleanup.
- Accepted overlap: docs/orchestration-loop.md only six added lines in section 0, “Also delegated: the landing mechanics,” after the publication/cleanup paragraph, commit 0f17c25331473a4dbc077389babbcb575d9d8a5a. InstructionBundleTests only four new methods: C1015_Composed_workers_keep_generated_evidence_untracked; C1015_Code_keeps_range_guard_and_exact_source; C1015_Review_remains_read_only_and_checks_history; C1015_SourceLanding_keeps_its_external_evidence_exception. Localized test-only commits 9ea6735b1765fa07cf2ae999f48b661d7030d17d and bd656f91fe632f6527109ca7d81ebbd15577b1bb; no existing methods/reformat/moved paragraphs.

## Verification disposition

Full eligible Unit ran once at 819596d9bcbaa7d024fe88d8af3e0605011e1f73: selected 4,020; executed 3,974; passed 3,967; failed seven; skipped 46. Every failed method passed individually at B and is now green in full affected repair classes. The original Unit receipt stays red: no floor, skip, assertion, timeout or repeat relaxation. The commissioning brief explicitly accepts inherited Windows/jq skips and the resulting strict Unit qualification refusal. No new tests skip. jq is absent; Windows-only prerequisite gates account for the other skipped cases. This is “met with disclosed inherited skips,” not zero-skip qualification.

Full history class 18/18 and full deletion class 6/6 passed, both smokes 1/1 passed, workflow 4/4 passed in Unit. Final repair group at 07f6c4fb029d4a0652104f105e18f66da0bf8014: eight full classes, 155/155 passed, zero failures/skips. These include complete InstructionBundleTests 66 and complete DelegateBundleLaunchTests 23 (pure launch-spec construction, no new asynchronous delivery). Distinct cumulative ordinary roster: 4,069 results, 4,023 passed, 46 inherited skipped, zero unresolved failures. Original 4,046-result scope plus 23 adjacent Integration results; repeated Unit class rows are not new distinct executions.

Qualified ordinary and repair checkpoint source states are clean with verified build provenance for TUnit rows; command rows correctly say notApplicable. All slots were granted, waited=0s. No full assembly, Pty assembly, live provider, browser or Windows-native qualification was run. No application delivery path changed. Instruction/YAML tests prove composition/wiring, not model obedience or hosted Actions execution. Ordinary estimate 29 minutes plus three preparation; bounded repair manifest adds eight minutes. Post-land Mutation floor 969.25 minutes remains separate.

## Every ordinary invariant and outcome

| ID | Actual outcome / evidence |
|---|---|
| V-1 | Passed: CP-2 full class, Rejects_checkpoint_paths and mandatory internal vectors. |
| V-2 | Passed: CP-2 full class, Rejects_non_markdown_outputs and mandatory internal vectors. |
| V-3 | Passed: CP-2 full class, Rejects_non_regular_modes and mandatory internal vectors. |
| V-4 | Passed: CP-2 full class, Enforces_one_mib_blob_limit and mandatory internal vectors. |
| V-5 | Passed: CP-2 full class, Allows_small_markdown_and_other_source and mandatory internal vectors. |
| V-6 | Passed: CP-2 full class, Grandfathers_unchanged_legacy_and_allows_deletion and mandatory internal vectors. |
| V-7 | Passed: CP-2 full class, Rechecks_modified_and_renamed_legacy_paths and mandatory internal vectors. |
| V-8 | Passed: CP-2 full class, Checks_intermediate_commits_even_when_tip_is_clean and mandatory internal vectors. |
| V-9 | Passed: CP-2 full class, Checks_merge_side_history and mandatory internal vectors. |
| V-10 | Passed: CP-2 full class, Reads_pinned_git_objects_not_index_or_worktree and mandatory internal vectors. |
| V-11 | Passed: CP-2 full class, Handles_literal_paths_and_case_variants and mandatory internal vectors. |
| V-12 | Passed: CP-2 full class, Refuses_unverifiable_ranges_and_objects and mandatory internal vectors. |
| V-13 | Passed: CP-2 full class, Refuses_failed_or_malformed_git_results and mandatory internal vectors. |
| V-14 | Passed: CP-2 full class, Resolves_refs_once_and_stays_read_only and mandatory internal vectors. |
| V-15 | Passed: CP-2 full class, Ci_selects_cumulative_feature_push_range and mandatory internal vectors. |
| V-16 | Passed: CP-2 full class, Ci_selects_complete_default_branch_push_range and mandatory internal vectors. |
| V-17 | Passed: CP-2 full class, Ci_requires_valid_manual_range and mandatory internal vectors. |
| V-18 | Passed: CP-2 full class, Ci_refuses_invalid_events_and_missing_history and mandatory internal vectors. |
| V-19 | Passed: four new composed-policy tests; complete 66-case bundle class CP-8; caps and quoted composition budget unchanged. |
| V-20 | Passed: all four actual YAML/PowerShell-AST Unit methods, fresh CP-1 TRX. |
| V-21 | Passed: CP-3 all six anchored real-Git methods and invalid/identity/preservation vectors. |
| V-22 | Passed: CP-4 and CP-5 exact existing methods, one result each. |
| V-23 | Passed: CP-6 real B..tested repair HEAD; report-commit rerun required and returned in final caller message. |
| V-24 | Passed: CP-7 exact S3/anchor/final tree/preservation/recoverability; report-commit rerun returned in final caller message. |
| V-25 | Met with disclosed inherited skips: 4020 selected, 3974 executed, 46 skipped; all seven caused failures repaired in full classes, no unresolved failure. Strict original Unit row remains red. |
| R-1 | Passed: V-19/V-20, complete bundle/workflow cases and eight repair classes; original authority and budget guards retained. |
| R-2 | Passed: pinned objects/read-only before-after snapshots and both existing ignored-source/landing smokes. |
| R-3 | Passed: CP-2 all history/path/mode/size/legacy/merge vectors and actual CP-6 candidate history. |
| R-4 | Passed: all fail-closed/ref/metadata/event/CI range vectors, explicit equal range and counters. |
| R-5 | Passed: CP-3 exact inventory/deletion/identity/anchor/preservation/recovery cases and actual CP-7. |
| R-6 | Passed: namespace_census_matches_compiled_checkpoint_cases in fresh CP-1, independent literal 377 unchanged; repeat documentation CP-12 green. |
| R-7 | Met with disclosed inherited Windows/jq skips: whole eligible Unit once, targeted full-class repairs green. No skip or count waiver introduced by code. |

Deferred-to-Final ordinary IDs: none. Hosted Actions at the landed SHA and loaded server bundle hashes are separately scheduled caller acceptance after Review/land/activation, explicitly excluded from pre-land local proof. No current cleanup/storage durability or transcript delivery claim is made.

Inherited skip roster (fresh Unit TRX, total 46):

| Existing class | Skipped results |
|---|---:|
| Antiphon.Tests.Agents.AgentRegistrySettingsTests | 1 |
| Antiphon.Tests.Application.AgentExecutableResolverTests | 1 |
| Antiphon.Tests.Application.AgentPinPathTests | 1 |
| Antiphon.Tests.Application.ClaudeRemoteControlLaunchArgsTests | 1 |
| Antiphon.Tests.Application.DelegationReportFormatterTests | 1 |
| Antiphon.Tests.Application.DirectoryBrowseServiceTests | 5 |
| Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests | 12 |
| Antiphon.Tests.Application.PtyDeliveryCeilingsTests | 3 |
| Antiphon.Tests.Application.SessionDeliveryProfileTests | 2 |
| Antiphon.Tests.Scripts.RemoteScriptContractTests | 19 |

## Red-first, failures and unlisted preparation

The mandatory gated tool bootstrap built tools/Antiphon.Checkpoints with OutputPath=bin-c1015-tool/ and UseAppHost=false. Actual DLL is tools/Antiphon.Checkpoints/bin-c1015-tool/Antiphon.Checkpoints.dll (no net9.0 subdirectory). Slot granted, waited=0s. The actual importer admitted all seven original rows before implementation; after explicit repair amendments it admitted 12, then 15. Read-only coverage lint with seven explicit test files and the ignored external bindings reports 46 obligations / 46 matched / zero missing/unmapped/PC issues; reachability remains unproven, not PC green.

Red-first preparation was outside the ordinary table because the brief requires new tests before the implementation:

- RED-S1 at 9ea6735b1765fa07cf2ae999f48b661d7030d17d initially failed compilation (28 Shouldly message-binding errors); no red proof is claimed for it. Fixed only the new test calls with named customMessage.
- RED-S1 at bd656f91fe632f6527109ca7d81ebbd15577b1bb executed four: three intended new policy assertions failed against unchanged bundles; SourceLanding preservation was already correct and passed. No SourceLanding defect red is claimed; PC-54/55 stay pending.
- RED-S2-S2b at 0e87857bfed59df6e4ef9eb15daca84bef65ba80 executed 28 / failed 28 / skipped zero before wrappers/workflow were installed. Failures were explicit installed-feature assertions, not compiler/fixture errors. These prove feature absence at the base; they do not prove each deep guard mutant. All deep controls remain pending.
- First original manifest run 20261003-203943-2d3d: Unit filter parser refused before any Unit TRX; history 16/18, deletion 4/6, both smokes and actual guards passed. PowerShell EncodedCommand duplicated Write-Host markers in CLIXML. OutputFormat Text restored exactly-one-hit observations without relaxing the counter assertions. CP-1 uses the already-landed CARD-1005 MTP syntax repair, same six exclusions and same floor.
- First actual whole Unit run 20261003-205229-3106: the seven caused failures are pinned checkpoint wording, quoted composition budget, two Review base/platform contracts, fresh-TRX wording, Final Review wording and Code byte budget. Each passed at immutable B. The first combined baseline filter selected zero: discarded, not credited. Seven separate exact-method NoBuild baseline calls then each executed one/pass one, clean B, verified same isolated baseline build, slot granted/waited=0s.
- First targeted repair 20261003-211527-effd: 63/66 bundle results, other four classes 19/19. Three bundle failures were two shortened existing pins and the quoted command-line estimate. Restored both pins and shortened explanatory prose; no test/assertion/cap/time budget changed.
- Final targeted repair 20261003-212229-b54c: all eight classes 155/155 passed, plus both actual guards. Fresh TRX audits name every intended class/method and reconcile all seven original failing cases. CP-13..CP-15 add full adjacent instruction/launch-spec classes, explicitly justified in the amended closed table.

Each repair was committed/pushed before its run; no source was edited during a run. No second whole Unit execution occurred. No repeat exceeded the unchanged-selection budget; no loaded/flake repetitions were requested. Baseline verification used only the seven named methods, never the baseline assembly.

The repair tool's report says builds: 2 because the manifest defines two groups. Its state records bin-c1015 as unused/0 seconds and bin-c1015-repair as the one actual build (97.3887837 seconds). The executor used one build lease; no unlisted second build was performed. Final repair wall 3m03s; all build/test leases granted, waited=0s.

Manual Code work completed: independent admission inventory, active footprint/explicit overlap decision, actual importer, InventoryOnly agreement before staging, exact staged deletion proof, fresh TRX/roster inspection, source/slot/build receipt inspection and actual range/tree guards. Alternate outputs and owned baseline worktree are removed after final publication guard checks. This is task-owned verification disposal, not CARD-1017 runtime cleanup.

## Essential unedited checkpoint receipts

Generated TRX/log/JSON stays ignored. The following CHECKPOINT lines are copied byte-for-byte from each report; no source SHA/count has been relabeled.

### 20261003-203943-2d3d

Raw report: /work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-203943-2d3d/report.md

CHECKPOINT CP-6 commit=3c761d7b18c8330f62dd5127b66f4c2677bb6799 build=n/a filter=pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef "$C1015_BASE_SHA" -HeadRef HEAD executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=3c761d7b18c8330f62dd5127b66f4c2677bb6799 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-7 commit=3c761d7b18c8330f62dd5127b66f4c2677bb6799 build=n/a filter=pwsh -NoProfile -File scripts/check-evidence-deletion.ps1 -InventoryRef "$C1015_BASE_SHA" -InventoryPathSha256 "$C1015_DELETE_PATH_SHA256" -InventoryCount "$C1015_DELETE_COUNT" -InventoryBytes "$C1015_DELETE_BYTES" -HeadRef HEAD executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=3c761d7b18c8330f62dd5127b66f4c2677bb6799 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-1 commit=3c761d7b18c8330f62dd5127b66f4c2677bb6799 build=n/a filter=/*/*/*/(!windows_quick_row_finishes_beside_a_slow_row)&(!windows_row_arguments_round_trip_intact)&(!windows_chatty_row_drains_interleaved_stdout_and_stderr)&(!windows_row_timeout_kills_the_start_b_grandchild)&(!C665_LockedFileMidDeleteResumesOnLaterPass)&(!C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded)[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=granted waited=0s dirty=0 source=3c761d7b18c8330f62dd5127b66f4c2677bb6799 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=3c761d7b18c8330f62dd5127b66f4c2677bb6799 build=reused filter=/*/*/EvidenceDiffGuardTests/* executed=18 passed=16 failed=2 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-203943-2d3d/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=3c761d7b18c8330f62dd5127b66f4c2677bb6799 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=3c761d7b18c8330f62dd5127b66f4c2677bb6799 build=reused filter=/*/*/EvidenceDeletionGuardTests/* executed=6 passed=4 failed=2 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-203943-2d3d/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=3c761d7b18c8330f62dd5127b66f4c2677bb6799 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=3c761d7b18c8330f62dd5127b66f4c2677bb6799 build=reused filter=/*/*/LandingGitTests/C642_IdentityAndStatusScopeSkipsIgnoredListing executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-203943-2d3d/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=3c761d7b18c8330f62dd5127b66f4c2677bb6799 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=3c761d7b18c8330f62dd5127b66f4c2677bb6799 build=reused filter=/*/*/CheckpointSourceStateTests/clean_and_ignored_outputs_match_head executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-203943-2d3d/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=3c761d7b18c8330f62dd5127b66f4c2677bb6799 sourceState=clean buildSource=verified

### 20261003-205229-3106

Raw report: /work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-205229-3106/report.md

CHECKPOINT CP-6 commit=819596d9bcbaa7d024fe88d8af3e0605011e1f73 build=n/a filter=pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef "$C1015_BASE_SHA" -HeadRef HEAD executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=819596d9bcbaa7d024fe88d8af3e0605011e1f73 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-7 commit=819596d9bcbaa7d024fe88d8af3e0605011e1f73 build=n/a filter=pwsh -NoProfile -File scripts/check-evidence-deletion.ps1 -InventoryRef "$C1015_BASE_SHA" -InventoryPathSha256 "$C1015_DELETE_PATH_SHA256" -InventoryCount "$C1015_DELETE_COUNT" -InventoryBytes "$C1015_DELETE_BYTES" -HeadRef HEAD executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=819596d9bcbaa7d024fe88d8af3e0605011e1f73 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-1 commit=819596d9bcbaa7d024fe88d8af3e0605011e1f73 build=ok filter=/*[Category=Unit]/*/*/(!windows_quick_row_finishes_beside_a_slow_row*)&(!windows_row_arguments_round_trip_intact*)&(!windows_chatty_row_drains_interleaved_stdout_and_stderr*)&(!windows_row_timeout_kills_the_start_b_grandchild*)&(!C665_LockedFileMidDeleteResumesOnLaterPass*)&(!C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded*) executed=3974 passed=3967 failed=7 skipped=46 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-205229-3106/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=819596d9bcbaa7d024fe88d8af3e0605011e1f73 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=819596d9bcbaa7d024fe88d8af3e0605011e1f73 build=reused filter=/*/*/EvidenceDiffGuardTests/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-205229-3106/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=819596d9bcbaa7d024fe88d8af3e0605011e1f73 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=819596d9bcbaa7d024fe88d8af3e0605011e1f73 build=reused filter=/*/*/EvidenceDeletionGuardTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-205229-3106/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=819596d9bcbaa7d024fe88d8af3e0605011e1f73 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=819596d9bcbaa7d024fe88d8af3e0605011e1f73 build=reused filter=/*/*/LandingGitTests/C642_IdentityAndStatusScopeSkipsIgnoredListing executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-205229-3106/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=819596d9bcbaa7d024fe88d8af3e0605011e1f73 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=819596d9bcbaa7d024fe88d8af3e0605011e1f73 build=reused filter=/*/*/CheckpointSourceStateTests/clean_and_ignored_outputs_match_head executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-205229-3106/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=819596d9bcbaa7d024fe88d8af3e0605011e1f73 sourceState=clean buildSource=verified

### 20261003-211527-effd

Raw report: /work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-211527-effd/report.md

CHECKPOINT CP-6 commit=f903232b89d2c391ea478763d759167f94c155c0 build=n/a filter=pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef "$C1015_BASE_SHA" -HeadRef HEAD executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=f903232b89d2c391ea478763d759167f94c155c0 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-7 commit=f903232b89d2c391ea478763d759167f94c155c0 build=n/a filter=pwsh -NoProfile -File scripts/check-evidence-deletion.ps1 -InventoryRef "$C1015_BASE_SHA" -InventoryPathSha256 "$C1015_DELETE_PATH_SHA256" -InventoryCount "$C1015_DELETE_COUNT" -InventoryBytes "$C1015_DELETE_BYTES" -HeadRef HEAD executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=f903232b89d2c391ea478763d759167f94c155c0 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-8 commit=f903232b89d2c391ea478763d759167f94c155c0 build=ok filter=/*/*/InstructionBundleTests/* executed=66 passed=63 failed=3 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-211527-effd/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=f903232b89d2c391ea478763d759167f94c155c0 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=f903232b89d2c391ea478763d759167f94c155c0 build=reused filter=/*/*/CheckpointManifestDocumentationTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-211527-effd/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=f903232b89d2c391ea478763d759167f94c155c0 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=f903232b89d2c391ea478763d759167f94c155c0 build=reused filter=/*/*/TaskPlatformGuidanceTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-211527-effd/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=f903232b89d2c391ea478763d759167f94c155c0 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=f903232b89d2c391ea478763d759167f94c155c0 build=reused filter=/*/*/VerificationRoundInstructionTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-211527-effd/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=f903232b89d2c391ea478763d759167f94c155c0 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=f903232b89d2c391ea478763d759167f94c155c0 build=reused filter=/*/*/CheckpointRepeatDocumentationTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-211527-effd/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=f903232b89d2c391ea478763d759167f94c155c0 sourceState=clean buildSource=verified

### 20261003-212229-b54c

Raw report: /work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-212229-b54c/report.md

CHECKPOINT CP-6 commit=07f6c4fb029d4a0652104f105e18f66da0bf8014 build=n/a filter=pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef "$C1015_BASE_SHA" -HeadRef HEAD executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=07f6c4fb029d4a0652104f105e18f66da0bf8014 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-7 commit=07f6c4fb029d4a0652104f105e18f66da0bf8014 build=n/a filter=pwsh -NoProfile -File scripts/check-evidence-deletion.ps1 -InventoryRef "$C1015_BASE_SHA" -InventoryPathSha256 "$C1015_DELETE_PATH_SHA256" -InventoryCount "$C1015_DELETE_COUNT" -InventoryBytes "$C1015_DELETE_BYTES" -HeadRef HEAD executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=07f6c4fb029d4a0652104f105e18f66da0bf8014 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-8 commit=07f6c4fb029d4a0652104f105e18f66da0bf8014 build=ok filter=/*/*/InstructionBundleTests/* executed=66 passed=66 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-212229-b54c/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=07f6c4fb029d4a0652104f105e18f66da0bf8014 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=07f6c4fb029d4a0652104f105e18f66da0bf8014 build=reused filter=/*/*/CheckpointManifestDocumentationTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-212229-b54c/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=07f6c4fb029d4a0652104f105e18f66da0bf8014 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=07f6c4fb029d4a0652104f105e18f66da0bf8014 build=reused filter=/*/*/TaskPlatformGuidanceTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-212229-b54c/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=07f6c4fb029d4a0652104f105e18f66da0bf8014 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=07f6c4fb029d4a0652104f105e18f66da0bf8014 build=reused filter=/*/*/VerificationRoundInstructionTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-212229-b54c/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=07f6c4fb029d4a0652104f105e18f66da0bf8014 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=07f6c4fb029d4a0652104f105e18f66da0bf8014 build=reused filter=/*/*/CheckpointRepeatDocumentationTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-212229-b54c/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=07f6c4fb029d4a0652104f105e18f66da0bf8014 sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=07f6c4fb029d4a0652104f105e18f66da0bf8014 build=reused filter=/*/*/PostLandMutationContractTests/* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-212229-b54c/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=07f6c4fb029d4a0652104f105e18f66da0bf8014 sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=07f6c4fb029d4a0652104f105e18f66da0bf8014 build=reused filter=/*/*/ScopedVerificationInstructionTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-212229-b54c/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=07f6c4fb029d4a0652104f105e18f66da0bf8014 sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=07f6c4fb029d4a0652104f105e18f66da0bf8014 build=reused filter=/*/*/DelegateBundleLaunchTests/* executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/checkpoints/20261003-212229-b54c/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=07f6c4fb029d4a0652104f105e18f66da0bf8014 sourceState=clean buildSource=verified

### Immutable base exact-method confirmations

CHECKPOINT BASE-the_checkpoint_phrases_are_pinned_in_every_copy commit=90a936e39f899655e48dddb9d2ed8287222d92e8 build=reused filter=/*/*/*/the_checkpoint_phrases_are_pinned_in_every_copy executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/c1015-baseline/BASE-the_checkpoint_phrases_are_pinned_in_every_copy-20261003-211009-fc3c/run.trx slot=granted waited=0s dirty=0 source=90a936e39f899655e48dddb9d2ed8287222d92e8 sourceState=clean buildSource=verified
CHECKPOINT BASE-the_worst_case_composition_measured_sits_far_under_the_budget commit=90a936e39f899655e48dddb9d2ed8287222d92e8 build=reused filter=/*/*/*/the_worst_case_composition_measured_sits_far_under_the_budget executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/c1015-baseline/BASE-the_worst_case_composition_measured_sits_far_under_the_budget-20261003-211017-f927/run.trx slot=granted waited=0s dirty=0 source=90a936e39f899655e48dddb9d2ed8287222d92e8 sourceState=clean buildSource=verified
CHECKPOINT BASE-Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes commit=90a936e39f899655e48dddb9d2ed8287222d92e8 build=reused filter=/*/*/*/Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/c1015-baseline/BASE-Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes-20261003-211026-2fde/run.trx slot=granted waited=0s dirty=0 source=90a936e39f899655e48dddb9d2ed8287222d92e8 sourceState=clean buildSource=verified
CHECKPOINT BASE-Review_guidance_preserves_every_instruction commit=90a936e39f899655e48dddb9d2ed8287222d92e8 build=reused filter=/*/*/*/Review_guidance_preserves_every_instruction executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/c1015-baseline/BASE-Review_guidance_preserves_every_instruction-20261003-211033-259d/run.trx slot=granted waited=0s dirty=0 source=90a936e39f899655e48dddb9d2ed8287222d92e8 sourceState=clean buildSource=verified
CHECKPOINT BASE-C544_ExecutionEvidenceContract commit=90a936e39f899655e48dddb9d2ed8287222d92e8 build=reused filter=/*/*/*/C544_ExecutionEvidenceContract executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/c1015-baseline/BASE-C544_ExecutionEvidenceContract-20261003-211042-08f7/run.trx slot=granted waited=0s dirty=0 source=90a936e39f899655e48dddb9d2ed8287222d92e8 sourceState=clean buildSource=verified
CHECKPOINT BASE-C544_FinalReviewContract commit=90a936e39f899655e48dddb9d2ed8287222d92e8 build=reused filter=/*/*/*/C544_FinalReviewContract executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/c1015-baseline/BASE-C544_FinalReviewContract-20261003-211049-0d94/run.trx slot=granted waited=0s dirty=0 source=90a936e39f899655e48dddb9d2ed8287222d92e8 sourceState=clean buildSource=verified
CHECKPOINT BASE-repeat_budget_reaches_code_briefs_without_bundle_growth commit=90a936e39f899655e48dddb9d2ed8287222d92e8 build=reused filter=/*/*/*/repeat_budget_reaches_code_briefs_without_bundle_growth executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-bc463e02/.antiphon/c1015-baseline/BASE-repeat_budget_reaches_code_briefs_without_bundle_growth-20261003-211057-3d88/run.trx slot=granted waited=0s dirty=0 source=90a936e39f899655e48dddb9d2ed8287222d92e8 sourceState=clean buildSource=verified

### Latest actual guard facts before this report commit

EVIDENCE range base=90a936e39f899655e48dddb9d2ed8287222d92e8 head=07f6c4fb029d4a0652104f105e18f66da0bf8014
EVIDENCE result commits=15 entries=289 violations=0 base=90a936e39f899655e48dddb9d2ed8287222d92e8 head=07f6c4fb029d4a0652104f105e18f66da0bf8014
EVIDENCE inventory base=90a936e39f899655e48dddb9d2ed8287222d92e8 suppliedCount=289 suppliedBytes=37541892 suppliedDigest=4e0a2ec36b9817c7cba9a2d5e05e85ecaa3f5a23b56daeb7469b3fd55b7684ed count=289 bytes=37541892 digest=4e0a2ec36b9817c7cba9a2d5e05e85ecaa3f5a23b56daeb7469b3fd55b7684ed anchor=f5082847ba6f5e2b1db50b407ff80b7f8cb36575ef9fe8f90c825c7b4ca09c2b kept=23 head=07f6c4fb029d4a0652104f105e18f66da0bf8014
EVIDENCE deletion result base=90a936e39f899655e48dddb9d2ed8287222d92e8 deletion=37ddd0501605d7117736436fa6cd794f572ce02a head=07f6c4fb029d4a0652104f105e18f66da0bf8014 deleted=289 bytes=37541892 digest=4e0a2ec36b9817c7cba9a2d5e05e85ecaa3f5a23b56daeb7469b3fd55b7684ed kept=23 finalTreeViolations=0 recoverable=289 violations=0

## Every Mutation control remains pending

No deliberate source mutant, red/restore/green cycle or control discharge occurred. Every variant/vector named in each frozen row below remains pending for method-scoped post-land SourceLanding Mutation, including zero/multiple, case/path/mode/size, stage ASCII/cap and both parameter-mode variants. Ordinary positive/negative fixtures and static coverage bindings do not discharge controls.

| PC / guard | Exact method | Decisive assertion / variants | Status |
|---|---|---|---|
| PC-1 / G-1 | `EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants` | root-case: ExitCode == 1 | pending, every variant |
| PC-2 / G-2 | `EvidenceDiffGuardTests.Rejects_checkpoint_paths` | nested-checkpoint: ExitCode == 1 | pending, every variant |
| PC-3 / G-3 | `EvidenceDiffGuardTests.Rejects_checkpoint_paths` | prefixed-checkpoint: ExitCode == 1 | pending, every variant |
| PC-4 / G-4 | `EvidenceDiffGuardTests.Rejects_checkpoint_paths` | checkpoint-case: ExitCode == 1 | pending, every variant |
| PC-5 / G-5 | `EvidenceDiffGuardTests.Rejects_non_markdown_outputs` | format: ExitCode == 1 for tiny .trx.gz | pending, every variant |
| PC-6 / G-6 | `EvidenceDiffGuardTests.Rejects_non_regular_modes` | mode: ExitCode == 1 for the symlink; separately assert gitlink vector in ordinary run | pending, every variant |
| PC-7 / G-7 | `EvidenceDiffGuardTests.Enforces_one_mib_blob_limit` | oversize: ExitCode == 1 at 1048577 | pending, every variant |
| PC-8 / G-8 | `EvidenceDiffGuardTests.Enforces_one_mib_blob_limit` | utf8-bytes: ExitCode == 1 for multibyte oversize blob | pending, every variant |
| PC-9 / G-9 | `EvidenceDiffGuardTests.Grandfathers_unchanged_legacy_and_allows_deletion` | legacy-unchanged: ExitCode == 0 | pending, every variant |
| PC-10 / G-10 | `EvidenceDiffGuardTests.Grandfathers_unchanged_legacy_and_allows_deletion` | legacy-delete: ExitCode == 0 | pending, every variant |
| PC-11 / G-11 | `EvidenceDiffGuardTests.Rechecks_modified_and_renamed_legacy_paths` | legacy-modified: ExitCode == 1 | pending, every variant |
| PC-12 / G-12 | `EvidenceDiffGuardTests.Rechecks_modified_and_renamed_legacy_paths` | rename-destination: ExitCode == 1 | pending, every variant |
| PC-13 / G-13 | `EvidenceDiffGuardTests.Checks_intermediate_commits_even_when_tip_is_clean` | intermediate: ExitCode == 1 | pending, every variant |
| PC-14 / G-14 | `EvidenceDiffGuardTests.Checks_merge_side_history` | side-history: ExitCode == 1 | pending, every variant |
| PC-15 / G-15 | `EvidenceDiffGuardTests.Checks_merge_side_history` | merge-resolution: ExitCode == 1 | pending, every variant |
| PC-16 / G-16 | `EvidenceDiffGuardTests.Reads_pinned_git_objects_not_index_or_worktree` | committed-object: ExitCode == 1 with staged/working small bytes | pending, every variant |
| PC-17 / G-17 | `EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants` | literal-newline: ExitCode == 1 with the full escaped path | pending, every variant |
| PC-18 / G-18 | `EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants` | no-path-execution: canary file absent, checked before verdict details | pending, every variant |
| PC-19 / G-19 | `EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants` | escaped-path: serialized diagnostic contains escaped LF, no injected physical diagnostic line | pending, every variant |
| PC-20 / G-20 | `EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants` | no-payload: output excludes fixture payload sentinel | pending, every variant |
| PC-21 / G-21 | `EvidenceDiffGuardTests.Refuses_unverifiable_ranges_and_objects` | base-unresolved: ExitCode == 2 | pending, every variant |
| PC-22 / G-22 | `EvidenceDiffGuardTests.Refuses_unverifiable_ranges_and_objects` | head-unresolved: ExitCode == 2 | pending, every variant |
| PC-23 / G-23 | `EvidenceDiffGuardTests.Refuses_unverifiable_ranges_and_objects` | unrelated: ExitCode == 2 | pending, every variant |
| PC-24 / G-24 | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | git-exit: ExitCode == 2 and fault hit == 1 | pending, every variant |
| PC-25 / G-25 | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | raw-fields: ExitCode == 2 for an extra header token | pending, every variant |
| PC-26 / G-26 | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | invalid-size: ExitCode == 2 | pending, every variant |
| PC-27 / G-27 | `EvidenceDiffGuardTests.Resolves_refs_once_and_stays_read_only` | pinned-head: reported/inspected head equals the first resolved OID | pending, every variant |
| PC-28 / G-28 | `EvidenceDiffGuardTests.Resolves_refs_once_and_stays_read_only` | read-only: index bytes equal the independent before snapshot | pending, every variant |
| PC-29 / G-29 | `EvidenceDiffGuardTests.Ci_selects_cumulative_feature_push_range` | feature-cumulative: ExitCode == 1 for earlier unlanded bad commit | pending, every variant |
| PC-30 / G-30 | `EvidenceDiffGuardTests.Ci_selects_complete_default_branch_push_range` | default-whole-push: ExitCode == 1 for earlier bad commit | pending, every variant |
| PC-31 / G-31 | `EvidenceDiffGuardTests.Ci_selects_complete_default_branch_push_range` | event-head: resolved head equals fixture event after | pending, every variant |
| PC-32 / G-32 | `EvidenceDiffGuardTests.Ci_requires_valid_manual_range` | manual-base-required: ExitCode == 2 | pending, every variant |
| PC-33 / G-33 | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | unknown-event: ExitCode == 2 | pending, every variant |
| PC-34 / G-34 | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | event-shape: ExitCode == 2 | pending, every variant |
| PC-35 / G-35 | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | remote-base-missing: ExitCode == 2 | pending, every variant |
| PC-36 / G-36 | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | default-zero: ExitCode == 2 | pending, every variant |
| PC-37 / G-37 | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | shallow-history: ExitCode == 2 | pending, every variant |
| PC-38 / G-38 | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | deleted-type: ExitCode == 2 for string false | pending, every variant |
| PC-39 / G-39 | `EvidencePolicyWorkflowTests.Workflow_selects_every_required_event` | trigger-feature: branch patterns contain feat/** | pending, every variant |
| PC-40 / G-40 | `EvidencePolicyWorkflowTests.Workflow_selects_every_required_event` | no-path-filter: push paths/paths-ignore absent | pending, every variant |
| PC-41 / G-41 | `EvidencePolicyWorkflowTests.Workflow_pins_full_history_and_event_head` | full-history: fetch-depth == 0 | pending, every variant |
| PC-42 / G-42 | `EvidencePolicyWorkflowTests.Workflow_pins_full_history_and_event_head` | checkout-head: ref equals expected event/selected-head expression | pending, every variant |
| PC-43 / G-43 | `EvidencePolicyWorkflowTests.Workflow_calls_real_guard_and_propagates_failure` | real-guard-call: AST invokes scripts/check-evidence-diff.ps1 with event path/name inputs | pending, every variant |
| PC-44 / G-44 | `EvidencePolicyWorkflowTests.Workflow_calls_real_guard_and_propagates_failure` | exit-propagation: exit AST uses LASTEXITCODE | pending, every variant |
| PC-45 / G-45 | `EvidencePolicyWorkflowTests.Workflow_calls_real_guard_and_propagates_failure` | no-error-waiver: neither job nor step enables continue-on-error | pending, every variant |
| PC-46 / G-46 | `EvidencePolicyWorkflowTests.Workflow_uses_data_arguments_and_no_write_permissions` | no-event-shell: run AST/source contains no GitHub expression interpolation | pending, every variant |
| PC-47 / G-47 | `EvidencePolicyWorkflowTests.Workflow_uses_data_arguments_and_no_write_permissions` | read-permission: contents == read | pending, every variant |
| PC-48 / G-48 | `InstructionBundleTests.C1015_Composed_workers_keep_generated_evidence_untracked` | common-ignored: all four compositions contain the ignored-evidence rule | pending, every variant |
| PC-49 / G-49 | `InstructionBundleTests.C1015_Composed_workers_keep_generated_evidence_untracked` | common-cap: all four compositions contain the 1048576-byte cap | pending, every variant |
| PC-50 / G-50 | `InstructionBundleTests.C1015_Code_keeps_range_guard_and_exact_source` | code-range: stage Code and its composition name check-evidence-diff.ps1 and full task range | pending, every variant |
| PC-51 / G-51 | `InstructionBundleTests.C1015_Code_keeps_range_guard_and_exact_source` | code-source: stage Code contains actual tested SHA requirement | pending, every variant |
| PC-52 / G-52 | `InstructionBundleTests.C1015_Review_remains_read_only_and_checks_history` | review-read-only: composed Review contains both prohibitions | pending, every variant |
| PC-53 / G-53 | `InstructionBundleTests.C1015_Review_remains_read_only_and_checks_history` | review-range: stage Review names checker and complete candidate range | pending, every variant |
| PC-54 / G-54 | `InstructionBundleTests.C1015_SourceLanding_keeps_its_external_evidence_exception` | source-external: composed Mutation contains assigned external evidence root requirement | pending, every variant |
| PC-55 / G-55 | `InstructionBundleTests.C1015_SourceLanding_keeps_its_external_evidence_exception` | source-no-commit: exception text within composed basics contains never commit/push | pending, every variant |
| PC-56 / G-56 | `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` | existing stage Length.ShouldBeLessThanOrEqualTo(2500), Code argument result | pending, every variant |
| PC-57 / G-57 | `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` | existing character code ShouldBeLessThan(128), Review argument result | pending, every variant |
| PC-58 / G-58 | `InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget` | existing composed.Text.Length.ShouldBeLessThan(budget) | pending, every variant |
| PC-59 / G-59 | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | inventory-count: ExitCode == 1 with correct digest but wrong count | pending, every variant |
| PC-60 / G-60 | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | inventory-digest: ExitCode == 1 with correct count but wrong digest | pending, every variant |
| PC-61 / G-61 | `EvidenceDeletionGuardTests.Rejects_ambiguous_or_merge_deletion` | deletion-unique: ExitCode == 1 with two candidates | pending, every variant |
| PC-62 / G-62 | `EvidenceDeletionGuardTests.Rejects_ambiguous_or_merge_deletion` | deletion-parent: ExitCode == 1 for a marked merge with otherwise exact deletions | pending, every variant |
| PC-63 / G-63 | `EvidenceDeletionGuardTests.Refuses_unverifiable_deletion_history` | inventory-ancestry: ExitCode == 2 for a graft-free unrelated inventory with identical entries | pending, every variant |
| PC-64 / G-64 | `EvidenceDeletionGuardTests.Refuses_unverifiable_deletion_history` | deletion-ancestry: ExitCode == 2; cut hit == 1 | pending, every variant |
| PC-65 / G-65 | `EvidenceDeletionGuardTests.Rejects_missing_extra_or_non_deletions` | deletion-only: ExitCode == 1 for exact required deletions plus one addition | pending, every variant |
| PC-66 / G-66 | `EvidenceDeletionGuardTests.Rejects_missing_extra_or_non_deletions` | deletion-missing: ExitCode == 1 when S3 omits a required path that a later unmarked commit deletes; all final-tree checks otherwise pass | pending, every variant |
| PC-67 / G-67 | `EvidenceDeletionGuardTests.Rejects_missing_extra_or_non_deletions` | deletion-extra: ExitCode == 1 for D(B) plus one permitted Markdown deletion, restored identically after S3; outside sentinel vector remains ordinary coverage | pending, every variant |
| PC-68 / G-68 | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | deletion-old-oid: ExitCode == 1 after editing a legacy blob before deletion | pending, every variant |
| PC-69 / G-69 | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | deletion-old-mode: ExitCode == 1 after 100644 to 100755 change with identical blob | pending, every variant |
| PC-70 / G-70 | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | deletion-resurrection: ExitCode == 1 after a deleted oversize Markdown path returns with permitted small bytes; final rule scan alone passes | pending, every variant |
| PC-71 / G-71 | `EvidenceDeletionGuardTests.Refuses_unverifiable_deletion_history` | deletion-recoverable: ExitCode == 2 after the final-tree observation cut removes a fixture-owned loose deleted blob; metadata/classification already succeeded and cut hit == 1 | pending, every variant |
| PC-72 / G-72 | `EvidenceDeletionGuardTests.Verifies_exact_legacy_deletion` | deletion-read-only: index digest equals independent before snapshot | pending, every variant |
| PC-73 / G-73 | `EvidenceDiffGuardTests.Allows_small_markdown_and_other_source` | root-scope: ExitCode == 0 for .antiphon-other and nested x/.antiphon | pending, every variant |
| PC-74 / G-74 | `EvidenceDiffGuardTests.Rejects_checkpoint_paths` | checkpoint-leaf: ExitCode == 0 for .antiphon/checkpoints.md | pending, every variant |
| PC-75 / G-75 | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | raw-terminator: ExitCode == 2 with otherwise valid tiny Markdown record | pending, every variant |
| PC-76 / G-76 | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | raw-status: ExitCode == 2 for malformed status | pending, every variant |
| PC-77 / G-77 | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | raw-object-id: ExitCode == 2 before object lookup | pending, every variant |
| PC-78 / G-78 | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | raw-mode: ExitCode == 2 | pending, every variant |
| PC-79 / G-79 | `EvidencePolicyWorkflowTests.Workflow_selects_every_required_event` | trigger-master: branch patterns contain master | pending, every variant |
| PC-80 / G-80 | `EvidencePolicyWorkflowTests.Workflow_selects_every_required_event` | trigger-manual: workflow_dispatch node exists | pending, every variant |
| PC-81 / G-81 | `EvidencePolicyWorkflowTests.Workflow_calls_real_guard_and_propagates_failure` | event-path-binding: AST EventPath argument uses the expected environment-backed path | pending, every variant |
| PC-82 / G-82 | `EvidencePolicyWorkflowTests.Workflow_calls_real_guard_and_propagates_failure` | event-name-binding: AST EventName argument uses the event-name environment variable | pending, every variant |
| PC-83 / G-83 | `EvidencePolicyWorkflowTests.Workflow_uses_data_arguments_and_no_write_permissions` | manual-base-binding: YAML env and AST argument preserve the explicit input | pending, every variant |
| PC-84 / G-84 | `InstructionBundleTests.C1015_Composed_workers_keep_generated_evidence_untracked` | common-individual: composed common allowance requires individual report paths | pending, every variant |
| PC-85 / G-85 | `InstructionBundleTests.C1015_Composed_workers_keep_generated_evidence_untracked` | common-directory: composed common allowance excludes checkpoint directories | pending, every variant |
| PC-86 / G-86 | `InstructionBundleTests.C1015_Code_keeps_range_guard_and_exact_source` | code-verbatim: stage Code requires unedited CHECKPOINT lines | pending, every variant |
| PC-87 / G-87 | `EvidenceDeletionGuardTests.Rejects_ambiguous_or_merge_deletion` | deletion-trailer: ExitCode == 1 with only a CARD-10150 near-match marker | pending, every variant |
| PC-88 / G-88 | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | raw-encoding: ExitCode == 2 for an invalid-byte tiny Markdown path | pending, every variant |
| PC-89 / G-89 | `EvidenceDiffGuardTests.Checks_merge_side_history` | merged-root: ExitCode == 1 and introducing root SHA is reported although the root lineage later deleted the artifact | pending, every variant |
| PC-90 / G-90 | `EvidenceDeletionGuardTests.Verifies_exact_legacy_deletion` | inventory-rule-set: InventoryOnly ordered rejected paths equal the independent B oracle, including fixture newer-pre-base.log; check this assertion before aggregate diagnostics | pending, every variant |
| PC-91 / G-91 | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | inventory-bytes: ExitCode == 1 with correct count/digest and wrong byte sum | pending, every variant |
| PC-92 / G-92 | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | inventory-pinned-base: ExitCode == 1 after a non-anchor artifact receives same-length new bytes before S3; count/path digest/byte sum stay correct | pending, every variant |
| PC-93 / G-93 | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | anchor-present: InventoryOnly ExitCode == 1 when B omits one permitted original task report; immutable anchor signature otherwise matches | pending, every variant |
| PC-94 / G-94 | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | anchor-oid: InventoryOnly ExitCode == 1 after B replaces one permitted original report with same-length permitted bytes; path/mode/classification remain valid | pending, every variant |
| PC-95 / G-95 | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | anchor-mode: InventoryOnly ExitCode == 1 after B changes an allowed original report from 100644 to 100755 with identical OID | pending, every variant |
| PC-96 / G-96 | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | anchor-classification: InventoryOnly ExitCode == 1 when the low-level size-result cut supplies an oversize size for one allowed original report only during immutable-anchor classification; B metadata stays native, other identity checks pass, cut hit == 1 | pending, every variant |
| PC-97 / G-97 | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | final-tree-policy: deletion-validator ExitCode == 1 after a new non-D(B) forbidden file is added after exact S3; no history checker is invoked before this assertion | pending, every variant |
| PC-98 / G-98 | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | kept-present: ExitCode == 1 after a permitted B report is deleted in a later unmarked commit; S3 and final rule compliance otherwise pass | pending, every variant |
| PC-99 / G-99 | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | kept-oid: ExitCode == 1 after a permitted report gets different permitted bytes after S3 | pending, every variant |
| PC-100 / G-100 | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | kept-mode: ExitCode == 1 after a permitted report changes only from 100644 to 100755 after S3 | pending, every variant |
| PC-101 / G-101 | `EvidenceDeletionGuardTests.Refuses_unverifiable_deletion_history` | tree-fields: InventoryOnly ExitCode == 2 for one malformed B tree record, with otherwise valid OID/mode/size/path and permissive downstream provider; cut hit == 1 | pending, every variant |

## Durable slices

1fea9304249684d5f6e732f1590e9402f5789427 docs(CARD-1015): record admission collision; implementation and tests unstarted
9ea6735b1765fa07cf2ae999f48b661d7030d17d test(CARD-1015): add localized composed-policy witnesses; red-first pending
bd656f91fe632f6527109ca7d81ebbd15577b1bb test(CARD-1015): fix Shouldly message binding; baseline assertions still pending
0f17c25331473a4dbc077389babbcb575d9d8a5a docs(CARD-1015): localize publication versus evidence-residue policy
b8a20df5b81404fdcc82faab2669c7b23d8d322a feat(CARD-1015): state evidence boundary and exact-source policy; Final pending
d76cf7b4049da4047ca7f6f9ecb37a92f09f2849 test(CARD-1015): add history and actual-workflow fixtures; baseline red pending
0e87857bfed59df6e4ef9eb15daca84bef65ba80 test(CARD-1015): add independently anchored deletion fixtures; baseline red pending
5700c1dd1c91c54f6b1c198e77e8841014361da0 feat(CARD-1015): enforce proposed evidence history in CLI and CI; baseline 28 methods red, Final pending
0145516f904b36c9aed41b926f823a33ce74a333 feat(CARD-1015): validate rule-derived deletion and preservation; Final pending
37ddd0501605d7117736436fa6cd794f572ce02a chore(CARD-1015): delete exactly 289 rule-rejected evidence paths; history retained
44d7b8c19a7a9549fe2dd74feb224229acc46801 test(CARD-1015): bind literal guard labels to vector outcomes for coverage lint
3c761d7b18c8330f62dd5127b66f4c2677bb6799 test(CARD-1015): complete pinned-base and both-mode boundary vectors; Final pending
819596d9bcbaa7d024fe88d8af3e0605011e1f73 fix(card-1015): repair Unit filter and emit single plain-text fault markers
f903232b89d2c391ea478763d759167f94c155c0 fix(card-1015): preserve pinned instruction contracts within existing budgets
07f6c4fb029d4a0652104f105e18f66da0bf8014 fix(card-1015): restore remaining handoff pins and account for quoted budget

## Reproduce and next owner

Mandatory recorded inputs (same B/aggregates for Review and actual landed-master acceptance):

```sh
export C1015_BASE_SHA=90a936e39f899655e48dddb9d2ed8287222d92e8
export C1015_DELETE_PATH_SHA256=4e0a2ec36b9817c7cba9a2d5e05e85ecaa3f5a23b56daeb7469b3fd55b7684ed
export C1015_DELETE_COUNT=289
export C1015_DELETE_BYTES=37541892
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1015-importer -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1015-tool/ --property:UseAppHost=false
dotnet tools/Antiphon.Checkpoints/bin-c1015-tool/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-03-card-1015-evidence-git-policy-plan.md
dotnet tools/Antiphon.Checkpoints/bin-c1015-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-03-card-1015-evidence-git-policy-plan.md --expected-source-sha <exact-pushed-tip> --max-wait 50s
dotnet tools/Antiphon.Checkpoints/bin-c1015-tool/Antiphon.Checkpoints.dll wait --run <run-id> --max-wait 50s
```

Wait until not exit 75; never bypass a slot. The revised closed table has the original seven ordinary rows plus eight specifically justified full-class repair/adjacent rows. A fresh Final Review qualifies the actual publication tip. Code's single whole-Unit execution is not relabeled with the repair/report SHA.

This individually committed 54+ KiB Markdown report is allowed by D-1; no generated directory or raw artifact is added. Immediately after its push, Code reruns only CP-6/CP-7 through the checkpoint tool at committed HEAD. Those fresh unedited lines and resolved final SHA are in the final caller message/local publication receipt; do not make a recurring report-commit/qualification loop. The tracked report is frozen before Final Review.

Evidence paths, all under /work/worktrees/task-bc463e02:

- .antiphon/task-bc463e02.md: this durable compact report and essential receipts.
- .antiphon/checkpoints/20261003-205229-3106: single whole-Unit and full Git/smoke raw receipts/TRX.
- .antiphon/checkpoints/20261003-212229-b54c: final clean repair/adjacent class receipts/TRX.
- .antiphon/c1015-red-first: red-first compiler/installation/assertion records.
- .antiphon/c1015-baseline: exact-method seven-case immutable-base confirmations; initial zero selection separately uncredited.
- .antiphon/c1015-bc463e02-admission: independent inventory, staged proof, coverage bindings/lint and fresh roster reconciliation.
- docs/investigations/2026-10-03-card-1015-code-admission-bc463e02.md: original durable admission facts, superseded blocked disposition.

Raw artifacts gain no durability guarantee; essential source/count/receipt facts are stored here. Own alternate output directories and detached baseline verification worktree are disposed before settlement; no unrelated process/root is touched.

Next: ordinary read-only Final Review. Caller then lands original Code owner bc463e02-3fd6-4c05-9883-0ef2f876aaf2, repeats actual-master CP-6/CP-7 with the same B/inventory (record rebased S3 identity), activates server bundles and observes SHA-bound hosted Actions plus loaded bundle hashes. Restart: server after land; runner: none. No restart now. Only afterward commission method-scoped SourceLanding Mutation for PC-1..PC-101 and every separately named variant.
