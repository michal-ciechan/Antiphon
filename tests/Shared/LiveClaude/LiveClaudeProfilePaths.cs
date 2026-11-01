using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Agents.Pty;

namespace Antiphon.TestSupport.LiveClaude;

internal sealed class LiveClaudeProfilePaths
{
    public string ConfigDir { get; }
    public string Cwd { get; }
    public string GlobalState => ClaudeProjectTrust.DefaultConfigPath(ConfigDir);
    public string Projects => Path.Combine(ConfigDir, "projects");
    public string Sessions => Path.Combine(ConfigDir, "sessions");
    public LiveClaudeProfilePaths(LiveClaudeProfileContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ConfigDir)) throw new LiveClaudeRefusal("missing-config");
        ConfigDir = Owned(context.ConfigDir, context.OwnedRoot);
        Cwd = Owned(context.Cwd, context.OwnedRoot);
        if (Within(Cwd, context.CandidateRoot) || Within(ConfigDir, context.CandidateRoot))
            throw new LiveClaudeRefusal("candidate-cwd");
    }
    public static bool Within(string path, string root) => Within(path, root, LiveClaudeOwnershipRules.Platform);
    internal static bool Within(string path, string root, LiveClaudeOwnershipRules rules)
    {
        var canonicalPath = TrimEnding(rules.Canonicalize(path));
        var canonicalRoot = TrimEnding(rules.Canonicalize(root));
        if (canonicalPath.Equals(canonicalRoot, rules.Comparison)) return true;
        var separator = canonicalRoot.Contains('\\') ? '\\' : '/';
        var prefix = canonicalRoot.EndsWith(separator) ? canonicalRoot : canonicalRoot + separator;
        return canonicalPath.StartsWith(prefix, rules.Comparison);
    }
    public static string Owned(string path, string root) => Owned(path, root, LiveClaudeOwnershipRules.Platform);
    internal static string Owned(string path, string root, LiveClaudeOwnershipRules rules)
    {
        if (!rules.IsFullyQualified(path) || !rules.IsFullyQualified(root) || !Within(path, root, rules))
            throw new LiveClaudeRefusal("unowned-path");
        var canonical = rules.Canonicalize(path);
        if (rules.CheckReparse)
        {
            for (var p = canonical; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            {
                if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new LiveClaudeRefusal("linked-path");
            }
        }
        return canonical;
    }
    private static string TrimEnding(string path)
    {
        if (path.Length <= 1) return path;
        var trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 0) return path[0] == '/' ? "/" : path;
        if (trimmed.Length == 2 && trimmed[1] == ':') return trimmed + "\\";
        return trimmed;
    }
    public string? FindSessionJsonl(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out _)) throw new LiveClaudeRefusal("invalid-session");
        if (!Directory.Exists(Projects)) return null;
        return Walk(Projects).Where(p=>Path.GetFileName(p)==sessionId+".jsonl").SingleOrDefault();
        IEnumerable<string> Walk(string directory)
        {
            foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                Owned(entry,ConfigDir);
                if(Directory.Exists(entry)) { foreach(var nested in Walk(entry)) yield return nested; }
                else yield return entry;
            }
        }
    }
    public (bool Found, string? BridgeId) ReadSessionFile(string sessionId, string cwd)
    {
        if (!Directory.Exists(Sessions)) return (false, null);
        foreach (var file in Directory.EnumerateFiles(Sessions, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Owned(file, ConfigDir)));
            var r = doc.RootElement;
            if (r.GetProperty("sessionId").GetString() != sessionId) continue;
            if (Path.GetFullPath(r.GetProperty("cwd").GetString()!) != Path.GetFullPath(cwd)) continue;
            return (true, r.TryGetProperty("bridgeSessionId", out var id) ? id.GetString() : null);
        }
        return (false, null);
    }
}

// Both sides are canonicalised the same way. Windows compares case-insensitively; a separator
// boundary keeps a sibling such as C:\root2 outside C:\root. Callers inject a Windows-shaped
// canonicaliser where the host cannot expand 8.3 names or backslashes.
internal sealed class LiveClaudeOwnershipRules
{
    internal static readonly LiveClaudeOwnershipRules Platform = new(
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal,
        static path => Path.GetFullPath(path),
        static path => Path.IsPathFullyQualified(path),
        checkReparse: true);
    internal LiveClaudeOwnershipRules(StringComparison comparison, Func<string, string> canonicalize,
        Func<string, bool> isFullyQualified, bool checkReparse)
    {
        Comparison = comparison;
        Canonicalize = canonicalize;
        IsFullyQualified = isFullyQualified;
        CheckReparse = checkReparse;
    }
    internal StringComparison Comparison { get; }
    internal Func<string, string> Canonicalize { get; }
    internal Func<string, bool> IsFullyQualified { get; }
    internal bool CheckReparse { get; }
    internal static LiveClaudeOwnershipRules WindowsShape(Func<string, string> canonicalize) =>
        new(StringComparison.OrdinalIgnoreCase, canonicalize, WindowsQualified, checkReparse: false);
    internal static bool WindowsQualified(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            return true;
        return path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);
    }
}

// Exclusive lane-profile ownership is a precondition; no runner/operator profile is discovered here.
internal sealed class LiveClaudeOwnedTrust : IDisposable
{
    private readonly LiveClaudeProfilePaths paths;
    private readonly string key;
    private readonly bool existed;
    private readonly JsonNode? priorFlag;
    private bool disposed;
    public LiveClaudeOwnedTrust(LiveClaudeProfilePaths paths)
    {
        this.paths = paths;
        key = ClaudeProjectTrust.ProjectKey(paths.Cwd);
        var root = JsonNode.Parse(File.ReadAllText(paths.GlobalState))!.AsObject();
        var projects = root["projects"] as JsonObject ?? new JsonObject();
        existed = projects[key] is JsonObject;
        priorFlag = projects[key]?["hasTrustDialogAccepted"]?.DeepClone();
        if (root["projects"] is null) root["projects"] = projects;
        if (projects[key] is null) projects[key] = new JsonObject();
        projects[key]!["hasTrustDialogAccepted"] = true;
        File.WriteAllText(paths.GlobalState, root.ToJsonString());
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var root = JsonNode.Parse(File.ReadAllText(paths.GlobalState))!.AsObject();
        var projects = root["projects"]!.AsObject();
        if (!existed)
        {
            var created = projects[key]!.AsObject();
            created.Remove("hasTrustDialogAccepted");
            if (created.Count == 0) projects.Remove(key);
        }
        else if (priorFlag is null) projects[key]!.AsObject().Remove("hasTrustDialogAccepted");
        else projects[key]!["hasTrustDialogAccepted"] = priorFlag.DeepClone();
        File.WriteAllText(paths.GlobalState, root.ToJsonString());
    }
}
