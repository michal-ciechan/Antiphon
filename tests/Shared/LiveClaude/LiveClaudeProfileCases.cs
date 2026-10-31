using System.Text.Json.Nodes;
using Antiphon.Agents.Pty;

namespace Antiphon.TestSupport.LiveClaude;

internal sealed class LiveClaudeProfileFixture : IDisposable
{
    public string Root { get; }
    public string Ambient { get; }
    public string Session { get; } = "15450000-0000-0000-0000-000000000001";
    public string Overlay => Path.Combine(Root, "off.json");
    public Dictionary<string, string> Declared { get; } = new(StringComparer.Ordinal);
    public LiveClaudeProfileContext Context { get; set; }
    private readonly bool keep;
    public LiveClaudeProfileFixture(string? root = null, string mode = "named-A")
    {
        keep = root is not null;
        Root = root is null ? Directory.CreateTempSubdirectory("c1545-profile-").FullName : Directory.CreateDirectory(root).FullName;
        Ambient = Directory.CreateDirectory(Path.Combine(Root, "ambient")).FullName;
        var config = Directory.CreateDirectory(Path.Combine(Root, "profile")).FullName;
        var cwd = Directory.CreateDirectory(Path.Combine(Root, "workspace")).FullName;
        Directory.CreateDirectory(Path.Combine(cwd, ".claude"));
        Directory.CreateDirectory(Path.Combine(config, "projects", "owned"));
        Directory.CreateDirectory(Path.Combine(config, "sessions"));
        File.WriteAllText(Path.Combine(config, ".claude.json"), "{\"numStartups\":1,\"projects\":{\"sibling\":{\"model\":\"sibling-model\"}}}");
        foreach (var p in new[] { Path.Combine(config, "settings.json"), Path.Combine(cwd, ".claude", "settings.json"), Path.Combine(cwd, ".claude", "settings.local.json"), Path.Combine(Root, "managed-settings.json") })
            Write(p, "{\"remoteControlAtStartup\":true}", declare: !p.EndsWith("managed-settings.json"));
        File.WriteAllText(Overlay, "{\"remoteControlAtStartup\":false}");
        Context = new(Root, LiveClaudeExecutableAudit.Root(), config, cwd, Path.Combine(Root, "managed-settings.json"), true,
            mode == "production-default" ? mode : "named", mode == "production-default" ? null : mode,
            mode == "production-default" ? "observed-default" : mode, Declared, true);
    }
    public void Write(string path, string text, bool declare = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        if (declare) Declared[path] = LiveClaudeProfile.InputDigest(path);
    }
    public void Dispose() { if (!keep) Directory.Delete(Root, true); }
}

