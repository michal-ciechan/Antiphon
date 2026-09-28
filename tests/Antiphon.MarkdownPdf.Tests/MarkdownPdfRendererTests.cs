using Antiphon.MarkdownPdf;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.MarkdownPdf.Tests;

/// <summary>CARD-0337 S1: Markdig HTML and the Edge/Chrome print-to-pdf invocation.</summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public class MarkdownPdfRendererTests
{
    [Test]
    public void Markdig_renders_a_gfm_table_fenced_code_and_task_list()
    {
        const string markdown = """
            | Col | Val |
            | --- | --- |
            | a   | 1   |

            ```csharp
            Console.WriteLine("hi");
            ```

            - [x] done
            - [ ] todo
            """;

        var html = MarkdownPdfRenderer.ToMarkdownHtml(markdown);

        html.ShouldContain("<table");
        html.ShouldContain("<th");
        html.ShouldContain("Col");
        html.ShouldContain("<pre");
        html.ShouldContain("Console.WriteLine");
        html.ShouldContain("type=\"checkbox\"");
    }

    [Test]
    public void ToHtml_wraps_each_document_as_a_section_with_its_path_as_h1()
    {
        var renderer = CreateRenderer();
        var html = renderer.ToHtml(
            "CARD-0002 Title · ab12cd34 · 2026-09-03",
            [
                new MarkdownPdfRenderer.DocumentSection("docs/features/one/a.md", "# Hello"),
                new MarkdownPdfRenderer.DocumentSection("docs/features/one/b.md", "body"),
            ]);

        html.ShouldContain("@page { size: A4; margin: 18mm; }");
        html.ShouldContain("pre { white-space: pre-wrap;");
        html.ShouldContain("CARD-0002 Title");
        html.ShouldContain("<h1>docs/features/one/a.md</h1>");
        html.ShouldContain("<h1>docs/features/one/b.md</h1>");
        html.ShouldContain("section + section { page-break-before: always; }");
    }

    [Test]
    public void Cover_and_document_paths_are_html_escaped_without_losing_unicode()
    {
        var html = CreateRenderer().ToHtml("<script>✨ & cover</script>",
            [new MarkdownPdfRenderer.DocumentSection("docs/<outside>&zażółć.md", "# Body ✨")]);
        html.ShouldContain("&lt;script&gt;✨ &amp; cover&lt;/script&gt;");
        html.ShouldContain("docs/&lt;outside&gt;&amp;zaż&#243;łć.md");
        html.ShouldNotContain("<script>✨");
        html.ShouldContain("Body ✨");
    }

    [Test]
    public void BuildArguments_match_the_headless_print_to_pdf_contract()
    {
        var pdf = Path.Combine("C:", "tmp", "out.pdf");
        var html = Path.Combine("C:", "tmp", "in.html");
        var args = MarkdownPdfRenderer.BuildArguments(pdf, html);

        args.ShouldBe([
            MarkdownPdfRenderer.HeadlessArg,
            MarkdownPdfRenderer.DisableGpuArg,
            MarkdownPdfRenderer.NoHeaderFooterArg,
            MarkdownPdfRenderer.PrintToPdfPrefix + pdf,
            MarkdownPdfRenderer.ToFileUrl(html),
        ]);
        args[4].ShouldStartWith("file:///");
    }

    [Test]
    public async Task A_missing_browser_returns_failure_and_does_not_throw()
    {
        var renderer = CreateRenderer(browserPath: Path.Combine(Path.GetTempPath(), "no-such-edge", "msedge.exe"));
        var pdf = Path.Combine(Path.GetTempPath(), $"antiphon-pdf-{Guid.NewGuid():N}.pdf");
        try
        {
            var result = await renderer.RenderToPdfAsync("<html></html>", pdf, CancellationToken.None);
            result.Succeeded.ShouldBeFalse();
            result.Error.ShouldContain("browser not found");
        }
        finally
        {
            try { File.Delete(pdf); } catch (IOException) { }
        }
    }

    [Test]
    public async Task A_timeout_returns_failure_and_does_not_throw()
    {
        var fakeBrowser = Path.Combine(Path.GetTempPath(), $"fake-edge-{Guid.NewGuid():N}.exe");
        await File.WriteAllBytesAsync(fakeBrowser, [0]);
        var renderer = CreateRenderer(browserPath: fakeBrowser, timeoutSeconds: 1);
        renderer.TestHang = hangCt => Task.Delay(Timeout.Infinite, hangCt);
        var pdf = Path.Combine(Path.GetTempPath(), $"antiphon-pdf-{Guid.NewGuid():N}.pdf");
        try
        {
            var result = await renderer.RenderToPdfAsync("<html><body>x</body></html>", pdf, CancellationToken.None);
            result.Succeeded.ShouldBeFalse();
            result.Error.ShouldContain("timed out");
        }
        finally
        {
            try { File.Delete(pdf); } catch (IOException) { }
            try { File.Delete(fakeBrowser); } catch (IOException) { }
        }
    }

    [Test]
    public async Task A_real_browser_timeout_stops_its_child_process()
    {
        if (OperatingSystem.IsWindows()) return; // The Unix script is a real process-tree fixture.
        await AssertBrowserTreeStoppedAsync(cancelCaller: false);
    }

    [Test]
    public async Task Caller_cancellation_stops_the_real_browser_process_tree()
    {
        if (OperatingSystem.IsWindows()) return;
        await AssertBrowserTreeStoppedAsync(cancelCaller: true);
    }

    [Test]
    public async Task Real_browser_exit_zero_requires_a_fresh_nonempty_pdf()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("c0418-pdf-no-output").FullName;
        var browser = Path.Combine(root, "fake browser.sh");
        var arguments = Path.Combine(root, "arguments.txt");
        var pdf = Path.Combine(root, "stale zażółć ✨.pdf");
        try
        {
            await File.WriteAllTextAsync(browser,
                "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"" + arguments + "\"\nexit 0\n");
            File.SetUnixFileMode(browser, UnixFileMode.UserRead | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
            await File.WriteAllTextAsync(pdf, "stale output");
            var renderer = CreateRenderer(browser);
            var result = await renderer.RenderToPdfAsync("<html>x</html>", pdf, CancellationToken.None);
            result.Succeeded.ShouldBeFalse();
            result.Error!.ShouldContain("wrote no PDF");
            File.Exists(pdf).ShouldBeFalse();
            var seen = await File.ReadAllTextAsync(arguments);
            seen.ShouldContain(MarkdownPdfRenderer.HeadlessArg);
            seen.ShouldContain(MarkdownPdfRenderer.PrintToPdfPrefix + pdf);
            seen.ShouldContain("file://");

            await File.WriteAllTextAsync(browser,
                "#!/bin/sh\nfor arg do case \"$arg\" in --print-to-pdf=*) : > \"${arg#*=}\";; esac; done\nexit 0\n");
            result = await renderer.RenderToPdfAsync("<html>x</html>", pdf, CancellationToken.None);
            result.Succeeded.ShouldBeFalse();
            result.Error!.ShouldContain("wrote no PDF");
            new FileInfo(pdf).Length.ShouldBe(0);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task AssertBrowserTreeStoppedAsync(bool cancelCaller)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var root = Directory.CreateTempSubdirectory("c0418-pdf-process").FullName;
        var browser = Path.Combine(root, "fake browser.sh");
        var parentFile = Path.Combine(root, "parent.pid");
        var childFile = Path.Combine(root, "child.pid");
        var pdf = Path.Combine(root, "result.pdf");
        int? parentPid = null, childPid = null;
        DateTime? parentStarted = null, childStarted = null;
        try
        {
            await File.WriteAllTextAsync(browser, "#!/bin/sh\n"
                + "printf '%s\\n' \"$$\" > \"" + parentFile + "\"\n"
                + "sleep 30 &\n"
                + "printf '%s\\n' \"$!\" > \"" + childFile + "\"\n"
                + "wait\n");
            File.SetUnixFileMode(browser, UnixFileMode.UserRead | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
            var renderer = CreateRenderer(browser, timeoutSeconds: cancelCaller ? 20 : 1);
            using var caller = new CancellationTokenSource();
            var render = renderer.RenderToPdfAsync("<html><body>x</body></html>", pdf, caller.Token);
            await WaitForFileAsync(parentFile);
            await WaitForFileAsync(childFile);
            parentPid = int.Parse(await File.ReadAllTextAsync(parentFile));
            childPid = int.Parse(await File.ReadAllTextAsync(childFile));
            parentStarted = Process.GetProcessById(parentPid.Value).StartTime;
            childStarted = Process.GetProcessById(childPid.Value).StartTime;
            if (cancelCaller)
            {
                caller.Cancel();
                await Should.ThrowAsync<OperationCanceledException>(async () => await render);
            }
            else
            {
                var result = await render;
                result.Succeeded.ShouldBeFalse();
                result.Error!.ShouldContain("timed out");
            }
            await WaitForExitAsync(parentPid.Value);
            await WaitForExitAsync(childPid.Value);
            File.Exists(pdf).ShouldBeFalse();
        }
        finally
        {
            // Only the two PIDs written by this fixture are eligible for cleanup.
            if (childPid is null && File.Exists(childFile))
                childPid = int.Parse(await File.ReadAllTextAsync(childFile));
            if (parentPid is null && File.Exists(parentFile))
                parentPid = int.Parse(await File.ReadAllTextAsync(parentFile));
            if (childPid is int child) KillFixtureProcess(child, childStarted);
            if (parentPid is int parent) KillFixtureProcess(parent, parentStarted);
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(path))
        {
            deadline.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, deadline.Token);
        }
    }

    private static async Task WaitForExitAsync(int pid)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (IsRunning(pid))
        {
            deadline.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, deadline.Token);
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return false;
            // Reparented child zombies are no longer executing, but may await init's reap.
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return !stat.Split(' ')[2].Equals("Z", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return false; }
    }

    private static void KillFixtureProcess(int pid, DateTime? started)
    {
        if (!IsRunning(pid)) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (started is not null && process.StartTime != started) return;
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
    }

    private static MarkdownPdfRenderer CreateRenderer(string? browserPath = null, int timeoutSeconds = 20) =>
        new(
            Options.Create(new MarkdownPdfToolSettings
            {
                BrowserPath = browserPath,
                RenderTimeoutSeconds = timeoutSeconds,
            }),
            NullLogger<MarkdownPdfRenderer>.Instance);
}
