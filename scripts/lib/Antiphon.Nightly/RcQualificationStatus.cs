// CARD-1500: the read-only status of one explicitly selected rc qualification. It replaced Get-RcQualificationStatus
// (scripts/lib/rc-qualification-status.ps1) and its entry scripts/rc-qualification-status.ps1, deleted 2026-11-23 (unit 7 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as `antiphon-scripts rc-qualification-status`.
//
// It reads <StateRoot>/last-run.json, <StateRoot>/run.lock (NightlyRunLock.LockPath), <CoordinationRoot>/native-run.lock
// (ReleaseGateNativeLock.LockPath) and, with a release root, <ReleaseRoot>/candidates/<CandidateId>/candidate.json, each through
// RcQualificationRecords.Read, and observes each lock's recorded owner through the host's process table. It changes no file
// and no process. The JSON names each input's outcome (never its raw body), the selected state fields with their types
// checked, both locks' relationship to this run and owner observation, the journal identity and every identity mismatch.
// observationComplete is true only for a coherent observation (a failed run included); neither it nor an absent owner grants
// release credit or cleanup authority. Exit 0 coherent, 2 incomplete or invalid input. Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The rc-qualification-status command's options.</summary>
    public sealed class RcQualificationStatusOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "rc-qualification-status --candidate-id <id> --run-id <id> --state-root <dir> --coordination-root <dir> [--release-root <dir>]";

        private static readonly string[] Valued = { "--candidate-id", "--run-id", "--state-root", "--coordination-root", "--release-root" };

        /// <summary>The candidate id (the journal's directory name).</summary>
        public string CandidateId { get; init; } = "";

        /// <summary>The run id the state and locks must name.</summary>
        public string RunId { get; init; } = "";

        /// <summary>The run's state root (last-run.json, run.lock).</summary>
        public string StateRoot { get; init; } = "";

        /// <summary>The coordination root (native-run.lock).</summary>
        public string CoordinationRoot { get; init; } = "";

        /// <summary>The release root (candidates/&lt;id&gt;/candidate.json); empty: no journal is read.</summary>
        public string ReleaseRoot { get; init; } = "";

        /// <summary>
        /// The arguments after the command name. A flag left out is empty: the status, not the parser, reports an invalid
        /// selection (reason invalid-input, exit 2), as the script did.
        /// </summary>
        public static RcQualificationStatusOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Array.Empty<string>());
            return new RcQualificationStatusOptions
            {
                CandidateId = flags.Value("--candidate-id", ""),
                RunId = flags.Value("--run-id", ""),
                StateRoot = flags.Value("--state-root", ""),
                CoordinationRoot = flags.Value("--coordination-root", ""),
                ReleaseRoot = flags.Value("--release-root", ""),
            };
        }
    }

    /// <summary>The read-only rc qualification status (Get-RcQualificationStatus).</summary>
    public static class RcQualificationStatus
    {
        /// <summary>A candidate or run id: a letter or digit, then up to 127 letters, digits, dots, underscores or hyphens.</summary>
        public static readonly Regex IdPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant);

        private static readonly Regex Sha = new Regex("^[a-fA-F0-9]{40}$", RegexOptions.CultureInvariant);

        private static readonly string[] StateFields =
        {
            "runId", "candidateId", "profile", "ref", "sha", "expectedSha", "phase", "startedAt", "lastActivityAt", "exitCode",
            "testExit", "reportExit", "coverageComplete", "testsPassed", "reportDelivered", "succeeded",
        };

        private static readonly string[] LockFields = { "pid", "processStartedAt", "runId", "lane", "continuationId", "acquiredAt", "startedAt" };

        /// <summary>The command: the status JSON, one compact line on host.Out. Exit 0 a coherent observation, 2 otherwise.</summary>
        public static int Run(RcQualificationStatusOptions options, NightlyHost host, IFileSystem files)
        {
            var status = Observe(options, host, files);
            host.Out.WriteLine(RcQualificationRecords.Compact(status));
            return status["observationComplete"]!.GetValue<bool>() ? 0 : 2;
        }

        /// <summary>
        /// Get-RcQualificationStatus: the status object. afterRead (a test seam) runs after each record's bytes are read,
        /// before its stamp is taken again.
        /// </summary>
        public static JsonObject Observe(RcQualificationStatusOptions options, NightlyHost host, IFileSystem files, Action<string> afterRead = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var inputs = new JsonObject();
            var locks = new JsonObject();
            var mismatches = new JsonArray();
            var result = new JsonObject
            {
                ["candidateId"] = options.CandidateId,
                ["runId"] = options.RunId,
                ["stateRoot"] = options.StateRoot,
                ["coordinationRoot"] = options.CoordinationRoot,
                ["releaseRoot"] = options.ReleaseRoot,
                ["observedAtUtc"] = host.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ["observationComplete"] = false,
                ["reason"] = null,
                ["inputs"] = inputs,
                ["state"] = null,
                ["locks"] = locks,
                ["journal"] = null,
                ["identityMismatches"] = mismatches,
            };
            if (!IdPattern.IsMatch(options.CandidateId ?? "") || !IdPattern.IsMatch(options.RunId ?? "")
                || string.IsNullOrWhiteSpace(options.StateRoot) || string.IsNullOrWhiteSpace(options.CoordinationRoot))
            {
                result["reason"] = "invalid-input";
                return result;
            }
            var reads = new List<KeyValuePair<string, RcQualificationRead>>();
            try
            {
                var paths = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("state", files.Path.Combine(options.StateRoot, "last-run.json")),
                    new KeyValuePair<string, string>("runLock", NightlyRunLock.LockPath(options.StateRoot)),
                    new KeyValuePair<string, string>("nativeLock", files.Path.Combine(options.CoordinationRoot, "native-run.lock")),
                };
                if (!string.IsNullOrWhiteSpace(options.ReleaseRoot))
                    paths.Add(new KeyValuePair<string, string>("journal", files.Path.Combine(options.ReleaseRoot, "candidates", options.CandidateId, "candidate.json")));
                foreach (var pair in paths)
                    reads.Add(new KeyValuePair<string, RcQualificationRead>(pair.Key, RcQualificationRecords.Read(files, files.Path.GetFullPath(pair.Value), afterRead)));
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is System.IO.PathTooLongException)
            {
                result["reason"] = "invalid-input";
                return result;
            }
            var byName = new Dictionary<string, RcQualificationRead>(StringComparer.Ordinal);
            foreach (var pair in reads) byName[pair.Key] = pair.Value;
            if (!byName.ContainsKey("journal")) byName["journal"] = RcQualificationRecords.NotRequested();
            var complete = byName["state"].Ok;
            foreach (var pair in reads)
                if (pair.Value.Status != "ok" && pair.Value.Status != "missing") complete = false;
            result["journal"] = RcQualificationRecords.Select(byName["journal"].Record, "candidateId", "ref", "sha");
            var state = byName["state"].Record;
            var selected = RcQualificationRecords.Select(state, StateFields);
            result["state"] = selected;
            if (state != null)
            {
                foreach (var (name, expected) in new[] { ("runId", options.RunId), ("candidateId", options.CandidateId), ("profile", "rc") })
                {
                    var value = RcQualificationRecords.Member(state, name);
                    if (!RcQualificationRecords.IsString(value) || value.Value.GetString() != expected) mismatches.Add("state-" + name);
                }
                var sha = RcQualificationRecords.Member(state, "sha");
                var expectedSha = RcQualificationRecords.Member(state, "expectedSha");
                var effective = string.IsNullOrWhiteSpace(RcQualificationRecords.Text(sha)) ? expectedSha : sha;
                if (!RcQualificationRecords.IsString(effective) || !Sha.IsMatch(effective.Value.GetString())) mismatches.Add("state-sha");
                if (RcQualificationRecords.Truthy(sha) && RcQualificationRecords.Truthy(expectedSha) && !SameValue(sha, expectedSha)) mismatches.Add("state-expectedSha");
                foreach (var name in new[] { "coverageComplete", "testsPassed", "reportDelivered", "succeeded" })
                {
                    var value = RcQualificationRecords.Member(state, name);
                    if (RcQualificationRecords.Present(value) && !RcQualificationRecords.IsBool(value))
                    {
                        mismatches.Add("state-type-" + name);
                        selected[name] = null;
                    }
                }
                foreach (var name in new[] { "exitCode", "testExit", "reportExit" })
                {
                    var value = RcQualificationRecords.Member(state, name);
                    if (RcQualificationRecords.Present(value) && !RcQualificationRecords.IsInteger(value))
                    {
                        mismatches.Add("state-type-" + name);
                        selected[name] = null;
                    }
                }
                var journal = byName["journal"].Record;
                if (journal != null)
                {
                    var stateRef = RcQualificationRecords.Member(state, "ref");
                    foreach (var (name, expected) in new[] { ("candidateId", (JsonElement?)JsonSerializer.SerializeToElement(options.CandidateId)), ("ref", stateRef), ("sha", effective) })
                    {
                        var value = RcQualificationRecords.Member(journal, name);
                        if (!RcQualificationRecords.IsString(value) || !RcQualificationRecords.IsString(expected) || value.Value.GetString() != expected.Value.GetString())
                            mismatches.Add("journal-" + name);
                    }
                }
            }
            var tolerance = RcQualificationOwner.ToleranceMilliseconds(host.Probe.Platform());
            foreach (var (name, input) in new[] { ("run", "runLock"), ("native", "nativeLock") })
            {
                var record = byName[input].Record;
                var relationship = "unavailable";
                var observation = new RcQualificationObservation("unknown", "no-record");
                if (record != null)
                {
                    relationship = "this-run";
                    var lockRun = RcQualificationRecords.Member(record, "runId");
                    if (!RcQualificationRecords.IsString(lockRun) || lockRun.Value.GetString() != options.RunId)
                    {
                        relationship = "other-run";
                        if (name == "run") mismatches.Add("run-lock-runId");
                    }
                    observation = RcQualificationOwner.Observe(record, host.Processes, tolerance);
                    if (observation.Status == "unknown") complete = false;
                }
                locks[name] = new JsonObject
                {
                    ["relationship"] = relationship,
                    ["ownerObservation"] = observation.Json(),
                    ["record"] = RcQualificationRecords.Select(record, LockFields),
                };
            }
            // Raw input objects never leave the reader; only each source's read outcome.
            foreach (var name in new[] { "state", "runLock", "nativeLock", "journal" }) inputs[name] = byName[name].Outcome();
            var coherent = complete && mismatches.Count == 0;
            result["observationComplete"] = coherent;
            if (!coherent) result["reason"] = "incomplete-observation";
            return result;
        }

        // -cne of two record values: both strings compare ordinally, anything else by its text.
        private static bool SameValue(JsonElement? left, JsonElement? right) =>
            RcQualificationRecords.IsString(left) && RcQualificationRecords.IsString(right)
                ? left.Value.GetString() == right.Value.GetString()
                : RcQualificationRecords.Text(left) == RcQualificationRecords.Text(right);
    }
}
