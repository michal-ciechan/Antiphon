using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public sealed class WorktreeResidueRegistrationTests
{
    private readonly AntiphonWebAppFactory _factory;
    public WorktreeResidueRegistrationTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task C459_OneScheduler()
    {
        var janitorHostedServices = _factory.Services.GetServices<IHostedService>()
            .Where(s => s is WorktreeJanitorHostedService)
            .ToList();
        janitorHostedServices.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_DisabledSchedulerStaysOff()
    {
        var storage = _factory.Services.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        var residueRecurringJobs = connection.GetRecurringJobs()
            .Where(j => j.Id.Contains("worktree-residue", StringComparison.OrdinalIgnoreCase))
            .ToList();
        residueRecurringJobs.ShouldBeEmpty();
        await Task.CompletedTask;
    }
}
