using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Server.Infrastructure.Supervision;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Only inherited, parent-owned test configuration enters this graph. It starts the real
// recovery host; neither turn-end events nor an in-memory capture notification are supplied.
internal static class UnifiedRecoveryProbe
{
    public static async Task<int> RunAsync(ProbeConfig config)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        var observedId = config.DeliveryId;
        async Task BarrierAsync(string point, Guid id, CancellationToken token)
        {
            if (id != config.DeliveryId) observedId = id;
            if (point != config.Barrier) return;
            await WriteMarkerAsync(config, point, observedId, token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(config.ConnectionString);
            if (config.FailPublishCommit) o.AddInterceptors(new OutcomeRefusal(config));
        });
        var clock = new ProbeClock(config.ClockOffsetSeconds);
        var settings = Options.Create(new ChannelOutboundSettings
            { UnifiedRecoveryEnabled = true, ScanIntervalSeconds = 1,
                LeaseSeconds = config.ConverterAgentId == Guid.Empty ? 300 : 60 });
        if (config.ConverterAgentId != Guid.Empty)
            settings.Value.Profiles["crash-pdf"] = new ChannelOutboundProfile
            {
                ProjectId = config.ProjectId, AgentId = config.ConverterAgentId, PromptFile = "convert.md",
                Trigger = ChannelOutboundTrigger.EveryAgentReply, TimeoutSeconds = 300,
            };
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(settings);
        services.AddSingleton(Options.Create(new ChannelBridgeSettings
            { Enabled = true, MachineTurnTextOrigins = [QueuedMessageOrigin.Check] }));
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new AntiphonMessagingOptions()));
        services.AddSingleton<IEventBus, ProbeEventBus>();
        using var kafka = new KafkaAntiphonMessagingProducer(Options.Create(new AntiphonMessagingOptions
            { BootstrapServers = config.BootstrapServers!, OutboundTopic = config.Topic! }));
        services.AddSingleton<IAntiphonMessagingProducer>(new EntryProducer(kafka, BarrierAsync, config.DeliveryId));
        var files = new ChannelOutboundFileStore(config.StoreRoot) { ProbeBarrierAsync = BarrierAsync };
        services.AddSingleton<IChannelOutboundFileStore>(files);
        services.AddSingleton<IChannelReplyAttachmentReader, ChannelReplyAttachmentReader>();
        services.AddScoped<ChannelReplyPreparation>();
        services.AddScoped<OutboundConversionTaskRunner>(sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var tasks = new AgentTaskService(db,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                Options.Create(new DelegationSettings { AllowedRoots = [config.WorkspaceRoot] }),
                new ProbeEventBus(), new RefusingSessionStopper(), clock, NullLogger<AgentTaskService>.Instance);
            return new OutboundConversionTaskRunner(db, tasks) { ProbeBarrierAsync = BarrierAsync };
        });
        services.AddScoped<ChannelOutboundService>(sp =>
        {
            var service = ActivatorUtilities.CreateInstance<ChannelOutboundService>(sp);
            service.ProbeBarrierAsync = BarrierAsync;
            return service;
        });
        services.AddSingleton<ChannelReplyDispatcher>();
        services.AddScoped<ChatChannelService>();
        services.AddSingleton<ChannelOutboundDiscoveryService>();
        services.AddSingleton<ChannelOutboundWorkCursor>();
        services.AddScoped<ChannelOutboundDeliveryPump>(sp => new(
            sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<OutboundConversionTaskRunner>(), files,
            sp.GetRequiredService<IAntiphonMessagingProducer>(),
            sp.GetRequiredService<IOptions<AntiphonMessagingOptions>>(), clock,
            NullLogger<ChannelOutboundDeliveryPump>.Instance, settings,
            sp.GetRequiredService<ChannelReplyPreparation>(),
            cursor: sp.GetRequiredService<ChannelOutboundWorkCursor>()) { ProbeBarrierAsync = BarrierAsync });
        await using var provider = services.BuildServiceProvider();
        using var host = new ChannelOutboundHostedService(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChannelOutboundHostedService>.Instance, clock, settings);
        var cycles = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.CycleCompletedAsync = async token =>
        {
            if (config.FailPublishCommit && File.Exists(config.MarkerPath))
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            if (++cycles == 3) completed.TrySetResult();
        };
        await host.StartAsync(ct);
        try { await completed.Task.WaitAsync(ct); }
        finally { await host.StopAsync(ct); }
        return 0;
    }

    internal static async Task WriteMarkerAsync(ProbeConfig config, string point, Guid id, CancellationToken ct)
    {
        var process = Process.GetCurrentProcess();
        var assembly = typeof(UnifiedRecoveryProbe).Assembly;
        var marker = JsonSerializer.SerializeToUtf8Bytes(new
        {
            point, deliveryId = id, pid = process.Id, startedAt = process.StartTime.ToUniversalTime(),
            nativeStart = NativeStart(process),
            mvid = assembly.ManifestModule.ModuleVersionId,
            build = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            expectedSourceSha = config.ExpectedSourceSha,
        });
        await using (var file = new FileStream(config.MarkerPath + ".tmp", FileMode.Create,
            FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
        {
            await file.WriteAsync(marker, ct);
            file.Flush(true);
        }
        File.Move(config.MarkerPath + ".tmp", config.MarkerPath);
    }

    private static string NativeStart(Process process)
    {
        if (OperatingSystem.IsWindows()) return process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var stat = File.ReadAllText($"/proc/{process.Id}/stat");
        return stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[19];
    }

    private sealed class EntryProducer(IAntiphonMessagingProducer inner,
        Func<string, Guid, CancellationToken, Task> barrier, Guid id) : IAntiphonMessagingProducer
    {
        public async Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            await barrier("producer-entered", id, cancellationToken);
            await inner.SendAsync(reply, cancellationToken);
        }
    }

    private sealed class OutcomeRefusal(ProbeConfig config) : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            // Only the accepted-outcome transaction. Capture and lease commits remain real.
            if (eventData.Context!.ChangeTracker.Entries<Antiphon.Server.Domain.Entities.ChannelOutboundDelivery>()
                .Any(e => e.Entity.State == ChannelOutboundDeliveryState.Published))
            {
                var id = eventData.Context.ChangeTracker.Entries<Antiphon.Server.Domain.Entities.ChannelOutboundDelivery>()
                    .Single(e => e.Entity.State == ChannelOutboundDeliveryState.Published).Entity.Id;
                await WriteMarkerAsync(config, "outcome-refused", id, cancellationToken);
                throw new IOException("Injected accepted-outcome commit refusal.");
            }
            return result;
        }
    }
}
