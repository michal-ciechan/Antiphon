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
                new FileInfo($"/proc/{holder.Id}/cwd").LinkTarget.ShouldBe(folder.FullName);
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

            (await RunAsync(folder.FullName, """
                Get-AntiphonRetentionRefusal -Path $env:RETENTION_FIXTURE -Parent ([IO.Path]::GetTempPath().TrimEnd('/')) `
                    -LeafPattern '^retention-tree-' -MinAgeHours 24
                """)).Trim().ShouldBe("unsafe-or-recent-descendant", component);
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
