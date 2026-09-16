using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.TestHelpers;

internal sealed class HerdrLabelFollowDbFixture : IAsyncDisposable
{
    private IsolatedTestSchema _store = null!;
    private readonly List<AppDbContext> _contexts = [];
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero));
    public Guid AgentId { get; } = Guid.NewGuid();
    public Guid SessionId { get; } = Guid.NewGuid();
    public Guid EditToken { get; } = Guid.NewGuid();
    public SessionRunnerSessionDto Dto { get; set; } = null!;
    public FakeSessionRunnerClient Runner { get; } = new();
    public RecordingBus Bus { get; } = new();
    public HerdrLabelFollowSettings Settings { get; } = new();
    public int Polls { get; private set; }
    public string ConnectionString => _store.ConnectionString;

    public async Task StartAsync()
    {
        _store = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = Clock.GetUtcNow().UtcDateTime;
        await using var db = Open();
        db.Agents.Add(new Agent { Id = AgentId, Name = "Follow", Slug = "follow-" + AgentId.ToString("N"), WorkingDirectory = "D:/src/app",
            Kind = AgentKind.ClaudeCode, SessionBackend = SessionBackend.Herdr, HerdrTabLabel = "Old", HerdrWorkspaceLabel = "Old workspace",
            HerdrPlacementEditToken = EditToken, PersistentSessionId = SessionId.ToString(), CreatedAt = now.AddDays(-1), UpdatedAt = now.AddDays(-1) });
        db.AgentSessions.Add(new AgentSession { Id = SessionId, StandingAgentId = AgentId, SessionBackend = SessionBackend.Herdr,
            Status = SessionStatus.Running, StartedAt = now, CreatedAt = now, LastSeenAt = now, Cwd = "D:/src/app" });
        await db.SaveChangesAsync();
        Dto = new(SessionId, 4243, now, "Running", null, AgentExitReason.Unknown, 0, Backend: SessionBackends.Herdr,
            HerdrOrigin: HerdrPaneOrigins.Launched, AcceptedStartedAt: now,
            LabelObservation: new(1, SessionId, now, "w1", "w1:t1", "w1:p1", HerdrPaneOrigins.Launched,
                new(1, AgentId, EditToken, "Old workspace", "Old"), 1, now, now.AddHours(1), "validated", "New", "New workspace", true));
        Runner.GetOverride = (_, _) => { Polls++; return Task.FromResult(Dto); };
    }

    public AppDbContext Open() => new(TestDbFixture.CreateDbContextOptions(_store.ConnectionString));
    public HerdrLabelFollowService Service(AppDbContext? context = null)
    {
        if (context is null) { context = Open(); _contexts.Add(context); }
        return new(context, Runner, Bus, Clock, Options.Create(Settings), NullLogger<HerdrLabelFollowService>.Instance);
    }
    public Task<bool> ApplyAsync() => Service().ApplyAsync(AgentId, Dto, CancellationToken.None);
    public async Task<Agent> ReadAsync() { await using var db = Open(); return await db.Agents.AsNoTracking().SingleAsync(a => a.Id == AgentId); }
    public async Task MutateAsync(Action<Agent, AgentSession> change)
    {
        await using var db = Open(); change(await db.Agents.SingleAsync(a => a.Id == AgentId), await db.AgentSessions.SingleAsync(s => s.Id == SessionId));
        await db.SaveChangesAsync();
    }
    public async Task ManualAsync(string? tab = null, string? workspace = null)
    {
        await using var db = Open(); var a = await db.Agents.SingleAsync(a => a.Id == AgentId);
        var service = new AgentService(db, new CardWorkflowRunFactory(db, TimeProvider.System), new MockEventBus(),
            TimeProvider.System, new NoDirectories(), NullLogger<AgentService>.Instance);
        await service.UpdateAsync(a.Id, new(a.Name, a.WorkingDirectory, a.Details, a.DefaultWorkflowTemplateId, a.AssignmentPolicy,
            HerdrTabLabel: tab, HerdrWorkspaceLabel: workspace), CancellationToken.None);
    }
    public async ValueTask DisposeAsync()
    { foreach (var db in _contexts) await db.DisposeAsync(); if (_store is not null) await _store.DisposeAsync(); }

    private sealed class NoDirectories : IDirectoryWriter { public void CreateDirectory(string path) { } }
    public sealed class RecordingBus : IEventBus
    {
        public int Count { get; private set; }
        public Func<Task>? BeforeRecord { get; set; }
        public async Task PublishToAllAsync(string name, object payload, CancellationToken ct = default)
        { if (name != "AgentChanged") return; if (BeforeRecord is not null) await BeforeRecord(); Count++; }
        public Task PublishToGroupAsync(string group, string name, object payload, CancellationToken ct = default) => PublishToAllAsync(name, payload, ct);
    }
}
