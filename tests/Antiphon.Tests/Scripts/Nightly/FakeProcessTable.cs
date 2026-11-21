using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S01: the process table of a test (the IsProcessAlive, GetProcessStartTimeUtc, GetParentProcess,
/// GetBootId and GetHostId seams). Like Write-C487Seams, every id above 0 is alive until Exit marks it gone; start
/// times and parents are what the test set, null and NightlyParent.None otherwise. Thread-safe.
/// </summary>
public sealed class FakeProcessTable : INightlyProcessTable
{
    private readonly object _lock = new();
    private readonly HashSet<int> _exited = [];
    private readonly Dictionary<int, DateTime> _starts = [];
    private readonly Dictionary<int, NightlyParent> _parents = [];

    /// <summary>This process's id.</summary>
    public int CurrentPid { get; set; } = 4242;

    /// <summary>The BootId answer.</summary>
    public string Boot { get; set; } = "fake-boot";

    /// <summary>The HostId answer.</summary>
    public string Host { get; set; } = "fake-host";

    /// <summary>Marks the process alive again, with its start time when given.</summary>
    public FakeProcessTable Start(int pid, DateTime? startedUtc = null)
    {
        lock (_lock)
        {
            _exited.Remove(pid);
            if (startedUtc is { } started) _starts[pid] = DateTime.SpecifyKind(started, DateTimeKind.Utc);
        }
        return this;
    }

    /// <summary>Marks the process gone: not alive, no start time.</summary>
    public FakeProcessTable Exit(int pid)
    {
        lock (_lock)
        {
            _exited.Add(pid);
            _starts.Remove(pid);
        }
        return this;
    }

    /// <summary>Sets the process's parent.</summary>
    public FakeProcessTable SetParent(int pid, int parentPid, DateTime? parentStartedUtc = null)
    {
        lock (_lock) _parents[pid] = new NightlyParent(parentPid, parentStartedUtc);
        return this;
    }

    /// <inheritdoc />
    public DateTime? StartTimeUtc(int pid)
    {
        lock (_lock) return _starts.TryGetValue(pid, out var started) ? started : null;
    }

    /// <inheritdoc />
    public bool IsAlive(int pid)
    {
        lock (_lock) return pid > 0 && !_exited.Contains(pid);
    }

    /// <inheritdoc />
    public NightlyParent Parent(int pid)
    {
        lock (_lock) return _parents.TryGetValue(pid, out var parent) ? parent : NightlyParent.None;
    }

    /// <inheritdoc />
    public string BootId() => Boot;

    /// <inheritdoc />
    public string HostId() => Host;
}

/// <summary>
/// The host probe of a test: the C487 seam rows (Docker exit 0 "fake"/"ok", 50 GB free over a 10 GB floor), platform
/// windows (the HostPlatform seam), and the ToolchainVersions seam's git 2.42.0 and SDK 10.0.204. Every answer is settable.
/// </summary>
public sealed class FakeHostProbe : INightlyHostProbe
{
    private const long GiB = 1024L * 1024L * 1024L;

    /// <summary>The Platform answer.</summary>
    public string PlatformName { get; set; } = "windows";

    /// <summary>The Docker answer.</summary>
    public NightlyProbeRow DockerRow { get; set; } = new() { ExitCode = 0, Version = "fake", Detail = "ok" };

    /// <summary>The DiskFree answer.</summary>
    public NightlyProbeRow DiskRow { get; set; } = new() { ExitCode = 0, FreeBytes = 50 * GiB, MinimumFreeBytes = 10 * GiB };

    /// <summary>The ToolVersion answers by tool; a tool not listed answers empty.</summary>
    public Dictionary<string, string> Versions { get; } = new(StringComparer.Ordinal)
    {
        ["git"] = "git version 2.42.0",
        ["dotnet-sdk"] = "10.0.204 [/fake/sdk]",
    };

    /// <inheritdoc />
    public string Platform() => PlatformName;

    /// <inheritdoc />
    public NightlyProbeRow Docker() => DockerRow;

    /// <inheritdoc />
    public NightlyProbeRow DiskFree() => DiskRow;

    /// <inheritdoc />
    public string ToolVersion(string tool) => Versions.TryGetValue(tool, out var text) ? text : "";
}
