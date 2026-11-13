using System.Text.RegularExpressions;

namespace Antiphon.Server.Infrastructure.Data;

// CARD-1886. Pure startup-option policy for application connections.
// Hangfire and other raw administrative connections do not use this helper.
public static class ApplicationPostgresConnection
{
    public const string JitOffOption = "-c jit=off";

    // Last -c jit= assignment wins. An earlier jit=on stays in the string.
    private static readonly Regex JitAssignment = new(
        @"(?:^|\s)-c\s+jit\s*=\s*(?<value>\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? ApplyPolicy(string? connectionString, bool disableJit)
    {
        if (!disableJit || string.IsNullOrEmpty(connectionString))
            return connectionString;
        return SuppressJit(connectionString);
    }

    public static string SuppressJit(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var options = builder.Options ?? "";
        if (!LastJitIsOff(options))
        {
            builder.Options = options.Length == 0
                ? JitOffOption
                : options.TrimEnd() + " " + JitOffOption;
        }

        return builder.ConnectionString;
    }

    private static bool LastJitIsOff(string options)
    {
        var last = "";
        foreach (Match match in JitAssignment.Matches(options))
            last = match.Groups["value"].Value;
        return last.Equals("off", StringComparison.OrdinalIgnoreCase);
    }
}
