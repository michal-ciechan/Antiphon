// CARD-1836 S8a forced-Full policy (moved from rc-forced-full.ps1). Pure policy: no selection, launch, persistence or
// S1 API changes. Callers supply findings from the admission/closure validators, not operator assertions; this policy
// neither authenticates receipts nor authorizes carries. Hashes bind complete executed closures and actual toolchain
// receipts. Unresolved imports or ownership are unresolved evidence; no equivalence override exists.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
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

        private static PSObject Note(params object[] pairs)
        {
            var o = new PSObject();
            for (var i = 0; i < pairs.Length; i += 2) o.Properties.Add(new PSNoteProperty((string)pairs[i], pairs[i + 1]));
            return o;
        }

        /// <summary>Get-RcFullField: a dictionary key or object property named exactly (case-sensitive); null when absent.</summary>
        public static object Field(object o, string name)
        {
            if (PsOps.Base(o) is IDictionary d)
            {
                foreach (var key in d.Keys) if (key is string s && string.Equals(s, name, StringComparison.Ordinal)) return d[key];
                return null;
            }
            if (o is PSObject ps)
                foreach (var property in ps.Properties)
                    if (string.Equals(property.Name, name, StringComparison.Ordinal)) return property.Value;
            return null;
        }

        /// <summary>Get-RcFullFieldNames: a dictionary's keys or an object's property names; none for anything else.</summary>
        public static object[] FieldNames(object o)
        {
            var names = new List<object>();
            if (PsOps.Base(o) is IDictionary d) foreach (var key in d.Keys) names.Add(key);
            else if (o is PSObject ps) foreach (var property in ps.Properties) names.Add(property.Name);
            return names.ToArray();
        }

        private static string Text(object value) => PsOps.Base(value) as string;

        /// <summary>Test-RcFullText: a string equal (ordinal) to expected.</summary>
        public static bool IsText(object value, string expected) => Text(value) is string s && string.Equals(s, expected, StringComparison.Ordinal);

        /// <summary>Test-RcFullDigest: a string of 64 lower-case hex digits.</summary>
        public static bool IsDigest(object value) => Text(value) is string s && Digest64.IsMatch(s);

        /// <summary>Test-RcFullSourceSha: a string of 40 or 64 lower-case hex digits.</summary>
        public static bool IsSourceSha(object value) => Text(value) is string s && SourceShaShape.IsMatch(s);

        /// <summary>Test-RcFullContractValue: the input contract field's value is a string of its known shape.</summary>
        public static bool IsContractValue(string field, object value)
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
        /// ConvertTo-RcFullUtcTime: an explicit UTC timestamp yyyy-MM-ddTHH:mm:ss[.fffffff]Z at tick precision; null for
        /// anything else (local or unspecified time, a DateTime, culture-dependent text, missing time).
        /// </summary>
        public static DateTimeOffset? UtcTime(object value)
        {
            if (!(Text(value) is string s) || !UtcShape.IsMatch(s)) return null;
            return DateTimeOffset.TryParseExact(s, UtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed : (DateTimeOffset?)null;
        }

        /// <summary>
        /// Get-RcForcedFullDecision: { forceFull, launchAll, reasons, fullAnchor }. Any reason forces Full: an explicit
        /// request; an unknown or changed input contract field (or a field outside the reviewed contract on either
        /// side); unproven baseline, admission chain, history, global ownership or shadow; a Full anchor that is
        /// unproven, of unknown or future time, or seven days old. Reasons are ordinally sorted; the anchor is returned
        /// as given, never renewed.
        /// </summary>
        public static PSObject Decision(object currentContract, object baselineContract, object evidence, object fullAnchor, DateTimeOffset utcNow, bool forceFull)
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
                if (currentKnown && baselineKnown && !string.Equals(Text(current), Text(baseline), StringComparison.Ordinal)) reasons.Add("contract-changed:" + field);
            }
            // New execution semantics need a reviewed contract version, not silently ignored fields.
            foreach (var (side, contract) in new[] { ("current", currentContract), ("baseline", baselineContract) })
                foreach (var name in FieldNames(contract))
                    if (Array.IndexOf(ContractFields, PsValue.Str(name)) < 0) reasons.Add("contract-" + side + "-unknown:" + PsValue.Str(name));
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
                && IsSourceSha(Field(fullAnchor, "sourceSha")) && IsDigest(Field(fullAnchor, "closureDigest"));
            if (!anchorKnown) reasons.Add("full-anchor-unproven");
            else
            {
                var completed = UtcTime(Field(fullAnchor, "completedAtUtc"));
                if (completed == null) reasons.Add("full-anchor-time-unknown");
                else if (completed.Value > utcNow) reasons.Add("full-anchor-future");
                else if (utcNow.UtcTicks - completed.Value.UtcTicks >= TimeSpan.FromDays(7).Ticks) reasons.Add("full-anchor-expired");
            }
            reasons.Sort(StringComparer.Ordinal);
            return Note("forceFull", reasons.Count > 0, "launchAll", reasons.Count > 0, "reasons", reasons.ToArray(), "fullAnchor", fullAnchor);
        }

        /// <summary>
        /// Get-RcExecutedFullAnchor: { eligible, reasons, fullAnchor, forcedFull }. A verified, complete Full closure with
        /// sane counts (no carries, failures or unexpected skips; executed plus expected skips cover the required set), a
        /// known origin and a UTC completion not in the future becomes an executed-full anchor. Pre-run forced-Full
        /// reasons are carried as history; they never veto a fresh Full nor are cleared to make one.
        /// </summary>
        public static PSObject ExecutedFullAnchor(object closure, DateTimeOffset utcNow)
        {
            var reasons = new List<string>();
            if (!IsText(Field(closure, "validationState"), "verified")) reasons.Add("closure-unproven");
            if (!IsText(Field(closure, "mode"), "Full")) reasons.Add("closure-not-full");
            if (!(PsOps.Base(Field(closure, "coverageComplete")) is bool complete) || !complete) reasons.Add("closure-incomplete");
            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            var countsKnown = true;
            foreach (var field in CountFields)
            {
                long value;
                switch (PsOps.Base(Field(closure, field)))
                {
                    case int i: value = i; break;
                    case long l: value = l; break;
                    default: countsKnown = false; continue;
                }
                if (value < 0 || value > int.MaxValue || (field == "requiredCount" && value == 0)) countsKnown = false;
                else counts[field] = value;
            }
            if (!countsKnown) reasons.Add("closure-counts-unknown");
            else
            {
                if (counts["carriedCount"] != 0) reasons.Add("closure-has-carries");
                if (counts["executedCount"] + counts["expectedSkipCount"] != counts["requiredCount"]) reasons.Add("closure-membership-incomplete");
                if (counts["failedCount"] != 0) reasons.Add("closure-failed");
                if (counts["unexpectedSkipCount"] != 0) reasons.Add("closure-unexpected-skip");
            }
            if (!IsSourceSha(Field(closure, "sourceSha")) || !IsDigest(Field(closure, "closureDigest"))) reasons.Add("closure-origin-unknown");
            var completed = UtcTime(Field(closure, "completedAtUtc"));
            if (completed == null) reasons.Add("closure-time-unknown");
            else if (completed.Value > utcNow) reasons.Add("closure-time-future");
            reasons.Sort(StringComparer.Ordinal);
            OrderedDictionary anchor = null;
            if (reasons.Count == 0)
            {
                anchor = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
                {
                    { "kind", "executed-full" }, { "admissionState", "verified" }, { "completedAtUtc", Field(closure, "completedAtUtc") },
                    { "sourceSha", Field(closure, "sourceSha") }, { "closureDigest", Field(closure, "closureDigest") },
                };
            }
            var prior = Field(closure, "forcedFull");
            return Note("eligible", reasons.Count == 0, "reasons", reasons.ToArray(), "fullAnchor", anchor,
                "forcedFull", prior == null ? new object[0] : PsValue.AsArray(prior));
        }
    }
}
