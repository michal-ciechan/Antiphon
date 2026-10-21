# CARD-1280 investigation: Floating UI mock boundary for agent-modal tests

Date: 2026-10-21. Task: b89d6095. Source measured: `326c91c7759522751213bf2c1bd24a1a9a1cb66e` (branch `feat/card-task-b89d6095` before this doc). Master only.

## Outcome

**Confirmed.** Mantine Popover still runs Floating UI positioning under `MantineProvider env="test"`. A `vi.mock('@floating-ui/dom')` in the Vitest setup does not intercept that work while the default Vite module runner is on, because Vitest externalizes the installed ESM packages and the native mocker refuses importers inside `node_modules`. Inlining `@mantine/core`, `@floating-ui/react`, and `@floating-ui/react-dom` makes `@floating-ui/react-dom`'s import of `@floating-ui/dom` a module-runner fetch, and the same setup mock then drops the positioning `getComputedStyle` count from 1,560 to 0. The three `AgentCreateModal` cases still pass, including the Assignment policy dropdown and the submitted `ManualConfirm` body. Non-positioning style reads stay positive (173).

Not done, noted: the setup mock and `server.deps.inline` list were applied only for the measured runs and reverted; they are not on this branch.

## Why the committed mock left the count unchanged

Failed Code attempt `6eab1517c94d92c5baab89a704680c4d83ab24d5` (report `e77130b13f5074441a3185a995c06aec297a55f4`, `docs/superpowers/evidence/2026-10-20-card-1280-code.md`) added `vi.mock('@floating-ui/dom')` in `client/src/test/setup.ts` and still recorded 1,560 positioning reads. This checkout's `client/package-lock.json` sha256 is `b5ed4aa8848580a01e013d16d6543ace85a75f685ca26a34a0b850c2973fdb49`, identical to that worktree, so the installed copies below are the packages this tree installs.

Installed: Mantine core 8.3.17, `@floating-ui/react` 0.27.19, `@floating-ui/react-dom` 2.1.8, `@floating-ui/dom` 1.7.6, Vitest 4.1.0, Vite 8.0.0, Node v22.23.3.

Import chain, from the installed ESM:

- `client/node_modules/@mantine/core/esm/components/Popover/use-popover.mjs:3` imports `useFloating` and `autoUpdate` from `@floating-ui/react`.
- `use-popover.mjs:100-116` always constructs `useFloating` with offset/hide/flip/shift/arrow/size middleware, and on open calls `autoUpdate` directly. `keepMounted` only clears `whileElementsMounted` (`use-popover.mjs:104`). It leaves the open effect in place.
- `@floating-ui/react/dist/floating-ui.react.mjs:7-8` re-exports `autoUpdate` and `computePosition` from `@floating-ui/react-dom` and calls `useFloating` from that package.
- `@floating-ui/react-dom/dist/floating-ui.react-dom.mjs:1-2` imports and re-exports `computePosition` and `autoUpdate` from `@floating-ui/dom`.
- `@floating-ui/dom/dist/floating-ui.dom.mjs:3-7` reads layout through `getComputedStyle` from `@floating-ui/utils/dom`.

`client/src/test/utils.ts:85-86` already passes `env: 'test'` to `MantineProvider`. That pins detached-reference visibility and skips transition waits (`utils.ts:32-45`). It does not skip `useFloating`. Current `client/vite.config.ts:116-131` sets no `experimental.viteModuleRunner` and no `server.deps.inline`.

Vitest 4.1.0 `dist/chunks/cli-api.DuT9iuvY.js:9249` defaults `experimental.viteModuleRunner` to true. `dist/chunks/base.DM0-RqVb.js:113-145` constructs `NativeModuleMocker` and calls `setupNodeLoaderHooks` only when that flag is false. With the default runner, `dist/chunks/cli-api.DuT9iuvY.js:10494-10502` externalizes a library module that is a valid Node import unless `server.deps.inline` matches it. The externalized file is loaded by Node, so its static imports never enter the module-runner mock check at `dist/chunks/startVitestModuleRunner.C3ZR-4J3.js:668-678`.

Turning the native runner on does not fix this edge. `dist/chunks/nativeModuleMocker.DndvSdL6.js:30-36` returns immediately when `parentURL` contains `/node_modules/`. `@floating-ui/react-dom` is that parent for `@floating-ui/dom`, so a hoisted `vi.mock` of `@floating-ui/dom`, `@floating-ui/react-dom`, or `@floating-ui/react` is skipped for Mantine's import. `resolve.alias` is the same boundary: Vite rewrites imports only in modules it transforms, and these packages stay external.

