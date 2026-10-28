using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Caps <c>TerminalRunnerSeatReleaseTests</c> so its rows do not start as one wave.
/// Run 28bbb58e started all 39 rows in the same millisecond (p95 61.2s, max 94.8s).
/// Four matches <see cref="GitProcessSpawnLimit"/>'s maximum. A hung row holds one
/// slot for the 180s method timeout; ceil(40/4)*180s = 1800s, under the 2400s
/// lone-class launch ceiling.
/// </summary>
public sealed class TerminalSeatReleaseLimit : IParallelLimit
{
    public int Limit => 4;
}
