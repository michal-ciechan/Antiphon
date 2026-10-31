using System.Text.Json;
using Antiphon.TestSupport.LiveClaude;
using Antiphon.Tests.Agents;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit"), Timeout(30_000)]
public class LiveLaneIsolationTests
{
    private static LiveClaudeProfileSpec Build(LiveClaudeProfile profile,string role,string session,string? overlay)
    {
        using var f=new LiveClaudeExecutableCases(LiveClaudeCore.C680,
            (u,r,c,l)=>ClaudeDialogWatchdogCanaryTests.PrepareExecutable(r,c,l));
        var executable=ClaudeDialogWatchdogCanaryTests.PrepareExecutable(f.Request,f.Context with {Profile=profile.Context});
        var settings=overlay ?? Path.Combine(profile.Context.OwnedRoot,"off.json");
        var spec=ClaudeDialogWatchdogCanaryTests.CreateProfileLaunch(executable,profile,settings,Guid.Parse(session),DateTime.UnixEpoch);
        return new(spec.Args.ToArray(),new(spec.Env),spec.Cwd);
    }
    private static void Run(string v)
    {
        LiveClaudeProfileCases.Run(v,Build,project:ClaudeDialogWatchdogCanaryTests.ProjectProfile);
        LiveClaudeProfileAudit.C680Callers();
        LiveClaudeProfileMutants.Run(v);
    }
    [Test] public void DedicatedConfigOwnsTrustAndEvidence() => Run("V-13");
    [Test] public void CwdInstructionsCannotLeak() => Run("V-14");
    [Test] public void SettingsProjectionDetectsDrift() => Run("V-15");
    [Test] public void UnknownConfigDisablesReuse() => Run("V-16");

    [Test, Explicit]
    public void StartupArgvMatchesProductionApplyOff()
    {
        var context=LiveClaudePortableContext.Required();
        // Compute every independent production request BEFORE opening any producer capture.
        var expected=new Dictionary<string,string[]>();
        foreach(var model in new[]{"named-A","named-B","production-default"})
        {
            var independent=Production(context,model);
            expected[model+"/control"]=independent;
            expected[model+"/submit"]=independent;
            expected[model+"/menu"]=independent;
            var off=Path.Combine(context.Root,model,"off.json");
            expected[model+"/off"]=ClaudeRemoteControlLaunchArgs.ApplyOff(AgentKind.ClaudeCode,independent,off).ToArray();
            expected[model+"/slash"]=ClaudeRemoteControlLaunchArgs.ApplyOff(AgentKind.ClaudeCode,independent,off).ToArray();
        }
        var captures=LiveClaudeArgvCapture.Load(context);
        LiveClaudeParityCases.Run(context,captures,expected);
        LiveClaudeProfileMutants.Parity(context,captures,expected);
    }
    private static string[] Production(LiveClaudePortableContext context,string model)
    {
        using var doc=JsonDocument.Parse(LiveClaudeExecutableAudit.Read("server/appsettings.json"));
        var definition=doc.RootElement.GetProperty("Agents").GetProperty("Definitions").GetProperty("claude");
        var settings=new AgentRegistrySettings {DefaultDefinition="claude"};
        settings.Definitions["claude"]=new AgentDefinition
        {
            Kind=definition.GetProperty("Kind").GetString()!,
            Exe=Path.Combine(context.Root,"inert-executable"),
            ArgsTemplate=definition.GetProperty("ArgsTemplate").EnumerateArray().Select(a=>a.GetString()!).ToList(),
            Env=definition.GetProperty("Env").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!)
        };
        var registry=new AgentRegistry(new Monitor(settings));
        var spec=registry.Resolve("claude",new AgentLaunchOptions(Cwd:Path.Combine(context.Root,model,"workspace"),Cols:120,Rows:30,
            ExtraArgs:["--session-id","15450000-0000-0000-0000-000000000001"],TierModelAlias:model=="production-default"?null:model));
        return spec.Args.ToArray();
    }
    private sealed class Monitor(AgentRegistrySettings value):IOptionsMonitor<AgentRegistrySettings>
    {
        public AgentRegistrySettings CurrentValue=>value;
        public AgentRegistrySettings Get(string? name)=>value;
        public IDisposable? OnChange(Action<AgentRegistrySettings,string?> listener)=>null;
    }
}
