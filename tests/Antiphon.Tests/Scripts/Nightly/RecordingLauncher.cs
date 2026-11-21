using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S01: the launcher seam of a test (plan section 2 rule 3; the StartProcess seam of Write-C487Seams). It
/// starts nothing: each launch is recorded, with its "EXEC &lt;file&gt; &lt;arguments&gt;" line (the C487 trace), and answered by
/// the behaviour (FakeTestHost.NativePass, or a test's own), or by Passed when there is none. Thread-safe.
/// </summary>
public sealed class RecordingLauncher : INightlyLauncher
{
    private readonly object _gate = new();
    private readonly List<NightlyLaunch> _launches = [];
    private readonly Func<NightlyLaunch, NightlyLaunchResult>? _behaviour;

    /// <summary>A launcher answering with behaviour (null: every launch passes).</summary>
    public RecordingLauncher(Func<NightlyLaunch, NightlyLaunchResult>? behaviour = null) => _behaviour = behaviour;

    /// <summary>The launches so far, in order.</summary>
    public IReadOnlyList<NightlyLaunch> Launches
    {
        get { lock (_gate) return _launches.ToArray(); }
    }

    /// <summary>The C487 trace of the launches: "EXEC &lt;file&gt; &lt;arguments joined by spaces&gt;".</summary>
    public IReadOnlyList<string> Lines => Launches.Select(launch => "EXEC " + launch.CommandText).ToArray();

    /// <summary>The WaitCleanup answer; null answers ChildrenExited (Wait-NightlyOwnedCleanup over a seam result).</summary>
    public Func<NightlyLaunchResult, bool>? Cleanup { get; set; }

    /// <summary>The NativeActiveCount answer (the NativeActiveCount seam).</summary>
    public int NativeActiveCount { get; set; }

    /// <summary>A passing launch: exit 0, pid 2, children exited, the C487 invocation log.</summary>
    public static NightlyLaunchResult Passed() =>
        new() { ExitCode = 0, TimedOut = false, Pid = 2, ChildrenExited = true, LogText = FakeTestHost.InvocationLog };

    /// <inheritdoc />
    public NightlyLaunchResult Run(NightlyLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        lock (_gate) _launches.Add(launch);
        return _behaviour?.Invoke(launch) ?? Passed();
    }

    /// <inheritdoc />
    public bool WaitCleanup(NightlyLaunchResult result) => Cleanup?.Invoke(result) ?? result.ChildrenExited;
}
