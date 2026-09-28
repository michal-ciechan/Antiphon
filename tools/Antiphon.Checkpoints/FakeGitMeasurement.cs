using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Antiphon.Checkpoints;

public sealed class FakeGitPilotCase
{
    public string Method { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Outcome { get; set; } = "passed";
    public string Observation { get; set; } = "";
    public int? ObservedGitLaunches { get; set; }
    public bool LaunchObserverPresent { get; set; } = true;
}

public sealed class FakeGitPilotRow
{
    public string Checkpoint { get; set; } = "";
    public string RunId { get; set; } = "";
    public string SourceSha { get; set; } = "";
    public string BuildSourceSha { get; set; } = "";
    public string HostId { get; set; } = "";
    public string Os { get; set; } = "";
    public string SdkVersion { get; set; } = "";
    public string GitVersion { get; set; } = "";
    public string Backend { get; set; } = "";
    public string Storage { get; set; } = "";
    public string TimingOrigin { get; set; } = "outer-no-build";
    public double? OuterNoBuildSeconds { get; set; }
    public double? TrxElapsedSeconds { get; set; }
    public double? BuildSeconds { get; set; }
    public double? SlotWaitSeconds { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int Reruns { get; set; }
    public List<FakeGitPilotCase> Cases { get; set; } = [];
}

public sealed record FakeGitPilotVerdict(bool Pass, string Reason, double RealMedian,
    double FakeMedian, double SecondsSaved, double PercentSaved);

public static class FakeGitMeasurement
{
    public sealed class PilotPointer
    {
        public string RunDirectory { get; set; } = "";
        public string RunId { get; set; } = "";
        public string ManifestHash { get; set; } = "";
        public string TestedSha { get; set; } = "";
        public string HostId { get; set; } = "";
        public string GitVersion { get; set; } = "";
        public Dictionary<string, string> ReceiptDirectories { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed record SlicePair(string ClassName, string SourceSha, double RealSeconds,
        double FakeSeconds, bool ContractPresent);

    public static bool EvaluateSliceGate(IReadOnlyList<SlicePair> pairs, IReadOnlyList<string> requiredClasses)
    {
        if (pairs.Count != requiredClasses.Count || pairs.Select(p => p.ClassName).Distinct().Count() != pairs.Count)
            return false;
        if (pairs.Select(p => p.SourceSha).Distinct().Count() != 1) return false;
        return requiredClasses.All(c => pairs.Any(p => p.ClassName == c))
            && pairs.All(p => p.ContractPresent && p.FakeSeconds < p.RealSeconds);
    }

    public sealed record AuditEntry(string Slice, bool GatePassed, string Os, bool Skipped,
        string SourceSha, string BuildSourceSha, bool RetentionPresent);

    public static bool EvaluateAudit(IReadOnlyList<AuditEntry> entries)
    {
        var required = Enumerable.Range(1, 12).Select(x => "S" + x).ToArray();
        return required.All(s => entries.Count(e => e.Slice == s) == 1)
            && entries.All(e => e.GatePassed && !e.Skipped && e.SourceSha == e.BuildSourceSha
                && e.RetentionPresent && (e.Slice != "S12" || e.Os == "Windows"));
    }

    public static readonly string[] PilotMethods =
    [
        "GetWorkflowMasterBranch_ReturnsCorrectFormat",
        "GetStageBranch_ReturnsCorrectFormat",
        "GetStageTag_ReturnsCorrectFormat",
        "GetStageTag_VersionIncrements",
        "GetArtifactDirectory_ReturnsCorrectFormat",
        "GetStageBranch_WithSpacesInName_IncludesSpaces",
        "InitializeWorkflowBranchesAsync_CreatesWorkflowMasterBranch",
        "CreateStageBranchAsync_CreatesBranchFromWorkflowMaster",
        "CommitArtifactAsync_CreatesFileAndCommitsWithAntiphonTrailer",
        "TagStageAsync_CreatesVersionedTag",
        "MergeStageBranchAsync_MergesIntoWorkflowMaster",
        "GetDiffBetweenTagsAsync_ReturnsDiff"
    ];
    private static readonly string[] Checkpoints = ["CP-3", "CP-4", "CP-5", "CP-6", "CP-7", "CP-8"];
    private static readonly string[] Backends = ["real", "fake", "fake", "real", "real", "fake"];

    public static FakeGitPilotVerdict EvaluatePilot(IReadOnlyList<FakeGitPilotRow> rows)
    {
        FakeGitPilotVerdict Reject(string reason) => new(false, reason, 0, 0, 0, 0);
        if (rows.Count != 6) return Reject("missing-row");
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Checkpoint != Checkpoints[i]) return Reject("wrong-order-or-duplicate-row");
            if (row.Backend != Backends[i]) return Reject("wrong-backend");
            if (row.Storage != (row.Backend == "fake" ? "mock" : "physical")) return Reject("wrong-storage");
            if (string.IsNullOrWhiteSpace(row.RunId) || string.IsNullOrWhiteSpace(row.SourceSha)
                || string.IsNullOrWhiteSpace(row.BuildSourceSha) || string.IsNullOrWhiteSpace(row.HostId)
                || string.IsNullOrWhiteSpace(row.Os) || string.IsNullOrWhiteSpace(row.SdkVersion)
                || string.IsNullOrWhiteSpace(row.GitVersion)) return Reject("missing-identity");
            if (row.RunId != rows[0].RunId || row.SourceSha != rows[0].SourceSha
                || row.BuildSourceSha != rows[0].BuildSourceSha || row.HostId != rows[0].HostId
                || row.Os != rows[0].Os || row.SdkVersion != rows[0].SdkVersion
                || row.GitVersion != rows[0].GitVersion) return Reject("mixed-source-or-host");
            if (row.SourceSha != row.BuildSourceSha) return Reject("stale-build");
            if (row.TimingOrigin != "outer-no-build" || row.OuterNoBuildSeconds is null
                || row.OuterNoBuildSeconds <= 0 || row.TrxElapsedSeconds is null
                || row.BuildSeconds is null || row.SlotWaitSeconds is null)
                return Reject("invalid-timing-origin");
            if (row.Failed != 0 || row.Skipped != 0 || row.Reruns != 0) return Reject("failed-skipped-or-rerun");
            if (row.Cases.Count != PilotMethods.Length) return Reject("missing-case");
            var names = row.Cases.Select(c => c.Method).ToArray();
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length) return Reject("duplicate-case");
            if (!names.Order(StringComparer.Ordinal).SequenceEqual(PilotMethods.Order(StringComparer.Ordinal)))
                return Reject("wrong-roster");
            foreach (var test in row.Cases)
            {
                if (test.Arguments != "") return Reject("changed-argument");
                if (test.Outcome != "passed") return Reject("case-outcome");
                if (!test.LaunchObserverPresent || test.ObservedGitLaunches is null)
                    return Reject("missing-launch-evidence");
                if (row.Backend == "fake" && test.ObservedGitLaunches != 0)
                    return Reject("fake-git-launch");
                if (row.Backend == "real" && IsOperational(test.Method)
                    && test.ObservedGitLaunches <= 0) return Reject("real-operation-zero-launches");
                if (string.IsNullOrWhiteSpace(test.Observation)) return Reject("missing-observation");
            }
        }
        var baseline = rows[0].Cases.ToDictionary(c => c.Method, c => c.Observation, StringComparer.Ordinal);
        foreach (var row in rows.Skip(1))
            foreach (var test in row.Cases)
                if (test.Observation != baseline[test.Method]) return Reject("observation-mismatch");
        var real = new[] { rows[0], rows[3], rows[4] }.Select(x => x.OuterNoBuildSeconds!.Value).Order().ToArray();
        var fake = new[] { rows[1], rows[2], rows[5] }.Select(x => x.OuterNoBuildSeconds!.Value).Order().ToArray();
        var r = real[1]; var f = fake[1];
        var saved = r - f;
        if (f > .70 * r + 1e-9) return new(false, "ratio", r, f, saved, 100 * saved / r);
        if (saved < 1 - 1e-9) return new(false, "seconds", r, f, saved, 100 * saved / r);
        if (rows[1].OuterNoBuildSeconds >= rows[0].OuterNoBuildSeconds
            || rows[2].OuterNoBuildSeconds >= rows[3].OuterNoBuildSeconds
            || rows[5].OuterNoBuildSeconds >= rows[4].OuterNoBuildSeconds)
            return new(false, "paired-order", r, f, saved, 100 * saved / r);
        return new(true, "pass", r, f, saved, 100 * saved / r);
    }

