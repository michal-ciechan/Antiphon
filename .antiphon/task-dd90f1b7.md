# Task dd90f1b7 — CARD-1690 client test performance (Code, test-only)

## Outcome
Done, all green. Full client suite through `scripts/test-client.ps1` at 86f3984458e66e9b26ad9f1702a00c65f4aefb83:
typecheck 0, lint 0, 129 files, 1853 passed, 1 skipped, 0 failed. Wall time 235.8 s. The same-session
base run took 274.1 s and the card baseline was 370-393 s. Test-time sum 1,817 s -> 1,669 s.
Cases over 5 s in the full suite: 73 -> 54. Cases over 10 s: 9 -> 2. No assertion changed. No timeout was widened.

Branch feat/card-task-dd90f1b7, base master f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0. The 39c0fd93 fix was
not on master when I started, so I cherry-picked it with -x as e201989492553d16a041bbcd93eb1289616ce473 (from a0260588e) and 75a2fe01b64fa03bbe4cdea65741b4af4c4529e7 (from d099bbfcd).
It has since landed on master as 467471928/ab7a868b5, with identical client trees.

## Before/after timings
### Full suite (server2 Linux, one lease each, `test-client.ps1 -JsonResultPath`)
| | base 75a2fe01b | after 86f398445 |
|---|---|---|
| Duration | 274.13 s | 235.83 s |
| phases: transform / setup / import / tests / environment (s) | 65.0 / 116.9 / 376.9 / 1820.1 / 355.5 | 40.0 / 110.5 / 338.9 / 1671.8 / 351.9 |
| slot wait | 0 s | 225 s |
| files / passed / skipped | 128 / 1848 / 1 | 129 / 1853 / 1 (+5 guard cases) |

The lint.gate test took 54.2 s -> 104.3 s. It runs eslint, has its own 180 s budget, and the client lint config is unchanged, so I read this as host load. It is not counted in the gains above.

### File test-time sums (s), full suite base -> after
RoutingSettingsTab 263.3 -> 226.7; DispatchConcurrencyEditor 151.8 -> 135.9; ProjectSetupModal 128.4 -> 116.4;
DelegateModal 123.3 -> 88.1; CardEditModal 81.3 -> 38.0; BoardPage 79.0 -> 63.5; AgentRemoteControl create case 15.8 -> 8.2 s.

### 5 runs per file, the 21 slowest files run together (one lease per run; base = 75a2fe01b worktree, post = be2bcf8bf worktree)
Phase line per run, Duration (tests phase):
- base: 299.5 (1096), 274.6 (1002), 247.4 (945), 315.2 (1082), 249.5 (960); slot waits 0/30/75/90/300 s
- post: 198.3 (813), 200.7 (812), 219.9 (993), 272.1 (1133), 194.6 (838); slot waits 90/300/150/90/406 s

Host load average during the runs was 5-38. Runs base-4/5 overlapped post-1/2, and post-3/4 ran under heavier load. Treat single-run differences as noise; compare the medians.

| file (sum of case durations, s) | base runs | post runs |
|---|---|---|
| RoutingSettingsTab | 291.9 265.8 237.2 307.0 241.5 | 189.6 192.8 211.9 263.1 186.3 |
| DispatchConcurrencyEditor | 140.0 109.7 101.1 142.8 106.0 | 96.5 95.3 130.9 150.3 106.2 |
| ProjectSetupModal | 119.3 84.3 84.9 112.7 85.9 | 74.4 76.6 102.0 125.3 79.6 |
| DelegateModal | 108.7 80.4 75.8 86.2 78.2 | 51.1 49.9 70.7 76.9 54.3 |
| CardEditModal | 60.2 57.2 52.4 51.7 53.4 | 29.4 28.4 33.0 36.5 31.6 |
| AgentsPage | 54.4 54.1 48.3 47.7 50.5 | 45.0 45.0 64.6 65.7 48.7 |
| BoardPage | 51.4 47.0 45.9 45.6 48.5 | 41.3 41.1 54.4 58.9 44.1 |
| AgentRemoteControl | 19.2 22.5 21.1 20.0 20.4 | 16.4 16.4 18.8 20.9 15.6 |
| ProviderEndpointFields | 28.6 32.4 32.1 32.7 34.5 | 33.1 31.8 37.6 41.5 34.5 |
| other 12 files | unchanged within noise | |

