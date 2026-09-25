namespace Antiphon.Checkpoints;

public sealed class RunState
{
    public string RunId { get; set; } = "";
    public string Phase { get; set; } = "running";
    public int ExecutorPid { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? TotalTimeoutAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int? ExitCode { get; set; }
    public int Nonce { get; set; }
    public string Marker { get; set; } = "";
    public int MaxConcurrentRows { get; set; }
    public List<BuildProgress> Builds { get; set; } = [];
    public List<RowProgress> Rows { get; set; } = [];

    public string Heartbeat(DateTimeOffset now)
    {
        var elapsed = FormatDuration((now - StartedAt).TotalSeconds);
        var parts = new List<string>();
        foreach (var row in Rows)
        {
            if (row.State == "running")
            {
                var ago = row.LastOutputAt is DateTimeOffset last
                    ? FormatDuration((now - last).TotalSeconds)
                    : "n/a";
                parts.Add($"{row.Id} running {FormatDuration(row.Seconds)} last-output {ago} ago");
            }
            else if (row.State is "green" or "red")
                parts.Add($"{row.Id} {row.State} {FormatDuration(row.Seconds)}");
            else
                parts.Add($"{row.Id} {row.State}");
        }

        foreach (var build in Builds.Where(b => b.State == "building"))
            parts.Add($"{build.Id} building {FormatDuration(build.Seconds)}");
        return $"HEARTBEAT run={RunId} elapsed={elapsed} | " + string.Join(" | ", parts);
    }

    public static string FormatDuration(double seconds)
    {
        if (seconds < 0)
            seconds = 0;
        var whole = (int)seconds;
        var minutes = whole / 60;
        var secs = whole % 60;
        return $"{minutes}m{secs:00}s";
    }
}

public sealed class BuildProgress
{
    public string Id { get; set; } = "";
    public string State { get; set; } = "pending";
    public double Seconds { get; set; }
    public string Slot { get; set; } = "";
    public int WaitedSeconds { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
}

public sealed class RowProgress
{
    public string Id { get; set; } = "";
    public string State { get; set; } = "queued";
    public DateTimeOffset? StartedAt { get; set; }
    public double Seconds { get; set; }
    public DateTimeOffset? LastOutputAt { get; set; }
    public string? Line { get; set; }
    public int ExitCode { get; set; }
}
