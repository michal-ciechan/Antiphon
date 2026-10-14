using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class ProjectCascadeForeignKeyTests
{
    [Test]
    [Arguments(false, ExpectationAttemptState.Attempting)]
    [Arguments(true, ExpectationAttemptState.Attempting)]
    [Arguments(false, ExpectationAttemptState.Uncertain)]
    [Arguments(true, ExpectationAttemptState.Uncertain)]
    [Arguments(false, ExpectationAttemptState.Unconfirmed)]
    [Arguments(true, ExpectationAttemptState.Unconfirmed)]
    public async Task Holding_nudge_refuses_delete_preserves_composer_and_allows_audited_release(
        bool projectDelete, ExpectationAttemptState state)
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        await using var db = f.Db();
        var graph = await SeedGraphAsync(db);
        AddNudge(db, graph.Doomed, f.SessionId);
        AddQuestion(db, graph.Doomed, graph.Other.TaskId);
        await db.SaveChangesAsync();
        var generation = await f.GenerationAsync();
        await db.ExpectationNudges.Where(n => n.Id == graph.Doomed.NudgeId).ExecuteUpdateAsync(u => u
            .SetProperty(n => n.AttemptState, state).SetProperty(n => n.DestinationGeneration, generation));
        f.Harness.Adapter.PrimeComposer("Keep the audit evidence until its card is deleted.");
        await AssertComposerHeldAsync(f);
        await using var host = await DeleteHost.StartAsync(f);

        using var response = await host.Client.DeleteAsync(DeleteRoute(graph.Doomed, projectDelete));
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe("project_delete_blocked_by_expectation_hold");
        problem.RootElement.GetProperty("detail").GetString().ShouldContain(
            $"POST /api/sessions/{f.SessionId:D}/expectation-hold/release");

        await using (var verify = f.Db())
        {
            foreach (var scope in graph.Scopes)
            {
                (await verify.Projects.AnyAsync(p => p.Id == scope.ProjectId)).ShouldBeTrue();
                (await verify.Boards.AnyAsync(b => b.Id == scope.BoardId)).ShouldBeTrue();
                (await verify.BoardColumns.AnyAsync(c => c.BoardId == scope.BoardId)).ShouldBeTrue();
                (await verify.Cards.SingleAsync(c => c.Id == scope.CardId)).OwnerSessionId.ShouldBe(scope.SessionId);
                (await verify.CardComments.AnyAsync(c => c.Id == scope.CommentId)).ShouldBeTrue();
                (await verify.AgentSessions.AnyAsync(s => s.Id == scope.SessionId)).ShouldBeTrue();
                (await verify.AgentTasks.SingleAsync(t => t.Id == scope.TaskId)).CardId.ShouldBe(scope.CardId);
            }
            (await verify.ExpectationNudges.SingleAsync(n => n.Id == graph.Doomed.NudgeId)).AttemptState.ShouldBe(state);
            (await verify.AgentTaskDecisionQuestions.SingleAsync(q => q.Id == graph.Doomed.QuestionId)).Answer.ShouldBe("Keep this answer.");
            (await verify.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running);
        }
        await AssertComposerHeldAsync(f);

        using var release = new HttpRequestMessage(HttpMethod.Post, ExpectationHoldAudit.ReleaseRoute(f.SessionId))
        {
            Content = JsonContent.Create(new ReleaseExpectationHoldRequest("Inspected and cleared the composer."))
        };
        host.Authorize(release);
        using var released = await host.Client.SendAsync(release);
        released.StatusCode.ShouldBe(HttpStatusCode.OK, await released.Content.ReadAsStringAsync());
        (await released.Content.ReadFromJsonAsync<ExpectationHoldReleaseResult>())!.ReleasedNudgeIds.ShouldBe([graph.Doomed.NudgeId]);
        await using (var verify = f.Db())
        {
            (await verify.ExpectationNudges.SingleAsync(n => n.Id == graph.Doomed.NudgeId)).AttemptState.ShouldBe(ExpectationAttemptState.Released);
            (await verify.CardComments.CountAsync(c => c.CardId == graph.Doomed.CardId
                && c.Body.Contains(ExpectationHoldAudit.ReleasedMarkerPrefix))).ShouldBe(1);
        }
        f.Harness.Adapter.Inputs.ShouldBeEmpty();
        using var deleted = await host.Client.DeleteAsync(DeleteRoute(graph.Doomed, projectDelete));
        deleted.StatusCode.ShouldBe(projectDelete ? HttpStatusCode.NoContent : HttpStatusCode.OK,
            await deleted.Content.ReadAsStringAsync());
        await using var final = f.Db();
        (await final.ExpectationNudges.AnyAsync(n => n.Id == graph.Doomed.NudgeId)).ShouldBeFalse();
        (await final.AgentSessions.AnyAsync(s => s.Id == f.SessionId)).ShouldBeTrue();
        await AssertSurvivorsAsync(final, graph, projectDelete);
    }

    [Test]
    [Arguments(false, ExpectationAttemptState.None)]
    [Arguments(true, ExpectationAttemptState.None)]
    [Arguments(false, ExpectationAttemptState.Confirmed)]
    [Arguments(true, ExpectationAttemptState.Confirmed)]
    [Arguments(false, ExpectationAttemptState.Refused)]
    [Arguments(true, ExpectationAttemptState.Refused)]
    [Arguments(false, ExpectationAttemptState.Submitted)]
    [Arguments(true, ExpectationAttemptState.Submitted)]
    [Arguments(false, ExpectationAttemptState.Released)]
    [Arguments(true, ExpectationAttemptState.Released)]
    public async Task Nonholding_nudge_allows_delete_while_another_board_hold_survives(
        bool projectDelete, ExpectationAttemptState state)
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        await using var db = f.Db();
        var graph = await SeedGraphAsync(db);
        AddNudge(db, graph.Doomed, f.SessionId);
        AddNudge(db, graph.Other, f.SessionId);
        await db.SaveChangesAsync();
        var generation = await f.GenerationAsync();
        await db.ExpectationNudges.Where(n => n.Id == graph.Doomed.NudgeId)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.AttemptState, state));
        await db.ExpectationNudges.Where(n => n.Id == graph.Other.NudgeId).ExecuteUpdateAsync(u => u
            .SetProperty(n => n.AttemptState, ExpectationAttemptState.Unconfirmed)
            .SetProperty(n => n.DestinationGeneration, generation));
        f.Harness.Adapter.PrimeComposer("Keep the audit evidence until its card is deleted.");
        await using var host = await DeleteHost.StartAsync(f);

        using var deleted = await host.Client.DeleteAsync(DeleteRoute(graph.Doomed, projectDelete));
        deleted.StatusCode.ShouldBe(projectDelete ? HttpStatusCode.NoContent : HttpStatusCode.OK,
            await deleted.Content.ReadAsStringAsync());
        await using var verify = f.Db();
        (await verify.ExpectationNudges.AnyAsync(n => n.Id == graph.Doomed.NudgeId)).ShouldBeFalse();
        (await verify.ExpectationNudges.SingleAsync(n => n.Id == graph.Other.NudgeId)).AttemptState.ShouldBe(ExpectationAttemptState.Unconfirmed);
        await AssertSurvivorsAsync(verify, graph, projectDelete);
        await AssertComposerHeldAsync(f);
    }

    private static async Task AssertComposerHeldAsync(ExpectationDeliveryFixture f)
    {
        var error = await Should.ThrowAsync<ConflictException>(() => f.Harness.Queue.EnqueueAsync(
            f.SessionId, "ordinary input after delete", MessageSendMode.Now, CancellationToken.None));
        error.Code.ShouldBe("expectation_prompt_unconfirmed");
        f.Harness.Adapter.Inputs.ShouldBeEmpty();
    }

    private static string DeleteRoute(Scope scope, bool projectDelete) => projectDelete
        ? $"/api/projects/{scope.ProjectId}?force=true" : $"/api/boards/{scope.BoardId}";

    private sealed class DeleteHost(WebApplication app, string directory, string token) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();
        public void Authorize(HttpRequestMessage request) => request.Headers.Add(OperatorTokenFile.Header, token);

        public static async Task<DeleteHost> StartAsync(ExpectationDeliveryFixture f)
        {
            var directory = Path.Combine(Path.GetTempPath(), "antiphon-c1253-" + Guid.NewGuid().ToString("N"));
            var tokenPath = Path.Combine(directory, "operator-token");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.Configure<PhoneHomeRunnerSettings>(o => o.OperatorTokenPath = tokenPath);
            builder.Services.AddSingleton(f.Harness.Queue);
            builder.Services.AddScoped(_ => f.Db());
            builder.Services.AddScoped(sp => new BoardService(sp.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>(),
                new MockEventBus(), TimeProvider.System));
            builder.Services.AddScoped(sp => new ProjectService(sp.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>(),
                new StubHttpClientFactory(), Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance));
            builder.Services.AddScoped<CardService>(_ => throw new InvalidOperationException("Unused route"));
            builder.Services.AddScoped<WorkflowDefinitionLoader>(_ => throw new InvalidOperationException("Unused route"));
            builder.Services.AddScoped<ProjectSetupService>(_ => throw new InvalidOperationException("Unused route"));
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUser>(sp =>
                (ICurrentUser)sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext!.Items["CurrentUser"]!);
            var app = builder.Build();
            app.UseMiddleware<CurrentUserMiddleware>();
            app.UseMiddleware<ExceptionMiddleware>();
            app.MapBoardEndpoints();
            app.MapProjectEndpoints();
            app.MapGroup("/api/sessions").MapExpectationHoldRelease();
            await app.StartAsync();
            return new DeleteHost(app, directory, OperatorTokenFile.ReadOrCreate(tokenPath));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
