namespace Antiphon.Server.Application.Interfaces;

/// <summary>CARD-0824 S2. What one runner said about deleting its local refs for a delegate branch.</summary>
public enum RunnerBranchRefsOutcome { Deleted = 0, Refused = 1 }

public sealed record RunnerBranchRefsResult(RunnerBranchRefsOutcome Outcome, string? Code,
    IReadOnlyList<string>? Deleted = null, IReadOnlyList<string>? Absent = null);

/// <summary>
/// CARD-0824 S2 (B-3 step 3). The runner half of a branch deletion: before origin is touched, every runner that held
/// the branch deletes its <c>refs/heads/&lt;b&gt;</c> and <c>refs/remotes/origin/&lt;b&gt;</c> at or behind the leased SHA.
/// Production wraps <c>PhoneHomeRunnerClient</c>; a transport failure throws.
/// </summary>
public interface IRunnerBranchRefs
{
    /// <summary>The runner is connected and advertises <c>workspaceBranchRefsV1</c>.</summary>
    Task<bool> SupportsAsync(string runnerId, CancellationToken ct);

    Task<RunnerBranchRefsResult> DeleteAsync(string runnerId, string branch, string expectedSha, CancellationToken ct);
}

/// <summary>CARD-0824 S2. The fail-closed default: no runner is ever reported as supporting the operation.</summary>
public sealed class RefusingRunnerBranchRefs : IRunnerBranchRefs
{
    public Task<bool> SupportsAsync(string runnerId, CancellationToken ct) => Task.FromResult(false);

    public Task<RunnerBranchRefsResult> DeleteAsync(string runnerId, string branch, string expectedSha, CancellationToken ct) =>
        Task.FromResult(new RunnerBranchRefsResult(RunnerBranchRefsOutcome.Refused, "not_implemented"));
}