    public static bool IsOperational(string method) =>
        !PilotMethods.Take(6).Contains(method, StringComparer.Ordinal);

    public static int EvaluateFromPointer(string pointerPath)
    {
        var pointer = JsonSerializer.Deserialize<PilotPointer>(File.ReadAllText(pointerPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty FakeGit source pointer");
        if (string.IsNullOrWhiteSpace(pointer.RunId) ||
            !string.Equals(Path.GetFileName(Path.GetFullPath(pointer.RunDirectory)), pointer.RunId,
                StringComparison.Ordinal)) throw new InvalidDataException("FakeGit run identity mismatch");
        var report = JsonSerializer.Deserialize<ReportModel>(
            File.ReadAllText(Path.Combine(pointer.RunDirectory, "report.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Missing checkpoint report");
        if (report.RunId != pointer.RunId || report.ManifestHash != pointer.ManifestHash
            || report.Commit != pointer.TestedSha || report.Verdict != "GREEN")
            throw new InvalidDataException("Stale or mixed FakeGit source run");
        if (pointer.ReceiptDirectories.Count != 6) throw new InvalidDataException("Missing FakeGit receipt directory");
        var rows = new List<FakeGitPilotRow>();
        foreach (var (cp, backend) in Checkpoints.Zip(Backends))
        {
            var reportRow = report.Rows.Single(row => row.Id == cp);
            if (reportRow.Executed != 12 || reportRow.Failed != 0 || reportRow.Skipped != 0
                || reportRow.Reruns != 0 || reportRow.TestProcessSeconds is not > 0
                || reportRow.Trx is null)
                throw new InvalidDataException($"Invalid checkpoint row {cp}");
            if (!pointer.ReceiptDirectories.TryGetValue(cp, out var folder))
                throw new InvalidDataException($"Missing receipt folder {cp}");
            if (Directory.GetFiles(folder, "*.json").Length != 12)
                throw new InvalidDataException($"Incomplete receipt folder {cp}");
            var row = new FakeGitPilotRow
            {
                Checkpoint = cp,
                RunId = pointer.RunId,
                SourceSha = pointer.TestedSha,
                BuildSourceSha = report.Commit,
                HostId = pointer.HostId,
                Os = report.Host.Os,
                SdkVersion = File.ReadAllLines(Path.Combine(pointer.RunDirectory, "host.txt"))
                    .Single(line => line.StartsWith("dotnet=", StringComparison.Ordinal))[7..],
                GitVersion = pointer.GitVersion,
                Backend = backend,
                Storage = backend == "fake" ? "mock" : "physical",
                OuterNoBuildSeconds = reportRow.TestProcessSeconds,
                TrxElapsedSeconds = TrxElapsed(reportRow.Trx),
                BuildSeconds = report.Builds.Single(build => build.Id == "bin-fakegit-s1").Seconds,
                SlotWaitSeconds = double.TryParse(
                    Regex.Match(reportRow.Line ?? "", @"waited=(\d+)s").Groups[1].Value,
                    out var waited) ? waited : throw new InvalidDataException($"Missing slot wait {cp}"),
                Failed = reportRow.Failed.Value,
                Skipped = reportRow.Skipped.Value,
                Reruns = reportRow.Reruns,
            };
            foreach (var method in PilotMethods)
            {
                var file = Path.Combine(folder, method + ".json");
                if (!File.Exists(file)) throw new InvalidDataException($"Missing receipt {cp}/{method}");
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var receipt = document.RootElement;
                if (receipt.GetProperty("checkpoint").GetString() != cp
                    || receipt.GetProperty("backend").GetString() != backend
                    || receipt.GetProperty("storage").GetString() != row.Storage
                    || receipt.GetProperty("method").GetString() != method
                    || receipt.GetProperty("fixtureFailureLedger").GetArrayLength() != 0)
                    throw new InvalidDataException($"Wrong receipt identity {cp}/{method}");
                var commands = receipt.GetProperty("commands").EnumerateArray()
                    .Select(CommandSignature).ToArray();
                row.Cases.Add(new FakeGitPilotCase
                {
                    Method = method,
                    Arguments = "",
                    Outcome = receipt.GetProperty("outcome").GetString() ?? "",
                    Observation = receipt.GetProperty("observations").GetRawText(),
                    ObservedGitLaunches = receipt.GetProperty("observedGitLaunches").GetInt32(),
                    LaunchObserverPresent = receipt.GetProperty("launchObserverPresent").GetBoolean(),
                });
                if (rows.Count > 0)
                {
                    var firstFolder = pointer.ReceiptDirectories["CP-3"];
                    using var first = JsonDocument.Parse(File.ReadAllText(Path.Combine(firstFolder, method + ".json")));
                    var firstCommands = first.RootElement.GetProperty("commands").EnumerateArray()
                        .Select(CommandSignature).ToArray();
                    if (!commands.SequenceEqual(firstCommands, StringComparer.Ordinal))
                        throw new InvalidDataException($"Command sequence drift {cp}/{method}");
                }
            }
            rows.Add(row);
        }
        var verdict = EvaluatePilot(rows);
        var evidence = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pointerPath))!, "fakegit-evidence",
            pointer.TestedSha, pointer.RunId);
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "comparison.json"), JsonSerializer.Serialize(verdict,
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(verdict));
        return verdict.Pass ? 0 : 1;
    }

