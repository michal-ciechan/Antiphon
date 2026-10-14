using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.Checkpoints;

// A value type keeps old ownership journals (which lack this member) readable.
public readonly record struct ToolCopyDigest(string Sha256);

// Only the starter can seal its in-memory creation records. This capability never
// imports disk entries into SafeWrite or authorizes a generic build-output delete.
internal sealed class ToolCopyAuthority
{
    private const string FileName = "tool-copy-authority.json";
    private const int MaxEntries = 10000;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly CopyRecord _record;
    private readonly Dictionary<string, CopyEntry> _entries;

    private ToolCopyAuthority(CopyRecord record)
    {
        _record = record;
        _entries = record.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
    }

    public static ToolCopyDigest Seal(string run, RunOwnership owner)
    {
        var tool = Path.Combine(Path.GetFullPath(run), "tool");
        SafeWrite.Require(OwnedOutputLedger.Check(tool));
        var entries = Inventory(tool).Select(path =>
        {
            var stamp = LedgerStat.Of(path);
            var content = SafeWrite.CreationRecord(path);
            if (content is null || !SafeWrite.MatchesCreation(path) || !stamp.Matches(LedgerStat.Of(path)))
                throw new IOException("refused: tool-authority-creation-mismatch");
            return new CopyEntry
            {
                Path = Path.GetRelativePath(tool, path).Replace('\\', '/'),
                Content = content, Stamp = stamp,
                CreatedUtcTicks = Directory.Exists(path) ? Directory.GetCreationTimeUtc(path).Ticks : 0,
            };
        }).ToList();
        var record = new CopyRecord
        {
            RunId = owner.RunId, RunDirectory = Path.GetFullPath(run), ToolDirectory = tool,
            Creator = owner.Starter ?? throw new IOException("refused: tool-authority-owner-missing"),
            Root = LedgerStat.Of(tool), RootCreatedUtcTicks = Directory.GetCreationTimeUtc(tool).Ticks,
            Entries = entries,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, Json);
        SafeWrite.Require(SafeWrite.Bytes(Path.Combine(run, FileName), bytes, SafeWrite.Under(run)));
        return new ToolCopyDigest(Hash(bytes));
    }

    public static ToolCopyAuthority? Load(string run, RunOwnership owner, out string? refusal)
    {
        refusal = "tool-authority-missing";
        if (owner.ToolCopy is not { } seal) return null;
        try
        {
            var bytes = ReceiptFile.ReadBytes(Path.Combine(run, FileName));
            if (seal.Sha256 != Hash(bytes)) { refusal = "tool-authority-digest-mismatch"; return null; }
            using (var json = JsonDocument.Parse(bytes)) RequireUniqueProperties(json.RootElement);
            var record = ReceiptJson.Parse<CopyRecord>(Encoding.UTF8.GetString(bytes), Json);
            if (record.Version != 1 || record.RunId != owner.RunId
                || !RunOwnershipStore.SamePath(record.RunDirectory, run)
                || !RunOwnershipStore.SamePath(record.ToolDirectory, Path.Combine(run, "tool"))
                || record.Creator != owner.Starter)
            { refusal = "tool-authority-run-mismatch"; return null; }
            if (record.Entries.Count is 0 or > MaxEntries
                || record.Entries.Any(entry => !SafeRelative(entry.Path))
                || record.Entries.Select(entry => entry.Path).Distinct(StringComparer.Ordinal).Count() != record.Entries.Count)
            { refusal = "tool-authority-path-invalid"; return null; }
            var authority = new ToolCopyAuthority(record);
            refusal = authority.Check();
            return refusal is null ? authority : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or ArgumentException or NotSupportedException)
        { refusal = "tool-authority-unreadable"; return null; }
    }

    public string? Check()
    {
        try
        {
            var root = _record.ToolDirectory;
            var preflight = SafeWrite.Preflight(root, [root]);
            if (preflight is not null) return preflight;
            if (!SameDirectory(root, _record.Root, _record.RootCreatedUtcTicks))
                return "tool-authority-root-mismatch";
            var actual = Inventory(root);
            if (actual.Count != _entries.Count) return "tool-authority-inventory-mismatch";
            foreach (var path in actual)
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (!_entries.TryGetValue(relative, out var entry) || !Matches(path, entry))
                    return "tool-authority-entry-mismatch";
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return "tool-authority-inventory-unreadable"; }
    }

