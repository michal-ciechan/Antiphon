# CARD-1254 checkpoint leftovers

Code and landing owner: 71fd0d46-2382-4ffd-b704-35a9896333d7. Base:
a02389cab727d508532e6d3db8ff6cbcf3eb7366. Final, bounded to the classes below;
the task brief explicitly excludes a whole Unit lane. No assembly-wide run.

## Investigation and implementation

The unchanged base reproduced 8/10 SlotExecutor, 4/11 LaunchCleanup and 7/8
TempUsage passing, with no skips. All ten failing methods also run individually
against that base. No missing broker, Docker or nested-executor dependency excuses
these failures. The slot fixture omits the project parent required by exclusive
output creation. Launch tests retain obsolete rollback/latest-pointer expectations
and unowned payload setup. A definite native refusal also leaves ambiguous launch
custody. The sweep cannot replace another process's marker through ordinary
SafeWrite. The separate waiter lacks the starter's in-memory copy inventory, and
PowerShell receipt validation ignores cleanupRefusals.

S1 adds cross-process cleanup and validator regressions, before product fixes.
S2 repairs fixtures, records definite no-launch custody, introduces a narrowly
guarded root-marker transition, persists a shadow-copy inventory before launch,
and makes PowerShell refuse cleanup refusals. The adjacent base binary also reproduced ShadowCopyTests' missing destination
parent and the scope's unrecorded marker creation (11/13 pass). These are repaired
without changing the guarded registration/deletion assertions. S3 merges fetched origin/master,
checks evidence history and records final verification without relabeling SHAs.

Shadow authority is restricted to the literal tool child of its recorded run.
It contains only creation records verified in the copying process, bound to that
run's ownership journal by SHA-256. The waiter checks executor death, containment,
links, Git tracking, directory identity, the full inventory and file bytes under
the cleanup gate; deletion is individual and nonrecursive. Generic build-output
ledgers remain diagnostic. The run control records share the starter/waiter's OS
account trust boundary: an actor able to rewrite both the journal and its receipt
is not authenticated away by a hash. Even such records must not authorize paths
outside tool. Current executor images and legacy copies without authority remain
retained. No timeout or assertion threshold is widened.

## Verification design

- V-1: full CheckpointSlotExecutorTests; actual broker leases, row rejection,
  distinct holders, cancellation and renewal remain asserted.
- V-2: full CheckpointLaunchCleanupTests; no native launch before intent, retained
  exact evidence on preparation/copy/intent failure, no shared latest write,
  definite refusal custody and late executor acknowledgement.
- V-3: full CheckpointTempUsageTests; assembly/allocation child sweeps remove the
  seeded dead orphan. Full lifecycle/scope/sweep classes guard bounds and custody.
- V-4: CheckpointShadowCustodyTests.separate_wait_removes_only_the_sealed_tool_copy
  (green/red exits) and separate_wait_retains_unproven_copy_and_outside_bytes
  (added, changed, forged-ledger, missing, malformed, foreign, escape, linked,
  hardlinked, replaced). Each unsafe variant first proves a removable copy.
- V-5: CheckpointSourceExecutionTests.cleanup_refusals_disqualify_green_rows_in_both_validators;
  all rows and selected rows refuse the same literal cleanup reason; empty list
  controls pass. Run the full affected class.
- R-1: full tool-copy, run-ownership, owned-output, wait, shadow-copy, app,
  report-writer and receipt-safety classes retain existing cleanup/receipt guards.
- R-2: full temp-root sweep, lifecycle and scope classes; no unknown/live owner
  cleanup, unchanged sweep budgets and teardown ownership.
- R-3: compiled checkpoint census and manual separate-process run/wait using the
  rebuilt tool, both validators, exact-SHA source evidence and fresh TRX roster.

The unbound candidate is docs/superpowers/mutations/2026-10-14-card-1254-candidate.md.
All PCs below remain pending for method-scoped SourceLanding Mutation. Code does
not perform deliberate mutants, and ordinary green does not discharge PCs.

