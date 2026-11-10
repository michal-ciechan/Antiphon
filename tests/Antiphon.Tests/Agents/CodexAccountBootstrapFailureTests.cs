using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-1508. The capture names the frame, the raw tail stays bounded, and the adapter
/// keeps a sanitized outcome without putting the screen on the log line.
/// </summary>
[Category("Unit")]
public class CodexAccountBootstrapFailureTests
{
    private const string Exact =
        "Error: account/read failed during TUI bootstrap: account/read failed: workspace routing discovery timed out (code -32603)";

    [Test]
    public void Capture_names_account_bootstrap_and_keeps_the_raw_tail_bound()
    {
        var raw = new string('x', CodexStartupCaptureStore.RawTailChars + 50) + Exact;
        var frame = new CodexStartupSnapshot(Welcome(Exact), raw);
        var body = CodexStartupCaptureStore.Format(
            Guid.NewGuid(),
            frame,
            "codex-startup not-ready reason=Unknown elapsedMs=43954 mcpSeen=False",
            DateTimeOffset.Parse("2026-10-22T18:39:52.0807857Z"));

        body.ShouldContain("classifiedAs: AccountBootstrap");
        body.ShouldContain("elapsedMs=43954");
        body.ShouldContain($"({CodexStartupCaptureStore.RawTailChars} of {raw.Length} chars");
        body.ShouldNotContain(new string('x', 80));
    }

    [Test]
    public async Task Adapter_records_the_bounded_outcome_without_logging_the_screen()
    {
        var captureDir = "/tmp/c1508-codex-capture-" + Guid.NewGuid().ToString("N");
        try
        {
            var logger = new CollectingLogger();
            var client = new ScriptedCodexRunnerClient
            {
                StartupScreens = [Welcome(Exact)],
                RawOutput = Exact,
            };
            var adapter = NewAdapter(client, maxMs: 2_000, logger: logger, captureDir: captureDir);
            await adapter.StartAsync(NewSpec(), CancellationToken.None);
            (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
            client.Writes.ShouldBeEmpty();

            var seam = (IAgentProtocolAdapter)adapter;
            seam.LaunchBlock.ShouldBeNull();
            seam.StartupFailureOutcome.ShouldNotBeNull();
            seam.StartupFailureOutcome.ShouldContain("AccountBootstrap:");
            seam.StartupFailureOutcome.ShouldContain(
                "account/read failed during TUI bootstrap: account/read failed: workspace routing discovery timed out (code -32603)");
            seam.StartupFailureOutcome.ShouldContain("elapsedMs=");
            seam.StartupFailureOutcome.ShouldContain("capture=");
            seam.StartupFailureOutcome.ShouldNotContain("permissions: YOLO mode");

            var file = Directory.GetFiles(captureDir, "codex-startup-*.txt").ShouldHaveSingleItem();
            seam.StartupFailureOutcome.ShouldContain(file);
            var body = await File.ReadAllTextAsync(file);
            body.ShouldContain("classifiedAs: AccountBootstrap");
            var log = string.Join('\n', logger.Messages);
            log.ShouldContain("codex-startup not-ready");
            log.ShouldContain("reason=AccountBootstrap");
            log.ShouldContain(file);
            log.ShouldNotContain("account/read failed");
            log.ShouldNotContain("YOLO mode");
        }
        finally
        {
            try { Directory.Delete(captureDir, true); }
            catch (IOException) { }
        }
    }

    [Test]
    public async Task Sign_in_frame_does_not_become_an_account_bootstrap_outcome()
    {
        var captureDir = "/tmp/c1508-codex-signin-" + Guid.NewGuid().ToString("N");
        try
        {
            var logger = new CollectingLogger();
            var screen = "Welcome to Codex\nSign in with ChatGPT\n" + Exact;
            var client = new ScriptedCodexRunnerClient
            {
                StartupScreens = [screen],
                RawOutput = Exact,
            };
            var adapter = NewAdapter(client, maxMs: 2_000, logger: logger, captureDir: captureDir);
            await adapter.StartAsync(NewSpec(), CancellationToken.None);
            (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
            client.Writes.ShouldBeEmpty();

            var seam = (IAgentProtocolAdapter)adapter;
            seam.LaunchBlock.ShouldBeNull();
            seam.StartupFailureOutcome.ShouldBeNull();
            var log = string.Join('\n', logger.Messages);
            log.ShouldContain("reason=SignIn");
            log.ShouldNotContain("account/read failed");
            log.ShouldNotContain("Sign in with ChatGPT");
        }
        finally
        {
            try { Directory.Delete(captureDir, true); }
            catch (IOException) { }
        }
    }

    private static string Welcome(string error) =>
        "OpenAI Codex (v0.160.0)\npermissions: YOLO mode\n" + error + "\n";

    private static RunnerCodexAdapter NewAdapter(
        ScriptedCodexRunnerClient client,
        int maxMs,
        ILogger logger,
        string captureDir) =>
        new(
            client,
            Options.Create(new AgentRegistrySettings
            {
                CodexReadyQuietPeriodMs = 50,
                CodexReadyMaxWaitMs = maxMs,
                CodexBootStatusMaxWaitMs = 10_000,
                CodexStartupCaptureDirectory = captureDir,
            }),
            logger);

    private static AgentLaunchSpec NewSpec() => new(
        DefinitionName: "codex",
        Kind: AgentKind.Codex,
        Exe: "codex.exe",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: Path.GetTempPath(),
        Cols: 120,
        Rows: 30,
        SessionId: Guid.NewGuid(),
        AcceptedStartedAt: DateTime.UtcNow);

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
