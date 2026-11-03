using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0824 S1c (T-D-1). The grammar of <c>Delegation:WorkspaceSlotRoles</c>: exactly <c>*</c> (every role except
/// Mutation), <c>none</c> (no new leases), or a comma-separated list of <see cref="AgentTaskRole"/> names in canonical
/// case with no whitespace and no empty entry. <c>Mutation</c> cannot be listed, and <c>*</c> and <c>none</c> stand alone.
/// </summary>
public static class WorkspaceSlotRoles
{
    public const string SettingName = "Delegation:WorkspaceSlotRoles";
    public const string All = "*";
    public const string None = "none";

    /// <summary>The role names a list may carry: every <see cref="AgentTaskRole"/> name except Mutation.</summary>
    public static IReadOnlyList<string> ListableNames { get; } =
        Enum.GetNames<AgentTaskRole>().Where(name => name != nameof(AgentTaskRole.Mutation)).ToArray();

    public static bool TryParse(string? value, out WorkspaceSlotRoleSet set, out string error)
    {
        // S1c red-first stub: today's behaviour, every value admits every role.
        set = WorkspaceSlotRoleSet.Everything;
        error = "";
        return true;
    }
}

/// <summary>The lease verdict for one role: <see cref="Reason"/> is <c>all</c>, <c>listed</c>, <c>role_not_listed</c>,
/// <c>roles_none</c>, <c>roles_invalid</c>, <c>mutation</c> or <c>source_landing</c>.</summary>
public readonly record struct WorkspaceSlotRoleVerdict(bool Lease, string Reason)
{
    public const string AllReason = "all";
    public const string Listed = "listed";
    public const string RoleNotListed = "role_not_listed";
    public const string RolesNone = "roles_none";
    public const string RolesInvalid = "roles_invalid";
    public const string Mutation = "mutation";
    public const string SourceLanding = "source_landing";
}

/// <summary>A parsed <c>Delegation:WorkspaceSlotRoles</c> value.</summary>
public sealed class WorkspaceSlotRoleSet
{
    private WorkspaceSlotRoleSet()
    {
    }

    public static WorkspaceSlotRoleSet Everything { get; } = new();

    public WorkspaceSlotRoleVerdict Admits(AgentTaskRole role, Guid? sourceLandingOperationId) =>
        new(true, WorkspaceSlotRoleVerdict.AllReason);
}
