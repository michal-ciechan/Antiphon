# CARD-0097 S-C1 Code evidence

This records the original S-C1 Code run. Final Review `64f32586` subsequently found D1
(a diagnostic exposed a non-default policy target and treated an unread chain as unset) and D2
(an older server acquired an invented default column). The repair record is
[S-C1 D1/D2 repair evidence](2026-10-14-card-0097-sc1-repair-evidence.md).
Original counts and CHECKPOINT lines below are historical results, including the replaced
V-C4 expectation; they do not qualify the repair.

Original Code task / landing owner: `6812413b-f769-4554-9d4a-5b73c6a11afa`.
Branch: `feat/card-task-6812413b`.
Worktree: `/work/worktrees/task-6812413b`.
Base / fetched master at start: `f7ba0684c5680e250473a2310e292e23179b3642`.
Implementation and final targeted verification SHA: `bbefbe8fe265996599f27f295671719ef675b30e`.
Whole-client verification SHA: `8a7a452cdb724bbee4509f9e6329eccc67748d99`.
The only difference between those tested SHAs is a test expectation's explicit presence guard after TS2769; production is identical. The subsequent evidence-only commit is identified in the settlement report.

Plan: [matrix plan](2026-10-14-card-0097-matrix-plan.md).
Test design: [matrix test design](2026-10-14-card-0097-matrix-test-design.md).
Mutation candidate: [S-C1 pending controls](../mutations/2026-10-14-card-0097-sc1-candidate.md).

Scope is S-C1 only. No row editor, routing write, query-key change, server implementation, deployment or restart. The brief explicitly excludes the server Unit lane. No affected server integration classes or S-C1 manual/E2E acceptance are named. S-C2 and the other slices remain outside this dispatch; no in-scope V/R IDs are deferred.

## Verification outcomes

| ID / selection | Actual outcome |
|---|---|
| V-C1 | 16 literal model cases passed: all seven sources, missing target, and surviving targets with withheld diagnostics. Red-first: 10 failed, 6 existing source-label cases passed. |
| V-C2 | 4 cases passed: Easy/Hard unset wording, default fallback, and dynamic ordinary-worker boundary. All 4 failed red-first. |
| V-C3 | 4 count cases passed: zero, one, two, and excluding stage/other-role pins. Red-first: 2 failed, 2 passed. |
| V-C4 | 12 DOM cases passed: effective overlay first plus 11 literal diagnostic combinations. All 12 failed red-first because the guarded display was absent. |
| V-C5 | 1 passed: default header, role-policy fallback, held fable reason, non-default unset. Failed red-first. |
| V-C6 | 1 passed: card-pin count in the role row. Failed red-first. |
| R-C1 | All 7 unchanged complexityChains API tests passed, including distinct query keys and write invalidation. |
| Existing model/tab regression cases | 9 model and 16 tab cases passed. Three tab copy expectations were updated exactly as the plan requires and failed red-first. |
| CP-16 red-first | 3 files, 70 results: 33 failed, 37 passed, 0 skipped at f0862146eb5af32571e8a3c8b09c12f040428f81. No build/fixture failures. |
| CP-16 implementation | 3 files, 70 passed, 0 failed/skipped at 8a7a452cdb724bbee4509f9e6329eccc67748d99. |
| CP-23 Final whole client | 121 files, 1320 passed, 0 failed/skipped, including lint gate, at 8a7a452cdb724bbee4509f9e6329eccc67748d99. Run once; no inherited failures or flake reruns. |
| CP-16 after type repair | 3 files, 70 passed, 0 failed/skipped at bbefbe8fe265996599f27f295671719ef675b30e. |
| Typecheck / production bundle | `npm --prefix client run build` (tsc -b then Vite) passed at bbefbe8fe265996599f27f295671719ef675b30e. |

CP-16 per-file rosters: routingSettingsModel.test.ts **33**, RoutingSettingsTab.test.tsx **30**, complexityChains.test.tsx **7**. The exact three-file selection executed 70 and all passed. Client command rows produce Vitest summaries, not TRX; their CHECKPOINT counts and build provenance are intentionally n/a / notApplicable. Clean start/end SHA receipts and the fresh console aggregates were inspected for every run.

The first commit added the new DTO types, assertion tests, and a compile-only helper bridge preserving the old label behavior. RoutingSettingsTab production remained at master for that red run. The bridge was completely replaced by the implementation. No deliberate PC mutation was run.

Before the full client selection, the stated invariant was preserving routing display and adjacent client behavior, including the shared mock and lint gate. Scope was the bounded whole client suite, planned cost 10 minutes; no unbounded server classes were selected. Actual combined CP-16/CP-23 wall time was 4m15s. One client flow ran at a time. No assertion or production/test timeout was loosened.

## Checkpoint execution and provenance

