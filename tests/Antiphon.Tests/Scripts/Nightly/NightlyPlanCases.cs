using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Shouldly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S11: the named rows of a driver case moved in-process (Assert-C487 rows: the same names, PASS or FAIL with
/// the detail), asserted together at the end so one failing row still reports every other row.
/// </summary>
public sealed class NightlyCaseRows
{
    private readonly List<(string Name, bool Ok, string Detail)> _rows = new();

    /// <summary>Assert-C487 -Cond ok -Name name -Detail detail.</summary>
    public void Check(bool ok, string name, string detail = "") => _rows.Add((name, ok, detail ?? ""));

    /// <summary>The rows as the C487 harness printed them.</summary>
    public string Text() => string.Join("\n", _rows.Select(r => (r.Ok ? "PASS " : "FAIL ") + r.Name + (r.Detail.Length == 0 ? "" : " - " + r.Detail)));

    /// <summary>Exactly expected rows (0: any), every one passed, and each required name present.</summary>
    public void ShouldAllPass(int expected = 0, params string[] required)
    {
        var text = Text();
        Console.WriteLine(text);
        if (expected > 0) _rows.Count.ShouldBe(expected, text);
        foreach (var name in required) _rows.Any(r => r.Name == name).ShouldBeTrue("missing row " + name + "\n" + text);
        _rows.Where(r => !r.Ok).Select(r => r.Name).ShouldBeEmpty(text);
    }
}

/// <summary>Plain inputs and PowerShell's readings the moved launch-plan cases share.</summary>
internal static class NightlyPlanCases
{
    /// <summary>The repository root.</summary>
    public static string Repo { get; } = Antiphon.Tests.TestHelpers.C1514Support.RepoRoot();

    /// <summary>scripts/lib/nightly-class-costs.json.</summary>
    public static string CostPath { get; } = Path.Combine(Repo, "scripts", "lib", "nightly-class-costs.json");

    /// <summary>Sort-Object's string order: current culture, case-insensitive.</summary>
    public static readonly StringComparer SortObject = StringComparer.Create(CultureInfo.CurrentCulture, true);

    /// <summary>The shipped policy.</summary>
    public static PolicyFile Policy() => NightlyPolicy.Read(Repo, "");

    /// <summary>A profile's exclusion rows for one suite and platform.</summary>
    public static object[] Exclusions(PolicyFile policy, string profile, string suite, string platform) =>
        NightlyPolicy.ProfileExclusions(policy.Object, profile, suite, platform).Cast<object>().ToArray();

    /// <summary>The production cost file, read at its first use.</summary>
    public static NightlyClassCosts Costs() => new(CostPath);

    /// <summary>[pscustomobject]@{ id = 'all'; classes = @() } as the one declared chunk.</summary>
    public static object[] AllChunk() => [new PlainObject(("id", "all"), ("classes", Array.Empty<object>()))];

    /// <summary>A discovery node ({ ClassName, Type, Method, Uid, Categories }).</summary>
    public static PlainObject Node(string className, string type, string method, string uid, params string[] categories) =>
        new(("ClassName", className), ("Type", type), ("Method", method), ("Uid", uid), ("Categories", categories.Cast<object>().ToArray()));

    /// <summary>[string]$o.name.</summary>
    public static string Str(object? o, string name) => PsValue.Str(PsValue.Prop(o, name));

    /// <summary>[double]$o.name.</summary>
    public static double Num(object? o, string name) => PsValue.Double(PsValue.Prop(o, name));

    /// <summary>@($o.name) as strings.</summary>
    public static string[] Strings(object? o, string name) => PsValue.AsArray(PsValue.Prop(o, name)).Select(PsValue.Str).ToArray();

    /// <summary>A launch's classes, or none when it names no class.</summary>
    public static string[] Classes(object launch) => PsValue.Prop(launch, "classes") == null ? [] : Strings(launch, "classes");

    /// <summary>The native argv of a launch (its exact, group and method filters).</summary>
    public static string[] Argv(object launch, string trxPath, string diagnosticDirectory) =>
        NightlyTestsPlan.NativeExecutionArguments(trxPath, diagnosticDirectory, Classes(launch), Str(launch, "exactFilter"),
            Str(launch, "groupFilter"), Str(launch, "methodFilter"));

    /// <summary>The --treenode-filter value of an argv, or null.</summary>
    public static string? Filter(string[] argv)
    {
        var at = Array.IndexOf(argv, "--treenode-filter");
        return at < 0 || at + 1 >= argv.Length ? null : argv[at + 1];
    }

    /// <summary>The refusal text of an action, or null when it is accepted.</summary>
    public static string? Refusal(Action action)
    {
        try { action(); }
        catch (NightlyRefusal ex) { return ex.Message; }
        return null;
    }

    /// <summary>$value -clike $pattern (PowerShell wildcards: * and ?, case-sensitive).</summary>
    public static bool Like(string value, string pattern) =>
        Regex.IsMatch(value ?? "", "^" + Regex.Escape(pattern ?? "").Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>Same-C1323Set: equal as sorted ordinal bags.</summary>
    public static bool SameSet(IEnumerable<string> actual, IEnumerable<string> expected) =>
        actual.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(s => s, StringComparer.Ordinal), StringComparer.Ordinal);

    /// <summary>The class rows of the production cost file.</summary>
    public static Dictionary<string, JsonElement> CostRows()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(CostPath));
        var rows = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var row in doc.RootElement.GetProperty("classes").EnumerateObject()) rows[row.Name] = row.Value.Clone();
        return rows;
    }

    /// <summary>[int]$row.Value.name, [double]$row.Value.name and [string]$row.Value.name of a cost row (absent: 0 or empty).</summary>
    public static int RowInt(JsonElement row, string name) => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble(), MidpointRounding.ToEven) : 0;

    public static double RowDouble(JsonElement row, string name) => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    public static string RowText(JsonElement row, string name) => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    public static bool RowHas(JsonElement row, string name) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out _);

    /// <summary>A Hashtable as @{} builds it (case-insensitive keys).</summary>
    public static Hashtable Table() => new(StringComparer.OrdinalIgnoreCase);
}
