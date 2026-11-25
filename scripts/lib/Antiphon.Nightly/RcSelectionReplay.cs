// CARD-1836 S5: the offline five-run replay oracle (plan "Replay measurement and activation gate", steps 1-6), the
// replay-rc-selection command. It reads ONLY a read-only input root laid out as the server2 custody copy
// (MANIFEST.sha256, custody.json, runs/<run8>/{logs,timing,attempt,candidate}, state/) plus git objects of
// --repository-root, and writes ONLY into a new --output directory. It predicts each run with the same selector
// (RcChunkSelection), trust gate (RcTrustedProjectGate) and admission predicate (RcChunkReceipt) as dispatch, seals
// predictions/<k>.json BEFORE opening run k's summary or TRX, reveals only own-gate rows to resolve a trust gate (sealed
// again as predictions/<k>.resolved.json), and advances the chained ledger with selected executions only (a simulated
// carry never seeds the next run with the result it hid). Scoring: missed = predicted carry AND actual FAIL; unsafe =
// predicted carry AND (FAIL or other nonclean or unknown). Exit 0 criterion met (enables=false: activation still needs
// two prospective Full shadows), 1 hard red (a missed failure or unsafe carry), 2 bad input, 3 insufficient evidence.
// A teacher-forced diagnosis (all prior observed outcomes) is written separately and never changes the verdict.
// Plain C#, ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    public static class RcSelectionReplay
    {
        public const string Name = "replay-rc-selection";
        public const string Usage = "replay-rc-selection --input-root <dir> --output <new-dir> --as-of <yyyy-MM-ddTHH:mm:ssZ> [--repository-root <git>]";
        public const string VerdictFormat = "antiphon-rc-replay-verdict-v1";
        public const string AlgorithmRevision = "card-1836-replay-r1";
        const string ManifestName = "MANIFEST.sha256";

        /// <summary>The qualification the replay scores: Windows x64 rc runs (a receipt from elsewhere is never a clean outcome).</summary>
        static readonly RcReceiptScope ReplayScope = new RcReceiptScope { Platform = "windows", Architecture = "x64", Profile = "rc" };

        /// <summary>The selector identity: algorithm revision, chunk selection format, project planner hash and content format.</summary>
        public static string SelectorHash => Digest(Encoding.UTF8.GetBytes(AlgorithmRevision + "/" + RcChunkSelectionResult.Format + "/" + RcProjectSelection.PlannerHash + "/" + RcChunkReceipt.ContentFormat));

        sealed class Refusal : Exception
        {
            public Refusal(string message) : base(message) { }
        }

        sealed class PlannedChunk
        {
            public string Suite = "";
            public string Chunk = "";
            public string Project = "";
            public string[] Classes = Array.Empty<string>();
            public string[] Consumes = Array.Empty<string>();
            public string[] Cases = Array.Empty<string>();
            public double? PriorWallSeconds;
            public string ContentDigest = "";
        }

        sealed class RunInput
        {
            public string Run8 = "";
            public string RunId = "";
            public string Stamp = "";
            public string Sha = "";
            public string Dir = "";
        }

        sealed class Origin
        {
            public string RunId = "";
            public string Sha = "";
            public IDictionary Row;
        }

        /// <summary>The command: parse, refuse bad input with exit 2, otherwise replay and write the verdict.</summary>
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            string inputRoot = "", outputRoot = "", asOfText = "", repository = Environment.CurrentDirectory;
            for (var i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new Refusal("usage");
                try
                {
                    switch (args[i])
                    {
                        case "--input-root": inputRoot = Next(); break;
                        case "--output": outputRoot = Next(); break;
                        case "--as-of": asOfText = Next(); break;
                        case "--repository-root": repository = Next(); break;
                        default: error.WriteLine(Name + ": unknown argument " + args[i] + "; usage: " + Usage); return 2;
                    }
                }
                catch (Refusal)
                {
                    error.WriteLine(Name + ": usage: " + Usage);
                    return 2;
                }
            }
            try
            {
                if (inputRoot.Length == 0 || outputRoot.Length == 0 || asOfText.Length == 0) throw new Refusal("usage");
                if (!DateTimeOffset.TryParseExact(asOfText, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var asOf))
                    throw new Refusal("as-of-format");
                return Replay(Path.GetFullPath(inputRoot), Path.GetFullPath(outputRoot), asOf, Path.GetFullPath(repository), output);
            }
            catch (Refusal r)
            {
                error.WriteLine(Name + ": refused (" + r.Message + ")");
                return 2;
            }
        }

        static int Replay(string input, string output, DateTimeOffset asOf, string repository, TextWriter log)
        {
            var phases = new OrderedDictionary(StringComparer.Ordinal);
            var clock = Stopwatch.StartNew();
            void Phase(string name)
            {
                phases[name] = Math.Round((phases.Contains(name) ? (double)phases[name] : 0) + clock.Elapsed.TotalMilliseconds, 3);
                clock.Restart();
            }

            // ---- input refusal and freeze ------------------------------------------------------------------------
            if (!Directory.Exists(input)) throw new Refusal("input-root-missing");
            if (Exists(output)) throw new Refusal("output-exists");
            if (Inside(output, input)) throw new Refusal("output-inside-input");
            if (Inside(output, repository) || Inside(repository, output)) throw new Refusal("output-inside-checkout");
            var manifest = VerifyManifest(input);
            Directory.CreateDirectory(output);
            var frozen = Path.Combine(output, "frozen");
            foreach (var relative in manifest.Keys)
            {
                var target = Path.Combine(frozen, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(input, relative.Replace('/', Path.DirectorySeparatorChar)), target);
            }
            File.Copy(Path.Combine(input, ManifestName), Path.Combine(frozen, ManifestName));
            if (!VerifyManifest(frozen).SequenceEqual(manifest)) throw new Refusal("freeze-mismatch");
            var inventoryDigest = Digest(File.ReadAllBytes(Path.Combine(frozen, ManifestName)));
            Phase("freeze");

            var custody = Json(Path.Combine(frozen, "custody.json")) as IDictionary ?? throw new Refusal("custody-missing");
            var runs = Items(custody["runs"]).Cast<IDictionary>().Select(r => new RunInput
            {
                Run8 = Str(r["run8"]), RunId = Str(r["runId"]), Stamp = Str(r["stamp"]), Sha = Str(r["sha"]), Dir = Path.Combine(frozen, "runs", Str(r["run8"])),
            }).Where(r => string.CompareOrdinal(r.Stamp, asOf.UtcDateTime.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)) < 0)
              .OrderBy(r => r.Stamp, StringComparer.Ordinal).ThenBy(r => r.RunId, StringComparer.Ordinal).ToList();
            var chosen = runs.Skip(Math.Max(0, runs.Count - 5)).ToList();
            var predecessorRow = custody["predecessor"] as IDictionary ?? throw new Refusal("predecessor-missing");
            var predecessor = new RunInput
            {
                Run8 = Str(predecessorRow["run8"]), RunId = Str(predecessorRow["runId"]), Sha = Str(predecessorRow["sha"]),
                Dir = Path.Combine(frozen, "runs", Str(predecessorRow["run8"])),
            };
            foreach (var run in chosen.Append(predecessor))
            {
                if (!RcChunkReceipt.IsSha(run.Sha)) throw new Refusal("run-sha:" + run.Run8);
                var type = TestSelectionGit.Run(repository, new[] { "cat-file", "-t", run.Sha }, new[] { 0, 1, 128 });
                if (type.Exit != 0 || type.Stdout.Trim() != "commit") throw new Refusal("candidate-sha-missing:" + run.Run8);
                if (!Directory.Exists(run.Dir)) throw new Refusal("run-missing:" + run.Run8);
            }

            var selectorHash = SelectorHash;
            var predictionsDir = Path.Combine(output, "predictions");
            Directory.CreateDirectory(predictionsDir);

            // ---- the chained ledger: seeded by the predecessor's clean executions ---------------------------------
            var ledger = new Dictionary<string, Origin>(StringComparer.Ordinal);
            var teacher = new Dictionary<string, Origin>(StringComparer.Ordinal);
            var predecessorPlan = Plan(predecessor);
            foreach (var (chunk, outcome, row) in Outcomes(predecessor, predecessorPlan))
                if (outcome == "clean")
                {
                    ledger[chunk.ContentDigest] = new Origin { RunId = predecessor.RunId, Sha = predecessor.Sha, Row = row };
                    teacher[chunk.ContentDigest] = ledger[chunk.ContentDigest];
                }
            var openFailures = new HashSet<string>(StringComparer.Ordinal);

            var runReports = new List<object>();
            var missed = new List<object>();
            var unsafeCarries = new List<object>();
            var caught = new List<object>();
            var teacherRuns = new List<object>();
            var predictionDigests = new OrderedDictionary(StringComparer.Ordinal);
            var totalCarries = 0;
            var strictCarries = 0;
            var failuresTotal = 0;
            double carriedSeconds = 0;
            var unpriced = 0;
            var reconciled = 0;
            var baseline = predecessor;
            var trustHash = "";
            var k = 0;
            foreach (var run in chosen)
            {
                k++;
                var plan = Plan(run);
                var census = Items(Json(Path.Combine(run.Dir, "logs", "groups-planned.json")) is IDictionary planDoc ? planDoc["census"] : null).Cast<IDictionary>()
                    .Select(c => new RcReaderCensusEntry { Reader = Str(c["reader"]), Globs = Items(c["globs"]).Select(Str).ToArray() }).ToArray();

                // ---- predict from pre-run inputs and the chained ledger only ----------------------------------------
                // Counterfactual column: the ledger origin by reconstructed content (the strict column counts v2 rows only).
                var priorRows = plan.Where(c => ledger.ContainsKey(c.ContentDigest)).Select(c => (IDictionary)new OrderedDictionary(StringComparer.Ordinal)
                {
                    { "contentDigest", c.ContentDigest }, { "sha", ledger[c.ContentDigest].Sha }, { "originRun", ledger[c.ContentDigest].RunId }, { "result", "pass" },
                }).ToArray();
                var selection = RcChunkSelection.Select(repository, baseline.Sha, run.Sha,
                    plan.Select(c => new RcPlannedChunk { Id = c.Chunk, Project = c.Project, Classes = c.Classes, ContentDigest = c.ContentDigest }).ToArray(),
                    priorRows, census, row => new RcReuse { Reusable = true });
                Phase("git");
                var changed = TestSelectionGit.Run(repository, new[] { "diff", "--no-ext-diff", "--no-renames", "--name-only", "-z", baseline.Sha, run.Sha, "--" })
                    .Stdout.Split('\0').Where(p => p.Length > 0).ToArray();
                var policyB = Blob(repository, baseline.Sha, RcTrustedProjectGate.PolicyPath);
                var policyA = Blob(repository, run.Sha, RcTrustedProjectGate.PolicyPath);
                trustHash = RcTrustedProjectGate.PolicyDigest(policyA);
                var contracts = Items(Json(Path.Combine(run.Dir, "attempt", "contracts.json"))).Cast<IDictionary>().Select(c => new RcContractReceipt
                {
                    Project = Str(c["project"]), Side = Str(c["side"]), ExtractorVersion = Str(c["extractorVersion"]), ContractHash = Str(c["contractHash"]),
                }).ToArray();
                var trustChunks = plan.Select(c => new RcTrustChunk
                {
                    Id = c.Chunk, Project = c.Project, Consumes = c.Consumes,
                    DirectHit = selection.Of(c.Chunk).Reasons.Any(r => !r.StartsWith("project-closure:", StringComparison.Ordinal) && !r.StartsWith("no-receipt", StringComparison.Ordinal)),
                }).ToArray();
                var trust = selection.Mode == "Full" ? null : RcTrustedProjectGate.Decide(policyB, policyA, changed, trustChunks, null, contracts);
                var predictions = new List<OrderedDictionary>();
                foreach (var chunk in plan)
                {
                    var decision = selection.Of(chunk.Chunk);
                    var state = decision.Decision == "carry" ? "carry" : "run";
                    var reasons = decision.Reasons.ToList();
                    if (state == "run" && trust != null && trust.Of(chunk.Chunk)?.Decision == "held"
                        && decision.Reasons.All(r => r.StartsWith("project-closure:", StringComparison.Ordinal)))
                    {
                        state = "held";
                        reasons.AddRange(trust.Of(chunk.Chunk).Reasons);
                    }
                    if (state == "run" && openFailures.Contains(chunk.ContentDigest)) reasons.Add("open-failure");
                    var origin = state == "carry" ? ledger[chunk.ContentDigest] : null;
                    // A carry states why: no selector hit, and the receipt it carries from (the missed-failure table quotes it).
                    if (state == "carry") reasons.AddRange(new[] { "carry:no-impact-hit", "origin-receipt:" + origin.RunId });
                    predictions.Add(new OrderedDictionary(StringComparer.Ordinal)
                    {
                        { "suite", chunk.Suite }, { "chunk", chunk.Chunk }, { "contentDigest", chunk.ContentDigest }, { "decision", state }, { "reasons", reasons.ToArray() },
                        { "originRun", origin?.RunId }, { "originSha", origin?.Sha }, { "ifGatePasses", state == "held" ? "carry" : null }, { "ifGateFails", state == "held" ? "run" : null },
                    });
                }
                Phase("predict");
                var predictionPath = Path.Combine(predictionsDir, k.ToString(CultureInfo.InvariantCulture) + ".json");
                var sealedPrediction = Write(predictionPath, new OrderedDictionary(StringComparer.Ordinal)
                {
                    { "run", run.RunId }, { "sha", run.Sha }, { "baselineSha", baseline.Sha }, { "baselineRun", baseline.RunId }, { "mode", selection.Mode },
                    { "fallbackReasons", selection.FallbackReasons }, { "selectorHash", selectorHash },
                    { "index", selection.Index.Select(i => (object)new OrderedDictionary { { "side", i.Side }, { "blob", i.Blob }, { "sha256", i.Sha256 } }).ToArray() },
                    { "predictions", predictions.Cast<object>().ToArray() },
                });
                predictionDigests[k.ToString(CultureInfo.InvariantCulture)] = sealedPrediction;
                Phase("seal-predictions");

                // ---- outcomes: only after the seal; own-gate rows first --------------------------------------------
                var outcomes = Outcomes(run, plan, sealedPrediction, predictionPath).ToDictionary(o => o.Chunk.Chunk, StringComparer.Ordinal);
                if (predictions.Any(p => Str(p["decision"]) == "held"))
                {
                    var own = new List<RcOwnResult>();
                    foreach (var chunk in plan.Where(c => trust.Of(c.Chunk)?.Reasons.Any(r => r.StartsWith("trust-gate-own:", StringComparison.Ordinal)) == true))
                    {
                        var o = outcomes[chunk.Chunk];
                        own.Add(new RcOwnResult
                        {
                            Chunk = chunk.Chunk, Disposition = o.Outcome == "unknown" ? "missing" : "executed", Result = o.Outcome == "clean" ? "pass" : "FAIL",
                            PassedCases = o.Outcome == "clean" ? chunk.Cases : Array.Empty<string>(),
                        });
                    }
                    var resolved = RcTrustedProjectGate.Decide(policyB, policyA, changed, trustChunks, own, contracts);
                    foreach (var p in predictions.Where(p => Str(p["decision"]) == "held"))
                    {
                        var r = resolved.Of(Str(p["chunk"]));
                        p["decision"] = r.Decision == "carry" ? "carry" : "run";
                        p["reasons"] = ((string[])p["reasons"]).Concat(r.Reasons).Distinct(StringComparer.Ordinal).ToArray();
                        if (r.Decision == "carry")
                        {
                            var digest = Str(p["contentDigest"]);
                            if (!ledger.TryGetValue(digest, out var o)) p["decision"] = "run";
                            else { p["originRun"] = o.RunId; p["originSha"] = o.Sha; }
                        }
                    }
                    Write(Path.Combine(predictionsDir, k.ToString(CultureInfo.InvariantCulture) + ".resolved.json"),
                        new OrderedDictionary { { "run", run.RunId }, { "predictions", predictions.Cast<object>().ToArray() } });
                }
                Phase("outcomes");

                // ---- score ----------------------------------------------------------------------------------------
                var matrix = new OrderedDictionary(StringComparer.Ordinal);
                foreach (var d in new[] { "run", "carry" })
                    foreach (var a in new[] { "clean", "FAIL", "other", "unknown" }) matrix[d + "/" + a] = 0;
                int timeouts = 0, skipped = 0, incomplete = 0;
                var trace = new StringBuilder();
                var runCarries = 0;
                foreach (var p in predictions)
                {
                    var chunk = plan.First(c => c.Chunk == Str(p["chunk"]));
                    var o = outcomes[chunk.Chunk];
                    var predicted = Str(p["decision"]);
                    var actual = o.Outcome == "clean" ? "clean" : o.Outcome == "FAIL" ? "FAIL" : o.Outcome == "unknown" ? "unknown" : "other";
                    matrix[predicted + "/" + actual] = (int)matrix[predicted + "/" + actual] + 1;
                    if (o.Detail == "timeout") timeouts++;
                    if (o.Detail == "skipped") skipped++;
                    if (o.Outcome == "unknown") incomplete++;
                    if (actual == "FAIL") failuresTotal++;
                    var entry = new OrderedDictionary(StringComparer.Ordinal)
                    {
                        { "run", run.RunId }, { "suite", chunk.Suite }, { "chunk", chunk.Chunk }, { "contentDigest", chunk.ContentDigest }, { "case", o.FailedCase },
                        { "sha", run.Sha }, { "baseline", baseline.Sha }, { "reason", string.Join(",", (string[])p["reasons"]) }, { "trx", o.Trx },
                        { "originRun", p["originRun"] }, { "actual", actual },
                    };
                    if (predicted == "carry")
                    {
                        runCarries++;
                        if (ledger.TryGetValue(chunk.ContentDigest, out var origin) && origin.Row != null && RcChunkReceipt.IsSha(PsValue.Str(origin.Row["sha"]))
                            && RcChunkReceipt.IsDigest(PsValue.Str(origin.Row["contentDigest"]))) strictCarries++;
                        if (chunk.PriorWallSeconds.HasValue) carriedSeconds += chunk.PriorWallSeconds.Value; else unpriced++;
                        if (actual == "FAIL") missed.Add(entry);
                        if (actual != "clean") unsafeCarries.Add(entry);
                    }
                    else if (actual == "FAIL") caught.Add(entry);
                    trace.Append(JsonSerializer.Serialize(new { run = run.RunId, chunk = chunk.Chunk, contentDigest = chunk.ContentDigest, decision = predicted, reasons = (string[])p["reasons"], actual }))
                        .Append('\n');
                }
                totalCarries += runCarries;
                var universe = plan.Count;
                var accounted = predictions.Count(p => Str(p["decision"]) == "run") + predictions.Count(p => Str(p["decision"]) == "carry");
                var disjoint = predictions.Select(p => Str(p["chunk"])).Distinct(StringComparer.Ordinal).Count() == predictions.Count;
                var ok = accounted == universe && disjoint && predictions.All(p => Str(p["decision"]) != "held");
                if (ok) reconciled++;
                var runDir = Path.Combine(output, "runs", k.ToString(CultureInfo.InvariantCulture));
                Directory.CreateDirectory(runDir);
                var report = new OrderedDictionary(StringComparer.Ordinal)
                {
                    { "run", run.RunId }, { "sha", run.Sha }, { "baselineSha", baseline.Sha }, { "mode", selection.Mode }, { "fallbackReasons", selection.FallbackReasons },
                    { "universe", universe }, { "predictedRun", predictions.Count(p => Str(p["decision"]) == "run") }, { "predictedCarry", runCarries },
                    { "predictedHeld", predictions.Count(p => Str(p["decision"]) == "held") }, { "reconciled", ok }, { "matrix", matrix },
                    { "timeouts", timeouts }, { "skipped", skipped }, { "incomplete", incomplete },
                };
                Write(Path.Combine(runDir, "matrix.json"), report);
                File.WriteAllText(Path.Combine(runDir, "trace.jsonl"), trace.ToString(), new UTF8Encoding(false));
                runReports.Add(report);

                // ---- the teacher-forced diagnosis (every prior observed outcome), separate from the verdict -----------
                var teacherDecisions = plan.Select(c => (object)new OrderedDictionary
                {
                    { "chunk", c.Chunk }, { "contentDigest", c.ContentDigest },
                    { "decision", selection.Of(c.Chunk).Decision == "execute" || !teacher.ContainsKey(c.ContentDigest) ? "run" : "carry" },
                    { "originRun", teacher.TryGetValue(c.ContentDigest, out var t) ? t.RunId : null },
                }).ToArray();
                teacherRuns.Add(new OrderedDictionary { { "run", run.RunId }, { "decisions", teacherDecisions } });

                // ---- advance the chain: selected executions only --------------------------------------------------
                foreach (var p in predictions)
                {
                    var chunk = plan.First(c => c.Chunk == Str(p["chunk"]));
                    var o = outcomes[chunk.Chunk];
                    if (o.Outcome == "clean") teacher[chunk.ContentDigest] = new Origin { RunId = run.RunId, Sha = run.Sha, Row = o.Row };
                    else teacher.Remove(chunk.ContentDigest);
                    if (Str(p["decision"]) != "run") continue;
                    if (o.Outcome == "clean")
                    {
                        ledger[chunk.ContentDigest] = new Origin { RunId = run.RunId, Sha = run.Sha, Row = o.Row };
                        openFailures.Remove(chunk.ContentDigest);
                    }
                    else
                    {
                        ledger.Remove(chunk.ContentDigest);
                        openFailures.Add(chunk.ContentDigest);
                    }
                }
                baseline = run;
                Phase("score");
            }

            Write(Path.Combine(output, "teacher-forced.json"), new OrderedDictionary { { "mode", "teacher-forced" }, { "diagnosticOnly", true }, { "runs", teacherRuns.ToArray() } });

            // ---- verdict ----------------------------------------------------------------------------------------------
            int exit;
            string activation, reason;
            if (missed.Count > 0 || unsafeCarries.Count > 0) { exit = 1; activation = "blocked"; reason = missed.Count > 0 ? "missed-failure" : "unsafe-carry"; }
            else if (chosen.Count < 5 || reconciled < 5) { exit = 3; activation = "insufficient"; reason = "insufficient:fewer-than-five-reconciled"; }
            else if (totalCarries == 0) { exit = 3; activation = "insufficient"; reason = "insufficient:no-carry-opportunity"; }
            else { exit = 0; activation = "criterion-met"; reason = "criterion-met"; }
            var caughtCount = caught.Count;
            Write(Path.Combine(output, "verdict.json"), new OrderedDictionary(StringComparer.Ordinal)
            {
                { "format", VerdictFormat }, { "selectorHash", selectorHash }, { "algorithmRevision", AlgorithmRevision }, { "trustPolicyHash", trustHash },
                { "inventoryDigest", inventoryDigest }, { "predictionDigests", predictionDigests }, { "asOf", asOf.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) },
                { "runs", runReports.ToArray() }, { "missedFailures", missed.ToArray() }, { "unsafeCarries", unsafeCarries.ToArray() }, { "caughtFailures", caught.ToArray() },
                { "failureRecall", failuresTotal == 0 ? null : (object)Math.Round((double)caughtCount / failuresTotal, 6) },
                { "carries", new OrderedDictionary { { "counterfactual", totalCarries }, { "strict", strictCarries } } },
                { "cost", new OrderedDictionary { { "carriedSeconds", carriedSeconds }, { "unpricedCarries", unpriced } } },
                { "activation", activation }, { "reason", reason }, { "exitCode", exit }, { "enables", false },
            });
            Phase("verdict");
            Write(Path.Combine(output, "phases.json"), phases);
            log.WriteLine(Name + ": " + reason + " (exit " + exit.ToString(CultureInfo.InvariantCulture) + ") missed=" + missed.Count.ToString(CultureInfo.InvariantCulture)
                + " unsafe=" + unsafeCarries.Count.ToString(CultureInfo.InvariantCulture) + " carries=" + totalCarries.ToString(CultureInfo.InvariantCulture));
            return exit;
        }

        // ---- inputs ---------------------------------------------------------------------------------------------

        /// <summary>The manifest's (path, sha256) map after checking the tree has exactly those files, no links, and equal digests.</summary>
        static SortedDictionary<string, string> VerifyManifest(string root)
        {
            var path = Path.Combine(root, ManifestName);
            if (!File.Exists(path)) throw new Refusal("manifest-missing");
            var listed = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(path).Where(l => l.Length > 0))
            {
                var split = line.IndexOf("  ", StringComparison.Ordinal);
                if (split != 64 || !RcChunkReceipt.IsDigest(line.Substring(0, 64))) throw new Refusal("manifest-line");
                var relative = line.Substring(66);
                if (relative.Contains("..", StringComparison.Ordinal) || relative.StartsWith("/", StringComparison.Ordinal) || listed.ContainsKey(relative)) throw new Refusal("manifest-path");
                listed[relative] = line.Substring(0, 64);
            }
            var present = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(entry);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new Refusal("link:" + Relative(root, entry));
                if (Directory.Exists(entry)) continue;
                var relative = Relative(root, entry);
                if (relative == ManifestName) continue;
                if (!listed.ContainsKey(relative)) throw new Refusal("extra-file:" + relative);
                present[relative] = Digest(File.ReadAllBytes(entry));
            }
            foreach (var pair in listed)
            {
                if (!present.TryGetValue(pair.Key, out var actual)) throw new Refusal("missing-file:" + pair.Key);
                if (actual != pair.Value) throw new Refusal("digest-mismatch:" + pair.Key);
            }
            return listed;
        }

        /// <summary>A run's planned universe from its prelaunch groups-planned.json, each chunk's content digest reconstructed from its members.</summary>
        static List<PlannedChunk> Plan(RunInput run)
        {
            var doc = Json(Path.Combine(run.Dir, "logs", "groups-planned.json")) as IDictionary ?? throw new Refusal("plan-missing:" + run.Run8);
            var chunks = new List<PlannedChunk>();
            foreach (IDictionary c in Items(doc["chunks"]))
            {
                var members = Items(c["members"]).Cast<IDictionary>()
                    .Select(m => new RcCaseIdentity(Str(m["assembly"]), Str(m["class"]), Str(m["method"]), Str(m["case"]))).ToArray();
                var suite = Str(c["suite"]);
                var digest = RcChunkReceipt.ContentDigest(suite, "native", members) ?? throw new Refusal("content-unproven:" + run.Run8 + ":" + Str(c["chunk"]));
                chunks.Add(new PlannedChunk
                {
                    Suite = suite, Chunk = Str(c["chunk"]), Project = Str(c["project"]), Classes = Items(c["classes"]).Select(Str).ToArray(),
                    Consumes = Items(c["consumes"]).Select(Str).ToArray(), Cases = members.Select(m => m.CaseKey).ToArray(),
                    PriorWallSeconds = c["priorWallSeconds"] == null ? null : Convert.ToDouble(c["priorWallSeconds"], CultureInfo.InvariantCulture), ContentDigest = digest,
                });
            }
            if (chunks.Select(c => c.Chunk).Distinct(StringComparer.Ordinal).Count() != chunks.Count || chunks.Select(c => c.ContentDigest).Distinct(StringComparer.Ordinal).Count() != chunks.Count)
                throw new Refusal("plan-duplicate:" + run.Run8);
            return chunks;
        }

        sealed class ChunkOutcome
        {
            public PlannedChunk Chunk;
            public string Outcome = "unknown";
            public string Detail = "";
            public string FailedCase = "";
            public string Trx = "";
            public IDictionary Row;
            public void Deconstruct(out PlannedChunk chunk, out string outcome, out IDictionary row) { chunk = Chunk; outcome = Outcome; row = Row; }
        }

        /// <summary>The predecessor's outcomes (before any prediction; no seal needed).</summary>
        static IEnumerable<ChunkOutcome> Outcomes(RunInput run, List<PlannedChunk> plan) => ReadOutcomes(run, plan);

        /// <summary>Run k's outcomes: refused unless predictions/&lt;k&gt;.json was sealed with exactly the given digest.</summary>
        static IEnumerable<ChunkOutcome> Outcomes(RunInput run, List<PlannedChunk> plan, string sealedDigest, string predictionPath)
        {
            if (!File.Exists(predictionPath) || Digest(File.ReadAllBytes(predictionPath)) != sealedDigest) throw new Refusal("outcomes-before-seal:" + run.Run8);
            return ReadOutcomes(run, plan).ToList();
        }

        static IEnumerable<ChunkOutcome> ReadOutcomes(RunInput run, List<PlannedChunk> plan)
        {
            var summary = Json(Path.Combine(run.Dir, "logs", "summary.json")) as IDictionary;
            var rows = summary == null ? Array.Empty<IDictionary>() : Items(summary["suites"]).Cast<IDictionary>().ToArray();
            foreach (var chunk in plan)
            {
                var outcome = new ChunkOutcome { Chunk = chunk };
                var matches = rows.Where(r => Str(r["id"]) == chunk.Suite && Str(r["chunk"]) == chunk.Chunk).ToArray();
                if (matches.Length == 1)
                {
                    var row = matches[0];
                    outcome.Row = row;
                    var trxName = Path.GetFileName(Str(row["trx"]).Replace('\\', '/'));
                    var trxPath = trxName.Length == 0 ? "" : Path.Combine(run.Dir, "logs", trxName);
                    var trx = trxPath.Length == 0 ? null : RcChunkReceipt.ReadTrx(trxPath);
                    outcome.Trx = trx == null ? "" : "runs/" + run.Run8 + "/logs/" + trxName;
                    if (trx == null) outcome.Outcome = "unknown";
                    else
                    {
                        var reuse = RcChunkReceipt.Reusable(row, trx, chunk.Cases, ReplayScope);
                        var failedCase = trx.FirstOrDefault(c => c.Outcome == "Failed" || c.Outcome == "Error");
                        outcome.FailedCase = failedCase?.CaseKey ?? "";
                        if (reuse.Reusable) outcome.Outcome = "clean";
                        else if (Str(row["result"]) == "FAIL" || failedCase != null) outcome.Outcome = "FAIL";
                        else
                        {
                            outcome.Outcome = "other";
                            outcome.Detail = PsValue.IsTrue(row["timedOut"]) ? "timeout" : reuse.Reasons.Any(r => r.StartsWith("skipped-cases", StringComparison.Ordinal)) ? "skipped" : "";
                        }
                    }
                }
                yield return outcome;
            }
        }

        // ---- helpers ----------------------------------------------------------------------------------------------

        static string Blob(string repository, string sha, string path)
        {
            var result = TestSelectionGit.Run(repository, new[] { "cat-file", "blob", sha + ":" + path }, new[] { 0, 1, 128 });
            return result.Exit == 0 ? result.Stdout : null;
        }

        static string Write(string path, object document)
        {
            var bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes, 0, bytes.Length);
            return Digest(bytes);
        }

        static object Json(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                return RcChunkReceipt.FromJson(doc.RootElement);
            }
            catch (JsonException) { throw new Refusal("json:" + Path.GetFileName(path)); }
        }

        static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        static bool Inside(string child, string parent)
        {
            var c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return c.StartsWith(p, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

        static string Str(object value) => PsValue.Str(value);

        static object[] Items(object value) => value is IEnumerable items && !(value is string) && !(value is IDictionary) ? items.Cast<object>().ToArray() : value == null ? new object[0] : new[] { value };
    }
}
