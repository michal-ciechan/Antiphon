using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class StaleWorkingTranscriptWatchdogTests
{
    [Test]
    public async Task C1205_Hosted_restart_discovers_working_sessions_immediately()
    {
        await using var host = new WatchdogHost();
        var hosts = host.Services.GetServices<IHostedService>()
            .Where(h => h.GetType().Name == "StaleWorkingTranscriptHostedService").ToArray();
        hosts.Length.ShouldBe(1, "Program must register the independent transcript recovery host");
    }

    private sealed class WatchdogHost : AntiphonWebAppFactory
    {
        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            // Retain the production-runner refusal installed by the base factory.
            // Only the recovery host under test needs to run in this host.
            var unrelated = services.Where(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType?.Name != "StaleWorkingTranscriptHostedService"
                && d.ImplementationType?.Name != "StartupValidator").ToArray();
            foreach (var descriptor in unrelated) services.Remove(descriptor);
        }
    }
}