Imported the landed plan to ignored `.antiphon/c097m-client.yaml`, set manifest rowMinutes to 40, and used `--row-timeout 40m --total-timeout 150m --serial`, without keep-outputs. Every run passed its committed HEAD via `--expected-source-sha`. `run` / `wait` were awaited until terminal. All selected rows had slot=granted and waited=0s.

Run directories (ignored generated reports, receipts, console logs):
- `.antiphon/checkpoints/20261014-130552-e272/` — red-first CP-16.
- `.antiphon/checkpoints/20261014-130954-e55b/` — green CP-16 and CP-23.
- `.antiphon/checkpoints/20261014-131710-0866/` — green CP-16 after the type repair.

Unedited CHECKPOINT lines:

```text
CHECKPOINT CP-16 commit=f0862146eb5af32571e8a3c8b09c12f040428f81 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=f0862146eb5af32571e8a3c8b09c12f040428f81 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-16 commit=8a7a452cdb724bbee4509f9e6329eccc67748d99 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=8a7a452cdb724bbee4509f9e6329eccc67748d99 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-23 commit=8a7a452cdb724bbee4509f9e6329eccc67748d99 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=8a7a452cdb724bbee4509f9e6329eccc67748d99 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-16 commit=bbefbe8fe265996599f27f295671719ef675b30e build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=bbefbe8fe265996599f27f295671719ef675b30e sourceState=clean buildSource=notApplicable
```

The manifest contains nine unused server build definitions; maxConcurrentBuilds was 0. No server build/test was launched by these selections. Separate wait invocations reported output-memory-authority-missing for the three tool shadow directories. Those exact run-created directories were manually removed after the executors were dead. The final green waits then returned exit 0 with Tool Cleanup AlreadyAbsent; no tests were rerun for cleanup.

Additional gated builds, each explained by the brief's tool/typecheck/bundle requirements:
- Bootstrap: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c097m-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c097m-tool/ --nologo`. Base SHA f7ba0684c5680e250473a2310e292e23179b3642, exit 0, slot=granted waited=0s. Two existing nullable warnings. The isolated bin-c097m-tool directory is removed before settlement.
- Build at c1f532fe2f5ed679d8e28d4fae8239511989d6d4: exit 0, slot=granted waited=0s.
- Build at 8a7a452cdb724bbee4509f9e6329eccc67748d99: exit 2, TS2769 in the new table's optional candidateText expectation. Owned and fixed by bbefbe8fe265996599f27f295671719ef675b30e; no baseline blame or relaxed assertion.
- Build at bbefbe8fe265996599f27f295671719ef675b30e: exit 0, slot=granted waited=0s, held=55s. Log: `.antiphon/c097m-build-repair.log`. Vite retains its existing large-chunk warning. The bundle is rebuilt in client/dist.

Rerun commands after bootstrapping the checkpoint tool:
```sh
dotnet tools/Antiphon.Checkpoints/bin-c097m-tool/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-14-card-0097-matrix-plan.md --out .antiphon/c097m-client.yaml
# Set timeouts.rowMinutes: 40 in that ignored manifest.
dotnet tools/Antiphon.Checkpoints/bin-c097m-tool/Antiphon.Checkpoints.dll run .antiphon/c097m-client.yaml --rows CP-16,CP-23 --serial --row-timeout 40m --total-timeout 150m --expected-source-sha <committed-HEAD> --max-wait 45s
# If exit 75, wait for that explicit run id until terminal.
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c097m-client-build -- npm --prefix client run build
```

## Changes and handoff

Per-file implementation diff from base to bbefbe8fe265996599f27f295671719ef675b30e:

| File under client/src | Added | Removed |
|---|---:|---:|
| api/complexityChains.ts | 11 | 1 |
| features/settings/RoutingSettingsTab.test.tsx | 97 | 8 |
| features/settings/RoutingSettingsTab.tsx | 63 | 13 |
| features/settings/routingSettingsModel.test.ts | 66 | 1 |
| features/settings/routingSettingsModel.ts | 46 | 4 |
| test/mocks/handlers.ts | 1 | 0 |

The candidate/evidence Markdown files are reporting artifacts. The API regression file is unchanged.

GET /api/runner-defaults and /api/session-runners both returned 200; defaults revision 2 and the eligible catalogue determined placement. No Runner or Platform was pinned. The observed server SHA was 893ddfceeebf4d13d9401848c535abada3983c27; the task did not activate changes there. Restart: none.

PC-24, PC-25 and PC-26 are **pending** for method-scoped SourceLanding Mutation. The tests-first red run and ordinary green runs discharge none. Mutation owns deliberate mutants, restoration and missing-control discovery, including the added degraded-read combinations. No approval or human choice is blocking.

Next: ordinary Review; the caller then lands original Code task 6812413b and commissions SourceLanding Mutation. Final publication SHA, full-range evidence-diff result, merge-tree result and clean-worktree confirmation are recorded in the settlement report and its ignored copy `.antiphon/task-6812413b.md`.
