using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public sealed partial class RunnerWorkspaceService
{
    internal string InspectionPublishedRef => "refs/heads/" + _publishedBranch;
    internal static bool InspectionPathsEqual(string left, string right) => PathsEqual(left, right);

    internal string? InspectionBindingReason(CompletedCardWorkspaceBinding binding)
    {
        if (binding.RunnerId != _recipientRunnerId || !Guid.TryParse(binding.RunnerStoreId, out var storeId)
            || storeId == Guid.Empty || storeId != _recipientStoreId) return "runner_store_mismatch";
        var store = OpenBindingStore();
        if (!RunnerCompletedCardWorkspaceRemoval.UnlinkedPath(store.StateRoot)
            || !RunnerCompletedCardWorkspaceRemoval.UnlinkedPath(store.RecordPath(binding.PreparationId)))
            return "recipient_store_unreadable";
        var read = store.Read(binding.PreparationId);
        if (read.Kind == BindingReadKind.Unreadable) return "recipient_store_unreadable";
        if (read.Kind != BindingReadKind.Complete || read.Record is null) return "unbound_mirror";
        if (ToBindingResponse(read.Record).Binding != binding) return "foreign_mirror_owner";
        if (!Path.GetFileName(binding.WorktreePath).Equals("task-" + binding.TaskId.ToString("N")[..8], StringComparison.Ordinal)
            || !InspectionPathsEqual(Path.GetDirectoryName(binding.WorktreePath)!, _worktreeRoot))
            return "stale_mirror_binding";
        return null;
    }

    internal async Task<string?> InspectionPhysicalIdentityReasonAsync(CompletedCardWorkspaceBinding binding, CancellationToken ct)
    {
        var common = await GitAsync(binding.WorktreePath, ct, "rev-parse", "--path-format=absolute", "--git-common-dir");
        var admin = await GitAsync(binding.WorktreePath, ct, "rev-parse", "--path-format=absolute", "--git-dir");
        if (common.ExitCode != 0 || admin.ExitCode != 0
            || !PathsEqual(common.Stdout.Trim(), binding.CommonDirectory) || !PathsEqual(admin.Stdout.Trim(), binding.GitDirectory))
            return "stale_mirror_binding";
        var store = OpenBindingStore();
        var record = store.Read(binding.PreparationId).Record;
        var stamp = RunnerWorkspaceBindingStore.OwnerStampPath(binding.GitDirectory);
        if (record is null || !RunnerCompletedCardWorkspaceRemoval.UnlinkedPath(stamp)
            || !store.OwnerStampMatches(binding.GitDirectory, record)) return "foreign_mirror_owner";
        var origin = await GitAsync(binding.WorktreePath, ct, "remote", "get-url", "origin");
        if (origin.ExitCode != 0 || (!RepositoryCloneSource.TryNormalize(origin.Stdout.Trim(), out var identity) || identity != binding.RepositoryIdentity))
            return "stale_mirror_binding";
        return null;
    }
}
