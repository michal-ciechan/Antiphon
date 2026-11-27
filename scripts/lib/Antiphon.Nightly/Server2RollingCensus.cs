// The recycle census of the rolling server2 deploy (unit 65; deploy-server2.ps1's Start-RecycleReceipt through
// Wait-RecycleTaskCensus): the project resolution (CARD-1087), the two-pass census of open work and pending lands
// (CARD-1087, CARD-1317 to CARD-1374, CARD-1693/1697), the bound-land wait (CARD-1353 D-3/D-4) and the census receipt.
// The same reads, retries, refusals, receipt fields and order. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One census run on its own (Server2RollingDeploy.ProbeCensus).</summary>
    public sealed class Server2RollingCensusProbe
    {
        /// <summary>The recycled runner.</summary>
        public string RunnerId { get; init; } = "server2";

        /// <summary>The root project.</summary>
        public string ProjectId { get; init; } = "";

        /// <summary>The receipt's phase (census-&lt;phase&gt;-&lt;runner&gt;.json).</summary>
        public string Phase { get; init; } = "probe";

        /// <summary>The receipt's directory.</summary>
        public string EvidenceRoot { get; init; } = "";

        /// <summary>The API origin the reads name.</summary>
        public string Api { get; init; } = "http://127.0.0.1:1";

        /// <summary>Wait-RecycleTaskCensus instead of one Assert-RecycleTaskCensus.</summary>
        public bool Wait { get; init; }

        /// <summary>Assert-RecycleTaskCensus -Report.</summary>
        public bool Report { get; init; }

        /// <summary>--resume-recycle was given (exhausted drift then passes the wait).</summary>
        public bool Resume { get; init; }

        /// <summary>--land-wait-minutes.</summary>
        public int LandWaitMinutes { get; init; } = 30;
    }

    /// <summary>What a census probe saw: the refusal (empty when none), the returned snapshot or report, and the receipt's parts.</summary>
    public sealed class Server2RollingCensusResult
    {
        /// <summary>The refusal's message, empty when the census passed.</summary>
        public string Error { get; set; } = "";

        /// <summary>0 passed, 2 refused.</summary>
        public int Code { get; set; }

        /// <summary>The compact snapshot the census returned (not a report), null when none.</summary>
        public string Returned { get; set; }

        /// <summary>The report (-Report), null otherwise.</summary>
        public PlainObject Report { get; set; }

        /// <summary>The receipt's snapshot as compact JSON, null when none was taken.</summary>
        public string Snapshot { get; set; }

        /// <summary>The receipt's tasks keyed by id, as compact JSON.</summary>
        public string Tasks { get; set; } = "{}";

        /// <summary>The receipt file.</summary>
        public string ReceiptPath { get; set; } = "";
    }

    public static partial class Server2RollingDeploy
    {
        /// <summary>Assert-RecycleTaskCensus -Report's counts.</summary>
        internal sealed class CensusReport
        {
            public int Open;
            public int BoundOpen;
            public int LandPending;
            public int BoundLandPending;
            public int Scopes;
            public long ElapsedMs;
        }

        private static readonly string[] TaskKeys = { "id", "status", "runnerId", "projectId", "scopeSource", "landRequestedAt", "landStartedAt" };
        private static readonly string[] OpenStatuses = { "Queued", "Dispatched", "Working", "Blocked" };
        private const string GuidPattern = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";

        /// <summary>Test-RecycleGuid: a lower-case GUID string.</summary>
        public static bool TestRecycleGuid(object value) => value is string && RollingPs.CMatch(value, GuidPattern);

        /// <summary>Test-RecycleTimestamp: null, a date, or an ISO string with an explicit zone that parses.</summary>
        public static bool TestRecycleTimestamp(object value)
        {
            if (value == null || value is DateTime || value is DateTimeOffset) return true;
            return value is string text && Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$")
                && DateTimeOffset.TryParse(text, out _);
        }

        /// <summary>ConvertTo-RecycleRepositoryUrl: an https GitHub-style URL's identity (lower case), '' for anything else.</summary>
        public static string ConvertToRecycleRepositoryUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var text = value.Trim();
            var ssh = Regex.Match(text, "^git@([^:]+):(.+)$", RegexOptions.IgnoreCase);
            if (ssh.Success) text = "https://" + ssh.Groups[1].Value + "/" + ssh.Groups[2].Value;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, "https", StringComparison.Ordinal) || !uri.IsDefaultPort
                || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) return "";
            // GitHub repository identity is case-insensitive, including its owner/name.
            var repository = Regex.Replace(uri.AbsolutePath.TrimEnd('/'), @"\.git$", "", RegexOptions.IgnoreCase);
            return "https://" + uri.Host.ToLowerInvariant() + repository.ToLowerInvariant();
        }

        /// <summary>ConvertTo-RecycleRepositoryPath: a full path without a trailing separator (lower case on Windows), '' for blank.</summary>
        public static string ConvertToRecycleRepositoryPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            if (OperatingSystem.IsWindows()) value = value.Replace('/', '\\');
            var full = Path.GetFullPath(value);
            var root = Path.GetPathRoot(full) ?? "";
            if (full.Length > root.Length) full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
        }

        /// <summary>Test-RecycleJsonEqual: two ConvertFrom-Json values compared by shape and text.</summary>
        public static bool TestRecycleJsonEqual(object left, object right)
        {
            if (left == null || right == null) return left == null && right == null;
            var leftObject = left is PlainObject;
            var rightObject = right is PlainObject;
            if (leftObject || rightObject)
            {
                if (!(leftObject && rightObject)) return false;
                var leftNames = RollingPs.Names(left);
                var rightNames = RollingPs.Names(right);
                if (leftNames.Length != rightNames.Length) return false;
                foreach (var name in leftNames)
                {
                    if (!rightNames.Contains(name, StringComparer.Ordinal)) return false;
                    if (!TestRecycleJsonEqual(RollingPs.Prop(left, name), RollingPs.Prop(right, name))) return false;
                }
                return true;
            }
            var leftItems = left is System.Collections.IEnumerable && !(left is string);
            var rightItems = right is System.Collections.IEnumerable && !(right is string);
            if (leftItems || rightItems)
            {
                if (!(leftItems && rightItems)) return false;
                var l = RollingPs.Items(left);
                var r = RollingPs.Items(right);
                if (l.Length != r.Length) return false;
                for (var i = 0; i < l.Length; i++) if (!TestRecycleJsonEqual(l[i], r[i])) return false;
                return true;
            }
            return RollingPs.Ceq(RollingPs.Str(left), RollingPs.Str(right));
        }

        // ($value | ConvertTo-Json -Compress): the pipeline hands an array on element by element (none: no text, one: that element).
        private static string PipeJson(object value)
        {
            if (value is object[] items)
            {
                if (items.Length == 0) return null;
                if (items.Length == 1) return RollingPs.Json(items[0]);
            }
            return RollingPs.Json(value);
        }

        private static bool SameJson(object a, object b) => string.Equals(PipeJson(a), PipeJson(b), StringComparison.Ordinal);

        /// <summary>Remove-RecycleOpenIds: drops the ids from a closure's tasks, land and scope lists.</summary>
        public static void RemoveRecycleOpenIds(object closure, IEnumerable<string> ids)
        {
            foreach (var id in ids)
            {
                if (RollingPs.Prop(closure, "tasks") is PlainObject tasks && tasks.Names.Contains(id, StringComparer.Ordinal)) tasks.Remove(id);
                if (RollingPs.Prop(closure, "land") is PlainObject land && land.Names.Contains(id, StringComparer.Ordinal)) land.Remove(id);
                if (RollingPs.Prop(closure, "scopes") is PlainObject scopes)
                {
                    foreach (var entry in scopes.Entries.ToArray())
                    {
                        var value = RollingPs.Str(entry.Value);
                        var pipe = value.IndexOf('|');
                        var idPart = pipe >= 0 ? value.Substring(0, pipe) : value;
                        var suffix = pipe >= 0 ? value.Substring(pipe) : "";
                        var kept = idPart.Split(',').Where(x => x.Length > 0 && !string.Equals(x, id, StringComparison.Ordinal));
                        scopes[entry.Key] = string.Join(",", kept) + suffix;
                    }
                }
            }
        }

        /// <summary>
        /// Test-RecycleOpenStatusMove: a status change is safe only with complete, identical off-runner facts and explicit
        /// no-pending-land proof (shared by list/detail admission and comparison-copy reconciliation).
        /// </summary>
        public static bool TestRecycleOpenStatusMove(object before, object after, object detail, string runnerId)
        {
            foreach (var key in TaskKeys)
            {
                if (!RollingPs.HasExact(before, key) || !RollingPs.HasExact(after, key)) return false;
                if (key != "status" && !SameJson(RollingPs.Prop(before, key), RollingPs.Prop(after, key))) return false;
            }
            if (!RollingPs.CIn(RollingPs.Prop(before, "status"), OpenStatuses) || !RollingPs.CIn(RollingPs.Prop(after, "status"), OpenStatuses)
                || RollingPs.Eq(RollingPs.Str(RollingPs.Prop(before, "runnerId")), runnerId) || RollingPs.Eq(RollingPs.Str(RollingPs.Prop(after, "runnerId")), runnerId)
                || !RollingPs.HasExact(detail, "landRequest")) return false;
            var proof = RollingPs.Prop(detail, "landRequest");
            return proof == null || (RollingPs.CIn(RollingPs.Prop(proof, "state"), "Completed", "Superseded", "Canceled")
                && RollingPs.Prop(proof, "terminalEventId") is string && RollingPs.CMatch(RollingPs.Prop(proof, "terminalEventId"), GuidPattern));
        }

        /// <summary>Sync-RecycleOpenStatuses: aligns the prior copy's safe off-runner status moves with the live copy; returns how many.</summary>
        public static int SyncRecycleOpenStatuses(object prior, object live, string runnerId)
        {
            object Tasks(object o) => RollingPs.Prop(RollingPs.Prop(o, "open"), "tasks");
            object Land(object o) => RollingPs.Prop(RollingPs.Prop(o, "open"), "land");
            var changed = 0;
            foreach (var id in RollingPs.Names(Tasks(prior)))
            {
                if (!RollingPs.Names(Tasks(live)).Contains(id, StringComparer.Ordinal) || !RollingPs.Names(Land(prior)).Contains(id, StringComparer.Ordinal)
                    || !RollingPs.Names(Land(live)).Contains(id, StringComparer.Ordinal)) continue;
                if (!TestRecycleJsonEqual(RollingPs.Prop(Land(prior), id), RollingPs.Prop(Land(live), id))) continue;
                var detail = RollingPs.Object(("landRequest", RollingPs.Prop(Land(live), id)));
                var before = RollingPs.Prop(Tasks(prior), id);
                var after = RollingPs.Prop(Tasks(live), id);
                if (!RollingPs.Ceq(RollingPs.Prop(before, "status"), RollingPs.Prop(after, "status")) && TestRecycleOpenStatusMove(before, after, detail, runnerId))
                {
                    ((PlainObject)before)["status"] = RollingPs.Prop(after, "status");
                    changed++;
                }
            }
            return changed;
        }

        /// <summary>
        /// Test-RecycleForgivenDrift: pass-0 tasks proven settled without a land may leave the pass-1 list; pass-1 tasks proven to
        /// carry no land may appear; safe off-runner status moves align. True only when that drift is the sole difference.
        /// </summary>
        public static bool TestRecycleForgivenDrift(string previous, string current, IEnumerable<string> settledIds, string runnerId)
        {
            var prior = RollingPs.ParseEnumerated(previous);
            var live = RollingPs.ParseEnumerated(current);
            object Tasks(object o) => RollingPs.Prop(RollingPs.Prop(o, "open"), "tasks");
            var priorNames = RollingPs.Names(Tasks(prior));
            var liveNames = RollingPs.Names(Tasks(live));
            var gone = new List<string>();
            foreach (var id in settledIds ?? Array.Empty<string>())
                if (priorNames.Contains(id, StringComparer.Ordinal) && !liveNames.Contains(id, StringComparer.Ordinal)) gone.Add(id);
            if (gone.Count > 0) RemoveRecycleOpenIds(RollingPs.Prop(prior, "open"), gone);
            var landNames = RollingPs.Names(RollingPs.Prop(RollingPs.Prop(live, "land"), "tasks"));
            var fresh = new List<string>();
            foreach (var id in liveNames)
            {
                if (priorNames.Contains(id, StringComparer.Ordinal)) continue;
                var task = RollingPs.Prop(Tasks(live), id);
                if (RollingPs.Prop(task, "landRequestedAt") != null || RollingPs.Prop(task, "landStartedAt") != null) continue;
                if (landNames.Contains(id, StringComparer.Ordinal)) continue;
                var openLand = RollingPs.Prop(RollingPs.Prop(live, "open"), "land") as PlainObject;
                var landProp = openLand?.Entries.FirstOrDefault(e => string.Equals(e.Key, id, StringComparison.Ordinal));
                if (landProp == null || landProp.Value.Key == null || landProp.Value.Value != null) continue;
                fresh.Add(id);
            }
            if (fresh.Count > 0) RemoveRecycleOpenIds(RollingPs.Prop(live, "open"), fresh);
            var changed = SyncRecycleOpenStatuses(prior, live, runnerId);
            if (gone.Count == 0 && fresh.Count == 0 && changed == 0) return false;
            return TestRecycleJsonEqual(prior, live);
        }

        /// <summary>
        /// The open row's detail land proof: an in-flight state (Queued, Held, Running, NeedsResolution) is a pending land (D-5:
        /// recorded and counted; bound to the runner it is RecycleLandInFlight unless reporting); a terminal state needs a GUID
        /// terminalEventId (else RecycleLandUnknown); no proof is nothing.
        /// </summary>
        public static void AdmitDetailLand(object landRequest, string id, bool bound, bool report, ISet<string> pendingLands, ISet<string> boundLands)
        {
            if (landRequest == null) return;
            var state = RollingPs.Str(RollingPs.Prop(landRequest, "state"));
            if (RollingPs.CIn(state, "Queued", "Held", "Running", "NeedsResolution"))
            {
                pendingLands.Add(id);
                if (bound) { boundLands.Add(id); if (!report) throw new Server2RollingRefusal("RecycleLandInFlight " + id); }
            }
            else if (!RollingPs.CIn(state, "Completed", "Superseded", "Canceled")
                || !RollingPs.CMatch(RollingPs.Str(RollingPs.Prop(landRequest, "terminalEventId")), GuidPattern))
                throw new Server2RollingRefusal("RecycleLandUnknown");
        }

        /// <summary>The excluded.total and excluded.unscoped admission: a whole number that is not negative.</summary>
        public static bool ValidCount(object value) => RollingPs.IsInt(value) && !RollingPs.LessThan(value, 0);

        /// <summary>
        /// The compared closure's projection (CARD-1353 D-2 / CARD-1697): of the census rows (id to compact facts, as the census
        /// keeps them), the rows bound to the runner, in sorted id order, as ConvertFrom-Json read their facts.
        /// </summary>
        public static PlainObject BoundProjection(IReadOnlyDictionary<string, string> rows, string runnerId)
        {
            var taskSnapshot = new PlainObject();
            foreach (var key in RollingPs.Sorted(rows.Keys).ToArray())
            {
                var fact = RollingPs.ParseEnumerated(rows[key]);
                if (RollingPs.Eq(RollingPs.Str(RollingPs.Prop(fact, "runnerId")), runnerId)) taskSnapshot[key] = fact;
            }
            return taskSnapshot;
        }

        /// <summary>
        /// The census alone, as the census probes drove the script's extracted functions: Start-RecycleReceipt (phase, runner) in the
        /// evidence root, then Wait-RecycleTaskCensus (wait) or Assert-RecycleTaskCensus (with -Report when asked), on the seams'
        /// world. The refusal is the error and code 2; the receipt is the one written.
        /// </summary>
        public static Server2RollingCensusResult ProbeCensus(Server2RollingCensusProbe probe, NightlyHost host, System.IO.Abstractions.IFileSystem files, Server2RollingSeams seams)
        {
            if (probe == null) throw new ArgumentNullException(nameof(probe));
            var options = new Server2RollingOptions
            {
                Rolling = true, Phase = probe.Phase, LandWaitMinutes = probe.LandWaitMinutes, Checkout = probe.EvidenceRoot,
                ResumeRecycle = probe.Resume ? "c100800000000000000000000000000000001" : null,
            };
            var session = new Session(options, host, files, seams);
            return session.Probe(probe);
        }

        private sealed partial class Session
        {
            internal Server2RollingCensusResult Probe(Server2RollingCensusProbe probe)
            {
                _api = (probe.Api ?? "").TrimEnd('/');
                _evidenceRoot = probe.EvidenceRoot;
                _repoRoot = probe.EvidenceRoot;
                var result = new Server2RollingCensusResult();
                try
                {
                    StartRecycleReceipt(probe.Phase, probe.RunnerId);
                    var value = probe.Wait ? WaitRecycleTaskCensus(probe.RunnerId, probe.ProjectId) : AssertRecycleTaskCensus(probe.RunnerId, probe.ProjectId, probe.Report);
                    if (value is CensusReport report)
                        result.Report = RollingPs.Object(("open", report.Open), ("boundOpen", report.BoundOpen), ("landPending", report.LandPending),
                            ("boundLandPending", report.BoundLandPending), ("scopes", report.Scopes), ("elapsedMs", report.ElapsedMs));
                    else result.Returned = value as string;
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    result.Error = e.Message;
                    result.Code = 2;
                }
                result.Snapshot = _recycleReceipt?["snapshot"] == null ? null : RollingPs.Json(_recycleReceipt["snapshot"], 20);
                var tasks = new PlainObject();
                foreach (var key in RollingPs.Sorted(_recycleReceiptTasks?.Keys ?? (IEnumerable<string>)Array.Empty<string>())) tasks[key] = _recycleReceiptTasks[key];
                result.Tasks = RollingPs.Json(tasks, 20);
                result.ReceiptPath = _recycleReceiptPath;
                return result;
            }

            private string _recycleReceiptPath;
            private PlainObject _recycleReceipt;
            private List<object> _reads;
            private Dictionary<string, PlainObject> _recycleReceiptTasks;
            private string _resolvedRecycleProjectId;

            private void StartRecycleReceipt(string executingPhase, string runnerId)
            {
                _recycleReceiptPath = Join(_evidenceRoot, "census-" + executingPhase + "-" + runnerId + ".json");
                _reads = new List<object>();
                _recycleReceipt = RollingPs.Object(("schema", 1), ("phase", executingPhase), ("runnerId", runnerId), ("project", null),
                    ("reads", _reads), ("tasks", Array.Empty<object>()));
                _recycleReceiptTasks = new Dictionary<string, PlainObject>(StringComparer.OrdinalIgnoreCase);
                _resolvedRecycleProjectId = null;
                WriteRecycleReceipt();
            }

            // Persist approved fields only, including a partial census on refusal. Never retain response bodies, exception messages,
            // credentials or task prose.
            private void WriteRecycleReceipt()
            {
                if (_recycleReceipt == null) return;
                _recycleReceipt["tasks"] = RollingPs.Sorted(_recycleReceiptTasks.Keys).Select(k => (object)_recycleReceiptTasks[k]).ToArray();
                try { SetContent(_recycleReceiptPath, RollingPs.JsonIndented(_recycleReceipt, 20)); }
                catch (Exception) { throw new Server2RollingRefusal("RecycleTaskCensusUnknown cause=Transport field=receipt"); }
            }

            private Exception StopRecycleMalformed(string field, string path, string reasonCode = "RecycleTaskCensusUnknown")
            {
                if (_recycleReceipt != null)
                {
                    var read = _reads.OfType<PlainObject>().LastOrDefault(r => string.Equals(RollingPs.Str(r["path"]), path, StringComparison.Ordinal));
                    if (read != null) { read["outcome"] = "Malformed"; read["field"] = field; }
                }
                return new Server2RollingRefusal(reasonCode + " cause=Malformed field=" + field + " path=" + path);
            }

            private object InvokeRecycleRead(string path, bool retryTransient = false)
            {
                var maxAttempts = retryTransient ? 3 : 1;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    var started = Timestamp();
                    var read = RollingPs.Object(("path", path), ("attempt", attempt), ("elapsedMs", 0L), ("outcome", "ok"), ("items", null), ("excludedTotal", null));
                    if (_recycleReceipt != null) _reads.Add(read);
                    string cause = null;
                    var retryable = false;
                    try
                    {
                        Server2RollingHttpAnswer response = null;
                        Exception failure = null;
                        try { response = _world.ReadHttp(path, _api + path, _headers); }
                        catch (Exception e) { failure = e; }
                        if (failure != null)
                        {
                            cause = "Transport";
                            for (var error = failure; error != null; error = error.InnerException)
                            {
                                if (error is OperationCanceledException || error is TimeoutException
                                    || (error is WebException timeout && timeout.Status == WebExceptionStatus.Timeout))
                                {
                                    cause = "Timeout";
                                    break;
                                }
                                if ((error is SocketException socket && socket.SocketErrorCode == SocketError.ConnectionReset)
                                    || (error is WebException web && (web.Status == WebExceptionStatus.ConnectionClosed
                                        || web.Status == WebExceptionStatus.ReceiveFailure || web.Status == WebExceptionStatus.SendFailure)))
                                    retryable = true;
                            }
                        }
                        else if (response.Status < 200 || response.Status >= 300)
                        {
                            // A failed response body is never parsed or persisted.
                            read["status"] = response.Status;
                            cause = "Http";
                        }
                        else if (string.IsNullOrWhiteSpace(response.Body)) cause = "Empty";
                        else
                        {
                            object value = null;
                            try { value = RollingPs.Parse(response.Body); }
                            catch (RollingJsonException) { cause = "Malformed"; read["field"] = "body"; }
                            if (cause == null)
                            {
                                if (value is object[] array) read["items"] = array.Length;
                                else if (RollingPs.Prop(value, "items") is object[] items) read["items"] = items.Length;
                                var total = RollingPs.Prop(RollingPs.Prop(value, "excluded"), "total");
                                if (RollingPs.IsInt(total)) read["excludedTotal"] = total;
                                return value;
                            }
                        }
                        retryable = retryTransient && (retryable || cause == "Timeout"
                            || (cause == "Http" && new[] { 502, 503, 504 }.Contains((int)read["status"])));
                        read["outcome"] = cause;
                    }
                    finally
                    {
                        // Flush every physical observation before either returning or sleeping.
                        read["elapsedMs"] = ElapsedMs(started);
                        WriteRecycleReceipt();
                    }
                    if (retryable && attempt < maxAttempts)
                    {
                        if (_timing.StubbedReads && _timing.PollMs.HasValue) Sleep(_timing.PollMs.Value);
                        else Sleep(1000);
                        continue;
                    }
                    var suffix = cause == "Http" ? " status=" + read["status"] : cause == "Malformed" ? " field=body" : "";
                    if (retryable) { suffix += " attempts=" + attempt + " failure=" + cause; cause = "Http"; }
                    throw new Server2RollingRefusal("RecycleTaskCensusUnknown cause=" + cause + suffix + " path=" + path + " elapsedMs=" + RollingPs.Str(read["elapsedMs"]));
                }
                throw new InvalidOperationException("unreachable");
            }

            private string GetRecycleProjectId()
            {
                if (!string.IsNullOrEmpty(_resolvedRecycleProjectId)) return _resolvedRecycleProjectId;
                try
                {
                    var explicitId = _recycleProjectIdWasProvided;
                    string path;
                    object[] projects;
                    if (explicitId)
                    {
                        if (!TestRecycleGuid(_requestedRecycleProjectId)) throw new Server2RollingRefusal("RecycleProjectUnresolved cause=InvalidId");
                        path = "/api/projects/" + _requestedRecycleProjectId;
                        try { projects = new[] { InvokeRecycleRead(path) }; }
                        catch (Server2RollingRefusal e) when (Regex.IsMatch(e.Message, "^RecycleTaskCensusUnknown cause=Http status=404 "))
                        {
                            throw new Server2RollingRefusal("RecycleProjectUnresolved cause=NotFound id=" + _requestedRecycleProjectId);
                        }
                    }
                    else
                    {
                        path = "/api/projects?includeArchived=true";
                        var value = InvokeRecycleRead(path);
                        if (!(value is object[] list)) throw StopRecycleMalformed("projects", path, "RecycleProjectUnresolved");
                        projects = list;
                    }
                    var projectMatches = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    var order = new List<string>();
                    var urlMatches = new HashSet<string>(StringComparer.Ordinal);
                    var pathMatches = new HashSet<string>(StringComparer.Ordinal);
                    var checkout = ConvertToRecycleRepositoryPath(_repoRoot);
                    foreach (var project in projects)
                    {
                        object Q(string key) => RollingPs.Prop(project, key);
                        foreach (var key in new[] { "id", "archivedAt", "gitRepositoryUrl", "localRepositoryPath" })
                            if (!RollingPs.HasExact(project, key)) throw StopRecycleMalformed(key, path, "RecycleProjectUnresolved");
                        if (!TestRecycleGuid(Q("id"))) throw StopRecycleMalformed("id", path, "RecycleProjectUnresolved");
                        if (explicitId && !RollingPs.Ceq(Q("id"), _requestedRecycleProjectId)) throw StopRecycleMalformed("id", path, "RecycleProjectUnresolved");
                        if (!TestRecycleTimestamp(Q("archivedAt"))) throw StopRecycleMalformed("archivedAt", path, "RecycleProjectUnresolved");
                        foreach (var key in new[] { "gitRepositoryUrl", "localRepositoryPath" })
                            if (Q(key) != null && !(Q(key) is string)) throw StopRecycleMalformed(key, path, "RecycleProjectUnresolved");
                        if ((explicitId || PhaseIs("check-census")) && !(Q("name") is string)) throw StopRecycleMalformed("name", path, "RecycleProjectUnresolved");
                        string localPath;
                        try { localPath = ConvertToRecycleRepositoryPath((string)Q("localRepositoryPath")); }
                        catch (Exception) { throw StopRecycleMalformed("localRepositoryPath", path, "RecycleProjectUnresolved"); }
                        var urlMatch = ConvertToRecycleRepositoryUrl((string)Q("gitRepositoryUrl")) == "https://github.com/michal-ciechan/antiphon";
                        var pathMatch = localPath.Length > 0 && string.Equals(localPath, checkout, StringComparison.Ordinal);
                        var id = (string)Q("id");
                        if (urlMatch) urlMatches.Add(id);
                        if (pathMatch) pathMatches.Add(id);
                        if (explicitId || urlMatch || pathMatch)
                        {
                            if (projectMatches.TryGetValue(id, out var seen) && !string.Equals(RollingPs.Json(seen), RollingPs.Json(project), StringComparison.Ordinal))
                                throw StopRecycleMalformed("id", path, "RecycleProjectUnresolved");
                            if (!projectMatches.ContainsKey(id)) order.Add(id);
                            projectMatches[id] = project;
                        }
                    }
                    if (projectMatches.Count == 0)
                        throw new Server2RollingRefusal("RecycleProjectUnresolved cause=NoMatch candidates=" + projects.Length + " urlMatches=" + urlMatches.Count + " pathMatches=" + pathMatches.Count);
                    if (projectMatches.Count > 1)
                        throw new Server2RollingRefusal("RecycleProjectUnresolved cause=Ambiguous candidates=" + projects.Length + " matches=" + projectMatches.Count);
                    var selected = projectMatches[order[0]];
                    var selectedId = RollingPs.Str(RollingPs.Prop(selected, "id"));
                    if (RollingPs.Prop(selected, "archivedAt") != null) throw new Server2RollingRefusal("RecycleProjectUnresolved cause=Archived id=" + selectedId);
                    var resolvedBy = explicitId ? "explicit" : urlMatches.Contains(selectedId) && pathMatches.Contains(selectedId) ? "both"
                        : urlMatches.Contains(selectedId) ? "url" : "path";
                    if (_recycleReceipt != null)
                    {
                        var name = RollingPs.Prop(selected, "name");
                        _recycleReceipt["project"] = RollingPs.Object(("id", RollingPs.Prop(selected, "id")), ("resolvedBy", resolvedBy), ("name", name is string ? name : null));
                    }
                    _resolvedRecycleProjectId = selectedId;
                    return _resolvedRecycleProjectId;
                }
                finally { WriteRecycleReceipt(); }
            }

            private object AssertRecycleTaskCensus(string runnerId, string projectId, bool report = false)
            {
                // CARD-1371: restart the whole two-pass census on cross-read count drift only. Never accept partial closures or send
                // Drift into the separate bound-land wait.
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        try { return CensusPasses(runnerId, projectId, report); }
                        finally { WriteRecycleReceipt(); }
                    }
                    catch (Exception e) when (attempt < 3 && Regex.IsMatch(e.Message, "^RecycleTaskCensusUnknown cause=Drift "))
                    {
                        // Use the existing offline clock seam only with the fake HTTP transport.
                        if (_timing.StubbedReads && _timing.PollMs.HasValue) Sleep(_timing.PollMs.Value);
                        else Sleep(30000);
                    }
                }
            }

            // Open work and pending lands each form a complete, server-filtered closure. No time window: an old terminal task can still
            // have a pending land. Compare both closures twice; filtered excluded counts must reconcile independently.
            private object CensusPasses(string runnerId, string projectId, bool report)
            {
                string previous = null;
                // CARD-1693 / CARD-1697: for either runner the compared open closure keeps bound rows only, with no per-scope id lists,
                // like the land closure (CARD-1353 D-2). Off-runner work starting and finishing between the passes cannot end Unstable.
                // Every row is still read, shape-checked and receipted, and a bound open row or bound land still refuses in either pass.
                var pass0Settled = new HashSet<string>(StringComparer.Ordinal);
                var started = Timestamp();
                int openCount = 0, boundCount = 0;
                var pendingLands = new HashSet<string>(StringComparer.Ordinal);
                var boundLands = new HashSet<string>(StringComparer.Ordinal);
                var allScopes = new HashSet<string>(StringComparer.Ordinal);
                for (var pass = 0; pass < 2; pass++)
                {
                    var settledThisPass = new HashSet<string>(StringComparer.Ordinal);
                    var closures = new PlainObject();
                    pendingLands = new HashSet<string>(StringComparer.Ordinal);
                    boundLands = new HashSet<string>(StringComparer.Ordinal);
                    allScopes = new HashSet<string>(StringComparer.Ordinal);
                    openCount = 0;
                    boundCount = 0;
                    foreach (var kind in new[] { "open", "land" })
                    {
                        var pending = new Queue<string>();
                        pending.Enqueue(projectId);
                        var scopes = new HashSet<string>(StringComparer.Ordinal);
                        var rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var scopeCounts = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        var countDrifts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var landEvidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        string path = null;
                        while (pending.Count > 0)
                        {
                            var scope = pending.Dequeue();
                            if (!scopes.Add(scope)) continue;
                            allScopes.Add(scope);
                            if (scopes.Count > 1000) throw new Server2RollingRefusal("RecycleTaskCensusUnknown cause=ScopeLimit scopes=" + scopes.Count);
                            var filter = kind == "open" ? "&status=Queued,Dispatched,Working,Blocked" : "&landPending=true";
                            path = "/api/agent-tasks?projectId=" + scope + "&unscoped=include&includeChecks=true" + filter;
                            var envelope = InvokeRecycleRead(path, retryTransient: true);
                            object E(string name) => RollingPs.Prop(envelope, name);
                            object X(string name) => RollingPs.Prop(E("excluded"), name);
                            if (!(envelope is PlainObject)) throw StopRecycleMalformed("envelope", path);
                            if (!(E("items") is object[])) throw StopRecycleMalformed("items", path);
                            if (!(E("excluded") is PlainObject)) throw StopRecycleMalformed("excluded", path);
                            if (!(X("byProject") is object[])) throw StopRecycleMalformed("excluded.byProject", path);
                            var scopeProject = RollingPs.Prop(E("scope"), "projectId");
                            if (!(scopeProject is string) || !RollingPs.Ceq(scopeProject, scope)) throw StopRecycleMalformed("scope.projectId", path);
                            var scopeUnscoped = RollingPs.Prop(E("scope"), "unscoped");
                            if (!(scopeUnscoped is string) || !RollingPs.Ceq(scopeUnscoped, "include")) throw StopRecycleMalformed("scope.unscoped", path);
                            foreach (var key in new[] { "total", "unscoped" })
                                if (!ValidCount(X(key))) throw StopRecycleMalformed("excluded." + key, path);
                            if (!RollingPs.Eq(X("unscoped"), 0)) throw StopRecycleMalformed("excluded.unscoped", path);
                            long withheldCount = 0;
                            var excludedScopes = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var excluded in (object[])X("byProject"))
                            {
                                var excludedId = RollingPs.Str(RollingPs.Prop(excluded, "projectId"));
                                if (!TestRecycleGuid(RollingPs.Prop(excluded, "projectId")) || RollingPs.Eq(excludedId, scope) || !excludedScopes.Add(excludedId))
                                    throw StopRecycleMalformed("excluded.byProject.projectId", path);
                                var count = RollingPs.Prop(excluded, "count");
                                if (!RollingPs.HasExact(excluded, "count") || !RollingPs.IsInt(count) || RollingPs.Long(count) <= 0)
                                    throw StopRecycleMalformed("excluded.byProject.count", path);
                                withheldCount += RollingPs.Long(count);
                                if (scopeCounts.TryGetValue(excludedId, out var known))
                                {
                                    if (!RollingPs.Eq(known, count) && !countDrifts.ContainsKey(excludedId))
                                        countDrifts[excludedId] = "expected=" + RollingPs.Str(known) + " observed=" + RollingPs.Str(count) + " path=" + path;
                                }
                                else scopeCounts[excludedId] = count;
                                pending.Enqueue(excludedId);
                            }
                            if (!RollingPs.Eq(withheldCount, X("total"))) throw StopRecycleMalformed("excluded.total", path);
                            foreach (var task in (object[])E("items"))
                            {
                                object T(string name) => RollingPs.Prop(task, name);
                                foreach (var key in TaskKeys)
                                    if (!RollingPs.Has(task, key)) throw StopRecycleMalformed(key, path);
                                var id = RollingPs.Str(T("id"));
                                if (!TestRecycleGuid(T("id"))) throw StopRecycleMalformed("id", path);
                                if (!(T("status") is string) || !RollingPs.CIn(T("status"), "Queued", "Dispatched", "Working", "Blocked", "Succeeded", "Failed", "Canceled"))
                                    throw StopRecycleMalformed("status", path);
                                if (T("runnerId") != null && !(T("runnerId") is string)) throw StopRecycleMalformed("runnerId", path);
                                if (!(T("scopeSource") is string) || !RollingPs.CIn(T("scopeSource"), "Task", "Card", "None")) throw StopRecycleMalformed("scopeSource", path);
                                if ((T("projectId") != null && !TestRecycleGuid(T("projectId")))
                                    || (RollingPs.Eq(T("scopeSource"), "None") && T("projectId") != null)
                                    || (!RollingPs.Eq(T("scopeSource"), "None") && !RollingPs.Ceq(RollingPs.Str(T("projectId")), scope)))
                                    throw StopRecycleMalformed("projectId", path);
                                foreach (var key in new[] { "landRequestedAt", "landStartedAt" })
                                    if (!TestRecycleTimestamp(T(key))) throw StopRecycleMalformed(key, path);
                                var reduced = RollingPs.Object(("id", id), ("status", RollingPs.Str(T("status"))), ("runnerId", T("runnerId")),
                                    ("projectId", T("projectId")), ("scopeSource", T("scopeSource")), ("landRequestedAt", T("landRequestedAt")),
                                    ("landStartedAt", T("landStartedAt")));
                                var facts = RollingPs.Json(reduced);
                                if (rows.TryGetValue(id, out var seen) && !string.Equals(seen, facts, StringComparison.Ordinal)) throw StopRecycleMalformed("id", path);
                                rows[id] = facts;
                                if (_recycleReceipt != null && (kind == "open" || !_recycleReceiptTasks.ContainsKey(id))) _recycleReceiptTasks[id] = reduced;
                                var landed = T("landRequestedAt") != null || T("landStartedAt") != null;
                                if ((kind == "open" && !RollingPs.CIn(T("status"), OpenStatuses)) || (kind == "land" && !landed))
                                {
                                    if (_recycleReceipt != null && _reads.Count > 0) ((PlainObject)_reads[_reads.Count - 1])["outcome"] = "UnfilteredRow";
                                    throw new Server2RollingRefusal("RecycleTaskCensusUnknown cause=UnfilteredRow id=" + id + " status=" + RollingPs.Str(T("status")) + " path=" + path);
                                }
                                // CARD-1353 D-1: the land rule shares the RecycleBoundTasks runner predicate. An off-runner pending land is
                                // read, counted and recorded, never refused.
                                var bound = RollingPs.Eq(RollingPs.Str(T("runnerId")), runnerId);
                                if (landed)
                                {
                                    pendingLands.Add(id);
                                    if (bound) { boundLands.Add(id); if (!report) throw new Server2RollingRefusal("RecycleLandInFlight " + id); }
                                }
                                if (kind == "open" && bound && RollingPs.CIn(T("status"), OpenStatuses))
                                {
                                    if (!report) throw new Server2RollingRefusal("RecycleBoundTasks " + id + " status=" + RollingPs.Str(T("status")) + " runner=" + runnerId);
                                }
                                if (kind == "land") continue;
                                var detail = InvokeRecycleRead("/api/agent-tasks/" + id, retryTransient: true);
                                var summary = RollingPs.Prop(detail, "summary");
                                if (!RollingPs.Has(detail, "landRequest") || !RollingPs.Eq(RollingPs.Str(RollingPs.Prop(summary, "id")), id))
                                    throw new Server2RollingRefusal("RecycleLandUnknown");
                                var landRequest = RollingPs.Prop(detail, "landRequest");
                                // A short-lived task can settle between the open list and this detail. Terminal status plus a null
                                // landRequest is proven no-land, so status may move. Any other summary disagreement, a missing landRequest,
                                // or an unproven landRequest still refuses.
                                var settledWithoutLand = landRequest == null && RollingPs.CIn(RollingPs.Str(RollingPs.Prop(summary, "status")), "Succeeded", "Failed", "Canceled");
                                var openStatusMove = TestRecycleOpenStatusMove(task, summary, detail, runnerId);
                                foreach (var key in TaskKeys)
                                {
                                    if (!RollingPs.Has(summary, key)) throw new Server2RollingRefusal("RecycleLandUnknown");
                                    if ((settledWithoutLand || openStatusMove) && key == "status") continue;
                                    if (!SameJson(RollingPs.Prop(summary, key), T(key))) throw new Server2RollingRefusal("RecycleLandUnknown");
                                }
                                if (settledWithoutLand) settledThisPass.Add(id);
                                AdmitDetailLand(landRequest, id, bound, report, pendingLands, boundLands);
                                var proof = landRequest == null ? "null"
                                    : RollingPs.Json(RollingPs.Object(("state", RollingPs.Prop(landRequest, "state")), ("terminalEventId", RollingPs.Prop(landRequest, "terminalEventId"))));
                                if (landEvidence.TryGetValue(id, out var evidence) && !string.Equals(evidence, proof, StringComparison.Ordinal))
                                    throw new Server2RollingRefusal("RecycleLandUnknown");
                                landEvidence[id] = proof;
                                if (_recycleReceipt != null) reduced["landRequest"] = RollingPs.ParseEnumerated(proof);
                            }
                        }
                        foreach (var scopeId in RollingPs.Sorted(scopeCounts.Keys).ToArray())
                        {
                            var actual = rows.Values.Select(RollingPs.ParseEnumerated).Count(f => RollingPs.Eq(RollingPs.Prop(f, "projectId"), scopeId));
                            if (!RollingPs.Eq(actual, scopeCounts[scopeId]) && !countDrifts.ContainsKey(scopeId))
                                countDrifts[scopeId] = "expected=" + RollingPs.Str(scopeCounts[scopeId]) + " observed=" + actual + " path=" + path;
                        }
                        // All local shapes and binding/land guards have run before classifying cross-read drift.
                        if (countDrifts.Count > 0)
                        {
                            var scopeId = RollingPs.Sorted(countDrifts.Keys).First();
                            throw new Server2RollingRefusal("RecycleTaskCensusUnknown cause=Drift field=excluded.byProject.count project=" + scopeId + " " + countDrifts[scopeId]);
                        }
                        var scopeSnapshot = new PlainObject();
                        // CARD-1353 D-2 / CARD-1697: the compared open and land closures keep bound rows only (and no scopes entry), so
                        // off-runner work or an off-runner land moving during the audits cannot end Unstable. The rows and the receipt
                        // stay complete.
                        var taskSnapshot = BoundProjection(rows, runnerId);
                        var landSnapshot = new PlainObject();
                        foreach (var key in RollingPs.Sorted(landEvidence.Keys).ToArray())
                        {
                            if (!taskSnapshot.Has(key)) continue;
                            landSnapshot[key] = RollingPs.ParseEnumerated(landEvidence[key]);
                        }
                        closures[kind] = kind == "open" ? RollingPs.Object(("scopes", scopeSnapshot), ("tasks", taskSnapshot)) : RollingPs.Object(("tasks", taskSnapshot));
                        if (kind == "open")
                        {
                            ((PlainObject)closures[kind])["land"] = landSnapshot;
                            openCount = rows.Count;
                            boundCount = taskSnapshot.Entries.Count(e => RollingPs.Eq(RollingPs.Prop(e.Value, "runnerId"), runnerId));
                        }
                    }
                    var snapshot = RollingPs.Json(closures, 20);
                    if (_recycleReceipt != null) _recycleReceipt["snapshot"] = closures;
                    if (pass == 0) pass0Settled = settledThisPass;
                    // A pass-0 task recorded as settled without a land may leave the pass-1 list. Safe off-runner open-status changes
                    // align comparison copies only. All residual drift refuses.
                    if (pass == 1 && !string.Equals(snapshot, previous, StringComparison.Ordinal)
                        && !TestRecycleForgivenDrift(previous, snapshot, pass0Settled, runnerId))
                        throw new Server2RollingRefusal("RecycleTaskCensusUnknown cause=Unstable");
                    previous = snapshot;
                }
                if (report)
                {
                    return new CensusReport
                    {
                        Open = openCount, BoundOpen = boundCount, LandPending = pendingLands.Count, BoundLandPending = boundLands.Count,
                        Scopes = allScopes.Count, ElapsedMs = ElapsedMs(started),
                    };
                }
                return previous;
            }

            // CARD-1353 D-3/D-4. A land bound to the recycled runner makes the census wait, bounded, instead of refusing on the first
            // read. Only RecycleLandInFlight is retried; every other refusal is immediate. --land-wait-minutes 0 refuses on the first
            // read. On expiry the refusal keeps its first token: RecycleLandInFlight <id> runner=<runner> waited=<n>polls. The receipt
            // records landWait.
            private object WaitRecycleTaskCensus(string runnerId, string projectId)
            {
                var deadline = UtcNow.AddMinutes(_o.LandWaitMinutes);
                var pollMs = 30000;
                var polls = 0;
                if (_o.LandWaitMinutes > 0 && _timing.LandWaitMs.HasValue) deadline = UtcNow.AddMilliseconds(_timing.LandWaitMs.Value);
                if (_timing.PollMs.HasValue) pollMs = _timing.PollMs.Value;
                while (true)
                {
                    try
                    {
                        var snapshot = AssertRecycleTaskCensus(runnerId, projectId);
                        if (_recycleReceipt != null)
                        {
                            _recycleReceipt["landWait"] = RollingPs.Object(("polls", polls), ("outcome", polls > 0 ? "cleared" : "none"));
                            WriteRecycleReceipt();
                        }
                        return snapshot;
                    }
                    catch (Exception e)
                    {
                        // CARD-1371. Resume continues a recycle that may already have removed containers. Exhausted count drift must
                        // not stop that resume; the host census still refuses Drift until the journal says the containers are gone.
                        if (Resume && Regex.IsMatch(e.Message, "^RecycleTaskCensusUnknown cause=Drift ")) return null;
                        var land = Regex.Match(e.Message, "^RecycleLandInFlight ([0-9a-f-]{36})$");
                        if (!land.Success) throw;
                        var landId = land.Groups[1].Value;
                        if (UtcNow >= deadline)
                        {
                            if (_recycleReceipt != null)
                            {
                                _recycleReceipt["landWait"] = RollingPs.Object(("polls", polls), ("outcome", "expired"));
                                WriteRecycleReceipt();
                            }
                            throw new Server2RollingRefusal("RecycleLandInFlight " + landId + " runner=" + runnerId + " waited=" + polls + "polls");
                        }
                        // The console, not the function's output: every call site voids the function's output.
                        Out("RECYCLE_LAND_WAIT runner=" + runnerId + " id=" + landId + " polls=" + polls);
                        Sleep(pollMs);
                        polls++;
                    }
                }
            }
        }
    }
}
