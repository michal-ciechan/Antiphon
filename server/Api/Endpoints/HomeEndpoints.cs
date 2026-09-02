using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class HomeEndpoints
{
    /// <summary>
    /// The read-only home-rail projection over Cards and unbound AgentTasks (CARD-0002).
    /// Fleet-global and unfiltered: the client folds worktrees and filters by the selected
    /// project's directories. Bound tasks nest as a card's worker line, never as their own item.
    /// There is no question field — that text is <c>GET /api/attention</c>'s evidence.
    /// </summary>
    public static void MapHomeEndpoints(this WebApplication app)
    {
        var home = app.MapGroup("/api/home").WithTags("Home");

        home.MapGet("/tasks", async (
            HomeTaskService service,
            CancellationToken ct) => Results.Ok(await service.GetAsync(ct)));
    }
}
