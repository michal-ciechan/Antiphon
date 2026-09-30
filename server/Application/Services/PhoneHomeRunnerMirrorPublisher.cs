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
        var descriptor = await runners.DescribeAsync(task.RunnerId, ct);
        if (descriptor?.Capabilities?.Features?.Contains(RunnerCapabilityFeatures.WorkspacePublishV1) != true)
            return null;
        if (runners.Resolve(task.RunnerId) is not PhoneHomeRunnerClient runner)
            return null;
        return await runner.PublishWorkspaceAsync(new PhoneHomeWorkspacePublishRequest(
            task.RemoteWorktreePath, RemoteWorkspaceService.OwnedBranch(task.Id), baselineSha, remoteSha, true), ct);
    }
}
