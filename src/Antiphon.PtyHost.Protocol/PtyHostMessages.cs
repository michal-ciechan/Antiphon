using System.Text.Json.Serialization;

namespace Antiphon.PtyHost.Protocol;

/// <summary>
/// Wire messages between the session-runner (client) and a pty-host (server) over the
/// per-session named pipe. The protocol is versioned and append-only: never repurpose a
/// discriminator or change the meaning of an existing field — add new messages/fields and
/// bump <see cref="PtyHostProtocol.Version"/> only when the runner must distinguish hosts.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
// client -> host
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(LaunchMessage), "launch")]
[JsonDerivedType(typeof(AttachMessage), "attach")]
[JsonDerivedType(typeof(InputMessage), "input")]
[JsonDerivedType(typeof(SendLineMessage), "sendLine")]
[JsonDerivedType(typeof(ResizeMessage), "resize")]
[JsonDerivedType(typeof(KillMessage), "kill")]
[JsonDerivedType(typeof(ClearLiveBufferMessage), "clearLiveBuffer")]
[JsonDerivedType(typeof(StatusRequestMessage), "status")]
[JsonDerivedType(typeof(ShutdownMessage), "shutdown")]
// host -> client
[JsonDerivedType(typeof(HelloAckMessage), "helloAck")]
[JsonDerivedType(typeof(LaunchedMessage), "launched")]
[JsonDerivedType(typeof(OutputMessage), "output")]
[JsonDerivedType(typeof(ExitedMessage), "exited")]
[JsonDerivedType(typeof(StatusReplyMessage), "statusReply")]
[JsonDerivedType(typeof(ResyncMessage), "resync")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
public abstract record PtyHostMessage;

// ── client -> host ───────────────────────────────────────────────────────────

/// <summary>First frame on every connection. The host replies with <see cref="HelloAckMessage"/>.</summary>
public sealed record HelloMessage(int ProtocolVersion) : PtyHostMessage;

/// <summary>
/// Spawn the ConPTY child. Sent over the pipe (never on the command line) because
/// <paramref name="Env"/> may contain secrets and args quoting is fragile.
/// </summary>
public sealed record LaunchMessage(
    string Exe,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Env,
    string Cwd,
    int Cols,
    int Rows,
    int MemoryLimitMb,
    bool TranscriptEnabled,
    string AnsiLogPath) : PtyHostMessage;

/// <summary>
/// Subscribe to live output, replaying chunks with sequence &gt; <paramref name="LastSeq"/> first.
/// If the replay ring no longer holds that range the host replies <see cref="ResyncMessage"/> and
/// does NOT stream; the client rebuilds from the ansi log and re-attaches at the resync sequence.
/// </summary>
public sealed record AttachMessage(long LastSeq) : PtyHostMessage;

public sealed record InputMessage(string Data) : PtyHostMessage;

/// <summary>Write text then Enter as two writes (ConPTY drops trailing \r on oversized single chunks).</summary>
public sealed record SendLineMessage(string Line) : PtyHostMessage;

public sealed record ResizeMessage(int Cols, int Rows) : PtyHostMessage;

public sealed record KillMessage(int TimeoutMs) : PtyHostMessage;

public sealed record ClearLiveBufferMessage : PtyHostMessage;

public sealed record StatusRequestMessage : PtyHostMessage;

/// <summary>
/// Final ack: the runner has recorded the session's fate; the host deletes its manifest and exits.
/// </summary>
public sealed record ShutdownMessage : PtyHostMessage;

// ── host -> client ───────────────────────────────────────────────────────────

public sealed record HelloAckMessage(
    int ProtocolVersion,
    string HostVersion,
    Guid SessionId,
    string Status) : PtyHostMessage;

public sealed record LaunchedMessage(int ChildPid, DateTime ChildStartTimeUtc) : PtyHostMessage;

public sealed record OutputMessage(long Seq, string Chunk) : PtyHostMessage;

public sealed record ExitedMessage(int? ExitCode, string ExitReason, long LastSeq) : PtyHostMessage;

public sealed record StatusReplyMessage(
    string Status,
    int? ChildPid,
    DateTime? ChildStartTimeUtc,
    int Cols,
    int Rows,
    long LastSeq,
    int? ExitCode,
    string? ExitReason) : PtyHostMessage;

/// <summary>
/// The requested attach point fell out of the replay ring. The client must rebuild state from
/// the ansi log, then send a fresh <see cref="AttachMessage"/> with <paramref name="LastSeq"/>.
/// </summary>
public sealed record ResyncMessage(long FirstAvailableSeq, long LastSeq) : PtyHostMessage;

public sealed record ErrorMessage(string Code, string Message) : PtyHostMessage;

/// <summary>Session status strings used in <see cref="HelloAckMessage"/>/<see cref="StatusReplyMessage"/>.</summary>
public static class PtyHostStatus
{
    public const string WaitingForLaunch = "WaitingForLaunch";
    public const string Running = "Running";
    public const string Exited = "Exited";
}

public static class PtyHostProtocol
{
    public const int Version = 1;

    /// <summary>Well-known pipe name for a session's host.</summary>
    public static string PipeNameFor(Guid sessionId) => $"antiphon-pty-{sessionId:N}";
}
