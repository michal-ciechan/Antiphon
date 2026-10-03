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
using System.Text.Json;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class GrokFreshWorktreeQualificationTests
{
    private const string TrustScreen = """
        Do you trust the contents of this directory?
            C:\Antiphon\worktrees\c1011-scripted
        Grok Build may run or modify contents in this directory,
                         posing security risks.
                     Yes, proceed                 y
                     No, quit                     n
        """;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1011_startup_accepts_observed_trust_or_absence(bool showsTrust)
    {
        var readyScreen = GrokStartupFixture.ReadyScreen();
        var client = new ScriptedClient(showsTrust ? TrustScreen : readyScreen, readyScreen);
        var observer = new C1011GrokQualification.Observer(client);
        await using var adapter = new RunnerGrokAdapter(observer, Options.Create(new AgentRegistrySettings
        {
            GrokReadyQuietPeriodMs = 50, GrokReadyMaxWaitMs = 10000,
            GrokReadyMinTotalWaitMs = 0, GrokTrustPromptSettleMs = 3000,
        }));
        var id = Guid.NewGuid();
        await adapter.StartAsync(new AgentLaunchSpec("grok", AgentKind.Grok, "grok.exe", [],
            new Dictionary<string, string>(), @"C:\Antiphon\worktrees\c1011-scripted", 120, 30, SessionId: id), CancellationToken.None);
        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);
        var snapshot = await observer.GetSnapshotAsync(id, CancellationToken.None);
        observer.TrustBeforeFirstInput.ShouldBe(showsTrust);
        observer.StartupInputs.ShouldBe(showsTrust ? new[] { "y" } : Array.Empty<string>());
        (await observer.GetTranscriptAsync(id, CancellationToken.None)).Entries.ShouldBeEmpty();
        C1011GrokQualification.Startup(ready, snapshot.RenderedScreen, observer)
            .ShouldBe(C1011GrokQualification.StartupVerdict.Accepted, "startup-accepted");
    }

    [Test]
    public void C1011_receipt_retains_cli_and_backend()
    {
        const string capturedVersion = "Grok Build 1.0.46 (2765805b9442) [stable]";
        const string observedBackend = "Launched grok.exe (child pid 123); pty backend: ModernConPty (requested 'modern'): observed package at C:\\qualified\\conpty";
        var evidence = new C1011GrokQualification.ReceiptEvidence("measured-source", capturedVersion,
            observedBackend, Guid.NewGuid(), "unique-worktree", "grok-4.7", ["grok-4.7-build"],
            new { hash = "measured-runner" }, new { hash = "measured-host" }, new { hash = "measured-dll" },
            false, [], [], "complete prompt", new(C1011GrokQualification.TurnVerdict.Accepted, 2, [3, 4], 6), true);
        using var emitted = JsonDocument.Parse(C1011GrokQualification.Receipt(evidence));
        emitted.RootElement.GetProperty("version").GetString().ShouldBe(capturedVersion, "cli-version");
        emitted.RootElement.GetProperty("backendLine").GetString().ShouldBe(observedBackend, "backend-line");
        emitted.RootElement.GetProperty("trustBeforeFirstInput").GetBoolean().ShouldBeFalse();
        emitted.RootElement.GetProperty("startupInputs").GetArrayLength().ShouldBe(0);
    }

    [Test]
    [Arguments("complete")]
    [Arguments("missing-assistant")]
    [Arguments("missing-turn-end")]
    [Arguments("duplicate-prompt")]
    [Arguments("duplicate-reply")]
    public void C1011_paid_turn_requires_complete_receipt(string boundary)
    {
        const string nonce = "C1011_nonce";
        const string body = "Reply exactly C1011_nonce. Do not use tools or change files.";
        var id = Guid.NewGuid();
        var entries = new List<SessionRunnerTranscriptEvent>
        {
            Entry(2, TranscriptKinds.UserPrompt, body),
            Entry(3, TranscriptKinds.AssistantText, "C1011_"),
            Entry(4, TranscriptKinds.AssistantText, "nonce"),
            Entry(6, TranscriptKinds.TurnEnd, null),
        };
        if (boundary == "missing-assistant") entries.RemoveAll(x => x.Kind == TranscriptKinds.AssistantText);
        if (boundary == "missing-turn-end") entries.RemoveAll(x => x.Kind == TranscriptKinds.TurnEnd);
        if (boundary == "duplicate-prompt") entries.Add(Entry(7, TranscriptKinds.UserPrompt, body));
        if (boundary == "duplicate-reply") entries.Insert(3, Entry(5, TranscriptKinds.AssistantText, nonce));
        var actual = C1011GrokQualification.Turn(new(id, entries, entries.Max(x => x.Sequence)), body, nonce);
        var expected = boundary switch
        {
            "complete" => C1011GrokQualification.TurnVerdict.Accepted,
            "missing-turn-end" => C1011GrokQualification.TurnVerdict.MissingTurnEnd,
            "duplicate-prompt" => C1011GrokQualification.TurnVerdict.PromptMultiplicity,
            _ => C1011GrokQualification.TurnVerdict.AssistantReplyMismatch,
        };
        actual.Verdict.ShouldBe(expected, boundary == "complete" ? "turn-accepted" : "turn-rejection");
        SessionRunnerTranscriptEvent Entry(long sequence, string kind, string? text) =>
            new(id, sequence, kind, null, null, DateTimeOffset.UtcNow, null, text, null, null, null, null, null);
    }

    private sealed class ScriptedClient(string initial, string ready) : ISessionRunnerClient
    {
        private string _screen = initial;
        private long _sequence = 1;
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) => GetAsync(id, ct);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(id, 4321, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, _sequence));
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => Task.FromResult(new SessionRunnerBufferDto(id, _screen, _sequence));
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) => Task.FromResult(new SessionRunnerSnapshotDto(id, _screen, _screen, _sequence, DateTime.UtcNow));
        public Task SendInputAsync(Guid id, string input, CancellationToken ct)
        {
            _sequence++;
            if (input == "y") _screen = ready;
            return Task.CompletedTask;
        }
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => Task.FromResult(new SessionRunnerTranscriptDto(id, [], 0));
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) => Task.FromResult(new SessionRunnerSessionDto(id, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, _sequence));
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