    // SafeDelete still performs its full Git/link/containment preflight. Recheck
    // each entry immediately before removal; a late addition prevents rmdir.
    public string? Remove(string root)
    {
        if (!RunOwnershipStore.SamePath(root, _record.ToolDirectory)) return "tool-authority-root-mismatch";
        var refusal = Check();
        if (refusal is not null) return refusal;
        foreach (var entry in _record.Entries.Where(entry => entry.Stamp.Kind == "file"))
        {
            var path = Path.Combine(root, entry.Path);
            refusal = SafeWrite.Preflight(root, [path]);
            if (refusal is not null || !Matches(path, entry)) return refusal ?? "tool-authority-entry-mismatch";
            File.Delete(path);
        }
        foreach (var entry in _record.Entries.Where(entry => entry.Stamp.Kind == "directory")
                     .OrderByDescending(entry => entry.Path.Count(c => c == '/')))
        {
            var path = Path.Combine(root, entry.Path);
            refusal = SafeWrite.Preflight(root, [path]);
            if (refusal is not null || !SameDirectory(path, entry.Stamp, entry.CreatedUtcTicks))
                return refusal ?? "tool-authority-entry-mismatch";
            Directory.Delete(path, recursive: false);
        }
        if (!SameDirectory(root, _record.Root, _record.RootCreatedUtcTicks)) return "tool-authority-root-mismatch";
        Directory.Delete(root, recursive: false);
        return null;
    }

    private static bool Matches(string path, CopyEntry entry)
    {
        if (entry.Stamp.Kind == "directory")
            return entry.Content == "directory" && SameDirectory(path, entry.Stamp, entry.CreatedUtcTicks);
        if (entry.Stamp.Kind != "file") return false;
        var before = LedgerStat.Of(path);
        if (!entry.Stamp.Matches(before)) return false;
        var bytes = ReceiptFile.ReadBytes(path); // regular single-link, bounded, verified handle
        return entry.Content == bytes.LongLength + ":" + Hash(bytes) && before.Matches(LedgerStat.Of(path));
    }

    private static bool SameDirectory(string path, LedgerStat expected, long created)
    {
        var actual = LedgerStat.Of(path);
        return expected.Kind == "directory" && actual.Kind == "directory"
            && (expected.Inode != 0 ? expected.Device == actual.Device && expected.Inode == actual.Inode
                : created == Directory.GetCreationTimeUtc(path).Ticks);
    }

    private static List<string> Inventory(string root)
    {
        var entries = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            if (entries.Count >= MaxEntries) throw new IOException("tool inventory limit");
            var stamp = LedgerStat.Of(path);
            if (stamp.Kind is not ("file" or "directory")) throw new IOException("nonregular tool entry");
            entries.Add(path);
            if (stamp.Kind == "directory") pending.Push(path);
        }
        return entries;
    }

    private static bool SafeRelative(string path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path)
        && !path.Contains('\\') && !path.Contains(':')
        && path.Split('/').All(part => part is not ("" or "." or ".."));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new JsonException("duplicate authority property");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RequireUniqueProperties(child);
    }

    public sealed class CopyRecord
    {
        public int Version { get; set; } = 1;
        public string RunId { get; set; } = "";
        public string RunDirectory { get; set; } = "";
        public string ToolDirectory { get; set; } = "";
        public ProcessIdentity Creator { get; set; } = null!;
        public LedgerStat Root { get; set; }
        public long RootCreatedUtcTicks { get; set; }
        public List<CopyEntry> Entries { get; set; } = [];
    }

    public sealed class CopyEntry
    {
        public string Path { get; set; } = "";
        public string Content { get; set; } = "";
        public LedgerStat Stamp { get; set; }
        public long CreatedUtcTicks { get; set; }
    }
}
