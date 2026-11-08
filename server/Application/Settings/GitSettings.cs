namespace Antiphon.Server.Application.Settings;

public class GitSettings
{
    public int MaxConcurrentProcesses { get; set; } = 8;
    public int TimeoutSeconds { get; set; } = 15;
    public string ExecutableName { get; set; } = "git";
    public string WorkspacePath { get; set; } = "work";
    public string DefaultBranch { get; set; } = "master";
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

    /// <summary>
    /// CARD-0543: an <c>index.lock</c> this old with no git process started at or before its
    /// mtime is <c>stale</c>. Must be at least 30. Scripts mirror 300 as a constant.
    /// </summary>
    public int IndexLockStaleAfterSeconds { get; set; } = 300;

    /// <summary>Shared monotonic inspection budget for one card-bound worktree base decision.</summary>
    public int WorktreeBaseInspectionTimeoutSeconds { get; set; } = 5;
    public int WorktreeBaseMaxCandidates { get; set; } = 16;
    public int WorktreeBaseMaxInventoryRows { get; set; } = 256;
    public int WorktreeBaseMaxGitCommands { get; set; } = 128;

    /// <summary>
    /// CARD-1776. Absolute path of the mounted GitHub token file. Empty, together with
    /// <see cref="PushCredentialPolicyPath"/>, leaves the desktop land path unchanged.
    /// The probe opens the file for read and checks that it is non-empty. It does not
    /// read or cache the bytes.
    /// </summary>
    public string PushCredentialTokenFile { get; set; } = "";

    /// <summary>
    /// CARD-1776. Absolute path of the push allow-list. Empty, together with
    /// <see cref="PushCredentialTokenFile"/>, leaves the desktop land path unchanged.
    /// </summary>
    public string PushCredentialPolicyPath { get; set; } = "";

    /// <summary>
    /// CARD-1776. Absolute path of the mounted git identity file (<c>user.name</c> and
    /// <c>user.email</c>). Empty leaves the desktop path unchanged. When set, a missing or
    /// incomplete file refuses before git starts, and a usable file is the child's
    /// <c>GIT_CONFIG_GLOBAL</c>. Each command reads the file again, so the answer is not cached.
    /// </summary>
    public string IdentityFile { get; set; } = "";
}
