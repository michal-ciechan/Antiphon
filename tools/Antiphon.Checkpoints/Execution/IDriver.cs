namespace Antiphon.Checkpoints;

public interface IPlatform
{
    bool IsWindows { get; }
}

public sealed class RuntimePlatform : IPlatform
{
    public bool IsWindows => OperatingSystem.IsWindows();
}

public sealed record DriverRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string? LogPath = null,
    Action<string>? OnOutput = null,
    IReadOnlyDictionary<string, string>? Environment = null)
{
    // Stamps of non-output files taken before the build slot; the build stamps only the rest.
    public BuildConfinement.Ledger? Ledger { get; init; }
}

public sealed record DriverResult(int ExitCode, string Stdout, string Stderr, bool Killed = false, bool TimedOut = false)
{
    // Set only by a driver that observed the child exit and its redirected output close (CARD-1242).
    public DateTimeOffset? Exited { get; init; }
}

public interface IDriver
{
    Task<DriverResult> RunAsync(DriverRequest request, CancellationToken cancellationToken);

    void Kill(bool entireProcessTree);
}

// A driver that confines real builds lets the scheduler stamp non-output files before it takes
// the build slot and verify them after release, so the slot covers the build itself (CARD-1241).
public interface IConfinedDriver : IDriver
{
    BuildConfinement.LedgerCapture CaptureLedger(DriverRequest request);
}
