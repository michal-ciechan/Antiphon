# Windows native confirmations for the November 27 repairs

Nine checkpoints validated with 101 passed cases. The junction checkpoint recorded one skip with clean, verified build provenance and exited 3 because the test did not reach its assertion.

The desktop ran the selected checkpoints on Windows 10 Pro build 19045, x64, with eight logical CPUs, .NET SDK 10.0.300 and Git for Windows 2.50.1.windows.1. The test shell had a non-elevated token and tested from clean, detached worktrees. Each credited row used one isolated build, one TUnit filter and a desktop build-slot lease with a four-node MSBuild limit. The CPU percentage comes from `Win32_Processor.LoadPercentage` and available memory comes from the slot broker. The arrows show the samples immediately before and after each driver invocation. The receipt and sibling TRX retain the execution timestamps and exact outcomes.

| Source | SHA |
|---|---|
| `feat/card-task-14a6ef67` | `6bc92ec048536ccc49202dd925c521ff603938d8` |
| `feat/card-task-c039373b` | `ada752025d26538d373ddf6dca4fcea20d5950d3` |
| `origin/master` at initial fetch | `b5b349d968b5c5ae28a66c73201f1ab06a540aa5` |

`C:\src\Antiphon` remained pinned to `5c0883bf203d122fdc7d4556e79087af2a683669`. The Grok fixture used its own runner client, staged FakeGrok and isolated test PostgreSQL schema. The task's desktop authority applied after Windows qualification `419ed669` had settled. On Windows, the two CARD-1614 parameter rows configure a modern ConPTY host and request Linux custody. The `false` arm exercises the OS admission path and the `true` arm forces the Linux refusal. The test is in `Antiphon.PtyHost.Tests`. The CARD-1277 fixture is in `Antiphon.Tests`.

## Checkpoints

**P/F/S** gives pass, fail and skip counts. Receipt paths are relative to `C:\Antiphon\evidence\debug-da40d5f9`.

| Row | Project | Exact TUnit filter | Source | P/F/S | CPU % | Available MiB | Receipt |
|---|---|---|---|---:|---:|---:|---|
| CP-2 | `Antiphon.Tests` | `/*/Antiphon.Tests.Application/SessionMessageQueueGrokPtyIntegrationTests/Captured_spinner_reaches_ready_and_complete_user_prompt` | `6bc92ec04` | 1/0/0 | 55 → 76 | 9788 → 9606 | `CP-2\CP-2-20261127-152702-62dd\report.json` |
| CP-3 | `Antiphon.Tests` | `/*/Antiphon.Tests.Application/SessionMessageQueueGrokPtyIntegrationTests/Captured_spinner_reaches_ready_and_complete_user_prompt` | `6bc92ec04` | 1/0/0 | 87 → 76 | 9615 → 10244 | `CP-3\CP-3-20261127-154028-7ab7\report.json` |
| CP-11 | `Antiphon.Tests` | `/*/Antiphon.Tests.Application/SessionMessageQueueGrokPtyIntegrationTests/Captured_spinner_reaches_ready_and_complete_user_prompt` | `6bc92ec04` | 1/0/0 | 100 → 99 | 9253 → 9655 | `CP-11\CP-11-20261127-155231-b7bc\report.json` |
| CP-4 | `Antiphon.SessionRunner.Tests` | `/*/Antiphon.SessionRunner.Tests/HerdrLabelFollowSchedulingTests/*` | `ada752025` | 28/0/0 | 56 → 82 | 10429 → 9094 | `CP-4\CP-4-20261127-155321-71ad\report.json` |
| CP-5 | `Antiphon.SessionRunner.Tests` | `/*/Antiphon.SessionRunner.Tests/HerdrLabelFollowSchedulingTests/Saved_snapshot_poll_expires_under_contention_and_rejects_late_success` | `ada752025` | 1/0/0 | 74 → 62 | 8836 → 9271 | `CP-5\CP-5-20261127-155703-1422\report.json` |
| CP-6 | `Antiphon.SessionRunner.Tests` | `/*/Antiphon.SessionRunner.Tests/ProcessSpawnLimitTests/*` | `ada752025` | 2/0/0 | 57 → 82 | 9347 → 10183 | `CP-6\CP-6-20261127-160107-7cc8\report.json` |
| CP-7 | `Antiphon.PtyHost.Tests` | `/*/Antiphon.PtyHost.Tests/HostCustodyPlatformTests/C1614_Foreign_platform_binding_is_refused_before_native_start` | `ada752025` | 2/0/0 | 91 → 70 | 9947 → 9625 | `CP-7\CP-7-20261127-160530-01b2\report.json` |
| CP-8 | `Antiphon.Tests` | `/*/Antiphon.Tests.Agents/Card1277PtyHostFixtureTests/*` | `ada752025` | 3/0/0 | 55 → 94 | 9662 → 9863 | `CP-8\CP-8-20261127-160742-0645\report.json` |
| CP-9 | `Antiphon.GitInProcess.Tests` | `/*/Antiphon.Tests.Infrastructure/LibGit2VsGitWorkingTreeVerdictTests/Status_and_ls_files_verdicts_match_git` | `b5b349d96` | 62/0/0 | 86 → 65 | 9799 → 4507 | `CP-9\CP-9-20261127-153033-463e\report.json` |
| CP-10 | `Antiphon.Tests` | `/*/Antiphon.Tests.Infrastructure/WorktreeGuardedCleanupTests/C665_EmptyUnlistedJunctionTargetSurvivesRemoval` | `b5b349d96` | 0/0/1 | 70 → 55 | 9739 → 10062 | `CP-10\CP-10-20261127-162034-0b57\report.json` |

