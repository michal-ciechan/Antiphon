using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

/// <summary>Recorded desktop representation only; no remote workspace or deletion authority.</summary>
internal static class DesktopWorkspaceCleanup
{
    internal const string NotRequiredReason = "desktop_not_required";

    internal static string? Reason(AgentTask task) => task.DesktopWorkspaceKind switch
    {
        DesktopWorkspaceKind.RefOnly => NotRequiredReason,
        DesktopWorkspaceKind.Checkout when task.TaskRefWorkspaceBindingId is null => null,
        DesktopWorkspaceKind.Checkout => "desktop_workspace_binding_contradictory",
        _ => "desktop_workspace_kind_invalid"
    };

    internal static async Task<WorktreeRemoval?> DecideAsync(AppDbContext db, AgentTask task, CancellationToken ct)
    {
        if (Reason(task) is not { } reason) return null;
        if (reason != NotRequiredReason) return new(false, false, false, reason);
        try
        {
            var context = await new TaskWorkspaceContextResolver(db).ResolveAsync(task, ct);
            return NotRequired(context.BindingId);
        }
        catch (ConflictException ex) { return new(false, false, false, ex.Code ?? "ref_only_binding_invalid"); }
    }

    internal static WorktreeRemoval NotRequired(Guid? binding) => binding is null || binding == Guid.Empty
        ? new(false, false, false, "ref_only_binding_required")
        : new(false, false, false, null)
        {
            DesktopCleanupDisposition = DesktopCleanupDisposition.NotRequired,
            DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly,
            TaskRefWorkspaceBindingId = binding
        };
}

