# Review ed8199e4 (round 1 of 3, Final): CARD rc client jsdom style-sheet purge and mouse test fix

Subject: Code 39c0fd93-78dc-4670-bcb0-18c746a1ae5b, branch feat/card-task-39c0fd93, tip 55581453eba557bb005f83a8e8675715b2b123e0 (report only on top of tested code d099bbfcd3b6d8396f736cea406283fa6b99487e; red guard a0260588e7cb7a18dc4d31f1435d6f9e1a19e93e; base master f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0).

Verdict: Clean. No D-n defects.

## (1) releaseDetachedStyleSheets (global afterEach)
- jsdom 29.0.0 source confirms the mechanism. `Node._remove` calls `_detach` on the removed subtree before clearing the descendants' `_cachedRoot` (Node-impl.js:1150 vs 1160). So `HTMLStyleElement._updateAStyleBlock` sees `isConnected` true and re-adds the sheet. After `_remove` returns, every descendant cache is cleared. `nodeRoot` only caches Document roots, so `isConnected` read later in afterEach is exact. A connected node cannot read disconnected. A detached node can read connected only during `_remove` itself, and that error is in the safe direction (the sheet is skipped).
- What gets purged: only sheets whose `ownerNode` is an `HTMLStyleElement` with `isConnected === false`. Per spec such an element has no sheet in `document.styleSheets`, so removing it is the spec-correct result. Sheets owned by `<link>` or SVG `<style>`, and constructed or adopted sheets (`ownerNode` null, not in `document.styleSheets`), are skipped. The textContent re-assign takes jsdom's `_childTextContentChangeSteps -> _updateAStyleBlock` path: `removeStyleSheet`, then an early return because the owner is disconnected.
- jsdom probe (scratch, using the same function body):
  - Kept: a connected head `<style>` (Vite css), an emotion-style head tag filled by appended text nodes (2 rules kept, computed colour unchanged), and a connected `<style>` inside a body subtree (Mantine CSS vars).
  - Shadow-root style: never in `document.styleSheets`.
  - Leaked subtree sheet: dropped.
- Throw risk: the function is not wrapped in try/catch. It only touches `document.styleSheets`, `ownerNode`, `instanceof`, `isConnected` and the textContent setter. All 128 files run under the jsdom environment, and no `@vitest-environment` override exists in client/src. A throw is therefore implausible, and if one happened it would be loud, not silent.
- Cost (probe): ~20 µs per call with 4 sheets and ~0.68 ms with 134 connected sheets. That is about 37 ms over 1,848 calls on normal files. A one-off purge of 120 leaked sheets took ~48 ms.

## (2) Supplied .xterm-screen padding
- xterm 6 calls `getComputedStyle` exactly once, for `padding-left`/`padding-top` in getCoordsRelativeToElement. The spy answers only for elements with class `xterm-screen` and no pseudo-element. Every other call goes through the captured real implementation. `vi.restoreAllMocks()` in afterEach removes the spy, and beforeEach installs it again.
- The check is not vacuous. `openTerminal` always calls `assertGeometry`, so the first terminal in the file compares jsdom's real cascade (the harness's own `.xterm-screen { padding: 0 }` rule) with the supplied `'0px'` and fails loudly if they differ.
- The test-body diff (f32aed42e..d099bbfcd) is additions only: no assertion removed or weakened, and no timeout changed.

## (3) Guard can go red
- Base (a0260588e test file + base setup.ts, direct vitest under a build slot): 14 failed / 14, every one on 'disposed terminals release their style sheets'. Sheet count went 7 -> 10 -> 28 -> ... -> 133.
- My throwaway mutant (`releaseDetachedStyleSheets` body made `return;`): 14 failed / 14 on the same guard. Restored with `git checkout --`.

## (4) Runs at d099bbfcd (each its own BUILD SLOT lease via scripts/test-client.ps1)
- Full client suite: typecheck PASS, lint PASS, 128 files. 1847 passed, 1 failed, 1 skipped; 393 s.
  - The failure is RoutingSettingsTab 'C1270 D-1 Plan effective cells read fetching with stale data' (2,885 ms). It is the first `waitFor` (default 1 s) on a textContent presence check at RoutingSettingsTab.test.tsx:742 and has no style or getComputedStyle dependence.
  - Isolation re-run `scripts/test-client.ps1 RoutingSettingsTab.test`: 191/191 passed; the case took 771 ms.
  - Classified as a load flake on loaded server2 (other builds ran during the run). It is not caused by this change and is not a defect.
- `SessionTerminal.mouse.test` alone: 14/14 passed. `SessionTerminal` family: 2 files, 19/19 passed.
- No test relied on leaked sheets. The only full-suite failure is unrelated to styles.
- Per-file timing: I made no base full-suite run of my own; Code's one-sample base/after comparison stands. Most expensive files in my run (test-time sum): RoutingSettingsTab 381 s (250 s alone), DispatchConcurrencyEditor 199 s, ProjectSetupModal 152 s, DelegateModal 138 s.
- check-evidence-diff f32aed42e..55581453e: commits=3 entries=1 violations=0. The one entry is the .antiphon report markdown.
- `git merge-tree --write-tree origin/master(f32aed42e) 55581453e`: clean (tree 9cf160d5c).
- .NET Unit lane: not run. No .NET source changed, and nothing in tests/, scripts/ or tools/ references setup.ts, detachedStyleSheets or the mouse test. No checkpoint plan exists, so there are no CP rows.

## Advisories (no action required for land)
- A-1: the global setup.ts call has no witness of its own. The mouse file calls the purge itself, so deleting the setup.ts line turns nothing red. The perf benefit for other files is not pinned.
- A-2: a leaked `<style>` with empty textContent is not purged, because assigning "" changes no children and so runs no change steps. It has zero rules and costs nothing; noted for completeness.
- A-3: RoutingSettingsTab takes 250 s alone and 381 s in the suite, with the C1270 D-1 table on default 1 s `waitFor`s. A candidate for the CARD-0069 cheaper-test follow-up Code already recommended.

## Pending for Mutation
None are declared; the PC candidates are in Code's report (no-op purge, padding '5px'). Both stay pending.
