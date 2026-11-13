using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>CARD-1895. The dispatcher holds its mutation lock across check and delete.</summary>
public sealed record WorkspaceOutputDropStep(
    PhoneHomeWorkspaceOutputDropResponse? Response,
    PhoneHomeWorkspaceOutputDropRequest? Request = null,
    string? HeadSha = null,
    string? OriginTip = null,
    IAsyncDisposable? Lease = null,
    Func<CancellationToken, Task<string?>>? AdmissionRecheck = null)
{
    public bool Eligible => Response is null && Request is not null;
}

public sealed partial class RunnerWorkspaceRetention
{
    private const int OutputDiscoveryDepth = 24;
    private const int OutputDiscoveryEntries = 100_000;

    /// <summary>Null skips the ledger. Production sets a directory beside the runner store id.</summary>
    internal string? OutputDropLedgerRoot { get; set; }

    /// <summary>Invoked after a durable ledger write. A throw stands in for a crash once the saving is on disk.</summary>
    internal Action<PhoneHomeWorkspaceOutputDropResponse>? AfterLedgerPersisted { get; set; }

    /// <summary>Invoked immediately before each directory is rechecked and deleted.</summary>
    internal Action<string>? BeforeEachOutputDelete { get; set; }

    /// <summary>CARD-1895. The retention checks, then the allow-listed delete. Tests call this as one step.</summary>
    public async Task<PhoneHomeWorkspaceOutputDropResponse> DropOutputsAsync(
        PhoneHomeWorkspaceOutputDropRequest request, CancellationToken ct)
    {
        var step = await CheckOutputsAsync(request, ct);
        try
        {
            return step.Response ?? await ApplyOutputsAsync(step, ct);
        }
        finally
        {
            if (step.Lease is { } lease)
                await lease.DisposeAsync();
        }
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

        IAsyncDisposable? lease = null;
        if (_service.WorkspaceUseGate is { } gate)
        {
            try
            {
                lease = await gate.AcquireRemovalAsync(path, path, ct);
            }
            catch (PhoneHomeAdmissionException ex)
            {
                return Refused(new PhoneHomeWorkspaceRetainResponse(
                    WorkspaceOutputDropCodes.Kept,
                    ex.Code == WorkspaceRetentionCodes.WorkspaceInUse
                        ? WorkspaceRetentionCodes.WorkspaceInUse
                        : WorkspaceRetentionCodes.WorkspaceRetiringOrUnknown,
                    Detail: ex.Message));
            }
        }

        return new WorkspaceOutputDropStep(null, request with { Path = path }, step.Facts?.HeadSha, step.Facts?.OriginTip, lease);
    }

