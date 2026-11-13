using System.Text;
using Npgsql;
using Serilog;

namespace Antiphon.Server.Infrastructure.Data;

// CARD-1886. Pure startup-option policy for application connections.
// Hangfire and other raw administrative connections do not use this helper.
// Options are split the way PostgreSQL pg_split_opts does: a backslash escapes
// the next character, an unescaped space separates tokens, and "--" ends options.
// "-c jit=off" is inserted before a "--" terminator, or appended when the
// existing text can take another token. A trailing unmatched backslash would
// consume an appended separator. When a later token still assigns jit, the
// original string is kept and one warning is logged. Otherwise the suppression
// is prefixed so the existing token text stays intact. A second call is a no-op.
public static class ApplicationPostgresConnection
{
    public const string JitOffOption = "-c jit=off";
    private static int _unappliedWarning;

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
        if (options.Length == 0)
        {
            builder.Options = JitOffOption;
            return builder.ConnectionString;
        }

        var scan = ScanOptions(options);
        if (EndsWithOwnTokens(scan.OptionTokens) && !scan.IncompleteEscape)
            return connectionString;

        if (scan.TerminatorStart >= 0)
        {
            builder.Options = options.Insert(scan.TerminatorStart, JitOffOption + " ");
            return builder.ConnectionString;
        }

        if (scan.IncompleteEscape)
        {
            if (StartsWithOwnTokens(scan.OptionTokens) && !AssignsJit(scan.OptionTokens, 2))
                return connectionString;
            if (AssignsJit(scan.OptionTokens, 0))
            {
                WarnUnapplied();
                return connectionString;
            }

            builder.Options = JitOffOption + " " + options;
            return builder.ConnectionString;
        }

        builder.Options = options + " " + JitOffOption;
        return builder.ConnectionString;
    }

    private static void WarnUnapplied()
    {
        if (Interlocked.Exchange(ref _unappliedWarning, 1) != 0)
            return;
        Log.Warning(
            "Postgres JIT suppression left startup Options unchanged because a trailing backslash would consume the appended separator.");
    }

    private readonly record struct OptionScan(List<string> OptionTokens, int TerminatorStart, bool IncompleteEscape);

    private static OptionScan ScanOptions(string options)
    {
        var optionTokens = new List<string>();
        var terminator = -1;
        var incomplete = false;
        var i = 0;
        while (i < options.Length)
        {
            while (i < options.Length && IsPgSpace(options[i]))
                i++;
            if (i >= options.Length)
                break;

            var start = i;
            var escaped = false;
            var text = new StringBuilder();
            while (i < options.Length)
            {
                var c = options[i];
                if (IsPgSpace(c) && !escaped)
                    break;
                if (!escaped && c == '\\')
                    escaped = true;
                else
                {
                    escaped = false;
                    text.Append(c);
                }

                i++;
            }

            if (escaped)
                incomplete = true;
            var token = text.ToString();
            if (terminator < 0)
            {
                if (token == "--")
                    terminator = start;
                else
                    optionTokens.Add(token);
            }
        }

        return new OptionScan(optionTokens, terminator, incomplete);
    }

    private static bool IsPgSpace(char c) =>
        c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    private static bool EndsWithOwnTokens(List<string> tokens) =>
        tokens.Count >= 2
        && tokens[^1] == "jit=off"
        && tokens[^2] == "-c";

    private static bool StartsWithOwnTokens(List<string> tokens) =>
        tokens.Count >= 2
        && tokens[0] == "-c"
        && tokens[1] == "jit=off";

    private static bool AssignsJit(List<string> tokens, int start)
    {
        for (var i = start; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token == "-c")
            {
                if (i + 1 < tokens.Count && IsJitValue(tokens[i + 1]))
                    return true;
                continue;
            }

            if (token.StartsWith("-c", StringComparison.Ordinal) && token.Length > 2 && IsJitValue(token[2..]))
                return true;
            if (token.StartsWith("--", StringComparison.Ordinal) && token.Length > 2 && IsJitValue(token[2..]))
                return true;
        }

        return false;
    }

    private static bool IsJitValue(string value) =>
        value.StartsWith("jit=", StringComparison.OrdinalIgnoreCase);
}
