# CARD-0506 S1 — Code report (task 65ec918e)

Outcome: S1 (API client, pure draft model, SignalR invalidation) is implemented, pushed and green on CP-1 at
`2fefbde550bcb6cb390585dde4562f3b341b220c`: 3 files, 78 results, 0 failed, 0 skipped. S2–S5 untouched.

- Role Code, tier Frontier, round Final (profile v1), bound run (`ANTIPHON_TASK_TOKEN` present; value never printed).
- Branch `feat/card-task-65ec918e`; worktree `/work/worktrees/task-65ec918e` (server2 Linux mirror, Debian 12, 24 cores);
  landing owner = this Code task 65ec918e. Task base `e44bbe3adeeafc541ab8b987023fd3f1c4424475` (frozen test design).
- Plan: `docs/superpowers/plans/2026-10-13-card-0506-concurrency-settings-ui.md`.
  Test design (checkpoint source): `docs/superpowers/plans/2026-10-13-card-0506-test-design.md`.
- Card read: CARD-0506 (board Antiphon, InProgress). Backend CARD-0505 is landed on master; no missing dependency.

## Commits (fast-forward from base, all pushed)

| SHA | Content |
|---|---|
| `394a7c9cecd1b003a24151bdc01807f0163ac321` | tests first: `client/src/features/settings/dispatchConcurrencyModel.test.ts` (M, 67), `client/src/api/dispatchConcurrency.test.ts` (H, 5), `C506 V07 changed-event` added to `client/src/hooks/useSignalRInvalidation.test.ts` (I, 5→6) |
| `63660770805f64ceb4375fec4050d63375878291` | `client/src/api/dispatchConcurrency.ts` (DTO types, `dispatchConcurrencyKeys`, `useDispatchConcurrency`, `useDispatchConcurrencyRevisions` limit=10, `usePutDispatchConcurrency`, `isProjectScope`, `toPutDispatchConcurrencyRequest`); `client/src/features/settings/dispatchConcurrencyModel.ts` (`draftFromOverrides`, `overridesFromDraft`, `validateDraft`, `diffDrafts`, `isCleared`, `sourceLabel`) |
| `f43fc610382bf4f68a77fe4ad8739608879010ca` | `DispatchConcurrencyChanged` → `[['dispatchConcurrency'], ['agentTasks','pipeline']]` in `INVALIDATION_MAP` (D-9) |
| `2fefbde550bcb6cb390585dde4562f3b341b220c` | test correction: `C506 V05 project-zero` asserts every GET and the PUT named project P instead of exactly one GET (see CP-1 first run) |

Files changed in the task range: 6 (the plan's S1 closed list exactly; no other slice files). `git merge-tree --write-tree origin/master HEAD`
(origin/master `00fa6b884a5f127982cff26a677aae74e6666650`, freshly fetched) exit 0. `git ls-files` 7571 before this report commit. Worktree clean.

## Red-first proof (unlisted runs the brief requires; each under `scripts/build-slot.ps1`, slot=granted waited=0s)

1. `c506-s1-red1` at `394a7c9ce` (tests only), exact CP-1 filter: all 3 files fail to load —
   `Failed to resolve import "./dispatchConcurrency"`, `"../api/dispatchConcurrency"`, `"./dispatchConcurrencyModel"`;
   `Test Files 3 failed (3)`, `Tests no tests`, `CLIENT TESTS EXIT CODE: 1`. This is the only pre-slice red a new module's tests can show.
2. `c506-s1-red2` at `636607708` (modules present, map unchanged), `src/hooks/useSignalRInvalidation.test.ts` only:
   `6 tests | 1 failed` — `C506 V07 changed-event`: `TypeError: callbacks.get(...) is not a function` (no handler registered for
   `DispatchConcurrencyChanged`); the 5 existing R-4 cases passed. `CLIENT TESTS EXIT CODE: 1`.

Mutation-level reds (PC-1..PC-24) are not claimed run; they stay pending for SourceLanding Mutation.

## Checkpoints (closed list for S1 = CP-1 only; tool runs `dotnet run --project tools/Antiphon.Checkpoints -- start/wait`)

Run 1 `20261013-171537-161a` (RED, exit 1), unedited line:

```
CHECKPOINT CP-1 commit=f43fc610382bf4f68a77fe4ad8739608879010ca build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/api/dispatchConcurrency.test.ts src/features/settings/dispatchConcurrencyModel.test.ts src/hooks/useSignalRInvalidation.test.ts executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=f43fc610382bf4f68a77fe4ad8739608879010ca sourceState=clean buildSource=notApplicable
```

console.log: `Test Files 1 failed | 2 passed (3)`, `Tests 1 failed | 77 passed (78)`. Failure `C506 V05 project-zero`:
`expected [P, P] to strictly equal [P]` at `getIds` — the mutation's success invalidation (the V-6 behaviour) refetched the
project scope before the assertion ran. V-5's oracle is the route parameter and the five-field body, so the case now asserts
`getIds.every(id => id === P)` and `putIds` exactly `[P]`; PC-16/PC-17 detection (body `expectedRevision` 0 /
`expectedGlobalRevision` 7) is unchanged. No timeout widened; no production change.

Run 2 `20261013-171647-1ec8` (GREEN, exit 0, reruns=1 for CP-1), unedited line:

```
CHECKPOINT CP-1 commit=2fefbde550bcb6cb390585dde4562f3b341b220c build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/api/dispatchConcurrency.test.ts src/features/settings/dispatchConcurrencyModel.test.ts src/hooks/useSignalRInvalidation.test.ts executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=2fefbde550bcb6cb390585dde4562f3b341b220c sourceState=clean buildSource=notApplicable
```

console.log: `Test Files 3 passed (3)`, `Tests 78 passed (78)`, `CLIENT TESTS EXIT CODE: 0`. Expect cell met: 3 files, exactly 78.
Vitest's default reporter prints no per-file lines on a green run; split: M 67 (20 declarations = 6 + 6 + each(40) + each(6) +
each(4) + 1 + 4), H 5, I 6 (runtime-confirmed by red2). Evidence: `.antiphon/checkpoints/20261013-171647-1ec8/report.md`
(ignored, runtime-owned). `outputs: deleted bin-c506-e2e/` (nothing left; `find -name 'bin-*'` = 0). Tool bootstrap compile ran
under `build-slot.ps1` (`c506-cp1-start`, `c506-cp1-start2`); `wait` used `--no-build`. Repeat budget: 2 executions of CP-1
(1 required + 1 failure-driven rerun), no repeat proof.

