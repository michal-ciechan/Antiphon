using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// What <c>RunnerSession</c> needs from a transcript tailer, whatever the format: Claude's per-cwd
/// JSONL (<see cref="TranscriptTailer"/>, discovery + adoption rules), Grok's deterministic ACP
/// <c>updates.jsonl</c> (<see cref="GrokTranscriptTailer"/>, no discovery), or Codex's rollout
/// JSONL (<see cref="CodexTranscriptTailer"/>, discovery under the same CARD-0006 rules as Claude
/// because Codex honours no session-id flag). All publish <c>SessionTranscript</c> events on the
/// shared hub; everything downstream is format-agnostic.
/// </summary>
internal interface ITranscriptTailer : IAsyncDisposable
{
    void Start();

    /// <summary>The child process is gone — stop looking for/reading the transcript after a settle window.</summary>
    void NotifyChildExited();

    /// <summary>
    /// CARD-0181: this session's claim on <paramref name="path"/> was displaced by the file's
    /// namesake. If we were tailing it, drop it and resume discovery; if we were not yet bound,
    /// the registry already refuses the path from here on.
    /// </summary>
    void NotifyClaimRevoked(string path, Guid newOwner);

    /// <summary>Full ordered snapshot of everything parsed so far (for catch-up after a missed stream).</summary>
    RunnerTranscriptDto Snapshot();

    /// <summary>The transcript currently being tailed, or null while unbound.</summary>
    string? BoundTranscriptPath { get; }

    /// <summary>
    /// How the current bind was made (<see cref="TranscriptBindMethods"/>), or null while unbound.
    /// CARD-0180 S4 / CARD-0181: Exact vs Heuristic is derived at claim time; this is the
    /// tailer's own bind method (<c>exact</c>/<c>sidecar</c>/<c>discovery</c>/<c>fork</c>/
    /// <c>deterministic</c>).
    /// </summary>
    string? BindHow { get; }

    /// <summary>
    /// CARD-0190: why no transcript is currently bound. Null once bound and for deterministic
    /// tailers; otherwise one of awaiting-input, locating, refused, or missing.
    /// </summary>
    string? UnboundReason { get; }
}