| PC | Variant / mutation | Detecting filter |
|---|---|---|
| PC-1 | omit durable copy authority | `/*/*/CheckpointShadowCustodyTests/separate_wait_removes_only_the_sealed_tool_copy` |
| PC-2 | bypass inventory, content, digest, containment or identity checks; all ten V-4 unsafe variants | `/*/*/CheckpointShadowCustodyTests/separate_wait_retains_unproven_copy_and_outside_bytes` |
| PC-3 | ignore cleanupRefusals in PowerShell; all/selected rows | `/*/*/CheckpointSourceExecutionTests/cleanup_refusals_disqualify_green_rows_in_both_validators` |
| PC-4 | omit definite no-launch journal update | `/*/*/CheckpointLaunchCleanupTests/definite_native_refusal_retains_evidence_with_known_outcome` |
| PC-5 | disable assembly/allocation sweep or marker transition | `/*/*/CheckpointTempUsageTests/assembly_and_allocation_invoke_bounded_sweep` |
| PC-6 | bypass live executor veto | `/*/*/CheckpointRunOwnershipTests/custody_is_reread_under_the_lock` |

### Cost

Final table floor: 27 minutes, including one isolated test build. Baseline,
tests-first red and red-row repairs use the same selections with fresh named
outputs; the two tool bootstraps are disclosed gated builds. Import this table
and set manifest timeouts.rowMinutes to 40; invoke run with --row-timeout 40m
--total-timeout 150m --serial --keep-outputs and the exact committed --expected-source-sha.
Keep compiler outputs because lifecycle children write their HTML reports after
the build seal; remove only those producer-owned literal output paths at the end.
Wait until exit is not 75. Keep generated evidence ignored. Inspect each fresh
TRX, run both validators, and run scripts/check-evidence-diff.ps1 over base..HEAD.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1254-qualified/` | slots | `/*/*/CheckpointSlotExecutorTests/*` | V-1 | all 10, 0 failed/skipped | 10 | 8 |
| CP-2 | all | CP-1 | launch | `/*/*/CheckpointLaunchCleanupTests/*` | V-2 | all 11, 0 failed/skipped | 11 | 1 |
| CP-3 | all | CP-1 | usage | `/*/*/CheckpointTempUsageTests/*` | V-3 | all 8, 0 failed/skipped | 8 | 2 |
| CP-4 | all | CP-1 | shadow-custody | `/*/*/CheckpointShadowCustodyTests/*` | V-4 | all 12, 0 failed/skipped | 12 | 2 |
| CP-5 | all | CP-1 | source-validation | `/*/*/CheckpointSourceExecutionTests/*` | V-5 | all 8, 0 failed/skipped | 8 | 3 |
| CP-6 | all | CP-1 | ownership-guards | `/*/*/(CheckpointToolCopyCleanupTests*\|CheckpointOwnedOutputTests*\|CheckpointRunOwnershipTests*)/*` | R-1 | all selected, 0 failed/skipped | 38 | 2 |
| CP-7 | all | CP-1 | wait-and-report | `/*/*/(WaitCommandTests*\|ShadowCopyTests*\|CheckpointAppTests*\|ReportWriterTests*)/*` | R-1 | all selected, 0 failed/skipped | 22 | 2 |
| CP-8 | all | CP-1 | temp-lifecycle | `/*/*/(CheckpointTempRootSweepTests*\|CheckpointTempLifecycleTests*\|CheckpointTempScopeTests*)/*` | R-2 | all selected, 0 failed/skipped | 44 | 2 |
| CP-9 | all | CP-1 | census | `/*/*/CheckpointNamespaceCensusUsageTests/*` | R-3 | all 1, 0 failed/skipped | 1 | 1 |
| CP-10 | all | CP-1 | receipt-safety | `/*/*/CheckpointReceiptSafetyTests/*` | R-1 | all 8, 0 failed/skipped | 8 | 4 |
