using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

internal sealed class DeliveredSpillFixture(BridgeQueueHarness harness, ScaledTimeProvider clock) : IAsyncDisposable
{
    public BridgeQueueHarness H { get; private set; } = harness;
    public ScaledTimeProvider Clock { get; } = clock;
    public SessionQueuedMessage Before { get; private set; } = null!;
    public string Wire => Before.Body;
    private readonly List<SessionRunnerTranscriptEvent> _entries = [];
    private readonly List<string> _captured = [];
    private readonly List<string> _roots = [harness.TempRoot];
    private Action<IServiceCollection>? _configureServices;
    private SaveChangesInterceptor? _interceptor;
    private int _tolerance;
    public IReadOnlyList<string> Captured => _captured;
    private string[] _inputs = [], _conditional = [], _prompts = [], _lifecycle = [], _submissions = [];

    public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(H.ConnectionString));

    public static async Task<DeliveredSpillFixture> CreateAsync(string connection, int tolerance = 30,
        SaveChangesInterceptor? interceptor = null, Action<IServiceCollection>? configureServices = null)
    {
        var clock = new ScaledTimeProvider(1);
        var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = connection,
            TimeProvider = clock,
            PreserveDatabaseOnDispose = true,
            ConfigureDbContext = o => { if (interceptor is not null) o.AddInterceptors(interceptor); },
            ConfigureServices = s => { s.AddSingleton<RemoteSpillCourier>(); configureServices?.Invoke(s); },
            ConfigureDeliveryVerification = v => v.UnobservableBaselineConfirmClockToleranceSeconds = tolerance,
        });
        var f = new DeliveredSpillFixture(h, clock)
        { _interceptor = interceptor, _configureServices = configureServices, _tolerance = tolerance };
        f.ConfigureAdapter();
        await using var db = f.Db();
        await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.AgentKind, AgentKind.Codex)
            .SetProperty(s => s.RunnerId, "c1056-fixture")
            .SetProperty(s => s.RunnerStoreId, Guid.NewGuid())
            .SetProperty(s => s.RunnerCwd, h.TempRoot));
        return f;
    }

    private void ConfigureAdapter()
    {
        H.Adapter.OnSubmitted = text => { _captured.Add(text); return Task.CompletedTask; };
        H.Adapter.SwallowSubmits = 0;
        H.Adapter.SubmitAck = "\n• Working (0s • esc to interrupt)";
    }

    public async Task RecreateAsync()
    {
        NoInput();
        var previous = H;
        await previous.DisposeAsync();
        H = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = previous.ConnectionString, TimeProvider = Clock,
            PreserveDatabaseOnDispose = true,
            AttachSessionId = previous.SessionId, AttachAgentId = previous.AgentId,
            ConfigureDbContext = o => { if (_interceptor is not null) o.AddInterceptors(_interceptor); },
            ConfigureServices = s => { s.AddSingleton<RemoteSpillCourier>(); _configureServices?.Invoke(s); },
            ConfigureDeliveryVerification = v => v.UnobservableBaselineConfirmClockToleranceSeconds = _tolerance,
        });
        _roots.Add(H.TempRoot);
        ConfigureAdapter();
        SetSnapshot();
        SnapshotInput();
        H.Provider.GetRequiredService<RemoteSpillCourier>().IsStaged(H.SessionId)
            .ShouldBeFalse("fresh-graph-no-staged-spill");
    }

    public async Task SetKindAsync(AgentKind kind)
    {
        await using var db = Db();
        await db.AgentSessions.Where(s => s.Id == H.SessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.AgentKind, kind));
    }

    public async Task ScreenAsync(QueuedMessageOrigin origin = QueuedMessageOrigin.Ui)
    {
        (await TranscriptCountAsync()).ShouldBe(0, "real-empty-transcript-before-input");
        await EnqueueSpillAsync(origin);
        await CaptureScreenAsync();
    }

    public async Task EnqueueSpillAsync(QueuedMessageOrigin origin = QueuedMessageOrigin.Ui, bool deliverIfIdle = true)
    {
        const string relative = ".antiphon/task-c1056-brief.md";
        var body = "[antiphon-task:c1056abc] complete original spill " + new string('e', 3000);
        var pointer = "[antiphon-task:c1056abc] Read the full brief at " + relative
            + "\nPreserve the queue identity and the original attempt floor. The file contains the entire"
            + " task and its verification requirements; read it before making changes. Report the exact"
            + " checkpoint results and keep the full recipient submission evidence. End-of-wire-Z";
        H.Queue.StageRemoteSpill(H.SessionId, H.TempRoot, new PhoneHomeInputSpill(relative, body));
        await H.Queue.EnqueueAsync(H.SessionId, pointer, MessageSendMode.WhenIdle, CancellationToken.None,
            origin, deliverIfIdle: deliverIfIdle);
    }

    public async Task CaptureScreenAsync()
    {
        await using var db = Db();
        Before = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == H.SessionId);
        _captured.ShouldHaveSingleItem("exactly-one-actual-submission");
        H.Adapter.SubmittedBodies.ShouldBe(_captured);
        Before.Body.ShouldBe(_captured.Single(), "actual-captured-wire");
        Before.Body.ShouldContain(Before.RemoteSpillRelativePath!);
        Before.LastDeliveryBaselineSequence.ShouldBeNull("honest-unobservable-baseline");
        Before.LastDeliveryStartedAt.ShouldNotBeNull();
        Before.LastDeliveryGeneration.ShouldNotBeNull();
        Before.DeliveryAttempts.ShouldBe(1);
        Before.Status.ShouldBe(QueuedMessageStatus.Sent);
        Before.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        Before.RemoteSpillBody.ShouldBe("[antiphon-task:c1056abc] complete original spill " + new string('e', 3000),
            "screen-spill-retained");
        (await TranscriptCountAsync()).ShouldBe(0, "screen-has-no-receipt");
        SnapshotInput();
    }

    public async Task SeedAsync(string? body = null, long? baseline = null, Action<SessionQueuedMessage>? change = null)
    {
        var id = await H.SeedPendingMessageAsync(body ?? "historical-spill-wire-" + Guid.NewGuid(),
            deliveryAttempts: 1, baselineSequence: baseline, status: QueuedMessageStatus.Sent,
            deliveryVerdict: DeliveryVerdict.Delivered, lastDeliveryStartedAt: H.Now);
        await using (var db = Db())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.RemoteSpillBody = "historical-owned-bytes-" + id;
            row.RemoteSpillRelativePath = TypedBodySpill.InboxRelativePath(id.ToString("D"));
            change?.Invoke(row);
            await db.SaveChangesAsync();
        }
        await using var fresh = Db();
        Before = await fresh.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        SnapshotInput();
    }

    private void SnapshotInput()
    {
        _inputs = H.Adapter.Inputs.ToArray();
        _conditional = H.Adapter.ConditionalInputs.ToArray();
        _prompts = H.Adapter.Prompts.ToArray();
        _lifecycle = H.Adapter.Lifecycle.ToArray();
        _submissions = H.Adapter.SubmittedBodies.ToArray();
    }

    public void NoInput()
    {
        H.Adapter.Inputs.ShouldBe(_inputs, "receipt-zero-input");
        H.Adapter.ConditionalInputs.ToArray().ShouldBe(_conditional, "receipt-zero-conditional-input");
        H.Adapter.Prompts.ShouldBe(_prompts, "receipt-zero-prompts");
        H.Adapter.Lifecycle.ShouldBe(_lifecycle, "receipt-zero-lifecycle");
        H.Adapter.SubmittedBodies.ShouldBe(_submissions, "receipt-zero-submissions");
    }

    private SessionRunnerTranscriptEvent Entry(string? text, string kind, DateTime? timestamp,
        long? sequence = null, string? tool = null) => new(H.SessionId,
            sequence ?? (_entries.Count == 0 ? 1 : _entries.Max(e => e.Sequence) + 1),
            kind, Guid.NewGuid().ToString("N"), null,
            timestamp is { } time ? new DateTimeOffset(time) : null, "user", text,
            tool, tool == "AskUserQuestion" ? "{\"questions\":[]}" : null, null, false, null);

    public SessionRunnerTranscriptEvent StageReceipt()
    {
        _captured.ShouldHaveSingleItem("one-recipient-submission-across-graphs");
        var entry = Entry(_captured.Single(), TranscriptKinds.UserPrompt, H.Now);
        _entries.Add(entry);
        SetSnapshot();
        return entry;
    }

    private void SetSnapshot() => H.Runner.SetTranscript(new(H.SessionId, _entries.ToArray(),
        _entries.Count == 0 ? 0 : _entries.Max(e => e.Sequence)));

    public async Task AssertReceiptAsync(SessionRunnerTranscriptEvent entry)
    {
        await using var db = Db();
        var receipts = await db.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == H.SessionId
            && e.Kind == TranscriptKinds.UserPrompt && e.Uuid == entry.Uuid).ToListAsync();
        receipts.ShouldHaveSingleItem("one-committed-recipient-receipt");
        receipts.Single().Text.ShouldBe(_captured.Single(), "committed-actual-receipt-text");
        receipts.Single().Timestamp.ShouldBe(SessionGeneration.Normalize(entry.Timestamp!.Value.UtcDateTime),
            "committed-native-timestamp");
        receipts.Single().Timestamp!.Value.ShouldBeGreaterThanOrEqualTo(Before.LastDeliveryStartedAt!.Value,
            "receipt-after-original-floor");
    }

    public async Task<TranscriptEntry> PublishAsync(string? text, string kind, DateTime? timestamp,
        long? sequence = null, string? tool = null)
    {
        var entry = Entry(text, kind, timestamp, sequence, tool);
        _entries.Add(entry);
        H.Runner.SetTranscript(new(H.SessionId, _entries.ToArray(), _entries.Max(e => e.Sequence)));
        await H.Runtime.CatchUpTranscriptAsync(H.SessionId, CancellationToken.None);
        await using var db = Db();
        var saved = await db.TranscriptEntries.AsNoTracking().SingleAsync(e => e.AgentSessionId == H.SessionId
            && e.Uuid == entry.Uuid && e.Kind == kind);
        saved.Text.ShouldBe(text, "committed-actual-receipt-text");
        saved.Timestamp.ShouldBe(timestamp is { } t ? SessionGeneration.Normalize(t) : null,
            "committed-native-timestamp");
        return saved;
    }

    public async Task PublishBatchAsync(string[] texts)
    {
        foreach (var text in texts) _entries.Add(Entry(text, TranscriptKinds.UserPrompt, H.Now));
        H.Runner.SetTranscript(new(H.SessionId, _entries.ToArray(), _entries.Max(e => e.Sequence)));
        await H.Runtime.CatchUpTranscriptAsync(H.SessionId, CancellationToken.None);
        await using var db = Db();
        foreach (var entry in _entries.TakeLast(texts.Length))
            (await db.TranscriptEntries.AsNoTracking().SingleAsync(e => e.AgentSessionId == H.SessionId
                && e.Uuid == entry.Uuid)).Text.ShouldBe(entry.Text, "batch-receipt-persisted");
    }

    public async Task<SessionQueueTurnEndResult?> FlushAsync(string entry = "flush-if-idle")
    {
        if (entry == "turn-end") return await H.Queue.OnTurnEndAsync(H.SessionId, CancellationToken.None);
        if (entry == "flush-session") await H.Queue.FlushSessionAsync(H.SessionId, CancellationToken.None);
        else await H.Queue.FlushIfIdleAsync(H.SessionId, CancellationToken.None);
        return null;
    }

    public async Task<SessionQueuedMessage> RowAsync()
    {
        await using var db = Db();
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == Before.Id);
    }

    public async Task<int> TranscriptCountAsync()
    {
        await using var db = Db();
        return await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == H.SessionId);
    }

    public static object?[] Identity(SessionQueuedMessage row) =>
    [row.Id, row.AgentSessionId, row.Body, row.RemoteSpillRelativePath, row.DeliveryAttempts,
        row.LastDeliveryStartedAt, row.LastDeliveryBaselineSequence, row.LastDeliveryGeneration,
        row.Status, row.SentAt, row.Sequence, row.CreatedAt, row.Origin];

    public async Task RetainedAsync(string label)
    {
        var row = await RowAsync();
        row.RemoteSpillBody.ShouldBe(Before.RemoteSpillBody, label);
        row.DeliveryVerdict.ShouldBe(Before.DeliveryVerdict, label);
        row.DeliveryVerdictAt.ShouldBe(Before.DeliveryVerdictAt, label);
        Identity(row).ShouldBe(Identity(Before), label);
        NoInput();
    }

    public async Task ReleasedAsync(string label, DateTime from)
    {
        var row = await RowAsync();
        row.RemoteSpillBody.ShouldBeNull(label);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed, label);
        row.DeliveryVerdictAt.ShouldNotBeNull(label);
        row.DeliveryVerdictAt!.Value.ShouldBeGreaterThanOrEqualTo(SessionGeneration.Normalize(from), label);
        row.DeliveryVerdictAt.Value.ShouldBeLessThanOrEqualTo(H.Now, label);
        Identity(row).ShouldBe(Identity(Before), label);
        NoInput();
    }

    public async ValueTask DisposeAsync()
    {
        await H.DisposeAsync();
        foreach (var root in _roots)
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
