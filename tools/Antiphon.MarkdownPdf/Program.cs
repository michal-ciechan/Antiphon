using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.MarkdownPdf;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var values = ParseArguments(args);
            if (!values.TryGetValue("--manifest", out var manifestPath)
                || !values.TryGetValue("--output", out var outputPath))
                throw new ArgumentException("--manifest and --output are required.");
            var timeoutSeconds = 20;
            if (values.TryGetValue("--timeout-seconds", out var timeoutText)
                && (!int.TryParse(timeoutText, out timeoutSeconds) || timeoutSeconds is < 1 or > 300))
                throw new ArgumentException("--timeout-seconds must be between 1 and 300.");

            var manifestRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
            var json = await File.ReadAllTextAsync(manifestPath);
            var request = JsonSerializer.Deserialize<MarkdownPdfRequest>(json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (request is null || request.Version != 1 || string.IsNullOrWhiteSpace(request.Title)
                || request.Documents is not { Count: > 0 and <= 1000 })
                throw new ArgumentException("Invalid PDF manifest version, title or document list.");

            var documents = new List<MarkdownPdfRenderer.DocumentSection>(request.Documents.Count);
            long total = 0;
            foreach (var document in request.Documents)
            {
                if (document is null || string.IsNullOrWhiteSpace(document.Path)
                    || string.IsNullOrWhiteSpace(document.File)
                    || Path.IsPathRooted(document.File) || document.File.Contains("..", StringComparison.Ordinal)
                    || document.File.Contains(':') || document.File.StartsWith('\\'))
                    throw new ArgumentException("Invalid staged document path.");
                var input = Path.GetFullPath(Path.Combine(manifestRoot,
                    document.File.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
                var prefix = manifestRoot.EndsWith(Path.DirectorySeparatorChar)
                    ? manifestRoot : manifestRoot + Path.DirectorySeparatorChar;
                if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Staged document escaped the manifest directory.");
                var length = new FileInfo(input).Length;
                if (length > 64L * 1024 * 1024 - total)
                    throw new ArgumentException("Staged documents exceed 64 MiB.");
                total += length;
                documents.Add(new MarkdownPdfRenderer.DocumentSection(document.Path,
                    await File.ReadAllTextAsync(input)));
            }

            var renderer = new MarkdownPdfRenderer(Options.Create(new MarkdownPdfToolSettings
            {
                BrowserPath = values.GetValueOrDefault("--browser-path"),
                RenderTimeoutSeconds = timeoutSeconds,
            }), NullLogger<MarkdownPdfRenderer>.Instance);
            var html = renderer.ToHtml(request.Title, documents);
            var result = await renderer.RenderToPdfAsync(html, Path.GetFullPath(outputPath), CancellationToken.None);
            if (result.Succeeded)
                return 0;
            Console.Error.WriteLine(result.Error);
            return 1;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length == 0 || args.Length % 2 != 0)
            throw new ArgumentException("Expected --manifest <path> --output <path> [--browser-path <path>] [--timeout-seconds <n>].");
        for (var i = 0; i < args.Length; i += 2)
        {
            if (args[i] is not ("--manifest" or "--output" or "--browser-path" or "--timeout-seconds")
                || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unknown or duplicate PDF command argument.");
        }
        return values;
    }
}

public sealed record MarkdownPdfRequest(int Version, string Title, IReadOnlyList<MarkdownPdfInputDocument> Documents);
public sealed record MarkdownPdfInputDocument(string Path, string File);
