using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentControl")]
public sealed class CardSpawnModelArgumentTests
{
    [Test]
    public async Task Assigned_card_spawn_with_a_blank_model_uses_the_agents_claude_tier()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.ClaudeCode, modelArgumentName: "--model");
            var card = await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.ClaudeCode,
                AgentModelLevel.High, modelId: null);

            var directAgent = await db.Agents.SingleAsync(a => a.Id == card.AssignedAgentId);
            var direct = await AgentLaunchResolution.ResolveForAgentAsync(
                directAgent,
                harness.Scope.ServiceProvider.GetRequiredService<AgentRegistry>(),
                harness.Scope.ServiceProvider.GetRequiredService<AgentTuiLaunchResolver>(),
                new AgentLaunchOptions(),
                CancellationToken.None);
            ModelPair(direct.Spec.Args).ShouldBe(["--model", "opus"]);
            await SpawnOnlyCardAsync(db, harness);

            adapter.Started.ShouldBeTrue();
            ModelPair(adapter.StartedArgs).ShouldBe(["--model", "opus"]);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Card_spawn_matches_a_cardless_start_for_the_same_tier()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var cardlessAdapter = new FakeAgentProtocolAdapter();
            var cardAdapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [cardlessAdapter, cardAdapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.ClaudeCode, modelArgumentName: "--model");
            var cardless = await SeedAgentAsync(db, tempRoot, profile.Id, AgentKind.ClaudeCode,
                AgentModelLevel.Medium, modelId: null);

            await harness.Control.StartAsync(cardless.Id, new StartAgentRequest(Fresh: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.ClaudeCode,
                AgentModelLevel.Medium, modelId: null);
            await SpawnOnlyCardAsync(db, harness);

            ModelPair(cardlessAdapter.StartedArgs).ShouldBe(ModelPair(cardAdapter.StartedArgs));
            ModelPair(cardAdapter.StartedArgs).ShouldBe(["--model", "sonnet"]);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Blank_model_argument_profile_suppresses_a_derived_tier_with_profile_owned_provenance()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.Raw, modelArgumentName: null);
            var card = await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.Raw,
                AgentModelLevel.High, modelId: null);

            ClearHarnessTracking(harness);
            await harness.CardService.SpawnAsync(card.Id, new SpawnCardRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            adapter.StartedArgs.ShouldNotContain("--model");
            adapter.StartedArgs.ShouldNotContain("opus");
            var agent = await db.Agents.SingleAsync(a => a.TuiProfileId == profile.Id);
            var resolved = await AgentLaunchResolution.ResolveForAgentAsync(
                agent,
                harness.Scope.ServiceProvider.GetRequiredService<AgentRegistry>(),
                harness.Scope.ServiceProvider.GetRequiredService<AgentTuiLaunchResolver>(),
                new AgentLaunchOptions(),
                CancellationToken.None);
            resolved.ModelArgument.ShouldBe(LaunchModelArgument.ProfileOwned);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Exact_model_on_a_card_spawn_wins_once_over_the_derived_tier()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            var profile = await SeedProfileAsync(db, AgentKind.ClaudeCode, modelArgumentName: "--model", models: ["exact-model"]);
            await SeedAssignedCardAsync(db, harness, tempRoot, profile.Id, AgentKind.ClaudeCode,
                AgentModelLevel.High, modelId: "exact-model");

            await SpawnOnlyCardAsync(db, harness);

            adapter.StartedArgs.Count(a => a == "--model").ShouldBe(1);
            ModelPair(adapter.StartedArgs).ShouldBe(["--model", "exact-model"]);
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Unassigned_card_spawn_offers_no_synthetic_default_tier()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            await using var db = NewDb(schema.ConnectionString);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "Raw", includeLaunchResolver: true,
                connectionString: schema.ConnectionString);
            await SeedProfileAsync(db, AgentKind.Raw, modelArgumentName: "--model", isDefault: true);
            var card = await SeedUnassignedCardAsync(db, harness, tempRoot);

            ClearHarnessTracking(harness);
            await harness.CardService.SpawnAsync(card.Id, new SpawnCardRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            adapter.StartedArgs.ShouldNotContain("--model");
        }
        finally
        {
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static async Task<Card> SeedAssignedCardAsync(AppDbContext db,
        AgentControlServiceIntegrationTests.Harness harness, string tempRoot, Guid profileId,
        AgentKind kind, AgentModelLevel level, string? modelId)
    {
        var card = await SeedUnassignedCardAsync(db, harness, tempRoot);
        var agent = await SeedAgentAsync(db, tempRoot, profileId, kind, level, modelId);
        card.AssignedAgentId = agent.Id;
        await db.SaveChangesAsync();
        return card;
    }

    private static async Task<Agent> SeedAgentAsync(AppDbContext db, string tempRoot, Guid profileId,
        AgentKind kind, AgentModelLevel level, string? modelId)
    {
        var now = DateTime.UtcNow;
        var workspace = Path.Combine(tempRoot, $"agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var agent = new Agent
        {
            Id = Guid.NewGuid(), Name = "Card spawn agent", Slug = $"card-spawn-{Guid.NewGuid():N}"[..30],
            WorkingDirectory = workspace, Details = string.Empty, Status = AgentStatus.Idle, Kind = kind,
            ModelLevel = level, ModelId = modelId, TuiProfileId = profileId, CreatedAt = now, UpdatedAt = now
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent;
    }

    private static async Task<Card> SeedUnassignedCardAsync(AppDbContext db,
        AgentControlServiceIntegrationTests.Harness harness, string tempRoot)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"Card spawn {Guid.NewGuid():N}", GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(tempRoot, $"repo-{Guid.NewGuid():N}"), BaseBranch = "main",
            CreatedAt = now, UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var board = await harness.BoardService.CreateAsync(new CreateBoardRequest(project.Id, "Card spawn board"), CancellationToken.None);
        var card = await harness.CardService.CreateAsync(board.Id, new CreateCardRequest(null, "Spawn it"), CancellationToken.None);
        return await db.Cards.SingleAsync(c => c.Id == card.Id);
    }

    private static async Task SpawnOnlyCardAsync(AppDbContext db, AgentControlServiceIntegrationTests.Harness harness)
    {
        var card = await db.Cards.Where(c => c.AssignedAgentId != null).OrderByDescending(c => c.CreatedAt).FirstAsync();
        ClearHarnessTracking(harness);
        await harness.CardService.SpawnAsync(card.Id, new SpawnCardRequest(), CancellationToken.None);
        await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
    }

    private static void ClearHarnessTracking(AgentControlServiceIntegrationTests.Harness harness) =>
        harness.Scope.ServiceProvider.GetRequiredService<AppDbContext>().ChangeTracker.Clear();

    private static string[] ModelPair(IReadOnlyList<string> args)
    {
        var index = args.ToList().IndexOf("--model");
        index.ShouldBeGreaterThanOrEqualTo(0);
        return [args[index], args[index + 1]];
    }

    private static AppDbContext NewDb(string connectionString) => new(TestDbFixture.CreateDbContextOptions(connectionString));

    private static async Task<AgentTuiProfile> SeedProfileAsync(AppDbContext db, AgentKind kind,
        string? modelArgumentName, bool isDefault = false, IReadOnlyList<string>? models = null)
    {
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(), DisplayName = $"Profile {Guid.NewGuid():N}", Kind = kind, IsEnabled = true,
            IsDefault = isDefault, Source = AgentTuiProfileSource.Operator, CreatedAt = now, UpdatedAt = now
        };
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();
        var revision = new AgentTuiProfileRevision
        {
            Id = Guid.NewGuid(), ProfileId = profile.Id, RevisionNumber = 1,
            Executable = Path.Combine(Environment.SystemDirectory, "cmd.exe"), ArgumentsJson = "[]",
            DiscoveryArgumentsJson = "[]", VersionArgumentsJson = "[]", AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
            NonSecretEnvironmentJson = "{}", SecretEnvironmentNamesJson = "[]", ModelArgumentName = modelArgumentName,
            Guidance = "CARD-0193", CreatedAt = now
        };
        db.AgentTuiProfileRevisions.Add(revision);
        profile.ActiveRevisionId = revision.Id;
        foreach (var model in models ?? [])
            db.AgentTuiModels.Add(new AgentTuiModel { Id = Guid.NewGuid(), ProfileId = profile.Id, Identifier = model,
                DisplayName = model, Source = AgentTuiModelSource.Operator, Availability = AgentTuiModelAvailability.Verified,
                CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        return profile;
    }
}
