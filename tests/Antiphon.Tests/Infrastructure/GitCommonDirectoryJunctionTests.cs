using System.Diagnostics;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// A junction above a checkout that is re-pointed at another tree is a new layout for the child
/// journal's common-dir cache, even when the two <c>.git</c> entries carry identical timestamps.
/// Starts <c>cmd /c mklink /J</c>, so it is on the process lane rather than the git lane.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GitCommonDirectoryJunctionTests
{
    [Test]
    public async Task A_repointed_junction_changes_the_stamp()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var root = Path.Combine(Path.GetTempPath(), "antiphon-gcd-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            foreach (var tree in new[] { first, second })
            {
                Directory.CreateDirectory(Path.Combine(tree, ".git"));
                await File.WriteAllTextAsync(Path.Combine(tree, ".git", "HEAD"), "ref: refs/heads/master\n");
            }
            var created = Directory.GetCreationTimeUtc(Path.Combine(first, ".git"));
            Directory.SetCreationTimeUtc(Path.Combine(second, ".git"), created);
            var link = Path.Combine(root, "checkout");

            await JunctionAsync(link, first);
            var before = GitCommonDirectory.Stamp(link);
            Directory.Delete(link); // removes the junction, not its target
            await JunctionAsync(link, second);
            var after = GitCommonDirectory.Stamp(link);

            before.ShouldNotBeNull();
            after.ShouldNotBeNull();
            after.ShouldNotBe(before, "same timestamps, different tree behind the junction");
            Directory.Exists(Path.Combine(first, ".git")).ShouldBeTrue();
        }
        finally
        {
            try
            {
                var link = Path.Combine(root, "checkout");
                if (Directory.Exists(link)) Directory.Delete(link);
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
        }
    }

    private static async Task JunctionAsync(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("mklink did not start");
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await process.StandardError.ReadToEndAsync());
    }
}
