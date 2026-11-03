using Testcontainers.PostgreSql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Server and clone-concurrency settings for the test Postgres container.
/// <para>
/// <c>max_connections</c> defaults to 500 instead of Postgres' 100: every clone is its own database
/// with its own Npgsql pool, so a parallel run holds many idle connections at once, and a
/// high-core runner has exhausted 100 during cleanup (53300, Preserved Gotcha #26). Override with
/// <c>ANTIPHON_TEST_PG_MAX_CONNECTIONS</c>.
/// </para>
/// <para>
/// Clones run up to <see cref="CloneWidth"/> at a time (<c>ANTIPHON_TEST_PG_CLONE_WIDTH</c>, default 4).
/// The template refuses connections (<c>ALLOW_CONNECTIONS false</c>) and concurrent
/// <c>CREATE DATABASE … TEMPLATE</c> only share-lock it, so they do not block each other. Measured in
/// the baked container: 16 FILE_COPY clones took ~120 ms in parallel against ~800 ms serially.
/// The width is still capped because each clone holds a maintenance connection and checkpoints.
/// </para>
/// </summary>
internal static class TestDbServerSettings
{
    public const string MaxConnectionsEnvVar = "ANTIPHON_TEST_PG_MAX_CONNECTIONS";
    public const string CloneWidthEnvVar = "ANTIPHON_TEST_PG_CLONE_WIDTH";
    public const int DefaultMaxConnections = 500;
    public const int DefaultCloneWidth = 4;

    public static int MaxConnections => Read(MaxConnectionsEnvVar, DefaultMaxConnections, 20, 5000);

    public static int CloneWidth => Read(CloneWidthEnvVar, DefaultCloneWidth, 1, 32);

    /// <summary>Applies the server settings to the test container (not the bake container).</summary>
    public static PostgreSqlBuilder Tuned(this PostgreSqlBuilder builder) =>
        builder.WithCommand("-c", $"max_connections={MaxConnections}");

    private static int Read(string name, int fallback, int min, int max) =>
        int.TryParse(Environment.GetEnvironmentVariable(name)?.Trim(), out var n) ? Math.Clamp(n, min, max) : fallback;
}
