using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1744 S4. Phone-home env, autostart config refusal, and the AppHost
/// launch guard. The harness is pure functions and file parses, so the same
/// cases run on Windows and Linux.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C1744G3PhoneHomeSupervisionTests
{
    [Test]
    public Task Env_table_matches_the_start_script() =>
        ScriptHarness.RunHarnessCaseAsync(
            "test-windows-phone-home-runner.ps1",
            "C1744G3",
            "env-table",
            5,
            "C1744G3 env-table.keys",
            "C1744G3 env-table.values",
            "C1744G3 env-table.executables",
            "C1744G3 env-table.start-calls-helper",
            "C1744G3 env-table.start-no-inline-table");

    [Test]
    public Task Autostart_without_config_is_the_direct_runner() =>
        ScriptHarness.RunHarnessCaseAsync(
            "test-windows-phone-home-runner.ps1",
            "C1744G3",
            "autostart-direct",
            3,
            "C1744G3 autostart-direct.mode",
            "C1744G3 autostart-direct.env",
            "C1744G3 autostart-direct.exe-args");

    [Test]
    public Task Autostart_with_config_runs_desktop_win() =>
        ScriptHarness.RunHarnessCaseAsync(
            "test-windows-phone-home-runner.ps1",
            "C1744G3",
            "autostart-phone-home",
            5,
            "C1744G3 autostart-phone-home.runner-id",
            "C1744G3 autostart-phone-home.capacity",
            "C1744G3 autostart-phone-home.port",
            "C1744G3 autostart-phone-home.explicit-port",
            "C1744G3 autostart-phone-home.helper-source");

    [Test]
    public Task Autostart_refuses_bad_config_without_fallback() =>
        ScriptHarness.RunHarnessCaseAsync(
            "test-windows-phone-home-runner.ps1",
            "C1744G3",
            "autostart-refusals",
            13,
            "C1744G3 autostart-refusals.local",
            "C1744G3 autostart-refusals.desktop",
            "C1744G3 autostart-refusals.bad-id",
            "C1744G3 autostart-refusals.http-origin",
            "C1744G3 autostart-refusals.origin-path",
            "C1744G3 autostart-refusals.capacity-0",
            "C1744G3 autostart-refusals.capacity-3",
            "C1744G3 autostart-refusals.port-17299",
            "C1744G3 autostart-refusals.port-not-broker",
            "C1744G3 autostart-refusals.unknown-key",
            "C1744G3 autostart-refusals.malformed",
            "C1744G3 autostart-refusals.unc-root",
            "C1744G3 autostart-refusals.secret-missing");

    [Test]
    public Task AppHost_launchers_refuse_while_phase2_config_exists() =>
        ScriptHarness.RunHarnessCaseAsync(
            "test-windows-phone-home-runner.ps1",
            "C1744G3",
            "apphost-guard",
            5,
            "C1744G3 apphost-guard.present",
            "C1744G3 apphost-guard.absent",
            "C1744G3 apphost-guard.restart-order",
            "C1744G3 apphost-guard.aspire-order",
            "C1744G3 apphost-guard.no-linux-name");

    [Test]
    public Task Supervision_scripts_stay_ascii_and_51_compatible() =>
        ScriptHarness.RunHarnessCaseAsync(
            "test-windows-phone-home-runner.ps1",
            "C1744G3",
            "compat",
            4,
            "C1744G3 compat.ascii",
            "C1744G3 compat.helper-syntax",
            "C1744G3 compat.autostart-syntax",
            "C1744G3 compat.no-requires-7");
}
