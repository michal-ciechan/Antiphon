using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0418 F-2 producer spy. The reply is deep-copied AT INVOCATION through
/// <see cref="MessagingJson.Options"/>, so a later edit of the caller's object, its attachment
/// arrays or the files those bytes came from cannot retroactively change what this recorded.
///
/// <para>Entry is not acceptance. <see cref="MethodEntries"/> counts calls; <see cref="Accepted"/>
/// counts the ones that reached the point a broker would have taken the bytes. The failure modes are
/// independently controlled because "blocked before the call" and "blocked after the broker took it"
/// are different facts about the world, and a test that cannot tell them apart cannot prove
/// CARD-0418's uncertainty handling.</para>
/// </summary>
internal sealed class OutboundProducerSpy : IAntiphonMessagingProducer
{
    private readonly object _gate = new();
    private readonly List<AcceptedRecord> _accepted = [];
    private int _methodEntries;
    private int _definiteFailuresRemaining;

    /// <summary>Awaited at method entry, before anything is accepted.</summary>
    public TaskCompletionSource? BlockBeforeInvocation { get; set; }

    /// <summary>Awaited after entry but still before acceptance.</summary>
    public TaskCompletionSource? BlockBeforeAcceptance { get; set; }

    /// <summary>Never accepts; each call throws. Demonstrable nonacceptance.</summary>
    public bool FailDefinitelyAlways { get; set; }

    /// <summary>Accepts, records, and then throws — the ambiguous shape.</summary>
    public bool ThrowAfterAcceptance { get; set; }

    /// <summary>Accepts, records, and then never returns until this is released.</summary>
    public TaskCompletionSource? HangAfterAcceptance { get; set; }

    /// <summary>An append-only sink that outlives a killed process (F-3 crash evidence).</summary>
    public string? EvidencePath { get; set; }

    public int MethodEntries => Volatile.Read(ref _methodEntries);

    public IReadOnlyList<AcceptedRecord> Accepted
    {
        get { lock (_gate) return _accepted.ToList(); }
    }

    public int AcceptedCount
    {
        get { lock (_gate) return _accepted.Count; }
    }

    /// <summary>Fails definitely for the next <paramref name="count"/> calls, then behaves normally.</summary>
    public void FailDefinitelyNext(int count) => Volatile.Write(ref _definiteFailuresRemaining, count);

    public async Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _methodEntries);
        if (BlockBeforeInvocation is { } before)
            await before.Task.WaitAsync(cancellationToken);
        if (BlockBeforeAcceptance is { } gate)
            await gate.Task.WaitAsync(cancellationToken);

        if (FailDefinitelyAlways)
            throw new OutboundDefiniteNonAcceptanceException("producer refused before acceptance");
        while (true)
        {
            var remaining = Volatile.Read(ref _definiteFailuresRemaining);
            if (remaining <= 0)
                break;
            if (Interlocked.CompareExchange(ref _definiteFailuresRemaining, remaining - 1, remaining) == remaining)
                throw new OutboundDefiniteNonAcceptanceException("producer refused before acceptance");
        }

        var json = JsonSerializer.Serialize(reply, Antiphon.Messaging.MessagingJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        var record = new AcceptedRecord(
            json,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            bytes.LongLength,
            reply.Channel,
            reply.ConversationId,
            reply.ReplyHandle,
            reply.ReplyToMessageId,
            reply.Kind.ToString(),
            reply.Attachments
                .Select(a => new AcceptedAttachment(
                    a.Name,
                    a.Mime,
                    a.Content?.LongLength ?? 0,
                    a.Content is null ? "" : Convert.ToHexStringLower(SHA256.HashData(a.Content))))
                .ToList());
        lock (_gate)
            _accepted.Add(record);
        if (EvidencePath is { } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.AppendAllTextAsync(
                path,
                JsonSerializer.Serialize(record) + Environment.NewLine,
                cancellationToken);
        }

        if (ThrowAfterAcceptance)
            throw new InvalidOperationException("ambiguous: accepted, then the call faulted");
        if (HangAfterAcceptance is { } hang)
            await hang.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Replays a killed process's accepted records from the durable sink.</summary>
    public static IReadOnlyList<AcceptedRecord> ReadEvidence(string path)
    {
        if (!File.Exists(path))
            return [];
        return File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<AcceptedRecord>(l)!)
            .ToList();
    }

    internal sealed record AcceptedRecord(
        string Json,
        string Sha256,
        long Bytes,
        string Channel,
        string? ConversationId,
        string? ReplyHandle,
        string? ReplyToMessageId,
        string Kind,
        IReadOnlyList<AcceptedAttachment> Attachments);

    internal sealed record AcceptedAttachment(string? Name, string? Mime, long Length, string Sha256);
}

/// <summary>
/// A refusal the broker made BEFORE taking the bytes. Distinct from an arbitrary exception on
/// purpose: only a demonstrable nonacceptance may be retried without risking a duplicate message.
/// </summary>
internal sealed class OutboundDefiniteNonAcceptanceException(string message) : Exception(message);
