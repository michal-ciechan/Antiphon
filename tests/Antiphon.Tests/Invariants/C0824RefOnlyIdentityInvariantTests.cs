using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Application;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
public sealed class C0824RefOnlyIdentityInvariantTests
{
    [Test]
    public async Task Ref_only_model_and_migration_are_declared()
    {
        await using (var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                         .UseNpgsql("Host=localhost;Database=metadata_only").Options))
        {
            var task = model.Model.FindEntityType(typeof(AgentTask))!;
            task.FindProperty(nameof(AgentTask.DesktopWorkspaceKind)).ShouldNotBeNull("persisted-mode=RefOnly");
            Convert.ToInt32(task.FindProperty(nameof(AgentTask.DesktopWorkspaceKind))!.GetDefaultValue())
                .ShouldBe((int)DesktopWorkspaceKind.Checkout, "legacy-default=Checkout");
            task.FindProperty(nameof(AgentTask.TaskRefWorkspaceBindingId)).ShouldNotBeNull("persisted-binding=expected");
            var bindingModel = model.Model.FindEntityType(typeof(TaskRefWorkspaceBinding)).ShouldNotBeNull("durable-binding-schema");
            bindingModel.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual([nameof(TaskRefWorkspaceBinding.TaskId)]))
                .ShouldBeTrue("duplicate-task-rejected");
            bindingModel.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name)
                .SequenceEqual([nameof(TaskRefWorkspaceBinding.RepositoryPath), nameof(TaskRefWorkspaceBinding.FullRef)]))
                .ShouldBeTrue("duplicate-ref-rejected");
        }
        var migrations = MigrationTestDatabase.Names();
        var migration = Array.FindIndex(migrations, m => m.EndsWith("_Card0824RefOnlyDesktopCoordinates", StringComparison.Ordinal));
        migration.ShouldBeGreaterThan(0, "generated-upgrade-present");
    }
}
