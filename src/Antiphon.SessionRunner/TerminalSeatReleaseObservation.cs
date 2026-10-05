using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Volatile authority for one runtime/session object. The token is random and returned only
/// after two matching fresh observations; wall time is diagnostic, never elapsed authority.
/// A conditional signal must reobserve under the launch gate.
/// </summary>
internal sealed record TerminalSeatProof(
    Guid RuntimeEpoch, object Session, TerminalSeatObservationRequest Request,
    TerminalTranscriptObservation Transcript, long InputRevision, long OutputRevision,
    long FirstTimestamp, DateTimeOffset FirstObservedAt, string Token);

internal sealed class TerminalSeatQualification
{
    internal static readonly TimeSpan SafetyMargin = TimeSpan.FromSeconds(120);
    internal TerminalSeatProof? Proof { get; private set; }

    internal void Discard() => Proof = null;

    internal TerminalSeatObservation Observe(
        Guid epoch, object session, TerminalSeatObservationRequest request,
        TerminalTranscriptObservation transcript, long inputRevision, long outputRevision,
        TimeProvider clock)
    {
        var evidence = CheckEvidence(request, transcript);
        if (evidence != TerminalSeatQualificationStatus.Qualified)
        {
            Discard();
            return new(evidence, transcript);
        }

        if (Proof is not { } previous || previous.RuntimeEpoch != epoch
            || !ReferenceEquals(previous.Session, session) || previous.Request != request
            || previous.Transcript != transcript || previous.InputRevision != inputRevision
            || previous.OutputRevision != outputRevision)
        {
            Proof = new(epoch, session, request, transcript, inputRevision, outputRevision,
                clock.GetTimestamp(), clock.GetUtcNow(), Guid.NewGuid().ToString("N"));
            // A long scheduler pause cannot turn the FIRST successful read into two reads.
            return new(TerminalSeatQualificationStatus.Waiting, transcript,
                FirstObservedAt: Proof.FirstObservedAt);
        }

        var proof = Proof!;
        var status = Authorize(proof, epoch, session, request, transcript, inputRevision, outputRevision, clock);
        return new(status, transcript,
            status == TerminalSeatQualificationStatus.Qualified ? proof.Token : null,
            clock.GetElapsedTime(proof.FirstTimestamp), proof.FirstObservedAt);
    }

    // Kept as the actual typed production decision so a guard's refusal can be witnessed
    // without a downstream dictionary miss or a different session object masking that guard.
    internal static TerminalSeatQualificationStatus Authorize(
        TerminalSeatProof proof, Guid epoch, object session, TerminalSeatObservationRequest request,
        TerminalTranscriptObservation transcript, long inputRevision, long outputRevision, TimeProvider clock)
    {
        if (proof.RuntimeEpoch != epoch)
            return TerminalSeatQualificationStatus.StaleObservation;
        if (!ReferenceEquals(proof.Session, session) || proof.Request != request
            || proof.Transcript != transcript || proof.InputRevision != inputRevision
            || proof.OutputRevision != outputRevision)
            return TerminalSeatQualificationStatus.StaleObservation;
        var evidence = CheckEvidence(request, transcript);
        if (evidence != TerminalSeatQualificationStatus.Qualified) return evidence;
        return clock.GetElapsedTime(proof.FirstTimestamp) >= SafetyMargin
            ? TerminalSeatQualificationStatus.Qualified : TerminalSeatQualificationStatus.Waiting;
    }

    // Null is authorization to proceed to the last pre-signal fence, never an exit receipt.
    // This is also the mutation oracle for individual guards otherwise masked by later fences.
    internal static TerminalSeatReleaseOutcome? AuthorizeRelease(
        TerminalSeatProof proof, Guid epoch, object session, TerminalSeatReleaseRequest request,
        TerminalTranscriptObservation transcript, long inputRevision, long outputRevision, TimeProvider clock)
    {
        if (proof.RuntimeEpoch != epoch || !ReferenceEquals(proof.Session, session)
            || proof.Request != request.Observation || proof.Token != request.Token)
            return TerminalSeatReleaseOutcome.StaleObservation;
        if (transcript.Status != TerminalTranscriptReadStatus.Success
            || transcript.Verdict == TerminalTranscriptVerdict.Unknown)
            return TerminalSeatReleaseOutcome.Unknown;
        if (transcript.Verdict == TerminalTranscriptVerdict.Working)
            return TerminalSeatReleaseOutcome.Working;
        if (proof.Transcript != transcript || proof.InputRevision != inputRevision
            || proof.OutputRevision != outputRevision
            || clock.GetElapsedTime(proof.FirstTimestamp) < SafetyMargin)
            return TerminalSeatReleaseOutcome.StaleObservation;
        return null;
    }

