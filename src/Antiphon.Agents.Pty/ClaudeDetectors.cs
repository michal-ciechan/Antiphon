namespace Antiphon.Agents.Pty;

public sealed class ClaudeReadyDetector
{
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromMilliseconds(1500);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(30);

    public Task<bool> WaitAsync(PtyAgentRunner runner, CancellationToken ct = default)
        => runner.WaitForQuietAsync(QuietPeriod, MaxWait, ct);
}

public sealed class ClaudeDoneDetector
{
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(2);

    public Task<bool> WaitAsync(PtyAgentRunner runner, CancellationToken ct = default)
        => runner.WaitForQuietAsync(QuietPeriod, MaxWait, ct);
}
