using System.Net;
using System.Net.Http.Json;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Application;

/// <summary>Migrated isolated PostgreSQL, real queue/state/DI and serialized fake runner I/O.</summary>
internal sealed class RunnerSeatReleaseFixture : IAsyncDisposable
{
    public required IsolatedTestSchema Schema { get; init; }
    public required BridgeQueueHarness Harness { get; init; }
    public required FakeTimeProvider Clock { get; init; }
    public required SeatWire Wire { get; init; }
    public required SeatDirectory Directory { get; init; }
    public Guid TaskId { get; } = Guid.NewGuid();
    public Guid SessionId => Harness.SessionId;
    public Guid AgentId => Harness.AgentId;
    public DateTime Now => Clock.GetUtcNow().UtcDateTime;
    public TerminalSeatObservationRequest Observation => new(Directory.StoreId, Now.AddHours(-1), "binding", 10);
    public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public static async Task<RunnerSeatReleaseFixture> CreateAsync(AgentTaskStatus status = AgentTaskStatus.Succeeded)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));
        var wire = new SeatWire();
        var http = new HttpClient(wire);
        var client = new SessionRunnerHttpClient(http, new ClientFactory(http),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://seat.test" }));
        var directory = new SeatDirectory(client);
        BridgeQueueHarness harness;
        try
        {
            harness = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = schema.ConnectionString, TimeProvider = clock,
                AlwaysOn = false, PreserveDatabaseOnDispose = true,
                ConfigureServices = services =>
                {
                    services.AddSingleton<ISessionRunnerDirectory>(directory);
                    services.AddSingleton<ISessionStateLoader, SessionStateLoader>();
                    services.AddSingleton<SessionStateStore>();
                    services.AddSingleton(Options.Create(new SessionStateSettings()));
                    services.AddSingleton(Options.Create(new TerminalRunnerSeatReleaseOptions { AutomaticEnabled = true }));
                    services.AddSingleton<TerminalRunnerSeatReleasePolicy>();
                    services.AddScoped<TerminalRunnerSeatReleaseService>();
                }
            });
        }
        catch { http.Dispose(); await schema.DisposeAsync(); throw; }
        var f = new RunnerSeatReleaseFixture { Schema = schema, Harness = harness, Clock = clock, Wire = wire, Directory = directory };
        await using var db = f.Db();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
        session.RunnerId = "fixture"; session.RunnerStoreId = directory.StoreId;
        session.StartedAt = f.Observation.ExpectedAcceptedStartedAt;
        var agent = await db.Agents.SingleAsync(a => a.Id == f.AgentId);
        agent.IsPoolDelegate = true; agent.AlwaysOn = false; agent.BoardId = null;
        db.AgentTasks.Add(new AgentTask
        {
            Id = f.TaskId, RootTaskId = f.TaskId, AgentId = f.AgentId, AgentSessionId = f.SessionId,
            RunnerId = "fixture", Workspace = WorkspaceMode.Worktree, Attempt = 1,
            Status = status, CompletedAt = f.Now.AddMinutes(-3), CreatedAt = f.Now.AddHours(-1),
            Result = "completed report", ReportEvidence = AgentTaskReportEvidence.Marked,
        });
        await db.SaveChangesAsync();
        await harness.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "task", timestamp: f.Now.AddMinutes(-4));
        await harness.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn", timestamp: f.Now.AddMinutes(-3));
        return f;
    }

    public async Task<TerminalRunnerSeatReservation> RunAsync(Guid? taskId = null)
    {
        using var scope = Harness.Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()
            .RegisterAndReserveAsync(taskId ?? TaskId, Observation, CancellationToken.None);
    }

    public async Task EditAsync(Action<AgentTask, Agent> edit)
    {
        await using var db = Db();
        edit(await db.AgentTasks.SingleAsync(t => t.Id == TaskId), await db.Agents.SingleAsync(a => a.Id == AgentId));
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Harness.DisposeAsync();
        Wire.Dispose();
        await Schema.DisposeAsync();
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => client; }

    internal sealed class SeatWire : HttpMessageHandler
    {
        public bool Unsupported { get; set; }
        public List<string> Calls { get; } = [];
        public int ConditionalCommands => Calls.Count(p => p.EndsWith("/release-terminal-seat"));
        public int ForceCommands => Calls.Count(p => p.EndsWith("/kill") || p.EndsWith("/kill-generation") || p.EndsWith("/release"));
        public TerminalSeatObservation Qualified { get; } = new(TerminalSeatQualificationStatus.Qualified,
            new(TerminalTranscriptReadStatus.Success, TerminalTranscriptVerdict.Idle,
                "binding", "file", 100, 12, 12, 11), "issued-token", TimeSpan.FromSeconds(120));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(Unsupported
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Qualified) });
        }
    }

    internal sealed class SeatDirectory(ISessionRunnerClient client) : ISessionRunnerDirectory
    {
        public Guid StoreId { get; } = Guid.NewGuid();
        public ISessionRunnerClient Client { get; set; } = client;
        public ISessionRunnerClient Local => Client;
        public ISessionRunnerClient Resolve(string? runnerId) => Client;
        public Guid? GetLiveStoreId(string? runnerId) => StoreId;
        public IReadOnlyList<string> KnownRunnerIds => ["fixture"];
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(new("fixture", StoreId, "/fixture"));
        public Task<SessionRunnerBinding> GetBindingAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(new SessionRunnerBinding.Remote(new("fixture", StoreId, "/fixture")));
        public Task<RunnerInventory> GetInventoryAsync(string? id, CancellationToken ct) => Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("not part of S3a"));
    }
}
