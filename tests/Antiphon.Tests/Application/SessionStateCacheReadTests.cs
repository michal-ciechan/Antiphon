using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public class SessionStateCacheReadTests
{
    [Test]
    public async Task Queue_reads_after_warmup_do_not_query_working_state()
    {
        await using var f = await SessionStateReadFixture.CreateAsync();
        var queue = f.Services.GetRequiredService<SessionMessageQueueService>();
        (await queue.GetQueueAsync(f.SessionId, default)).Working.ShouldBeTrue();
        f.Capture.Clear();
        for (var i = 0; i < 5; i++)
            (await queue.GetQueueAsync(f.SessionId, default)).Working.ShouldBeTrue();
        f.WorkingReads.ShouldBe(0, "warmed queue reads must not derive working state from PostgreSQL again");
    }

    [Test]
    public async Task Agent_reads_after_warmup_do_not_query_working_state()
    {
        await using var f = await SessionStateReadFixture.CreateAsync();
        // A fresh request scope each time: a scoped service-local cache cannot satisfy this.
        await f.ReadAgentAsync();
        f.Capture.Clear();
        for (var i = 0; i < 5; i++) await f.ReadAgentAsync();
        f.WorkingReads.ShouldBe(0, "warmed agent list/detail reads must not derive working state from PostgreSQL again");
    }
}

internal sealed class SessionStateReadFixture : IAsyncDisposable
{
    private readonly IsolatedTestSchema _schema;
    public ServiceProvider Services { get; }
    public TranscriptCommandCapture Capture { get; } = new();
    public Guid SessionId { get; } = Guid.NewGuid();
    public Guid AgentId { get; } = Guid.NewGuid();
    public int WorkingReads => Capture.Reads.Count(c =>
        c.Sql.Contains("end_seq", StringComparison.Ordinal) ||
        c.Sql.Contains("session-state.seed", StringComparison.Ordinal));

    private SessionStateReadFixture(IsolatedTestSchema schema)
    {
        _schema = schema;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton<IDirectoryWriter, NoDirectories>();
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddDbContext<AppDbContext>(b => b.UseNpgsql(schema.ConnectionString,
            o => o.MigrationsAssembly("Antiphon.Server")).AddInterceptors(Capture));
        services.AddSingleton(Options.Create(new SessionStateSettings()));
        services.AddSingleton<ISessionStateLoader, SessionStateLoader>();
        services.AddSingleton<SessionStateStore>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddScoped<CardWorkflowRunFactory>();
        services.AddScoped<AgentService>();
        Services = services.BuildServiceProvider();
    }

    public static async Task<SessionStateReadFixture> CreateAsync()
    {
        var f = new SessionStateReadFixture(await TestDbFixture.CreateIsolatedSchemaAsync());
        try
        {
            await using var scope = f.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession { Id = f.SessionId, DefinitionName = "c701",
                Status = Antiphon.Server.Domain.Enums.SessionStatus.Running, Cwd = Path.GetTempPath(),
                CreatedAt = now, StartedAt = now, LastSeenAt = now });
            db.Agents.Add(new Agent { Id = f.AgentId, Name = "c701", Slug = "c701",
                WorkingDirectory = Path.GetTempPath(), PersistentSessionId = f.SessionId.ToString(),
                CreatedAt = now, UpdatedAt = now });
            db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.SessionId,
                Sequence = 1, Kind = "UserPrompt", Text = "synthetic work", Timestamp = now, CreatedAt = now });
            await db.SaveChangesAsync();
            f.Capture.Clear();
            return f;
        }
        catch { await f.DisposeAsync(); throw; }
    }

    public async Task ReadAgentAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var agents = scope.ServiceProvider.GetRequiredService<AgentService>();
        (await agents.GetAllAsync(default)).Single(a => a.Id == AgentId).Working.ShouldBeTrue();
        (await agents.GetByIdAsync(AgentId, default)).Working.ShouldBeTrue();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _schema.DisposeAsync();
    }

    private sealed class NoDirectories : IDirectoryWriter { public void CreateDirectory(string path) { } }
}
