# CARD-1860 Code evidence: missing logs and DerivedImpact prerequisite

Implemented and locally verified. Missing logs now behaves like an absent PID file
without Census creating anything. Unreadable/malformed PID files still fail before
controls, including Hard/WaitOnly. A-3 uses the brief's explicit checkpoint-prerequisite
option: build the regeneration driver before its unchanged real script witness.

- Original Code / landing owner: `042b61c0-ab0d-49e7-a298-93ce0cf8792b`.
- Worktree: `/work/worktrees/task-042b61c0`; branch: `feat/card-task-042b61c0`.
- Task base: `cc48c70050a2932eddf4abe18cea8659d478c33e`.
- Red-test commit: `145bca676723f9b815ff30970788720ddbb57a09` (pushed before red run).
- Production/prerequisite commit and actual green tested source:
  `73292703cc2ccbcab9f11d0d1616ae2bef33bea5` (pushed before green run).
- Subsequent report/historical-contract annotations change no production or C# test source.
- Plan: `docs/superpowers/plans/2026-11-12-card-1860-restart-and-driver-plan.md`.
- Unbound candidate: `docs/superpowers/mutations/2026-11-12-card-1860-restart-and-driver.md`.
- Execution: server2 Linux, pwsh 7, .NET 9 test host. Child-only PATH used the real
  `/opt/powershell` host and TEMP `/tmp`, as the inherited fixture resolver requires.
  GET /api/runner-defaults and /api/session-runners were read: portable work uses the
  effective Linux default, while desktop Windows is available for the caller's native work.
  No location is embedded in product code. Restart: **none**.

## Ordinary outcomes and acceptance IDs

Every row below belongs to the CARD-1860 plan; the historical CARD-1851 nightly rows
were not rerun for this separate brief. Its table now declares the same A-3 prerequisite
and consumer, and preserves its historical outcomes. The new task's closed rows were
executed once per committed group using the checkpoint tool and exact expected source SHA.

| Row / requirement | Actual outcome |
|---|---|
| CP-1 / V-1 red | 2 failed, 0 passed, 2 native skips; both pwsh entry forms fail at missing logs PID read |
| CP-2 / V-2 red | 1 failed: real regeneration script cannot execute missing Antiphon.DerivedImpactIndex.dll |
| CP-0 / V-2 prerequisite | exit 0, native-default driver built before consumer; no test count applies |
| CP-3 / V-1,R-1 | 20 passed, 0 failed, 22 native Windows skips |
| CP-4 / V-2,R-2 | 84 passed: DerivedImpactIndexTests 27, Antiphon.Tests ProcessSpawnLimitTests 57; 0 failed/skipped |
| CP-5 / R-2 | SessionRunner ProcessSpawnLimitTests 2 passed, 0 failed/skipped |
| CP-6 / R-2 | Pty ProcessSpawnLimitTests 2 passed, 0 failed/skipped |
| V-3 manual | ASCII 0 non-ASCII bytes; OS-witness lint exit 0; full-range impact resolution covered; evidence-diff and merge-tree clean at tested source |

Fresh TRX was inspected for every intended class and method, with nonzero counts.
`Missing_logs_is_an_absent_pid_without_creating_the_directory`: both Linux -File
and & arguments passed (4.1764784s and 2.9537138s), after each failed at S1. Its real
factory Census observes absent supervisor/wrapper PID paths without creating logs;
then only control/health boundaries are replaced to observe expected startup actions.
`Pid_read_errors_stop_restart_before_any_control`: all 12 pwsh arguments passed,
including directory, locked, invalid, overflow, zero, unreadable wrapper, Hard/WaitOnly
unreadable/garbage variants, and the missing/live positive controls. Existing scope,
HTTP-bound and nightly-closure compatibility witnesses also remain green in CP-3.
`regenerate_script_reports_changed_then_unchanged` passed in 10.9843550s after CP-0;
its script actually rewrites stale bytes, then reports unchanged without a commit.

