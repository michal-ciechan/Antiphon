using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;
using Antiphon.Tests.Application;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class Card1619ExitPersistenceInvariantTests
{
    [Test]
    public void Exit_evidence_migration_and_nullable_metadata_are_declared()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        var migrations = MigrationTestDatabase.Names();
        var added = migrations.SingleOrDefault(m => m.EndsWith("_AddSessionExitSignalEvidence", StringComparison.Ordinal));
        added.ShouldNotBeNull("the additive nullable migration must exist");
        foreach (var entity in new[] { typeof(AgentSession), typeof(RunAttempt), typeof(AgentIncident) })
            db.Model.FindEntityType(entity)!.FindProperty("ExitSignal")!.IsNullable.ShouldBeTrue();
        db.Model.FindEntityType(typeof(AgentSession))!.FindProperty("ExitStopIntentJson")!.IsNullable.ShouldBeTrue();
    }
}
