using System.Globalization;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Incremental read of ONE candidate transcript, collecting just the facts the adoption rules need
/// (CARD-0006): its recorded <c>cwd</c> (C2), its <c>agentName</c> meta record (C2b), the first
/// TIMESTAMPED record (C3), and whether any user prompt — or a queued delivery of the same text
/// (CARD-0064) — matches input this session actually sent (C4).
///
/// Incremental because discovery polls four times a second and a candidate can be tens of MB: the
/// file is append-only, so each pass reads only the bytes added since the last one. The C4 check
/// is re-run against the most recent prompts on every pass, because the input log grows too — a
/// candidate can legitimately go from "not yet provable" to "proven" as records flush.
/// </summary>
internal sealed class TranscriptCandidateProbe
{
    private const int MaxRetainedPrompts = 32;

    /// <summary>
    /// Bytes read per pass while only establishing whose conversation this is. Every transcript in
    /// the projects root is a candidate until its cwd is known, and the root holds every project on
    /// the machine — reading them whole to answer "is this even our cwd?" would be gratuitous. The
    /// cwd sits in the first record, so this is one read for almost every file.
    /// </summary>
    public const int LeadScanBytes = 64 * 1024;

    /// <summary>Bytes per pass once the cwd matched and the file is worth scanning for prompts.</summary>
    public const int DeepScanBytes = 1 << 20; // 1 MiB

    private readonly List<byte> _pending = new();
    private readonly List<string> _recentPrompts = new();
    private long _offset;

    public TranscriptCandidateProbe(string path) => Path = path;

    public string Path { get; }

    /// <summary>The <c>cwd</c> field of the first record that carries one (rule C2).</summary>
    public string? Cwd { get; private set; }

    /// <summary>
    /// The <c>agentName</c> of the first record that carries one (rule C2b). Only this exact field
    /// is read: the <c>custom-title</c> shape has not been verified against a live transcript, and a
    /// guess there would produce FALSE REJECTS of legitimate transcripts. Absence stays neutral.
    /// </summary>
    public string? AgentName { get; private set; }

    /// <summary>
    /// The first record carrying a timestamp (rule C3). Real transcripts open with untimestamped
    /// meta records (<c>last-prompt</c>, <c>custom-title</c>, <c>agent-name</c>, <c>mode</c>,
    /// <c>permission-mode</c>), so "the first record" is not the same question.
    /// </summary>
    public DateTimeOffset? FirstTimestamp { get; private set; }

    /// <summary>True once a non-command user prompt in this file was proven to be input we sent.</summary>
    public bool ContentMatched { get; private set; }

    /// <summary>True when the file has produced at least one parseable record.</summary>
    public bool HasRecords { get; private set; }

    /// <summary>
    /// Reads up to <paramref name="maxBytes"/> of whatever has been appended since the last pass and
    /// re-tests C4. Returns false if the file could not be read at all (deleted, locked) — the caller
    /// treats that as "not a candidate this pass" rather than a refusal.
    ///
    /// Reading is incremental across passes (the file is append-only), so discovery polling four
    /// times a second costs one read of each byte over the candidate's lifetime, not one per poll.
    /// </summary>
    public bool Refresh(SessionInputLog? inputLog, int maxBytes = DeepScanBytes)
    {
        try
        {
            var info = new FileInfo(Path);
            if (!info.Exists)
                return false;

            if (info.Length < _offset)
            {
                // Truncated/replaced under us — start over rather than decode garbage.
                Reset();
            }

            if (info.Length > _offset)
            {
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
        AgentName = null;
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

            if (Cwd is null && root.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String)
                Cwd = cwd.GetString();

            if (AgentName is null
                && root.TryGetProperty("agentName", out var name)
                && name.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(name.GetString()))
            {
                AgentName = name.GetString();
            }

            if (FirstTimestamp is null
                && root.TryGetProperty("timestamp", out var ts)
                && ts.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(
                    ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                FirstTimestamp = parsed;
            }

            // CARD-0064: C4-only. A brief typed into a mid-turn composer lands as queue-operation
            // / queued_command, never as a user record. Harvest here, not in TranscriptNormalizer —
            // a queued-but-unsubmitted body must not become a UserPrompt for CARD-0055 confirmation.
            HarvestQueuedDelivery(root);
        }

        RememberPromptText(line);
    }

    // Prompts are extracted through the SAME normalizer the tail loop uses, so C4 compares exactly
    // the text a bound transcript would emit. Local slash-command records are excluded: they are
    // echoes of a /clear or /model the operator typed, not evidence of whose session this is.
    private void RememberPromptText(string line)
    {
        IReadOnlyList<TranscriptPart> parts;
        try { parts = TranscriptNormalizer.Normalize(line); }
        catch (Exception) { return; }

        foreach (var part in parts)
        {
            if (part.Kind != TranscriptKinds.UserPrompt || string.IsNullOrWhiteSpace(part.Text))
                continue;
            RememberPrompt(part.Text!);
        }
    }

    /// <summary>
    /// Pulls delivered-text evidence out of the two record kinds Claude writes when the composer
    /// queues a body instead of submitting it. Either <c>enqueue</c> or <c>remove</c> carries the
    /// full body; a <c>queued_command</c> attachment carries it as <c>attachment.prompt</c>.
    /// Other attachment types are ignored.
    /// </summary>
    private void HarvestQueuedDelivery(JsonElement root)
    {
        var type = GetString(root, "type");
        string? text = type switch
        {
            "queue-operation" => GetString(root, "content"),
            "attachment" when root.TryGetProperty("attachment", out var att)
                && att.ValueKind == JsonValueKind.Object
                && GetString(att, "type") == "queued_command"
                => GetString(att, "prompt"),
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(text))
            RememberPrompt(text);
    }

    private void RememberPrompt(string text)
    {
        if (TranscriptKinds.IsLocalCommandRecord(TranscriptKinds.UserPrompt, text))
            return;
        if (TranscriptKinds.IsInterruptPrompt(TranscriptKinds.UserPrompt, text))
            return;

        _recentPrompts.Add(text);
        if (_recentPrompts.Count > MaxRetainedPrompts)
            _recentPrompts.RemoveAt(0);
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(prop, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
