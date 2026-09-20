using System.Diagnostics;

namespace Antiphon.Card0490.NativeHarness;

public sealed record QemuLaunchSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    bool UseShellExecute = false,
    bool CreateNoWindow = true);

public sealed class OwnedQemuProcess
{
    public Process? Process { get; private set; }
    public bool ChildExited { get; set; }
    public bool StdoutDrained { get; set; }
    public bool StderrDrained { get; set; }
    public List<QemuLaunchSpec> LaunchIntents { get; } = [];
    public List<int> StopTargets { get; } = [];

    public bool Completes => ChildExited && StdoutDrained && StderrDrained;

    public bool TryLaunch(QemuLaunchSpec spec, QemuLaunchSpec allowed, Action<QemuLaunchSpec>? sink = null)
    {
        if (!SpecEquals(spec, allowed))
        {
            sink?.Invoke(spec);
            LaunchIntents.Add(spec);
            return false;
        }

        if (spec.UseShellExecute)
        {
            sink?.Invoke(spec);
            LaunchIntents.Add(spec);
            return false;
        }

        LaunchIntents.Add(spec);
        return true;
    }

    public void Stop(int ownedPid, int? discoveredPid = null)
    {
        StopTargets.Add(discoveredPid ?? ownedPid);
    }

    public static bool SpecEquals(QemuLaunchSpec actual, QemuLaunchSpec allowed) =>
        string.Equals(actual.FileName, allowed.FileName, StringComparison.OrdinalIgnoreCase)
        && actual.Arguments.SequenceEqual(allowed.Arguments)
        && !actual.UseShellExecute
        && !actual.Arguments.Any(a => a is "-accel" or "kvm" or "whpx" or "-daemonize" or "-incoming" or "-nic");
}
