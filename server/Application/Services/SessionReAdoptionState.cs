using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Singleton per-session re-adoption accounting for this server uptime (CARD-0502 D-6/D-7).
/// Counts committed Failed-to-Running restorations, not probes or refused observations.
/// </summary>
public sealed class SessionReAdoptionState
{
    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();

    public int CountFor(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var state) ? Volatile.Read(ref state.SuccessfulReAdoptions) : 0;

    public bool IsLatched(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var state) && state.Escalation is { Reported: true };

    public async Task<ReAdoptionLease> TryReserveAsync(Guid sessionId, int cap, CancellationToken ct)
    {
        cap = Math.Max(0, cap);
        var state = _sessions.GetOrAdd(sessionId, _ => new SessionState());
        await state.Gate.WaitAsync(ct).ConfigureAwait(false);
        var held = true;
        try
        {
            if (state.Escalation is { Reported: true })
            {
                held = false;
                state.Gate.Release();
                return new ReAdoptionLease(state, allowed: false, alreadyLatched: true, escalationEligible: false, cap);
            }

            if (state.SuccessfulReAdoptions >= cap)
            {
                return new ReAdoptionLease(state, allowed: false, alreadyLatched: false, escalationEligible: true, cap);
            }

            return new ReAdoptionLease(state, allowed: true, alreadyLatched: false, escalationEligible: false, cap);
        }
        catch
        {
            if (held)
                state.Gate.Release();
            throw;
        }
    }

    internal sealed class SessionState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int SuccessfulReAdoptions;
        public PendingCapEscalation? Escalation;
    }

    public sealed class PendingCapEscalation
    {
        public required Guid IncidentId { get; init; }
        public required DateTime OriginalAt { get; init; }
        public required int Count { get; init; }
        public required int Cap { get; init; }
        public bool IncidentPersisted { get; set; }
        public bool AlertSubmitted { get; set; }
        public bool Reported => IncidentPersisted && AlertSubmitted;
    }

    public sealed class ReAdoptionLease : IAsyncDisposable
    {
        private readonly SessionState _state;
        private bool _held;
        private bool _committed;

        internal ReAdoptionLease(
            SessionState state, bool allowed, bool alreadyLatched, bool escalationEligible, int cap)
        {
            _state = state;
            _held = !alreadyLatched;
            Allowed = allowed;
            AlreadyLatched = alreadyLatched;
            EscalationEligible = escalationEligible;
            Cap = cap;
        }

        public bool Allowed { get; }
        public bool AlreadyLatched { get; }
        public bool EscalationEligible { get; }
        public int Cap { get; }
        public int SuccessfulReAdoptions => Volatile.Read(ref _state.SuccessfulReAdoptions);
        public PendingCapEscalation? Escalation => _state.Escalation;

        public void Commit()
        {
            if (!_held)
                throw new InvalidOperationException("Re-adoption lease is not held.");
            if (!Allowed)
                throw new InvalidOperationException("A refused re-adoption reservation cannot commit.");
            if (_committed)
                return;
            Interlocked.Increment(ref _state.SuccessfulReAdoptions);
            _committed = true;
        }

        public PendingCapEscalation GetOrCreateEscalation(DateTime at, Guid incidentId)
        {
            if (!_held)
                throw new InvalidOperationException("Re-adoption lease is not held.");
            _state.Escalation ??= new PendingCapEscalation
            {
                IncidentId = incidentId,
                OriginalAt = at,
                Count = SuccessfulReAdoptions,
                Cap = Cap,
            };
            return _state.Escalation;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_held)
                return;
            _held = false;
            _state.Gate.Release();
            await ValueTask.CompletedTask;
        }
    }
}
