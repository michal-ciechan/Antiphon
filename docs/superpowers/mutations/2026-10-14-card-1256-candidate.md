# CARD-1256 unbound Mutation candidate

Source: `feat/card-task-43b6edf5` (tested source `a0480581091c47dabffee0acda87c82c4776f5b1`).
Plan: `docs/superpowers/plans/2026-10-14-card-1256-cardmodal-edit-flake-plan.md`.
All controls are pending; none was run by Code. Method-scoped vitest filters (`-t "<name>"`).

| PC | Mutation | Test | Expected red |
|---|---|---|---|
| PC-1 | `CardModal.tsx`: the "Edit card" `onClick` no longer sets `editing` | `CardModal.test.tsx` -t "opens the edit dialog from the header actions, prefilled" | `getByRole('heading', { name: 'Edit #1' })` throws |
| PC-1b | `CardEditModal.tsx`: title `useState('')` | same | Title value `''` instead of `'Implement terminal'` |
| PC-2 | `boards.ts`: remove `onError` from `useAddBlocker` | `CardBlockers.test.tsx` -t "reloads the card after a stale-token refusal" | renamed title never appears |
| PC-3 | `boards.ts`: remove the `setQueryData` of `cardConcurrencyToken` in `useAddBlocker` | `CardBlockers.test.tsx` -t "the next write carries the token from the add response" | remove posts `token-1` |
| PC-4 | `useSignalRInvalidation.ts`: drop `['cards', 'detail', p.cardId]` | `useSignalRInvalidation.test.ts` -t "C1256" (2 tests) | `isInvalidated` false; token stays `token-1` |
| PC-5 | `boards.ts`: remove `onError` from `useRemoveBlocker` | none | **Missing control**: no test covers the remove-path 409 reload |

Not a PC: the shared `loadCardEditModal` promise. With a fresh `import()` V-1 still passes alone
(measured at S2), so it is defence in depth; the flake fix's evidence is the stress record.
