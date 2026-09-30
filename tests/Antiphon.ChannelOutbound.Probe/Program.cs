using System.Data.Common;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// The parent owns the database and directory. No production configuration or
// external runner is loaded by this crash probe.
if (args.Length != 1 || !File.Exists(args[0])) return 2;
var config = JsonSerializer.Deserialize<ProbeConfig>(await File.ReadAllTextAsync(args[0]));
if (config is null || config.DeliveryId == Guid.Empty && config.Mode != "admit") return 2;
try
{
    var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(config.ConnectionString);
    if (config.FailPublishCommit)
        options.AddInterceptors(new RefusingCommitInterceptor(
            Path.Combine(Path.GetDirectoryName(config.MarkerPath)!, "commit-injected")));
    await using var db = new AppDbContext(options.Options);
    Func<string, Guid, CancellationToken, Task> barrier = async (point, id, ct) =>
    {
        if (point != config.Barrier || (config.DeliveryId != Guid.Empty && id != config.DeliveryId)) return;
        await using (var stream = new FileStream(config.MarkerPath, FileMode.CreateNew,
                         FileAccess.Write, FileShare.Read, 1, FileOptions.WriteThrough))
        {
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(point), ct);
            stream.Flush(flushToDisk: true);
        }
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    };
    var files = new ChannelOutboundFileStore(config.StoreRoot);
    if (config.Barrier is not null) files.ProbeBarrierAsync = barrier;
    var clock = new ProbeClock(config.ClockOffsetSeconds);
    var profiles = Options.Create(new ChannelOutboundSettings
    {
        Profiles = new Dictionary<string, ChannelOutboundProfile>
        {
            ["crash-pdf"] = new()
            {
                ProjectId = config.ProjectId, AgentId = config.ConverterAgentId,
                PromptFile = "convert.md", Trigger = ChannelOutboundTrigger.MarkdownSources,
            },
        },
    });
    if (config.Mode is "dispatch" or "dispatch-running")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(config.ConnectionString));
        services.AddSingleton<IEventBus, ProbeEventBus>();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
            { AllowedRoots = [config.WorkspaceRoot], MaxConcurrentTasks = 16 }));
        services.AddSingleton(Options.Create(new GitSettings()));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            var running = config.Mode == "dispatch-running";
            s.DefaultDefinition = running ? "grok" : "claude";
            s.GrokCredentialProbeEnabled = false;
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            if (running)
                s.Definitions["grok"] = new AgentDefinition
                {
                    Kind = "Grok", Exe = config.WorkerExe!,
                    ArgsTemplate = ["--always-approve", "--no-alt-screen"],
                    Env = new Dictionary<string, string>
                    {
                        ["GROK_HOME"] = config.WorkerHome!,
                        ["ANTIPHON_FAKE_REPORT_LINE"] = "1",
                        ["ANTIPHON_FAKE_OUTBOUND_TOOL"] = "fixture:pdf",
                        ["ANTIPHON_FAKE_OUTBOUND_TOOL_GATE"] = config.WorkerGate!,
                    },
                };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RefusingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton<IWorktreeManager, WorktreeManager>();
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<GitWorkspaceService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        services.AddSingleton<IAgentTaskLaunchSink>(config.Mode == "dispatch-running"
            ? new CapturingTaskLaunchSink(config.LaunchSpecPath!) : new RefusingTaskLaunchSink());
        services.AddSingleton<LandDeliveryBoundary>(new ProbeDispatchBoundary(
            config.ConnectionString, config.MarkerPath, config.Barrier, config.DeliveryId));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
            .TickAsync(CancellationToken.None);
        if (config.Mode == "dispatch-running")
        {
            await File.WriteAllTextAsync(config.MarkerPath, "dispatch-ready");
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
    }
    else if (config.Mode == "admit")
    {
        var service = new ChannelOutboundService(db, files, new RefusingProducer(), profiles, clock);
        if (config.Barrier is not null) service.ProbeBarrierAsync = barrier;
        await service.SendAsync(new ChannelReply
        {
            Channel = "fake", ConversationId = config.ChannelId.ToString("N"),
            ReplyHandle = "thread-1", Text = "admission-frozen-source",
            Attachments = [new OutboundAttachment
            {
                Kind = AttachmentKind.File, Name = "source.md", Mime = "text/markdown",
                Content = "# crash source"u8.ToArray(),
            }],
        }, ChannelOutboundOrigin.AgentReply,
            new ChannelOutboundSource(config.SessionId, 1, 2, 3, "main", [config.CorrelationId]),
            CancellationToken.None);
    }
    else
    {
        OutboundConversionTaskRunner? runner = null;
        if (config.Mode == "prepare")
        {
            var tasks = new AgentTaskService(db,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                Options.Create(new DelegationSettings { AllowedRoots = [config.WorkspaceRoot] }),
                new ProbeEventBus(), new RefusingSessionStopper(), clock,
                NullLogger<AgentTaskService>.Instance);
            runner = new OutboundConversionTaskRunner(db, tasks);
            if (config.Barrier is not null) runner.ProbeBarrierAsync = barrier;
        }
        using var kafka = config.Mode == "broker"
            ? new KafkaAntiphonMessagingProducer(Options.Create(new AntiphonMessagingOptions
            {
                BootstrapServers = config.BootstrapServers!, OutboundTopic = config.Topic!,
            })) : null;
        var pump = new ChannelOutboundDeliveryPump(db, runner!, files,
            config.Mode == "prepare" && !config.AllowPublication
                ? new RefusingProducer() : (IAntiphonMessagingProducer?)kafka
                    ?? new EvidenceProducer(config.EvidencePath),
            Options.Create(new AntiphonMessagingOptions()), clock,
            NullLogger<ChannelOutboundDeliveryPump>.Instance, profiles);
        if (config.Barrier is not null) pump.ProbeBarrierAsync = barrier;
        await pump.TickAsync(CancellationToken.None);
        if (config.FailPublishCommit)
            await barrier("commit-failed", config.DeliveryId, CancellationToken.None);
    }
    return 0;
}
catch (Exception ex)
{
    // The config contains a database password. Never include exception messages
    // or the config contents in the child protocol/output.
    Console.Error.WriteLine(ex is FileNotFoundException missing
        ? $"{ex.GetType().Name}: {Path.GetFileName(missing.FileName)}"
        : ex.GetType().Name);
    return 1;
}

