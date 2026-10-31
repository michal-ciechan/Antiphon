using System.Text.Json.Nodes;

namespace Antiphon.TestSupport.LiveClaude;

internal sealed record LiveClaudeSettingsSource(string Kind, string Path, string State, string Projection);
internal sealed record LiveClaudeSettingsProjection(string Digest, bool ReuseEligible, IReadOnlyList<LiveClaudeSettingsSource> Sources)
{
    // Deliberately closed diagnostic schema. Unknown CLI keys do not disappear as bookkeeping.
    private static readonly HashSet<string> behavior = new(StringComparer.Ordinal)
    { "model", "remoteControlAtStartup", "disableAllHooks", "permissions", "hooks", "mcpServers", "enabledPlugins", "env",
      "hasTrustDialogAccepted", "hasCompletedProjectOnboarding", "projectOnboardingSeenCount", "allowedTools" };
    private static readonly HashSet<string> credentials = new(StringComparer.Ordinal)
    { "oauthAccount", "accessToken", "refreshToken", "apiKey", "primaryApiKey", "ANTHROPIC_API_KEY", "CLAUDE_CODE_OAUTH_TOKEN" };
    private static readonly HashSet<string> volatileKeys = new(StringComparer.Ordinal) { "numStartups", "lastSessionId", "lastCost", "lastDuration" };

    public static string InputDigest(string path)
    {
        try
        {
            var node=JsonNode.Parse(File.ReadAllText(path));
            return LiveClaudeProfile.Hash(Scrub(node)?.ToJsonString() ?? "null");
        }
        catch(System.Text.Json.JsonException) { return LiveClaudeProfile.Hash("unreadable-json"); }
        static JsonNode? Scrub(JsonNode? node)
        {
            if(node is JsonObject obj)
            {
                var copy=new JsonObject();
                foreach(var pair in obj.OrderBy(p=>p.Key,StringComparer.Ordinal))
                    if(!credentials.Contains(pair.Key)) copy[pair.Key]=Scrub(pair.Value);
                return copy;
            }
            if(node is JsonArray array) return new JsonArray(array.Select(Scrub).ToArray());
            return node?.DeepClone();
        }
    }

