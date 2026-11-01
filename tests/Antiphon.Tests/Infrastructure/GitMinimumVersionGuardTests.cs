using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Infrastructure;

// CARD-1260: execute docker/git-minimum-version.sh, the one declaration of the Git landing floor
// that the server and runner images both run at build time. Debian bookworm's 2.39.5, and any
// version below 2.43 by version order (not string order), must fail the build; the floor itself and
// the pinned 2.47.3 pass. Without an argument the guard reads the installed `git --version`.
[NativeLinuxOnly("CARD-1260: the Git floor guard runs under /bin/sh with GNU sort -V, as in the Debian images; Windows skips explicitly.")]
[Timeout(600_000)]
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GitMinimumVersionGuardTests
{
    internal const string NativeLinuxReason = "CARD-1260: the Git floor guard runs under /bin/sh with GNU sort -V, as in the Debian images; Windows skips explicitly.";

    [Test]
    public async Task Guard_refuses_git_below_the_landing_floor()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        foreach (var version in new[] { "2.39.5", "2.42.9", "2.9.0", "1.99.99" })
        {
            var refused = await Guard(version, fakeGit: null);
            refused.ShouldBe(new GuardResult(1, "", "git-minimum-version: Git " + version + " is below minimum 2.43\n"),
                "below-floor " + version);
        }

        foreach (var version in new[] { "2.43", "2.43.0", "2.47.3", "2.100.0", "3.0.0" })
        {
            var admitted = await Guard(version, fakeGit: null);
            admitted.ShouldBe(new GuardResult(0, "git-minimum-version: Git " + version + " meets minimum 2.43\n", ""),
                "at-or-above-floor " + version);
        }

        foreach (var version in new[] { "", "garbage", "2.47.3 extra", "2.39.5.windows.1", ".2.43", "2.43.", "2..43" })
        {
            var unparseable = await Guard(version, fakeGit: null);
            unparseable.ShouldBe(new GuardResult(1, "", "git-minimum-version: unparseable Git version '" + version + "'\n"),
                "unparseable " + version);
        }

        // No argument: the guard asks the installed git, exactly as the image RUN step does.
        (await Guard(null, "printf 'git version 2.39.5\\n'"))
            .ShouldBe(new GuardResult(1, "", "git-minimum-version: Git 2.39.5 is below minimum 2.43\n"), "installed-bookworm-git");
        (await Guard(null, "printf 'git version 2.47.3\\n'"))
            .ShouldBe(new GuardResult(0, "git-minimum-version: Git 2.47.3 meets minimum 2.43\n", ""), "installed-pinned-git");
        (await Guard(null, "exit 127"))
            .ShouldBe(new GuardResult(1, "", "git-minimum-version: unparseable Git version ''\n"), "installed-git-unusable");
    }

    private static async Task<GuardResult> Guard(string? version, string? fakeGit)
    {
        var script = Path.Combine(DockerStackDocuments.RepoRoot, "docker", "git-minimum-version.sh");
        var dir = Path.Combine(Path.GetTempPath(), "git-floor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var startInfo = new ProcessStartInfo("/bin/sh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(script);
            if (version is not null) startInfo.ArgumentList.Add(version);
            if (fakeGit is not null)
            {
                var git = Path.Combine(dir, "git");
                await File.WriteAllTextAsync(git, "#!/bin/sh\n" + fakeGit + "\n");
                File.SetUnixFileMode(git, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                startInfo.Environment["PATH"] = dir + ":" + Environment.GetEnvironmentVariable("PATH");
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("sh did not start");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("git floor guard exceeded 15s");
            }

            return new GuardResult(process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private readonly record struct GuardResult(int ExitCode, string Stdout, string Stderr);
}
