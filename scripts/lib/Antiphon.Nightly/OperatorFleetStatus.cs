// CARD-2087: project and fleet facts from the existing read APIs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Antiphon.Nightly.OperatorRcStatus;

namespace Antiphon.Nightly
{
    internal static class OperatorFleetStatus
    {
        internal const string LandsPath = "/api/agent-tasks?landPending=true";
        internal const string AttentionPath = "/api/attention";

        private static string DetailPath(Guid id) => "/api/agent-tasks/" + id;

        private static string SlotsPath(string runner) => "/api/session-runners/" + Uri.EscapeDataString(runner) + "/slots";

        private static string RecentPath(string start) => "/api/agent-tasks?since=" + Uri.EscapeDataString(start);

        /// <summary>The detail and runner census reads Read will make, so they can be requested at once (D-5).</summary>
        internal static IEnumerable<string> Prefetch(JsonNode fleet, JsonNode scoped, JsonNode lands, string runner)
        {
            var ids = new List<string>();
            var stages = Field(fleet, "stages") as JsonArray ?? Field(scoped, "stages") as JsonArray;
            foreach (var stage in Rows(stages))
            {
                foreach (var bucket in new[] { "inFlight", "queued", "blocked" })
                    ids.AddRange(Rows(Field(stage, bucket)).Select(row => Text(row, "taskId")));
                ids.AddRange(Rows(Field(stage, "ready")).Select(row => Text(row, "sourcePlanTaskId")));
            }
            ids.AddRange(Rows(Field(lands, "items")).Select(row => Text(row, "id")));
            var paths = ids.Select(id => Guid.TryParse(id, out var key) ? DetailPath(key) : null).Where(path => path != null).ToList();
            var runners = string.IsNullOrEmpty(runner) ? Rows(Field(fleet ?? scoped, "runners")).Select(row => Text(row, "runnerId")) : new[] { runner };
            paths.AddRange(runners.Where(id => id != null).Select(SlotsPath));
            return paths.Distinct(StringComparer.Ordinal).ToArray();
        }

        /// <summary>The repair-candidate detail reads for the rc failures (waits for the recent-task list).</summary>
        internal static IEnumerable<string> RepairPrefetch(JsonNode rc, OperatorStatusReads reads)
        {
            var start = Text(Field(rc, "lastRun"), "startedAt");
            if (Field(rc, "failures") is not JsonArray failures || failures.Count == 0 || Text(rc, "runId") == null || Time(start) == null)
                return Array.Empty<string>();
            var recent = reads.Take(RecentPath(start), null);
            return RepairRows(Field(recent, "items") as JsonArray).Select(row => DetailPath(Guid.Parse(row.Id))).ToArray();
        }

        private static IEnumerable<(JsonNode Row, string Id)> RepairRows(JsonArray recent) =>
            Rows(recent).Where(row => Text(row, "role") is "Debug" or "Custom"
                    && Text(row, "title")?.StartsWith("rc failure:", StringComparison.OrdinalIgnoreCase) == true)
                .Select(row => (Row: row, Id: Text(row, "id")))
                .Where(row => Guid.TryParse(row.Id, out _));