    /// <summary>
    /// Discover proven output directories and delete them by the identity captured at validation.
    /// The tree and its git registration stay. A failed recheck deletes nothing further.
    /// </summary>
    public async Task<PhoneHomeWorkspaceOutputDropResponse> ApplyOutputsAsync(
        WorkspaceOutputDropStep step, CancellationToken ct)
    {
        var request = step.Request ?? throw new InvalidOperationException("output drop apply without a request");
        var path = request.Path ?? "";
        if (OutputDropLedgerRoot is { } ledgerRoot)
        {
            var saved = OutputDropLedger.Read(ledgerRoot, request.RequestId);
            if (saved is not null)
                return saved;
        }

        var execute = request.Mode == WorkspaceOutputDropModes.Execute;
        var gitlinks = await GitlinksAsync(path, ct);
        if (gitlinks is null)
            return Answer(WorkspaceOutputDropCodes.Kept, WorkspaceRetentionCodes.InspectionFailed, step, detail: "ls-files unavailable");

        List<FoundOutput> found;
        var bounded = false;
        try
        {
            (found, bounded) = Discover(path, gitlinks, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Answer(WorkspaceOutputDropCodes.Kept, WorkspaceRetentionCodes.InspectionFailed, step, detail: ex.Message);
        }

        var entries = new List<WorkspaceOutputDropEntry>();
        var proven = new List<ProvenDir>();
        try
        {
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

                if (!RunnerWorkspaceNoFollowDelete.TryHoldOutput(path, dir.Full, ct, out var held, out var refusal) || held is null)
                {
                    entries.Add(new WorkspaceOutputDropEntry(dir.Relative, dir.Name, 0, WorkspaceOutputDropCodes.Kept, refusal ?? "unproven_content"));
                    continue;
                }

                proven.Add(new ProvenDir(dir, held, held.MeasuredBytes, held.MeasuredFiles));
            }

            proven.Sort((a, b) => string.CompareOrdinal(a.Dir.Relative, b.Dir.Relative));
            if (!execute)
            {
                var previewBytes = proven.Sum(item => item.Bytes);
                var previewFiles = proven.Sum(item => item.Files);
                foreach (var item in proven)
                    entries.Add(new WorkspaceOutputDropEntry(item.Dir.Relative, item.Dir.Name, item.Bytes, WorkspaceOutputDropCodes.WouldDrop));
                var preview = Answer(WorkspaceOutputDropCodes.WouldDrop, WorkspaceOutputDropCodes.WouldDrop, step,
                    previewBytes, proven.Count, entries, bounded, filesRemoved: 0, filesLeft: previewFiles);
                Persist(preview, request.RequestId);
                return preview;
            }

            long deletedBytes = 0;
            var deleted = 0;
            var filesRemoved = 0;
            var filesLeft = 0;
            string? failure = null;
            string? failureReason = null;
            for (var index = 0; index < proven.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var item = proven[index];
                BeforeEachOutputDelete?.Invoke(item.Dir.Relative);
                var holder = await RescanHoldersAsync(path, ct);
                if (holder is not null)
                {
                    failure = holder;
                    failureReason = WorkspaceOutputDropCodes.ChangedDuringDrop;
                    filesLeft += RemainingFiles(proven, index);
                    entries.Add(new WorkspaceOutputDropEntry(item.Dir.Relative, item.Dir.Name, 0, WorkspaceOutputDropCodes.Kept, failureReason));
                    break;
                }

                if (step.AdmissionRecheck is { } recheck)
                {
                    var admission = await recheck(ct);
                    if (admission is not null)
                    {
                        failure = admission;
                        failureReason = WorkspaceOutputDropCodes.ChangedDuringDrop;
                        filesLeft += RemainingFiles(proven, index);
                        entries.Add(new WorkspaceOutputDropEntry(item.Dir.Relative, item.Dir.Name, 0, WorkspaceOutputDropCodes.Kept, failureReason));
                        break;
                    }
                }

                var git = await RecheckGitAsync(path, item.Dir.Relative, ct);
                if (git is not null)
                {
                    failure = git;
                    failureReason = git is "inspection_failed"
                        ? WorkspaceRetentionCodes.InspectionFailed
                        : WorkspaceOutputDropCodes.ChangedDuringDrop;
                    filesLeft += RemainingFiles(proven, index);
                    entries.Add(new WorkspaceOutputDropEntry(item.Dir.Relative, item.Dir.Name, 0, WorkspaceOutputDropCodes.Kept, failureReason));
                    break;
                }

                if (item.Held is null)
                {
                    failure = "windows_output_hold_unexecuted";
                    failureReason = failure;
                    filesLeft += RemainingFiles(proven, index);
                    entries.Add(new WorkspaceOutputDropEntry(item.Dir.Relative, item.Dir.Name, 0, WorkspaceOutputDropCodes.Kept, failureReason));
                    break;
                }

                Action<RunnerWorkspaceNoFollowDelete.OutputDisposition>? progress = null;
                if (OutputDropLedgerRoot is not null || AfterLedgerPersisted is not null)
                {
                    var removedBefore = filesRemoved;
                    var leftBefore = filesLeft;
                    var directoriesBefore = deleted;
                    progress = snapshot =>
                    {
                        var partial = Answer(WorkspaceOutputDropCodes.DeleteFailed, snapshot.Error ?? "in_progress", step,
                            deletedBytes + snapshot.BytesRemoved, directoriesBefore, entries, bounded, snapshot.Error,
                            removedBefore + snapshot.FilesRemoved, leftBefore + snapshot.FilesLeft);
                        Persist(partial, request.RequestId);
                    };
                }

                var disposition = RunnerWorkspaceNoFollowDelete.DeleteHeld(item.Held, ct, progress);
                filesRemoved += disposition.FilesRemoved;
                deletedBytes += disposition.BytesRemoved;
                if (disposition.Error is not null)
                {
                    failure = disposition.Error;
                    failureReason = disposition.Error == WorkspaceOutputDropCodes.ChangedDuringDrop
                        ? WorkspaceOutputDropCodes.ChangedDuringDrop
                        : WorkspaceOutputDropCodes.DeleteFailed;
                    filesLeft += disposition.FilesLeft;
                    filesLeft += RemainingFiles(proven, index + 1);
                    entries.Add(new WorkspaceOutputDropEntry(item.Dir.Relative, item.Dir.Name, disposition.BytesRemoved,
                        WorkspaceOutputDropCodes.Kept, failureReason));
                    break;
                }

                deleted++;
                entries.Add(new WorkspaceOutputDropEntry(item.Dir.Relative, item.Dir.Name, disposition.BytesRemoved, WorkspaceOutputDropCodes.Dropped));
            }

            if (failure is not null)
            {
                var verdict = deletedBytes > 0 ? WorkspaceOutputDropCodes.DeleteFailed : WorkspaceOutputDropCodes.Kept;
                var response = Answer(verdict, failureReason, step, deletedBytes, deleted, entries, bounded, failure, filesRemoved, filesLeft);
                Persist(response, request.RequestId);
                return response;
            }

            if (deleted == 0 && entries.Exists(entry => entry.Reason == WorkspaceOutputDropCodes.Symlink) && proven.Count == 0)
            {
                var kept = Answer(WorkspaceOutputDropCodes.Kept, WorkspaceOutputDropCodes.Symlink, step, entries: entries, bounded: bounded);
                Persist(kept, request.RequestId);
                return kept;
            }

            var dropped = Answer(WorkspaceOutputDropCodes.Dropped, null, step, deletedBytes, deleted, entries, bounded,
                filesRemoved: filesRemoved, filesLeft: filesLeft);
            Persist(dropped, request.RequestId);
            return dropped;
        }
        finally
        {
            foreach (var item in proven)
                item.Held?.Dispose();
        }
    }

