using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>Boot-scoped monotonic source identity, independent of clocks and payload labels.</summary>
public sealed class HostLoadIdentity(string runnerId, Guid runnerStoreId, Guid? boot = null)
{
    private readonly Guid _boot = boot ?? Guid.NewGuid();
    private long _sequence;
    public HostLoadSource Next() => At(Interlocked.Increment(ref _sequence));
    public HostLoadSource At(long sequence) => sequence > 0
        ? new(runnerId, runnerStoreId, _boot, sequence) : throw new ArgumentOutOfRangeException(nameof(sequence));
}
