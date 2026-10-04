using System.Reflection;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
[NotInParallel]
public class C1022BackendCapabilitiesTests
{
    private static readonly RunnerBuildDto Build = new("test", new string('a', 40), DateTime.UnixEpoch, DateTime.UnixEpoch);

    [Test]
    public void Daemon_environment_overrides_configuration()
    {
        foreach (var environment in new string?[] { null, "", " ", "modern", "inbox" })
        foreach (var configured in new string?[] { null, "modern", "inbox" })
        {
            var expected = environment is null or "" ? configured ?? "" : environment;
            PtyBackendConfiguration.EffectiveRequest(configured, environment)
                .ShouldBe(expected, "daemon-precedence");
        }
    }

    [Test]
    public async Task Local_capabilities_use_runtime_decision() => await CheckProjectionAsync(phone: false);

    [Test]
    public async Task Phone_home_capabilities_use_runtime_decision() => await CheckProjectionAsync(phone: true);

    private static async Task CheckProjectionAsync(bool phone)
    {
        var previous = Environment.GetEnvironmentVariable(PtyBackendPolicy.EnvVar);
        try
        {
            foreach (var backend in new[] { PtyBackend.ModernConPty, PtyBackend.InboxConhost, PtyBackend.UnixPty })
            {
                Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar,
                    backend == PtyBackend.InboxConhost ? "modern" : "inbox");
                await using var world = new World(backend);
                var decision = world.Runtime.BackendDecision;
                PtyBackendConfiguration.LogDecision(world.Log, decision);
                var warnings = world.Log.Levels.Count(x => x == LogLevel.Warning);
                warnings.ShouldBe(decision.Deprecated ? 1 : 0, "startup-warning");
                for (var i = 0; i < 3; i++)
                {
                    var dto = phone ? world.Phone() : world.Local();
                    dto.PtyBackend.ShouldBe(backend.ToString(), phone ? "phone-decision" : "local-decision");
                    dto.PtyBackendRequested.ShouldBe(decision.Requested);
                    dto.PtyBackendReason.ShouldBe(decision.Reason);
                    dto.PtyBackendFellBack.ShouldBe(decision.FellBack);
                    dto.PtyBackendDeprecated.ShouldBe(decision.Deprecated);
                }
                world.Log.Levels.Count(x => x == LogLevel.Warning).ShouldBe(warnings, "polls-do-not-warn");
            }
            var log = new CapturingLogger();
            PtyBackendConfiguration.LogDecision(log, new(PtyBackend.InboxConhost, null, "modern", "missing pair"));
            PtyBackendConfiguration.LogDecision(log, new(PtyBackend.ModernConPty, "pair", "typo", "unrecognised"));
            log.Levels.ShouldBe([LogLevel.Warning, LogLevel.Warning], "fallback-and-unknown-warn");
        }
        finally { Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, previous); }
    }

    [Test]
    public async Task Custody_uses_runtime_decision()
    {
        var previous = Environment.GetEnvironmentVariable(PtyBackendPolicy.EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, "inbox");
            await using (var modern = new World(PtyBackend.ModernConPty))
                modern.Runtime.VerificationCustodyBackend.ShouldBe(VerificationCustodyBackends.WindowsJob, "custody-decision");
            Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, "modern");
            await using var legacy = new World(PtyBackend.InboxConhost);
            legacy.Runtime.VerificationCustodyBackend.ShouldBeNull("custody-decision");
            // Phone-home's existing Linux-only custody rule is independent and remains unchanged.
            legacy.Phone().VerificationCustodyBackend.ShouldBeNull();
        }
        finally { Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, previous); }
    }

    [Test]
    public async Task Capability_json_preserves_nullable_deprecation_and_cli_observations()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        const string old = """{"ptyBackend":"InboxConhost","ptyBackendRequested":"inbox","ptyBackendReason":"old","ptyBackendFellBack":false}""";
        JsonSerializer.Deserialize<RunnerCapabilitiesDto>(old, web)!.PtyBackendDeprecated.ShouldBeNull("deprecation-wire");
        JsonSerializer.Deserialize<RunnerCapabilitiesDto>(old.Replace("InboxConhost", "future-backend"), web)!
            .PtyBackend.ShouldBe("future-backend");
        using var probe = new CodexCliVersionProbe(TimeProvider.System, new PhoneHomeProcessIdentity(),
            Options.Create(new CodexCliVersionSettings()));
        var sample = new RunnerCodexCliVersionDto("0.160.1", DateTimeOffset.UnixEpoch.AddDays(11), "probe_unavailable", "distinct-fingerprint");
        // Supply an observation to the real producer; this serialization test starts no CLI.
        typeof(CodexCliVersionProbe).GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(probe, sample);
        foreach (var backend in new[] { PtyBackend.ModernConPty, PtyBackend.InboxConhost })
        {
            await using var world = new World(backend);
            world.Runtime.CodexCliProbe = probe;
            foreach (var dto in new[] { world.Local(), world.Phone() })
            {
                var roundTrip = JsonSerializer.Deserialize<RunnerCapabilitiesDto>(JsonSerializer.Serialize(dto, web), web)!;
                roundTrip.PtyBackendDeprecated.ShouldBe(backend == PtyBackend.InboxConhost, "deprecation-wire");
                roundTrip.CodexCliVersion.ShouldBe("0.160.1", "cli-fields-retained");
                roundTrip.CodexCliVersionCheckedAtUtc.ShouldBe(DateTimeOffset.UnixEpoch.AddDays(11), "cli-fields-retained");
                roundTrip.CodexCliVersionError.ShouldBe("probe_unavailable", "cli-fields-retained");
                roundTrip.CodexCliLauncherFingerprint.ShouldBe("distinct-fingerprint", "cli-fields-retained");
                roundTrip.Features.ShouldNotBeNull().ShouldContain(CodexCliVersionProbe.Capability);
                var nullable = dto with { PtyBackendDeprecated = null };
                JsonSerializer.Deserialize<RunnerCapabilitiesDto>(JsonSerializer.Serialize(nullable, web), web)!
                    .PtyBackendDeprecated.ShouldBeNull("deprecation-wire");
            }
        }
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "c1022-cap-" + Guid.NewGuid().ToString("N"));
        public CapturingLogger Log { get; } = new();
        public SessionRunnerRuntime Runtime { get; }
        public World(PtyBackend backend)
        {
            var raw = backend == PtyBackend.InboxConhost ? "inbox" : "modern";
            Runtime = new(Options.Create(new SessionRunnerSettings { SessionLogPath = _root, PtyBackend = raw }), Log)
            {
                BackendPlatformIsWindows = true,
                BackendResolver = requested => new(backend, backend == PtyBackend.ModernConPty ? "pair" : null,
                    requested!, "instance decision"),
            };
        }
        public RunnerCapabilitiesDto Local() => Runtime.DescribeCapabilities(Build, [SessionBackends.PtyHost], []);
        public RunnerCapabilitiesDto Phone() => new PhoneHomeRuntimeAdapter(Runtime, Build).Capabilities();
        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private sealed class CapturingLogger : ILogger<SessionRunnerRuntime>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(level);
    }
}
