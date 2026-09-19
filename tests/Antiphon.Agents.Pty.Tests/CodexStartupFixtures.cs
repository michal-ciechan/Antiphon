using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.Agents.Pty.Tests;

internal static class CodexStartupFixtures
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Captures = new(LoadCaptures);

    public static string DirectoryPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "CodexStartup");

    public static string P1 => Captures.Value["P-1"];
    public static string P2 => Captures.Value["P-2"];
    public static string P3 => Captures.Value["P-3"];
    public static string N1 => File.ReadAllText(Path.Combine(DirectoryPath, "n1.txt"));
    public static string N2 => File.ReadAllText(Path.Combine(DirectoryPath, "n2.txt"));

    public static string DerivedWriteTestsHint =>
        ReplaceComposer(P2, "› " + CodexStartupScreen.HintWriteTests);

    public static string BlankComposer(string screen, char glyph = '›') =>
        ReplaceComposer(screen, glyph + " ");

    public static string AsciiGlyph(string screen) =>
        ReplaceComposer(screen, "> " + ComposerContent(screen));

    public static string WithCrlf(string screen) =>
        screen.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);

    public static string WithTrailingPadding(string screen) => screen.TrimEnd() + "\n\n\n\n";

    public static string ReplaceModelValue(string screen, string modelValue)
    {
        var lines = CodexStartupScreen.NormalizeLines(screen).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains("model:", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = RegexReplaceModel(lines[i], modelValue);
                return string.Join('\n', lines);
            }
        }

        throw new InvalidOperationException("No model: row in fixture.");
    }

    public static string EraseModelValue(string screen) => ReplaceModelValue(screen, "");

    public static string ReplaceFooter(string screen, string footer)
    {
        var lines = CodexStartupScreen.NormalizeLines(screen);
        var end = lines.Length - 1;
        while (end >= 0 && lines[end].Length == 0)
            end--;
        if (end < 0)
            throw new InvalidOperationException("No footer row.");
        lines[end] = footer;
        return string.Join('\n', lines);
    }

    public static string ReplaceComposer(string screen, string composerLine)
    {
        var lines = CodexStartupScreen.NormalizeLines(screen);
        var end = lines.Length - 1;
        while (end >= 0 && lines[end].Length == 0)
            end--;
        var composerIdx = end - 1;
        if (composerIdx >= 0 && lines[composerIdx].Length == 0)
            composerIdx--;
        if (composerIdx < 0)
            throw new InvalidOperationException("No composer row.");
        lines[composerIdx] = composerLine;
        return string.Join('\n', lines);
    }

    public static string InsertBeforeComposer(string screen, string line)
    {
        var lines = CodexStartupScreen.NormalizeLines(screen).ToList();
        var composerIdx = ComposerIndex(lines);
        lines.Insert(composerIdx, line);
        return string.Join('\n', lines);
    }

    public static string InsertAfterComposer(string screen, string line)
    {
        var lines = CodexStartupScreen.NormalizeLines(screen).ToList();
        var composerIdx = ComposerIndex(lines);
        lines.Insert(composerIdx + 1, line);
        return string.Join('\n', lines);
    }

    public static string DropBanner(string screen)
    {
        var kept = CodexStartupScreen.NormalizeLines(screen)
            .Where(l => !l.Contains("OpenAI Codex", StringComparison.Ordinal))
            .ToArray();
        return string.Join('\n', kept);
    }

    public static string BannerOnly(string screen)
    {
        var lines = CodexStartupScreen.NormalizeLines(screen);
        var kept = new List<string>();
        var inBanner = false;
        foreach (var line in lines)
        {
            if (line.Contains('╭'))
                inBanner = true;
            if (inBanner)
                kept.Add(line);
            if (inBanner && line.Contains('╰'))
            {
                if (kept.Any(l => l.Contains("OpenAI Codex", StringComparison.Ordinal)))
                    break;
                inBanner = false;
                kept.Clear();
            }
        }

        return string.Join('\n', kept);
    }

    public static string HistoricalComposerAboveUnknownBottom(string screen)
    {
        return screen.TrimEnd() + "\n\n> leftover unknown bottom\n";
    }

    public static string ClipFooter(string screen)
    {
        var lines = CodexStartupScreen.NormalizeLines(screen);
        var end = lines.Length - 1;
        while (end >= 0 && lines[end].Length == 0)
            end--;
        if (end < 0 || lines[end].Length == 0)
            throw new InvalidOperationException("Footer too short to clip.");
        var footer = lines[end].Trim();
        var dot = footer.IndexOf('·');
        lines[end] = dot > 0 ? footer[..dot].TrimEnd() : footer[..Math.Min(8, footer.Length)];
        return string.Join('\n', lines);
    }

    public static string Sha256(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static int ComposerIndex(List<string> lines)
    {
        var end = lines.Count - 1;
        while (end >= 0 && lines[end].Length == 0)
            end--;
        var composerIdx = end - 1;
        if (composerIdx >= 0 && lines[composerIdx].Length == 0)
            composerIdx--;
        if (composerIdx < 0)
            throw new InvalidOperationException("No composer row.");
        return composerIdx;
    }

    private static string ComposerContent(string screen)
    {
        var lines = CodexStartupScreen.NormalizeLines(screen);
        var end = lines.Length - 1;
        while (end >= 0 && lines[end].Length == 0)
            end--;
        var composerIdx = end - 1;
        if (composerIdx >= 0 && lines[composerIdx].Length == 0)
            composerIdx--;
        CodexStartupScreen.TryParseComposer(lines[composerIdx], out _, out var content);
        return content;
    }

    private static string RegexReplaceModel(string line, string modelValue)
    {
        var idx = line.IndexOf("model:", StringComparison.OrdinalIgnoreCase);
        var prefix = line[..(idx + "model:".Length)];
        var rest = line[(idx + "model:".Length)..];
        var cut = rest.IndexOf("/model", StringComparison.OrdinalIgnoreCase);
        if (cut < 0)
            return prefix + " " + modelValue;
        var spaces = rest[..cut];
        var width = spaces.Length;
        var padded = modelValue.Length >= width
            ? modelValue
            : modelValue + new string(' ', width - modelValue.Length);
        return prefix + padded + rest[cut..];
    }

    private static IReadOnlyDictionary<string, string> LoadCaptures()
    {
        var path = Path.Combine(DirectoryPath, "captures.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var capture in doc.RootElement.GetProperty("captures").EnumerateArray())
        {
            map[capture.GetProperty("id").GetString()!] =
                capture.GetProperty("renderedScreen").GetString()!;
        }

        return map;
    }
}
