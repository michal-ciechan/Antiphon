using System.Diagnostics;
using System.Text;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Antiphon.PtyHost.Protocol;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokRulesFileLaunchTests
{
    [Test]
    [Arguments("cr")]
    [Arguments("lf")]
    [Arguments("crlf")]
    [Arguments("nul")]
    [Arguments("alias")]
    [Arguments("equals")]
    [Arguments("missing")]
    [Arguments("duplicate_first")]
    [Arguments("duplicate_second")]
    [Arguments("env")]
    [Arguments("braced_env")]
    [Arguments("env_flag")]
    public async Task Unsafe_final_runner_boundary_has_zero_effects_even_without_server_validation(string variant)
    {
        foreach (var herdr in new[] { false, true })
        {
            if (variant.Contains("env") && !herdr) continue;
            await using var fake = new FakeHerdrServer { LaunchScriptAgentKind = HerdrAgentKinds.Grok };
            fake.Start();
            await fake.WaitUntilListeningAsync();
            var root = Path.Combine(Path.GetTempPath(), "card0395", Guid.NewGuid().ToString("N"));
            var settings = new SessionRunnerSettings { SessionLogPath = root,
                PtyHostSourceDir = OperatingSystem.IsWindows() ? Path.Combine(root, "missing-host") : null };
            await using var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance,
                new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session, SocketPath = fake.EndpointPath }), new PowershellProcessProbe());
            var args = variant switch {
                "cr" => new[] { "--rules", "private\rsentinel" },
                "crlf" => ["--rules", "private\r\nsentinel"], "nul" => ["--rules", "private\0sentinel"],
                "alias" => ["--append-system-prompt", "private\nsentinel"], "equals" => ["--rules=private\nsentinel"],
                "missing" => ["--rules"], "duplicate_first" => ["--rules", "private\nsentinel", "--rules", "safe"],
                "duplicate_second" => ["--rules", "safe", "--rules", "private\nsentinel"],
                "env" => ["--rules", "$env:RULES"], "braced_env" => ["--rules", "${env:RULES}"],
                "env_flag" => ["$env:FLAG", "private\nsentinel"], _ => ["--rules", "private\nsentinel"] };
            var request = Request(root) with { Args = args, GrokRulesPayload = null,
                Env = new Dictionary<string,string> { ["RULES"] = "private\nsentinel", ["FLAG"] = "--rules" },
                Backend = herdr ? SessionBackends.Herdr : null,
                Herdr = herdr ? new HerdrLaunchOptions("card0395-" + Guid.NewGuid().ToString("N"), "rules", root, "rules", AgentKind: HerdrAgentKinds.Grok) : null };
            var argvCapture = Path.Combine(root, "native-argv");
            if (!OperatingSystem.IsWindows() && !herdr)
            {
                var unixEnv = request.Env.ToDictionary(pair => pair.Key, pair => pair.Value);
                unixEnv["ANTIPHON_TEST_ARGV"] = argvCapture;
                request = request with { Exe = CreateAtomicUnixArgvChild(root), Env = unixEnv };
            }
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var failure = await CaptureAsync(() => runtime.StartAsync(request, CancellationToken.None));
                    fake.Requests.ShouldBeEmpty("raw refusal must precede every Herdr effect");
                    Directory.Exists(root).ShouldBeFalse("raw refusal must precede every host/store effect");
                    failure.ShouldBeOfType<GrokRulesLaunchException>().Code.ShouldBe(GrokRulesArgvPolicy.ProblemCode);
                    runtime.List().ShouldBeEmpty();
                }
                else if (variant == "nul" && !herdr)
                {
                    var failure = await CaptureAsync(() => runtime.StartAsync(request, CancellationToken.None));
                    var refusal = failure.ShouldBeOfType<Antiphon.Agents.Pty.UnixPtyArgvException>();
                    refusal.Code.ShouldBe("pty_argv_nul", "native-nul-code");
                    refusal.Reason.ShouldBe("nul");
                    refusal.Message.ShouldNotContain("private", customMessage: "native-nul-sanitized");
                    runtime.List().ShouldBeEmpty();
                    runtime.StartCoreSessionRegistrations.ShouldBe(0, "native-nul-before-registration");
                    File.Exists(argvCapture).ShouldBeFalse();
                }
                else
                {
                    var started = await runtime.StartAsync(request, CancellationToken.None);
                    started.Status.ShouldBe("Running");
                    if (herdr)
                    {
                        fake.Requests.ShouldNotBeEmpty();
                        var script = fake.LastLaunchScriptContent.ShouldNotBeNull();
                        foreach (var arg in args)
                        {
                            var effective = arg switch { "$env:RULES" => request.Env["RULES"],
                                "${env:RULES}" => request.Env["RULES"], "$env:FLAG" => request.Env["FLAG"], _ => arg };
                            script.ShouldContain(effective);
                        }
                    }
                    else
                    {
                        var until = DateTime.UtcNow + TimeSpan.FromSeconds(20);
                        while (!File.Exists(argvCapture) && DateTime.UtcNow < until) await Task.Delay(20);
                        File.Exists(argvCapture).ShouldBeTrue("owned native child must receive the argv");
                        File.ReadAllBytes(argvCapture).ShouldBe(Encoding.UTF8.GetBytes(string.Concat(args.Select(arg => arg + "\0"))),
                            "native-argv-exact-bytes");
                    }
                }
            }
            finally
            {
                if (runtime.List().FirstOrDefault(s => s.SessionId == request.SessionId) is { } live)
                {
                    await TestSessionTeardown.KillAndAwaitHostExitAsync(runtime, request.SessionId, live.HostPid);
                    await AwaitOwnedHostExitAsync(live.HostPid);
                }
            }
        }
    }

    [Test]
    public async Task Herdr_receipt_is_durable_before_first_request_and_before_typing()
    {
        await using var fake = new FakeHerdrServer { LaunchScriptAgentKind = HerdrAgentKinds.Grok };
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var root = Path.Combine(Path.GetTempPath(), "card0395", Guid.NewGuid().ToString("N"));
        var settings = new SessionRunnerSettings { SessionLogPath = root };
        var request = Request(root) with { Exe = "grok", Backend = SessionBackends.Herdr,
            Herdr = new HerdrLaunchOptions("card0395-" + Guid.NewGuid().ToString("N"), "rules", root, "rules", AgentKind: HerdrAgentKinds.Grok) };
        var snapshots = new List<(string Method, HerdrPaneSidecar? Sidecar, byte[]? Bytes)>();
        fake.BeforeRequest = method =>
        {
            var sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(root, request.SessionId));
            var path = sidecar?.GrokRulesReceipt?.Path;
            snapshots.Add((method, sidecar, path is not null && File.Exists(path) ? File.ReadAllBytes(path) : null));
        };
        await using var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session, SocketPath = fake.EndpointPath }), new PowershellProcessProbe());
        try
        {
            var result = await runtime.StartAsync(request, CancellationToken.None);
            foreach (var snapshot in snapshots)
                snapshot.Bytes.ShouldBe(GrokRulesTransport.Encode(request.GrokRulesPayload!, true, 262144),
                    "complete rules bytes must precede the first allocation and every subsequent launch boundary");
            var initial = snapshots.First().Sidecar;
            initial.ShouldNotBeNull("receipt must precede even the first Herdr request");
            initial.LaunchPending.ShouldBeTrue();
            initial.GrokRulesReceipt.ShouldBe(result.GrokRulesReceipt);
            var typed = snapshots.First(s => s.Method == "pane.send_text").Sidecar;
            typed.ShouldNotBeNull();
            typed.PaneId.ShouldNotBeEmpty();
            typed.LaunchPending.ShouldBeTrue();
            typed.GrokRulesReceipt.ShouldBe(result.GrokRulesReceipt);
            HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(root, request.SessionId))!.LaunchPending.ShouldBeFalse();
        }
        finally { await runtime.KillAsync(request.SessionId, TimeSpan.FromSeconds(2), CancellationToken.None); }
    }

    [Test]
    public async Task Launch_failure_retains_pre_spawn_receipt_in_existing_manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "card0395", Guid.NewGuid().ToString("N"));
        var settings = new SessionRunnerSettings { SessionLogPath = root, PtyHostSourceDir = Path.Combine(root, "missing-host") };
        await using var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        var request = Request(root);
        var failure = await CaptureAsync(() => runtime.StartAsync(request, CancellationToken.None));
        failure.ShouldNotBeNull();
        var manifest = PtyHostManifest.TryLoad(PtyHostManifest.PathFor(settings.PtyHostManifestDir, request.SessionId));
        manifest.ShouldNotBeNull("receipt must survive failure before the host is spawned");
        manifest.LaunchPending.ShouldBeTrue();
        manifest.HostPid.ShouldBe(0);
        manifest.GrokRulesReceipt.ShouldNotBeNull();
        manifest.GrokRulesReceipt.Generation.ShouldBe(request.GrokRulesPayload!.Generation);
        File.ReadAllBytes(manifest.GrokRulesReceipt.Path).ShouldBe(GrokRulesTransport.Encode(request.GrokRulesPayload, true, 262144));
        runtime.List().ShouldBeEmpty();
    }

    [Test]
    [Arguments("nul")]
    [Arguments("unresolved_key")]
    [Arguments("too_large")]
    [Arguments("wrong_kind")]
    [Arguments("invalid_unicode")]
    public async Task Invalid_payload_refuses_before_session_registration_or_disk_effects(string reason)
    {
        var root = Path.Combine(Path.GetTempPath(), "card0395", Guid.NewGuid().ToString("N"));
        var settings = new SessionRunnerSettings { SessionLogPath = root, PtyHostSourceDir = Path.Combine(root, "missing-host") };
        await using var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        var content = reason switch
        {
            "nul" => "content\0",
            "unresolved_key" => "{{key:NAME}}",
            "too_large" => new string('x', 262145),
            "invalid_unicode" => "\ud800",
            _ => "content",
        };
        var request = Request(root) with
        {
            TranscriptFormat = reason == "wrong_kind" ? TranscriptFormats.Claude : TranscriptFormats.Grok,
            GrokRulesPayload = new(content, 1, Guid.NewGuid()),
        };
        var failure = await CaptureAsync(() => runtime.StartAsync(request, CancellationToken.None));
        Directory.Exists(root).ShouldBeFalse("invalid payload must be refused before any store/host effect");
        var error = failure.ShouldBeOfType<GrokRulesTransportException>();
        error.Code.ShouldBe("grok_rules_content_invalid");
        error.Reason.ShouldStartWith(reason);
        runtime.List().ShouldBeEmpty();
        Directory.Exists(root).ShouldBeFalse();
    }

    [Test]
    public async Task Explicit_rules_conflict_and_unsafe_source_precedence_have_no_effects()
    {
        var root = Path.Combine(Path.GetTempPath(), "card0395", Guid.NewGuid().ToString("N"));
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = root, PtyHostSourceDir = Path.Combine(root, "missing-host") }), NullLogger<SessionRunnerRuntime>.Instance);
        var request = Request(root) with { Args = ["--rules", "@C:\\literal path\\rules.md"] };
        var error = await CaptureAsync(() => runtime.StartAsync(request, CancellationToken.None));
        Directory.Exists(root).ShouldBeFalse("source conflict must refuse before rules materialization");
        runtime.List().ShouldBeEmpty();
        error.ShouldBeOfType<GrokRulesTransportException>()
            .Code.ShouldBe("grok_rules_source_conflict");
        if (OperatingSystem.IsWindows())
            (await Should.ThrowAsync<GrokRulesLaunchException>(() => runtime.StartAsync(
                request with { Args = ["--rules", "unsafe\nbody"] }, CancellationToken.None)))
                .Code.ShouldBe("grok_rules_argv_unsafe");
        runtime.List().ShouldBeEmpty();
        Directory.Exists(root).ShouldBeFalse();
    }

    [Test]
    public async Task Actual_argv_budget_includes_generated_bootstrap_before_materialization()
    {
        var root = Path.Combine(Path.GetTempPath(), "card0395", Guid.NewGuid().ToString("N"));
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = root, PtyHostSourceDir = Path.Combine(root, "missing-host") }), NullLogger<SessionRunnerRuntime>.Instance);
        var request = Request(root) with { CommandLineBudgetChars = 100 };
        var error = await CaptureAsync(() => runtime.StartAsync(request, CancellationToken.None));
        Directory.Exists(root).ShouldBeFalse("actual argv over budget must refuse before rules materialization");
        runtime.List().ShouldBeEmpty();
        error.ShouldBeOfType<GrokRulesTransportException>()
            .Reason.ShouldBe("command_line_budget");
        runtime.List().ShouldBeEmpty();
        Directory.Exists(root).ShouldBeFalse();
    }

    private static RunnerLaunchRequest Request(string root) => new(Guid.NewGuid(), "missing-executable", [],
        new Dictionary<string, string>(), root, 120, 30, TranscriptFormat: TranscriptFormats.Grok,
        GrokRulesPayload: new("full\r\nrules", 1, Guid.NewGuid()));

    private static string CreateAtomicUnixArgvChild(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "owned-argv-child.sh");
        File.WriteAllText(path, "#!/bin/sh\ntemp=\"$ANTIPHON_TEST_ARGV.tmp.$$\"\nprintf '%s\\0' \"$@\" > \"$temp\"\nmv \"$temp\" \"$ANTIPHON_TEST_ARGV\"\nprintf 'ARGV_CAPTURED\\n'\nwhile :; do sleep 1; done\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static async Task AwaitOwnedHostExitAsync(int? hostPid)
    {
        if (hostPid is not { } pid) return;
        try
        {
            using var host = Process.GetProcessById(pid);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await host.WaitForExitAsync(deadline.Token);
        }
        catch (ArgumentException) { /* already exited */ }
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }
}
