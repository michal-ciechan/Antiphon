# CARD-0408 Code evidence

Task `0998ad05`, isolated checkout `C:\Antiphon\worktrees\card-task-0998ad05`,
branch `feat/card-task-0998ad05`, reviewed plan base `3efe2c69`.
Implementation and verification are in progress; this is not a completion claim.

The review task made no commit. Origin has no `feat/card-task-f67c413b` ref;
its local branch points to `fc9cdf5c`, whereas the review's worktree and full
report identify `3efe2c69`. Code uses that reviewed plan and the brief's decisions.

## S1 baseline evidence

- `dotnet build server/Antiphon.Server.csproj --property:OutputPath=bin-c408/ --nologo -v quiet`:
  passed, zero errors, one existing AgentService nullable warning.
- EF CLI: `dotnet ef migrations add CardFilePrivacy --project server --output-dir Migrations`:
  succeeded; generated `20260906223252_CardFilePrivacy`, designer and snapshot.
  Runner URL was set to loopback port 1 and the three standing test seats disabled
  in this command's environment; no shared-stack restart or migration was run.
- Named TUnit class filters with `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c408/ -- --treenode-filter '/*/*/<Class>/*'`:
  `CardFilePolicyTests`: 3 passed, 0 failed (18 policy rows inside one test);
  `CardTaskFileRendererTests`: 11 passed, 0 failed;
  `CardFilePrivacyGitTests`: 5 passed, 0 failed, repeated after repository primitives changed.
- TRX artifacts under `tests/Antiphon.Tests/bin-c408/TestResults/`:
  `c408-policy.trx`, `c408-renderer.trx`, `c408-s1-git.trx`.

These are partial baseline checks for V-1/V-2/V-4/V-5/V-21/V-22/V-23/V-26.
They do not complete these groups; migration upgrade, service/API, expanded Git
and mutation controls remain. No PC control has yet been run.
