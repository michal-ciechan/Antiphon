using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using UglyToad.PdfPig;

namespace Antiphon.MarkdownPdf.Tests;

[Category("Integration")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public class MarkdownPdfRealBrowserTests
{
    [Test]
    public async Task Four_documents_produce_readable_combined_pdf()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS"), "1", StringComparison.Ordinal))
            throw new SkipTestException("ANTIPHON_HEADED_TESTS=1 required");

        var renderer = new MarkdownPdfRenderer(new MarkdownPdfSettings());
        if (renderer.ResolveBrowserPath() is null)
            throw new SkipTestException("no Edge/Chrome found for PDF rendering");

        var dir = Directory.CreateTempSubdirectory("antiphon-mdpdf-real").FullName;
        try
        {
            var docs = new[]
            {
                ("docs/features/001-kalshi-ref-data-downloader/01-requirements.md", "# Requirements sentinel\n\nmiddle-req\n\nend-req"),
                ("docs/features/001-kalshi-ref-data-downloader/03-design.md", "# Design sentinel\n\nmiddle-design\n\nend-design"),
                ("docs/features/001-kalshi-ref-data-downloader/04-external-api.md", "# External API sentinel\n\nmiddle-api\n\nend-api"),
                ("docs/features/001c-kalshi-current-first-snapshots/04-external-api.md", "# Current snapshots sentinel\n\nmiddle-snap\n\nend-snap"),
            };
            var sections = new List<MarkdownPdfRenderer.DocumentSection>();
            foreach (var (relative, body) in docs)
            {
                var path = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, body);
                sections.Add(new MarkdownPdfRenderer.DocumentSection(relative, body));
            }

            var pdf = Path.Combine(dir, "combined.pdf");
            var html = renderer.ToHtml("CARD-0418 specimen", sections);
            var result = await renderer.RenderToPdfAsync(html, pdf, CancellationToken.None);
            result.Succeeded.ShouldBeTrue(result.Error ?? result.Log);
            File.Exists(pdf).ShouldBeTrue();

            using var document = PdfDocument.Open(pdf);
            document.NumberOfPages.ShouldBeGreaterThanOrEqualTo(4);
            var text = string.Join("\n", document.GetPages().Select(p => p.Text));
            text.ShouldContain("Requirements sentinel");
            text.ShouldContain("Design sentinel");
            text.ShouldContain("External API sentinel");
            text.ShouldContain("Current snapshots sentinel");
            text.ShouldContain("end-req");
            text.ShouldContain("end-design");
            text.ShouldContain("end-api");
            text.ShouldContain("end-snap");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
