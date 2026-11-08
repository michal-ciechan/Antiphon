using System.Diagnostics;
using System.Security.Cryptography;
using Shouldly;
using TUnit.Core;

namespace Antiphon.MarkdownPdf.Tests;

/// <summary>
/// CARD-1740. CopyFakeBrowser reads the producer's Release directory. A solution
/// build assigns that configuration only for projects listed in Antiphon.sln, so
/// this witness builds the consumer from the solution and checks the files.
/// </summary>
[Category("Unit")]
[Category("Slow")]
[Category("RealToolchain")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(420_000)]
public sealed class FakeBrowserSolutionReleaseTests
{
    private const string ProducerAssembly = "Antiphon.MarkdownPdf.FakeBrowser.dll";

    [Test]
    public async Task Release_solution_builds_the_fake_browser_producer(CancellationToken cancellationToken)
    {
        var repo = FindRepository();
        var producer = Path.Combine(repo, "tests", "Antiphon.MarkdownPdf.FakeBrowser");
        var consumer = Path.Combine(repo, "tests", "Antiphon.MarkdownPdf.Tests");
        // A leftover Release dll would make a failed solution build look green.
        DeleteDirectory(Path.Combine(producer, "bin", "Debug"));
        DeleteDirectory(Path.Combine(producer, "bin", "Release"));
        DeleteDirectory(Path.Combine(consumer, "bin", "Release"));

        var (exit, output) = await RunLeasedSolutionBuildAsync(repo, cancellationToken);
        var detail = Relevant(output);

        detail.ShouldContain("BUILD SLOT granted");
        detail.ShouldContain("BUILD SLOT released");
        detail.ShouldNotContain("BUILD SLOT unleased");
        detail.ShouldNotContain("BUILD SLOT skipped");
        exit.ShouldBe(0, detail);

        var releaseDll = Path.Combine(producer, "bin", "Release", "net9.0", ProducerAssembly);
        var debugDll = Path.Combine(producer, "bin", "Debug", "net9.0", ProducerAssembly);
        File.Exists(releaseDll).ShouldBeTrue(releaseDll + "\n" + detail);
        File.Exists(debugDll).ShouldBeFalse(debugDll);

        var copied = Path.Combine(consumer, "bin", "Release", "net9.0", "fake-browser", ProducerAssembly);
        File.Exists(copied).ShouldBeTrue(copied + "\n" + detail);
        SHA256.HashData(await File.ReadAllBytesAsync(copied, cancellationToken))
            .ShouldBe(SHA256.HashData(await File.ReadAllBytesAsync(releaseDll, cancellationToken)));
    }

    private static async Task<(int Exit, string Output)> RunLeasedSolutionBuildAsync(
        string repo, CancellationToken cancellationToken)
    {
        // The slot wait sits inside this test's timeout. Three minutes fails the row
        // instead of holding the default forty-five while the runner is busy.
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        foreach (var arg in new[]
        {
            "-NoProfile", "-NonInteractive", "-File", Path.Combine(repo, "scripts", "build-slot.ps1"),
            "-Label", "card-1740-fakebrowser",
            "-SlotWaitMinutes", "3",
            "--",
            "dotnet", "msbuild", "Antiphon.sln",
            "-restore",
            "-p:Configuration=Release",
            "-p:UseAppHost=false",
            "-t:tests\\Antiphon_MarkdownPdf_Tests",
            "-nodeReuse:false",
            "--nologo",
        })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("build slot did not start");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        return (process.ExitCode, await stdout + await stderr);
    }

    private static string Relevant(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('\n', lines.Where(line =>
            line.Contains("BUILD SLOT", StringComparison.Ordinal)
            || line.Contains("error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("FakeBrowser", StringComparison.Ordinal)));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static string FindRepository()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln"))
                && File.Exists(Path.Combine(dir.FullName, "scripts", "build-slot.ps1")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Antiphon.sln not found above " + AppContext.BaseDirectory);
    }
}
