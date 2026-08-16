using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Launch into a directory Claude has never seen (CARD-0047's standing check interpreter got its own
/// <c>C:\logs\antiphon\check-interpreter</c>, which is the whole point of it — a private transcript
/// root). Claude opens on the trust dialog and waits.
///
/// <para>Everything downstream is blind to that. The dialog makes NO output, so the quiet-period
/// ready detector said READY; the composer then swallowed every write, delivery verification
/// correctly reported <c>NoComposerEvidence</c>, and the always-on kill restarted the session — into
/// the same directory, onto the same dialog. Seven launches, seven kills, zero checks interpreted
/// (2026-08-16). The kill was right; the "ready" was the lie.</para>
///
/// <para>These pin both halves: a trust dialog is cleared before the launch is called ready, and a
/// modal we will NOT auto-answer is never keyed into.</para>
/// </summary>
public class RunnerClaudeAdapterTrustPromptTests
{
    private const string TrustScreen = """
         Accessing workspace:

         C:\logs\antiphon\check-interpreter

         Quick safety check: Is this a project you created or one you trust? (Like your own code, a
         well-known open source project, or work from your team).

         ❯ 1. Yes, I trust this folder
           2. No, exit

         Enter to confirm · Esc to cancel
        """;

    private const string ReadyScreen = """
        ╭────────────────────────────────────────╮
        │ >                                      │
        ╰────────────────────────────────────────╯
        """;

    private const string PermissionScreen = """
         Bash command
           git push --force

         Do you want to proceed?
         ❯ 1. Yes
           2. No, and tell Claude what to do differently
        """;

    [Test]
    public async Task A_launch_into_an_untrusted_directory_answers_the_dialog_before_reporting_ready()
    {
        var client = new ScreenScriptedRunnerClient(TrustScreen, clearedBy: "1", thenShowing: ReadyScreen);
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);

        ready.ShouldBeTrue("the trust dialog is answerable, so the launch must recover, not fail");
        client.Inputs.ShouldBe(["1"], "the affirmative digit, and nothing else");
        ClaudeBlockingPromptDetector.IsBlocked(adapter.SnapshotRenderedScreen())
            .ShouldBeFalse("ready must mean the composer can actually receive the boot prompt");
    }

    [Test]
    public async Task A_healthy_launch_types_nothing_at_all()
    {
        var client = new ScreenScriptedRunnerClient(ReadyScreen, clearedBy: "1", thenShowing: ReadyScreen);
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();

        client.Inputs.ShouldBeEmpty("a session that was never blocked must not be keyed into");
    }

    [Test]
    public async Task A_permission_modal_at_launch_is_not_auto_confirmed()
    {
        var client = new ScreenScriptedRunnerClient(PermissionScreen, clearedBy: "1", thenShowing: ReadyScreen);
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.WaitForReadyAsync(CancellationToken.None);

        client.Inputs.ShouldBeEmpty(
            "keying '1' into a permission modal would approve a tool call nobody asked for");
    }

    private static RunnerClaudeAdapter NewAdapter(ISessionRunnerClient client) => new(
        client,
        Options.Create(new AgentRegistrySettings
        {
            // The real values are 5s quiet / 9s floor; the logic under test is identical at 100ms.
            ClaudeReadyQuietPeriodMs = 100,
            ClaudeReadyMaxWaitMs = 5000,
            ClaudeReadyMinTotalWaitMs = 0,
        }),
        Options.Create(new SupervisionSettings()));

    private static AgentLaunchSpec NewSpec() => new(
        DefinitionName: "claude",
        Kind: AgentKind.ClaudeCode,
        Exe: "claude.exe",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: @"C:\logs\antiphon\check-interpreter",
        Cols: 120,
        Rows: 30,
        SessionId: Guid.NewGuid());

    /// <summary>
    /// A runner client that renders a fixed screen and makes no output of its own — exactly like a
    /// TUI parked on a modal, which is what made this invisible.
    /// </summary>
    private sealed class ScreenScriptedRunnerClient(string initial, string clearedBy, string thenShowing)
        : ISessionRunnerClient
    {
        private string _screen = initial;
        private long _sequence;

        public List<string> Inputs { get; } = [];

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, 4321, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0));

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, 4321, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, _sequence));

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, "", _sequence));

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSnapshotDto(sessionId, _screen, _screen, _sequence, DateTime.UtcNow));

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            Inputs.Add(input);
            _sequence++;
            if (input == clearedBy)
                _screen = thenShowing;
            return Task.CompletedTask;
        }

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
