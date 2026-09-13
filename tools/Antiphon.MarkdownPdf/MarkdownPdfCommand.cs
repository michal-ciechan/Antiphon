using System.Text.Json;

namespace Antiphon.MarkdownPdf;

public sealed class MarkdownPdfManifest
{
    public int Version { get; set; } = 1;
    public string? Cover { get; set; }
    public List<MarkdownPdfDocument> Documents { get; set; } = [];
}

public sealed class MarkdownPdfDocument
{
    public string Path { get; set; } = "";
}

public static class MarkdownPdfCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        string? manifestPath = null;
        string? outputPath = null;
        string? browserPath = null;
        int timeoutSeconds = 20;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--manifest" when i + 1 < args.Length:
                    manifestPath = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    outputPath = args[++i];
                    break;
                case "--browser-path" when i + 1 < args.Length:
                    browserPath = args[++i];
                    break;
                case "--timeout-seconds" when i + 1 < args.Length:
                    timeoutSeconds = int.Parse(args[++i]);
                    break;
                default:
                    Console.Error.WriteLine($"unknown argument {args[i]}");
                    return 2;
            }
        }

        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("usage: Antiphon.MarkdownPdf --manifest <path> --output <path> [--browser-path <path>] [--timeout-seconds <n>]");
            return 2;
        }

        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine("manifest not found");
            return 2;
        }

        MarkdownPdfManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<MarkdownPdfManifest>(
                await File.ReadAllTextAsync(manifestPath, ct),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("malformed manifest: " + ex.Message);
            return 2;
        }

        if (manifest is null || manifest.Version != 1)
        {
            Console.Error.WriteLine("unsupported or missing manifest version");
            return 2;
        }

        if (manifest.Documents.Count == 0)
        {
            Console.Error.WriteLine("manifest has no documents");
            return 2;
        }

        var sections = new List<MarkdownPdfRenderer.DocumentSection>();
        foreach (var document in manifest.Documents)
        {
            if (string.IsNullOrWhiteSpace(document.Path) || !File.Exists(document.Path))
            {
                Console.Error.WriteLine("missing source " + document.Path);
                return 2;
            }

            var markdown = await File.ReadAllTextAsync(document.Path, ct);
            var relative = Path.GetFileName(document.Path);
            sections.Add(new MarkdownPdfRenderer.DocumentSection(relative, markdown));
        }

        var renderer = new MarkdownPdfRenderer(new MarkdownPdfSettings
        {
            BrowserPath = browserPath,
            RenderTimeoutSeconds = timeoutSeconds,
        });
        if (!string.IsNullOrWhiteSpace(browserPath) && renderer.ResolveBrowserPath() is null)
        {
            Console.Error.WriteLine("browser not found");
            return 2;
        }

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        var html = renderer.ToHtml(manifest.Cover ?? "Deliverable", sections);
        var result = await renderer.RenderToPdfAsync(html, outputPath, ct);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(result.Error ?? "render failed");
            return 1;
        }

        return 0;
    }
}
