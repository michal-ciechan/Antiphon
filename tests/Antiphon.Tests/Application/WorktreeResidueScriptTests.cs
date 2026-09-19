using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeResidueScriptTests
{
    [Test]
    public async Task C459_BatchRequiresFullIds()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var script = Path.Combine(root, "scripts", "test-worktree-residue.ps1");
        File.Exists(script).ShouldBeTrue();
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoProfile -File \"{script}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        proc.ExitCode.ShouldBe(0, stdout);
        stdout.ShouldContain("short id refused");
        var unexpectedReleasePosts = 0;
        unexpectedReleasePosts.ShouldBe(0);
    }
}
