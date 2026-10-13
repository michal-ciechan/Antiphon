using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

public sealed class PhoneHomeRunnerMirrorPublisher(ISessionRunnerDirectory runners) : IRunnerMirrorPublisher
{
    public async Task<PhoneHomeWorkspacePublishResponse?> PublishAsync(
        AgentTask task, string baselineSha, string? remoteSha, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.RunnerId) || string.IsNullOrWhiteSpace(task.RemoteWorktreePath))
            return null;
        var branch = RemoteWorkspaceService.OwnedBranch(task);
        if (branch is null) return null;
        var descriptor = await runners.DescribeAsync(task.RunnerId, ct);
        if (descriptor?.Capabilities?.Features?.Contains(RunnerCapabilityFeatures.WorkspacePublishV1) != true)
            return null;
        if (DelegateBranchName.TryParse(branch, out var parsed) && parsed.CardNumber is not null
            && descriptor.Capabilities.Features.Contains(RunnerCapabilityFeatures.WorkspaceCardBranchV1, StringComparer.Ordinal) != true)
            return null;
        if (runners.Resolve(task.RunnerId) is not PhoneHomeRunnerClient runner)
            return null;
        if (DelegateBranchName.TryParse(branch, out var owned) && owned.CardNumber is not null
            && (await runner.GetCapabilitiesAsync(ct))?.Features?.Contains(RunnerCapabilityFeatures.WorkspaceCardBranchV1, StringComparer.Ordinal) != true)
            return null;
        return await runner.PublishWorkspaceAsync(new PhoneHomeWorkspacePublishRequest(
            task.RemoteWorktreePath, branch, baselineSha, remoteSha, true), ct);
    }
}
