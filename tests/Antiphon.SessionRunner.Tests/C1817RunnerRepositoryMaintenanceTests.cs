using System.Diagnostics;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1817. The runner's own bounded gc, the lease skip, and a named refusal for an invalid
/// GIT_CONFIG_COUNT that still lets an in-process read answer.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C1817RunnerRepositoryMaintenanceTests
{
    [Test]
    [Timeout(60_000)]
    public async Task C1817_gc_pass_reduces_loose_objects()
    {
        var root = Temp("gc");
        var outside = Temp("gc-out");
        var link = Path.Combine(root, "escape");
        try
        {
            var repo = Repo(root, "antiphon", 15);
            var buried = Repo(root, "antiphon_pgdata", 1);
            var foreign = Repo(outside, "foreign", 1);
            var marker = Path.Combine(buried, "MARKER");
            File.WriteAllText(marker, "keep");
            MaintenanceFixture.LinkDirectory(link, foreign);
            var buriedLoose = RunnerRepositoryMaintenance.CountLoose(Path.Combine(buried, ".git"));
            var foreignLoose = RunnerRepositoryMaintenance.CountLoose(Path.Combine(foreign, ".git"));
            buriedLoose.ShouldBeGreaterThan(0);
            foreignLoose.ShouldBeGreaterThan(0);

            var log = new CaptureLogger();
            var maintenance = Build(root, logger: log, threshold: 1);
            var before = RunnerRepositoryMaintenance.CountLoose(Path.Combine(repo, ".git"));
            var packsBefore = RunnerRepositoryMaintenance.CountPacks(Path.Combine(repo, ".git"));
            before.ShouldBeGreaterThan(0);
            packsBefore.ShouldBe(0);

            var status = await maintenance.RunAsync(CancellationToken.None, force: true);
            var report = status.Repositories.ShouldHaveSingleItem();
            report.Repository.ShouldBe(Path.GetFullPath(repo));
            report.Outcome.ShouldBe("ran");
            report.Reason.ShouldBeNull();
            report.LooseBefore.ShouldBe(before);
            report.LooseAfter.ShouldBeLessThan(report.LooseBefore);
            report.PacksBefore.ShouldBe(0);
            report.PacksAfter.ShouldBeGreaterThan(report.PacksBefore);
            maintenance.LastArguments.ShouldBe(new[] { "gc", "--prune=2.weeks.ago" });
            RunnerRepositoryMaintenance.Discover(root).ShouldBe(new[] { Path.GetFullPath(repo) });
            RunnerRepositoryMaintenance.CountLoose(Path.Combine(buried, ".git")).ShouldBe(buriedLoose);
            RunnerRepositoryMaintenance.CountLoose(Path.Combine(foreign, ".git")).ShouldBe(foreignLoose);
            File.ReadAllText(marker).ShouldBe("keep");
            log.Lines.ShouldContain(line => line.Contains($"loose={report.LooseBefore}->{report.LooseAfter}", StringComparison.Ordinal)
                && line.Contains($"packs={report.PacksBefore}->{report.PacksAfter}", StringComparison.Ordinal)
                && line.Contains("outcome=ran", StringComparison.Ordinal));
        }
        finally
        {
            Unlink(link);
            Delete(root);
            Delete(outside);
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1817_gc_budget_kills_the_child()
    {
        var root = Temp("budget");
        Process? child = null;
        var pid = 0;
        try
        {
            Repo(root, "antiphon", 0);
            var maintenance = Build(root, threshold: 1, budget: 1, start: _ =>
            {
                child = MaintenanceFixture.StartSleep(30);
                pid = child!.Id;
                return child;
            });
            var status = await maintenance.RunAsync(CancellationToken.None, force: true);
            var report = status.Repositories.ShouldHaveSingleItem();
            report.Outcome.ShouldBe("failed");
            report.Reason.ShouldBe("budget");
            MaintenanceFixture.ProcessGone(pid).ShouldBeTrue();
        }
        finally
        {
            try
            {
                if (child is not null && pid > 0 && !MaintenanceFixture.ProcessGone(pid))
                    child.Kill(entireProcessTree: true);
            }
            catch { /* already reaped */ }
            Delete(root);
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1817_gc_child_strips_inherited_git_config()
    {
        var root = Temp("strip");
        try
        {
            var repo = Repo(root, "antiphon", 0);
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "x");
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "gc.auto");
            ProcessStartInfo? seen = null;
            var maintenance = Build(root, threshold: 1, start: psi =>
            {
                seen = psi;
                return MaintenanceFixture.StartTrue();
            });
            var status = await maintenance.RunAsync(CancellationToken.None, force: true);
            seen.ShouldNotBeNull();
            seen!.Environment.ContainsKey("GIT_CONFIG_COUNT").ShouldBeFalse();
            seen.Environment.ContainsKey("GIT_CONFIG_KEY_0").ShouldBeFalse();
            seen.Environment["GIT_TERMINAL_PROMPT"].ShouldBe("0");
            seen.WorkingDirectory.ShouldBe(Path.GetFullPath(repo));
            seen.ArgumentList.ShouldBe(new[] { "gc", "--prune=2.weeks.ago" });
            status.Repositories.Single().Outcome.ShouldBe("ran");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", null);
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", null);
            Delete(root);
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1817_skipped_while_lease_held()
    {
        var root = Temp("lease");
        try
        {
            var repo = Repo(root, "antiphon", 1);
            var loose = RunnerRepositoryMaintenance.CountLoose(Path.Combine(repo, ".git"));
            var activity = new RunnerRepositoryActivity();
            var maintenance = Build(root, activity, threshold: 1);
            using (activity.EnterLease())
            {
                var report = (await maintenance.RunAsync(CancellationToken.None, force: true)).Repositories.Single();
                report.Outcome.ShouldBe("skipped");
                report.Reason.ShouldBe("lease");
                report.LooseAfter.ShouldBe(loose);
            }
            maintenance.LastArguments.ShouldBeNull();
        }
        finally { Delete(root); }
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1817_skipped_while_pool_repository_lock_held()
    {
        var root = Temp("pool");
        try
        {
            var repo = Repo(root, "antiphon", 1);
            var allowed = Path.Combine(root, "allowed");
            Directory.CreateDirectory(allowed);
            var activity = new RunnerRepositoryActivity();
            var workspace = new RunnerWorkspaceService(
                new RunnerRepositoryPolicy(repo, "https://example.invalid/a.git", root, ["https://example.invalid/"], false),
                allowed, Process.Start, TimeSpan.FromSeconds(20));
            workspace.Activity = activity;
            var pool = new RunnerWorkspaceSlotPool(workspace, new PhoneHomeWorkspaceSlotSettings(), 1, activity: activity);
            var maintenance = Build(root, activity, threshold: 1);
            await pool.RunUnderRepositoryLockAsync(async () =>
            {
                var report = (await maintenance.RunAsync(CancellationToken.None, force: true)).Repositories.Single();
                report.Outcome.ShouldBe("skipped");
                report.Reason.ShouldBe("lease");
            }, CancellationToken.None);
        }
        finally { Delete(root); }
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1817_skipped_while_worktree_or_land_in_flight()
    {
        var root = Temp("land");
        try
        {
            var repo = Repo(root, "antiphon", 0);
            var activity = new RunnerRepositoryActivity();
            var maintenance = Build(root, activity, threshold: 1);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Process? child = null;
            var allowed = Path.Combine(root, "allowed");
            Directory.CreateDirectory(allowed);
            var workspace = new RunnerWorkspaceService(
                new RunnerRepositoryPolicy(repo, "https://example.invalid/a.git", root, ["https://example.invalid/"], false),
                allowed, Hold(entered, process => child = process), TimeSpan.FromSeconds(20));
            workspace.Activity = activity;
            var git = workspace.GitAsync(repo, CancellationToken.None, "worktree", "add", "nowhere");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                var report = (await maintenance.RunAsync(CancellationToken.None, force: true)).Repositories.Single();
                report.Outcome.ShouldBe("skipped");
                report.Reason.ShouldBe("worktree_or_land");
            }
            finally
            {
                Stop(child);
                try { await git; } catch { /* the held child was stopped */ }
            }
        }
        finally { Delete(root); }
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1817_skipped_while_task_git_in_flight()
    {
        var root = Temp("git");
        try
        {
            var repo = Repo(root, "antiphon", 0);
            var activity = new RunnerRepositoryActivity();
            var maintenance = Build(root, activity, threshold: 1);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Process? child = null;
            var allowed = Path.Combine(root, "allowed");
            Directory.CreateDirectory(allowed);
            var workspace = new RunnerWorkspaceService(
                new RunnerRepositoryPolicy(repo, "https://example.invalid/a.git", root, ["https://example.invalid/"], false),
                allowed, Hold(entered, process => child = process), TimeSpan.FromSeconds(20));
            workspace.Activity = activity;
            var git = workspace.GitAsync(repo, CancellationToken.None, "config", "--get", "gc.auto");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                var report = (await maintenance.RunAsync(CancellationToken.None, force: true)).Repositories.Single();
                report.Outcome.ShouldBe("skipped");
                report.Reason.ShouldBe("git_call");
            }
            finally
            {
                Stop(child);
                try { await git; } catch { /* the held child was stopped */ }
            }
        }
        finally { Delete(root); }
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1817_invalid_count_read_still_answers()
    {
        var root = Temp("read");
        try
        {
            var repo = Repo(root, "antiphon", 1);
            var expected = (await RunnerWorkspaceService.RunGitAsync(repo, Process.Start, TimeSpan.FromSeconds(20),
                CancellationToken.None, "rev-parse", "HEAD")).Stdout.Trim();
            expected.Length.ShouldBe(40);
            var allowed = Path.Combine(root, "allowed");
            Directory.CreateDirectory(allowed);
            var workspace = new RunnerWorkspaceService(
                new RunnerRepositoryPolicy(repo, "https://example.invalid/a.git", root, ["https://example.invalid/"], false),
                allowed, Process.Start, TimeSpan.FromSeconds(20));
            workspace.GitEnvironmentForTest = new Dictionary<string, string> { ["GIT_CONFIG_COUNT"] = "x" };
            var answer = await workspace.GitAsync(repo, CancellationToken.None, "rev-parse", "HEAD");
            answer.ExitCode.ShouldBe(0);
            answer.Stdout.Trim().ShouldBe(expected);
        }
        finally { Delete(root); }
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1817_invalid_count_spawn_is_named()
    {
        var root = Temp("spawn");
        try
        {
            var repo = Repo(root, "antiphon", 0);
            var allowed = Path.Combine(root, "allowed");
            Directory.CreateDirectory(allowed);
            var workspace = new RunnerWorkspaceService(
                new RunnerRepositoryPolicy(repo, "https://example.invalid/a.git", root, ["https://example.invalid/"], false),
                allowed, Process.Start, TimeSpan.FromSeconds(20));
            workspace.GitEnvironmentForTest = new Dictionary<string, string> { ["GIT_CONFIG_COUNT"] = "x" };
            var ex = await Should.ThrowAsync<PhoneHomeAdmissionException>(() =>
                workspace.GitAsync(repo, CancellationToken.None, "config", "--get", "gc.auto"));
            ex.Code.ShouldBe("git_config_count_invalid");
            ex.StatusCode.ShouldBe(409);
        }
        finally { Delete(root); }
    }

    [Test]
    public void C1817_due_when_loose_objects_exceed_threshold_or_the_week_elapsed()
    {
        var now = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        var week = TimeSpan.FromDays(7);
        RunnerRepositoryMaintenance.IsDue(now, 6700, 6700, null, week).ShouldBeTrue();
        RunnerRepositoryMaintenance.IsDue(now, 6699, 6700, null, week).ShouldBeFalse();
        RunnerRepositoryMaintenance.IsDue(now, 0, 6700, now.AddDays(-7), week).ShouldBeTrue();
        RunnerRepositoryMaintenance.IsDue(now, 0, 6700, now.AddDays(-7).AddTicks(1), week).ShouldBeFalse();
        RunnerRepositoryMaintenance.IsDue(now, 10, 6700, now.AddDays(-1), week).ShouldBeFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1817_first_pass_below_threshold_arms_the_week_without_gc()
    {
        var root = Temp("arm");
        try
        {
            var repo = Repo(root, "antiphon", 0);
            var loose = RunnerRepositoryMaintenance.CountLoose(Path.Combine(repo, ".git"));
            var state = Path.Combine(root, "repository-maintenance.json");
            var maintenance = Build(root, threshold: 100_000, state: state);
            var first = (await maintenance.RunAsync(CancellationToken.None)).Repositories.Single();
            first.Outcome.ShouldBe("armed");
            first.Reason.ShouldBe("week_started");
            first.LooseAfter.ShouldBe(loose);
            maintenance.LastArguments.ShouldBeNull();
            File.Exists(state).ShouldBeTrue();
            var second = (await maintenance.RunAsync(CancellationToken.None)).Repositories.Single();
            second.Outcome.ShouldBe("skipped");
            second.Reason.ShouldBe("not_due");
            second.LooseAfter.ShouldBe(loose);
        }
        finally { Delete(root); }
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1817_lease_skip_does_not_start_the_week()
    {
        var root = Temp("arm-lease");
        try
        {
            Repo(root, "antiphon", 0);
            var state = Path.Combine(root, "repository-maintenance.json");
            var activity = new RunnerRepositoryActivity();
            var maintenance = Build(root, activity, threshold: 100_000, state: state);
            using (activity.EnterLease())
            {
                var report = (await maintenance.RunAsync(CancellationToken.None)).Repositories.Single();
                report.Outcome.ShouldBe("skipped");
                report.Reason.ShouldBe("lease");
            }
            File.Exists(state).ShouldBeFalse();
            var armed = (await maintenance.RunAsync(CancellationToken.None)).Repositories.Single();
            armed.Outcome.ShouldBe("armed");
            armed.Reason.ShouldBe("week_started");
        }
        finally { Delete(root); }
    }

    [Test]
    [Timeout(10_000)]
    public async Task C1817_disabled_service_does_not_tick()
    {
        var root = Temp("off");
        try
        {
            Repo(root, "antiphon", 0);
            var maintenance = Build(root, threshold: 1);
            var off = new RunnerRepositoryMaintenanceService(maintenance,
                Options.Create(new PhoneHomeSettings { Enabled = false }),
                NullLogger<RunnerRepositoryMaintenanceService>.Instance);
            await off.StartAsync(CancellationToken.None);
            await off.StopAsync(CancellationToken.None);
            var gated = new RunnerRepositoryMaintenanceService(maintenance,
                Options.Create(new PhoneHomeSettings
                {
                    Enabled = true,
                    RepositoryMaintenance = new RunnerRepositoryMaintenanceOptions { Enabled = false },
                }),
                NullLogger<RunnerRepositoryMaintenanceService>.Instance);
            await gated.StartAsync(CancellationToken.None);
            await gated.StopAsync(CancellationToken.None);
            maintenance.Last.ShouldBeNull();
        }
        finally { Delete(root); }
    }

    [Test]
    public void C1817_forced_windows_fixture_uses_no_posix_tool()
    {
        MaintenanceFixture.ForceWindows = true;
        try
        {
            Path.GetFileName(MaintenanceFixture.SleepStart(30).FileName).ShouldBe("cmd.exe");
            Path.GetFileName(MaintenanceFixture.TrueStart().FileName).ShouldBe("cmd.exe");
            MaintenanceFixture.HolderStart("packed-refs.lock").FileName.ShouldNotContain("/bin/");
            MaintenanceFixture.IdentityKind.ShouldBe("content");
            var junctionCalled = false;
            Exception? thrown = null;
            try
            {
                MaintenanceFixture.LinkDirectory("link", "target",
                    (_, _) => throw new IOException("symlink privilege"),
                    (_, _) => junctionCalled = true);
            }
            catch (Exception ex) { thrown = ex; }
            junctionCalled.ShouldBeTrue(thrown?.Message);
        }
        finally { MaintenanceFixture.ForceWindows = null; }
    }

    [Test]
    [Timeout(30_000)]
    public void C1817_commit_fixture_pins_identity_and_disables_signing()
    {
        var root = Temp("id");
        try
        {
            var repo = Repo(root, "antiphon", 1);
            var config = File.ReadAllText(Path.Combine(repo, ".git", "config"));
            config.ShouldContain("name = c1817");
            config.ShouldContain("email = c1817@example.com");
            config.ShouldContain("gpgsign = false");
            config.ShouldContain("hooksPath = " + (OperatingSystem.IsWindows() ? "NUL" : "/dev/null"));
        }
        finally { Delete(root); }
    }

    [Test]
    public void C1817_readonly_loose_object_delete_removes_the_tree()
    {
        var root = Temp("ro");
        try
        {
            var objects = Path.Combine(root, "repo", ".git", "objects", "4d");
            Directory.CreateDirectory(objects);
            var loose = Path.Combine(objects, "de070c607382e53f749f4b220511c93de63b");
            File.WriteAllText(loose, "loose");
            File.SetAttributes(loose, File.GetAttributes(loose) | FileAttributes.ReadOnly);
            // Linux Directory.Delete ignores a read-only file. A read-only parent is the
            // refusal this host can raise. Git for Windows sets the bit on the loose object
            // and RemoveDirectoryRecursive throws UnauthorizedAccessException.
            File.SetAttributes(objects, File.GetAttributes(objects) | FileAttributes.ReadOnly);
            Delete(root);
            Directory.Exists(root).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
                ReadOnlyDirectory.Delete(root);
        }
    }

    [Test]
    public void C1817_status_omits_a_null_maintenance_result()
    {
        var sample = new RunnerRepositoryMaintenanceStatus(DateTimeOffset.UnixEpoch, []);
        JsonSerializer.Serialize(new PhoneHomeCapacityHeartbeat(1), PhoneHomeFraming.Json)
            .ShouldNotContain("repositoryMaintenance");
        JsonSerializer.Serialize(new PhoneHomeCapacityHeartbeat(1, RepositoryMaintenance: sample), PhoneHomeFraming.Json)
            .ShouldContain("repositoryMaintenance");
        var status = new PhoneHomeRunnerStatusDto("server2", null, null, null, false, false, null, null, null, null);
        JsonSerializer.Serialize(status, PhoneHomeFraming.Json).ShouldNotContain("repositoryMaintenance");
        JsonSerializer.Serialize(status with { RepositoryMaintenance = sample }, PhoneHomeFraming.Json)
            .ShouldContain("repositoryMaintenance");
    }

    private static RunnerRepositoryMaintenance Build(string root, RunnerRepositoryActivity? activity = null,
        int threshold = 6700, int budget = 120, string? state = null, ILogger? logger = null,
        Func<ProcessStartInfo, Process?>? start = null) =>
        new(root, activity ?? new RunnerRepositoryActivity(), TimeProvider.System,
            logger ?? NullLogger.Instance,
            new RunnerRepositoryMaintenanceOptions { LooseObjectThreshold = threshold, BudgetSeconds = budget },
            state ?? Path.Combine(root, "repository-maintenance.json"), start);

    private static Func<ProcessStartInfo, Process?> Hold(TaskCompletionSource entered, Action<Process> started) =>
        _ =>
        {
            // Return at once. Blocking here never gives GitAsync a Task to await, so the test cannot release it.
            var child = MaintenanceFixture.StartSleep(30);
            started(child);
            entered.TrySetResult();
            return child;
        };

    private static void Stop(Process? child)
    {
        if (child is null)
            return;
        try
        {
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
        }
        catch { /* already reaped */ }
    }

    private static string Repo(string parent, string name, int commits)
    {
        var path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        Git(path, "init", "-q");
        Git(path, "config", "gc.auto", "0");
        Git(path, "config", "maintenance.auto", "false");
        Git(path, "config", "user.name", "c1817");
        Git(path, "config", "user.email", "c1817@example.com");
        Git(path, "config", "commit.gpgsign", "false");
        Git(path, "config", "core.hooksPath", OperatingSystem.IsWindows() ? "NUL" : "/dev/null");
        for (var i = 0; i < commits; i++)
        {
            File.WriteAllText(Path.Combine(path, "f" + i + ".txt"), "body " + i);
            Git(path, "add", "--", "f" + i + ".txt");
            Git(path, "commit", "-q", "-m", "c" + i);
        }
        return path;
    }

    private static void Git(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var key in psi.Environment.Keys.Where(key => key.StartsWith("GIT_CONFIG_", StringComparison.Ordinal)).ToArray())
            psi.Environment.Remove(key);
        psi.Environment["GIT_AUTHOR_NAME"] = "c1817";
        psi.Environment["GIT_AUTHOR_EMAIL"] = "c1817@example.com";
        psi.Environment["GIT_COMMITTER_NAME"] = "c1817";
        psi.Environment["GIT_COMMITTER_EMAIL"] = "c1817@example.com";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("git did not start");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.Join(' ', args) + " -> " + stderr + stdout);
    }

    private static string Temp(string name) =>
        Path.Combine(Path.GetTempPath(), "c1817-" + name + "-" + Guid.NewGuid().ToString("N"));

    private static void Unlink(string path)
    {
        try { Directory.Delete(path); }
        catch { try { File.Delete(path); } catch { /* already gone */ } }
    }

    private static void Delete(string path)
    {
        if (!Directory.Exists(path))
            return;
        // Git for Windows marks loose objects and commit-graph read-only.
        // ReadOnlyDirectory clears that bit before Directory.Delete.
        ReadOnlyDirectory.Delete(path);
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}
