# CARD-1264: lifecycle-host reports belong to the test sandbox

Original Code / landing owner: 72b8f17b-0f75-498c-a4b5-b803fdaabaf7.
Task base and fetched origin/master at start: 5665ddb1a3ba730f3b858acc109e65b5d7304d6c.

## Implementation

S1 commits the failing report-location assertions before changing either launch.
S2 passes an explicit `--results-directory <owned sandbox>/TestResults` at both
lifecycle-host launch sites. Existing CheckpointTestScope custody joins the child
before reclaiming the sandbox. Do not adopt post-seal build writes or relax cleanup.
Update CARD-1254 Cost to remove the obsolete Linux keep-outputs workaround.

Scope: CheckpointTempLifecycleTests.cs, CheckpointTempUsageTests.cs,
OutputCleanupTests.cs, the compiled census in scripts/lib/checkpoint-usage.ps1,
and the testing/build owner and CARD-1254 plan documentation.
No production service, database, delivery, landing or lease logic changes.
Restart: none.

## Verification design

- V-1: CheckpointTempLifecycleTests.passing_host_reports_are_removed_with_the_owned_sandbox
  runs a real passing child, requires exactly one HTML report in its owned sandbox,
  refuses a TestResults directory beside the staged DLL, then disposes the scope
  and requires the sandbox to be absent. Existing six lifecycle cases remain intact.
- V-2: CheckpointTempUsageTests.assembly_and_allocation_invoke_bounded_sweep
  requires reports inside each sandbox for both assembly-only and allocation-only
  children, and no report beside the DLL; retain the actual orphan-removal assertions.
- V-3: OutputCleanupTests.unknown_lifecycle_report_after_seal_is_refused_and_kept
  plants a literal unknown file under the lifecycle-host TestResults path after
  seal. Require its exact refusal, preserved bytes and retained compiler payload;
  a separate unchanged sealed root must be removed as the control.
- V-4: manual acceptance of the complete checkpoint run without --keep-outputs:
  exit 0/GREEN, empty cleanupRefusals, isolated output and staged TestResults absent,
  separate waiter complete, both receipt validators accept the exact tested SHA.
- R-1: every full affected class: lifecycle, temp usage and output cleanup.
- R-2: full CheckpointTempScopeTests and CheckpointTempRootSweepTests retain the
  existing custody, joins, bounded sweep and failure-path behavior.
- R-3: full CheckpointNamespaceCensusUsageTests keeps the independent count aligned.

Tests-first red runs CP-1 and CP-2 at S1; CP-1 must fail V-1 and CP-2 must fail
V-2 on the missing sandbox report. The unknown-file safety assertion covers an
existing guard; deliberate defect injection belongs to Mutation. All ordinary rows
run at S2. Fresh TRX inspection must show every named class/method and nonzero counts.
No repeated green runs are required. No full assembly is planned: the invariant is
confined to these two launch sites and the unchanged cleanup/scope boundaries.
The brief explicitly excludes a whole Unit lane; that coverage is disclosed rather
than credited. This is the task-specific Final selection.

### Positive controls (pending SourceLanding Mutation)

| PC | Variant | Method-scoped detecting filter |
|---|---|---|
| PC-1 | Omit lifecycle Verify results-directory argument | `/*/*/CheckpointTempLifecycleTests/passing_host_reports_are_removed_with_the_owned_sandbox` |
| PC-2 | Omit usage host results-directory argument; assembly and allocation variants | `/*/*/CheckpointTempUsageTests/assembly_and_allocation_invoke_bounded_sweep` |
| PC-3 | Accept unknown post-seal report by path / bypass the creation record check | `/*/*/OutputCleanupTests/unknown_lifecycle_report_after_seal_is_refused_and_kept` |
| PC-4 | Leave owned sandbox reports on disposal | `/*/*/CheckpointTempLifecycleTests/passing_host_reports_are_removed_with_the_owned_sandbox` |

All variants are pending. Code performs no deliberate mutants. The unbound candidate
is `docs/superpowers/mutations/2026-10-14-card-1264-candidate.md`.

### Cost

Ordinary floor is 17 minutes: one isolated test build and the five exact rows below.
Allow authoring and tests-first red another 15 minutes; shared-host slot waiting is
additional. Bootstrap the checkpoint CLI once through scripts/build-slot.ps1 into
its own bin-c1264-tool/ directory (an explicitly disclosed non-row tool build).
Import this table and pin manifest timeouts.rowMinutes to 40. Run with --row-timeout
40m --total-timeout 150m --serial and the exact committed --expected-source-sha;
omit --keep-outputs on Linux. Await every run until exit is not 75. Red outputs stay
for inspection, then remove only the recorded producer-owned literal paths before
rerunning. Validators are read-only CLI/PowerShell commands, not build/test drivers.
Run scripts/check-evidence-diff.ps1 over the complete task base..HEAD.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1264-qualified/` | lifecycle | `/*/*/CheckpointTempLifecycleTests/*` | V-1, V-4, R-1 | all 7, 0 failed/skipped | 7 | 8 |
| CP-2 | all | CP-1 | usage | `/*/*/CheckpointTempUsageTests/*` | V-2, V-4, R-1 | all 8, 0 failed/skipped | 8 | 2 |
| CP-3 | all | CP-1 | cleanup | `/*/*/OutputCleanupTests/*` | V-3, R-1 | all selected, 0 failed/skipped | 40 | 4 |
| CP-4 | all | CP-1 | scope-sweep | `/*/*/(CheckpointTempScopeTests*\|CheckpointTempRootSweepTests*)/*` | R-2 | all 38, 0 failed/skipped | 38 | 2 |
| CP-5 | all | CP-1 | census | `/*/*/CheckpointNamespaceCensusUsageTests/*` | R-3 | all 1, 0 failed/skipped | 1 | 1 |
