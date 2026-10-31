using Antiphon.TestSupport.LiveClaude;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit"), Timeout(30_000)]
public class LiveLaneIsolationTests
{
    private static void Run(string v)
    {
        var type = typeof(LiveClaudeLaunchContext).Assembly.GetType("Antiphon.TestSupport.LiveClaude.LiveClaudeProfileCases");
        if (type is null)
        {
            var caller = LiveClaudeExecutableAudit.Read("tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs");
            if (!caller.Contains("ClaudeProjectTrust.Seed(cwd)") || !caller.Contains("? \"haiku\""))
                throw new InvalidOperationException("C1545 baseline changed unexpectedly");
            throw new InvalidOperationException($"C1545 {v}/baseline legacy ambient trust and hard-coded model; required profile/parity entry missing");
        }
        try { type.GetMethod("Run")!.Invoke(null, [v, true]); }
        catch (System.Reflection.TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); }
    }
    [Test] public void DedicatedConfigOwnsTrustAndEvidence() => Run("V-13");
    [Test] public void CwdInstructionsCannotLeak() => Run("V-14");
    [Test] public void SettingsProjectionDetectsDrift() => Run("V-15");
    [Test] public void UnknownConfigDisablesReuse() => Run("V-16");
    [Test, Explicit] public void StartupArgvMatchesProductionApplyOff() => Run("V-17");
}
