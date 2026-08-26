using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0032 S2 — git URL is optional when a local path is given.</summary>
[Category("Integration")]
public class ProjectServiceTests
{
    [Test]
    public async Task git_url_is_optional_when_local_repository_path_is_set()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            new CreateProjectRequest(
                $"Local {Guid.NewGuid():N}",
                GitRepositoryUrl: "",
                ConstitutionPath: null,
                GitHubIntegrationEnabled: false,
                NotificationsEnabled: false,
                LocalRepositoryPath: @"D:\src\local-only",
                BaseBranch: "master"),
            CancellationToken.None);

        created.GitRepositoryUrl.ShouldBe("");
        created.LocalRepositoryPath.ShouldBe(@"D:\src\local-only");
    }

    [Test]
    public async Task git_url_is_still_required_without_a_local_path()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var ex = await Should.ThrowAsync<ValidationException>(() =>
            service.CreateAsync(
                new CreateProjectRequest(
                    $"Remote {Guid.NewGuid():N}",
                    GitRepositoryUrl: "",
                    ConstitutionPath: null,
                    GitHubIntegrationEnabled: false,
                    NotificationsEnabled: false,
                    LocalRepositoryPath: null,
                    BaseBranch: "master"),
                CancellationToken.None));
        ex.Errors.ShouldContainKey("gitRepositoryUrl");
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static ProjectService CreateService(Antiphon.Server.Infrastructure.Data.AppDbContext db) =>
        new(
            db,
            new StubHttpClientFactory(),
            Options.Create(new GithubSettings()),
            NullLogger<ProjectService>.Instance);
}
