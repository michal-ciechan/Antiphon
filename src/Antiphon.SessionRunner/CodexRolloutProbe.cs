using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Incremental read of ONE candidate Codex rollout, collecting exactly the facts the CARD-0006
/// adoption rules need: its recorded <c>cwd</c> (C2), its first TIMESTAMPED record (C3), and
/// whether any user prompt in it matches input this session actually sent (C4). The Codex twin of
/// <see cref="TranscriptCandidateProbe"/>.
///
/// <para>Two things are cheaper here than they are for Claude, and both come from
/// <c>session_meta</c> being line 0 of every rollout: the cwd is a real recorded field rather than
/// something inferred from a directory name, and the session's own start timestamp is on the same
/// record. So C2 and C3 are EXACT for Codex — they are not heuristics that happen to work.</para>
///
/// <para>Prompts are extracted through the SAME <see cref="CodexTranscriptNormalizer"/> the tail
/// loop uses, so C4 compares precisely the text a bound rollout would emit — including the
/// thread-item dialect the TUI writes, which is the only dialect an Antiphon-launched session
/// ever produces.</para>
///
/// <para>Incremental because discovery polls several times a second and a rollout can be
/// megabytes: the file is append-only, so each pass reads only what was added since the last one.
/// C4 is re-run on every pass, because the input log grows too — a candidate can legitimately go
/// from "not yet provable" to "proven" as rows are flushed.</para>
/// </summary>
internal sealed class CodexRolloutProbe
{
    private const int MaxRetainedPrompts = 32;

    /// <summary>
    /// Bytes read per pass while only establishing whose session this is. Every rollout under
    /// CODEX_HOME is a candidate until its cwd is known, and that root holds every Codex session
    /// ever run on the machine. <c>session_meta</c> is line 0 but it is FAT — 18 KB of base
    /// instructions was measured on 0.147.0 — so the lead has to clear it in one read or the cwd
    /// never arrives.
    /// </summary>
    public const int LeadScanBytes = 64 * 1024;

    /// <summary>Bytes per pass once the cwd matched and the file is worth scanning for prompts.</summary>
    public const int DeepScanBytes = 1 << 20; // 1 MiB

    private readonly CodexTranscriptNormalizer _normalizer = new();
    private readonly List<byte> _pending = new();
    private readonly List<string> _recentPrompts = new();
    private long _offset;

    public CodexRolloutProbe(string path) => Path = path;

    public string Path { get; }

    /// <summary><c>session_meta.cwd</c> — rule C2, a recorded fact rather than an inference.</summary>
    public string? Cwd { get; private set; }

    /// <summary><c>session_meta.session_id</c>, for the refusal detail and diagnostics.</summary>
    public string? RolloutSessionId { get; private set; }

    /// <summary>
    /// <c>session_meta.originator</c> (<c>codex-tui</c>, <c>codex_exec</c>, <c>Codex Desktop</c>).
    /// Reported in refusals so an operator can see WHOSE session was refused, deliberately NOT
    /// used as a gate: a human running <c>codex</c> in the same checkout produces the same
    /// originator we do, so it separates nothing, and gating on it could only ever false-reject.
    /// </summary>
    public string? Originator { get; private set; }

    /// <summary>The first record carrying a timestamp — rule C3.</summary>
    public DateTimeOffset? FirstTimestamp { get; private set; }

    /// <summary>True once a user prompt in this file was proven to be input we sent (C4).</summary>
    public bool ContentMatched { get; private set; }

    /// <summary>True when the file has produced at least one parseable record.</summary>
    public bool HasRecords { get; private set; }

    /// <summary>
    /// Reads up to <paramref name="maxBytes"/> of whatever was appended since the last pass and
    /// re-tests C4. Returns false when the file could not be read at all — the caller treats that
    /// as "not a candidate this pass" rather than a refusal.
    /// </summary>
    public bool Refresh(SessionInputLog? inputLog, int maxBytes = DeepScanBytes)
    {
        try
        {
            var info = new FileInfo(Path);
            if (!info.Exists)
                return false;

            if (info.Length < _offset)
                Reset(); // truncated/replaced under us — start over rather than decode garbage

            if (info.Length > _offset)
            {
                // Codex holds its rollout open for the whole session: a plain File.ReadLines on a
                // live one throws "being used by another process" (measured 2026-08-20). Every
                // read of a rollout, here and in the tailer, must share write AND delete.
                using var fs = new FileStream(
                    Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fs.Seek(_offset, SeekOrigin.Begin);
                var len = (int)Math.Min(info.Length - _offset, Math.Max(1, maxBytes));
                var buffer = new byte[len];
                var read = fs.Read(buffer, 0, len);
                if (read > 0)
                {
                    _offset += read;
                    _pending.AddRange(read == buffer.Length ? buffer : buffer[..read]);
                    ConsumePending();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (!ContentMatched && inputLog is not null)
        {
            foreach (var prompt in _recentPrompts)
            {
                if (inputLog.MatchesRecordedInput(prompt))
                {
                    ContentMatched = true;
                    break;
                }
            }
        }

        return true;
    }

    private void Reset()
    {
        _offset = 0;
        _pending.Clear();
        _recentPrompts.Clear();
        Cwd = null;
        RolloutSessionId = null;
        Originator = null;
        FirstTimestamp = null;
        ContentMatched = false;
        HasRecords = false;
    }

    private void ConsumePending()
    {
        var start = 0;
        for (var i = 0; i < _pending.Count; i++)
        {
            if (_pending[i] != (byte)'\n')
                continue;

            var count = i - start;
            if (count > 0)
                ConsumeLine(Encoding.UTF8.GetString(_pending.GetRange(start, count).ToArray()).TrimEnd('\r'));
            start = i + 1;
        }

        if (start > 0)
            _pending.RemoveRange(0, start);
    }

    private void ConsumeLine(string line)
    {
        if (line.Length == 0)
            return;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { return; } // partial line mid-write, or not JSON

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return;

            HasRecords = true;

            if (FirstTimestamp is null && CodexTranscriptNormalizer.GetTimestamp(root) is { } ts)
                FirstTimestamp = ts;

            if (Cwd is null
                && GetString(root, "type") == "session_meta"
                && root.TryGetProperty("payload", out var meta)
                && meta.ValueKind == JsonValueKind.Object)
            {
                Cwd = GetString(meta, "cwd");
                RolloutSessionId = GetString(meta, "session_id") ?? GetString(meta, "id");
                Originator = GetString(meta, "originator");
            }
        }

        RememberPromptText(line);
    }

    private void RememberPromptText(string line)
    {
        IReadOnlyList<TranscriptPart> parts;
        try { parts = _normalizer.Normalize(line); }
        catch (Exception) { return; }

        foreach (var part in parts)
        {
            if (part.Kind != TranscriptKinds.UserPrompt || string.IsNullOrWhiteSpace(part.Text))
                continue;

            _recentPrompts.Add(part.Text!);
            if (_recentPrompts.Count > MaxRetainedPrompts)
                _recentPrompts.RemoveAt(0);
        }
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(prop, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
