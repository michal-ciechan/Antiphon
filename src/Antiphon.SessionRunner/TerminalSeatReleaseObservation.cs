using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>Owns read serialization; observation never owns the ingestion cursor.</summary>
internal sealed class TerminalSeatReleaseObservation
{
    internal const long MaximumBytes = 8 * 1024 * 1024;
    internal SemaphoreSlim ReadGate { get; } = new(1, 1);
    internal Func<CancellationToken, Task>? BeforePoll { get; set; }
    internal Func<CancellationToken, Task>? AfterRead { get; set; }
    internal Func<string, Stream>? OpenRead { get; set; }

    internal Task<TerminalTranscriptObservation> ObserveAsync(
        Func<(string Path, string Identity)?> captureBinding,
        Func<RunnerTranscriptDto> snapshot,
        CancellationToken ct)
    {
        // Compiling read-only seam for the S1a behavioral-red checkpoint.
        var verdict = (TerminalTranscriptVerdict)TranscriptWorkingState.Classify(snapshot().Entries);
        return Task.FromResult(new TerminalTranscriptObservation(
            TerminalTranscriptReadStatus.Success, verdict));
    }
}
