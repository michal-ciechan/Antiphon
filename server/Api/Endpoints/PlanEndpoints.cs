using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class PlanEndpoints
{
    /// <summary>
    /// The read-only projection over the plan files in the repo. There is deliberately no POST,
    /// PUT or DELETE here and there never will be: git is the store, agents keep writing markdown
    /// exactly as they always have, and a write path would make this a second one.
    /// </summary>
    /// <remarks>
    /// <c>?path=</c> is the repo root to look under, resolved by walking UP from what was given, so
    /// a worktree subdirectory works. Omitted, it resolves from the server's own location — the
    /// common case, where the operator means "this checkout".
    ///
    /// <para>Same localhost trust model as <see cref="FileSystemEndpoints"/>: the root is a host
    /// path a caller chooses. What is NOT left to the caller is which files come back — the content
    /// route resolves every requested name inside <c>docs/superpowers/specs</c> or
    /// <c>docs/features</c> and refuses anything else (422), so this cannot be turned into a
    /// read-any-file endpoint by editing a query string.</para>
    /// </remarks>
    public static void MapPlanEndpoints(this WebApplication app)
    {
        var plans = app.MapGroup("/api/plans").WithTags("Plans");

        plans.MapGet("/", async (
            string? path,
            PlanCatalogService service,
            CancellationToken ct) => Results.Ok(await service.ListAsync(path, ct)));

        plans.MapGet("/content", async (
            string? path,
            string file,
            string? @ref,
            PlanCatalogService service,
            CancellationToken ct) => Results.Ok(await service.ReadAsync(path, file, @ref, ct)));
    }
}
