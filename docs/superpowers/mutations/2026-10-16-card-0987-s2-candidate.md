# CARD-0987 S2 unbound Mutation candidate

Original Code / landing owner: `aed84ae3-c380-4961-af36-76fb65e86402`.
Implementation source: `a2c28d3fdffe7746e539a51e249e626e7f59e081`.
Branch: `feat/card-task-aed84ae3`. Base: `9ec9a4542d6beead31b133ed9f45b7effd361d16`.
Plan: [S2 extraction refresh](../plans/2026-10-16-card-0987-s2-extraction-refresh-plan.md).
Retained ledger: [frozen verification design](../plans/2026-10-15-card-0987-test-design.md).

This is an unbound candidate, not a Mutation execution pack. Every **PC-1..422** and
every frozen variant is **pending SourceLanding Mutation**. That includes PC-414's
four (pendingTemp, missingDirectory) pairs and PC-415..421 below. Code ran no deliberate
mutants. Mutation owns missing-control discovery, exact landed-source bindings and
method-scoped break/red/restore/green. Ordinary green does not discharge a control.

## New controls

Use `/*/*/HostCleanupCheckpointExtractionTests/<exact method>` for the three extraction
methods and `/*/*/CheckpointOwnedOutputScriptTests/<exact method>` for the two script methods.
Each selection executes one result; literal matrices remain internal variants.

| PC / variants | Production boundary at implementation source | Exact method | Required assertion failure |
|---|---|---|---|
| PC-415 | `src/Antiphon.HostCleanup/Checkpoints/CheckpointOutputNames.cs:22`; remove the segment conjunct from isolated admission | `Naming_policy_and_facades_preserve_admission` | `C987.Naming_policy_and_facades_preserve_admission.bin-a..b.shared-isolated` expects false. |
| PC-416 / instance, static | `tools/Antiphon.Checkpoints/Cleanup/OutputCleanup.cs:20,236`; bypass each isolated-name facade for `bin-a..b`, one mutant at a time | `Naming_policy_and_facades_preserve_admission` | Same prefix plus `bin-a..b.instance-isolated` or `bin-a..b.static-isolated` expects false. The literal segment booleans also cover both facade predicates. |
| PC-417 / root, stamp-parent | `src/Antiphon.HostCleanup/Checkpoints/SafeDelete.cs:160,157`; bypass each naming veto separately | `Naming_policy_and_facades_preserve_admission` | `C987.Naming_policy_and_facades_preserve_admission.root-False.admission` / `stamp-False.admission` expects Refused. Each fixture has valid context custody, checks the reached link observation, exact refusal, intact sentinel and zero unlink; valid paired roots/stamps remove successfully. |
| PC-418 | `src/Antiphon.HostCleanup/Checkpoints/SafeDelete.cs:59,90`; use native/default registry at FileOnly match | `File_only_delete_uses_supplied_registry` | `C987.File_only_delete_uses_supplied_registry.owned.removed` expects Deleted, plus verified open and exactly one virtual unlink. Fresh context over the same bytes reaches the creation-record refusal and retains the sentinel. |
| PC-419 | `src/Antiphon.HostCleanup/Checkpoints/ExclusiveDirectory.cs:21`; return success for existing-directory error | `C987_StandaloneNativeSource` | Named child assertion `C987.C987_StandaloneNativeSource.native.collision` expects pre-existing-output. The outer ScriptHarness success assertion fails with that explicit FAIL row; compilation and first creation must have succeeded. |
| PC-420 | `scripts/lib/checkpoint-owned-output.ps1:10`; restore the obsolete source path | `C987_OwnedOutputHelper` | `C987.C987_OwnedOutputHelper.helper.source-load` fails with the observed read/compile error. Outer host compiles; child failure is recorded by Assert-C487, not an unexplained fixture crash. |
| PC-421 | `src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj` plus compiling library use of a TUnit type | `Moved_types_have_one_library_owner` | `C987.Moved_types_have_one_library_owner.library.no-test-or-tool-dependency` rejects the actual assembly reference. A duplicate-definition compiler error is not red. |

The standalone source moved byte-for-byte. The fresh PowerShell child reads that source with
nullable enabled and no HostCleanup DLL. Its native directory identity probe compares Linux
device/inode or Windows volume/file-index before/after collision. Helper proof dot-sources the
real helper from an unrelated cwd, loads the driver's exact safe-write function ASTs without
executing the driver, and records the actual source read. Unexpected Git/build/process commands
throw. ScriptHarness owns the bounded child/join and private scratch.

## Frozen scope and sweep ports

