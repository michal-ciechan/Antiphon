using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1817. Bounds for the runner's own repository gc. The pass is the explicit owner
/// after CARD-1441 stops auto gc on task git calls. Defaults match git's <c>gc.auto</c> of 6700
/// loose objects, or seven days, whichever comes first.
/// </summary>
public sealed class RunnerRepositoryMaintenanceOptions
{
    public bool Enabled { get; set; } = true;
    public int LooseObjectThreshold { get; set; } = 6700;
    public int IntervalDays { get; set; } = 7;
    public int CheckIntervalMinutes { get; set; } = 60;
    public int BudgetSeconds { get; set; } = 600;

    public void Validate(string section)
    {
        if (LooseObjectThreshold < 1)
            throw new InvalidOperationException(section + ":LooseObjectThreshold must be at least 1.");
        if (IntervalDays < 1)
            throw new InvalidOperationException(section + ":IntervalDays must be at least 1.");
        if (CheckIntervalMinutes < 1)
            throw new InvalidOperationException(section + ":CheckIntervalMinutes must be at least 1.");
        if (BudgetSeconds is < 1 or > 10_800)
            throw new InvalidOperationException(section + ":BudgetSeconds must be between 1 and 10800.");
    }
}

/// <summary>
/// CARD-1817. One bounded <c>git gc --prune=2.weeks.ago</c> for each repository directly under
/// the runner repository root. Skips a repository while the lease is held, a worktree add or land
/// is in flight, or a task git call is in flight. A task git call that starts after that check,
/// while gc is already running, is not blocked: the pass does not take the lease it skips for.
/// Never follows a linked child and never enters <c>antiphon_pgdata</c>. Never uses
/// <c>gc --auto</c> or <c>--prune=now</c>.
/// </summary>
public sealed class RunnerRepositoryMaintenance
{
    public const string Prune = "2.weeks.ago";

    private readonly string _root;
    private readonly RunnerRepositoryActivity _activity;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly RunnerRepositoryMaintenanceOptions _options;
    private readonly string? _statePath;
    private readonly Func<ProcessStartInfo, Process?> _start;

    public RunnerRepositoryMaintenance(
        string repositoriesRoot,
        RunnerRepositoryActivity activity,
        TimeProvider clock,
        ILogger logger,
        RunnerRepositoryMaintenanceOptions? options = null,
        string? statePath = null,
        Func<ProcessStartInfo, Process?>? start = null)
    {
        _root = repositoriesRoot;
        _activity = activity;
        _clock = clock;
        _logger = logger;
        _options = options ?? new RunnerRepositoryMaintenanceOptions();
        _statePath = statePath;
        _start = start ?? Process.Start;
    }

    public RunnerRepositoryMaintenanceStatus? Last { get; private set; }

    /// <summary>Arguments of the last gc the pass started. Tests read this; production logs the counts.</summary>
    internal IReadOnlyList<string>? LastArguments { get; private set; }

    public static bool IsDue(DateTimeOffset now, int looseObjects, int threshold, DateTimeOffset? lastRunUtc, TimeSpan interval) =>
        looseObjects >= threshold || lastRunUtc is { } last && now - last >= interval;

    public Task<RunnerRepositoryMaintenanceStatus> RunAsync(CancellationToken ct, bool force = false)
    {
        _ = force;
        var now = _clock.GetUtcNow();
        var repos = Discover(_root);
        var reports = new List<RunnerRepositoryMaintenanceReport>(repos.Count);
        foreach (var repo in repos)
        {
            ct.ThrowIfCancellationRequested();
            var gitDir = Path.Combine(repo, ".git");
            reports.Add(Row(repo, "not_run", "not_implemented", CountLoose(gitDir), CountLoose(gitDir), CountPacks(gitDir), CountPacks(gitDir)));
        }
        if (repos.Count == 0)
            _logger.LogInformation("Repository maintenance repos=0 outcome=absent loose=0->0 packs=0->0");
        foreach (var report in reports)
            _logger.LogInformation(
                "Repository maintenance repo={Repository} outcome={Outcome} reason={Reason} loose={LooseBefore}->{LooseAfter} packs={PacksBefore}->{PacksAfter}",
                report.Repository, report.Outcome, report.Reason ?? "", report.LooseBefore, report.LooseAfter,
                report.PacksBefore, report.PacksAfter);
        Last = new RunnerRepositoryMaintenanceStatus(now, reports);
        return Task.FromResult(Last);
    }

