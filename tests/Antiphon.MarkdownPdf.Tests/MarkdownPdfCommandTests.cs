using Shouldly;
using TUnit.Core;

namespace Antiphon.MarkdownPdf.Tests;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public class MarkdownPdfCommandTests
{
    [Test]
    public async Task Standalone_manifest_and_failures_are_bounded()
    {
        var dir = Directory.CreateTempSubdirectory("antiphon-mdpdf-cmd").FullName;
        try
        {
            var missing = await MarkdownPdfCommand.RunAsync(
                ["--manifest", Path.Combine(dir, "no.json"), "--output", Path.Combine(dir, "out.pdf")],
                CancellationToken.None);
            missing.ShouldBe(2);

            await File.WriteAllTextAsync(Path.Combine(dir, "bad.json"), "{");
            var malformed = await MarkdownPdfCommand.RunAsync(
                ["--manifest", Path.Combine(dir, "bad.json"), "--output", Path.Combine(dir, "out.pdf")],
                CancellationToken.None);
            malformed.ShouldBe(2);

            var source = Path.Combine(dir, "doc.md");
            await File.WriteAllTextAsync(source, "# Hello");
            var manifest = Path.Combine(dir, "manifest.json");
            await File.WriteAllTextAsync(manifest, """
                {"version":1,"cover":"cover","documents":[{"path":"MISSING.md"}]}
                """);
            var missingSource = await MarkdownPdfCommand.RunAsync(
                ["--manifest", manifest, "--output", Path.Combine(dir, "out.pdf")],
                CancellationToken.None);
            missingSource.ShouldBe(2);

            await File.WriteAllTextAsync(manifest, $$"""
                {"version":1,"cover":"cover","documents":[{"path":{{ToJson(source)}} }]}
                """);
            var missingBrowser = await MarkdownPdfCommand.RunAsync(
                [
                    "--manifest", manifest,
                    "--output", Path.Combine(dir, "out.pdf"),
                    "--browser-path", Path.Combine(dir, "no-browser", "msedge.exe"),
                ],
                CancellationToken.None);
            missingBrowser.ShouldBe(2);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static string ToJson(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value);
}
