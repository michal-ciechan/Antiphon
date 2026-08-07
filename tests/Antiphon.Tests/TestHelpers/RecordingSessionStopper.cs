using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Records the sessions a task action asked to stop. The point of the seam: a Cancel that only
/// relabels the row leaves a Claude running against the run's cost ceiling, so "did it actually
/// stop the delegate" is the thing worth asserting.
/// </summary>
public sealed class RecordingSessionStopper : IDelegateSessionStopper
{
    private readonly List<Guid> _killed = [];

    public IReadOnlyList<Guid> Killed => _killed;

    /// <summary>Set to make the stopper throw — a runner that has already lost the session.</summary>
    public Exception? Throws { get; set; }

    public Task KillAsync(Guid sessionId, CancellationToken ct)
    {
        _killed.Add(sessionId);
        return Throws is null ? Task.CompletedTask : Task.FromException(Throws);
    }
}
