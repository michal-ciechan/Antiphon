using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1818. A fault in one repository or one pass does not end the owner, a budget kill
/// does not leave the packed-refs lock it created, and a symlinked .git is not a repository.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C1818GcOwnerRobustnessTests
{
    [Test]
    [Timeout(60_000)]
    public async Task C1818_one_repository_fault_does_not_abort_the_pass()
    {
        var root = Temp("fault");
        try
        {
            Repo(root, "badrepo");
            Repo(root, "goodrepo");
            var maintenance = Build(root, start: psi =>
            {
                if (psi.WorkingDirectory.Contains("badrepo", StringComparison.Ordinal))
                    throw new Exception("unreadable-objects");
                return Process.Start(new ProcessStartInfo("/bin/true")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
            });

            var status = await maintenance.RunAsync(CancellationToken.None, force: true);

            status.Repositories.Count.ShouldBe(2);
            status.Repositories.ShouldContain(report =>
                report.Repository.EndsWith($"{Path.DirectorySeparatorChar}badrepo", StringComparison.Ordinal)
                && report.Outcome == "failed" && report.Reason == "error");
            status.Repositories.ShouldContain(report =>
                report.Repository.EndsWith($"{Path.DirectorySeparatorChar}goodrepo", StringComparison.Ordinal)
                && report.Outcome == "ran" && report.Reason == null);
        }
        finally { Delete(root); }
    }

    [Test]
    [Timeout(15_000)]
    public async Task C1818_maintenance_owner_retries_on_the_next_tick()
    {
        var root = Temp("owner");
        Directory.CreateDirectory(root);
        var calls = 0;
        var maintenance = Build(root);
        maintenance.BeforePass = () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("owner-fault");
        };
        var service = new RunnerRepositoryMaintenanceService(
            maintenance,
            Options.Create(new PhoneHomeSettings { Enabled = true }),
            NullLogger<RunnerRepositoryMaintenanceService>.Instance)
        {
            InitialDelay = TimeSpan.Zero,
            Interval = TimeSpan.FromMilliseconds(40),
        };
        try
        {
            await service.StartAsync(CancellationToken.None);
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (Volatile.Read(ref calls) < 2 && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            Volatile.Read(ref calls).ShouldBeGreaterThanOrEqualTo(2);
            maintenance.Last.ShouldNotBeNull();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            Delete(root);
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task C1818_budget_kill_removes_a_stale_packed_refs_lock()
    {
        var root = Temp("lock");
        var children = new List<Process>();
        try
        {
            var oldRepo = Repo(root, "oldrepo");
            var freshRepo = Repo(root, "freshrepo");
            var oldLock = Path.Combine(oldRepo, ".git", "packed-refs.lock");
            var freshLock = Path.Combine(freshRepo, ".git", "packed-refs.lock");
            File.WriteAllText(oldLock, "");
            File.SetLastWriteTimeUtc(oldLock, DateTime.UtcNow.AddHours(-2));
            var maintenance = Build(root, budget: 1, start: psi =>
            {
                if (psi.WorkingDirectory.Contains("freshrepo", StringComparison.Ordinal))
                    File.WriteAllText(freshLock, "");
                var child = Process.Start(new ProcessStartInfo("/bin/sleep", "30")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                }) ?? throw new InvalidOperationException("sleep did not start");
                children.Add(child);
                return child;
            });

            var status = await maintenance.RunAsync(CancellationToken.None, force: true);

            File.Exists(oldLock).ShouldBeTrue();
            File.Exists(freshLock).ShouldBeFalse();
            status.Repositories.ShouldContain(report =>
                report.Repository.EndsWith($"{Path.DirectorySeparatorChar}oldrepo", StringComparison.Ordinal)
                && report.Outcome == "failed" && report.Reason == "budget");
            status.Repositories.ShouldContain(report =>
                report.Repository.EndsWith($"{Path.DirectorySeparatorChar}freshrepo", StringComparison.Ordinal)
                && report.Outcome == "failed" && report.Reason == "budget");
        }
        finally
        {
            foreach (var child in children)
                Stop(child);
            Delete(root);
        }
    }

    [Test]
    public void C1818_symlinked_git_directory_is_not_a_repository()
    {
        var root = Temp("link");
        try
        {
            var real = Repo(root, "real");
            var mirror = Path.Combine(root, "mirror");
            Directory.CreateDirectory(mirror);
            var link = Path.Combine(mirror, ".git");
            File.CreateSymbolicLink(link, Path.Combine(real, ".git"));
            Directory.Exists(link).ShouldBeTrue();

            var found = RunnerRepositoryMaintenance.Discover(root);

            found.ShouldContain(Path.GetFullPath(real));
            found.ShouldNotContain(Path.GetFullPath(mirror));
        }
        finally { Delete(root); }
    }

    [Test]
    public async Task C1818_park_workspace_shares_repository_activity()
    {
        var temp = Temp("park");
        Directory.CreateDirectory(temp);
        var activity = new RunnerRepositoryActivity();
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = temp }),
            NullLogger<SessionRunnerRuntime>.Instance,
            phoneHomeSettings: Options.Create(new PhoneHomeSettings { Enabled = true, AllowedCwd = temp }),
            repositoryActivity: activity);
        try
        {
            runtime.ParkWorkspaceForTest.ShouldNotBeNull();
            runtime.ParkWorkspaceForTest!.Activity.ShouldBeSameAs(activity);
        }
        finally { Delete(temp); }
    }

    [Test]
    public void C1818_retention_workspace_binds_repository_activity()
    {
        Source("src/Antiphon.SessionRunner/Program.cs")
            .ShouldContain("workspace.Activity = sp.GetRequiredService<RunnerRepositoryActivity>();");
    }

    [Test]
    public void C1818_dispatcher_workspace_shares_repository_activity()
    {
        var activity = new RunnerRepositoryActivity();
        var dispatcher = new PhoneHomeCommandDispatcher(
            new PhoneHomeCommandDispatcherTests.RecordingRuntime(),
            new PhoneHomeSettings { Enabled = true, AllowedCwd = "/tmp/c1818-dispatcher" },
            activity: activity);
        dispatcher.WorkspaceForTest().Activity.ShouldBeSameAs(activity);
    }

    [Test]
    public void C1818_runner_maps_repository_maintenance()
    {
        Source("src/Antiphon.SessionRunner/Program.cs")
            .ShouldContain("app.MapGet(\"/repository-maintenance\"");
    }

    [Test]
    public void C1818_skip_docs_name_the_counted_git_calls()
    {
        var invariants = Source("docs/session-runtime-invariants.md");
        var stack = Source("docs/docker-stack.md");
        invariants.ShouldContain("dispatcher workspace, the retention workspace, or the park workspace");
        invariants.ShouldContain("Git an agent runs itself inside a worktree is not counted");
        invariants.ShouldContain("does not stop the pass or the owner");
        invariants.ShouldContain("packed-refs.lock");
        invariants.ShouldContain("symlinked .git");
        stack.ShouldContain("Git an agent runs itself is not counted");
    }

    private static string Source(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        dir.ShouldNotBeNull();
        return File.ReadAllText(Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static RunnerRepositoryMaintenance Build(string root, int budget = 120,
        Func<ProcessStartInfo, Process?>? start = null) =>
        new(root, new RunnerRepositoryActivity(), TimeProvider.System, NullLogger.Instance,
            new RunnerRepositoryMaintenanceOptions { LooseObjectThreshold = 6700, BudgetSeconds = budget },
            Path.Combine(root, "repository-maintenance.json"), start);

    private static string Repo(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        Git(path, "init", "-q");
        Git(path, "config", "gc.auto", "0");
        File.WriteAllText(Path.Combine(path, "f.txt"), "body");
        Git(path, "add", "--", "f.txt");
        Git(path, "commit", "-q", "-m", "c");
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
        psi.Environment["GIT_AUTHOR_NAME"] = "c1818";
        psi.Environment["GIT_AUTHOR_EMAIL"] = "c1818@example.com";
        psi.Environment["GIT_COMMITTER_NAME"] = "c1818";
        psi.Environment["GIT_COMMITTER_EMAIL"] = "c1818@example.com";
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
        Path.Combine(Path.GetTempPath(), "c1818-" + name + "-" + Guid.NewGuid().ToString("N"));

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

    private static void Delete(string path)
    {
        if (!Directory.Exists(path))
            return;
        try { Directory.Delete(path, recursive: true); }
        catch (IOException)
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
    }
}
