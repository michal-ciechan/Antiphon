using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[NotInParallel("ClaudeConfigDirEnv")]
public class ResumeReceiptReadinessTests
{
    [Test]
    public async Task C1574_Reader_progress_tracks_complete_current_file()
    {
        // Two actual read chunks, with a partial JSON record at the chunk boundary.
        await using var world = new World(Prompt("history", new string('x', (1 << 20) + 128)));
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processingHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processingRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var polls = 0;
        var reads = 0;
        world.Tailer.ReadinessAfterRead = async ct =>
        {
            if (Interlocked.Increment(ref reads) == 2)
            {
                processingHeld.TrySetResult();
                await processingRelease.Task.WaitAsync(ct);
            }
        };
        world.Tailer.TerminalObservation.BeforePoll = async ct =>
        {
            if (Interlocked.Increment(ref polls) == 2)
            {
                held.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        world.Tailer.Start();
        try
        {
            await held.Task.WaitAsync(TimeSpan.FromSeconds(8));
            (world.Tailer.Snapshot().Readiness?.Status).ShouldBe("Behind");
        }
        finally { release.TrySetResult(); }
        try
        {
            await processingHeld.Task.WaitAsync(TimeSpan.FromSeconds(8));
            (world.Tailer.Snapshot().Readiness?.Status).ShouldBe("Behind", "bytes read are not yet parsed");
            world.Tailer.Snapshot().Entries.ShouldBeEmpty();
        }
        finally { processingRelease.TrySetResult(); }
        await Ready(world.Tailer);
        var before = world.Tailer.Snapshot();
        before.Entries.Single().Uuid.ShouldBe("history");
        before.Readiness!.ParsedThrough.ShouldBe(new FileInfo(world.Path).Length);
        before.Readiness.BindingId.ShouldNotBeNullOrEmpty();
        before.Readiness.BindingId!.ShouldNotContain(world.Root);

        // Hold the next poll so the snapshot must freshly notice the append itself.
        await world.Tailer.TerminalObservation.ReadGate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(world.Path, Prompt("receipt", "restart note"));
            (world.Tailer.Snapshot().Readiness?.Status).ShouldBe("Behind");
        }
        finally { world.Tailer.TerminalObservation.ReadGate.Release(); }
        await Ready(world.Tailer);
        var after = world.Tailer.Snapshot();
        after.Entries.Select(e => e.Uuid).ShouldBe(new[] { "history", "receipt" });
        after.Entries.Select(e => e.Sequence).ShouldBe(new long[] { 1, 2 });
        after.Readiness!.BindingId.ShouldBe(before.Readiness.BindingId);
    }

    [Test]
    public async Task C1574_Invalid_binding_or_partial_tail_is_unknown()
    {
        foreach (var variant in new[] { "partial", "malformed", "revoked", "replacement", "truncated", "ignored", "fork" })
        {
            Console.WriteLine("C1574 reader variant=" + variant);
            await using var world = new World(Prompt("initial", "hello"));
            world.Tailer.Start();
            await Ready(world.Tailer);
            if (variant == "fork")
            {
                var oldBinding = world.Tailer.Snapshot().Readiness!.BindingId;
                var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var fork = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(world.Path)!, Guid.NewGuid() + ".jsonl");
                world.Tailer.ReadinessAfterRead = async ct =>
                {
                    if (world.Tailer.BoundTranscriptPath == fork)
                    {
                        held.TrySetResult();
                        await release.Task.WaitAsync(ct);
                    }
                };
                try
                {
                    File.SetLastWriteTimeUtc(world.Path, DateTime.UtcNow.AddMinutes(-1));
                    await File.WriteAllTextAsync(fork, JsonSerializer.Serialize(new
                    {
                        type = "user", uuid = "fork", cwd = world.Root,
                        message = new { role = "user", content = "the first prompt after the clear" }
                    }) + "\n");
                    await held.Task.WaitAsync(TimeSpan.FromSeconds(8));
                    var observation = world.Tailer.Snapshot().Readiness!;
                    observation.BindingId.ShouldNotBe(oldBinding);
                    observation.Status.ShouldBe("Behind");
                    observation.ParsedThrough.ShouldBe(0);
                }
                finally { release.TrySetResult(); }
                await Ready(world.Tailer);
                world.Tailer.Snapshot().Entries.Last().Uuid.ShouldBe("fork");
                continue;
            }
            await world.Tailer.TerminalObservation.ReadGate.WaitAsync();
            try
            {
                switch (variant)
                {
                    case "partial": await File.AppendAllTextAsync(world.Path, "{\"type\":"); break;
                    case "malformed": await File.AppendAllTextAsync(world.Path, "{broken}\n"); break;
                    case "revoked": world.Tailer.NotifyClaimRevoked(world.Path, Guid.NewGuid()); break;
                    case "replacement":
                        File.Move(world.Path, world.Path + ".old");
                        File.Copy(world.Path + ".old", world.Path);
                        break;
                    case "truncated": await File.WriteAllTextAsync(world.Path, ""); break;
                    case "ignored": await File.AppendAllTextAsync(world.Path, "{\"type\":\"unrecognized-valid-record\"}\n"); break;
                }
                (world.Tailer.Snapshot().Readiness?.Status).ShouldNotBe("Ready", variant);
            }
            finally { world.Tailer.TerminalObservation.ReadGate.Release(); }
            if (variant == "ignored") await Ready(world.Tailer);
            else if (variant is "partial" or "malformed")
            {
                // Observe after the entire suffix has actually been consumed.
                await Until(() => world.Tailer.Snapshot().Readiness?.ParsedThrough == new FileInfo(world.Path).Length);
                (world.Tailer.Snapshot().Readiness?.Status).ShouldBe("Unknown", variant);
            }
        }
    }

