using System.Globalization;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.HostLoad;

namespace Antiphon.Server.Api.Endpoints;

public static class HostLoadEndpoints
{
    public static void MapHostLoadEndpoints(this WebApplication app)
    {
        app.MapGet("/api/hosts/{hostId}/load", async (string hostId, HttpContext http,
            HostLoadHistoryService history, TimeProvider time, CancellationToken ct) =>
        {
            Guid? project = null; AgentTaskRole? role = null;
            if (http.Request.Query.TryGetValue("projectId", out var text))
            { if (!Guid.TryParse(text, out var id)) throw new ValidationException("projectId", "Invalid project id."); project = id; }
            if (http.Request.Query.TryGetValue("role", out text))
            {
                if (!Enum.TryParse<AgentTaskRole>(text, true, out var selected) || !Enum.IsDefined(selected)) throw new ValidationException("role", "Invalid task role.");
                role = selected;
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            return Results.Ok(await history.CurrentAsync(hostId, project, role, linked.Token));
        });
        app.MapGet("/api/hosts/{hostId}/load/history", async (string hostId, HttpContext http,
            HostLoadHistoryService history, TimeProvider time, CancellationToken ct) =>
        {
            string? Read(string name) => http.Request.Query.TryGetValue(name, out var value) && value.Count == 1
                ? value[0] : http.Request.Query.ContainsKey(name) ? throw new ValidationException(name, "A scalar is required.") : null;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            return Results.Ok(await history.HistoryAsync(hostId, Read("from"), Read("to"), Read("resolution"), Read("limit"), Read("cursor"), linked.Token));
        });
        app.MapGet("/api/agent-tasks/{id}/load", async (string id, HttpContext http, AgentTaskService tasks,
            HostLoadHistoryService history, TimeProvider time, CancellationToken ct) =>
        {
            var taskId = await tasks.ResolveTaskIdAsync(id, ct);
            var caller = await AgentTaskEndpoints.ResolvePollingCallerAsync(http, tasks, ct);
            await tasks.GetAsync(taskId, ct, caller?.SessionId);
            int? attempt = null;
            if (http.Request.Query.TryGetValue("attempt", out var value))
            { if (value.Count != 1 || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1) throw new ValidationException("attempt", "Invalid task attempt."); attempt = parsed; }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            return Results.Ok(await history.TaskAsync(taskId, attempt, linked.Token));
        });
    }
}
