using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1818. A fault in one repository or one pass does not end the owner, a budget kill
/// leaves packed-refs.lock in place, and a symlinked .git is not a repository.
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
                return MaintenanceFixture.StartTrue();
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
    [Timeout(60_000)]
    public async Task C1818_budget_kill_reports_a_lock_the_gc_child_wrote()
    {
        var root = Temp("child-lock");
        var children = new List<Process>();
        var log = new CaptureLogger();
        try
        {
            var oldRepo = Repo(root, "oldrepo");
            var childRepo = Repo(root, "childrepo");
            var oldLock = Path.Combine(oldRepo, ".git", "packed-refs.lock");
            var childLock = Path.Combine(childRepo, ".git", "packed-refs.lock");
            File.WriteAllText(oldLock, "");
            File.SetLastWriteTimeUtc(oldLock, DateTime.UtcNow.AddHours(-2));
            string? childIdentity = null;
            var maintenance = Build(root, budget: 1, log, start: psi =>
            {
                Process child;
                if (psi.WorkingDirectory.Contains($"{Path.DirectorySeparatorChar}childrepo", StringComparison.Ordinal))
                {
                    // The start delegate runs after the pass stamps its start, so this mtime is during the pass.
                    File.WriteAllText(childLock, "c1818-child-lock");
                    childIdentity = MaintenanceFixture.Identity(childLock);
                    child = MaintenanceFixture.StartSleep(30);
                }
                else
                {
                    child = MaintenanceFixture.StartSleep(30);
                }
                children.Add(child);
                return child;
            });

            var status = await maintenance.RunAsync(CancellationToken.None, force: true);

            File.Exists(oldLock).ShouldBeTrue();
            File.Exists(childLock).ShouldBeTrue();
            MaintenanceFixture.Identity(childLock).ShouldBe(childIdentity);
            status.Repositories.ShouldContain(report =>
                report.Repository.EndsWith($"{Path.DirectorySeparatorChar}oldrepo", StringComparison.Ordinal)
                && report.Outcome == "failed" && report.Reason == "budget");
            status.Repositories.ShouldContain(report =>
                report.Repository.EndsWith($"{Path.DirectorySeparatorChar}childrepo", StringComparison.Ordinal)
                && report.Outcome == "failed" && report.Reason == "packed_refs_lock_left");
            log.Lines.ShouldContain(line =>
                line.Contains("left packed-refs.lock", StringComparison.Ordinal)
                && line.Contains(childRepo, StringComparison.Ordinal)
                && line.Contains("ageSeconds=", StringComparison.Ordinal));
            log.Lines.ShouldNotContain(line =>
                line.Contains("left packed-refs.lock", StringComparison.Ordinal)
                && line.Contains(oldRepo, StringComparison.Ordinal));
        }
        finally
        {
            foreach (var child in children)
                Stop(child);
            Delete(root);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task C1818_budget_kill_leaves_a_lock_held_by_an_outside_process()
    {
        var root = Temp("held-lock");
        var children = new List<Process>();
        Process? holder = null;
        var log = new CaptureLogger();
        try
        {
            var heldRepo = Repo(root, "heldrepo");
            var heldLock = Path.Combine(heldRepo, ".git", "packed-refs.lock");
            string? heldIdentity = null;
            var gcPid = 0;
            var maintenance = Build(root, budget: 1, log, start: psi =>
            {
                if (psi.WorkingDirectory.Contains($"{Path.DirectorySeparatorChar}heldrepo", StringComparison.Ordinal))
                {
                    File.WriteAllText(heldLock, "c1818-held-lock");
                    var outside = MaintenanceFixture.StartHolder(heldLock);
                    holder = outside;
                    WaitUntil(() => HolderHasLock(outside, heldLock), "outside process did not hold packed-refs.lock");
                    heldIdentity = MaintenanceFixture.Identity(heldLock);
                }
                var child = MaintenanceFixture.StartSleep(30);
                gcPid = child.Id;
                children.Add(child);
                return child;
            });

            var status = await maintenance.RunAsync(CancellationToken.None, force: true);

            holder.ShouldNotBeNull();
            holder!.HasExited.ShouldBeFalse();
            HolderHasLock(holder, heldLock).ShouldBeTrue();
            File.Exists(heldLock).ShouldBeTrue();
            MaintenanceFixture.Identity(heldLock).ShouldBe(heldIdentity);
            gcPid.ShouldBeGreaterThan(0);
            MaintenanceFixture.ProcessGone(gcPid).ShouldBeTrue();
            status.Repositories.ShouldContain(report =>
                report.Repository.EndsWith($"{Path.DirectorySeparatorChar}heldrepo", StringComparison.Ordinal)
                && report.Outcome == "failed" && report.Reason == "packed_refs_lock_left");
            log.Lines.ShouldContain(line =>
                line.Contains("left packed-refs.lock", StringComparison.Ordinal)
                && line.Contains(heldRepo, StringComparison.Ordinal)
                && line.Contains("ageSeconds=", StringComparison.Ordinal));
        }
        finally
        {
            foreach (var child in children)
                Stop(child);
            Stop(holder);
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
            MaintenanceFixture.LinkDirectory(link, Path.Combine(real, ".git"));
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
        invariants.ShouldContain("row reason is `packed_refs_lock_left`");
        invariants.ShouldContain("names the repository and the lock age");
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
        ILogger? logger = null, Func<ProcessStartInfo, Process?>? start = null) =>
        new(root, new RunnerRepositoryActivity(), TimeProvider.System, logger ?? NullLogger.Instance,
            new RunnerRepositoryMaintenanceOptions { LooseObjectThreshold = 6700, BudgetSeconds = budget },
            Path.Combine(root, "repository-maintenance.json"), start);

    private static void WaitUntil(Func<bool> ready, string failure)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < deadline)
            Thread.Sleep(20);
        ready().ShouldBeTrue(failure);
    }

    private static bool HolderHasLock(Process holder, string lockPath)
    {
        if (holder.HasExited)
            return false;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        var fdDir = "/proc/" + holder.Id + "/fd";
        if (!Directory.Exists(fdDir))
            return false;
        var full = Path.GetFullPath(lockPath);
        foreach (var entry in Directory.EnumerateFileSystemEntries(fdDir))
        {
            string? target;
            try { target = Directory.ResolveLinkTarget(entry, returnFinalTarget: false)?.FullName; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { continue; }
            if (target is not null && string.Equals(Path.GetFullPath(target), full, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string Repo(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        Git(path, "init", "-q");
        Git(path, "config", "gc.auto", "0");
        Git(path, "config", "user.name", "c1818");
        Git(path, "config", "user.email", "c1818@example.com");
        Git(path, "config", "commit.gpgsign", "false");
        Git(path, "config", "core.hooksPath", OperatingSystem.IsWindows() ? "NUL" : "/dev/null");
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
        // Same read-only loose-object refusal as C1817. Clear the bit, then delete.
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
