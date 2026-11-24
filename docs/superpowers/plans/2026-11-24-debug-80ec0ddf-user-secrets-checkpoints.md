# Debug 80ec0ddf: factory hosts read the machine's user-secrets store (checkpoints)

rc run acca11ba (rc/2026-12-04, Windows Full, chunk antiphon-all-group-128):
`ReviewDelegationRunnerApiTests.The_reviewer_is_a_read_only_review_task_at_the_head_and_its_report_completes_the_node`
failed with `request.RunnerId should be null but was "local"`. Cause and why-missed:
[docs/investigations/2026-11-24-why-missed-review-delegation-runner-id-local.md](../../investigations/2026-11-24-why-missed-review-delegation-runner-id-local.md).

- R-1: an `AntiphonWebAppFactory` host carries no `dotnet user-secrets` configuration source on any machine
  (`ProductionRunnerIsolationTests.The_shared_host_reads_no_user_secrets_store`; red on Linux at the base because the
  Development host defaults add the source even when the store file does not exist).
- R-2: the rc failure reproduced on Linux. CP-2 points `APPDATA` at an ignored scratch store under `.antiphon/`
  holding only `Review:Agents:Runner = local`, which is the Windows store layout
  (`<APPDATA>/Microsoft/UserSecrets/antiphon-server/secrets.json`) the desktop operator's
  `scripts/review-github-setup.ps1` writes. Red at the base with the rc message, green after the fix.
- R-3: the whole reviewer class stays green without the simulation.

The CP-2 store is created before the run with
`mkdir -p .antiphon/usersecrets-sim/Microsoft/UserSecrets/antiphon-server` and a `secrets.json` of
`{"Review:Agents:Runner": "local"}`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-d80ec/ | factory-isolation | `/*/*/ProductionRunnerIsolationTests/*` | R-1 | all listed, 0 failed/skipped | 5 | 15 | false |  |
| CP-2 | S1 | CP-1 | windows-store-sim | `/*/*/ReviewDelegationRunnerApiTests/The_reviewer_is_a_read_only_review_task_at_the_head_and_its_report_completes_the_node` | R-2 | 1 passed, 0 failed/skipped | 1 | 3 | false | APPDATA=/work/worktrees/ws-03-d4/.antiphon/usersecrets-sim |
| CP-3 | S1 | CP-1 | reviewer-class | `/*/*/ReviewDelegationRunnerApiTests/*` | R-3 | all listed, 0 failed/skipped | 8 | 3 | false |  |
