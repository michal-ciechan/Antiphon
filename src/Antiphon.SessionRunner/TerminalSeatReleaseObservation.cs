using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Owns read serialization, never the ingestion cursor. Each observation uses a private parser
/// and bounded streaming passes: parse the complete file, then verify the consumed bytes still
/// name the same file. A successful read supplies evidence only, never permission to release.
/// </summary>
internal sealed class TerminalSeatReleaseObservation
{
    internal const long MaximumBytes = 8 * 1024 * 1024;
    private const int MaximumRecordBytes = 1024 * 1024;
    private const int MaximumParts = 100_000;
    internal SemaphoreSlim ReadGate { get; } = new(1, 1);
    internal Func<CancellationToken, Task>? BeforePoll { get; set; }
    internal Func<CancellationToken, Task>? AfterRead { get; set; }
    internal Func<string, Stream>? OpenRead { get; set; }
    private DateTime? _boundCreationUtc;
    private long _consumedByPoll;
    private long _largestObservation;

    // Called by the real binding/poll path, not by observation. A replacement cannot be adopted
    // merely because the next observation sees an old idle transcript at the same pathname.
    internal void Bind(string path)
    {
        _boundCreationUtc = null;
        _consumedByPoll = 0;
        _largestObservation = 0;
        try
        {
            var info = new FileInfo(path);
            if (info.Exists) _boundCreationUtc = info.CreationTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal void RecordConsumed(long offset) => _consumedByPoll = Math.Max(_consumedByPoll, offset);

    internal async Task<TerminalTranscriptObservation> ObserveAsync(
        Func<(string Path, string Identity)?> captureBinding,
        Func<string, IReadOnlyList<TranscriptPart>> normalize,
        Func<IReadOnlyList<TranscriptPart>>? flushPending,
        CancellationToken ct)
    {
        await ReadGate.WaitAsync(ct);
        try
        {
            var binding = captureBinding();
            if (binding is null) return Refuse(TerminalTranscriptReadStatus.Unbound);
            var (path, identity) = binding.Value;
            var before = CaptureFile(path);
            if (before is null) return Refuse(TerminalTranscriptReadStatus.Unavailable);
            if (_boundCreationUtc is null || before.Created != _boundCreationUtc
                || before.Length < Math.Max(_consumedByPoll, _largestObservation))
                return Refuse(TerminalTranscriptReadStatus.StaleObservation);
            _largestObservation = before.Length;
            if (before.Length > MaximumBytes) return Refuse(TerminalTranscriptReadStatus.BudgetExceeded);

            var entries = new List<RunnerTranscriptEvent>();
            long? lastEnd = null, lastPrompt = null;
            void Add(IReadOnlyList<TranscriptPart> parts)
            {
                foreach (var part in parts)
                {
                    if (entries.Count >= MaximumParts) throw new ReadBudgetException();
                    var evt = new RunnerTranscriptEvent(Guid.Empty, entries.Count + 1, part.Kind,
                        part.Uuid, part.ParentUuid, part.Timestamp, part.Role, part.Text,
                        part.ToolName, part.ToolInput, part.ToolUseId, part.ToolIsError, part.StopReason);
                    entries.Add(evt);
                    // Reuse the file-order classifier, including interrupt/manual compaction rules.
                    if (TranscriptWorkingState.Classify([evt]) == TranscriptWorkingState.WorkingVerdict.Idle)
                        lastEnd = evt.Sequence;
                    if (evt.Kind == TranscriptKinds.UserPrompt
                        && !TranscriptKinds.IsCompactionContinuationPrompt(evt.Kind, evt.Text)
                        && !TranscriptKinds.IsLocalCommandRecord(evt.Kind, evt.Text))
                        lastPrompt = evt.Sequence;
                }
            }

            var first = await ReadAsync(path, before.Length, line => Add(normalize(line)), ct);
            if (first.Status != TerminalTranscriptReadStatus.Success) return Refuse(first.Status);
            // Grok coalesces output until turn_completed. Pending chunks are activity, not idle.
            // Flushing this PRIVATE parser does not change the live normalizer or publish rows.
            if (flushPending is not null) Add(flushPending());
            if (AfterRead is { } afterRead) await afterRead(ct);

            if (captureBinding() != binding || CaptureFile(path) != before)
                return Refuse(TerminalTranscriptReadStatus.StaleObservation);
            var verify = await ReadAsync(path, before.Length, null, ct);
            if (verify.Status != TerminalTranscriptReadStatus.Success
                || verify.Digest != first.Digest
                || captureBinding() != binding || CaptureFile(path) != before)
                return Refuse(TerminalTranscriptReadStatus.StaleObservation);

            var verdict = TranscriptWorkingState.Classify(entries) switch
            {
                TranscriptWorkingState.WorkingVerdict.Idle => TerminalTranscriptVerdict.Idle,
                TranscriptWorkingState.WorkingVerdict.Working => TerminalTranscriptVerdict.Working,
                _ => TerminalTranscriptVerdict.Unknown
            };
            return new(TerminalTranscriptReadStatus.Success, verdict,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "|" + before.Created.Ticks))),
                first.Digest, before.Length, entries.Count, lastEnd, lastPrompt);
        }
        catch (ReadBudgetException) { return Refuse(TerminalTranscriptReadStatus.BudgetExceeded); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refuse(TerminalTranscriptReadStatus.Unavailable);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidOperationException)
        {
            return Refuse(TerminalTranscriptReadStatus.Malformed);
        }
        finally { ReadGate.Release(); }
    }

    private async Task<(TerminalTranscriptReadStatus Status, string? Digest)> ReadAsync(
        string path, long length, Action<string>? consume, CancellationToken ct)
    {
        await using var stream = OpenRead?.Invoke(path) ?? new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var record = new MemoryStream();
        var utf8 = new UTF8Encoding(false, true);
        var buffer = new byte[64 * 1024];
        long consumed = 0;
        while (consumed < length)
        {
            ct.ThrowIfCancellationRequested();
            var count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - consumed)), ct);
            if (count == 0) return (TerminalTranscriptReadStatus.StaleObservation, null);
            consumed += count;
            hash.AppendData(buffer, 0, count);
            if (consume is null) continue;
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] != (byte)'\n')
                {
                    if (record.Length >= MaximumRecordBytes) throw new ReadBudgetException();
                    record.WriteByte(buffer[i]);
                    continue;
                }
                var line = utf8.GetString(record.GetBuffer(), 0, (int)record.Length).TrimEnd('\r');
                record.SetLength(0);
                if (string.IsNullOrWhiteSpace(line)) continue;
                // Normalizers deliberately swallow malformed JSON. Destructive-path evidence must
                // instead refuse the entire read; accepting the older prefix would certify idle.
                using var parsed = JsonDocument.Parse(line);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                    return (TerminalTranscriptReadStatus.Malformed, null);
                consume(line);
            }
        }
        if (record.Length > 0) return (TerminalTranscriptReadStatus.Partial, null);
        // Growth beyond our initial bounded extent also refuses, even if the new row is complete.
        if (await stream.ReadAsync(buffer.AsMemory(0, 1), ct) != 0)
            return (TerminalTranscriptReadStatus.StaleObservation, null);
        return (TerminalTranscriptReadStatus.Success, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static TerminalTranscriptObservation Refuse(TerminalTranscriptReadStatus status) =>
        new(status, TerminalTranscriptVerdict.Unknown);

    private static FileStamp? CaptureFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new(info.CreationTimeUtc, info.LastWriteTimeUtc, info.Length) : null;
    }

    private sealed record FileStamp(DateTime Created, DateTime Written, long Length);
    private sealed class ReadBudgetException : Exception;
}
