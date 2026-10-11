using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1009. Task chips read the session's persisted EffectiveModelId from the summary.
/// The current tier alias stays a client fallback when this value is null.
/// </summary>
[Category("Integration")]
public class AgentTaskEffectiveModelChipTests
{
    [Test]
    public async Task list_and_detail_carry_the_session_effective_model_id()
    {
        var root = Guid.NewGuid();
        Guid highId, mediumId, currentId, unrecordedId, unlinkedId;
        await using (var seed = CreateContext())
        {
            var high = Session("gpt-6-sol");
            var medium = Session("gpt-5.6-terra");
            var current = Session("gpt-6.1-sol");
            var unrecorded = Session(null);
            seed.AgentSessions.AddRange(high, medium, current, unrecorded);

            var highTask = TaskRow(root, AgentModelLevel.High, high.Id);
            var mediumTask = TaskRow(root, AgentModelLevel.Medium, medium.Id);
            var currentTask = TaskRow(root, AgentModelLevel.High, current.Id);
            var unrecordedTask = TaskRow(root, AgentModelLevel.High, unrecorded.Id);
            var unlinked = TaskRow(root, AgentModelLevel.Medium, null);
            seed.AgentTasks.AddRange(highTask, mediumTask, currentTask, unrecordedTask, unlinked);
            await seed.SaveChangesAsync();

            highId = highTask.Id;
            mediumId = mediumTask.Id;
            currentId = currentTask.Id;
            unrecordedId = unrecordedTask.Id;
            unlinkedId = unlinked.Id;
        }

        await using var db = CreateContext();
        var service = CreateService(db);
        var rows = await service.ListAsync(
            root, statuses: null, includeChecks: false, since: null, CancellationToken.None);

        rows.Items.Single(r => r.Id == highId).EffectiveModelId.ShouldBe("gpt-6-sol");
        rows.Items.Single(r => r.Id == mediumId).EffectiveModelId.ShouldBe("gpt-5.6-terra");
        rows.Items.Single(r => r.Id == currentId).EffectiveModelId.ShouldBe("gpt-6.1-sol");
        rows.Items.Single(r => r.Id == unrecordedId).EffectiveModelId.ShouldBeNull();
        rows.Items.Single(r => r.Id == unlinkedId).EffectiveModelId.ShouldBeNull();

        var detail = await service.GetAsync(mediumId, CancellationToken.None);
        detail.Summary.EffectiveModelId.ShouldBe("gpt-5.6-terra");
        var missing = await service.GetAsync(unlinkedId, CancellationToken.None);
        missing.Summary.EffectiveModelId.ShouldBeNull();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static AgentTaskService CreateService(AppDbContext db) =>
        new(db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings()),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);

    private static AgentSession Session(string? effectiveModelId)
    {
        var now = DateTime.UtcNow;
        return new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "codex",
            AgentKind = AgentKind.Codex,
            Status = SessionStatus.Stopped,
            Cwd = @"C:\tmp\card-1009",
            Cols = 80,
            Rows = 24,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            EffectiveModelId = effectiveModelId,
        };
    }

    private static AgentTask TaskRow(Guid root, AgentModelLevel level, Guid? sessionId)
    {
        var id = Guid.NewGuid();
        return new AgentTask
        {
            Id = id,
            RootTaskId = root,
            Title = $"card-1009 {level} {id:N}",
            Goal = "card-1009",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.Codex,
            ModelLevel = level,
            Status = AgentTaskStatus.Succeeded,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = @"C:\tmp\card-1009",
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            AgentSessionId = sessionId,
        };
    }
}
