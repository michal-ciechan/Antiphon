using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.MarkdownPdf.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class MarkdownPdfCommandTests
{
    [Test]
    [Arguments("malformed_json")]
    [Arguments("unknown_version")]
    [Arguments("empty_documents")]
    [Arguments("missing_source")]
    [Arguments("traversal")]
    [Arguments("unknown_argument")]
    public async Task Invalid_requests_never_invoke_a_browser_or_claim_a_pdf(string fault)
    {
        var root = Directory.CreateTempSubdirectory("c0418-pdf-invalid-").FullName;
        try
        {
            var manifest = Path.Combine(root, "request.json");
            var output = Path.Combine(root, "result.pdf");
            var browserLog = Path.Combine(root, "browser-called.txt");
            var browser = Path.Combine(root, "fake browser.sh");
            if (!OperatingSystem.IsWindows())
            {
                await File.WriteAllTextAsync(browser,
                    "#!/bin/sh\nprintf invoked > '" + browserLog + "'\nexit 0\n");
                File.SetUnixFileMode(browser, UnixFileMode.UserRead | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute);
            }
            await File.WriteAllTextAsync(Path.Combine(root, "source.md"), "# source\n");
            var json = fault switch
            {
                "malformed_json" => "{broken",
                "unknown_version" => """{"version":2,"title":"Cover","documents":[{"path":"docs/source.md","file":"source.md"}]}""",
                "empty_documents" => """{"version":1,"title":"Cover","documents":[]}""",
                "missing_source" => """{"version":1,"title":"Cover","documents":[{"path":"docs/source.md","file":"missing.md"}]}""",
                "traversal" => """{"version":1,"title":"Cover","documents":[{"path":"docs/source.md","file":"../outside.md"}]}""",
                _ => """{"version":1,"title":"Cover","documents":[{"path":"docs/source.md","file":"source.md"}]}""",
            };
            await File.WriteAllTextAsync(manifest, json);
            var args = new List<string> { "--manifest", manifest, "--output", output,
                "--browser-path", browser };
            if (fault == "unknown_argument") args.AddRange(["--unrecognized", "value"]);
            var (exit, error) = await RunCommandAsync(root, args);
            exit.ShouldBe(2);
            error.ShouldNotBeNullOrWhiteSpace();
            File.Exists(output).ShouldBeFalse();
            File.Exists(browserLog).ShouldBeFalse();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task Symlinked_staged_document_is_refused_before_browser_launch()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("c0418-pdf-link-").FullName;
        var outside = Directory.CreateTempSubdirectory("c0418-pdf-outside-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(outside, "secret.md"), "outside sentinel");
            Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside);
            var manifest = Path.Combine(root, "request.json");
            await File.WriteAllTextAsync(manifest,
                """{"version":1,"title":"Cover","documents":[{"path":"docs/source.md","file":"linked/secret.md"}]}""");
            var output = Path.Combine(root, "result.pdf");
            var (exit, error) = await RunCommandAsync(root,
                ["--manifest", manifest, "--output", output,
                 "--browser-path", Path.Combine(root, "missing-browser")]);
            exit.ShouldBe(2);
            error.ShouldContain("contains a link");
            File.Exists(output).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

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

    private static async Task<(int Exit, string Error)> RunCommandAsync(
        string workingDirectory, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Antiphon.MarkdownPdf.dll"));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        var errorTask = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync();
        return (child.ExitCode, await errorTask);
    }
}
