# CARD-1393: lint the client gate before Vitest

Date: 2026-10-21. Stage: Code. Task: `dafd3039`.
Source inspected: `origin/master` `7c585199d7f528ac41c08368d54f1b8faa1d5bbd`.
Target: **master only**. No product-host restart. Code authoring plus the checkpoints below: about **40 minutes**. Ordinary checkpoint floor: **14 minutes**.

The ordinary client gate is `scripts/test-client.ps1`. CARD-1387 (`e41e830db`) runs `tsc -b` before Vitest. ESLint still runs only at `npm run lint` in the release qualification, so a lint error in a client test stays green through every gate row and fails the qualification. The known instance is `no-control-regex` on `SessionTerminal.mouse.test.tsx` (`/^(\x1b\[D)+$/`), repaired on this master tip by `15f742812`. This plan does not re-fix that file.

No separate Plan or TestDesign artifact was on `origin/master`. This file is the closed checkpoint list for that brief. The brief forbids a Unit lane and a whole Vitest suite. There is no affected integration class.

## Ground truth

| What the card assumes | What `7c585199d` does | Consequence |
|---|---|---|
| The client gate does not lint. | `scripts/test-client.ps1` calls `typecheck-client.ps1`, then `node $vitest`. It never invokes ESLint. | A scoped gate run can be green while `npm run lint` is red. |
| `npm run lint` is `eslint . --max-warnings 0`. | `client/package.json` `lint` is that command, working directory `client/`. `client/eslint.config.js` extends `js.configs.recommended`, which includes `no-control-regex`. | The gate command is that same binary and those same arguments. |
| The lint-gate Vitest file does not close the hole. | `client/src/lint.gate.test.ts` lints inside Vitest. A file filter that does not name it skips it. | The step has to live in `test-client.ps1`, as the typecheck step does. |
| Release qualification already lints. | `scripts/lib/nightly-tests-impl.ps1` runs `npm run lint` before `scripts/test-client.ps1`. | Do not add a roster id or edit `tests/test-execution-policy.json`. Leave the nightly lint where it is. |
| The known regex is still on master. | `15f742812` is an ancestor of this tip. The mouse test no longer has that regex. | Do not reintroduce it under `client/`. |

## Decisions

### D-1 — Run `eslint . --max-warnings 0` from the script the gate already is

Add `scripts/lint-client.ps1`. `scripts/test-client.ps1` calls it after the typecheck step and before `node $vitest`. A nonzero typecheck still skips both lint and Vitest. A nonzero lint skips Vitest.

The production command, with working directory `client/`, is:

`node client/node_modules/eslint/bin/eslint.js . --max-warnings 0`

Use that local `node` path. Do not call `npx`, `npm exec`, or `eslint` from `PATH`. Propagate ESLint's exit code. Print:

`CLIENT LINT EXIT CODE: n  (PASS|FAIL - do not report this run as green)`

Tee the output to `logs/client-lint.log`. On a nonzero lint, print `CLIENT TESTS EXIT CODE: n  (FAIL - lint failed; vitest was not started)` and exit with ESLint's code.

### D-2 — The witness is a fixture outside `client/`

`tests/fixtures/card1393-lint/bad.test.ts` contains `/^(\x1b\[D)+$/`. `eslint.config.mjs` beside it enables only `no-control-regex`. `lint-client.ps1 -Path <file>` runs ESLint with that directory as the working directory, so the client config is not used and `eslint .` from `client/` does not see the file.

The fixture is not a `scriptCensus` row and not a project file. Do not edit `tests/test-execution-policy.json`.

### D-3 — One shared install, and the caller holds the build slot

`test-client.ps1` runs `npm ci` once when `vitest`, `tsc`, or `eslint.js` is missing, then calls typecheck and lint. Each standalone script still installs if its own binary is missing. Neither script calls `scripts/build-slot.ps1`.

### D-4 — One red witness, then one narrow green

No `Antiphon.Tests` build, no Unit lane, no Vitest suite, no `npm run build`.

| Id | Behaviour | Proof |
|---|---|---|
| V-1 | A scratch test with `no-control-regex` fails the lint step. | `scripts/test-client-lint.ps1 -Case ControlRegex` runs `lint-client.ps1 -Path tests/fixtures/card1393-lint/bad.test.ts`. Pass means the gate exit is nonzero, the output contains `no-control-regex`, and `CLIENT LINT EXIT CODE` is that same code. Vitest is not started. Before the gate script exists, the harness exits nonzero and says the gate did not reject the fixture. |
| V-2 | The clean client lint the gate will run exits 0, and `test-client.ps1` actually calls it. | `scripts/test-client-lint.ps1 -Case CleanClient` runs `lint-client.ps1` with no `-Path` and requires `CLIENT LINT EXIT CODE: 0`. The same case reads `scripts/test-client.ps1` and requires the lint script is invoked after `typecheck-client.ps1` and before `node $vitest`, with no `npx` and no `build-slot.ps1`. It reads `lint-client.ps1` and requires `node $eslint . --max-warnings 0`. |

### D-5 — Master only

Land on master through the normal stages. Do not open an rc backport here. The regex repair is already on this master tip. Product hosts track the rc tip, so the gate reaches them at the next rc cut from master.

### D-6 — Document the verdict where the gate is documented

Add two or three sentences to the existing client-suite bullet in `docs/testing-and-build.md`. State that `test-client.ps1` runs `lint-client.ps1` after typecheck and before Vitest, that a nonzero lint skips Vitest, and that a direct lint outside the checkpoint tool goes through `scripts/build-slot.ps1`.

## Slices

### S1 — Commit the red witness

Add the fixture, `scripts/test-client-lint.ps1`, and this plan. Do not add `scripts/lint-client.ps1` yet. Run CP-1. The required red is a nonzero harness exit that says the lint gate did not reject the fixture.

### S2 — Add the gate and one narrow green

Add `scripts/lint-client.ps1` as D-1 and D-2. Call it from `scripts/test-client.ps1`. Apply D-3 and D-6. Run CP-2 and CP-3. Do not start Vitest.

## Verification design

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---:|
| CP-1 | S1 | n/a | lint-red | `pwsh -NoProfile -File scripts/test-client-lint.ps1 -Case ControlRegex` | V-1 witness | nonzero harness exit; the gate did not reject the fixture. No Vitest success line | n/a | 2 |
| CP-2 | S2 | n/a | lint-control | `pwsh -NoProfile -File scripts/test-client-lint.ps1 -Case ControlRegex` | V-1 | harness exit 0; gate exit nonzero; output contains `no-control-regex` and a matching `CLIENT LINT EXIT CODE`; Vitest not started | n/a | 4 |
| CP-3 | S2 | n/a | lint-clean | `pwsh -NoProfile -File scripts/test-client-lint.ps1 -Case CleanClient` | V-2 | harness exit 0; `CLIENT LINT EXIT CODE: 0`; `test-client.ps1` invokes the lint script after typecheck and before `node $vitest` and does not call `npx` or `build-slot.ps1` | n/a | 8 |

CP-1's nonzero exit is the witness, not a defect to fix inside S1. No row builds a .NET project. `Min` is `n/a` because these are command rows. No PC rows. Mutation has nothing to plant until a later pass finds a missing control.

## Cost

Ordinary Code floor is 2 + 4 + 8 = 14 minutes. With authoring and a cold `npm ci`, `-ExpectAbout` is about 40 minutes.
