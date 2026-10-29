# Task 2dec24ba — bounded redacted runner failures

Tip `c5c9aecb057c2be049b34e4172898f93cf8f0794` on `feat/card-task-2dec24ba`, fast-forward of `a8505f83946f7d04ec2627dfb3b7900c5fc9b98f`. Linux checks below passed. The tip is pushed.

## Fix

D-1. An unmapped launch exception becomes problem type `runner_unhandled_exception` (`UnhandledExceptionProblemType` in `SessionReadLaunchRoutes`). The title is the exception type name. The detail is `CommandLineDisclosure.ForProblemDetail`: message only, redacted, then clipped to 512 characters. The stack stays in the runner log through `RedactedFailureLog`, which does not pass the exception object to the logger. `OperationCanceledException` still propagates, and a started response is rethrown. `AgentSessionService` clips the three persistence sites with `ColumnText.Clip`: card `ErrorDetails` at 4000, card `FailureReason` at 2000, interactive `FailureReason` at 2000.

D-2. `CommandLineDisclosure.Redact` replaces the argument after `--append-system-prompt`, `--system-prompt`, and `--rules`, including `--flag=value`, and any argument longer than 200 characters, with `<redacted N chars>`. `--system-prompt-file` is left alone. `ModernConPtyConnection` puts that redacted line in the exception and in the debug trace. Redaction happens before the 512 clip.

D-3. `PtyHostServer` again catches only `InvalidOperationException` and `ArgumentException` and writes `commandFailed` with `ex.Message`.

A-1 and A-2. The catch comment describes the typed 500, and the type is the named constant.

## Commits

- `7ce5f0f23c0b168fce2ad8e0cab1bbe84a41a0b5` test witness
- `e1a383e6ef40d75bd53e53dcc358cde0a186c850` bound and redact
- `b31b79440927b0035e00507c629dfe4fed4b3eb7` CS0136 rename
- `c5c9aecb057c2be049b34e4172898f93cf8f0794` StringBuilder import

## Tests

`dotnet exec` of the slot build (`UseAppHost=false`, `OutputPath=bin-c2dec24ba/`, lease `1619b647-78d3-498c-b53f-2d76db3ef8fd`), source `c5c9aecb`.

- Session-runner `(GrokRules*)|(HerdrPlacementCheckRoute*)|(RunnerAbsenceEvidenceContract*)|(CommandLineDisclosureTests*)`: confirming run 99 total, 97 passed, 2 skipped. Skips are the Windows PtyHost acceptance test and the Linux CARD-0871 corrupt-receipt case.
- The first pass of that filter was 96 passed, 1 failed, 2 skipped. `GrokRulesAdoptionTests.Restart_recovers_only_a_verified_rules_receipt_and_retains_files_after_worktree_removal(False, False)` found the adopted session already exited, so `ExpireRulesArtifactAsync` returned instead of throwing. That method alone then passed 3 and skipped 1. The full filter rerun is the 97/0/2 line. Source was unchanged between the runs.
- `HostSessionPipeTests`: 9 total, 2 passed, 7 skipped (ConPTY only on Windows).
- `Runner_500_longer_than_both_text_columns_still_records_a_failed_session` and `Runner_500_argv_sentinel_is_absent_from_the_body_the_failure_reason_and_the_server_log`: 2 passed.
- `ColumnTextTests`: 6 passed.
- `DelegationDispatchRecoveryBoundaryTests` `C1153_*`: 36 passed.
- `RcRosterRepairTests`: 3 passed.
- `C1210_Roster_closure_matches_source_gates`: 1 passed.
- Census `C1584`, `C1577`, `C1404`, `C1410`, `G2865`: 5 passed.

## Evidence and merge

`check-evidence-diff` `a8505f839..HEAD`: commits=4 entries=0 violations=0.

`git merge-tree --write-tree origin/rc/2026-10-27` (`91b45006b4d76f144c2651180039aece5a9d7938`) is clean, tree `73b58bff29a43ebbbaf7fe429048245fd5211d81`.

`origin/master` (`f8bddb6efda7b0e8f50f3b45744087e52188bc66`) conflicts on the same six paths already conflicted at `a8505f839`:

- `.antiphon/task-af2b78c8.md`
- `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`
- `scripts/lib/test-phase-evidence.ps1`
- `scripts/test-scripts-lane-repairs.ps1`
- `tests/Antiphon.SessionRunner.Tests/GrokRulesHttpAcceptanceTests.cs`
- `tests/Antiphon.Tests/Scripts/ScriptsLaneRepairTests.cs`

The GrokRules overlap is master's `launch-response.json` assertion beside this branch's `launch-response-body:` assertion. `AgentSessionService` auto-merges and keeps the three `ColumnText.Clip` sites. Conflicts left for landing.

Isolated `bin-c2dec24ba` directories are removed. Working tree was clean before this report file.