    internal static IReadOnlyList<string> Discover(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return [];
        string resolvedRoot;
        try { resolvedRoot = Path.GetFullPath(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return []; }
        if (!Directory.Exists(resolvedRoot))
            return [];
        if (IsRepository(resolvedRoot) && !IsExcluded(resolvedRoot))
            return [resolvedRoot];

        IEnumerable<string> children;
        try { children = Directory.EnumerateDirectories(resolvedRoot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return []; }

        var found = new List<string>();
        foreach (var child in children)
        {
            if (IsExcluded(child))
                continue;
            var resolved = ResolveChild(resolvedRoot, child);
            if (resolved is not null && IsRepository(resolved))
                found.Add(resolved);
        }
        return found;
    }

    internal static int CountLoose(string gitDir)
    {
        var objects = Path.Combine(gitDir, "objects");
        if (!Directory.Exists(objects))
            return 0;
        var count = 0;
        foreach (var dir in Directory.EnumerateDirectories(objects))
        {
            var name = Path.GetFileName(dir);
            if (name.Length != 2 || name is "info" or "pack")
                continue;
            try { count += Directory.EnumerateFiles(dir).Count(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { /* a directory we cannot read is not a count we invent */ }
        }
        return count;
    }

    internal static int CountPacks(string gitDir)
    {
        var pack = Path.Combine(gitDir, "objects", "pack");
        if (!Directory.Exists(pack))
            return 0;
        try { return Directory.EnumerateFiles(pack, "*.pack").Count(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return 0; }
    }

    private string? BlockReason()
    {
        if (_activity.LeaseHeld)
            return "lease";
        if (_activity.WorktreeAddOrLandInFlight)
            return "worktree_or_land";
        if (_activity.TaskGitInFlight)
            return "git_call";
        return null;
    }

    private async Task<RunnerRepositoryMaintenanceReport> GcAsync(
        string repo, string gitDir, int loose, int packs, CancellationToken ct)
    {
        var arguments = new[] { "gc", "--prune=" + Prune };
        LastArguments = arguments;
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);
        foreach (var key in psi.Environment.Keys.Where(key => key.StartsWith("GIT_CONFIG_", StringComparison.Ordinal)).ToArray())
            psi.Environment.Remove(key);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(_options.BudgetSeconds));
        Process? started;
        try { started = _start(psi); }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Row(repo, "failed", "git", loose, loose, packs, packs);
        }
        if (started is null)
            return Row(repo, "failed", "git", loose, loose, packs, packs);

        using (started)
        {
            var stdout = started.StandardOutput.ReadToEndAsync(budget.Token);
            var stderr = started.StandardError.ReadToEndAsync(budget.Token);
            try
            {
                await started.WaitForExitAsync(budget.Token);
            }
            catch (OperationCanceledException)
            {
                try { started.Kill(entireProcessTree: true); } catch { /* already gone */ }
                try { await started.WaitForExitAsync(CancellationToken.None); } catch { /* reaped */ }
                try { await Task.WhenAll(stdout, stderr); } catch { /* cancelled with the budget */ }
                if (ct.IsCancellationRequested)
                    throw;
                return Row(repo, "failed", "budget", loose, CountLoose(gitDir), packs, CountPacks(gitDir));
            }

            try { await Task.WhenAll(stdout, stderr); }
            catch { /* the exit code is the verdict; the counts are on disk */ }
            var looseAfter = CountLoose(gitDir);
            var packsAfter = CountPacks(gitDir);
            return started.ExitCode == 0
                ? Row(repo, "ran", null, loose, looseAfter, packs, packsAfter)
                : Row(repo, "failed", "git", loose, looseAfter, packs, packsAfter);
        }
    }

    private DateTimeOffset? ReadLastRun()
    {
        if (string.IsNullOrWhiteSpace(_statePath) || !File.Exists(_statePath))
            return null;
        try
        {
            var json = File.ReadAllText(_statePath);
            var stamp = JsonSerializer.Deserialize<Stamp>(json);
            return stamp?.LastRunUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    private void WriteLastRun(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(_statePath))
            return;
        try
        {
            var directory = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(new Stamp(now)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _logger.LogWarning("Repository maintenance could not record its last run."); }
    }

    private static RunnerRepositoryMaintenanceReport Row(
        string repo, string outcome, string? reason, int looseBefore, int looseAfter, int packsBefore, int packsAfter) =>
        new(repo, outcome, reason, looseBefore, looseAfter, packsBefore, packsAfter);

    private static bool IsExcluded(string path) =>
        string.Equals(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            "antiphon_pgdata", StringComparison.OrdinalIgnoreCase);

    private static bool IsRepository(string path)
    {
        var git = Path.Combine(path, ".git");
        try { return Directory.Exists(git); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return false; }
    }

    private static string? ResolveChild(string root, string child)
    {
        try
        {
            var info = new DirectoryInfo(child);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                return null;
            var full = Path.GetFullPath(child);
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return candidate.StartsWith(prefix, StringComparison.Ordinal) ? full : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return null; }
    }

    private sealed record Stamp(DateTimeOffset LastRunUtc);
}
