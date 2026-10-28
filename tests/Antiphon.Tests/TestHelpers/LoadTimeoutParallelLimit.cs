using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Width-4 cap for the load-timeout classes. TUnit keys the semaphore by limiter
/// type, so classes that share this type share four slots when one process runs
/// them together. A nightly chunk of one class, and a class run alone, only
/// limits that class. Four matches <see cref="TerminalSeatReleaseLimit"/>.
/// </summary>
public sealed class LoadTimeoutParallelLimit : IParallelLimit
{
    public int Limit => 4;
}
