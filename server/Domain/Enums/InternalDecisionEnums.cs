namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Bounded mechanical categories a dispatch may grant for CARD-0407 internal decisions.
/// There is no catch-all; a future category needs a reviewed contract.
/// </summary>
public enum InternalDecisionCategory
{
    LineEndings = 0,
    ShellTransport = 1,
    BuildTestHarness = 2,
}
