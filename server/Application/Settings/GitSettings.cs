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
}
