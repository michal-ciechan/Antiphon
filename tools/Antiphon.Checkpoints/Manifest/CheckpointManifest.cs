namespace Antiphon.Checkpoints;

/// <summary>In-memory checkpoint manifest (YAML schemaVersion 1).</summary>
public sealed class CheckpointManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string? Plan { get; set; }
    public string ResultsRoot { get; set; } = ".antiphon/checkpoints";
    public BuildDefaults Build { get; set; } = new();
    public ParallelDefaults Parallel { get; set; } = new();
    public TimeoutDefaults Timeouts { get; set; } = new();
    public RerunDefaults Rerun { get; set; } = new();
    public BaselineDefaults Baseline { get; set; } = new();
    public List<BuildSpec> Builds { get; set; } = [];
    public List<CheckpointSpec> Checkpoints { get; set; } = [];

    public int EffectiveMaxRows(bool isWindows) =>
        Parallel.MaxRows is > 0 ? Parallel.MaxRows.Value : isWindows ? 1 : 2;
}

public sealed class BuildDefaults
{
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int MaxCpuCount { get; set; }
    public string Slots { get; set; } = "auto";
    public int SlotWaitMinutes { get; set; } = 45;
}

public sealed class ParallelDefaults
{
    public int? MaxRows { get; set; }
}

public sealed class TimeoutDefaults
{
    public int RowMinutes { get; set; } = 15;
    public int TotalMinutes { get; set; } = 90;
}

public sealed class RerunDefaults
{
    public List<string> KnownFlaky { get; set; } = [];
}

public sealed class BaselineDefaults
{
    public string? Ref { get; set; }
}

public sealed class BuildSpec
{
    public string Id { get; set; } = "";
    public string Project { get; set; } = "";
    public string OutputPath { get; set; } = "";
}

public sealed class CheckpointSpec
{
    public string Id { get; set; } = "";
    public List<string> After { get; set; } = [];
    public string? Build { get; set; }
    public string? Group { get; set; }
    public string? Filter { get; set; }
    public string? Command { get; set; }
    public List<string> Expect { get; set; } = [];
    public string? ExpectText { get; set; }
    public int? MinExecuted { get; set; }
    public int? EstimatedMinutes { get; set; }
    public int? EstimatedMinutesWindows { get; set; }
    public int? TimeoutMinutes { get; set; }
    public bool Serial { get; set; }
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsCommand => !string.IsNullOrWhiteSpace(Command);
}

public sealed class ManifestValidationException : Exception
{
    public ManifestValidationException(string field, string message) : base(message)
    {
        Field = field;
    }

    public string Field { get; }
    public int ExitCode => ExitCodes.Invalid;
}
