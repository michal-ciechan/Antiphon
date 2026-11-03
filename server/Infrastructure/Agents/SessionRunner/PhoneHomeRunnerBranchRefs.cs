using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-0824 S2 (B-3 step 3). The production runner half of a branch deletion over phone-home: only a connected runner
/// that advertises <c>workspaceBranchRefsV1</c> is asked; a missing connection or a transport failure throws, and
/// the caller defers with origin untouched.
/// </summary>
public sealed class PhoneHomeRunnerBranchRefs(ISessionRunnerDirectory runners) : IRunnerBranchRefs
{
    public async Task<bool> SupportsAsync(string runnerId, CancellationToken ct)
    {
        if (runners.Resolve(runnerId) is not PhoneHomeRunnerClient)
            return false;
        var descriptor = await runners.DescribeAsync(runnerId, ct);
        return descriptor?.Capabilities?.Features?.Contains(RunnerCapabilityFeatures.WorkspaceBranchRefsV1, StringComparer.Ordinal) == true;
    }

    public async Task<RunnerBranchRefsResult> DeleteAsync(string runnerId, string branch, string expectedSha, CancellationToken ct)
    {
        var client = runners.Resolve(runnerId) as PhoneHomeRunnerClient
            ?? throw new IOException($"runner '{runnerId}' is not connected over phone-home");
        var response = await client.DeleteWorkspaceBranchRefsAsync(new(branch, expectedSha), ct);
        return new(response.Succeeded ? RunnerBranchRefsOutcome.Deleted : RunnerBranchRefsOutcome.Refused,
            response.Code, response.Deleted, response.Absent);
    }
}
