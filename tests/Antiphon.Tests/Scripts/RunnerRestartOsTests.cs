using System.Diagnostics;
using System.IO.Abstractions;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0420: the production platform of the desktop runner restart (Antiphon.Nightly.RunnerRestartOs, the C# of
/// New-RunnerRestartPlatform in the deleted scripts/session-runner-restart-health.ps1; unit 30 of the 2026-11-22 plan) on a
/// temporary checkout root: the PID census and its refusals, the controls (the tree stop is QualificationTestHost's, the
/// logon task's start a RecordingLauncher's, so nothing is stopped or started), the probes against loopback stubs, and this
/// process's identity. The rows of the deleted RunnerRestartScriptCompatibilityTests that drove the real platform from a
/// PowerShell entry keep their names where their subject stayed (the call-operator scope case is now the identity guard).
/// The runner port 17204 is never contacted.
/// </summary>
[Category("Integration")]
public sealed class RunnerRestartOsTests
{
    /// <summary>A production platform with members replaced, as the PowerShell cases replaced the platform's closures.</summary>
    private sealed class PartialPlatform : IRunnerRestartPlatform
    {
        private readonly IRunnerRestartPlatform _inner;

        public PartialPlatform(IRunnerRestartPlatform inner)
        {
            _inner = inner;
            OnProbe = inner.Probe;
            OnProbeBuild = inner.ProbeBuild;
            OnIdentity = inner.Identity;
            OnVerify = inner.Verify;
            OnCensus = inner.Census;
            OnControl = inner.Control;
        }

        public Func<double, RunnerProbeResult> OnProbe { get; set; }
        public Func<JsonNode?> OnProbeBuild { get; set; }
        public Func<RunnerProcessIdentity?> OnIdentity { get; set; }
        public Func<RunnerProcessIdentity?, bool> OnVerify { get; set; }
        public Func<RunnerCensus> OnCensus { get; set; }
        public Action<string, RunnerProcessIdentity?> OnControl { get; set; }

        public double? CommandOrigin => _inner.CommandOrigin;
        public IFileSystem Files => _inner.Files;
        public string LogPath => _inner.LogPath;
        public double Now() => _inner.Now();
        public string Utc() => _inner.Utc();
        public void Sleep(double milliseconds) => _inner.Sleep(milliseconds);
        public RunnerProbeResult Probe(double budgetMilliseconds) => OnProbe(budgetMilliseconds);
        public JsonNode? ProbeBuild() => OnProbeBuild();
        public RunnerProcessIdentity? Identity() => OnIdentity();
        public bool Verify(RunnerProcessIdentity? identity) => OnVerify(identity!);
        public RunnerCensus Census() => OnCensus();
        public RunnerProcessIdentity? Process(int pid) => _inner.Process(pid);
        public void Control(string operation, RunnerProcessIdentity? identity) => OnControl(operation, identity);
    }

    private static NightlyHost Host(NightlyFixture fixture, QualificationTestHost? trees = null, INightlyLauncher? launcher = null) => new()
    {
        Clock = new FakeTimeProvider(NightlyFixture.DefaultNow), Git = new FakeGit().Run, Launcher = launcher ?? new RecordingLauncher(),
        Processes = new FakeProcessTable(), Probe = new FakeHostProbe(), Out = fixture.Out, Error = fixture.Error, Trace = TextWriter.Null,
        Qualification = trees ?? new QualificationTestHost(),
    };

    private static RunnerRestartOs Platform(NightlyFixture fixture, NightlyHost host, int port = RunnerRestartOs.RunnerPort, RunnerProcessSeams? seams = null) =>
        new(fixture.Root, host, new FileSystem(), Stopwatch.StartNew(), port, seams);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Hung_http_response_is_canceled_at_wait_deadline(bool hang)
    {
        if (!hang && !OperatingSystem.IsWindows())
            Skip.Test("CARD-1669: a 200 reaches Identity, and RunnerProcesses.ListeningOwner reads the iphlpapi.dll TCP owner table, which exists only on Windows");
        using var fixture = NightlyFixture.Create("runner-restart-os");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clients = new List<TcpClient>();
        var requests = 0;
        var server = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(cts.Token);
                    lock (clients) clients.Add(client);
                    var buffer = new byte[4096];
                    var read = await client.GetStream().ReadAsync(buffer, cts.Token);
                    if (read == 0) continue;
                    Interlocked.Increment(ref requests);
                    accepted.TrySetResult();
                    if (!hang) await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), cts.Token);
                }
            }
            catch (Exception) when (cts.IsCancellationRequested) { }
        });
        try
        {
            var platform = new PartialPlatform(Platform(fixture, Host(fixture), port))
            {
                OnProbeBuild = () => null,
                OnCensus = () => new RunnerCensus(null, null),
                OnVerify = identity => identity?.Pid == Environment.ProcessId,
                OnControl = (_, _) => throw new InvalidOperationException("forbidden fixture mutation"),
            };
            var result = RunnerRestart.Restart(RunnerRestartOptions.Parse(["--wait-only", "--timeout-sec", "2"], fixture.Root), platform, TextWriter.Null);
            accepted.Task.IsCompletedSuccessfully.ShouldBeTrue("hung endpoint accepted actual health request");
            result.Outcome.ShouldBe(hang ? "wait-expired" : "healthy", result.ToJsonText());
            if (hang)
            {
                result.WaitElapsedMs!.Value.ShouldBeGreaterThanOrEqualTo(2000);
                result.WaitElapsedMs!.Value.ShouldBeLessThanOrEqualTo(3000);
            }
            using var after = new TcpClient();
            await after.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            var before = Volatile.Read(ref requests);
            await after.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET /health HTTP/1.1\r\nHost: localhost\r\n\r\n"), cts.Token);
            while (Volatile.Read(ref requests) <= before) await Task.Delay(10, cts.Token);
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await server;
            lock (clients) foreach (var client in clients) client.Dispose();
        }
    }

    [Test]
    public void Missing_logs_is_an_absent_pid_without_creating_the_directory()
    {
        using var fixture = NightlyFixture.Create("runner-restart-os");
        var logs = fixture.PathOf("logs");
        Directory.Exists(logs).ShouldBeFalse("fixture must start without logs");
        var real = Platform(fixture, Host(fixture));
        var census = real.Census();
        census.Supervisor.ShouldBeNull("missing logs must mean absent PIDs");
        census.Wrapper.ShouldBeNull("missing logs must mean absent PIDs");
        Directory.Exists(logs).ShouldBeFalse("PID census created logs");

        var controls = new List<string>();
        var platform = new PartialPlatform(real)
        {
            OnControl = (operation, _) => controls.Add(operation),
            OnProbe = _ => new RunnerProbeResult(200, "fixture-healthy"),
            OnProbeBuild = () => null,
            OnIdentity = () => new RunnerProcessIdentity(Environment.ProcessId, "fixture", "fixture"),
            OnVerify = _ => true,
        };
        var result = RunnerRestart.Restart(RunnerRestartOptions.Parse(["--timeout-sec", "1"], fixture.Root), platform, TextWriter.Null);
        result.Outcome.ShouldBe("healthy", result.ToJsonText());
        controls.ShouldBe(["write-state", "stop-service", "delete-pids", "start-task"]);
        Directory.Exists(logs).ShouldBeFalse();
    }

    [Test]
    [Arguments("directory")]
    [Arguments("locked")]
    [Arguments("invalid")]
    [Arguments("overflow")]
    [Arguments("zero")]
    [Arguments("wrapper-directory")]
    [Arguments("hard-directory")]
    [Arguments("hard-invalid")]
    [Arguments("wait-directory")]
    [Arguments("wait-invalid")]
    [Arguments("missing")]
    [Arguments("live")]
    public void Pid_read_errors_stop_restart_before_any_control(string state)
    {
        using var fixture = NightlyFixture.Create("runner-restart-os");
        var logs = Directory.CreateDirectory(fixture.PathOf("logs")).FullName;
        var pidPath = Path.Combine(logs, "session-runner.supervisor.pid");
        FileStream? held = null;
        switch (state.StartsWith("hard-", StringComparison.Ordinal) || state.StartsWith("wait-", StringComparison.Ordinal) ? state.Split('-')[^1] : state)
        {
            case "directory": Directory.CreateDirectory(pidPath); break;
            case "locked":
                File.WriteAllText(pidPath, Environment.ProcessId.ToString());
                held = new FileStream(pidPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                break;
            case "invalid": File.WriteAllText(pidPath, "not-a-pid"); break;
            case "overflow": File.WriteAllText(pidPath, "999999999999999999999"); break;
            case "zero": File.WriteAllText(pidPath, "0"); break;
            case "wrapper-directory":
                File.WriteAllText(pidPath, Environment.ProcessId.ToString());
                pidPath = Path.Combine(logs, "session-runner.service.pid");
                Directory.CreateDirectory(pidPath);
                break;
            case "live": File.WriteAllText(pidPath, Environment.ProcessId.ToString()); break;
        }
        RunnerRestartResult result;
        var controls = new List<string>();
        try
        {
            var platform = new PartialPlatform(Platform(fixture, Host(fixture)))
            {
                OnControl = (operation, _) => controls.Add(operation),
                OnProbe = _ => new RunnerProbeResult(200, "fixture-healthy"),
                OnProbeBuild = () => null,
                OnIdentity = () => new RunnerProcessIdentity(Environment.ProcessId, "fixture", "fixture"),
                OnVerify = _ => true,
            };
            string[] arguments = state.StartsWith("hard-", StringComparison.Ordinal) ? ["--timeout-sec", "1", "--hard"]
                : state.StartsWith("wait-", StringComparison.Ordinal) ? ["--timeout-sec", "1", "--wait-only"] : ["--timeout-sec", "1"];
            result = RunnerRestart.Restart(RunnerRestartOptions.Parse(arguments, fixture.Root), platform, TextWriter.Null);
        }
        finally
        {
            held?.Dispose();
        }
        var pidExists = File.Exists(pidPath) || Directory.Exists(pidPath);
        if (state is "missing" or "live")
        {
            result.Outcome.ShouldBe("healthy", result.ToJsonText());
            controls.ShouldBe(state == "missing" ? ["write-state", "stop-service", "delete-pids", "start-task"] : ["write-state", "stop-service"]);
            pidExists.ShouldBe(state == "live");
        }
        else
        {
            result.Outcome.ShouldBe("action-failed", result.ToJsonText());
            result.ExitCode.ShouldBe(1);
            result.Error.ShouldNotBeNullOrWhiteSpace();
            controls.ShouldBeEmpty();
            pidExists.ShouldBeTrue();
        }
    }

    // Debug 4ca0643a (2026-11-12) was a PowerShell scope failure of the platform's closures under the call operator; the
    // C# platform has no such scope. What the case also proved stays: the census names the recorded supervisor, stop-service
    // leaves a runner at another executable path alone, a changed identity refuses the stop, and Verify accepts only this
    // checkout's Debug executable at the same start.
    [Test]
    public void Real_platform_stops_only_this_checkouts_runner_and_guards_identity()
    {
        using var fixture = NightlyFixture.Create("runner-restart-os");
        Directory.CreateDirectory(fixture.PathOf("logs"));
        File.WriteAllText(fixture.PathOf("logs", "session-runner.supervisor.pid"), Environment.ProcessId + Environment.NewLine);
        var listed = new List<string>();
        var seams = new RunnerProcessSeams
        {
            IdsByName = name =>
            {
                listed.Add(name);
                return name == "Antiphon.SessionRunner" ? [Environment.ProcessId] : [];
            },
        };
        var trees = new QualificationTestHost();
        var platform = Platform(fixture, Host(fixture, trees), seams: seams);

        platform.Census().Supervisor!.Pid.ShouldBe(Environment.ProcessId);
        platform.Control(RunnerRestartOperations.StopService, null);
        listed.ShouldContain("Antiphon.SessionRunner", "stop-service reached the process listing");
        trees.Stops.ShouldBeEmpty("this test host is not the checkout's Debug runner");
        Should.Throw<InvalidOperationException>(() => platform.Control(RunnerRestartOperations.StopSupervisor,
            new RunnerProcessIdentity(Environment.ProcessId, "2000-01-01T00:00:00.0000000Z", "synthetic"))).Message.ShouldBe("Process identity changed before stop");
        trees.Stops.ShouldBeEmpty();
        var me = platform.Process(Environment.ProcessId)!;
        platform.ExpectedExecutable.ShouldBe(Path.GetFullPath(Path.Combine(fixture.Root, "src", "Antiphon.SessionRunner", "bin", "Debug", "net9.0", "Antiphon.SessionRunner.exe")));
        platform.Verify(me with { Path = platform.ExpectedExecutable }).ShouldBeTrue();
        platform.Verify(me).ShouldBeFalse("another executable path is not this checkout's runner");
        platform.Verify(me with { Path = platform.ExpectedExecutable, StartTimeUtc = "2000-01-01T00:00:00.0000000Z" }).ShouldBeFalse("a reused id is not the same process");
        platform.Verify(null!).ShouldBeFalse();
    }

    [Test]
    public void A_runner_at_this_checkouts_path_is_stopped_by_tree_and_a_failed_stop_of_a_live_one_refuses()
    {
        using var fixture = NightlyFixture.Create("runner-restart-os");
        var expected = Path.GetFullPath(Path.Combine(fixture.Root, "src", "Antiphon.SessionRunner", "bin", "Debug", "net9.0", "Antiphon.SessionRunner.exe"));
        var alive = true;
        var seams = new RunnerProcessSeams
        {
            IdsByName = name => name == "Antiphon.SessionRunner" ? [4100, 4200] : [],
            Identity = pid => pid switch
            {
                4100 => new RunnerProcessIdentity(4100, "2026-11-24T08:00:00.0000000Z", expected.ToUpperInvariant()),
                4200 => alive ? new RunnerProcessIdentity(4200, "2026-11-24T08:00:00.0000000Z", @"C:\elsewhere\Antiphon.SessionRunner.exe") : null,
                _ => null,
            },
        };
        var trees = new QualificationTestHost();
        var platform = Platform(fixture, Host(fixture, trees), seams: seams);
        platform.Control(RunnerRestartOperations.StopService, null);
        trees.Stops.ShouldBe([4100], "the path compare ignores case, and the foreign runner is left alone");

        trees.OnStop = pid => new QualificationTreeStop(false, "taskkill-exit-128", QualificationTreeStop.CommandFor(pid));
        Should.Throw<InvalidOperationException>(() => platform.Control(RunnerRestartOperations.StopService, null)).Message.ShouldBe("taskkill failed (taskkill-exit-128)");
        trees.OnStop = pid =>
        {
            alive = false;
            return new QualificationTreeStop(false, "taskkill-exit-128", QualificationTreeStop.CommandFor(pid));
        };
        platform.Control(RunnerRestartOperations.StopSupervisor, seams.Identity(4200));
        trees.Stops.ShouldBe([4100, 4100, 4200], "a failed stop of a process that is gone is no failure");
    }

    [Test]
    public void Controls_write_the_state_delete_the_pids_and_start_the_logon_task()
    {
        using var fixture = NightlyFixture.Create("runner-restart-os");
        var launcher = new RecordingLauncher(_ => new NightlyLaunchResult { ExitCode = 0 });
        var platform = Platform(fixture, Host(fixture, launcher: launcher));

        platform.Control(RunnerRestartOperations.WriteState, null);
        File.ReadAllText(fixture.PathOf("logs", "session-runner.state")).ShouldBe("running" + Environment.NewLine);
        File.ReadAllBytes(fixture.PathOf("logs", "session-runner.state"))[0].ShouldBe((byte)'r', "no byte order mark");

        File.WriteAllText(fixture.PathOf("logs", "session-runner.service.pid"), "1");
        File.WriteAllText(fixture.PathOf("logs", "session-runner.supervisor.pid"), "2");
        platform.Control(RunnerRestartOperations.DeletePids, null);
        File.Exists(fixture.PathOf("logs", "session-runner.service.pid")).ShouldBeFalse();
        File.Exists(fixture.PathOf("logs", "session-runner.supervisor.pid")).ShouldBeFalse();
        platform.Control(RunnerRestartOperations.DeletePids, null);

        platform.Control(RunnerRestartOperations.StartTask, null);
        var start = launcher.Launches.ShouldHaveSingleItem();
        (start.FileName, start.CaptureOutput, start.Custody).ShouldBe(("schtasks.exe", true, NightlyCustody.Watched));
        start.Arguments.ShouldBe(["/Run", "/TN", "Antiphon Session Runner"]);

        var missing = Platform(fixture, Host(fixture, launcher: new RecordingLauncher(_ => new NightlyLaunchResult
        {
            ExitCode = 1, LogText = "\nERROR: The system cannot find the file specified.\n",
        })));
        Should.Throw<InvalidOperationException>(() => missing.Control(RunnerRestartOperations.StartTask, null))
            .Message.ShouldBe("schtasks /Run /TN \"Antiphon Session Runner\" exited 1: ERROR: The system cannot find the file specified.");
        Should.Throw<InvalidOperationException>(() => platform.Control("restart-everything", null)).Message.ShouldBe("Unknown control operation restart-everything");
    }

    [Test]
    public void Kill_sessions_posts_kill_all_then_stops_every_pty_host_and_says_when_the_endpoint_failed()
    {
        using var fixture = NightlyFixture.Create("runner-restart-os");
        var seams = new RunnerProcessSeams
        {
            IdsByName = name => name == "Antiphon.PtyHost" ? [5100, 5200] : [],
            Identity = pid => pid is 5100 or 5200 ? new RunnerProcessIdentity(pid, "2026-11-24T08:00:00.0000000Z", @"C:\any\Antiphon.PtyHost.exe") : null,
        };
        var trees = new QualificationTestHost();
        using (var stub = LoopbackHttpStub.Start(("/sessions/kill-all", "{}")))
        {
            Platform(fixture, Host(fixture, trees), stub.Port, seams).Control(RunnerRestartOperations.KillSessions, null);
            stub.Stop().ShouldBe(["POST /sessions/kill-all HTTP/1.1"]);
        }
        trees.Stops.ShouldBe([5100, 5200]);
        fixture.Out.ToString().ShouldNotContain("kill-all endpoint unavailable");

        using (var refusing = LoopbackHttpStub.Start(("/sessions/kill-all", 500)))
        {
            Platform(fixture, Host(fixture, trees), refusing.Port, seams).Control(RunnerRestartOperations.KillSessions, null);
        }
        fixture.Out.ToString().ShouldContain("kill-all endpoint unavailable; checking detached pty-host stragglers.");
        trees.Stops.ShouldBe([5100, 5200, 5100, 5200]);
    }

    [Test]
    public void Probes_answer_the_health_status_and_the_build_document()
    {
        using var fixture = NightlyFixture.Create("runner-restart-os");
        using (var stub = LoopbackHttpStub.Start(("/health", 503), ("/capabilities", """{"build":{"commitSha":"abc"}}""")))
        {
            var platform = Platform(fixture, Host(fixture), stub.Port);
            platform.Probe(2000).ShouldBe(new RunnerProbeResult(503, "http-503"));
            platform.ProbeBuild()!["build"]!["commitSha"]!.GetValue<string>().ShouldBe("abc");
        }
        using (var stub = LoopbackHttpStub.Start(("/capabilities", 500)))
            Platform(fixture, Host(fixture), stub.Port).ProbeBuild().ShouldBeNull();
        using (var stub = LoopbackHttpStub.Start(("/capabilities", "not json")))
            Platform(fixture, Host(fixture), stub.Port).ProbeBuild().ShouldBeNull();
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        Platform(fixture, Host(fixture), port).Probe(1000).ShouldBe(new RunnerProbeResult(0, "transport-error-or-timeout"));
    }

    [Test]
    public void This_process_has_an_identity_and_its_loopback_listener_an_owner()
    {
        var me = RunnerProcesses.Identity(Environment.ProcessId).ShouldNotBeNull();
        using (var current = Process.GetCurrentProcess())
        {
            RunnerRestart.TryUtc(me.StartTimeUtc, out var started).ShouldBeTrue();
            started.ShouldBe(current.StartTime.ToUniversalTime());
        }
        me.StartTimeUtc.ShouldEndWith("Z");
        if (OperatingSystem.IsWindows()) me.Path.ShouldBe(Environment.ProcessPath, StringCompareShould.IgnoreCase);
        RunnerProcesses.Identity(0).ShouldBeNull();
        RunnerProcesses.Identity(-1).ShouldBeNull();
        RunnerProcesses.IdsByName(Path.GetFileNameWithoutExtension(Environment.ProcessPath!)).ShouldContain(Environment.ProcessId);
        if (!OperatingSystem.IsWindows())
        {
            RunnerProcesses.ListeningOwner(17204).ShouldBe(0);
            return;
        }
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            RunnerProcesses.ListeningOwner(((IPEndPoint)listener.LocalEndpoint).Port).ShouldBe(Environment.ProcessId);
        }
        finally
        {
            listener.Stop();
        }
    }
}
