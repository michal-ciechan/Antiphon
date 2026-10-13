# CARD-1032 S4 repair verification

Task ebf0a2f0-49cb-4525-95b7-1d2dcffecd2f is the new Code landing owner.
Base: d7d27ae327a4397fe061cbb17626e249fc7d7190. Original implementation
owner: 8bcfa5da-1c58-477c-bf97-65f83a299509. Repair only; S5–S8 remain deferred.

C1032_InspectionHoldShapes has 42 hand-written shape/reason arguments.
C1032_InspectionReadOnlyGitCalls checks the argument allowlist, optional-lock
setting, mtime-only index preservation, proof reads and absence of retirement state.
C1032_InspectionReadOnlyBound checks bound identity and the clean positive path;
C1032_InspectionSourceProof checks actual advanced HEAD and containment.

## HOLD table

Every HOLD row below names an argument of C1032_InspectionHoldShapes.
Eligible is an inspection snapshot; C1032_InspectionSourceProof separately checks
preservation admission. Native Windows junction/locked-file qualification remains
parent acceptance; the link argument exercises a scratch directory symlink on Linux.

| Shape / test argument | Required verdict / reason |
|---|---|
| assume-clean | HOLD tracked_index_flags |
| assume-dirty | HOLD tracked_index_flags |
| skip-clean | HOLD tracked_index_flags |
| skip-dirty | HOLD tracked_index_flags |
| both-flags | HOLD tracked_index_flags |
| flagged-newline-path | HOLD tracked_index_flags |
| ignored-bare | HOLD nested_repository |
| bare | HOLD nested_repository |
| admin-index.lock | HOLD repository_locked |
| common-index.lock | HOLD repository_locked |
| admin-HEAD.lock | HOLD repository_locked |
| common-packed-refs.lock | HOLD repository_locked |
| common-shallow.lock | HOLD repository_locked |
| common-gc.pid | HOLD repository_locked |
| admin-gc.pid | HOLD repository_locked |
| common-refs/heads/side.lock | HOLD repository_locked |
| admin-custom.lock | HOLD repository_locked |
| merge | HOLD unfinished_operation |
| rebase-merge | HOLD unfinished_operation |
| rebase-apply | HOLD unfinished_operation |
| CHERRY_PICK_HEAD | HOLD unfinished_operation |
| REVERT_HEAD | HOLD unfinished_operation |
| BISECT_LOG | HOLD unfinished_operation |
| sequencer | HOLD unfinished_operation |
| common-MERGE_HEAD | HOLD unfinished_operation |
| tracked | HOLD dirty_source |
| staged | HOLD dirty_source |
| untracked | HOLD dirty_source |
| protected | HOLD protected_content |
| evidence | HOLD evidence_requires_admission |
| link | HOLD linked_content |
| nested | HOLD nested_repository |
| ignored-nested | HOLD nested_repository |
| gitlink | HOLD submodule_content |
| locked-registration | HOLD locked_registration |
| prunable-registration | HOLD stale_mirror_binding |
| sparse | HOLD sparse_checkout |
| detached | HOLD source_ref_mismatch |
| unborn | HOLD inspection_unreadable |
| shallow | HOLD shallow_repository |
| other-repository | HOLD stale_mirror_binding |
| case-twin | HOLD case_ambiguous_content |
| clean bound | May be Eligible: C1032_InspectionReadOnlyBound |
| mtime-only | May be Eligible: C1032_InspectionReadOnlyGitCalls |
| HEAD differs from creation baseline | May be Eligible; actual HEAD: C1032_InspectionSourceProof |

## Verification design

CP-6's frozen prefix now executes 46 results: original three methods, 42 shape
arguments and one read-only Git-call method. Run CP-6 first on the tests-only
commit to demonstrate D1/D2/D3 against unchanged base production, then the full
closed selection below on the fix commit. Two isolated builds are needed because
S2 capture/join uses the server test project; all same-project rows reuse output.
No build ID is named tests. UseAppHost=false is supplied by the checkpoint driver.

