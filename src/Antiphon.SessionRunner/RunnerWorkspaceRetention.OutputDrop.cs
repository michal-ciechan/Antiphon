using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>CARD-1895. Check under the dispatcher's lock; the delete runs after the lock is released.</summary>
public sealed record WorkspaceOutputDropStep(
    PhoneHomeWorkspaceOutputDropResponse? Response,
    PhoneHomeWorkspaceOutputDropRequest? Request = null,
    string? HeadSha = null,
    string? OriginTip = null)
{
    public bool Eligible => Response is null && Request is not null;
}

public sealed partial class RunnerWorkspaceRetention
{
    private const int OutputDiscoveryDepth = 24;
    private const int OutputDiscoveryEntries = 100_000;

    /// <summary>CARD-1895. The retention checks, then the allow-listed delete. Tests call this as one step.</summary>
    public async Task<PhoneHomeWorkspaceOutputDropResponse> DropOutputsAsync(
        PhoneHomeWorkspaceOutputDropRequest request, CancellationToken ct)
    {
        var step = await CheckOutputsAsync(request, ct);
        return step.Response ?? await ApplyOutputsAsync(step, ct);
    }

    /// <summary>Eligibility only. A refusal carries the retention reason and deletes nothing.</summary>
    public async Task<WorkspaceOutputDropStep> CheckOutputsAsync(
        PhoneHomeWorkspaceOutputDropRequest request, CancellationToken ct)
    {
        var execute = request.Mode == WorkspaceOutputDropModes.Execute;
        if (!execute && request.Mode != WorkspaceOutputDropModes.DryRun)
            return Refused(Kept(WorkspaceRetentionCodes.OutsideScope, "mode must be DryRun or Execute"));
        var path = (request.Path ?? "").Replace('\\', '/').TrimEnd('/');
        var retain = new PhoneHomeWorkspaceRetainRequest(
            request.TaskId, path, request.Branch, WorkspaceRetainModes.DryRun, request.RequestId);
        var scope = Confine(retain, path);
        if (scope is not null)
            return Refused(scope);
        if (!EntryExists(path))
            return Refused(new PhoneHomeWorkspaceRetainResponse(
                WorkspaceOutputDropCodes.Kept, WorkspaceRetentionCodes.AlreadyAbsent));
        var step = await CheckAsync(retain, path, countNested: false, ct);
        if (step.Final is { } refused)
            return Refused(refused);
        return new WorkspaceOutputDropStep(null, request with { Path = path }, step.Facts?.HeadSha, step.Facts?.OriginTip);
    }

