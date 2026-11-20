// ConvertTo-NightlyProfileLaunchGroups (nightly-tests-impl.ps1) without -BoundaryAware, compiled: the profile
// launch planner that decides chunk membership, so its groups, ids, class order, object shapes and errors are
// exactly what the PowerShell body returned (about 1.4 s a call over the 1,321 leaves of the 19,012-node replay).
//
// The PowerShell semantics mirrored here:
// - Sort-Object (no -Stable) is List<T>.Sort over the input in input order with the current culture,
//   case-insensitive comparison (as NightlyPlannerParts.PackOrder): the same sort over the same sequence with
//   the same comparison gives the same permutation, ties included. Sort-Object emits PSObject-wrapped strings.
// - Group-Object { [string]$_.Method } (PowerShell 7): groups are the current culture case-insensitive
//   equivalence classes, named by their first member, members in input order, emitted in key order.
// - @{} is a Hashtable with OrdinalIgnoreCase keys; @($byNs.Keys) enumerates it, so the same table is built.
// - Add-Member -Force replaces an instance member by removing it and adding the new one last.
// - A PowerShell throw is returned as Error for the caller to throw with the same text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;

namespace Antiphon.Nightly
{
    /// <summary>The profile launch planner of nightly-tests-impl.ps1 (ConvertTo-NightlyProfileLaunchGroups).</summary>
    public static class LaunchPlanner
    {
        private const string NullMethodCall = "You cannot call a method on a null-valued expression.";

