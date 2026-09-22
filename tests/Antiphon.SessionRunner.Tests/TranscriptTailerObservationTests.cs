using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[NotInParallel("ClaudeConfigDirEnv")]
public class TranscriptTailerObservationTests
{
    [Test]
    public async Task Tail_observation_partial_line_is_unknown()
    {
        await using var world = await BoundWorld.CreateAsync(complete: true, trailingPartial: true);
        var observation = await world.Tailer.ObserveCompactionSilenceAsync(CancellationToken.None);
        observation.IsSuccessful.ShouldBeFalse();
        observation.Status.ShouldBe(CompactionObservationStatuses.Partial);
    }

    [Test]
    public async Task Unreadable_bound_file_is_unknown()
    {
        await using var world = await BoundWorld.CreateAsync(complete: true, trailingPartial: false);

        // Only this test takes an exclusive handle, so only this test needs the extra wait.
        // The tailer publishes BoundTranscriptPath *before* its first read, so CreateAsync's
        // binding wait can still return while the tail loop holds its own read handle — and the
        // FileShare.None open below then loses that race. Parsed entries, by contrast, are
        // appended only after that read scope is disposed, so a full snapshot is the causal
        // signal that no tailer handle is open. The count is this fixture's body
        // (SilentBody(complete: true, trailingPartial: false) writes three lines, one entry
        // each), not a property of transcripts in general; with no further appends the tail loop
        // stops reopening the file once its offset reaches the file length.
        const int fixtureEntryCount = 3;
        var readyBy = DateTime.UtcNow.AddSeconds(8);
        while (world.Tailer.Snapshot().Entries.Count < fixtureEntryCount && DateTime.UtcNow < readyBy)
            await Task.Delay(50);
        world.Tailer.Snapshot().Entries.Count.ShouldBe(
            fixtureEntryCount,
            "the tailer's initial read must have completed before the exclusive handle is taken");

        await using var locked = new FileStream(world.Path, FileMode.Open, FileAccess.Read, FileShare.None);
        var observation = await world.Tailer.ObserveCompactionSilenceAsync(CancellationToken.None);
        observation.IsSuccessful.ShouldBeFalse();
        observation.Status.ShouldBe(CompactionObservationStatuses.Unavailable);
    }

    [Test]
    public async Task Unparsed_new_line_is_unknown()
    {
        await using var world = await BoundWorld.CreateAsync(complete: true, trailingPartial: false);
        await File.AppendAllTextAsync(world.Path, "{not-json}\n");
        var observation = await world.Tailer.ObserveCompactionSilenceAsync(CancellationToken.None);
        observation.IsSuccessful.ShouldBeFalse();
        observation.Status.ShouldBe(CompactionObservationStatuses.Unparsed);
    }

    [Test]
    public async Task Claim_switch_during_read_invalidates_observation()
    {
        await using var world = await BoundWorld.CreateAsync(complete: true, trailingPartial: false);
        var before = world.Tailer.BindingIdentity;
        before.ShouldNotBeNull();
        world.Tailer.CompactionObserveAfterRead = async _ =>
        {
            world.Tailer.NotifyClaimRevoked(world.Path, Guid.NewGuid());
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (world.Tailer.BindingIdentity == before && DateTime.UtcNow < deadline)
                await Task.Delay(50);
        };
        var observation = await world.Tailer.ObserveCompactionSilenceAsync(CancellationToken.None);
        observation.IsSuccessful.ShouldBeFalse();
    }

    [Test]
    public async Task Unsupported_tailer_cannot_certify_silence()
    {
        ITranscriptTailer tailer = new GrokTranscriptTailer(Guid.NewGuid(), Path.GetTempFileName(), new SessionRunnerEventHub(), NullLogger.Instance);
        var observation = await tailer.ObserveCompactionSilenceAsync(CancellationToken.None);
        observation.IsSuccessful.ShouldBeFalse();
        observation.Status.ShouldBe(CompactionObservationStatuses.Unsupported);
        await tailer.DisposeAsync();
    }

    private sealed class BoundWorld : IAsyncDisposable
    {
        public TranscriptTailer Tailer { get; }
        public string Path { get; }
        private readonly string _configDir;
        private readonly string? _previousConfig;

        private BoundWorld(TranscriptTailer tailer, string path, string configDir, string? previousConfig)
        {
            Tailer = tailer;
            Path = path;
            _configDir = configDir;
            _previousConfig = previousConfig;
        }

        public static async Task<BoundWorld> CreateAsync(bool complete, bool trailingPartial)
        {
            var previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            var configDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"antiphon-c79-obs-{Guid.NewGuid():N}");
            var projectDir = System.IO.Path.Combine(configDir, "projects", "cwd");
            Directory.CreateDirectory(projectDir);
            var sessionId = Guid.NewGuid();
            var path = System.IO.Path.Combine(projectDir, sessionId.ToString("D") + ".jsonl");
            await File.WriteAllTextAsync(path, SilentBody(complete, trailingPartial));
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);
            var tailer = new TranscriptTailer(sessionId, System.IO.Path.GetTempPath(), new SessionRunnerEventHub(), NullLogger.Instance);
            tailer.Start();
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (tailer.BoundTranscriptPath is null && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            tailer.BoundTranscriptPath.ShouldNotBeNull();
            return new BoundWorld(tailer, path, configDir, previous);
        }

        public async ValueTask DisposeAsync()
        {
            await Tailer.DisposeAsync();
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _previousConfig);
            try { Directory.Delete(_configDir, true); } catch { /* temp */ }
        }

        private static string SilentBody(bool complete, bool trailingPartial)
        {
            var lines = new[]
            {
                """{"type":"user","uuid":"prompt-1","message":{"role":"user","content":"check the board"}}""",
                """{"type":"system","subtype":"compact_boundary","uuid":"boundary-1","compactMetadata":{"trigger":"auto"}}""",
                "{\"type\":\"user\",\"uuid\":\"cont-1\",\"message\":{\"role\":\"user\",\"content\":\"" + TranscriptKinds.CompactionContinuationPromptPrefix + " summary\"}}",
            };
            var body = string.Join('\n', lines) + "\n";
            if (!complete)
                return "";
            if (trailingPartial)
                body += """{"type":"assistant","uuid":"partial"}""";
            return body;
        }
    }
}