    /// <summary>
    /// Discover allow-listed directories and delete the ones that are real, untracked, and proven.
    /// The tree and its git registration stay.
    /// </summary>
    public async Task<PhoneHomeWorkspaceOutputDropResponse> ApplyOutputsAsync(
        WorkspaceOutputDropStep step, CancellationToken ct)
    {
        var request = step.Request ?? throw new InvalidOperationException("output drop apply without a request");
        var path = request.Path ?? "";
        var execute = request.Mode == WorkspaceOutputDropModes.Execute;
        List<FoundOutput> found;
        var bounded = false;
        try
        {
            (found, bounded) = Discover(path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Answer(WorkspaceOutputDropCodes.Kept, WorkspaceRetentionCodes.InspectionFailed, step, detail: ex.Message);
        }

        var entries = new List<WorkspaceOutputDropEntry>();
        var proven = new List<(FoundOutput Dir, long Bytes)>();
        foreach (var dir in found)
        {
            if (dir.Symlink)
            {
                entries.Add(new WorkspaceOutputDropEntry(dir.Relative, dir.Name, 0, WorkspaceOutputDropCodes.Kept, WorkspaceOutputDropCodes.Symlink));
                continue;
            }

            var listed = await Inspect(path, ct, "ls-files", "-z", "--", dir.Relative);
            if (listed is not { ExitCode: 0 })
            {
                entries.Add(new WorkspaceOutputDropEntry(dir.Relative, dir.Name, 0, WorkspaceOutputDropCodes.Kept, WorkspaceRetentionCodes.InspectionFailed));
                continue;
            }

            if (listed.Value.Stdout.Length > 0)
            {
                entries.Add(new WorkspaceOutputDropEntry(dir.Relative, dir.Name, 0, WorkspaceOutputDropCodes.Kept, WorkspaceOutputDropCodes.TrackedContent));
                continue;
            }

            try
            {
                var bytes = RunnerWorkspaceNoFollowDelete.MeasureOrDelete(dir.Full, delete: false, ct);
                proven.Add((dir, bytes));
            }
            catch (RunnerWorkspaceNoFollowDelete.Refusal refusal)
            {
                entries.Add(new WorkspaceOutputDropEntry(dir.Relative, dir.Name, 0, WorkspaceOutputDropCodes.Kept, refusal.Reason));
            }
        }

        if (!execute)
        {
            var previewBytes = proven.Sum(item => item.Bytes);
            foreach (var (dir, bytes) in proven)
                entries.Add(new WorkspaceOutputDropEntry(dir.Relative, dir.Name, bytes, WorkspaceOutputDropCodes.WouldDrop));
            return Answer(WorkspaceOutputDropCodes.WouldDrop, WorkspaceOutputDropCodes.WouldDrop, step, previewBytes, proven.Count, entries, bounded);
        }

        if (proven.Count > 0)
        {
            var again = await RescanHoldersAsync(path, ct);
            if (again is not null)
                return Answer(WorkspaceOutputDropCodes.Kept, again, step);
        }

        long deletedBytes = 0;
        var deleted = 0;
        string? failure = null;
        foreach (var (dir, bytes) in proven)
        {
            try
            {
                RunnerWorkspaceNoFollowDelete.MeasureOrDelete(dir.Full, delete: true, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failure = ex.Message;
            }

            if (Directory.Exists(dir.Full) || IsLink(dir.Full))
            {
                entries.Add(new WorkspaceOutputDropEntry(dir.Relative, dir.Name, 0, WorkspaceOutputDropCodes.Kept, WorkspaceOutputDropCodes.DeleteFailed));
                if (failure is not null)
                    break;
                continue;
            }

            deletedBytes += bytes;
            deleted++;
            entries.Add(new WorkspaceOutputDropEntry(dir.Relative, dir.Name, bytes, WorkspaceOutputDropCodes.Dropped));
            if (failure is not null)
                break;
        }

        if (failure is not null)
            return Answer(WorkspaceOutputDropCodes.DeleteFailed, WorkspaceOutputDropCodes.DeleteFailed, step, deletedBytes, deleted, entries, bounded, failure);
        if (deleted == 0 && entries.Exists(entry => entry.Reason == WorkspaceOutputDropCodes.Symlink) && proven.Count == 0)
            return Answer(WorkspaceOutputDropCodes.Kept, WorkspaceOutputDropCodes.Symlink, step, entries: entries, bounded: bounded);
        return Answer(WorkspaceOutputDropCodes.Dropped, null, step, deletedBytes, deleted, entries, bounded);
    }

    private static WorkspaceOutputDropStep Refused(PhoneHomeWorkspaceRetainResponse refused) =>
        new(new PhoneHomeWorkspaceOutputDropResponse(
            WorkspaceOutputDropCodes.Kept, refused.Reason, refused.HeadSha, refused.OriginTip, Detail: refused.Detail));

    private static PhoneHomeWorkspaceOutputDropResponse Answer(
        string verdict, string? reason, WorkspaceOutputDropStep step, long? bytes = null, int? directories = null,
        List<WorkspaceOutputDropEntry>? entries = null, bool bounded = false, string? detail = null) =>
        new(verdict, reason, step.HeadSha, step.OriginTip, bytes, directories, entries,
            detail ?? (bounded ? "walk_bounded" : null));

    private async Task<string?> RescanHoldersAsync(string path, CancellationToken ct)
    {
        HolderScan scan;
        try
        {
            scan = await Task.Run(() => _holders.Scan(path), ct).WaitAsync(HolderScanBudget, ct);
        }
        catch (TimeoutException)
        {
            return WorkspaceRetentionCodes.HolderScanUnknown;
        }

        if (scan.Holders.Count > 0)
            return WorkspaceRetentionCodes.LiveProcess;
        return scan.Complete ? null : WorkspaceRetentionCodes.HolderScanUnknown;
    }

    private static (List<FoundOutput> Found, bool Bounded) Discover(string root, CancellationToken ct)
    {
        var found = new List<FoundOutput>();
        var pending = new Stack<(string Full, int Depth)>();
        pending.Push((root, 0));
        var seen = 0;
        var bounded = false;
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = pending.Pop();
            if (depth > OutputDiscoveryDepth)
            {
                bounded = true;
                continue;
            }

            foreach (var child in Directory.EnumerateFileSystemEntries(dir))
            {
                ct.ThrowIfCancellationRequested();
                if (++seen > OutputDiscoveryEntries)
                    return (found, true);
                var name = Path.GetFileName(child);
                if (name == ".git" || name.Length == 0)
                    continue;
                if (IsLink(child))
                {
                    if (IsOutputName(name))
                        found.Add(new FoundOutput(child, Relative(root, child), name, true));
                    continue;
                }

                if (!Directory.Exists(child))
                    continue;
                if (IsOutputName(name))
                    found.Add(new FoundOutput(child, Relative(root, child), name, false));
                else
                    pending.Push((child, depth + 1));
            }
        }

        return (found, bounded);
    }

    internal static bool IsOutputName(string name)
    {
        if (name is "bin" or "obj")
            return true;
        if (!name.StartsWith("bin-", StringComparison.Ordinal) || name.Length is < 5 or > 68)
            return false;
        for (var i = 4; i < name.Length; i++)
        {
            var c = name[i];
            var ok = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-';
            if (!ok)
                return false;
        }

        return true;
    }

    private static string Relative(string root, string full) =>
        Path.GetRelativePath(root, full).Replace('\\', '/');

    private sealed record FoundOutput(string Full, string Relative, string Name, bool Symlink);
}
