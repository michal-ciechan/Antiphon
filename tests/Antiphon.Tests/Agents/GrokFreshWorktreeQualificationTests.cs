using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class GrokFreshWorktreeQualificationTests
{
    private const string TrustScreen = """
        Do you trust the contents of this directory?
            C:\Antiphon\worktrees\card-task-8e8e1ce3

        Grok Build may run or modify contents in this directory,
                         posing security risks.

                     Yes, proceed                 y
                     No, quit                     n
        """;


    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1011_startup_accepts_observed_trust_or_absence(bool trustAppears)
    {
        var client = new ScreenScriptedRunnerClient(trustAppears ? TrustScreen : GrokStartupFixture.ReadyScreen(),
            clearedBy: "y", thenShowing: GrokStartupFixture.ReadyScreen());
        var observer = new C1011GrokQualification.Observer(client);
        await using var adapter = new RunnerGrokAdapter(observer, Options.Create(new AgentRegistrySettings
        {
            GrokReadyQuietPeriodMs = 50, GrokReadyMaxWaitMs = 10000,
            GrokReadyMinTotalWaitMs = 0, GrokTrustPromptSettleMs = 3000,
        }));
        var id = Guid.NewGuid();
        await adapter.StartAsync(new AgentLaunchSpec("grok", AgentKind.Grok, "grok.exe", [],
            new Dictionary<string, string>(), ".", 120, 30, SessionId: id), CancellationToken.None);
        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);
        ready.ShouldBeTrue();
        var screen = await observer.GetSnapshotAsync(id, CancellationToken.None);
        var prePrompt = await observer.GetTranscriptAsync(id, CancellationToken.None);
        observer.TrustBeforeFirstInput.ShouldBe(trustAppears);
        var expected = trustAppears ? new[] { "y" } : Array.Empty<string>();
        observer.StartupInputs.ShouldBe(expected);
        client.Inputs.ShouldBe(expected);
        prePrompt.Entries.ShouldNotContain(x => x.Kind == TranscriptKinds.UserPrompt);
        C1011GrokQualification.StartupVerdict(ready, screen.RenderedScreen,
            observer.TrustBeforeFirstInput, observer.StartupInputs, prePrompt)
            .ShouldBe(C1011GrokQualification.Verdict.Accepted, "startup-accepted");
    }

    [Test]
    public void C1011_receipt_retains_cli_and_backend()
    {
        const string measuredVersion = "grok 1.0.46 (2765805b9442) [stable]";
        const string measuredBackend = "2026-10-03T20:01:00Z pty backend: ModernConPty (requested 'modern')";
        var id = Guid.NewGuid();
        var transcript = CompleteTurn(id, "complete");
        var measured = new C1011GrokQualification.Measurements(new string('a', 40), measuredVersion,
            measuredBackend, id, "task-owned-fresh-worktree", "grok-4.7", "Reply exactly C1011-NONCE. Do not use tools or change files.",
            "C1011-NONCE", new { runnerBuild = "fixture-runner", hostBuild = "fixture-host" }, []);
        var observer = new C1011GrokQualification.Observer(new ScreenScriptedRunnerClient(
            GrokStartupFixture.ReadyScreen(), "y", GrokStartupFixture.ReadyScreen()));
        using var emitted = JsonDocument.Parse(C1011GrokQualification.SerializeReceipt(measured, observer, transcript, true));
        emitted.RootElement.GetProperty("version").GetString().ShouldBe(measuredVersion, "cli-version");
        emitted.RootElement.GetProperty("backendLine").GetString().ShouldBe(measuredBackend, "backend-line");
        emitted.RootElement.GetProperty("TrustBeforeFirstInput").GetBoolean().ShouldBeFalse();
        emitted.RootElement.GetProperty("StartupInputs").GetArrayLength().ShouldBe(0);
        emitted.RootElement.GetProperty("releaseConfirmed").GetBoolean().ShouldBeTrue();
    }

    [Test]
    [Arguments("complete")]
    [Arguments("missing-assistant")]
    [Arguments("missing-turn-end")]
    [Arguments("duplicate-prompt")]
    [Arguments("duplicate-reply")]
    public void C1011_paid_turn_requires_complete_receipt(string variant)
    {
        const string body = "Reply exactly C1011-NONCE. Do not use tools or change files.";
        var expected = variant switch
        {
            "complete" => C1011GrokQualification.Verdict.Accepted,
            "missing-turn-end" => C1011GrokQualification.Verdict.MissingTurnEnd,
            "duplicate-prompt" => C1011GrokQualification.Verdict.PromptMultiplicity,
            _ => C1011GrokQualification.Verdict.AssistantReplyMismatch,
        };
        C1011GrokQualification.TurnVerdict(CompleteTurn(Guid.NewGuid(), variant), body, "C1011-NONCE")
            .ShouldBe(expected, "turn-rejection");
    }

    private static SessionRunnerTranscriptDto CompleteTurn(Guid id, string variant)
    {
        var entries = new List<SessionRunnerTranscriptEvent>
        {
            Entry(id, 1, TranscriptKinds.UserPrompt, "Reply exactly C1011-NONCE. Do not use tools or change files."),
        };
        if (variant != "missing-assistant")
        {
            entries.Add(Entry(id, 2, TranscriptKinds.AssistantText, "C1011-"));
            entries.Add(Entry(id, 3, TranscriptKinds.AssistantText, "NONCE"));
        }
        if (variant == "duplicate-reply") entries.Add(Entry(id, 4, TranscriptKinds.AssistantText, "C1011-NONCE"));
        if (variant != "missing-turn-end") entries.Add(Entry(id, 5, TranscriptKinds.TurnEnd, null));
        if (variant == "duplicate-prompt") entries.Add(Entry(id, 6, TranscriptKinds.UserPrompt, entries[0].Text));
        return new SessionRunnerTranscriptDto(id, entries, entries[^1].Sequence);
    }

    private static SessionRunnerTranscriptEvent Entry(Guid id, long sequence, string kind, string? text) =>
        new(id, sequence, kind, null, null, DateTimeOffset.UtcNow, null, text, null, null, null, null, null,
            Model: kind == TranscriptKinds.TurnEnd ? "grok-4.7-build" : null);

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
