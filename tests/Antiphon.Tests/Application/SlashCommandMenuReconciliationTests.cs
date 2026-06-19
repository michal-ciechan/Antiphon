using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// Ground-truth reconciliation between our slash-command catalog (<see cref="SlashCommandCatalogService"/>)
/// and the commands Claude Code actually offers in its live `/` menu. Launches a real Claude TUI in the
/// repo, enumerates the full menu via prefix-descent (type `/&lt;prefix&gt;`; if the result set overflows the
/// screen, recurse into `/&lt;prefix&gt;a`, `/&lt;prefix&gt;b`, … — never scroll), unions the leaves, then diffs that
/// against the catalog built for the same directories.
///
/// Opt-in: needs Windows ConPTY, <c>ANTIPHON_HEADED_TESTS=1</c>, and the <c>claude</c> CLI on PATH. It is
/// slow and, because the TUI render is imperfect to scrape, can surface scrape noise as drift — a failure
/// means "go look at the report", which is written to <c>logs/slash-reconciliation-report.txt</c>.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
public class SlashCommandMenuReconciliationTests
{
    private const string EnvFlag = "ANTIPHON_HEADED_TESTS";
    private const int Cols = 220;
    private const int Rows = 60;
    // When at least this many TRUE-prefix matches are visible for a prefix, assume the on-screen list is
    // truncated and there may be hidden later-alphabet siblings, so expand every next char (not just the
    // visible ones). Kept below the menu's on-screen row capacity so truncation can't hide a next char.
    private const int TruncationGuard = 8;
    private const int MaxDepth = 14;
    private const int MaxQueries = 6000;
    // A real command recurs across many fuzzy queries; a one-off render-bleed artifact appears once. Only
    // count a scraped name as a real menu entry if it was seen in at least this many distinct queries.
    private const int MinOccurrences = 2;
    // Scraping Claude's TUI `/` menu has an irreducible noise floor (~6-12 entries per run, BOTH directions):
    // deep entries occasionally render mangled (garbled "mashup" names that aren't in the catalog) or drop out
    // entirely (real catalog entries not seen). That floor is larger than a single new command, so this test
    // can't be an exact 0/0 gate — it is a DIAGNOSTIC. The categorized report (logs/slash-reconciliation-report.txt)
    // is always written for a human to eyeball (real drift = clean names; noise = obvious mangles); the assertion
    // only fails on drift far beyond the scrape floor (a Claude version change or a broken catalog).
    private const int DriftTolerance = 20;
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-";
    private static readonly string Del = ((char)0x7f).ToString(); // backspace (DEL) in a raw TUI
    private static readonly string Esc = ((char)0x1b).ToString();

