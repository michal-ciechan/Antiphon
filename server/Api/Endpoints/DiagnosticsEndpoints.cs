using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class DiagnosticsEndpoints
{
    public static void MapDiagnosticsEndpoints(this WebApplication app)
    {
        var diagnostics = app.MapGroup("/api/diagnostics").WithTags("Diagnostics");

        diagnostics.MapPost("/bundle", async (
            BugReportRequest request,
            DiagnosticsBundleService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            var clientSha = http.Request.Headers[DiagnosticsBundleService.ClientShaHeader].FirstOrDefault();
            var stream = await service.BuildAsync(request, clientSha, ct);
            var name = $"antiphon-bug-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
            return Results.File(stream, "application/zip", name);
        });
    }
}