        internal static void Read(JsonObject snapshot, JsonNode fleet, JsonNode scoped, string runner,
            Func<string, JsonNode> get, List<string> errors)
        {
            var details = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
            JsonNode Detail(string id)
            {
                if (!Guid.TryParse(id, out var key)) return null;
                if (!details.TryGetValue(id, out var detail))
                    details[id] = detail = get(DetailPath(key));
                return detail;
            }

            var projected = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            var stages = Field(fleet, "stages") as JsonArray;
            snapshot["taskScope"] = stages != null ? "fleet" : Field(scoped, "stages") is JsonArray ? "project" : null;
            if (stages == null)
            {
                errors.Add("Fleet pipeline stages unavailable.");
                stages = Field(scoped, "stages") as JsonArray;
            }
            foreach (var stage in Rows(stages))
            {
                foreach (var (bucket, state) in new[] { ("inFlight", (string)null), ("queued", "Queued"), ("blocked", "Blocked"), ("ready", "Succeeded") })
                {
                    if (Field(stage, bucket) is not JsonArray rows)
                    {
                        errors.Add("Fleet pipeline " + Text(stage, "role") + " " + bucket + " rows unavailable.");
                        continue;
                    }
                    foreach (var row in rows)
                    {
                        var ready = bucket == "ready";
                        var id = Text(row, ready ? "sourcePlanTaskId" : "taskId");
                        if (!Guid.TryParse(id, out _))
                        {
                            errors.Add("Fleet pipeline task identity unavailable in " + bucket + ".");
                            continue;
                        }
                        var role = ready ? Text(row, "sourceRole") : Text(stage, "role");
                        if (projected.ContainsKey(id)) continue;
                        var task = Task(Detail(id), row, id, role, state, ready);
                        projected[id] = task;
                        if (Text(task, "state") == "Blocked" && Text(task, "reason") == null)
                            errors.Add("Blocked reason unavailable for task " + ShortId(id) + ".");
                    }
                }
            }
            var fleetTasks = projected.Values.OrderBy(task => Text(task, "role"), StringComparer.Ordinal)
                .ThenBy(task => Text(task, "state"), StringComparer.Ordinal)
                .ThenBy(task => Text(task, "shortId"), StringComparer.Ordinal).ToArray();
            snapshot["tasks"] = new JsonArray(fleetTasks);
            snapshot["mergeInFlight"] = new JsonArray(fleetTasks.Where(task => Text(task, "role") == "Merge"
                && Text(task, "state") is "Dispatched" or "Working").Select(task => task.DeepClone()).ToArray());
            snapshot["fleetHosts"] = Field(fleet ?? scoped, "hosts")?.DeepClone();
            snapshot["fleetConcurrencyScopes"] = Field(fleet, "concurrencyScopes")?.DeepClone();
            snapshot["runnerDefaults"] = Field(fleet ?? scoped, "runnerDefaults")?.DeepClone();

            var lands = List(get, LandsPath, errors);
            if (lands != null)
            {
                var queue = new JsonArray();
                snapshot["landQueue"] = queue;
                foreach (var row in lands.OrderBy(row => Text(row, "landRequestedAt"), StringComparer.Ordinal)
                    .ThenBy(row => Text(row, "id"), StringComparer.Ordinal))
                {
                    var id = Text(row, "id");
                    if (!Guid.TryParse(id, out _))
                    {
                        errors.Add("Pending land task identity unavailable.");
                        continue;
                    }
                    var task = Task(Detail(id), row, id);
                    var request = Field(Detail(id), "landRequest");
                    task["landState"] = Text(request, "state") ?? (Text(row, "landStartedAt") == null ? "Pending" : "Started");
                    task["requestedAt"] = Text(request, "requestedAt") ?? Text(row, "landRequestedAt");
                    task["holdReason"] = FirstLine(Text(request, "holdDetail") ?? Text(request, "holdReasonCode"));
                    task["phase"] = Text(request, "statusPhase");
                    queue.Add(task);
                }
            }

            var catalogue = Field(fleet ?? scoped, "runners") as JsonArray;
            if (catalogue == null) catalogue = get("/api/session-runners") as JsonArray;
            snapshot["runnerCatalogue"] = catalogue?.DeepClone();
            var slots = new JsonObject();
            snapshot["runnerSlots"] = slots;
            var ids = string.IsNullOrEmpty(runner) ? Rows(catalogue).Select(row => Text(row, "runnerId")) : new[] { runner };
            foreach (var id in ids.Where(id => id != null).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.Ordinal))
            {
                var fact = get(SlotsPath(id));
                slots[id] = fact;
                if (fact != null && (Field(fact, "occupied") == null || Field(fact, "slots") is not JsonArray))
                    errors.Add("Runner " + id + " slot census unavailable (invalid shape).");
            }

            var attention = List(get, AttentionPath, errors);
            if (attention != null)
            {
                var risks = attention.Where(row => Field(row, "cardId") != null)
                    .Select(row => (JsonNode)new JsonObject
                    {
                        ["kind"] = Text(row, "kind"), ["severity"] = Text(row, "severity"),
                        ["cardId"] = Text(row, "cardId"), ["boardId"] = Text(row, "boardId"),
                        ["title"] = FirstLine(Text(row, "title")), ["reason"] = FirstLine(Text(row, "headline")),
                        ["task"] = ShortId(Text(row, "taskId")),
                    });
                snapshot["cardsAtRisk"] = new JsonArray(risks.ToArray());
            }

