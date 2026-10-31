using System.Reflection;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport.LiveClaude;
using Microsoft.Extensions.Logging.Abstractions;
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
        LiveClaudeProfileAudit.C680Callers(Build,ClaudeDialogWatchdogCanaryTests.ProjectProfile);
        LiveClaudeProfileMutants.Run(v);
    }
    [Test]
    public void DedicatedConfigOwnsTrustAndEvidence()
    {
        Run("V-13");
        using var fixture = new LiveClaudeProfileFixture();
        using var profile = new LiveClaudeProfile(fixture.Context);
        var root = TranscriptTailer.ResolveProjectsRoot(profile.Environment());
        LiveClaudeProfileCases.Assert(root == profile.Paths.Projects, "V-13", "projects-root", "reader uses the profile config directory");
        var ambient = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        LiveClaudeProfileCases.Assert(!string.Equals(root, ambient, StringComparison.Ordinal), "V-13", "projects-root", "reader stays off the operator profile");
        LaunchEnvironmentBindsItsOwnTranscript();
    }

    private static void LaunchEnvironmentBindsItsOwnTranscript()
    {
        var root = Directory.CreateTempSubdirectory("c1545-tailer-").FullName;
        var launchConfig = Path.Combine(root, "launch");
        var decoyConfig = Path.Combine(root, "decoy");
        var sessionId = Guid.NewGuid();
        var fileName = sessionId.ToString("D") + ".jsonl";
        var launchFile = Path.Combine(launchConfig, "projects", "launch-cwd", fileName);
        var decoyFile = Path.Combine(decoyConfig, "projects", "decoy-cwd", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(launchFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(decoyFile)!);
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        Directory.CreateDirectory(Path.Combine(root, "cwd"));
        File.WriteAllText(launchFile, "{\"type\":\"user\",\"uuid\":\"launch-only\"}\n");
        File.WriteAllText(decoyFile, "{\"type\":\"user\",\"uuid\":\"decoy-only\"}\n");
        const string key = "CLAUDE_CONFIG_DIR";
        var previous = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, decoyConfig);
        var session = new RunnerSession(sessionId, new Antiphon.SessionRunner.SessionRunnerSettings { SessionLogPath = Path.Combine(root, "logs") },
            new SessionRunnerEventHub(), NullLogger.Instance);
        try
        {
            var request = new RunnerLaunchRequest(sessionId, "claude", Array.Empty<string>(),
                new Dictionary<string, string> { [key] = launchConfig }, Path.Combine(root, "cwd"), 80, 24, TranscriptEnabled: true);
            var start = typeof(RunnerSession).GetMethod("StartTailerFor", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("C1545 V-13/projects-root StartTailerFor missing");
            start.Invoke(session, new object[] { request, DateTime.UtcNow });
            var deadline = Environment.TickCount64 + 2_000;
            string? bound = null;
            while (Environment.TickCount64 < deadline)
            {
                bound = session.Tailer?.BoundTranscriptPath;
                if (bound is not null) break;
                Thread.Sleep(25);
            }
            var actual = bound is null ? null : Path.GetFullPath(bound);
            LiveClaudeProfileCases.Assert(string.Equals(actual, Path.GetFullPath(launchFile), StringComparison.Ordinal)
                && !string.Equals(actual, Path.GetFullPath(decoyFile), StringComparison.Ordinal),
                "V-13", "projects-root", "tailer bound " + (actual ?? "<none>"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }
    [Test] public void CwdInstructionsCannotLeak() => Run("V-14");
    [Test] public void SettingsProjectionDetectsDrift() => Run("V-15");
    [Test] public void UnknownConfigDisablesReuse() => Run("V-16");

    [Test, Explicit]
    public void StartupArgvMatchesProductionApplyOff()
    {
        var context=LiveClaudePortableContext.Required();
        // Compute every independent production request BEFORE opening any producer capture.
        var expected=new Dictionary<string,string[]>();
        var environments=new Dictionary<string,IReadOnlyDictionary<string,string>>();
        foreach(var model in new[]{"named-A","named-B","production-default"})
        {
            var independent=Production(context,model);
            environments[model]=independent.Env;
            expected[model+"/control"]=independent.Args.ToArray();
            expected[model+"/submit"]=independent.Args.ToArray();
            expected[model+"/menu"]=independent.Args.ToArray();
            var off=Path.Combine(context.Root,model,"off.json");
            expected[model+"/off"]=ClaudeRemoteControlLaunchArgs.ApplyOff(AgentKind.ClaudeCode,independent.Args,off).ToArray();
            expected[model+"/slash"]=ClaudeRemoteControlLaunchArgs.ApplyOff(AgentKind.ClaudeCode,independent.Args,off).ToArray();
        }
        var captures=LiveClaudeArgvCapture.Load(context);
        foreach(var capture in captures)
            LiveClaudeProfileCases.Assert(capture.Env.OrderBy(p=>p.Key).SequenceEqual(environments[capture.Model].OrderBy(p=>p.Key)),"V-17","environment","production defaults and explicit owned config match; only submit geometry differs (200x60 versus 120x30)");
        LiveClaudeParityCases.Run(context,captures,expected);
        LiveClaudeProfileMutants.Parity(context,captures,expected);
    }
    private static AgentLaunchSpec Production(LiveClaudePortableContext context,string model)
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
            ExtraArgs:["--session-id","15450000-0000-0000-0000-000000000001"],
            ExtraEnv:new Dictionary<string,string>{{"CLAUDE_CONFIG_DIR",Path.Combine(context.Root,model,"profile")}},TierModelAlias:model=="production-default"?null:model));
        return spec;
    }
    private sealed class Monitor(AgentRegistrySettings value):IOptionsMonitor<AgentRegistrySettings>
    {
        public AgentRegistrySettings CurrentValue=>value;
        public AgentRegistrySettings Get(string? name)=>value;
        public IDisposable? OnChange(Action<AgentRegistrySettings,string?> listener)=>null;
    }
}