S4-unit is a declared higher-priority Final-profile supplement to the brief's
Unit exclusion. Invariant: existing runner Unit behavior with inspector unconfigured.
Its scope is every Unit class in Antiphon.SessionRunner.Tests; cost 1–3 minutes.
S4-classes selects only the two named full affected classes, cost about 3 minutes.
No whole-assembly test is selected. Existing Windows-only Unit reds require exact
base-case confirmation; an unchanged broad rerun is not authorized.

V-1: S2 capture/identity regressions CP-3/4/17–24; S7/S8 obligations deferred.
V-2: S4 read-only argument/state evidence CP-6; wire/receipt/composition obligations
CP-1/2/11/14/15 deferred. V-3: CP-6 plus full classes; destructive preservation
CP-8/9/13 deferred. V-4: CP-5/25/26 plus full classes; S5 owner tests deferred.
V-5 and V-6: S5–S8 removal/recovery/coordinator obligations deferred, not passed.
R-1: CP-24/26/27 and full classes; desktop-sync CP-16 deferred.
R-2: CP-3/23/24 and full classes; S6/S8 composition deferred.
R-3: destructive parent witnesses deferred to S5–S8, not passed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-6 | S4-repair | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-s4r-runner/` | bound-inspection | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_Inspection*` | V-3 | all 46 results; 0 failed/skipped after fix | 46 | 5 | true |
| CP-3 | S4-repair | CP-6 | runner-binding | `/*/*/RunnerWorkspaceBindingTests/C1032_Binding*` | V-1, R-2 | all 5; 0 failed/skipped | 5 | 1 | true |
| CP-4 | S4-repair | `tests/Antiphon.Tests -> bin-c1032-s4r-server/` | server-binding | `/*/*/(CardWorktreeMirrorBindingTests*)\|(RemoteWorkspacePreparerTests*)/C1032_Capture*` | V-1 | all 4; 0 failed/skipped | 4 | 5 | true |
| CP-5 | S4-repair | CP-6 | use-fence | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_Fence*` | V-4 | all 6 once; 0 failed/skipped | 6 | 1 | true |
| CP-17 | S4-repair | CP-6 | foreign-owner | `/*/*/RunnerWorkspaceBindingTests/C1032_ForeignOwnerAdoptionRefused*` | V-1 | all 1; 0 failed/skipped | 1 | 1 | true |
| CP-18 | S4-repair | CP-6 | absent-replay | `/*/*/RunnerWorkspaceBindingTests/C1032_AbsentMirrorReplayRefused*` | V-1 | all 1; 0 failed/skipped | 1 | 1 | true |
| CP-19 | S4-repair | CP-6 | store-mismatch | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreMismatch*` | V-1 | all 1; 0 failed/skipped | 1 | 1 | true |
| CP-20 | S4-repair | CP-4 | store-replacement | `/*/*/RemoteWorkspacePreparerTests/C1032_StoreReplacement*` | V-1 | all 1; 0 failed/skipped | 1 | 1 | true |
| CP-21 | S4-repair | CP-4 | mirror-join | `/*/*/PhoneHomeMirrorJoinTests/C1032_Join*` | V-1 | all 6; 0 failed/skipped | 6 | 3 | true |
| CP-22 | S4-repair | CP-6 | store-identity | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentity*` | V-1 | all 10; 0 failed/skipped | 10 | 1 | true |
| CP-23 | S4-repair | CP-6 | legacy-unbound | `/*/*/RunnerWorkspaceBindingTests/C1032_LegacyUnboundPathOnlyContract*` | R-2 | all 1; 0 failed/skipped | 1 | 1 | true |
| CP-24 | S4-repair | CP-6 | workspace-compatibility | `/*/*/PhoneHomeCommandDispatcherTests/Workspace_*` | R-1, R-2 | all 3; 0 failed/skipped | 3 | 1 | true |
| CP-25 | S4-repair | CP-6 | held-removal | `/*/*/LegacyWorkspaceRemovalGateTests/(C1032_LegacyRemoveHeldLease*)\|(C1032_LegacyPruneHeldLease*)` | V-4 | all 2; 0 failed/skipped | 2 | 1 | true |
| CP-26 | S4-repair | CP-6 | removal-repair-class | `/*/*/LegacyWorkspaceRemovalGateTests/*` | V-4, R-1, R-2 | all 11; 0 failed/skipped | 11 | 2 | true |
| CP-27 | S4-repair | CP-6 | workspace-service-class | `/*/*/RunnerWorkspaceServiceTests/*` | R-1, R-2 | all 33; 0 failed/skipped | 33 | 2 | true |
| S4-classes | S4-repair | CP-6 | affected-classes | `/*/*/(CompletedCardWorkspaceRemovalTests*)\|(RunnerWorkspaceServiceTests*)/*` | V-3, V-4, R-1, R-2 | all 85; 0 failed/skipped | 85 | 3 | true |
| S4-unit | S4-repair | CP-6 | final-unit | `/*/*/*/*[Category=Unit]` | R-1, R-2 | every Unit result, disclose inherited reds | 378 | 3 | true |

## Mutation handoff

All prior 302 PC IDs and variants remain pending. These additional controls are
also pending for method-scoped SourceLanding Mutation, after Review and landing.
No Code regression failure discharges a PC.

| PC / variants | Deliberate defect | Detecting filter | Expected red | State |
|---|---|---|---|---|
| PC-561: assume-clean, assume-dirty | Ignore lowercase ls-files tags | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | tracked_index_flags reason differs | pending |
| PC-562: skip-clean, skip-dirty, both-flags, flagged-newline-path | Ignore S/s or split filenames on whitespace | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | reason or HeldPaths differs | pending |
| PC-563: bare, ignored-bare | Omit HEAD/objects/refs recognition | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | nested_repository reason differs | pending |
| PC-564: every admin/common *.lock argument (7) | Omit recursive lock inventory | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | repository_locked reason differs | pending |
| PC-565: admin-gc.pid, common-gc.pid | Ignore gc.pid | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | repository_locked reason differs | pending |
| PC-566: merge, common-MERGE_HEAD, rebase-merge, rebase-apply, CHERRY_PICK_HEAD, REVERT_HEAD, BISECT_LOG, sequencer | Omit each operation marker separately | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | unfinished_operation reason differs | pending |
| PC-567: sparse | Omit sparse-checkout config hold | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | sparse_checkout reason differs | pending |
| PC-568: shallow | Omit shallow-repository hold | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | shallow_repository reason differs | pending |
| PC-569: gitlink | Ignore committed unpopulated gitlink | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | submodule_content reason differs | pending |
| PC-570: case-twin | Omit case-insensitive sibling check | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | case_ambiguous_content reason differs | pending |
| PC-571 | Omit GIT_OPTIONAL_LOCKS=0 | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionReadOnlyGitCalls*` | environment assertion fails | pending |
| PC-572 | Add a mutating Git invocation / index refresh | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionReadOnlyGitCalls*` | allowlist/digest assertion fails | pending |

Mutation must bind exact method filters when executing these parameterized methods;
the wildcards above are ordinary checkpoint discovery filters, not permission to
widen a PC to a class. Native Windows qualification remains deferred, not passed.

## Shared recognition boundary

Fetched origin/master 6e58767debf33d6a09c4e88050095f1cfdf76332 has
WorktreeNestedRepositories.HasMarker recognizing only .git; CARD-1211's bare
recognition commit 021f14732 is not ancestral to that target. This repair places
filesystem-only .git or HEAD-file/objects-directory/refs-directory recognition in
SessionRunner.Contracts.RepositoryDirectoryShape, exercised by the bare/nested
arguments of C1032_InspectionHoldShapes. CARD-1211 should converge to this shared
helper rather than duplicate a different bare recognition rule. No server
production dependency or cleanup behavior is edited by this S4 repair.
