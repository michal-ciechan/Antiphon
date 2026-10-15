using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>Lossless display-only delta; never used for matching or state calculation.</summary>
internal static class TranscriptUiProjection
{
    internal static SessionRunnerTranscriptEvent Stored(TranscriptEntry r, long sequence) => new(
        r.AgentSessionId, sequence, r.Kind, r.Uuid, r.ParentUuid,
        r.Timestamp is { } time ? new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)) : null,
        r.Role, r.Text, r.ToolName, r.ToolInput, r.ToolUseId, r.ToolIsError, r.StopReason,
        r.ApiCallId, r.InputTokens, r.OutputTokens, r.CacheReadTokens, r.CacheCreationTokens,
        r.IsApiError, r.ApiErrorClass, r.ApiErrorStatus, r.Model, r.ModelCalls, r.ApiErrorTimeZoneId);

    private static string?[] Fields(SessionRunnerTranscriptEvent e) =>
        [e.Kind, e.Uuid, e.ParentUuid, e.Role, e.Text, e.ToolName, e.ToolInput,
         e.ToolUseId, e.StopReason, e.ApiCallId, e.ApiErrorClass, e.Model, e.ApiErrorTimeZoneId];

    internal static byte[] Capture(SessionRunnerTranscriptEvent original, TranscriptEntry row)
    {
        var source = Fields(original);
        var stored = Stored(row, original.Sequence);
        var target = Fields(stored);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == target[i]) continue;
            writer.Write((byte)i);
            var value = source[i];
            if (value is not null && value.Replace('\0', '\uFFFD') == target[i])
            {
                writer.Write((byte)1);
                var positions = Enumerable.Range(0, value.Length).Where(p => value[p] == '\0').ToArray();
                writer.Write(positions.Length);
                foreach (var position in positions) writer.Write(position);
            }
            else
            {
                writer.Write((byte)2);
                writer.Write(value?.Length ?? -1);
                if (value is not null)
                    foreach (var character in value) writer.Write((ushort)character);
            }
        }
        writer.Write(byte.MaxValue);
        writer.Write(original.Timestamp != stored.Timestamp
            || original.Timestamp?.Offset != stored.Timestamp?.Offset);
        if (original.Timestamp != stored.Timestamp || original.Timestamp?.Offset != stored.Timestamp?.Offset)
        {
            writer.Write(original.Timestamp.HasValue);
            if (original.Timestamp is { } timestamp)
            {
                writer.Write(timestamp.Ticks);
                writer.Write(timestamp.Offset.Ticks);
            }
        }
        return stream.ToArray();
    }

    internal static SessionRunnerTranscriptEvent Restore(TranscriptEntry row, SessionRuntimeAction action)
    {
        if (action.UiProjectionVersion != 1 || action.UiProjectionDelta.Length == 0)
            throw new InvalidDataException("Missing or unsupported transcript UI projection.");
        var entry = Stored(row, action.ProducerSequence);
        var fields = Fields(entry);
        using var stream = new MemoryStream(action.UiProjectionDelta, writable: false);
        using var reader = new BinaryReader(stream);
        var seen = new HashSet<byte>();
        for (var field = reader.ReadByte(); field != byte.MaxValue; field = reader.ReadByte())
        {
            if (field >= fields.Length || !seen.Add(field)) throw new InvalidDataException("Invalid UI field.");
            var kind = reader.ReadByte();
            var count = reader.ReadInt32();
            if (kind == 1)
            {
                var chars = fields[field]?.ToCharArray() ?? throw new InvalidDataException("Missing UI string.");
                if (count < 0 || count > chars.Length) throw new InvalidDataException("Invalid UI positions.");
                for (var i = 0; i < count; i++)
                {
                    var position = reader.ReadInt32();
                    if ((uint)position >= chars.Length || chars[position] != '\uFFFD')
                        throw new InvalidDataException("Invalid UI position.");
                    chars[position] = '\0';
                }
                fields[field] = new string(chars);
            }
            else if (kind == 2)
            {
                if (count < -1 || count > (stream.Length - stream.Position) / 2)
                    throw new InvalidDataException("Invalid UI replacement.");
                if (count == -1) fields[field] = null;
                else
                {
                    var chars = new char[count];
                    for (var i = 0; i < count; i++) chars[i] = (char)reader.ReadUInt16();
                    fields[field] = new string(chars);
                }
            }
            else throw new InvalidDataException("Invalid UI delta operation.");
        }
        var timestamp = entry.Timestamp;
        if (reader.ReadBoolean())
            timestamp = reader.ReadBoolean() ? new DateTimeOffset(reader.ReadInt64(), TimeSpan.FromTicks(reader.ReadInt64())) : null;
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing UI projection data.");
        return entry with { Kind = fields[0]!, Uuid = fields[1], ParentUuid = fields[2], Role = fields[3],
            Text = fields[4], ToolName = fields[5], ToolInput = fields[6], ToolUseId = fields[7],
            StopReason = fields[8], ApiCallId = fields[9], ApiErrorClass = fields[10], Model = fields[11],
            ApiErrorTimeZoneId = fields[12], Timestamp = timestamp };
    }
}
