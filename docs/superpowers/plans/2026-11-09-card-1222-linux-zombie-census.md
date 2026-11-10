# CARD-1222 Linux zombie census: supported omission

Plan for task `b460c2b8` on `feat/card-task-b460c2b8`. Base `84bc17576287fb43cf2dc04395970a8bc52e2d76`.
Recommendation: record a supported omission. Register the WMI census only on Windows, refuse `ZombieCensus:Enabled` true at startup on every other OS, and keep every Linux compose flag false. Do not add a `/proc` census.

`GET /api/runner-defaults` and `GET /api/session-runners` were not readable from this runner (`127.0.0.1:17202` connection refused). This plan names no fleet host. Checkpoints omit `-Runner` and `-Platform`. The lane is the host that runs the checkpoint tool. The Windows-native registration method is owed and skips off Windows.

This plan adds no session that waits for input. The CARD-1083 release checklist does not apply.

## Decisions

- **D-1.** The Linux contract is a supported omission. CARD-1221 left the census "Not supported" and assigned the choice to this card. The card's open-decision fallback is platform-selected registration so a Linux server never reaches the WMI throw. A `/proc` implementation is the wrong process table (evidence below).
- **D-2.** Startup refusal is the existing `ZombieCensusSettingsValidator` on `ValidateOnStart` (`server/Program.cs:218-221`). When `Enabled` is true and `OperatingSystem.IsWindows()` is false, validation fails with this exact sentence: `Zombie census is a supported omission off Windows. Set ZombieCensus:Enabled to false.` Other field checks still run. `Enabled` false does not add this failure.
- **D-3.** `ZombieCensusRegistration.AddProcessCensus(IServiceCollection)` is the production factory. It passes `OperatingSystem.IsWindows()` to `AddProcessCensus(IServiceCollection, bool)`. True registers `WindowsZombieProcessCensus`. False registers `ZombieCensusUnsupportedPlatform`, whose `SnapshotAsync` throws `InvalidOperationException` with the same sentence. It never returns an empty list (`IZombieProcessCensus.cs:6-8`).
- **D-4.** Test hosts that boot `Program` set the flag false, because `server/appsettings.json:210-211` defaults `Enabled` to true for the desktop. `ProductionRunnerGuard` sets `ZombieCensus__Enabled=false` for every boot in `Antiphon.Tests`. The Observability startup factory and the E2E Kestrel factory set `ZombieCensus:Enabled` false in memory. Compose files are already false and stay false.
- **D-5.** No migration, no DTO change, no route change, no change to `WindowsZombieProcessCensus`, the classifier, the job, or the Hangfire register/remove function. Desktop `appsettings.json` stays `Enabled: true`.
- **D-6.** New production types are named `ZombieCensus*.cs` so `tests/Antiphon.Tests/Layering/layers.txt:12` already maps them to Host. Do not edit `layers.txt`.
- **D-7.** The public parameterless registration on Windows is a pending native obligation. Forcing the bool on Linux supplements that witness. A skip is not a pass of the Windows branch.

## Ground truth

