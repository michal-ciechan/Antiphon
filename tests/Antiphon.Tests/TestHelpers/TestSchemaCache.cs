using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Tests.TestHelpers;

/// <summary>A migrated <c>antiphon_test</c> captured as plain SQL, with the history it must restore.</summary>
internal sealed record TestSchemaSnapshot(
    byte[] Sql,
    IReadOnlyList<string> MigrationIds,
    string ServerAssemblySha256,
    string Image);

/// <summary>
/// CARD-1420: host-wide store of migrated schema snapshots keyed by the migrations assembly.
/// An entry is <c>&lt;root&gt;/&lt;key&gt;/schema.sql</c> plus <c>manifest.json</c>; publish is a
/// temp directory renamed into place, so concurrent publishers keep one winner and a reader never
/// sees a partial entry. Anything unverifiable is a miss.
/// </summary>
internal sealed class TestSchemaCache
{
    public const string EnvVar = "ANTIPHON_TEST_SCHEMA_CACHE";
    public const string Format = "c1420-v1";
    public const string DefaultDirectoryName = "antiphon-test-schema-cache";
    public const string SqlFileName = "schema.sql";
    public const string ManifestFileName = "manifest.json";
    public const int KeepEntries = 8;

    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(14);
    public static readonly TimeSpan OrphanAfter = TimeSpan.FromHours(1);

