using System.Collections.Concurrent;
using System.Data.Common;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1420 S3: a snapshot restored through the owned container reproduces a fresh migration.
/// Every test owns its lifecycles (and containers) and a temp cache root; the shared fixture and
/// the host cache are never used.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("C1420Snapshot")]
public sealed class TestSchemaSnapshotTests
{
    [Test]
    public async Task Warm_cache_restore_matches_a_fresh_migration()
    {
        await using var pair = await WarmPair.StartAsync();

        pair.Restored.Migrate.ShouldBe(0);
        pair.Restored.Restore.ShouldBe(1);
        pair.Restored.SchemaSource.ShouldBe("hit");
        pair.Migrated.Migrate.ShouldBe(1);
        pair.Migrated.Capture.ShouldBe(1);
        pair.Migrated.SchemaSource.ShouldBe("miss");

        var ops = new TestDbOperations();
        (await ops.ReadAppliedMigrationsAsync(pair.RestoredReady.SharedConnectionString))
            .ShouldBe(ops.GetAssemblyMigrations());

        foreach (var mode in DumpModes)
        {
            var fresh = await DumpAsync(pair.MigratedReady.Container, mode);
            var restored = await DumpAsync(pair.RestoredReady.Container, mode);

            // The raw difference may only be the known re-parse of an index predicate (see
            // CanonicalizeArrayCasts); no table, constraint, sequence or data line may differ.
            var raw = Compare(fresh, restored);
            raw.OnlyInFirst.Concat(raw.OnlyInSecond).ShouldAllBe(
                line => line.StartsWith("CREATE INDEX ", StringComparison.Ordinal)
                    || line.StartsWith("CREATE UNIQUE INDEX ", StringComparison.Ordinal),
                mode);
            raw.OnlyInFirst.Count.ShouldBe(raw.OnlyInSecond.Count, mode);

            var diff = Compare(CanonicalizeArrayCasts(fresh), CanonicalizeArrayCasts(restored));
            diff.OnlyInFirst.ShouldBeEmpty($"{mode}: lines only in the fresh migration");
            diff.OnlyInSecond.ShouldBeEmpty($"{mode}: lines only in the restored store");
        }
    }

    [Test]
    public async Task Parity_comparison_detects_a_dropped_table()
    {
        await using var pair = await WarmPair.StartAsync();
        pair.Restored.SchemaSource.ShouldBe("hit");
        var table = await DropOneEmptyLeafTableAsync(pair.RestoredReady.SharedConnectionString);

        foreach (var mode in DumpModes)
        {
            var diff = Compare(
                CanonicalizeArrayCasts(await DumpAsync(pair.MigratedReady.Container, mode)),
                CanonicalizeArrayCasts(await DumpAsync(pair.RestoredReady.Container, mode)));

            diff.OnlyInSecond.ShouldBeEmpty(mode);
            diff.OnlyInFirst.ShouldNotBeEmpty(mode);
            var statements = diff.OnlyInFirst.Where(IsStatementStart).ToArray();
            statements.ShouldNotBeEmpty(mode);
            statements.ShouldAllBe(line => line.Contains("\"" + table + "\"", StringComparison.Ordinal), mode);
            var expectedHead = mode == "--schema-only" ? $"CREATE TABLE public.\"{table}\" (" : $"COPY public.\"{table}\" ";
            diff.OnlyInFirst.ShouldContain(line => line.StartsWith(expectedHead, StringComparison.Ordinal), mode);
        }
    }

    [Test]
    public async Task Restored_store_clones_and_program_migrate_is_a_version_check()
    {
        await using var pair = await WarmPair.StartAsync();
        pair.Restored.SchemaSource.ShouldBe("hit");
        var ops = new TestDbOperations();
        var expected = ops.GetAssemblyMigrations();

        await using var clone = await pair.Restored.CreateIsolatedSchemaAsync();
        var counter = new AllCommandsInterceptor();
        await using (var db = new AppDbContext(TestDbFixtureLifecycle.BuildOptions(clone.ConnectionString, counter)))
        {
            (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            await db.Database.MigrateAsync();
        }

        counter.Commands.ShouldNotBeEmpty("the interceptor observed the migrate call");
        counter.Commands.ShouldNotContain(
            c => c.Contains("INSERT INTO \"__EFMigrationsHistory\"", StringComparison.Ordinal),
            "a restored clone is current; MigrateAsync must apply no migration");
        (await ops.ReadAppliedMigrationsAsync(clone.ConnectionString)).ShouldBe(expected);

        // D-8: restore and capture accept only the owned container's antiphon_test.
        var owned = pair.RestoredReady.Container;
        (await Should.ThrowAsync<ArgumentException>(() => ops.RestoreSnapshotAsync(owned, "antiphon", [])))
            .Message.ShouldContain("antiphon_test");
        await Should.ThrowAsync<ArgumentException>(() => ops.CaptureSnapshotAsync(owned, "antiphon"));
        var handleless = new OwnedTestDbContainer { ConnectionString = pair.RestoredReady.SharedConnectionString };
        await Should.ThrowAsync<InvalidOperationException>(
            () => ops.RestoreSnapshotAsync(handleless, TestDbFixture.SharedDatabaseName, []));
        await Should.ThrowAsync<InvalidOperationException>(
            () => ops.CaptureSnapshotAsync(handleless, TestDbFixture.SharedDatabaseName));
    }

    private static readonly string[] DumpModes = ["--schema-only", "--data-only"];

    internal static async Task<IReadOnlyList<string>> DumpAsync(OwnedTestDbContainer container, string mode)
    {
        var real = container.Real ?? throw new InvalidOperationException("Owned container has no Testcontainers handle.");
        var result = await real.ExecAsync(
        [
            "pg_dump", "-U", TestDbOperations.DatabaseUser, "-d", TestDbFixture.SharedDatabaseName,
            "--no-owner", "--no-privileges", mode
        ]);
        result.ExitCode.ShouldBe(0, result.Stderr);
        return Normalize(result.Stdout);
    }

    /// <summary>Drops only <c>\restrict</c>/<c>\unrestrict</c> tokens, comments and blank lines.</summary>
    internal static IReadOnlyList<string> Normalize(string dump) =>
        dump.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0
                && !line.StartsWith("--", StringComparison.Ordinal)
                && !line.StartsWith("\\restrict", StringComparison.Ordinal)
                && !line.StartsWith("\\unrestrict", StringComparison.Ordinal))
            .ToArray();

