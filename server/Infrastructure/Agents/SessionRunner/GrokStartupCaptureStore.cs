using System.Globalization;
using System.Text;
using Antiphon.Agents.Pty;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>Best-effort, bounded local evidence from a failed Grok startup wait.</summary>
public static class GrokStartupCaptureStore
{
    public const int SourceLimit = 8192;
    public const int FileLimit = 128 * 1024;
    private const long DirectoryByteLimit = 50L * 1024 * 1024;
    private const string NamePrefix = "grok-startup-";
    private const string NameTimeFormat = "yyyyMMdd'T'HHmmssfff'Z'";
    private static readonly TimeSpan RetentionAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan CountGrace = TimeSpan.FromDays(1);
    private static readonly object CaptureLock = new();

    public static string ResolveDirectory(string? configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "antiphon-grok-startup") : configured;

    public static string Write(string directory, int keep, Guid sessionId,
        GrokStartupReason outcome, GrokStartupReason lastScreenReason,
        GrokStartupSnapshot? frame, TimeSpan elapsed, int positiveObservations,
        bool mcpSeen, bool signInSeen, DateTimeOffset utcNow, TimeSpan? workingCeiling = null)
    {
        Directory.CreateDirectory(directory);
        var timestamp = utcNow.UtcDateTime.ToString(NameTimeFormat, CultureInfo.InvariantCulture);
        var name = $"{NamePrefix}{timestamp}-{sessionId:N}-{Guid.NewGuid():N}.txt";
        var path = Path.Combine(directory, name);
        var content = Format(sessionId, outcome, lastScreenReason, frame, elapsed,
            positiveObservations, mcpSeen, signInSeen, utcNow, workingCeiling);
        lock (CaptureLock)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
            Prune(directory, keep, utcNow.UtcDateTime);
        }
        return path;
    }

    public static string Format(Guid sessionId, GrokStartupReason outcome,
        GrokStartupReason lastScreenReason, GrokStartupSnapshot? frame,
        TimeSpan elapsed, int positiveObservations, bool mcpSeen, bool signInSeen,
        DateTimeOffset utcNow, TimeSpan? workingCeiling = null)
    {
        var sb = new StringBuilder();
        sb.Append("session: ").Append(sessionId).Append('\n');
        sb.Append("capturedAtUtc: ").Append(utcNow.UtcDateTime.ToString("O")).Append('\n');
        sb.Append("outcome: ").Append(outcome).Append('\n');
        sb.Append("lastScreenReason: ").Append(lastScreenReason).Append('\n');
        sb.Append("elapsedMs: ").Append((long)elapsed.TotalMilliseconds).Append('\n');
        if (workingCeiling is { } ceiling)
            sb.Append("workingCeilingMs: ").Append((long)ceiling.TotalMilliseconds).Append('\n');
        sb.Append("positiveObservations: ").Append(positiveObservations).Append('\n');
        sb.Append("mcpSeen: ").Append(mcpSeen).Append('\n');
        sb.Append("frameSequence: ").Append(frame?.Sequence.ToString() ?? "none").Append('\n');
        sb.Append("frameAtUtc: ").Append(frame?.CapturedAt.ToUniversalTime().ToString("O") ?? "none").Append('\n');
        if (signInSeen)
        {
            sb.Append("content: suppressed after sign-in\n");
            return sb.ToString();
        }
        if (frame is null)
        {
            sb.Append("frame: none\n");
            return sb.ToString();
        }

        var screen = frame.RenderedScreen ?? "";
        var raw = frame.RawOutput ?? "";
        var screenPart = Prefix(screen, SourceLimit);
        var rawPart = Tail(raw, SourceLimit);
        sb.Append("screenSourceUnits: ").Append(screen.Length).Append(" truncated: ")
            .Append(screenPart.Length < screen.Length).Append('\n');
        sb.Append("rawSourceUnits: ").Append(raw.Length).Append(" truncated: ")
            .Append(rawPart.Length < raw.Length).Append('\n');
        sb.Append("--- rendered screen (controls escaped) ---\n").Append(Escape(screenPart)).Append('\n');
        sb.Append("--- raw tail (controls escaped) ---\n").Append(Escape(rawPart)).Append('\n');
        var result = sb.ToString();
        if (Encoding.UTF8.GetByteCount(result) > FileLimit)
        {
            // This is a defensive whole-file cap. The two source caps normally keep it below 128 KiB.
            result = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(result).AsSpan(0, FileLimit - 32))
                + "\nfileTruncated: true\n";
        }
        return result;
    }

    private static string Prefix(string value, int limit)
    {
        if (value.Length <= limit) return value;
        var end = limit;
        if (char.IsHighSurrogate(value[end - 1])) end--;
        return value[..end];
    }

    private static string Tail(string value, int limit)
    {
        if (value.Length <= limit) return value;
        var start = value.Length - limit;
        if (char.IsLowSurrogate(value[start])) start++;
        return value[start..];
    }

    internal static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsControl(c) || c is '\u007f' or '\u2028' or '\u2029')
                sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static void Prune(string directory, int configuredKeep, DateTime utcNow)
    {
        var keep = Math.Clamp(configuredKeep, 1, 200);
        var captures = new List<(FileInfo File, DateTime CapturedAt, long Bytes)>();
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles($"{NamePrefix}*.txt"))
        {
            if (!TryGetCaptureTime(file.Name, out var capturedAt)
                || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;
            try { captures.Add((file, capturedAt, file.Length)); }
            catch (FileNotFoundException) { }
        }

        var count = captures.Count;
        var bytes = captures.Sum(capture => capture.Bytes);
        foreach (var capture in captures.OrderBy(capture => capture.File.Name, StringComparer.Ordinal))
        {
            var age = utcNow - capture.CapturedAt;
            if (age < RetentionAge && bytes <= DirectoryByteLimit && (count <= keep || age < CountGrace))
                break;
            try
            {
                capture.File.Delete();
                count--;
                bytes -= capture.Bytes;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool TryGetCaptureTime(string name, out DateTime capturedAt)
    {
        const int timeLength = 19;
        const int idLength = 32;
        var timeEnd = NamePrefix.Length + timeLength;
        var sessionEnd = timeEnd + 1 + idLength;
        var captureEnd = sessionEnd + 1 + idLength;
        capturedAt = default;
        return name.Length == captureEnd + 4
            && name.StartsWith(NamePrefix, StringComparison.Ordinal)
            && name.EndsWith(".txt", StringComparison.Ordinal)
            && name[timeEnd] == '-' && name[sessionEnd] == '-'
            && Guid.TryParseExact(name.AsSpan(timeEnd + 1, idLength), "N", out _)
            && Guid.TryParseExact(name.AsSpan(sessionEnd + 1, idLength), "N", out _)
            && DateTime.TryParseExact(name.AsSpan(NamePrefix.Length, timeLength), NameTimeFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out capturedAt);
    }
}
