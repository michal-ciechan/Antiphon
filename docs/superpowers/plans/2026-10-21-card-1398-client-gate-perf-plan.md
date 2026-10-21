# CARD-1398 / CARD-1394: cheaper client gate, real wrapper exits, one npm ci

Date: 2026-10-21. Stage: Code. Task: `722446a4`.
Source inspected: `HEAD` `317d0a41aac68ab145266dbec07af914fbda5a64` (CARD-1393). `origin/master` `16a13c00de61a2ad72655f31f41b6e7c87c8a099` does not change these scripts.
Target: **master only**. No product-host restart. Code authoring plus the checkpoints below: about **50 minutes**. Ordinary checkpoint floor: **8 minutes**.

`scripts/test-client.ps1` already runs typecheck, then lint, then Vitest, and it already exits before Vitest when either gate is nonzero. Two holes remain. `tsc -b` does not treat the project as up to date (`noEmit` without `incremental`), so every scoped run pays a full typecheck. The committed witnesses call `typecheck-client.ps1` or `lint-client.ps1` directly, so their "Vitest was not started" checks cannot fail. A cold worktree can also run two `npm ci` processes into the same `client/`.

No separate Plan or TestDesign artifact was on `origin/master`. This file is the closed checkpoint list for that brief. The brief forbids a Unit lane and a Vitest suite. There is no affected integration class.

## Ground truth

| What the cards assume | What `317d0a41a` does | Consequence |
|---|---|---|
| `tsc -b` ignores `tsBuildInfoFile`. | `client/tsconfig.app.json` and `client/tsconfig.node.json` set `noEmit` and `tsBuildInfoFile` under `node_modules/.tmp`, and do not set `incremental` or `composite`. The gate runs `node .../tsc -b --pretty false`. | Every gate run typechecks from scratch. |
| Lint has no cache. | `scripts/lint-client.ps1` runs `node $eslint . --max-warnings 0`. | Every gate run lints from scratch. |
| Nothing exercises `test-client.ps1`'s failing exit. | `scripts/test-client-typecheck.ps1` and `scripts/test-client-lint.ps1` invoke the child scripts. `ControlRegex` treats `CLIENT TESTS EXIT CODE` as "Vitest started", but `lint-client.ps1` never prints that line. | Removing `exit $typecheckCode` or `exit $lintCode` from `test-client.ps1` stays green. |
| Two cold rows can `npm ci` together. | `test-client.ps1`, `typecheck-client.ps1`, and `lint-client.ps1` each install when their binary is missing, with no lock. | Overlapping checkpoint rows install into one `client/`. |
| The scoped-run figure is ~15-35 s. | `docs/testing-and-build.md` Preserved Gotcha #19 still says `attentionVisuals.test` is ~15-35 s. That predates the typecheck and lint steps. | The documented cost is stale until a warm scoped run is measured. |

## Decisions

### D-1 — Incremental typecheck without turning emit back on

Set `"incremental": true` on `client/tsconfig.app.json` and `client/tsconfig.node.json`. Keep `"noEmit": true` and the existing `tsBuildInfoFile` under `client/node_modules/.tmp`. Do not set `composite` (it requires declaration emit and fights `noEmit`). Leave the gate command as `node .../tsc -b --pretty false` so `npm run build`'s `tsc -b` uses the same setting.

Measured on this worktree: the first `tsc -b` after the setting wrote the tsbuildinfo files in 26.2 s (exit 0). The next `tsc -b` was 0.3 s and reported both projects up to date. `tsc --noEmit --incremental -p` did not short-circuit (about 7-12 s on a repeat). `tsc -b` with incremental returns 1 for a source diagnostic such as TS2322; the same diagnostic without incremental returns 2. `typecheck-client.ps1` maps that source-diagnostic 1 back to 2. A compiler failure that is not a `file(line,col): error TS` diagnostic stays 1. The `-Project` witness path is `tsc --noEmit -p` and already exits 2.

### D-2 — ESLint cache under an ignored path

The production lint command, working directory `client/`, becomes:

`node $eslint . --max-warnings 0 --cache --cache-location <client/node_modules/.tmp/eslintcache>`

`node_modules/` and `**/.tmp/` are already gitignored. Do not write `.eslintcache` in `client/`. Keep `--max-warnings 0`. Propagate ESLint's exit code unchanged. The `-Path` fixture invocation stays uncached so the one-file witness does not share the client cache.

### D-3 — One npm ci lock

`scripts/lib/client-npm-install.ps1` takes an exclusive `logs/client-npm-ci.lock` (`FileShare.None`, retry until 15 minutes) and runs `npm ci` only when a required binary is still missing after the lock is held. `test-client.ps1`, `typecheck-client.ps1`, and `lint-client.ps1` call that function and do not invoke npm themselves. `logs/` is gitignored. Neither script calls `scripts/build-slot.ps1`.

