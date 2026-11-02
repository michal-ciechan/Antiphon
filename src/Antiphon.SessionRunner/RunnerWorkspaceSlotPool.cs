using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>A lease request after the runner's admission (name, sha, branch, repository) passed.</summary>
public sealed record WorkspaceSlotLeaseRequest(Guid TaskId, Guid PreparationId, string Branch, string Sha, bool Cold = false);

public sealed record WorkspaceSlotLease(string SlotId, int Generation, string Path);

/// <summary>
/// CARD-0824 S1. RED-COMMIT STUB: the public surface only. The lease returns the first directory
/// unchanged and the release does nothing, so every witness is red until the green commits.
/// </summary>
public sealed class RunnerWorkspaceSlotPool
{
    private readonly RunnerWorkspaceService _service;
    private readonly RunnerWorkspaceSlotLedger _ledger;

    public RunnerWorkspaceSlotPool(
        RunnerWorkspaceService service, PhoneHomeWorkspaceSlotSettings settings, int capacity,
        TimeProvider? clock = null, IWorkspaceSlotFileSystem? fileSystem = null,
        IWorkspaceSlotProcessProbe? processes = null, IWorkspaceSlotDiskProbe? disk = null)
    {
        _service = service;
        Settings = settings;
        Capacity = capacity;
        _ledger = new RunnerWorkspaceSlotLedger(service.AllowedRoot + "/workspace-slots");
    }

    public PhoneHomeWorkspaceSlotSettings Settings { get; }
    public int Capacity { get; }
    public int Target => Capacity + Settings.Headroom;
    public string LedgerRoot => _ledger.StateRoot;
    public string RetainedRoot => _service.AllowedRoot + "/retained";

    /// <summary>Test seam: named phase cuts (picked, leasing, leased, releasing, before-clean, free, ...).</summary>
    internal Action<string>? PhaseCut { get; set; }

    /// <summary>Test seam: completed when a lease starts waiting for a Releasing slot.</summary>
    internal Action? WaitingForRelease { get; set; }

    public WorkspaceSlotLedgerState Ledger() => _ledger.Read();

    public async Task<WorkspaceSlotLease> LeaseAsync(WorkspaceSlotLeaseRequest request, CancellationToken ct)
    {
        var path = _service.WorktreeRoot + "/ws-01";
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(_service.WorktreeRoot);
            await _service.GitAsync(_service.PrimaryRepository, ct, "worktree", "add", "--detach", path, request.Sha);
        }

        return new WorkspaceSlotLease("ws-01", 1, path);
    }

    public Task<PhoneHomeWorkspaceSlotReleaseResponse> ReleaseAsync(
        PhoneHomeWorkspaceSlotReleaseRequest request, CancellationToken ct) =>
        Task.FromResult(new PhoneHomeWorkspaceSlotReleaseResponse("released", request.SlotId, request.Generation));

    public Task<PhoneHomeWorkspaceSlotColdResetResponse> ColdResetAsync(
        PhoneHomeWorkspaceSlotColdResetRequest request, CancellationToken ct) =>
        Task.FromResult(new PhoneHomeWorkspaceSlotColdResetResponse(request.RequestId, []));

    public Task<PhoneHomeWorkspaceSlotQuarantineReleaseResponse> ReleaseQuarantineAsync(
        PhoneHomeWorkspaceSlotQuarantineReleaseRequest request, CancellationToken ct) =>
        Task.FromResult(new PhoneHomeWorkspaceSlotQuarantineReleaseResponse(request.DirId, "removed"));

    public Task MaintainAsync(CancellationToken ct) => Task.CompletedTask;

    public Task RecoverAsync(IReadOnlyCollection<(string SlotId, int Generation)>? confirmed, CancellationToken ct) =>
        Task.CompletedTask;

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>True when a path names a slot directory (<c>ws-NN</c> or <c>ws-NN-dK</c>).</summary>
    public static bool IsSlotLeaf(string? leaf) =>
        leaf is not null && System.Text.RegularExpressions.Regex.IsMatch(leaf, @"^ws-[0-9]{2,}(-d[0-9]+)?$");

    internal WorkspaceSlotRecord? SlotByPath(string path) => null;
}
