using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Migrations;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.AgentTui;

/// <summary>
/// CARD-0182 T9 — the D1 backfill writes '--model' into null non-Raw revisions and leaves Raw null.
/// </summary>
[Category("Integration")]
public sealed class AgentTuiModelArgumentMigrationTests
{
    [Test]
    public async Task T9_null_grok_revision_gains_model_flag_raw_stays_null()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(
            TestDbFixture.CreateDbContextOptions(isolatedSchema.ConnectionString));

        var now = DateTime.UtcNow;
        var grok = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = $"gkp-{Guid.NewGuid():N}",
            Kind = AgentKind.Grok,
            IsEnabled = true,
            Source = AgentTuiProfileSource.Operator,
            CreatedAt = now,
            UpdatedAt = now
        };
        var raw = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = $"raw-{Guid.NewGuid():N}",
            Kind = AgentKind.Raw,
            IsEnabled = true,
            Source = AgentTuiProfileSource.Operator,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AgentTuiProfiles.AddRange(grok, raw);
        await db.SaveChangesAsync();

        var grokRevision = NewRevision(grok.Id, now);
        var rawRevision = NewRevision(raw.Id, now);
        db.AgentTuiProfileRevisions.AddRange(grokRevision, rawRevision);
        await db.SaveChangesAsync();

        grokRevision.ModelArgumentName.ShouldBeNull();
        rawRevision.ModelArgumentName.ShouldBeNull();

        await db.Database.ExecuteSqlRawAsync(BackfillNonRawModelArgumentName.BackfillSql);

        await db.Entry(grokRevision).ReloadAsync();
        await db.Entry(rawRevision).ReloadAsync();
        grokRevision.ModelArgumentName.ShouldBe("--model");
        rawRevision.ModelArgumentName.ShouldBeNull();
    }

    private static AgentTuiProfileRevision NewRevision(Guid profileId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        ProfileId = profileId,
        RevisionNumber = 1,
        Executable = "pwsh.exe",
        ArgumentsJson = "[]",
        DiscoveryArgumentsJson = "[]",
        VersionArgumentsJson = "[]",
        AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
        NonSecretEnvironmentJson = "{}",
        SecretEnvironmentNamesJson = "[]",
        ModelArgumentName = null,
        Guidance = "CARD-0182 T9",
        CreatedAt = now
    };
}
