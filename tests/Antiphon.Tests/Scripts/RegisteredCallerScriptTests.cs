using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-2004. The registered scripts send their own <c>X-Antiphon-Caller</c> label.
/// <c>scripts/lib/antiphon-caller.ps1</c> (and its compiled reading, <c>Antiphon.Nightly.AntiphonCaller</c>, used by the
/// status-snapshot command) refuses any other value and does not copy it into the header or the error. The server list is
/// <see cref="AgentTaskGetPollMetrics.RegisteredLabels"/>.
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
        var run = await RunAsync(stub, "delegate.ps1", "-Status", TaskId);
        AssertRegisteredCaller(stub, "delegate.ps1");
        stub.Calls.ShouldContain(c => c.Path == "/api/agent-tasks/" + TaskId);
        run.ExitCode.ShouldBe(0, run.Stderr);
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
        foreach (var label in new[] { "runner-drain.ps1", "phone-home", "", "status-snapshot" })
        {
            var refused = Should.Throw<InvalidOperationException>(() => Antiphon.Nightly.AntiphonCaller.Label(label));
            if (label.Length > 0) refused.Message.ShouldNotContain(label);
        }
    }

    [Test]
    public async Task Checkpoint_task_sends_its_registered_caller_label()
    {
        using var stub = new HeaderStub(_ => new(200,
            """{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","summary":{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","title":"caller","workingDirectory":"/no/such/caller-dir","workspace":"Shared"}}"""));
        var run = await RunAsync(stub, "checkpoint-task.ps1", "-TaskId", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        AssertRegisteredCaller(stub, "checkpoint-task.ps1");
        stub.Calls.ShouldContain(c => c.Path == "/api/agent-tasks/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        run.ExitCode.ShouldBe(2, run.Stderr);
        run.Stderr.ShouldContain("no usable working directory");
    }

    [Test]
    public async Task Card_limits_sends_its_registered_caller_label()
    {
        using var stub = new HeaderStub(_ => new(200,
            """{"maxTitleLength":1,"maxDescriptionLength":1,"maxPrivateNotesLength":1,"maxReasonLength":1,"maxActorLength":1,"maxAliasLength":1,"maxAliasWords":1}"""));
        var run = await RunAsync(stub, "card.ps1", "-Limits");
        AssertRegisteredCaller(stub, "card.ps1");
        stub.Calls.ShouldContain(c => c.Path == "/api/cards/limits");
        run.ExitCode.ShouldBe(0, run.Stderr);
    }

    [Test]
    public async Task Unregistered_caller_label_is_refused_and_not_echoed()
    {
        var helper = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "antiphon-caller.ps1");
        File.Exists(helper).ShouldBeTrue(
            "scripts/lib/antiphon-caller.ps1 must refuse an unregistered X-Antiphon-Caller");
        var text = await File.ReadAllTextAsync(helper);
        var block = Regex.Match(text, @"\$script:AntiphonRegisteredCallers = @\((.*?)\)", RegexOptions.Singleline);
        block.Success.ShouldBeTrue("the helper must publish AntiphonRegisteredCallers");
        var listed = Regex.Matches(block.Groups[1].Value, @"'([^']*)'")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        listed.OrderBy(label => label, StringComparer.Ordinal).ShouldBe(
            AgentTaskGetPollMetrics.RegisteredLabels.OrderBy(label => label, StringComparer.Ordinal).ToArray());

        var probe = await RunPwshAsync(
            "$ErrorActionPreference = 'Stop'; " +
            ". $env:CALLER_HELPER; " +
            "$ok = @{}; Add-AntiphonCallerHeader -Headers $ok -Label '  Delegate.ps1  '; " +
            "if ($ok['X-Antiphon-Caller'] -ne 'delegate.ps1') { throw 'registered-label-not-normalized' }; " +
            "foreach ($label in @('runner-drain.ps1','phone-home','')) { " +
            "  $bad = @{}; $refused = $false; $echoed = $false; " +
            "  try { Add-AntiphonCallerHeader -Headers $bad -Label $label } catch { " +
            "    $refused = $true; " +
            "    if (-not [string]::IsNullOrWhiteSpace($label) -and [string]$_.Exception.Message -like ('*' + $label + '*')) { $echoed = $true } " +
            "  }; " +
            "  if (-not $refused) { throw 'unregistered-was-accepted' }; " +
            "  if ($echoed) { throw 'unregistered-label-was-echoed' }; " +
            "  if ($bad.ContainsKey('X-Antiphon-Caller')) { throw 'unregistered-header-was-set' } " +
            "}; Write-Output 'REFUSED'",
            ("CALLER_HELPER", helper));
        probe.ExitCode.ShouldBe(0, probe.Stderr);
        probe.Stdout.Trim().ShouldBe("REFUSED");
        probe.Stderr.ShouldNotContain("runner-drain.ps1");
        probe.Stderr.ShouldNotContain("phone-home");
    }

    private static void AssertRegisteredCaller(HeaderStub stub, string label)
    {
        AgentTaskGetPollMetrics.RegisteredLabels.Contains(label).ShouldBeTrue();
        stub.Calls.ShouldNotBeEmpty();
        stub.Calls.ShouldAllBe(call => call.Caller == label);
    }

    private static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        HeaderStub stub, string script, params string[] args)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-File",
                     Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", script),
                 }.Concat(args))
            start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_API"] = stub.BaseUrl.TrimEnd('/');
        start.Environment["ANTIPHON_TASK_TOKEN"] = "synthetic-task-token";
        return RunProcessAsync(start);
    }

    private static Task<(int ExitCode, string Stdout, string Stderr)> RunPwshAsync(
        string command, params (string Name, string Value)[] environment)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        foreach (var (name, value) in environment) start.Environment[name] = value;
        return RunProcessAsync(start);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        return (process.ExitCode, await stdout, await stderr);
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
