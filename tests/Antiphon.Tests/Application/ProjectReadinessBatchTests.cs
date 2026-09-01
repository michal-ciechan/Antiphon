using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0217 S6 — cached, bounded readiness batches never fan out git processes.</summary>
[Category("GitIntegration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class ProjectReadinessBatchTests
{
    [Test]
    public async Task batch_of_84_projects_returns_every_row_then_reuses_the_ttl_cache()
    {
        await SkipIfGitUnavailableAsync();
        using var repo = new ScratchGitRepo("antiphon-readiness-batch");
        await repo.CommitFileAsync("readme.md", "ready");
        using var provider = BuildProvider(out var gate);
        var ids = await SeedProjectsAsync(provider, repo.Path, 84);
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ProjectSetupService>();
            var first = await service.GetReadinessBatchAsync(ids, CancellationToken.None);

            first.Count.ShouldBe(84);
            first.Select(row => row.ProjectId).ShouldBe(ids);
            var started = gate.Started;
            started.ShouldBeGreaterThan(0);
            gate.PeakInFlight.ShouldBeLessThanOrEqualTo(4);

            var second = await service.GetReadinessBatchAsync(ids, CancellationToken.None);
            second.Count.ShouldBe(84);
            gate.Started.ShouldBe(started, "the second batch is served entirely from the per-project TTL cache");
        }
        finally
        {
            await DeleteProjectsAsync(provider, ids);
        }
    }

    [Test]
    public async Task a_bad_project_is_an_error_row_without_failing_its_batch()
    {
        using var provider = BuildProvider(out _);
        var goodId = (await SeedProjectsAsync(provider, localPath: null, count: 1)).Single();
        var missingId = Guid.NewGuid();
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ProjectSetupService>();
            var rows = await service.GetReadinessBatchAsync([goodId, missingId], CancellationToken.None);

            rows.Count.ShouldBe(2);
            rows[0].ProjectId.ShouldBe(goodId);
            rows[1].ProjectId.ShouldBe(missingId);
            rows[1].CanDispatch.ShouldBeFalse();
            rows[1].Checks.Single().Summary.ShouldContain("could not be checked", Case.Insensitive);
        }
        finally
        {
            await DeleteProjectsAsync(provider, [goodId]);
        }
    }

    private static ServiceProvider BuildProvider(out GitProcessGate gate)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        services.Configure<DelegationSettings>(_ => { });
        services.Configure<ProjectsSettings>(settings => settings.ReadinessCacheSeconds = 60);
        services.AddMemoryCache();
        gate = new GitProcessGate(8);
        services.AddSingleton(gate);
        services.AddGitWorkspaceService();
        services.AddSingleton<ProjectReadinessCache>();
        services.AddScoped<DelegationWorkspaceResolver>();
        services.AddScoped<ProjectSetupService>();
        return services.BuildServiceProvider();
    }

    private static async Task<List<Guid>> SeedProjectsAsync(ServiceProvider provider, string? localPath, int count)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var projects = Enumerable.Range(0, count).Select(index => new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Readiness batch {Guid.NewGuid():N}-{index}",
            GitRepositoryUrl = string.Empty,
            LocalRepositoryPath = localPath,
            BaseBranch = "master",
            CreatedAt = now,
            UpdatedAt = now,
        }).ToList();
        db.Projects.AddRange(projects);
        await db.SaveChangesAsync();
        return projects.Select(project => project.Id).ToList();
    }

    private static async Task DeleteProjectsAsync(ServiceProvider provider, IReadOnlyList<Guid> ids)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Projects.Where(project => ids.Contains(project.Id)).ExecuteDeleteAsync();
    }

    private static async Task SkipIfGitUnavailableAsync()
    {
        var probe = await ScratchGitRepo.GitInAsync(Environment.CurrentDirectory, "--version");
        if (!probe.Ok)
            throw new SkipTestException("git is required for readiness batch integration tests");
    }
}
