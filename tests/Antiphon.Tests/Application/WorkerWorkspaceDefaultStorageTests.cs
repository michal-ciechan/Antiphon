using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class WorkerWorkspaceDefaultStorageTests
{
    [Test]
    public void C458_DefaultsAreWorktreeAndHistoricalSourceIsNull()
    {
        new DelegationSettings().DefaultWorkerWorkspace.ShouldBe(WorkspaceMode.Worktree);
        new Project().DefaultWorkerWorkspace.ShouldBeNull();
        var task = new AgentTask();
        task.Workspace.ShouldBe(WorkspaceMode.Shared);
        task.WorkspaceSource.ShouldBeNull();
    }

    [Test]
    [Arguments(WorkspaceMode.Shared, true)]
    [Arguments(WorkspaceMode.Worktree, true)]
    [Arguments(WorkspaceMode.ReadOnly, false)]
    [Arguments((WorkspaceMode)99, false)]
    public void C458_GlobalDefaultValidatorAllowsOnlyWritableModes(WorkspaceMode mode, bool valid)
    {
        var result = new DelegationSettingsValidator().Validate(null,
            new DelegationSettings { DefaultWorkerWorkspace = mode });
        result.Succeeded.ShouldBe(valid);
        if (!valid) result.Failures.ShouldContain(f => f.Contains("DefaultWorkerWorkspace"));
    }

    [Test]
    public void C458_EfMappingsStoreNullableTextWithStableWorkspaceOrdinals()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        var project = db.Model.FindEntityType(typeof(Project))!
            .FindProperty(nameof(Project.DefaultWorkerWorkspace))!;
        var source = db.Model.FindEntityType(typeof(AgentTask))!
            .FindProperty(nameof(AgentTask.WorkspaceSource))!;
        project.IsNullable.ShouldBeTrue();
        source.IsNullable.ShouldBeTrue();
        project.GetColumnType().ShouldBe("text");
        source.GetColumnType().ShouldBe("text");
        project.GetValueConverter().ShouldNotBeNull();
        source.GetValueConverter().ShouldNotBeNull();
        project.GetDefaultValue().ShouldBeNull();
        source.GetDefaultValue().ShouldBeNull();
        ((int)WorkspaceMode.Shared).ShouldBe(0);
        ((int)WorkspaceMode.Worktree).ShouldBe(1);
        ((int)WorkspaceMode.ReadOnly).ShouldBe(2);
    }
}
