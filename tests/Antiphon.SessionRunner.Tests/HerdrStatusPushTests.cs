using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0163 B1: status push remains display-only, refreshes its TTL, and removes its labels when
/// the runner exits. These run through the normal event hub and named-pipe fake, rather than
/// mocking the Herdr client.
/// </summary>
[NotInParallel("HerdrStatusPush")]
public class HerdrStatusPushTests
{
    private static readonly MethodInfo ClassifyMethod = typeof(HerdrStatusPushService)
        .GetMethod("Classify", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Test]
    public void Classify_returns_the_safe_verdict_and_reason_for_every_evidence_branch()
    {
        var sessionId = Guid.NewGuid();
        var cases = new[]
        {
            (Session: Session(sessionId, TranscriptBound: null), Entries: Array.Empty<RunnerTranscriptEvent>(), Verdict: "unknown", Reason: "no-transcript"),
            (Session: Session(sessionId, TranscriptBound: false, TranscriptUnboundReason: "awaiting-input"), Entries: Array.Empty<RunnerTranscriptEvent>(), Verdict: "unknown", Reason: "awaiting-input"),
            (Session: Session(sessionId, TranscriptBound: true), Entries: Array.Empty<RunnerTranscriptEvent>(), Verdict: "unknown", Reason: "empty"),
            (Session: Session(sessionId, TranscriptBound: true), Entries: new[] { Entry(sessionId, 1, TranscriptKinds.UserPrompt) }, Verdict: "working", Reason: (string?)null),
            (Session: Session(sessionId, TranscriptBound: true), Entries: new[] { Entry(sessionId, 1, TranscriptKinds.UserPrompt), Entry(sessionId, 2, TranscriptKinds.TurnEnd) }, Verdict: "idle", Reason: (string?)null),
        };

        foreach (var testCase in cases)
        {
            var result = Classify(testCase.Session, new RunnerTranscriptDto(sessionId, testCase.Entries, testCase.Entries.Length));
            result.Verdict.ShouldBe(testCase.Verdict);
            result.Reason.ShouldBe(testCase.Reason);
        }
    }

    [Test]
    public async Task Initial_changed_verdict_pushes_after_debounce_and_unchanged_verdict_is_skipped_before_heartbeat()
    {
        await using var fixture = await Fixture.CreateAsync(heartbeatSeconds: 5, debounceMs: 30);
        await fixture.StartSessionAsync();

        var reports = await fixture.WaitForMetadataReportsAsync(1);
        var firstParameters = Parameters(reports[0]);
        firstParameters.TryGetProperty("tokens", out var tokens).ShouldBeTrue(firstParameters.GetRawText());
        tokens.TryGetProperty("antiphon-state", out var state).ShouldBeTrue(tokens.GetRawText());
        state.GetString().ShouldBe("unknown:no-transcript");

        await Task.Delay(200);
        MetadataReports(fixture.Fake).Count.ShouldBe(1, "the unchanged verdict must wait for the heartbeat");
        AssertDisplayOnly(fixture.Fake);
    }

    [Test]
    public async Task Unchanged_verdict_is_republished_at_the_heartbeat_with_monotonically_increasing_utc_millisecond_seq()
    {
        await using var fixture = await Fixture.CreateAsync(heartbeatSeconds: 1, debounceMs: 0);
        var lowerBound = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await fixture.StartSessionAsync();

        var reports = await fixture.WaitForMetadataReportsAsync(2, TimeSpan.FromSeconds(4));
        var upperBound = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var firstSeq = Sequence(Parameters(reports[0]));
        var secondSeq = Sequence(Parameters(reports[1]));

        firstSeq.ShouldBeGreaterThanOrEqualTo(lowerBound);
        secondSeq.ShouldBeLessThanOrEqualTo(upperBound);
        secondSeq.ShouldBeGreaterThan(firstSeq);
        AssertDisplayOnly(fixture.Fake);
    }

    [Test]
    public async Task Session_exit_clears_state_labels_and_both_metadata_tokens_within_the_configured_timeout()
    {
        await using var fixture = await Fixture.CreateAsync(heartbeatSeconds: 5, debounceMs: 0, exitClearTimeoutMs: 1_000);
        var sessionId = await fixture.StartSessionAsync();
        await fixture.WaitForMetadataReportsAsync(1);

        var stopwatch = Stopwatch.StartNew();
        await fixture.Runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        var reports = await fixture.WaitForMetadataReportsAsync(2, TimeSpan.FromSeconds(4));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(1_000));
        var parameters = Parameters(reports[^1]);

