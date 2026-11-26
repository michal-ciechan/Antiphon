// CARD-1836 S6: the opt-in Incremental qualification hop (plan D-6, rollout steps 1-4). Full stays the default and the
// only active mode: an Incremental request is admitted only with a bound approval (replay verdict digest, the running
// selector hash, the candidate's trust-policy hash and two distinct prospective shadow run ids, not withdrawn), a Full
// sweep younger than seven days (UTC; an anchor exactly seven days old is expired) and a sealed v2 comparison inventory;
// anything else runs Full with the reason. Even admitted, the request is qualification-only: the cut never calls the
// publisher for it (ReleaseCut). At the tests hop (after discovery) Execute re-verifies the bound inventory digest
// (a stale plan refuses), seals the partition once (a resume reuses the sealed plan and launches only unlaunched owed
// chunks, none twice), carries only strictly reusable receipts for exactly their contents, and closes a ledger that
// reconciles to the roster. Diagnostic wiring: production NightlyTests does not call Execute yet. Plain C#, ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>The cut hop's admission answer.</summary>
    public sealed class RcIncrementalAdmission
    {
        public bool Admitted { get; set; }
        public string Reason { get; set; } = "";
        public string ApprovalDigest { get; set; } = "";
        public string BaselineSha { get; set; } = "";
        public string InventoryPath { get; set; } = "";
        public string InventoryDigest { get; set; } = "";
        public Dictionary<string, string> Owed { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public string OwedDigest { get; set; } = "";
    }

    /// <summary>The tests hop's answer: what launched, what carried (with origins), the closed ledger and its scope.</summary>
    public sealed class RcIncrementalExecution
    {
        public string Refusal { get; set; } = "";
        public string CoverageScope { get; set; } = "Full";
        public string PlanDigest { get; set; } = "";
        public List<string> Launched { get; } = new List<string>();
        public List<IDictionary> Carried { get; } = new List<IDictionary>();
        public bool Reconciled { get; set; }
        public bool AllGreen { get; set; }
        public string LedgerDigest { get; set; } = "";
    }

    public static class RcIncrementalCut
    {
        public const string ApprovalFormat = "antiphon-rc-incremental-approval-v1";
        public const string PlanFormat = "antiphon-rc-incremental-plan-v1";
        public const string RequestedMode = "Incremental";
        public static readonly TimeSpan FullSweepMaxAge = TimeSpan.FromDays(7);

        /// <summary>The selector identity an approval must name (the replay oracle's selector hash).</summary>
        public static string SelectorHash => RcSelectionReplay.SelectorHash;

        /// <summary>The directory holding approval.json, last-full-sweep.json and comparison.json under a release root.</summary>
        public static string IncrementalRoot(string releaseRoot) => Path.Combine(releaseRoot, "incremental");

        /// <summary>D-6 admission at the cut hop. Every refusal names why; the caller then runs Full.</summary>
        public static RcIncrementalAdmission Admit(string releaseRoot, string candidateSha, string candidateTrustPolicy, DateTime nowUtc, string repositoryRoot)
        {
            var dir = IncrementalRoot(releaseRoot);
            RcIncrementalAdmission No(string why) => new RcIncrementalAdmission { Reason = "incremental-not-admitted:" + why };
            var approvalPath = Path.Combine(dir, "approval.json");
            if (!File.Exists(approvalPath)) return No("approval-absent");
            var approvalBytes = File.ReadAllBytes(approvalPath);
            JsonElement approval;
            try
            {
                using var doc = JsonDocument.Parse(approvalBytes);
                approval = doc.RootElement.Clone();
            }
            catch (JsonException) { return No("approval-unreadable"); }
            if (Text(approval, "format") != ApprovalFormat) return No("approval-format");
            if (approval.TryGetProperty("withdrawn", out var withdrawn) && withdrawn.ValueKind == JsonValueKind.True) return No("approval-withdrawn");
            if (!RcChunkReceipt.IsDigest(Text(approval, "replayVerdictDigest"))) return No("approval-verdict");
            if (Text(approval, "selectorHash") != SelectorHash) return No("approval-selector-mismatch");
            if (Text(approval, "trustPolicyHash") != RcTrustedProjectGate.PolicyDigest(candidateTrustPolicy)) return No("approval-trust-mismatch");
            var shadows = approval.TryGetProperty("shadowRunIds", out var s) && s.ValueKind == JsonValueKind.Array
                ? s.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Count() : 0;
            if (shadows < 2) return No("approval-shadows");

            var sweepPath = Path.Combine(dir, "last-full-sweep.json");
            if (!File.Exists(sweepPath)) return No("full-sweep-absent");
            DateTime completed;
            try
            {
                using var sweep = JsonDocument.Parse(File.ReadAllBytes(sweepPath));
                if (!DateTime.TryParse(Text(sweep.RootElement, "completedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out completed))
                    return No("full-sweep-unreadable");
            }
            catch (JsonException) { return No("full-sweep-unreadable"); }
            if (completed > nowUtc) return No("full-sweep-future");
            if (nowUtc - completed >= FullSweepMaxAge) return No("full-sweep-expired");

            var comparisonPath = Path.Combine(dir, "comparison.json");
            if (!File.Exists(comparisonPath)) return No("comparison-absent");
            string baseline, inventoryPath, inventoryDigest, owedDigest;
            Dictionary<string, string> owed = null;
            try
            {
                using var comparison = JsonDocument.Parse(File.ReadAllBytes(comparisonPath));
                baseline = Text(comparison.RootElement, "sha");
                inventoryPath = Text(comparison.RootElement, "inventoryPath");
                inventoryDigest = Text(comparison.RootElement, "inventoryDigest");
                owedDigest = Text(comparison.RootElement, "owedDigest");
                if (comparison.RootElement.ValueKind == JsonValueKind.Object && comparison.RootElement.TryGetProperty("owed", out var owedElement)
                    && owedElement.ValueKind == JsonValueKind.Object && owedElement.EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.String))
                    owed = owedElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
            }
            catch (JsonException) { return No("comparison-unreadable"); }
            if (!RcChunkReceipt.IsSha(baseline)) return No("comparison-sha");
            // Review r1 D-3: the comparison carries the D-1 later-open obligations, bound by digest; none recorded is no binding.
            if (owed == null) return No("comparison-owed-absent");
            if (RcComparisonBaseline.OwedDigest(owed) != owedDigest) return No("comparison-owed-digest");
            var inventory = RcChunkReceipt.Read(inventoryPath, inventoryDigest);
            if (inventory.Refusal.Length > 0) return No("comparison-" + inventory.Refusal);
            // The inventory must have been sealed at the commit the comparison names, and that commit must be in the candidate's history.
            if (!string.Equals(inventory.Sha, baseline, StringComparison.Ordinal)) return No("comparison-inventory-sha");
            if (string.IsNullOrEmpty(repositoryRoot)) return No("comparison-ancestry-unknown");
            if (TestSelectionGit.Run(repositoryRoot, new[] { "merge-base", "--is-ancestor", baseline, candidateSha }, new[] { 0, 1, 128 }).Exit != 0)
                return No("comparison-not-ancestor");
            return new RcIncrementalAdmission
            {
                Admitted = true, ApprovalDigest = RcChunkReceipt.Hex(SHA256.HashData(approvalBytes)), BaselineSha = baseline, InventoryPath = inventoryPath,
                InventoryDigest = inventoryDigest, Owed = owed, OwedDigest = owedDigest,
            };
        }

        /// <summary>
        /// The tests hop for one roster (after discovery). Full (no binding) launches every roster chunk. Incremental
        /// verifies the hop binding and the bound inventory, seals or reuses the partition under attemptDirectory, launches
        /// the owed chunks not launched before, carries the rest from their origin receipts and closes the ledger.
        /// launch returns the executed row (result, exitCode, trx, ...) for a chunk.
        /// </summary>
        public static RcIncrementalExecution Execute(NightlyRunOptions run, IReadOnlyList<RcPlannedChunk> roster, string attemptDirectory, Func<RcPlannedChunk, IDictionary> launch,
            string platform = null)
        {
            var answer = new RcIncrementalExecution();
            // The qualification's own scope: a carried receipt must have run on this platform, architecture and profile.
            var scope = new RcReceiptScope { Platform = platform ?? RcReceiptScope.CurrentPlatform(), Architecture = RcReceiptScope.CurrentArchitecture(), Profile = run.Profile ?? "" };
            var requested = string.Equals(run.RequestedSelectionMode, RequestedMode, StringComparison.Ordinal);
            var bound = !string.IsNullOrEmpty(run.IncrementalInventoryDigest);
            if (bound && (!requested || !string.Equals(run.SelectionMode, "Full", StringComparison.Ordinal)))
                return Refuse(answer, "incremental-hop-mismatch");
            if (!bound)
            {
                // Full runs the invariant roster first and requires fresh case evidence for that prefix.
                var launchedRows = new List<IDictionary>();
                foreach (var chunk in roster.OrderByDescending(chunk => chunk.RequiresFreshInvariantExecution))
                {
                    var row = launch(chunk);
                    answer.Launched.Add(chunk.Id);
                    if (chunk.RequiresFreshInvariantExecution)
                    {
                        if (row == null) return Refuse(answer, "repository-invariant-missing-result:" + chunk.Id);
                        var fresh = RcChunkReceipt.FreshInvariant(row, run, chunk, scope, attemptDirectory);
                        if (!fresh.Reusable) return Refuse(answer, "repository-invariant-receipt:" + chunk.Id + ":" + string.Join(",", fresh.Reasons));
                    }
                    launchedRows.Add(row);
                }
                answer.Reconciled = true;
                answer.AllGreen = launchedRows.All(r => r != null && PsValue.Str(r["result"]) == "pass");
                return answer;
            }
            var owed = run.IncrementalOwed ?? new Dictionary<string, string>(StringComparer.Ordinal);
            if (RcComparisonBaseline.OwedDigest(owed) != run.IncrementalOwedDigest) return Refuse(answer, "incremental-hop-mismatch:owed");

            var inventory = RcChunkReceipt.Read(run.IncrementalInventoryPath, run.IncrementalInventoryDigest);
            if (inventory.Refusal.Length > 0) return Refuse(answer, "incremental-plan-stale:" + inventory.Refusal);
            if (roster.Any(chunk => chunk.RequiresFreshInvariantExecution)
                && inventory.Rows.Any(row => !RcChunkReceipt.IsDigest(PsValue.Str(row["contentDigest"]))))
                return Execute(FullFallback(run), roster, attemptDirectory, launch, platform);
            Directory.CreateDirectory(attemptDirectory);
            var planPath = Path.Combine(attemptDirectory, "incremental-plan.json");
            var digestPath = planPath + ".sha256";
            OrderedDictionary[] entries;
            if (File.Exists(planPath))
            {
                var bytes = File.ReadAllBytes(planPath);
                if (!File.Exists(digestPath) || File.ReadAllText(digestPath).Trim() != RcChunkReceipt.Hex(SHA256.HashData(bytes))) return Refuse(answer, "incremental-plan-stale:sealed-plan");
                using var doc = JsonDocument.Parse(bytes);
                var root = doc.RootElement;
                if (Text(root, "inventoryDigest") != run.IncrementalInventoryDigest || Text(root, "candidateSha") != run.ExpectedSha
                    || (roster.Any(chunk => chunk.RequiresFreshInvariantExecution) && Text(root, "invariantAttempt") != AttemptKey(run))
                    || Text(root, "rosterDigest") != RosterDigest(roster) || Text(root, "owedDigest") != run.IncrementalOwedDigest) return Refuse(answer, "incremental-plan-stale:binding");
                entries = root.GetProperty("entries").EnumerateArray().Select(e => (OrderedDictionary)RcChunkReceipt.FromJson(e)).ToArray();
                answer.PlanDigest = RcChunkReceipt.Hex(SHA256.HashData(bytes));
            }
            else
            {
                var trxRoot = Path.GetDirectoryName(Path.GetFullPath(run.IncrementalInventoryPath));
                var matches = RcChunkReceipt.Match(roster.Select(c => (c.Id, c.ContentDigest)), inventory.Rows, row =>
                {
                    var chunk = roster.FirstOrDefault(c => c.ContentDigest == PsValue.Str(row["contentDigest"]));
                    var trx = PsValue.Str(row["trx"]);
                    if (trx.Length > 0 && !Path.IsPathRooted(trx)) trx = Path.Combine(trxRoot, trx);
                    return RcChunkReceipt.Reusable(row, RcChunkReceipt.ReadTrx(trx), chunk?.Cases ?? Array.Empty<string>(), scope);
                });
                var sameSource = string.Equals(run.IncrementalBaselineSha, run.ExpectedSha, StringComparison.Ordinal);
                entries = matches.Select(m =>
                {
                    // Review r1 D-3: a carry needs the same source, an origin executed at the bound baseline (a carried-of-carried
                    // row is re-executed), and no later open obligation for its content.
                    var carry = m.Decision == "carry";
                    var reasons = carry ? new List<string>() : new List<string>(m.Reasons);
                    if (!sameSource) reasons.Add("source-changed-unverified");
                    var originSha = carry ? PsValue.Str(m.Origin["sha"]) : "";
                    if (carry && !string.Equals(originSha, run.IncrementalBaselineSha, StringComparison.Ordinal)) reasons.Add("origin-sha:" + originSha);
                    if (owed.TryGetValue(m.ContentDigest ?? "", out var open)) reasons.Add(open);
                    if (roster.Any(chunk => chunk.Id == m.Chunk && chunk.RequiresFreshInvariantExecution))
                        reasons.Add("repository-invariant-fresh-execution");
                    var carried = carry && reasons.Count == 0;
                    return new OrderedDictionary(StringComparer.Ordinal)
                    {
                        { "chunk", m.Chunk }, { "contentDigest", m.ContentDigest }, { "decision", carried ? "carry" : "execute" },
                        { "reasons", reasons.ToArray() }, { "origin", carried ? m.Origin : null },
                    };
                }).ToArray();
                var plan = new OrderedDictionary(StringComparer.Ordinal)
                {
                    { "format", PlanFormat }, { "candidateSha", run.ExpectedSha }, { "baselineSha", run.IncrementalBaselineSha }, { "approvalDigest", run.IncrementalApprovalDigest },
                    { "inventoryDigest", run.IncrementalInventoryDigest }, { "owedDigest", run.IncrementalOwedDigest }, { "rosterDigest", RosterDigest(roster) },
                    { "entries", entries.Cast<object>().ToArray() },
                };
                if (roster.Any(chunk => chunk.RequiresFreshInvariantExecution)) plan["invariantAttempt"] = AttemptKey(run);
                var bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(plan));
                using (var stream = new FileStream(planPath, FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes, 0, bytes.Length);
                answer.PlanDigest = RcChunkReceipt.Hex(SHA256.HashData(bytes));
                File.WriteAllText(digestPath, answer.PlanDigest);
            }

            // Launch the owed chunks not launched yet (a resume never launches one twice).
            var journalPath = Path.Combine(attemptDirectory, "incremental-launches.jsonl");
            var executed = new Dictionary<string, IDictionary>(StringComparer.Ordinal);
            if (File.Exists(journalPath))
                foreach (var line in File.ReadAllLines(journalPath).Where(l => l.Length > 0))
                {
                    using var doc = JsonDocument.Parse(line);
                    var row = (IDictionary)RcChunkReceipt.FromJson(doc.RootElement);
                    executed[PsValue.Str(row["chunk"])] = row;
                }
            var ledger = new List<IDictionary>();
            var byId = roster.ToDictionary(chunk => chunk.Id, StringComparer.Ordinal);
            foreach (var entry in entries.OrderByDescending(entry => byId[PsValue.Str(entry["chunk"])].RequiresFreshInvariantExecution))
            {
                var id = PsValue.Str(entry["chunk"]);
                var chunk = byId[id];
                if (PsValue.Str(entry["decision"]) == "carry")
                {
                    if (chunk.RequiresFreshInvariantExecution) return Refuse(answer, "repository-invariant-carried:" + id);
                    var carried = RcChunkReceipt.Carry((IDictionary)entry["origin"], run.ExpectedSha, run.IncrementalInventoryDigest);
                    answer.Carried.Add(carried);
                    ledger.Add(carried);
                    continue;
                }
                var resume = executed.TryGetValue(id, out var row);
                if (resume && chunk.RequiresFreshInvariantExecution
                    && !RcChunkReceipt.FreshInvariant(row, run, chunk, scope, attemptDirectory).Reusable)
                    resume = false;
                if (!resume)
                {
                    row = launch(chunk) ?? throw new NightlyRefusal("incremental-launch-no-row:" + id);
                    if (chunk.RequiresFreshInvariantExecution)
                    {
                        var fresh = RcChunkReceipt.FreshInvariant(row, run, chunk, scope, attemptDirectory);
                        if (!fresh.Reusable) return Refuse(answer, "repository-invariant-receipt:" + id + ":" + string.Join(",", fresh.Reasons));
                    }
                    else
                    {
                        row["chunk"] = id;
                        RcChunkReceipt.Stamp(row, run.ExpectedSha, chunk.ContentDigest, scope);
                    }
                    File.AppendAllText(journalPath, JsonSerializer.Serialize(Plain(row)) + "\n", new UTF8Encoding(false));
                    executed[id] = row;
                    answer.Launched.Add(id);
                }
                ledger.Add(row);
            }

            // Closure: the ledger accounts for exactly the roster.
            var accounted = ledger.Select(r => PsValue.Str(r["chunk"])).ToArray();
            answer.Reconciled = accounted.Length == roster.Count && accounted.Distinct(StringComparer.Ordinal).Count() == accounted.Length
                && roster.All(c => accounted.Contains(c.Id, StringComparer.Ordinal));
            answer.AllGreen = answer.Reconciled && ledger.All(r => PsValue.Str(r["result"]) == "pass");
            answer.LedgerDigest = RcChunkReceipt.Seal(Path.Combine(attemptDirectory, "rc-chunk-receipts.json"), run.ExpectedSha, run.Attempt, ledger);
            answer.CoverageScope = "Incremental";
            return answer;
        }

        private static string AttemptKey(NightlyRunOptions run) => run.RunId + "/" + run.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static NightlyRunOptions FullFallback(NightlyRunOptions run) => new NightlyRunOptions
        {
            RunId = run.RunId, Attempt = run.Attempt, ExpectedSha = run.ExpectedSha, Profile = run.Profile,
        };

        static RcIncrementalExecution Refuse(RcIncrementalExecution answer, string why)
        {
            answer.Refusal = why;
            answer.CoverageScope = "none";
            return answer;
        }

        /// <summary>The digest of the roster's ordered (id, content digest) pairs.</summary>
        public static string RosterDigest(IReadOnlyList<RcPlannedChunk> roster)
        {
            var rows = roster.Select(chunk => chunk.Id + "\0" + chunk.ContentDigest
                + (chunk.RequiresFreshInvariantExecution ? "\0repository-invariant" : ""));
            return RcChunkReceipt.Hex(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
        }

        static object Plain(IDictionary row)
        {
            var copy = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (DictionaryEntry e in row) copy[PsValue.Str(e.Key)] = e.Value is IDictionary d ? Plain(d) : e.Value;
            return copy;
        }

        static string Text(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }
}
