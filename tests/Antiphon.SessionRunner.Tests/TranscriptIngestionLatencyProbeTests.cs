using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

// Investigation probe: measures the real file-to-event path, without a provider, server or chat.
// Sixteen concurrent tailers are a synthetic file workload, not a saturated desktop benchmark.
[NotInParallel("ClaudeConfigDirEnv")]
public class TranscriptIngestionLatencyProbeTests
{
    [Test]
    [Arguments(0)]
    [Arguments(96)]
    public async Task Cold_tail_records_latency_to_a_prompt_after_existing_history(int historyMiB)
    {
        const int chunkBytes = 1 << 20;
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c1259-cold-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "projects", "synthetic");
        Directory.CreateDirectory(project);
        var previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var id = Guid.NewGuid();
        var path = Path.Combine(project, id + ".jsonl");
        var hub = new SessionRunnerEventHub();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var reader = hub.Subscribe(stop.Token);
        var tailer = new TranscriptTailer(id, root, hub, NullLogger.Instance, knownTranscriptPath: path);
        try
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", root);
            // Valid ignored provider records: normalized row count does not bound native file size.
            const string prefix = "{\"type\":\"progress\",\"padding\":\"";
            const string suffix = "\"}\n";
            var block = System.Text.Encoding.ASCII.GetBytes(prefix +
                new string('x', chunkBytes - prefix.Length - suffix.Length) + suffix);
            await using (var file = File.Create(path))
                for (var n = 0; n < historyMiB; n++) await file.WriteAsync(block, stop.Token);
            await File.AppendAllTextAsync(path,
                "{\"type\":\"user\",\"uuid\":\"c1259-cold\",\"message\":{\"role\":\"user\",\"content\":\"CARD-1259 cold-tail marker\"}}\n",
                stop.Token);
            var started = Stopwatch.GetTimestamp();
            tailer.Start();
            RunnerTranscriptEvent? prompt = null;
            await foreach (var frame in reader.ReadAllAsync(stop.Token))
            {
                if (frame.EventName != SessionRunnerEventNames.SessionTranscript) continue;
                prompt = JsonSerializer.Deserialize<RunnerTranscriptEvent>(frame.Json);
                break;
            }
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            prompt.ShouldNotBeNull();
            prompt.Uuid.ShouldBe("c1259-cold");
            prompt.Kind.ShouldBe(TranscriptKinds.UserPrompt);
            prompt.Text.ShouldBe("CARD-1259 cold-tail marker");
            tailer.Snapshot().Entries.Count.ShouldBe(1);
            Console.WriteLine($"C1259 cold-tail historyMiB={historyMiB} bytesBeforePrompt={historyMiB * (long)chunkBytes} " +
                $"startToPromptMs={elapsed:F3}; known binding, generated ignored history, no provider or server");
        }
        finally
        {
            stop.Cancel();
            try { await tailer.DisposeAsync(); }
            finally
            {
                Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previous);
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    [Arguments(1)]
    [Arguments(16)]
    public async Task Appended_prompts_reach_the_event_hub_under_concurrent_file_load(int sessions)
    {
        const int rounds = 32;
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c1259-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "projects", "synthetic");
        Directory.CreateDirectory(project);
        var previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var tailers = new List<TranscriptTailer>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var pending = new ConcurrentDictionary<string, (long Start, TaskCompletionSource<double> Done)>();
        var hub = new SessionRunnerEventHub();
        var reader = hub.Subscribe(stop.Token);
        var lags = new ConcurrentBag<double>();
        var consumer = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in reader.ReadAllAsync(stop.Token))
                {
                    if (frame.EventName != SessionRunnerEventNames.SessionTranscript) continue;
                    var entry = JsonSerializer.Deserialize<RunnerTranscriptEvent>(frame.Json)!;
                    if (entry.Uuid is null || !pending.TryRemove(entry.Uuid, out var sample)) continue;
                    entry.Kind.ShouldBe(TranscriptKinds.UserPrompt);
                    entry.Text.ShouldBe("CARD-1259 synthetic " + entry.Uuid);
                    sample.Done.TrySetResult(Stopwatch.GetElapsedTime(sample.Start).TotalMilliseconds);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        try
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", root);
            var paths = new List<string>();
            for (var lane = 0; lane < sessions; lane++)
            {
                var id = Guid.NewGuid();
                var path = Path.Combine(project, id + ".jsonl");
                await File.WriteAllTextAsync(path, "", stop.Token);
                var tailer = new TranscriptTailer(id, root, hub, NullLogger.Instance, knownTranscriptPath: path);
                tailers.Add(tailer);
                paths.Add(path);
                tailer.Start();
            }

            // Warm each binding separately from the measured append workload.
            while (tailers.Any(t => t.BoundTranscriptPath is null))
                await Task.Delay(10, stop.Token);
            await Task.WhenAll(paths.Select(async (path, lane) =>
            {
                for (var round = 0; round < rounds; round++)
                {
                    await Task.Delay(13 + (round * 37 + lane * 11) % 120, stop.Token);
                    var uuid = $"c1259-{lane}-{round}";
                    var done = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pending.TryAdd(uuid, (Stopwatch.GetTimestamp(), done)).ShouldBeTrue();
                    var line = JsonSerializer.Serialize(new
                    {
                        type = "user", uuid, timestamp = DateTime.UtcNow,
                        message = new { role = "user", content = "CARD-1259 synthetic " + uuid }
                    });
                    await File.AppendAllTextAsync(path, line + "\n", stop.Token);
                    lags.Add(await done.Task.WaitAsync(stop.Token));
                }
            }));
            lags.Count.ShouldBe(sessions * rounds);
            tailers.Sum(t => t.Snapshot().Entries.Count).ShouldBe(sessions * rounds);
            pending.ShouldBeEmpty();
            var sorted = lags.Order().ToArray();
            Console.WriteLine($"C1259 file-to-hub sessions={sessions} samples={sorted.Length} " +
                $"medianMs={sorted[sorted.Length / 2]:F3} p95Ms={sorted[(int)Math.Ceiling(sorted.Length * .95) - 1]:F3} " +
                $"maxMs={sorted[^1]:F3}; stopwatch before append to matching event receipt; no latency SLA assertion");
        }
        finally
        {
            stop.Cancel();
            try
            {
                foreach (var tailer in tailers) await tailer.DisposeAsync();
                await consumer;
            }
            finally
            {
                Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previous);
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
