namespace Antiphon.Tests.TestHelpers;

/// <summary>A migrated <c>antiphon_test</c> captured as plain SQL, with the history it must restore.</summary>
internal sealed record TestSchemaSnapshot(
    byte[] Sql,
    IReadOnlyList<string> MigrationIds,
    string ServerAssemblySha256,
    string Image);

/// <summary>
/// CARD-1420: host-wide store of migrated schema snapshots keyed by the migrations assembly.
/// </summary>
internal sealed class TestSchemaCache
{
    public const string EnvVar = "ANTIPHON_TEST_SCHEMA_CACHE";
    public const string DefaultDirectoryName = "antiphon-test-schema-cache";
    public const string SqlFileName = "schema.sql";
    public const string ManifestFileName = "manifest.json";
    public const int KeepEntries = 8;

    private readonly TimeProvider _clock;

    public TestSchemaCache(string? root, TimeProvider? clock = null)
    {
        Root = root;
        _clock = clock ?? TimeProvider.System;
    }

    public static TestSchemaCache Disabled { get; } = new(null);

    public string? Root { get; }

    public bool IsEnabled => Root is not null;

    public static TestSchemaCache FromEnvironment() => FromValue(Environment.GetEnvironmentVariable(EnvVar));

    public static TestSchemaCache FromValue(string? raw) => throw new NotImplementedException();

    public static string ComputeKey(byte[] serverAssemblyBytes, string image, string efVersion, string npgsqlVersion) =>
        throw new NotImplementedException();

    public bool TryRead(string key, out TestSchemaSnapshot? snapshot) => throw new NotImplementedException();

    public bool Publish(string key, TestSchemaSnapshot snapshot) => throw new NotImplementedException();

    public void Discard(string key) => throw new NotImplementedException();

    public void Touch(string key) => throw new NotImplementedException();

    public void Prune(DateTimeOffset now) => throw new NotImplementedException();
}
