using System.Collections;
using Antiphon.Nightly;

namespace Antiphon.Tests.Application;

/// <summary>
/// The land-graph commands (land-graph-audit, land-graph-precheck, land-graph-land-refresh; unit 11 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md), and regenerate-derived-impact-index (unit 41), in
/// process, over real children. The production CLI lets
/// the audit tool write to this process's own streams; here that output lands in the host's writers instead, so a test
/// reads what an operator would see. git, dotnet and the tool are real processes (WatchedLauncher).
/// </summary>
internal static class LandGraphCommandHost
{
    /// <summary>A host on the system clock, real git and the real process table, writing to output and error.</summary>
    public static NightlyHost Create(TextWriter output, TextWriter error, Func<NightlyLaunch, NightlyLaunch>? prepareLaunch = null) => new()
    {
        Clock = TimeProvider.System,
        Git = NightlyNativeGit.Run,
        Launcher = new StreamCapturingLauncher(output, error, prepareLaunch),
        Processes = new NightlyProcessTable(),
        Probe = new NightlyHostProbe(),
        Out = output,
        Error = error,
        Trace = TextWriter.Null,
    };

    /// <summary>This process's environment with the overrides applied (a null value removes the name), as the CLI passes it in.</summary>
    public static IReadOnlyDictionary<string, string> Environment(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var snapshot = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables()) snapshot[(string)entry.Key] = (string)entry.Value!;
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            if (value is null) snapshot.Remove(key);
            else snapshot[key] = value;
        }
        return snapshot;
    }

    /// <summary>
    /// WatchedLauncher, except that a launch sharing this process's streams (no CaptureOutput) is captured and its stdout
    /// and stderr are written to the host's writers once it has exited.
    /// </summary>
    private sealed class StreamCapturingLauncher(
        TextWriter output, TextWriter error, Func<NightlyLaunch, NightlyLaunch>? prepareLaunch) : INightlyLauncher
    {
        private readonly WatchedLauncher _inner = new();

        public NightlyLaunchResult Run(NightlyLaunch launch)
        {
            launch = prepareLaunch?.Invoke(launch) ?? launch;
            if (launch.CaptureOutput) return _inner.Run(launch);
            var result = _inner.Run(launch with { CaptureOutput = true });
            lock (output)
            {
                output.Write(result.StandardOutputText);
                error.Write(result.StandardErrorText);
            }
            return result;
        }

        public bool WaitCleanup(NightlyLaunchResult result) => true;

        public int NativeActiveCount => 0;
    }
}
