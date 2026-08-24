using System.Reflection;
using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Build-time git SHA stamped into <c>AssemblyInformationalVersion</c> (CARD-0179 R3).
/// Directory.Build.props sets <c>SourceRevisionId</c> from <c>git rev-parse HEAD</c> (fallback
/// "unknown"); the SDK appends it after '+' on InformationalVersion.
/// </summary>
public static partial class AntiphonVersion
{
    public static string Informational { get; } = ResolveInformational();

    public static string Sha { get; } = ResolveSha(Informational);

    private static string ResolveInformational() =>
        typeof(AntiphonVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

    /// <summary>
    /// Prefers a 40-hex SourceLink SHA; otherwise the '+' suffix (which is "unknown" when git
    /// was not available at build); otherwise "unknown".
    /// </summary>
    public static string ResolveSha(string? informationalVersion)
    {
        var value = informationalVersion ?? string.Empty;
        var match = ShaRegex().Match(value);
        if (match.Success)
            return match.Groups[1].Value;

        var plus = value.LastIndexOf('+');
        if (plus >= 0 && plus < value.Length - 1)
        {
            var suffix = value[(plus + 1)..];
            var cut = suffix.IndexOfAny(['.', '+']);
            return cut >= 0 ? suffix[..cut] : suffix;
        }

        return "unknown";
    }

    [GeneratedRegex(@"\+([0-9a-f]{40})(?:$|[.+])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShaRegex();
}