    private void Persist(PhoneHomeWorkspaceOutputDropResponse response, Guid requestId)
    {
        if (OutputDropLedgerRoot is { } root)
            OutputDropLedger.Write(root, requestId, response);
        AfterLedgerPersisted?.Invoke(response);
    }

    private async Task<string?> RecheckGitAsync(string root, string relative, CancellationToken ct)
    {
        var status = await Inspect(root, ct, "status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none");
        if (status is not { ExitCode: 0 })
            return "inspection_failed";
        if (status.Value.Stdout.Trim().Length > 0)
            return "dirty";
        var listed = await Inspect(root, ct, "ls-files", "-z", "--", relative);
        if (listed is not { ExitCode: 0 })
            return "inspection_failed";
        if (listed.Value.Stdout.Length > 0)
            return "tracked";
        var ignored = await Inspect(root, ct, "check-ignore", "-q", "--", relative);
        if (ignored is not { } result)
            return "inspection_failed";
        if (result.ExitCode != 0)
            return "not_ignored";
        return null;
    }

    private async Task<HashSet<string>?> GitlinksAsync(string root, CancellationToken ct)
    {
        var listed = await Inspect(root, ct, "ls-files", "-s", "-z");
        if (listed is not { ExitCode: 0 })
            return null;
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in listed.Value.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.StartsWith("160000 ", StringComparison.Ordinal))
                continue;
            var tab = part.IndexOf('\t');
            if (tab > 0)
                set.Add(part[(tab + 1)..].Replace('\\', '/'));
        }

        return set;
    }

    private static int RemainingFiles(List<ProvenDir> proven, int from)
    {
        var count = 0;
        for (var index = from; index < proven.Count; index++)
            count += proven[index].Files;
        return count;
    }

    private static WorkspaceOutputDropStep Refused(PhoneHomeWorkspaceRetainResponse refused) =>
        new(new PhoneHomeWorkspaceOutputDropResponse(
            WorkspaceOutputDropCodes.Kept, refused.Reason, refused.HeadSha, refused.OriginTip, Detail: refused.Detail));

    private static PhoneHomeWorkspaceOutputDropResponse Answer(
        string verdict, string? reason, WorkspaceOutputDropStep step, long? bytes = null, int? directories = null,
        List<WorkspaceOutputDropEntry>? entries = null, bool bounded = false, string? detail = null,
        int? filesRemoved = null, int? filesLeft = null) =>
        new(verdict, reason, step.HeadSha, step.OriginTip, bytes, directories, entries,
            detail ?? (bounded ? "walk_bounded" : null), filesRemoved, filesLeft);

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

    private static (List<FoundOutput> Found, bool Bounded) Discover(string root, HashSet<string> gitlinks, CancellationToken ct)
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
            // A directory at depth 24 is 24 segments down. Enumerating it would select a bin 25 segments down.
            if (depth >= OutputDiscoveryDepth)
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
                if (name.Length == 0 || name is ".git" or "node_modules" or "dist")
                    continue;
                var relative = Relative(root, child);
                if (IsUnderGitlink(relative, gitlinks))
                    continue;
                if (IsLink(child))
                {
                    if (IsOutputName(name))
                        found.Add(new FoundOutput(child, relative, name, true));
                    continue;
                }

                if (!Directory.Exists(child))
                    continue;
                if (IsNestedRepository(child))
                    continue;
                if (IsOutputName(name))
                {
                    if (HasProjectSibling(child))
                        found.Add(new FoundOutput(child, relative, name, false));
                }
                else
                    pending.Push((child, depth + 1));
            }
        }

        return (found, bounded);
    }

    private static bool HasProjectSibling(string directory)
    {
        var parent = Path.GetDirectoryName(directory);
        if (string.IsNullOrEmpty(parent))
            return false;
        try
        {
            foreach (var file in Directory.EnumerateFiles(parent))
            {
                if (IsLink(file))
                    continue;
                if (IsProjectFile(Path.GetFileName(file)))
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static bool IsProjectFile(string name)
    {
        if (name.StartsWith("Directory.Build.", StringComparison.Ordinal))
            return true;
        return Path.GetExtension(name) is ".csproj" or ".fsproj" or ".vbproj" or ".sln";
    }

    private static bool IsNestedRepository(string directory) =>
        Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git"));

    private static bool IsUnderGitlink(string relative, HashSet<string> gitlinks)
    {
        var cursor = relative.Replace('\\', '/');
        while (cursor.Length > 0)
        {
            if (gitlinks.Contains(cursor))
                return true;
            var slash = cursor.LastIndexOf('/');
            if (slash < 0)
                break;
            cursor = cursor[..slash];
        }

        return false;
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

    private sealed record ProvenDir(FoundOutput Dir, RunnerWorkspaceNoFollowDelete.HeldOutput? Held, long Bytes, int Files);
}

/// <summary>CARD-1895. One JSON file per request id, replaced atomically after fsync.</summary>
internal static class OutputDropLedger
{
    internal static void Write(string root, Guid requestId, PhoneHomeWorkspaceOutputDropResponse response)
    {
        Directory.CreateDirectory(root);
        var finalPath = Path.Combine(root, requestId.ToString("N") + ".json");
        var temp = finalPath + ".tmp";
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, PhoneHomeFraming.Json));
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }

        File.Move(temp, finalPath, overwrite: true);
    }

    internal static PhoneHomeWorkspaceOutputDropResponse? Read(string root, Guid requestId)
    {
        var path = Path.Combine(root, requestId.ToString("N") + ".json");
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<PhoneHomeWorkspaceOutputDropResponse>(File.ReadAllText(path), PhoneHomeFraming.Json);
    }
}
