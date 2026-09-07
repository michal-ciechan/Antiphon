using System.Text.Json;
using System.Reflection;
using Antiphon.SessionRunner.Contracts;
using Antiphon.PtyHost.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public class RunnerStartupDiagnosticsTests
{
    [Test]
    public async Task Empty_sweep_records_zero_work_phases()
    {
        using var f = new RestartFixture(); var log = new MilestoneLogger();
        await using var runtime = new SessionRunnerRuntime(Options.Create(new SessionRunnerSettings { SessionLogPath = f.Root }), log);
        (await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None)).ShouldBe(0);
        log.Records.Select(r => r.GetProperty("event").GetString()).ShouldBe(new[] { "adoption-sweep-start", "claims-start", "claims-end", "herdr-start", "herdr-end", "pty-manifests-start", "pty-manifests-end", "adoption-sweep-end" });
        foreach (var r in log.Records.Where(r => r.GetProperty("event").GetString()!.EndsWith("-end")))
        { r.GetProperty("elapsedMs").GetDouble().ShouldBeGreaterThanOrEqualTo(0); r.GetProperty("outcome").GetString().ShouldBe("completed"); }
        log.Records.Single(r => r.GetProperty("event").GetString() == "pty-manifests-end").GetProperty("count").GetInt32().ShouldBe(0);
    }
    [Test]
    public async Task Canceled_manifest_pass_preserves_cancellation()
    {
        using var f = new RestartFixture(); var log = new MilestoneLogger(); var settings = new SessionRunnerSettings { SessionLogPath = f.Root };
        SeedTerminal(settings);
        await using var runtime = new SessionRunnerRuntime(Options.Create(settings), log);
        await Should.ThrowAsync<OperationCanceledException>(() => runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), new CancellationToken(true)));
        log.Records.Last().GetProperty("outcome").GetString().ShouldBe("failed-or-canceled");
        log.Records.Single(r => r.GetProperty("event").GetString() == "pty-manifests-end").GetProperty("outcome").GetString().ShouldBe("failed-or-canceled");
    }
    [Test]
    public async Task Diagnostic_sink_failure_does_not_change_adoption()
    {
        using var f = new RestartFixture(); var log = new MilestoneLogger { Throw = true }; var settings = new SessionRunnerSettings { SessionLogPath = f.Root };
        var id = SeedTerminal(settings);
        await using var runtime = new SessionRunnerRuntime(Options.Create(settings), log);
        (await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None)).ShouldBe(0);
        runtime.Get(id).Status.ShouldBe("Exited"); runtime.Get(id).ExitCode.ShouldBe(17); log.Attempts.ShouldBeGreaterThan(0);
        SeedTerminal(settings);
        await Should.ThrowAsync<OperationCanceledException>(() => runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), new CancellationToken(true)));
    }
    [Test, Arguments(SessionBackends.PtyHost, 0), Arguments(SessionBackends.Herdr, 1)]
    public async Task Backend_count_does_not_build_a_dto_or_interrupt_the_remaining_sweep(string backend, int expectedCount)
    {
        using var f = new RestartFixture(); var log = new MilestoneLogger(); var settings = new SessionRunnerSettings { SessionLogPath = f.Root };
        var existingId = SeedTerminal(settings);
        await using var runtime = new SessionRunnerRuntime(Options.Create(settings), log);
        await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        var sessions = (System.Collections.IDictionary)typeof(SessionRunnerRuntime).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        var session = sessions[existingId]!;
        var tailer = session.GetType().GetField("_tailer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var backendField = session.GetType().GetField("_backend", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalTailer = tailer.GetValue(session); var originalBackend = backendField.GetValue(session);
        try
        {
            tailer.SetValue(session, new ThrowingTailer()); backendField.SetValue(session, backend);
            Should.Throw<IOException>(() => runtime.Get(existingId)).Message.ShouldBe("synthetic DTO observation failure");
            var nextId = SeedTerminal(settings); log.Records.Clear();
            (await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None)).ShouldBe(0);
            runtime.Get(nextId).ExitCode.ShouldBe(17);
            log.Records.Single(r => r.GetProperty("event").GetString() == "herdr-end").GetProperty("count").GetInt32().ShouldBe(expectedCount);
            log.Records.Last().GetProperty("event").GetString().ShouldBe("adoption-sweep-end");
            log.Records.Last().GetProperty("outcome").GetString().ShouldBe("completed");
        }
        finally { tailer.SetValue(session, originalTailer); backendField.SetValue(session, originalBackend); }
    }
    private sealed class ThrowingTailer : ITranscriptTailer
    {
        public string? BoundTranscriptPath => throw new IOException("synthetic DTO observation failure");
        public string? BindHow => null;
        public string? UnboundReason => null;
        public void Start() { }
        public void NotifyChildExited() { }
        public void NotifyClaimRevoked(string path, Guid newOwner) { }
        public RunnerTranscriptDto Snapshot() => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    internal static Guid SeedTerminal(SessionRunnerSettings settings)
    {
        var id = Guid.NewGuid();
        new PtyHostManifest { SessionId = id, HostPid = 0, HostStartTimeUtc = DateTime.UtcNow.AddMinutes(-1), PipeName = "unused-" + id.ToString("N"), ExitCode = 17, ExitReason = "ProcessExit", ExitedAtUtc = DateTime.UtcNow }
            .SaveAtomic(PtyHostManifest.PathFor(settings.PtyHostManifestDir, id));
        return id;
    }
    internal sealed class MilestoneLogger : ILogger<SessionRunnerRuntime>
    {
        public bool Throw { get; init; }
        public int Attempts { get; private set; }
        public List<JsonElement> Records { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            if (!text.StartsWith("ANTIPHON_STARTUP ")) return;
            Attempts++;
            if (Throw) throw new IOException("instrumentation sink failure");
            Records.Add(JsonDocument.Parse(text[17..]).RootElement.Clone());
        }
    }
}