internal static class LiveClaudeProfileCases
{
    internal delegate LiveClaudeProfileSpec Builder(LiveClaudeProfile profile, string role, string session, string? overlay);
    internal delegate LiveClaudeSettingsProjection Projector(LiveClaudeProfile profile, string[] args);
    public static void Assert(bool condition, string v, string variant, string predicate)
    { if (!condition) throw new InvalidOperationException($"C1545 {v}/{variant} {predicate}"); }
    public static void Refuses(Action action, string reason, string v, string variant)
    {
        try { action(); }
        catch (LiveClaudeRefusal e) when (e.Reason == reason) { return; }
        throw new InvalidOperationException($"C1545 {v}/{variant} expected typed {reason} refusal");
    }
    public static void Run(string v, Builder? build = null,
        Func<LiveClaudeProfilePaths, string, string?>? find = null,
        Func<LiveClaudeProfilePaths, string, string, (bool Found, string? BridgeId)>? read = null, Projector? project = null, string? variant = null)
    {
        build ??= (p, r, s, o) => p.Build(r, s, o);
        project ??= (p,a) => p.Project(a);
        if (v is "V-7" or "V-13") Dedicated(v, build, find ?? ((p,s) => p.FindSessionJsonl(s)), read ?? ((p,s,c) => p.ReadSessionFile(s,c)));
        else if (v is "V-8" or "V-14") Instructions(v, build, variant);
        else if (v is "V-9" or "V-15") { Drift(v, build, project); if(v=="V-15") Models(v,build); }
        else if (v is "V-10" or "V-16") Unknown(v, build, project);
        else if (v == "V-11") Arguments(v, build);
        else if (v == "V-12") Models(v, build, variant);
        else throw new InvalidOperationException("C1545 unknown witness " + v);
    }
    private static void Dedicated(string v, Builder build,
        Func<LiveClaudeProfilePaths,string,string?> find,
        Func<LiveClaudeProfilePaths,string,string,(bool Found,string? BridgeId)> read)
    {
        foreach (var preexisting in new[] { false, true })
        {
            using var f = new LiveClaudeProfileFixture();
            var paths = new LiveClaudeProfilePaths(f.Context);
            Assert(paths.GlobalState==Path.Combine(f.Context.ConfigDir!,".claude.json"),v,"trust","global state is independently rooted in dedicated config");
            Assert(paths.Projects==Path.Combine(f.Context.ConfigDir!,"projects"),v,"projects","projects are independently rooted in dedicated config");
            Assert(paths.Sessions==Path.Combine(f.Context.ConfigDir!,"sessions"),v,"sessions","sidecars are independently rooted in dedicated config");
            var key = ClaudeProjectTrust.ProjectKey(paths.Cwd);
            if (preexisting)
            {
                var root = JsonNode.Parse(File.ReadAllText(paths.GlobalState))!;
                root["projects"]![key] = new JsonObject { ["model"] = "existing", ["hasTrustDialogAccepted"] = false, ["allowedTools"] = new JsonArray("Read") };
                File.WriteAllText(paths.GlobalState, root.ToJsonString());
            }
            var before = JsonNode.Parse(File.ReadAllText(paths.GlobalState));
            var ambient = Path.Combine(f.Ambient, ".claude.json");
            File.WriteAllText(ambient, "{\"projects\":{\"ambient\":{\"hasTrustDialogAccepted\":false}}}");
            var ambientBefore = File.ReadAllText(ambient);
            var transcript = Path.Combine(paths.Projects, "owned", f.Session + ".jsonl");
            f.Write(transcript, "{\"type\":\"bridge-session\",\"bridgeSessionId\":\"dedicated\"}", false);
            Directory.CreateDirectory(Path.Combine(f.Ambient, "projects"));
            File.WriteAllText(Path.Combine(f.Ambient, "projects", f.Session + ".jsonl"), "ambient-decoy");
            f.Write(Path.Combine(paths.Sessions, "1.json"), $$"""{"sessionId":"foreign","cwd":{{System.Text.Json.JsonSerializer.Serialize(paths.Cwd)}},"bridgeSessionId":"foreign"}""", false);
            using (var profile = new LiveClaudeProfile(f.Context))
            {
                Assert(ClaudeProjectTrust.IsTrusted(paths.Cwd, paths.GlobalState), v, "trust", "actual owned trust was seeded");
                Assert(JsonNode.DeepEquals(before!["projects"]!["sibling"],JsonNode.Parse(File.ReadAllText(paths.GlobalState))!["projects"]!["sibling"]),v,"trust-siblings","seed preserves sibling project behavior");
                var launch = build(profile, "control", f.Session, null);
                Assert(launch.Env["CLAUDE_CONFIG_DIR"] == paths.ConfigDir, v, "profile", "actual launch uses dedicated config");
                Assert(find(paths, f.Session) == transcript, v, "projects", "reader uses dedicated projects");
                Assert(!read(paths, f.Session, paths.Cwd).Found, v, "foreign-session", "same cwd cannot admit a foreign session");
                File.Delete(Path.Combine(paths.Sessions, "1.json"));
                f.Write(Path.Combine(paths.Sessions, "2.json"), $$"""{"sessionId":"{{f.Session}}","cwd":{{System.Text.Json.JsonSerializer.Serialize(paths.Cwd)}},"bridgeSessionId":"owned"}""", false);
                Assert(read(paths, f.Session, paths.Cwd) == (true, "owned"), v, "sessions", "reader uses dedicated sidecar");
            }
            Assert(JsonNode.DeepEquals(before, JsonNode.Parse(File.ReadAllText(paths.GlobalState))), v,
                preexisting ? "restore-existing" : "restore-owned", "owned trust restored with siblings and behavior intact");
            Assert(File.ReadAllText(ambient) == ambientBefore, v, "ambient", "ambient bytes unchanged");
            Refuses(() => _ = new LiveClaudeProfilePaths(f.Context with { ConfigDir = null }), "missing-config", v, "missing-config");
        }
        using (var f=new LiveClaudeProfileFixture())
        {
            var paths=new LiveClaudeProfilePaths(f.Context);
            var key=ClaudeProjectTrust.ProjectKey(paths.Cwd);
            using(var profile=new LiveClaudeProfile(f.Context))
            {
                var node=JsonNode.Parse(File.ReadAllText(paths.GlobalState))!;
                node["projects"]![key]!["model"]="independently-added";
                File.WriteAllText(paths.GlobalState,node.ToJsonString());
                build(profile,"control",f.Session,null);
            }
            var after=JsonNode.Parse(File.ReadAllText(paths.GlobalState))!;
            Assert(after["projects"]![key]!["model"]?.GetValue<string>()=="independently-added" && after["projects"]![key]!["hasTrustDialogAccepted"] is null,v,"restore-narrow","cleanup removes only its own trust change");
        }
    }
    private static void Instructions(string v, Builder build, string? variant = null)
    {
        foreach (var name in variant is null ? new[] { "CLAUDE.md", "AGENTS.md", "import.md", ".claude/settings.json", ".claude/skills/s/SKILL.md", ".mcp.json" } : new[] { variant })
        {
            using var f = new LiveClaudeProfileFixture();
            using (var healthy = new LiveClaudeProfile(f.Context))
            {
                var spec = build(healthy, "control", f.Session, null);
                Assert(spec.Cwd == f.Context.Cwd && !LiveClaudeProfilePaths.Within(spec.Cwd, f.Context.CandidateRoot), v, "owned-cwd", "actual launch has owned non-candidate cwd");
            }
            var path = Path.Combine(f.Root, name);
            f.Write(path, name.EndsWith(".json") ? "{}" : "synthetic unowned instructions", false);
            if (name == "import.md") f.Write(Path.Combine(f.Root, "CLAUDE.md"), "@import.md");
            Refuses(() => { using var p = new LiveClaudeProfile(f.Context); build(p,"control",f.Session,null); }, "unowned-instructions", v, "ancestor-" + name);
            f.Declared[path] = LiveClaudeProfile.HashFile(path);
            using var declared = new LiveClaudeProfile(f.Context);
            build(declared, "control", f.Session, null);
            File.WriteAllText(path, "changed-after-admission");
            Refuses(declared.Complete, "instruction-drift", v, "instruction-drift");
            File.WriteAllText(path, name.EndsWith(".json") ? "{}" : "synthetic unowned instructions");
        }
        if (variant is null)
        {
            using var f=new LiveClaudeProfileFixture();
            var runtime=Path.Combine(f.Root,"owned-node");
            var writer=Path.Combine(f.Context.ConfigDir!,"hooks","settings-source-witness.mjs");
            var output=Path.Combine(f.Root,"hook-observations.jsonl");
            f.Write(runtime,"inert owned interpreter",false);
            f.Write(writer,"// inert declared marker writer",false);
            var command=LiveClaudeProfile.HookCommand(runtime,writer,output,"user","fixture-retained");
            var settings=new JsonObject { ["hooks"]=new JsonObject { ["SessionStart"]=new JsonArray(new JsonObject { ["hooks"]=new JsonArray(new JsonObject { ["type"]="command", ["command"]=command }) }) } };
            f.Write(Path.Combine(f.Context.ConfigDir!,"settings.json"),settings.ToJsonString());
            Refuses(()=>{using var p=new LiveClaudeProfile(f.Context);},"unowned-instructions",v,"undeclared-hook");
            f.Declared[runtime]=LiveClaudeProfile.HashFile(runtime);
            f.Declared[writer]=LiveClaudeProfile.HashFile(writer);
            using (var declaredHook=new LiveClaudeProfile(f.Context))
            {
                build(declaredHook,"control",f.Session,null);
                Assert(declaredHook.Project([]).ReuseEligible,v,"declared-hook","a fully declared benign hook is admissible over owned inert files");
            }
            var wrong=Path.Combine(f.Context.ConfigDir!,"hooks","other-witness.mjs");
            f.Write(wrong,"// inert other writer",false);
            f.Declared[wrong]=LiveClaudeProfile.HashFile(wrong);
            var bad=$"\"{runtime}\" \"{wrong}\" \"{output}\" user fixture-retained";
            f.Write(Path.Combine(f.Context.ConfigDir!,"settings.json"),new JsonObject{["hooks"]=new JsonObject{["SessionStart"]=new JsonArray(new JsonObject{["hooks"]=new JsonArray(new JsonObject{["type"]="command",["command"]=bad})})}}.ToJsonString());
            Refuses(()=>{using var rejected=new LiveClaudeProfile(f.Context);},"unowned-instructions",v,"hook-script-name");
            var bare=$"node \"{writer}\" \"{output}\" user fixture-retained";
            f.Write(Path.Combine(f.Context.ConfigDir!,"settings.json"),new JsonObject{["hooks"]=new JsonObject{["SessionStart"]=new JsonArray(new JsonObject{["hooks"]=new JsonArray(new JsonObject{["type"]="command",["command"]=bare})})}}.ToJsonString());
            Refuses(()=>{using var rejected=new LiveClaudeProfile(f.Context);},"unowned-instructions",v,"bare-node");
        }
    }
    private static void Drift(string v, Builder build, Projector project)
    {
        using var f = new LiveClaudeProfileFixture();
        using var p = new LiveClaudeProfile(f.Context);
        var launch = build(p, "off", f.Session, f.Overlay);
        var original = project(p, launch.Args);
        Assert(original.Sources.Count == 6 && original.ReuseEligible, v, "inventory", "all six actual loaded sources are measured");
        foreach (var source in original.Sources)
        {
            var old = File.ReadAllText(source.Path);
            try
            {
                var node = JsonNode.Parse(old)!.AsObject();
                node["model"] = "drift-" + source.Kind;
                File.WriteAllText(source.Path, node.ToJsonString());
                Assert(project(p, launch.Args).Digest != original.Digest, v, "drift-" + source.Kind, "behavior edit changes digest without revision bump");
            }
            finally { File.WriteAllText(source.Path, old); }
        }
        var reordered = LiveClaudeSettingsProjection.Read(f.Context, p.Paths, launch.Args, precedence: ["local", "project", "user", "global", "managed"]);
        Assert(reordered.Digest != original.Digest, v, "precedence", "effective source precedence changes digest");
        Refuses(() => LiveClaudeSettingsProjection.Read(f.Context, p.Paths, launch.Args, precedence: ["project", "local", "global", "managed"]), "missing-source", v, "missing-source");
        var captured = new[] { "--setting-sources", "project,local" };
        var loaded = project(p, captured);
        Assert(loaded.Sources.All(s => s.Kind != "user") && loaded.Sources.Count == 4, v, "loaded-set", "source set derives from actual argv");
        var global = JsonNode.Parse(File.ReadAllText(p.Paths.GlobalState))!;
        try
        {
            global["projects"]!["sibling"]!["model"] = "changed-sibling";
            File.WriteAllText(p.Paths.GlobalState, global.ToJsonString());
            Assert(project(p, launch.Args).Digest != original.Digest, v, "trust-behavior", "only owned trust is normalized");
            Refuses(p.Complete, "settings-drift", v, "settings-drift");
        }
        finally
        {
            global["projects"]!["sibling"]!["model"] = "sibling-model";
            File.WriteAllText(p.Paths.GlobalState, global.ToJsonString());
        }
    }
    private static void Unknown(string v, Builder build, Projector project)
    {
        using var f = new LiveClaudeProfileFixture();
        using var p = new LiveClaudeProfile(f.Context);
        var launch = build(p, "control", f.Session, null);
        var original = project(p, launch.Args);
        var user = Path.Combine(p.Paths.ConfigDir, "settings.json");
        var old = File.ReadAllText(user);
        var managed = File.ReadAllText(f.Context.ManagedPolicyPath);
        var globalBefore = File.ReadAllText(p.Paths.GlobalState);
        try
        {
        File.WriteAllText(user, "{\"remoteControlAtStartup\":true,\"newBehavior\":true}");
        Assert(!project(p, launch.Args).ReuseEligible, v, "unknown-key", "unknown behavior disables reuse");
        File.WriteAllText(user,"{\"permissions\":{\"futurePolicy\":true}}");
        Assert(!project(p, launch.Args).ReuseEligible,v,"unknown-nested-key","unknown nested behavior disables reuse");
        File.WriteAllText(user, old);
        Assert(!LiveClaudeSettingsProjection.Read(f.Context,p.Paths,launch.Args,effectiveObserved:false).ReuseEligible, v, "unknown-effective", "unknown effective settings disable reuse");
        Assert(!LiveClaudeSettingsProjection.Read(f.Context,p.Paths,launch.Args,additionalSources:["unresolved-server-policy"]).ReuseEligible, v, "unknown-source", "unknown additional source disables reuse");
        var unreadable = LiveClaudeSettingsProjection.Read(f.Context,p.Paths,launch.Args,read: path => path == user ? throw new UnauthorizedAccessException() : File.ReadAllText(path));
        Assert(!unreadable.ReuseEligible && unreadable.Sources.Single(s=>s.Kind=="user").State == "unreadable", v, "unreadable", "unreadable differs from absent");
        File.Delete(f.Context.ManagedPolicyPath);
        var unknown = LiveClaudeSettingsProjection.Read(f.Context with { ManagedLocationProven=false },p.Paths,launch.Args);
        Assert(!unknown.ReuseEligible && unknown.Sources.Single(s=>s.Kind=="managed").State == "absent-unverified", v, "managed-unverified", "unproven managed absence never permits reuse");
        f.Write(f.Context.ManagedPolicyPath, "{\"remoteControlAtStartup\":true}", false);
        var digests = new List<string>();
        foreach (var text in new string?[] { null, "{}", "{\"model\":\"\"}", "{\"model\":\"present\"}" })
        {
            if (text is null) File.Delete(user); else File.WriteAllText(user,text);
            digests.Add(project(p, launch.Args).Digest);
        }
        Assert(digests.Distinct().Count() == 4, v, "presence", "absent, empty object, empty value and value remain distinct");
        File.WriteAllText(user,old);
        var inputBefore=LiveClaudeProfile.InputDigest(user);
        foreach(var key in new[]{"apiKey","ANTHROPIC_AUTH_TOKEN","CLAUDE_CODE_OAUTH_TOKEN_FILE","ANTIPHON_TASK_TOKEN"})
        foreach (var value in new[] { "synthetic-credential-A", "synthetic-credential-B" })
        {
            var node=JsonNode.Parse(old)!.AsObject(); node[key]=value;
            File.WriteAllText(user,node.ToJsonString());
            var current=project(p, launch.Args);
            Assert(current.Digest==original.Digest && current.ReuseEligible && LiveClaudeProfile.InputDigest(user)==inputBefore && current.Sources.All(s=>!s.Projection.Contains(value)), v,"credential-exclusion","known credential values never enter projection or digest");
        }
        File.WriteAllText(user,old);
        var state=JsonNode.Parse(File.ReadAllText(p.Paths.GlobalState))!; state["numStartups"]=999;
        File.WriteAllText(p.Paths.GlobalState,state.ToJsonString());
        Assert(project(p, launch.Args).Digest==original.Digest,v,"volatile","known bookkeeping alone remains stable");
        foreach (var env in new[] { new Dictionary<string,string>(), new Dictionary<string,string>{{"ANTHROPIC_BASE_URL",""}}, new Dictionary<string,string>{{"ANTHROPIC_BASE_URL","http://fixture.invalid"}} })
            digests.Add(LiveClaudeSettingsProjection.Read(f.Context with { Environment=env },p.Paths,launch.Args).Digest);
        Assert(digests.TakeLast(3).Distinct().Count()==3,v,"environment-presence","absent, empty and valued environment remain distinct");
        }
        finally
        {
            File.WriteAllText(user, old);
            File.WriteAllText(f.Context.ManagedPolicyPath, managed);
            File.WriteAllText(p.Paths.GlobalState, globalBefore);
        }
    }
    private static void Arguments(string v, Builder build)
    {
        var portable = Environment.GetEnvironmentVariable("C1545_PROFILE_CONTROLS") is null ? LiveClaudePortableContext.Optional() : null;
        var exports = new List<LiveClaudeArgvCapture>();
        foreach (var model in new[] { "named-A", "named-B", "production-default" })
        {
            using var f = new LiveClaudeProfileFixture(portable is null ? null : Path.Combine(portable.Root, model), model);
            using var p = new LiveClaudeProfile(f.Context);
            foreach (var role in new[] { "control", "off", "slash", "submit", "menu" })
            {
                var spec = build(p,role,f.Session,role is "off" or "slash" ? f.Overlay:null);
                Assert(!spec.Args.Contains("--setting-sources"),v,"sources","actual builders preserve production default user/project/local sources");
                Assert(spec.Args.Count(a=>a=="--settings")== (role is "off" or "slash" ? 1:0),v,"overlay-"+role,"only off/slash have exactly one owned overlay");
                Assert(spec.Cwd==f.Context.Cwd && spec.Env["CLAUDE_CONFIG_DIR"]==f.Context.ConfigDir,v,"launch-"+role,"actual captured cwd/environment are owned");
                var i=Array.IndexOf(spec.Args,"--model");
                Assert(model=="production-default" ? i<0 : i>=0 && spec.Args[i+1]==model,v,"model-"+model,"actual argv selects context model");
                if (portable is not null) exports.Add(LiveClaudeArgvCapture.Create(portable,model,role,f.Session,spec,f.Overlay));
            }
        }
        if (portable is not null) LiveClaudeArgvCapture.Publish(portable,exports);
    }
    private static void Models(string v, Builder build, string? variant = null)
    {
        foreach (var mode in variant is null ? new[] { "named-A", "named-B", "production-default" } : new[] { variant })
        {
            using var f=new LiveClaudeProfileFixture(mode:mode);
            using var p=new LiveClaudeProfile(f.Context);
            var spec=build(p,"control",f.Session,null);
            var i=Array.IndexOf(spec.Args,"--model");
            Assert(mode=="production-default" ? i<0 : i>=0 && spec.Args[i+1]==mode,v,mode,"model argv comes from explicit context");
        }
        using var invalid=new LiveClaudeProfileFixture();
        Refuses(()=>{using var p=new LiveClaudeProfile(invalid.Context with {ModelMode=null});},"missing-model",v,"model-missing");
        Refuses(()=>{using var p=new LiveClaudeProfile(invalid.Context with {EffectiveModel=null});},"unobserved-model",v,"effective-model");
        using var envProfile=new LiveClaudeProfile(invalid.Context with {Environment=new Dictionary<string,string>{{"FIXTURE_EMPTY",""}}});
        var env=envProfile.Environment();
        Assert(env.ContainsKey("FIXTURE_EMPTY") && env["FIXTURE_EMPTY"]=="" && !env.ContainsKey("FIXTURE_ABSENT"),v,"env","absent and present-empty preserved");
    }
}
