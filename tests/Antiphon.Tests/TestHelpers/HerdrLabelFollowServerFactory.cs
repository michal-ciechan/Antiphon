using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Supervision;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Boots production Program, its follow registrations and detail endpoint on an owned DB/runner.</summary>
internal sealed class HerdrLabelFollowServerFactory(string connectionString, ISessionRunnerClient runner,
    IEventBus bus, TimeProvider clock, bool enabled) : AntiphonWebAppFactory
{
    protected override string ConnectionString => connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HerdrLabelFollow:Enabled"] = enabled.ToString(),
            ["HerdrLabelFollow:SweepPeriodSeconds"] = "60",
        }));
    }

    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        // Preserve the real registration: deleting AddHostedService from Program must fail this fixture.
        services.Count(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(HerdrLabelFollowHostedService)).ShouldBe(1);
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType != typeof(HerdrLabelFollowHostedService)).ToArray()) services.Remove(descriptor);
        services.RemoveAll<ISessionRunnerClient>(); services.AddSingleton(runner);
        services.RemoveAll<IEventBus>(); services.AddSingleton(bus);
        // This host runs only follow and read-only detail requests. The separate real launch/queue
        // harness keeps TimeProvider.System, so no queue deadline can freeze on this timer clock.
        services.RemoveAll<TimeProvider>(); services.AddSingleton(clock);
    }

    public HerdrLabelFollowHostedService FollowHost => Services.GetServices<IHostedService>()
        .OfType<HerdrLabelFollowHostedService>().Single();
}