    private static readonly System.Text.RegularExpressions.Regex VarcharArrayCast = new(
        @"\(\(ARRAY\[(?<items>'(?:[^']|'')*'::character varying(?:, '(?:[^']|'')*'::character varying)*)\]\)::text\[\]\)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex VarcharLiteral = new(
        @"'(?:[^']|'')*'::character varying",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// pg_dump is not a textual fixed point for a varchar <c>IN (...)</c> list: a fresh migration
    /// stores <c>ANY ((ARRAY['a'::character varying, ...])::text[])</c>, and replaying that text
    /// re-parses the cast into each element, <c>ANY (ARRAY[('a'::character varying)::text, ...])</c>.
    /// Both fold to the same <c>text[]</c> constant. Rewrites only the first form into the second.
    /// </summary>
    internal static IReadOnlyList<string> CanonicalizeArrayCasts(IReadOnlyList<string> lines) =>
        lines.Select(line => VarcharArrayCast.Replace(line, match =>
                "(ARRAY[" + string.Join(", ", VarcharLiteral.Matches(match.Groups["items"].Value)
                    .Select(item => "(" + item.Value + ")::text")) + "])"))
            .ToArray();

    /// <summary>Multiset line difference, so a duplicated or missing line is still reported.</summary>
    internal static (IReadOnlyList<string> OnlyInFirst, IReadOnlyList<string> OnlyInSecond) Compare(
        IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in first)
            counts[line] = counts.GetValueOrDefault(line) + 1;
        var onlyInSecond = new List<string>();
        foreach (var line in second)
        {
            if (counts.TryGetValue(line, out var n) && n > 0)
                counts[line] = n - 1;
            else
                onlyInSecond.Add(line);
        }

        var onlyInFirst = counts.Where(p => p.Value > 0).SelectMany(p => Enumerable.Repeat(p.Key, p.Value)).ToList();
        return (onlyInFirst, onlyInSecond);
    }

    private static bool IsStatementStart(string line) =>
        line.StartsWith("CREATE ", StringComparison.Ordinal)
        || line.StartsWith("ALTER ", StringComparison.Ordinal)
        || line.StartsWith("COMMENT ", StringComparison.Ordinal)
        || line.StartsWith("COPY ", StringComparison.Ordinal)
        || line.StartsWith("SELECT ", StringComparison.Ordinal);

    private static async Task<string> DropOneEmptyLeafTableAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        var candidates = new List<string>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind = 'r' AND c.relname <> '__EFMigrationsHistory'
              AND NOT EXISTS (SELECT 1 FROM pg_constraint k
                              WHERE k.contype = 'f' AND k.confrelid = c.oid AND k.conrelid <> c.oid)
              AND NOT EXISTS (SELECT 1 FROM pg_depend d JOIN pg_rewrite r ON r.oid = d.objid
                              WHERE d.refobjid = c.oid AND r.ev_class <> c.oid)
            ORDER BY c.relname
            """,
            connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                candidates.Add(reader.GetString(0));
        }

        foreach (var table in candidates)
        {
            await using var probe = new NpgsqlCommand($"SELECT NOT EXISTS (SELECT 1 FROM public.\"{table}\")", connection);
            if ((bool)(await probe.ExecuteScalarAsync())!)
            {
                await using var drop = new NpgsqlCommand($"DROP TABLE public.\"{table}\"", connection);
                await drop.ExecuteNonQueryAsync();
                return table;
            }
        }

        throw new InvalidOperationException("no empty leaf table to drop");
    }

    /// <summary>Lifecycle A misses and publishes into a temp root; lifecycle B then hits it.</summary>
    private sealed class WarmPair : IAsyncDisposable
    {
        private readonly string _root;

        private WarmPair(string root, TestDbFixtureLifecycle migrated, TestDbFixtureLifecycle restored)
        {
            _root = root;
            Migrated = migrated;
            Restored = restored;
        }

        public TestDbFixtureLifecycle Migrated { get; }
        public TestDbFixtureLifecycle Restored { get; }
        public TestDbReadyState MigratedReady { get; private set; } = null!;
        public TestDbReadyState RestoredReady { get; private set; } = null!;

        public static async Task<WarmPair> StartAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "c1420-snapshot-test-" + Guid.NewGuid().ToString("N"));
            var cache = new TestSchemaCache(root);
            var pair = new WarmPair(
                root,
                new TestDbFixtureLifecycle(new TestDbOperations(), cache),
                new TestDbFixtureLifecycle(new TestDbOperations(), cache));
            try
            {
                pair.MigratedReady = await pair.Migrated.EnsureReadyAsync();
                pair.RestoredReady = await pair.Restored.EnsureReadyAsync();
                return pair;
            }
            catch
            {
                await pair.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Restored.DisposeAsync();
            await Migrated.DisposeAsync();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class AllCommandsInterceptor : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Commands.Enqueue(command.CommandText);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Enqueue(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Commands.Enqueue(command.CommandText);
            return base.ScalarExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
