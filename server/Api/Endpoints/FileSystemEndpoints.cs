using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class FileSystemEndpoints
{
    public static void MapFileSystemEndpoints(this WebApplication app)
    {
        var fs = app.MapGroup("/api/filesystem")
            .WithTags("FileSystem");

        // Working-directory autocomplete. Empty path → drive roots; otherwise existence +
        // matching child directories for the typed prefix.
        // SECURITY: enumerates arbitrary directories on the host — intended for the
        // single-user localhost dev tool only. If Antiphon ever becomes multi-user, gate
        // this behind auth and restrict to a configured set of allowed roots.
        fs.MapGet("/browse", async (
            string? path,
            DirectoryBrowseService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.BrowseAsync(path, cancellationToken));
        });

        // Git identity for a set of directories (repeat ?path=a&path=b). Feeds the home
        // screen's project/workspace grouping; a non-repo directory comes back with
        // isGitRepository=false rather than erroring. Same localhost-only trust model
        // as /browse above.
        fs.MapGet("/workspaces", async (
            HttpContext http,
            WorkspaceInfoService service,
            CancellationToken cancellationToken) =>
        {
            var paths = http.Request.Query["path"]
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!.ToString())
                .ToList();
            return Results.Ok(await service.GetWorkspacesAsync(paths, cancellationToken));
        });

        // Every git worktree of the repo containing `path`, branch names included — the
        // workspace switcher's source for worktrees nobody has an agent in yet.
        fs.MapGet("/worktrees", async (
            string path,
            WorkspaceInfoService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetWorktreesAsync(path, cancellationToken));
        });
    }
}
