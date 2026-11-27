using System.Diagnostics;
using Antiphon.TestSupport;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[NativeLinuxOnly("Linux procfs and GNU find are used by the evidence sweep.")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class Server2EvidenceRetentionTests
{
    [Test]
    public async Task WorkingDirectoryAloneRetainsEvidence()
    {
        var folder = Directory.CreateTempSubdirectory("retention-cwd-");
        try
        {
            var start = new ProcessStartInfo("sleep") { WorkingDirectory = folder.FullName };
            start.ArgumentList.Add("60");
            using var holder = Process.Start(start)!;
            try
            {
                new FileInfo(Path.Combine("/proc", holder.Id.ToString(), "cwd")).LinkTarget.ShouldBe(folder.FullName);
                (await RunAsync(folder.FullName, "Get-AntiphonRetentionHolder $env:RETENTION_FIXTURE"))
                    .Trim().ShouldBe("live-holder", "the process has no file open inside the directory");
            }
            finally
            {
                if (!holder.HasExited)
                    holder.Kill();
                await holder.WaitForExitAsync();
            }
        }
        finally { folder.Delete(); }
    }

    [Test]
    [Arguments(".CODex")]
    [Arguments("postgres")]
    [Arguments("deliverables")]
    [Arguments("runner-state")]
    public async Task ProtectedDescendantRetainsWholeEvidenceDirectory(string component)
    {
        var folder = Directory.CreateTempSubdirectory("retention-tree-");
        try
        {
            var protectedPath = Directory.CreateDirectory(Path.Combine(folder.FullName, "nested", component));
            foreach (var path in new[] { protectedPath.FullName, protectedPath.Parent!.FullName, folder.FullName })
                Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-25));

            const string command = "Get-AntiphonRetentionRefusal -Path $env:RETENTION_FIXTURE " +
                "-Parent ([IO.Path]::GetTempPath().TrimEnd('/')) -LeafPattern '^retention-tree-' -MinAgeHours 24";
            (await RunAsync(folder.FullName, command)).Trim().ShouldBe("unsafe-or-recent-descendant", component);
        }
        finally { folder.Delete(recursive: true); }
    }

    [Test]
    public async Task ReleaseOutsideEvidenceRootsIsRejected()
    {
        var result = await RunSweepAsync("root");
        result.ShouldContain("outside the task's evidence directory");
        result.ShouldContain("listed=True;unlisted=True");
    }

    [Test]
    public async Task ApplyRemovesOnlyListedEvidence()
    {
        var result = await RunSweepAsync("release");
        result.ShouldContain("removed ");
        result.ShouldContain("listed=False;unlisted=True");
    }

    [Test]
    public async Task ReferenceAddedByAnExistingTaskDuringRenameRestoresEvidence()
    {
        var result = await RunSweepAsync("race");
        result.ShouldContain("active-reference");
        result.ShouldContain("listed=True;unlisted=True");
    }

    [Test]
    public async Task AReplacementAtTheReleasedPathIsRetained()
    {
        var result = await RunSweepAsync("identity");
        result.ShouldContain("directory-identity");
        result.ShouldContain("listed=True;unlisted=True");
    }

    private static async Task<string> RunSweepAsync(string scenario)
    {
        var folder = Directory.CreateTempSubdirectory("retention-sweep-");
        try
        {
            return await RunAsync(folder.FullName,
                "& (Join-Path $env:RETENTION_REPOSITORY 'tests/Antiphon.Tests/Fixtures/Server2EvidenceRetention.ps1') " +
                "-Fixture $env:RETENTION_FIXTURE -Repository $env:RETENTION_REPOSITORY -Scenario " + scenario);
        }
        finally { folder.Delete(recursive: true); }
    }

    private static async Task<string> RunAsync(string fixture, string command)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["RETENTION_FIXTURE"] = fixture;
        start.Environment["RETENTION_REPOSITORY"] = CheckpointFixtures.RepoRoot;
        start.Environment["RETENTION_LIBRARY"] =
            Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "lib", "server2-retention-files.ps1");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference = 'Stop'; . $env:RETENTION_LIBRARY; " + command })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await error);
        return await output;
    }
}
