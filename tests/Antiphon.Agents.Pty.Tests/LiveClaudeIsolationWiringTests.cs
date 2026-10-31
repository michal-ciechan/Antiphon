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
    [Test] public void CwdInstructionsCannotLeak() => Run("V-8");
    [Test] public void SettingsProjectionDetectsDrift() => Run("V-9");
    [Test] public void UnknownConfigDisablesReuse() => Run("V-10");
    [Test] public void LaunchArgumentsKeepProductionSources() => Run("V-11");
    [Test] public void ModelSelectionComesFromContext() => Run("V-12");
}
