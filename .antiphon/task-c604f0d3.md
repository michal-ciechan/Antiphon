# CARD-0506 S5 carry onto master — Code task c604f0d3

Outcome: origin/master 8995111253 merged into the S5 tip (3a1a84d46) with no conflicts. CP-1 and CP-2 are green. CP-3 (the full client suite) was red in 4 of 4 branch runs, each time on a single different test and never in a CARD-0506 file. On the identical client tree at master it was green in 2 of 2 runs. These are reported as findings only, nothing was fixed. CP-4 is pending on Windows.

## Merge

- Merge commit `f4f2d9aff9259f9203c3b8ff79fbb5301550abb9`, with parents 3a1a84d46 (the S5 tip) and 8995111253 (origin/master), on branch `feat/card-task-c604f0d3`.
- No conflict files. Git merged everything automatically, including ConcurrencySettingsTab.test, DispatchConcurrencyEditor.test and SettingsPage.test. ProjectConfig and ProjectSetupModal were not touched by master.
- After the merge, `client/` and `scripts/test-client.ps1` are byte-identical to master (`git diff --quiet 8995111253 HEAD -- client scripts/test-client.ps1`). The branch differs from master only by `tests/Antiphon.E2E/DispatchConcurrencySettingsE2ETests.cs`.
- `git merge-tree --write-tree origin/master HEAD` exited 0 (origin/master was still 8995111253 when this ran).
- `scripts/check-evidence-diff.ps1 -BaseRef 8995111253ae0b54b66c2d480aee76508d265220 -HeadRef HEAD` exited 0: `commits=2 entries=6 violations=0`.

## Checkpoints

Manifest: `.antiphon/checkpoints.yaml`, written by `import`, with rowMinutes 40, totalMinutes 150 and each row timeout ≥40 (ignored, not committed). Every run below tested source f4f2d9aff, `sourceState=clean`.

| Run | Row | Result |
|---|---|---|
| 20261014-060223-45ce | CP-1 | exit=1: environment only. In a fresh worktree CP-1 and CP-2 ran test-client's auto `npm ci` at the same time and esbuild's install hit `ETXTBSY`, so no tests ran. slot=granted waited=0s |
| 20261014-060223-45ce | CP-2 | exit=1, same `npm ci` race. slot=granted waited=0s |
| 20261014-060223-45ce | CP-3 | exit=1, 3 failed / 1263 passed / 1 skipped (1267), 118/120 files passed. Failures: `AgentHerdrPlacement.test.tsx` "create submits camelCase herdrWorkspaceLabel and herdrTabLabel" (20 s timeout); same file "settings modal pre-fills from detail and sends only changed fields" (`expected 'hOrch2' to be 'Orch2'`); `AgentsPage.test.tsx` "drafts agent fields from a description before creating" (20 s timeout). slot=granted waited=0s |
| 20261014-061029-3cd3 | CP-1 | **green**, 3 files, 120 passed, exit=0. slot=granted waited=0s |
| 20261014-061029-3cd3 | CP-2 | **green**, 4 files, 77 passed, exit=0. slot=granted waited=15s |
| 20261014-061029-3cd3 | CP-3 | exit=1, 1 failed / 1265 passed / 1 skipped: `hostStatsHubReceipt.test.ts` "bounds the receipt timeout while the SignalR connection never replies" (`receipt exceeded timeout plus 2 seconds`). slot=granted waited=300s |
| 20261014-063258-0abf | CP-3 | exit=1, 1 failed / 1265 passed / 1 skipped: `AgentAttachHerdr.test.tsx` "shows Attach… only for Herdr agents with no live session" (`Unable to find role="button" and name "Attach…"`). slot=granted waited=105s |
| CP-4 | — | **Not run, pending Windows.** It needs the desktop with UseAppHost=true (CARD-1236 manifest override). |

The lint gate (`client/src/lint.gate.test.ts`) passed in every CP-3 run; only the files named above failed.

The tool's `wait` exited 2 after every run with `refused: receipt-unreadable: JsonException: receipt-shape: outputPaths required for outputNames`. This is the known CARD-1236/CARD-1242 behaviour: the report verdicts above come from each run's `report.md` (exit=1 RED, or green rows).

Unedited CHECKPOINT lines:

```
CHECKPOINT CP-1 commit=f4f2d9aff9259f9203c3b8ff79fbb5301550abb9 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 dispatchConcurrency.test dispatchConcurrencyModel.test useSignalRInvalidation.test executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=f4f2d9aff9259f9203c3b8ff79fbb5301550abb9 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=f4f2d9aff9259f9203c3b8ff79fbb5301550abb9 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 DispatchConcurrencyEditor.test ConcurrencySettingsTab.test SettingsPage.test ProjectConfig.test executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=15s dirty=0 source=f4f2d9aff9259f9203c3b8ff79fbb5301550abb9 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=f4f2d9aff9259f9203c3b8ff79fbb5301550abb9 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=300s dirty=0 source=f4f2d9aff9259f9203c3b8ff79fbb5301550abb9 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=f4f2d9aff9259f9203c3b8ff79fbb5301550abb9 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=105s dirty=0 source=f4f2d9aff9259f9203c3b8ff79fbb5301550abb9 sourceState=clean buildSource=notApplicable
```

## Comparison against master (unlisted runs: each went through build-slot.ps1)

- A detached worktree at master 8995111253 ran `test-client.ps1` (full suite) twice: green both times, 120/120 files, 1266 passed, 1 skipped. Labels `c604-master-client-full` (waited 285s) and `-2` (waited 60s). Reason: the brief asked whether the red is also red on master.
- On the branch, `test-client.ps1 AgentHerdrPlacement.test AgentsPage.test hostStatsHubReceipt.test` was green: 3 files, 34 passed (label `c604-branch-flaky3`, waited 150s). Reason: to isolate the failures from the full suite.
- On the branch, `test-client.ps1` (full suite, same driver as the master runs, label `c604-branch-client-full-direct`, waited 30s) was red, 1 failed / 1265 passed / 1 skipped: `CardModal.test.tsx` "opens the edit dialog from the header actions, prefilled". Reason: to rule out the checkpoint tool as the cause.
- `diff -rq` of branch and master `client/node_modules` showed no differences, and neither `client/` has extra untracked files. The suite input is identical.

**Finding (not fixed).** The full client suite has load-sensitive failures. On the branch, a different single test in agents, hosts or board failed in each of 4 runs: AgentHerdrPlacement ×2, AgentsPage, hostStatsHubReceipt, AgentAttachHerdr, CardModal. Master was green in 2 of 2 runs on an identical client tree. No failure is in a CARD-0506 file and the branch changes no client file relative to master, so this cannot be attributed to S5. Interleaving with host load is the likely variable. The CP-3 repeat budget (3 normal) is spent; no loaded repetitions were used.

## Pending

- CP-4 / V-18: Windows desktop with UseAppHost=true.
- PC-1 to PC-9: method-scoped SourceLanding Mutation; not run here.
- Unit lane: not run, per the brief ("NO Unit lane").

## Cleanup

- Removed the master scratch worktree with `git worktree remove --force` on its literal path.
- Removed `tools/Antiphon.Checkpoints/bin` and `obj`, which my `dotnet run` created.
- No `bin-c506-e2e` was created, because CP-4 was not selected.
