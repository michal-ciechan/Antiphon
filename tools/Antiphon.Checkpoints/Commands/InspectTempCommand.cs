using System.Text.Json;

namespace Antiphon.Checkpoints;

// CARD-0804/0805: inventory the checkpoint test roots (c723-<32 hex>) under a temp root without deleting anything; never
// infer a cleanup verdict from name or age alone. `antiphon-scripts checkpoints inspect-temp [--temp-root <dir>]
// [--output-file <json>] [--max-entries <1..10000>] [--max-descendants <1..100000>]`. It replaced
// scripts/inspect-checkpoint-temp.ps1 (unit 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): the same
// schema-1 JSON, the marker read through the verified native reader, a reparse point below a root not followed.
internal static class InspectTempCommand
{
    private const string MarkerName = ".checkpoint-test-root.json";

    public static int Run(Program.ArgSet options, TextWriter output)
    {
        var maxEntries = Bounded(options.Get("max-entries"), 512, 10_000);
        var maxDescendants = Bounded(options.Get("max-descendants"), 10_000, 100_000);
        if (maxEntries is null || maxDescendants is null)
        {
            output.WriteLine("Inspection bounds are outside the supported range.");
            return ExitCodes.Invalid;
        }
        var tempRoot = Path.GetFullPath(options.Get("temp-root") is { Length: > 0 } given ? given : Path.GetTempPath());
        var roots = new List<Dictionary<string, object>>();
        var examined = 0;
        var truncated = false;
        foreach (var entry in Directory.EnumerateFileSystemEntries(tempRoot).Take(maxEntries.Value + 1))
        {
            if (examined >= maxEntries) { truncated = true; break; }
            examined++;
            var name = Path.GetFileName(entry);
            if (!Directory.Exists(entry) || !System.Text.RegularExpressions.Regex.IsMatch(name, "^c723-[0-9a-f]{32}$")) continue;
            var reason = Marker(entry, name);
            var (count, bytes, sampleTruncated, sampleReason) = Sample(entry, maxDescendants.Value);
            roots.Add(new Dictionary<string, object>
            {
                ["path"] = entry,
                ["reason"] = sampleReason ?? reason,
                ["sampledDescendants"] = count,
                ["sampledLogicalBytes"] = bytes,
                ["sampleTruncated"] = sampleTruncated,
            });
        }
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["schemaVersion"] = 1,
            ["tempRoot"] = tempRoot,
            ["inspectedAt"] = DateTimeOffset.UtcNow.ToString("o"),
            ["maxEntries"] = maxEntries.Value,
            ["maxDescendants"] = maxDescendants.Value,
            ["entriesExamined"] = examined,
            ["roots"] = roots,
            ["truncated"] = truncated,
        }, new JsonSerializerOptions { WriteIndented = true });
        if (options.Get("output-file") is { Length: > 0 } file)
        {
            var full = Path.GetFullPath(file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, json);
        }
        output.WriteLine(json);
        return ExitCodes.Green;
    }

    private static int? Bounded(string? text, int fallback, int max) =>
        text is null ? fallback
        : int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            && value >= 1 && value <= max ? value : null;

    private static string Marker(string root, string name)
    {
        var marker = Path.Combine(root, MarkerName);
        if (!File.Exists(marker)) return "legacy-unmarked";
        try
        {
            using var document = JsonDocument.Parse(CheckpointFileReader.Read(root, marker, 4 * 1024 * 1024));
            var value = document.RootElement;
            return value.TryGetProperty("Version", out var version) && version.ValueKind == JsonValueKind.Number && version.GetInt32() == 1
                && value.TryGetProperty("RootId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == name[5..]
                && value.TryGetProperty("RootPath", out var path) && path.ValueKind == JsonValueKind.String
                && Path.GetFullPath(path.GetString()!) == Path.GetFullPath(root)
                ? "marked-identity-unverified" : "marker-invalid";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException
            or FormatException or ArgumentException or InvalidOperationException)
        {
            return "marker-unreadable";
        }
    }

    // Logical bytes of up to max descendants, breadth first; a reparse point below the root stops the sample unfollowed.
    private static (int Count, long Bytes, bool Truncated, string? Reason) Sample(string root, int max)
    {
        var count = 0;
        long bytes = 0;
        var queue = new Queue<string>();
        queue.Enqueue(root);
        try
        {
            while (queue.TryDequeue(out var directory))
            {
                foreach (var child in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    if (count >= max) return (count, bytes, true, null);
                    count++;
                    if ((child.Attributes & FileAttributes.ReparsePoint) != 0) return (count, bytes, true, "linked-descendant");
                    if (child is FileInfo file) bytes += file.Length;
                    else queue.Enqueue(child.FullName);
                }
            }
            return (count, bytes, false, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (count, bytes, true, "inventory-incomplete");
        }
    }
}
