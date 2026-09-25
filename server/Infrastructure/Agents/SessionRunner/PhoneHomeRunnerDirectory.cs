using System.Collections.Concurrent;
using System.Security.Cryptography;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
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
    private readonly PendingRunnerSessionInventory _pendingInventory;
    private readonly object _gate = new();
    private readonly Dictionary<string, Ticket> _tickets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RunnerSlot> _slots = new(StringComparer.Ordinal);
    private readonly ILogger _logger;
    private readonly Antiphon.Server.Application.Services.RemoteSpillCourier? _spills;

    public PhoneHomeRunnerDirectory(
        ISessionRunnerClient local,
        IOptions<PhoneHomeRunnerSettings> settings,
        IServiceScopeFactory scopes,
        TimeProvider clock,
        // CARD-0604 G-21: absent, a remote spill is typed whole rather than written anywhere.
        Antiphon.Server.Application.Services.RemoteSpillCourier? spills = null,
        ILogger<PhoneHomeRunnerDirectory>? inventoryLogger = null)
    {
        _spills = spills;
        _local = local;
        _settings = settings.Value;
        _scopes = scopes;
        _clock = clock;
        _logger = inventoryLogger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PhoneHomeRunnerDirectory>.Instance;
        _pendingInventory = new PendingRunnerSessionInventory(scopes, clock, _logger);
        if (_settings.Enabled)
        {
            if (PhoneHomeRunnerCatalog.UsesMap(_settings))
                _logger.LogWarning("PhoneHomeRunner singleton keys are ignored because PhoneHomeRunner:Runners is set.");
            foreach (var resolved in PhoneHomeRunnerCatalog.Configured(_settings))
                _slots[resolved.Id] = new RunnerSlot { Id = resolved.Id, Entry = resolved.Entry };
        }
    }

    public ISessionRunnerClient Local => _local;
    public IReadOnlyList<string> KnownRunnerIds
    {
        get
        {
            var ids = new List<string> { LocalRunnerId };
            ids.AddRange(_slots.Keys);
            return ids;
        }
    }

    internal IReadOnlyList<string> RemoteRunnerIds
    {
        get
        {
            lock (_gate)
                return _slots.Keys.ToArray();
        }
    }

    public Guid? GetLiveStoreId(string? runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId) || !_slots.TryGetValue(runnerId, out var slot))
            return null;
        lock (_gate)
            return SnapshotOf(slot)?.RunnerStoreId ?? slot.StoreId;
    }

    public ISessionRunnerClient Resolve(string? runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId) || runnerId == LocalRunnerId || RunnerRequestIntent.IsDesktopAlias(runnerId))
            return _local;
        var live = SnapshotLive(runnerId);
        if (live is null)
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
        if (!_slots.TryGetValue(runnerId, out var slot) || !slot.Entry.Enabled)
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

        var live = SnapshotLive(runnerId);
        if (live is null || !live.DispatchEligible || live.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds)))
            return new RunnerInventory.Unavailable("phone-home runner unavailable");
        try
        {
            return new RunnerInventory.Available(await new PhoneHomeRunnerClient(live, _spills).ListAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new RunnerInventory.Unavailable(ex.Message);
        }
    }

    public bool AuthenticateSecret(string? provided)
    {
        if (_slots.Count != 1)
            return false;
        return AuthenticateSecret(_slots.Keys.First(), provided);
    }

    public bool AuthenticateSecret(string? runnerId, string? provided)
    {
        if (!_settings.Enabled || string.IsNullOrEmpty(provided) || string.IsNullOrWhiteSpace(runnerId))
            return false;
        if (!_slots.TryGetValue(runnerId, out var slot) || !slot.Entry.Enabled || string.IsNullOrEmpty(slot.Entry.SharedSecret))
            return false;
        var expected = System.Text.Encoding.UTF8.GetBytes(slot.Entry.SharedSecret);
        var actual = System.Text.Encoding.UTF8.GetBytes(provided);
        if (expected.Length != actual.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public PhoneHomeRegistrationResponse Register(PhoneHomeRegistrationRequest request)
    {
        if (request.ProtocolVersion != PhoneHomeProtocol.Version)
            throw new ConflictException("Unsupported phone-home protocol version.", PhoneHomeProblemTypes.ProtocolVersion);
        if (!_slots.TryGetValue(request.RunnerId, out var slot) || !slot.Entry.Enabled)
            throw new ConflictException("Runner id is not allowed.", PhoneHomeProblemTypes.RunnerMismatch);
        // CARD-0604 D-14: the runner is a bounded pool, not a single seat. Capacity is declared by
        // the runner and bounded by that entry, so a misconfigured runner cannot enlarge itself.
        if (request.Capacity < 1 || request.Capacity > slot.Entry.MaxCapacity)
            throw new ConflictException(
                $"Phone-home capacity must be between 1 and {slot.Entry.MaxCapacity}.",
                PhoneHomeProblemTypes.Capacity);
        var registeredPlatform = RunnerPlatformWire.Normalize(request.Platform);
        var reportedPlatform = RunnerPlatformWire.Normalize(request.Capabilities?.Platform);
        if (registeredPlatform is not null && reportedPlatform is not null
            && !string.Equals(registeredPlatform, reportedPlatform, StringComparison.Ordinal))
        {
            throw new ConflictException(
                $"Runner '{request.RunnerId}' registration platform {registeredPlatform} disagrees with capabilities {reportedPlatform}.",
                RunnerPlatformProblems.Conflict);
        }
        var platform = reportedPlatform ?? registeredPlatform;

        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            var lease = TimeSpan.FromSeconds(_settings.LeaseSeconds);
            if (slot.BootId is { } currentBoot
                && currentBoot != Guid.Empty
                && currentBoot != request.ProcessBootId
                && (slot.Live is { } liveOwner && !liveOwner.IsLeaseExpired(lease)
                    || slot.LeaseUntil > now))
            {
                throw new ConflictException("A competing boot cannot replace an unexpired owner.", PhoneHomeProblemTypes.BootConflict);
            }
            if (slot.Live is { } live && !live.IsLeaseExpired(lease))
            {
                if (live.ProcessBootId != request.ProcessBootId)
                    throw new ConflictException("A competing boot cannot replace an unexpired owner.", PhoneHomeProblemTypes.BootConflict);
            }
            else if (slot.StoreId is { } store && store != request.RunnerStoreId)
            {
                throw new ConflictException("Runner store identity does not match the live binding.", PhoneHomeProblemTypes.StoreMismatch);
            }

            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            // CARD-0604 CP-6a: the epoch is minted HERE, not at AcceptConnect, so the registration
            // response can hand the runner the same number the server will stamp on its own frames.
            // A runner that numbers its own connections desynchronises the moment either process
            // restarts alone, and both receive loops then drop every frame in silence.
            var epoch = ++slot.Epoch;
            _tickets[ticket] = new Ticket(
                ticket, request.RunnerId, request.RunnerStoreId, request.ProcessBootId,
                now.AddSeconds(_settings.TicketTtlSeconds),
                request.Capacity, platform, request.Capabilities, epoch);
            slot.StoreId = request.RunnerStoreId;
            slot.BootId = request.ProcessBootId;
            slot.LeaseUntil = now.AddSeconds(_settings.LeaseSeconds);
            slot.RegisteredPlatform = platform;
            slot.PlatformObservedAt = platform is null ? slot.PlatformObservedAt : now;
            slot.Capabilities = request.Capabilities ?? slot.Capabilities;
            slot.RegisteredCapacity = request.Capacity;
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

            if (!_slots.TryGetValue(runnerId, out var slot))
                throw new ConflictException("Connection ticket is invalid.", PhoneHomeProblemTypes.InvalidTicket);
            if (slot.Live is { } superseded)
            {
                RecordDisconnect(slot, superseded, "superseded");
                superseded.DisposeAsync("superseded").AsTask().GetAwaiter().GetResult();
            }
            // The ticket already carries the epoch the runner was told at registration.
            var epoch = ticket.Epoch;
            var connection = new PhoneHomeLiveConnection(
                runnerId, ticket.RunnerStoreId, ticket.ProcessBootId, epoch, socket, _settings.Limits, _clock,
                ticket.Capacity, ticket.Platform, ticket.Capabilities);
            slot.Live = connection;
            slot.LeaseUntil = _clock.GetUtcNow().AddSeconds(_settings.LeaseSeconds);
            slot.Reconnects++;
            return connection;
        }
    }

    public void MarkRecovered(PhoneHomeLiveConnection? connection)
    {
        if (connection is null)
            return;
        lock (_gate)
        {
            if (!_slots.TryGetValue(connection.RunnerId, out var slot) || !ReferenceEquals(slot.Live, connection))
                return;
            connection.DispatchEligible = true;
            slot.LastRecovered = connection;
        }
    }

    private void ValidateTicket(string runnerId, Ticket ticket)
    {
        var now = _clock.GetUtcNow();
        if (!_slots.TryGetValue(ticket.RunnerId, out var slot)
            || ticket.ExpiresAtUtc < now
            || !string.Equals(ticket.RunnerId, runnerId, StringComparison.Ordinal)
            || ticket.RunnerStoreId != slot.StoreId
            || ticket.ProcessBootId != slot.BootId)
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
            if (!_slots.TryGetValue(connection.RunnerId, out var slot) || !ReferenceEquals(slot.Live, connection))
                return;
            RecordDisconnect(slot, connection, reason);
            connection.DispatchEligible = false;
            slot.Live = null;
        }
    }

    // Caller holds _gate.
    private void RecordDisconnect(RunnerSlot slot, PhoneHomeLiveConnection connection, string reason)
    {
        connection.TryRecordDisconnect(reason);
        slot.LastDisconnect = new LastDisconnectRecord(
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
                return _slots.Values.Sum(slot => slot.Reconnects);
        }
    }

    public int? DeclaredCapacity(string runnerId)
    {
        var live = SnapshotLive(runnerId);
        if (live is null)
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
        var set = new HashSet<Guid>();
        foreach (var slot in _slots.Values)
        {
            var live = SnapshotOf(slot);
            if (live is null || !live.DispatchEligible || !live.SocketOpen
                || live.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds)))
                continue;
            foreach (var id in live.KnownLiveSessions(_settings.InventoryMaxAge))
                set.Add(id);
        }

        return set;
    }

    /// <summary>
    /// CARD-0679 (review 87af1bf6): sessions neither confirmed live nor confirmed gone. On the
    /// current recovered connection, the entries past <see cref="PhoneHomeRunnerSettings.InventoryMaxAge"/>;
    /// when that connection is not vouching (recovering, lease-expired, closed or gone), every entry
    /// of the last recovered one, until a newer connection's catch-up List answers for them.
    /// Before any connection has answered in this process (review f87b49a7) there is no inventory
    /// at all, so it is every session the desktop's own record binds to the accepted runner and
    /// has not seen end (Starting, Running, Stopping).
    /// </summary>
    public IReadOnlyCollection<Guid> UnknownRemoteSessionIds()
    {
        var set = new HashSet<Guid>();
        foreach (var slot in _slots.Values)
        {
            foreach (var id in UnknownFor(slot))
                set.Add(id);
        }

        return set;
    }

    private IReadOnlyCollection<Guid> UnknownFor(RunnerSlot slot)
    {
        PhoneHomeLiveConnection? last;
        lock (_gate)
            last = slot.LastRecovered;
        if (last is null)
        {
            if (!RemoteInventoryPending(slot.Id))
                return [];
            IReadOnlyCollection<Guid> pending;
            try { pending = _pendingInventory.Read(slot.Id); }
            catch
            {
                // A successful first List can overtake even a failed bootstrap read.
                lock (_gate)
                {
                    if (slot.LastRecovered is null) throw;
                    return slot.LastRecovered.KnownLiveSessions();
                }
            }
            lock (_gate)
            {
                // Include current authoritative membership: ListLiveOrUnknownSessions may have
                // read its live half before this load blocked. Tombstones remain authoritative.
                return slot.LastRecovered is { } recovered ? recovered.KnownLiveSessions() : pending;
            }
        }

        var live = SnapshotOf(slot);
        if (live is not null && ReferenceEquals(live, last) && live.DispatchEligible && live.SocketOpen
            && !live.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds)))
            return live.UnconfirmedSessions(_settings.InventoryMaxAge);
        return last.KnownLiveSessions();
    }

    public bool RemoteInventoryPending(string? runnerId)
    {
        // A runner this desktop does not accept can never answer, so its sessions are not held open.
        if (string.IsNullOrWhiteSpace(runnerId) || !_slots.TryGetValue(runnerId, out var slot) || !slot.Entry.Enabled)
            return false;
        lock (_gate)
            return slot.LastRecovered is null;
    }

    public PhoneHomeRunnerStatusDto Status(string runnerId)
    {
        if (RunnerRequestIntent.IsDesktopAlias(runnerId))
        {
            return new PhoneHomeRunnerStatusDto(
                RunnerPlatformWire.DesktopId, null, null, null, false, false, null, null, null, "desktop");
        }

        if (!_slots.TryGetValue(runnerId, out var slot))
            throw new NotFoundException("SessionRunner", runnerId);
        var live = SnapshotOf(slot);
        var leaseExpired = live is not null && live.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds));
        var available = live is not null && !leaseExpired && live.SocketOpen;
        LastDisconnectRecord? last;
        long reconnects;
        lock (_gate)
        {
            last = slot.LastDisconnect;
            reconnects = slot.Reconnects;
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
            live?.RunnerStoreId ?? slot.StoreId,
            live?.ProcessBootId ?? slot.BootId,
            live?.Epoch,
            available,
            live is { DispatchEligible: true } && available,
            live?.LastHeartbeatUtc,
            // CARD-0604: registration carries the platform and the capabilities DTO, so the status
            // a deploy or a restart row reads is the runner's own report, not a null placeholder.
            Platform: live?.Platform ?? slot.RegisteredPlatform,
            BuildVersion: live?.Capabilities?.Version ?? slot.Capabilities?.Version,
            DisconnectReason: disconnectReason,
            PendingEvents: live?.PendingEvents,
            PendingEventBytes: live?.PendingEventBytes,
            LastDisconnectAtUtc: live?.LastDisconnectAtUtc ?? last?.AtUtc,
            Reconnects: reconnects,
            LastCatchUpMs: live?.LastCatchUpMs);
    }

    public async Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runnerId) || RunnerRequestIntent.IsDesktopAlias(runnerId))
        {
            RunnerCapabilitiesDto? caps = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                caps = await _local.GetCapabilitiesAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                caps = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                caps = null;
            }

            var platform = RunnerPlatformWire.Normalize(caps?.Platform);
            var features = caps?.Features ?? [];
            return new RunnerDescriptor(
                RunnerPlatformWire.DesktopId, "Desktop", platform,
                platform is null ? null : _clock.GetUtcNow(),
                caps is not null, caps is not null && features.Contains(RunnerPlatformWire.Feature),
                platform is null, null, caps);
        }

        if (!_slots.TryGetValue(runnerId, out var slot))
            return null;
        var live = SnapshotOf(slot);
        var observed = RunnerPlatformWire.Normalize(live?.Platform ?? slot.RegisteredPlatform);
        var eligible = live is { DispatchEligible: true, SocketOpen: true }
            && !live.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds));
        var display = string.IsNullOrWhiteSpace(slot.Entry.DisplayName) ? slot.Id : slot.Entry.DisplayName;
        return new RunnerDescriptor(
            slot.Id, display, observed,
            observed is null ? null : slot.PlatformObservedAt ?? live?.LastHeartbeatUtc ?? _clock.GetUtcNow(),
            eligible, eligible, !eligible, eligible ? live?.Capacity : null, live?.Capabilities ?? slot.Capabilities);
    }

    /// <summary>The live connection for <paramref name="runnerId"/> only. Never another runner's socket.</summary>
    public PhoneHomeLiveConnection? SnapshotLive(string? runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId) || RunnerRequestIntent.IsDesktopAlias(runnerId))
            return null;
        return _slots.TryGetValue(runnerId, out var slot) ? SnapshotOf(slot) : null;
    }

    /// <summary>The single configured remote, for callers that predate a runner id. Null when several are configured.</summary>
    public PhoneHomeLiveConnection? SnapshotLive()
    {
        if (_slots.Count != 1)
            return null;
        return SnapshotOf(_slots.Values.First());
    }

    private PhoneHomeLiveConnection? SnapshotOf(RunnerSlot slot)
    {
        lock (_gate)
        {
            if (slot.Live is null)
                return null;
            if (slot.Live.IsLeaseExpired(TimeSpan.FromSeconds(_settings.LeaseSeconds)))
            {
                slot.Live.DispatchEligible = false;
                return slot.Live;
            }

            return slot.Live;
        }
    }

    private sealed class RunnerSlot
    {
        public required string Id;
        public required PhoneHomeRunnerEntry Entry;
        public PhoneHomeLiveConnection? Live;
        public PhoneHomeLiveConnection? LastRecovered;
        public Guid? StoreId;
        public Guid? BootId;
        public DateTimeOffset LeaseUntil;
        public long Epoch;
        public long Reconnects;
        public LastDisconnectRecord? LastDisconnect;
        public string? RegisteredPlatform;
        public DateTimeOffset? PlatformObservedAt;
        public RunnerCapabilitiesDto? Capabilities;
        public int RegisteredCapacity;
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