internal sealed record ProbeConfig(string ConnectionString, string StoreRoot, Guid DeliveryId,
    string EvidencePath, string MarkerPath, string? Barrier, int ClockOffsetSeconds,
    string? Mode = null, string WorkspaceRoot = "", Guid ProjectId = default,
    Guid ConverterAgentId = default, Guid ChannelId = default, Guid SessionId = default,
    Guid CorrelationId = default, bool AllowPublication = false,
    bool FailPublishCommit = false, string? BootstrapServers = null, string? Topic = null,
    string? WorkerExe = null, string? WorkerHome = null, string? WorkerGate = null,
    string? LaunchSpecPath = null);

internal sealed class ProbeClock(int offsetSeconds) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow().AddSeconds(offsetSeconds);
}

internal sealed class EvidenceProducer(string path) : IAntiphonMessagingProducer
{
    public async Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(reply, Antiphon.Messaging.MessagingJson.Options);
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.WriteThrough);
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        stream.Flush(flushToDisk: true);
    }
}

internal sealed class RefusingProducer : IAntiphonMessagingProducer
{
    public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unexpected external publication from an admission/worker probe.");
}

internal sealed class RefusingCommitInterceptor(string markerPath) : DbTransactionInterceptor
{
    public override InterceptionResult TransactionCommitting(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
    {
        File.WriteAllText(markerPath, "before-commit");
        throw new IOException("Injected database commit refusal after broker acceptance.");
    }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default)
    {
        File.WriteAllText(markerPath, "before-commit");
        return ValueTask.FromException<InterceptionResult>(
            new IOException("Injected database commit refusal after broker acceptance."));
    }
}

internal sealed class ProbeEventBus : IEventBus
{
    public Task PublishToGroupAsync(string group, string eventName, object payload,
        CancellationToken ct = default) => Task.CompletedTask;
    public Task PublishToAllAsync(string eventName, object payload,
        CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class RefusingSessionStopper : IDelegateSessionStopper
{
    public Task KillAsync(Guid sessionId, CancellationToken ct) =>
        throw new InvalidOperationException("Unexpected external session stop from conversion probe.");
    public Task KillAsync(Guid sessionId, SessionTerminationSource source, CancellationToken ct) =>
        throw new InvalidOperationException("Unexpected external session stop from conversion probe.");
}

internal sealed class RefusingTaskLaunchSink : IAgentTaskLaunchSink
{
    public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
        throw new InvalidOperationException("Unexpected external worker launch from crash probe.");
}

internal sealed class CapturingTaskLaunchSink(string path) : IAgentTaskLaunchSink
{
    public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec)
    {
        var staged = path + ".tmp";
        File.WriteAllText(staged, JsonSerializer.Serialize(new CapturedLaunch(sessionId, agentId,
            acceptedGeneration, spec)));
        File.Move(staged, path);
    }
}

internal sealed record CapturedLaunch(Guid SessionId, Guid AgentId,
    DateTime AcceptedGeneration, AgentLaunchSpec Spec);

internal sealed class ProbeDispatchBoundary(string connectionString, string markerPath,
    string? barrier, Guid deliveryId)
    : LandDeliveryBoundary
{
    public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
    {
        if (barrier != "conversion-dispatched" || boundary != "dispatch-warning-claim-committed")
            return;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString).Options);
        if (!await db.AgentTasks.AnyAsync(t => t.Id == taskId && t.OutboundDeliveryId == deliveryId, ct))
            return;
        await using (var stream = new FileStream(markerPath, FileMode.CreateNew,
                         FileAccess.Write, FileShare.Read, 1, FileOptions.WriteThrough))
        {
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
                $"{boundary}|{deliveryId:N}|{taskId:N}"), ct);
            stream.Flush(flushToDisk: true);
        }
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }
}