    public static LiveClaudeSettingsProjection Read(LiveClaudeProfileContext context, LiveClaudeProfilePaths paths, string[] args,
        Func<string, string>? read = null, string[]? precedence = null, bool effectiveObserved = true, string[]? additionalSources = null)
    {
        var eligible = context.SourcesComplete && effectiveObserved && !string.IsNullOrWhiteSpace(context.EffectiveModel);
        var loaded = new[] { "user", "project", "local" };
        var hit = Array.IndexOf(args, "--setting-sources");
        if (args.Any(a => a.StartsWith("--setting-sources="))) throw new LiveClaudeRefusal("unknown-source-syntax");
        if (hit >= 0)
        {
            if (args.Count(a => a == "--setting-sources") != 1 || hit + 1 >= args.Length) throw new LiveClaudeRefusal("invalid-sources");
            loaded = args[hit + 1].Split(',', StringSplitOptions.RemoveEmptyEntries);
        }
        if (loaded.Any(s => s is not ("user" or "project" or "local")) || loaded.Distinct().Count() != loaded.Length) eligible = false;
        var ordered = precedence ?? loaded.Concat(new[] { "global", "managed" }).ToArray();
        if (!ordered.Order().SequenceEqual(loaded.Concat(new[] { "global", "managed" }).Order())) throw new LiveClaudeRefusal("missing-source");
        var files = new Dictionary<string, string>
        {
            ["user"] = Path.Combine(paths.ConfigDir, "settings.json"),
            ["project"] = Path.Combine(paths.Cwd, ".claude", "settings.json"),
            ["local"] = Path.Combine(paths.Cwd, ".claude", "settings.local.json"),
            ["global"] = paths.GlobalState, ["managed"] = context.ManagedPolicyPath
        };
        var sources = new List<LiveClaudeSettingsSource>();
        foreach (var kind in ordered)
        {
            if (!files.TryGetValue(kind, out var file)) { eligible = false; continue; }
            Add(kind, file);
        }
        var overlays = args.Select((a, i) => (a, i)).Where(x => x.a == "--settings").ToArray();
        if (overlays.Length > 1 || args.Any(a => a.StartsWith("--settings="))) throw new LiveClaudeRefusal("unknown-overlay-syntax");
        foreach (var overlay in overlays)
        {
            if (overlay.i + 1 >= args.Length) throw new LiveClaudeRefusal("missing-overlay");
            Add("overlay", args[overlay.i + 1]);
        }
        foreach (var extra in additionalSources ?? []) { eligible = false; sources.Add(new(extra, "unresolved", "unknown", "")); }
        var envProjection = new JsonObject();
        foreach (var name in new[] { "CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN", "DISABLE_AUTOUPDATER", "ANTHROPIC_BASE_URL" })
            envProjection[name] = context.Environment?.TryGetValue(name, out var value) == true ? "value:" + value : "absent";
        var canonical = string.Join("\n", sources.Select(s => $"{s.Kind}|{s.Path}|{s.State}|{s.Projection}"))
            + $"\n{context.ModelMode}|{context.RequestedModel}|{context.EffectiveModel}|{envProjection}";
        return new(LiveClaudeProfile.Hash(canonical), eligible, sources);

        void Add(string kind, string file)
        {
            var state = "absent";
            var projection = "";
            if (kind == "managed" && !context.ManagedLocationProven) { state = "absent-unverified"; eligible = false; }
            try
            {
                if (File.Exists(file))
                {
                    LiveClaudeProfilePaths.Owned(file, kind == "managed" ? Path.GetDirectoryName(context.ManagedPolicyPath)! : context.OwnedRoot);
                    var text = (read ?? File.ReadAllText)(file);
                    var node = JsonNode.Parse(text) as JsonObject ?? throw new System.Text.Json.JsonException();
                    state = node.Count == 0 ? "empty" : "value";
                    projection = Project(node, kind == "global", false).ToJsonString();
                    if (kind == "managed" && !context.ManagedLocationProven) { state = "unverified"; eligible = false; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { state = "unreadable"; eligible = false; }
            sources.Add(new(kind, file, state, projection));
        }
        JsonObject Project(JsonObject obj, bool global, bool ownedProject)
        {
            var result = new JsonObject();
            foreach (var pair in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (credentials.Contains(pair.Key)) continue;
                if (volatileKeys.Contains(pair.Key)) continue;
                if (global && pair.Key == "projects" && pair.Value is JsonObject projects)
                {
                    var p = new JsonObject();
                    foreach (var project in projects.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        var owned = project.Key == Antiphon.Agents.Pty.ClaudeProjectTrust.ProjectKey(paths.Cwd);
                        if (project.Value is not JsonObject value) { eligible = false; continue; }
                        p[owned ? "$ownedWorkspace" : project.Key] = Project(value, false, owned);
                    }
                    result[pair.Key] = p;
                    continue;
                }
                if (!behavior.Contains(pair.Key)) { eligible = false; continue; }
                if (ownedProject && pair.Key == "hasTrustDialogAccepted") continue;
                if (!KnownNested(pair.Key,pair.Value)) { eligible=false; continue; }
                result[pair.Key] = Clean(pair.Value);
            }
            return result;
        }
        bool KnownNested(string kind,JsonNode? value)
        {
            if (kind == "permissions" && value is JsonObject permissions)
                return permissions.All(p=>p.Key is "allow" or "deny" or "ask" or "defaultMode" or "additionalDirectories" or "disableBypassPermissionsMode");
            if (kind == "env" && value is JsonObject environment)
                return environment.All(p=>credentials.Contains(p.Key) || p.Key is "DISABLE_AUTOUPDATER" or "CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN" or "ANTHROPIC_BASE_URL");
            if (kind == "hooks") return Hook(value);
            return true;
            bool Hook(JsonNode? node)
            {
                if(node is JsonArray array) return array.All(Hook);
                if(node is not JsonObject obj) return true;
                return obj.All(p=>(p.Key is "SessionStart" or "SessionEnd" or "PreToolUse" or "PostToolUse" or "UserPromptSubmit" or "Stop" or "SubagentStart" or "SubagentStop"
                    or "Notification" or "PermissionRequest" or "PreCompact" or "PostCompact" or "Setup" or "matcher" or "hooks" or "type" or "command" or "timeout" or "async" or "statusMessage" or "once") && Hook(p.Value));
            }
        }
        JsonNode? Clean(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                var copy = new JsonObject();
                foreach (var pair in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                    if (!credentials.Contains(pair.Key)) copy[pair.Key] = Clean(pair.Value);
                return copy;
            }
            if (node is JsonArray array) return new JsonArray(array.Select(Clean).ToArray());
            return node?.DeepClone();
        }
    }
}
