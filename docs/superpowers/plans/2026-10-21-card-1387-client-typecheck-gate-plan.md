# CARD-1387: typecheck the client gate before the release build

Date: 2026-10-21. Stage: Plan. Task: `7d10af1a`.
Source inspected: `origin/master` `dcb461ce03c9bd8ea7d0edebfc637af33917776b` (this branch tip; fetched 2026-10-21).
Target: **master only**. No product-host restart. Code authoring plus the proposed checkpoints: about **30 minutes**. Ordinary checkpoint floor: **8 minutes**.

The ordinary client gate is `scripts/test-client.ps1`. It runs Vitest and does not typecheck. `npm run build` is `tsc -b && vite build`, and `tsc -b` includes every file under `client/src`, tests included. A type error in a test file therefore stays green through the gate tasks and Review already run, and fails the release build. The known instance is `SessionTerminal.mouse.test.tsx` TS2353 (`cols`/`rows` on `ITerminalOptions` for `@xterm/xterm` 6.0.0), repaired on this master tip by `b71157f20938f2fe842ee896103aadcf95dad6d1`. The card names that repair as CARD-1350 (`025c3850`, type-only); that object is not in this clone. This plan does not re-fix that file.

TestDesign is a separate stage. The section below is the proposal it freezes. This file has no `### Checkpoints` table yet, so the checkpoint importer has nothing to run.

## Ground truth

| What the card assumes | What `dcb461ce` does | Consequence |
|---|---|---|
| The client gate is Vitest only. | `scripts/test-client.ps1` resolves `node_modules/vitest/vitest.mjs` and runs `node $vitest run` plus the caller's filter. It never invokes `tsc`. The verdict line is `CLIENT TESTS EXIT CODE`. | A scoped or full gate run can be green while `tsc -b` is red. |
| Test files are part of the release typecheck. | `client/package.json` `build` is `tsc -b && vite build`. `client/tsconfig.json` references `tsconfig.app.json` and `tsconfig.node.json`. `tsconfig.app.json` `include` is `src` and `noEmit` is true. There is no test exclude. | `tsc -b` typechecks `*.test.ts` / `*.test.tsx`. Vite's transform does not. |
| The lint gate does not cover this. | `client/src/lint.gate.test.ts` runs ESLint inside Vitest. A file filter that does not name it skips it. Nothing in that test calls `tsc`. | Copying the lint-gate pattern would miss the same filtered checkpoint rows that missed TS2353. |
| Release qualification typechecks only by building. | Nightly runs `npm run build`, then `npm run lint`, then `scripts/test-client.ps1` (`scripts/lib/nightly-tests-impl.ps1`). The client roster ids exercised by the release-gate fixtures are `lint`, `build`, `vitest`. | `tsc -b` already runs inside the release build. The hole is every earlier `test-client.ps1` row. A new roster id is not required to close it. |
| The known TS2353 is still on master. | `client/src/features/board/SessionTerminal.mouse.test.tsx` types `layout` as `ITerminalOptions & ITerminalInitOnlyOptions`. The same intersection is on `origin/rc/2026-10-20` at that line. `b71157f2` is an ancestor of master and not of `rc/2026-10-20` (`c419a8e111fb00788f30225f9bff982e7355914e`). | Do not reintroduce the error in the real test. The gate gap is still open on master and on that rc tip: neither `test-client.ps1` calls `tsc`. |
| A fast check can reuse the release typecheck program. | Both referenced tsconfigs set `tsBuildInfoFile` under `node_modules/.tmp`. | `tsc -b --pretty false` is the typecheck half of `npm run build`, incremental, and does not run Vite. |

No live `tsc` was run. The gap is the missing invocation, and the tree's layout typing is already the repaired form.

## Decisions

### D-1 — Run `tsc -b` from the script the gate already is

Add `scripts/typecheck-client.ps1`. `scripts/test-client.ps1` calls it with no project argument after the existing `node_modules` install check and before `node $vitest`. Every current and future client checkpoint row, and every Review rerun of those rows, typechecks because they already call `test-client.ps1`.

The production command, with working directory `client/`, is:

`node client/node_modules/typescript/bin/tsc -b --pretty false`

Use that local `node` path. Do not call `npx`, `npm exec`, or `tsc` from `PATH`. The CARD-0307 `npx.ps1` argv rewrite is why `test-client.ps1` already avoids `npx`. Propagate `tsc`'s exit code. Print one line the logs cannot lose:

