using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Antiphon.TimeoutAudit;

internal static class ReceiptMeasurements
{
    public static void Read(string[] roots, AuditReport report)
    {
        var files = roots.SelectMany(SourceInventory.Files).Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var attributed = new HashSet<string>(StringComparer.Ordinal);
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files.Where(x => Path.GetFileName(x) == "report.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                if (!root.TryGetProperty("rows", out var rows)) continue;
                var sha = Text(root, "commit");
                var run = Text(root, "runId");
                var host = root.TryGetProperty("host", out var h) ? h : default;
                var context = $"os:{Text(host, "os")}|host:{Text(host, "hostId")}|load:{Text(host, "loadAvg")}|concurrency:{Text(root, "maxConcurrentRows")}";
                foreach (var row in rows.EnumerateArray())
                {
                    var filter = Text(row, "filter");
                    var trx = Text(row, "trx");
                    if (trx == "unknown") continue;
                    trx = Path.GetFullPath(Path.IsPathRooted(trx) ? trx : Path.Combine(Path.GetDirectoryName(file)!, trx));
                    attributed.Add(trx);
                    var key = run + "|" + sha + "|" + Text(row, "id");
                    if (run == "unknown" || sha.Length != 40 && sha.Length != 64)
                    { report.Warnings.Add(file + ": missing source/run identity"); continue; }
                    var fingerprint = filter + "|" + Text(row, "seconds") + "|" + Text(row, "state") + "|" + (File.Exists(trx) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(trx))) : "missing");
                    if (seen.TryGetValue(key, out var prior))
                    {
                        if (prior != fingerprint) report.Warnings.Add(file + ": conflicting duplicate run/row");
                        continue;
                    }
                    seen[key] = fingerprint;
                    var timings = row.TryGetProperty("timings", out var t) ? t : default;
                    var wait = Numeric(row, "waitedSeconds") ?? Numeric(timings, "rowSlotWaitSeconds");
                    var wall = Numeric(timings, "testHostWallSeconds");
                    var outcome = (Numeric(row, "attempt") ?? 1) > 1 ? "Retry" : Text(row, "state") == "green" ? "Passed" : "Failed";
                    var rowContext = context + "|source:" + sha + "|filter:" + filter;
                    if (wall is >= 0 && double.IsFinite(wall.Value))
                        report.Observations.Add(new(filter, rowContext, wall.Value, file, sha, outcome, wait));
                    // Never substitute summed case time or slot-inclusive wall for host execution time.
                    else report.Warnings.Add(file + ":" + Text(row, "id") + ": execution wall unknown");
                    ReadTrx(trx, rowContext, sha, report, (Numeric(row, "attempt") ?? 1) > 1);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or System.Xml.XmlException or FormatException)
            { report.Warnings.Add(file + ": " + ex.GetType().Name); }
        }
        foreach (var trx in files.Where(x => x.EndsWith(".trx", StringComparison.OrdinalIgnoreCase) && !attributed.Contains(x)))
        {
            try
            {
                // Parse even orphaned files so corrupt files are distinguished from absent provenance.
                _ = XDocument.Load(trx);
                report.Warnings.Add(trx + ": source/host/load/row attribution unavailable; not a measured sample");
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
            { report.Warnings.Add(trx + ": " + ex.GetType().Name); }
        }
    }
    private static void ReadTrx(string path, string context, string sha, AuditReport report, bool retry)
    {
        var xml = XDocument.Load(path);
        var definitions = xml.Descendants().Where(x => x.Name.LocalName == "UnitTest")
            .Where(x => x.Attribute("id") != null)
            .GroupBy(x => (string)x.Attribute("id")!).ToDictionary(x => x.Key, x => x.First().Descendants().FirstOrDefault(n => n.Name.LocalName == "TestMethod"));
        foreach (var result in xml.Descendants().Where(x => x.Name.LocalName == "UnitTestResult"))
        {
            if (!definitions.TryGetValue((string?)result.Attribute("testId") ?? "", out var method) || method == null)
            { report.Warnings.Add(path + ": unresolved test definition"); continue; }
            var className = (string?)method.Attribute("className") ?? "";
            var parens = className.IndexOf('(');
            if (parens >= 0) className = className[..parens];
            var name = (string?)method.Attribute("name");
            var display = (string?)result.Attribute("testName") ?? name;
            if (!TimeSpan.TryParse((string?)result.Attribute("duration"), CultureInfo.InvariantCulture, out var duration) || duration < TimeSpan.Zero)
            { report.Warnings.Add(path + ": missing/invalid duration"); continue; }
            // Expanded parameter cases remain distinct contexts; they are not extra repeated runs.
            report.Observations.Add(new(className + "." + name, context + "|case:" + display, duration.TotalSeconds, path, sha, retry ? "Retry:" + (string?)result.Attribute("outcome") : (string?)result.Attribute("outcome") ?? "Unknown"));
        }
    }
    private static string Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "unknown";
    private static double? Numeric(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
}
