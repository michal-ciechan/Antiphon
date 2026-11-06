using System.Runtime.InteropServices;
using System.Text;
using Antiphon.Tests.Application;
using Npgsql;
using World = Antiphon.Tests.Application.PostLandMutationWorld;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// One post-land world per test process, copied for every test that asks for the default world.
/// Building a <see cref="World"/> runs a full land plus a provisioned mutation task (~110 git
/// processes, most of a PostLand test's time); copying it is a directory copy and a database clone.
/// <para>
/// The first default world is built normally, then frozen: its services are disposed, its pools
/// cleared and its database's backends ended, so its folder and database become read-only
/// templates for the rest of the process (the database dies with the test container; the folder is
/// deleted at process exit). A copy:
/// </para>
/// <list type="bullet">
/// <item>copies the folder to <c>%TEMP%/antiphon-c448-&lt;new id&gt;</c>. Every absolute path the
/// world stored (git config, gitfiles, worktrees/*/gitdir, lock reasons, task files) contains the
/// template's 32-hex id once, so replacing that same-length token rewrites every spelling (either
/// slash, JSON-escaped). Objects, packs and the index are not touched (no absolute paths; checksums);</item>
/// <item>clones the template database (<c>CREATE DATABASE … TEMPLATE … STRATEGY FILE_COPY</c>) and
/// replaces the token in every text/json column in one server-side statement;</item>
/// <item>attaches a harness to both (<see cref="LandingSafetyHarness.Attach"/>) and restores the
/// world's identities, the fake runner's store id and the verifier's record.</item>
/// </list>
/// Anything unexpected while building the snapshot (a reparse point in the tree, a failure) turns it
/// off for the process and worlds are built as before. <c>ANTIPHON_TEST_WORLD_SNAPSHOT=off</c> too.
/// </summary>
internal static class PostLandWorldSnapshot
{
    public const string EnvVar = "ANTIPHON_TEST_WORLD_SNAPSHOT";

    public static bool Enabled { get; } =
        !string.Equals(Environment.GetEnvironmentVariable(EnvVar)?.Trim(), "off", StringComparison.OrdinalIgnoreCase);

    public sealed record WorldState(Guid HostTaskId, string SeedSha, Guid TaskId, Guid Operation, Guid Original,
        Guid Companion, Guid OtherCompanion, Guid? VerificationStoreId, int VerifierCalls,
        (string Worktree, string? Filter)[] VerifierInvocations);

    private sealed record Template(string Root, string Token, string Database, string ConnectionString, WorldState State);

    private const string RootPrefix = "antiphon-c448-";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Template? _template;
    private static bool _broken;

    public static async Task<World> CopyAsync(Func<Task<World>> build)
    {
        var template = await TemplateAsync(build).ConfigureAwait(false);
        return template is null ? await build().ConfigureAwait(false) : await CopyFromAsync(template).ConfigureAwait(false);
    }