        /// <summary>
        /// Whether the PowerShell body's leaf loop reached Get-NightlyLaunchClassCost (which loads the class cost
        /// table) before any throw: its first leaf (<c>@($Leaves)</c>, so a null list is one null leaf) is an
        /// exact leaf or names a class with a namespace.
        /// </summary>
        public static bool NeedsCostTable(object[] leaves)
        {
            var list = leaves ?? new object[] { null };
            if (list.Length == 0) return false;
            var leaf = list[0];
            if (!string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(leaf, "exactFilter")))) return true;
            return NightlyLaunchPlan.BareTypeParts(FirstClass(leaf)) != null;
        }

        /// <summary>
        /// The body of ConvertTo-NightlyProfileLaunchGroups after its -BoundaryAware branch: { Error, Groups }.
        /// Error is the text the PowerShell body threw (Groups is then empty); exact leaves processed before it
        /// already carry their seconds, as they did. <paramref name="discoveryIndex"/> is $DiscoveryIndex (null:
        /// NightlyLaunchPlan.IndexNodes of the discovery); its Universe gains the class items' types, as it did.
        /// </summary>
        public static PSObject ProfileLaunchGroups(object[] leaves, object[] discoveryNodes, object discoveryIndex,
            IDictionary costTable, double nodeSeconds, double slowNodeSeconds, double laneSlowNodeSeconds,
            double heavyFactor, double targetSeconds, double loneClassBudgetSeconds, int maxOperands)
        {
            var cost = new CostModel
            {
                Table = costTable, NodeSeconds = nodeSeconds, SlowNodeSeconds = slowNodeSeconds,
                LaneSlowNodeSeconds = laneSlowNodeSeconds, HeavyFactor = heavyFactor
            };
            var indexed = discoveryIndex ?? NightlyLaunchPlan.IndexNodes(discoveryNodes);
            var nodesByBare = Unwrap(PsValue.Prop(indexed, "NodesByBare"));
            var exact = new List<object>();
            var classItems = new List<ClassItem>();
            var oversized = new List<Oversized>();
            var sourceId = "all";
            foreach (var leaf in leaves ?? new object[] { null })
            {
                var id = PsValue.Str(PsValue.Prop(leaf, "id"));
                // $id.IndexOf('-profile-'): String.IndexOf(string), current culture, as PowerShell calls it.
                var mark = id.IndexOf("-profile-");
                if (mark > 0) sourceId = id.Substring(0, mark);
                if (!string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(leaf, "exactFilter"))))
                {
                    var leafParts = NightlyLaunchPlan.BareTypeParts(FirstClass(leaf));
                    var leafCost = cost.Of(PsValue.AsArray(PsValue.Prop(leaf, "nodes")),
                        leafParts == null ? "" : (string)leafParts.Properties["Type"].Value,
                        leafParts == null ? "" : (string)leafParts.Properties["Bare"].Value);
                    AddNoteForce(leaf, "seconds", Math.Round(leafCost.Seconds, 1));
                    exact.Add(leaf);
                    continue;
                }
                var parts = NightlyLaunchPlan.BareTypeParts(FirstClass(leaf));
                if (parts == null) return Result("profile-discovery-namespace-required", null);
                var bare = (string)parts.Properties["Bare"].Value;
                var ns = (string)parts.Properties["Namespace"].Value;
                var type = (string)parts.Properties["Type"].Value;
                var byBare = nodesByBare as IDictionary;
                if (byBare == null) return Result(NullMethodCall, null);
                var known = new object[0];
                if (byBare.Contains(bare)) known = ToObjectArray(byBare[bare]);
                var classCost = cost.Of(known, type, bare);
                var item = new ClassItem { Bare = bare, Namespace = ns, Type = type, NodeCount = classCost.NodeCount, Seconds = classCost.Seconds };
                item.Ps = new PSObject();
                item.Ps.Properties.Add(new PSNoteProperty("Bare", bare));
                item.Ps.Properties.Add(new PSNoteProperty("Namespace", ns));
                item.Ps.Properties.Add(new PSNoteProperty("Type", type));
                item.Ps.Properties.Add(new PSNoteProperty("Key", bare));
                item.Ps.Properties.Add(new PSNoteProperty("NodeCount", item.NodeCount));
                item.Ps.Properties.Add(new PSNoteProperty("Seconds", item.Seconds));
                // CARD-1584: lone E2E classes stay below 25 minutes.
                var classBudget = loneClassBudgetSeconds;
                if (string.Equals(ns, "Antiphon.E2E", StringComparison.Ordinal) || ns.StartsWith("Antiphon.E2E.", StringComparison.Ordinal)) classBudget = 1500;
                if (known.Length > 0 && classCost.Seconds > classBudget)
                {
                    oversized.Add(new Oversized { Parts = item, Nodes = known });
                    continue;
                }
                classItems.Add(item);
            }

            var byNs = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (var item in classItems)
            {
                if (!byNs.ContainsKey(item.Namespace)) byNs[item.Namespace] = new List<ClassItem>();
                ((List<ClassItem>)byNs[item.Namespace]).Add(item);
            }
            var universe = Unwrap(PsValue.Prop(indexed, "Universe")) as IDictionary;
            if (classItems.Count > 0 && universe == null) return Result(NullMethodCall, null);
            foreach (var item in classItems)
            {
                // New-Object's output, stored as the PSObject it is.
                if (!universe.Contains(item.Namespace)) universe[item.Namespace] = new PSObject(new List<string>());
                var types = universe[item.Namespace];
                if (types is List<string> typed) typed.Add(item.Type);
                else if (Unwrap(types) is IList list) list.Add(item.Type);
                else return Result(NullMethodCall, null);
            }

            return Pack(byNs, exact, oversized, sourceId, ns => NightlyPlannerParts.PrefixUnsafeNames(UniverseNames(universe[ns])), cost,
                targetSeconds, maxOperands, nodeSeconds);
        }

        // The pack shared by both planners: per namespace (Sort-Object order) a first-fit bin pack of the classes
        // whose type prefixes no sibling, each prefix-unsafe class alone, then the oversized classes split into
        // exact-method chunks, then the exact leaves.
        private static PSObject Pack(Hashtable byNs, List<object> exact, List<Oversized> oversized, string sourceId,
            Func<string, HashSet<string>> unsafeFor, CostModel cost, double targetSeconds, int maxOperands, double nodeSeconds)
        {
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            Comparison<string> byCulture = (a, b) => compare.Compare(a, b, CompareOptions.IgnoreCase);
            var groups = new List<object>();
            var index = 0;
            var namespaces = new List<string>();
            foreach (var key in byNs.Keys) namespaces.Add((string)key);
            namespaces.Sort(byCulture);
            foreach (var ns in namespaces)
            {
                var unsafeTypes = unsafeFor(ns);
                var nsItems = (List<ClassItem>)byNs[ns];
                var byPs = new Dictionary<object, ClassItem>(ReferenceEqualityComparer.Instance);
                var pool = new List<object>();
                var alone = new List<ClassItem>();
                foreach (var item in nsItems)
                {
                    if (unsafeTypes.Contains(item.Type)) { alone.Add(item); continue; }
                    pool.Add(item.Ps);
                    byPs[item.Ps] = item;
                }
                var bins = new List<Bin>();
                foreach (var packed in NightlyPlannerParts.SplitBySeconds(pool.ToArray(), targetSeconds, maxOperands, nodeSeconds))
                {
                    var bin = new Bin { Seconds = (double)packed.Properties["Seconds"].Value };
                    foreach (var member in (ArrayList)packed.Properties["Items"].Value) bin.Classes.Add(byPs[member]);
                    bins.Add(bin);
                }
                alone.Sort((a, b) => byCulture(a.Bare, b.Bare));
                foreach (var item in alone)
                {
                    var bin = new Bin { Seconds = item.Seconds };
                    bin.Classes.Add(item);
                    bins.Add(bin);
                }
                foreach (var bin in bins)
                {
                    if (bin.Classes.Count == 0) continue;
                    index++;
                    var types = new List<string>(bin.Classes.Count);
                    var bares = new List<string>(bin.Classes.Count);
                    long sumNodes = 0;
                    foreach (var item in bin.Classes)
                    {
                        types.Add(item.Type);
                        bares.Add(item.Bare);
                        sumNodes += item.NodeCount;
                    }
                    bares.Sort(byCulture);
                    var filter = "";
                    if (types.Count >= 2) filter = ClassGroupFilter(ns, types, byCulture);
                    groups.Add(Launch(Format("{0}-group-{1}", sourceId, index), Wrapped(bares), "", filter, "",
                        new object[0], PsInt(sumNodes), Math.Round(bin.Seconds, 1)));
                }
            }

            var splitIndex = 0;
            oversized.Sort((a, b) => byCulture(a.Parts.Bare, b.Parts.Bare));
            foreach (var big in oversized)
            {
                foreach (var chunk in SplitOversizedClass(big.Parts, big.Nodes, sourceId, ref splitIndex, cost,
                    targetSeconds, maxOperands, nodeSeconds, byCulture))
                    groups.Add(chunk);
            }
            foreach (var leaf in exact) groups.Add(leaf);
            return Result(null, groups.ToArray());
        }

        /// <summary>
        /// New-NightlyBoundaryPackedChunks: the same pack over one boundary's leaves, from the boundary discovery index
        /// (NightlyLaunchPlan.IndexBoundaryDiscovery). A class leaf takes its record's cost; a namespace's prefix-unsafe
        /// types come from the index universe and are computed once per namespace across one pack's boundaries
        /// (prefixCache). { Error, Groups }.
        /// </summary>
        public static PSObject BoundaryPackedChunks(object[] leaves, IDictionary classes, IDictionary universe, IDictionary prefixCache,
            IDictionary costTable, double nodeSeconds, double slowNodeSeconds, double laneSlowNodeSeconds, double heavyFactor,
            double targetSeconds, double loneClassBudgetSeconds, int maxOperands)
        {
            var cost = new CostModel
            {
                Table = costTable, NodeSeconds = nodeSeconds, SlowNodeSeconds = slowNodeSeconds,
                LaneSlowNodeSeconds = laneSlowNodeSeconds, HeavyFactor = heavyFactor
            };
            var exact = new List<object>();
            var classItems = new List<ClassItem>();
            var oversized = new List<Oversized>();
            var sourceId = "all";
            foreach (var leaf in leaves ?? new object[] { null })
            {
                var id = PsValue.Str(PsValue.Prop(leaf, "id"));
                // $id.IndexOf('-profile-'): String.IndexOf(string), current culture, as PowerShell calls it.
                var mark = id.IndexOf("-profile-");
                if (mark > 0) sourceId = id.Substring(0, mark);
                if (!string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(leaf, "exactFilter"))))
                {
                    var leafParts = NightlyLaunchPlan.BareTypeParts(FirstClass(leaf));
                    var leafCost = cost.Of(PsValue.AsArray(PsValue.Prop(leaf, "nodes")),
                        leafParts == null ? "" : (string)leafParts.Properties["Type"].Value,
                        leafParts == null ? "" : (string)leafParts.Properties["Bare"].Value);
                    AddNoteForce(leaf, "seconds", Math.Round(leafCost.Seconds, 1));
                    exact.Add(leaf);
                    continue;
                }
                var rec = classes == null ? null : classes[FirstClass(leaf)];
                var item = new ClassItem
                {
                    Bare = PsValue.Str(PsValue.Prop(rec, "Bare")), Namespace = PsValue.Str(PsValue.Prop(rec, "Namespace")),
                    Type = PsValue.Str(PsValue.Prop(rec, "Type")),
                    NodeCount = (int)LanguagePrimitives.ConvertTo(PsValue.Prop(rec, "NodeCount"), typeof(int), CultureInfo.InvariantCulture),
                    Seconds = (double)LanguagePrimitives.ConvertTo(PsValue.Prop(rec, "Seconds"), typeof(double), CultureInfo.InvariantCulture),
                };
                item.Ps = new PSObject();
                item.Ps.Properties.Add(new PSNoteProperty("Bare", item.Bare));
                item.Ps.Properties.Add(new PSNoteProperty("Namespace", item.Namespace));
                item.Ps.Properties.Add(new PSNoteProperty("Type", item.Type));
                item.Ps.Properties.Add(new PSNoteProperty("Key", item.Bare));
                item.Ps.Properties.Add(new PSNoteProperty("NodeCount", item.NodeCount));
                item.Ps.Properties.Add(new PSNoteProperty("Seconds", item.Seconds));
                // CARD-1584: lone E2E classes stay below 25 minutes.
                var classBudget = loneClassBudgetSeconds;
                if (string.Equals(item.Namespace, "Antiphon.E2E", StringComparison.Ordinal) || item.Namespace.StartsWith("Antiphon.E2E.", StringComparison.Ordinal)) classBudget = 1500;
                if (item.NodeCount > 0 && item.Seconds > classBudget)
                {
                    oversized.Add(new Oversized { Parts = item, Nodes = PsValue.AsArray(PsValue.Prop(rec, "Nodes")) });
                    continue;
                }
                classItems.Add(item);
            }
            var byNs = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (var item in classItems)
            {
                if (!byNs.ContainsKey(item.Namespace)) byNs[item.Namespace] = new List<ClassItem>();
                ((List<ClassItem>)byNs[item.Namespace]).Add(item);
            }
            HashSet<string> UnsafeFor(string ns)
            {
                if (!prefixCache.Contains(ns))
                {
                    var names = universe != null && universe.Contains(ns) ? UniverseNames(universe[ns]) : new string[0];
                    prefixCache[ns] = NightlyPlannerParts.PrefixUnsafeNames(names);
                }
                return (HashSet<string>)Unwrap(prefixCache[ns]);
            }
            return Pack(byNs, exact, oversized, sourceId, UnsafeFor, cost, targetSeconds, maxOperands, nodeSeconds);
        }

        /// <summary>
        /// Split-NightlyOversizedClass: exact-method chunks. Methods that prefix a sibling launch alone on the
        /// plain exact form; the rest pack into (Name*) OR groups.
        /// </summary>
        private static List<PSObject> SplitOversizedClass(ClassItem parts, object[] nodes, string sourceId, ref int splitIndex,
            CostModel cost, double targetSeconds, int maxOperands, double nodeSeconds, Comparison<string> byCulture)
        {
            var methodNames = new string[nodes.Length];
            for (var i = 0; i < nodes.Length; i++) methodNames[i] = PsValue.Str(PsValue.Prop(nodes[i], "Method"));
            var unsafeMethods = NightlyPlannerParts.PrefixUnsafeNames(methodNames);
            // Group-Object { [string]$_.Method }.
            var byKey = new Dictionary<string, MethodGroup>(StringComparer.Create(CultureInfo.CurrentCulture, true));
            var found = new List<MethodGroup>();
            for (var i = 0; i < nodes.Length; i++)
            {
                if (!byKey.TryGetValue(methodNames[i], out var group))
                {
                    group = new MethodGroup { Name = methodNames[i], Position = found.Count };
                    byKey.Add(methodNames[i], group);
                    found.Add(group);
                }
                // Group-Object's input binding wraps each input object in a PSObject.
                group.Nodes.Add(nodes[i] is PSObject ? nodes[i] : PSObject.AsPSObject(nodes[i]));
            }
            // Group-Object 7 emits the groups in a stable sort by key (the keys of distinct groups never tie).
            var ordered = new List<MethodGroup>(found);
            ordered.Sort((a, b) =>
            {
                var order = byCulture(a.Name, b.Name);
                return order != 0 ? order : a.Position.CompareTo(b.Position);
            });
            var items = new List<MethodItem>();
            foreach (var group in ordered)
            {
                var methodNodes = group.Nodes.ToArray();
                var item = new MethodItem { Method = group.Name, Nodes = methodNodes, Seconds = cost.Of(methodNodes, parts.Type, parts.Bare).Seconds, Unsafe = unsafeMethods.Contains(group.Name) };
                item.Ps = new PSObject();
                item.Ps.Properties.Add(new PSNoteProperty("Key", item.Method));
                item.Ps.Properties.Add(new PSNoteProperty("Method", item.Method));
                item.Ps.Properties.Add(new PSNoteProperty("Nodes", methodNodes));
                item.Ps.Properties.Add(new PSNoteProperty("Seconds", item.Seconds));
                item.Ps.Properties.Add(new PSNoteProperty("Unsafe", item.Unsafe));
                items.Add(item);
            }
            var byPs = new Dictionary<object, MethodItem>(ReferenceEqualityComparer.Instance);
            var pool = new List<object>();
            var alone = new List<MethodItem>();
            foreach (var item in items)
            {
                if (item.Unsafe) { alone.Add(item); continue; }
                pool.Add(item.Ps);
                byPs[item.Ps] = item;
            }
            var bins = new List<MethodBin>();
            foreach (var packed in NightlyPlannerParts.SplitBySeconds(pool.ToArray(), targetSeconds, maxOperands, nodeSeconds))
            {
                var bin = new MethodBin { Seconds = (double)packed.Properties["Seconds"].Value };
                foreach (var member in (ArrayList)packed.Properties["Items"].Value) bin.Methods.Add(byPs[member]);
                bins.Add(bin);
            }
            alone.Sort((a, b) => byCulture(a.Method, b.Method));
            foreach (var item in alone)
            {
                var bin = new MethodBin { Seconds = item.Seconds };
                bin.Methods.Add(item);
                bins.Add(bin);
            }
            var chunks = new List<PSObject>();
            foreach (var bin in bins)
            {
                var methods = new List<string>(bin.Methods.Count);
                var chunkNodes = new List<object>();
                foreach (var item in bin.Methods)
                {
                    methods.Add(item.Method);
                    chunkNodes.AddRange(item.Nodes);
                }
                methods.Sort(byCulture);
                var exactFilter = "";
                var methodFilter = "";
                if (methods.Count == 1) exactFilter = Format("/*/{0}/{1}/{2}", parts.Namespace, parts.Type, methods[0]);
                else
                {
                    // New-NightlyMethodGroupFilter sorts its (already sorted) operands again.
                    var operands = new List<string>(methods);
                    operands.Sort(byCulture);
                    methodFilter = "/*/" + parts.Namespace + "/" + parts.Type + "/" + Operands(operands);
                }
                splitIndex++;
                var nodeArray = chunkNodes.ToArray();
                chunks.Add(Launch(Format("{0}-split-{1}", sourceId, splitIndex), new object[] { parts.Bare }, exactFilter, "",
                    methodFilter, nodeArray, nodeArray.Length, Math.Round(bin.Seconds, 1)));
            }
            return chunks;
        }

        /// <summary>New-NightlyClassGroupFilter: /*/ns/(A*)|(B*)/* over the type names in sorted order.</summary>
        private static string ClassGroupFilter(string ns, List<string> typeNames, Comparison<string> byCulture)
        {
            var ordered = new List<string>(typeNames);
            ordered.Sort(byCulture);
            return "/*/" + ns + "/" + Operands(ordered) + "/*";
        }

        /// <summary>$parts -join '|' over (Name*) operands.</summary>
        private static string Operands(List<string> names)
        {
            var parts = new string[names.Count];
            for (var i = 0; i < parts.Length; i++) parts[i] = "(" + names[i] + "*)";
            return string.Join("|", parts);
        }

        /// <summary>The launch [pscustomobject] both packers emit, in its property order.</summary>
        private static PSObject Launch(string id, object[] classes, string exactFilter, string groupFilter, string methodFilter,
            object[] nodes, object nodeCount, double seconds)
        {
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("id", id));
            o.Properties.Add(new PSNoteProperty("classes", classes));
            o.Properties.Add(new PSNoteProperty("exactFilter", exactFilter));
            o.Properties.Add(new PSNoteProperty("groupFilter", groupFilter));
            o.Properties.Add(new PSNoteProperty("methodFilter", methodFilter));
            o.Properties.Add(new PSNoteProperty("nodes", nodes));
            o.Properties.Add(new PSNoteProperty("nodeCount", nodeCount));
            o.Properties.Add(new PSNoteProperty("seconds", seconds));
            return o;
        }

        /// <summary>Sort-Object's output collected by @(): each string in its own PSObject.</summary>
        private static object[] Wrapped(List<string> values)
        {
            var result = new object[values.Count];
            for (var i = 0; i < result.Length; i++) result[i] = PSObject.AsPSObject(values[i]);
            return result;
        }

        /// <summary>PowerShell's int sum: an int, or a double once it no longer fits.</summary>
        private static object PsInt(long value) =>
            value >= int.MinValue && value <= int.MaxValue ? (object)(int)value : (object)(double)value;

        /// <summary>The -f operator (current culture).</summary>
        private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

        /// <summary><c>[string]@($leaf.classes)[0]</c>.</summary>
        private static string FirstClass(object leaf)
        {
            var classes = PsValue.AsArray(PsValue.Prop(leaf, "classes"));
            return PsValue.Str(classes.Length > 0 ? classes[0] : null);
        }

        /// <summary><c>@($nodesByBare[$bare].ToArray())</c>.</summary>
        private static object[] ToObjectArray(object value)
        {
            value = Unwrap(value);
            if (value is ArrayList arrayList) return arrayList.ToArray();
            if (value == null) return new object[0];
            return PsValue.AsArray(value);
        }

        /// <summary><c>[string[]]@($universe[$ns].ToArray())</c>.</summary>
        private static string[] UniverseNames(object value)
        {
            value = Unwrap(value);
            if (value is List<string> typed) return typed.ToArray();
            var items = PsValue.AsArray(value);
            var names = new string[items.Length];
            for (var i = 0; i < items.Length; i++) names[i] = items[i] == null ? null : PsValue.Str(items[i]);
            return names;
        }

        private static object Unwrap(object value) => value is PSObject ps && !(ps.BaseObject is PSCustomObject) ? ps.BaseObject : value;

        /// <summary>Add-Member -InputObject $target -NotePropertyName $name -NotePropertyValue $value -Force.</summary>
        private static void AddNoteForce(object target, string name, object value)
        {
            var ps = PSObject.AsPSObject(target);
            var previous = ps.Members[name];
            if (previous != null && previous.IsInstance) ps.Members.Remove(name);
            ps.Members.Add(new PSNoteProperty(name, value));
        }

        /// <summary>
        /// Get-NightlyProfileExecutionChunks: the launch leaves of a profile. Per chunk, the discovery nodes of its classes
        /// bucketed by bare class (NightlyDiscovery.BucketByBareClass); a class with no excluded node is one leaf, a class
        /// with some excluded nodes is one exact-method leaf per required method, so sibling methods run without the
        /// excluded one. Grouping is Group-Object's: current-culture case-insensitive keys named by their first member,
        /// emitted in key order, members (wrapped) in input order. { id, classes, exactFilter, nodes } each.
        /// </summary>
        public static object[] ProfileExecutionChunks(object[] discoveryNodes, object[] chunks, object exclusions)
        {
            var exclusionIndex = NightlyPolicy.ExclusionIndex(exclusions);
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            Comparison<string> byCulture = (a, b) => compare.Compare(a, b, CompareOptions.IgnoreCase);
            var leaves = new List<object>();
            var index = 0;
            foreach (var chunk in chunks ?? new object[0])
            {
                var chunkId = PsValue.Str(PsValue.Prop(chunk, "id"));
                var classItems = PsValue.AsArray(PsValue.Prop(chunk, "classes"));
                var required = new string[classItems.Length];
                for (var i = 0; i < classItems.Length; i++) required[i] = classItems[i] == null ? null : PsValue.Str(classItems[i]);
                var buckets = NightlyDiscovery.BucketByBareClass(discoveryNodes ?? new object[0], required, exclusionIndex);
                foreach (var group in GroupObject(buckets, b => PsValue.Str(PsValue.Prop(b, "Bare")), byCulture))
                {
                    var bare = group.Name;
                    var bucket = group.Members[0];
                    var members = PsValue.AsArray(PsValue.Prop(bucket, "Nodes"));
                    var requiredNodes = PsValue.AsArray(PsValue.Prop(bucket, "Required"));
                    if (requiredNodes.Length == 0) continue;
                    if (requiredNodes.Length == members.Length)
                    {
                        leaves.Add(Leaf(chunkId, ++index, bare, "", new object[0]));
                        continue;
                    }
                    // Preserve sibling methods without launching the excluded method. The filter type is the bare
                    // metadata name: TUnit does not put constructor arguments on the treenode path.
                    foreach (var method in GroupObject(requiredNodes, n => PsValue.Str(PsValue.Prop(n, "Method")), byCulture))
                    {
                        var dot = bare.LastIndexOf('.');
                        if (dot < 1) throw new NightlyRefusal("profile-discovery-namespace-required");
                        var node = method.Members[0];
                        var filter = "/*/" + bare.Substring(0, dot) + "/" + bare.Substring(dot + 1) + "/" + PsValue.Str(PsValue.Prop(node, "Method"));
                        leaves.Add(Leaf(chunkId, ++index, bare, filter, method.Members.ToArray()));
                    }
                }
            }
            return leaves.ToArray();
        }

        private static PSObject Leaf(string chunkId, int index, string bare, string exactFilter, object[] nodes)
        {
            var leaf = new PSObject();
            leaf.Properties.Add(new PSNoteProperty("id", chunkId + "-profile-" + index.ToString(CultureInfo.CurrentCulture)));
            leaf.Properties.Add(new PSNoteProperty("classes", new object[] { bare }));
            leaf.Properties.Add(new PSNoteProperty("exactFilter", exactFilter));
            leaf.Properties.Add(new PSNoteProperty("nodes", nodes));
            return leaf;
        }

        private sealed class ObjectGroup
        {
            public string Name;
            public int Position;
            public readonly List<object> Members = new List<object>();
        }

        // Group-Object (PowerShell 7): current-culture case-insensitive keys named by their first member, a stable sort
        // by key, members wrapped in PSObjects in input order.
        private static List<ObjectGroup> GroupObject(IEnumerable items, Func<object, string> key, Comparison<string> byCulture)
        {
            var byKey = new Dictionary<string, ObjectGroup>(StringComparer.Create(CultureInfo.CurrentCulture, true));
            var found = new List<ObjectGroup>();
            foreach (var item in items)
            {
                var k = key(item) ?? "";
                if (!byKey.TryGetValue(k, out var group))
                {
                    group = new ObjectGroup { Name = k, Position = found.Count };
                    byKey.Add(k, group);
                    found.Add(group);
                }
                group.Members.Add(item is PSObject ? item : PSObject.AsPSObject(item));
            }
            var ordered = new List<ObjectGroup>(found);
            ordered.Sort((a, b) =>
            {
                var order = byCulture(a.Name, b.Name);
                return order != 0 ? order : a.Position.CompareTo(b.Position);
            });
            return ordered;
        }

        private static PSObject Result(string error, object[] groups)
        {
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("Error", error));
            o.Properties.Add(new PSNoteProperty("Groups", groups ?? new object[0]));
            return o;
        }

        /// <summary>Get-NightlyLaunchClassCost with the planner's fixed prices.</summary>
        private sealed class CostModel
        {
            public IDictionary Table;
            public double NodeSeconds;
            public double SlowNodeSeconds;
            public double LaneSlowNodeSeconds;
            public double HeavyFactor;

            public (int NodeCount, double Seconds) Of(object[] nodes, string typeName, string bare)
            {
                var cost = NightlyLaunchPlan.ClassCost(nodes, typeName, bare, Table, NodeSeconds, SlowNodeSeconds, LaneSlowNodeSeconds, HeavyFactor);
                return ((int)cost.Properties["NodeCount"].Value, (double)cost.Properties["Seconds"].Value);
            }
        }

        private sealed class ClassItem
        {
            public PSObject Ps;
            public string Bare;
            public string Namespace;
            public string Type;
            public int NodeCount;
            public double Seconds;
        }

        private sealed class Oversized
        {
            public ClassItem Parts;
            public object[] Nodes;
        }

        private sealed class Bin
        {
            public readonly List<ClassItem> Classes = new List<ClassItem>();
            public double Seconds;
        }

        private sealed class MethodGroup
        {
            public string Name;
            public int Position;
            public readonly List<object> Nodes = new List<object>();
        }

        private sealed class MethodItem
        {
            public PSObject Ps;
            public string Method;
            public object[] Nodes;
            public double Seconds;
            public bool Unsafe;
        }

        private sealed class MethodBin
        {
            public readonly List<MethodItem> Methods = new List<MethodItem>();
            public double Seconds;
        }
    }
}
