using System.Diagnostics;

namespace Antiphon.Server.Application.Services;

/// <summary>Metadata-only phase evidence; diagnostics failure never changes the runtime verdict.</summary>
public sealed class RuntimePhase : IDisposable
{
    private readonly ILogger _logger;
    private readonly TimeProvider _clock;
    private readonly Guid _session;
    private readonly string _operation;
    private readonly long _started;
    private readonly Guid? _taskId;
    private readonly long? _storedSequence;
    private readonly DateTimeOffset? _producerTimestamp;
    private readonly long? _producerSequence;
    private readonly string? _transcriptUuid;
    public RuntimePhase(ILogger logger, TimeProvider clock, Guid session, string operation,
        Guid? taskId = null, long? storedSequence = null, DateTimeOffset? producerTimestamp = null,
        long? producerSequence = null, string? transcriptUuid = null)
    {
        _logger = logger; _clock = clock; _session = session; _operation = operation;
        _started = Stopwatch.GetTimestamp();
        _taskId = taskId; _storedSequence = storedSequence;
        _producerTimestamp = producerTimestamp; _producerSequence = producerSequence; _transcriptUuid = transcriptUuid;
        Write("start");
    }
    private void Write(string phase)
    {
        try
        {
            _logger.LogDebug("Runtime phase {Operation} {Phase} session={SessionId} task={TaskId} storedSequence={StoredSequence} at={ObservedAt} elapsedMs={ElapsedMs} producerTimestamp={ProducerTimestamp} producerSequence={ProducerSequence} uuid={TranscriptUuid}",
                _operation, phase, _session, _taskId, _storedSequence, _clock.GetUtcNow(), Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
                _producerTimestamp, _producerSequence, _transcriptUuid);
        }
        catch { /* Optional observations cannot suppress storage or settlement. */ }
    }
    public void Dispose() => Write("end");
    public void Completed() => Write("completed");
}
