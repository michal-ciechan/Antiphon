using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class UnixPtyArgvAdmissionTests
{
    [Test]
    [Arguments("plain")]
    [Arguments("grok")]
    [Arguments("grok_payload")]
    [Arguments("tracked")]
    public async Task Nul_request_is_refused_before_effects(string shape)
    {
        RequireUnix();
        var root = Root();
        var settings = Settings(root);
        await using var runtime = Runtime(settings);
        var request = Launch(root) with { Exe = "/bin/sh\0private" };
        if (shape == "grok")
            request = request with { Exe = "/bin/sh", Backend = SessionBackends.PtyHost,
                TranscriptFormat = TranscriptFormats.Grok, Args = ["--rules", "private\0tail"] };
        if (shape == "grok_payload")
            request = request with { Exe = "/bin/sh", Backend = "PTY-HOST",
                TranscriptFormat = TranscriptFormats.Grok, Args = ["--extra", "private\0tail"],
                GrokRulesPayload = new("valid rules", 1, Guid.NewGuid()) };
        if (shape == "tracked")
        {
            var store = runtime.RunnerStoreId;
            request = request with { Exe = "/bin/sh", Args = ["private\0tail"],
                VerificationBinding = Binding(request.SessionId, store) };
        }
        var beforeFiles = Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : [];
        var registrations = runtime.StartCoreSessionRegistrations;
        var thrown = await CaptureAsync(() => runtime.StartAsync(request, CancellationToken.None));
        runtime.StartCoreSessionRegistrations.ShouldBe(registrations, "nul-before-registration");
        runtime.List().ShouldBeEmpty();
        (Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : [])
            .ShouldBe(beforeFiles, "nul-before-disk-effects");
        await Should.ThrowAsync<KeyNotFoundException>(() => runtime.GetAsync(request.SessionId, CancellationToken.None));
        AssertNul(thrown.ShouldBeOfType<UnixPtyArgvException>());
    }

    [Test]
    [Arguments("ordinary")]
    [Arguments("platform_constrained")]
    public async Task Nul_refusal_is_named_on_both_launch_routes(string route)
    {
        RequireUnix();
        var root = Root();
        await using var runtime = Runtime(Settings(root));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Test" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(runtime);
        await using var app = builder.Build();
        app.MapSessionLaunchRoute();
        app.MapPlatformConstrainedLaunchRoute();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        new Uri(address).Port.ShouldNotBe(17204);
        using var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(20) };
        var request = Launch(root) with { Args = ["--", "private\0tail"],
            RequiredPlatform = route == "platform_constrained" ? RunnerPlatformWire.Linux : null };
        using var reply = await http.PostAsJsonAsync(route == "ordinary" ? "/sessions" : "/sessions/platform-constrained", request);
        reply.StatusCode.ShouldBe(HttpStatusCode.Conflict, "http-nul-status");
        using var body = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("type").GetString().ShouldBe("pty_argv_nul", "http-nul-type");
        body.RootElement.GetProperty("title").GetString().ShouldBe("pty_argv_nul", "http-nul-title");
        body.RootElement.GetProperty("detail").GetString().ShouldNotContain("private", customMessage: "http-nul-sanitized");
        runtime.StartCoreSessionRegistrations.ShouldBe(0, "http-no-registration");
        Directory.Exists(root).ShouldBeFalse("http-no-disk-effects");
        await app.StopAsync();
    }

    [Test]
    [Arguments("launch")]
    [Arguments("platform_constrained")]
    public async Task Phone_home_nul_refusal_is_named(string operation)
    {
        RequireUnix();
        var root = Root();
        Directory.CreateDirectory(root);
        await using var runtime = Runtime(Settings(root));
        var generation = SessionGeneration.Normalize(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));
        var request = Launch(root) with { Args = ["private\0tail"], AcceptedStartedAt = generation,
            RequiredPlatform = operation == "platform_constrained" ? RunnerPlatformWire.Linux : null };
        var settings = new PhoneHomeSettings { Enabled = true, AllowedCwd = root, RawExeAllowList = ["/bin/sh"],
            Capacity = 1, CapacityStatePath = Path.Combine(root, "capacity"),
            LaunchGenerationsPath = Path.Combine(root, "generations"), ClaudeAuthProbeEnabled = false,
            GrokAuthProbeEnabled = false, CodexAuthProbeEnabled = false };
        var clock = new FakeTimeProvider(new DateTimeOffset(generation, TimeSpan.Zero));
        var dispatcher = new PhoneHomeCommandDispatcher(new PhoneHomeRuntimeAdapter(runtime,
            new RunnerBuildDto("test", null, generation, generation)), settings, time: clock);
        var op = operation == "launch" ? PhoneHomeOperation.Launch : PhoneHomeOperation.LaunchPlatformConstrained;
        var frame = new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), op,
            JsonSerializer.SerializeToElement(request, PhoneHomeFraming.Json));
        var reply = await dispatcher.DispatchAsync(frame, CancellationToken.None);
        reply.RequestId.ShouldBe(frame.RequestId);
        reply.Kind.ShouldBe(PhoneHomeFrameKind.Error, "phone-home-nul-kind");
        reply.ErrorCode.ShouldBe("pty_argv_nul", "phone-home-nul-code");
        reply.ErrorDetail.ShouldNotContain("private", customMessage: "phone-home-nul-sanitized");
        reply.StatusCode.ShouldBe(409, "phone-home-nul-status");
        runtime.StartCoreSessionRegistrations.ShouldBe(0);
        new PhoneHomeLaunchGenerationStore(settings.LaunchGenerationsPath).Read(request.SessionId)
            .ShouldBe(generation, "phone-home-retains-watermark");
    }

    [Test]
    [Arguments("platform_mismatch")]
    [Arguments("platform_invalid")]
    [Arguments("unknown_backend")]
    [Arguments("herdr_nul")]
    public async Task Platform_and_backend_boundaries_keep_existing_verdicts(string shape)
    {
        RequireUnix();
        var root = Root();
        await using var fake = new FakeHerdrServer { LaunchScriptAgentKind = HerdrAgentKinds.Grok };
        if (shape == "herdr_nul") { fake.Start(); await fake.WaitUntilListeningAsync(); }
        await using var runtime = new SessionRunnerRuntime(Options.Create(Settings(root)),
            NullLogger<SessionRunnerRuntime>.Instance,
            shape == "herdr_nul" ? new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session, SocketPath = fake.EndpointPath }) : null,
            new PowershellProcessProbe());
        var request = Launch(root) with { Args = ["--rules", "private\0tail"],
            RequiredPlatform = shape switch { "platform_mismatch" => RunnerPlatformWire.Windows,
                "platform_invalid" => "bsd", _ => null },
            Backend = shape switch { "unknown_backend" => "unknown", "herdr_nul" => SessionBackends.Herdr, _ => null } };
        if (shape == "herdr_nul")
            request = request with { Exe = "/bin/sh", TranscriptFormat = TranscriptFormats.Grok,
                Herdr = new HerdrLaunchOptions("c863-" + Guid.NewGuid().ToString("N"), "argv", root, "argv", AgentKind: HerdrAgentKinds.Grok) };
        if (shape == "herdr_nul")
        {
            try
            {
                (await runtime.StartAsync(request, CancellationToken.None)).Status.ShouldBe("Running");
                fake.LastLaunchScriptContent.ShouldNotBeNull().ShouldContain("private\0tail", customMessage: "herdr-script-preserves-nul");
            }
            finally { await runtime.KillAsync(request.SessionId, TimeSpan.FromSeconds(2), CancellationToken.None); }
        }
        else
        {
            var error = await CaptureAsync(() => runtime.StartAsync(request, CancellationToken.None));
            if (shape == "unknown_backend")
            {
                error.ShouldBeOfType<ArgumentException>().Message.ShouldContain("Unsupported session backend", customMessage: "unknown-backend-precedence");
                (error is UnixPtyArgvException).ShouldBeFalse("unknown-backend-keeps-existing-verdict");
            }
            else
                error.ShouldBeOfType<RunnerPlatformLaunchException>().Code.ShouldBe(
                    shape == "platform_mismatch" ? RunnerPlatformLaunchGuard.Mismatch : RunnerPlatformLaunchGuard.Invalid,
                    "platform-precedence");
            runtime.StartCoreSessionRegistrations.ShouldBe(0);
            Directory.Exists(root).ShouldBeFalse();
        }
    }

    private static SessionRunnerSettings Settings(string root) => new()
    {
        SessionLogPath = root, PtyHostSourceDir = Path.Combine(root, "missing-host"), CpuWatchdogEnabled = false,
    };

    private static SessionRunnerRuntime Runtime(SessionRunnerSettings settings) => new(
        Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);

    private static RunnerLaunchRequest Launch(string root) => new(Guid.NewGuid(), "/bin/sh", [],
        new Dictionary<string, string>(), root, 80, 24);

    private static VerificationExecutionBinding Binding(Guid sessionId, Guid store) => new(
        Guid.NewGuid(), new VerificationSourceIdentity(Guid.NewGuid(), Guid.NewGuid(), "deadbeef"),
        new VerificationSessionGeneration(sessionId, DateTime.UtcNow),
        new VerificationCreationCoordinates("r", "g", "w", "wg", "main", Guid.NewGuid()),
        VerificationCustodyBackends.LinuxCgroup, store);

    private static string Root() => Path.Combine(Path.GetTempPath(), "c863-runner-" + Guid.NewGuid().ToString("N"));

    private static void RequireUnix()
    {
        if (OperatingSystem.IsWindows()) throw new InvalidOperationException("Unix checkpoint selected on Windows");
    }

    private static void AssertNul(UnixPtyArgvException error)
    {
        error.Code.ShouldBe("pty_argv_nul", "runner-nul-code");
        error.Reason.ShouldBe("nul");
        error.Message.ShouldNotContain("private", customMessage: "runner-nul-sanitized");
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception error) { return error; }
    }
}
