using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

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
    public async Task Edge_or_Chrome_prints_a_one_page_pdf()
    {
        var renderer = CreateRenderer();
        if (renderer.ResolveBrowserPath() is null)
            throw new SkipTestException("no Edge/Chrome found for PDF rendering");

        var dir = Directory.CreateTempSubdirectory("antiphon-pdf-render").FullName;
        try
        {
            var pdf = Path.Combine(dir, "out.pdf");
            var html = renderer.ToHtml(
                "cover",
                [new MarkdownPdfRenderer.DocumentSection("docs/a.md", "# Hello\n\nA paragraph.")]);
            var result = await renderer.RenderToPdfAsync(html, pdf, CancellationToken.None);
            result.Succeeded.ShouldBeTrue(result.Error ?? result.Log);
            File.Exists(pdf).ShouldBeTrue();
            new FileInfo(pdf).Length.ShouldBeGreaterThan(100);
            var header = new byte[5];
            await using (var stream = File.OpenRead(pdf))
                _ = await stream.ReadAsync(header);
            System.Text.Encoding.ASCII.GetString(header).ShouldBe("%PDF-");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static MarkdownPdfRenderer CreateRenderer(string? browserPath = null, int timeoutSeconds = 20) =>
        new(
            Options.Create(new DeliverablesSettings
            {
                BrowserPath = browserPath,
                RenderTimeoutSeconds = timeoutSeconds,
            }),
            NullLogger<MarkdownPdfRenderer>.Instance);
}
