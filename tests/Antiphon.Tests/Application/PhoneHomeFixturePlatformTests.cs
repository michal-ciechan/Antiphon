using System.Net;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class PhoneHomeFixturePlatformTests
{
    [Test]
    [Arguments("windows")]
    [Arguments("linux")]
    public async Task Registration_uses_the_supplied_runtime_platform(string platform)
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        var capabilities = new RunnerCapabilitiesDto("test", "test", "test", false, Platform: platform);
        await using var peer = await host.ConnectPeerAsync(capabilities: capabilities);
        (await host.WaitLiveAsync()).Platform.ShouldBe(platform);
    }

    [Test]
    public async Task Explicit_mismatch_still_reaches_the_production_refusal()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        var capabilities = new RunnerCapabilitiesDto("test", "test", "test", false, Platform: "windows");
        var error = await Should.ThrowAsync<HttpRequestException>(() =>
            host.RegisterAsync(platform: "linux", capabilities: capabilities));
        error.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
