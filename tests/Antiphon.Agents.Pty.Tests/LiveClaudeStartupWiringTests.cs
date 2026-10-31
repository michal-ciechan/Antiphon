using System.Reflection;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit"), Timeout(30_000)]
[ParallelLimiter<ProcessSpawnLimit>]
public class LiveClaudeStartupWiringTests
{
    private static async Task RunAsync(string witness)
    {
        var entry = typeof(LiveClaudeStartupWiringTests).Assembly.GetType(
            "Antiphon.Agents.Pty.Tests.LiveClaudeStartupCases");
        if (entry?.GetMethod("RunAsync", BindingFlags.Static | BindingFlags.Public) is not { } run)
            throw new InvalidOperationException($"C1545 {witness}/entry required startup oracle is absent");
        try { await (Task)run.Invoke(null, [witness])!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
    }

    [Test] public Task ExplicitBridgeSetup() => RunAsync("V-18");
    [Test] public Task ControlRequiredForSettingsOff() => RunAsync("V-19");
    [Test] public Task ControlWindowBoundsOffObservation() => RunAsync("V-20");
    [Test] public Task StartupMethodsUseControlOracle() => RunAsync("V-21");
    [Test] public Task LoadedSettingsSurviveOffOverlay() => RunAsync("V-22");
}
