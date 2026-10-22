# CARD-0105 repair report (Code task bde3f3ad, descendant of 3850bcf9)

Outcome: Review 74b6b98b D-1 repaired and verified. Three red witnesses (V-7, V-8, V-9) proven red
at 38c09034 by CP-7, the fix at df9db897 green on CP-8, CP-2, CP-4, CP-5 (whole `AgentsPage.test.tsx`,
35/35) with a validated receipt. CP-6 (Playwright) still cannot run on this Linux lane.

- Branch `feat/card-task-bde3f3ad`, worktree `/work/worktrees/task-bde3f3ad` (server2), start ref
  `0e62b31ab5978a24299e58879df9a56a4b24f9f9` (tip of `feat/card-task-3850bcf9`), CARD-0105 base
  `673a8c6d759b379294469985111e49ae5c8ede2b`. Landing owner: original Code task 3850bcf9's delta
  lands with this task (master only).
- Tested source `df9db8972b065333e10b7833718a2a0ee0c06bfe`; this report is committed on top of it.
- Plan (amended, S4): `docs/superpowers/plans/2026-10-22-card-0105-terminal-icon-context-menu-plan.md`.
  Evidence: `.antiphon/checkpoints/20261022-060706-b028/` (CP-7 red), `.antiphon/checkpoints/20261022-061047-445c/` (green).

## Commits

| SHA | Slice |
|---|---|
| 38c09034f3d18f2a3cd2ccfae8ea5373cc035834 | V-7, V-8, V-9 red witnesses + plan amendment (S4, CP-7/CP-8, CP-2/CP-5 counts) |
| df9db8972b065333e10b7833718a2a0ee0c06bfe | fix: local `useLongPress` hook, press-scoped click swallow, contextmenu cancel |

Files: `client/src/hooks/useLongPress.ts` (new, 71 lines), `client/src/features/agents/AgentsPage.tsx`
(+13/-4), `client/src/features/agents/AgentsPage.test.tsx` (+103), the plan (+17/-3). No
`client/package.json` change; `@mantine/hooks` `useLongPress` is no longer imported by this page.

## The defect and the fix

`@mantine/hooks` 8.3.17 `useLongPress` (read from `client/node_modules/.../use-long-press.mjs`):
`onMouseDown: start` for any button, `cancel` only on `onMouseUp`, `onMouseLeave`, `onTouchEnd`. A
held right button (release gives no click) or a touch ended by `touchmove`/`touchcancel` fired the
callback, which armed `swallowClick`; nothing consumed it until the next genuine left-click, which
then did nothing. Scroll also opened the menu at 500 ms.

Fix (plan D-7 fallback, brief-authorised): `client/src/hooks/useLongPress.ts` starts only for
`event.button === 0` or a touch; cancels on mouseup, mouseleave, touchmove, touchend, touchcancel
and unmount; returns `{ handlers, cancel }`. In `AgentCard`: `onPressStart` clears `swallowClick`
on every mousedown/touchstart (before the button gate), `closeMenu` clears it, and `onContextMenu`
calls `longPress.cancel()` after `preventDefault`. Kebab, `AgentCardDestinations`, the menu items,
`onKeyDown` (Shift+F10 / Menu key) and all labels are byte-identical.

## Checkpoints (tool runs; lines unedited)

