namespace Antiphon.Server.Application.Settings;

public class GitSettings
{
    public string WorkspacePath { get; set; } = "work";
    public string DefaultBranch { get; set; } = "main";
    public int PollIntervalSeconds { get; set; } = 30;
    public string WorktreeBasePath { get; set; } = "/tmp/antiphon-worktrees";
}
