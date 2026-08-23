using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Seam between RunnerSession orchestration (transcript, input log, events) and the process that
/// actually hosts the child (pty-host or herdr pane). CARD-0160 extracts the previous inline
/// pty-host launch behind this interface so the herdr lane can share everything above it.
/// </summary>
internal interface ISessionChild : IAsyncDisposable
{
    Task<ChildStarted> LaunchAsync(RunnerLaunchRequest request, CancellationToken ct);
    Task WriteAsync(string input, CancellationToken ct);
    Task ResizeAsync(int cols, int rows, CancellationToken ct);
    Task<bool> KillAsync(CancellationToken ct);
    /// <summary>Null means push-driven (pty-host); herdr serves on-demand pane.read snapshots.</summary>
    Task<ChildScreen?> ReadScreenAsync(CancellationToken ct);
    event Action<ChildExit> Exited;
}

internal sealed record ChildStarted(int? ChildPid, int? HostPid, DateTime ChildStartUtc);

internal sealed record ChildExit(int? ExitCode, string Reason);

internal sealed record ChildScreen(string Text, long Revision);
