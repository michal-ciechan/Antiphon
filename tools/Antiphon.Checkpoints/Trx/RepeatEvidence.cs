using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

public sealed class RepeatEvidence
{
    public int Requested { get; set; }
    public int Completed { get; set; }
    public int Passed { get; set; }
    public int HostInvocations { get; set; }
    public int HostPid { get; set; }
    public string Nonce { get; set; } = "";
    public List<RepeatOrdinal> Repetitions { get; set; } = [];
}

public sealed class RepeatOrdinal
{
    public int Ordinal { get; set; }
    public int Executed { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<RepeatCase> Cases { get; set; } = [];
}

public sealed class RepeatCase
{
    public string CaseKey { get; set; } = "";
    public string NativeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Outcome { get; set; } = "";
}

public sealed record RepeatCheck(RepeatEvidence Evidence, string? Error, int ExitCode)
{
    public bool Ok => Error is null;
}

public sealed record RepeatMarker(int Version, int Requested, string Nonce, string NativeId,
    int Ordinal, string CaseKey, string Class, string Method, int HostPid);

public static class RepeatEvidenceValidator
{
    private static readonly Regex NativeTail = new(@"^(?<key>.+\.\d+\.\d+)\.(?<ordinal>\d+)(?<inherit>_inherited[1-9]\d*)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static RepeatCheck Validate(TrxParseResult trx, int requested, string nonce,
        int minExecuted, IReadOnlyList<string> expect)
    {
        var evidence = new RepeatEvidence { Requested = requested, HostInvocations = 1, Nonce = nonce };
        RepeatCheck Bad(string reason, int exit = ExitCodes.Invalid) => new(evidence, reason, exit);
        if (!trx.Ok || requested < 2 || string.IsNullOrWhiteSpace(nonce) || trx.Results.Count == 0)
            return Bad("repeat_trx_or_request_missing");
        var executionIds = new HashSet<string>(StringComparer.Ordinal);
        var nativeIds = new HashSet<string>(StringComparer.Ordinal);
        var perOrdinal = Enumerable.Range(0, requested).Select(index => new RepeatOrdinal { Ordinal = index }).ToArray();
        foreach (var result in trx.Results)
        {
            if (result.ExecutionId.Length == 0 || !executionIds.Add(result.ExecutionId))
                return Bad("duplicate_execution_id");
            if (result.Outcome is not ("Passed" or "Failed" or "Error" or "Timeout" or "Aborted"))
                return Bad("repeat_skipped_or_unknown_outcome");
            var starts = MarkerLines(result.StdOut, "C885_REPEAT_START ");
            var ends = MarkerLines(result.StdOut, "C885_REPEAT_END ");
            if (starts.Count != 1 || ends.Count != 1 || result.StdOut.Contains("C885_REPEAT_INVALID", StringComparison.Ordinal))
                return Bad("repeat_marker_pair_missing_or_duplicate");
            RepeatMarker? start;
            RepeatMarker? end;
            try
            {
                start = JsonSerializer.Deserialize<RepeatMarker>(starts[0]);
                end = JsonSerializer.Deserialize<RepeatMarker>(ends[0]);
            }
            catch (JsonException) { return Bad("repeat_marker_malformed"); }
            if (start is null || end is null || start != end || start.Version != 1
                || start.Requested != requested || start.Nonce != nonce || start.HostPid <= 0
                || start.Ordinal < 0 || start.Ordinal >= requested)
                return Bad("repeat_marker_identity_mismatch");
            var native = NativeTail.Match(start.NativeId);
            if (!native.Success || !int.TryParse(native.Groups["ordinal"].Value, out var nativeOrdinal)
                || nativeOrdinal != start.Ordinal
                || native.Groups["key"].Value + native.Groups["inherit"].Value != start.CaseKey
                || !nativeIds.Add(start.NativeId))
                return Bad("repeat_native_identity_invalid");
            if (evidence.HostPid == 0) evidence.HostPid = start.HostPid;
            else if (evidence.HostPid != start.HostPid) return Bad("repeat_multiple_hosts");
            if (start.Class.Length == 0 || start.Method.Length == 0
                || !result.Name.Contains(start.Method, StringComparison.Ordinal))
                return Bad("repeat_name_mismatch");
            var ordinal = perOrdinal[start.Ordinal];
            ordinal.Cases.Add(new RepeatCase
            {
                CaseKey = start.CaseKey, NativeId = start.NativeId, Name = result.Name, Outcome = result.Outcome,
            });
            ordinal.Executed++;
            if (result.Outcome == "Passed") ordinal.Passed++;
            else ordinal.Failed++;
        }

        var roster = perOrdinal[0].Cases.Select(item => item.CaseKey).ToHashSet(StringComparer.Ordinal);
        if (roster.Count == 0) return Bad("repeat_empty_roster", ExitCodes.RosterOrMin);
        foreach (var ordinal in perOrdinal)
        {
            if (ordinal.Cases.Select(item => item.CaseKey).Distinct(StringComparer.Ordinal).Count() != ordinal.Cases.Count
                || !ordinal.Cases.Select(item => item.CaseKey).ToHashSet(StringComparer.Ordinal).SetEquals(roster))
                return Bad("repeat_roster_mismatch", ExitCodes.RosterOrMin);
            if (ordinal.Executed < minExecuted)
                return Bad("repeat_min_executed", ExitCodes.RosterOrMin);
            if (expect.Any(token => !ordinal.Cases.Any(item => item.Name.Contains(token, StringComparison.OrdinalIgnoreCase))))
                return Bad("repeat_expect_miss", ExitCodes.RosterOrMin);
        }

        evidence.Repetitions = perOrdinal.ToList();
        evidence.Completed = requested;
        evidence.Passed = perOrdinal.Count(item => item.Failed == 0 && item.Skipped == 0);
        var executed = perOrdinal.Sum(item => item.Executed);
        var passed = perOrdinal.Sum(item => item.Passed);
        var failed = perOrdinal.Sum(item => item.Failed);
        if (trx.Executed != executed || trx.Passed != passed || trx.Failed != failed || trx.Skipped != 0
            || executed != roster.Count * requested)
            return Bad("repeat_counter_disagreement");
        return failed > 0 ? Bad("repeat_failed", ExitCodes.FailedTests) : new RepeatCheck(evidence, null, ExitCodes.Green);
    }

    private static List<string> MarkerLines(string output, string prefix) => output
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.TrimEnd('\r'))
        .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
        .Select(line => line[prefix.Length..])
        .ToList();
}
