using System.Diagnostics;
using System.Net;
using System.Text;
using Antiphon.Server.Application.Settings;
using Markdig;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0337: Markdig → self-contained HTML → headless Edge/Chrome <c>--print-to-pdf</c>.
/// Never throws for a missing browser, timeout, or non-zero exit; the caller records the error.
/// </summary>
public sealed class MarkdownPdfRenderer
{
    public const string HeadlessArg = "--headless=new";
    public const string DisableGpuArg = "--disable-gpu";
    public const string NoHeaderFooterArg = "--no-pdf-header-footer";
    public const string PrintToPdfPrefix = "--print-to-pdf=";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly string[] EdgeCandidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft", "Edge", "Application", "msedge.exe"),
    ];

    private static readonly string[] ChromeCandidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Google", "Chrome", "Application", "chrome.exe"),
    ];

    private readonly DeliverablesSettings _settings;
    private readonly ILogger<MarkdownPdfRenderer> _logger;

    /// <summary>Test seam: a hang that is cancelled by the render timeout. Production is null.</summary>
    internal Func<CancellationToken, Task>? TestHang { get; set; }

    public MarkdownPdfRenderer(
        IOptions<DeliverablesSettings> settings,
        ILogger<MarkdownPdfRenderer> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public readonly record struct DocumentSection(string RepoRelativePath, string Markdown);

    public sealed record PdfRenderResult(
        bool Succeeded,
        string? Error,
        string Log,
        int DurationMs);

    /// <summary>
    /// A configured <see cref="DeliverablesSettings.BrowserPath"/> is exclusive: if it is set
    /// and missing, we do not fall through to Edge/Chrome (the operator named a specific binary).
    /// Null/empty auto-detects Edge, then Chrome.
    /// </summary>
    public string? ResolveBrowserPath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BrowserPath))
            return File.Exists(_settings.BrowserPath) ? _settings.BrowserPath : null;
        foreach (var candidate in EdgeCandidates.Concat(ChromeCandidates))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static IReadOnlyList<string> BuildArguments(string pdfPath, string htmlPath)
    {
        var fileUrl = ToFileUrl(htmlPath);
        return
        [
            HeadlessArg,
            DisableGpuArg,
            NoHeaderFooterArg,
            PrintToPdfPrefix + pdfPath,
            fileUrl,
        ];
    }

    public string ToHtml(string coverLine, IReadOnlyList<DocumentSection> sections)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>Deliverable</title>
            <style>
            @page { size: A4; margin: 18mm; }
            body { font-family: system-ui, "Segoe UI", sans-serif; color: #111; line-height: 1.45; }
            .cover { font-size: 12pt; margin: 0 0 18pt 0; }
            section + section { page-break-before: always; }
            h1 { font-size: 16pt; }
            pre { white-space: pre-wrap; font-family: Consolas, "Courier New", monospace; background: #f6f6f6; padding: 8px; }
            code { font-family: Consolas, "Courier New", monospace; }
            table { border-collapse: collapse; }
            table, th, td { border: 1px solid #888; padding: 4px 8px; }
            blockquote { border-left: 3px solid #ccc; margin-left: 0; padding-left: 12px; color: #444; }
            </style>
            </head>
            <body>

            """);
        sb.Append("<p class=\"cover\">").Append(WebUtility.HtmlEncode(coverLine)).Append("</p>\n");
        foreach (var section in sections)
        {
            sb.Append("<section>\n<h1>")
              .Append(WebUtility.HtmlEncode(section.RepoRelativePath.Replace('\\', '/')))
              .Append("</h1>\n");
            sb.Append(Markdown.ToHtml(section.Markdown ?? string.Empty, Pipeline));
            sb.Append("\n</section>\n");
        }

        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>Markdig HTML for a single document — used by renderer tests without a browser.</summary>
    public static string ToMarkdownHtml(string markdown) =>
        Markdown.ToHtml(markdown ?? string.Empty, Pipeline);

    public async Task<PdfRenderResult> RenderToPdfAsync(
        string html,
        string pdfPath,
        CancellationToken ct)
    {
        var browser = ResolveBrowserPath();
        if (browser is null)
        {
            var missing = string.IsNullOrWhiteSpace(_settings.BrowserPath)
                ? "no Edge/Chrome found in default locations"
                : $"browser not found at Deliverables:BrowserPath ({_settings.BrowserPath})";
            return new PdfRenderResult(false, missing, missing, 0);
        }

        var htmlPath = Path.Combine(
            Path.GetTempPath(),
            $"antiphon-deliverable-{Guid.NewGuid():N}.html");
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.RenderTimeoutSeconds));
        var started = Stopwatch.StartNew();
        try
        {
            await File.WriteAllTextAsync(htmlPath, html, ct);
            var args = BuildArguments(pdfPath, htmlPath);
            var run = await RunBrowserAsync(browser, args, timeout, ct);
            started.Stop();
            var log = TrimLog($"exit {run.ExitCode}\n{run.Stdout}\n{run.Stderr}");
            if (run.TimedOut)
            {
                return new PdfRenderResult(
                    false,
                    $"PDF render timed out after {_settings.RenderTimeoutSeconds}s",
                    log,
                    (int)started.ElapsedMilliseconds);
            }

            if (run.ExitCode != 0)
            {
                return new PdfRenderResult(
                    false,
                    TrimError($"browser exited {run.ExitCode}"),
                    log,
                    (int)started.ElapsedMilliseconds);
            }

            if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length == 0)
            {
                return new PdfRenderResult(
                    false,
                    "browser exited 0 but wrote no PDF",
                    log,
                    (int)started.ElapsedMilliseconds);
            }

            return new PdfRenderResult(true, null, log, (int)started.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            started.Stop();
            return new PdfRenderResult(
                false,
                $"PDF render timed out after {_settings.RenderTimeoutSeconds}s",
                "timed out",
                (int)started.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            started.Stop();
            _logger.LogWarning(ex, "Markdown PDF render failed for {PdfPath}", pdfPath);
            return new PdfRenderResult(
                false,
                TrimError(ex.Message),
                ex.ToString(),
                (int)started.ElapsedMilliseconds);
        }
        finally
        {
            try { File.Delete(htmlPath); }
            catch (IOException) { }
        }
    }

    internal static string ToFileUrl(string path)
    {
        var full = Path.GetFullPath(path).Replace('\\', '/');
        if (full.Length >= 2 && full[1] == ':')
            return "file:///" + full;
        return "file://" + full;
    }

    private async Task<BrowserRun> RunBrowserAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        if (TestHang is not null)
        {
            try
            {
                await TestHang(timeoutCts.Token);
                return new BrowserRun(0, "", "", TimedOut: false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new BrowserRun(-1, "", "timed out", TimedOut: true);
            }
        }

        var start = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in arguments)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return new BrowserRun(process.ExitCode, await stdoutTask, await stderrTask, TimedOut: false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            return new BrowserRun(-1, "", "timed out", TimedOut: true);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // Already exited, or the platform cannot kill the tree.
        }
    }

    private static string TrimError(string text)
    {
        var oneLine = text.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= 300 ? oneLine : oneLine[..300];
    }

    private static string TrimLog(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 8000 ? trimmed : trimmed[..8000];
    }

    private sealed record BrowserRun(int ExitCode, string Stdout, string Stderr, bool TimedOut);
}
