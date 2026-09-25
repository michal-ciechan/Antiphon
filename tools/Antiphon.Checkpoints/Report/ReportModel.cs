namespace Antiphon.Checkpoints;

public sealed class ReportModel
{
    public int SchemaVersion { get; set; } = 1;
    public string RunId { get; set; } = "";
    public string? ManifestPath { get; set; }
    public string ManifestHash { get; set; } = "";
    public string Commit { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Worktree { get; set; } = "";
    public ReportHost Host { get; set; } = new();
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public double WallSeconds { get; set; }
    public double SequentialEquivalentSeconds { get; set; }
    public int ExitCode { get; set; }
    public string Verdict { get; set; } = "GREEN";
    public List<ReportBuild> Builds { get; set; } = [];
    public List<ReportRow> Rows { get; set; } = [];
    public List<string> Unlisted { get; set; } = [];
    public string? Evidence { get; set; }
    public bool CleanedOutputs { get; set; }
    public List<string> OutputNames { get; set; } = [];
    public int MaxConcurrentRows { get; set; }
}

public sealed class ReportHost
{
    public string Os { get; set; } = "";
    public int Cores { get; set; }
    public long MemAvailableMb { get; set; }
    public string LoadAvg { get; set; } = "";
}

public sealed class ReportBuild
{
    public string Id { get; set; } = "";
    public string Project { get; set; } = "";
    public string State { get; set; } = "";
    public double Seconds { get; set; }
    public string Slot { get; set; } = "";
    public int WaitedSeconds { get; set; }
}

public sealed class ReportRow
{
    public string Id { get; set; } = "";
    public string? Group { get; set; }
    public string? Filter { get; set; }
    public string? Command { get; set; }
    public string? Build { get; set; }
    public string State { get; set; } = "";
    public int ExitCode { get; set; }
    public int? Executed { get; set; }
    public int? Passed { get; set; }
    public int? Failed { get; set; }
    public int? Skipped { get; set; }
    public int Reruns { get; set; }
    public string? Trx { get; set; }
    public double Seconds { get; set; }
    public string? Line { get; set; }
    public List<ReportFailure> Failures { get; set; } = [];
    public List<string> RerunLines { get; set; } = [];
    public List<SlowClass> SlowClasses { get; set; } = [];
    public int Attempt { get; set; } = 1;
}

public sealed class ReportFailure
{
    public string Name { get; set; } = "";
    public string Message { get; set; } = "";
    public string StackTrace { get; set; } = "";
    public string StdOut { get; set; } = "";
    public string? Baseline { get; set; }
    public double DurationSeconds { get; set; }
}
