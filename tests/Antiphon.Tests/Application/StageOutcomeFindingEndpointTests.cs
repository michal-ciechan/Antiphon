using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0272 S3: <c>POST /api/agent-tasks/{id}/finding</c> supersedes the latest delegate row
/// and can create a row on a task that had no stage.
/// </summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class StageOutcomeFindingEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    private readonly AntiphonWebAppFactory _factory;

    public StageOutcomeFindingEndpointTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task override_supersedes_the_delegate_row_and_latest_summary_counts_it_only()
    {
        var cardId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AgentTasks.Add(TaskRow(taskId, OrchestrationStage.Review));
            db.StageOutcomes.Add(new StageOutcome
            {
                Id = firstId,
                Stage = OrchestrationStage.Review,
                Outcome = StageOutcomeKind.Clean,
                Source = StageOutcomeSource.Delegate,
                StageTaskId = taskId,
                CardId = cardId,
                CostUsd = 7.48m,
                TokensIn = 10_000,
                TokensOut = 800,
                DurationSeconds = 420,
                Detail = "no blocking defects",
                RecordedAt = DateTime.UtcNow.AddMinutes(-5),
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{taskId}/finding",
            new { stage = "Review", found = true, detail = "acted on a non-blocking hole" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var written = await response.Content.ReadFromJsonAsync<StageOutcomeDto>(Json);
        written.ShouldNotBeNull();
        written.Source.ShouldBe(StageOutcomeSource.Orchestrator);
        written.Outcome.ShouldBe(StageOutcomeKind.Found);
        written.SupersedesId.ShouldBe(firstId);
        written.CostUsd.ShouldBe(7.48m);
        written.Detail.ShouldBe("acted on a non-blocking hole");

        var latest = await client.GetFromJsonAsync<StageOutcomeListDto>(
            $"/api/stage-outcomes?cardId={cardId}&latestOnly=true", Json);
        latest.ShouldNotBeNull();
        latest.Rows.ShouldHaveSingleItem();
        latest.Rows[0].Id.ShouldBe(written.Id);
        latest.Rows[0].Outcome.ShouldBe(StageOutcomeKind.Found);
        var summary = latest.Summary.ShouldHaveSingleItem();
        summary.Found.ShouldBe(1);
        summary.Clean.ShouldBe(0);
        summary.HitPercent.ShouldBe(100.0m);

        var all = await client.GetFromJsonAsync<StageOutcomeListDto>(
            $"/api/stage-outcomes?cardId={cardId}&latestOnly=false", Json);
        all.ShouldNotBeNull();
        all.Rows.Count.ShouldBe(2);
        all.Summary.ShouldHaveSingleItem().HitPercent.ShouldBe(50.0m);

        using var verify = _factory.Services.CreateScope();
        var events = await verify.ServiceProvider.GetRequiredService<AppDbContext>()
            .AgentTaskEvents.Where(e => e.AgentTaskId == taskId).ToListAsync();
        events.ShouldContain(e => e.Type == AgentTaskEventType.FindingRecorded
            && e.Detail.Contains("Found")
            && e.Detail.Contains("acted on a non-blocking hole"));
    }

    [Test]
    public async Task a_finding_on_a_task_with_no_stage_creates_the_row_at_the_given_stage()
    {
        var cardId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = TaskRow(taskId, stage: null);
            task.CardId = cardId;
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{taskId}/finding",
            new { stage = "Verify", found = false, detail = "build was already green" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var written = await response.Content.ReadFromJsonAsync<StageOutcomeDto>(Json);
        written.ShouldNotBeNull();
        written.Stage.ShouldBe(OrchestrationStage.Verify);
        written.Outcome.ShouldBe(StageOutcomeKind.Clean);
        written.Source.ShouldBe(StageOutcomeSource.Orchestrator);
        written.StageTaskId.ShouldBe(taskId);
        written.SupersedesId.ShouldBeNull();
        written.Detail.ShouldBe("build was already green");
    }

    [Test]
    public async Task an_unknown_stage_name_is_422()
    {
        var taskId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AgentTasks.Add(TaskRow(taskId, stage: null));
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{taskId}/finding",
            new { stage = "PlanReview", found = true, detail = "nope" });
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("PlanReview");
        body.ShouldContain("Rebase");
    }

    private static AgentTask TaskRow(Guid id, OrchestrationStage? stage) => new()
    {
        Id = id,
        RootTaskId = id,
        Title = "finding-endpoint",
        Goal = "finding-endpoint",
        Role = AgentTaskRole.Review,
        Stage = stage,
        Status = AgentTaskStatus.Succeeded,
        WorkingDirectory = @"C:\tmp\finding-endpoint",
        CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        DispatchedAt = DateTime.UtcNow.AddMinutes(-9),
        CompletedAt = DateTime.UtcNow.AddMinutes(-1),
        CostUsd = 1.23m,
    };
}
