using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-1668: every automated startup keystroke (trust answer, effort answer, probe token, Ctrl+U)
/// is a guarded write fenced on the output sequence of the snapshot taken just before it. Claude
/// Code 2.1.280 keeps repainting its footer hint during the first seconds, so one output byte can
/// land between that snapshot and the write; the runner then refuses with <c>stale-observation</c>
/// and sends nothing. The startup path used to let that refusal escape <c>WaitForReadyAsync</c>,
/// which failed the whole launch (task 62afb5fc died 18 s after dispatch on 2026-11-02, Review
/// 5c904345 the same way on 2026-10-30).
///
/// <para>The invariant: a startup keystroke is delivered at most once, and a transient stale
/// observation never fails a launch. The runner model below refuses exactly as the real one does
/// (fence on <c>ExpectedLastSequence</c>, zero bytes on refusal), and output "arrives" just before
/// a chosen write so the race is deterministic.</para>
/// </summary>
[Category("Unit")]
public class RunnerStartupStaleObservationTests
{
    // The named bound in RunnerTerminalSession (StartupStaleObservationAttempts): pinned here as its
    // default so a change to the bound is a deliberate test change.
    private const int Attempts = 3;

    private const string TrustScreen = """
        ────────────────────────────────────────────────────────────────────────────────────────────────────
         Accessing workspace:

         C:\logs\antiphon\diagnose

         Quick safety check: Is this a project you created or one you trust? (Like your own code, a well-known open source
         project, or work from your team). If not, take a moment to review what's in this folder first.

         Claude Code'll be able to read, edit, and execute files here.

         Security guide

         > No, exit
           Yes, I trust this folder

         Enter to confirm · Esc to cancel
        """;

    private const string PermissionScreen = """
         Bash command
           git push --force

         Do you want to proceed?
         ❯ 1. Yes
           2. No, and tell Claude what to do differently
        """;

    [Test]
    public async Task A_stale_observation_on_the_probe_token_and_on_ctrl_u_is_reobserved_and_sent_once()
    {
        var spec = NewSpec();
        var token = ComposerInputProbe.TokenFor(spec.SessionId!.Value);
        var client = new RacingRunnerClient();
        client.RaceBefore[token] = 1;
        client.RaceBefore[ComposerInputProbe.KillLine] = 1;
        await using var adapter = NewAdapter(client);
        await adapter.StartAsync(spec, CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);

        ready.ShouldBeTrue("a footer repaint between snapshot and write is not a reason to fail the launch");
        client.Inputs.ShouldBe([token, ComposerInputProbe.KillLine],
            "each startup keystroke reaches the composer exactly once");
        client.Attempts.ShouldBe([
            (token, ConditionalInputOutcomes.StaleObservation),
            (token, ConditionalInputOutcomes.Written),
            (ComposerInputProbe.KillLine, ConditionalInputOutcomes.StaleObservation),
            (ComposerInputProbe.KillLine, ConditionalInputOutcomes.Written),
        ]);
    }

    [Test]
    public async Task A_stale_observation_on_every_attempt_fails_after_the_bound_with_the_original_refusal()
    {
        var spec = NewSpec();
        var token = ComposerInputProbe.TokenFor(spec.SessionId!.Value);
        var client = new RacingRunnerClient();
        client.RaceBefore[token] = int.MaxValue;
        await using var adapter = NewAdapter(client);
        await adapter.StartAsync(spec, CancellationToken.None);

        var refused = await Should.ThrowAsync<ProviderDialogInputRefusedException>(
            () => adapter.WaitForReadyAsync(CancellationToken.None));

        refused.Reason.ShouldBe(ConditionalInputOutcomes.StaleObservation);
        client.Attempts.Count.ShouldBe(Attempts, "the retry is bounded, not run until the readiness deadline");
        client.Attempts.ShouldAllBe(a => a.Input == token && a.Outcome == ConditionalInputOutcomes.StaleObservation);
        client.Inputs.ShouldBeEmpty();
    }

