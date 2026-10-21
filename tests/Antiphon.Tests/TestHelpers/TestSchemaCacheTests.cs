using System.Text;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1420 S1: the host-wide schema snapshot store. Filesystem only; every test owns a temp root.
/// </summary>
[Category("Unit")]
public sealed class TestSchemaCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] ServerBytes = Encoding.UTF8.GetBytes("server-assembly-v1");

    [Test]
    public void Key_is_stable_for_identical_inputs()
    {
        var first = TestSchemaCache.ComputeKey(ServerBytes, "postgres:16-alpine", "9.0.21", "9.0.4");
        var second = TestSchemaCache.ComputeKey(
            Encoding.UTF8.GetBytes("server-assembly-v1"), "postgres:16-alpine", "9.0.21", "9.0.4");

        second.ShouldBe(first);
        first.Length.ShouldBe(32);
        first.ShouldMatch("^[0-9a-f]{32}$");
    }

    [Test]
    public void Key_changes_when_server_assembly_bytes_change()
    {
        var baseline = TestSchemaCache.ComputeKey(ServerBytes, "postgres:16-alpine", "9.0.21", "9.0.4");

        TestSchemaCache.ComputeKey(Encoding.UTF8.GetBytes("server-assembly-v2"), "postgres:16-alpine", "9.0.21", "9.0.4")
            .ShouldNotBe(baseline);
        TestSchemaCache.ComputeKey(ServerBytes, "postgres:17-alpine", "9.0.21", "9.0.4").ShouldNotBe(baseline);
        TestSchemaCache.ComputeKey(ServerBytes, "postgres:16-alpine", "9.0.22", "9.0.4").ShouldNotBe(baseline);
        TestSchemaCache.ComputeKey(ServerBytes, "postgres:16-alpine", "9.0.21", "9.0.5").ShouldNotBe(baseline);
        // Field framing: moving a character between adjacent inputs is a different key.
        TestSchemaCache.ComputeKey(ServerBytes, "postgres:16-alpine9", ".0.21", "9.0.4").ShouldNotBe(baseline);
    }

    [Test]
    public void Published_entry_reads_back_verified()
    {
        using var temp = new TempRoot();
        var cache = new TestSchemaCache(temp.Path, new FakeTimeProvider(Now));
        var key = KeyFor("published");
        var snapshot = Snapshot("CREATE TABLE a (id int);\n");

        cache.Publish(key, snapshot).ShouldBeTrue();

        cache.TryRead(key, out var read).ShouldBeTrue();
        read.ShouldNotBeNull();
        read.Sql.ShouldBe(snapshot.Sql);
        read.MigrationIds.ShouldBe(snapshot.MigrationIds);
        read.ServerAssemblySha256.ShouldBe(snapshot.ServerAssemblySha256);
        read.Image.ShouldBe(snapshot.Image);
        File.Exists(Path.Combine(temp.Path, key, TestSchemaCache.SqlFileName)).ShouldBeTrue();
        File.Exists(Path.Combine(temp.Path, key, TestSchemaCache.ManifestFileName)).ShouldBeTrue();
        Directory.GetDirectories(temp.Path).Select(Path.GetFileName).ShouldBe([key]);
    }

    [Test]
    public async Task Concurrent_publishers_keep_one_winner_and_no_temp_dirs()
    {
        using var temp = new TempRoot();
        var cache = new TestSchemaCache(temp.Path, new FakeTimeProvider(Now));
        var key = KeyFor("concurrent");
        const int publishers = 8;
        using var barrier = new Barrier(publishers);

        var results = await Task.WhenAll(Enumerable.Range(0, publishers).Select(i => Task.Factory.StartNew(
            () =>
            {
                var snapshot = Snapshot($"-- publisher {i}\n");
                barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                return (Index: i, Won: cache.Publish(key, snapshot));
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)));

        results.Count(r => r.Won).ShouldBe(1);
        var winner = results.Single(r => r.Won).Index;
        cache.TryRead(key, out var read).ShouldBeTrue();
        Encoding.UTF8.GetString(read!.Sql).ShouldBe($"-- publisher {winner}\n");
        Directory.GetDirectories(temp.Path).Select(Path.GetFileName).ShouldBe([key]);
    }

    [Test]
    public void Sql_hash_mismatch_is_a_miss_and_the_entry_is_discarded()
    {
        using var temp = new TempRoot();
        var cache = new TestSchemaCache(temp.Path, new FakeTimeProvider(Now));
        var key = KeyFor("tampered");
        cache.Publish(key, Snapshot("CREATE TABLE a (id int);\n")).ShouldBeTrue();
        File.WriteAllText(Path.Combine(temp.Path, key, TestSchemaCache.SqlFileName), "CREATE TABLE b (id int);\n");

        cache.TryRead(key, out var read).ShouldBeFalse();

        read.ShouldBeNull();
        Directory.Exists(Path.Combine(temp.Path, key)).ShouldBeFalse();
        Directory.GetDirectories(temp.Path).ShouldBeEmpty();
    }

    [Test]
    public void Missing_manifest_or_sql_is_a_miss()
    {
        using var temp = new TempRoot();
        var cache = new TestSchemaCache(temp.Path, new FakeTimeProvider(Now));
        cache.TryRead(KeyFor("never-published"), out var absent).ShouldBeFalse();
        absent.ShouldBeNull();

        foreach (var missing in new[] { TestSchemaCache.ManifestFileName, TestSchemaCache.SqlFileName })
        {
            var key = KeyFor("missing-" + missing);
            cache.Publish(key, Snapshot("CREATE TABLE a (id int);\n")).ShouldBeTrue();
            File.Delete(Path.Combine(temp.Path, key, missing));

            cache.TryRead(key, out var read).ShouldBeFalse(missing);
            read.ShouldBeNull();
            Directory.Exists(Path.Combine(temp.Path, key)).ShouldBeFalse(missing);
        }

        Directory.GetDirectories(temp.Path).ShouldBeEmpty();
    }

    [Test]
    public void Off_never_reads_or_writes()
    {
        TestSchemaCache.FromValue("off").IsEnabled.ShouldBeFalse();
        TestSchemaCache.FromValue("OFF").IsEnabled.ShouldBeFalse();
        TestSchemaCache.FromValue(" Off ").IsEnabled.ShouldBeFalse();
        TestSchemaCache.Disabled.IsEnabled.ShouldBeFalse();
        TestSchemaCache.FromValue(null).Root.ShouldBe(
            Path.Combine(Path.GetTempPath(), TestSchemaCache.DefaultDirectoryName));
        TestSchemaCache.FromValue("").Root.ShouldBe(
            Path.Combine(Path.GetTempPath(), TestSchemaCache.DefaultDirectoryName));
        TestSchemaCache.FromValue("relative-c1420").Root.ShouldBe(Path.GetFullPath("relative-c1420"));

        using var temp = new TempRoot();
        var key = KeyFor("off");
        new TestSchemaCache(temp.Path, new FakeTimeProvider(Now)).Publish(key, Snapshot("-- live\n")).ShouldBeTrue();
        var before = Directory.GetFileSystemEntries(temp.Path, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray();

        var off = TestSchemaCache.FromValue("off");
        off.TryRead(key, out var read).ShouldBeFalse();
        read.ShouldBeNull();
        off.Publish(key, Snapshot("-- other\n")).ShouldBeFalse();
        off.Touch(key);
        off.Discard(key);
        off.Prune(Now);

        Directory.GetFileSystemEntries(temp.Path, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray()
            .ShouldBe(before);
    }

    [Test]
    public void Prune_keeps_newest_entries_and_removes_stale_temp_and_bad_dirs()
    {
        using var temp = new TempRoot();
        var cache = new TestSchemaCache(temp.Path, new FakeTimeProvider(Now));
        var recent = Enumerable.Range(0, 9).Select(i => KeyFor("recent-" + i)).ToArray();
        for (var i = 0; i < recent.Length; i++)
        {
            cache.Publish(recent[i], Snapshot($"-- {i}\n")).ShouldBeTrue();
            SetTouched(temp.Path, recent[i], Now.AddHours(-i));
        }

        var stale = KeyFor("stale");
        cache.Publish(stale, Snapshot("-- stale\n")).ShouldBeTrue();
        SetTouched(temp.Path, stale, Now.AddDays(-15));

        var oldTmp = MakeDir(temp.Path, $"{recent[0]}.tmp-{Guid.NewGuid():N}", Now.AddHours(-2));
        var youngTmp = MakeDir(temp.Path, $"{recent[0]}.tmp-{Guid.NewGuid():N}", Now.AddMinutes(-10));
        var oldBad = MakeDir(temp.Path, $"{recent[1]}.bad-{Guid.NewGuid():N}", Now.AddHours(-3));
        var foreign = MakeDir(temp.Path, "keep-me", Now.AddDays(-30));

        cache.Prune(Now);

        var remaining = Directory.GetDirectories(temp.Path).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var expected = recent.Take(TestSchemaCache.KeepEntries)
            .Append(Path.GetFileName(youngTmp))
            .Append(Path.GetFileName(foreign))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        remaining.ShouldBe(expected);
        Directory.Exists(oldTmp).ShouldBeFalse();
        Directory.Exists(oldBad).ShouldBeFalse();
        Directory.Exists(Path.Combine(temp.Path, recent[8])).ShouldBeFalse();
        Directory.Exists(Path.Combine(temp.Path, stale)).ShouldBeFalse();
    }

    private static string KeyFor(string seed) =>
        TestSchemaCache.ComputeKey(Encoding.UTF8.GetBytes(seed), "postgres:16-alpine", "9.0.21", "9.0.4");

    private static TestSchemaSnapshot Snapshot(string sql) =>
        new(Encoding.UTF8.GetBytes(sql), ["20260101000000_First", "20260102000000_Second"], new string('a', 64), "postgres:16-alpine");

    private static void SetTouched(string root, string key, DateTimeOffset when)
    {
        File.SetLastWriteTimeUtc(Path.Combine(root, key, TestSchemaCache.ManifestFileName), when.UtcDateTime);
        Directory.SetLastWriteTimeUtc(Path.Combine(root, key), when.UtcDateTime);
    }

    private static string MakeDir(string root, string name, DateTimeOffset when)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "partial"), "x");
        Directory.SetLastWriteTimeUtc(path, when.UtcDateTime);
        return path;
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c1420-cache-test-" + Guid.NewGuid().ToString("N"));
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
