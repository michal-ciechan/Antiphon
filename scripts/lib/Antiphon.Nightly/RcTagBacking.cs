// CARD-1820 S5. Test-RcTagBacking over an admission envelope chain.
// CARD-1836 admission is still PowerShell (scripts/lib/rc-admission.ps1), so this
// policy lives in the Nightly core. No tag, publish, or promote caller. gateCredit
// stays false: ledger-basis success is not publication credit.
// Until TrustedShadowCutsRequired successful Windows shadow cuts exist, only an
// all-executed Full of the exact tagged SHA is backed. A chain anchor is expired
// at age >= 7 days, the same full-anchor-expired bound forced-Full already uses.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Antiphon.Nightly
{
    public sealed class RcTagBackingResult
    {
        public RcTagBackingResult(string kind, string anchorSha, TimeSpan? age, string reason, bool gateCredit)
        {
            Kind = kind;
            AnchorSha = anchorSha;
            Age = age;
            Reason = reason;
            GateCredit = gateCredit;
            Answer = kind == "full" ? "full"
                : kind == "chain-to-full" ? "chain-to-full(" + anchorSha + "," + FormatAge(age.Value) + ")"
                : "not-backed(" + reason + ")";
        }

        public string Kind { get; }
        public string Answer { get; }
        public string AnchorSha { get; }
        public TimeSpan? Age { get; }
        public string Reason { get; }
        public bool GateCredit { get; }

        public static RcTagBackingResult NotBacked(string reason) =>
            new RcTagBackingResult("not-backed", null, null, reason, false);

        public static RcTagBackingResult Full() =>
            new RcTagBackingResult("full", null, null, null, false);

        public static RcTagBackingResult Chain(string anchorSha, TimeSpan age) =>
            new RcTagBackingResult("chain-to-full", anchorSha, age, null, false);

        public static string FormatAge(TimeSpan age)
        {
            var negative = age < TimeSpan.Zero;
            var ticks = age.Duration().Ticks;
            var days = ticks / TimeSpan.TicksPerDay;
            ticks %= TimeSpan.TicksPerDay;
            var hours = ticks / TimeSpan.TicksPerHour;
            ticks %= TimeSpan.TicksPerHour;
            var minutes = ticks / TimeSpan.TicksPerMinute;
            ticks %= TimeSpan.TicksPerMinute;
            var seconds = ticks / TimeSpan.TicksPerSecond;
            var fraction = ticks % TimeSpan.TicksPerSecond;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}.{2:00}:{3:00}:{4:00}.{5:0000000}",
                negative ? "-" : "", days, hours, minutes, seconds, fraction);
        }
    }

    public static class RcTagBacking
    {
        public const int TrustedShadowCutsRequired = 2;
        public const int MaxChainLinks = 64;
        public static readonly TimeSpan MaxAnchorAge = TimeSpan.FromDays(7);
        private const int MaxCommits = 4096;
        private const int MaxParents = 16;
        private const int MaxJsonChars = 1048576;

        public static RcTagBackingResult Test(string chainJson)
        {
            try
            {
                return Decide(Parse(chainJson));
            }
            catch (ChainInvalid)
            {
                return RcTagBackingResult.NotBacked("chain-invalid");
            }
            catch (JsonException)
            {
                return RcTagBackingResult.NotBacked("chain-invalid");
            }
        }

        private static RcTagBackingResult Decide(Chain chain)
        {
            Link head = null;
            foreach (var link in chain.Links)
            {
                if (!string.Equals(link.SourceSha, chain.TaggedSha, StringComparison.Ordinal))
                    continue;
                if (head != null)
                    throw new ChainInvalid();
                head = link;
            }
            if (head == null)
                return RcTagBackingResult.NotBacked("missing-link");

            if (head.Disposition == "executed-full")
            {
                var fault = LinkFault(head);
                if (fault != null)
                    return RcTagBackingResult.NotBacked(fault);
                if (!head.AllExecuted)
                    return RcTagBackingResult.NotBacked("anchor-not-all-executed");
                if (head.CarriedCount != 0)
                    return RcTagBackingResult.NotBacked("anchor-has-carries");
                return RcTagBackingResult.Full();
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = head;
            for (var depth = 0; depth < MaxChainLinks; depth++)
            {
                if (!seen.Add(current.RunId))
                    return RcTagBackingResult.NotBacked("chain-cycle-or-depth");
                var fault = LinkFault(current);
                if (fault != null)
                    return RcTagBackingResult.NotBacked(fault);
                if (current.Disposition == "executed-full")
                    return FinishAnchor(chain, current);
                if (current.Disposition != "carried" && current.Disposition != "shadow")
                    throw new ChainInvalid();
                if (string.IsNullOrEmpty(current.OriginRunId))
                    return RcTagBackingResult.NotBacked("carry-without-anchor");
                Link next = null;
                foreach (var candidate in chain.Links)
                {
                    if (!string.Equals(candidate.RunId, current.OriginRunId, StringComparison.Ordinal))
                        continue;
                    if (next != null)
                        throw new ChainInvalid();
                    next = candidate;
                }
                if (next == null)
                    return RcTagBackingResult.NotBacked("missing-link");
                current = next;
            }
            return RcTagBackingResult.NotBacked("chain-cycle-or-depth");
        }

        // First fault wins, in this order, so one defect has one reason.
        private static string LinkFault(Link link)
        {
            if (link.GateCredit)
                return "gate-credit-claimed";
            if (!string.Equals(link.Platform, "windows", StringComparison.Ordinal))
                return "not-windows";
            if (!link.Admitted)
                return "link-not-admitted";
            if (link.OpenFailure)
                return "open-failure";
            if (link.ShadowDiscrepancy)
                return "shadow-discrepancy";
            if (link.Seed || link.Disposition == "seed")
                return "seed-is-not-credit";
            return null;
        }

        private static RcTagBackingResult FinishAnchor(Chain chain, Link anchor)
        {
            if (!anchor.AllExecuted)
                return RcTagBackingResult.NotBacked("anchor-not-all-executed");
            if (anchor.CarriedCount != 0)
                return RcTagBackingResult.NotBacked("anchor-has-carries");
            if (string.IsNullOrEmpty(anchor.CompletedAtUtc))
                return RcTagBackingResult.NotBacked("full-anchor-time-unknown");
            var completed = ReadUtc(anchor.CompletedAtUtc);
            if (completed > chain.UtcNow)
                return RcTagBackingResult.NotBacked("full-anchor-future");
            if (!IsAncestor(chain, anchor.SourceSha, chain.TaggedSha))
                return RcTagBackingResult.NotBacked("anchor-not-ancestor");
            var age = chain.UtcNow - completed;
            if (age >= MaxAnchorAge)
                return RcTagBackingResult.NotBacked("chain-older-than-seven-days");
            if (chain.TrustedShadowCuts < TrustedShadowCutsRequired)
                return RcTagBackingResult.NotBacked("shadow-trust-absent");
            return RcTagBackingResult.Chain(anchor.SourceSha, age);
        }

        private static bool IsAncestor(Chain chain, string anchorSha, string taggedSha)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            stack.Push(taggedSha);
            var steps = 0;
            while (stack.Count > 0)
            {
                if (++steps > MaxCommits)
                    return false;
                var sha = stack.Pop();
                if (!seen.Add(sha))
                    continue;
                if (string.Equals(sha, anchorSha, StringComparison.Ordinal))
                    return true;
                if (!chain.Parents.TryGetValue(sha, out var parents))
                    continue;
                foreach (var parent in parents)
                    stack.Push(parent);
            }
            return false;
        }

        private static Chain Parse(string chainJson)
        {
            if (string.IsNullOrEmpty(chainJson) || chainJson.Length > MaxJsonChars)
                throw new ChainInvalid();
            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                AllowDuplicateProperties = false,
                MaxDepth = 16
            };
            using var document = JsonDocument.Parse(chainJson, options);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ChainInvalid();
            RequireKeys(root, "format", "taggedSha", "utcNow", "trustedShadowCuts", "envelopes", "commits");
            if (!string.Equals(RequiredString(root, "format"), "antiphon-rc-tag-chain-v1", StringComparison.Ordinal))
                throw new ChainInvalid();
            var tagged = RequiredString(root, "taggedSha");
            if (!IsSha(tagged))
                throw new ChainInvalid();
            var utcNow = ReadUtc(RequiredString(root, "utcNow"));
            var trusted = RequiredInt(root, "trustedShadowCuts");
            var envelopes = root.GetProperty("envelopes");
            if (envelopes.ValueKind != JsonValueKind.Array || envelopes.GetArrayLength() == 0
                || envelopes.GetArrayLength() > MaxChainLinks)
                throw new ChainInvalid();
            var links = new List<Link>(envelopes.GetArrayLength());
            var runIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in envelopes.EnumerateArray())
            {
                var link = ParseLink(item);
                if (!runIds.Add(link.RunId))
                    throw new ChainInvalid();
                links.Add(link);
            }
            var commits = root.GetProperty("commits");
            if (commits.ValueKind != JsonValueKind.Array || commits.GetArrayLength() > MaxCommits)
                throw new ChainInvalid();
            var parents = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var item in commits.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new ChainInvalid();
                RequireKeys(item, "sha", "parents");
                var sha = RequiredString(item, "sha");
                if (!IsSha(sha) || parents.ContainsKey(sha))
                    throw new ChainInvalid();
                var parentValues = item.GetProperty("parents");
                if (parentValues.ValueKind != JsonValueKind.Array || parentValues.GetArrayLength() > MaxParents)
                    throw new ChainInvalid();
                var list = new string[parentValues.GetArrayLength()];
                var index = 0;
                foreach (var parent in parentValues.EnumerateArray())
                {
                    if (parent.ValueKind != JsonValueKind.String)
                        throw new ChainInvalid();
                    var value = parent.GetString();
                    if (!IsSha(value))
                        throw new ChainInvalid();
                    list[index++] = value;
                }
                parents.Add(sha, list);
            }
            return new Chain(tagged, utcNow, trusted, links, parents);
        }

        private static Link ParseLink(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new ChainInvalid();
            RequireKeys(item, "format", "platform", "runId", "sourceSha", "disposition", "originRunId",
                "anchorCompletedAtUtc", "seed", "admitted", "openFailure", "shadowDiscrepancy",
                "gateCredit", "allExecuted", "carriedCount");
            if (!string.Equals(RequiredString(item, "format"), "antiphon-rc-tag-envelope-v1", StringComparison.Ordinal))
                throw new ChainInvalid();
            var disposition = RequiredString(item, "disposition");
            if (disposition != "executed-full" && disposition != "carried" && disposition != "shadow" && disposition != "seed")
                throw new ChainInvalid();
            var runId = RequiredString(item, "runId");
            if (runId.Length == 0 || runId.Length > 128)
                throw new ChainInvalid();
            foreach (var ch in runId)
            {
                if (ch <= ' ' || ch > 126)
                    throw new ChainInvalid();
            }
            var source = RequiredString(item, "sourceSha");
            if (!IsSha(source))
                throw new ChainInvalid();
            var platform = RequiredString(item, "platform");
            if (platform.Length == 0 || platform.Length > 32)
                throw new ChainInvalid();
            return new Link(
                runId,
                source,
                platform,
                disposition,
                OptionalString(item, "originRunId"),
                OptionalString(item, "anchorCompletedAtUtc"),
                RequiredBool(item, "seed"),
                RequiredBool(item, "admitted"),
                RequiredBool(item, "openFailure"),
                RequiredBool(item, "shadowDiscrepancy"),
                RequiredBool(item, "gateCredit"),
                RequiredBool(item, "allExecuted"),
                RequiredInt(item, "carriedCount"));
        }

        private static void RequireKeys(JsonElement obj, params string[] keys)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in obj.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                    throw new ChainInvalid();
                var known = false;
                foreach (var key in keys)
                {
                    if (string.Equals(key, property.Name, StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }
                if (!known)
                    throw new ChainInvalid();
            }
            if (seen.Count != keys.Length)
                throw new ChainInvalid();
        }

        private static string RequiredString(JsonElement obj, string name)
        {
            var value = obj.GetProperty(name);
            if (value.ValueKind != JsonValueKind.String)
                throw new ChainInvalid();
            var text = value.GetString();
            if (text == null)
                throw new ChainInvalid();
            return text;
        }

        private static string OptionalString(JsonElement obj, string name)
        {
            var value = obj.GetProperty(name);
            if (value.ValueKind == JsonValueKind.Null)
                return null;
            if (value.ValueKind != JsonValueKind.String)
                throw new ChainInvalid();
            return value.GetString();
        }

        private static bool RequiredBool(JsonElement obj, string name)
        {
            var value = obj.GetProperty(name);
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                throw new ChainInvalid();
            return value.GetBoolean();
        }

        private static int RequiredInt(JsonElement obj, string name)
        {
            var value = obj.GetProperty(name);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0)
                throw new ChainInvalid();
            return number;
        }

        private static DateTimeOffset ReadUtc(string text)
        {
            if (string.IsNullOrEmpty(text) || text[text.Length - 1] != 'Z')
                throw new ChainInvalid();
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                || parsed.Offset != TimeSpan.Zero)
                throw new ChainInvalid();
            return parsed;
        }

        private static bool IsSha(string text)
        {
            if (text == null || text.Length != 40)
                return false;
            foreach (var ch in text)
            {
                var hex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f');
                if (!hex)
                    return false;
            }
            return true;
        }

        private sealed class ChainInvalid : Exception { }

        private sealed class Chain
        {
            public Chain(string taggedSha, DateTimeOffset utcNow, int trustedShadowCuts, List<Link> links,
                Dictionary<string, string[]> parents)
            {
                TaggedSha = taggedSha;
                UtcNow = utcNow;
                TrustedShadowCuts = trustedShadowCuts;
                Links = links;
                Parents = parents;
            }

            public string TaggedSha { get; }
            public DateTimeOffset UtcNow { get; }
            public int TrustedShadowCuts { get; }
            public List<Link> Links { get; }
            public Dictionary<string, string[]> Parents { get; }
        }

        private sealed class Link
        {
            public Link(string runId, string sourceSha, string platform, string disposition, string originRunId,
                string completedAtUtc, bool seed, bool admitted, bool openFailure, bool shadowDiscrepancy,
                bool gateCredit, bool allExecuted, int carriedCount)
            {
                RunId = runId;
                SourceSha = sourceSha;
                Platform = platform;
                Disposition = disposition;
                OriginRunId = originRunId;
                CompletedAtUtc = completedAtUtc;
                Seed = seed;
                Admitted = admitted;
                OpenFailure = openFailure;
                ShadowDiscrepancy = shadowDiscrepancy;
                GateCredit = gateCredit;
                AllExecuted = allExecuted;
                CarriedCount = carriedCount;
            }

            public string RunId { get; }
            public string SourceSha { get; }
            public string Platform { get; }
            public string Disposition { get; }
            public string OriginRunId { get; }
            public string CompletedAtUtc { get; }
            public bool Seed { get; }
            public bool Admitted { get; }
            public bool OpenFailure { get; }
            public bool ShadowDiscrepancy { get; }
            public bool GateCredit { get; }
            public bool AllExecuted { get; }
            public int CarriedCount { get; }
        }
    }
}
