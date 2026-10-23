using System.Text.Json;
using Antiphon.TimeoutAudit;

try
{
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    var receipts = new List<string>();
    var check = false;
    for (var i = 0; i < args.Length; i++)
    {
        var key = args[i];
        if (key == "--check") { check = true; continue; }
        if (key is not ("--repo" or "--receipts" or "--exceptions" or "--output")) throw new ArgumentException("Unknown option " + key);
        if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing value for " + key);
        if (key == "--receipts") receipts.Add(args[i]);
        else if (!values.TryAdd(key, args[i])) throw new ArgumentException("Duplicate option " + key);
    }
    var report = Audit.Run(new(values.GetValueOrDefault("--repo") ?? Directory.GetCurrentDirectory(), receipts.ToArray(), values.GetValueOrDefault("--exceptions")));
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    if (values.TryGetValue("--output", out var output)) File.WriteAllText(output, json + Environment.NewLine);
    else Console.WriteLine(json);
    Console.Error.WriteLine($"Timeout audit: {report.Inventory.Count} declarations, {report.Budgets.Count} budgets, {report.Violations.Count} violations, {report.Warnings.Count} warnings");
    return check && (report.Violations.Count > 0 || report.Warnings.Count > 0) ? 1 : 0;
}
catch (Exception ex) when (ex is IOException or ArgumentException or JsonException or InvalidOperationException or System.Xml.XmlException)
{
    Console.Error.WriteLine("Timeout audit refused: " + ex.Message);
    return 2;
}
