using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;
using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Test-local oracles shared by the scripted fixture and the commissioned real probe.</summary>
internal static class C1011GrokQualification
{
    internal enum StartupVerdict { Accepted, NotReady, UnexpectedStartupInput }
    internal enum TurnVerdict { Accepted, PromptMultiplicity, PromptMismatch, AssistantReplyMismatch, MissingTurnEnd }
    internal sealed record TurnResult(TurnVerdict Verdict, long? PromptSequence = null,
        long[]? ReplySequences = null, long? TurnEndSequence = null);
    internal sealed record StartupObservation(long Sequence, string Reason, DateTime At);

    internal static StartupVerdict Startup(bool ready, string screen, Observer observer)
    {
        if (!ready || GrokStartupScreen.Classify(screen).Reason != GrokStartupReason.Ready
            || GrokTrustPromptDetector.IsVisibleOnScreen(screen))
            return StartupVerdict.NotReady;
        // Preparatory red phase: retain the old requirement until absence is proven red.
        string[] expectedInputs = ["y"];
        return observer.TrustBeforeFirstInput && observer.StartupInputs.SequenceEqual(expectedInputs)
            ? StartupVerdict.Accepted : StartupVerdict.UnexpectedStartupInput;
    }

    internal static TurnResult Turn(SessionRunnerTranscriptDto transcript, string body, string nonce)
    {
        var prompts = transcript.Entries.Where(x => x.Kind == TranscriptKinds.UserPrompt).ToArray();
        if (prompts.Length != 1) return new(TurnVerdict.PromptMultiplicity);
        var prompt = prompts[0];
        if (prompt.Text != body) return new(TurnVerdict.PromptMismatch, prompt.Sequence);
        var replies = transcript.Entries.Where(x => x.Kind == TranscriptKinds.AssistantText
            && x.Sequence > prompt.Sequence).OrderBy(x => x.Sequence).ToArray();
        var reply = string.Concat(replies.Select(x => x.Text)).Trim();
        var replySequences = replies.Select(x => x.Sequence).ToArray();
        if (reply != nonce || replies.Length == 0)
            return new(TurnVerdict.AssistantReplyMismatch, prompt.Sequence, replySequences);
        var end = transcript.Entries.Where(x => x.Kind == TranscriptKinds.TurnEnd
            && x.Sequence > replies[^1].Sequence).OrderBy(x => x.Sequence).FirstOrDefault();
        var hasLaterTurnEnd = end is not null;
        return hasLaterTurnEnd
            ? new(TurnVerdict.Accepted, prompt.Sequence, replySequences, end!.Sequence)
            : new(TurnVerdict.MissingTurnEnd, prompt.Sequence, replySequences);
    }

    internal sealed record ReceiptEvidence(string Source, string Version, string BackendLine,
        Guid SessionId, string Cwd, string RequestedModel, string[] TranscriptModels,
        object RunnerBuild, object PtyHostBuild, object ModernBinaryProvenance,
        bool TrustBeforeFirstInput, IReadOnlyList<string> StartupInputs,
        IReadOnlyList<StartupObservation> Observations, string Body, TurnResult Turn,
        bool ReleaseConfirmed, int Cols = 120, int Rows = 30, string? BannerVersion = null);

    internal static string Receipt(ReceiptEvidence evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence.BackendLine);
        if (!evidence.ReleaseConfirmed || evidence.Turn.Verdict != TurnVerdict.Accepted)
            throw new InvalidOperationException("An incomplete turn or release cannot produce a success receipt");
        return JsonSerializer.Serialize(new
        {
            source = evidence.Source, version = evidence.Version, backendLine = evidence.BackendLine,
            evidence.SessionId, evidence.Cwd, evidence.RequestedModel, evidence.TranscriptModels,
            evidence.RunnerBuild, evidence.PtyHostBuild, evidence.ModernBinaryProvenance,
            evidence.Cols, evidence.Rows, evidence.BannerVersion,
            evidence.TrustBeforeFirstInput, evidence.StartupInputs, evidence.Observations,
            prompt = evidence.Body, evidence.Turn.PromptSequence, evidence.Turn.ReplySequences,
            evidence.Turn.TurnEndSequence, evidence.ReleaseConfirmed,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    internal sealed class Observer(ISessionRunnerClient inner) : ISessionRunnerClient
    {
        public bool StartupComplete { get; set; }
        public bool TrustBeforeFirstInput { get; private set; }
        public List<string> StartupInputs { get; } = [];
        public List<StartupObservation> Observations { get; } = [];
        public async Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct)
        {
            var snapshot = await inner.GetSnapshotAsync(id, ct);
            var reason = GrokStartupScreen.Classify(snapshot.RenderedScreen).Reason;
            if (!StartupComplete)
            {
                if (reason == GrokStartupReason.Trust && StartupInputs.Count == 0) TrustBeforeFirstInput = true;
                // Metadata only; never retain sign-in screen contents or unrelated screen text.
                var observation = new StartupObservation(snapshot.LastSequence, reason.ToString(), DateTime.UtcNow);
                if (Observations.Count < 4096) Observations.Add(observation);
                else Observations[^1] = observation;
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
