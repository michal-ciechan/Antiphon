using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Owns exact caller-input identity and the private event-backed read.</summary>
public sealed class AgentTaskInputService(AppDbContext db)
{
    private const string Prefix = "task-input:";

    public static string ConversationKey(Guid taskId, Guid eventId) =>
        $"{Prefix}{taskId:D}:{eventId:D}";

    public static bool TryParseConversationKey(string? key, out Guid taskId, out Guid eventId)
    {
        taskId = eventId = Guid.Empty;
        if (key is null || key.Length != 84 || !key.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        return Guid.TryParseExact(key.AsSpan(11, 36), "D", out taskId)
            && Guid.TryParseExact(key.AsSpan(48, 36), "D", out eventId)
            && string.Equals(key, ConversationKey(taskId, eventId), StringComparison.Ordinal);
    }

    public static string Route(Guid taskId, Guid eventId) =>
        $"/api/agent-tasks/{taskId:D}/inputs/{eventId:D}";

    public async Task<string?> ReadAsync(
        Guid taskId, Guid eventId, AgentTaskService.Caller caller, CancellationToken ct)
    {
        // AuthenticateAsync may also return a standing-session or capability principal.
        // Neither may read a task's exact input, even if it can read ordinary task status.
        if (caller.Task is null || caller.Task.Id != taskId
            || caller.Task.AgentSessionId is not Guid recipient)
            throw new ForbiddenException("This task token cannot read that input.");

        var input = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.Id == eventId && e.AgentTaskId == taskId
                && e.InputBody != null
                && (e.Type == AgentTaskEventType.Refined || e.Type == AgentTaskEventType.Replied))
            .Select(e => new { e.AgentSessionId, e.InputBody })
            .FirstOrDefaultAsync(ct);
        if (input is null)
            return null;
        if (input.AgentSessionId != recipient)
            throw new ForbiddenException("This task session cannot read that input.");
        return input.InputBody;
    }
}