The full base..tested-source impact resolution has no matching hand kind, no capped
or owed class, and selects RunnerRestartScriptCompatibilityTests plus ProcessSpawnLimitTests
in all three declaring projects. Every filter is covered by CP-3..CP-6. No rostered guard
class, limiter, generated index, policy or schema was edited; the brief's minimal-testing
exception therefore omits the guard union. The named DerivedImpact class ran in full.
Whole Unit/nightly/rc lanes, unrelated layering/HTTP/database/delivery/landing classes,
loaded repetitions and whole assemblies were not selected. No green proof was repeated.

## Provenance and qualification limits

Red run: `.antiphon/checkpoints/20261112-235101-f6a4/` (exit 1, expected witness failures).
Green run: `.antiphon/checkpoints/20261112-235906-610d/` (exit 0, five green ordinary rows).
The reports bind the exact expected and tested SHA. All test rows show dirty=0,
sourceState=clean and buildSource=verified. Source start/end SHA, dirty count and
fingerprint agree. CP-0 is a command row, source-bound with buildSource=notApplicable;
its leased compiler output and successful consuming method are retained. No receipt
was edited. All executed test builds/rows acquired host slots; no unleased run occurred.

Deterministic checkpoint validate over CP-0,CP-4,CP-5,CP-6 exits 0:
`CHECKPOINT SOURCE VALID source=73292703cc2ccbcab9f11d0d1616ae2bef33bea5 rows=4`.
Full validate exits 2: `CHECKPOINT SOURCE INVALID reason=row_failed`, because CP-3
contains 22 platform-native skips. Cleanup refusals are empty. This retains the
explicitly out-of-scope A-2 incomplete gate; a skipped native case is never a pass.

Caller owes native Windows CP-3 at the reviewed source, under PowerShell 7 and
Windows PowerShell 5.1, including both missing-logs entry forms, and the operator-owned
desktop & restart acceptance. Example checkpoint selection (fresh Windows worktree):
`dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-11-12-card-1860-restart-and-driver-plan.md --rows CP-3 --serial --expected-source-sha <reviewed-full-SHA>`.
A native operational restart is the caller's action; Code touched no live runner,
session, Scheduled Task, host port, Docker object or volume.

OS declaration/lint: ignored `.antiphon/c1860-evidence/os-witnesses.json`. The changed
PID reader is a portable File.ReadAllText exception branch in the real Windows runner
factory. Callers scripts/restart-session-runner.ps1 and both root forwarders were
inspected. No OS-selected production branch changed; existing fixture platform/Windows
TCP-owner skips were explicitly classified as unchanged. Census is exercised through
production with real missing filesystem inputs; no mock replaces it. Bootstrap startup
census paths are unchanged; no AppHost restart or rc activation was attempted.

Only unlisted build: leased checkpoint-tool bootstrap, required by the fresh checkout
and declared by the plan; exit 0, slot=granted waited=0s, compiler 15.13s, zero errors.
Validation, evidence lint, OS lint, impact resolution and merge-tree are read-only checks,
not additional build/test drivers. No test selection was retried or broadened after green.

## Performance and pending work

Green run wall: **2202.354s (36m42s)**; execution budget accounting 1857.229s;
admission-only 345.125s; finalization 156.339s (output cleanup 156.105s).
The run exceeded the provisional estimate and 30-minute policy threshold: **CARD-1861**
(`b1ba44bb-16a3-4ab4-b150-8281b8db821d`) was filed after a complete duplicate check.
The caller must commission its Debug investigation. Do not repeat the full selection
merely to measure it again. Its title is “Debug: CARD-1860 checkpoint over 30 minutes;
DerivedImpact and cold-build costs”. Both Code runs and their historical cost remain valid.

CP-0 compiler 367.68s; isolated runner compiler 42.71s; isolated Antiphon.Tests compiler
194.10s; isolated Pty compiler 43.59s. Build receipt execution includes additional
confinement/ledger work: runner 108.527s, server 324.065s, Pty 132.632s. Build wall is
151.192s/365.434s/224.038s respectively; Pty build admission waited=30s. CP-4 row waited=240s,
executed=535.020s; CP-6 row waited=75s, executed=4.538s. Other rows/builds waited=0s.
CP-4 test summary is 8m48.475s, with per-method TRX and case phase journal retained.
No timeout/assertion/retry was widened. Fixtures remain independent scratch scopes;
no new database/container/web-host setup exists. Missing child IO/setup/teardown
subspans are unknown/remainder, not measured zero. Existing timeout policies stay
unchanged; this is not a new p95 claim.

