using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.ApiKeys;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0166 S2: token_key resolves project-then-global; env-var fallback stays byte-compatible.</summary>
[Category("Integration")]
public class TrackerTokenResolverTests
{
    [Test]
    public async Task token_key_resolves_project_key_over_global()
    {
        await using var db = NewDb();
        var project = await AddProjectAsync(db);
        var name = "github-antiphon-sync";
        await AddKeyAsync(db, projectId: null, value: "sk-global", name: name);
        await AddKeyAsync(db, projectId: project.Id, value: "sk-project", name: name);
        var sut = NewResolver(db);
        var config = NewConfig() with { TokenKeyName = name };

        var resolved = await sut.ResolveAsync(config, project.Id, CancellationToken.None);

        resolved.ShouldNotBeNull();
        resolved!.ResolvedToken.ShouldBe("sk-project");
    }

    [Test]
    public async Task token_key_falls_back_to_global_when_project_has_none()
    {
        await using var db = NewDb();
        var project = await AddProjectAsync(db);
        var name = await AddKeyAsync(db, projectId: null, value: "sk-global");
        var sut = NewResolver(db);

        var resolved = await sut.ResolveAsync(
            NewConfig() with { TokenKeyName = name },
            project.Id,
            CancellationToken.None);

        resolved.ShouldNotBeNull();
        resolved!.ResolvedToken.ShouldBe("sk-global");
    }

    [Test]
    public async Task missing_token_key_returns_null_without_throwing()
    {
        await using var db = NewDb();
        var sut = NewResolver(db);

        var resolved = await sut.ResolveAsync(
            NewConfig() with { TokenKeyName = "does-not-exist" },
            projectId: null,
            CancellationToken.None);

        resolved.ShouldBeNull();
    }

    [Test]
    public async Task api_key_env_fallback_reads_environment_variable()
    {
        await using var db = NewDb();
        var envName = $"ANTIPHON_TEST_GH_TOKEN_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(envName, "env-token-value");
        try
        {
            var sut = NewResolver(db);
            var resolved = await sut.ResolveAsync(
                NewConfig() with { ApiKeyEnv = envName },
                projectId: null,
                CancellationToken.None);

            resolved.ShouldNotBeNull();
            resolved!.ResolvedToken.ShouldBe("env-token-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Test]
    public async Task no_token_key_and_no_env_resolves_unauthenticated()
    {
        await using var db = NewDb();
        var sut = NewResolver(db);

        var resolved = await sut.ResolveAsync(NewConfig(), projectId: null, CancellationToken.None);

        resolved.ShouldNotBeNull();
        resolved!.ResolvedToken.ShouldBeNull();
    }

    [Test]
    public void parser_reads_token_key_into_TokenKeyName()
    {
        var board = new Board
        {
            Id = Guid.NewGuid(),
            TrackerKind = TrackerKind.GitHubIssues,
            WorkflowDefinitions =
            [
                new BoardWorkflowDefinition
                {
                    Id = Guid.NewGuid(),
                    IsActive = true,
                    Version = 1,
                    Name = "wf",
                    Content = """
                        ---
                        tracker:
                          kind: github
                          repository: acme/app
                          token_key: github-antiphon-sync
                        ---
                        Work.
                        """,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            ]
        };

        IssueTrackerConfigParser.TryParse(board, out var config, out var error).ShouldBeTrue(error);
        config!.TokenKeyName.ShouldBe("github-antiphon-sync");
    }

    private static AppDbContext NewDb() => new(TestDbFixture.CreateDbContextOptions());

    private static TrackerTokenResolver NewResolver(AppDbContext db) =>
        new(db, new ApiKeyStoreTests.FakeApiKeyProtector(), NullLogger<TrackerTokenResolver>.Instance);

    private static IssueTrackerConfig NewConfig() =>
        new(
            TrackerKind.GitHubIssues,
            BaseUrl: "https://api.github.com",
            ProjectKey: null,
            Repository: "acme/app",
            ActiveStates: ["open"],
            ApiKeyEnv: null,
            Jql: null,
            Options: new Dictionary<string, string>());

    private static async Task<string> AddKeyAsync(
        AppDbContext db,
        Guid? projectId,
        string value,
        string? name = null)
    {
        var keyName = name ?? $"tok-{Guid.NewGuid():N}";
        var id = Guid.NewGuid();
        db.ApiKeys.Add(new ApiKey
        {
            Id = id,
            Name = keyName,
            ProjectId = projectId,
            Ciphertext = new ApiKeyStoreTests.FakeApiKeyProtector().Protect(id, value),
            ProtectionVersion = "v1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return keyName;
    }

    private static async Task<Project> AddProjectAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Token Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }
}
