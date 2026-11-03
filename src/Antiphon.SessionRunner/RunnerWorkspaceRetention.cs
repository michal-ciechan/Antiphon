using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>CARD-1677 S1. One retention request split at the dispatcher's mutation lock (see the green commit).</summary>
public sealed record WorkspaceRetainStep(
    PhoneHomeWorkspaceRetainResponse? Final,
    string? MovedPath = null,
    string? Repository = null,
    PhoneHomeWorkspaceRetainResponse? Facts = null)
{
    public bool Moved => Final is null && MovedPath is not null;
}

/// <summary>
/// CARD-1677 S1 RED SHIM. No check at all: every request moves the tree aside and removes it WITH
/// --force (the RemoveAsync shape), and an absent path prunes. Every refusal witness is red for the right
/// reason (the tree is gone); the green commit replaces this with the checked engine.
/// </summary>
public sealed class RunnerWorkspaceRetention
{
    private readonly RunnerWorkspaceService _service;

    public RunnerWorkspaceRetention(RunnerWorkspaceService service, IWorkspaceHolderScan holders,
        Func<string, ulong?>? deviceOf = null, string? retiringRoot = null, TimeProvider? clock = null)
    {
        _service = service;
        _ = holders;
        _ = deviceOf;
        _ = clock;
        RetiringRoot = (retiringRoot ?? service.AllowedRoot + "/worktrees-retiring").Replace('\\', '/').TrimEnd('/');
    }

    public string WorktreeRoot => _service.WorktreeRoot;
    public string RetiringRoot { get; }
    internal Action<string>? PhaseCut { get; set; }
    internal Func<string, Task>? PhaseBGate { get; set; }
    internal TimeSpan LsRemoteBudget { get; set; } = TimeSpan.FromSeconds(60);
    internal TimeSpan NestedCountBudget { get; set; } = TimeSpan.FromSeconds(10);
    internal TimeSpan HolderScanBudget { get; set; } = TimeSpan.FromSeconds(10);

    public async Task<PhoneHomeWorkspaceRetainResponse> RetainAsync(PhoneHomeWorkspaceRetainRequest request, CancellationToken ct)
    {
        var step = await CheckAndMoveAsync(request, ct);
        return step.Moved ? await CompleteAsync(step, ct) : step.Final!;
    }

    public async Task<WorkspaceRetainStep> CheckAndMoveAsync(PhoneHomeWorkspaceRetainRequest request, CancellationToken ct)
    {
        var path = (request.Path ?? "").Replace('\\', '/').TrimEnd('/');
        if (!Directory.Exists(path))
        {
            await _service.GitAsync(_service.RunnerRepositories()[0], ct, "worktree", "prune");
            return new WorkspaceRetainStep(new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.AlreadyAbsent));
        }
        var common = await _service.GitAsync(path, ct, "rev-parse", "--path-format=absolute", "--git-common-dir");
        var repository = Path.GetDirectoryName(common.Stdout.Trim())!.Replace('\\', '/');
        Directory.CreateDirectory(RetiringRoot);
        var moved = RetiringRoot + "/" + Path.GetFileName(path) + "." + request.RequestId.ToString("N")[..8];
        await _service.GitAsync(repository, ct, "worktree", "move", path, moved);
        PhaseCut?.Invoke("moved");
        return new WorkspaceRetainStep(null, moved, repository, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept));
    }

    public async Task<PhoneHomeWorkspaceRetainResponse> CompleteAsync(WorkspaceRetainStep step, CancellationToken ct)
    {
        if (!step.Moved)
            return step.Final!;
        if (PhaseBGate is { } gate)
            await gate(step.MovedPath!);
        await _service.GitAsync(step.Repository!, ct, "worktree", "remove", "--force", step.MovedPath!);
        return new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Removed, MovedPath: step.MovedPath);
    }

    public Task<PhoneHomeWorkspaceRetentionInventoryResponse> InventoryAsync(CancellationToken ct) =>
        Task.FromResult(new PhoneHomeWorkspaceRetentionInventoryResponse([], false));
}