| What the card or the 2026-11-07 decision says | What this checkout does |
|---|---|
| `Program.cs:576` registers `WindowsZombieProcessCensus` with no platform guard (card evidence, commit `d7648f8e9`) | The unconditional registration is `server/Program.cs:626`. Options bind and `ValidateOnStart` are `server/Program.cs:218-221`. |
| `docker-stack.md` cites `Program.cs:622` and the job at `:1172-1173` or `:1177-1180` | The job call is `server/Program.cs:1189` inside `if (hangfire.ServerEnabled)` at `:1186`. `HangfireConfiguration.RegisterCensusJob` adds the row only when `Enabled` is true and removes it otherwise (`server/Infrastructure/Agents/HangfireConfiguration.cs:136-141`). |
| A Linux server with the flag on throws from `SnapshotAsync` | `WindowsZombieProcessCensus.SnapshotAsync` throws `PlatformNotSupportedException` when `OperatingSystem.IsWindows()` is false (`server/Infrastructure/Agents/WindowsZombieProcessCensus.cs:17-18`). `ZombieCensusService.RunAsync` wraps that as `InvalidOperationException` (`server/Infrastructure/Agents/ZombieCensusService.cs:52-58`). The validator (`server/Application/Settings/ZombieCensusSettingsValidator.cs:7-53`) does not look at the OS, so the host still starts. |
| Compose disables the census | `ZombieCensus__Enabled` is `"false"` at `docker-compose.yml:45`, `docker-compose.server2-staging-server.yml:54`, and `docker-compose.server2-server.yml:90`. No compose file sets `pid:`. |
| The census is the machine's agent process table | `LoadDbSnapshotAsync` keeps sessions with `RunnerId == null` only (`ZombieCensusService.cs:112-113`). Phone-home sessions are excluded. The Windows reader leaves `Cwd` empty and resamples CPU across a 5-second delay (`WindowsZombieProcessCensus.cs:21-47`, DTO at `server/Application/Dtos/ZombieCensusDtos.cs:6-15`). |
| A supported omission is already written down | `docs/bootstrap.md:544` describes a local Windows OS-process census. `docs/docker-stack.md:1044` and `:1059` say "Not supported" and that enabling the flag throws. `:1201` says enabling census on Linux throws. |
| Tests already avoid a live WMI scan | `ProductionRunnerGuard` sets `Hangfire__ServerEnabled=false` (`tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs:58-74`). Factories then replace `IZombieProcessCensus` (`AntiphonWebAppFactory.cs:143-144`, `LiveStartupIntegrationTests.cs:126-127`). They do not set `ZombieCensus:Enabled`, so the desktop default true still binds. |

The 2026-11-07 investigation stays a historical decision record. Do not edit it. The living text is `docs/bootstrap.md` and `docs/docker-stack.md`.

`tests/impacted-classes.json` has no row for `server/Program.cs`, `server/Infrastructure/Agents/ZombieCensus*`, `server/Application/Settings/ZombieCensusSettingsValidator.cs`, `ProductionRunnerGuard.cs`, the Observability or E2E factories, or those two docs. CARD-1782 selects nothing from this diff. No migration, public DTO, route, operation label, remote script, or statement budget changes.

## Intended behaviour

### Before

1. Host build binds `ZombieCensus` and runs `ZombieCensusSettingsValidator`. `Enabled` defaults to true (`ZombieCensusSettings.cs:9`, `appsettings.json:210-211`). The OS is not an input.
2. `Program.cs:626` always registers `WindowsZombieProcessCensus` as `IZombieProcessCensus`. `ZombieCensusService` and `ZombieCensusJob` are registered beside it (`Program.cs:627-629`).
3. When the Hangfire worker is on, `RegisterCensusJob` adds `antiphon:zombie-census` if `Enabled` is true, or removes that id if it is false.
4. The job calls `ZombieCensusService.RunAsync`, which calls `SnapshotAsync`, then the runner, then local (`RunnerId == null`) sessions, then manifests, then `ZombieCensusClassifier`. The result is published to `ZombieCensusState` for `AttentionKind.ZombieCensusReport`. The job does not kill processes.
5. Off Windows, step 4 throws from the WMI type if something invokes the census. Linux compose avoids that by leaving the flag false, and the base stack also leaves the worker false. The throw is latent. A Linux process that turns the flag and the worker on still starts, then fails inside the job.

### After

1. The same validator runs. `Enabled` true and a non-Windows process add the omission sentence and `ValidateOnStart` stops the host. `Enabled` false takes the existing field checks only. The recurring row is still removed when the worker is on and the flag is false.
2. `Program.cs` calls `ZombieCensusRegistration.AddProcessCensus(services)` at the old line 626. Windows resolves `WindowsZombieProcessCensus`. Any other OS resolves `ZombieCensusUnsupportedPlatform`.
3. The unsupported `SnapshotAsync` throws `InvalidOperationException` with the omission sentence if something calls it. That call is not the startup path. Startup already refused `Enabled` true. `Enabled` false does not register the job. The throw is the backstop required by the interface, so a failed read cannot look like an empty machine.
4. Windows data flow is the same WMI snapshot, runner list, `RunnerId == null` rows, classifier, state, and attention report.