    private static TerminalSeatQualificationStatus CheckEvidence(
        TerminalSeatObservationRequest request, TerminalTranscriptObservation transcript)
    {
        if (transcript.Status != TerminalTranscriptReadStatus.Success
            || transcript.Verdict == TerminalTranscriptVerdict.Unknown
            || string.IsNullOrEmpty(request.PromptBindingIdentity) || request.PromptFloorRevision < 0)
            return TerminalSeatQualificationStatus.Unknown;
        if (transcript.Verdict == TerminalTranscriptVerdict.Working)
            return TerminalSeatQualificationStatus.Working;
        if (transcript.BindingIdentity != request.PromptBindingIdentity
            || transcript.LastPromptRevision is not { } prompt || prompt <= request.PromptFloorRevision
            || transcript.LastEndRevision is not { } end || end <= prompt)
            return TerminalSeatQualificationStatus.OldPrompt;
        return TerminalSeatQualificationStatus.Qualified;
    }
}

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
    private NativeFileIdentity? _boundFileIdentity;
    private long _consumedByPoll;
    private long _largestObservation;

    // Called by the real binding/poll path, not by observation. A replacement cannot be adopted
    // merely because the next observation sees an old idle transcript at the same pathname.
    internal void Bind(string path)
    {
        _boundFileIdentity = null;
        _consumedByPoll = 0;
        _largestObservation = 0;
        try
        {
            _boundFileIdentity = CaptureFile(path)?.Identity;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
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
            if (_boundFileIdentity is null || before.Identity != _boundFileIdentity
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

            var first = await ReadAsync(path, before, line => Add(normalize(line)), ct);
            if (first.Status != TerminalTranscriptReadStatus.Success) return Refuse(first.Status);
            // Grok coalesces output until turn_completed. Pending chunks are activity, not idle.
            // Flushing this PRIVATE parser does not change the live normalizer or publish rows.
            if (flushPending is not null) Add(flushPending());
            if (AfterRead is { } afterRead) await afterRead(ct);

            if (captureBinding() != binding || CaptureFile(path) != before)
                return Refuse(TerminalTranscriptReadStatus.StaleObservation);
            var verify = await ReadAsync(path, before, null, ct);
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
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "|" + before.Identity))),
                first.Digest, before.Length, entries.Count, lastEnd, lastPrompt);
        }
        catch (ReadBudgetException) { return Refuse(TerminalTranscriptReadStatus.BudgetExceeded); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
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
        string path, FileStamp expected, Action<string>? consume, CancellationToken ct)
    {
        await using var stream = OpenRead?.Invoke(path) ?? new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous);
        if (stream is not FileStream file || CaptureHandle(file.SafeFileHandle) != expected)
            return (TerminalTranscriptReadStatus.StaleObservation, null);
        var length = expected.Length;
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
        if (CaptureHandle(file.SafeFileHandle) != expected)
            return (TerminalTranscriptReadStatus.StaleObservation, null);
        return (TerminalTranscriptReadStatus.Success, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static TerminalTranscriptObservation Refuse(TerminalTranscriptReadStatus status) =>
        new(status, TerminalTranscriptVerdict.Unknown);

    private static FileStamp? CaptureFile(string path)
    {
        if (!File.Exists(path)) return null;
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return CaptureHandle(handle);
    }

    private static FileStamp CaptureHandle(SafeFileHandle handle)
    {
        NativeFileIdentity identity;
        if (OperatingSystem.IsLinux())
        {
            // Same stable statx ABI as AgentPinPosixReader; AT_EMPTY_PATH inspects this
            // opened descriptor. Birth/creation timestamps are not file identity on Linux.
            const uint requested = 0x101; // STATX_TYPE | STATX_INO
            if (Statx(handle, "", 0x1000, requested, out var stat) != 0
                || (stat.Mask & requested) != requested || (stat.Mode & 0xf000) != 0x8000)
                throw new IOException("Cannot identify native transcript file.");
            identity = new(((ulong)stat.DeviceMajor << 32) | stat.DeviceMinor, stat.Inode, 0);
        }
        else if (OperatingSystem.IsWindows())
        {
            // Volume serial + 128-bit FILE_ID_INFO, stable across appends and renames.
            if (!GetFileInformationByHandleEx(handle, 18, out var id, (uint)Marshal.SizeOf<WindowsFileId>()))
                throw new IOException("Cannot identify native transcript file.");
            identity = new(id.Volume, id.Low, id.High);
        }
        else throw new PlatformNotSupportedException("Native transcript identity is unavailable.");
        return new(identity, File.GetLastWriteTimeUtc(handle), RandomAccess.GetLength(handle));
    }

    private sealed record NativeFileIdentity(ulong Volume, ulong Low, ulong High);
    private sealed record FileStamp(NativeFileIdentity Identity, DateTime Written, long Length);
    private sealed class ReadBudgetException : Exception;

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxInfo
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileId
    {
        public ulong Volume;
        public ulong Low;
        public ulong High;
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(SafeFileHandle file, string path, int flags, uint mask, out StatxInfo result);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file, int informationClass, out WindowsFileId result, uint size);
}
