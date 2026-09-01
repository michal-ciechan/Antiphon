using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0315: a fresh Grok <c>-Worktree</c> cwd opens on the directory-trust dialog. Quiet-period
/// ready detection calls that READY; the spilled brief is then typed into the dialog and the
/// task dies <c>StoppedBeforeFirstPrompt</c>. These pin that <c>WaitForReadyAsync</c> answers
/// <c>y</c> before reporting ready, and refuses ready when the dialog does not leave.
/// </summary>
public class RunnerGrokAdapterTrustPromptTests
{
    private const string TrustScreen = """
        Do you trust the contents of this directory?
            C:\Antiphon\worktrees\card-task-8e8e1ce3

        Grok Build may run or modify contents in this directory,
                         posing security risks.

                     Yes, proceed                 y
                     No, quit                     n
        """;

    private const string ReadyScreen = """
        C:\Antiphon\worktrees\card-task-8e8e1ce3

        >
        """;

    [Test]
    public async Task A_launch_into_an_untrusted_directory_answers_y_before_reporting_ready()
    {
        var client = new ScreenScriptedRunnerClient(TrustScreen, clearedBy: "y", thenShowing: ReadyScreen);
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);

        ready.ShouldBeTrue("the trust dialog is answerable, so the launch must recover, not fail");
        client.Inputs.ShouldBe([GrokTrustPromptDetector.AffirmativeKey]);
        GrokTrustPromptDetector.IsVisibleOnScreen(adapter.SnapshotRenderedScreen())
            .ShouldBeFalse("ready must mean the composer can actually receive the boot prompt");
    }

    [Test]
    public async Task A_healthy_launch_types_nothing()
    {
        var client = new ScreenScriptedRunnerClient(ReadyScreen, clearedBy: "y", thenShowing: ReadyScreen);
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();
        client.Inputs.ShouldBeEmpty("a session that was never blocked is never keyed into");
    }

    [Test]
    public async Task A_trust_dialog_that_does_not_clear_is_not_ready()
    {
        var client = new ScreenScriptedRunnerClient(TrustScreen, clearedBy: "never", thenShowing: ReadyScreen);
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);

        ready.ShouldBeFalse("reporting ready on a standing trust dialog is how CARD-0315 died");
        client.Inputs.ShouldBe([GrokTrustPromptDetector.AffirmativeKey]);
    }

    [Test]
    public async Task Enter_is_not_the_affirmative_key()
    {
        var client = new ScreenScriptedRunnerClient(TrustScreen, clearedBy: "\r", thenShowing: ReadyScreen);
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse(
            "both Yes and No render bold on the live screen; Enter is not safe");
        client.Inputs.ShouldNotContain("\r");
        client.Inputs.ShouldBe([GrokTrustPromptDetector.AffirmativeKey]);
    }

    private static RunnerGrokAdapter NewAdapter(ISessionRunnerClient client) => new(
        client,
        Options.Create(new AgentRegistrySettings
        {
            GrokReadyQuietPeriodMs = 50,
            GrokReadyMaxWaitMs = 2000,
            GrokReadyMinTotalWaitMs = 0,
            GrokTrustPromptSettleMs = 200,
        }),
        Options.Create(new SupervisionSettings()));

    private static AgentLaunchSpec NewSpec() => new(
        DefinitionName: "grok",
        Kind: AgentKind.Grok,
        Exe: "grok.exe",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: @"C:\Antiphon\worktrees\card-task-8e8e1ce3",
        Cols: 120,
        Rows: 30,
        SessionId: Guid.NewGuid());

    private sealed class ScreenScriptedRunnerClient(string initial, string clearedBy, string thenShowing)
        : ISessionRunnerClient
    {
        private string _screen = initial;
        private long _sequence = 1;

        public List<string> Inputs { get; } = [];

        public string Screen => _screen;

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, 4321, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, _sequence));

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, 4321, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, _sequence));

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, _screen, _sequence));

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSnapshotDto(sessionId, _screen, _screen, _sequence, DateTime.UtcNow));

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            Inputs.Add(input);
            _sequence++;
            if (input == clearedBy)
                _screen = thenShowing;
            return Task.CompletedTask;
        }

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, _sequence));

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
