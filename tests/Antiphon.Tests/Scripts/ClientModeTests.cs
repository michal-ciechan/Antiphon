using System.IO.Abstractions.TestingHelpers;
using System.Net.Http;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0216 S1. The client-mode command (Antiphon.Nightly.ClientMode, which replaced scripts/client-mode.ps1 and its driver
/// scripts/test-client-mode.ps1 in unit 32 of the 2026-11-22 plan) in process: the mode file, the wait for the port to show
/// the mode, the rebuild request, the status (the shim's state file and a live probe) and the command line, on an in-memory
/// file system, a recording transport and a fake clock. Nothing leaves the process, so no case can flip the real client.
/// </summary>
[Category("Unit")]
public sealed class ClientModeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 30, 0, TimeSpan.Zero);

    private const string BuiltPage = "<!doctype html><script type=\"module\" src=\"/assets/index-abc.js\"></script>";
    private const string DevPage = "<!doctype html><script type=\"module\" src=\"/@vite/client\"></script>";

    private sealed class World
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "client-mode-world");
        public MockFileSystem Files { get; } = new();
        public FakeTimeProvider Clock { get; } = new(Now);
        public StringWriter Out { get; } = new();
        public StringWriter Error { get; } = new();
        public RecordingOperatorHttp Http { get; }

        public World(Func<RecordingOperatorHttp.Call, NightlyReportHttpResponse> answer) => Http = new RecordingOperatorHttp(answer);

        public string Logs(string name) => Path.Combine(Root, "logs", name);

        public NightlyHost Host() => new()
        {
            Clock = Clock, Git = new FakeGit().Run, Launcher = new RecordingLauncher(), Processes = new FakeProcessTable(), Probe = new FakeHostProbe(),
            Out = Out, Error = Error, Trace = TextWriter.Null, Sleep = Clock.Advance,
        };

        public int Run(params string[] args) => ClientMode.Run(ClientModeOptions.Parse(args, Root), Host(), Files, Http.Transport);

        public string[] Lines => Out.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
    }

    private static NightlyReportHttpResponse Refused(RecordingOperatorHttp.Call call) =>
        throw new HttpRequestException("No connection could be made because the target machine actively refused it. (localhost:65500)");

    // The driver's rows: --status prints a state-file verdict and a live-probe verdict and exits 0 whether or not anything
    // listens; an unreachable port reports NOT reachable; nothing is written but logs/.
    [Test]
    public void Status_on_an_unreachable_port_prints_both_verdicts_and_exits_0()
    {
        var world = new World(Refused);

        world.Run("--status", "--port", "65500").ShouldBe(0, world.Out.ToString());

        world.Lines.ShouldBe([
            "No state file at " + world.Logs("client.state.json") + " yet (the shim writes this on its first poll).",
            "Live probe   : NOT reachable (No connection could be made because the target machine actively refused it. (localhost:65500))",
        ]);
        world.Error.ToString().ShouldBeEmpty();
        world.Http.Calls.ShouldHaveSingleItem().Uri.ShouldBe("http://localhost:65500/");
        world.Files.AllFiles.ShouldBeEmpty("status writes nothing");
        world.Files.Directory.Exists(Path.Combine(world.Root, "logs")).ShouldBeTrue();
    }

    [Test]
    public void Status_prints_the_shims_state_as_written_and_the_live_mode()
    {
        var world = new World(_ => new(200, DevPage));
        world.Files.AddFile(world.Logs("client.state.json"), new MockFileData("""
            {"mode":"dev","pid":4242,"since":"2026-11-24T09:00:00.000Z","lastBuildAt":null,"status":"running"}
            """));

        world.Run("--status").ShouldBe(0);

        world.Lines.ShouldBe([
            "State file (" + world.Logs("client.state.json") + "):",
            "  mode        : dev",
            "  status      : running",
            "  pid         : 4242",
            "  since       : 2026-11-24T09:00:00.000Z",
            "  lastBuildAt : ",
            "Live probe   : reachable, HTTP 200, observed mode = dev",
        ]);
        world.Http.Calls.Single().Uri.ShouldBe("http://localhost:17203/");
    }

    [Test]
    public void A_state_file_that_is_not_json_fails_with_one_error_line()
    {
        var world = new World(_ => throw new InvalidOperationException("no probe after a failed state read"));
        world.Files.AddFile(world.Logs("client.state.json"), new MockFileData("{ half"));

        world.Run("--status").ShouldBe(1);

        world.Error.ToString().ShouldStartWith("client-mode: the state file " + world.Logs("client.state.json") + " is not JSON: ");
        world.Http.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Mode_writes_the_mode_file_and_waits_until_the_port_serves_that_mode()
    {
        var answers = new Queue<NightlyReportHttpResponse>([new(200, BuiltPage), new(502, ""), new(200, DevPage.ToUpperInvariant())]);
        var world = new World(_ => answers.Dequeue());

        world.Run("--mode", "DEV", "--timeout-sec", "10").ShouldBe(0, world.Out.ToString());

        world.Files.File.ReadAllBytes(world.Logs("client.mode")).ShouldBe("dev"u8.ToArray(), "the mode alone, UTF-8 without a BOM or a line end");
        world.Lines.ShouldBe([
            "Wrote " + world.Logs("client.mode") + " = dev",
            "Waiting up to 10s for :17203 to reflect 'dev' mode...",
            "OK: :17203 is serving 'dev' mode (HTTP 200).",
        ]);
        world.Http.Calls.Count.ShouldBe(3);
        world.Clock.GetUtcNow().ShouldBe(Now.AddSeconds(2), "one second between probes");
    }

    [Test]
    public void Mode_times_out_when_the_port_keeps_the_other_mode_or_never_answers()
    {
        var wrong = new World(_ => new(200, BuiltPage));
        wrong.Run("--mode", "dev", "--timeout-sec", "3").ShouldBe(1);
        wrong.Lines.Last().ShouldBe("TIMEOUT: :17203 is reachable but still reports 'built' mode after 3s.");
        wrong.Http.Calls.Count.ShouldBe(3);

        var down = new World(Refused);
        down.Run("--mode", "built", "--timeout-sec", "2", "--port", "65500").ShouldBe(1);
        down.Lines.Last().ShouldBe("TIMEOUT: :65500 did not become reachable within 2s (No connection could be made because the target machine actively refused it. (localhost:65500)).");
        down.Files.File.ReadAllText(down.Logs("client.mode")).ShouldBe("built");

        // No time to wait: no probe, and the reason is empty.
        var none = new World(_ => throw new InvalidOperationException("no probe"));
        none.Run("--mode", "dev", "--timeout-sec", "0").ShouldBe(1);
        none.Lines.Last().ShouldBe("TIMEOUT: :17203 did not become reachable within 0s ().");
    }

    [Test]
    public void Mode_with_status_prints_the_status_after_a_timeout_and_exits_0()
    {
        var world = new World(_ => new(200, BuiltPage));

        world.Run("--mode", "dev", "--timeout-sec", "1", "--status").ShouldBe(0);

        world.Lines.ShouldBe([
            "Wrote " + world.Logs("client.mode") + " = dev",
            "Waiting up to 1s for :17203 to reflect 'dev' mode...",
            "TIMEOUT: :17203 is reachable but still reports 'built' mode after 1s.",
            "No state file at " + world.Logs("client.state.json") + " yet (the shim writes this on its first poll).",
            "Live probe   : reachable, HTTP 200, observed mode = built",
        ]);
    }

    [Test]
    public void Rebuild_touches_the_sentinel_with_the_utc_time_and_probes_nothing()
    {
        var world = new World(_ => throw new InvalidOperationException("no probe"));

        world.Run("--rebuild").ShouldBe(0);

        world.Files.File.ReadAllText(world.Logs("client.rebuild-requested")).ShouldBe("2026-11-24T09:30:00.0000000Z\n");
        world.Lines.ShouldBe(["Rebuild requested at 2026-11-24T09:30:00.0000000Z (built mode only - a no-op right now if the shim is in dev mode)."]);
        world.Files.File.Exists(world.Logs("client.mode")).ShouldBeFalse();

        var status = new World(_ => new(503, "busy"));
        status.Run("--rebuild", "--status").ShouldBe(0);
        status.Lines.Last().ShouldBe("Live probe   : NOT reachable (Response status code does not indicate success: 503.)");
    }

    [Test]
    public void A_malformed_command_line_is_a_usage_error()
    {
        // The script's no-argument refusal (exit 1, "Specify -Mode built|dev, -Status, or -Rebuild.") is a usage error now.
        Should.Throw<NightlyUsageException>(() => ClientModeOptions.Parse([], "r")).Message.ShouldBe("specify --mode built|dev, --status, or --rebuild");
        Should.Throw<NightlyUsageException>(() => ClientModeOptions.Parse(["--mode", "hot"], "r")).Message.ShouldBe("--mode expects built or dev, not 'hot'");
        Should.Throw<NightlyUsageException>(() => ClientModeOptions.Parse(["--mode", ""], "r")).Message.ShouldBe("--mode expects built or dev, not ''");
        Should.Throw<NightlyUsageException>(() => ClientModeOptions.Parse(["--status", "--port", "x"], "r")).Message.ShouldBe("--port expects an integer, not 'x'");
        Should.Throw<NightlyUsageException>(() => ClientModeOptions.Parse(["-Status"], "r"));
        var options = ClientModeOptions.Parse(["--mode", "Built", "--timeout-sec", "5", "--port", "18000", "--rebuild"], "root");
        (options.Mode, options.TimeoutSec, options.Port, options.Rebuild, options.Status, options.RepoRoot).ShouldBe(("built", 5, 18000, true, false, "root"));
        var defaults = ClientModeOptions.Parse(["--status"], "root");
        (defaults.Mode, defaults.TimeoutSec, defaults.Port).ShouldBe(("", 60, 17203));
    }

    [Test]
    public void The_client_command_sources_are_ascii()
    {
        foreach (var file in new[] { "Antiphon.Nightly/ClientMode.cs", "Antiphon.Nightly/ClientChecks.cs", "Antiphon.Nightly/ClientTools.cs", "Antiphon.Scripts/ClientCommands.cs" })
            File.ReadAllBytes(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "lib", file)).ShouldAllBe(b => b < 128, file);
    }
}