    [Test]
    public async Task C1574_Truncation_regrowth_and_invalid_utf8_stay_unknown()
    {
        var unsafeVariants = new List<string>();
        foreach (var variant in new[] { "truncation-regrowth", "invalid-utf8" })
        {
            Console.WriteLine("C1574 reader variant=" + variant);
            var initial = Prompt("initial", "hello");
            await using var world = new World(initial);
            world.Tailer.Start();
            await Ready(world.Tailer);
            await world.Tailer.TerminalObservation.ReadGate.WaitAsync();
            try
            {
                if (variant == "truncation-regrowth")
                {
                    await File.WriteAllTextAsync(world.Path, "");
                    (world.Tailer.Snapshot().Readiness?.Status).ShouldBe("Unknown");
                    // Same inode and extent, different complete content: the old parsed prefix
                    // cannot become authoritative simply because the file grew again.
                    var replacement = Prompt("changed", "other");
                    replacement.Length.ShouldBe(initial.Length);
                    await File.WriteAllTextAsync(world.Path, replacement);
                    if (world.Tailer.Snapshot().Readiness?.Status == "Ready")
                        unsafeVariants.Add(variant);
                }
                else
                {
                    await using var append = new FileStream(world.Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    await append.WriteAsync(System.Text.Encoding.UTF8.GetBytes("{\"type\":\"user\",\"uuid\":\"invalid\",\"message\":{\"role\":\"user\",\"content\":\""));
                    await append.WriteAsync(new byte[] { 0xff });
                    await append.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\"}}\n"));
                }
            }
            finally { world.Tailer.TerminalObservation.ReadGate.Release(); }
            if (variant == "invalid-utf8")
            {
                await Until(() => world.Tailer.Snapshot().Readiness?.ParsedThrough == new FileInfo(world.Path).Length);
                if (world.Tailer.Snapshot().Readiness?.Status == "Ready")
                    unsafeVariants.Add(variant);
            }
        }
        unsafeVariants.ShouldBeEmpty("neither lost history nor replacement-decoded input can certify a complete current transcript");
    }

    private static async Task Ready(TranscriptTailer tailer) =>
        await Until(() => tailer.Snapshot().Readiness?.Status == "Ready");

    private static async Task Until(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(10);
        predicate().ShouldBeTrue("reader must reach the expected observation within 8 seconds");
    }

    private static string Prompt(string uuid, string body) =>
        JsonSerializer.Serialize(new { type = "user", uuid, message = new { role = "user", content = body } }) + "\n";

    private sealed class World : IAsyncDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c1574-" + Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public TranscriptTailer Tailer { get; }
        private readonly string? _previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");

        public World(string body)
        {
            var id = Guid.NewGuid();
            var dir = System.IO.Path.Combine(Root, "projects", "cwd");
            Directory.CreateDirectory(dir);
            Path = System.IO.Path.Combine(dir, id + ".jsonl");
            File.WriteAllText(Path, body);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Root);
            var inputs = new SessionInputLog();
            inputs.Append("the first prompt after the clear");
            Tailer = new TranscriptTailer(id, Root, new SessionRunnerEventHub(), NullLogger.Instance,
                inputLog: inputs, forkScanInterval: TimeSpan.FromMilliseconds(1));
        }

        public async ValueTask DisposeAsync()
        {
            await Tailer.DisposeAsync();
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _previous);
            Directory.Delete(Root, true);
        }
    }
}
