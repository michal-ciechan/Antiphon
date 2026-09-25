using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>The answer to one acquire: a grant, or why not yet.</summary>
public abstract record BuildSlotOutcome
{
    public sealed record Granted(BuildSlotGrant Grant) : BuildSlotOutcome;
    public sealed record Busy(BuildSlotBusy Refusal) : BuildSlotOutcome;
    public sealed record MemoryFloor(BuildSlotMemoryFloor Refusal) : BuildSlotOutcome;
    public sealed record Invalid(string Reason) : BuildSlotOutcome;
}

/// <summary>
/// CARD-0589 D-3: the host's build/test driver budget. Leases and a FIFO waiter queue live in
/// memory under one lock. A grant goes only to a waiter within the free-slot window at the head of
/// the queue, so a long waiter is never overtaken; a waiter that stops re-polling is dropped. The
/// sweep (timer and every acquire) reaps a lease whose holder pid is gone or recycled, or that is
/// past its TTL. The memory floor is checked at grant time only; a held lease is never revoked and
/// nothing is killed.
/// </summary>
public sealed class BuildSlotBroker
{
    private readonly BuildSlotSettings _settings;
    private readonly IProcessLivenessProbe _liveness;
    private readonly IHostMemoryProbe _memory;
    private readonly TimeProvider _time;
    private readonly ILogger<BuildSlotBroker> _logger;
    private readonly object _gate = new();
    private readonly List<Lease> _leases = [];
    private readonly List<Waiter> _waiters = [];

    public BuildSlotBroker(
        IOptions<BuildSlotSettings> settings,
        IProcessLivenessProbe liveness,
        IHostMemoryProbe memory,
        TimeProvider time,
        ILogger<BuildSlotBroker> logger)
    {
        _settings = settings.Value;
        _liveness = liveness;
        _memory = memory;
        _time = time;
        _logger = logger;
    }

    /// <summary>True only for a grant; <paramref name="outcome"/> says what to tell the wrapper.</summary>
    public bool TryAcquire(BuildSlotRequest request, out BuildSlotOutcome outcome)
    {
        if (request.Pid <= 0)
        {
            outcome = new BuildSlotOutcome.Invalid("pid must be positive");
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.Label))
        {
            outcome = new BuildSlotOutcome.Invalid("label must not be blank");
            return false;
        }
        if (!_settings.Enabled)
        {
            outcome = new BuildSlotOutcome.Granted(new BuildSlotGrant(null, _settings.MaxCpuCount, 0, _settings.MaxConcurrent, null, Unlimited: true));
            return true;
        }

