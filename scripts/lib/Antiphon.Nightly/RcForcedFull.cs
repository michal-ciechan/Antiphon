// CARD-1836 S8a forced-Full policy (moved from rc-forced-full.ps1). Pure policy: no selection, launch, persistence or
// S1 API changes. Callers supply findings from the admission/closure validators, not operator assertions; this policy
// neither authenticates receipts nor authorizes carries. Hashes bind complete executed closures and actual toolchain
// receipts. Unresolved imports or ownership are unresolved evidence; no equivalence override exists. The contract,
// evidence, anchor and closure are JSON documents. Plain C#: no PowerShell types.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public sealed class RcForcedFullDecision
    {
        public bool ForceFull { get; init; }
        public bool LaunchAll { get; init; }
        public string[] Reasons { get; init; }
        /// <summary>The anchor as given (never renewed); undefined when none was given.</summary>
        public JsonElement FullAnchor { get; init; }
    }

    public sealed class RcExecutedFullAnchor
    {
        public bool Eligible { get; init; }
        public string[] Reasons { get; init; }
        /// <summary>{ kind, admissionState, completedAtUtc, sourceSha, closureDigest } when eligible; otherwise null.</summary>
        public Dictionary<string, string> FullAnchor { get; init; }
        /// <summary>Pre-run forced-Full reasons carried as history.</summary>
        public JsonElement[] ForcedFull { get; init; }
    }

    public static class RcForcedFull
    {
        private static readonly string[] ContractFields =
        {
            "format", "inputMapVersion", "sdkVersion", "runtimeVersion", "platform", "executionImplementation",
            "policyHash", "catalogHash", "selectorHash", "skipResolverHash", "discoveryHash", "parserHash",
            "coverageHash", "launcherHash", "custodyHash", "buildHash", "harnessHash", "inputMapHash", "sdkHash", "runtimeHash",
        };

        private static readonly string[] CountFields = { "requiredCount", "executedCount", "expectedSkipCount", "carriedCount", "failedCount", "unexpectedSkipCount" };

        private static readonly Regex Digest64 = new Regex(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant);
        private static readonly Regex SourceShaShape = new Regex(@"\A(?:[0-9a-f]{40}|[0-9a-f]{64})\z", RegexOptions.CultureInvariant);
        private static readonly Regex MapVersion = new Regex(@"\A[1-9][0-9]*\z", RegexOptions.CultureInvariant);
        private static readonly Regex Version = new Regex(@"\A[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?\z", RegexOptions.CultureInvariant);
        private static readonly Regex UtcShape = new Regex(@"\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z\z", RegexOptions.CultureInvariant);
        private static readonly string[] UtcFormats = { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" };

        /// <summary>The member named exactly (case-sensitive) of a JSON object; undefined when absent or not an object.</summary>
        public static JsonElement Field(JsonElement o, string name)
        {
            if (o.ValueKind != JsonValueKind.Object) return default;
            foreach (var p in o.EnumerateObject()) if (p.Name == name) return p.Value;
            return default;
        }

        private static string Text(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary>A JSON string equal (ordinal) to expected.</summary>
        public static bool IsText(JsonElement value, string expected) => Text(value) == expected && expected != null;

        /// <summary>64 lower-case hex digits.</summary>
        public static bool IsDigest(string value) => value != null && Digest64.IsMatch(value);

        public static bool IsDigest(JsonElement value) => IsDigest(Text(value));

        /// <summary>40 or 64 lower-case hex digits.</summary>
        public static bool IsSourceSha(string value) => value != null && SourceShaShape.IsMatch(value);

        /// <summary>The input contract field's value is a string of its known shape.</summary>
        public static bool IsContractValue(string field, JsonElement value)
        {
            if (!(Text(value) is string s)) return false;
            if ((field ?? "").EndsWith("Hash", StringComparison.Ordinal)) return Digest64.IsMatch(s);
            switch (field)
            {
                case "format": return s == "antiphon-rc-input-contract-v1";
                case "inputMapVersion": return MapVersion.IsMatch(s);
                case "sdkVersion":
                case "runtimeVersion": return Version.IsMatch(s);
                case "platform": return s == "linux" || s == "windows";
                case "executionImplementation": return s == "compiled" || s == "fallback";
                default: return false;
            }
        }

        /// <summary>
        /// An explicit UTC timestamp yyyy-MM-ddTHH:mm:ss[.fffffff]Z at tick precision; null for anything else (local or
        /// unspecified time, culture-dependent text, missing time).
        /// </summary>
        public static DateTimeOffset? UtcTime(string value)
        {
            if (value == null || !UtcShape.IsMatch(value)) return null;
            return DateTimeOffset.TryParseExact(value, UtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed : (DateTimeOffset?)null;
        }

        public static DateTimeOffset? UtcTime(JsonElement value) => UtcTime(Text(value));

        /// <summary>
        /// Forced Full and its ordinally sorted reasons. Any reason forces Full: an explicit request; an unknown or
        /// changed input contract field (or a field outside the reviewed contract on either side); unproven baseline,
        /// admission chain, history, global ownership or shadow; a Full anchor that is unproven, of unknown or future
        /// time, or seven days old. The anchor is returned as given, never renewed.
        /// </summary>
        public static RcForcedFullDecision Decision(JsonElement currentContract, JsonElement baselineContract, JsonElement evidence, JsonElement fullAnchor,
            DateTimeOffset utcNow, bool forceFull)
        {
            var reasons = new List<string>();
            if (forceFull) reasons.Add("explicit-force-full");
            foreach (var field in ContractFields)
            {
                var current = Field(currentContract, field);
                var baseline = Field(baselineContract, field);
                var currentKnown = IsContractValue(field, current);
                var baselineKnown = IsContractValue(field, baseline);
                if (!currentKnown) reasons.Add("contract-current-unknown:" + field);
                if (!baselineKnown) reasons.Add("contract-baseline-unknown:" + field);
                if (currentKnown && baselineKnown && Text(current) != Text(baseline)) reasons.Add("contract-changed:" + field);
            }
            // New execution semantics need a reviewed contract version, not silently ignored fields.
            foreach (var (side, contract) in new[] { ("current", currentContract), ("baseline", baselineContract) })
                if (contract.ValueKind == JsonValueKind.Object)
                    foreach (var p in contract.EnumerateObject())
                        if (Array.IndexOf(ContractFields, p.Name) < 0) reasons.Add("contract-" + side + "-unknown:" + p.Name);
            foreach (var (field, expected, reason) in new[]
            {
                ("baselineState", "admitted-ancestor", "baseline-unproven"), ("admissionChainState", "verified", "admission-chain-unproven"),
                ("historyState", "complete", "history-unproven"), ("globalOwnershipState", "resolved", "global-ownership-unresolved"),
            })
                if (!IsText(Field(evidence, field), expected)) reasons.Add(reason);
            var shadow = Field(evidence, "shadowState");
            if (IsText(shadow, "discrepancy")) reasons.Add("shadow-discrepancy");
            else if (!IsText(shadow, "clean")) reasons.Add("shadow-unproven");
            var anchorKnown = IsText(Field(fullAnchor, "kind"), "executed-full") && IsText(Field(fullAnchor, "admissionState"), "verified")
                && IsSourceSha(Text(Field(fullAnchor, "sourceSha"))) && IsDigest(Field(fullAnchor, "closureDigest"));
            if (!anchorKnown) reasons.Add("full-anchor-unproven");
            else
            {
                var completed = UtcTime(Field(fullAnchor, "completedAtUtc"));
                if (completed == null) reasons.Add("full-anchor-time-unknown");
                else if (completed.Value > utcNow) reasons.Add("full-anchor-future");
                else if (utcNow.UtcTicks - completed.Value.UtcTicks >= TimeSpan.FromDays(7).Ticks) reasons.Add("full-anchor-expired");
            }
            reasons.Sort(StringComparer.Ordinal);
            return new RcForcedFullDecision { ForceFull = reasons.Count > 0, LaunchAll = reasons.Count > 0, Reasons = reasons.ToArray(), FullAnchor = fullAnchor };
        }

        /// <summary>
        /// A verified, complete Full closure with sane counts (no carries, failures or unexpected skips; executed plus
        /// expected skips cover the required set), a known origin and a UTC completion not in the future becomes an
        /// executed-full anchor. Pre-run forced-Full reasons are carried as history; they never veto a fresh Full nor are
        /// cleared to make one.
        /// </summary>
        public static RcExecutedFullAnchor ExecutedFullAnchor(JsonElement closure, DateTimeOffset utcNow)
        {
            var reasons = new List<string>();
            if (!IsText(Field(closure, "validationState"), "verified")) reasons.Add("closure-unproven");
            if (!IsText(Field(closure, "mode"), "Full")) reasons.Add("closure-not-full");
            if (Field(closure, "coverageComplete").ValueKind != JsonValueKind.True) reasons.Add("closure-incomplete");
            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            var countsKnown = true;
            foreach (var field in CountFields)
            {
                var v = Field(closure, field);
                if (v.ValueKind != JsonValueKind.Number || v.GetRawText().IndexOfAny(new[] { '.', 'e', 'E' }) >= 0 || !v.TryGetInt64(out var value)
                    || value < 0 || value > int.MaxValue || (field == "requiredCount" && value == 0)) { countsKnown = false; continue; }
                counts[field] = value;
            }
            if (!countsKnown) reasons.Add("closure-counts-unknown");
            else
            {
                if (counts["carriedCount"] != 0) reasons.Add("closure-has-carries");
                if (counts["executedCount"] + counts["expectedSkipCount"] != counts["requiredCount"]) reasons.Add("closure-membership-incomplete");
                if (counts["failedCount"] != 0) reasons.Add("closure-failed");
                if (counts["unexpectedSkipCount"] != 0) reasons.Add("closure-unexpected-skip");
            }
            if (!IsSourceSha(Text(Field(closure, "sourceSha"))) || !IsDigest(Field(closure, "closureDigest"))) reasons.Add("closure-origin-unknown");
            var completed = UtcTime(Field(closure, "completedAtUtc"));
            if (completed == null) reasons.Add("closure-time-unknown");
            else if (completed.Value > utcNow) reasons.Add("closure-time-future");
            reasons.Sort(StringComparer.Ordinal);
            var prior = Field(closure, "forcedFull");
            return new RcExecutedFullAnchor
            {
                Eligible = reasons.Count == 0, Reasons = reasons.ToArray(),
                FullAnchor = reasons.Count == 0
                    ? new Dictionary<string, string>
                    {
                        ["kind"] = "executed-full", ["admissionState"] = "verified", ["completedAtUtc"] = Text(Field(closure, "completedAtUtc")),
                        ["sourceSha"] = Text(Field(closure, "sourceSha")), ["closureDigest"] = Text(Field(closure, "closureDigest")),
                    }
                    : null,
                ForcedFull = prior.ValueKind == JsonValueKind.Undefined || prior.ValueKind == JsonValueKind.Null ? Array.Empty<JsonElement>()
                    : prior.ValueKind == JsonValueKind.Array ? prior.EnumerateArray().Select(e => e.Clone()).ToArray() : new[] { prior.Clone() },
            };
        }

        /// <summary>A plain object as a JSON element (dictionaries become objects), for callers that hold one.</summary>
        public static JsonElement ToElement(object value) => JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));
    }
}
