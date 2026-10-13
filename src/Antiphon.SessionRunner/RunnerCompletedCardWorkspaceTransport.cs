using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

// Fixture-only composition. C1032_TransportDefaultInert checks the absent and installed arms;
// C1032_TransportDispatchReplay checks the external receipt before dispatch returns.
public sealed class RunnerCompletedCardWorkspaceTransport
{
    private readonly RunnerWorkspaceRemovalJournal _journal;
    internal RunnerWorkspaceUseGate Gate { get; }
    internal RunnerCompletedCardWorkspaceRemoval Removal { get; }

    public RunnerCompletedCardWorkspaceTransport(RunnerWorkspaceService workspace,
        RunnerWorkspaceRemovalJournal journal, RunnerWorkspaceUseGate gate) : this(workspace, journal, gate, null) { }

    public RunnerCompletedCardWorkspaceTransport(RunnerWorkspaceService workspace,
        RunnerWorkspaceRemovalJournal journal, RunnerWorkspaceUseGate gate, string? landingMapKey)
    {
        if (!ReferenceEquals(workspace.WorkspaceUseGate, gate) || !ReferenceEquals(gate.RemovalJournal, journal))
            throw new ArgumentException("Completed-card transport requires the shared workspace gate and journal.");
        _journal = journal;
        Gate = gate;
        Removal = new(workspace, landingMapKey);
    }

    public async Task<CompletedCardWorkspaceInspectionResponse> InspectAsync(
        CompletedCardWorkspaceInspectRequest request, CancellationToken ct)
    {
        await using var lease = await Gate.AcquireUseAsync(request.Binding.WorktreePath, ct);
        var read = await Removal.InspectAsync(request, ct);
        return new(read.HeadSha, read.SourceFullRef,
            read.IgnoredContent.Select(e => new CompletedCardWorkspaceInspectionEntry(e.Path, e.Kind.ToString())).ToArray(), read.Reason)
            { HeldPaths = read.HeldPaths };
    }

    public async Task<CompletedCardWorkspaceRemovalResponse> RemoveAsync(CompletedCardWorkspaceCommand command, CancellationToken ct)
    {
        var result = await Removal.RemoveAsync(command, _journal, Gate, ct);
        return new(result.Complete, result.Reason, result.Receipt);
    }
}