    private static double TrxElapsed(string path)
    {
        var root = XDocument.Load(path).Root ?? throw new InvalidDataException("Empty TRX");
        var times = root.Elements().Single(x => x.Name.LocalName == "Times");
        return (DateTimeOffset.Parse(times.Attribute("finish")!.Value)
            - DateTimeOffset.Parse(times.Attribute("start")!.Value)).TotalSeconds;
    }

    private static string CommandSignature(JsonElement command)
    {
        var argv = string.Join("\u001f", command.GetProperty("argv").EnumerateArray()
            .Select(arg => Regex.Replace(arg.GetString() ?? "", "[a-f0-9]{40}", "<oid>")));
        var exit = command.GetProperty("ExitCode").GetInt32();
        var error = command.GetProperty("Stderr").GetString() ?? "";
        var category = exit == 0 ? "ok"
            : error.Contains("ambiguous argument", StringComparison.OrdinalIgnoreCase)
                || error.Contains("unknown revision", StringComparison.OrdinalIgnoreCase) ? "missing-revision"
            : error.Contains("fatal: path", StringComparison.OrdinalIgnoreCase) ? "missing-path"
            : error.Contains("already exists", StringComparison.OrdinalIgnoreCase) ? "already-exists"
            : error.Trim();
        return command.GetProperty("role").GetString() + "|" + argv + "|" + exit + "|" + category;
    }
}
