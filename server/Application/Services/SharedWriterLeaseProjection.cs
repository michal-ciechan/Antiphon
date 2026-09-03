using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Pure snapshot of the shared-writer lease (CARD-0304). Given the currently running writers and
/// one queued task, which of those writers would serialise it <em>right now</em>? Never writes
/// events or status, and never treats a historical <c>Held</c> event as a live hold.
/// </summary>
public static class SharedWriterLeaseProjection
{
    /// <summary>
    /// A running task's claim on part of a repo, as the tick sees it. Carries the task's identity
    /// because a hold that leaves no trace is the defect CARD-0063 measured — the event text names
    /// who is being waited for.
    /// </summary>
    public sealed record Holder(
        Guid TaskId,
        string Title,
        string Key,
        ResolvedScope Scope,
        WorkspaceMode Workspace,
        string? Branch)
    {
        public static Holder From(
            Guid taskId,
            string title,
            string? repoPath,
            string workingDirectory,
            string? scope,
            WorkspaceMode workspace,
            string? branch,
            AreaMap areas) =>
            new(
                taskId,
                title,
                ScopeResolver.KeyFor(repoPath, workingDirectory),
                ScopeResolver.Resolve(scope, areas),
                workspace,
                branch);

        public bool Intersects(string key, ResolvedScope scope) =>
            SameRepo(Key, key) && ScopeResolver.Intersects(Scope, scope);

        /// <summary>
        /// What this running task costs a queued one - or null when it costs nothing. Two arms:
        /// declared scopes that intersect, and (D3) two shared writers in one checkout, which is a
        /// collision regardless of scope because they share a working TREE, not a file list.
        /// </summary>
        public Overlap? Evaluate(
            string key, WorkspaceMode workspace, ResolvedScope scope, bool serialiseSharedWriters)
        {
            if (!SameRepo(Key, key))
                return null;

            var intersection = ScopeResolver.Intersect(Scope, scope);
            if (!intersection.Any)
            {
                return serialiseSharedWriters
                    && workspace == WorkspaceMode.Shared
                    && Workspace == WorkspaceMode.Shared
                    ? new Overlap(this, ScopeOverlapPolicy.Serialise, workspace, Areas: null)
                    : null;
            }

            var policy = ScopeResolver.PolicyFor(workspace, Workspace, intersection.AllAllow);
            return policy == ScopeOverlapPolicy.Allow
                ? null
                : new Overlap(this, policy, workspace, intersection.Describe());
        }
    }

    /// <summary>One running task's claim on a queued one, and the sentence it earns.</summary>
    public sealed record Overlap(
        Holder Holder, ScopeOverlapPolicy Policy, WorkspaceMode Queued, string? Areas)
    {
        public string Describe(AgentTask task)
        {
            var pair = ScopeResolver.DescribePair(Queued, Holder.Workspace);
            var who = $"running task {DelegationReportFormatter.Short(Holder.TaskId)} \"{Holder.Title}\"";
            if (Areas is null)
            {
                return $"Held: {who} is already writing in this shared checkout ({pair}; "
                    + "no intersecting scope - two shared writers share one working tree).";
            }

            if (Policy == ScopeOverlapPolicy.Serialise)
                return $"Held: '{Areas}' intersects {who} ({pair}).";

            var against = Holder.Branch ?? task.WorktreeBranch ?? "the shared checkout";
            return $"'{Areas}' intersects {who} ({pair}) - dispatching anyway; "
                + $"expect a rebase against {against}.";
        }
    }

    public sealed record Decision(Overlap? Blocking, IReadOnlyList<Overlap> Warnings)
    {
        public bool IsSerialised => Blocking is not null;
    }

    /// <summary>
    /// ReadOnly writes nothing and specialist-role tasks spawn nothing, so neither participates in
    /// the lease in either direction.
    /// </summary>
    public static bool Participates(WorkspaceMode workspace, AgentTaskRole role) =>
        ScopeResolver.ParticipatesInLease(workspace) && !AgentTaskRoles.IsSpecialist(role);

    /// <summary>
    /// First serialising overlap is the live hold; remaining Warn overlaps are worktree collisions
    /// that dispatch anyway. Order matches the dispatcher's scan of the holder snapshot.
    /// </summary>
    public static Decision Decide(
        IReadOnlyList<Holder> holders,
        string queuedKey,
        WorkspaceMode queuedWorkspace,
        ResolvedScope queuedScope,
        bool serialiseSharedWriters)
    {
        Overlap? blocking = null;
        List<Overlap>? warnings = null;
        foreach (var candidate in holders)
        {
            if (candidate.Evaluate(queuedKey, queuedWorkspace, queuedScope, serialiseSharedWriters)
                is not { } overlap)
                continue;
            if (overlap.Policy == ScopeOverlapPolicy.Serialise)
            {
                blocking = overlap;
                break;
            }

            (warnings ??= []).Add(overlap);
        }

        return new Decision(blocking, warnings ?? (IReadOnlyList<Overlap>)[]);
    }

    /// <summary>
    /// Every current serialising holder of a queued task — the pipeline's <c>heldBy</c> list.
    /// Walks the full snapshot; a historical Held event is irrelevant.
    /// </summary>
    public static IReadOnlyList<Overlap> SerialisingHolders(
        IReadOnlyList<Holder> holders,
        string queuedKey,
        WorkspaceMode queuedWorkspace,
        ResolvedScope queuedScope,
        bool serialiseSharedWriters)
    {
        var result = new List<Overlap>();
        foreach (var candidate in holders)
        {
            if (candidate.Evaluate(queuedKey, queuedWorkspace, queuedScope, serialiseSharedWriters)
                is { Policy: ScopeOverlapPolicy.Serialise } overlap)
            {
                result.Add(overlap);
            }
        }

        return result;
    }

    internal static bool SameRepo(string a, string b) =>
        string.Equals(
            DelegationWorkspaceResolver.NormalizeSeparators(a),
            DelegationWorkspaceResolver.NormalizeSeparators(b),
            StringComparison.OrdinalIgnoreCase);
}
