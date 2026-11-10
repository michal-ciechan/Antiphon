using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Antiphon.LandGraphAudit;

/// <summary>
/// The graph script's reading of global.json at HEAD, measured on pwsh 7.6:
/// <c>(Get-GitValue @('show', 'HEAD:global.json') | ConvertFrom-Json).sdk</c>, then <c>.version</c> and
/// <c>.rollForward</c> under <c>-cnotmatch</c>, <c>[version]::TryParse</c> and <c>-cne</c>.
/// <list type="bullet">
/// <item>An object is a PSCustomObject: property names match ignoring case, a missing property is
/// <c>$null</c> (no strict mode), an exact duplicate key keeps the last value and keys differing only in case
/// refuse. A top-level array is written element by element, so <c>( ... )</c> collects none as <c>$null</c>,
/// one as that element and more as an array. Integers are Int64 (BigInteger past it), other numbers Double.</item>
/// <item><c>$array.name</c> enumerates members (missing ones as <c>$null</c>), flattening nested results.</item>
/// <item><c>$array -cnotmatch $p</c> and <c>$array -cne $s</c> filter; a filter result is true when it has two
/// or more elements, or one element that is itself true.</item>
/// <item>A .NET string argument receives <c>$null</c> as '' and an array joined with spaces.</item>
/// </list>
/// Parsing is System.Text.Json with comments and trailing commas allowed. Newtonsoft (ConvertFrom-Json) also
/// reads single quotes, unquoted names and NaN, which System.Text.Json refuses; the dotnet CLI refuses those
/// files too, so -Check stops at "SDK resolution failed" before reading global.json here. A parse failure
/// reads "Conversion from JSON failed with error: " followed by System.Text.Json's message, not Newtonsoft's.
/// </summary>
internal static class PsJson
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 1024,
    };

    /// <summary><c>($text | ConvertFrom-Json)</c>.</summary>
    public static object? ConvertFrom(string text)
    {
        if (text.Length == 0) return null;
        object? value;
        try
        {
            using var document = JsonDocument.Parse(text, Options);
            value = Convert(document.RootElement);
        }
        catch (JsonException error)
        {
            throw new AuditRefusal("Conversion from JSON failed with error: " + error.Message);
        }
        catch (InvalidOperationException error)
        {
            throw new AuditRefusal("Conversion from JSON failed with error: " + error.Message);
        }

        if (value is object?[] items) return items.Length switch { 0 => null, 1 => items[0], _ => items };
        return value;
    }

    /// <summary><c>$value.name</c>.</summary>
    public static object? Get(object? target, string name)
    {
        switch (target)
        {
            case PsJsonObject item:
                return item.TryGet(name, out var value) ? value : null;
            case object?[] array:
                var results = new List<object?>();
                foreach (var element in array)
                {
                    var found = Get(element, name);
                    if (found is object?[] nested) results.AddRange(nested);
                    else results.Add(found);
                }

                return results.Count switch { 0 => null, 1 => results[0], _ => results.ToArray() };
            default:
                return null;
        }
    }

    /// <summary><c>if ($value)</c>.</summary>
    public static bool IsTrue(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        string text => text.Length > 0,
        long number => number != 0,
        double number => number != 0,
        BigInteger number => !number.IsZero,
        object?[] array => array.Length switch { 0 => false, 1 => IsTrue(array[0]), _ => true },
        _ => true,
    };

    /// <summary><c>$value -cnotmatch $pattern</c>: a bool, or the filtered array.</summary>
    public static object CNotMatchOp(object? value, string pattern)
    {
        if (value is object?[] array)
            return array.Where(element => !Ps.CMatch(MatchString(element), pattern)).ToArray();
        return !Ps.CMatch(MatchString(value), pattern);
    }

    /// <summary><c>$value -cne $text</c>: a bool, or the filtered array.</summary>
    public static object CNeOp(object? value, string text)
    {
        if (value is object?[] array) return array.Where(element => !CEquals(element, text)).ToArray();
        return !CEquals(value, text);
    }

    /// <summary>The string a .NET <c>[string]</c> parameter receives.</summary>
    public static string ArgumentString(object? value) => value switch
    {
        null => "",
        object?[] array => string.Join(" ", array.Select(ElementString)),
        _ => Scalar(value),
    };

    private static bool CEquals(object? left, string right) => left switch
    {
        null => false,
        string text => Ps.CEq(text, right),
        bool flag => flag == (right.Length > 0),
        _ => false,
    };

    private static string MatchString(object? value) => value switch
    {
        null => "",
        object?[] array => string.Join(" ", array.Select(ElementString)),
        _ => Scalar(value),
    };

    private static string ElementString(object? value) => value switch
    {
        null => "",
        object?[] => "System.Object[]",
        _ => Scalar(value),
    };

    private static string Scalar(object value) => value switch
    {
        string text => text,
        bool flag => flag ? "True" : "False",
        PsJsonObject item => item.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static object? Convert(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var item = new PsJsonObject();
                foreach (var property in element.EnumerateObject()) item.Add(property.Name, Convert(property.Value));
                return item;
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(Convert).ToArray();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                var raw = element.GetRawText();
                if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)) return integer;
                if (raw.IndexOfAny(['.', 'e', 'E']) < 0) return BigInteger.Parse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }
}

/// <summary>A PSCustomObject from ConvertFrom-Json: case-insensitive names in document order.</summary>
internal sealed class PsJsonObject
{
    private readonly List<string> _names = [];
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string name, object? value)
    {
        var existing = _names.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !existing.Equals(name, StringComparison.Ordinal))
        {
            throw new AuditRefusal("Cannot convert the JSON string because it contains keys with different casing. Please use "
                + "the -AsHashTable switch instead. The key that was attempted to be added to the existing key '" + existing
                + "' was '" + name + "'.");
        }

        if (existing is null) _names.Add(name);
        _values[name] = value;
    }

    public bool TryGet(string name, out object? value) => _values.TryGetValue(name, out value);

    public override string ToString() =>
        "@{" + string.Join("; ", _names.Select(name => name + "=" + PsJson.ArgumentString(_values[name]))) + "}";
}
