using System.Text;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1420 S2: the bootstrap branch over the schema cache, driven by fake operations and a
/// temp-root cache. No container, no database.
/// </summary>
[Category("Unit")]
[NotInParallel]
public sealed class TestDbFixtureSchemaCacheLifecycleTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private static readonly byte[] SeededSql = Encoding.UTF8.GetBytes("-- seeded snapshot\n");

    [Test]
    public async Task Cache_hit_restores_without_migrating()
    {
        using var root = new TempRoot();
        var cache = new TestSchemaCache(root.Path);
        var key = Seed(cache);
        var ops = new ControlledTestDbOperations();
        var lifecycle = new TestDbFixtureLifecycle(ops, cache);

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);

        lifecycle.Migrate.ShouldBe(0);
        lifecycle.Restore.ShouldBe(1);
        lifecycle.Protect.ShouldBe(1);
        lifecycle.Capture.ShouldBe(0);
        lifecycle.CacheFallback.ShouldBe(0);
        lifecycle.SchemaSource.ShouldBe("hit");
        ops.MigrateCalls.ShouldBe(0);
        ops.VerifyCalls.ShouldBe(1, "a hit runs the EF version check before readiness");
        ops.RestoredSql.ShouldBe(SeededSql);
        cache.TryRead(key, out _).ShouldBeTrue("a hit keeps its entry");
        await lifecycle.DisposeAsync();
    }

    [Test]
    public async Task Cache_miss_migrates_captures_and_publishes()
    {
        using var root = new TempRoot();
        var cache = new TestSchemaCache(root.Path);
        var ops = new ControlledTestDbOperations();
        var lifecycle = new TestDbFixtureLifecycle(ops, cache);

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);

        lifecycle.Migrate.ShouldBe(1);
        lifecycle.Capture.ShouldBe(1);
        lifecycle.Restore.ShouldBe(0);
        lifecycle.Protect.ShouldBe(1);
        lifecycle.SchemaSource.ShouldBe("miss");
        cache.TryRead(TestSchemaCache.ComputeDefaultKey().Key, out var published).ShouldBeTrue();
        published!.Sql.ShouldBe(ControlledTestDbOperations.CapturedSql);
        published.MigrationIds.ShouldBe(ControlledTestDbOperations.Migrations);
        published.Image.ShouldBe(TestDbOperations.PostgresImage);
        published.ServerAssemblySha256.ShouldBe(TestSchemaCache.ComputeDefaultKey().ServerAssemblySha256);
        await lifecycle.DisposeAsync();
    }

    [Test]
    [Arguments("restore-throws")]
    [Arguments("history-mismatch")]
    public async Task Failed_restore_resets_and_migrates(string fault)
    {
        using var root = new TempRoot();
        var cache = new TestSchemaCache(root.Path);
        var key = Seed(cache);
        var ops = new ControlledTestDbOperations { CacheFault = fault };
        var lifecycle = new TestDbFixtureLifecycle(ops, cache);

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);

        lifecycle.State.ShouldBe("ready");
        ops.RestoreCalls.ShouldBe(1);
        lifecycle.ResetSharedDatabase.ShouldBe(1);
        ops.ResetCalls.ShouldBe(1);
        lifecycle.Migrate.ShouldBe(1);
        lifecycle.CacheFallback.ShouldBe(1);
        lifecycle.Restore.ShouldBe(0);
        lifecycle.Protect.ShouldBe(1);
        lifecycle.SchemaSource.ShouldBe("fallback");
        Directory.Exists(Path.Combine(root.Path, key)).ShouldBeFalse("the failed entry is discarded");
        Directory.GetDirectories(root.Path).ShouldBeEmpty();
        await lifecycle.DisposeAsync();
    }

    [Test]
    public async Task Disabled_cache_migrates_and_never_publishes()
    {
        using var root = new TempRoot();
        var seeded = new TestSchemaCache(root.Path);
        var key = Seed(seeded);
        var before = Directory.GetFileSystemEntries(root.Path, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
        var ops = new ControlledTestDbOperations();
        var lifecycle = new TestDbFixtureLifecycle(ops, TestSchemaCache.Disabled);

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);

        lifecycle.SchemaSource.ShouldBe("off");
        lifecycle.Migrate.ShouldBe(1);
        lifecycle.Restore.ShouldBe(0);
        lifecycle.Capture.ShouldBe(0);
        ops.RestoreCalls.ShouldBe(0);
        ops.CaptureCalls.ShouldBe(0);
        Directory.GetFileSystemEntries(root.Path, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray()
            .ShouldBe(before);
        seeded.TryRead(key, out var untouched).ShouldBeTrue();
        untouched!.Sql.ShouldBe(SeededSql);
        await lifecycle.DisposeAsync();
    }

    [Test]
    public async Task Capture_failure_still_reaches_ready()
    {
        using var root = new TempRoot();
        var cache = new TestSchemaCache(root.Path);
        var ops = new ControlledTestDbOperations { CacheFault = "capture-throws" };
        var lifecycle = new TestDbFixtureLifecycle(ops, cache);

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);

        lifecycle.State.ShouldBe("ready");
        lifecycle.SchemaSource.ShouldBe("miss");
        ops.CaptureCalls.ShouldBe(1);
        lifecycle.Capture.ShouldBe(0);
        lifecycle.Migrate.ShouldBe(1);
        lifecycle.Protect.ShouldBe(1);
        Directory.GetDirectories(root.Path).ShouldBeEmpty();
        await lifecycle.DisposeAsync();
    }

    private static string Seed(TestSchemaCache cache)
    {
        var (key, serverSha) = TestSchemaCache.ComputeDefaultKey();
        cache.Publish(
            key,
            new TestSchemaSnapshot(SeededSql, ControlledTestDbOperations.Migrations, serverSha, TestDbOperations.PostgresImage))
            .ShouldBeTrue();
        return key;
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c1420-lifecycle-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
