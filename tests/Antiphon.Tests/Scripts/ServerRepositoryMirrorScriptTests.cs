using System.Diagnostics;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1776. The mirror script plans and refuses without cloning. It does not
/// start docker and it does not print a token.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ServerRepositoryMirrorScriptTests
{
    [Test]
    public async Task WhatIf_maps_desktop_paths_and_does_not_create_them()
    {
        var root = NewRoot();
        try
        {
            var table = Path.Combine(root, "table.txt");
            var repos = Path.Combine(root, "repos");
            await File.WriteAllTextAsync(table, """
                antiphon|C:/src/Antiphon|https://github.com/michal-ciechan/Antiphon.git
                gym-stat|C:/src/gym-stat|https://github.com/example/gym-stat.git
                markdown-package|C:\src\markdown-package|https://github.com/example/markdown-package.git
                """);
            var run = await RunAsync("-TableFile", table, "-ReposRoot", repos);
            run.Exit.ShouldBe(0, run.Output);
            run.Output.ShouldContain("reason=planned");
            run.Output.ShouldContain("after=" + repos + "/antiphon");
            run.Output.ShouldContain("after=" + repos + "/gym-stat");
            run.Output.ShouldContain("after=" + repos + "/markdown-package");
            run.Output.ShouldContain("before=C:/src/Antiphon");
            run.Output.ShouldContain("before=C:\\src\\markdown-package");
            run.Output.ShouldContain("rollback restores the before column");
            Directory.Exists(Path.Combine(repos, "antiphon")).ShouldBeFalse();
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Empty_antiphon_remote_is_unrecorded()
    {
        var root = NewRoot();
        try
        {
            var table = Path.Combine(root, "table.txt");
            await File.WriteAllTextAsync(table, "antiphon|C:/src/Antiphon|\n");
            var run = await RunAsync("-TableFile", table);
            run.Exit.ShouldBe(2, run.Output);
            run.Output.ShouldContain("reason=remote-unrecorded");
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Execute_without_a_token_refuses_before_clone()
    {
        var root = NewRoot();
        try
        {
            var table = Path.Combine(root, "table.txt");
            var repos = Path.Combine(root, "repos");
            var token = Path.Combine(root, "missing-token");
            var policy = Path.Combine(root, "policy");
            Directory.CreateDirectory(repos);
            await File.WriteAllTextAsync(table, "antiphon|C:/src/Antiphon|https://github.com/michal-ciechan/Antiphon.git\n");
            var run = await RunAsync("-Execute", "-TableFile", table, "-ReposRoot", repos, "-TokenFile", token, "-PolicyFile", policy);
            run.Exit.ShouldBe(2, run.Output);
            run.Output.ShouldContain("reason=credential-absent");
            Directory.Exists(Path.Combine(repos, "antiphon")).ShouldBeFalse();
            File.Exists(policy).ShouldBeFalse();
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Present_token_still_refuses_before_git_when_the_mirror_parent_is_missing()
    {
        var root = NewRoot();
        try
        {
            var table = Path.Combine(root, "table.txt");
            var repos = Path.Combine(root, "repos");
            var token = Path.Combine(root, "token");
            var policy = Path.Combine(root, "policy");
            await File.WriteAllTextAsync(table, "antiphon|C:/src/Antiphon|https://github.com/michal-ciechan/Antiphon.git\n");
            await File.WriteAllTextAsync(token, "CANARY-TOKEN-VALUE-1776\n");
            var run = await RunAsync("-Execute", "-TableFile", table, "-ReposRoot", repos, "-TokenFile", token, "-PolicyFile", policy);
            run.Exit.ShouldBe(2, run.Output);
            run.Output.ShouldContain("reason=mirror-parent-missing");
            run.Output.ShouldNotContain("CANARY-TOKEN-VALUE-1776");
            File.Exists(policy).ShouldBeFalse();
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public async Task Both_switches_are_ambiguous_and_secrets_and_volumes_are_refused()
    {
        var root = NewRoot();
        try
        {
            var table = Path.Combine(root, "table.txt");
            await File.WriteAllTextAsync(table, "antiphon|C:/src/Antiphon|https://github.com/michal-ciechan/Antiphon.git\n");
            var both = await RunAsync("-WhatIf", "-Execute", "-TableFile", table);
            both.Exit.ShouldBe(3, both.Output);
            both.Output.ShouldContain("reason=ambiguous-mode");

            await File.WriteAllTextAsync(table, "antiphon|C:/src/antiphon_pgdata|https://github.com/michal-ciechan/Antiphon.git\n");
            var volume = await RunAsync("-TableFile", table);
            volume.Exit.ShouldBe(2, volume.Output);
            volume.Output.ShouldContain("reason=volume-forbidden");

            var secret = "ghp_" + new string('a', 20);
            await File.WriteAllTextAsync(table, "antiphon|C:/src/Antiphon|https://user:" + secret + "@github.com/michal-ciechan/Antiphon.git\n");
            var leaked = await RunAsync("-TableFile", table);
            leaked.Exit.ShouldBe(2, leaked.Output);
            leaked.Output.ShouldContain("reason=secret-in-table");
            leaked.Output.ShouldNotContain(secret);

            await File.WriteAllTextAsync(table, "antiphon|C:/src/../Antiphon|https://github.com/michal-ciechan/Antiphon.git\n");
            var escape = await RunAsync("-TableFile", table);
            escape.Exit.ShouldBe(2, escape.Output);
            escape.Output.ShouldContain("reason=path-forbidden");
        }
        finally { DeleteRoot(root); }
    }

    private static async Task<(int Exit, string Output)> RunAsync(params string[] args)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "server2-repository-mirrors.ps1"));
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("mirror script hung");
        }

        return (process.ExitCode, (await stdout) + (await stderr));
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "card-1776-mirrors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
