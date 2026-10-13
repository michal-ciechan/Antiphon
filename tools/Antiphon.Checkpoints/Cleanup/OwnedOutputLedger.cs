using System.Security.Cryptography;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public static class OwnedOutputLedger
{
    private const string Ledger = ".checkpoint-output-ledger.json";
    public static string? Seal(string root)
    {
        try
        {
            if (!File.Exists(Path.Combine(root, ".checkpoint-output-owner.json"))) return "output-owner-missing";
            var entries = Inventory(root);
            return SafeWrite.Text(Path.Combine(root, Ledger), JsonSerializer.Serialize(entries), SafeWrite.Under(root));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return "output-inventory-failed"; }
    }

    public static string? Check(string root)
    {
        try
        {
            var preflight = SafeWrite.Preflight(root, [root]);
            if (preflight is not null) return preflight;
            if (!File.Exists(Path.Combine(root, Ledger))) return "output-ledger-missing";
            var recorded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, Ledger)));
            var actual = Inventory(root);
            return recorded is not null && recorded.Count == actual.Count && recorded.All(pair => actual.TryGetValue(pair.Key, out var value) && value == pair.Value)
                ? null : "output-ledger-mismatch";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return "output-inventory-failed"; }
    }

    private static Dictionary<string, string> Inventory(string root)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(Path.GetFullPath(root));
        while (queue.TryDequeue(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null
                    || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("linked output");
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative == Ledger) continue;
                if (Directory.Exists(path)) { entries.Add(relative, "directory"); queue.Enqueue(path); }
                else { using var stream = File.OpenRead(path); entries.Add(relative, Convert.ToHexString(SHA256.HashData(stream))); }
            }
        }
        return entries;
    }
}
