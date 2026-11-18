# CARD-1896 Code WIP evidence

Task / original landing owner: `79e80be8-3ede-42c1-b546-91a34575bd2a`.
Branch: `feat/card-task-79e80be8`.
Worktree: `/work/worktrees/ws-07` (the assigned desktop checkout was not used).
Base: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`, equal to fetched
`origin/master` at task start.
Plan: [2026-11-18-card-1896-checkpoints.md](2026-11-18-card-1896-checkpoints.md).

## Outcome

Implementation is incomplete. Three regression methods (five argument-expanded
cases) were authored in `C1677TempSweepTests.cs`; no production source was edited.
They exercise aggregate counts/bytes without deletion, family fairness with exact
size-based order, and skipping an oversized entry without starving a fitting one.
They are expected-red witnesses, **not confirmed red**: no compiler or test driver
was launched. Their assertions and existing timeouts were not loosened.

The required checkpoint tool insists on fresh `bin-<name>/` output paths
(`PlanTableImporter.cs` build-cell parser and
`ManifestValidator.OutputPathPattern`). The brief explicitly forbids custom
build outputs. Clarification was requested before running dependent work. No
checkpoint command row, manual source stamp or native-output test run was used
to evade the required TUnit checkpoint provenance. No custom output was created.

Question for the caller: authorize checkpoint-owned isolated outputs while
retaining native outputs for direct builds, or retain the prohibition and leave
this task at its committed witnesses. The Code task needs that answer before
its mandatory red-first checkpoint can run.

## Verification ledger

- CP-1/CP-2/CP-3: NOT RUN. Executed 0; passed 0; failed 0; skipped 0.
  Those zeroes describe absence of execution, not a green selection.
- V-1: authored, NOT RUN. V-2 Execute/DryRun: authored, NOT RUN.
  V-3 Execute/DryRun: authored, NOT RUN.
- V-4 scratch clone policy and V-5 session ANSI-log policy: NOT IMPLEMENTED,
  witnesses not yet bound. No live cleanup or setting change occurred.
- R-1/R-2/R-3/R-4: NOT RUN. Named runner tests and server settings/transport
  acceptance remain outstanding. The existing dispatch test file was not edited.
- R-5: three pin recomputations passed below; `land-graph -Check` NOT RUN;
  evidence-diff and merge-tree results are reported against the published tip
  in the task's final report.
- Classification/selector/pin paths were not changed; the guard union was not
  run under the brief's short-tests restriction. C1729 VIOLATIONS/LISTED were
  not executed or claimed. The derived index was not regenerated or claimed
  current; its required native driver build and two runs remain pending.
- No trusted tested SHA, TRX, CHECKPOINT line or build receipt exists for this
  slice. `slot=not-requested`, `waited=0s`; no unleased driver was launched.
- `/proc/loadavg` observations: `12.55 16.30 17.88`, then
  `7.24 11.92 15.80`. These are ambient readings, not build/run measurements.
- OS: no production OS branch was changed. The production Linux seam and native
  holder/no-follow witnesses remain required when the implementation proceeds.
- PC-1 through PC-4 and all Execute/DryRun variants in the plan remain PENDING
  Mutation. V-4/V-5 controls remain unbound. The operator waiver grants no pass.
- No deferred-to-Final acceptance ID is marked passed: V-1..V-5, R-1..R-4,
  and the incomplete parts of R-5 all remain pending. This is not a completed
  Interim or Final verification round.

## Read-only checks

`git diff --check` passed before commit. Required placement GETs were read:
`/api/runner-defaults` revision 2; `/api/session-runners` showed an available
Linux default and an available Windows runner. No runner or platform was pinned
and no work ran on the desktop.

All three recomputed values matched the brief and the unchanged tracked pins:

| Pin | Recomputed SHA-256 |
|---|---|
| policyHash | `5c478a13259f9d4bc3e638ab28ecee3ef517dc292d04f075246a796da944e352` |
| LegacyExclusionDigest | `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a` |
| PinnedInputDigest | `5e8c164421506fd1024e6da39351c097c4119f475957c41ee030c0c391488994` |

The policy hash used `Read-NightlyExecutionPolicy` after importing
`nightly-common.ps1` and `nightly-policy.ps1`, with
`ANTIPHON_NIGHTLY_CORE=off` to avoid automatic compilation. The protected-input
digest used `scripts/test-live-lane-run.ps1 -PrintInputDigest` with the same
setting. Legacy digest was recomputed in PowerShell using the checked-in
`WitnessedRowKeys`, exclusion selection rules and canonical JSON/hash helpers.
No pins were changed.

Two preliminary read-only commands failed: importing only `nightly-policy.ps1`
omitted its canonicalization helpers; `python3` was unavailable. The successful
PowerShell recomputations above resolved those command failures. Neither was a
test or build result.

No restart is requested for this WIP. No migrations were added; migration
red/green counts are not applicable. No live host, production PostgreSQL,
Docker volume, qualification run or existing scratch checkout was changed.