Pending Mutation: PC-1/missing-directory-catch; PC-2/swallowed-unreadable;
PC-2/invalid-as-absent; PC-3/omitted-driver-prerequisite. All **pending**, none passed.
Operator waived deliberate mutation for this Code task (2026-11-12). Mutation owns
method-scoped red/restore/fresh-build/green, binding and missing-control discovery if
commissioned. PC-3 needs a qualifying control: the ordinary missing-driver precondition
failure alone earns no deliberate-mutant red. Historical CARD-1851 controls retain
historical pending status; its old missing-parent expectation is explicitly superseded.

Manual source checks before publication: evidence-diff over
cc48c70050a2932eddf4abe18cea8659d478c33e..73292703cc2ccbcab9f11d0d1616ae2bef33bea5
exited 0 (commits=2, entries=0, violations=0). Fetched origin/master was
cc48c70050a2932eddf4abe18cea8659d478c33e; merge-tree exited 0 with tree
7409a42b2cd1e344cb85730b0d0e5cb789d99d7a. Publication checks are repeated across
the final report commit; the caller report gives the final full publication SHA.

## Unedited checkpoint lines

These lines are copied directly from each schema-2 report.json row.line (not rebuilt
from counts). The green report's digest/source provenance validates for the four
non-skipping rows; CP-3's source binding agrees and its native-skip gate stays incomplete.
Red lines preserve the expected earlier failures and their source instead of relabeling them.

```text
CHECKPOINT CP-1 commit=145bca676723f9b815ff30970788720ddbb57a09 build=ok filter=/*/*/RunnerRestartScriptCompatibilityTests/Missing_logs_is_an_absent_pid_without_creating_the_directory executed=2 passed=0 failed=2 skipped=2 trx=/work/worktrees/task-042b61c0/.antiphon/checkpoints/20261112-235101-f6a4/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=145bca676723f9b815ff30970788720ddbb57a09 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=145bca676723f9b815ff30970788720ddbb57a09 build=ok filter=/*/*/DerivedImpactIndexTests/regenerate_script_reports_changed_then_unchanged executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-042b61c0/.antiphon/checkpoints/20261112-235101-f6a4/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=145bca676723f9b815ff30970788720ddbb57a09 sourceState=clean buildSource=verified
CHECKPOINT CP-0 commit=73292703cc2ccbcab9f11d0d1616ae2bef33bea5 build=n/a filter=dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:1 --nologo executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=73292703cc2ccbcab9f11d0d1616ae2bef33bea5 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=73292703cc2ccbcab9f11d0d1616ae2bef33bea5 build=ok filter=/*/*/RunnerRestartScriptCompatibilityTests/* executed=20 passed=20 failed=0 skipped=22 trx=/work/worktrees/task-042b61c0/.antiphon/checkpoints/20261112-235906-610d/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=73292703cc2ccbcab9f11d0d1616ae2bef33bea5 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=73292703cc2ccbcab9f11d0d1616ae2bef33bea5 build=reused filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-042b61c0/.antiphon/checkpoints/20261112-235906-610d/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=73292703cc2ccbcab9f11d0d1616ae2bef33bea5 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=73292703cc2ccbcab9f11d0d1616ae2bef33bea5 build=ok filter=/*/*/(DerivedImpactIndexTests*)|(ProcessSpawnLimitTests*)/* executed=84 passed=84 failed=0 skipped=0 trx=/work/worktrees/task-042b61c0/.antiphon/checkpoints/20261112-235906-610d/rows/CP-4/run.trx slot=granted waited=240s dirty=0 source=73292703cc2ccbcab9f11d0d1616ae2bef33bea5 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=73292703cc2ccbcab9f11d0d1616ae2bef33bea5 build=ok filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-042b61c0/.antiphon/checkpoints/20261112-235906-610d/rows/CP-6/run.trx slot=granted waited=75s dirty=0 source=73292703cc2ccbcab9f11d0d1616ae2bef33bea5 sourceState=clean buildSource=verified
```
