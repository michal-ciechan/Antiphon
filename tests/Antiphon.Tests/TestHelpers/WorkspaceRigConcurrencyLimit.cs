using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>One shared lane for the workspace/retention rigs, matching the measured clone capacity.
/// TUnit admits cases before their timed bodies; per-test database and clock isolation stay intact.
/// Bind only to scripted methods; real transports bind only the strict process-spawn limiter.</summary>
public sealed class WorkspaceRigConcurrencyLimit : IParallelLimit
{
    public int Limit => TestDbServerSettings.DefaultCloneWidth;
}