Mantine props are the same boundary. `env: 'test'` is already on, and the open `autoUpdate` effect still runs.

## Measured runs

Both runs used the same temporary witness in `AgentCreateModal.test.tsx` (reverted afterward): forward `window.getComputedStyle`, count frames whose stack contains `@floating-ui/dom/` or `@floating-ui/core/`, open Assignment policy, choose Manual confirm, submit, and log the counts. The witness is why these durations sit above an uninstrumented run. Comparison is between these two instrumented runs.

Command shape, through the host slot:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label <label> -SlotWaitMinutes 5 -- pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentCreateModal.test.tsx --no-file-parallelism --reporter=verbose
```

| Run | Slot | Result | Style reads | Case durations | Vitest |
|---|---|---|---|---|---|
| Baseline, current setup, no inline | `67bb8c16-d494-472f-a954-70544bf58e64` waited=0s held=15s maxcpucount=6 | 1 file, 3 passed, 0 failed, CLIENT TESTS EXIT CODE: 0 | total 1831, positioning 1560, other 271 | 4271 ms, 2081 ms, 2620 ms | tests 8.99 s, transform 356 ms, import 2.11 s, duration 13.22 s |
| Inline plus DOM mock, cold cache `/tmp/card-1280-vite` | `d35e9648-2724-409c-95a7-d989cb7b6af8` waited=0s held=19s | 3 passed, exit 0 | total 173, positioning 0, other 173 | 3023 ms, 1670 ms, 2352 ms | tests 7.06 s, transform 5.64 s, import 7.30 s, duration 16.98 s |
| Same winner, warm cache | `57861228-3916-43ab-89e5-42605d2edbe3` waited=0s held=19s | 3 passed, exit 0 | total 173, positioning 0, other 173 | 3320 ms, 2059 ms, 2372 ms | tests 7.77 s, transform 5.35 s, import 6.88 s, duration 16.95 s |

The winning run's first case reached Manual confirm, asserted the select value, and matched `assignmentPolicy: 'ManualConfirm'` on the posted body. Worker and Phone cases passed with their existing preset and reply-style assertions. The 173 remaining reads are stacks outside `@floating-ui/dom` and `@floating-ui/core`, so the real style engine is still what Testing Library and the components call.

Idle file wall rose by about 4 s because inlining `@mantine/core` transformed the barrel (transform stayed about 5.4 s on the warm rerun). Test-body time fell. CARD-1246's 20 s hang detector is the test body under CPU load, fed by these positioning reads. The measured change removes that read storm. The `cacheDir` used for the probe is not part of the change.

## Exact change for Code

Two edits. No product component change, no `experimental.viteModuleRunner: false`, no `server.deps.inline: true`, no Mantine prop change.

`client/vite.config.ts`, inside the existing `test` block:

```ts
server: {
  deps: {
    inline: [
      '@mantine/core',
      '@floating-ui/react',
      '@floating-ui/react-dom',
    ],
  },
},
```

`client/src/test/setup.ts`, hoisted with the other Vitest imports:

```ts
vi.mock('@floating-ui/dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@floating-ui/dom')>();
  const computePosition: typeof actual.computePosition = async (_reference, _floating, options) => ({
    x: 0,
    y: 0,
    placement: options?.placement ?? 'bottom',
    strategy: options?.strategy ?? 'absolute',
    middlewareData: {},
  });
  const autoUpdate: typeof actual.autoUpdate = (_reference, _floating, update) => {
    update();
    return () => {};
  };
  return { ...actual, computePosition, autoUpdate };
});
```

Inlining all three packages is required for this runner. The test's transformed module imports `@mantine/core`. That barrel must be transformed so Popover's `use-popover.mjs` is a runner module. `@floating-ui/react` and `@floating-ui/react-dom` must be transformed so `computePosition` is the mocked binding inside `useFloating`, not a Node import closed over by the real file. A file-level regex shorter than these three package names was not measured and is not equivalent until it is.

A test that needs real placement adds a hoisted `vi.unmock('@floating-ui/dom')` in its own file. Search of `client/src/**/*.test.tsx` found no import of `@floating-ui` and no Floating UI placement assertion. `placement` hits in those files are Antiphon placement fields. `SessionTerminal.mouse.test.tsx` was not re-run.

## Remaining uncertainties

- The full client suite was not run. Shared setup will stub positioning for every jsdom file.
- Clipping, target-width sizing, arrows, and collision geometry stay unproved, matching the existing plan.
- No CPU-stressor rerun. Proof is the call-count drop on this file, which is the plan's chosen signal.
- Warm-cache transform of inlined `@mantine/core` stayed about 5.4 s on this host. Suite startup cost is unmeasured.
