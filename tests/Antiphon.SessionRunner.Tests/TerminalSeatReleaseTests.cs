using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.SessionRunner.Contracts;
using Antiphon.PtyHost.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[NotInParallel("ClaudeConfigDirEnv")]
public class TerminalSeatReleaseTests
{
    [Test]
    public async Task Two_observations_require_the_full_safety_margin()
    {
        await using var world = new SeatWorld("Codex");
        await world.StartAsync();
        await world.DeliverAsync();
        var first = await world.ObserveAsync();
        first.Status.ShouldBe(TerminalSeatQualificationStatus.Waiting);
        first.StableFor.ShouldBe(TimeSpan.Zero);
        first.Token.ShouldBeNull();
        world.ServerClock.Advance(TimeSpan.FromDays(2));
        (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Waiting,
            "the server clock cannot supply runner elapsed time");
        world.Clock.Advance(TimeSpan.FromMilliseconds(119999));
        var early = await world.ObserveAsync();
        early.Status.ShouldBe(TerminalSeatQualificationStatus.Waiting, "119.999 seconds is not qualified");
        early.Token.ShouldBeNull();
        world.Clock.Advance(TimeSpan.FromMilliseconds(1));
        var qualified = await world.ObserveAsync();
        qualified.Status.ShouldBe(TerminalSeatQualificationStatus.Qualified, "exactly 120 seconds qualifies");
        qualified.StableFor.ShouldBe(TimeSpan.FromSeconds(120));
        qualified.Token.ShouldNotBeNullOrWhiteSpace();
        qualified.FirstObservedAt.ShouldBe(first.FirstObservedAt);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1));
        var later = await world.ObserveAsync();
        later.Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        later.Token.ShouldBe(qualified.Token, "unchanged evidence retains its opaque token");
        world.AssertRetained();
    }

    [Test]
    public async Task Old_turn_end_does_not_qualify_a_new_generation()
    {
        foreach (var provider in new[] { "Claude", "Grok", "Codex" })
        {
            await using var world = new SeatWorld(provider);
            await world.StartAsync();
            // Old history is idle, but none of it follows the current delivery floor.
            var old = await world.ObserveAsync();
            old.Status.ShouldBe(TerminalSeatQualificationStatus.OldPrompt, "old-end-rejected: " + provider);
            world.Clock.Advance(TimeSpan.FromHours(1));
            (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.OldPrompt);
            await world.DeliverAsync();
            (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Waiting);
            world.Clock.Advance(TimeSpan.FromSeconds(120));
            (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Qualified,
                "a real later delivered prompt and end must be accepted: " + provider);
            world.Child.Inputs.ShouldContain(SeatWorld.TaskPrompt);
            world.AssertRetained();
        }
    }

    [Test]
    public async Task Unavailable_observation_discards_qualification()
    {
        await using var world = new SeatWorld("Grok");
        await world.StartAsync();
        await world.DeliverAsync();
        (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Waiting);
        world.Clock.Advance(TimeSpan.FromSeconds(60));
        world.Tail.Observer.OpenRead = _ => throw new IOException("fixture unavailable");
        var unavailable = await world.ObserveAsync();
        unavailable.Status.ShouldBe(TerminalSeatQualificationStatus.Unknown);
        unavailable.Token.ShouldBeNull();
        world.Tail.Observer.OpenRead = null;
        world.Clock.Advance(TimeSpan.FromSeconds(60));
        var recovered = await world.ObserveAsync();
        recovered.Status.ShouldBe(TerminalSeatQualificationStatus.Waiting,
            "recovery at old t+120 must start a new window");
        recovered.StableFor.ShouldBe(TimeSpan.Zero);
        world.Clock.Advance(TimeSpan.FromSeconds(120));
        (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        world.AssertRetained();
    }

    [Test]
    public async Task Restart_invalidates_volatile_observation_tokens()
    {
        await using var world = new SeatWorld("Claude");
        await world.StartAsync();
        await world.DeliverAsync();
        await world.ObserveAsync();
        world.Clock.Advance(TimeSpan.FromSeconds(120));
        var before = await world.ObserveAsync();
        before.Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        before.Token.ShouldNotBeNullOrWhiteSpace();
        (await world.AuthorizeAsync(before.Token)).Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        (await world.AuthorizeAsync("not-an-issued-token")).Status.ShouldBe(TerminalSeatQualificationStatus.StaleObservation);
        var proof = world.Runtime.TerminalSeatProofFor(world.Tail.SessionId)!;
        await world.RestartAsync();
        (await world.AuthorizeAsync(before.Token)).Status.ShouldBe(TerminalSeatQualificationStatus.StaleObservation,
            "a prior-process token cannot authorize the restarted runtime");
        var restarted = await world.ObserveAsync();
        restarted.Status.ShouldBe(TerminalSeatQualificationStatus.Waiting, "restart requires a new runner window");
        restarted.Token.ShouldBeNull();
        var newEpoch = world.Runtime.TerminalSeatProofFor(world.Tail.SessionId)!.RuntimeEpoch;
        TerminalSeatQualification.Authorize(proof, proof.RuntimeEpoch, proof.Session, proof.Request,
            proof.Transcript, proof.InputRevision, proof.OutputRevision, world.Clock)
            .ShouldBe(TerminalSeatQualificationStatus.Qualified, "otherwise-valid control for the epoch guard");
        TerminalSeatQualification.Authorize(proof, newEpoch, proof.Session, proof.Request,
            proof.Transcript, proof.InputRevision, proof.OutputRevision, world.Clock)
            .ShouldBe(TerminalSeatQualificationStatus.StaleObservation,
                "old-epoch proof must fail before a token-cache miss or different object can mask the guard");
        world.Clock.Advance(TimeSpan.FromSeconds(120));
        var qualified = await world.ObserveAsync();
        qualified.Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        qualified.Token.ShouldNotBe(before.Token, "a restarted runtime must not recreate an old token");
        world.AssertRetained();
    }

    [Test]
    [Arguments("Claude")]
    [Arguments("Grok")]
    [Arguments("Codex")]
    public async Task Fresh_tail_reads_each_provider(string provider)
    {
        await using var world = new TailWorld(provider);
        await world.StartAsync();
        TranscriptWorkingState.Classify(world.Tailer.Snapshot().Entries)
            .ShouldBe(TranscriptWorkingState.WorkingVerdict.Idle);
        var idle = await world.ObserveAsync();
        idle.Verdict.ShouldBe(TerminalTranscriptVerdict.Idle);

        await world.AppendAsync(world.Prompt("unread prompt", "second"));
        var fresh = await world.ObserveAsync();
        fresh.Verdict.ShouldBe(TerminalTranscriptVerdict.Working,
            $"{provider} unread native prompt must outrank the cached idle turn");
        fresh.Status.ShouldBe(TerminalTranscriptReadStatus.Success);
        fresh.TranscriptRevision.ShouldBeGreaterThan(idle.TranscriptRevision);
        fresh.FileRevision.ShouldNotBe(idle.FileRevision);
        fresh.BindingIdentity.ShouldBe(idle.BindingIdentity);
        fresh.LastPromptRevision.ShouldNotBeNull();
        fresh.LastEndRevision.ShouldNotBeNull();
        fresh.LastPromptRevision.Value.ShouldBeGreaterThan(fresh.LastEndRevision.Value);
        fresh.ConsumedBytes.ShouldBe(new FileInfo(world.Path).Length);
        TranscriptWorkingState.Classify(world.Tailer.Snapshot().Entries)
            .ShouldBe(TranscriptWorkingState.WorkingVerdict.Idle, "fresh reads must not advance ingestion");

        // End that turn, then observe activity without a new prompt. Grok buffers chunks;
        // inspection must flush its private normalizer, never the poller's normalizer.
        await world.AppendAsync(world.End("second"));
        (await world.ObserveAsync()).Verdict.ShouldBe(TerminalTranscriptVerdict.Idle);
        await world.AppendAsync(world.Activity("third"));
        (await world.ObserveAsync()).Verdict.ShouldBe(TerminalTranscriptVerdict.Working,
            $"{provider} unread output/tool activity must veto idle");
    }

    [Test]
    public async Task Unknown_or_partial_tail_never_authorizes_release()
    {
        foreach (var provider in new[] { "Claude", "Grok", "Codex" })
        {
            await using (var unbound = new TailWorld(provider))
                (await unbound.ObserveAsync()).Verdict.ShouldBe(TerminalTranscriptVerdict.Unknown,
                    $"{provider}: an unbound file is not evidence");

            foreach (var defect in new[] { "empty", "missing", "unreadable", "partial", "malformed", "utf8", "budget" })
            {
                await using var world = new TailWorld(provider);
                await world.StartAsync();
                (await world.ObserveAsync()).Verdict.ShouldBe(TerminalTranscriptVerdict.Idle);
                switch (defect)
                {
                    case "empty": File.WriteAllText(world.Path, ""); break;
                    case "missing": File.Delete(world.Path); break;
                    case "unreadable": world.Observer.OpenRead = _ => throw new UnauthorizedAccessException(); break;
                    case "partial": await world.AppendAsync(world.Prompt("partial", "next").TrimEnd('\n')); break;
                    case "malformed": await world.AppendAsync("{invalid json}\n"); break;
                    case "utf8":
                        await using (var stream = new FileStream(world.Path, FileMode.Append))
                            await stream.WriteAsync(new byte[] { 0xff, (byte)'\n' });
                        break;
                    case "budget":
                        using (var stream = new FileStream(world.Path, FileMode.Open))
                            stream.SetLength(TerminalSeatReleaseObservation.MaximumBytes + 1);
                        break;
                }
                (await world.ObserveAsync()).Verdict.ShouldBe(TerminalTranscriptVerdict.Unknown,
                    $"{provider}: {defect} must refuse even with an older idle turn");
            }
        }
    }

    [Test]
    public async Task Binding_changes_during_read_refuse_qualification()
    {
        foreach (var provider in new[] { "Claude", "Grok", "Codex" })
        foreach (var change in new[] { "replace", "truncate", "grow", "revoke" })
        {
            if (provider == "Grok" && change == "revoke") continue; // deterministic path, no claim registry
            await using var world = new TailWorld(provider);
            await world.StartAsync();
            (await world.ObserveAsync()).Verdict.ShouldBe(TerminalTranscriptVerdict.Idle);
            world.Observer.AfterRead = async _ =>
            {
                switch (change)
                {
                    case "replace":
                        var replacement = world.Path + ".replacement";
                        // Same byte length and mtime, different contents and creation identity.
                        File.WriteAllText(replacement, File.ReadAllText(world.Path).Replace("hello", "other"));
                        File.SetLastWriteTimeUtc(replacement, File.GetLastWriteTimeUtc(world.Path));
                        File.Move(replacement, world.Path, overwrite: true);
                        break;
                    case "truncate": File.WriteAllText(world.Path, world.End("first")); break;
                    case "grow": await world.AppendAsync(world.Prompt("race", "second")); break;
                    case "revoke":
                        world.Claims.Release(world.Path, world.SessionId);
                        world.Claims.TryClaim(world.Path, Guid.NewGuid()).Claimed.ShouldBeTrue();
                        world.Tailer.NotifyClaimRevoked(world.Path, Guid.NewGuid());
                        break;
                }
            };
            var observed = await world.ObserveAsync();
            observed.Verdict.ShouldBe(TerminalTranscriptVerdict.Unknown,
                $"{provider}: {change} invalidates the consumed binding");
            observed.Status.ShouldBe(TerminalTranscriptReadStatus.StaleObservation);
        }
    }

    [Test]
    public async Task Fresh_observation_does_not_publish_duplicate_entries()
    {
        foreach (var provider in new[] { "Claude", "Grok", "Codex" })
        {
            await using var world = new TailWorld(provider);
            await world.StartAsync();
            var initial = world.DrainTranscript();
            initial.Length.ShouldBeGreaterThan(0);
            var before = world.Tailer.Snapshot().LastSequence;
            await world.AppendAsync(world.Prompt("second prompt", "second") + world.Activity("second") + world.End("second"));
            await world.ObserveAsync();
            await world.ObserveAsync();
            world.DrainTranscript().ShouldBeEmpty("observation must publish zero events/callbacks");
            world.Tailer.Snapshot().LastSequence.ShouldBe(before);
            await world.PollAsync();
            var published = world.DrainTranscript();
            published.Select(e => e.Kind).ShouldBe(new[]
            {
                TranscriptKinds.UserPrompt,
                provider == "Claude" ? TranscriptKinds.ToolCall
                    : provider == "Grok" ? TranscriptKinds.AssistantText : TranscriptKinds.Thinking,
                TranscriptKinds.TurnEnd
            }, "private inspection must preserve every later ingestion part exactly once");
            published.Count(e => e.Kind == TranscriptKinds.UserPrompt && e.Text == "second prompt").ShouldBe(1);
            published.Count(e => e.Kind == TranscriptKinds.TurnEnd).ShouldBe(1);
            published.Select(e => e.Sequence).Distinct().Count().ShouldBe(published.Length);
            published.Select(e => e.Sequence).ShouldBe(Enumerable.Range((int)before + 1, published.Length).Select(i => (long)i));
            await world.PollAsync();
            world.DrainTranscript().ShouldBeEmpty("the next poll must not re-emit the same bytes");
        }
    }

    private sealed class SeatWorld : IAsyncDisposable
    {
        internal const string TaskPrompt = "[antiphon-task:c667-s1b] Current generation delivery, complete distinctive task prompt.";
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c667-seat-" + Guid.NewGuid().ToString("N"));
        private readonly SessionRunnerSettings _settings;
        private readonly string _manifest;
        private readonly string _sidecar;
        private readonly DateTime _generation = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        public TailWorld Tail { get; }
        public SeatChild Child { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));
        public FakeTimeProvider ServerClock { get; } = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
        public SessionRunnerRuntime Runtime { get; private set; }
        public TerminalSeatObservationRequest Request { get; private set; } = null!;

        public SeatWorld(string provider)
        {
            Tail = new TailWorld(provider);
            _settings = new SessionRunnerSettings { SessionLogPath = _root };
            _manifest = PtyHostManifest.PathFor(_settings.PtyHostManifestDir, Tail.SessionId);
            _sidecar = TranscriptSidecar.PathFor(_root, Tail.SessionId);
            new PtyHostManifest
            {
                SessionId = Tail.SessionId, PipeName = "fixture-only-no-process", HostPid = 1,
                HostStartTimeUtc = _generation, CreatedAtUtc = _generation, AcceptedStartedAt = _generation
            }.SaveAtomic(_manifest);
            new TranscriptSidecar
            {
                SessionId = Tail.SessionId, ChildStartUtc = _generation, TranscriptPath = Tail.Path,
                Cwd = System.IO.Path.GetDirectoryName(Tail.Path), UpdatedAtUtc = _generation
            }.SaveAtomic(_sidecar);
            Runtime = CreateRuntime();
        }

        private SessionRunnerRuntime CreateRuntime() => new(Options.Create(_settings),
            NullLogger<SessionRunnerRuntime>.Instance, timeProvider: Clock);

        private void Bind()
        {
            var session = new SessionRunnerRuntime.RunnerSession(Tail.SessionId, _settings,
                new SessionRunnerEventHub(), NullLogger.Instance);
            session.BindChildForTest(Child, Tail.Tailer, _generation);
            Runtime.Track(session);
        }

        public async Task StartAsync()
        {
            await Tail.StartAsync();
            Bind();
            var baseline = await Tail.ObserveAsync();
            Request = new(Runtime.RunnerStoreId, _generation, baseline.BindingIdentity!, baseline.TranscriptRevision);
        }

        public async Task DeliverAsync()
        {
            await Runtime.SendInputAsync(Tail.SessionId, TaskPrompt, CancellationToken.None);
            await Runtime.SendInputAsync(Tail.SessionId, "\r", CancellationToken.None);
            await Tail.AppendAsync(Tail.Prompt(TaskPrompt, "current") + Tail.End("current"));
        }

        public Task<TerminalSeatObservation> ObserveAsync() =>
            Runtime.ObserveTerminalSeatAsync(Tail.SessionId, Request, CancellationToken.None);

        public Task<TerminalSeatObservation> AuthorizeAsync(string token) =>
            Runtime.AuthorizeTerminalSeatTokenAsync(Tail.SessionId, Request, token, CancellationToken.None);

        public async Task RestartAsync()
        {
            // Detach the real tailer before runtime disposal; the same native transcript stays
            // owned by TailWorld, like the transcript/child surviving a runner process restart.
            Runtime.DetachTerminalTailerForTest(Tail.SessionId);
            await Runtime.DisposeAsync();
            Runtime = CreateRuntime();
            Runtime.RunnerStoreId.ShouldBe(Request.ExpectedRunnerStoreId);
            Bind();
        }

        public void AssertRetained()
        {
            Child.Kills.ShouldBe(0, "S1b has no signal route");
            Runtime.LiveSessionCount.ShouldBe(1);
            Runtime.List().ShouldContain(s => s.SessionId == Tail.SessionId);
            File.Exists(_manifest).ShouldBeTrue("read-only qualification retains manifest custody");
            File.Exists(_sidecar).ShouldBeTrue("read-only qualification retains transcript binding");
        }

        public async ValueTask DisposeAsync()
        {
            Runtime.DetachTerminalTailerForTest(Tail.SessionId);
            await Runtime.DisposeAsync();
            await Tail.DisposeAsync();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class SeatChild : ISessionChild
    {
        public List<string> Inputs { get; } = [];
        public int Kills { get; private set; }
        public Task<ChildStarted> LaunchAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task WriteAsync(string input, CancellationToken ct) { Inputs.Add(input); return Task.CompletedTask; }
        public Task ResizeAsync(int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> KillAsync(CancellationToken ct) { Kills++; return Task.FromResult(false); }
        public Task<ChildScreen?> ReadScreenAsync(CancellationToken ct) => Task.FromResult<ChildScreen?>(null);
        public event Action<ChildExit>? Exited { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TailWorld : IAsyncDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c667-" + Guid.NewGuid().ToString("N"));
        private readonly string? _oldClaudeConfig = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        private readonly string _provider;
        private readonly CancellationTokenSource _hubLifetime = new();
        private readonly SemaphoreSlim _pollPermit = new(0);
        private readonly Channel<bool> _pollArrived = Channel.CreateUnbounded<bool>();
        private readonly ChannelReader<RunnerServerSentEvent> _events;
        public Guid SessionId { get; } = Guid.NewGuid();
        public TranscriptClaimRegistry Claims { get; } = new();
        public string Path { get; }
        public ITranscriptTailer Tailer { get; }
        public TerminalSeatReleaseObservation Observer { get; }

        public TailWorld(string provider)
        {
            _provider = provider;
            Directory.CreateDirectory(_root);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _root);
            Path = System.IO.Path.Combine(_root, "transcript.jsonl");
            File.WriteAllText(Path, Prompt("hello", "first") + End("first"));
            var hub = new SessionRunnerEventHub();
            _events = hub.Subscribe(_hubLifetime.Token);
            // Real sidecar adoption/claim logic, restricted to this fixture's private root.
            Tailer = provider switch
            {
                "Claude" => new TranscriptTailer(SessionId, _root, hub, NullLogger.Instance,
                    knownTranscriptPath: Path, claims: Claims, forkScanInterval: TimeSpan.FromDays(1)),
                "Grok" => new GrokTranscriptTailer(SessionId, Path, hub, NullLogger.Instance,
                    pollInterval: TimeSpan.FromMilliseconds(1)),
                _ => new CodexTranscriptTailer(SessionId, _root, hub, NullLogger.Instance,
                    knownTranscriptPath: Path, sessionsRoot: _root, claims: Claims,
                    pollInterval: TimeSpan.FromMilliseconds(1))
            };
            Observer = Tailer switch
            {
                TranscriptTailer t => t.TerminalObservation,
                GrokTranscriptTailer t => t.TerminalObservation,
                CodexTranscriptTailer t => t.TerminalObservation,
                _ => throw new InvalidOperationException()
            };
            Observer.BeforePoll = async ct =>
            {
                await _pollArrived.Writer.WriteAsync(true, ct);
                await _pollPermit.WaitAsync(ct);
            };
        }

        public async Task StartAsync()
        {
            Tailer.Start();
            await AwaitPollAsync();
            await PollAsync();
        }

        public async Task PollAsync()
        {
            _pollPermit.Release();
            await AwaitPollAsync();
        }

        private async Task AwaitPollAsync() =>
            await _pollArrived.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        public Task AppendAsync(string text) => File.AppendAllTextAsync(Path, text, new UTF8Encoding(false));
        public Task<TerminalTranscriptObservation> ObserveAsync() => Tailer.ObserveTerminalSeatAsync(CancellationToken.None);

        public RunnerTranscriptEvent[] DrainTranscript()
        {
            var rows = new List<RunnerTranscriptEvent>();
            while (_events.TryRead(out var evt))
                if (evt.EventName == SessionRunnerEventNames.SessionTranscript)
                    rows.Add(JsonSerializer.Deserialize<RunnerTranscriptEvent>(evt.Json)!);
            return rows.ToArray();
        }

        public string Prompt(string text, string id) => _provider switch
        {
            "Claude" => JsonSerializer.Serialize(new { type = "user", uuid = id, message = new { role = "user", content = text } }) + "\n",
            "Grok" => Grok(new { sessionUpdate = "user_message_chunk", content = new { type = "text", text } }, id),
            _ => Codex(new { type = "user_message", message = text }, id)
        };

        public string End(string id) => _provider switch
        {
            "Claude" => JsonSerializer.Serialize(new { type = "assistant", uuid = id + "-end", message = new { role = "assistant", content = Array.Empty<object>(), stop_reason = "end_turn" } }) + "\r\n",
            "Grok" => Grok(new { sessionUpdate = "turn_completed", prompt_id = id, stop_reason = "end_turn" }, id),
            _ => Codex(new { type = "task_complete", turn_id = id }, id)
        };

        public string Activity(string id) => _provider switch
        {
            "Claude" => JsonSerializer.Serialize(new { type = "assistant", uuid = id + "-tool", message = new { role = "assistant", content = new[] { new { type = "tool_use", id, name = "read", input = new { path = "C:\\fixture\\file" } } }, stop_reason = "tool_use" } }) + "\n",
            "Grok" => Grok(new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = "still working" } }, id),
            _ => Codex(new { type = "agent_reasoning", text = "still working" }, id)
        };

        private static string Grok(object update, string id) => JsonSerializer.Serialize(new
        {
            method = "session/update", @params = new { update, _meta = new { eventId = Guid.NewGuid().ToString(), promptId = id } }
        }) + "\n";
        private static string Codex(object payload, string id) => JsonSerializer.Serialize(new
        {
            type = "event_msg", timestamp = "2026-10-01T00:00:00Z", id, payload
        }) + "\n";

        public async ValueTask DisposeAsync()
        {
            await Tailer.DisposeAsync();
            await _hubLifetime.CancelAsync();
            _hubLifetime.Dispose();
            _pollPermit.Dispose();
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _oldClaudeConfig);
            Directory.Delete(_root, recursive: true);
        }
    }
}
