using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-0822 R-8. The state table, the session column, and a closed model snapshot.</summary>
[Category("Unit")]
public sealed class OrchestratorInstructionsMigrationShapeTests
{
    [Test]
    public void State_table_and_session_column_match_the_snapshot()
    {
        using var db = NewContext();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
        db.Database.GetMigrations()
            .Any(id => id.EndsWith("_AddOrchestratorInstructions", StringComparison.Ordinal))
            .ShouldBeTrue();

        var state = db.Model.FindEntityType(typeof(OrchestratorInstructionsState));
        state.ShouldNotBeNull();
        state.GetTableName().ShouldBe("OrchestratorInstructionsStates");
        state.FindProperty(nameof(OrchestratorInstructionsState.Version))!.GetMaxLength().ShouldBe(16);
        state.FindProperty(nameof(OrchestratorInstructionsState.Body))!.GetMaxLength().ShouldBeNull();
        state.FindProperty(nameof(OrchestratorInstructionsState.SnapshotJson))!.GetMaxLength().ShouldBeNull();
        state.FindProperty(nameof(OrchestratorInstructionsState.LastReason))!.GetMaxLength().ShouldBe(200);
        state.FindProperty(nameof(OrchestratorInstructionsState.WrittenPath))!.GetMaxLength().ShouldBe(1000);
        state.FindProperty(nameof(OrchestratorInstructionsState.LastWriteError))!.GetMaxLength().ShouldBe(2000);

        var column = db.Model.FindEntityType(typeof(AgentSession))!
            .FindProperty(nameof(AgentSession.OrchestratorInstructionsVersion))!;
        column.IsNullable.ShouldBeTrue();
        column.GetMaxLength().ShouldBe(16);
        column.GetColumnType().ShouldBe("character varying(16)");
    }

    private static AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_guard;Username=unused;Password=unused")
            .Options);
}
