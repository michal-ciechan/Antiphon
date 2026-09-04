using System.Net;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Tests.TestHelpers;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.Server;
using Hangfire.Storage;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-0298: Hangfire worker stays off in Program test boots; storage, recurring registration,
/// and the local-only dashboard behave as configured.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class HangfireStartupSafetyTests
{
    private readonly AntiphonWebAppFactory _factory;

    public HangfireStartupSafetyTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task Assembly_guard_and_factory_override_disable_the_Hangfire_worker()
    {
        Environment.GetEnvironmentVariable(ProductionRunnerGuard.HangfireServerEnabledEnvVar)
            .ShouldBe("false");
        _factory.Services.GetService<IBackgroundProcessingServer>().ShouldBeNull();
        _factory.Services.GetServices<IHostedService>()
            .Any(s => (s.GetType().FullName ?? "").Contains("Hangfire", StringComparison.OrdinalIgnoreCase)
                      || (s.GetType().Name ?? "").Contains("BackgroundJobServer", StringComparison.OrdinalIgnoreCase))
            .ShouldBeFalse();
        // CARD-0336: ListCalls / ZombieCensus.Calls are process-wide on the shared
        // AntiphonWebAppFactory. Isolation is 0; a full session is not. Keep the env-var /
        // IBackgroundProcessingServer / no Hangfire hosted-service pins.
        await Task.CompletedTask;
    }

    [Test]
    public async Task Configured_in_memory_storage_expires_after_eight_days()
    {
        var storage = _factory.Services.GetRequiredService<JobStorage>().ShouldBeOfType<InMemoryStorage>();
        storage.Options.MaxExpirationTime.ShouldBe(TimeSpan.FromDays(8));
        HangfireConfiguration.CreateStorageOptions(new HangfireSettings { HistoryRetentionDays = 8 })
            .MaxExpirationTime.ShouldBe(TimeSpan.FromDays(8));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Recurring_census_job_is_not_registered_when_the_worker_is_disabled()
    {
        var storage = _factory.Services.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        connection.GetRecurringJobs().ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Recurring_census_job_is_re_added_on_a_fresh_in_memory_host_with_London_daily_cron()
    {
        var settings = new ZombieCensusSettings();
        var storage = new InMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings()));
        var manager = new RecurringJobManager(storage);
        HangfireConfiguration.AddOrUpdateCensusJob(manager, settings);
        using var connection = storage.GetConnection();
        var job = connection.GetRecurringJobs().ShouldHaveSingleItem();
        job.Id.ShouldBe("antiphon:zombie-census");
        job.Cron.ShouldBe("30 9 * * *");
        job.TimeZoneId.ShouldBe("Europe/London");

        await Task.CompletedTask;
    }

    [Test]
    public async Task AddOrUpdateCensusJob_works_from_a_DI_resolved_manager_without_priming_JobStorage_Current()
    {
        // Regression: Program.cs resolves IRecurringJobManager from DI (mirrored here via a plain
        // ServiceCollection, not the static RecurringJob API) - AddHangfire's UseInMemoryStorage
        // does not synchronously set the JobStorage.Current global, so the static call crashed
        // every real server startup with "Current JobStorage instance has not been initialized
        // yet" even though this class's other test above passed by manually priming that global.
        var services = new ServiceCollection();
        services.AddHangfire(config =>
            config.UseInMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings())));
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IRecurringJobManager>();
        var settings = new ZombieCensusSettings();

        HangfireConfiguration.AddOrUpdateCensusJob(manager, settings);

        var storage = provider.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        connection.GetRecurringJobs().ShouldHaveSingleItem().Id.ShouldBe("antiphon:zombie-census");
    }

    [Test]
    public async Task Recurring_residue_job_is_re_added_on_a_fresh_in_memory_host_with_London_daily_cron()
    {
        var settings = new WorktreeResidueSettings();
        var storage = new InMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings()));
        var manager = new RecurringJobManager(storage);
        HangfireConfiguration.AddOrUpdateWorktreeResidueJob(manager, settings);
        using var connection = storage.GetConnection();
        var job = connection.GetRecurringJobs().ShouldHaveSingleItem();
        job.Id.ShouldBe("antiphon:worktree-residue");
        job.Cron.ShouldBe("0 10 * * *");
        job.TimeZoneId.ShouldBe("Europe/London");

        await Task.CompletedTask;
    }

    [Test]
    public async Task AddOrUpdateWorktreeResidueJob_works_from_a_DI_resolved_manager_without_priming_JobStorage_Current()
    {
        var services = new ServiceCollection();
        services.AddHangfire(config =>
            config.UseInMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings())));
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IRecurringJobManager>();
        var settings = new WorktreeResidueSettings();

        HangfireConfiguration.AddOrUpdateWorktreeResidueJob(manager, settings);

        var storage = provider.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        connection.GetRecurringJobs().ShouldHaveSingleItem().Id.ShouldBe("antiphon:worktree-residue");
    }

    [Test]
    public async Task Loopback_request_reaches_the_Hangfire_dashboard()
    {
        var context = await _factory.Server.SendAsync(ctx =>
        {
            ctx.Request.Method = "GET";
            ctx.Request.Path = "/hangfire";
            ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
            ctx.Connection.LocalIpAddress = IPAddress.Loopback;
        });
        context.Response.StatusCode.ShouldBeOneOf(200, 302);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Non_local_request_is_rejected_by_the_built_in_local_only_filter()
    {
        var context = await _factory.Server.SendAsync(ctx =>
        {
            ctx.Request.Method = "GET";
            ctx.Request.Path = "/hangfire";
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8");
            ctx.Connection.LocalIpAddress = IPAddress.Parse("10.0.0.5");
        });

        context.Response.StatusCode.ShouldBeOneOf(401, 403);
        await Task.CompletedTask;
    }
}
