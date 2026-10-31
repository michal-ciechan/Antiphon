using Antiphon.TestSupport.LiveClaude;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit"), Timeout(30_000)]
public class LiveClaudeIsolationWiringTests
{
    private static void Run(string v)
    {
        var type = typeof(LiveClaudeLaunchContext).Assembly.GetType("Antiphon.TestSupport.LiveClaude.LiveClaudeProfileCases");
        if (type is null)
        {
            var startup = LiveClaudeExecutableAudit.Read("tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs");
            if (!startup.Contains("SpecialFolder.UserProfile") || !startup.Contains("var extra = new[] { \"--settings\""))
                throw new InvalidOperationException("C1545 baseline changed unexpectedly");
            throw new InvalidOperationException($"C1545 {v}/baseline legacy UserProfile/inline overlay, omitted cwd/model and no measured source projection; required profile entry missing");
        }
        try { type.GetMethod("Run")!.Invoke(null, [v, false]); }
        catch (System.Reflection.TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); }
    }
    [Test] public void DedicatedConfigOwnsTrustAndEvidence() => Run("V-7");
    [Test] public void CwdInstructionsCannotLeak() => Run("V-8");
    [Test] public void SettingsProjectionDetectsDrift() => Run("V-9");
    [Test] public void UnknownConfigDisablesReuse() => Run("V-10");
    [Test] public void LaunchArgumentsKeepProductionSources() => Run("V-11");
    [Test] public void ModelSelectionComesFromContext() => Run("V-12");
}