The complete class now has 45 methods / 54 argument-expanded results. The original 14 tool-only
and 16 nested-custody subsets remain intact; the later 80 union remains S3's obligation.
The following 32 methods supply the remaining 27 sweep + 11 scope results. Their exact
arguments, original assertion labels and frozen mutant meanings remain binding.
All use `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointCompatibilityTests.cs`;
filter `/*/*/HostCleanupCheckpointCompatibilityTests/<exact method>`.

| Exact case | Binding / variant count | Oracle | Pending PC |
|---|---|---|---|
| `live_roots_are_skipped_without_taking_the_root_lock` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Callback registration failure is null; completed roots zero. | PC-177 |
| `uncertain_roots_are_skipped_without_taking_the_root_lock` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; `ProcessVerdict.Unknown`; `ProcessVerdict.ReusedPid`; 2 results | Both argument rows register without failure and retain payload. | PC-178 |
| `young_roots_are_skipped_without_taking_the_root_lock` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Lock file does not exist; skips[grace] == 1. | PC-179 |
| `root_lock_is_still_required_for_dead_candidates` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | CompletedRoots == 0; skips[root-busy] == 1; payload retained. | PC-180 |
| `eligibility_is_rechecked_after_the_root_lock` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; `"live"`; `"unknown"`; `"reused"`; `"grace"`; `"marker"`; `"inventory"`; 6 results | live arm: CompletedRoots == 0 at locked-owner-retained; payload/marker/index unchanged. | PC-119, PC-228, PC-229, PC-230, PC-242, PC-243 |
| `two_hundred_concurrent_allocations_ignore_the_sweep_gate` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | 200 allocations finish while virtual coordinator held; not waiting on release. | PC-181 |
| `grace_is_additional_to_dead_ownership` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | At grace minus one tick CompletedRoots == 0; at grace == 1. | PC-182 |
| `sweep_interval_is_shared_across_hosts` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Second instance Examined == 0; interval present and coordinator-busy absent. | PC-183 |
| `cursor_write_failure_does_not_fail_the_sweep` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Sweep returns normally with CompletedRoots == 1. | PC-184 |
| `sweep_removes_only_stale_index_temp_files` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Stale file absent and fresh file present. | PC-185 |
| `root_delete_count_is_bounded` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | CompletedRoots == 2; third remains. | PC-111 |
| `actual_deleted_bytes_are_bounded` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | ReclaimedBytes <= 8192; remaining payload length == 8192. | PC-186 |
| `direct_entry_scan_is_bounded` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | First Examined == 2; one root remains for second sweep. | PC-187 |
| `descendant_scan_is_bounded` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | CompletedRoots == 0 and inventory-incomplete-or-linked skip == 1. | PC-188 |
| `monotonic_deadline_stops_new_operations` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Zero-duration budget has Examined == 0, probe calls == 0, BudgetExhausted true. | PC-189 |
| `resumed_deletion_rechecks_every_veto` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Second pass CompletedRoots == 0 and payload length unchanged. | PC-190 |
| `marker_survives_partial_deletion` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | deleting marker exists after partial pass. | PC-110 |
| `cursor_prevents_new_arrival_starvation` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Five original roots absent after explicit three passes. | PC-191 |
| `absent_root_pointer_is_dropped_by_the_sweep` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | IndexExists == false at absent-pointer-dropped; CompletedRoots == 0. | PC-120 |
| `disposed_roots_do_not_consume_the_next_sweep_budget` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | With 700 absent entries plus orphan: Examined == 1, CompletedRoots == 1, orphan absent. | PC-192 |
| `eligible_backlog_drains_without_touching_exclusions` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`; 1 result | Four eligible roots absent after four bounded passes; live root retained. | PC-193 |
| `unfinished_registered_work_retains_roots` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | unfinished-work-roots-retained payload, marker and index remain on both roots. | PC-194 |
| `completed_faulted_work_reports_failure_and_deletes_roots` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | registered-fault-reported contains synthetic-registered-fault while both roots/index entries are removed. | PC-195 |
| `allocation_requires_an_owner` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | Expected InvalidOperationException is observed. | PC-196 |
| `root_is_registered_before_return` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | callback observes marker and indexed root before return. | PC-113, PC-245, PC-246 |
| `sealed_scope_awaits_registered_work` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | before explicit release, disposal incomplete and root exists. | PC-112 |
| `live_child_prevents_scope_deletion` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | identity-unknown failure receipt; virtual root remains. | PC-114 |
| `canceled_test_gets_a_fresh_cleanup_token` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | Virtual root removed despite canceled test token; no remaining cleanup debt. | PC-197 |
| `teardown_preserves_failure_and_attempts_other_roots` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | both failure reasons and second root removed. | PC-115 |
| `marker_failure_rolls_back_empty_root` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | Marker-write IOException observed and root absent. | PC-198 |
| `parallel_scopes_have_distinct_attempt_ownership` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | AttemptId and RootId differ. | PC-199 |
| `owned_disposal_does_not_require_dead_test_host` | Ported from `tests/Antiphon.Tests/Checkpoints/CheckpointTempScopeTests.cs`; 1 result | Live test owner positive fixture; root absent after disposal. | PC-200 |

The production sweep owner is now
`src/Antiphon.HostCleanup/Checkpoints/CheckpointTempRootSweep.cs`:
registration at 73, interval/gate at 110, scan/count/byte/time bounds at 133,
preflight at 149, root lock and locked recheck at 160, marker transition at 168,
fresh eligibility at 202, bounded deletion/retained custody at 232,
cursor persistence at 316, stale temporary entries at 332, legacy migration at 353.
PC-414 remains the per-entry missing-file/directory catch in EnumerateIndexFiles.

The test-owned scope policy remains
`tests/Antiphon.Tests/Checkpoints/CheckpointTestScope.cs`:
allocation/marker/registration-before-return at 50-77, rollback at 80-85,
registered-work join and fresh cleanup timer at 116-123, uncertain-child veto at 142-151,
per-root failure isolation and owned teardown at 145-175. Synthetic identity registration
never opens a native Process; native Process registration preserves the existing kill/join path.
The explicit context supplies all scope filesystem/time/usage observations. The 200 concurrent
allocation test serializes the fixture's primitive dictionary operations independently of the
held coordinator gate; all 200 complete before releasing that gate.

Retained PC-326..413 reader/custody/authority bindings move with their owner; the old paths
do not denote duplicate implementations. Receipt shape/identity test retains Validate/Parse
oracles and now also reaches Read for collection-null, collection-blank, output-pair and
markdown-required variants. ReportWriter retains digest/source/terminal-exit binding unchanged.
CARD-1307 A1/A2 behavior is unchanged and unclaimed. A3 receives PC-418's witness only.

## Ordinary evidence and handoff

CP-29 at unmoved `9449e1ffeaf1b10be0e5f388135bce39534574f6`: 53/54 passed,
one new port assertion accidentally read native storage. Correction committed as
`8c758b320b79c0d0d06f247376352b79b0f444db`; CP-29 rerun: 54/54 passed,
45 methods, clean source and verified build, receipt validated. This was a test-port
correction, not a production defect or a deliberate control cycle. No assertion was weakened.

Post-move ordinary results and exact tested SHA are in the Code task report.
CP-33 requires native Windows at the same post-move source; Linux cannot discharge it.
Review precedes landing; caller lands original Code task, then commissions SourceLanding Mutation.
Restart: none.

## Introduced scope-casing repair and PC-422

The first post-move group at `4b842b285ffbee705122153f53ec8d58302fd731` built all
four isolated outputs and reported the same two compiler diagnostics from one incorrect
Shouldly overload in the new FileOnly witness. All nine rows were build-failed; no tests
executed and no runtime-red proof is claimed. The overload was corrected without changing
the expected message or adding a retry.

Inspection found that the context port's journal enumeration used ordinal equality, losing
the original Windows Directory.EnumerateFiles case behavior. Test-first commit
`1be240cffb3f36c307be9e70d20ee22b4eeae704` adds a Windows primitive filesystem comparer and
an uppercase `OWNERSHIP.JSON` variant inside the existing
`teardown_preserves_failure_and_attempts_other_roots` method. The 45-method/54-result roster
is unchanged. CP-2 executed 54: 53 passed, 1 failed at
`C987.teardown_preserves_failure_and_attempts_other_roots.windows-journal.refusal`:
DisposeAsync returned instead of throwing IOException. This is a reached policy assertion,
not a compilation error, and demonstrates an introduced extraction defect.

Repair source `7b3f9e10314816038187d57ec14a228b2fd66638` changes only
`tests/Antiphon.Tests/Checkpoints/CheckpointTestScope.cs:188` to use
`_context.PathComparison`. Required ordinary green is CP-2 plus the full affected Linux
group on the final committed source; results belong to the task report.

**PC-422 remains pending SourceLanding Mutation.** Bind the exact method filter
`/*/*/HostCleanupCheckpointCompatibilityTests/teardown_preserves_failure_and_attempts_other_roots`.
Replace the path comparison with StringComparison.Ordinal; expect the named
windows-journal.refusal assertion above to fail. Restoration must also pass the owner,
reached verified-read, retained-root and sentinel assertions. The fixture supplies
case-insensitive primitive lookup and serialized foreign nested custody; it does not supply
the scope's eligibility decision. This extra control does not renumber PC-1..421 and is
not discharged by the ordinary regression. Mutation still owns missing-control discovery.
