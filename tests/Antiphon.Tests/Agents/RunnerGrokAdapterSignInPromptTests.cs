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
/// CARD-0324: a Grok launch onto the sign-in screen must fail ready, type nothing, and name
/// the store plus <c>grok login</c>. Sign-in that also contains trust text still must not
/// send <c>y</c>. Trust-only is unchanged from CARD-0315.
/// </summary>
public class RunnerGrokAdapterSignInPromptTests
{
    private const string SignInScreen = """
                                         Approve in your browser to finish signing in.
                                                           FYED-XF4N
                                            Make sure your browser shows this code.
                                                    Waiting for approval...
                                                         ctrl+q  quit
        """;

    private const string SignInPlusTrustScreen = """
        Approve in your browser to finish signing in.
        Waiting for approval...
        Do you trust the contents of this directory?
                     Yes, proceed                 y
                     No, quit                     n
        """;

    private const string TrustScreen = """
        Do you trust the contents of this directory?
            C:\Antiphon\worktrees\card-task-8e8e1ce3

                     Yes, proceed                 y
                     No, quit                     n
        """;

    private const string ReadyScreen = """
        C:\Antiphon\worktrees\card-task-8e8e1ce3

        >
        """;

    [Test]
    public async Task A_sign_in_screen_is_not_ready_and_types_nothing()
    {
        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        var client = new ScreenScriptedRunnerClient(SignInScreen, clearedBy: "y", thenShowing: ReadyScreen);
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(grokHome), CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);

        ready.ShouldBeFalse("the sign-in screen has no composer; ready would type the brief into it");
        client.Inputs.ShouldBeEmpty("CARD-0324: type nothing into the sign-in screen");
        adapter.LaunchBlock.ShouldNotBeNull();
        adapter.LaunchBlock!.Kind.ShouldBe(AgentLaunchBlockKind.ProviderSignInRequired);
        adapter.LaunchBlock.Reason.ShouldContain(Path.Combine(grokHome, "auth.json"));
        adapter.LaunchBlock.Reason.ShouldContain("grok login");
        adapter.LaunchBlock.GrokHome.ShouldBe(grokHome);
    }

    [Test]
    public async Task A_sign_in_screen_that_also_contains_trust_text_still_types_nothing()
    {
        var client = new ScreenScriptedRunnerClient(
            SignInPlusTrustScreen, clearedBy: "y", thenShowing: ReadyScreen);
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);

        ready.ShouldBeFalse();
        client.Inputs.ShouldBeEmpty("sign-in gates trust; y must never be sent");
        adapter.LaunchBlock!.Kind.ShouldBe(AgentLaunchBlockKind.ProviderSignInRequired);
    }

    [Test]
    public async Task A_trust_only_screen_is_unchanged_from_CARD_0315()
    {
        var client = new ScreenScriptedRunnerClient(TrustScreen, clearedBy: "y", thenShowing: ReadyScreen);
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);

        ready.ShouldBeTrue();
        client.Inputs.ShouldBe([GrokTrustPromptDetector.AffirmativeKey]);
        adapter.LaunchBlock.ShouldBeNull();
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

    private static AgentLaunchSpec NewSpec(string? grokHome = null)
    {
        var env = new Dictionary<string, string>();
        if (grokHome is not null)
            env["GROK_HOME"] = grokHome;
        return new(
            DefinitionName: "grok",
            Kind: AgentKind.Grok,
            Exe: "grok.exe",
            Args: [],
            Env: env,
            Cwd: @"C:\Antiphon\worktrees\card-task-8e8e1ce3",
            Cols: 120,
            Rows: 30,
            SessionId: Guid.NewGuid());
    }

    private sealed class ScreenScriptedRunnerClient(string initial, string clearedBy, string thenShowing)
        : ISessionRunnerClient
    {
        private string _screen = initial;
        private long _sequence = 1;

        public List<string> Inputs { get; } = [];

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
