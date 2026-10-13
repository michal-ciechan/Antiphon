# CARD-0506 S1 round 2 — F1 repair (Code task 48cc1d59)

Landing owner: Code task 65ec918e-9130-4b4c-82b0-845327dbb0c2 (feat/card-task-65ec918e). Continued from
1980c22232214301bf7d35e37e519b8ce3dffb01 on feat/card-task-48cc1d59 (worktree /work/worktrees/task-48cc1d59), no rebase.
Task base for evidence: e44bbe3adeeafc541ab8b987023fd3f1c4424475.

Commits: 2f8895c85166a9be58d2f1fa0b2d943e1d4c5a89 tests (red-first), 9f27bd32e1c923c15c76fb1df363791abd3adb79 fix, fd7550bd29f0d25df8a172f6f8e39a89469b0139 plan bindings/census (CP-1 green SHA), then this report (no source change).

## Red first (tests only, production at 1980c2223)

Run 20261013-173920-16b3, unedited line:

```
CHECKPOINT CP-1 commit=2f8895c85166a9be58d2f1fa0b2d943e1d4c5a89 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/api/dispatchConcurrency.test.ts src/features/settings/dispatchConcurrencyModel.test.ts src/hooks/useSignalRInvalidation.test.ts executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=2f8895c85166a9be58d2f1fa0b2d943e1d4c5a89 sourceState=clean buildSource=notApplicable
```

console: `Test Files 2 failed | 1 passed (3)`, `Tests 22 failed | 98 passed (120)`. Red rows (22):
- model: decode-document-role '__proto__' (`expected ['Code'] to strictly equal ['Code','__proto__']`), decode-supported-role __proto__
  (`expected [] to strictly equal ['__proto__']`), encode-role '__proto__' and roundtrip-role '__proto__' (`expected '{}' to be '{"roles":{"__proto__":…'`),
  diff-saved-only × 5 (`expected [] to strictly equal [{…(4)}]`), diff-draft-only × 5 (TypeError reading 'specified' / missing diff),
  source-label constructor/toString/hasOwnProperty/valueOf/__proto__ (`expected [Function Object] to be 'constructor'`, `expected {…(12)} to be '__proto__'`).
- API: keys-project-named-global, project-scope-own-property, project-id-path-encoded (MSW unmatched request for the unencoded path).
Already green at base (regression guards, not red-first): decode/encode/roundtrip for constructor/toString/hasOwnProperty/valueOf,
decode-supported for those four, source-label project/global/default/future-source.

## Green

Run 20261013-174203-f5d0, unedited line:

```
CHECKPOINT CP-1 commit=fd7550bd29f0d25df8a172f6f8e39a89469b0139 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/api/dispatchConcurrency.test.ts src/features/settings/dispatchConcurrencyModel.test.ts src/hooks/useSignalRInvalidation.test.ts executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=fd7550bd29f0d25df8a172f6f8e39a89469b0139 sourceState=clean buildSource=notApplicable
```

console: `Test Files 3 passed (3)`, `Tests 120 passed (120)`, `CLIENT TESTS EXIT CODE: 0`. New census: M 106 (67 + 39), H 8 (5 + 3), I 6 = 120.

## Unlisted runs (reasons)

- Tool bootstrap: `build-slot.ps1 -Label c506-f1-red-start -- dotnet run --project tools/Antiphon.Checkpoints -- start …` (slot granted, waited 0s; 1 existing CS8602 warning). Green start used `--no-build` (c506-f1-green-start, granted 0s). tools/Antiphon.Checkpoints/bin,obj deleted afterwards.
- Reviewer probe (brief requirement): ef9c0e6b model-probe.mjs with paths pointed at this worktree, `build-slot.ps1 -Label c506-f1-review-probe -- node …` at the fix tree: `{"passed":15,"failed":0,"total":15}` (slot granted, waited 0s).
- Typecheck of the generic `setOwn` change: `build-slot.ps1 -Label c506-f1-typecheck -- npx --prefix client tsc -p client/tsconfig.app.json --noEmit`: no errors (granted, waited 0s).

## Sweep (S1 modules, lookups keyed by server- or caller-provided strings)

dispatchConcurrencyModel.ts — fixed: doc.roles / doc.mode reads (own), docRoles[role] (own), roles[role]= in decode and encode
(defineProperty), `role in draft.roles` (replaced by Set over own keys), draft.roles[role]/saved.roles[role] in diff (own),
SOURCE_LABELS[source] (Object.hasOwn). Checked, no change: decodeLimit (already Object.hasOwn), Object.entries/keys iteration
(own enumerable only), validateDraft error keys (always prefixed `roles.`/fixed names, never a bare prototype name), isCleared (Object.keys).
dispatchConcurrency.ts — fixed: isProjectScope `'projectId' in dto` → Object.hasOwn; query key global sentinel 'global' → null
(a project id 'global' shared the global cache entry); project id now encodeURIComponent in the route. Checked, no change:
toPutDispatchConcurrencyRequest (fixed field copies), HISTORY_LIMIT query string (constant).
useSignalRInvalidation.ts — DispatchConcurrencyChanged entry ignores its payload and returns constant keys; event dispatch is
connection.on(name) per mapping, no name-keyed dictionary. No change.
Public shapes unchanged (ConcurrencyDraft.roles and the encoded overrides stay ordinary objects; the keys' prefix is unchanged).

## Plan

docs/superpowers/plans/2026-10-13-card-0506-test-design.md: PC-6..PC-14 selectors bound to the quoted runtime titles
(e.g. `C506 V03 'role-parallel' 'below'`, `C506 V03 reason '401'`, `C506 V03 'queued-8-reject'`); PC-15 unchanged (plain title).
CP-1 expect 120; CP-3 expect 1212.

## Other

PCs: all pending for SourceLanding Mutation (none run). Inherited lint reds (CARD-0754/0786) not run here. No timeout widened,
no assertion weakened; production changes only in the two S1 files.
