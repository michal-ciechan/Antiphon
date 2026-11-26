// CARD-1545 diagnostic-only typed outcomes, ported from scripts/lib/live-lane-outcome.ps1 (W2 slice S21; the script is
// deleted). Raw screens, exception text and prompt bodies are inputs only; results hold closed enums and correlation
// booleans. Terminal evidence and its rows are JSON (System.Text.Json.Nodes) read with the hashtable semantics the
// PowerShell had (LiveLaneValues). Plain C#, framework only. ASCII only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Antiphon.LiveLane
{
    /// <summary>A typed verdict: the exit code, its reason and the classification of the code.</summary>
    public sealed class LiveLaneOutcomeVerdict
    {
        public LiveLaneOutcomeVerdict(int exitCode, string reason)
        {
            ExitCode = exitCode;
            Reason = reason ?? "";
        }

        public int ExitCode { get; }
        public string Reason { get; }
        public string Classification => LiveLaneOutcome.Classification(ExitCode);
    }

    /// <summary>Who a probe or an outage proof is about: a row's uid (none for the run's preflight), account, endpoint and dependency.</summary>
    public sealed class LiveLaneProbeIdentity
    {
        public LiveLaneProbeIdentity(string uid, string account, string endpoint, string dependency)
        {
            Uid = uid;
            Account = account;
            Endpoint = endpoint;
            Dependency = dependency;
        }

        public string Uid { get; }
        public string Account { get; }
        public string Endpoint { get; }
        public string Dependency { get; }

        /// <summary>A terminal row's identity fields as their comparison text.</summary>
        public static LiveLaneProbeIdentity Of(JsonNode row) => new LiveLaneProbeIdentity(
            LiveLaneValues.Text(LiveLaneValues.Field(row, "Uid")), LiveLaneValues.Text(LiveLaneValues.Field(row, "Account")),
            LiveLaneValues.Text(LiveLaneValues.Field(row, "Endpoint")), LiveLaneValues.Text(LiveLaneValues.Field(row, "Dependency")));
    }

    /// <summary>The availability request a probe adapter gets: never a prompt (PromptCount 0, Operation Availability).</summary>
    public sealed class LiveLaneProbeRequest
    {
        public string Operation { get; set; } = "Availability";
        public int PromptCount { get; set; }
        public double Deadline { get; set; }
        public string Uid { get; set; }
        public string Account { get; set; }
        public string Endpoint { get; set; }
        public string Dependency { get; set; }
    }

    /// <summary>What a probe adapter answered. A null answer is no observation (a timeout).</summary>
    public sealed class LiveLaneProbeReply
    {
        public string Status { get; set; }
        public string Error { get; set; }
        public string Account { get; set; }
        public string Endpoint { get; set; }
        public string Dependency { get; set; }
    }

    /// <summary>One probe (Invoke-LiveLaneProbe): the adapter's answer, bounded against its deadline on the lane clock.</summary>
    public sealed class LiveLaneProbe
    {
        public string Uid { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
        public string Account { get; set; }
        public string Endpoint { get; set; }
        public string Dependency { get; set; }
        public bool Bounded { get; set; }
        public double Started { get; set; }
        public double Finished { get; set; }
        public string Phase { get; set; }
    }

    /// <summary>A probe as a result records it (ConvertTo-LiveLaneProbeObservation): a closed status, a typed error only.</summary>
    public sealed class LiveLaneProbeObservation
    {
        public string Status { get; set; }
        public string ProviderError { get; set; }
        public string Phase { get; set; }
        public double Started { get; set; }
        public double Finished { get; set; }
        public bool Bounded { get; set; }
    }

    /// <summary>One classified terminal row.</summary>
    public sealed class LiveLaneOutcomeRow
    {
        public string Uid { get; set; }
        public string State { get; set; }
        public string Setup { get; set; }
        public string Proof { get; set; }
        public int ExitCode { get; set; }
        public string Reason { get; set; }
        public string ProviderError { get; set; }
        public string Phase { get; set; }
        public bool OutageCorrelated { get; set; }
    }

    /// <summary>Get-LiveLaneOutcome's answer.</summary>
    public sealed class LiveLaneOutcomeResult
    {
        public int ExitCode { get; set; }
        public string Reason { get; set; }
        public string Classification { get; set; }
        public IReadOnlyList<LiveLaneOutcomeRow> Rows { get; set; } = Array.Empty<LiveLaneOutcomeRow>();
        public string RawChildOutcome { get; set; }
    }

    /// <summary>The preflight's verdict and every probe it made.</summary>
    public sealed class LiveLanePreflight
    {
        public LiveLaneOutcomeVerdict Verdict { get; set; }
        public IReadOnlyList<LiveLaneProbe> Probes { get; set; } = Array.Empty<LiveLaneProbe>();
    }

    /// <summary>The classifier: the codes, the setup statuses, the outage proofs and the terminal verdict.</summary>
    public static class LiveLaneOutcome
    {
        private static readonly string[] OutageTypes = { "Overloaded", "ServiceUnavailable", "RateLimited" };
        private static readonly string[] ProbeStatuses =
        {
            "Ready", "Unavailable", "Timeout", "AuthMissing", "AuthExpired", "QuotaExceeded", "DependencyUnavailable",
            "StartupDefaultAbsent", "ReadyTimeout", "TrustNotCleared", "BridgeNotEstablished"
        };
        private static readonly string[] SetupStatuses =
        {
            "Ready", "AuthMissing", "AuthExpired", "QuotaExceeded", "DependencyUnavailable", "StartupDefaultAbsent",
            "ReadyTimeout", "TrustNotCleared", "BridgeNotEstablished", "SetupContractViolation"
        };
        private static readonly string[] TerminalOutcomes = { "Passed", "Failed", "Invalid" };
        private static readonly string[] RowStates = { "Passed", "Failed", "Skipped" };

        /// <summary>Get-LiveLaneClassification.</summary>
        public static string Classification(int code)
        {
            switch (code)
            {
                case 0: return "GREEN";
                case 1: return "RED";
                case 2: return "INVALID";
                case 4: return "SLOT-TIMEOUT";
                case 21: return "DEFERRED-BUSY";
                default: return "INCONCLUSIVE";
            }
        }

        /// <summary>New-LiveLaneOutcomeVerdict.</summary>
        public static LiveLaneOutcomeVerdict Verdict(int code, string reason) => new LiveLaneOutcomeVerdict(code, reason);

        /// <summary>Test-LiveLaneOutageType: one of the typed provider outages.</summary>
        public static bool IsOutageType(string errorType) => errorType != null && Array.IndexOf(OutageTypes, errorType) >= 0;

        private static bool In(string value, string[] set) => value != null && Array.IndexOf(set, value) >= 0;

        /// <summary>
        /// Test-LiveLaneTranscriptOutage: the row's own whole prompt was submitted (a UserPrompt with the row's body,
        /// session and request) and a typed ApiError of the same session and request follows it.
        /// </summary>
        public static bool TranscriptOutage(JsonNode row, out string providerError)
        {
            providerError = null;
            var prompt = LiveLaneValues.Text(LiveLaneValues.Field(row, "Prompt"));
            var session = LiveLaneValues.Text(LiveLaneValues.Field(row, "Session"));
            var request = LiveLaneValues.Text(LiveLaneValues.Field(row, "Request"));
            if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(request)) return false;
            var transcript = LiveLaneValues.Items(LiveLaneValues.Field(row, "Transcript"));
            foreach (var submitted in transcript)
            {
                if (!(submitted is JsonObject)
                    || !LiveLaneValues.Same(LiveLaneValues.Text(LiveLaneValues.Field(submitted, "Type")), "UserPrompt")
                    || !LiveLaneValues.Same(LiveLaneValues.Text(LiveLaneValues.Field(submitted, "Body")), prompt)
                    || !LiveLaneValues.Same(LiveLaneValues.Text(LiveLaneValues.Field(submitted, "Session")), session)
                    || !LiveLaneValues.Same(LiveLaneValues.Text(LiveLaneValues.Field(submitted, "Request")), request)
                    || !LiveLaneValues.IsInteger(LiveLaneValues.Field(submitted, "Sequence"), out var promptSequence)) continue;
                foreach (var error in transcript)
                {
                    if (error is JsonObject
                        && LiveLaneValues.Same(LiveLaneValues.Text(LiveLaneValues.Field(error, "Type")), "ApiError")
                        && LiveLaneValues.Same(LiveLaneValues.Text(LiveLaneValues.Field(error, "Session")), session)
                        && LiveLaneValues.Same(LiveLaneValues.Text(LiveLaneValues.Field(error, "Request")), request)
                        && LiveLaneValues.IsInteger(LiveLaneValues.Field(error, "Sequence"), out var errorSequence)
                        && errorSequence > promptSequence)
                    {
                        var type = LiveLaneValues.AsString(LiveLaneValues.Field(error, "Error"));
                        if (!IsOutageType(type)) continue;
                        providerError = type;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Test-LiveLaneProbeOutage: a bounded probe saw a typed outage for exactly this identity (uid, account,
        /// endpoint and dependency, the last three present).
        /// </summary>
        public static bool ProbeOutage(LiveLaneProbeIdentity identity, LiveLaneProbe probe) =>
            probe != null && identity != null && (probe.Status == "Unavailable" || probe.Status == "DependencyUnavailable")
            && IsOutageType(probe.Error) && probe.Bounded
            && LiveLaneValues.Same(probe.Uid, identity.Uid)
            && LiveLaneValues.Same(probe.Account, identity.Account) && LiveLaneValues.Same(probe.Endpoint, identity.Endpoint)
            && LiveLaneValues.Same(probe.Dependency, identity.Dependency)
            && !string.IsNullOrWhiteSpace(identity.Account) && !string.IsNullOrWhiteSpace(identity.Endpoint)
            && !string.IsNullOrWhiteSpace(identity.Dependency);

        /// <summary>Get-LiveLaneSetupOutcome: a typed setup status as a verdict.</summary>
        public static LiveLaneOutcomeVerdict SetupOutcome(string status, bool healthy = false, bool deterministic = false, bool outage = false)
        {
            switch (status)
            {
                case "Ready": return Verdict(0, "ready");
                case "AuthMissing":
                case "AuthExpired": return Verdict(20, "auth-required");
                case "QuotaExceeded": return Verdict(20, "quota-exhausted");
                case "DependencyUnavailable": return outage ? Verdict(20, "provider-outage") : Verdict(20, "setup-unclassified");
                case "StartupDefaultAbsent": return Verdict(20, "premise-absent");
                case "ReadyTimeout":
                case "TrustNotCleared":
                case "BridgeNotEstablished": return Verdict(20, "setup-unclassified");
                case "SetupContractViolation":
                    return healthy && deterministic ? Verdict(1, "setup-contract-violation") : Verdict(20, "setup-unclassified");
                default: return Verdict(20, "unclassified-skip");
            }
        }

        /// <summary>
        /// Invoke-LiveLaneProbe: one availability probe for the identity, timed on the lane clock. A late or missing
        /// observation cannot attribute an outage, so it reads as Timeout. Adapters are fixture-only here; a future native
        /// transport must separately qualify actual cancellation.
        /// </summary>
        public static LiveLaneProbe Probe(LiveLaneBoundary boundary, string purpose, LiveLaneProbeIdentity identity, double deadline)
        {
            var start = boundary.Clock();
            var reply = boundary.Probe(purpose, new LiveLaneProbeRequest
            {
                Operation = "Availability", PromptCount = 0, Deadline = deadline,
                Uid = identity.Uid, Account = identity.Account, Endpoint = identity.Endpoint, Dependency = identity.Dependency
            });
            var finish = boundary.Clock();
            var bounded = finish <= deadline && finish >= start;
            if (reply == null || !bounded) reply = new LiveLaneProbeReply { Status = "Timeout", Error = "", Account = "", Endpoint = "", Dependency = "" };
            return new LiveLaneProbe
            {
                Uid = identity.Uid, Status = reply.Status, Error = reply.Error, Account = reply.Account, Endpoint = reply.Endpoint,
                Dependency = reply.Dependency, Bounded = bounded, Started = start, Finished = finish, Phase = purpose
            };
        }

        /// <summary>
        /// Invoke-LiveLanePreflight: at most two probes inside min(run deadline, start + 60), each allowed 30 s. Quota and
        /// an explicit auth refusal never enter the availability retry.
        /// </summary>
        public static LiveLanePreflight Preflight(LiveLaneBoundary boundary, LiveLaneRequest request, double runDeadline)
        {
            var start = boundary.Clock();
            var limit = Math.Min(runDeadline, start + 60);
            var probes = new List<LiveLaneProbe>();
            LiveLaneProbe probe = null;
            var identity = request.Identity();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var now = boundary.Clock();
                if (now >= limit) break;
                var allowance = Math.Min(limit, now + 30);
                probe = Probe(boundary, "Preflight", identity, allowance);
                probes.Add(probe);
                if (probe.Status != "Unavailable" && probe.Status != "Timeout") break;
            }
            LiveLaneOutcomeVerdict verdict;
            if (probe == null) verdict = Verdict(20, "preflight-incomplete");
            else if (probe.Status == "Unavailable") verdict = SetupOutcome("DependencyUnavailable", outage: ProbeOutage(identity, probe));
            else verdict = SetupOutcome(probe.Status, outage: ProbeOutage(identity, probe));
            return new LiveLanePreflight { Verdict = verdict, Probes = probes };
        }

        /// <summary>ConvertTo-LiveLaneProbeObservation.</summary>
        public static LiveLaneProbeObservation Observation(LiveLaneProbe probe)
        {
            var status = In(probe.Status, ProbeStatuses) ? probe.Status : "Unknown";
            string providerError = null;
            if ((status == "Unavailable" || status == "DependencyUnavailable") && IsOutageType(probe.Error)) providerError = probe.Error;
            return new LiveLaneProbeObservation
            {
                Status = status, ProviderError = providerError, Phase = probe.Phase, Started = probe.Started, Finished = probe.Finished,
                Bounded = probe.Bounded
            };
        }

        /// <summary>
        /// Get-LiveLaneOutcome: the terminal evidence of one checkpoint run, row by row, against the request. Independent
        /// invalid and red facts outrank an unrelated outage or missing evidence; only an exact, complete, drift-free,
        /// all-passed roster with a passing child is GREEN.
        /// </summary>
        public static LiveLaneOutcomeResult Evaluate(LiveLaneRequest request, JsonNode terminal, string childRunId, int rawChildExit, LiveLaneProbe postProbe)
        {
            var rows = new List<LiveLaneOutcomeRow>();
            var verdict = Verdict(20, "evidence-incomplete");
            string rawOutcome = null;
            var uids = request.Uids ?? Array.Empty<string>();
            if (terminal is JsonObject)
            {
                string Text(string name) => LiveLaneValues.Text(LiveLaneValues.Field(terminal, name));
                var invalid = "";
                var outcome = Text("Outcome");
                if (!(LiveLaneValues.IsInteger(LiveLaneValues.Field(terminal, "SchemaVersion"), out var schema) && schema == 1)
                    || !LiveLaneValues.Same(Text("RunId"), childRunId) || !LiveLaneValues.Same(Text("Lane"), request.Lane)
                    || !(LiveLaneValues.IsInteger(LiveLaneValues.Field(terminal, "Attempt"), out var attempt) && attempt == request.Attempt)
                    || !LiveLaneValues.Same(Text("Source"), request.Source) || Text("Build") != "Succeeded"
                    || Text("Platform") != "Fixture" || Text("Authority") != "diagnostic"
                    || !LiveLaneValues.IsBool(LiveLaneValues.Field(terminal, "Complete"), out _) || !In(outcome, TerminalOutcomes))
                    invalid = "terminal-invalid";
                if (In(outcome, TerminalOutcomes)) rawOutcome = outcome;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var exact = uids.Count > 0;
                var rowsNode = LiveLaneValues.Field(terminal, "Rows");
                var terminalRows = rowsNode == null ? Array.Empty<JsonNode>() : LiveLaneValues.Items(rowsNode);
                foreach (var row in terminalRows)
                {
                    string uid = null, state = "Invalid", setup = "Unclassified", proof = "None", providerError = null, phase = "Terminal";
                    LiveLaneOutcomeVerdict observation;
                    var rowUid = LiveLaneValues.AsString(LiveLaneValues.Field(row, "Uid"));
                    var rowState = LiveLaneValues.Text(LiveLaneValues.Field(row, "State"));
                    if (!(row is JsonObject) || rowUid == null || string.IsNullOrWhiteSpace(rowUid) || !In(rowState, RowStates))
                    {
                        observation = Verdict(2, "row-invalid");
                        invalid = "terminal-invalid";
                    }
                    else
                    {
                        state = rowState;
                        if (!seen.Add(rowUid)) exact = false;
                        if (uids.Contains(rowUid, StringComparer.Ordinal)) uid = rowUid; else exact = false;
                        var transcriptOutage = TranscriptOutage(row, out var transcriptError);
                        var probeOutage = ProbeOutage(LiveLaneProbeIdentity.Of(row), postProbe);
                        if (transcriptOutage) { proof = "Transcript"; phase = "PostSubmission"; providerError = transcriptError; }
                        else if (probeOutage) { proof = "Probe"; phase = "PostFailure"; providerError = postProbe.Error; }
                        // A case-sensitive external reader's dictionary must carry the exact key Setup.
                        var rowSetup = LiveLaneValues.HasExactKey(row, "Setup") ? LiveLaneValues.AsString(row["Setup"]) : null;
                        if (In(rowSetup, SetupStatuses)) setup = rowSetup;
                        if (setup == "Unclassified")
                        {
                            observation = Verdict(2, "row-invalid");
                            invalid = "row-invalid";
                        }
                        else if (rowState == "Failed" && proof == "None")
                        {
                            // An executed failure needs this row's outage proof before any setup refusal can turn its
                            // assertion into INCONCLUSIVE.
                            observation = Verdict(1, "assertion-failed");
                        }
                        else if (setup != "Ready")
                        {
                            var healthy = LiveLaneValues.IsBool(LiveLaneValues.Field(row, "Healthy"), out var h) && h;
                            var deterministic = LiveLaneValues.IsBool(LiveLaneValues.Field(row, "Deterministic"), out var d) && d;
                            observation = SetupOutcome(setup, healthy, deterministic, proof != "None");
                        }
                        else if (proof != "None") observation = Verdict(20, "provider-outage");
                        else if (rowState == "Passed") observation = Verdict(0, "all-passed");
                        else if (rowState == "Skipped") observation = Verdict(20, "unclassified-skip");
                        else observation = Verdict(1, "assertion-failed");
                    }
                    rows.Add(new LiveLaneOutcomeRow
                    {
                        Uid = uid, State = state, Setup = setup, Proof = proof, ExitCode = observation.ExitCode, Reason = observation.Reason,
                        ProviderError = providerError, Phase = phase, OutageCorrelated = proof != "None"
                    });
                }
                if (seen.Count != uids.Count || terminalRows.Count != uids.Count) exact = false;
                foreach (var expected in uids) if (!seen.Contains(expected)) exact = false;
                if (!exact && invalid.Length == 0) invalid = "roster-incomplete";
                var red = rows.FirstOrDefault(r => r.ExitCode == 1);
                var inconclusive = rows.FirstOrDefault(r => r.ExitCode == 20);
                LiveLaneValues.IsBool(LiveLaneValues.Field(terminal, "Complete"), out var complete);
                if (invalid.Length > 0) verdict = Verdict(2, invalid);
                else if (red != null) verdict = Verdict(1, red.Reason);
                else if (!complete || Text("Evidence") != "Complete") verdict = Verdict(20, "evidence-incomplete");
                else if (string.IsNullOrWhiteSpace(Text("InputBefore")) || string.IsNullOrWhiteSpace(Text("ExecutableBefore"))
                    || !LiveLaneValues.Same(Text("InputBefore"), Text("InputAfter")) || !LiveLaneValues.Same(Text("ExecutableBefore"), Text("ExecutableAfter")))
                    verdict = Verdict(20, "input-drift");
                else if (inconclusive != null) verdict = Verdict(20, inconclusive.Reason);
                else if (rawChildExit != 0 || rawOutcome != "Passed") verdict = Verdict(2, "checkpoint-inconsistent");
                else verdict = Verdict(0, "all-passed");
            }
            return new LiveLaneOutcomeResult
            {
                ExitCode = verdict.ExitCode, Reason = verdict.Reason, Classification = verdict.Classification, Rows = rows, RawChildOutcome = rawOutcome
            };
        }
    }
}
