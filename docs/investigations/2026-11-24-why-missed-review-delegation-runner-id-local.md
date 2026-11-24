# Why missed: test hosts read the desktop's user-secrets store (RunnerId "local")

Debug task 80ec0ddf, branch `feat/card-task-80ec0ddf`, base master `2f4cfa8df`.

## Symptom

rc run acca11ba (`rc/2026-12-04`, pin `6ed4cb58c`, Windows Full, chunk `antiphon-all-group-128`), 1 failed:
`ReviewDelegationRunnerApiTests.The_reviewer_is_a_read_only_review_task_at_the_head_and_its_report_completes_the_node`,
`ShouldAssertException: request.RunnerId should be null but was "local"`.

## Cause

The product code is right; the test host is not isolated.

- `DelegationReviewAgentRunner.BuildCreateRequest` sets `RunnerId` from `Review:Agents:Runner`
  (`ReviewAgentSettings.Runner`), null when unset. Pinning a runner when one is configured is intended.
- `AntiphonWebAppFactory` boots the real `Program` (`WebApplication.CreateBuilder`) in the Development
  environment. In Development the host defaults add the entry assembly's `dotnet user-secrets` store;
  `server/Antiphon.Server.csproj` declares `<UserSecretsId>antiphon-server</UserSecretsId>`. The factory
  added its in-memory settings on top but never removed that source.
- `scripts/review-github-setup.ps1` (line `'Review:Agents:Runner' = 'local'`, added in `9f9c90824`,
  2026-11-23) writes the operator's review settings into that same store on the desktop:
  `%APPDATA%\Microsoft\UserSecrets\antiphon-server\secrets.json`. No other file in the repository sets
  `Review:Agents:Runner`.
- So every factory host on the desktop read `Review:Agents:Runner=local`; on server2 there is no store
  (`~/.microsoft/usersecrets` does not exist), so the value is null and the test passes. The failure
  belongs to the machine's state, not to the platform, the run order or another test's static state.
  The class is `[NotInParallel]` with its own per-class factory, and no test writes this setting.

## Reproduction on Linux

`PathHelper` reads `APPDATA` first on every OS, so pointing `APPDATA` at a scratch directory with
`Microsoft/UserSecrets/antiphon-server/secrets.json` = `{"Review:Agents:Runner": "local"}` reproduces the
desktop store on server2. Checkpoint run `20261124-101744-f01e` at the red commit `cd54d190d`:

- CP-2 (the rc test, `APPDATA` simulated): failed 1/1 with the rc message, `request.RunnerId should be null but was "local"`.
- CP-1 (`ProductionRunnerIsolationTests`): the new `The_shared_host_reads_no_user_secrets_store` failed with
  `should be empty but had 1 item and was ["secrets.json"]`. The source is present even where the file is absent,
  so this guard is red on any machine, with no simulation.
- CP-3 (the whole class, no simulation): 8/8 passed, as on server2 before.

## Fix

Test-only. `AntiphonWebAppFactory.ConfigureWebHost` removes every `JsonConfigurationSource` whose path is
`secrets.json` before adding its in-memory settings. `ProductionRunnerIsolationTests.The_shared_host_reads_no_user_secrets_store`
pins that the host has no user-secrets provider (including through chained providers) and that
`ReviewAgentSettings.Runner` is null.

## Introducing commit/card

introducing commit/card: 7e378e861fe26d607530f416969d39bd25c889b6 (no card; review-api series, operator's direct push 2026-11-22).
The assertion was correct when written. It went red on the desktop once `review-github-setup.ps1` (`9f9c90824fb447bca0984a6abb16002be1b31cef`) had been run there.
The latent defect is older: `AntiphonWebAppFactory` has never removed the user-secrets source.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- none. The review-api series reached master by direct push, so no Review or Merge selection ran on it.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane, category `Integration`,
`tests/Antiphon.Tests/Review/ReviewDelegationRunnerApiTests.cs`. Even a Merge or Review that had selected the
class would have passed it: those runs happen on server2 (Linux), where the store does not exist. Only a host
where the operator ran `review-github-setup.ps1` can fail it, and the Windows Full qualification on the desktop
is the first run on such a host.

## Should a classification or roster guard have run the class on Windows?

No. A Windows-only classification would not have found it: a Windows host without the operator's store passes too.
This is not a platform difference, so a roster or `windows: only` row is the wrong guard. The right guard is the one
added here. It is platform-independent and red on Linux at the base, so any selection that includes
`ProductionRunnerIsolationTests` catches a regression on any host.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs` -> `/*/*/ProductionRunnerIsolationTests/*`

(Proposed, not added: this Debug keeps the change minimal.)

## Siblings

`ReviewEndToEndApiTests`, `ReviewPollingApiTests` and the other `AntiphonWebAppFactory` Review classes did not assert
`RunnerId`. They `PostConfigure` the policy, polling and viewer settings they check, so the store could only add
keys around those settings. With the source removed, none of them can read the store. Hosts that do not derive from
`AntiphonWebAppFactory` (`SmokeTests`, `PostgresJitPolicyTestsComposition`,
`Antiphon.Observability.Tests/LiveStartupIntegrationTests`, `Antiphon.E2E` `AntiphonAppFixture`) still use the
Development defaults. None of them asserts a review setting today.

## Exemption

none
