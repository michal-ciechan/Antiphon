using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

public class AgentTuiPersistenceTests
{
    [Test]
    public void Model_has_profile_revision_secret_model_and_effective_session_contracts()
    {
        using var db = NewModelContext();

        var profile = db.Model.FindEntityType(typeof(AgentTuiProfile));
        profile.ShouldNotBeNull();
        profile.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(AgentTuiProfile.DisplayName)]))
            .IsUnique.ShouldBeTrue();

        var revision = db.Model.FindEntityType(typeof(AgentTuiProfileRevision));
        revision.ShouldNotBeNull();
        revision.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(AgentTuiProfileRevision.ProfileId), nameof(AgentTuiProfileRevision.RevisionNumber)]))
            .IsUnique.ShouldBeTrue();

        var secret = db.Model.FindEntityType(typeof(AgentTuiSecret));
        secret.ShouldNotBeNull();
        secret.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(AgentTuiSecret.ProfileId), nameof(AgentTuiSecret.Name)]))
            .IsUnique.ShouldBeTrue();

        var model = db.Model.FindEntityType(typeof(AgentTuiModel));
        model.ShouldNotBeNull();
        model.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(AgentTuiModel.ProfileId), nameof(AgentTuiModel.Identifier)]))
            .IsUnique.ShouldBeTrue();

        typeof(Agent).GetProperty(nameof(Agent.TuiProfileId)).ShouldNotBeNull();
        typeof(Agent).GetProperty(nameof(Agent.ModelId)).ShouldNotBeNull();
        typeof(AgentSession).GetProperty(nameof(AgentSession.TuiProfileRevisionId)).ShouldNotBeNull();
        typeof(AgentSession).GetProperty(nameof(AgentSession.EffectiveModelId)).ShouldNotBeNull();
        AgentKind.OpenCode.ShouldBe((AgentKind)3);
    }

    private static AppDbContext NewModelContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=agent_tui_model;Username=unused;Password=unused")
            .Options;

        return new AppDbContext(options);
    }
}
