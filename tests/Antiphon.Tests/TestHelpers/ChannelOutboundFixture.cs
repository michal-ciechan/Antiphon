using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Tests.TestHelpers;

internal sealed class ScriptedOutboundProducer : IAntiphonMessagingProducer
{
    public string Mode { get; set; } = "accept";
    public string? RefuseConversationId { get; set; }
    public ConcurrentQueue<ChannelReply> Entered { get; } = new();
    private readonly Channel<ChannelReply> _entrySignals = Channel.CreateUnbounded<ChannelReply>();
    public ConcurrentQueue<ChannelReply> Accepted { get; } = new();
    public ConcurrentQueue<ChannelReply> Returned { get; } = new();
    public TaskCompletionSource<bool> HoldGate { get; set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int LiveHolds => Volatile.Read(ref _liveHolds);
    private int _liveHolds;
    public Task<ChannelReply> NextEntryAsync() =>
        _entrySignals.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

    public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
    {
        Entered.Enqueue(reply);
        _entrySignals.Writer.TryWrite(reply);
        if (reply.ConversationId == RefuseConversationId)
            throw new InvalidOperationException("scripted target refusal");
        if (Mode == "sync-refuse")
            throw new InvalidOperationException("scripted refusal");
        if (Mode == "definite-refuse")
            throw new ChannelPublicationRefusedException("MsgSizeTooLarge",
                new InvalidOperationException("scripted broker rejection"));
        if (Mode == "serialize")
            throw new JsonException("scripted serialization failure");
        if (Mode == "hold")
            return HoldAsync(reply, cancellationToken);
        return CompleteAsync(reply, cancellationToken);
    }

    private async Task HoldAsync(ChannelReply reply, CancellationToken ct)
    {
        Interlocked.Increment(ref _liveHolds);
        try
        {
            await HoldGate.Task.WaitAsync(ct);
            Accepted.Enqueue(reply);
            Returned.Enqueue(reply);
        }
        finally { Interlocked.Decrement(ref _liveHolds); }
    }

    private async Task CompleteAsync(ChannelReply reply, CancellationToken ct)
    {
        if (Mode == "async-refuse")
        {
            await Task.Yield();
            throw new InvalidOperationException("scripted refusal");
        }
        if (Mode == "cancel")
            throw new OperationCanceledException(ct);
        Accepted.Enqueue(reply);
        if (Mode == "accepted-lost")
            throw new OperationCanceledException(ct);
        Returned.Enqueue(reply);
    }
}

internal sealed class OutboundScanObserver : ChannelOutboundRecoveryObserver
{
    private readonly Channel<long> _passes = Channel.CreateUnbounded<long>();
    public override Task ScanCompletedAsync(long pass, CancellationToken ct)
    {
        _passes.Writer.TryWrite(pass);
        return Task.CompletedTask;
    }
    public Task<long> NextAsync(CancellationToken ct = default) => _passes.Reader.ReadAsync(ct).AsTask();
    public void Clear()
    {
        while (_passes.Reader.TryRead(out _)) { }
    }
}

internal sealed class OutboundRuntimeLogCapture : ILogger<AgentSessionRuntime>
{
    public ConcurrentQueue<string> Messages { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Enqueue(formatter(state, exception));
}

internal sealed class ChannelOutboundFixture : IAsyncDisposable
{
    private readonly IsolatedTestSchema _schema;
    private BridgeQueueHarness _harness;
    private readonly IAntiphonMessagingProducer _outboundProducer;
    private ChannelOutboundRecoveryWorker? _worker;
    private readonly List<string> _temporaryRoots = [];
    public ScriptedOutboundProducer Producer { get; } = new();
    public FakeTimeProvider Clock { get; } = new();
    public OutboundScanObserver Observer { get; } = new();
    public ChannelOutboundFaults Faults { get; } = new();
    public OutboundRuntimeLogCapture RuntimeLogs { get; }
    public Guid SessionId => _harness.SessionId;
    public Guid AgentId => _harness.AgentId;
    public string ConnectionString => _schema.ConnectionString;
    public BridgeQueueHarness Harness => _harness;
    public AppDbContext CreateContext() =>
        new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));

    private ChannelOutboundFixture(IsolatedTestSchema schema, BridgeQueueHarness harness,
        ScriptedOutboundProducer producer, IAntiphonMessagingProducer outboundProducer,
        FakeTimeProvider clock, OutboundScanObserver observer,
        ChannelOutboundFaults faults, OutboundRuntimeLogCapture runtimeLogs)
    {
        _schema = schema;
        _harness = harness;
        Producer = producer;
        _outboundProducer = outboundProducer;
        Clock = clock;
        Observer = observer;
        Faults = faults;
        RuntimeLogs = runtimeLogs;
        _temporaryRoots.Add(harness.TempRoot);
    }

