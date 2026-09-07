using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

internal sealed record DistillationSeed(
    AgentTask Task,
    Guid QueuedMessageId,
    string Report,
    string RawBody,
    string Header,
    string Digest);

internal sealed class OutputDistillationHarness : IDisposable
{
    private static string Pad(string text) => text.Length >= 120 ? text : text + "\n- " + new string('x', 120 - text.Length);

    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    public ServiceProvider Provider => _provider;
    public IServiceProvider Services => _scope.ServiceProvider;
    public DelegationSettings Settings => _settings;
    public string Scratch => _scratch;
    private readonly string _scratch;
    private readonly DelegationSettings _settings;
    private readonly object _clockLock = new();
    private bool _waitingForPoll;

    public OutputDistillationHarness(Action<DelegationSettings>? configure = null, Action<IServiceCollection>? servicesOverride = null, Action<DbContextOptionsBuilder>? configureDb = null)
    {
        _scratch = Directory.CreateTempSubdirectory("antiphon-distiller-wire").FullName;
        SpecialistSlug = $"distiller-{Guid.NewGuid():N}"[..24];
        _settings = new DelegationSettings
        {
            MaxConcurrentTasks = 512,
            OutputDistillerAgentSlug = SpecialistSlug,
            OutputDistillerWorkingDirectory = _scratch,
            OutputDistillerEnabled = true,
            OutputDistillerMode = OutputDistillerMode.Shadow,
            OutputDistillerWaitSeconds = 45,
            OutputDistillerMaxBacklog = 3,
            DistillMinChars = 1_200,
            DistillMaxRawChars = 20_000,
            DistilledMaxChars = 1_500,
            DistilledMaxRatio = 0.6,
            RolePolicy = new(StringComparer.OrdinalIgnoreCase),
        };
        configure?.Invoke(_settings);

        var utcNow = DateTimeOffset.UtcNow;
        Clock = new FakeTimeProvider(utcNow.AddTicks(-(utcNow.Ticks % 10)));
        StartedAt = Clock.GetUtcNow().UtcDateTime;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILogger<OutputDistillationService>>(new PollWaitLogger(this));
        services.AddDbContext<AppDbContext>(o => { o.UseNpgsql(TestDbFixture.ConnectionString); configureDb?.Invoke(o); });
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Options.Create(_settings));
        services.AddScoped(p => new OutputDistillerProvisioner(p.GetRequiredService<AppDbContext>(),
            Options.Create(_settings), Clock, NullLogger<OutputDistillerProvisioner>.Instance,
            claudeConfigJsonPath: Path.Combine(_scratch, "claude-test.json")));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddScoped<IModelAvailability, ModelAvailability>();
        services.AddSingleton<OutputDistillationQueue>();
        services.AddSingleton<SpecialistFailureQueue>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IAlertRouter, NullAlertRouter>();
        services.AddScoped<OutputDistillationService>();

