// The stopped-run seed's files: the builder (Export, behind export-nightly-stopped-run-seed), the reader, the content
// hash, the summary prefix and the timing root. Scripts call these directly; there is no PowerShell layer.
//
// Same checks, same refusal texts: the walk and every read are link-checked (HistoryLinkCache), every input is hashed
// before and after it is read and again before the seed is written (chunk-file drift marks that chunk unfinished; plan,
// owner and summary-prefix drift refuses), and the output must lie outside the run and timing directories. The seed's
// JSON is written by System.Text.Json, so its layout is not ConvertTo-Json's; its content hash is the same projection
// (Newtonsoft's escaping of seven strings), so seeds written before still verify. The records are PsValue records
// (PSObjects under PowerShell, PlainObjects for a plain caller such as the in-process tests).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Antiphon.Nightly
{
    /// <summary>
    /// The stopped-run seed's files. Each method is one call from a script; a refusal is thrown as NightlyRefusal (its
    /// message the reason). Link checks go through the process's HistoryLinkCache.
    /// </summary>
    public static class StoppedSeedFiles
    {
        /// <summary>Tests: runs after each export input's text is read (given its full path) and before its digest is taken again.</summary>
        public static Action<string> ReadBarrier { get; set; }

        private const string Format = "antiphon-nightly-stopped-seed-v1";
        private const string Kind = "operator-seed-from-stopped-run";
        private const int PrefixLimit = 33554432;
        private static readonly Regex Sha40 = new Regex("^[a-f0-9]{40}$");
        private static readonly Regex Sha64 = new Regex("^[a-f0-9]{64}$");
        private static readonly Regex SuiteId = new Regex("^[a-z][a-z0-9-]*$");
        private static readonly Regex ChunkId = new Regex("^[A-Za-z0-9_-]+$");
        private static readonly Regex TimingName = new Regex("^timing-.+$");
        private static readonly Regex ShortRunId = new Regex("-([A-Za-z0-9]{8})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex OwnerFile = new Regex(@"^.*owner.*\.json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex OrphanTrx = new Regex(@"^(antiphon|session-runner|pty-host|agents-pty|messaging|e2e|markdown-pdf|observability|server-git|resilience|git-in-process)-([A-Za-z0-9_-]+)\.trx$");
        private static readonly string[] FailedOutcomes = { "Failed", "Error", "Timeout", "Aborted" };
        private static readonly string[] ClosedSummaries = { "Completed", "Passed", "Failed", "Error", "Timeout", "Aborted" };
        private static readonly string[] RanToEnd = { "Completed", "Passed", "Failed" };
        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // ---- hash, read ----

        /// <summary>
        /// The seed content hash: SHA-256 of the compact JSON array of format, seedKind, sourceRunId,
        /// sourceCandidateId, sourceSha, createdAtUtc (UTC, round-trip) and payload, strings escaped as Newtonsoft does.
        /// </summary>
        public static string ContentHash(object envelope)
        {
            var stamp = StoppedSeed.Utc(PsValue.Prop(envelope, "createdAtUtc"));
            if (stamp == null) throw new NightlyRefusal("seed-timestamp-missing");
            var parts = new[]
            {
                PsValue.Str(PsValue.Prop(envelope, "format")), PsValue.Str(PsValue.Prop(envelope, "seedKind")),
                PsValue.Str(PsValue.Prop(envelope, "sourceRunId")), PsValue.Str(PsValue.Prop(envelope, "sourceCandidateId")),
                PsValue.Str(PsValue.Prop(envelope, "sourceSha")), stamp.Value.ToString("o", CultureInfo.InvariantCulture),
                PsValue.Str(PsValue.Prop(envelope, "payload")),
            };
            var sb = new StringBuilder(256 + parts[6].Length);
            sb.Append('[');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(',');
                TestPhaseRecords.AppendJsonString(sb, parts[i]);
            }
            sb.Append(']');
            return TestPhaseRecords.Sha256Text(sb.ToString());
        }

        /// <summary>
        /// Reads a seed: the payload with its envelope identity (seedKind, sourceRunId, sourceCandidateId, sourceSha,
        /// createdAtUtc), one member per failed suite and class, and no unresolved rows. expectedDigest, when given, is
        /// the file's required SHA-256.
        /// </summary>
        public static object Read(string path, string expectedDigest) =>
            ReadContent(path, expectedDigest, HistoryLinkCache.Current, HistoryNodeIdentity.ForceWindows);

        internal static object ReadContent(string path, string expectedDigest, HistoryLinkCache links, bool forceWindows)
        {
            NoLink(links, path, forceWindows);
            var digest = Sha(path);
            if (!string.IsNullOrEmpty(expectedDigest) && !string.Equals(digest, expectedDigest, StringComparison.Ordinal)) throw new NightlyRefusal("history-digest-mismatch");
            var envelope = NightlyJson.ParseText(File.ReadAllText(path), 1024, true) ?? throw new NightlyRefusal("seed-schema");
            var payload = PsValue.Prop(envelope, "payload");
            if (!PsOps.StrEqValue(PsValue.Prop(envelope, "format"), Format) || !PsOps.StrEqValue(PsValue.Prop(envelope, "seedKind"), Kind)
                || !Sha40.IsMatch(PsValue.Str(PsValue.Prop(envelope, "sourceSha")))
                || string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(envelope, "sourceRunId")))
                || string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(envelope, "sourceCandidateId")))
                || !(PsOps.Base(payload) is string payloadText)) throw new NightlyRefusal("seed-schema");
            var contentHash = PsValue.Str(PsValue.Prop(envelope, "contentHash"));
            string actualHash;
            try { actualHash = ContentHash(envelope); }
            catch (NightlyRefusal) { throw new NightlyRefusal("seed-hash-mismatch"); }
            if (!Sha64.IsMatch(contentHash) || !string.Equals(actualHash, contentHash, StringComparison.Ordinal)) throw new NightlyRefusal("seed-hash-mismatch");
            var content = NightlyJson.ParseText(payloadText, 1024, true) ?? throw new NightlyRefusal("seed-schema");
            foreach (var field in new[] { "seedKind", "sourceRunId", "sourceCandidateId", "sourceSha" })
                PsValue.SetNote(content, field, PsValue.Str(PsValue.Prop(envelope, field)));
            PsValue.SetNote(content, "createdAtUtc", PsValue.Prop(envelope, "createdAtUtc"));
            StoppedSeed.AssertContent(content);
            // S1's resolver remains the single class -> new-plan chunk mapper: one member per suite and class, in the
            // order their first failure appears, observed in each failure's chunk.
            var members = new List<object>();
            var groups = new Dictionary<string, List<object>>(StringComparer.Ordinal);
            var firsts = new List<(string Key, object Test)>();
            foreach (var test in PsValue.AsArray(PsValue.Prop(content, "failedTests")))
            {
                var key = PsValue.Str(PsValue.Prop(test, "suite")) + "\0" + PsValue.Str(PsValue.Prop(test, "className"));
                if (!groups.TryGetValue(key, out var observations))
                {
                    observations = new List<object>();
                    groups[key] = observations;
                    firsts.Add((key, test));
                }
                observations.Add(PsValue.Record(("chunk", PsValue.Prop(test, "chunk"))));
            }
            foreach (var (key, test) in firsts)
                members.Add(PsValue.Record(("suite", PsValue.Prop(test, "suite")), ("className", PsValue.Prop(test, "className")),
                    ("reasons", new object[] { "stopped-run-failure" }), ("observations", groups[key].ToArray())));
            SetNote(content, "members", members.ToArray());
            SetNote(content, "unresolved", new object[0]);
            if (!string.Equals(Sha(path), digest, StringComparison.Ordinal)) throw new NightlyRefusal("seed-changed-during-read");
            return content;
        }

        // ---- summary prefix, timing root ----

        /// <summary>{ Document, Sha, Length } of a summary before its first bulk key (timingChunks, suiteTimeline, suites).</summary>
        public static object SummaryPrefix(string path)
        {
            var prefix = ReadPrefix(path);
            return PsValue.Record(("Document", prefix.Document), ("Sha", prefix.Sha), ("Length", prefix.Length));
        }

        private sealed class Prefix
        {
            public object Document;
            public string Sha;
            public int Length;
        }

        private static Prefix ReadPrefix(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var buf = new byte[65536];
                var data = new MemoryStream();
                var scanner = new HistorySummaryPrefix.Scanner();
                var cut = -1;
                while (data.Length < PrefixLimit)
                {
                    var n = fs.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    var offset = (int)data.Length;
                    data.Write(buf, 0, n);
                    cut = scanner.Feed(buf, n, offset);
                    if (cut >= 0) break;
                }
                var all = data.ToArray();
                if (cut < 0)
                {
                    if (all.Length >= PrefixLimit) throw new NightlyRefusal("seed-summary-header");
                    if (fs.Read(buf, 0, 1) > 0) throw new NightlyRefusal("seed-summary-header");
                    cut = all.Length;
                }
                var prefix = new byte[cut];
                Array.Copy(all, prefix, cut);
                var text = new UTF8Encoding(false).GetString(prefix).Trim();
                if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
                text = text.TrimEnd().TrimEnd(',');
                if (!text.EndsWith("}", StringComparison.Ordinal)) text += "}";
                var document = NightlyJson.ParseText(text, 1024, true) ?? throw new NightlyRefusal("seed-summary-header");
                return new Prefix { Document = document, Sha = TestPhaseRecords.Sha256Hex(prefix), Length = cut };
            }
        }

        private static string PrefixSha(string path, int length)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var prefix = new byte[length];
                var got = 0;
                while (got < length)
                {
                    var n = fs.Read(prefix, got, length - got);
                    if (n <= 0) throw new NightlyRefusal("seed-summary-tampered");
                    got += n;
                }
                return TestPhaseRecords.Sha256Hex(prefix);
            }
        }

        /// <summary>The run's timing directory: a real directory beside the run directory, named timing-&lt;run id&gt;.</summary>
        public static string TimingRoot(string runRoot, string timingPath, string runId) =>
            ResolveTimingRoot(runRoot, timingPath, runId, HistoryNodeIdentity.ForceWindows);

        private static string ResolveTimingRoot(string runRoot, string timingPath, string runId, bool forceWindows)
        {
            if (string.IsNullOrWhiteSpace(timingPath)) throw new NightlyRefusal("seed-timing-missing");
            var full = HistoryLinkCache.FullPath(timingPath);
            var id = HistoryNodeIdentity.Read(full, forceWindows);
            if (id.Missing) throw new NightlyRefusal("seed-timing-missing");
            if (id.Unreadable) throw new NightlyRefusal("evidence-unreadable");
            if (id.Linked || !id.Directory) throw new NightlyRefusal("evidence-link");
            var parent = HistoryLinkCache.FullPath(Path.GetDirectoryName(HistoryLinkCache.FullPath(runRoot)));
            var timingParent = HistoryLinkCache.FullPath(Path.GetDirectoryName(full));
            if (!timingParent.Equals(parent, PathComparison)) throw new NightlyRefusal("seed-timing-outside");
            if (!UnderRoot(full, parent)) throw new NightlyRefusal("seed-timing-outside");
            if (full.Equals(HistoryLinkCache.FullPath(runRoot), PathComparison)) throw new NightlyRefusal("seed-timing-outside");
            var name = Path.GetFileName(full);
            if (!TimingName.IsMatch(name)) throw new NightlyRefusal("seed-timing-run-mismatch");
            if (!string.IsNullOrEmpty(runId) && !string.Equals(name.Substring(7), runId, StringComparison.Ordinal)) throw new NightlyRefusal("seed-timing-run-mismatch");
            return full;
        }

        // The run's timing sibling: timing-<run id>, else the one timing-<8-character short id>* directory.
        private static string FindTimingSibling(string runRoot, string runId, bool forceWindows)
        {
            var parent = Path.GetDirectoryName(HistoryLinkCache.FullPath(runRoot));
            if (!string.IsNullOrEmpty(runId))
            {
                var named = Path.Combine(parent, "timing-" + runId);
                var id = HistoryNodeIdentity.Read(named, forceWindows);
                if (!id.Missing && id.Directory && !id.Linked) return HistoryLinkCache.FullPath(named);
            }
            var match = ShortRunId.Match(Path.GetFileName(HistoryLinkCache.FullPath(runRoot)));
            if (!match.Success) return null;
            var shortId = match.Groups[1].Value;
            var sibling = new Regex("^timing-" + Regex.Escape(shortId) + "[A-Za-z0-9]*$");
            var hits = new List<string>();
            foreach (var dir in Directory.EnumerateDirectories(parent))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith("timing-" + shortId, StringComparison.Ordinal) && sibling.IsMatch(name)) hits.Add(HistoryLinkCache.FullPath(dir));
            }
            if (hits.Count > 1) throw new NightlyRefusal("seed-timing-ambiguous");
            return hits.Count == 1 ? hits[0] : null;
        }

        // ---- export ----

        /// <summary>
        /// Builds an ordering-only seed from a stopped run's retained logs and returns { Path, Digest }. The run
        /// directory is read-only; the output must be outside it and its timing directory. Source identity the owner
        /// files and summary do not carry comes from the arguments; a conflict refuses. The process link cache restarts.
        /// </summary>
        public static object Export(string runDirectory, string outputPath, string sourceRunId, string sourceCandidateId,
            string sourceSha, string timingRoot)
        {
            HistoryLinkCache.Reset();
            var export = new SeedExport(HistoryLinkCache.Current, HistoryNodeIdentity.ForceWindows, ReadBarrier)
            {
                SourceRunId = sourceRunId ?? "", SourceCandidateId = sourceCandidateId ?? "", SourceSha = sourceSha ?? "",
            };
            var path = export.Run(runDirectory, outputPath, timingRoot ?? "");
            return PsValue.Record(("Path", path), ("Digest", Sha(path)));
        }

        private sealed class SeedExport
        {
            private readonly HistoryLinkCache _links;
            private readonly bool _force;
            private readonly Action<string> _barrier;
            private readonly Dictionary<string, string> _inputs = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _chunkInputs = new Dictionary<string, string>(StringComparer.Ordinal);
            private string _root;
            private string _timing;

            public string SourceRunId, SourceCandidateId, SourceSha;

            public SeedExport(HistoryLinkCache links, bool forceWindows, Action<string> readBarrier)
            {
                _links = links ?? new HistoryLinkCache();
                _force = forceWindows;
                _barrier = readBarrier;
            }

            public string Run(string runDirectory, string outputPath, string timingRoot)
            {
                if (string.IsNullOrWhiteSpace(runDirectory) || string.IsNullOrWhiteSpace(outputPath)) throw new NightlyRefusal("seed-path-required");
                _root = HistoryLinkCache.FullPath(runDirectory);
                if (string.IsNullOrEmpty(_root) || string.Equals(_root, Path.GetPathRoot(_root), StringComparison.Ordinal)) throw new NightlyRefusal("seed-source-root-refused");
                var output = Path.GetFullPath(outputPath);
                if (UnderRoot(output, _root)) throw new NightlyRefusal("seed-output-inside-source");
                NoLink(_links, _root, _force);
                NoLink(_links, output, _force);
                if (!Directory.Exists(_root)) throw new NightlyRefusal("seed-source-missing");
                if (File.Exists(output)) throw new NightlyRefusal("seed-output-exists");

                Prefix summaryPrefix = null;
                var summaryPath = Path.Combine(_root, "summary.json");
                var declaredTiming = "";
                if (File.Exists(summaryPath))
                {
                    NoLink(_links, summaryPath, _force);
                    summaryPrefix = ReadPrefix(summaryPath);
                    var summary = summaryPrefix.Document;
                    Adopt(summary, "runId", ref SourceRunId, "seed-summary-run-mismatch");
                    Adopt(summary, "sha", ref SourceSha, "seed-summary-sha-mismatch");
                    Adopt(summary, "candidateId", ref SourceCandidateId, "seed-summary-candidate-mismatch");
                    declaredTiming = PsValue.Str(PsValue.Prop(summary, "timingRoot"));
                }
                var inRunPlans = 0;
                foreach (var name in Directory.EnumerateFiles(_root))
                {
                    var file = Path.GetFileName(name);
                    if (IsPlanFile(file) || string.Equals(file, "groups-planned.json", StringComparison.Ordinal)) inRunPlans++;
                }
                if (!string.IsNullOrWhiteSpace(declaredTiming))
                {
                    if (!string.IsNullOrEmpty(timingRoot) && !string.Equals(HistoryLinkCache.FullPath(timingRoot), HistoryLinkCache.FullPath(declaredTiming), StringComparison.Ordinal))
                        throw new NightlyRefusal("seed-timing-conflict");
                    _timing = ResolveTimingRoot(_root, declaredTiming, SourceRunId, _force);
                }
                else if (!string.IsNullOrEmpty(timingRoot)) _timing = ResolveTimingRoot(_root, timingRoot, SourceRunId, _force);
                else if (inRunPlans == 0)
                {
                    var found = FindTimingSibling(_root, SourceRunId, _force);
                    if (found != null) _timing = ResolveTimingRoot(_root, found, SourceRunId, _force);
                }
                if (_timing != null && string.IsNullOrEmpty(SourceRunId))
                {
                    SourceRunId = Path.GetFileName(_timing).Substring(7);
                    _timing = ResolveTimingRoot(_root, _timing, SourceRunId, _force);
                }
                if (_timing != null && UnderRoot(output, _timing)) throw new NightlyRefusal("seed-output-inside-source");

                // The run directory and, when plans live beside it, the timing sibling: every entry link-checked.
                var walk = _links.Walk(_timing != null ? new[] { _root, _timing } : new[] { _root }, _force);
                if (walk.Refusal != null) throw new NightlyRefusal(walk.Refusal);
                var files = walk.Files;
                var planRoot = _timing ?? _root;
                var plans = new List<HistoryWalkFile>();
                foreach (var file in files) if (string.Equals(file.DirectoryName, planRoot, StringComparison.Ordinal) && IsPlanFile(file.Name)) plans.Add(file);
                plans.Sort((a, b) => ByName(a.FullName, b.FullName));
                if (plans.Count == 0)
                    foreach (var file in files) if (string.Equals(file.DirectoryName, planRoot, StringComparison.Ordinal) && string.Equals(file.Name, "groups-planned.json", StringComparison.Ordinal)) plans.Add(file);
                if (plans.Count == 0) throw new NightlyRefusal("seed-plan-missing");

                var observed = DateTimeOffset.MinValue;
                if (summaryPrefix != null)
                    foreach (var field in new[] { "startedAt", "completedAt" }) Later(ref observed, StoppedSeed.Utc(PsValue.Prop(summaryPrefix.Document, field)));
                foreach (var ownerFile in files)
                {
                    if (!string.Equals(ownerFile.DirectoryName, _root, StringComparison.Ordinal) || !OwnerFile.IsMatch(ownerFile.Name)) continue;
                    var owner = NightlyJson.ParseText(ReadInput(ownerFile.FullName, false), 1024, true) ?? throw new NightlyRefusal("seed-owner-schema");
                    Claim(owner, "runId", ref SourceRunId);
                    Claim(owner, "candidateId", ref SourceCandidateId);
                    Claim(owner, "sha", ref SourceSha);
                    Later(ref observed, StoppedSeed.Utc(PsValue.Prop(owner, "startedAt")));
                }

                var documents = new List<object>();
                var plannedKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var file in plans)
                {
                    var document = NightlyJson.ParseText(ReadInput(file.FullName, false), 1024, true) ?? throw new NightlyRefusal("seed-plan-schema");
                    documents.Add(document);
                    foreach (var group in PsValue.AsArray(PsValue.Prop(document, "groups")))
                        plannedKeys.Add(PsValue.Str(PsValue.Prop(document, "suite")) + "/" + PsValue.Str(PsValue.Prop(group, "chunk")));
                }
                if (string.IsNullOrEmpty(SourceRunId)) SourceRunId = PsValue.Str(PsValue.Prop(documents[0], "runId"));
                if (string.IsNullOrEmpty(SourceSha)) SourceSha = PsValue.Str(PsValue.Prop(documents[0], "sourceDigest"));
                // A stopped run can retain a TRX whose historical group row is missing. Keep its identities
                // independently of plan lookup; missing membership elsewhere must never erase a resolvable failure. Only
                // supervisor per-chunk names at the supplied root are admitted, never nested discovery/diagnostic TRX files.
                var trxFiles = new List<HistoryWalkFile>();
                foreach (var file in files)
                    if (string.Equals(file.DirectoryName, _root, StringComparison.OrdinalIgnoreCase) && string.Equals(file.Extension, ".trx", StringComparison.Ordinal)) trxFiles.Add(file);
                trxFiles.Sort((a, b) => ByName(a.Name, b.Name));
                foreach (var trxFile in trxFiles)
                {
                    var match = OrphanTrx.Match(trxFile.Name);
                    if (!match.Success) continue;
                    var orphanSuite = match.Groups[1].Value;
                    var orphanChunk = match.Groups[2].Value;
                    if (plannedKeys.Contains(orphanSuite + "/" + orphanChunk)) continue;
                    var orphanClasses = new List<object>();
                    try
                    {
                        var rows = TrxRows(ReadInput(trxFile.FullName, false));
                        foreach (var row in rows) orphanClasses.Add(NightlyClassNames.Bare(PsValue.Str(PsValue.Prop(row, "ClassName"))));
                    }
                    catch (Exception e) when (!Message(e).StartsWith("seed-", StringComparison.Ordinal)) { }
                    var group = PsValue.Record(("chunk", orphanChunk), ("classes", DispatchSchedule.OrdinalStrings(orphanClasses)),
                        ("plannedTestIds", new object[0]), ("fromTrxOnly", true));
                    documents.Add(PsValue.Record(("version", 1), ("runId", SourceRunId), ("sourceDigest", SourceSha), ("suite", orphanSuite),
                        ("groups", new object[] { group })));
                }

                var chunks = new List<OrderedDictionary>();
                var failed = new List<OrderedDictionary>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var failedKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var plan in documents)
                {
                    if (!PsValue.Eq(PsValue.Prop(plan, "version"), 1, false) || !PsValue.IsTrue(PsValue.Prop(plan, "suite"))
                        || !PsOps.IsArray(PsValue.Prop(plan, "groups"))) throw new NightlyRefusal("seed-plan-schema");
                    var planRun = PsValue.Str(PsValue.Prop(plan, "runId"));
                    var planSha = PsValue.Str(PsValue.Prop(plan, "sourceDigest"));
                    if (!string.IsNullOrEmpty(SourceRunId) && !string.Equals(planRun, SourceRunId, StringComparison.Ordinal)) throw new NightlyRefusal("seed-plan-run-mismatch");
                    if (!string.IsNullOrEmpty(SourceSha) && !string.Equals(planSha, SourceSha, StringComparison.Ordinal)) throw new NightlyRefusal("seed-plan-source-mismatch");
                    SourceRunId = planRun;
                    SourceSha = planSha;
                    foreach (var group in PsValue.AsArray(PsValue.Prop(plan, "groups")))
                    {
                        var row = ChunkRow(plan, group, seen, ref observed, out var chunkFailed, out var dropFailures);
                        if (!dropFailures)
                        {
                            foreach (var item in chunkFailed)
                            {
                                var failureKey = row["suite"] + "/" + row["chunk"] + "\0" + item["className"] + "\0" + item["method"];
                                if (failedKeys.Add(failureKey)) failed.Add(item);
                            }
                        }
                        chunks.Add(row);
                    }
                }
                if (string.IsNullOrEmpty(SourceRunId) || string.IsNullOrEmpty(SourceCandidateId) || !Sha40.IsMatch(SourceSha ?? "") || observed == DateTimeOffset.MinValue)
                    throw new NightlyRefusal("seed-source-identity-or-time-missing: supply source candidate/run/sha where owner metadata is absent");

                // Chunk-file drift marks that chunk unfinished. Plan, owner and summary-prefix drift still refuse the export.
                foreach (var path in new List<string>(_inputs.Keys))
                {
                    if (string.Equals(Sha(path), _inputs[path], StringComparison.Ordinal)) continue;
                    if (!_chunkInputs.TryGetValue(path, out var changedKey)) throw new NightlyRefusal("seed-source-changed");
                    _inputs.Remove(path);
                    var kind = path.EndsWith(".trx", StringComparison.Ordinal) ? "trx-changed" : "timing-changed";
                    foreach (var row in chunks)
                    {
                        if (!string.Equals(row["suite"] + "/" + row["chunk"], changedKey, StringComparison.Ordinal)) continue;
                        row["completed"] = false;
                        var prior = row.Contains("unfinishedReason") ? (string)row["unfinishedReason"] : "";
                        row["unfinishedReason"] = string.IsNullOrEmpty(prior) ? kind : prior + "," + kind;
                    }
                    if (kind == "trx-changed") failed.RemoveAll(item => string.Equals(item["suite"] + "/" + item["chunk"], changedKey, StringComparison.Ordinal));
                }
                if (summaryPrefix != null && !string.Equals(PrefixSha(summaryPath, summaryPrefix.Length), summaryPrefix.Sha, StringComparison.Ordinal))
                    throw new NightlyRefusal("seed-summary-tampered");

                var evidencePaths = new List<string>(_inputs.Keys);
                evidencePaths.Sort(ByName);
                var evidence = new List<object>();
                foreach (var path in evidencePaths)
                {
                    var row = new OrderedDictionary { { "path", EvidencePath(path) }, { "sha256", _inputs[path] } };
                    evidence.Add(row);
                }
                var payload = new OrderedDictionary
                {
                    { "sourceObservedAtUtc", observed.ToString("o", CultureInfo.InvariantCulture) },
                    { "chunks", chunks.ToArray() },
                    { "failedTests", failed.ToArray() },
                    { "evidence", evidence.ToArray() },
                };
                var envelope = new OrderedDictionary
                {
                    { "format", Format }, { "seedKind", Kind }, { "sourceRunId", SourceRunId }, { "sourceCandidateId", SourceCandidateId },
                    { "sourceSha", SourceSha }, { "createdAtUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                    { "payload", SeedJson.Text(payload, false) }, { "contentHash", "" },
                };
                envelope["contentHash"] = ContentHash(envelope);
                var parent = Path.GetDirectoryName(output);
                Directory.CreateDirectory(parent);
                NoLink(_links, parent, _force);
                var bytes = SeedJson.Bytes(envelope, true);
                using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                return output;
            }

            // One planned group's row; its failures in chunkFailed (dropFailures when the TRX cannot be trusted).
            private OrderedDictionary ChunkRow(object plan, object group, HashSet<string> seen, ref DateTimeOffset observed,
                out List<OrderedDictionary> chunkFailed, out bool dropFailures)
            {
                var suite = PsValue.Str(PsValue.Prop(plan, "suite"));
                var chunk = PsValue.Str(PsValue.Prop(group, "chunk"));
                var key = suite + "/" + chunk;
                if (!SuiteId.IsMatch(suite) || !ChunkId.IsMatch(chunk)) throw new NightlyRefusal("seed-plan-identity");
                if (!seen.Add(key)) throw new NightlyRefusal("seed-plan-duplicate");
                var planned = new List<object>();
                foreach (var cls in PsValue.AsArray(PsValue.Prop(group, "classes"))) planned.Add(NightlyClassNames.Bare(PsValue.Str(cls)));
                var classes = DispatchSchedule.OrdinalStrings(planned);
                var stem = suite + "-" + chunk;
                object timing = null;
                DateTimeOffset? last = null;
                var completed = false;
                var started = false;
                var reasons = new List<string>();
                chunkFailed = new List<OrderedDictionary>();

                var timingPath = HistoryLinkCache.FullPath(Path.Combine(_root, stem + ".timing.json"));
                if (ChunkPresent(timingPath))
                {
                    started = true;
                    try
                    {
                        timing = NightlyJson.ParseText(ReadInput(timingPath, true), 1024, true) ?? throw new InvalidDataException("timing-json");
                        _chunkInputs[timingPath] = key;
                        if (!PsOps.StrEqValue(PsValue.Prop(timing, "suite"), suite) || !PsOps.StrEqValue(PsValue.Prop(timing, "chunk"), chunk)) AddReason(reasons, "timing-identity");
                        else
                        {
                            foreach (var field in new[] { "startedAt", "completedAt", "lastPhaseUtc" }) Later(ref observed, StoppedSeed.Utc(PsValue.Prop(timing, field)));
                            last = StoppedSeed.Utc(PsValue.Prop(timing, "startedAt"));
                        }
                    }
                    catch (Exception e) { ChunkReadFailed("timing", Message(e), reasons); }
                }
                var trxPath = HistoryLinkCache.FullPath(Path.Combine(_root, stem + ".trx"));
                if (ChunkPresent(trxPath))
                {
                    started = true;
                    try
                    {
                        var trxText = ReadInput(trxPath, true);
                        _chunkInputs[trxPath] = key;
                        // A file still being appended has no closing tag. Do not apply the ResultSummary closed guard to it.
                        if (trxText.IndexOf("</TestRun>", StringComparison.Ordinal) < 0) AddReason(reasons, "trx-open");
                        else
                        {
                            var xml = LoadXml(trxText);
                            var rows = IdentityRows(xml);
                            if (classes.Length == 0)
                            {
                                var fromRows = new List<object>();
                                foreach (var row in rows) fromRows.Add(NightlyClassNames.Bare(PsValue.Str(PsValue.Prop(row, "ClassName"))));
                                classes = DispatchSchedule.OrdinalStrings(fromRows);
                            }
                            var terminal = xml.SelectSingleNode("//*[local-name()='ResultSummary']") as XmlElement;
                            var times = xml.SelectSingleNode("//*[local-name()='Times']") as XmlElement;
                            DateTimeOffset? finish = null;
                            if (times != null) finish = StoppedSeed.Utc(times.GetAttribute("finish"));
                            if (finish == null && timing != null) finish = StoppedSeed.Utc(PsValue.Prop(timing, "completedAt"));
                            if (finish != null) { last = finish; Later(ref observed, finish); }
                            var summaryOutcome = terminal != null ? terminal.GetAttribute("outcome") : "";
                            var closed = Array.IndexOf(ClosedSummaries, summaryOutcome) >= 0;
                            // MTP writes an explicit [Skip] as NotExecuted ('Skipped' in other writers). A test that never
                            // started has no row, which the planned-count check covers. Skips count as completed only under a
                            // summary that ran to the end; any other outcome stays unfinished.
                            var finished = new List<string> { "Passed", "Failed", "Error", "Timeout", "Aborted" };
                            if (Array.IndexOf(RanToEnd, summaryOutcome) >= 0) { finished.Add("NotExecuted"); finished.Add("Skipped"); }
                            var allFinished = true;
                            foreach (var row in rows) if (!finished.Contains(PsValue.Str(PsValue.Prop(row, "Outcome")))) { allFinished = false; break; }
                            // closed stays in this conjunction so a missing ResultSummary cannot count as completed.
                            completed = closed && finish != null && rows.Length > 0 && rows.Length >= PsValue.AsArray(PsValue.Prop(group, "plannedTestIds")).Length && allFinished;
                            if (!closed) AddReason(reasons, "trx-result-open");
                            foreach (var row in rows)
                            {
                                var cls = NightlyClassNames.Bare(PsValue.Str(PsValue.Prop(row, "ClassName")));
                                if (Array.IndexOf(classes, cls) < 0) throw new NightlyRefusal("seed-trx-class-unplanned");
                                var outcome = PsValue.Str(PsValue.Prop(row, "Outcome"));
                                if (Array.IndexOf(FailedOutcomes, outcome) >= 0)
                                    chunkFailed.Add(new OrderedDictionary
                                    {
                                        { "suite", suite }, { "chunk", chunk }, { "className", cls },
                                        { "method", PsValue.Str(PsValue.Prop(row, "MethodName")) }, { "outcome", outcome },
                                    });
                            }
                        }
                    }
                    catch (Exception e) { ChunkReadFailed("trx", Message(e), reasons); }
                }
                var force = false;
                dropFailures = false;
                foreach (var reason in reasons)
                {
                    if (reason != "trx-result-open") force = true;
                    if (reason == "trx-unreadable" || reason == "trx-changed" || reason == "trx-open") dropFailures = true;
                }
                if (force) completed = false;
                var chunkRow = new OrderedDictionary
                {
                    { "suite", suite }, { "chunk", chunk }, { "classes", classes },
                    { "membershipSource", PsValue.IsTrue(PsValue.Prop(group, "fromTrxOnly")) ? "trx-without-plan-row" : "plan" },
                    { "completed", completed }, { "started", started },
                    { "lastRunAtUtc", last.HasValue ? last.Value.ToString("o", CultureInfo.InvariantCulture) : null },
                };
                if (reasons.Count > 0) chunkRow["unfinishedReason"] = string.Join(",", reasons);
                return chunkRow;
            }

            private static void ChunkReadFailed(string kind, string message, List<string> reasons)
            {
                if (message == "seed-source-changed") { AddReason(reasons, kind + "-changed"); return; }
                if (message == "seed-chunk-unreadable" || message == "evidence-unreadable") { AddReason(reasons, kind + "-unreadable"); return; }
                if (message.StartsWith("seed-", StringComparison.Ordinal) || message == "evidence-link") throw new NightlyRefusal(message);
                AddReason(reasons, kind + "-unreadable");
            }

            private static void AddReason(List<string> reasons, string reason)
            {
                if (!string.IsNullOrEmpty(reason) && !reasons.Contains(reason)) reasons.Add(reason);
            }

            private bool ChunkPresent(string path)
            {
                var id = HistoryNodeIdentity.Read(path, _force);
                if (id.Missing) return false;
                if (id.Linked) throw new NightlyRefusal("evidence-link");
                return true;
            }

            // Read-SeedFile: an input under the run or timing root, link-checked, hashed before and after its text is read.
            private string ReadInput(string path, bool tolerate)
            {
                var full = HistoryLinkCache.FullPath(path);
                if (!UnderRoot(full, _root) && !(_timing != null && UnderRoot(full, _timing))) throw new NightlyRefusal("seed-input-outside-source");
                NoLink(_links, full, _force);
                string digest, text;
                try
                {
                    digest = Sha(full);
                    text = File.ReadAllText(full);
                    _barrier?.Invoke(full);
                    if (!string.Equals(Sha(full), digest, StringComparison.Ordinal)) throw new NightlyRefusal("seed-source-changed");
                }
                catch (Exception e)
                {
                    var message = Message(e);
                    if (message.StartsWith("seed-", StringComparison.Ordinal) || message == "evidence-link") throw new NightlyRefusal(message);
                    if (tolerate) throw new NightlyRefusal("seed-chunk-unreadable");
                    throw;
                }
                if (_inputs.TryGetValue(full, out var prior) && !string.Equals(prior, digest, StringComparison.Ordinal)) throw new NightlyRefusal("seed-source-changed");
                _inputs[full] = digest;
                return text;
            }

            private string EvidencePath(string full)
            {
                if (UnderRoot(full, _root)) return full.Substring(_root.Length).TrimStart('\\', '/');
                if (_timing != null && UnderRoot(full, _timing))
                {
                    var rel = full.Substring(_timing.Length).TrimStart('\\', '/');
                    var name = Path.GetFileName(_timing);
                    return rel.Length > 0 ? name + "/" + rel : name;
                }
                throw new NightlyRefusal("seed-input-outside-source");
            }

            // A summary value fills an identity the caller left empty, and refuses one that differs.
            private static void Adopt(object summary, string field, ref string current, string mismatch)
            {
                var value = PsValue.Prop(summary, field);
                if (!PsValue.IsTrue(value)) return;
                var text = PsValue.Str(value);
                if (!string.IsNullOrEmpty(current) && !string.Equals(current, text, StringComparison.Ordinal)) throw new NightlyRefusal(mismatch);
                if (string.IsNullOrEmpty(current)) current = text;
            }

            private static void Claim(object owner, string field, ref string current)
            {
                var value = PsValue.Str(PsValue.Prop(owner, field));
                if (string.IsNullOrEmpty(value)) return;
                if (!string.IsNullOrEmpty(current) && !string.Equals(current, value, StringComparison.Ordinal)) throw new NightlyRefusal("seed-owner-identity-mismatch");
                current = value;
            }
        }

        private static bool IsPlanFile(string name) =>
            name.StartsWith("groups-planned-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)
            && !string.Equals(name, "groups-planned-index.json", StringComparison.Ordinal);

        private static int ByName(string a, string b)
        {
            var c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }

        private static void Later(ref DateTimeOffset observed, DateTimeOffset? time)
        {
            if (time.HasValue && time.Value > observed) observed = time.Value;
        }

        private static XmlDocument LoadXml(string text)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }))
                doc.Load(reader);
            return doc;
        }

        private static object[] IdentityRows(XmlDocument xml)
        {
            var result = NightlyTrx.IdentityRows(xml);
            var error = PsValue.Str(PsValue.Prop(result, "Error"));
            if (!string.IsNullOrEmpty(error)) throw new InvalidDataException(error);
            return PsValue.AsArray(PsValue.Prop(result, "Rows"));
        }

        private static object[] TrxRows(string text) => IdentityRows(LoadXml(text));

        internal static bool UnderRoot(string path, string root) =>
            path.Equals(root, PathComparison) || path.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, PathComparison);

        internal static void NoLink(HistoryLinkCache links, string path, bool forceWindows)
        {
            var check = links.Check(path, forceWindows);
            if (check.Refusal != null) throw new NightlyRefusal(check.Refusal);
        }

        /// <summary>Lower-case hex SHA-256 of the file (opened shared for read and write).</summary>
        internal static string Sha(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(64);
                foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static void SetNote(object target, string name, object value) => PsValue.SetNote(target, name, value);

        /// <summary>A failure's reason: the innermost invoked exception's message (under PowerShell, also a runtime error's record).</summary>
        internal static string Message(Exception e)
        {
            if (PsRuntime.Loaded) return PsFields.InvocationMessage(e);
            while (e is System.Reflection.TargetInvocationException && e.InnerException != null) e = e.InnerException;
            return e.Message;
        }
    }

    /// <summary>Seed and report JSON: dictionaries, custom objects (PSCustomObjects, PlainObjects), lists and scalars; dates in round-trip form.</summary>
    internal static class SeedJson
    {
        private static readonly JsonWriterOptions Compact = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = false };
        private static readonly JsonWriterOptions Indented = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true };

        public static byte[] Bytes(object value, bool indented)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, indented ? Indented : Compact)) Write(writer, value, 0);
                return stream.ToArray();
            }
        }

        public static string Text(object value, bool indented) => Encoding.UTF8.GetString(Bytes(value, indented));

        private static void Write(Utf8JsonWriter w, object value, int depth)
        {
            if (depth > 64) throw new InvalidDataException("seed-json-depth");
            if (PsValue.IsCustomObject(value))
            {
                w.WriteStartObject();
                foreach (var property in PsValue.Properties(value))
                {
                    w.WritePropertyName(property.Key);
                    Write(w, property.Value, depth + 1);
                }
                w.WriteEndObject();
                return;
            }
            value = PsValue.BaseObject(value);
            switch (value)
            {
                case null: w.WriteNullValue(); return;
                case string s: w.WriteStringValue(s); return;
                case bool b: w.WriteBooleanValue(b); return;
                case int n: w.WriteNumberValue(n); return;
                case long n: w.WriteNumberValue(n); return;
                case double n:
                    if (double.IsNaN(n) || double.IsInfinity(n)) w.WriteStringValue(n.ToString(CultureInfo.InvariantCulture));
                    else w.WriteNumberValue(n);
                    return;
                case decimal n: w.WriteNumberValue(n); return;
                case DateTime d: w.WriteStringValue(d.ToString("o", CultureInfo.InvariantCulture)); return;
                case DateTimeOffset d: w.WriteStringValue(d.ToString("o", CultureInfo.InvariantCulture)); return;
                case IDictionary dictionary:
                    w.WriteStartObject();
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        w.WritePropertyName(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "");
                        Write(w, entry.Value, depth + 1);
                    }
                    w.WriteEndObject();
                    return;
                case IEnumerable items:
                    w.WriteStartArray();
                    foreach (var item in items) Write(w, item, depth + 1);
                    w.WriteEndArray();
                    return;
                default:
                    if (value.GetType().IsPrimitive) { w.WriteRawValue(Convert.ToString(value, CultureInfo.InvariantCulture), true); return; }
                    w.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }
    }
}
