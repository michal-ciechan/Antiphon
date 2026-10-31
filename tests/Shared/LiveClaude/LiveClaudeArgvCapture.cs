using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace Antiphon.TestSupport.LiveClaude;

internal sealed record LiveClaudePortableContext(string Root, string Source, string Run,
    string? ProducerBuild = null, string? ProducerReceipt = null, string? ProducerReceiptHash = null)
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static LiveClaudePortableContext? Optional()
    {
        var path = Environment.GetEnvironmentVariable("ANTIPHON_LIVE_PORTABLE_CONTEXT");
        if (path is null) return null;
        var context = JsonSerializer.Deserialize<LiveClaudePortableContext>(File.ReadAllText(path),Json)
            ?? throw new LiveClaudeRefusal("invalid-portable-context");
        if (!Path.IsPathFullyQualified(context.Root) || context.Source.Length != 40 || string.IsNullOrWhiteSpace(context.Run))
            throw new LiveClaudeRefusal("invalid-portable-context");
        LiveClaudeProfilePaths.Owned(path,context.Root);
        return context;
    }
    public static LiveClaudePortableContext Required() => Optional() ?? throw new LiveClaudeRefusal("missing-portable-context");
}

internal sealed record LiveClaudeArgvCapture(string Source, string Run, string ProducerBuild, string Uid,
    string Model, string Role, string Session, string[] Args, Dictionary<string,string> Env,
    string Cwd, string? OverlayJson, bool Complete, int Cols = 120, int Rows = 30)
{
    public const string ProducerUid = "Antiphon.Agents.Pty.Tests.LiveClaudeIsolationWiringTests.LaunchArgumentsKeepProductionSources";
    public static LiveClaudeArgvCapture Create(LiveClaudePortableContext? portable,string model,string role,string session,LiveClaudeProfileSpec spec,string overlay)
        => new(portable?.Source ?? "local", portable?.Run ?? "local",
            LiveClaudeProfile.HashFile(System.Reflection.Assembly.GetExecutingAssembly().Location), ProducerUid,model,role,session,
            spec.Args.ToArray(),new(spec.Env),spec.Cwd,role is "off" or "slash" ? File.ReadAllText(overlay):null,true,spec.Cols,spec.Rows);
    public static void Publish(LiveClaudePortableContext context,IReadOnlyList<LiveClaudeArgvCapture> captures)
    {
        if (captures.Count != 15) throw new LiveClaudeRefusal("partial-capture");
        var path=Path.Combine(context.Root,"captures.json");
        File.WriteAllText(path+".partial",JsonSerializer.Serialize(captures,LiveClaudePortableContext.Json));
        File.Move(path+".partial",path); // no ambient latest file, no overwrite
    }
    public static LiveClaudeArgvCapture[] Load(LiveClaudePortableContext context)
    {
        RequireProducer(context);
        var captures=JsonSerializer.Deserialize<LiveClaudeArgvCapture[]>(File.ReadAllText(Path.Combine(context.Root,"captures.json")),LiveClaudePortableContext.Json)
            ?? throw new LiveClaudeRefusal("partial-capture");
        if (captures.Length != 15 || captures.Select(c=>c.Model+"/"+c.Role).Distinct().Count()!=15)
            throw new LiveClaudeRefusal("partial-capture");
        foreach (var capture in captures) Validate(context,capture,capture.Model,capture.Role);
        return captures;
    }
    public static void RequireProducer(LiveClaudePortableContext context)
    {
        if (context.ProducerReceipt is null || context.ProducerBuild is null || context.ProducerReceiptHash is null)
            throw new LiveClaudeRefusal("producer-incomplete");
        if (LiveClaudeProfile.HashFile(context.ProducerReceipt) != context.ProducerReceiptHash) throw new LiveClaudeRefusal("producer-incomplete");
        using var doc=JsonDocument.Parse(File.ReadAllText(context.ProducerReceipt));
        var root=doc.RootElement;
        var source=root.GetProperty("source");
        if (root.GetProperty("exitCode").GetInt32()!=0 || root.GetProperty("commit").GetString()!=context.Source
            || source.GetProperty("state").GetString()!="clean" || source.GetProperty("buildSource").GetString()!="verified")
            throw new LiveClaudeRefusal("producer-incomplete");
        var row=root.GetProperty("rows").EnumerateArray().Single(r=>r.GetProperty("id").GetString()=="CP-18");
        if (row.GetProperty("executed").GetInt32()!=6 || row.GetProperty("passed").GetInt32()!=6
            || row.GetProperty("failed").GetInt32()!=0 || row.GetProperty("skipped").GetInt32()!=0)
            throw new LiveClaudeRefusal("producer-incomplete");
        var trx=XDocument.Load(row.GetProperty("trx").GetString()!);
        XNamespace ns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var result=trx.Descendants(ns+"UnitTestResult").Single(r=>r.Attribute("testName")?.Value=="LaunchArgumentsKeepProductionSources");
        if(result.Attribute("outcome")?.Value!="Passed") throw new LiveClaudeRefusal("producer-incomplete");
    }
    public static void Validate(LiveClaudePortableContext context,LiveClaudeArgvCapture capture,string model,string role)
    {
        if (capture.Source != context.Source) throw new LiveClaudeRefusal("capture-source");
        if (capture.Run != context.Run) throw new LiveClaudeRefusal("capture-run");
        if (capture.ProducerBuild != context.ProducerBuild) throw new LiveClaudeRefusal("capture-build");
        if (capture.Uid != ProducerUid) throw new LiveClaudeRefusal("capture-uid");
        if (capture.Model != model || capture.Role != role || model is not ("named-A" or "named-B" or "production-default")
            || role is not ("control" or "off" or "slash" or "submit" or "menu")) throw new LiveClaudeRefusal("capture-request");
        if (!capture.Complete || capture.Args.Length==0) throw new LiveClaudeRefusal("partial-capture");
        var expectedRoot=Path.Combine(context.Root,model);
        if(capture.Cwd!=Path.Combine(expectedRoot,"workspace") || capture.Session!="15450000-0000-0000-0000-000000000001"
            || !capture.Env.TryGetValue("CLAUDE_CONFIG_DIR",out var config) || config!=Path.Combine(expectedRoot,"profile"))
            throw new LiveClaudeRefusal("capture-request");
        if(capture.Cols!=(role=="submit"?200:120) || capture.Rows!=(role=="submit"?60:30)) throw new LiveClaudeRefusal("capture-request");
    }
    public static void Compare(LiveClaudeArgvCapture actual,string[] production)
    {
        if (!actual.Args.SequenceEqual(production)) throw new LiveClaudeRefusal("argv-parity");
        if(actual.Role is "off" or "slash")
        {
            var json=System.Text.Json.Nodes.JsonNode.Parse(actual.OverlayJson!)!.AsObject();
            if(json.Count!=1 || json["remoteControlAtStartup"]?.GetValue<bool>()!=false) throw new LiveClaudeRefusal("argv-parity");
        }
        else if(actual.OverlayJson is not null) throw new LiveClaudeRefusal("argv-parity");
    }
}
