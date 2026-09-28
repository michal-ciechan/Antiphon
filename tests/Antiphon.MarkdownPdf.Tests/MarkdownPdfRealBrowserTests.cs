using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Antiphon.MarkdownPdf;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.MarkdownPdf.Tests;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class MarkdownPdfRealBrowserTests
{
    private const string Image = "antiphon-card0418-browser:latest";

    [Test]
    public async Task Four_documents_produce_readable_combined_pdf()
    {
        if (Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1")
            throw new SkipTestException("Set ANTIPHON_HEADED_TESTS=1 for the real browser PDF gate.");
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The Docker browser fixture is available on the Linux test host.");

        var repo = FindRepository();
        var specimen = Path.Combine(repo, "tests", "Antiphon.Tests", "Fixtures", "Card0418");
        var root = Path.Combine(repo, ".antiphon", "test-output", "card-0418", "v20-r7",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var names = new[] { "requirements.md", "design.md", "external-api.md", "current-snapshots.md" };
        var before = new Dictionary<string, string>();
        foreach (var name in names)
        {
            var source = Path.Combine(specimen, name);
            before[name] = await HashAsync(source);
            File.Copy(source, Path.Combine(root, name));
        }
        File.Copy(Path.Combine(specimen, "manifest.json"), Path.Combine(root, "manifest.json"));

        var wrapper = Path.Combine(root, "browser.sh");
        var containerIdFile = Path.Combine(root, "browser.cid");
        await File.WriteAllTextAsync(wrapper, "#!/bin/sh\n"
            + "exec docker run --rm --network none --cidfile '" + containerIdFile + "' "
            + "--mount type=bind,src=/tmp,dst=/tmp "
            + "--mount type=bind,src='" + root + "',dst='" + root + "' "
            + "--entrypoint /usr/bin/chromium " + Image
            + " --no-sandbox --disable-dev-shm-usage \"$@\"\n");
        File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var pdf = Path.Combine(root, "combined.pdf");
        try
        {
            var tool = typeof(MarkdownPdfRenderer).Assembly.Location;
            var runtimeConfig = Path.Combine(AppContext.BaseDirectory,
                "Antiphon.MarkdownPdf.Tests.runtimeconfig.json");
            var depsFile = Path.Combine(AppContext.BaseDirectory,
                "Antiphon.MarkdownPdf.Tests.deps.json");
            File.Exists(runtimeConfig).ShouldBeTrue();
            File.Exists(depsFile).ShouldBeTrue();
            var render = await RunAsync("dotnet",
                ["exec", "--runtimeconfig", runtimeConfig, "--depsfile", depsFile, tool,
                    "--manifest", Path.Combine(root, "manifest.json"), "--output", pdf,
                    "--browser-path", wrapper, "--timeout-seconds", "60"], TimeSpan.FromSeconds(90));
            render.ExitCode.ShouldBe(0, render.Stderr);
            File.Exists(pdf).ShouldBeTrue();
            new FileInfo(pdf).Length.ShouldBeGreaterThan(10_000);

            var info = await RunAsync("docker",
                ["run", "--rm", "--network", "none", "--mount",
                    $"type=bind,src={root},dst={root}", "--entrypoint", "pdfinfo", Image, pdf],
                TimeSpan.FromSeconds(20));
            info.ExitCode.ShouldBe(0, info.Stderr);
            Regex.IsMatch(info.Stdout, @"(?m)^Pages:\s+4\s*$").ShouldBeTrue();

            var extraction = await RunAsync("docker",
                ["run", "--rm", "--network", "none", "--mount",
                    $"type=bind,src={root},dst={root}", "--entrypoint", "pdftotext", Image,
                    "-layout", pdf, "-"], TimeSpan.FromSeconds(20));
            extraction.ExitCode.ShouldBe(0, extraction.Stderr);
            await File.WriteAllTextAsync(Path.Combine(root, "extracted.txt"), extraction.Stdout);
            var pages = extraction.Stdout.TrimEnd('\f', '\r', '\n').Split('\f');
            pages.Length.ShouldBe(4);
            var headings = new[] { "Requirements sentinel", "Design sentinel",
                "External API sentinel", "Current snapshots sentinel" };
            var endings = new[] { "requirements final sentinel", "design final sentinel",
                "external API final sentinel", "current snapshots final sentinel" };
            for (var i = 0; i < pages.Length; i++)
            {
                pages[i].ShouldContain(headings[i]);
                pages[i].ShouldContain(endings[i]);
                foreach (var other in headings.Where((_, n) => n != i))
                    pages[i].ShouldNotContain(other);
            }
            pages[0].ShouldContain("zażółć gęślą jaźń");
            pages[3].ShouldContain("Unicode ✨");

            var images = await RunAsync("docker",
                ["run", "--rm", "--network", "none", "--mount",
                    $"type=bind,src={root},dst={root}", "--entrypoint", "pdftoppm", Image,
                    "-png", "-r", "110", pdf, Path.Combine(root, "page")],
                TimeSpan.FromSeconds(30));
            images.ExitCode.ShouldBe(0, images.Stderr);
            Directory.GetFiles(root, "page-*.png").Length.ShouldBe(4);
            foreach (var name in names)
                (await HashAsync(Path.Combine(specimen, name))).ShouldBe(before[name]);
            Console.WriteLine($"CARD-0418 V-20 artifact: {pdf} sha256={await HashAsync(pdf)}");
        }
        finally
        {
            if (File.Exists(containerIdFile))
            {
                var id = (await File.ReadAllTextAsync(containerIdFile)).Trim();
                if (id.Length == 64 && id.All(Uri.IsHexDigit))
                    await RunAsync("docker", ["rm", "-f", id], TimeSpan.FromSeconds(10));
            }
        }
    }

    private static string FindRepository()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "tests", "Antiphon.Tests", "Fixtures",
                    "Card0418", "manifest.json")))
                return dir.FullName;
        throw new DirectoryNotFoundException("CARD-0418 specimen source tree is unavailable.");
    }

    private static async Task<string> HashAsync(string path) =>
        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string program, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {program}.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellation.Token);
        var stderr = process.StandardError.ReadToEndAsync(cancellation.Token);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
            return (process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            try { await Task.WhenAll(stdout, stderr); }
            catch (OperationCanceledException) { }
            throw new System.TimeoutException($"{program} exceeded {timeout}.");
        }
    }
}