        var start = request.ProcessStartUtc?.ToUniversalTime() ?? _liveness.TryGetStartTimeUtc(request.Pid);
        if (start is null)
        {
            outcome = new BuildSlotOutcome.Invalid($"holder pid {request.Pid} is not running and no processStartUtc was given");
            return false;
        }
        var label = request.Label.Trim();
        lock (_gate)
        {
            var now = UtcNow;
            SweepLocked(now);

            // A holder that asks again (a lost response, a retried call) gets its own lease back.
            var held = _leases.FirstOrDefault(l => l.Pid == request.Pid && l.ProcessStartUtc == start.Value);
            if (held is not null)
            {
                outcome = new BuildSlotOutcome.Granted(GrantOf(held));
                return true;
            }

            var waiter = _waiters.FirstOrDefault(w => w.Pid == request.Pid && w.ProcessStartUtc == start.Value);
            if (waiter is null)
            {
                waiter = new Waiter(request.Pid, start.Value, label, now);
                _waiters.Add(waiter);
            }
            waiter.LastSeenUtc = now;
            var position = _waiters.IndexOf(waiter) + 1;
            var free = _settings.MaxConcurrent - _leases.Count;
            if (position > free)
            {
                outcome = new BuildSlotOutcome.Busy(new BuildSlotBusy(_leases.Count, _settings.MaxConcurrent, position, _settings.RetryAfterMs));
                return false;
            }

            if (_memory.AvailableBytes is { } available && _settings.MinAvailableMemoryMb > 0
                && available / (1024 * 1024) < _settings.MinAvailableMemoryMb)
            {
                outcome = new BuildSlotOutcome.MemoryFloor(new BuildSlotMemoryFloor(
                    available / (1024 * 1024), _settings.MinAvailableMemoryMb, position, _settings.RetryAfterMs));
                return false;
            }

            _waiters.Remove(waiter);
            var lease = new Lease(Guid.NewGuid(), request.Pid, start.Value, label, request.SessionId, request.TaskId,
                now, now.AddMinutes(_settings.LeaseTtlMinutes));
            _leases.Add(lease);
            _logger.LogInformation(
                "Build slot lease {LeaseId} granted to {Label} (pid {Pid}, session {SessionId}, task {TaskId}) after {WaitedSeconds:0}s; {Occupied}/{Budget} occupied",
                lease.Id, label, request.Pid, request.SessionId, request.TaskId, (now - waiter.SinceUtc).TotalSeconds,
                _leases.Count, _settings.MaxConcurrent);
            outcome = new BuildSlotOutcome.Granted(GrantOf(lease));
            return true;
        }
    }

    /// <summary>False when the lease is unknown (already released, reaped, or never granted).</summary>
    public bool Release(Guid leaseId)
    {
        lock (_gate)
        {
            var lease = _leases.FirstOrDefault(l => l.Id == leaseId);
            if (lease is null)
                return false;
            _leases.Remove(lease);
            _logger.LogInformation("Build slot lease {LeaseId} released by {Label} (pid {Pid}) after {HeldSeconds:0}s",
                lease.Id, lease.Label, lease.Pid, (UtcNow - lease.GrantedAtUtc).TotalSeconds);
            return true;
        }
    }

    /// <summary>Reaps dead, recycled and expired leases and drops silent waiters; returns the leases reaped.</summary>
    public int Sweep()
    {
        lock (_gate)
            return SweepLocked(UtcNow);
    }

    public BuildSlotListing List()
    {
        lock (_gate)
        {
            var available = _memory.AvailableBytes;
            return new BuildSlotListing(
                _settings.Enabled,
                _settings.MaxConcurrent,
                _settings.MaxCpuCount,
                _leases.Count,
                new BuildSlotMemory(available is { } bytes ? bytes / (1024 * 1024) : null, _settings.MinAvailableMemoryMb),
                _leases.Select(l => new BuildSlotLease(l.Id, l.Pid, l.Label, l.SessionId, l.TaskId, l.GrantedAtUtc,
                    l.ExpiresAtUtc, _liveness.IsAlive(l.Pid, l.ProcessStartUtc))).ToList(),
                _waiters.Select((w, i) => new BuildSlotWaiter(w.Pid, w.Label, w.SinceUtc, i + 1)).ToList());
        }
    }

    private int SweepLocked(DateTime now)
    {
        var reaped = 0;
        foreach (var lease in _leases.ToList())
        {
            string? reason = null;
            if (now >= lease.ExpiresAtUtc)
                reason = $"ttl {_settings.LeaseTtlMinutes}m expired";
            else if (!_liveness.IsAlive(lease.Pid, lease.ProcessStartUtc))
                reason = "holder pid exited or was recycled";
            if (reason is null)
                continue;
            _leases.Remove(lease);
            reaped++;
            _logger.LogWarning("Build slot lease {LeaseId} held by {Label} (pid {Pid}) reaped: {Reason}; held {HeldSeconds:0}s",
                lease.Id, lease.Label, lease.Pid, reason, (now - lease.GrantedAtUtc).TotalSeconds);
        }

        var silence = TimeSpan.FromMilliseconds(_settings.WaiterSilenceMs);
        foreach (var waiter in _waiters.Where(w => now - w.LastSeenUtc > silence).ToList())
        {
            _waiters.Remove(waiter);
            _logger.LogInformation("Build slot waiter {Label} (pid {Pid}) dropped: silent for {SilentSeconds:0}s",
                waiter.Label, waiter.Pid, (now - waiter.LastSeenUtc).TotalSeconds);
        }
        return reaped;
    }

    private BuildSlotGrant GrantOf(Lease lease) =>
        new(lease.Id, _settings.MaxCpuCount, _leases.Count, _settings.MaxConcurrent, lease.ExpiresAtUtc);

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    private sealed record Lease(Guid Id, int Pid, DateTime ProcessStartUtc, string Label, string? SessionId, string? TaskId,
        DateTime GrantedAtUtc, DateTime ExpiresAtUtc);

    private sealed class Waiter(int pid, DateTime processStartUtc, string label, DateTime sinceUtc)
    {
        public int Pid { get; } = pid;
        public DateTime ProcessStartUtc { get; } = processStartUtc;
        public string Label { get; } = label;
        public DateTime SinceUtc { get; } = sinceUtc;
        public DateTime LastSeenUtc { get; set; } = sinceUtc;
    }
}
