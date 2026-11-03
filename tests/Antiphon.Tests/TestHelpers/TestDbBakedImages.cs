using System.Diagnostics;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Local Docker images of Postgres with the migrated <c>antiphon_test</c> already in the data
/// directory, tagged <c>antiphon-test-pg:&lt;schema key&gt;</c>. A test process whose key has an
/// image starts it and is ready with the schema in well under a second, skipping both migrate
/// (~12 s) and the schema-cache restore (~4–7 s).
/// <para>
/// The first process for a key takes the normal path, then bakes the image in the background
/// from the SQL it already holds: a throwaway container with <see cref="DataDirectory"/> outside
/// the base image's declared <c>VOLUME</c> (a commit drops volumes), restore, clean stop, commit.
/// One baker per key per host (a lock file); the process waits for its bake at teardown so the
/// image is never half-made. Baking never fails a test run: every error is one stderr line.
/// </para>
/// <c>ANTIPHON_TEST_PG_IMAGE=off</c> disables both lookup and baking.
/// </summary>
internal class TestDbBakedImages
{
    public const string EnvVar = "ANTIPHON_TEST_PG_IMAGE";
    public const string Repository = "antiphon-test-pg";
    public const string DataDirectory = "/pgdata";
    /// <summary>The base image's declared <c>VOLUME</c>; a started baked image runs Postgres there.</summary>
    public const string VolumeDataDirectory = "/var/lib/postgresql/data";
    public const string KeyLabel = "net.codeperf.antiphon.test-schema-key";
    public const int KeepImages = 4;

    private static readonly TimeSpan BakeWaitAtTeardown = TimeSpan.FromMinutes(2);

    protected TestDbBakedImages(bool enabled) => IsEnabled = enabled;

    public static TestDbBakedImages Disabled { get; } = new(false);

    public bool IsEnabled { get; }

    /// <summary>Whether a bake prunes older tags; a test's throwaway bake leaves real ones alone.</summary>
    protected virtual bool PrunesAfterBake => true;

    public static TestDbBakedImages FromEnvironment() =>
        string.Equals(Environment.GetEnvironmentVariable(EnvVar)?.Trim(), "off", StringComparison.OrdinalIgnoreCase)
            ? Disabled
            : new TestDbBakedImages(true);

    public static string ImageName(string key) => $"{Repository}:{key}";

    /// <summary>
    /// Starts a baked image with Postgres running from a copy of <see cref="DataDirectory"/> in the
    /// container's anonymous volume, not from the committed layer. On overlayfs every first
    /// read-write open of a layer file is a copy-up, and a backend signalled during one, or while
    /// it waits for another backend's copy-up of the same file, gets EINTR from open(), which
    /// Postgres reports as <c>XX000 could not open file "base/16384/…": Interrupted system call</c>
    /// (rc/2026-11-03 group-78). The copy (~85 MB) runs once in the entrypoint shell before
    /// Postgres starts: reading layer files never copies them up.
    /// </summary>
    public static PostgreSqlBuilder RunFromVolume(PostgreSqlBuilder builder) =>
        builder
            .WithEnvironment("PGDATA", VolumeDataDirectory)
            .WithEntrypoint("sh", "-c",
                $"[ -s \"$PGDATA/PG_VERSION\" ] || cp -a {DataDirectory}/. \"$PGDATA\"/ || exit 1; " +
                "exec docker-entrypoint.sh \"$@\"",
                "sh");

    public virtual async Task<bool> ExistsAsync(string key)
    {
        if (!IsEnabled)
            return false;
        using var span = TestTrace.Span("db-image lookup");
        using var client = CreateClient();
        try
        {
            await client.Images.InspectImageAsync(ImageName(key)).ConfigureAwait(false);
            span?.SetTag("antiphon.image.hit", true);
            return true;
        }
        catch (DockerImageNotFoundException)
        {
            span?.SetTag("antiphon.image.hit", false);
            return false;
        }
    }

    /// <summary>Starts the bake without blocking; <see cref="WaitForBakesAsync"/> joins it.</summary>
    public void BakeInBackground(string key, byte[] sql)
    {
        if (!IsEnabled)
            return;
        lock (_bakes)
            _bakes.Add(Task.Run(() => BakeAsync(key, sql)));
    }

    private readonly List<Task> _bakes = [];

    public async Task WaitForBakesAsync()
    {
        Task[] pending;
        lock (_bakes)
            pending = [.. _bakes];
        if (pending.Length == 0)
            return;
        using var span = TestTrace.Span("db-image bake wait");
        var all = Task.WhenAll(pending);
        if (await Task.WhenAny(all, Task.Delay(BakeWaitAtTeardown)).ConfigureAwait(false) != all)
            Console.Error.WriteLine($"C1420 pg-image bake still running after {BakeWaitAtTeardown.TotalSeconds:F0}s at teardown; abandoned.");
    }