    private static async Task<Template?> TemplateAsync(Func<Task<World>> build)
    {
        if (_template is not null || _broken) return _template;
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_template is not null || _broken) return _template;
            using var span = TestTrace.Span("world-snapshot build");
            var world = await build().ConfigureAwait(false);
            try
            {
                var root = Path.GetFullPath(world.Host.Fixture.Root);
                var token = Path.GetFileName(root)[RootPrefix.Length..];
                if (token.Length != 32 || !token.All(Uri.IsHexDigit) || HasReparsePoint(root))
                    throw new InvalidOperationException("world root is not a plain antiphon-c448-<id> tree");
                var state = world.Capture();
                var schema = world.Host.Schema;
                // Freeze: no service, pooled connection or backend may hold the template open.
                await world.Host.Services.DisposeAsync().ConfigureAwait(false);
                NpgsqlConnection.ClearPool(new NpgsqlConnection(schema.ConnectionString));
                var database = new NpgsqlConnectionStringBuilder(schema.ConnectionString).Database!;
                await using (var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString))
                {
                    await maintenance.OpenAsync().ConfigureAwait(false);
                    await TestDbFixtureLifecycle.TerminateBackendsAsync(maintenance, database).ConfigureAwait(false);
                }
                AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteTree(root);
                _template = new(root, token, database, schema.ConnectionString, state);
                span?.SetTag("antiphon.world.root", Path.GetFileName(root));
                return _template;
            }
            catch
            {
                await world.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex)
        {
            _broken = true;
            Console.Error.WriteLine($"world-snapshot disabled: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<World> CopyFromAsync(Template template)
    {
        using var span = TestTrace.Span("world-snapshot copy");
        var token = Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetDirectoryName(template.Root)!, RootPrefix + token);
        string Rewrite(string text) => text.Replace(template.Token, token, StringComparison.OrdinalIgnoreCase);
        try
        {
            using (TestTrace.Span("world-snapshot files"))
                CopyTree(template.Root, root, Encoding.ASCII.GetBytes(template.Token), Encoding.ASCII.GetBytes(token));
            IsolatedTestSchema schema;
            using (TestTrace.Span("world-snapshot database"))
                schema = await CloneDatabaseAsync(template, token).ConfigureAwait(false);
            return World.FromSnapshot(root, template.State, schema, Rewrite);
        }
        catch
        {
            DeleteTree(root);
            throw;
        }
    }

    private static Task<IsolatedTestSchema> CloneDatabaseAsync(Template template, string token) =>
        CloneDatabaseAsync(template.Database, template.ConnectionString, template.Token, token);

    /// <summary>Clones a frozen template database as <c>test_&lt;token&gt;</c>, replacing <paramref name="oldToken"/> with
    /// <paramref name="token"/> in every text/json column (shared with <see cref="LandingHarnessSnapshot"/>).</summary>
    internal static async Task<IsolatedTestSchema> CloneDatabaseAsync(string templateDatabase, string templateConnectionString,
        string oldToken, string token, params (string Old, string New)[] alsoReplace)
    {
        var template = (Database: templateDatabase, ConnectionString: templateConnectionString, Token: oldToken);
        var name = "test_" + token;
        await using var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString);
        await maintenance.OpenAsync().ConfigureAwait(false);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await TestDbFixtureLifecycle.ExecuteNonQueryAsync(maintenance,
                    $"CREATE DATABASE {name} TEMPLATE {template.Database} STRATEGY FILE_COPY").ConfigureAwait(false);
                break;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ObjectInUse && attempt < 5)
            {
                await TestDbFixtureLifecycle.TerminateBackendsAsync(maintenance, template.Database).ConfigureAwait(false);
                await Task.Delay(50 * attempt).ConfigureAwait(false);
            }
        }
        var connection = new NpgsqlConnectionStringBuilder(template.ConnectionString) { Database = name }.ConnectionString;
        await using (var db = new NpgsqlConnection(connection))
        {
            await db.OpenAsync().ConfigureAwait(false);
            // One round trip: every text/json column of every table, token replaced where present.
            await using var rewrite = new NpgsqlCommand($$"""
                DO $$
                DECLARE r record;
                BEGIN
                  FOR r IN SELECT c.table_name, c.column_name, c.data_type
                           FROM information_schema.columns c
                           JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                           WHERE c.table_schema = 'public' AND t.table_type = 'BASE TABLE' AND c.is_generated = 'NEVER'
                             AND c.data_type IN ('text', 'character varying', 'json', 'jsonb')
                  LOOP
                    IF r.data_type IN ('json', 'jsonb') THEN
                      EXECUTE format('UPDATE public.%I SET %I = replace(%I::text, %L, %L)::%s WHERE %I::text ILIKE %L',
                        r.table_name, r.column_name, r.column_name, '{{template.Token}}', '{{token}}', r.data_type,
                        r.column_name, '%{{template.Token}}%');
                    ELSE
                      EXECUTE format('UPDATE public.%I SET %I = replace(replace(%I, %L, %L), %L, %L) WHERE %I ILIKE %L',
                        r.table_name, r.column_name, r.column_name, '{{template.Token}}', '{{token}}',
                        upper('{{template.Token}}'), '{{token}}', r.column_name, '%{{template.Token}}%');
                    END IF;
                  END LOOP;
                END $$;
                """, db);
            await rewrite.ExecuteNonQueryAsync().ConfigureAwait(false);
            // Values derived from the root that a text swap of the token cannot reach (a hash of a path), as exact pairs.
            foreach (var (old, @new) in alsoReplace)
            {
                await using var extra = new NpgsqlCommand($$"""
                    DO $$
                    DECLARE r record;
                    BEGIN
                      FOR r IN SELECT c.table_name, c.column_name, c.data_type
                               FROM information_schema.columns c
                               JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                               WHERE c.table_schema = 'public' AND t.table_type = 'BASE TABLE' AND c.is_generated = 'NEVER'
                                 AND c.data_type IN ('text', 'character varying', 'json', 'jsonb')
                      LOOP
                        IF r.data_type IN ('json', 'jsonb') THEN
                          EXECUTE format('UPDATE public.%I SET %I = replace(%I::text, %L, %L)::%s WHERE strpos(%I::text, %L) > 0',
                            r.table_name, r.column_name, r.column_name, '{{old}}', '{{@new}}', r.data_type, r.column_name, '{{old}}');
                        ELSE
                          EXECUTE format('UPDATE public.%I SET %I = replace(%I, %L, %L) WHERE strpos(%I, %L) > 0',
                            r.table_name, r.column_name, r.column_name, '{{old}}', '{{@new}}', r.column_name, '{{old}}');
                        END IF;
                      END LOOP;
                    END $$;
                    """, db);
                await extra.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
        return new IsolatedTestSchema(name, connection, TestDbFixture.Lifecycle);
    }

    /// <summary>Copies the tree, replacing the template token (same length) in every file but git's
    /// object store and index; read-only attributes are kept. Object files (loose objects and packs)
    /// are hard-linked where the platform allows, as <c>git clone --local</c> does: git writes a new
    /// object file and renames it into place, and never edits one in place.</summary>
    /// <summary>Freezes a world's database as a template: no pooled connection or backend may hold it open.</summary>
    internal static async Task FreezeDatabaseAsync(string connectionString)
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        await using var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString);
        await maintenance.OpenAsync().ConfigureAwait(false);
        await TestDbFixtureLifecycle.TerminateBackendsAsync(maintenance, database).ConfigureAwait(false);
    }

    internal static void CopyTree(string from, string to, string oldToken, string newToken) =>
        CopyTree(from, to, Encoding.ASCII.GetBytes(oldToken), Encoding.ASCII.GetBytes(newToken));

    internal static bool IsPlainTree(string root) => !HasReparsePoint(root);

    internal static void DeleteTreeAtExit(string root) => AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteTree(root);

    private static void CopyTree(string from, string to, byte[] oldToken, byte[] newToken)
    {
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories).Prepend(from))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            var target = Path.Combine(to, relative);
            var parts = relative.Split(Path.DirectorySeparatorChar);
            var binary = parts.Contains("objects") || Path.GetFileName(file) == "index";
            if (binary)
            {
                if (!(IsImmutableObject(parts) && TryHardLink(file, target)))
                    File.Copy(file, target);
                continue;
            }
            var bytes = File.ReadAllBytes(file);
            if (ReplaceAll(bytes, oldToken, newToken))
            {
                File.WriteAllBytes(target, bytes);
                File.SetAttributes(target, File.GetAttributes(file));
                File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
            }
            else File.Copy(file, target);
        }
    }

    /// <summary>objects/&lt;2 hex&gt;/&lt;38 hex&gt; or objects/pack/pack-*.{pack,idx,rev}; never objects/info.</summary>
    private static bool IsImmutableObject(string[] parts)
    {
        var at = Array.LastIndexOf(parts, "objects");
        if (at < 0 || at + 3 != parts.Length) return false;
        var (dir, name) = (parts[at + 1], parts[at + 2]);
        if (dir == "pack")
            return name.StartsWith("pack-", StringComparison.Ordinal)
                && (name.EndsWith(".pack", StringComparison.Ordinal) || name.EndsWith(".idx", StringComparison.Ordinal)
                    || name.EndsWith(".rev", StringComparison.Ordinal));
        return dir.Length == 2 && dir.All(Uri.IsHexDigit) && name.Length == 38 && name.All(Uri.IsHexDigit);
    }

    private static bool TryHardLink(string existing, string link) =>
        OperatingSystem.IsWindows() && CreateHardLinkW(link, existing, IntPtr.Zero);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newFileName, string existingFileName, IntPtr securityAttributes);

    /// <summary>In-place same-length replacement, ignoring ASCII case of hex letters.</summary>
    private static bool ReplaceAll(byte[] bytes, byte[] from, byte[] to)
    {
        var changed = false;
        for (var i = 0; i + from.Length <= bytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < from.Length && match; j++)
                match = bytes[i + j] == from[j]
                    || from[j] is >= (byte)'a' and <= (byte)'f' && bytes[i + j] == from[j] - 0x20;
            if (!match) continue;
            Array.Copy(to, 0, bytes, i, to.Length);
            changed = true;
            i += from.Length - 1;
        }
        return changed;
    }

    private static bool HasReparsePoint(string root) =>
        new DirectoryInfo(root).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
            .Any(i => (i.Attributes & FileAttributes.ReparsePoint) != 0);

    private static void DeleteTree(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
        catch (Exception) { /* best effort at process exit */ }
    }
}
