# rc/2026-10-18 Linux re-gate

Status: **roster red, one new failure**. This is not an rc-profile complete-green receipt and not a publication.

Candidate: `42cbbd0417a2611f3a690e6671b13c9ba51fbf34` (`rc/2026-10-18`, also `candidate rc-2026-10-18-42cbbd0417a2`).
First gate, for comparison only: `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` (`docs/investigations/2026-10-18-rc-linux-lane.md`).
Previous rc tip used for the inherited check: `61117dd1663d2f47ce4708c18b609b8936491986` (`rc/2026-10-15`).
Host: Debian 12 container `a6457bc89508`, pwsh 7.5.4, .NET SDK 10.0.402, test host .NET 9.0.21, TUnit 1.44.0 / Microsoft Testing Platform 2.2.2, node v22.23.3, npm 10.9.9. Build-slot broker `http://127.0.0.1:8080/build-slots`. Production session-runner sessions were not used; the suite cleared the runner base URL to `http://127.0.0.1:1`.

The assigned branch `feat/card-task-541c1ad7` is fast-forward-only from `c8bc95276ad1dfa22fcfcd0d1a2b3b2c6a1d80b0`. That commit and the candidate have diverged (merge-base `a1d24b6fc92d45ae5274b418bf54c6b8819a8697`), so the roster was not checked out onto the task branch. It ran in a detached worktree this task created at the candidate, `/tmp/rc-regate-541c1ad7`. Checkout HEAD during the build and the roster was the candidate. No product or test file was edited.

## What ran

Same method as `docs/investigations/2026-10-18-rc-linux-lane.md`: the reproduction in `docs/investigations/2026-10-15-card-1287-linux-unit.md`, one isolated build, then one no-build execution of the frozen filter. Filter SHA-256 `9ba95d1db683438f6c382422c9ca4a596d2280930f100da9973b657596020ad7`. Roster file is 373 class names. `UseAppHost=false`. Output path `bin-rc1019-541c1ad7/`. `MSBUILDDISABLENODEREUSE` and `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER` were set. Headed, distiller, broker, and the same task-token variables as that lane's driver were unset (`ANTIPHON_HEADED_TESTS`, `ANTIPHON_HEADED_LONG_TESTS`, `ANTIPHON_DISTILLER_APPLY_CANARY`, `ANTIPHON_TG_TEST_TOKEN`, `ANTIPHON_CARD0133_RUN_P3`, `ANTIPHON_CARD0133_CODEX_EXE`, `ANTIPHON_BROKER_TESTS`). `TUNIT_MAX_PARALLEL_TESTS` was unset.

| Piece | Slot | Wait | Hold | Result |
|---|---|---:|---:|---|
| `dotnet build tests/Antiphon.Tests` | `abf9e06c-0969-4472-b9de-502fe464ffa5` | 0s | 189s | 1085 warnings, 0 errors. Start `2026-10-19T04:19:03Z`, end `2026-10-19T04:22:13Z` |
| Frozen roster | `7895faa2-72fd-424d-b591-c1d317346564` | 0s | 2124s | exit 2. Outer `2026-10-19T04:22:32Z` to `2026-10-19T04:57:58Z` |

Assembly informational version `1.0.0+42cbbd0417a2611f3a690e6671b13c9ba51fbf34`. Assembly SHA-256 `844bb42699cf7e0d3d45d14fe55583a314577332d82042619213a35ec24493fb`.

TRX `0275471162ed94a65aaa31cba158453ea91c1997dc090b329f66cf9af074a880`. Times start `2026-10-19T04:22:37.3265263Z`, finish `2026-10-19T04:57:57.7896748Z` (35m 20.463s). Path: `/tmp/rc-linux-541c1ad7/roster/run.trx`. Class identities equal all 373 roster names, missing 0, extra 0. Outcomes are 4527 Passed and 1 Failed.

| Suite | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| antiphon, frozen Linux Unit roster (373 classes) | 4528 | 4527 | 1 | 0 |
| session-runner | not run | | | |
| pty-host | not run | | | |
| agents-pty | not run | | | |
| messaging | not run | | | |
| client | not run | | | |
| scripts | not run | | | |
| e2e | not run | | | |
| markdown-pdf | not run | | | |

