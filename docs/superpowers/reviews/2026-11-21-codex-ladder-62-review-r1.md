# Codex 6.2 ladder review, round 1 (Final/Full)

Subject: `origin/feat/card-task-ae8b6243` at `228789f0eede954c0ca7ce97fa69fc2b512f8876` (Codex author, Code task `ae8b6243-c56b-4cea-bb8a-383059354890`). Base `2afaf4bebfb763c12ace57849ed7dd17e5bfe0ca`, which is also `origin/master` `4aaeea4760983178357cf99c35bc2f0118247b9c`. Verdict: **CLEAN**. No ladder blocker. One red checkpoint method is an untouched Postgres deadlock and does not hold the land. Advisories below go to the caller. PC-1 and PC-2 stay pending.

## Behavior

Codex Frontier now resolves to `gpt-6.2-astra` with floor `0.169.1`. High and Medium stay `gpt-6.2-sol` (operator 2026-11-19) at `high` and `medium`. Low stays `gpt-5.6-luna`. Explicit `gpt-6-astra` stays delegatable. `MinimumCodexCliVersion` is still data only: create, retry, dispatch, warm reuse, and launch do not probe the CLI.

Warm reuse is exact and Frontier-only. `TryReuseWarmAgentAsync` sets `expectedWarmModel` to `ModelLevelAliases.ForCodex(Frontier)` (`gpt-6.2-astra`) only when the claim is Codex Frontier. The unpinned shop keeps sessions whose `EffectiveModelId` equals that slug. Null, empty, and `gpt-6-astra` do not match. The same check runs again in `LiveSessionIdOfAsync`. Role is not a separate path, so Review, Plan, TestDesign, and Code share it. `MayWarmPool` is false for any non-whitespace `RequestedModelAlias`, and `WithRequestedAlias` excludes any row that has ever carried one, so an explicit `gpt-6.2-astra` or `gpt-6-astra` takes a fresh process and cannot poison the pool. A ladder Frontier pool launch writes `session.EffectiveModelId = LaunchTierAlias(...)` and does not store `RequestedModelAlias`. High and Medium pool launches still leave `EffectiveModelId` null; they did not change slug, and the level match already separates them.

A busy pin returns `WaitForAgent` before the model check. An idle old-slug pin is rejected by the later `LiveSessionIdOfAsync` and spawns fresh, with the pin kept. That does not hand the Frontier task to `gpt-6-astra`. `ExistingProcessCanTakeAsync` is a memory prediction and does not apply the slug filter.

Frontier effort `xhigh` is the same switch as master. The diff in `CodexLaunchArgs.cs` is the comment that names `gpt-6.2-astra`. The 2026-11-19 catalog note lists `low, medium, high, xhigh, max, ultra` for `gpt-6.2-astra` on 0.169.1 and 0.170.0. CP-2's argv witness asserts `--model gpt-6.2-astra` and `model_reasoning_effort=xhigh`. The 2026-11-21 canaries used `high`, not `xhigh`.

An older CLI asked for a 6.2 slug fails in the CLI. It does not fall through to the previous model. Desktop and the standing server2 runner both report `codexCliVersion=0.170.0` (`versionStale=false`, `dispatchEligible=true`; observation error `stderr_output` is the permitted freshness note). Checked 2026-11-21 11:41:22 and 11:39:37. `server2-temp` is `unavailable=retired`, empty version, `dispatchEligible=false`. Runner defaults revision 2 still name `globalRunnerId=server2`.

The seven launch-fixture repairs and the two Frontier expectation updates still assert the launched slug. CP-2 is 140/140 and CP-3 is 107/107, including `CodexDelegateDispatchTests`, `NamedCodexAgentLaunchTests`, `AgentTaskPoolTests`, and `CodexCliObservationTests`. CP-14 is the seven-case reuse matrix, 7/7. The Linux `cmd.exe` to `Environment.ProcessPath` change and the profile-launch DI registrations are fixture setup so the argv path runs. `RolePolicyReadServiceTests` updates both the stored pin and the expected DTO together; other ladder tests call `ForCodex`.

Section 7 is rendered at runtime. CP-5 is 62/62 and includes the four Codex lines: Frontier `gpt-6.2-astra`, High `gpt-6.2-sol`, Medium `gpt-6.2-sol`, Low `gpt-5.6-luna`. CP-7 is 27/27, including `regenerate_script_reports_changed_then_unchanged`. The last subject commit `228789f0ee` is the index regeneration. This review did not rewrite the committed index. The driver build named in R-2 ran first because those tests pass `-NoBuild`: slot lease `04c07d05-b36f-47c2-a0dd-ebf01ea9bc50`, native output, `UseAppHost=false`, 0 warnings, 0 errors, 9.52 s.

Client JSON for the two named files: 73/73 tests, 0 failed, `success=true`. A recorded `gpt-6-astra` still labels as `gpt-6-astra`.