`checkpoints validate` accepted CP-2 through CP-9 and CP-11 with their expected SHAs. CP-10 records `dirty=0`, `sourceState=clean` and `buildSource=verified`. Its zero executed cases made the validator return exit 2 with `report_failed`.

## Grok spinner

All three fresh test processes at `6bc92ec048536ccc49202dd925c521ff603938d8` passed the pre-ready queue hold, actual modern ConPTY backend assertion and exactly one complete persisted UserPrompt. The test durations were 12.320, 12.284 and 14.364 seconds. The `NoSubmitOutput` symptom did not recur, so the earlier submit failure remains unexplained. The earlier qualification TRX records the later standing-launch authorization conflict.

## Prior rc outcomes and fixture prerequisites

| Windows Full run | Pinned SHA | `unreadable-directory` | `C665_EmptyUnlistedJunctionTargetSurvivesRemoval` |
|---|---|---|---|
| `1fe763a9` | `0cfc4ac1ffb41f5af159a0c1db423fe119950267` | Failed with eight comparison mismatches after Git exited zero with the expected permission warning | `NotExecuted` because Git listed the empty directory link |
| `419ed669` | `5c0883bf203d122fdc7d4556e79087af2a683669` | Passed | `NotExecuted` because Git listed the empty directory link |

The exact witnesses are in `git-in-process-all.trx` and `antiphon-all-group-118.trx` under the desktop candidate logs at `C:\Antiphon\releases\candidates\rc-2026-12-06-0cfc4ac1ffb4\logs\2026-11-26-0742-1fe763a9` and `C:\Antiphon\releases\candidates\rc-2026-12-07-5c0883bf203d\logs\2026-11-27-0023-419ed669`.

The ACL witness applies `icacls /deny` to the test-owned `locked` directory. It needs a caller token that loses directory enumeration after the deny entry. The fixture explicitly skips when .NET can still list it, which has been observed with an elevated administrator token. The CP-9 TRX has an executed pass for the `unreadable-directory` argument (9.380 seconds) within the exact method selection. That case verifies Git exits zero, reports the permission warning and lists only the visible path before the in-process comparison. [ACL witness](../../tests/Antiphon.GitInProcess.Tests/LibGit2VsGitWorkingTreeVerdictTests.cs) · [Earlier failure analysis](2026-11-26-why-missed-rc-run-1fe763a9-git-in-process.md)

In both historical junction TRX rows the skip trace reads `Skipped: This host's Git lists the empty directory link; the unlisted-junction boundary requires Windows review.` This branch is reached after successful junction creation. The native junction helper requires ordinary write and reparse access to the test directory. The witness can exercise removal only when `git ls-files --others --ignored --exclude-standard -z` omits `bin-link` from the fixture's inventory. There is no test feature flag. [Junction witness](../../tests/Antiphon.Tests/Infrastructure/WorktreeGuardedCleanupTests.cs) · [Junction fixture](../../tests/Antiphon.Tests/TestHelpers/DirectoryLink.cs)

CP-10 reached that same skip on master at `b5b349d968b5c5ae28a66c73201f1ab06a540aa5`. Its TRX records `NotExecuted` and the same skip trace. A native pass for this cleanup path remains unverified. The Windows fixture needs to present an unlisted native junction and then exercise the late write and target-byte preservation assertions. A Code follow-up should make that precondition reproducible with the supported Windows Git and return an executed, source-validated pass.

## Reproduce a row

Run from a clean worktree pinned to that row's SHA. Choose a fresh `bin-…/` and results directory on each invocation.

```powershell
scripts/antiphon checkpoints row --name <CP> --project tests/<project> --output-path bin-<fresh>/ --filter '<filter>' --min-executed <count> --expect <class-or-method> --expected-source-sha <sha> --results-root <evidence-root>
scripts/antiphon checkpoints validate --evidence <report.json> --expected-source-sha <sha> --rows <CP>
```

The method filter for CP-9 selects all 62 arguments to `Status_and_ls_files_verdicts_match_git`. The `unreadable-directory` argument is verified by display name in the TRX. [TUnit tree filters](https://tunit.dev/docs/execution/test-filters/)

The initial CP-1 launch addressed the wrong URL on the build-slot broker and entered the tool's unleased fallback. Its process tree was stopped. The first leased retry refused the partial output with `pre-existing-output`. Those two attempts executed zero tests. CP-2, CP-3 and CP-11 used fresh outputs and all three obtained leases.

The discarded CP-1 launch sampled 62% CPU and 9,568 MiB available before the broker error. The leased retry at the same Grok SHA sampled 61% → 63% and 9,543 → 9,771 MiB. Its refused receipt is `CP-1\CP-1-20261127-152652-39be\report.json` under the evidence root.
