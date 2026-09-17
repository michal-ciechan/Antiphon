using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Antiphon.NightlyWatchdog;

public enum JobKind { Queued, Running, Completed }

/// <summary>One Windmill job row for the watched script, as the probes see it.</summary>
public sealed record WindmillJob(
    string Id,
    JobKind Kind,
    bool? Success,
    string? SchedulePath,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string Logs,
    JsonObject? Result);

public enum WindmillCallStatus { Ok, Unreachable, Unauthorized, NotFound }

public sealed record WindmillCall<T>(WindmillCallStatus Status, T? Value, string? Error = null)
{
    public static WindmillCall<T> Ok(T value) => new(WindmillCallStatus.Ok, value);
    public bool IsOk => Status == WindmillCallStatus.Ok;
}

public sealed record ScriptInfo(string Hash);
public sealed record ScheduleInfo(bool Enabled);
public sealed record WorkerPing(string WorkerGroup, DateTime LastPingUtc);

public enum CrashPoint { Intent, SendBeforeResponse, TransportAccepted, ReaderObservation }

/// <summary>Thrown by a fault hook at a qualification crash cut; the loop lets it propagate.</summary>
public sealed class WatchdogCrashException(CrashPoint point) : Exception($"injected crash at {point}")
{
    public CrashPoint Point { get; } = point;
}

public sealed class LedgerNamespaceMismatchException(string stored, string requested)
    : Exception($"ledger belongs to namespace '{stored}', not '{requested}'");

public enum OutageChange { Opened, Closed, Amended }

public sealed record OutageTransition(string Kind, string OutageId, OutageChange Change, string? Nid);

public sealed record SendRecord(string Nid, int Attempt, bool Accepted, string? MessageId, string? ErrorClass, string? Note);

public sealed record ImportOutcome(string? Nid, int? Attempt, string Reason, bool Imported);

public enum ReaderState { Eligible, Held }

public sealed record TickReport(
    IReadOnlyList<OutageTransition> Transitions,
    IReadOnlyList<SendRecord> Sends,
    IReadOnlyList<ImportOutcome> Imports,
    ReaderState ReaderState);

public sealed record OutageRow(string OutageId, string Kind, string DueDay, int Epoch, DateTime OpenedAt, DateTime? ClosedAt,
    string? JobId, string? RunId, string? FailureNid, string? RecoveryNid, string EvidenceJson);

public sealed record NotificationRow(string Nid, string Kind, string? OutageId, string? LinkedNid, string? RunId, string State,
    DateTime CreatedAt, DateTime? ReceivedAt);

public sealed record AttemptRow(string Nid, int Attempt, DateTime StartedAt, bool Accepted, DateTime? AcceptedAt, string? MessageId,
    string? ErrorClass, DateTime? RetryAfterUtc, string Body, string BodySha256)
{
    /// <summary>True once the transport returned or threw for this attempt (not a bare intent).</summary>
    public bool Sent => Accepted || ErrorClass != null;
}

public sealed record ReceiptRow(string Nid, int Attempt, string MessageId, DateTime DateUtc, string PeerHash, string TextSha256,
    DateTime? ReadObservedAt, DateTime ImportedAt);

public sealed record LastDueDay(string DueDay, string JobId, string Status, string NativeRunId, string Sha);

public sealed record HeartbeatRow(string Namespace, string InstanceId, DateTime? HeartbeatAt, string? DestinationHash,
    LastDueDay? LastDueDay, string? StateJson);

public sealed record RecipientObservation(string PeerId, string MessageId, DateTime DateUtc, string Text, bool ReadByRecipient)
{
    /// <summary>D-6 mapping of one MTProto history message; the dialog read marker gives readByRecipient.</summary>
    public static RecipientObservation From(TL.Message message, int readInboxMaxId) => new(
        message.peer_id?.ID.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
        message.id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        LondonClock.AsUtc(message.date),
        message.message ?? "",
        readInboxMaxId >= message.id);
}

public sealed record ReaderReadResult(bool IsHeld, string? HeldReason, IReadOnlyList<RecipientObservation> Observations)
{
    public static ReaderReadResult Held(string reason) => new(true, reason, []);
    public static ReaderReadResult Eligible(IReadOnlyList<RecipientObservation> observations) => new(false, null, observations);
}

public sealed record OutboundMessage(string Nid, int Attempt, string Text);

public sealed record TransportResult(bool Accepted, string? MessageId, string? ErrorClass, string? Error, int? RetryAfterSeconds = null)
{
    public static TransportResult Ok(string messageId) => new(true, messageId, null, null);
    public static TransportResult Fail(string errorClass, string error, int? retryAfterSeconds = null) =>
        new(false, null, errorClass, error, retryAfterSeconds);
}

/// <summary>Crockford base32 ULID: 48-bit millisecond time plus 80 random bits.</summary>
public static class Ulid
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string New(DateTimeOffset now)
    {
        Span<byte> bytes = stackalloc byte[16];
        var ms = now.ToUnixTimeMilliseconds();
        for (var i = 5; i >= 0; i--) { bytes[i] = (byte)(ms & 0xFF); ms >>= 8; }
        RandomNumberGenerator.Fill(bytes[6..]);
        var value = new System.Numerics.BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var chars = new char[26];
        for (var i = 25; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(value % 32)];
            value /= 32;
        }
        return new string(chars);
    }
}
