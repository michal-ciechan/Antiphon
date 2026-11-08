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

        var (exit, output) = await RunSolutionBuildAsync(repo, cancellationToken);
        var detail = Relevant(output);

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

    private static async Task<(int Exit, string Output)> RunSolutionBuildAsync(
        string repo, CancellationToken cancellationToken)
    {
        // The outer run already holds the host build slot. A nested build-slot.ps1
        // lease would take a second slot, and a granted-lease assertion fails when
        // slots are off or the runner is unreachable (CARD-1740).
        var start = new ProcessStartInfo("dotnet")
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
            "msbuild", "Antiphon.sln",
            "-maxcpucount:2",
            "-nodeReuse:false",
            "-restore",
            "-p:Configuration=Release",
            "-p:UseAppHost=false",
            "-t:tests\\Antiphon_MarkdownPdf_Tests",
            "--nologo",
        })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("solution build did not start");
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
            line.Contains("error", StringComparison.OrdinalIgnoreCase)
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