`CLIENT TYPECHECK EXIT CODE: n  (PASS|FAIL - do not report this run as green)`

Tee the typecheck output to `logs/client-typecheck.log`. On a nonzero typecheck, do not start Vitest. Still print `CLIENT TESTS EXIT CODE: n  (FAIL - typecheck failed; vitest was not started)` and exit with `tsc`'s code, so a capped log still has the existing verdict line. On success, Vitest runs exactly as it does today, including `-JsonResultPath` and the filter splat.

Rejected: a Vitest twin of `lint.gate.test.ts` (filtered rows never select it). A sibling that checkpoint authors must remember to add (the next `test-client.ps1 <file>` row would skip it). `tsc --noEmit -p client/tsconfig.app.json` alone (`tsconfig.node.json` / `vite.config.ts` would still reach `npm run build`). Folding this into `npm run build` (Vite is the slow bundle the card leaves on the release path). A new release-roster id or a `test-execution-policy.json` edit (the nightly build already runs `tsc -b`; a roster change rewrites the frozen `lint`/`build`/`vitest` contract and the policy hash). A `-SkipTypecheck` switch (that is the lint-gate escape the gate exists to remove).

### D-2 — The witness is a fixture outside `client/`

`tests/fixtures/card1387-typecheck/tsconfig.json` plus `bad.test.ts`. One object literal types `cols` on a type that only has `lineHeight`, which is TS2353. `strict` and `noEmit` are on. Do not enable `noUnusedLocals`. Do not import from `client/`. The real `tsc -b` does not include `tests/fixtures`.

`typecheck-client.ps1 -Project <tsconfig>` runs, from the repo root:

`node client/node_modules/typescript/bin/tsc --pretty false --noEmit -p <tsconfig>`

`test-client.ps1` never passes `-Project`. Reintroducing TS2353 on `SessionTerminal.mouse.test.tsx` would make every client gate red, including other cards.

### D-3 — The caller holds the build slot

The script does not call `scripts/build-slot.ps1` or `Enter-AntiphonBuildSlot`. A checkpoint command row already acquires a slot before the command (`tools/Antiphon.Checkpoints/Execution/RunScheduler.cs`). A second acquire from inside the row can wait on the lease the row is holding.