### Unchanged

`WindowsZombieProcessCensus` (including its off-Windows throw and the 5-second resample), `ZombieCensusClassifier`, `ZombieCensusJob`, `ZombieCensusService` except that it now receives whichever census the factory registered, `HangfireConfiguration.RegisterCensusJob`, DTOs, attention kind 47, compose flag values, and desktop `appsettings.json`.

## Design

The production server container does not share the host PID namespace, and the census does not read remote runner sessions. A `/proc` port of the WMI contract would report the server container's own processes and would miss the agents. That is a false all-clear. The omission matches the card's acceptance: a Linux server with the flag on refuses at startup with a configuration error and does not throw from `SnapshotAsync` on the job path.

Rejected `/proc` census. Same `ZombieOsProcess` contract would still be a container-local process list. No compose service sets `pid:`. `RunnerId == null` drops phone-home sessions. Parent, command line, start time, working set, and a second CPU sample are a new parser for the wrong machine.

Rejected an empty snapshot. `IZombieProcessCensus` says a failed read throws and is never an empty list standing in for "could not read".

Rejected registering no `IZombieProcessCensus` off Windows. `ZombieCensusService` always requires the seam. A missing registration fails at first resolve, including test hosts, with a DI error rather than the configuration sentence.

Rejected leaving the WMI type registered and only documenting the flag. The card's failing test is unconditional Windows registration reachable on Linux. Startup refusal without swapping the registration still constructs the WMI type.

Rejected editing `WindowsZombieProcessCensus`. CARD-0298 owns that behaviour. Its off-Windows throw stays as defense if the type is ever called. Production registration stops calling it off Windows.

Test-host override is required. After D-2, every Linux `Program` boot that still binds `Enabled` true fails `ValidateOnStart` before the factory can replace the census. The boots in this checkout are the `Antiphon.Tests` assembly guard (covers `SmokeTests` and `AntiphonWebAppFactory`), `LiveStartupIntegrationTests.StartupFactory` (`tests/Antiphon.Observability.Tests/LiveStartupIntegrationTests.cs:97-117`), and `AntiphonAppFixture.KestrelWebApplicationFactory` (`tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs:640-658`). `LandDeliveryFixture` uses that Kestrel factory. Set the flag false in those three places. If Code finds another `WebApplicationFactory<Program>`, give it the same key.

## Tests

Class `Antiphon.Tests.Infrastructure.ZombieCensusPlatformTests`, `[Category("Unit")]`. No host, database, or process. Read docs through `DockerStackDocuments.Read`. Build settings with `new ZombieCensusSettings()` (property defaults). `Europe/London` must already resolve; that is the existing validator rule. A missing zone is a host defect, not a reason to change the production timezone.

Skip with `Skip.Test` and return. Do not assert the other platform's result. `[SupportedOSPlatform]` does not skip.

S1 commits these methods against the still-unconditional seam, runs the class once, and records the failures before any fix:

- `Enabled_true_off_windows_is_rejected`. Skip on Windows. Public `ZombieCensusSettingsValidator.Validate` with `Enabled` true fails, and the message contains the omission sentence. Red on current source: the validator returns success.
- `Enabled_false_off_windows_is_accepted`. Every OS. `Enabled` false is success. Green on current source. It locks D-2 so the new check cannot reject a disabled census.
- `Linux_registration_is_not_the_wmi_census`. Skip on Windows. `ZombieCensusRegistration.AddProcessCensus(services)` then `GetRequiredService<IZombieProcessCensus>()` is not `WindowsZombieProcessCensus`. Red on S1 because the seam still registers the WMI type. S2 also asserts the resolved type is `ZombieCensusUnsupportedPlatform`.
- `Bootstrap_records_the_supported_omission`. `docs/bootstrap.md` contains the omission sentence in the `antiphon:zombie-census` row. Red until S2.
- `Docker_stack_records_the_supported_omission`. `docs/docker-stack.md` contains the omission sentence. It does not contain `Enabling the flag on Linux throws.`, `Census must stay unregistered or the WMI job throws.`, `Enabling census on Linux. The job throws.`, or `Same WMI throw as the compose row.` Red until S2.

