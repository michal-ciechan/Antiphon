using System.Collections.Concurrent;
using System.Text;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The bootstrap branch over baked Postgres images, driven by fake operations, a fake image store
/// and a temp-root schema cache. No Docker, no database.
/// </summary>
[Category("Unit")]
[NotInParallel]
public sealed class TestDbBakedImageLifecycleTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private static readonly byte[] SeededSql = Encoding.UTF8.GetBytes("-- seeded snapshot\n");

    [Test]
    public async Task Baked_image_starts_ready_without_migrating_or_restoring()
    {
        using var root = new TempRoot();
        var ops = new ControlledTestDbOperations();
        var images = new FakeBakedImages { Exists = true };
        var lifecycle = new TestDbFixtureLifecycle(ops, new TestSchemaCache(root.Path), images);

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);
        await lifecycle.DisposeAsync();

        ops.CreatedFromImage.ShouldBe(TestDbBakedImages.ImageName(TestSchemaCache.ComputeDefaultKey().Key));
        lifecycle.SchemaSource.ShouldBe("image");
        lifecycle.ImageStart.ShouldBe(1);
        lifecycle.Migrate.ShouldBe(0);
        lifecycle.Restore.ShouldBe(0);
        lifecycle.Protect.ShouldBe(1);
        ops.RestoreCalls.ShouldBe(0);
        ops.CaptureCalls.ShouldBe(0);
        images.Baked.ShouldBeEmpty();
    }

    [Test]
    public async Task Baked_image_with_its_template_skips_protecting_one()
    {
        using var root = new TempRoot();
        var ops = new ControlledTestDbOperations { TemplateBaked = true };
        var lifecycle = new TestDbFixtureLifecycle(ops, new TestSchemaCache(root.Path), new FakeBakedImages { Exists = true });

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);
        await lifecycle.DisposeAsync();

        lifecycle.SchemaSource.ShouldBe("image");
        lifecycle.Protect.ShouldBe(0, "the image already ships the protected template");
        lifecycle.Migrate.ShouldBe(0);
    }

    [Test]
    public async Task Baked_image_with_the_wrong_history_resets_and_migrates()
    {
        using var root = new TempRoot();
        var ops = new ControlledTestDbOperations { CacheFault = "history-mismatch" };
        var images = new FakeBakedImages { Exists = true };
        var lifecycle = new TestDbFixtureLifecycle(ops, new TestSchemaCache(root.Path), images);

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);
        await lifecycle.DisposeAsync();

        lifecycle.SchemaSource.ShouldBe("image-fallback");
        ops.DropTemplateCalls.ShouldBe(1, "the baked template is dropped before re-protecting");
        lifecycle.ResetSharedDatabase.ShouldBe(1);
        lifecycle.Migrate.ShouldBe(1);
        lifecycle.Protect.ShouldBe(1);
        images.Baked.ShouldBeEmpty();
    }

    [Test]
    public async Task Cold_start_migrates_then_bakes_the_captured_schema_before_teardown_ends()
    {
        using var root = new TempRoot();
        var ops = new ControlledTestDbOperations();
        var images = new FakeBakedImages { BakeDelay = TimeSpan.FromMilliseconds(200) };
        var lifecycle = new TestDbFixtureLifecycle(ops, new TestSchemaCache(root.Path), images);

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);
        lifecycle.SchemaSource.ShouldBe("miss");
        ops.CreatedFromImage.ShouldBeNull("a miss starts the stock image");
        await lifecycle.DisposeAsync();

        images.Baked.Count.ShouldBe(1, "teardown waits for the bake");
        images.Baked.Single().Key.ShouldBe(TestSchemaCache.ComputeDefaultKey().Key);
        images.Baked.Single().Sql.ShouldBe(ControlledTestDbOperations.CapturedSql);
    }

    [Test]
    public async Task Schema_cache_hit_bakes_from_the_restored_snapshot()
    {
        using var root = new TempRoot();
        var cache = new TestSchemaCache(root.Path);
        var (key, serverSha) = TestSchemaCache.ComputeDefaultKey();
        cache.Publish(key, new TestSchemaSnapshot(
            SeededSql, ControlledTestDbOperations.Migrations, serverSha, TestDbOperations.PostgresImage)).ShouldBeTrue();
        var ops = new ControlledTestDbOperations();
        var images = new FakeBakedImages();
        var lifecycle = new TestDbFixtureLifecycle(ops, cache, images);

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);
        await lifecycle.DisposeAsync();

        lifecycle.SchemaSource.ShouldBe("hit");
        images.Baked.Single().Sql.ShouldBe(SeededSql);
    }

    [Test]
    public async Task Disabled_images_never_look_up_or_bake()
    {
        using var root = new TempRoot();
        var ops = new ControlledTestDbOperations();
        var lifecycle = new TestDbFixtureLifecycle(ops, new TestSchemaCache(root.Path), TestDbBakedImages.Disabled);

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);
        await lifecycle.DisposeAsync();

        lifecycle.SchemaSource.ShouldBe("miss");
        lifecycle.ImageStart.ShouldBe(0);
        ops.CreatedFromImage.ShouldBeNull();
    }

    [Test]
    public async Task Image_lookup_failure_falls_back_to_the_stock_image()
    {
        using var root = new TempRoot();
        var ops = new ControlledTestDbOperations();
        var images = new FakeBakedImages { LookupThrows = true };
        var lifecycle = new TestDbFixtureLifecycle(ops, new TestSchemaCache(root.Path), images);

        await lifecycle.EnsureReadyAsync().WaitAsync(Bound);
        await lifecycle.DisposeAsync();

        lifecycle.State.ShouldBe("ready");
        ops.CreatedFromImage.ShouldBeNull();
        lifecycle.SchemaSource.ShouldBe("miss");
    }

    private sealed class FakeBakedImages() : TestDbBakedImages(enabled: true)
    {
        public bool Exists { get; init; }
        public bool LookupThrows { get; init; }
        public TimeSpan BakeDelay { get; init; }
        public ConcurrentBag<(string Key, byte[] Sql)> Baked { get; } = [];

        public override Task<bool> ExistsAsync(string key) =>
            LookupThrows ? throw new InvalidOperationException("docker unavailable") : Task.FromResult(Exists);

        public override async Task BakeAsync(string key, byte[] sql)
        {
            await Task.Delay(BakeDelay);
            Baked.Add((key, sql));
        }
    }

    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("c1420-image-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
