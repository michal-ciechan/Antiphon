using Antiphon.Server.Infrastructure.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0495: real <c>Program</c> host with land drain/sweep hosted services removed so
/// admitted requests stay pending and observable.
/// </summary>
public sealed class LandContractWebAppFactory : AntiphonWebAppFactory
{
    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        var hosted = services.Where(d =>
            d.ServiceType == typeof(IHostedService)
            && (d.ImplementationType == typeof(AgentTaskLandHostedService)
                || d.ImplementationType == typeof(AgentTaskLandSweepHostedService))).ToList();
        foreach (var descriptor in hosted)
            services.Remove(descriptor);
    }
}
