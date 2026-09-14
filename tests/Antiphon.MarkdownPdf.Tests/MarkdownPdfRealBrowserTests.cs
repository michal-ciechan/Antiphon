using System.Security.Cryptography;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using UglyToad.PdfPig;

namespace Antiphon.MarkdownPdf.Tests;

/// <summary>
/// CARD-0418 V-20: a real browser renders the four-document specimen into a PDF that a person could
/// actually read.
///
/// <para>"%PDF", a nonzero length and a .pdf extension are not oracles — every one of them is true
/// of a PDF with four blank pages. So the file is reopened with an INDEPENDENT parser, every page is
/// extracted separately, and the assertions are about where each document's text landed: its
/// heading, its middle and its end sentinel, its table, its fenced code and its Unicode, with each
/// document starting a new page and no page left blank.</para>
///
/// <para>Artifacts (the PDF and the per-page extracted text) are retained under
/// <c>ANTIPHON_MDPDF_EVIDENCE</c> when that is set, so a reviewer can look at the thing itself
/// rather than at this test's opinion of it.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public class MarkdownPdfRealBrowserTests
{
    private static readonly (string Relative, string Heading, string Middle, string End, string Body)[] Specimen =
    [
        ("docs/features/001-kalshi-ref-data-downloader/01-requirements.md",
            "Requirements sentinel", "middle-req", "end-req",
            """
            | Requirement | State |
            | --- | --- |
            | Zażółć gęślą jaźń 🙂 | accepted |

            middle-req

            ```csharp
            var requirement = "fenced-req";
            ```

            end-req
            """),
        ("docs/features/001-kalshi-ref-data-downloader/03-design.md",
            "Design sentinel", "middle-design", "end-design",
            """
            middle-design

            - [x] a task list item
            - [ ] another one

            ```json
            { "design": "fenced-design" }
            ```

            end-design
            """),
        ("docs/features/001-kalshi-ref-data-downloader/04-external-api.md",
            "External API sentinel", "middle-api", "end-api",
            """
            middle-api

            | Endpoint | Verb |
            | --- | --- |
            | /api/markets | GET |

            end-api
            """),
        ("docs/features/001c-kalshi-current-first-snapshots/04-external-api.md",
            "Current snapshots sentinel", "middle-snap", "end-snap",
            """
            middle-snap

            ```text
            fenced-snap
            ```

            end-snap
            """),
    ];

    [Test]
    [Timeout(300_000)]
    public async Task Four_documents_produce_readable_combined_pdf(CancellationToken ct)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS"), "1", StringComparison.Ordinal))
            throw new SkipTestException("ANTIPHON_HEADED_TESTS=1 required");

        var renderer = new MarkdownPdfRenderer(new MarkdownPdfSettings());
        if (renderer.ResolveBrowserPath() is null)
            throw new SkipTestException("no Edge/Chrome found for PDF rendering");

        var dir = Directory.CreateTempSubdirectory("antiphon-mdpdf-real").FullName;
        try
        {
            var sections = new List<MarkdownPdfRenderer.DocumentSection>();
            var sourceHashes = new Dictionary<string, string>();
            foreach (var (relative, heading, _, _, body) in Specimen)
            {
                var text = "# " + heading + "\n\n" + body + "\n";
                var path = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, text, ct);
                sourceHashes[relative] = Convert.ToHexStringLower(
                    SHA256.HashData(await File.ReadAllBytesAsync(path, ct)));
                sections.Add(new MarkdownPdfRenderer.DocumentSection(relative, text));
            }

            var pdf = Path.Combine(dir, "combined.pdf");
            var html = renderer.ToHtml("CARD-0418 specimen", sections);
            var result = await renderer.RenderToPdfAsync(html, pdf, ct);
            result.Succeeded.ShouldBeTrue(result.Error ?? result.Log);
            File.Exists(pdf).ShouldBeTrue();

            using var document = PdfDocument.Open(pdf);
            var pages = document.GetPages().Select(p => p.Text).ToList();
            pages.Count.ShouldBeGreaterThanOrEqualTo(4, "four documents, each starting its own page");

            await RetainAsync(pdf, pages, ct);

            // Every page carries something. A blank page means a section was lost or clipped.
            for (var i = 0; i < pages.Count; i++)
                pages[i].Trim().ShouldNotBeEmpty($"page {i + 1} rendered blank");

            // Each document opens a page of its own, in the order it was given.
            var headingPages = new List<int>();
            foreach (var (_, heading, middle, end, _) in Specimen)
            {
                var page = pages.FindIndex(p => p.Contains(heading, StringComparison.Ordinal));
                page.ShouldBeGreaterThanOrEqualTo(0, $"'{heading}' is missing from the PDF entirely");
                headingPages.Add(page);

                // Middle and end are present, so the section was not truncated after its title.
                pages.Any(p => p.Contains(middle, StringComparison.Ordinal))
                    .ShouldBeTrue($"'{middle}' is missing: {heading} was cut short");
                pages.Any(p => p.Contains(end, StringComparison.Ordinal))
                    .ShouldBeTrue($"'{end}' is missing: {heading} was clipped before its end");
            }

            headingPages.ShouldBe(headingPages.OrderBy(p => p).ToList(), "documents are out of order");
            headingPages.Distinct().Count().ShouldBe(Specimen.Length, "two documents share a page");

            var all = string.Join("\n", pages);

            // Unicode survives the HTML and the PDF font.
            all.ShouldContain("Zażółć gęślą jaźń");

            // Tables keep their cells, fenced code keeps its contents, task lists keep their items.
            all.ShouldContain("Endpoint");
            all.ShouldContain("/api/markets");
            all.ShouldContain("fenced-req");
            all.ShouldContain("fenced-design");
            all.ShouldContain("fenced-snap");
            all.ShouldContain("a task list item");

            // Both 04-external-api.md documents are present and distinguishable: the colliding
            // basename is the case the historic incident actually produced.
            all.ShouldContain("External API sentinel");
            all.ShouldContain("Current snapshots sentinel");

            // Rendering reads the sources; it does not touch them.
            foreach (var (relative, _, _, _, _) in Specimen)
            {
                var path = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
                Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, ct)))
                    .ShouldBe(sourceHashes[relative], relative + " changed during rendering");
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Copies the rendered PDF and the per-page extracted text to the evidence root, when one is
    /// configured. Page IMAGES are not produced here: no rasterizer ships with this assembly, so
    /// visual inspection is a human step on the retained PDF rather than something this test can
    /// claim to have done.
    /// </summary>
    private static async Task RetainAsync(string pdf, IReadOnlyList<string> pages, CancellationToken ct)
    {
        var root = Environment.GetEnvironmentVariable("ANTIPHON_MDPDF_EVIDENCE");
        if (string.IsNullOrWhiteSpace(root))
            return;
        var dir = Path.Combine(root, "v20-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(dir);
        File.Copy(pdf, Path.Combine(dir, "combined.pdf"), overwrite: true);
        for (var i = 0; i < pages.Count; i++)
            await File.WriteAllTextAsync(Path.Combine(dir, $"page-{i + 1:00}.txt"), pages[i], ct);
    }
}
