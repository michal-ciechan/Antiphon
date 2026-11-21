using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// What NightlyCoreGoldenTests compares with: the goldens in scripts/fixtures/nightly/core-golden, which are the text
/// PowerShell 7's <c>ConvertTo-Json -Depth 14 -Compress</c> wrote for each area's result (a golden over 64 KB is kept
/// as its SHA-256 and length). <see cref="PsJson"/> writes that text for the plain values the core returns to a plain
/// caller (PlainObject where PowerShell got a PSObject), so the goldens are compared unchanged.
/// </summary>
internal static class NightlyCoreGolden
{
    public static string Repo { get; } = C1514Support.RepoRoot();

    public static string GoldenPath(string name) => Path.Combine(Repo, "scripts", "fixtures", "nightly", "core-golden", name + ".json");

    public static string Read(string name) => File.ReadAllText(GoldenPath(name), Encoding.UTF8);

    public static string Sha256Text(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// The golden comparison: <paramref name="actual"/> (the JSON text) equals the golden text, or, for a digest golden,
    /// has its SHA-256 and length. Each witness must appear in the golden text.
    /// </summary>
    public static (bool Ok, string Detail) Compare(string name, string actual, params string[] witnesses)
    {
        var expected = Read(name);
        var missing = witnesses.Where(w => !expected.Contains(w, StringComparison.Ordinal)).ToArray();
        bool same;
        if (expected.StartsWith("sha256:", StringComparison.Ordinal))
            same = string.Equals(expected, "sha256:" + Sha256Text(actual) + " chars:" + actual.Length, StringComparison.Ordinal);
        else same = string.Equals(expected, actual, StringComparison.Ordinal);
        var detail = $"{name}: " + (same ? "same" : "differs") + (missing.Length > 0 ? "; golden lacks " + string.Join(", ", missing) : "")
            + $"\nactual   ({actual.Length}) {Head(actual)}\nexpected ({expected.Length}) {Head(expected)}" + (same ? "" : "\n" + FirstDifference(actual, expected));
        return (same && missing.Length == 0, detail);
    }

    private static string Head(string text) => text.Length <= 400 ? text : text[..400] + "...";

    private static string FirstDifference(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        var from = Math.Max(0, i - 80);
        return $"first difference at {i}: actual ...{a.Substring(from, Math.Min(160, a.Length - from))} | expected ...{(b.StartsWith("sha256:", StringComparison.Ordinal) ? "(digest)" : b.Substring(from, Math.Min(160, Math.Max(0, b.Length - from))))}";
    }

    /// <summary>A run-specific directory written as &lt;root&gt; (the JSON-escaped form first), as the goldens hold it.</summary>
    public static string Normalize(string text, string root) =>
        text.Replace(root.Replace("\\", "\\\\"), "<root>", StringComparison.Ordinal).Replace(root, "<root>", StringComparison.Ordinal);

    /// <summary>
    /// Get-NCFixtureNodes: discovery nodes of scripts/fixtures/nightly/c1410-antiphon-listing.txt, as the
    /// [pscustomobject]s the driver built ({ ClassName, Namespace, Type, Method, Uid, Categories, Excluded }).
    /// </summary>
    public static List<PlainObject> FixtureNodes(int take)
    {
        var nodes = new List<PlainObject>();
        foreach (var line in File.ReadAllLines(Path.Combine(Repo, "scripts", "fixtures", "nightly", "c1410-antiphon-listing.txt")))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var fields = line.Split('|');
            var i = 0;
            foreach (var entry in fields[2].Split(','))
            {
                var eq = entry.IndexOf('=');
                string method;
                int count;
                if (eq >= 0) { method = entry[..eq]; count = int.Parse(entry[(eq + 1)..], CultureInfo.InvariantCulture); }
                else { method = "M" + i.ToString("0000", CultureInfo.InvariantCulture); count = int.Parse(entry, CultureInfo.InvariantCulture); }
                i++;
                for (var k = 0; k < count; k++)
                {
                    var cls = fields[0];
                    var bare = NightlyCensus.BareClassIdentity(cls);
                    var dot = bare.LastIndexOf('.');
                    object[] categories = fields[1] == "S" ? ["Slow"] : [];
                    nodes.Add(new PlainObject(("ClassName", cls), ("Namespace", bare[..dot]), ("Type", cls[(dot + 1)..]), ("Method", method),
                        ("Uid", cls + "." + method + "." + k.ToString(CultureInfo.InvariantCulture)), ("Categories", categories), ("Excluded", false)));
                    if (nodes.Count >= take) return nodes;
                }
            }
        }
        return nodes;
    }

