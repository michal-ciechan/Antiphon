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
[NotInParallel]
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
        var marked = typeof(LiveLaneIsolationTests).GetCustomAttributes(inherit: false)
            .Any(attribute => attribute.GetType().Name == "NotInParallelAttribute");
        if (!marked) throw new InvalidOperationException("C1545 V-13/config-dir process-wide env needs NotInParallel");
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
        var session = new SessionRunnerRuntime.RunnerSession(sessionId, new Antiphon.SessionRunner.SessionRunnerSettings { SessionLogPath = Path.Combine(root, "logs") },
            new SessionRunnerEventHub(), NullLogger.Instance);
        try
        {
            var request = new RunnerLaunchRequest(sessionId, "claude", Array.Empty<string>(),
                new Dictionary<string, string> { [key] = launchConfig }, Path.Combine(root, "cwd"), 80, 24, TranscriptEnabled: true);
            var start = typeof(SessionRunnerRuntime.RunnerSession).GetMethod("StartTailerFor", BindingFlags.Instance | BindingFlags.NonPublic)
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

    [Test]
    public void WindowsShapedOwnershipAcceptsAliasesAndRefusesEscapes()
    {
        var shortNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["LNDCO~1"] = "lndco" };
        var junctions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\Users\lndco\AppData\Local\Temp\profile\link"] = @"D:\outside\escaped"
        };
        var rules = LiveClaudeOwnershipRules.WindowsShape(path => CanonicalWindows(path, shortNames, junctions));
        var root = @"C:\Users\LNDCO~1\AppData\Local\Temp\profile\";
        var child = @"c:/users/lndco/appdata/local/temp/profile/workspace/.claude";
        LiveClaudeProfileCases.Assert(LiveClaudeProfilePaths.Within(child, root, rules),
            "V-14", "windows-alias", "mixed-case, slash and 8.3 forms of an in-root path are owned");
        var owned = LiveClaudeProfilePaths.Owned(child, root, rules);
        LiveClaudeProfileCases.Assert(owned.EndsWith(@"\workspace\.claude", StringComparison.OrdinalIgnoreCase),
            "V-14", "windows-alias", "the owned result is the canonical child");
        LiveClaudeProfileCases.Assert(LiveClaudeProfilePaths.Within(@"C:\Users\lndco\AppData\Local\Temp\profile\workspace\..\workspace\.claude", root, rules),
            "V-14", "windows-alias", "dot segments that stay inside the root remain owned");
        LiveClaudeProfileCases.Refuses(() => LiveClaudeProfilePaths.Owned(@"C:\Users\lndco\AppData\Local\Temp\profile2\workspace", root, rules),
            "unowned-path", "V-14", "sibling-prefix");
        LiveClaudeProfileCases.Refuses(() => LiveClaudeProfilePaths.Owned(@"C:\ROOT2\child", @"C:\root", rules),
            "unowned-path", "V-14", "sibling-case");
        LiveClaudeProfileCases.Refuses(() => LiveClaudeProfilePaths.Owned(@"C:\Users\lndco\AppData\Local\Temp\profile\workspace\..\..\..\Windows", root, rules),
            "unowned-path", "V-14", "dotdot-escape");
        LiveClaudeProfileCases.Refuses(() => LiveClaudeProfilePaths.Owned(@"C:\Users\lndco\AppData\Local\Temp\profile\link\CLAUDE.md", root, rules),
            "unowned-path", "V-14", "junction-escape");
        LiveClaudeProfileCases.Refuses(() => LiveClaudeProfilePaths.Owned(@"relative\workspace", root, rules),
            "unowned-path", "V-14", "relative");
    }

    [Test]
    public void SymlinkInsideTheRootThatPointsOutsideIsRefused()
    {
        var root = Directory.CreateTempSubdirectory("c1545-link-root-").FullName;
        var outside = Directory.CreateTempSubdirectory("c1545-link-out-").FullName;
        var link = Path.Combine(root, "escape");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
            LiveClaudeProfileCases.Refuses(() => LiveClaudeProfilePaths.Owned(link, root), "linked-path", "V-14", "linked-escape");
            LiveClaudeProfileCases.Refuses(() => LiveClaudeProfilePaths.Owned(Path.Combine(outside, "nope"), root), "unowned-path", "V-14", "outside-root");
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link, false);
            Directory.Delete(root, true);
            Directory.Delete(outside, true);
        }
    }

    private static string CanonicalWindows(string path, IReadOnlyDictionary<string, string> shortNames, IReadOnlyDictionary<string, string> junctions, int depth = 0)
    {
        if (depth > 8) throw new InvalidOperationException("C1545 windows canonicaliser exceeded junction depth");
        var normalized = path.Replace('/', '\\');
        var unc = normalized.StartsWith(@"\\", StringComparison.Ordinal);
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var drive = "";
        var start = 0;
        if (!unc && parts.Length > 0 && parts[0].Length == 2 && parts[0][1] == ':')
        {
            drive = char.ToUpperInvariant(parts[0][0]) + ":";
            start = 1;
        }
        else if (unc) drive = @"\\";
        var stack = new List<string>();
        for (var i = start; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part == ".") continue;
            if (part == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                continue;
            }
            foreach (var pair in shortNames)
            {
                if (!part.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)) continue;
                part = pair.Value;
                break;
            }
            stack.Add(part);
            if (drive.Length == 0) continue;
            var soFar = JoinWindows(drive, stack);
            foreach (var pair in junctions)
            {
                var link = pair.Key.Replace('/', '\\').TrimEnd('\\');
                if (!soFar.Equals(link, StringComparison.OrdinalIgnoreCase)) continue;
                var rest = i + 1 < parts.Length ? "\\" + string.Join("\\", parts.Skip(i + 1)) : "";
                return CanonicalWindows(linkTarget(pair.Value) + rest, shortNames, junctions, depth + 1);
            }
        }
        return JoinWindows(drive, stack);
        static string linkTarget(string target) => target.Replace('/', '\\').TrimEnd('\\');
    }

    private static string JoinWindows(string drive, List<string> stack)
    {
        var body = string.Join("\\", stack);
        if (drive == @"\\") return body.Length == 0 ? @"\\" : @"\\" + body;
        if (drive.Length == 0) return body;
        return body.Length == 0 ? drive + "\\" : drive + "\\" + body;
    }

    [Test]
    public void HostAncestorClaudeDirectoryStaysOutsideTheOwnedClosure()
    {
        // Windows TEMP sits under the user profile, whose .claude Claude may load from the ancestor
        // walk. A lane rooted there refuses; the fixture moves to a base with nothing above it.
        var scratch = LiveClaudeProfileFixture.CleanTempSubdirectory("c1545-host-");
        try
        {
            var home = Path.Combine(scratch, "home");
            Directory.CreateDirectory(Path.Combine(home, ".claude"));
            File.WriteAllText(Path.Combine(home, ".claude", "CLAUDE.md"), "operator instructions");
            File.WriteAllText(Path.Combine(home, ".claude", "settings.json"), "{\"model\":\"host\"}");
            var hostTemp = Directory.CreateDirectory(Path.Combine(home, "AppData", "Local", "Temp")).FullName;
            var outside = Path.Combine(scratch, "outside");
            using (var underHost = new LiveClaudeProfileFixture(Path.Combine(hostTemp, "profile")))
                LiveClaudeProfileCases.Refuses(() => { using var p = new LiveClaudeProfile(underHost.Context); },
                    "unowned-instructions", "V-14", "host-ancestor-refuses");
            var chosen = LiveClaudeProfileFixture.CleanBase([hostTemp, outside]);
            LiveClaudeProfileCases.Assert(chosen == Path.GetFullPath(outside), "V-14", "host-ancestor",
                "the fixture skips a base under a host .claude");
            using var fixture = new LiveClaudeProfileFixture(Path.Combine(chosen, "profile"));
            using var profile = new LiveClaudeProfile(fixture.Context);
            LiveClaudeProfileCases.Assert(LiveClaudeProfileFixture.InstructionInputAt(scratch) is null,
                "V-14", "host-ancestor", "the default fixture base has no instruction input above it");
            var only = false;
            try { LiveClaudeProfileFixture.CleanBase([hostTemp]); }
            catch (InvalidOperationException e) when (e.Message.StartsWith("C1545 fixture has no base", StringComparison.Ordinal)) { only = true; }
            LiveClaudeProfileCases.Assert(only, "V-14", "host-ancestor", "a host with Claude inputs above every base fails the fixture");
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

    [Test]
    public void InstructionFilesAboveTheOwnedRootRefuse()
    {
        // Claude loads project memory from every ancestor of its cwd; CLAUDE_CONFIG_DIR redirects
        // only the user-level directory. A file strictly above the owned root must still refuse.
        foreach (var name in new[] { "CLAUDE.md", "AGENTS.md", ".mcp.json", ".claude/CLAUDE.md" })
        {
            var parent = LiveClaudeProfileFixture.CleanTempSubdirectory("c1545-above-");
            try
            {
                using var fixture = new LiveClaudeProfileFixture(Path.Combine(parent, "profile"));
                using (new LiveClaudeProfile(fixture.Context)) { }
                var path = Path.Combine(parent, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, name.EndsWith(".json") ? "{}" : "host instructions above the owned root");
                LiveClaudeProfileCases.Refuses(() => { using var p = new LiveClaudeProfile(fixture.Context); },
                    "unowned-instructions", "V-14", "above-root-" + name);
            }
            finally
            {
                Directory.Delete(parent, true);
            }
        }
    }

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
