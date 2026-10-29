# Native Windows verification A: red witnesses

Task 7783f346. Frozen integration source: 3f22407a02328307949ad1553d72b6e802cb563f.
Base: cf1a124035f45857e3e2bf205ab9f8b05cf7a8e4.
Evidence: C:\Antiphon\releases\task-7783f346 (raw TRX, run commands, timings and source records).

All listed drivers used scripts/build-slot.ps1 and an isolated bin-7783f346/ output.
Initial Antiphon.Tests build: 0 errors, 1374 warnings, 736.63 seconds.

## Failures observed before local repairs

- g105-rest: 50 passed, 11 failed. Receipt consumers refused healthy files with read-stalled: 0 bytes within 3 seconds. CheckpointOrFilterTests 23/8, CheckpointLaunchCleanupTests 10/1, CheckpointAppTests 2/1, CheckpointAdmissionContractTests 2/1, CheckpointSlotExecutorTests 13/0 (pass/fail). The Linux-only saturation witness currently skips native Windows. Shared and embedded reader code is unchanged from the base.
- g123-baseline: ChangeAwareReleaseTests 0 passed, 3 failed, wall 913.3 seconds. Each hit the unchanged five-minute execution budget with root=False stdout=False stderr=False. BaselineAuthority completed 8/14 labels, ModeCredit 6/7, ParameterHops 4/10. No holder= diagnostic was emitted: these were execution timeouts, not the post-exit drain diagnostic path. The fixture copies 54 projects although the real IsTestProject census has 15 projects.
- g113-g114: LandingGitTests 57 passed, 1 failed, 4 skipped. C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal returned git_timeout in 4.1607694 seconds but its journal remained after fixture cleanup. The hook records an MSYS shell PID, while KillHolder interprets it as a native Windows PID. The product budget itself returned within its assertion.
- g114-g126-g129: CheckpointGitEncodingScriptTests 1 passed, 1 failed. C1608_invalid_utf8_git_output_is_refused saw exit=128, empty stdout and no git-encoding reason. The Windows fixture writes git.cmd, but the no-shell native process launch resolves real git.exe. It therefore never emitted the intended invalid UTF-8 byte.

These are historical red outcomes; later passing receipts do not replace them. Attribution is observed-on-integration unless an explicit base rerun is recorded. No timeout or acceptance assertion is relaxed by the proposed repairs.
