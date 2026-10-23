using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>One epoch's cumulative event credits. Waiting never owns the socket writer gate.</summary>
internal sealed class PhoneHomeEventWindow
{
    private readonly object _gate = new();
    private readonly PhoneHomeEventWindowOptions _limits;
    private TaskCompletionSource _changed = Signal();
    private long _sentEvents, _sentBytes, _releasedEvents, _releasedBytes;
    private int _waiting;
    public bool Waiting => Volatile.Read(ref _waiting) != 0;

    public PhoneHomeEventWindow(PhoneHomeEventWindowOptions limits)
    {
        limits.Validate();
        _limits = limits;
    }

    public async Task ReserveAsync(int payloadBytes, CancellationToken ct)
    {
        if (payloadBytes < 0 || payloadBytes > _limits.MaxPayloadUtf8Bytes)
            throw new PhoneHomeTransportException(PhoneHomeProblemTypes.EventWindowTooSmall,
                $"Event payload UTF-8 bytes {payloadBytes} exceed window {_limits.MaxPayloadUtf8Bytes}.");
        try
        {
            while (true)
            {
                Task changed;
                lock (_gate)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_sentEvents == long.MaxValue || _sentBytes > long.MaxValue - payloadBytes)
                        throw Invalid("Event-window cumulative totals exhausted.");
                    if (_sentEvents - _releasedEvents < _limits.MaxPendingEvents
                        && _sentBytes - _releasedBytes <= _limits.MaxPayloadUtf8Bytes - payloadBytes)
                    {
                        _sentEvents++;
                        _sentBytes += payloadBytes;
                        return;
                    }
                    Volatile.Write(ref _waiting, 1);
                    changed = _changed.Task;
                }
                await changed.WaitAsync(ct);
            }
        }
        finally { Volatile.Write(ref _waiting, 0); }
    }

    public void Update(PhoneHomeEventWindowUpdate update)
    {
        TaskCompletionSource changed;
        lock (_gate)
        {
            if (update.ReleasedEvents < _releasedEvents || update.ReleasedPayloadBytes < _releasedBytes
                || update.ReleasedEvents > _sentEvents || update.ReleasedPayloadBytes > _sentBytes
                || (update.ReleasedEvents == _releasedEvents && update.ReleasedPayloadBytes != _releasedBytes))
                throw Invalid("Impossible or nonmonotonic event-window release totals.");
            if (update.ReleasedEvents == _releasedEvents && update.ReleasedPayloadBytes == _releasedBytes) return;
            _releasedEvents = update.ReleasedEvents;
            _releasedBytes = update.ReleasedPayloadBytes;
            changed = _changed;
            _changed = Signal();
        }
        changed.TrySetResult();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static PhoneHomeTransportException Invalid(string detail) => new(PhoneHomeProblemTypes.EventWindowInvalid, detail);
}
