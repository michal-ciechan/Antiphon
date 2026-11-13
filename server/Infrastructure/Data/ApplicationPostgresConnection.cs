using Npgsql;

namespace Antiphon.Server.Infrastructure.Data;

// CARD-1886. Pure startup-option policy for application connections.
// Hangfire and other raw administrative connections do not use this helper.
// PostgreSQL keeps the last startup option, including -c, -c glued to the name,
// and --name=value. Always append a final "-c jit=off" token. Existing option
// bytes stay intact, including a trailing escaped space. A second call that
// already ends on that exact separate token returns the input unchanged.
public static class ApplicationPostgresConnection
{
    public const string JitOffOption = "-c jit=off";

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
        if (EndsWithOwnSuppression(options))
            return connectionString;

        builder.Options = options.Length == 0
            ? JitOffOption
            : options + " " + JitOffOption;
        return builder.ConnectionString;
    }

    // A space is a token separator only when an even number of backslashes precedes it.
    private static bool EndsWithOwnSuppression(string options)
    {
        if (!options.EndsWith(JitOffOption, StringComparison.Ordinal))
            return false;
        if (options.Length == JitOffOption.Length)
            return true;

        var separator = options.Length - JitOffOption.Length - 1;
        if (options[separator] != ' ')
            return false;

        var slashes = 0;
        for (var i = separator - 1; i >= 0 && options[i] == '\\'; i--)
            slashes++;
        return slashes % 2 == 0;
    }
}
