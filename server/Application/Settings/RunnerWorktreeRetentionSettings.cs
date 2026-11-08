using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-1677. Runner-side retention of FINISHED tasks' mirrors: the server decides which tasks are finished,
/// the runner rechecks every filesystem and git fact itself. On by default (operator decision 2026-11-03):
/// <see cref="Enabled"/> and <see cref="Execute"/> are true here and in server/appsettings.json, which also
/// names the runner (<c>Runners=["server2"]</c>; the list stays empty here because configuration binding
/// appends to a list initializer, so a default here could never be emptied). Kill switch:
/// <c>Enabled=false</c>; dry run: <c>Execute=false</c> (or the preview route, always DryRun). See
/// docs/docker-stack.md ("Runner worktree retention (CARD-1677)").
/// </summary>
public sealed class RunnerWorktreeRetentionSettings
{
    public const string SectionName = "RunnerWorktreeRetention";

    /// <summary>The kill switch. Re-read before every request, so switching it off also stops a running tick.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>False sends DryRun only (the runner reports <c>would_remove</c>, nothing moves). Re-read before every request.</summary>
    public bool Execute { get; set; } = true;

    /// <summary>
    /// Allow-listed Linux phone-home runner ids. Empty contacts no runner. Deliberately empty here: the shipped
    /// <c>["server2"]</c> lives in server/appsettings.json only (a list initializer would be appended to by binding).
    /// </summary>
    public List<string> Runners { get; set; } = [];

    public string RecurringJobId { get; set; } = "antiphon:runner-worktree-retention";

    /// <summary>Five-field cron (UTC). Default every 30 minutes.</summary>
    public string Cron { get; set; } = "*/30 * * * *";

    /// <summary>D-1. A terminal task is a candidate only once its CompletedAt is at least this old.</summary>
    public int MinTerminalHours { get; set; } = 12;

    /// <summary>I-9. At most this many requests per runner per tick.</summary>
    public int MaxRemovalsPerTick { get; set; } = 25;

    /// <summary>CARD-1794 (red shim).</summary>
    public int MaxAbsentPerTick { get; set; } = 200;

    public int PauseBetweenSeconds { get; set; } = 5;

    public int RequestTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Review c238f9ca A-2. A tick starts no new request once it has run this long (a slow runner at
    /// <see cref="RequestTimeoutSeconds"/> per request could otherwise outrun the 30-minute schedule).
    /// </summary>
    public int MaxTickMinutes { get; set; } = 20;

    /// <summary>
    /// Review c238f9ca D-1. A tree the runner kept is not re-sent for this many minutes, doubling for each repeat
    /// of the same reason up to <see cref="KeptRecheckMaxHours"/>; a changed reason or changed task facts re-send it at once.
    /// </summary>
    public int KeptRecheckMinutes { get; set; } = 60;

    public int KeptRecheckMaxHours { get; set; } = 24;

    /// <summary>The runner's worktree root as the task rows name it; a candidate path is its direct child.</summary>
    public string WorktreeRoot { get; set; } = "/work/worktrees";

    /// <summary>The runner's retiring root (a sibling of <see cref="WorktreeRoot"/>, never inside it).</summary>
    public string RetiringRoot { get; set; } = "/work/worktrees-retiring";

    /// <summary>
    /// CARD-1677 S2. The /tmp sweep of the runner's registered temp families, under its own switch. It ships OFF and
    /// dry-run (the operator decision turned on the worktree retention only); it runs only inside an enabled tick.
    /// </summary>
    public RunnerTempSweepSettings TempSweep { get; set; } = new();
}

/// <summary>CARD-1677 S2 (plan §3.5). Enabled=false sends nothing; Execute=false (the default) sends DryRun.</summary>
public sealed class RunnerTempSweepSettings
{
    /// <summary>D-12: the CLI scratchpad family, off unless its flag is true.</summary>
    public const string ClaudeScratch = "claude-scratch";

    public bool Enabled { get; set; }
    public bool Execute { get; set; }
    public int MinAgeHours { get; set; } = 48;
    public int MaxEntriesPerTick { get; set; } = 200;
    public long MaxBytesPerTick { get; set; } = 21474836480;
    public Dictionary<string, bool> Families { get; set; } = new(StringComparer.Ordinal) { ["claude-scratch"] = false };
}

public sealed class RunnerWorktreeRetentionSettingsValidator : IValidateOptions<RunnerWorktreeRetentionSettings>
{
    public ValidateOptionsResult Validate(string? name, RunnerWorktreeRetentionSettings options)
    {
        var failures = new List<string>();
        if (options.MinTerminalHours < 1)
            failures.Add("RunnerWorktreeRetention:MinTerminalHours must be at least 1.");
        if (options.MaxRemovalsPerTick is < 1 or > 100)
            failures.Add("RunnerWorktreeRetention:MaxRemovalsPerTick must be between 1 and 100.");
        if (options.PauseBetweenSeconds < 0)
            failures.Add("RunnerWorktreeRetention:PauseBetweenSeconds must not be negative.");
        if (options.RequestTimeoutSeconds < 1)
            failures.Add("RunnerWorktreeRetention:RequestTimeoutSeconds must be positive.");
        if (options.MaxTickMinutes is < 1 or > 600)
            failures.Add("RunnerWorktreeRetention:MaxTickMinutes must be between 1 and 600.");
        if (options.KeptRecheckMinutes < 1)
            failures.Add("RunnerWorktreeRetention:KeptRecheckMinutes must be at least 1.");
        if (options.KeptRecheckMaxHours < 1)
            failures.Add("RunnerWorktreeRetention:KeptRecheckMaxHours must be at least 1.");
        if (!IsPosixAbsolute(options.RetiringRoot))
            failures.Add("RunnerWorktreeRetention:RetiringRoot must be an absolute POSIX path.");
        if (!IsPosixAbsolute(options.WorktreeRoot))
            failures.Add("RunnerWorktreeRetention:WorktreeRoot must be an absolute POSIX path.");
        else if (IsPosixAbsolute(options.RetiringRoot)
            && (Trim(options.RetiringRoot) == Trim(options.WorktreeRoot)
                || Trim(options.RetiringRoot).StartsWith(Trim(options.WorktreeRoot) + "/", StringComparison.Ordinal)))
            failures.Add("RunnerWorktreeRetention:RetiringRoot must not be the worktree root or inside it.");
        if (string.IsNullOrWhiteSpace(options.Cron)
            || options.Cron.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 5)
            failures.Add("RunnerWorktreeRetention:Cron must be a five-field expression.");
        if (options.Runners.Any(string.IsNullOrWhiteSpace))
            failures.Add("RunnerWorktreeRetention:Runners must not contain an empty id.");
        if (options.TempSweep.MinAgeHours < 1)
            failures.Add("RunnerWorktreeRetention:TempSweep:MinAgeHours must be at least 1.");
        if (options.TempSweep.MaxEntriesPerTick is < 1 or > 1000)
            failures.Add("RunnerWorktreeRetention:TempSweep:MaxEntriesPerTick must be between 1 and 1000.");
        if (options.TempSweep.MaxBytesPerTick < 1)
            failures.Add("RunnerWorktreeRetention:TempSweep:MaxBytesPerTick must be positive.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsPosixAbsolute(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.StartsWith('/') && !path.Contains("/../", StringComparison.Ordinal)
        && !path.EndsWith("/..", StringComparison.Ordinal);

    private static string Trim(string path) => path.TrimEnd('/');
}
