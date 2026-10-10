using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

/// <summary>CARD-0822. The live file as text/markdown, and an operator-forced regenerate.</summary>
public static class OrchestratorInstructionsEndpoints
{
    public static void MapOrchestratorInstructionsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/orchestrator-instructions").WithTags("OrchestratorInstructions");

        group.MapGet("/", async (
            HttpContext http,
            OrchestratorInstructionsService service,
            IOptions<DelegationSettings> settings,
            CancellationToken ct) =>
        {
            if (!settings.Value.OrchestratorInstructions.Enabled)
                throw new OrchestratorInstructionsDisabledException();

            var publication = await service.ReadAsync(ct);
            if (publication is null)
                throw new NotFoundException("OrchestratorInstructions", OrchestratorInstructionsState.FleetId);

            http.Response.Headers.ETag = publication.Version;
            http.Response.Headers["X-Antiphon-Instructions-Version"] = publication.Version;
            return Results.Text(publication.FileText, "text/markdown; charset=utf-8");
        });

        group.MapPost("/refresh", async (
            HttpContext http,
            OrchestratorInstructionsService service,
            IOptions<DelegationSettings> settings,
            IOptions<PhoneHomeRunnerSettings> phoneHome,
            CancellationToken ct) =>
        {
            if (!settings.Value.OrchestratorInstructions.Enabled)
                throw new OrchestratorInstructionsDisabledException();

            OperatorCredential.Require(http, phoneHome.Value, "An operator token is required.");
            var version = await service.ReconcileNowAsync("operator", ct);
            return Results.Ok(new { version });
        });
    }
}
