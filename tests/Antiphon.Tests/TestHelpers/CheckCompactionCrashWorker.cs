using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0079 owned child. The parent keeps the database and kills this process at a committed cut.
/// </summary>
internal static class CheckCompactionCrashWorker
{
    internal const string Marker = "ANTIPHON_C79_CRASH_WORKER";

    // Dispatched by TestDbFixture.InitializeAsync through TestWorkerModes (CARD-0646).
    internal static async Task RunAsync(string encoded)
    {
        var request = JsonSerializer.Deserialize<CrashWorkerRequest>(encoded)
            ?? throw new InvalidOperationException("Missing crash worker request.");
        var owned = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".antiphon", "acceptance", "card-0079"))
            + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(request.Root).StartsWith(owned, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(request.Root, "owner")))
            throw new InvalidOperationException("Unowned compaction crash worker.");

        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        switch (request.Scenario)
        {
            case "sweep":
                await SweepAsync(request, budget.Token);
                break;
            case "produce-hold":
                await ProduceHoldAsync(request, budget.Token);
                break;
            case "scan":
                await ScanAsync(request, budget.Token);
                break;
            case "reconcile":
                await ReconcileAsync(request, budget.Token);
                break;
            default:
                throw new InvalidOperationException("Unknown crash scenario " + request.Scenario);
        }
    }

    private static async Task SweepAsync(CrashWorkerRequest request, CancellationToken ct)
    {
        var provider = BuildProvider(request, new HoldingBoundary(request));
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resume = new CountingResume(request);
        var service = new CheckCompactionContinuationService(
            db,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<DelegationSettings>>(),
            provider.GetRequiredService<CheckCompactionContinuationGate>(),
            NullLogger<CheckCompactionContinuationService>.Instance,
            Stopper(request),
            resume,
            provider.GetRequiredService<SessionMessageQueueService>(),
            provider.GetRequiredService<CheckCompactionBoundary>(),
            provider.GetRequiredService<IEventBus>());
        await service.SweepAsync(ct);
        var ids = await db.CheckCompactionRecoveries.AsNoTracking().Select(r => r.Id).ToListAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(request.Root, $"{Environment.ProcessId}.episodes.txt"),
            string.Join(Environment.NewLine, ids), ct);
    }

    private static async Task ProduceHoldAsync(CrashWorkerRequest request, CancellationToken ct)
    {
        var boundary = new HoldingBoundary(request);
        await using var harness = await AttachAsync(request, boundary);
        await using var scope = harness.Provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<LegacyCheckNotePublicationService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subject = await db.AgentTasks.SingleAsync(t => t.Id == request.CheckedTaskId, ct);
        await publisher.TryPublishAsync(
            subject, request.CheckNumber, request.Body, "event", request.RunId, false, null, ct, request.EpisodeId);
    }

    private static async Task ScanAsync(CrashWorkerRequest request, CancellationToken ct)
    {
        var boundary = new HoldingBoundary(request);
        await using var harness = await AttachAsync(request, boundary);
        var factory = harness.Provider.GetRequiredService<IServiceScopeFactory>();
        var notes = new AgentTaskLandNotificationHostedService(factory, NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        var flush = new CompletionNoteWorkHostedService(
            factory,
            harness.Provider.GetRequiredService<CompletionNoteFlushQueue>(),
            harness.Provider.GetRequiredService<SpecialistFailureQueue>(),
            harness.Provider.GetRequiredService<TimeProvider>(),
            NullLogger<CompletionNoteWorkHostedService>.Instance);
        await notes.StartAsync(ct);
        await flush.StartAsync(ct);
        try
        {
            var stop = Path.Combine(request.Root, "stop");
            while (!File.Exists(stop) && !ct.IsCancellationRequested)
                await Task.Delay(50, ct);
        }
        finally
        {
            await notes.StopAsync(CancellationToken.None);
            await flush.StopAsync(CancellationToken.None);
            notes.Dispose();
            flush.Dispose();
        }
    }

    private static async Task ReconcileAsync(CrashWorkerRequest request, CancellationToken ct)
    {
        var boundary = new HoldingBoundary(request);
        var harness = await AttachAsync(request, boundary);
        try
        {
            await using var scope = harness.Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>()
                .ReconcileAsync(request.NotificationId, ct);
        }
        finally
        {
            // Harness.DisposeAsync deletes this shared session's transcripts. The parent still
            // has to count them, and the process exit drops the provider.
            await harness.Provider.DisposeAsync();
        }
    }

    private static ServiceProvider BuildProvider(CrashWorkerRequest request, CheckCompactionBoundary boundary)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(request.ConnectionString, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<SpecialistFailureQueue>();
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            Enabled = !request.ChecksDisabled,
            CheckEnabled = !request.ChecksDisabled,
            CheckInterpreterEnabled = true,
        }));
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(boundary);
        services.AddSingleton<CheckCompactionContinuationGate>();
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddScoped<LegacyCheckNotePublicationService>();
        return services.BuildServiceProvider();
    }

    private static async Task<BridgeQueueHarness> AttachAsync(CrashWorkerRequest request, CheckCompactionBoundary boundary)
    {
        var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConnectionString = request.ConnectionString,
            AttachSessionId = request.SessionId,
            AttachAgentId = request.AgentId,
            Delegation = new DelegationSettings
            {
                Enabled = !request.ChecksDisabled,
                CheckEnabled = !request.ChecksDisabled,
                CheckInterpreterEnabled = true,
            },
            ConfigureServices = services =>
            {
                services.AddSingleton(boundary);
                services.AddSingleton<CompletionNoteFlushQueue>();
                services.AddSingleton<SpecialistFailureQueue>();
                services.AddSingleton<LandDeliveryBoundary>(new ScanBoundary(request));
                services.AddScoped<AgentTaskLandNotificationService>();
                services.AddScoped<LegacyCheckNotePublicationService>();
            },
        });
        return harness;
    }

    private static FakeSessionRunnerClient Stopper(CrashWorkerRequest request) => new()
    {
        AdvertiseCompactionStop = true,
        GetOverride = (sessionId, _) => Task.FromResult(new SessionRunnerSessionDto(
            sessionId, 1, request.Accepted, "Running", null, AgentExitReason.Unknown, 1,
            AcceptedStartedAt: request.Accepted)),
        CompactionStopResultFor = stop => new CompactionContinuationStopResult(
            request.SessionId, stop.AttemptId, true, CompactionStopOutcomes.Exited, request.Accepted),
    };

    private sealed class CountingResume(CrashWorkerRequest request) : ICompactionContinuationResume
    {
        public int Calls { get; private set; }

        public Task<CompactionResumeResult> ResumeAsync(Guid episodeId, CancellationToken ct)
        {
            Calls++;
            File.AppendAllText(Path.Combine(request.Root, "resume-calls.txt"), "call" + Environment.NewLine);
            var generation = SessionGeneration.Next(request.Accepted, request.Accepted.AddMinutes(30));
            return Task.FromResult(new CompactionResumeResult(true, request.SessionId, generation, "reserved"));
        }
    }

    private sealed class HoldingBoundary(CrashWorkerRequest request) : CheckCompactionBoundary
    {
        public override async Task ReachedAsync(string boundary, Guid operationId, CancellationToken ct)
        {
            if (boundary == "legacy-note-produced")
            {
                await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(request.ConnectionString));
                var publication = await db.LegacyCheckNotePublications.AsNoTracking().SingleAsync(p => p.Id == operationId, ct);
                await File.WriteAllTextAsync(Path.Combine(request.Root, "produced.json"), JsonSerializer.Serialize(new
                {
                    publicationId = publication.Id,
                    notificationId = publication.NotificationId,
                    body = publication.Body,
                    mvid = typeof(CheckCompactionCrashWorker).Assembly.ManifestModule.ModuleVersionId,
                    dbLifecycle = TestDbFixture.Lifecycle.State,
                    pid = Environment.ProcessId,
                }), ct);
            }

            if (string.IsNullOrEmpty(request.Hold) || boundary != request.Hold)
                return;
            await File.WriteAllTextAsync(Path.Combine(request.Root, $"held-{Environment.ProcessId}"), boundary, ct);
            while (!File.Exists(Path.Combine(request.Root, "release")) && !ct.IsCancellationRequested)
                await Task.Delay(25, ct);
        }
    }

    private sealed class ScanBoundary(CrashWorkerRequest request) : LandDeliveryBoundary
    {
        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "notification-scan")
                return;
            await File.AppendAllTextAsync(Path.Combine(request.Root, "scans.txt"), "scan" + Environment.NewLine, ct);
        }
    }
}

internal sealed class CrashWorkerRequest
{
    public string Root { get; set; } = "";
    public string Scenario { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string Hold { get; set; } = "";
    public Guid SessionId { get; set; }
    public Guid AgentId { get; set; }
    public Guid EpisodeId { get; set; }
    public Guid CheckedTaskId { get; set; }
    public Guid RunId { get; set; }
    public Guid NotificationId { get; set; }
    public int CheckNumber { get; set; } = 1;
    public string Body { get; set; } = "";
    public DateTime Accepted { get; set; }
    public bool ChecksDisabled { get; set; }
}