Cases with p95 > 5 s across the 5 runs: 70 -> 58. Cases with p95 > 10 s: 4 -> 2.
Per-case highlights, p95 base -> post (ms): AgentRemoteControl create 13461 -> 8606; CardEditModal "sends only the fields that changed" 9032 -> 2573;
ProjectSetupModal C1282 12559 -> 7010; BoardPage "creates with Inherit" 6888 -> 5938; DelegateModal "sends an explicit tier" 6908 -> 1833.

## Where the time goes (in-worker node:inspector CPU profiles, probe config uncommitted)
- 32 RoutingSettingsTab matrix cases took 37.4 s CPU. 7.3 s of that went to Testing Library's `getElementError -> prettyDOM`: every unsatisfied waitFor/findBy poll pretty-printed the whole container into an error that waitFor then threw away. 11.1 s went to jsdom getComputedStyle (7.0 s of it from floating-ui positioning of Mantine Popover/Combobox, 2.7 s from Mantine ScrollArea). The rest is React dev rendering. After fix 1 the same selection took 30.3 s.
- AgentRemoteControl create case: 2.5 s of 5.6 s went to per-keystroke re-renders of the whole modal (27 `userEvent.type` keystrokes at ~93 ms each).
- ProviderEndpointFields and DispatchConcurrencyEditor: getByRole accessible-name computation, and React re-renders on clicks and inputs. That is product render cost, with no shared test waste. Style sheets are not the cause: 2 sheets / 10 rules during a RoutingSettingsTab render, 1.6 ms per getComputedStyle.
- Not applicable, checked: per-test Mantine provider setup (renderWithProviders is already minimal, env="test"), MSW handler setup (cheap in the profiles), waitFor intervals (default 50 ms), userEvent delays (time goes to renders, not timers).

## Changes (commits)
1. e708ee8f5 test (red): `client/src/test/setupGuards.test.tsx`
   - Style-sheet guard: a describe.sequential pair. Case 1 renders a `<style>` and records the sheet count. Case 2 asserts the count returned to the start without calling the purge, which relies on setup.ts's afterEach.
   - Polling-error guard: 3 cases.
   - At this commit the polling cases are red (1 dump vs 0; a timed-out waitFor printed the DOM twice, 2 vs 1). With `releaseDetachedStyleSheets()` removed from setup.ts, the sheet case is red too (1 vs 0 sheets left). Both probes are recorded in this session.
