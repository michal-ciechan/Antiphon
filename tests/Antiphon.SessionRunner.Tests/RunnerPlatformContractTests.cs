using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0710 V-3. Capability builders report the actual OS. Unknown is not Windows.</summary>
public sealed class RunnerPlatformContractTests
{
    [Test]
    public async Task Local_and_phone_home_report_actual_platform()
    {
        var expected = RunnerPlatformWire.FromOperatingSystem();
        expected.ShouldNotBeNull();
        var logRoot = TestSessionLogRoot.Create("c710-platform-contract");
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = logRoot, PtyHostLingerHours = 0.02, CpuWatchdogEnabled = false }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var build = new RunnerBuildDto("1.0.0+c710", new string('a', 40), DateTime.UnixEpoch, DateTime.UnixEpoch);
        var phoneHome = new PhoneHomeRuntimeAdapter(runtime, build).Capabilities();
        phoneHome.Platform.ShouldBe(expected);
        phoneHome.Features.ShouldNotBeNull().ShouldContain(RunnerPlatformWire.Feature);

        var local = runtime.DescribeCapabilities(build, [SessionBackends.PtyHost], [GrokRulesTransport.Capability]);
        local.Platform.ShouldBe(expected);
        local.Features.ShouldNotBeNull().ShouldContain(RunnerPlatformWire.Feature);
    }

    [Test]
    public void Old_payloads_remain_unknown()
    {
        var json = """{"ptyBackend":"InboxConhost","ptyBackendRequested":"inbox","ptyBackendReason":"test","ptyBackendFellBack":false}""";
        var dto = JsonSerializer.Deserialize<RunnerCapabilitiesDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        dto.ShouldNotBeNull();
        dto.Platform.ShouldBeNull();
        dto.Features.ShouldBeNull();
    }

    [Test]
    public void Unknown_os_is_not_windows()
    {
        RunnerPlatformWire.Normalize("freebsd").ShouldBeNull();
        RunnerPlatformWire.Normalize("SunOS").ShouldBeNull();
        RunnerPlatformWire.Normalize("windows").ShouldBe(RunnerPlatformWire.Windows);
        RunnerPlatformWire.Normalize("LINUX").ShouldBe(RunnerPlatformWire.Linux);
        RunnerPlatformWire.Normalize("not linux").ShouldBeNull();
        (RunnerPlatformWire.Normalize("darwin") == RunnerPlatformWire.Windows).ShouldBeFalse();
    }
}