No Dockerfile, AppHost, or docker path is in the diff. Live `GET /api/version` is `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` (`land-v2`, `operator-shutdown-v1`), which is `origin/rc/2026-11-30`. On that commit `CodexFrontier` is still `Gpt6Astra` with a null floor. A master-only land does not restart product hosts. The standing runner image is already CLI 0.170.0 and this commit does not rebuild it. The new ladder is inert until a later rc contains `228789f0eede954c0ca7ce97fa69fc2b512f8876` and the server process restarts.

## Evidence

Receipt `20261121-105220-65a7` at `228789f0eede954c0ca7ce97fa69fc2b512f8876`. Source clean, `dirty=0`, `buildSource=verified` on the build rows. One cold `tests/Antiphon.Tests` build to `bin-codex62-ladder/`, 285.8 s. Started at 1-minute load 12.59. Wall 47m23s, execution 2122.304 s, admission 720.195 s. Tool exit 1 because CP-10 failed one method. `unlisted: none`.

| Row | Result |
|---|---|
| CP-1 | 1 passed, 0 failed |
| CP-2 | 140 passed, 0 failed |
| CP-3 | 107 passed, 0 failed |
| CP-4 | 129 passed, 0 failed |
| CP-5 | 62 passed, 0 failed |
| CP-6 | 84 passed, 0 failed |
| CP-7 | 27 passed, 0 failed |
| CP-8 | exit 0; client JSON 73/73, 0 failed |
| CP-9 | 53 passed, 0 failed |
| CP-10 | 76 passed, 1 failed |
| CP-11 | exit 0; `commits=11 entries=0 violations=0` |
| CP-12 | exit 0; digest `78497A2C9CC6C908D77259B0181A87AA4CA1B25F923AE358C16DD2DB2D1FA181` count 80, `phase=pass2`, approved |
| CP-13 | `policyHash=11897a91e82799521012ed5f457d8500549dba2fb50694ecca61e08b8fc57f2a` and `PinnedInputDigest=659ea09e42513920ce1e46c4479ef092728db7702b687a65742768d67d46886e` match the pins |
| CP-14 | 7 passed, 0 failed |
| CP-15 | 26 passed, 0 failed |
| CP-16 | 119 passed, 0 failed |
| CP-17 | 115 passed, 0 failed |
| CP-18 | 14 passed, 0 failed |

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` exited 0, tree `9ed8a1b65539a9d490aef91b3f7375973e2dd815`. No merge was made. OS-witness lint exited 0: no new OS token, six existing `OperatingSystem.IsWindows()` path comparisons in `AgentTaskDispatcher.cs` classified non-behavior (base 4428/8118/10215, head 4428/8120/10231), `tests/derived-impact-index.json` inventoried as unrecognized, `restartImpact.required` false.

An earlier closed run `20261121-103610-395b` exited 2 before the test build. The ledger refused `client/node_modules/@esbuild/linux-x64/bin/esbuild` and `client/node_modules/esbuild/bin/esbuild` (`selected file has multiple links`, one inode, nlink 2). That is npm's esbuild layout, not the ladder. The links were split in the ignored tree and the empty kept output was removed. The run above is the evidence.

## CP-10 does not hold the land

`AgentTaskLandNotificationPersistenceTests.C467_V07_ConcurrentSettlementAndExplicitCleanup` failed in 2.36 s with `LandFailurePersistenceException: Could not persist land failure`. The inner error is Postgres `40P01` deadlock while locking `AgentTasks`. The test starts two `FailAsync` calls with `Task.WhenAll`. `git diff 2afaf4bebfb763c12ace57849ed7dd17e5bfe0ca HEAD` is empty for `AgentTaskLandService.cs`, `LandingSafetyHarness.cs`, and the test file. The stack does not enter the ladder or the dispatcher. This review did not isolate-rerun the method: after the suite the 1-minute load was 22.09, above the start gate. The checkpoint tool therefore exited 1 and kept `bin-codex62-ladder/`. Treat that as an inherited race under host load, not as a Codex-ladder regression.

## Advisories

- The closed run's wall was 47m23s, past the 30-minute selection budget. Execution was 35.4 minutes and admission waits were 12.0 minutes. The caller owns that Debug task. This review did not file it.
- `xhigh` is pinned in argv and listed in the 0.169.1/0.170.0 catalog. No live Frontier turn in the 2026-11-21 canaries used `xhigh`.
- `docs/agent-kinds.md` line 545 still lists catalog defaults for `gpt-6-astra`, `gpt-6.2-sol`, and `gpt-6.1-sol`. Launch sets effort explicitly, and the ladder prose names `gpt-6.2-astra` at `xhigh`. The table does not say that `gpt-6.2-astra`'s catalog default is `low`.
- The 0.169.1 floor is not enforced. Desktop and server2 are on 0.170.0. A retired temp runner has no version.

PC-1 and PC-2 in `docs/superpowers/mutations/2026-11-21-codex-ladder-62-candidate.md` remain pending for SourceLanding Mutation.
