// W2-nightly S11 (plan 2026-11-21-w2-nightly-port-slices.md): the executor leaves of nightly-tests-impl.ps1 in plain C#.
// The launch prices (the $NightlyLaunch* constants), the class-cost table (Get-NightlyClassCostTable), the launch
// groups and plans (Get-NightlyLaunchClassCost, ConvertTo-NightlyProfileLaunchGroups with and without -BoundaryAware,
// ConvertTo-NightlyBoundaryLaunchGroupsIndexed, Complete-NightlyBoundaryLaunchGroups, Get-NightlyProfileLaunchPlan,
// Resolve-NightlySuiteChunks), native classification (Test-NightlySuiteIsNative), the E2E prerequisites
// (Test-NightlyE2EPrerequisites), the test host (Resolve-NightlyTestHost), the chunk discovery seconds
// (Resolve-NightlyChunkDiscoverySeconds, Resolve-NightlyProductionChunkDiscovery), the native argv
// (Get-NightlyNativeExecutionArguments), the npm launcher (Resolve-NightlyNpmLauncher, Get-NightlyNpmLaunch), the
// execution platform (Get-NightlyExecutionPlatform) and the run outcome (Set-NightlyOutcome). A refusal is a
// NightlyRefusal whose message is the text the PowerShell threw; the still-PowerShell callers throw that text again
// (Get-NightlyRefusalText). Records follow the core (PsValue): PSObjects while PowerShell is loaded, PlainObjects
// otherwise, so the PowerShell callers see what they saw. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>
    /// The launch prices ($NightlyLaunch* in nightly-tests-impl.ps1, CARD-1410). A class with a row in
    /// nightly-class-costs.json uses it; any other class uses the node model: an ordinary node is 0.5 s and Slow 1.5 s
    /// (4 s in a Land or Delivery type), and a type naming Land, Delivery or Check doubles its nodes. A group targets 15
    /// minutes; a class estimated over 40 minutes (25 for Antiphon.E2E, CARD-1584) is split into exact-method chunks of at
    /// most the target. Only the target and the lone-class budget vary (the boundary-pack fixtures shrink them).
    /// </summary>
    public sealed class NightlyLaunchPrices
    {
        public const double DefaultNodeSeconds = 0.5;
        public const double DefaultSlowNodeSeconds = 1.5;
        public const double DefaultLaneSlowNodeSeconds = 4;
        public const double DefaultHeavyFamilyFactor = 2;
        public const double DefaultTargetSeconds = 900;
        public const double DefaultLoneClassBudgetSeconds = 2400;
        public const int DefaultMaxGroupOperands = 30;

        /// <summary>The production prices.</summary>
        public static NightlyLaunchPrices Default { get; } = new NightlyLaunchPrices();

        public NightlyLaunchPrices() : this(DefaultTargetSeconds, DefaultLoneClassBudgetSeconds) { }

        public NightlyLaunchPrices(double targetSeconds, double loneClassBudgetSeconds)
        {
            TargetSeconds = targetSeconds;
            LoneClassBudgetSeconds = loneClassBudgetSeconds;
        }

        public double NodeSeconds => DefaultNodeSeconds;
        public double SlowNodeSeconds => DefaultSlowNodeSeconds;
        public double LaneSlowNodeSeconds => DefaultLaneSlowNodeSeconds;
        public double HeavyFamilyFactor => DefaultHeavyFamilyFactor;
        public double TargetSeconds { get; }
        public double LoneClassBudgetSeconds { get; }
        public int MaxGroupOperands => DefaultMaxGroupOperands;
    }

    /// <summary>
    /// One cost file's class-cost table, read at its first use ($script:NightlyClassCostPath and
    /// $script:NightlyClassCostTable): bare class identity -> measured seconds per node. A table given up front (a
    /// fixture's, or one already read) is used as it is.
    /// </summary>
    public sealed class NightlyClassCosts
    {
        private IDictionary _table;

        public NightlyClassCosts(string path) : this(path, null) { }

        public NightlyClassCosts(string path, IDictionary table)
        {
            Path = path ?? "";
            _table = table;
        }

        /// <summary>A fixed table (no file).</summary>
        public static NightlyClassCosts Of(IDictionary table) => new NightlyClassCosts("", table ?? new Hashtable(StringComparer.OrdinalIgnoreCase));

        /// <summary>The cost file (nightly-class-costs.json); empty for a fixed table.</summary>
        public string Path { get; }

        /// <summary>Whether the table has been read (or was given).</summary>
        public bool Loaded => _table != null;

        /// <summary>The table, read from Path on first use (NightlyTestsPlan.ClassCostTable; its refusals propagate).</summary>
        public IDictionary Table => _table ??= NightlyTestsPlan.ClassCostTable(Path);
    }

    /// <summary>Test-NightlyE2EPrerequisites: Ok, and every probe's row ({ name, exitCode, detail }).</summary>
    public sealed class NightlyE2EPrerequisites
    {
        public NightlyE2EPrerequisites(bool ok, object[] rows)
        {
            Ok = ok;
            Rows = rows ?? Array.Empty<object>();
        }

        public bool Ok { get; }
        public object[] Rows { get; }
    }

    /// <summary>Resolve-NightlyTestHost: what to start (FilePath and its Prefix arguments) and the test assembly.</summary>
    public sealed class NightlyTestHost
    {
        public NightlyTestHost(string filePath, string[] prefix, string assemblyPath)
        {
            FilePath = filePath;
            Prefix = prefix ?? Array.Empty<string>();
            AssemblyPath = assemblyPath;
        }

        public string FilePath { get; }
        public string[] Prefix { get; }
        public string AssemblyPath { get; }
    }

    /// <summary>Resolve-NightlyChunkDiscoverySeconds: a chunk's own discovery seconds (null when it ran none); never the suite's copied.</summary>
    public sealed class NightlyChunkDiscovery
    {
        public NightlyChunkDiscovery(double? chunkDiscoverySeconds, bool copiedFromSuite, double suiteDiscoverySeconds)
        {
            ChunkDiscoverySeconds = chunkDiscoverySeconds;
            CopiedFromSuite = copiedFromSuite;
            SuiteDiscoverySeconds = suiteDiscoverySeconds;
        }

        public double? ChunkDiscoverySeconds { get; }
        public bool CopiedFromSuite { get; }
        public double SuiteDiscoverySeconds { get; }
    }

    /// <summary>Get-NightlyNpmLaunch: cmd.exe /c with the quoted absolute npm launcher, then the npm arguments.</summary>
    public sealed class NightlyNpmLaunch
    {
        public NightlyNpmLaunch(string filePath, string[] argumentList)
        {
            FilePath = filePath ?? "";
            ArgumentList = argumentList ?? Array.Empty<string>();
        }

        public string FilePath { get; }
        public string[] ArgumentList { get; }
    }

    /// <summary>The executor leaves of nightly-tests-impl.ps1.</summary>
    public static class NightlyTestsPlan
    {
        /// <summary>The class-cost file's format.</summary>
        public const string ClassCostFormat = "antiphon-nightly-class-costs-v1";

        // CARD-0599 D-6: the pre-existing native inventory, a fallback for a suite without project and assembly.
        private static readonly string[] NativeGroups = { "antiphon", "session-runner", "pty-host", "agents-pty", "messaging" };

        // The boundaries a class may be mapped to (ConvertTo-NightlyBoundaryLaunchGroupsIndexed).
        private static readonly string[] Boundaries =
        {
            "host", "core", "persistence", "git", "runners", "integrations", "application", "scripts", "checkpoints", "tui", "e2e",
            "client", "messaging", "session-runner", "pty-host", "agents-pty", "observability", "server-git", "markdown-pdf",
        };

        private static readonly string[] OutcomeOrder = { "PREFLIGHT", "BUILD", "TESTS", "TIMEOUT" };
        private static readonly string[] NpmNames = { "npm.cmd", "npm.exe", "npm" };
        private static readonly Regex ExactFilterShape = new Regex(@"^/\*/[A-Za-z_][A-Za-z0-9_.]*/[A-Za-z_][A-Za-z0-9_]*/[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ClassFilterShape = new Regex(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex PathSeparators = new Regex(@"[\\/]");

        // ---- the class-cost table --------------------------------------------------------------------------------

        /// <summary>
        /// Get-NightlyClassCostTable: bare class identity -> measured seconds per node, an @{}-style Hashtable
        /// (OrdinalIgnoreCase) the caller may add to. No file is an empty table. A document whose format is not
        /// antiphon-nightly-class-costs-v1 refuses class-cost-format; a row with fewer than one node or no positive
        /// seconds refuses class-cost-row NAME. The file is parsed once per process and content (ClassCostCache, shared
        /// with Get-TestPhaseClassBasisMap, whose basis map is filled from the same parse); each call gets its own copy.
        /// </summary>
        public static Hashtable ClassCostTable(string path)
        {
            if (string.IsNullOrEmpty(path) || !(File.Exists(path) || Directory.Exists(path))) return new Hashtable(StringComparer.OrdinalIgnoreCase);
            var full = System.IO.Path.GetFullPath(path);
            var entry = ClassCostCache.Find(full);
            if (entry != null && entry.Costs != null) return (Hashtable)entry.Costs.Clone();
            var digest = entry != null ? entry.Digest : ClassCostCache.Digest(full);
            var (costs, basis) = ParseClassCosts(File.ReadAllText(path));
            if (entry == null) entry = ClassCostCache.Store(full, digest, null);
            entry.Costs = (Hashtable)costs.Clone();
            if (entry.Basis == null) entry.Basis = (Hashtable)basis.Clone();
            return costs;
        }

        private static (Hashtable Costs, Hashtable Basis) ParseClassCosts(string text)
        {
            var costs = new Hashtable(StringComparer.OrdinalIgnoreCase);
            var basis = new Hashtable(StringComparer.OrdinalIgnoreCase);
            JsonDocument doc;
            try { doc = JsonDocument.Parse((text ?? "").TrimStart((char)0xFEFF), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 64 }); }
            catch (JsonException e) { throw new NightlyRefusal("class-cost-json: " + e.Message); }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format)
                    || format.ValueKind != JsonValueKind.String || !string.Equals(format.GetString(), ClassCostFormat, StringComparison.Ordinal))
                    throw new NightlyRefusal("class-cost-format");
                // @($doc.classes.PSObject.Properties): no classes is one null row, a list or a string has its adapted Length
                // first, a number or a boolean has none.
                if (!root.TryGetProperty("classes", out var classes) || classes.ValueKind == JsonValueKind.Null) throw new NightlyRefusal("class-cost-row ");
                if (classes.ValueKind == JsonValueKind.Array || classes.ValueKind == JsonValueKind.String) throw new NightlyRefusal("class-cost-row Length");
                if (classes.ValueKind != JsonValueKind.Object) return (costs, basis);
                foreach (var row in classes.EnumerateObject())
                {
                    var nodes = JsonInt(row.Value, "nodes");
                    var seconds = JsonDouble(row.Value, "seconds");
                    if (nodes < 1 || !(seconds > 0)) throw new NightlyRefusal("class-cost-row " + row.Name);
                    costs[row.Name] = seconds / nodes;
                }
                foreach (var row in classes.EnumerateObject()) basis[row.Name] = JsonText(row.Value, "basis");
            }
            return (costs, basis);
        }

        private static bool Field(JsonElement o, string name, out JsonElement value)
        {
            value = default;
            return o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out value);
        }

        // [int]$row.Value.<name>: a number rounds half to even, a string parses, true is 1, absent or null is 0.
        private static int JsonInt(JsonElement o, string name)
        {
            if (!Field(o, name, out var v)) return 0;
            switch (v.ValueKind)
            {
                case JsonValueKind.Number: return v.TryGetInt32(out var i) ? i : Convert.ToInt32(v.GetDouble());
                case JsonValueKind.String: return PlainValues.Int(v.GetString());
                case JsonValueKind.True: return 1;
                default: return 0;
            }
        }

        // [double]$row.Value.<name>.
        private static double JsonDouble(JsonElement o, string name)
        {
            if (!Field(o, name, out var v)) return 0;
            switch (v.ValueKind)
            {
                case JsonValueKind.Number: return v.GetDouble();
                case JsonValueKind.String: return PlainValues.Double(v.GetString());
                case JsonValueKind.True: return 1;
                default: return 0;
            }
        }

        // [string]$row.Value.<name>.
        private static string JsonText(JsonElement o, string name)
        {
            if (!Field(o, name, out var v)) return "";
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString();
                case JsonValueKind.Number: return v.TryGetInt64(out var l) ? l.ToString(CultureInfo.InvariantCulture) : PlainValues.Str(v.GetDouble());
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Null: return "";
                default: return v.GetRawText();
            }
        }

        // ---- launch groups and plans ------------------------------------------------------------------------------

        /// <summary>Get-NightlyLaunchClassCost: { NodeCount, Seconds, MeasuredPerNode, Heavy } at the production prices.</summary>
        public static object LaunchClassCost(object nodes, string typeName, string bare, IDictionary costTable)
        {
            var p = NightlyLaunchPrices.Default;
            return NightlyLaunchPlan.ClassCost(nodes, typeName, bare, costTable, p.NodeSeconds, p.SlowNodeSeconds, p.LaneSlowNodeSeconds, p.HeavyFamilyFactor);
        }

        /// <summary>
        /// ConvertTo-NightlyProfileLaunchGroups without -BoundaryAware (LaunchPlanner.ProfileLaunchGroups): the class cost
        /// table is read only when the leaf loop reaches a class cost, as the PowerShell body read it. discoveryIndex is
        /// $DiscoveryIndex (null: NightlyLaunchPlan.IndexNodes of the discovery). Null prices are the production ones.
        /// </summary>
        public static object[] ProfileLaunchGroups(object[] leaves, object[] discoveryNodes, object discoveryIndex, NightlyClassCosts costs, NightlyLaunchPrices prices)
        {
            var p = prices ?? NightlyLaunchPrices.Default;
            IDictionary table = null;
            if (LaunchPlanner.NeedsCostTable(leaves)) table = costs?.Table;
            var plan = LaunchPlanner.ProfileLaunchGroups(leaves, discoveryNodes, discoveryIndex, table, p.NodeSeconds, p.SlowNodeSeconds,
                p.LaneSlowNodeSeconds, p.HeavyFamilyFactor, p.TargetSeconds, p.LoneClassBudgetSeconds, p.MaxGroupOperands);
            var error = PsValue.Prop(plan, "Error");
            if (error != null) throw new NightlyRefusal(PsValue.Str(error));
            return PsValue.AsArray(PsValue.Prop(plan, "Groups"));
        }

        /// <summary>
        /// ConvertTo-NightlyProfileLaunchGroups -BoundaryAware (ConvertTo-NightlyBoundaryLaunchGroupsIndexed): opt-in
        /// metadata only. The discovery is indexed once; each leaf (one class, an exact leaf's declared cases complete)
        /// goes to its class's boundary (missing or unknown: unassigned); each boundary in ordinal order is packed by the
        /// profile planner (LaunchPlanner.BoundaryPackedChunks), every chunk gets its case membership, and the chunks are
        /// completed with their S2 ids (CompleteBoundaryLaunchGroups). The case partition must be exact. Grants no carry
        /// credit.
        /// </summary>
        public static object[] BoundaryLaunchGroups(object[] leaves, object[] discoveryNodes, string suiteId, IDictionary classBoundaries,
            NightlyClassCosts costs, NightlyLaunchPrices prices)
        {
            var p = prices ?? NightlyLaunchPrices.Default;
            var costTable = costs?.Table;
            var index = NightlyLaunchPlan.IndexBoundaryDiscovery(discoveryNodes, costTable, p.NodeSeconds, p.SlowNodeSeconds, p.LaneSlowNodeSeconds, p.HeavyFamilyFactor);
            var indexError = PsValue.Prop(index, "Error");
            if (PsValue.IsTrue(indexError)) throw new NightlyRefusal(PsValue.Str(indexError));
            var classes = PsValue.Unwrap(PsValue.Prop(index, "Classes")) as IDictionary;
            var universe = PsValue.Unwrap(PsValue.Prop(index, "Universe")) as IDictionary;
            var jsonByUid = PsValue.Unwrap(PsValue.Prop(index, "JsonByUid"));
            var mapping = new Dictionary<string, object>(StringComparer.Ordinal);
            if (classBoundaries != null) foreach (DictionaryEntry pair in classBoundaries) mapping.Add(PsValue.Str(pair.Key), pair.Value);
            var partitions = new Dictionary<string, List<object>>(StringComparer.Ordinal);
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var original in leaves ?? Array.Empty<object>())
            {
                var leaf = original;
                var leafClasses = PsValue.AsArray(PsValue.Prop(leaf, "classes"));
                if (leafClasses.Length != 1) throw new NightlyRefusal("boundary-pack: leaf must have one class");
                var bare = PsValue.Str(leafClasses[0]);
                if (classes == null || !classes.Contains(bare)) throw new NightlyRefusal("boundary-pack: leaf has no discovery membership");
                var members = PsValue.AsArray(PsValue.Prop(classes[bare], "Nodes"));
                var exactFilter = PsValue.Prop(leaf, "exactFilter");
                if (PsValue.IsTrue(exactFilter))
                {
                    var kept = new List<object>();
                    foreach (var node in members)
                    {
                        var parts = NightlyLaunchPlan.BareTypeParts(PsValue.Str(PsValue.Prop(node, "ClassName")));
                        var path = string.Format(CultureInfo.CurrentCulture, "/*/{0}/{1}/{2}", PsValue.Prop(parts, "Namespace"), PsValue.Prop(parts, "Type"),
                            PsValue.Prop(node, "Method"));
                        if (PsValue.Eq(path, exactFilter, false)) kept.Add(node);
                    }
                    members = kept.ToArray();
                    var declared = Uids(PsValue.AsArray(PsValue.Prop(leaf, "nodes")));
                    var actual = Uids(members);
                    if (!SameSequence(declared, actual)) throw new NightlyRefusal("boundary-pack: incomplete exact-method cases");
                    leaf = PsValue.Record(("id", PsValue.Prop(leaf, "id")), ("classes", new object[] { bare }), ("exactFilter", exactFilter), ("nodes", members));
                }
                if (members.Length == 0) throw new NightlyRefusal("boundary-pack: empty leaf membership");
                foreach (var node in members)
                    if (!expected.Add(PsValue.Str(PsValue.Prop(node, "Uid")))) throw new NightlyRefusal("boundary-pack: overlapping leaves");
                var boundary = "unassigned";
                if (mapping.TryGetValue(bare, out var mapped) && PsValue.BaseObject(mapped) is string named && Array.IndexOf(Boundaries, named) >= 0) boundary = named;
                if (!partitions.TryGetValue(boundary, out var partition)) partitions.Add(boundary, partition = new List<object>());
                partition.Add(leaf);
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var memberships = new List<object>();
            var ordered = new List<string>(partitions.Keys);
            ordered.Sort(StringComparer.Ordinal);
            var result = new List<object>();
            var prefixCache = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (var boundary in ordered)
            {
                // Compiled (LaunchPlanner.BoundaryPackedChunks): the profile planner's pack over this boundary's leaves.
                var pack = LaunchPlanner.BoundaryPackedChunks(partitions[boundary].ToArray(), classes, universe, prefixCache, costTable,
                    p.NodeSeconds, p.SlowNodeSeconds, p.LaneSlowNodeSeconds, p.HeavyFamilyFactor, p.TargetSeconds, p.LoneClassBudgetSeconds, p.MaxGroupOperands);
                var packError = PsValue.Prop(pack, "Error");
                if (packError != null) throw new NightlyRefusal(PsValue.Str(packError));
                foreach (var chunk in PsValue.AsArray(PsValue.Prop(pack, "Groups")))
                {
                    var packed = NightlyLaunchPlan.ChunkMembershipForChunk(PsValue.Prop(chunk, "nodes"), PsValue.Prop(chunk, "classes"), classes, jsonByUid, expected, seen);
                    var packedError = PsValue.Prop(packed, "Error");
                    if (PsValue.IsTrue(packedError)) throw new NightlyRefusal(PsValue.Str(packedError));
                    memberships.Add(new RcBoundaryMembership { Suite = suiteId ?? "", Boundary = boundary, Identities = Members(PsValue.AsArray(PsValue.Prop(packed, "Identities"))) });
                    PsValue.AddMemberForce(chunk, "caseMembership", PsValue.AsArray(PsValue.Prop(packed, "Cases")));
                    PsValue.AddMemberForce(chunk, "caseIdentityBasis", "discovery-only");
                    result.Add(chunk);
                }
            }
            if (!seen.SetEquals(expected)) throw new NightlyRefusal("boundary-pack: incomplete case partition");
            return CompleteBoundaryLaunchGroups(result.ToArray(), memberships.ToArray());
        }

        /// <summary>
        /// Complete-NightlyBoundaryLaunchGroups: resolution stays at the packer/S2 boundary so the collision policy is
        /// shared. Each chunk takes its resolved membership's id (in place) and its suite, boundary, membershipDigest,
        /// identityKeys, caseKeys, alwaysDirty and collision (added, replacing any). A membership is an
        /// RcBoundaryMembership or a { suite, boundary, identities = { identityKey, caseKey }... } record.
        /// </summary>
        public static object[] CompleteBoundaryLaunchGroups(object[] chunks, object[] memberships)
        {
            var count = chunks == null ? 0 : chunks.Length;
            if (count != (memberships == null ? 0 : memberships.Length)) throw new NightlyRefusal("boundary-pack: launch/membership count mismatch");
            if (count == 0) return Array.Empty<object>();
            var typed = new RcBoundaryMembership[memberships.Length];
            for (var i = 0; i < typed.Length; i++) typed[i] = Membership(memberships[i]);
            var resolved = RcBoundary.ResolveChunks(typed);
            for (var i = 0; i < count; i++)
            {
                var chunk = chunks[i];
                var membership = resolved[i];
                if (!RcBoundary.IsChunkId(membership.ChunkId ?? "")) throw new NightlyRefusal("boundary-pack: chunk id length");
                SetExisting(chunk, "id", membership.ChunkId);
                PsValue.AddMemberForce(chunk, "suite", membership.Suite);
                PsValue.AddMemberForce(chunk, "boundary", membership.Boundary);
                PsValue.AddMemberForce(chunk, "membershipDigest", membership.FullDigest);
                PsValue.AddMemberForce(chunk, "identityKeys", Objects(membership.IdentityKeys));
                PsValue.AddMemberForce(chunk, "caseKeys", Objects(membership.CaseKeys));
                PsValue.AddMemberForce(chunk, "alwaysDirty", membership.AlwaysDirty);
                PsValue.AddMemberForce(chunk, "collision", membership.Collision);
            }
            return chunks;
        }

        public const string RepositoryInvariantCategory = "RepositoryInvariant";
        public const string RepositoryInvariantFilter = "/*/*/*/*[Category=RepositoryInvariant]";

        internal static bool RepositoryInvariantNode(object node) =>
            PsValue.AsArray(PsValue.Prop(node, "Categories")).Any(category =>
                string.Equals(PsValue.Str(category), RepositoryInvariantCategory, StringComparison.Ordinal));

        internal static bool IsInvariantChunk(object chunk) =>
            PsValue.IsTrue(PsValue.Prop(chunk, "repositoryInvariant"));

        internal static bool IsInvariantClass(string className) =>
            !string.IsNullOrEmpty(className) && className.Contains(".Invariants.", StringComparison.Ordinal);

        private static object[] RepositoryInvariantLaunchPlan(object[] discovery, object[] declared, object exclusions,
            string suiteId, object policy, NightlyClassCosts costs)
        {
            var all = discovery.GroupBy(node => NightlyCensus.BareClassIdentity(PsValue.Str(PsValue.Prop(node, "ClassName"))),
                StringComparer.Ordinal).ToArray();
            var planned = new List<object>();
            var admittedUids = new HashSet<string>(StringComparer.Ordinal);
            var actualUids = new HashSet<string>(StringComparer.Ordinal);
            var owners = declared.Select(chunk => PsValue.AsArray(PsValue.Prop(chunk, "classes")).Select(PsValue.Str)
                .ToHashSet(StringComparer.Ordinal)).ToArray();

            foreach (var group in all)
            {
                var testClass = group.Key;
                if (string.IsNullOrEmpty(testClass)) throw new NightlyRefusal("invariant-discovery-missing-class");
                foreach (var node in group)
                    if (RepositoryInvariantNode(node) != IsInvariantClass(testClass))
                        throw new NightlyRefusal("repository-invariant-designation:" + testClass);

                var required = group.Where(node => !NightlyCensus.Excluded(node, exclusions, true)).ToArray();
                if (required.Length == 0) continue;
                var shortName = testClass.Substring(testClass.LastIndexOf('.') + 1);
                if (owners.Count(owner => owner.Count == 0 || owner.Contains(testClass) || owner.Contains(shortName)) != 1)
                    throw new NightlyRefusal("invariant-discovery-unmapped-or-overlapping-class:" + testClass);
                foreach (var node in required)
                {
                    var uid = PsValue.Str(PsValue.Prop(node, "Uid"));
                    if (string.IsNullOrWhiteSpace(uid) || !admittedUids.Add(uid))
                        throw new NightlyRefusal("invariant-discovery-duplicate-or-missing-uid:" + uid);
                }
            }

            // Keep suite and declared ownership. Filter through the normal planner separately for each phase;
            // it preserves method grouping and the original profile's exact exclusion handling.
            foreach (var phase in new[] { true, false })
            {
                var separate = declared.Select((chunk, index) =>
                {
                    var owner = owners[index];
                    var classes = all.Where(group => IsInvariantClass(group.Key) == phase)
                        .Where(group => owner.Count == 0 || owner.Contains(group.Key)
                            || owner.Contains(group.Key.Substring(group.Key.LastIndexOf('.') + 1)))
                        .Select(group => (object)group.Key).ToArray();
                    return PsValue.Record(("id", (phase ? "invariant-" : "ordinary-") + PsValue.Str(PsValue.Prop(chunk, "id"))),
                        ("classes", classes));
                }).Where(chunk => PsValue.AsArray(PsValue.Prop(chunk, "classes")).Length > 0).ToArray();
                var leaves = LaunchPlanner.ProfileExecutionChunks(discovery, separate, exclusions);
                var packed = PackProfileLaunchPlan(leaves, discovery, suiteId, policy, costs);
                var byClass = all.ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
                foreach (var chunk in packed)
                {
                    var selectedNodes = PsValue.AsArray(PsValue.Prop(chunk, "nodes") ?? Array.Empty<object>());
                    if (selectedNodes.Length == 0)
                        selectedNodes = PsValue.AsArray(PsValue.Prop(chunk, "classes"))
                            .SelectMany(name => byClass[PsValue.Str(name)]).ToArray();
                    var uids = selectedNodes.Where(node => !NightlyCensus.Excluded(node, exclusions, true))
                        .Select(node => PsValue.Str(PsValue.Prop(node, "Uid"))).ToArray();
                    if (uids.Length == 0 || uids.Any(uid => !actualUids.Add(uid)))
                        throw new NightlyRefusal("invariant-discovery-overlapping-or-empty-chunk:" + PsValue.Str(PsValue.Prop(chunk, "id")));
                    if (phase)
                    {
                        PsValue.AddMemberForce(chunk, "id", "invariant-" + PsValue.Str(PsValue.Prop(chunk, "id")));
                        PsValue.AddMemberForce(chunk, "repositoryInvariant", true);
                        PsValue.AddMemberForce(chunk, "alwaysDirty", true);
                        PsValue.AddMemberForce(chunk, "plannedInvariantUids", uids);
                    }
                    planned.Add(chunk);
                }
            }
            if (!admittedUids.SetEquals(actualUids))
                throw new NightlyRefusal("invariant-discovery-incomplete-partition:" + suiteId);
            return planned.ToArray();
        }

        /// <summary>
        /// Get-NightlyProfileLaunchPlan: the profile's launch leaves (LaunchPlanner.ProfileExecutionChunks) packed into
        /// launch groups, each refused before any test process starts when its estimate is not a positive finite number
        /// within the ceiling (2400 s; 1800 s for an Antiphon.E2E class, CARD-1567: the 60-minute native watchdog detects
        /// hangs, not packing overflow) or when the watchdog does not admit it; an admitted launch notes watchdogSeconds,
        /// watchdogReason and watchdogAdmitted.
        /// </summary>
        public static object[] ProfileLaunchPlan(object[] discoveryNodes, object[] chunks, object exclusions, string suiteId, object policyObject, NightlyClassCosts costs)
        {
            var leaves = LaunchPlanner.ProfileExecutionChunks(discoveryNodes, chunks, exclusions);
            return PackProfileLaunchPlan(leaves, discoveryNodes, suiteId, policyObject, costs);
        }

        private static object[] PackProfileLaunchPlan(object[] leaves, object[] discoveryNodes, string suiteId, object policyObject, NightlyClassCosts costs)
        {
            var plan = ProfileLaunchGroups(leaves, discoveryNodes, null, costs, NightlyLaunchPrices.Default);
            foreach (var launch in plan)
            {
                var seconds = PsValue.Double(PsValue.Prop(launch, "seconds"));
                var classes = PsValue.AsArray(PsValue.Prop(launch, "classes"));
                var e2e = false;
                foreach (var cls in classes) if (PsValue.Str(cls).StartsWith("Antiphon.E2E.", StringComparison.Ordinal)) e2e = true;
                var ceiling = e2e ? 1800 : 2400;
                if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0 || seconds > ceiling)
                    throw new NightlyRefusal(string.Format(CultureInfo.CurrentCulture, "launch-estimate-ceiling id={0} seconds={1} ceiling={2}",
                        PsValue.Prop(launch, "id"), seconds, ceiling));
                var suiteForWatchdog = string.IsNullOrWhiteSpace(suiteId) ? "antiphon" : suiteId;
                if (e2e) suiteForWatchdog = "e2e";
                var admission = NightlyWatchdog.ResolveLaunch(suiteForWatchdog, seconds, "default", "", null, null, policyObject);
                var watchdogSeconds = PsValue.Double(PsValue.Prop(admission, "WatchdogSeconds"));
                if (!PsValue.IsTrue(PsValue.Prop(admission, "Admitted")))
                {
                    var watchdogText = string.Format(CultureInfo.InvariantCulture, "{0:0}", watchdogSeconds);
                    throw new NightlyRefusal(string.Format(CultureInfo.CurrentCulture, "watchdog-admission-refused id={0} reason={1} watchdogSeconds={2}",
                        PsValue.Prop(launch, "id"), PsValue.Prop(admission, "Reason"), watchdogText));
                }
                PsValue.AddMemberForce(launch, "watchdogSeconds", watchdogSeconds);
                PsValue.AddMemberForce(launch, "watchdogReason", PsValue.Str(PsValue.Prop(admission, "Reason")));
                PsValue.AddMemberForce(launch, "watchdogAdmitted", PsValue.IsTrue(PsValue.Prop(admission, "Admitted")));
            }
            return plan;
        }

        /// <summary>
        /// Resolve-NightlySuiteChunks: master nightly is not profile-aware and has no exclusions, so it stays the declared
        /// chunks (the single all chunk); only a native profile that actually excludes packs leaves (ProfileLaunchPlan).
        /// </summary>
        public static object[] SuiteChunks(object[] discoveryNodes, object[] chunks, object exclusions, bool profileAware, bool native,
            string suiteId, object policyObject, NightlyClassCosts costs)
        {
            if (native && profileAware && discoveryNodes != null && discoveryNodes.Any(node => RepositoryInvariantNode(node) || IsInvariantClass(PsValue.Str(PsValue.Prop(node, "ClassName")))))
                return RepositoryInvariantLaunchPlan(discoveryNodes, chunks, exclusions, suiteId, policyObject, costs);
            if (native && profileAware && PsValue.AsArray(exclusions).Length > 0)
                return ProfileLaunchPlan(discoveryNodes, chunks, exclusions, suiteId, policyObject, costs);
            return PsValue.AsArray(chunks);
        }

        // ---- suites, hosts and prerequisites ----------------------------------------------------------------------

        /// <summary>
        /// Test-NightlySuiteIsNative: a native suite runs a built TUnit assembly (project and assembly declared): it owns its
        /// process tree, serialises against every other native suite and must produce TRX plus discovery and execution
        /// diagnostics. The legacy id list is only the fallback for a suite that predates those fields.
        /// </summary>
        public static bool SuiteIsNative(object suite, string suiteId)
        {
            if (suite != null)
            {
                var project = PsValue.Str(PsValue.Prop(suite, "project"));
                var assembly = PsValue.Str(PsValue.Prop(suite, "assembly"));
                if (!string.IsNullOrWhiteSpace(project) && !string.IsNullOrWhiteSpace(assembly)) return true;
            }
            foreach (var group in NativeGroups) if (string.Equals(group, suiteId ?? "", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Test-NightlyE2EPrerequisites (D-6): before any E2E chunk runs, the matching Playwright Chromium is installed
        /// through the generated tests/Antiphon.E2E/bin/Debug/net9.0/playwright.ps1 (pwsh -NoProfile -NonInteractive -File
        /// &lt;it&gt; install chromium, Owned custody, 15 minutes, e2e-playwright-install.log) and the client bundle must exist.
        /// A missing browser is red, never a skip; every probe's real outcome is a row. install runs the launch and returns
        /// its exit code (the caller's owned process).
        /// </summary>
        public static NightlyE2EPrerequisites E2EPrerequisites(string repoRoot, string logRoot, IDictionary environment, string bundlePath,
            Func<NightlyLaunch, int> install)
        {
            var rows = new List<object>();
            var ok = true;
            var installer = Path.Combine(repoRoot ?? "", "tests", "Antiphon.E2E", "bin", "Debug", "net9.0", "playwright.ps1");
            if (!Exists(installer))
            {
                rows.Add(Row("playwright-script", 1, string.Format(CultureInfo.CurrentCulture, "missing {0}", installer)));
                ok = false;
            }
            else
            {
                if (install == null) throw new ArgumentNullException(nameof(install));
                var launch = new NightlyLaunch("pwsh", new[] { "-NoProfile", "-NonInteractive", "-File", installer, "install", "chromium" }, repoRoot ?? "",
                    900000, StringMap(environment), Path.Combine(logRoot ?? "", "e2e-playwright-install.log"), NightlyCustody.Owned);
                var exit = install(launch);
                rows.Add(Row("playwright-install", exit, ""));
                if (exit != 0) ok = false;
            }
            if (!string.IsNullOrWhiteSpace(bundlePath))
            {
                var present = Exists(bundlePath);
                rows.Add(Row("client-bundle", present ? 0 : 1, bundlePath));
                if (!present) ok = false;
            }
            return new NightlyE2EPrerequisites(ok, rows.ToArray());
        }

        /// <summary>
        /// Resolve-NightlyTestHost: tests/&lt;assembly&gt;/bin/Debug/net9.0/&lt;assembly&gt;.exe when it exists; else the .dll
        /// under dotnet exec (dotnet as Get-Command finds it); else the legacy .exe path.
        /// </summary>
        public static NightlyTestHost TestHost(string repoRoot, string assembly)
        {
            var root = repoRoot ?? "";
            var name = assembly ?? "";
            var dir = Path.Combine(root, "tests", name, "bin", "Debug", "net9.0");
            var exe = Path.Combine(dir, name + ".exe");
            var dll = Path.Combine(dir, name + ".dll");
            if (Exists(exe)) return new NightlyTestHost(exe, Array.Empty<string>(), dll);
            if (Exists(dll)) return new NightlyTestHost(OwnedLauncher.FindApplication("dotnet") ?? "dotnet", new[] { "exec", dll }, dll);
            var legacy = Path.Combine(root, string.Format(CultureInfo.CurrentCulture, "tests\\{0}\\bin\\Debug\\net9.0\\{0}.exe", name).Replace('\\', Path.DirectorySeparatorChar));
            return new NightlyTestHost(legacy, Array.Empty<string>(), legacy);
        }

        /// <summary>
        /// Resolve-NightlyChunkDiscoverySeconds (and Resolve-NightlyProductionChunkDiscovery, its copy): a supplemental
        /// launch's discovery is the suite's plus its own marker, rounded to milliseconds; another chunk's is its own marker,
        /// or null when it ran none. The suite constant is never copied to a chunk.
        /// </summary>
        public static NightlyChunkDiscovery ChunkDiscoverySeconds(double suiteDiscoverySeconds, double? chunkMarkerSeconds, bool supplementalLaunch)
        {
            double? chunk = null;
            if (supplementalLaunch) chunk = Math.Round(suiteDiscoverySeconds + (chunkMarkerSeconds ?? 0.0), 3);
            else if (chunkMarkerSeconds.HasValue) chunk = chunkMarkerSeconds.Value;
            return new NightlyChunkDiscovery(chunk, false, suiteDiscoverySeconds);
        }

        // ---- the native argv ----------------------------------------------------------------------------------------

        /// <summary>
        /// Get-NightlyNativeExecutionArguments: the MTP arguments of a native launch - no progress, no ANSI, a fresh TRX
        /// (its basename, the directory carried separately), MTP diagnostics in the given directory - plus at most one
        /// filter: the exact method, a method group (CARD-1410, /*/Namespace/Type/(MethodA*)|(MethodB*)), a class group
        /// (CARD-0403, /*/Namespace/(ClassA*)|(ClassB*)/*; no prefix operand), or the one class's bare
        /// /*/Namespace/Type/* (TUnit's treenode path has no constructor arguments).
        /// </summary>
        public static string[] NativeExecutionArguments(string trxPath, string diagnosticDirectory, string[] classes, string exactFilter, string groupFilter, string methodFilter)
        {
            if (string.IsNullOrWhiteSpace(trxPath)) throw new NightlyRefusal("missing trx path");
            if (string.IsNullOrWhiteSpace(diagnosticDirectory)) throw new NightlyRefusal("missing diagnostic directory");
            var trxName = Path.GetFileName(trxPath);
            var resultsDir = Path.GetDirectoryName(trxPath);
            if (string.IsNullOrWhiteSpace(trxName)) throw new NightlyRefusal("missing trx filename");
            if (PathSeparators.IsMatch(trxName)) throw new NightlyRefusal("trx filename must be a basename");
            if (string.IsNullOrWhiteSpace(resultsDir)) throw new NightlyRefusal("missing trx results directory");
            var args = new List<string>
            {
                "--no-progress", "--no-ansi", "--report-trx", "--report-trx-filename", trxName, "--results-directory", resultsDir,
                "--diagnostic", "--diagnostic-output-directory", diagnosticDirectory,
            };
            var filters = 0;
            foreach (var filter in new[] { exactFilter, groupFilter, methodFilter }) if (!string.IsNullOrWhiteSpace(filter)) filters++;
            if (filters > 1) throw new NightlyRefusal("ambiguous-chunk-filter");
            if (!string.IsNullOrEmpty(exactFilter))
            {
                if (!ExactFilterShape.IsMatch(exactFilter)) throw new NightlyRefusal("invalid-exact-filter");
                args.Add("--treenode-filter");
                args.Add(exactFilter);
            }
            else if (!string.IsNullOrWhiteSpace(methodFilter))
            {
                if (!NightlyLaunchPlan.MethodGroupFilter(classes, methodFilter)) throw new NightlyRefusal("invalid-method-filter");
                args.Add("--treenode-filter");
                args.Add(methodFilter);
            }
            else if (!string.IsNullOrWhiteSpace(groupFilter))
            {
                if (!NightlyLaunchPlan.ClassGroupFilter(classes, groupFilter)) throw new NightlyRefusal("invalid-group-filter");
                args.Add("--treenode-filter");
                args.Add(groupFilter);
            }
            else if (classes != null && classes.Length > 0 && (classes.Length > 1 || !string.IsNullOrEmpty(classes[0])))
            {
                // `$Classes -and @($Classes).Count -gt 0`: a one-element list is as true as its element.
                if (classes.Length != 1) throw new NightlyRefusal("multiple classes require split exact roster chunks");
                var cls = NightlyCensus.BareClassIdentity(classes[0] ?? "") ?? "";
                if (!ClassFilterShape.IsMatch(cls)) throw new NightlyRefusal("invalid-class-filter");
                var dot = cls.LastIndexOf('.');
                var ns = dot < 0 ? "*" : cls.Substring(0, dot);
                var type = dot < 0 ? cls : cls.Substring(dot + 1);
                args.Add("--treenode-filter");
                args.Add("/*/" + ns + "/" + type + "/*");
            }
            return args.ToArray();
        }

        // ---- npm, platform, outcome ---------------------------------------------------------------------------------

        /// <summary>
        /// Resolve-NightlyNpmLauncher: the first npm.cmd, npm.exe or npm file in a PATH directory, in PATH order, as a full
        /// path. Refuses "npm launcher not found" for a blank PATH or no launcher.
        /// </summary>
        public static string NpmLauncher(string pathValue)
        {
            if (string.IsNullOrWhiteSpace(pathValue)) throw new NightlyRefusal("npm launcher not found");
            foreach (var dir in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (var name in NpmNames)
                {
                    string candidate;
                    try { candidate = Path.Combine(dir.Trim(), name); }
                    catch (ArgumentException) { continue; }
                    if (!File.Exists(candidate)) continue;
                    var full = Path.GetFullPath(candidate);
                    if (!Path.IsPathRooted(full)) throw new NightlyRefusal("npm launcher is not absolute");
                    return full;
                }
            }
            throw new NightlyRefusal("npm launcher not found");
        }

        /// <summary>
        /// Get-NightlyNpmLaunch: comSpec /c &lt;absolute npm launcher&gt; &lt;npm arguments&gt;. A quoted bare npm.cmd makes
        /// cmd.exe set %~dp0 to the client cwd (CARD-1341), so the launcher is always the resolved full path.
        /// </summary>
        public static NightlyNpmLaunch NpmLaunch(string[] npmArguments, string pathValue, string comSpec)
        {
            if (npmArguments == null || npmArguments.Length == 0) throw new ArgumentException("npm arguments are required", nameof(npmArguments));
            var launcher = NpmLauncher(pathValue);
            var argv = new List<string> { "/c", launcher };
            argv.AddRange(npmArguments);
            return new NightlyNpmLaunch(comSpec ?? "", argv.ToArray());
        }

        /// <summary>
        /// Get-NightlyExecutionPlatform: the observed platform (INightlyHostProbe.Platform, or the HostPlatform seam), which
        /// must be exactly windows or linux; anything else refuses unsupported-host-platform. The observation replaces only
        /// OS observation, never policy or coverage decisions.
        /// </summary>
        public static string ExecutionPlatform(string observed)
        {
            if (!string.Equals(observed, "windows", StringComparison.Ordinal) && !string.Equals(observed, "linux", StringComparison.Ordinal))
                throw new NightlyRefusal("unsupported-host-platform");
            return observed;
        }

        /// <summary>
        /// Set-NightlyOutcome: the run outcome after a failure class. A green run takes the class; otherwise the earlier of
        /// PREFLIGHT, BUILD, TESTS, TIMEOUT wins (an unknown current outcome yields to a known class).
        /// </summary>
        public static string Outcome(string current, string failureClass)
        {
            if (string.Equals(current, "green", StringComparison.OrdinalIgnoreCase)) return failureClass;
            var currentIndex = current == null ? -1 : Array.IndexOf(OutcomeOrder, current);
            var newIndex = failureClass == null ? -1 : Array.IndexOf(OutcomeOrder, failureClass);
            if (newIndex >= 0 && (currentIndex < 0 || newIndex < currentIndex)) return failureClass;
            return current;
        }

        // ---- helpers ------------------------------------------------------------------------------------------------

        // Test-Path -LiteralPath: a file or a directory.
        private static bool Exists(string path) => !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        private static OrderedDictionary Row(string name, int exitCode, string detail) =>
            new OrderedDictionary(StringComparer.OrdinalIgnoreCase) { { "name", name }, { "exitCode", exitCode }, { "detail", detail } };

        private static IReadOnlyDictionary<string, string> StringMap(IDictionary environment)
        {
            if (environment == null) return null;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry pair in environment) map[PsValue.Str(pair.Key)] = pair.Value == null ? null : PsValue.Str(pair.Value);
            return map;
        }

        // [string[]]@(... | ForEach-Object { [string]$_.Uid }) sorted ordinally.
        private static string[] Uids(object[] nodes)
        {
            var uids = new string[nodes.Length];
            for (var i = 0; i < nodes.Length; i++) uids[i] = PsValue.Str(PsValue.Prop(nodes[i], "Uid"));
            Array.Sort(uids, StringComparer.Ordinal);
            return uids;
        }

        private static bool SameSequence(string[] left, string[] right)
        {
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++) if (!string.Equals(left[i], right[i], StringComparison.Ordinal)) return false;
            return true;
        }

        private static object[] Objects(string[] values)
        {
            var objects = new object[values == null ? 0 : values.Length];
            for (var i = 0; i < objects.Length; i++) objects[i] = values[i];
            return objects;
        }

        // The packer's { identityKey, caseKey } identities as S2 members.
        private static RcBoundaryMember[] Members(object[] identities)
        {
            var members = new RcBoundaryMember[identities.Length];
            for (var i = 0; i < members.Length; i++) members[i] = Member(identities[i]);
            return members;
        }

        private static RcBoundaryMember Member(object identity)
        {
            if (identity == null) return null;
            if (PsValue.BaseObject(identity) is RcBoundaryMember typed) return typed;
            return new RcBoundaryMember { IdentityKey = Text(PsValue.Prop(identity, "identityKey")), CaseKey = Text(PsValue.Prop(identity, "caseKey")) };
        }

        // PowerShell's conversion of a { suite, boundary, identities } record to RcBoundaryMembership: absent stays null.
        private static RcBoundaryMembership Membership(object membership)
        {
            if (membership == null) return null;
            if (PsValue.BaseObject(membership) is RcBoundaryMembership typed) return typed;
            var identities = PsValue.Prop(membership, "identities");
            return new RcBoundaryMembership
            {
                Suite = Text(PsValue.Prop(membership, "suite")),
                Boundary = Text(PsValue.Prop(membership, "boundary")),
                Identities = identities == null ? null : Members(PsValue.AsArray(identities)),
            };
        }

        private static string Text(object value) => value == null ? null : PsValue.Str(value);

        // $record.<name> = value: sets an existing property in place (its order kept); a missing one refuses.
        private static void SetExisting(object record, string name, object value)
        {
            if (PsRuntime.Loaded) { PsFields.SetValue(record, name, value); return; }
            switch (record)
            {
                case PlainObject plain when plain.Has(name): plain[name] = value; return;
                case IDictionary dictionary: dictionary[name] = value; return;
            }
            throw new NightlyRefusal("The property '" + name + "' cannot be found on this object. Verify that the property exists and can be set.");
        }
    }
}
