# Code fb91d78a: CARD-0506 load-flake repair round 2 (Review 2c7391a4 F1, F2)

Outcome: F1 is fixed. F2 is partly met. With the after files, every test stayed under 6.67 s in 9 of 10 loaded runs for SettingsPage and ConcurrencySettingsTab, but in only 8 of 10 for DispatchConcurrencyEditor. The editor outliers are `C506 V10 overall-queued` (8.0 s and 9.0 s). It was equally slow in the before copy in the same runs (7.6 s and 8.8 s), and the profile attributes that remaining cost to component re-rendering; see "Disclosure" below. CP-2 is green at 77/77. All 1,316 tests in the load loop passed, before and after.

- Branch `feat/card-task-fb91d78a`, worktree `/work/worktrees/task-fb91d78a` (runner mirror), Code task fb91d78a.
- Started from `117fcd7a97954724b3b0326a313741e68fb4bf3e`; landing owner remains the original Code task d8a8fabb-6248-4b2e-90db-e8b0a52f92e3.
- Fix commit `13fa4f6252337c9f3c239fa0cb807678042a5492`, test files only. No production code was touched.

## Changes

- **F1, `SettingsPage.test.tsx` `C506 V16 controlled-tab`:** both `history.back()` and `history.forward()` now go through `traverse()`. It is an async `act` that registers a `{ once: true }` popstate listener before the traversal and resolves on the real event. Every findBy/selected assertion is unchanged, and there are no sleeps and no timeout changes.
- **F2, `ConcurrencySettingsTab.test.tsx`:** new helpers `scope()` (test id `concurrency-scope`), `sw()` (role query inside `dispatch-concurrency-row-<Role>`) and `reason()` (test id). They are used in `chooseScope` and `C506 V15 scope-selector`. The `project-deeplink` test keeps its page-wide accessible-name queries.
- **F2, `DispatchConcurrencyEditor.test.tsx` `setValue`:** `userEvent.type(input, value, { skipClick: true })` after `userEvent.clear`, which already focuses and empties the input.

### Diff proof: every changed line that is not a lookup

- Added assertions: `expect(scope()).toHaveAccessibleName('Scope')`, `expect(reason()).toHaveAccessibleName(/Reason/)` and `expect(save).toHaveAccessibleName('Save')`. These keep the label checks that the page-wide role queries used to imply.
- Unchanged, with only the locator swapped: `toHaveValue('P')`, `not.toBeChecked()` ×2 and `toHaveValue('')` ×2. The Save click uses the same element through `const save`.
- `setValue`: `skipClick: true` (an interaction option, not an assertion).
- SettingsPage: the `traverse` helper (8 lines plus a 2-line comment), and `act(() => history.back()/forward())` became `await traverse(...)`.
- Comments: two JSDoc blocks.
- Nothing was removed: no assertion, test case or timeout.

## Profile (loaded single test, CPUs 0-2 plus a busy loop, `ZzProfile*` temporaries since deleted)

`C506 V15 scope-selector` before the fix, in ms, with the second loaded sample in brackets:

- Page-wide role queries:
  - `getByRole('textbox',{name:'Scope'})` 175 [452] vs test id 7 [20]
  - Review switch page-wide 399 [999] vs within row 9 [37]
  - Code switch page-wide 480 [1173]
  - Save button by role 208 [460]
- Clicks:
  - switch click 274 [802]
  - Save click 804 [1530]
  - option click (remounts the editor) 654 [1494]
- Renders: initial render 595 [1991], first data 784 [2131].

Click-variant probe on the same switch:

| Variant | ms |
|---|---:|
| `userEvent.click` | 368 |
| `pointerEventsCheck: 0` | 359 |
| `skipHover` | 340 |
| native `element.click()` in `act` | 311 |

So the remaining click cost is the editor's own re-render on each state change, not userEvent overhead. Weakening the pointer-events check would buy almost nothing, and it was not done.

## Load proof (10 runs, before and after in the same vitest invocation)

- Before copies `ZzBefore*.test.tsx` are the files at 117fcd7a9 (untracked, since deleted).
- Each run was one `scripts/test-client.ps1 --reporter=verbose` call over the six files, pinned with `taskset -c 0-2` beside an owned `node` busy loop on the same CPUs.
- One build slot: `c506-r2-load10`, waited=105 s, held=1536 s.
- Host loadavg was about 13-17 during the loop. That is lower than Review 2c7391a4's runs, and is why the before copy peaked at 8.8 s rather than 14.5 s.

Slowest test per file, in ms, after / before (bold is over 6,667):

