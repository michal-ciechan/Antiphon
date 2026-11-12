# Review round 1: Debug d268c50c harness hang fixes (g118 sleep, sr g4 pty-host)

- Review task: 4d63b6fe (CARD-1514). Round 1, Final profile, full scope.
- Subject: `origin/feat/card-task-d268c50c` at `cb8ab7f55d22ef173a83a20365c50dc812c6d445`, base master `56e96eabf698e9c1fcd462836ad32ed1fb259e83`. That is 10 commits, all test-side plus one investigation doc.
- Verdict: **CLEAN**. There are no blockers and no regressions. Four advisories follow. The native Windows proof is still owed and stays pending.

## Scope checked

- Production code is unchanged: `git diff 56e96eabf..cb8ab7f55 -- server src` is empty. `ScopedLandingGit` uses only the existing `protected virtual` `ConfigureProcess` and `StartProcess` seams (`server/Infrastructure/Git/LandingGit.cs:103,111`).
- No existing assertion was weakened or removed. Each C502 body and the multiline delivery body only moved into a `try`. Every change adds teardown or a new leak check.
- `RootFromHostPath` now builds its alternation from `TestSessionLogRoot.KnownPrefixes`. The regex is escaped, and overlapping prefixes such as `c502-v26` and `c502-v26-manifest` resolve by backtracking on the 32-hex suffix. `C1677TempFamilyRegistryTests` (3/3) still pins the alternation to the list.

## Windows job code (DescendantLeakScope)

- **Struct layout:** `ExtendedLimits` is 64 + 48 + 32 = 144 bytes on x64, which is the size of `JOBOBJECT_EXTENDED_LIMIT_INFORMATION`. The PID-list read is correct: offset 4 holds `NumberOfProcessIdsInList` and offset 8 holds the `ULONG_PTR` array.
- **Handle lifetime:** the job handle is closed exactly once, in `Dispose`, and `KILL_ON_JOB_CLOSE` covers a host that dies. The query buffer is freed in a `finally`. The root `Process.Handle` belongs to the caller's `using`. I found no handle leak.
- **Creation and assignment race:** `Adopt` runs after `Process.Start`. A child spawned before the assignment escapes the job, so the race can only cause a false negative (a missed leak), never a false positive. The code documents this. The pwsh worker, git and the 1 s `cmd` delay all give ample margin (see A-2).
- **Nested jobs, V28:** `ScopedLandingGit.StartProcess` assigns git to the test job before production `OwnedChildMarker.Attach` assigns it to the empty production job. Windows 8 and later nest an empty job under the process's existing job, so production behaviour should not change. If assignment did fail, production logs `repository_child_job unavailable reason=assign_win32_*`. The native run must show that line is absent.
- **False positive on a healthy host:** C24 checks while the killed worker's `Process` handle is still open. It stays green only if `JobObjectBasicProcessIdList` omits exited processes that still have open handles. The kernel marks those not-really-active, but only the native run can prove it.

## Linux token code

- The token is a fresh 32-hex GUID. The match needs `ANTIPHON_TEST_DESCENDANT_SCOPE=<token>\0` at offset 0 or right after a NUL, and the check runs again immediately before each `Kill`. The test host's own pid is skipped, and the host never carries the token: only `ProcessStartInfo.Environment` gets it.
- `A_process_outside_the_scope_is_never_seen_or_killed` is green. An unrelated process can match only if its environ carries the same GUID.

## Runs at the reviewed SHA (server2 Linux, build-slot leased, UseAppHost=false, native output)

