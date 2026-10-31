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
    public static bool Within(string path, string root)
        => Path.GetFullPath(path).Equals(Path.GetFullPath(root), StringComparison.Ordinal)
           || Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    public static string Owned(string path, string root)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(root) || !Within(path, root))
            throw new LiveClaudeRefusal("unowned-path");
        for (var p = Path.GetFullPath(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
        {
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new LiveClaudeRefusal("linked-path");
        }
        return Path.GetFullPath(path);
    }
    public string? FindSessionJsonl(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out _)) throw new LiveClaudeRefusal("invalid-session");
        if (!Directory.Exists(Projects)) return null;
        return Directory.EnumerateFiles(Projects, sessionId + ".jsonl", SearchOption.AllDirectories)
            .Select(p => Owned(p, ConfigDir)).SingleOrDefault();
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
        if (!existed) projects.Remove(key);
        else if (priorFlag is null) projects[key]!.AsObject().Remove("hasTrustDialogAccepted");
        else projects[key]!["hasTrustDialogAccepted"] = priorFlag.DeepClone();
        File.WriteAllText(paths.GlobalState, root.ToJsonString());
    }
}
