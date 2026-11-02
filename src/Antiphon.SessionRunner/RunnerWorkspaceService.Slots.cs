using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>CARD-0824 S1. The workspace slot half of the mirror RPC; absent unless the runner flag is on.</summary>
public sealed partial class RunnerWorkspaceService
{
    /// <summary>The configured allowed root (<c>/work</c>): slot ledger and retained evidence live here.</summary>
    internal string AllowedRoot => Path.GetDirectoryName(_worktreeRoot)!.Replace('\\', '/');

    internal string PrimaryRepository => _repository;

    internal string PublishedBranch => _publishedBranch;

    /// <summary>Set only when <c>PhoneHome:WorkspaceSlots:Enabled</c> is true.</summary>
    internal RunnerWorkspaceSlotPool? SlotPool { get; set; }

    private async Task<PhoneHomeWorkspaceMirrorResponse> MirrorSlotAsync(
        PhoneHomeWorkspaceMirrorRequest request, CancellationToken ct)
    {
        var pool = SlotPool ?? throw Refuse(WorkspaceSlotCodes.Disabled);
        if (!NamePattern.IsMatch(request.Name ?? ""))
            throw Refuse("Mirror name must be task-<8 hex>.");
        if (!ShaPattern.IsMatch(request.Sha ?? ""))
            throw Refuse("Mirror sha must be 40 lowercase hex characters.");
        if (!BranchPattern.IsMatch(request.Branch ?? "") || request.Branch!.Contains("..", StringComparison.Ordinal))
            throw Refuse("Mirror branch name is not admitted.");
        var binding = request.Binding ?? throw Refuse("A workspace slot lease needs a binding.");
        if (binding.TaskId == Guid.Empty || binding.PreparationId == Guid.Empty)
            throw Refuse("A workspace slot lease needs a task and preparation id.");
        if (!string.Equals(request.Name, "task-" + binding.TaskId.ToString("N")[..8], StringComparison.Ordinal))
            throw Refuse("Mirror name does not match the task id.");
        var (repository, cloneSource, named, secondary) = ResolveRepository(request.Repository);
        if (secondary)
            throw Refuse("Workspace slots cover the primary repository only.");
        await EnsureRepositoryAsync(repository, cloneSource, ct);
        if (named)
            await VerifyOriginAsync(repository, cloneSource, ct);
        var lease = await pool.LeaseAsync(
            new WorkspaceSlotLeaseRequest(binding.TaskId, binding.PreparationId, request.Branch!, request.Sha!, request.Cold == true), ct);
        return new PhoneHomeWorkspaceMirrorResponse(lease.Path, null, lease.SlotId, lease.Generation);
    }
}
