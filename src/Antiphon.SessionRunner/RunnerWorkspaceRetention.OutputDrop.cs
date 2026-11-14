using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.SessionRunner;

/// <summary>CARD-1895. The dispatcher holds its mutation lock across each proof batch and releases it between batches.</summary>
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

    /// <summary>
    /// Invoked between proof batches while a delete is in progress. The dispatcher releases
    /// its mutation lock across this call and acquires it again before the next batch.
    /// </summary>
    internal Func<CancellationToken, Task>? BetweenOutputBatches { get; set; }

    /// <summary>Git and open-file proof are refreshed at least once per this many regular files. Default 8.</summary>
    internal int OutputProofBatchSize { get; set; } = 8;

    /// <summary>Test seam: a relative path under the output directory is treated as a different device.</summary>
    internal Func<string, bool>? ForeignDevice { get; set; }

    /// <summary>Invoked with the ledger phase and temp path immediately before a ledger file is created.</summary>
    internal Action<string, string>? BeforeLedgerWrite { get; set; }

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
        OutputDropLedgerRecord? prior = null;
        if (OutputDropLedgerRoot is { } ledgerRoot)
        {
            prior = OutputDropLedger.ReadRecord(ledgerRoot, request.RequestId);
            if (prior is { Replay: true, Response: not null })
                return prior.Response;
        }

        var carriedBytes = prior?.BytesFreed ?? 0;
        var partial = prior is { Consistent: false } || prior?.Accounting == WorkspaceOutputDropCodes.PartialUnknown;

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

                var measureGate = new RunnerWorkspaceNoFollowDelete.OutputUnlinkGate { ForeignDevice = ForeignDevice };
                if (!RunnerWorkspaceNoFollowDelete.TryHoldOutput(path, dir.Full, ct, out var held, out var refusal, measureGate) || held is null)
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
            var preserved = 0;
            var preservedPaths = new List<string>();
            string? failure = null;
            string? failureReason = null;
            string? rootIdentity = null;
            if (OutputDropLedgerRoot is not null)
            {
                try
                {
                    rootIdentity = RunnerWorkspaceNoFollowDelete.Identity(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return Answer(WorkspaceOutputDropCodes.Kept, WorkspaceRetentionCodes.InspectionFailed, step, detail: ex.Message);
                }

                if (prior?.RootIdentity is { Length: > 0 } expected
                    && !string.Equals(expected, rootIdentity, StringComparison.Ordinal))
                {
                    var mismatch = Answer(WorkspaceOutputDropCodes.Kept, WorkspaceOutputDropCodes.ChangedDuringDrop, step,
                        carriedBytes, detail: "root identity changed", bytesAccounting: WorkspaceOutputDropCodes.PartialUnknown);
                    Persist(mismatch, request.RequestId);
                    return mismatch;
                }

                // Durable intent before the first unlink. A failure here has deleted nothing.
                WriteLedger(new OutputDropLedgerRecord
                {
                    Phase = "intent",
                    Replay = false,
                    Consistent = !partial,
                    BytesFreed = carriedBytes,
                    RootIdentity = rootIdentity,
                    Accounting = partial ? WorkspaceOutputDropCodes.PartialUnknown : null,
                }, request.RequestId);
            }

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

                var proof = new OutputEntryProof(this, path, item.Dir.Relative, item.Dir.Full, ct);
                var gate = new RunnerWorkspaceNoFollowDelete.OutputUnlinkGate
                {
                    BatchSize = OutputProofBatchSize < 1 ? 1 : OutputProofBatchSize,
                    ForeignDevice = ForeignDevice,
                    BeginBatch = proof.BeginBatch,
                    SkipFile = proof.Skip,
                    BetweenBatches = BetweenOutputBatches is null
                        ? null
                        : () => BetweenOutputBatches(CancellationToken.None).GetAwaiter().GetResult(),
                };
                Action<RunnerWorkspaceNoFollowDelete.OutputDisposition>? progress = null;
                if (OutputDropLedgerRoot is not null || AfterLedgerPersisted is not null)
                {
                    var removedBefore = filesRemoved;
                    var leftBefore = filesLeft;
                    var directoriesBefore = deleted;
                    progress = snapshot =>
                    {
                        var checkpoint = Answer(WorkspaceOutputDropCodes.DeleteFailed, snapshot.Error ?? "in_progress", step,
                            carriedBytes + deletedBytes + snapshot.BytesRemoved, directoriesBefore, entries, bounded, snapshot.Error,
                            removedBefore + snapshot.FilesRemoved, leftBefore + snapshot.FilesLeft,
                            bytesAccounting: partial ? WorkspaceOutputDropCodes.PartialUnknown : null);
                        Persist(checkpoint, request.RequestId);
                    };
                }

                if (OutputDropLedgerRoot is not null)
                {
                    // The next batch may unlink before the next checkpoint. Retry treats this record as incomplete.
                    WriteLedger(new OutputDropLedgerRecord
                    {
                        Phase = "window",
                        Replay = false,
                        Consistent = false,
                        BytesFreed = carriedBytes + deletedBytes,
                        RootIdentity = rootIdentity,
                        Accounting = WorkspaceOutputDropCodes.PartialUnknown,
                    }, request.RequestId);
                }

                var disposition = RunnerWorkspaceNoFollowDelete.DeleteHeld(item.Held, ct, progress, gate);
                filesRemoved += disposition.FilesRemoved;
                deletedBytes += disposition.BytesRemoved;
                if (disposition.PreservedBoundaries > 0)
                {
                    preserved += disposition.PreservedBoundaries;
                    if (disposition.PreservedPaths is not null)
                    {
                        foreach (var preservedPath in disposition.PreservedPaths)
                        {
                            if (preservedPaths.Count >= RunnerWorkspaceNoFollowDelete.OutputUnlinkGate.PreservedPathLimit)
                                break;
                            var shown = preservedPath == "." ? item.Dir.Relative : item.Dir.Relative + "/" + preservedPath;
                            preservedPaths.Add(shown);
                        }
                    }
                }

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

                if (disposition.PreservedBoundaries > 0)
                {
                    filesLeft += disposition.FilesLeft;
                    entries.Add(new WorkspaceOutputDropEntry(item.Dir.Relative, item.Dir.Name, disposition.BytesRemoved,
                        WorkspaceOutputDropCodes.Kept, WorkspaceOutputDropCodes.PreservedBoundary));
                    continue;
                }

                deleted++;
                entries.Add(new WorkspaceOutputDropEntry(item.Dir.Relative, item.Dir.Name, disposition.BytesRemoved, WorkspaceOutputDropCodes.Dropped));
            }

            var reportedBytes = carriedBytes + deletedBytes;
            var accounting = partial ? WorkspaceOutputDropCodes.PartialUnknown : null;
            if (failure is not null)
            {
                var verdict = reportedBytes > 0 ? WorkspaceOutputDropCodes.DeleteFailed : WorkspaceOutputDropCodes.Kept;
                var response = Answer(verdict, failureReason, step, reportedBytes, deleted, entries, bounded, failure, filesRemoved, filesLeft,
                    preserved, preservedPaths, accounting);
                Persist(response, request.RequestId);
                return response;
            }

            if (preserved > 0)
            {
                var keptBoundary = Answer(WorkspaceOutputDropCodes.Kept, WorkspaceOutputDropCodes.PreservedBoundary, step,
                    reportedBytes, deleted, entries, bounded, filesRemoved: filesRemoved, filesLeft: filesLeft,
                    preservedBoundaries: preserved, preservedPaths: preservedPaths, bytesAccounting: accounting);
                Persist(keptBoundary, request.RequestId);
                return keptBoundary;
            }

            if (deleted == 0 && entries.Exists(entry => entry.Reason == WorkspaceOutputDropCodes.Symlink) && proven.Count == 0)
            {
                var kept = Answer(WorkspaceOutputDropCodes.Kept, WorkspaceOutputDropCodes.Symlink, step, entries: entries, bounded: bounded,
                    bytesAccounting: accounting);
                Persist(kept, request.RequestId);
                return kept;
            }

            // An inconsistent prior record makes Bytes a lower bound. Never call that an exact drop.
            if (accounting is not null)
            {
                var boundedVerdict = reportedBytes > 0
                    ? WorkspaceOutputDropCodes.DeleteFailed
                    : WorkspaceOutputDropCodes.Kept;
                var boundedResponse = Answer(boundedVerdict, WorkspaceOutputDropCodes.PartialUnknown, step,
                    reportedBytes, deleted, entries, bounded, filesRemoved: filesRemoved, filesLeft: filesLeft,
                    preservedBoundaries: preserved, preservedPaths: preservedPaths, bytesAccounting: accounting);
                Persist(boundedResponse, request.RequestId);
                return boundedResponse;
            }

            var dropped = Answer(WorkspaceOutputDropCodes.Dropped, null, step, reportedBytes, deleted, entries, bounded,
                filesRemoved: filesRemoved, filesLeft: filesLeft, bytesAccounting: accounting);
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
        if (OutputDropLedgerRoot is not null)
        {
            WriteLedger(new OutputDropLedgerRecord
            {
                Phase = "checkpoint",
                Replay = true,
                Consistent = response.BytesAccounting != WorkspaceOutputDropCodes.PartialUnknown,
                BytesFreed = response.Bytes ?? 0,
                Accounting = response.BytesAccounting,
                Response = response,
            }, requestId);
        }

        AfterLedgerPersisted?.Invoke(response);
    }

    private void WriteLedger(OutputDropLedgerRecord record, Guid requestId)
    {
        if (OutputDropLedgerRoot is not { } root)
            return;
        OutputDropLedger.Write(root, requestId, record, BeforeLedgerWrite);
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
        int? filesRemoved = null, int? filesLeft = null, int? preservedBoundaries = null,
        IReadOnlyList<string>? preservedPaths = null, string? bytesAccounting = null) =>
        new(verdict, reason, step.HeadSha, step.OriginTip, bytes, directories, entries,
            detail ?? (bounded ? "walk_bounded" : null), filesRemoved, filesLeft,
            preservedBoundaries > 0 ? preservedBoundaries : null,
            preservedPaths is { Count: > 0 } ? preservedPaths : null,
            bytesAccounting);

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

    /// <summary>
    /// Re-proves one output file immediately before unlink. The git index is statted on every
    /// decision; a changed stamp reloads <c>git ls-files -z</c> for this output directory.
    /// A <c>git add</c> that lands after that stat and before <c>unlinkat</c> can still remove
    /// the file, and nothing restores it. Open descriptors are sampled once per proof batch
    /// (default 8), so a process that opens a file after the sample and before unlink can still
    /// lose it. The dispatch lock does not exclude those writers.
    /// </summary>
    private sealed class OutputEntryProof
    {
        private readonly RunnerWorkspaceRetention _owner;
        private readonly string _root;
        private readonly string _outputRelative;
        private readonly string _outputFull;
        private readonly CancellationToken _ct;
        private readonly HashSet<string> _tracked = new(StringComparer.Ordinal);
        private readonly HashSet<string> _open = new(StringComparer.Ordinal);
        private string? _stamp;
        private bool _ready;
        private bool _unproven;

        internal OutputEntryProof(RunnerWorkspaceRetention owner, string root, string outputRelative, string outputFull, CancellationToken ct)
        {
            _owner = owner;
            _root = root;
            _outputRelative = outputRelative.Replace('\\', '/').Trim('/');
            _outputFull = Path.GetFullPath(outputFull).Replace('\\', '/').TrimEnd('/');
            _ct = ct;
        }

        internal void BeginBatch()
        {
            RefreshTracked();
            LoadOpen();
        }

        internal bool Skip(string relativeUnderOutput)
        {
            RefreshTracked();
            if (_unproven)
                return true;
            var repoPath = _outputRelative.Length == 0 ? relativeUnderOutput : _outputRelative + "/" + relativeUnderOutput;
            if (_tracked.Contains(repoPath))
                return true;
            return _open.Contains(_outputFull + "/" + relativeUnderOutput);
        }

        private void RefreshTracked()
        {
            var stamp = IndexStamp();
            if (_ready && stamp == _stamp)
                return;
            _stamp = stamp;
            _ready = true;
            _tracked.Clear();
            if (stamp == "unreadable")
            {
                _unproven = true;
                return;
            }

            var listed = Task.Run(() => _owner.Inspect(_root, _ct, "ls-files", "-z", "--", _outputRelative), _ct)
                .GetAwaiter().GetResult();
            if (listed is not { ExitCode: 0 })
            {
                _unproven = true;
                return;
            }

            _unproven = false;
            foreach (var part in listed.Value.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                _tracked.Add(part.Replace('\\', '/'));
        }

        private string IndexStamp()
        {
            try
            {
                var gitDir = GitDirectory(_root);
                if (gitDir is null)
                    return "unreadable";
                var index = Path.Combine(gitDir, "index");
                if (!File.Exists(index))
                    return "none";
                var info = new FileInfo(index);
                return RunnerWorkspaceNoFollowDelete.EntryIdentity(index)
                    + ":" + info.Length.ToString(CultureInfo.InvariantCulture)
                    + ":" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "unreadable";
            }
        }

        private static string? GitDirectory(string root)
        {
            var git = Path.Combine(root, ".git");
            if (Directory.Exists(git))
                return git;
            if (!File.Exists(git))
                return null;
            var text = File.ReadAllText(git);
            const string prefix = "gitdir:";
            var line = text.Trim();
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                return null;
            var target = line[prefix.Length..].Trim();
            if (target.Length == 0)
                return null;
            return Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(root, target));
        }

        private void LoadOpen()
        {
            _open.Clear();
            if (!OperatingSystem.IsLinux())
            {
                _unproven = true;
                return;
            }

            List<string> entries;
            try
            {
                entries = Directory.EnumerateDirectories("/proc").ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _unproven = true;
                return;
            }

            var self = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            var sawSelf = false;
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (name == self)
                {
                    sawSelf = true;
                    continue;
                }

                if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    continue;
                try
                {
                    foreach (var fd in Directory.EnumerateFileSystemEntries(Path.Combine(entry, "fd")))
                    {
                        string? target;
                        try
                        {
                            target = new FileInfo(fd).LinkTarget;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            continue;
                        }

                        if (target is not null && target.StartsWith(_outputFull + "/", StringComparison.Ordinal))
                            _open.Add(target);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Another uid's descriptor directory is unreadable. That is not an incomplete table.
                }
            }

            if (!sawSelf)
                _unproven = true;
        }
    }

    private sealed record FoundOutput(string Full, string Relative, string Name, bool Symlink);

    private sealed record ProvenDir(FoundOutput Dir, RunnerWorkspaceNoFollowDelete.HeldOutput? Held, long Bytes, int Files);
}

