using System.Diagnostics;
using Antiphon.Tests.Checkpoints;
using TUnit.Core;

namespace Antiphon.Checkpoints.LifecycleHost;

public sealed class LifecycleHostTests : CheckpointTestBase
{
    [Test]
    public void passing()
    {
        var root = TempDir();
        Publish(root);
        File.WriteAllText(Path.Combine(root, "sentinel"), "pass");
    }

    [Test]
    public void assertion_failure()
    {
        Publish(TempDir());
        throw new InvalidOperationException("deliberate assertion path");
    }

    [Test]
    public void cancellation()
    {
        Publish(TempDir());
        throw new OperationCanceledException("deliberate cancellation path");
    }

    [Test]
    public void multiple_roots()
    {
        Publish(TempDir());
        Publish(TempDir());
    }

    [Test]
    public async Task early_assertion_with_child()
    {
        Publish(TempDir());
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd", "/c ping -n 30 127.0.0.1 >nul")
            : new ProcessStartInfo("sleep", "30");
        psi.UseShellExecute = false;
        using var child = Process.Start(psi)!;
        RegisterCheckpointChild(child);
        var pidFile = Environment.GetEnvironmentVariable("C804_LIFECYCLE_CHILD_PID")!;
        File.WriteAllText(pidFile, child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Task.Delay(100);
        throw new InvalidOperationException("deliberate early failure");
    }

    internal static void Publish(string root)
    {
        var target = Environment.GetEnvironmentVariable("C804_LIFECYCLE_ROOTS")
            ?? throw new InvalidOperationException("missing lifecycle inventory");
        File.AppendAllText(target, root + Environment.NewLine);
    }
}

public sealed class LifecycleSetupFailureTests : CheckpointTestBase
{
    [Before(Test)]
    public void FailSetup()
    {
        LifecycleHostTests.Publish(TempDir());
        throw new InvalidOperationException("deliberate setup failure");
    }

    [Test]
    public void setup_failure() { }
}
