using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TaskParkPublicationTests
{
    [Test]
    public Task C1065_WorkspaceModesPreservePublicationAuthority()
    {
        var task = new AgentTask { Id = Guid.NewGuid(), Workspace = WorkspaceMode.Shared,
            Role = AgentTaskRole.Code, Goal = "Inspect landed source", SourceLandingOperationId = Guid.NewGuid() };
        DelegationReportFormatter.BuildBrief(task, new DelegationSettings())
            .ShouldNotContain(DelegationReportFormatter.SharedWriteCommitLine,
                "G-73: SourceLanding custody overrides generic writable instructions");
        return Task.CompletedTask;
    }

    [Test]
    public async Task C1065_CommitInstructionsAndRefusalsRespectOverrides()
    {
        var task = new AgentTask { Id = Guid.NewGuid(), Workspace = WorkspaceMode.Worktree,
            Role = AgentTaskRole.Code, Goal = "Implement the assigned change" };
        var brief = DelegationReportFormatter.BuildBrief(task, new DelegationSettings());
        brief.ShouldContain("Before reporting blocked", "G-76: ordinary writable brief must require publication before block");
        brief.ShouldContain("truthful WIP commit", "G-76");
    }

    [Test]
    public async Task C1065_PublicationReceiptCannotAuthorizeChangedAttempt()
    {
        await using var f = await BlockedTaskParkFixture.CreateAsync();
        f.Options.Enabled = true;
        var id = (await f.RegisterAsync()).ShouldNotBeNull();
        await using (var db = f.Db())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
            task.Result = "A different report after the captured block";
            await db.SaveChangesAsync();
        }
        (await f.AdvanceAsync(id, AgentTaskParkState.Published)).ShouldBeFalse(
            "G-92: changed report cannot authorize the captured episode");
    }
}
