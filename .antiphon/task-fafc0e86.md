# CARD-1619 policy coverage repair

## Baseline red

At untouched base `c622368c2fd8a134c191c5cdf8d484f9059406a2`, the exact two-class
selection `/*/*/(NativeLinuxPolicyWitnessTests*)|(NativeLinuxPolicyWitnessMutationTests*)/*`
executed 15 cases: 13 passed, 2 failed, 0 skipped (26.744 seconds).
Both `Policy_rows_require_pinned_attribute` and `Markers_do_not_expand_policy_exclusions`
failed with `reverse-coverage` diagnostics for these already-marked classes:

- `Antiphon.Tests.Application.Card1619ExitConsumerTests`
- `Antiphon.Tests.Application.Card1619ExitPersistenceTests`
- `Antiphon.Tests.Application.Card1619StopAttributionTests`

All 12 mutation cases passed, including LF/CRLF legacy digest, marker origin and
lookalike checks. No source change or commit preceded this reproduction.

The base build succeeded (0 errors; existing warnings), 195.63 seconds excluding
slot admission. Every driver used `scripts/build-slot.ps1`, `TEMP=/tmp`,
`UseAppHost=false`, producer-owned `OutputPath=bin-fafc0e86/`. Test execution used
`TUNIT_MAX_PARALLEL_TESTS=1`. Raw logs and fresh TRX are retained in
`/work/worktrees/task-fafc0e86/.antiphon/fafc0e86-evidence/` (ignored).

The referenced Debug report was absent here; it was read in full from
`/work/worktrees/task-d3f7786a/.antiphon/task-d3f7786a.md`.

## Linux-only source evidence

Each class already imports `Antiphon.TestSupport` and uses the real pinned
`NativeLinuxOnlyAttribute`. The shared native fixture
`tests/Antiphon.Tests/TestHelpers/Card1619NativeExitFixture.cs` launches `/bin/sh`
through the real detached PTY resolver, reads `/proc/<pid>/stat` start ticks to
verify Linux child identity, and imports libc `kill` to observe actual signal exit.
The three classes call that fixture to exercise consumers, persistence and stop
attribution respectively. This inspection confirms their existing class exclusion
is intentional; native test bodies were not executed under this brief.

## Pending

Add the three Windows policy rows, recompute policyHash with owner tooling, then
run the requested narrow checks and the real land-graph `-Check`.
