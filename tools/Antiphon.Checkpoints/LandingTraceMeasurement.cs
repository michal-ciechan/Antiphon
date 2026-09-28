using System.Diagnostics;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public sealed record LandingTraceVerdict(bool Complete, bool Feasible, string Reason,
    int Cases, int GitLaunches, double GitUnionSeconds, double OuterSeconds, double RequiredSeconds);

public static class LandingTraceMeasurement
{
    public static LandingTraceVerdict Evaluate(string runDirectory, string receiptDirectory)
    {
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(runDirectory, "report.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("missing-report");
        var row = report.Rows.Single(r => r.Id == "CP-42");
        var outer = row.TestProcessSeconds ?? 0;
        var required = Math.Max(0.30 * outer, 1);
        LandingTraceVerdict Reject(string reason, int cases = 0, int launches = 0, double union = 0) =>
            new(false, false, reason, cases, launches, union, outer, required);
        if (report.Verdict != "GREEN" || row.Executed != 19 || row.Passed != 19
            || row.Failed != 0 || row.Skipped != 0 || row.Reruns != 0 || row.TestProcessStartedTimestamp is null
            || row.TestProcessFinishedTimestamp is null || row.TestProcessStopwatchFrequency is null
            || row.TestProcessStopwatchFrequency <= 0 || outer <= 0)
            return Reject("incomplete-checkpoint-row");
        var start = row.TestProcessStartedTimestamp.Value;
        var finish = row.TestProcessFinishedTimestamp.Value;
        var frequency = row.TestProcessStopwatchFrequency.Value;
        if (start >= finish || Math.Abs((finish - start) / (double)frequency - outer) > 0.1)
            return Reject("invalid-outer-interval");
        var bootstrapPath = Path.Combine(receiptDirectory, "bootstrap.json");
        if (!File.Exists(bootstrapPath)) return Reject("missing-bootstrap");
        using (var bootstrap = JsonDocument.Parse(File.ReadAllText(bootstrapPath)))
        {
            var b = bootstrap.RootElement;
            if (b.GetProperty("outcome").GetString() != "complete"
                || b.GetProperty("startedTicks").GetInt64() < start
                || b.GetProperty("finishedTicks").GetInt64() > finish)
                return Reject("invalid-bootstrap");
        }
        var expected = Enumerable.Range(1, 19).Select(i => $"B{i:00}.json").ToArray();
        var actual = Directory.GetFiles(receiptDirectory, "B*.json").Select(Path.GetFileName)
            .Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected)) return Reject("wrong-case-files", actual.Length);
        var intervals = new List<(long Start, long Finish)>();
        var cases = 0;
        foreach (var file in expected)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(receiptDirectory, file)));
            var receipt = document.RootElement;
            var caseId = Path.GetFileNameWithoutExtension(file);
            var caseStart = receipt.GetProperty("startTicks").GetInt64();
            var caseFinish = receipt.GetProperty("finishTicks").GetInt64();
            if (receipt.GetProperty("caseId").GetString() != caseId
                || receipt.GetProperty("runId").GetString() != report.RunId
                || receipt.GetProperty("sourceSha").GetString() != report.Commit
                || receipt.GetProperty("checkpoint").GetString() != "CP-42"
                || receipt.GetProperty("backend").GetString() != "real"
                || receipt.GetProperty("storage").GetString() != "physical"
                || receipt.GetProperty("stopwatchFrequency").GetInt64() != frequency
                || caseStart < start || caseFinish > finish || caseStart >= caseFinish)
                return Reject("wrong-case-identity-or-interval", cases);
            var args = receipt.GetProperty("arguments");
            if (!ArgumentsMatch(caseId, args)) return Reject("wrong-argument-tuple", cases);
            var expectedMethod = int.Parse(caseId[1..]) <= 15
                ? "C448_V10_EachAcknowledgedBoundaryRechecksSource"
                : "C448_V11_TargetMutationAfterFastForwardCannotBeAcknowledged";
            if (receipt.GetProperty("method").GetString() != expectedMethod
                || receipt.GetProperty("fixtureFailureLedger").GetArrayLength() != 0)
                return Reject("wrong-method-or-ledger", cases);
            var phases = receipt.GetProperty("phases").EnumerateArray().ToArray();
            foreach (var phase in new[] { "case", "setup", "service", "capture", "observer",
                         "db-clone", "db-drop", "provider-disposal", "fixture-disposal" })
            {
                var entered = phases.Count(p => p.GetProperty("phase").GetString() == phase
                    && p.GetProperty("disposition").GetString() == "entered");
                var exited = phases.Count(p => p.GetProperty("phase").GetString() == phase
                    && p.GetProperty("disposition").GetString() == "exited");
                if (entered == 0 || entered != exited) return Reject("incomplete-phase:" + phase, cases);
            }
            var children = receipt.GetProperty("children").EnumerateArray().ToArray();
            if (children.Length == 0 || receipt.GetProperty("observedGitLaunches").GetInt32() != children.Length
                || !receipt.GetProperty("launchObserverPresent").GetBoolean())
                return Reject("missing-launch-census", cases);
            var ids = new HashSet<long>();
            foreach (var child in children)
            {
                var id = child.GetProperty("launchId").GetInt64();
                var a = child.GetProperty("startTicks").GetInt64();
                var b = child.GetProperty("finishTicks").GetInt64();
                if (!ids.Add(id) || a < caseStart || b > caseFinish || a >= b
                    || child.GetProperty("pid").GetInt32() <= 0
                    || child.GetProperty("argv").GetArrayLength() == 0
                    || string.IsNullOrWhiteSpace(child.GetProperty("role").GetString())
                    || string.IsNullOrWhiteSpace(child.GetProperty("cwd").GetString()))
                    return Reject("invalid-child-interval", cases);
                intervals.Add((Math.Max(start, a), Math.Min(finish, b)));
            }
            var kinds = receipt.GetProperty("observations").EnumerateArray()
                .Select(o => o.GetProperty("kind").GetString()).ToHashSet(StringComparer.Ordinal);
            if (!new[] { "before_service", "after_service", "committed_after_service", "terminal_image" }
                .All(kinds.Contains)) return Reject("missing-observation", cases);
            cases++;
        }
        intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
        long total = 0, left = -1, right = -1;
        foreach (var (a, b) in intervals)
        {
            if (left < 0) (left, right) = (a, b);
            else if (a <= right) right = Math.Max(right, b);
            else { total += right - left; (left, right) = (a, b); }
        }
        if (left >= 0) total += right - left;
        var seconds = total / (double)frequency;
        return new(true, seconds >= required, seconds >= required ? "qualified-trace" : "below-optimistic-bound",
            cases, intervals.Count, seconds, outer, required);
    }

    private static bool ArgumentsMatch(string caseId, JsonElement arguments)
    {
        string[][] first =
        [
            ["remote", "advance", "false"], ["remote", "advance", "true"], ["remote", "switch", "true"],
            ["BeforeRebaseIntent", "dirty", "false"], ["RebaseStarted", "staged", "false"],
            ["Prepared", "untracked", "false"], ["Prepared", "metadata", "false"],
            ["Verified", "switch", "false"], ["Verified", "metadata-path", "false"],
            ["TargetAdvanceStarted", "advance", "false"], ["LocalTargetAdvanced", "dirty", "false"],
            ["BeforePushIntent", "staged", "false"], ["BeforePushIntent", "metadata-target", "false"],
            ["PushStarted", "untracked", "false"], ["PushStarted", "metadata-repository", "false"],
            ["advance"], ["switch"], ["dirty"], ["staged"]
        ];
        var expected = first[int.Parse(caseId[1..]) - 1];
        var actual = arguments.EnumerateArray().Select(a => a.ValueKind == JsonValueKind.String
            ? a.GetString()! : a.GetRawText()).ToArray();
        return actual.SequenceEqual(expected, StringComparer.Ordinal);
    }
}
