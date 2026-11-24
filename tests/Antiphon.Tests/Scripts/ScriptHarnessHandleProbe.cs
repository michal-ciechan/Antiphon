using System.Diagnostics;
using Shouldly;

namespace Antiphon.Tests.Scripts;

internal static class ScriptHarnessHandleProbe
{
    internal static async Task<string[]> WaitAsync(string directory, string nonce, Task root, Func<bool> rootExecuting)
    {
        var clock = Stopwatch.StartNew();
        var path = Path.Combine(directory, "probe-receipt");
        while (!File.Exists(path))
        {
            if (root.IsCompleted)
                throw new InvalidOperationException("Root exited before handle probe.", await ScriptHarnessProcessFixture.CaptureAsync(root));
            if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Handle probe readiness deadline.");
            await Task.Delay(20);
        }
        var fields = File.ReadAllText(path).Split(' ');
        fields.Length.ShouldBe(7);
        fields[0].ShouldBe(nonce);
        rootExecuting().ShouldBeTrue();
        return fields;
    }

    internal static void AssertOnlyStandardHandles(string[] receipt, bool eventIsSignaled, IntPtr[] startupHandles)
    {
        bool.Parse(receipt[3]).ShouldBeFalse("Child cannot signal the unrelated inheritable event.");
        eventIsSignaled.ShouldBeFalse();
        var actualStandard = receipt.Skip(4).Select(value => new IntPtr(long.Parse(value))).ToArray();
        actualStandard.ShouldBe(startupHandles);
    }
}