S2 adds these methods with the fix:

- `Forced_non_windows_branch_registers_the_omission`. `AddProcessCensus(services, false)` resolves `ZombieCensusUnsupportedPlatform` on every OS.
- `Forced_windows_branch_registers_the_wmi_type`. `AddProcessCensus(services, true)` resolves `WindowsZombieProcessCensus`. Do not call `SnapshotAsync` (that would invoke WMI logic on Linux).
- `Omission_snapshot_throws_and_is_not_empty`. `new ZombieCensusUnsupportedPlatform().SnapshotAsync` throws `InvalidOperationException` whose message contains the omission sentence.
- `Windows_registration_uses_the_wmi_census`. Skip off Windows with the reason that a skip is not a pass. On Windows, parameterless `AddProcessCensus` resolves `WindowsZombieProcessCensus`.

S2 also adds one assertion to the existing `ProductionRunnerGuardTests.Every_program_boot_in_this_assembly_is_pointed_at_a_dead_runner`: `ZombieCensus__Enabled` is `false`. Set the variable in the same commit as the assertion, in `PointEveryProgramBootAwayFromTheProductionRunner`.

Mutants, one production edit each, detected by the named method:

- PC-1. Delete the non-Windows `Enabled` true failure. `Enabled_true_off_windows_is_rejected` stays success.
- PC-2. Reject every non-Windows settings object, including `Enabled` false. `Enabled_false_off_windows_is_accepted` fails.
- PC-3. Register `WindowsZombieProcessCensus` for both bool values. `Linux_registration_is_not_the_wmi_census` and `Forced_non_windows_branch_registers_the_omission` see the WMI type.
- PC-4. Ignore the bool and always register the unsupported type. `Forced_windows_branch_registers_the_wmi_type` sees the unsupported type.
- PC-5. Return an empty list from `ZombieCensusUnsupportedPlatform.SnapshotAsync`. `Omission_snapshot_throws_and_is_not_empty` does not throw.
- PC-6. Remove the omission sentence from `docs/bootstrap.md`. `Bootstrap_records_the_supported_omission` fails.
- PC-7. Restore `Enabling the flag on Linux throws.` in `docs/docker-stack.md`. `Docker_stack_records_the_supported_omission` fails.
- PC-8. Leave `ZombieCensus__Enabled` unset in the assembly guard. `Every_program_boot_in_this_assembly_is_pointed_at_a_dead_runner` fails the new assertion.

Doc pins are the bootstrap and docker-stack methods above. They read the files. They do not parse line numbers.

The S1 class run is unlisted on purpose. A checkpoint row that expects failures is exit 1, and the tool treats that as a failed row. Run it once under the build slot, record executed/failed/skipped, commit the red tests, then fix in S2. CP-1 and CP-2 are the green proof after S2.

## Migrations

None. No schema, model, or `Down` change.

## Slices

### S1 — red witnesses (about 60 minutes)

Files:

- `server/Infrastructure/Agents/ZombieCensusRegistration.cs` (new). `AddProcessCensus(IServiceCollection)` only, and it registers `WindowsZombieProcessCensus`. Behaviour matches `Program.cs:626`.
- `server/Program.cs` at the current line 626. Replace the inline `AddSingleton` with the seam call.
- `tests/Antiphon.Tests/Infrastructure/ZombieCensusPlatformTests.cs` with the five S1 methods.

Commit the red class before changing the validator, the bool overload, the unsupported type, or the docs. Push that commit. On Linux the expected result is 4 failed and 1 passed: failures are `Enabled_true_off_windows_is_rejected`, `Linux_registration_is_not_the_wmi_census`, `Bootstrap_records_the_supported_omission`, and `Docker_stack_records_the_supported_omission`; the pass is `Enabled_false_off_windows_is_accepted`. Count them from the TRX. Do not weaken an assertion to make S1 green.

### S2 — omission (about 90 minutes)

Files:

