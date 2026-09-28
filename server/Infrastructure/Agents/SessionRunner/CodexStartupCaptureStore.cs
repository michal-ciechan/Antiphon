using System.Text;
using Antiphon.Agents.Pty;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-0777: stores the last frame a failed Codex readiness wait observed, so a timeout can be
/// diagnosed from what the terminal actually showed rather than guessed. CARD-0772's three dead
/// desktop sessions left only <c>reason=Unknown</c>; the screen that explained them (an update
/// modal the gate did not classify) was gone with the runner session. The file holds the rendered
/// screen verbatim and the raw output tail with control bytes made visible. The caller logs only
/// the returned path, never the contents (CARD-0574 R-56).
/// </summary>
public static class CodexStartupCaptureStore
{
    public const int RawTailChars = 16_384;

    public static string ResolveDirectory(string? configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "antiphon-codex-startup")
            : configured;

    public static string Write(
        string directory,
        int keep,
        Guid sessionId,
        CodexStartupSnapshot? frame,
        string? diagnostic,
        DateTimeOffset utcNow)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory, $"codex-startup-{utcNow.UtcDateTime:yyyyMMddTHHmmssfffZ}-{sessionId:N}.txt");
        File.WriteAllText(path, Format(sessionId, frame, diagnostic, utcNow), new UTF8Encoding(false));
        Prune(directory, keep);
        return path;
    }

    public static string Format(
        Guid sessionId, CodexStartupSnapshot? frame, string? diagnostic, DateTimeOffset utcNow)
    {
        var sb = new StringBuilder();
        sb.Append("session: ").Append(sessionId).Append('\n');
        sb.Append("capturedAtUtc: ").Append(utcNow.UtcDateTime.ToString("O")).Append('\n');
        sb.Append("diagnostic: ").Append(diagnostic ?? "(none)").Append('\n');
        if (frame is null)
        {
            sb.Append("frame: none (no runner snapshot was read before the wait ended)\n");
            return sb.ToString();
        }

        var observation = CodexStartupScreen.Classify(frame.RenderedScreen, frame.RawOutput);
        sb.Append("classifiedAs: ").Append(observation.Reason).Append('\n');
        sb.Append("--- rendered screen ---\n");
        sb.Append(frame.RenderedScreen ?? "").Append('\n');
        var raw = frame.RawOutput ?? "";
        var tail = raw.Length > RawTailChars ? raw[^RawTailChars..] : raw;
        sb.Append("--- raw output tail (").Append(tail.Length).Append(" of ").Append(raw.Length)
            .Append(" chars, control bytes escaped) ---\n");
        sb.Append(EscapeControl(tail)).Append('\n');
        return sb.ToString();
    }

    internal static string EscapeControl(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == '\n')
                sb.Append("\\n\n");
            else if (c == '\r')
                sb.Append("\\r");
            else if (c == '\x1b')
                sb.Append("\\e");
            else if (char.IsControl(c))
                sb.Append("\\x").Append(((int)c).ToString("x2"));
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static void Prune(string directory, int keep)
    {
        if (keep <= 0)
            return;
        var stale = new DirectoryInfo(directory)
            .GetFiles("codex-startup-*.txt")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(keep);
        foreach (var file in stale)
        {
            try { file.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