/// <summary>CARD-1895. Intent, in-progress window, or a replayable checkpoint for one request id.</summary>
internal sealed class OutputDropLedgerRecord
{
    public string Phase { get; set; } = "intent";
    public bool Replay { get; set; }
    public bool Consistent { get; set; } = true;
    public long BytesFreed { get; set; }
    public string? RootIdentity { get; set; }
    public string? Accounting { get; set; }
    public PhoneHomeWorkspaceOutputDropResponse? Response { get; set; }
}

/// <summary>
/// CARD-1895. One JSON file per request id. The file is fsynced and then the parent directory is
/// fsynced, because flushing the file does not persist the rename. A record without <c>phase</c>
/// is a legacy response and is replayed as a checkpoint.
/// </summary>
internal static class OutputDropLedger
{
    internal static void Write(string root, Guid requestId, OutputDropLedgerRecord record, Action<string, string>? before = null)
    {
        Directory.CreateDirectory(root);
        var finalPath = Path.Combine(root, requestId.ToString("N") + ".json");
        var temp = finalPath + ".tmp";
        before?.Invoke(record.Phase, temp);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, PhoneHomeFraming.Json);
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }

        File.Move(temp, finalPath, overwrite: true);
        SyncDirectory(root);
    }

    internal static OutputDropLedgerRecord? ReadRecord(string root, Guid requestId)
    {
        var path = Path.Combine(root, requestId.ToString("N") + ".json");
        if (!File.Exists(path))
            return null;
        var text = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("phase", out _))
            return JsonSerializer.Deserialize<OutputDropLedgerRecord>(text, PhoneHomeFraming.Json);
        var response = JsonSerializer.Deserialize<PhoneHomeWorkspaceOutputDropResponse>(text, PhoneHomeFraming.Json);
        if (response is null)
            return null;
        return new OutputDropLedgerRecord
        {
            Phase = "checkpoint",
            Replay = true,
            Consistent = true,
            BytesFreed = response.Bytes ?? 0,
            Accounting = response.BytesAccounting,
            Response = response,
        };
    }

    private static void SyncDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var handle = CreateFileW(path, 0x80000000 | 0x40000000, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (handle.IsInvalid || !FlushFileBuffers(handle))
                throw new IOException("directory flush failed", new Win32Exception(Marshal.GetLastWin32Error()));
            return;
        }

        var fd = Open(path, 0);
        if (fd < 0)
            throw new IOException("directory open for fsync failed: errno " + Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture));
        try
        {
            if (Fsync(fd) != 0)
                throw new IOException("directory fsync failed: errno " + Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            _ = Close(fd);
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int fd);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle handle);
}
