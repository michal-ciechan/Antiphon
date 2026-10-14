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
    IReadOnlyDictionary<string, string>? Environment = null);

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
