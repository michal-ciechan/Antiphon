using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the restart tools behind the external Aspire control
/// endpoint (restart-dev-stack, restart-server, restart-client, stop-server, assert-dev-ports-clear; DevRestart) in process on
/// AppHostWorld. restart.ps1, restart-server.ps1, restart-client.ps1, stop-server.ps1 and assert-dev-ports-clear.ps1 had no
/// driver; these rows pin each step's order and abort, the stamped server build (the SourceRevisionStampContractTests census
/// read restart-server.ps1; the argv is C# now), the port owner table and the steps restart-dev-stack runs in process.
/// Mikey.AspireCommand, the endpoint, the ports and the processes are the world's fakes.
/// </summary>
[Category("Unit")]
public sealed class DevRestartTests
{
    private static string Text(AppHostWorld world) => world.Out.ToString() + world.Error.ToString();

    private static string[] Aspire(string resource, string verb, string endpoint = DevRestart.DefaultEndpoint) =>
        ["run", "--project", DevRestart.AspireCommand, "--", resource, verb, "--endpoint", endpoint];

    private static int Server(AppHostWorld world, params string[] arguments) =>
        DevRestart.RestartServer(DevServerRestartOptions.Parse(arguments, world.Root), world.Host(), world, world);

    [Test]
    public void Restart_server_warns_stops_checks_the_port_builds_stamped_and_starts()
    {
        using var world = new AppHostWorld();
        var exit = Server(world, "--endpoint", "https://localhost:9");
        exit.ShouldBe(0, Text(world));
        world.Launches.Select(launch => launch.Arguments.ToArray()).ShouldBe([
            Aspire("antiphon-server", "stop", "https://localhost:9"),
            ["build", world.PathOf("server", "Antiphon.Server.csproj"), "-p:SourceRevisionId=" + AppHostWorld.Head],
            Aspire("antiphon-server", "start", "https://localhost:9"),
        ]);
        world.Launches.ShouldAllBe(launch => launch.FileName == "dotnet");
        var text = Text(world);
        // CARD-1698: the floor warnings (this fixture root names no floor, so each tool warns) come before the stop.
        text.IndexOf("WARNING:", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0);
        text.IndexOf("WARNING:", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("Stopping server...", StringComparison.Ordinal));
        world.IndexOf("sleep 3").ShouldBeLessThan(world.IndexOf("port 17281"));
        text.ShouldContain("Dev ports are clear: 17281.");
        text.ShouldContain("Server restarted.");
    }

    [Test]
    public void Restart_server_refuses_an_unreadable_head_before_the_stop()
    {
        using var world = new AppHostWorld { HeadExit = 128 };
        Server(world).ShouldBe(1);
        Text(world).ShouldContain("restart-server: git rev-parse HEAD failed; refusing an unstamped server build.");
        world.Launches.ShouldBeEmpty();
    }

    [Test]
    [Arguments("stop", "Server stop failed! Aborting.", 1)]
    [Arguments("build", "Build failed! Aborting.", 2)]
    [Arguments("start", "Server start failed! Aborting.", 3)]
    public void Restart_server_aborts_at_the_failing_step(string step, string message, int launches)
    {
        using var world = new AppHostWorld { Tools = (launch, line) => launch.Arguments.Contains(step) ? 1 : 0 };
        Server(world).ShouldBe(1);
        Text(world).ShouldContain(message);
        world.Launches.Count.ShouldBe(launches);
    }