    private static readonly Regex KeyName = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    private static readonly Regex EntryName = new(
        "^[0-9a-f]{32}(\\.tmp-[0-9a-f]{32}|\\.bad-[0-9a-f]{32})?$",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly Lazy<(string Key, string ServerAssemblySha256)> DefaultKey = new(ComputeDefaultKeyCore);

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

    public static TestSchemaCache FromValue(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
            return new TestSchemaCache(Path.Combine(Path.GetTempPath(), DefaultDirectoryName));
        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            return Disabled;
        return new TestSchemaCache(Path.GetFullPath(value));
    }

    public static string ComputeKey(byte[] serverAssemblyBytes, string image, string efVersion, string npgsqlVersion)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, Encoding.UTF8.GetBytes(Format));
        AppendField(hash, serverAssemblyBytes);
        AppendField(hash, Encoding.UTF8.GetBytes(image));
        AppendField(hash, Encoding.UTF8.GetBytes(efVersion));
        AppendField(hash, Encoding.UTF8.GetBytes(npgsqlVersion));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()[..32];
    }

    /// <summary>The key of this process's migrations assembly, image and EF provider versions.</summary>
    public static (string Key, string ServerAssemblySha256) ComputeDefaultKey() => DefaultKey.Value;

    public bool TryRead(string key, out TestSchemaSnapshot? snapshot)
    {
        snapshot = null;
        if (Root is null)
            return false;
        ValidateKey(key);
        var entry = Path.Combine(Root, key);
        if (!Directory.Exists(entry))
            return false;

        try
        {
            var manifestPath = Path.Combine(entry, ManifestFileName);
            var sqlPath = Path.Combine(entry, SqlFileName);
            if (!File.Exists(manifestPath) || !File.Exists(sqlPath))
            {
                Discard(key);
                return false;
            }

            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(manifestPath), Json);
            var sql = File.ReadAllBytes(sqlPath);
            if (manifest is null
                || !string.Equals(manifest.Format, Format, StringComparison.Ordinal)
                || !string.Equals(manifest.Key, key, StringComparison.Ordinal)
                || manifest.MigrationIds is null
                || manifest.SqlBytes != sql.LongLength
                || !string.Equals(manifest.SqlSha256, Sha256Hex(sql), StringComparison.Ordinal))
            {
                Discard(key);
                return false;
            }

            snapshot = new TestSchemaSnapshot(
                sql,
                manifest.MigrationIds,
                manifest.ServerAssemblySha256 ?? "",
                manifest.Image ?? "");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Discard(key);
            return false;
        }
    }

    /// <summary>Publishes atomically. Returns false when disabled or when another publisher won.</summary>
    public bool Publish(string key, TestSchemaSnapshot snapshot)
    {
        if (Root is null)
            return false;
        ValidateKey(key);
        Directory.CreateDirectory(Root);
        var destination = Path.Combine(Root, key);
        var temp = Path.Combine(Root, $"{key}.tmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            WriteFlushed(Path.Combine(temp, SqlFileName), snapshot.Sql);
            var manifest = new Manifest
            {
                Format = Format,
                Key = key,
                ServerAssemblySha256 = snapshot.ServerAssemblySha256,
                Image = snapshot.Image,
                MigrationIds = [.. snapshot.MigrationIds],
                SqlSha256 = Sha256Hex(snapshot.Sql),
                SqlBytes = snapshot.Sql.LongLength,
                CreatedUtc = _clock.GetUtcNow().UtcDateTime
            };
            WriteFlushed(Path.Combine(temp, ManifestFileName), JsonSerializer.SerializeToUtf8Bytes(manifest, Json));

            if (Directory.Exists(destination))
            {
                DeleteBestEffort(temp);
                return false;
            }

            try
            {
                Directory.Move(temp, destination);
                return true;
            }
            catch (IOException) when (Directory.Exists(destination))
            {
                DeleteBestEffort(temp);
                return false;
            }
        }
        catch
        {
            DeleteBestEffort(temp);
            throw;
        }
    }

    /// <summary>Moves a bad entry off its live name, then deletes it best-effort.</summary>
    public void Discard(string key)
    {
        if (Root is null)
            return;
        ValidateKey(key);
        var entry = Path.Combine(Root, key);
        var bad = Path.Combine(Root, $"{key}.bad-{Guid.NewGuid():N}");
        try
        {
            Directory.Move(entry, bad);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        DeleteBestEffort(bad);
    }

    public void Touch(string key)
    {
        if (Root is null)
            return;
        ValidateKey(key);
        try
        {
            File.SetLastWriteTimeUtc(Path.Combine(Root, key, ManifestFileName), _clock.GetUtcNow().UtcDateTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Keeps the <see cref="KeepEntries"/> most recently touched entries, drops entries untouched for
    /// <see cref="StaleAfter"/> and temp/bad directories older than <see cref="OrphanAfter"/>.
    /// Never deletes a name outside the entry pattern. Best-effort.
    /// </summary>
    public void Prune(DateTimeOffset now)
    {
        if (Root is null || !Directory.Exists(Root))
            return;

        var live = new List<(string Name, DateTime Touched)>();
        string[] directories;
        try
        {
            directories = Directory.GetDirectories(Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            if (!EntryName.IsMatch(name))
                continue;
            try
            {
                if (KeyName.IsMatch(name))
                {
                    var manifest = Path.Combine(directory, ManifestFileName);
                    var touched = File.Exists(manifest)
                        ? File.GetLastWriteTimeUtc(manifest)
                        : Directory.GetLastWriteTimeUtc(directory);
                    live.Add((name, touched));
                }
                else if (now.UtcDateTime - Directory.GetLastWriteTimeUtc(directory) > OrphanAfter)
                {
                    DeleteBestEffort(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        var ordered = live.OrderByDescending(e => e.Touched).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (i >= KeepEntries || now.UtcDateTime - ordered[i].Touched > StaleAfter)
                Discard(ordered[i].Name);
        }
    }

    private static (string Key, string ServerAssemblySha256) ComputeDefaultKeyCore()
    {
        var server = File.ReadAllBytes(typeof(Antiphon.Server.Infrastructure.Data.AppDbContext).Assembly.Location);
        var key = ComputeKey(
            server,
            TestDbOperations.PostgresImage,
            InformationalVersion(typeof(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions).Assembly),
            InformationalVersion(typeof(Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions).Assembly));
        return (key, Sha256Hex(server));
    }

    private static string InformationalVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";

    private static void AppendField(IncrementalHash hash, byte[] field)
    {
        hash.AppendData(BitConverter.GetBytes((long)field.Length));
        hash.AppendData(field);
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void ValidateKey(string key)
    {
        if (!KeyName.IsMatch(key))
            throw new ArgumentException($"Invalid schema cache key '{key}'.", nameof(key));
    }

    private static void WriteFlushed(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void DeleteBestEffort(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class Manifest
    {
        public string? Format { get; set; }
        public string? Key { get; set; }
        public string? ServerAssemblySha256 { get; set; }
        public string? Image { get; set; }
        public List<string>? MigrationIds { get; set; }
        public string? SqlSha256 { get; set; }
        public long SqlBytes { get; set; }
        public DateTime CreatedUtc { get; set; }
    }
}