    /// <summary>A fresh owned results directory under the temp directory (the C487 root the driver wrote into).</summary>
    public static string NewRoot() => OwnedTempRoot.CreateOwned();

    /// <summary>Releases a results directory, clearing the read-only and hidden attributes a case set.</summary>
    public static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true };
            foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", options))
            {
                try { if ((entry.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden)) != 0) entry.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.Hidden); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        OwnedTempRoot.Release(root);
    }

    /// <summary>[ordered]@{} (OrderedDictionary, case-insensitive keys).</summary>
    public static System.Collections.Specialized.OrderedDictionary Ordered(params (string Key, object? Value)[] entries)
    {
        var d = new System.Collections.Specialized.OrderedDictionary(StringComparer.CurrentCultureIgnoreCase);
        foreach (var (key, value) in entries) d.Add(key, value);
        return d;
    }

    /// <summary>@{} (Hashtable, case-insensitive keys).</summary>
    public static Hashtable Table(params (object Key, object? Value)[] entries)
    {
        var h = new Hashtable(StringComparer.CurrentCultureIgnoreCase);
        foreach (var (key, value) in entries) h.Add(key, value);
        return h;
    }

    public static PlainObject Obj(params (string Name, object? Value)[] entries) => new(entries.Select(e => (e.Name, (object)e.Value!)).ToArray());
}

/// <summary>
/// PowerShell 7's ConvertTo-Json for plain values: JsonObject.ProcessValue (a PlainObject is a PSCustomObject, its
/// entries the note properties) and Newtonsoft's writer (escaping, number and date formats, two-space indenting).
/// </summary>
internal static class PsJson
{
    /// <summary><c>$output | ConvertTo-Json -Depth $depth -Compress</c>: one item is itself, several an array.</summary>
    public static string Pipeline(IReadOnlyList<object?> output, int depth = 14) =>
        output.Count == 1 ? Compress(output[0], depth) : Compress(output.ToArray(), depth);

    /// <summary>The pipeline output of a value: a list is its items; anything else is one item.</summary>
    public static List<object?> Unroll(object? value)
    {
        if (value is IEnumerable e && value is not string && value is not IDictionary)
            return e.Cast<object?>().ToList();
        return [value];
    }

    public static string Compress(object? value, int depth = 14)
    {
        var sb = new StringBuilder();
        Write(sb, Process(value, 0, depth), false, 0);
        return sb.ToString();
    }

    public static string Indented(object? value, int depth = 2)
    {
        var sb = new StringBuilder();
        Write(sb, Process(value, 0, depth), true, 0);
        return sb.ToString();
    }

