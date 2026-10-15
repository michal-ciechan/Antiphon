using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PipelineHandoffPilotTests
{
    [Test]
    public async Task C1281_DelayAccounting()
    {
        await using var host = await PilotHost.CreateAsync();
        var request = host.Request("consumed", "Read the completion");
        using var response = await host.Client.PostAsJsonAsync(host.Url, request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "explicit-consumption-is-durable: " + await response.Content.ReadAsStringAsync());
        var initial = (await response.Content.ReadFromJsonAsync<PipelineHandoffActionDto>())!;
        initial.Action.ConsumedAt.ShouldNotBeNull("explicit-consumption-is-durable");
        initial.Action.ConsumedAt.ShouldNotBe(initial.Action.SettledAt, "separate-consumption-clock");
        initial.Action.ReceivedAt.ShouldBeNull();
        initial.Action.EligibleAt.ShouldBeNull();
        initial.Action.ChildCreatedAt.ShouldBeNull();
        initial.Action.ChildDispatchedAt.ShouldBeNull("unknown-dispatch-null");
        long sequence = 1;
        foreach (var reason in new[] { "dependency", "decision", "capacity", "overlap", "priority", "evidence", "quota", "placement" })
        {
            request = host.Request(reason, "Waiting: " + reason, sequence);
            using var saved = await host.Client.PostAsJsonAsync(host.Url, request);
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            await host.RestartHttpAsync();
            var read = (await host.Client.GetFromJsonAsync<List<PipelineHandoffActionDto>>(host.Url))!.ShouldHaveSingleItem();
            read.Action.Id.ShouldBe(initial.Action.Id);
            read.Action.ConsumedAt.ShouldBe(initial.Action.ConsumedAt);
            read.Observations.Last().ReasonCode.ShouldBe(reason, "restart-retains-reason-and-next-observation");
            read.Observations.Last().NextObservationAt.ShouldNotBeNull("restart-retains-reason-and-next-observation");
            read.Observations.Count.ShouldBe((int)++sequence);
            using var repeat = await host.Client.PostAsJsonAsync(host.Url, request);
            repeat.StatusCode.ShouldBe(HttpStatusCode.OK, "lost-response-replay");
        }
        var race = await Task.WhenAll(new[] { "one", "two" }.Select(detail =>
            host.Client.PostAsJsonAsync(host.Url, host.Request("capacity", detail, sequence))));
        try
        {
            race.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1, "observation-fence-one-winner");
            race.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1, "observation-fence-one-winner");
        }
        finally { foreach (var item in race) item.Dispose(); }
        sequence++;
        using var eligible = await host.Client.PostAsJsonAsync(host.Url, host.Request("eligible", "All prerequisites observed", sequence));
        eligible.StatusCode.ShouldBe(HttpStatusCode.OK);
        host.Clock.Advance(TimeSpan.FromMinutes(15));
        (await host.ReadAsync()).UnexplainedWaitRequiresInvestigation.ShouldBeFalse("15m-is-not-over-15m");
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        (await host.ReadAsync()).UnexplainedWaitRequiresInvestigation.ShouldBeTrue("unexplained-over-15m");
        (await host.ReadAsync()).Action.ChildDispatchedAt.ShouldBeNull("unknown-dispatch-null");
        // These are projection fixtures, not a claim that B-2 child admission is implemented.
        await using var db = host.World.CreateContext();
        var action = await db.PipelineHandoffActions.SingleAsync();
        var child = new AgentTask { Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "clock fixture",
            Goal = "clock fixture", CardId = action.CardId, ProjectId = action.ProjectId, Role = action.NextStage,
            WorkingDirectory = host.World.RepositoryPath, CreatedAt = action.EligibleAt!.Value };
        db.Add(child); action.ChildTaskId = child.Id;
        foreach (var (elapsed, expected) in new[] { (TimeSpan.FromSeconds(299), true), (TimeSpan.FromMinutes(5), true), (TimeSpan.FromMilliseconds(300001), false) })
        {
            child.CreatedAt = action.EligibleAt.Value + elapsed;
            await db.SaveChangesAsync();
            (await host.ReadAsync()).RequestedWithinFiveMinutes.ShouldBe(expected, "exact-five-minute-boundary");
        }
        child.DispatchedAt = child.CreatedAt.AddMinutes(2);
        await db.SaveChangesAsync();
        (await host.ReadAsync()).Action.ChildDispatchedAt.ShouldBe(child.DispatchedAt, "actual-child-dispatch-clock");
    }

    [Test]
    public async Task C1281_WriterAuthorization()
    {
        await using var host = await PilotHost.CreateAsync();
        using (var saved = await host.Client.PostAsJsonAsync(host.Url, host.Request("consumed", "operator")))
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var variant in new[] { "anonymous", "invalid", "revoked", "worker", "project", "repository", "board", "card", "allowed" })
        {
            using var client = host.App.GetTestClient();
            if (variant != "anonymous")
                client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", await host.CredentialAsync(variant));
            using var get = await client.GetAsync(host.Url);
            using var post = await client.PostAsJsonAsync(host.Url, host.Request("consumed", variant, 1));
            var expected = variant == "allowed" ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            get.StatusCode.ShouldBe(expected, "get-foreign-history-refused " + variant);
            post.StatusCode.ShouldBe(expected, "post-anonymous-refused " + variant + " " + await post.Content.ReadAsStringAsync());
            if (variant != "allowed")
            {
                (await get.Content.ReadAsStringAsync()).Contains("operator", StringComparison.Ordinal)
                    .ShouldBeFalse("denied-get-no-history");
                (await host.ReadAsync()).Observations.Count.ShouldBe(1, "denied-post-no-write");
            }
        }
        (await host.ReadAsync()).Observations.Last().Actor.StartsWith("task:", StringComparison.Ordinal)
            .ShouldBeTrue("authenticated-actor-retained");
        var body = JsonSerializer.SerializeToNode(host.Request("consumed", "forged", 2))!;
        body["actor"] = "operator";
        using var forged = await host.Client.PostAsJsonAsync(host.Url, body);
        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "actor-cannot-be-supplied");
        foreach (var invalid in new[]
        {
            host.Request("consumed", "stale", 0),
            host.Request("consumed", "attempt", 2) with { Attempt = host.World.Owner.Attempt + 1 },
            host.Request("consumed", "event", 2) with { SettlementEventId = Guid.NewGuid() },
            host.Request("consumed", "workflow", 2) with { WorkflowRunId = Guid.NewGuid() },
            host.Request("consumed", "revision", 2) with { PipelineDefinitionRevisionId = Guid.NewGuid() },
            host.Request("consumed", "stage", 2) with { NextStage = AgentTaskRole.Review },
        })
        {
            using var refused = await host.Client.PostAsJsonAsync(host.Url, invalid);
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, "stale-identity-refused");
        }
        (await host.ReadAsync()).Observations.Count.ShouldBe(2);
    }

    [Test]
    public async Task C1281_WriterReceipt()
    {
        foreach (var busy in new[] { false, true })
        {
            await using var rig = await C544DeliveryRig.CreateAsync(busy);
            var (taskId, _) = await rig.SettleReviewAsync(next: "code");
            await rig.DeliverAsync("B-2 receipt");
            await rig.ScanAsync();
            var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull();
            var queue = await rig.RowAsync(taskId);
            var wire = TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson).ShouldNotBeNull();
            await using (var fresh = rig.World.CreateContext())
                (await fresh.PipelineHandoffActions.CountAsync()).ShouldBe(0, "delivered-not-consumed");
            var receipt = new PipelineHandoffReceiptReference(note.Id, queue.Id, rig.World.CallerSessionId,
                queue.LastDeliveryGeneration!.Value, note.ConfirmingPromptSequence!.Value, wire.WireSha256);
            var predecessor = await rig.World.TaskAsync(taskId);
            var request = new PipelineHandoffActionRequest(predecessor.Attempt, note.SourceEventId, AgentTaskRole.Code,
                0, "consumed", "Explicit receipt consumption", Receipt: receipt);
            foreach (var wrong in new[] { receipt with { NotificationId = Guid.NewGuid() },
                receipt with { QueueId = Guid.NewGuid() }, receipt with { RecipientSessionId = Guid.NewGuid() },
                receipt with { RecipientGeneration = receipt.RecipientGeneration.AddSeconds(-1) },
                receipt with { ReceiptSequence = queue.LastDeliveryBaselineSequence ?? 0 },
                receipt with { WireDigest = new string('0', 64) } })
            {
                await using var db = rig.World.CreateContext();
                var service = new PipelineHandoffActionService(db, rig.World.Clock);
                var error = await Should.ThrowAsync<ConflictException>(() => service.RecordAsync(taskId,
                    request with { Receipt = wrong }, "operator", CancellationToken.None), "foreign-receipt-refused");
                error.Code.ShouldBe("handoff_foreign_receipt");
            }
            await using (var db = rig.World.CreateContext())
            {
                var service = new PipelineHandoffActionService(db, rig.World.Clock);
                var result = await service.RecordAsync(taskId, request, "operator", CancellationToken.None);
                result.Action.ReceivedAt.ShouldBe(note.ConfirmedAt);
                result.Action.ConsumedAt.ShouldNotBeNull();
                result.Action.ChildDispatchedAt.ShouldBeNull("unknown-dispatch-null");
            }
            await rig.RestartAsync();
            await using var restarted = rig.World.CreateContext();
            var records = await new PipelineHandoffActionService(restarted, rig.World.Clock).GetAsync(taskId, CancellationToken.None);
            records.ShouldHaveSingleItem().Action.ReceiptSequence.ShouldBe(note.ConfirmingPromptSequence);
        }
    }

    [Test]
    [Arguments("obligation-insert")]
    [Arguments("settled-committed")]
    [Arguments("note-insert")]
    [Arguments("note-committed")]
    [Arguments("wakeup-dropped")]
    [Arguments("render-committed")]
    [Arguments("spill-written")]
    [Arguments("attempt-committed")]
    [Arguments("prompt-accepted")]
    public Task C1281_CompletionRecovery(string cut) =>
        new CompletionWarningDeliveryTests().C788_ProgressWarningRecovery(cut);

    // Component composition only. S6 owns real Program registration. All routes below are
    // the production extension; no test-only endpoint or production runner is installed.
    private sealed class PilotHost : IAsyncDisposable
    {
        public C544World World { get; private set; } = null!;
        public WebApplication App { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public Guid EventId { get; private set; }
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero));
        private List<ServiceDescriptor> Graph { get; set; } = [];
        public string Url => $"/api/agent-tasks/{World.Owner.Id:D}/handoff-actions";
        private string TokenPath { get; } = Path.Combine(Path.GetTempPath(), "c1281-operator-" + Guid.NewGuid().ToString("N"));

        public static async Task<PilotHost> CreateAsync()
        {
            var host = new PilotHost();
            List<ServiceDescriptor> graph = [];
            host.World = await C544World.CreateAsync(configure: services => graph = services.ToList());
            await using (var db = host.World.CreateContext())
            {
                var owner = await db.AgentTasks.SingleAsync(t => t.Id == host.World.Owner.Id);
                owner.NextStage = PipelineHandoffKind.Code;
                var ev = new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = owner.Id,
                    Type = AgentTaskEventType.Completed, At = owner.CompletedAt!.Value };
                host.EventId = ev.Id;
                db.Add(ev);
                await db.SaveChangesAsync();
            }
            host.Graph = graph;
            await host.StartHttpAsync();
            return host;
        }

        private async Task StartHttpAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            foreach (var descriptor in Graph) builder.Services.Add(descriptor);
            builder.Services.AddSingleton<TimeProvider>(Clock);
            builder.Services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings { OperatorTokenPath = TokenPath }));
            builder.Services.AddScoped<PipelineHandoffActionService>();
            App = builder.Build();
            App.UseMiddleware<ExceptionMiddleware>();
            App.MapPipelineHandoffActionEndpoints();
            await App.StartAsync();
            Client = App.GetTestClient();
            Client.DefaultRequestHeaders.Add(OperatorTokenFile.Header, OperatorTokenFile.ReadOrCreate(TokenPath));
        }

        public async Task RestartHttpAsync()
        {
            Client.Dispose(); await App.DisposeAsync(); await StartHttpAsync();
        }

        public PipelineHandoffActionRequest Request(string reason, string detail, long sequence = 0) =>
            new(World.Owner.Attempt, EventId, AgentTaskRole.Code, sequence, reason, detail,
                reason is "consumed" or "eligible" ? null : Clock.GetUtcNow().UtcDateTime.AddMinutes(5));

        public async Task<PipelineHandoffActionDto> ReadAsync() =>
            (await Client.GetFromJsonAsync<List<PipelineHandoffActionDto>>(Url))!.ShouldHaveSingleItem();

        public async Task<string> CredentialAsync(string variant)
        {
            if (variant == "invalid") return "deliberately-invalid";
            var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            await using var db = World.CreateContext();
            if (variant is "revoked" or "repository" or "board")
            {
                var board = World.Board.Id;
                if (variant == "board")
                {
                    var other = new Board { Id = Guid.NewGuid(), ProjectId = World.Project.Id, Name = "foreign board" };
                    db.Add(other); board = other.Id;
                }
                db.Add(new DelegationCapability { Id = Guid.NewGuid(), Name = "B2 test " + variant, ProjectId = World.Project.Id,
                    BoardId = board, TokenHash = AgentTaskService.HashToken(token),
                    RootsJson = JsonSerializer.Serialize(new[] { World.RepositoryPath + (variant == "repository" ? "-sibling" : "") }),
                    RevokedAt = variant == "revoked" ? DateTime.UtcNow : null });
            }
            else
            {
                var project = World.Project.Id;
                if (variant == "project")
                {
                    var other = new Project { Id = Guid.NewGuid(), Name = "foreign project" };
                    db.Add(other); project = other.Id;
                }
                Guid? card = null;
                if (variant == "card")
                {
                    var other = new Card { Id = Guid.NewGuid(), BoardId = World.Board.Id, BoardColumnId = World.Card.BoardColumnId,
                        Identifier = "CARD-9999", Title = "foreign card" };
                    db.Add(other); card = other.Id;
                }
                var id = Guid.NewGuid();
                db.Add(new AgentTask { Id = id, RootTaskId = id, Title = "credential", Goal = "credential",
                    ProjectId = project, CardId = card, RepoPath = World.RepositoryPath, WorkingDirectory = World.RepositoryPath,
                    Kind = variant == "worker" ? AgentTaskKind.Worker : AgentTaskKind.Orchestrator,
                    TokenHash = AgentTaskService.HashToken(token) });
            }
            await db.SaveChangesAsync();
            return token;
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
            await World.DisposeAsync();
            File.Delete(TokenPath);
        }
    }
}