2. e9ad7c968 fix: `client/src/test/pollingElementError.ts`, installed from setup.ts. While waitFor polls (Testing Library's `_disableExpensiveErrorDiagnostics`), getElementError returns the message and the name `TestingLibraryElementError` without the DOM dump. A timed-out waitFor still prints the DOM at the timeout through its onTimeout. getBy outside waitFor is unchanged.
3. e82df5230: `userEvent.type` -> `pasteInto` (existing CARD-1246 helper) in AgentRemoteControl (create case), BoardPage (create dialog), CardEditModal (all but a single-character '!') and DelegateModal. Proof: each file is identical to HEAD~ after mapping pasteInto back to userEvent.type, apart from the import line. 0 expect lines changed.
   - Exposed latent leak: CardEditModal's 409 case failed deterministically once the cases ran faster. Earlier cases' save notifications stayed in the global Mantine notifications store (limit 5, autoClose 4 s), so the 409 notification queued and never rendered. Fix: `afterEach(() => notifications.clean())` in that file. 14/14 green after.
4. 4deb97dcd: the three C1270 D-1 read-state waits now use `C1270_D1_READ_WAIT_MS = 6_000` instead of the 1 s default, with a comment.
   - Why a case duration bounds the wait: a wait cannot outlast its case. D-1 case p95 under load was 1,058-2,296 ms (full suite, two 21-file runs), and the rc failure was 2,885 ms. 2x 2,885, rounded up, gives 6 s.
   - Labelled provisional: it rests on a few loaded samples, not a measured p95 of the wait itself. In the after full suite the stale-data case took 801 ms.
5. be2bcf8bf: ProjectSetupModal now pastes the project directory (4 sites, same proof as 3).
6. 86f398445: provisional per-test budgets, `it(..., fn, N)` with a comment at each site.
   - Formula: 2x the post p95 x 1.5. The 1.5 is the p75 of the full-suite/measurement-run ratio over 83 base cases (p50 1.32), rounded up to 1 s.
   - 29 sites (28 `it` + the RoutingSettingsTab V-2 matrix `it.each`) get 16-19 s. In the after full suite the worst case used 38% of its budget.
   - Diff check: only `})` -> `}, N)` plus comment lines.

## Decision for the caller: 26 cases whose formula budget is at least 20 s
For these cases, 2 x post p95 x 1.5 comes to 20 s or more. Setting that budget would raise them above the suite's single 20 s hang detector, which CARD-0069 (vite.config) and the no-widening rule forbid, so I left them at the global 20 s. They are the remaining flake exposure under heavy load. Post p95 (ms):
ProviderEndpointFields "renders stored endpoints" 11893; ConcurrencySettingsTab V15 scope-selector 11580; SettingsPage C506 V16 9967; ProjectSetupModal C458 9629;
AgentCreateModal "defaults to Standing orchestrator" 9256; AgentRemoteControl create 8606; AgentAutoCompact "starts empty" 8591; AgentReplyStyle "Terse" 8018;
AgentHerdrPlacement x2 7660/7210; AgentBundleAttachments "offers the catalog" 7621; AgentLaunchEnv 7311; CardEditModal "saves a new default platform" 7267;
ComplexityChainCellEditor "adds, removes" 7154; DelegateModal "omits runner" 7148; AgentsPage "edits an agent" 7054; AgentSessionBackend 7031;
ProjectSetupModal C1282 7010, five-step 6847, first-agent Phone/Normal 6620/6546, C1274 profiles failed 6401; ProviderEndpointFields Own endpoint 6861;
ProjectConfig commitOnSettle 6662; DispatchConcurrencyEditor V10 overall-queued 6453; AgentReplyStyle Phone 6404.
Their remaining cost is React dev rendering, accessible-name queries and floating-ui inside Mantine modals, which tests alone cannot remove without lowering fidelity. Recommended: a Debug/Plan card. Two directions to consider: a lighter render tree for these modal tests, or a cached getComputedStyle under an explicit fidelity argument.

## Verification
- Guard red probes (direct vitest under build-slot): base setup gives 2 failed / 5 (polling). Purge removed gives 3 failed / 5 (sheet + polling). At the fix: 5/5.
- Touched-file runs: AgentRemoteControl+BoardPage+CardEditModal+DelegateModal gave 89 with 1 red (the notifications leak, fixed); CardEditModal 14/14 after; ProjectSetupModal 28/28.
- 5 base + 5 post runs of the 21 files: all exit 0. One extra post run (after-1) was discarded, because I switched its worktree to be2bcf8bf while it ran (mixed source).
- Full suite via test-client.ps1 at 86f398445: PASS (see above).
- check-evidence-diff f32aed42e..86f398445: commits=8 entries=0 violations=0.
- `git merge-tree --write-tree origin/master HEAD` (origin/master e56e4d1b6) CONFLICTS in client/src/test/setup.ts. The cause: master now carries the same 39c0fd93 fix under different SHAs, and the 3-way merge sees both sides adding it. In a scratch worktree, a plain `git rebase origin/master` skips the two picked commits as already applied and replays all 6 task commits cleanly, with the same client delta. A rebase land is clean; a merge land is not.
- Not run: the .NET Unit lane and integration classes (no .NET source changed). No checkpoint plan exists for this ad-hoc brief, so there are no CP/V/R rows. Unlisted runs: CPU-profile probes, a style-sheet probe, the red probes and the touched-file runs above, all through scripts/build-slot.ps1. The measurement loops, probes and touched-file runs called node vitest directly under the slot, so lint/typecheck ran only in the final test-client.ps1 run.

## Pending for Mutation
No PCs declared. Candidate controls:
- (a) Make `installPollingElementError` a no-op: setupGuards polling cases go red (shown at e708ee8f5).
- (b) Delete `releaseDetachedStyleSheets()` from setup.ts afterEach: "the previous case left no sheet behind" goes red (shown in this session).
- (c) Drop `notifications.clean()` from CardEditModal: the 409 case goes red when run in file order.
- (d) Make the polling branch also dump the DOM: the poll case goes red.
