using System.Diagnostics;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class UnixPtyArgvTests
{
    private static string ProbeDirectory => Path.Combine(AppContext.BaseDirectory, "probes");
    private static string ProbeName => "argv-echo.js";
    private static string Node => ResolveNode();

    [Test]
    [Arguments("plain")]
    [Arguments("empty")]
    [Arguments("space_tab")]
    [Arguments("cr")]
    [Arguments("lf")]
    [Arguments("crlf")]
    [Arguments("quotes_backslashes")]
    [Arguments("unicode")]
    [Arguments("literal_shell")]
    public async Task Native_argv_is_verbatim(string shape)
    {
        RequireUnix();
        var payload = shape switch
        {
            "plain" => new[] { "plain=value" },
            "empty" => new[] { "" },
            "space_tab" => new[] { " a\tb " },
            "cr" => new[] { "a\rb", new string(Enumerable.Range(1, 31).Select(i => (char)i).ToArray()) + "\u007f" },
            "lf" => new[] { "a\nb" },
            "crlf" => new[] { "a\r\nb" },
            "quotes_backslashes" => new[] { "\"edge\"", "a\\\"b", "trail\\" },
            "unicode" => new[] { "é中😀" },
            _ => new[] { "$HOME", "$(printf sentinel)", ";", "*" },
        };
        var expected = new[] { "before" }.Concat(payload).Concat(new[] { "after", "" }).ToArray();
        var argv = new[] { ProbeName }.Concat(expected).ToArray();
        var original = argv.ToArray();
        await using var fixture = new NativeFixture();
        await fixture.Runner.StartAsync(Node, argv, ProbeDirectory, fixture.Environment);
        try
        {
            var actual = await fixture.CaptureAsync();
            actual.ShouldBe(expected, "native-argv-exact");
            argv.ShouldBe(original, "caller-vector-unchanged");
        }
        finally { await fixture.StopAsync(); }
    }

    [Test]
    [Arguments("exe")]
    [Arguments("first")]
    [Arguments("middle")]
    [Arguments("last")]
    public async Task Nul_is_refused_before_native_spawn(string shape)
    {
        RequireUnix();
        var sentinel = "private-sentinel";
        var app = shape == "exe" ? Node + "\0" + sentinel : Node;
        string[] argv = shape switch
        {
            "first" => ["\0" + sentinel, ProbeName],
            "middle" => [ProbeName, "--rules", "safe", "--rules", sentinel + "\0tail", "end"],
            "last" => [ProbeName, "--", "--flag" + sentinel + "\0"],
            _ => [ProbeName],
        };
        var index = shape switch { "exe" => 0, "first" => 1, "middle" => 5, _ => 3 };
        AssertNul(Should.Throw<UnixPtyArgvException>(() => UnixPtyArgvGuard.VerifyOrThrow(app, argv)), index, sentinel, app);
        await using var fixture = new NativeFixture();
        var error = await Should.ThrowAsync<UnixPtyArgvException>(() => fixture.Runner.StartAsync(app, argv, ProbeDirectory, fixture.Environment));
        AssertNul(error, index, sentinel, app);
        fixture.Runner.Pid.ShouldBeNull("nul-no-native-spawn");
        File.Exists(fixture.CapturePath).ShouldBeFalse("nul-no-capture");
    }

    [Test]
    public async Task Tracked_argv_is_verbatim_after_containment()
    {
        RequireUnix();
        await using var fixture = new NativeFixture();
        var original = new[] { ProbeName, "line\n\"quoted\"", "" };
        var journal = new Journal();
        var placement = new Placement(launch => new PtyTrackedLaunch(launch.App,
            launch.CommandLine.Take(1).Concat(new[] { "placed-sentinel" }).Concat(launch.CommandLine.Skip(1)).ToArray()));
        await using var runner = new PtyAgentRunner { CustodyContainment = placement };
        await runner.StartTrackedAsync(Node, original, ProbeDirectory, fixture.Environment, 80, 24, 0, journal);
        try
        {
            placement.Original!.App.ShouldBe(Node);
            placement.Original.CommandLine.ShouldBe(original);
            (await fixture.CaptureAsync()).ShouldBe(new[] { "placed-sentinel", original[1], original[2] }, "tracked-native-argv-exact");
            journal.Events.ToArray().ShouldBe(new[] { "start", "track" }, "tracked-journal-order");
        }
        finally { await NativeFixture.StopAsync(runner); }
    }

    [Test]
    [Arguments("exe")]
    [Arguments("arg")]
    public async Task Containment_introduced_nul_is_refused_before_start_intent(string shape)
    {
        RequireUnix();
        await using var fixture = new NativeFixture();
        var journal = new Journal(throwOnStart: true);
        var placement = new Placement(launch => shape == "exe"
            ? new PtyTrackedLaunch(Node + "\0secret", launch.CommandLine)
            : new PtyTrackedLaunch(launch.App, new[] { ProbeName, "ok", "\0secret" }));
        await using var runner = new PtyAgentRunner { CustodyContainment = placement };
        var thrown = await CaptureAsync(() => runner.StartTrackedAsync(Node, [ProbeName, "ok"], ProbeDirectory,
            fixture.Environment, 80, 24, 0, journal));
        journal.Events.ShouldBeEmpty("final-nul-before-start-intent");
        placement.Calls.ShouldBe(1);
        AssertNul(thrown.ShouldBeOfType<UnixPtyArgvException>(), shape == "exe" ? 0 : 3, "secret", Node);
        runner.Pid.ShouldBeNull();
        File.Exists(fixture.CapturePath).ShouldBeFalse();
    }

    [Test]
    public async Task Original_nul_is_refused_before_containment_and_consumes_attempt()
    {
        RequireUnix();
        await using var fixture = new NativeFixture();
        var placement = new Placement(_ => throw new PlacementTripwire());
        var journal = new Journal();
        await using var runner = new PtyAgentRunner { CustodyContainment = placement };
        var thrown = await CaptureAsync(() => runner.StartTrackedAsync(Node, [ProbeName, "bad\0secret"], ProbeDirectory,
            fixture.Environment, 80, 24, 0, journal));
        placement.Calls.ShouldBe(0, "original-nul-before-placement");
        journal.Events.ShouldBeEmpty("original-nul-before-journal");
        AssertNul(thrown.ShouldBeOfType<UnixPtyArgvException>(), 2, "secret", Node);
        await Should.ThrowAsync<InvalidOperationException>(() => runner.StartTrackedAsync(Node, [ProbeName, "valid"], ProbeDirectory,
            fixture.Environment, 80, 24, 0, journal));
        placement.Calls.ShouldBe(0, "tracked-attempt-consumed");
    }

    private static void AssertNul(UnixPtyArgvException error, int index, string sentinel, string app)
    {
        error.Code.ShouldBe("pty_argv_nul", "named-nul-code");
        error.Reason.ShouldBe("nul");
        error.ArgumentIndex.ShouldBe(index, "nul-index");
        error.Message.ShouldContain($"argv[{index}]");
        error.Message.ShouldNotContain(sentinel, customMessage: "nul-sanitized");
        error.Message.ShouldNotContain(app.Replace("\0", ""), customMessage: "nul-no-executable");
    }

    private static void RequireUnix()
    {
        if (OperatingSystem.IsWindows()) throw new SkipTestException("Unix PTY argv contract requires a Unix PTY and node");
    }

    private static string ResolveNode()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(dir, OperatingSystem.IsWindows() ? "node.exe" : "node");
            if (File.Exists(path)) return path;
        }
        throw new InvalidOperationException("Node executable prerequisite is missing");
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception error) { return error; }
    }

    private sealed class Placement(Func<PtyTrackedLaunch, PtyTrackedLaunch> place) : IPtyCustodyContainment
    {
        public Guid ContainerId { get; } = Guid.NewGuid();
        public int Calls { get; private set; }
        public PtyTrackedLaunch? Original { get; private set; }
        public PtyTrackedLaunch Place(PtyTrackedLaunch launch) { Calls++; Original = launch; return place(launch); }
        public PtyCustodyPopulation ReadActive() => new(0, []);
        public PtyTerminationObservation Terminate() => new(true, 0);
    }

    private sealed class PlacementTripwire : Exception;

    private sealed class Journal(bool throwOnStart = false) : IPtyCustodyJournal
    {
        public Guid ContainerId { get; } = Guid.NewGuid();
        public List<string> Events { get; } = [];
        public void RecordStartIntent() { Events.Add("start"); if (throwOnStart) throw new PlacementTripwire(); }
        public void RecordTracking(int processId) => Events.Add("track");
        public void RecordSeal() => Events.Add("seal");
    }

    private sealed class NativeFixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c863-" + Guid.NewGuid().ToString("N"));
        public string CapturePath => Path.Combine(Root, "argv.json");
        public Dictionary<string, string> Environment => new() { ["ANTIPHON_ARGV_CAPTURE"] = CapturePath };
        public PtyAgentRunner Runner { get; } = new("inbox");

        public NativeFixture() => Directory.CreateDirectory(Root);

        public async Task<string[]> CaptureAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(CapturePath))
            {
                deadline.Token.ThrowIfCancellationRequested();
                await Task.Delay(20, deadline.Token);
            }
            return JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(CapturePath, deadline.Token))
                ?? throw new InvalidDataException("Native argv capture was empty");
        }

        public Task StopAsync() => StopAsync(Runner);

        public static async Task StopAsync(PtyAgentRunner runner)
        {
            if (runner.Pid is not { } pid) return;
            try
            {
                using var child = Process.GetProcessById(pid);
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await child.WaitForExitAsync(deadline.Token);
            }
            catch (ArgumentException) { /* already exited */ }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            await Runner.DisposeAsync();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
