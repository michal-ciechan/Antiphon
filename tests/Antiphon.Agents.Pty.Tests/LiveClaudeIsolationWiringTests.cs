using Antiphon.TestSupport.LiveClaude;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit"), Timeout(30_000)]
public class LiveClaudeIsolationWiringTests
{
    private static LiveClaudeProfileSpec Build(LiveClaudeProfile profile,string role,string session,string? overlay)
        => role is "control" or "off" or "slash"
            ? ClaudeRemoteControlAtStartupCanaryTests.BuildProfileLaunch(profile,role,session,overlay)
            : ClSession.BuildProfileLaunch(profile,role,session,overlay);
    private static void Run(string v)
    {
        LiveClaudeProfileCases.Run(v,Build,ClaudeRemoteControlAtStartupCanaryTests.FindSessionJsonl,
            ClaudeRemoteControlAtStartupCanaryTests.ReadSessionFile,ClSession.ProjectProfile);
        if(v=="V-7")
        {
            using var fixture=new LiveClaudeProfileFixture();
            using var profile=new LiveClaudeProfile(fixture.Context);
            using var sandbox=ClaudeRemoteControlAtStartupCanaryTests.CanarySandbox.Create(profile,"control");
            LiveClaudeProfileCases.Assert(sandbox.Dir==profile.Paths.Cwd,v,"sandbox","actual startup sandbox owns dedicated cwd/trust");
        }
        LiveClaudeProfileAudit.PtyCallers();
        LiveClaudeProfileMutants.Run(v);
    }
    [Test] public void DedicatedConfigOwnsTrustAndEvidence() => Run("V-7");
    [Test]
    public void CwdInstructionsCannotLeak()
    {
        Run("V-8");
        using var f = new LiveClaudeProfileFixture();
        var runtime = Path.Combine(f.Root, "owned-node");
        var writer = Path.Combine(f.Context.ConfigDir!, "hooks", "settings-source-witness.mjs");
        var marker = Path.Combine(f.Context.OwnedRoot, "startup-settings.jsonl");
        f.Write(runtime, "inert owned interpreter", false);
        f.Write(writer, "// inert declared marker writer", false);
        f.Declared[runtime] = LiveClaudeProfile.HashFile(runtime);
        f.Declared[writer] = LiveClaudeProfile.HashFile(writer);
        foreach (var source in new[] { "user", "project", "local" })
            f.Write(SettingsPath(f, source), HookSettings(LiveClaudeProfile.HookCommand(runtime, writer, marker, source, "retained-model")));
        using (var profile = new LiveClaudeProfile(f.Context))
        {
            ClaudeRemoteControlAtStartupCanaryTests.RequireHookConfiguration(profile);
            try
            {
                foreach (var source in new[] { "user", "project", "local" })
                    f.Write(SettingsPath(f, source), HookSettings($"node \"{writer}\" \"{marker}\" {source} \"retained-model\""));
                LiveClaudeProfileCases.Refuses(() => { using var rejected = new LiveClaudeProfile(f.Context); }, "unowned-instructions", "V-8", "bare-node");
                LiveClaudeProfileCases.Refuses(() => ClaudeRemoteControlAtStartupCanaryTests.RequireHookConfiguration(profile), "settings-unobserved", "V-8", "bare-node-canary");
            }
            finally
            {
                foreach (var source in new[] { "user", "project", "local" })
                    f.Write(SettingsPath(f, source), HookSettings(LiveClaudeProfile.HookCommand(runtime, writer, marker, source, "retained-model")));
            }
        }
    }

    private static string SettingsPath(LiveClaudeProfileFixture f, string source) => source switch
    {
        "user" => Path.Combine(f.Context.ConfigDir!, "settings.json"),
        "project" => Path.Combine(f.Context.Cwd, ".claude", "settings.json"),
        _ => Path.Combine(f.Context.Cwd, ".claude", "settings.local.json")
    };

    private static string HookSettings(string command) =>
        "{\"remoteControlAtStartup\":true,\"model\":\"retained-model\",\"hooks\":{\"SessionStart\":[{\"hooks\":[{\"type\":\"command\",\"command\":"
        + System.Text.Json.JsonSerializer.Serialize(command) + "}]}]}}";
    [Test] public void SettingsProjectionDetectsDrift() => Run("V-9");
    [Test] public void UnknownConfigDisablesReuse() => Run("V-10");
    [Test] public void LaunchArgumentsKeepProductionSources() => Run("V-11");
    [Test] public void ModelSelectionComesFromContext() => Run("V-12");
}
