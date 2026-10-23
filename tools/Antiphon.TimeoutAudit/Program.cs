using System.Text.Json;
using Antiphon.TimeoutAudit;

try
{
    string? Value(string key) { var i = Array.IndexOf(args, key); return i < 0 ? null : i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing " + key); }
    var root = Value("--repo") ?? Directory.GetCurrentDirectory();
    var receipts = args.Select((x, i) => (x, i)).Where(x => x.x == "--receipts")
        .Select(x => x.i + 1 < args.Length ? args[x.i + 1] : throw new ArgumentException("Missing --receipts")).ToArray();
    var report = Audit.Run(new(root, receipts, Value("--exceptions")));
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    if (Value("--output") is { } output) File.WriteAllText(output, json + Environment.NewLine);
    else Console.WriteLine(json);
    Console.Error.WriteLine($"Timeout audit: {report.Inventory.Count} declarations, {report.Budgets.Count} budgets, {report.Violations.Count} violations, {report.Warnings.Count} warnings");
    return args.Contains("--check") && (report.Violations.Count > 0 || report.Warnings.Count > 0) ? 1 : 0;
}
catch (Exception ex) when (ex is IOException or ArgumentException or JsonException or System.Xml.XmlException)
{
    Console.Error.WriteLine("Timeout audit refused: " + ex.Message);
    return 2;
}
