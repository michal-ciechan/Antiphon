using System.Diagnostics;
using System.Runtime.InteropServices;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("C1514Choke")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestTimingChokePointTests
{
    [Test]
    [Timeout(300_000)]
    public async Task C1514T_DbHostGit(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        using var session = C1514Support.Open(root);
        var testId = TestContext.Current!.Metadata.TestDetails.TestId;
        using (TestTiming.Install(session))
        {
            session.BeginCase(testId, "TestTimingChokePointTests", "C1514T_DbHostGit", [], session.NowTicks());
            // The assembly hook already started the shared store. Own a cold lifecycle so
            // start and migrate run after this session is installed.
            var lifecycle = new TestDbFixtureLifecycle();
            try
            {
                await using (var schema = await lifecycle.CreateIsolatedSchemaAsync())
                {
                    schema.ShouldNotBeNull();
                }

                await using (var host = new AntiphonWebAppFactory())
                {
                    using var client = host.CreateClient();
                    using var response = await client.GetAsync("/health", cancellationToken);
                    response.IsSuccessStatusCode.ShouldBeTrue();
                    (await response.Content.ReadAsStringAsync(cancellationToken)).ShouldBe("Healthy");
                }

                await using var git = new LandingGitFixture();
                var scratch = Path.Combine(git.Root, "scratch");
                Directory.CreateDirectory(scratch);
                var init = await git.Git.RunAsync(scratch, ["init", "-b", "master"], cancellationToken);
                init.Succeeded.ShouldBeTrue(init.Diagnostic);
                var rev = await git.Git.RunAsync(scratch, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
                rev.Succeeded.ShouldBeTrue(rev.Diagnostic);
                session.EndCase(testId, "Passed", session.NowTicks());
            }
            catch
            {
                session.EndCase(testId, "Failed", session.NowTicks());
                throw;
            }
            finally
            {
                await lifecycle.DisposeAsync();
            }
        }

        session.Dispose();
        var bytes = File.ReadAllText(session.ShardPath!);
        bytes.ShouldNotContain("Password=", Case.Sensitive);
        bytes.ShouldNotContain("rev-parse", Case.Sensitive);
        bytes.ShouldNotContain("db-snapshot", Case.Sensitive);
        bytes.ShouldNotContain("db-restore", Case.Sensitive);
        foreach (var token in new[]
                 {
                     "db-create", "db-migrate", "db-cleanup", "host-startup", "host-shutdown",
                     "\"operation\":\"queue\"", "\"operation\":\"clone\"", "\"operation\":\"drop\"",
                     "\"operation\":\"spawn\"", "\"operation\":\"wait\"",
                 })
            bytes.ShouldContain(token, Case.Sensitive);
    }

    [Test]
    [Timeout(300_000)]
    public async Task C1514T_ReadOnlyTree(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var tree = Path.Combine(root, "tree");
        var objects = Path.Combine(tree, "objects");
        Directory.CreateDirectory(objects);
        var loose = Path.Combine(objects, "loose");
        await File.WriteAllTextAsync(loose, "loose", cancellationToken);
        File.SetAttributes(loose, FileAttributes.ReadOnly);
        var outside = Path.Combine(root, "outside-target");
        Directory.CreateDirectory(outside);
        var marker = Path.Combine(outside, "keep.txt");
        await File.WriteAllTextAsync(marker, "keep", cancellationToken);
        Directory.CreateSymbolicLink(Path.Combine(tree, "link"), outside);

        var timing = Path.Combine(root, "timing");
        using var session = C1514Support.Open(timing);
        var testId = TestContext.Current!.Metadata.TestDetails.TestId;
        using (TestTiming.Install(session))
        {
            session.BeginCase(testId, "TestTimingChokePointTests", "C1514T_ReadOnlyTree", [], session.NowTicks());
            ReadOnlyDirectory.Delete(tree);
            session.EndCase(testId, "Passed", session.NowTicks());
        }

        session.Dispose();
        Directory.Exists(tree).ShouldBeFalse();
        File.Exists(marker).ShouldBeTrue();
        var bytes = File.ReadAllText(session.ShardPath!);
        bytes.ShouldContain("\"phase\":\"filesystem\"", Case.Sensitive);
        bytes.ShouldContain("\"operation\":\"delete-tree\"", Case.Sensitive);
        bytes.ShouldNotContain(outside, Case.Sensitive);
    }

    [Test]
    [Timeout(300_000)]
    public async Task C1514T_Overhead(CancellationToken cancellationToken)
    {
        var evidence = C1514Support.FreshRoot();
        // Sequential 10s walls on this host differ by more than 2% because the
        // idle P-state is ~2.3x slower and a hot core still wanders. Pin one CPU,
        // leave that idle state before the clocks, and interleave OFF/ON repetitions
        // so both walls share one frequency window. The bound itself is unchanged.
        PinCurrentCpu();
        HeatUntilFast();
        var one = TimeInProcessRep();
        var reps = Math.Clamp((int)Math.Ceiling(10000d / Math.Max(one.TotalMilliseconds, 0.01d)), 4, 80);
        var lifecycle = new TestDbFixtureLifecycle();
        var setup = Stopwatch.StartNew();
        try
        {
            await using (var schema = await lifecycle.CreateIsolatedSchemaAsync())
                schema.ShouldNotBeNull();
            await RecordHostStartupAsync(cancellationToken);
            setup.Stop();
            var warm = await PairedSampleAsync(lifecycle, offFirst: true, reps, cancellationToken);
            var ratios = new List<double>();
            var samples = new List<object>();
            foreach (var offFirst in new[] { true, false, true, false, true })
            {
                var pair = await PairedSampleAsync(lifecycle, offFirst, reps, cancellationToken);
                samples.Add(new
                {
                    offFirst,
                    offMs = pair.Off.Wall.TotalMilliseconds,
                    onMs = pair.On.Wall.TotalMilliseconds,
                    dropped = pair.On.Dropped,
                    shardBytes = pair.On.ShardBytes,
                });
                pair.Off.Wall.TotalMilliseconds.ShouldBeGreaterThanOrEqualTo(10000d);
                pair.On.Wall.TotalMilliseconds.ShouldBeGreaterThanOrEqualTo(10000d);
                pair.On.Dropped.ShouldBe(0);
                ratios.Add((pair.On.Wall.TotalMilliseconds - pair.Off.Wall.TotalMilliseconds) / pair.Off.Wall.TotalMilliseconds);
            }

            var emptyOff = TimeEmptyScopes(enabled: false);
            var emptyOn = TimeEmptyScopes(enabled: true);
            var median = Median(ratios);
            var upper = BootstrapUpper(ratios);
            var payload = new
            {
                oneMs = one.TotalMilliseconds,
                reps,
                setupMs = setup.Elapsed.TotalMilliseconds,
                warmMs = warm.On.Wall.TotalMilliseconds,
                warmDropped = warm.On.Dropped,
                ratios,
                median,
                bootstrapUpper = upper,
                emptyOffNsPerScope = emptyOff.NsPerScope,
                emptyOnNsPerScope = emptyOn.NsPerScope,
                emptyOnBytesPerCase = emptyOn.BytesPerCase,
                samples,
            };
            await File.WriteAllTextAsync(Path.Combine(evidence, "overhead-raw.json"),
                System.Text.Json.JsonSerializer.Serialize(payload), cancellationToken);
            warm.On.Dropped.ShouldBe(0);
            median.ShouldBeLessThan(0.02d);
            upper.ShouldBeLessThan(0.02d);
            emptyOn.BytesPerCase.ShouldBeGreaterThan(0d);
        }
        finally
        {
            await lifecycle.DisposeAsync();
        }
    }

    // The host's background probes move pair walls by about a second. Record one
    // start and request here, then keep that process out of the paired interval.
    private static async Task RecordHostStartupAsync(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var session = OpenReal(Path.Combine(root, "timing"), "host");
        var path = session.ShardPath!;
        using (var lease = TestTiming.Install(session))
        {
            var testId = TestContext.Current!.Metadata.TestDetails.TestId;
            TestTiming.BindTestId(testId);
            try
            {
                session.BeginCase(testId, "TestTimingChokePointTests", "C1514T_Overhead", [], session.NowTicks());
                await using var host = new AntiphonWebAppFactory();
                using var client = host.CreateClient();
                using var response = await client.GetAsync("/health", cancellationToken);
                response.IsSuccessStatusCode.ShouldBeTrue();
                session.EndCase(testId, "Passed", session.NowTicks());
            }
            finally
            {
                TestTiming.BindTestId(null);
            }
        }

        session.Dispose();
        var text = File.ReadAllText(path);
        text.ShouldContain("host-startup", Case.Sensitive);
        text.ShouldContain("host-shutdown", Case.Sensitive);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int sched_getcpu();

    [DllImport("libc", SetLastError = true)]
    private static extern int sched_setaffinity(int pid, IntPtr cpusetsize, ref ulong mask);

    private static void PinCurrentCpu()
    {
        if (!OperatingSystem.IsLinux())
            return;
        var cpu = sched_getcpu();
        if ((uint)cpu >= 64)
            return;
        ulong mask = 1UL << cpu;
        _ = sched_setaffinity(0, (IntPtr)sizeof(ulong), ref mask);
    }

    // A cold rep on the idle P-state is about 370ms; a hot rep is about 160ms.
    private static void HeatUntilFast()
    {
        for (var n = 0; n < 16; n++)
        {
            if (TimeInProcessRep().TotalMilliseconds < 220d)
                return;
        }
    }

    private static TimeSpan TimeInProcessRep()
    {
        var watch = Stopwatch.StartNew();
        InProcessRep();
        watch.Stop();
        return watch.Elapsed;
    }

    private static void InProcessRep()
    {
        for (var slice = 0; slice < 8; slice++)
        {
            using (TestTiming.BeginPhase(TestPhaseKind.OurCode, "span"))
            {
                var acc = slice;
                // One rep stays near a few hundred milliseconds so eighty reps still
                // cover ten seconds while eight spans per rep stay under the 4096 cap.
                for (var i = 0; i < 8_000_000; i++)
                    acc = unchecked(acc * 31 + i);
                if (acc == int.MinValue)
                    _ = acc.ToString();
            }
        }
    }

    private static async Task<PairResult> PairedSampleAsync(
        TestDbFixtureLifecycle lifecycle, bool offFirst, int reps, CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var session = OpenReal(Path.Combine(root, "timing"), Guid.NewGuid().ToString("N"));
        var path = session.ShardPath!;
        HeatUntilFast();
        var testId = TestContext.Current!.Metadata.TestDetails.TestId;
        TestTiming.BindTestId(testId);
        session.BeginCase(testId, "TestTimingChokePointTests", "C1514T_Overhead", [], session.NowTicks());
        var off = TimeSpan.Zero;
        var on = TimeSpan.Zero;
        try
        {
            for (var step = 0; step < reps * 2; step++)
            {
                var onStep = offFirst ? step % 2 == 1 : step % 2 == 0;
                if (onStep)
                {
                    using (TestTiming.Install(session))
                    {
                        var watch = Stopwatch.StartNew();
                        InProcessRep();
                        watch.Stop();
                        on += watch.Elapsed;
                    }
                }
                else
                {
                    var watch = Stopwatch.StartNew();
                    InProcessRep();
                    watch.Stop();
                    off += watch.Elapsed;
                }
            }

            if (offFirst)
            {
                off += await TimeIoAsync(lifecycle, session, enabled: false, cancellationToken);
                on += await TimeIoAsync(lifecycle, session, enabled: true, cancellationToken);
            }
            else
            {
                on += await TimeIoAsync(lifecycle, session, enabled: true, cancellationToken);
                off += await TimeIoAsync(lifecycle, session, enabled: false, cancellationToken);
            }
        }
        finally
        {
            session.Dispose();
            TestTiming.BindTestId(null);
        }

        var dropped = 0;
        var bytes = new FileInfo(path).Length;
        foreach (var line in File.ReadLines(path))
        {
            if (!line.Contains("\"droppedPhaseCount\":", StringComparison.Ordinal))
                continue;
            var marker = "\"droppedPhaseCount\":";
            var at = line.IndexOf(marker, StringComparison.Ordinal);
            var tail = line[(at + marker.Length)..];
            var end = 0;
            while (end < tail.Length && char.IsDigit(tail[end]))
                end++;
            if (end > 0)
                dropped = int.Parse(tail[..end], System.Globalization.CultureInfo.InvariantCulture);
        }

        var text = File.ReadAllText(path);
        text.ShouldContain("db-create", Case.Sensitive);
        text.ShouldContain("filesystem", Case.Sensitive);
        text.ShouldContain("process-spawn", Case.Sensitive);
        return new PairResult(new SampleResult(off, 0, 0), new SampleResult(on, dropped, bytes));
    }

    private static async Task<TimeSpan> TimeIoAsync(
        TestDbFixtureLifecycle lifecycle, TestTimingSession session, bool enabled, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        if (!enabled)
        {
            await IoWorkloadAsync(lifecycle, cancellationToken);
            watch.Stop();
            return watch.Elapsed;
        }

        using (TestTiming.Install(session))
        {
            await IoWorkloadAsync(lifecycle, cancellationToken);
            var testId = TestContext.Current!.Metadata.TestDetails.TestId;
            session.EndCase(testId, "Passed", session.NowTicks());
        }

        watch.Stop();
        return watch.Elapsed;
    }

    private static async Task IoWorkloadAsync(TestDbFixtureLifecycle lifecycle, CancellationToken cancellationToken)
    {
        await using (var schema = await lifecycle.CreateIsolatedSchemaAsync())
            schema.ShouldNotBeNull();

        await using var git = new LandingGitFixture();
        var scratch = Path.Combine(git.Root, "scratch");
        Directory.CreateDirectory(scratch);
        var init = await git.Git.RunAsync(scratch, ["init", "-b", "master"], cancellationToken);
        init.Succeeded.ShouldBeTrue(init.Diagnostic);
        var tree = Path.Combine(git.Root, "fs");
        Directory.CreateDirectory(tree);
        var file = Path.Combine(tree, "obj");
        await File.WriteAllTextAsync(file, "x", cancellationToken);
        File.SetAttributes(file, FileAttributes.ReadOnly);
        ReadOnlyDirectory.Delete(tree);
    }

    private static EmptyCost TimeEmptyScopes(bool enabled)
    {
        var root = C1514Support.FreshRoot();
        var watch = Stopwatch.StartNew();
        TestTimingSession? session = null;
        IDisposable? lease = null;
        if (enabled)
        {
            session = OpenReal(Path.Combine(root, "timing"), "empty");
            lease = TestTiming.Install(session);
        }

        try
        {
            for (var state = 0; state < 1000; state++)
            {
                var id = "empty-" + state.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (session is not null)
                {
                    TestTiming.BindTestId(id);
                    session.BeginCase(id, "Empty", "Scopes", [], session.NowTicks());
                }

                for (var scope = 0; scope < 100; scope++)
                {
                    using var phase = TestTiming.BeginPhase(TestPhaseKind.OurCode, "span");
                }

                if (session is not null)
                    session.EndCase(id, "Passed", session.NowTicks());
            }
        }
        finally
        {
            session?.Dispose();
            lease?.Dispose();
            TestTiming.BindTestId(null);
            watch.Stop();
        }
        var bytes = session?.ShardPath is not null && File.Exists(session.ShardPath) ? new FileInfo(session.ShardPath).Length : 0L;
        return new EmptyCost(watch.Elapsed.TotalNanoseconds / 100_000d, enabled ? bytes / 1000d : 0d);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted[sorted.Length / 2];
    }

    private static double BootstrapUpper(IReadOnlyList<double> values)
    {
        const int draws = 2000;
        var rng = new Random(1514);
        var medians = new double[draws];
        for (var draw = 0; draw < draws; draw++)
        {
            var sample = new double[values.Count];
            for (var i = 0; i < values.Count; i++)
                sample[i] = values[rng.Next(values.Count)];
            medians[draw] = Median(sample);
        }

        Array.Sort(medians);
        var index = (int)Math.Ceiling(0.95d * draws) - 1;
        return medians[Math.Clamp(index, 0, draws - 1)];
    }

    private static TestTimingSession OpenReal(string root, string nonce)
    {
        var session = TestTimingSession.TryOpen(new TestTimingConfig
        {
            Root = root,
            RunId = "run-a",
            GroupId = "group-a",
            Attempt = "1",
            Suite = "suite-a",
            Pid = 4242,
            Nonce = nonce,
            AssemblyId = "asm1",
        });
        session.Enabled.ShouldBeTrue(session.FailureCode ?? "recorder disabled");
        return session;
    }

    private readonly record struct SampleResult(TimeSpan Wall, int Dropped, long ShardBytes);

    private readonly record struct PairResult(SampleResult Off, SampleResult On);

    private readonly record struct EmptyCost(double NsPerScope, double BytesPerCase);
}
