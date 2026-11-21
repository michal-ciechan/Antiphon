// W2-nightly S05: the readiness evaluator's CARD-0487 notification ledger, ported from scripts/lib/nightly-health.ps1
// (notifications.json, notification-receipts.json, the logical identity, enqueue and receipt). CARD-0545 D-9 moved
// production notification to the independent watchdog: the production sink is null, so in production an unhealthy
// run with a destination persists its event and looks for a receipt but never sends. Only the NightlyHealthSeams
// sink of the tests exercises enqueue. Events and receipts are JSON objects as the files hold them (unknown fields
// are kept). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>Add-NightlyNotificationEvent's answer: whether the logical event was already stored, and the stored event.</summary>
    public sealed record NightlyNotificationAdd(bool Deduped, JsonObject Event);

    /// <summary>The notification ledger.</summary>
    public static class NightlyHealthNotifications
    {
        /// <summary>Get-NightlyNotificationStorePath.</summary>
        public static string StorePath(string stateRoot) => Path.Combine(stateRoot ?? "", "notifications.json");

        /// <summary>Get-NightlyRecipientReceiptPath.</summary>
        public static string ReceiptPath(string stateRoot) => Path.Combine(stateRoot ?? "", "notification-receipts.json");

        /// <summary>New-NightlyRunId: a new 32-hex-digit id.</summary>
        public static string NewId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// Read-NightlyNotificationStore: the stored events, empty when the file is missing (or when it does not parse,
        /// which also writes one error line).
        /// </summary>
        public static List<JsonObject> ReadEvents(string path, TextWriter error = null)
        {
            var events = new List<JsonObject>();
            var store = ReadFile(path, error);
            var items = HealthJson.Field(store, "events");
            // if ($store.events) { $events = @($store.events) }
            if (!HealthJson.IsTrue(items)) return events;
            foreach (var item in Items(items))
                if (item is JsonObject obj) events.Add((JsonObject)obj.DeepClone());
                else events.Add(null);
            return events;
        }

        /// <summary>Save-NightlyNotificationStore: { events: [...] } written atomically.</summary>
        public static void SaveEvents(string path, IEnumerable<JsonObject> events)
        {
            var array = new JsonArray();
            foreach (var item in events) array.Add(item?.DeepClone());
            HealthJson.WriteAtomic(path, new JsonObject { ["events"] = array });
        }

        /// <summary>
        /// Get-NightlyNotificationLogicalIdentity: workspace|localDueDate|windmillJobId|nativeRunId|sha|policyHash|failureKind.
        /// Two events with the same logical identity are one notification.
        /// </summary>
        public static string LogicalIdentity(JsonNode notification) =>
            string.Join("|", new[] { "workspace", "localDueDate", "windmillJobId", "nativeRunId", "sha", "policyHash", "failureKind" }
                .Select(name => HealthJson.Str(HealthJson.Field(notification, name))));

        /// <summary>New-NightlyNotificationIdentity: the logical identity and the notification id.</summary>
        public static string Identity(JsonNode notification) => LogicalIdentity(notification) + "|" + HealthJson.Str(HealthJson.Field(notification, "notificationId"));

        /// <summary>
        /// Add-NightlyNotificationEvent: an event whose logical identity is stored already answers that stored event
        /// (Deduped). Otherwise the event gets a notification id and status pending when it has none, and is appended
        /// before any enqueue.
        /// </summary>
        public static NightlyNotificationAdd AddEvent(string storePath, JsonObject notification, TextWriter error = null)
        {
            var events = ReadEvents(storePath, error);
            var logical = LogicalIdentity(notification);
            var existing = events.FirstOrDefault(item => HealthJson.Eq(LogicalIdentity(item), logical));
            if (existing != null) return new NightlyNotificationAdd(true, existing);
            if (string.IsNullOrWhiteSpace(HealthJson.Str(HealthJson.Field(notification, "notificationId")))) Set(notification, "notificationId", NewId());
            if (string.IsNullOrWhiteSpace(HealthJson.Str(HealthJson.Field(notification, "status")))) Set(notification, "status", "pending");
            events.Add(notification);
            SaveEvents(storePath, events);
            return new NightlyNotificationAdd(false, notification);
        }

        /// <summary>New-NightlyNotificationPayload: the message the sink would send, naming the failure, run, sha and notification id.</summary>
        public static JsonObject Payload(JsonNode notification, string destination)
        {
            var id = HealthJson.Str(HealthJson.Field(notification, "notificationId"));
            var run = HealthJson.Str(HealthJson.Field(notification, "nativeRunId"));
            var text = string.Format(CultureInfo.InvariantCulture, "Antiphon nightly monitor failureKind={0} run={1} sha={2} notificationId={3}",
                HealthJson.Str(HealthJson.Field(notification, "failureKind")), run, HealthJson.Str(HealthJson.Field(notification, "sha")), id);
            return new JsonObject { ["text"] = text, ["chat_id"] = destination ?? "", ["notificationId"] = id, ["nativeRunId"] = run };
        }

        /// <summary>
        /// Read-NightlyRecipientReceiptStore: the receipts of notification-receipts.json. A truthy receipts property gives
        /// its items, a top-level array its items, any other value is one receipt; a missing file or JSON null is none.
        /// </summary>
        public static List<JsonNode> ReadReceipts(string path, TextWriter error = null)
        {
            var receipts = new List<JsonNode>();
            if (!File.Exists(path)) return receipts;
            var store = ReadFile(path, error);
            if (store == null) return receipts;
            var items = HealthJson.Field(store, "receipts");
            if (HealthJson.IsTrue(items)) receipts.AddRange(Items(items).Select(item => item?.DeepClone()));
            else if (store is JsonArray array) receipts.AddRange(array.Select(item => item?.DeepClone()));
            else receipts.Add(store.DeepClone());
            return receipts;
        }

        /// <summary>Save-NightlyRecipientReceipt: appends { notificationId, nativeRunId, receivedAt } unless that pair is recorded.</summary>
        public static void SaveReceipt(string stateRoot, JsonNode notification, DateTime nowUtc, TextWriter error = null)
        {
            var path = ReceiptPath(stateRoot);
            var receipts = ReadReceipts(path, error);
            var id = HealthJson.Str(HealthJson.Field(notification, "notificationId"));
            var run = HealthJson.Str(HealthJson.Field(notification, "nativeRunId"));
            foreach (var item in receipts)
                if (HealthJson.Eq(HealthJson.Str(HealthJson.Field(item, "notificationId")), id) && HealthJson.Eq(HealthJson.Str(HealthJson.Field(item, "nativeRunId")), run)) return;
            receipts.Add(new JsonObject { ["notificationId"] = id, ["nativeRunId"] = run, ["receivedAt"] = nowUtc.ToString("o", CultureInfo.InvariantCulture) });
            var array = new JsonArray();
            foreach (var item in receipts) array.Add(item);
            HealthJson.WriteAtomic(path, new JsonObject { ["receipts"] = array });
        }

        /// <summary>Set-NightlyNotificationReceived: every stored event with the logical identity becomes status received.</summary>
        public static void MarkReceived(string storePath, JsonNode notification, TextWriter error = null)
        {
            var events = ReadEvents(storePath, error);
            var logical = LogicalIdentity(notification);
            foreach (var item in events)
                if (item != null && HealthJson.Eq(LogicalIdentity(item), logical)) Set(item, "status", "received");
            SaveEvents(storePath, events);
        }

        /// <summary>Invoke-NightlyNotificationEnqueue: the sink's answer; no sink throws (notification sink unavailable).</summary>
        public static JsonNode Enqueue(JsonObject notification, Func<JsonObject, JsonNode> sink)
        {
            if (sink == null) throw new InvalidOperationException("notification sink unavailable");
            return sink(notification);
        }

        /// <summary>Test-NightlyNotificationTransportAccepted: the event says transportAccepted, or its status is accepted or received.</summary>
        public static bool TransportAccepted(JsonNode notification)
        {
            if (notification == null) return false;
            if (HealthJson.IsTrue(HealthJson.Field(notification, "transportAccepted"))) return true;
            var status = HealthJson.Str(HealthJson.Field(notification, "status"));
            return HealthJson.Eq(status, "accepted") || HealthJson.Eq(status, "received");
        }

        /// <summary>Test-NightlyNotificationEnqueueAccepted: the answer says Enqueued, or its TransportStatus is 2xx.</summary>
        public static bool EnqueueAccepted(JsonNode notify)
        {
            if (!(notify is JsonObject)) return false;
            if (HealthJson.Has(notify, "Enqueued") && HealthJson.IsTrue(HealthJson.Field(notify, "Enqueued"))) return true;
            if (HealthJson.Has(notify, "TransportStatus"))
            {
                var code = HealthJson.CastInt(HealthJson.Field(notify, "TransportStatus"));
                if (code == null) return false;
                if (code.Value >= 200 && code.Value < 300) return true;
            }
            return false;
        }

        /// <summary>
        /// Set-NightlyNotificationTransportAccepted: every stored event with the logical identity gets transportAccepted,
        /// status accepted (unless received) and the answer's TransportStatus.
        /// </summary>
        public static void MarkTransportAccepted(string storePath, JsonNode notification, JsonNode notify = null, TextWriter error = null)
        {
            var events = ReadEvents(storePath, error);
            var logical = LogicalIdentity(notification);
            foreach (var item in events)
            {
                if (item == null || !HealthJson.Eq(LogicalIdentity(item), logical)) continue;
                Set(item, "transportAccepted", true);
                if (!HealthJson.Eq(HealthJson.Str(HealthJson.Field(item, "status")), "received")) Set(item, "status", "accepted");
                if (notify is JsonObject && HealthJson.Has(notify, "TransportStatus")) Set(item, "transportStatus", HealthJson.Field(notify, "TransportStatus")?.DeepClone());
            }
            SaveEvents(storePath, events);
        }

        /// <summary>Test-NightlyNotificationReceipt: a receipt in the view names the event's notification id and native run.</summary>
        public static bool HasReceipt(JsonNode notification, IEnumerable<JsonNode> view)
        {
            if (view == null) return false;
            var id = HealthJson.Str(HealthJson.Field(notification, "notificationId"));
            var run = HealthJson.Str(HealthJson.Field(notification, "nativeRunId"));
            foreach (var item in view)
                if (HealthJson.Eq(HealthJson.Str(HealthJson.Field(item, "notificationId")), id) && HealthJson.Eq(HealthJson.Str(HealthJson.Field(item, "nativeRunId")), run)) return true;
            return false;
        }

        /// <summary>Get-NightlyRecipientView: notification-receipts.json, then the seam's receipts (none in production).</summary>
        public static List<JsonNode> RecipientView(string stateRoot, Func<IEnumerable<JsonNode>> seam, TextWriter error = null)
        {
            var items = ReadReceipts(ReceiptPath(stateRoot), error);
            if (seam != null) items.AddRange(seam() ?? Enumerable.Empty<JsonNode>());
            return items;
        }

        // A property set the way Add-Member -Force sets a note property: the stored name keeps its case.
        private static void Set(JsonObject obj, string name, JsonNode value)
        {
            var stored = obj.Select(pair => pair.Key).FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
            obj[stored ?? name] = value;
        }

        private static IEnumerable<JsonNode> Items(JsonNode node)
        {
            if (node is JsonArray array) return array;
            return new[] { node };
        }

        private static JsonNode ReadFile(string path, TextWriter error)
        {
            if (!File.Exists(path)) return null;
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error?.WriteLine("nightly-health: cannot read " + path + ": " + NightlyHealth.Message(e));
                return null;
            }
            if (HealthJson.TryParse(text, out var node)) return node;
            error?.WriteLine("nightly-health: " + path + " is not JSON");
            return null;
        }
    }
}
