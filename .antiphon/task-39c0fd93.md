# Task 39c0fd93 — rc client failure: SessionTerminal mouse "reset and instances remain independent" past the 20 s cap

## Outcome
Fixed by removing the waste. No timeout or assertion was changed. Branch feat/card-task-39c0fd93, base master f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0.
- a0260588e7cb7a18dc4d31f1435d6f9e1a19e93e: red guard. afterEach asserts disposed terminals release their style sheets. At base 14/14 cases fail (sheets 1 -> 133).
- d099bbfcd3b6d8396f736cea406283fa6b99487e: the fix (tested tip).

## Root cause (measured on server2 with a node:inspector CPU profile)
1. jsdom 29 defect. `Node._remove` calls descendants' `_detach` before it clears their `_cachedRoot`. A `<style>` inside a removed subtree therefore still reads as connected: it drops its sheet and then re-adds it. Each disposed xterm DOM renderer left ~794 rules in `document.styleSheets`. Over the file that grew from 10 sheets / 1,644 rules to 134 sheets / 34,982 rules.
2. xterm calls `getComputedStyle(.xterm-screen)` (padding-left/top in getCoordsRelativeToElement) on every mouse event. jsdom's cascade matches the element and its ancestors against every rule. With two live terminals one call cost ~29 ms; with the leaked sheets it cost 0.3–1.4 s per uncached call. That is why the cases late in the file (reset, ANY, DRAG) and getByRole in "mixed split" were slow. They were not waiting on timers or polls.

## Fix
- `client/src/test/detachedStyleSheets.ts`: `releaseDetachedStyleSheets()` re-assigns textContent on each disconnected owner `<style>`, so jsdom removes the sheet and does not re-add it. It is called in the global `src/test/setup.ts` afterEach (after cleanup) and in the mouse file's `closeTerminal`.
- In the mouse harness, `installGeometry` now supplies `.xterm-screen` padding through a `getComputedStyle` spy; all other elements and pseudo-elements still get the real implementation. `assertGeometry` checks the supplied value against jsdom's real cascade once per file. Padding is layout, the same kind of value the harness already supplies for offsetWidth and getBoundingClientRect.
- New guard: the mouse afterEach asserts `document.styleSheets.length` returns to its pre-test value.

## Measurements: SessionTerminal.mouse.test.tsx, server2 Linux, 5 runs each, base and after interleaved, one build slot lease per run (ms; ! = failed at the 20 s cap)
| case | base runs | base p95 | after runs | after p95 |
|---|---|---|---|---|
| panel rejects hover from backlog | 1923 2881 1769 4397 1860 | 4397 | 1082 1090 1039 1038 1345 | 1345 |
| panel rejects hover from live and reconnect | 1005 2867 1143 2565 1137 | 2867 | 253 274 255 250 273 | 274 |
| protocol 'X10' stays local | 4490 9451 3832 9236 3922 | 9451 | 975 783 756 739 777 | 975 |
| protocol 'VT200' stays local | 6316 10818 5317 11007 5583 | 11007 | 1026 754 1019 715 760 | 1026 |
| protocol 'DRAG' stays local | 13231 21146! 7320 7568 7561 | 21146 | 772 820 747 756 782 | 820 |
| protocol 'ANY' stays local | 22027! 12029 10358 9028 10066 | 22027 | 812 753 705 693 1053 | 1053 |
| normal drag copies selection | 8274 3438 3152 3341 3603 | 8274 | 254 285 283 248 313 | 313 |
| wheel scrolls local history | 1271 549 520 574 661 | 1271 | 663 532 522 524 571 | 663 |
| wheel without history stays local | 1615 552 724 919 796 | 1615 | 823 513 498 477 530 | 823 |
| mixed split modes retain order and input | 14822 8656 6581 6635 6034 | 14822 | 464 697 500 494 500 | 697 |
| reset and instances remain independent | 20791! 25926! 17520 15791 16166 | 25926 | 675 505 499 491 521 | 675 |
| alt click stays local | 3927 13724 3538 3862 3744 | 13724 | 440 398 381 375 421 | 440 |
| factory rejects incompatible mouse seam | 128 271 105 110 113 | 271 | 113 121 122 115 137 | 137 |
| factory disposes on setup failure | 138 363 122 114 119 | 363 | 108 107 109 107 135 | 135 |

File test-time sum (s): base 100 / 112.7 / 62 / 75.1 / 61.4; after 8.5 / 7.6 / 7.4 / 7.0 / 8.1.
Baseline hit the cap in 3 of 5 runs (reset ×2, DRAG, ANY). After: 0 failures, and the largest case p95 is 1.35 s.
In the full client suite under load, the mouse file's sum went from 129.5 s (DRAG 23.4 s and ANY 20.7 s FAILED, which reproduces the rc failure on Linux) to 19.6 s, all green.
The wrapper run before measuring (typecheck PASS, lint PASS) gave 14/14 green, tests 7.25 s against 58.87 s at the first baseline.

## Verification (all on d099bbfcd except the red run)
- Red guard at a0260588e: 14 failed / 14 (expected red).
- `scripts/test-client.ps1 SessionTerminal.mouse.test`: typecheck 0, lint 0, 14/14 passed.
- `scripts/test-client.ps1 SessionTerminal` (family): 2 files, 19/19 passed.
- Full client suite (`scripts/test-client.ps1 -JsonResultPath`): 128 files, 1848 passed, 1 skipped, 0 failed, 305.7 s. Baseline full suite with the three files at base: 2 failed (the mouse DRAG and ANY cases at the cap), 1846 passed.
- check-evidence-diff base..HEAD: commits=2 violations=0. merge-tree onto origin/master f32aed42e: clean.
- No checkpoint plan exists for this ad-hoc rc-repair brief, so there were no CP rows. The .NET Unit lane was not run because no .NET source changed. Profiling and probe runs (instrumented, uncommitted) were made only to locate the cost.

## (4) Other client cases above 5 s on Linux (full-suite run, base / after ms)
Same leak pattern: only the mouse file. Every other case over 5 s is a Mantine modal or form test driven by user-event: a different cost, and not near-identical to this one. The global purge trimmed most of them by about 10–30% in this one-sample comparison, which is noise-level evidence. The ones closest to the cap:
AgentRemoteControl "create modal disables the switch for an Unsupported default profile" 16249/13357; ProviderEndpointFields "project settings modal renders stored endpoints" 12906/10277; ProjectSetupModal "C458 new project submits worker workspace" 12491/10337; CardEditModal "failed note read never clears saved notes" 11637/9236; BoardPage "creating a card creates with Inherit…" 11518/9819; AgentCreateModal "defaults to Standing orchestrator…" 11190/7963; SettingsPage "C506 V16 controlled-tab" 11187/8406; CardEditModal "sends only the fields that changed" 10588/9681; ConcurrencySettingsTab "C506 V15 scope-selector" 9822/9959. About 60 more are between 5 and 9 s (DelegateModal, AgentsPage, ProjectSetupModal, DispatchConcurrencyEditor, AgentReplyStyle…). lint.gate (115.9/57.1 s) has its own 180 s budget.
They are not fixed here: the cost is a different mechanism and outside the 60-minute budget. Recommended follow-up: a Debug/Code card to profile AgentRemoteControl and ProviderEndpointFields, which are at 50–80% of the cap under suite load on Linux.

## Pending for Mutation
No PCs are declared. Candidate controls: (a) make `releaseDetachedStyleSheets` a no-op; the guard goes red, as shown by the committed red at a0260588e. (b) Make the spy return padding '5px'; the "harness screen padding" check goes red once per file.
