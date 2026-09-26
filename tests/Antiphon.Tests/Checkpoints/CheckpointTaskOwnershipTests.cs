using Antiphon.Checkpoints;
using System.Net;
using System.Text;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointTaskOwnershipTests
{
    [Test]
    public async Task settlement_cancels_two_drivers_and_skips_later_rows()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var driver = new FakeDriver();
        driver.When(_ => true, async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref count) == 2)
                entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { }
            await release.Task;
            throw new OperationCanceledException(cancellationToken);
        });
        var manifest = new CheckpointManifest();
        for (var i = 1; i <= 3; i++)
            manifest.Checkpoints.Add(new CheckpointSpec { Id = $"CP-{i}", After = ["S1"], Command = "true", EstimatedMinutes = 1 });
        using var cancel = new CancellationTokenSource();
        var state = new RunState();
        var run = new RunScheduler(driver, new FakePlatform()).RunAsync(new SchedulerRequest
        {
            Manifest = manifest,
            Rows = manifest.Checkpoints,
            RunDirectory = CheckpointFixtures.TempDir(),
            WorkingDirectory = CheckpointFixtures.RepoRoot,
            State = state,
            Slots = new FixedSlotClient("off"),
            Width = 2,
            TotalTimeout = TimeSpan.FromMinutes(5),
            OwnerBound = true,
        }, cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            run.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            release.TrySetResult();
        }
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        driver.Count(_ => true).ShouldBe(2);
        result.Rows.Single(row => row.Id == "CP-3").State.ShouldBe("owner-ended");
        result.ExitCode.ShouldBe(ExitCodes.OwnerEnded);
    }

    private static readonly Guid TaskId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private const string Token = "C759-SYNTHETIC-SECRET-SENTINEL";

    private static Func<string, string?> OwnerEnvironment(string? taskId = null) => name => name switch
    {
        "ANTIPHON_TASK_ID" => taskId ?? TaskId.ToString(),
        "ANTIPHON_SESSION_ID" => SessionId.ToString(),
        "ANTIPHON_API" => "http://owner.invalid",
        "ANTIPHON_TASK_TOKEN" => Token,
        _ => null,
    };

    private static Task HoldDelay(TimeSpan _, CancellationToken token) => Task.Delay(Timeout.InfiniteTimeSpan, token);

    private static CheckpointManifest CommandManifest() => new()
    {
        Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["S1"], Command = "true", EstimatedMinutes = 1 }],
    };

    private static string NewBoundRun(CheckpointManifest? manifest = null)
    {
        var repo = CheckpointFixtures.TempDir();
        return CheckpointApp.CreateRun(manifest ?? CommandManifest(), new RunRequest
        {
            Slots = "off", KeepOutputs = true, OwnerTaskId = TaskId.ToString(), OwnerSessionId = SessionId.ToString(),
        }, repo);
    }

    private static CheckpointApp.Runtime Runtime(OwnerHandler handler, IDriver? driver = null, IBuildSlotClient? slots = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null) => new()
    {
        EnvironmentLookup = OwnerEnvironment(), OwnerHandler = handler, Delay = delay ?? HoldDelay,
        Driver = driver ?? new FakeDriver(), Slots = slots ?? new FixedSlotClient("off"),
    };

    [Test]
    public async Task terminal_owner_refuses_start_without_creating_a_run()
    {
        foreach (var status in new[] { "Succeeded", "Failed", "Canceled" })
        {
            var root = CheckpointFixtures.TempDir();
            var handler = new OwnerHandler { TaskStatus = status };
            var launches = 0;
            var runtime = new CheckpointApp.Runtime
            {
                EnvironmentLookup = OwnerEnvironment(), OwnerHandler = handler, Delay = HoldDelay,
                Launch = _ => { launches++; return 123; },
            };
            var started = await CheckpointApp.StartAsync(CommandManifest(), new RunRequest(), root, TextWriter.Null, runtime);
            started.ExitCode.ShouldBe(ExitCodes.OwnerEnded);
            launches.ShouldBe(0);
            Directory.Exists(Path.Combine(root, ".antiphon", "checkpoints")).ShouldBeFalse();
        }
    }

    [Test]
    public async Task executor_rechecks_owner_after_start_admission()
    {
        var handler = new OwnerHandler { TaskStatus = "Succeeded" };
        var driver = new FakeDriver();
        var run = NewBoundRun();
        (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, Runtime(handler, driver))).ShouldBe(ExitCodes.OwnerEnded);
        driver.Count(_ => true).ShouldBe(0);
        var state = new RunStateStore().TryRead(Path.Combine(run, "state.json"))!;
        state.Phase.ShouldBe("done");
        state.Reason.ShouldBe("owner-ended");
        File.ReadAllText(Path.Combine(run, "report.md")).ShouldContain("reason=owner-ended");
    }

    [Test]
    public async Task each_build_rechecks_owner_immediately_before_launch()
    {
        var manifest = new CheckpointManifest();
        manifest.Builds.Add(new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" });
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-1", "bin-a"));
        var handler = new OwnerHandler();
        var slots = new BoundarySlots { OnAcquire = _ => handler.TaskStatus = "Succeeded" };
        var driver = new FakeDriver();
        var run = NewBoundRun(manifest);
        (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, Runtime(handler, driver, slots))).ShouldBe(ExitCodes.OwnerEnded);
        slots.Acquires.ShouldBeGreaterThan(0);
        driver.Count(_ => true).ShouldBe(0);
        slots.Releases.ShouldBe(slots.Acquires);
    }

    [Test]
    public async Task each_row_and_rerun_rechecks_owner_before_launch()
    {
        var handler = new OwnerHandler();
        var slots = new BoundarySlots { OnAcquire = _ => handler.TaskStatus = "Succeeded" };
        var driver = new FakeDriver();
        var run = NewBoundRun();
        (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, Runtime(handler, driver, slots))).ShouldBe(ExitCodes.OwnerEnded);
        driver.Count(_ => true).ShouldBe(0);
        new RunStateStore().TryRead(Path.Combine(run, "state.json"))!.Reason.ShouldBe("owner-ended");
    }

    [Test]
    public async Task ownership_loss_cancels_slot_wait_and_rejects_a_late_grant()
    {
        var handler = new OwnerHandler();
        var poll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slots = new BoundarySlots
        {
            Acquire = async token =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
                return new SlotLease { State = "granted" };
            },
        };
        var run = NewBoundRun();
        var execute = CheckpointApp.ExecuteAsync(run, CancellationToken.None,
            Runtime(handler, new FakeDriver(), slots, (_, token) => poll.Task.WaitAsync(token)));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            handler.TaskStatus = "Succeeded";
            poll.TrySetResult();
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            (await execute).ShouldBe(ExitCodes.OwnerEnded);
        }
        finally { poll.TrySetResult(); }
    }

    [Test]
    public async Task process_driver_cancellation_kills_each_local_tree()
    {
        var factory = new BlockingHandleFactory();
        var driver = new ProcessDriver(factory);
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        var a = driver.RunAsync(new DriverRequest("fake", [], CheckpointFixtures.TempDir()), first.Token);
        var b = driver.RunAsync(new DriverRequest("fake", [], CheckpointFixtures.TempDir()), second.Token);
        factory.Handles.Count.ShouldBe(2);
        first.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => a);
        factory.Handles[0].Kills.ShouldBe(1);
        factory.Handles[0].KilledTree.ShouldBeTrue();
        factory.Handles[1].Kills.ShouldBe(0);
        second.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => b);
        factory.Handles[1].Kills.ShouldBe(1);
        factory.Handles[1].KilledTree.ShouldBeTrue();
    }

    [Test]
    public async Task terminal_publication_waits_for_driver_exit_and_lease_disposal()
    {
        var handler = new OwnerHandler();
        var poll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driverExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FakeDriver();
        driver.When(_ => true, async (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { observed.TrySetResult(); }
            await driverExit.Task;
            throw new OperationCanceledException(token);
        });
        var slots = new BoundarySlots { Release = _ => release.Task };
        var run = NewBoundRun();
        var execute = CheckpointApp.ExecuteAsync(run, CancellationToken.None, Runtime(handler, driver, slots, (_, token) => poll.Task.WaitAsync(token)));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            handler.TaskStatus = "Succeeded";
            poll.TrySetResult();
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            execute.IsCompleted.ShouldBeFalse();
            new RunStateStore().TryRead(Path.Combine(run, "state.json"))!.Phase.ShouldNotBe("done");
            driverExit.TrySetResult();
            execute.IsCompleted.ShouldBeFalse();
            release.TrySetResult();
            (await execute).ShouldBe(ExitCodes.OwnerEnded);
            slots.Releases.ShouldBe(1);
        }
        finally { poll.TrySetResult(); driverExit.TrySetResult(); release.TrySetResult(); }
    }

    [Test]
    public async Task stopped_bound_session_ends_an_unsettled_task()
    {
        foreach (var status in new[] { "Stopped", "Failed", "Stopping" })
        {
            var handler = new OwnerHandler { SessionStatus = status };
            using var owner = new TaskOwnerGuard(OwnerEnvironment(), handler, (_, _) => Task.CompletedTask);
            (await owner.EnsureLiveAsync(CancellationToken.None)).ShouldBeFalse();
            owner.Reason.ShouldBe("owner-ended");
            handler.Calls.ShouldBe(1);
        }
        foreach (var status in new[] { "Dispatched", "Working", "Blocked" })
        {
            var handler = new OwnerHandler { TaskStatus = status };
            using var owner = new TaskOwnerGuard(OwnerEnvironment(), handler);
            (await owner.EnsureLiveAsync(CancellationToken.None)).ShouldBeTrue();
        }
    }

    [Test]
    public async Task mismatched_task_or_session_is_unverified()
    {
        foreach (var (wrongTask, wrongSession) in new[] { (true, false), (false, true) })
        {
            var handler = new OwnerHandler { WrongTask = wrongTask, WrongSession = wrongSession };
            using var owner = new TaskOwnerGuard(OwnerEnvironment(), handler, (_, _) => Task.CompletedTask);
            (await owner.EnsureLiveAsync(CancellationToken.None)).ShouldBeFalse();
            owner.Reason.ShouldBe("owner-unverified");
            handler.Calls.ShouldBe(3);
        }
    }

    [Test]
    public async Task unbound_runs_and_normal_starter_exit_remain_allowed()
    {
        var unbound = NewBoundRun();
        var requestPath = Path.Combine(unbound, "request.json");
        var request = System.Text.Json.JsonSerializer.Deserialize<RunRequest>(File.ReadAllText(requestPath), CheckpointApp.Json)!;
        request.OwnerTaskId = null;
        request.OwnerSessionId = null;
        File.WriteAllText(requestPath, System.Text.Json.JsonSerializer.Serialize(request, CheckpointApp.Json));
        var handler = new OwnerHandler();
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null, OwnerHandler = handler, Driver = new FakeDriver(), Slots = new FixedSlotClient("off"),
        };
        (await CheckpointApp.ExecuteAsync(unbound, CancellationToken.None, runtime)).ShouldBe(0);
        handler.Calls.ShouldBe(0);

        var bound = NewBoundRun();
        (await CheckpointApp.ExecuteAsync(bound, CancellationToken.None, Runtime(handler))).ShouldBe(0);
        handler.Calls.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task transient_owner_errors_recover_and_persistent_errors_stop()
    {
        var handler = new OwnerHandler();
        handler.Next.Enqueue("HTTP500");
        handler.Next.Enqueue("Working");
        using var owner = new TaskOwnerGuard(OwnerEnvironment(), handler, (_, _) => Task.CompletedTask);
        (await owner.EnsureLiveAsync(CancellationToken.None)).ShouldBeTrue();
        handler.Next.Enqueue("HTTP500");
        handler.Next.Enqueue("HTTP500");
        handler.Next.Enqueue("HTTP500");
        (await owner.EnsureLiveAsync(CancellationToken.None)).ShouldBeFalse();
        owner.Reason.ShouldBe("owner-unverified");
        handler.Calls.ShouldBe(5);

        using var incomplete = new TaskOwnerGuard(name => name == "ANTIPHON_TASK_ID" ? TaskId.ToString() : null,
            new OwnerHandler(), (_, _) => Task.CompletedTask);
        (await incomplete.EnsureLiveAsync(CancellationToken.None)).ShouldBeFalse();
        incomplete.Reason.ShouldBe("owner-unverified");

        var held = new OwnerHandler();
        for (var i = 0; i < 3; i++) held.Next.Enqueue("HOLD");
        var deadlines = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource<CancellationTokenSource>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var index = 0;
        using var timed = new TaskOwnerGuard(OwnerEnvironment(), held, (_, _) => Task.CompletedTask,
            deadline: (span, token) =>
            {
                span.ShouldBe(TimeSpan.FromSeconds(2));
                var source = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadlines[index++].TrySetResult(source);
                return source;
            });
        var observation = timed.EnsureLiveAsync(CancellationToken.None);
        foreach (var gate in deadlines)
            (await gate.Task.WaitAsync(TimeSpan.FromSeconds(5))).Cancel();
        (await observation).ShouldBeFalse();
        held.HeldCanceled.Count.ShouldBe(3);
        held.HeldCanceled.All(receipt => receipt.Task.IsCompleted).ShouldBeTrue();
    }

    [Test]
    public async Task owner_token_is_used_only_in_the_http_header()
    {
        var handler = new OwnerHandler();
        var root = CheckpointFixtures.TempDir();
        LaunchRequest? launch = null;
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = OwnerEnvironment(), OwnerHandler = handler,
            Launch = request => { launch = request; return 123; },
        };
        var started = await CheckpointApp.StartAsync(CommandManifest(), new RunRequest(), root, TextWriter.Null, runtime);
        started.ExitCode.ShouldBe(0);
        handler.SeenToken.ShouldBe(Token);
        launch.ShouldNotBeNull();
        foreach (var name in new[] { "request.json", "manifest.resolved.yaml", "state.json" })
            File.ReadAllText(Path.Combine(started.RunDirectory, name)).ShouldNotContain(Token);
        string.Join(' ', launch.Arguments).ShouldNotContain(Token);
        var request = System.Text.Json.JsonSerializer.Deserialize<RunRequest>(File.ReadAllText(Path.Combine(started.RunDirectory, "request.json")), CheckpointApp.Json)!;
        request.OwnerTaskId.ShouldBe(TaskId.ToString());
    }

    [Test]
    public async Task ownership_loss_prevents_new_baseline_processes()
    {
        var driver = new FakeDriver();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var comparer = new BaselineComparer(driver, new FixedSlotClient("off"), beforeLaunch: token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        await Should.ThrowAsync<OperationCanceledException>(() => comparer.CompareAsync(
            CheckpointFixtures.TempDir(), CheckpointFixtures.TempDir(), "origin/master",
            [new ReportRow { Id = "CP-1", Filter = "/*/*/ExampleTests/*", Failures = [new ReportFailure { Name = "ExampleTests.method" }] }],
            [new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" }], canceled.Token));
        driver.Count(_ => true).ShouldBe(0);
    }

    [Test]
    public void ownership_loss_keeps_outputs_and_stops_new_cleanup()
    {
        var repo = CheckpointFixtures.TempDir();
        var output = Path.Combine(repo, "bin-owned");
        Directory.CreateDirectory(output);
        var marker = Path.Combine(output, "marker.txt");
        File.WriteAllText(marker, "owned");
        var checks = 0;
        Should.Throw<OperationCanceledException>(() => OutputCleanup.CleanOwnedOutputs(repo, ["bin-owned"], 0, false, false,
            beforeDelete: () => { checks++; throw new OperationCanceledException("owner-ended"); }));
        checks.ShouldBe(1);
        File.Exists(marker).ShouldBeTrue();
    }

    [Test]
    public async Task windows_estimate_selects_row_and_total_deadlines()
    {
        var markdown = """
            ### Checkpoints
            | CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows |
            |---|---|---|---|---|---|---|---:|---:|---:|
            | CP-1 | S1 | n/a | estimate | `true` | V-1 | exit 0 | n/a | 9 | 27 |
            """;
        var linux = PlanTableImporter.ImportMarkdown(markdown, isWindows: false).Manifest!.Checkpoints.Single();
        var windows = PlanTableImporter.ImportMarkdown(markdown, isWindows: true).Manifest!.Checkpoints.Single();
        linux.EstimatedMinutes.ShouldBe(9);
        linux.TimeoutMinutes.ShouldBe(27);
        windows.EstimatedMinutes.ShouldBe(27);
        windows.TimeoutMinutes.ShouldBe(81);
        RowTimeout.DeriveTotalMinutes([linux.EstimatedMinutes!.Value], null).ShouldBe(30);
        RowTimeout.DeriveTotalMinutes([windows.EstimatedMinutes!.Value], null).ShouldBe(64);
        var manifest = new CheckpointManifest { Checkpoints = [windows] };
        var repo = CheckpointFixtures.TempDir();
        var run = CheckpointApp.CreateRun(manifest, new RunRequest { Slots = "off", KeepOutputs = true }, repo);
        var runtime = new CheckpointApp.Runtime { EnvironmentLookup = _ => null, Driver = new FakeDriver(),
            Slots = new FixedSlotClient("off"), Platform = new FakePlatform { IsWindows = true } };
        (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, runtime)).ShouldBe(0);
        var state = new RunStateStore().TryRead(Path.Combine(run, "state.json"))!;
        (state.TotalTimeoutAt!.Value - state.StartedAt).TotalMinutes.ShouldBe(64);
    }

    [Test]
    public void invalid_estimates_are_refused_before_start()
    {
        const string head = "### Checkpoints\n| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows |\n|---|---|---|---|---|---|---|---:|---:|---:|\n";
        foreach (var cell in new[] { "0", "-1", "abc", "2147483648", "715827883" })
        {
            foreach (var row in new[]
            {
                $"| CP-1 | S1 | n/a | bad | `true` | V-1 | exit 0 | n/a | {cell} | 27 |",
                $"| CP-1 | S1 | n/a | bad | `true` | V-1 | exit 0 | n/a | 9 | {cell} |",
            })
            {
                var imported = PlanTableImporter.ImportMarkdown(head + row);
                imported.ExitCode.ShouldBe(2, cell);
                imported.Error.ShouldContain("EstimatedMinutes");
            }
        }
        PlanTableImporter.ImportMarkdown(head + "| CP-1 | S1 | n/a | good | `true` | V-1 | exit 0 | n/a | 9 | 27 |").ExitCode.ShouldBe(0);
    }

    private sealed class OwnerHandler : HttpMessageHandler
    {
        public string TaskStatus { get; set; } = "Working";
        public string SessionStatus { get; set; } = "Running";
        public bool WrongTask { get; set; }
        public bool WrongSession { get; set; }
        public Queue<string> Next { get; } = new();
        public List<TaskCompletionSource> HeldCanceled { get; } = [];
        public int Calls { get; private set; }
        public string? SeenToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Method.ShouldBe(HttpMethod.Get);
            request.RequestUri!.AbsoluteUri.ShouldBe("http://owner.invalid/api/agent-tasks/" + TaskId);
            Calls++;
            SeenToken = request.Headers.GetValues("X-Antiphon-Task-Token").Single();
            SeenToken.ShouldBe(Token);
            var status = Next.Count > 0 ? Next.Dequeue() : TaskStatus;
            if (status == "HTTP500")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            if (status == "HOLD")
            {
                var receipt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                HeldCanceled.Add(receipt);
                return WaitForCancellation(cancellationToken, receipt);
            }
            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                summary = new { id = WrongTask ? Guid.Empty : TaskId, agentSessionId = SessionId, status },
                session = new { sessionId = WrongSession ? Guid.Empty : SessionId, status = SessionStatus },
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }

        private static async Task<HttpResponseMessage> WaitForCancellation(CancellationToken token, TaskCompletionSource receipt)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { receipt.TrySetResult(); throw; }
            throw new InvalidOperationException("held request unexpectedly completed");
        }
    }

    private sealed class BoundarySlots : IBuildSlotClient
    {
        public Action<string>? OnAcquire { get; init; }
        public Func<CancellationToken, Task<SlotLease>>? Acquire { get; init; }
        public Func<CancellationToken, Task>? Release { get; init; }
        public int Acquires { get; private set; }
        public int Releases { get; private set; }
        public Task<SlotSession> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(new SlotSession("enabled", 4));
        public async Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken)
        {
            Acquires++;
            OnAcquire?.Invoke(label);
            if (Acquire is not null)
                return await Acquire(cancellationToken);
            return new SlotLease
            {
                State = "granted", MaxCpuCount = 4,
                ReleaseAsync = async token => { if (Release is not null) await Release(token); Releases++; },
            };
        }
    }

    private sealed class BlockingHandleFactory : IProcessHandleFactory
    {
        public List<BlockingHandle> Handles { get; } = [];
        public IProcessHandle Create(System.Diagnostics.ProcessStartInfo startInfo)
        {
            var handle = new BlockingHandle();
            Handles.Add(handle);
            return handle;
        }
    }

    private sealed class BlockingHandle : IProcessHandle
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Kills { get; private set; }
        public bool KilledTree { get; private set; }
        public bool Start() => true;
        public void BeginRead(Action<string?> stdout, Action<string?> stderr) { }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task.WaitAsync(cancellationToken);
        public bool HasExited => _exit.Task.IsCompleted;
        public int ExitCode => 137;
        public void Kill(bool entireProcessTree)
        {
            Kills++;
            KilledTree = entireProcessTree;
            _exit.TrySetResult();
        }
        public void Dispose() { }
    }
}
