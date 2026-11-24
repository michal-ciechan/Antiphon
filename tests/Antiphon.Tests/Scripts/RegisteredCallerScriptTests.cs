using System.Net;
using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-2004. The registered callers send their own <c>X-Antiphon-Caller</c> label: the delegate, status-snapshot, card and
/// checkpoint-task commands keep the labels of the scripts they replaced. <c>Antiphon.Nightly.AntiphonCaller</c> (which replaced
/// <c>scripts/lib/antiphon-caller.ps1</c>, deleted with its last loader in unit 29 of the 2026-11-22 plan) refuses any other value
/// and does not copy it into the header or the error. The server list is <see cref="AgentTaskGetPollMetrics.RegisteredLabels"/>.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RegisteredCallerScriptTests
{
    private const string TaskId = "12345678-1111-2222-3333-444444444444";
    private const string ProjectId = "acacacac-1111-2222-3333-444444444444";

    [Test]
    public async Task Delegate_status_sends_its_registered_caller_label()
    {
        using var stub = new HeaderStub(_ => new(200,
            """{"summary":{"status":"Working","title":"caller","kind":"Grok","role":"Code","modelLevel":"High"}}"""));
        // The delegate command (in-process since unit 28) keeps the registered label of the script it replaced.
        var run = await DelegateScriptRunner.RunAsync(stub.BaseUrl,
            new Dictionary<string, string?> { ["ANTIPHON_TASK_TOKEN"] = "synthetic-task-token" }, "status", TaskId);
        Antiphon.Nightly.DelegateCli.CallerLabel.ShouldBe("delegate.ps1");
        AssertRegisteredCaller(stub, "delegate.ps1");
        stub.Calls.ShouldContain(c => c.Path == "/api/agent-tasks/" + TaskId);
        run.ExitCode.ShouldBe(0, run.Output);
    }

    [Test]
    public async Task Status_snapshot_sends_its_registered_caller_label()
    {
        using var repo = new ScratchGitRepo("caller-snapshot");
        await repo.CommitFileAsync("seed.txt", "master fixture");
        await repo.AddBareOriginAsync();
        using var stub = new HeaderStub(call => call.Path switch
        {
            "/api/agent-tasks/pipeline" => new(200,
                $$"""{"taskScope":"project","stages":[{"role":"Code","inFlight":[{"taskId":"{{TaskId}}"}],"queued":[],"blocked":[],"ready":[]}]}"""),
            "/api/session-runners/fixture-runner/slots" => new(200,
                """{"occupied":0,"declaredCapacity":1,"slots":[]}"""),
            "/api/orchestrator-instructions" => new(200, """{"content":"fixture"}"""),
            _ when call.Path == "/api/agent-tasks/" + TaskId => new(200,
                """{"status":"Working","nextStage":"review","handoff":"Review snapshot"}"""),
            _ => new(404, """{"detail":"Unexpected request"}""")
        });
        // The status-snapshot command (in-process since unit 7) keeps the registered label of the script it replaced.
        var run = StatusSnapshotScriptTests.RunWith(Antiphon.Nightly.NightlyReport.HttpTransport(timeout: Antiphon.Nightly.StatusSnapshot.Deadline),
            stub.BaseUrl.TrimEnd('/'), repo.Path, "--project-id", ProjectId);
        AssertRegisteredCaller(stub, "status-snapshot.ps1");
        stub.Calls.ShouldContain(c => c.Path == "/api/agent-tasks/" + TaskId);
        run.ExitCode.ShouldBe(0, run.Stdout);
    }

    [Test]
    public void The_compiled_caller_list_is_the_server_list_and_refuses_without_echo()
    {
        Antiphon.Nightly.AntiphonCaller.RegisteredLabels.OrderBy(label => label, StringComparer.Ordinal).ShouldBe(
            AgentTaskGetPollMetrics.RegisteredLabels.OrderBy(label => label, StringComparer.Ordinal).ToArray());
        Antiphon.Nightly.AntiphonCaller.Label("  Status-Snapshot.ps1  ").ShouldBe("status-snapshot.ps1");
        Antiphon.Nightly.AntiphonCaller.Label("  Delegate.ps1  ").ShouldBe("delegate.ps1");
        foreach (var label in new[] { "runner-drain.ps1", "phone-home", "", "status-snapshot" })
        {
            var refused = Should.Throw<InvalidOperationException>(() => Antiphon.Nightly.AntiphonCaller.Label(label));
            if (label.Length > 0) refused.Message.ShouldNotContain(label);
        }
    }

    [Test]
    public void Checkpoint_task_sends_its_registered_caller_label()
    {
        // The checkpoint-task command (in-process since unit 29) keeps the registered label of the script it replaced.
        var http = new RecordingOperatorHttp(_ => new(200,
            """{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","summary":{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","title":"caller","workingDirectory":"/no/such/caller-dir","workspace":"Shared"}}"""));
        var run = BoardCommandRun.Run(host => Antiphon.Nightly.CheckpointTask.Run(Antiphon.Nightly.CheckpointTaskOptions.Parse(
            ["--task-id", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"], BoardCommandRun.Api, "synthetic-task-token"), host, new System.IO.Abstractions.FileSystem(), http.Transport));
        Antiphon.Nightly.CheckpointTask.CallerLabel.ShouldBe("checkpoint-task.ps1");
        AgentTaskGetPollMetrics.RegisteredLabels.Contains(Antiphon.Nightly.CheckpointTask.CallerLabel).ShouldBeTrue();
        http.Calls.Select(c => c.Method + " " + c.Path).ShouldBe(["GET /api/agent-tasks/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]);
        http.Calls.ShouldAllBe(call => call.Headers["X-Antiphon-Caller"] == "checkpoint-task.ps1");
        run.Exit.ShouldBe(2, run.All);
        run.Error.Trim().ShouldBe("Task aaaaaaaa has no usable working directory.");
    }

    [Test]
    public void Card_limits_sends_its_registered_caller_label()
    {
        // The card command (in-process since unit 27) keeps the registered label of the script it replaced, on every request.
        var http = new RecordingOperatorHttp(call => call.Path.StartsWith("/api/cards/limits", StringComparison.Ordinal)
            ? new(200, """{"maxTitleLength":1,"maxDescriptionLength":1,"maxPrivateNotesLength":1,"maxReasonLength":1,"maxActorLength":1,"maxAliasLength":1,"maxAliasWords":1}""")
            : call.Path.Contains("/relationships", StringComparison.Ordinal)
                ? new(200, """{"blockedBy":[],"blocks":[]}""")
                : new(200, """{"id":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","identifier":"CARD-0001","status":"Backlog","title":"caller"}"""));
        var run = CardCommandRun.Run(http.Transport, ["limits"], taskToken: "synthetic-task-token");
        run.Exit.ShouldBe(0, run.All);
        var get = CardCommandRun.Run(http.Transport, ["get", "CARD-0001"], taskToken: "synthetic-task-token");
        get.Exit.ShouldBe(0, get.All);
        Antiphon.Nightly.CardCli.CallerLabel.ShouldBe("card.ps1");
        AgentTaskGetPollMetrics.RegisteredLabels.Contains(Antiphon.Nightly.CardCli.CallerLabel).ShouldBeTrue();
        http.Calls.Select(c => c.Method + " " + new Uri(c.Uri).AbsolutePath).ShouldBe(
            ["GET /api/cards/limits", "GET /api/cards/CARD-0001", "GET /api/cards/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/relationships"]);
        http.Calls.ShouldAllBe(call => call.Headers["X-Antiphon-Caller"] == "card.ps1");
    }

    private static void AssertRegisteredCaller(HeaderStub stub, string label)
    {
        AgentTaskGetPollMetrics.RegisteredLabels.Contains(label).ShouldBeTrue();
        stub.Calls.ShouldNotBeEmpty();
        stub.Calls.ShouldAllBe(call => call.Caller == label);
    }

    private sealed class HeaderStub : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _pump;
        private readonly Func<Call, Reply> _respond;
        public readonly List<Call> Calls = [];
        public string BaseUrl { get; }

        public HeaderStub(Func<Call, Reply> respond)
        {
            _respond = respond;
            BaseUrl = EphemeralHttpListener.BindLoopback(out _listener);
            _pump = Task.Run(PumpAsync);
        }

        private async Task PumpAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }
                var request = context.Request;
                var call = new Call(request.HttpMethod, request.Url!.AbsolutePath, request.Headers["X-Antiphon-Caller"]);
                Calls.Add(call);
                var reply = _respond(call);
                var bytes = Encoding.UTF8.GetBytes(reply.Body);
                context.Response.StatusCode = reply.Status;
                context.Response.ContentType = "application/json";
                try { await context.Response.OutputStream.WriteAsync(bytes); }
                catch (HttpListenerException) { }
                finally { context.Response.Close(); }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); } catch { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch { }
            _listener.Close();
            _stop.Dispose();
        }
    }

    private readonly record struct Call(string Method, string Path, string? Caller);
    private readonly record struct Reply(int Status, string Body);
}
