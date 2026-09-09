using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeRemovalAuthorityTests
{
    [Test]
    public async Task C448_V35_BuildJunkScriptPreservesOpaqueWildcardMatches()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var path = Path.Combine(fixture.Source, "bin-private", "nested", "bin-archive", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "irreplaceable");
        Directory.SetLastWriteTime(Path.Combine(fixture.Source, "bin-private"), DateTime.Now.AddDays(-10));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "scripts", "cleanup-build-junk.ps1"))) root = root.Parent;
        root.ShouldNotBeNull();
        var start = new System.Diagnostics.ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(root.FullName, "scripts", "cleanup-build-junk.ps1"),
                     "-RepoRoot", fixture.Source, "-SkipIfModifiedWithinMinutes", "0" }) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
        process.ExitCode.ShouldBe(0, await error);
        File.Exists(path).ShouldBeTrue("build cleanup must preserve opaque wildcard-matched bytes");
        (await output).ShouldContain("0 dir(s) removed");
        (await File.ReadAllTextAsync(path)).ShouldBe("irreplaceable");
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(".antiphon/report.md")]
    [Arguments(".claude/settings.local.json")]
    [Arguments("bin-private/irreplaceable.txt")]
    [Arguments("untracked.txt")]
    [Arguments("keep.txt")]
    public async Task C448_V24_LegacyRemovalCannotEraseTaskContents(string relative)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var file = Path.Combine(fixture.Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "opaque user work\n");
        var manager = new WorktreeManager(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(fixture.Root, "trees"),
        }), TimeProvider.System, NullLogger<WorktreeManager>.Instance);

        var result = await manager.TryRemoveAsync(fixture.Repository, fixture.Source, "master", CancellationToken.None);

        File.Exists(file).ShouldBeTrue("removal without typed task authority must preserve the sentinel");
        (await File.ReadAllTextAsync(file)).ShouldBe("opaque user work\n");
        result.IsClean.ShouldBeFalse();
        (await fixture.RequiredAsync(fixture.Repository, "rev-parse", fixture.SourceRef)).Trim().ShouldBe(fixture.SeedSha);
        (await fixture.RequiredAsync(fixture.Repository, "worktree", "list", "--porcelain")).ShouldContain(fixture.Source.Replace('\\', '/'));
        await fixture.AssertRemoteSourceAsync();
    }
}