    [Test]
    public void A_held_dev_port_aborts_the_restart_unless_its_owners_may_be_stopped()
    {
        using var held = new AppHostWorld();
        held.Owners[17281] = [4321];
        held.Described[4321] = new DevStackProcess("dotnet", @"C:\Program Files\dotnet\dotnet.exe");
        Server(held).ShouldBe(1);
        var text = Text(held);
        text.ShouldContain("Dev port owner check found listeners:");
        text.ShouldContain(" Port ProcessId ProcessName Path");
        text.ShouldContain("17281      4321 dotnet      C:\\Program Files\\dotnet\\dotnet.exe");
        text.ShouldContain("Stop these processes manually, or rerun with --stop-port-owners to stop the listed owners.");
        text.ShouldContain("Backend dev port check failed! Aborting.");
        held.Trace.ShouldNotContain("stop-id 4321");
        held.Launches.Count.ShouldBe(1, "only the stop ran");

        // With --stop-port-owners the owner is stopped (its listener goes with it) and the restart goes on.
        using var stopped = new AppHostWorld { StopFreesPorts = true };
        stopped.Owners[17281] = [4321];
        stopped.Described[4321] = new DevStackProcess("dotnet", "");
        Server(stopped, "--stop-port-owners").ShouldBe(0, Text(stopped));
        stopped.Trace.Count(row => row == "stop-id 4321").ShouldBe(1);
        Text(stopped).ShouldContain("Stopping PID 4321...");
        Text(stopped).ShouldContain("Dev ports are clear: 17281.");
        stopped.Launches.Count.ShouldBe(3);
    }

    [Test]
    public void Assert_dev_ports_clear_never_stops_an_exited_owner_and_fails_when_a_listener_remains()
    {
        using var world = new AppHostWorld();
        world.Owners[17202] = [11, 12];
        world.Described[11] = new DevStackProcess("Antiphon.Server", "");
        var exit = DevRestart.AssertPortsClear(DevPortsOptions.Parse(["--ports", "17202,17203", "--stop-port-owners"]), world.Host(), world, world);
        exit.ShouldBe(1);
        world.Trace.ShouldContain("stop-id 11");
        world.Trace.ShouldNotContain("stop-id 12");
        var text = Text(world);
        text.ShouldContain("17202        12 <exited>");
        text.ShouldContain("Some dev ports are still occupied after stopping owners:");
        world.Trace.ShouldContain("sleep 1");
    }

    [Test]
    public void Assert_dev_ports_clear_reads_its_ports_and_refuses_an_empty_or_bad_list()
    {
        DevPortsOptions.Parse([]).Ports.ShouldBe([17202, 17203, 17209]);
        DevPortsOptions.Parse(["--ports", "17282"]).Ports.ShouldBe([17282]);
        DevPortsOptions.Parse(["--ports", " 1 , 2 "]).Ports.ShouldBe([1, 2]);
        Should.Throw<NightlyUsageException>(() => DevPortsOptions.Parse(["--ports", ""])).Message.ShouldBe("at least one port must be provided");
        Should.Throw<NightlyUsageException>(() => DevPortsOptions.Parse(["--ports", "17282,web"])).Message.ShouldBe("--ports expects port numbers, not 'web'");
        Should.Throw<NightlyUsageException>(() => DevPortsOptions.Parse(["--ports", "70000"]));
        using var world = new AppHostWorld();
        DevRestart.AssertPortsClear(DevPortsOptions.Parse([]), world.Host(), world, world).ShouldBe(0);
        Text(world).ShouldContain("Dev ports are clear: 17202, 17203, 17209.");
    }

    [Test]
    public void Restart_client_stops_waits_checks_17282_and_starts()
    {
        using var world = new AppHostWorld();
        DevRestart.RestartClient(DevServerRestartOptions.Parse(["--stop-port-owners"], world.Root), world.Host(), world, world).ShouldBe(0, Text(world));
        world.Launches.Select(launch => launch.Arguments.ToArray()).ShouldBe([Aspire("antiphon-client", "stop"), Aspire("antiphon-client", "start")]);
        world.IndexOf("sleep 2").ShouldBeLessThan(world.IndexOf("port 17282"));
        Text(world).ShouldContain("Client restarted.");

        using var failed = new AppHostWorld { Tools = (launch, line) => launch.Arguments.Contains("start") ? 1 : 0 };
        DevRestart.RestartClient(DevServerRestartOptions.Parse([], failed.Root), failed.Host(), failed, failed).ShouldBe(1);
        Text(failed).ShouldContain("Client start failed! Aborting.");
    }