The eight suites other than this roster were not started. This re-gate repeats the first gate's Linux roster only. The first gate at `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` was also 4528 executed, 4527 passed, 1 failed, 0 skipped. That failure was `namespace_census_matches_compiled_checkpoint_cases`. In this roster the same case passed (3.819s). `2273f108b2dd7d3b0d51432c87166563cb792551` (CARD-1342) is an ancestor of the candidate.

## The red

`Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests.disk_ledger_cannot_authorize_user_deletion(outside)` (9.8ms). `DirectoryNotFoundException: Could not find a part of the path '/proc/19323/stat'`. The throw is `File.ReadAllText` in `CheckpointNativeIo.StartToken` (`src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs` line 37), called from `CheckpointTestScope.Register` (`tests/Antiphon.Tests/Checkpoints/CheckpointTestScope.cs` line 98) during `InitRepo` (`tests/Antiphon.Tests/Checkpoints/CheckpointOwnedOutputTests.cs` line 66) before the ledger assertion at line 23. The other three arguments of the same method passed in this roster.

Classification: **new**. The same method at `61117dd1663d2f47ce4708c18b609b8936491986` passed in a scratch worktree, including the `outside` argument. `tests/Antiphon.Tests/Checkpoints/CheckpointOwnedOutputTests.cs` is identical between that tip and the candidate. `CheckpointNativeIo.cs` and `CheckpointTestScope.cs` differ. The stored first-gate TRX also records `outside` as Passed (129ms). No fix was applied.

| Baseline piece | Detail |
|---|---|
| Worktree | `/tmp/rc-baseline-61117dd` (detached). Filter `/*/*/CheckpointOwnedOutputTests/disk_ledger_cannot_authorize_user_deletion(outside)*` selected the whole method (4 arguments) |
| Build | slot `68a3cb69-9ea4-4ca2-9725-16495727208c`, waited 0s, held 140s, 1069 warnings, 0 errors. `2026-10-19T04:59:29Z` to `2026-10-19T05:01:50Z`. Assembly `1.0.0+61117dd1663d2f47ce4708c18b609b8936491986`, SHA-256 `c7dc359413f3ad97e239b7a7527ad01584c4ce7474d050b93eb4418347272a94` |
| Test | slot `0ddba0be-a3c7-4911-9414-83dd65616d31`, waited 0s, held 5s. `2026-10-19T05:01:52Z` to `2026-10-19T05:01:57Z`. Exit 0. 4 executed, 4 passed, 0 failed, 0 skipped. `outside` passed in 47ms |
| TRX | SHA-256 `e7856acae7a1eade6e10e78a166bfefa9cdaa44e5f9f4e36ac4b6071dc0dc8c5`. Path: `/tmp/rc-linux-541c1ad7/baseline/run.trx`. Times `2026-10-19T05:01:56.3820816Z` to `2026-10-19T05:01:57.4389712Z` |

## Not in this roster

The frozen filter does not contain `RemoteScriptContractTests`, `ReleaseGateRunTests`, `RollingVolumeRecycleScriptTests`, or `C1008PlatformContractTests`. The named inherited Linux reds from the first-gate record were therefore not executed and were not reclassified here. E2E was not run.

## Limits

No tags, no publish, no `release-cut.ps1`, no product or test edit. Generated TRX stays outside Git. Removed 56 owned `bin-rc1019-541c1ad7` and `bin-rc1015-541c1ad7` output directories; none remain. This pass does not discharge Full rc coverage, ChangeAware admission, or the Windows lane.

--- next stage ---
next: decide
handoff: The Linux CARD-1287 roster at 42cbbd0417a2611f3a690e6671b13c9ba51fbf34 is 4528 executed, 4527 passed, 1 failed, 0 skipped. The checkpoint namespace census that failed the first gate now passes. The remaining failure is disk_ledger_cannot_authorize_user_deletion(outside), a /proc stat miss during git-init registration. That case passed at rc/2026-10-15 61117dd1663d2f47ce4708c18b609b8936491986, so it is new. The other eight required suites were not started.
artifact: docs/investigations/2026-10-19-rc-linux-regate.md
