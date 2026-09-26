# CARD-0730 reply-class failures on server2

Confirmed. Five `AgentTaskReplyIntegrationTests` results are red on this Linux runner at `origin/master` `e4b4159d89208fa074e1da6bbdc9fc08cf72b756`. They were red when the tests were added. No later master commit changes the lines that make them fail. Two mechanisms, both Linux-specific. This session did not execute them on Windows.

## Reproduction

Build: `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c730/ -nodeReuse:false --disable-build-servers -maxcpucount:4 -p:UseAppHost=false` through `scripts/build-slot.ps1` (lease granted, exit 0, 400 warnings, 0 errors).

Run, `--no-build`, same output path, filter:

`/*/*/AgentTaskReplyIntegrationTests/(C547_CommitFailed_after_a_verified_empty_search_resolves_and_a_later_settle_proceeds*)|(C547_CommitFailed_with_a_failed_history_search_leaves_the_obligation_pending*)|(C527_hook_failure_is_CommitFailed_and_the_child_brief_carries_stderr*)|(a_shared_report_naming_an_uncommitted_path_is_committed_when_the_policy_is_on*)`

TRX `.antiphon/c730-reply/run.trx` counters: total 5, executed 5, passed 0, failed 5, skipped 0. Wall about 59s. Same assertion lines as the 2026-09-25 class TRX at `/work/worktrees/task-c0543045/.antiphon/c714-repair2-checkpoints/X-reply-class-20260925-213544-bd4f/run.trx`.

| Result | Assertion |
|---|---|
| `C547_CommitFailed_after_a_verified_empty_search_resolves_and_a_later_settle_proceeds(fixed)` | `AgentTaskReplyC547RecoveryTests.cs:58` `CommitRecoveryNotNeeded` count 0 |
| same method `(still-failing)` | same line, same count |
| `C547_CommitFailed_with_a_failed_history_search_leaves_the_obligation_pending` | `AgentTaskReplyC527EmptyRecoveryTests.cs:138` via `:131`, status `Succeeded` where the test requires `Dispatched` |
| `C527_hook_failure_is_CommitFailed_and_the_child_brief_carries_stderr` | `AgentTaskReplyC527Tests.cs:446` `SingleAsync` on the commit child, no row |
| `a_shared_report_naming_an_uncommitted_path_is_committed_when_the_policy_is_on` | `AgentTaskReplyIntegrationTests.cs:3544` header was `git=uncommitted:1 → commit ta…`, required `git=committed:` |

Host: git 2.47.3, umask 0022, `GIT_CONFIG_GLOBAL=/run/antiphon/gitconfig` (user name and email only). No `core.hooksPath`.

## Cause 1 — the failing hook is not executable

`ScratchGitRepo.InstallFailingPreCommitHookAsync` (`tests/Antiphon.Tests/TestHelpers/ScratchGitRepo.cs:80-88`) writes `.git/hooks/pre-commit` with `File.WriteAllTextAsync` and never sets the executable bit. `git log -L 80,88` shows that body in one commit, `197289d0` (CARD-0527 S2). `git diff 197289d0 HEAD -- tests/Antiphon.Tests/TestHelpers/ScratchGitRepo.cs` is empty.

Measured outside the suite, same git:

- mode 644: `git commit` exits 0. stderr: `hint: The '.git/hooks/pre-commit' hook was ignored because it's not set as executable.`
- mode 755, same script: `git commit` exits 1. stderr: `gate says no`.

`GitWorkspaceService.CommitOnlyAsync` (`server/Application/Services/GitWorkspaceService.cs:842-869`) runs `git commit` with no `--no-verify`, so an executable hook would run. The four hook tests install that script and then observe a successful commit:

- `C527_hook_failure_is_CommitFailed_and_the_child_brief_carries_stderr` (`AgentTaskReplyC527Tests.cs:435-454`) expects a commit child whose goal contains `gate says no`, a warning containing `CommitFailed`, and `x.md` left staged. The commit succeeds, so there is no child.
- Both arms of `C547_CommitFailed_after_a_verified_empty_search_resolves_and_a_later_settle_proceeds` (`AgentTaskReplyC547RecoveryTests.cs:33-68`) get past the `Dispatched` check and the single `CommitRecoveryStarted` row, then fail because `CommitFailed` is not resolved with `CommitRecoveryNotNeeded`. A commit that actually landed, followed by `ThrowOnceSaveInterceptor`, leaves the obligation open. That is the successful-commit recovery path, not the hook-failure path (`AgentTaskReplyService.cs:3826-3839` writes `NotNeeded` only after `GatedCommitOutcome.CommitFailed` and an empty history search).
- `C547_CommitFailed_with_a_failed_history_search_leaves_the_obligation_pending` (`:105-135`) keeps the hook in place for the second settle. The first settle still records one unresolved obligation (the search override fails, and a real commit exists). The second settle finds that commit and the task becomes `Succeeded`. The test requires it to stay `Dispatched`.

The same helper is also called from `GatedCommitServiceTests.cs:481` and `AgentTaskCommitEndpointTests.cs:186`. Those classes were not in this run. CARD-0713 is a separate set of card-file tests that write hook files themselves and also omit the executable bit.

## Cause 2 — a POSIX absolute path is not a reported path

`a_shared_report_naming_an_uncommitted_path_is_committed_when_the_policy_is_on` (`AgentTaskReplyIntegrationTests.cs:3523-3548`) writes `docs/superpowers/uncommitted-plan.md` and reports `Wrote {claimedPath}.`, where `claimedPath` is `Path.Combine` and therefore absolute (`/tmp/antiphon-reply-shared-committed-repo…/docs/superpowers/uncommitted-plan.md.`). It does not call `SeedFileEditAsync`.

