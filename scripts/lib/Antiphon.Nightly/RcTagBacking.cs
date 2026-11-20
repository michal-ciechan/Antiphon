// CARD-1820 S5. Test-RcTagBacking over an admission envelope chain.
// CARD-1836 admission is still PowerShell (scripts/lib/rc-admission.ps1), so this
// policy lives in the Nightly core. No tag, publish, or promote caller. gateCredit
// stays false: ledger-basis success is not publication credit.
// ChainFormEnabled is false until a CARD-1836 D-8 producer exists. A chain with
// two distinct trusted-cut records then answers chain-form-disabled-until-trusted-cuts.
// Fewer than two answers shadow-trust-absent. The document integer trustedShadowCuts
// is not a trust decision. Only an all-executed Full of the exact tagged SHA is
// backed, and only when its completed time is present and not later than the
// document utcNow by more than ExactFullClockSkew. That utcNow is the document
// field, not the host clock. A chain anchor is expired at age >= 7 days, and a
// chain anchor after the document utcNow is anchor-in-future with no skew.
// Public parse failures, including reader exceptions, answer chain-invalid.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    public enum RcTagBackingKind
    {
        Full,
        ChainToFull,
        NotBacked
    }

    public sealed class RcTagBackingResult
    {
        internal RcTagBackingResult(RcTagBackingKind kind, string anchorSha, TimeSpan? age, string reason, bool gateCredit)
        {
            Kind = kind;
            AnchorSha = anchorSha;
            Age = age;
            Reason = reason;
            GateCredit = gateCredit;
            Answer = kind == RcTagBackingKind.Full ? "full"
                : kind == RcTagBackingKind.ChainToFull ? "chain-to-full(" + anchorSha + "," + FormatAge(age.Value) + ")"
                : "not-backed(" + reason + ")";
        }

        public RcTagBackingKind Kind { get; }
        public string Answer { get; }
        public string AnchorSha { get; }
        public TimeSpan? Age { get; }
        public string Reason { get; }
        public bool GateCredit { get; }

        internal static RcTagBackingResult NotBacked(string reason) =>
            new RcTagBackingResult(RcTagBackingKind.NotBacked, null, null, reason, false);

        internal static RcTagBackingResult Full() =>
            new RcTagBackingResult(RcTagBackingKind.Full, null, null, null, false);

        internal static RcTagBackingResult Chain(string anchorSha, TimeSpan age) =>
            new RcTagBackingResult(RcTagBackingKind.ChainToFull, anchorSha, age, null, false);

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
        public const bool ChainFormEnabled = false;
        public const int MaxChainLinks = 64;
        public const int ExactFullClockSkewMinutes = 5;
        public static readonly TimeSpan ExactFullClockSkew = TimeSpan.FromMinutes(ExactFullClockSkewMinutes);
        public static readonly TimeSpan MaxAnchorAge = TimeSpan.FromDays(7);
        private const int MaxCommits = 4096;
        private const int MaxParents = 16;
        private const int MaxJsonChars = 1048576;
        private static readonly string[] UtcPatterns =
        {
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.f'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.ff'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.ffff'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.fffff'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"
        };

        public static RcTagBackingResult Test(string chainJson)
        {
            try
            {
                return Decide(Parse(chainJson));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
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
                if (string.IsNullOrEmpty(head.CompletedAtUtc))
                    return RcTagBackingResult.NotBacked("full-time-unknown");
                var completed = ReadUtc(head.CompletedAtUtc);
                if (completed > chain.UtcNow + ExactFullClockSkew)
                    return RcTagBackingResult.NotBacked("full-time-in-future");
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
                return RcTagBackingResult.NotBacked("anchor-time-unknown");
            var completed = ReadUtc(anchor.CompletedAtUtc);
            if (completed > chain.UtcNow)
                return RcTagBackingResult.NotBacked("anchor-in-future");
            if (!IsAncestor(chain, anchor.SourceSha, chain.TaggedSha))
                return RcTagBackingResult.NotBacked("anchor-not-ancestor");
            var age = chain.UtcNow - completed;
            if (age >= MaxAnchorAge)
                return RcTagBackingResult.NotBacked("chain-older-than-seven-days");
            if (CountDistinctTrustedCuts(chain.TrustedCuts) < TrustedShadowCutsRequired)
                return RcTagBackingResult.NotBacked("shadow-trust-absent");
            return AnswerChain(anchor.SourceSha, age);
        }

        private static RcTagBackingResult AnswerChain(string anchorSha, TimeSpan age)
        {
#pragma warning disable CS0162 // ChainFormEnabled is false until a CARD-1836 D-8 producer exists.
            if (ChainFormEnabled)
                return RcTagBackingResult.Chain(anchorSha, age);
#pragma warning restore CS0162
            return RcTagBackingResult.NotBacked("chain-form-disabled-until-trusted-cuts");
        }

        private static int CountDistinctTrustedCuts(List<TrustedCut> cuts)
        {
            var seen = new Dictionary<string, TrustedCut>(StringComparer.Ordinal);
            foreach (var cut in cuts)
            {
                if (seen.TryGetValue(cut.RunId, out var prior))
                {
                    if (!prior.Same(cut))
                        throw new ChainInvalid();
                    continue;
                }
                seen.Add(cut.RunId, cut);
            }
            var count = 0;
            foreach (var cut in seen.Values)
            {
                if (cut.Admitted && cut.ExecutedSamples > 0)
                    count++;
            }
            return count;
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

        private static void RejectDuplicateProperties(string json)
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                MaxDepth = 16
            });
            var names = new Stack<HashSet<string>>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                    names.Push(new HashSet<string>(StringComparer.Ordinal));
                else if (reader.TokenType == JsonTokenType.EndObject)
                    names.Pop();
                else if (reader.TokenType == JsonTokenType.PropertyName && !names.Peek().Add(reader.GetString()))
                    throw new ChainInvalid();
            }
        }

        private static Chain Parse(string chainJson)
        {
            if (string.IsNullOrEmpty(chainJson) || chainJson.Length > MaxJsonChars)
                throw new ChainInvalid();
            // net9 JsonDocumentOptions has no AllowDuplicateProperties; that member is net10.
            RejectDuplicateProperties(chainJson);
            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                MaxDepth = 16
            };
            using var document = JsonDocument.Parse(chainJson, options);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ChainInvalid();
            RequireKeys(root, new[] { "format", "taggedSha", "utcNow", "trustedShadowCuts", "envelopes", "commits" }, new[] { "trustedCuts" });
            if (!string.Equals(RequiredString(root, "format"), "antiphon-rc-tag-chain-v1", StringComparison.Ordinal))
                throw new ChainInvalid();
            var tagged = RequiredString(root, "taggedSha");
            if (!IsSha(tagged))
                throw new ChainInvalid();
            var utcNow = ReadUtc(RequiredString(root, "utcNow"));
            RequiredInt(root, "trustedShadowCuts");
            var trustedCuts = ParseTrustedCuts(root);
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
            return new Chain(tagged, utcNow, trustedCuts, links, parents);
        }

        private static List<TrustedCut> ParseTrustedCuts(JsonElement root)
        {
            var cuts = new List<TrustedCut>();
            if (!root.TryGetProperty("trustedCuts", out var array))
                return cuts;
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > MaxChainLinks)
                throw new ChainInvalid();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new ChainInvalid();
                RequireKeys(item, "runId", "cutSha", "admitted", "executedSamples");
                var runId = RequiredString(item, "runId");
                if (runId.Length == 0 || runId.Length > 128)
                    throw new ChainInvalid();
                foreach (var ch in runId)
                {
                    if (ch <= ' ' || ch > 126)
                        throw new ChainInvalid();
                }
                var sha = RequiredString(item, "cutSha");
                if (!IsSha(sha))
                    throw new ChainInvalid();
                cuts.Add(new TrustedCut(runId, sha, RequiredBool(item, "admitted"), RequiredInt(item, "executedSamples")));
            }
            return cuts;
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
                ReadCompletedAt(OptionalString(item, "anchorCompletedAtUtc")),
                RequiredBool(item, "seed"),
                RequiredBool(item, "admitted"),
                RequiredBool(item, "openFailure"),
                RequiredBool(item, "shadowDiscrepancy"),
                RequiredBool(item, "gateCredit"),
                RequiredBool(item, "allExecuted"),
                RequiredInt(item, "carriedCount"));
        }

        private static void RequireKeys(JsonElement obj, params string[] keys) =>
            RequireKeys(obj, keys, null);

        private static void RequireKeys(JsonElement obj, string[] required, string[] optional)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in obj.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                    throw new ChainInvalid();
                if (!KnownKey(property.Name, required) && !KnownKey(property.Name, optional))
                    throw new ChainInvalid();
            }
            if (required != null)
            {
                foreach (var key in required)
                {
                    if (!seen.Contains(key))
                        throw new ChainInvalid();
                }
            }
        }

        private static bool KnownKey(string name, string[] keys)
        {
            if (keys == null)
                return false;
            foreach (var key in keys)
            {
                if (string.Equals(key, name, StringComparison.Ordinal))
                    return true;
            }
            return false;
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

        private static string ReadCompletedAt(string text)
        {
            if (text == null)
                return null;
            ReadUtc(text);
            return text;
        }

        private static DateTimeOffset ReadUtc(string text)
        {
            if (string.IsNullOrEmpty(text) || text[text.Length - 1] != 'Z')
                throw new ChainInvalid();
            if (!DateTimeOffset.TryParseExact(
                    text,
                    UtcPatterns,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed)
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
            public Chain(string taggedSha, DateTimeOffset utcNow, List<TrustedCut> trustedCuts, List<Link> links,
                Dictionary<string, string[]> parents)
            {
                TaggedSha = taggedSha;
                UtcNow = utcNow;
                TrustedCuts = trustedCuts;
                Links = links;
                Parents = parents;
            }

            public string TaggedSha { get; }
            public DateTimeOffset UtcNow { get; }
            public List<TrustedCut> TrustedCuts { get; }
            public List<Link> Links { get; }
            public Dictionary<string, string[]> Parents { get; }
        }

        private sealed class TrustedCut
        {
            public TrustedCut(string runId, string cutSha, bool admitted, int executedSamples)
            {
                RunId = runId;
                CutSha = cutSha;
                Admitted = admitted;
                ExecutedSamples = executedSamples;
            }

            public string RunId { get; }
            public string CutSha { get; }
            public bool Admitted { get; }
            public int ExecutedSamples { get; }

            public bool Same(TrustedCut other) =>
                other != null
                && string.Equals(RunId, other.RunId, StringComparison.Ordinal)
                && string.Equals(CutSha, other.CutSha, StringComparison.Ordinal)
                && Admitted == other.Admitted
                && ExecutedSamples == other.ExecutedSamples;
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