        servicesOverride?.Invoke(services);
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        Distiller = _scope.ServiceProvider.GetRequiredService<OutputDistillationService>();
    }

    public FakeTimeProvider Clock { get; }
    public DateTime StartedAt { get; }
    public string SpecialistSlug { get; }
    public OutputDistillationService Distiller { get; }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        try { Directory.Delete(_scratch, recursive: true); }
        catch (IOException) { }
    }

    public async Task<Agent> EnsureSpecialistAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var agent = await scope.ServiceProvider.GetRequiredService<OutputDistillerProvisioner>()
            .EnsureAsync(CancellationToken.None);
        agent.ShouldNotBeNull();
        return agent;
    }

    public string PassingDistillation() => Pad(
        "- Landed CARD-0330 at a1b2c3d4e5f6789. See https://example.com/x. Cost $12.50. 3 failed. "
        + "Path server/Application/Services/Foo.cs:40 [[attach: docs/tmp/f.md]] docs/tmp/f.md");

    public async Task<DistillationSeed> SeedSourceAsync(
        string? report = null, DateTime? holdUntil = null)
    {
        var body = report ?? LongReport();
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var queuedId = Guid.NewGuid();
        var header = $"[task {DelegationReportFormatter.Short(id)} done] seeded";
        var rawBody = header + "\n\n" + body;
        var digest = DelegationNoteDigest.Compute(body);
        var now = DateTime.UtcNow;

        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ParentSessionId = sessionId,
            ReplyTo = AgentTaskReplyTo.Session,
            Title = "distill source",
            Goal = "do the thing",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = AgentTaskStatus.Succeeded,
            Result = body,
            CreatedAt = now,
            CompletedAt = now,
        };
        db.AgentTasks.Add(task);
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = queuedId,
            AgentSessionId = sessionId,
            Body = rawBody,
            Status = QueuedMessageStatus.Pending,
            Sequence = 1,
            Origin = QueuedMessageOrigin.Delegation,
            SourceTaskId = id,
            ContentDigest = digest,
            NoteHeader = header,
            HoldUntil = holdUntil,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return new DistillationSeed(task, queuedId, body, rawBody, header, digest);
    }

    public async Task SeedQueuedDistillAsync(Guid specialistId)
    {
        await using var db = CreateContext();
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "queued distill",
            Goal = "g",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Distill,
            ModelLevel = AgentModelLevel.Low,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = _scratch,
            AgentId = specialistId,
            Status = AgentTaskStatus.Queued,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public async Task MarkQueuedSentAsync(Guid queuedId)
    {
        await using var db = CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == queuedId);
        row.Status = QueuedMessageStatus.Sent;
        row.SentAt = DateTime.UtcNow;
        row.DeliveryAttempts = 1;
        await db.SaveChangesAsync();
    }

    public async Task<AgentTask> WaitForDistillAsync(Guid specialistId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var db = CreateContext();
            var row = await db.AgentTasks.AsNoTracking()
                .Where(t => t.AgentId == specialistId
                    && t.Role == AgentTaskRole.Distill
                    && t.CreatedAt >= StartedAt
                    && t.Status == AgentTaskStatus.Queued)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();
            if (row is not null)
                return row;
            await Task.Delay(25);
        }

        throw new TimeoutException("The distiller never created a Distill task.");
    }

    public async Task PumpClockAsync(Task run)
    {
        await using var db = CreateContext();
        var queued = await db.AgentTasks.AnyAsync(t => t.AgentName == SpecialistSlug
            && t.Role == AgentTaskRole.Distill && t.Status == AgentTaskStatus.Queued);
        // A poll delay can be registered after an advance, so keep pumping while it waits.
        // Do not advance during real I/O: one two-second step exhausts the cleanup budget.
        var firstAdvance = true;
        for (var spins = 0; !run.IsCompleted && spins < 400; spins++)
        {
            lock (_clockLock)
            {
                if (_waitingForPoll)
                {
                    Clock.Advance(TimeSpan.FromSeconds(
                        firstAdvance && queued ? _settings.OutputDistillerWaitSeconds : 2));
                    firstAdvance = false;
                }
            }
            await Task.Delay(15);
        }

        if (!run.IsCompleted)
            throw new TimeoutException("RequestAsync never finished waiting for its distillation.");

        await run;
    }

    // The service passes this logger to its specialist runner. Serialize phase transitions
    // with clock advances so a poll ending cannot race an advance into the cleanup phase.
    private sealed class PollWaitLogger(OutputDistillationHarness harness) : ILogger<OutputDistillationService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> properties)
                return;
            var operation = properties.FirstOrDefault(p => p.Key == "Operation").Value as string;
            if (operation != "specialist.poll-wait")
                return;
            var phase = properties.FirstOrDefault(p => p.Key == "Phase").Value as string;
            lock (harness._clockLock)
                harness._waitingForPoll = phase == "start";
        }
    }

    public async Task SettleDistillAsync(Guid id, string result)
    {
        await using var db = CreateContext();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == id);
        row.Status = AgentTaskStatus.Succeeded;
        row.Result = result;
        row.CostUsd = 0.0031m;
        row.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<AgentTask> ReloadTaskAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    public async Task<SessionQueuedMessage> ReloadQueuedAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    public async Task<List<OutputDistillationRecord>> LedgerAsync(Guid taskId)
    {
        await using var db = CreateContext();
        return await db.OutputDistillations.AsNoTracking()
            .Where(d => d.TaskId == taskId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<int> DistillCountAsync(Guid specialistId)
    {
        await using var db = CreateContext();
        return await db.AgentTasks.CountAsync(
            t => t.AgentId == specialistId && t.Role == AgentTaskRole.Distill);
    }

    public async Task<List<AgentIncident>> IncidentsAsync(Guid specialistId)
    {
        await using var db = CreateContext();
        return await db.AgentIncidents.AsNoTracking()
            .Where(i => i.AgentId == specialistId
                && i.Kind == AgentIncidentKind.OutputDistillerUnavailable
                && i.CreatedAt >= StartedAt)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync();
    }

    private static string LongReport()
    {
        var core = """
            Landed CARD-0330 at a1b2c3d4e5f6789. See https://example.com/x. Cost $12.50. 3 failed.
            Path server/Application/Services/Foo.cs:40. [[attach: docs/tmp/f.md]] docs/tmp/f.md
            """;
        return core + "\n" + new string('y', 1_400);
    }

    public static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
}
