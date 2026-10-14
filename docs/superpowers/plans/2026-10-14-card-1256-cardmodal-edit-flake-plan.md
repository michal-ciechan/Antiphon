# CARD-1256 — CardModal edit-dialog flake, stale-token blockers reload, CardChanged card detail

**Date:** 2026-10-14 · **Stage:** Code (tests first) · **Branch:** `feat/card-task-43b6edf5`
**Task base:** `db6fe765663ba604c087e899c2cb07b6297d8faf`

## Problem

1. `CardModal.test.tsx` "opens the edit dialog from the header actions, prefilled" fails under
   load and passes alone; it also failed at base `e3ea274f9` (Final Review 796e7783 of CARD-0100
   S5). The S5 author fix (`await import('./CardEditModal')` before the click) did not hold.
2. D3: after a 409 from a blocker add/remove with a stale card token, nothing reloads the card,
   so every retry sends the same stale token. The SignalR `CardChanged` mapping invalidates
   `['cards', cardId]`, which does not prefix-match the full-card key `['cards', 'detail', cardId]`
   (`boardKeys.card`), so a live event does not repair it either.
3. D4: no test proves that the token in the add response (`cardConcurrencyToken`) is written into
   the cached card and used by the next write. The existing add test re-serves token-2 from the
   card GET, so it passes without that write.

## Measured root cause of the flake (2026-10-14, server2, base `db6fe765`)

A scratch timing probe (not committed) rendered `CardModal` and clicked "Edit card":

| Probe | Cold (first test in file) | Warm |
|---|---|---|
| `CardEditModal` rendered standalone | 2 089 ms | 685–1 224 ms |
| click → "Edit #1" in the DOM, module pre-imported | 2 620 ms click + 2 435 ms more | 1–2.7 s inside the click |

`React.lazy` always suspends on the first render even when the module is cached: the lazy
payload only turns resolved in a `.then` callback, a microtask after the render that threw. The
click therefore commits the Suspense fallback, and the retry render of the dialog runs later as a
time-sliced Scheduler task. `findByRole` runs with React's act environment switched off and a
1 000 ms window; the dialog's retry render costs 1–2.5 s of CPU alone and several times that
under load, so the window's timer fires between render slices before the dialog commits.
Pre-importing the module does not change this, because the lazy payload still suspends once.

Reproduced at base: `CardModal.test.tsx` alone, 5 runs with 48 `nice 19` busy loops beside a
`nice 19` vitest (host load ~42): 3 of 5 runs failed with
`Unable to find role="heading" and name "Edit #1"`; a fourth failed
"creates with Inherit and stores private notes separately…" at the 20 s hang detector, its time
spent in `userEvent.type` keystrokes (2.9 s alone).

## Design

- **S2 / flake.** A new `client/src/features/board/cardEditModalLoader.ts` owns one memoised
  `loadCardEditModal()` promise; `CardModal` builds its lazy component from it. The test awaits
  that same promise before rendering, then clicks inside one `await act(async () => …)`. Act queues
  the click's render; the lazy suspends on an already-resolved promise, React's `.then` runs as a
  microtask, the retry lands in act's queue, and act flushes it to completion before it returns.
  The assertions become synchronous `getBy*` with no polling window. No timeout changes, no skip.
  The separate module keeps `CardModal.tsx` component-only for `react-refresh/only-export-components`.
- **S2 / cheaper create test.** The create test pastes its three values instead of typing them
  key by key; the asserted request body is unchanged.
- **S2 / D3.** `useAddBlocker` and `useRemoveBlocker` invalidate `boardKeys.card(cardId)` on any
  409 refusal. Stale-token and archived refusals share the code `conflict`; both mean the cached
  card is out of date, and the cycle/exists refusals cost one extra card read.
- **S2 / CardChanged.** The `CardChanged` mapping adds `['cards', 'detail', cardId]` when the
  payload names a card.

## Verification design

