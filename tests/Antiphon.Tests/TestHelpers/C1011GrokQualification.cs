using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Test-local WQ-3 observations and verdicts shared by scripted and installed-CLI probes.</summary>
internal static class C1011GrokQualification
{
    internal enum Verdict
    {
        Accepted, TrustNotObserved, NotReady, VisibleTrust, StartupInputMismatch,
        PrePromptReceipt, PromptMultiplicity, PromptBodyMismatch, AssistantReplyMismatch, MissingTurnEnd,
    }

    internal static Verdict StartupVerdict(bool adapterReady, string currentScreen,
        bool trustBeforeFirstInput, IReadOnlyList<string> startupInputs, SessionRunnerTranscriptDto prePrompt)
    {
        if (!adapterReady || GrokStartupScreen.Classify(currentScreen).Reason != GrokStartupReason.Ready)
            return Verdict.NotReady;
        if (GrokTrustPromptDetector.IsVisibleOnScreen(currentScreen)) return Verdict.VisibleTrust;
        // Legacy oracle retained for the commissioned assertion-red proof before correction.
        if (!trustBeforeFirstInput) return Verdict.TrustNotObserved;
        var expectedInputs = new[] { "y" };
        if (!startupInputs.SequenceEqual(expectedInputs)) return Verdict.StartupInputMismatch;
        if (prePrompt.Entries.Any(x => x.Kind == TranscriptKinds.UserPrompt)) return Verdict.PrePromptReceipt;
        return Verdict.Accepted;
    }

    internal static Verdict TurnVerdict(SessionRunnerTranscriptDto transcript, string body, string nonce)
    {
        var prompts = transcript.Entries.Where(x => x.Kind == TranscriptKinds.UserPrompt).ToArray();
        if (prompts.Length != 1) return Verdict.PromptMultiplicity;
        if (!string.Equals(prompts[0].Text, body, StringComparison.Ordinal)) return Verdict.PromptBodyMismatch;
        var assistant = transcript.Entries.Where(x => x.Sequence > prompts[0].Sequence
            && x.Kind == TranscriptKinds.AssistantText).OrderBy(x => x.Sequence).ToArray();
        var reply = string.Concat(assistant.Select(x => x.Text)).Trim();
        if (assistant.Length == 0 || !string.Equals(reply, nonce, StringComparison.Ordinal))
            return Verdict.AssistantReplyMismatch;
        var hasLaterTurnEnd = transcript.Entries.Any(x => x.Kind == TranscriptKinds.TurnEnd
            && x.Sequence > assistant[^1].Sequence);
        return hasLaterTurnEnd ? Verdict.Accepted : Verdict.MissingTurnEnd;
    }

    internal sealed record Measurements(string Source, string Version, string BackendLine, Guid SessionId,
        string Cwd, string RequestedModel, string Body, string Nonce, object BuildProvenance,
        IReadOnlyList<string> SessionBannerVersions);

    // Project the measured arguments directly. Tests deserialize the emitted artifact independently.
    internal static string SerializeReceipt(Measurements measured, Observer observer,
        SessionRunnerTranscriptDto transcript, bool releaseConfirmed) => JsonSerializer.Serialize(new
        {
            source = measured.Source, version = measured.Version, backendLine = measured.BackendLine,
            sessionId = measured.SessionId, cwd = measured.Cwd, requestedModel = measured.RequestedModel,
            buildProvenance = measured.BuildProvenance, sessionBannerVersions = measured.SessionBannerVersions,
            cols = 120, rows = 30, observer.TrustBeforeFirstInput, observer.StartupInputs, observer.Observations,
            body = measured.Body, nonce = measured.Nonce,
            prompts = transcript.Entries.Where(x => x.Kind == TranscriptKinds.UserPrompt),
            reply = transcript.Entries.Where(x => x.Kind == TranscriptKinds.AssistantText),
            turnEnds = transcript.Entries.Where(x => x.Kind == TranscriptKinds.TurnEnd),
            transcriptModels = transcript.Entries.Where(x => !string.IsNullOrWhiteSpace(x.Model))
                .Select(x => new { x.Sequence, x.Model }),
            releaseConfirmed,
        });

    internal sealed class Observer(ISessionRunnerClient inner) : ISessionRunnerClient
    {
        public bool StartupComplete { get; set; }
        public bool TrustBeforeFirstInput { get; private set; }
        public List<string> StartupInputs { get; } = [];
        public List<object> Observations { get; } = [];
        public async Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct)
        {
            var snapshot = await inner.GetSnapshotAsync(id, ct);
            var reason = GrokStartupScreen.Classify(snapshot.RenderedScreen).Reason;
            if (!StartupComplete)
            {
                if (reason == GrokStartupReason.Trust && StartupInputs.Count == 0) TrustBeforeFirstInput = true;
                // Metadata only: suppress sign-in contents and all unrelated screen text.
                if (Observations.Count < 4096) Observations.Add(new { snapshot.LastSequence, reason = reason.ToString(), at = DateTime.UtcNow });
            }
            return snapshot;
        }
        public Task SendInputAsync(Guid id, string input, CancellationToken ct)
        {
            if (!StartupComplete) StartupInputs.Add(input);
            return inner.SendInputAsync(id, input, ct);
        }
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => inner.GetCapabilitiesAsync(ct);
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) => inner.StartAsync(id, spec, ct);
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => inner.ListAsync(ct);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => inner.GetBufferAsync(id, ct);
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => inner.GetTranscriptAsync(id, ct);
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => inner.ClearLiveBufferAsync(id, ct);
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => inner.ResizeAsync(id, cols, rows, ct);
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) => inner.KillAsync(id, ct);
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => inner.StreamEventsAsync(ct);
    }

}