- `server/Infrastructure/Agents/ZombieCensusRegistration.cs`. Add the bool overload. Parameterless method passes `OperatingSystem.IsWindows()`.
- `server/Infrastructure/Agents/ZombieCensusUnsupportedPlatform.cs` (new).
- `server/Application/Settings/ZombieCensusSettingsValidator.cs`. Public `Validate` passes `OperatingSystem.IsWindows()` into a public overload that takes the bool. The omission failure is one entry in the existing failure list.
- `tests/Antiphon.Tests/Infrastructure/ZombieCensusPlatformTests.cs`. Strengthen the Linux registration assertion and add the four S2 methods.
- `tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs`. Env var and assertion.
- `tests/Antiphon.Observability.Tests/LiveStartupIntegrationTests.cs`. In-memory `ZombieCensus:Enabled` = `false` next to the Hangfire key.
- `tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs`. Same key next to `ZombieCensus:SessionLogPath`.
- `docs/bootstrap.md` zombie-census row. Keep the Windows report-only description. Add the omission sentence.
- `docs/docker-stack.md` census row (`:1044`), Hangfire blocker sentence (`:1043`), section 2 census row (`:1059`), and the risk bullet (`:1201`). Correct the behaviour text so it describes startup refusal and platform registration. Leave `ZombieCensus__Enabled` is false at `:1750`. Do not remove that key name; `DockerStackDocumentationTests` pins it in the production-server section.

Rewrite the census row's "today" cell so it no longer says the Linux host always registers the WMI type or that enabling the flag throws at snapshot time. Disposition becomes `Supported omission`. The blocker cell contains the omission sentence and the two facts that a container `/proc` read is the wrong namespace and that the query keeps `RunnerId == null`. Owner cell: `CARD-1222. Stays off in every Linux compose.`

Section 2 disposition becomes `Supported omission` with the same sentence. Replace `Same WMI throw as the compose row.`

Hangfire blocker: replace `Census must stay unregistered or the WMI job throws.` with `Census stays off. Setting ZombieCensus:Enabled true off Windows fails startup validation.` Keep the rest of that cell.

Risk bullet `:1201`: replace `Enabling census on Linux. The job throws.` with `Enabling census on Linux. Startup validation refuses ZombieCensus:Enabled true. Compose stays false.`

Commit S2 and push. Then run CP-1 and CP-2.

## Performance budget

Both rows are provisional. This class has no measured p95 (CARD-1404 / CARD-1720). The first green checkpoint run is the collection. Do not repeat a green row to manufacture a p95. If either row's charged execution exceeds 30 minutes, stop and hand the run id, SHA, host, and phase times to a Debug task. Do not widen an assertion or a timeout to absorb that.

CP-1 wall is dominated by one cold `tests/Antiphon.Tests` build plus nine unit methods. Provisional estimate 10 minutes. The bodies construct a validator, a `ServiceCollection`, and two file reads. No web host, database, migration, or Docker. CP-2 reuses that build. Its selection is two existing integration methods; one opens a TCP connect to `127.0.0.1:1` with the existing 5-second ceiling. Provisional estimate 2 minutes. `TestDbFixture` starts Postgres only when the selected tests need the shared store. This selection does not. If a receipt shows a container start, record that phase and treat it as unexpected.

Importer row deadlines are `max(15, 3 * EstimatedMinutes)`: 30 minutes for CP-1 and 15 minutes for CP-2. That 3x floor is the CARD-1720 importer exception, not an R5 measurement. Expected body time is seconds. Admission is the build-slot gate, separate from charged execution, default 45 minutes. Derived total allowance is `max(30, 2*12+10)` = 34 minutes. No `totalTimeoutMinutes` override.

No shared fixture. Per-test setup is a settings object and a service collection. Phase times come from the checkpoint report (admission, build, host, tests, teardown). File-read and timezone-lookup subphases stay unknown until a receipt splits them. Do not report them as zero. Evidence stays under `.antiphon/checkpoints/<run>/`.

## Risks and operator steps

No operator step. Do not deploy, restart, or set `ZombieCensus__Enabled` true. Current compose, staging, and production server files already set it false. Desktop production stays Windows with `Enabled` true and is not restarted by this card.