| ID | Test | Guards |
|---|---|---|
| V-1 | `CardModal.test.tsx` › `opens the edit dialog from the header actions, prefilled` | Flake: dialog present synchronously after the act-wrapped click; title/description prefilled. Stress repetitions at the fix commit (10 file runs under the base stressor, plus the full client suite) show 0 failures. |
| V-2 | `CardBlockers.test.tsx` › `reloads the card after a stale-token refusal so the retry carries the current token` | D3: 409 shown, card re-read (the other writer's title appears), retry posts token-2. Red at S1. |
| V-3 | `CardBlockers.test.tsx` › `the next write carries the token from the add response, not a later card read` | D4: card reads after the first are held open; remove posts token-2. Green at S1 (guards existing code). |
| V-4 | `useSignalRInvalidation.test.ts` › `C1256 CardChanged invalidates the changed card detail and no other card detail` | `boardKeys.card('c1')` invalidated; `c2` untouched. Red at S1. |
| V-5 | `useSignalRInvalidation.test.ts` › `C1256 a mounted full card refetches after CardChanged and carries the new token` | Mounted `useCard` refetches to token-2. Red at S1. |
| R-1 | Full `CardModal`, `CardBlockers`, `CardEditModal`, `useSignalRInvalidation` files | Neighbours unchanged. |
| R-2 | Full client suite incl. `lint.gate.test.ts` | Lint (react-refresh rule), whole-suite load. |
| R-3 | `tsc -b` over the client | The new loader's types and the test imports typecheck (vitest strips types). |

### Positive controls (pending for SourceLanding Mutation)

| PC | Mutation | Expected red |
|---|---|---|
| PC-1 | `CardModal.tsx`: build the lazy from a fresh `import('./CardEditModal').then(...)` instead of `loadCardEditModal` | V-1: the dialog is not in the DOM after the act-wrapped click (`getByRole` throws) |
| PC-2 | `boards.ts`: drop the 409 `onError` invalidation in `useAddBlocker` | V-2 |
| PC-3 | `boards.ts`: drop the `setQueryData` of `cardConcurrencyToken` in `useAddBlocker` | V-3 (remove posts token-1) |
| PC-4 | `useSignalRInvalidation.ts`: drop the `['cards', 'detail', cardId]` key from `CardChanged` | V-4, V-5 |
| PC-5 | `boards.ts`: drop the 409 `onError` invalidation in `useRemoveBlocker` | **No V covers it** — disclosed gap; Mutation decides on a missing control. |

### Checkpoints

One run through the checkpoint tool (`dotnet run --project tools/Antiphon.Checkpoints -- run`,
imported manifest with `rowMinutes: 40`, `--total-timeout 150m`, expected source SHA = committed
HEAD). The tool bootstrap build is the one unlisted, slot-gated build. Client rows build nothing.
Red-first runs at S1 and the stress repetitions run through `scripts/build-slot.ps1`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S2 | n/a | client-affected | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/CardModal.test.tsx src/features/board/CardBlockers.test.tsx src/features/board/CardEditModal.test.tsx src/hooks/useSignalRInvalidation.test.ts` | V-1..V-5, R-1 | 4 files; `Tests 49 passed`, 0 failed/skipped; `CLIENT TESTS EXIT CODE: 0` | n/a | 4 | false |
| CP-2 | S2 | n/a | client-full | `pwsh -NoProfile -File scripts/test-client.ps1` | R-2 | `CLIENT TESTS EXIT CODE: 0`, 0 failed/skipped, `lint gate` present | n/a | 12 | true |
| CP-3 | S2 | n/a | client-types | `node client/node_modules/typescript/bin/tsc -b client/tsconfig.json --pretty false` | R-3 | exit 0, 0 TypeScript errors | n/a | 2 | false |

Census (source, S2): `CardModal` 19, `CardBlockers` 6 + 2 = 8, `CardEditModal` 14,
`useSignalRInvalidation` 6 + 2 = 8 → 49 (`it(` census; no `each`, `skip`, `only` or `todo`).