### D-4 — The wrapper witness invokes `test-client.ps1`

`scripts/test-client.ps1` accepts `-TypecheckProject <tsconfig>` and `-LintPath <file>`, forwards them to the child scripts, and still exits before Vitest on a nonzero child. Those switches are not vitest filters.

`scripts/test-client-gate-exit.ps1 -Case Wrapper`:

- Before the switches exist on executable lines of `test-client.ps1`, exit 1 and say the wrapper did not reject the fixture error before Vitest. Do not start `test-client.ps1` (that would typecheck, lint, and run Vitest).
- After they exist, run `test-client.ps1 -TypecheckProject tests/fixtures/card1387-typecheck/tsconfig.json` and require process exit 2, `TS2353`, `CLIENT TESTS EXIT CODE: 2`, `vitest was not started`, no `CLIENT LINT EXIT CODE`, and no `vitest.mjs` or `Test Files`.
- Then run `test-client.ps1 -TypecheckProject tests/fixtures/card1398-typecheck-ok/tsconfig.json -LintPath tests/fixtures/card1393-lint/bad.test.ts` and require process exit 1, `no-control-regex`, `CLIENT TESTS EXIT CODE: 1`, `vitest was not started`, a passing typecheck line, and no `vitest.mjs` or `Test Files`.
- Require `incremental` and `noEmit` in both client tsconfigs, `--cache` with `node_modules/.tmp` in `lint-client.ps1`, and two overlapping `Invoke-WithClientNpmCiLock` holders whose critical sections do not overlap.

The ok fixture is outside `client/` so `tsc -b` and `eslint .` do not see it.

### D-5 — One red, then one narrow green

No `Antiphon.Tests` build, no Unit lane, no Vitest suite, no `npm run build`. CP-1's nonzero exit is the witness, not a defect to fix inside S1. CP-2 is the only green row. A separate warm `attentionVisuals.test` timing, plus the typecheck and lint probes that explain it, are unlisted because the doc figure is not a checkpoint assertion. They are not a suite run.

### D-6 — Replace the stale scoped-run figure with the measured warm time

In Preserved Gotcha #19, replace `~15-35 s` with the single measured warm `scripts/test-client.ps1 attentionVisuals.test` wall time: **17.9 s**, exit 0, one file (`attentionVisuals.test.ts`, 31 tests) on 2026-10-21 after the incremental typecheck and eslint cache were warm. State that typecheck is incremental, lint uses the ignored ESLint cache, and `npm ci` takes the lock. Do not widen `TestClientFilterTests`'s 180 s budget. The 17.9 s run fits it.

### D-7 — Master only

Land on master through the normal stages. Do not open an rc backport. Product hosts track the rc tip.

## Slices

### S1 — Commit the red witness

Add this plan, `scripts/test-client-gate-exit.ps1`, and `tests/fixtures/card1398-typecheck-ok/`. Do not add the lock, the switches, `incremental`, or the cache yet. Run CP-1. The required red is a nonzero harness exit that says the wrapper did not reject the fixture error before Vitest.

### S2 — Make the gate cheaper and prove the wrapper exits

Apply D-1, D-2, D-3, D-4's forwarding, and D-6. Run CP-2. Do not start the Vitest suite.

## Verification design

TestDesign was skipped. This table is the closed checkpoint list.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---:|
| CP-1 | S1 | n/a | gate-red | `pwsh -NoProfile -File scripts/test-client-gate-exit.ps1 -Case Wrapper` | V-1 witness | nonzero harness exit; the wrapper did not reject the fixture error before Vitest. No Vitest summary | n/a | 2 |
| CP-2 | S2 | n/a | gate-wrapper | `pwsh -NoProfile -File scripts/test-client-gate-exit.ps1 -Case Wrapper` | V-1, V-2 | harness exit 0; type error exits 2 with TS2353 before Vitest; lint error exits 1 with no-control-regex before Vitest; incremental and noEmit stay set; eslint cache path is under node_modules/.tmp; npm ci lock sections do not overlap | n/a | 6 |

CP-1's nonzero exit is the witness, not a defect to fix inside S1. No row builds a .NET project. `Min` is `n/a` because these are command rows. No PC rows. Mutation has nothing to plant until a later pass finds a missing control.

| Id | Behaviour | Proof |
|---|---|---|
| V-1 | A type error and a lint error each make `test-client.ps1` exit nonzero before Vitest. | CP-2's two invocations above. CP-1 is the same command while the switches are absent. |
| V-2 | The typecheck is incremental, lint caches under an ignored path, and npm ci is serialized. | CP-2's static checks and the lock overlap. The warm scoped-run seconds are the D-6 measurement, reported beside CP-2, not as a second row. |

## Cost

Ordinary Code floor is 2 + 6 = 8 minutes. With authoring, one `npm ci`, the incremental probe, and one warm scoped file, `-ExpectAbout` is about 50 minutes.
