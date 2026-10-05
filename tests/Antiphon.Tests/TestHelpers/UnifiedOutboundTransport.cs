using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Gateway;
using Antiphon.Messaging.Slack;
using Antiphon.Messaging.Tests.FakeSlack;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.SessionRunner.Contracts;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.Redpanda;

namespace Antiphon.Tests.TestHelpers;

// The broker belongs to the test method; every matrix cell has its own schema, topic,
// consumer group, gateway, native thread, and receipt ledger.
internal sealed class UnifiedOutboundTransport : IAsyncDisposable
{
    public required BridgeQueueHarness H { get; init; }
    public required IsolatedTestSchema Schema { get; init; }
    public required FakeSlackServer Slack { get; init; }
    public required HttpClient Http { get; init; }
    public required GatewayOutboundService Gateway { get; init; }
    public required KafkaAntiphonMessagingProducer Producer { get; init; }
    public required string Bootstrap { get; init; }
    public required string Topic { get; init; }
    public required string StoreRoot { get; init; }
    public required string Conversation { get; init; }
    public required Guid ChannelId { get; init; }
    public string Thread => "1700000000.000519";
    public string Key => "slack:" + Conversation;
    public string SourcePath => Path.Combine(H.TempRoot, "workspace", "original.md");
    public byte[] OriginalBytes { get; } = "# frozen original S12 source\nwith complete middle and tail"u8.ToArray();
    public string Answer { get; } = "original complete reply " + Guid.NewGuid().ToString("N");
    public Guid MemberId { get; set; }
    public Guid? RootId { get; set; }
    public int BaselineReceipts { get; set; }
    public Guid ConverterId { get; set; }
    public Guid ProjectId { get; set; }
    private bool _gatewayStarted;
    public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public static async Task<UnifiedOutboundTransport> CreateAsync(RedpandaContainer broker,
        bool gatewayStarted = true, Action<IServiceCollection>? configure = null,
        Action<DbContextOptionsBuilder>? configureDb = null, int? maxMessageBytes = null)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var topic = "c519-" + Guid.NewGuid().ToString("N");
        using (var admin = new AdminClientBuilder(new AdminClientConfig
            { BootstrapServers = broker.GetBootstrapAddress() }).Build())
            await admin.CreateTopicsAsync([new TopicSpecification
            {
                Name = topic, NumPartitions = 1, ReplicationFactor = 1,
                Configs = maxMessageBytes is int max ? new() { ["max.message.bytes"] = max.ToString() } : new(),
            }]).WaitAsync(TimeSpan.FromSeconds(30));
        var slack = new FakeSlackServer();
        await slack.StartAsync();
        var http = new HttpClient();
        var adapter = new SlackChannelAdapter(http, new SlackSettings
        {
            ApiBaseUrl = slack.ApiBaseUrl, BotToken = slack.BotToken, AppToken = slack.AppToken,
            ErrorBackoffSeconds = 0,
        }, NullLogger<SlackChannelAdapter>.Instance);
        var gateway = new GatewayOutboundService([adapter], Options.Create(new AntiphonGatewayOptions
        {
            BootstrapServers = broker.GetBootstrapAddress(), OutboundTopic = topic,
            ConsumerGroup = topic + "-recipient", AutoOffsetReset = "Earliest",
        }), NullLogger<GatewayOutboundService>.Instance);
        var producer = new KafkaAntiphonMessagingProducer(Options.Create(new AntiphonMessagingOptions
            { BootstrapServers = broker.GetBootstrapAddress(), OutboundTopic = topic }));
        var storeRoot = Path.Combine(Path.GetTempPath(), "c519-store-" + Guid.NewGuid().ToString("N"));
        var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = true },
            Bridge = new ChannelBridgeSettings
            { Enabled = true, DebounceWindowMs = 0, MachineTurnTextOrigins = [QueuedMessageOrigin.Check] },
            ConfigureDbContext = configureDb,
            ConfigureServices = services =>
            {
                services.AddSingleton<IAntiphonMessagingProducer>(producer);
                services.AddSingleton<IChannelOutboundFileStore>(new ChannelOutboundFileStore(storeRoot));
                services.AddSingleton<ChannelOutboundDiscoveryService>();
                configure?.Invoke(services);
            },
        });
        var channelId = Guid.NewGuid();
        var conversation = "C" + Guid.NewGuid().ToString("N");
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var project = new Project { Id = Guid.NewGuid(), Name = topic, CreatedAt = h.Now, UpdatedAt = h.Now };
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = topic, CreatedAt = h.Now, UpdatedAt = h.Now };
            db.Projects.Add(project); db.Boards.Add(board);
            await db.SaveChangesAsync();
            await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.BoardId, board.Id));
            db.ChatChannels.Add(new ChatChannel
            {
                Id = channelId, Provider = "slack", ExternalId = conversation,
                ReplyHandle = conversation + "|1700000000.000519", AgentId = h.AgentId,
                Enabled = true, CreatedAt = h.Now, UpdatedAt = h.Now,
            });
            await db.SaveChangesAsync();
        }
        var world = new UnifiedOutboundTransport
        {
            H = h, Schema = schema, Slack = slack, Http = http, Gateway = gateway, Producer = producer,
            Bootstrap = broker.GetBootstrapAddress(), Topic = topic, StoreRoot = storeRoot,
            Conversation = conversation, ChannelId = channelId,
        };
        await File.WriteAllBytesAsync(world.SourcePath, world.OriginalBytes);
        if (gatewayStarted) await world.StartGatewayAsync();
        return world;
    }

    public async Task StartGatewayAsync()
    {
        if (_gatewayStarted) return;
        await Gateway.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        _gatewayStarted = true;
    }

    public async Task SeedCrashSourceAsync(string kind)
    {
        if (kind == "machine")
        {
            var context = await H.SeedChannelCorrelationAsync("prior channel context", Key);
            await using var db = Db();
            await db.SessionQueuedMessages.Where(m => m.Id == context)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ChannelReplySettledAt, H.Now));
            MemberId = await H.SeedPendingMessageAsync("[Check] machine complete prompt", status: QueuedMessageStatus.Sent,
                origin: QueuedMessageOrigin.Check, deliveryAttempts: 1, baselineSequence: 0,
                createdAtUtc: H.Now, lastDeliveryStartedAt: H.Now, legacyNullGeneration: true);
            await H.InsertTurnAsync("[Check] machine complete prompt", Answer + "\n[[attach: " + SourcePath + "]]");
        }
        else
        {
            MemberId = await H.SeedChannelCorrelationAsync("complete source prompt", Key);
            await H.InsertTurnAsync("complete source prompt", kind == "trailing" ? "initial reply" : Answer + "\n[[attach: " + SourcePath + "]]");
            if (kind == "trailing")
            {
                await H.Dispatcher.OnTurnEndAsync(H.SessionId, default);
                await H.DrainOutboundAsync();
                await WaitForAsync(() => Slack.SentMessages.Count == 1);
                await using var db = Db();
                RootId = (await db.ChannelOutboundDeliveries.SingleAsync()).Id;
                BaselineReceipts = 1;
                await H.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, Answer + "\n[[attach: " + SourcePath + "]]");
            }
        }
    }

    public async Task<ChannelOutboundDelivery?> DeliveryAsync(string kind)
    {
        await using var db = Db();
        return await db.ChannelOutboundDeliveries.AsNoTracking().SingleOrDefaultAsync(d => d.SendKind == kind);
    }

    public async Task AssertReceiptAsync(int copies = 1, string? expectedText = null)
    {
        await WaitForAsync(() => Slack.SentMessages.Count == BaselineReceipts + copies
            && Slack.UploadedFiles.Count == copies);
        foreach (var message in Slack.SentMessages.Skip(BaselineReceipts))
        {
            message.Channel.ShouldBe(Conversation); message.ThreadTs.ShouldBe(Thread);
            message.Text.ShouldBe(expectedText ?? Answer);
        }
        foreach (var file in Slack.UploadedFiles)
        {
            file.Title.ShouldBe("original.md"); file.ThreadTs.ShouldBe(Thread);
            file.Bytes.ShouldBe(OriginalBytes);
        }
        // Flush independent parent observations before a child death, not producer counters.
        await using var ledger = new FileStream(Path.Combine(H.TempRoot, "recipient.json"), FileMode.Create,
            FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(ledger, new { MemberId, RootId, H.SessionId, Conversation, Thread,
            Messages = Slack.SentMessages, Files = Slack.UploadedFiles });
        ledger.Flush(true);
    }

    public async Task<OwnedProbe> StartProbeAsync(string? barrier, int offset = 0, bool refuseOutcome = false,
        string mode = "unified", Guid? deliveryId = null)
    {
        var marker = Path.Combine(H.TempRoot, "cut-" + Guid.NewGuid().ToString("N") + ".json");
        var config = marker + ".config";
        var source = typeof(UnifiedOutboundTransport).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+').Last();
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new
        {
            ConnectionString = Schema.ConnectionString, StoreRoot, DeliveryId = deliveryId ?? H.SessionId,
            EvidencePath = marker + ".unused", MarkerPath = marker, Barrier = barrier,
            ClockOffsetSeconds = offset, Mode = mode, BootstrapServers = Bootstrap, Topic,
            FailPublishCommit = refuseOutcome, ExpectedSourceSha = source,
            ConverterAgentId = ConverterId, ProjectId, WorkspaceRoot = H.TempRoot,
        }));
        var start = new ProcessStartInfo("dotnet")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe", "Antiphon.ChannelOutbound.Probe.dll"));
        start.ArgumentList.Add(config);
        return new OwnedProbe(Process.Start(start) ?? throw new InvalidOperationException("Probe did not start"), marker, source);
    }

    public async Task RecoverAsync(int offset = 600)
    {
        await using var child = await StartProbeAsync(null, offset);
        await child.CompleteAsync();
    }

    public static async Task WaitForAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!predicate()) await Task.Delay(25, timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_gatewayStarted) await Gateway.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        Gateway.Dispose(); Producer.Dispose(); Http.Dispose();
        await Slack.DisposeAsync(); await H.DisposeAsync(); await Schema.DisposeAsync();
        if (Directory.Exists(StoreRoot)) Directory.Delete(StoreRoot, true);
    }

    internal sealed class OwnedProbe(Process process, string marker, string source) : IAsyncDisposable
    {
        private readonly Task<string> _stdout = process.StandardOutput.ReadToEndAsync();
        private readonly Task<string> _stderr = process.StandardError.ReadToEndAsync();
        public async Task ReachAsync(string point)
        {
            await WaitForAsync(() => File.Exists(marker) || process.HasExited);
            if (process.HasExited) throw new InvalidOperationException("Child exited before cut: " + await _stderr);
            await AssertMarkerAsync(point);
        }
        public async Task AssertMarkerAsync(string point)
        {
            using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(marker));
            receipt.RootElement.GetProperty("point").GetString().ShouldBe(point);
            receipt.RootElement.GetProperty("pid").GetInt32().ShouldBe(process.Id);
            receipt.RootElement.GetProperty("mvid").GetGuid().ShouldNotBe(Guid.Empty);
            receipt.RootElement.GetProperty("expectedSourceSha").GetString().ShouldBe(source);
            receipt.RootElement.GetProperty("build").GetString()!.ShouldContain(source);
            receipt.RootElement.GetProperty("startedAt").GetDateTime().ShouldBe(process.StartTime.ToUniversalTime());
        }
        public async Task KillAsync()
        {
            process.HasExited.ShouldBeFalse();
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(10));
        }
        public async Task CompleteAsync()
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(10));
            process.ExitCode.ShouldBe(0, await _stderr);
        }
        public async ValueTask DisposeAsync()
        {
            if (!process.HasExited) await KillAsync();
            process.Dispose();
        }
    }
}