`SeedTurnAsync` (`AgentTaskReplyIntegrationTests.cs:4729-4761`) stores a user prompt, assistant text, and a turn end. It does not store a tool call. `AgentFilesService.GetEditedPathsAsync` (`AgentFilesService.cs:570-609`) returns only `Write` / `Edit` / `NotebookEdit` tool calls after `DispatchedAt`. Edited paths are empty.

The footprint is the dirty paths that are edited or named in the report (`AgentTaskReplyService.cs:3762-3777`). `ExtractReportedRepositoryPaths` (`:4163-4191`) tries three patterns (`:4147-4156`), unchanged since `15a7269e` (CARD-0261, 2026-08-31). `git log -L 4150,4156` shows that one commit and no later edit.

Against the report string this test builds, on this host:

- backtick pattern: no match
- `ReportedWindowsPathPattern` (`[A-Za-z]:[/\]…`): no match
- `ReportedRelativePathPattern`: no match, because the sentence period after `.md` fails the trailing lookahead `(?![A-Za-z0-9_.-])`

Dropping that period still does not yield the repo path. The relative pattern then matches `tmp/antiphon-reply-shared-committed-repoABC/docs/superpowers/uncommitted-plan.md`, which is not the dirty path `docs/superpowers/uncommitted-plan.md`. A Windows report `C:\Users\…\uncommitted-plan.md.` matches `ReportedWindowsPathPattern` including the trailing period; `TrimEnd` at `:4181-4183` strips it, and `TryMakeRepoRelative` (`:4196-4221`) keeps the file.

Empty footprint takes the branch at `:3772-3776` and returns `uncommitted:{n} → commit task …` from `SpawnCommitChildAsync` (`:3964-3966`) with reason `unattributable` (`AgentTaskService.cs:3101`). That return is before `gated.CommitAsync`. The header in the fresh failure matches that string. This is not a hook failure and not `CommitOnSettle` being off.

The policy-off sibling `a_shared_report_naming_an_uncommitted_path_warns_the_caller` warns from full git status (`:3729-3749`), not from the extractor. It passed in the 2026-09-25 class TRX. It was not re-run in this session.

`DelegationUnitTests.reported_repository_paths_normalize_relative_and_absolute_windows_forms` (`DelegationUnitTests.cs:580-585`) skips off Windows and says the absolute pattern is drive-letter only.

## When they went red

Source oracle, each step under a 40s `git` timeout. Runtime execution is HEAD only. The failing lines have no later edit, so an intermediate SHA cannot be the first red commit.

| SHA | Date | What is true |
|---|---|---|
| `15a7269ef12d4a2a54986e43fcab137d12ac6c6e` | 2026-08-31 | Drive-letter and relative patterns added. No POSIX absolute pattern. |
| `197289d0dd319a7e654ee453c6a4fd9393896794` | 2026-09-17 | Hook helper added, no executable bit. Parent of the next row. `git grep` finds neither the hook test nor the shared policy-on test. |
| `d5765e848a91ae34a835e119139533a1bb56b25b` | 2026-09-17 | Adds `C527_hook_failure_is_CommitFailed_and_the_child_brief_carries_stderr` and `a_shared_report_naming_an_uncommitted_path_is_committed_when_the_policy_is_on`. The shared test already uses `Path.Combine` and `Wrote {claimedPath}.` First red commit for those two results. |
| `12a000725e4f3cb27b8f3b5939b1a23355ca5182` | 2026-09-17 | Parent of the C547 tests. `git grep` finds no `C547_CommitFailed_after_a_verified_empty_search`. |
| `c5d88828f3b1505c71ca48b7e5b3679adcab13fc` | 2026-09-17 | Adds both `C547_CommitFailed_*` methods, both calling `InstallFailingPreCommitHookAsync`. First red commit for those three results (two arguments plus the failed-search test). |
| `e4b4159d89208fa074e1da6bbdc9fc08cf72b756` | 2026-09-25 | Current master. 5 failed, 0 passed. |

## Fix proposal

Not done, noted: no production or test edit in this stage. Two causes, so this is not a one-line change to land from Investigate.

1. Hook tests. After the write at `ScratchGitRepo.cs:87`, on non-Windows set the mode to user/group/other read and execute plus user write (`UnixFileMode` 0755). Mode 0755 is the measurement that makes git 2.47.3 run the hook and surface `gate says no`. That makes the four hook results exercise `GatedCommitOutcome.CommitFailed`. It also covers the two other callers of this helper.

2. Shared policy-on test. Add a POSIX absolute pattern beside `ReportedWindowsPathPattern` (`AgentTaskReplyService.cs:4150-4152`) and include it in the list at `:4169-4174`, before the relative pattern. A pattern that matches a leading `/`, a slash-separated body, and an extension, with a lookbehind that rejects a preceding letter, digit, `_`, `.`, `:`, or `/`, keeps `https://host/a.md` out and lets `TrimEnd` at `:4183` strip a sentence period. Point it at a temp repo in a unit test next to `DelegationUnitTests.cs:580` that is not Windows-skipped. Rewriting only the integration report to a backticked relative path would make this one test green and would leave Linux absolute paths on the `unattributable` branch.

## Remaining uncertainty

Windows was not executed here. The hook hint and the drive-letter regex are the reasons these five are expected to pass on the desktop; that is not a fresh Windows TRX. The two other helper callers and the CARD-0713 hook files were not re-run.
