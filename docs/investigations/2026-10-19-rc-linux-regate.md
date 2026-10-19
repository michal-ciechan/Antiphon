# rc/2026-10-18 Linux re-gate

Status: **roster green**. This is not an rc-profile complete-green receipt and not a publication.

Candidate: `1f72e20158b99434cb6398fb7efbbd133d1c5569` (`rc/2026-10-18`, candidate name `rc-2026-10-18-1f72e20158b9`).
First Linux gate: `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` in `docs/investigations/2026-10-18-rc-linux-lane.md` (4528 executed, 4527 passed, 1 failed).
Previous rc tip named for an inherited check: `61117dd1663d2f47ce4708c18b609b8936491986` (`rc/2026-10-15`). No red was produced, so that tip was not re-run.
Host: Debian 12 container `a6457bc89508`, pwsh 7.5.4, .NET SDK 10.0.402, test host .NET 9.0.21, TUnit 1.44.0 / Microsoft Testing Platform 2.2.2, node v22.23.3, npm 10.9.9. Build-slot broker `http://127.0.0.1:8080/build-slots`. Production session-runner sessions were not used; the suite cleared the runner base URL to `http://127.0.0.1:1`.

The assigned branch `feat/card-task-ed312042` is `c8bc95276ad1dfa22fcfcd0d1a2b3b2c6a1d80b0` and is not an ancestor of the candidate, so the branch was not moved. The run used a detached worktree of this task at the exact candidate, `/tmp/rc-regate-ed312042`. Checkout HEAD during the build and the roster was `1f72e20158b99434cb6398fb7efbbd133d1c5569`. `git status --porcelain` was empty before the build. No product or test file was edited.

## What ran

The same method as `docs/investigations/2026-10-18-rc-linux-lane.md`, which follows `docs/investigations/2026-10-15-card-1287-linux-unit.md` lines 13-17. One isolated build, then one no-build execution of the frozen filter. Filter SHA-256 `9ba95d1db683438f6c382422c9ca4a596d2280930f100da9973b657596020ad7`. `UseAppHost=false`. Output path `bin-rc1019-ed312042/`. `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`. Unset before the run: `ANTIPHON_HEADED_TESTS`, `ANTIPHON_HEADED_LONG_TESTS`, `ANTIPHON_DISTILLER_APPLY_CANARY`, `ANTIPHON_TG_TEST_TOKEN`, `ANTIPHON_CARD0133_RUN_P3`, `ANTIPHON_CARD0133_CODEX_EXE`, `ANTIPHON_BROKER_TESTS`. `TUNIT_MAX_PARALLEL_TESTS` was unset.

| Piece | Slot | Wait | Hold | Result |
|---|---|---:|---:|---|
| `dotnet build tests/Antiphon.Tests` | `10f518a9-fc37-4419-ae82-e655305c1ee2` | 0s | 198s | 1085 warnings, 0 errors. Start `2026-10-18T23:25:11Z`, end `2026-10-18T23:28:30Z`. MSBuild `00:03:16.88` |
| Frozen roster | `a033def9-5056-46c1-ada8-ea8aec845d0f` | 0s | 2143s | exit 0. Outer `2026-10-18T23:28:32Z` to `2026-10-19T00:04:17Z` |

Assembly informational version `1.0.0+1f72e20158b99434cb6398fb7efbbd133d1c5569`. Assembly SHA-256 `0eb7a034a2079e7e5a57f1cb1eaed43c49a125c7ee4ed850cac9d0b518eb49a5`.

TRX `68c1d8e5-232b-4a1b-9939-5dccc8e364e9`. Times start `2026-10-18T23:28:37.0191716Z`, finish `2026-10-19T00:04:16.0231079Z` (35m 39.613s in the TUnit summary). SHA-256 `ef92b400c891ca39c88f1169899eef6b440dbf92bf1d94c707e52340e87f9e69`. Path: `/tmp/rc-linux-ed312042/roster/run.trx`. Counters: total 4528, executed 4528, passed 4528, failed 0, error 0, timeout 0, aborted 0, inconclusive 0, notExecuted 0. Class identities equal all 373 roster names, missing 0, extra 0. Every roster method has a Passed result.

| Suite | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| antiphon, frozen Linux Unit roster (373 classes) | 4528 | 4528 | 0 | 0 |
| session-runner | not run | | | |
| pty-host | not run | | | |
| agents-pty | not run | | | |
| messaging | not run | | | |
| client | not run | | | |
| scripts | not run | | | |
| e2e | not run | | | |
| markdown-pdf | not run | | | |

The eight suites other than this roster were not started. The profile entry refuses on this host before those suites, and the published Linux gate for the previous candidate was this 373-class roster only.

## The previous red

`Antiphon.Tests.Checkpoints.CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases` passed in this roster (4.000s). At `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` the same method failed because `Get-NamespaceCensus` `selected` was 594 against 600 compiled cases. No failure remained to classify as inherited or new. `61117dd1663d2f47ce4708c18b609b8936491986` was not checked out.

## Not in this roster

The frozen filter does not contain `RemoteScriptContractTests`, `ReleaseGateRunTests`, `RollingVolumeRecycleScriptTests`, or `C1008PlatformContractTests`. Those named cases were not executed. E2E was not run.

## Limits

No tags, no publish, no `release-cut.ps1`, no product or test edit, no red fixed. Generated TRX stays outside Git. The alternate `bin-rc1019-ed312042/` outputs were removed with the detached worktree after the DLL hash was recorded. This pass does not discharge Full rc coverage, ChangeAware admission, or the Windows lane.

--- next stage ---
next: decide
handoff: The Linux CARD-1287 roster at 1f72e20158b99434cb6398fb7efbbd133d1c5569 is 4528 passed, 0 failed, 0 skipped. The first gate's census failure is passed in this binary. The rc profile entry cannot run on this Linux host. The other eight required suites were not started. No red was available to classify against rc/2026-10-15.
artifact: docs/investigations/2026-10-19-rc-linux-regate.md
