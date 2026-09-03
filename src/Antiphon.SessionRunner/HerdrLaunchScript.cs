using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0187: BOM'd PowerShell launch script typed into the pane instead of <c>agent.start</c>.
/// Single-quoted arguments with <c>'</c> doubled so newlines, <c>$</c>, backticks and double quotes
/// reach the child byte-identical (probe K7).
///
/// CARD-0341: the launch env enters the script. A relaunch into a standing pane (CARD-0224) types
/// only this file — <c>tab.create</c> / <c>pane.split</c> env never reaches a reused shell — so
/// every <c>request.Env</c> entry is applied as a <c>Set-Item -LiteralPath 'Env:NAME'</c> line
/// before the command, and names the previous launch set but this one does not carry are removed
/// first. Whole-argument <c>$env:NAME</c> tokens are resolved from the env before quoting, because
/// PowerShell never expands them inside a single-quoted argument. A script kept on failure is
/// rewritten with <see cref="RedactedValue"/> in place of every value.
/// </summary>
internal static partial class HerdrLaunchScript
{
    public const string FileSuffix = ".launch.ps1";

    /// <summary>What a kept-on-failure script shows in place of each env value.</summary>
    public const string RedactedValue = "<redacted>";

    internal static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static string PathFor(string sessionLogPath, Guid sessionId) =>
        Path.Combine(HerdrPaneSidecar.DirectoryFor(sessionLogPath), $"{sessionId:N}{FileSuffix}");

    /// <summary>PowerShell single-quoted literal; embedded quotes doubled.</summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    public static string BuildContent(
        string exe,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        string? workingDirectory = null,
        IReadOnlyCollection<string>? clearNames = null,
        bool redactEnv = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(exe);
        args ??= Array.Empty<string>();

        var lines = new List<string>();

        if (clearNames is { Count: > 0 })
        {
            foreach (var name in clearNames.Where(n => !string.IsNullOrEmpty(n)).Order(StringComparer.Ordinal))
                lines.Add($"Remove-Item -LiteralPath {Quote("Env:" + name)} -ErrorAction SilentlyContinue");
        }

        if (env is { Count: > 0 })
        {
            foreach (var (name, value) in env.Where(kv => !string.IsNullOrEmpty(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var rendered = redactEnv ? RedactedValue : value ?? "";
                lines.Add($"Set-Item -LiteralPath {Quote("Env:" + name)} -Value {Quote(rendered)}");
            }
        }

        if (!string.IsNullOrEmpty(workingDirectory))
            lines.Add($"Set-Location -LiteralPath {Quote(workingDirectory)}");

        var quoted = new string[args.Count];
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i] ?? "";
            if (!redactEnv && env is not null && TryResolveEnvToken(arg, env, out var resolved))
                arg = resolved;
            quoted[i] = Quote(arg);
        }

        lines.Add($"& {Quote(exe)} @({string.Join(", ", quoted)})");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// CARD-0341: a whole argument of the form <c>$env:NAME</c> or <c>${env:NAME}</c> resolves to
    /// the env value when the name is present (exact case first, then case-insensitive — Windows
    /// env names are case-insensitive). Anything else, including a token whose name the env does
    /// not carry, is left verbatim.
    /// </summary>
    internal static bool TryResolveEnvToken(
        string argument,
        IReadOnlyDictionary<string, string> env,
        out string value)
    {
        value = "";
        if (!TryReadEnvTokenName(argument, out var name))
            return false;

        if (env.TryGetValue(name, out var exact))
        {
            value = exact ?? "";
            return true;
        }

        foreach (var (key, candidate) in env)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate ?? "";
                return true;
            }
        }

        return false;
    }

    /// <summary>The NAME of a whole-argument <c>$env:NAME</c> / <c>${env:NAME}</c> token; false otherwise.</summary>
    internal static bool TryReadEnvTokenName(string argument, out string name)
    {
        name = "";
        if (string.IsNullOrEmpty(argument))
            return false;

        var match = EnvToken().Match(argument);
        if (!match.Success)
            return false;

        name = match.Groups["braced"].Success ? match.Groups["braced"].Value : match.Groups["bare"].Value;
        return name.Length > 0;
    }

    [GeneratedRegex(@"^\$(?:\{[Ee][Nn][Vv]:(?<braced>[^}]+)\}|[Ee][Nn][Vv]:(?<bare>[A-Za-z_][A-Za-z0-9_]*))$")]
    private static partial Regex EnvToken();

    /// <summary>The constant-length line typed into the pane: <c>&amp; '&lt;path&gt;'</c>.</summary>
    public static string TypedCommand(string scriptPath) => $"& {Quote(scriptPath)}";

    public static void Write(
        string path,
        string exe,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null,
        string? workingDirectory = null,
        IReadOnlyCollection<string>? clearNames = null,
        bool redactEnv = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, BuildContent(exe, args, env, workingDirectory, clearNames, redactEnv), Utf8Bom);
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