A Linux `dotnet run` of the server that inherits `appsettings.json` will refuse to start until the flag is false. That is D-2. Compose already supplies the override.

The unsupported snapshot throw is reachable only if validation is bypassed or a test calls it. The job path does not reach it.

Windows owed, not run here and not a passed skip: execute `Windows_registration_uses_the_wmi_census` on Windows and confirm parameterless registration resolves `WindowsZombieProcessCensus` while desktop `Enabled` true still validates. Do not add a WMI `SnapshotAsync` call to the Linux rows.

## OS witness obligations

Code writes version-1 JSON under the ignored evidence root and runs:

`pwsh -NoProfile -File scripts/check-os-witnesses.ps1 -BaseRef 84bc17576287fb43cf2dc04395970a8bc52e2d76 -HeadRef <full S2 sha> -EvidencePath <ignored-json> -Plan docs/superpowers/plans/2026-11-09-card-1222-linux-zombie-census.md`

Inventory every changed non-document path. Branch anchor in production is the literal `OperatingSystem.IsWindows()` passed by the parameterless registration method and by `ZombieCensusSettingsValidator.Validate`. Inspected paths include `server/Program.cs`, `server/Infrastructure/Agents/ZombieCensusRegistration.cs`, `server/Infrastructure/Agents/ZombieCensusUnsupportedPlatform.cs`, and `server/Application/Settings/ZombieCensusSettingsValidator.cs`. Helpers: `server/Infrastructure/Agents/ZombieCensusService.cs` (caller of `SnapshotAsync`, unchanged guard). Test-only paths (`ProductionRunnerGuard.cs`, the two factories, the new test file) are `test-only`. Docs are documentation.

Witnesses:

- Linux public validator. `productionType` `ZombieCensusSettingsValidator`, `factory` `Validate`, `testCase` `Enabled_true_off_windows_is_rejected`, `targetOS` Linux, `executionMode` native, `checkpointId` CP-1. `nativeObligation` recorded only with the CP-1 receipt; until then pending is honest.
- Linux public registration. `productionType` `ZombieCensusRegistration`, `factory` `AddProcessCensus`, `testCase` `Linux_registration_is_not_the_wmi_census`, same native Linux obligation.
- Forced false. `factory` `AddProcessCensus(IServiceCollection, bool)`, `testCase` `Forced_non_windows_branch_registers_the_omission`, `executionMode` portable-forced, `targetOS` Linux, `nativeObligation` `not-required` because the bool is the input and the public method has its own witness.
- Forced true. `testCase` `Forced_windows_branch_registers_the_wmi_type`, `executionMode` portable-forced, `targetOS` Windows. This does not replace the native Windows witness.
- Windows public registration. `testCase` `Windows_registration_uses_the_wmi_census`, `executionMode` native, `targetOS` Windows, `nativeObligation` `pending`, command is the CP-1 filter on a Windows host. A Linux skip stays pending.

Input `kind` is `other`, not optional-config. The validator reads the bound `Enabled` bool. It does not read the configuration key. Absent key becomes the property default true through the existing binder, which the rejecting test covers with `new ZombieCensusSettings()`. The test source must contain `options.Enabled.ShouldBeTrue()` before the rejecting `Validate` call and `options.Enabled.ShouldBeFalse()` before the accepting call. Those strings are the `setupAssertion` values. Empty string remains the existing bool binder's concern. Do not add an empty-string branch in the validator.

`restartImpact.required` is false. None of the named restart-script paths change. `additionalPaths` includes `server/Program.cs` because validation runs at host start. This card restarts nothing. If the checker disagrees, keep the inventory and record the obligation it asks for. Do not delete a candidate to make exit 0.

Also run `scripts/check-evidence-diff.ps1` over `84bc17576287fb43cf2dc04395970a8bc52e2d76..HEAD` before the Code report. Both scripts are unlisted because their SHAs are the S2 tip. Say so in the report.

## Verification design

V-1: `ZombieCensusPlatformTests.Enabled_true_off_windows_is_rejected` asserts the public validator fails off Windows when Enabled is true, and the failure contains the omission sentence.

