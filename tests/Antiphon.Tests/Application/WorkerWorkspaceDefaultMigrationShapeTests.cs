using Antiphon.Server.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class WorkerWorkspaceDefaultMigrationShapeTests
{
    [Test]
    public void C458_UpAddsOnlyNullableWorkspaceColumns()
    {
        var operations = new Card0458WorkerWorkspaceDefault().UpOperations;
        operations.Count.ShouldBe(2);
        var added = operations.Cast<AddColumnOperation>().ToDictionary(x => x.Table + "." + x.Name);
        added.Keys.Order().ShouldBe(new[] { "AgentTasks.WorkspaceSource", "Projects.DefaultWorkerWorkspace" });
        foreach (var column in added.Values)
        {
            column.IsNullable.ShouldBeTrue();
            column.ColumnType.ShouldBe("text");
            column.DefaultValue.ShouldBeNull();
            column.DefaultValueSql.ShouldBeNull();
        }
    }

    [Test]
    public void C458_DownDropsOnlyTheAddedColumns()
    {
        var operations = new Card0458WorkerWorkspaceDefault().DownOperations;
        operations.Count.ShouldBe(2);
        operations.Cast<DropColumnOperation>()
            .Select(x => x.Table + "." + x.Name).Order()
            .ShouldBe(new[] { "AgentTasks.WorkspaceSource", "Projects.DefaultWorkerWorkspace" });
    }
}