    public static async Task<ChannelOutboundFixture> CreateAsync(
        IAntiphonMessagingProducer? outboundProducer = null)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var producer = new ScriptedOutboundProducer();
        var clock = new FakeTimeProvider();
        var observer = new OutboundScanObserver();
        var faults = new ChannelOutboundFaults();
        var runtimeLogs = new OutboundRuntimeLogCapture();
        var selectedProducer = outboundProducer ?? producer;
        var harness = await BridgeQueueHarness.CreateAsync(Options(schema.ConnectionString,
            selectedProducer, clock, observer, faults, runtimeLogs));
        return new ChannelOutboundFixture(schema, harness, producer, selectedProducer,
            clock, observer, faults, runtimeLogs);
    }

    private static BridgeQueueHarness.HarnessOptions Options(string connection,
        IAntiphonMessagingProducer producer, FakeTimeProvider clock, OutboundScanObserver observer,
        ChannelOutboundFaults faults, OutboundRuntimeLogCapture runtimeLogs,
        Guid? sessionId = null, Guid? agentId = null) => new()
    {
        ConnectionString = connection,
        PreserveDatabaseOnDispose = true,
        AttachSessionId = sessionId,
        AttachAgentId = agentId,
        ConfigureDbContext = options => options.AddInterceptors(
            faults.SaveInterceptor, faults.CommandInterceptor),
        Bridge = new ChannelBridgeSettings
        {
            Enabled = true, DebounceWindowMs = 0, OutboundScanSeconds = 30,
            OutboundRetrySeconds = 30, OutboundSendTimeoutSeconds = 30,
            OutboundAttemptLeaseSeconds = 90, OutboundMaxAttempts = 3,
            OutboundPageSize = 2,
        },
        ConfigureServices = services =>
        {
            services.AddSingleton<IAntiphonMessagingProducer>(producer);
            services.AddSingleton<ChannelOutboundRecoveryObserver>(observer);
            services.AddSingleton<ChannelOutboundBoundary>(faults);
            services.AddSingleton<ChannelAttachmentReader>(faults.AttachmentReader);
            services.AddSingleton(new ChannelOutboundClock(clock));
            services.AddSingleton<ILogger<AgentSessionRuntime>>(runtimeLogs);
        },
    };

    public async Task<(Guid SourceId, string Answer)> CompleteMainAsync()
    {
        var chat = await _harness.BindChannelAsync();
        const string prompt = "Please return the original middle and tail sentinels";
        const string answer = "Original middle sentinel; original tail sentinel.";
        var pdf = Path.Combine(_harness.TempRoot, "main-original.pdf");
        File.WriteAllBytes(pdf, "%PDF-1.4 original main bytes"u8.ToArray());
        var id = await _harness.SeedChannelCorrelationAsync(prompt, $"telegram:{chat}");
        await _harness.InsertTurnAsync(prompt, $"{answer}\n[[attach: {pdf}]]");
        return (id, answer);
    }

    public async Task<(Guid SourceId, string Answer, int Baseline)> CompleteSourceTurnAsync(string path)
    {
        if (path == "main")
        {
            var main = await CompleteMainAsync();
            return (main.SourceId, main.Answer, 0);
        }
        var chat = await _harness.BindChannelAsync();
        const string prompt = "Please start the original reply";
        var channelId = await _harness.SeedChannelCorrelationAsync(prompt, $"telegram:{chat}");
        await _harness.InsertTurnAsync(prompt, "Initial answer.");
        await DispatchAsync();
        if (path == "trailing")
        {
            var trailingPdf = Path.Combine(_harness.TempRoot, "trailing-original.pdf");
            File.WriteAllBytes(trailingPdf, "%PDF-1.4 original trailing bytes"u8.ToArray());
            await _harness.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText,
                $"Late middle sentinel; Late tail sentinel.\n[[attach: {trailingPdf}]]");
            await _harness.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
            return (channelId, "Late middle sentinel; Late tail sentinel.", 1);
        }
        if (path != "machine")
            throw new ArgumentOutOfRangeException(nameof(path));
        const string note = "[task 15ed2644 done] original follow-up";
        var sourceId = await _harness.SeedPendingMessageAsync(note,
            origin: QueuedMessageOrigin.Delegation, status: QueuedMessageStatus.Sent);
        var pdf = Path.Combine(_harness.TempRoot, "original.pdf");
        File.WriteAllBytes(pdf, "%PDF-1.4 original bytes"u8.ToArray());
        await _harness.InsertTurnAsync(note,
            $"Machine middle sentinel; Machine tail sentinel.\n[[attach: {pdf}]]");
        return (sourceId, "Machine middle sentinel; Machine tail sentinel.", 1);
    }

    public async Task<Guid> CompleteAdditionalMainTurnAsync(int number)
    {
        var chat = await _harness.BindChannelAsync();
        var prompt = $"Please answer independent prompt {number} completely";
        var source = await _harness.SeedChannelCorrelationAsync(prompt, $"telegram:{chat}");
        await _harness.InsertTurnAsync(prompt,
            $"Independent middle {number}; independent tail {number}.");
        return source;
    }

    public async Task<(Guid SourceId, Guid TaskId, byte[] PdfBytes, int Baseline)> CompleteMachineBundleAsync()
    {
        var chat = await _harness.BindChannelAsync();
        const string channelPrompt = "Please start the original bundle task";
        await _harness.SeedChannelCorrelationAsync(channelPrompt, $"telegram:{chat}");
        await _harness.InsertTurnAsync(channelPrompt, "Initial answer.");
        await DispatchAsync();
        var taskId = Guid.NewGuid();
        var shortId = DelegationReportFormatter.Short(taskId);
        var bundleDir = Path.Combine(_harness.TempRoot, ".antiphon", "deliverables", shortId);
        Directory.CreateDirectory(bundleDir);
        var pdf = Path.Combine(bundleDir, $"{shortId}-spec.pdf");
        var pdfBytes = "%PDF-1.4 frozen implied bundle"u8.ToArray();
        File.WriteAllBytes(pdf, pdfBytes);
        var oversized = Path.Combine(bundleDir, "01-over-cap.md");
        using (var file = File.Create(oversized))
            file.SetLength(15 * 1024 * 1024);
        var note = $"[task {shortId} done] original bundle result";
        var source = await _harness.SeedPendingMessageAsync(note,
            origin: QueuedMessageOrigin.Delegation, status: QueuedMessageStatus.Sent);
        await using (var db = CreateContext())
        {
            await db.SessionQueuedMessages.Where(m => m.Id == source)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.SourceTaskId, taskId));
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "Original bundle task", Goal = "Write spec",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Custom,
                ModelLevel = AgentModelLevel.Medium, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = _harness.TempRoot, RepoPath = _harness.TempRoot,
                Status = AgentTaskStatus.Succeeded, DeliverableBundleDir = bundleDir,
                DeliverablePdfPath = pdf, DeliverableFileCount = 1,
                CreatedAt = Clock.GetUtcNow().UtcDateTime, CompletedAt = Clock.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync();
        }
        await _harness.InsertTurnAsync(note, "Bundle middle sentinel; bundle tail sentinel.");
        return (source, taskId, pdfBytes, 1);
    }

    public Task DispatchAsync(CancellationToken ct = default) =>
        _harness.Dispatcher.OnTurnEndAsync(SessionId, ct);

    public async Task<BridgeQueueHarness> ParallelHarnessAsync()
    {
        var parallel = await BridgeQueueHarness.CreateAsync(Options(_schema.ConnectionString,
            _outboundProducer, Clock, Observer, Faults, RuntimeLogs, SessionId, AgentId));
        _temporaryRoots.Add(parallel.TempRoot);
        return parallel;
    }

    public async Task<(SessionQueuedMessage Source, ChannelOutboundPublication? Publication)> ReadAsync(
        Guid sourceId, string path = "main")
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
        var source = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == sourceId);
        var publication = await db.ChannelOutboundPublications.AsNoTracking()
            .Include(p => p.Sources)
            .SingleOrDefaultAsync(p => p.Path == path
                && p.Sources.Any(s => s.QueueMessageId == sourceId));
        return (source, publication);
    }

    public async Task<List<ChannelOutboundPublication>> ReadPublicationsAsync(Guid sourceId, string path)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
        return await db.ChannelOutboundPublications.AsNoTracking().Include(p => p.Sources)
            .Where(p => p.Path == path && p.Sources.Any(s => s.QueueMessageId == sourceId))
            .OrderBy(p => p.FirstTextSequence).ToListAsync();
    }

    public async Task StartWorkerAsync()
    {
        Observer.Clear();
        _worker = _harness.Provider.GetRequiredService<ChannelOutboundRecoveryWorker>();
        await _worker.StartAsync(CancellationToken.None);
        await Observer.NextAsync().WaitAsync(TimeSpan.FromSeconds(45));
    }

    public async Task AdvanceAndScanAsync(TimeSpan interval)
    {
        Clock.Advance(interval);
        await Observer.NextAsync().WaitAsync(TimeSpan.FromSeconds(45));
    }

    public async Task RestartAsync()
    {
        if (_worker is not null)
        {
            await _worker.StopAsync(CancellationToken.None);
            _worker = null;
        }
        Observer.Clear();
        var sessionId = _harness.SessionId;
        var agentId = _harness.AgentId;
        await _harness.DisposeAsync();
        _harness = await BridgeQueueHarness.CreateAsync(Options(_schema.ConnectionString,
            _outboundProducer, Clock, Observer, Faults, RuntimeLogs, sessionId, agentId));
        _temporaryRoots.Add(_harness.TempRoot);
    }

    public async ValueTask DisposeAsync()
    {
        if (_worker is not null)
            await _worker.StopAsync(CancellationToken.None);
        await _harness.DisposeAsync();
        await _schema.DisposeAsync();
        foreach (var root in _temporaryRoots)
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
}