V-2: `ZombieCensusPlatformTests.Enabled_false_off_windows_is_accepted` asserts the public validator succeeds when Enabled is false.

V-3: `ZombieCensusPlatformTests.Linux_registration_is_not_the_wmi_census` asserts parameterless registration off Windows resolves the unsupported census type.

V-4: `ZombieCensusPlatformTests.Forced_non_windows_branch_registers_the_omission` asserts the false bool resolves the unsupported census type.

V-5: `ZombieCensusPlatformTests.Forced_windows_branch_registers_the_wmi_type` asserts the true bool resolves the WMI census type and does not call SnapshotAsync.

V-6: `ZombieCensusPlatformTests.Omission_snapshot_throws_and_is_not_empty` asserts SnapshotAsync throws InvalidOperationException with the omission sentence.

V-7: `ZombieCensusPlatformTests.Bootstrap_records_the_supported_omission` asserts docs/bootstrap.md contains the omission sentence.

V-8: `ZombieCensusPlatformTests.Docker_stack_records_the_supported_omission` asserts docs/docker-stack.md contains the omission sentence and does not contain the four stale sentences named in S1.

V-9: `ZombieCensusPlatformTests.Windows_registration_uses_the_wmi_census` asserts parameterless registration on Windows resolves the WMI census type. Off Windows it skips. The skip is pending native work.

R-1: `ProductionRunnerGuardTests.Every_program_boot_in_this_assembly_is_pointed_at_a_dead_runner` asserts the census enabled environment variable is false for the assembly guard.

| PC | Mutation | Filter | Detects |
|---|---|---|---|
| PC-1 | delete the non-Windows Enabled true failure | `/*/*/ZombieCensusPlatformTests/Enabled_true_off_windows_is_rejected` | `ZombieCensusPlatformTests.Enabled_true_off_windows_is_rejected` |
| PC-2 | reject Enabled false off Windows | `/*/*/ZombieCensusPlatformTests/Enabled_false_off_windows_is_accepted` | `ZombieCensusPlatformTests.Enabled_false_off_windows_is_accepted` |
| PC-3 | register the WMI type for both bool values | `/*/*/ZombieCensusPlatformTests/Linux_registration_is_not_the_wmi_census` | `ZombieCensusPlatformTests.Linux_registration_is_not_the_wmi_census` |
| PC-4 | ignore the bool and always register the unsupported type | `/*/*/ZombieCensusPlatformTests/Forced_windows_branch_registers_the_wmi_type` | `ZombieCensusPlatformTests.Forced_windows_branch_registers_the_wmi_type` |
| PC-5 | return an empty process list from the unsupported snapshot | `/*/*/ZombieCensusPlatformTests/Omission_snapshot_throws_and_is_not_empty` | `ZombieCensusPlatformTests.Omission_snapshot_throws_and_is_not_empty` |
| PC-6 | remove the omission sentence from bootstrap | `/*/*/ZombieCensusPlatformTests/Bootstrap_records_the_supported_omission` | `ZombieCensusPlatformTests.Bootstrap_records_the_supported_omission` |
| PC-7 | restore the stale Linux throw sentence in docker-stack | `/*/*/ZombieCensusPlatformTests/Docker_stack_records_the_supported_omission` | `ZombieCensusPlatformTests.Docker_stack_records_the_supported_omission` |
| PC-8 | leave the census env var unset in the assembly guard | `/*/*/ProductionRunnerGuardTests/Every_program_boot_in_this_assembly_is_pointed_at_a_dead_runner` | `ProductionRunnerGuardTests.Every_program_boot_in_this_assembly_is_pointed_at_a_dead_runner` |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | tests/Antiphon.Tests -> bin-c1222/ | omission | `/*/*/ZombieCensusPlatformTests*/*` | V-1..V-9 | all listed, 0 failed; Linux skips the Windows registration method; Windows skips the two off-Windows methods | 8 linux / 7 windows | 10 |
| CP-2 | S2 | CP-1 | guard | `/*/*/ProductionRunnerGuardTests*/*` | R-1 | all listed, 0 failed | 2 | 2 |
