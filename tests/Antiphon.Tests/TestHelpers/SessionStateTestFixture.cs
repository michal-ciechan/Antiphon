using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

internal sealed class SessionStateTestFixture : IAsyncDisposable
{
    private readonly IsolatedTestSchema _schema;
    private readonly IInterceptor? _fault;
    private readonly Func<ISessionStateLoader, ISessionStateLoader>? _decorate;
    private readonly SessionStateSettings _settings;
    private readonly TimeProvider _clock;
    public Guid SessionId { get; } = Guid.NewGuid();
    public Guid OtherId { get; } = Guid.NewGuid();
    public ServiceProvider Services { get; }
    public TranscriptCommandCapture Capture { get; } = new();
    public SessionStateStore Store => Services.GetRequiredService<SessionStateStore>();
    public SessionStateCommandMetrics Commands => Services.GetRequiredService<SessionStateCommandMetrics>();
    public AgentSessionRuntime Runtime => Services.GetRequiredService<AgentSessionRuntime>();
    public int SeedQueries => Capture.Reads.Count(c => c.Sql.Contains("session-state.seed"));

    private SessionStateTestFixture(IsolatedTestSchema schema, IInterceptor? fault,
        Func<ISessionStateLoader, ISessionStateLoader>? decorate, SessionStateSettings? settings, TimeProvider? clock)
    {
        _schema = schema; _fault = fault; _decorate = decorate;
        _settings = settings ?? new SessionStateSettings(); _clock = clock ?? TimeProvider.System;
        Services = NewContainer();
    }

    public ServiceProvider NewContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_clock);
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(_settings));
        services.AddSingleton<SessionStateCommandMetrics>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            Configure(options);
            options.AddInterceptors(sp.GetRequiredService<SessionStateCommandMetrics>());
        });
        services.AddSingleton<ISessionStateLoader>(sp =>
        {
            var loader = new SessionStateLoader(sp.GetRequiredService<IServiceScopeFactory>());
            return _decorate?.Invoke(loader) ?? loader;
        });
        services.AddSingleton<SessionStateStore>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        return services.BuildServiceProvider();
    }

    private void Configure(DbContextOptionsBuilder b)
    {
        b.UseNpgsql(_schema.ConnectionString, o => o.MigrationsAssembly("Antiphon.Server")).AddInterceptors(Capture);
        if (_fault is not null) b.AddInterceptors(_fault);
    }

    public AppDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>(); Configure(options);
        return new AppDbContext(options.Options);
    }

    public static async Task<SessionStateTestFixture> CreateAsync(IInterceptor? fault = null,
        Func<ISessionStateLoader, ISessionStateLoader>? decorate = null, SessionStateSettings? settings = null, TimeProvider? clock = null)
    {
        var f = new SessionStateTestFixture(await TestDbFixture.CreateIsolatedSchemaAsync(), fault, decorate, settings, clock);
        try
        {
            await using var db = f.Db();
            foreach (var id in new[] { f.SessionId, f.OtherId }) db.AgentSessions.Add(new AgentSession
            {
                Id = id, DefinitionName = "c701", Status = SessionStatus.Running, Cwd = Path.GetTempPath(),
                CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(); f.Capture.Clear(); return f;
        }
        catch { await f.DisposeAsync(); throw; }
    }

    public SessionRunnerTranscriptEvent Event(long sequence, string kind = "AssistantText", int? seconds = 0,
        string? text = null, string? uuid = null) => new(SessionId, sequence, kind, uuid ?? Guid.NewGuid().ToString(), null,
        seconds is { } s ? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(s) : null,
        "assistant", text, null, null, null, null, null);

    public async Task<List<TranscriptEntry>> RowsAsync()
    {
        await using var db = Db();
        return await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId).OrderBy(t => t.Sequence).ToListAsync();
    }

    public async ValueTask DisposeAsync() { await Services.DisposeAsync(); await _schema.DisposeAsync(); }
}
