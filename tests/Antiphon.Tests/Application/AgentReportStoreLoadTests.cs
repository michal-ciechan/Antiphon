using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1605 V-12. Opt-in production-budget sample. Gate contention is the load;
/// CPU burners are not part of this fixture.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class AgentReportStoreLoadTests
{
    [Test]
    public async Task Store_samples_record_normal_and_contended_phases()
    {
        if (Environment.GetEnvironmentVariable("ANTIPHON_REPORT_STORE_MEASUREMENTS") != "1")
            throw new SkipTestException("CARD-1605 load sampling is opt-in via ANTIPHON_REPORT_STORE_MEASUREMENTS=1");
        NativeLinuxGate.Require("CARD-1605 load telemetry requires native Linux");

        await using var workspace = await ReportBudgetWorkspace.CreateAsync();
        var gate = new GitProcessGate(8);
        var logs = new PhaseLog();
        var store = workspace.CreateStore(gate, logger: logs);
        var before = ReadAmbient();
        var samples = new List<Sample>(80);

        await StoreOneAsync(workspace, store, logs, "warmup", Payload("warmup", 4096), samples, keep: false);

        await RunSequentialAsync(workspace, store, logs, gate, "normal-4k", 4096, samples);
        await RunSequentialAsync(workspace, store, logs, gate, "normal-64k", 65536, samples);
        await RunLoadedAsync(workspace, store, logs, gate, "loaded-4k", 4096, samples);
        await RunLoadedAsync(workspace, store, logs, gate, "loaded-64k", 65536, samples);
        var after = ReadAmbient();

        samples.Count.ShouldBe(80);
        var report = new StringBuilder();
        report.AppendLine("source=CARD-1605");
        report.AppendLine("processors=" + Environment.ProcessorCount);
        report.AppendLine("ambient-before");
        report.AppendLine(before);
        report.AppendLine("ambient-after");
        report.AppendLine(after);
        foreach (var bucket in samples.GroupBy(sample => sample.Bucket))
        {
            var rows = bucket.ToList();
            rows.Count.ShouldBe(20, bucket.Key);
            var refusals = rows.Where(row => row.Reason is not null).GroupBy(row => row.Reason!).ToDictionary(group => group.Key, group => group.Count());
            foreach (var reason in refusals.Keys)
                reason.ShouldBe("io-budget-expired", bucket.Key + " refused with " + reason);
            rows.ShouldNotContain(row => row.Reason == "preparation-budget-expired");
            var successes = rows.Where(row => row.Path is not null).ToList();
            successes.Count.ShouldBeGreaterThan(0, bucket.Key + " was all refused; the bucket is incomplete");
            var preparation = rows.Select(row => row.PreparationMs!.Value).ToList();
            var prepP95 = Percentile(preparation, 0.95);
            var prepMax = preparation.Max();
            prepP95.ShouldBeLessThanOrEqualTo(15000, bucket.Key + " preparation p95");
            prepMax.ShouldBeLessThan(30000, bucket.Key + " preparation max");
            var successIo = successes.Select(row => row.IoMs!.Value).ToList();
            var censoredIo = rows.Where(row => row.Reason == "io-budget-expired" && row.IoMs is not null)
                .Select(row => row.IoMs!.Value).ToList();
            report.AppendLine(string.Join(' ',
                "bucket=" + bucket.Key,
                "n=" + rows.Count,
                "success=" + successes.Count,
                "prepP50=" + Percentile(preparation, 0.50),
                "prepP95=" + prepP95,
                "prepMax=" + prepMax,
                "ioSuccessP50=" + Percentile(successIo, 0.50),
                "ioSuccessP95=" + Percentile(successIo, 0.95),
                "ioSuccessMax=" + successIo.Max(),
                "ioCensoredN=" + censoredIo.Count,
                "ioCensoredP95=" + (censoredIo.Count == 0 ? "none" : Percentile(censoredIo, 0.95).ToString(CultureInfo.InvariantCulture)),
                "refusals=" + (refusals.Count == 0 ? "none" : string.Join(",", refusals.Select(pair => pair.Key + ":" + pair.Value))),
                "queuePeak=" + rows.Max(row => row.QueuePeak)));
        }
        report.AppendLine("gatePeak=" + gate.PeakInFlight + " started=" + gate.Started);
        var destination = WriteReport(report.ToString());
        Console.WriteLine(report.ToString());
        Console.WriteLine("load-report=" + destination);
        gate.Waiting.ShouldBe(0);
        gate.InFlight.ShouldBe(0);
        Directory.EnumerateFiles(workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    private static async Task RunSequentialAsync(ReportBudgetWorkspace workspace, AgentReportStore store, PhaseLog logs,
        GitProcessGate gate, string bucket, int size, List<Sample> samples)
    {
        gate.Waiting.ShouldBe(0);
        gate.InFlight.ShouldBe(0);
        for (var i = 0; i < 20; i++)
            await StoreOneAsync(workspace, store, logs, bucket, Payload(bucket + "-" + i, size), samples, keep: true);
    }

    private static async Task RunLoadedAsync(ReportBudgetWorkspace workspace, AgentReportStore store, PhaseLog logs,
        GitProcessGate gate, string bucket, int size, List<Sample> samples)
    {
        var leases = new List<IDisposable>();
        var running = new List<Task<Sample>>();
        var done = new List<Sample>();
        var queued = 0;
        try
        {
            for (var i = 0; i < 8; i++)
                leases.Add(await gate.EnterAsync(CancellationToken.None));
            for (var i = 0; i < 16; i++)
                running.Add(StoreOneAsync(workspace, store, logs, bucket, Payload(bucket + "-" + i, size), queuePeak: 0));
            (await WaitUntilAsync(() => gate.Waiting >= 16, TimeSpan.FromSeconds(20)))
                .ShouldBeTrue(bucket + " queued " + gate.Waiting);
            queued = gate.Waiting;
            queued.ShouldBeGreaterThanOrEqualTo(16);
            foreach (var lease in leases) lease.Dispose();
            leases.Clear();

            var next = 16;
            while (running.Count > 0 || next < 20)
            {
                while (next < 20 && running.Count < 16)
                {
                    running.Add(StoreOneAsync(workspace, store, logs, bucket, Payload(bucket + "-" + next, size), queuePeak: 0));
                    next++;
                    running.Count.ShouldBeLessThanOrEqualTo(16);
                }
                if (running.Count == 0) break;
                var finished = await Task.WhenAny(running);
                running.Remove(finished);
                done.Add(await finished);
            }
        }
        finally
        {
            foreach (var lease in leases) lease.Dispose();
            if (running.Count > 0)
            {
                try { done.AddRange(await Task.WhenAll(running)); }
                catch (Exception) { /* the original failure is the one to surface */ }
            }
        }

        done.Count.ShouldBe(20, bucket);
        foreach (var sample in done)
            samples.Add(sample with { QueuePeak = queued });
    }

    private static async Task StoreOneAsync(ReportBudgetWorkspace workspace, AgentReportStore store, PhaseLog logs,
        string bucket, string body, List<Sample> samples, bool keep)
    {
        var sample = await StoreOneAsync(workspace, store, logs, bucket, body, queuePeak: 0);
        if (keep) samples.Add(sample);
    }

    private static async Task<Sample> StoreOneAsync(ReportBudgetWorkspace workspace, AgentReportStore store, PhaseLog logs,
        string bucket, string body, int queuePeak)
    {
        var task = workspace.Task(body);
        var wall = Stopwatch.StartNew();
        var result = await store.StoreAsync(task, CancellationToken.None);
        wall.Stop();
        var entry = logs.Take(task.Id);
        var sample = new Sample(bucket, task.Id, result.Path, result.Reason, wall.ElapsedMilliseconds,
            ReadOptional(entry, "PreparationMs"), ReadOptional(entry, "IoMs"), ReadRequired(entry, "TotalMs"),
            entry.State["Outcome"]?.ToString(), queuePeak);
        sample.PreparationMs.ShouldNotBeNull();
        if (result.Path is not null)
        {
            result.Reason.ShouldBeNull();
            (await File.ReadAllBytesAsync(result.Path)).ShouldBe(ExactBytes(body));
            sample.IoMs.ShouldNotBeNull();
        }
        else
        {
            result.Reason.ShouldNotBeNullOrWhiteSpace();
            if (File.Exists(workspace.Expected(task)))
                (await File.ReadAllBytesAsync(workspace.Expected(task))).ShouldBe(ExactBytes(body));
        }
        var rendered = entry.Message + string.Join("\n", entry.State.Select(pair => pair.Value));
        rendered.Contains(body, StringComparison.Ordinal).ShouldBeFalse();
        return sample;
    }

    private static long? ReadOptional(RecordingLogEntry entry, string key) =>
        entry.State.TryGetValue(key, out var value) && value is not null ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;

    private static long ReadRequired(RecordingLogEntry entry, string key)
    {
        entry.State.ContainsKey(key).ShouldBeTrue(key);
        return Convert.ToInt64(entry.State[key], CultureInfo.InvariantCulture);
    }

    private static long Percentile(IReadOnlyList<long> values, double p)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        var rank = (int)Math.Ceiling(p * sorted.Length);
        if (rank < 1) rank = 1;
        if (rank > sorted.Length) rank = sorted.Length;
        return sorted[rank - 1];
    }

    private static string Payload(string label, int size)
    {
        var prefix = label + "\n";
        prefix.Length.ShouldBeLessThan(size);
        return prefix + new string('x', size - prefix.Length);
    }

    private static byte[] ExactBytes(string raw) => new UTF8Encoding(false, true).GetBytes(raw);

    private static async Task<bool> WaitUntilAsync(Func<bool> ready, TimeSpan budget)
    {
        var until = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < until)
        {
            if (ready()) return true;
            await Task.Delay(15);
        }
        return ready();
    }

    private static string ReadAmbient()
    {
        static string Read(string path) => File.Exists(path) ? File.ReadAllText(path).Trim() : "unknown";
        var stat = Read("/proc/stat");
        var cpu = stat == "unknown" ? "unknown" : stat.Split('\n')[0];
        return "cpu=" + cpu + "\nloadavg=" + Read("/proc/loadavg") + "\nmeminfo=" + Read("/proc/meminfo")
            + "\nprocessors=" + Environment.ProcessorCount;
    }

    private static string WriteReport(string text)
    {
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "Antiphon.sln")))
            checkout = checkout.Parent;
        var directory = Path.Combine(checkout?.FullName ?? Directory.GetCurrentDirectory(),
            ".antiphon", "checkpoints", "card-1605");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "load-samples.txt");
        File.WriteAllText(path, text);
        return path;
    }

    private sealed record Sample(string Bucket, Guid TaskId, string? Path, string? Reason, long WallMs,
        long? PreparationMs, long? IoMs, long? TotalMs, string? Outcome, int QueuePeak);

    private sealed class PhaseLog : ILogger<AgentReportStore>
    {
        private readonly ConcurrentDictionary<Guid, RecordingLogEntry> _entries = new();

        public RecordingLogEntry Take(Guid taskId) =>
            _entries.TryGetValue(taskId, out var entry) ? entry : throw new ShouldAssertException("missing diagnostics for " + taskId);

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var pairs = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var item in values)
                    pairs[item.Key] = item.Value;
            }
            if (pairs.TryGetValue("TaskId", out var id) && id is Guid taskId)
                _entries[taskId] = new RecordingLogEntry(logLevel, formatter(state, exception), exception, pairs);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
