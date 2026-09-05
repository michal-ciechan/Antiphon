using System.Xml.Linq;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0110 S6: parse a TRX and report tests whose duration is ≥ the threshold and whose name
/// does not match the checked-in allowlist.
/// </summary>
public static class SlowTestTripwire
{
    public static readonly TimeSpan Threshold = TimeSpan.FromSeconds(5);

    public static IReadOnlyList<string> LoadAllowlist(string path) =>
        File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
            .ToArray();

    public static IReadOnlyList<SlowTestHit> FindUnlisted(
        string trxXml, IReadOnlyList<string> allowlist, TimeSpan? threshold = null)
    {
        var limit = threshold ?? Threshold;
        var doc = XDocument.Parse(trxXml);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var hits = new List<SlowTestHit>();
        foreach (var result in doc.Descendants(ns + "UnitTestResult"))
        {
            var name = (string?)result.Attribute("testName") ?? "";
            var durationText = (string?)result.Attribute("duration") ?? "";
            if (!TimeSpan.TryParse(durationText, out var duration) || duration < limit)
                continue;
            if (allowlist.Any(entry => name.Contains(entry, StringComparison.OrdinalIgnoreCase)))
                continue;
            hits.Add(new SlowTestHit(name, duration));
        }
        return hits;
    }
}

public readonly record struct SlowTestHit(string TestName, TimeSpan Duration);
