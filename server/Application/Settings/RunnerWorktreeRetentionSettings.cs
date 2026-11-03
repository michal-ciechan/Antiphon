using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-1677. Runner-side retention of FINISHED tasks' mirrors. Off by default (<see cref="Enabled"/> false,
/// <see cref="Runners"/> empty) and dry-run by default (<see cref="Execute"/> false): the server decides which
/// tasks are finished, the runner rechecks every filesystem and git fact itself. Enabling is the documented
/// three-step rollout in docs/docker-stack.md ("Runner worktree retention (CARD-1677)").
/// </summary>
public sealed class RunnerWorktreeRetentionSettings
{
    public const string SectionName = "RunnerWorktreeRetention";

    public bool Enabled { get; set; }

    /// <summary>False sends DryRun only (the runner reports <c>would_remove</c>, nothing moves).</summary>
    public bool Execute { get; set; }

    /// <summary>Allow-listed Linux phone-home runner ids. Empty contacts no runner.</summary>
    public List<string> Runners { get; set; } = [];

    public string RecurringJobId { get; set; } = "antiphon:runner-worktree-retention";

    /// <summary>Five-field cron (UTC). Default every 30 minutes.</summary>
    public string Cron { get; set; } = "*/30 * * * *";

    /// <summary>D-1. A terminal task is a candidate only once its CompletedAt is at least this old.</summary>
    public int MinTerminalHours { get; set; } = 12;

    /// <summary>I-9. At most this many requests per runner per tick.</summary>
    public int MaxRemovalsPerTick { get; set; } = 25;

    public int PauseBetweenSeconds { get; set; } = 5;

    public int RequestTimeoutSeconds { get; set; } = 300;

    /// <summary>The runner's worktree root as the task rows name it; a candidate path is its direct child.</summary>
    public string WorktreeRoot { get; set; } = "/work/worktrees";

    /// <summary>The runner's retiring root (a sibling of <see cref="WorktreeRoot"/>, never inside it).</summary>
    public string RetiringRoot { get; set; } = "/work/worktrees-retiring";

    /// <summary>CARD-1677 S2 (not implemented in S1). Carried here so its defaults ship off.</summary>
    public RunnerTempSweepSettings TempSweep { get; set; } = new();
}

public sealed class RunnerTempSweepSettings
{
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
        _ = failures; // CARD-1677 red shim: no bound is enforced yet
        return ValidateOptionsResult.Success;
    }

    private static bool IsPosixAbsolute(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.StartsWith('/') && !path.Contains("/../", StringComparison.Ordinal)
        && !path.EndsWith("/..", StringComparison.Ordinal);

    private static string Trim(string path) => path.TrimEnd('/');
}
