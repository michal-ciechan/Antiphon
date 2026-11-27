// The CARD-1609 retirement coordinator of the live server2 bridge (unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md,
// W10 part 1): Invoke-C1609RetirementCoordinator, Get-C1008RetireProofExports and Get-C1609ProofSetDigest of scripts/c590-real.ps1.
//
// retire-temp-runner with a recycle context runs scripts/c590-remote.sh over one ssh child whose output is a request protocol.
// Each C1609_PROOF_REQUEST (initial, then renew) has the desktop copy the host's retirement inventory, prove every private
// origin it names into exchange-<nonce>/c1008-proof-<operation>-<started>-<nonce>/ (C1609HostProof), stage that proof on server2
// and answer C1609_PROOF_RESPONSE with its digests; each C1609_CONTROL_REQUEST has it re-read every origin live within the control
// window (C1609HostProof.AssertLiveOriginProof) and answer C1609_CONTROL_RESPONSE. Each answer is bound to the exchange nonce,
// the phase, the previous proof set and the required objects. The receipt (remote-output.txt) records transport-exit,
// failure-line, failure-script and failure-reason and, for a refused control, its step, elapsed and limit. coordinator-exchanges.jsonl
// and coordinator-controls.jsonl hold one JSON line per exchange and per control.
//
// The ssh child is IC590BridgeWorld.StartCoordinator; the proof's ssh, scp and git children are IC1609Processes. A proof refusal
// writes exchange-<nonce>/c590-result.json and ends the case with exit 2 (Write-C590Result's exit), as the script did. Plain C#.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>The coordinator's refusal site.</summary>
    public static class C1609Coordinator
    {
        /// <summary>A refusal's site (a receipt's failure-script) for the coordinator and the proof exports.</summary>
        public const string Site = "C1609Coordinator.cs";
    }

    public static partial class C590Bridge
    {
        internal sealed partial class Session
        {
            private static readonly Regex Token = new Regex("^[a-z][a-z-]{0,31}$");
            private static readonly Regex ProofRequest = new Regex("^C1609_PROOF_REQUEST ([0-9a-f]{32}) ([0-9a-f]{32}) (initial|renew) (none|[0-9a-f]{64}) ([0-9a-f]{64})$");
            private static readonly Regex ControlRequest = new Regex("^C1609_CONTROL_REQUEST ([0-9a-f]{32}) (ready|apply|audit|antiphon-runner-temp_(runner-state|dind-data|runner-tmp|work)) (none|[0-9a-f]{64}) ([0-9a-f]{64})$");
            private static readonly Regex ExportLine = new Regex("^export (C1008_HOST_PROOF|C1008_OPERATION_STARTED|C1609_PROOF_NONCE|C1609_PROOF_INVENTORY)='([A-Za-z0-9._/-]+)'$");
            private static readonly JsonWriterOptions Compact = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

            private static C1609Refusal Refuse(string token) => new C1609Refusal(token, C1609Coordinator.Site);

            /// <summary>Get-C1008RetireProofExports' parameters, in its order.</summary>
            internal delegate IReadOnlyList<string> ProofExportsCall(string evidenceRoot, string run, string recycleOperation, string inventoryRemote, string projectId,
                bool coordinated, int networkBudgetMs, string previousProofDirectory, string requiredObjectDigest, bool requireObjectCoverage, string inventoryPhaseNonce);

            /// <summary>A proof request's exports (default RetireProofExports); a test replaces it as the PowerShell tests replaced the function.</summary>
            internal ProofExportsCall ProofExports { get; set; }

            /// <summary>The session's desktop proof (its withdrawn-ref record and reason tokens).</summary>
            internal C1609HostProof Proof => _proof;

            /// <summary>One control request's record (the script's $control hashtable; written in its key order).</summary>
            private sealed class ControlRecord
            {
                public string Nonce = "";
                public string Purpose = "";
                public int LimitMs;
                public string Outcome = "refused";
                public string Step = "inventory";
                public string Reason = "";
                public readonly List<C1609Timing> Origins = new List<C1609Timing>();
                public long CleanupMs;
                public long ElapsedMs;
            }

            /// <summary>Get-C1609ProofSetDigest: the digest of a staged proof set (each proof and bundle name and its SHA-256).</summary>
            private string ProofSetDigest(string directory)
            {
                var lines = new List<string>();
                foreach (var file in _files.Directory.EnumerateFileSystemEntries(directory))
                {
                    var name = _files.Path.GetFileName(file);
                    if (string.Equals(name, "host-proof.log", StringComparison.Ordinal)) continue;
                    if (!Regex.IsMatch(name, "^[0-9a-f]{64}(\\.bundle)?$") || (_files.FileInfo.New(file).Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                        throw Refuse("proof-custody");
                    lines.Add(name + " " + _proof.FileSha256(file));
                }
                if (lines.Count == 0) throw Refuse("proof-empty");
                var array = lines.ToArray();
                Array.Sort(array, StringComparer.Ordinal);
                return C1609HostProof.Sha256(string.Join("\n", array) + "\n");
            }

            private static string Ms(long value) => value.ToString(CultureInfo.InvariantCulture);

            private void AppendJson(string path, Action<Utf8JsonWriter> write)
            {
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, Compact)) write(writer);
                _files.File.AppendAllText(path, Encoding.UTF8.GetString(stream.ToArray()) + "\n");
            }

            private static void WriteTiming(Utf8JsonWriter writer, C1609Timing timing)
            {
                writer.WriteStartObject();
                writer.WriteStartArray("steps");
                foreach (var step in timing.Steps)
                {
                    writer.WriteStartObject();
                    writer.WriteString("step", step.Step);
                    writer.WriteNumber("elapsedMs", step.ElapsedMs);
                    writer.WriteString("outcome", step.Outcome);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteString("outcome", timing.Outcome);
                writer.WriteString("reason", timing.Reason);
                writer.WriteString("step", timing.Step);
                writer.WriteNumber("limitMs", timing.LimitMs);
                writer.WriteNumber("elapsedMs", timing.ElapsedMs);
                writer.WriteNumber("cleanupMs", timing.CleanupMs);
                writer.WriteEndObject();
            }

            /// <summary>Invoke-C1609RetirementCoordinator: the ssh child's exit code (2 for any failure of the exchange); the receipt is written.</summary>
            internal int Coordinate(string remote, string evidenceRoot, string run, string operation, string projectId, string receiptPath)
            {
                IC1609Duplex proc = null;
                var started = false;
                var code = 2;
                var watch = Stopwatch.StartNew();
                long networkMs = 0;
                var previousDirectory = "";
                var previousDigest = "none";
                var objectDigest = C1609HostProof.Sha256("");
                var nonces = new HashSet<string>(StringComparer.Ordinal);
                var lineBuffer = "";
                long? exitedAt = null;
                var phaseBinding = "";
                var failureLine = 0;
                var failureScript = "";
                var failureReason = "";
                _proof.LiveProofReason = "";
                ControlRecord control = null;
                try
                {
                    var controlWindowMs = _proof.ControlWindowMs();
                    var directory = C1609BoundedProcesses.StartDirectory("", _seams.CurrentDirectory(), _files.Path.GetTempPath());
                    proc = _world.StartCoordinator(remote, directory);
                    started = true;
                    var streams = new[]
                    {
                        new C1609BoundedProcesses.BoundedStream(proc.Stdout, keep: true),
                        new C1609BoundedProcesses.BoundedStream(proc.Stderr, keep: false),
                    };
                    foreach (var s in streams) s.Next();
                    while (streams.Any(s => !s.Done))
                    {
                        // Content has its own 10800 s cap, network work shares 600 s, and each confirmation is bounded. This outer cap
                        // cannot shorten a valid scan.
                        if (watch.Elapsed.TotalSeconds > 26000) throw Refuse("deadline");
                        if (proc.HasExited)
                        {
                            exitedAt ??= watch.ElapsedMilliseconds;
                            if (watch.ElapsedMilliseconds - exitedAt.Value > 2000) throw Refuse("drain");
                        }
                        foreach (var s in streams)
                        {
                            if (s.Done || !s.Task.IsCompleted) continue;
                            var n = s.Task.GetAwaiter().GetResult();
                            if (n == 0) { s.Done = true; continue; }
                            s.Count += n;
                            if (s.Count > 16777216) throw Refuse("output-bound");
                            if (s.Keep)
                            {
                                lineBuffer += new string(s.Buffer, 0, n);
                                int newline;
                                while ((newline = lineBuffer.IndexOf('\n')) >= 0)
                                {
                                    var line = lineBuffer.Substring(0, newline);
                                    lineBuffer = lineBuffer.Substring(newline + 1);
                                    if (line.Length > 8192) throw Refuse("line-bound");
                                    var response = "";
                                    var proofMatch = ProofRequest.Match(line);
                                    var controlMatch = proofMatch.Success ? Match.Empty : ControlRequest.Match(line);
                                    if (proofMatch.Success)
                                    {
                                        var exchange = proofMatch.Groups[1].Value;
                                        var phase = proofMatch.Groups[2].Value;
                                        var kind = proofMatch.Groups[3].Value;
                                        var prior = proofMatch.Groups[4].Value;
                                        var requiredDigest = proofMatch.Groups[5].Value;
                                        if (phaseBinding.Length == 0) phaseBinding = phase;
                                        if (!string.Equals(phaseBinding, phase, StringComparison.Ordinal)) throw Refuse("phase-binding");
                                        if (!nonces.Add(exchange) || !string.Equals(prior, previousDigest, StringComparison.Ordinal)
                                            || (kind == "initial" && previousDirectory.Length > 0) || (kind == "renew" && previousDirectory.Length == 0))
                                            throw Refuse("exchange-binding");
                                        var remaining = 600000 - (int)networkMs;
                                        if (remaining <= 0) throw Refuse("network-deadline");
                                        var destination = Join(evidenceRoot, "exchange-" + exchange);
                                        _files.Directory.CreateDirectory(destination);
                                        var network = Stopwatch.StartNew();
                                        var exports = (ProofExports ?? RetireProofExports)(destination, run, operation, "true", projectId, true, remaining, previousDirectory,
                                            requiredDigest, kind == "renew", phase);
                                        networkMs += network.ElapsedMilliseconds;
                                        if (networkMs > 600000 || exports.Count != 4) throw Refuse("network-deadline");
                                        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                        foreach (var export in exports)
                                        {
                                            var exportMatch = ExportLine.Match(export);
                                            if (!exportMatch.Success) throw Refuse("proof-response");
                                            values[exportMatch.Groups[1].Value] = exportMatch.Groups[2].Value;
                                        }
                                        if (values.Count != 4) throw Refuse("proof-response");
                                        var name = Path.GetFileName(values["C1008_HOST_PROOF"]);
                                        previousDirectory = Join(destination, name);
                                        previousDigest = ProofSetDigest(previousDirectory);
                                        objectDigest = requiredDigest;
                                        response = "C1609_PROOF_RESPONSE " + exchange + " " + name + " " + values["C1008_OPERATION_STARTED"] + " " + values["C1609_PROOF_NONCE"] + " "
                                            + values["C1609_PROOF_INVENTORY"] + " " + previousDigest + " " + prior + " " + requiredDigest;
                                        var elapsed = network.ElapsedMilliseconds;
                                        AppendJson(Join(evidenceRoot, "coordinator-exchanges.jsonl"), w =>
                                        {
                                            w.WriteStartObject();
                                            w.WriteString("nonce", exchange);
                                            w.WriteString("kind", kind);
                                            w.WriteString("objectsDigest", objectDigest);
                                            w.WriteString("proofDigest", previousDigest);
                                            w.WriteNumber("networkMs", elapsed);
                                            w.WriteEndObject();
                                        });
                                    }
                                    else if (controlMatch.Success)
                                    {
                                        var exchange = controlMatch.Groups[1].Value;
                                        var purpose = controlMatch.Groups[2].Value;
                                        var proof = controlMatch.Groups[4].Value;
                                        var objects = controlMatch.Groups[5].Value;
                                        if (!nonces.Add(exchange) || !string.Equals(proof, previousDigest, StringComparison.Ordinal) || !string.Equals(objects, objectDigest, StringComparison.Ordinal))
                                            throw Refuse("control-binding");
                                        // Staging a large graph can outlive its last network read. Complete this very control request with
                                        // fresh live refs, bound to the same approved inventory and proof set.
                                        var controlWatch = Stopwatch.StartNew();
                                        long cleanupMs = 0;
                                        _proof.LiveProofReason = "";
                                        control = new ControlRecord { Nonce = exchange, Purpose = purpose, LimitMs = controlWindowMs };
                                        var record = control;
                                        try
                                        {
                                            if (previousDirectory.Length > 0)
                                            {
                                                var inventoryDirectory = Join(Path.GetDirectoryName(previousDirectory), "inventory-" + phaseBinding);
                                                var allowText = _files.File.ReadAllText(Join(inventoryDirectory, "trusted-origins.json"));
                                                var allow = allowText.Length == 0 ? null : RollingPs.ParseEnumerated(allowText);
                                                if (!RollingPs.Eq(RollingPs.Prop(allow, "schema"), 1) || !RollingPs.Ceq(RollingPs.Prop(allow, "projectId"), projectId)
                                                    || !(RollingPs.Prop(allow, "origins") is object[] allowed))
                                                    throw Refuse("control-origin");
                                                var approved = allowed.Select(RollingPs.Str).ToArray();
                                                var rows = C1609HostProof.ReadPrivateOriginLines(_files.File.ReadAllText(Join(inventoryDirectory, "private-origins.txt")));
                                                foreach (var row in rows)
                                                {
                                                    var remaining = Math.Min(controlWindowMs - (int)(controlWatch.ElapsedMilliseconds - cleanupMs),
                                                        600000 - (int)networkMs - (int)controlWatch.ElapsedMilliseconds);
                                                    if (remaining <= 0) throw Refuse("control-deadline");
                                                    var timing = new C1609Timing();
                                                    record.Origins.Add(timing);
                                                    _proof.AssertLiveOriginProof(row.Origin, row.Repo, operation, Join(previousDirectory, row.ProofName), approved, remaining, timing);
                                                    // Scratch cleanup is outside the control budget.
                                                    cleanupMs += timing.CleanupMs;
                                                }
                                            }
                                            record.Step = "complete";
                                            if (controlWatch.ElapsedMilliseconds - cleanupMs > controlWindowMs) throw Refuse("control-deadline");
                                            record.Outcome = "passed";
                                        }
                                        catch (Exception e) when (!(e is C590Exit))
                                        {
                                            var reason = e.Message;
                                            record.Reason = _proof.LiveProofReason.Length > 0 ? _proof.LiveProofReason : Token.IsMatch(reason) ? reason : "other";
                                            if (record.Origins.Count > 0 && record.Origins[record.Origins.Count - 1].Outcome == "refused")
                                                record.Step = record.Origins[record.Origins.Count - 1].Step;
                                            throw;
                                        }
                                        finally
                                        {
                                            record.CleanupMs = 0;
                                            foreach (var originTiming in record.Origins) record.CleanupMs += originTiming.CleanupMs;
                                            record.ElapsedMs = controlWatch.ElapsedMilliseconds - record.CleanupMs;
                                            networkMs += controlWatch.ElapsedMilliseconds;
                                            AppendJson(Join(evidenceRoot, "coordinator-controls.jsonl"), w =>
                                            {
                                                w.WriteStartObject();
                                                w.WriteNumber("schema", 1);
                                                w.WriteString("nonce", record.Nonce);
                                                w.WriteString("purpose", record.Purpose);
                                                w.WriteNumber("limitMs", record.LimitMs);
                                                w.WriteString("outcome", record.Outcome);
                                                w.WriteString("step", record.Step);
                                                w.WriteString("reason", record.Reason);
                                                w.WriteStartArray("origins");
                                                foreach (var originTiming in record.Origins) WriteTiming(w, originTiming);
                                                w.WriteEndArray();
                                                w.WriteNumber("cleanupMs", record.CleanupMs);
                                                w.WriteNumber("elapsedMs", record.ElapsedMs);
                                                w.WriteEndObject();
                                            });
                                        }
                                        response = "C1609_CONTROL_RESPONSE " + exchange + " " + purpose + " " + proof + " " + objects;
                                    }
                                    else if (line.StartsWith("C1609_PROOF_REQUEST", StringComparison.Ordinal) || line.StartsWith("C1609_CONTROL_REQUEST", StringComparison.Ordinal))
                                        throw Refuse("protocol");
                                    if (response.Length > 0)
                                    {
                                        if (proc.HasExited) throw Refuse("controller-eof");
                                        var write = proc.Stdin.WriteLineAsync(response);
                                        if (!write.Wait(10000)) throw Refuse("response-deadline");
                                        write.GetAwaiter().GetResult();
                                        proc.Stdin.Flush();
                                    }
                                }
                                if (lineBuffer.Length > 8192) throw Refuse("line-bound");
                            }
                            s.Next();
                        }
                        Thread.Sleep(10);
                    }
                    if (!proc.WaitForExit(2000)) throw Refuse("exit");
                    code = proc.ExitCode;
                }
                catch (Exception e) when (!(e is C590Exit))
                {
                    code = 2;
                    // The script recorded the PowerShell line of the throw; the port names the source that refused and no line.
                    failureLine = 0;
                    failureScript = e is C1609Refusal refusal ? refusal.Site : C1609Coordinator.Site;
                    var reason = e.Message;
                    if (failureScript == C1609HostProof.Site && Token.IsMatch(_proof.LiveProofReason ?? "")) reason = _proof.LiveProofReason;
                    failureReason = reason.StartsWith("RecycleGitAuditUnknown", StringComparison.Ordinal) ? "git" : Token.IsMatch(reason) ? reason : "other";
                }
                finally
                {
                    if (started)
                    {
                        try { proc.Stdin.Close(); } catch (Exception) { }
                        if (!proc.HasExited) { proc.Kill(); proc.WaitForExit(2000); }
                    }
                    proc?.Dispose();
                    var timingDetail = "";
                    if (control != null && control.Outcome == "refused")
                        timingDetail = "control-step=" + control.Step + "\ncontrol-elapsed-ms=" + Ms(control.ElapsedMs) + "\ncontrol-limit-ms=" + control.LimitMs.ToString(CultureInfo.InvariantCulture) + "\n";
                    _files.File.WriteAllText(receiptPath, "transport-exit=" + code.ToString(CultureInfo.InvariantCulture) + "\nfailure-line=" + failureLine.ToString(CultureInfo.InvariantCulture)
                        + "\nfailure-script=" + failureScript + "\nfailure-reason=" + failureReason + "\n" + timingDetail);
                }
                return code;
            }

            private int ProofSsh(string command, string receipt, bool coordinated, Stopwatch networkWatch, int networkBudgetMs)
            {
                if (!coordinated) return Ssh(command, receipt);
                try
                {
                    var remaining = Math.Min(30000, networkBudgetMs - (int)networkWatch.ElapsedMilliseconds);
                    return _proof.Processes.Run(new C1609ProcessCall
                    {
                        FileName = "ssh",
                        Arguments = new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=15", "mc@server2", command },
                        TimeoutMs = remaining,
                    }).ExitCode;
                }
                catch (Exception) { return 2; }
            }

            private int ProofScp(string[] arguments, bool coordinated, Stopwatch networkWatch, int networkBudgetMs)
            {
                if (!coordinated) return _world.Scp(arguments, discardOutput: false);
                try
                {
                    var remaining = Math.Min(300000, networkBudgetMs - (int)networkWatch.ElapsedMilliseconds);
                    return _proof.Processes.Run(new C1609ProcessCall { FileName = "scp", Arguments = arguments, TimeoutMs = remaining }).ExitCode;
                }
                catch (Exception) { return 2; }
            }

            /// <summary>
            /// Get-C1008RetireProofExports: copies the host's retirement inventory, proves every private origin it names (one graph per
            /// origin, a copy for its other repos), stages the proof set on server2 and returns the four exports that bind it; none when
            /// the inventory names no private origin. A refusal writes evidenceRoot's c590-result.json and ends the case (exit 2).
            /// </summary>
            internal IReadOnlyList<string> RetireProofExports(string evidenceRoot, string run, string recycleOperation, string inventoryRemote, string projectId,
                bool coordinated, int networkBudgetMs = 600000, string previousProofDirectory = "", string requiredObjectDigest = "", bool requireObjectCoverage = false,
                string inventoryPhaseNonce = "")
            {
                var networkWatch = Stopwatch.StartNew();
                if (!Regex.IsMatch(run ?? "", "^[a-z0-9]+$") || !Regex.IsMatch(recycleOperation ?? "", "^c1008[0-9a-f]{32}$")) Result(evidenceRoot, false, "RecycleGitAuditUnknown", 2, print: false);
                var code = ProofSsh(inventoryRemote, Join(evidenceRoot, "inventory-remote-output.txt"), coordinated, networkWatch, networkBudgetMs);
                var inventoryParent = evidenceRoot;
                var inventoryName = "retire-temp-inventory";
                var inventorySource = "mc@server2:/work/test-evidence/" + run + "/retire-temp-inventory";
                if (coordinated)
                {
                    if (!Regex.IsMatch(inventoryPhaseNonce ?? "", "^[0-9a-f]{32}$")) throw Refuse("phase-binding");
                    inventoryName = "inventory-" + inventoryPhaseNonce;
                    inventorySource = "mc@server2:/home/mc/antiphon-server2/retirement/" + recycleOperation + "/" + inventoryName;
                }
                if (ProofScp(new[] { "-o", "BatchMode=yes", "-r", inventorySource, inventoryParent }, coordinated, networkWatch, networkBudgetMs) != 0)
                    Result(evidenceRoot, false, "RecycleReceiptUnavailable", 2, print: false);
                var inventoryDir = Join(evidenceRoot, inventoryName);
                var resultPath = Join(inventoryDir, C590Boundary.ResultFileName);
                if (!_files.File.Exists(resultPath)) Result(evidenceRoot, false, "RecycleGitAuditUnknown", 2, print: false);
                var resultText = _files.File.ReadAllText(resultPath);
                var result = resultText.Length == 0 ? null : RollingPs.ParseEnumerated(resultText);
                if (code != 0 || !RollingPs.True(RollingPs.Prop(result, "accepted")))
                {
                    var diagnosis = RollingPs.Str(RollingPs.Prop(result, "diagnosis"));
                    if (!C1609HostProof.SafeAuditDiagnosis(diagnosis)) diagnosis = "RecycleGitAuditUnknown";
                    Result(evidenceRoot, false, diagnosis, 2, print: false);
                }
                var listPath = Join(inventoryDir, "private-origins.txt");
                if (!_files.File.Exists(listPath)) Result(evidenceRoot, false, "RecycleGitAuditUnknown", 2, print: false);
                IReadOnlyList<C1008OriginRow> rows = null;
                try { rows = C1609HostProof.ReadPrivateOriginLines(_files.File.ReadAllText(listPath)); }
                catch (Exception e) when (!(e is C590Exit)) { Result(evidenceRoot, false, "RecycleGitAuditUnknown", 2, print: false); }
                if (rows.Count == 0) return Array.Empty<string>();
                // The script's @{} tables compared their keys ignoring case.
                var requiredByOrigin = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                string requestDigest = null;
                string[] approved = null;
                try
                {
                    var allowText = _files.File.ReadAllText(Join(inventoryDir, "trusted-origins.json"));
                    RollingPs.AssertC994RawManifest(allowText);
                    var allow = RollingPs.ParseEnumerated(allowText);
                    if (!RollingPs.Eq(RollingPs.Prop(allow, "schema"), 1) || !RollingPs.Ceq(RollingPs.Prop(allow, "projectId"), projectId)
                        || !Regex.IsMatch(projectId ?? "", "^[0-9a-f-]{36}$") || !(RollingPs.Prop(allow, "origins") is object[] origins)
                        || !string.Equals(string.Join(",", RollingPs.Sorted(RollingPs.Names(allow))), "origins,projectId,schema", StringComparison.Ordinal))
                        throw Refuse("registry");
                    approved = origins.Select(RollingPs.Str).ToArray();
                    var requestLines = rows.Select(r => "C1008_PRIVATE_ORIGIN repo=" + r.Repo + " origin=" + r.Origin + " originHash=" + r.OriginHash + " proofName=" + r.ProofName).ToArray();
                    Array.Sort(requestLines, StringComparer.Ordinal);
                    requestDigest = C1609HostProof.Sha256(string.Join("\n", requestLines) + "\n");
                    foreach (var row in rows)
                        if (!requiredByOrigin.ContainsKey(row.OriginHash)) requiredByOrigin[row.OriginHash] = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (coordinated)
                    {
                        var requiredFile = Join(inventoryDir, "published-objects.txt");
                        if (_files.FileInfo.New(requiredFile).Length > 16777216) throw Refuse("required-size");
                        var requiredText = _files.File.ReadAllText(requiredFile);
                        if (!string.Equals(C1609HostProof.Sha256(requiredText), requiredObjectDigest, StringComparison.Ordinal)) throw Refuse("required-digest");
                        var repos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var row in rows) repos[row.Repo] = row.OriginHash;
                        var covered = new HashSet<string>(StringComparer.Ordinal);
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var line in requiredText.Split('\n'))
                        {
                            if (line.Length == 0) continue;
                            var match = Regex.Match(line, "^C1609_PROVED_OBJECT repo=([A-Za-z0-9._/-]+) object=([0-9a-f]{40}) type=(commit|tag|tree|blob)$");
                            if (!match.Success) throw Refuse("required-shape");
                            var repo = match.Groups[1].Value;
                            var oid = match.Groups[2].Value;
                            var type = match.Groups[3].Value;
                            if (!repos.ContainsKey(repo) || !seen.Add(repo + ":" + oid)) throw Refuse("required-coverage");
                            covered.Add(repo);
                            var group = requiredByOrigin[repos[repo]];
                            if (group.TryGetValue(oid, out var known) && !string.Equals(known, type, StringComparison.Ordinal)) throw Refuse("required-type");
                            group[oid] = type;
                        }
                        if (requireObjectCoverage && covered.Count != rows.Count) throw Refuse("required-coverage");
                    }
                }
                catch (Exception e) when (!(e is C590Exit)) { Result(evidenceRoot, false, "RecycleGitAuditUnknown", 2, print: false); }
                var phaseNonce = _seams.NewGuid().ToString("N");
                var startedAt = _host.Clock.GetUtcNow().ToUnixTimeSeconds();
                var startedText = startedAt.ToString(CultureInfo.InvariantCulture);
                var remoteName = "c1008-proof-" + recycleOperation + "-" + startedText + "-" + phaseNonce;
                var remoteDir = "/home/mc/antiphon-c590/" + remoteName;
                if (!Regex.IsMatch(remoteName, "^c1008-proof-c1008[0-9a-f]{32}-[0-9]+-[0-9a-f]{32}$")
                    || !Regex.IsMatch(remoteDir, "^/home/mc/antiphon-c590/c1008-proof-c1008[0-9a-f]{32}-[0-9]+-[0-9a-f]{32}$"))
                    Result(evidenceRoot, false, "RecycleGitAuditUnknown", 2, print: false);
                var localDir = Join(evidenceRoot, remoteName);
                if (_files.File.Exists(localDir) || _files.Directory.Exists(localDir)) Result(evidenceRoot, false, "RecycleGitAuditUnknown", 2, print: false);
                _files.Directory.CreateDirectory(localDir);
                if (ProofSsh("mkdir " + remoteDir + " && chmod 755 " + remoteDir, "", coordinated, networkWatch, networkBudgetMs) != 0) Result(evidenceRoot, false, "RecycleGitAuditUnknown", 2, print: false);
                var logPath = Join(localDir, "host-proof.log");
                var phaseWatch = Stopwatch.StartNew();
                var graphs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows)
                {
                    try
                    {
                        if (graphs.TryGetValue(row.OriginHash, out var sourceName))
                            _proof.CopyOriginProof(localDir, sourceName, row.Repo, row.Origin);
                        else
                        {
                            var remaining = networkBudgetMs - (int)phaseWatch.ElapsedMilliseconds;
                            if (coordinated) remaining = Math.Min(remaining, networkBudgetMs - (int)networkWatch.ElapsedMilliseconds);
                            var required = requiredByOrigin[row.OriginHash].Select(pair => new C1609RequiredObject(pair.Key, pair.Value)).ToArray();
                            var previousFile = string.IsNullOrEmpty(previousProofDirectory) ? "" : Join(previousProofDirectory, row.ProofName);
                            _proof.WriteOriginProof(new C1609ProofRequest
                            {
                                OriginUrl = row.Origin, RepoRelative = row.Repo, OperationId = recycleOperation, ProofDirectory = localDir, CreatedUnix = startedAt, LogPath = logPath,
                                PhaseNonce = phaseNonce, InventoryDigest = requestDigest, ApprovedOrigins = approved, NetworkBudgetMs = remaining, RequiredObjects = required,
                                PreviousProofFile = previousFile, FreshGraph = requireObjectCoverage,
                            });
                            graphs[row.OriginHash] = row.ProofName;
                        }
                    }
                    catch (Exception e) when (!(e is C590Exit))
                    {
                        var message = e is C1609Refusal ? e.Message : "";
                        if (!Regex.IsMatch(message, "^RecycleGitAuditUnknown audit check=host-ls-remote status=[0-9]+ repo=[A-Za-z0-9._/-]+$")) message = "RecycleGitAuditUnknown";
                        Result(evidenceRoot, false, message, 2, print: false);
                    }
                    if (!Regex.IsMatch(row.ProofName, "^[0-9a-f]{64}$")) Result(evidenceRoot, false, "RecycleGitAuditUnknown", 2, print: false);
                    var proofFile = Join(localDir, row.ProofName);
                    if (!_files.File.Exists(proofFile) && !_files.Directory.Exists(proofFile)) Result(evidenceRoot, false, "RecycleGitAuditUnknown", 2, print: false);
                    if (ProofScp(new[] { "-o", "BatchMode=yes", proofFile, "mc@server2:" + remoteDir + "/" + row.ProofName }, coordinated, networkWatch, networkBudgetMs) != 0)
                        Result(evidenceRoot, false, "RecycleReceiptUnavailable", 2, print: false);
                    var bundleFile = Join(localDir, row.ProofName + ".bundle");
                    if (_files.File.Exists(bundleFile) || _files.Directory.Exists(bundleFile))
                    {
                        if (ProofScp(new[] { "-o", "BatchMode=yes", bundleFile, "mc@server2:" + remoteDir + "/" + row.ProofName + ".bundle" }, coordinated, networkWatch, networkBudgetMs) != 0)
                            Result(evidenceRoot, false, "RecycleReceiptUnavailable", 2, print: false);
                        if (ProofSsh("chmod 644 " + remoteDir + "/" + row.ProofName + ".bundle", "", coordinated, networkWatch, networkBudgetMs) != 0)
                            Result(evidenceRoot, false, "RecycleGitAuditUnknown", 2, print: false);
                    }
                    if (ProofSsh("chmod 644 " + remoteDir + "/" + row.ProofName, "", coordinated, networkWatch, networkBudgetMs) != 0)
                        Result(evidenceRoot, false, "RecycleGitAuditUnknown", 2, print: false);
                }
                return new[]
                {
                    "export C1008_HOST_PROOF='" + remoteDir + "'",
                    "export C1008_OPERATION_STARTED='" + startedText + "'",
                    "export C1609_PROOF_NONCE='" + phaseNonce + "'",
                    "export C1609_PROOF_INVENTORY='" + requestDigest + "'",
                };
            }
        }
    }
}
