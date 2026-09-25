using System.Collections.Concurrent;
using System.Security.Cryptography;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class PhoneHomeRunnerDirectory : ISessionRunnerDirectory
{
    public const string LocalRunnerId = PhoneHomeProtocol.LocalRunnerId;

    private readonly ISessionRunnerClient _local;
    private readonly PhoneHomeRunnerSettings _settings;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, Ticket> _tickets = new(StringComparer.Ordinal);
    private PhoneHomeLiveConnection? _live;
    private Guid? _liveStoreId;
    private Guid? _liveBootId;
    private DateTimeOffset _liveLeaseUntil;
    private long _epoch;
    private long _reconnects;
    private LastDisconnectRecord? _lastDisconnect;
    private readonly Antiphon.Server.Application.Services.RemoteSpillCourier? _spills;

    public PhoneHomeRunnerDirectory(
        ISessionRunnerClient local,
        IOptions<PhoneHomeRunnerSettings> settings,
        IServiceScopeFactory scopes,
        TimeProvider clock,
        // CARD-0604 G-21: absent, a remote spill is typed whole rather than written anywhere.
        Antiphon.Server.Application.Services.RemoteSpillCourier? spills = null)
    {
        _spills = spills;
        _local = local;
        _settings = settings.Value;
        _scopes = scopes;
        _clock = clock;
    }

    public ISessionRunnerClient Local => _local;
    public IReadOnlyList<string> KnownRunnerIds =>
        _settings.Enabled
            ? [LocalRunnerId, _settings.AllowedRunnerId]
            : [LocalRunnerId];

    public Guid? LiveStoreId
    {
        get
        {
            lock (_gate)
                return SnapshotLive()?.RunnerStoreId ?? _liveStoreId;
        }
    }

    public ISessionRunnerClient Resolve(string? runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId) || runnerId == LocalRunnerId)
            return _local;
        var live = SnapshotLive();
        if (live is null || !string.Equals(live.RunnerId, runnerId, StringComparison.Ordinal))
            throw new ServiceUnavailableException("Phone-home runner is unavailable.", PhoneHomeProblemTypes.Unavailable);
        if (!live.DispatchEligible)
            throw new ServiceUnavailableException("Phone-home runner has not completed recovery.", PhoneHomeProblemTypes.Unavailable);
        // CARD-0679 D-6 (card ask 2): the socket can close before the connect route records the
        // end; a client on it would only fail its first request.
        if (!live.SocketOpen)
            throw new ServiceUnavailableException("Phone-home runner connection is closed.", PhoneHomeProblemTypes.Unavailable);
        return new PhoneHomeRunnerClient(live, _spills);
    }

    public async Task<RunnerProviderAuthDto?> RequestProviderAuthAsync(
        string runnerId, string provider, CancellationToken ct)
    {
        if (!_settings.Enabled || !string.Equals(runnerId, _settings.AllowedRunnerId, StringComparison.Ordinal))
            throw new ConflictException("Phone-home runner is unavailable.", PhoneHomeProblemTypes.Unavailable);
        try
        {
            return await Resolve(runnerId).GetProviderAuthAsync(provider, ct);
        }
        catch (ServiceUnavailableException)
        {
            throw new ConflictException("Phone-home runner is unavailable.", PhoneHomeProblemTypes.Unavailable);
        }
        catch (ConflictException ex) when (ex.Code == PhoneHomeProblemTypes.UnsupportedOperation)
        {
            throw new ConflictException("The runner does not support provider authentication probes.",
                PhoneHomeProblemTypes.UnsupportedOperation);
        }
    }

    public async Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
        await GetBindingAsync(sessionId, ct) is SessionRunnerBinding.Remote remote ? remote.Owner : null;

    /// <summary>
    /// CARD-0679 D-3: how many binding reads this directory has run, so a test can see the
    /// recovery pump's owner cache doing its job.
    /// </summary>
    internal long BindingLookups => Interlocked.Read(ref _bindingLookups);

    private long _bindingLookups;

    public async Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct)
    {
        Interlocked.Increment(ref _bindingLookups);
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.RunnerId, s.RunnerStoreId, s.RunnerCwd })
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return SessionRunnerBinding.Missing.Instance;
        if (row.RunnerId is null || row.RunnerStoreId is null || row.RunnerCwd is null)
            return SessionRunnerBinding.Local.Instance;
        return new SessionRunnerBinding.Remote(new SessionRunnerOwner(row.RunnerId, row.RunnerStoreId.Value, row.RunnerCwd));
    }

    public async Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runnerId) || runnerId == LocalRunnerId)
        {
            try
            {
                return new RunnerInventory.Available(await _local.ListAsync(ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new RunnerInventory.Unavailable(ex.Message);
            }
        }

        var live = SnapshotLive();
        if (live is null || !live.DispatchEligible || live.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds)))
            return new RunnerInventory.Unavailable("phone-home runner unavailable");
        try
        {
            return new RunnerInventory.Available(await new PhoneHomeRunnerClient(live).ListAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new RunnerInventory.Unavailable(ex.Message);
        }
    }

    public bool AuthenticateSecret(string? provided)
    {
        if (!_settings.Enabled || string.IsNullOrEmpty(_settings.SharedSecret) || string.IsNullOrEmpty(provided))
            return false;
        var expected = System.Text.Encoding.UTF8.GetBytes(_settings.SharedSecret);
        var actual = System.Text.Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public PhoneHomeRegistrationResponse Register(PhoneHomeRegistrationRequest request)
    {
        if (request.ProtocolVersion != PhoneHomeProtocol.Version)
            throw new ConflictException("Unsupported phone-home protocol version.", PhoneHomeProblemTypes.ProtocolVersion);
        if (!string.Equals(request.RunnerId, _settings.AllowedRunnerId, StringComparison.Ordinal))
            throw new ConflictException("Runner id is not allowed.", PhoneHomeProblemTypes.RunnerMismatch);
        // CARD-0604 D-14: the runner is a bounded pool, not a single seat. Capacity is declared by
        // the runner and bounded by the server, so a misconfigured runner cannot enlarge itself.
        if (request.Capacity < 1 || request.Capacity > _settings.MaxCapacity)
            throw new ConflictException(
                $"Phone-home capacity must be between 1 and {_settings.MaxCapacity}.",
                PhoneHomeProblemTypes.Capacity);

        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (_liveBootId is { } currentBoot
                && currentBoot != Guid.Empty
                && currentBoot != request.ProcessBootId
                && (_live is { } liveOwner && !liveOwner.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds))
                    || _liveLeaseUntil > now))
            {
                throw new ConflictException("A competing boot cannot replace an unexpired owner.", PhoneHomeProblemTypes.BootConflict);
            }
            if (_live is { } live && !live.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds)))
            {
                if (live.ProcessBootId != request.ProcessBootId)
                    throw new ConflictException("A competing boot cannot replace an unexpired owner.", PhoneHomeProblemTypes.BootConflict);
            }
            else if (_liveStoreId is { } store && store != request.RunnerStoreId)
            {
                throw new ConflictException("Runner store identity does not match the live binding.", PhoneHomeProblemTypes.StoreMismatch);
            }

            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            // CARD-0604 CP-6a: the epoch is minted HERE, not at AcceptConnect, so the registration
            // response can hand the runner the same number the server will stamp on its own frames.
            // A runner that numbers its own connections desynchronises the moment either process
            // restarts alone, and both receive loops then drop every frame in silence.
            var epoch = ++_epoch;
            _tickets[ticket] = new Ticket(
                ticket, request.RunnerId, request.RunnerStoreId, request.ProcessBootId,
                now.AddSeconds(_settings.TicketTtlSeconds),
                request.Capacity, request.Platform, request.Capabilities, epoch);
            _liveStoreId = request.RunnerStoreId;
            _liveBootId = request.ProcessBootId;
            _liveLeaseUntil = now.AddSeconds(_settings.LeaseSeconds);
            return new PhoneHomeRegistrationResponse(
                ticket, now.AddSeconds(_settings.TicketTtlSeconds), request.RunnerStoreId, request.ProcessBootId, epoch);
        }
    }

    public void PeekTicket(string runnerId, string ticketValue)
    {
        lock (_gate)
        {
            if (!_tickets.TryGetValue(ticketValue, out var ticket))
                throw new ConflictException("Connection ticket is invalid.", PhoneHomeProblemTypes.InvalidTicket);
            ValidateTicket(runnerId, ticket);
        }
    }

    public PhoneHomeLiveConnection AcceptConnect(
        string runnerId, string ticketValue, System.Net.WebSockets.WebSocket socket)
    {
        lock (_gate)
        {
            if (!_tickets.Remove(ticketValue, out var ticket))
                throw new ConflictException("Connection ticket is invalid.", PhoneHomeProblemTypes.InvalidTicket);
            ValidateTicket(runnerId, ticket);

            if (_live is { } superseded)
            {
                RecordDisconnect(superseded, "superseded");
                superseded.DisposeAsync("superseded").AsTask().GetAwaiter().GetResult();
            }
            // The ticket already carries the epoch the runner was told at registration.
            var epoch = ticket.Epoch;
            var connection = new PhoneHomeLiveConnection(
                runnerId, ticket.RunnerStoreId, ticket.ProcessBootId, epoch, socket, _settings.Limits, _clock,
                ticket.Capacity, ticket.Platform, ticket.Capabilities);
            _live = connection;
            _liveLeaseUntil = _clock.GetUtcNow().AddSeconds(_settings.LeaseSeconds);
            _reconnects++;
            return connection;
        }
    }

    public void MarkRecovered(PhoneHomeLiveConnection? connection)
    {
        if (connection is null)
            return;
        lock (_gate)
        {
            if (!ReferenceEquals(_live, connection))
                return;
            connection.DispatchEligible = true;
        }
    }

    private void ValidateTicket(string runnerId, Ticket ticket)
    {
        var now = _clock.GetUtcNow();
        if (ticket.ExpiresAtUtc < now
            || !string.Equals(ticket.RunnerId, runnerId, StringComparison.Ordinal)
            || ticket.RunnerStoreId != _liveStoreId
            || ticket.ProcessBootId != _liveBootId)
            throw new ConflictException("Connection ticket is bound, expired, or already used.", PhoneHomeProblemTypes.InvalidTicket);
    }

    /// <summary>
    /// CARD-0679 D-1: the first reason recorded for a connection wins; the directory keeps the
    /// current connection's reason as <see cref="PhoneHomeRunnerStatusDto.DisconnectReason"/>.
    /// </summary>
    public void Disconnect(PhoneHomeLiveConnection connection, string reason)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_live, connection))
                return;
            RecordDisconnect(connection, reason);
            connection.DispatchEligible = false;
            _live = null;
        }
    }

    // Caller holds _gate.
    private void RecordDisconnect(PhoneHomeLiveConnection connection, string reason)
    {
        connection.TryRecordDisconnect(reason);
        _lastDisconnect = new LastDisconnectRecord(
            connection.LastDisconnectReason ?? reason,
            connection.LastDisconnectAtUtc ?? _clock.GetUtcNow(),
            connection.Epoch);
    }

    /// <summary>CARD-0679 D-1: connections accepted since this process started.</summary>
    public long Reconnects
    {
        get
        {
            lock (_gate)
                return _reconnects;
        }
    }

    public int? DeclaredCapacity(string runnerId)
    {
        var live = SnapshotLive();
        if (live is null || !string.Equals(live.RunnerId, runnerId, StringComparison.Ordinal))
            return null;
        if (!live.DispatchEligible || !live.SocketOpen
            || live.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds)))
            return null;
        return live.Capacity;
    }

    /// <summary>
    /// CARD-0679 D-10: the recovered connection's cached inventory, less entries unconfirmed for
    /// longer than <see cref="PhoneHomeRunnerSettings.InventoryMaxAge"/>. A connection that is not
    /// dispatch-eligible (still recovering), whose lease expired or whose socket closed vouches for nothing.
    /// </summary>
    public IReadOnlyCollection<Guid> LiveRemoteSessionIds()
    {
        var live = SnapshotLive();
        if (live is null || !live.DispatchEligible || !live.SocketOpen
            || live.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds)))
            return [];
        return live.KnownLiveSessions(_settings.InventoryMaxAge);
    }

    public PhoneHomeRunnerStatusDto Status(string runnerId)
    {
        var live = SnapshotLive();
        var leaseExpired = live is not null && live.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds));
        var available = live is not null && !leaseExpired && live.SocketOpen;
        LastDisconnectRecord? last;
        long reconnects;
        lock (_gate)
        {
            last = _lastDisconnect;
            reconnects = _reconnects;
        }

        // CARD-0679 D-1: name why the runner is not available instead of a constant: the live
        // connection's own recorded end, an expired lease, or the last connection's end.
        var disconnectReason = available
            ? null
            : live?.LastDisconnectReason
                ?? (leaseExpired ? "lease_expired" : null)
                ?? (live is not null && !live.SocketOpen ? "socket_closed" : null)
                ?? last?.Reason
                ?? "unavailable";
        return new PhoneHomeRunnerStatusDto(
            runnerId,
            live?.RunnerStoreId ?? _liveStoreId,
            live?.ProcessBootId ?? _liveBootId,
            live?.Epoch,
            available,
            live is { DispatchEligible: true } && available,
            live?.LastHeartbeatUtc,
            // CARD-0604: registration carries the platform and the capabilities DTO, so the status
            // a deploy or a restart row reads is the runner's own report, not a null placeholder.
            Platform: live?.Platform,
            BuildVersion: live?.Capabilities?.Version,
            DisconnectReason: disconnectReason,
            PendingEvents: live?.PendingEvents,
            PendingEventBytes: live?.PendingEventBytes,
            LastDisconnectAtUtc: live?.LastDisconnectAtUtc ?? last?.AtUtc,
            Reconnects: reconnects,
            LastCatchUpMs: live?.LastCatchUpMs);
    }

    public PhoneHomeLiveConnection? SnapshotLive()
    {
        lock (_gate)
        {
            if (_live is null)
                return null;
            if (_live.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds)))
            {
                _live.DispatchEligible = false;
                return _live;
            }

            return _live;
        }
    }

    private sealed record LastDisconnectRecord(string Reason, DateTimeOffset AtUtc, long Epoch);

    private sealed record Ticket(
        string Value,
        string RunnerId,
        Guid RunnerStoreId,
        Guid ProcessBootId,
        DateTimeOffset ExpiresAtUtc,
        int Capacity,
        string? Platform,
        RunnerCapabilitiesDto? Capabilities,
        long Epoch);
}
