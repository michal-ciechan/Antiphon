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
}
