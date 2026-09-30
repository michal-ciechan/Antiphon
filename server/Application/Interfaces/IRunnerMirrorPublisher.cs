using Antiphon.Server.Domain.Entities;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>Optional runner capability. Null means the runner cannot publish a mirror.</summary>
public interface IRunnerMirrorPublisher
{
    Task<PhoneHomeWorkspacePublishResponse?> PublishAsync(
        AgentTask task, string baselineSha, string? remoteSha, CancellationToken ct);
}
