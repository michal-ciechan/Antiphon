using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Antiphon.LandGraphAudit;

/// <summary>
/// The PowerShell language rules the land-graph pre-check was written against, so that
/// <see cref="LandGraphPrecheck"/> decides exactly as scripts/audit-land-verification-precheck.ps1 did.
/// Each member names the construct it stands for. Measured on pwsh 7.6 (.NET 10):
/// <list type="bullet">
/// <item><c>-eq</c>, <c>-ieq</c>, <c>-contains</c> and <c>switch</c> on strings compare with the invariant
/// culture, ignoring case; <c>-ceq</c> does the same without ignoring case. Both are linguistic, so a
/// zero-width character is ignored ("a​" -eq "a").</item>
/// <item><c>-match</c> is a <see cref="RegexOptions.IgnoreCase"/> regex in the current culture.</item>
/// <item><c>'x'.IndexOf('y')</c>, <c>.LastIndexOf</c> and <c>.StartsWith</c> with a string argument bind to
/// the current-culture overloads; a <c>[char]</c> argument binds to the ordinal one.</item>
/// <item><c>@{}</c> is a <see cref="Hashtable"/> keyed <see cref="StringComparer.OrdinalIgnoreCase"/>.</item>
/// <item>Property access on an <see cref="XmlNode"/> goes through the XML adapter first: an attribute or a
/// non-whitespace child node whose local name matches (ordinal, ignoring case) shadows the .NET property.</item>
/// </list>
/// </summary>
internal static class Ps
{
    private static readonly CompareInfo Invariant = CultureInfo.InvariantCulture.CompareInfo;

    /// <summary><c>$a -eq $b</c> (and <c>-ieq</c>, <c>-contains</c>, <c>switch</c>) for a string left operand.</summary>
    public static bool Eq(string? left, string? right)
    {
        if (left is null) return right is null;
        if (right is null) return false;
        return Invariant.Compare(left, right, CompareOptions.IgnoreCase) == 0;
    }

    /// <summary><c>$a -ceq $b</c> for a string left operand.</summary>
    public static bool CEq(string? left, string? right)
    {
        if (left is null) return right is null;
        if (right is null) return false;
        return Invariant.Compare(left, right, CompareOptions.None) == 0;
    }

    /// <summary><c>$value -match $pattern</c>; a null input is matched as ''.</summary>
    public static bool Match(string? input, string pattern) =>
        Regex.IsMatch(input ?? "", pattern, RegexOptions.IgnoreCase);

    /// <summary><c>$value -cmatch $pattern</c>.</summary>
    public static bool CMatch(string? input, string pattern) => Regex.IsMatch(input ?? "", pattern);

    /// <summary><c>$text.IndexOf('x')</c>: the string overload, current culture.</summary>
    public static int IndexOf(string text, string value) => text.IndexOf(value, StringComparison.CurrentCulture);

    /// <summary><c>$text.LastIndexOf('x')</c>: the string overload, current culture.</summary>
    public static int LastIndexOf(string text, string value) => text.LastIndexOf(value, StringComparison.CurrentCulture);

    /// <summary><c>$text.StartsWith('x')</c>: the string overload, current culture.</summary>
    public static bool StartsWith(string text, string value) => text.StartsWith(value, StringComparison.CurrentCulture);

    /// <summary><c>@{}</c>.</summary>
    public static Hashtable NewHashtable() => new(StringComparer.OrdinalIgnoreCase);

    /// <summary><c>$object.Name</c> with PowerShell's adapters, intrinsic Count and member enumeration.</summary>
    public static object? Get(object? target, string name)
    {
        switch (target)
        {
            case null:
                return IsName(name, "Count") ? 0 : null;
            case string text:
                if (IsName(name, "Length")) return text.Length;
                return IsName(name, "Count") ? 1 : null;
            case object?[] array:
                if (IsName(name, "Length") || IsName(name, "Count")) return array.Length;
                return Enumerate(array, name);
            case XmlNode node:
                var hits = FindNodes(node, name);
                if (hits.Count == 1) return NodeValue(hits[0]);
                if (hits.Count > 1) return hits.Select(NodeValue).ToArray();
                if (TryNodeProperty(node, name, out var value)) return value;
                return IsName(name, "Count") || IsName(name, "Length") ? 1 : null;
            case XmlNodeList list:
                if (IsName(name, "Count")) return list.Count;
                return Enumerate(list, name);
            case XmlNamedNodeMap map:
                if (IsName(name, "Count")) return map.Count;
                return Enumerate(map, name);
            default:
                return IsName(name, "Count") || IsName(name, "Length") ? 1 : null;
        }
    }

    /// <summary>The values <c>foreach ($x in $value)</c> visits. An XmlNode and a string are one value.</summary>
    public static IEnumerable<object?> Each(object? value)
    {
        switch (value)
        {
            case null:
                yield break;
            case string or XmlNode:
                yield return value;
                yield break;
            case IDictionary:
                yield return value;
                yield break;
            case IEnumerable items:
                foreach (var item in items) yield return item;
                yield break;
            default:
                yield return value;
                yield break;
        }
    }

    /// <summary><c>[string]$value</c>.</summary>
    public static string Str(object? value) => value switch
    {
        null => "",
        string text => text,
        XmlNode node => node.GetType().FullName ?? "",
        IDictionary => value.ToString() ?? "",
        IEnumerable items => string.Join(" ", items.Cast<object?>().Select(ElementStr)),
        _ => Scalar(value),
    };

