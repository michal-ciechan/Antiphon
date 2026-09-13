using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Application.Services;

public sealed class WorktreeBaseBudgetExceededException : Exception
{
    public WorktreeBaseBudgetExceededException(string reason) : base(reason) => Reason = reason;
    public string Reason { get; }
}

/// <summary>One worktree-base resolution's command/time budget. Equality on the deadline is expired.</summary>
public sealed class WorktreeBaseGitSession
{
    public WorktreeBaseGitSession(GitSettings settings, TimeProvider clock, DateTimeOffset startedAt)
    {
        MaxCommands = Math.Max(1, settings.WorktreeBaseMaxGitCommands);
        MaxCandidates = Math.Max(1, settings.WorktreeBaseMaxCandidates);
        Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.WorktreeBaseInspectionTimeoutSeconds));
        Deadline = startedAt + Timeout;
        Clock = clock;
        StartedAt = startedAt;
    }

    public int MaxCommands { get; }
    public int MaxCandidates { get; }
    public TimeSpan Timeout { get; }
    public DateTimeOffset Deadline { get; }
    public TimeProvider Clock { get; }
    public DateTimeOffset StartedAt { get; }
    public int Admissions { get; private set; }
    public int Starts { get; private set; }
    public bool FetchAttempted { get; private set; }
    public List<string[]> Commands { get; } = [];

    public double ElapsedSeconds => Math.Max(0, (Clock.GetUtcNow() - StartedAt).TotalSeconds);

    public bool DeadlineReached => Clock.GetUtcNow() >= Deadline;

    public void Admit(IReadOnlyList<string> args)
    {
        if (args.Count > 0 && string.Equals(args[0], "fetch", StringComparison.OrdinalIgnoreCase))
            FetchAttempted = true;
        if (DeadlineReached)
            throw new WorktreeBaseBudgetExceededException("inspection_timeout");
        if (Admissions >= MaxCommands || Starts >= MaxCommands)
            throw new WorktreeBaseBudgetExceededException("git_command_limit");
        Admissions++;
    }

    public void Started(IReadOnlyList<string> args)
    {
        Starts++;
        var copy = new string[args.Count];
        for (var i = 0; i < args.Count; i++) copy[i] = args[i];
        Commands.Add(copy);
    }
}