CP-7 run `20261022-060706-b028` (RED as required; `validate` -> `report_failed`, as for any red run):
```
CHECKPOINT CP-7 commit=38c09034f3d18f2a3cd2ccfae8ea5373cc035834 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105 repair" executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=38c09034f3d18f2a3cd2ccfae8ea5373cc035834 sourceState=clean buildSource=notApplicable
```
console: `CLIENT TYPECHECK EXIT CODE: 0`, `CLIENT LINT EXIT CODE: 0`, `Tests 3 failed | 32 skipped (35)`,
V-7 and V-9 `TestingLibraryElementError: Unable to find role="button" and name "Start agent"` (the
reviewer's failure), V-8 `AssertionError: expected <div …> to be null` (menu open mid-scroll),
`CLIENT TESTS EXIT CODE: 1`.

Green run `20261022-061047-445c` at df9db897, `validate --rows CP-2,CP-4,CP-5,CP-8` ->
`CHECKPOINT SOURCE VALID source=df9db8972b065333e10b7833718a2a0ee0c06bfe rows=4`:
```
CHECKPOINT CP-8 commit=df9db8972b065333e10b7833718a2a0ee0c06bfe build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105 repair" executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=df9db8972b065333e10b7833718a2a0ee0c06bfe sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=df9db8972b065333e10b7833718a2a0ee0c06bfe build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105" executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=df9db8972b065333e10b7833718a2a0ee0c06bfe sourceState=clean buildSource=notApplicable
CHECKPOINT CP-4 commit=df9db8972b065333e10b7833718a2a0ee0c06bfe build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105 keyboard|C0105 long-press" executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=df9db8972b065333e10b7833718a2a0ee0c06bfe sourceState=clean buildSource=notApplicable
CHECKPOINT CP-5 commit=df9db8972b065333e10b7833718a2a0ee0c06bfe build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=df9db8972b065333e10b7833718a2a0ee0c06bfe sourceState=clean buildSource=notApplicable
```
console (each row typecheck 0, lint 0, `CLIENT TESTS EXIT CODE: 0`): CP-8 `Tests 3 passed | 32 skipped (35)`;
CP-2 `Tests 8 passed | 27 skipped (35)`; CP-4 `Tests 2 passed | 33 skipped (35)`; CP-5 `Tests 35 passed (35)`.
All rows `Serial: true`, so they ran one at a time; wall 3m06s.

CP-1 and CP-3 (the V-1/V-4 red-witness rows) were not re-run: their red was recorded in task
3850bcf9 at 39b92ef3 / 0ad457a0 and their selections are strict subsets of CP-2, CP-4 and CP-5,
which are green here (repeat-proof: no repetition required after green).

CP-6: NOT RUN. Same lane facts as task 3850bcf9 (no Playwright browser cache, no system libs, no
sudo on server2). Hand to the Windows desktop lane: rebuild `client/dist` with
`npm.cmd --prefix client run build`, `run --plan <plan> --rows CP-6 --expected-source-sha <tip>`,
delete `bin-c0105-e2e/` by literal path afterwards. The E2E case is unchanged by this task.

## Invariants

| ID | Outcome |
|---|---|
| V-1, V-2, V-3 | pass (CP-2, CP-5) |
| V-4 | pass (CP-4, CP-5) |
| V-5 (control: a completed touch long-press still swallows exactly its own trailing click, then a later click opens the terminal) | pass (CP-4, CP-5) |
| V-7 held right-click, Escape, left-click opens the CLI modal | red at 38c09034 (CP-7), pass at df9db897 (CP-8) |
| V-8 touchmove before the threshold cancels the long-press | red at 38c09034 (CP-7), pass (CP-8) |
| V-9 touchcancel after a long-press, next tap opens the CLI modal | red at 38c09034 (CP-7), pass (CP-8) |
| V-6 | not run (CP-6, Linux lane); unchanged source |
| R-1, R-2, R-3 | pass (CP-5 whole file, 35/35) |
| Inspection | `AgentCard` props unchanged; both dropdowns still render `AgentCardDestinations`; `Terminal <name>`, `Terminal menu <name>`, `Agent menu <name>` unchanged; no new package; `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 673a8c6d... -HeadRef df9db897...` -> `commits=10 entries=1 violations=0`, exit 0 (the one entry is the earlier task's committed Markdown report) |

Landable: `git fetch origin master` (now `daf19e530f0d652a2cbc60b1d43444506cb81b2d`, later than the
brief's f2b0f1748), `git merge-tree --write-tree origin/master HEAD` -> tree `dab56c43...`, exit 0,
no conflicts.

## Deviations and notes

- Plan D-7 local-hook fallback taken for a different reason than the export check: the Mantine
  hook exists but cannot be cancelled on touchmove/touchcancel/contextmenu without calling its
  `onTouchEnd` handler for other events; the brief allowed the fallback over fighting the library.
- The brief's "CP-2 expected 5" became 8: the three repair witnesses carry the `C0105` prefix so
  `-t "C0105"` selects them too. CP-5 35 = 27 pre-existing (incl. the two-case `it.each`) + 5 + 3.
- The swallow is cleared on every press start and on menu close rather than by a timeout.
- A 500 ms primary-button mouse hold still opens the menu (unchanged behaviour, now button 0 only).

## Unlisted builds and runs (each through `scripts/build-slot.ps1`; no test ran outside the tool)

- `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c0105r-cp/` (tool dll to run
  `run`/`start`/`wait`/`validate` without a pipe), slot granted after 30s; both `bin-c0105r-cp`
  directories (tools/Antiphon.Checkpoints, src/Antiphon.HostCleanup) deleted after `validate`.
- `npm --prefix client ci` (node_modules absent on the mirror; plan-named prerequisite), exit 0.

## Pending for Mutation (zero-PC plan; method-scoped candidates, unbound)

- Drop the `event.button === 0` gate in `useLongPress.onMouseDown` -> V-7 red.
- Remove `onTouchMove: cancel` -> V-8 red.
- Remove the `onPressStart` clearing of `swallowClick` -> V-9 red. Removing `onTouchCancel: cancel`
  alone leaves V-9 green (the press-start clearing covers it): missing-control candidate.
- Remove `swallowClick.current = false` in `closeMenu` alone -> no current red (press-start clearing
  covers the witnesses): missing-control candidate for Mutation.
- Remove the `swallowClick` check in `onClick` -> V-5 red (`dialog` null).
- Remove `longPress.cancel()` in `onContextMenu` -> no current red (V-7 gates on the button first):
  missing-control candidate.
- Earlier task's candidates (preventDefault in onContextMenu, onKeyDown, focus return, Open board)
  still stand. Candidate doc: `docs/superpowers/mutations/`.
