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
/// <para>These pin the gates <c>WaitForReadyAsync</c> runs, in order: a trust dialog is cleared
/// before the launch is called ready, a modal we will NOT auto-answer is never keyed into, and —
/// CARD-0103 — the launch finishes with a round trip through the composer that proves the TUI is
/// actually READING, which is the one thing a quiet screen cannot tell you. The scripted client
/// below models a composer: it echoes what is typed and honours Ctrl+U.</para>
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
        client.Inputs[0].ShouldBe("1", "the affirmative digit comes first, and it is the only digit sent");
        ClaudeBlockingPromptDetector.IsBlocked(adapter.SnapshotRenderedScreen())
            .ShouldBeFalse("ready must mean the composer can actually receive the boot prompt");
    }

    [Test]
    public async Task A_healthy_launch_types_nothing_but_the_input_probe()
    {
        var client = new ScreenScriptedRunnerClient(ReadyScreen, clearedBy: "1", thenShowing: ReadyScreen);
        var adapter = NewAdapter(client);
        var spec = NewSpec();
        await adapter.StartAsync(spec, CancellationToken.None);

        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();

        // A session that was never blocked is never keyed into — but it IS probed, because that is
        // the only way to learn the difference between a painted composer and a reading one. The
        // probe writes a token and takes it straight back out; nothing is submitted, ever.
        client.Inputs.ShouldBe(
            [ComposerInputProbe.TokenFor(spec.SessionId!.Value), ComposerInputProbe.KillLine]);
        client.Inputs.ShouldNotContain(i => i.Contains('\r'));
        client.Screen.Contains(ComposerInputProbe.TokenFor(spec.SessionId!.Value), StringComparison.Ordinal)
            .ShouldBeFalse("the probe leaves the composer as it found it");
    }

    [Test]
    public async Task A_permission_modal_at_launch_is_not_auto_confirmed_and_is_not_probed_either()
    {
        var client = new ScreenScriptedRunnerClient(PermissionScreen, clearedBy: "1", thenShowing: ReadyScreen);
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);

        ready.ShouldBeTrue("the lenient pass-through for an un-auto-answerable modal is unchanged");
        client.Inputs.ShouldBeEmpty(
            "keying '1' into a permission modal would approve a tool call nobody asked for — and the "
            + "input probe must not type into it either: a modal is exactly where a stray keystroke "
            + "means something (CARD-0047), so the probe is SKIPPED rather than sent into it");
    }

    // CARD-0103's live shape: the TUI is painted, quiet, unblocked and past the 9s floor, and it is
    // reading nothing. Every output-side signal says ready. The probe is the only one that says no.
    [Test]
    public async Task A_painted_but_deaf_tui_is_not_ready()
    {
        var client = new ScreenScriptedRunnerClient(ReadyScreen, clearedBy: "1", thenShowing: ReadyScreen)
        {
            EchoTypedInput = false,
        };
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);

        ready.ShouldBeFalse(
            "a composer that never renders what was typed is not ready — reporting it ready is how a "
            + "5 829-char brief was typed into a deaf pty three times and parked");
        client.Inputs.ShouldNotBeEmpty("the probe must actually have tried");
    }

    // A composer we cannot empty is not a session to append a boot prompt to: the body would arrive
    // spliced onto whatever is standing there.
    [Test]
    public async Task A_composer_that_will_not_clear_is_not_ready()
    {
        var client = new ScreenScriptedRunnerClient(ReadyScreen, clearedBy: "1", thenShowing: ReadyScreen)
        {
            HonourKillLine = false,
        };
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        client.Inputs.ShouldContain(ComposerInputProbe.KillLine, "Ctrl+U was tried before giving up");
    }

    // The kill switch, so the probe can be turned off without editing code if it ever misbehaves in
    // production. Off means readiness means exactly what it meant before CARD-0103.
    [Test]
    public async Task A_zero_probe_budget_disables_the_probe_entirely()
    {
        var client = new ScreenScriptedRunnerClient(ReadyScreen, clearedBy: "1", thenShowing: ReadyScreen)
        {
            EchoTypedInput = false,
        };
        var adapter = NewAdapter(client, probeTimeoutMs: 0);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();
        client.Inputs.ShouldBeEmpty("a disabled probe writes nothing at all");
    }

    private static RunnerClaudeAdapter NewAdapter(ISessionRunnerClient client, int probeTimeoutMs = 3000) => new(
        client,
        Options.Create(new AgentRegistrySettings
        {
            // The real values are 5s quiet / 9s floor; the logic under test is identical at 100ms.
            ClaudeReadyQuietPeriodMs = 100,
            ClaudeReadyMaxWaitMs = 5000,
            ClaudeReadyMinTotalWaitMs = 0,
            // Production is 90s/250ms/30s/10s; compressed here for the same reason as the floor.
            ClaudeInputProbeTimeoutMs = probeTimeoutMs,
            ClaudeInputProbePollIntervalMs = 25,
            ClaudeInputProbeRetypeIntervalMs = 1000,
            ClaudeInputProbeClearTimeoutMs = 500,
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
    ///
    /// <para>It also models the two composer behaviours the CARD-0103 probe depends on: typed text is
    /// echoed onto the rendered screen, and Ctrl+U takes it back off. Both are switchable, because
    /// "the composer does not echo" and "the composer will not clear" are the two ways readiness can
    /// now legitimately fail.</para>
    /// </summary>
    private sealed class ScreenScriptedRunnerClient(string initial, string clearedBy, string thenShowing)
        : ISessionRunnerClient
    {
        private string _screen = initial;
        private string _composer = string.Empty;
        private long _sequence;

        public List<string> Inputs { get; } = [];

        /// <summary>False models the CARD-0103 shape: painted, quiet, and reading nothing.</summary>
        public bool EchoTypedInput { get; init; } = true;

        public bool HonourKillLine { get; init; } = true;

        public string Screen => _screen + _composer;

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
            Task.FromResult(new SessionRunnerSnapshotDto(sessionId, Screen, Screen, _sequence, DateTime.UtcNow));

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            Inputs.Add(input);
            _sequence++;
            if (input == clearedBy)
                _screen = thenShowing;
            else if (input == ComposerInputProbe.KillLine)
            {
                if (HonourKillLine)
                    _composer = string.Empty;
            }
            else if (EchoTypedInput)
                _composer += input;
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
