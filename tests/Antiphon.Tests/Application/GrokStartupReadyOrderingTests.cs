using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
public sealed class GrokStartupReadyOrderingTests
{
    private const string Nonce = "C778WHOLE";

    [Test]
    public async Task Work_waits_for_ready_rules_ack_and_complete_prompt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var runner = new ScriptedRunner();
        var factory = new Factory(runner);
        await using var h = await CreateHarness(schema.ConnectionString, runner, factory);
        factory.CaptureDirectory = h.TempRoot;
        await Prepare(h);
        var ordinary = await h.SeedPendingMessageAsync("Task nonce " + Nonce);
        var generation = Guid.NewGuid();
        var payload = new GrokRulesPayload("rules for c778\n", 1, generation);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var scope = h.Provider.CreateScope();
        var launch = scope.ServiceProvider.GetRequiredService<AgentSessionService>()
            .LaunchInteractiveAsync(h.SessionId, h.AgentId, Spec(h, payload), null, false, null, deadline.Token);
        try
        {
            await WaitUntil(() => runner.SnapshotReads >= 2 || launch.IsCompleted, deadline.Token);
            launch.IsCompleted.ShouldBeFalse();
            runner.Writes.ShouldBeEmpty();
            await h.Queue.FlushSessionAsync(h.SessionId, deadline.Token);
            runner.Writes.ShouldBeEmpty();
            runner.ReleaseReady();
            try { await launch; }
            catch (Exception ex)
            {
                await using var failedDb = new Antiphon.Server.Infrastructure.Data.AppDbContext(
                    TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                var state = await failedDb.AgentSessions.AsNoTracking().Where(s => s.Id == h.SessionId)
                    .Select(s => new { s.Status, s.GrokRulesState, s.GrokRulesFailure }).SingleAsync();
                var rows = await failedDb.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.AgentSessionId == h.SessionId)
                    .Select(m => new { m.Status, m.RulesRefreshKey, m.RulesAcknowledgedAt, m.DeliveryAttempts })
                    .ToListAsync();
                throw new InvalidOperationException(
                    $"C778 ordering: writes={runner.Writes.Count} submits={runner.SubmittedBodies.Count} "
                    + $"state={state} rows={string.Join(';', rows)}", ex);
            }
            await h.Runtime.SyncTranscriptAsync(h.SessionId, deadline.Token);
            await using var db = new Antiphon.Server.Infrastructure.Data.AppDbContext(
                TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == h.SessionId);
            session.Status.ShouldBe(SessionStatus.Running);
            session.GrokRulesState.ShouldBe(GrokRulesState.Ready);
            runner.SubmittedBodies.Count.ShouldBeGreaterThanOrEqualTo(2);
            runner.SubmittedBodies[0].ShouldStartWith("[antiphon-grok-rules:");
            runner.SubmittedBodies.Last().ShouldContain(Nonce);
            var message = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == ordinary);
            message.Status.ShouldBe(QueuedMessageStatus.Sent);
            var prompts = await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt).ToListAsync();
            prompts.Count(t => t.Text?.Contains(Nonce, StringComparison.Ordinal) == true).ShouldBe(1);
            prompts.Single(t => t.Text?.Contains(Nonce, StringComparison.Ordinal) == true).Text
                .ShouldContain("Task nonce " + Nonce);
        }
        finally { deadline.Cancel(); try { await launch; } catch { } }
    }

    [Test]
    public async Task Unready_launch_cleans_up_and_keeps_work_pending()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var runner = new ScriptedRunner();
        var factory = new Factory(runner) { MaxWaitMs = 180 };
        await using var h = await CreateHarness(schema.ConnectionString, runner, factory);
        factory.CaptureDirectory = h.TempRoot;
        await Prepare(h);
        var ordinary = await h.SeedPendingMessageAsync("Task nonce " + Nonce);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var scope = h.Provider.CreateScope();
        await Should.ThrowAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<AgentSessionService>()
            .LaunchInteractiveAsync(h.SessionId, h.AgentId, Spec(h), null, false, null, deadline.Token));
        await using var db = new Antiphon.Server.Infrastructure.Data.AppDbContext(
            TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == h.SessionId)).Status
            .ShouldBe(SessionStatus.Failed);
        var message = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == ordinary);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryAttempts.ShouldBe(0);
        runner.Writes.ShouldBeEmpty();
        runner.KillGenerationCalls.ShouldBe(1);
        Directory.GetFiles(h.TempRoot, "grok-startup-*.txt").Length.ShouldBe(1);
    }

    private static async Task<BridgeQueueHarness> CreateHarness(string connection,
        ScriptedRunner runner, Factory factory) => await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = connection,
            ConfigureServices = services =>
            {
                services.AddSingleton<ISessionRunnerClient>(runner);
                services.AddSingleton<IAgentProtocolAdapterFactory>(factory);
                services.AddSingleton(Options.Create(new GrokRulesSettings()));
                services.AddSingleton<GrokRulesRefreshService>();
            },
        });

    private static async Task Prepare(BridgeQueueHarness h)
    {
        h.Runtime.TryRemove(h.SessionId, out var old).ShouldBeTrue();
        if (old is not null) await old.DisposeAsync();
        await using var db = new Antiphon.Server.Infrastructure.Data.AppDbContext(
            TestDbFixture.CreateDbContextOptions(h.ConnectionString));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.Status = SessionStatus.Starting;
        await db.SaveChangesAsync();
    }

    private static AgentLaunchSpec Spec(BridgeQueueHarness h, GrokRulesPayload? payload = null) =>
        new("grok", AgentKind.Grok, "grok", [], new Dictionary<string, string>(),
            h.TempRoot, 120, 30, GrokRulesPayload: payload);

    private static async Task WaitUntil(Func<bool> predicate, CancellationToken ct)
    {
        while (!predicate()) { ct.ThrowIfCancellationRequested(); await Task.Delay(20, ct); }
    }

    private sealed class Factory(ScriptedRunner runner) : IAgentProtocolAdapterFactory
    {
        public int MaxWaitMs { get; set; } = 1500;
        public string? CaptureDirectory { get; set; }
        public IAgentProtocolAdapter Create(AgentKind kind) => new RunnerGrokAdapter(runner,
            Options.Create(new AgentRegistrySettings
            {
                GrokReadyMaxWaitMs = MaxWaitMs, GrokReadyQuietPeriodMs = 80,
                GrokReadyMinTotalWaitMs = 0, GrokStartupCaptureDirectory = CaptureDirectory,
            }), Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings { Enabled = false },
            }));
    }

    private sealed class ScriptedRunner : ISessionRunnerClient
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<SessionRunnerTranscriptEvent> _entries = [];
        private Guid _id;
        private DateTime _started;
        private GrokRulesReceipt? _receipt;
        private string? _body;
        private long _sequence;
        private bool _killed;
        public int SnapshotReads { get; private set; }
        public int KillGenerationCalls { get; private set; }
        public List<string> Writes { get; } = [];
        public List<string> SubmittedBodies { get; } = [];
        public void ReleaseReady() => _ready.TrySetResult();
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) =>
            Task.FromResult<RunnerCapabilitiesDto?>(new("test", "test", "test", false,
                Features: [GrokRulesTransport.Capability]));
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct)
        {
            _id = id;
            _started = DateTime.UtcNow;
            if (spec.GrokRulesPayload is { } rules)
            {
                var bytes = Encoding.UTF8.GetBytes(rules.Content);
                _receipt = new GrokRulesReceipt($"C:\\remote\\instructions\\grok\\{id:N}\\rules.md",
                    GrokRulesTransport.Hash(bytes), bytes.Length, 1, rules.Generation);
            }
            return GetAsync(id, ct);
        }
        public async Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            _id == Guid.Empty || _killed ? [] : [await GetAsync(_id, ct)];
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(id, _killed ? null : 123,
                _started, _killed ? "Exited" : "Running", _killed ? 0 : null,
                _killed ? AgentExitReason.KilledByRequest : AgentExitReason.Unknown,
                _sequence, TranscriptBound: true, GrokRulesReceipt: _receipt));
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(id, "", _sequence));
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct)
        {
            SnapshotReads++;
            _sequence++;
            var screen = _ready.Task.IsCompleted ? GrokStartupFixture.ReadyScreen() : StartingScreen;
            return Task.FromResult(new SessionRunnerSnapshotDto(id, screen, screen, _sequence, _started));
        }
        private static string StartingScreen
        {
            get
            {
                using var doc = GrokStartupFixture.Read();
                return GrokStartupFixture.Screen(GrokStartupFixture.Capture(doc, "startup-"), 15);
            }
        }
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(id, _entries.ToArray(), _entries.LastOrDefault()?.Sequence ?? 0));
        public Task SendInputAsync(Guid id, string input, CancellationToken ct)
        {
            Writes.Add(input);
            _sequence++;
            if (input != "\r") { _body = input.Replace("\x1b[200~", "").Replace("\x1b[201~", ""); return Task.CompletedTask; }
            if (_body is null) return Task.CompletedTask;
            var text = _body.Replace("\n", "");
            _body = null;
            SubmittedBodies.Add(text);
            Append(TranscriptKinds.UserPrompt, text);
            if (text.StartsWith("[antiphon-grok-rules:", StringComparison.Ordinal) && _receipt is not null)
            {
                var end = text.IndexOf(']');
                var promptId = text[21..end];
                Append(TranscriptKinds.AssistantText,
                    $"ANTIPHON_RULES_ACK id={promptId} generation={_receipt.Generation:N} sha256={_receipt.Sha256}");
            }
            Append(TranscriptKinds.TurnEnd, null, "end_turn");
            return Task.CompletedTask;
        }
        private void Append(string kind, string? text, string? stop = null) => _entries.Add(
            new SessionRunnerTranscriptEvent(_id, _entries.Count + 1, kind, Guid.NewGuid().ToString("N"),
                null, DateTimeOffset.UtcNow, kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
                text, null, null, null, null, stop));
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct)
        { _killed = true; return GetAsync(id, ct); }
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid id,
            DateTime expectedAcceptedStartedAt, CancellationToken ct)
        {
            KillGenerationCalls++;
            _killed = true;
            return Task.FromResult(new RunnerKillGenerationResult(id, true,
                KillGenerationOutcomes.Killed, expectedAcceptedStartedAt));
        }
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        { await Task.CompletedTask; yield break; }
    }
}
