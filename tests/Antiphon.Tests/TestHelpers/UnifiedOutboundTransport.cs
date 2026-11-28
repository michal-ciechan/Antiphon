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

// The broker may be shared by a whole test class (ChannelOutboundUnifiedTransportTests); isolation comes only from
// each matrix cell's own schema, topic (deleted on dispose), consumer group, gateway, native thread, and receipt
// ledger. A cell must not change broker-wide state.
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

    // warmProbes: how many probe children this world will start. Each is started ahead as a spare (ProbeSpare), the
    // first when the world is ready and each next one when the one before is handed its config, and warmed meanwhile
    // (runtime, compiled model, the Kafka native library, one read of the world's database), so its start overlaps the
    // parent's own work. It does nothing else until StartProbeAsync gives it its config. Each probe is still its own
    // fresh process, killed or recovering as before; only its start moves earlier. A spare is optional: one that cannot
    // start, take its config or warm up is replaced by a probe started then, as before.
    private int _sparesLeft;
    private (Process Process, string ConfigPath)? _spare;
    // A spare takes its config within its 10 ms poll (about 20 ms here). Far above 2x that on purpose: a handoff that
    // runs out ends in a kill and a probe started then, and only an acknowledgement racing that kill is an error
    // (HandToSpareAsync), so a briefly descheduled spare (GC, a loaded host) must not run into the bound.
    private static readonly TimeSpan SpareHandoff = TimeSpan.FromSeconds(5);

    private static ProcessStartInfo ProbeStart()
    {
        var start = new ProcessStartInfo("dotnet")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        // These one-shot probe children reach a cut or finish a few cycles before exiting.
        start.Environment["DOTNET_JitMinOpts"] = "1";
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe", "Antiphon.ChannelOutbound.Probe.dll"));
        return start;
    }

    private async Task StartSpareAsync()
    {
        var root = Path.Combine(H.TempRoot, "spare-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(root + ".warm.json", JsonSerializer.Serialize(new
        {
            ConnectionString = Schema.ConnectionString, CompiledModelPath = await ProbeGeneratedModel.PathAsync(),
            BootstrapServers = Bootstrap, Topic,
        }));
        var start = ProbeStart();
        start.ArgumentList.Add("--spare");
        start.ArgumentList.Add(root + ".warm.json");
        start.ArgumentList.Add(root + ".config");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _spare = (Process.Start(start) ?? throw new InvalidOperationException("Spare probe did not start"), root + ".config");
    }

    // The next spare, when this world still has a probe to start; never a failure of the probe already running.
    private async Task TryStartSpareAsync()
    {
        if (_sparesLeft <= 0 || _spare is not null) return;
        _sparesLeft--;
        try { await StartSpareAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // StartSpareAsync sets _spare only once its process has started, so nothing is left to stop here.
            Console.WriteLine("c519-probe spare-not-started " + ex.GetType().Name);
        }
    }

    // The config, written whole and then named, and the spare's acknowledgement: it writes <config>.taken before it runs
    // anything. Taken, the spare is the probe (running, or exited on its own, which its OwnedProbe reports). Not taken
    // within SpareHandoff, the spare is killed and its exit confirmed; still not taken, it never started anything and
    // false starts a probe now. Taken after all, or a kill not confirmed, is an ambiguous handoff (a spare that may
    // have begun the cut): an error, never a second probe on one cut.
    private async Task<bool> HandToSpareAsync((Process Process, string ConfigPath) spare, string json)
    {
        var taken = spare.ConfigPath + ".taken";
        if (!spare.Process.HasExited)
        {
            await File.WriteAllTextAsync(spare.ConfigPath + ".tmp", json);
            File.Move(spare.ConfigPath + ".tmp", spare.ConfigPath);
            var waited = Stopwatch.StartNew();
            while (waited.Elapsed < SpareHandoff && !spare.Process.HasExited && !File.Exists(taken))
                await Task.Delay(5);
        }
        if (File.Exists(taken))
        {
            _spare = null;
            return true;
        }
        var exit = spare.Process.HasExited ? spare.Process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "running";
        _spare = null;
        var (stopped, phase) = await StopAsync(spare);
        Console.WriteLine("c519-probe spare-lost exit=" + exit + " " + phase);
        if (!stopped || File.Exists(taken))
            throw new InvalidOperationException($"Ambiguous spare handoff (stopped {stopped}, taken {File.Exists(taken)}): "
                + "a spare may have begun the cut, so no second probe is started for it.");
        return false;
    }

    // A spare whose exit cannot be confirmed is a failure: its process may still hold this world's schema.
    private async Task StopSpareAsync()
    {
        if (_spare is not { } spare) return;
        _spare = null;
        var (stopped, phase) = await StopAsync(spare);
        if (!stopped) throw new InvalidOperationException("Spare probe did not exit after its kill " + phase);
    }

    // Kills the spare and confirms its exit (a killed child exits within a second here; the 10 s bound only stops a
    // stuck kill holding up the world), then reads its last phase line (types only, ProbeLog's contract).
    private static async Task<(bool Stopped, string Phase)> StopAsync((Process Process, string ConfigPath) spare)
    {
        var stopped = false;
        var phase = "";
        try
        {
            try
            {
                if (!spare.Process.HasExited) spare.Process.Kill(entireProcessTree: true);
                await spare.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                stopped = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                stopped = spare.Process.HasExited;
                if (!stopped) Console.WriteLine("c519-probe spare-not-stopped " + ex.GetType().Name);
            }
            if (stopped)
            {
                var read = spare.Process.StandardError.ReadToEndAsync();
                _ = read.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                try
                {
                    var stderr = await read.WaitAsync(TimeSpan.FromSeconds(2));
                    phase = stderr.Split((char)10).LastOrDefault(l => l.Contains("phase ", StringComparison.Ordinal))?.Trim() ?? "";
                }
                catch (TimeoutException) { phase = "phase unread"; }
            }
        }
        finally { spare.Process.Dispose(); }
        return (stopped, phase);
    }
    public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public static async Task<UnifiedOutboundTransport> CreateAsync(RedpandaContainer broker,
        bool gatewayStarted = true, Action<IServiceCollection>? configure = null,
        Action<DbContextOptionsBuilder>? configureDb = null, int? maxMessageBytes = null,
        TimeProvider? clock = null, int warmProbes = 0, int? postEvidenceSettleMs = null)
    {
        using var timing = TestTrace.ChunkSpan("C519 create world");
        await AwaitEarlierTeardownsAsync();
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
        var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            TimeProvider = clock,
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = true },
            Bridge = new ChannelBridgeSettings
            { Enabled = true, DebounceWindowMs = 0, MachineTurnTextOrigins = [QueuedMessageOrigin.Check] },
            ConfigureDbContext = configureDb,
            ConfigureDeliveryVerification = postEvidenceSettleMs is int settle
                ? verification => verification.PostEvidenceSettleMs = settle : null,
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
        world._sparesLeft = warmProbes;
        await world.TryStartSpareAsync();
        return world;
    }

    public async Task StartGatewayAsync()
    {
        if (_gatewayStarted) return;
        using var timing = TestTrace.ChunkSpan("C519 start gateway");
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
        using var timing = TestTrace.ChunkSpan("C519 assert receipt");
        using (TestTrace.ChunkSpan("C519 wait for receipt"))
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
        string mode = "unified", Guid? deliveryId = null, string? stallPoint = null)
    {
        var marker = Path.Combine(H.TempRoot, "cut-" + Guid.NewGuid().ToString("N") + ".json");
        var config = marker + ".config";
        var source = ProbeSource;
        // The compiled model this probe build generated (ProbeGeneratedModel), or none: the probe then builds it.
        var model = await ProbeGeneratedModel.PathAsync();
        var json = JsonSerializer.Serialize(ProbeConfigFor(marker, barrier, offset, refuseOutcome, mode, deliveryId, stallPoint, model));
        Process? process = null;
        if (_spare is { } spare && await HandToSpareAsync(spare, json)) process = spare.Process;
        var warmed = process is not null;
        if (process is null)
        {
            await File.WriteAllTextAsync(config, json);
            var start = ProbeStart();
            start.ArgumentList.Add(config);
            process = Process.Start(start) ?? throw new InvalidOperationException("Probe did not start");
        }
        var owned = new OwnedProbe(process, marker, source, warmed);
        await TryStartSpareAsync();
        return owned;
    }

    private static string ProbeSource => typeof(UnifiedOutboundTransport).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+').Last();

    private ProbeConfig ProbeConfigFor(string marker, string? barrier, int offset, bool refuseOutcome, string mode,
        Guid? deliveryId, string? stallPoint, string? model) => new(
        ConnectionString: Schema.ConnectionString, StoreRoot: StoreRoot, DeliveryId: deliveryId ?? H.SessionId,
        EvidencePath: marker + ".unused", MarkerPath: marker, Barrier: barrier, ClockOffsetSeconds: offset,
        Mode: mode, WorkspaceRoot: H.TempRoot, ProjectId: ProjectId, ConverterAgentId: ConverterId,
        FailPublishCommit: refuseOutcome, BootstrapServers: Bootstrap, Topic: Topic, ExpectedSourceSha: ProbeSource,
        StallPoint: stallPoint, DeadlineSeconds: (int)ProbeDeadline.TotalSeconds, CompiledModelPath: model);

    // A recovery's clock: past every lease the parent or a dead child holds (300 s, or 60 s with a converter).
    private int RecoveryOffset(int? offset) => offset ?? (ConverterId == Guid.Empty ? 600 : 120);

    // An in-process recovery's own deadline. Measured on one host only (HP laptop on battery beside a landing run,
    // 2026-11-22: p50 331, p95 1278, max 2810 ms, n=328; the max is a first recovery in the process, which also pays
    // the graph's JIT and EF warm-up). Not 2x p95: server2 and Windows Full are unmeasured and run other tests in
    // the same process, so it keeps about 5x that max until they are measured; a stall still fails in half the
    // child's 30 s. A first recovery in process (FirstRecoveryAsync) has the child's whole ProbeDeadline instead.
    private static readonly TimeSpan InProcessRecoveryDeadline = TimeSpan.FromSeconds(15);

    // A restart's recovery: the probe's own unified recovery graph (UnifiedRecoveryProbe: its own services, clock,
    // producer and store over the durable rows and files only; no parent event or in-memory signal reaches it), run
    // in this process. The same host and code as a recovery child, on this process's runtime EF model (a child loads
    // the compiled model ProbeModelComparison pins to it), without a child's runtime start, JIT and EF warm-up (about
    // 6 s of each 6-7 s recovery child on the HP laptop). Every death is still a real killed child (StartProbeAsync);
    // the first recovery after a death or an operator retry is a fresh child for the main kind (FirstRecoveryAsync).
    public Task RecoverAsync(int? offset = null) => RecoverInProcessAsync(offset, InProcessRecoveryDeadline, "in-process");

    private async Task RecoverInProcessAsync(int? offset, TimeSpan deadline, string label)
    {
        using var timing = TestTrace.ChunkSpan("C519 recover in process");
        var started = Stopwatch.StartNew();
        var marker = Path.Combine(H.TempRoot, "recover-" + Guid.NewGuid().ToString("N") + ".json");
        var config = ProbeConfigFor(marker, null, RecoveryOffset(offset), false, "unified", null, null, null)
            with { DeadlineSeconds = (int)deadline.TotalSeconds };
        int code;
        try
        {
            code = await UnifiedRecoveryProbe.RunAsync(config);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // As a child reports (its config holds the database password): exception types and frames, no messages.
            throw new InvalidOperationException($"In-process recovery failed after {started.ElapsedMilliseconds} ms: "
                + ProbeLog.Describe(ex));
        }
        code.ShouldBe(0);
        Console.WriteLine($"c519-probe recover {label} {started.ElapsedMilliseconds} ms");
    }

    // The first recovery after a real child death (crash tests) or an operator retry (crash and transport tests), for
    // a test run once per source kind: a fresh child for the main kind, and for every kind where the caller asks
    // (coldForEveryKind: the attempt-death test's first cut, so each kind's uncertain marking, failure recorder and
    // retry republish also run in a fresh process on the compiled model); otherwise in process. Every cut keeps a
    // cross-process recovery when the main row runs; the kind changes the source a recovery publishes, which the
    // in-process recovery runs on the same code. In process it keeps the child's whole ProbeDeadline: right after a
    // kill it may wait for Postgres to end the dead child's backend and its locks, which a child's own start hid.
    // The kind is checked first, so a new kind is refused before any recovery runs.
    public async Task FirstRecoveryAsync(string kind, bool coldForEveryKind = false)
    {
        if (kind is not ("main" or "trailing" or "machine"))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A C519 source kind is main, trailing or machine.");
        if (kind == "main" || coldForEveryKind) await ChildRecoverAsync();
        else await RecoverInProcessAsync(null, ProbeDeadline, "first-in-process");
    }

    // A recovery in a fresh child process (FirstRecoveryAsync decides when).
    public async Task ChildRecoverAsync(int? offset = null)
    {
        using var timing = TestTrace.ChunkSpan("C519 recover in child");
        await using var child = await StartProbeAsync(null, RecoveryOffset(offset));
        await child.CompleteAsync();
    }

    // The child's own deadline, counted from its run start. The parent's reach wait adds the
    // child's runtime startup and orderly exit, so a stalled child always ends first and its
    // stderr phase log reaches the failure. Run d88de419 g29 failed a 30 s parent poll that
    // equalled this deadline and so expired first, leaving only TaskCanceledException.
    public static readonly TimeSpan ProbeDeadline = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ProbeStartupAndExit = TimeSpan.FromSeconds(30);

    public static async Task WaitForAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!predicate()) await Task.Delay(25, timeout.Token);
    }

    // A world's teardown after its spare (the gateway's stop, the bridge harness, the schema, the store and the topic:
    // about 0.6 s a world on the HP laptop on AC) runs in the background, so the next world's setup overlaps it. The
    // spare, a process, is stopped (and its exit confirmed) before DisposeAsync returns, so probe children never
    // overlap across worlds; a spare that cannot be stopped fails the teardown, which still runs. Each world keeps its
    // own schema, topic, Slack fake and gateway, so a teardown still running touches nothing the next one uses, and a
    // new world first waits for every teardown but the latest (AwaitEarlierTeardownsAsync), so at most one overlaps it:
    // this test's own failure is raised there, two worlds later, naming the failed world, or else at the test's drain.
    // Every class that builds worlds drains its test's own teardowns after each test (DrainDisposalsAsync in an
    // After(Test) hook), and before the shared broker can go waits for every teardown still listed, reporting only those
    // no test owns (DrainAllDisposalsAsync in After(Class), which runs outside the process limit beside the other C519
    // class's tests): no world outlives its test, and a teardown failure fails the test that made it.
    // Timings a world records (reach, recovery) may include one earlier world's teardown running beside them.
    private static readonly object TeardownGate = new();
    private static readonly List<(string Owner, string World, Task Teardown)> Teardowns = [];
    private int _disposed;

    private static string CurrentTest => TUnit.Core.TestContext.Current?.Metadata.TestDetails.TestId ?? "";

    // A teardown waited for this long has hung: Gateway.StopAsync's own 30 s bound and the topic delete's 15 s are the
    // only long steps, and a normal teardown takes about 0.6 s.
    private static readonly TimeSpan TeardownHung = TimeSpan.FromMinutes(2);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        Exception? spareFailure = null;
        try { await StopSpareAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { spareFailure = ex; }
        var world = Topic;
        var teardown = Task.Run(async () =>
        {
            Exception? teardownFailure = null;
            try { await DisposeNowAsync(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { teardownFailure = ex; }
            Exception[] failures = [.. new[] { spareFailure, teardownFailure }.OfType<Exception>()];
            if (failures.Length > 0)
                throw new InvalidOperationException($"World {world} teardown failed",
                    failures.Length == 1 ? failures[0] : new AggregateException(failures));
        });
        lock (TeardownGate) Teardowns.Add((CurrentTest, world, teardown));
    }

    private static async Task AwaitTeardownAsync((string Owner, string World, Task Teardown) entry)
    {
        try { await entry.Teardown.WaitAsync(TeardownHung); }
        catch (TimeoutException) { throw new TimeoutException($"World {entry.World} teardown still running after {TeardownHung.TotalMinutes:0} min"); }
    }

    // Before a world is built: every teardown but the latest has ended (that one may overlap the new world). This test's
    // own failure is raised (and taken off the list); another test's is left for that test's drain.
    private static async Task AwaitEarlierTeardownsAsync()
    {
        (string Owner, string World, Task Teardown)[] earlier;
        lock (TeardownGate) earlier = [.. Teardowns.Take(Math.Max(0, Teardowns.Count - 1))];
        var current = CurrentTest;
        foreach (var entry in earlier)
        {
            try { await AwaitTeardownAsync(entry); }
            catch (Exception ex) when (ex is not OutOfMemoryException && entry.Owner != current) { continue; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                lock (TeardownGate) Teardowns.Remove(entry);
                throw;
            }
            lock (TeardownGate) Teardowns.Remove(entry);
        }
    }

    // After each test: this test's own teardowns, every failure collected.
    public static Task DrainDisposalsAsync()
    {
        var current = CurrentTest;
        return DrainAsync(t => t.Owner == current);
    }

    // Before the shared broker can go (After(Class)): waits for every teardown still listed, then takes and reports only
    // those no test owns. Another test's (the other C519 class may be mid-test) stays listed for that test's drain.
    public static async Task DrainAllDisposalsAsync()
    {
        (string Owner, string World, Task Teardown)[] listed;
        lock (TeardownGate) listed = [.. Teardowns];
        foreach (var entry in listed)
        {
            try { await AwaitTeardownAsync(entry); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
        await DrainAsync(t => t.Owner == "");
    }

    private static async Task DrainAsync(Func<(string Owner, string World, Task Teardown), bool> select)
    {
        (string Owner, string World, Task Teardown)[] taken;
        lock (TeardownGate)
        {
            taken = Teardowns.Where(select).ToArray();
            Teardowns.RemoveAll(t => select(t));
        }
        var failures = new List<Exception>();
        foreach (var entry in taken)
        {
            try { await AwaitTeardownAsync(entry); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { failures.Add(ex); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("World teardowns failed", failures);
    }

    private async Task DisposeNowAsync()
    {
        using var timing = TestTrace.ChunkSpan("C519 dispose world");
        try
        {
            if (_gatewayStarted) await Gateway.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
            Gateway.Dispose(); Producer.Dispose(); Http.Dispose();
            await Slack.DisposeAsync(); await H.DisposeAsync(); await Schema.DisposeAsync();
            if (Directory.Exists(StoreRoot)) Directory.Delete(StoreRoot, true);
        }
        finally
        {
            await DeleteTopicAsync();
        }
    }

    // A broker shared by a class keeps no cell's topic (a dev-mode broker has a bounded partition budget), even when
    // the cell's other disposal failed. Best effort and bounded: a broker that is gone or slow leaves the topic to the
    // container's disposal, and an abandoned delete is observed.
    private async Task DeleteTopicAsync()
    {
        // The client is disposed inside the task, after its delete returns, never under a pending call.
        var deleting = Task.Run(async () =>
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = Bootstrap }).Build();
            await admin.DeleteTopicsAsync([Topic], new DeleteTopicsOptions
                { RequestTimeout = TimeSpan.FromSeconds(10), OperationTimeout = TimeSpan.FromSeconds(10) });
        });
        _ = deleting.ContinueWith(attempt => _ = attempt.Exception,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        try
        {
            await deleting.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Visible, so a broker that keeps leaking topics can be told from one that fails to create them.
            Console.WriteLine("C519_TOPIC_DELETE_FAILED " + Topic + " " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // warmed: a spare started ahead (its age counts from its handoff), else started now; the timing lines say which.
    internal sealed class OwnedProbe(Process process, string marker, string source, bool warmed = false) : IAsyncDisposable
    {
        private readonly Task<string> _stdout = process.StandardOutput.ReadToEndAsync();
        private readonly Task<string> _stderr = process.StandardError.ReadToEndAsync();
        private readonly string _nativeStart = ReadNativeStart(process);
        private readonly Stopwatch _age = Stopwatch.StartNew();
        private static string ReadNativeStart(Process child)
        {
            if (OperatingSystem.IsWindows()) return child.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var stat = File.ReadAllText($"/proc/{child.Id}/stat");
            return stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[19];
        }
        public async Task ReachAsync(string point)
        {
            var budget = ProbeDeadline + ProbeStartupAndExit;
            while (!File.Exists(marker) && !process.HasExited)
            {
                if (_age.Elapsed > budget) throw new TimeoutException(await StallReportAsync(point, budget));
                await Task.Delay(25);
            }
            if (process.HasExited)
                throw new InvalidOperationException($"Child exited before cut {point} after {_age.ElapsedMilliseconds} ms"
                    + $" (exit {process.ExitCode}): " + await _stderr);
            await AssertMarkerAsync(point);
            // One line per cut so Full-run TRX stdout yields the reach p95 for this budget.
            Console.WriteLine($"c519-probe reach {point} {_age.ElapsedMilliseconds} ms {(warmed ? "warm" : "cold")}");
        }
        private async Task<string> StallReportAsync(string point, TimeSpan budget)
        {
            var staged = File.Exists(marker + ".tmp");
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await Task.WhenAny(Task.WhenAll(process.WaitForExitAsync(), _stdout, _stderr), Task.Delay(TimeSpan.FromSeconds(10)));
            static string Text(Task<string> stream) => stream.IsCompletedSuccessfully ? stream.Result : "<unavailable>";
            return $"Child neither reached {point} nor exited within {budget.TotalSeconds:F0} s"
                + $" ({_age.ElapsedMilliseconds} ms, staged marker {staged}); it was killed."
                + $"{Environment.NewLine}stderr:{Environment.NewLine}{Text(_stderr)}"
                + $"stdout:{Environment.NewLine}{Text(_stdout)}";
        }
        public async Task AssertMarkerAsync(string point)
        {
            using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(marker));
            receipt.RootElement.GetProperty("point").GetString().ShouldBe(point);
            receipt.RootElement.GetProperty("pid").GetInt32().ShouldBe(process.Id);
            receipt.RootElement.GetProperty("mvid").GetGuid().ShouldNotBe(Guid.Empty);
            receipt.RootElement.GetProperty("expectedSourceSha").GetString().ShouldBe(source);
            receipt.RootElement.GetProperty("build").GetString()!.ShouldContain(source);
            // Linux Process.StartTime reconstructs UTC from a sampled uptime. The kernel's
            // exact start tick is stable across these independent parent/child observations.
            receipt.RootElement.GetProperty("nativeStart").GetString().ShouldBe(_nativeStart);
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
            var budget = ProbeDeadline + ProbeStartupAndExit;
            try { await process.WaitForExitAsync().WaitAsync(budget - _age.Elapsed); }
            catch (TimeoutException) { throw new TimeoutException(await StallReportAsync("recovery completion", budget)); }
            Console.WriteLine($"c519-probe complete {_age.ElapsedMilliseconds} ms {(warmed ? "warm" : "cold")}");
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
