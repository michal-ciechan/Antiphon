using System.Net;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1764: a transcript read with a since-cursor on the tailer's own stream returns only the
/// rows after it. Any other cursor, a replaced tailer, or no cursor is the full transcript.
/// </summary>
[Category("Unit")]
public class TranscriptSinceReadTests
{
    private static readonly DateTime Generation = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Since_on_the_current_stream_returns_only_the_later_rows()
    {
        await using var world = World.Arm(rows: 3);
        await using var host = await Host.StartAsync(world.Runtime);

        var full = await host.Transcript(world.Id);
        full.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L]);
        full.Stream.ShouldNotBeNullOrWhiteSpace();
        world.Tailer.Add(2);

        var delta = await host.Transcript(world.Id, TranscriptSince.Query(3, full.Stream!));

        delta.Entries.Select(e => e.Sequence).ShouldBe([4L, 5L]);
        delta.LastSequence.ShouldBe(5);
        delta.Stream.ShouldBe(full.Stream);
        delta.AcceptedStartedAt.ShouldBe(Generation);
    }

    [Test]
    public async Task Since_at_the_last_row_returns_no_rows_and_the_same_sequence()
    {
        await using var world = World.Arm(rows: 2);
        await using var host = await Host.StartAsync(world.Runtime);
        var full = await host.Transcript(world.Id);

        var delta = await host.Transcript(world.Id, TranscriptSince.Query(2, full.Stream!));

        delta.Entries.ShouldBeEmpty();
        delta.LastSequence.ShouldBe(2);
        delta.Stream.ShouldBe(full.Stream);
    }

    [Test]
    public async Task A_replaced_tailer_answers_an_old_cursor_with_its_full_transcript()
    {
        await using var world = World.Arm(rows: 3);
        await using var host = await Host.StartAsync(world.Runtime);
        var first = await host.Transcript(world.Id);

        var replacement = new ListTailer(world.Id);
        replacement.Add(4);
        world.Session.AttachTailerForTest(replacement);
        var after = await host.Transcript(world.Id, TranscriptSince.Query(3, first.Stream!));

        after.Stream.ShouldNotBeNullOrWhiteSpace();
        after.Stream.ShouldNotBe(first.Stream);
        after.Entries.Select(e => e.Sequence).ShouldBe([1L, 2L, 3L, 4L]);
    }

    [Test]
    public async Task Unknown_stream_missing_stream_and_bad_cursor_return_the_full_transcript()
    {
        await using var world = World.Arm(rows: 3);
        await using var host = await Host.StartAsync(world.Runtime);
        var full = await host.Transcript(world.Id);

        (await host.Transcript(world.Id, TranscriptSince.Query(1, "other"))).Entries.Count.ShouldBe(3);
        (await host.Transcript(world.Id, "?since=1")).Entries.Count.ShouldBe(3);
        (await host.Transcript(world.Id, "?since=-1&stream=" + full.Stream)).Entries.Count.ShouldBe(3);
        (await host.Transcript(world.Id, "?since=x&stream=" + full.Stream)).Entries.Count.ShouldBe(3);
        (await host.Transcript(world.Id, TranscriptSince.Query(9, full.Stream!))).Entries.Count.ShouldBe(3);
        (await host.Transcript(world.Id, TranscriptSince.Query(1, full.Stream!))).Entries.Count.ShouldBe(2);
    }

    [Test]
    public async Task Phone_home_transcript_since_returns_only_the_later_rows()
    {
        await using var world = World.Arm(rows: 3);
        var dispatcher = new PhoneHomeCommandDispatcher(
            new PhoneHomeRuntimeAdapter(world.Runtime, RunnerBuildIdentity.Resolve()),
            new PhoneHomeSettings { Enabled = true, AllowedCwd = world.Root, CapacityStatePath = Path.Combine(world.Root, "capacity") });

        var first = await dispatcher.DispatchAsync(Frame(world.Id, null), CancellationToken.None);
        var full = RunnerReadJson.ReadElement(first.Payload, RunnerReadJson.PhoneTranscript)!;
        full.Entries.Count.ShouldBe(3);
        world.Tailer.Add(1);

        var second = await dispatcher.DispatchAsync(
            Frame(world.Id, new { sessionId = world.Id, since = 3, stream = full.Stream }), CancellationToken.None);
        var delta = RunnerReadJson.ReadElement(second.Payload, RunnerReadJson.PhoneTranscript)!;

        delta.Entries.Select(e => e.Sequence).ShouldBe([4L]);
        delta.LastSequence.ShouldBe(4);
        delta.Stream.ShouldBe(full.Stream);
    }

    [Test]
    public async Task Capabilities_advertise_transcript_since()
    {
        PhoneHomeRuntimeAdapter.StaticFeatures.ShouldContain(RunnerCapabilityFeatures.TranscriptSinceV1);
        foreach (var herdr in new[] { false, true })
        {
            await using var world = World.Arm(rows: 0);
            await using var host = await Host.StartAsync(world.Runtime, herdr);
            var response = await host.Get("/capabilities");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).ShouldContain(RunnerCapabilityFeatures.TranscriptSinceV1);
        }
    }

    [Test]
    public void After_returns_the_suffix_past_the_cursor()
    {
        var id = Guid.NewGuid();
        var entries = Enumerable.Range(1, 5).Select(i => ListTailer.Row(id, i)).ToList();

        TranscriptEntries.After(entries, 0).Select(e => e.Sequence).ShouldBe([1L, 2L, 3L, 4L, 5L]);
        TranscriptEntries.After(entries, 2).Select(e => e.Sequence).ShouldBe([3L, 4L, 5L]);
        TranscriptEntries.After(entries, 4).Select(e => e.Sequence).ShouldBe([5L]);
        TranscriptEntries.After(entries, 5).ShouldBeEmpty();
        TranscriptEntries.After(entries, 9).ShouldBeEmpty();
        TranscriptEntries.After([], 3).ShouldBeEmpty();
    }

    private static PhoneHomeFrame Frame(Guid sessionId, object? payload) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Transcript,
            JsonSerializer.SerializeToElement(payload ?? new { sessionId }, PhoneHomeFraming.Json));

    private sealed class World : IAsyncDisposable
    {
        public SessionRunnerRuntime Runtime { get; }
        public SessionRunnerRuntime.RunnerSession Session { get; }
        public ListTailer Tailer { get; }
        public Guid Id { get; }
        public string Root { get; }

        private World(SessionRunnerRuntime runtime, SessionRunnerRuntime.RunnerSession session, ListTailer tailer, Guid id, string root)
        {
            Runtime = runtime;
            Session = session;
            Tailer = tailer;
            Id = id;
            Root = root;
        }

        public static World Arm(int rows)
        {
            var root = Path.Combine(Path.GetTempPath(), "transcript-since-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var settings = new SessionRunnerSettings { SessionLogPath = root };
            var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
            var id = Guid.NewGuid();
            var session = new SessionRunnerRuntime.RunnerSession(id, settings, new SessionRunnerEventHub(), NullLogger.Instance);
            session.ArmPtyReadForTest(Generation, null);
            var tailer = new ListTailer(id);
            tailer.Add(rows);
            session.AttachTailerForTest(tailer);
            runtime.Track(session);
            return new World(runtime, session, tailer, id, root);
        }

        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
    }

    internal sealed class ListTailer(Guid id) : ITranscriptTailer
    {
        private readonly object _gate = new();
        private readonly List<RunnerTranscriptEvent> _entries = new();

        public static RunnerTranscriptEvent Row(Guid sessionId, long sequence) =>
            new(sessionId, sequence, TranscriptKinds.AssistantText, "u" + sequence, null,
                DateTimeOffset.UnixEpoch.AddSeconds(sequence), "assistant", "row " + sequence, null, null, null, null, null);

        public void Add(int count)
        {
            lock (_gate)
            {
                for (var i = 0; i < count; i++)
                    _entries.Add(Row(id, _entries.Count + 1));
            }
        }

        public RunnerTranscriptDto Snapshot()
        {
            lock (_gate)
                return new RunnerTranscriptDto(id, _entries.ToArray(), _entries.Count);
        }

        public string? BoundTranscriptPath => "/owned/conversation.jsonl";
        public string? BindHow => "exact";
        public string? UnboundReason => null;
        public void Start() { }
        public void NotifyChildExited() { }
        public void NotifyClaimRevoked(string path, Guid newOwner) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Host(WebApplication app, HttpClient client)
        {
            _app = app;
            _client = client;
        }

        public static async Task<Host> StartAsync(SessionRunnerRuntime runtime, bool herdr = false)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Test" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(runtime);
            builder.Services.Configure<HerdrSettings>(settings => settings.Enabled = herdr);
            builder.Services.Configure<HostStatsSettings>(_ => { });
            var app = builder.Build();
            app.UseRunnerExceptionMapping();
            app.MapRunnerCapabilitiesRoute(new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow));
            app.MapSessionTranscriptRoute();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            new Uri(address).Port.ShouldNotBe(17204);
            return new Host(app, new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(20) });
        }

        public Task<HttpResponseMessage> Get(string path) => _client.GetAsync(path);

        public async Task<RunnerTranscriptDto> Transcript(Guid sessionId, string query = "")
        {
            using var response = await _client.GetAsync($"/sessions/{sessionId}/transcript{query}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), RunnerReadJson.PhoneTranscript)!;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _client.Dispose();
        }
    }
}
