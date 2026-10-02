using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.Checkpoints.Coverage;

public sealed record CoverageSource(string Path, string Text);
public sealed record CoverageLocation(int Line, int Column);
public sealed record CoverageMatch(string Path, int Line);
public sealed record CoverageObligation(string Id, string Test, string Kind, string Name, int PlanLine, int PlanColumn = 1, string? Maps = null)
{
    public bool FromChecklist { get; init; }
    public List<CoverageLocation> Locations { get; init; } = [];
    public List<CoverageMatch> Matches { get; init; } = [];
}
public sealed record CoverageDiagnostic(string Code, int PlanLine = 0, int PlanColumn = 1, string Id = "", string Test = "", string Name = "", string TestPath = "", int TestLine = 0, string Detail = "");
public sealed record CoveragePc(string Id, string Status, string Target, string Test, int TargetLine, int PredecessorLine, IReadOnlyList<string> EarlierOtherLabels, string Reachability = "unproven");
public sealed record CoverageSelection(string Path, string Sha256, IReadOnlyList<string> Classes);
public sealed record CoverageSummary(int Obligations, int Matched, int Missing, int Unmapped, int PcIssues, int PcAdvisories, string Result, string Reachability = "unproven");
public sealed class PlanCoverageReport
{
    public int SchemaVersion => 1;
    public string Mode => "static";
    public string Plan { get; set; } = "";
    public string PlanSha256 { get; set; } = "";
    public string? ChecklistSha256 { get; set; }
    public string InputsSha256 { get; set; } = "";
    public List<CoverageSelection> Sources { get; set; } = [];
    public List<CoverageObligation> Obligations { get; set; } = [];
    public List<CoverageDiagnostic> Exclusions { get; set; } = [];
    public List<CoverageDiagnostic> Diagnostics { get; set; } = [];
    public List<CoveragePc> Pcs { get; set; } = [];
    public bool Invalid { get; set; }
    public CoverageSummary Summary => new(Obligations.Count, Obligations.Count(o => o.Matches.Count > 0),
        Diagnostics.Count(d => d.Code.StartsWith("MISSING_", StringComparison.Ordinal)),
        Diagnostics.Count(d => d.Code.Contains("UNMAPPED", StringComparison.Ordinal)),
        Pcs.Count(p => p.Status != "static-labeled"), Pcs.Count(p => p.EarlierOtherLabels.Count > 0),
        Invalid ? "invalid" : Diagnostics.Any(d => !d.Code.StartsWith("PC_", StringComparison.Ordinal)) || Pcs.Any(p => p.Status != "static-labeled") ? "findings" : "clean");
    public int ExitCode => Invalid ? 2 : Summary.Result == "clean" ? 0 : 1;
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public string Json() => JsonSerializer.Serialize(this, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    public string Text()
    {
        var b = new StringBuilder();
        string Q(string s) => JsonSerializer.Serialize(s);
        b.AppendLine($"PLAN-COVERAGE schema=1 mode=static plan={Q(Plan)} planSha256={PlanSha256} inputsSha256={InputsSha256} files={Sources.Count}");
        foreach (var d in Diagnostics)
            b.AppendLine($"COVERAGE code={d.Code} planLine={d.PlanLine} planColumn={d.PlanColumn} id={d.Id} test={Q(d.Test)} name={Q(d.Name)} testPath={Q(d.TestPath)} testLine={d.TestLine} detail={Q(d.Detail)}");
        foreach (var p in Pcs)
            b.AppendLine($"PC-COVERAGE id={p.Id} status={p.Status} target={Q(p.Target)} test={Q(p.Test)} targetLine={p.TargetLine} predecessorLine={p.PredecessorLine} earlierOtherLabels={JsonSerializer.Serialize(p.EarlierOtherLabels)} reachability=unproven");
        var s = Summary;
        b.AppendLine($"PLAN-COVERAGE-END obligations={s.Obligations} matched={s.Matched} missing={s.Missing} unmapped={s.Unmapped} pcIssues={s.PcIssues} pcAdvisories={s.PcAdvisories} result={s.Result} reachability=unproven");
        return b.ToString();
    }
}
