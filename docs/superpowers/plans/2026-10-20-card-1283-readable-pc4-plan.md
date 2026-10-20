# CARD-1283: make CARD-1278 PC-4 fail readably

Target: **master only**. Plan task: `e59797b3`.
Inspected current master: `9b05461295644e700f1391d97d4861f78422016b`
(fetched 2026-10-20; also this task's base). The four tests still exist and their
competitors are still effectively unbounded. This is a test-oracle repair, not
a production sweep regression.

## Ground truth

| Card assumption | Current code | Consequence |
|---|---|---|
| Removing the sweep bound makes PC-4 hang | `server/Application/Services/ComplexityRoutingService.cs`, `SweepExpiredAsync`, retries forever except for `if (attempt >= SweepAttempts) throw;`. Each lost race detaches its cleared rows and rereads them. | Removing that guard admits another real read/write race indefinitely while the competitor keeps rotating the token. |
| V-1/V-2 compete on every sweep save | `tests/Antiphon.Tests/Application/MatrixDefaultComplexityCreateTests.cs`, `SweepRace.SavingChangesAsync`, invokes `Compete` on every save containing a modified chain row. | There is no fixture exhaustion path to the result/count assertions. |
| V-3/V-4 compete indefinitely | `tests/Antiphon.Tests/Application/ComplexityChainRaceTests.cs` arms `CompetingCommit` with `int.MaxValue` in both sweep-exhaustion methods. | Finite in theory, operationally a hang. The unrelated unconditional PUT/DELETE exhaustion test also uses `int.MaxValue`; it is outside this card. |
| The production bound is three | `SweepAttempts` aliases `ComplexityChainService.UnconditionalAttempts`, currently 3. | All four tests must retain their expected three failures and existing persistence assertions. |
| Arming `SweepAttempts + 1` succeeds on save four | `CompetingCommit` decrements its remaining count and competes before each save while it is positive. | **Correction:** four conflicts mean an unbounded sweep succeeds on save **five**. Four is the number of competing commits, not successful-save ordinal. |
| A Unit sweep is needed | Both affected classes are Integration tests using isolated PostgreSQL databases. | Select only the four affected methods; no whole-Unit or whole-class run. |

## Current-master verification

The unchanged V-4 method passed: 1 executed, 1 passed, 0 failed, 0 skipped.
It was built and run through `scripts/run-checkpoint.ps1` with a granted host
build slot, an isolated output, and strict source binding to the SHA above.
The direct script invocation is a Plan-stage premise check, not a Code
checkpoint execution.

```text
CHECKPOINT REPRO-BASE commit=9b05461295644e700f1391d97d4861f78422016b build=ok filter=/*/*/ComplexityChainRaceTests/A_sweep_that_loses_every_attempt_lets_the_concurrency_error_propagate executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-e59797b3/.antiphon/c1283-base/REPRO-BASE-20261020-141203-5cb8/run.trx slot=granted waited=0s dirty=0 source=9b05461295644e700f1391d97d4861f78422016b sourceState=clean buildSource=verified
```

The hang mechanism also reproduced on the same implementation. Starting from
plan-only commit `6ed12c05e5c3d406b86f4c785bb24969c41fe476`,
a **temporary, uncommitted diagnostic** removed only the final
attempt guard and passed a five-second `CancellationTokenSource` token to the
V-4 sweep call. A `finally` printed the fixture's existing save/fire counters.
The subject made **1,569 saves and 1,569 competing commits** before cancellation:

```text
CARD1283_DIAGNOSTIC saves=1569 fired=1569 cancelled=True
```

The one selected test failed because `Should.ThrowAsync` expected
`DbUpdateConcurrencyException` but received `TaskCanceledException`: 1 executed,
0 passed, 1 failed, 0 skipped; test-host duration 45.948 seconds including database
startup/migration. This is bounded evidence of the retry loop, **not an accepted
readable-red PC-4 result**, and it is not a clean-source qualification.
The original token is `CancellationToken.None`; the original competitor has
no practical exhaustion deadline.

Both diagnostic drivers used `scripts/build-slot.ps1` with granted slots:
`c1283-bounded-repro-build` (build exit 0, 2m38s) and
`c1283-bounded-repro-test` (TUnit exit 2). The build reused this task's isolated
`tests/Antiphon.Tests/bin-c1283-base/` output with the same absolute OutputPath,
ArtifactsPath and UseArtifactsOutput properties; it replaced the baseline
binaries, without relabeling their earlier receipt. The test used `dotnet run
--no-build` and the exact REPRO-BASE filter above. These additional diagnostic
drivers were necessary to check the old card's premise, not additional ordinary
coverage. Logs, diagnostic patch and TRX remain ignored under
`.antiphon/c1283-probe/`.

Both commands finished; the test disposed its PostgreSQL container. The two
temporary source edits were restored byte-for-byte from pre-probe backups and
`git diff --exit-code` over both files passed. No production or test change is
part of this Plan deliverable. The checkpoint importer accepted the table below
as one row. The implementation repair and readable-red witness remain pending.

## Decisions

- **D-1 — finite competition:** arm exactly `SweepAttempts + 1` competing
  commits. This preserves three failures in ordinary code, detects an extra
  attempt, and lets a boundless mutant reach a normal assertion failure.
  Rejected: `int.MaxValue`, an always-active callback, or a timeout as the
  expected red; all make the failure depend on an external deadline.
- **D-2 — keep the fault realistic:** retain the second context's token
  rotation, real PostgreSQL concurrency exception, and all existing warning,
  task, saved-row, tracker-detachment and subsequent-save assertions.
  Rejected: throwing a synthetic exception or weakening the assertions.
- **D-3 — keep the repair local:** change the two fixture usages and the
  CARD-1278 PC-4 description. Keep the production retry limit and detachment
  logic unchanged. Do not refactor the unrelated PUT/DELETE race fixtures.
- **D-4 — placement and scope:** use the Integration lane with PostgreSQL
  support on the dynamically selected runner. Both `/api/runner-defaults`
  (revision 2, no per-kind overrides) and `/api/session-runners` were read on
  2026-10-20; eligible Linux and Windows runners exist. No OS dependency or
  host pin is needed: omit `-Runner` and `-Platform` on dispatch. No deployment,
  RC backport or runtime restart is required.

These are implementation choices within the brief, with no pending operator
decision. This plan does not add or change any session waiting for input.

## Slices

| Slice | Files and change | Tests |
|---|---|---|
| S1 — bounded fixtures | In `tests/Antiphon.Tests/Application/MatrixDefaultComplexityCreateTests.cs`, give private `SweepRace` an explicit finite `Arm(int times, Func<Task> compete)` operation, reset its counters when armed, and invoke the callback only for a modified-chain save while a conflict remains. Arm V-1 and V-2 with `SweepAttempts + 1`. In `tests/Antiphon.Tests/Application/ComplexityChainRaceTests.cs`, change only the two sweep-exhaustion `Arm(int.MaxValue, ...)` calls to `Arm(SweepAttempts + 1, ...)`. Retain the zero-arm cleanup. Update PC-4 in `docs/superpowers/plans/2026-10-15-card-1278-sweep-detach.md` to describe finite competition and save five. Commit and push this single repair slice. | V-1 through V-4 below. Existing assertions are the oracle; no new test suite is needed. |

## Verification design

The Code brief explicitly skips TestDesign and retains this small closed selection:
one bounded readable-red diagnostic and one ordinary green row for all four methods,
with `--row-timeout 40m`. Formal PCs remain pending for SourceLanding Mutation.

| ID | Existing method | Required ordinary result |
|---|---|---|
| V-1 | `MatrixDefaultComplexityCreateTests.A_default_tier_create_whose_expiry_sweep_loses_every_race_is_created_with_the_warning` | Three competitor commits; queued default-tier task with the exact routing-read warning; original persistence and detachment assertions pass. |
| V-2 | `MatrixDefaultComplexityCreateTests.An_explicit_complexity_create_whose_expiry_sweep_loses_every_race_still_fails` | `DbUpdateConcurrencyException` after three competitor commits; no task, expired row remains active. |
| V-3 | `ComplexityChainRaceTests.A_sweep_of_several_rows_that_loses_every_attempt_leaves_none_of_them_modified` | Exception after three saves; neither row remains modified; later save writes zero; both rows remain active. |
| V-4 | `ComplexityChainRaceTests.A_sweep_that_loses_every_attempt_lets_the_concurrency_error_propagate` | Exception after three saves/competitor commits; row remains active; no modified chain remains; later save writes zero. |

One readable-red witness is sufficient: CARD-1278 PC-4, using V-4 alone.
Remove only the final `if (attempt >= SweepAttempts) throw;` guard in
`SweepExpiredAsync`, preserving the catch filter and detach operations.
With the repaired fixture there are four real concurrency conflicts and the
fifth sweep save succeeds. `Should.ThrowAsync<DbUpdateConcurrencyException>`
must fail because no exception was thrown. A timeout, cancellation, fixture/build
failure, or zero executed tests is not this witness.

The detecting filter is
`/*/*/ComplexityChainRaceTests/A_sweep_that_loses_every_attempt_lets_the_concurrency_error_propagate`.
Keep this PC method-scoped; after restoring the guard, rebuild and run the same
method green. Retain the original PC-1..PC-3 obligations without rerunning or
claiming them here. Ordinary Code/Review uses CP-1; the deliberate PC remains
for separately commissioned post-land SourceLanding Mutation.

Use the checkpoint tool for the committed S1 ordinary run, with
`--expected-source-sha <full S1 SHA>`, then await its completion. Record actual
TRX counts and source/build provenance. All build/test drivers use the host
build-slot gate. Cleanup task-owned isolated outputs after all children exit.

### Cost

Ordinary floor: 8 minutes including the isolated build; allow about 10 minutes
for the small edit and review of the four assertions. One method-scoped PC
red/restore/green cycle: allow another 8 minutes including fresh builds.
No broad regression run is justified by this fixture-only change.

### Checkpoints

The Group names the lane. One ordinary green row covers exactly four methods.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1283/` | integration-sweep-fixtures | `/*/*/(ComplexityChainRaceTests*)\|(MatrixDefaultComplexityCreateTests*)/(A_sweep_that_loses_every_attempt_lets_the_concurrency_error_propagate*)\|(A_sweep_of_several_rows_that_loses_every_attempt_leaves_none_of_them_modified*)\|(A_default_tier_create_whose_expiry_sweep_loses_every_race_is_created_with_the_warning*)\|(An_explicit_complexity_create_whose_expiry_sweep_loses_every_race_still_fails*)` | V-1, V-2, V-3, V-4 | all four listed methods, 4 passed, 0 failed/skipped | 4 | 8 | true | n/a |
