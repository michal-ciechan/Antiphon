using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Antiphon.TestSupport.LiveClaude;

internal static class LiveClaudeProfileMutants
{
    private static readonly Lazy<MetadataReference[]> references = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(Antiphon.Agents.Pty.ClaudeProjectTrust).Assembly.Location).Distinct()
            .Select(p=>(MetadataReference)MetadataReference.CreateFromFile(p)).ToArray());
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,byte[]> healthyPayloads = new();
    private sealed record Mutation(string Pc,string V,string File,string From,string To,string Assertion,string? Variant=null);
    private static readonly Mutation[] mutations =
    [
        new("PC-34","V-7","LiveClaudeProfilePaths.cs","if (string.IsNullOrWhiteSpace(context.ConfigDir)) throw new LiveClaudeRefusal(\"missing-config\");","if (string.IsNullOrWhiteSpace(context.ConfigDir)) context = context with { ConfigDir = context.OwnedRoot };","missing-config"),
        new("PC-35","V-7","LiveClaudeProfilePaths.cs","created.Remove(\"hasTrustDialogAccepted\");","/* owned trust restoration omitted */","restore-owned"),
        new("PC-36","V-7","LiveClaudeProfilePaths.cs","else if (priorFlag is null) projects[key]!.AsObject().Remove(\"hasTrustDialogAccepted\");","else if (true) projects.Remove(key);","restore-existing"),
        new("PC-37","V-7","LiveClaudeProfilePaths.cs","if (root[\"projects\"] is null) root[\"projects\"] = projects;","root[\"projects\"] = projects = new JsonObject();","trust-siblings"),
        new("PC-46","V-8","LiveClaudeProfile.cs","dir = dir.Parent","dir = null","ancestor-"),
        new("PC-47","V-8","LiveClaudeProfile.cs","public void Complete()\n    {\n        if (InstructionDigest() != instructions)","public void Complete()\n    {\n        if (false)","instruction-drift"),
        new("PC-48","V-11","LiveClaudeProfile.cs","return args.ToArray();","args.AddRange(new[] { \"--setting-sources\", \"\" }); return args.ToArray();","sources"),
        new("PC-49","V-11","LiveClaudeProfile.cs","else if (overlay is not null) throw new LiveClaudeRefusal(\"control-overlay\");","else args.AddRange(new[] { \"--settings\", Path.Combine(Context.OwnedRoot, \"off.json\") });","overlay-control"),
        new("PC-52","V-12","LiveClaudeProfile.cs","args.AddRange([\"--model\", Context.RequestedModel!]);","args.AddRange([\"--model\", \"haiku\"]);","named-A","named-A"),
        new("PC-52","V-12","LiveClaudeProfile.cs","args.AddRange([\"--model\", Context.RequestedModel!]);","args.AddRange([\"--model\", \"haiku\"]);","named-B","named-B"),
        new("PC-53","V-12","LiveClaudeProfile.cs","if (Context.ModelMode == \"named\") args.AddRange([\"--model\", Context.RequestedModel!]);","if (true) args.AddRange([\"--model\", Context.RequestedModel ?? \"haiku\"]);","production-default"),
        new("PC-54","V-12","LiveClaudeProfile.cs","if (Context.ModelMode is not (\"named\" or \"production-default\")) throw new LiveClaudeRefusal(\"missing-model\");","if (Context.ModelMode is not (\"named\" or \"production-default\")) return;","model-missing"),
        new("PC-55","V-12","LiveClaudeProfile.cs","if (string.IsNullOrWhiteSpace(Context.EffectiveModel)) throw new LiveClaudeRefusal(\"unobserved-model\");","if (false) throw new LiveClaudeRefusal(\"unobserved-model\");","effective-model"),
        new("PC-57","V-9","LiveClaudeSettingsProjection.cs","projection = Project(node, kind == \"global\", false).ToJsonString();","projection = kind == \"user\" ? \"{}\" : Project(node, kind == \"global\", false).ToJsonString();","drift-user"),
        new("PC-58","V-9","LiveClaudeSettingsProjection.cs","projection = Project(node, kind == \"global\", false).ToJsonString();","projection = kind == \"project\" ? \"{}\" : Project(node, kind == \"global\", false).ToJsonString();","drift-project"),
        new("PC-59","V-9","LiveClaudeSettingsProjection.cs","projection = Project(node, kind == \"global\", false).ToJsonString();","projection = kind == \"local\" ? \"{}\" : Project(node, kind == \"global\", false).ToJsonString();","drift-local"),
        new("PC-60","V-9","LiveClaudeSettingsProjection.cs","projection = Project(node, kind == \"global\", false).ToJsonString();","projection = kind == \"overlay\" ? \"{}\" : Project(node, kind == \"global\", false).ToJsonString();","drift-overlay"),
        new("PC-61","V-9","LiveClaudeSettingsProjection.cs","projection = Project(node, kind == \"global\", false).ToJsonString();","projection = kind == \"managed\" ? \"{}\" : Project(node, kind == \"global\", false).ToJsonString();","drift-managed"),
        new("PC-62","V-9","LiveClaudeSettingsProjection.cs","projection = Project(node, kind == \"global\", false).ToJsonString();","projection = kind == \"global\" ? \"{}\" : Project(node, kind == \"global\", false).ToJsonString();","drift-global"),
        new("PC-63","V-9","LiveClaudeSettingsProjection.cs","var ordered = precedence ?? loaded.Concat(new[] { \"global\", \"managed\" }).ToArray();","var ordered = (precedence ?? loaded.Concat(new[] { \"global\", \"managed\" }).ToArray()).Order().ToArray();","precedence"),
        new("PC-64","V-9","LiveClaudeSettingsProjection.cs","loaded = args[hit + 1].Split(',', StringSplitOptions.RemoveEmptyEntries);","loaded = new[] { \"user\", \"project\", \"local\" };","loaded-set"),
        new("PC-65","V-9","LiveClaudeSettingsProjection.cs","result[pair.Key] = p;","result[pair.Key] = new JsonObject();","trust-behavior"),
        new("PC-66","V-10","LiveClaudeSettingsProjection.cs","if (credentials.Contains(pair.Key)) continue;","if (credentials.Contains(pair.Key)) { result[pair.Key] = pair.Value?.DeepClone(); continue; }","credential-exclusion"),
        new("PC-67","V-10","LiveClaudeSettingsProjection.cs","if (!behavior.Contains(pair.Key)) { eligible = false; continue; }","if (!behavior.Contains(pair.Key)) { continue; }","unknown-key"),
        new("PC-68","V-10","LiveClaudeSettingsProjection.cs","context.SourcesComplete && effectiveObserved &&","context.SourcesComplete &&","unknown-effective"),
        new("PC-69","V-10","LiveClaudeSettingsProjection.cs","foreach (var extra in additionalSources ?? []) { eligible = false;","foreach (var extra in additionalSources ?? []) {","unknown-source"),
        new("PC-70","V-10","LiveClaudeSettingsProjection.cs","{ state = \"unreadable\"; eligible = false; }","{ state = \"absent\"; }","unreadable"),
        new("PC-71","V-10","LiveClaudeSettingsProjection.cs","{ state = \"absent-unverified\"; eligible = false; }","{ state = \"absent\"; }","managed-unverified"),
        new("PC-72","V-10","LiveClaudeSettingsProjection.cs","sources.Add(new(kind, file, state, projection));","if (state == \"empty\") { state = \"absent\"; projection = \"\"; } sources.Add(new(kind, file, state, projection));","presence"),
        new("PC-75","V-17","LiveClaudeArgvCapture.cs","if (!actual.Args.SequenceEqual(production))","if (!actual.Args.SequenceEqual(actual.Args))","independent-control"),
        new("PC-76","V-17","LiveClaudeArgvCapture.cs","if (!actual.Args.SequenceEqual(production))","if (actual.Role != \"control\" && !actual.Args.SequenceEqual(production))","independent-control"),
        new("PC-77","V-17","LiveClaudeArgvCapture.cs","if (!actual.Args.SequenceEqual(production))","if (actual.Role is not (\"off\" or \"slash\") && !actual.Args.SequenceEqual(production))","off-missing"),
        new("PC-77","V-17","LiveClaudeArgvCapture.cs","if(json.Count!=1 || json[\"remoteControlAtStartup\"]?.GetValue<bool>()!=false)","if(false)","off-json"),
        new("PC-78","V-17","LiveClaudeArgvCapture.cs","if (capture.Source != context.Source)","if (false)","capture-source"),
        new("PC-79","V-17","LiveClaudeArgvCapture.cs","if (capture.Run != context.Run)","if (false)","capture-run"),
        new("PC-80","V-17","LiveClaudeArgvCapture.cs","if (capture.ProducerBuild != context.ProducerBuild)","if (false)","capture-build"),
        new("PC-81","V-17","LiveClaudeArgvCapture.cs","if (capture.Uid != ProducerUid)","if (false)","capture-uid"),
        new("PC-82","V-17","LiveClaudeArgvCapture.cs","capture.Model != model || capture.Role != role ||","capture.Role != role ||","capture-request"),
        new("PC-83","V-17","LiveClaudeArgvCapture.cs","throw new LiveClaudeRefusal(\"producer-incomplete\");\n        if (LiveClaudeProfile.HashFile(context.ProducerReceipt)","return;\n        if (LiveClaudeProfile.HashFile(context.ProducerReceipt)","producer-incomplete"),
        new("PC-84","V-17","LiveClaudeArgvCapture.cs","if (!capture.Complete || capture.Args.Length==0)","if (capture.Args.Length==0)","capture-cut"),
        new("PC-84","V-17","LiveClaudeArgvCapture.cs","if (!capture.Complete || capture.Args.Length==0)","if (!capture.Complete)","capture-cut")
    ];
    private static readonly string[] files = ["LiveClaudeLaunchContext.cs","LiveClaudeProfilePaths.cs","LiveClaudeProfile.cs",
        "LiveClaudeSettingsProjection.cs","LiveClaudeProfileCases.cs","LiveClaudeArgvCapture.cs","LiveClaudeParityCases.cs","LiveClaudeExecutableAudit.cs"];
    public static void Run(string v)
    {
        if(System.Environment.GetEnvironmentVariable("C1545_PROFILE_CONTROLS")!=v) return;
        foreach(var m in mutations.Where(m=>m.V==v))
        {
            if(m.Pc=="PC-46")
                foreach(var variant in new[]{"CLAUDE.md","AGENTS.md","import.md",".claude/settings.json",".claude/skills/s/SKILL.md",".mcp.json"})
                    Cycle(m with {Variant=variant,Assertion="ancestor-"+variant},null,null,null);
            else Cycle(m,null,null,null);
        }
    }
    public static void Parity(LiveClaudePortableContext context,LiveClaudeArgvCapture[] captures,Dictionary<string,string[]> expected)
    {
        if(System.Environment.GetEnvironmentVariable("C1545_PROFILE_CONTROLS")!="V-17") return;
        foreach(var m in mutations.Where(m=>m.V=="V-17")) Cycle(m,context,captures,expected);
    }
    private static void Cycle(Mutation m,LiveClaudePortableContext? context,LiveClaudeArgvCapture[]? captures,Dictionary<string,string[]>? expected)
    {
        var root=Path.Combine(LiveClaudeExecutableAudit.Root(),"tests/Shared/LiveClaude");
        var sources=files.ToDictionary(f=>f,f=>File.ReadAllText(Path.Combine(root,f)));
        var source=sources[m.File];
        var site=source.IndexOf(m.From,StringComparison.Ordinal);
        if(site<0 || source.IndexOf(m.From,site+m.From.Length,StringComparison.Ordinal)>=0) throw new InvalidOperationException("C1545 "+m.Pc+" unique mutation site missing");
        sources[m.File]=source.Replace(m.From,m.To,StringComparison.Ordinal);
        var red=Execute(sources.Values,m.V,m.Variant,context,captures,expected);
        if(red is null || !red.Message.StartsWith($"C1545 {m.V}/{m.Assertion}",StringComparison.Ordinal))
            throw new InvalidOperationException($"C1545 {m.Pc} expected assertion-red {m.Assertion}; got {red?.GetType().Name}: {red?.Message}");
        sources[m.File]=source;
        var green=Execute(sources.Values,m.V,m.Variant,context,captures,expected,cacheHealthy:true);
        if(green is not null) throw new InvalidOperationException("C1545 "+m.Pc+" restored-green failed",green);
        Console.WriteLine($"C1545 {m.Pc}/{m.Variant??m.Assertion} assertion-red; restored-green source={LiveClaudeProfile.Hash(source)} patch={LiveClaudeProfile.Hash(m.From+"\n"+m.To)}");
    }
    private static Exception? Execute(IEnumerable<string> sources,string v,string? variant,LiveClaudePortableContext? context,
        LiveClaudeArgvCapture[]? captures,Dictionary<string,string[]>? expected,bool cacheHealthy=false)
    {
        const string entry="""
            namespace Antiphon.TestSupport.LiveClaude;
            public static class ProfileMutantEntry {
              public static void Run(string v,string variant,string context,string captures,string expected) {
                if(v!="V-17") LiveClaudeProfileCases.Run(v,variant:variant);
                else LiveClaudeParityCases.Run(
                  System.Text.Json.JsonSerializer.Deserialize<LiveClaudePortableContext>(context,LiveClaudePortableContext.Json)!,
                  System.Text.Json.JsonSerializer.Deserialize<LiveClaudeArgvCapture[]>(captures,LiveClaudePortableContext.Json)!,
                  System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string,string[]>>(expected)!);
              }
            }
            """;
        const string usings="global using System; global using System.IO; global using System.Linq; global using System.Collections.Generic;\n";
        var sourceTexts=sources.Append(entry).ToArray();
        var sourceHash=LiveClaudeProfile.Hash(string.Join("\n",sourceTexts));
        byte[]? payload=null;
        if(cacheHealthy) healthyPayloads.TryGetValue(sourceHash,out payload);
        if(payload is null)
        {
            var compilation=CSharpCompilation.Create("ProfileMutant"+Guid.NewGuid().ToString("N"),sourceTexts.Select(s=>CSharpSyntaxTree.ParseText(usings+s)),references.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,nullableContextOptions:NullableContextOptions.Enable));
            using var emittedBytes=new MemoryStream(); var emitted=compilation.Emit(emittedBytes);
            if(!emitted.Success) throw new InvalidOperationException("C1545 mutant compilation: "+string.Join(";",emitted.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
            payload=emittedBytes.ToArray();
            if(cacheHealthy) healthyPayloads.TryAdd(sourceHash,payload);
        }
        // Only the byte-identical restored source can reuse its compilation. Each invocation
        // still gets a fresh collectible context and executes the same witness after the red.
        using var bytes=new MemoryStream(payload);
        var load=new AssemblyLoadContext("c1545-profile-copy",isCollectible:true);
        try
        {
            var asm=load.LoadFromStream(bytes);
            try {asm.GetType("Antiphon.TestSupport.LiveClaude.ProfileMutantEntry")!.GetMethod("Run")!.Invoke(null,
                [v,variant,JsonSerializer.Serialize(context,LiveClaudePortableContext.Json),JsonSerializer.Serialize(captures,LiveClaudePortableContext.Json),JsonSerializer.Serialize(expected)]);}
            catch(TargetInvocationException e) {return e.InnerException;}
            return null;
        }
        finally {load.Unload();}
    }
}
