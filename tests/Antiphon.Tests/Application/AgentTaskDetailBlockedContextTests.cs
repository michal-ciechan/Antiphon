using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0033 S1: task detail carries the isolated question, kind, rounds and degraded progress.</summary>
[Category("Integration")]
public class AgentTaskDetailBlockedContextTests
{
    [Test]
    public async Task a_question_blocked_task_isolates_the_trailing_question()
    {
        using var workspace = new TempWorkspace();
        var sessionId = Guid.NewGuid();
        var task = await SeedAsync(
            workspace.Path,
            AgentTaskStatus.Blocked,
            sessionId: sessionId,
            result: "Added Fizz(int).\n\nBuzz throws on negatives — should Fizz match that?");
        await AddEventAsync(task.Id, AgentTaskEventType.Blocked, "Delegate asked: Buzz throws on negatives — should Fizz match that?");

        await using var db = CreateContext();
        var detail = await CreateService(db).GetAsync(task.Id, CancellationToken.None);

        var blocked = detail.Blocked.ShouldNotBeNull();
        blocked.Kind.ShouldBe(BlockedKind.Question);
        blocked.Round.ShouldBe(1);
        blocked.Question.ShouldBe("Buzz throws on negatives — should Fizz match that?");
        blocked.Context.ShouldBe("Added Fizz(int).");
        blocked.CanAnswer.ShouldBeTrue();
        blocked.CannotAnswerReason.ShouldBeNull();
        blocked.CanContinue.ShouldBeFalse();
        blocked.Reason.ShouldBeNull("ReportEvidence is Legacy on a seeded row without a settlement class");
        blocked.PriorRounds.ShouldBeEmpty();
        blocked.Progress.ShouldNotBeNull();
        blocked.Progress!.Unavailable.ShouldBe(DelegateCheckProbe.SharedWorkspaceUnattributableExplanation);

        await DeleteAsync(task.Id);
    }

    [Test]
    public async Task an_unmarked_waiting_block_uses_the_asks_line_not_the_whole_narration()
    {
        using var workspace = new TempWorkspace();
        var sessionId = Guid.NewGuid();
        const string ask = "Please approve this design and I'll begin the recorded TDD cycles.";
        var task = await SeedAsync(
            workspace.Path,
            AgentTaskStatus.Blocked,
            sessionId: sessionId,
            result: $"I'll start by reading the spec.\n\n{ask}",
            standingAuthority: "start the remaining Coesite downloader epics one after another",
            reportEvidence: AgentTaskReportEvidence.UnmarkedWaiting);
        await AddEventAsync(task.Id, AgentTaskEventType.Blocked,
            "Turn ended without `[antiphon-report:…]`; asked once and the session stayed idle. Waiting on a human.");

        await using var db = CreateContext();
        var detail = await CreateService(db).GetAsync(task.Id, CancellationToken.None);

        var blocked = detail.Blocked.ShouldNotBeNull();
        blocked.Kind.ShouldBe(BlockedKind.Question);
        blocked.Question.ShouldBe(ask);
        blocked.Context.ShouldBe("I'll start by reading the spec.");
        blocked.Reason.ShouldBe("waiting-unmarked");
        blocked.Authority.ShouldBe("start the remaining Coesite downloader epics one after another");
        blocked.CanContinue.ShouldBeTrue();
        detail.StandingAuthority.ShouldBe("start the remaining Coesite downloader epics one after another");

        await DeleteAsync(task.Id);
    }

    [Test]
    public async Task a_cost_ceiling_block_cannot_be_answered()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedAsync(
            workspace.Path,
            AgentTaskStatus.Blocked,
            failureReason: "Run cost ceiling reached ($5.00).");
        await AddEventAsync(task.Id, AgentTaskEventType.Blocked, "Run cost ceiling reached ($5.00).");

        await using var db = CreateContext();
        var detail = await CreateService(db).GetAsync(task.Id, CancellationToken.None);

        var blocked = detail.Blocked.ShouldNotBeNull();
        blocked.Kind.ShouldBe(BlockedKind.CostCeiling);
        blocked.Question.ShouldBe("Run cost ceiling reached ($5.00).");
        blocked.CanAnswer.ShouldBeFalse();
        blocked.CannotAnswerReason.ShouldBe("Run cost ceiling reached ($5.00).");

