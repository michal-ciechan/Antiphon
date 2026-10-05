using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[NotInParallel("ClaudeConfigDirEnv")]
public class TerminalSeatReleaseTests
{
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
