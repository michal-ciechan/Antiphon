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

        var duplicate = new DuplicateGrantHandler();
        var holders = new Holders(100, 101, 102);
        var retrying = new BuildSlotClient(duplicate, "http://slots.test/build-slots", holders: holders);
        var firstLease = await retrying.AcquireAsync(Enabled, "first", CancellationToken.None);
        var secondLease = await retrying.AcquireAsync(Enabled, "second", CancellationToken.None);
        secondLease.State.ShouldBe("granted", "replacement-pair: the second driver must receive a new grant");
        duplicate.Pids.ShouldBe([100, 101, 102]);
        holders.Disposed.ShouldContain(101, "replacement-pair: duplicate holder must be disposed");
        await firstLease.DisposeAsync();
        await secondLease.DisposeAsync();
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
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden,
                     HttpStatusCode.NotFound, HttpStatusCode.Conflict })
        {
            var scripted = new ScriptedHttpHandler();
            scripted.Enqueue(status, """{"type":"other_refusal","detail":"denied"}""");
            var attempts = 0;
            var denied = await Client(scripted, delay: (_, _) => { attempts++; return Task.CompletedTask; })
                .AcquireAsync(Enabled, "denied", CancellationToken.None);
            denied.ExitCode.ShouldBe(2, $"acquire-refused-exit status={(int)status}");
            denied.SlotReason.ShouldBe("other_refusal");
            scripted.Calls.Count.ShouldBe(1, "acquire-delay-count: one POST on definitive refusal");
            attempts.ShouldBe(0);
        }
    }

    [Test]
    public async Task busy_then_refused_acquire_carries_exact_wait()
    {
        var handler = new ScriptedHttpHandler();
        handler.Enqueue(HttpStatusCode.Conflict, """{"type":"build_slot_busy","retryAfterMs":7000}""");
        handler.Enqueue(HttpStatusCode.BadRequest, """{"type":"build_slot_invalid","detail":"denied"}""");
        var (client, elapsed) = Virtual(handler);
        var lease = await client.AcquireAsync(Enabled, "busy-denied", CancellationToken.None);
        lease.ExitCode.ShouldBe(2);
        lease.WaitedSeconds.ShouldBe(7, "busy-refusal-client-wait");
        elapsed().ShouldBe(TimeSpan.FromSeconds(7));
        handler.Calls.Count.ShouldBe(2);
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
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden,
                     HttpStatusCode.UnprocessableEntity })
        {
            var scripted = new ScriptedHttpHandler();
            scripted.Enqueue(status, """{"type":"probe_denied"}""");
            var denied = await Client(scripted).ProbeAsync(CancellationToken.None);
            denied.ExitCode.ShouldBe(2, $"probe-refused-exit status={(int)status}");
            scripted.Calls.Count.ShouldBe(1);
        }
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
        foreach (var body in new[] { "{", "[]", "{}", """{"maxCpuCount":6}""", """{"leaseId":"L","maxCpuCount":0}""", """{"leaseId":"L","maxCpuCount":"six"}""" })
        {
            var handler = new ScriptedHttpHandler();
            handler.Enqueue(HttpStatusCode.OK, body);
            var lease = await Client(handler).AcquireAsync(Enabled, "invalid", CancellationToken.None);
            lease.ExitCode.ShouldBe(2);
            lease.MaxCpuCount.ShouldBe(0);
            lease.State.ShouldBe("refused");
        }
        var invalid = new ScriptedHttpHandler();
        invalid.Enqueue(HttpStatusCode.OK, """{"leaseId":"L-cleanup","maxCpuCount":0}""");
        invalid.Enqueue(HttpStatusCode.NoContent, "");
        var refused = await Client(invalid).AcquireAsync(Enabled, "cleanup", CancellationToken.None);
        refused.ExitCode.ShouldBe(2);
        invalid.Calls.Count(call => call.Method == "DELETE" && call.Uri.EndsWith("/L-cleanup", StringComparison.Ordinal))
            .ShouldBe(1, "invalid-grant-released: an identifiable invalid grant must be released");
        await using var host = await BuildSlotBrokerFixture.StartAsync();
        var recorder = (BuildSlotBrokerFixture.Recorder)host.Recording(invalidateGrantAfter: 1);
        var client = new BuildSlotClient(recorder, "http://slots.test/build-slots", holders: new Holders(100, 101));
        var first = await client.AcquireAsync(Enabled, "sibling", CancellationToken.None);
        first.State.ShouldBe("granted");
        var second = await client.AcquireAsync(Enabled, "invalid-second", CancellationToken.None);
        second.ExitCode.ShouldBe(2);
        host.Broker.List().Occupied.ShouldBe(1, "invalid-grant-released: only the invalid second grant is removed");
        await first.DisposeAsync();
        host.Broker.List().Occupied.ShouldBe(0);
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
        var afterBusy = new ScriptedHttpHandler();
        afterBusy.Enqueue(HttpStatusCode.Conflict, """{"type":"build_slot_busy","retryAfterMs":60000}""");
        afterBusy.EnqueueThrow();
        var (busyClient, busyElapsed) = Virtual(afterBusy);
        var busyFallback = await busyClient.AcquireAsync(Enabled, "busy-transport", CancellationToken.None);
        busyFallback.State.ShouldBe("unleased", "busy-then-transport: only a full unanswered grace permits fallback");
        busyElapsed().ShouldBe(TimeSpan.FromSeconds(75));
        afterBusy.Calls.Count.ShouldBeGreaterThan(2, "busy-then-transport: retry after the first error");

        var reset = new ScriptedHttpHandler();
        reset.Enqueue(HttpStatusCode.Conflict, """{"type":"build_slot_busy","retryAfterMs":10000}""");
        reset.EnqueueThrow();
        reset.Enqueue(HttpStatusCode.Conflict, """{"type":"build_slot_busy","retryAfterMs":10000}""");
        reset.EnqueueThrow();
        var (resetClient, resetElapsed) = Virtual(reset);
        var resetFallback = await resetClient.AcquireAsync(Enabled, "busy-reset", CancellationToken.None);
        resetFallback.State.ShouldBe("unleased");
        resetElapsed().ShouldBe(TimeSpan.FromSeconds(40), "busy-answer-resets-grace: the second busy answer starts a fresh transport window");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            postClient.AcquireAsync(Enabled, "cancel", canceled.Token));
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
        (await getClient.AcquireAsync(session, "answered", CancellationToken.None)).WaitedSeconds
            .ShouldBe(15, "answered-error-refused: probe wait must reach the refusal receipt");
        var post = new ScriptedHttpHandler();
        post.Enqueue(HttpStatusCode.ServiceUnavailable, """{"type":"overloaded","detail":"later"}""");
        var (postClient, _) = Virtual(post);
        var lease = await postClient.AcquireAsync(Enabled, "server", CancellationToken.None);
        lease.ExitCode.ShouldBe(2);
        lease.Diagnostic!.Status.ShouldBe(503);
        var afterBusy = new ScriptedHttpHandler();
        afterBusy.Enqueue(HttpStatusCode.Conflict, """{"type":"build_slot_busy","retryAfterMs":60000}""");
        afterBusy.Enqueue(HttpStatusCode.ServiceUnavailable, """{"type":"overloaded","detail":"retry later"}""");
        var (busyClient, busyElapsed) = Virtual(afterBusy);
        var busyRefusal = await busyClient.AcquireAsync(Enabled, "busy-server", CancellationToken.None);
        busyRefusal.ExitCode.ShouldBe(2, "busy-then-503: the answered error must refuse after its own grace");
        busyRefusal.Diagnostic!.Status.ShouldBe(503);
        busyRefusal.Diagnostic.Detail.ShouldContain("retry later");
        busyElapsed().ShouldBe(TimeSpan.FromSeconds(75));
        afterBusy.Calls.Count.ShouldBeGreaterThan(2, "busy-then-503: retry within the unanswered window");
    }

    [Test]
    public async Task renew_and_release_diagnostics_keep_status_and_body()
    {
        var handler = new RenewDiagnosticHandler();
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var client = Client(handler, log: log.Enqueue);
        var lease = await client.AcquireAsync(Enabled, "diag", CancellationToken.None);
        await handler.RenewObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var diagnosticDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!log.Any(line => line.Contains("operation=renew status=404")) && DateTime.UtcNow < diagnosticDeadline)
            await Task.Delay(10);
        await lease.DisposeAsync();
        log.ShouldContain(line => line.Contains("operation=renew status=404") && line.Contains("renew gone"));
        log.ShouldContain(line => line.Contains("operation=release status=404") && line.Contains("release gone"));
        var gated = new GatedRenewHandler();
        var gatedLease = await Client(gated).AcquireAsync(Enabled, "drain", CancellationToken.None);
        await gated.RenewEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var disposal = gatedLease.DisposeAsync().AsTask();
        await disposal.WaitAsync(TimeSpan.FromSeconds(3));
        gated.Events.ShouldBe(["renew-entered", "renew-drained", "delete"],
            "renewal-drained-before-delete: cancellation must finish before DELETE");
    }

    [Test]
    public async Task diagnostics_are_bounded_and_escape_body_controls()
    {
        var original = "line\r\n" + new string('a', 3000) + "\0";
        var diagnostic = SlotDiagnostic.Answer("acquire", 400, "build_slot_invalid", original, 1);
        diagnostic.Detail!.Length.ShouldBeLessThanOrEqualTo(2048);
        diagnostic.Detail.ShouldContain("[truncated]");
        diagnostic.Line("label").ShouldNotContain('\n');
        diagnostic.Line("label").ShouldNotContain('\r');
        diagnostic.Line("label").ShouldNotContain('\0');
        const string reflected = "C833-REFLECTED-TOKEN";
        var secret = SlotDiagnostic.Answer("acquire", 400, "build_slot_invalid",
            "reflected " + reflected + " at https://user:password@example.test/path?q=secret", 1, reflected);
        secret.Line("safe").Contains(reflected, StringComparison.Ordinal).ShouldBeFalse("token-absent");
        secret.Line("safe").ShouldNotContain("password");
        secret.Line("safe").ShouldNotContain("q=secret");
        var reflectedHandler = new ScriptedHttpHandler();
        reflectedHandler.Enqueue(HttpStatusCode.BadRequest,
            "{\"type\":\"build_slot_invalid\",\"detail\":\"reflected " + reflected + "\"}");
        var captured = new List<string>();
        var reflectedClient = new BuildSlotClient(reflectedHandler, "http://slots.test/build-slots",
            pid: 100, processStartUtc: Start, sensitiveToken: reflected, log: captured.Add);
        var refused = await reflectedClient.AcquireAsync(Enabled, "label", CancellationToken.None);
        refused.Diagnostic!.Detail!.Contains(reflected, StringComparison.Ordinal)
            .ShouldBeFalse("token-absent: acquisition diagnostic");
        captured.ShouldAllBe(line => !line.Contains(reflected, StringComparison.Ordinal));
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var client = new BuildSlotClient(new SplitRefusalHandler(), "http://slots.test/build-slots",
            holders: new Holders(201, 202), log: log.Enqueue);
        var outcomes = await Task.WhenAll(client.AcquireAsync(Enabled, "alpha", CancellationToken.None),
            client.AcquireAsync(Enabled, "beta", CancellationToken.None));
        outcomes.Select(item => item.SlotReason).OrderBy(item => item).ShouldBe(["alpha_refusal", "beta_refusal"]);
        log.ShouldContain(line => line.Contains("label=\"alpha\"") && line.Contains("reason=alpha_refusal"));
        log.ShouldContain(line => line.Contains("label=\"beta\"") && line.Contains("reason=beta_refusal"));
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
        public List<int> Disposed { get; } = [];
        public ILeaseHolder Open() => new Holder(_pids.Dequeue(), Disposed);
        private sealed class Holder(int pid, List<int> disposed) : ILeaseHolder
        {
            public int Pid => pid;
            public string? ProcessStartUtc => Start;
            public ValueTask DisposeAsync() { disposed.Add(pid); return ValueTask.CompletedTask; }
        }
    }

    private sealed class DuplicateGrantHandler : HttpMessageHandler
    {
        public List<int> Pids { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Delete)
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var pid = body.RootElement.GetProperty("pid").GetInt32();
            Pids.Add(pid);
            var id = pid == 102 ? "L2" : "L1";
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"leaseId\":\"" + id + "\",\"maxCpuCount\":6}") };
        }
    }

    private sealed class SplitRefusalHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var label = body.RootElement.GetProperty("label").GetString();
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"type\":\"" + label + "_refusal\",\"detail\":\"" + label + " only\"}"),
            };
        }
    }

    private sealed class GatedRenewHandler : HttpMessageHandler
    {
        public TaskCompletionSource RenewEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Events { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/renew", StringComparison.Ordinal))
            {
                Events.Add("renew-entered");
                RenewEntered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                Events.Add("renew-drained");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Delete)
            {
                Events.Add("delete");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"leaseId":"L-drain","maxCpuCount":6,"renewEverySeconds":1}"""),
            };
        }
    }

    private sealed class RenewDiagnosticHandler : HttpMessageHandler
    {
        public TaskCompletionSource RenewObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/renew", StringComparison.Ordinal))
            {
                RenewObserved.TrySetResult();
                return Task.FromResult(Reply(HttpStatusCode.NotFound,
                    """{"type":"build_slot_unknown","detail":"renew gone"}"""));
            }
            if (request.Method == HttpMethod.Delete)
                return Task.FromResult(Reply(HttpStatusCode.NotFound,
                    """{"type":"build_slot_unknown","detail":"release gone"}"""));
            return Task.FromResult(Reply(HttpStatusCode.OK,
                """{"leaseId":"L","maxCpuCount":6,"renewEverySeconds":1}"""));
        }

        private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body) };
    }
}
