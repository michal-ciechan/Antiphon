using System.Collections.Concurrent;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// A <see cref="LandingSafetyHarness"/> world built once per process by a shared setup (typically a first land), then
/// copied for each test that starts from it, the way <see cref="PostLandWorldSnapshot"/> copies the post-land world.
/// <para>
/// The first request for a key builds the world normally (<c>configure</c>, then <c>setup</c>), then freezes it: its
/// services are disposed and its database's pooled connections and backends ended, so the folder and database become
/// read-only templates (the database dies with the test container; the folder is deleted at process exit). A copy:
/// <list type="bullet">
/// <item>copies the folder to <c>%TEMP%/antiphon-c448-&lt;new id&gt;</c>, replacing the template's 32-hex root token
/// (the only spelling of its root in git config, gitfiles, worktree registrations and recorded paths);</item>
/// <item>clones the template database and replaces the token in every text/json column;</item>
/// <item>attaches a new harness to both, with the same task id and seed (as post-land copies do), after
/// <c>configure</c> has set its services for the test.</item>
/// </list>
/// Only state that lives in git and the database survives: anything the setup left in harness memory (verifier
/// counters, fault hooks, a git trace) starts fresh in the copy. A failure while building turns the snapshot off for
/// that key and every caller builds the world itself; <c>ANTIPHON_TEST_WORLD_SNAPSHOT=off</c> too.
/// </para>
/// </summary>
internal static class LandingHarnessSnapshot
{
    private const string RootPrefix = "antiphon-c448-";

    private sealed record Template(string Root, string Token, Guid TaskId, string SeedSha, string Database, string ConnectionString);

    private static readonly ConcurrentDictionary<string, Lazy<Task<Template?>>> Templates = new(StringComparer.Ordinal);

    /// <summary>
    /// A harness whose world is the result of <paramref name="setup"/>: a copy of the key's template, or (snapshot off
    /// or unavailable) a world built here by running <paramref name="setup"/>. <paramref name="configure"/> runs on
    /// every harness before its services are built (template build, copy and fallback alike).
    /// </summary>
    public static async Task<LandingSafetyHarness> CreateAsync(string key, Action<LandingSafetyHarness> configure,
        Func<LandingSafetyHarness, Task> setup)
    {
        var template = PostLandWorldSnapshot.Enabled
            ? await Templates.GetOrAdd(key, _ => new Lazy<Task<Template?>>(() => BuildAsync(configure, setup))).Value.ConfigureAwait(false)
            : null;
        if (template is null)
        {
            var built = new LandingSafetyHarness();
            configure(built);
            await setup(built).ConfigureAwait(false);
            return built;
        }
        using var span = TestTrace.Span("harness-snapshot copy");
        var token = Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetDirectoryName(template.Root)!, RootPrefix + token);
        var harness = new LandingSafetyHarness(root, template.TaskId);
        try
        {
            using (TestTrace.Span("harness-snapshot files"))
                PostLandWorldSnapshot.CopyTree(template.Root, root, template.Token, token);
            IsolatedTestSchema schema;
            using (TestTrace.Span("harness-snapshot database"))
                schema = await PostLandWorldSnapshot.CloneDatabaseAsync(template.Database, template.ConnectionString,
                    template.Token, token, (Fingerprint(template.Root), Fingerprint(root))).ConfigureAwait(false);
            configure(harness);
            harness.Attach(schema, template.SeedSha);
            return harness;
        }
        catch
        {
            await harness.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The land's remote fingerprint for the fixture remote under <paramref name="root"/>: SHA-256 of the push
    /// endpoint exactly as <c>remote get-url</c> prints it (the path the fixture passed to <c>remote add</c>), as
    /// LandingGit.ReadDestinationAsync computes it. A copy's operations and requests must carry its own.</summary>
    private static string Fingerprint(string root) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            Path.Combine(root, "remote.git"))));

    private static async Task<Template?> BuildAsync(Action<LandingSafetyHarness> configure, Func<LandingSafetyHarness, Task> setup)
    {
        using var span = TestTrace.Span("harness-snapshot build");
        var harness = new LandingSafetyHarness();
        try
        {
            configure(harness);
            await setup(harness).ConfigureAwait(false);
            var root = Path.GetFullPath(harness.Fixture.Root);
            var token = Path.GetFileName(root)[RootPrefix.Length..];
            if (token.Length != 32 || !token.All(Uri.IsHexDigit) || !PostLandWorldSnapshot.IsPlainTree(root))
                throw new InvalidOperationException("harness root is not a plain antiphon-c448-<id> tree");
            var connection = harness.Schema.ConnectionString;
            await harness.Services.DisposeAsync().ConfigureAwait(false);
            await PostLandWorldSnapshot.FreezeDatabaseAsync(connection).ConfigureAwait(false);
            PostLandWorldSnapshot.DeleteTreeAtExit(root);
            var database = new Npgsql.NpgsqlConnectionStringBuilder(connection).Database!;
            return new Template(root, token, harness.Fixture.TaskId, harness.Fixture.SeedSha, database, connection);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"harness-snapshot disabled: {ex.GetType().Name}: {ex.Message}");
            try { await harness.DisposeAsync().ConfigureAwait(false); } catch (Exception) { /* best effort */ }
            return null;
        }
    }
}
