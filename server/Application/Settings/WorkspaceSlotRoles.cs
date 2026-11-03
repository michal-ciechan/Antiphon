using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0824 S1c (T-D-1). The grammar of <c>Delegation:WorkspaceSlotRoles</c>: exactly <c>*</c> (every role except
/// Mutation), <c>none</c> (no new leases), or a comma-separated list of <see cref="AgentTaskRole"/> names in canonical
/// case with no whitespace and no empty entry. <c>Mutation</c> cannot be listed, and <c>*</c> and <c>none</c> stand alone.
/// Names are compared ordinally against <see cref="Enum.GetNames{TEnum}"/>, never through <c>Enum.TryParse</c>, which
/// would also take a number or another case.
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
        set = WorkspaceSlotRoleSet.Nothing;
        error = "";
        if (value == All)
        {
            set = WorkspaceSlotRoleSet.Everything;
            return true;
        }

        if (value == None)
            return true;

        if (string.IsNullOrEmpty(value))
            return Refuse("is empty", out error);

        var listed = new HashSet<AgentTaskRole>();
        foreach (var entry in value.Split(','))
        {
            if (entry.Length == 0)
                return Refuse($"'{value}' has an empty entry", out error);
            if (entry is All or None)
                return Refuse($"'{value}' mixes '{entry}' with role names; '*' and 'none' stand alone", out error);
            if (!ListableNames.Contains(entry, StringComparer.Ordinal))
                return Refuse(entry == nameof(AgentTaskRole.Mutation)
                    ? $"'{value}' lists Mutation, which never leases a slot"
                    : $"'{value}' has '{entry}', which is not a role name in canonical case", out error);
            listed.Add(Enum.Parse<AgentTaskRole>(entry));
        }

        set = WorkspaceSlotRoleSet.Listing(listed);
        return true;
    }

    private static bool Refuse(string problem, out string error)
    {
        error = $"{SettingName} {problem}. Use '*' (every role except Mutation), 'none', or a comma-separated list "
            + $"of exact role names without spaces from: {string.Join(", ", ListableNames)}.";
        return false;
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
    private readonly bool _all;
    private readonly IReadOnlySet<AgentTaskRole>? _listed;

    private WorkspaceSlotRoleSet(bool all, IReadOnlySet<AgentTaskRole>? listed)
    {
        _all = all;
        _listed = listed;
    }

    public static WorkspaceSlotRoleSet Everything { get; } = new(true, null);

    public static WorkspaceSlotRoleSet Nothing { get; } = new(false, null);

    internal static WorkspaceSlotRoleSet Listing(IReadOnlySet<AgentTaskRole> listed) => new(false, listed);

    /// <summary>
    /// A SourceLanding task never leases (T-D-6, beside the dispatcher's own D-8 guard), <c>none</c> admits nothing,
    /// and Mutation never leases whatever the value (A-D-3).
    /// </summary>
    public WorkspaceSlotRoleVerdict Admits(AgentTaskRole role, Guid? sourceLandingOperationId)
    {
        if (sourceLandingOperationId is not null)
            return new(false, WorkspaceSlotRoleVerdict.SourceLanding);
        if (!_all && _listed is null)
            return new(false, WorkspaceSlotRoleVerdict.RolesNone);
        if (role == AgentTaskRole.Mutation)
            return new(false, WorkspaceSlotRoleVerdict.Mutation);
        if (_all)
            return new(true, WorkspaceSlotRoleVerdict.AllReason);
        return _listed!.Contains(role)
            ? new(true, WorkspaceSlotRoleVerdict.Listed)
            : new(false, WorkspaceSlotRoleVerdict.RoleNotListed);
    }
}
