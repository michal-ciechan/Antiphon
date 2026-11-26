using System.Diagnostics;
using System.Text.Json.Serialization;
using Antiphon.SessionRunner.Contracts;

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
[JsonDerivedType(typeof(CustodyRequestMessage), "custody")]
// host -> client
[JsonDerivedType(typeof(HelloAckMessage), "helloAck")]
[JsonDerivedType(typeof(AttachedMessage), "attached")]
[JsonDerivedType(typeof(LaunchedMessage), "launched")]
[JsonDerivedType(typeof(OutputMessage), "output")]
[JsonDerivedType(typeof(ExitedMessage), "exited")]
[JsonDerivedType(typeof(StatusReplyMessage), "statusReply")]
[JsonDerivedType(typeof(ResyncMessage), "resync")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
[JsonDerivedType(typeof(CustodyReplyMessage), "custodyReply")]
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
    string AnsiLogPath,
    global::Antiphon.SessionRunner.Contracts.GrokRulesReceipt? GrokRulesReceipt = null,
    global::Antiphon.SessionRunner.Contracts.VerificationExecutionBinding? VerificationBinding = null,
    Guid? RunnerStoreId = null,
    DateTime? AcceptedStartedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? LinuxPlacementDeadline = null) : PtyHostMessage;

/// <summary>Private launch extension using the Linux Stopwatch clock (CLOCK_MONOTONIC ns).</summary>
public static class LinuxPlacementLaunch
{
    public const string Feature = "linuxPlacementV1";
    public static readonly TimeSpan RequestLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReplyReserve = TimeSpan.FromSeconds(1);
    // CARD-2080 measured an eight-second cold p95 under runner CPU contention.
    private static readonly TimeSpan PlacementLimit = TimeSpan.FromSeconds(16);

    public static bool AppliesTo(LaunchMessage launch) => OperatingSystem.IsLinux() &&
        launch.VerificationBinding?.Backend == VerificationCustodyBackends.LinuxCgroup;

    public static long Stamp(long requestStarted, long now) =>
        Math.Min(Add(now, PlacementLimit), Add(requestStarted, RequestLimit - ReplyReserve));

    public static long ClampAtReceipt(long senderDeadline, long atReceipt) =>
        Math.Min(senderDeadline, Add(atReceipt, PlacementLimit));

    private static long Add(long timestamp, TimeSpan duration) =>
        checked(timestamp + (long)duration.TotalSeconds * Stopwatch.Frequency);
}

/// <summary>Negotiated v1 custody request. Seal is irreversible and never kills a process.</summary>
public sealed record CustodyRequestMessage(
    global::Antiphon.SessionRunner.Contracts.VerificationExecutionBinding Binding, bool Seal = false) : PtyHostMessage;

public sealed record CustodyReplyMessage(
    global::Antiphon.SessionRunner.Contracts.VerificationCustodyStatus Custody) : PtyHostMessage;

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
    string Status,
    IReadOnlyList<string>? Features = null,
    Guid? HostInstanceId = null) : PtyHostMessage;

public sealed record LaunchedMessage(
    int ChildPid,
    DateTime ChildStartTimeUtc,
    DateTime? AcceptedStartedAt = null) : PtyHostMessage;

/// <summary>
/// Successful attach ack, sent BEFORE any replayed output so the client can distinguish
/// "attached, replay follows" from "resync required" deterministically.
/// </summary>
public sealed record AttachedMessage(long ReplayFromSeq, long LastSeq) : PtyHostMessage;

public sealed record OutputMessage(long Seq, string Chunk) : PtyHostMessage;

public sealed record ExitedMessage(int? ExitCode, string ExitReason, long LastSeq, int? ExitSignal = null,
    global::Antiphon.SessionRunner.Contracts.ProcessStopIntentDto? ExitStopIntent = null) : PtyHostMessage;

public sealed record StatusReplyMessage(
    string Status,
    int? ChildPid,
    DateTime? ChildStartTimeUtc,
    int Cols,
    int Rows,
    long LastSeq,
    int? ExitCode,
    string? ExitReason,
    int? ExitSignal = null,
    global::Antiphon.SessionRunner.Contracts.ProcessStopIntentDto? ExitStopIntent = null) : PtyHostMessage;

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

    /// <summary>
    /// Well-known pipe name for a session's host. Windows uses an unrooted name (the NT named-pipe
    /// prefix is added by the runtime). Linux/macOS use an absolute path under <c>/tmp</c> so the
    /// runner and host cannot disagree about <see cref="Path.GetTempPath"/> (a Docker phone-home
    /// launch timed out connecting to <c>CoreFxPipe_antiphon-pty-*</c> while the host waited).
    /// </summary>
    public static string PipeNameFor(Guid sessionId)
    {
        var name = $"antiphon-pty-{sessionId:N}";
        return OperatingSystem.IsWindows() ? name : "/tmp/" + name;
    }
}