    /// <summary>PowerShell truth: <c>if ($value)</c>.</summary>
    public static bool IsTrue(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        string text => text.Length > 0,
        int number => number != 0,
        Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture) != 0,
        object?[] array => array.Length switch { 0 => false, 1 => IsTrue(array[0]), _ => true },
        _ => true,
    };

    /// <summary><c>$left -eq $right</c> (ignoreCase) or <c>-ceq</c>; an array left operand filters.</summary>
    public static object EqOp(object? left, object? right, bool ignoreCase = true) => Compare(left, right, ignoreCase, false);

    /// <summary><c>$left -ne $right</c> (ignoreCase) or <c>-cne</c>; an array left operand filters.</summary>
    public static object NeOp(object? left, object? right, bool ignoreCase = true) => Compare(left, right, ignoreCase, true);

    /// <summary><c>$value.GetAttribute($name)</c>. Any other target is a method-invocation failure.</summary>
    public static object? GetAttribute(object? target, string name)
    {
        switch (target)
        {
            case XmlElement element:
                return element.GetAttribute(name);
            case object?[] array when array.Length > 0 && array.All(static x => x is XmlElement):
                var results = array.Select(x => (object?)((XmlElement)x!).GetAttribute(name)).ToArray();
                return results.Length == 1 ? results[0] : results;
            case null:
                throw new InvalidOperationException("You cannot call a method on a null-valued expression.");
            default:
                throw new InvalidOperationException(
                    $"Method invocation failed because [{target.GetType().FullName}] does not contain a method named 'GetAttribute'.");
        }
    }

    private static object Compare(object? left, object? right, bool ignoreCase, bool negate)
    {
        if (left is object?[] array)
            return array.Where(item => ScalarEquals(item, right, ignoreCase) != negate).ToArray();
        return ScalarEquals(left, right, ignoreCase) != negate;
    }

    private static bool ScalarEquals(object? left, object? right, bool ignoreCase)
    {
        switch (left)
        {
            case null:
                return right is null;
            case string text:
                if (right is null) return false;
                return Invariant.Compare(text, Str(right), ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None) == 0;
            case bool flag:
                return flag == IsTrue(right);
            case int number:
                return right switch
                {
                    int other => number == other,
                    Enum e => number == Convert.ToInt64(e, CultureInfo.InvariantCulture),
                    string s => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed == number,
                    _ => false,
                };
            case Enum e:
                return right switch
                {
                    Enum other when other.GetType() == e.GetType() => e.Equals(other),
                    string s => Enum.TryParse(e.GetType(), s.Trim(), ignoreCase: true, out var parsed) && e.Equals(parsed),
                    int other => Convert.ToInt64(e, CultureInfo.InvariantCulture) == other,
                    _ => false,
                };
            default:
                return ReferenceEquals(left, right) || left.Equals(right);
        }
    }

    private static string ElementStr(object? value) => value switch
    {
        null => "",
        string text => text,
        XmlNode node => node.LocalName,
        Array array => array.GetType().FullName ?? "",
        _ => Scalar(value),
    };

    private static string Scalar(object value) => value switch
    {
        bool flag => flag ? "True" : "False",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static object? Enumerate(IEnumerable items, string name)
    {
        var results = new List<object?>();
        foreach (var item in items)
        {
            var value = Get(item, name);
            if (value is object?[] nested) results.AddRange(nested);
            else results.Add(value);
        }

        return results.Count switch { 0 => null, 1 => results[0], _ => results.ToArray() };
    }

    private static List<XmlNode> FindNodes(XmlNode node, string name)
    {
        var hits = new List<XmlNode>();
        if (node.Attributes is { } attributes)
        {
            foreach (XmlNode attribute in attributes)
            {
                if (attribute.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)) hits.Add(attribute);
            }
        }

        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is XmlWhitespace) continue;
            if (child.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)) hits.Add(child);
        }

        return hits;
    }

    // An attribute is its value; an element with attributes, or with any child other than one text node, is
    // the node itself; otherwise the node's inner text.
    private static object? NodeValue(XmlNode node)
    {
        if (node is XmlText text) return text.InnerText;
        if (node is XmlAttribute attribute) return attribute.Value;
        if (node.Attributes is { Count: > 0 }) return node;
        if (!node.HasChildNodes) return node.InnerText;
        var children = node.ChildNodes;
        if (children.Count == 1 && children[0]!.NodeType == XmlNodeType.Text) return node.InnerText;
        return node;
    }

    private static bool TryNodeProperty(XmlNode node, string name, out object? value)
    {
        value = null;
        if (IsName(name, "LocalName")) value = node.LocalName;
        else if (IsName(name, "NodeType")) value = node.NodeType;
        else if (IsName(name, "InnerText")) value = node.InnerText;
        else if (IsName(name, "InnerXml")) value = node.InnerXml;
        else if (IsName(name, "ChildNodes")) value = node.ChildNodes;
        else if (IsName(name, "Attributes")) value = node.Attributes;
        else if (IsName(name, "ParentNode")) value = node.ParentNode;
        else if (IsName(name, "OwnerDocument")) value = node.OwnerDocument;
        else if (IsName(name, "HasChildNodes")) value = node.HasChildNodes;
        else if (IsName(name, "Name")) value = node.Name;
        else if (IsName(name, "Value")) value = node.Value;
        else if (IsName(name, "DocumentElement") && node is XmlDocument document) value = document.DocumentElement;
        else return false;
        return true;
    }

    private static bool IsName(string name, string expected) => name.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
