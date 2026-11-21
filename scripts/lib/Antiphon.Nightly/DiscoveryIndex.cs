// The discovery census and the per-suite discovery index (moved from nightly-coverage.ps1). D-5: every discovery UID
// resolves to exactly one disposition, and a launch is judged against its suite's discovery: CARD-1419 parses and
// censuses each discovery once and looks up only the launch's classes. Nodes are the discovery reader's PowerShell
// objects (PlainObjects for a plain caller); records are built through PsValue.
using System;
using System.Collections.Generic;

namespace Antiphon.Nightly
{
    /// <summary>One discovery file's parse: its document, its InProgress nodes, and one view per exclusion policy.</summary>
    public sealed class DiscoveryIndexEntry
    {
        public object Document { get; internal set; }
        public object[] InProgress { get; internal set; } = new object[0];
        public Dictionary<string, object> Views { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The discoveries a suite's launches are judged against, keyed by content (not path): the profile lane reuses one
    /// file, the default lane writes the same discovery beside each launch, and a rewrite is new bytes.
    /// </summary>
    public sealed class NightlyDiscoveryIndex
    {
        public Dictionary<string, DiscoveryIndexEntry> Entries { get; } = new Dictionary<string, DiscoveryIndexEntry>(StringComparer.Ordinal);

        /// <summary>Get-NightlyDiscoveryIndexEntry: the parsed discovery at path, read once per content.</summary>
        public DiscoveryIndexEntry Entry(string path)
        {
            var key = "";
            if (System.IO.File.Exists(path))
            {
                // A queued write's bytes, else the disk digest reused while the file is provably unchanged.
                key = EvidenceFiles.CachedSha256(path);
                if (Entries.TryGetValue(key, out var cached)) return cached;
            }
            var document = NightlyDiagnosticLog.ReadDiscovery(path);
            var nodes = PsValue.AsArray(PsValue.Prop(document, "Nodes"));
            var inProgress = DiscoveryViewKey.InProgressNodes(nodes);
            if (inProgress == null)
            {
                var list = new List<object>();
                foreach (var n in nodes) if (string.Equals(PsValue.Str(PsValue.Prop(n, "State")), "InProgress", StringComparison.OrdinalIgnoreCase)) list.Add(n);
                inProgress = list.ToArray();
            }
            var entry = new DiscoveryIndexEntry { Document = document, InProgress = inProgress };
            if (key.Length > 0) Entries[key] = entry;
            return entry;
        }

        /// <summary>
        /// Get-NightlyDiscoveryIndexView: one disposition pass per entry and exclusion policy - the census, the all-classes
        /// required set and a class lookup. Views are shared only by exclusions of equal content (DiscoveryViewKey); a
        /// shape that key does not vouch for gets a fresh view.
        /// </summary>
        public static object View(object entry, object exclusions, bool profileAware)
        {
            // An entry from Entry, or one a caller built ({ Document, Views }): its views are a string-keyed dictionary.
            var views = PsOps.Base(PsValue.Prop(entry, "Views")) as System.Collections.IDictionary;
            var policyKey = profileAware ? DiscoveryViewKey.PolicyKey(exclusions) : "default";
            if (policyKey != null && views != null && views.Contains(policyKey)) return views[policyKey];
            var nodes = PsValue.AsArray(PsValue.Prop(PsValue.Prop(entry, "Document"), "Nodes"));
            var index = profileAware ? NightlyPolicy.ExclusionIndex(exclusions) : null;
            var pass = NightlyDiscovery.IndexView(nodes, index);
            var view = PsValue.Record(("Nodes", nodes), ("Excluded", PsValue.Prop(pass, "Excluded")), ("ByClass", PsValue.Prop(pass, "ByClass")),
                ("RequiredUids", PsValue.Prop(pass, "RequiredUids")), ("Classes", PsValue.Prop(pass, "Classes")),
                ("Census", NightlyCensus.Census(nodes, exclusions, profileAware)));
            if (policyKey != null && views != null) views[policyKey] = view;
            return view;
        }

        /// <summary>Get-NightlyDiscoveryIndexEntry for a script: index.Entry(path).</summary>
        public static DiscoveryIndexEntry EntryOf(NightlyDiscoveryIndex index, string path) => (index ?? new NightlyDiscoveryIndex()).Entry(path);

        /// <summary>Get-NightlyDiscoveryIndexRequiredUids: the view's required UIDs of the launch's classes, in discovery order.</summary>
        public static object[] RequiredUids(object view, string[] requiredClasses)
        {
            if (requiredClasses == null || requiredClasses.Length == 0) return PsValue.AsArray(PsValue.Prop(view, "RequiredUids"));
            return NightlyVerdicts.IndexRequiredUids((System.Collections.IDictionary)PsOps.Base(PsValue.Prop(view, "ByClass")),
                (bool[])PsOps.Base(PsValue.Prop(view, "Excluded")), PsValue.Prop(view, "Nodes"), requiredClasses);
        }
    }

    /// <summary>D-5 dispositions and the expanded-UID census.</summary>
    public static class NightlyCensus
    {
        /// <summary>
        /// Get-NightlyBareClassIdentity: TUnit 1.44 appends constructor arguments to a class name
        /// (AgentTuiApiTests(AgentTuiApiWebAppFactory)); filters, rosters, policy rows and TRX keys use the bare name.
        /// </summary>
        public static string BareClassIdentity(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            var open = name.IndexOf('(');
            return open > 0 && name.EndsWith(")", StringComparison.Ordinal) ? name.Substring(0, open) : name;
        }

        /// <summary>Get-NightlyClassNameFromIdentity: the simple name of the bare class identity.</summary>
        public static string ClassNameFromIdentity(string className)
        {
            if (string.IsNullOrWhiteSpace(className)) return className;
            var parts = BareClassIdentity(className).Split('.');
            return parts[parts.Length - 1];
        }

        /// <summary>
        /// CARD-0599 D-5 (Test-NightlyDiscoveryExcluded): in the default lane OptIn/Explicit (or Excluded) excludes; in a
        /// profile lane only a declared class (or class+method) row excludes, so an unclassified node stays required.
        /// </summary>
        public static bool Excluded(object node, object exclusions, bool profileAware)
        {
            if (node == null) return false;
            if (profileAware) return NightlyPolicy.NodeExcluded(node, exclusions);
            return NightlyDiscovery.DefaultExcluded(node);
        }

        /// <summary>
        /// Get-NightlyDiscoveryDisposition: { Disposition, Reason, Owner, Uid } - required, or excluded with the declaring
        /// row's reason and owner (the default lane: category-optin, nightly-default).
        /// </summary>
        public static object Disposition(object node, object exclusions, bool profileAware)
        {
            if (!profileAware)
            {
                var uid = PsValue.Str(PsValue.Prop(node, "Uid"));
                return Excluded(node, null, false)
                    ? NightlyDiscovery.DispositionObject("excluded", "category-optin", "nightly-default", uid)
                    : NightlyDiscovery.DispositionObject("required", "", "", uid);
            }
            return new ProfileExclusionIndex(exclusions).Disposition(node);
        }

        /// <summary>
        /// D-5 (Get-NightlyDiscoveryCensus): every discovered node gets exactly one disposition; duplicates and
        /// unclassified nodes are reported, never dropped. { Ok, Required, Excluded, Duplicates, Digest, RequiredCount,
        /// ExcludedCount }.
        /// </summary>
        public static object Census(object[] nodes, object exclusions, bool profileAware)
        {
            var index = profileAware ? NightlyPolicy.ExclusionIndex(exclusions) : null;
            var rows = NightlyDiscovery.CensusRows(nodes ?? new object[0], index);
            var required = PsValue.AsArray(PsValue.Prop(rows, "Required"));
            var excluded = PsValue.AsArray(PsValue.Prop(rows, "Excluded"));
            var duplicates = PsValue.AsArray(PsValue.Prop(rows, "Duplicates"));
            return PsValue.Record(("Ok", duplicates.Length == 0 && required.Length > 0), ("Required", required), ("Excluded", excluded),
                ("Duplicates", duplicates), ("Digest", PlannedManifestCheck.CensusDigest(required, excluded)),
                ("RequiredCount", required.Length), ("ExcludedCount", excluded.Length));
        }

        /// <summary>Test-NightlyClassAssigned: no required classes assigns every class.</summary>
        public static bool ClassAssigned(string className, string typeName, string[] requiredClasses) =>
            requiredClasses == null || requiredClasses.Length == 0 || NightlyClassNames.Assigned(className, typeName, requiredClasses);

        /// <summary>Test-NightlyClassAssignedCached.</summary>
        public static bool ClassAssignedCached(ClassAssignment cache, string className, string typeName) => cache.Assigned(className, typeName);

        /// <summary>Get-NightlyDiscoveryDispositionIndexed: the disposition through a profile exclusion index.</summary>
        public static object DispositionIndexed(ProfileExclusionIndex index, object node) => index.Disposition(node);

        /// <summary>Get-NightlyRequiredDiscoveryUids: the required UIDs of the classes (every class when none), in order.</summary>
        public static object[] RequiredUids(object[] nodes, string[] requiredClasses, object exclusions, bool profileAware) =>
            NightlyDiscovery.RequiredUids(nodes ?? new object[0], requiredClasses ?? new string[0], profileAware ? NightlyPolicy.ExclusionIndex(exclusions) : null);
    }
}