            var rc = Field(snapshot, "rc") as JsonObject;
            if (Field(rc, "failures") is JsonArray failures && failures.Count > 0)
                BindRepairs(rc, failures, get, Detail, errors);
        }

        private static void BindRepairs(JsonObject rc, JsonArray failures, Func<string, JsonNode> get,
            Func<string, JsonNode> detail, List<string> errors)
        {
            var run = Text(rc, "runId");
            var start = Text(rc["lastRun"], "startedAt");
            if (run == null || Time(start) == null)
            {
                errors.Add("rc repair positions unavailable without a bound run id and start time.");
                return;
            }
            var recent = List(get, RecentPath(start), errors);
            if (recent == null) return;
            var candidates = RepairRows(recent)
                .Select(row => (row.Row, row.Id, Detail: detail(row.Id)))
                .Where(row => ContainsToken(Text(row.Detail, "goal"), run)).ToArray();
            var unresolved = 0;
            foreach (var failure in failures.Cast<JsonObject>())
            {
                var name = Text(failure, "testName");
                var matches = candidates.Where(task => ContainsToken(Text(task.Row, "title"), name) ||
                    ContainsToken(Text(task.Detail, "goal"), name)).ToArray();
                if (matches.Length == 0)
                {
                    unresolved++;
                    continue;
                }
                failure["fixes"] = new JsonArray(matches.Select(task =>
                {
                    var position = Task(task.Detail, task.Row, task.Id);
                    var evidence = Field(task.Detail, "landing");
                    position["landingPublication"] = Text(evidence, "publication");
                    position["landingRef"] = Text(evidence, "destinationRef");
                    position["landingSha"] = Text(evidence, "verifiedSha");
                    return (JsonNode)position;
                }).ToArray());
            }
            if (unresolved > 0)
                errors.Add("rc repair positions unverified for " + unresolved + " failure(s); no task names both the run and test.");
        }

        private static bool ContainsToken(string text, string value) =>
            text != null && value != null && Regex.IsMatch(text, @"(?<!\w)(?<!\w\.)" + Regex.Escape(value) + @"(?!\w|\.\w)",
                RegexOptions.CultureInvariant);

        private static JsonObject Task(JsonNode detail, JsonNode row, string id, string role = null, string state = null, bool ready = false)
        {
            var summary = Field(detail, "summary") ?? row;
            var blocked = Field(detail, "blocked");
            var status = Text(summary, "status") ?? state;
            return new JsonObject
            {
                ["taskId"] = id,
                ["shortId"] = Text(row, ready ? "sourcePlanShortId" : "shortId") ?? ShortId(id),
                ["role"] = Text(summary, "role") ?? role,
                ["state"] = status,
                ["title"] = FirstLine(Text(summary, "title") ?? Text(row, "title") ?? Text(Field(row, "card"), "title")),
                ["runner"] = Text(summary, "runnerId") ?? Text(row, "runnerId"),
                ["card"] = Text(summary, "cardIdentifier") ?? Text(Field(row, "card"), "identifier"),
                ["branch"] = Text(summary, "worktreeBranch"),
                ["nextStage"] = Text(detail, "nextStage"),
                ["handoff"] = FirstLine(Text(detail, "nextHandoff")),
                ["reason"] = FirstLine(Text(blocked, "question") ?? Text(detail, "failureReason") ?? Text(blocked, "reason") ?? Text(row, "queueReason"))
                    ?? (Field(row, "routingExhausted")?.ToJsonString() == "true" ? "routing exhausted" : null),
            };
        }

        private static JsonArray List(Func<string, JsonNode> get, string path, List<string> errors)
        {
            var response = get(path);
            if (response == null) return null;
            if (Field(response, "items") is JsonArray items) return items;
            errors.Add("GET " + path + " unavailable (items shape invalid).");
            return null;
        }

        private static string ShortId(string id) => id == null || id.Length <= 8 ? id : id.Substring(0, 8);

