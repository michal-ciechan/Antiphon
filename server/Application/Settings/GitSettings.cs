namespace Antiphon.Server.Application.Settings;

public class GitSettings
{
    public int MaxConcurrentProcesses { get; set; } = 8;
    public int TimeoutSeconds { get; set; } = 15;
    public string ExecutableName { get; set; } = "git";
    public string WorkspacePath { get; set; } = "work";
    public string DefaultBranch { get; set; } = "main";
    public int PollIntervalSeconds { get; set; } = 30;
    public string WorktreeBasePath { get; set; } = "/tmp/antiphon-worktrees";
    public int WorktreeStaleAfterDays { get; set; } = 7;
    public int WorktreeJanitorIntervalHours { get; set; } = 24;

    /// <summary>
    /// Per-command budget for <c>git worktree add</c> only. A full checkout under IO load is
    /// legitimately slower than a <c>show-ref</c>; every other git command keeps the 30 s constant
    /// inside <c>WorktreeManager</c>. CARD-0220: 30 s killed three adds in five days.
    /// </summary>
    public int WorktreeAddTimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// Per-command budget for <c>git worktree remove</c> only. Deleting a worktree that still
    /// holds <c>bin/</c>, <c>obj/</c>, and <c>node_modules</c> is slower than a <c>show-ref</c>;
    /// every other git command keeps the 30 s constant inside <c>WorktreeManager</c>. CARD-0328:
    /// 30 s left the registration in place and the land was reported as refused.
    /// </summary>
    public int WorktreeRemoveTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// CARD-0147 S3: how often the detection-only worktree health sweep runs. Must be positive.
    /// The sweep never prunes, removes, or fails a task.
    /// </summary>
    public int WorktreeHealthIntervalSeconds { get; set; } = 60;
}
