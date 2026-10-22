using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Antiphon.LandGraphAudit;

/// <summary>
/// D-5: evaluation cannot inspect target execution or the other OS branch. Review approves
/// these exact XML bodies, including task parameters. A pin never bypasses evaluation checks.
/// </summary>
internal static class TargetPins
{
    private const string PinsPath = "server/Application/Services/LandVerificationBuildGraph.pins.txt";

    public static void Check(string repo, IReadOnlySet<string> tracked, ISet<string> refusals)
    {
        var pins = ReadPins(repo, tracked, refusals);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in tracked.Where(BuildInputPolicy.Definition).Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);
            var extension = Path.GetExtension(file);
            var projectXml = extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase)
                             || extension.Equals(".props", StringComparison.OrdinalIgnoreCase)
                             || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase)
                             || file.EndsWith("proj.user", StringComparison.OrdinalIgnoreCase);
            // Some digested definitions are JSON, solution text or response files. Also inspect
            // XML-shaped definitions with unusual names (e.g. Directory.Build.custom), which D-7
            // permits as imports. A broken project XML file fails closed in XmlReader.
            if (!projectXml && !text.AsSpan().TrimStart().StartsWith("<", StringComparison.Ordinal)) continue;
            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            });
            document.Load(reader);
            var relative = Path.GetRelativePath(repo, file).Replace(Path.DirectorySeparatorChar, '/');
            foreach (var element in Elements(document.DocumentElement!))
            {
                var target = element.LocalName == "Target";
                var condition = element.GetAttribute("Condition");
                if (!target && !OsDependent(condition)) continue;
                var selector = target ? element.GetAttribute("Name") : ElementPath(element);
                var display = relative + " " + (target ? "Target " : "") + selector;
                if (selector.Length == 0 || selector.IndexOfAny(['\t', '\r', '\n']) >= 0)
                {
                    refusals.Add($"refused: {display} (invalid element identity)");
                    continue;
                }
                var key = relative + "\t" + selector;
                if (!seen.Add(key))
                    refusals.Add($"refused: {display} (duplicate element)");
                if (!pins.TryGetValue(key, out var expected))
                    refusals.Add($"refused: {display} (missing pin)");
                else if (!expected.Equals(Hash(element), StringComparison.OrdinalIgnoreCase))
                    refusals.Add($"refused: {display} (changed pin)");
            }
        }
        foreach (var key in pins.Keys.Where(key => !seen.Contains(key)))
            refusals.Add($"refused: {key.Replace('\t', ' ')} (stale pin)");
    }

    private static Dictionary<string, string> ReadPins(string repo, IReadOnlySet<string> tracked, ISet<string> refusals)
    {
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(repo, PinsPath);
        if (!File.Exists(path))
        {
            if (tracked.Contains(path)) refusals.Add($"refused: {PinsPath} (missing pins file)");
            return pins;
        }
        if (!tracked.Contains(path))
        {
            refusals.Add($"refused: {PinsPath} (untracked pins file)");
            return pins;
        }
        var expectedPath = Path.Combine(SolutionAudit.RealPath(repo), PinsPath);
        if (!BuildInputPolicy.PathComparer.Equals(SolutionAudit.RealPath(path), expectedPath))
        {
            refusals.Add($"refused: {PinsPath} (linked pins file)");
            return pins;
        }
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var fields = line.Split('\t');
            if (fields.Length != 3 || fields[0].Length == 0 || fields[1].Length == 0
                || fields[0].Contains('\\') || fields[0].Split('/').Any(p => p is "" or "." or "..")
                || Path.IsPathRooted(fields[0]) || fields[2].Length != 64 || !fields[2].All(Uri.IsHexDigit))
            {
                refusals.Add($"refused: {PinsPath}:{lineNumber} (invalid pin)");
                continue;
            }
            if (!pins.TryAdd(fields[0] + "\t" + fields[1], fields[2]))
                refusals.Add($"refused: {PinsPath}:{lineNumber} {fields[0]} {fields[1]} (duplicate pin)");
        }
        return pins;
    }

    private static bool OsDependent(string condition) =>
        condition.Contains("$(OS)", StringComparison.OrdinalIgnoreCase)
        || condition.Contains("IsOSPlatform", StringComparison.OrdinalIgnoreCase)
        || condition.Contains("RuntimeInformation", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<XmlElement> Elements(XmlElement element)
    {
        yield return element;
        foreach (var child in element.ChildNodes.OfType<XmlElement>())
            foreach (var descendant in Elements(child)) yield return descendant;
    }

    private static string ElementPath(XmlElement element)
    {
        var index = 1;
        for (var sibling = element.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
            if (sibling is XmlElement other && other.Name == element.Name) index++;
        return (element.ParentNode is XmlElement parent ? ElementPath(parent) : "") + $"/{element.Name}[{index}]";
    }

    private static string Hash(XmlElement element)
    {
        var copy = (XmlElement)element.CloneNode(true);
        NormalizeLayout(copy);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(copy.OuterXml)));
    }

    private static void NormalizeLayout(XmlElement element)
    {
        var children = element.ChildNodes.OfType<XmlElement>().ToArray();
        // Never trim text/CDATA, attribute values, leaf whitespace or xml:space=preserve.
        // Only indentation in element-only content is layout, rather than a property value.
        if (children.Length > 0 && !element.ChildNodes.Cast<XmlNode>().Any(n => n is XmlText or XmlCDataSection))
            foreach (var whitespace in element.ChildNodes.OfType<XmlWhitespace>().ToArray())
                element.RemoveChild(whitespace);
        foreach (var child in children) NormalizeLayout(child);
    }
}
