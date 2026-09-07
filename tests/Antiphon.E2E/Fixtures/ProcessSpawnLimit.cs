using TUnit.Core.Interfaces;

namespace Antiphon.E2E.Fixtures;

public sealed class ProcessSpawnLimit : IParallelLimit
{
    public int Limit => 1;
}
