using System.Text.Json;

namespace Antiphon.Checkpoints;

public sealed class SourceRunGuard
{
    private readonly object _gate = new();
    private readonly string _repository;
    private readonly Func<string, SourceObservation> _capture;
    private readonly SourceEvidence _evidence;
    private string? _reason;

    public SourceRunGuard(string repository, SourceEvidence evidence,
        Func<string, SourceObservation>? capture = null)
    {
        _repository = repository;
        _capture = capture ?? new SourceSnapshot().Capture;
        _evidence = evidence;
    }

    public string? Reason { get { lock (_gate) return _reason; } }

    public bool Observe()
    {
        lock (_gate)
        {
            var current = _capture(_repository);
            _evidence.End = current;
            var observed = SourceEvidence.StateOf(_evidence.Start, current);
            if (observed == "unknown") _reason ??= "source_unknown";
            else if (observed == "changed") _reason ??= "source_changed";
            _evidence.State = _reason switch
            {
                "source_unknown" => "unknown",
                "source_changed" => "changed",
                _ => observed,
            };
            return _reason is null;
        }
    }

    public SourceEvidence Copy(string buildSource)
    {
        lock (_gate)
            return new SourceEvidence
            {
                Version = _evidence.Version,
                Start = _evidence.Start,
                End = _evidence.End,
                State = _evidence.State,
                BuildSource = buildSource,
            };
    }
}

public sealed class CheckpointBuildBinding
{
    public int Version { get; set; } = 1;
    public string RepositoryRoot { get; set; } = "";
    public string Project { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public List<string> Properties { get; set; } = [];
    public string? Commit { get; set; }
    public string? Fingerprint { get; set; }
    public string SourceState { get; set; } = "unknown";

    public static CheckpointBuildBinding Expected(string root, string project, string output,
        IReadOnlyList<string> properties, SourceObservation source) => new()
    {
        RepositoryRoot = Path.GetFullPath(root),
        Project = Path.GetFullPath(Path.Combine(root, project)),
        OutputPath = Path.GetFullPath(Path.Combine(root, project, output)),
        Properties = properties.Select(value => value["--property:".Length..]).Order(StringComparer.Ordinal).ToList(),
        Commit = source.Commit,
        Fingerprint = source.Fingerprint,
        SourceState = source.DirtyFiles == 0 ? "clean" : source.CaptureStatus == "known" ? "dirty" : "unknown",
    };

    public string PathName => Path.Combine(OutputPath, "checkpoint-build-source.json");

    public string Check()
    {
        if (!File.Exists(PathName)) return "unknown";
        try
        {
            var actual = JsonSerializer.Deserialize<CheckpointBuildBinding>(File.ReadAllText(PathName), CheckpointApp.Json);
            if (actual is null || actual.Version != 1) return "mismatch";
            var rootComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(actual.RepositoryRoot, RepositoryRoot, rootComparison) ||
                !string.Equals(actual.Project, Project, rootComparison) ||
                !string.Equals(actual.OutputPath, OutputPath, rootComparison) ||
                actual.Commit != Commit || actual.Fingerprint != Fingerprint || actual.SourceState != SourceState ||
                !actual.Properties.SequenceEqual(Properties, StringComparer.Ordinal)) return "mismatch";
            return "verified";
        }
        catch (Exception) { return "mismatch"; }
    }

    public void Invalidate()
    {
        if (File.Exists(PathName)) File.Delete(PathName);
    }

    public void Write()
    {
        Directory.CreateDirectory(OutputPath);
        var temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, CheckpointApp.Json));
        File.Move(temporary, PathName, overwrite: true);
    }
}
