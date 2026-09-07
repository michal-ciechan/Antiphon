using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Usefulness is independent of mode, model work cap, and transport safety.</summary>
public static class AgentReportPolicy
{
    public static bool IsTarget(AgentTask task) =>
        task.ReplyTo == AgentTaskReplyTo.Session && !AgentTaskRoles.IsSpecialist(task.Role)
        && task.Status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed;

    public static bool ShouldStore(AgentTask task, DelegationSettings settings) =>
        IsTarget(task) && task.Result is { } raw && raw.Length >= settings.DistillMinChars;
}