    [Test]
    [Arguments(ConditionalInputOutcomes.ModalBlocked)]
    [Arguments(ConditionalInputOutcomes.ObservationUnknown)]
    [Arguments(ConditionalInputOutcomes.GenerationMismatch)]
    public async Task A_refusal_other_than_stale_observation_is_not_retried(string reason)
    {
        var spec = NewSpec();
        var token = ComposerInputProbe.TokenFor(spec.SessionId!.Value);
        var client = new RacingRunnerClient();
        client.RefuseOnce[token] = reason;
        await using var adapter = NewAdapter(client);
        await adapter.StartAsync(spec, CancellationToken.None);

        var refused = await Should.ThrowAsync<ProviderDialogInputRefusedException>(
            () => adapter.WaitForReadyAsync(CancellationToken.None));

        refused.Reason.ShouldBe(reason);
        client.Attempts.ShouldBe([(token, reason)]);
        client.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task An_uncertain_transport_result_is_not_retried()
    {
        var spec = NewSpec();
        var token = ComposerInputProbe.TokenFor(spec.SessionId!.Value);
        var client = new RacingRunnerClient();
        client.RefuseOnce[token] = ConditionalInputOutcomes.Unknown;
        await using var adapter = NewAdapter(client);
        await adapter.StartAsync(spec, CancellationToken.None);

        await Should.ThrowAsync<IOException>(() => adapter.WaitForReadyAsync(CancellationToken.None));

        client.Attempts.ShouldBe([(token, ConditionalInputOutcomes.Unknown)],
            "an unknown outcome may have been delivered; re-sending could type the keystroke twice");
    }

    [Test]
    public async Task A_modal_that_appears_during_the_race_is_observed_and_never_typed_into()
    {
        var spec = NewSpec();
        var token = ComposerInputProbe.TokenFor(spec.SessionId!.Value);
        var client = new RacingRunnerClient();
        client.RaceBefore[token] = 1;
        client.OnRace = () => client.FixedScreen = PermissionScreen;
        await using var adapter = NewAdapter(client);
        await adapter.StartAsync(spec, CancellationToken.None);

        var refused = await Should.ThrowAsync<ProviderDialogInputRefusedException>(
            () => adapter.WaitForReadyAsync(CancellationToken.None));

        refused.Reason.ShouldBe(ConditionalInputOutcomes.ModalBlocked,
            "the retry re-observes the screen and must see the modal before it re-sends");
        client.Attempts.ShouldBe([(token, ConditionalInputOutcomes.StaleObservation)]);
        client.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task A_dialog_key_is_not_resent_once_the_dialog_it_answered_has_gone()
    {
        var spec = NewSpec();
        var client = new RacingRunnerClient { FixedScreen = TrustScreen };
        client.RaceBefore["j"] = 1;
        client.OnRace = () => client.FixedScreen = null;
        var session = new RunnerTerminalSession(client);
        await session.StartAsync(spec, CancellationToken.None);
        try
        {
            var refused = await Should.ThrowAsync<ProviderDialogInputRefusedException>(
                () => session.WriteStartupAsync("j", CancellationToken.None));

            refused.Reason.ShouldBe(ConditionalInputOutcomes.StaleObservation);
            client.Attempts.ShouldBe([("j", ConditionalInputOutcomes.StaleObservation)]);
            client.Inputs.ShouldBeEmpty("a trust key re-sent over a composer would be typed as text");
        }
        finally
        {
            session.StopWatching();
        }
    }

    [Test]
    public async Task The_retry_stays_inside_the_callers_deadline()
    {
        var spec = NewSpec();
        using var deadline = new CancellationTokenSource();
        var client = new RacingRunnerClient();
        client.RaceBefore["x"] = int.MaxValue;
        client.OnRace = deadline.Cancel;
        var session = new RunnerTerminalSession(client);
        await session.StartAsync(spec, CancellationToken.None);
        try
        {
            Exception? thrown = null;
            try { await session.WriteStartupAsync("x", deadline.Token); }
            catch (Exception ex) { thrown = ex; }

            thrown.ShouldBeAssignableTo<OperationCanceledException>();
            client.Attempts.ShouldBe([("x", ConditionalInputOutcomes.StaleObservation)],
                "the readiness deadline expired during the first refusal; no further attempt is made");
        }
        finally
        {
            session.StopWatching();
        }
    }

    // Windows rc 6591e378: the second PtyHost Claude launch failed in 4.7s with
    // observation-unknown. FakeClaude prints "Fake Claude ready" before the CSI composer
    // paint. A quiet period can close on that banner, and the probe's first write then
    // refuses before the composer frame arrives. Nothing may be typed while the frame
    // is unclassified; the caller's existing budget is the bound.
    [Test]
    public async Task A_pre_composer_banner_is_reread_inside_the_caller_budget_before_any_keystroke()
    {
        var spec = NewSpec();
        var client = new RacingRunnerClient();
        client.ScreenForRead = n => n == 1 ? "Fake Claude ready" : null;
        var session = new RunnerTerminalSession(client);
        await session.StartAsync(spec, CancellationToken.None);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await session.WriteStartupAsync("zz", budget.Token);
            client.Inputs.ShouldBe(["zz"]);
            client.Attempts.ShouldBe([("zz", ConditionalInputOutcomes.Written)]);
            client.SnapshotReads.ShouldBeGreaterThanOrEqualTo(2);
        }
        finally
        {
            session.StopWatching();
        }
    }

    [Test]
    public async Task A_banner_that_never_becomes_a_composer_waits_out_the_caller_and_writes_nothing()
    {
        var spec = NewSpec();
        var client = new RacingRunnerClient();
        using var deadline = new CancellationTokenSource();
        client.ScreenForRead = n =>
        {
            if (n >= 2) deadline.Cancel();
            return "Fake Claude ready";
        };
        var session = new RunnerTerminalSession(client);
        await session.StartAsync(spec, CancellationToken.None);
        try
        {
            var thrown = await Should.ThrowAsync<OperationCanceledException>(
                () => session.WriteStartupAsync("zz", deadline.Token));
            thrown.ShouldBeAssignableTo<OperationCanceledException>();
            client.Inputs.ShouldBeEmpty();
            client.Attempts.ShouldBeEmpty();
            client.SnapshotReads.ShouldBeGreaterThanOrEqualTo(2);
        }
        finally
        {
            session.StopWatching();
        }
    }

    [Test]
    public async Task A_dialog_that_appears_while_the_banner_is_unclassified_is_not_typed_into()
    {
        var spec = NewSpec();
        var client = new RacingRunnerClient();
        client.ScreenForRead = n => n == 1 ? "Fake Claude ready" : PermissionScreen;
        var session = new RunnerTerminalSession(client);
        await session.StartAsync(spec, CancellationToken.None);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var refused = await Should.ThrowAsync<ProviderDialogInputRefusedException>(
                () => session.WriteStartupAsync("zz", budget.Token));
            refused.Reason.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
            client.Inputs.ShouldBeEmpty();
            client.Attempts.ShouldBeEmpty();
        }
        finally
        {
            session.StopWatching();
        }
    }

