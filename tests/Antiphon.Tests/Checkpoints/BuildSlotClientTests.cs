using System.Diagnostics;
using System.Net;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class BuildSlotClientTests
{
    [Test]
    public async Task probe_404_marks_unavailable_and_makes_no_lease_calls()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.NotFound, "");
        var (client, _) = Client(handler);
        var session = await client.ProbeAsync(CancellationToken.None);
        session.Mode.ShouldBe("unavailable");
        var lease = await client.AcquireAsync(session, "CP-1@tests", CancellationToken.None);
        lease.State.ShouldBe("unavailable");
        handler.Calls.ShouldNotContain(call => call.Method == "POST");
    }

    [Test]
    public async Task refused_becomes_unleased_after_grace()
    {
        var handler = new ScriptedHttpHandler();
        handler.EnqueueThrow();
        var (client, _) = Client(handler, grace: TimeSpan.FromSeconds(60));
        var session = await client.ProbeAsync(CancellationToken.None);
        session.Mode.ShouldBe("unleased");
        session.MaxCpuCount.ShouldBe(4);
        handler.Calls.ShouldAllBe(call => call.Method == "GET");
    }

    [Test]
    public async Task busy_waits_then_grants()
    {
        var handler = ScriptProbe();
        handler.Enqueue(HttpStatusCode.Conflict, """{"type":"build_slot_busy","queuePosition":1,"occupied":1,"budget":1,"retryAfterMs":5}""");
        handler.Enqueue(HttpStatusCode.OK, """{"leaseId":"L1","maxCpuCount":2}""");
        var (client, _) = Client(handler);
        var session = await client.ProbeAsync(CancellationToken.None);
        var lease = await client.AcquireAsync(session, "CP-1", CancellationToken.None);
        lease.State.ShouldBe("granted");
        lease.LeaseId.ShouldBe("L1");
        lease.MaxCpuCount.ShouldBe(2);
        handler.Calls.Count(call => call.Method == "POST").ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task memory_floor_waits()
    {
        var handler = ScriptProbe();
        handler.Enqueue(HttpStatusCode.Conflict, """{"type":"build_slot_memory_floor","availableMb":100,"floorMb":512,"queuePosition":0,"retryAfterMs":5}""");
        handler.Enqueue(HttpStatusCode.OK, """{"leaseId":"L2","maxCpuCount":1}""");
        var (client, _) = Client(handler);
        var session = await client.ProbeAsync(CancellationToken.None);
        var lease = await client.AcquireAsync(session, "build:bin-a", CancellationToken.None);
        lease.State.ShouldBe("granted");
        lease.LeaseId.ShouldBe("L2");
    }

    [Test]
    public async Task timeout_is_exit_4()
    {
        var handler = ScriptProbe();
        handler.Enqueue(HttpStatusCode.Conflict, """{"type":"build_slot_busy","queuePosition":2,"occupied":1,"budget":1,"retryAfterMs":5000}""");
        handler.Enqueue(HttpStatusCode.Conflict, """{"type":"build_slot_busy","queuePosition":2,"occupied":1,"budget":1,"retryAfterMs":5000}""");
        var (client, _) = Client(handler, wait: TimeSpan.FromMilliseconds(1));
        var session = await client.ProbeAsync(CancellationToken.None);
        var lease = await client.AcquireAsync(session, "CP-9", CancellationToken.None);
        lease.ExitCode.ShouldBe(4);
        lease.State.ShouldBe("timeout");
    }

    [Test]
    public async Task release_on_dispose()
    {
        var handler = ScriptProbe();
        handler.Enqueue(HttpStatusCode.OK, """{"leaseId":"L3","maxCpuCount":4}""");
        handler.Enqueue(HttpStatusCode.NoContent, "");
        var (client, _) = Client(handler);
        var session = await client.ProbeAsync(CancellationToken.None);
        var lease = await client.AcquireAsync(session, "CP-1", CancellationToken.None);
        await lease.DisposeAsync();
        handler.Calls.ShouldContain(call => call.Method == "DELETE" && call.Uri.Contains("L3", StringComparison.Ordinal));
    }

    [Test]
    public async Task pid_idempotent_broker_grants_distinct_leases_and_one_release_keeps_the_other()
    {
        var handler = new PidIdempotentSlotHandler();
        var (client, _) = Client(handler, holders: new QueueHolders(101, 202));
        var session = await client.ProbeAsync(CancellationToken.None);
        var firstTask = client.AcquireAsync(session, "build:bin-a", CancellationToken.None);
        var secondTask = client.AcquireAsync(session, "CP-7@command", CancellationToken.None);
        var leases = await Task.WhenAll(firstTask, secondTask);
        leases[0].State.ShouldBe("granted");
        leases[1].State.ShouldBe("granted");
        leases[0].LeaseId.ShouldNotBeNullOrWhiteSpace();
        leases[0].LeaseId.ShouldNotBe(leases[1].LeaseId);
        handler.LiveCount.ShouldBe(2);
        await leases[0].DisposeAsync();
        handler.IsLive(leases[0].LeaseId!).ShouldBeFalse();
        handler.IsLive(leases[1].LeaseId!).ShouldBeTrue();
        await leases[1].DisposeAsync();
        handler.LiveCount.ShouldBe(0);
    }

    [Test]
    public async Task a_shared_pid_lease_is_not_claimed_by_the_second_driver()
    {
        var handler = new PidIdempotentSlotHandler();
        var (client, _) = Client(handler, pid: 50);
        var session = await client.ProbeAsync(CancellationToken.None);
        var leases = await Task.WhenAll(
            client.AcquireAsync(session, "CP-1", CancellationToken.None),
            client.AcquireAsync(session, "CP-2", CancellationToken.None));
        leases.Count(lease => lease.State == "granted").ShouldBe(1);
        leases.Count(lease => lease.State == "unleased").ShouldBe(1);
        handler.LiveCount.ShouldBe(1);
        await leases.Single(lease => lease.State == "granted").DisposeAsync();
        handler.LiveCount.ShouldBe(0);
    }

    [Test]
    public async Task holder_process_stays_alive_until_the_lease_is_released()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "Antiphon.Checkpoints.dll");
        File.Exists(dll).ShouldBeTrue(dll);
        var holder = ProcessLeaseHolder.Start(Environment.ProcessId);
        var pid = holder.Pid;
        pid.ShouldBeGreaterThan(0);
        ProcessAlive(pid).ShouldBeTrue();
        await holder.DisposeAsync();
        ProcessAlive(pid).ShouldBeFalse();
    }

    private static ScriptedHttpHandler ScriptProbe()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"enabled":true}""");
        return handler;
    }

    private static (BuildSlotClient Client, DateTimeOffset Now) Client(
        HttpMessageHandler handler,
        TimeSpan? grace = null,
        TimeSpan? wait = null,
        int? pid = null,
        ILeaseHolderSource? holders = null)
    {
        var now = DateTimeOffset.UtcNow;
        Task Delay(TimeSpan span, CancellationToken _)
        {
            now = now.Add(span);
            return Task.CompletedTask;
        }

        var client = new BuildSlotClient(
            handler,
            "http://slots.test/build-slots",
            grace,
            wait,
            () => now,
            Delay,
            pid: pid,
            holders: holders);
        return (client, now);
    }

    private static bool ProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class QueueHolders(params int[] pids) : ILeaseHolderSource
    {
        private readonly Queue<int> _pids = new(pids);

        public ILeaseHolder Open() => new Holder(_pids.Dequeue());

        private sealed class Holder(int pid) : ILeaseHolder
        {
            public int Pid { get; } = pid;

            public string? ProcessStartUtc => null;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
