using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class GitCommandExecutorTests
{
    [Test]
    public async Task ArgumentVectorAndWorkingDirectoryRoundTrip()
    {
        var root = Directory.CreateTempSubdirectory("antiphon git argv ").FullName;
        try
        {
            var env = Identity(root);
            var git = new CliGitCommandExecutor(env);
            (await Run(git, root, "init", "-b", "master")).ExitCode.ShouldBe(0);
            const string content = "exact content\n";
            await File.WriteAllTextAsync(Path.Combine(root, "file with spaces.txt"), content);
            (await Run(git, root, "add", "file with spaces.txt")).ExitCode.ShouldBe(0);
            const string message = "subject with \"quotes\"\nsecond line";
            (await Run(git, root, "commit", "-m", message, "--trailer", "antiphon=true")).ExitCode.ShouldBe(0);
            (await Run(git, root, "show", "HEAD:file with spaces.txt")).Stdout.ShouldBe(content);
            var log = (await Run(git, root, "log", "-1", "--format=%B")).Stdout;
            log.ShouldContain("subject with \"quotes\"");
            log.ShouldContain("second line");
            log.ShouldContain("antiphon: true");
        }
        finally { Delete(root); }
    }

    [Test]
    public async Task NonzeroExitPreservesBothStreams()
    {
        var probe = Pwsh();
        var result = await Run(probe, Environment.CurrentDirectory, "-NoProfile", "-Command",
            "[Console]::Out.Write('OUT'); [Console]::Error.Write('ERR'); exit 17");
        result.ExitCode.ShouldBe(17);
        result.Stdout.ShouldBe("OUT");
        result.Stderr.ShouldContain("ERR");
    }

    [Test]
    public async Task ConcurrentStreamDrainDoesNotDeadlock()
    {
        var probe = Pwsh();
        var result = await Run(probe, Environment.CurrentDirectory, "-NoProfile", "-Command",
            "[Console]::Out.Write('A' * 131072); [Console]::Error.Write('B' * 131072)");
        result.ExitCode.ShouldBe(0);
        result.Stdout.Length.ShouldBe(131072);
        result.Stderr.Length.ShouldBe(131072);
    }

    [Test]
    public async Task CancellationKillsAndAwaitsOwnedTree()
    {
        var probe = Pwsh();
        var root = Directory.CreateTempSubdirectory("antiphon-git-tree-").FullName;
        var pidFile = Path.Combine(root, "owned-pids.txt");
        var escaped = pidFile.Replace("'", "''", StringComparison.Ordinal);
        var script = "$child=Start-Process pwsh -ArgumentList @('-NoProfile','-Command','Start-Sleep -Seconds 30') -PassThru; "
            + $"Set-Content -LiteralPath '{escaped}' -Value \"$PID,$($child.Id)\"; "
            + "Start-Sleep -Seconds 30";
        using var cts = new CancellationTokenSource();
        var command = probe.ExecuteAsync(Environment.CurrentDirectory,
            ["-NoProfile", "-Command", script], TimeSpan.FromSeconds(40), cts.Token);
        int[] pids = [];
        try
        {
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (!File.Exists(pidFile) && wait.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(20);
            File.Exists(pidFile).ShouldBeTrue("The owned parent and child must announce their identities.");
            pids = (await File.ReadAllTextAsync(pidFile)).Trim().Split(',').Select(int.Parse).ToArray();
            pids.Length.ShouldBe(2);
            cts.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => command);
            wait.Restart();
            while (pids.Any(Alive) && wait.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(50);
            pids.Any(Alive).ShouldBeFalse("Cancellation must terminate the owned process tree.");
        }
        finally
        {
            cts.Cancel();
            foreach (var pid in pids)
            {
                try { using var process = System.Diagnostics.Process.GetProcessById(pid); process.Kill(true); }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            Delete(root);
        }
    }

    [Test]
    public async Task CallerTimeoutIsHonored()
    {
        var probe = Pwsh();
        var started = System.Diagnostics.Stopwatch.StartNew();
        await Should.ThrowAsync<OperationCanceledException>(() => probe.ExecuteAsync(
            Environment.CurrentDirectory, ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"],
            TimeSpan.FromMilliseconds(200), CancellationToken.None));
        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task PreCanceledCallDoesNotLaunch()
    {
        var launches = 0;
        var git = new CliGitCommandExecutor(onLaunch: () => launches++);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => git.ExecuteAsync(
            Environment.CurrentDirectory, ["--version"], TimeSpan.FromSeconds(1), cts.Token));
        launches.ShouldBe(0);
    }

    [Test]
    public async Task RealFixtureIgnoresAmbientGitConfiguration()
    {
        var poisonRoot = Directory.CreateTempSubdirectory("antiphon-poison-git-").FullName;
        var poisonConfig = Path.Combine(poisonRoot, "config");
        await File.WriteAllTextAsync(poisonConfig, "[user]\n\tname = Poison\n\temail = poison@example.invalid\n");
        var ambient = new Dictionary<string, string>
        {
            ["HOME"] = poisonRoot,
            ["GIT_CONFIG_GLOBAL"] = poisonConfig,
            ["GIT_AUTHOR_NAME"] = "Poison",
            ["GIT_AUTHOR_EMAIL"] = "poison@example.invalid",
            ["GIT_COMMITTER_NAME"] = "Poison",
            ["GIT_COMMITTER_EMAIL"] = "poison@example.invalid"
        };
        await using var fixture = new Antiphon.Tests.TestHelpers.FakeGit.TestGitBackendHost(() => "1", ambient).Create();
        await fixture.InitializeAsync();
        var log = await fixture.RequiredAsync("log", "-1", "--format=%B");
        log.ShouldContain("Initial commit");
        (await fixture.RequiredAsync("log", "-1", "--format=%an|%ae"))
            .Trim().ShouldBe("Antiphon Test|test@antiphon.dev");
        fixture.GitLaunches.ShouldBeGreaterThan(0);
        Directory.Delete(poisonRoot, true);
    }

    private static CliGitCommandExecutor Pwsh() => new(executable: "pwsh");
    private static bool Alive(int pid)
    {
        try { using var process = System.Diagnostics.Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
    private static Task<Antiphon.Server.Application.Interfaces.GitCommandResult> Run(
        CliGitCommandExecutor executor, string path, params string[] args) =>
        executor.ExecuteAsync(path, args, TimeSpan.FromSeconds(30), CancellationToken.None);

    private static Dictionary<string, string> Identity(string root) => new()
    {
        ["HOME"] = root,
        ["GIT_CONFIG_GLOBAL"] = Path.Combine(root, "empty-config"),
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_AUTHOR_NAME"] = "Antiphon Test",
        ["GIT_AUTHOR_EMAIL"] = "test@antiphon.dev",
        ["GIT_COMMITTER_NAME"] = "Antiphon Test",
        ["GIT_COMMITTER_EMAIL"] = "test@antiphon.dev"
    };

    private static void Delete(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }
}