## Other unlisted runs (all under `build-slot.ps1`, every slot granted with waited=0s)

| Label | Command | Reason | Result |
|---|---|---|---|
| `c506-s1-npm-ci` | `npm --prefix client ci` | fresh worktree had no `client/node_modules` (setup allowance) | 765 packages, 32 s |
| `c506-s1-lint`, `c506-s1-lint-list2` | `eslint . --max-warnings 0` in `client/` | lint gate is R-5 in CP-3 (after S5); checked S1 files now so S2–S5 do not inherit a red gate from this slice | 3 errors, 0 warnings, none in S1 files (below) |
| `c506-s1-lint-list` | same with `--format unix` | formatter removed in ESLint 9 | exit 2, no lint result |
| `c506-s1-tsc` | `tsc -b` in `client/` | CP-3's `npm run build` runs `tsc -b`; S1 test files are inside `include: ["src"]` | exit 0 |

## Inherited red (fact, not S1's to fix)

The client lint gate (`lint.gate.test.ts`, R-5/CP-3) reports 3 errors at this head: `client/src/features/delegations/placement.tsx`
5:17 and 11:17 `react-refresh/only-export-components`; `client/src/features/settings/RunnerDefaultsSection.tsx` 113:5
`react-hooks/set-state-in-effect`. Those files, `client/eslint.config.js`, `client/package.json` and `client/package-lock.json` are
byte-identical to the task base (`git diff --stat e44bbe3ad..HEAD` lists only the six S1 files), so the same result holds at base.
Already carded: CARD-0754 (RunnerDefaultsSection), CARD-0786 (placement.tsx), CARD-0473 (lint debt umbrella). CP-3 cannot be green
until those land; CP-3 is after S5 and outside this slice.

## V/R and PC status

- Passed in CP-1: V-1 (6), V-2 (6), V-3 (51), V-4 (4), V-5 (3), V-6 (2), V-7 (1); R-4 (5 existing invalidation cases).
- Deferred to later slices, not passed: V-8–V-14 (S2, CP-2), V-15–V-17 (S3, CP-2), V-18 (S5, CP-4); R-1, R-2, R-3, R-5 (CP-2/CP-3).
- PCs: all 49 pending for method-scoped SourceLanding Mutation, none run. Detectors now bound: PC-1–PC-15 (M), PC-16–PC-22 (H),
  PC-23–PC-24 (I). PC-25–PC-49 bind to S2–S5 tests not yet written. G-30/G-42 split check is an S2 (editor) concern.
- Evidence policy: `scripts/check-evidence-diff.ps1 -BaseRef e44bbe3ad… -HeadRef 2fefbde55…` → `commits=4 entries=0 violations=0`, exit 0.
  Rerun over the final head after this report commit is in the final message.

## Interface notes for S2 (editor)

- `useDispatchConcurrency(projectId?)` returns `DispatchConcurrencyScopeDto` (global | project union); `isProjectScope(dto)` narrows.
- `usePutDispatchConcurrency(projectId?).mutate({ expectedRevision, expectedGlobalRevision, overrides, reason })`; the hook adds
  `provenance: 'Human'`, trims `reason`, sends exactly five keys and invalidates the `['dispatchConcurrency']` prefix on success.
  Global scope passes `dto.revision` for both revisions; project passes `dto.revision` / `dto.globalRevision`.
- Model: `draftFromOverrides(dto.overrides, dto.supportedRoles)` → `ConcurrencyDraft` (`mode?` carried; one `RoleLimitDraft` per
  supported role plus any document-only role); `overridesFromDraft(draft)`; `validateDraft(draft, dto.ranges, reason)` →
  `Record<path, message>` using camelCase paths (`maxParallel`, `roles.Code.maxQueued`, `reason`); `diffDrafts(draft, saved)` →
  `{path,label,draft,saved}[]` (labels `Code max parallel`, `Overall max queued`); `isCleared(dto.overrides)`; `sourceLabel(source)`.
