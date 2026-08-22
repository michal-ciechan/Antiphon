using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

[Category("Integration")]
[NotInParallel("AgentTuiProfileImporterBackfill")]
[ClassDataSource<TestDbFixture>(Shared = SharedType.PerTestSession)]
public class AgentTuiProfileImporterBackfillTests
{
    private IsolatedTestSchema? _isolatedSchema;
    private AppDbContext DbContext { get; set; } = null!;

    public AgentTuiProfileImporterBackfillTests(TestDbFixture fixture)
    {
    }

    [Before(Test)]
    public async Task CreateIsolatedSchemaAsync()
    {
        _isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        DbContext = new AppDbContext(
            TestDbFixture.CreateDbContextOptions(_isolatedSchema.ConnectionString));
    }

    [After(Test)]
    public async Task DisposeIsolatedSchemaAsync()
    {
        if (DbContext is not null)
        {
            await DbContext.DisposeAsync();
            DbContext = null!;
        }

        if (_isolatedSchema is not null)
        {
            await _isolatedSchema.DisposeAsync();
            _isolatedSchema = null;
        }
    }

    [Test]
    public async Task Backfill_skips_pool_delegates_and_syncs_Kind_on_standing_null_profile_agents()
    {
        var now = DateTime.UtcNow;
        var pool = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"pool-{Guid.NewGuid():N}"[..20],
            Slug = $"pool-{Guid.NewGuid():N}"[..20],
            WorkingDirectory = Path.GetTempPath(),
            Details = string.Empty,
            Kind = AgentKind.Grok,
            TuiProfileId = null,
            IsPoolDelegate = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var standing = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"standing-{Guid.NewGuid():N}"[..24],
            Slug = $"standing-{Guid.NewGuid():N}"[..24],
            WorkingDirectory = Path.GetTempPath(),
            Details = string.Empty,
            Kind = AgentKind.Codex,
            TuiProfileId = null,
            IsPoolDelegate = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        DbContext.Agents.AddRange(pool, standing);
        await DbContext.SaveChangesAsync();

        var importer = new AgentTuiProfileImporter(
            DbContext,
            Options.Create(new AgentRegistrySettings
            {
                DefaultDefinition = "claude-main",
                Definitions =
                {
                    ["claude-main"] = new AgentDefinition
                    {
                        Kind = "ClaudeCode",
                        Exe = "synthetic-claude-wrapper",
                        ArgsTemplate = ["--first"],
                    }
                }
            }),
            new NoOpSecretProtector(),
            new AgentTuiRunnerCatalog(),
            TimeProvider.System);

        var result = await importer.ImportAsync(CancellationToken.None);

        result.ProfilesCreated.ShouldBe(1);
        result.AgentsAssigned.ShouldBe(1);

        var defaultProfile = await DbContext.AgentTuiProfiles.AsNoTracking()
            .SingleAsync(profile => profile.IsDefault);
        defaultProfile.Kind.ShouldBe(AgentKind.ClaudeCode);

        DbContext.ChangeTracker.Clear();
        var survivingPool = await DbContext.Agents.AsNoTracking().SingleAsync(agent => agent.Id == pool.Id);
        survivingPool.Kind.ShouldBe(AgentKind.Grok);
        survivingPool.TuiProfileId.ShouldBeNull();
        survivingPool.IsPoolDelegate.ShouldBeTrue();

        var backfilled = await DbContext.Agents.AsNoTracking().SingleAsync(agent => agent.Id == standing.Id);
        backfilled.TuiProfileId.ShouldBe(defaultProfile.Id);
        backfilled.Kind.ShouldBe(AgentKind.ClaudeCode);
        backfilled.IsPoolDelegate.ShouldBeFalse();
    }

    private sealed class NoOpSecretProtector : IAgentTuiSecretProtector
    {
        public string Protect(Guid profileId, string environmentName, string plaintext) => plaintext;

        public string Unprotect(Guid profileId, string environmentName, string protectedValue) => protectedValue;
    }
}