        private static string FirstLine(string text) => text?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);

        private static JsonNode Field(JsonNode node, string name) => node is JsonObject obj ? obj[name] : null;

        private static IEnumerable<JsonNode> Rows(JsonNode node) => node is JsonArray array ? array : Enumerable.Empty<JsonNode>();

        internal static void Print(JsonObject snapshot, TextWriter output, IEnumerable<string> errors)
        {
            output.WriteLine("OBSERVED " + Show(snapshot, "observedAtUtc") + " project=" + Show(snapshot, "projectId"));
            output.WriteLine("MASTER " + Show(snapshot["master"], "tip") + " (remote)");
            foreach (var rcRef in Rows(snapshot["rcRefs"]))
                output.WriteLine("rc REF " + Show(rcRef, "ref") + " " + Show(rcRef, "sha"));

            var rc = snapshot["rc"];
            var active = Field(rc, "activeChunk");
            output.WriteLine("rc RUN " + Show(rc, "candidateId") + " pin=" + Show(rc, "pin") + " run=" + Show(rc, "runId")
                + " phase=" + Show(rc, "phase") + " elapsed=" + Duration(Field(rc, "elapsedSeconds"))
                + " TRX=" + Show(rc, "trxCount") + "/" + Show(rc, "scheduledEntries") + " active=" + Show(active, "state")
                + "/" + Show(active, "suite") + "/" + Show(active, "chunk"));
            var heartbeat = Field(rc, "heartbeat");
            output.WriteLine("rc HEARTBEAT at=" + (Text(heartbeat, "observedAtUtc") ?? Show(heartbeat, "utc"))
                + " idleMinutes=" + Show(heartbeat, "idleMinutes") + " hangSuspected=" + Show(heartbeat, "hangSuspected")
                + " age=" + Duration(Field(rc, "heartbeatAgeSeconds")));
            var eta = Field(rc, "eta");
            if (eta != null)
            {
                var bound = Field(eta, "lowerBound")?.ToJsonString() == "true" ? "at least " : "";
                output.WriteLine("rc ETA " + (Text(eta, "status") ?? bound + Show(eta, "projectedFinishUtc"))
                    + " remaining=" + Duration(Field(eta, "projectedRemainingSeconds")) + " ratio=" + Show(eta, "ratio")
                    + " p95=" + Show(eta, "p95FinishUtc") + " confidence=" + Show(eta, "confidence")
                    + " unknownRemaining=" + Show(eta, "unknownRemaining") + " finishedWithoutBaseline=" + Show(eta, "finishedWithoutBaseline")
                    + " previous=" + Show(eta, "previousRunLogDir"));
                output.WriteLine("rc ETA " + Show(eta, "startupOverheadNote")
                    + (Field(eta, "observedStartupSeconds") == null ? "" : " Observed startup=" + Duration(Field(eta, "observedStartupSeconds")) + "."));
                var largest = Rows(Field(eta, "biggestRemaining")).Select(row => Show(row, "chunk") + "=" + Duration(Field(row, "previousSeconds"))).ToArray();
                if (largest.Length > 0) output.WriteLine("rc BIGGEST REMAINING " + string.Join(", ", largest));
            }
            var failures = Field(rc, "failures") as JsonArray;
            output.WriteLine("rc FAILURES " + (failures == null ? "unknown" : failures.Count.ToString(CultureInfo.InvariantCulture)));
            foreach (var failure in Rows(failures))
            {
                var fixes = Field(failure, "fixes") as JsonArray;
                output.WriteLine("  " + Show(failure, "chunk") + " " + Show(failure, "testName") + " fix="
                    + (fixes == null ? "UNVERIFIED" : string.Join(", ", fixes.Select(fix => Show(fix, "shortId") + " "
                        + Show(fix, "state") + " branch=" + Show(fix, "branch") + " publication=" + Show(fix, "landingPublication")
                        + " ref=" + Show(fix, "landingRef")))));
            }

            var counts = snapshot["counts"];
            output.WriteLine("PROJECT TASKS inFlight=" + Show(counts, "inFlight") + " queued=" + Show(counts, "queued")
                + " blocked=" + Show(counts, "blocked"));
            foreach (var scope in Rows(Field(snapshot["pipeline"], "concurrencyScopes")))
            {
                var effective = Field(scope, "effective");
                var occupancy = Field(scope, "occupancy");
                output.WriteLine("PROJECT CAP parallel=" + Show(effective, "maxParallel") + " queued=" + Show(effective, "maxQueued")
                    + " parallelRemaining=" + Show(occupancy, "parallelRemaining") + " queuedRemaining=" + Show(occupancy, "queuedRemaining"));
                var byRole = Rows(Field(occupancy, "roles")).ToLookup(row => Text(row, "role"), StringComparer.OrdinalIgnoreCase);
                var caps = Rows(Field(effective, "roles")).Select(role =>
                {
                    var name = Text(role, "role");
                    var used = byRole[name].FirstOrDefault();
                    return name + " " + Show(role, "maxParallel") + "/" + Show(role, "maxQueued")
                        + " remain " + Show(used, "parallelRemaining") + "/" + Show(used, "queuedRemaining");
                }).ToArray();
                if (caps.Length > 0) output.WriteLine("ROLE CAPS parallel/queued " + string.Join(", ", caps));
            }
            foreach (var host in Rows(snapshot["fleetHosts"]))
                output.WriteLine("FLEET HOST " + Show(host, "hostId") + " inFlight=" + Show(host, "inFlight")
                    + " cap=" + Show(host, "effectiveLimit") + " remaining=" + Show(host, "remaining"));
            if (snapshot["runnerSlots"] is JsonObject slots)
                foreach (var (id, slot) in slots)
                {
                    var ghosts = (Field(slot, "slots") as JsonArray)?.Count(row => Show(row, "orphan") == "true" && Show(row, "occupiesCapacity") == "true");
                    output.WriteLine("RUNNER " + id + " occupied=" + Show(slot, "occupied") + "/" + Show(slot, "declaredCapacity")
                        + " orphan=" + (ghosts?.ToString(CultureInfo.InvariantCulture) ?? "unknown"));
                }
            foreach (var task in Rows(snapshot["tasks"]).Where(task => Text(task, "role") != "Merge" ||
                Text(task, "state") is not ("Dispatched" or "Working"))) PrintTask(output, "TASK", task);
            var scopeLabel = Text(snapshot, "taskScope") ?? "unknown";
            if (snapshot["mergeInFlight"] is JsonArray merges && merges.Count > 0)
                foreach (var task in merges) PrintTask(output, "MERGE " + scopeLabel, task);
            else
                output.WriteLine("MERGE " + scopeLabel + (scopeLabel == "unknown" ? "" : " none observed"));
            if (snapshot["landQueue"] is JsonArray queue && queue.Count > 0)
                foreach (var task in queue) PrintTask(output, "LAND " + Show(task, "landState"), task, Text(task, "holdReason"));
            else
                output.WriteLine(snapshot["landQueue"] == null ? "LAND queue unknown" : "LAND queue empty");
            foreach (var risk in Rows(snapshot["cardsAtRisk"]))
                output.WriteLine("CARD AT RISK " + Show(risk, "title") + " " + Show(risk, "kind") + " " + Show(risk, "reason"));
            foreach (var error in errors) output.WriteLine("UNVERIFIED " + error);
        }

        private static void PrintTask(TextWriter output, string label, JsonNode task, string hold = null)
        {
            var state = Show(task, "state");
            var next = Text(task, "nextStage");
            var reason = hold ?? Text(task, "reason");
            output.WriteLine(label + " " + Show(task, "role") + " " + state + " " + Show(task, "runner")
                + " " + Show(task, "shortId") + " " + (Text(task, "card") ?? "(no card)") + " " + Show(task, "title")
                + (next != null || state == "Succeeded" ? " next=" + (next ?? "unknown") : "")
                + (Text(task, "handoff") is string handoff ? " handoff=" + handoff : "")
                + (reason != null ? " reason=" + reason : ""));
        }

        private static string Show(JsonNode obj, string key) =>
            FirstLine(Text(obj, key)) ?? Field(obj, key)?.ToJsonString() ?? "unknown";

        private static string Duration(JsonNode value) => value is JsonValue number && number.TryGetValue<double>(out var seconds)
            ? (seconds / 60).ToString("0.0", CultureInfo.InvariantCulture) + "m" : "unknown";
    }
}
