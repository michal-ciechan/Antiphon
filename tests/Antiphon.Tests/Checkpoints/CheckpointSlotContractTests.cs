using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointSlotContractTests
{
    private const string Start = "2026-09-30T00:00:00.0000000Z";
    private static readonly SlotSession Enabled = new("enabled", 6);

    [Test]
    public async Task holder_identity_is_preserved_in_acquire_and_retry()
    {
        await using var host = await BuildSlotBrokerFixture.StartAsync(budget: 1);
        var recorder = (BuildSlotBrokerFixture.Recorder)host.Recording();
        var client = new BuildSlotClient(recorder, "http://slots.test/build-slots", holders: new Holders(100, 101));
        var session = await client.ProbeAsync(CancellationToken.None);
        var first = await client.AcquireAsync(session, "first", CancellationToken.None);
        first.State.ShouldBe("granted");
        var queued = client.AcquireAsync(session, "second", CancellationToken.None);
        await Task.Delay(30);
        await first.DisposeAsync();
        await using var second = await queued.WaitAsync(TimeSpan.FromSeconds(5));
        second.State.ShouldBe("granted");
        var posts = recorder.Calls.Where(call => call.Method == "POST" && call.Path == "/build-slots").ToArray();
        posts.Length.ShouldBeGreaterThanOrEqualTo(3);
        using var p0 = JsonDocument.Parse(posts[0].Body);
        using var p1 = JsonDocument.Parse(posts[1].Body);
        using var p2 = JsonDocument.Parse(posts[^1].Body);
        p0.RootElement.GetProperty("processStartUtc").GetString().ShouldBe(Start);
        p1.RootElement.GetProperty("processStartUtc").GetString().ShouldBe(Start);
        p2.RootElement.GetProperty("pid").GetInt32().ShouldBe(101);
        host.Broker.List().Occupied.ShouldBe(1);
    }

    [Test]
    public async Task definitive_acquire_400_refuses_without_delay()
    {
        await using var host = await BuildSlotBrokerFixture.StartAsync();
        using var direct = await host.Http.PostAsJsonAsync("build-slots", new { pid = 100, label = "bad" });
        direct.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await direct.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("type").GetString().ShouldBe("build_slot_invalid");
        problem.RootElement.GetProperty("detail").GetString().ShouldBe("holder pid 100 is not running and no processStartUtc was given");
        var recorder = (BuildSlotBrokerFixture.Recorder)host.Recording(omitStart: true);
        var delays = 0;
        var client = new BuildSlotClient(recorder, "http://slots.test/build-slots",
            delay: (_, _) => { delays++; return Task.CompletedTask; }, pid: 100, processStartUtc: Start);
        var session = await client.ProbeAsync(CancellationToken.None);
        await using var lease = await client.AcquireAsync(session, "bad", CancellationToken.None);
        lease.State.ShouldBe("refused");
        lease.ExitCode.ShouldBe(2);
        lease.MaxCpuCount.ShouldBe(0);
        lease.SlotReason.ShouldBe("build_slot_invalid");
        lease.Diagnostic!.Detail.ShouldContain("holder pid 100");
        recorder.Calls.Count(call => call.Method == "POST").ShouldBe(1);
        delays.ShouldBe(0);
    }

    [Test]
    public async Task probe_400_refuses_without_delay()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, """{"type":"bad_probe","detail":"no"}""");
        var delays = 0;
        var client = Client(handler, delay: (_, _) => { delays++; return Task.CompletedTask; });
        var session = await client.ProbeAsync(CancellationToken.None);
        var lease = await client.AcquireAsync(session, "no", CancellationToken.None);
        session.ExitCode.ShouldBe(2);
        session.MaxCpuCount.ShouldBe(0);
        lease.State.ShouldBe("refused");
        lease.ExitCode.ShouldBe(2);
        handler.Calls.Count.ShouldBe(1);
        delays.ShouldBe(0);
    }

    [Test]
    public async Task malformed_probe_success_is_refused()
    {
        foreach (var body in new[] { "{", "{}", """{"enabled":true,"budget":0,"maxCpuCount":6}""", """{"enabled":"yes","budget":2,"maxCpuCount":6}""" })
        {
            var handler = new ScriptedHttpHandler();
            handler.Enqueue(HttpStatusCode.OK, body);
            var session = await Client(handler).ProbeAsync(CancellationToken.None);
            session.Mode.ShouldBe("refused");
            session.ExitCode.ShouldBe(2);
            session.SlotReason.ShouldBe("invalid_listing");
        }
    }

    [Test]
    public async Task malformed_grant_is_refused()
    {
        foreach (var body in new[] { "{", "{}", """{"maxCpuCount":6}""", """{"leaseId":"L","maxCpuCount":0}""", """{"leaseId":"L","maxCpuCount":"six"}""" })
        {
            var handler = new ScriptedHttpHandler();
            handler.Enqueue(HttpStatusCode.OK, body);
            var lease = await Client(handler).AcquireAsync(Enabled, "invalid", CancellationToken.None);
            lease.ExitCode.ShouldBe(2);
            lease.MaxCpuCount.ShouldBe(0);
            lease.State.ShouldBe("refused");
        }
    }

    [Test]
    public async Task transport_fallback_carries_last_failure_after_bounded_grace()
    {
        var handler = new ScriptedHttpHandler();
        handler.EnqueueThrow();
        var (client, now) = Virtual(handler);
        var session = await client.ProbeAsync(CancellationToken.None);
        session.Mode.ShouldBe("unleased");
        session.SlotReason.ShouldBe("runner_unreachable");
        session.Diagnostic!.Exception.ShouldContain("HttpRequestException");
        now().ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(15));
        var post = new ScriptedHttpHandler();
        post.EnqueueThrow();
        var (postClient, _) = Virtual(post);
        var lease = await postClient.AcquireAsync(Enabled, "transport", CancellationToken.None);
        lease.State.ShouldBe("unleased");
        lease.SlotReason.ShouldBe("runner_unreachable");
    }

    [Test]
    public async Task answered_server_error_never_becomes_successful_unleased()
    {
        var get = new ScriptedHttpHandler();
        get.Enqueue(HttpStatusCode.ServiceUnavailable, """{"type":"overloaded","detail":"later"}""");
        var (getClient, _) = Virtual(get);
        var session = await getClient.ProbeAsync(CancellationToken.None);
        session.ExitCode.ShouldBe(2);
        session.Diagnostic!.Status.ShouldBe(503);
        var post = new ScriptedHttpHandler();
        post.Enqueue(HttpStatusCode.ServiceUnavailable, """{"type":"overloaded","detail":"later"}""");
        var (postClient, _) = Virtual(post);
        var lease = await postClient.AcquireAsync(Enabled, "server", CancellationToken.None);
        lease.ExitCode.ShouldBe(2);
        lease.Diagnostic!.Status.ShouldBe(503);
    }

    [Test]
    public async Task renew_and_release_diagnostics_keep_status_and_body()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"leaseId":"L","maxCpuCount":6,"renewEverySeconds":1}""");
        handler.Enqueue(HttpStatusCode.NotFound, """{"type":"build_slot_unknown","detail":"renew gone"}""");
        handler.Enqueue(HttpStatusCode.NotFound, """{"type":"build_slot_unknown","detail":"release gone"}""");
        var log = new List<string>();
        var client = Client(handler, log: log.Add);
        var lease = await client.AcquireAsync(Enabled, "diag", CancellationToken.None);
        await Task.Delay(1200);
        await lease.DisposeAsync();
        log.ShouldContain(line => line.Contains("operation=renew status=404") && line.Contains("renew gone"));
        log.ShouldContain(line => line.Contains("operation=release status=404") && line.Contains("release gone"));
    }

    [Test]
    public void diagnostics_are_bounded_and_escape_body_controls()
    {
        var original = "line\r\n" + new string('a', 3000) + "\0";
        var diagnostic = SlotDiagnostic.Answer("acquire", 400, "build_slot_invalid", original, 1);
        diagnostic.Detail!.Length.ShouldBeLessThanOrEqualTo(2048);
        diagnostic.Detail.ShouldContain("[truncated]");
        diagnostic.Line("label").ShouldNotContain('\n');
        diagnostic.Line("label").ShouldNotContain('\r');
        diagnostic.Line("label").ShouldNotContain('\0');
    }

    [Test]
    public async Task compatible_modes_preserve_limits_and_reasons()
    {
        var notFound = new ScriptedHttpHandler();
        notFound.Enqueue(HttpStatusCode.NotFound, "");
        var missing = await Client(notFound).ProbeAsync(CancellationToken.None);
        missing.Mode.ShouldBe("unavailable");
        missing.SlotReason.ShouldBe("broker_not_found");
        var disabled = new ScriptedHttpHandler();
        disabled.Enqueue(HttpStatusCode.OK, """{"enabled":false,"budget":2,"maxCpuCount":6}""");
        var client = Client(disabled);
        var session = await client.ProbeAsync(CancellationToken.None);
        var lease = await client.AcquireAsync(session, "off", CancellationToken.None);
        lease.State.ShouldBe("unlimited");
        lease.MaxCpuCount.ShouldBe(6);
        disabled.Calls.Count.ShouldBe(1);
        var granted = new ScriptedHttpHandler();
        granted.Enqueue(HttpStatusCode.OK, """{"unlimited":true,"maxCpuCount":6}""");
        var unlimited = await Client(granted).AcquireAsync(Enabled, "unlimited", CancellationToken.None);
        unlimited.State.ShouldBe("unlimited");
        unlimited.MaxCpuCount.ShouldBe(6);
    }

    private static BuildSlotClient Client(HttpMessageHandler handler,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Action<string>? log = null) =>
        new(handler, "http://slots.test/build-slots", delay: delay, log: log, pid: 100, processStartUtc: Start);

    private static (BuildSlotClient Client, Func<TimeSpan> Elapsed) Virtual(HttpMessageHandler handler)
    {
        var started = DateTimeOffset.UtcNow;
        var now = started;
        Task Delay(TimeSpan span, CancellationToken _) { now += span; return Task.CompletedTask; }
        return (new BuildSlotClient(handler, "http://slots.test/build-slots", grace: TimeSpan.FromSeconds(15),
            clock: () => now, delay: Delay, pid: 100, processStartUtc: Start), () => now - started);
    }

    private sealed class Holders(params int[] pids) : ILeaseHolderSource
    {
        private readonly Queue<int> _pids = new(pids);
        public ILeaseHolder Open() => new Holder(_pids.Dequeue());
        private sealed class Holder(int pid) : ILeaseHolder
        {
            public int Pid => pid;
            public string? ProcessStartUtc => Start;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
