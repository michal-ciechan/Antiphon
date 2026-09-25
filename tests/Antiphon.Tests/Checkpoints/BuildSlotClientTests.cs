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

    private static ScriptedHttpHandler ScriptProbe()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"enabled":true}""");
        return handler;
    }

    private static (BuildSlotClient Client, DateTimeOffset Now) Client(ScriptedHttpHandler handler, TimeSpan? grace = null, TimeSpan? wait = null)
    {
        var now = DateTimeOffset.UtcNow;
        Task Delay(TimeSpan span, CancellationToken _)
        {
            now = now.Add(span);
            return Task.CompletedTask;
        }

        var client = new BuildSlotClient(handler, "http://slots.test/build-slots", grace, wait, () => now, Delay);
        return (client, now);
    }
}
