namespace Antiphon.Checkpoints;

public sealed class RunRequest
{
    public List<string> Rows { get; set; } = [];
    public string? Baseline { get; set; }
    public List<string> KnownFlaky { get; set; } = [];
    public int? RowTimeoutMinutes { get; set; }
    public int? TotalTimeoutMinutes { get; set; }
    public int? Parallel { get; set; }
    public bool Serial { get; set; }
    public string Slots { get; set; } = "auto";
    public bool KeepOutputs { get; set; }
    public bool CleanOnRed { get; set; }
    public string RepoRoot { get; set; } = "";
    public string Commit { get; set; } = "";
    public string Branch { get; set; } = "";
    public string? OwnerTaskId { get; set; }
    public string? OwnerSessionId { get; set; }
}
