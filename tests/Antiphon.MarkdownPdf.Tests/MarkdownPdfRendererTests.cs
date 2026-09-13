using Shouldly;
using TUnit.Core;

namespace Antiphon.MarkdownPdf.Tests;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public class MarkdownPdfRendererTests
{
    [Test]
    public void Html_and_arguments_preserve_document_contract()
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

        var renderer = new MarkdownPdfRenderer(new MarkdownPdfSettings());
        var wrapped = renderer.ToHtml(
            "CARD-0002 Title · ab12cd34 · 2026-09-03",
            [
                new MarkdownPdfRenderer.DocumentSection("docs/features/one/a.md", "# Hello"),
                new MarkdownPdfRenderer.DocumentSection("docs/features/one/b.md", "body"),
            ]);
        wrapped.ShouldContain("@page { size: A4; margin: 18mm; }");
        wrapped.ShouldContain("CARD-0002 Title");
        wrapped.ShouldContain("<h1>docs/features/one/a.md</h1>");
        wrapped.ShouldContain("section + section { page-break-before: always; }");
        wrapped.ShouldContain(System.Net.WebUtility.HtmlEncode("docs/features/one/a.md"));
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
        var renderer = new MarkdownPdfRenderer(new MarkdownPdfSettings
        {
            BrowserPath = Path.Combine(Path.GetTempPath(), "no-such-edge", "msedge.exe"),
        });
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
        var renderer = new MarkdownPdfRenderer(new MarkdownPdfSettings
        {
            BrowserPath = fakeBrowser,
            RenderTimeoutSeconds = 1,
        })
        {
            TestHang = hangCt => Task.Delay(Timeout.Infinite, hangCt),
        };
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
}
