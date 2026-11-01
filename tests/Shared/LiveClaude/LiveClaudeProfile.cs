using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Antiphon.TestSupport.LiveClaude;

internal sealed record LiveClaudeProfileSpec(string[] Args, Dictionary<string, string> Env, string Cwd, int Cols = 120, int Rows = 30);
internal sealed class LiveClaudeProfile : IDisposable
{
    public LiveClaudeProfileContext Context { get; }
    public LiveClaudeProfilePaths Paths { get; }
    private readonly LiveClaudeOwnedTrust trust;
    private readonly string instructions;
    private string[]? argv;
    private LiveClaudeSettingsProjection? before;
    private bool disposed;
    public LiveClaudeProfile(LiveClaudeProfileContext context)
    {
        Context = context;
        Paths = new(context);
        ValidateModel();
        instructions = InstructionDigest();
        trust = new(Paths);
    }
    private void ValidateModel()
    {
        if (Context.ModelMode is not ("named" or "production-default")) throw new LiveClaudeRefusal("missing-model");
        if (Context.ModelMode == "named" && string.IsNullOrWhiteSpace(Context.RequestedModel)) throw new LiveClaudeRefusal("missing-model");
        if (Context.ModelMode == "production-default" && Context.RequestedModel is not null) throw new LiveClaudeRefusal("invalid-model");
        if (string.IsNullOrWhiteSpace(Context.EffectiveModel)) throw new LiveClaudeRefusal("unobserved-model");
    }
    public string[] Arguments(string role, string session, string? overlay)
    {
        ValidateModel();
        var args = new List<string> { "--dangerously-skip-permissions", "--session-id", session };
        if (Context.ModelMode == "named") args.AddRange(["--model", Context.RequestedModel!]);
        if (role is "off" or "slash")
        {
            if (overlay is null) throw new LiveClaudeRefusal("missing-overlay");
            LiveClaudeProfilePaths.Owned(overlay, Context.OwnedRoot);
            var node = JsonNode.Parse(File.ReadAllText(overlay))!.AsObject();
            if (node.Count != 1 || node["remoteControlAtStartup"]?.GetValue<bool>() != false)
                throw new LiveClaudeRefusal("invalid-overlay");
            args.AddRange(["--settings", overlay]);
        }
        else if (overlay is not null) throw new LiveClaudeRefusal("control-overlay");
        return args.ToArray();
    }
    public Dictionary<string, string> Environment()
    {
        var env = new Dictionary<string, string>(Context.Environment ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        env["CLAUDE_CONFIG_DIR"] = Paths.ConfigDir;
        env["CLAUDE_CODE_CHILD_SESSION"] = "";
        env["CLAUDE_CODE_SESSION_ID"] = "";
        env["CLAUDE_CODE_BRIDGE_SESSION_ID"] = "";
        env["CLAUDECODE"] = "";
        env["CLAUDE_CODE_ENTRYPOINT"] = "";
        env.TryAdd("DISABLE_AUTOUPDATER", "1");
        env.TryAdd("CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN", "1");
        return env;
    }
    public LiveClaudeProfileSpec Build(string role, string session, string? overlay)
    {
        var args = Arguments(role, session, overlay);
        Capture(args);
        return new(args, Environment(), Paths.Cwd, role == "submit" ? 200 : 120, role == "submit" ? 60 : 30);
    }
    public void Capture(string[] actualArgs)
    {
        if (InstructionDigest() != instructions) throw new LiveClaudeRefusal("instruction-drift");
        argv = actualArgs.ToArray();
        before = Project(actualArgs);
    }
    public LiveClaudeSettingsProjection Project(string[] actualArgs)
        => LiveClaudeSettingsProjection.Read(Context, Paths, actualArgs);
    public void Complete()
    {
        if (InstructionDigest() != instructions) throw new LiveClaudeRefusal("instruction-drift");
        if (argv is not null && before!.Digest != Project(argv).Digest) throw new LiveClaudeRefusal("settings-drift");
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { Complete(); } finally { trust.Dispose(); }
    }
    private string InstructionDigest()
    {
        var observed = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        for (var dir = new DirectoryInfo(Paths.Cwd); dir is not null; dir = dir.Parent)
        {
            foreach (var name in new[] { "CLAUDE.md", "AGENTS.md", ".mcp.json" })
            { var file = Path.Combine(dir.FullName, name); if (File.Exists(file)) queue.Enqueue(file); }
            var dot = Path.Combine(dir.FullName, ".claude");
            if (!Directory.Exists(dot)) continue;
            LiveClaudeProfilePaths.Owned(dot, Context.OwnedRoot);
            if ((File.GetAttributes(dot) & FileAttributes.ReparsePoint) != 0) throw new LiveClaudeRefusal("linked-instructions");
            Enumerate(dot);
        }
        // User-level instruction/hook/skill inputs are inspected inside the explicit profile too.
        foreach (var name in new[] { "CLAUDE.md", "AGENTS.md", "hooks", "skills" })
        {
            var p = Path.Combine(Paths.ConfigDir, name);
            if (Directory.Exists(p)) Enumerate(p);
            else if (File.Exists(p)) queue.Enqueue(p);
        }
        var userSettings = Path.Combine(Paths.ConfigDir, "settings.json");
        if (File.Exists(userSettings)) queue.Enqueue(userSettings);
        while (queue.TryDequeue(out var path))
        {
            path = Path.GetFullPath(path);
            if (observed.ContainsKey(path)) continue;
            if (!Context.DeclaredFiles.TryGetValue(path, out var declared)) throw new LiveClaudeRefusal("unowned-instructions");
            LiveClaudeProfilePaths.Owned(path, Context.OwnedRoot);
            var hash = InputDigest(path);
            if (hash != declared) throw new LiveClaudeRefusal("instruction-drift");
            observed[path] = hash;
            if (Path.GetExtension(path) == ".json")
            {
                var json = JsonNode.Parse(File.ReadAllText(path));
                Commands(json);
            }
            if (Path.GetExtension(path) != ".md") continue;
            foreach (var token in File.ReadAllText(path).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(t => t.StartsWith('@')))
                queue.Enqueue(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, token[1..])));
        }
        return Hash(string.Join("\n", observed.Select(p => p.Key + "=" + p.Value)));
        void Commands(JsonNode? node)
        {
            if (node is JsonArray array) { foreach (var child in array) Commands(child); return; }
            if (node is not JsonObject obj) return;
            foreach (var pair in obj)
            {
                if (pair.Key == "command" && pair.Value is JsonValue value && value.TryGetValue<string>(out var command))
                {
                    // This diagnostic slice admits only the declared benign marker hook shape.
                    // Other shell/MCP command grammars remain unresolved and cannot get admission.
                    RequireHookCommand(command);
                    var tokens = HookTokens(command);
                    LiveClaudeProfilePaths.Owned(tokens[2], Context.OwnedRoot);
                    queue.Enqueue(tokens[0]);
                    queue.Enqueue(tokens[1]);
                }
                else Commands(pair.Value);
            }
        }
        void Enumerate(string dir)
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(dir))
            {
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new LiveClaudeRefusal("linked-instructions");
                if (Directory.Exists(item)) Enumerate(item); else queue.Enqueue(item);
            }
        }
    }
    public static string[] HookTokens(string command) => System.Text.RegularExpressions.Regex.Matches(command, "\"[^\"]*\"|[^\\s\"]+")
        .Select(m => m.Value.Trim('"')).ToArray();

    public static string HookCommand(string interpreter, string writer, string marker, string source, string retained)
    {
        if (interpreter is "node" or "node.exe") throw new LiveClaudeRefusal("unowned-instructions");
        if (!Path.IsPathFullyQualified(interpreter) || !Path.IsPathFullyQualified(writer) || !Path.IsPathFullyQualified(marker))
            throw new LiveClaudeRefusal("unowned-instructions");
        if (Path.GetFileName(writer) != "settings-source-witness.mjs") throw new LiveClaudeRefusal("unowned-instructions");
        if (source is not ("user" or "project" or "local")) throw new LiveClaudeRefusal("unowned-instructions");
        if (new[] { interpreter, writer, marker, source, retained }.Any(t => t.IndexOfAny([';', '|', '&', '`', '$', '\n', '\r']) >= 0))
            throw new LiveClaudeRefusal("unowned-instructions");
        return $"\"{interpreter}\" \"{writer}\" \"{marker}\" {source} \"{retained}\"";
    }

    public static void RequireHookCommand(string command)
    {
        var tokens = HookTokens(command);
        if (tokens.Length != 5) throw new LiveClaudeRefusal("unowned-instructions");
        var expected = HookTokens(HookCommand(tokens[0], tokens[1], tokens[2], tokens[3], tokens[4]));
        if (!tokens.SequenceEqual(expected, StringComparer.Ordinal)) throw new LiveClaudeRefusal("unowned-instructions");
    }

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public static string InputDigest(string path) => Path.GetExtension(path)==".json" ? LiveClaudeSettingsProjection.InputDigest(path) : HashFile(path);
}
