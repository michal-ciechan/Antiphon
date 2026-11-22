// The cumulative seed merge, compiled: antiphon-scripts merge-nightly-seeds (NightlySeedCommands) forwards its argv here.
//
// Merges stopped-run seeds into one operator-seed-from-stopped-run envelope and a deterministic report. Chunk rows and
// evidence come from the newest seed; failedTests lists the identities not yet proven, each placed in the newest
// seed's chunk for it. With -Repo, a decision SHA the last failing run already contained (ancestor or cherry-pick
// trailer) is ignored, and a later green proves an identity only when its source contains a remaining decision SHA
// (or -Proven names that run). Exit 0 merged, 2 refused (the reason is the output), 3 merged under -Strict with an
// identity unplaced or open without a decision. The git proof runs git: one cat-file --batch-check for every SHA's
// existence, then merge-base/log per (source, fix) pair, cached.
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The merge's exit code and the lines it printed.</summary>
    public sealed class SeedMergeOutcome
    {
        public int Code { get; internal set; }
        public string[] Lines { get; internal set; }
    }

    public static class SeedMerge
    {
        internal const string SkipNote = "A test skipped inside a completed chunk looks green; that is an accepted seed-schema limit.";
        private static readonly Regex Sha40 = new Regex("^[a-f0-9]{40}$");
        private static readonly Regex CommitLine = new Regex("^[0-9a-f]{40} commit [0-9]+$");

        private sealed class Exit : Exception
        {
            public Exit(int code, string line) : base(line) { Code = code; }
            public int Code { get; }
        }

        private sealed class Loaded
        {
            public object Doc;
            public int Index;
            public long Ticks;
            public string RunId => PsValue.Str(PsValue.Prop(Doc, "sourceRunId"));
            public string Sha => PsValue.Str(PsValue.Prop(Doc, "sourceSha"));
        }

        private sealed class Identity
        {
            public string Suite, ClassName, Method, Outcome;
            public int FirstIndex, LastIndex;
        }

        private sealed class ProvenRow
        {
            public string Key, RunId, Suite, ClassName, Method;
        }

        private sealed class Containment
        {
            public bool Contained;
            public string Commit;
        }

        private static Exit Refuse(string reason) => new Exit(2, reason);

        /// <summary>merge-nightly-seeds with these arguments (-Seed and -Proven repeat; -Strict is a switch).</summary>
        public static SeedMergeOutcome Run(string[] args)
        {
            var links = new HistoryLinkCache();
            var forceWindows = HistoryNodeIdentity.ForceWindows;
            var lines = new List<string>();
            string outputPath = "", reportPath = "";
            bool wroteSeed = false, wroteReport = false;
            try
            {
                var seeds = new List<string>();
                var proven = new List<string>();
                string decision = "", repo = "";
                var strict = false;
                args = args ?? new string[0];
                for (var i = 0; i < args.Length; i++)
                {
                    var name = args[i];
                    if (name == "-Strict") { strict = true; continue; }
                    if (i + 1 >= args.Length) throw Refuse("bad-argument");
                    var value = args[i + 1];
                    if (value.StartsWith("-", StringComparison.Ordinal)) throw Refuse("bad-argument");
                    if (name == "-Seed") seeds.Add(value);
                    else if (name == "-Proven") proven.Add(value);
                    else if (name == "-Decision") { if (!string.IsNullOrWhiteSpace(decision)) throw Refuse("bad-argument"); decision = value; }
                    else if (name == "-Repo") { if (!string.IsNullOrWhiteSpace(repo)) throw Refuse("bad-argument"); repo = value; }
                    else if (name == "-OutputPath") { if (!string.IsNullOrWhiteSpace(outputPath)) throw Refuse("bad-argument"); outputPath = value; }
                    else if (name == "-ReportPath") { if (!string.IsNullOrWhiteSpace(reportPath)) throw Refuse("bad-argument"); reportPath = value; }
                    else throw Refuse("bad-argument");
                    i++;
                }
                if (seeds.Count < 1) throw Refuse("seed-required");
                foreach (var path in seeds) if (string.IsNullOrWhiteSpace(path)) throw Refuse("seed-required");
                if (string.IsNullOrWhiteSpace(outputPath) || string.IsNullOrWhiteSpace(reportPath)) throw Refuse("seed-output-exists");
                if (outputPath == reportPath) throw Refuse("seed-output-exists");
                if (File.Exists(outputPath) || File.Exists(reportPath)) throw Refuse("seed-output-exists");

                var loaded = new List<Loaded>();
                foreach (var path in seeds)
                {
                    var doc = StoppedSeedFiles.ReadContent(path, "", links, forceWindows);
                    var stamp = StoppedSeed.Utc(PsValue.Prop(doc, "sourceObservedAtUtc"));
                    loaded.Add(new Loaded { Doc = doc, Index = loaded.Count, Ticks = stamp.HasValue ? stamp.Value.UtcDateTime.Ticks : long.MinValue });
                }
                var runIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var doc in loaded) if (!runIds.Add(doc.RunId)) throw Refuse("duplicate-source-run");
                var newest = loaded[0];
                foreach (var doc in loaded) if (Later(doc, newest)) newest = doc;

                var decisions = ReadDecisions(decision);
                var git = string.IsNullOrWhiteSpace(repo) ? null : new GitProof(repo);
                if (git != null)
                {
                    var needed = new List<string>();
                    foreach (var doc in loaded) if (!needed.Contains(doc.Sha)) needed.Add(doc.Sha);
                    foreach (var shas in decisions.Values) foreach (var sha in shas) if (!needed.Contains(sha)) needed.Add(sha);
                    git.RequireCommits(needed);
                }

                var identities = new Dictionary<string, Identity>(StringComparer.Ordinal);
                foreach (var doc in loaded)
                {
                    foreach (var failed in PsValue.AsArray(PsValue.Prop(doc.Doc, "failedTests")))
                    {
                        var suite = PsValue.Str(PsValue.Prop(failed, "suite"));
                        var className = PsValue.Str(PsValue.Prop(failed, "className"));
                        var method = PsValue.Str(PsValue.Prop(failed, "method"));
                        var outcome = PsValue.Str(PsValue.Prop(failed, "outcome"));
                        var key = suite + "|" + className + "|" + method;
                        if (!identities.TryGetValue(key, out var record))
                        {
                            identities[key] = new Identity { Suite = suite, ClassName = className, Method = method, FirstIndex = doc.Index, LastIndex = doc.Index, Outcome = outcome };
                            continue;
                        }
                        var first = loaded[record.FirstIndex];
                        if (doc.Ticks < first.Ticks || (doc.Ticks == first.Ticks && doc.Index < first.Index)) record.FirstIndex = doc.Index;
                        if (Later(doc, loaded[record.LastIndex])) { record.LastIndex = doc.Index; record.Outcome = outcome; }
                    }
                }

                var provenRows = new List<ProvenRow>();
                foreach (var item in proven)
                {
                    if (string.IsNullOrWhiteSpace(item)) throw Refuse("proven-run-unknown");
                    var colon = item.LastIndexOf(':');
                    if (colon <= 0 || colon >= item.Length - 1) throw Refuse("proven-run-unknown");
                    var parts = item.Substring(0, colon).Split('|');
                    var runId = item.Substring(colon + 1);
                    if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[2]) || string.IsNullOrWhiteSpace(runId))
                        throw Refuse("proven-run-unknown");
                    provenRows.Add(new ProvenRow { Key = parts[0] + "|" + parts[1] + "|" + parts[2], RunId = runId, Suite = parts[0], ClassName = parts[1], Method = parts[2] });
                }
                foreach (var row in provenRows)
                {
                    if (!identities.TryGetValue(row.Key, out var record)) throw Refuse("proven-run-unknown");
                    var named = loaded.Find(doc => doc.RunId == row.RunId) ?? throw Refuse("proven-run-unknown");
                    if (!Later(named, loaded[record.LastIndex])) throw Refuse("proven-run-unknown");
                    if (!Green(named.Doc, row.Suite, row.ClassName, row.Method)) throw Refuse("proven-not-green");
                }

                var reportRows = new List<OrderedDictionary>();
                var failedRows = new List<OrderedDictionary>();
                foreach (var pair in identities)
                {
                    var key = pair.Key;
                    var record = pair.Value;
                    var last = loaded[record.LastIndex];
                    var later = loaded.FindAll(doc => Later(doc, last));
                    later.Sort((a, b) => a.Ticks != b.Ticks ? a.Ticks.CompareTo(b.Ticks) : a.Index.CompareTo(b.Index));
                    var decisionList = decisions.TryGetValue(key, out var stored) ? stored : new List<string>();
                    var usable = new List<string>();
                    var fixPresentAtFailure = false;
                    foreach (var fix in decisionList)
                    {
                        if (string.IsNullOrWhiteSpace(fix)) continue;
                        if (git != null && git.Contains(last.Sha, fix).Contained) { fixPresentAtFailure = true; continue; }
                        usable.Add(fix);
                    }
                    Loaded proving = null;
                    string containing = null;
                    foreach (var doc in later)
                    {
                        var green = Green(doc.Doc, record.Suite, record.ClassName, record.Method);
                        var hit = DecisionHit(git, doc, usable);
                        var named = provenRows.Exists(row => row.Key == key && row.RunId == doc.RunId);
                        if (green && (hit != null || named))
                        {
                            proving = doc;
                            if (hit != null) containing = hit.Commit;
                            break;
                        }
                    }
                    var status = "open";
                    string provingRun = null, provingSha = null;
                    if (proving != null) { status = "proven"; provingRun = proving.RunId; provingSha = proving.Sha; }
                    else
                    {
                        foreach (var doc in later)
                        {
                            var hit = DecisionHit(git, doc, usable);
                            if (hit != null) { status = "fixed-pending-proof"; containing = hit.Commit; break; }
                        }
                    }
                    string chunkName = null;
                    if (status != "proven")
                    {
                        foreach (var failed in PsValue.AsArray(PsValue.Prop(newest.Doc, "failedTests")))
                        {
                            if (PsValue.Str(PsValue.Prop(failed, "suite")) == record.Suite && PsValue.Str(PsValue.Prop(failed, "className")) == record.ClassName
                                && PsValue.Str(PsValue.Prop(failed, "method")) == record.Method) { chunkName = PsValue.Str(PsValue.Prop(failed, "chunk")); break; }
                        }
                        if (chunkName == null)
                        {
                            foreach (var chunk in PsValue.AsArray(PsValue.Prop(newest.Doc, "chunks")))
                            {
                                if (PsValue.Str(PsValue.Prop(chunk, "suite")) == record.Suite && PsOps.Contains(PsValue.AsArray(PsValue.Prop(chunk, "classes")), record.ClassName, false))
                                { chunkName = PsValue.Str(PsValue.Prop(chunk, "chunk")); break; }
                            }
                        }
                    }
                    var placed = chunkName != null;
                    if (placed)
                        failedRows.Add(new OrderedDictionary { { "suite", record.Suite }, { "chunk", chunkName }, { "className", record.ClassName }, { "method", record.Method }, { "outcome", record.Outcome } });
                    var decisionShas = decisionList.FindAll(sha => !string.IsNullOrWhiteSpace(sha)).ToArray();
                    reportRows.Add(new OrderedDictionary
                    {
                        { "suite", record.Suite }, { "className", record.ClassName }, { "method", record.Method },
                        { "firstFailedRun", loaded[record.FirstIndex].RunId }, { "lastFailedRun", last.RunId },
                        { "status", status }, { "placed", placed }, { "outcome", record.Outcome },
                        { "provingRun", provingRun }, { "provingSha", provingSha }, { "decisionShas", decisionShas },
                        { "containingCommit", containing }, { "fixPresentAtFailure", fixPresentAtFailure },
                    });
                }
                failedRows.Sort(ByIdentity);
                reportRows.Sort(ByIdentity);
                var unplacedCount = 0;
                var strictBreach = false;
                foreach (var row in reportRows)
                {
                    var open = (string)row["status"] != "proven";
                    if (!(bool)row["placed"] && open) unplacedCount++;
                    if (((string[])row["decisionShas"]).Length == 0 && open) strictBreach = true;
                }
                if (unplacedCount > 0) strictBreach = true;

                var created = StoppedSeed.Utc(PsValue.Prop(newest.Doc, "createdAtUtc"));
                var observed = StoppedSeed.Utc(PsValue.Prop(newest.Doc, "sourceObservedAtUtc"));
                if (created == null || observed == null) throw new InvalidDataException("seed-timestamp-missing");
                var evidence = PsValue.Prop(newest.Doc, "evidence");
                var payload = new OrderedDictionary
                {
                    { "sourceObservedAtUtc", observed.Value.ToString("o", CultureInfo.InvariantCulture) },
                    { "chunks", PsValue.AsArray(PsValue.Prop(newest.Doc, "chunks")) },
                    { "failedTests", failedRows.ToArray() },
                    { "evidence", evidence == null ? new object[0] : PsValue.AsArray(evidence) },
                };
                var envelope = new OrderedDictionary
                {
                    { "format", "antiphon-nightly-stopped-seed-v1" }, { "seedKind", "operator-seed-from-stopped-run" },
                    { "sourceRunId", newest.RunId }, { "sourceCandidateId", PsValue.Str(PsValue.Prop(newest.Doc, "sourceCandidateId")) },
                    { "sourceSha", newest.Sha }, { "createdAtUtc", created.Value.ToString("o", CultureInfo.InvariantCulture) },
                    { "payload", SeedJson.Text(payload, false) }, { "contentHash", "" },
                };
                envelope["contentHash"] = StoppedSeedFiles.ContentHash(envelope);
                var report = new OrderedDictionary
                {
                    { "format", "antiphon-cumulative-failure-report-v1" }, { "newestRun", newest.RunId },
                    { "unplacedCount", unplacedCount }, { "identities", reportRows.ToArray() },
                };
                var seedParent = Path.GetDirectoryName(outputPath);
                var reportParent = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrWhiteSpace(seedParent)) Directory.CreateDirectory(seedParent);
                if (!string.IsNullOrWhiteSpace(reportParent)) Directory.CreateDirectory(reportParent);
                if (File.Exists(outputPath) || File.Exists(reportPath)) throw Refuse("seed-output-exists");
                File.WriteAllBytes(outputPath, SeedJson.Bytes(envelope, true));
                wroteSeed = true;
                File.WriteAllBytes(reportPath, SeedJson.Bytes(report, true));
                wroteReport = true;
                var written = StoppedSeedFiles.ReadContent(outputPath, "", links, forceWindows);
                if (PsValue.Str(PsValue.Prop(written, "sourceRunId")) != newest.RunId) throw new InvalidDataException("merged-seed-unreadable");
                lines.Add(SkipNote);
                lines.Add("digest=" + StoppedSeedFiles.Sha(outputPath));
                return new SeedMergeOutcome { Code = strict && strictBreach ? 3 : 0, Lines = lines.ToArray() };
            }
            catch (Exit exit)
            {
                lines.Add(exit.Message);
                return new SeedMergeOutcome { Code = exit.Code, Lines = lines.ToArray() };
            }
            catch (Exception e)
            {
                if (wroteSeed && File.Exists(outputPath)) File.Delete(outputPath);
                if (wroteReport && File.Exists(reportPath)) File.Delete(reportPath);
                lines.Add(StoppedSeedFiles.Message(e));
                return new SeedMergeOutcome { Code = 2, Lines = lines.ToArray() };
            }
        }

        private static bool Later(Loaded candidate, Loaded baseline) =>
            candidate.Ticks > baseline.Ticks || (candidate.Ticks == baseline.Ticks && candidate.Index > baseline.Index);

        // Every chunk listing the class completed, and the method not among the seed's failures.
        private static bool Green(object seed, string suite, string className, string method)
        {
            var listed = 0;
            foreach (var chunk in PsValue.AsArray(PsValue.Prop(seed, "chunks")))
            {
                if (PsValue.Str(PsValue.Prop(chunk, "suite")) != suite || !PsOps.Contains(PsValue.AsArray(PsValue.Prop(chunk, "classes")), className, false)) continue;
                listed++;
                if (PsOps.Ne(PsValue.Prop(chunk, "completed"), true, true)) return false;
            }
            if (listed == 0) return false;
            foreach (var failed in PsValue.AsArray(PsValue.Prop(seed, "failedTests")))
            {
                if (PsValue.Str(PsValue.Prop(failed, "suite")) == suite && PsValue.Str(PsValue.Prop(failed, "className")) == className
                    && PsValue.Str(PsValue.Prop(failed, "method")) == method) return false;
            }
            return true;
        }

        private static Containment DecisionHit(GitProof git, Loaded doc, List<string> fixes)
        {
            if (git == null) return null;
            foreach (var fix in fixes)
            {
                var hit = git.Contains(doc.Sha, fix);
                if (hit.Contained) return hit;
            }
            return null;
        }

        private static int ByIdentity(OrderedDictionary a, OrderedDictionary b)
        {
            var c = string.CompareOrdinal((string)a["suite"], (string)b["suite"]);
            if (c == 0) c = string.CompareOrdinal((string)a["className"], (string)b["className"]);
            if (c == 0) c = string.CompareOrdinal((string)a["method"], (string)b["method"]);
            return c;
        }

        // suite|class|method <TAB> sha[,sha...] per line; blank lines and # comments skipped; ASCII only.
        private static Dictionary<string, List<string>> ReadDecisions(string decision)
        {
            var decisions = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(decision)) return decisions;
            if (!File.Exists(decision)) throw Refuse("decision-missing");
            foreach (var b in File.ReadAllBytes(decision)) if (b > 127) throw Refuse("decision-not-ascii");
            foreach (var line in File.ReadAllLines(decision))
            {
                var trim = line.Trim();
                if (trim.Length == 0 || trim.StartsWith("#", StringComparison.Ordinal)) continue;
                var tab = trim.IndexOf('\t');
                if (tab < 1) throw Refuse("decision-row");
                var parts = trim.Substring(0, tab).Split('|');
                var shaText = trim.Substring(tab + 1).Trim();
                if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[2])) throw Refuse("decision-row");
                var key = parts[0] + "|" + parts[1] + "|" + parts[2];
                if (!decisions.TryGetValue(key, out var shas)) decisions[key] = shas = new List<string>();
                if (string.IsNullOrWhiteSpace(shaText)) throw Refuse("decision-sha");
                foreach (var piece in shaText.Split(','))
                {
                    var one = piece.Trim();
                    if (!Sha40.IsMatch(one)) throw Refuse("decision-sha");
                    if (!shas.Contains(one)) shas.Add(one);
                }
            }
            return decisions;
        }

        /// <summary>The git proof against one repository, with each (source, fix) answer cached.</summary>
        private sealed class GitProof
        {
            private readonly string _repo;
            private readonly Dictionary<string, Containment> _cache = new Dictionary<string, Containment>(StringComparer.Ordinal);

            public GitProof(string repo) { _repo = repo; }

            // Every SHA names a commit in the repository: one cat-file process for all of them.
            public void RequireCommits(List<string> shas)
            {
                var input = new StringBuilder();
                foreach (var sha in shas) input.Append(sha).Append("^{commit}\n");
                var result = Git(input.ToString(), "cat-file", "--batch-check");
                if (result.Code != 0) throw Refuse("repo-sha-unresolved");
                var answers = result.Output.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (answers.Length != shas.Count) throw Refuse("repo-sha-unresolved");
                foreach (var answer in answers) if (!CommitLine.IsMatch(answer)) throw Refuse("repo-sha-unresolved");
            }

            // The fix is an ancestor of the source, or the source's history carries its cherry-pick trailer.
            public Containment Contains(string sourceSha, string fixSha)
            {
                var key = sourceSha + "|" + fixSha;
                if (_cache.TryGetValue(key, out var hit)) return hit;
                var ancestor = Git(null, "merge-base", "--is-ancestor", fixSha, sourceSha).Code;
                if (ancestor == 0) hit = new Containment { Contained = true, Commit = fixSha };
                else if (ancestor != 1) throw Refuse("repo-sha-unresolved");
                else
                {
                    var found = Git(null, "log", "-1", "--format=%H", sourceSha, "-E", "--grep=^\\(cherry picked from commit " + fixSha + "\\)$");
                    if (found.Code != 0) throw Refuse("repo-sha-unresolved");
                    var text = found.Output.Trim();
                    hit = Sha40.IsMatch(text) ? new Containment { Contained = true, Commit = text } : new Containment { Contained = false };
                }
                _cache[key] = hit;
                return hit;
            }

            private (int Code, string Output) Git(string stdin, params string[] args)
            {
                var start = new ProcessStartInfo("git")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin != null,
                };
                start.ArgumentList.Add("-C");
                start.ArgumentList.Add(_repo);
                foreach (var arg in args) start.ArgumentList.Add(arg);
                start.Environment["GIT_TERMINAL_PROMPT"] = "0";
                using (var process = Process.Start(start))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (stdin != null)
                    {
                        process.StandardInput.Write(stdin);
                        process.StandardInput.Close();
                    }
                    process.WaitForExit();
                    error.Wait();
                    return (process.ExitCode, output.Result);
                }
            }
        }
    }
}