    /// <summary>Bakes <paramref name="sql"/> into <c>antiphon-test-pg:&lt;key&gt;</c>. Never throws.</summary>
    public virtual async Task BakeAsync(string key, byte[] sql)
    {
        var timer = Stopwatch.StartNew();
        using var span = TestTrace.Span("db-image bake");
        FileStream? gate = null;
        try
        {
            var lockDirectory = Path.Combine(Path.GetTempPath(), "antiphon-test-pg-bake");
            Directory.CreateDirectory(lockDirectory);
            try
            {
                gate = new FileStream(Path.Combine(lockDirectory, key + ".lock"), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                Report("busy", key, timer);
                return;
            }

            if (await ExistsAsync(key).ConfigureAwait(false))
            {
                Report("exists", key, timer);
                return;
            }

            var container = new PostgreSqlBuilder(TestDbOperations.PostgresImage)
                .WithDatabase(TestDbFixture.SharedDatabaseName)
                .WithUsername(TestDbOperations.DatabaseUser)
                .WithPassword("test")
                .WithEnvironment("PGDATA", DataDirectory)
                .Own()
                .Build();
            await using (container.ConfigureAwait(false))
            await using (TestDbContainerOwnership.RemoveOnDispose(container))
            {
                using (TestTrace.Span("db-image bake start"))
                    await container.StartAsync().ConfigureAwait(false);
                using (TestTrace.Span("db-image bake restore"))
                    await RestoreAsync(container, sql).ConfigureAwait(false);
                using (TestTrace.Span("db-image bake template"))
                    await ProtectTemplateAsync(container).ConfigureAwait(false);
                using (TestTrace.Span("db-image bake stop"))
                    await container.StopAsync().ConfigureAwait(false);
                using (TestTrace.Span("db-image bake commit"))
                {
                    using var client = CreateClient();
                    var inspected = await client.Containers.InspectContainerAsync(container.Id).ConfigureAwait(false);
                    // A commit inherits the container's labels. Testcontainers' session label would
                    // let the resource reaper delete the image when this process ends; overwrite it.
                    var labels = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var name in inspected.Config.Labels?.Keys ?? [])
                    {
                        if (name.StartsWith("org.testcontainers", StringComparison.Ordinal))
                            labels[name] = "antiphon-baked";
                    }
                    // Nor may a container started from the image look like this baker's orphan.
                    labels[TestDbContainerOwnership.OwnerLabel] = "baked-image";
                    labels[KeyLabel] = key;
                    await client.Images.CommitContainerChangesAsync(new CommitContainerChangesParameters
                    {
                        ContainerID = container.Id,
                        RepositoryName = Repository,
                        Tag = key,
                        Comment = "Antiphon test Postgres with the migrated schema baked in",
                        Labels = labels,
                    }).ConfigureAwait(false);
                    if (PrunesAfterBake)
                        await PruneAsync(client, key).ConfigureAwait(false);
                }
            }

            Report("baked", key, timer);
        }
        catch (Exception ex)
        {
            span?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            Console.Error.WriteLine($"C1420 pg-image bake failed key={key}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            gate?.Dispose();
        }
    }

    private static async Task RestoreAsync(PostgreSqlContainer container, byte[] sql)
    {
        var path = $"/tmp/c1420-bake-{Guid.NewGuid():N}.sql";
        await container.CopyAsync(sql, path).ConfigureAwait(false);
        var restore = await container.ExecAsync(
        [
            "psql", "-X", "-q", "-v", "ON_ERROR_STOP=1", "--single-transaction",
            "-U", TestDbOperations.DatabaseUser, "-d", TestDbFixture.SharedDatabaseName, "-f", path
        ]).ConfigureAwait(false);
        if (restore.ExitCode != 0)
            throw new InvalidOperationException($"psql exited {restore.ExitCode}: {(restore.Stderr ?? "").Trim()}");
        await container.ExecAsync(["rm", "-f", path]).ConfigureAwait(false);
    }

    /// <summary>
    /// The clone template exactly as <see cref="TestDbOperations.ProtectTemplateAsync"/> makes it,
    /// so a process started from the image skips that ~1 s copy. Separate <c>-c</c> commands:
    /// CREATE DATABASE cannot run inside the implicit transaction of a multi-statement string.
    /// </summary>
    private static async Task ProtectTemplateAsync(PostgreSqlContainer container)
    {
        var template = TestDbFixture.TemplateDatabaseName;
        var result = await container.ExecAsync(
        [
            "psql", "-X", "-q", "-v", "ON_ERROR_STOP=1", "-U", TestDbOperations.DatabaseUser, "-d", "postgres",
            "-c", $"CREATE DATABASE {template} TEMPLATE {TestDbFixture.SharedDatabaseName}",
            "-c", $"ALTER DATABASE {template} IS_TEMPLATE true",
            "-c", $"ALTER DATABASE {template} ALLOW_CONNECTIONS false",
        ]).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"template psql exited {result.ExitCode}: {(result.Stderr ?? "").Trim()}");
    }

    /// <summary>Keeps the newest <see cref="KeepImages"/> tags (always the current one). Best-effort.</summary>
    private static async Task PruneAsync(DockerClient client, string currentKey)
    {
        try
        {
            var images = await client.Images.ListImagesAsync(new ImagesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["reference"] = new Dictionary<string, bool> { [Repository] = true }
                }
            }).ConfigureAwait(false);
            var stale = images
                .Where(i => i.Labels is null || !i.Labels.TryGetValue(KeyLabel, out var k) || k != currentKey)
                .OrderByDescending(i => i.Created)
                .Skip(KeepImages - 1);
            foreach (var image in stale)
            {
                try
                {
                    await client.Images.DeleteImageAsync(image.ID, new ImageDeleteParameters { Force = false })
                        .ConfigureAwait(false);
                }
                catch (DockerApiException)
                {
                    // In use by a running test container, or already gone.
                }
            }
        }
        catch (DockerApiException)
        {
        }
    }

    private static DockerClient CreateClient() =>
        TestcontainersSettings.OS.DockerEndpointAuthConfig
            .GetDockerClientBuilder(ResourceReaper.DefaultSessionId)
            .Build();

    private static void Report(string outcome, string key, Stopwatch timer) =>
        Console.Error.WriteLine($"C1420 pg-image {outcome} key={key} ms={timer.ElapsedMilliseconds}");
}
