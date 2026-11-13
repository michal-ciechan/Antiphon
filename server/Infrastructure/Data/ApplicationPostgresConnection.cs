namespace Antiphon.Server.Infrastructure.Data;

// CARD-1886. Pure startup-option policy for application connections.
// Hangfire and other raw administrative connections do not use this helper.
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
        return connectionString;
    }
}
