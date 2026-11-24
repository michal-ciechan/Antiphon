using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The client commands' production tool runner (ClientTools.RunProcess, unit 32 of the 2026-11-22 plan) on a real child that
/// is the platform shell, never node or npm: stdout and stderr lines both reach the caller, the exit code comes back, and a
/// program that cannot start is one line and exit 1 through ClientTools.Invoke.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ClientToolRunnerTests
{
    [Test]
    public void The_production_runner_hands_over_both_streams_and_the_exit_code()
    {
        var launch = OperatingSystem.IsWindows()
            ? new ClientToolLaunch(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", ["/d", "/c", "echo out-line& (echo err-line)1>&2& exit /b 3"], Path.GetTempPath())
            : new ClientToolLaunch("sh", ["-c", "echo out-line; echo err-line 1>&2; exit 3"], Path.GetTempPath());
        var lines = new List<string>();

        ClientTools.RunProcess(launch, lines.Add).ShouldBe(3);

        lines.Select(line => line.Trim()).OrderBy(line => line, StringComparer.Ordinal).ToArray().ShouldBe(["err-line", "out-line"]);

        var missing = new List<string>();
        ClientTools.Invoke(ClientTools.RunProcess, () => new ClientToolLaunch("antiphon-no-such-tool-" + Guid.NewGuid().ToString("N"), [], Path.GetTempPath()), missing.Add)
            .ShouldBe(1);
        missing.ShouldHaveSingleItem().ShouldNotBeNullOrWhiteSpace();
    }
}
