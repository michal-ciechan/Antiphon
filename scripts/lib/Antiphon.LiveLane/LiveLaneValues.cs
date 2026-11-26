// The few small helpers the live-lane project needs, copied here so the project references nothing that changes often
// (the owner's isolation rule, 2026-11-26): JSON reads with the PowerShell hashtable semantics the ported scripts had,
// the instant comparison of a lock record's start time, and SHA-256 hex. Plain C#, framework only. ASCII only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.LiveLane
{
    /// <summary>Reads of terminal evidence and lane context the way the PowerShell read its hashtables.</summary>
    public static class LiveLaneValues
    {
        /// <summary>
        /// $o.Name on a hashtable: the value under the key, the exact spelling first and otherwise the first key equal
        /// ignoring case. Null when o is not an object or has no such key.
        /// </summary>
        public static JsonNode Field(JsonNode o, string name)
        {
            if (!(o is JsonObject obj)) return null;
            if (obj.TryGetPropertyValue(name, out var exact)) return exact;
            foreach (var pair in obj)
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            return null;
        }

        /// <summary>$o.Keys -ccontains name: a key with exactly this spelling.</summary>
        public static bool HasExactKey(JsonNode o, string name) => o is JsonObject obj && obj.ContainsKey(name);

        /// <summary>The value is a JSON string (PowerShell's -is [string]); its text, otherwise null.</summary>
        public static string AsString(JsonNode node) =>
            node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

        /// <summary>
        /// The text a comparison sees ([string]$x): a string as it is, a number as written, a boolean as True or False;
        /// null for null, a missing value, an object or an array.
        /// </summary>
        public static string Text(JsonNode node)
        {
            if (!(node is JsonValue value)) return null;
            switch (value.GetValueKind())
            {
                case JsonValueKind.String: return value.GetValue<string>();
                case JsonValueKind.Number: return value.ToJsonString();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                default: return null;
            }
        }

        /// <summary>Test-LiveLaneInteger: an integral JSON number ([int] or [long] after ConvertFrom-Json).</summary>
        public static bool IsInteger(JsonNode node, out long number)
        {
            number = 0;
            return node is JsonValue value && value.GetValueKind() == JsonValueKind.Number
                && long.TryParse(value.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);
        }

        /// <summary>The value is a JSON boolean (-is [bool]).</summary>
        public static bool IsBool(JsonNode node, out bool flag)
        {
            flag = false;
            if (!(node is JsonValue value)) return false;
            var kind = value.GetValueKind();
            if (kind != JsonValueKind.True && kind != JsonValueKind.False) return false;
            flag = kind == JsonValueKind.True;
            return true;
        }

        /// <summary>@($x): an array's items (nulls kept), nothing for null, one item for anything else.</summary>
        public static IReadOnlyList<JsonNode> Items(JsonNode node)
        {
            if (node == null) return Array.Empty<JsonNode>();
            if (node is JsonArray array)
            {
                var items = new List<JsonNode>(array.Count);
                foreach (var item in array) items.Add(item);
                return items;
            }
            return new[] { node };
        }

        /// <summary>Ordinal equality, null equal to null (-ceq on two values).</summary>
        public static bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);

        /// <summary>
        /// ReleaseGateCredit.SameInstant for two texts: both blank is the same, one blank is not; two parseable times
        /// compare as UTC instants, anything else ignoring case. A lock record's processStartedAt round-trips this way.
        /// </summary>
        public static bool SameInstant(string left, string right)
        {
            var leftEmpty = string.IsNullOrWhiteSpace(left);
            var rightEmpty = string.IsNullOrWhiteSpace(right);
            if (leftEmpty && rightEmpty) return true;
            if (leftEmpty || rightEmpty) return false;
            if (Instant(left, out var l) && Instant(right, out var r)) return l == r;
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Instant(string text, out DateTime instant) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out instant);

        /// <summary>Lower-case hex SHA-256 of the UTF-8 bytes of text (no byte order mark).</summary>
        public static string Sha256Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""))).ToLowerInvariant();
    }
}