    private static object? Process(object? obj, int depth, int maxDepth)
    {
        switch (obj)
        {
            case null: return null;
            case DBNull: return null;
            case string or char or bool or DateTime or DateTimeOffset or Guid or Uri or double or float or decimal or BigInteger: return obj;
        }
        var type = obj.GetType();
        if (type.IsPrimitive) return obj;
        if (type.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(type);
            return underlying == typeof(long) || underlying == typeof(ulong) ? obj.ToString() : obj;
        }
        if (depth > maxDepth) return PsText(obj);
        switch (obj)
        {
            case PlainObject plain:
            {
                var result = new List<KeyValuePair<string, object?>>();
                foreach (var entry in plain.Entries) result.Add(new(entry.Key, Process(entry.Value, depth + 1, maxDepth)));
                return result;
            }
            case IDictionary dictionary:
            {
                var result = new List<KeyValuePair<string, object?>>();
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not string key) throw new InvalidOperationException("The type '" + dictionary.GetType().FullName + "' is not supported for serialization or deserialization of a dictionary. Keys must be strings.");
                    result.Add(new(key, Process(entry.Value, depth + 1, maxDepth)));
                }
                return result;
            }
            case IEnumerable enumerable:
                return enumerable.Cast<object?>().Select(item => Process(item, depth + 1, maxDepth)).ToList();
        }
        var members = new List<KeyValuePair<string, object?>>();
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            members.Add(new(field.Name, Process(field.GetValue(obj), depth + 1, maxDepth)));
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var getter = property.GetGetMethod();
            if (getter == null || getter.GetParameters().Length != 0) continue;
            object? value;
            try { value = getter.Invoke(obj, []); } catch (Exception) { value = null; }
            members.Add(new(property.Name, Process(value, depth + 1, maxDepth)));
        }
        return members;
    }

    /// <summary>[string]$value (LanguagePrimitives.ConvertTo, invariant culture) for the values a golden can hold.</summary>
    public static string PsText(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "True" : "False",
        double d => d.ToString("G15", CultureInfo.InvariantCulture),
        float f => f.ToString("G7", CultureInfo.InvariantCulture),
        DBNull => "",
        PlainObject plain => plain.ToString(),
        IDictionary dictionary => dictionary.ToString()!,
        IEnumerable e => string.Join(" ", e.Cast<object?>().Select(i => i is IEnumerable and not string ? i.ToString() : PsText(i))),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static void Write(StringBuilder sb, object? value, bool indent, int level)
    {
        switch (value)
        {
            case null: sb.Append("null"); return;
            case string s: WriteString(sb, s); return;
            case char c: WriteString(sb, c.ToString()); return;
            case bool b: sb.Append(b ? "true" : "false"); return;
            case double d: sb.Append(Float(d, d.ToString("R", CultureInfo.InvariantCulture), double.IsNaN(d), double.IsPositiveInfinity(d), double.IsNegativeInfinity(d))); return;
            case float f: sb.Append(Float(f, f.ToString("R", CultureInfo.InvariantCulture), float.IsNaN(f), float.IsPositiveInfinity(f), float.IsNegativeInfinity(f))); return;
            case decimal m: sb.Append(EnsureDecimalPlace(m.ToString(CultureInfo.InvariantCulture))); return;
            case BigInteger big: sb.Append(big.ToString(CultureInfo.InvariantCulture)); return;
            case DateTime dt: WriteString(sb, dt.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) + Kind(dt)); return;
            case DateTimeOffset dto: WriteString(sb, dto.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture)); return;
            case Guid g: WriteString(sb, g.ToString("D")); return;
            case Uri u: WriteString(sb, u.OriginalString); return;
            case Enum e: sb.Append(Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); return;
            case List<KeyValuePair<string, object?>> obj:
                if (obj.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                for (var i = 0; i < obj.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (indent) NewLine(sb, level + 1);
                    WriteString(sb, obj[i].Key);
                    sb.Append(indent ? ": " : ":");
                    Write(sb, obj[i].Value, indent, level + 1);
                }
                if (indent) NewLine(sb, level);
                sb.Append('}');
                return;
            case List<object?> list:
                if (list.Count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (indent) NewLine(sb, level + 1);
                    Write(sb, list[i], indent, level + 1);
                }
                if (indent) NewLine(sb, level);
                sb.Append(']');
                return;
        }
        sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static void NewLine(StringBuilder sb, int level) => sb.Append(Environment.NewLine).Append(' ', level * 2);

    private static string Kind(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Utc => "Z",
        DateTimeKind.Local => dt.ToString("zzz", CultureInfo.InvariantCulture),
        _ => "",
    };

    private static string Float(object _, string text, bool nan, bool positive, bool negative)
    {
        if (nan) return "\"NaN\"";
        if (positive) return "\"Infinity\"";
        if (negative) return "\"-Infinity\"";
        return EnsureDecimalPlace(text);
    }

    private static string EnsureDecimalPlace(string text) =>
        text.IndexOf('.') >= 0 || text.IndexOf('E') >= 0 || text.IndexOf('e') >= 0 || text.Contains("Infinity", StringComparison.Ordinal) || text == "NaN" ? text : text + ".0";

    /// <summary>Newtonsoft's default escaping (pwsh 7 ConvertTo-Json): C0, quote, backslash, U+0085, U+2028, U+2029.</summary>
    public static void WriteString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\u0085': sb.Append("\\u0085"); break;
                case '\u2028': sb.Append("\\u2028"); break;
                case '\u2029': sb.Append("\\u2029"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
    }
}
