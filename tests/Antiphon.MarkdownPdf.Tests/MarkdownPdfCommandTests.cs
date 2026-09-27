using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.MarkdownPdf.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class MarkdownPdfCommandTests
{
    [Test]
    public async Task Invalid_manifest_is_rejected_without_creating_output()
    {
        var root = Directory.CreateTempSubdirectory("c0418-pdf-command").FullName;
        try
        {
            var output = Path.Combine(root, "result.pdf");
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = root,
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Antiphon.MarkdownPdf.dll"));
            start.ArgumentList.Add("--manifest");
            start.ArgumentList.Add(Path.Combine(root, "missing.json"));
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(output);
            using var child = Process.Start(start)!;
            await child.WaitForExitAsync();
            child.ExitCode.ShouldNotBe(0);
            File.Exists(output).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Valid_manifest_with_missing_browser_reports_failure_without_pdf()
    {
        var root = Directory.CreateTempSubdirectory("c0418-pdf-missing-browser").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "source.md"), "# Source\n");
            var manifest = Path.Combine(root, "request.json");
            await File.WriteAllTextAsync(manifest,
                """{"version":1,"title":"Cover","documents":[{"path":"docs/source.md","file":"source.md"}]}""");
            var output = Path.Combine(root, "result.pdf");
            var missingBrowser = Path.Combine(root, "missing-browser.exe");
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = root,
            };
            foreach (var arg in new[] { Path.Combine(AppContext.BaseDirectory, "Antiphon.MarkdownPdf.dll"),
                         "--manifest", manifest, "--output", output, "--browser-path", missingBrowser })
                start.ArgumentList.Add(arg);
            using var child = Process.Start(start)!;
            var error = await child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync();
            child.ExitCode.ShouldBe(1);
            error.ShouldContain("browser not found");
            File.Exists(output).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
