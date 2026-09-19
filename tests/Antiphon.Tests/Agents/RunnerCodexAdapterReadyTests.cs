using Antiphon.Agents.Pty;
using Antiphon.Agents.Pty.Tests;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class RunnerCodexAdapterReadyTests
{
    private const string SecretSentinel = "SECRET-PROMPT-BODY-C574";

    [Test]
    [Arguments(0)]
    [Arguments(10_000)]
    [Arguments(250)]
    public async Task Legacy_boot_threshold_never_bypasses_the_positive_gate(int thresholdMs)
    {
        var client = new ScriptedCodexRunnerClient
        {
            StartupScreens = [LoadingScreen(), McpScreen()],
        };
        var adapter = NewAdapter(client, bootMs: thresholdMs, maxMs: 60_000, settleMs: 50);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var ready = adapter.WaitForReadyAsync(cts.Token);
        await WaitUntilAsync(() => client.SnapshotReads >= 1);
        await Task.Delay(30);
        ready.IsCompleted.ShouldBeFalse("R-24: ready.IsCompleted.ShouldBeFalse() while model/MCP remains blocked");
        cts.Cancel();
        try { await ready; }
        catch (OperationCanceledException) { }
    }

    [Test]
    public async Task One_snapshot_is_used_for_each_startup_decision()
    {
        var client = new ScriptedCodexRunnerClient
        {
            StartupScreens = [CodexStartupFixtures.P3, CodexStartupFixtures.P3, CodexStartupFixtures.P3],
        };
        var adapter = NewAdapter(client, settleMs: 50, maxMs: 5_000);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);
        var ready = adapter.WaitForReadyAsync(CancellationToken.None);
        await WaitUntilAsync(() => client.SnapshotReads >= 1);
        await Task.Delay(20);
        client.SnapshotReads.ShouldBe(1, "R-33: snapshotReads.ShouldBe(decisions) on an alternating-frame script");
        (await ready).ShouldBeTrue();
        client.SnapshotReads.ShouldBeLessThan(4, "R-33: a double GetSnapshot per loop would be 4+");
    }

    [Test]
    public async Task Raw_history_cannot_authorize_trust_input()
    {
        var trust = "Do you trust the contents of this directory?\nYes, continue";
        var picker = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3, "Press enter to continue");
        var client = new ScriptedCodexRunnerClient
        {
            RawOutput = trust,
            StartupScreens = [picker],
        };
        var adapter = NewAdapter(client, settleMs: 50, maxMs: 400);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        client.Writes.ShouldBeEmpty("R-34: writes.ShouldBeEmpty() when raw trust overlays a current update picker");
    }

    [Test]
    public async Task Trust_acceptance_requires_both_current_labels()
    {
        var questionOnly = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3, "Do you trust the contents of this directory?");
        var client = new ScriptedCodexRunnerClient
        {
            StartupScreens = [questionOnly],
        };
        var adapter = NewAdapter(client, settleMs: 50, maxMs: 400);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);
        await adapter.WaitForReadyAsync(CancellationToken.None);
        client.Writes.Count(w => w == "\r").ShouldBe(0, "R-35: trustEnters.ShouldBe(0) when Yes-continue is absent");
    }

    [Test]
    public async Task Repeated_trust_frames_receive_only_one_enter()
    {
        var trust = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3,
            "Do you trust the contents of this directory? Yes, continue");
        var client = new ScriptedCodexRunnerClient
        {
            StartupScreens = [trust, trust, trust, CodexStartupFixtures.P3, CodexStartupFixtures.P3],
        };
        var adapter = NewAdapter(client, settleMs: 50, maxMs: 5_000);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();
        client.Writes.Count(w => w == "\r").ShouldBe(1, "R-36: trustEnters.ShouldBe(1) across repeated current trust frames");
    }

    [Test]
    public async Task Readiness_has_no_input_except_the_known_trust_enter()
    {
        var client = new ScriptedCodexRunnerClient
        {
            StartupScreens = [CodexStartupFixtures.P3, CodexStartupFixtures.P3],
        };
        var adapter = NewAdapter(client, settleMs: 50, maxMs: 5_000);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();
        client.Writes.ShouldBeEmpty("R-38");
        client.ResizeCalls.ShouldBe(0, "R-38");
    }

    [Test]
    public async Task Delayed_mcp_after_loading_withholds_runner_ready()
    {
        var client = new ScriptedCodexRunnerClient
        {
            StartupScreens = [LoadingScreen()],
        };
        var adapter = NewAdapter(client, settleMs: 50, maxMs: 60_000);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var ready = adapter.WaitForReadyAsync(cts.Token);
        await WaitUntilAsync(() => client.SnapshotReads >= 1);
        await Task.Delay(30);
        ready.IsCompleted.ShouldBeFalse("R-39: ready.IsCompleted.ShouldBeFalse() at the loading checkpoint");
        cts.Cancel();
        try { await ready; }
        catch (OperationCanceledException) { }
    }

    [Test]
    public async Task Timeout_diagnostics_name_blocker_without_prompt_or_screen()
    {
        var logger = new CollectingLogger();
        var screen = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3, SecretSentinel + "\nStarting MCP servers (1/3): cua_repl");
        var client = new ScriptedCodexRunnerClient
        {
            StartupScreens = [screen],
            RawOutput = SecretSentinel,
        };
        var adapter = NewAdapter(client, settleMs: 50, maxMs: 200, logger: logger);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        string.Join('\n', logger.Messages).ShouldNotContain(SecretSentinel, Case.Sensitive, "R-56");
        string.Join('\n', logger.Messages).ShouldContain("codex-startup not-ready");
    }

    private static string LoadingScreen() =>
        CodexStartupFixtures.ReplaceModelValue(CodexStartupFixtures.P3, "loading");

    private static string McpScreen() =>
        CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3, "Starting MCP servers (1/3): cua_repl");

    private static RunnerCodexAdapter NewAdapter(
        ScriptedCodexRunnerClient client,
        int settleMs = 50,
        int maxMs = 5_000,
        int bootMs = 10_000,
        ILogger? logger = null) =>
        new(
            client,
            Options.Create(new AgentRegistrySettings
            {
                CodexReadyQuietPeriodMs = settleMs,
                CodexReadyMaxWaitMs = maxMs,
                CodexBootStatusMaxWaitMs = bootMs,
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

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5))
                throw new TimeoutException("condition not met");
            await Task.Yield();
        }
    }

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
