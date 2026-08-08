using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Playwright;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// One directory per test holding everything needed to work out why it failed, so investigating a
/// failure never means reading the whole run's stdout.
///
/// Before this, an E2E failure printed the assertion at the top and then ~1500 lines of interleaved
/// server log after it — the useful line scrolled away, and nothing said which test produced which
/// log line. Now the server log, browser console, network failures and page HTML land in
/// <c>TestOutput/Logs/&lt;test&gt;/</c>, and stdout is quiet enough that the assertion is the last
/// thing you see.
///
/// Layout:
/// <code>
///   TestOutput/Logs/&lt;TestName&gt;/
///     server.log       server-side Serilog (via Serilog:LogPath)
///     browser.log      console messages, page errors, failed requests
///     page.html        DOM at the moment of failure
///     notes.log        anything the test itself recorded
/// </code>
/// Screenshots keep their existing home under <c>TestOutput/Screenshots/&lt;TestName&gt;/</c>.
/// </summary>
public sealed class TestDiagnostics
{
    private static readonly string LogRoot = Path.Combine(
        FindRepoRoot(), "tests", "Antiphon.E2E", "TestOutput", "Logs");

    private readonly object _gate = new();

    private TestDiagnostics(string testName, string directory)
    {
        TestName = testName;
        Directory = directory;
    }

    public string TestName { get; }

    /// <summary>Directory holding this test's artefacts. Handed to the app as Serilog:LogPath.</summary>
    public string Directory { get; }

    public string ServerLogDirectory => Directory;

    /// <summary>
    /// Opens (and clears) the artefact directory for the calling test. Clearing matters: a stale
    /// log from the previous run is worse than none, because it looks current.
    /// </summary>
    public static TestDiagnostics For([CallerMemberName] string testName = "")
    {
        var directory = Path.Combine(LogRoot, Sanitize(testName));
        if (System.IO.Directory.Exists(directory))
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(directory))
                TryDelete(file);
        }
        System.IO.Directory.CreateDirectory(directory);
        return new TestDiagnostics(testName, directory);
    }

    /// <summary>Records a line in this test's own log, timestamped.</summary>
    public void Note(string message) => Append("notes.log", message);

    /// <summary>
    /// Attaches browser-side recording to a page: console output, uncaught page errors, and failed
    /// requests. Warnings and errors still reach stdout — those are usually the reason a UI test
    /// failed — while the full stream goes to the file.
    /// </summary>
    public void Attach(IPage page)
    {
        page.Console += (_, msg) =>
        {
            Append("browser.log", $"[console:{msg.Type}] {msg.Text}");
            if (msg.Type is "error" or "warning")
                Console.WriteLine($"[browser:{msg.Type}] {msg.Text}");
        };

        page.PageError += (_, err) =>
        {
            Append("browser.log", $"[pageerror] {err}");
            Console.WriteLine($"[browser:pageerror] {err}");
        };

        // A UI assertion that fails because an API call 500'd is otherwise invisible from the DOM.
        page.RequestFailed += (_, request) =>
            Append("browser.log", $"[requestfailed] {request.Method} {request.Url} — {request.Failure}");

        page.Response += (_, response) =>
        {
            if (response.Status >= 400)
                Append("browser.log", $"[response:{response.Status}] {response.Request.Method} {response.Url}");
        };
    }

    /// <summary>
    /// Call from a test's finally block. On failure, dumps the DOM and points at the directory; on
    /// success, stays quiet apart from one line so a passing run does not grow noise.
    /// </summary>
    public async Task CompleteAsync(IPage? page, bool passed)
    {
        if (!passed && page is not null)
        {
            try
            {
                Write("page.html", await page.ContentAsync());
                Append("notes.log", $"[url at failure] {page.Url}");
            }
            catch (Exception ex)
            {
                Append("notes.log", $"[page dump failed] {ex.Message}");
            }
        }

        Console.WriteLine(passed
            ? $"[diagnostics] {TestName} passed — artefacts: {Directory}"
            : $"[diagnostics] {TestName} FAILED — artefacts: {Directory}");
    }

    private void Append(string fileName, string line)
    {
        var stamped = $"{DateTime.UtcNow:HH:mm:ss.fff} {line}{Environment.NewLine}";
        lock (_gate)
        {
            // Best-effort: a diagnostics write must never be the reason a test fails.
            try
            {
                File.AppendAllText(Path.Combine(Directory, fileName), stamped, Encoding.UTF8);
            }
            catch
            {
                // ignored
            }
        }
    }

    private void Write(string fileName, string content)
    {
        lock (_gate)
        {
            try
            {
                File.WriteAllText(Path.Combine(Directory, fileName), content, Encoding.UTF8);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A locked file from a previous run's still-flushing sink; leave it.
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
