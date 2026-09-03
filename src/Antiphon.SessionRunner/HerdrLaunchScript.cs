using System.Text;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0187: BOM'd PowerShell launch script typed into the pane instead of <c>agent.start</c>.
/// Single-quoted arguments with <c>'</c> doubled so newlines, <c>$</c>, backticks and double quotes
/// reach the child byte-identical (probe K7). Env never enters the script.
/// </summary>
internal static class HerdrLaunchScript
{
    public const string FileSuffix = ".launch.ps1";

    internal static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static string PathFor(string sessionLogPath, Guid sessionId) =>
        Path.Combine(HerdrPaneSidecar.DirectoryFor(sessionLogPath), $"{sessionId:N}{FileSuffix}");

    /// <summary>PowerShell single-quoted literal; embedded quotes doubled.</summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    public static string BuildContent(string exe, IReadOnlyList<string> args, string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(exe);
        args ??= Array.Empty<string>();
        var quoted = new string[args.Count];
        for (var i = 0; i < args.Count; i++)
            quoted[i] = Quote(args[i] ?? "");
        var command = $"& {Quote(exe)} @({string.Join(", ", quoted)})";
        if (string.IsNullOrEmpty(workingDirectory))
            return command;
        return $"Set-Location -LiteralPath {Quote(workingDirectory)}\n{command}";
    }

    /// <summary>The constant-length line typed into the pane: <c>&amp; '&lt;path&gt;'</c>.</summary>
    public static string TypedCommand(string scriptPath) => $"& {Quote(scriptPath)}";

    public static void Write(string path, string exe, IReadOnlyList<string> args, string? workingDirectory = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, BuildContent(exe, args, workingDirectory), Utf8Bom);
    }

    public static bool IsTypedCommand(string text)
    {
        const string prefix = "& '";
        if (text.Length < prefix.Length + 1
            || !text.StartsWith(prefix, StringComparison.Ordinal)
            || text[^1] != '\'')
            return false;

        var inner = text[prefix.Length..^1].Replace("''", "'", StringComparison.Ordinal);
        return inner.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase);
    }
}
