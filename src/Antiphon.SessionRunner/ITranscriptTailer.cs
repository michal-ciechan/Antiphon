using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// What <c>RunnerSession</c> needs from a transcript tailer, whatever the format: Claude's per-cwd
/// JSONL (<see cref="TranscriptTailer"/>, discovery + adoption rules) or Grok's deterministic ACP
/// <c>updates.jsonl</c> (<see cref="GrokTranscriptTailer"/>, no discovery). Both publish
/// <c>SessionTranscript</c> events on the shared hub; everything downstream is format-agnostic.
/// </summary>
internal interface ITranscriptTailer : IAsyncDisposable
{
    void Start();

    /// <summary>The child process is gone — stop looking for/reading the transcript after a settle window.</summary>
    void NotifyChildExited();

    /// <summary>Full ordered snapshot of everything parsed so far (for catch-up after a missed stream).</summary>
    RunnerTranscriptDto Snapshot();
}