| Run | Settings after/before | Tab after/before | Editor after/before | V16 controlled-tab a/b | V15 scope-selector a/b | S2F1 conflict mode-removed a/b | S2F2 refresh-fails a/b |
|---|---|---|---|---|---|---|---|
| 1 | 3761 / 3958 | 4500 / 6121 | 3041 / 2534 | 3761 / 3958 | 4500 / 6121 | 1899 / 1639 | 2195 / 1820 |
| 2 | 3670 / 4139 | 4767 / 5609 | 2348 / 2995 | 3670 / 4139 | 4767 / 5609 | 1742 / 1582 | 1804 / 1916 |
| 3 | 3864 / 4442 | 5285 / 5924 | 2427 / 3064 | 3864 / 4442 | 5285 / 5924 | 1559 / 1800 | 1730 / 2207 |
| 4 | 3807 / 4403 | 4311 / 5849 | 3282 / 3774 | 3807 / 4403 | 4311 / 5849 | 1667 / 1776 | 1640 / 1790 |
| 5 | 3686 / 4303 | 4580 / 5874 | **7996** / **7641** | 3686 / 4303 | 4580 / 5874 | 4295 / 5341 | 5438 / 5001 |
| 6 | 4052 / 4326 | 5886 / **8393** | 4984 / 5559 | 4052 / 4326 | 5886 / **8393** | 1842 / 1612 | 1687 / 1746 |
| 7 | **8843** / 4490 | 5787 / 6114 | **9041** / **8820** | 8843 / 4490 | 5787 / 6114 | 1588 / 1762 | 1883 / 2488 |
| 8 | 3950 / 4326 | **7022** / **8820** | 5730 / 5980 | 3950 / 4326 | **7022** / **8820** | 1640 / 1684 | 1858 / 1988 |
| 9 | 4041 / 4724 | 5382 / 6218 | 4808 / 5056 | 4041 / 4724 | 5382 / 6218 | 3764 / 3459 | 3894 / 3741 |
| 10 | 3260 / 4008 | 4178 / 5871 | 2393 / 3019 | 3260 / 4008 | 4178 / 5871 | 1501 / 2027 | 1828 / 2207 |

Pass counts:

| File | After | Before |
|---|---|---|
| SettingsPage | 40/40 | 40/40 |
| Tab | 60/60 | 60/60 |
| Editor | 520/520 | 520/520 |

Runs in which every test stayed under 6.67 s:

| File | After | Before |
|---|---|---|
| SettingsPage | 9/10 | 10/10 |
| Tab | 9/10 | 8/10 |
| Editor | 8/10 | 8/10 |

Medians:

| Test | Median after | Median before | Change |
|---|---|---|---|
| `scope-selector` | 4.9 s | 5.9 s | 17% faster |
| `controlled-tab` | 3.8 s | 4.3 s | |
| `conflict mode-removed` | 1.7 s | 1.8 s | no measurable change |
| `refresh-fails` | 1.85 s | 2.0 s | no measurable change |

The named editor cases `conflict mode-removed` and `refresh-fails` stayed at or below 5.4 s in all 10 runs, both before and after. The `skipClick` change gives no measurable gain, and it can be dropped if Review prefers.

### Disclosure

- **`controlled-tab` (8.8 s, run 7, after copy only):** this is one spike. The test now waits for the real popstate, so the wait time is spent on navigation and render, and its findBy no longer starts its 1 s default before the traversal has run.
- **`V10 overall-queued` (8.0 s and 9.0 s; before copy 7.6 s and 8.8 s):** it already uses only row-scoped and test-id lookups and paste. Its cost is 7 clicks and 2 saves, each re-rendering the 15-row editor. That cost belongs to the component, and this test-only task cannot reduce it further.
- **Page-wide negative query kept:** `queryByRole('checkbox', { name: 'Overall max parallel no limit' })` in that test was left page-wide on purpose. Narrowing it to the row would narrow what it asserts.

## Checks

- Static checks, in the loop slot:
  - `tsc -b`: exit 0.
  - `eslint --max-warnings 0` on the three files: exit 0.
- `src/lint.gate.test.ts` via test-client.ps1 in slot `c506-r2-lintgate` (waited=0 s): 1/1 passed, exit 0.
- **CP-2, checkpoint tool run 20261014-042356-a3e2, `--rows CP-2 --expected-source-sha 13fa4f62`:**
  - The report says GREEN.
  - Console: 4 files, **77 passed / 0 failed** (`CLIENT TESTS EXIT CODE: 0`).
  - The `run` command then printed `refused: receipt-unreadable: JsonException: receipt-shape: outputPaths required for outputNames`. This is the known tool defect, and it was not debugged.
  - Its CHECKPOINT line, unedited:
```
CHECKPOINT CP-2 commit=13fa4f6252337c9f3c239fa0cb807678042a5492 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ConcurrencySettingsTab.test.tsx src/features/settings/SettingsPage.test.tsx src/features/settings/ProjectConfig.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=13fa4f6252337c9f3c239fa0cb807678042a5492 sourceState=clean buildSource=notApplicable
```
  - The report also has `outputs: refused/kept bin-c506-e2e/`. No such directory exists.
- **Unlisted runs, all through build-slot.ps1:**
  - three loaded profiling runs of temporary `ZzProfile*.test.tsx` (waited 165, 420 and 135 s; plus a 0 s one). Two of them failed on my own probe lines, which looked up an absent textbox; the real files passed 114/114 in the third.
  - one isolated `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-fb91d78a/` to bootstrap the tool (waited 120 s);
  - the load loop;
  - the lint gate.
  - No whole-Unit lane, no full client suite, and no CP-3 or CP-4/V-18 (out of scope).
- Cleanup, literal paths only: `tools/Antiphon.Checkpoints/bin-fb91d78a`, `tools/Antiphon.Checkpoints/obj` (created by that build), `client/node_modules` (installed by test-client), and the Zz temporaries.
- `git merge-tree --write-tree origin/master(5f52b309b) HEAD`: exit 0.
- Mutation was not run; all PCs stay pending.
- Budget: 60 min planned, about 95 min actual. Most of it was slot waits (2-7 min each) and the 26-minute load loop.
