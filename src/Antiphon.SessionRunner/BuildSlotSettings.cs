namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0589 D-3/D-4: <c>SessionRunner:BuildSlots</c>. The defaults are the desktop's budget; the
/// server2 runner overrides them in <c>docker-compose.server2-runner.yml</c>
/// (<c>SessionRunner__BuildSlots__*</c>). <see cref="Enabled"/> false makes every acquire answer
/// <c>unlimited</c> — the rollback lever.
/// </summary>
public sealed class BuildSlotSettings
{
    public const string SectionName = "SessionRunner:BuildSlots";

    public bool Enabled { get; set; } = true;

    /// <summary>Concurrent build/test driver leases on this host.</summary>
    public int MaxConcurrent { get; set; } = 2;

    /// <summary>The <c>-maxcpucount:N</c> every grant carries for the wrapper to apply.</summary>
    public int MaxCpuCount { get; set; } = 4;

    /// <summary>A grant is refused while live available memory is below this (D-5); 0 disables the floor.</summary>
    public int MinAvailableMemoryMb { get; set; } = 6144;

    /// <summary>Above the 25.5-min full Antiphon.Tests run and the 21-min pathological build (CARD-0222).</summary>
    public int LeaseTtlMinutes { get; set; } = 90;

    /// <summary>The poll interval a refused wrapper is told to wait.</summary>
    public int RetryAfterMs { get; set; } = 15_000;

    /// <summary>A queued waiter that has not re-polled for this long loses its place.</summary>
    public int WaiterSilenceMs { get; set; } = 60_000;

    public int SweepIntervalMs { get; set; } = 30_000;

    public void Validate()
    {
        if (MaxConcurrent < 1)
            throw new InvalidOperationException($"{SectionName}:MaxConcurrent must be at least 1.");
        if (MaxCpuCount < 1)
            throw new InvalidOperationException($"{SectionName}:MaxCpuCount must be at least 1.");
        if (MinAvailableMemoryMb < 0)
            throw new InvalidOperationException($"{SectionName}:MinAvailableMemoryMb must not be negative.");
        if (LeaseTtlMinutes < 1)
            throw new InvalidOperationException($"{SectionName}:LeaseTtlMinutes must be at least 1.");
        if (RetryAfterMs < 1)
            throw new InvalidOperationException($"{SectionName}:RetryAfterMs must be positive.");
        if (WaiterSilenceMs < 1)
            throw new InvalidOperationException($"{SectionName}:WaiterSilenceMs must be positive.");
        if (SweepIntervalMs < 1)
            throw new InvalidOperationException($"{SectionName}:SweepIntervalMs must be positive.");
    }
}
