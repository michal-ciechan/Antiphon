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
    }
}
