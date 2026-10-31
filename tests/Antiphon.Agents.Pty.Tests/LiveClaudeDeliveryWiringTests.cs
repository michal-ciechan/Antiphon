using System.Reflection;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit"), Timeout(30_000)]
[ParallelLimiter<ProcessSpawnLimit>]
public class LiveClaudeDeliveryWiringTests
{
    private static async Task RunAsync(string witness)
    {
        var entry = typeof(LiveClaudeDeliveryWiringTests).Assembly.GetType("Antiphon.Agents.Pty.Tests.LiveClaudeDeliveryCases");
        if (entry?.GetMethod("RunAsync", BindingFlags.Static | BindingFlags.Public) is not { } run)
            throw new InvalidOperationException($"C1545 {witness}/entry required delivery oracle is absent");
        try { await (Task)run.Invoke(null, [witness])!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
    }
    [Test] public Task PromptReceiptPrecedesVerdict() => RunAsync("V-23");
    [Test] public Task LostEnterCannotPass() => RunAsync("V-24");
    [Test] public Task DroppedBridgeCannotPass() => RunAsync("V-25");
    [Test] public Task LiveMethodsUseTranscriptVerdicts() => RunAsync("V-26");
}