        parameters.GetProperty("clear_state_labels").GetBoolean().ShouldBeTrue();
        parameters.GetProperty("tokens").GetProperty("antiphon-state").ValueKind.ShouldBe(JsonValueKind.Null);
        parameters.GetProperty("tokens").GetProperty("antiphon-as-of").ValueKind.ShouldBe(JsonValueKind.Null);
        AssertDisplayOnly(fixture.Fake);
    }

    private static (string Verdict, string? Reason) Classify(RunnerSessionDto session, RunnerTranscriptDto transcript) =>
        ((string Verdict, string? Reason))ClassifyMethod.Invoke(null, [session, transcript])!;

    private static RunnerSessionDto Session(Guid sessionId, bool? TranscriptBound, string? TranscriptUnboundReason = null) =>
        new(sessionId, null, DateTime.UtcNow, "Running", null, "", 0,
            TranscriptBound: TranscriptBound, TranscriptUnboundReason: TranscriptUnboundReason);

    private static RunnerTranscriptEvent Entry(Guid sessionId, long sequence, string kind) =>
        new(sessionId, sequence, kind, null, null, DateTimeOffset.UtcNow, null, null, null, null, null, null,
            StopReason: null);

    private static List<JsonElement> MetadataReports(FakeHerdrServer fake) =>
        fake.Requests.Where(IsStatusMetadataReport).ToList();

    private static bool IsStatusMetadataReport(JsonElement request)
    {
        if (request.GetProperty("method").GetString() != "pane.report_metadata")
            return false;
        var parameters = Parameters(request);
        return parameters.TryGetProperty("clear_state_labels", out var clear) && clear.GetBoolean()
            || parameters.TryGetProperty("tokens", out var tokens) && tokens.TryGetProperty("antiphon-state", out _);
    }

    private static JsonElement Parameters(JsonElement request) =>
        request.TryGetProperty("params", out var parameters)
            ? parameters
            : throw new ShouldAssertException($"Metadata request has no params: {request.GetRawText()}");

    private static ulong Sequence(JsonElement parameters)
    {
        parameters.TryGetProperty("seq", out var seq).ShouldBeTrue(parameters.GetRawText());
        return seq.GetUInt64();
    }

    private static void AssertDisplayOnly(FakeHerdrServer fake) =>
        fake.Requests.Any(request => request.GetProperty("method").GetString() == "pane.report_agent")
            .ShouldBeFalse("HerdrStatusPushService must only call pane.report_metadata");

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly HerdrStatusPushService _service;
        private readonly string _logPath;

        private Fixture(FakeHerdrServer fake, SessionRunnerRuntime runtime, HerdrStatusPushService service, string logPath)
        {
            Fake = fake;
            Runtime = runtime;
            _service = service;
            _logPath = logPath;
        }

        public FakeHerdrServer Fake { get; }
        public SessionRunnerRuntime Runtime { get; }

        public static async Task<Fixture> CreateAsync(int heartbeatSeconds, int debounceMs, int exitClearTimeoutMs = 2_000)
        {
            var fake = new FakeHerdrServer();
            fake.Start();
            await fake.WaitUntilListeningAsync();

            var logPath = Path.Combine(Path.GetTempPath(), $"antiphon-herdr-status-push-{Guid.NewGuid():N}");
            var settings = new HerdrSettings
            {
                Enabled = true,
                Session = fake.Session,
                StatusPush = new HerdrStatusPushSettings
                {
                    Enabled = true,
                    DebounceMs = debounceMs,
                    HeartbeatSeconds = heartbeatSeconds,
                    TtlSeconds = 2,
                    ExitClearTimeoutMs = exitClearTimeoutMs,
                },
            };
            var runtime = new SessionRunnerRuntime(
                Options.Create(new SessionRunnerSettings { SessionLogPath = logPath, PtyHostLingerHours = 0.02 }),
                NullLogger<SessionRunnerRuntime>.Instance,
                new HerdrClient(settings),
                new PowershellProcessProbe());
            var service = new HerdrStatusPushService(runtime, new HerdrClient(settings), Options.Create(settings),
                NullLogger<HerdrStatusPushService>.Instance);
            var fixture = new Fixture(fake, runtime, service, logPath);
            await service.StartAsync(fixture._stopping.Token);
            await Task.Delay(50);
            return fixture;
        }

        public async Task<Guid> StartSessionAsync()
        {
            var sessionId = Guid.NewGuid();
            var started = await Runtime.StartAsync(
                new RunnerLaunchRequest(
                    sessionId, "claude", ["--dangerously-skip-permissions"], new Dictionary<string, string>(), _logPath,
                    Cols: 120, Rows: 30, TranscriptEnabled: false, Backend: SessionBackends.Herdr,
                    Herdr: new HerdrLaunchOptions($"test-{sessionId:N}"[..32], "card0163-status-push", _logPath,
                        "card0163-status-push")),
                CancellationToken.None);
            started.Status.ShouldBe("Running");
            return sessionId;
        }

        public async Task<List<JsonElement>> WaitForMetadataReportsAsync(int count, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
            while (DateTime.UtcNow < deadline)
            {
                var reports = MetadataReports(Fake);
                if (reports.Count >= count)
                    return reports;
                await Task.Delay(25);
            }

            return MetadataReports(Fake);
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            await _service.StopAsync(CancellationToken.None);
            await Runtime.DisposeAsync();
            await Fake.DisposeAsync();
            _stopping.Dispose();
            try { Directory.Delete(_logPath, recursive: true); } catch { }
        }
    }
}