    [Test]
    public async Task Catalog_matches_live_claude_slash_menu()
    {
        SkipIfNotEligible();

        var repoRoot = FindRepoRoot();
        var (app, args) = BuildLaunch(ResolveClaude()!);

        await using var runner = new PtyAgentRunner();
        await runner.StartAsync(
            app,
            args,
            cwd: repoRoot,
            env: new Dictionary<string, string> { ["DISABLE_AUTOUPDATER"] = "1" },
            cols: Cols,
            rows: Rows);

        var ready = await new ClaudeReadyDetector { MaxWait = TimeSpan.FromMinutes(1) }.WaitAsync(runner);
        if (!ready)
            throw new SkipTestException("Claude TUI did not reach a ready state");

        var counts = await EnumerateMenuAsync(runner);

        await runner.WriteAsync(Esc); // close the menu
        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));

        var catalog = BuildCatalog(repoRoot);
        var catalogNames = catalog.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A scraped name in the catalog is trusted at one sighting (it's a real command the scrape happened
        // to catch); a name NOT in the catalog must recur (>= MinOccurrences) to count, so one-off render
        // bleed is rejected while a genuinely new command — which appears under many prefix queries — passes.
        var menu = counts
            .Where(kv => kv.Value >= MinOccurrences || (kv.Value >= 1 && catalogNames.Contains(kv.Key)))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var menuOnly = menu.Where(n => !catalogNames.Contains(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var catalogOnly = catalogNames.Where(n => !menu.Contains(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        var report = BuildReport(menu, catalogNames, menuOnly, catalogOnly, catalog);
        WriteReport(repoRoot, report);

        // Fail only on drift beyond the scrape-noise floor (real Claude-version change or broken catalog).
        // For normal drift-checking, read the report and eyeball it — clean recurring names = real drift.
        if (menuOnly.Count > DriftTolerance || catalogOnly.Count > DriftTolerance)
        {
            throw new Exception(
                "Slash-command catalog drift exceeds the scrape-noise floor. " +
                $"menu={menu.Count} catalog={catalogNames.Count} " +
                $"missing-from-catalog={menuOnly.Count} extra-in-catalog={catalogOnly.Count} " +
                $"(tolerance {DriftTolerance}).\n" +
                "See logs/slash-reconciliation-report.txt for the full categorized diff.\n\n" +
                report);
        }
    }

    // Prefix-descent driven by the real command structure (depth-first). Claude's `/` filter is FUZZY, so
    // a prefix shows both true-prefix matches and incidental subsequence matches. We record everything (all
    // visible names are real commands), but only DESCEND a prefix that is a TRUE prefix of something visible
    // — otherwise nonsense like "/aaa" (which still fuzzy-matches many) would recurse forever. From a real
    // prefix we recurse the next chars actually observed; when the true-prefix matches look truncated we
    // expand every next char so hidden later-alphabet siblings (e.g. /bmad-wds-w*) aren't missed.
    private static async Task<Dictionary<string, int>> EnumerateMenuAsync(PtyAgentRunner runner)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        for (var i = Alphabet.Length - 1; i >= 0; i--)
            stack.Push(Alphabet[i].ToString());

        var lastTypedLen = 0;
        var queries = 0;
        while (stack.Count > 0 && queries < MaxQueries)
        {
            var prefix = stack.Pop();
            queries++;

            // Reset the input to empty, then type "/<prefix>".
            for (var i = 0; i < lastTypedLen; i++)
                await runner.WriteAsync(Del);
            await Task.Delay(15);
            await runner.WriteAsync("/" + prefix);
            lastTypedLen = prefix.Length + 1;
            await Task.Delay(110);

            // Read the menu only once it has stopped repainting — reading mid-redraw is what produces the
            // garbled "mashup" names (two entries' text overlaid). Require two identical consecutive reads.
            var visible = await StableMenuAsync(runner);
            foreach (var n in visible)
                counts[n] = counts.GetValueOrDefault(n) + 1;

            if (prefix.Length >= MaxDepth)
                continue;

            // True-prefix matches: the command name (without the leading '/') starts with this prefix.
            var truePrefix = visible
                .Select(n => n[1..])
                .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (truePrefix.Count == 0)
                continue; // not a real prefix — prune (avoids descending into fuzzy nonsense)

            // Next chars after the prefix among the true-prefix matches.
            var nextChars = truePrefix
                .Where(n => n.Length > prefix.Length)
                .Select(n => char.ToLowerInvariant(n[prefix.Length]))
                .Where(c => Alphabet.Contains(c))
                .ToHashSet();

            // If the list looks truncated, hidden siblings may use next chars we didn't see — expand all.
            var charsToPush = truePrefix.Count >= TruncationGuard ? Alphabet : new string(nextChars.ToArray());
            for (var i = charsToPush.Length - 1; i >= 0; i--)
                stack.Push(prefix + charsToPush[i]);
        }

        return counts;
    }

    // Snapshot the menu repeatedly until two consecutive reads parse to the same set, so we never capture a
    // mid-redraw frame (the source of garbled "mashup" names). Falls back to the last read after a few tries.
    private static async Task<List<string>> StableMenuAsync(PtyAgentRunner runner)
    {
        var previous = ParseMenu(runner.SnapshotScreen());
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await Task.Delay(120);
            var current = ParseMenu(runner.SnapshotScreen());
            if (current.Count == previous.Count && current.SequenceEqual(previous, StringComparer.OrdinalIgnoreCase))
                return current;
            previous = current;
        }
        return previous;
    }

    // Extract distinct "/name" tokens from menu lines, in on-screen order. The rendered screen is imperfect
    // (Claude's TUI does partial redraws), so strip ANSI and stray 256-colour residue ("...test153m" ->
    // "...test") before matching.
    private static List<string> ParseMenu(string screen)
    {
        var clean = AnsiStripper.Clean(screen) ?? string.Empty;
        clean = Regex.Replace(clean, @"\[[0-9;?]*[A-Za-z]", string.Empty);
        clean = Regex.Replace(clean, @"[0-9;]*[0-9]+m", string.Empty); // colour-code residue

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // A menu entry line is: optional indent, /name, 2+ spaces, then a description.
        foreach (Match m in Regex.Matches(clean, @"(?m)^\s{0,4}(/[a-z0-9][a-z0-9:_-]*)\s{2,}\S"))
            if (seen.Add(m.Groups[1].Value))
                names.Add(m.Groups[1].Value);
        return names;
    }

    private static IReadOnlyList<SlashCommandDto> BuildCatalog(string repoRoot)
    {
        var configProvider = new ClaudeConfigDirProvider();
        var service = new SlashCommandCatalogService(
            scopeFactory: null!,
            configProvider,
            new System.IO.Abstractions.FileSystem(),
            new FakeTimeProvider(),
            NullLogger<SlashCommandCatalogService>.Instance);

        var userDir = configProvider.Resolve();
        var projectDir = Path.Combine(repoRoot, ".claude");
        return service.GetForDirsAsync(userDir, projectDir, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string BuildReport(
        HashSet<string> menu,
        HashSet<string> catalogNames,
        IReadOnlyList<string> menuOnly,
        IReadOnlyList<string> catalogOnly,
        IReadOnlyList<SlashCommandDto> catalog)
    {
        var bySource = catalog
            .GroupBy(c => c.Source + "/" + c.Scope)
            .ToDictionary(g => g.Key, g => g.Count());

        var sb = new StringBuilder();
        sb.AppendLine("# Slash-command reconciliation report");
        sb.AppendLine($"menu entries : {menu.Count}");
        sb.AppendLine($"catalog      : {catalogNames.Count}  (" +
            string.Join(", ", bySource.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")) + ")");
        sb.AppendLine();
        sb.AppendLine($"## In Claude's menu but MISSING from catalog ({menuOnly.Count})");
        foreach (var n in menuOnly) sb.AppendLine("  " + n);
        sb.AppendLine();
        sb.AppendLine($"## In catalog but NOT in Claude's menu ({catalogOnly.Count})");
        foreach (var n in catalogOnly)
        {
            var dto = catalog.First(c => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"  {n}   [{dto.Source}/{dto.Scope}]");
        }
        return sb.ToString();
    }

    private static void WriteReport(string repoRoot, string report)
    {
        try
        {
            var dir = Path.Combine(repoRoot, "logs");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "slash-reconciliation-report.txt"), report);
        }
        catch
        {
            // Best-effort artifact.
        }
    }

    private static void SkipIfNotEligible()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("Headed tests require Windows ConPTY");
        if (Environment.GetEnvironmentVariable(EnvFlag) != "1")
            throw new SkipTestException($"Set {EnvFlag}=1 to opt in to the headed reconciliation test");
        if (ResolveClaude() is null)
            throw new SkipTestException("claude CLI not found on PATH");
    }

    private static string? ResolveClaude()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in new[] { "claude.cmd", "claude.exe", "claude.bat", "claude.ps1" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static (string app, string[] args) BuildLaunch(string claude)
    {
        var extra = new[] { "--dangerously-skip-permissions" };
        if (claude.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            return ("pwsh.exe", new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", claude }.Concat(extra).ToArray());
        if (claude.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return (claude, extra);
        return (Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            new[] { "/d", "/c", claude }.Concat(extra).ToArray());
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (.git)");
    }
}