        await DeleteAsync(task.Id);
    }

    [Test]
    public async Task a_merge_conflict_points_at_the_child_resolving_it()
    {
        using var workspace = new TempWorkspace();
        var parent = await SeedAsync(
            workspace.Path,
            AgentTaskStatus.Blocked,
            sessionId: Guid.NewGuid(),
            failureReason: "Rebase onto master conflicted in 2 file(s).");
        await AddEventAsync(parent.Id, AgentTaskEventType.Conflicted, "Conflicts: a.cs, b.cs");
        var merge = await SeedAsync(
            workspace.Path,
            AgentTaskStatus.Queued,
            parent: parent,
            role: AgentTaskRole.Merge);

        await using var db = CreateContext();
        var detail = await CreateService(db).GetAsync(parent.Id, CancellationToken.None);

        var blocked = detail.Blocked.ShouldNotBeNull();
        blocked.Kind.ShouldBe(BlockedKind.MergeConflict);
        blocked.Question.ShouldBe("Rebase onto master conflicted in 2 file(s).");
        blocked.CanAnswer.ShouldBeTrue();
        blocked.MergeTaskId.ShouldBe(merge.Id);

        await DeleteAsync(parent.Id, merge.Id);
    }

    [Test]
    public async Task prior_rounds_are_rebuilt_from_events()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedAsync(
            workspace.Path,
            AgentTaskStatus.Blocked,
            sessionId: Guid.NewGuid(),
            result: "Second question?");
        var firstAsked = DateTime.UtcNow.AddMinutes(-20);
        var firstAnswered = DateTime.UtcNow.AddMinutes(-15);
        var secondAsked = DateTime.UtcNow.AddMinutes(-5);
        await AddEventAsync(task.Id, AgentTaskEventType.Blocked, "Delegate asked: First question?", firstAsked);
        await AddEventAsync(
            task.Id, AgentTaskEventType.Replied, "Answered via Web (round 1): take the left branch", firstAnswered);
        await AddEventAsync(task.Id, AgentTaskEventType.Blocked, "Delegate asked: Second question?", secondAsked);

        await using var db = CreateContext();
        var detail = await CreateService(db).GetAsync(task.Id, CancellationToken.None);

        var blocked = detail.Blocked.ShouldNotBeNull();
        blocked.Round.ShouldBe(2);
        blocked.Question.ShouldBe("Second question?");
        blocked.PriorRounds.ShouldHaveSingleItem();
        blocked.PriorRounds[0].Round.ShouldBe(1);
        blocked.PriorRounds[0].Question.ShouldBe("First question?");
        blocked.PriorRounds[0].Answer.ShouldBe("take the left branch");
        blocked.PriorRounds[0].AnsweredVia.ShouldBe(AnswerOrigin.Web);

        await DeleteAsync(task.Id);
    }

    [Test]
    public async Task historical_replied_text_is_labelled_not_recorded()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedAsync(
            workspace.Path,
            AgentTaskStatus.Blocked,
            sessionId: Guid.NewGuid(),
            result: "New question?");
        await AddEventAsync(task.Id, AgentTaskEventType.Blocked, "Delegate asked a question.", DateTime.UtcNow.AddMinutes(-10));
        await AddEventAsync(task.Id, AgentTaskEventType.Replied, "Caller answered the delegate's question.", DateTime.UtcNow.AddMinutes(-8));
        await AddEventAsync(task.Id, AgentTaskEventType.Blocked, "Delegate asked: New question?", DateTime.UtcNow.AddMinutes(-1));

        await using var db = CreateContext();
        var detail = await CreateService(db).GetAsync(task.Id, CancellationToken.None);

        var blocked = detail.Blocked.ShouldNotBeNull();
        blocked.PriorRounds.ShouldHaveSingleItem();
        blocked.PriorRounds[0].Question.ShouldBe(BlockedQuestion.HistoricalQuestion);
        blocked.PriorRounds[0].Answer.ShouldBe(BlockedQuestion.HistoricalAnswer);

        await DeleteAsync(task.Id);
    }

    [Test]
    public async Task a_working_task_has_no_blocked_context()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedAsync(workspace.Path, AgentTaskStatus.Working, sessionId: Guid.NewGuid());

        await using var db = CreateContext();
        var detail = await CreateService(db).GetAsync(task.Id, CancellationToken.None);

        detail.Blocked.ShouldBeNull();
        await DeleteAsync(task.Id);
    }

    private static AgentTaskService CreateService(AppDbContext db) =>
        new(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings()),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static async Task<AgentTask> SeedAsync(
        string workingDirectory,
        AgentTaskStatus status,
        Guid? sessionId = null,
        string? result = null,
        string? failureReason = null,
        AgentTask? parent = null,
        AgentTaskRole role = AgentTaskRole.Docs,
        string? standingAuthority = null,
        AgentTaskReportEvidence reportEvidence = AgentTaskReportEvidence.Legacy)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = parent?.RootTaskId ?? id,
            ParentTaskId = parent?.Id,
            Depth = parent is null ? 0 : parent.Depth + 1,
            Title = "Blocked context seed",
            Goal = "Do the thing.",
            Kind = AgentTaskKind.Worker,
            Role = role,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            Status = status,
            AgentSessionId = sessionId,
            Result = result,
            FailureReason = failureReason,
            StandingAuthority = standingAuthority,
            ReportEvidence = reportEvidence,
            CreatedAt = DateTime.UtcNow,
            DispatchedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task AddEventAsync(
        Guid taskId, AgentTaskEventType type, string detail, DateTime? at = null)
    {
        await using var db = CreateContext();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = taskId,
            Type = type,
            Detail = detail,
            At = at ?? DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task DeleteAsync(params Guid[] ids)
    {
        await using var db = CreateContext();
        await db.AgentTaskEvents.Where(e => ids.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
        await db.AgentTasks.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync();
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-blocked-ctx").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
