using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyPlanCases;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S4a, CARD-1873 and CARD-1878: the boundary-aware launch pack (NightlyTestsPlan.BoundaryLaunchGroups and
/// CompleteBoundaryLaunchGroups) in process. These cases replaced scripts/test-rc-boundary-pack.ps1 and its fixture
/// scripts/fixtures/rc-boundary-pack-golden/serializer-order.ps1 (plan 2026-11-22-remaining-ps1-to-csharp.md, unit 16).
/// Each case keeps the driver's name and checks; a row is named by the driver's failure text. Every fixture pack is
/// also compared with its CARD-1873 golden in scripts/fixtures/rc-boundary-pack-golden (the pack's
/// <c>ConvertTo-Json -Depth 30 -Compress</c> text, fields in a fixed order, written here by <see cref="PsJson"/>), and
/// builds the boundary discovery index exactly once. The serializer case keeps the C# side of the pwsh 7
/// ConvertTo-Json pin: the bytes ConvertTo-Json wrote, as literals and digests, with PsJson as the per-code-unit oracle.
/// The pack's index counter is process-wide, so the cases run one at a time.
/// </summary>
[Category("Unit")]
[NotInParallel(nameof(RcBoundaryPackTests))]
public sealed class RcBoundaryPackTests
{
    private const string Ns = "Antiphon.Tests.Fixture";
    private static readonly string GoldenDirectory = Path.Combine(Repo, "scripts", "fixtures", "rc-boundary-pack-golden");

    [Test]
    public void TwoBoundariesAndPrefixCollisionsStaySeparate()
    {
        var rows = new NightlyCaseRows();
        object[] nodes = [FixtureNode("A"), FixtureNode("AExtra"), FixtureNode("B"), FixtureNode("C"), FixtureNode("Unknown")];
        var map = Map(("A", "core"), ("B", "core"), ("AExtra", "git"), ("C", "git"));
        var plan = Packed(rows, "separation", nodes, map);
        rows.Check(plan.Length == 4, "boundaries/prefix-unsafe classes were not separated", "chunks " + plan.Length);
        foreach (var chunk in plan)
        {
            var id = Str(chunk, "id");
            var expected = Classes(chunk).Select(c => map.Contains(c) ? (string)map[c]! : "unassigned").Distinct(StringComparer.Ordinal).ToArray();
            rows.Check(expected.Length == 1 && Str(chunk, "boundary") == expected[0], "mixed or missing boundary", id);
            rows.Check(Str(chunk, "suite") == "antiphon", "suite lost", id);
            rows.Check(Regex.IsMatch(id, "^b-[a-z-]+-[0-9a-f]{16}$") && Regex.IsMatch(Str(chunk, "membershipDigest"), "^[0-9a-f]{64}$"),
                "membership hash absent", id);
            rows.Check(PsValue.IsTrue(PsValue.Prop(chunk, "alwaysDirty")) == expected.Contains("unassigned"), "unassigned is carryable", id);
        }
        rows.Check(SameSet(Selected(plan, nodes), Uids(nodes)), "filters duplicate or omit a prefix sibling");
        var other = Packed(rows, "separation-scripts", nodes, map, suite: "scripts");
        rows.Check(!Ids(plan).Intersect(Ids(other), StringComparer.Ordinal).Any(), "suite aliases chunk IDs");
        Golden(rows, "separation.json", plan);
        Golden(rows, "separation-scripts.json", other);
        rows.ShouldAllPass();
    }

    [Test]
    public void RepackPreservesCaseUniverse()
    {
        var rows = new NightlyCaseRows();
        object[] nodes =
        [
            FixtureNode("Matrix", "Run", 0), FixtureNode("Matrix", "Run", 1), FixtureNode("Matrix", "RunExtra", 0), FixtureNode("Matrix", "Other", 0),
            FixtureNode("Matrix", "Other", 1), FixtureNode("Matrix", "Last", 0), FixtureNode("Z"),
        ];
        var map = Map(("Matrix", "core"), ("Z", "core"));
        var uids = Uids(nodes);
        // The launch target and lone-class budget shrunk so Matrix splits into exact-method chunks.
        var small = new NightlyLaunchPrices(1, 2);
        var plan = Packed(rows, "repack-split", nodes, map, prices: small);
        rows.Check(plan.Length >= 4, "oversized class did not split", "chunks " + plan.Length);
        rows.Check(SameSet(Selected(plan, nodes), uids), "split filter case universe changed");
        rows.Check(SameSet(plan.SelectMany(CaseUids), uids), "case manifest incomplete");
        rows.Check(SameSet(plan.SelectMany(c => Texts(c, "caseKeys")), uids), "canonical case keys incomplete or duplicated");
        rows.Check(SameSet(plan.SelectMany(c => Texts(c, "identityKeys")).Distinct(StringComparer.Ordinal),
            [Ns + ".Matrix/Run", Ns + ".Matrix/RunExtra", Ns + ".Matrix/Other", Ns + ".Matrix/Last", Ns + ".Z/Run"]), "method manifest incomplete");
        var reversed = Enumerable.Reverse(nodes).ToArray();
        rows.Check(SameSet(Ids(plan), Ids(Packed(rows, "repack-reversed", reversed, map, prices: small))), "discovery order changed IDs");
        var repacked = Packed(rows, "repack-restored", nodes, map);
        rows.Check(SameSet(Selected(repacked, nodes), uids), "repack changed case universe");
        rows.Check(SameSet(repacked.SelectMany(CaseUids), uids), "repack lost case evidence");
        Refused(rows, "duplicate discovery UID accepted", "boundary-pack: duplicate discovery UID", () => Pack([.. nodes, nodes[0]], map));
        var leaves = Leaves(nodes);
        Refused(rows, "overlapping leaves accepted", "boundary-pack: overlapping leaves", () => Pack(nodes, map, [.. leaves, leaves[0]]));
        var exact = ExactLeaf(nodes.Where(n => Str(n, "Method") == "Run" && Str(n, "Type") == "Matrix"));
        var partial = Packed(rows, "repack-exact", nodes, map, [exact]);
        rows.Check(SameSet(Selected(partial, nodes), ["Matrix/Run/0", "Matrix/Run/1"]), "exact leaf launches excluded sibling");
        rows.Check(SameSet(partial.SelectMany(CaseUids), ["Matrix/Run/0", "Matrix/Run/1"]), "exact leaf manifest includes excluded sibling");
        object[] constructors = [FixtureNode("Matrix(\"a\")", "Run", 0), FixtureNode("Matrix(\"b\")", "Run", 0)];
        var ctorRefusal = Refusal(() => Pack(constructors, map, [ExactLeaf(constructors)]));
        rows.Check(ctorRefusal == null, "constructor exact leaf refused", ctorRefusal ?? "");
        if (ctorRefusal == null)
        {
            var ctorPlan = Packed(rows, "repack-ctor", constructors, map, [ExactLeaf(constructors)]);
            rows.Check(SameSet(Selected(ctorPlan, constructors), Uids(constructors)), "constructor filter lost cases");
            rows.Check(SameSet(ctorPlan.SelectMany(c => Items(c, "caseMembership")).Select(m => Str(m, "className")), constructors.Select(n => Str(n, "ClassName"))),
                "constructor identities collapsed");
            rows.Check(SameSet(ctorPlan.SelectMany(c => Texts(c, "identityKeys")), [Ns + ".Matrix/Run", Ns + ".Matrix/Run"]),
                "constructor canonical identity pairs changed");
            rows.Check(SameSet(ctorPlan.SelectMany(c => Texts(c, "caseKeys")), Uids(constructors)), "constructor canonical case keys collapsed");
            Golden(rows, "repack-ctor.json", ctorPlan);
        }
        Refused(rows, "case-colliding classes accepted", "boundary-pack: case-colliding class identity",
            () => Pack([FixtureNode("Upper", "Run", 0), FixtureNode("upper", "Other", 0)], Map()));
        Golden(rows, "repack-split.json", plan);
        Golden(rows, "repack-restored.json", repacked);
        Golden(rows, "repack-exact.json", partial);
        rows.ShouldAllPass();
    }

    [Test]
    public void DefaultFullPackKeepsIds()
    {
        var rows = new NightlyCaseRows();
        object[] nodes = [FixtureNode("B"), FixtureNode("C")];
        var before = NightlyLaunchPlan.BoundaryIndexBuilds;
        var plan = NightlyTestsPlan.ProfileLaunchGroups(Leaves(nodes), nodes, null, Costs(), null);
        rows.Check(NightlyLaunchPlan.BoundaryIndexBuilds == before, "Full built a boundary index");
        var one = plan.Length == 1;
        rows.Check(one && Str(plan[0], "id") == "all-group-1", "Full ID changed", "chunks " + plan.Length);
        rows.Check(one && Str(plan[0], "groupFilter") == "/*/Antiphon.Tests.Fixture/(B*)|(C*)/*", "Full filter changed");
        rows.Check(one && plan[0] is PlainObject full && !full.Names.Contains("boundary", StringComparer.Ordinal), "Full opted into boundary metadata");
        rows.Check(one && Num(plan[0], "nodeCount") == 2 && Num(plan[0], "seconds") == 1, "Full estimate changed");
        // The new path must be opt-in and produce an actual different result.
        var opt = Packed(rows, "default-on", nodes, Map(("B", "core"), ("C", "git")));
        rows.Check(opt.Length == 2 && Ids(opt).Count(id => id.StartsWith("b-", StringComparison.Ordinal)) == 2, "opt-in ignored");
        Golden(rows, "default-off.json", plan);
        Golden(rows, "default-on.json", opt);
        rows.ShouldAllPass();
    }

    [Test]
    public void CanonicalDigestIncludesCaseKeys()
    {
        var rows = new NightlyCaseRows();
        object[] nodes = [FixtureNode("Matrix", "Run", 0), FixtureNode("Matrix", "Run", 1)];
        var map = Map(("Matrix", "core"));
        var plan = Packed(rows, "canonical", nodes, map);
        // Pinned length-delimited vector, including both expanded case UIDs.
        rows.Check(plan.Length == 1 && Str(plan[0], "id") == "b-core-c3d9377ff31dfd63", "packer did not use canonical case membership",
            string.Join(",", Ids(plan)));
        rows.Check(SameSet(plan.Take(1).SelectMany(c => Texts(c, "caseKeys")), Uids(nodes)), "S2 did not receive case keys");
        object[] changed = [FixtureNode("Matrix", "Run", 0), FixtureNode("Matrix", "Run", 2)];
        var changedPlan = Packed(rows, "canonical-changed", changed, map);
        rows.Check(plan.Length > 0 && changedPlan.Length > 0 && Str(plan[0], "id") != Str(changedPlan[0], "id"), "changed argument case reused the ID");
        rows.Check(SameSet(Selected(plan, nodes), Uids(nodes)), "canonical packing changed selection");
        Golden(rows, "canonical.json", plan);
        Golden(rows, "canonical-changed.json", changedPlan);
        rows.ShouldAllPass();
    }

    [Test]
    public void ShortIdCollisionUsesS2Policy()
    {
        // Real S2 64-bit collision through the finalizer used by the packer. Already partitioned launch descriptors
        // witness metadata and case custody; they do not claim that a method filter selects individual argument cases.
        var rows = new NightlyCaseRows();
        string[] keys = ["10788987499786589518", "17490688019260441962"];
        var chunks = keys.Select(key => (object)new PlainObject(("id", "pending"), ("nodes", new object[] { new PlainObject(("Uid", key)) }), ("marker", key))).ToArray();
        var memberships = keys.Select(key => (object)Table(("suite", "antiphon"), ("boundary", "core"),
            ("identities", new object[] { Table(("identityKey", "CTests/M"), ("caseKey", key)) }))).ToArray();
        rows.Check(RcBoundary.ChunkId("antiphon", "core", ["CTests/M"], [keys[0]]) == "b-core-360a4ef28b10a96c", "collision fixture moved");
        var plan = NightlyTestsPlan.CompleteBoundaryLaunchGroups(chunks, memberships);
        rows.Check(plan.Length == 2, "collision dropped a launch", "chunks " + plan.Length);
        foreach (var chunk in plan)
        {
            var id = Str(chunk, "id");
            var digest = Str(chunk, "membershipDigest");
            rows.Check(Str(chunk, "boundary") == "unassigned" && PsValue.Prop(chunk, "alwaysDirty") is true && PsValue.Prop(chunk, "collision") is true,
                "collision was not dirty/unassigned", id);
            rows.Check(id == "b-unassigned-" + digest && digest.Length == 64, "collision lost full digest ID", id);
            rows.Check(Texts(chunk, "caseKeys").FirstOrDefault() == Str(chunk, "marker"), "resolution reordered case ownership", id);
        }
        rows.Check(plan.Length == 2 && Str(plan[0], "id") != Str(plan[1], "id"), "collision IDs collapsed");
        rows.Check(SameSet(plan.SelectMany(c => Items(c, "nodes")).Select(n => Str(n, "Uid")), keys), "collision omitted or duplicated cases");
        Golden(rows, "collision.json", plan);
        rows.ShouldAllPass();
    }

    // ---- CARD-1873: the case-membership order is pwsh 7 ConvertTo-Json's ----------------------------------------------
    // Newtonsoft.Json 13 StringEscapeHandling.Default (the serializer behind pwsh 7 ConvertTo-Json -Compress) escapes
    // quote, backslash and C0 (\b \t \n \f \r, otherwise \u00xx), plus U+0085, U+2028 and U+2029. U+007F and every other
    // C1 control stay literal; so do slash and Windows PowerShell 5.1's HTML escapes (& ' < >). The old packer sorted a
    // chunk's cases by that text of [className, method, uid]; NightlyLaunchPlan.CaseSortKey is it in C#. The fixture
    // compared CaseSortKey with ConvertTo-Json in process; the ConvertTo-Json half is the captured bytes below.

    /// <summary>
    /// ConvertTo-Json -Compress -InputObject @($s, $s, $s) for every UTF-16 code unit s (U+0000..U+FFFF), joined by LF:
    /// its length and the SHA-256 of its UTF-16LE code units (pwsh 7.6.10, 2026-11-23).
    /// </summary>
    private const int CodeUnitKeysLength = 917974;
    private const string CodeUnitKeysSha256 = "a4aeeae87b67a25c31e918db7a130032fc6966851d56a3c950013c7410b5d81d";

    /// <summary>The same for the samples in the class, method and uid positions (84 keys, sample order).</summary>
    private const string SampleKeysSha256 = "320e6744953620e55c28b42ae07a66f6fc8ddd89891262a4710a94636e09605b";

    /// <summary>The sample nodes' uids in ConvertTo-Json key order (ordinal), as the fixture computed it.</summary>
    private const string SampleUidOrder = "29,21,22,20,23,24,18,19,8,13,11,14,9,6,7,12,15,1,3,4,2,27,5,16,17,26,28,25";

    private static readonly string Emoji = char.ConvertFromUtf32(0x1F600);

    private static readonly (string Name, string Text, string Uid)[] Samples =
    [
        ("nel", "x\u0085", "1"), ("xa", "xa", "2"), ("line", "x\u2028", "3"), ("para", "x\u2029", "4"), ("del", "x\u007F", "5"),
        ("nul", "x\u0000", "6"), ("soh", "x\u0001", "7"), ("bs", "x\u0008", "8"), ("tab", "x\u0009", "9"), ("lf", "x\u000A", "11"),
        ("vt", "x\u000B", "12"), ("ff", "x\u000C", "13"), ("cr", "x\u000D", "14"), ("us", "x\u001F", "15"), ("c1", "x\u0080", "16"),
        ("c1end", "x\u009F", "17"), ("quote", "x\"y", "18"), ("backslash", "x\\y", "19"), ("slash", "x/y", "20"), ("amp", "x&y", "21"),
        ("apos", "x'y", "22"), ("lt", "x<y", "23"), ("gt", "x>y", "24"), ("emoji", "x" + Emoji, "25"), ("lone-high", "x\uD800a", "26"),
        ("lone-low", "xa\uDC00", "27"), ("two-high", "x\uD800\uD800", "28"), ("pair-lone", "a" + Emoji + "\uD800", "29"),
    ];

    /// <summary>What ConvertTo-Json wrote for one string (the fixture's oracle pins).</summary>
    private static readonly (string Name, string Value, string Json)[] EscapePins =
    [
        ("nel-escaped", "x\u0085", "\"x\\u0085\""),
        ("del-literal", "x\u007F", "\"x\u007F\""),
        ("c1-literal", "x\u0080", "\"x\u0080\""),
        ("line-escaped", "\u2028", "\"\\u2028\""),
        ("para-escaped", "\u2029", "\"\\u2029\""),
        ("bs-short", "\u0008", "\"\\b\""),
        ("vt-unicode", "\u000B", "\"\\u000b\""),
        ("slash-literal", "a/b", "\"a/b\""),
        ("html-literal", "&'<>", "\"&'<>\""),
        ("emoji-raw", Emoji, "\"" + Emoji + "\""),
        ("lone-raw", "A\uD800", "\"A\uD800\""),
    ];

    [Test]
    public void SerializerEquivalentConstructorOrder()
    {
        var rows = new NightlyCaseRows();
        foreach (var (name, value, json) in EscapePins)
        {
            rows.Check(NightlyLaunchPlan.CaseSortKey(value, value, value) == "[" + json + "," + json + "," + json + "]", "ConvertTo-Json pin " + name);
            rows.Check(PsJson.Compress(new object[] { value }) == "[" + json + "]", "PsJson pin " + name);
        }
        rows.Check(string.CompareOrdinal(NightlyLaunchPlan.CaseSortKey(Ns + ".Matrix", "Run", "10"), NightlyLaunchPlan.CaseSortKey(Ns + ".Matrix", "Run", "2")) < 0,
            "uid-string-order");

        // The encoder decides each UTF-16 code unit alone, so every code unit matching makes every string match.
        var table = new StringBuilder(CodeUnitKeysLength);
        var misses = new List<string>();
        for (var i = 0; i <= 0xFFFF; i++)
        {
            var s = ((char)i).ToString();
            var key = NightlyLaunchPlan.CaseSortKey(s, s, s);
            if (misses.Count < 8 && key != PsJson.Compress(new object[] { s, s, s })) misses.Add(i.ToString("x4", CultureInfo.InvariantCulture));
            if (i > 0) table.Append('\n');
            table.Append(key);
        }
        rows.Check(misses.Count == 0, "code unit sort key diverges", string.Join(",", misses));
        var tableText = table.ToString();
        rows.Check(tableText.Length == CodeUnitKeysLength && Sha256(tableText) == CodeUnitKeysSha256, "code unit sort keys are not ConvertTo-Json's bytes",
            tableText.Length + " " + Sha256(tableText));

        var sampleKeys = new List<string>();
        var keyMisses = new List<string>();
        foreach (var (name, text, _) in Samples)
        {
            foreach (var (label, className, method, uid) in new[]
            {
                ("class:" + name, Ns + ".Matrix(\"" + text + "\")", "Run", "1"),
                ("method:" + name, Ns + ".Matrix", text, "1"),
                ("uid:" + name, Ns + ".Matrix", "Run", text),
            })
            {
                var key = NightlyLaunchPlan.CaseSortKey(className, method, uid);
                if (key != PsJson.Compress(new object[] { className, method, uid })) keyMisses.Add(label);
                sampleKeys.Add(key);
            }
        }
        rows.Check(keyMisses.Count == 0, "sort key is not ConvertTo-Json", string.Join(",", keyMisses));
        rows.Check(Sha256(string.Join("\n", sampleKeys)) == SampleKeysSha256, "sample sort keys are not ConvertTo-Json's bytes");

        object[] nodes = Samples.Select(sample => (object)FixtureNode("Matrix(\"" + sample.Text + "\")", "Run", sample.Uid)).ToArray();
        var map = Map(("Matrix", "core"));
        var plan = Packed(rows, "serializer-order", nodes, map, [ExactLeaf(nodes)]);
        rows.Check(plan.Length == 1 && Str(plan[0], "boundary") == "core", "serializer-order constructors did not stay one core chunk", "chunks " + plan.Length);
        var actual = plan.Take(1).SelectMany(CaseUids).ToArray();
        var expected = nodes.OrderBy(n => PsJson.Compress(new object[] { Str(n, "ClassName"), Str(n, "Method"), Str(n, "Uid") }), StringComparer.Ordinal)
            .Select(n => Str(n, "Uid")).ToArray();
        rows.Check(string.Join(",", actual) == string.Join(",", expected), "membership order", string.Join(",", actual) + " != " + string.Join(",", expected));
        rows.Check(string.Join(",", expected) == SampleUidOrder, "ConvertTo-Json uid order moved", string.Join(",", expected));
        var nel = Array.IndexOf(actual, "1");
        var xa = Array.IndexOf(actual, "2");
        rows.Check(nel >= 0 && xa >= 0 && nel < xa, "U+0085 uid 1 sorted after xa uid 2", "(" + nel + "," + xa + ")");
        rows.Check(string.Join("|", plan.Take(1).SelectMany(c => Texts(c, "identityKeys")).Distinct(StringComparer.Ordinal)) == Ns + ".Matrix/Run",
            "constructor identity was not the bare class");

        object[] numeric = [FixtureNode("Matrix", "Run", "10"), FixtureNode("Matrix", "Run", "2")];
        var numericPlan = Packed(rows, "numeric-uid", numeric, map, [ExactLeaf(numeric)]);
        var numericActual = string.Join(",", numericPlan.Take(1).SelectMany(CaseUids));
        rows.Check(numericActual == "10,2", "uid string order lost", numericActual);
        rows.ShouldAllPass();
    }

    // ---- fixture ----------------------------------------------------------------------------------------------------

    /// <summary>A discovery node of Antiphon.Tests.Fixture.TYPE, uid TYPE/METHOD/ROW.</summary>
    private static PlainObject FixtureNode(string type, string method = "Run", int row = 0) =>
        FixtureNode(type, method, type + "/" + method + "/" + row.ToString(CultureInfo.InvariantCulture));

    private static PlainObject FixtureNode(string type, string method, string uid) =>
        new(("ClassName", Ns + "." + type), ("Namespace", Ns), ("Type", type), ("Method", method), ("Uid", uid),
            ("Categories", Array.Empty<object>()), ("Excluded", false));

    /// <summary>One whole-class leaf per bare class, in first-seen order (Group-Object, case-insensitive).</summary>
    private static object[] Leaves(IEnumerable<object> nodes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var leaves = new List<object>();
        foreach (var node in nodes)
        {
            var bare = NightlyCensus.BareClassIdentity(Str(node, "ClassName"));
            if (seen.Add(bare))
                leaves.Add(new PlainObject(("id", "all-profile-1"), ("classes", new object[] { bare }), ("exactFilter", ""), ("nodes", Array.Empty<object>())));
        }
        return leaves.ToArray();
    }

    /// <summary>The exact-method leaf of Matrix.Run with its declared cases.</summary>
    private static PlainObject ExactLeaf(IEnumerable<object> nodes) =>
        new(("id", "all-profile-1"), ("classes", new object[] { Ns + ".Matrix" }), ("exactFilter", "/*/" + Ns + "/Matrix/Run"), ("nodes", nodes.ToArray()));

    /// <summary>A class-to-boundary map (@{}: case-insensitive keys).</summary>
    private static Hashtable Map(params (string Type, string Boundary)[] rows)
    {
        var map = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var (type, boundary) in rows) map.Add(Ns + "." + type, boundary);
        return map;
    }

    private static Hashtable Table(params (string Key, object Value)[] entries)
    {
        var table = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries) table.Add(key, value);
        return table;
    }

    private static object[] Pack(object[] nodes, IDictionary map, object[]? leaves = null, string suite = "antiphon", NightlyLaunchPrices? prices = null) =>
        NightlyTestsPlan.BoundaryLaunchGroups(leaves ?? Leaves(nodes), nodes, suite, map, Costs(), prices ?? NightlyLaunchPrices.Default);

    /// <summary>A pack that must be accepted and index the discovery once.</summary>
    private static object[] Packed(NightlyCaseRows rows, string label, object[] nodes, IDictionary map, object[]? leaves = null, string suite = "antiphon",
        NightlyLaunchPrices? prices = null)
    {
        var before = NightlyLaunchPlan.BoundaryIndexBuilds;
        var plan = Pack(nodes, map, leaves, suite, prices);
        var built = NightlyLaunchPlan.BoundaryIndexBuilds - before;
        rows.Check(built == 1, label + " indexed once", "built " + built);
        return plan;
    }

    private static void Refused(NightlyCaseRows rows, string name, string expected, Action action)
    {
        var refusal = Refusal(action);
        rows.Check(refusal == expected, name, refusal ?? "accepted");
    }

    /// <summary>@($o.name) with no value as no items.</summary>
    private static object[] Items(object chunk, string name) => PsValue.Prop(chunk, name) is { } value ? PsValue.AsArray(value) : [];

    private static string[] Texts(object chunk, string name) => Items(chunk, name).Select(PsValue.Str).ToArray();

    private static string[] Uids(IEnumerable<object> nodes) => nodes.Select(n => Str(n, "Uid")).ToArray();

    private static string[] Ids(IEnumerable<object> plan) => plan.Select(c => Str(c, "id")).ToArray();

    private static IEnumerable<string> CaseUids(object chunk) => Items(chunk, "caseMembership").Select(m => Str(m, "uid"));

    /// <summary>The uids a chunk's filter would launch: exact, method, group, else its class list.</summary>
    private static List<string> Selected(object[] plan, object[] nodes)
    {
        var hits = new List<string>();
        foreach (var chunk in plan)
        {
            var exact = Str(chunk, "exactFilter");
            var methodFilter = Str(chunk, "methodFilter");
            var groupFilter = Str(chunk, "groupFilter");
            var classes = Classes(chunk);
            foreach (var node in nodes)
            {
                var ns = Str(node, "Namespace");
                var type = Str(node, "Type").Split('(', 2)[0];
                var method = Str(node, "Method");
                bool hit;
                if (exact.Length > 0) hit = "/*/" + ns + "/" + type + "/" + method == exact;
                else if (methodFilter.Length > 0)
                {
                    var parts = methodFilter.Split('/');
                    hit = ns == parts[2] && type == parts[3] && Regex.IsMatch(method, "^(?:" + parts[4].Replace("*", ".*") + ")$");
                }
                else if (groupFilter.Length > 0)
                {
                    var parts = groupFilter.Split('/');
                    hit = ns == parts[2] && Regex.IsMatch(type, "^(?:" + parts[3].Replace("*", ".*") + ")$");
                }
                else hit = classes.Contains(ns + "." + type, StringComparer.Ordinal);
                if (hit) hits.Add(Str(node, "Uid"));
            }
        }
        return hits;
    }

    // ---- the CARD-1873 goldens ----------------------------------------------------------------------------------------

    private static readonly string[] PackCanonicalFields =
    [
        "id", "classes", "exactFilter", "groupFilter", "methodFilter", "nodes", "nodeCount", "seconds", "caseMembership", "caseIdentityBasis",
        "suite", "boundary", "membershipDigest", "identityKeys", "caseKeys", "alwaysDirty", "collision",
    ];

    /// <summary>
    /// The golden text of a plan: each chunk's fields in the canonical order (caseMembership as className, method, uid),
    /// then any other field by name, as <c>ConvertTo-Json -InputObject @($chunks) -Depth 30 -Compress</c>.
    /// </summary>
    private static string PackJson(object[] plan)
    {
        var chunks = new List<object>();
        foreach (var chunk in plan)
        {
            var source = (PlainObject)chunk;
            var present = source.Names.ToList();
            var ordered = new PlainObject();
            foreach (var name in PackCanonicalFields)
            {
                if (!present.Contains(name, StringComparer.Ordinal)) continue;
                ordered.Add(name, name == "caseMembership"
                    ? Items(source, name).Select(c => (object)new PlainObject(("className", Str(c, "className")), ("method", Str(c, "method")), ("uid", Str(c, "uid")))).ToArray()
                    : source[name]);
            }
            foreach (var name in present.Where(n => !PackCanonicalFields.Contains(n, StringComparer.Ordinal)).OrderBy(n => n, SortObject))
                ordered.Add(name, source[name]);
            chunks.Add(ordered);
        }
        return PsJson.Compress(chunks.ToArray(), 30);
    }

    private static void Golden(NightlyCaseRows rows, string name, object[] plan)
    {
        var expected = File.ReadAllText(Path.Combine(GoldenDirectory, name), new UTF8Encoding(false));
        var actual = PackJson(plan);
        rows.Check(string.Equals(actual, expected, StringComparison.Ordinal), "golden " + name, actual == expected ? "" : "actual " + actual);
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(text.AsSpan())));
}