    [Test]
    public async Task A_dialog_key_is_not_typed_into_the_composer_that_replaces_an_unclassified_banner()
    {
        var spec = NewSpec();
        var client = new RacingRunnerClient();
        client.ScreenForRead = n => n == 1 ? "Fake Claude ready" : null;
        var session = new RunnerTerminalSession(client);
        await session.StartAsync(spec, CancellationToken.None);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var refused = await Should.ThrowAsync<ProviderDialogInputRefusedException>(
                () => session.WriteStartupAsync("j", budget.Token));
            refused.Reason.ShouldBe(ConditionalInputOutcomes.StaleObservation);
            client.Inputs.ShouldBeEmpty();
            client.Attempts.ShouldBeEmpty();
        }
        finally
        {
            session.StopWatching();
        }
    }

    [Test]
    public async Task A_blocked_permission_dialog_is_refused_before_any_wait()
    {
        var spec = NewSpec();
        var client = new RacingRunnerClient { FixedScreen = PermissionScreen };
        var session = new RunnerTerminalSession(client);
        await session.StartAsync(spec, CancellationToken.None);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var refused = await Should.ThrowAsync<ProviderDialogInputRefusedException>(
                () => session.WriteStartupAsync("zz", budget.Token));
            refused.Reason.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
            client.Inputs.ShouldBeEmpty();
            client.Attempts.ShouldBeEmpty();
            client.SnapshotReads.ShouldBe(1);
        }
        finally
        {
            session.StopWatching();
        }
    }

    [Test]
    public async Task The_fakeclaude_startup_paint_is_a_clear_composer_and_a_mangled_rule_stays_unknown()
    {
        ClaudeDialogObservation.Observe(Render(FakeClaudeStartupBytes(carriageReturn: true))).State
            .ShouldBe(ClaudeDialogState.Clear);
        ClaudeDialogObservation.Observe(Render("Fake Claude ready\r\n")).State
            .ShouldBe(ClaudeDialogState.Unknown);
        ClaudeDialogObservation.Observe(PermissionScreen).State
            .ShouldBe(ClaudeDialogState.Blocked);
        var oemRules = FakeClaudeStartupBytes(carriageReturn: true).Replace("\u2500", "\u00C4", StringComparison.Ordinal);
        ClaudeDialogObservation.Observe(Render(oemRules)).State
            .ShouldBe(ClaudeDialogState.Unknown);
        ClaudeDialogObservation.Observe(Render(FakeClaudeStartupBytes(carriageReturn: false))).State
            .ShouldBe(ClaudeDialogState.Unknown);
    }

    private static string Render(string bytes)
    {
        var screen = new TerminalScreen(120, 30);
        screen.Feed(bytes);
        return screen.GetScreenText();
    }

    /// <summary>The bytes <c>ANTIPHON_FAKE_CLAUDE_COMPOSER=1</c> writes before its read loop.</summary>
    private static string FakeClaudeStartupBytes(bool carriageReturn)
    {
        var nl = carriageReturn ? "\r\n" : "\n";
        var rule = new string('\u2500', 60);
        return "Fake Claude ready" + nl
            + "\x1b]0;\u2733\x07"
            + "\x1b[2J\x1b[H"
            + rule + nl
            + "\u276f " + nl
            + rule + nl
            + "  \u23F5\u23F5 bypass permissions on (shift+tab to cycle)" + nl;
    }

    private static RunnerClaudeAdapter NewAdapter(ISessionRunnerClient client) => new(
        client,
        Options.Create(new AgentRegistrySettings
        {
            ClaudeReadyQuietPeriodMs = 100,
            ClaudeReadyMaxWaitMs = 5000,
            ClaudeReadyMinTotalWaitMs = 0,
            ClaudeInputProbeTimeoutMs = 3000,
            ClaudeInputProbePollIntervalMs = 25,
            ClaudeInputProbeRetypeIntervalMs = 1000,
            ClaudeInputProbeClearTimeoutMs = 500,
        }),
        Options.Create(new SupervisionSettings()));

    private static AgentLaunchSpec NewSpec() => new(
        DefinitionName: "claude",
        Kind: AgentKind.ClaudeCode,
        Exe: "claude",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: "/work/card-1668",
        Cols: 120,
        Rows: 30,
        SessionId: Guid.NewGuid(), AcceptedStartedAt: DateTime.UnixEpoch);

    /// <summary>
    /// A composer (echo + Ctrl+U) behind the runner's real conditional-input fence: a write whose
    /// <c>ExpectedLastSequence</c> is not the current sequence is refused with zero bytes, and a
    /// clear-required write over a blocked screen is refused as modal-blocked.
    /// <see cref="RaceBefore"/> makes output arrive just before the next N writes of an input.
    /// </summary>
    private sealed class RacingRunnerClient : ISessionRunnerClient
    {
        private readonly ScriptedClaudeInputGuard _guard = new();
        private readonly DateTime _startedAt = DateTime.UtcNow;
        private string _composer = string.Empty;
        private long _sequence = 1;

        public Dictionary<string, int> RaceBefore { get; } = [];
        public Dictionary<string, string> RefuseOnce { get; } = [];
        public Action? OnRace { get; set; }

        /// <summary>A modal in front of the composer; null shows the composer.</summary>
        public string? FixedScreen { get; set; }

        /// <summary>
        /// One-based snapshot read. Return a screen to show that frame, or null for
        /// <see cref="FixedScreen"/> or the composer.
        /// </summary>
        public Func<int, string?>? ScreenForRead { get; set; }

        public int SnapshotReads { get; private set; }
        public List<string> Inputs { get; } = [];
        public List<(string Input, string Outcome)> Attempts { get; } = [];

        private string Screen => FixedScreen ?? ScriptedClaudeInputGuard.Composer(_composer);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
        { _guard.Bind(spec.AcceptedStartedAt); return GetAsync(sessionId, ct); }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, 4321, _startedAt, "Running", null, AgentExitReason.Unknown, _sequence,
                AcceptedStartedAt: _guard.AcceptedStartedAt));

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, "", _sequence));

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
        {
            SnapshotReads++;
            var screen = ScreenForRead?.Invoke(SnapshotReads) ?? Screen;
            return Task.FromResult(new SessionRunnerSnapshotDto(sessionId, screen, screen, _sequence, _startedAt,
                _guard.AcceptedStartedAt, _guard.Observe(sessionId, _sequence, screen)));
        }

        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId,
            RunnerConditionalInputRequest request, CancellationToken ct)
        {
            var input = request.Input;
            if (RefuseOnce.Remove(input, out var reason))
                return Task.FromResult(Record(sessionId, input, reason));

            if (RaceBefore.TryGetValue(input, out var races) && races > 0)
            {
                RaceBefore[input] = races == int.MaxValue ? races : races - 1;
                _sequence++; // the TUI repaints its footer hint between snapshot and write
                OnRace?.Invoke();
            }

            if (request.ExpectedLastSequence != _sequence)
                return Task.FromResult(Record(sessionId, input, ConditionalInputOutcomes.StaleObservation));
            if (request.DialogIntent == ClaudeDialogInputIntent.None
                && _guard.Observe(sessionId, _sequence, Screen).State != ProviderDialogState.Clear)
                return Task.FromResult(Record(sessionId, input, ConditionalInputOutcomes.ModalBlocked));

            Inputs.Add(input);
            if (FixedScreen is null)
                _composer = input == ComposerInputProbe.KillLine ? string.Empty : _composer + input;
            _sequence++;
            return Task.FromResult(Record(sessionId, input, ConditionalInputOutcomes.Written));
        }

        private RunnerConditionalInputResult Record(Guid sessionId, string input, string outcome)
        {
            Attempts.Add((input, outcome));
            return new(sessionId, outcome, _guard.AcceptedStartedAt, _sequence);
        }

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new InvalidOperationException("startup keystrokes must go through the guarded write");

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
