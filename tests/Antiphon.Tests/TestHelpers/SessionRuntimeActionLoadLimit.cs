using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Caps <c>SessionRuntimeActionTests</c> so its 28 rows start four at a time.
/// Run 28bbb58e placed the class in a 193-row wave and the 10s page yield expired.
/// Four matches <see cref="GitProcessSpawnLimit"/>'s maximum. A missed hook holds
/// one slot for the 70s success cap; ceil(28/4)*70s = 490s, under the 2400s
/// lone-class launch ceiling. Unloaded Linux wall for the class was 36-39s.
/// </summary>
public sealed class SessionRuntimeActionLoadLimit : IParallelLimit
{
    public int Limit => 4;
}
