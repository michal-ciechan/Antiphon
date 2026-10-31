using System.Reflection;
using System.Runtime.Loader;
using Antiphon.TestSupport.LiveClaude;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Antiphon.Agents.Pty.Tests;

internal static class LiveClaudeStartupMutants
{
    private sealed record Mutation(string Pc,string V,string From,string To,string Assertion);
    private static readonly Mutation[] Mutations =
    [
        new("PC-85/badge","V-18","=> bridge is { Readable: true }", "=> bridge is null || bridge is { Readable: true }", "badge"),
        new("PC-85/foreign","V-18","bridge.Owner == owner", "true", "foreign-pid"),
        new("PC-86","V-18","RequireBridge(before.Owner, after);", "/* bridge setup omitted */", "slash-setup"),
        new("PC-88","V-19","return _control ??= start();", "return start();", "control-once"),
        new("PC-89","V-19","var control = await ControlAsync(experiment, start);", "dependent(); var control = await ControlAsync(experiment, start);", "control-order"),
        new("PC-90","V-19","RequireControl(experiment, control);\n        if (OwnedBridge", "if (control is null) return; RequireControl(experiment, control);\n        if (OwnedBridge", "control-absent"),
        new("PC-91","V-19","control.Experiment.Run != experiment.Run || control.Experiment.Epoch != experiment.Epoch", "false", "control-foreign"),
        new("PC-92","V-19","control.Experiment.Binary != experiment.Binary", "false", "control-binary"),
        new("PC-93","V-19","control.Experiment.Profile != experiment.Profile", "false", "control-profile"),
        new("PC-94","V-19","control.Experiment.Settings != experiment.Settings", "false", "control-settings"),
        new("PC-95","V-18","control.ModelPrompts != 0", "false", "zero-prompts"),
        new("PC-96","V-20","var window = OffWindow(control);", "var window = TimeSpan.FromSeconds(8);", "late-bridge"),
        new("PC-97","V-20","control.Latency > TimeSpan.FromSeconds(15) ? control.Latency : TimeSpan.FromSeconds(15)", "TimeSpan.FromSeconds(15)", "latency-bound"),
        new("PC-98","V-20","while (true)\n        {\n            var bridge = await read();", "while (true)\n        {\n            if (elapsed() >= window) return new(owner, null, elapsed(), settings()); var bridge = await read();", "deadline-bridge"),
        new("PC-102","V-22","new[] { \"user\", \"project\", \"local\" }", "new[] { \"project\", \"local\" }", "marker-user"),
        new("PC-103","V-22","new[] { \"user\", \"project\", \"local\" }", "new[] { \"user\", \"local\" }", "marker-project"),
        new("PC-104","V-22","new[] { \"user\", \"project\", \"local\" }", "new[] { \"user\", \"project\" }", "marker-local"),
        new("PC-105","V-22","m.Source == source && m.Session == owner.Session", "m.Source == source", "marker-foreign-user"),
        new("PC-106","V-22","actual[0].Retained != expected[0].Retained", "false", "retained-setting-user")
    ];
    public static async Task RunAsync(string v)
    {
        if(Environment.GetEnvironmentVariable("C1545_STARTUP_CONTROLS")!=v) return;
        if(v=="V-18")
        {
            var source=LiveClaudeExecutableAudit.Read(LiveClaudeStartupAudit.MenuPath);
            LiveClaudeStartupAudit.Reject(LiveClaudeStartupAudit.Menu,source,
                "await ClaudeRemoteControlAtStartupCanaryTests.EnsureMenuBridgeAsync(runner, profile, sessionId);", ";","PC-87");
        }
        if(v=="V-21")
        {
            var source=LiveClaudeExecutableAudit.Read(LiveClaudeStartupAudit.StartupPath);
            foreach(var (method,role,pc,oracle) in new[]{
                ("Control_auto_connects_a_bridge_session_before_the_first_user","control","PC-99","LiveClaudeStartupEvidence.RequireControl(experiment, control);"),
                ("Settings_file_prevents_auto_connect_and_keeps_startup_records","off","PC-100","LiveClaudeStartupEvidence.RequireOff(experiment, control, observation);"),
                ("Remote_control_slash_command_still_arms_after_settings_off","slash","PC-101","LiveClaudeStartupEvidence.RequireSlash(experiment, control, before, after);")})
            {
                var body=LiveClaudeStartupAudit.Method(source,method).ToFullString();
                foreach(var to in new[]{";","if (false) { "+oracle+" }","try { "+oracle+" } catch { }"})
                    LiveClaudeStartupAudit.Reject(s=>LiveClaudeStartupAudit.Caller("class Subject { "+s+" }",method,role),body,oracle,to,pc);
            }
        }
        if(!Mutations.Any(m=>m.V==v)) return;
        var root=LiveClaudeExecutableAudit.Root();
        var evidence=File.ReadAllText(Path.Combine(root,"tests/Antiphon.Agents.Pty.Tests/LiveClaudeStartupEvidence.cs"));
        var cases=File.ReadAllText(Path.Combine(root,"tests/Antiphon.Agents.Pty.Tests/LiveClaudeStartupCases.cs"));
        var entryMethod=LiveClaudeStartupAudit.Method(cases,"RunAsync");
        cases=cases.Replace(entryMethod.ToFullString(),"",StringComparison.Ordinal);
        // The exact witness bodies execute, with only source-location lookup injected.
        cases=cases.Replace("LiveClaudeExecutableAudit.Root()",Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(root,true),StringComparison.Ordinal);
        var context=File.ReadAllText(Path.Combine(root,"tests/Shared/LiveClaude/LiveClaudeLaunchContext.cs"));
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Distinct().Select(p=>(MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
        byte[]? restored=null;
        foreach(var mutation in Mutations.Where(m=>m.V==v))
        {
            var i=evidence.IndexOf(mutation.From,StringComparison.Ordinal);
            if(i<0 || evidence.IndexOf(mutation.From,i+mutation.From.Length,StringComparison.Ordinal)>=0)
                throw new InvalidOperationException("C1545 "+mutation.Pc+" mutation site ambiguous");
            var red=await ExecuteAsync(Compile(evidence.Replace(mutation.From,mutation.To,StringComparison.Ordinal)),v);
            if(red is null || !red.Message.StartsWith($"C1545 {v}/{mutation.Assertion}",StringComparison.Ordinal))
                throw new InvalidOperationException($"C1545 {mutation.Pc} expected assertion-red {mutation.Assertion}; got {red?.GetType().Name}: {red?.Message}");
            restored ??= Compile(evidence);
            var green=await ExecuteAsync(restored,v);
            if(green is not null) throw new InvalidOperationException("C1545 "+mutation.Pc+" restored-green failed",green);
            Console.WriteLine($"C1545 {mutation.Pc} assertion-red; restored-green source={LiveClaudeProfile.Hash(evidence)} patch={LiveClaudeProfile.Hash(mutation.From+"\n"+mutation.To)}");
        }
        byte[] Compile(string source)
        {
            const string usings="global using System; global using System.IO; global using System.Linq; global using System.Threading; global using System.Threading.Tasks; global using System.Collections.Generic;\n";
            const string entry="""
            namespace Antiphon.Agents.Pty.Tests;
            public static class StartupMutantEntry {
              public static Task Run(string v) => v switch {
                "V-18" => LiveClaudeStartupCases.BridgeAsync(),
                "V-19" => LiveClaudeStartupCases.ControlAsync(),
                "V-20" => LiveClaudeStartupCases.WindowAsync(),
                "V-22" => LiveClaudeStartupCases.SettingsAsync(),
                _ => throw new InvalidOperationException("Unknown witness") };
            }
            """;
            var compilation=CSharpCompilation.Create("StartupMutant"+Guid.NewGuid().ToString("N"),
                new[]{source,cases,context,entry}.Select(s=>CSharpSyntaxTree.ParseText(usings+s)),refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,nullableContextOptions:NullableContextOptions.Enable));
            using var bytes=new MemoryStream();var emit=compilation.Emit(bytes);
            if(!emit.Success) throw new InvalidOperationException("C1545 mutant compilation: "+string.Join(";",emit.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
            return bytes.ToArray();
        }
    }
    private static async Task<Exception?> ExecuteAsync(byte[] payload,string v)
    {
        var load=new AssemblyLoadContext("c1545-startup-copy",isCollectible:true);
        try
        {
            using var bytes=new MemoryStream(payload);
            var asm=load.LoadFromStream(bytes);
            try { await (Task)asm.GetType("Antiphon.Agents.Pty.Tests.StartupMutantEntry")!.GetMethod("Run")!.Invoke(null,[v])!; }
            catch(TargetInvocationException e) {return e.InnerException;}
            catch(Exception e) {return e;}
            return null;
        }
        finally {load.Unload();}
    }
}