Outside the checkpoint tool, the direct command is:

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label client-typecheck -- pwsh -NoProfile -File scripts/typecheck-client.ps1`

Proposed checkpoint filters below are the harness only. They do not wrap `build-slot.ps1` again.

### D-4 — One red witness, then one narrow green per behaviour

No `Antiphon.Tests` build, no Unit lane, no full Vitest suite, no `npm run build`.

| Id | Behaviour | Proof |
|---|---|---|
| V-1 | A test-file TS2353 fails the typecheck step. | `scripts/test-client-typecheck.ps1 -Case ExcessProperty` runs `typecheck-client.ps1 -Project tests/fixtures/card1387-typecheck/tsconfig.json`. Pass means the gate exit is nonzero, the output contains `TS2353`, and `CLIENT TYPECHECK EXIT CODE` is that same code. Vitest is not started. |
| V-2 | The clean client typecheck the gate will run exits 0, and `test-client.ps1` actually calls it. | `scripts/test-client-typecheck.ps1 -Case CleanClient` runs `typecheck-client.ps1` with no `-Project` and requires `CLIENT TYPECHECK EXIT CODE: 0`. The same case reads `scripts/test-client.ps1` and requires the typecheck script is invoked before `node $vitest`, with no `npx` and no `build-slot.ps1`. |

The harness is ASCII and uses the same PowerShell 5.1-safe shape as `test-client.ps1`. It is the card witness, not a new `scriptCensus` row. Do not edit `tests/test-execution-policy.json`.

`TestClientFilterTests` stays as it is. Do not rerun it unless S2 changes the Vitest argv splat. The typecheck prefix is before that splat.

### D-5 — Master only; no rc backport in this card

Land on master through the normal stages. Do not open an rc backport here.

The known TS2353 source repair is already on `origin/master` and the same typing is already on `origin/rc/2026-10-20`. Backporting the gate is not required to clear that incident. The gate itself is still absent on that rc tip. Product hosts track the rc tip, so client commits on the open rc line stay on the Vitest-only gate until the next rc cut from master or a later backport card. That backport is a separate dispatch if an operator wants the gate on `rc/2026-10-20` before the cut.

### D-6 — Placement

Read-only `GET /api/runner-defaults` and `GET /api/session-runners` returned 200 on 2026-10-21. Defaults revision is 2, kind defaults are empty, and a global default runner is set. The catalogue has eligible Windows and Linux capacity and one stale draining row. Those facts are not a pin.

The lane is portable PowerShell plus Node. Re-read both routes at dispatch. Omit `-Runner` and `-Platform`. The card is already `platform Any`; leave it. Do not embed a fleet host or address.

No session waits for input. Nothing in this card parks, releases, or deadlines a seat.

### D-7 — Document the verdict where the gate is documented

In S2, add two or three sentences to the existing client-suite bullet in `docs/testing-and-build.md` (Preserved Gotcha #19, the CARD-0069 / CARD-0307 paragraph). State that `test-client.ps1` runs `typecheck-client.ps1` (`tsc -b --pretty false`) before Vitest, that a nonzero typecheck skips Vitest, and that a direct typecheck outside the checkpoint tool goes through `scripts/build-slot.ps1`. Do not rewrite the bullet. Do not edit `server/Bundles/stage-review.md`: Review already reruns the plan's checkpoint rows, and those rows call `test-client.ps1`.

## Slices

### S1 — Commit the red witness (about 10 minutes)

Add:

- `tests/fixtures/card1387-typecheck/tsconfig.json`
- `tests/fixtures/card1387-typecheck/bad.test.ts`
- `scripts/test-client-typecheck.ps1` with `-Case ExcessProperty` and `-Case CleanClient`

Do not add `scripts/typecheck-client.ps1` yet. Commit and push. Run CP-1. The required red is a nonzero harness exit that says the typecheck gate did not reject the fixture. A missing script is that red. A harness bug, a zero exit, or Vitest output is not the witness.

### S2 — Add the gate and one narrow green (about 15 minutes)

Add `scripts/typecheck-client.ps1` as D-1 and D-2. Call it from `scripts/test-client.ps1` before Vitest. Apply D-7.

If `typescript/bin/tsc` is absent, reuse the existing npm ci install in `test-client.ps1` (and the same install in the typecheck script when it is invoked alone), then retry once. Keep both scripts ASCII.

Commit and push before CP-2 and CP-3. Do not start Vitest in either row. Do not run `npm run build`. Record `CLIENT TYPECHECK EXIT CODE` and the full SHA. Run `scripts/check-evidence-diff.ps1` over the Code task base through its pushed tip.

## Proposed verification

TestDesign turns this table into the file's first `### Checkpoints` table. Until that edit, this heading is not importer input.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---:|
| CP-1 | S1 | n/a | typecheck-red | `pwsh -NoProfile -File scripts/test-client-typecheck.ps1 -Case ExcessProperty` | V-1 witness | nonzero harness exit; the gate did not reject the fixture. No Vitest success line | n/a | 2 |
| CP-2 | S2 | n/a | typecheck-excess | `pwsh -NoProfile -File scripts/test-client-typecheck.ps1 -Case ExcessProperty` | V-1 | harness exit 0; gate exit nonzero; output contains `TS2353` and a matching `CLIENT TYPECHECK EXIT CODE`; Vitest not started | n/a | 2 |
| CP-3 | S2 | n/a | typecheck-clean | `pwsh -NoProfile -File scripts/test-client-typecheck.ps1 -Case CleanClient` | V-2 | harness exit 0; `CLIENT TYPECHECK EXIT CODE: 0`; `test-client.ps1` invokes the typecheck script before `node $vitest` and does not call `npx` or `build-slot.ps1` | n/a | 4 |

CP-2 and CP-3 do not share a tsbuildinfo file (fixture `-p` versus client `tsc -b`). CP-1's nonzero exit is the witness, not a defect to fix inside S1. No row builds a .NET project. `Min` is `n/a` because these are command rows.

## Cost

Ordinary Code floor is 2 + 2 + 4 = 8 minutes. With S1 and S2 authoring, `-ExpectAbout` is about 30 minutes. A cold `tsc -b` that exceeds CP-3's 4 minutes is reported with the measured time and the same row rerun. It is not a reason to run the Vitest suite or the Unit lane.
