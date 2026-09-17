using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.NightlyWatchdog;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0545 in-process world for the independent nightly watchdog. The real <see cref="WatchdogLoop"/>,
/// <see cref="OutageEvaluator"/>, <see cref="Ledger"/> (a real temp SQLite file), <see cref="NotificationBody"/>,
/// <see cref="ReceiptImporter"/>, <see cref="RetryPolicy"/> and <see cref="SnapshotServer"/> run unchanged; only the
/// four external boundaries are substitutes: Windmill HTTP (<see cref="FakeWindmillApi"/>), the Telegram Bot API
/// (<see cref="FakeTransport"/>, which alone appends to <see cref="FakeRecipientChat"/>), the MTProto reader
/// (<see cref="FakeRecipientReader"/>) and the clock (<see cref="FakeTimeProvider"/>).
///
/// <para>Substitutes cannot prove: real Windmill shapes/timeouts/token scopes, Telegram acceptance semantics, MTProto
/// history/peer resolution/read marker/revocation, NTP or host zone data, TLS/DNS, the tailnet bind, ssh/sudo/systemd.
/// Those are S6 (operator-run qualification).</para>
/// </summary>
internal sealed class C545World : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 9, 18, 0, 40, 0, TimeSpan.Zero);
    public const string ChatId = "123456789";
    public const string BotToken = "424242:c545-bot-token-never-print";

    public string StateDir { get; private set; } = "";
    public FakeTimeProvider Clock { get; } = new(Start);
    public WatchdogOptions Options { get; } = new();
    public FakeWindmillApi Windmill { get; private set; } = null!;
    public FakeRecipientChat Chat { get; private set; } = null!;
    public FakeTransport Transport { get; private set; } = null!;
    public FakeRecipientReader Reader { get; private set; } = null!;
    public Ledger Ledger { get; private set; } = null!;
    public WatchdogLoop Loop { get; private set; } = null!;
    public CrashPoint? CrashAt { get; set; }
    public TickReport? LastReport { get; private set; }
    public C545Jobs Jobs { get; } = new();

    private INotificationTransport? _transportOverride;
    private SnapshotServer? _server;
    private readonly List<string> _directories = [];

    public static async Task<C545World> CreateAsync(Action<WatchdogOptions>? configure = null, FakeRecipientChat? sharedChat = null)
    {
        var world = new C545World();
        world.StateDir = world.NewDirectory();
        var o = world.Options;
        o.Namespace = "mc/test";
        o.WorkingDirectory = world.StateDir;
        o.StateDir = world.StateDir;
        o.DestinationChatId = ChatId;
        o.TelegramBotToken = BotToken;
        o.ReaderPeer = "peer-bot";
        o.ExpectedScriptHash = "h1";
        o.WindmillWorkspace = "mc";
        o.WindmillBaseUrl = "http://windmill.invalid";
        o.WindmillToken = "c545-windmill-token-never-print";
        o.ReaderApiHash = "c545-api-hash-never-print";
        o.TickSeconds = 600;
        o.ReceiptGraceMinutes = 30;
        o.ReaderHoldExpiryMinutes = 30;
        o.RunBudgetHours = 6;
        o.SnapshotBind = "127.0.0.1:0";
        o.Runtime = "native";
        configure?.Invoke(o);
        world.Windmill = new FakeWindmillApi(() => world.Clock.GetUtcNow().UtcDateTime);
        world.Chat = sharedChat ?? new FakeRecipientChat();
        world.Transport = new FakeTransport(world);
        world.Reader = new FakeRecipientReader(world.Chat);
        world.Build();
        world._server = new SnapshotServer(o.SnapshotBind!, () => SnapshotBuilder.Build(world.Ledger, world.Options));
        await Task.CompletedTask;
        return world;
    }

    public string LedgerPath => Path.Combine(StateDir, "ledger.db");

    public async Task<TickReport> TickAsync()
    {
        LastReport = await Loop.TickAsync();
        return LastReport;
    }

    public Task<TickReport> AdvanceAndTickAsync(TimeSpan by)
    {
        Clock.Advance(by);
        return TickAsync();
    }

    /// <summary>Dispose loop and ledger; with <paramref name="ledgerOnly"/> only ledger.db moves to a fresh directory.</summary>
    public Task RestartAsync(bool ledgerOnly = false)
    {
        Ledger.Dispose();
        if (ledgerOnly)
        {
            var old = StateDir;
            var fresh = NewDirectory();
            File.Move(Path.Combine(old, "ledger.db"), Path.Combine(fresh, "ledger.db"));
            Directory.Delete(old, recursive: true);
            StateDir = fresh;
            Options.StateDir = fresh;
            Options.WorkingDirectory = fresh;
        }
        Build();
        return Task.CompletedTask;
    }

    public async Task<(int Status, string Body)> GetSnapshotAsync()
    {
        using var http = new HttpClient();
        using var response = await http.GetAsync($"http://127.0.0.1:{_server!.Port}/snapshot.json");
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public async Task<(int Status, string Body)> SendSnapshotRequestAsync(HttpMethod method, string path)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{_server!.Port}{path}");
        if (method == HttpMethod.Post) request.Content = new StringContent("{}");
        using var response = await http.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public async Task<JsonObject> SnapshotJsonAsync() => (JsonNode.Parse((await GetSnapshotAsync()).Body) as JsonObject)!;

    /// <summary>Swap the fake for the real <see cref="TelegramBotTransport"/> over a recording handler.</summary>
    public RecordingHttpHandler UseTelegramTransport(params ScriptedResponse[] responses)
    {
        var handler = new RecordingHttpHandler(responses);
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _transportOverride = new TelegramBotTransport(http, Options, () => Ledger.Heartbeat().DestinationHash);
        Loop = NewLoop();
        return handler;
    }

    public IReadOnlyList<OutageRow> Outages() => Ledger.Outages();
    public IReadOnlyList<NotificationRow> Notifications() => Ledger.Notifications();
    public NotificationRow Notification(string nid) => Ledger.Notification(nid)!;
    public IReadOnlyList<AttemptRow> Attempts(string nid) => Ledger.Attempts(nid);
    public IReadOnlyList<ReceiptRow> Receipts() => Ledger.Receipts();
    public HeartbeatRow Heartbeat() => Ledger.Heartbeat();

    private void Build()
    {
        Ledger = new Ledger(LedgerPath, Options.Namespace, Clock);
        Loop = NewLoop();
    }

    private WatchdogLoop NewLoop() => new(Options, Ledger, Windmill, _transportOverride ?? Transport, Reader, Clock, point =>
    {
        if (CrashAt == point)
        {
            CrashAt = null;
            throw new WatchdogCrashException(point);
        }
    });

    private string NewDirectory()
    {
        var dir = Directory.CreateTempSubdirectory("antiphon-c545").FullName;
        _directories.Add(dir);
        return dir;
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null) await _server.DisposeAsync();
        try { Ledger?.Dispose(); } catch (ObjectDisposedException) { }
        foreach (var dir in _directories)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

internal enum FakeAuth { Ok, Unauthorized }
internal enum FakeSchedule { Enabled, Disabled }

internal sealed class FakeWindmillApi(Func<DateTime> now) : IWindmillApi
{
    private DateTime? _ping;
    private bool _explicitPing;

    public bool Reachable { get; set; } = true;
    public FakeAuth Auth { get; set; } = FakeAuth.Ok;
    public string? Script { get; set; } = "h1";
    public FakeSchedule? Schedule { get; set; } = FakeSchedule.Enabled;
    /// <summary>Follows the clock (a live desktop worker) until set; null then means no ping at all.</summary>
    public DateTime? WorkerLastPingUtc
    {
        get => _explicitPing ? _ping : now();
        set { _ping = value; _explicitPing = true; }
    }
    public List<WindmillJob> Jobs { get; } = [];
    public bool ThrowOnEverything { get; set; }
    public List<string> Calls { get; } = [];

    private WindmillCall<T>? Gate<T>(string name, bool authenticated)
    {
        Calls.Add(name);
        if (ThrowOnEverything) throw new HttpRequestException("windmill unavailable (fake)");
        if (!Reachable) return new WindmillCall<T>(WindmillCallStatus.Unreachable, default, "unreachable (fake)");
        if (authenticated && Auth == FakeAuth.Unauthorized) return new WindmillCall<T>(WindmillCallStatus.Unauthorized, default, "401 (fake)");
        return null;
    }

    public Task<WindmillCall<string>> GetVersionAsync(CancellationToken ct) =>
        Task.FromResult(Gate<string>(nameof(GetVersionAsync), false) ?? WindmillCall<string>.Ok("CE fake"));

    public Task<WindmillCall<bool>> WhoAmIAsync(CancellationToken ct) =>
        Task.FromResult(Gate<bool>(nameof(WhoAmIAsync), true) ?? WindmillCall<bool>.Ok(true));

    public Task<WindmillCall<ScriptInfo>> GetScriptAsync(CancellationToken ct) =>
        Task.FromResult(Gate<ScriptInfo>(nameof(GetScriptAsync), true)
            ?? (Script is null ? new WindmillCall<ScriptInfo>(WindmillCallStatus.NotFound, null) : WindmillCall<ScriptInfo>.Ok(new ScriptInfo(Script))));

    public Task<WindmillCall<ScheduleInfo>> GetScheduleAsync(CancellationToken ct) =>
        Task.FromResult(Gate<ScheduleInfo>(nameof(GetScheduleAsync), true)
            ?? (Schedule is null ? new WindmillCall<ScheduleInfo>(WindmillCallStatus.NotFound, null)
                : WindmillCall<ScheduleInfo>.Ok(new ScheduleInfo(Schedule == FakeSchedule.Enabled))));

    public Task<WindmillCall<IReadOnlyList<WorkerPing>>> ListWorkersAsync(CancellationToken ct) =>
        Task.FromResult(Gate<IReadOnlyList<WorkerPing>>(nameof(ListWorkersAsync), true)
            ?? WindmillCall<IReadOnlyList<WorkerPing>>.Ok(WorkerLastPingUtc is { } ping ? [new WorkerPing("desktop", ping)] : []));

    public Task<WindmillCall<IReadOnlyList<WindmillJob>>> ListJobsAsync(CancellationToken ct) =>
        Task.FromResult(Gate<IReadOnlyList<WindmillJob>>(nameof(ListJobsAsync), true)
            ?? WindmillCall<IReadOnlyList<WindmillJob>>.Ok(Jobs.ToList()));

    public Task<WindmillCall<JsonObject?>> GetCompletedResultAsync(string jobId, CancellationToken ct) =>
        Task.FromResult(Gate<JsonObject?>(nameof(GetCompletedResultAsync), true)
            ?? WindmillCall<JsonObject?>.Ok(Jobs.FirstOrDefault(j => j.Id == jobId)?.Result));

    public Task<WindmillCall<string>> GetLogsAsync(string jobId, CancellationToken ct) =>
        Task.FromResult(Gate<string>(nameof(GetLogsAsync), true) ?? WindmillCall<string>.Ok(Jobs.FirstOrDefault(j => j.Id == jobId)?.Logs ?? ""));
}

internal sealed record ChatMessage(string PeerId, string MessageId, DateTime DateUtc, string Text);

/// <summary>The recipient's chat. Appended only by <see cref="FakeTransport"/>; a test never writes an observation.</summary>
internal sealed class FakeRecipientChat
{
    private int _nextId;
    public List<ChatMessage> Messages { get; } = [];
    public string NextMessageId() => (++_nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal enum TransportMode { Deliver, AcceptWithoutDelivery, LoseResponse, Throw, Reject }

internal sealed class FakeTransport : INotificationTransport
{
    private readonly C545World _world;

    public FakeTransport(C545World world)
    {
        _world = world;
        OnSend = CheckIntentVisible;
    }

    public TransportMode Mode { get; set; } = TransportMode.Deliver;
    public string RejectErrorClass { get; private set; } = "api-error";
    public int? RejectRetryAfterSeconds { get; private set; }
    public List<(DateTime At, OutboundMessage Message)> Sends { get; } = [];
    public Action<OutboundMessage>? OnSend { get; set; }
    public bool? IntentVisibleAtSend { get; private set; }

    public void Reject(string errorClass, int? retryAfterSeconds = null)
    {
        Mode = TransportMode.Reject;
        RejectErrorClass = errorClass;
        RejectRetryAfterSeconds = retryAfterSeconds;
    }

    public Task<TransportResult> SendAsync(OutboundMessage message, CancellationToken ct)
    {
        var now = _world.Clock.GetUtcNow().UtcDateTime;
        Sends.Add((now, message));
        OnSend?.Invoke(message);
        switch (Mode)
        {
            case TransportMode.Deliver:
            {
                var id = Append(message, now);
                return Task.FromResult(TransportResult.Ok(id));
            }
            case TransportMode.AcceptWithoutDelivery:
                return Task.FromResult(TransportResult.Ok(_world.Chat.NextMessageId()));
            case TransportMode.LoseResponse:
                Append(message, now);
                throw new HttpRequestException("response lost after delivery (fake)");
            case TransportMode.Throw:
                throw new HttpRequestException("send failed (fake)");
            default:
                return Task.FromResult(TransportResult.Fail(RejectErrorClass, "rejected (fake)", RejectRetryAfterSeconds));
        }
    }

    private string Append(OutboundMessage message, DateTime now)
    {
        var id = _world.Chat.NextMessageId();
        _world.Chat.Messages.Add(new ChatMessage(_world.Options.ReaderPeer!, id, now, message.Text));
        return id;
    }

    /// <summary>Opens a second connection on the ledger file at send time and looks for the pending attempt row.</summary>
    private void CheckIntentVisible(OutboundMessage message)
    {
        var path = _world.LedgerPath;
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM attempts WHERE nid = $nid AND attempt = $attempt";
        command.Parameters.AddWithValue("$nid", message.Nid);
        command.Parameters.AddWithValue("$attempt", message.Attempt);
        IntentVisibleAtSend = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }
}

internal enum ReaderMode { Eligible, Held }

internal sealed class FakeRecipientReader(FakeRecipientChat chat) : IRecipientReader
{
    public ReaderMode Mode { get; private set; } = ReaderMode.Eligible;
    public string HeldReason { get; private set; } = "";
    public TimeSpan DateOffset { get; set; }
    public string? PeerOverride { get; set; }
    public Func<string, string>? Transform { get; set; }
    public bool ReadByRecipient { get; set; }
    public int Reads { get; private set; }

    public void Hold(string reason) { Mode = ReaderMode.Held; HeldReason = reason; }
    public void Release() => Mode = ReaderMode.Eligible;

    public Task<ReaderReadResult> ReadAsync(DateTime floorUtc, CancellationToken ct)
    {
        Reads++;
        if (Mode == ReaderMode.Held) return Task.FromResult(ReaderReadResult.Held(HeldReason));
        var observations = chat.Messages
            .Where(m => m.DateUtc >= floorUtc - TimeSpan.FromHours(24))
            .Select(m => new RecipientObservation(PeerOverride ?? m.PeerId, m.MessageId, m.DateUtc + DateOffset,
                Transform?.Invoke(m.Text) ?? m.Text, ReadByRecipient))
            .ToList();
        return Task.FromResult(ReaderReadResult.Eligible(observations));
    }
}

internal sealed record ScriptedResponse(int Status, string? BodyJson = null, int DelayMs = 0);

internal sealed class RecordingHttpHandler(IEnumerable<ScriptedResponse> responses) : HttpMessageHandler
{
    private readonly Queue<ScriptedResponse> _responses = new(responses);
    public List<(HttpMethod Method, Uri Uri, string? BodyJson, string? Authorization, int AuthorizationCount)> Requests { get; } = [];

    public void Enqueue(ScriptedResponse response) => _responses.Enqueue(response);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var auth = request.Headers.TryGetValues("Authorization", out var values) ? values.ToList() : [];
        Requests.Add((request.Method, request.RequestUri!, body, auth.FirstOrDefault(), auth.Count));
        var next = _responses.Count > 0 ? _responses.Dequeue() : new ScriptedResponse(200, "{}");
        if (next.DelayMs > 0) await Task.Delay(next.DelayMs, cancellationToken);
        return new HttpResponseMessage((HttpStatusCode)next.Status)
        {
            Content = new StringContent(next.BodyJson ?? "", Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>Job-row builders. CreatedAtUtc defaults to DueUtc(dueDay) + 1 min (+ the builder's sequence offset).</summary>
internal sealed class C545Jobs
{
    private int _sequence;

    private DateTime Created(string dueDay, TimeSpan? offset) =>
        LondonClock.DueUtc(DateOnly.Parse(dueDay, System.Globalization.CultureInfo.InvariantCulture)).AddMinutes(1)
        + (offset ?? TimeSpan.FromSeconds(_sequence++));

    public WindmillJob HopFailed(string dueDay, string id, string logs = "ssh: connect to host windows port 22: Connection timed out\r\nexit 255", TimeSpan? createdOffset = null)
    {
        var created = Created(dueDay, createdOffset);
        return new WindmillJob(id, JobKind.Completed, false, "u/lndcobra/antiphon_nightly_tests", created, created, created.AddSeconds(30), logs, null);
    }

    public WindmillJob Failed(string dueDay, string id, string logs = "Assertion failed: expected 1", TimeSpan? createdOffset = null) =>
        HopFailed(dueDay, id, logs, createdOffset);

    public WindmillJob Running(DateTime startedAtUtc, string id = "j-running", string? dueDay = null)
    {
        var day = dueDay ?? LondonClock.Format(LondonClock.DueDay(startedAtUtc));
        var created = Created(day, TimeSpan.Zero);
        return new WindmillJob(id, JobKind.Running, null, "u/lndcobra/antiphon_nightly_tests", created, startedAtUtc, null, "", null);
    }

    public WindmillJob Queued(string dueDay, string id = "j-queued") =>
        new(id, JobKind.Queued, null, "u/lndcobra/antiphon_nightly_tests", Created(dueDay, TimeSpan.Zero), null, null, "", null);

    public WindmillJob Success(string dueDay, string runId, string sha = "s18", bool reportDelivered = true, bool testsPassed = true,
        bool coverageComplete = true, bool scheduled = true, string id = "j-ok", TimeSpan? createdOffset = null, string? localDueDate = null)
    {
        var created = Created(dueDay, createdOffset);
        var result = new JsonObject
        {
            ["nativeRunId"] = runId, ["localDueDate"] = localDueDate ?? dueDay, ["sha"] = sha, ["reportDelivered"] = reportDelivered,
            ["testsPassed"] = testsPassed, ["coverageComplete"] = coverageComplete, ["policyHash"] = "p1",
        };
        return new WindmillJob(id, JobKind.Completed, true, scheduled ? "u/lndcobra/antiphon_nightly_tests" : null, created, created,
            created.AddHours(2), "", result);
    }

    public WindmillJob SuccessWithResult(string dueDay, string id, JsonObject? result, TimeSpan? createdOffset = null)
    {
        var created = Created(dueDay, createdOffset);
        return new WindmillJob(id, JobKind.Completed, true, "u/lndcobra/antiphon_nightly_tests", created, created, created.AddHours(2), "", result);
    }
}