| Run | Result |
|---|---|
| Antiphon.Tests: RepositoryMutationLeaseTests 32, DescendantLeakScopeTests 4, ProcessSpawnLimitTests 57, TestClassificationPolicyTests 41, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3 | 145/145 passed (174 s) |
| SessionRunner.Tests: RunnerSessionGenerationTests 5 (V25 real-runner-exe skipped, as at base), RunnerMultilinePromptDeliveryTests 2, PtyHostLeakSweepTests 2, TestSessionTeardownTests 5, C1677TempFamilyRegistryTests 3, ProcessSpawnLimitTests 2, guards 4 | 22 passed, 1 skipped, 0 failed |
| DerivedImpactIndexTests (impact index changed) | 26/27. `regenerate_script_reports_changed_then_unchanged` failed only because the regen driver dll was not built. After `dotnet build scripts/regenerate-derived-impact-index` the method passed 1/1 |
| check-evidence-diff 56e96eabf..cb8ab7f55 | commits=10 entries=0 violations=0 |
| merge-tree origin/master (56e96eabf) HEAD | clean, tree 891f92182 |

## Mutants (all restored; `git status` clean)

| Mutant | Witness | Result |
|---|---|---|
| `RootFromHostPath` back to the hand-written 7-prefix regex | `A_host_under_any_registered_prefix_root_is_identified` | red |
| `AssertHostGoneAsync` returns without killing or throwing | `A_host_alive_after_teardown_fails_the_leak_check_and_is_killed` | red |
| Linux `KillAll` kills nothing | `An_orphan_*`, `Kill_all_*`, `Dispose_kills_*` | 3 red |
| C24 hook double-forks `( sleep 91 >/dev/null 2>&1 & )` and `leaks.KillAll()` is removed | C24 | red: `pid=6669/image=sleep` survived 5 s |
| Same double-fork hook with `KillAll` restored (the fix) | C24 | green, so the fix reaches the reparented orphan |
| V28 hook double-forks `( sleep 92 ... & )` | V28 | red, with and without the C24 fix (see A-1) |

On Linux, C502_V26 relaunch cannot go red: the Linux PTY dispose path completes, and the leak needs Windows modern ConPTY.

## Native Windows proof owed (pending, not discharged here)

1. At `47da99716`, `/*/*/RepositoryMutationLeaseTests/C448_C24_KilledWorkerLeavesLiveGitChildFenced` is red and names `image=sleep`. At the tip it is green.
2. At `131883025`, `/*/*/RunnerSessionGenerationTests/C502_V26_kill_and_relaunch_publish_their_own_generations` is red ("outlived the test's teardown"). At the tip it is green.
3. At the tip, `RepositoryMutationLeaseTests`, `DescendantLeakScopeTests`, `RunnerSessionGenerationTests`, `RunnerMultilinePromptDeliveryTests`, `PtyHostLeakSweepTests` and `TestSessionTeardownTests` are green. That proves the job PID list does not report exited processes with open handles, and that nested assignment works. The V28 output must also contain no `repository_child_job unavailable reason=assign_win32_` line.
4. A rerun of antiphon `all-group-118` and session-runner `all-group-4` under the nightly-owned-process job harness shows no `completed-but-hung`.

## Advisories (go to cards, do not hold the land)

- **A-1 (product, Linux):** when a hook double-forks, production `LandingGit` cancellation leaves the orphan alive on Linux. `OwnedChildMarker` keeps the fence through environ but kills only the tree. This is the Linux twin of the Windows parent-pid-walk gap that the investigation doc already lists. V28's new check catches it, so it is a real guard. The shipped hook does not double-fork, so V28 is green today.
- **A-2:** the Windows scope adopts after start, so it can miss an early grandchild (a false negative only). A suspended start, or `PROC_THREAD_ATTRIBUTE_JOB_LIST`, would close the race. This is optional.
- **A-3:** the `[After(Assembly)]` sweep's `earlierRun` clause now covers 30 prefixes instead of 7. A concurrent same-`%TEMP%` assembly that started earlier and has a dead child could be swept. That policy and risk come from CARD-0206 and are not new.
- **A-4:** in C24, if `leaks.Adopt(process)` throws on Windows, the worker is not killed because the `try` has not started yet. That is low probability.