    [Test]
    public void Stop_server_ignores_the_stop_exit_and_waits_for_the_file_locks()
    {
        using var world = new AppHostWorld { Tools = (launch, line) => { line("endpoint refused"); return 1; } };
        DevRestart.StopServer(DevServerRestartOptions.Parse([], world.Root, stopOnly: true), world.Host(), world).ShouldBe(0);
        world.Launches.ShouldHaveSingleItem().Arguments.ShouldBe(Aspire("antiphon-server", "stop"));
        Text(world).ShouldContain("endpoint refused");
        Text(world).ShouldContain("Server stopped.");
        world.Trace.ShouldContain("sleep 3");
        Should.Throw<NightlyUsageException>(() => DevServerRestartOptions.Parse(["--stop-port-owners"], "r", stopOnly: true));
    }

    [Test]
    public void Restart_dev_stack_runs_the_runner_server_client_and_verify_in_order_and_stops_at_a_failure()
    {
        using var world = new AppHostWorld();
        var steps = new DevRestartSteps
        {
            RestartSessionRunner = host => { world.Record("step runner"); return 0; },
            VerifyDevStack = host => { world.Record("step verify"); return 0; },
        };
        DevRestart.Restart(DevRestartOptions.Parse(["--restart-runner"], world.Root), world.Host(), world, world, steps).ShouldBe(0, Text(world));
        var order = new[] { world.IndexOf("step runner"), world.IndexOf("tool dotnet run --project " + DevRestart.AspireCommand + " -- antiphon-server stop"),
            world.IndexOf("tool dotnet run --project " + DevRestart.AspireCommand + " -- antiphon-client start"), world.IndexOf("step verify") };
        order.ShouldAllBe(index => index >= 0, string.Join("\n", world.Trace));
        order.ShouldBe(order.OrderBy(index => index).ToArray());
        Text(world).ShouldContain("=== Done. Open http://localhost:17282. ===");

        using var quiet = new AppHostWorld();
        DevRestart.Restart(DevRestartOptions.Parse(["--skip-verify"], quiet.Root), quiet.Host(), quiet, quiet, new DevRestartSteps
        {
            RestartSessionRunner = host => throw new InvalidOperationException("not asked"),
            VerifyDevStack = host => throw new InvalidOperationException("not asked"),
        }).ShouldBe(0);
        Text(quiet).ShouldContain("Leaving session runner running. Use --restart-runner to restart it too.");

        using var runner = new AppHostWorld();
        DevRestart.Restart(DevRestartOptions.Parse(["--restart-runner"], runner.Root), runner.Host(), runner, runner, new DevRestartSteps
        {
            RestartSessionRunner = host => 2,
            VerifyDevStack = host => 0,
        }).ShouldBe(1);
        runner.Launches.ShouldBeEmpty("a failed runner restart stops before the server");

        using var verify = new AppHostWorld();
        DevRestart.Restart(DevRestartOptions.Parse(["--endpoint", "https://localhost:9", "--stop-port-owners"], verify.Root), verify.Host(), verify, verify,
            new DevRestartSteps { RestartSessionRunner = host => 0, VerifyDevStack = host => 1 }).ShouldBe(1);
        verify.Launches.Where(launch => launch.Arguments.Contains("--endpoint")).Select(launch => launch.Arguments.Last()).ShouldBe(Enumerable.Repeat("https://localhost:9", 4));
        Text(verify).ShouldNotContain("=== Done.");
        Should.Throw<NightlyUsageException>(() => DevRestartOptions.Parse(["--endpoint", ""], "r")).Message.ShouldBe("--endpoint expects a URL");
    }
}
