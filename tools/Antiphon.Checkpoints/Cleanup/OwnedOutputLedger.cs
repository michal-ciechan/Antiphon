using System.Text.Json;

namespace Antiphon.Checkpoints;

public static class OwnedOutputLedger
{
    private const string Ledger = ".checkpoint-output-ledger.json";
    public static string? Seal(string root, DateTimeOffset? buildExited = null)
    {
        try
        {
            if (!File.Exists(Path.Combine(root, ".checkpoint-output-owner.json"))) return "output-owner-missing";
            var entries = Inventory(root);
            // CARD-1242: adopt only entries inside the custody window of this process's fresh root
            // and the driver-observed build exit, with size and hash taken now (OutputCustody holds
            // the threat model). Later additions, changes and links still refuse; a root this
            // process did not create, or a seal without an observed exit, adopts nothing.
            var window = SafeWrite.WasCreated(root) ? OutputCustody.For(root, buildExited) : null;
            if (window is not null)
                foreach (var relative in entries.Keys)
                    SafeWrite.AdoptCreated(Path.Combine(root, relative), window);
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
            if (!SafeWrite.WasCreated(root)) return "output-memory-authority-missing; left in place: " + root + "; remove it manually";
            var actual = Inventory(root);
            foreach (var path in actual.Keys.Append(Ledger))
                if (!SafeWrite.MatchesCreation(Path.Combine(root, path)))
                    return "output-ledger-mismatch; left in place: " + Path.Combine(root, path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return "output-inventory-failed"; }
    }

    public static string? Remove(string root, bool dryRun = false)
    {
            var refusal = Check(root);
            if (refusal is not null || dryRun) return refusal;
            // No recursive deletion: an unrecorded late addition keeps its directory alive.
            var entries = Inventory(root).Keys.Append(Ledger).OrderByDescending(path => path.Count(c => c == '/')).ToList();
            foreach (var relative in entries.Where(path => !Directory.Exists(Path.Combine(root, path))))
            {
                var full = Path.Combine(root, relative);
                var linked = SafeWrite.Preflight(root, [full]);
                if (linked is not null || !SafeWrite.MatchesCreation(full)) return "left in place: " + full;
                File.Delete(full);
            }
            foreach (var relative in entries.Where(path => Directory.Exists(Path.Combine(root, path))))
            {
                var full = Path.Combine(root, relative);
                if (SafeWrite.Preflight(root, [full]) is not null || !SafeWrite.MatchesCreation(full)) return "left in place: " + full;
                Directory.Delete(full, recursive: false);
            }
            Directory.Delete(root, recursive: false);
            return null;
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
                else { entries.Add(relative, "file-size:" + new FileInfo(path).Length); }
            }
        }
        return entries;
    }
}
