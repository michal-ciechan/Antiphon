using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.TestHelpers;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestDbFixtureLazyInitializationTests
{
    private const string ProbeEnv = TestDbFixture.ProbeMarker;
    private static readonly TimeSpan ChildBudget = TimeSpan.FromSeconds(150);

    [Test]
    public async Task A_db_free_exact_method_selection_constructs_and_starts_no_database()
    {
        SkipIfChildProcess();
        var run = await LaunchChildAsync("Child_db_free_selection_touches_no_database", mode: "db-free");
        run.Exit.ShouldBe(0, run.Stderr + run.Stdout);
        var executed = ReadExecuted(run.Trx);
        executed.Count.ShouldBe(1);
        executed[0].Name.ShouldContain("Child_db_free_selection_touches_no_database");
        executed[0].Outcome.ShouldBe("Passed");
        var life = ReadLifecycle(run.Root);
        life.GetProperty("state").GetString().ShouldBe("never-requested");
        life.GetProperty("create").GetInt32().ShouldBe(0);
        life.GetProperty("start").GetInt32().ShouldBe(0);
        life.GetProperty("teardownDispose").GetInt32().ShouldBe(0);
        life.GetProperty("runnerBaseUrl").GetString().ShouldBe(ProductionRunnerGuard.DeadRunnerBaseUrl);
        life.GetProperty("ptyBackend").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task A_class_filtered_child_skips_every_parent_probe()
    {
        SkipIfChildProcess();
        var run = await LaunchAsync(
            "/*/*/TestDbFixtureLazyInitializationTests/*",
            new { root = "", depth = 1 });
        run.Exit.ShouldBe(0, run.Stderr + run.Stdout);
        var trx = XDocument.Load(run.Trx);
        foreach (var result in trx.Descendants().Where(e => e.Name.LocalName == "UnitTestResult"))
        {
            var name = (string?)result.Attribute("testName") ?? "";
            var outcome = (string?)result.Attribute("outcome") ?? "";
            if (name.Contains("Child_", StringComparison.Ordinal)
                || name.Contains("A_db_free", StringComparison.Ordinal)
                || name.Contains("A_class_filtered", StringComparison.Ordinal)
                || name.Contains("A_child_at_depth", StringComparison.Ordinal)
                || name.Contains("Mixed_first", StringComparison.Ordinal)
                || name.Contains("A_post_start", StringComparison.Ordinal)
                || name.Contains("A_failing_worker", StringComparison.Ordinal))
            {
                (outcome is "Passed" or "Skipped" or "NotExecuted").ShouldBeTrue(name + " " + outcome);
            }
        }

        Directory.GetDirectories(run.Root, "probe-*").ShouldBeEmpty();
        if (File.Exists(Path.Combine(run.Root, "lifecycle.json")))
            ReadLifecycle(run.Root).GetProperty("create").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task A_child_at_depth_two_refuses_before_any_work()
    {
        SkipIfChildProcess();
        var run = await LaunchChildAsync(
            "Child_db_free_selection_touches_no_database",
            mode: "db-free",
            depth: 2);
        run.Exit.ShouldNotBe(0);
        var trxText = File.Exists(run.Trx) ? File.ReadAllText(run.Trx) : run.Stdout + run.Stderr;
        trxText.ShouldContain("depth");
        if (File.Exists(Path.Combine(run.Root, "lifecycle.json")))
        {
            var life = ReadLifecycle(run.Root);
            life.GetProperty("create").GetInt32().ShouldBe(0);
            life.GetProperty("start").GetInt32().ShouldBe(0);
        }
    }

    [Test]
    public async Task Mixed_first_consumers_share_one_real_bootstrap()
    {
        SkipIfChildProcess();
        var run = await LaunchChildAsync("Child_mixed_consumers_initialize_once", mode: "mixed");
        run.Exit.ShouldBe(0, run.Stderr + run.Stdout);
        var executed = ReadExecuted(run.Trx);
        executed.Count.ShouldBe(1);
        executed[0].Outcome.ShouldBe("Passed");
        var life = ReadLifecycle(run.Root);
        life.GetProperty("create").GetInt32().ShouldBe(1);
        life.GetProperty("start").GetInt32().ShouldBe(1);
        life.GetProperty("migrate").GetInt32().ShouldBe(1);
        life.GetProperty("protect").GetInt32().ShouldBe(1);
        life.GetProperty("teardownDispose").GetInt32().ShouldBe(1);
        var containerId = life.GetProperty("containerId").GetString();
        containerId.ShouldNotBeNullOrEmpty();
        DockerInspectExit(containerId!).ShouldNotBe(0);
    }

    [Test]
    public async Task A_post_start_fault_cleans_up_the_owned_container()
    {
        SkipIfChildProcess();
        var run = await LaunchChildAsync(
            "Child_post_start_fault_is_terminal",
            mode: "fault",
            fault: "after-migrate");
        run.Exit.ShouldBe(0, run.Stderr + run.Stdout);
        var life = ReadLifecycle(run.Root);
        life.GetProperty("state").GetString().ShouldBe("faulted");
        life.GetProperty("create").GetInt32().ShouldBe(1);
        life.GetProperty("start").GetInt32().ShouldBe(1);
        life.GetProperty("disposeOwned").GetInt32().ShouldBe(1);
        life.GetProperty("teardownDispose").GetInt32().ShouldBe(0);
        var containerId = life.GetProperty("containerId").GetString();
        containerId.ShouldNotBeNullOrEmpty();
        DockerInspectExit(containerId!).ShouldNotBe(0);
    }

    [Test]
    public async Task A_failing_worker_exits_1_without_running_tests()
    {
        SkipIfChildProcess();
        var root = CreateOwnedRoot();
        var unowned = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "c476-unowned-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(unowned);
        var settings = JsonSerializer.Serialize(new
        {
            Root = unowned,
            Session = Guid.NewGuid(),
            Task = Guid.NewGuid(),
            Notification = Guid.NewGuid()
        });
        var sw = Stopwatch.StartNew();
        var run = await LaunchProcessAsync(
            root,
            "/*/*/ProcessSpawnLimitTests/Caps_concurrent_process_spawning_tests_at_one",
            extraEnv: new Dictionary<string, string?> { [LandQueueRaceWorker.Marker] = settings });
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60));
        run.Exit.ShouldBe(1, run.Stderr + run.Stdout);
        run.Stderr.ShouldContain("InvalidOperationException");
        if (File.Exists(run.Trx))
            ReadExecuted(run.Trx).Count.ShouldBe(0);
        File.Exists(Path.Combine(root, "lifecycle.json")).ShouldBeFalse();
    }

    [Test]
    public async Task Child_db_free_selection_touches_no_database()
    {
        var probe = RequireChild("db-free");
        _ = probe;
        _ = new TestDbFixture();
        var options = TestDbFixture.CreateDbContextOptions(
            "Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p");
        new AppDbContext(options).Database.GetConnectionString()
            .ShouldBe("Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p");
        Environment.GetEnvironmentVariable(ProductionRunnerGuard.BaseUrlEnvVar)
            .ShouldBe(ProductionRunnerGuard.DeadRunnerBaseUrl);
        Environment.GetEnvironmentVariable(ProductionRunnerGuard.CheckInterpreterEnvVar)
            .ShouldBe("false");
        Environment.GetEnvironmentVariable(Antiphon.Agents.Pty.PtyBackendPolicy.EnvVar)
            .ShouldBeNull();
        TestDbFixture.Lifecycle.IsRequested.ShouldBeFalse();
    }

    [Test]
    public async Task Child_mixed_consumers_initialize_once()
    {
        RequireChild("mixed");
        var connections = new Task<string>[2];
        connections[0] = Task.Run(() => TestDbFixture.ConnectionString);
        connections[1] = Task.Run(() => TestDbFixture.ConnectionString);
        var maintenance = Task.Run(() => TestDbFixture.MaintenanceConnectionString);
        var options = new Task<string>[2];
        options[0] = Task.Run(() => new AppDbContext(TestDbFixture.CreateDbContextOptions()).Database.GetConnectionString()!);
        options[1] = Task.Run(() => new AppDbContext(TestDbFixture.CreateDbContextOptions()).Database.GetConnectionString()!);
        var opened = Task.Run(async () =>
        {
            await using var ctx = new TestDbFixture().CreateDbContext();
            await ctx.Database.OpenConnectionAsync();
            return ctx.Database.GetConnectionString()!;
        });
        var clones = new[]
        {
            TestDbFixture.CreateIsolatedSchemaAsync(),
            TestDbFixture.CreateIsolatedSchemaAsync()
        };
        await Task.WhenAll(connections[0], connections[1], options[0], options[1], maintenance, opened, clones[0], clones[1]);

        var shared = connections[0].Result;
        connections[1].Result.ShouldBe(shared);
        options[0].Result.ShouldBe(shared);
        options[1].Result.ShouldBe(shared);
        opened.Result.ShouldBe(shared);
        var sharedBuilder = new NpgsqlConnectionStringBuilder(shared);
        sharedBuilder.Database.ShouldBe(TestDbFixture.SharedDatabaseName);
        var maintenanceBuilder = new NpgsqlConnectionStringBuilder(maintenance.Result);
        maintenanceBuilder.Host.ShouldBe(sharedBuilder.Host);
        maintenanceBuilder.Port.ShouldBe(sharedBuilder.Port);
        maintenanceBuilder.Database.ShouldBe("postgres");

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();

        await using var maintenanceConn = new NpgsqlConnection(maintenance.Result);
        await maintenanceConn.OpenAsync();
        await using (var command = new NpgsqlCommand(
            "SELECT datistemplate, datallowconn FROM pg_database WHERE datname = @n",
            maintenanceConn))
        {
            command.Parameters.AddWithValue("n", TestDbFixture.TemplateDatabaseName);
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).ShouldBeTrue();
            reader.GetBoolean(0).ShouldBeTrue();
            reader.GetBoolean(1).ShouldBeFalse();
        }

        foreach (var clone in clones)
        {
            await using var cloneDb = new AppDbContext(TestDbFixture.CreateDbContextOptions(clone.Result.ConnectionString));
            (await cloneDb.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            (await cloneDb.Agents.AnyAsync()).ShouldBeFalse();
            await clone.Result.DisposeAsync();
        }
    }

    [Test]
    public async Task Child_post_start_fault_is_terminal()
    {
        RequireChild("fault");
        var waiters = Enumerable.Range(0, 3)
            .Select(_ => Task.Run(() => TestDbFixture.ConnectionString))
            .ToArray();
        foreach (var waiter in waiters)
        {
            var ex = await Should.ThrowAsync<Exception>(async () => await waiter);
            ex.ToString().ShouldContain("C476-FAULT");
        }

        var created = TestDbFixture.Lifecycle.Create;
        var later = await Should.ThrowAsync<Exception>(async () =>
            await Task.Run(() => TestDbFixture.ConnectionString));
        later.ToString().ShouldContain("C476-FAULT");
        TestDbFixture.Lifecycle.Create.ShouldBe(created);
        var cloneEx = await Should.ThrowAsync<Exception>(() => TestDbFixture.CreateIsolatedSchemaAsync());
        cloneEx.ToString().ShouldContain("C476-FAULT");
        TestDbFixture.Lifecycle.Drop.ShouldBe(0);
    }

    private static void SkipIfChildProcess()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ProbeEnv)))
            throw new SkipTestException("parent probe skipped in child process");
    }

    private static JsonElement RequireChild(string mode)
    {
        var raw = Environment.GetEnvironmentVariable(ProbeEnv);
        if (string.IsNullOrEmpty(raw))
            throw new SkipTestException("child requires " + ProbeEnv);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement.Clone();
        var depth = root.TryGetProperty("depth", out var depthEl) ? depthEl.GetInt32() : 0;
        if (depth != 1)
            throw new InvalidOperationException($"CARD-0476 probe depth {depth} exceeds 1");
        var actual = root.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() : null;
        if (!string.Equals(actual, mode, StringComparison.Ordinal))
            throw new SkipTestException($"child mode {actual} != {mode}");
        return root;
    }

    private static async Task<ChildRun> LaunchChildAsync(
        string childMethod,
        string mode,
        int depth = 1,
        string? fault = null)
    {
        return await LaunchAsync(
            "/*/*/TestDbFixtureLazyInitializationTests/" + childMethod,
            new { root = "", depth, mode, fault });
    }

    private static async Task<ChildRun> LaunchAsync(string filter, object probe)
    {
        var root = CreateOwnedRoot();
        var payload = JsonSerializer.Serialize(new
        {
            root,
            depth = ProbeInt(probe, "depth", 1),
            mode = ProbeString(probe, "mode"),
            fault = ProbeString(probe, "fault")
        });
        var extra = new Dictionary<string, string?> { [ProbeEnv] = payload };
        return await LaunchProcessAsync(root, filter, extra);
    }

    private static string CreateOwnedRoot()
    {
        var root = Path.GetFullPath(Path.Combine(
            ".antiphon",
            "acceptance",
            "card-0476",
            "probe-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<ChildRun> LaunchProcessAsync(
        string root,
        string filter,
        Dictionary<string, string?> extraEnv)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory
        };
        start.ArgumentList.Add(typeof(TestDbFixtureLazyInitializationTests).Assembly.Location);
        start.ArgumentList.Add("--treenode-filter");
        start.ArgumentList.Add(filter);
        start.ArgumentList.Add("--report-trx");
        start.ArgumentList.Add("--report-trx-filename");
        start.ArgumentList.Add("child.trx");
        start.ArgumentList.Add("--results-directory");
        start.ArgumentList.Add(root);
        foreach (var pair in extraEnv)
            start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(ChildBudget);
        }
        catch (System.TimeoutException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }

        var outText = await stdout;
        var errText = await stderr;
        await File.WriteAllTextAsync(Path.Combine(root, "stdout.log"), outText);
        await File.WriteAllTextAsync(Path.Combine(root, "stderr.log"), errText);
        return new ChildRun(process.ExitCode, outText, errText, root, Path.Combine(root, "child.trx"));
    }

    private static JsonElement ReadLifecycle(string root)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "lifecycle.json")));
        return doc.RootElement.Clone();
    }

    private static List<(string Name, string Outcome)> ReadExecuted(string trxPath)
    {
        if (!File.Exists(trxPath))
            return [];
        var doc = XDocument.Load(trxPath);
        var list = new List<(string, string)>();
        foreach (var result in doc.Descendants().Where(e => e.Name.LocalName == "UnitTestResult"))
        {
            var outcome = (string?)result.Attribute("outcome") ?? "";
            if (outcome is not ("Passed" or "Failed"))
                continue;
            list.Add(((string?)result.Attribute("testName") ?? "", outcome));
        }

        return list;
    }

    private static int DockerInspectExit(string containerId)
    {
        var start = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("inspect");
        start.ArgumentList.Add(containerId);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("docker missing");
        process.WaitForExit();
        return process.ExitCode;
    }

    private static int ProbeInt(object probe, string name, int fallback)
    {
        var prop = probe.GetType().GetProperty(name);
        if (prop?.GetValue(probe) is int i)
            return i;
        return fallback;
    }

    private static string? ProbeString(object probe, string name)
    {
        var prop = probe.GetType().GetProperty(name);
        return prop?.GetValue(probe) as string;
    }

    private sealed record ChildRun(int Exit, string Stdout, string Stderr, string Root, string Trx);
}
