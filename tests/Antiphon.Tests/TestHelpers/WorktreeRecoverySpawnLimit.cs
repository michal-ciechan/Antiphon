using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Each worktree recovery case owns its Git tree and database. A crash case joins the
/// killed child before launching its recovery child, so two cases need at most two workers.
/// </summary>
public sealed class WorktreeRecoverySpawnLimit : IParallelLimit
{
    public int Limit => 2;
}
