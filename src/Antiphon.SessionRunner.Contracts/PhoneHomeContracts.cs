using System.Text.Json;

namespace Antiphon.SessionRunner.Contracts;

public static class PhoneHomeProtocol
{
    public const int Version = 1;
    public const string RegisterPath = "/api/session-runners/register";
    public const string ConnectPath = "/api/session-runners/{runnerId}/connect";
    public const string StatusPath = "/api/session-runners/{runnerId}/status";
    public const string SecretHeader = "X-Antiphon-Runner-Secret";
    public const string TicketHeader = "X-Antiphon-Runner-Ticket";
    public const string LocalRunnerId = "local";
    public const int DefaultMaxInFlightRequests = 32;
    public const int DefaultMaxMessageUtf8Bytes = 16 * 1024 * 1024;
    public const int DefaultMaxPendingEvents = 1024;
    public const int DefaultMaxPendingEventBytes = 16 * 1024 * 1024;
    public const int DefaultHeartbeatSeconds = 15;
    public const int DefaultLeaseSeconds = 90;
}

public enum PhoneHomeOperation
{
    Capabilities = 1,
    Health = 2,
    List = 3,
    Get = 4,
    Launch = 5,
    Buffer = 6,
    Snapshot = 7,
    Transcript = 8,
    Input = 9,
    ConditionalInput = 10,
    ClearBuffer = 11,
    Resize = 12,
    KillGeneration = 13,

    // CARD-0604 D-15. The runner mirrors a task branch the desktop has already pushed to origin,
    // and removes that mirror at retirement. The DESKTOP worktree stays canonical: these two
    // operations only ever create and destroy a working copy of a branch that already exists on
    // origin, so nothing the runner holds is the only copy of anything.
    WorkspaceMirror = 14,
    WorkspaceRemove = 15,
}

/// <summary>
/// CARD-0604 D-15. Create a mirror worktree of an already-pushed task branch. The runner fetches
/// <paramref name="Branch"/> and refuses unless its tip is exactly <paramref name="Sha"/>: a
/// mirror at a different commit would run the session against source the desktop never produced.
/// </summary>
public sealed record PhoneHomeWorkspaceMirrorRequest(string Branch, string Sha, string Name);

public sealed record PhoneHomeWorkspaceMirrorResponse(string Path);

/// <summary>Remove a mirror created by <see cref="PhoneHomeOperation.WorkspaceMirror"/>.</summary>
public sealed record PhoneHomeWorkspaceRemoveRequest(string Path, bool Force = false);

public sealed record PhoneHomeWorkspaceRemoveResponse(bool Removed, string? Residue);

/// <summary>
/// CARD-0604 G-21. A spilled body that travels inside the Input operation for a remote session.
/// The desktop never writes the file: its Cwd is a Windows path the session cannot see, and a
/// desktop write would leave a file no one reads plus a prompt pointing at nothing.
/// </summary>
public sealed record PhoneHomeInputSpill(string RelativePath, string Body);

public enum PhoneHomeFrameKind
{
    Request = 1,
    Result = 2,
    Error = 3,
    Event = 4,
    Heartbeat = 5,
}

public static class PhoneHomeProblemTypes
{
    public const string Unauthenticated = "phone_home_unauthenticated";
    public const string InvalidTicket = "phone_home_invalid_ticket";
    public const string ProtocolVersion = "phone_home_protocol_version";
    public const string RunnerMismatch = "phone_home_runner_mismatch";
    public const string StoreMismatch = "phone_home_store_mismatch";
    public const string BootConflict = "phone_home_boot_conflict";
    public const string UnsupportedOperation = "phone_home_unsupported_operation";
    public const string UnsupportedTarget = "phone_home_unsupported_target";
    public const string Capacity = "phone_home_capacity";
    public const string Unavailable = "phone_home_unavailable";
    public const string MessageTooLarge = "phone_home_message_too_large";
    public const string RequestLimit = "phone_home_request_limit";
    public const string EventOverflow = "phone_home_event_overflow";
    public const string StaleEpoch = "phone_home_stale_epoch";
}

public sealed record PhoneHomeLimits(
    int MaxInFlightRequests = PhoneHomeProtocol.DefaultMaxInFlightRequests,
    int MaxMessageUtf8Bytes = PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes,
    int MaxPendingEvents = PhoneHomeProtocol.DefaultMaxPendingEvents,
    int MaxPendingEventBytes = PhoneHomeProtocol.DefaultMaxPendingEventBytes)
{
    public void Validate(string prefix)
    {
        if (MaxInFlightRequests <= 0)
            throw new InvalidOperationException($"{prefix}:MaxInFlightRequests must be positive.");
        if (MaxMessageUtf8Bytes <= 0)
            throw new InvalidOperationException($"{prefix}:MaxMessageUtf8Bytes must be positive.");
        if (MaxPendingEvents <= 0)
            throw new InvalidOperationException($"{prefix}:MaxPendingEvents must be positive.");
        if (MaxPendingEventBytes <= 0)
            throw new InvalidOperationException($"{prefix}:MaxPendingEventBytes must be positive.");
    }
}

public sealed record PhoneHomeRegistrationRequest(
    int ProtocolVersion,
    string RunnerId,
    Guid ProcessBootId,
    Guid RunnerStoreId,
    string Platform,
    int Capacity,
    RunnerCapabilitiesDto? Capabilities);

public sealed record PhoneHomeRegistrationResponse(
    string Ticket,
    DateTimeOffset ExpiresAtUtc,
    Guid RunnerStoreId,
    Guid ProcessBootId);

public sealed record PhoneHomeFrame(
    PhoneHomeFrameKind Kind,
    long Epoch,
    Guid RequestId,
    PhoneHomeOperation? Operation = null,
    JsonElement? Payload = null,
    string? ErrorCode = null,
    string? ErrorDetail = null,
    int? StatusCode = null,
    string? EventName = null);

public sealed record PhoneHomeSessionCommand(
    Guid SessionId,
    JsonElement? Body = null);

public sealed record PhoneHomeEventEnvelope(
    string RunnerId,
    Guid RunnerStoreId,
    long Epoch,
    string EventName,
    JsonElement Payload);

public sealed record PhoneHomeRunnerStatusDto(
    string RunnerId,
    Guid? RunnerStoreId,
    Guid? ProcessBootId,
    long? Epoch,
    bool Available,
    bool DispatchEligible,
    DateTimeOffset? LastHeartbeatUtc,
    string? Platform,
    string? BuildVersion,
    string? DisconnectReason);
